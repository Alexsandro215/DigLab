<#
.SYNOPSIS
    Carga el horario de los laboratorios (maestros, materias, grupos, clases y
    franjas) desde dos CSV, en una sola transaccion.

.DESCRIPTION
    Pensado para cada semestre: se transcribe el horario oficial a dos CSV y
    este guion lo sube a la base.

      Profesores (separador ';'):  clave;numero;nombre;apellido_paterno;apellido_materno
        - clave: como se nombra al maestro en el CSV de franjas (TREJO, ZITA...).
        - numero: su numero de empleado (con el que entra a DigBit). Si ya existe
          en la base se usa ese maestro; si no, se da de alta con una contrasena
          aleatoria y su codigo de clase. Solo letras y numeros.

      Franjas (separador ';'):  laboratorio;dia;hora;materia;grupo;profesor
        - una fila por HORA de laboratorio, como las celdas del horario impreso;
          las horas seguidas de la misma clase se juntan en una franja.
        - dia: 1 = lunes ... 7 = domingo. hora: "07:00" (dura una hora).
        - laboratorio, materia y grupo se buscan por nombre sin distinguir
          mayusculas ni acentos. Las materias y grupos que falten se crean en la
          carrera -IdCarrera; los laboratorios tienen que existir.

    Una clase es maestro + grupo + materia, con el codigo del maestro (uno para
    todas sus clases, db/09_codigo_por_profesor.sql). Si ya hay una clase activa
    igual en esas fechas se reutiliza, y una franja identica que aun se da en
    esas fechas no se repite: el guion se puede volver a ejecutar sin duplicar
    nada.

    Con db/20_vigencia_franjas.sql cada franja tiene su propia vigencia: una
    franja terminada (cambiada en Horarios desde una fecha) no cuenta como
    "ya estaba" ni choca con lo nuevo. Si ademas estan encendidos en Ajustes
    los cambios de horario desde una fecha (configuracion.franjas_con_fecha =
    1, como en DigBit), las franjas nuevas de una clase reutilizada que ya
    empezo empiezan en -Desde (o hoy, si -Desde ya paso): asi no aparecen en
    las semanas pasadas, donde Faltas las daria por no dadas. Si no, valen
    toda la vigencia de su clase, como antes (y lo avisa).

    Antes de escribir comprueba que ningun laboratorio, maestro ni grupo quede
    en dos sitios a la misma hora (entre si y contra lo que ya hay en la base,
    con la vigencia de cada franja: solo chocan si en el cruce de las dos cae
    su dia de la semana), igual que la pantalla de Horarios. Si algo choca, no
    escribe nada.

    SIN -Aplicar es un ENSAYO: hace todo dentro de una transaccion y la deshace
    al final. Con -Aplicar respalda antes la base (mysqldump) y confirma. Los
    usuarios y contrasenas de los maestros nuevos quedan en la carpeta de
    respaldos (fuera de git) para repartirlos.

.EXAMPLE
    # Ensayo (no cambia nada):
    .\importar_horarios.ps1 -Profesores ..\db\horarios\2026-09_profesores.csv -Franjas ..\db\horarios\2026-09_franjas.csv `
        -Desde 2026-09-30 -Hasta 2027-02-28 -Servidor sql.teschi.edu.mx -Usuario teschi_otru -Contrasena (Read-Host 'Contrasena') `
        -BinMysql A:\herramientas\mariadb-5.5.68

.EXAMPLE
    # De verdad (respalda y carga), borrando antes dos clases de prueba:
    .\importar_horarios.ps1 ...los mismos parametros... -BorrarClases 1,2 -Aplicar
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$Profesores,
    [Parameter(Mandatory = $true)] [string]$Franjas,
    [Parameter(Mandatory = $true)] [datetime]$Desde,
    [Parameter(Mandatory = $true)] [datetime]$Hasta,

    [string]$Servidor = 'localhost',
    [int]$Puerto = 3306,
    [string]$Base = 'teschi_otru',
    [string]$Usuario = 'root',
    [string]$Contrasena,

    # Carpeta de mysqldump.exe, para el respaldo (solo con -Aplicar).
    [string]$BinMysql,

    # Compilacion de DigBit: de ahi salen MySql.Data y el formato de contrasena (PBKDF2).
    # Por defecto, DigBit\bin\Release.
    [string]$CarpetaApp,

    # Carrera de las materias y grupos nuevos (1 = Sistemas Computacionales en la base del TESCHI).
    [int]$IdCarrera = 1,

    # Clases que se borran antes de cargar (por ejemplo, de prueba). Borrar una
    # clase borra sus franjas y excepciones. Solo clases SIN sesiones: con alguna,
    # el guion se niega (borrarla dejaria su historia sin clase); terminala en
    # Horarios en su lugar.
    [int[]]$BorrarClases = @(),

    # Queda como creador de las clases.
    [string]$Administrador = 'ADMIN001',

    # Por defecto, respaldos\ junto a deploy\.
    [string]$CarpetaRespaldo,

    [switch]$Aplicar
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot llega VACIO en los valores por defecto del bloque param cuando el
# guion se lanza con "powershell -File" (igual que en migrar_bd.ps1): aqui si esta.
if (-not $CarpetaApp) { $CarpetaApp = Join-Path $PSScriptRoot '..\DigBit\bin\Release' }
if (-not $CarpetaRespaldo) { $CarpetaRespaldo = Join-Path $PSScriptRoot '..\respaldos' }

