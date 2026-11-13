<#
.SYNOPSIS
    Aplica las migraciones de DigLab sobre una base que ya existe, con respaldo
    y revision previa, y se niega a seguir si algo va a fallar.

.DESCRIPTION
    Las migraciones tocan datos reales de alumnos y MySQL no hace DDL
    transaccional: si una falla a la mitad, la tabla se queda en un estado
    intermedio. Ejecutarlas a mano una por una es facil de hacer mal, sobre todo
    el dia del despliegue y con prisa.

    Este guion hace, en este orden:
      1. Respalda la base entera con mysqldump y comprueba que el respaldo sirve.
      2. Pasa db/00_revision_previa.sql, que no cambia nada, y SE DETIENE si
         alguna comprobacion dice BLOQUEA.
      3. Aplica 03, 04, 05, 06 y de la 08 a la 25 en orden, parando en el primer error.
      4. Vuelve a pasar la revision para enseñar como quedo.

    Las veintidos migraciones son repetibles: si una ya estaba aplicada lo dice y
    sigue. Se pueden volver a pasar sin romper nada.

    La 18 (encargados de laboratorio) crea ademas la cuenta de MySQL de solo
    lectura digbit_consulta, y eso solo lo puede hacer root: con otro usuario
    hace solo su primera parte y lo dice con un AVISO.

    La 20 (vigencia de cada franja) no cambia nada al aplicarla: los cambios de
    horario solo aplican desde una fecha cuando se enciende en Ajustes, y eso
    se hace despues de actualizar TODOS los equipos (INSTALAR.md).

    La 21 (historial de cambios) crea una tabla nueva y no toca nada mas. Con
    root deja ademas que digbit_consulta la lea (los encargados ven las alertas
    de cambios de horario); con otro usuario, lo dice con un AVISO.

    La 22 (asistencias que da el docente) crea una tabla nueva y no toca nada
    mas. Antes hay que actualizar TODOS los equipos. Con root da permiso a
    digbit_equipo y digbit_consulta; con otro usuario, lo dice con un AVISO.

    La 23 (configuraciones de horario) solo cambia las tablas de la 16, que
    solo usa el DigLab de la administracion: no hace falta actualizar los
    equipos. Con root deja ademas que digbit_consulta las lea (la persona
    responsable del laboratorio ve las guardadas y las aplicadas); con otro
    usuario, lo dice con un AVISO.

    La 24 (docentes con el SIIA) anade una columna vacia a usuarios y la tabla
    de quienes esperan que los enlacen. No cambia nada hasta encender en
    Ajustes el inicio de sesion de docentes con el SIIA. Con root da permiso a
    digbit_equipo y digbit_consulta; con otro usuario, lo dice con un AVISO.

    La 25 (horas que faltan por asignar) crea una tabla nueva y no toca nada
    mas. Solo la usa el DigLab de la administracion: no hace falta actualizar
    los equipos. Con root deja ademas que digbit_consulta la lea (la persona
    responsable ve "Falta asignar"); con otro usuario, lo dice con un AVISO.

    De la 26 a la 28 (fallas resueltas, suspensiones y memorias de emergencia)
    crean una tabla cada una; con root dan permiso de solo lectura a
    digbit_equipo (y a digbit_consulta la 26 y la 27).

    La 29 (DigLab 1.3: versiones publicadas en la base y ordenes a los equipos,
    "Actualizar ya" y "Apagar equipos") crea tres tablas y no toca nada mas.
    Va ANTES de instalar DigLab 1.3 en los equipos. Con la cuenta teschi_otru
    basta para crearlas; con root da ademas a digbit_equipo solo SELECT sobre
    ellas (un equipo nunca debe poder publicar un instalador).

.EXAMPLE
    .\migrar_bd.ps1 -Servidor 127.0.0.1 -Usuario root -ContrasenaAdmin "LaDelAdministrador"
    .\migrar_bd.ps1 -Servidor 10.0.0.5 -Usuario root -SoloRevisar
#>
[CmdletBinding()]
param(
    [string]$Servidor = "127.0.0.1",
    [int]$Puerto = 3306,
    [string]$Base = "teschi_otru",

    # Usuario de MySQL con permisos para ALTER y CREATE (normalmente root).
    [string]$Usuario = "root",
    [string]$Contrasena,

    # Contrasena del administrador ADMIN001 que crea la migracion 04. Se
    # sustituye en memoria, para no tener que editar el .sql ni dejarla escrita
    # en el repositorio. Solo hace falta si ADMIN001 todavia no existe.
    [string]$ContrasenaAdmin,

    [string]$CarpetaRespaldo,

    # Carpeta donde estan mysql.exe y mysqldump.exe, si no estan en el PATH.
    [string]$BinMysql,

    # Respalda y revisa, pero no aplica nada.
    [switch]$SoloRevisar
)

