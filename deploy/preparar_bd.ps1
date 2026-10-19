<#
.SYNOPSIS
    Deja una base MySQL lista DENTRO de esta maquina, con el esquema y los datos
    de prueba de DigBit, y la registra como servicio de Windows.

.DESCRIPTION
    Pensado para la maquina virtual de pruebas: DigBit va a ser el shell, asi que
    no habra escritorio desde donde arrancar nada a mano. Por eso MySQL tiene que
    quedar como servicio automatico.

    Escucha solo en 127.0.0.1: la base no sale de esta maquina.

    Ejecutar como administrador, ANTES de configurar_equipo.ps1.

.EXAMPLE
    .\preparar_bd.ps1
    .\preparar_bd.ps1 -Destino D:\DigBitDB -Puerto 3306
    .\preparar_bd.ps1 -Quitar
#>
[CmdletBinding()]
param(
    [string]$Destino = 'C:\DigBitDB',
    [int]$Puerto = 3306,
    [string]$Servicio = 'DigBitMySQL',

    # Solo para probar el guion fuera de la virtual: no instala servicio, deja el
    # servidor en primer plano hasta que lo pares.
    [switch]$SinServicio,

    # Borra el destino y empieza de cero. Util si un intento anterior se quedo
    # a medias.
    [switch]$Rehacer,

    [switch]$Quitar
)

$ErrorActionPreference = 'Stop'

function Paso([string]$texto) {
    Write-Host ''
    Write-Host "== $texto" -ForegroundColor Cyan
}

$identidad = [Security.Principal.WindowsIdentity]::GetCurrent()
$esAdmin = (New-Object Security.Principal.WindowsPrincipal $identidad).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin -and -not $SinServicio) {
    throw 'Ejecuta este guion como administrador.'
}

$Destino = [IO.Path]::GetFullPath($Destino)
$datos   = Join-Path $Destino 'data'
$ini     = Join-Path $Destino 'my.ini'
$mysqld  = Join-Path $Destino 'bin\mysqld.exe'
$mysql   = Join-Path $Destino 'bin\mysql.exe'

# --- Quitar -------------------------------------------------------------------
if ($Quitar) {
    Paso "Quitar el servicio $Servicio"
    if (Get-Service -Name $Servicio -ErrorAction SilentlyContinue) {
        Stop-Service -Name $Servicio -Force -ErrorAction SilentlyContinue
        & sc.exe delete $Servicio | Out-Null
        Write-Host '   servicio borrado'
    } else {
        Write-Host '   no estaba instalado'
    }
    Write-Host "   los archivos siguen en $Destino; borralos a mano si quieres."
    return
}

# El esquema y los datos (db\) se buscan como mysql\: junto al guion o un nivel
# por encima. Y ANTES de tocar nada: se miraba solo junto al guion, y desde
# deploy\ del paquete fallaba en el paso 5, con el servicio ya instalado y la
# base vacia.
$db = $null
foreach ($candidata in @((Join-Path $PSScriptRoot 'db'), (Join-Path $PSScriptRoot '..\db'))) {
    if (Test-Path (Join-Path $candidata '01_schema.sql')) { $db = [IO.Path]::GetFullPath($candidata); break }
}
if (-not $db) {
    throw "No encuentro la carpeta 'db' (01_schema.sql y 02_seed.sql) junto a este guion ni un nivel por encima."
}

# --- 1. Archivos ---------------------------------------------------------------
Paso "1. Copiar MySQL a $Destino"
# En el paquete este guion esta junto a la carpeta 'mysql'; en el repositorio
# vive en deploy\, un nivel por debajo. Se miran los dos sitios para que valga
# igual en los dos sitios.
$origen = $null
foreach ($candidata in @((Join-Path $PSScriptRoot 'mysql'), (Join-Path $PSScriptRoot '..\mysql'))) {
    if (Test-Path (Join-Path $candidata 'bin\mysqld.exe')) { $origen = [IO.Path]::GetFullPath($candidata); break }
}
if (-not $origen) {
    throw "No encuentro mysqld.exe. Este guion necesita la carpeta 'mysql' (los binarios portatiles de MySQL) a su lado o un nivel por encima. Vienen en el paquete DigBit-VM.zip; no se versionan porque son 94 MB."
}
if ($Rehacer -and (Test-Path $Destino)) {
    if (Get-Service -Name $Servicio -ErrorAction SilentlyContinue) {
        Stop-Service -Name $Servicio -Force -ErrorAction SilentlyContinue
        & sc.exe delete $Servicio | Out-Null
        Start-Sleep -Seconds 2
    }
    Get-Process mysqld -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Destino*" } | Stop-Process -Force
    Remove-Item -Recurse -Force $Destino
    Write-Host '   destino anterior borrado (-Rehacer)'
}
if (Test-Path $datos) {
    throw "Ya hay una carpeta de datos en $datos. Para empezar de cero vuelve a ejecutar con -Rehacer."
}
New-Item -ItemType Directory -Force $Destino | Out-Null
Copy-Item (Join-Path $origen '*') $Destino -Recurse -Force
Write-Host "   copiado"

