<#
.SYNOPSIS
    Prueba "Reemplazar y avisar" de Horario semanal (db/25, horas_por_asignar)
    contra una base de DESARROLLO (MySQL 8 o MariaDB 5.5).

.DESCRIPTION
    Carga DigBit.exe por reflexion y recorre la parte de HorariosDatos de
    HorariosDatos.PorAsignar.cs:
      - sin db/25 (la tabla escondida un momento): todo como antes, poner
        encima de otra clase sigue chocando, "Quitar horas" no deja nada
        pendiente y el log dice por que;
      - sin "Los cambios de horario aplican desde una fecha": reemplazo
        completo (la otra se quita) y parcial (en medio: se parte en dos), lo
        que queda por asignar ('desplazada'), y el choque con la misma persona
        docente en otro laboratorio, que sigue sin dejar guardar;
      - que se resuelva solo al ponerle horas a la clase, en parte y del todo
        (lo mas antiguo primero), tambien con "Cambiar horas" que anade tiempo;
      - "Quitar horas" con Si (queda 'quitada') y con No (nada pendiente), y
        "Ya no hace falta";
      - con franjas_con_fecha = 1: reemplazo parcial y completo desde una fecha
        (la franja con historia termina el dia antes y no se reescribe) y
        "Quitar horas" desde una fecha con Si;
      - el historial (db/21, si esta): cada reemplazo, cada pendiente y cada
        resolucion;
      - la persona responsable (digbit_consulta, db/18): lee la lista y no
        puede escribir (Permisos.Exigir y, en la base, el error 1142).
    Con CHECKSUM TABLE comprueba que codigos_accesos, registros_bitacoras,
    asistencias_dadas y asistencias_retiradas no cambian (hay una sesion de
    prueba con una bitacora en una franja que se recorta desde una fecha).

    Todo lo que crea va en dos laboratorios 'PRUEBA FA 1' y 'PRUEBA FA 2', el
    domingo, con clases de codigo 'TSTFA%', y se BORRA al terminar (tambien sus
    renglones del historial). Necesita db/20 y db/25, tres docentes y tres
    grupos sin clases el domingo, tres materias y un estudiante.
    MODIFICA la base: no lo ejecutes contra la base real del TESCHI. Mejor en
    una copia (ver INSTALAR.md, o scratchpad\falta_asignar\copiar_base.ps1).

.EXAMPLE
    .\probar_falta_asignar.ps1 -CadenaConexion 'Database=teschi_fa_8;Server=127.0.0.1;Port=3317;User Id=root;Password='
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CadenaConexion,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Otro DigBit.exe; por defecto, el de DigBit\bin\<Configuration>.
    [string]$Exe,

    # numero_identificador de la cuenta de la administracion que firma los cambios.
    [string]$Administrador = 'ADMPRUEBA',

    # Sin la parte de solo consulta (digbit_consulta).
    [switch]$SinConsulta
)

$ErrorActionPreference = 'Stop'
if ($CadenaConexion -match 'teschi\.edu\.mx') { throw 'Esta prueba escribe en la base: nunca contra la de la escuela.' }
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot "..\DigBit\bin\$Configuration\DigBit.exe" }
$Exe = (Resolve-Path $Exe).Path
$script:carpeta = Split-Path $Exe

