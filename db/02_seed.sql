-- =============================================================================
-- DigLab 2.0 - Datos de prueba para desarrollo local
-- =============================================================================
-- NO USAR EN PRODUCCION: las contrasenas de abajo son publicas y el hash es MD5.
--
-- Deja un horario realista de un semestre en los cuatro laboratorios: seis
-- maestros, siete grupos, once materias y dieciocho clases en bloques de dos
-- horas sin choques de laboratorio, maestro ni grupo. Las fechas son relativas
-- al dia en que se carga (vigencia: desde hace 60 dias hasta dentro de 120).
--
-- Uso:
--   mysql -u root -p < db/02_seed.sql
-- =============================================================================

USE teschi_otru;

SET FOREIGN_KEY_CHECKS = 0;
TRUNCATE TABLE registros_bitacoras;
TRUNCATE TABLE codigos_accesos;
TRUNCATE TABLE horario_excepciones;
TRUNCATE TABLE horarios;
TRUNCATE TABLE clases;
TRUNCATE TABLE carrera_grupo_semestre;
TRUNCATE TABLE usuarios;
TRUNCATE TABLE materias;
TRUNCATE TABLE grupos;
TRUNCATE TABLE laboratorios;
TRUNCATE TABLE semestres;
TRUNCATE TABLE carreras;
SET FOREIGN_KEY_CHECKS = 1;

-- -----------------------------------------------------------------------------
-- Catalogos
-- -----------------------------------------------------------------------------

INSERT INTO carreras (idcarreras, nombre_carrera) VALUES
    (1, 'Ingenieria en Sistemas Computacionales'),
    (2, 'Ingenieria Industrial'),
    (3, 'Ingenieria en Gestion Empresarial');

INSERT INTO semestres (idsemestres, numero_semestre) VALUES
    (1, '1'), (2, '2'), (3, '3'), (4, '4'), (5, '5'),
    (6, '6'), (7, '7'), (8, '8'), (9, '9');

INSERT INTO grupos (idgrupos, nombre_grupo, Carrera_id) VALUES
    (1, 'ISC-5001', 1),
    (2, 'ISC-5002', 1),
    (3, 'ISC-7001', 1),
    (4, 'IIN-3001', 2),
    (5, 'IGE-3001', 3),
    (6, 'ISC-3001', 1),
    (7, 'ISC-3002', 1);

INSERT INTO materias (id_materia, nombre_materia, idcarreras) VALUES
    (1,  'Programacion Orientada a Objetos', 1),
    (2,  'Bases de Datos', 1),
    (3,  'Redes de Computadoras', 1),
    (4,  'Estudio del Trabajo', 2),
    (5,  'Fundamentos de Gestion Empresarial', 3),
    (6,  'Programacion Web', 1),
    (7,  'Sistemas Operativos', 1),
    (8,  'Estructura de Datos', 1),
    (9,  'Simulacion', 2),
    (10, 'Contabilidad', 3),
    (11, 'Taller de Titulacion', 1);

-- Cuatro laboratorios, como en el TESCHI. El equipo de desarrollo se configura
-- como 'Laboratorio de Computo 1' (appSettings Laboratorio o DIGLAB_LABORATORIO).
INSERT INTO laboratorios (idlaboratorios, nombre_laboratorio) VALUES
    (1, 'Laboratorio de Computo 1'),
    (2, 'Laboratorio de Computo 2'),
    (3, 'Laboratorio de Computo 3'),
    (4, 'Laboratorio de Redes');

-- -----------------------------------------------------------------------------
-- Usuarios
-- -----------------------------------------------------------------------------
-- Credenciales de prueba (usuario / contrasena):
--   20230001 / alumno123     -> alumno, grupo ISC-5001
--   20230002 / alumno123     -> alumno, grupo ISC-5002
--   EMP001 .. EMP006 / profesor123 -> profesores
--   ADMIN001 / admin123      -> administrador (tipo 3)

-- A PROPOSITO se siembran con el MD5 antiguo y no con PBKDF2: asi el seed sirve
-- para probar la migracion perezosa de la fase 7. Al entrar por primera vez,
-- cada uno de estos hashes se reemplaza solo por su version PBKDF2 y la
-- contrasena sigue siendo la misma (ver docs/fase7-contrasenas.md). Nadie
-- deberia sembrar contrasenas asi en produccion, ni usar 'alumno123' para todos.
INSERT INTO usuarios
    (idusuarios, numero_identificador, nombre, apellido_paterno, apellido_materno, correo, password, fk_tipo_usuario)
