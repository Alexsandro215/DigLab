-- =============================================================================
-- DigLab 2.0 - Migracion: encargado de laboratorio (tipo 4) y su cuenta de
-- MySQL de solo consulta
-- =============================================================================
-- El encargado de laboratorio (y quien le ayuda del servicio social) entra al
-- panel del administrador en SOLO CONSULTA: ve los laboratorios en vivo, las
-- alertas, las bitacoras, los alumnos, los profesores, los laboratorios y los
-- horarios, pero no cambia nada. Puede entrar desde CUALQUIER equipo con
-- DigLab: al reconocerlo, DigLab deja la cuenta de MySQL del equipo y hace toda
-- su sesion con digbit_consulta, que solo puede leer. Asi no depende solo de la
-- pantalla: la base le rechaza cualquier cambio.
--
-- Hace tres cosas:
--   1) El tipo 4 en tipo_usuarios, si la base tiene ese catalogo (la del
--      TESCHI lo tiene, con clave foranea: sin la fila, dar de alta a un
--      encargado falla con el error 1452). Las bases de 01_schema no lo tienen.
--   2) La cuenta digbit_consulta: solo SELECT en las tablas que ensena el panel
--      del administrador. Ni INSERT, ni UPDATE, ni DELETE.
--   3) Su contrasena (aleatoria) en configuracion, para que cualquier equipo la
--      encuentre al entrar un encargado. No abre nada nuevo: la puede leer quien
--      ya lee configuracion (los equipos), y con ella solo se lee lo que esos
--      equipos ya leen.
--
-- PERMISOS: crear la cuenta y darle permisos lo hace root. Con otro usuario
-- (por ejemplo teschi_otru) se hace solo el paso 1 y se avisa.
--
-- ORDEN: 1) esta migracion (con root); 2) actualizar DigLab en los equipos
-- donde entraran los encargados; 3) darlos de alta en Profesores > Encargados
-- de laboratorio. Un DigLab anterior no conoce el tipo 4: avisa "Tipo de
-- usuario no manejado" y se queda sin ventana. No le da ningun permiso, pero
-- que no entren en esos equipos.
--
-- Repetible: si la contrasena ya esta en configuracion, se conserva; los
-- permisos se vuelven a dejar justo en solo lectura.
--
-- Uso:
--   mysql -u root -p < db/18_encargados.sql
-- =============================================================================

USE teschi_otru;

-- -----------------------------------------------------------------------------
-- 1) El tipo 4 en el catalogo, si lo hay. FROM DUAL no sobra: MariaDB 5.5 no
--    acepta un SELECT con WHERE sin tabla.
-- -----------------------------------------------------------------------------
SET @hay_catalogo := (SELECT COUNT(*) FROM information_schema.TABLES
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tipo_usuarios');
SET @sql := IF(@hay_catalogo = 1,
    'INSERT INTO tipo_usuarios (idtipo_usuarios, tipo_usuarios)
     SELECT 4, ''encargado de laboratorio'' FROM DUAL
     WHERE NOT EXISTS (SELECT 1 FROM tipo_usuarios WHERE idtipo_usuarios = 4)',
    'SELECT ''no hay tabla tipo_usuarios: el tipo 4 se usa sin migrar'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- -----------------------------------------------------------------------------
-- 2) La cuenta digbit_consulta (solo root)
-- -----------------------------------------------------------------------------
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @puede := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES
               WHERE GRANTEE = @yo AND PRIVILEGE_TYPE = 'CREATE USER') > 0;
SET @hay_ajustes := (SELECT COUNT(*) FROM information_schema.TABLES
                     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'configuracion') > 0;
SET @hacer := @puede AND @hay_ajustes;

-- La contrasena: la que ya este guardada, o una nueva al azar de 24 caracteres.
SET @sql := IF(@hay_ajustes,
    'SELECT valor INTO @clave FROM configuracion WHERE clave = ''consulta_clave''',
    'SELECT NULL INTO @clave');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SET @clave := IF(@clave IS NULL OR @clave = '',
                 SUBSTRING(SHA1(CONCAT(RAND(), NOW(), UUID(), CONNECTION_ID())), 1, 24), @clave);
SET @desde := '%';

-- Como en db/07, segun la version:
--   MySQL 5.7+ y MariaDB 10.2+: CREATE USER IF NOT EXISTS, ALTER USER y
--     REVOKE ALL PRIVILEGES, GRANT OPTION.
--   MariaDB 5.5 (la de la escuela): GRANT USAGE ... IDENTIFIED BY (la crea si
--     no existe), DROP USER (se lleva todos sus permisos) y otra vez GRANT USAGE.
SET @es_mariadb := VERSION() LIKE '%MariaDB%';
SET @version := SUBSTRING_INDEX(VERSION(), '-', 1);
SET @mayor := CAST(SUBSTRING_INDEX(@version, '.', 1) AS UNSIGNED);
SET @menor := CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(@version, '.', 2), '.', -1) AS UNSIGNED);
SET @moderno := IF(@es_mariadb, @mayor > 10 OR (@mayor = 10 AND @menor >= 2),
                                @mayor > 5 OR (@mayor = 5 AND @menor >= 7));
