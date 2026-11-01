-- =============================================================================
-- DigBit 2.0 - Migracion: horas que faltan por asignar en el horario en uso
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas: la tabla ya esta, y el permiso de digbit_consulta lo da la 18,
-- que en una base nueva hace falta igual.
--
-- Hasta ahora, en Horarios > Horario semanal, poner una clase (o cambiarle las
-- horas) encima de otra del mismo laboratorio no se podia: salia "Se solapa
-- con...", y habia que quitar antes la otra a mano. Y al quitar las horas de
-- una clase que se iba a dar en otro horario no quedaba nada que recordara
-- ponerlas: se pasaba por alto (pedido del 2026-10-31, sobre todo para los
-- cambios de inicio de ciclo).
--
-- Con esta migracion:
--   - "Poner clase..." y "Cambiar horas..." encima de otra clase del MISMO
--     laboratorio la reemplazan (antes lo confirma): a la otra se le quita
--     solo lo que se cruza y esas horas quedan aqui como pendientes
--     ("desplazada"). Con "Los cambios de horario aplican desde una fecha"
--     (db/20), desde esa fecha: lo pasado no se reescribe. Un choque con la
--     misma persona docente o el mismo grupo en OTRO laboratorio sigue sin
--     dejar guardar.
--   - "Quitar horas..." pregunta si se va a dar en otro horario; si si, sus
--     horas quedan pendientes ("quitada").
--   - Horario semanal ensena "Falta asignar: N h" (todos los laboratorios) con
--     la lista; al ponerle horas a una clase con pendientes, bajan solas (lo
--     mas antiguo primero), y "Ya no hace falta" las da por resueltas.
-- Cada pendiente que se crea o se resuelve queda en el historial de cambios
-- (db/21), en la misma transaccion. Nunca se borra un renglon: se resuelve.
--
-- PERMISOS:
--   digbit_admin (db/07): ya puede (SELECT, INSERT, UPDATE y DELETE en toda la
--     base). Solo la administracion crea y resuelve pendientes.
--   digbit_consulta (db/18): solo leerla (la persona responsable del
--     laboratorio ve la pastilla y la lista, sin acciones). Se le da aqui si
--     quien ejecuta esto puede dar permisos (root); si no, sale un AVISO. La 18
--     de esta version tambien la incluye.
--   digbit_equipo (db/07): nada. Los equipos de los laboratorios y de los
--     profesores no cambian el horario.
--
-- ORDEN: da igual. Sin ella DigBit funciona como antes (conexion/Esquema.cs):
-- sin reemplazo ni pastilla. Crea una tabla nueva y no toca ninguna otra: se
-- puede aplicar en horario de clase. Solo la usa el DigBit de la
-- administracion: no hace falta actualizar los equipos.
--
-- MariaDB 5.5 (la de la escuela): las fechas no llevan DEFAULT (alli no se
-- admite DEFAULT CURRENT_TIMESTAMP en un DATETIME); las pone DigBit con NOW()
-- del servidor, en la zona de DigBit (-06:00), como las demas.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/25_horas_por_asignar.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS horas_por_asignar (
    idhoras_por_asignar  INT           NOT NULL AUTO_INCREMENT,
    -- La clase (clases.idclases) a la que le faltan horas. Sin claves foraneas,
    -- como el historial: el renglon no se borra con nada (DigBit solo ensena
    -- las de clases activas que no han terminado).
    clases_idclases      INT           NOT NULL,
    -- Lo que falta, en minutos (baja al ponerle horas a la clase), y lo que
    -- faltaba al crearse.
    minutos              INT           NOT NULL,
    minutos_originales   INT           NOT NULL,
    -- De donde salieron, para ensenarlo: el laboratorio, el dia de la semana
    -- (1 = lunes, como horarios.dia_semana) y las horas que se le quitaron.
    laboratorio_id       INT           NULL,
    dia_semana           TINYINT       NOT NULL,
    hora_inicio          TIME          NOT NULL,
    hora_fin             TIME          NOT NULL,
    -- Desde que fecha faltan (la del cambio desde una fecha, o el dia en que se hizo).
    desde                DATE          NOT NULL,
    -- 'desplazada' (otra clase se puso en su lugar) o 'quitada' ("Quitar horas"
    -- con "Si, queda en Falta asignar"). VARCHAR y no ENUM: con sql_mode vacio un
    -- ENUM invalido seria ''.
    motivo               VARCHAR(12)   NOT NULL,
    -- Quien y cuando (reloj del servidor, sin DEFAULT: 5.5 no lo admite).
    creada_en            DATETIME      NOT NULL,
    creada_por           INT           NULL,
    -- pendiente | asignada (se le pusieron horas a la clase) | no_hace_falta.
    estado               VARCHAR(14)   NOT NULL DEFAULT 'pendiente',
    resuelta_en          DATETIME      NULL,
    resuelta_por         INT           NULL,
    PRIMARY KEY (idhoras_por_asignar),
    KEY ix_horas_por_asignar_estado (estado, clases_idclases, creada_en),
    KEY ix_horas_por_asignar_clase (clases_idclases)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permiso de digbit_consulta para leerla. Nunca se escribe a mano el origen
-- de la cuenta: en MariaDB 5.5, un GRANT a un 'usuario'@'origen' que no existe
-- la crearia SIN contrasena (igual que en db/13, db/19 y db/21).
SET @consulta := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                  WHERE GRANTEE LIKE '''digbit\_consulta''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @sql := CASE
    WHEN @consulta IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT ON `', DATABASE(), '`.horas_por_asignar TO ', @consulta)
    WHEN @consulta IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/25), ',
               'o que ejecute: GRANT SELECT ON `', DATABASE(), '`.horas_por_asignar TO ', REPLACE(@consulta, '''', ''''''),
               '; (es la cuenta que ya existe). Sin eso, los encargados de laboratorio no ven las horas que faltan por asignar.'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_consulta (db/18): no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si hay encargados de laboratorio (db/18), pide a root que aplique esta misma migracion (db/25): le dara a digbit_consulta permiso para leer horas_por_asignar.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- digbit_equipo a proposito no recibe nada (ver PERMISOS arriba).

SELECT 'horas_por_asignar lista' AS resultado,
       (SELECT COUNT(*) FROM horas_por_asignar WHERE estado = 'pendiente') AS pendientes,
       (SELECT IFNULL(SUM(minutos), 0) FROM horas_por_asignar WHERE estado = 'pendiente') AS minutos_por_asignar;
