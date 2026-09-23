<#
.SYNOPSIS
    Prueba DigBit contra una COPIA de la base del TESCHI ya migrada.

.DESCRIPTION
    La base del TESCHI no es la de db\01_schema.sql: es MariaDB 5.5.68, tiene
    cinco tablas que nosotros no creamos, claves foraneas que las nuestras no
    tienen, duplicados historicos y referencias rotas. probar_horarios.ps1 no
    sirve contra ella porque depende de los ids del seed de desarrollo, que ahi
    no existen. Esta prueba usa ids REALES de esa base.

    Ejercita el codigo de verdad, cargando DigBit.exe por reflexion, por debajo
    de la interfaz (lo que abre ventanas bloquearia una prueba automatica):
      - contrasenas: MD5 heredado -> migracion perezosa a PBKDF2
      - el administrador ADMIN001 que crea la migracion 04, con su tipo 3
      - ResolverCodigo: sesion creada, reutilizada, otro laboratorio, ya registrado
      - que un codigo de clase se pueda repetir en codigos_accesos (se quito el UNIQUE)
      - la cola sin conexion: envio, y reenvio que NO duplica aunque no haya UNIQUE
      - la insercion en linea de Consultas.cs, con su SQL leido del propio fuente

    Prepara la copia asi (respaldo sacado de phpMyAdmin, MariaDB 5.5 local):
      SET FOREIGN_KEY_CHECKS=0;  + el .sql del respaldo   -> base teschi_otru
      db\03, db\04, db\05 y db\06 en ese orden

    MODIFICA la base. NUNCA contra el servidor de la escuela: se niega si la
    cadena apunta a teschi.edu.mx.

.EXAMPLE
    .\deploy\probar_esquema_teschi.ps1 `
        -CadenaConexion 'Database=teschi_otru;Server=127.0.0.1;Port=3308;User Id=root;Password=' `
        -MysqlExe 'C:\mariadb-5.5.68-winx64\bin\mysql.exe'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CadenaConexion,

    [Parameter(Mandatory = $true)]
    [string]$MysqlExe,

    [string]$Configuration = 'Release',

    # La que se paso a migrar_bd.ps1 -ContrasenaAdmin. Sin eso la 04 deja CAMBIAME.
    [string]$ContrasenaAdmin = 'CAMBIAME'
)

$ErrorActionPreference = 'Stop'

if ($CadenaConexion -match 'teschi\.edu\.mx') {
    throw 'Esta prueba escribe en la base. No se ejecuta contra el servidor de la escuela: usa una copia local.'
}

$carpeta = Join-Path $PSScriptRoot "..\DigBit\bin\$Configuration"
$exe = Join-Path $carpeta 'DigBit.exe'
if (-not (Test-Path $exe)) { throw "No encuentro $exe. Compila antes: .\build.ps1 -Configuration $Configuration -NoRun" }
$fuenteConsultas = Join-Path $PSScriptRoot '..\DigBit\conexion\Consultas.cs'

# --- Ids REALES de la base del TESCHI -------------------------------------------
$idProfesor   = 90            # docente de la copia, tipo 2
$idAlumno     = 43            # alumno de la copia, tipo 1
$matricula    = '0000000000'  # la de $idAlumno; su contrasena (MD5) es la de la copia
$idAlumno2    = 53
$labPropio    = 1
$labAjeno     = 2
$idGrupo      = 2
$idMateria    = 2
$idClase      = 9901
$codigo       = 'ENSA1'

$env:DIGBIT_CONNECTION_STRING = $CadenaConexion
$partes = @{}
foreach ($p in $CadenaConexion.Split(';')) {
    if ($p -match '^\s*([^=]+)=(.*)$') { $partes[$matches[1].Trim().ToLower()] = $matches[2].Trim() }
}
$puerto = if ($partes['port']) { $partes['port'] } else { '3306' }
$mysqlArgs = @('-u', $partes['user id'], ('--host=' + $partes['server']), ('--port=' + $puerto), '--protocol=tcp', '-N', '-B', $partes['database'])
if ($partes['password']) { $env:MYSQL_PWD = $partes['password'] }