function Paso([string]$t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }

# --- Utilidades -------------------------------------------------------------------

# Minusculas, sin acentos y con un solo espacio: "Graficacion" = "GRAFICACION".
function Normalizar([string]$texto) {
    if ($null -eq $texto) { return '' }
    $descompuesto = $texto.Trim().Normalize([Text.NormalizationForm]::FormD)
    $limpio = New-Object Text.StringBuilder
    foreach ($c in $descompuesto.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($c) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$limpio.Append($c)
        }
    }
    return ([regex]::Replace($limpio.ToString(), '\s+', ' ')).ToLowerInvariant()
}

# Sin letras que se confunden al dictar (0/O, 1/l/I).
$alfabetoCodigo = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789'
$alfabetoClave  = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789'
$azar = New-Object Security.Cryptography.RNGCryptoServiceProvider
function Aleatorio([string]$alfabeto, [int]$largo) {
    $bytes = New-Object byte[] $largo
    $azar.GetBytes($bytes)
    $sb = New-Object Text.StringBuilder
    foreach ($b in $bytes) { [void]$sb.Append($alfabeto[$b % $alfabeto.Length]) }
    return $sb.ToString()
}

function Hora([TimeSpan]$t) { return $t.ToString('hh\:mm') }
$nombresDia = @('', 'lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado', 'domingo')

# --- Datos de entrada -------------------------------------------------------------
Paso '1. Leer los CSV'
if ($Hasta.Date -lt $Desde.Date) { throw "-Hasta ($($Hasta.ToString('yyyy-MM-dd'))) es anterior a -Desde." }

$filasProfesores = @(Import-Csv -LiteralPath $Profesores -Delimiter ';' -Encoding UTF8)
$filasFranjas = @(Import-Csv -LiteralPath $Franjas -Delimiter ';' -Encoding UTF8)
Write-Host ("   {0} docentes y {1} horas de laboratorio" -f $filasProfesores.Count, $filasFranjas.Count)

$profesPorClave = @{}
foreach ($p in $filasProfesores) {
    $clave = $p.clave.Trim().ToUpperInvariant()
    if ($profesPorClave.ContainsKey($clave)) { throw "La clave de docente $clave esta repetida en $Profesores." }
    if ($p.numero.Trim() -notmatch '^[A-Za-z0-9]+$') { throw "El numero '$($p.numero)' de $clave no vale: solo letras y numeros (asi lo pide el inicio de sesion)." }
    $profesPorClave[$clave] = $p
}

# --- Conexion ---------------------------------------------------------------------
Paso '2. Conectar y leer la base'
if (-not (Test-Path (Join-Path $CarpetaApp 'DigBit.exe'))) { throw "No esta DigBit.exe en $CarpetaApp (se usa su MySql.Data y su formato de contrasena)." }

# MySql.Data pide versiones exactas de sus dependencias (System.Runtime.
# CompilerServices.Unsafe 4.0.4.1...) que DigBit.exe.config redirige; aqui no hay
# ese .config, asi que se le da la que ya este cargada, sea cual sea su version.
# En C# y no como bloque de PowerShell: un bloque como manejador de
# AssemblyResolve se vuelve a llamar a si mismo y revienta la pila.
if (-not ('ResolverDependenciasDigBit' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Reflection;
public static class ResolverDependenciasDigBit
{
    public static void Instalar()
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate (object s, ResolveEventArgs e)
        {
            string nombre = new AssemblyName(e.Name).Name;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == nombre) { return a; }
            }
            return null;
        };
    }
}
'@
    [ResolverDependenciasDigBit]::Instalar()
}
foreach ($dll in Get-ChildItem $CarpetaApp -Filter *.dll) { try { [void][Reflection.Assembly]::LoadFrom($dll.FullName) } catch { } }
$app = [Reflection.Assembly]::LoadFrom((Join-Path $CarpetaApp 'DigBit.exe'))
$hashContrasena = $app.GetType('DigBit.conexion.Contrasenas').GetMethod('Hash', [Reflection.BindingFlags]'Static,Public,NonPublic')

$constructor = New-Object MySql.Data.MySqlClient.MySqlConnectionStringBuilder
$constructor.Server = $Servidor
$constructor.Port = [uint32]$Puerto
$constructor.Database = $Base
$constructor.UserID = $Usuario
$constructor.Password = $Contrasena
$constructor.CharacterSet = 'utf8'
$constructor.ConnectionTimeout = 15
$cx = New-Object MySql.Data.MySqlClient.MySqlConnection $constructor.ConnectionString
$cx.Open()
Write-Host "   $Usuario@${Servidor}:$Puerto/$Base"

function Tabla([string]$sql, [hashtable]$parametros = @{}, $tx = $null) {
    $cmd = New-Object MySql.Data.MySqlClient.MySqlCommand $sql, $cx
    if ($tx) { $cmd.Transaction = $tx }
    foreach ($k in $parametros.Keys) { [void]$cmd.Parameters.AddWithValue($k, $parametros[$k]) }
    $t = New-Object Data.DataTable
    $lector = $cmd.ExecuteReader()
    try { $t.Load($lector) } finally { $lector.Dispose() }
    return ,$t
}

