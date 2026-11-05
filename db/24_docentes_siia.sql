-- =============================================================================
-- DigLab 2.0 - Migracion: docentes que entran con el SIIA del TESCHI
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas: la columna y la tabla ya estan, y los permisos los dan db/07 y
-- db/18.
--
-- Con "Inicio de sesion de docentes con el SIIA" encendido en Ajustes, el
-- docente entra con su numero de empleado y su contrasena del SIIA. Para que
-- un inicio de sesion correcto del SIIA abra una cuenta de DigLab, la
-- administracion tiene que ENLAZAR antes ese numero con el docente de DigLab
-- (el que tiene sus clases): DigLab no crea ni enlaza cuentas solo. Esta
-- migracion anade:
--   1) usuarios.siia_enlazado_en (DATETIME, NULL): cuando la administracion
--      enlazo la cuenta de ese docente con su numero de empleado del SIIA. Con
--      NULL no esta enlazada: el SIIA no la abre y su huella no vale para entrar
--      sin el SIIA. Al enlazar, numero_identificador pasa a ser el numero de
--      empleado.
--   2) siia_solicitudes: los numeros de empleado que el SIIA acepto y que nadie
--      ha enlazado todavia (pendiente), con el nombre y el correo que dio el
--      SIIA, el primer y el ultimo intento. NUNCA la contrasena. La
--      administracion la ve en Alertas ("Docentes por enlazar") y en Docentes,
--      y la enlaza con un docente de la lista o la descarta.
--
-- Con "Los estudiantes entran con el SIIA" (el otro interruptor de Ajustes), la
-- cuenta de un estudiante que el SIIA acepta se crea sola desde el equipo, como
-- en "Crear cuenta" (sin fk_tipo_usuario: alumno), y deja su renglon en el
-- historial de cambios (db/21).
--
-- PERMISOS:
--   digbit_admin (db/07): ya puede (SELECT, INSERT, UPDATE y DELETE en toda la
--     base). Solo la administracion enlaza, desenlaza y descarta.
--   digbit_equipo (db/07): leer siia_solicitudes, anadir la de quien entra y
--     actualizar solo su nombre, su correo, el ultimo intento, los intentos y
--     que vuelva a estar pendiente. NUNCA usuarios.siia_enlazado_en (no tiene
--     permiso en esa columna): desde un equipo no se enlaza a nadie. Y anadir
--     (solo INSERT) renglones al historial de cambios, para el alta de un
--     estudiante con el SIIA: antes no tenia nada en esa tabla. No puede
--     cambiarlos ni borrarlos, pero si inventar alguno (como las bitacoras).
--   digbit_consulta (db/18): solo leerla (la persona responsable del
--     laboratorio ve quien espera, pero no enlaza).
--   Se dan aqui si quien ejecuta esto puede dar permisos (root); si no, sale un
--   AVISO. Lo que se acepta: quien lea connections.config de un equipo podria
--   anadir solicitudes falsas a la lista (no enlazan nada: eso lo decide la
--   administracion).
--
-- ORDEN: se puede aplicar en cualquier momento, tambien en horario de clase:
-- no cambia nada para nadie hasta que se encienda en Ajustes, y eso se hace
-- cuando TODOS los equipos tengan esta version de DigLab. MariaDB 5.5 copia
-- usuarios al anadir la columna (es una tabla chica).
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/24_docentes_siia.sql
-- =============================================================================

USE teschi_otru;

