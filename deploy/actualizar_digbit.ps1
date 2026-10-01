<#
.SYNOPSIS
    Sustituye el DigBit ya instalado por el de este paquete, sin volver a
    configurar nada.

.DESCRIPTION
    Copiar el .exe a mano falla casi siempre: DigBit esta corriendo como shell
    de la cuenta del laboratorio y Windows tiene el archivo bloqueado, asi que
    la copia se rechaza y uno se queda probando el binario viejo sin saberlo.

    Este guion lo detiene primero, copia, y despues COMPRUEBA que el archivo de
    destino coincide con el de origen. Si no coincide, lo dice en rojo.

    No toca el registro, ni la cuenta, ni la base de datos. Solo los archivos.

    Ejecutar como administrador, desde la carpeta del paquete.

.EXAMPLE
    .\deploy\actualizar_digbit.ps1
    .\deploy\actualizar_digbit.ps1 -Destino D:\DigBit
#>
[CmdletBinding()]
param(
    [string]$Origen,
    [string]$Destino = 'C:\DigBit'
)

$ErrorActionPreference = 'Stop'

# Administrador hace falta para cerrar sesiones de Windows y parar el servicio,
# no para copiar archivos. Se exige donde se necesita y con el motivo delante,
# asi el guion tambien sirve para actualizar una copia suelta en otra carpeta.
function ExigirAdministrador([string]$porque) {
    $identidad = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not (New-Object Security.Principal.WindowsPrincipal $identidad).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Ejecuta este guion como administrador: $porque"
    }
}

# OJO: $PSScriptRoot llega VACIO dentro de los valores por defecto del bloque
# param cuando el guion se lanza con "powershell -File <ruta>", que es como lo
# llama el instalador. En el cuerpo si esta, asi que se resuelve aqui. Con & o
# dot-source funcionaba de las dos formas, y por eso no se vio antes.
if (-not $Origen) { $Origen = Join-Path $PSScriptRoot '..' }
$Origen  = [IO.Path]::GetFullPath($Origen)
$Destino = [IO.Path]::GetFullPath($Destino)

$origenDigBit    = Join-Path $Origen 'DigBit\bin\Release'
$origenVigilante = Join-Path $Origen 'DigBit.Vigilante\bin\Release'
if (-not (Test-Path (Join-Path $origenDigBit 'DigBit.exe'))) {
    throw "No encuentro DigBit.exe en $origenDigBit."
}
if (-not (Test-Path $Destino)) {
    throw "No hay nada instalado en $Destino. Usa deploy\configurar_equipo.ps1 la primera vez."
}

Write-Host ''
Write-Host '== 1. Detener DigBit y el vigilante' -ForegroundColor Cyan

# Matar el proceso NO basta. En un equipo configurado, DigBit es el shell de la
# cuenta del laboratorio y Winlogon tiene AutoRestartShell=1, asi que lo relanza
# en cuanto muere y el archivo vuelve a quedar bloqueado antes de que de tiempo a
# copiarlo. Hay que cerrar esa sesion de Windows entera.
$miSesion = (Get-Process -Id $PID).SessionId
$ajenas = @(Get-Process -Name 'DigBit' -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -ne $miSesion } |
            Select-Object -ExpandProperty SessionId -Unique)

if ($ajenas.Count -gt 0) {
    ExigirAdministrador 'hay que cerrar la sesion de Windows donde DigBit corre como shell.'
    foreach ($id in $ajenas) {
        Write-Host ("   cerrando la sesion de Windows {0}: ahi DigBit corre como shell" -f $id)
        & logoff.exe $id 2>&1 | Out-Null
    }
    Start-Sleep -Seconds 6
}

$servicio = Get-Service -Name 'DigBitVigilante' -ErrorAction SilentlyContinue
if ($servicio -and $servicio.Status -ne 'Stopped') {
    ExigirAdministrador 'hay que detener el servicio DigBitVigilante.'
    Stop-Service -Name 'DigBitVigilante' -Force
    Write-Host '   servicio DigBitVigilante detenido'
}

$vivos = @(Get-Process -Name 'DigBit', 'DigBit.Vigilante' -ErrorAction SilentlyContinue)
if ($vivos.Count -gt 0) {
    foreach ($p in $vivos) { Write-Host ("   deteniendo {0} (pid {1}, sesion {2})" -f $p.ProcessName, $p.Id, $p.SessionId) }
    $vivos | Stop-Process -Force
    Start-Sleep -Seconds 3
} elseif ($ajenas.Count -eq 0) {
    Write-Host '   no habia ninguno corriendo'
}

# Si resucito, es que su sesion sigue abierta: avisar en vez de copiar en balde.
$resucitado = @(Get-Process -Name 'DigBit' -ErrorAction SilentlyContinue)
if ($resucitado.Count -gt 0) {
    foreach ($p in $resucitado) { Write-Host ("   sigue vivo: pid {0} en la sesion {1}" -f $p.Id, $p.SessionId) -ForegroundColor Red }
    throw @'
DigBit vuelve a arrancar solo, asi que su archivo sigue bloqueado y la copia no
serviria de nada. Es AutoRestartShell: Winlogon relanza el shell de esa cuenta.
Cierra su sesion de Windows a mano y repite:
    query session
    logoff <ID de la sesion de laboratorio>
O reinicia manteniendo Mayus pulsada, entra con tu cuenta, y ejecuta esto antes
de que la cuenta del laboratorio inicie sesion.
'@
}