function Escalar([string]$sql, [hashtable]$parametros = @{}, $tx = $null) {
    $cmd = New-Object MySql.Data.MySqlClient.MySqlCommand $sql, $cx
    if ($tx) { $cmd.Transaction = $tx }
    foreach ($k in $parametros.Keys) { [void]$cmd.Parameters.AddWithValue($k, $parametros[$k]) }
    return $cmd.ExecuteScalar()
}

# Devuelve el id insertado.
function Insertar([string]$sql, [hashtable]$parametros, $tx) {
    $cmd = New-Object MySql.Data.MySqlClient.MySqlCommand $sql, $cx
    $cmd.Transaction = $tx
    foreach ($k in $parametros.Keys) { [void]$cmd.Parameters.AddWithValue($k, $parametros[$k]) }
    [void]$cmd.ExecuteNonQuery()
    return [int]$cmd.LastInsertedId
}

function Existe([string]$tabla, [string]$columna) {
    return [int](Escalar 'SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c' @{ '@t' = $tabla; '@c' = $columna }) -gt 0
}

$codigoPorProfesor = Existe 'usuarios' 'codigo_clase'
$hayBajasLab = Existe 'laboratorios' 'activo'
$conVigencia = Existe 'horarios' 'vigente_hasta'
# Las franjas nuevas llevan fecha de inicio solo con db/20 y los cambios de horario
# desde una fecha encendidos en Ajustes, como VigenciaFranjas.ConFecha en DigBit:
# antes, un DigBit viejo en algun equipo no veria esa fecha.
$conFecha = $false
if ($conVigencia -and (Existe 'configuracion' 'valor')) {
    $valorConFecha = Escalar "SELECT valor FROM configuracion WHERE clave = 'franjas_con_fecha'"
    $conFecha = $null -ne $valorConFecha -and $valorConFecha -isnot [DBNull] -and "$valorConFecha".Trim() -eq '1'
}
# Sin la 09, clases.codigo sigue siendo UNICO (db/04) y este guion da el mismo
# codigo a todas las clases de un maestro: la carga fallaria, y se desharia, en
# cuanto uno tuviera dos clases nuevas. Mejor pararse ahora y decir por que.
if (-not $codigoPorProfesor) {
    throw 'La base no tiene la migracion 09 (usuarios.codigo_clase): un codigo por docente no cabe en ella. Aplicala antes con deploy\migrar_bd.ps1.'
}
Write-Host '   codigo por docente (db/09): si'
Write-Host ('   vigencia propia de cada franja (db/20): ' + $(if ($conVigencia) { 'si' } else { 'no' }))
if ($conVigencia) { Write-Host ('   cambios de horario desde una fecha (Ajustes): ' + $(if ($conFecha) { 'encendidos' } else { 'apagados' })) }

# "Hoy" es el del servidor con la hora de la aplicacion (la base de la escuela va en UTC).
[void](Escalar "SET time_zone = '-06:00'")
$hoy = ([datetime](Escalar 'SELECT CURDATE()')).Date

$idAdministrador = Escalar 'SELECT idusuarios FROM usuarios WHERE numero_identificador = @n' @{ '@n' = $Administrador }
if ($null -eq $idAdministrador) { throw "No existe la cuenta $Administrador (la que figura como creadora de las clases)." }

$laboratorios = @{}
foreach ($r in (Tabla ('SELECT idlaboratorios, nombre_laboratorio FROM laboratorios' + $(if ($hayBajasLab) { ' WHERE activo = 1' } else { '' })))) {
    $laboratorios[(Normalizar $r.nombre_laboratorio)] = [int]$r.idlaboratorios
}

# Grupos: primero los de la carrera; si no, uno con ese nombre sin carrera.
$grupos = @{}
foreach ($r in (Tabla 'SELECT idgrupos, nombre_grupo, Carrera_id FROM grupos ORDER BY (Carrera_id = @c) DESC, idgrupos' @{ '@c' = $IdCarrera })) {
    $k = Normalizar $r.nombre_grupo
    if (-not $grupos.ContainsKey($k)) { $grupos[$k] = [int]$r.idgrupos }
}

# Materias: con nombres repetidos, la de id mas bajo.
$materias = @{}
foreach ($r in (Tabla 'SELECT id_materia, nombre_materia FROM materias ORDER BY id_materia')) {
    $k = Normalizar $r.nombre_materia
    if (-not $materias.ContainsKey($k)) { $materias[$k] = [int]$r.id_materia }
}

$usuarios = @{}
$columnasUsuario = 'idusuarios, numero_identificador, fk_tipo_usuario' + $(if ($codigoPorProfesor) { ', codigo_clase' } else { ', NULL AS codigo_clase' })
foreach ($r in (Tabla "SELECT $columnasUsuario FROM usuarios")) {
    $usuarios[$r.numero_identificador.ToString().ToUpperInvariant()] = $r
}

$codigosUsados = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($r in (Tabla ('SELECT DISTINCT codigo FROM clases' + $(if ($codigoPorProfesor) { ' UNION SELECT codigo_clase FROM usuarios WHERE codigo_clase IS NOT NULL' } else { '' })))) {
    [void]$codigosUsados.Add($r[0].ToString())
}

