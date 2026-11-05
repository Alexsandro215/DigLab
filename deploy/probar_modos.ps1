<#
.SYNOPSIS
    Prueba los tres modos de acceso a las clases (db/11) contra una base de
    DESARROLLO.

.DESCRIPTION
    Carga DigLab.exe por reflexion y recorre el codigo unico de cada profesor,
    la generacion de codigos (uno por clase y dia, generado al empezar la
    semana: lo ve el administrador, el profesor lo recibe al abrir su clase y
    el alumno entra con el aunque nadie la haya abierto) y sin codigos (el
    alumno elige su clase), ademas de la cola de bitacoras sin conexion,
    corregir una clase con sesiones vacias, cambiar una franja con sesion (en
    su sitio y, con db/20 y la casilla de Ajustes encendida, desde hoy; la
    casilla vuelve a como estaba) y la copia local del horario. Crea
    sus propios laboratorios ('TST Modos X' e
    'Y'), profesores (TSTPROFA, TSTPROFB), un alumno (TSTALUM1), clases y
    franjas relativas a la hora del servidor, y lo BORRA todo al terminar.
    Necesita una base con db\01_schema.sql (o migrada hasta la 13) con algun
    grupo y alguna materia, y la hora del servidor entre 01:00 y 19:00.
    MODIFICA la base: no lo ejecutes contra la base real del TESCHI.

.EXAMPLE
    .\probar_modos.ps1 -CadenaConexion 'Database=teschi_otru;Server=127.0.0.1;Port=3307;User Id=root;Password='
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CadenaConexion,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Otro DigLab.exe; por defecto, el de DigLab\bin\<Configuration>.
    [string]$Exe
)

$ErrorActionPreference = 'Stop'
if ($CadenaConexion -match 'teschi\.edu\.mx') { throw 'Esta prueba escribe en la base: nunca contra la de la escuela.' }
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot "..\DigLab\bin\$Configuration\DigLab.exe" }
$Exe = (Resolve-Path $Exe).Path
$script:carpeta = Split-Path $Exe

$env:DIGLAB_CONNECTION_STRING = $CadenaConexion
# La copia local del horario y el log de la prueba, fuera de la carpeta de datos real.
$env:DIGLAB_CARPETA_DATOS = Join-Path $env:TEMP 'diglab_probar_modos'
New-Item -ItemType Directory -Force $env:DIGLAB_CARPETA_DATOS | Out-Null

[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($origen, $pedido)
    $ruta = Join-Path $script:carpeta ((New-Object Reflection.AssemblyName($pedido.Name)).Name + '.dll')
    if (Test-Path $ruta) { return [Reflection.Assembly]::LoadFrom($ruta) }
    return $null
})
$app = [Reflection.Assembly]::LoadFrom($Exe)
$null = [Reflection.Assembly]::LoadFrom((Join-Path $script:carpeta 'MySql.Data.dll'))
$bf = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
function T([string]$n) { $app.GetType($n, $true) }
$tHd = T 'DigLab.conexion.HorariosDatos'
$hd = [Activator]::CreateInstance($tHd, $true)
function M([string]$nombre) { $tHd.GetMethod($nombre, $bf) }

$script:total = 0
$script:malos = 0
function Ver([string]$que, [bool]$bien) {
    $script:total++
    if (-not $bien) { $script:malos++ }
    Write-Host ($(if ($bien) { '[OK ] ' } else { '[FALLO] ' }) + $que) -ForegroundColor $(if ($bien) { 'Gray' } else { 'Red' })
}

# SQL directo, con la misma zona horaria que fija la aplicacion.
function Sql([string]$texto) {
    $c = New-Object MySql.Data.MySqlClient.MySqlConnection($CadenaConexion)
    $c.Open()
    try {
        $z = $c.CreateCommand(); $z.CommandText = "SET time_zone = '-06:00'"; $null = $z.ExecuteNonQuery()
        $cmd = $c.CreateCommand(); $cmd.CommandText = $texto
        $t = New-Object System.Data.DataTable
        $r = $cmd.ExecuteReader(); $t.Load($r); $r.Close()
        return ,$t
    } finally { $c.Close() }
}
function Uno([string]$texto) { $t = Sql $texto; if ($t.Rows.Count -eq 0) { return $null }; return $t.Rows[0][0] }

if ([int](Uno "SELECT COUNT(1) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'laboratorios' AND column_name = 'modo_codigo'") -eq 0) {
    throw 'La base no tiene db/11_modo_de_acceso.sql (laboratorios.modo_codigo).'
}
if ([int](Uno "SELECT COUNT(1) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'configuracion' AND column_name = 'valor'") -eq 0) {
    throw 'La base no tiene db/13 (tabla configuracion): sin el secreto no hay codigos generados.'
}
$hora = [TimeSpan](Uno "SELECT CURTIME()")
if ($hora -lt [TimeSpan]'01:00:00' -or $hora -gt [TimeSpan]'19:00:00') { throw "La prueba necesita la hora del servidor entre 01:00 y 19:00 (es $hora)." }
Write-Host "Hora del servidor (-06:00): $hora"

