-- =============================================================================
-- DigLab 2.0 - Migracion: recuperar la contrasena con un codigo por correo
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- El alumno (o el profesor) que olvido su contrasena pulsa "Olvidaste tu
-- contrasena?" en el inicio de sesion de DigLab, tambien en el kiosco. La API
-- de recuperacion (carpeta api/, en el hosting con PHP de la escuela) le manda
-- a su correo un codigo de 6 cifras que vale 15 minutos; con el, en DigLab,
-- pone una contrasena nueva. Esta tabla guarda esos codigos.
--
-- Que hace:
--   recuperacion_contrasena: un renglon por cada codigo pedido. No guarda el
--   codigo sino su HMAC-SHA256 con un secreto que solo tiene la API (config.php),
--   asi que quien lea la tabla no puede usarlo. Cada codigo se usa una vez,
--   caduca y admite pocos intentos. Tambien quedan las peticiones de matriculas
--   que no existen (usuario_id NULL): con ellas la API limita por IP.
--
--   configuracion: ajustes de toda la escuela que DigLab lee de la base, para
--   no tener que tocar cada equipo (con Deep Freeze habria que descongelarlos
--   uno por uno). Por ahora, la direccion de la API y la clave que le manda
--   DigLab: las pone el administrador en DigLab (Ajustes, arriba en su panel)
--   y valen al momento en todos los equipos con DigLab 1.1 (los de una version
--   anterior hay que actualizarlos una vez, descongelados).
--
-- La tabla recuperarpassword que tiene la base de la escuela es del sistema
-- anterior: esta migracion no la toca ni la usa.
--
-- PERMISOS: los equipos con digbit_equipo (db/07) solo necesitan LEER
-- configuracion. Si esto lo ejecuta root, aqui se les da; si no, sale un AVISO
-- para que la aplique root (la 07 de esta version tambien lo da). NUNCA les des
-- INSERT ni UPDATE en ella: un alumno cambiaria la direccion por la de una
-- pagina suya. recuperacion_contrasena no la tocan. La API se conecta con su
-- propio usuario de MySQL; al final, en comentarios, como crearlo.
--
-- ORDEN: da igual. Crea dos tablas nuevas y, si la ejecuta root, da ese permiso
-- de lectura. No cambia ningun dato ni bloquea ninguna tabla.
--
-- Repetible: lo que ya esta, lo dice y no lo toca.
--
-- Uso:
--   mysql -u root -p < db/13_recuperacion_contrasena.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS recuperacion_contrasena (
    idrecuperacion  INT          NOT NULL AUTO_INCREMENT,
    -- De quien es; NULL si la matricula no existe (solo cuenta para el limite por IP).
    usuario_id      INT          NULL,
    -- Lo que se tecleo, tal cual (para los limites y para revisar abusos).
    matricula       VARCHAR(45)  NOT NULL,
    -- HMAC-SHA256 (hexadecimal) del codigo con el secreto de la API; NULL si no se envio codigo.
    codigo_hash     CHAR(64)     NULL,
    -- HMAC del correo al que salio: si la cuenta cambia de correo, el codigo deja de valer.
    correo_hash     CHAR(64)     NULL,
    creado_en       DATETIME     NOT NULL,
    expira_en       DATETIME     NOT NULL,
    -- Codigos equivocados tecleados para este renglon.
    intentos        INT          NOT NULL DEFAULT 0,
    -- Cuando se uso (o se anulo porque se pidio otro, o porque se cambio la contrasena).
    usado_en        DATETIME     NULL,
    ip              VARCHAR(45)  NULL,
    -- El equipo que lo pidio ("numero @ laboratorio", lo manda DigLab): limite por equipo.
    equipo          VARCHAR(60)  NULL,
    PRIMARY KEY (idrecuperacion),
    KEY ix_recuperacion_usuario (usuario_id, creado_en),
    KEY ix_recuperacion_ip (ip, creado_en),
    KEY ix_recuperacion_equipo (equipo, creado_en),
    KEY ix_recuperacion_creado (creado_en),
    CONSTRAINT fk_recuperacion_usuario
        FOREIGN KEY (usuario_id) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Si la tabla ya estaba de una version anterior de esta migracion (solo en
-- pruebas), le faltan estas columnas e indices: se anaden.
SET @falta := (SELECT COUNT(*) = 0 FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'recuperacion_contrasena' AND COLUMN_NAME = 'correo_hash');
SET @sql := IF(@falta, 'ALTER TABLE recuperacion_contrasena ADD COLUMN correo_hash CHAR(64) NULL AFTER codigo_hash',
                       'SELECT ''recuperacion_contrasena.correo_hash ya existe'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @falta := (SELECT COUNT(*) = 0 FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'recuperacion_contrasena' AND COLUMN_NAME = 'equipo');
SET @sql := IF(@falta, 'ALTER TABLE recuperacion_contrasena ADD COLUMN equipo VARCHAR(60) NULL AFTER ip, ADD KEY ix_recuperacion_equipo (equipo, creado_en)',
                       'SELECT ''recuperacion_contrasena.equipo ya existe'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @falta := (SELECT COUNT(*) = 0 FROM information_schema.STATISTICS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'recuperacion_contrasena' AND INDEX_NAME = 'ix_recuperacion_creado');
SET @sql := IF(@falta, 'ALTER TABLE recuperacion_contrasena ADD KEY ix_recuperacion_creado (creado_en)',
                       'SELECT ''ix_recuperacion_creado ya existe'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

CREATE TABLE IF NOT EXISTS configuracion (
    -- 'recuperacion_url': la direccion de api/recuperar.php (https).
    -- 'recuperacion_clave': la clave_api del config.php de la API.
    clave  VARCHAR(64)   NOT NULL,
    valor  VARCHAR(500)  NULL,
    PRIMARY KEY (clave)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Los equipos que entran con digbit_equipo (db/07) tienen que poder leer los
-- ajustes, y nada mas: sin eso, en ellos "Olvidaste tu contrasena?" dice que
-- no esta activada. Se da aqui si quien ejecuta esto ve esa cuenta y puede dar
-- permisos (root). Si no, sale un AVISO. Nunca se escribe a mano el origen de
-- la cuenta: en MariaDB 5.5, un GRANT a un 'usuario'@'origen' que no existe la
-- crearia SIN contrasena.
SET @equipo := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                WHERE GRANTEE LIKE '''digbit\_equipo''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
-- Las cuentas de los demas solo las ve quien puede leer la base mysql.
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @sql := CASE
    WHEN @equipo IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT ON `', DATABASE(), '`.configuracion TO ', @equipo)
    WHEN @equipo IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/13), ',
               'o que ejecute: GRANT SELECT ON `', DATABASE(), '`.configuracion TO ', REPLACE(@equipo, '''', ''''''),
               '; (es la cuenta que ya existe).'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_equipo: no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07), pide a root que aplique esta misma migracion (db/13): le dara a esa cuenta permiso para leer configuracion. Si entran con un usuario que ya lee toda la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'recuperacion_contrasena y configuracion listas' AS resultado,
       (SELECT COUNT(*) FROM recuperacion_contrasena) AS codigos_guardados,
       (SELECT COUNT(*) FROM configuracion WHERE clave = 'recuperacion_url' AND valor <> '') AS direccion_puesta;

-- Usuario de MySQL para la API. Lo crea quien administra el servidor (root),
-- como los de db/07, y es lo que va en bd_usuario del config.php de la API.
-- NO uses digbit_equipo (no puede tocar recuperacion_contrasena) y evita el
-- usuario que lo puede todo: ese config.php vive en un hosting web. Con estos
-- permisos puede cambiar la contrasena de cualquier alumno o profesor (es su
-- trabajo), pero no leer las contrasenas guardadas, ni tocar otras tablas, ni
-- cambiar a un administrador si esta el disparador de db/07. Cambia la
-- contrasena y el origen ('%' = cualquiera; mejor la IP del hosting) en TODAS
-- las lineas, y quitales los "-- ".
--
-- La primera, en MariaDB 5.5 (la de la escuela) y MySQL 5.7: con ella, un GRANT
-- con un origen distinto falla en vez de crear otra cuenta SIN contrasena. En
-- MySQL 8 no hace falta (ni existe): saltatela.
-- SET SESSION sql_mode = 'NO_AUTO_CREATE_USER';
-- CREATE USER 'digbit_api'@'%' IDENTIFIED BY 'CAMBIA_ESTA_CONTRASENA';
-- GRANT SELECT (idusuarios, numero_identificador, nombre, correo, fk_tipo_usuario)
--       ON teschi_otru.usuarios TO 'digbit_api'@'%';
-- GRANT UPDATE (password) ON teschi_otru.usuarios TO 'digbit_api'@'%';
-- GRANT SELECT, INSERT, UPDATE ON teschi_otru.recuperacion_contrasena TO 'digbit_api'@'%';
-- Y que pueda borrar las solicitudes de mas de 30 dias: recuperar.php las borra
-- al atender cada una (ya no cuentan para ningun limite y guardan la matricula,
-- la IP y el equipo de quien pidio). Sin este permiso no borra nada, la
-- recuperacion funciona igual y "Probar" de Ajustes lo dice:
-- GRANT DELETE ON teschi_otru.recuperacion_contrasena TO 'digbit_api'@'%';
-- Con la migracion 08 (bajas) tambien:
-- GRANT SELECT (activo) ON teschi_otru.usuarios TO 'digbit_api'@'%';
-- Y que no pueda acaparar las conexiones de la base de la escuela (las mismas
-- que usan los equipos). En MariaDB 5.5:
-- GRANT USAGE ON *.* TO 'digbit_api'@'%' WITH MAX_USER_CONNECTIONS 10;
-- En MySQL 8, en su lugar:
-- ALTER USER 'digbit_api'@'%' WITH MAX_USER_CONNECTIONS 10;