$clasesExistentes = Tabla 'SELECT idclases, usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa FROM clases'
# f_desde y f_hasta: la vigencia de cada franja, la de su clase recortada a la
# suya propia (db/20; sin ella, la de la clase). Como VigenciaFranjas.Desde/Hasta.
$vigenciaFranja = if ($conVigencia) {
    'GREATEST(c.vigente_desde, COALESCE(h.vigente_desde, c.vigente_desde)) AS f_desde, LEAST(c.vigente_hasta, COALESCE(h.vigente_hasta, c.vigente_hasta)) AS f_hasta'
} else { 'c.vigente_desde AS f_desde, c.vigente_hasta AS f_hasta' }
$franjasExistentes = Tabla @"
SELECT h.idhorarios, h.clases_idclases, h.laboratorios_idlaboratorios, h.dia_semana, h.hora_inicio, h.hora_fin,
       c.usuarios_idusuarios, c.grupos_idgrupos, c.activa, c.vigente_desde, c.vigente_hasta, $vigenciaFranja,
       l.nombre_laboratorio, g.nombre_grupo, m.nombre_materia
FROM horarios h
JOIN clases c ON c.idclases = h.clases_idclases
JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
JOIN grupos g ON g.idgrupos = c.grupos_idgrupos
JOIN materias m ON m.id_materia = c.materias_id_materia
"@
Write-Host ("   {0} laboratorios, {1} grupos, {2} materias, {3} clases y {4} franjas en la base" -f `
    $laboratorios.Count, $grupos.Count, $materias.Count, $clasesExistentes.Rows.Count, $franjasExistentes.Rows.Count)

foreach ($id in $BorrarClases) {
    if (-not ($clasesExistentes.Rows | Where-Object { [int]$_.idclases -eq $id })) { throw "La clase $id de -BorrarClases no existe." }
    # Sus sesiones como las cuenta DigBit (VigenciaFranjas.SesionDeLaClase): las de
    # sus franjas y sesiones extra, y las sin enlace de su profesor, materia y grupo
    # en su vigencia (las de antes de los horarios, sin fecha, por el dia en que se
    # crearon). Con alguna, borrarla le quitaria su historia (Faltas, asistencia).
    $sesiones = [int](Escalar @'
SELECT COUNT(*) FROM clases c JOIN codigos_accesos s
  ON (s.horarios_idhorarios IN (SELECT hh.idhorarios FROM horarios hh WHERE hh.clases_idclases = c.idclases)
      OR s.horario_excepciones_id IN (SELECT xx.idhorario_excepciones FROM horario_excepciones xx WHERE xx.clases_idclases = c.idclases)
      OR (s.horarios_idhorarios IS NULL AND s.horario_excepciones_id IS NULL
          AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia
          AND s.grupos_idgrupos = c.grupos_idgrupos AND COALESCE(s.fecha, DATE(s.hora_registro)) BETWEEN c.vigente_desde AND c.vigente_hasta))
WHERE c.idclases = @id
'@ @{ '@id' = $id })
    if ($sesiones -gt 0) {
        throw "La clase $id de -BorrarClases tiene $sesiones sesion(es): borrarla le quitaria su historia. Terminala en Horarios (Clases, Terminar clase...) y quitala de -BorrarClases."
    }
}

# --- Plan -------------------------------------------------------------------------
Paso '3. Preparar el plan'
$errores = New-Object Collections.Generic.List[string]
$gruposNuevos = [ordered]@{}
$materiasNuevas = [ordered]@{}

$horas = foreach ($f in $filasFranjas) {
    $lab = Normalizar $f.laboratorio
    if (-not $laboratorios.ContainsKey($lab)) { $errores.Add("No existe (o esta desactivado) el laboratorio '$($f.laboratorio)'."); continue }
    $clave = $f.profesor.Trim().ToUpperInvariant()
    if (-not $profesPorClave.ContainsKey($clave)) { $errores.Add("El docente '$($f.profesor)' no esta en $Profesores."); continue }
    $dia = [int]$f.dia
    if ($dia -lt 1 -or $dia -gt 7) { $errores.Add("Dia '$($f.dia)' no valido en $($f.laboratorio) $($f.hora)."); continue }
    $inicio = [TimeSpan]::Parse($f.hora)
    $g = Normalizar $f.grupo
    if (-not $grupos.ContainsKey($g) -and -not $gruposNuevos.Contains($g)) { $gruposNuevos[$g] = $f.grupo.Trim() }
    $m = Normalizar $f.materia
    if (-not $materias.ContainsKey($m) -and -not $materiasNuevas.Contains($m)) { $materiasNuevas[$m] = $f.materia.Trim() }
    [pscustomobject]@{ Lab = $lab; Dia = $dia; Inicio = $inicio; Fin = $inicio.Add([TimeSpan]::FromHours(1)); Materia = $m; Grupo = $g; Profesor = $clave }
}

# Las horas seguidas de la misma clase en el mismo laboratorio y dia, una franja.
$franjasPlan = New-Object Collections.Generic.List[object]
$ordenadas = $horas | Sort-Object Lab, Dia, Profesor, Grupo, Materia, Inicio
foreach ($h in $ordenadas) {
    $ultima = if ($franjasPlan.Count -gt 0) { $franjasPlan[$franjasPlan.Count - 1] } else { $null }
    if ($ultima -and $ultima.Lab -eq $h.Lab -and $ultima.Dia -eq $h.Dia -and $ultima.Profesor -eq $h.Profesor `
        -and $ultima.Grupo -eq $h.Grupo -and $ultima.Materia -eq $h.Materia -and $ultima.Fin -eq $h.Inicio) {
        $ultima.Fin = $h.Fin
    } else {
        $franjasPlan.Add([pscustomobject]@{ Lab = $h.Lab; Dia = $h.Dia; Inicio = $h.Inicio; Fin = $h.Fin; Materia = $h.Materia; Grupo = $h.Grupo; Profesor = $h.Profesor })
    }
}

