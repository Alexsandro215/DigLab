-- =============================================================================
-- DigLab 2.0 - Migracion: un codigo por profesor
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- Hasta ahora cada clase (profesor + grupo + materia) tenia su propio codigo
-- fijo: un profesor con seis grupos tenia seis codigos que recordar y repartir.
-- Desde aqui cada profesor tiene UNO, el mismo para todas sus clases. No hay
-- ambiguedad: al teclearlo en un equipo, la aplicacion busca la clase de ese
-- profesor que tiene horario en ESE laboratorio a ESA hora, y un profesor no
-- da dos clases a la vez.
--
-- Que hace:
--   1. usuarios.codigo_clase (VARCHAR(16), unico): el codigo del profesor.
--   2. clases.codigo deja de ser unico (lo comparten las clases de un mismo
--      profesor) y queda con un indice normal para buscar.
--   3. A cada profesor que aun no tiene codigo le da el de su clase con MAS
--      sesiones (el que ya conocen sus alumnos); si empatan, el de la mas
--      antigua.
--   4. Todas las clases de cada profesor pasan a tener su codigo.
--   5. Lista cada profesor con su codigo, para avisarles.
--
-- Las sesiones y bitacoras ya registradas no cambian: guardan el codigo que se
-- uso ese dia.
--
-- ORDEN: aplicala DESPUES de actualizar DigLab en todos los equipos. La version
-- anterior busca "la" clase de un codigo y se queda con la primera: con varias
-- clases compartiendo codigo rechazaria por error las demas. La version nueva
-- funciona igual con o sin esta migracion.
--
-- Repetible: si ya esta aplicada, lo dice y no cambia nada.
--
-- Uso:
--   mysql -u root -p < db/09_codigo_por_profesor.sql
-- =============================================================================

USE teschi_otru;

-- 1. La columna, con su indice unico.
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios'
                  AND COLUMN_NAME = 'codigo_clase');
SET @sql := IF(@existe = 0,
    'ALTER TABLE usuarios
        ADD COLUMN codigo_clase VARCHAR(16) NULL AFTER fk_tipo_usuario,
        ADD UNIQUE KEY uq_usuarios_codigo_clase (codigo_clase)',
    'SELECT ''usuarios.codigo_clase ya existe: no se toca'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- 2. clases.codigo sin UNIQUE. El indice se busca por su columna y no por su
-- nombre, por si en la base del TESCHI se llama distinto.
SET @indice := (SELECT INDEX_NAME FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'clases'
                  AND COLUMN_NAME = 'codigo' AND NON_UNIQUE = 0
                LIMIT 1);
SET @sql := IF(@indice IS NULL,
    'SELECT ''clases.codigo ya no es unico: no se toca'' AS resultado',
    CONCAT('ALTER TABLE clases DROP INDEX `', @indice, '`'));
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @hay := (SELECT COUNT(*) FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'clases' AND COLUMN_NAME = 'codigo');
SET @sql := IF(@hay = 0,
    'ALTER TABLE clases ADD KEY ix_clases_codigo (codigo)',
    'SELECT ''clases.codigo ya tiene indice'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- 3. El codigo de cada profesor: el de su clase con mas sesiones. Antes de esta
-- migracion cada clase tenia un codigo propio y sus sesiones lo guardaban, asi
-- que las sesiones de una clase se cuentan por su codigo.
UPDATE usuarios u
INNER JOIN (
    SELECT t.profesor,
           SUBSTRING_INDEX(GROUP_CONCAT(t.codigo ORDER BY t.sesiones DESC, t.idclases ASC SEPARATOR '|'), '|', 1) AS codigo
    FROM (SELECT c.idclases, c.usuarios_idusuarios AS profesor, c.codigo,
                 (SELECT COUNT(*) FROM codigos_accesos ca WHERE ca.codigo = c.codigo) AS sesiones
            FROM clases c) t
    GROUP BY t.profesor
) elegido ON elegido.profesor = u.idusuarios
SET u.codigo_clase = elegido.codigo
WHERE u.codigo_clase IS NULL;

-- 4. Todas sus clases con ese codigo.
UPDATE clases c
INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
SET c.codigo = u.codigo_clase
WHERE u.codigo_clase IS NOT NULL AND c.codigo <> u.codigo_clase;

-- 5. Para avisar a cada profesor de su codigo.
SELECT u.numero_identificador AS profesor,
       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS nombre,
       u.codigo_clase AS codigo,
       (SELECT COUNT(*) FROM clases c WHERE c.usuarios_idusuarios = u.idusuarios) AS clases
FROM usuarios u
WHERE u.fk_tipo_usuario = 2
ORDER BY u.apellido_paterno, u.nombre;
