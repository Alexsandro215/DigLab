<#
.SYNOPSIS
    Arma DigBit-Setup.exe: el instalador de DigBit, con el asistente normal de Windows.

.DESCRIPTION
    Corre solo en la PC de desarrollo; en los equipos donde se instala no hace falta
    PowerShell. Compila en Release, prepara una carpeta de carga y se la pasa a Inno Setup
    (instalador\DigBit.iss).

    La CONTRASENA de la base NO va dentro: se teclea al instalar cada equipo. El
    connections.config de la compilacion (el de este equipo) puede llevarla, asi que no se
    copia, y antes de armar se busca cualquier secreto real en toda la carga: si aparece,
    no se arma.

.EXAMPLE
    .\crear_setup.ps1 -Inno 'C:\ruta\a\ISCC.exe'

.EXAMPLE
    # Firmado, si se consigue un certificado (la clave del .pfx, en la variable DIGBIT_PFX_CLAVE)
    .\crear_setup.ps1 -Inno 'C:\ruta\a\ISCC.exe' -Certificado 'C:\ruta\certificado.pfx'
#>
[CmdletBinding()]
param(
    # Vacio: el ISCC.exe de Inno Setup en su carpeta normal.
    [string]$Inno,
    [string]$Servidor,
    [string]$Base,
    [string]$Usuario,
    [int]$Puerto = 0,
    # Vacio: DigBit-Setup.exe junto a la carpeta del repositorio.
    [string]$Destino,
    # Usar la compilacion Release que ya haya.
    [switch]$SinCompilar,
    # Las paginas del asistente, sin instalar nada ni pedir administrador. Solo para revisarlas.
    [switch]$VistaPrevia,
    # La carpeta donde la vista previa mira si ya hay algo instalado (solo la lee).
    [string]$VistaPreviaEn = 'C:\DigBitVistaPreviaNoExiste',
    # Un .pfx para firmar; vacio: sin firma, y Windows avisa "Editor desconocido".
    [string]$Certificado
)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
if (-not $Destino) { $Destino = Join-Path (Split-Path $raiz -Parent) 'DigBit-Setup.exe' }
$Destino = [IO.Path]::GetFullPath([IO.Path]::Combine((Get-Location).ProviderPath, $Destino))

function Paso([string]$t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }

# --- Inno Setup -------------------------------------------------------------------
if (-not $Inno) {
    $Inno = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Inno -or -not (Test-Path $Inno)) { throw 'No se encontro ISCC.exe de Inno Setup 7. Indica su ruta con -Inno.' }

# --- La conexion que va precargada (sin contrasena) --------------------------------
Paso '1. Conexion que llevara el instalador'
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
        throw "Falta -$($par[0]) (y $config no lo tiene: es la plantilla o no existe)."
    }
}
Write-Host "   base $Base en ${Servidor}:$Puerto, usuario $Usuario (la contrasena se teclea al instalar)"

# --- Compilar ---------------------------------------------------------------------
if (-not $SinCompilar) {
    Paso '2. Compilar la solucion (Release)'
    # -SinCerrar: sin el, build.ps1 cierra cualquier DigBit abierto.
    & (Join-Path $raiz 'build.ps1') -Configuration Release -NoRun -SinCerrar
}
$appDigBit = Join-Path $raiz 'DigBit\bin\Release'
$appVigilante = Join-Path $raiz 'DigBit.Vigilante\bin\Release'
$appConfigurador = Join-Path $raiz 'DigBit.Configurador\bin\Release'
foreach ($exe in @((Join-Path $appDigBit 'DigBit.exe'), (Join-Path $appVigilante 'DigBit.Vigilante.exe'), (Join-Path $appConfigurador 'DigBit.Configurador.exe'))) {
    if (-not (Test-Path $exe)) { throw "Falta $exe. Quita -SinCompilar o compila en Release." }
}

# --- Carga ------------------------------------------------------------------------
Paso '3. Preparar lo que va dentro'
$trabajo = Join-Path ([IO.Path]::GetTempPath()) ('digbit-setup-' + [Guid]::NewGuid().ToString('N'))
$carga = Join-Path $trabajo 'carga'
New-Item -ItemType Directory -Force $carga | Out-Null

