-- =============================================================================
-- DigBit 2.0 - Esquema de base de datos
-- =============================================================================
-- Reconstruido a partir de las consultas de conexion/Consultas.cs,
-- conexion/InsercionDatos.cs y conexion/Borrar.cs.
--
-- IMPORTANTE: el nombre de la base de datos NO es configurable hoy. Seis
-- consultas de Consultas.cs califican las tablas como teschi_otru.<tabla>
-- (por ejemplo MostrarNombreProfesor y ConsultaEditar), asi que la base tiene
-- que llamarse exactamente teschi_otru o esas consultas fallan.
--
-- Uso:
--   mysql -u root -p < db/01_schema.sql
--   mysql -u root -p < db/02_seed.sql
-- =============================================================================

DROP DATABASE IF EXISTS teschi_otru;
CREATE DATABASE teschi_otru
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;
USE teschi_otru;

-- -----------------------------------------------------------------------------
-- Catalogos
-- -----------------------------------------------------------------------------

CREATE TABLE carreras (
    idcarreras     INT          NOT NULL AUTO_INCREMENT,
    nombre_carrera VARCHAR(120) NOT NULL,
    PRIMARY KEY (idcarreras),
    UNIQUE KEY uq_carreras_nombre (nombre_carrera)
) ENGINE=InnoDB;

CREATE TABLE semestres (
    idsemestres     INT         NOT NULL AUTO_INCREMENT,
    -- Se lee y se compara como texto (ObtenerNombresSemestre -> cbSemestre.Texts),
    -- no como numero. Se conserva VARCHAR para no romper esa comparacion.
    numero_semestre VARCHAR(20) NOT NULL,
    PRIMARY KEY (idsemestres),
    UNIQUE KEY uq_semestres_numero (numero_semestre)
) ENGINE=InnoDB;

