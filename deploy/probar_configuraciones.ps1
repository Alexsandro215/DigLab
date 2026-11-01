<#
.SYNOPSIS
    Prueba la capa de datos de las configuraciones de horario (db/16 y db/23)
    contra una base de DESARROLLO.

.DESCRIPTION
    Carga DigBit.exe por reflexion y recorre CicloPreliminarDatos y
    RevisionCiclo:
      - guardar el horario de hoy, con las mismas cuentas que ResumirOrigen, y
        los nombres repetidos (sin acentos ni mayusculas);
      - preparar un horario de hoy, de una guardada (misma firma por clase) y
        en blanco; con otro en preparacion, sin reemplazar falla y con
        reemplazar el anterior desaparece;
      - editarlo: AgregarClase, CambiarClase, PonerHoras, QuitarFranja y
        QuitarClases, con la repetida y las franjas que se cruzan rechazadas, y
        los choques y las clases sin docente o sin horario en RevisionCiclo;
      - Guardar como y Guardar (el mismo contenido), Renombrar (un nombre
        repetido falla) y Borrar (una guardada si; una aplicada creada a mano y
        la que esta en preparacion, no);
      - con la cuenta de una persona responsable de laboratorio (digbit_consulta,
        db/18): lee las guardadas, no ve la que esta en preparacion, y toda
        escritura falla por Permisos.Exigir y, en la base, con el error 1142.
    Comprueba con CHECKSUM TABLE que clases, horarios, horario_excepciones,
    codigos_accesos, registros_bitacoras y asistencias_dadas (y
    historial_cambios, si esta) no cambian: nada de esto los toca (regla R1).

    Todo lo que crea se llama 'PRUEBA P1 ...' y se BORRA al terminar. Si la base
    ya tiene un horario en preparacion o una aplicada por empezar que no son de
    la prueba, no hace nada (preparar los descartaria). Necesita db/16 y db/23,
    clases que valgan hoy, dos docentes, dos grupos, tres materias, dos
    laboratorios activos y la cuenta de la administracion -Administrador; para
    la parte de solo consulta, db/18 aplicada con root (si no, se salta).
    MODIFICA la base: no lo ejecutes contra la base real del TESCHI.

.EXAMPLE
    .\probar_configuraciones.ps1 -CadenaConexion 'Database=teschi_otru;Server=127.0.0.1;Port=3317;User Id=root;Password='
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
# El log de la prueba, fuera de la carpeta de datos real.
$env:DIGBIT_CARPETA_DATOS = Join-Path $env:TEMP 'digbit_probar_configuraciones'
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
$tDatos    = T 'DigBit.conexion.CicloPreliminarDatos'
$tRevision = T 'DigBit.conexion.RevisionCiclo'
$tFuente   = T 'DigBit.conexion.FuentePreparacion'
$tFranja   = T 'DigBit.conexion.FranjaCiclo'
$tEstado   = T 'DigBit.conexion.EstadoConfiguracion'
$tFiltro   = T 'DigBit.conexion.FiltroClases'
$tUsuario  = T 'DigBit.conexion.Datos_User'
$tConexion = T 'DigBit.Conexion'
$datos = [Activator]::CreateInstance($tDatos, $true)
$publico = [Reflection.BindingFlags]'Public,Instance'

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
        $z = $c.CreateCommand(); $z.CommandText = "SET time_zone = '-06:00'; SET SESSION group_concat_max_len = 1000000"; $null = $z.ExecuteNonQuery()
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

# Los argumentos para MethodInfo.Invoke, sin la envoltura PSObject que les pone
# PowerShell al pasarlos por funciones (si no, Invoke no los convierte).
function Desnudos([object[]]$valores) {
    $limpios = New-Object 'object[]' $valores.Count
    for ($i = 0; $i -lt $valores.Count; $i++) {
        $v = $valores[$i]
        if ($null -ne $v) { $v = $v.psobject.BaseObject }
        $limpios[$i] = $v
    }
    return ,$limpios
}
function Invocar($metodo, $objeto, [object[]]$argumentos) { $metodo.Invoke($objeto, (Desnudos $argumentos)) }

