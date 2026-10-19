<#
.SYNOPSIS
    Abre la base de DigBit de esta maquina para que la usen otros equipos.

.DESCRIPTION
    Por defecto preparar_bd.ps1 deja MySQL escuchando solo en 127.0.0.1, que es
    lo correcto para una maquina suelta. Este guion la abre a la red para
    montar la topologia de verdad: UNA base y varias maquinas contra ella, que
    es como quedara en el TESCHI (los equipos del laboratorio y el equipo del
    administrador leyendo el mismo horario).

    Hace tres cosas:
      1. bind-address=0.0.0.0 en my.ini y reinicia el servicio.
      2. Crea un usuario de MySQL para los equipos remotos, con permisos de
         datos y ninguno de administracion. root sigue siendo solo local.
      3. Abre el puerto en el cortafuegos de Windows, solo para perfiles de red
         privada.

    ES PARA UNA RED DE CONFIANZA. En una maquina virtual de pruebas o en la red
    del laboratorio esta bien; no lo hagas en una maquina expuesta a internet.

    Ejecutar como administrador.

.EXAMPLE
    .\abrir_bd_en_red.ps1
    .\abrir_bd_en_red.ps1 -Usuario digbit -Contrasena 'otra-clave'
    .\abrir_bd_en_red.ps1 -Cerrar
#>
[CmdletBinding()]
param(
    [string]$Destino = 'C:\DigBitDB',
    [int]$Puerto = 3306,
    [string]$Servicio = 'DigBitMySQL',
    [string]$Usuario = 'digbit',
    # Sin valor por defecto a proposito: una contrasena escrita en un guion
    # acaba en el repositorio y de ahi no sale nunca.
    [Parameter(Mandatory = $true)]
    [string]$Contrasena,
    [string]$BaseDatos = 'teschi_otru',
    [string]$ReglaCortafuegos = 'DigBit MySQL',

    # Vuelve a dejarla solo local y quita la regla del cortafuegos.
    [switch]$Cerrar
)

$ErrorActionPreference = 'Stop'

function Paso([string]$t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }

$identidad = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $identidad).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ejecuta este guion como administrador.'
}

$ini   = Join-Path $Destino 'my.ini'
$mysql = Join-Path $Destino 'bin\mysql.exe'
foreach ($ruta in @($ini, $mysql)) {
    if (-not (Test-Path $ruta)) { throw "No encuentro $ruta. Ejecuta antes preparar_bd.ps1." }
}

