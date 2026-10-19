-- =============================================================================
-- DigBit 2.0 - Revision previa a las migraciones
-- =============================================================================
-- NO CAMBIA NADA. Solo mira y dictamina. Ejecutalo ANTES de las migraciones
-- (03 a 21) sobre la base real del TESCHI, y no sigas si alguna fila dice BLOQUEA.
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

-- Lo que vive en tablas que a lo mejor no existen se averigua antes, aparte:
-- una consulta que nombra una tabla que no existe falla entera, aunque esa rama
-- no se llegue a evaluar. tipo_usuarios no esta en las bases de 01_schema y
-- configuracion no llega hasta la 13. Las cuentas de MySQL de los demas solo
-- las ve root (igual que en db/15).
SET @hay_catalogo := (SELECT COUNT(*) FROM information_schema.TABLES
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tipo_usuarios') > 0;
SET @tipo3 := 0;
SET @tipo4 := 0;
SET @sql := IF(@hay_catalogo,
    'SELECT IFNULL(SUM(idtipo_usuarios = 3), 0), IFNULL(SUM(idtipo_usuarios = 4), 0) INTO @tipo3, @tipo4 FROM tipo_usuarios',
    'SELECT 0, 0 INTO @tipo3, @tipo4');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- La 18 (encargados de laboratorio): su cuenta de MySQL y su contrasena.
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @hay_consulta := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES
                      WHERE GRANTEE LIKE '''digbit\_consulta''@%') > 0;
SET @hay_ajustes := (SELECT COUNT(*) FROM information_schema.TABLES
                     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'configuracion') > 0;
SET @clave_consulta := 0;
SET @sql := IF(@hay_ajustes,
    'SELECT COUNT(*) INTO @clave_consulta FROM configuracion WHERE clave = ''consulta_clave'' AND valor <> ''''',
    'SELECT 0 INTO @clave_consulta');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- La 20 (vigencia de cada franja): si ya se encendio en Ajustes que los cambios
-- de horario apliquen desde una fecha (configuracion.franjas_con_fecha = '1').
SET @con_fecha := 0;
SET @sql := IF(@hay_ajustes,
    'SELECT COUNT(*) INTO @con_fecha FROM configuracion WHERE clave = ''franjas_con_fecha'' AND valor = ''1''',
    'SELECT 0 INTO @con_fecha');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

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
    IF(NOT @hay_catalogo,
       'no existe',
       CONCAT('existe; tipo 3 ',
              IF(@tipo3 > 0, 'ya esta', 'falta (la 04 lo crea antes que al administrador)'),
              '; tipo 4 ',
              IF(@tipo4 > 0, 'ya esta', 'falta (la 18 lo crea; sin el no se dan de alta encargados de laboratorio)'))),
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
    'Bajas de usuarios y laboratorios (columnas activo)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME = 'activo'
          AND TABLE_NAME IN ('usuarios', 'laboratorios')) = 2,
       'ya estan', 'faltan (la 08 las crea y deja todo activo)'),
    'info'
UNION ALL SELECT
    'Codigo por profesor (usuarios.codigo_clase)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios' AND COLUMN_NAME = 'codigo_clase') > 0,
       -- Sin contar clases: antes de la 04 la tabla no existe y esta revision
       -- se pasa tambien entonces.
       'ya esta', 'falta (la 09 deja a cada profesor el codigo de su clase con mas sesiones)'),
    'info'
UNION ALL SELECT
    'Equipos sin internet y hora de llegada (registros_bitacoras)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras'
          AND COLUMN_NAME IN ('equipos_sin_internet', 'recibido_en')) = 2,
       'ya estan', 'faltan (la 10 las anade; copia la tabla de bitacoras: fuera de clase)'),
    'info'
UNION ALL SELECT
    'Alta de alumnos desde el laboratorio (fk_tipo_usuario por defecto)',
    IFNULL((SELECT CONCAT('por defecto ', COLUMN_DEFAULT,
                          IF(COLUMN_DEFAULT = '1', ' (ya esta)', ' (la 10 lo pone en 1)'))
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios' AND COLUMN_NAME = 'fk_tipo_usuario'
              AND COLUMN_DEFAULT IS NOT NULL),
           'sin valor por defecto (la 10 lo pone en 1)'),
    'info'