CREATE TABLE grupos (
    idgrupos     INT         NOT NULL AUTO_INCREMENT,
    nombre_grupo VARCHAR(60) NOT NULL,
    -- Mayuscula y singular a proposito: asi lo escribe ObtenerGruposPorCarrera.
    Carrera_id   INT         NULL,
    PRIMARY KEY (idgrupos),
    KEY ix_grupos_carrera (Carrera_id),
    CONSTRAINT fk_grupos_carrera
        FOREIGN KEY (Carrera_id) REFERENCES carreras (idcarreras)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

CREATE TABLE materias (
    id_materia     INT          NOT NULL AUTO_INCREMENT,
    nombre_materia VARCHAR(120) NOT NULL,
    -- Plural aqui y singular en grupos. Inconsistente, pero es lo que consulta
    -- ObtenerMateriasPorCarrera.
    idcarreras     INT          NULL,
    PRIMARY KEY (id_materia),
    KEY ix_materias_carrera (idcarreras),
    CONSTRAINT fk_materias_carrera
        FOREIGN KEY (idcarreras) REFERENCES carreras (idcarreras)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

CREATE TABLE laboratorios (
    idlaboratorios     INT         NOT NULL AUTO_INCREMENT,
    nombre_laboratorio VARCHAR(80) NOT NULL,
    -- 0 = desactivado: no sale en el panel ni en Horarios, pero su historial se
    -- conserva. Uno con historial se desactiva en vez de borrarse. En una base
    -- ya existente se anade con db/08_migracion_bajas.sql.
    activo             TINYINT(1)  NOT NULL DEFAULT 1,
    -- Como entran los alumnos a sus clases aqui: 'fijo' con el codigo unico de
    -- cada profesor; 'renovable' con codigos generados cada semana (uno por
    -- clase y dia, del secreto 'codigos_secreto' de configuracion; el profesor
    -- ve el suyo al abrir su clase); 'sin_codigo' solo llenando la bitacora.
    -- Solo lo cambia el administrador. En una base ya existente se anade con
    -- db/11_modo_de_acceso.sql.
    modo_codigo        ENUM('fijo','renovable','sin_codigo') NOT NULL DEFAULT 'fijo',
    PRIMARY KEY (idlaboratorios),
    UNIQUE KEY uq_laboratorios_nombre (nombre_laboratorio)
) ENGINE=InnoDB;

-- -----------------------------------------------------------------------------
-- Usuarios
-- -----------------------------------------------------------------------------

CREATE TABLE usuarios (
    idusuarios           INT          NOT NULL AUTO_INCREMENT,
    -- Matricula del alumno o numero de empleado del profesor. Es la credencial
    -- de inicio de sesion; Login.cs la restringe a caracteres alfanumericos.
    numero_identificador VARCHAR(30)  NOT NULL,
    nombre               VARCHAR(80)  NOT NULL,
    apellido_paterno     VARCHAR(80)  NOT NULL,
    apellido_materno     VARCHAR(80)  NOT NULL,
    correo               VARCHAR(120) NULL,
    -- Desde la fase 7: PBKDF2-SHA256 con sal por usuario, en el formato
    -- "pbkdf2-sha256$<iteraciones>$<sal base64>$<hash base64>" (~90 caracteres).
    -- Puede haber todavia hashes MD5 antiguos (32 caracteres hexadecimales) de
    -- antes de la migracion: la aplicacion los acepta y los reemplaza por PBKDF2
    -- la primera vez que ese usuario entra bien (ver conexion/Contrasenas.cs).
    password             VARCHAR(255) NOT NULL,
    -- 1 = alumno, 2 = profesor, 3 = administrador (desde la fase 6; antes estaba
    -- escrito en duro en Login.cs), 4 = encargado de laboratorio: el panel del
    -- administrador en solo consulta (db/18). No hay tabla de catalogo: el
    -- codigo no la necesita (ver AbrirPestanaSegunTipoUsuario). Por defecto
    -- alumno: el alta desde el laboratorio no la nombra, y el usuario de MySQL de
    -- los equipos no puede escribirla (db/07). En una base ya existente, db/10.
    fk_tipo_usuario      TINYINT      NOT NULL DEFAULT 1,
    -- 0 = dado de baja: no puede entrar, pero su historial se conserva y se le
    -- puede reactivar. Se da de baja en vez de borrar porque las claves
    -- foraneas borran en cascada sus bitacoras. En una base ya existente se
    -- anade con db/08_migracion_bajas.sql.
    activo               TINYINT(1)   NOT NULL DEFAULT 1,
    baja_en              DATETIME     NULL,
    -- Solo profesores: el codigo que teclean sus alumnos en el laboratorio, uno
    -- para todas sus clases (se copia a clases.codigo). En una base ya
    -- existente se anade con db/09_codigo_por_profesor.sql.
    codigo_clase         VARCHAR(16)  NULL,
    -- Solo profesores: el color de sus clases en los horarios y en el panel,
    -- '#RRGGBB'. NULL = automatico (por el orden de su primera clase). Lo elige
    -- el administrador. En una base ya existente se anade con
    -- db/12_color_profesor.sql.
    color_horario        VARCHAR(7)   NULL,
    PRIMARY KEY (idusuarios),
    UNIQUE KEY uq_usuarios_numero_identificador (numero_identificador),
    UNIQUE KEY uq_usuarios_codigo_clase (codigo_clase)
) ENGINE=InnoDB;

-- Relacion alumno -> carrera / grupo / semestre.
-- OJO con el nombre: usuarios_numero_identificador NO guarda
-- numero_identificador, guarda usuarios.idusuarios. Lo confirma el JOIN de
-- ConsultaEditar: ON u.idusuarios = cgs.usuarios_numero_identificador
CREATE TABLE carrera_grupo_semestre (
    idcarrera_grupo_semestre      INT NOT NULL AUTO_INCREMENT,
    usuarios_numero_identificador INT NOT NULL,
    carreras_idcarreras           INT NULL,
    grupos_idgrupos               INT NULL,
    semestres_idsemestres         INT NULL,
    PRIMARY KEY (idcarrera_grupo_semestre),
    KEY ix_cgs_usuario (usuarios_numero_identificador),
    CONSTRAINT fk_cgs_usuario
        FOREIGN KEY (usuarios_numero_identificador) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_cgs_carrera
        FOREIGN KEY (carreras_idcarreras) REFERENCES carreras (idcarreras)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_cgs_grupo
        FOREIGN KEY (grupos_idgrupos) REFERENCES grupos (idgrupos)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_cgs_semestre
        FOREIGN KEY (semestres_idsemestres) REFERENCES semestres (idsemestres)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -----------------------------------------------------------------------------
-- Horarios por laboratorio (fase 6, docs/fase6-horarios.md)
-- -----------------------------------------------------------------------------

-- Una clase = profesor + grupo + materia con un CODIGO FIJO durante su vigencia.
-- Solo el administrador las crea, cambia y desactiva.
CREATE TABLE clases (
    idclases            INT         NOT NULL AUTO_INCREMENT,
    -- Profesor (usuarios.idusuarios).
    usuarios_idusuarios INT         NOT NULL,
    grupos_idgrupos     INT         NOT NULL,
    materias_id_materia INT         NOT NULL,
    -- El codigo de su profesor (usuarios.codigo_clase): todas las clases de un
    -- profesor lo comparten, por eso no es unico. Se busca la clase por codigo,
    -- laboratorio y hora (HorariosDatos.ResolverCodigo). En una base ya
    -- existente, db/09_codigo_por_profesor.sql.
    codigo              VARCHAR(16) NOT NULL,
    vigente_desde       DATE        NOT NULL,
    vigente_hasta       DATE        NOT NULL,
    activa              TINYINT(1)  NOT NULL DEFAULT 1,
    -- Administrador que la creo (usuarios.idusuarios).
    creada_por          INT         NULL,
    creada_en           DATETIME    NOT NULL,
    PRIMARY KEY (idclases),
    KEY ix_clases_codigo (codigo),
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

-- Franja semanal de una clase en un laboratorio. Los solapes (mismo
-- laboratorio, mismo profesor o mismo grupo a la misma hora) los comprueba la
-- aplicacion al guardar; MySQL no puede expresarlos solo.
CREATE TABLE horarios (
    idhorarios                  INT     NOT NULL AUTO_INCREMENT,
    clases_idclases             INT     NOT NULL,
    laboratorios_idlaboratorios INT     NOT NULL,
    -- 1 = lunes ... 7 = domingo. Coincide con WEEKDAY(fecha) + 1 de MySQL.
    dia_semana                  TINYINT NOT NULL,
    hora_inicio                 TIME    NOT NULL,
    hora_fin                    TIME    NOT NULL,
    -- Vigencia propia de la franja (db/20); NULL = la de su clase. Cambiar o
    -- quitar una franja con historia le pone fin y crea la nueva desde una fecha.
    vigente_desde               DATE    NULL,
    vigente_hasta               DATE    NULL,
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

-- Excepcion de un dia concreto: 'cancelada' anula una franja ese dia; 'extra'
-- programa una sesion fuera de horario (el codigo de una clase en un
-- laboratorio y un horario temporales).
CREATE TABLE horario_excepciones (
    idhorario_excepciones       INT                       NOT NULL AUTO_INCREMENT,
    tipo                        ENUM('cancelada','extra') NOT NULL,
    fecha                       DATE                      NOT NULL,
    -- cancelada: la franja que ese dia no se da.
    horarios_idhorarios         INT                       NULL,
    -- extra: la clase (y con ella el codigo y el profesor), donde y a que hora.
    clases_idclases             INT                       NULL,
    laboratorios_idlaboratorios INT                       NULL,
    hora_inicio                 TIME                      NULL,
    hora_fin                    TIME                      NULL,
    motivo                      VARCHAR(200)              NULL,
    creada_por                  INT                       NULL,
    creada_en                   DATETIME                  NOT NULL,
    PRIMARY KEY (idhorario_excepciones),
    -- Una franja solo se cancela una vez por dia (las 'extra' llevan NULL aqui).
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
-- Sesiones (codigos_accesos) y bitacoras
-- -----------------------------------------------------------------------------

-- Sesion de una clase en una fecha. Desde la fase 6 la crea la aplicacion al
-- primer uso del codigo en el dia, a partir de una franja de `horarios` o de
-- una excepcion 'extra'. Las filas anteriores (codigos que generaban los
-- profesores) quedan con horarios_idhorarios y horario_excepciones_id en NULL,
-- como historial: bitacoras y PDF siguen funcionando con ellas.
CREATE TABLE codigos_accesos (
    idcodigos_accesos           INT         NOT NULL AUTO_INCREMENT,
    -- Codigo con el que se entra a la sesion. En un laboratorio 'fijo' es el de
    -- la clase (clases.codigo) y se repite en cada sesion, por eso ya NO es
    -- UNIQUE; en uno 'renovable', el generado de 5 cifras de esa clase y dia
    -- (o uno al azar sin secreto), y en uno 'sin_codigo', uno al azar
    -- (laboratorios.modo_codigo); 'LIBRE' en las de uso libre. Nunca se cambia.
    --
    -- Sesion de USO LIBRE (db/14): la de un laboratorio 'sin_codigo' cuando no
    -- hay ninguna clase. Sin profesor, materia ni grupo (NULL), sin franja ni
    -- extra, y con el tramo libre entre clases en hora_entrada y hora_salida.
    codigo                      VARCHAR(16) NOT NULL,
    -- Momento en que se creo la sesion.
    hora_registro               DATETIME    NOT NULL,
    fecha                       DATE        NULL,
    -- Horas de la franja (o de la excepcion). La ventana de validez es
    -- [fecha + hora_entrada - 15 min, fecha + hora_salida], reloj del servidor.
    hora_entrada                TIME        NULL,
    hora_salida                 TIME        NULL,
    materias_id_materia         INT         NULL,
    grupos_idgrupos             INT         NULL,
    laboratorios_idlaboratorios INT         NULL,
    -- Profesor de la clase (usuarios.idusuarios); NULL en las de uso libre.
    usuarios_idusuarios         INT         NULL,
    horarios_idhorarios         INT         NULL,
    horario_excepciones_id      INT         NULL,
    PRIMARY KEY (idcodigos_accesos),
    -- Una sesion por franja y dia, y una por excepcion.
    UNIQUE KEY uq_sesion_franja_fecha (horarios_idhorarios, fecha),
    UNIQUE KEY uq_sesion_excepcion (horario_excepciones_id),
    KEY ix_codigos_codigo (codigo),
    KEY ix_codigos_profesor (usuarios_idusuarios),
    CONSTRAINT fk_codigos_horario
        FOREIGN KEY (horarios_idhorarios) REFERENCES horarios (idhorarios)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_codigos_excepcion
        FOREIGN KEY (horario_excepciones_id) REFERENCES horario_excepciones (idhorario_excepciones)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_codigos_materia
        FOREIGN KEY (materias_id_materia) REFERENCES materias (id_materia)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_codigos_grupo
        FOREIGN KEY (grupos_idgrupos) REFERENCES grupos (idgrupos)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_codigos_laboratorio
        FOREIGN KEY (laboratorios_idlaboratorios) REFERENCES laboratorios (idlaboratorios)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_codigos_profesor
        FOREIGN KEY (usuarios_idusuarios) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

CREATE TABLE registros_bitacoras (
    idregistros_bitacoras INT          NOT NULL AUTO_INCREMENT,
    fk_codigo_accesos     INT          NOT NULL,
    -- Alumno que llena la bitacora (usuarios.idusuarios).
    fk_usuario            INT          NOT NULL,
    -- El numero pegado en el equipo (NumeroMaquina, lo pone el instalador); si
    -- no se configuro, el nombre de Windows.
    numero_computadora    VARCHAR(80)  NULL,
    falla_red             VARCHAR(120) NULL,
    comentarios_red       VARCHAR(500) NULL,
    falla_hardware        VARCHAR(120) NULL,
    comentarios_hardware  VARCHAR(500) NULL,
    falla_software        VARCHAR(120) NULL,
    comentarios_software  VARCHAR(500) NULL,
    -- Fase 4: 1 si el alumno registro sin servidor (entro solo con su matricula,
    -- sin contrasena, y la bitacora se subio despues desde la cola local).
    registrado_sin_conexion TINYINT(1) NOT NULL DEFAULT 0,
    -- Cuando registro el alumno: reloj del servidor en linea, el del equipo sin
    -- conexion. Es el de su PRIMER registro y no se actualiza nunca: si vuelve a
    -- registrarse (se fue la luz, se reinicio el equipo) no entra otra bitacora.
    -- Con clases seguidas del mismo profesor dice cuando llego. NULL en las
    -- bitacoras en linea de antes del 2026-09-29, que no lo guardaban.
    registrado_en         DATETIME     NULL,
    -- Numeros de OTROS equipos que el alumno vio sin internet, separados por
    -- comas ("11,13"). NULL si no aviso de ninguno. En una base ya existente,
    -- esta y recibido_en se anaden con db/10_red_y_autoregistro.sql.
    equipos_sin_internet  VARCHAR(120) NULL,
    -- Cuando llego al servidor (su NOW()). Igual que registrado_en en linea; si
    -- la bitacora se subio despues desde la cola del equipo, dice cuanto tardo.
    recibido_en           DATETIME     NULL,
    PRIMARY KEY (idregistros_bitacoras),
    -- Un alumno registra UNA bitacora por codigo (por clase). Un segundo intento
    -- produce el error 1062, que consultaFinal traduce a "ya registraste tu
    -- bitacora". En una base ya existente se anade con db/03_migracion_fase2.sql.
    UNIQUE KEY uq_bitacora_codigo_usuario (fk_codigo_accesos, fk_usuario),
    KEY ix_bitacoras_usuario (fk_usuario),
    CONSTRAINT fk_bitacoras_codigo
        FOREIGN KEY (fk_codigo_accesos) REFERENCES codigos_accesos (idcodigos_accesos)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_bitacoras_usuario
        FOREIGN KEY (fk_usuario) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- Fallas que reporta el profesor mientras su clase esta en curso (db/15):
-- una fila por falla, colgada de la sesion de la clase. Las ven el profesor en
-- la bitacora de su clase y el administrador en las alertas y en Bitacoras.
CREATE TABLE reportes_profesor (
    idreportes_profesor INT          NOT NULL AUTO_INCREMENT,
    fk_codigo_accesos   INT          NOT NULL,
    -- El profesor que reporta (usuarios.idusuarios).
    fk_usuario          INT          NOT NULL,
    -- El numero pegado en el equipo; NULL si es del laboratorio en general.
    numero_computadora  VARCHAR(80)  NULL,
    area                ENUM('red','hardware','software','otro') NOT NULL,
    falla               VARCHAR(120) NOT NULL,
    comentarios         VARCHAR(500) NULL,
    registrado_en       DATETIME     NOT NULL,
    PRIMARY KEY (idreportes_profesor),
    KEY ix_reportes_profesor_sesion (fk_codigo_accesos),
    KEY ix_reportes_profesor_usuario (fk_usuario),
    CONSTRAINT fk_reportes_profesor_sesion
        FOREIGN KEY (fk_codigo_accesos) REFERENCES codigos_accesos (idcodigos_accesos)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_reportes_profesor_usuario
        FOREIGN KEY (fk_usuario) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- Codigos para recuperar la contrasena por correo (db/13): los pide y los
-- comprueba la API de recuperacion (api/, en un hosting con PHP), no DigBit.
-- Guarda el HMAC-SHA256 del codigo con el secreto de la API, nunca el codigo.
-- usuario_id NULL: una matricula que no existe (solo para limitar por IP).
CREATE TABLE recuperacion_contrasena (
    idrecuperacion  INT          NOT NULL AUTO_INCREMENT,
    usuario_id      INT          NULL,
    matricula       VARCHAR(45)  NOT NULL,
    codigo_hash     CHAR(64)     NULL,
    -- HMAC del correo al que salio: si la cuenta cambia de correo, el codigo deja de valer.
    correo_hash     CHAR(64)     NULL,
    creado_en       DATETIME     NOT NULL,
    expira_en       DATETIME     NOT NULL,
    intentos        INT          NOT NULL DEFAULT 0,
    usado_en        DATETIME     NULL,
    ip              VARCHAR(45)  NULL,
    -- El equipo que lo pidio ("numero @ laboratorio", lo manda DigBit): limite por equipo.
    equipo          VARCHAR(60)  NULL,
    PRIMARY KEY (idrecuperacion),
    KEY ix_recuperacion_usuario (usuario_id, creado_en),
    KEY ix_recuperacion_ip (ip, creado_en),
    KEY ix_recuperacion_equipo (equipo, creado_en),
    KEY ix_recuperacion_creado (creado_en),
    CONSTRAINT fk_recuperacion_usuario
        FOREIGN KEY (usuario_id) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE
) ENGINE=InnoDB;

-- Ajustes de toda la escuela que DigBit lee de la base (db/13), para no tocar
-- cada equipo: la direccion de la API de recuperacion ('recuperacion_url') y la
-- clave que le manda DigBit ('recuperacion_clave'). Los pone el administrador.
-- digbit_equipo solo puede leerla (db/07).
CREATE TABLE configuracion (
    clave  VARCHAR(64)   NOT NULL,
    valor  VARCHAR(500)  NULL,
    PRIMARY KEY (clave)
) ENGINE=InnoDB;

-- Asistencias que el profesor quita durante su clase (db/17): quien registro
-- su bitacora sin estar en el salon. La bitacora sigue contando como bitacora;
-- solo deja de contar como asistencia. digbit_equipo la lee, inserta y borra (db/07).
CREATE TABLE asistencias_retiradas (
    fk_bitacora  INT       NOT NULL,
    fk_profesor  INT       NOT NULL,
    retirada_en  DATETIME  NOT NULL,
    PRIMARY KEY (fk_bitacora),
    KEY ix_asistencias_retiradas_profesor (fk_profesor),
    CONSTRAINT fk_asistencias_retiradas_bitacora
        FOREIGN KEY (fk_bitacora) REFERENCES registros_bitacoras (idregistros_bitacoras)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_retiradas_profesor
        FOREIGN KEY (fk_profesor) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- Asistencias que da el profesor durante su clase a quien vino pero no pudo
-- registrar su bitacora (db/22): su equipo no tenia internet, no funcionaba o
-- no habia uno libre. Cuenta como asistencia de ese dia, pero no es bitacora.
-- Una fila por asistencia dada, con quien, cuando, por que y desde donde.
-- Documento oficial: anularla no la borra (activa = NULL) y sus claves son
-- RESTRICT. digbit_equipo la lee, inserta y solo puede cambiar activa,
-- anulada_en y anulada_por (db/07); digbit_consulta solo la lee (db/18).
CREATE TABLE asistencias_dadas (
    idasistencias_dadas  INT           NOT NULL AUTO_INCREMENT,
    -- La clase (profesor + materia + grupo): la lista se lee por clase y dia.
    fk_clase             INT           NOT NULL,
    -- El dia (CURDATE() del servidor al darla).
    fecha                DATE          NOT NULL,
    fk_alumno            INT           NOT NULL,
    -- La sesion del bloque en curso.
    fk_codigo_accesos    INT           NOT NULL,
    fk_profesor          INT           NOT NULL,
    -- Reloj del servidor; sin DEFAULT (MariaDB 5.5 no lo admite).
    dada_en              DATETIME      NOT NULL,
    -- sin_internet | equipo | sin_equipo | otro.
    motivo               VARCHAR(20)   NOT NULL,
    numero_computadora   VARCHAR(100)  NULL,
    comentario           VARCHAR(200)  NULL,
    dada_desde           VARCHAR(120)  NULL,
    -- 1 cuenta; NULL anulada (nunca 0): una sola que cuenta por clase, dia y alumno.
    activa               TINYINT(1)    NULL DEFAULT 1,
    anulada_en           DATETIME      NULL,
    anulada_por          INT           NULL,
    PRIMARY KEY (idasistencias_dadas),
    UNIQUE KEY uq_asistencias_dadas_dia (fk_clase, fecha, fk_alumno, activa),
    KEY ix_asistencias_dadas_fecha (fecha),
    KEY ix_asistencias_dadas_sesion (fk_codigo_accesos),
    KEY ix_asistencias_dadas_alumno (fk_alumno, fecha),
    KEY ix_asistencias_dadas_profesor (fk_profesor, fecha),
    KEY ix_asistencias_dadas_anulada_por (anulada_por),
    CONSTRAINT fk_asistencias_dadas_clase FOREIGN KEY (fk_clase)
        REFERENCES clases (idclases) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_dadas_sesion FOREIGN KEY (fk_codigo_accesos)
        REFERENCES codigos_accesos (idcodigos_accesos) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_dadas_alumno FOREIGN KEY (fk_alumno)
        REFERENCES usuarios (idusuarios) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_dadas_profesor FOREIGN KEY (fk_profesor)
        REFERENCES usuarios (idusuarios) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_asistencias_dadas_anulada_por FOREIGN KEY (anulada_por)
        REFERENCES usuarios (idusuarios) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB;

-- Configuraciones de horario (db/16 y db/23): copias con nombre del horario
-- (cada docente con sus clases y sus horas en cada laboratorio) que solo usa
-- el DigBit de la administracion (Horarios > Configuraciones y Aplicacion).
-- Una en preparacion ('borrador', como mucho una), las guardadas ('guardado')
-- y las aplicadas ('publicado'), que quedan como registro. Al aplicarse una,
-- DigBit crea clases y franjas normales con su vigencia. digbit_equipo no
-- tiene permiso sobre estas tablas (db/07); digbit_consulta solo las lee (db/18).
CREATE TABLE ciclos_preliminares (
    idciclo        INT          NOT NULL AUTO_INCREMENT,
    nombre         VARCHAR(60)  NOT NULL,
    -- Primer y ultimo dia de clases: la vigencia de sus clases al aplicarla.
    -- NULL en una guardada, y en la que se prepara hasta ponerlas en Aplicacion.
    desde          DATE         NULL,
    hasta          DATE         NULL,
    -- El dia cuyas clases se copiaron; NULL si se empezo en blanco o de otra.
    copiado_del    DATE         NULL,
    -- De que configuracion viene (o en cual se guardo por ultima vez). Sin
    -- clave foranea a si misma: si esa se borra, queda el numero.
    origen_ciclo   INT          NULL,
    estado         ENUM('borrador','publicado','guardado') NOT NULL DEFAULT 'borrador',
    creado_por     INT          NULL,
    creado_en      DATETIME     NOT NULL,
    -- El ultimo cambio (lo pone DigBit con NOW()).
    cambiado_en    DATETIME     NULL,
    cambiado_por   INT          NULL,
    -- Al aplicarla: el ultimo dia que se dio a las clases de antes.
    fin_anterior   DATE         NULL,
    publicado_por  INT          NULL,
    publicado_en   DATETIME     NULL,
    PRIMARY KEY (idciclo),
    KEY ix_ciclos_preliminares_estado (estado),
    KEY ix_ciclos_preliminares_origen (origen_ciclo),
    CONSTRAINT fk_ciclos_preliminares_creador
        FOREIGN KEY (creado_por) REFERENCES usuarios (idusuarios)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_ciclos_preliminares_publicador
        FOREIGN KEY (publicado_por) REFERENCES usuarios (idusuarios)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_ciclos_preliminares_cambio
        FOREIGN KEY (cambiado_por) REFERENCES usuarios (idusuarios)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

CREATE TABLE ciclo_profesores (
    idciclo   INT NOT NULL,
    profesor  INT NOT NULL,
    estado    ENUM('por_revisar','revisado','no_sigue') NOT NULL DEFAULT 'por_revisar',
    PRIMARY KEY (idciclo, profesor),
    KEY ix_ciclo_profesores_profesor (profesor),
    CONSTRAINT fk_ciclo_profesores_ciclo
        FOREIGN KEY (idciclo) REFERENCES ciclos_preliminares (idciclo)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_ciclo_profesores_usuario
        FOREIGN KEY (profesor) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- Una materia con un grupo: una clase al publicar. profesor NULL = "Sin profesor".
CREATE TABLE ciclo_cargas (
    idcarga          INT           NOT NULL AUTO_INCREMENT,
    idciclo          INT           NOT NULL,
    profesor         INT           NULL,
    grupo            INT           NOT NULL,
    materia          INT           NOT NULL,
    quitada          TINYINT(1)    NOT NULL DEFAULT 0,
    -- De que clase se copio (NULL = agregada a mano) y como era entonces.
    clase_origen     INT           NULL,
    profesor_origen  INT           NULL,
    grupo_origen     INT           NULL,
    materia_origen   INT           NULL,
    -- 'lab:dia:HH:MM-HH:MM;...' ordenado por dia, inicio y laboratorio.
    horario_origen   VARCHAR(1000) NULL,
    clase_publicada  INT           NULL,
    -- La clase de la configuracion de la que se copio (db/23). Sin clave foranea.
    carga_origen     INT           NULL,
    PRIMARY KEY (idcarga),
    KEY ix_ciclo_cargas_ciclo (idciclo, profesor),
    KEY ix_ciclo_cargas_profesor (profesor),
    KEY ix_ciclo_cargas_grupo (grupo),
    KEY ix_ciclo_cargas_materia (materia),
    KEY ix_ciclo_cargas_origen (clase_origen),
    KEY ix_ciclo_cargas_publicada (clase_publicada),
    KEY ix_ciclo_cargas_carga_origen (carga_origen),
    CONSTRAINT fk_ciclo_cargas_ciclo
        FOREIGN KEY (idciclo) REFERENCES ciclos_preliminares (idciclo)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_ciclo_cargas_profesor
        FOREIGN KEY (profesor) REFERENCES usuarios (idusuarios)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_ciclo_cargas_grupo
        FOREIGN KEY (grupo) REFERENCES grupos (idgrupos)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_ciclo_cargas_materia
        FOREIGN KEY (materia) REFERENCES materias (id_materia)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_ciclo_cargas_origen
        FOREIGN KEY (clase_origen) REFERENCES clases (idclases)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_ciclo_cargas_publicada
        FOREIGN KEY (clase_publicada) REFERENCES clases (idclases)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

CREATE TABLE ciclo_franjas (
    idfranja     INT      NOT NULL AUTO_INCREMENT,
    idcarga      INT      NOT NULL,
    laboratorio  INT      NOT NULL,
    dia_semana   TINYINT  NOT NULL,
    hora_inicio  TIME     NOT NULL,
    hora_fin     TIME     NOT NULL,
    PRIMARY KEY (idfranja),
    KEY ix_ciclo_franjas_carga (idcarga),
    KEY ix_ciclo_franjas_lab_dia (laboratorio, dia_semana),
    CONSTRAINT fk_ciclo_franjas_carga
        FOREIGN KEY (idcarga) REFERENCES ciclo_cargas (idcarga)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_ciclo_franjas_laboratorio
        FOREIGN KEY (laboratorio) REFERENCES laboratorios (idlaboratorios)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- Lo que Publicar acorto de las clases de antes, para Deshacer.
CREATE TABLE ciclo_recortes (
    idciclo      INT   NOT NULL,
    clase        INT   NOT NULL,
    hasta_antes  DATE  NOT NULL,
    PRIMARY KEY (idciclo, clase),
    KEY ix_ciclo_recortes_clase (clase),
    CONSTRAINT fk_ciclo_recortes_ciclo
        FOREIGN KEY (idciclo) REFERENCES ciclos_preliminares (idciclo)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_ciclo_recortes_clase
        FOREIGN KEY (clase) REFERENCES clases (idclases)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- Lo que cada profesor elige para sus clases (db/19): una imagen (en un
-- circulo), un color y un nombre corto. Solo lo ven sus pantallas; la clase no
-- cambia. fk_grupo = 0 vale para todos sus grupos de esa materia (tambien los
-- del ciclo siguiente); un grupo con su propia fila no sigue a la de la
-- materia. La imagen ya llega recortada por DigBit (128 x 128, 60 000 bytes
-- como mucho). digbit_equipo la lee, inserta, cambia y borra (db/07).
CREATE TABLE personalizacion_clases (
    fk_profesor     INT          NOT NULL,
    fk_materia      INT          NOT NULL,
    -- 0 = todos sus grupos de esa materia; sin clave foranea (el 0 no es un grupo).
    fk_grupo        INT          NOT NULL DEFAULT 0,
    color           VARCHAR(7)   NULL,
    nombre_corto    VARCHAR(24)  NULL,
    -- MEDIUMBLOB: con sql_mode vacio un BLOB cortaria sin error.
    imagen          MEDIUMBLOB   NULL,
    imagen_sha1     CHAR(40)     NULL,
    actualizado_en  DATETIME     NOT NULL,
    PRIMARY KEY (fk_profesor, fk_materia, fk_grupo),
    KEY ix_personalizacion_clases_materia (fk_materia),
    CONSTRAINT fk_personalizacion_clases_profesor
        FOREIGN KEY (fk_profesor) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_personalizacion_clases_materia
        FOREIGN KEY (fk_materia) REFERENCES materias (id_materia)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- Quien cambio que y cuando (db/21): cada alta, cambio, fin o baja de franjas y
-- clases, cada cancelacion y sesion extra que se pone o se quita, cada
-- correccion de una bitacora y cada cambio de un laboratorio o de su forma de
-- entrar. Lo escribe DigBit en la misma transaccion que el cambio y nunca lo
-- cambia ni lo borra. De ahi salen las alertas "Cambio de horario" y el
-- historial de Alertas. Sin claves foraneas: no se borra con nada. "cuando" sin
-- DEFAULT (MariaDB 5.5 no admite DEFAULT CURRENT_TIMESTAMP en DATETIME): lo
-- pone DigBit con NOW(). digbit_admin lo escribe, digbit_consulta solo lo lee
-- (db/18) y digbit_equipo no lo toca (db/07).
CREATE TABLE historial_cambios (
    idhistorial_cambios  INT           NOT NULL AUTO_INCREMENT,
    cuando               DATETIME      NOT NULL,
    -- Quien: su cuenta y su numero tal como entro.
    usuario_id           INT           NULL,
    usuario              VARCHAR(45)   NULL,
    -- 'franja', 'clase', 'cancelacion', 'extra', 'bitacora', 'laboratorio' o 'forma_de_entrar'.
    tipo                 VARCHAR(20)   NOT NULL,
    -- 'alta', 'cambio', 'fin', 'quitar', 'recorte', 'activar' o 'desactivar'.
    accion               VARCHAR(20)   NOT NULL,
    tabla                VARCHAR(40)   NOT NULL,
    registro_id          INT           NULL,
    laboratorio_id       INT           NULL,
    fecha                DATE          NULL,
    descripcion          VARCHAR(500)  NOT NULL,
    -- Un dato por linea; nunca el codigo de una clase (el encargado lo lee).
    antes                TEXT          NULL,
    despues              TEXT          NULL,
    PRIMARY KEY (idhistorial_cambios),
    KEY ix_historial_cambios_cuando (cuando),
    KEY ix_historial_cambios_tipo (tipo, cuando),
    KEY ix_historial_cambios_registro (tabla, registro_id)
) ENGINE=InnoDB;