VALUES
    -- MD5('alumno123') = 0c82ca5b1092a0c21dcfe3200688046e
    (1, '20230001', 'Ana',     'Ramirez',  'Soto',    'ana.ramirez@teschi.edu.mx',     '0c82ca5b1092a0c21dcfe3200688046e', 1),
    (2, '20230002', 'Luis',    'Ortega',   'Mendoza', 'luis.ortega@teschi.edu.mx',     '0c82ca5b1092a0c21dcfe3200688046e', 1),
    -- MD5('profesor123') = 70cf5c0095d91b8f2b9798700651df25
    (3, 'EMP001',   'Marta',   'Velazco',  'Nunez',   'marta.velazco@teschi.edu.mx',   '70cf5c0095d91b8f2b9798700651df25', 2),
    (4, 'EMP002',   'Jorge',   'Salinas',  'Cruz',    'jorge.salinas@teschi.edu.mx',   '70cf5c0095d91b8f2b9798700651df25', 2),
    -- MD5('admin123') = 0192023a7bbd73250516f069df18b500
    (5, 'ADMIN001', 'Administrador', 'DigLab', '',    'sistemas@teschi.edu.mx',        '0192023a7bbd73250516f069df18b500', 3),
    (6, 'EMP003',   'Laura',   'Cardenas', 'Rios',    'laura.cardenas@teschi.edu.mx',  '70cf5c0095d91b8f2b9798700651df25', 2),
    (7, 'EMP004',   'Ricardo', 'Pena',     'Soto',    'ricardo.pena@teschi.edu.mx',    '70cf5c0095d91b8f2b9798700651df25', 2),
    (8, 'EMP005',   'Sofia',   'Montes',   'Diaz',    'sofia.montes@teschi.edu.mx',    '70cf5c0095d91b8f2b9798700651df25', 2),
    (9, 'EMP006',   'Andres',  'Luna',     'Vega',    'andres.luna@teschi.edu.mx',     '70cf5c0095d91b8f2b9798700651df25', 2),
    -- Mas alumnos, para que las bitacoras de ejemplo tengan varias filas.
    (10, '20230003', 'Diego',  'Fuentes',  'Lara',    'diego.fuentes@teschi.edu.mx',   '0c82ca5b1092a0c21dcfe3200688046e', 1),
    (11, '20230004', 'Paola',  'Nava',     'Reyes',   'paola.nava@teschi.edu.mx',      '0c82ca5b1092a0c21dcfe3200688046e', 1),
    (12, '20230005', 'Hugo',   'Barrera',  'Islas',   'hugo.barrera@teschi.edu.mx',    '0c82ca5b1092a0c21dcfe3200688046e', 1),
    (13, '20230006', 'Elena',  'Quiroz',   'Mena',    'elena.quiroz@teschi.edu.mx',    '0c82ca5b1092a0c21dcfe3200688046e', 1);

-- Solo los alumnos llevan carrera/grupo/semestre (la columna guarda usuarios.idusuarios).
INSERT INTO carrera_grupo_semestre
    (usuarios_numero_identificador, carreras_idcarreras, grupos_idgrupos, semestres_idsemestres)
VALUES
    (1,  1, 1, 5),
    (2,  1, 2, 5),
    (10, 1, 1, 5),
    (11, 1, 1, 5),
    (12, 1, 2, 5),
    (13, 1, 2, 5);