Write-Host ''
Write-Host '== 2. Guardar la configuracion del equipo' -ForegroundColor Cyan

# La carpeta de compilacion trae un connections.config de PLANTILLA (con
# "Server=SERVIDOR") y un DigBit.exe.config con ModoKiosco=false y Laboratorio
# vacio. Copiarlos encima deja el equipo sin base de datos y sin kiosco, que es
# justo lo que paso la primera vez que se uso este guion. Esos dos archivos los
# escribe configurar_equipo.ps1 y son de la MAQUINA, no de la compilacion.
$rutaConexion = Join-Path $Destino 'connections.config'
$rutaConfig   = Join-Path $Destino 'DigBit.exe.config'

$conexionGuardada = $null
if (Test-Path $rutaConexion) {
    $conexionGuardada = Get-Content -Raw $rutaConexion
    $cadena = ([xml]$conexionGuardada).connectionStrings.add | Where-Object { $_.name -eq 'DigBit' }
    if ($cadena) {
        # Tambien entre comillas: el instalador las pone si la contrasena lleva ';'.
        $visible = $cadena.connectionString -replace '(?i)(password\s*=\s*)("(?:[^"]|"")*"|''(?:[^'']|'''')*''|[^;]*)', '$1***'
        Write-Host ("   connections.config: " + $visible)
    }
} else {
    Write-Warning "   No habia connections.config en $Destino."
}

$ajustes = @{}
if (Test-Path $rutaConfig) {
    foreach ($clave in @('ModoKiosco', 'Laboratorio')) {
        $nodo = ([xml](Get-Content -Raw $rutaConfig)).configuration.appSettings.add | Where-Object { $_.key -eq $clave }
        if ($nodo) {
            $ajustes[$clave] = $nodo.value
            Write-Host ("   {0} = '{1}'" -f $clave, $nodo.value)
        }
    }
} else {
    Write-Warning "   No habia DigBit.exe.config en $Destino."
}

Write-Host ''
Write-Host "== 3. Copiar a $Destino" -ForegroundColor Cyan
Copy-Item (Join-Path $origenDigBit '*') $Destino -Recurse -Force
Write-Host '   DigBit copiado'

# Devolver lo del equipo. El DigBit.exe.config NUEVO se conserva (puede traer
# claves que antes no existian) y solo se le reponen los valores desplegados.
if ($conexionGuardada) {
    Set-Content -Path $rutaConexion -Value $conexionGuardada -Encoding UTF8 -NoNewline
    Write-Host '   connections.config devuelto'
}
if ($ajustes.Count -gt 0 -and (Test-Path $rutaConfig)) {
    [xml]$xml = Get-Content -Raw $rutaConfig
    foreach ($clave in $ajustes.Keys) {
        $nodo = $xml.configuration.appSettings.add | Where-Object { $_.key -eq $clave }
        if (-not $nodo) {
            $nodo = $xml.CreateElement('add')
            $nodo.SetAttribute('key', $clave)
            [void]$xml.configuration.appSettings.AppendChild($nodo)
        }
        $nodo.SetAttribute('value', $ajustes[$clave])
    }
    $xml.Save($rutaConfig)
    Write-Host ("   DigBit.exe.config: ModoKiosco y Laboratorio repuestos")
}
if (Test-Path (Join-Path $origenVigilante '*.exe')) {
    $carpetaVigilante = Join-Path $Destino 'Vigilante'
    New-Item -ItemType Directory -Force $carpetaVigilante | Out-Null
    Copy-Item (Join-Path $origenVigilante '*') $carpetaVigilante -Recurse -Force
    Write-Host '   vigilante copiado'
}

Write-Host ''
Write-Host '== 4. Comprobar que de verdad se sustituyo' -ForegroundColor Cyan
$de = Get-Item (Join-Path $origenDigBit 'DigBit.exe')
$a  = Get-Item (Join-Path $Destino 'DigBit.exe')
Write-Host ("   origen:  {0,10:N0} bytes   {1}" -f $de.Length, $de.LastWriteTime)
Write-Host ("   destino: {0,10:N0} bytes   {1}" -f $a.Length, $a.LastWriteTime)
$hashDe = (Get-FileHash $de.FullName -Algorithm SHA256).Hash
$hashA  = (Get-FileHash $a.FullName  -Algorithm SHA256).Hash
if ($hashDe -eq $hashA) {
    Write-Host '   coinciden: el ejecutable quedo actualizado' -ForegroundColor Green
} else {
    Write-Host '   NO COINCIDEN: la copia no se aplico' -ForegroundColor Red
    Write-Host '   Cierra cualquier DigBit abierto (incluida otra sesion de Windows) y repite.' -ForegroundColor Red
    throw 'La actualizacion no se completo.'
}

Write-Host ''
Write-Host '== 5. Volver a arrancar el vigilante' -ForegroundColor Cyan
if ($servicio) {
    Start-Service -Name 'DigBitVigilante'
    Write-Host ("   " + (Get-Service -Name 'DigBitVigilante').Status)
} else {
    Write-Host '   el servicio no esta instalado; nada que arrancar'
}

Write-Host ''
Write-Host 'Listo. Reinicia la maquina para probar con el DigBit nuevo.' -ForegroundColor Green
