-- =============================================================================
-- DigLab 2.0 - Migracion: actualizar los equipos desde la base
-- =============================================================================
-- Hasta ahora, cada version nueva de DigLab se instalaba equipo por equipo con
-- DigLab-Setup.exe en una memoria. Con esta migracion (pedido del 2026-11-08 y
-- 2026-11-13: "avisa que hay una nueva actualizacion y se actualiza sola al
-- aceptar"):
--   - La administracion PUBLICA el instalador nuevo desde DigLab (Laboratorios >
--     "Versión de DigLab..."): va a la base partido en trozos, con su huella
--     SHA-256, y pide la contrasena de quien lo publica.
--   - Los equipos de administracion y de docentes, al abrir DigLab, avisan que
--     hay una version nueva y, si se acepta, la instalan y vuelven a abrir DigLab.
--   - Los equipos del laboratorio (kiosco) se actualizan solos: el servicio
--     DigLab.Vigilante revisa cada 10 minutos y, cuando nadie usa el equipo, la
--     descarga, comprueba la huella, la instala en silencio y reinicia.
--   - Una version se puede RETIRAR (retirada_en): deja de ofrecerse. No se borra
--     el renglon; sus trozos se borran al retirarla para no ocupar la base.
--   - Solo cuenta una version COMPLETA (completa = 1): si una publicacion se corta
--     a medias, ningun equipo la ve.
--
-- PERMISOS:
--   digbit_admin (db/07): ya puede (SELECT, INSERT, UPDATE y DELETE en toda la
--     base). Solo la administracion publica y retira.
--   digbit_equipo (db/07): SOLO LEER las tres tablas. Nunca le des INSERT ni UPDATE: sus
--     credenciales las puede leer cualquier estudiante sentado en el equipo, y con
--     ellas publicaria un instalador suyo que los equipos correrian como SYSTEM.
--   digbit_consulta (db/18): nada.
--   Se le da aqui si quien ejecuta esto puede dar permisos (root); si no, sale un
--   AVISO.
--
-- ORDEN: da igual. Sin ella DigLab funciona como antes: no hay "Versión de
-- DigLab..." y nadie busca versiones nuevas. Crea dos tablas nuevas y no toca
-- ninguna otra: se puede aplicar en horario de clase. OJO: un equipo con una
-- version de DigLab anterior a esta NO busca versiones nuevas: hay que llevarle
-- esta version una vez, como siempre.
--
-- TAMANO: el instalador pesa unos 12 MB y va en trozos de hasta 512 KB (o la
-- mitad de max_allowed_packet, si el servidor lo tiene mas chico).
--
-- MariaDB 5.5 (la de la escuela): las fechas no llevan DEFAULT; las pone DigLab
-- con NOW() del servidor, en la zona de DigLab (-06:00), como las demas.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/29_actualizaciones.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS actualizaciones (
    idactualizaciones    INT           NOT NULL AUTO_INCREMENT,
    -- La version del instalador (la de DigLab.exe): "1.2.2.0".
    version              VARCHAR(20)   NOT NULL,
    -- Solo para mostrarlo: "DigLab-Setup.exe".
    archivo              VARCHAR(120)  NOT NULL,
    tamano               BIGINT        NOT NULL,
    -- SHA-256 del instalador en hexadecimal: cada equipo la comprueba antes de correrlo.
    huella_sha256        CHAR(64)      NOT NULL,
    trozos               INT           NOT NULL,
    notas                VARCHAR(500)  NULL,
    -- 0 mientras se suben los trozos: ningun equipo la ve hasta que esta entera.
    completa             TINYINT(1)    NOT NULL DEFAULT 0,
    publicada_en         DATETIME      NOT NULL,
    publicada_por        INT           NULL,
    retirada_en          DATETIME      NULL,
    retirada_por         INT           NULL,
    PRIMARY KEY (idactualizaciones),
    KEY idx_actualizaciones_vigentes (completa, retirada_en)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS actualizaciones_trozos (
    idactualizaciones    INT           NOT NULL,
    numero               INT           NOT NULL,
    datos                MEDIUMBLOB    NOT NULL,
    PRIMARY KEY (idactualizaciones, numero),
    CONSTRAINT fk_trozos_actualizacion FOREIGN KEY (idactualizaciones)
        REFERENCES actualizaciones (idactualizaciones) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Ordenes de la administracion a los equipos del laboratorio (pedido del 2026-11-13: "se
-- puede actualizar todo un laboratorio desde admin? Solo es mandar la señal"). Hoy solo
-- 'actualizar': cada kiosco libre de ese laboratorio (o de todos, con laboratorio NULL)
-- instala ya la version vigente, sin esperar su revision de 10 minutos. Caducan
-- (caduca_en): un equipo que estaba apagado no la cumple al encenderse otro dia. Pensada
-- para crecer (apagar o reiniciar desde la administracion, pendiente 1n).
CREATE TABLE IF NOT EXISTS ordenes_equipos (
    idordenes_equipos    INT           NOT NULL AUTO_INCREMENT,
    tipo                 VARCHAR(20)   NOT NULL,
    laboratorios_idlaboratorios INT    NULL,
    creada_en            DATETIME      NOT NULL,
    creada_por           INT           NULL,
    caduca_en            DATETIME      NOT NULL,
    PRIMARY KEY (idordenes_equipos),
    KEY idx_ordenes_equipos_vigentes (tipo, caduca_en)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permisos. Nunca se escribe a mano el origen de una cuenta: en MariaDB 5.5, un
-- GRANT a un 'usuario'@'origen' que no existe la crearia SIN contrasena (igual
-- que en db/13, db/19, db/21 y db/25 a db/28).
SET @equipo := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                WHERE GRANTEE LIKE '''digbit\_equipo''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @nada := 'SELECT 1 FROM DUAL WHERE 1 = 0';

SET @sql := IF(@equipo IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT ON `', DATABASE(), '`.actualizaciones TO ', @equipo), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := IF(@equipo IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT ON `', DATABASE(), '`.actualizaciones_trozos TO ', @equipo), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @sql := IF(@equipo IS NOT NULL AND @puede_dar,
    CONCAT('GRANT SELECT ON `', DATABASE(), '`.ordenes_equipos TO ', @equipo), @nada);
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @sql := CASE
    WHEN @equipo IS NOT NULL AND @puede_dar THEN
        'SELECT ''permisos de actualizaciones: digbit_equipo solo lee'' AS resultado'
    WHEN @equipo IS NOT NULL THEN
        'SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/29). Sin eso, los equipos que usan digbit_equipo no ven las versiones publicadas y no se actualizan solos.'' AS resultado'
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_equipo: no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07), pide a root que aplique esta misma migracion (db/29): sin eso no se actualizan solos. Si entran con un usuario que ya puede todo en la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SELECT 'actualizaciones lista' AS resultado,
       (SELECT COUNT(*) FROM actualizaciones WHERE completa = 1 AND retirada_en IS NULL) AS publicadas;