$ErrorActionPreference = "Stop"

# OJO: $PSScriptRoot llega VACIO dentro de los valores por defecto del bloque
# param cuando el guion se lanza con "powershell -File <ruta>", que es como lo
# llama el instalador. En el cuerpo si esta, asi que se resuelve aqui. Con & o
# dot-source funcionaba de las dos formas, y por eso no se vio antes.
if (-not $CarpetaRespaldo) { $CarpetaRespaldo = Join-Path $PSScriptRoot "..\respaldos" }

function Paso([string]$t) { Write-Host ""; Write-Host "== $t" -ForegroundColor Cyan }

$carpetaDb = Join-Path $PSScriptRoot "..\db"
$revision  = Join-Path $carpetaDb "00_revision_previa.sql"
$migraciones = @(
    "03_migracion_fase2.sql",
    "04_migracion_horarios.sql",
    "05_migracion_sin_conexion.sql",
    "06_migracion_contrasenas.sql",
    "08_migracion_bajas.sql",
    "09_codigo_por_profesor.sql",
    "10_red_y_autoregistro.sql",
    "11_modo_de_acceso.sql",
    "12_color_profesor.sql",
    "13_recuperacion_contrasena.sql",
    "14_registro_sin_clase.sql",
    "15_reportes_profesor.sql",
    "16_ciclo_preliminar.sql",
    "17_asistencia_retirada.sql",
    "18_encargados.sql",
    "19_personalizacion_clases.sql",
    "20_vigencia_franjas.sql",
    "21_historial_cambios.sql",
    "22_asistencia_dada.sql",
    "23_horarios_guardados.sql",
    "24_docentes_siia.sql",
    "25_horas_por_asignar.sql",
    "26_fallas_resueltas.sql",
    "27_suspensiones.sql",
    "28_memorias_emergencia.sql",
    "29_actualizaciones.sql"
) | ForEach-Object { Join-Path $carpetaDb $_ }

# Repone la clave foranea que la 04 quita un momento, si una migracion se
# corta a medias. Ver db\04b_reparar_clave_pendiente.sql.
$reparacion = Join-Path $carpetaDb "04b_reparar_clave_pendiente.sql"

foreach ($f in @($revision, $reparacion) + $migraciones) {
    if (-not (Test-Path $f)) { throw "No encuentro $f. Ejecuta esto desde deploy\ dentro del repositorio." }
}

# --- Localizar mysql.exe y mysqldump.exe ----------------------------------------
function Buscar([string]$nombre) {
    if ($BinMysql) {
        $ruta = Join-Path $BinMysql $nombre
        if (Test-Path $ruta) { return $ruta }
        throw "No esta $ruta."
    }
    $cmd = Get-Command $nombre -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @(
        (Join-Path $env:ProgramFiles "MySQL\MySQL Server 8.0\bin"),
        (Join-Path $env:ProgramFiles "MySQL\MySQL Server 8.4\bin"),
        "C:\DigLabDB\bin")) {
        $ruta = Join-Path $p $nombre
        if (Test-Path $ruta) { return $ruta }
    }
    throw "No encuentro $nombre. Indica -BinMysql con la carpeta que lo contiene."
}

$mysql = Buscar "mysql.exe"
$dump  = Buscar "mysqldump.exe"

# La contrasena va por el entorno y no en la linea de comandos: asi no queda en
# el historial ni la ve quien liste los procesos.
if ($Contrasena) { $env:MYSQL_PWD = $Contrasena }
# El juego de caracteres se declara, no se deja al azar. utf8mb4 porque es un
# superconjunto de utf8: sirve igual para la base del TESCHI (tablas utf8, en
# MariaDB 5.5) que para una hecha con 01_schema.sql (utf8mb4), sin perder nada.
$comunes = @("-h", $Servidor, "-P", "$Puerto", "-u", $Usuario, "--default-character-set=utf8mb4")

# Lo que se le pasa a mysql.exe por la tuberia (la revision y las migraciones)
# sale codificado con $OutputEncoding, que en Windows PowerShell 5.1 es ASCII:
# cualquier letra con acento llegaria como '?'. Se fija aqui, en el ambito del
# guion, asi no se cambia nada de la consola de quien lo ejecute.
$OutputEncoding = New-Object Text.UTF8Encoding $false

# Cada .sql trae USE teschi_otru; si la base se llama de otro modo, se cambia al
# vuelo en vez de pedirle al encargado que edite cuatro archivos.
function LeerSql([string]$archivo) {
    return (Get-Content -Raw $archivo) -replace "(?m)^USE\s+\w+;", "USE $Base;"
}

