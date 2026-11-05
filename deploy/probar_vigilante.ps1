<#
.SYNOPSIS
    Prueba el vigilante en modo consola y simulacion, sin base de datos y sin
    cerrar ninguna sesion de verdad.

.DESCRIPTION
    Escribe el archivo de traspaso (%ProgramData%\DigLab\sesion_activa.txt) a
    mano para seis escenarios y muestra lo que el vigilante registro en cada
    uno. Tarda unos cuatro minutos. No necesita permisos de administrador.

.EXAMPLE
    .\probar_vigilante.ps1                        # usa DigLab.Vigilante\bin\Debug
    .\probar_vigilante.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$exe     = Join-Path $PSScriptRoot "..\DigLab.Vigilante\bin\$Configuration\DigLab.Vigilante.exe"
$carpeta = Join-Path $env:ProgramData 'DigLab'
$ruta    = Join-Path $carpeta 'sesion_activa.txt'
$log     = Join-Path $carpeta ('logs\vigilante-' + (Get-Date -Format 'yyyyMMdd') + '.log')

if (-not (Test-Path $exe)) {
    throw "No se encontro $exe. Compila primero (.\build.cmd -NoRun)."
}
$exe = (Resolve-Path $exe).Path

# El mismo calculo que SesionActiva.ArranqueActual(): ahora - tiempo encendido.
function Arranque {
    $t = [BitConverter]::ToUInt32([BitConverter]::GetBytes([Environment]::TickCount), 0)
    return (Get-Date).AddMilliseconds(-[double]$t)
}

function Escribir([datetime]$fin, [datetime]$latido, [datetime]$arranque, [datetime]$inicio,
                  [string]$usuario = '20230001', [string]$codigo = 'AB12c') {
    New-Item -ItemType Directory -Force $carpeta | Out-Null
    $sesion = (Get-Process -Id $PID).SessionId
    $ahora = Get-Date
    $lineas = @('version=1', "usuario=$usuario", "codigo=$codigo", "equipo=$env:COMPUTERNAME", "sesionWindows=$sesion",
        ('arranqueSistema=' + $arranque.ToString('o')), ('inicioLocal=' + $inicio.ToString('o')),
        ('finLocal=' + $fin.ToString('o')), ('escrito=' + $ahora.ToString('o')), ('latido=' + $latido.ToString('o')))
    [IO.File]::WriteAllText($ruta, (($lineas -join "`n") + "`n"), (New-Object Text.UTF8Encoding $false))
}

function Correr([int]$segundos, [scriptblock]$durante) {
    $p = Start-Process -FilePath $exe -ArgumentList '--consola', '--simular', '--segundos', $segundos -PassThru -WindowStyle Hidden
    if ($durante) { & $durante }
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { Write-Host "  (codigo de salida $($p.ExitCode))" -ForegroundColor Red }
}

function Escenario([string]$nombre, [scriptblock]$cuerpo) {
    $desde = 0
    if (Test-Path $log) { $desde = @(Get-Content $log).Count }
    Write-Host "`n=== $nombre ===" -ForegroundColor Cyan
    Remove-Item $ruta -ErrorAction SilentlyContinue
    & $cuerpo
    if (Test-Path $log) { Get-Content $log | Select-Object -Skip $desde | ForEach-Object { Write-Host "  $_" } }
    $estado = 'borrado'
    if (Test-Path $ruta) { $estado = 'EXISTE' }
    Write-Host "  archivo de traspaso al final: $estado"
}

$inicio = (Get-Date).AddMinutes(-30)

Escenario 'A: vigente, fin en 25 s, DigLab ausente (latido viejo) -> aviso + cierre simulado' {
    Escribir -fin (Get-Date).AddSeconds(25) -latido (Get-Date).AddMinutes(-3) -arranque (Arranque) -inicio $inicio
    Correr 40
}

Escenario 'B: traspaso de otro arranque -> ignorado y borrado' {
    Escribir -fin (Get-Date).AddMinutes(30) -latido (Get-Date) -arranque (Get-Date).AddDays(-3) -inicio $inicio
    Correr 12
}

Escenario 'C: misma sesion, el archivo alarga la salida a +10 min -> se mantiene la original' {
    Escribir -fin (Get-Date).AddSeconds(30) -latido (Get-Date) -arranque (Arranque) -inicio $inicio
    Correr 45 { Start-Sleep 12; Escribir -fin (Get-Date).AddMinutes(10) -latido (Get-Date) -arranque (Arranque) -inicio $inicio }
}

Escenario 'D: misma sesion Windows, archivo reescrito con OTRO codigo y salida +10 min -> se mantiene la original' {
    Escribir -fin (Get-Date).AddSeconds(30) -latido (Get-Date) -arranque (Arranque) -inicio $inicio
    Correr 45 { Start-Sleep 12; Escribir -fin (Get-Date).AddMinutes(10) -latido (Get-Date) -arranque (Arranque) -inicio (Get-Date) -usuario '20230002' -codigo 'ZZ99x' }
}

Escenario 'E: el archivo desaparece antes de la hora -> se cierra igual con lo recordado' {
    Escribir -fin (Get-Date).AddSeconds(30) -latido (Get-Date) -arranque (Arranque) -inicio $inicio
    Correr 45 { Start-Sleep 12; Remove-Item $ruta }
}

Escenario 'F: el archivo ADELANTA la salida -> se acepta' {
    Escribir -fin (Get-Date).AddMinutes(10) -latido (Get-Date) -arranque (Arranque) -inicio $inicio
    Correr 30 { Start-Sleep 12; Escribir -fin (Get-Date).AddSeconds(8) -latido (Get-Date) -arranque (Arranque) -inicio $inicio }
}
