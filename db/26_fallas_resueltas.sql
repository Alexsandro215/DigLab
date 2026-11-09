-- =============================================================================
-- DigLab 2.0 - Migracion: fallas reportadas que ya se resolvieron
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas: la tabla ya esta, y los permisos los dan la 07 y la 18.
--
-- Hasta ahora, lo que reportaba un estudiante en su bitacora o su docente desde
-- su panel se quedaba en naranja para siempre, aunque ya se hubiera arreglado, y
-- nada lo distinguia de lo que seguia pendiente (pedido del 2026-11-06).
--
-- Con esta migracion, en Bitacoras, la administracion marca las fallas de una
-- sesion como resueltas ("Resolver fallas..."), con lo que se hizo si quiere:
--   - Ya no salen en naranja ni en Alertas; en la lista dicen que se resolvieron.
--   - El PDF de la sesion las lleva al final, con la fecha y lo que se hizo.
--   - Se puede deshacer: desmarcarla la vuelve a dejar pendiente.
-- Lo que reporto cada quien NO se toca (registros_bitacoras y reportes_profesor
-- quedan igual) y Estadisticas sigue contando todo lo que se reporto: un renglon
-- aqui solo dice que ya se atendio. Cada cambio queda en el historial (db/21).
--
-- PERMISOS:
--   digbit_admin (db/07): ya puede (SELECT, INSERT, UPDATE y DELETE en toda la
--     base). Solo la administracion marca y desmarca.
--   digbit_equipo (db/07): solo leerla (el docente ve en "Mis bitacoras" lo que ya
--     se resolvio). Sin el permiso, a ese equipo todo le sale como pendiente.
--   digbit_consulta (db/18): solo leerla, por lo mismo.
--   Se les da aqui si quien ejecuta esto puede dar permisos (root); si no, sale un
--   AVISO. La 07 y la 18 de esta version tambien los incluyen.
--
-- ORDEN: da igual. Sin ella DigLab funciona como antes (conexion/Esquema.cs): todo
-- reportado sale en naranja y no hay "Resolver fallas...". Crea una tabla nueva y
-- no toca ninguna otra: se puede aplicar en horario de clase. Para marcarlas hace
-- falta el DigLab de la administracion con esta version; los equipos con otra
-- version siguen funcionando igual.
--
-- MariaDB 5.5 (la de la escuela): las fechas no llevan DEFAULT (alli no se admite
-- DEFAULT CURRENT_TIMESTAMP en un DATETIME); las pone DigLab con NOW() del
-- servidor, en la zona de DigLab (-06:00), como las demas.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/26_fallas_resueltas.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS fallas_resueltas (
    idfallas_resueltas  INT           NOT NULL AUTO_INCREMENT,
    -- De que es: 'bitacora' (registros_bitacoras.idregistros_bitacoras, lo que
    -- reporto un estudiante, tambien los equipos sin internet que aviso) o
    -- 'docente' (reportes_profesor.idreportes_profesor). VARCHAR y no ENUM: con
    -- sql_mode vacio un ENUM invalido seria ''. Sin claves foraneas, como el
    -- historial: son dos tablas distintas y nada de esto debe impedir nada.
    origen              VARCHAR(10)   NOT NULL,
    registro_id         INT           NOT NULL,
    -- Cuando y quien (reloj del servidor, sin DEFAULT: 5.5 no lo admite).
    resuelta_en         DATETIME      NOT NULL,
    resuelta_por        INT           NULL,
    -- Lo que se hizo, si se escribio: sale en el PDF.
    nota                VARCHAR(200)  NULL,
    PRIMARY KEY (idfallas_resueltas),
    UNIQUE KEY uq_fallas_resueltas (origen, registro_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permisos. Nunca se escribe a mano el origen de una cuenta: en MariaDB 5.5, un
-- GRANT a un 'usuario'@'origen' que no existe la crearia SIN contrasena (igual
-- que en db/13, db/19, db/21 y db/25).
SET @equipo := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                WHERE GRANTEE LIKE '''digbit\_equipo''@%' LIMIT 1);
SET @consulta := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                  WHERE GRANTEE LIKE '''digbit\_consulta''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @nada := 'SELECT 1 FROM DUAL WHERE 1 = 0';

SET @sql := IF(@equipo IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT ON `', DATABASE(), '`.fallas_resueltas TO ', @equipo), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := IF(@consulta IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT ON `', DATABASE(), '`.fallas_resueltas TO ', @consulta), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @sql := CASE
    WHEN (@equipo IS NOT NULL OR @consulta IS NOT NULL) AND @puede_dar THEN
        CONCAT('SELECT ''permisos de fallas_resueltas: ', IF(@equipo IS NULL, 'sin digbit_equipo', 'digbit_equipo solo lee'),
               ', ', IF(@consulta IS NULL, 'sin digbit_consulta', 'digbit_consulta solo lee'), ''' AS resultado')
    WHEN @equipo IS NOT NULL OR @consulta IS NOT NULL THEN
        'SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/26). Sin eso, los equipos que entran con digbit_equipo y los encargados de laboratorio ven todas las fallas como pendientes.'' AS resultado'
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuarios digbit_equipo ni digbit_consulta: no hace falta darles permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07) o hay encargados de laboratorio (db/18), pide a root que aplique esta misma migracion (db/26). Si entran con un usuario que ya puede todo en la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'fallas_resueltas lista' AS resultado, (SELECT COUNT(*) FROM fallas_resueltas) AS resueltas;
