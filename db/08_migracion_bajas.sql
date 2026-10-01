-- =============================================================================
-- DigBit 2.0 - Migracion: dar de baja usuarios sin borrar su historial
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- Que hace: anade a usuarios la marca de baja. Un usuario dado de baja no
-- puede entrar (ni con servidor ni sin el), pero sus bitacoras, clases y
-- horarios se conservan y se le puede reactivar. Hasta ahora quitar a alguien
-- era un DELETE, y las claves foraneas se llevaban en cascada todo su
-- historial (ver ON DELETE CASCADE en db/01_schema.sql).
--
--   usuarios.activo      1 = puede entrar; 0 = dado de baja. Al aplicar esto, todos 1.
--   usuarios.baja_en     cuando se le dio de baja; NULL mientras esta activo.
--
-- Lo mismo para laboratorios: uno con historial ya no se borra (se llevaria su
-- horario y dejaria sus sesiones sin laboratorio), se desactiva. Deja de salir
-- en el panel del administrador y en Horarios; sus bitacoras se conservan.
--
--   laboratorios.activo  1 = en uso; 0 = desactivado. Al aplicar esto, todos 1.
--
-- Repetible: si las columnas ya existen, lo dice y no toca nada.
--
-- Uso:
--   mysql -u root -p < db/08_migracion_bajas.sql
-- =============================================================================

USE teschi_otru;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios'
                  AND COLUMN_NAME = 'activo');
SET @sql := IF(@existe = 0,
    'ALTER TABLE usuarios ADD COLUMN activo TINYINT(1) NOT NULL DEFAULT 1 AFTER fk_tipo_usuario',
    'SELECT ''usuarios.activo ya existe: no se toca'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios'
                  AND COLUMN_NAME = 'baja_en');
SET @sql := IF(@existe = 0,
    'ALTER TABLE usuarios ADD COLUMN baja_en DATETIME NULL AFTER activo',
    'SELECT ''usuarios.baja_en ya existe: no se toca'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'laboratorios'
                  AND COLUMN_NAME = 'activo');
SET @sql := IF(@existe = 0,
    'ALTER TABLE laboratorios ADD COLUMN activo TINYINT(1) NOT NULL DEFAULT 1 AFTER nombre_laboratorio',
    'SELECT ''laboratorios.activo ya existe: no se toca'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT SUM(activo = 1) AS usuarios_activos, SUM(activo = 0) AS usuarios_de_baja, COUNT(*) AS usuarios
FROM usuarios;
SELECT SUM(activo = 1) AS laboratorios_activos, SUM(activo = 0) AS laboratorios_desactivados, COUNT(*) AS laboratorios
FROM laboratorios;