# En Windows PowerShell 5.1, con $ErrorActionPreference = "Stop", CUALQUIER linea
# que un .exe escriba en stderr se convierte en un error terminante, aunque el
# programa acabe bien. mysqldump avisa por stderr de cosas normales, asi que sin
# esto el guion se caia en avisos inofensivos. Se baja la guardia solo mientras
# corre el ejecutable y se decide por el codigo de salida, que es lo fiable.
function Nativo([scriptblock]$bloque) {
    $previo = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & $bloque } finally { $ErrorActionPreference = $previo }
}

Write-Host "Base: $Usuario@${Servidor}:$Puerto/$Base" -ForegroundColor DarkGray

# --- 1. Respaldo -----------------------------------------------------------------
Paso "1. Respaldo"
if (-not (Test-Path $CarpetaRespaldo)) { New-Item -ItemType Directory -Force $CarpetaRespaldo | Out-Null }
$archivoRespaldo = Join-Path $CarpetaRespaldo ("$Base-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".sql")

# Se intenta el volcado completo. Si el usuario no tiene permiso para leer
# rutinas o eventos (pasa con cuentas restringidas), se reintenta sin ellos en
# vez de quedarse sin respaldo, y se avisa de lo que falta. Lo que nunca se
# salta es el esquema y los datos.
#
# --result-file y NO una tuberia a Out-File. Pasandolo por PowerShell, la salida
# de mysqldump (UTF-8) se leia con la pagina de codigos de la consola y se volvia
# a escribir: "Quimica" con acento quedaba "Qu├¡mica" en el respaldo, mas un BOM
# al principio. Se vio ensayando contra una copia de la base del TESCHI, que
# tiene acentos. Un respaldo asi restaura nombres rotos: la red de seguridad era
# falsa. Con --result-file es mysqldump quien escribe los bytes, sin traducir.
$codigo = Nativo { & $dump @comunes "--single-transaction" "--no-tablespaces" "--routines" "--events" "--triggers" `
                       "--result-file=$archivoRespaldo" $Base
                   $LASTEXITCODE }
if ($codigo -ne 0) {
    Write-Warning "El usuario '$Usuario' no puede volcar rutinas ni eventos. Se reintenta solo con esquema y datos."
    $codigo = Nativo { & $dump @comunes "--single-transaction" "--no-tablespaces" "--skip-triggers" `
                           "--result-file=$archivoRespaldo" $Base
                       $LASTEXITCODE }
    if ($codigo -ne 0) { throw "mysqldump devolvio $codigo incluso en modo minimo. Sin respaldo no se sigue." }
    Write-Warning "El respaldo NO incluye rutinas, eventos ni disparadores. Para uno completo, usa una cuenta administradora."
}

