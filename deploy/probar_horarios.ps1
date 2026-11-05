<#
.SYNOPSIS
    Prueba la capa de datos de la fase 6 (horarios por laboratorio) contra una
    base de DESARROLLO.

.DESCRIPTION
    Carga DigLab.exe por reflexion y ejercita HorariosDatos.ResolverCodigo (todos
    los motivos de rechazo), las excepciones, la validacion de franjas, la agenda
    y la marca "fuera de grupo" de los informes; con db/20, ademas, que una
    franja terminada no se da ni choca y la que la sustituye si. Crea sus propias clases de
    prueba (codigos TST01..TSTNO, ids 901-905) y las borra al terminar, asi que
    no depende del seed. Necesita una base creada con db\01_schema.sql y
    db\02_seed.sql (usa los laboratorios 1 a 3, los alumnos 20230001/20230002 y
    los profesores EMP001/EMP002) y el cliente mysql.exe.
    MODIFICA la base: no lo ejecutes contra la base real del TESCHI.

.EXAMPLE
    .\probar_horarios.ps1 -CadenaConexion 'Database=teschi_otru;Server=127.0.0.1;Port=3307;User Id=root;Password=' -MysqlExe 'C:\mysql\bin\mysql.exe'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CadenaConexion,

    [string]$MysqlExe = 'mysql.exe',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Otro DigLab.exe; por defecto, el de DigLab\bin\<Configuration>.
    [string]$Exe
)

$ErrorActionPreference = 'Stop'
if ($CadenaConexion -match 'teschi\.edu\.mx') { throw 'Esta prueba escribe en la base: nunca contra la de la escuela.' }
$exe   = if ($Exe) { $Exe } else { Join-Path $PSScriptRoot "..\DigLab\bin\$Configuration\DigLab.exe" }
$mysql = $MysqlExe
$env:DIGLAB_CONNECTION_STRING = $CadenaConexion

# Servidor, puerto y usuario para el cliente mysql, sacados de la cadena.
$partes = @{}
foreach ($p in $CadenaConexion.Split(';')) {
    if ($p -match '^\s*([^=]+)=(.*)$') { $partes[$matches[1].Trim().ToLower()] = $matches[2].Trim() }
}
$puerto = '3306'
if ($partes['port']) { $puerto = $partes['port'] }
$mysqlArgs = @('-u', $partes['user id'], ('--host=' + $partes['server']), ('--port=' + $puerto), '--protocol=tcp')

# La contrasena va por el entorno y NO en la linea de comandos. Con --password,
# mysql avisa por stderr de que es inseguro, y en Windows PowerShell 5.1 con
# $ErrorActionPreference = 'Stop' esa linea de stderr se convierte en un error
# terminante aunque el programa acabe bien: la suite entera se caia en el primer
# uso si la base tenia contrasena. Ademas asi no queda en el historial.
if ($partes['password']) { $env:MYSQL_PWD = $partes['password'] }

function Sql([string]$consulta) {
    $previo = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { return (& $mysql @mysqlArgs teschi_otru -N -e $consulta) }
    finally { $ErrorActionPreference = $previo }
}

# Las franjas y extras de prueba se ponen alrededor de la hora del servidor (de 3
# horas antes a 2 despues): cerca de la medianoche cruzarian de dia y la prueba
# fallaria sin motivo.
$horaServidor = [TimeSpan]"$(Sql 'SELECT CURTIME();')"
if ($horaServidor -lt [TimeSpan]'03:00:00' -or $horaServidor -gt [TimeSpan]'21:00:00') {
    throw "Esta prueba necesita la hora del servidor entre 03:00 y 21:00 (es $horaServidor)."
}