# "Los cambios de horario aplican desde una fecha" (db/20, configuracion
# franjas_con_fecha): la prueba lo pone a su gusto y al final lo deja como estaba.
$script:conFechaFila = [int](Uno "SELECT COUNT(1) FROM configuracion WHERE clave = 'franjas_con_fecha'")
$script:conFechaValor = Uno "SELECT valor FROM configuracion WHERE clave = 'franjas_con_fecha'"
function ConFecha([string]$valor) {
    $null = Sql "INSERT INTO configuracion (clave, valor) VALUES ('franjas_con_fecha', '$valor') ON DUPLICATE KEY UPDATE valor = VALUES(valor)"
}
function DevolverConFecha {
    if ($script:conFechaFila -eq 0) { $null = Sql "DELETE FROM configuracion WHERE clave = 'franjas_con_fecha'" }
    elseif ($script:conFechaValor -is [DBNull] -or $null -eq $script:conFechaValor) { $null = Sql "UPDATE configuracion SET valor = NULL WHERE clave = 'franjas_con_fecha'" }
    else { ConFecha ([string]$script:conFechaValor) }
}

function Limpiar {
    # Con db/21, lo que la prueba cambia con las funciones de DigLab queda en el
    # historial de cambios: se borra con lo demas (solo lo de sus laboratorios y clases).
    if ([int](Uno "SELECT COUNT(1) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'historial_cambios'") -gt 0) {
        $null = Sql @"
DELETE FROM historial_cambios
 WHERE laboratorio_id IN (SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio LIKE 'TST Modos %')
    OR (tabla = 'laboratorios' AND registro_id IN (SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio LIKE 'TST Modos %'))
    OR (tabla = 'horarios' AND registro_id IN (SELECT h.idhorarios FROM horarios h INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios WHERE l.nombre_laboratorio LIKE 'TST Modos %'))
    OR (tabla = 'clases' AND registro_id IN (SELECT c.idclases FROM clases c INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios WHERE u.numero_identificador IN ('TSTPROFA','TSTPROFB','TSTPROFC')));
"@
    }
    $null = Sql @"
DELETE rb FROM registros_bitacoras rb INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos
 INNER JOIN laboratorios l ON l.idlaboratorios = ca.laboratorios_idlaboratorios WHERE l.nombre_laboratorio LIKE 'TST Modos %';
DELETE ca FROM codigos_accesos ca INNER JOIN laboratorios l ON l.idlaboratorios = ca.laboratorios_idlaboratorios WHERE l.nombre_laboratorio LIKE 'TST Modos %';
DELETE x FROM horario_excepciones x INNER JOIN laboratorios l ON l.idlaboratorios = x.laboratorios_idlaboratorios WHERE l.nombre_laboratorio LIKE 'TST Modos %';
DELETE x FROM horario_excepciones x INNER JOIN horarios h ON h.idhorarios = x.horarios_idhorarios
 INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios WHERE l.nombre_laboratorio LIKE 'TST Modos %';
DELETE h FROM horarios h INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios WHERE l.nombre_laboratorio LIKE 'TST Modos %';
DELETE c FROM clases c INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios WHERE u.numero_identificador IN ('TSTPROFA','TSTPROFB','TSTPROFC');
DELETE FROM usuarios WHERE numero_identificador IN ('TSTPROFA','TSTPROFB','TSTPROFC','TSTALUM1');
DELETE FROM grupos WHERE nombre_grupo = 'TST-MODOS';
DELETE FROM laboratorios WHERE nombre_laboratorio LIKE 'TST Modos %';
"@
}
Limpiar

# --- Datos de prueba ---------------------------------------------------------
$null = Sql @"
INSERT INTO laboratorios (nombre_laboratorio) VALUES ('TST Modos X'), ('TST Modos Y');
INSERT INTO usuarios (numero_identificador, nombre, apellido_paterno, apellido_materno, password, fk_tipo_usuario, codigo_clase)
VALUES ('TSTPROFA','Ana','Modos','Prueba','x',2,'TSTCA'), ('TSTPROFB','Beto','Modos','Prueba','x',2,'TSTCB');
INSERT INTO usuarios (numero_identificador, nombre, apellido_paterno, apellido_materno, password, fk_tipo_usuario)
VALUES ('TSTALUM1','Alumno','Modos','Prueba','x',1);
"@
$X = [int](Uno "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio = 'TST Modos X'")
$Y = [int](Uno "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio = 'TST Modos Y'")
$profA = [int](Uno "SELECT idusuarios FROM usuarios WHERE numero_identificador = 'TSTPROFA'")
$profB = [int](Uno "SELECT idusuarios FROM usuarios WHERE numero_identificador = 'TSTPROFB'")
$alumno = [int](Uno "SELECT idusuarios FROM usuarios WHERE numero_identificador = 'TSTALUM1'")
$grupo = [int](Uno "SELECT MIN(idgrupos) FROM grupos")
$grupo2 = [int](Uno "SELECT MAX(idgrupos) FROM grupos")
$materia = [int](Uno "SELECT MIN(id_materia) FROM materias")
$null = Sql @"
INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_en)
VALUES ($profA, $grupo, $materia, 'TSTCA', CURDATE() - INTERVAL 30 DAY, CURDATE() + INTERVAL 30 DAY, 1, NOW()),
       ($profB, $grupo, $materia, 'TSTCB', CURDATE() - INTERVAL 30 DAY, CURDATE() + INTERVAL 30 DAY, 1, NOW()),
       ($profA, $grupo2, $materia, 'TSTCA', CURDATE() - INTERVAL 30 DAY, CURDATE() + INTERVAL 30 DAY, 1, NOW());