$info = Get-Item $archivoRespaldo
if ($info.Length -lt 1024 -or -not (Select-String -Path $archivoRespaldo -Pattern "CREATE TABLE" -Quiet)) {
    throw "El respaldo $archivoRespaldo no parece valido ($($info.Length) bytes). Sin respaldo no se sigue."
}
Write-Host ("   {0}  ({1:N0} KB)" -f $archivoRespaldo, ($info.Length / 1KB)) -ForegroundColor Green
# Para restaurar, "source" dentro de mysql: lee el archivo el mismo. La receta
# anterior (Get-Content | mysql) pasaba por la tuberia de PowerShell y tenia el
# mismo defecto al reves: los acentos llegaban como '?'.
$respaldoParaMysql = $archivoRespaldo.Replace('\', '/')
Write-Host "   Para deshacerlo todo:" -ForegroundColor DarkGray
Write-Host "     & '$mysql' -h $Servidor -P $Puerto -u $Usuario -p --default-character-set=utf8mb4 $Base -e `"source $respaldoParaMysql`"" -ForegroundColor DarkGray

# --- 2. Revision -----------------------------------------------------------------
Paso "2. Revision previa (no cambia nada)"
$salida = Nativo { LeerSql $revision | & $mysql @comunes "--table" $Base }
if ($LASTEXITCODE -ne 0) { throw "mysql devolvio $LASTEXITCODE al revisar." }
$salida | ForEach-Object { Write-Host $_ }

$bloqueos = @($salida | Where-Object { $_ -match "BLOQUEA:" })
if ($bloqueos.Count -gt 0) {
    Write-Host ""
    throw "La revision encontro $($bloqueos.Count) problema(s) marcados BLOQUEA. Arreglalos y vuelve a ejecutar esto. NO se ha cambiado nada."
}
Write-Host ""
Write-Host "   Sin bloqueos." -ForegroundColor Green

if ($SoloRevisar) {
    Write-Host ""
    Write-Host "Modo -SoloRevisar: no se aplico ninguna migracion." -ForegroundColor Yellow
    return
}

# --- 3. Migraciones ---------------------------------------------------------------
Paso "3. Migraciones"
$necesitaAdmin = -not ($salida | Where-Object { $_ -match "ADMIN001" -and $_ -match "ya existe" })
if ($necesitaAdmin -and -not $ContrasenaAdmin) {
    throw "ADMIN001 no existe y no indicaste -ContrasenaAdmin. La 04 lo crearia con la contrasena de ejemplo CAMBIAME, que no puede quedarse."
}

foreach ($m in $migraciones) {
    $nombre = Split-Path $m -Leaf
    Write-Host "   aplicando $nombre ..." -NoNewline
    $texto = LeerSql $m
    if ($nombre -like "04_*" -and $ContrasenaAdmin) {
        $texto = $texto.Replace("MD5('CAMBIAME')", "MD5('" + $ContrasenaAdmin.Replace("'", "''") + "')")
    }
    $salidaMigracion = Nativo { $texto | & $mysql @comunes "--table" $Base 2>&1 }
    $codigoMigracion = $LASTEXITCODE
    $errores = @($salidaMigracion | Where-Object { $_ -is [Management.Automation.ErrorRecord] })
    # Lo que una migracion pide hacer a mano (por ejemplo, un permiso que solo
    # puede dar root) sale en una fila con AVISO: que no se pierda con el resto.
    $avisos = @($salidaMigracion | Where-Object { $_ -isnot [Management.Automation.ErrorRecord] -and "$_" -match 'AVISO' } |
        ForEach-Object { ("$_" -replace '^\|\s*', '' -replace '\s*\|\s*$', '').Trim() })
    if ($codigoMigracion -ne 0) {
        Write-Host ""
        foreach ($e in $errores) { Write-Host "   $e" -ForegroundColor Red }

        # Lo primero, antes de explicar nada: no dejar la base a medias. La 04
        # quita una clave foranea y la vuelve a poner; si se corto entre medias,
        # la clave estaria quitada. Esto la repone (y no hace nada si no hace falta).
        Write-Host "   reparando lo que haya quedado a medias..." -NoNewline
        $salidaReparacion = Nativo { LeerSql $reparacion | & $mysql @comunes "-N" "-B" $Base 2>&1 }
        if ($LASTEXITCODE -eq 0) {
            $linea = @($salidaReparacion | Where-Object { $_ -match 'REPUESTA|nada que reponer' }) | Select-Object -Last 1
            Write-Host " $linea" -ForegroundColor Yellow
        } else {
            Write-Host " FALLO: $salidaReparacion" -ForegroundColor Red
            Write-Host "   Ejecuta a mano db\04b_reparar_clave_pendiente.sql en phpMyAdmin." -ForegroundColor Red
        }

        if ("$errores" -match '1553') {
            Write-Host ""
            Write-Host "   El indice unico de codigos_accesos.codigo lo necesita una clave foranea que" -ForegroundColor Yellow
            Write-Host "   '$Usuario' NO puede ver (de otra base del servidor, o huerfana en InnoDB)." -ForegroundColor Yellow
            Write-Host "   Pide a quien administre el servidor que ejecute, como administrador:" -ForegroundColor Yellow
            Write-Host "     SELECT f.ID, f.FOR_NAME, c.FOR_COL_NAME FROM information_schema.INNODB_SYS_FOREIGN f" -ForegroundColor Yellow
            Write-Host "       JOIN information_schema.INNODB_SYS_FOREIGN_COLS c ON c.ID = f.ID" -ForegroundColor Yellow
            Write-Host "      WHERE f.REF_NAME = '$Base/codigos_accesos';" -ForegroundColor Yellow
        }
        throw "mysql devolvio $codigoMigracion al aplicar $nombre. Las anteriores si se aplicaron. Respaldo: $archivoRespaldo"
    }
    Write-Host " ok" -ForegroundColor Green
    foreach ($aviso in $avisos) { Write-Host "      $aviso" -ForegroundColor Yellow }
}

# --- 4. Como quedo -----------------------------------------------------------------
Paso "4. Como quedo"
(Nativo { LeerSql $revision | & $mysql @comunes "--table" $Base }) | ForEach-Object { Write-Host $_ }

Write-Host ""
Write-Host "Migraciones aplicadas." -ForegroundColor Green
if ($ContrasenaAdmin) {
    Write-Host "La cuenta de la administracion de DigLab es ADMIN001, con la contrasena que indicaste. Se guarda en MD5 y la aplicacion la reemplaza por PBKDF2 la primera vez que se entre con ella." -ForegroundColor Yellow
}
Write-Host "Respaldo por si acaso: $archivoRespaldo" -ForegroundColor DarkGray