# Maestros: existentes (por numero) o nuevos, cada uno con su codigo.
$profesPlan = @{}
foreach ($clave in ($franjasPlan | Select-Object -ExpandProperty Profesor -Unique)) {
    $p = $profesPorClave[$clave]
    $numero = $p.numero.Trim()
    $existente = $usuarios[$numero.ToUpperInvariant()]
    if ($existente -and [int]$existente.fk_tipo_usuario -ne 2) { $errores.Add("El numero $numero ($clave) ya es de una cuenta que no es docente."); continue }
    $codigo = if ($existente -and $existente.codigo_clase -isnot [DBNull] -and $existente.codigo_clase) { $existente.codigo_clase.ToString() } else { $null }
    $profesPlan[$clave] = [pscustomobject]@{
        Clave = $clave; Numero = $numero; Nombre = $p.nombre.Trim(); Paterno = $p.apellido_paterno.Trim(); Materno = $p.apellido_materno.Trim()
        Id = $(if ($existente) { [int]$existente.idusuarios } else { 0 })
        Nuevo = -not $existente; Codigo = $codigo; CodigoNuevo = $false; Contrasena = $null
    }
}
foreach ($p in $profesPlan.Values) {
    if (-not $p.Codigo) {
        do { $c = Aleatorio $alfabetoCodigo 5 } while ($codigosUsados.Contains($c))
        [void]$codigosUsados.Add($c)
        $p.Codigo = $c
        $p.CodigoNuevo = $true
    }
    if ($p.Nuevo) { $p.Contrasena = Aleatorio $alfabetoClave 8 }
}

