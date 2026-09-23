-- =============================================================================
-- DigBit 2.0 - Revision previa a las migraciones
-- =============================================================================
-- NO CAMBIA NADA. Solo mira y dictamina. Ejecutalo ANTES de 03, 04, 05 y 06
-- sobre la base real del TESCHI, y no sigas si alguna fila dice BLOQUEA.
--
-- Existe porque las migraciones tocan datos reales de alumnos y MySQL no hace
-- DDL transaccional: si una falla a la mitad, la tabla se queda en un estado
-- intermedio. Por eso cada choque que se conoce esta aqui, ANTES de tocar nada.
-- Los que se han encontrado ensayando contra una copia de la base del TESCHI
-- (MariaDB 5.5.68) salen en la seccion de detalles: las migraciones ya los
-- sortean, pero conviene saber que estan.
--
-- Uso:
--   mysql -u root -p < db/00_revision_previa.sql
--
-- Si la base no se llama teschi_otru, cambia el USE de abajo.
-- =============================================================================

USE teschi_otru;

SELECT '=== QUE HAY AHORA ===' AS comprobacion, '' AS resultado, '' AS veredicto
UNION ALL SELECT
    'Base de datos',
    DATABASE(),
    'info'
UNION ALL SELECT
    'Codificacion (base / tablas)',
    CONCAT((SELECT DEFAULT_CHARACTER_SET_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = DATABASE()),
           ' / ',
           (SELECT TABLE_COLLATION FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos')),
    -- Lo que cuenta son las TABLAS: la 04 hace las nuevas con el cotejamiento de
    -- codigos_accesos, no con el de la base. En el TESCHI la base es latin1 por
    -- defecto pero las tablas son utf8, y eso esta bien.
    IF((SELECT TABLE_COLLATION FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos') LIKE 'utf8%',
       'ok', 'REVISA: las tablas no son utf8, los acentos pueden salir mal')
UNION ALL SELECT
    'Filas: usuarios / codigos / bitacoras',
    CONCAT((SELECT COUNT(*) FROM usuarios), ' / ',
           (SELECT COUNT(*) FROM codigos_accesos), ' / ',
           (SELECT COUNT(*) FROM registros_bitacoras)),
    'info'

UNION ALL SELECT '=== LO QUE PUEDE IMPEDIRLO ===', '', ''
UNION ALL SELECT
    'Bitacoras duplicadas (mismo alumno y codigo)',
    CONCAT((SELECT COUNT(*) FROM (
        SELECT 1 FROM registros_bitacoras
        GROUP BY fk_codigo_accesos, fk_usuario HAVING COUNT(*) > 1) d), ' pares repetidos'),
    IF((SELECT COUNT(*) FROM (
        SELECT 1 FROM registros_bitacoras
        GROUP BY fk_codigo_accesos, fk_usuario HAVING COUNT(*) > 1) d) = 0,
       'ok', 'AVISO: la 03 no pondra el UNIQUE; la aplicacion hace cumplir la regla al insertar. No hay que borrar nada')
UNION ALL SELECT
    'Motor de registros_bitacoras',
    (SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras'),
    IF((SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras') = 'InnoDB',
       'ok', 'BLOQUEA: sin InnoDB no hay claves foraneas')
UNION ALL SELECT
    'Motor de codigos_accesos',
    (SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos'),
    IF((SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos') = 'InnoDB',
       'ok', 'BLOQUEA: sin InnoDB no hay claves foraneas')
UNION ALL SELECT
    'Tablas que las migraciones dan por hechas',
    CONCAT((SELECT COUNT(*) FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME IN ('usuarios','codigos_accesos','registros_bitacoras','laboratorios','grupos','materias','semestres','carreras','carrera_grupo_semestre')), ' de 9'),
    IF((SELECT COUNT(*) FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME IN ('usuarios','codigos_accesos','registros_bitacoras','laboratorios','grupos','materias','semestres','carreras','carrera_grupo_semestre')) = 9,
       'ok', 'BLOQUEA: falta alguna tabla base; esta no es la base de DigBit')

UNION ALL SELECT '=== QUE MIGRACIONES YA ESTAN ===', '', ''
UNION ALL SELECT
    '03 fase 2 (una bitacora por alumno y codigo)',
    -- Con duplicados la 03 no pone el UNIQUE a proposito. Decir "pendiente" ahi
    -- hacia creer que no se habia corrido.
    IF((SELECT COUNT(*) FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras'
          AND INDEX_NAME = 'uq_bitacora_codigo_usuario') > 0, 'ya aplicada',
       IF((SELECT COUNT(*) FROM (
            SELECT 1 FROM registros_bitacoras
            GROUP BY fk_codigo_accesos, fk_usuario HAVING COUNT(*) > 1) d) > 0,
          'sin UNIQUE a proposito: hay duplicados viejos (la aplicacion cumple la regla)',
          'pendiente')),
    'info'
UNION ALL SELECT
    '04 fase 6 (horarios por laboratorio)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos'
          AND COLUMN_NAME = 'horarios_idhorarios') > 0, 'ya aplicada', 'pendiente'),
    'info'
UNION ALL SELECT
    '05 fase 4 (bitacoras sin conexion)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras'
          AND COLUMN_NAME = 'registrado_sin_conexion') > 0, 'ya aplicada', 'pendiente'),
    'info'
UNION ALL SELECT
    '06 fase 7 (columna password para PBKDF2)',
    (SELECT CONCAT(COLUMN_TYPE, IF(CHARACTER_MAXIMUM_LENGTH >= 100, ' (cabe PBKDF2: la 06 no la toca)', ' (corta: la 06 la ensancha)'))
     FROM information_schema.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios' AND COLUMN_NAME = 'password'),
    'info'

UNION ALL SELECT '=== DETALLES UTILES ===', '', ''
UNION ALL SELECT
    'Indice unico sobre codigos_accesos.codigo',
    IFNULL((SELECT INDEX_NAME FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos'
              AND COLUMN_NAME = 'codigo' AND NON_UNIQUE = 0 LIMIT 1),
           'ninguno (nada que quitar)'),
    'info'
UNION ALL SELECT
    'Tabla tipo_usuarios',
    IF((SELECT COUNT(*) FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tipo_usuarios') = 0,
       'no existe',
       CONCAT('existe; tipo 3 ',
              IF((SELECT COUNT(*) FROM tipo_usuarios WHERE idtipo_usuarios = 3) > 0,
                 'ya esta', 'falta (la 04 lo crea antes que al administrador)'))),
    'info'
UNION ALL SELECT
    'Claves foraneas que apuntan a codigos_accesos.codigo',
    CONCAT((SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE
            WHERE REFERENCED_TABLE_SCHEMA = DATABASE()
              AND REFERENCED_TABLE_NAME = 'codigos_accesos'
              AND REFERENCED_COLUMN_NAME = 'codigo'),
           ' (la 04 la quita, quita el unico y la vuelve a poner identica)'),
    'info'
UNION ALL SELECT
    'Referencias rotas: bitacoras de usuarios que ya no existen',
    CONCAT((SELECT COUNT(*) FROM registros_bitacoras r
            WHERE NOT EXISTS (SELECT 1 FROM usuarios u WHERE u.idusuarios = r.fk_usuario)),
           ' (no impiden migrar; no salen en informes que cruzan con usuarios)'),
    'info'
UNION ALL SELECT
    'Administrador ADMIN001',
    IF((SELECT COUNT(*) FROM usuarios WHERE numero_identificador = 'ADMIN001') > 0,
       'ya existe (la 04 no lo tocara)', 'no existe (la 04 lo creara con la contrasena que pongas)'),
    'info'
UNION ALL SELECT
    'Contrasenas: PBKDF2 / MD5 / total',
    CONCAT(IFNULL(SUM(password LIKE 'pbkdf2-%'), 0), ' / ',
           IFNULL(SUM(CHAR_LENGTH(password) = 32 AND password REGEXP '^[0-9a-f]{32}$'), 0), ' / ',
           COUNT(*)),
    'info'
FROM usuarios;

-- -----------------------------------------------------------------------------
-- Los duplicados uno a uno, por si alguien quiere revisarlos. NO hace falta
-- borrarlos para migrar: la 03 simplemente no pone el UNIQUE si los hay.
-- -----------------------------------------------------------------------------
SELECT r.fk_codigo_accesos, r.fk_usuario, COUNT(*) AS repeticiones,
       GROUP_CONCAT(r.idregistros_bitacoras ORDER BY r.idregistros_bitacoras) AS ids,
       MIN(r.idregistros_bitacoras) AS conservar_este
FROM registros_bitacoras r
GROUP BY r.fk_codigo_accesos, r.fk_usuario
HAVING COUNT(*) > 1
ORDER BY repeticiones DESC;
