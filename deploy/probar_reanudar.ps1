<#
.SYNOPSIS
    Prueba que DigLab reanuda un equipo liberado sin base de datos.

.DESCRIPTION
    Escribe un traspaso vigente y arranca DigLab en modo kiosco con el cierre de
    sesion simulado. DigLab debe reanudar sin login ni prueba de conexion,
    avisar, "cerrar" la sesion al vencer y volver al login. Al final el guion
    termina el proceso (en kiosco el login no se puede cerrar con Alt+F4).
    Durante un minuto veras el aviso y despues el login a pantalla completa.

.EXAMPLE
    .\probar_reanudar.ps1
    .\probar_reanudar.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$exe     = Join-Path $PSScriptRoot "..\DigLab\bin\$Configuration\DigLab.exe"
$carpeta = Join-Path $env:ProgramData 'DigLab'
$ruta    = Join-Path $carpeta 'sesion_activa.txt'
$log     = Join-Path $env:LOCALAPPDATA ('DigLab\logs\diglab-' + (Get-Date -Format 'yyyyMMdd') + '.log')

if (-not (Test-Path $exe)) {
    throw "No se encontro $exe. Compila primero (.\build.cmd -NoRun)."
}
$exe = (Resolve-Path $exe).Path

if (Get-Process -Name 'DigLab' -ErrorAction SilentlyContinue) {
    throw 'Ya hay un DigLab en ejecucion; cierralo antes de probar.'
}

# El mismo calculo que SesionActiva.ArranqueActual(): ahora - tiempo encendido.
$t = [BitConverter]::ToUInt32([BitConverter]::GetBytes([Environment]::TickCount), 0)
$arranque = (Get-Date).AddMilliseconds(-[double]$t)
$ahora = Get-Date
$fin = $ahora.AddSeconds(40)
New-Item -ItemType Directory -Force $carpeta | Out-Null
$lineas = @('version=1', 'usuario=20230001', 'codigo=AB12c', "equipo=$env:COMPUTERNAME",
    "sesionWindows=$((Get-Process -Id $PID).SessionId)",
    ('arranqueSistema=' + $arranque.ToString('o')), ('inicioLocal=' + $ahora.AddMinutes(-30).ToString('o')),
    ('finLocal=' + $fin.ToString('o')), ('escrito=' + $ahora.ToString('o')), ('latido=' + $ahora.ToString('o')))
[IO.File]::WriteAllText($ruta, (($lineas -join "`n") + "`n"), (New-Object Text.UTF8Encoding $false))

$desde = 0
if (Test-Path $log) { $desde = @(Get-Content $log).Count }

Write-Host "=== DigLab: reanudar en kiosco sin base, fin a las $($fin.ToString('HH:mm:ss')) ===" -ForegroundColor Cyan
$kioscoAntes = $env:DIGLAB_MODO_KIOSCO
$logoffAntes = $env:DIGLAB_SIMULAR_LOGOFF
$env:DIGLAB_MODO_KIOSCO = '1'
$env:DIGLAB_SIMULAR_LOGOFF = '1'
try {
    $p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
}
finally {
    $env:DIGLAB_MODO_KIOSCO = $kioscoAntes
    $env:DIGLAB_SIMULAR_LOGOFF = $logoffAntes
}
Start-Sleep -Seconds 52

$vivo = -not $p.HasExited
$titulo = ''
if ($vivo) { $titulo = (Get-Process -Id $p.Id).MainWindowTitle }
Write-Host "  proceso sigue vivo tras vencer: $vivo (ventana: '$titulo')"
if ($vivo) { Stop-Process -Id $p.Id -Force; Write-Host '  (DigLab terminado por el guion)' }

if (Test-Path $log) { Get-Content $log | Select-Object -Skip $desde | ForEach-Object { Write-Host "  $_" } }
$estado = 'borrado'
if (Test-Path $ruta) { $estado = 'EXISTE' }
Write-Host "  archivo de traspaso al final: $estado"
