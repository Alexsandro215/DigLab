-- =============================================================================
-- DigBit 2.0 - Migracion: asistencias que da el profesor a quien no pudo
-- registrar su bitacora
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- La asistencia de una clase sale de las bitacoras: quien registra, asistio.
-- Pero un alumno puede venir a la clase y no poder registrar: su equipo no
-- tiene internet, no funciona o no hay uno libre. Mientras la clase dura, su
-- profesor le da la asistencia de ese dia desde Mis grupos, con el motivo.
-- Cuenta como asistencia, igual que una bitacora, pero no es una bitacora: no
-- cuenta en las bitacoras registradas, ni en las fallas, ni en los equipos.
--
-- Una fila por asistencia dada: la clase, el dia, el alumno, la sesion del
-- bloque en curso, el profesor que la dio, cuando (reloj del servidor), por
-- que, el equipo que no le sirvio y desde que equipo se dio. Es documento
-- oficial: anularla no borra la fila (activa pasa a NULL, con quien y cuando)
-- y todas sus claves foraneas son RESTRICT: borrar a mano una clase, una
-- sesion o una cuenta con asistencias dadas se rechaza en vez de llevarselas.
--
-- PERMISOS: digbit_equipo (db/07), con el que entran los equipos de los
-- profesores, necesita leer e insertar en la tabla nueva y cambiar SOLO las
-- tres columnas con que se anula (activa, anulada_en, anulada_por): nunca
-- borrar ni reescribir lo dado. Se le dan aqui si quien ejecuta esto puede dar
-- permisos (root). digbit_admin ya puede todo en la base. digbit_consulta
-- (db/18), la cuenta de solo lectura de los encargados de laboratorio, solo la
-- lee: si ya existe, tambien se le da aqui; si no, se lo da la 18 al crearla.
--
-- ORDEN: primero se actualizan TODOS los equipos (laboratorios, profesores y
-- administracion) y despues se aplica esta migracion con root. Un DigBit
-- anterior no lee la tabla: veria falta donde hay asistencia dada, y su
-- Horarios podria mover una sesion que solo tiene asistencias dadas. Es un
-- CREATE TABLE que no copia ni bloquea ninguna otra tabla: se puede aplicar en
-- horario de clase. No depende de la 15, la 17 ni la 20.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/22_asistencia_dada.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS asistencias_dadas (
    idasistencias_dadas  INT           NOT NULL AUTO_INCREMENT,
    -- La clase (profesor + materia + grupo): la lista se lee por clase y dia.
    fk_clase             INT           NOT NULL,
    -- El dia (CURDATE() del servidor al darla): la lista cuenta por dia.
    fecha                DATE          NOT NULL,
    -- El alumno (tipo 1).
    fk_alumno            INT           NOT NULL,
    -- La sesion del bloque en curso: laboratorio, horas, Bitacora de la clase y Faltas.
    fk_codigo_accesos    INT           NOT NULL,
    -- El profesor que la dio.
    fk_profesor          INT           NOT NULL,
    -- Reloj del servidor (NOW() en la conexion de DigBit, -06:00). Sin DEFAULT: 5.5 no lo admite.
    dada_en              DATETIME      NOT NULL,
    -- sin_internet | equipo | sin_equipo | otro. VARCHAR y no ENUM: con sql_mode vacio un ENUM invalido seria ''.
    motivo               VARCHAR(20)   NOT NULL,
    -- El equipo que no le sirvio (como registros_bitacoras.numero_computadora).
    numero_computadora   VARCHAR(100)  NULL,
    comentario           VARCHAR(200)  NULL,
    -- "PC-12 @ Laboratorio de Redes (kiosco)": desde donde se dio.
    dada_desde           VARCHAR(120)  NULL,
    -- 1 cuenta; NULL anulada (nunca 0): el UNIQUE admite muchas anuladas y una sola que cuenta.
    activa               TINYINT(1)    NULL DEFAULT 1,
    anulada_en           DATETIME      NULL,
    anulada_por          INT           NULL,
    PRIMARY KEY (idasistencias_dadas),
    UNIQUE KEY uq_asistencias_dadas_dia (fk_clase, fecha, fk_alumno, activa),
    KEY ix_asistencias_dadas_fecha (fecha),
    KEY ix_asistencias_dadas_sesion (fk_codigo_accesos),
    KEY ix_asistencias_dadas_alumno (fk_alumno, fecha),
    KEY ix_asistencias_dadas_profesor (fk_profesor, fecha),
    KEY ix_asistencias_dadas_anulada_por (anulada_por),
    CONSTRAINT fk_asistencias_dadas_clase FOREIGN KEY (fk_clase)
        REFERENCES clases (idclases) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_dadas_sesion FOREIGN KEY (fk_codigo_accesos)
        REFERENCES codigos_accesos (idcodigos_accesos) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_dadas_alumno FOREIGN KEY (fk_alumno)
        REFERENCES usuarios (idusuarios) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_dadas_profesor FOREIGN KEY (fk_profesor)
        REFERENCES usuarios (idusuarios) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_dadas_anulada_por FOREIGN KEY (anulada_por)
        REFERENCES usuarios (idusuarios) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permiso de digbit_equipo para leer, dar y anular. Nunca se escribe a mano el
-- origen de la cuenta: en MariaDB 5.5, un GRANT a un 'usuario'@'origen' que no
-- existe la crearia SIN contrasena (igual que en db/13 y db/17).
SET @equipo := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                WHERE GRANTEE LIKE '''digbit\_equipo''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;

-- digbit_equipo: leer y dar.
SET @sql := CASE
    WHEN @equipo IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT, INSERT ON `', DATABASE(), '`.asistencias_dadas TO ', @equipo)
    WHEN @equipo IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/22), ',
               'o que ejecute: GRANT SELECT, INSERT ON `', DATABASE(), '`.asistencias_dadas TO ', REPLACE(@equipo, '''', ''''''),
               '; y GRANT UPDATE (activa, anulada_en, anulada_por) ON `', DATABASE(), '`.asistencias_dadas TO ', REPLACE(@equipo, '''', ''''''),
               '; (es la cuenta que ya existe).'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_equipo: no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07), pide a root que aplique esta misma migracion (db/22): le dara a esa cuenta permiso para leer, dar y anular asistencias dadas. Si entran con un usuario que ya puede todo en la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- digbit_equipo: anular, SOLO estas tres columnas (nunca el motivo, el dia, el
-- alumno ni el profesor). FROM DUAL no sobra: MariaDB 5.5 no acepta un SELECT
-- con WHERE sin tabla.
SET @sql := IF(@equipo IS NOT NULL AND @puede_dar,
    CONCAT('GRANT UPDATE (activa, anulada_en, anulada_por) ON `', DATABASE(), '`.asistencias_dadas TO ', @equipo),
    'SELECT 1 FROM DUAL WHERE 1 = 0');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- Lo mismo para digbit_consulta (db/18), pero SOLO leer: DigBit no deja entrar
-- a los encargados con esa cuenta si puede escribir algo.
SET @consulta := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                  WHERE GRANTEE LIKE '''digbit\_consulta''@%' LIMIT 1);
SET @sql := CASE
    WHEN @consulta IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT ON `', DATABASE(), '`.asistencias_dadas TO ', @consulta)
    WHEN @consulta IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/22), ',
               'o que ejecute: GRANT SELECT ON `', DATABASE(), '`.asistencias_dadas TO ', REPLACE(@consulta, '''', ''''''),
               '; (es la cuenta de solo lectura de los encargados de laboratorio, que ya existe).'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_consulta (lo crea db/18): no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si ya se aplico db/18 con root (digbit_consulta, la cuenta de solo lectura de los encargados de laboratorio), pide a root que aplique esta misma migracion (db/22) o que vuelva a aplicar la 18: asi esa cuenta podra leer asistencias_dadas. Si no, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'asistencias_dadas lista' AS resultado,
       (SELECT COUNT(*) FROM asistencias_dadas WHERE activa = 1) AS cuentan,
       (SELECT COUNT(*) FROM asistencias_dadas WHERE activa IS NULL) AS anuladas;