SET @usuario := CONCAT('''digbit_consulta''@''', @desde, '''');
SET @identificado := CONCAT(' IDENTIFIED BY ''', @clave, '''');
SET @nada := 'SELECT 1 FROM DUAL WHERE 1 = 0';

SET @sql := IF(NOT @hacer, @nada,
            IF(@moderno, CONCAT('CREATE USER IF NOT EXISTS ', @usuario, @identificado),
                         CONCAT('GRANT USAGE ON *.* TO ', @usuario, @identificado)));
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(NOT @hacer, @nada,
            IF(@moderno, CONCAT('ALTER USER ', @usuario, @identificado),
                         CONCAT('DROP USER ', @usuario)));
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(NOT @hacer, @nada,
            IF(@moderno, CONCAT('REVOKE ALL PRIVILEGES, GRANT OPTION FROM ', @usuario),
                         CONCAT('GRANT USAGE ON *.* TO ', @usuario, @identificado)));
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Solo leer lo que ensena el panel del administrador, tabla por tabla, y solo
-- las que existen (reportes_profesor, asistencias_retiradas, historial_cambios
-- y asistencias_dadas llegan con db/15, db/17, db/21 y db/22, y las de las
-- configuraciones de horario con db/16). Nunca: recuperacion_contrasena ni
-- tipo_usuarios.
SET @tablas := 'carreras,grupos,semestres,materias,laboratorios,clases,horarios,horario_excepciones,codigos_accesos,registros_bitacoras,usuarios,carrera_grupo_semestre,configuracion,reportes_profesor,asistencias_retiradas,historial_cambios,asistencias_dadas,ciclos_preliminares,ciclo_profesores,ciclo_cargas,ciclo_franjas,ciclo_recortes,siia_solicitudes,horas_por_asignar';
SET @i := 0;
SET @sql := IF(@hacer AND FIND_IN_SET('carreras', @tablas) > 0 AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'carreras') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.carreras TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'grupos') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.grupos TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'semestres') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.semestres TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'materias') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.materias TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'laboratorios') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.laboratorios TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'clases') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.clases TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'horarios') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.horarios TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'horario_excepciones') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.horario_excepciones TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.codigos_accesos TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.registros_bitacoras TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.usuarios TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'carrera_grupo_semestre') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.carrera_grupo_semestre TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'configuracion') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.configuracion TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'reportes_profesor') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.reportes_profesor TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'asistencias_retiradas') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.asistencias_retiradas TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
-- El historial de cambios (db/21): las alertas de cambios de horario y su ventana.
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'historial_cambios') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.historial_cambios TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
-- Las asistencias que da el profesor sin bitacora (db/22): Faltas, el historial
-- del alumno y Estadisticas.
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'asistencias_dadas') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.asistencias_dadas TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
-- Las configuraciones de horario (db/16 y db/23): la persona responsable ve las
-- guardadas y las aplicadas en Horarios > Configuraciones y Aplicacion.
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclos_preliminares') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.ciclos_preliminares TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclo_profesores') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.ciclo_profesores TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclo_cargas') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.ciclo_cargas TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclo_franjas') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.ciclo_franjas TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ciclo_recortes') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.ciclo_recortes TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
-- Quien espera que lo enlacen con el SIIA (db/24): la persona responsable lo ve, no enlaza.
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'siia_solicitudes') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.siia_solicitudes TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
-- Las horas que faltan por asignar (db/25): la persona responsable ve "Falta asignar", no la cambia.
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'horas_por_asignar') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.horas_por_asignar TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
-- Las fallas que ya se resolvieron (db/26): la persona responsable las ve, no las marca.
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fallas_resueltas') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.fallas_resueltas TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
-- Las suspensiones de acceso (db/27): la persona responsable las ve, no las cambia.
SET @sql := IF(@hacer AND (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'suspensiones') > 0,
    CONCAT('GRANT SELECT ON teschi_otru.suspensiones TO ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hacer, 'FLUSH PRIVILEGES', @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -----------------------------------------------------------------------------
-- 3) Donde la encuentran los equipos
-- -----------------------------------------------------------------------------
SET @sql := IF(@hacer,
    CONCAT('INSERT INTO configuracion (clave, valor) VALUES (''consulta_usuario'', ''digbit_consulta''), (''consulta_clave'', ''', @clave, ''') ',
           'ON DUPLICATE KEY UPDATE valor = VALUES(valor)'),
    @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -----------------------------------------------------------------------------
-- 4) Comprobacion
-- -----------------------------------------------------------------------------
SELECT CASE
    WHEN NOT @hay_ajustes THEN 'AVISO: falta la tabla configuracion (db/13). Aplica antes db/13 y vuelve a correr esta.'
    WHEN NOT @puede THEN 'AVISO: este usuario no puede crear cuentas. Pide a root que aplique esta misma migracion: sin digbit_consulta los encargados no pueden entrar.'
    ELSE 'digbit_consulta lista: solo lectura; su contrasena quedo en configuracion (consulta_clave)'
END AS resultado;
SET @sql := IF(@hacer, CONCAT('SHOW GRANTS FOR ', @usuario), @nada);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hay_catalogo = 1,
    'SELECT idtipo_usuarios AS tipo, tipo_usuarios AS nombre FROM tipo_usuarios ORDER BY idtipo_usuarios',
    'SELECT ''sin tabla tipo_usuarios'' AS tipo');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;
SELECT 'encargados de laboratorio' AS resultado,
       (SELECT COUNT(*) FROM usuarios WHERE fk_tipo_usuario = 4) AS cuantos;
