-- =============================================================================
-- DigBit 2.0 - Reponer la clave foranea que la migracion 04 quita un momento
-- =============================================================================
-- La 04 tiene que quitar el UNIQUE de codigos_accesos.codigo, y para eso quita
-- antes la clave foranea que otra tabla (en el TESCHI, reportess) tiene hacia
-- esa columna, y la vuelve a poner despues. Si entre medias algo falla, mysql
-- se detiene y la clave se queda quitada. Paso en el servidor real: algo que el
-- usuario de la aplicacion no puede ver tambien se apoyaba en ese indice.
--
-- La 04 apunta la definicion de la clave en digbit_clave_pendiente ANTES de
-- quitarla. Esto la lee y la repone, identica. migrar_bd.ps1 lo corre solo en
-- cuanto una migracion falla; tambien se puede pegar tal cual en phpMyAdmin.
-- Se puede repetir: si no hay nada pendiente, no hace nada.
--
-- Uso:
--   mysql -u USUARIO -p < db/04b_reparar_clave_pendiente.sql
-- =============================================================================

USE teschi_otru;

-- (bloque IDENTICO al de la 04: si cambias uno, cambia el otro)
CREATE TABLE IF NOT EXISTS digbit_clave_pendiente (
    nombre     VARCHAR(64) NOT NULL,
    tabla      VARCHAR(64) NOT NULL,
    columna    VARCHAR(64) NOT NULL,
    al_borrar  VARCHAR(20) NOT NULL,
    al_cambiar VARCHAR(20) NOT NULL,
    PRIMARY KEY (nombre)
) ENGINE=InnoDB;
SET @p_nombre  := (SELECT nombre     FROM digbit_clave_pendiente LIMIT 1);
SET @p_tabla   := (SELECT tabla      FROM digbit_clave_pendiente WHERE nombre = @p_nombre);
SET @p_columna := (SELECT columna    FROM digbit_clave_pendiente WHERE nombre = @p_nombre);
SET @p_borrar  := (SELECT al_borrar  FROM digbit_clave_pendiente WHERE nombre = @p_nombre);
SET @p_cambiar := (SELECT al_cambiar FROM digbit_clave_pendiente WHERE nombre = @p_nombre);
SET @p_falta := (@p_nombre IS NOT NULL AND
                 (SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS
                  WHERE CONSTRAINT_SCHEMA = DATABASE()
                    AND CONSTRAINT_NAME = @p_nombre AND TABLE_NAME = @p_tabla) = 0);
SET @comprobar_claves := @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;
SET @sql := IF(@p_falta,
    CONCAT('ALTER TABLE `', @p_tabla, '` ADD CONSTRAINT `', @p_nombre,
           '` FOREIGN KEY (`', @p_columna, '`) REFERENCES codigos_accesos (codigo)',
           ' ON DELETE ', @p_borrar, ' ON UPDATE ', @p_cambiar),
    'SELECT ''ninguna clave pendiente de reponer'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET FOREIGN_KEY_CHECKS = @comprobar_claves;
SELECT IF(@p_falta, CONCAT('REPUESTA la clave ', @p_tabla, '.', @p_nombre,
                           ' que un intento anterior dejo quitada'),
          'nada que reponer') AS reparacion;
DELETE FROM digbit_clave_pendiente;
-- (fin del bloque identico)

DROP TABLE IF EXISTS digbit_clave_pendiente;
