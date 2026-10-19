-- =============================================================================
-- DigBit 2.0 - Migracion: como entran los alumnos a sus clases
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- laboratorios.modo_codigo dice como entran los alumnos a sus clases ahi:
--
--   'fijo'        "con codigo unico": el codigo de cada profesor (db/09), uno
--                 para todas sus materias. Es lo de siempre y el valor de todos
--                 los laboratorios al aplicar esto.
--   'renovable'   "generacion de codigos": un codigo distinto por clase y dia,
--                 generado al empezar cada semana a partir de un secreto en
--                 configuracion (db/13). Solo el administrador los ve todos; el
--                 profesor ve el de su clase al entrar a DigBit durante ella
--                 (desde 15 minutos antes), y solo vale para esa clase.
--   'sin_codigo'  "sin codigos": el alumno inicia sesion y llena su bitacora;
--                 con una sola clase en ese laboratorio a esa hora se elige
--                 sola, con dos elige la suya.
--
-- "Para toda la escuela" es el mismo valor en todas las filas; el
-- administrador lo cambia desde su panel (Acceso a las clases).
--
-- ENUM y no TINYINT(1): la aplicacion lee TINYINT(1) como booleano. Con el
-- sql_mode vacio de la escuela un valor fuera de la lista entra como '': la
-- aplicacion lo trata como 'fijo' y esta migracion lo devuelve a 'fijo'.
--
-- PERMISOS: ninguno nuevo. El usuario de los equipos (digbit_equipo, db/07)
-- ya puede leer laboratorios y no puede cambiarlos; el del administrador si.
--
-- ORDEN: aplicarla no cambia nada (todo queda en 'fijo'). No saques un
-- laboratorio de 'fijo' hasta que TODOS sus equipos y las computadoras de sus
-- profesores tengan DigBit 1.1 (deploy\diagnostico_equipo.ps1 lo dice): una
-- version anterior no conoce los modos y seguiria aceptando el codigo fijo.
--
-- Solo anade una columna a una tabla de pocas filas: es inmediato, tambien en
-- MariaDB 5.5. No toca codigos_accesos ni registros_bitacoras.
--
-- Repetible: lo que ya esta, lo dice y no lo toca.
--
-- Uso:
--   mysql -u root -p < db/11_modo_de_acceso.sql
-- =============================================================================

USE teschi_otru;

SET @falta := (SELECT COUNT(*) = 0 FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'laboratorios'
                 AND COLUMN_NAME = 'modo_codigo');
SET @sql := IF(@falta,
    'ALTER TABLE laboratorios ADD COLUMN modo_codigo ENUM(''fijo'',''renovable'',''sin_codigo'') NOT NULL DEFAULT ''fijo''',
    'SELECT ''laboratorios.modo_codigo ya existe: no se toca'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

UPDATE laboratorios SET modo_codigo = 'fijo' WHERE modo_codigo = '';

-- Solo informa. La generacion de codigos se apoya en UNA sesion por franja
-- y dia, y UNA por sesion extra (codigos_accesos, db/04): es lo que garantiza
-- un solo codigo por clase aunque la abran a la vez el panel del profesor, el
-- administrador y un equipo que sube su cola. Se buscan por columnas, no por
-- nombre.
SET @uq_franja := (SELECT COUNT(*) FROM information_schema.STATISTICS a
    INNER JOIN information_schema.STATISTICS b
        ON b.TABLE_SCHEMA = a.TABLE_SCHEMA AND b.TABLE_NAME = a.TABLE_NAME AND b.INDEX_NAME = a.INDEX_NAME
    WHERE a.TABLE_SCHEMA = DATABASE() AND a.TABLE_NAME = 'codigos_accesos' AND a.NON_UNIQUE = 0
      AND a.COLUMN_NAME = 'horarios_idhorarios' AND a.SEQ_IN_INDEX = 1
      AND b.COLUMN_NAME = 'fecha' AND b.SEQ_IN_INDEX = 2);
SET @uq_extra := (SELECT COUNT(*) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND NON_UNIQUE = 0
      AND COLUMN_NAME = 'horario_excepciones_id' AND SEQ_IN_INDEX = 1);
SELECT IF(@uq_franja > 0 AND @uq_extra > 0,
          'ok: una sesion por franja y dia, y una por sesion extra',
          'AVISO: falta un UNIQUE de codigos_accesos (db/04); no actives la generacion de codigos') AS sesiones_unicas;

SHOW COLUMNS FROM laboratorios WHERE Field = 'modo_codigo';
SELECT idlaboratorios, nombre_laboratorio, modo_codigo FROM laboratorios ORDER BY nombre_laboratorio;