$env:DIGBIT_CONNECTION_STRING = $CadenaConexion
# El log de la prueba, fuera de la carpeta de datos real (se mira que diga por que falta db/25).
$env:DIGBIT_CARPETA_DATOS = Join-Path $env:TEMP ('digbit_probar_falta_asignar_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $env:DIGBIT_CARPETA_DATOS | Out-Null

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
$tH = T 'DigBit.conexion.HorariosDatos'
$tUsuario = T 'DigBit.conexion.Datos_User'
$tConexion = T 'DigBit.Conexion'
$tEsquema = T 'DigBit.conexion.Esquema'
$h = [Activator]::CreateInstance($tH, $true)

$script:total = 0
$script:malos = 0
function Ver([string]$que, [bool]$bien, [string]$detalle = '') {
    $script:total++
    if (-not $bien) { $script:malos++ }
    $texto = $(if ($bien) { '[OK ] ' } else { '[FALLO] ' }) + $que + $(if ($detalle) { ' (' + $detalle + ')' } else { '' })
    Write-Host $texto -ForegroundColor $(if ($bien) { 'Gray' } else { 'Red' })
}

# SQL directo con la cadena de la prueba, con la zona horaria de la aplicacion.
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
function Uno([string]$texto) {
    $t = Sql $texto
    if ($t.Rows.Count -eq 0 -or $t.Rows[0][0] -is [DBNull]) { return $null }
    return $t.Rows[0][0]
}
function Existe([string]$tabla, [string]$columna) {
    "$(Uno "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '$tabla' AND COLUMN_NAME = '$columna'")" -eq '1'
}

function Desnudos([object[]]$valores) {
    $limpios = New-Object 'object[]' $valores.Count
    for ($i = 0; $i -lt $valores.Count; $i++) {
        $v = $valores[$i]
        if ($null -ne $v) { $v = $v.psobject.BaseObject }
        $limpios[$i] = $v
    }
    return ,$limpios
}

# Un metodo publico de HorariosDatos por su nombre y sus tipos: @{ Ok; Valor } o @{ Ok = $false; Error }.
$nDate = [Nullable[DateTime]]; $nInt = [Nullable[int]]; $ts = [TimeSpan]
function Llamar([string]$metodo, [Type[]]$tipos, [object[]]$argumentos) {
    $m = $tH.GetMethod($metodo, $tipos)
    if ($m -eq $null) { throw "No encuentro HorariosDatos.$metodo" }
    try { return @{ Ok = $true; Valor = $m.Invoke($script:h, (Desnudos $argumentos)) } }
    catch {
        $e = $_.Exception
        while (($e -is [Management.Automation.MethodInvocationException] -or $e -is [Reflection.TargetInvocationException]) -and $e.InnerException) { $e = $e.InnerException }
        return @{ Ok = $false; Error = $e }
    }
}
function Bien([string]$que, $r) {
    Ver $que $r.Ok $(if (-not $r.Ok) { $r.Error.GetType().Name + ': ' + $r.Error.Message } else { '' })
    return $r.Ok
}
function Falla([string]$que, $r, [string]$tipo, [string]$patron) {
    if ($r.Ok) { Ver $que $false 'no fallo'; return }
    $ok = ($r.Error.GetType().Name -eq $tipo) -and ($r.Error.Message -match $patron)
    Ver $que $ok ($r.Error.GetType().Name + ': ' + $r.Error.Message)
}
function FallaMySql([string]$que, $r, [int]$numero) {
    if ($r.Ok) { Ver $que $false 'no fallo'; return }
    $e = $r.Error
    while ($e -and -not ($e -is [MySql.Data.MySqlClient.MySqlException])) { $e = $e.InnerException }
    Ver $que ($e -ne $null -and $e.Number -eq $numero) ($r.Error.GetType().Name + ': ' + $r.Error.Message)
}

function Plan([object]$franja, [int]$clase, [int]$lab, [string]$ini, [string]$fin, $desde) {
    Llamar 'PlanDeReemplazo' @($nInt, [int], [int], [int], $ts, $ts, $nDate) @($franja, $clase, $lab, 7, [TimeSpan]$ini, [TimeSpan]$fin, $desde)
}
function Poner([int]$clase, [int]$lab, [string]$ini, [string]$fin, $desde) {
    Llamar 'PonerClaseReemplazando' @([int], [int], [int], $ts, $ts, $nDate) @($clase, $lab, 7, [TimeSpan]$ini, [TimeSpan]$fin, $desde)
}
function Cambiar([int]$franja, [int]$clase, [int]$lab, [string]$ini, [string]$fin, [DateTime]$desde) {
    Llamar 'CambiarFranjaReemplazando' @([int], [int], [int], [int], $ts, $ts, [DateTime]) @($franja, $clase, $lab, 7, [TimeSpan]$ini, [TimeSpan]$fin, $desde)
}
function Borrar([int]$franja, [bool]$porAsignar) { Llamar 'BorrarFranja' @([int], [bool]) @($franja, $porAsignar) }
function QuitarDesde([int]$franja, [DateTime]$desde, [bool]$porAsignar) { Llamar 'QuitarFranjaDesde' @([int], [DateTime], [bool]) @($franja, $desde, $porAsignar) }
function YaNo([int]$id) { Llamar 'YaNoHaceFalta' @([int]) @($id) }
function Leer() { Llamar 'LeerHorasPorAsignar' @() @() }

# Una franja de prueba puesta a mano (sin vigencia propia: con la clase empezada
# hace 60 dias ya tiene historia, semanas pasadas).
function Franja([int]$clase, [int]$lab, [string]$ini, [string]$fin) {
    $null = Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES ($clase, $lab, 7, '$ini', '$fin')"
    return [int](Uno "SELECT MAX(idhorarios) FROM horarios WHERE clases_idclases = $clase AND laboratorios_idlaboratorios = $lab")
}
# "lab:HH:mm-HH:mm[ hasta yyyy-MM-dd][ desde yyyy-MM-dd]" de las franjas del domingo de una clase.
function FranjasDe([int]$clase) {
    $t = Sql ("SELECT CONCAT(l.nombre_laboratorio, ' ', TIME_FORMAT(h.hora_inicio, '%H:%i'), '-', TIME_FORMAT(h.hora_fin, '%H:%i')" +
        $(if ($conVigencia) { ", IF(h.vigente_desde IS NULL, '', CONCAT(' desde ', DATE_FORMAT(h.vigente_desde, '%Y-%m-%d'))), IF(h.vigente_hasta IS NULL, '', CONCAT(' hasta ', DATE_FORMAT(h.vigente_hasta, '%Y-%m-%d')))" } else { "" }) +
        ") FROM horarios h JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios WHERE h.clases_idclases = $clase AND h.dia_semana = 7 ORDER BY l.nombre_laboratorio, h.hora_inicio, h.idhorarios")
    return (@($t.Rows | ForEach-Object { "$($_[0])" }) -join '; ')
}
# Lo pendiente de una clase: "minutos/estado/motivo" de lo mas antiguo a lo mas nuevo.
function PendientesDe([int]$clase) {
    $t = Sql "SELECT CONCAT(minutos, '/', estado, '/', motivo) FROM horas_por_asignar WHERE clases_idclases = $clase ORDER BY creada_en, idhoras_por_asignar"
    return (@($t.Rows | ForEach-Object { "$($_[0])" }) -join '; ')
}
function Historial([string]$condicion) {
    if (-not $conHistorial) { return 0 }
    return [int](Uno "SELECT COUNT(*) FROM historial_cambios WHERE cuando >= '$($inicio.ToString('yyyy-MM-dd HH:mm:ss'))' AND $condicion")
}

$tablasIntactas = @('codigos_accesos', 'registros_bitacoras', 'asistencias_dadas', 'asistencias_retiradas') |
    Where-Object { "$(Uno "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '$_'")" -eq '1' }
function Sumas() { ((Sql ('CHECKSUM TABLE ' + ($tablasIntactas -join ', '))).Rows | ForEach-Object { "$($_[0] -replace '^.*\.', '')=$($_[1])" }) -join '; ' }
function Cuentas() { (@($tablasIntactas | ForEach-Object { "$_=$(Uno "SELECT COUNT(*) FROM $_")" })) -join '; ' }

function Limpiar() {
    $labs = "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio LIKE 'PRUEBA FA %'"
    $clases = "SELECT idclases FROM clases WHERE codigo LIKE 'TSTFA%'"
    $null = Sql "DELETE rb FROM registros_bitacoras rb JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos WHERE ca.codigo = 'TSTFA'"
    $null = Sql "DELETE FROM codigos_accesos WHERE codigo = 'TSTFA'"
    if (Existe 'horas_por_asignar' 'idhoras_por_asignar') { $null = Sql "DELETE FROM horas_por_asignar WHERE clases_idclases IN ($clases)" }
    if (Existe 'historial_cambios' 'despues') {
        $null = Sql "DELETE FROM historial_cambios WHERE laboratorio_id IN ($labs) OR (tabla = 'horas_por_asignar' AND descripcion LIKE '%PRUEBA FA%')"
    }
    $null = Sql "DELETE FROM horario_excepciones WHERE horarios_idhorarios IN (SELECT idhorarios FROM horarios WHERE clases_idclases IN ($clases)) OR clases_idclases IN ($clases)"
    $null = Sql "DELETE FROM horarios WHERE clases_idclases IN ($clases) OR laboratorios_idlaboratorios IN ($labs)"
    $null = Sql "DELETE FROM clases WHERE codigo LIKE 'TSTFA%'"
    $null = Sql "DELETE FROM laboratorios WHERE nombre_laboratorio LIKE 'PRUEBA FA %'"
}

# --- Antes de empezar ----------------------------------------------------------
if (-not (Existe 'horas_por_asignar' 'resuelta_por')) { throw 'La base no tiene db/25_horas_por_asignar.sql: aplicala antes.' }
# Sin db/20 no hay cambios desde una fecha: esa parte se salta.
$conVigencia = Existe 'horarios' 'vigente_hasta'
$conHistorial = Existe 'historial_cambios' 'despues'
$conAjustes = Existe 'configuracion' 'valor'
if (-not $conAjustes) { throw 'La base no tiene configuracion (db/13): sin ella no se prueba franjas_con_fecha.' }
Limpiar

$idAdmin = [int]("$(Uno "SELECT idusuarios FROM usuarios WHERE numero_identificador = '$Administrador'")" -replace '^$', '0')
if ($idAdmin -eq 0) { Write-Host "Sin la cuenta ${Administrador}: los cambios quedan sin firmar." -ForegroundColor Yellow }
$tUsuario.GetMethod('SetUser').Invoke($null, [object[]]@($Administrador)) | Out-Null
$tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 3)
$hoy = [DateTime](Uno 'SELECT CURDATE()')
$conFechaAntes = Uno "SELECT valor FROM configuracion WHERE clave = 'franjas_con_fecha'"
# Sin la clave ya cuenta como encendido (db/20): para probar sin los cambios desde una fecha, '0' a mano.
$null = Sql "DELETE FROM configuracion WHERE clave = 'franjas_con_fecha'"
$null = Sql "INSERT INTO configuracion (clave, valor) VALUES ('franjas_con_fecha', '0')"

