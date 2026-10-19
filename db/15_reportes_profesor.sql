-- =============================================================================
-- DigBit 2.0 - Migracion: fallas que reporta el profesor durante su clase
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- En el panel del profesor, mientras su clase esta en curso, "Reportar una
-- falla" guarda aqui lo que el profesor vio: un equipo que no enciende, la red,
-- el proyector... Cuelga de la sesion de la clase (codigos_accesos): al terminar
-- la clase ya no se puede reportar. Lo ven el profesor (en la bitacora de esa
-- clase y en las cifras de su panel) y el administrador (en las alertas de
-- fallas por laboratorio y dia, y en Bitacoras).
--
-- Una fila por falla: el profesor puede reportar varias en la misma clase.
--
-- PERMISOS: digbit_equipo (db/07), con el que entran los equipos de los
-- profesores, necesita leer e insertar en la tabla nueva; se le dan aqui si
-- quien ejecuta esto puede dar permisos (root). digbit_admin ya puede todo en
-- la base. digbit_consulta (db/18), la cuenta de solo lectura de los
-- encargados de laboratorio, solo la lee (sus Alertas ensenan estas fallas):
-- si ya existe, tambien se le da aqui; si no, se lo da la 18 al crearla.
--
-- ORDEN: aplicarla no cambia nada para nadie. Los equipos con DigBit 1.1 la
-- notan solos y desde entonces muestran "Reportar una falla"; sin ella, el
-- boton no aparece.
--
-- Repetible: lo que ya esta, lo dice y no lo toca.
--
-- Uso:
--   mysql -u root -p < db/15_reportes_profesor.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS reportes_profesor (
    idreportes_profesor INT          NOT NULL AUTO_INCREMENT,
    -- La sesion de la clase en que se reporto (la de la franja o la sesion extra, ese dia).
    fk_codigo_accesos   INT          NOT NULL,
    -- El profesor que reporta (usuarios.idusuarios).
    fk_usuario          INT          NOT NULL,
    -- El numero pegado en el equipo; NULL si la falla es del laboratorio en
    -- general (la red de todo el salon, el proyector, la luz...).
    numero_computadora  VARCHAR(80)  NULL,
    area                ENUM('red','hardware','software','otro') NOT NULL,
    -- De la misma lista que ve el alumno, o lo que escribio el profesor.
    falla               VARCHAR(120) NOT NULL,
    comentarios         VARCHAR(500) NULL,
    -- Reloj del servidor.
    registrado_en       DATETIME     NOT NULL,
    PRIMARY KEY (idreportes_profesor),
    KEY ix_reportes_profesor_sesion (fk_codigo_accesos),
    KEY ix_reportes_profesor_usuario (fk_usuario),
    CONSTRAINT fk_reportes_profesor_sesion
        FOREIGN KEY (fk_codigo_accesos) REFERENCES codigos_accesos (idcodigos_accesos)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_reportes_profesor_usuario
        FOREIGN KEY (fk_usuario) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permiso de digbit_equipo para leer e insertar. Nunca se escribe a mano el
-- origen de la cuenta: en MariaDB 5.5, un GRANT a un 'usuario'@'origen' que no
-- existe la crearia SIN contrasena (igual que en db/13).
SET @equipo := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                WHERE GRANTEE LIKE '''digbit\_equipo''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @sql := CASE
    WHEN @equipo IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT, INSERT ON `', DATABASE(), '`.reportes_profesor TO ', @equipo)
    WHEN @equipo IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/15), ',
               'o que ejecute: GRANT SELECT, INSERT ON `', DATABASE(), '`.reportes_profesor TO ', REPLACE(@equipo, '''', ''''''),
               '; (es la cuenta que ya existe).'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_equipo: no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07), pide a root que aplique esta misma migracion (db/15): le dara a esa cuenta permiso para leer e insertar en reportes_profesor. Si entran con un usuario que ya puede todo en la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- Lo mismo para digbit_consulta (db/18), pero SOLO leer: DigBit no deja entrar
-- a los encargados con esa cuenta si puede escribir algo. Si se aplica esta
-- migracion despues de la 18, sin esto sus Alertas no ensenan las fallas que
-- reporta el profesor.
SET @consulta := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                  WHERE GRANTEE LIKE '''digbit\_consulta''@%' LIMIT 1);
SET @sql := CASE
    WHEN @consulta IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT ON `', DATABASE(), '`.reportes_profesor TO ', @consulta)
    WHEN @consulta IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/15), ',
               'o que ejecute: GRANT SELECT ON `', DATABASE(), '`.reportes_profesor TO ', REPLACE(@consulta, '''', ''''''),
               '; (es la cuenta de solo lectura de los encargados de laboratorio, que ya existe).'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_consulta (lo crea db/18): no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si ya se aplico db/18 con root (digbit_consulta, la cuenta de solo lectura de los encargados de laboratorio), pide a root que aplique esta misma migracion (db/15) o que vuelva a aplicar la 18: asi esa cuenta podra leer reportes_profesor. Si no, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'reportes_profesor lista' AS resultado, (SELECT COUNT(*) FROM reportes_profesor) AS reportes_guardados;
