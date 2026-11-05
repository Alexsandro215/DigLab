<#
.SYNOPSIS
    Instala (o quita) el servicio DigLab.Vigilante en este equipo.

.DESCRIPTION
    El vigilante corre como LocalSystem y cierra la sesion de Windows del alumno
    cuando vence su codigo de acceso. Se instala una vez por equipo del
    laboratorio. Hay que ejecutarlo como administrador.

.EXAMPLE
    .\instalar_vigilante.ps1
        Instala desde ..\DigLab.Vigilante\bin\Release\DigLab.Vigilante.exe y lo arranca.

    .\instalar_vigilante.ps1 -Ruta 'C:\DigLab\Vigilante\DigLab.Vigilante.exe'
        Instala desde una ruta concreta: esa es la del equipo ya desplegado.

    .\instalar_vigilante.ps1 -Desinstalar
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$Ruta,
    [switch]$Desinstalar
)

$ErrorActionPreference = 'Stop'
$nombre = 'DigLabVigilante'

# OJO: $PSScriptRoot llega VACIO dentro de los valores por defecto del bloque
# param cuando el guion se lanza con "powershell -File <ruta>", que es como lo
# llama el instalador. En el cuerpo si esta, asi que se resuelve aqui. Con & o
# dot-source funcionaba de las dos formas, y por eso no se vio antes.
if (-not $Ruta) { $Ruta = Join-Path $PSScriptRoot '..\DigLab.Vigilante\bin\Release\DigLab.Vigilante.exe' }

# El modo seguro arranca con cmd.exe como shell (SafeBoot\AlternateShell) y sin
# los servicios que no esten en su lista. Es decir: Mayus + Reiniciar deja un
# simbolo del sistema SIN DigLab y, hasta ahora, SIN vigilante, asi que la
# sesion tampoco se cerraba a su hora. Registrar el servicio aqui no impide
# entrar en modo seguro --eso depende de que la contrasena del inicio automatico
# deje de estar en claro-- pero al menos el vigilante sigue haciendo su trabajo.
$ramasSafeBoot = @(
    'HKLM:\SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal',
    'HKLM:\SYSTEM\CurrentControlSet\Control\SafeBoot\Network'
)

function RegistrarEnModoSeguro {
    foreach ($rama in $ramasSafeBoot) {
        $clave = Join-Path $rama $nombre
        if (-not (Test-Path $clave)) { New-Item -Path $clave -Force | Out-Null }
        # El valor predeterminado dice que tipo de entrada es, igual que en las
        # que trae Windows: 'Service' para un servicio.
        Set-ItemProperty -Path $clave -Name '(Default)' -Value 'Service'
    }
}

function QuitarDeModoSeguro {
    foreach ($rama in $ramasSafeBoot) {
        $clave = Join-Path $rama $nombre
        if (Test-Path $clave) { Remove-Item -Path $clave -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# La ruta se valida ANTES de tocar el servicio. Al reves, una ruta mala borraba
# el vigilante existente y despues fallaba al recrearlo, dejando el equipo sin
# nadie que cierre la sesion a la hora de salida. Y el ejemplo de la cabecera
# tenia la ruta equivocada, asi que seguir la propia ayuda del guion bastaba
# para provocarlo.
if (-not $Desinstalar) {
    if (-not (Test-Path $Ruta)) {
        throw "No se encontro $Ruta. No se toca el servicio actual. Compila en Release (.\build.cmd -Configuration Release -NoRun) o indica -Ruta; en un equipo ya desplegado es 'C:\DigLab\Vigilante\DigLab.Vigilante.exe'."
    }
    $Ruta = (Resolve-Path $Ruta).Path
}

# Get-Service devuelve objetos: no depende del idioma de Windows. Antes esto
# buscaba 'SERVICE_NAME' en la salida de sc.exe, que en un Windows en espanol
# dice 'NOMBRE_SERVICIO', asi que nunca detectaba un servicio ya instalado.
$existe = $null -ne (Get-Service -Name $nombre -ErrorAction SilentlyContinue)
if ($existe) {
    Write-Host "Deteniendo y quitando el servicio $nombre existente..." -ForegroundColor Yellow
    Stop-Service -Name $nombre -Force -ErrorAction SilentlyContinue
    sc.exe delete $nombre | Out-Null
    foreach ($espera in 1..10) {
        Start-Sleep -Seconds 1
        if (-not (Get-Service -Name $nombre -ErrorAction SilentlyContinue)) { break }
    }
}

if ($Desinstalar) {
    # El verde de antes era incondicional y mentia: si algo tiene el servicio
    # abierto (services.msc, el Visor de eventos), Windows lo deja "marcado para
    # eliminacion" y sigue ahi. No se lanza excepcion porque configurar_equipo
    # -Revertir llama aqui primero y un throw dejaria el kiosco a medio deshacer.
    QuitarDeModoSeguro
    if (Get-Service -Name $nombre -ErrorAction SilentlyContinue) {
        Write-Warning "$nombre SIGUE INSTALADO (marcado para eliminacion: algo lo tiene abierto, por ejemplo services.msc). Cierra esas ventanas, reinicia y comprueba con diagnostico_equipo.ps1."
    }
    elseif ($existe) {
        Write-Host "Servicio $nombre desinstalado." -ForegroundColor Green
    }
    else {
        Write-Host "No habia ningun servicio $nombre que quitar." -ForegroundColor Yellow
    }
    return
}

if (Get-Service -Name $nombre -ErrorAction SilentlyContinue) {
    throw "$nombre sigue marcado para eliminacion y no se puede recrear. AHORA MISMO este equipo esta SIN vigilante: cierra services.msc y el Visor de eventos, reinicia y repite."
}

# binPath= necesita la ruta entre comillas si tiene espacios; sc.exe exige el
# espacio despues de cada '='.
sc.exe create $nombre binPath= "`"$Ruta`"" start= auto DisplayName= "DigLab Vigilante" obj= LocalSystem | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe create fallo con codigo $LASTEXITCODE." }
sc.exe description $nombre "Cierra la sesion de Windows de quien usa el equipo cuando vence su codigo de acceso de DigLab." | Out-Null
sc.exe failure $nombre reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

# Start-Service en vez de 'sc.exe start': espera a que arranque de verdad y, si
# falla, dice por que en vez de dejar un codigo suelto.
try {
    Start-Service -Name $nombre -ErrorAction Stop
} catch {
    throw "El servicio $nombre se creo pero no arranca: $($_.Exception.Message) Mira $env:ProgramData\DigLab\logs y el Visor de eventos."
}

# Objetos, no texto: la salida de sc.exe esta traducida y 'STATE' no aparece en
# un Windows en espanol, con lo que esto reventaba con "metodo en un valor NULL"
# aunque el servicio hubiera quedado bien instalado.
$servicio = Get-Service -Name $nombre -ErrorAction SilentlyContinue
if (-not $servicio) { throw "El servicio $nombre no aparece despues de crearlo." }

RegistrarEnModoSeguro

Write-Host "Servicio $nombre instalado desde $Ruta" -ForegroundColor Green
Write-Host "  Estado: $($servicio.Status), arranque: $($servicio.StartType)"
Write-Host "  Tambien arranca en modo seguro (SafeBoot Minimal y Network)"
Write-Host "  Log: $env:ProgramData\DigLab\logs\vigilante-AAAAMMDD.log"
