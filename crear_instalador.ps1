<#
.SYNOPSIS
    Arma DigBit-Instalador.exe: UN solo archivo que instala DigBit en un equipo
    y pregunta de que tipo es (administrador, maestro o alumno/kiosco).

.DESCRIPTION
    Dentro del .exe van, como un zip incrustado (ver DigBit.Instalador\Carga.cs):
        DigBit\bin\Release\            la aplicacion, con la PLANTILLA de connections.config
        DigBit.Vigilante\bin\Release\  el servicio del kiosco
        deploy\                        los guiones que hacen la instalacion
        conexion.txt                   servidor, base, usuario y puerto de la base

    La CONTRASENA de la base NO va dentro: se teclea al instalar cada equipo.
    Asi el .exe puede circular (memoria USB, correo) sin exponer la base. Por el
    mismo motivo el connections.config de la compilacion (el de este equipo, que
    puede llevarla) se sustituye por la plantilla.

    No lleva el MySQL portatil: la base de la escuela esta en un servidor. Para
    montar un servidor de base de datos propio sigue estando empaquetar.ps1.

.EXAMPLE
    .\crear_instalador.ps1
    # La conexion sale del connections.config de DigBit\bin\Release (sin la contrasena).

.EXAMPLE
    .\crear_instalador.ps1 -Servidor sql.teschi.edu.mx -Base teschi_otru -Usuario teschi_otru -SinCompilar
#>
[CmdletBinding()]
param(
    [string]$Servidor,
    [string]$Base,
    [string]$Usuario,
    [int]$Puerto = 0,

    # Vacio: DigBit-Instalador.exe junto a la carpeta del repositorio.
    [string]$Destino,

    # Usar la compilacion Release que ya haya, sin recompilar la solucion.
    [switch]$SinCompilar
)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
# En el cuerpo y no como valor por defecto del param: ahi $PSScriptRoot llega
# vacio cuando se lanza con "powershell -File".
if (-not $Destino) { $Destino = Join-Path (Split-Path $raiz -Parent) 'DigBit-Instalador.exe' }
# Relativa a donde se esta, no a la carpeta del proceso (que es lo que usa .NET).
$Destino = [IO.Path]::GetFullPath([IO.Path]::Combine((Get-Location).ProviderPath, $Destino))

function Paso([string]$t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }

# En Windows PowerShell 5.1 una linea en stderr de un .exe se vuelve error
# terminante con 'Stop' aunque el programa acabe bien: se decide por el codigo.
function Nativo([scriptblock]$bloque) {
    $previo = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $bloque } finally { $ErrorActionPreference = $previo }
}

# --- Conexion que va dentro (sin contrasena) -------------------------------------
Paso '1. Conexion a la base que llevara el instalador'
$config = Join-Path $raiz 'DigBit\bin\Release\connections.config'
if ((-not $Servidor -or -not $Base -or -not $Usuario) -and (Test-Path $config)) {
    $nodo = ([xml](Get-Content -Raw $config)).connectionStrings.add | Where-Object { $_.name -eq 'DigBit' } | Select-Object -First 1
    if ($nodo) {
        $partes = @{}
        foreach ($trozo in $nodo.connectionString.Split(';')) {
            $igual = $trozo.IndexOf('=')
            if ($igual -gt 0) { $partes[$trozo.Substring(0, $igual).Trim().ToLowerInvariant()] = $trozo.Substring($igual + 1).Trim() }
        }
        if (-not $Servidor) { $Servidor = @($partes['server'], $partes['host'], $partes['data source']) | Where-Object { $_ } | Select-Object -First 1 }
        if (-not $Base)     { $Base = @($partes['database'], $partes['initial catalog']) | Where-Object { $_ } | Select-Object -First 1 }
        if (-not $Usuario)  { $Usuario = @($partes['user id'], $partes['uid'], $partes['user'], $partes['username']) | Where-Object { $_ } | Select-Object -First 1 }
        if ($Puerto -le 0 -and $partes['port']) { $Puerto = [int]$partes['port'] }
    }
}
if ($Puerto -le 0) { $Puerto = 3306 }

foreach ($par in @(@('Servidor', $Servidor, 'SERVIDOR'), @('Base', $Base, 'NOMBRE_BD'), @('Usuario', $Usuario, 'USUARIO'))) {
    if (-not $par[1] -or $par[1] -eq $par[2]) {
        throw "Falta -$($par[0]) (y $config no lo tiene: es la plantilla o no existe). Indica -Servidor, -Base y -Usuario."
    }
}
Write-Host "   base $Base en ${Servidor}:$Puerto, usuario $Usuario (la contrasena se teclea al instalar)"

# --- Compilar ---------------------------------------------------------------------
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw "No se encontro vswhere.exe. Instala 'Build Tools for Visual Studio 2022' con la carga de escritorio .NET." }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'No se encontro MSBuild.' }

if (-not $SinCompilar) {
    Paso '2. Compilar la solucion (Release)'
    # Con build.ps1, que en un clon nuevo restaura los paquetes NuGet (sin eso
    # fallaba con veinte errores que no dicen nada), pero -SinCerrar: sin el,
    # cierra cualquier DigBit abierto.
    try {
        & (Join-Path $raiz 'build.ps1') -Configuration Release -NoRun -SinCerrar
    }
    catch {
        throw "La compilacion fallo: $($_.Exception.Message) Si DigBit esta abierto desde bin\Release, cierralo y repite (o usa -SinCompilar)."
    }
}

