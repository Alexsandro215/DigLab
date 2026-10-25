<#
.SYNOPSIS
    Prueba Aplicar y Deshacer un horario (Horarios > Aplicacion, db/16 y db/23)
    contra una base de DESARROLLO.

.DESCRIPTION
    Carga DigBit.exe por reflexion y recorre CicloPreliminarDatos.Aplicar.cs y
    RevisionAplicacion:
      - prepara (con la capa de configuraciones) una copia del horario de hoy
        con algun cambio: se quitan las clases que chocan y se anade una clase
        nueva, el dia de la semana de hoy, de una persona docente que hoy no
        tiene horas (si hay una sin codigo, esa: asi se prueba que lo recibe);
      - lo que no deja aplicar, llamando a Aplicar por reflexion (se revisa
        dentro de su transaccion y no cambia nada): fechas, un ultimo dia del
        horario de ahora anterior a una sesion ya registrada, una clase sin
        docente, de una persona docente de baja, en un laboratorio
        desactivado, repetida, igual que una que ya esta en Clases en esas
        fechas, con un choque con ella, y un nombre repetido en el registro;
      - aplicar con el ultimo dia del de ahora en la vispera: se recortan las
        que valen ese dia (tambien una futura que empiece antes), las nuevas
        llevan el codigo de su docente y franjas sin fecha propia, un solo
        renglon en el historial y su alerta para otra cuenta, y la guardada
        "como quedo";
      - un DigBit sin db/20 (-ExeAnterior, en un proceso aparte) y este no
        dejan entrar con el codigo antes del primer dia, y el dia nuevo ven las
        nuevas y no las recortadas;
      - lo que no deja deshacer: una sesion o una asistencia dada en una clase
        nueva, que ya empezo, otro horario en preparacion y la cuenta de solo
        consulta (Permisos.Exigir y, en la base, el error 1142);
      - Deshacer deja clases y horarios con el mismo contenido que antes de
        aplicar (MD5 de cada fila, ordenado; el AUTO_INCREMENT puede cambiar) y
        el mismo CHECKSUM TABLE de clases, horarios, horario_excepciones,
        codigos_accesos, registros_bitacoras y asistencias_dadas; y no toca
        una clase a la que se le cambio la fecha despues ni una que se
        cruzaria con otra igual.
    Todo lo que crea se llama 'PRUEBA P4 ...' o 'PRBP4' y se BORRA al terminar:
    las tablas del ciclo quedan sin nada de la prueba y lo demas igual que al
    empezar (CHECKSUM de las tablas de arriba y de usuarios, laboratorios,
    configuracion, grupos, materias e historial_cambios). Si la base ya tiene
    un horario en preparacion o una aplicada por empezar que no son de la
    prueba, no hace nada. Las fechas por omision (del 11/01/2027 al 30/06/2027)
    son futuras: lo que ya paso no se toca.
    MODIFICA la base: solo contra una base local de desarrollo (127.0.0.1).

.EXAMPLE
    .\probar_aplicar.ps1 -CadenaConexion 'Database=teschi_otru;Server=127.0.0.1;Port=3317;User Id=root;Password=' -ExeAnterior C:\viejo\DigBit.exe

.EXAMPLE
    .\probar_aplicar.ps1 -CadenaConexion 'Database=teschi_otru;Server=127.0.0.1;Port=3318;User Id=root;Password=' -Administrador ADMIN001 -SinConsulta
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CadenaConexion,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Otro DigBit.exe; por defecto, el de DigBit\bin\<Configuration>.
    [string]$Exe,

    # numero_identificador de la cuenta de la administracion que aplica.
    [string]$Administrador = 'ADMPRUEBA',

    # Un DigBit.exe anterior a la 20 (sin la vigencia de las franjas), para ver que no deja entrar antes de tiempo.
    [string]$ExeAnterior,

    # Sin la parte de solo consulta (digbit_consulta).
    [switch]$SinConsulta,

    # Primer y ultimo dia del horario nuevo (el ultimo del de ahora es la vispera).
    [DateTime]$Desde = [DateTime]'2027-01-11',
    [DateTime]$Hasta = [DateTime]'2027-06-30',

    # Uso interno: el proceso aparte que carga el DigBit anterior a la 20 (solo lee).
    [switch]$Anterior,
    [int]$Laboratorio,
    [int]$ClaseNueva,
    [string]$Codigo,
    [string]$DiaNuevo,
    [string]$Recortadas,
    [int]$Alumno
)

$ErrorActionPreference = 'Stop'
if ($CadenaConexion -match 'teschi\.edu\.mx' -or $CadenaConexion -notmatch 'Server=(127\.0\.0\.1|localhost)(;|$)') {
    throw 'Esta prueba escribe en la base: solo contra una base local de desarrollo (Server=127.0.0.1), nunca contra la de la escuela.'
}
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot "..\DigBit\bin\$Configuration\DigBit.exe" }
$Exe = (Resolve-Path $Exe).Path
$script:carpeta = Split-Path $Exe

$env:DIGBIT_CONNECTION_STRING = $CadenaConexion
# El log de la prueba, fuera de la carpeta de datos real.
$env:DIGBIT_CARPETA_DATOS = Join-Path $env:TEMP 'digbit_probar_aplicar'
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
$publico = [Reflection.BindingFlags]'Public,Instance'
function T([string]$n) { $app.GetType($n, $true) }

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
        $z = $c.CreateCommand(); $z.CommandText = "SET time_zone = '-06:00'; SET SESSION group_concat_max_len = 4000000"; $null = $z.ExecuteNonQuery()
        $cmd = $c.CreateCommand(); $cmd.CommandText = $texto
        $t = New-Object System.Data.DataTable
        $r = $cmd.ExecuteReader(); $t.Load($r); $r.Close()
        return ,$t
    } finally { $c.Close() }
}
function Uno([string]$texto) {
    $t = Sql $texto
    if ($t.Rows.Count -eq 0 -or $t.Rows[0][0] -is [DBNull]) { return $null }
    $valor = $t.Rows[0][0]
    # MariaDB 5.5 da CONCAT con fechas o numeros como cadena binaria.
    if ($valor -is [byte[]]) { return [Text.Encoding]::UTF8.GetString($valor) }
    return $valor
}
# Un INSERT y su id.
function Insertar([string]$texto) {
    $c = New-Object MySql.Data.MySqlClient.MySqlConnection($CadenaConexion)
    $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = "SET time_zone = '-06:00'"; $null = $cmd.ExecuteNonQuery()
        $cmd.CommandText = $texto; $null = $cmd.ExecuteNonQuery()
        $cmd.CommandText = 'SELECT LAST_INSERT_ID()'; return [int]$cmd.ExecuteScalar()
    } finally { $c.Close() }
}
function Existe([string]$tabla, [string]$columna = '') {
    if ($columna) { return "$(Uno "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '$tabla' AND COLUMN_NAME = '$columna'")" -eq '1' }
    return "$(Uno "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '$tabla'")" -eq '1'
}
function F([DateTime]$d) { $d.ToString('yyyy-MM-dd') }
function Dmy([DateTime]$d) { $d.ToString('dd/MM/yyyy') }

# Los argumentos para MethodInfo.Invoke, sin la envoltura PSObject de PowerShell.
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
function Lista([int[]]$valores) {
    $l = New-Object 'System.Collections.Generic.List[int]'
    foreach ($v in $valores) { $l.Add($v) }
    return ,$l
}

