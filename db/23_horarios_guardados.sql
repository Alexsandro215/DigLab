-- =============================================================================
-- DigLab 2.0 - Migracion: configuraciones de horario (guardar, preparar y
-- aplicar)
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas: las tablas ya tienen esta forma, y el permiso de digbit_consulta
-- lo da la 18, que en una base nueva hace falta igual.
--
-- Requiere db/16_ciclo_preliminar.sql; sin ella solo avisa.
--
-- Con la 16, el administrador armaba en Horarios UN borrador del proximo
-- ciclo. Con esta, en Horarios > Configuraciones guarda copias con nombre del
-- horario (cada docente con sus clases y sus horas en cada laboratorio), tiene
-- varias, carga una de un ciclo anterior para prepararla, y en Horarios >
-- Aplicacion la aplica con sus fechas. Hace cuatro cosas en las tablas de la
-- 16, sin tocar ninguna otra:
--   1) ciclos_preliminares.estado admite 'guardado' (una configuracion
--      guardada). 'borrador' es la que se prepara y 'publicado', una aplicada.
--   2) ciclos_preliminares.desde y hasta pueden ir vacias: una guardada no
--      tiene fechas, y la que se prepara las recibe al aplicarla.
--   3) ciclos_preliminares.origen_ciclo (de que configuracion viene, o en cual
--      se guardo por ultima vez), cambiado_en y cambiado_por (el ultimo cambio).
--   4) ciclo_cargas.carga_origen: la clase de la configuracion de la que se
--      copio cada una (asi se copian sus horas con un solo INSERT ... SELECT).
--
-- ORDEN: aplicarla no cambia nada para nadie: solo el DigLab de la
-- administracion usa estas tablas, y los laboratorios no se tocan. MariaDB 5.5
-- reconstruye ciclos_preliminares al cambiar el ENUM; son tablas chicas que
-- nadie mas usa, asi que se puede aplicar en horario de clase.
--
-- PERMISOS:
--   digbit_admin (db/07): ya puede (SELECT, INSERT, UPDATE y DELETE en toda la
--     base).
--   digbit_consulta (db/18): solo leer las 5 tablas de la 16, para que la
--     persona responsable del laboratorio vea las configuraciones guardadas y
--     las aplicadas. Se le da aqui si quien ejecuta esto puede dar permisos
--     (root); si no, sale un AVISO. La 18 de esta version tambien lo incluye.
--   digbit_equipo (db/07): nada. Los equipos de los laboratorios y de los
--     docentes no usan estas tablas.
--
-- MariaDB 5.5 (la de la escuela): cambiado_en no lleva DEFAULT (alli no se
-- admite DEFAULT CURRENT_TIMESTAMP en un DATETIME); lo pone DigLab con NOW()
-- del servidor. Las columnas se anaden despues de mirar information_schema,
-- como en db/20.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/23_horarios_guardados.sql
-- =============================================================================

USE teschi_otru;