function Sql([string]$consulta) {
    # Relajar la preferencia: en PowerShell 5.1 una linea de stderr de un
    # programa nativo es un error terminante aunque el programa acabe bien.
    $previo = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $salida = & $MysqlExe @mysqlArgs -e $consulta 2>&1
        if ($LASTEXITCODE -ne 0) { throw "mysql fallo: $salida`n  en: $consulta" }
        return $salida
    }
    finally { $ErrorActionPreference = $previo }
}

# --- Cargar DigBit.exe con sus dependencias ---------------------------------------
$global:CarpetaDigBit = $carpeta
[AppDomain]::CurrentDomain.add_AssemblyResolve([ResolveEventHandler] {
    param($remitente, $evento)
    $dll = Join-Path $global:CarpetaDigBit ((($evento.Name -split ',')[0]) + '.dll')
    if (Test-Path $dll) { return [Reflection.Assembly]::LoadFrom($dll) }
    return $null
})
[void][Reflection.Assembly]::LoadFrom((Join-Path $carpeta 'MySql.Data.dll'))
$asm = [Reflection.Assembly]::LoadFrom($exe)
$todo = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'

$tContrasenas = $asm.GetType('DigBit.conexion.Contrasenas')
$mVerificar   = $tContrasenas.GetMethod('Verificar', $todo)
$mMigrar      = $asm.GetType('DigBit.conexion.Consultas').GetMethod('MigrarContrasena', $todo)
$tHorarios    = $asm.GetType('DigBit.conexion.HorariosDatos')
$horarios     = [Activator]::CreateInstance($tHorarios, $true)
$mResolver    = $tHorarios.GetMethod('ResolverCodigo', [Type[]]@([string], [int], [int]))
$tCola        = $asm.GetType('DigBit.conexion.ColaBitacoras')
$mEnviar      = $tCola.GetMethod('Enviar', $todo)
$tPendiente   = $asm.GetType('DigBit.conexion.BitacoraPendiente')
$tSesion      = $asm.GetType('DigBit.conexion.SesionLocal')

# Lo que sale de una funcion llega envuelto en un PSObject, y MethodInfo.Invoke
# no lo desenvuelve: por eso las llamadas por reflexion usan .psobject.BaseObject.
function Conectar {
    $c = New-Object MySql.Data.MySqlClient.MySqlConnection($CadenaConexion)
    $c.Open()
    return $c
}

$total = 0; $fallos = 0
function Esperar([string]$nombre, $obtenido, $esperado) {
    $script:total++
    $ok = ("$obtenido" -eq "$esperado")
    if (-not $ok) { $script:fallos++ }
    Write-Host ("[{0}] {1}: {2}{3}" -f $(if ($ok) { 'OK ' } else { 'FALLO' }), $nombre, $obtenido,
        $(if ($ok) { '' } else { "  (esperado $esperado)" })) -ForegroundColor $(if ($ok) { 'Gray' } else { 'Red' })
}

function Verificar([string]$clave, [string]$guardada) {
    # El tercero es un parametro out: la reflexion lo devuelve en el array.
    $parametros = [object[]]@($clave, $guardada, $false)
    $ok = $mVerificar.Invoke($null, $parametros)
    return @{ Ok = $ok; Rehash = $parametros[2] }
}

# ================================================================================
Write-Host "`n== 1. Contrasenas: MD5 heredado -> PBKDF2" -ForegroundColor Cyan
# Como la dejo el sistema viejo, para poder repetir la prueba.
Sql "UPDATE usuarios SET password = MD5('123') WHERE numero_identificador = '$matricula'" | Out-Null
$guardada = "$(Sql "SELECT password FROM usuarios WHERE numero_identificador = '$matricula'")".Trim()
Esperar 'la guardada es MD5 (32 hex)' ($guardada -match '^[0-9a-f]{32}$') $true
$v = Verificar '123' $guardada
Esperar "'123' contra el MD5 entra" $v.Ok $true
Esperar 'y pide rehash' $v.Rehash $true