# Tres docentes y tres grupos sin clases el domingo (asi no choca nada de la base), tres materias y un estudiante.
$sinDomingo = "NOT EXISTS (SELECT 1 FROM clases c JOIN horarios hh ON hh.clases_idclases = c.idclases WHERE hh.dia_semana = 7 AND c.{0} = {1})"
$docentes = @((Sql ("SELECT u.idusuarios FROM usuarios u WHERE u.fk_tipo_usuario = 2 AND " + ($sinDomingo -f 'usuarios_idusuarios', 'u.idusuarios') + " ORDER BY u.idusuarios LIMIT 3")).Rows | ForEach-Object { [int]$_[0] })
$grupos = @((Sql ("SELECT g.idgrupos FROM grupos g WHERE " + ($sinDomingo -f 'grupos_idgrupos', 'g.idgrupos') + " ORDER BY g.idgrupos LIMIT 3")).Rows | ForEach-Object { [int]$_[0] })
$materias = @((Sql 'SELECT id_materia FROM materias ORDER BY id_materia LIMIT 3').Rows | ForEach-Object { [int]$_[0] })
$alumno = [int]("$(Uno 'SELECT idusuarios FROM usuarios WHERE fk_tipo_usuario = 1 ORDER BY idusuarios LIMIT 1')" -replace '^$', '0')
if ($docentes.Count -lt 3 -or $grupos.Count -lt 3 -or $materias.Count -lt 3 -or $alumno -eq 0) {
    throw 'Hacen falta tres docentes y tres grupos sin clases el domingo, tres materias y un estudiante.'
}
$d1, $d2, $d3 = $docentes; $g1, $g2, $g3 = $grupos; $m1, $m2, $m3 = $materias

