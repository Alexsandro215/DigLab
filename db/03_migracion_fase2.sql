-- =============================================================================
-- DigBit 2.0 - Migracion fase 2 para una base que YA existe
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de la fase 2, no necesitas
-- este script: la restriccion ya viene incluida.
--
-- Que hace: impide que un alumno registre dos bitacoras con el mismo codigo.
-- La aplicacion traduce el error 1062 resultante a "ya registraste tu bitacora".
--
-- Si la base YA trae duplicados, el UNIQUE no se anade y el script lo dice, en
-- vez de fallar. Es el caso de la base del TESCHI: tiene pares repetidos de
-- 2024 que no se borran, porque son registros reales. Para los registros nuevos
-- la regla la hace cumplir la propia aplicacion: sus dos INSERT llevan un
-- WHERE NOT EXISTS (Consultas.cs y ColaBitacoras.cs), asi que con o sin UNIQUE
-- un alumno no puede registrar dos veces con el mismo codigo.
--
-- Uso:
--   mysql -u root -p < db/03_migracion_fase2.sql
-- =============================================================================

USE teschi_otru;

-- 1) Duplicados que ya hubiera. Si esta consulta devuelve filas, el UNIQUE no
--    se anade (ver arriba). No hace falta borrar nada.
SELECT fk_codigo_accesos, fk_usuario, COUNT(*) AS repeticiones
FROM registros_bitacoras
GROUP BY fk_codigo_accesos, fk_usuario
HAVING COUNT(*) > 1;

-- 2) La restriccion. Solo ADD: en la base real el indice que sostiene la FK de
--    fk_codigo_accesos se llama como lo nombro quien la creo, y un DROP INDEX
--    con un nombre que no existe (error 1091) tumba tambien el ADD en el mismo
--    ALTER. Un indice redundante sobre fk_codigo_accesos no estorba.
--    Se anade solo si no esta ya, para que repetir el script no de error 1061 y
--    corte una migracion a medias; y solo si no hay duplicados, porque con ellos
--    el ALTER falla con 1062 y dejaba la migracion parada aqui.
SET @hay := (SELECT COUNT(*) FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras'
               AND INDEX_NAME = 'uq_bitacora_codigo_usuario');
SET @dup := (SELECT COUNT(*) FROM (SELECT 1 FROM registros_bitacoras
                                   GROUP BY fk_codigo_accesos, fk_usuario
                                   HAVING COUNT(*) > 1) d);
SET @sql := IF(@hay > 0,
    'SELECT ''uq_bitacora_codigo_usuario ya existia'' AS resultado',
    IF(@dup > 0,
        CONCAT('SELECT ''NO se anade el UNIQUE: hay ', @dup,
               ' pares duplicados de antes. La aplicacion hace cumplir la regla al insertar.'' AS resultado'),
        'ALTER TABLE registros_bitacoras ADD UNIQUE KEY uq_bitacora_codigo_usuario (fk_codigo_accesos, fk_usuario)'));
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- 3) Comprobacion: debe aparecer uq_bitacora_codigo_usuario con Non_unique = 0.
SHOW INDEX FROM registros_bitacoras WHERE Key_name = 'uq_bitacora_codigo_usuario';