$asm = [Reflection.Assembly]::LoadFrom($exe)
$tHorarios  = $asm.GetType('DigLab.conexion.HorariosDatos')
$tConsultas = $asm.GetType('DigLab.conexion.Consultas')
$h = [Activator]::CreateInstance($tHorarios, $true)
$c = [Activator]::CreateInstance($tConsultas, $true)
$mResolver = $tHorarios.GetMethod('ResolverCodigo')
$mValidarFranja = $tHorarios.GetMethod('ValidarFranja')
$mAgenda = $tHorarios.GetMethod('AgendaDelDia')
$mClaseEnCurso = $tHorarios.GetMethod('ClaseEnCurso')
$mRegistro = $tConsultas.GetMethod('consultaRegistroSesion')
$mClases = $tHorarios.GetMethod('ObtenerClases')

$total = 0; $fallos = 0
function Esperar([string]$nombre, [string]$codigo, [int]$lab, [int]$alumno, [string]$motivoEsperado) {
    $script:total++
    # El cuarto, la matricula (opcional en C#, pero por reflexion van todos).
    $r = $mResolver.Invoke($h, [object[]]@($codigo, $lab, $alumno, $null))
    $motivo = $r.Motivo.ToString()
    $ok = ($motivo -eq $motivoEsperado)
    if (-not $ok) { $script:fallos++ }
    $marca = if ($ok) { 'OK ' } else { 'FALLO' }
    $extra = ''
    if ($r.Ventana -ne $null) { $extra = " sesion=$($r.Ventana.IdCodigo) lab='$($r.Ventana.Laboratorio)' fin=$($r.Ventana.Fin.ToString('HH:mm'))" }
    Write-Host ("[{0}] {1}: {2} (esperado {3}) - {4}{5}" -f $marca, $nombre, $motivo, $motivoEsperado, $r.Mensaje, $extra)
    return $r
}

# --- Datos de prueba propios (se crean aqui y se borran al final) -------------
# Las sesiones se buscan tambien por su franja o su excepcion: fuera del codigo
# fijo (db/11) llevan un codigo al azar, y al borrar la franja solo quedarian
# huerfanas (la clave foranea las pone en NULL).
function LimpiarPruebas {
    $deLaPrueba = "LEFT JOIN horarios h ON h.idhorarios = ca.horarios_idhorarios" `
        + " LEFT JOIN horario_excepciones x ON x.idhorario_excepciones = ca.horario_excepciones_id" `
        + " WHERE ca.codigo LIKE 'TST%' OR h.clases_idclases BETWEEN 901 AND 905 OR x.clases_idclases BETWEEN 901 AND 905 OR x.motivo LIKE 'PRUEBA%'"
    Sql "DELETE rb FROM registros_bitacoras rb INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos $deLaPrueba; DELETE ca FROM codigos_accesos ca $deLaPrueba;" | Out-Null
    Sql "DELETE FROM horario_excepciones WHERE motivo LIKE 'PRUEBA%' OR clases_idclases BETWEEN 901 AND 905 OR horarios_idhorarios IN (SELECT idhorarios FROM horarios WHERE clases_idclases BETWEEN 901 AND 905); DELETE FROM horarios WHERE clases_idclases BETWEEN 901 AND 905; DELETE FROM clases WHERE idclases BETWEEN 901 AND 905;" | Out-Null
}
LimpiarPruebas

# Con db/11, los laboratorios 1 a 3 en codigo fijo, que es lo que se prueba
# aqui; al terminar vuelven al modo que tuvieran.
$modosAntes = @()
if ("$(Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'laboratorios' AND column_name = 'modo_codigo';")" -eq '1') {
    $modosAntes = @(Sql "SELECT CONCAT(idlaboratorios, '=', modo_codigo) FROM laboratorios WHERE idlaboratorios IN (1, 2, 3);")
    Sql "UPDATE laboratorios SET modo_codigo = 'fijo' WHERE idlaboratorios IN (1, 2, 3);" | Out-Null
}
Sql @"
INSERT INTO clases (idclases, usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en) VALUES
 (901, 3, 1, 2, 'TST01', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, NULL, NOW()),
 (902, 4, 2, 3, 'TST02', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, NULL, NOW()),
 (903, 3, 3, 1, 'TSTEX', CURDATE() - INTERVAL 180 DAY, CURDATE() - INTERVAL 30 DAY,  1, NULL, NOW()),
 (904, 3, 1, 3, 'TSTFU', CURDATE() + INTERVAL 30 DAY,  CURDATE() + INTERVAL 150 DAY, 1, NULL, NOW()),
 (905, 4, 4, 4, 'TSTNO', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, NULL, NOW());
INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES
 (901,1,1,'00:00:00','23:59:59'),(901,1,2,'00:00:00','23:59:59'),(901,1,3,'00:00:00','23:59:59'),(901,1,4,'00:00:00','23:59:59'),(901,1,5,'00:00:00','23:59:59'),(901,1,6,'00:00:00','23:59:59'),(901,1,7,'00:00:00','23:59:59'),
 (902,2,1,'00:00:00','23:59:59'),(902,2,2,'00:00:00','23:59:59'),(902,2,3,'00:00:00','23:59:59'),(902,2,4,'00:00:00','23:59:59'),(902,2,5,'00:00:00','23:59:59'),(902,2,6,'00:00:00','23:59:59'),(902,2,7,'00:00:00','23:59:59');
"@ | Out-Null

$dia = (((Get-Date).DayOfWeek.value__ + 6) % 7) + 1

Write-Host "`n== Resolucion de codigos (lab 1 = Computo 1, estudiante 1 = Ana ISC-5001, estudiante 2 = Luis ISC-5002) ==" -ForegroundColor Cyan
$r1 = Esperar 'TST01 en su lab'            'TST01' 1 1 'Aceptado'
$r2 = Esperar 'TST01 otra vez, misma sesion' 'TST01' 1 1 'Aceptado'
$script:total++; if ($r1.Ventana.IdCodigo -eq $r2.Ventana.IdCodigo) { Write-Host "[OK ] la sesion se reutiliza (id $($r1.Ventana.IdCodigo))" } else { $script:fallos++; Write-Host "[FALLO] se crearon dos sesiones" }
Esperar 'TST02 desde el lab 1'          'TST02' 1 1 'OtroLaboratorio' | Out-Null
Esperar 'TSTEX (vigencia vencida)'      'TSTEX' 1 1 'FueraDePeriodo' | Out-Null
Esperar 'TSTFU (aun no vigente)'        'TSTFU' 1 1 'FueraDePeriodo' | Out-Null
Esperar 'TSTNO (sin franjas hoy)'       'TSTNO' 1 1 'HoyNoHayClase' | Out-Null
Esperar 'codigo inexistente'            'ZZZZZ' 1 1 'NoExiste' | Out-Null
Esperar 'codigo en minusculas/espacios' ' tst01 ' 1 1 'Aceptado' | Out-Null

Write-Host "`n== Excepciones ==" -ForegroundColor Cyan
Sql "INSERT INTO horario_excepciones (tipo, fecha, horarios_idhorarios, motivo, creada_en) SELECT 'cancelada', CURDATE(), idhorarios, 'PRUEBA cancelada', NOW() FROM horarios WHERE clases_idclases = 901 AND dia_semana = $dia;" | Out-Null
Esperar 'TST01 con la franja de hoy cancelada' 'TST01' 1 1 'Cancelada' | Out-Null
Sql "DELETE FROM horario_excepciones WHERE motivo = 'PRUEBA cancelada';" | Out-Null
Sql "INSERT INTO horario_excepciones (tipo, fecha, clases_idclases, laboratorios_idlaboratorios, hora_inicio, hora_fin, motivo, creada_en) VALUES ('extra', CURDATE(), 905, 1, SUBTIME(CURTIME(), '01:00:00'), ADDTIME(CURTIME(), '01:00:00'), 'PRUEBA extra', NOW());" | Out-Null
$rx = Esperar 'TSTNO con sesion extra ahora en lab 1' 'TSTNO' 1 2 'Aceptado'
$script:total++; $esExtra = Sql "SELECT horario_excepciones_id IS NOT NULL FROM codigos_accesos WHERE idcodigos_accesos = $($rx.Ventana.IdCodigo);"; if ("$esExtra" -eq '1') { Write-Host '[OK ] la sesion quedo ligada a la excepcion extra' } else { $script:fallos++; Write-Host "[FALLO] sesion sin excepcion ($esExtra)" }
Sql "DELETE FROM horario_excepciones WHERE motivo = 'PRUEBA extra';" | Out-Null

