-- =============================================================================
-- DigBit 2.0 - Migracion: asistencias que el profesor quita durante su clase
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- La asistencia de una clase sale de las bitacoras: quien registra, asistio.
-- Pero alguien puede registrar sin estar en el salon (se la llena un
-- companero, o la llena desde otro lado). Mientras la clase dura, el profesor
-- puede quitarle la asistencia de ese dia desde su lista ("registro su
-- bitacora pero no esta presente") y devolversela si se equivoco. La bitacora
-- no se toca: sigue contando como bitacora (y sus fallas, como fallas); solo
-- deja de contar como asistencia, y su fila de la lista se ve de otro color.
--
-- Una fila por bitacora retirada; devolver la asistencia borra la fila.
--
-- PERMISOS: digbit_equipo (db/07), con el que entran los equipos de los
-- profesores, necesita leer, insertar y borrar en la tabla nueva; se le dan
-- aqui si quien ejecuta esto puede dar permisos (root). digbit_admin ya puede
-- todo en la base. digbit_consulta (db/18), la cuenta de solo lectura de los
-- encargados de laboratorio, solo la lee: si ya existe, tambien se le da aqui;
-- si no, se lo da la 18 al crearla.
--
-- ORDEN: aplicarla no cambia nada para nadie. Los equipos con DigBit 1.1 la
-- notan solos y desde entonces el profesor puede quitar asistencias; sin ella,
-- la lista solo se consulta.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/17_asistencia_retirada.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS asistencias_retiradas (
    -- La bitacora que deja de contar como asistencia (registros_bitacoras).
    fk_bitacora  INT       NOT NULL,
    -- El profesor que la quito (usuarios.idusuarios).
    fk_profesor  INT       NOT NULL,
    -- Reloj del servidor.
    retirada_en  DATETIME  NOT NULL,
    PRIMARY KEY (fk_bitacora),
    KEY ix_asistencias_retiradas_profesor (fk_profesor),
    CONSTRAINT fk_asistencias_retiradas_bitacora
        FOREIGN KEY (fk_bitacora) REFERENCES registros_bitacoras (idregistros_bitacoras)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_retiradas_profesor
        FOREIGN KEY (fk_profesor) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permiso de digbit_equipo para leer, quitar y devolver. Nunca se escribe a
-- mano el origen de la cuenta: en MariaDB 5.5, un GRANT a un
-- 'usuario'@'origen' que no existe la crearia SIN contrasena (igual que en db/13).
SET @equipo := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                WHERE GRANTEE LIKE '''digbit\_equipo''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @sql := CASE
    WHEN @equipo IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT, INSERT, DELETE ON `', DATABASE(), '`.asistencias_retiradas TO ', @equipo)
    WHEN @equipo IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/17), ',
               'o que ejecute: GRANT SELECT, INSERT, DELETE ON `', DATABASE(), '`.asistencias_retiradas TO ', REPLACE(@equipo, '''', ''''''),
               '; (es la cuenta que ya existe).'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_equipo: no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07), pide a root que aplique esta misma migracion (db/17): le dara a esa cuenta permiso para leer, insertar y borrar en asistencias_retiradas. Si entran con un usuario que ya puede todo en la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- Lo mismo para digbit_consulta (db/18), pero SOLO leer: DigBit no deja entrar
-- a los encargados con esa cuenta si puede escribir algo.
SET @consulta := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                  WHERE GRANTEE LIKE '''digbit\_consulta''@%' LIMIT 1);
SET @sql := CASE
    WHEN @consulta IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT ON `', DATABASE(), '`.asistencias_retiradas TO ', @consulta)
    WHEN @consulta IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/17), ',
               'o que ejecute: GRANT SELECT ON `', DATABASE(), '`.asistencias_retiradas TO ', REPLACE(@consulta, '''', ''''''),
               '; (es la cuenta de solo lectura de los encargados de laboratorio, que ya existe).'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_consulta (lo crea db/18): no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si ya se aplico db/18 con root (digbit_consulta, la cuenta de solo lectura de los encargados de laboratorio), pide a root que aplique esta misma migracion (db/17) o que vuelva a aplicar la 18: asi esa cuenta podra leer asistencias_retiradas. Si no, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'asistencias_retiradas lista' AS resultado, (SELECT COUNT(*) FROM asistencias_retiradas) AS retiradas;