# Un metodo publico de CicloPreliminarDatos: @{ Ok; Valor } o @{ Ok = $false; Error }.
function Llamar([string]$metodo, [object[]]$argumentos) {
    $m = $tDatos.GetMethod($metodo, $publico)
    if ($m -eq $null) { throw "No encuentro CicloPreliminarDatos.$metodo" }
    try { return @{ Ok = $true; Valor = $m.Invoke($script:datos, (Desnudos $argumentos)) } }
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
# Que falle con ese tipo de excepcion (nombre corto) y un mensaje que case con el patron.
function Falla([string]$que, $r, [string]$tipo, [string]$patron) {
    if ($r.Ok) { Ver $que $false 'no fallo'; return }
    $mensaje = $r.Error.Message
    $ok = ($r.Error.GetType().Name -eq $tipo) -and ($mensaje -match $patron)
    Ver $que $ok ($r.Error.GetType().Name + ': ' + $mensaje)
}
function FallaMySql([string]$que, $r, [int]$numero) {
    if ($r.Ok) { Ver $que $false 'no fallo'; return }
    $e = $r.Error
    while ($e -and -not ($e -is [MySql.Data.MySqlClient.MySqlException])) { $e = $e.InnerException }
    Ver $que ($e -ne $null -and $e.Number -eq $numero) ($r.Error.GetType().Name + ': ' + $r.Error.Message)
}

function Estado([string]$n) { [Enum]::Parse($tEstado, $n) }
function Lista([int[]]$valores) {
    $l = New-Object 'System.Collections.Generic.List[int]'
    foreach ($v in $valores) { $l.Add($v) }
    return ,$l
}
# Franjas: @(@(lab, dia, 'HH:mm', 'HH:mm'), ...)
function Franjas([object[]]$filas) {
    # @(@(1, 2, '07:00', '09:00')) llega aplanado: una sola franja.
    if ($filas.Count -gt 0 -and -not ($filas[0] -is [Array])) { $filas = @(,$filas) }
    $l = [Activator]::CreateInstance([System.Collections.Generic.List`1].MakeGenericType($tFranja))
    foreach ($f in $filas) {
        $x = [Activator]::CreateInstance($tFranja)
        $x.LaboratorioId = [int]$f[0]; $x.DiaSemana = [int]$f[1]; $x.Inicio = [TimeSpan]$f[2]; $x.Fin = [TimeSpan]$f[3]
        $l.Add($x)
    }
    return ,$l
}
function DelDia([DateTime]$dia, $labs, [bool]$sinHorario) { Invocar $tFuente.GetMethod('DelDia') $null @($dia, $labs, $sinHorario) }
function DeConfiguracion([int]$id) { $tFuente.GetMethod('DeConfiguracion').Invoke($null, [object[]]@($id)) }
function EnBlanco() { $tFuente.GetMethod('EnBlanco').Invoke($null, [object[]]@()) }
function Revisar($leido) { Invocar $tRevision.GetMethod('Revisar') $null @($leido) }
function Leer([int]$id) { (Llamar 'Leer' @($id)).Valor }

# El contenido de una configuracion: cuantas clases y un MD5 de cada docente,
# grupo, materia y sus franjas, ordenado (no depende de los ids).
function Contenido([int]$id) {
    $t = Sql ("SELECT COUNT(*) AS n, IFNULL(MD5(GROUP_CONCAT(x.firma ORDER BY x.firma SEPARATOR '|')), '') AS h FROM (" +
        "SELECT CONCAT(IFNULL(cc.profesor, '-'), '/', cc.grupo, '/', cc.materia, '=', IFNULL((SELECT GROUP_CONCAT(CONCAT(f.laboratorio, ':', f.dia_semana, ':', " +
        "TIME_FORMAT(f.hora_inicio, '%H:%i'), '-', TIME_FORMAT(f.hora_fin, '%H:%i')) ORDER BY f.dia_semana, f.hora_inicio, f.laboratorio SEPARATOR ';') " +
        "FROM ciclo_franjas f WHERE f.idcarga = cc.idcarga), '')) AS firma FROM ciclo_cargas cc WHERE cc.idciclo = $id AND cc.quitada = 0) x")
    return "$($t.Rows[0]['n'])/$($t.Rows[0]['h'])"
}

$tablasR1 = @('clases', 'horarios', 'horario_excepciones', 'codigos_accesos', 'registros_bitacoras', 'asistencias_dadas', 'historial_cambios') |
    Where-Object { "$(Uno "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '$_'")" -eq '1' }
function Sumas() { ((Sql ('CHECKSUM TABLE ' + ($tablasR1 -join ', '))).Rows | ForEach-Object { "$($_[0])=$($_[1])" }) -join '; ' }
function Limpiar() { $null = Sql "DELETE FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA P1%'" }

# --- Antes de empezar ----------------------------------------------------------
if ("$(Uno "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclo_cargas' AND COLUMN_NAME = 'carga_origen'")" -ne '1') {
    throw 'La base no tiene db/23_horarios_guardados.sql (ni quiza la 16): aplicalas antes.'
}
$ajenos = [int](Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE (estado = 'borrador' OR (estado = 'publicado' AND desde > CURDATE())) AND nombre NOT LIKE 'PRUEBA P1%'")
if ($ajenos -gt 0) { throw 'La base ya tiene un horario en preparacion (o una aplicada por empezar) que no es de esta prueba: preparar lo descartaria. No se hizo nada.' }
Limpiar

$idAdmin = [int]("$(Uno "SELECT idusuarios FROM usuarios WHERE numero_identificador = '$Administrador'")" -replace '^$', '0')
if ($idAdmin -eq 0) { Write-Host "Sin la cuenta ${Administrador}: los cambios quedan sin firmar." -ForegroundColor Yellow }
$hoy = [DateTime](Uno 'SELECT CURDATE()')
$conActivo = "$(Uno "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'laboratorios' AND COLUMN_NAME = 'activo'")" -eq '1'
$labsActivos = @((Sql $(if ($conActivo) { 'SELECT idlaboratorios FROM laboratorios WHERE activo = 1 ORDER BY idlaboratorios' } else { 'SELECT idlaboratorios FROM laboratorios ORDER BY idlaboratorios' })).Rows | ForEach-Object { [int]$_[0] })
$docentes = @((Sql 'SELECT idusuarios FROM usuarios WHERE fk_tipo_usuario = 2 ORDER BY idusuarios LIMIT 2').Rows | ForEach-Object { [int]$_[0] })
$grupos = @((Sql 'SELECT idgrupos FROM grupos ORDER BY idgrupos LIMIT 2').Rows | ForEach-Object { [int]$_[0] })
$materias = @((Sql 'SELECT id_materia FROM materias ORDER BY id_materia LIMIT 3').Rows | ForEach-Object { [int]$_[0] })
if ($labsActivos.Count -lt 2 -or $docentes.Count -lt 2 -or $grupos.Count -lt 2 -or $materias.Count -lt 3) {
    throw 'Hacen falta dos laboratorios activos, dos docentes, dos grupos y tres materias.'
}
$lab1 = $labsActivos[0]; $lab2 = $labsActivos[1]
$p1 = $docentes[0]; $p2 = $docentes[1]; $g1 = $grupos[0]; $g2 = $grupos[1]; $m1 = $materias[0]; $m2 = $materias[1]; $m3 = $materias[2]
$sumasAntes = Sumas
Write-Host "Hoy segun el servidor: $($hoy.ToString('yyyy-MM-dd')); laboratorios activos: $($labsActivos -join ','); administracion: $idAdmin"

try {
    Write-Host "`n== Situacion y nombres ==" -ForegroundColor Cyan
    $s = (Llamar 'LeerSituacion' @()).Valor
    Ver 'LeerSituacion ve las tablas de la 16 y la 23' (-not $s.SinTablas -and -not $s.SinGuardados) "SinTablas=$($s.SinTablas) SinGuardados=$($s.SinGuardados)"
    Ver 'ErrorDeNombre: vacio' ("$($tDatos.GetMethod('ErrorDeNombre').Invoke($null, [object[]]@('  ')))" -eq 'Escribe un nombre.')
    Ver 'ErrorDeNombre: 61 letras' ("$($tDatos.GetMethod('ErrorDeNombre').Invoke($null, [object[]]@('x' * 61)))" -match 'hasta 60 letras')

    Write-Host "`n== Guardar el horario de hoy ==" -ForegroundColor Cyan
    $labs = Lista $labsActivos
    $origen = (Llamar 'ResumirOrigen' @($hoy)).Valor
    $enLabs = @($origen | Where-Object { $_.LaboratorioId -ne $null -and $labsActivos -contains [int]$_.LaboratorioId })
    $esperadas = @($enLabs | ForEach-Object { $_.ClaseId } | Select-Object -Unique).Count
    $esperadosDoc = @($enLabs | ForEach-Object { $_.ProfesorId } | Select-Object -Unique).Count
    $esperadosHor = $enLabs.Count
    $desdeMin = ($origen | ForEach-Object { $_.VigenteDesde } | Measure-Object -Minimum).Minimum
    $hastaMax = ($origen | ForEach-Object { $_.VigenteHasta } | Measure-Object -Maximum).Maximum
    $propuesto = $tDatos.GetMethod('NombrePropuesto').Invoke($null, [object[]]@([DateTime]$desdeMin, [DateTime]$hastaMax))
    Ver "NombrePropuesto del horario de hoy: '$propuesto'" ("$propuesto" -match '^\S.* \d{4}$')
    $r = Llamar 'GuardarDia' @('PRUEBA P1 hoy', $hoy, $labs, $false, $idAdmin)
    if (-not (Bien 'GuardarDia(hoy, laboratorios activos)' $r)) { throw 'Sin la guardada no se puede seguir.' }
    $gHoy = $r.Valor.CicloId
    Ver "las mismas cuentas que ResumirOrigen: $($r.Valor.Clases) clases de $($r.Valor.Docentes) docentes con $($r.Valor.Horarios) horarios" `
        ($r.Valor.Clases -eq $esperadas -and $r.Valor.Docentes -eq $esperadosDoc -and $r.Valor.Horarios -eq $esperadosHor) "ResumirOrigen: $esperadas, $esperadosDoc, $esperadosHor"
    $fila = (Sql "SELECT estado, copiado_del, origen_ciclo, desde, cambiado_por FROM ciclos_preliminares WHERE idciclo = $gHoy").Rows[0]
    Ver 'queda guardada, del dia de hoy, sin fechas ni origen' ($fila['estado'] -eq 'guardado' -and ([DateTime]$fila['copiado_del']).Date -eq $hoy -and $fila['origen_ciclo'] -is [DBNull] -and $fila['desde'] -is [DBNull])
    $s = (Llamar 'LeerSituacion' @()).Valor
    $enLista = @($s.Configuraciones | Where-Object { $_.Id -eq $gHoy })
    Ver 'sale en la lista con sus cuentas y quien la guardo' ($enLista.Count -eq 1 -and $enLista[0].Clases -eq $r.Valor.Clases -and $enLista[0].Horarios -eq $r.Valor.Horarios -and
        $enLista[0].Docentes -eq $r.Valor.Docentes -and $enLista[0].Guardada -and ($idAdmin -eq 0 -or $enLista[0].CambiadoPor -eq $Administrador)) `
        "$($enLista[0].Clases) clases, $($enLista[0].Horas) horas en $($enLista[0].Laboratorios) laboratorios, por $($enLista[0].CambiadoPor)"
    Falla 'otra guardada con el mismo nombre (otras mayusculas)' (Llamar 'GuardarDia' @('prueba p1 HOY', $hoy, $labs, $false, $idAdmin)) 'ErrorDeDatos' 'Ya hay una plantilla guardada que se llama .PRUEBA P1 hoy.: usa otro nombre'
    Falla 'sin laboratorios ni las sin horario' (Llamar 'GuardarDia' @('PRUEBA P1 sin labs', $hoy, (Lista @()), $false, $idAdmin)) 'ErrorDeDatos' 'Elige al menos un laboratorio'
    Falla 'un dia sin clases' (Llamar 'GuardarDia' @('PRUEBA P1 vacio', [DateTime]'2000-01-03', $labs, $true, $idAdmin)) 'ErrorDeDatos' 'no hay clases que guardar'
    Ver '... y no deja nada a medias' ("$(Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE nombre = 'PRUEBA P1 vacio'")" -eq '0')
    $libre = (Llamar 'NombreLibre' @('PRUEBA P1 hoy', (Estado 'Guardada'))).Valor
    Ver "NombreLibre propone '$libre'" ($libre -eq 'PRUEBA P1 hoy (2)')
    Ver 'en preparacion el nombre no se compara' ((Llamar 'NombreLibre' @('PRUEBA P1 hoy', (Estado 'EnPreparacion'))).Valor -eq 'PRUEBA P1 hoy')

    Write-Host "`n== Preparar ==" -ForegroundColor Cyan
    $r = Llamar 'Preparar' @('PRUEBA P1 Proximo ciclo', (DelDia $hoy $labs $true), $false, $idAdmin)
    if (-not (Bien 'Preparar desde hoy (con las sin horario)' $r)) { throw 'Sin el horario en preparacion no se puede seguir.' }
    $bHoy = $r.Valor.CicloId
    $sinHorario = @($origen | Where-Object { $_.LaboratorioId -eq $null } | ForEach-Object { $_.ClaseId } | Select-Object -Unique).Count
    Ver "copia $($r.Valor.Clases) clases ($esperadas con horas y $sinHorario sin horario)" ($r.Valor.Clases -eq $esperadas + $sinHorario -and $r.Valor.Horarios -eq $esperadosHor)
    $s = (Llamar 'LeerSituacion' @()).Valor
    Ver 'LeerSituacion lo da como el que esta en preparacion' ($s.Borrador -ne $null -and $s.Borrador.Id -eq $bHoy -and -not ($s.Configuraciones | Where-Object { $_.Id -eq $bHoy }))
    Falla 'otro sin reemplazar' (Llamar 'Preparar' @('PRUEBA P1 otro', (EnBlanco), $false, $idAdmin)) 'ErrorDeDatos' 'Ya hay un horario en preparaci.n'
    $r = Llamar 'Preparar' @('PRUEBA P1 Proximo ciclo', (DeConfiguracion $gHoy), $true, $idAdmin)
    if (-not (Bien 'Preparar desde la guardada, reemplazando' $r)) { throw 'No se pudo preparar desde la guardada.' }
    $bGuardada = $r.Valor.CicloId
    Ver '... el anterior desaparece' ("$(Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE idciclo = $bHoy")" -eq '0')
    Ver '... con el mismo contenido que la guardada (misma firma por clase)' ((Contenido $bGuardada) -eq (Contenido $gHoy)) "$(Contenido $bGuardada) / $(Contenido $gHoy)"
    $leido = Leer $bGuardada
    $rev = Revisar $leido
    $firmas = @($leido.Cargas | Where-Object { (Invocar $tRevision.GetMethod('Firma') $null @(,$_.Franjas)) -ne "$($_.HorarioOrigen)" }).Count
    Ver '... cada clase igual que en la guardada (Como queda: Igual)' ($rev.ConCambios -eq 0 -and $firmas -eq 0) "ConCambios=$($rev.ConCambios), firmas distintas=$firmas"
    Ver "... se guarda en ella con Guardar ($($leido.Ciclo.FrenteA))" ($leido.Ciclo.SeGuardaEnOrigen -and $leido.Ciclo.OrigenId -eq $gHoy -and $leido.Ciclo.FrenteA -match 'PRUEBA P1 hoy')
    $r = Llamar 'Preparar' @('PRUEBA P1 Proximo ciclo', (EnBlanco), $true, $idAdmin)
    if (-not (Bien 'Preparar en blanco, reemplazando' $r)) { throw 'No se pudo preparar en blanco.' }
    $b = $r.Valor.CicloId
    Ver '... sin clases y sin el anterior' ($r.Valor.Clases -eq 0 -and "$(Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE idciclo = $bGuardada")" -eq '0')
    Ver '... sin donde guardarse' (-not (Leer $b).Ciclo.SeGuardaEnOrigen)

    Write-Host "`n== Editar ==" -ForegroundColor Cyan
    $r = Llamar 'AgregarClase' @($b, $p1, $g1, $m1, (Franjas @(@($lab1, 1, '07:00', '09:00'), @($lab1, 2, '07:00', '09:00'))), $idAdmin)
    Bien 'AgregarClase con dos horarios' $r | Out-Null
    $a = $r.Valor
    Ver '... con sus dos franjas' ("$(Uno "SELECT COUNT(*) FROM ciclo_franjas WHERE idcarga = $a")" -eq '2')
    Falla 'la misma docente, grupo y materia otra vez' (Llamar 'AgregarClase' @($b, $p1, $g1, $m1, (Franjas @()), $idAdmin)) 'ErrorDeDatos' 'ya tiene .+ con .+ en este horario'
    Falla 'dos horarios suyos que se cruzan (en laboratorios distintos)' (Llamar 'AgregarClase' @($b, $p2, $g1, $m2, (Franjas @(@($lab1, 1, '08:00', '10:00'), @($lab2, 1, '09:00', '11:00'))), $idAdmin)) 'ErrorDeDatos' 'Dos horarios de esta clase se cruzan el Lunes'
    Falla 'fin antes del inicio' (Llamar 'AgregarClase' @($b, $p2, $g1, $m2, (Franjas @(@($lab1, 3, '10:00', '09:00'))), $idAdmin)) 'ErrorDeDatos' 'hora de fin tiene que ser despu.s'
    Falla 'un laboratorio que no existe' (Llamar 'AgregarClase' @($b, $p2, $g1, $m2, (Franjas @(@(999999, 3, '07:00', '08:00'))), $idAdmin)) 'ErrorDeDatos' 'laboratorios ya no est'
    Falla 'un grupo que no existe' (Llamar 'AgregarClase' @($b, $p2, 999999, $m2, (Franjas @()), $idAdmin)) 'ErrorDeDatos' 'Ese grupo ya no est'
    # Un choque es el mismo grupo (o docente) a la misma hora en OTRO laboratorio
    # (2026-10-31); en el mismo laboratorio se reemplaza (probar_choques.ps1).
    $r = Llamar 'AgregarClase' @($b, $null, $g1, $m2, (Franjas @(@($lab2, 1, '08:00', '10:00'))), $idAdmin)
    Bien 'AgregarClase sin docente que choca (el mismo grupo en otro laboratorio)' $r | Out-Null
    $bb = $r.Valor
    $r = Llamar 'AgregarClase' @($b, $p2, $g2, $m3, (Franjas @()), $idAdmin)
    Bien 'AgregarClase sin horario' $r | Out-Null
    $c = $r.Valor
    $leido = Leer $b
    $rev = Revisar $leido
    $cargaA = @($leido.Cargas | Where-Object { $_.Id -eq $a })[0]
    $cargaB = @($leido.Cargas | Where-Object { $_.Id -eq $bb })[0]
    $filtroChoques = [Enum]::Parse($tFiltro, 'Choques')
    $mFiltro = $tRevision.GetMethod('EntraEnFiltro')
    Ver "RevisionCiclo: $($rev.Choques.Count) choque, $($rev.SinProfesor) sin docente, $($rev.SinHorario) sin horario" ($rev.Choques.Count -eq 1 -and $rev.SinProfesor -eq 1 -and $rev.SinHorario -eq 1 -and -not $rev.Choques[0].MismoLaboratorio -and $rev.Choques[0].MismoGrupo -and $rev.Encimadas.Count -eq 0)
    Ver '... el filtro Choques da las dos que chocan' ((Invocar $mFiltro $null @($leido, $rev, $cargaA, $filtroChoques)) -and (Invocar $mFiltro $null @($leido, $rev, $cargaB, $filtroChoques)))
    Ver '... la sin docente es la primera de la lista' ($leido.Cargas[0].Id -eq $bb)
    Ver '... dos docentes en la leyenda' ($leido.Profesores.Count -eq 2)
    Bien 'CambiarClase: otra persona docente y otro horario' (Llamar 'CambiarClase' @($bb, $p2, $g2, $m2, (Franjas @(@($lab2, 3, '10:00', '12:00'))), $idAdmin)) | Out-Null
    $fila = (Sql "SELECT cc.profesor, (SELECT GROUP_CONCAT(CONCAT(f.laboratorio, ':', f.dia_semana)) FROM ciclo_franjas f WHERE f.idcarga = cc.idcarga) AS fr FROM ciclo_cargas cc WHERE cc.idcarga = $bb").Rows[0]
    Ver '... quedo como se pidio' ("$($fila['profesor'])" -eq "$p2" -and "$($fila['fr'])" -eq "${lab2}:3")
    Ver '... y ya no hay choques' ((Revisar (Leer $b)).Choques.Count -eq 0)
    Falla 'CambiarClase a una terna que ya esta' (Llamar 'CambiarClase' @($bb, $p1, $g1, $m1, (Franjas @()), $idAdmin)) 'ErrorDeDatos' 'ya tiene'
    Bien 'PonerHoras: el jueves' (Llamar 'PonerHoras' @($a, (Franjas @(@($lab1, 4, '07:00', '09:00'))), $idAdmin)) | Out-Null
    Ver '... tres franjas' ("$(Uno "SELECT COUNT(*) FROM ciclo_franjas WHERE idcarga = $a")" -eq '3')
    Falla 'PonerHoras que se cruzan con las suyas' (Llamar 'PonerHoras' @($a, (Franjas @(@($lab2, 1, '08:00', '09:00'))), $idAdmin)) 'ErrorDeDatos' 'se cruzan el Lunes'
    $jueves = [int](Uno "SELECT idfranja FROM ciclo_franjas WHERE idcarga = $a AND dia_semana = 4")
    Bien 'QuitarFranja (el jueves)' (Llamar 'QuitarFranja' @($jueves, $idAdmin)) | Out-Null
    Ver '... dos franjas otra vez' ("$(Uno "SELECT COUNT(*) FROM ciclo_franjas WHERE idcarga = $a")" -eq '2')
    Falla 'QuitarFranja otra vez' (Llamar 'QuitarFranja' @($jueves, $idAdmin)) 'ErrorDeDatos' 'ya no est'
    $r = Llamar 'QuitarClases' @((Lista @($c)), $idAdmin)
    Ver 'QuitarClases (la sin horario)' ($r.Ok -and $r.Valor -eq 1 -and "$(Uno "SELECT COUNT(*) FROM ciclo_cargas WHERE idcarga = $c")" -eq '0')
    Bien 'GuardarFechas' (Llamar 'GuardarFechas' @($b, [DateTime]'2027-02-02', [DateTime]'2027-07-01', $idAdmin)) | Out-Null
    Falla 'GuardarFechas con el ultimo antes del primero' (Llamar 'GuardarFechas' @($b, [DateTime]'2027-02-02', [DateTime]'2027-01-01', $idAdmin)) 'ErrorDeDatos' 'no puede ser antes del primero'
    Bien 'Renombrar el que se prepara como una guardada (se puede)' (Llamar 'Renombrar' @($b, 'PRUEBA P1 hoy', $idAdmin)) | Out-Null
    $fila = (Sql "SELECT nombre, desde, cambiado_por FROM ciclos_preliminares WHERE idciclo = $b").Rows[0]
    Ver '... con su nombre, sus fechas y quien lo cambio' ($fila['nombre'] -eq 'PRUEBA P1 hoy' -and ([DateTime]$fila['desde']).Date -eq [DateTime]'2027-02-02' -and ($idAdmin -eq 0 -or "$($fila['cambiado_por'])" -eq "$idAdmin"))

    Write-Host "`n== Guardar como y Guardar ==" -ForegroundColor Cyan
    Falla 'Guardar como con el nombre de otra guardada' (Llamar 'GuardarComo' @($b, 'PRUEBA P1 HOY', $idAdmin)) 'ErrorDeDatos' 'Ya hay una plantilla guardada'
    Falla 'Guardar sin donde guardarse' (Llamar 'GuardarEn' @($b, $idAdmin)) 'ErrorDeDatos' 'Guardar como'
    $r = Llamar 'GuardarComo' @($b, 'PRUEBA P1 copia', $idAdmin)
    Bien 'Guardar como' $r | Out-Null
    $gCopia = $r.Valor.CicloId
    Ver '... el mismo contenido' ((Contenido $gCopia) -eq (Contenido $b)) "$(Contenido $gCopia) / $(Contenido $b)"
    $leido = Leer $b
    Ver "... y desde ahora se guarda en ella ($($leido.Ciclo.FrenteA))" ($leido.Ciclo.SeGuardaEnOrigen -and $leido.Ciclo.OrigenId -eq $gCopia -and (Revisar $leido).ConCambios -eq 0)
    Bien 'otra clase en el que se prepara' (Llamar 'AgregarClase' @($b, $p1, $g2, $m3, (Franjas @(@($lab2, 5, '07:00', '08:00'))), $idAdmin)) | Out-Null
    Ver '... sale como Nueva' ((Revisar (Leer $b)).ConCambios -eq 1)
    $r = Llamar 'GuardarEn' @($b, $idAdmin)
    Bien 'Guardar (en la de origen)' $r | Out-Null
    Ver "... la reemplaza: $($r.Valor.Clases) clases, el mismo contenido" ($r.Valor.CicloId -eq $gCopia -and $r.Valor.Clases -eq 3 -and (Contenido $gCopia) -eq (Contenido $b)) "$(Contenido $gCopia) / $(Contenido $b)"

    Write-Host "`n== Renombrar y borrar ==" -ForegroundColor Cyan
    Falla 'Renombrar una guardada con el nombre de otra' (Llamar 'Renombrar' @($gCopia, 'PRUEBA P1 hoy', $idAdmin)) 'ErrorDeDatos' 'Ya hay una plantilla guardada que se llama'
    Bien 'Renombrar una guardada' (Llamar 'Renombrar' @($gCopia, 'PRUEBA P1 copia 2', $idAdmin)) | Out-Null
    Ver '... con el nombre nuevo' ("$(Uno "SELECT nombre FROM ciclos_preliminares WHERE idciclo = $gCopia")" -eq 'PRUEBA P1 copia 2')
    $null = Sql "INSERT INTO ciclos_preliminares (nombre, desde, hasta, estado, creado_en, cambiado_en, fin_anterior, publicado_en) VALUES ('PRUEBA P1 aplicada', '2020-01-06', '2020-06-30', 'publicado', NOW(), NOW(), '2020-01-03', NOW())"
    $aplicada = [int](Uno "SELECT idciclo FROM ciclos_preliminares WHERE nombre = 'PRUEBA P1 aplicada'")
    Falla 'Borrar una aplicada' (Llamar 'Borrar' @($aplicada)) 'ErrorDeDatos' 'las aplicadas quedan como registro'
    Falla 'Borrar la que esta en preparacion' (Llamar 'Borrar' @($b)) 'ErrorDeDatos' 'Descartar'
    Bien 'Renombrar una aplicada (en el registro)' (Llamar 'Renombrar' @($aplicada, 'PRUEBA P1 aplicada 2020', $idAdmin)) | Out-Null
    Bien 'Borrar una guardada' (Llamar 'Borrar' @($gCopia)) | Out-Null
    Ver '... sin sus clases' ("$(Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE idciclo = $gCopia")" -eq '0' -and "$(Uno "SELECT COUNT(*) FROM ciclo_cargas WHERE idciclo = $gCopia")" -eq '0')
    $leido = Leer $b
    Ver '... y el que se prepara se queda sin donde guardarse' (-not $leido.Ciclo.SeGuardaEnOrigen -and $leido.Ciclo.FrenteA -match 'ya se borr')
    Falla '... asi que Guardar pide Guardar como' (Llamar 'GuardarEn' @($b, $idAdmin)) 'ErrorDeDatos' 'Guardar como'
    $s = (Llamar 'LeerSituacion' @()).Valor
    $ap = @($s.Configuraciones | Where-Object { $_.Id -eq $aplicada })
    Ver 'la aplicada sale en la lista, ya terminada' ($ap.Count -eq 1 -and $ap[0].Aplicada -and -not $ap[0].EnUso($hoy) -and -not $ap[0].PorEmpezar($hoy) -and $s.PorEmpezar -eq $null)

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
                $s = (Llamar 'LeerSituacion' @()).Valor
                Ver 'LeerSituacion con digbit_consulta ve las tablas' (-not $s.SinTablas -and -not $s.SinGuardados) "SinTablas=$($s.SinTablas) SinGuardados=$($s.SinGuardados)"
                Ver '... lee las guardadas y las aplicadas' (@($s.Configuraciones | Where-Object { $_.Id -eq $gHoy -or $_.Id -eq $aplicada }).Count -eq 2)
                Ver '... y no ve la que esta en preparacion' ($s.Borrador -eq $null)
                $r = Llamar 'Leer' @($gHoy)
                Ver 'Leer una guardada' ($r.Ok -and $r.Valor.Cargas.Count -gt 0) $(if ($r.Ok) { "$($r.Valor.Cargas.Count) clases" } else { $r.Error.Message })
                Falla 'Leer la que esta en preparacion' (Llamar 'Leer' @($b)) 'ErrorDeDatos' 'solo lo ve la administraci.n'
                $escrituras = [ordered]@{
                    'GuardarDia'    = @('PRUEBA P1 x', $hoy, $labs, $false, $idAdmin)
                    'Preparar'      = @('PRUEBA P1 x', (EnBlanco), $true, $idAdmin)
                    'GuardarComo'   = @($b, 'PRUEBA P1 x', $idAdmin)
                    'GuardarEn'     = @($b, $idAdmin)
                    'Renombrar'     = @($gHoy, 'PRUEBA P1 x', $idAdmin)
                    'Borrar'        = @($gHoy)
                    'Descartar'     = @($b)
                    'GuardarFechas' = @($b, $null, $null, $idAdmin)
                    'AgregarClase'  = @($b, $p1, $g1, $m2, (Franjas @()), $idAdmin)
                    'CambiarClase'  = @($a, $p1, $g1, $m1, (Franjas @()), $idAdmin)
                    'PonerHoras'    = @($a, (Franjas @(@($lab1, 6, '07:00', '08:00'))), $idAdmin)
                    'QuitarFranja'  = @(0, $idAdmin)
                    'QuitarClases'  = @((Lista @($a)), $idAdmin)
                }
                foreach ($metodo in $escrituras.Keys) {
                    Falla "$metodo no se deja (Permisos.Exigir)" (Llamar $metodo ([object[]]$escrituras[$metodo])) 'AccionNoPermitida' 'Solo la administraci.n'
                }

                # Aunque la pantalla se equivocara (otro tipo de usuario con esta
                # cuenta), la base rechaza escribir: error 1142.
                $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 3)
                FallaMySql 'GuardarDia con digbit_consulta: la base lo rechaza (1142)' (Llamar 'GuardarDia' @('PRUEBA P1 x', $hoy, $labs, $false, $idAdmin)) 1142
                FallaMySql 'Renombrar con digbit_consulta (1142)' (Llamar 'Renombrar' @($gHoy, 'PRUEBA P1 x', $idAdmin)) 1142
                FallaMySql 'Borrar con digbit_consulta (1142)' (Llamar 'Borrar' @($gHoy)) 1142
                FallaMySql 'AgregarClase con digbit_consulta (1142)' (Llamar 'AgregarClase' @($b, $p1, $g1, $m2, (Franjas @()), $idAdmin)) 1142
                FallaMySql 'Descartar con digbit_consulta (1142)' (Llamar 'Descartar' @($b)) 1142
            }
            finally {
                $tConexion.GetMethod('UsarCuentaDelEquipo', $bf).Invoke($null, [object[]]@()) | Out-Null
                $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 0)
            }
            Ver '... y nada cambio' ("$(Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA P1 x%'")" -eq '0' -and "$(Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE idciclo IN ($gHoy, $b)")" -eq '2')
        }
    }

    Write-Host "`n== Descartar ==" -ForegroundColor Cyan
    Bien 'Descartar el que se prepara' (Llamar 'Descartar' @($b)) | Out-Null
    Ver '... sin sus clases' ("$(Uno "SELECT COUNT(*) FROM ciclo_cargas WHERE idciclo = $b")" -eq '0')
    Falla 'Descartar otra vez' (Llamar 'Descartar' @($b)) 'ErrorDeDatos' 'vuelve a entrar a la pesta'
}
finally {
    Limpiar
    $sumasDespues = Sumas
    Ver 'CHECKSUM igual antes y despues (clases, horarios, excepciones, sesiones, bitacoras y asistencias)' ($sumasAntes -eq $sumasDespues) $(if ($sumasAntes -ne $sumasDespues) { "antes: $sumasAntes / despues: $sumasDespues" } else { '' })
    Ver 'no quedan datos de la prueba' ("$(Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA P1%'")" -eq '0')
}

Write-Host "`nResultado: $($script:total - $script:malos)/$($script:total) correctas" -ForegroundColor $(if ($script:malos -eq 0) { 'Green' } else { 'Red' })
if ($script:malos -gt 0) { exit 1 }
