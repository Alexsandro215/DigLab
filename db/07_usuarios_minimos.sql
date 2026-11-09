-- =============================================================================
-- DigLab 2.0 - Dos usuarios de MySQL con permisos minimos
-- =============================================================================
-- POR QUE
--
-- Cada equipo del laboratorio lleva su connections.config con un usuario y una
-- contrasena de MySQL. Ese archivo el alumno PUEDE LEERLO: DigLab corre con su
-- propia sesion de Windows, asi que todo lo que la aplicacion necesita leer
-- para conectarse, lo puede leer el. No hay forma de esconderselo, y ocultar la
-- carpeta no sirve de nada (oculto es un atributo, no un permiso).
--
-- Por eso el ataque realista no es una inyeccion de SQL --DigLab usa parametros
-- en sus 165 consultas, se reviso una por una-- sino mucho mas simple: leer las
-- credenciales y conectarse DIRECTO a la base desde cualquier cliente, sin
-- pasar por la aplicacion. Contra eso, lo unico que sirve es que ese usuario no
-- pueda hacer gran cosa.
--
-- De ahi dos usuarios:
--   digbit_equipo : va en los ~30 equipos. Puede lo justo para que un ALUMNO
--                   registre su bitacora y un PROFESOR vea sus clases.
--   digbit_admin  : solo en la maquina del administrador, que nadie mas usa.
--
-- Hay una tercera, digbit_consulta, que NO se crea aqui: la crea
-- db/18_encargados.sql (tambien con root), solo con SELECT en las tablas que
-- ensena el panel del administrador. Con ella trabaja la sesion de un
-- encargado de laboratorio (tipo 4, solo consulta) en cualquier equipo. No va
-- en ningun connections.config: DigLab arma su cadena con el servidor del
-- equipo y la contrasena que la 18 guarda en configuracion. Volver a pasar esta
-- migracion no la toca.
--
-- COMO SE HIZO LA LISTA
--
-- No a ojo: se inventariaron las 165 sentencias SQL de la aplicacion, una por
-- una, rastreando quien llama a cada metodo para saber si la dispara el alumno,
-- el profesor o el administrador. Un permiso de menos rompe una clase entera;
-- uno de mas anula el ejercicio.
--
-- Uso:
--   mysql -u root -p < db/07_usuarios_minimos.sql
--   (edita antes las dos contrasenas de abajo)
-- =============================================================================

-- CAMBIA ESTAS DOS CONTRASENAS ANTES DE EJECUTAR.
SET @clave_equipo := 'CAMBIAME-EQUIPO';
SET @clave_admin  := 'CAMBIAME-ADMIN';

-- Y este es el rango desde el que se permite conectar. '%' es cualquier sitio:
-- con la base en la red de la escuela, restringelo a la subred de los
-- laboratorios, por ejemplo '192.168.10.%'. Es la diferencia entre que las
-- credenciales robadas sirvan desde un aula o desde cualquier parte.
SET @desde := '%';

-- Cada usuario se crea (o se le cambia la contrasena) y se deja SIN permisos,
-- por si ya existia con otros. Como depende de la version:
--   MySQL 5.7+ y MariaDB 10.2+: CREATE USER IF NOT EXISTS, ALTER USER y
--     REVOKE ALL PRIVILEGES, GRANT OPTION.
--   MariaDB 5.5 (la de la escuela): nada de eso existe o se puede preparar, asi
--     que GRANT USAGE ... IDENTIFIED BY (lo crea si no existe), DROP USER (se
--     lleva todos sus permisos) y otra vez GRANT USAGE ... IDENTIFIED BY.
-- NO borres estas lineas si dan error: con sql_mode vacio, el primer GRANT de
-- mas abajo crearia el usuario SIN contrasena.
SET @es_mariadb := VERSION() LIKE '%MariaDB%';
SET @version := SUBSTRING_INDEX(VERSION(), '-', 1);
SET @mayor := CAST(SUBSTRING_INDEX(@version, '.', 1) AS UNSIGNED);
SET @menor := CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(@version, '.', 2), '.', -1) AS UNSIGNED);
SET @moderno := IF(@es_mariadb, @mayor > 10 OR (@mayor = 10 AND @menor >= 2),
                                @mayor > 5 OR (@mayor = 5 AND @menor >= 7));
