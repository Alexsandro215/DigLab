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
    igual en esas fechas se reutiliza, y una franja identica no se repite: el
    guion se puede volver a ejecutar sin duplicar nada.

    Antes de escribir comprueba que ningun laboratorio, maestro ni grupo quede
    en dos sitios a la misma hora (entre si y contra lo que ya hay en la base),
    igual que la pantalla de Horarios. Si algo choca, no escribe nada.

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
    [string]$CarpetaApp = (Join-Path $PSScriptRoot '..\DigBit\bin\Release'),

    # Carrera de las materias y grupos nuevos (1 = Sistemas Computacionales en la base del TESCHI).
    [int]$IdCarrera = 1,

    # Clases que se borran antes de cargar (por ejemplo, de prueba). Borrar una
    # clase borra sus franjas y excepciones; sus sesiones pasadas se conservan.
    [int[]]$BorrarClases = @(),

    # Queda como creador de las clases.
    [string]$Administrador = 'ADMIN001',

    [string]$CarpetaRespaldo = (Join-Path $PSScriptRoot '..\respaldos'),

    [switch]$Aplicar
)

$ErrorActionPreference = 'Stop'

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
Write-Host ("   {0} maestros y {1} horas de laboratorio" -f $filasProfesores.Count, $filasFranjas.Count)

$profesPorClave = @{}
foreach ($p in $filasProfesores) {
    $clave = $p.clave.Trim().ToUpperInvariant()
    if ($profesPorClave.ContainsKey($clave)) { throw "La clave de maestro $clave esta repetida en $Profesores." }
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
# Sin la 09, clases.codigo sigue siendo UNICO (db/04) y este guion da el mismo
# codigo a todas las clases de un maestro: la carga fallaria, y se desharia, en
# cuanto uno tuviera dos clases nuevas. Mejor pararse ahora y decir por que.
if (-not $codigoPorProfesor) {
    throw 'La base no tiene la migracion 09 (usuarios.codigo_clase): un codigo por maestro no cabe en ella. Aplicala antes con deploy\migrar_bd.ps1.'
}
Write-Host '   codigo por profesor (db/09): si'

$idAdministrador = Escalar 'SELECT idusuarios FROM usuarios WHERE numero_identificador = @n' @{ '@n' = $Administrador }
if ($null -eq $idAdministrador) { throw "No existe el usuario $Administrador (creador de las clases)." }

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
$franjasExistentes = Tabla @'
SELECT h.idhorarios, h.clases_idclases, h.laboratorios_idlaboratorios, h.dia_semana, h.hora_inicio, h.hora_fin,
       c.usuarios_idusuarios, c.grupos_idgrupos, c.activa, c.vigente_desde, c.vigente_hasta,
       l.nombre_laboratorio, g.nombre_grupo, m.nombre_materia
FROM horarios h
JOIN clases c ON c.idclases = h.clases_idclases
JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
JOIN grupos g ON g.idgrupos = c.grupos_idgrupos
JOIN materias m ON m.id_materia = c.materias_id_materia
'@
Write-Host ("   {0} laboratorios, {1} grupos, {2} materias, {3} clases y {4} franjas en la base" -f `
    $laboratorios.Count, $grupos.Count, $materias.Count, $clasesExistentes.Rows.Count, $franjasExistentes.Rows.Count)

foreach ($id in $BorrarClases) {
    if (-not ($clasesExistentes.Rows | Where-Object { [int]$_.idclases -eq $id })) { throw "La clase $id de -BorrarClases no existe." }
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
    if (-not $profesPorClave.ContainsKey($clave)) { $errores.Add("El maestro '$($f.profesor)' no esta en $Profesores."); continue }
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
    if ($existente -and [int]$existente.fk_tipo_usuario -ne 2) { $errores.Add("El numero $numero ($clave) ya es de un usuario que no es maestro."); continue }
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
    $clasesPlan[$k] = [pscustomobject]@{ Profesor = $f.Profesor; Grupo = $f.Grupo; Materia = $f.Materia; Id = $(if ($existente) { [int]$existente.idclases } else { 0 }) }
}

# Franjas que ya estan tal cual (volver a ejecutar el guion no duplica).
$repetidas = 0
foreach ($f in $franjasPlan) {
    $clase = $clasesPlan["$($f.Profesor)|$($f.Grupo)|$($f.Materia)"]
    $f | Add-Member -NotePropertyName Omitir -NotePropertyValue $false
    if ($clase.Id -ne 0) {
        $igual = $franjasExistentes.Rows | Where-Object {
            [int]$_.clases_idclases -eq $clase.Id -and [int]$_.laboratorios_idlaboratorios -eq $laboratorios[$f.Lab] -and [int]$_.dia_semana -eq $f.Dia `
            -and $_.hora_inicio -eq $f.Inicio -and $_.hora_fin -eq $f.Fin
        }
        if ($igual) { $f.Omitir = $true; $repetidas++ }
    }
}