"@
$clases = Sql "SELECT idclases FROM clases WHERE codigo IN ('TSTCA','TSTCB') ORDER BY idclases"
$cA = [int]$clases.Rows[0][0]; $cB = [int]$clases.Rows[1][0]; $cC = [int]$clases.Rows[2][0]
# FA: de A, abierta (empezo hace 30 min); FB: de B, abierta (empieza en 10 min, dentro del margen);
# FC: de A, dentro de 3 horas (cerrada).
$null = Sql @"
INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES
 ($cA, $X, WEEKDAY(CURDATE()) + 1, SUBTIME(CURTIME(), '00:30:00'), ADDTIME(CURTIME(), '01:00:00')),
 ($cB, $X, WEEKDAY(CURDATE()) + 1, ADDTIME(CURTIME(), '00:10:00'), ADDTIME(CURTIME(), '01:10:00')),
 ($cC, $X, WEEKDAY(CURDATE()) + 1, ADDTIME(CURTIME(), '03:00:00'), ADDTIME(CURTIME(), '04:00:00'));
"@
$fr = Sql "SELECT idhorarios, hora_inicio, hora_fin FROM horarios WHERE laboratorios_idlaboratorios = $X ORDER BY idhorarios"
$FA = [int]$fr.Rows[0][0]; $FB = [int]$fr.Rows[1][0]; $FC = [int]$fr.Rows[2][0]
Write-Host "Laboratorios X=$X Y=$Y; docentes A=$profA B=$profB; estudiante $alumno; franjas A=$FA B=$FB C=$FC"

function Modo([int]$lab, [string]$modo) { $null = Sql "UPDATE laboratorios SET modo_codigo = '$modo' WHERE idlaboratorios = $lab" }
function Resolver([string]$codigo, [int]$lab) { (M 'ResolverCodigo').Invoke($hd, @($codigo, $lab, $alumno, $null)) }
# Los campos int? y TimeSpan se ponen por reflexion: el adaptador de PowerShell no sabe.
function Poner($objeto, [string]$campo, $valor) { $objeto.GetType().GetField($campo).SetValue($objeto, $valor) }
function Entrada([int]$franja, [int]$prof, [int]$clase) {
    $e = [Activator]::CreateInstance((T 'DigLab.conexion.SesionAgenda'))
    $f = $fr.Rows | Where-Object { [int]$_[0] -eq $franja } | Select-Object -First 1
    Poner $e 'EsExtra' $false; Poner $e 'FranjaId' ([int]$franja); Poner $e 'ProfesorId' $prof; Poner $e 'ClaseId' $clase
    Poner $e 'LaboratorioId' $X; Poner $e 'Inicio' ([TimeSpan]$f[1]); Poner $e 'Fin' ([TimeSpan]$f[2])
    return $e
}
function Sesiones([int]$franja) { [int](Uno "SELECT COUNT(1) FROM codigos_accesos WHERE horarios_idhorarios = $franja AND fecha = CURDATE()") }
function CodigoDe([int]$franja) { [string](Uno "SELECT codigo FROM codigos_accesos WHERE horarios_idhorarios = $franja AND fecha = CURDATE() ORDER BY idcodigos_accesos LIMIT 1") }