# Clases: maestro + grupo + materia. Se reutiliza una activa igual que se cruce con las fechas.
$clasesPlan = @{}
foreach ($f in $franjasPlan) {
    $k = "$($f.Profesor)|$($f.Grupo)|$($f.Materia)"
    if ($clasesPlan.ContainsKey($k)) { continue }
    $p = $profesPlan[$f.Profesor]
    $existente = $null
    if (-not $p.Nuevo -and $grupos.ContainsKey($f.Grupo) -and $materias.ContainsKey($f.Materia)) {
        $existente = $clasesExistentes.Rows | Where-Object {
            [int]$_.usuarios_idusuarios -eq $p.Id -and [int]$_.grupos_idgrupos -eq $grupos[$f.Grupo] -and [int]$_.materias_id_materia -eq $materias[$f.Materia] `
            -and [int]$_.activa -eq 1 -and $_.vigente_desde -le $Hasta.Date -and $_.vigente_hasta -ge $Desde.Date -and $BorrarClases -notcontains [int]$_.idclases
        } | Select-Object -First 1
    }
    # Vigencia de la clase (la de la base si se reutiliza) y desde cuando valen sus
    # franjas nuevas: si la clase ya empezo, desde -Desde o desde hoy si -Desde ya
    # paso (con db/20 y los cambios desde una fecha encendidos); asi no salen en las
    # semanas pasadas. Si no, toda la clase, como antes.
    $claseDesde = if ($existente) { ([datetime]$existente.vigente_desde).Date } else { $Desde.Date }
    $claseHasta = if ($existente) { ([datetime]$existente.vigente_hasta).Date } else { $Hasta.Date }
    $inicioFranjas = if ($Desde.Date -gt $hoy) { $Desde.Date } else { $hoy }
    $franjasDesde = if ($conFecha -and $inicioFranjas -gt $claseDesde) { $inicioFranjas } else { $null }
    if ($existente -and $franjasDesde -and $franjasDesde -gt $claseHasta) {
        $errores.Add("La clase $($existente.idclases) ($($f.Materia), $($f.Grupo), $($f.Profesor)) termina el $($claseHasta.ToString('yyyy-MM-dd')): sus franjas nuevas no se darian nunca. Alarga su vigencia en Horarios o carga con otras fechas.")
    }
    $clasesPlan[$k] = [pscustomobject]@{
        Profesor = $f.Profesor; Grupo = $f.Grupo; Materia = $f.Materia; Id = $(if ($existente) { [int]$existente.idclases } else { 0 })
        Desde = $claseDesde; Hasta = $claseHasta; FranjasDesde = $franjasDesde
    }
}

# Cada franja nueva vale de FranjasDesde (o el inicio de su clase) al fin de su clase.
foreach ($f in $franjasPlan) {
    $clase = $clasesPlan["$($f.Profesor)|$($f.Grupo)|$($f.Materia)"]
    $f | Add-Member -NotePropertyName VigenteDesde -NotePropertyValue $(if ($clase.FranjasDesde) { $clase.FranjasDesde } else { $clase.Desde })
    $f | Add-Member -NotePropertyName VigenteHasta -NotePropertyValue $clase.Hasta
}

# Si dos vigencias se dan a la vez algun dia de la semana $dia (1 = lunes). Con
# db/20, la regla de HorariosDatos.ValidarFranja: en su cruce tiene que caer ese
# dia (una franja de los lunes que termina un martes no coincide con otra de los
# lunes que empieza el jueves de esa semana). Sin db/20 basta con cruzarse.
function SeDanALaVez([datetime]$aDesde, [datetime]$aHasta, [datetime]$bDesde, [datetime]$bHasta, [int]$dia) {
    $desdeComun = if ($aDesde.Date -gt $bDesde.Date) { $aDesde.Date } else { $bDesde.Date }
    $hastaComun = if ($aHasta.Date -lt $bHasta.Date) { $aHasta.Date } else { $bHasta.Date }
    if ($desdeComun -gt $hastaComun) { return $false }
    if (-not $conVigencia) { return $true }
    $diaDesde = ((([int]$desdeComun.DayOfWeek) + 6) % 7) + 1
    return $desdeComun.AddDays(($dia - $diaDesde + 7) % 7) -le $hastaComun
}

# Franjas que ya estan tal cual y aun se dan en esas fechas (volver a ejecutar el
# guion no duplica). Una identica ya terminada (db/20) no cuenta: se vuelve a crear.
$repetidas = 0
foreach ($f in $franjasPlan) {
    $clase = $clasesPlan["$($f.Profesor)|$($f.Grupo)|$($f.Materia)"]
    $f | Add-Member -NotePropertyName Omitir -NotePropertyValue $false
    if ($clase.Id -ne 0) {
        $igual = $franjasExistentes.Rows | Where-Object {
            [int]$_.clases_idclases -eq $clase.Id -and [int]$_.laboratorios_idlaboratorios -eq $laboratorios[$f.Lab] -and [int]$_.dia_semana -eq $f.Dia `
            -and $_.hora_inicio -eq $f.Inicio -and $_.hora_fin -eq $f.Fin `
            -and (SeDanALaVez ([datetime]$_.f_desde) ([datetime]$_.f_hasta) $f.VigenteDesde $f.VigenteHasta $f.Dia)
        }
        if ($igual) { $f.Omitir = $true; $repetidas++ }
    }
}

# --- Choques ----------------------------------------------------------------------
# Lo mismo que HorariosDatos.ValidarFranja: nada a la misma hora en el mismo
# laboratorio, ni el mismo maestro ni el mismo grupo en dos sitios, en los dias en
# que las dos se dan (SeDanALaVez, con la vigencia de cada franja: una terminada no
# choca con la que la sustituye).
function Describir($f) { return "{0} {1}-{2} {3}: {4}, {5} ({6})" -f $nombresDia[$f.Dia], (Hora $f.Inicio), (Hora $f.Fin), $f.Lab, $f.Materia, $f.Grupo, $f.Profesor }
$nuevas = @($franjasPlan | Where-Object { -not $_.Omitir })
for ($i = 0; $i -lt $nuevas.Count; $i++) {
    for ($j = $i + 1; $j -lt $nuevas.Count; $j++) {
        $a = $nuevas[$i]; $b = $nuevas[$j]
        if ($a.Dia -ne $b.Dia -or $a.Inicio -ge $b.Fin -or $b.Inicio -ge $a.Fin) { continue }
        if (-not (SeDanALaVez $a.VigenteDesde $a.VigenteHasta $b.VigenteDesde $b.VigenteHasta $a.Dia)) { continue }
        $motivo = if ($a.Lab -eq $b.Lab) { 'mismo laboratorio' } elseif ($a.Profesor -eq $b.Profesor) { 'misma persona docente' } elseif ($a.Grupo -eq $b.Grupo) { 'mismo grupo' } else { $null }
        if ($motivo) { $errores.Add("Choque ($motivo): $(Describir $a)  <->  $(Describir $b)") }
    }
}
foreach ($e in $franjasExistentes.Rows) {
    if ([int]$e.activa -ne 1 -or $BorrarClases -contains [int]$e.clases_idclases) { continue }
    $eDesde = ([datetime]$e.f_desde).Date
    $eHasta = ([datetime]$e.f_hasta).Date
    foreach ($n in $nuevas) {
        if ($n.Dia -ne [int]$e.dia_semana -or $n.Inicio -ge $e.hora_fin -or $e.hora_inicio -ge $n.Fin) { continue }
        if (-not (SeDanALaVez $eDesde $eHasta $n.VigenteDesde $n.VigenteHasta $n.Dia)) { continue }
        $p = $profesPlan[$n.Profesor]
        $motivo = if ($laboratorios[$n.Lab] -eq [int]$e.laboratorios_idlaboratorios) { 'mismo laboratorio' } `
            elseif ($p.Id -ne 0 -and $p.Id -eq [int]$e.usuarios_idusuarios) { 'misma persona docente' } `
            elseif ($grupos.ContainsKey($n.Grupo) -and $grupos[$n.Grupo] -eq [int]$e.grupos_idgrupos) { 'mismo grupo' } else { $null }
        if ($motivo) {
            # Entre parentesis: dentro de Add(...) las comas del -f serian otros argumentos.
            $errores.Add(("Choque con la base ($motivo): $(Describir $n)  <->  clase $($e.clases_idclases) {0} {1}-{2} {3}: {4}, {5}" -f `
                $nombresDia[[int]$e.dia_semana], $e.hora_inicio.ToString('hh\:mm'), $e.hora_fin.ToString('hh\:mm'), $e.nombre_laboratorio, $e.nombre_materia, $e.nombre_grupo))
        }
    }
}