# --- 2. Configuracion ----------------------------------------------------------
Paso '2. Escribir my.ini'
# Barras normales a proposito: en un my.ini la barra invertida es un escape, asi
# que una ruta como ...\scratchpad\... pierde la "s" y el servidor no arranca.
# MySQL acepta '/' en Windows sin problema.
$base = $Destino.Replace('\', '/')
$rutaIni = @"
[mysqld]
basedir=$base
datadir=$base/data
port=$Puerto
# Solo esta maquina: la base no se expone a la red.
bind-address=127.0.0.1
skip-log-bin
default-time-zone=SYSTEM
log-error=$base/error.log
"@
Set-Content -Path $ini -Value $rutaIni -Encoding ASCII
Write-Host "   $ini"

# --- 3. Inicializar ------------------------------------------------------------
Paso '3. Inicializar la base (root sin contrasena, solo local)'
& $mysqld "--defaults-file=$ini" --initialize-insecure
if ($LASTEXITCODE -ne 0) {
    # 0xC0000135 = no encuentra una DLL. En un Windows recien instalado casi
    # siempre es el runtime de Visual C++, que MySQL necesita. El paquete lleva
    # MSVCP140.dll, VCRUNTIME140.dll y VCRUNTIME140_1.dll junto a mysqld.exe,
    # asi que si sale esto es que falta alguna otra.
    if ($LASTEXITCODE -eq -1073741515) {
        $hay = @('MSVCP140.dll', 'VCRUNTIME140.dll', 'VCRUNTIME140_1.dll') |
               Where-Object { -not (Test-Path (Join-Path $Destino "bin\$_")) }
        $falta = if ($hay) { "Faltan en bin\: $($hay -join ', ')." } else { 'Las tres del paquete estan.' }
        throw @"
mysqld no arranco porque le falta una DLL (codigo 0xC0000135).
$falta
Instala el 'Microsoft Visual C++ Redistributable (x64)' en esta maquina y vuelve
a ejecutar con -Rehacer:
  https://aka.ms/vs/17/release/vc_redist.x64.exe
"@
    }
    throw "mysqld --initialize-insecure fallo con codigo $LASTEXITCODE. Mira $Destino\error.log."
}
Write-Host '   inicializada'

# --- 4. Arrancar ---------------------------------------------------------------
if ($SinServicio) {
    Paso '4. Arrancar en primer plano (modo prueba)'
    $proceso = Start-Process -FilePath $mysqld -ArgumentList "--defaults-file=$ini", '--console' -PassThru
} else {
    Paso "4. Instalar y arrancar el servicio $Servicio"
    if (Get-Service -Name $Servicio -ErrorAction SilentlyContinue) {
        Stop-Service -Name $Servicio -Force -ErrorAction SilentlyContinue
        & sc.exe delete $Servicio | Out-Null
        Start-Sleep -Seconds 2
    }
    & $mysqld "--install" $Servicio "--defaults-file=$ini"
    if ($LASTEXITCODE -ne 0) { throw "mysqld --install fallo con codigo $LASTEXITCODE." }
    & sc.exe config $Servicio start= auto | Out-Null
    Start-Service -Name $Servicio
    Write-Host '   servicio arrancado y en automatico (sobrevive al reinicio)'
}

# Esperar a que acepte conexiones.
$listo = $false
foreach ($intento in 1..30) {
    & $mysql '-h' '127.0.0.1' '-P' $Puerto '-u' 'root' '-N' '-B' '-e' 'SELECT 1' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $listo = $true; break }
    Start-Sleep -Seconds 1
}
if (-not $listo) { throw "MySQL no acepta conexiones en el puerto $Puerto. Mira $Destino\error.log." }
Write-Host '   acepta conexiones'

# --- 5. Esquema y datos --------------------------------------------------------
Paso '5. Cargar el esquema y los datos de prueba'
foreach ($archivo in @('01_schema.sql', '02_seed.sql')) {
    $ruta = Join-Path $db $archivo
    if (-not (Test-Path $ruta)) { throw "Falta $ruta." }
    Get-Content -Raw $ruta | & $mysql '-h' '127.0.0.1' '-P' $Puerto '-u' 'root'
    if ($LASTEXITCODE -ne 0) { throw "Fallo al cargar $archivo." }
    Write-Host "   $archivo cargado"
}

$cuenta = (& $mysql '-h' '127.0.0.1' '-P' $Puerto '-u' 'root' '-N' '-B' '-e' 'SELECT COUNT(*) FROM usuarios' 'teschi_otru')
Write-Host "   $cuenta cuentas en la base"

# --- Resumen -------------------------------------------------------------------
$cadena = "Database=teschi_otru;Server=127.0.0.1;Port=$Puerto;User Id=root;Password="
Write-Host ''
Write-Host 'Base lista.' -ForegroundColor Green
Write-Host "  Cadena de conexion para DigBit:"
Write-Host "    $cadena" -ForegroundColor Yellow
if ($SinServicio) {
    Write-Host "  Servidor en primer plano, pid $($proceso.Id). Paralo con: Stop-Process -Id $($proceso.Id)"
} else {
    Write-Host "  Servicio '$Servicio' en automatico. Para quitarlo: .\preparar_bd.ps1 -Quitar"
}
