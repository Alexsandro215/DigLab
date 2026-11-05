-- =============================================================================
-- DigLab 2.0 - Migracion: cada franja con su propia vigencia
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- Hasta ahora una franja (horarios: dia, horas y laboratorio de una clase)
-- valia todas las semanas de la vigencia de su clase. Cambiarla a media
-- clase (de lunes a martes, de las 7 a las 9, de laboratorio) reescribia las
-- semanas pasadas: Faltas las juzgaba con el horario nuevo y las sesiones
-- viejas dejaban de cuadrar. Borrarla se llevaba sus cancelaciones.
--
-- Con esta migracion cada franja puede tener su propio inicio y fin
-- (vigente_desde, vigente_hasta; NULL = el de su clase). Cambiar o quitar una
-- franja que ya tiene historia ya no la toca: se le pone fin y la nueva
-- empieza en la fecha que diga el administrador. Lo pasado queda como fue.
--
-- ORDEN: aplicarla no cambia nada para nadie (las franjas de hoy quedan con
-- NULL, es decir, como estaban). DigLab solo parte franjas cuando, ademas, el
-- administrador enciende en Ajustes "Los cambios de horario aplican desde una
-- fecha" (configuracion.franjas_con_fecha = '1'): un DigLab viejo no conoce
-- estas columnas y veria a la vez la franja terminada y la nueva. Primero se
-- actualizan TODOS los equipos y despues se enciende.
--
-- PERMISOS: ninguno nuevo. Los permisos de db/07 y db/18 son por tabla.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/20_vigencia_franjas.sql
-- =============================================================================

USE teschi_otru;

SET @hay := (SELECT COUNT(*) FROM information_schema.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'horarios' AND COLUMN_NAME = 'vigente_desde');
SET @sql := IF(@hay = 0,
    'ALTER TABLE horarios ADD COLUMN vigente_desde DATE NULL AFTER hora_fin',
    'SELECT ''horarios ya tiene vigente_desde'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @hay := (SELECT COUNT(*) FROM information_schema.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'horarios' AND COLUMN_NAME = 'vigente_hasta');
SET @sql := IF(@hay = 0,
    'ALTER TABLE horarios ADD COLUMN vigente_hasta DATE NULL AFTER vigente_desde',
    'SELECT ''horarios ya tiene vigente_hasta'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'vigencia de las franjas lista' AS resultado,
       (SELECT COUNT(*) FROM horarios) AS franjas,
       (SELECT COUNT(*) FROM horarios WHERE vigente_desde IS NOT NULL OR vigente_hasta IS NOT NULL) AS con_fecha_propia;