-- 1) Cuando se enlazo cada docente con el SIIA (NULL: sin enlazar).
SET @hay := (SELECT COUNT(*) FROM information_schema.COLUMNS
             WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios' AND COLUMN_NAME = 'siia_enlazado_en');
SET @sql := IF(@hay = 0,
    'ALTER TABLE usuarios ADD COLUMN siia_enlazado_en DATETIME NULL',
    'SELECT ''usuarios.siia_enlazado_en ya existe: no se toca'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- 2) Los que entraron con el SIIA y esperan que los enlacen.
CREATE TABLE IF NOT EXISTS siia_solicitudes (
    idsiia_solicitudes  INT           NOT NULL AUTO_INCREMENT,
    -- El numero de empleado con que entro (el "usuario" del SIIA).
    numero_empleado     VARCHAR(45)   NOT NULL,
    -- Lo que dio el SIIA, para reconocerlo: nombre, apellidos, correo y tipo
    -- ('P' docente, 'A' administrativo, 'E' estudiante). Nunca la contrasena.
    nombre              VARCHAR(100)  NULL,
    apellido_paterno    VARCHAR(100)  NULL,
    apellido_materno    VARCHAR(100)  NULL,
    nombre_completo     VARCHAR(255)  NULL,
    correo              VARCHAR(120)  NULL,
    tipo_siia           VARCHAR(4)    NULL,
    -- Reloj del servidor (NOW() de DigLab, -06:00). Sin DEFAULT: 5.5 no lo admite.
    primer_intento      DATETIME      NOT NULL,
    ultimo_intento      DATETIME      NOT NULL,
    intentos            INT           NOT NULL DEFAULT 1,
    -- pendiente | enlazada | descartada. VARCHAR y no ENUM: con sql_mode vacio un ENUM invalido seria ''.
    estado              VARCHAR(12)   NOT NULL DEFAULT 'pendiente',
    -- Con quien se enlazo y el numero que tenia antes (para desenlazar). Sin
    -- claves foraneas, como el historial: la solicitud no se borra con la cuenta.
    usuario_id          INT           NULL,
    numero_anterior     VARCHAR(45)   NULL,
    resuelta_en         DATETIME      NULL,
    resuelta_por        INT           NULL,
    PRIMARY KEY (idsiia_solicitudes),
    UNIQUE KEY uq_siia_solicitudes_numero (numero_empleado),
    KEY ix_siia_solicitudes_estado (estado, ultimo_intento)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- 3) Permisos. Nunca se escribe a mano el origen de una cuenta: en MariaDB
--    5.5, un GRANT a un 'usuario'@'origen' que no existe la crearia SIN
--    contrasena (como en db/13 y db/22).
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

-- digbit_equipo: leer, anadir y, de una que ya estaba, solo estas columnas.
SET @sql := IF(@equipo IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT, INSERT ON `', DATABASE(), '`.siia_solicitudes TO ', @equipo), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := IF(@equipo IS NOT NULL AND @puede_dar,
    CONCAT('GRANT UPDATE (nombre, apellido_paterno, apellido_materno, nombre_completo, correo, tipo_siia, ultimo_intento, intentos, estado) ON `',
           DATABASE(), '`.siia_solicitudes TO ', @equipo), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- digbit_equipo: anadir al historial (el alta de un estudiante con el SIIA). Sin db/21 no hay tabla.
SET @hay21 := (SELECT COUNT(*) FROM information_schema.TABLES
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'historial_cambios') > 0;
SET @sql := IF(@equipo IS NOT NULL AND @puede_dar AND @hay21,
    CONCAT('GRANT INSERT ON `', DATABASE(), '`.historial_cambios TO ', @equipo), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- digbit_consulta: solo leer.
SET @sql := IF(@consulta IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT ON `', DATABASE(), '`.siia_solicitudes TO ', @consulta), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @sql := CASE
    WHEN (@equipo IS NOT NULL OR @consulta IS NOT NULL) AND @puede_dar THEN
        CONCAT('SELECT ''permisos de siia_solicitudes: ', IF(@equipo IS NULL, 'sin digbit_equipo', 'digbit_equipo lee, anade y actualiza'),
               ', ', IF(@consulta IS NULL, 'sin digbit_consulta', 'digbit_consulta solo lee'), ''' AS resultado')
    WHEN @equipo IS NOT NULL OR @consulta IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/24). ',
               'Sin eso, los equipos que entran con digbit_equipo no pueden avisar a la administracion de quien espera que lo enlacen.'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuarios digbit_equipo ni digbit_consulta: no hace falta darles permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07), pide a root que aplique esta misma migracion (db/24). Si entran con un usuario que ya puede todo en la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'docentes con el SIIA: listo' AS resultado,
       (SELECT COUNT(*) FROM usuarios WHERE fk_tipo_usuario = 2 AND siia_enlazado_en IS NOT NULL) AS docentes_enlazados,
       (SELECT COUNT(*) FROM usuarios WHERE fk_tipo_usuario = 2 AND siia_enlazado_en IS NULL) AS docentes_sin_enlazar,
       (SELECT COUNT(*) FROM siia_solicitudes WHERE estado = 'pendiente') AS por_enlazar;