# ---------------------------------------------------------------------------
# Modo aparte: el DigBit anterior a la 20 (solo lee; lo lanza la prueba).
# ---------------------------------------------------------------------------
if ($Anterior) {
    $tH = T 'DigBit.conexion.HorariosDatos'
    $hd = [Activator]::CreateInstance($tH, $true)
    Ver "DigBit sin la vigencia de las franjas (no tiene VigenciaFranjas): $($app.GetName().Version), $Exe" (-not ($app.GetTypes() | Where-Object { $_.Name -eq 'VigenciaFranjas' }))
    $hoyA = [DateTime](Uno 'SELECT CURDATE()')
    $dia = [DateTime]$DiaNuevo
    $agenda = @($tH.GetMethod('AgendaDelDia', [Type[]]@([int], [DateTime])).Invoke($hd, [object[]]@($Laboratorio, $hoyA)))
    Ver "[anterior a la 20] hoy ($(Dmy $hoyA)) el laboratorio $Laboratorio no ensena la clase nueva $ClaseNueva" (-not ($agenda | Where-Object { $_.ClaseId -eq $ClaseNueva })) "$($agenda.Count) en su agenda"
    $agenda = @($tH.GetMethod('AgendaDelDia', [Type[]]@([int], [DateTime])).Invoke($hd, [object[]]@($Laboratorio, $dia)))
    Ver "[anterior a la 20] el $(Dmy $dia) si la ensena" (@($agenda | Where-Object { $_.ClaseId -eq $ClaseNueva }).Count -eq 1)
    $cortadas = @($Recortadas -split ',' | Where-Object { $_ } | ForEach-Object { [int]$_ })
    if ($cortadas.Count -gt 0) {
        $labs = @((Sql "SELECT DISTINCT laboratorios_idlaboratorios FROM horarios WHERE clases_idclases IN ($($cortadas -join ',')) AND dia_semana = WEEKDAY('$(F $dia)') + 1").Rows | ForEach-Object { [int]$_[0] })
        $vistas = 0
        foreach ($lab in $labs) {
            $vistas += @(@($tH.GetMethod('AgendaDelDia', [Type[]]@([int], [DateTime])).Invoke($hd, [object[]]@($lab, $dia))) | Where-Object { $cortadas -contains $_.ClaseId }).Count
        }
        Ver "[anterior a la 20] el $(Dmy $dia) no ensena ninguna de las recortadas ($($labs.Count) laboratorios con sus horas ese dia)" ($vistas -eq 0) "$vistas vistas"
    }
    $antes = [int](Uno 'SELECT COUNT(*) FROM codigos_accesos')
    $r = $tH.GetMethod('ResolverCodigo').Invoke($hd, [object[]]@($Codigo, $Laboratorio, $Alumno, $null))
    $despues = [int](Uno 'SELECT COUNT(*) FROM codigos_accesos')
    Ver "[anterior a la 20] hoy el codigo del docente no deja entrar en el laboratorio $Laboratorio" (-not $r.Aceptado -and $antes -eq $despues) "$($r.Motivo): $($r.Mensaje)"
    exit $(if ($script:malos -eq 0) { 0 } else { 1 })
}

