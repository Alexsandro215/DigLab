<#
.SYNOPSIS
    Hace que Microsoft Defender deje de tratar a DigLab como malware, y recupera
    lo que ya haya puesto en cuarentena.

.DESCRIPTION
    Defender detecta a DigLab como "Trojan:Win32/Bearfoos.A!ml" y lo pone en
    cuarentena. Es un FALSO POSITIVO, pero no es caprichoso: el sufijo !ml
    significa que lo decidio un modelo de aprendizaje automatico, y lo que ve es
    un ejecutable SIN FIRMAR que se registra a si mismo como shell de Winlogon,
    lanza procesos y cierra sesiones de Windows. Eso es, literalmente, el
    comportamiento de un troyano de persistencia. El modelo no puede saber que
    aqui es intencionado.

    Este guion hace tres cosas:
      1. Excluye la carpeta de DigLab y sus dos ejecutables.
      2. Recupera de la cuarentena lo que ya se haya llevado.
      3. Comprueba que el ejecutable volvio a su sitio.

    LAS EXCLUSIONES SON UNA CONCESION, NO UNA SOLUCION. Se excluye una carpeta
    concreta, no el disco, y aun asi eso significa que Defender deja de mirar lo
    que haya ahi dentro. La solucion de verdad es FIRMAR el ejecutable con un
    certificado de firma de codigo, y ademas enviar el archivo a Microsoft como
    falso positivo para que el modelo deje de marcarlo:
        https://www.microsoft.com/en-us/wdsi/filesubmission

    Ejecutar como administrador.

.EXAMPLE
    .\permitir_en_defender.ps1
    .\permitir_en_defender.ps1 -Destino D:\DigLab
    .\permitir_en_defender.ps1 -Quitar
#>
[CmdletBinding()]
param(
    [string]$Destino = 'C:\DigLab',
    [string]$Amenaza = 'Trojan:Win32/Bearfoos.A!ml',

    # Quita las exclusiones. Defender volvera a vigilar esa carpeta y, si el
    # ejecutable sigue sin firmar, probablemente lo vuelva a poner en cuarentena.
    [switch]$Quitar
)

$ErrorActionPreference = 'Stop'

function Paso([string]$t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }

$identidad = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $identidad).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ejecuta este guion como administrador.'
}

if (-not (Get-Command Add-MpPreference -ErrorAction SilentlyContinue)) {
    Write-Warning 'Este equipo no tiene los comandos de Microsoft Defender. Si usa otro antivirus, hay que excluir la carpeta en el suyo.'
    return
}

$Destino   = [IO.Path]::GetFullPath($Destino)
$exeDigLab = Join-Path $Destino 'DigLab.exe'
$exeVigila = Join-Path $Destino 'Vigilante\DigLab.Vigilante.exe'

# --- Quitar --------------------------------------------------------------------
if ($Quitar) {
    Paso 'Quitar las exclusiones'
    foreach ($ruta in @($Destino)) {
        try { Remove-MpPreference -ExclusionPath $ruta -ErrorAction Stop; Write-Host "   quitada la carpeta $ruta" }
        catch { Write-Host "   no habia exclusion para $ruta" }
    }
    foreach ($exe in @($exeDigLab, $exeVigila)) {
        try { Remove-MpPreference -ExclusionProcess $exe -ErrorAction Stop; Write-Host "   quitado el proceso $exe" }
        catch { Write-Host "   no habia exclusion para $exe" }
    }
    Write-Warning 'Defender vuelve a vigilar esa carpeta. Mientras DigLab no este firmado, es probable que lo ponga en cuarentena otra vez.'
    return
}

# --- 1. Excluir -----------------------------------------------------------------
Paso "1. Excluir $Destino en Defender"
Add-MpPreference -ExclusionPath $Destino
Write-Host "   carpeta excluida: $Destino"
foreach ($exe in @($exeDigLab, $exeVigila)) {
    Add-MpPreference -ExclusionProcess $exe
    Write-Host "   proceso excluido: $exe"
}

$actual = Get-MpPreference
if ($actual.ExclusionPath -notcontains $Destino) {
    throw "La exclusion no quedo puesta. Si el equipo tiene directivas de grupo que las bloquean, hay que pedirlo a quien administre el dominio."
}

# --- 2. Recuperar de la cuarentena ----------------------------------------------
Paso '2. Recuperar lo que ya estuviera en cuarentena'

# MpCmdRun vive en la carpeta de la plataforma, que cambia con cada
# actualizacion, asi que se busca la mas reciente antes de la ruta clasica.
$mpcmd = $null
$plataforma = Join-Path $env:ProgramData 'Microsoft\Windows Defender\Platform'
if (Test-Path $plataforma) {
    $ultima = Get-ChildItem $plataforma -Directory -ErrorAction SilentlyContinue |
              Sort-Object Name -Descending | Select-Object -First 1
    if ($ultima -and (Test-Path (Join-Path $ultima.FullName 'MpCmdRun.exe'))) {
        $mpcmd = Join-Path $ultima.FullName 'MpCmdRun.exe'
    }
}
if (-not $mpcmd) {
    $clasica = Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe'
    if (Test-Path $clasica) { $mpcmd = $clasica }
}

if (-not $mpcmd) {
    Write-Warning '   No encuentro MpCmdRun.exe. Recupera a mano desde Seguridad de Windows, Proteccion antivirus, Historial de proteccion, Acciones, Restaurar.'
}
else {
    Write-Host "   usando $mpcmd"
    & $mpcmd -Restore -Name $Amenaza 2>&1 | ForEach-Object { '   ' + $_ }
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "   MpCmdRun devolvio $LASTEXITCODE. Si dice que no encuentra la amenaza, es que no habia nada en cuarentena, o el nombre es otro."
        Write-Host '   Para ver que hay en cuarentena:' -ForegroundColor DarkGray
        Write-Host "     & '$mpcmd' -Restore -ListAll" -ForegroundColor DarkGray
    }
}

# --- 3. Comprobar ----------------------------------------------------------------
Paso '3. Comprobar que el ejecutable esta en su sitio'
if (Test-Path $exeDigLab) {
    $i = Get-Item $exeDigLab
    Write-Host ("   {0}  {1:N0} bytes  {2}" -f $exeDigLab, $i.Length, $i.LastWriteTime) -ForegroundColor Green
}
else {
    Write-Host "   $exeDigLab NO esta." -ForegroundColor Red
    Write-Host '   Recupera desde Seguridad de Windows o vuelve a copiarlo con deploy\actualizar_diglab.ps1.' -ForegroundColor Red
}

Write-Host ''
Write-Host 'Recuerda: esto es un parche. La solucion es firmar el ejecutable y' -ForegroundColor Yellow
Write-Host 'enviarlo a Microsoft como falso positivo:' -ForegroundColor Yellow
Write-Host '  https://www.microsoft.com/en-us/wdsi/filesubmission' -ForegroundColor Yellow