$clasesNuevas = @($clasesPlan.Values | Where-Object { $_.Id -eq 0 }).Count
Write-Host ("   Grupos nuevos   ({0}): {1}" -f $gruposNuevos.Count, ($gruposNuevos.Values -join ', '))
Write-Host ("   Materias nuevas ({0}): {1}" -f $materiasNuevas.Count, ($materiasNuevas.Values -join ', '))
Write-Host ("   Docentes nuevos ({0}): {1}" -f @($profesPlan.Values | Where-Object Nuevo).Count, ((@($profesPlan.Values | Where-Object Nuevo) | Sort-Object Clave | ForEach-Object { $_.Numero }) -join ', '))
Write-Host ("   Docentes que ya estaban: {0}" -f ((@($profesPlan.Values | Where-Object { -not $_.Nuevo }) | ForEach-Object { "$($_.Numero) (codigo $($_.Codigo))" }) -join ', '))
Write-Host ("   Clases: {0} nuevas, {1} ya existentes" -f $clasesNuevas, ($clasesPlan.Count - $clasesNuevas))
Write-Host ("   Franjas: {0} nuevas (de {1} horas), {2} ya estaban" -f $nuevas.Count, $filasFranjas.Count, $repetidas)
$conInicio = @($nuevas | Where-Object { $clasesPlan["$($_.Profesor)|$($_.Grupo)|$($_.Materia)"].FranjasDesde })
if ($conInicio.Count -gt 0) {
    Write-Host ("   {0} franja(s) nuevas de clases que ya empezaron valen desde el {1} (no cuentan en las semanas pasadas)" -f $conInicio.Count, $conInicio[0].VigenteDesde.ToString('yyyy-MM-dd'))
} elseif (-not $conFecha -and @($nuevas | Where-Object { $clasesPlan["$($_.Profesor)|$($_.Grupo)|$($_.Materia)"].Desde -lt $hoy }).Count -gt 0) {
    $porQue = if (-not $conVigencia) { 'sin db/20' } else { 'con los cambios de horario desde una fecha apagados en Ajustes' }
    Write-Host "   AVISO: $porQue las franjas nuevas de clases que ya empezaron valen tambien en sus semanas pasadas (Faltas las juzgara)." -ForegroundColor Yellow
}
if ($BorrarClases.Count -gt 0) { Write-Host ("   Se borran antes las clases: {0}" -f ($BorrarClases -join ', ')) }

if ($errores.Count -gt 0) {
    Write-Host ''
    foreach ($e in $errores) { Write-Host "   $e" -ForegroundColor Red }
    $cx.Close()
    throw "Hay $($errores.Count) problema(s). NO se ha cambiado nada."
}

# --- Respaldo (solo de verdad) ----------------------------------------------------
if ($Aplicar) {
    Paso '4. Respaldo'
    $dump = if ($BinMysql) { Join-Path $BinMysql 'mysqldump.exe' } else { 'mysqldump.exe' }
    if ($BinMysql -and -not (Test-Path $dump)) { $dump = Join-Path $BinMysql 'bin\mysqldump.exe' }
    if (-not (Get-Command $dump -ErrorAction SilentlyContinue)) { throw "No se encontro mysqldump.exe (indica -BinMysql). Sin respaldo no se sigue." }
    if (-not (Test-Path $CarpetaRespaldo)) { New-Item -ItemType Directory -Force $CarpetaRespaldo | Out-Null }
    $archivoRespaldo = Join-Path $CarpetaRespaldo ("$Base-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-antes-de-horarios.sql')
    if ($Contrasena) { $env:MYSQL_PWD = $Contrasena }
    $previo = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $dump -h $Servidor -P $Puerto -u $Usuario --default-character-set=utf8mb4 --single-transaction --no-tablespaces --skip-triggers "--result-file=$archivoRespaldo" $Base
        $codigoDump = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previo
        Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
    }
    if ($codigoDump -ne 0 -or -not (Test-Path $archivoRespaldo) -or -not (Select-String -Path $archivoRespaldo -Pattern 'CREATE TABLE' -Quiet)) {
        throw "El respaldo fallo (mysqldump devolvio $codigoDump). Sin respaldo no se sigue."
    }
    Write-Host ("   {0} ({1:N0} KB)" -f $archivoRespaldo, ((Get-Item $archivoRespaldo).Length / 1KB)) -ForegroundColor Green
}