$c = Conectar
try { [void]$mMigrar.Invoke($null, [object[]]@($c.psobject.BaseObject, $matricula, '123', $true)) } finally { $c.Close() }
$nueva = "$(Sql "SELECT password FROM usuarios WHERE numero_identificador = '$matricula'")".Trim()
Esperar 'tras migrar, empieza por pbkdf2-' ($nueva.StartsWith('pbkdf2-')) $true
Esperar "cabe en varchar(200) ($($nueva.Length) caracteres)" ($nueva.Length -le 200) $true
$v = Verificar '123' $nueva
Esperar "'123' contra PBKDF2 entra" $v.Ok $true
Esperar 'y ya no pide rehash' $v.Rehash $false
Esperar "'124' contra PBKDF2 no entra" (Verificar '124' $nueva).Ok $false

# ================================================================================
Write-Host "`n== 2. Administrador de la migracion 04" -ForegroundColor Cyan
$fila = "$(Sql "SELECT CONCAT(fk_tipo_usuario, '|', password) FROM usuarios WHERE numero_identificador = 'ADMIN001'")".Trim()
$tipoAdmin, $claveAdmin = $fila.Split('|')
Esperar 'ADMIN001 es tipo 3' $tipoAdmin '3'
Esperar "su contrasena entra" (Verificar $ContrasenaAdmin $claveAdmin).Ok $true
Esperar "'CAMBIAME' ya no, si se cambio" (Verificar 'CAMBIAME' $claveAdmin).Ok ($ContrasenaAdmin -eq 'CAMBIAME')

# ================================================================================
Write-Host "`n== 3. Codigos de clase" -ForegroundColor Cyan
$dia = (((Get-Date).DayOfWeek.value__ + 6) % 7) + 1
Sql @"
DELETE FROM registros_bitacoras WHERE fk_codigo_accesos IN (SELECT idcodigos_accesos FROM (SELECT idcodigos_accesos FROM codigos_accesos WHERE codigo = '$codigo') x);
DELETE FROM codigos_accesos WHERE codigo = '$codigo';
DELETE FROM horarios WHERE clases_idclases = $idClase;
DELETE FROM clases WHERE idclases = $idClase;
INSERT INTO clases (idclases, usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en)
VALUES ($idClase, $idProfesor, $idGrupo, $idMateria, '$codigo', CURDATE() - INTERVAL 60 DAY, CURDATE() + INTERVAL 60 DAY, 1, NULL, NOW());
INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin)
VALUES ($idClase, $labPropio, $dia, '00:00:00', '23:59:59');
INSERT INTO codigos_accesos (codigo, hora_registro, materias_id_materia, grupos_idgrupos, laboratorios_idlaboratorios, usuarios_idusuarios, fecha, hora_entrada, hora_salida)
VALUES ('$codigo', NOW() - INTERVAL 1 DAY, $idMateria, $idGrupo, $labPropio, $idProfesor, CURDATE() - INTERVAL 1 DAY, '08:00:00', '10:00');
"@ | Out-Null

$r1 = $mResolver.Invoke($horarios, [object[]]@($codigo, $labPropio, $idAlumno))
Esperar "$codigo en su laboratorio" $r1.Motivo 'Aceptado'
$r2 = $mResolver.Invoke($horarios, [object[]]@($codigo, $labPropio, $idAlumno))
Esperar 'otra vez: misma sesion' ($r2.Ventana.IdCodigo -eq $r1.Ventana.IdCodigo) $true
Esperar "$codigo desde otro laboratorio" ($mResolver.Invoke($horarios, [object[]]@($codigo, $labAjeno, $idAlumno))).Motivo 'OtroLaboratorio'
$repetidos = "$(Sql "SELECT COUNT(*) FROM codigos_accesos WHERE codigo = '$codigo'")".Trim()
Esperar 'el codigo existe en dos sesiones (ayer y hoy)' $repetidos '2'
$idSesion = $r1.Ventana.IdCodigo