try {
    # Sin .pdb ni .xml, que no hacen falta para ejecutar, y SIN connections.config: esa
    # la escribe el configurador en cada equipo, y la de aqui puede llevar la contrasena.
    function CopiarCompilacion([string]$desde, [string]$hacia) {
        New-Item -ItemType Directory -Force $hacia | Out-Null
        Get-ChildItem $desde -Recurse -File | Where-Object {
            $_.Extension -notin '.pdb', '.xml' -and $_.Name -notlike 'connections.config*'
        } | ForEach-Object {
            $relativa = $_.FullName.Substring($desde.TrimEnd('\').Length + 1)
            $destinoArchivo = Join-Path $hacia $relativa
            New-Item -ItemType Directory -Force (Split-Path $destinoArchivo) | Out-Null
            Copy-Item $_.FullName $destinoArchivo -Force
        }
    }

    $cargaDigBit = Join-Path $carga 'DigBit'
    $cargaConfigurador = Join-Path $carga 'Configurador'
    CopiarCompilacion $appDigBit $cargaDigBit
    CopiarCompilacion $appVigilante (Join-Path $carga 'Vigilante')
    New-Item -ItemType Directory -Force $cargaConfigurador | Out-Null
    Copy-Item (Join-Path $appConfigurador 'DigBit.Configurador.exe') $cargaConfigurador -Force
    # Lo que necesita para hablar con la base: lo mismo de DigBit, con sus redirecciones.
    Copy-Item (Join-Path $cargaDigBit '*.dll') $cargaConfigurador -Force
    Copy-Item (Join-Path $cargaDigBit 'DigBit.exe.config') (Join-Path $cargaConfigurador 'DigBit.Configurador.exe.config') -Force
    Copy-Item (Join-Path $raiz 'DigBit\img\iconoDB.ico') (Join-Path $carga 'icono.ico') -Force

    # Lo que el Setup extrae antes de instalar, para comprobar la base.
    $temporales = Join-Path $trabajo 'temporales.iss'
    $lineas = Get-ChildItem $cargaConfigurador -File | ForEach-Object { "  ExtractTemporaryFile('$($_.Name)');" }
    [IO.File]::WriteAllLines($temporales, [string[]]$lineas, (New-Object Text.UTF8Encoding $true))

    # Ningun secreto real dentro. Se leen en memoria y nunca se imprimen; se buscan en cada
    # archivo como texto, en UTF-8 y en UTF-16 (el de las cadenas dentro de un .exe).
    $secretos = New-Object Collections.Generic.List[string]
    if (Test-Path $config) {
        Add-Type -AssemblyName System.Data
        $constructor = New-Object System.Data.Common.DbConnectionStringBuilder
        $constructor.set_ConnectionString((([xml](Get-Content -Raw $config)).connectionStrings.add | Where-Object { $_.name -eq 'DigBit' }).connectionString)
        if ($constructor.ContainsKey('Password') -and "$($constructor['Password'])") { $secretos.Add([string]$constructor['Password']) }
    }
    $configApi = Join-Path $raiz 'api\config.php'
    if (Test-Path $configApi) {
        foreach ($m in [regex]::Matches([IO.File]::ReadAllText($configApi), "'([a-z_]*(?:contrasena|clave|password|pass)[a-z_]*)'\s*=>\s*'([^']*)'")) {
            if ($m.Groups[2].Value.Length -ge 6) { $secretos.Add($m.Groups[2].Value) }
        }
    }
    $hallados = 0
    foreach ($archivo in (Get-ChildItem $carga -Recurse -File)) {
        $bytes = [IO.File]::ReadAllBytes($archivo.FullName)
        $utf8 = [Text.Encoding]::UTF8.GetString($bytes)
        $utf16 = [Text.Encoding]::Unicode.GetString($bytes)
        foreach ($secreto in $secretos) {
            if ($utf8.Contains($secreto) -or $utf16.Contains($secreto)) {
                Write-Host "   un secreto aparece en $($archivo.Name)" -ForegroundColor Red
                $hallados++
            }
        }
    }
    if ($hallados -gt 0) { throw 'Hay secretos reales dentro de la carga. No se arma el instalador.' }
    Write-Host ("   {0} archivos; ningun secreto dentro ({1} buscados)" -f @(Get-ChildItem $carga -Recurse -File).Count, $secretos.Count)

    # --- Firma (opcional) -----------------------------------------------------------
    $firmar = $null
    if ($Certificado) {
        $firmar = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $firmar) { throw 'Se pidio firmar y no se encontro signtool.exe (viene con el SDK de Windows).' }
        foreach ($exe in @((Join-Path $cargaDigBit 'DigBit.exe'), (Join-Path $carga 'Vigilante\DigBit.Vigilante.exe'), (Join-Path $cargaConfigurador 'DigBit.Configurador.exe'))) {
            & $firmar.FullName sign /f $Certificado /p $env:DIGBIT_PFX_CLAVE /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 $exe
            if ($LASTEXITCODE -ne 0) { throw "No se pudo firmar $exe." }
        }
    }

    # --- Inno Setup -----------------------------------------------------------------
    Paso '4. Armar el instalador'
    $salidaInno = Join-Path $trabajo 'salida'
    $argumentos = @(
        "/DCarga=$carga", "/DTemporales=$temporales",
        "/DBaseServidor=$Servidor", "/DBaseNombre=$Base", "/DBaseUsuario=$Usuario", "/DBasePuerto=$Puerto",
        "/O$salidaInno", '/Qp'
    )
    if ($VistaPrevia) { $argumentos += "/DVistaPrevia=$VistaPreviaEn" }
    & $Inno @argumentos (Join-Path $raiz 'instalador\DigBit.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup termino con codigo $LASTEXITCODE." }

    $generado = Join-Path $salidaInno 'DigBit-Setup.exe'
    if (-not (Test-Path $generado)) { throw "No aparecio $generado." }
    if ($firmar) {
        & $firmar.FullName sign /f $Certificado /p $env:DIGBIT_PFX_CLAVE /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 $generado
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo firmar el instalador.' }
    }

    try {
        Copy-Item $generado $Destino -Force
    }
    catch [System.IO.IOException] {
        throw "No se puede reemplazar $Destino porque esta abierto. Cierralo y repite."
    }
}
finally {
    Remove-Item $trabajo -Recurse -Force -ErrorAction SilentlyContinue
}

$info = Get-Item $Destino
Write-Host ''
Write-Host ("Instalador listo: {0} ({1:N1} MB)" -f $info.FullName, ($info.Length / 1MB)) -ForegroundColor Green
Write-Host "   SHA256 $((Get-FileHash $Destino -Algorithm SHA256).Hash)" -ForegroundColor DarkGray
if (-not $Certificado) { Write-Host '   Sin firma: al abrirlo, Windows avisa "Editor desconocido".' -ForegroundColor Yellow }