function ReiniciarServicio {
    Restart-Service -Name $Servicio -Force
    foreach ($intento in 1..30) {
        & $mysql '-h' '127.0.0.1' '-P' $Puerto '-u' 'root' '-N' '-B' '-e' 'SELECT 1' 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw "MySQL no volvio a aceptar conexiones. Mira $Destino\error.log."
}

# --- Cerrar -------------------------------------------------------------------
if ($Cerrar) {
    Paso 'Volver a dejar la base solo local'
    (Get-Content -Raw $ini) -replace 'bind-address=.*', 'bind-address=127.0.0.1' | Set-Content -Path $ini -Encoding ASCII
    ReiniciarServicio
    Write-Host '   bind-address=127.0.0.1'

    Paso 'Quitar el usuario remoto y la regla del cortafuegos'
    & $mysql '-h' '127.0.0.1' '-P' $Puerto '-u' 'root' '-e' "DROP USER IF EXISTS '$Usuario'@'%';" | Out-Null
    Write-Host "   usuario '$Usuario' borrado"
    $regla = Get-NetFirewallRule -DisplayName $ReglaCortafuegos -ErrorAction SilentlyContinue
    if ($regla) { Remove-NetFirewallRule -DisplayName $ReglaCortafuegos; Write-Host '   regla borrada' }
    else { Write-Host '   no habia regla' }
    Write-Host ''
    Write-Host 'La base vuelve a ser solo de esta maquina.' -ForegroundColor Green
    return
}

# --- 1. Escuchar en la red ------------------------------------------------------
Paso '1. Escuchar en todas las interfaces'
$contenido = Get-Content -Raw $ini
if ($contenido -match 'bind-address=') {
    $contenido = $contenido -replace 'bind-address=.*', 'bind-address=0.0.0.0'
} else {
    $contenido = $contenido.TrimEnd() + "`r`nbind-address=0.0.0.0`r`n"
}
Set-Content -Path $ini -Value $contenido -Encoding ASCII
ReiniciarServicio
Write-Host '   bind-address=0.0.0.0'

# --- 2. Usuario para los equipos remotos ----------------------------------------
# Este guion es anterior a db\07_usuarios_minimos.sql. Cuando se escribio, el
# usuario 'digbit' con permisos de datos sobre la base era lo mas restringido
# que habia. Ya no: 07 crea digbit_equipo, que solo puede insertar bitacoras y
# tocar cinco columnas de usuarios, y digbit_admin para el equipo de gestion.
# Los dos son '@%', asi que en cuanto el puerto esta abierto ya valen desde
# fuera. Crear 'digbit' encima de eso anade un usuario capaz de borrar el
# semestre entero, que es justo lo que 07 evita; asi que si ya existen, no se
# crea nada y se imprimen los que hay.
$equipoExiste = (& $mysql '-h' '127.0.0.1' '-P' $Puerto '-u' 'root' '-N' '-B' `
    '-e' "SELECT COUNT(*) FROM mysql.user WHERE user='digbit_equipo' AND host='%'") -eq '1'
$usarMinimos = $equipoExiste -and -not $PSBoundParameters.ContainsKey('Usuario')

if ($usarMinimos) {
    Paso '2. Usuarios para los equipos remotos'
    Write-Host '   ya existen digbit_equipo y digbit_admin (db\07_usuarios_minimos.sql)'
    Write-Host '   no se crea ninguno mas: son mas restringidos que el de este guion'
    $Usuario = 'digbit_equipo'
} else {
    Paso "2. Usuario '$Usuario' para los equipos remotos"
    if ($equipoExiste) {
        Write-Warning "Ya existe digbit_equipo, que esta mas restringido. '$Usuario' podra BORRAR cualquier cosa de $BaseDatos."
    }
    # Solo permisos de datos: nada de crear usuarios ni tablas. root sigue atado a
    # localhost, asi que desde otra maquina no se puede administrar el servidor.
    $sql = @"
CREATE USER IF NOT EXISTS '$Usuario'@'%' IDENTIFIED WITH mysql_native_password BY '$Contrasena';
ALTER USER '$Usuario'@'%' IDENTIFIED WITH mysql_native_password BY '$Contrasena';
GRANT SELECT, INSERT, UPDATE, DELETE ON $BaseDatos.* TO '$Usuario'@'%';
FLUSH PRIVILEGES;
"@
    $sql | & $mysql '-h' '127.0.0.1' '-P' $Puerto '-u' 'root'
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el usuario.' }
    Write-Host "   creado, con permisos solo sobre $BaseDatos"
}

# --- 3. Cortafuegos --------------------------------------------------------------
Paso "3. Abrir el puerto $Puerto en redes privadas"
$regla = Get-NetFirewallRule -DisplayName $ReglaCortafuegos -ErrorAction SilentlyContinue
if ($regla) { Remove-NetFirewallRule -DisplayName $ReglaCortafuegos }
New-NetFirewallRule -DisplayName $ReglaCortafuegos -Direction Inbound -Action Allow `
    -Protocol TCP -LocalPort $Puerto -Profile Private, Domain | Out-Null
Write-Host '   regla creada (solo perfil privado y de dominio)'

# La regla se limita a privado y dominio a proposito: abrir MySQL en una red
# publica es otra cosa. Pero si Windows tiene clasificada como PUBLICA la red
# por la que van a venir los equipos, la regla no se aplica a nada y el guion
# terminaba en verde mientras nadie podia conectarse. El sintoma que sale al
# otro lado es "Unable to connect to any of the specified MySQL hosts", que no
# apunta al cortafuegos por ningun lado.
$publicas = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object { $_.NetworkCategory -eq 'Public' })
if ($publicas.Count -gt 0) {
    Write-Host ''
    Write-Warning 'La regla NO cubre las redes que tienes clasificadas como publicas:'
    foreach ($p in $publicas) {
        Write-Host ("     {0,-20} {1}" -f $p.InterfaceAlias, $p.Name) -ForegroundColor Yellow
    }
    Write-Host '   Si los equipos vienen por ahi, no van a conectar. Clasificala como privada:' -ForegroundColor Yellow
    foreach ($p in $publicas) {
        Write-Host ("     Set-NetConnectionProfile -InterfaceAlias '{0}' -NetworkCategory Private" -f $p.InterfaceAlias) -ForegroundColor Yellow
    }
    Write-Host '   (o Configuracion -> Red e Internet -> esa red -> Red privada)' -ForegroundColor Yellow
}

# --- Resumen ---------------------------------------------------------------------
Paso 'Desde donde se puede llegar'
$ips = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -ne '127.0.0.1' }
foreach ($ip in $ips) {
    Write-Host ("   {0,-16} {1}" -f $ip.IPAddress, $ip.InterfaceAlias)
}

