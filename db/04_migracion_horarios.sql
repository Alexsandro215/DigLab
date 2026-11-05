-- =============================================================================
-- DigLab 2.0 - Migracion fase 6 (horarios por laboratorio) para una base que
-- YA existe
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de la fase 6, no necesitas
-- este script: todo viene incluido.
--
-- Que hace:
--   1) Da de alta al administrador como usuario real (tipo 3). EDITA LA
--      CONTRASENA antes de ejecutar.
--   2) Crea las tablas clases, horarios y horario_excepciones.
--   3) Convierte codigos_accesos en "sesion de una clase en una fecha": quita
--      la unicidad del codigo (se repite cada semana) y enlaza cada sesion con
--      su franja o su excepcion. Las filas que ya existen no se tocan: quedan
--      como historial y sus bitacoras y PDF siguen funcionando.
--
-- Uso:
--   mysql -u root -p < db/04_migracion_horarios.sql
-- =============================================================================

USE teschi_otru;

-- -----------------------------------------------------------------------------
-- 0) El tipo de usuario 3. En la base del TESCHI existe la tabla tipo_usuarios
--    con solo 1 alumno y 2 profesor, y usuarios.fk_tipo_usuario tiene clave
--    foranea hacia ella: sin esta fila, el INSERT del administrador de abajo
--    falla y la migracion se queda a medias. Las bases hechas con 01_schema.sql
--    no tienen esa tabla, asi que solo se toca si existe.
-- -----------------------------------------------------------------------------
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tipo_usuarios');
SET @sql := IF(@hay = 1,
    'INSERT INTO tipo_usuarios (idtipo_usuarios, tipo_usuarios)
     SELECT 3, ''administrador'' FROM DUAL
     WHERE NOT EXISTS (SELECT 1 FROM tipo_usuarios WHERE idtipo_usuarios = 3)',
    'SELECT ''no hay tabla tipo_usuarios'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- -----------------------------------------------------------------------------
-- 1) Administrador real. Cambia 'CAMBIAME' por la contrasena definitiva.
--    El hash se siembra en MD5 porque desde SQL no se puede generar el formato
--    nuevo. No pasa nada: la aplicacion lo acepta y lo reemplaza por PBKDF2 la
--    primera vez que el administrador entre (ver docs/fase7-contrasenas.md).
--
--    FROM DUAL no sobra: MariaDB 5.5, que es lo que tiene el TESCHI, no acepta
--    un SELECT con WHERE y sin FROM (error 1064). MySQL 8 si, y por eso no se
--    vio hasta ensayarlo contra una copia de su base.
-- -----------------------------------------------------------------------------
INSERT INTO usuarios
    (numero_identificador, nombre, apellido_paterno, apellido_materno, correo, password, fk_tipo_usuario)
SELECT 'ADMIN001', 'Administrador', 'DigLab', '', NULL, MD5('CAMBIAME'), 3
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM usuarios WHERE numero_identificador = 'ADMIN001');