Push-Location $script:carpeta
try {
    Write-Host "`n== Codigo fijo ==" -ForegroundColor Cyan
    Modo $X 'fijo'
    $r = Resolver 'tstca' $X
    Ver ("'tstca' (en minusculas) entra en la clase de A: " + $r.Motivo) ($r.Motivo.ToString() -eq 'Aceptado')
    Ver ("la sesion se crea con el codigo como esta guardado ('" + (CodigoDe $FA) + "')") ((CodigoDe $FA) -ceq 'TSTCA')

    Write-Host "`n== Generacion de codigos ==" -ForegroundColor Cyan
    Modo $X 'renovable'
    $r = Resolver 'TSTCA' $X
    Ver ("la clase de A, ya abierta con el codigo fijo, lo conserva hasta su fin: " + $r.Motivo) ($r.Motivo.ToString() -eq 'Aceptado')
    $r = Resolver 'TSTCB' $X
    Ver ("el codigo fijo de B (su clase aun sin abrir) se explica: " + $r.Motivo + " - " + $r.Mensaje) ($r.Motivo.ToString() -eq 'CodigoFijo')
    # Los codigos de esta semana que ve el administrador (aqui se crea el secreto si no hay).
    $hoy = [DateTime](Uno "SELECT CURDATE()")
    $lunes = $hoy.AddDays(-((([int]$hoy.DayOfWeek) + 6) % 7))
    $semana = (M 'CodigosGeneradosDe').Invoke($hd, @($X, $lunes, $lunes.AddDays(6)))
    $generadoB = [string]$semana.DeFranja($FB, $hoy)
    # Pidiendo las dos semanas: la que viene aun no tiene ninguno (se generan su lunes).
    $dos = (M 'CodigosGeneradosDe').Invoke($hd, @($X, $lunes, $lunes.AddDays(13)))
    Ver ("la administracion ve el codigo generado de hoy de la clase de B ('" + $generadoB + "'), y nada de la semana que viene") ($generadoB -match '^[0-9]{5}$' -and [string]$dos.DeFranja($FB, $hoy) -eq $generadoB -and [string]::IsNullOrEmpty($dos.DeFranja($FB, $hoy.AddDays(7))) -and -not $dos.Generados($lunes.AddDays(7)))
    $malo = '55555'; if ($malo -eq $generadoB -or $malo -eq [string]$semana.DeFranja($FC, $hoy)) { $malo = '55556' }
    $r = Resolver $malo $X
    # La de A ya esta abierta (tiene sesion) y la de B aun no: puede ser un alumno de
    # A que se equivoco al teclear, asi que el mensaje dice las dos cosas.
    Ver ("un codigo cualquiera con la clase de A abierta y la de B en hora sin abrir: " + $r.Motivo + " - " + $r.Mensaje) ($r.Motivo.ToString() -eq 'SinAbrir' -and $r.Mensaje -match 'revisa')
    $abrir = M 'AbrirSesion'
    $a1 = $abrir.Invoke($hd, @((Entrada $FB $profB $cB), $profB))
    $a2 = $abrir.Invoke($hd, @((Entrada $FB $profB $cB), $profB))
    Ver ("B abre su clase con su codigo generado: '" + $a1.Codigo + "', sesion " + $a1.SesionId + "; otra vez, la misma (" + $a2.SesionId + ")") ($a1.Abierta -and $a1.Codigo -eq $generadoB -and $a2.SesionId -eq $a1.SesionId -and $a2.Codigo -eq $a1.Codigo)
    $a3 = $abrir.Invoke($hd, @((Entrada $FB $profB $cB), $profA))
    Ver ("A no recibe el codigo de la clase de B: '" + $a3.Motivo + "'") ((-not $a3.Abierta) -and [string]::IsNullOrEmpty($a3.Codigo))
    $antes = Sesiones $FB
    $r = Resolver $a1.Codigo $X
    Ver ("el codigo de la sesion entra (" + $r.Motivo + ") y no crea sesiones (" + $antes + " -> " + (Sesiones $FB) + ")") ($r.Motivo.ToString() -eq 'Aceptado' -and (Sesiones $FB) -eq $antes -and $r.Ventana.Codigo -eq $a1.Codigo)
    $r = Resolver $a1.Codigo $Y
    Ver ("desde otro laboratorio: " + $r.Motivo + " - " + $r.Mensaje) ($r.Motivo.ToString() -eq 'OtroLaboratorio')
    $a4 = $abrir.Invoke($hd, @((Entrada $FC $profA $cC), $profA))
    Ver ("la clase de dentro de 3 horas no se abre: '" + $a4.Motivo + "'") (-not $a4.Abierta)
    $admin = $abrir.Invoke($hd, @((Entrada $FB $profB $cB), 0))
    Ver ("la administracion (0) ve el codigo de la clase de B: '" + $admin.Codigo + "'") ($admin.Abierta -and $admin.Codigo -eq $a1.Codigo)

    # El panel de B abre solo su clase (se borra antes su sesion, que no tiene bitacoras).
    $null = Sql "DELETE FROM codigos_accesos WHERE horarios_idhorarios = $FB AND fecha = CURDATE()"
    $p = (M 'PanoramaDeProfesor').Invoke($hd, @('TSTPROFB'))
    (M 'AbrirClasesDeHoy').Invoke($hd, @($p))
    $suya = $p.Hoy | Where-Object { $_.FranjaId -eq $FB } | Select-Object -First 1
    Ver ("el panel de B abre su clase al refrescarse, con el mismo codigo generado: '" + $suya.CodigoSesion + "', aviso '" + $p.AvisoApertura + "'") ($suya.SesionId -ne $null -and $suya.CodigoSesion -eq $generadoB -and $p.AvisoApertura -eq $null -and (CodigoDe $FB) -eq $suya.CodigoSesion)
    $codigoB = $suya.CodigoSesion

    # El codigo generado vale desde el lunes aunque nadie haya abierto la clase (se
    # lo pudo dar el administrador): el alumno entra y la sesion se abre con el.
    $null = Sql "DELETE FROM codigos_accesos WHERE horarios_idhorarios = $FB AND fecha = CURDATE()"
    $r = Resolver $generadoB $X
    Ver ("un estudiante teclea el codigo generado de la clase de B sin abrir: " + $r.Motivo + ", y la sesion queda con el ('" + (CodigoDe $FB) + "')") ($r.Motivo.ToString() -eq 'Aceptado' -and (CodigoDe $FB) -eq $generadoB)

    Write-Host "`n== Sin codigo ==" -ForegroundColor Cyan
    Modo $X 'sin_codigo'
    $sit = (M 'SituacionDelLaboratorio').Invoke($hd, @($X))
    $conSesion = @($sit.Agenda | Where-Object { $_.SesionId -ne $null }).Count
    Ver ("la situacion dice el modo (" + $sit.Modo + ") y trae las 3 clases (" + $sit.Agenda.Count + "), 2 con sesion (" + $conSesion + ")") ($sit.Modo.ToString() -eq 'SinCodigo' -and $sit.Agenda.Count -eq 3 -and $conSesion -eq 2)
    $clase = M 'ResolverClase'
    $r = $clase.Invoke($hd, @($X, $alumno, $null, [Nullable[int]]$FB, $null))
    Ver ("elegir la clase de B entra en su sesion ya abierta (" + $r.Motivo + ", codigo " + $r.Ventana.Codigo + ")") ($r.Motivo.ToString() -eq 'Aceptado' -and $r.Ventana.Codigo -eq $codigoB)
    $r = $clase.Invoke($hd, @($X, $alumno, $null, [Nullable[int]]$FC, $null))
    Ver ("elegir la de dentro de 3 horas ofrece las abiertas: " + $r.Motivo + " (" + @($r.Opciones).Count + " opciones)") ($r.Motivo.ToString() -eq 'ElegirClase' -and @($r.Opciones).Count -eq 2)
    $null = Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES ($cC, $X, WEEKDAY(CURDATE()) + 1, SUBTIME(CURTIME(), '00:05:00'), ADDTIME(CURTIME(), '00:20:00'))"
    $FD = [int](Uno "SELECT MAX(idhorarios) FROM horarios WHERE laboratorios_idlaboratorios = $X")
    $r = $clase.Invoke($hd, @($X, $alumno, $null, [Nullable[int]]$FD, $null))
    $codigoD = CodigoDe $FD
    Ver ("una clase sin sesion la crea con un codigo aleatorio ('" + $codigoD + "'), nunca el fijo") ($r.Motivo.ToString() -eq 'Aceptado' -and $codigoD -match '^[0-9]{5}$' -and $codigoD -ne 'TSTCA')
    Modo $X 'fijo'
    $r = $clase.Invoke($hd, @($X, $alumno, $null, [Nullable[int]]$FB, $null))
    Ver ("si el laboratorio volvio al codigo fijo: " + $r.Motivo) ($r.Motivo.ToString() -eq 'ModoCambiado')

    Write-Host "`n== De vuelta al codigo fijo ==" -ForegroundColor Cyan
    $r = Resolver $codigoB $X
    Ver ("el codigo generado ya dictado de la clase de B sigue entrando hasta su fin: " + $r.Motivo) ($r.Motivo.ToString() -eq 'Aceptado')

    Write-Host "`n== Cola de bitacoras sin conexion ==" -ForegroundColor Cyan
    Modo $X 'renovable'
    $null = Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES ($cB, $X, WEEKDAY(CURDATE()) + 1, SUBTIME(CURTIME(), '00:01:00'), ADDTIME(CURTIME(), '00:30:00'))"
    $FE = [int](Uno "SELECT MAX(idhorarios) FROM horarios WHERE laboratorios_idlaboratorios = $X")
    $sl = [Activator]::CreateInstance((T 'DigLab.conexion.SesionLocal'))
    Poner $sl 'Codigo' 'TSTCB'; Poner $sl 'ClaseId' $cB; Poner $sl 'FranjaId' ([int]$FE); Poner $sl 'Fecha' ([DateTime](Uno "SELECT CURDATE()"))
    Poner $sl 'Inicio' ([TimeSpan](Uno "SELECT hora_inicio FROM horarios WHERE idhorarios = $FE")); Poner $sl 'Fin' ([TimeSpan](Uno "SELECT hora_fin FROM horarios WHERE idhorarios = $FE"))
    Poner $sl 'LaboratorioId' $X; Poner $sl 'MateriaId' $materia; Poner $sl 'GrupoId' $grupo; Poner $sl 'ProfesorId' $profB
    # El que le toca, antes de que exista su sesion (despues, el administrador ve el de la sesion).
    $generadoE = [string](M 'CodigosGeneradosDe').Invoke($hd, @($X, $hoy, $hoy)).DeFranja($FE, $hoy)
    $conexion = [Activator]::CreateInstance((T 'DigLab.Conexion'), $true).GetConexion()
    try { $null = (T 'DigLab.conexion.ColaBitacoras').GetMethod('ObtenerOCrearSesion', $bf).Invoke($null, @($conexion, $sl)) } finally { $conexion.Dispose() }
    $codigoE = CodigoDe $FE
    Ver ("crea la sesion de un laboratorio con generacion de codigos con el generado ('" + $codigoE + "', no 'TSTCB')") ($codigoE -match '^[0-9]{5}$' -and $codigoE -eq $generadoE)

    Write-Host "`n== Sesiones vacias y corregir la clase ==" -ForegroundColor Cyan
    $lista = (M 'ObtenerClases').Invoke($hd, @($false)) | Where-Object { $_.Id -eq $cB }
    Ver ("la clase de B tiene sesiones vacias y cuenta " + $lista.Sesiones + " (solo cuentan las que tienen bitacoras)") ($lista.Sesiones -eq 0)
    (M 'ActualizarClase').Invoke($hd, @($cB, $profB, $grupo2, $materia, 'TSTCB', [DateTime]::Today.AddDays(-30), [DateTime]::Today.AddDays(30)))
    $grupos = (Sql "SELECT DISTINCT grupos_idgrupos FROM codigos_accesos WHERE horarios_idhorarios IN ($FB, $FE) AND fecha = CURDATE()").Rows | ForEach-Object { [int]$_[0] }
    Ver ("al corregir el grupo de la clase de B, sus sesiones vacias de hoy toman el nuevo (" + ($grupos -join ',') + ")") (@($grupos).Count -eq 1 -and $grupos[0] -eq $grupo2)

    Write-Host "`n== Codigo fijo en un laboratorio renovable, sesion vencida, cambio de modo a media clase ==" -ForegroundColor Cyan
    # El codigo fijo de B entra en Y (fijo) y su sesion queda con ese codigo...
    $null = Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES ($cB, $Y, WEEKDAY(CURDATE()) + 1, SUBTIME(CURTIME(), '00:20:00'), ADDTIME(CURTIME(), '00:40:00'))"
    $FY = [int](Uno "SELECT MAX(idhorarios) FROM horarios WHERE laboratorios_idlaboratorios = $Y")
    $r = Resolver 'TSTCB' $Y
    Ver ("en Y (fijo) el codigo fijo de B entra y su sesion lleva ese codigo: " + $r.Motivo + " ('" + (CodigoDe $FY) + "')") ($r.Motivo.ToString() -eq 'Aceptado' -and (CodigoDe $FY) -ceq 'TSTCB')
    # ...y en X (renovable) ese mismo codigo se explica como el fijo, no como "es de otro laboratorio".
    $r = Resolver 'TSTCB' $X
    # Su clase de Y esta abierta ahora: tambien se dice donde sirve (puede que se sentara en el equivocado).
    Ver ("en X (renovable) el codigo fijo que B usa ahora en Y: " + $r.Motivo + " - " + $r.Mensaje) ($r.Motivo.ToString() -eq 'CodigoFijo' -and $r.Mensaje -match 'TST Modos Y')

    # La sesion se creo con otras horas (se cambio la franja despues) y ya termino: manda la sesion.
    $null = Sql "UPDATE codigos_accesos SET hora_salida = SUBTIME(CURTIME(), '00:05:00') WHERE horarios_idhorarios = $FY AND fecha = CURDATE()"
    $r = Resolver 'TSTCB' $Y
    Ver ("con la sesion de hoy ya terminada, aunque la franja siga en hora, no se acepta: " + $r.Motivo + " - " + $r.Mensaje) ($r.Motivo.ToString() -eq 'Expirado')

    # Z pasa de fijo a renovable con una clase ya empezada sin sesion y otra a punto de empezar.
    $null = Sql "INSERT INTO laboratorios (nombre_laboratorio) VALUES ('TST Modos Z')"
    $Z = [int](Uno "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio = 'TST Modos Z'")
    $null = Sql @"
INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES
 ($cA, $Z, WEEKDAY(CURDATE()) + 1, SUBTIME(CURTIME(), '00:10:00'), ADDTIME(CURTIME(), '00:50:00')),
 ($cB, $Z, WEEKDAY(CURDATE()) + 1, ADDTIME(CURTIME(), '00:05:00'), ADDTIME(CURTIME(), '01:05:00'));
"@
    $FZ1 = [int](Uno "SELECT MIN(idhorarios) FROM horarios WHERE laboratorios_idlaboratorios = $Z")
    $FZ2 = [int](Uno "SELECT MAX(idhorarios) FROM horarios WHERE laboratorios_idlaboratorios = $Z")
    $tLab = T 'DigLab.conexion.LaboratoriosDatos'
    $labs = [Activator]::CreateInstance($tLab, $true)
    # Con el objeto de verdad: New-Object lo devuelve envuelto y Invoke no lo convierte.
    $soloZ = [System.Collections.Generic.List[int]]::new()
    $soloZ.Add($Z)
    $argumentos = [object[]]@($soloZ.PSObject.BaseObject, [Enum]::Parse((T 'DigLab.conexion.ModoCodigo'), 'Renovable'))
    $null = $tLab.GetMethod('CambiarModo').Invoke($labs, $argumentos)
    Ver ("al pasar Z a renovable, la clase ya empezada recibe su sesion con el codigo fijo ('" + (CodigoDe $FZ1) + "') y la que aun no empieza no (" + (Sesiones $FZ2) + " sesiones)") ((CodigoDe $FZ1) -ceq 'TSTCA' -and (Sesiones $FZ2) -eq 0)
    $r = Resolver 'TSTCA' $Z
    Ver ("y en ella el codigo fijo sigue entrando hasta que termine: " + $r.Motivo) ($r.Motivo.ToString() -eq 'Aceptado')

    # Si despues se cancela esa clase, se dice que esta cancelada (no "es el codigo fijo").
    $null = Sql "INSERT INTO horario_excepciones (tipo, fecha, horarios_idhorarios, motivo, creada_en) VALUES ('cancelada', CURDATE(), $FZ1, 'TST cancelada', NOW())"
    $r = Resolver 'TSTCA' $Z
    Ver ("cancelada despues, su codigo responde que esta cancelada: " + $r.Motivo + " - " + $r.Mensaje) ($r.Motivo.ToString() -eq 'Cancelada')

    # Una franja que se guarda con su sesion de hoy vacia y con otras horas (las de antes):
    # la sesion toma las de la franja. Laboratorio, profesor y grupo propios, para que
    # la validacion de franjas no choque con los demas datos de prueba.
    $null = Sql @"
INSERT INTO laboratorios (nombre_laboratorio) VALUES ('TST Modos W');
INSERT INTO grupos (nombre_grupo) VALUES ('TST-MODOS');
INSERT INTO usuarios (numero_identificador, nombre, apellido_paterno, apellido_materno, password, fk_tipo_usuario, codigo_clase)
VALUES ('TSTPROFC','Carla','Modos','Prueba','x',2,'TSTCC');
"@
    $W = [int](Uno "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio = 'TST Modos W'")
    $grupoW = [int](Uno "SELECT MAX(idgrupos) FROM grupos WHERE nombre_grupo = 'TST-MODOS'")
    $profC = [int](Uno "SELECT idusuarios FROM usuarios WHERE numero_identificador = 'TSTPROFC'")
    $null = Sql "INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_en) VALUES ($profC, $grupoW, $materia, 'TSTCC', CURDATE() - INTERVAL 30 DAY, CURDATE() + INTERVAL 30 DAY, 1, NOW())"
    $cW = [int](Uno "SELECT idclases FROM clases WHERE codigo = 'TSTCC'")
    $null = Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES ($cW, $W, WEEKDAY(CURDATE()) + 1, SUBTIME(CURTIME(), '00:30:00'), ADDTIME(CURTIME(), '00:30:00'))"
    $FW = [int](Uno "SELECT idhorarios FROM horarios WHERE laboratorios_idlaboratorios = $W")
    $r = Resolver 'TSTCC' $W
    $null = Sql "UPDATE codigos_accesos SET hora_entrada = SUBTIME(CURTIME(), '01:30:00'), hora_salida = SUBTIME(CURTIME(), '00:10:00') WHERE horarios_idhorarios = $FW AND fecha = CURDATE()"
    $antes = Resolver 'TSTCC' $W
    # (No $fw: PowerShell no distingue mayusculas y pisaria $FW.)
    $franjaW = Sql "SELECT hora_inicio, hora_fin, WEEKDAY(CURDATE()) + 1 FROM horarios WHERE idhorarios = $FW"
    $diaW = [int]$franjaW.Rows[0][2]; $iniW = [TimeSpan]$franjaW.Rows[0][0]; $finW = [TimeSpan]$franjaW.Rows[0][1]
    # Con la casilla apagada (como en una base sin db/20) se edita en su sitio, como siempre.
    ConFecha '0'
    $null = (M 'ActualizarFranja').Invoke($hd, [object[]]@($FW, $cW, $W, $diaW, $iniW, $finW))
    $despues = Resolver 'TSTCC' $W
    Ver ("sesion vacia con horas viejas: antes " + $antes.Motivo + ", y al guardar la franja toma sus horas: " + $despues.Motivo) ($r.Motivo.ToString() -eq 'Aceptado' -and $antes.Motivo.ToString() -eq 'Expirado' -and $despues.Motivo.ToString() -eq 'Aceptado')

    # Encendida (db/20): la franja ya tiene sesion, asi que editarla en su sitio se niega
    # y se cambia desde hoy: la vieja termina ayer y la sesion vacia de hoy pasa a la nueva.
    $mDesde = M 'CambiarFranjaDesde'
    if ([int](Uno "SELECT COUNT(1) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'horarios' AND column_name = 'vigente_hasta'") -eq 1 -and $mDesde) {
        ConFecha '1'
        $rechazo = $null
        try { $null = (M 'ActualizarFranja').Invoke($hd, [object[]]@($FW, $cW, $W, $diaW, $iniW, $finW)) } catch { $rechazo = $_.Exception.GetBaseException() }
        Ver ("con la casilla encendida, editar en su sitio una franja con sesion se niega: " + $(if ($rechazo) { $rechazo.Message } else { 'no se nego' })) ($rechazo -is [InvalidOperationException])
        $hoyW = [DateTime](Uno "SELECT CURDATE()")
        $FW2 = [int]$mDesde.Invoke($hd, [object[]]@($FW, $cW, $W, $diaW, $iniW, $finW.Add([TimeSpan]::FromMinutes(5)), $hoyW))
        $finVieja = Uno "SELECT vigente_hasta FROM horarios WHERE idhorarios = $FW"
        $sesionW = Sql "SELECT horarios_idhorarios FROM codigos_accesos WHERE fecha = CURDATE() AND horarios_idhorarios IN ($FW, $FW2)"
        $otraVez = Resolver 'TSTCC' $W
        Ver ("cambiada desde hoy: la vieja ($FW) termina el " + $(if ($finVieja -is [DBNull]) { '(sin fin)' } else { ([DateTime]$finVieja).ToString('yyyy-MM-dd') }) + ", la sesion de hoy pasa a la nueva ($FW2) y el codigo entra: " + $otraVez.Motivo) `
            ($FW2 -gt $FW -and $finVieja -isnot [DBNull] -and ([DateTime]$finVieja).Date -eq $hoyW.AddDays(-1) -and $sesionW.Rows.Count -eq 1 -and [int]$sesionW.Rows[0][0] -eq $FW2 -and $otraVez.Motivo.ToString() -eq 'Aceptado')
    } else {
        Write-Host '(sin db/20 o sin CambiarFranjaDesde en esta compilacion: no se prueba el cambio desde una fecha)'
    }
    DevolverConFecha

    Write-Host "`n== Sesion extra de una clase fuera de vigencia ==" -ForegroundColor Cyan
    $null = Sql "INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_en) VALUES ($profB, $grupo, $materia, 'TSTCB', CURDATE() - INTERVAL 60 DAY, CURDATE() - INTERVAL 1 DAY, 1, NOW())"
    $cVieja = [int](Uno "SELECT MAX(idclases) FROM clases WHERE codigo = 'TSTCB'")
    $null = Sql "INSERT INTO horario_excepciones (tipo, fecha, clases_idclases, laboratorios_idlaboratorios, hora_inicio, hora_fin, motivo, creada_en) VALUES ('extra', CURDATE(), $cVieja, $X, SUBTIME(CURTIME(), '00:05:00'), ADDTIME(CURTIME(), '00:40:00'), 'TST extra vieja', NOW())"
    $xVieja = [int](Uno "SELECT MAX(idhorario_excepciones) FROM horario_excepciones WHERE clases_idclases = $cVieja")
    # PanoramaDelDia(fecha, codigosParaDar): por reflexion van los dos (hoy y sin los codigos del panel).
    $dia = (M 'PanoramaDelDia').Invoke($hd, @($null, $false))
    $enPanel = $dia.Agenda | Where-Object { $_.ExcepcionId -eq $xVieja } | Select-Object -First 1
    $p = (M 'PanoramaDeProfesor').Invoke($hd, @('TSTPROFB'))
    $enProfesor = @($p.Hoy | Where-Object { $_.ExcepcionId -eq $xVieja }).Count
    (M 'AbrirClasesDeHoy').Invoke($hd, @($p))
    $abierta = [int](Uno "SELECT COUNT(1) FROM codigos_accesos WHERE horario_excepciones_id = $xVieja")
    Ver ("la administracion la ve como no utilizable (" + $enPanel.Utilizable + "), el panel docente no la ve (" + $enProfesor + "), no se intenta abrir (" + $abierta + ") y no hay aviso ('" + $p.AvisoApertura + "')") ($enPanel -ne $null -and -not $enPanel.Utilizable -and $enProfesor -eq 0 -and $abierta -eq 0 -and [string]::IsNullOrEmpty($p.AvisoApertura))

    Write-Host "`n== Copia local del horario ==" -ForegroundColor Cyan
    $tCache = T 'DigLab.conexion.CacheHorario'
    $cache = $tCache.GetMethod('Actualizar', $bf).Invoke($null, @($X))
    Ver ("guarda el modo del laboratorio (" + $cache.Modo + ")") ($cache.Modo.ToString() -eq 'Renovable')
    $sal = $null
    $r = $cache.ResolverCodigo('TSTCA', 'TSTALUM1', [ref]$sal)
    Ver ("sin servidor, en renovable el codigo fijo NO entra: " + $r.Motivo) ($r.Motivo.ToString() -eq 'ModoCambiado')
    $ab = $cache.ClasesAbiertas($cache.AhoraEstimado)
    $r = $cache.ResolverClase([Nullable[int]]$FB, $null, 'TSTALUM1', [ref]$sal)
    Ver ("sin servidor se elige la clase (" + @($ab).Count + " abiertas): " + $r.Motivo) ($r.Motivo.ToString() -eq 'Aceptado' -and @($ab).Count -ge 2)
    $leida = $tCache.GetMethod('Cargar', $bf).Invoke($null, $null)
    Ver ("al leerla de disco conserva el modo (" + $leida.Modo + ")") ($leida.Modo.ToString() -eq 'Renovable')
    $ruta = $tCache.GetProperty('Ruta', $bf).GetValue($null, $null)
    $xml = [xml](Get-Content -Raw $ruta); $xml.horario.RemoveAttribute('modoCodigo'); $xml.Save($ruta)
    $vieja = $tCache.GetMethod('Cargar', $bf).Invoke($null, $null)
    Ver ("una copia de antes (sin el atributo) cuenta como codigo fijo (" + $vieja.Modo + ")") ($vieja.Modo.ToString() -eq 'Fijo')
}
finally {
    Pop-Location
    DevolverConFecha
    Limpiar
    Write-Host ("Limpieza hecha: quedan " + (Uno "SELECT COUNT(1) FROM laboratorios WHERE nombre_laboratorio LIKE 'TST Modos %'") + " laboratorios de prueba")
}

Write-Host "`nResultado: $($script:total - $script:malos)/$($script:total) correctas" -ForegroundColor $(if ($script:malos -eq 0) { 'Green' } else { 'Red' })
if ($script:malos -gt 0) { exit 1 }
