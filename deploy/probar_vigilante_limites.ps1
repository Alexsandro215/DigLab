<#
.SYNOPSIS
    Le da al vigilante entradas TORCIDAS y comprueba que hace algo sensato y,
    sobre todo, que no se muere.

.DESCRIPTION
    probar_vigilante.ps1 cubre los seis casos normales y se lee a ojo. Esto
    cubre los raros y se comprueba solo: cada caso declara que espera encontrar
    en el registro, que NO debe aparecer, y como tiene que quedar el archivo de
    traspaso.

    Por que importa: el vigilante es lo unico del sistema que destruye trabajo
    ajeno, porque cierra la sesion de Windows de una persona. Y si una entrada
    rara lo mata, Windows lo reinicia unas cuantas veces y se rinde; a partir de
    ahi el equipo se queda SIN NADIE que cierre sesiones, en silencio.

    Todo corre con --simular: no se cierra ninguna sesion de verdad y no hace
    falta ser administrador. Tarda unos cuatro minutos.

.EXAMPLE
    .\probar_vigilante_limites.ps1
    .\probar_vigilante_limites.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$exe = Join-Path $PSScriptRoot "..\DigLab.Vigilante\bin\$Configuration\DigLab.Vigilante.exe"
if (-not (Test-Path $exe)) { throw "No se encontro $exe. Compila primero (.\build.cmd -NoRun)." }
$exe = (Resolve-Path $exe).Path

# Sin carpeta de datos configurada, para que el registro caiga donde lo espera
# este guion. Si estuviera puesta, el vigilante escribiria en el ThawSpace.
$env:DIGLAB_CARPETA_DATOS = ''

$carpeta = Join-Path $env:ProgramData 'DigLab'
$ruta    = Join-Path $carpeta 'sesion_activa.txt'
$log     = Join-Path $carpeta ('logs\vigilante-' + (Get-Date -Format 'yyyyMMdd') + '.log')
$sesionWindows = (Get-Process -Id $PID).SessionId

# El mismo calculo que SesionActiva.ArranqueActual(): ahora - tiempo encendido.
function Arranque {
    $t = [BitConverter]::ToUInt32([BitConverter]::GetBytes([Environment]::TickCount), 0)
    return (Get-Date).AddMilliseconds(-[double]$t)
}

function Borrar {
    if (Test-Path $ruta) {
        Set-ItemProperty -Path $ruta -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
        Remove-Item $ruta -Force -ErrorAction SilentlyContinue
    }
}

function EscribirCrudo([string]$texto) {
    New-Item -ItemType Directory -Force $carpeta | Out-Null
    [IO.File]::WriteAllText($ruta, $texto, (New-Object Text.UTF8Encoding $false))
}

function Escribir {
    param(
        [datetime]$Fin,
        [datetime]$Inicio = (Get-Date).AddMinutes(-30),
        [datetime]$Latido = (Get-Date).AddMinutes(-10),   # DigLab ausente: el vigilante avisa el
        [datetime]$ArranqueSistema = (Arranque),
        [int]$Sesion = $sesionWindows,
        [string]$Usuario = '20230001',
        [string]$Codigo = 'AB12c'
    )
    $lineas = @(
        'version=1', "usuario=$Usuario", "codigo=$Codigo", "equipo=$env:COMPUTERNAME",
        "sesionWindows=$Sesion",
        ('arranqueSistema=' + $ArranqueSistema.ToString('o')),
        ('inicioLocal=' + $Inicio.ToString('o')),
        ('finLocal=' + $Fin.ToString('o')),
        ('escrito=' + (Get-Date).ToString('o')),
        ('latido=' + $Latido.ToString('o')))
    EscribirCrudo (($lineas -join "`n") + "`n")
}

$total = 0; $fallos = 0

function Caso {
    param(
        [string]$Nombre,
        [scriptblock]$Preparar,
        [int]$Segundos = 15,
        [string[]]$Espera = @(),
        [string[]]$Prohibe = @(),
        # 'texto' => numero de veces que tiene que aparecer, exactamente.
        [hashtable]$Veces = @{},
        # 'existe', 'borrado' o 'cualquiera'
        [string]$ArchivoAlFinal = 'cualquiera',
        [scriptblock]$Durante
    )

    $script:total++
    Write-Host ''
    Write-Host "== $Nombre" -ForegroundColor Cyan

    Borrar
    & $Preparar

    $desde = 0
    if (Test-Path $log) { $desde = @(Get-Content $log).Count }

    $p = Start-Process -FilePath $exe -ArgumentList '--consola', '--simular', '--segundos', $Segundos `
                       -PassThru -WindowStyle Hidden
    if ($Durante) { & $Durante }
    $p.WaitForExit()

    $lineas = @()
    if (Test-Path $log) { $lineas = @(Get-Content $log | Select-Object -Skip $desde) }
    $texto = $lineas -join "`n"

    $problemas = @()

    # Lo primero y mas importante: que no se haya muerto.
    if ($p.ExitCode -ne 0) { $problemas += "el vigilante termino con codigo $($p.ExitCode)" }
    $errores = @($lineas | Where-Object { $_ -match '\[ERROR\]' })
    if ($errores.Count -gt 0) { $problemas += "escribio $($errores.Count) linea(s) de ERROR" }

    foreach ($e in $Espera)  { if ($texto -notmatch [regex]::Escape($e)) { $problemas += "falta en el registro: '$e'" } }
    foreach ($p2 in $Prohibe) { if ($texto -match [regex]::Escape($p2)) { $problemas += "NO deberia aparecer: '$p2'" } }
    foreach ($k in $Veces.Keys) {
        $cuantas = @([regex]::Matches($texto, [regex]::Escape($k))).Count
        if ($cuantas -ne $Veces[$k]) { $problemas += "'$k' aparece $cuantas vez(ces) y se esperaban $($Veces[$k])" }
    }

    $estado = if (Test-Path $ruta) { 'existe' } else { 'borrado' }
    if ($ArchivoAlFinal -ne 'cualquiera' -and $estado -ne $ArchivoAlFinal) {
        $problemas += "el traspaso quedo '$estado' y se esperaba '$ArchivoAlFinal'"
    }

    foreach ($l in $lineas) { Write-Host "     $l" -ForegroundColor DarkGray }

    if ($problemas.Count -eq 0) {
        Write-Host "  [OK ] traspaso $estado" -ForegroundColor Green
    } else {
        $script:fallos++
        foreach ($x in $problemas) { Write-Host "  [FALLO] $x" -ForegroundColor Red }
    }
}

