<#
.SYNOPSIS
    Arma el paquete distribuible de DigBit: el instalador con todo lo que
    necesita al lado, listo para copiar a una memoria USB.

.DESCRIPTION
    DigBit.Instalador.exe no lleva nada dentro: por diseno ejecuta los guiones
    de deploy\, que son los mismos del camino por consola. Por eso el entregable
    no es un .exe suelto sino una carpeta con esta forma, la que esperan
    Guiones.RaizPaquete() y el parametro -Origen de configurar_equipo.ps1:

        DigBit.Instalador.exe
        deploy\
        db\
        DigBit\bin\Release\
        DigBit.Vigilante\bin\Release\

    Si falta cualquiera de esas carpetas el instalador arranca igual y falla a
    la mitad, que es justo lo que se quiere evitar.

.EXAMPLE
    .\empaquetar.ps1                 # compila Release y arma el paquete + .zip
    .\empaquetar.ps1 -SinCompilar    # usa la compilacion Release que ya haya
    .\empaquetar.ps1 -SinZip         # deja solo la carpeta
#>
[CmdletBinding()]
param(
    [string]$Destino,
    [switch]$SinCompilar,
    [switch]$SinZip
)

$ErrorActionPreference = 'Stop'

if (-not $Destino) {
    $Destino = Join-Path (Split-Path $PSScriptRoot -Parent) 'DigBit-Paquete'
}

# --- Compilar ----------------------------------------------------------------
if (-not $SinCompilar) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -NoRun
}

# --- Comprobar que hay algo que empaquetar -----------------------------------
# Se valida antes de borrar el destino: si la compilacion no esta, es mejor
# terminar aqui que dejar al usuario sin el paquete anterior y sin uno nuevo.
$instalador = Join-Path $PSScriptRoot 'DigBit.Instalador\bin\Release\DigBit.Instalador.exe'
$appDigBit  = Join-Path $PSScriptRoot 'DigBit\bin\Release'
$vigilante  = Join-Path $PSScriptRoot 'DigBit.Vigilante\bin\Release'

foreach ($ruta in @($instalador, $appDigBit, $vigilante)) {
    if (-not (Test-Path $ruta)) {
        throw "Falta $ruta. Ejecuta .\build.cmd -Configuration Release -NoRun antes, o quita -SinCompilar."
    }
}

# --- Armar -------------------------------------------------------------------
# servidor.config no sale de este guion: lo escribe el instalador al preparar el
# servidor, y es lo que hace que los demas equipos no tengan que teclear ninguna
# IP. Rehacer el paquete encima lo borraria, y volveriamos a preguntar la
# direccion en cada maquina, que es justo lo que se quito.
$rutaServidor = Join-Path $Destino 'servidor.config'
$servidorPrevio = $null
if (Test-Path $rutaServidor) {
    $servidorPrevio = Get-Content -Raw $rutaServidor
}

# La carpeta mysql\ (binarios portatiles, ~94 MB) tampoco sale de aqui: no se
# versiona y preparar_bd.ps1 la busca junto al paquete para montar el servidor.
# Borrar el destino entero la perdia; se aparta y se devuelve.
$rutaMysql = Join-Path $Destino 'mysql'
$mysqlApartado = "$Destino.mysql-apartado"
if (Test-Path $rutaMysql) {
    if (Test-Path $mysqlApartado) { throw "Ya existe $mysqlApartado (de un empaquetado cortado). Devuelvela a $rutaMysql o borrala y repite." }
    Move-Item $rutaMysql $mysqlApartado
}

if (Test-Path $Destino) {
    Remove-Item $Destino -Recurse -Force
}
New-Item -ItemType Directory -Path $Destino | Out-Null

if ($servidorPrevio) {
    [IO.File]::WriteAllText($rutaServidor, $servidorPrevio, (New-Object Text.UTF8Encoding $false))
    Write-Host 'Conservado el servidor.config del paquete anterior.' -ForegroundColor DarkGray
}

if (Test-Path $mysqlApartado) {
    Move-Item $mysqlApartado $rutaMysql
    Write-Host 'Conservada la carpeta mysql\ del paquete anterior.' -ForegroundColor DarkGray
}

Write-Host "Armando el paquete en $Destino..." -ForegroundColor Cyan

# El instalador va suelto en la raiz: es lo primero que tiene que ver quien abra
# la carpeta, y RaizPaquete() busca deploy\ junto al ejecutable.
Copy-Item (Join-Path (Split-Path $instalador) '*') $Destino -Recurse -Force

# Las carpetas conservan su ruta relativa porque configurar_equipo.ps1 las
# compone a mano sobre -Origen ('DigBit\bin\Release', etc.).
$carpetas = @{
    'deploy'                       = Join-Path $PSScriptRoot 'deploy'
    'db'                           = Join-Path $PSScriptRoot 'db'
    'DigBit\bin\Release'           = $appDigBit
    'DigBit.Vigilante\bin\Release' = $vigilante
}

foreach ($relativa in $carpetas.Keys) {
    $destinoCarpeta = Join-Path $Destino $relativa
    New-Item -ItemType Directory -Path $destinoCarpeta -Force | Out-Null
    Copy-Item (Join-Path $carpetas[$relativa] '*') $destinoCarpeta -Recurse -Force
}

# Los .pdb solo sirven para depurar y pesan; fuera del paquete que se reparte.
Get-ChildItem $Destino -Recurse -Filter '*.pdb' | Remove-Item -Force

# El connections.config de la compilacion es el del equipo donde se compila y
# puede llevar la contrasena real de la base. En el paquete va la PLANTILLA: es
# lo que esperan configurar_equipo.ps1 y actualizar_digbit.ps1, que escriben la
# conexion buena en cada equipo; asi la contrasena no viaja en la memoria USB.
Copy-Item (Join-Path $PSScriptRoot 'DigBit\connections.config.example') (Join-Path $Destino 'DigBit\bin\Release\connections.config') -Force

foreach ($doc in @('INSTALAR.md', 'README.md')) {
    Copy-Item (Join-Path $PSScriptRoot $doc) $Destino -Force
}

# --- Comprobacion final ------------------------------------------------------
# La misma que hace el instalador al arrancar, hecha aqui para que un paquete
# mal armado se detecte ahora y no delante del equipo que se va a configurar.
$imprescindibles = @(
    'DigBit.Instalador.exe',
    'deploy\configurar_equipo.ps1',
    'deploy\preparar_bd.ps1',
    'DigBit\bin\Release\DigBit.exe',
    'DigBit.Vigilante\bin\Release\DigBit.Vigilante.exe'
)
foreach ($archivo in $imprescindibles) {
    if (-not (Test-Path (Join-Path $Destino $archivo))) {
        throw "El paquete quedo incompleto: falta $archivo."
    }
}

$tamano = [math]::Round(((Get-ChildItem $Destino -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Paquete listo ($tamano MB) -> $Destino" -ForegroundColor Green

# --- Comprimir ---------------------------------------------------------------
if (-not $SinZip) {
    $zip = "$Destino.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $Destino '*') -DestinationPath $zip
    $tamanoZip = [math]::Round(((Get-Item $zip).Length / 1MB), 1)
    Write-Host "Comprimido ($tamanoZip MB) -> $zip" -ForegroundColor Green
}