$appDigBit = Join-Path $raiz 'DigBit\bin\Release'
$appVigilante = Join-Path $raiz 'DigBit.Vigilante\bin\Release'
foreach ($exe in @((Join-Path $appDigBit 'DigBit.exe'), (Join-Path $appVigilante 'DigBit.Vigilante.exe'))) {
    if (-not (Test-Path $exe)) { throw "Falta $exe. Quita -SinCompilar o compila en Release." }
}

# --- Carga ------------------------------------------------------------------------
Paso '3. Preparar lo que va dentro'
$trabajo = Join-Path ([IO.Path]::GetTempPath()) ('digbit-instalador-' + [Guid]::NewGuid().ToString('N'))
$carga = Join-Path $trabajo 'carga'
$zip = Join-Path $trabajo 'Carga.zip'
New-Item -ItemType Directory -Force $carga | Out-Null

try {
    # Sin .pdb (depuracion) ni .xml (documentacion de las librerias): no hacen
    # falta para ejecutar y son la mitad del peso.
    function CopiarCompilacion([string]$desde, [string]$hacia) {
        New-Item -ItemType Directory -Force $hacia | Out-Null
        Get-ChildItem $desde -Recurse -File | Where-Object { $_.Extension -notin '.pdb', '.xml' } | ForEach-Object {
            $relativa = $_.FullName.Substring($desde.TrimEnd('\').Length + 1)
            $destinoArchivo = Join-Path $hacia $relativa
            New-Item -ItemType Directory -Force (Split-Path $destinoArchivo) | Out-Null
            Copy-Item $_.FullName $destinoArchivo -Force
        }
    }

    CopiarCompilacion $appDigBit (Join-Path $carga 'DigBit\bin\Release')
    CopiarCompilacion $appVigilante (Join-Path $carga 'DigBit.Vigilante\bin\Release')

    # La conexion de la compilacion es la de ESTE equipo y puede llevar la
    # contrasena: dentro va la plantilla. configurar_equipo.ps1 escribe la buena
    # en cada equipo con lo que se teclea en el instalador.
    Copy-Item (Join-Path $raiz 'DigBit\connections.config.example') (Join-Path $carga 'DigBit\bin\Release\connections.config') -Force

    New-Item -ItemType Directory -Force (Join-Path $carga 'deploy') | Out-Null
    Copy-Item (Join-Path $raiz 'deploy\*.ps1') (Join-Path $carga 'deploy') -Force

    $lineasConexion = @(
        '# Base de datos a la que se conectan los equipos instalados con este .exe.',
        '# La contrasena NO esta aqui: se teclea al instalar.',
        "Servidor=$Servidor",
        "Base=$Base",
        "Usuario=$Usuario",
        "Puerto=$Puerto"
    )
    [IO.File]::WriteAllLines((Join-Path $carga 'conexion.txt'), $lineasConexion, (New-Object Text.UTF8Encoding $false))

    # Comprobacion: ninguna contrasena de verdad dentro de la carga.
    $plantilla = Get-Content -Raw (Join-Path $carga 'DigBit\bin\Release\connections.config')
    if ($plantilla -notmatch 'Server=SERVIDOR') { throw 'El connections.config de la carga no es la plantilla. No se sigue.' }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($carga, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archivos = @(Get-ChildItem $carga -Recurse -File).Count
    Write-Host ("   {0} archivos, {1:N1} MB comprimidos" -f $archivos, ((Get-Item $zip).Length / 1MB))

    # --- Instalador con la carga dentro ---------------------------------------------
    Paso '4. Compilar el instalador con todo dentro'
    # MSBuild exige la barra final (la de IntermediateOutputPath no la pone solo),
    # y se usa '/' porque una '\' final dentro de comillas escapa la comilla de
    # cierre si la ruta lleva espacios.
    $salidaExe = (Join-Path $trabajo 'bin') + '/'
    $intermedia = (Join-Path $trabajo 'obj') + '/'
    $proyecto = Join-Path $raiz 'DigBit.Instalador\DigBit.Instalador.csproj'
    $salida = Nativo { & $msbuild $proyecto -t:Rebuild -p:Configuration=Release "-p:CargaInstalador=$zip" "-p:OutputPath=$salidaExe" "-p:IntermediateOutputPath=$intermedia" -v:minimal -nologo 2>&1 }
    if ($LASTEXITCODE -ne 0) {
        $salida | ForEach-Object { Write-Host "   $_" }
        throw "No se pudo compilar el instalador ($LASTEXITCODE)."
    }

    $generado = Join-Path $salidaExe 'DigBit.Instalador.exe'
    if (-not (Test-Path $generado)) { throw "No aparecio $generado." }
    if ((Get-Item $generado).Length -lt (Get-Item $zip).Length) { throw 'El instalador pesa menos que su carga: no la lleva dentro.' }

    try {
        Copy-Item $generado $Destino -Force
    }
    catch [System.IO.IOException] {
        throw "No se puede reemplazar $Destino porque esta abierto (la ventana del instalador). Cierralo y repite."
    }
}
finally {
    Remove-Item $trabajo -Recurse -Force -ErrorAction SilentlyContinue
}

$info = Get-Item $Destino
$hash = (Get-FileHash $Destino -Algorithm SHA256).Hash
Write-Host ''
Write-Host ("Instalador listo: {0} ({1:N1} MB)" -f $info.FullName, ($info.Length / 1MB)) -ForegroundColor Green
Write-Host "   SHA256 $hash" -ForegroundColor DarkGray
Write-Host '   Al abrirlo pide permisos de administrador, pregunta el tipo de equipo y la contrasena de la base.' -ForegroundColor DarkGray