# --- Escribir ---------------------------------------------------------------------
Paso $(if ($Aplicar) { '5. Cargar' } else { '4. Ensayo (todo se deshace al final)' })
$tx = $cx.BeginTransaction()
try {
    [void](Escalar "SET time_zone = '-06:00'" @{} $tx)

    foreach ($id in $BorrarClases) {
        [void](Escalar 'DELETE FROM clases WHERE idclases = @id' @{ '@id' = $id } $tx)
    }

    foreach ($k in @($gruposNuevos.Keys)) {
        $grupos[$k] = Insertar 'INSERT INTO grupos (nombre_grupo, Carrera_id) VALUES (@n, @c)' @{ '@n' = $gruposNuevos[$k]; '@c' = $IdCarrera } $tx
    }

    foreach ($k in @($materiasNuevas.Keys)) {
        $materias[$k] = Insertar 'INSERT INTO materias (nombre_materia, idcarreras) VALUES (@n, @c)' @{ '@n' = $materiasNuevas[$k]; '@c' = $IdCarrera } $tx
    }

    foreach ($p in $profesPlan.Values) {
        if ($p.Nuevo) {
            $hash = $hashContrasena.Invoke($null, @([string]$p.Contrasena))
            $columnas = 'numero_identificador, nombre, apellido_paterno, apellido_materno, correo, password, fk_tipo_usuario'
            $valores = '@num, @nom, @pat, @mat, NULL, @pwd, 2'
            if ($codigoPorProfesor) { $columnas += ', codigo_clase'; $valores += ', @cod' }
            $p.Id = Insertar "INSERT INTO usuarios ($columnas) VALUES ($valores)" @{
                '@num' = $p.Numero; '@nom' = $p.Nombre; '@pat' = $p.Paterno; '@mat' = $p.Materno; '@pwd' = $hash; '@cod' = $p.Codigo } $tx
        } elseif ($p.CodigoNuevo -and $codigoPorProfesor) {
            [void](Escalar 'UPDATE usuarios SET codigo_clase = @cod WHERE idusuarios = @id' @{ '@cod' = $p.Codigo; '@id' = $p.Id } $tx)
        }
    }

    foreach ($c in $clasesPlan.Values) {
        if ($c.Id -ne 0) { continue }
        $p = $profesPlan[$c.Profesor]
        $c.Id = Insertar @'
INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en)
VALUES (@prof, @grupo, @materia, @cod, @desde, @hasta, 1, @admin, NOW())
'@ @{ '@prof' = $p.Id; '@grupo' = $grupos[$c.Grupo]; '@materia' = $materias[$c.Materia]; '@cod' = $p.Codigo
      '@desde' = $Desde.Date; '@hasta' = $Hasta.Date; '@admin' = [int]$idAdministrador } $tx
    }

    foreach ($f in $nuevas) {
        $c = $clasesPlan["$($f.Profesor)|$($f.Grupo)|$($f.Materia)"]
        if ($c.FranjasDesde) {
            [void](Insertar 'INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin, vigente_desde) VALUES (@c, @l, @d, @i, @f, @v)' @{
                '@c' = $c.Id; '@l' = $laboratorios[$f.Lab]; '@d' = $f.Dia; '@i' = $f.Inicio; '@f' = $f.Fin; '@v' = $c.FranjasDesde } $tx)
        } else {
            [void](Insertar 'INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES (@c, @l, @d, @i, @f)' @{
                '@c' = $c.Id; '@l' = $laboratorios[$f.Lab]; '@d' = $f.Dia; '@i' = $f.Inicio; '@f' = $f.Fin } $tx)
        }
    }

    # Comprobacion dentro de la misma transaccion: lo que queda en la base (las
    # franjas que se dan algun dia de esas fechas, con su propia vigencia si la hay).
    $franjaEnFechas = if ($conVigencia) {
        ' AND (h.vigente_desde IS NULL OR h.vigente_desde <= @hasta) AND (h.vigente_hasta IS NULL OR h.vigente_hasta >= @desde)'
    } else { '' }
    $resumen = Tabla @"
SELECT l.nombre_laboratorio AS laboratorio, COUNT(*) AS franjas, SUM(TIME_TO_SEC(TIMEDIFF(h.hora_fin, h.hora_inicio))) / 3600 AS horas
FROM horarios h JOIN clases c ON c.idclases = h.clases_idclases JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
WHERE c.activa = 1 AND c.vigente_desde <= @hasta AND c.vigente_hasta >= @desde$franjaEnFechas
GROUP BY l.nombre_laboratorio ORDER BY l.nombre_laboratorio
"@ @{ '@desde' = $Desde.Date; '@hasta' = $Hasta.Date } $tx
    foreach ($r in $resumen.Rows) { Write-Host ("   {0,-24} {1,3} franjas, {2,3} horas" -f $r.laboratorio, $r.franjas, [int]$r.horas) }

    if ($Aplicar) {
        $tx.Commit()
        Write-Host '   Confirmado.' -ForegroundColor Green
    } else {
        $tx.Rollback()
        Write-Host '   ENSAYO: todo se deshizo; la base queda como estaba. Para cargar de verdad, repite con -Aplicar.' -ForegroundColor Yellow
    }
} catch {
    try { $tx.Rollback() } catch { }
    $cx.Close()
    throw "Fallo al escribir; se deshizo todo, la base queda como estaba. Detalle: $($_.Exception.Message)"
}
$cx.Close()

# --- Lista para repartir ----------------------------------------------------------
if ($Aplicar) {
    Paso '6. Cuentas de los docentes'
    $lista = Join-Path $CarpetaRespaldo ('maestros-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.csv')
    $lineas = New-Object Collections.Generic.List[string]
    $lineas.Add('Docente;Usuario (numero de empleado);Contrasena inicial;Codigo de clase')
    foreach ($p in ($profesPlan.Values | Sort-Object Paterno, Materno, Nombre)) {
        $contrasena = if ($p.Nuevo) { $p.Contrasena } else { '(la que ya tenia)' }
        $lineas.Add(('{0} {1} {2};{3};{4};{5}' -f $p.Nombre, $p.Paterno, $p.Materno, $p.Numero, $contrasena, $p.Codigo))
    }
    # Con BOM: Excel lo abre con los acentos bien.
    [IO.File]::WriteAllLines($lista, $lineas, (New-Object Text.UTF8Encoding $true))
    Write-Host "   $lista" -ForegroundColor Green
    Write-Host '   Tiene contrasenas: repartelas en persona y no la subas a git (respaldos\ esta excluida).' -ForegroundColor Yellow
}