SET @hay16 := (SELECT COUNT(*) FROM information_schema.TABLES
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclos_preliminares');

-- 1) estado: 'guardado' para las configuraciones guardadas. 'publicado' es una
--    configuracion aplicada (el nombre viene de db/16 y no se cambia). Sin la
--    tabla, @tipo es NULL, NOT LIKE da NULL y el IF cae en el aviso.
SET @tipo := (SELECT COLUMN_TYPE FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclos_preliminares' AND COLUMN_NAME = 'estado');
SET @sql := IF(@hay16 = 1 AND @tipo NOT LIKE '%guardado%',
    'ALTER TABLE ciclos_preliminares MODIFY COLUMN estado ENUM(''borrador'',''publicado'',''guardado'') NOT NULL DEFAULT ''borrador''',
    'SELECT ''estado ya admite guardado (o falta db/16)'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- 2) desde y hasta pueden ir vacias: una guardada no tiene fechas y la que se
--    prepara las recibe en Aplicacion.
SET @nulo := (SELECT IS_NULLABLE FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclos_preliminares' AND COLUMN_NAME = 'desde');
SET @sql := IF(@hay16 = 1 AND @nulo = 'NO',
    'ALTER TABLE ciclos_preliminares MODIFY COLUMN desde DATE NULL, MODIFY COLUMN hasta DATE NULL',
    'SELECT ''desde y hasta ya admiten NULL (o falta db/16)'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- 3) De que configuracion viene (o en cual se guardo por ultima vez) y el
--    ultimo cambio. Sin clave foranea a si misma: si esa se borra, queda el
--    numero y DigLab lo trata como "sin guardar".
SET @hay := (SELECT COUNT(*) FROM information_schema.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclos_preliminares' AND COLUMN_NAME = 'origen_ciclo');
SET @sql := IF(@hay16 = 1 AND @hay = 0,
    'ALTER TABLE ciclos_preliminares
       ADD COLUMN origen_ciclo INT NULL AFTER copiado_del,
       ADD COLUMN cambiado_en DATETIME NULL AFTER creado_en,
       ADD COLUMN cambiado_por INT NULL AFTER cambiado_en,
       ADD KEY ix_ciclos_preliminares_origen (origen_ciclo),
       ADD CONSTRAINT fk_ciclos_preliminares_cambio FOREIGN KEY (cambiado_por)
           REFERENCES usuarios (idusuarios) ON DELETE SET NULL ON UPDATE CASCADE',
    'SELECT ''ciclos_preliminares ya tiene origen_ciclo (o falta db/16)'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- 4) La clase de la configuracion de la que se copio cada una: con ella se
--    copian sus franjas con un solo INSERT ... SELECT. Sin clave foranea.
SET @hay := (SELECT COUNT(*) FROM information_schema.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclo_cargas' AND COLUMN_NAME = 'carga_origen');
SET @sql := IF(@hay16 = 1 AND @hay = 0,
    'ALTER TABLE ciclo_cargas ADD COLUMN carga_origen INT NULL AFTER clase_publicada, ADD KEY ix_ciclo_cargas_carga_origen (carga_origen)',
    'SELECT ''ciclo_cargas ya tiene carga_origen (o falta db/16)'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- 5) digbit_consulta: solo leer las 5 tablas (la persona responsable ve las
--    guardadas y las aplicadas). Como en db/21: nunca se escribe a mano el
--    origen de la cuenta (en MariaDB 5.5, un GRANT a un 'usuario'@'origen' que
--    no existe la crearia SIN contrasena). GRANT admite una tabla por
--    sentencia: un bloque por tabla, y solo con la 16.
SET @consulta := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                  WHERE GRANTEE LIKE '''digbit\_consulta''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @dar := @hay16 = 1 AND @consulta IS NOT NULL AND @puede_dar;
SET @nada := 'SELECT 1 FROM DUAL WHERE 1 = 0';

SET @sql := IF(@dar, CONCAT('GRANT SELECT ON `', DATABASE(), '`.ciclos_preliminares TO ', @consulta), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := IF(@dar, CONCAT('GRANT SELECT ON `', DATABASE(), '`.ciclo_profesores TO ', @consulta), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := IF(@dar, CONCAT('GRANT SELECT ON `', DATABASE(), '`.ciclo_cargas TO ', @consulta), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := IF(@dar, CONCAT('GRANT SELECT ON `', DATABASE(), '`.ciclo_franjas TO ', @consulta), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := IF(@dar, CONCAT('GRANT SELECT ON `', DATABASE(), '`.ciclo_recortes TO ', @consulta), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @sql := CASE
    WHEN @hay16 = 0 THEN @nada
    WHEN @dar THEN
        'SELECT ''digbit_consulta puede leer las configuraciones de horario (solo SELECT)'' AS resultado'
    WHEN @consulta IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/23), ',
               'o que ejecute GRANT SELECT ON `', DATABASE(), '`.<tabla> TO ', REPLACE(@consulta, '''', ''''''),
               ' con cada una de estas tablas: ciclos_preliminares, ciclo_profesores, ciclo_cargas, ciclo_franjas y ciclo_recortes ',
               '(es la cuenta que ya existe). Sin eso, las personas responsables de laboratorio no ven las configuraciones de horario.'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_consulta (db/18): no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si hay responsables de laboratorio (db/18), pide a root que aplique esta misma migracion (db/23): le dara a digbit_consulta permiso para leer las configuraciones de horario.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- digbit_equipo a proposito no recibe nada (ver PERMISOS arriba).

SELECT CASE WHEN @hay16 = 0 THEN 'AVISO: falta db/16_ciclo_preliminar.sql: aplicala y vuelve a correr esta.'
            ELSE 'configuraciones de horario listas' END AS resultado,
       (SELECT COUNT(*) FROM information_schema.COLUMNS
         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclo_cargas' AND COLUMN_NAME = 'carga_origen') AS con_carga_origen;