SELECT VERSION() AS servidor,
       IF(@moderno, 'CREATE USER / ALTER USER / REVOKE ALL', 'GRANT USAGE / DROP USER / GRANT USAGE') AS como_se_preparan_los_usuarios;

-- -----------------------------------------------------------------------------
-- 1) digbit_equipo : los equipos del laboratorio
-- -----------------------------------------------------------------------------
SET @usuario := CONCAT('''digbit_equipo''@''', @desde, '''');
SET @identificado := CONCAT(' IDENTIFIED BY ''', @clave_equipo, '''');
SET @sql := IF(@moderno, CONCAT('CREATE USER IF NOT EXISTS ', @usuario, @identificado),
                         CONCAT('GRANT USAGE ON *.* TO ', @usuario, @identificado));
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@moderno, CONCAT('ALTER USER ', @usuario, @identificado),
                         CONCAT('DROP USER ', @usuario));
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@moderno, CONCAT('REVOKE ALL PRIVILEGES, GRANT OPTION FROM ', @usuario),
                         CONCAT('GRANT USAGE ON *.* TO ', @usuario, @identificado));
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Catalogos: solo leer. Los rellena el administrador. laboratorios lleva
-- ademas el modo de acceso a las clases (modo_codigo, db/11), que solo cambia
-- el administrador: NUNCA concedas UPDATE en laboratorios a digbit_equipo, o
-- un alumno con estas credenciales pondria su laboratorio "sin codigo".
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.carreras  TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.grupos     TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.semestres  TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.materias   TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.laboratorios TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Horario: solo leer. Lo administra el ADM, y si el equipo pudiera escribirlo
-- un alumno se inventaria una franja de 00:00 a 23:59 y entraria a cualquier hora.
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.clases              TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.horarios            TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.horario_excepciones TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Sesiones: leer, y crear la del dia al primer uso. Nunca modificar ni borrar.
-- Con la generacion de codigos (db/11) este INSERT tambien lo usa el panel
-- del profesor para abrir su clase. El secreto de esos codigos esta en
-- configuracion, que aqui solo se puede leer: lo crea el administrador.
SET @sql := CONCAT('GRANT SELECT, INSERT ON teschi_otru.codigos_accesos TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Bitacoras: leer y anadir. SIN UPDATE ni DELETE: una bitacora entregada no se
-- retoca desde un equipo. Que no se duplique lo impide el UNIQUE (sesion, alumno)
-- de la 03, y ademas lo comprueba la aplicacion al insertar.
SET @sql := CONCAT('GRANT SELECT, INSERT ON teschi_otru.registros_bitacoras TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Usuarios: leer (hace falta para el login) y actualizar SOLO estas columnas.
-- Es un permiso POR COLUMNA a proposito. Sin el, con UPDATE a secas cualquiera
-- con estas credenciales se pondria fk_tipo_usuario = 3 y seria administrador.
-- Aqui no puede: esa columna no esta en la lista. Lo que si puede es cambiar la
-- contrasena de otro: ver el disparador del punto 3.
--   password  -> la migracion perezosa a PBKDF2, y el cambio de contrasena
--   los demas -> editar el perfil
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.usuarios TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := CONCAT('GRANT UPDATE (password, nombre, apellido_paterno, apellido_materno, correo) ON teschi_otru.usuarios TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Alta de alumnos desde el laboratorio ("Crear cuenta" del inicio de sesion):
-- INSERT en usuarios, pero tambien POR COLUMNA y SIN fk_tipo_usuario. La cuenta
-- nueva toma el valor por defecto de esa columna, 1 = alumno (lo pone
-- db/10_red_y_autoregistro.sql), asi que con estas credenciales se pueden
-- crear alumnos y nada mas: un INSERT que nombre fk_tipo_usuario se rechaza.
-- Antes no se concedia INSERT en usuarios en absoluto, porque con el a secas
-- cualquiera se creaba una cuenta de tipo 3, administrador.
SET @sql := CONCAT('GRANT INSERT (numero_identificador, nombre, apellido_paterno, apellido_materno, correo, password) ON teschi_otru.usuarios TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Carrera/grupo/semestre del alumno: leer, actualizar desde su perfil y crear la
-- de una cuenta nueva.
SET @sql := CONCAT('GRANT SELECT, INSERT, UPDATE ON teschi_otru.carrera_grupo_semestre TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Ajustes de toda la escuela (db/13): solo leer, para saber a donde llamar en
-- "Olvidaste tu contrasena?". NUNCA INSERT ni UPDATE: un alumno cambiaria esa
-- direccion por la de una pagina suya. La tabla es de la 13, pero se crea aqui
-- tambien (vacia, igual que alli) para que este permiso no dependa del orden:
-- la 13 la puede aplicar un usuario que no puede dar permisos, y esto lo
-- ejecuta siempre root.
CREATE TABLE IF NOT EXISTS teschi_otru.configuracion (
    clave  VARCHAR(64)   NOT NULL,
    valor  VARCHAR(500)  NULL,
    PRIMARY KEY (clave)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;
SET @sql := CONCAT('GRANT SELECT ON teschi_otru.configuracion TO ''digbit_equipo''@''', @desde, ''''); PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Fallas que reporta el profesor durante su clase (db/15): leer e insertar,
-- nunca cambiar ni borrar. Solo si la tabla ya esta (la crea la 15 o
-- 01_schema); si no, el permiso lo da la 15 al aplicarse.
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'teschi_otru' AND TABLE_NAME = 'reportes_profesor');
SET @sql := IF(@hay > 0, CONCAT('GRANT SELECT, INSERT ON teschi_otru.reportes_profesor TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT ''reportes_profesor aun no existe: el permiso lo da db/15 al aplicarla'' AS resultado');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Fallas que ya se resolvieron (db/26): solo leer, para que el docente vea en
-- "Mis bitacoras" lo que ya se atendio. Solo si la tabla ya esta; si no, el
-- permiso lo da la 26 al aplicarse.
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'teschi_otru' AND TABLE_NAME = 'fallas_resueltas');
SET @sql := IF(@hay > 0, CONCAT('GRANT SELECT ON teschi_otru.fallas_resueltas TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT ''fallas_resueltas aun no existe: el permiso lo da db/26 al aplicarla'' AS resultado');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Suspensiones de acceso (db/27): solo leer. Es lo que las hace valer en los
-- laboratorios: sin el permiso, ese equipo no las ve y el estudiante entra igual.
-- Solo si la tabla ya esta; si no, el permiso lo da la 27 al aplicarse.
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'teschi_otru' AND TABLE_NAME = 'suspensiones');
SET @sql := IF(@hay > 0, CONCAT('GRANT SELECT ON teschi_otru.suspensiones TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT ''suspensiones aun no existe: el permiso lo da db/27 al aplicarla'' AS resultado');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Memorias de emergencia (db/28): SOLO leer, para reconocerlas en el equipo. NUNCA
-- INSERT ni UPDATE: con estas credenciales un alumno registraria su propia memoria
-- y abriria el escritorio sin bitacora. Solo si la tabla ya esta; si no, el
-- permiso lo da la 28 al aplicarse.
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'teschi_otru' AND TABLE_NAME = 'memorias_emergencia');
SET @sql := IF(@hay > 0, CONCAT('GRANT SELECT ON teschi_otru.memorias_emergencia TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT ''memorias_emergencia aun no existe: el permiso lo da db/28 al aplicarla'' AS resultado');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Asistencias que el profesor quita durante su clase (db/17): leer, quitar
-- (insertar) y devolver (borrar). Solo si la tabla ya esta; si no, el permiso
-- lo da la 17 al aplicarse.
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'teschi_otru' AND TABLE_NAME = 'asistencias_retiradas');
SET @sql := IF(@hay > 0, CONCAT('GRANT SELECT, INSERT, DELETE ON teschi_otru.asistencias_retiradas TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT ''asistencias_retiradas aun no existe: el permiso lo da db/17 al aplicarla'' AS resultado');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Asistencias que da el profesor a quien no pudo registrar su bitacora
-- (db/22): leer, dar (insertar) y anular, y para anular SOLO las columnas
-- activa, anulada_en y anulada_por. Nunca borrar ni reescribir lo dado. Solo
-- si la tabla ya esta; si no, el permiso lo da la 22 al aplicarse.
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'teschi_otru' AND TABLE_NAME = 'asistencias_dadas');
SET @sql := IF(@hay > 0, CONCAT('GRANT SELECT, INSERT ON teschi_otru.asistencias_dadas TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT ''asistencias_dadas aun no existe: el permiso lo da db/22 al aplicarla'' AS resultado');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hay > 0, CONCAT('GRANT UPDATE (activa, anulada_en, anulada_por) ON teschi_otru.asistencias_dadas TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT 1 FROM DUAL WHERE 1 = 0');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Lo que cada profesor elige para sus clases (db/19): imagen, color y nombre
-- corto. Leer, guardar, cambiar y quitar. Solo si la tabla ya esta; si no, el
-- permiso lo da la 19 al aplicarse.
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'teschi_otru' AND TABLE_NAME = 'personalizacion_clases');
SET @sql := IF(@hay > 0, CONCAT('GRANT SELECT, INSERT, UPDATE, DELETE ON teschi_otru.personalizacion_clases TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT ''personalizacion_clases aun no existe: el permiso lo da db/19 al aplicarla'' AS resultado');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Quien entro con su contrasena del SIIA y espera que lo enlacen (db/24): leer,
-- anadir su solicitud y actualizar SOLO lo que dio el SIIA, los intentos y que
-- vuelva a estar pendiente. Nunca usuarios.siia_enlazado_en (no esta en las
-- listas de arriba): desde un equipo no se enlaza a nadie, eso lo hace la
-- administracion. Solo si la tabla ya esta; si no, el permiso lo da la 24.
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'teschi_otru' AND TABLE_NAME = 'siia_solicitudes');
SET @sql := IF(@hay > 0, CONCAT('GRANT SELECT, INSERT ON teschi_otru.siia_solicitudes TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT ''siia_solicitudes aun no existe: el permiso lo da db/24 al aplicarla'' AS resultado');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@hay > 0, CONCAT('GRANT UPDATE (nombre, apellido_paterno, apellido_materno, nombre_completo, correo, tipo_siia, ultimo_intento, intentos, estado) ON teschi_otru.siia_solicitudes TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT 1 FROM DUAL WHERE 1 = 0');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- El historial de cambios (db/21, historial_cambios): solo INSERT. Lo escribe
-- el panel del administrador (digbit_admin, abajo, ya puede) y lo lee el
-- encargado de laboratorio (digbit_consulta, db/18). Desde un equipo solo se
-- anota el alta de un estudiante con el SIIA (db/24). Nunca leerlo, cambiarlo
-- ni borrarlo; quien lea connections.config si podria inventar renglones (como
-- las bitacoras). Solo si la tabla ya esta; si no, el permiso lo da la 24.
SET @hay := (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'teschi_otru' AND TABLE_NAME = 'historial_cambios');
SET @sql := IF(@hay > 0, CONCAT('GRANT INSERT ON teschi_otru.historial_cambios TO ''digbit_equipo''@''', @desde, ''''),
                         'SELECT ''historial_cambios aun no existe: el permiso lo da db/24 al aplicarla'' AS resultado');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -----------------------------------------------------------------------------
-- 2) digbit_admin : solo la maquina del administrador
-- -----------------------------------------------------------------------------
-- Ese equipo no lo usa ningun alumno y no corre en modo kiosco, asi que su
-- connections.config no esta expuesto igual. Aun asi tampoco es root: no puede
-- borrar tablas, ni cambiar el esquema, ni crear usuarios de MySQL. Para las
-- migraciones se usa root, una vez, con deploy/migrar_bd.ps1.
SET @usuario := CONCAT('''digbit_admin''@''', @desde, '''');
SET @identificado := CONCAT(' IDENTIFIED BY ''', @clave_admin, '''');
SET @sql := IF(@moderno, CONCAT('CREATE USER IF NOT EXISTS ', @usuario, @identificado),
                         CONCAT('GRANT USAGE ON *.* TO ', @usuario, @identificado));
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@moderno, CONCAT('ALTER USER ', @usuario, @identificado),
                         CONCAT('DROP USER ', @usuario));
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := IF(@moderno, CONCAT('REVOKE ALL PRIVILEGES, GRANT OPTION FROM ', @usuario),
                         CONCAT('GRANT USAGE ON *.* TO ', @usuario, @identificado));
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @sql := CONCAT('GRANT SELECT, INSERT, UPDATE, DELETE ON teschi_otru.* TO ''digbit_admin''@''', @desde, '''');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

FLUSH PRIVILEGES;

-- -----------------------------------------------------------------------------
-- 3) Las cuentas de administrador, fuera del alcance de digbit_equipo
-- -----------------------------------------------------------------------------
-- digbit_equipo necesita UPDATE de password (migracion perezosa a PBKDF2 y
-- cambio de contrasena) y de los nombres y el correo (Mi perfil), y MySQL no
-- deja limitarlo a la fila de quien entro. Sin esto, quien lea connections.config
-- de un equipo podria copiar a ADMIN001 un hash cuya contrasena conoce (el de
-- una cuenta suya, o un MD5) y entrar como administrador en la maquina del
-- administrador. Con este disparador, ese usuario de MySQL no modifica ninguna
-- fila de tipo 3. USER() es quien se conecto (dentro de un disparador,
-- CURRENT_USER() seria su dueno). A los profesores no se les puede proteger
-- asi: entran desde sus equipos con digbit_equipo y tienen que poder cambiar su
-- contrasena.
-- Tampoco digbit_api, el usuario de la API de recuperacion por correo (db/13):
-- su config.php vive en un hosting web y solo debe tocar alumnos y profesores.
-- MariaDB 5.5 admite un solo disparador BEFORE UPDATE por tabla: si ya hubiera
-- otro, esto falla y lo dice (los permisos de arriba ya quedaron puestos).
DROP TRIGGER IF EXISTS teschi_otru.trg_usuarios_admin_protegido;
DELIMITER //
CREATE TRIGGER teschi_otru.trg_usuarios_admin_protegido
BEFORE UPDATE ON teschi_otru.usuarios
FOR EACH ROW
BEGIN
    IF OLD.fk_tipo_usuario = 3 AND SUBSTRING_INDEX(USER(), '@', 1) IN ('digbit_equipo', 'digbit_api') THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Las cuentas de administrador no se modifican desde los equipos del laboratorio';
    END IF;
END//
DELIMITER ;

-- -----------------------------------------------------------------------------
-- 4) Comprobacion
-- -----------------------------------------------------------------------------
SELECT CONCAT('digbit_equipo@', @desde) AS usuario;
SET @sql := CONCAT('SHOW GRANTS FOR ''digbit_equipo''@''', @desde, '''');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SELECT CONCAT('digbit_admin@', @desde) AS usuario;
SET @sql := CONCAT('SHOW GRANTS FOR ''digbit_admin''@''', @desde, '''');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -----------------------------------------------------------------------------
-- 5) Las cadenas de conexion que van en cada sitio
-- -----------------------------------------------------------------------------
-- Las barras van dobladas porque MySQL se come la barra invertida dentro de una
-- cadena: 'deploy\configurar' salia impreso como 'deployconfigurar'.
SELECT 'Equipos del laboratorio (deploy\\configurar_equipo.ps1 -CadenaConexion)' AS donde,
       CONCAT('Database=teschi_otru;Server=SERVIDOR;Port=3306;User Id=digbit_equipo;Password=', @clave_equipo) AS cadena
UNION ALL
SELECT 'Maquina del administrador (DigLab\\connections.config)',
       CONCAT('Database=teschi_otru;Server=SERVIDOR;Port=3306;User Id=digbit_admin;Password=', @clave_admin);

-- -----------------------------------------------------------------------------
-- Lo que sigue sin cubrir esto, y conviene saberlo
-- -----------------------------------------------------------------------------
-- digbit_equipo PUEDE LEER la tabla usuarios entera, incluidas las contrasenas.
-- Hace falta para validar el login y no hay forma de evitarlo sin mover la
-- comprobacion a un procedimiento almacenado. Lo que lo hace soportable es que
-- desde la fase 7 son PBKDF2-SHA256 con 100 000 iteraciones y sal por usuario:
-- unos 260 ms por intento, frente a los millones por segundo que permitia MD5.
-- Mientras queden hashes MD5 sin migrar (lo dice db/06_migracion_contrasenas.sql)
-- esa parte sigue siendo debil.
--
-- Y puede INSERTAR bitacoras, asi que puede meter registros falsos. Es el limite
-- de lo que se puede hacer sin quitarle a la aplicacion su razon de ser.
--
-- Puede cambiar la contrasena (y el nombre y el correo) de cualquier alumno o
-- profesor, porque la aplicacion lo necesita para la migracion perezosa y para
-- Mi perfil: con ella, un alumno puede entrar como otro alumno o como un
-- profesor. Al administrador no (punto 3). Cerrarlo del todo exige mover el
-- cambio de contrasena a un procedimiento almacenado.
--
-- Y puede crear cuentas de alumno con cualquier matricula que aun no exista,
-- igual que "Crear cuenta". La aplicacion solo acepta matriculas de 10 cifras,
-- pero desde otro cliente de MySQL no hay esa limitacion.
--
-- Y puede leer configuracion, que guarda el secreto de los codigos generados
-- cada semana (db/11, modo renovable): con el se calcula el codigo de cualquier
-- clase y dia. Ya podia leer los codigos de los profesores y las sesiones de
-- hoy: que solo el administrador vea los codigos es una regla de las pantallas.
--
-- Y puede leer en configuracion la contrasena de digbit_consulta (db/18,
-- consulta_clave), que cada equipo necesita para pasar a esa cuenta cuando
-- entra un encargado de laboratorio. No le da nada nuevo: digbit_consulta solo
-- lee, y solo tablas que digbit_equipo ya lee. Por lo mismo, el disparador del
-- punto 3 no protege las filas de los encargados (tipo 4): su contrasena la
-- migra a PBKDF2 la cuenta del equipo en que entran, antes de pasar a
-- digbit_consulta, y quien se la cambiara solo veria lo que ya puede leer.
--
-- Y puede leer y cambiar lo que cualquier profesor eligio para sus clases
-- (personalizacion_clases, db/19: imagen, color y nombre corto). Solo lo ve ese
-- profesor en sus pantallas y no toca la clase; es del mismo tipo que lo que ya
-- se acepta arriba (puede cambiar la contrasena de un profesor). Cada guardado
-- desde DigLab queda en el log con el numero de empleado, y el profesor lo
-- deshace con Restablecer.
--
-- Y puede anadir solicitudes de enlace con el SIIA falsas (db/24): salen en
-- "Docentes por enlazar", pero no enlazan nada; eso lo decide la
-- administracion, con digbit_admin. Tambien puede guardar la huella de la
-- contrasena de un docente (es UPDATE de password, como arriba).
--
-- Y puede dar y anular asistencias (db/22) en cualquier sesion y a nombre de
-- cualquier profesor: la base no comprueba la hora ni quien es (eso lo hace
-- DigLab). Con el permiso de anular (activa, anulada_en, anulada_por) tambien
-- podria volver a poner en 1 una anulada o cambiar cuando se anulo; el UNIQUE
-- sigue sin dejar dos que cuenten el mismo dia. Sin DELETE ni UPDATE del resto
-- de las columnas, lo dado no se borra ni se reescribe. Es del mismo tipo que
-- meter bitacoras falsas o quitar asistencias (db/17). Si los equipos entran
-- con un usuario que puede todo en la base (como hoy en la escuela), ese
-- usuario si puede borrarlas o reescribirlas: ahi solo valida DigLab.