Write-Host ''
Write-Host 'Cadena de conexion para las OTRAS maquinas:' -ForegroundColor Green
foreach ($ip in $ips) {
    Write-Host ("   Database=$BaseDatos;Server=$($ip.IPAddress);Port=$Puerto;User Id=$Usuario;Password=$Contrasena") -ForegroundColor Yellow
}
# Una sola IP es la buena, y cual depende de por donde venga cada equipo. Sin
# esto hay que adivinar entre varias, y la de VirtualBox se parece bastante a
# la de la red de verdad como para escoger mal.
if ($ips.Count -gt 1) {
    Write-Host ''
    Write-Host 'Sale mas de una porque este equipo tiene varias redes. Usa:' -ForegroundColor Cyan
    foreach ($ip in $ips) {
        $pista = switch -Wildcard ($ip.InterfaceAlias) {
            '*VirtualBox*' { 'solo para maquinas virtuales de este equipo' }
            '*Hyper-V*'    { 'solo para maquinas virtuales de este equipo' }
            '*Wi-Fi*'      { 'la red inalambrica; vale si los equipos van por wifi' }
            default        { 'la red del edificio; esta es la de los equipos del laboratorio' }
        }
        if ($ip.IPAddress -like '192.168.56.*') { $pista = 'red interna de VirtualBox; solo para las virtuales de este equipo' }
        Write-Host ("   {0,-16} {1}" -f $ip.IPAddress, $pista)
    }
}
if ($usarMinimos) {
    Write-Host ''
    Write-Host 'Para el equipo de la administracion, el mismo Server pero:' -ForegroundColor Green
    Write-Host "   User Id=digbit_admin  y su contrasena" -ForegroundColor Yellow
}
Write-Host ''
Write-Host 'Si esta maquina es una virtual con adaptador NAT y ninguna de esas IP'
Write-Host 'te sirve desde el anfitrion, anade un reenvio de puertos en VirtualBox:'
Write-Host '   Configuracion -> Red -> Avanzadas -> Reenvio de puertos'
Write-Host ("   Anfitrion 127.0.0.1:13306  ->  Invitado (vacio):$Puerto")
Write-Host '   y desde el anfitrion usa Server=127.0.0.1;Port=13306'
Write-Host ''
Write-Host 'Para deshacerlo: .\abrir_bd_en_red.ps1 -Cerrar'
