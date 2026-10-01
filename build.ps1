<#
.SYNOPSIS
    Compila DigBit y el servicio DigBit.Vigilante, y ejecuta DigBit, con un solo comando.

.EXAMPLE
    .\build.ps1                          # compila en Debug y lanza la app
    .\build.ps1 -Configuration Release   # compila en Release y lanza la app
    .\build.ps1 -Rebuild                 # recompila desde cero
    .\build.ps1 -NoRun                   # solo compila
    .\build.ps1 -Wait                    # espera a que cierres la app
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Rebuild,
    [switch]$NoRun,
    [switch]$Wait,

    # No cerrar los DigBit abiertos (lo usa crear_instalador.ps1). Si hay uno
    # abierto desde la carpeta de salida, la compilacion falla y lo dice.
    [switch]$SinCerrar
)

$ErrorActionPreference = 'Stop'

$solucion  = Join-Path $PSScriptRoot 'DigBit.sln'
$exe       = Join-Path $PSScriptRoot "DigBit\bin\$Configuration\DigBit.exe"
$vigilante = Join-Path $PSScriptRoot "DigBit.Vigilante\bin\$Configuration\DigBit.Vigilante.exe"

# --- Localizar MSBuild -------------------------------------------------------
# vswhere es la via soportada; evita depender de una ruta de instalacion fija.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "No se encontro vswhere.exe. Instala 'Build Tools for Visual Studio 2022' con la carga de trabajo de escritorio .NET."
}

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
           Select-Object -First 1

if (-not $msbuild) {
    throw "Visual Studio / Build Tools esta instalado pero sin el componente MSBuild. Agrega la carga de trabajo de escritorio .NET."
}

# --- Localizar NuGet ---------------------------------------------------------
# El proyecto usa packages.config, no PackageReference. Eso significa que
# 'msbuild -t:Restore' NO restaura nada (responde "ningun proyecto contiene
# paquetes para restaurar") y que las rutas HintPath del .csproj apuntan a
# ..\packages\, carpeta que esta en .gitignore. Sin este paso un clon nuevo
# falla con ~20 errores CS0246 (MySql, iTextSharp, Google...).
$nuget = (Get-Command 'nuget.exe' -ErrorAction SilentlyContinue).Source

if (-not $nuget) {
    $nuget = Join-Path $PSScriptRoot 'tools\nuget.exe'

    if (-not (Test-Path $nuget)) {
        Write-Host "nuget.exe no encontrado; descargandolo en tools\..." -ForegroundColor Yellow
        $carpetaTools = Split-Path $nuget
        if (-not (Test-Path $carpetaTools)) {
            New-Item -ItemType Directory -Path $carpetaTools | Out-Null
        }

        # Tls12 explicito: Windows PowerShell 5.1 negocia TLS 1.0 por defecto y
        # nuget.org rechaza esa conexion.
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        try {
            Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' `
                              -OutFile $nuget -UseBasicParsing
        }
        catch {
            throw ("No se pudo descargar nuget.exe. Descargalo a mano desde " +
                   "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe " +
                   "y colocalo en tools\. Detalle: $_")
        }
    }
}

# --- Restaurar paquetes ------------------------------------------------------
Write-Host "Restaurando paquetes NuGet..." -ForegroundColor Cyan
& $nuget restore $solucion -NonInteractive

if ($LASTEXITCODE -ne 0) {
    throw "La restauracion de paquetes fallo con codigo $LASTEXITCODE."
}

# --- Preparar ----------------------------------------------------------------
# Una instancia abierta bloquea el .exe y hace fallar el enlazado. Se cierran
# tambien los vigilantes en modo consola de esta sesion; el servicio instalado
# (sesion 0, SYSTEM) no se toca desde aqui.
$sesionActual = (Get-Process -Id $PID).SessionId
$abiertos = Get-Process -Name 'DigBit', 'DigBit.Vigilante' -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -eq $sesionActual }
if ($abiertos -and -not $SinCerrar) {
    Write-Host "Cerrando $($abiertos.Count) instancia(s) de DigBit / DigBit.Vigilante en ejecucion..." -ForegroundColor Yellow
    $abiertos | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# Las credenciales no se versionan; en un clon nuevo hay que crearlas.
$config = Join-Path $PSScriptRoot 'DigBit\connections.config'
if (-not (Test-Path $config)) {
    Copy-Item (Join-Path $PSScriptRoot 'DigBit\connections.config.example') $config
    Write-Host "Se creo DigBit\connections.config desde la plantilla. Edita los datos de la base de datos." -ForegroundColor Yellow
}

# --- Compilar ----------------------------------------------------------------
if ($Rebuild) { $target = 'Rebuild' } else { $target = 'Build' }

Write-Host "Compilando DigBit ($Configuration, $target)..." -ForegroundColor Cyan
& $msbuild $solucion -t:$target -p:Configuration=$Configuration -v:minimal -nologo -m

if ($LASTEXITCODE -ne 0) {
    throw "La compilacion fallo con codigo $LASTEXITCODE."
}
Write-Host "Compilacion correcta -> $exe" -ForegroundColor Green
Write-Host "                     -> $vigilante (servicio; no se lanza)" -ForegroundColor Green

# --- Ejecutar ----------------------------------------------------------------
if ($NoRun) { return }

if (-not (Test-Path $exe)) {
    throw "No se encontro el ejecutable en $exe"
}

Write-Host "Iniciando DigBit..." -ForegroundColor Cyan
if ($Wait) {
    Start-Process $exe -WorkingDirectory (Split-Path $exe) -Wait
} else {
    Start-Process $exe -WorkingDirectory (Split-Path $exe)
}