# ---------------------------------------------------------------------------
# La prueba
# ---------------------------------------------------------------------------
$tDatos     = T 'DigBit.conexion.CicloPreliminarDatos'
$tRevision  = T 'DigBit.conexion.RevisionCiclo'
$tRevAp     = T 'DigBit.conexion.RevisionAplicacion'
$tFuente    = T 'DigBit.conexion.FuentePreparacion'
$tFranja    = T 'DigBit.conexion.FranjaCiclo'
$tUsuario   = T 'DigBit.conexion.Datos_User'
$tConexion  = T 'DigBit.Conexion'
$tHorarios  = T 'DigBit.conexion.HorariosDatos'
$tHistorial = T 'DigBit.conexion.HistorialCambios'
$datos = [Activator]::CreateInstance($tDatos, $true)
$horarios = [Activator]::CreateInstance($tHorarios, $true)

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
function Falla([string]$que, $r, [string]$tipo, [string]$patron) {
    if ($r.Ok) { Ver $que $false 'no fallo'; return }
    $mensaje = $r.Error.Message
    Ver $que (($r.Error.GetType().Name -eq $tipo) -and ($mensaje -match $patron)) ($r.Error.GetType().Name + ': ' + $mensaje)
}
function FallaMySql([string]$que, $r, [int]$numero) {
    if ($r.Ok) { Ver $que $false 'no fallo'; return }
    $e = $r.Error
    while ($e -and -not ($e -is [MySql.Data.MySqlClient.MySqlException])) { $e = $e.InnerException }
    Ver $que ($e -ne $null -and $e.Number -eq $numero) ($r.Error.GetType().Name + ': ' + $r.Error.Message)
}
# Franjas: @(@(lab, dia, 'HH:mm', 'HH:mm'), ...)
function Franjas([object[]]$filas) {
    if ($filas.Count -gt 0 -and -not ($filas[0] -is [Array])) { $filas = @(,$filas) }
    $l = [Activator]::CreateInstance([System.Collections.Generic.List`1].MakeGenericType($tFranja))
    foreach ($f in $filas) {
        $x = [Activator]::CreateInstance($tFranja)
        $x.LaboratorioId = [int]$f[0]; $x.DiaSemana = [int]$f[1]; $x.Inicio = [TimeSpan]$f[2]; $x.Fin = [TimeSpan]$f[3]
        $l.Add($x)
    }
    return ,$l
}
function Leer([int]$id) { (Llamar 'Leer' @($id)).Valor }
function Revisar($leido) { Invocar $tRevision.GetMethod('Revisar') $null @($leido) }
function Renglones($d, [DateTime]$de, [DateTime]$a, [DateTime]$fin, [string]$nombre) { @(Invocar $tRevAp.GetMethod('Revisar') $null @($d, $de, $a, $fin, $nombre)) }
function Rojos($renglones) { @($renglones | Where-Object { "$($_.Nivel)" -eq 'Rojo' }) }

$conVigencia = Existe 'horarios' 'vigente_desde'
$conHistorial = Existe 'historial_cambios'
$conDadas = Existe 'asistencias_dadas'
$conBajas = Existe 'usuarios' 'activo'
$conLabActivo = Existe 'laboratorios' 'activo'
$conModo = Existe 'laboratorios' 'modo_codigo'

# El contenido de una tabla, fila por fila y ordenado (no depende del AUTO_INCREMENT).
function ContenidoClases([string]$donde = '1 = 1') {
    Uno ("SELECT CONCAT(COUNT(*), '/', IFNULL(MD5(GROUP_CONCAT(CONCAT_WS('|', idclases, usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, " +
        "vigente_desde, vigente_hasta, activa, IFNULL(creada_por, '-'), creada_en) ORDER BY idclases SEPARATOR ';')), '')) FROM clases WHERE $donde")
}
function ContenidoHorarios() {
    $propias = $(if ($conVigencia) { ", IFNULL(vigente_desde, '-'), IFNULL(vigente_hasta, '-')" } else { '' })
    Uno ("SELECT CONCAT(COUNT(*), '/', IFNULL(MD5(GROUP_CONCAT(CONCAT_WS('|', idhorarios, clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin$propias) " +
        "ORDER BY idhorarios SEPARATOR ';')), '')) FROM horarios")
}
$tablasR1 = @('clases', 'horarios', 'horario_excepciones', 'codigos_accesos', 'registros_bitacoras', 'asistencias_dadas') | Where-Object { Existe $_ }
$tablasResto = @('usuarios', 'laboratorios', 'configuracion', 'grupos', 'materias', 'historial_cambios') | Where-Object { Existe $_ }
function Sumas([string[]]$tablas) { ((Sql ('CHECKSUM TABLE ' + ($tablas -join ', '))).Rows | ForEach-Object { "$($_[0] -replace '^.*\.', '')=$($_[1])" }) -join '; ' }

# Lo que la prueba crea fuera de las tablas del ciclo, para quitarlo pase lo que pase.
$script:clasesDePrueba = New-Object System.Collections.Generic.List[int]
$script:labsDePrueba = New-Object System.Collections.Generic.List[int]
$script:fechasPorDevolver = @{}

function Limpiar() {
    # Una aplicada de la prueba que siga por empezar: se deshace antes de borrarla.
    foreach ($fila in (Sql "SELECT idciclo FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA P4%' AND estado = 'publicado' AND desde > CURDATE()").Rows) {
        $null = Sql "DELETE FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA P4%' AND estado = 'borrador'"
        $null = Sql "DELETE FROM codigos_accesos WHERE codigo = 'PRBP4'"
        $r = Llamar 'Deshacer' @([int]$fila[0], $script:idAdmin)
        if (-not $r.Ok) { Write-Host "No se pudo deshacer la aplicada de la prueba $($fila[0]): $($r.Error.Message)" -ForegroundColor Yellow }
    }
    if ($conDadas) { $null = Sql "DELETE ad FROM asistencias_dadas ad INNER JOIN codigos_accesos s ON s.idcodigos_accesos = ad.fk_codigo_accesos WHERE s.codigo = 'PRBP4'" }
    $null = Sql "DELETE FROM codigos_accesos WHERE codigo = 'PRBP4'"
    foreach ($id in $script:fechasPorDevolver.Keys) { $null = Sql "UPDATE clases SET vigente_hasta = '$($script:fechasPorDevolver[$id])' WHERE idclases = $id" }
    $script:fechasPorDevolver = @{}
    if ($script:clasesDePrueba.Count -gt 0) { $null = Sql "DELETE FROM clases WHERE idclases IN ($($script:clasesDePrueba -join ','))"; $script:clasesDePrueba.Clear() }
    if ($script:labsDePrueba.Count -gt 0) { $null = Sql "DELETE FROM laboratorios WHERE idlaboratorios IN ($($script:labsDePrueba -join ','))"; $script:labsDePrueba.Clear() }
    if ($conHistorial -and $script:ciclosDePrueba.Count -gt 0) {
        $null = Sql "DELETE FROM historial_cambios WHERE idhistorial_cambios > $($script:maxHistorial) AND tipo = 'horario' AND registro_id IN ($($script:ciclosDePrueba -join ','))"
    }
    $null = Sql "DELETE FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA P4%'"
    # Quien recibio su codigo en la prueba vuelve a como estaba.
    if ($script:sinCodigo.Count -gt 0) {
        foreach ($fila in (Sql "SELECT idusuarios FROM usuarios WHERE IFNULL(codigo_clase, '') <> '' AND idusuarios IN ($(@($script:sinCodigo.Keys) -join ','))").Rows) {
            $valor = $script:sinCodigo[[int]$fila[0]]
            $null = Sql "UPDATE usuarios SET codigo_clase = $(if ($valor -eq $null) { 'NULL' } else { "'$valor'" }) WHERE idusuarios = $($fila[0])"
        }
    }
}

# --- Antes de empezar ----------------------------------------------------------
if (-not (Existe 'ciclo_cargas' 'carga_origen')) { throw 'La base no tiene db/23_horarios_guardados.sql (ni quiza la 16): aplicalas antes.' }
if (-not (Existe 'usuarios' 'codigo_clase')) { throw 'La base no tiene db/09_codigo_por_profesor.sql: sin ella no se aplica.' }
$ajenos = [int](Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE (estado = 'borrador' OR (estado = 'publicado' AND desde > CURDATE())) AND nombre NOT LIKE 'PRUEBA P4%'")
if ($ajenos -gt 0) { throw 'La base ya tiene un horario en preparacion (o una aplicada por empezar) que no es de esta prueba: no se hizo nada.' }

$script:idAdmin = [int]("$(Uno "SELECT idusuarios FROM usuarios WHERE numero_identificador = '$Administrador'")" -replace '^$', '0')
if ($script:idAdmin -eq 0) { throw "No hay una cuenta $Administrador en esta base: pasa otra con -Administrador." }
$tUsuario.GetMethod('SetUser').Invoke($null, [object[]]@($Administrador)) | Out-Null
$tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 3)
$script:maxHistorial = $(if ($conHistorial) { [int]("$(Uno 'SELECT IFNULL(MAX(idhistorial_cambios), 0) FROM historial_cambios')") } else { 0 })
$script:ciclosDePrueba = New-Object System.Collections.Generic.List[int]
$script:sinCodigo = @{}
foreach ($fila in (Sql "SELECT idusuarios, codigo_clase FROM usuarios WHERE codigo_clase IS NULL OR codigo_clase = ''").Rows) {
    $script:sinCodigo[[int]$fila[0]] = $(if ($fila[1] -is [DBNull]) { $null } else { [string]$fila[1] })
}
Limpiar
$hoy = [DateTime](Uno 'SELECT CURDATE()')
$Fin = $Desde.AddDays(-1)
$sumasR1Inicio = Sumas $tablasR1
$sumasRestoInicio = Sumas $tablasResto
Write-Host "Hoy segun el servidor: $(F $hoy); se aplica del $(F $Desde) al $(F $Hasta) con el de ahora hasta el $(F $Fin); administracion: $Administrador ($($script:idAdmin))"
if ($Desde -le $hoy) { throw 'El primer dia del horario de la prueba tiene que ser futuro.' }

try {
    Write-Host "`n== Preparar una copia del horario de hoy con algun cambio ==" -ForegroundColor Cyan
    $r = Llamar 'Preparar' @('PRUEBA P4 horario', (Invocar $tFuente.GetMethod('DelDia') $null @($hoy, $null, $true)), $false, $script:idAdmin)
    if (-not (Bien 'Preparar desde hoy (todos los laboratorios y las sin horario)' $r)) { throw 'Sin el horario en preparacion no se puede seguir.' }
    $b = $r.Valor.CicloId
    $script:ciclosDePrueba.Add($b)
    $quitadas = 0
    for ($i = 0; $i -lt 200; $i++) {
        $rev = Revisar (Leer $b)
        if ($rev.Choques.Count -eq 0) { break }
        $choque = $rev.Choques[0]
        $carga = $(if ($choque.CargaB -ne $null) { $choque.CargaB } else { $choque.CargaA })
        $null = Llamar 'QuitarClases' @((Lista @($carga.Id)), $script:idAdmin)
        $quitadas++
    }
    Ver "copia de $($r.Valor.Clases) clases; se quitaron $quitadas que chocaban" ((Revisar (Leer $b)).Choques.Count -eq 0)

    # La clase nueva: el dia de hoy, de una persona docente que hoy no tiene horas (asi su codigo hoy no abre nada).
    $dow = [int](Uno 'SELECT WEEKDAY(CURDATE()) + 1')
    $ocupados = Sql "SELECT DISTINCT IFNULL(cc.profesor, 0) AS p, cc.grupo AS g, f.laboratorio AS l FROM ciclo_cargas cc INNER JOIN ciclo_franjas f ON f.idcarga = cc.idcarga WHERE cc.idciclo = $b AND f.dia_semana = $dow"
    $profOcupados = @($ocupados.Rows | ForEach-Object { [int]$_['p'] }) + @(0)
    $grupOcupados = @($ocupados.Rows | ForEach-Object { [int]$_['g'] }) + @(0)
    $labOcupados = @($ocupados.Rows | ForEach-Object { [int]$_['l'] }) + @(0)
    $activo = $(if ($conBajas) { 'u.activo = 1' } else { '1 = 1' })
    $doc = Sql ("SELECT u.idusuarios, IFNULL(u.codigo_clase, '') AS codigo FROM usuarios u WHERE u.fk_tipo_usuario = 2 AND $activo AND u.idusuarios NOT IN ($($profOcupados -join ',')) " +
        "AND NOT EXISTS (SELECT 1 FROM clases c INNER JOIN horarios h ON h.clases_idclases = c.idclases WHERE c.usuarios_idusuarios = u.idusuarios AND c.activa = 1 " +
        "AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta AND h.dia_semana = $dow) " +
        "AND NOT EXISTS (SELECT 1 FROM horario_excepciones x INNER JOIN clases c ON c.idclases = x.clases_idclases WHERE x.tipo = 'extra' AND x.fecha = CURDATE() AND c.usuarios_idusuarios = u.idusuarios) " +
        "AND NOT EXISTS (SELECT 1 FROM clases c WHERE c.usuarios_idusuarios = u.idusuarios AND c.vigente_desde > CURDATE()) " +
        "ORDER BY (IFNULL(u.codigo_clase, '') = '') DESC, u.idusuarios LIMIT 1")
    if ($doc.Rows.Count -eq 0) { throw 'No hay una persona docente activa sin horas hoy para la clase nueva.' }
    $docente = [int]$doc.Rows[0][0]
    $sinCodigoAntes = "$($doc.Rows[0][1])" -eq ''
    $grupo = [int](Uno "SELECT idgrupos FROM grupos WHERE idgrupos NOT IN ($($grupOcupados -join ',')) ORDER BY idgrupos LIMIT 1")
    $materias = @((Sql ("SELECT m.id_materia FROM materias m WHERE NOT EXISTS (SELECT 1 FROM clases c WHERE c.grupos_idgrupos = $grupo AND c.materias_id_materia = m.id_materia) " +
        "AND NOT EXISTS (SELECT 1 FROM ciclo_cargas cc WHERE cc.idciclo = $b AND cc.grupo = $grupo AND cc.materia = m.id_materia) ORDER BY m.id_materia LIMIT 4")).Rows | ForEach-Object { [int]$_[0] })
    $labCond = @()
    if ($conLabActivo) { $labCond += 'activo = 1' }
    if ($conModo) { $labCond += "modo_codigo = 'fijo'" }
    $labCond += "idlaboratorios NOT IN ($($labOcupados -join ','))"
    $labNueva = [int](Uno "SELECT idlaboratorios FROM laboratorios WHERE $($labCond -join ' AND ') ORDER BY idlaboratorios DESC LIMIT 1")
    $idAlumno = [int](Uno 'SELECT idusuarios FROM usuarios WHERE fk_tipo_usuario = 1 ORDER BY idusuarios LIMIT 1')
    $r = Llamar 'AgregarClase' @($b, $docente, $grupo, $materias[0], (Franjas @(@($labNueva, $dow, '00:00', '23:59'))), $script:idAdmin)
    if (-not (Bien "la clase nueva: docente $docente$(if ($sinCodigoAntes) { ' (sin codigo)' }), grupo $grupo, materia $($materias[0]), laboratorio $labNueva, dia $dow de 00:00 a 23:59" $r)) { throw 'No se pudo agregar la clase nueva.' }
    $cargaNueva = [int]$r.Valor
    $leido = Leer $b
    $rev = Revisar $leido
    Ver "el horario queda sin choques, sin docente ni repetidas ($($leido.Cargas.Count) clases)" ($rev.Choques.Count -eq 0 -and $rev.SinProfesor -eq 0 -and $rev.Repetidas.Count -eq 0)

    # Una clase futura que empieza antes del ultimo dia del de ahora: tambien se recorta.
    # Si la base no tiene una, la prueba pone una (y la quita al terminar).
    if ([int](Uno "SELECT COUNT(*) FROM clases WHERE vigente_desde > CURDATE() AND vigente_desde <= '$(F $Fin)' AND vigente_hasta > '$(F $Fin)'") -eq 0) {
        # De una persona docente que ya tiene codigo (Aplicar da el suyo a quien no tiene y lo pone en sus clases).
        $otraDocente = Sql ("SELECT u.idusuarios, u.codigo_clase, m.id_materia FROM usuarios u INNER JOIN materias m " +
            "WHERE u.fk_tipo_usuario = 2 AND IFNULL(u.codigo_clase, '') <> '' AND u.idusuarios <> $docente " +
            "AND NOT EXISTS (SELECT 1 FROM clases c WHERE c.usuarios_idusuarios = u.idusuarios AND c.grupos_idgrupos = $grupo AND c.materias_id_materia = m.id_materia) " +
            "AND NOT EXISTS (SELECT 1 FROM ciclo_cargas cc WHERE cc.idciclo = $b AND cc.profesor = u.idusuarios AND cc.grupo = $grupo AND cc.materia = m.id_materia) " +
            "ORDER BY u.idusuarios, m.id_materia LIMIT 1")
        $futuraAntes = Insertar ("INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en) " +
            "VALUES ($($otraDocente.Rows[0][0]), $grupo, $($otraDocente.Rows[0][2]), '$($otraDocente.Rows[0][1])', '$(F $hoy.AddDays(10))', '$(F $Fin.AddDays(30))', 1, NULL, NOW())")
        $script:clasesDePrueba.Add($futuraAntes)
        Write-Host "   (la base no tenia una clase futura que empezara antes del $(Dmy $Fin): se puso la $futuraAntes)" -ForegroundColor DarkGray
    }

    Write-Host "`n== Antes de aplicar ==" -ForegroundColor Cyan
    $d = (Llamar 'LeerParaAplicar' @($b, $Desde, $Hasta, $Fin)).Valor
    $renglones = Renglones $d $Desde $Hasta $Fin 'PRUEBA P4 aplicado'
    foreach ($x in $renglones) { Write-Host ("   {0,-6} {1}" -f $x.Nivel, $x.Texto) -ForegroundColor DarkGray }
    Ver 'con las fechas en orden no hay nada en rojo' ((Rojos $renglones).Count -eq 0) (((Rojos $renglones) | ForEach-Object { $_.Texto }) -join ' / ')
    $esperadas = [int](Uno "SELECT COUNT(*) FROM clases c WHERE c.vigente_desde <= '$(F $Fin)' AND c.vigente_hasta > '$(F $Fin)'")
    Ver "terminan $($d.Terminan) clases (las que valen el $(Dmy $Fin) y siguen despues)" ($d.Terminan -eq $esperadas) "en la base: $esperadas"
    Ver 'sin sesiones despues de ese dia' ($d.SesionPosterior -eq $null)
    Ver "la casilla de Ajustes sale con su estado (encendida: $($d.ConFecha))" (@($renglones | Where-Object { $_.Texto -match 'Los cambios de horario aplican desde una fecha' -and $_.Texto -match $(if ($d.ConFecha) { 'encendida' } else { 'apagada .*Ajustes' }) }).Count -eq 1)
    Ver "propone guardar el de ahora como '$($d.PropuestaGuardar)'" ("$($d.PropuestaGuardar)".Length -gt 0)
    if ($sinCodigoAntes) { Ver "dice quien recibe codigo: $($d.DocentesSinCodigo -join ', ')" ($d.DocentesSinCodigo.Count -ge 1) }

    Write-Host "`n== Lo que no deja aplicar (Aplicar por reflexion, revisado dentro de su transaccion) ==" -ForegroundColor Cyan
    # Lo esperado sale en rojo en "Antes de aplicar" y Aplicar (por reflexion) se niega
    # con el primer texto rojo, o con el de su propia regla (el nombre), sin cambiar nada.
    function NoAplica([string]$que, [DateTime]$de, [DateTime]$a, [DateTime]$fin, [string]$patron, [string]$nombre = 'PRUEBA P4 aplicado') {
        $antes = Sumas $tablasR1
        $dX = (Llamar 'LeerParaAplicar' @($b, $de, $a, $fin)).Valor
        $rojos = Rojos (Renglones $dX $de $a $fin $nombre)
        $r = Llamar 'Aplicar' @($b, $nombre, $de, $a, $fin, 'PRUEBA P4 no se guarda', $script:idAdmin)
        if ($r.Ok) {
            Ver $que $false 'SE APLICO'
            $null = Llamar 'Deshacer' @($b, $script:idAdmin)
            return
        }
        $enRojo = @($rojos | Where-Object { $_.Texto -match $patron }).Count -ge 1
        $mensaje = $r.Error.Message
        $esSuyo = $r.Error.GetType().Name -eq 'ErrorDeDatos' -and (($rojos.Count -gt 0 -and $mensaje -eq $rojos[0].Texto) -or $mensaje -match $patron)
        Ver $que ($enRojo -and $esSuyo) ("en rojo: " + (($rojos | ForEach-Object { $_.Texto }) -join ' / ') + "; Aplicar: " + $r.Error.GetType().Name + ': ' + $mensaje)
        $fila = (Sql "SELECT estado, (SELECT COUNT(*) FROM ciclo_recortes WHERE idciclo = $b) AS recortes, (SELECT COUNT(*) FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA P4 no se guarda%') AS g FROM ciclos_preliminares WHERE idciclo = $b").Rows[0]
        Ver '... y no cambio nada' ((Sumas $tablasR1) -eq $antes -and $fila['estado'] -eq 'borrador' -and [int]$fila['recortes'] -eq 0 -and [int]$fila['g'] -eq 0)
    }
    NoAplica 'el primer dia es hoy' $hoy $Hasta $hoy.AddDays(-1) 'tiene que ser despu.s de hoy'
    NoAplica 'el ultimo dia antes del primero' $Desde $Desde.AddDays(-1) $Fin 'no puede ser antes del primero'
    NoAplica 'el ultimo dia del de ahora no es antes del primero del nuevo' $Desde $Hasta $Desde 'tiene que ser antes del primer d.a del nuevo'

    # La ultima sesion ya registrada de una clase que valia la vispera.
    $diaSesion = Uno ("SELECT MAX(t.dia) FROM (" +
        "SELECT COALESCE(s.fecha, DATE(s.hora_registro)) AS dia, c.vigente_desde AS vd, c.vigente_hasta AS vh FROM clases c INNER JOIN horarios hh ON hh.clases_idclases = c.idclases INNER JOIN codigos_accesos s ON s.horarios_idhorarios = hh.idhorarios " +
        "UNION ALL SELECT COALESCE(s.fecha, DATE(s.hora_registro)), c.vigente_desde, c.vigente_hasta FROM clases c INNER JOIN horario_excepciones xx ON xx.clases_idclases = c.idclases INNER JOIN codigos_accesos s ON s.horario_excepciones_id = xx.idhorario_excepciones " +
        "UNION ALL SELECT COALESCE(s.fecha, DATE(s.hora_registro)), c.vigente_desde, c.vigente_hasta FROM clases c INNER JOIN codigos_accesos s ON s.horarios_idhorarios IS NULL AND s.horario_excepciones_id IS NULL " +
        "AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia AND s.grupos_idgrupos = c.grupos_idgrupos " +
        "AND COALESCE(s.fecha, DATE(s.hora_registro)) BETWEEN c.vigente_desde AND c.vigente_hasta) t WHERE t.dia > t.vd AND t.dia <= t.vh AND t.dia <= CURDATE()")
    if ($diaSesion -ne $null) {
        $diaSesion = ([DateTime]$diaSesion).Date
        $finSesion = $diaSesion.AddDays(-1)
        $dS = (Llamar 'LeerParaAplicar' @($b, $Desde, $Hasta, $finSesion)).Valor
        Ver "con el de ahora hasta el $(Dmy $finSesion), ve la sesion del $(Dmy $diaSesion) ($($dS.SesionPosterior.Clase))" ($dS.SesionPosterior -ne $null -and $dS.SesionPosterior.Fecha.Date -eq $diaSesion)
        $rS = Renglones $dS $Desde $Hasta $finSesion 'PRUEBA P4 aplicado'
        if ($finSesion -lt $hoy) { Ver '... y avisa que esa fecha ya paso' (@($rS | Where-Object { "$($_.Nivel)" -eq 'Ambar' -and $_.Texto -match 'Esa fecha ya pas' }).Count -eq 1) }
        NoAplica "el ultimo dia del de ahora antes de la sesion del $(Dmy $diaSesion)" $Desde $Hasta $finSesion ('no puede ser antes del ' + [regex]::Escape((Dmy $diaSesion)) + ': ese d.a (hubo una sesi.n|se dio asistencia)')
    } else { Ver 'hay una sesion registrada para probar la regla de las sesiones' $false 'la base no tiene ninguna' }

    $r = Llamar 'AgregarClase' @($b, $null, $grupo, $materias[1], (Franjas @()), $script:idAdmin)
    if (Bien 'una clase sin docente' $r) {
        NoAplica '... no deja aplicar' $Desde $Hasta $Fin 'sin docente'
        $null = Llamar 'QuitarClases' @((Lista @([int]$r.Valor)), $script:idAdmin)
    }

    $deBaja = $(if ($conBajas) { Uno 'SELECT idusuarios FROM usuarios WHERE activo = 0 ORDER BY idusuarios LIMIT 1' } else { $null })
    if ($deBaja -ne $null) {
        $r = Llamar 'AgregarClase' @($b, [int]$deBaja, $grupo, $materias[1], (Franjas @()), $script:idAdmin)
        if (Bien "una clase de una cuenta de baja ($deBaja)" $r) {
            NoAplica '... no deja aplicar' $Desde $Hasta $Fin 'de baja'
            $null = Llamar 'QuitarClases' @((Lista @([int]$r.Valor)), $script:idAdmin)
        }
    } else { Write-Host '   (sin cuentas de baja en esta base: se salta la de una docente de baja)' -ForegroundColor Yellow }

    if ($conLabActivo) {
        $apagado = Insertar "INSERT INTO laboratorios (nombre_laboratorio, activo) VALUES ('PRUEBA P4 apagado', 0)"
        $script:labsDePrueba.Add($apagado)
        $r = Llamar 'AgregarClase' @($b, $docente, $grupo, $materias[1], (Franjas @(@($apagado, (($dow % 6) + 1), '07:00', '08:00'))), $script:idAdmin)
        if (Bien 'una clase con horas en un laboratorio desactivado' $r) {
            NoAplica '... no deja aplicar' $Desde $Hasta $Fin 'laboratorio desactivado'
            $null = Llamar 'QuitarClases' @((Lista @([int]$r.Valor)), $script:idAdmin)
        }
        $null = Sql "DELETE FROM laboratorios WHERE idlaboratorios = $apagado"
        $script:labsDePrueba.Remove($apagado) | Out-Null
    }

    $repetida = Insertar "INSERT INTO ciclo_cargas (idciclo, profesor, grupo, materia, quitada) SELECT idciclo, profesor, grupo, materia, 0 FROM ciclo_cargas WHERE idcarga = $cargaNueva"
    NoAplica 'una clase repetida (puesta a mano en la configuracion)' $Desde $Hasta $Fin 'Repetida: .* tiene dos veces'
    $null = Sql "DELETE FROM ciclo_cargas WHERE idcarga = $repetida"

    # Una clase que ya esta en Clases en esas fechas: la misma terna que una de la
    # configuracion (R6) y con una hora que choca con otra de la configuracion.
    $conHoras = @($leido.Cargas | Where-Object { $_.Franjas.Count -gt 0 -and $_.Id -ne $cargaNueva -and $_.ProfesorId -ne $null })
    if ($conHoras.Count -ge 2) {
        $x = $conHoras[0]
        $y = @($conHoras | Where-Object { $_.ProfesorId -ne $x.ProfesorId -and $_.GrupoId -ne $x.GrupoId })[0]
        if ($y -eq $null) { $y = $conHoras[1] }
        $fy = $y.Franjas[0]
        $codigoX = "$(Uno "SELECT codigo_clase FROM usuarios WHERE idusuarios = $($x.ProfesorId)")"
        $futura = Insertar ("INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en) " +
            "VALUES ($($x.ProfesorId), $($x.GrupoId), $($x.MateriaId), '$codigoX', '$(F $Desde.AddDays(14))', '$(F $Hasta.AddDays(-14))', 1, NULL, NOW())")
        $script:clasesDePrueba.Add($futura)
        $null = Sql ("INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES " +
            "($futura, $($fy.LaboratorioId), $($fy.DiaSemana), '$($fy.Inicio)', '$($fy.Fin)')")
        $dF = (Llamar 'LeerParaAplicar' @($b, $Desde, $Hasta, $Fin)).Valor
        $rF = Renglones $dF $Desde $Hasta $Fin 'PRUEBA P4 aplicado'
        Ver "la clase futura de Clases ($futura) sale como ya programada y como terna cruzada" ($dF.Leido.YaProgramadas.Count -ge 1 -and @($dF.TernasCruzadas | Where-Object { $_.ClaseId -eq $futura }).Count -eq 1)
        Ver '... en rojo: el choque con ella y la terna, y en gris que ya esta en Clases' (@($rF | Where-Object { "$($_.Nivel)" -eq 'Rojo' -and $_.Texto -match 'choque' -and $_.Texto -match 'ya est.n? en Clases' }).Count -eq 1 -and
            @($rF | Where-Object { "$($_.Nivel)" -eq 'Rojo' -and $_.Texto -match 'ya est. en Clases del' }).Count -eq 1 -and
            @($rF | Where-Object { "$($_.Nivel)" -eq 'Gris' -and $_.Texto -match 'ya est.n? en Clases tambi.n val' }).Count -eq 1)
        NoAplica '... el choque con una clase ya programada no deja aplicar' $Desde $Hasta $Fin 'choque'
        $null = Sql "DELETE FROM horarios WHERE clases_idclases = $futura"
        NoAplica '... ni la misma clase en esas fechas (R6, ExigirSinCruceDeTerna)' $Desde $Hasta $Fin 'ya est. en Clases del .*termina antes la otra'
        $null = Sql "DELETE FROM clases WHERE idclases = $futura"
        $script:clasesDePrueba.Remove($futura) | Out-Null
    } else { Ver 'hay dos clases con horas para la prueba de las ya programadas' $false }

    $null = Sql "INSERT INTO ciclos_preliminares (nombre, desde, hasta, estado, creado_en, cambiado_en, fin_anterior, publicado_en) VALUES ('PRUEBA P4 viejo', '2020-01-06', '2020-06-30', 'publicado', NOW(), NOW(), '2020-01-03', NOW())"
    $script:ciclosDePrueba.Add([int](Uno "SELECT idciclo FROM ciclos_preliminares WHERE nombre = 'PRUEBA P4 viejo'"))
    NoAplica 'con el nombre de otra aplicada (otras mayusculas)' $Desde $Hasta $Fin 'Ya hay una plantilla aplicada que se llama' 'prueba p4 VIEJO'

    Write-Host "`n== Aplicar ==" -ForegroundColor Cyan
    $recortables = Sql "SELECT idclases, vigente_desde, vigente_hasta, activa FROM clases WHERE vigente_desde <= '$(F $Fin)' AND vigente_hasta > '$(F $Fin)' ORDER BY idclases"
    $idsRecortables = @($recortables.Rows | ForEach-Object { [int]$_['idclases'] })
    $futuraQueEmpiezaAntes = @($recortables.Rows | Where-Object { ([DateTime]$_['vigente_desde']).Date -gt $hoy }).Count
    $maxClase = [int](Uno 'SELECT IFNULL(MAX(idclases), 0) FROM clases')
    $otras = $(if ($idsRecortables.Count -gt 0) { "idclases <= $maxClase AND idclases NOT IN ($($idsRecortables -join ','))" } else { "idclases <= $maxClase" })
    $contenidoClases = ContenidoClases
    $contenidoHorarios = ContenidoHorarios
    $contenidoOtras = ContenidoClases $otras
    $sumasAntes = Sumas $tablasR1
    $activasAntes = "$(Uno "SELECT GROUP_CONCAT(CONCAT(idclases, ':', activa) ORDER BY idclases) FROM clases WHERE idclases <= $maxClase")"
    $cargas = [int](Uno "SELECT COUNT(*) FROM ciclo_cargas WHERE idciclo = $b AND quitada = 0")
    $franjasCiclo = [int](Uno "SELECT COUNT(*) FROM ciclo_franjas f INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga WHERE cc.idciclo = $b AND cc.quitada = 0")
    $r = Llamar 'Aplicar' @($b, 'PRUEBA P4 aplicado', $Desde, $Hasta, $Fin, 'PRUEBA P4 como quedo', $script:idAdmin)
    if (-not (Bien 'Aplicar con el ultimo dia del de ahora en la vispera' $r)) { throw 'No se aplico: no se puede seguir.' }
    $res = $r.Valor
    Ver "crea $($res.Clases) clases con $($res.Horarios) horarios (las de la configuracion)" ($res.Clases -eq $cargas -and $res.Horarios -eq $franjasCiclo)
    Ver "recorta $($res.Terminan) clases: las que valian el $(Dmy $Fin) y seguian" ($res.Terminan -eq $idsRecortables.Count -and $res.Terminan -gt 0)
    if ($idsRecortables.Count -gt 0) {
        Ver '... todas terminan ese dia' ([int](Uno "SELECT COUNT(*) FROM clases WHERE idclases IN ($($idsRecortables -join ',')) AND vigente_hasta = '$(F $Fin)'") -eq $idsRecortables.Count)
    }
    Ver "... tambien una futura que empieza antes ($futuraQueEmpiezaAntes)" ($futuraQueEmpiezaAntes -ge 1) $(if ($futuraQueEmpiezaAntes -eq 0) { 'esta base no tiene una clase futura que empiece antes de ese dia' } else { '' })
    $activasDespues = "$(Uno "SELECT GROUP_CONCAT(CONCAT(idclases, ':', activa) ORDER BY idclases) FROM clases WHERE idclases <= $maxClase")"
    $contenidoOtrasDespues = ContenidoClases $otras
    Ver '... nunca se toca activa ni las demas clases' ($activasDespues -eq $activasAntes -and $contenidoOtrasDespues -eq $contenidoOtras) "activa: $activasAntes / $activasDespues; las demas: $contenidoOtras / $contenidoOtrasDespues"
    $nuevas = [int](Uno ("SELECT COUNT(*) FROM clases c INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios WHERE c.idclases > $maxClase AND c.vigente_desde = '$(F $Desde)' " +
        "AND c.vigente_hasta = '$(F $Hasta)' AND c.activa = 1 AND c.codigo = u.codigo_clase AND c.creada_por = $($script:idAdmin)"))
    Ver "las $nuevas nuevas llevan su vigencia y el codigo de su docente" ($nuevas -eq $res.Clases -and [int](Uno "SELECT COUNT(*) FROM clases WHERE idclases > $maxClase") -eq $res.Clases)
    $sinFecha = $(if ($conVigencia) { ' AND vigente_desde IS NULL AND vigente_hasta IS NULL' } else { '' })
    Ver "sus franjas, sin fecha propia$(if (-not $conVigencia) { ' (base sin db/20)' })" ([int](Uno "SELECT COUNT(*) FROM horarios WHERE clases_idclases > $maxClase$sinFecha") -eq $res.Horarios)
    $fila = (Sql "SELECT estado, desde, hasta, fin_anterior, nombre, (SELECT COUNT(*) FROM ciclo_cargas WHERE idciclo = $b AND quitada = 0 AND clase_publicada IS NULL) AS sueltas, (SELECT COUNT(*) FROM ciclo_recortes WHERE idciclo = $b) AS recortes FROM ciclos_preliminares WHERE idciclo = $b").Rows[0]
    Ver 'la configuracion queda aplicada, con su vigencia, el fin de las de antes, cada clase enlazada y sus recortes' ($fila['estado'] -eq 'publicado' -and ([DateTime]$fila['desde']).Date -eq $Desde -and
        ([DateTime]$fila['hasta']).Date -eq $Hasta -and ([DateTime]$fila['fin_anterior']).Date -eq $Fin -and $fila['nombre'] -eq 'PRUEBA P4 aplicado' -and [int]$fila['sueltas'] -eq 0 -and [int]$fila['recortes'] -eq $res.Terminan)
    $idClaseNueva = [int](Uno "SELECT clase_publicada FROM ciclo_cargas WHERE idcarga = $cargaNueva")
    $codigoNuevo = "$(Uno "SELECT codigo FROM clases WHERE idclases = $idClaseNueva")"
    if ($sinCodigoAntes) {
        Ver "la docente sin codigo lo recibio ($codigoNuevo) y su clase nueva lo lleva" ($res.RecibieronCodigo.Count -ge 1 -and $codigoNuevo.Length -gt 0 -and "$(Uno "SELECT codigo_clase FROM usuarios WHERE idusuarios = $docente")" -eq $codigoNuevo)
    }
    $guardada = $(if ($res.GuardadaId -ne $null) { (Sql "SELECT estado, copiado_del, (SELECT COUNT(*) FROM ciclo_cargas WHERE idciclo = $($res.GuardadaId)) AS n FROM ciclos_preliminares WHERE idciclo = $($res.GuardadaId)").Rows[0] } else { $null })
    if ($res.GuardadaId -ne $null) { $script:ciclosDePrueba.Add([int]$res.GuardadaId) }
    Ver "se guardo el de ahora como quedo: '$($res.GuardadaNombre)'" ($guardada -ne $null -and $guardada['estado'] -eq 'guardado' -and ([DateTime]$guardada['copiado_del']).Date -eq $Fin -and [int]$guardada['n'] -eq $d.ValenElFin) "$(if ($guardada) { "$($guardada['n']) clases; ese dia valen $($d.ValenElFin)" })"
    if ($conHistorial) {
        $renglonesHist = Sql "SELECT idhistorial_cambios, tipo, accion, descripcion FROM historial_cambios WHERE idhistorial_cambios > $($script:maxHistorial) AND (registro_id = $b OR usuario = '$Administrador')"
        $deAplicar = @($renglonesHist.Rows | Where-Object { $_['tipo'] -eq 'horario' -and $_['accion'] -eq 'aplicar' })
        $otrosHist = @($renglonesHist.Rows | Where-Object { $_['tipo'] -ne 'horario' })
        Ver "un solo renglon en el historial: '$(if ($deAplicar.Count) { $deAplicar[0]['descripcion'] })'" ($deAplicar.Count -eq 1 -and ($otrosHist.Count -eq 0 -or $res.RecibieronCodigo.Count -gt 0)) "$($otrosHist.Count) de otro tipo"
        $conexion = $tConexion.GetMethod('GetConexion').Invoke([Activator]::CreateInstance($tConexion, $true), [object[]]@())
        try {
            $paraOtra = @($tHistorial.GetMethod('DeHorario').Invoke($null, [object[]]@($conexion, $hoy, 'OTRA_CUENTA_P4')))
            $paraMi = @($tHistorial.GetMethod('DeHorario').Invoke($null, [object[]]@($conexion, $hoy, $Administrador)))
        } finally { $conexion.Dispose() }
        $alerta = @($paraOtra | Where-Object { $_.Id -eq [int]$deAplicar[0]['idhistorial_cambios'] })
        Ver "... y otra cuenta lo recibe como alerta de cambio de horario ('$(if ($alerta.Count) { $alerta[0].Titulo })'); quien lo hizo, no" ($alerta.Count -eq 1 -and $alerta[0].Titulo -eq 'Horario aplicado' -and -not ($paraMi | Where-Object { $_.Id -eq $alerta[0].Id }))
    }
    $s = (Llamar 'LeerSituacion' @()).Valor
    Ver 'LeerSituacion la da como aplicada por empezar' ($s.PorEmpezar -ne $null -and $s.PorEmpezar.Id -eq $b -and $s.Borrador -eq $null)
    $info = (Llamar 'LeerAplicada' @($b)).Valor
    Ver "LeerAplicada: $($info.Creadas) creadas, $($info.Recortadas) recortadas, se puede deshacer" ($info.Creadas -eq $res.Clases -and $info.Recortadas -eq $res.Terminan -and $info.Motivo -eq $null) "$($info.Motivo)"
    Falla 'mientras, no se prepara otro' (Llamar 'Preparar' @('PRUEBA P4 otro', (Invocar $tFuente.GetMethod('EnBlanco') $null @()), $true, $script:idAdmin)) 'ErrorDeDatos' 'ya est. aplicado y todav.a no empieza'

    Write-Host "`n== Antes del primer dia nadie entra con las clases nuevas ==" -ForegroundColor Cyan
    $fechaNueva = $Desde
    while ([int]$fechaNueva.DayOfWeek -ne ($dow % 7)) { $fechaNueva = $fechaNueva.AddDays(1) }
    $agenda = @($horarios.GetType().GetMethod('AgendaDelDia', [Type[]]@([int], [DateTime])).Invoke($horarios, [object[]]@($labNueva, $hoy)))
    Ver "[este DigBit] hoy el laboratorio $labNueva no ensena la clase nueva" (-not ($agenda | Where-Object { $_.ClaseId -eq $idClaseNueva }))
    $agenda = @($horarios.GetType().GetMethod('AgendaDelDia', [Type[]]@([int], [DateTime])).Invoke($horarios, [object[]]@($labNueva, $fechaNueva)))
    Ver "[este DigBit] el $(Dmy $fechaNueva) si" (@($agenda | Where-Object { $_.ClaseId -eq $idClaseNueva }).Count -eq 1)
    $antesS = [int](Uno 'SELECT COUNT(*) FROM codigos_accesos')
    $rc = $horarios.GetType().GetMethod('ResolverCodigo').Invoke($horarios, [object[]]@($codigoNuevo, $labNueva, $idAlumno, $null))
    Ver "[este DigBit] hoy el codigo $codigoNuevo no deja entrar" (-not $rc.Aceptado -and [int](Uno 'SELECT COUNT(*) FROM codigos_accesos') -eq $antesS) "$($rc.Motivo): $($rc.Mensaje)"
    if ($ExeAnterior) {
        $salida = & powershell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -CadenaConexion $CadenaConexion -Exe $ExeAnterior -Anterior `
            -Laboratorio $labNueva -ClaseNueva $idClaseNueva -Codigo $codigoNuevo -DiaNuevo (F $fechaNueva) -Recortadas ($idsRecortables -join ',') -Alumno $idAlumno 2>&1
        $lineas = @($salida | ForEach-Object { "$_" } | Where-Object { $_ -match '^\[(OK |FALLO)\]' })
        foreach ($l in $lineas) { Ver ($l -replace '^\[(OK |FALLO)\] ', '') ($l.StartsWith('[OK ')) }
        if ($lineas.Count -eq 0) { Ver 'el DigBit anterior a la 20 corrio' $false (($salida | ForEach-Object { "$_" }) -join ' / ') }
        $null = Sql "DELETE FROM codigos_accesos WHERE horarios_idhorarios IN (SELECT idhorarios FROM horarios WHERE clases_idclases > $maxClase)"
    } else { Write-Host '   (sin -ExeAnterior: no se prueba un DigBit anterior a la 20)' -ForegroundColor Yellow }

    Write-Host "`n== Lo que no deja deshacer ==" -ForegroundColor Cyan
    $franjaNueva = [int](Uno "SELECT idhorarios FROM horarios WHERE clases_idclases = $idClaseNueva LIMIT 1")
    $null = Sql ("INSERT INTO codigos_accesos (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos, laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios) " +
        "VALUES ('PRBP4', NOW(), '$(F $fechaNueva)', '00:00:00', '23:59:00', $($materias[0]), $grupo, $labNueva, $docente, $franjaNueva)")
    Ver 'con una sesion en una clase nueva, PuedeDeshacer lo dice' ("$((Llamar 'PuedeDeshacer' @($b)).Valor)" -match 'Ya hay sesiones de sus clases')
    Falla '... y Deshacer no deja' (Llamar 'Deshacer' @($b, $script:idAdmin)) 'ErrorDeDatos' 'Ya hay sesiones'
    $null = Sql "DELETE FROM codigos_accesos WHERE codigo = 'PRBP4'"
    if ($conDadas) {
        $sesionSuelta = Insertar "INSERT INTO codigos_accesos (codigo, hora_registro, fecha, hora_entrada, hora_salida, laboratorios_idlaboratorios) VALUES ('PRBP4', NOW(), '2000-01-03', '00:00:00', '00:30:00', $labNueva)"
        $null = Sql ("INSERT INTO asistencias_dadas (fk_clase, fecha, fk_alumno, fk_codigo_accesos, fk_profesor, dada_en, motivo, activa) " +
            "VALUES ($idClaseNueva, '2000-01-03', $idAlumno, $sesionSuelta, $docente, NOW(), 'otro', 1)")
        Ver 'con una asistencia dada en una clase nueva, tampoco' ("$((Llamar 'PuedeDeshacer' @($b)).Valor)" -match 'Ya hay sesiones')
        $null = Sql "DELETE FROM asistencias_dadas WHERE fk_codigo_accesos = $sesionSuelta"
        $null = Sql "DELETE FROM codigos_accesos WHERE idcodigos_accesos = $sesionSuelta"
    }
    $null = Sql "UPDATE ciclos_preliminares SET desde = CURDATE() WHERE idciclo = $b"
    Ver 'si ya empezo, no' ("$((Llamar 'PuedeDeshacer' @($b)).Valor)" -match 'Ya empez')
    $null = Sql "UPDATE ciclos_preliminares SET desde = '$(F $Desde)' WHERE idciclo = $b"
    $otro = Insertar "INSERT INTO ciclos_preliminares (nombre, estado, creado_en, cambiado_en) VALUES ('PRUEBA P4 otro', 'borrador', NOW(), NOW())"
    $script:ciclosDePrueba.Add($otro)
    $null = Sql "INSERT INTO ciclo_cargas (idciclo, profesor, grupo, materia, quitada) VALUES ($otro, $docente, $grupo, $($materias[0]), 0)"
    Falla 'con otro horario en preparacion, no' (Llamar 'Deshacer' @($b, $script:idAdmin)) 'ErrorDeDatos' 'Ya hay otro horario en preparaci'
    Falla '... y ese otro no se aplica mientras este no empiece' (Llamar 'Aplicar' @($otro, 'PRUEBA P4 otro', $Desde.AddDays(200), $Desde.AddDays(300), $Desde.AddDays(199), $null, $script:idAdmin)) 'ErrorDeDatos' 'ya est. aplicado y todav.a no empieza'
    $null = Sql "DELETE FROM ciclos_preliminares WHERE idciclo = $otro"
    if (-not $SinConsulta) {
        $conexionEquipo = $tConexion.GetMethod('GetConexion').Invoke([Activator]::CreateInstance($tConexion, $true), [object[]]@())
        $argsPreparar = [object[]]@($conexionEquipo, $null)
        $problema = (T 'DigBit.conexion.CuentaDeConsulta').GetMethod('Preparar', $bf).Invoke($null, $argsPreparar)
        $conexionEquipo.Dispose()
        if ($problema) { Write-Host "   Se salta la de solo consulta: $problema" -ForegroundColor Yellow }
        else {
            $tConexion.GetMethod('UsarCuentaDeSesion', $bf).Invoke($null, [object[]]@([string]$argsPreparar[1])) | Out-Null
            $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 4)
            try {
                $s = (Llamar 'LeerSituacion' @()).Valor
                Ver '[solo consulta] ve la aplicada por empezar' ($s.PorEmpezar -ne $null -and $s.PorEmpezar.Id -eq $b)
                $info = Llamar 'LeerAplicada' @($b)
                Ver '[solo consulta] lee lo que se recorto' ($info.Ok -and $info.Valor.Recortadas -eq $res.Terminan) $(if (-not $info.Ok) { $info.Error.Message })
                Falla '[solo consulta] Deshacer no se deja (Permisos.Exigir)' (Llamar 'Deshacer' @($b, $script:idAdmin)) 'AccionNoPermitida' 'Solo la administraci.n'
                Falla '[solo consulta] Aplicar no se deja' (Llamar 'Aplicar' @($b, 'x', $Desde, $Hasta, $Fin, $null, $script:idAdmin)) 'AccionNoPermitida' 'Solo la administraci.n'
                $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 3)
                FallaMySql '[solo consulta] aunque la pantalla se equivocara, la base no deja deshacer (1142)' (Llamar 'Deshacer' @($b, $script:idAdmin)) 1142
            }
            finally {
                $tConexion.GetMethod('UsarCuentaDelEquipo', $bf).Invoke($null, [object[]]@()) | Out-Null
                $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 3)
            }
            Ver '[solo consulta] ... y sigue aplicada' ("$(Uno "SELECT estado FROM ciclos_preliminares WHERE idciclo = $b")" -eq 'publicado')
        }
    }

    Write-Host "`n== Deshacer ==" -ForegroundColor Cyan
    $r = Llamar 'Deshacer' @($b, $script:idAdmin)
    if (Bien 'Deshacer' $r) {
        Ver "quita sus $($r.Valor.ClasesBorradas) clases y $($r.Valor.Devueltas) recuperan su fecha de fin" ($r.Valor.ClasesBorradas -eq $res.Clases -and $r.Valor.Devueltas -eq $res.Terminan -and $r.Valor.NoDevueltas -eq 0 -and $r.Valor.PorOtraIgual -eq 0)
    }
    $cc = ContenidoClases; $ch = ContenidoHorarios; $sm = Sumas $tablasR1
    Ver "clases con el mismo contenido que antes de aplicar ($cc)" ($cc -eq $contenidoClases) "antes $contenidoClases"
    Ver "horarios con el mismo contenido ($ch)" ($ch -eq $contenidoHorarios) "antes $contenidoHorarios"
    Ver "CHECKSUM igual que antes de aplicar: $sm" ($sm -eq $sumasAntes) "antes: $sumasAntes"
    $fila = (Sql "SELECT estado, desde, hasta, fin_anterior, publicado_en, (SELECT COUNT(*) FROM ciclo_recortes WHERE idciclo = $b) AS recortes, (SELECT COUNT(*) FROM ciclo_cargas WHERE idciclo = $b AND clase_publicada IS NOT NULL) AS enlazadas FROM ciclos_preliminares WHERE idciclo = $b").Rows[0]
    Ver 'vuelve a quedar en preparacion, con sus fechas como propuesta, sin recortes ni enlaces' ($fila['estado'] -eq 'borrador' -and ([DateTime]$fila['desde']).Date -eq $Desde -and ([DateTime]$fila['hasta']).Date -eq $Hasta -and
        $fila['fin_anterior'] -is [DBNull] -and $fila['publicado_en'] -is [DBNull] -and [int]$fila['recortes'] -eq 0 -and [int]$fila['enlazadas'] -eq 0)
    Ver 'la guardada "como quedo" se queda' ($res.GuardadaId -eq $null -or "$(Uno "SELECT estado FROM ciclos_preliminares WHERE idciclo = $($res.GuardadaId)")" -eq 'guardado')
    if ($conHistorial) {
        Ver 'un renglon "Aplicacion deshecha" en el historial' ([int](Uno "SELECT COUNT(*) FROM historial_cambios WHERE idhistorial_cambios > $($script:maxHistorial) AND tipo = 'horario' AND accion = 'deshacer' AND registro_id = $b") -eq 1)
    }

    Write-Host "`n== Deshacer no toca lo que se cambio despues ==" -ForegroundColor Cyan
    $r = Llamar 'Aplicar' @($b, 'PRUEBA P4 aplicado', $Desde, $Hasta, $Fin, $null, $script:idAdmin)
    if (Bien 'Aplicar otra vez (sin guardar el de ahora)' $r) {
        $dos = @($recortables.Rows | Where-Object { ([DateTime]$_['vigente_desde']).Date -lt $Fin } | Select-Object -First 2)
        if ($dos.Count -eq 2) {
            $a1 = [int]$dos[0]['idclases']; $a2 = [int]$dos[1]['idclases']
            $script:fechasPorDevolver[$a1] = F ([DateTime]$dos[0]['vigente_hasta'])
            $script:fechasPorDevolver[$a2] = F ([DateTime]$dos[1]['vigente_hasta'])
            $null = Sql "UPDATE clases SET vigente_hasta = '$(F $Fin.AddDays(-1))' WHERE idclases = $a1"
            $t2 = (Sql "SELECT usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo FROM clases WHERE idclases = $a2").Rows[0]
            $igual = Insertar ("INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en) " +
                "VALUES ($($t2[0]), $($t2[1]), $($t2[2]), '$($t2[3])', '$(F $Fin.AddDays(1))', '$(F $Fin.AddDays(3))', 1, NULL, NOW())")
            $script:clasesDePrueba.Add($igual)
            $r = Llamar 'Deshacer' @($b, $script:idAdmin)
            if (Bien 'Deshacer con una clase terminada a mano y otra con una igual despues' $r) {
                Ver "no toca la que se cambio ($($r.Valor.NoDevueltas)) ni la que se cruzaria con otra igual ($($r.Valor.PorOtraIgual)); $($r.Valor.Devueltas) recuperan su fecha" ($r.Valor.NoDevueltas -eq 1 -and $r.Valor.PorOtraIgual -eq 1 -and $r.Valor.Devueltas -eq $res.Terminan - 2)
                Ver '... se quedan como estaban' ((F ([DateTime](Uno "SELECT vigente_hasta FROM clases WHERE idclases = $a1"))) -eq (F $Fin.AddDays(-1)) -and (F ([DateTime](Uno "SELECT vigente_hasta FROM clases WHERE idclases = $a2"))) -eq (F $Fin))
            }
            $null = Sql "DELETE FROM clases WHERE idclases = $igual"
            $script:clasesDePrueba.Remove($igual) | Out-Null
            foreach ($id in @($a1, $a2)) { $null = Sql "UPDATE clases SET vigente_hasta = '$($script:fechasPorDevolver[$id])' WHERE idclases = $id" }
            $script:fechasPorDevolver = @{}
            Ver '... y devueltas a mano, clases otra vez igual que al empezar' ((ContenidoClases) -eq $contenidoClases -and (ContenidoHorarios) -eq $contenidoHorarios)
        } else {
            Ver 'hay dos clases recortadas para probarlo' $false
            $null = Llamar 'Deshacer' @($b, $script:idAdmin)
        }
    }

    Write-Host "`n== Descartar ==" -ForegroundColor Cyan
    Bien 'Descartar el horario de la prueba' (Llamar 'Descartar' @($b)) | Out-Null
}
finally {
    Limpiar
    $tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 0)
    $sumasR1Fin = Sumas $tablasR1
    $sumasRestoFin = Sumas $tablasResto
    Ver "al terminar, CHECKSUM igual que al empezar: $sumasR1Fin" ($sumasR1Inicio -eq $sumasR1Fin) "al empezar: $sumasR1Inicio"
    Ver "... y lo demas tambien: $sumasRestoFin" ($sumasRestoInicio -eq $sumasRestoFin) "al empezar: $sumasRestoInicio"
    Ver 'no quedan datos de la prueba' ("$(Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA P4%'")" -eq '0' -and "$(Uno "SELECT COUNT(*) FROM codigos_accesos WHERE codigo = 'PRBP4'")" -eq '0')
}

Write-Host "`nResultado: $($script:total - $script:malos)/$($script:total) correctas" -ForegroundColor $(if ($script:malos -eq 0) { 'Green' } else { 'Red' })
if ($script:malos -gt 0) { exit 1 }
