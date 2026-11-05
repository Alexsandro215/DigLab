-- =============================================================================
-- DigLab 2.0 - Migracion: horario preliminar del proximo ciclo
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- En Horarios > Proximo ciclo el administrador arma el horario del semestre
-- siguiente en un BORRADOR: empieza como copia de la carga de cada profesor de
-- hoy (sus materias, sus grupos y sus horas en cada laboratorio) y despues solo
-- marca lo que cambia. Mientras es borrador vive solo en estas tablas: ninguna
-- clase, franja ni sesion cambia. Al publicarlo, DigLab crea clases y franjas
-- normales con la vigencia del ciclo nuevo y termina las de ahora en su ultimo
-- dia de clases (ciclo_recortes guarda su fecha de antes, para deshacerlo).
--
-- PERMISOS: ninguno nuevo. digbit_admin ya puede todo en la base (db/07), y
-- digbit_equipo, con el que entran los equipos de los laboratorios, NO debe ver
-- estas tablas: el borrador no le sirve a nadie mas que al administrador.
--
-- ORDEN: aplicarla no cambia nada para nadie. Solo el DigLab del administrador
-- la usa; los laboratorios no se tocan.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/16_ciclo_preliminar.sql
-- =============================================================================

USE teschi_otru;

-- Un horario preliminar. Solo puede haber uno en borrador, y ninguno nuevo
-- mientras haya uno publicado que todavia no empieza (lo cuida DigLab).
CREATE TABLE IF NOT EXISTS ciclos_preliminares (
    idciclo        INT          NOT NULL AUTO_INCREMENT,
    nombre         VARCHAR(60)  NOT NULL,
    -- Primer dia de clases: el vigente_desde de sus clases al publicar.
    desde          DATE         NOT NULL,
    -- Ultimo dia de clases: el vigente_hasta.
    hasta          DATE         NOT NULL,
    -- El dia cuyas clases se copiaron; NULL si se empezo en blanco.
    copiado_del    DATE         NULL,
    estado         ENUM('borrador','publicado') NOT NULL DEFAULT 'borrador',
    creado_por     INT          NULL,
    creado_en      DATETIME     NOT NULL,
    -- Al publicar: el ultimo dia que se dio a las clases de antes.
    fin_anterior   DATE         NULL,
    publicado_por  INT          NULL,
    publicado_en   DATETIME     NULL,
    PRIMARY KEY (idciclo),
    KEY ix_ciclos_preliminares_estado (estado),
    CONSTRAINT fk_ciclos_preliminares_creador
        FOREIGN KEY (creado_por) REFERENCES usuarios (idusuarios)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_ciclos_preliminares_publicador
        FOREIGN KEY (publicado_por) REFERENCES usuarios (idusuarios)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Los profesores del ciclo y como va su revision.
CREATE TABLE IF NOT EXISTS ciclo_profesores (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Una materia con un grupo: una clase al publicar.
CREATE TABLE IF NOT EXISTS ciclo_cargas (
    idcarga          INT           NOT NULL AUTO_INCREMENT,
    idciclo          INT           NOT NULL,
    -- NULL = "Sin profesor" (no deja publicar).
    profesor         INT           NULL,
    grupo            INT           NOT NULL,
    materia          INT           NOT NULL,
    -- Copiada pero quitada: no se publica y se puede regresar.
    quitada          TINYINT(1)    NOT NULL DEFAULT 0,
    -- De que clase se copio; NULL = agregada a mano.
    clase_origen     INT           NULL,
    -- Como era al copiarla, para decir que cambio.
    profesor_origen  INT           NULL,
    grupo_origen     INT           NULL,
    materia_origen   INT           NULL,
    -- 'lab:dia:HH:MM-HH:MM;...' ordenado por dia, inicio y laboratorio.
    horario_origen   VARCHAR(1000) NULL,
    -- La clase que creo al publicarse.
    clase_publicada  INT           NULL,
    PRIMARY KEY (idcarga),
    KEY ix_ciclo_cargas_ciclo (idciclo, profesor),
    KEY ix_ciclo_cargas_profesor (profesor),
    KEY ix_ciclo_cargas_grupo (grupo),
    KEY ix_ciclo_cargas_materia (materia),
    KEY ix_ciclo_cargas_origen (clase_origen),
    KEY ix_ciclo_cargas_publicada (clase_publicada),
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Las horas de cada materia en cada laboratorio.
CREATE TABLE IF NOT EXISTS ciclo_franjas (
    idfranja     INT      NOT NULL AUTO_INCREMENT,
    idcarga      INT      NOT NULL,
    laboratorio  INT      NOT NULL,
    -- 1 = lunes ... 7 = domingo, como horarios.
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Lo que Publicar acorto de las clases de antes, para Deshacer.
CREATE TABLE IF NOT EXISTS ciclo_recortes (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

SELECT 'horario del proximo ciclo listo' AS resultado, (SELECT COUNT(*) FROM ciclos_preliminares) AS ciclos;
