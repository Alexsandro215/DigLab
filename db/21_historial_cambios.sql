-- =============================================================================
-- DigBit 2.0 - Migracion: historial de cambios (quien cambio que y cuando)
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas: la tabla ya esta, y el permiso de digbit_consulta lo da la 18,
-- que en una base nueva hace falta igual.
--
-- Hasta ahora, cambiar el horario (franjas y clases), cancelar una clase un
-- dia, poner o quitar una sesion extra, corregir una bitacora o cambiar un
-- laboratorio y su forma de entrar dejaba, como mucho, una linea en el log del
-- equipo del administrador: en la base no quedaba quien lo hizo, cuando ni que
-- decia antes, y quitar una cancelacion o una sesion extra borraba la fila sin
-- dejar rastro.
--
-- Con esta migracion, DigBit anota cada uno de esos cambios en
-- historial_cambios en la MISMA transaccion que el cambio: si el cambio queda,
-- su renglon tambien, y si falla, ninguno. Con ella:
--   - Alertas avisa de los cambios de horario que hizo OTRA persona ("Cambio de
--     horario", tambien en la notificacion de Windows), y
--   - Alertas > "Historial de cambios..." los ensena todos, con lo que decia
--     antes, en solo lectura.
-- Nunca se cambia ni se borra un renglon: solo se anaden.
--
-- PERMISOS:
--   digbit_admin (db/07): ya puede (SELECT, INSERT, UPDATE y DELETE en toda la
--     base).
--   digbit_consulta (db/18): solo leerla, para que el encargado de laboratorio
--     vea las alertas de cambios y el historial. Se le da aqui si quien ejecuta
--     esto puede dar permisos (root); si no, sale un AVISO. La 18 de esta
--     version tambien la incluye.
--   digbit_equipo (db/07): nada. Los equipos de los laboratorios y de los
--     profesores no hacen ninguno de estos cambios.
--
-- ORDEN: da igual. Sin ella DigBit funciona como antes (conexion/Esquema.cs);
-- con ella dejan rastro los cambios hechos desde un DigBit de esta version (el
-- del equipo del administrador). Crea una tabla nueva y no toca ninguna otra:
-- se puede aplicar en horario de clase.
--
-- MariaDB 5.5 (la de la escuela): "cuando" no lleva DEFAULT (alli no se admite
-- DEFAULT CURRENT_TIMESTAMP en un DATETIME); lo pone DigBit con NOW() del
-- servidor, en la zona de DigBit (-06:00), como las demas fechas.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/21_historial_cambios.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS historial_cambios (
    idhistorial_cambios  INT           NOT NULL AUTO_INCREMENT,
    -- Cuando, con el reloj del servidor.
    cuando               DATETIME      NOT NULL,
    -- Quien: su cuenta (usuarios.idusuarios) y su numero tal como entro
    -- (numero_identificador), por si un dia la cuenta ya no esta. Sin claves
    -- foraneas en toda la tabla: el historial no se borra con nada.
    usuario_id           INT           NULL,
    usuario              VARCHAR(45)   NULL,
    -- Que: 'franja', 'clase', 'cancelacion', 'extra', 'bitacora', 'laboratorio'
    -- o 'forma_de_entrar'.
    tipo                 VARCHAR(20)   NOT NULL,
    -- 'alta', 'cambio', 'fin', 'quitar', 'recorte', 'activar' o 'desactivar'.
    accion               VARCHAR(20)   NOT NULL,
    -- La tabla y la fila que cambio (puede ya no existir).
    tabla                VARCHAR(40)   NOT NULL,
    registro_id          INT           NULL,
    -- El laboratorio y el dia a los que se refiere, si se refiere a uno.
    laboratorio_id       INT           NULL,
    fecha                DATE          NULL,
    -- Lo que paso, en una linea, como lo lee el administrador.
    descripcion          VARCHAR(500)  NOT NULL,
    -- Lo que decia antes y lo que dice ahora, un dato por linea ("Dia: Martes");
    -- NULL en un alta (antes) o al quitar (despues). Nunca el codigo de una
    -- clase: el encargado de laboratorio tambien lo lee.
    antes                TEXT          NULL,
    despues              TEXT          NULL,
    PRIMARY KEY (idhistorial_cambios),
    KEY ix_historial_cambios_cuando (cuando),
    KEY ix_historial_cambios_tipo (tipo, cuando),
    KEY ix_historial_cambios_registro (tabla, registro_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permiso de digbit_consulta para leerla. Nunca se escribe a mano el origen
-- de la cuenta: en MariaDB 5.5, un GRANT a un 'usuario'@'origen' que no existe
-- la crearia SIN contrasena (igual que en db/13 y db/19).
SET @consulta := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                  WHERE GRANTEE LIKE '''digbit\_consulta''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @sql := CASE
    WHEN @consulta IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT ON `', DATABASE(), '`.historial_cambios TO ', @consulta)
    WHEN @consulta IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/21), ',
               'o que ejecute: GRANT SELECT ON `', DATABASE(), '`.historial_cambios TO ', REPLACE(@consulta, '''', ''''''),
               '; (es la cuenta que ya existe). Sin eso, los encargados de laboratorio no ven los cambios de horario.'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_consulta (db/18): no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si hay encargados de laboratorio (db/18), pide a root que aplique esta misma migracion (db/21): le dara a digbit_consulta permiso para leer historial_cambios.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- digbit_equipo a proposito no recibe nada (ver PERMISOS arriba).

SELECT 'historial_cambios lista' AS resultado,
       (SELECT COUNT(*) FROM historial_cambios) AS renglones,
       (SELECT DATE_FORMAT(MAX(cuando), '%Y-%m-%d %H:%i') FROM historial_cambios) AS ultimo_cambio;