-- -----------------------------------------------------------------------------
-- Clases: maestro + grupo + materia + CODIGO FIJO
-- -----------------------------------------------------------------------------
--   id  codigo  maestro            materia                  grupo     notas
--   1   AB12c   Marta Velazco      Bases de Datos           ISC-5001  Lab 1, lun y mie 08-10
--   2   QW7pz   Jorge Salinas      Redes                    ISC-5002  Lab 2, lun y mie 08-10
--   3   EXP01   Marta Velazco      POO                      ISC-7001  vigencia TERMINADA (sale en gris)
--   4   FUT01   Marta Velazco      Redes                    ISC-5001  vigencia FUTURA (sale en gris)
--   5   NOHOY   Sofia Montes       Taller de Titulacion     ISC-7001  sin franjas; sesion extra manana en el Lab 3
--   6   PW7ka   Laura Cardenas     Programacion Web         ISC-7001  Lab 1
--   7   SO4me   Ricardo Pena       Sistemas Operativos      ISC-5002  Lab 1
--   8   ED9tr   Marta Velazco      Estructura de Datos      ISC-3001  Lab 1
--   9   CT2an   Sofia Montes       Contabilidad             IGE-3001  Lab 1
--   10  SM5lu   Andres Luna        Simulacion               IIN-3001  Lab 1
--   11  PO3zx   Ricardo Pena       POO                      ISC-3002  Lab 2
--   12  BD8qe   Marta Velazco      Bases de Datos           ISC-5002  Lab 2
--   13  FG6ty   Sofia Montes       Fund. de Gestion         IGE-3001  Lab 2
--   14  ET1rw   Jorge Salinas      Estudio del Trabajo      IIN-3001  Lab 3
--   15  PW2bn   Laura Cardenas     Programacion Web         ISC-5001  Lab 3
--   16  ED3cv   Andres Luna        Estructura de Datos      ISC-3002  Lab 3
--   17  RD5hj   Jorge Salinas      Redes                    ISC-7001  Lab de Redes
--   18  SO7mn   Ricardo Pena       Sistemas Operativos      ISC-3001  Lab de Redes

INSERT INTO clases
    (idclases, usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en)