UNION ALL SELECT
    'Alumno sin grupo (carrera_grupo_semestre)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'carrera_grupo_semestre'
          AND COLUMN_NAME IN ('grupos_idgrupos', 'semestres_idsemestres') AND IS_NULLABLE = 'NO') = 0,
       'ya se admite', 'grupo y semestre obligatorios: no se puede dar de alta a ningun alumno (la 10 los hace opcionales)'),
    'info'
UNION ALL SELECT
    'Modo de acceso a las clases (laboratorios.modo_codigo)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'laboratorios' AND COLUMN_NAME = 'modo_codigo') > 0,
       'ya esta', 'falta (la 11 la anade con todos los laboratorios en codigo fijo)'),
    'info'
UNION ALL SELECT
    'Una sesion por clase y dia (UNIQUE de codigos_accesos)',
    IF((SELECT COUNT(*) FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND NON_UNIQUE = 0
          AND SEQ_IN_INDEX = 1 AND COLUMN_NAME IN ('horarios_idhorarios', 'horario_excepciones_id')) >= 2,
       'ok', 'AVISO: falta uno; sin el no actives la generacion de codigos'),
    'info'
UNION ALL SELECT
    'Color de cada profesor (usuarios.color_horario)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios' AND COLUMN_NAME = 'color_horario') > 0,
       'ya esta', 'falta (la 12 la anade con todos en automatico; copia la tabla usuarios: fuera de clase)'),
    'info'
UNION ALL SELECT
    'Recuperar la contrasena por correo (recuperacion_contrasena y configuracion)',
    IF((SELECT COUNT(*) FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('recuperacion_contrasena', 'configuracion')) = 2,
       'ya esta', 'falta (la 13 crea las dos tablas; no toca nada mas)'),
    'info'
UNION ALL SELECT
    'Uso libre: sesiones sin clase (codigos_accesos sin profesor, materia ni grupo)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND IS_NULLABLE = 'YES'
          AND COLUMN_NAME IN ('usuarios_idusuarios', 'materias_id_materia', 'grupos_idgrupos')) = 3,
       'ya esta',
       CONCAT('falta (la 14 los deja opcionales',
              IF((SELECT COUNT(*) FROM information_schema.STATISTICS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND INDEX_NAME = 'PRIMARY') > 1,
                 ' y deja la clave primaria solo en idcodigos_accesos', ''),
              '; copia la tabla de sesiones: fuera de clase)')),
    'info'
UNION ALL SELECT
    'Encargados de laboratorio: cuenta de solo lectura (digbit_consulta)',
    IF(@hay_consulta, 'ya existe',
       IF(@ve_cuentas, 'falta (la 18 la crea, con root; sin ella los encargados no pueden entrar)',
          'no se sabe: este usuario no ve las cuentas de MySQL (ejecuta esta revision con root)')),
    'info'
UNION ALL SELECT
    'Encargados de laboratorio: su contrasena en configuracion (consulta_clave)',
    IF(NOT @hay_ajustes, 'falta (primero la 13, que crea configuracion; luego la 18, con root)',
       IF(@clave_consulta > 0, 'ya esta', 'falta (la 18 la guarda al crear la cuenta, con root)')),
    'info'
UNION ALL SELECT
    'Personalizacion de las clases (personalizacion_clases)',
    IF((SELECT COUNT(*) FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'personalizacion_clases') > 0,
       'ya esta', 'falta (la 19 crea la tabla; no toca nada mas: se puede aplicar en horario de clase)'),
    'info'
UNION ALL SELECT
    'Vigencia propia de cada franja (horarios.vigente_desde / vigente_hasta)',
    IF((SELECT COUNT(*) FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'horarios'
          AND COLUMN_NAME IN ('vigente_desde', 'vigente_hasta')) = 2,
       IF(@con_fecha > 0, 'ya esta; los cambios de horario ya aplican desde una fecha (Ajustes)',
          'ya esta; en Ajustes sigue apagado que los cambios de horario apliquen desde una fecha'),
       'falta (la 20 anade dos columnas vacias a horarios, tabla pequena: no cambia nada hasta encenderlo en Ajustes, despues de actualizar TODOS los equipos)'),
    'info'
UNION ALL SELECT
    'Historial de cambios (historial_cambios)',
    IF((SELECT COUNT(*) FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'historial_cambios') > 0,
       'ya esta',
       'falta (la 21 crea la tabla; no toca nada mas: se puede aplicar en horario de clase; con root, ademas deja leerla a digbit_consulta)'),
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
