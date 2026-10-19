-- =============================================================================
-- DigBit 2.0 - Migracion: bitacoras sin clase ("uso libre", modo sin codigos)
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- En un laboratorio "sin codigos" (db/11, modo_codigo = 'sin_codigo') el alumno
-- solo llena su bitacora: si hay clase, queda en esa clase; si NO hay ninguna,
-- queda como USO LIBRE del laboratorio. Toda bitacora cuelga de una sesion
-- (registros_bitacoras.fk_codigo_accesos), y una sesion de uso libre no tiene
-- clase: ni profesor, ni materia, ni grupo. Esta migracion deja esas tres
-- columnas de codigos_accesos en NULL (las sesiones que ya hay no cambian):
--
--   materias_id_materia, grupos_idgrupos, usuarios_idusuarios  ->  NULL
--
-- En la base del TESCHI esas columnas forman parte de la clave primaria
-- (idcodigos_accesos, materias_id_materia, grupos_idgrupos,
-- laboratorios_idlaboratorios, usuarios_idusuarios), y una columna de la clave
-- primaria no puede ser NULL: la clave pasa a ser solo idcodigos_accesos, que
-- ya es unico (AUTO_INCREMENT). Sus claves foraneas siguen igual, y las que
-- apuntan a esta tabla (registros_bitacoras -> idcodigos_accesos, reportess ->
-- codigo) tambien. Todo va en UNA orden ALTER TABLE: MariaDB 5.5 copia la
-- tabla una vez (con unos miles de sesiones, segundos) y la bloquea mientras.
-- Mejor aplicarla fuera de clases.
--
-- Una sesion de uso libre es la de un laboratorio y dia SIN profesor
-- (usuarios_idusuarios NULL), sin franja ni sesion extra, con el tramo libre
-- entre clases en hora_entrada y hora_salida y el codigo 'LIBRE'.
--
-- PERMISOS: ninguno nuevo. digbit_equipo (db/07) ya puede crear sesiones.
--
-- ORDEN: aplicarla no cambia nada para nadie. Los equipos con DigBit 1.1 la
-- notan solos: desde entonces, en un laboratorio sin codigos y sin clase, el
-- alumno registra uso libre (antes no podia registrar). Una version anterior
-- de DigBit no lee sesiones sin profesor: actualiza los equipos del
-- administrador y de los laboratorios antes de pasar uno a "sin codigos".
--
-- Repetible: lo que ya esta, lo dice y no lo toca.
--
-- Uso:
--   mysql -u root -p < db/14_registro_sin_clase.sql
-- =============================================================================

USE teschi_otru;

-- La clave primaria hoy: cuantas columnas tiene.
SET @columnas_pk := (SELECT COUNT(*) FROM information_schema.STATISTICS
                     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND INDEX_NAME = 'PRIMARY');

-- Cada columna que aun es NOT NULL se cambia a NULL con su mismo tipo.
SET @materia := (SELECT IF(IS_NULLABLE = 'NO', CONCAT('MODIFY materias_id_materia ', COLUMN_TYPE, ' NULL'), NULL)
                 FROM information_schema.COLUMNS
                 WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND COLUMN_NAME = 'materias_id_materia');
SET @grupo := (SELECT IF(IS_NULLABLE = 'NO', CONCAT('MODIFY grupos_idgrupos ', COLUMN_TYPE, ' NULL'), NULL)
               FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND COLUMN_NAME = 'grupos_idgrupos');
SET @profesor := (SELECT IF(IS_NULLABLE = 'NO', CONCAT('MODIFY usuarios_idusuarios ', COLUMN_TYPE, ' NULL'), NULL)
                  FROM information_schema.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND COLUMN_NAME = 'usuarios_idusuarios');

-- CONCAT_WS se salta los NULL: solo lo que falta, en una sola orden.
SET @cambios := CONCAT_WS(', ',
    IF(@columnas_pk > 1, 'DROP PRIMARY KEY, ADD PRIMARY KEY (idcodigos_accesos)', NULL),
    @materia, @grupo, @profesor);
SET @sql := IF(@cambios IS NULL OR @cambios = '',
    'SELECT ''codigos_accesos ya admite sesiones sin clase: no se toca'' AS resultado',
    CONCAT('ALTER TABLE codigos_accesos ', @cambios));
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- Como quedo.
SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos'
  AND COLUMN_NAME IN ('idcodigos_accesos', 'materias_id_materia', 'grupos_idgrupos', 'usuarios_idusuarios')
ORDER BY ORDINAL_POSITION;
SELECT GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS clave_primaria FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND INDEX_NAME = 'PRIMARY';
SELECT COUNT(*) AS sesiones, SUM(usuarios_idusuarios IS NULL) AS de_uso_libre FROM codigos_accesos;