VALUES
    (1,  3, 1, 2,  'AB12c', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (2,  4, 2, 3,  'QW7pz', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (3,  3, 3, 1,  'EXP01', CURDATE() - INTERVAL 180 DAY, CURDATE() - INTERVAL 30 DAY,  1, 5, NOW()),
    (4,  3, 1, 3,  'FUT01', CURDATE() + INTERVAL 30 DAY,  CURDATE() + INTERVAL 150 DAY, 1, 5, NOW()),
    (5,  8, 3, 11, 'NOHOY', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (6,  6, 3, 6,  'PW7ka', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (7,  7, 2, 7,  'SO4me', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (8,  3, 6, 8,  'ED9tr', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (9,  8, 5, 10, 'CT2an', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (10, 9, 4, 9,  'SM5lu', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (11, 7, 7, 1,  'PO3zx', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (12, 3, 2, 2,  'BD8qe', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (13, 8, 5, 5,  'FG6ty', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (14, 4, 4, 4,  'ET1rw', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (15, 6, 1, 6,  'PW2bn', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (16, 9, 7, 8,  'ED3cv', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (17, 4, 3, 3,  'RD5hj', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW()),
    (18, 7, 6, 7,  'SO7mn', CURDATE() - INTERVAL 60 DAY,  CURDATE() + INTERVAL 120 DAY, 1, 5, NOW());

-- -----------------------------------------------------------------------------
-- Horario semanal (dia: 1 = lunes ... 6 = sabado). Bloques de dos horas.
-- -----------------------------------------------------------------------------
INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin) VALUES
    -- Laboratorio de Computo 1
    (1,  1, 1, '08:00:00', '10:00:00'), (1,  1, 3, '08:00:00', '10:00:00'),   -- AB12c  Marta
    (6,  1, 1, '10:00:00', '12:00:00'), (6,  1, 4, '10:00:00', '12:00:00'),   -- PW7ka  Laura
    (7,  1, 2, '08:00:00', '10:00:00'), (7,  1, 4, '08:00:00', '10:00:00'),   -- SO4me  Ricardo
    (8,  1, 2, '12:00:00', '14:00:00'), (8,  1, 5, '12:00:00', '14:00:00'),   -- ED9tr  Marta
    (9,  1, 1, '16:00:00', '18:00:00'), (9,  1, 3, '16:00:00', '18:00:00'),   -- CT2an  Sofia
    (10, 1, 5, '08:00:00', '10:00:00'), (10, 1, 6, '09:00:00', '11:00:00'),   -- SM5lu  Andres
    (3,  1, 6, '12:00:00', '14:00:00'),                                       -- EXP01  vencida (gris)
    (4,  1, 6, '14:00:00', '16:00:00'),                                       -- FUT01  futura (gris)
    -- Laboratorio de Computo 2
    (2,  2, 1, '08:00:00', '10:00:00'), (2,  2, 3, '08:00:00', '10:00:00'),   -- QW7pz  Jorge
    (11, 2, 1, '12:00:00', '14:00:00'), (11, 2, 4, '12:00:00', '14:00:00'),   -- PO3zx  Ricardo
    (12, 2, 2, '10:00:00', '12:00:00'), (12, 2, 4, '10:00:00', '12:00:00'),   -- BD8qe  Marta
    (13, 2, 2, '16:00:00', '18:00:00'), (13, 2, 5, '16:00:00', '18:00:00'),   -- FG6ty  Sofia
    -- Laboratorio de Computo 3
    (14, 3, 2, '10:00:00', '12:00:00'), (14, 3, 4, '10:00:00', '12:00:00'),   -- ET1rw  Jorge
    (15, 3, 3, '12:00:00', '14:00:00'), (15, 3, 5, '10:00:00', '12:00:00'),   -- PW2bn  Laura
    (16, 3, 1, '08:00:00', '10:00:00'), (16, 3, 3, '10:00:00', '12:00:00'),   -- ED3cv  Andres
    -- Laboratorio de Redes
    (17, 4, 5, '12:00:00', '14:00:00'), (17, 4, 6, '08:00:00', '10:00:00'),   -- RD5hj  Jorge
    (18, 4, 3, '14:00:00', '16:00:00'), (18, 4, 5, '14:00:00', '16:00:00');   -- SO7mn  Ricardo

-- -----------------------------------------------------------------------------
-- Excepciones de ejemplo
-- -----------------------------------------------------------------------------
-- Sesion extra de NOHOY manana de 18:00 a 20:00 en el Lab 3 (reposicion).
INSERT INTO horario_excepciones
    (tipo, fecha, horarios_idhorarios, clases_idclases, laboratorios_idlaboratorios, hora_inicio, hora_fin, motivo, creada_por, creada_en)
VALUES
    ('extra', CURDATE() + INTERVAL 1 DAY, NULL, 5, 3, '18:00:00', '20:00:00', 'Reposicion de clase', 5, NOW());

-- La clase AB12c del proximo miercoles queda cancelada (junta academica).
INSERT INTO horario_excepciones
    (tipo, fecha, horarios_idhorarios, clases_idclases, laboratorios_idlaboratorios, hora_inicio, hora_fin, motivo, creada_por, creada_en)
SELECT 'cancelada', CURDATE() + INTERVAL ((2 - WEEKDAY(CURDATE()) + 7) % 7) DAY, idhorarios, NULL, NULL, NULL, NULL, 'Junta academica', 5, NOW()
FROM horarios WHERE clases_idclases = 1 AND dia_semana = 3;

-- SOLO PARA DESARROLLO: sesion extra de AB12c en el Lab 1 desde la hora en que
-- se carga el seed y durante cuatro horas, para probar el flujo del alumno sin
-- esperar a su franja. Tambien sirve para ver en la cuadricula del
-- administrador como se dibuja una sesion extra (rayado naranja), que
-- desaparece sola al pasar su hora. Vuelve a cargar el seed cuando la necesites.
INSERT INTO horario_excepciones
    (tipo, fecha, horarios_idhorarios, clases_idclases, laboratorios_idlaboratorios, hora_inicio, hora_fin, motivo, creada_por, creada_en)
VALUES
    ('extra', CURDATE(), NULL, 1, 1,
     MAKETIME(HOUR(CURTIME()), 0, 0),
     MAKETIME(LEAST(HOUR(CURTIME()) + 4, 23), IF(HOUR(CURTIME()) + 4 > 23, 59, 0), 0),
     'Sesion de prueba del seed', 5, NOW());

-- -----------------------------------------------------------------------------
-- Sesiones y bitacoras de ejemplo
-- -----------------------------------------------------------------------------
-- Cada sesion es "la bitacora de esa hora": una clase en una fecha, con todos
-- los alumnos que registraron dentro. Se dejan cuatro de la semana pasada para
-- ver la pantalla del administrador (sesiones arriba, alumnos abajo).
--
--   1  OLD01  historica, de antes de la fase 6 (sin franja): sigue funcionando
--   2  AB12c  lunes pasado,     Bases de Datos, ISC-5001, Lab 1, 4 alumnos
--   3  SO4me  martes pasado,    Sistemas Operativos, ISC-5002, Lab 1, 3 alumnos
--   4  QW7pz  miercoles pasado, Redes, ISC-5002, Lab 2, 2 alumnos (uno sin conexion)

INSERT INTO codigos_accesos
    (idcodigos_accesos, codigo, hora_registro, fecha, hora_entrada, hora_salida,
     materias_id_materia, grupos_idgrupos, laboratorios_idlaboratorios, usuarios_idusuarios,
     horarios_idhorarios, horario_excepciones_id)
VALUES
    (1, 'OLD01', NOW() - INTERVAL 14 DAY, CURDATE() - INTERVAL 14 DAY, '08:00:00', '10:00:00', 2, 1, 1, 3, NULL, NULL),
    (2, 'AB12c', NOW() - INTERVAL 7 DAY, CURDATE() - INTERVAL (WEEKDAY(CURDATE()) + 7) DAY, '08:00:00', '10:00:00', 2, 1, 1, 3,
        (SELECT idhorarios FROM horarios WHERE clases_idclases = 1 AND dia_semana = 1 LIMIT 1), NULL),
    (3, 'SO4me', NOW() - INTERVAL 6 DAY, CURDATE() - INTERVAL (WEEKDAY(CURDATE()) + 6) DAY, '08:00:00', '10:00:00', 7, 2, 1, 7,
        (SELECT idhorarios FROM horarios WHERE clases_idclases = 7 AND dia_semana = 2 LIMIT 1), NULL),
    (4, 'QW7pz', NOW() - INTERVAL 5 DAY, CURDATE() - INTERVAL (WEEKDAY(CURDATE()) + 5) DAY, '08:00:00', '10:00:00', 3, 2, 2, 4,
        (SELECT idhorarios FROM horarios WHERE clases_idclases = 2 AND dia_semana = 3 LIMIT 1), NULL);

-- Alumnos por sesion. Las bitacoras que reportan alguna falla o comentario
-- salen en naranja en tablas y PDF; las que dicen "Ninguno" en todo, no. Los
-- marcados con registrado_sin_conexion entraron solo con su matricula mientras
-- el equipo no alcanzaba el servidor.
INSERT INTO registros_bitacoras
    (fk_codigo_accesos, fk_usuario, numero_computadora, falla_red, comentarios_red, falla_hardware, comentarios_hardware, falla_software, comentarios_software, registrado_sin_conexion, registrado_en)
VALUES
    -- OLD01: Ana sin novedad y Luis con una falla de red (naranja).
    (1, 1,  'PC-07', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', '', 0, NULL),
    (1, 2,  'PC-12', 'Otro', 'Sin acceso a internet', 'Ninguno', 'Ninguno', 'Ninguno', '', 0, NULL),
    -- AB12c del lunes: cuatro alumnos, tres con falla y Ana sin novedad.
    (2, 1,  'PC-03', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', '', 0, NULL),
    (2, 10, 'PC-04', 'Ninguno', 'Ninguno', 'Otro', 'El teclado no responde', 'Ninguno', '', 0, NULL),
    (2, 11, 'PC-05', 'Otro', 'Red intermitente', 'Ninguno', 'Ninguno', 'Ninguno', '', 0, NULL),
    (2, 12, 'PC-06', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', 'Otro', 'Falta Visual Studio', 0, NULL),
    -- SO4me del martes: tres alumnos, solo uno con falla.
    (3, 2,  'PC-11', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', '', 0, NULL),
    (3, 12, 'PC-13', 'Ninguno', 'Ninguno', 'Otro', 'Mouse sin cable', 'Ninguno', '', 0, NULL),
    (3, 13, 'PC-14', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', '', 0, NULL),
    -- QW7pz del miercoles en el Lab 2: dos alumnos, uno registrado sin conexion.
    (4, 2,  'PC-21', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', 'Ninguno', '', 0, NULL),
    (4, 13, 'PC-22', 'Otro', 'Sin salida a internet', 'Ninguno', 'Ninguno', 'Ninguno', '', 1, NOW() - INTERVAL 5 DAY);

-- Un codigo por profesor (db/09_codigo_por_profesor.sql): cada maestro se queda
-- con el de su clase con mas sesiones y todas sus clases lo comparten. Asi los
-- datos de prueba siguen la misma regla que la aplicacion.
UPDATE usuarios u
INNER JOIN (
    SELECT t.profesor,
           SUBSTRING_INDEX(GROUP_CONCAT(t.codigo ORDER BY t.sesiones DESC, t.idclases ASC SEPARATOR '|'), '|', 1) AS codigo
    FROM (SELECT c.idclases, c.usuarios_idusuarios AS profesor, c.codigo,
                 (SELECT COUNT(*) FROM codigos_accesos ca WHERE ca.codigo = c.codigo) AS sesiones
            FROM clases c) t
    GROUP BY t.profesor
) elegido ON elegido.profesor = u.idusuarios
SET u.codigo_clase = elegido.codigo;

UPDATE clases c
INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
SET c.codigo = u.codigo_clase
WHERE u.codigo_clase IS NOT NULL;