Write-Host "`n== Antes / despues de la franja ==" -ForegroundColor Cyan
Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES (905, 3, $dia, ADDTIME(CURTIME(), '01:00:00'), ADDTIME(CURTIME(), '02:00:00'));" | Out-Null
Esperar 'TSTNO en lab 3, franja dentro de 1 h' 'TSTNO' 3 1 'AunNoEmpieza' | Out-Null
Sql "UPDATE horarios SET hora_inicio = SUBTIME(CURTIME(), '03:00:00'), hora_fin = SUBTIME(CURTIME(), '02:00:00') WHERE clases_idclases = 905;" | Out-Null
Esperar 'TSTNO en lab 3, franja terminada hace 2 h' 'TSTNO' 3 1 'Expirado' | Out-Null
Sql "UPDATE horarios SET hora_inicio = SUBTIME(CURTIME(), '00:10:00'), hora_fin = ADDTIME(CURTIME(), '00:50:00') WHERE clases_idclases = 905;" | Out-Null
Esperar 'TSTNO en lab 3, dentro del margen de 15 min' 'TSTNO' 3 1 'Aceptado' | Out-Null
Sql "DELETE FROM codigos_accesos WHERE horarios_idhorarios IN (SELECT idhorarios FROM horarios WHERE clases_idclases = 905); DELETE FROM horarios WHERE clases_idclases = 905;" | Out-Null