$null = Sql "INSERT INTO laboratorios (nombre_laboratorio) VALUES ('PRUEBA FA 1'), ('PRUEBA FA 2')"
$lab1 = [int](Uno "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio = 'PRUEBA FA 1'")
$lab2 = [int](Uno "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio = 'PRUEBA FA 2'")
function Clase([string]$codigo, [int]$docente, [int]$grupo, [int]$materia) {
    $null = Sql ("INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en) " +
        "VALUES ($docente, $grupo, $materia, '$codigo', CURDATE() - INTERVAL 60 DAY, CURDATE() + INTERVAL 120 DAY, 1, NULL, NOW())")
    return [int](Uno "SELECT idclases FROM clases WHERE codigo = '$codigo'")
}
$cA = Clase 'TSTFA_A' $d1 $g1 $m1   # la que se desplaza
$cB = Clase 'TSTFA_B' $d2 $g2 $m2   # la que entra
$cC = Clase 'TSTFA_C' $d2 $g3 $m3   # la misma docente que B, en el otro laboratorio
$cD = Clase 'TSTFA_D' $d3 $g3 $m1   # la que se quita con Quitar horas
$inicio = [DateTime](Uno 'SELECT NOW()')
Write-Host "Hoy segun el servidor: $($hoy.ToString('yyyy-MM-dd')); laboratorios $lab1 y $lab2; clases A=$cA B=$cB C=$cC D=$cD; administracion: $idAdmin; historial: $conHistorial"

