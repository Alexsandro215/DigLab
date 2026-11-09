-- =============================================================================
-- DigLab 2.0 - Migracion: suspender el acceso de un estudiante por unos dias
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas: la tabla ya esta, y los permisos los dan la 07 y la 18.
--
-- Hasta ahora, para que un estudiante no entrara solo se le podia dar de baja, y
-- volvia hasta que alguien se acordara de reactivarlo (pedido del 2026-11-06).
--
-- Con esta migracion, en Estudiantes, la administracion le suspende el acceso por
-- los dias que elija y con una razon ("Suspender acceso..."):
--   - Cuenta desde ese momento: 7 dias a las 10:32 terminan a las 10:32 del dia 7.
--   - No puede iniciar sesion en DigLab, ni con el SIIA ni con su contrasena; al
--     intentarlo, despues de comprobarla, ve hasta cuando y la razon.
--   - Al vencer el plazo vuelve a entrar solo, sin que nadie haga nada.
--   - Se puede quitar antes ("Quitar suspension...").
-- Su cuenta sigue activa y no se borra nada: sus bitacoras quedan como estan y su
-- docente le puede dar asistencia. Los renglones nunca se borran (el de una
-- suspension que se quito lleva levantada_en): lo que paso se le puede entregar al
-- exportar sus datos. Al anonimizar su cuenta, la razon se vacia. Cada cambio queda
-- en el historial (db/21), sin la razon.
--
-- La razon NO va a la copia local de los equipos: esa carpeta la puede leer quien
-- se siente en el equipo. Sin servidor, un suspendido no esta en la copia (como
-- los dados de baja), asi que tampoco entra con solo su matricula.
--
-- PERMISOS:
--   digbit_admin (db/07): ya puede (SELECT, INSERT, UPDATE y DELETE en toda la
--     base). Solo la administracion suspende y quita.
--   digbit_equipo (db/07): solo leerla. ES LO QUE HACE VALER LA SUSPENSION en los
--     laboratorios: sin el permiso, ese equipo no la ve y el estudiante entra igual.
--   digbit_consulta (db/18): solo leerla (la persona responsable ve quien esta
--     suspendido y por que, no lo cambia).
--   Se les da aqui si quien ejecuta esto puede dar permisos (root); si no, sale un
--   AVISO. La 07 y la 18 de esta version tambien los incluyen.
--
-- ORDEN: da igual. Sin ella DigLab funciona como antes (conexion/Esquema.cs): no
-- hay "Suspender acceso..." y nadie queda suspendido. Crea una tabla nueva y no
-- toca ninguna otra: se puede aplicar en horario de clase. OJO: un equipo con una
-- version de DigLab anterior a esta NO mira las suspensiones: hay que actualizar
-- los equipos para que valgan en ellos.
--
-- MariaDB 5.5 (la de la escuela): las fechas no llevan DEFAULT (alli no se admite
-- DEFAULT CURRENT_TIMESTAMP en un DATETIME); las pone DigLab con NOW() del
-- servidor, en la zona de DigLab (-06:00), como las demas.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/27_suspensiones.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS suspensiones (
    idsuspensiones       INT           NOT NULL AUTO_INCREMENT,
    -- La cuenta del estudiante (usuarios.idusuarios). Sin clave foranea, como el
    -- historial: nada de esto debe impedir nada.
    usuarios_idusuarios  INT           NOT NULL,
    -- Desde cuando y hasta cuando (reloj del servidor, sin DEFAULT: 5.5 no lo admite).
    desde                DATETIME      NOT NULL,
    hasta                DATETIME      NOT NULL,
    -- Por que: la ve el estudiante al intentar entrar. Se vacia al anonimizar su cuenta.
    razon                VARCHAR(200)  NOT NULL,
    creada_por           INT           NULL,
    -- Si alguien la quito antes de tiempo: cuando y quien. Sigue vigente mientras
    -- levantada_en sea NULL y hasta no haya pasado.
    levantada_en         DATETIME      NULL,
    levantada_por        INT           NULL,
    PRIMARY KEY (idsuspensiones),
    KEY idx_suspensiones_usuario (usuarios_idusuarios, hasta)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permisos. Nunca se escribe a mano el origen de una cuenta: en MariaDB 5.5, un
-- GRANT a un 'usuario'@'origen' que no existe la crearia SIN contrasena (igual
-- que en db/13, db/19, db/21, db/25 y db/26).
SET @equipo := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                WHERE GRANTEE LIKE '''digbit\_equipo''@%' LIMIT 1);
SET @consulta := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                  WHERE GRANTEE LIKE '''digbit\_consulta''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @nada := 'SELECT 1 FROM DUAL WHERE 1 = 0';

SET @sql := IF(@equipo IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT ON `', DATABASE(), '`.suspensiones TO ', @equipo), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := IF(@consulta IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT ON `', DATABASE(), '`.suspensiones TO ', @consulta), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @sql := CASE
    WHEN (@equipo IS NOT NULL OR @consulta IS NOT NULL) AND @puede_dar THEN
        CONCAT('SELECT ''permisos de suspensiones: ', IF(@equipo IS NULL, 'sin digbit_equipo', 'digbit_equipo solo lee'),
               ', ', IF(@consulta IS NULL, 'sin digbit_consulta', 'digbit_consulta solo lee'), ''' AS resultado')
    WHEN @equipo IS NOT NULL OR @consulta IS NOT NULL THEN
        'SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/27). Sin eso, un estudiante suspendido ENTRA IGUAL en los equipos que usan digbit_equipo.'' AS resultado'
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuarios digbit_equipo ni digbit_consulta: no hace falta darles permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07) o hay encargados de laboratorio (db/18), pide a root que aplique esta misma migracion (db/27): sin eso, un estudiante suspendido entra igual en esos equipos. Si entran con un usuario que ya puede todo en la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'suspensiones lista' AS resultado, (SELECT COUNT(*) FROM suspensiones) AS suspensiones;
