-- =============================================================================
-- DigLab 2.0 - Migracion: el color de cada profesor en los horarios
-- =============================================================================
-- Si creaste la base con db/01_schema.sql despues de esta migracion, no la
-- necesitas.
--
-- Cada profesor tiene un color, el mismo en Horarios, en la lista de Clases,
-- en el panel "Los laboratorios ahora" y en "Mi horario". Hasta ahora salia
-- solo, por el orden en que se le dio su primera clase. Desde aqui el
-- administrador puede elegirlo al dar de alta o editar al profesor.
--
-- Que hace:
--   usuarios.color_horario (VARCHAR(7), por ejemplo '#FAAAA0'): el color
--   elegido. NULL = automatico, que es como quedan todos al aplicar esto, asi
--   que nada cambia de color hasta que el administrador elija uno.
--
-- PERMISOS: ninguno nuevo. El usuario de los equipos (digbit_equipo, db/07)
-- lee la tabla usuarios entera, asi que ve el color, y solo puede cambiar las
-- columnas de su lista (contrasena, nombre, apellidos y correo), asi que no
-- puede cambiarlo; el del administrador si.
--
-- ORDEN: da igual. La version anterior de DigLab no nombra la columna, y la
-- nueva no ofrece elegir el color hasta verla (conexion/Esquema.cs).
--
-- MariaDB 5.5 copia la tabla usuarios entera al anadir la columna, con las
-- escrituras bloqueadas mientras tanto (unos segundos con unos miles de
-- alumnos): corre esto fuera de horario de clase.
--
-- Repetible: lo que ya esta, lo dice y no lo toca.
--
-- Uso:
--   mysql -u root -p < db/12_color_profesor.sql
-- =============================================================================

USE teschi_otru;

SET @falta := (SELECT COUNT(*) = 0 FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios'
                 AND COLUMN_NAME = 'color_horario');
SET @sql := IF(@falta,
    'ALTER TABLE usuarios ADD COLUMN color_horario VARCHAR(7) NULL',
    'SELECT ''usuarios.color_horario ya existe: no se toca'' AS resultado');
PREPARE ejecutar FROM @sql; EXECUTE ejecutar; DEALLOCATE PREPARE ejecutar;

-- Un valor que no sea '#RRGGBB' (escrito a mano en la base) se toma como
-- automatico; aqui solo se ensena para poder corregirlo.
SHOW COLUMNS FROM usuarios WHERE Field = 'color_horario';
SELECT numero_identificador AS profesor,
       CONCAT_WS(' ', nombre, apellido_paterno, apellido_materno) AS nombre,
       IFNULL(color_horario, 'automatico') AS color
FROM usuarios
WHERE fk_tipo_usuario = 2
ORDER BY apellido_paterno, nombre;