try {
    Write-Host "`n== Sin db/25: como antes ==" -ForegroundColor Cyan
    $x0 = Franja $cA $lab1 '20:00:00' '21:00:00'
    $null = Sql 'RENAME TABLE horas_por_asignar TO horas_por_asignar_escondida'
    try {
        Ver 'HorasPorAsignarDisponible: no' (-not (Llamar 'HorasPorAsignarDisponible' @() @()).Valor)
        $r = Leer
        Ver 'LeerHorasPorAsignar: null (sin pastilla)' ($r.Ok -and $r.Valor -eq $null)
        Falla 'PonerClaseReemplazando no se usa sin la 25' (Poner $cB $lab1 '20:00' '21:00' $null) 'ErrorDeDatos' 'db/25'
        Falla 'CrearFranja encima de otra clase sigue chocando' (Llamar 'CrearFranja' @([int], [int], [int], $ts, $ts, $nDate) @($cB, $lab1, 7, [TimeSpan]'20:00', [TimeSpan]'21:00', $null)) 'InvalidOperationException' 'Se solapa con .*mismo laboratorio'
        Bien 'BorrarFranja(porAsignar = true) borra como antes' (Borrar $x0 $true) | Out-Null
        Ver '... sin la franja' ("$(Uno "SELECT COUNT(*) FROM horarios WHERE idhorarios = $x0")" -eq '0')
        $log = @(Get-ChildItem $env:DIGBIT_CARPETA_DATOS -Recurse -Filter *.log -ErrorAction SilentlyContinue | Get-Content -ErrorAction SilentlyContinue) -join "`n"
        Ver 'el log dice por que (falta db/25_horas_por_asignar.sql)' ($log -match 'falta db/25_horas_por_asignar\.sql')
    }
    finally {
        $null = Sql 'RENAME TABLE horas_por_asignar_escondida TO horas_por_asignar'
        # Lo que falto se recuerda un minuto por cuenta (Esquema): para seguir ya, se olvida.
        $columna = $tEsquema.GetField('TablaPorAsignar', $bf).GetValue($null)
        $noLaVen = $columna.GetType().GetField('cuentasQueNoLaVen', $bf).GetValue($columna)
        $noLaVen.GetType().GetField('faltoEn', $bf).GetValue($noLaVen).Clear()
    }
    Ver '... y con la 25 de vuelta, si' ((Llamar 'HorasPorAsignarDisponible' @() @()).Valor)
    Ver '... sin nada pendiente de eso' ((PendientesDe $cA) -eq '')

    # Una sesion de prueba del domingo pasado, con una bitacora, en la franja que
    # despues se recorta desde una fecha: no se tiene que tocar.
    $x7 = Franja $cA $lab1 '10:00:00' '12:00:00'
    $null = Sql ("INSERT INTO codigos_accesos (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos, laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios) " +
        "VALUES ('TSTFA', NOW(), CURDATE() - INTERVAL (WEEKDAY(CURDATE()) + 1) DAY, '10:00:00', '12:00:00', $m1, $g1, $lab1, $d1, $x7)")
    $sesion = [int](Uno "SELECT idcodigos_accesos FROM codigos_accesos WHERE codigo = 'TSTFA'")
    $null = Sql "INSERT INTO registros_bitacoras (fk_codigo_accesos, fk_usuario, numero_computadora) VALUES ($sesion, $alumno, 'PC-FA')"
    $sumasAntes = Sumas
    $cuentasAntes = Cuentas

    Write-Host "`n== Sin franjas_con_fecha: reemplazo completo ==" -ForegroundColor Cyan
    $x1 = Franja $cA $lab1 '08:00:00' '10:00:00'
    $r = Plan $null $cB $lab1 '08:00' '10:00' $null
    if (Bien 'PlanDeReemplazo (B sobre A, 08:00-10:00)' $r) {
        $q = @($r.Valor.Quitadas)
        Ver '... quita a A entera: 120 min, sin bloqueo' ($q.Count -eq 1 -and $q[0].FranjaId -eq $x1 -and $q[0].Minutos -eq 120 -and $r.Valor.Bloqueo -eq $null) "quitadas=$($q.Count) bloqueo=$($r.Valor.Bloqueo)"
        Ver '... y avisa de que cambia su pasado (ya tiene historia)' ($q.Count -eq 1 -and $q[0].CambiaElPasado)
        Ver "... la confirmacion: '$($q[0].Describir())'" ($q[0].Describir() -match '^.+, .+ \(.+\) del domingo 08:00.10:00 \(2 h\)$')
    }
    $r = Poner $cB $lab1 '08:00' '10:00' $null
    if (Bien 'PonerClaseReemplazando' $r) { Ver '... con una quitada y nada asignado (B no debia nada)' (@($r.Valor.Quitadas).Count -eq 1 -and $r.Valor.MinutosAsignados -eq 0) }
    Ver '... A ya no tiene esas horas' ("$(Uno "SELECT COUNT(*) FROM horarios WHERE idhorarios = $x1")" -eq '0') (FranjasDe $cA)
    Ver '... B las tiene' ((FranjasDe $cB) -eq 'PRUEBA FA 1 08:00-10:00') (FranjasDe $cB)
    Ver '... y a A le faltan 2 h (desplazada)' ((PendientesDe $cA) -eq '120/pendiente/desplazada') (PendientesDe $cA)
    $p1 = (Sql "SELECT * FROM horas_por_asignar WHERE clases_idclases = $cA ORDER BY idhoras_por_asignar LIMIT 1").Rows[0]
    Ver '... de donde salio: el laboratorio, el domingo de 08:00 a 10:00, desde hoy, quien' ([int]$p1['laboratorio_id'] -eq $lab1 -and [int]$p1['dia_semana'] -eq 7 -and
        "$($p1['hora_inicio'])" -eq '08:00:00' -and "$($p1['hora_fin'])" -eq '10:00:00' -and ([DateTime]$p1['desde']).Date -eq $hoy -and
        ($idAdmin -eq 0 -or "$($p1['creada_por'])" -eq "$idAdmin") -and [int]$p1['minutos_originales'] -eq 120) "creada_por=$($p1['creada_por'])"

    Write-Host "`n== Sin franjas_con_fecha: reemplazo parcial (en medio) ==" -ForegroundColor Cyan
    $x2 = Franja $cA $lab1 '12:00:00' '16:00:00'
    $r = Poner $cB $lab1 '13:00' '14:00' $null
    Bien 'PonerClaseReemplazando 13:00-14:00 sobre A 12:00-16:00' $r | Out-Null
    Ver '... A se queda con 12:00-13:00 y 14:00-16:00' ((FranjasDe $cA) -match 'PRUEBA FA 1 10:00-12:00; PRUEBA FA 1 12:00-13:00; PRUEBA FA 1 14:00-16:00') (FranjasDe $cA)
    Ver '... la misma franja, recortada en su sitio' ("$(Uno "SELECT TIME_FORMAT(hora_fin, '%H:%i') FROM horarios WHERE idhorarios = $x2")" -eq '13:00')
    Ver '... B tiene 13:00-14:00' ((FranjasDe $cB) -eq 'PRUEBA FA 1 08:00-10:00; PRUEBA FA 1 13:00-14:00') (FranjasDe $cB)
    Ver '... y a A le falta 1 h mas' ((PendientesDe $cA) -eq '120/pendiente/desplazada; 60/pendiente/desplazada') (PendientesDe $cA)

    Write-Host "`n== La misma persona docente en otro laboratorio: sigue sin dejar ==" -ForegroundColor Cyan
    $y = Franja $cC $lab2 '18:00:00' '19:00:00'
    $x3 = Franja $cA $lab1 '18:00:00' '19:00:00'
    $r = Plan $null $cB $lab1 '18:00' '19:00' $null
    Ver 'PlanDeReemplazo dice el bloqueo (misma persona docente)' ($r.Ok -and "$($r.Valor.Bloqueo)" -match 'misma persona docente' -and "$($r.Valor.Bloqueo)" -match 'PRUEBA FA 2') "$($r.Valor.Bloqueo)"
    $pendientesAntes = PendientesDe $cA
    Falla 'PonerClaseReemplazando choca (B y C son de la misma docente)' (Poner $cB $lab1 '18:00' '19:00' $null) 'InvalidOperationException' 'misma persona docente'
    Ver '... A sigue entera y no queda nada pendiente de eso' ("$(Uno "SELECT CONCAT(TIME_FORMAT(hora_inicio, '%H:%i'), '-', TIME_FORMAT(hora_fin, '%H:%i')) FROM horarios WHERE idhorarios = $x3")" -eq '18:00-19:00' -and (PendientesDe $cA) -eq $pendientesAntes)

    Write-Host "`n== Se resuelve solo (lo mas antiguo primero) ==" -ForegroundColor Cyan
    $r = Poner $cA $lab2 '06:00' '07:00' $null
    if (Bien 'A gana 1 h en el otro laboratorio' $r) { Ver '... bajan 60 min y le faltan 120' ($r.Valor.MinutosAsignados -eq 60 -and $r.Valor.MinutosQueFaltan -eq 120) "asignados=$($r.Valor.MinutosAsignados) faltan=$($r.Valor.MinutosQueFaltan)" }
    Ver '... en parte: el mas antiguo baja a 60' ((PendientesDe $cA) -eq '60/pendiente/desplazada; 60/pendiente/desplazada') (PendientesDe $cA)
    $r = Poner $cA $lab2 '07:00' '08:30' $null
    if (Bien 'A gana 1 h 30 min mas' $r) { Ver '... bajan 90 y le faltan 30' ($r.Valor.MinutosAsignados -eq 90 -and $r.Valor.MinutosQueFaltan -eq 30) }
    Ver '... del todo: el mas antiguo queda asignado (0) y el otro en 30' ((PendientesDe $cA) -eq '0/asignada/desplazada; 30/pendiente/desplazada') (PendientesDe $cA)
    $asignada = (Sql "SELECT resuelta_en, resuelta_por FROM horas_por_asignar WHERE clases_idclases = $cA AND estado = 'asignada'").Rows[0]
    Ver '... con cuando y quien' (-not ($asignada['resuelta_en'] -is [DBNull]) -and ($idAdmin -eq 0 -or "$($asignada['resuelta_por'])" -eq "$idAdmin"))
    $x6a = [int](Uno "SELECT idhorarios FROM horarios WHERE clases_idclases = $cA AND laboratorios_idlaboratorios = $lab2 AND hora_inicio = '06:00:00'")
    $r = Cambiar $x6a $cA $lab2 '05:30' '07:00' $hoy
    if (Bien 'Cambiar horas que anade 30 min (06:00 -> 05:30)' $r) { Ver '... bajan 30 y ya no le falta nada' ($r.Valor.MinutosAsignados -eq 30 -and $r.Valor.MinutosQueFaltan -eq 0) }
    Ver '... todo asignado' ((PendientesDe $cA) -eq '0/asignada/desplazada; 0/asignada/desplazada') (PendientesDe $cA)
    $r = Cambiar $x6a $cA $lab2 '06:00' '07:00' $hoy
    Ver 'Cambiar horas que quita tiempo no deja nada pendiente' ($r.Ok -and (PendientesDe $cA) -eq '0/asignada/desplazada; 0/asignada/desplazada') (PendientesDe $cA)
    $r = Leer
    Ver 'LeerHorasPorAsignar: A ya no sale' ($r.Ok -and @($r.Valor | Where-Object { $_.ClaseId -eq $cA }).Count -eq 0)

    Write-Host "`n== Cambiar horas encima de otra clase ==" -ForegroundColor Cyan
    $xb = [int](Uno "SELECT idhorarios FROM horarios WHERE clases_idclases = $cB AND hora_inicio = '13:00:00'")
    $r = Plan $xb $cB $lab1 '13:00' '15:00' $hoy
    Ver 'PlanDeReemplazo de B 13:00-15:00: quita a A 14:00-15:00 (1 h), sin contarse a si misma' ($r.Ok -and @($r.Valor.Quitadas).Count -eq 1 -and $r.Valor.Quitadas[0].ClaseId -eq $cA -and $r.Valor.Quitadas[0].Minutos -eq 60)
    $r = Cambiar $xb $cB $lab1 '13:00' '15:00' $hoy
    Bien 'CambiarFranjaReemplazando' $r | Out-Null
    Ver '... A se queda con 15:00-16:00 y le falta 1 h' ((FranjasDe $cA) -match '15:00-16:00' -and (PendientesDe $cA) -match '60/pendiente/desplazada$') "$(FranjasDe $cA) / $(PendientesDe $cA)"

    Write-Host "`n== Quitar horas: Si y No ==" -ForegroundColor Cyan
    $x5 = Franja $cD $lab2 '20:00:00' '21:00:00'
    $x6 = Franja $cD $lab2 '21:00:00' '22:30:00'
    Bien 'BorrarFranja con Si (se dara en otro horario)' (Borrar $x5 $true) | Out-Null
    Ver '... ya no esta y le falta 1 h (quitada)' ("$(Uno "SELECT COUNT(*) FROM horarios WHERE idhorarios = $x5")" -eq '0' -and (PendientesDe $cD) -eq '60/pendiente/quitada') (PendientesDe $cD)
    Bien 'BorrarFranja con No (la clase tendra menos horas)' (Borrar $x6 $false) | Out-Null
    Ver '... ya no esta y no queda nada mas pendiente' ("$(Uno "SELECT COUNT(*) FROM horarios WHERE idhorarios = $x6")" -eq '0' -and (PendientesDe $cD) -eq '60/pendiente/quitada') (PendientesDe $cD)

    Write-Host "`n== Ya no hace falta ==" -ForegroundColor Cyan
    $pD = [int](Uno "SELECT idhoras_por_asignar FROM horas_por_asignar WHERE clases_idclases = $cD AND estado = 'pendiente'")
    Bien 'YaNoHaceFalta' (YaNo $pD) | Out-Null
    $fila = (Sql "SELECT estado, minutos, resuelta_por FROM horas_por_asignar WHERE idhoras_por_asignar = $pD").Rows[0]
    Ver '... queda no_hace_falta, con sus minutos y quien' ($fila['estado'] -eq 'no_hace_falta' -and [int]$fila['minutos'] -eq 60 -and ($idAdmin -eq 0 -or "$($fila['resuelta_por'])" -eq "$idAdmin"))
    Falla '... otra vez, ya no esta pendiente' (YaNo $pD) 'InvalidOperationException' 'ya no est.n por asignar'

    $desde = $hoy.AddDays(1)
    if (-not $conVigencia) { Write-Host "`nSin db/20 (vigencia de cada franja): se salta la parte de los cambios desde una fecha." -ForegroundColor Yellow }
    else {
    Write-Host "`n== Con franjas_con_fecha: desde una fecha ==" -ForegroundColor Cyan
    $null = Sql "UPDATE configuracion SET valor = '1' WHERE clave = 'franjas_con_fecha'"
    $r = Plan $null $cB $lab1 '10:00' '11:00' $desde
    Ver 'PlanDeReemplazo: quita a A 10:00-11:00 sin tocar su pasado' ($r.Ok -and @($r.Valor.Quitadas).Count -eq 1 -and -not $r.Valor.Quitadas[0].CambiaElPasado) $(if ($r.Ok) { "$(@($r.Valor.Quitadas).Count) quitadas" } else { $r.Error.Message })
    $r = Poner $cB $lab1 '10:00' '11:00' $desde
    Bien 'PonerClaseReemplazando desde manana, parcial (A 10:00-12:00 con historia)' $r | Out-Null
    $d = $desde.ToString('yyyy-MM-dd'); $antesDe = $hoy.ToString('yyyy-MM-dd')
    Ver "... A 10:00-12:00 termina hoy y desde manana tiene 11:00-12:00" ((FranjasDe $cA) -match "PRUEBA FA 1 10:00-12:00 hasta $antesDe" -and (FranjasDe $cA) -match "PRUEBA FA 1 11:00-12:00 desde $d") (FranjasDe $cA)
    Ver '... la franja vieja es la misma (no se reescribe) y su sesion sigue en ella' ("$(Uno "SELECT horarios_idhorarios FROM codigos_accesos WHERE idcodigos_accesos = $sesion")" -eq "$x7" -and
        "$(Uno "SELECT CONCAT(TIME_FORMAT(hora_inicio, '%H:%i'), '-', TIME_FORMAT(hora_fin, '%H:%i')) FROM horarios WHERE idhorarios = $x7")" -eq '10:00-12:00')
    Ver "... B 10:00-11:00 desde manana" ((FranjasDe $cB) -match "PRUEBA FA 1 10:00-11:00 desde $d") (FranjasDe $cB)
    $pFecha = (Sql "SELECT minutos, desde, motivo FROM horas_por_asignar WHERE clases_idclases = $cA AND estado = 'pendiente' ORDER BY idhoras_por_asignar DESC LIMIT 1").Rows[0]
    Ver '... y a A le falta 1 h desde manana' ([int]$pFecha['minutos'] -eq 60 -and ([DateTime]$pFecha['desde']).Date -eq $desde -and $pFecha['motivo'] -eq 'desplazada')
    $x8 = Franja $cA $lab1 '16:00:00' '17:00:00'
    $r = Poner $cB $lab1 '16:00' '17:00' $desde
    Bien 'PonerClaseReemplazando desde manana, completo (A 16:00-17:00 con historia)' $r | Out-Null
    Ver '... A no se borra: termina hoy' ("$(Uno "SELECT DATE_FORMAT(vigente_hasta, '%Y-%m-%d') FROM horarios WHERE idhorarios = $x8")" -eq $antesDe) (FranjasDe $cA)
    $x15 = [int](Uno "SELECT idhorarios FROM horarios WHERE clases_idclases = $cA AND hora_inicio = '15:00:00'")
    $pendientesA = [int](Uno "SELECT COUNT(*) FROM horas_por_asignar WHERE clases_idclases = $cA")
    Bien 'QuitarFranjaDesde con Si (A 15:00-16:00, con historia)' (QuitarDesde $x15 $desde $true) | Out-Null
    Ver '... termina hoy (no se borra) y le falta 1 h desde manana (quitada)' ("$(Uno "SELECT DATE_FORMAT(vigente_hasta, '%Y-%m-%d') FROM horarios WHERE idhorarios = $x15")" -eq $antesDe -and
        "$(Uno "SELECT CONCAT(minutos, '/', motivo, '/', DATE_FORMAT(desde, '%Y-%m-%d')) FROM horas_por_asignar WHERE clases_idclases = $cA ORDER BY idhoras_por_asignar DESC LIMIT 1")" -eq "60/quitada/$d" -and
        [int](Uno "SELECT COUNT(*) FROM horas_por_asignar WHERE clases_idclases = $cA") -eq $pendientesA + 1)
    Bien 'QuitarFranjaDesde con No (A 12:00-13:00)' (QuitarDesde $x2 $desde $false) | Out-Null
    Ver '... sin nada mas pendiente' ([int](Uno "SELECT COUNT(*) FROM horas_por_asignar WHERE clases_idclases = $cA") -eq $pendientesA + 1)
    }

    # Lo que deberia salir: lo pendiente de A en la tabla (4 y 240 min con db/20; 1 y 60 sin ella).
    $esperadas = [int](Uno "SELECT COUNT(*) FROM horas_por_asignar WHERE clases_idclases = $cA AND estado = 'pendiente'")
    $esperados = [int](Uno "SELECT IFNULL(SUM(minutos), 0) FROM horas_por_asignar WHERE clases_idclases = $cA AND estado = 'pendiente'")
    $r = Leer
    $deA = @($r.Valor | Where-Object { $_.ClaseId -eq $cA })
    $orden = @($deA | ForEach-Object { $_.Id })
    Ver "LeerHorasPorAsignar: A con $($deA.Count) pendientes, $(($deA | Measure-Object Minutos -Sum).Sum) min, lo mas antiguo primero" ($r.Ok -and $deA.Count -eq $esperadas -and
        $esperadas -eq $(if ($conVigencia) { 4 } else { 1 }) -and ($deA | Measure-Object Minutos -Sum).Sum -eq $esperados -and $deA[0].Laboratorio -eq 'PRUEBA FA 1' -and
        $deA[0].Origen -match '^PRUEBA FA 1, domingo ' -and (($orden | Sort-Object) -join ',') -eq ($orden -join ','))
    Ver "... con su origen: '$($deA[0].Origen)'" ($deA.Count -gt 0 -and $deA[0].Origen -match '14:00.15:00')

    if ($conHistorial) {
        Write-Host "`n== Historial ==" -ForegroundColor Cyan
        $altas = [int](Uno "SELECT COUNT(*) FROM horas_por_asignar WHERE clases_idclases IN ($cA, $cB, $cC, $cD)")
        Ver "una alta por cada pendiente ($altas)" ((Historial "tipo = 'por_asignar' AND accion = 'alta'") -eq $altas)
        Ver 'una por cada pendiente que bajo (4: 60; 90 = 60 + 30; 30)' ((Historial "tipo = 'por_asignar' AND accion = 'asignar'") -eq 4) "$(Historial "tipo = 'por_asignar' AND accion = 'asignar'")"
        Ver 'una de Ya no hace falta' ((Historial "tipo = 'por_asignar' AND accion = 'no_hace_falta' AND registro_id = $pD") -eq 1)
        # Completa, parcial (y el trozo de despues) y Cambiar horas; con db/20, ademas las dos desde una fecha.
        $reemplazos = $(if ($conVigencia) { 6 } else { 4 })
        Ver "cada reemplazo dice que la reemplaza ($reemplazos)" ((Historial "tipo = 'franja' AND descripcion LIKE '%La reemplaza%'") -eq $reemplazos) "$(Historial "tipo = 'franja' AND descripcion LIKE '%La reemplaza%'")"
        Ver 'y la nueva dice en lugar de quien' ((Historial "tipo = 'franja' AND accion = 'alta' AND descripcion LIKE '%En lugar de%'") -ge $(if ($conVigencia) { 4 } else { 2 }))
        Ver 'el choque no dejo nada' ((Historial "laboratorio_id = $lab1 AND descripcion LIKE '%18:00 a 19:00%'") -eq 0)
    }

    if (-not $SinConsulta) {
        Write-Host "`n== Solo consulta (persona responsable del laboratorio) ==" -ForegroundColor Cyan
        $conexionEquipo = $tConexion.GetMethod('GetConexion').Invoke([Activator]::CreateInstance($tConexion, $true), [object[]]@())
        $argsPreparar = [object[]]@($conexionEquipo, $null)
        $problema = (T 'DigBit.conexion.CuentaDeConsulta').GetMethod('Preparar', $bf).Invoke($null, $argsPreparar)
        $conexionEquipo.Dispose()
        if ($problema) {
            Write-Host "Se salta: $problema" -ForegroundColor Yellow
        }
        else {
            $tConexion.GetMethod('UsarCuentaDeSesion', $bf).Invoke($null, [object[]]@([string]$argsPreparar[1])) | Out-Null
            $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 4)
            try {
                $r = Leer
                Ver 'lee la lista' ($r.Ok -and $r.Valor -ne $null -and @($r.Valor | Where-Object { $_.ClaseId -eq $cA }).Count -eq $esperadas) $(if (-not $r.Ok) { $r.Error.Message } else { '' })
                $escrituras = @(
                    @('Poner', (Poner $cB $lab1 '22:00' '23:00' $null)),
                    @('Cambiar', (Cambiar $xb $cB $lab1 '13:00' '14:00' $hoy)),
                    @('Borrar', (Borrar $x3 $true)),
                    @('QuitarDesde', (QuitarDesde $x3 $desde $true)),
                    @('YaNoHaceFalta', (YaNo ([int](Uno "SELECT idhoras_por_asignar FROM horas_por_asignar WHERE clases_idclases = $cA AND estado = 'pendiente' LIMIT 1")))))
                foreach ($e in $escrituras) { Falla "$($e[0]) no se deja (Permisos.Exigir)" $e[1] 'AccionNoPermitida' 'Solo la administraci.n' }
                # Aunque la pantalla se equivocara, la base rechaza escribir: error 1142.
                $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 3)
                FallaMySql 'YaNoHaceFalta con digbit_consulta: la base lo rechaza (1142)' (YaNo ([int](Uno "SELECT idhoras_por_asignar FROM horas_por_asignar WHERE clases_idclases = $cA AND estado = 'pendiente' LIMIT 1"))) 1142
                FallaMySql 'QuitarFranjaDesde con Si con digbit_consulta (1142)' (QuitarDesde $x3 $desde $true) 1142
            }
            finally {
                $tConexion.GetMethod('UsarCuentaDelEquipo', $bf).Invoke($null, [object[]]@()) | Out-Null
                $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 3)
            }
            Ver '... y nada cambio' ([int](Uno "SELECT COUNT(*) FROM horas_por_asignar WHERE clases_idclases = $cA AND estado = 'pendiente'") -eq $esperadas -and
                "$(Uno "SELECT COUNT(*) FROM horarios WHERE idhorarios = $x3")" -eq '1')
        }
    }

    $sumasDespues = Sumas
    Ver "sesiones, bitacoras y asistencias intactas ($cuentasAntes)" ($sumasAntes -eq $sumasDespues -and $cuentasAntes -eq (Cuentas)) $(if ($sumasAntes -ne $sumasDespues) { "antes: $sumasAntes / despues: $sumasDespues" } else { '' })
}
finally {
    $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 0)
    Limpiar
    $null = Sql "DELETE FROM configuracion WHERE clave = 'franjas_con_fecha'"
    if ($conFechaAntes -ne $null) { $null = Sql "INSERT INTO configuracion (clave, valor) VALUES ('franjas_con_fecha', '$conFechaAntes')" }
    Ver 'no quedan datos de la prueba' ("$(Uno "SELECT COUNT(*) FROM clases WHERE codigo LIKE 'TSTFA%'")" -eq '0' -and "$(Uno "SELECT COUNT(*) FROM laboratorios WHERE nombre_laboratorio LIKE 'PRUEBA FA %'")" -eq '0')
    Remove-Item -Recurse -Force $env:DIGBIT_CARPETA_DATOS -ErrorAction SilentlyContinue
}

Write-Host "`nResultado: $($script:total - $script:malos)/$($script:total) correctas" -ForegroundColor $(if ($script:malos -eq 0) { 'Green' } else { 'Red' })
if ($script:malos -gt 0) { exit 1 }
