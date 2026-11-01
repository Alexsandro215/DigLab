<#
.SYNOPSIS
    Prueba la regla de los choques de horario (decision del 2026-10-31) y el
    reemplazo en la preparacion de Proximo ciclo, contra una base de
    DESARROLLO (MySQL 8 o MariaDB 5.5).

.DESCRIPTION
    Un choque es la misma persona docente o el mismo grupo a la misma hora en
    DOS laboratorios distintos. Dos clases a la vez en el MISMO laboratorio no
    son un choque: al poner una encima de otra, la reemplaza (en Horario
    semanal con db/25, deploy\probar_falta_asignar.ps1; en la preparacion,
    aqui). Si llegan asi por datos viejos o copiados, "se enciman" y no dejan
    aplicar.

    Carga DigBit.exe por reflexion y recorre:
      - en memoria, sin la base: RevisionCiclo.Choques/Encimadas/Revisar
        (mismo docente en otro laboratorio = choque, mismo grupo en otro
        laboratorio = choque, los dos, mismo laboratorio = no es choque sino
        encimada), los textos de los motivos y de los avisos (DescribirPara,
        ChoquesSi, el filtro); ChoqueEnUso de Horario semanal (pastilla, lista
        y rojo de la cuadricula), con la vigencia de cada franja; el titulo de
        los avisos de ValidarFranja (TextosDelHorario.TituloDeCruce);
      - en la base: ValidarFranja sigue sin dejar guardar dos clases
        encimadas en el mismo laboratorio (sin llamarlo "choque") y lo de la
        misma persona docente en otro laboratorio; el horario en uso de la
        semana con ChoqueEnUso (cuantos, y que ninguno es del mismo
        laboratorio);
      - la preparacion: reemplazo parcial (en medio: se parte en dos),
        completo y la que se queda "Sin horario", con PonerHoras, CambiarFranja,
        AgregarClase y CambiarClase; la vista previa (RevisionCiclo.Reemplazos,
        SeQuedanSinHoras) y el texto de la confirmacion; que la misma persona
        docente o el mismo grupo en otro laboratorio no se reemplaza (se guarda
        y sale como choque);
      - antes de aplicar: dos clases encimadas en el mismo laboratorio (puestas
        a mano, como datos viejos) salen en rojo con su texto propio ("Se
        enciman en el mismo laboratorio"), no como choque, y Aplicar no
        aplica; sin ellas, ese renglon desaparece.

    Todo lo que crea va en dos laboratorios 'PRUEBA CH 1' y 'PRUEBA CH 2', el
    domingo, con clases de codigo 'TSTCH%' y un horario en preparacion
    'PRUEBA CH ...', y se BORRA al terminar. Si la base ya tiene un horario en
    preparacion o una aplicada por empezar, no hace nada (preparar los
    descartaria): usala en una COPIA (scratchpad\falta_asignar\copiar_base.ps1)
    sin ellos. Necesita db/16 y db/23, y tres docentes y tres grupos sin clases
    el domingo; las cuatro materias 'PRUEBA CH M1..4' las pone y las quita.
    MODIFICA la base: no lo ejecutes contra la base real del TESCHI.

.EXAMPLE
    .\probar_choques.ps1 -CadenaConexion 'Database=teschi_fa_8_ch;Server=127.0.0.1;Port=3317;User Id=root;Password='
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

    # Cuantos choques debe tener el horario en uso esta semana (-1: no se mira).
    [int]$ChoquesDeLaSemana = -1
)

$ErrorActionPreference = 'Stop'
if ($CadenaConexion -match 'teschi\.edu\.mx') { throw 'Esta prueba escribe en la base: nunca contra la de la escuela.' }
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot "..\DigBit\bin\$Configuration\DigBit.exe" }
$Exe = (Resolve-Path $Exe).Path
$script:carpeta = Split-Path $Exe

$env:DIGBIT_CONNECTION_STRING = $CadenaConexion
$env:DIGBIT_CARPETA_DATOS = Join-Path $env:TEMP 'digbit_probar_choques'
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
$tRevAp    = T 'DigBit.conexion.RevisionAplicacion'
$tFuente   = T 'DigBit.conexion.FuentePreparacion'
$tFranja   = T 'DigBit.conexion.FranjaCiclo'
$tCarga    = T 'DigBit.conexion.CargaCiclo'
$tLeido    = T 'DigBit.conexion.CicloLeido'
$tFiltro   = T 'DigBit.conexion.FiltroClases'
$tFI       = T 'DigBit.conexion.FranjaInfo'
$tH        = T 'DigBit.conexion.HorariosDatos'
$tUsuario  = T 'DigBit.conexion.Datos_User'
$tEnUso    = T 'DigBit.ChoqueEnUso'
$tConfirma = T 'DigBit.ConfirmarReemplazoEnPreparacion'
$tTextos   = T 'DigBit.TextosDelHorario'
$datos = [Activator]::CreateInstance($tDatos, $true)
$h = [Activator]::CreateInstance($tH, $true)
$publico = [Reflection.BindingFlags]'Public,Instance'

