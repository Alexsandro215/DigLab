-- =============================================================================
-- DigLab 2.0 - Migracion: equipos sin internet, hora de llegada y autoregistro
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- Que hace:
--
--   registros_bitacoras.equipos_sin_internet
--       Al registrar, el alumno contesta "Alguna computadora de tu alrededor
--       no tiene internet?". Si dice que si, aqui van los numeros (los que
--       llevan pegados) separados por comas, por ejemplo "11,13". NULL si no
--       aviso de ninguna, que es lo de todas las bitacoras anteriores. Va en la
--       bitacora y no en las columnas de falla: esas describen el equipo DESDE
--       el que se registra, y el aviso es de OTRO.
--
--   registros_bitacoras.recibido_en
--       Cuando llego la bitacora al servidor (su NOW()). En linea es la misma
--       hora que registrado_en; si la red se cayo y se subio despues desde la
--       cola del equipo, dice cuanto tarde. NULL en las anteriores.
--
--   usuarios.fk_tipo_usuario: DEFAULT 1 (alumno)
--       Los alumnos pueden crear su cuenta desde el equipo del laboratorio. Con
--       el usuario de MySQL de los equipos (db/07_usuarios_minimos.sql) eso se
--       concede POR COLUMNAS y sin fk_tipo_usuario: la cuenta toma este valor
--       por defecto y desde un equipo no se puede crear un administrador.
--
--   carrera_grupo_semestre: grupos_idgrupos y semestres_idsemestres admiten NULL
--       El alumno ya no declara grupo (el codigo es del profesor), y en la base
--       de la escuela los dos eran NOT NULL: sin esto no se puede guardar la
--       carrera de un alumno nuevo, asi que fallan tanto "Nuevo alumno" del
--       administrador como "Crear cuenta". Las claves foraneas no cambian.
--
-- MariaDB 5.5 copia la tabla entera al anadir columnas o cambiarlas, con las
-- escrituras bloqueadas mientras tanto: corre esto fuera de horario de clase.
-- Cambiar el valor por defecto, en cambio, no copia nada.
--
-- ORDEN: da igual. La version anterior de DigLab no nombra estas columnas, y
-- la nueva no pregunta por equipos sin internet hasta verlas (conexion/Esquema.cs).
--
-- Repetible: lo que ya esta, lo dice y no lo toca.
--
-- Uso:
--   mysql -u root -p < db/10_red_y_autoregistro.sql
-- =============================================================================

USE teschi_otru;

-- Las dos columnas de la bitacora en un solo ALTER: en 5.5 cada uno copia la tabla.
SET @falta_equipos := (SELECT COUNT(*) = 0 FROM information_schema.COLUMNS
                       WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras'
                         AND COLUMN_NAME = 'equipos_sin_internet');
SET @falta_recibido := (SELECT COUNT(*) = 0 FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'registros_bitacoras'
                          AND COLUMN_NAME = 'recibido_en');
SET @sql := CASE
    WHEN @falta_equipos AND @falta_recibido THEN
        'ALTER TABLE registros_bitacoras ADD COLUMN equipos_sin_internet VARCHAR(120) NULL, ADD COLUMN recibido_en DATETIME NULL'
    WHEN @falta_equipos THEN
        'ALTER TABLE registros_bitacoras ADD COLUMN equipos_sin_internet VARCHAR(120) NULL'
    WHEN @falta_recibido THEN
        'ALTER TABLE registros_bitacoras ADD COLUMN recibido_en DATETIME NULL'
    ELSE
        'SELECT ''registros_bitacoras ya tiene equipos_sin_internet y recibido_en: no se toca'' AS resultado'
    END;
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SET @defecto := (SELECT COLUMN_DEFAULT FROM information_schema.COLUMNS
                 WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios'
                   AND COLUMN_NAME = 'fk_tipo_usuario');
SET @sql := IF(@defecto IS NULL OR @defecto <> '1',
    'ALTER TABLE usuarios ALTER COLUMN fk_tipo_usuario SET DEFAULT 1',
    'SELECT ''usuarios.fk_tipo_usuario ya es 1 por defecto: no se toca'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- MODIFY solo cambia NOT NULL por NULL: el tipo es el mismo y las claves
-- foraneas a grupos y semestres se quedan.
SET @obligatorias := (SELECT COUNT(*) FROM information_schema.COLUMNS
                      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'carrera_grupo_semestre'
                        AND COLUMN_NAME IN ('grupos_idgrupos', 'semestres_idsemestres')
                        AND IS_NULLABLE = 'NO');
SET @sql := IF(@obligatorias > 0,
    'ALTER TABLE carrera_grupo_semestre MODIFY grupos_idgrupos INT NULL, MODIFY semestres_idsemestres INT NULL',
    'SELECT ''carrera_grupo_semestre ya admite alumnos sin grupo ni semestre: no se toca'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

SHOW COLUMNS FROM registros_bitacoras WHERE Field IN ('equipos_sin_internet', 'recibido_en');
SHOW COLUMNS FROM usuarios WHERE Field = 'fk_tipo_usuario';
SHOW COLUMNS FROM carrera_grupo_semestre WHERE Field IN ('grupos_idgrupos', 'semestres_idsemestres');