# Con db/20 una franja cambiada "desde una fecha" no se borra: se le pone fin y
# la nueva empieza ese dia. Nada de lo que decide si una franja se da debe ver
# la terminada, y la terminada no choca con lo que se ponga despues en su hueco.
if ("$(Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'horarios' AND column_name = 'vigente_hasta';")" -eq '1') {
    Write-Host "`n== Vigencia propia de la franja (db/20) ==" -ForegroundColor Cyan
    Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin, vigente_hasta) VALUES (905, 3, $dia, SUBTIME(CURTIME(), '00:10:00'), ADDTIME(CURTIME(), '00:50:00'), CURDATE() - INTERVAL 1 DAY);" | Out-Null
    $vieja = [int]"$(Sql 'SELECT MAX(idhorarios) FROM horarios WHERE clases_idclases = 905;')"
    Esperar 'TSTNO en lab 3, su franja de hoy termino ayer' 'TSTNO' 3 1 'HoyNoHayClase' | Out-Null
    Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin, vigente_desde) VALUES (905, 3, $dia, SUBTIME(CURTIME(), '00:10:00'), ADDTIME(CURTIME(), '00:50:00'), CURDATE());" | Out-Null
    $nueva = [int]"$(Sql 'SELECT MAX(idhorarios) FROM horarios WHERE clases_idclases = 905;')"
    $rv = Esperar 'TSTNO en lab 3, con la franja que la sustituye desde hoy' 'TSTNO' 3 1 'Aceptado'
    $enlace = if ($rv.Ventana) { "$(Sql "SELECT horarios_idhorarios FROM codigos_accesos WHERE idcodigos_accesos = $($rv.Ventana.IdCodigo);")" } else { '' }
    $script:total++; if ($enlace -eq "$nueva") { Write-Host "[OK ] la sesion de hoy es de la nueva ($nueva), no de la terminada ($vieja)" } else { $script:fallos++; Write-Host "[FALLO] sesion enlazada a '$enlace' (nueva $nueva, vieja $vieja)" }
    $agenda3 = @($mAgenda.Invoke($h, [object[]]@([int]3, [DateTime]::Today)) | Where-Object { $_.Codigo -eq 'TSTNO' })
    $script:total++; if ($agenda3.Count -eq 1 -and $agenda3[0].FranjaId -eq $nueva) { Write-Host '[OK ] la agenda de hoy trae solo la nueva' } else { $script:fallos++; Write-Host "[FALLO] agenda con $($agenda3.Count) TSTNO: $(($agenda3 | ForEach-Object { $_.FranjaId }) -join ',')" }
    Sql "DELETE FROM codigos_accesos WHERE horarios_idhorarios IN ($vieja, $nueva); DELETE FROM horarios WHERE clases_idclases = 905;" | Out-Null

    # Validacion: TSTFU (904, empieza dentro de 30 dias) en el hueco de una franja de
    # TSTNO que termino ayer, de 21:00 a 21:30 (fuera del horario del seed). Sin TST01,
    # que ocupa todo el dia a su mismo profesor y grupo.
    Sql "UPDATE clases SET activa = 0 WHERE idclases = 901; INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin, vigente_hasta) VALUES (905, 3, $dia, '21:00:00', '21:30:00', CURDATE() - INTERVAL 1 DAY);" | Out-Null
    $conf = $mValidarFranja.Invoke($h, [object[]]@($null, 904, 3, $dia, [TimeSpan]'21:00', [TimeSpan]'21:30'))
    $script:total++; if ($conf -eq $null) { Write-Host '[OK ] la franja terminada no choca con una clase que empieza despues en su hueco' } else { $script:fallos++; Write-Host "[FALLO] conflicto con la terminada: $conf" }
    Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin, vigente_desde) VALUES (905, 3, $dia, '21:00:00', '21:30:00', CURDATE());" | Out-Null
    $conf = $mValidarFranja.Invoke($h, [object[]]@($null, 904, 3, $dia, [TimeSpan]'21:00', [TimeSpan]'21:30'))
    Sql "UPDATE clases SET activa = 1 WHERE idclases = 901; DELETE FROM horarios WHERE clases_idclases = 905;" | Out-Null
    $script:total++; if ($conf -and $conf -match 'mismo laboratorio') { Write-Host "[OK ] la que la sustituye si choca: $conf" } else { $script:fallos++; Write-Host "[FALLO] sin conflicto con la que la sustituye: $conf" }
}

Write-Host "`n== Bitacora y marca 'con falla' ==" -ForegroundColor Cyan
$ses = $r1.Ventana.IdCodigo
# Luis reporta una falla de red; Ana no reporta nada.
Sql "INSERT INTO registros_bitacoras (fk_codigo_accesos, fk_usuario, numero_computadora, falla_red, comentarios_red, falla_hardware, comentarios_hardware, falla_software, comentarios_software) VALUES ($ses, 2, 'PC-PRUEBA', 'Otro', 'Sin acceso a internet', 'Ninguno', 'Ninguno', 'Ninguno', '');" | Out-Null
Esperar 'Luis ya registro en TST01' 'TST01' 1 2 'YaRegistrado' | Out-Null
Sql "INSERT INTO registros_bitacoras (fk_codigo_accesos, fk_usuario, numero_computadora, falla_red, comentarios_red, falla_hardware, comentarios_hardware, falla_software, comentarios_software) VALUES ($ses, 1, 'PC-PRUEBA', 'Ninguno ', 'Ninguno ', 'Ninguno ', 'Ninguno ', ' Ninguno ', '');" | Out-Null
$tabla = $mRegistro.Invoke($c, [object[]]@($ses))
$luis = @($tabla.Select("nombre = 'Luis'"))[0]
$ana = @($tabla.Select("nombre = 'Ana'"))[0]
$script:total++
if ([int]$luis['con_falla'] -eq 1) { Write-Host "[OK ] $($luis['nombre_completo']) con falla: se resalta ('$($luis['comentarios_red'])')" } else { $script:fallos++; Write-Host "[FALLO] con_falla = $($luis['con_falla'])" }
$script:total++
if ([int]$ana['con_falla'] -eq 0) { Write-Host '[OK ] Ana, que marco Ninguno en todo, no se resalta' } else { $script:fallos++; Write-Host "[FALLO] Ana marcada: $($ana['con_falla'])" }