$script:total = 0
$script:malos = 0
function Ver([string]$que, [bool]$bien, [string]$detalle = '') {
    $script:total++
    if (-not $bien) { $script:malos++ }
    $texto = $(if ($bien) { '[OK ] ' } else { '[FALLO] ' }) + $que + $(if ($detalle) { ' (' + $detalle + ')' } else { '' })
    Write-Host $texto -ForegroundColor $(if ($bien) { 'Gray' } else { 'Red' })
}

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
function Invocar($metodo, $objeto, [object[]]$argumentos) { $metodo.Invoke($objeto, (Desnudos $argumentos)) }
# Con la coma: la lista (o el diccionario) que devuelve el metodo llega entera, para pasarla a otro.
function InvocarLista($metodo, $objeto, [object[]]$argumentos) { $r = $metodo.Invoke($objeto, (Desnudos $argumentos)); return ,$r }
function Estatico($tipo, [string]$metodo) {
    $m = $tipo.GetMethod($metodo, $bf)
    if ($m -eq $null) { throw "No encuentro $($tipo.Name).$metodo" }
    return $m
}

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
function Lista([int[]]$valores) {
    $l = New-Object 'System.Collections.Generic.List[int]'
    foreach ($v in $valores) { $l.Add($v) }
    return ,$l
}
function ListaDe($tipo) { return ,([Activator]::CreateInstance([System.Collections.Generic.List`1].MakeGenericType($tipo))) }
# Una FranjaCiclo; Franjas: @(@(lab, dia, 'HH:mm', 'HH:mm'), ...).
function FCiclo([int]$id, [int]$lab, [string]$nombreLab, [int]$dia, [string]$ini, [string]$fin) {
    $x = [Activator]::CreateInstance($tFranja)
    $x.Id = $id; $x.LaboratorioId = $lab; $x.Laboratorio = $nombreLab; $x.DiaSemana = $dia; $x.Inicio = [TimeSpan]$ini; $x.Fin = [TimeSpan]$fin
    return $x
}
function Franjas([object[]]$filas) {
    if ($filas.Count -gt 0 -and -not ($filas[0] -is [Array])) { $filas = @(,$filas) }
    $l = ListaDe $tFranja
    foreach ($f in $filas) { $l.Add((FCiclo 0 ([int]$f[0]) '' ([int]$f[1]) $f[2] $f[3])) }
    return ,$l
}
function Revisar($leido) { Invocar (Estatico $tRevision 'Revisar') $null @($leido) }
function Leer([int]$id) { (Llamar 'Leer' @($id)).Valor }
# Las franjas de una clase del horario en preparacion: 'lab:dia HH:mm-HH:mm; ...'.
function FranjasDe([int]$carga) {
    (@((Sql "SELECT laboratorio, dia_semana, TIME_FORMAT(hora_inicio, '%H:%i') AS i, TIME_FORMAT(hora_fin, '%H:%i') AS f FROM ciclo_franjas WHERE idcarga = $carga ORDER BY dia_semana, hora_inicio").Rows |
        ForEach-Object { "$($_['laboratorio']):$($_['dia_semana']) $($_['i'])-$($_['f'])" })) -join '; '
}

# =============================================================================
Write-Host "`n== 1. La regla, en memoria ==" -ForegroundColor Cyan
# =============================================================================
function Carga([int]$id, $profesor, [int]$grupo, [string]$materia, [string]$nombreGrupo, [object[]]$franjas) {
    $c = [Activator]::CreateInstance($tCarga)
    $c.Id = $id
    if ($profesor -ne $null) { $c.ProfesorId = [int]$profesor; $c.ProfesorCorto = "Docente $profesor"; $c.Profesor = "Docente $profesor" }
    $c.GrupoId = $grupo; $c.Grupo = $nombreGrupo; $c.Materia = $materia; $c.MateriaId = $id
    foreach ($f in $franjas) { $f.CargaId = $id; $c.Franjas.Add($f) }
    return $c
}
# Lunes: A (docente 1, grupo 10) en Lab Uno; B, la misma docente en Lab Dos; C, el
# mismo grupo en Lab Dos; D, otra en el MISMO laboratorio que A. Martes: E y F,
# misma docente y mismo grupo en Lab Tres y Lab Cuatro. Miercoles: G y H, dos
# docentes distintos en el mismo laboratorio.
$A = Carga 1 1 10 'Redes' 'G10' @((FCiclo 11 1 'Lab Uno' 1 '08:00' '10:00'))
$B = Carga 2 1 20 'Bases' 'G20' @((FCiclo 21 2 'Lab Dos' 1 '09:00' '11:00'))
$C = Carga 3 2 10 'Mate' 'G10' @((FCiclo 31 2 'Lab Dos' 1 '08:00' '09:00'))
$D = Carga 4 3 30 'Fisica' 'G30' @((FCiclo 41 1 'Lab Uno' 1 '09:00' '10:00'))
$E = Carga 5 4 40 'Quimica' 'G40' @((FCiclo 51 3 'Lab Tres' 2 '08:00' '09:00'))
$F = Carga 6 4 40 'Ingles' 'G40' @((FCiclo 61 4 'CISCO' 2 '08:00' '09:00'))
$G = Carga 7 5 50 'Etica' 'G50' @((FCiclo 71 1 'Lab Uno' 3 '08:00' '09:00'))
$Hc = Carga 8 6 51 'Dibujo' 'G51' @((FCiclo 81 1 'Lab Uno' 3 '08:30' '09:30'))
$todas = ListaDe $tCarga
foreach ($x in @($A, $B, $C, $D, $E, $F, $G, $Hc)) { $todas.Add($x) }
$sinProgramadas = ListaDe $tFI
$choques = @(Invocar (Estatico $tRevision 'Choques') $null @($todas, $sinProgramadas))
$encimadas = @(Invocar (Estatico $tRevision 'Encimadas') $null @($todas, $sinProgramadas))
function Par($lista, $x, $y) { @($lista | Where-Object { ($_.CargaA.Id -eq $x.Id -and $_.CargaB.Id -eq $y.Id) -or ($_.CargaA.Id -eq $y.Id -and $_.CargaB.Id -eq $x.Id) })[0] }
$ab = Par $choques $A $B; $ac = Par $choques $A $C; $ef = Par $choques $E $F
Ver "Choques: 3 (A-B, A-C y E-F); son $($choques.Count)" ($choques.Count -eq 3 -and $ab -ne $null -and $ac -ne $null -and $ef -ne $null)
Ver 'mismo docente en otro laboratorio = choque (misma persona docente)' ($ab -ne $null -and $ab.Motivos -eq 'misma persona docente' -and -not $ab.MismoLaboratorio) "$($ab.Motivos)"
Ver 'mismo grupo en otro laboratorio = choque (mismo grupo)' ($ac -ne $null -and $ac.Motivos -eq 'mismo grupo' -and -not $ac.MismoLaboratorio) "$($ac.Motivos)"
Ver 'los dos: misma persona docente y mismo grupo' ($ef -ne $null -and $ef.Motivos -eq 'misma persona docente y mismo grupo') "$($ef.Motivos)"
Ver 'mismo laboratorio = no es choque (ni A-D ni G-H)' ((Par $choques $A $D) -eq $null -and (Par $choques $G $Hc) -eq $null)
$ad = Par $encimadas $A $D
Ver "... sino encimadas: A-D y G-H ($($encimadas.Count))" ($encimadas.Count -eq 2 -and $ad -ne $null -and (Par $encimadas $G $Hc) -ne $null -and $ad.MismoLaboratorio)
Ver 'ningun motivo dice "mismo laboratorio"' (@($choques + $encimadas | Where-Object { $_.Motivos -match 'laboratorio' }).Count -eq 0)
$tAB = $ab.DescribirPara($A)
Ver 'texto del choque (desde A)' ($tAB -ceq 'Choca con Bases con G20 (Docente 1) en el Lab Dos el Lunes de 09:00 a 10:00: misma persona docente.') $tAB
$tCA = $ac.DescribirPara($C)
Ver 'texto del choque (desde C: el laboratorio de la otra)' ($tCA -ceq 'Choca con Redes con G10 (Docente 1) en el Lab Uno el Lunes de 08:00 a 09:00: mismo grupo.') $tCA
$tFE = $ef.DescribirPara($F)
Ver 'texto con los dos motivos (desde F: "en el Lab Tres")' ($tFE -match '^Choca con Quimica con G40 \(Docente 4\) en el Lab Tres el Martes de 08:00 a 09:00: misma persona docente y mismo grupo\.$') $tFE
$tEF = $ef.DescribirPara($E)
Ver '... y desde E: "en el Laboratorio CISCO"' ($tEF -match ' en el Laboratorio CISCO el Martes') $tEF
$tAD = $ad.DescribirPara($A)
Ver 'texto de la encimada' ($tAD -match '^Se encima con Fisica con G30 \(Docente 3\) el Lunes de 09:00 a 10:00 en el mismo laboratorio: qu.tale esas horas a una de las dos\.$') $tAD

$leidoM = [Activator]::CreateInstance($tLeido)
foreach ($x in $todas) { $leidoM.Cargas.Add($x) }
$revM = Revisar $leidoM
Ver "Revisar: 3 choques y 2 encimadas ($($revM.Choques.Count) y $($revM.Encimadas.Count))" ($revM.Choques.Count -eq 3 -and $revM.Encimadas.Count -eq 2)
$avD = @($revM.AvisosDe($D) | ForEach-Object { $_ })
Ver 'D: un aviso rojo, el de la encimada (no de choque)' ($avD.Count -eq 1 -and $avD[0].Bloquea -and $avD[0].Texto -match '^Se encima con Redes') "$($avD | ForEach-Object { $_.Texto })"
$avA = @($revM.AvisosDe($A) | ForEach-Object { $_.Texto })
Ver 'A: dos choques y una encimada' (@($avA | Where-Object { $_ -match '^Choca con' }).Count -eq 2 -and @($avA | Where-Object { $_ -match '^Se encima con' }).Count -eq 1) ($avA -join ' | ')
$filtroEnc = [Enum]::Parse($tFiltro, 'Encimadas')
$filtroCho = [Enum]::Parse($tFiltro, 'Choques')
$mFiltro = Estatico $tRevision 'EntraEnFiltro'
Ver 'filtro Encimadas: D si, B no; filtro Choques: B si, D no' ((Invocar $mFiltro $null @($leidoM, $revM, $D, $filtroEnc)) -and -not (Invocar $mFiltro $null @($leidoM, $revM, $B, $filtroEnc)) -and
    (Invocar $mFiltro $null @($leidoM, $revM, $B, $filtroCho)) -and -not (Invocar $mFiltro $null @($leidoM, $revM, $D, $filtroCho)))
Ver "TextoFiltro(Encimadas) = 'Encimadas en un laboratorio'" ("$(Invocar (Estatico $tRevision 'TextoFiltro') $null @($filtroEnc))" -eq 'Encimadas en un laboratorio')
# ChoquesSi: lo de los dialogos de la preparacion.
$sinNada = Lista @()
$si = @(Invocar (Estatico $tRevision 'ChoquesSi') $null @($leidoM, [Nullable[int]]1, 99, (Franjas @(@(1, 1, '08:00', '09:00'))), $sinNada, $null))
Ver 'ChoquesSi: la misma docente que A en el MISMO laboratorio no es choque' ($si.Count -eq 0) ($si -join ' | ')
$si = @(Invocar (Estatico $tRevision 'ChoquesSi') $null @($leidoM, [Nullable[int]]1, 99, (Franjas @(@(3, 1, '08:30', '09:30'))), $sinNada, $null))
Ver 'ChoquesSi: en otro laboratorio si (con A y con B), con el laboratorio de la otra' ($si.Count -eq 2 -and @($si | Where-Object { $_ -match '^Lunes de 08:30 a 09:30: choca con Redes con G10 \(Docente 1\) en el Lab Uno, misma persona docente\.$' }).Count -eq 1 -and
    @($si | Where-Object { $_ -match 'choca con Bases con G20 \(Docente 1\) en el Lab Dos, misma persona docente\.$' }).Count -eq 1) ($si -join ' | ')

Write-Host "`n-- Horario semanal: ChoqueEnUso --" -ForegroundColor DarkCyan
$lunes = [DateTime]'2026-10-26'
function FI([int]$id, [int]$lab, [int]$dia, [string]$ini, [string]$fin, [int]$prof, [int]$grupo, [DateTime]$desde, [DateTime]$hasta) {
    $f = [Activator]::CreateInstance($tFI)
    $f.Id = $id; $f.ClaseId = $id; $f.LaboratorioId = $lab; $f.Laboratorio = "L$lab"; $f.DiaSemana = $dia; $f.Inicio = [TimeSpan]$ini; $f.Fin = [TimeSpan]$fin
    $f.ProfesorId = $prof; $f.Profesor = "P$prof"; $f.ProfesorCorto = "P$prof"; $f.GrupoId = $grupo; $f.Grupo = "G$grupo"; $f.Materia = "M$id"
    $f.Activa = $true; $f.VigenteDesde = $desde; $f.VigenteHasta = $hasta
    return $f
}
$d0 = [DateTime]'2026-08-01'; $d1 = [DateTime]'2027-01-31'
$fis = ListaDe $tFI
$fis.Add((FI 1 1 1 '08:00' '10:00' 1 10 $d0 $d1))   # a
$fis.Add((FI 2 2 1 '09:00' '11:00' 1 20 $d0 $d1))   # b: misma docente, otro laboratorio
$fis.Add((FI 3 2 1 '08:00' '09:00' 2 10 $d0 $d1))   # c: mismo grupo, otro laboratorio
$fis.Add((FI 4 1 1 '09:00' '10:00' 3 30 $d0 $d1))   # d: mismo laboratorio que a
$fis.Add((FI 5 3 2 '08:00' '09:00' 4 40 $d0 $d1))   # e
$fis.Add((FI 6 4 2 '08:00' '09:00' 4 40 $d0 $d1))   # f: misma docente y grupo que e
$fis.Add((FI 7 3 3 '08:00' '09:00' 5 50 $d0 $lunes.AddDays(1)))   # g: termino el martes
$fis.Add((FI 8 4 3 '08:00' '09:00' 5 50 $lunes.AddDays(2) $d1))   # h: la sustituye desde el miercoles
$fis.Add((FI 9 1 4 '08:00' '09:00' 6 60 $d0 $d1))   # i y j: misma docente, mismo laboratorio
$fis.Add((FI 10 1 4 '08:00' '09:00' 6 61 $d0 $d1))
$enUsoL = InvocarLista (Estatico $tEnUso 'DeLaSemana') $null @($fis, $lunes)
$enUso = @($enUsoL)
Ver "ChoqueEnUso: 3 (a-b, a-c y e-f); son $($enUso.Count)" ($enUso.Count -eq 3) (($enUso | ForEach-Object { "$($_.A.Id)-$($_.B.Id): $($_.Motivos)" }) -join ' | ')
Ver '... ninguno en el mismo laboratorio (ni a-d ni i-j), ni g-h (no se dan a la vez)' (@($enUso | Where-Object { $_.A.LaboratorioId -eq $_.B.LaboratorioId -or $_.A.Id -eq 7 -or $_.A.Id -eq 9 }).Count -eq 0)
$mot = @{}; foreach ($x in $enUso) { $mot["$($x.A.Id)-$($x.B.Id)"] = $x.Motivos }
Ver 'motivos: a-b misma persona docente, a-c mismo grupo, e-f los dos' ($mot['1-2'] -eq 'misma persona docente' -and $mot['1-3'] -eq 'mismo grupo' -and $mot['5-6'] -eq 'misma persona docente y mismo grupo') (($mot.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')
$porFranja = InvocarLista (Estatico $tEnUso 'MotivosPorFranja') $null @(,$enUsoL)
Ver 'el rojo de la cuadricula: a "misma persona docente y mismo grupo", d sin rojo' ($porFranja[1] -eq 'misma persona docente y mismo grupo' -and -not $porFranja.ContainsKey(4) -and -not $porFranja.ContainsKey(9)) (($porFranja.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')

Write-Host "`n-- Titulo de los avisos de ValidarFranja --" -ForegroundColor DarkCyan
$mTitulo = Estatico $tTextos 'TituloDeCruce'
Ver 'mismo laboratorio: "Laboratorio ocupado"' ((Invocar $mTitulo $null @('Se solapa con Redes - 5A (ABC12) de 08:00 a 10:00 en Lab 1: mismo laboratorio.')) -eq 'Laboratorio ocupado')
Ver 'mismo laboratorio y docente: tambien "Laboratorio ocupado"' ((Invocar $mTitulo $null @('Se solapa con Redes - 5A (ABC12) de 08:00 a 10:00 en Lab 1: mismo laboratorio, misma persona docente.')) -eq 'Laboratorio ocupado')
Ver 'docente en otro laboratorio: "Choque de horario"' ((Invocar $mTitulo $null @('Se solapa con Redes - 5A (ABC12) de 08:00 a 10:00 en Lab 2: misma persona docente.')) -eq 'Choque de horario')

# =============================================================================
# En la base
# =============================================================================
foreach ($t in 'ciclo_cargas', 'ciclo_franjas') {
    if ("$(Uno "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '$t'")" -ne '1') { throw "La base no tiene $t (db/16): aplicala antes." }
}
if (-not (Existe 'ciclo_cargas' 'carga_origen')) { throw 'La base no tiene db/23_horarios_guardados.sql: aplicala antes.' }
$ajenos = [int](Uno "SELECT COUNT(*) FROM ciclos_preliminares WHERE (estado = 'borrador' OR (estado = 'publicado' AND desde > CURDATE())) AND nombre NOT LIKE 'PRUEBA CH%'")
if ($ajenos -gt 0) { throw 'La base ya tiene un horario en preparacion (o una aplicada por empezar) que no es de esta prueba: usala en una copia sin ellos. No se hizo nada.' }

$tUsuario.GetMethod('SetUser').Invoke($null, [object[]]@($Administrador)) | Out-Null
$tUsuario.GetProperty('TipoUsuario', $bf).SetValue($null, 3)
$idAdmin = [int]("$(Uno "SELECT idusuarios FROM usuarios WHERE numero_identificador = '$Administrador'")" -replace '^$', '0')
$hoy = [DateTime](Uno 'SELECT CURDATE()')
$conActivoUsuario = Existe 'usuarios' 'activo'
$conVigencia = Existe 'horarios' 'vigente_desde'

function Limpiar() {
    $null = Sql "DELETE FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA CH%'"
    $null = Sql "DELETE h FROM horarios h INNER JOIN clases c ON c.idclases = h.clases_idclases WHERE c.codigo LIKE 'TSTCH%'"
    $null = Sql "DELETE FROM clases WHERE codigo LIKE 'TSTCH%'"
    $null = Sql "DELETE h FROM horarios h INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios WHERE l.nombre_laboratorio LIKE 'PRUEBA CH%'"
    $null = Sql "DELETE FROM laboratorios WHERE nombre_laboratorio LIKE 'PRUEBA CH%'"
    $null = Sql "DELETE FROM materias WHERE nombre_materia LIKE 'PRUEBA CH M%'"
}
Limpiar

# Tres docentes (activos) y tres grupos sin clases el domingo, y cuatro materias
# de la prueba (asi ninguna clase "ya esta en Clases").
$sinDomingo = "NOT EXISTS (SELECT 1 FROM clases c JOIN horarios hh ON hh.clases_idclases = c.idclases WHERE hh.dia_semana = 7 AND c.{0} = {1})"
$activo = $(if ($conActivoUsuario) { ' AND u.activo = 1' } else { '' })
$docentes = @((Sql ("SELECT u.idusuarios FROM usuarios u WHERE u.fk_tipo_usuario = 2$activo AND " + ($sinDomingo -f 'usuarios_idusuarios', 'u.idusuarios') + " ORDER BY u.idusuarios LIMIT 3")).Rows | ForEach-Object { [int]$_[0] })
$grupos = @((Sql ("SELECT g.idgrupos FROM grupos g WHERE " + ($sinDomingo -f 'grupos_idgrupos', 'g.idgrupos') + " ORDER BY g.idgrupos LIMIT 3")).Rows | ForEach-Object { [int]$_[0] })
if ($docentes.Count -lt 3 -or $grupos.Count -lt 3) { throw 'Hacen falta tres docentes y tres grupos sin clases el domingo.' }
$null = Sql "INSERT INTO materias (nombre_materia) VALUES ('PRUEBA CH M1'), ('PRUEBA CH M2'), ('PRUEBA CH M3'), ('PRUEBA CH M4')"
$materias = @((Sql "SELECT id_materia FROM materias WHERE nombre_materia LIKE 'PRUEBA CH M%' ORDER BY nombre_materia").Rows | ForEach-Object { [int]$_[0] })
$p1, $p2, $p3 = $docentes; $g1, $g2, $g3 = $grupos; $m1, $m2, $m3, $m4 = $materias
$null = Sql "INSERT INTO laboratorios (nombre_laboratorio) VALUES ('PRUEBA CH 1'), ('PRUEBA CH 2')"
$lab1 = [int](Uno "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio = 'PRUEBA CH 1'")
$lab2 = [int](Uno "SELECT idlaboratorios FROM laboratorios WHERE nombre_laboratorio = 'PRUEBA CH 2'")
Write-Host "Hoy segun el servidor: $($hoy.ToString('yyyy-MM-dd')); laboratorios $lab1 y $lab2; docentes $($docentes -join ','); grupos $($grupos -join ','); administracion: $idAdmin"

try {
    # =========================================================================
    Write-Host "`n== 2. Horario semanal: ValidarFranja y la semana ==" -ForegroundColor Cyan
    # =========================================================================
    function Clase([string]$codigo, [int]$docente, [int]$grupo, [int]$materia) {
        $null = Sql ("INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en) " +
            "VALUES ($docente, $grupo, $materia, '$codigo', CURDATE() - INTERVAL 30 DAY, CURDATE() + INTERVAL 30 DAY, 1, NULL, NOW())")
        return [int](Uno "SELECT idclases FROM clases WHERE codigo = '$codigo'")
    }
    $cX = Clase 'TSTCH_X' $p1 $g1 $m1
    $cY = Clase 'TSTCH_Y' $p2 $g2 $m2
    $cZ = Clase 'TSTCH_Z' $p1 $g3 $m3
    $null = Sql "INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES ($cX, $lab1, 7, '08:00', '10:00')"
    $mValidar = $tH.GetMethod('ValidarFranja')
    $v = "$($mValidar.Invoke($h, [object[]]@([Nullable[int]]$null, $cY, $lab1, 7, [TimeSpan]'09:00', [TimeSpan]'11:00')))"
    Ver 'ValidarFranja: otra clase en el mismo laboratorio sigue sin poder guardarse encimada' ($v -match '^Se solapa con .* en PRUEBA CH 1: mismo laboratorio\.$') $v
    Ver '... sin decir "choque" (titulo "Laboratorio ocupado")' ($v -notmatch 'hoque' -and (Invocar $mTitulo $null @($v)) -eq 'Laboratorio ocupado')
    $v = "$($mValidar.Invoke($h, [object[]]@([Nullable[int]]$null, $cZ, $lab2, 7, [TimeSpan]'09:00', [TimeSpan]'11:00')))"
    Ver 'ValidarFranja: la misma docente en otro laboratorio tampoco (choque)' ($v -match 'en PRUEBA CH 1: misma persona docente\.$' -and (Invocar $mTitulo $null @($v)) -eq 'Choque de horario') $v
    $v = "$($mValidar.Invoke($h, [object[]]@([Nullable[int]]$null, $cY, $lab2, 7, [TimeSpan]'09:00', [TimeSpan]'11:00')))"
    Ver 'ValidarFranja: otra docente y otro grupo en otro laboratorio, si' ($v -eq '') $v

    # La semana de hoy con la regla nueva, en todos los laboratorios activos (como CargarFranjas).
    $todasFr = $tH.GetMethod('ObtenerTodasLasFranjas').Invoke($h, [object[]]@())
    $condLab = $(if (Existe 'laboratorios' 'activo') { 'WHERE activo = 1' } else { '' })
    $activos = @((Sql "SELECT idlaboratorios FROM laboratorios $condLab").Rows | ForEach-Object { [int]$_[0] })
    $enLabs = ListaDe $tFI
    foreach ($x in $todasFr) { if ($activos -contains $x.LaboratorioId -and $x.ClaseId -ne $cX) { $enLabs.Add($x) } }
    $lunesHoy = $hoy.AddDays(-(([int]$hoy.DayOfWeek + 6) % 7))
    $semana = @(Invocar (Estatico $tEnUso 'DeLaSemana') $null @($enLabs, $lunesHoy))
    $mismoLab = @($semana | Where-Object { $_.A.LaboratorioId -eq $_.B.LaboratorioId }).Count
    $porMotivo = ($semana | Group-Object Motivos | ForEach-Object { "$($_.Count) $($_.Name)" }) -join ', '
    Ver "horario en uso, semana del $($lunesHoy.ToString('dd/MM')): $($semana.Count) choques ($porMotivo), ninguno del mismo laboratorio" ($mismoLab -eq 0 -and ($ChoquesDeLaSemana -lt 0 -or $semana.Count -eq $ChoquesDeLaSemana))

    # =========================================================================
    Write-Host "`n== 3. La preparacion: reemplazar ==" -ForegroundColor Cyan
    # =========================================================================
    $r = Llamar 'Preparar' @('PRUEBA CH preparacion', ($tFuente.GetMethod('EnBlanco').Invoke($null, [object[]]@())), $false, $idAdmin)
    if (-not (Bien 'Preparar en blanco' $r)) { throw 'Sin el horario en preparacion no se puede seguir.' }
    $b = $r.Valor.CicloId
    $r = Llamar 'AgregarClase' @($b, $p1, $g1, $m1, (Franjas @(@($lab1, 7, '08:00', '12:00'))), $idAdmin)
    if (-not (Bien 'A en PRUEBA CH 1, domingo de 08:00 a 12:00' $r)) { throw 'No se pudo poner A.' }
    $a = [int]$r.Valor
    $r = Llamar 'AgregarClase' @($b, $p2, $g2, $m2, (Franjas @()), $idAdmin)
    if (-not (Bien 'B, sin horas' $r)) { throw 'No se pudo poner B.' }
    $bb = [int]$r.Valor

    # Vista previa y texto de la confirmacion (lo que dicen los dialogos).
    $leido = Leer $b
    $nuevas = Franjas @(@($lab1, 7, '09:00', '10:00'))
    $nuevas[0].Laboratorio = 'PRUEBA CH 1'
    $planL = InvocarLista (Estatico $tRevision 'Reemplazos') $null @($leido, $bb, $nuevas, $null)
    $plan = @($planL)
    Ver 'vista previa: a A se le quitaria 09:00-10:00' ($plan.Count -eq 1 -and $plan[0].ClaseId -eq $a -and $plan[0].QuitaInicio -eq [TimeSpan]'09:00' -and $plan[0].QuitaFin -eq [TimeSpan]'10:00')
    $sinHorasL = InvocarLista (Estatico $tRevision 'SeQuedanSinHoras') $null @($leido, $nuevas, $planL)
    $sinHoras = @($sinHorasL)
    Ver '... y A no se queda sin horas' ($sinHoras.Count -eq 0)
    $texto = "$(Invocar (Estatico $tConfirma 'Texto') $null @($planL, $sinHorasL))"
    $cargaA = $leido.Carga($a)
    $esperado = "Esto quita a $($cargaA.Materia), $($cargaA.Grupo) ($($cargaA.ProfesorCorto)) del domingo 09:00" + [char]0x2013 + "10:00 (1 h) de este horario en preparaci" + [char]0xF3 + "n."
    Ver 'texto de la confirmacion, como el de Horario semanal y sin "Falta asignar"' ($texto.StartsWith($esperado) -and $texto -notmatch 'Falta asignar' -and $texto -match 'Lo que se usa hoy no cambia\.$') $texto

    Bien 'PonerHoras a B encima de A (09:00-10:00)' (Llamar 'PonerHoras' @($bb, $nuevas, $idAdmin)) | Out-Null
    Ver 'reemplazo parcial: A queda en 08:00-09:00 y 10:00-12:00 (se parte en dos)' ((FranjasDe $a) -eq "${lab1}:7 08:00-09:00; ${lab1}:7 10:00-12:00") (FranjasDe $a)
    Ver '... y B tiene 09:00-10:00' ((FranjasDe $bb) -eq "${lab1}:7 09:00-10:00") (FranjasDe $bb)
    $rev = Revisar (Leer $b)
    Ver '... sin choques ni encimadas' ($rev.Choques.Count -eq 0 -and $rev.Encimadas.Count -eq 0)

    # CambiarFranja: la de B pasa a 08:00-12:00 y tapa todo lo de A.
    $fB = [int](Uno "SELECT idfranja FROM ciclo_franjas WHERE idcarga = $bb")
    $leido = Leer $b
    $nueva = FCiclo 0 $lab1 'PRUEBA CH 1' 7 '08:00' '12:00'
    $lNueva = ListaDe $tFranja; $lNueva.Add($nueva)
    $planL = InvocarLista (Estatico $tRevision 'Reemplazos') $null @($leido, $bb, $lNueva, (Lista @($fB)))
    $plan = @($planL)
    $sinHorasL = InvocarLista (Estatico $tRevision 'SeQuedanSinHoras') $null @($leido, $lNueva, $planL)
    $sinHoras = @($sinHorasL)
    Ver 'vista previa de Cambiar horas: le quita las dos de A y A se queda sin horas' ($plan.Count -eq 2 -and $sinHoras.Count -eq 1 -and $sinHoras[0].Id -eq $a)
    $texto = "$(Invocar (Estatico $tConfirma 'Texto') $null @($planL, $sinHorasL))"
    Ver '... el texto: la lista y "se queda sin horas: sale en Sin horario"' ($texto -match '^Esto quita de este horario en preparaci.n:' -and $texto -match "$([regex]::Escape($cargaA.Nombre)) se queda sin horas: sale en .Sin horario.\.") $texto
    Bien 'CambiarFranja de B a 08:00-12:00' (Llamar 'CambiarFranja' @($fB, $bb, $nueva, $idAdmin)) | Out-Null
    Ver 'reemplazo completo: A se queda sin horas' ((FranjasDe $a) -eq '') (FranjasDe $a)
    Ver '... B conserva su franja (mismo id) con 08:00-12:00' ((FranjasDe $bb) -eq "${lab1}:7 08:00-12:00" -and "$(Uno "SELECT idfranja FROM ciclo_franjas WHERE idcarga = $bb")" -eq "$fB") (FranjasDe $bb)
    $rev = Revisar (Leer $b)
    Ver '... A sale en "Sin horario"' ($rev.SinHorario -ge 1 -and (Invocar $mFiltro $null @((Leer $b), $rev, ((Leer $b).Carga($a)), [Enum]::Parse($tFiltro, 'SinHorario'))))

    # La misma persona docente (p2, la de B) o el mismo grupo (g2) en OTRO laboratorio: no se reemplaza, choca.
    $r = Llamar 'AgregarClase' @($b, $p2, $g3, $m3, (Franjas @(@($lab2, 7, '09:00', '10:00'))), $idAdmin)
    Bien 'C: la docente de B en PRUEBA CH 2 a la misma hora' $r | Out-Null
    $cc = [int]$r.Valor
    $r = Llamar 'AgregarClase' @($b, $p3, $g2, $m4, (Franjas @(@($lab2, 7, '11:00', '12:00'))), $idAdmin)
    Bien 'D: el grupo de B en PRUEBA CH 2 a la misma hora' $r | Out-Null
    $dd = [int]$r.Valor
    Ver '... a B no se le quito nada' ((FranjasDe $bb) -eq "${lab1}:7 08:00-12:00") (FranjasDe $bb)
    Ver '... C y D tienen sus horas' ((FranjasDe $cc) -eq "${lab2}:7 09:00-10:00" -and (FranjasDe $dd) -eq "${lab2}:7 11:00-12:00")
    $leido = Leer $b
    $rev = Revisar $leido
    $chBC = @($rev.Choques | Where-Object { $_.Toca($leido.Carga($cc)) })
    $chBD = @($rev.Choques | Where-Object { $_.Toca($leido.Carga($dd)) })
    Ver "... y salen como choques: B-C misma persona docente, B-D mismo grupo ($($rev.Choques.Count))" ($rev.Choques.Count -eq 2 -and $chBC.Count -eq 1 -and $chBC[0].Motivos -eq 'misma persona docente' -and
        $chBD.Count -eq 1 -and $chBD[0].Motivos -eq 'mismo grupo' -and $rev.Encimadas.Count -eq 0) (($rev.Choques | ForEach-Object { $_.Motivos }) -join ' | ')
    $planOtro = @(Invocar (Estatico $tRevision 'Reemplazos') $null @($leido, $cc, (Franjas @(@($lab2, 7, '09:00', '10:00'))), $null))
    Ver '... la vista previa no reemplaza lo de otro laboratorio' ($planOtro.Count -eq 0)
    $avB = @($rev.AvisosDe($leido.Carga($bb)) | ForEach-Object { $_.Texto })
    Ver '... el aviso de B dice el laboratorio y el motivo' (@($avB | Where-Object { $_ -match '^Choca con .* en el Laboratorio PRUEBA CH 2 el Domingo de 09:00 a 10:00: misma persona docente\.$' }).Count -eq 1) ($avB -join ' | ')

    # AgregarClase y CambiarClase tambien reemplazan en su laboratorio.
    $r = Llamar 'AgregarClase' @($b, $p1, $g3, $m4, (Franjas @(@($lab2, 7, '09:30', '11:30'))), $idAdmin)
    Bien 'E con AgregarClase en PRUEBA CH 2 de 09:30 a 11:30 (encima de C y de D)' $r | Out-Null
    $ee = [int]$r.Valor
    Ver '... a C le queda 09:00-09:30 y a D 11:30-12:00' ((FranjasDe $cc) -eq "${lab2}:7 09:00-09:30" -and (FranjasDe $dd) -eq "${lab2}:7 11:30-12:00") "C: $(FranjasDe $cc) / D: $(FranjasDe $dd)"
    Bien 'CambiarClase de A: de vuelta en PRUEBA CH 1 de 11:00 a 12:00 (encima de B)' (Llamar 'CambiarClase' @($a, $p1, $g1, $m1, (Franjas @(@($lab1, 7, '11:00', '12:00'))), $idAdmin)) | Out-Null
    Ver '... a B le queda 08:00-11:00' ((FranjasDe $bb) -eq "${lab1}:7 08:00-11:00" -and (FranjasDe $a) -eq "${lab1}:7 11:00-12:00") "B: $(FranjasDe $bb) / A: $(FranjasDe $a)"
    $rev = Revisar (Leer $b)
    Ver '... y ninguna se encima con otra en su laboratorio' ($rev.Encimadas.Count -eq 0) "choques: $(($rev.Choques | ForEach-Object { $_.Motivos }) -join ' | ')"
    Ver '... el ciclo quedo firmado (cambiado_por)' ($idAdmin -eq 0 -or "$(Uno "SELECT cambiado_por FROM ciclos_preliminares WHERE idciclo = $b")" -eq "$idAdmin")

    # =========================================================================
    Write-Host "`n== 4. Antes de aplicar: encimadas en el mismo laboratorio ==" -ForegroundColor Cyan
    # =========================================================================
    # Sin choques (se quitan C, D y E) y con una franja puesta a mano encima de
    # otra en el mismo laboratorio, como datos viejos o copiados.
    Bien 'quitar C, D y E (los choques)' (Llamar 'QuitarClases' @((Lista @($cc, $dd, $ee)), $idAdmin)) | Out-Null
    $null = Sql "INSERT INTO ciclo_franjas (idcarga, laboratorio, dia_semana, hora_inicio, hora_fin) VALUES ($a, $lab1, 7, '10:00', '11:00')"
    $Desde = [DateTime]'2027-01-11'; $Hasta = [DateTime]'2027-06-30'; $Fin = $Desde.AddDays(-1)
    $dA = (Llamar 'LeerParaAplicar' @($b, $Desde, $Hasta, $Fin)).Valor
    $renglones = @(Invocar (Estatico $tRevAp 'Revisar') $null @($dA, $Desde, $Hasta, $Fin, 'PRUEBA CH aplicado'))
    $rojos = @($renglones | Where-Object { "$($_.Nivel)" -eq 'Rojo' })
    $enc = @($rojos | Where-Object { $_.Texto -match '^Se enciman en el mismo laboratorio: ' })
    Ver 'en rojo, con su texto propio' ($enc.Count -eq 1 -and $enc[0].Texto -match 'el Domingo de 10:00 a 11:00 en el Laboratorio PRUEBA CH 1\. Qu.tale esas horas a una de las dos\.$' -and "$($enc[0].Ver)" -eq 'Encimadas') "$($enc | ForEach-Object { $_.Texto })"
    Ver '... no como choque ("No hay choques." en verde)' ($dA.Revision.Choques.Count -eq 0 -and $dA.Revision.Encimadas.Count -eq 1 -and
        @($renglones | Where-Object { "$($_.Nivel)" -eq 'Verde' -and $_.Texto -eq 'No hay choques.' }).Count -eq 1) (($rojos | ForEach-Object { $_.Texto }) -join ' / ')
    Ver '... es lo unico que no deja aplicar' ($rojos.Count -eq 1) (($rojos | ForEach-Object { $_.Texto }) -join ' / ')
    $r = Llamar 'Aplicar' @($b, 'PRUEBA CH aplicado', $Desde, $Hasta, $Fin, 'PRUEBA CH no se guarda', $idAdmin)
    if ($r.Ok) { $null = Llamar 'Deshacer' @($b, $idAdmin) }
    Ver 'Aplicar no aplica' (-not $r.Ok -and $r.Error.GetType().Name -eq 'ErrorDeDatos' -and $r.Error.Message -match '^Se enciman en el mismo laboratorio') $(if ($r.Ok) { 'SE APLICO' } else { $r.Error.GetType().Name + ': ' + $r.Error.Message })
    Ver '... y el horario sigue en preparacion' ("$(Uno "SELECT estado FROM ciclos_preliminares WHERE idciclo = $b")" -eq 'borrador')
    $null = Sql "DELETE FROM ciclo_franjas WHERE idcarga = $a AND hora_inicio = '10:00:00'"
    $dA = (Llamar 'LeerParaAplicar' @($b, $Desde, $Hasta, $Fin)).Valor
    $renglones = @(Invocar (Estatico $tRevAp 'Revisar') $null @($dA, $Desde, $Hasta, $Fin, 'PRUEBA CH aplicado'))
    Ver 'sin la encimada, ese renglon desaparece y no queda nada en rojo' (@($renglones | Where-Object { "$($_.Nivel)" -eq 'Rojo' }).Count -eq 0) (($renglones | Where-Object { "$($_.Nivel)" -eq 'Rojo' } | ForEach-Object { $_.Texto }) -join ' / ')
}
finally {
    Limpiar
    Ver 'no quedan datos de la prueba' ("$(Uno "SELECT (SELECT COUNT(*) FROM ciclos_preliminares WHERE nombre LIKE 'PRUEBA CH%') + (SELECT COUNT(*) FROM clases WHERE codigo LIKE 'TSTCH%') + (SELECT COUNT(*) FROM laboratorios WHERE nombre_laboratorio LIKE 'PRUEBA CH%') + (SELECT COUNT(*) FROM materias WHERE nombre_materia LIKE 'PRUEBA CH M%')")" -eq '0')
}

Write-Host "`nResultado: $($script:total - $script:malos)/$($script:total) correctas" -ForegroundColor $(if ($script:malos -eq 0) { 'Green' } else { 'Red' })
if ($script:malos -gt 0) { exit 1 }
