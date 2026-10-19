-- =============================================================================
-- DigBit 2.0 - Migracion: cada profesor personaliza sus clases
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- El profesor elige, para las clases que le asigno el administrador, una
-- imagen (se ve en un circulo), un color y un nombre corto. Es SOLO SUYO: lo
-- ven sus pantallas (su panel, Mi horario y Mis grupos). No cambia nada de la
-- clase: ni clases, ni materias, ni grupos, ni usuarios. El administrador y el
-- encargado lo ven todo como antes, y la lista de asistencia, los PDF y los
-- reportes siguen con los nombres oficiales.
--
-- Una fila por profesor y materia (fk_grupo = 0: todos sus grupos de esa
-- materia, tambien los del ciclo siguiente) o por profesor, materia y grupo
-- (ese grupo tiene la suya y no sigue a la de la materia). En una fila, NULL
-- es lo de siempre: color automatico, nombre oficial, iniciales.
--
-- PERMISOS: digbit_equipo (db/07), con el que entran los equipos de los
-- profesores, necesita leer, insertar, cambiar y borrar en la tabla nueva; se
-- le dan aqui si quien ejecuta esto puede dar permisos (root). digbit_admin ya
-- puede todo en la base. digbit_consulta (db/18) NO: el encargado nunca ve
-- las pantallas del profesor, y DigBit no lo deja entrar si esa cuenta puede
-- escribir algo.
--
-- ORDEN: da igual. En los equipos con la version que la usa, el panel, Mi
-- horario y Mis grupos del profesor cambian de aspecto en cuanto la notan
-- (conexion/Esquema.cs): cada clase sale con su circulo y su color aunque el
-- profesor no elija nada. Crea una tabla nueva y no copia nada: se puede
-- aplicar en horario de clase.
--
-- Repetible: lo que ya esta, lo deja como esta.
--
-- Uso:
--   mysql -u root -p < db/19_personalizacion_clases.sql
-- =============================================================================

USE teschi_otru;

CREATE TABLE IF NOT EXISTS personalizacion_clases (
    -- El profesor que lo eligio (usuarios.idusuarios).
    fk_profesor     INT          NOT NULL,
    -- La materia (materias.id_materia).
    fk_materia      INT          NOT NULL,
    -- 0 = todos sus grupos de esa materia; si no, solo ese grupo (grupos.idgrupos).
    -- Sin clave foranea: el 0 no es un grupo, y una fila de un grupo que ya no
    -- existe no estorba (solo se cruza con las clases que existen).
    fk_grupo        INT          NOT NULL DEFAULT 0,
    -- '#RRGGBB', como usuarios.color_horario. NULL = automatico.
    color           VARCHAR(7)   NULL,
    -- Como la llama el profesor. NULL = el nombre de la materia. La aplicacion
    -- quita lo que el utf8 de 3 bytes no guarda (emojis).
    nombre_corto    VARCHAR(24)  NULL,
    -- PNG o JPEG de 128 x 128 que ya recorto DigBit, de 60 000 bytes como mucho.
    -- MEDIUMBLOB y no BLOB: un BLOB admite 65 535 bytes y, con el sql_mode vacio
    -- de la escuela, lo que sobrara se cortaria sin error. DigBit ademas
    -- comprueba LENGTH(imagen) despues de guardar. NULL = las iniciales.
    imagen          MEDIUMBLOB   NULL,
    -- SHA1 de la imagen, calculado por el servidor sobre lo guardado. Se lee en
    -- lugar de la imagen para saber si la que se tiene en memoria sigue valiendo.
    imagen_sha1     CHAR(40)     NULL,
    -- Reloj del servidor.
    actualizado_en  DATETIME     NOT NULL,
    PRIMARY KEY (fk_profesor, fk_materia, fk_grupo),
    KEY ix_personalizacion_clases_materia (fk_materia),
    CONSTRAINT fk_personalizacion_clases_profesor
        FOREIGN KEY (fk_profesor) REFERENCES usuarios (idusuarios)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_personalizacion_clases_materia
        FOREIGN KEY (fk_materia) REFERENCES materias (id_materia)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Permiso de digbit_equipo para leer, guardar, cambiar y quitar. Nunca se
-- escribe a mano el origen de la cuenta: en MariaDB 5.5, un GRANT a un
-- 'usuario'@'origen' que no existe la crearia SIN contrasena (igual que en db/13).
SET @equipo := (SELECT GRANTEE FROM information_schema.USER_PRIVILEGES
                WHERE GRANTEE LIKE '''digbit\_equipo''@%' LIMIT 1);
SET @yo := CONCAT('''', SUBSTRING_INDEX(CURRENT_USER(), '@', 1), '''@''', SUBSTRING_INDEX(CURRENT_USER(), '@', -1), '''');
SET @ve_cuentas := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE <> @yo) > 0;
SET @puede_dar := (SELECT COUNT(*) FROM information_schema.USER_PRIVILEGES WHERE GRANTEE = @yo AND IS_GRANTABLE = 'YES')
                + (SELECT COUNT(*) FROM information_schema.SCHEMA_PRIVILEGES
                   WHERE GRANTEE = @yo AND TABLE_SCHEMA = DATABASE() AND IS_GRANTABLE = 'YES') > 0;
SET @sql := CASE
    WHEN @equipo IS NOT NULL AND @puede_dar THEN
        CONCAT('GRANT SELECT, INSERT, UPDATE, DELETE ON `', DATABASE(), '`.personalizacion_clases TO ', @equipo)
    WHEN @equipo IS NOT NULL THEN
        CONCAT('SELECT ''AVISO: este usuario no puede dar permisos. Pide a root que aplique esta misma migracion (db/19), ',
               'o que ejecute: GRANT SELECT, INSERT, UPDATE, DELETE ON `', DATABASE(), '`.personalizacion_clases TO ', REPLACE(@equipo, '''', ''''''),
               '; (es la cuenta que ya existe).'' AS resultado')
    WHEN @ve_cuentas THEN
        'SELECT ''No hay usuario digbit_equipo: no hace falta darle permiso'' AS resultado'
    ELSE
        'SELECT ''AVISO: este usuario no ve las cuentas de MySQL. Si los equipos entran con digbit_equipo (db/07), pide a root que aplique esta misma migracion (db/19): le dara a esa cuenta permiso para leer, guardar, cambiar y borrar en personalizacion_clases. Si entran con un usuario que ya puede todo en la base, no hace falta.'' AS resultado'
END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- digbit_consulta (db/18) a proposito no recibe nada (ver PERMISOS arriba).

SELECT 'personalizacion_clases lista' AS resultado, (SELECT COUNT(*) FROM personalizacion_clases) AS filas;