Write-Host "`n== Validacion de franjas ==" -ForegroundColor Cyan
$conf = $mValidarFranja.Invoke($h, [object[]]@($null, 902, 1, $dia, [TimeSpan]'10:00', [TimeSpan]'12:00'))
$script:total++; if ($conf -and $conf -match 'mismo laboratorio') { Write-Host "[OK ] TST02 en lab 1: $conf" } else { $script:fallos++; Write-Host "[FALLO] sin conflicto: $conf" }
$conf = $mValidarFranja.Invoke($h, [object[]]@($null, 901, 3, $dia, [TimeSpan]'10:00', [TimeSpan]'12:00'))
$script:total++; if ($conf -and $conf -match 'misma persona docente') { Write-Host "[OK ] TST01 en lab 3: $conf" } else { $script:fallos++; Write-Host "[FALLO] sin conflicto: $conf" }
Sql "UPDATE clases SET activa = 0 WHERE idclases = 902;" | Out-Null
$conf = $mValidarFranja.Invoke($h, [object[]]@($null, 905, 3, $dia, [TimeSpan]'20:00', [TimeSpan]'21:00'))
Sql "UPDATE clases SET activa = 1 WHERE idclases = 902;" | Out-Null
$script:total++; if ($conf -eq $null) { Write-Host '[OK ] TSTNO en lab 3 a las 20:00: sin conflicto' } else { $script:fallos++; Write-Host "[FALLO] conflicto inesperado: $conf" }
$conf = $mValidarFranja.Invoke($h, [object[]]@($null, 905, 3, $dia, [TimeSpan]'12:00', [TimeSpan]'10:00'))
$script:total++; if ($conf -match 'posterior') { Write-Host "[OK ] horas invertidas: $conf" } else { $script:fallos++; Write-Host "[FALLO] $conf" }

Write-Host "`n== Agenda y clase en curso ==" -ForegroundColor Cyan
$agenda = $mAgenda.Invoke($h, [object[]]@([int]1, [DateTime]::Today))
$script:total++; if (@($agenda | Where-Object { $_.Codigo -eq 'TST01' }).Count -eq 1) { Write-Host "[OK ] agenda lab 1 hoy incluye TST01 ($($agenda.Count) entradas en total)" } else { $script:fallos++; Write-Host "[FALLO] agenda sin TST01" }
$args = [object[]]@(1, $null)
$enCurso = $mClaseEnCurso.Invoke($h, $args)
$script:total++; if ($enCurso -ne $null -and $enCurso.Codigo -eq 'TST01') { Write-Host "[OK ] clase en curso lab 1: $($enCurso.Descripcion) (servidor: $($args[1]))" } else { $script:fallos++; Write-Host "[FALLO] clase en curso: $(if ($enCurso) { $enCurso.Codigo } else { 'ninguna' })" }
$clases = $mClases.Invoke($h, [object[]]@($false))
$script:total++; $tst = $clases | Where-Object { $_.Codigo -eq 'TST01' }; if ($clases.Count -ge 5 -and $tst -and $tst.Sesiones -eq 1) { Write-Host "[OK ] ObtenerClases: $($clases.Count) clases; TST01 tiene $($tst.Sesiones) sesion" } else { $script:fallos++; Write-Host "[FALLO] clases: $($clases.Count)" }

LimpiarPruebas
foreach ($m in $modosAntes) {
    if ("$m" -match '^(\d+)=(fijo|renovable|sin_codigo)$') { Sql "UPDATE laboratorios SET modo_codigo = '$($matches[2])' WHERE idlaboratorios = $($matches[1]);" | Out-Null }
}
Write-Host "`nResultado: $($total - $fallos)/$total correctas" -ForegroundColor $(if ($fallos -eq 0) { 'Green' } else { 'Red' })
