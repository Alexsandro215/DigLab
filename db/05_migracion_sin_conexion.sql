-- =============================================================================
-- DigLab 2.0 - Migracion fase 4 (modo sin conexion) para una base que YA existe
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de la fase 4, no la necesitas.
--
-- Que hace: marca las bitacoras que se registraron sin servidor (el alumno entro
-- solo con su matricula y la bitacora se subio despues desde la cola local del
-- equipo). Los informes las senalan con "[sin conexion]".
--
-- Uso:
--   mysql -u root -p < db/05_migracion_sin_conexion.sql
-- =============================================================================

USE teschi_otru;

-- Solo si faltan: MySQL no admite ADD COLUMN IF NOT EXISTS, y repetir el script
-- daria error 1060 y cortaria la migracion a medias.
SET @hay := (SELECT COUNT(*) FROM information_schema.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras'
               AND COLUMN_NAME = 'registrado_sin_conexion');
SET @sql := IF(@hay = 0,
    'ALTER TABLE registros_bitacoras
        ADD COLUMN registrado_sin_conexion TINYINT(1) NOT NULL DEFAULT 0 AFTER comentarios_software,
        ADD COLUMN registrado_en DATETIME NULL AFTER registrado_sin_conexion',
    'SELECT ''las columnas ya existian'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SHOW COLUMNS FROM registros_bitacoras LIKE 'registrado%';