# --- Choques ----------------------------------------------------------------------
# Lo mismo que HorariosDatos.ValidarFranja: nada a la misma hora en el mismo
# laboratorio, ni el mismo maestro ni el mismo grupo en dos sitios.
function Describir($f) { return "{0} {1}-{2} {3}: {4}, {5} ({6})" -f $nombresDia[$f.Dia], (Hora $f.Inicio), (Hora $f.Fin), $f.Lab, $f.Materia, $f.Grupo, $f.Profesor }
$nuevas = @($franjasPlan | Where-Object { -not $_.Omitir })
for ($i = 0; $i -lt $nuevas.Count; $i++) {
    for ($j = $i + 1; $j -lt $nuevas.Count; $j++) {
        $a = $nuevas[$i]; $b = $nuevas[$j]
        if ($a.Dia -ne $b.Dia -or $a.Inicio -ge $b.Fin -or $b.Inicio -ge $a.Fin) { continue }
        $motivo = if ($a.Lab -eq $b.Lab) { 'mismo laboratorio' } elseif ($a.Profesor -eq $b.Profesor) { 'mismo maestro' } elseif ($a.Grupo -eq $b.Grupo) { 'mismo grupo' } else { $null }
        if ($motivo) { $errores.Add("Choque ($motivo): $(Describir $a)  <->  $(Describir $b)") }
    }
}
foreach ($e in $franjasExistentes.Rows) {
    if ([int]$e.activa -ne 1 -or $BorrarClases -contains [int]$e.clases_idclases) { continue }
    if ($e.vigente_desde -gt $Hasta.Date -or $e.vigente_hasta -lt $Desde.Date) { continue }
    foreach ($n in $nuevas) {
        if ($n.Dia -ne [int]$e.dia_semana -or $n.Inicio -ge $e.hora_fin -or $e.hora_inicio -ge $n.Fin) { continue }
        $p = $profesPlan[$n.Profesor]
        $motivo = if ($laboratorios[$n.Lab] -eq [int]$e.laboratorios_idlaboratorios) { 'mismo laboratorio' } `
            elseif ($p.Id -ne 0 -and $p.Id -eq [int]$e.usuarios_idusuarios) { 'mismo maestro' } `
            elseif ($grupos.ContainsKey($n.Grupo) -and $grupos[$n.Grupo] -eq [int]$e.grupos_idgrupos) { 'mismo grupo' } else { $null }
        if ($motivo) {
            $errores.Add("Choque con la base ($motivo): $(Describir $n)  <->  clase $($e.clases_idclases) {0} {1}-{2} {3}: {4}, {5}" -f `
                $nombresDia[[int]$e.dia_semana], $e.hora_inicio.ToString('hh\:mm'), $e.hora_fin.ToString('hh\:mm'), $e.nombre_laboratorio, $e.nombre_materia, $e.nombre_grupo)
        }
    }
}

$clasesNuevas = @($clasesPlan.Values | Where-Object { $_.Id -eq 0 }).Count
Write-Host ("   Grupos nuevos   ({0}): {1}" -f $gruposNuevos.Count, ($gruposNuevos.Values -join ', '))
Write-Host ("   Materias nuevas ({0}): {1}" -f $materiasNuevas.Count, ($materiasNuevas.Values -join ', '))
Write-Host ("   Maestros nuevos ({0}): {1}" -f @($profesPlan.Values | Where-Object Nuevo).Count, ((@($profesPlan.Values | Where-Object Nuevo) | Sort-Object Clave | ForEach-Object { $_.Numero }) -join ', '))
Write-Host ("   Maestros que ya estaban: {0}" -f ((@($profesPlan.Values | Where-Object { -not $_.Nuevo }) | ForEach-Object { "$($_.Numero) (codigo $($_.Codigo))" }) -join ', '))
Write-Host ("   Clases: {0} nuevas, {1} ya existentes" -f $clasesNuevas, ($clasesPlan.Count - $clasesNuevas))
Write-Host ("   Franjas: {0} nuevas (de {1} horas), {2} ya estaban" -f $nuevas.Count, $filasFranjas.Count, $repetidas)
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
        [void](Insertar 'INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES (@c, @l, @d, @i, @f)' @{
            '@c' = $c.Id; '@l' = $laboratorios[$f.Lab]; '@d' = $f.Dia; '@i' = $f.Inicio; '@f' = $f.Fin } $tx)
    }

    # Comprobacion dentro de la misma transaccion: lo que queda en la base.
    $resumen = Tabla @'
SELECT l.nombre_laboratorio AS laboratorio, COUNT(*) AS franjas, SUM(TIME_TO_SEC(TIMEDIFF(h.hora_fin, h.hora_inicio))) / 3600 AS horas
FROM horarios h JOIN clases c ON c.idclases = h.clases_idclases JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
WHERE c.activa = 1 AND c.vigente_desde <= @hasta AND c.vigente_hasta >= @desde
GROUP BY l.nombre_laboratorio ORDER BY l.nombre_laboratorio
'@ @{ '@desde' = $Desde.Date; '@hasta' = $Hasta.Date } $tx
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
    Paso '6. Usuarios de los maestros'
    $lista = Join-Path $CarpetaRespaldo ('maestros-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.csv')
    $lineas = New-Object Collections.Generic.List[string]
    $lineas.Add('Maestro;Usuario (numero de empleado);Contrasena inicial;Codigo de clase')
    foreach ($p in ($profesPlan.Values | Sort-Object Paterno, Materno, Nombre)) {
        $contrasena = if ($p.Nuevo) { $p.Contrasena } else { '(la que ya tenia)' }
        $lineas.Add(('{0} {1} {2};{3};{4};{5}' -f $p.Nombre, $p.Paterno, $p.Materno, $p.Numero, $contrasena, $p.Codigo))
    }
    # Con BOM: Excel lo abre con los acentos bien.
    [IO.File]::WriteAllLines($lista, $lineas, (New-Object Text.UTF8Encoding $true))
    Write-Host "   $lista" -ForegroundColor Green
    Write-Host '   Tiene contrasenas: repartelas en persona y no la subas a git (respaldos\ esta excluida).' -ForegroundColor Yellow
}
