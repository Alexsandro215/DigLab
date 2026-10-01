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
    -- escrito en duro en Login.cs). No hay tabla de catalogo: el codigo nunca la
    -- consulta (ver AbrirPestanaSegunTipoUsuario).
    fk_tipo_usuario      TINYINT      NOT NULL,
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
    -- Codigo de la clase (clases.codigo). Se repite en cada sesion de la misma
    -- clase, por eso ya NO es UNIQUE.
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
    -- Profesor de la clase (usuarios.idusuarios).
    usuarios_idusuarios         INT         NOT NULL,
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
    -- Environment.MachineName del equipo, no un numero.
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