-- -----------------------------------------------------------------------------
-- 2) Tablas nuevas (identicas a db/01_schema.sql)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS clases (
    idclases            INT         NOT NULL AUTO_INCREMENT,
    usuarios_idusuarios INT         NOT NULL,
    grupos_idgrupos     INT         NOT NULL,
    materias_id_materia INT         NOT NULL,
    codigo              VARCHAR(16) NOT NULL,
    vigente_desde       DATE        NOT NULL,
    vigente_hasta       DATE        NOT NULL,
    activa              TINYINT(1)  NOT NULL DEFAULT 1,
    creada_por          INT         NULL,
    creada_en           DATETIME    NOT NULL,
    PRIMARY KEY (idclases),
    UNIQUE KEY uq_clases_codigo (codigo),
    KEY ix_clases_profesor (usuarios_idusuarios),
    KEY ix_clases_grupo (grupos_idgrupos),
    CONSTRAINT fk_clases_profesor
        FOREIGN KEY (usuarios_idusuarios) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_clases_grupo
        FOREIGN KEY (grupos_idgrupos) REFERENCES grupos (idgrupos)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_clases_materia
        FOREIGN KEY (materias_id_materia) REFERENCES materias (id_materia)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_clases_creador
        FOREIGN KEY (creada_por) REFERENCES usuarios (idusuarios)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS horarios (
    idhorarios                  INT     NOT NULL AUTO_INCREMENT,
    clases_idclases             INT     NOT NULL,
    laboratorios_idlaboratorios INT     NOT NULL,
    dia_semana                  TINYINT NOT NULL,
    hora_inicio                 TIME    NOT NULL,
    hora_fin                    TIME    NOT NULL,
    PRIMARY KEY (idhorarios),
    KEY ix_horarios_lab_dia (laboratorios_idlaboratorios, dia_semana),
    KEY ix_horarios_clase (clases_idclases),
    CONSTRAINT chk_horarios_dia   CHECK (dia_semana BETWEEN 1 AND 7),
    CONSTRAINT chk_horarios_horas CHECK (hora_fin > hora_inicio),
    CONSTRAINT fk_horarios_clase
        FOREIGN KEY (clases_idclases) REFERENCES clases (idclases)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_horarios_laboratorio
        FOREIGN KEY (laboratorios_idlaboratorios) REFERENCES laboratorios (idlaboratorios)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS horario_excepciones (
    idhorario_excepciones       INT                       NOT NULL AUTO_INCREMENT,
    tipo                        ENUM('cancelada','extra') NOT NULL,
    fecha                       DATE                      NOT NULL,
    horarios_idhorarios         INT                       NULL,
    clases_idclases             INT                       NULL,
    laboratorios_idlaboratorios INT                       NULL,
    hora_inicio                 TIME                      NULL,
    hora_fin                    TIME                      NULL,
    motivo                      VARCHAR(200)              NULL,
    creada_por                  INT                       NULL,
    creada_en                   DATETIME                  NOT NULL,
    PRIMARY KEY (idhorario_excepciones),
    UNIQUE KEY uq_excepcion_cancelada (horarios_idhorarios, fecha),
    KEY ix_excepciones_fecha (fecha),
    KEY ix_excepciones_clase (clases_idclases),
    CONSTRAINT fk_excepciones_horario
        FOREIGN KEY (horarios_idhorarios) REFERENCES horarios (idhorarios)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_excepciones_clase
        FOREIGN KEY (clases_idclases) REFERENCES clases (idclases)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_excepciones_laboratorio
        FOREIGN KEY (laboratorios_idlaboratorios) REFERENCES laboratorios (idlaboratorios)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_excepciones_creador
        FOREIGN KEY (creada_por) REFERENCES usuarios (idusuarios)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -----------------------------------------------------------------------------
-- 2b) Mismo juego de caracteres que las tablas que ya existen.
--     Las tres de arriba no lo declaran, asi que toman el de la BASE. En el
--     TESCHI la base es latin1 y todas sus tablas son utf8: las nuevas saldrian
--     latin1, los acentos se romperian, y cualquier consulta que compare texto
--     entre una tabla nueva y una vieja daria "Illegal mix of collations". No se
--     pone utf8 a pelo porque una base hecha con 01_schema.sql es utf8mb4: se
--     copia el cotejamiento de codigos_accesos, sea el que sea.
-- -----------------------------------------------------------------------------
SET @cotejo := (SELECT TABLE_COLLATION FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos');
SET @juego := (SELECT CHARACTER_SET_NAME FROM information_schema.COLLATIONS
               WHERE COLLATION_NAME = @cotejo);

SET @sql := CONCAT('ALTER TABLE clases CONVERT TO CHARACTER SET ', @juego, ' COLLATE ', @cotejo);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := CONCAT('ALTER TABLE horarios CONVERT TO CHARACTER SET ', @juego, ' COLLATE ', @cotejo);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := CONCAT('ALTER TABLE horario_excepciones CONVERT TO CHARACTER SET ', @juego, ' COLLATE ', @cotejo);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- -----------------------------------------------------------------------------
-- 3) codigos_accesos pasa a ser "sesion"
-- -----------------------------------------------------------------------------
-- 3a) Quitar la unicidad de 'codigo'. El indice se llama como lo nombro quien
--     creo la base, asi que NO se pone el nombre a mano: se busca. Con el
--     nombre fijo, una base real donde se llame de otra forma daba error 1091
--     justo aqui, despues de haber creado ya las tres tablas de arriba, y
--     dejaba la migracion a medias.
--
--     Y hay un segundo obstaculo. En el TESCHI, reportess tiene una clave
--     foranea que apunta a codigos_accesos.codigo, y InnoDB no deja borrar el
--     indice que la sostiene (error 1553). En MySQL 8 basta con crear antes un
--     indice normal sobre la misma columna: la clave se pasa a el. En MariaDB
--     5.5, que es lo que tiene el TESCHI, NO: aunque el otro indice exista, se
--     sigue negando. Se comprobo ensayando contra una copia de su base.
--
--     Asi que se hace en tres pasos: se quita la clave foranea, se quita el
--     indice unico y se vuelve a poner la clave IDENTICA (mismo nombre, misma
--     columna, mismas reglas ON DELETE / ON UPDATE), que ahora se apoya en el
--     indice normal. La definicion se lee de information_schema, no se escribe
--     a mano. Al volver a ponerla se desactiva la comprobacion de claves un
--     momento: asi no se revalidan las filas existentes, y reportess queda
--     exactamente como estaba, tambien si ya traia alguna referencia rota.
--
--     Con UNA clave asi es lo que hay en el TESCHI. Si hubiera mas de una, no
--     se toca nada de este paso y el script lo dice: rehacerlas a ciegas de una
--     en una sin bucles, fuera de un procedimiento, es pedir un estado a medias.
--
--     Y NO se puede dejar a medias. En el servidor real el paso 2 fallo (1553)
--     DESPUES de que el paso 1 hubiera quitado la clave de reportess: algo que
--     el usuario de la aplicacion no puede ver (una clave de otra base del mismo
--     servidor, o una huerfana en el diccionario de InnoDB) tambien se apoya en
--     ese indice. mysql se detiene en el primer error, el paso 3 no llego a
--     correr, y reportess se quedo sin su clave. Por eso, antes de quitarla, su
--     definicion se apunta en digbit_clave_pendiente. Si algo falla,
--     migrar_bd.ps1 corre 04b_reparar_clave_pendiente.sql, que la repone en el
--     acto; y este mismo bloque la repone al empezar, si se corre a mano.
--     Lo de abajo es IDENTICO a 04b: si cambias uno, cambia el otro.
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
-- (fin del bloque identico a 04b)

SET @hay := (SELECT COUNT(*) FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos'
               AND INDEX_NAME = 'ix_codigos_codigo');
SET @sql := IF(@hay = 0,
    'ALTER TABLE codigos_accesos ADD KEY ix_codigos_codigo (codigo)',
    'SELECT ''ix_codigos_codigo ya existia'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT INDEX_NAME AS indice_unico_sobre_codigo
FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos'
  AND COLUMN_NAME = 'codigo' AND NON_UNIQUE = 0;

SET @idx := (SELECT INDEX_NAME FROM information_schema.STATISTICS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos'
               AND COLUMN_NAME = 'codigo' AND NON_UNIQUE = 0
             LIMIT 1);

-- Las claves foraneas de OTRAS tablas que apuntan a codigos_accesos.codigo.
SET @n_fk := (SELECT COUNT(*) FROM information_schema.KEY_COLUMN_USAGE
              WHERE REFERENCED_TABLE_SCHEMA = DATABASE()
                AND REFERENCED_TABLE_NAME = 'codigos_accesos'
                AND REFERENCED_COLUMN_NAME = 'codigo');
SET @fk_nombre := (SELECT CONSTRAINT_NAME FROM information_schema.KEY_COLUMN_USAGE
                   WHERE REFERENCED_TABLE_SCHEMA = DATABASE()
                     AND REFERENCED_TABLE_NAME = 'codigos_accesos'
                     AND REFERENCED_COLUMN_NAME = 'codigo' LIMIT 1);
SET @fk_tabla := (SELECT TABLE_NAME FROM information_schema.KEY_COLUMN_USAGE
                  WHERE REFERENCED_TABLE_SCHEMA = DATABASE()
                    AND REFERENCED_TABLE_NAME = 'codigos_accesos'
                    AND REFERENCED_COLUMN_NAME = 'codigo' LIMIT 1);
SET @fk_columna := (SELECT COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE
                    WHERE REFERENCED_TABLE_SCHEMA = DATABASE()
                      AND REFERENCED_TABLE_NAME = 'codigos_accesos'
                      AND REFERENCED_COLUMN_NAME = 'codigo' LIMIT 1);
SET @fk_al_borrar := (SELECT DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS
                      WHERE CONSTRAINT_SCHEMA = DATABASE()
                        AND CONSTRAINT_NAME = @fk_nombre AND TABLE_NAME = @fk_tabla);
SET @fk_al_cambiar := (SELECT UPDATE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS
                       WHERE CONSTRAINT_SCHEMA = DATABASE()
                         AND CONSTRAINT_NAME = @fk_nombre AND TABLE_NAME = @fk_tabla);

-- Lo que se va a hacer, antes de hacerlo.
SELECT @idx AS indice_unico, @n_fk AS claves_que_apuntan_a_codigo,
       CONCAT(@fk_tabla, '.', @fk_columna, ' (', @fk_nombre, ') ON DELETE ',
              @fk_al_borrar, ' ON UPDATE ', @fk_al_cambiar) AS clave_a_rehacer;

-- Antes de quitarla, apuntarla, para poder reponerla si el paso 2 falla.
INSERT INTO digbit_clave_pendiente (nombre, tabla, columna, al_borrar, al_cambiar)
SELECT @fk_nombre, @fk_tabla, @fk_columna, @fk_al_borrar, @fk_al_cambiar
FROM DUAL
WHERE @idx IS NOT NULL AND @n_fk = 1;

-- Paso 1: quitar la clave foranea (solo si hay unico que quitar y es UNA).
SET @sql := IF(@idx IS NOT NULL AND @n_fk = 1,
    CONCAT('ALTER TABLE `', @fk_tabla, '` DROP FOREIGN KEY `', @fk_nombre, '`'),
    'SELECT ''sin clave foranea que rehacer'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- Paso 2: quitar el indice unico.
SET @sql := IF(@idx IS NULL,
    'SELECT ''no habia indice unico sobre codigo'' AS resultado',
    IF(@n_fk > 1,
        CONCAT('SELECT ''NO se quita ', @idx, ': hay ', @n_fk,
               ' claves foraneas apuntando a codigo. Rehazlas a mano y repite.'' AS resultado'),
        CONCAT('ALTER TABLE codigos_accesos DROP INDEX `', @idx, '`')));
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- Paso 3: volver a poner la clave foranea identica.
SET @comprobar_claves := @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;
SET @sql := IF(@idx IS NOT NULL AND @n_fk = 1,
    CONCAT('ALTER TABLE `', @fk_tabla, '` ADD CONSTRAINT `', @fk_nombre,
           '` FOREIGN KEY (`', @fk_columna, '`) REFERENCES codigos_accesos (codigo)',
           ' ON DELETE ', @fk_al_borrar, ' ON UPDATE ', @fk_al_cambiar),
    'SELECT ''sin clave foranea que rehacer'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET FOREIGN_KEY_CHECKS = @comprobar_claves;

-- Salio bien: ya no hay nada pendiente.
DELETE FROM digbit_clave_pendiente;
DROP TABLE IF EXISTS digbit_clave_pendiente;

-- 3b) Columnas, indices y claves nuevas. Solo si faltan, para poder repetir el
--     script sin que corte. Las filas viejas quedan con las dos columnas en
--     NULL, y un UNIQUE en MySQL admite tantos NULL como haga falta, asi que el
--     historial no estorba.
SET @hay := (SELECT COUNT(*) FROM information_schema.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos'
               AND COLUMN_NAME = 'horarios_idhorarios');
SET @sql := IF(@hay = 0,
    'ALTER TABLE codigos_accesos
        ADD COLUMN horarios_idhorarios    INT NULL AFTER usuarios_idusuarios,
        ADD COLUMN horario_excepciones_id INT NULL AFTER horarios_idhorarios,
        ADD UNIQUE KEY uq_sesion_franja_fecha (horarios_idhorarios, fecha),
        ADD UNIQUE KEY uq_sesion_excepcion (horario_excepciones_id),
        ADD CONSTRAINT fk_codigos_horario
            FOREIGN KEY (horarios_idhorarios) REFERENCES horarios (idhorarios)
            ON DELETE SET NULL ON UPDATE CASCADE,
        ADD CONSTRAINT fk_codigos_excepcion
            FOREIGN KEY (horario_excepciones_id) REFERENCES horario_excepciones (idhorario_excepciones)
            ON DELETE SET NULL ON UPDATE CASCADE',
    'SELECT ''codigos_accesos ya estaba convertida'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- -----------------------------------------------------------------------------
-- Comprobacion: deben aparecer las tres tablas nuevas y las dos columnas.
-- -----------------------------------------------------------------------------
SHOW TABLES LIKE 'clases';
SHOW TABLES LIKE 'horario%';
SHOW COLUMNS FROM codigos_accesos LIKE 'horario%';
SELECT numero_identificador, fk_tipo_usuario FROM usuarios WHERE fk_tipo_usuario = 3;
