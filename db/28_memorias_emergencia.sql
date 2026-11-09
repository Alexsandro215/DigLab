-- =============================================================================
-- DigLab 2.0 - Migracion: memorias de emergencia registradas en la base
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas: la tabla ya esta, y el permiso de digbit_equipo lo da la 07.
--
-- El "Acceso de emergencia" de los equipos del laboratorio abre el escritorio sin
-- bitacora con una memoria USB, reconocida por el numero de serie de su firmware.
-- Hasta ahora la memoria se autorizaba AL INSTALAR cada equipo, con ella
-- conectada, y cada equipo aceptaba solo la suya (pedido del 2026-11-07: "para
-- que se instale rapido y no dependa de 1 memoria").
--
-- Con esta migracion, la administracion las registra en DigLab (Laboratorios >
-- "Memorias de emergencia...", con la memoria conectada a su computadora) y
-- CUALQUIERA de las registradas abre CUALQUIER equipo del laboratorio:
--   - En la base va el numero de serie HASHEADO (PBKDF2, como las contrasenas),
--     nunca en claro, con un nombre para reconocerla y el modelo de la memoria.
--   - Quitar una no borra el renglon: lleva quitada_en. Deja de abrir equipos.
--   - Cada equipo guarda la lista en C:\DigLab (donde un estudiante no puede
--     escribir) para cuando no hay servidor: la copia al instalar y la renueva el
--     servicio DigLab.Vigilante cada 10 minutos. Con servidor manda la base.
--   - Mientras no haya ninguna registrada, cada equipo sigue aceptando la que se
--     autorizo al instalarlo, si la tiene.
-- Cada alta y cada baja quedan en el historial (db/21), sin el numero de serie.
--
-- PERMISOS:
--   digbit_admin (db/07): ya puede (SELECT, INSERT, UPDATE y DELETE en toda la
--     base). Solo la administracion registra y quita.
--   digbit_equipo (db/07): SOLO LEERLA. Nunca le des INSERT ni UPDATE: sus
--     credenciales las puede leer cualquier estudiante sentado en el equipo, y con
--     ellas registraria su propia memoria. Sin el permiso de leer, los equipos no
--     ven la lista: siguen con la memoria que se autorizo al instalarlos.
--   digbit_consulta (db/18): nada. La persona responsable no la necesita.
--   Se le da aqui si quien ejecuta esto puede dar permisos (root); si no, sale un
--   AVISO. La 07 de esta version tambien lo incluye.
--
-- ORDEN: da igual. Sin ella DigLab funciona como antes (conexion/Esquema.cs): no
-- hay "Memorias de emergencia..." y cada equipo acepta la memoria que se autorizo
-- al instalarlo. Crea una tabla nueva y no toca ninguna otra: se puede aplicar en
-- horario de clase. OJO: un equipo con una version de DigLab anterior a esta NO
-- mira la lista: hay que actualizar los equipos.
--
-- MariaDB 5.5 (la de la escuela): las fechas no llevan DEFAULT (alli no se admite
-- DEFAULT CURRENT_TIMESTAMP en un DATETIME); las pone DigLab con NOW() del
-- servidor, en la zona de DigLab (-06:00), como las demas.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/28_memorias_emergencia.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS memorias_emergencia (
    idmemorias_emergencia INT          NOT NULL AUTO_INCREMENT,
    -- Para reconocerla: "Llavero de la responsable de CISCO".
    nombre               VARCHAR(80)   NOT NULL,
    -- Lo que dice Windows de ella (modelo y tamano), para saber cual es en la mano.
    modelo               VARCHAR(120)  NULL,
    -- El numero de serie del firmware, hasheado con PBKDF2 (conexion/Contrasenas.cs).
    huella               VARCHAR(255)  NOT NULL,
    agregada_en          DATETIME      NOT NULL,
    agregada_por         INT           NULL,
    -- Si se quito: cuando y quien. Abre equipos mientras quitada_en sea NULL.
    quitada_en           DATETIME      NULL,
    quitada_por          INT           NULL,
    PRIMARY KEY (idmemorias_emergencia),
    KEY idx_memorias_emergencia_vigentes (quitada_en)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permisos. Nunca se escribe a mano el origen de una cuenta: en MariaDB 5.5, un
-- GRANT a un 'usuario'@'origen' que no existe la crearia SIN contrasena (igual
-- que en db/13, db/19, db/21, db/25, db/26 y db/27).
SET @equipo := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                WHERE GRANTEE LIKE '''digbit\_equipo''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @nada := 'SELECT 1 FROM DUAL WHERE 1 = 0';

SET @sql := IF(@equipo IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT ON `', DATABASE(), '`.memorias_emergencia TO ', @equipo), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @sql := CASE
    WHEN @equipo IS NOT NULL AND @puede_dar THEN
        'SELECT ''permisos de memorias_emergencia: digbit_equipo solo lee'' AS resultado'
    WHEN @equipo IS NOT NULL THEN
        'SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/28). Sin eso, los equipos que usan digbit_equipo no ven las memorias registradas y siguen solo con la que se autorizo al instalarlos.'' AS resultado'
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_equipo: no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07), pide a root que aplique esta misma migracion (db/28): sin eso no ven las memorias registradas. Si entran con un usuario que ya puede todo en la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'memorias_emergencia lista' AS resultado,
       (SELECT COUNT(*) FROM memorias_emergencia WHERE quitada_en IS NULL) AS registradas;