Write-Host "Vigilante: $exe" -ForegroundColor DarkGray
Write-Host "Traspaso:  $ruta" -ForegroundColor DarkGray
Write-Host "Sesion de Windows de esta consola: $sesionWindows" -ForegroundColor DarkGray

# --- Horas imposibles -----------------------------------------------------------
Caso -Nombre 'G1: la hora de salida paso hace 2 horas' `
     -Preparar { Escribir -Fin (Get-Date).AddHours(-2) } `
     -Espera @('Traspaso ignorado (Caducada)') `
     -Prohibe @('se cerraria') `
     -ArchivoAlFinal 'borrado'

Caso -Nombre 'G2: la hora de salida paso hace 3 min (dentro de la tolerancia de 10)' `
     -Preparar { Escribir -Fin (Get-Date).AddMinutes(-3) } `
     -Espera @('Sesion liberada', 'se cerraria')

Caso -Nombre 'H: la salida es ANTERIOR a la entrada (franja invertida)' `
     -Preparar { Escribir -Inicio (Get-Date).AddHours(1) -Fin (Get-Date).AddMinutes(4) } `
     -Espera @('Sesion liberada')

Caso -Nombre 'I: la clase cruza la medianoche (entra 23:30, sale 00:30)' `
     -Preparar {
        $medianoche = (Get-Date).Date.AddDays(1)
        Escribir -Inicio $medianoche.AddMinutes(-30) -Fin $medianoche.AddMinutes(30)
     } `
     -Espera @('Sesion liberada') `
     -Prohibe @('se cerraria')

Caso -Nombre 'L: la hora de salida es dentro de diez anos' `
     -Preparar { Escribir -Fin (Get-Date).AddYears(10) } `
     -Espera @('Sesion liberada') `
     -Prohibe @('se cerraria', 'se avisaria')

# --- Archivos rotos ---------------------------------------------------------------
Caso -Nombre 'J: el archivo esta cortado a media linea' `
     -Preparar { EscribirCrudo "version=1`nusuario=20230001`ncodigo=AB1" } `
     -Prohibe @('se cerraria')

Caso -Nombre 'K1: el archivo esta vacio' `
     -Preparar { EscribirCrudo '' } `
     -Prohibe @('se cerraria')

Caso -Nombre 'K2: la fecha de salida no se puede leer' `
     -Preparar {
        EscribirCrudo ("version=1`nusuario=20230001`ncodigo=AB12c`nequipo=$env:COMPUTERNAME`n" +
                       "sesionWindows=$sesionWindows`nfinLocal=manana por la tarde`n")
     } `
     -Prohibe @('se cerraria')

Caso -Nombre 'K3: el archivo es binario' `
     -Preparar { [IO.File]::WriteAllBytes($ruta, [byte[]](0..255)) } `
     -Prohibe @('se cerraria')

# --- Sesiones que no son suyas ------------------------------------------------------
Caso -Nombre 'M: el traspaso es de OTRA sesion de Windows' `
     -Preparar { Escribir -Fin (Get-Date).AddMinutes(4) -Sesion ($sesionWindows + 7) } `
     -Espera @('Traspaso ignorado (DeOtraSesionWindows)') `
     -Prohibe @('se cerraria') `
     -ArchivoAlFinal 'existe'

# --- Que no cierre dos veces ---------------------------------------------------------
# Se cierra una vez y, mientras el vigilante sigue vivo, reaparece el archivo.
# En produccion el cierre de verdad se lleva la sesion de Windows por delante;
# aqui, en simulacion, la sesion sigue abierta, asi que esto comprueba lo unico
# que se puede comprobar sin cerrarle la sesion a nadie: que no cierra DOS veces
# por el mismo vencimiento.
Caso -Nombre 'N: el traspaso reaparece despues de haber cerrado' `
     -Segundos 40 `
     -Preparar { Escribir -Fin (Get-Date).AddSeconds(12) } `
     -Durante {
        Start-Sleep -Seconds 25
        Escribir -Fin (Get-Date).AddSeconds(12)
     } `
     -Veces @{ 'se cerraria' = 1 }

# --- Permisos ------------------------------------------------------------------------
Caso -Nombre 'O: el traspaso esta en solo lectura y hay que borrarlo' `
     -Preparar {
        Escribir -Fin (Get-Date).AddHours(-2)
        Set-ItemProperty -Path $ruta -Name IsReadOnly -Value $true
     } `
     -Espera @('Traspaso ignorado (Caducada)')

Borrar
Write-Host ''
$aciertos = $total - $fallos
if ($fallos -eq 0) { Write-Host "$total de $total correctas" -ForegroundColor Green }
else { Write-Host "$aciertos de $total correctas y $fallos con fallo" -ForegroundColor Red }