# ================================================================================
Write-Host "`n== 4. Cola sin conexion: no duplica aunque no haya UNIQUE" -ForegroundColor Cyan
$sesion = [Activator]::CreateInstance($tSesion, $true)
$sesion.Codigo = $codigo; $sesion.ClaseId = $idClase
$sesion.FranjaId = [int]"$(Sql "SELECT idhorarios FROM horarios WHERE clases_idclases = $idClase LIMIT 1")".Trim()
$sesion.Fecha = (Get-Date).Date; $sesion.Inicio = [TimeSpan]::Zero; $sesion.Fin = [TimeSpan]::Parse('23:59:59')
$sesion.LaboratorioId = $labPropio; $sesion.MateriaId = $idMateria; $sesion.GrupoId = $idGrupo; $sesion.ProfesorId = $idProfesor

function Pendiente {
    $b = [Activator]::CreateInstance($tPendiente, $true)
    $b.Registrada = Get-Date; $b.Matricula = $matricula; $b.Sesion = $sesion; $b.Computadora = 'ENSAYO-01'
    $b.FallaRed = 'Ninguno'; $b.ComentarioRed = ''; $b.FallaHardware = 'Ninguno'; $b.ComentarioHardware = ''
    $b.FallaSoftware = 'Ninguno'; $b.ComentarioSoftware = ''
    return $b
}
$c = Conectar
try {
    Esperar 'primer envio' ($mEnviar.Invoke($null, [object[]]@($c.psobject.BaseObject, (Pendiente).psobject.BaseObject))) 'Enviada'
    Esperar 'reenvio de la misma' ($mEnviar.Invoke($null, [object[]]@($c.psobject.BaseObject, (Pendiente).psobject.BaseObject))) 'Duplicada'
}
finally { $c.Close() }
Esperar 'una sola bitacora de ese alumno en la sesion' "$(Sql "SELECT COUNT(*) FROM registros_bitacoras WHERE fk_codigo_accesos = $idSesion AND fk_usuario = $idAlumno")".Trim() '1'
Esperar 'marcada como registrada sin conexion' "$(Sql "SELECT registrado_sin_conexion FROM registros_bitacoras WHERE fk_codigo_accesos = $idSesion AND fk_usuario = $idAlumno")".Trim() '1'
Esperar 'el alumno ya no puede volver a usar el codigo' ($mResolver.Invoke($horarios, [object[]]@($codigo, $labPropio, $idAlumno))).Motivo 'YaRegistrado'

# ================================================================================
Write-Host "`n== 5. Insercion en linea (Consultas.cs), con su SQL leido del fuente" -ForegroundColor Cyan
# Se lee del .cs en vez de copiarlo aqui, para probar el texto que de verdad se
# compila. Sus parametros @Nombre son variables de usuario en el cliente mysql.
$fuente = Get-Content -Raw $fuenteConsultas
if ($fuente -notmatch '(?s)string consultaInsertar = @"(.*?)";') { throw 'No encuentro consultaInsertar en Consultas.cs' }
$insertar = $matches[1].Trim()
$variables = "SET @IdCodigoAcceso = $idSesion, @codigo_usuario = $idAlumno2, @computadora = 'ENSAYO-02', @fallared = 'Ninguno', @comentariored = '', @fallahardware = 'Ninguno', @comentariohardware = '', @fallasoftware = 'Ninguno', @comentariosoftware = '';"
Esperar 'primera insercion: 1 fila' "$(Sql "$variables $insertar; SELECT ROW_COUNT();")".Trim() '1'
Esperar 'segunda: 0 filas (duplicado detectado)' "$(Sql "$variables $insertar; SELECT ROW_COUNT();")".Trim() '0'

# ================================================================================
Write-Host ''
if ($fallos -eq 0) { Write-Host "TODO BIEN: $total de $total" -ForegroundColor Green }
else { Write-Host "$fallos FALLO(S) de $total" -ForegroundColor Red; exit 1 }
