# Fase 6 — Horarios por laboratorio

> **Cambio del 2026-09-29: un código por profesor.** Con un código por clase,
> un profesor con seis grupos tenía seis códigos que recordar y repartir. Desde
> `db/09_codigo_por_profesor.sql` cada profesor tiene **uno** (`usuarios.codigo_clase`),
> el mismo para todas sus clases, grupos y materias; se copia a `clases.codigo`,
> que deja de ser único. Al teclearlo en un equipo, la aplicación busca, entre
> las clases de ese profesor, la que tiene horario en **ese laboratorio a esa
> hora** (`conexion/ReglasCodigo.cs`, la misma regla con y sin servidor). Un
> profesor no da dos clases a la vez, así que no hay ambigüedad; el único cruce
> es el margen de 15 minutos: con dos clases seguidas en el mismo laboratorio,
> gana la que está en curso. El código se asigna y se cambia en **Profesores**;
> en Horarios la clase solo lo muestra. El alumno ya no declara grupo.
>
> Donde este documento dice "código de la clase", léase "código de su profesor".

## Objetivo

Hoy el profesor genera un código al llegar a clase y ese código vale en
cualquier equipo. Pasa a esto:

```
El ADMINISTRADOR arma, una vez por semestre, la agenda de cada laboratorio:
    Lunes 08:00-10:00  Lab 1   Redes I        Grupo 3A   Prof. Velazco   código AB12c
    Lunes 10:00-12:00  Lab 1   Bases de datos Grupo 5B   Prof. Ruiz      código QW7pz
    ...
El código es FIJO: identifica maestro + grupo + materia durante el semestre.
Sólo sirve en SU laboratorio y en SU horario (o en una excepción que el
administrador haya aplicado para un día concreto).
El profesor ya no genera códigos: los consulta.
Cada equipo sabe en qué laboratorio está.
```

Decisiones tomadas con el usuario (2026-09-19):

1. Código fijo por clase durante todo el semestre.
2. Sólo el administrador crea, cambia y desactiva códigos.
3. Cuatro laboratorios con horario fijo. Un código no vale en otro laboratorio
   ni fuera de su franja.
4. Excepciones: si un laboratorio se usa fuera de horario por otro profesor, el
   administrador aplica una **excepción temporal**: el código de ese profesor
   en un horario temporal. También sirve para cancelar una franja un día.

## Modelo

Tres tablas nuevas y dos columnas en `codigos_accesos`. La tabla
`codigos_accesos` **se conserva** con otro significado: pasa a ser *la sesión
de una clase en una fecha*, que es lo que ya era en la práctica. Así no se
toca nada de lo que cuelga de ella: `registros_bitacoras`, los PDF, la
ventana de validez de la fase 2 ni la liberación del equipo de la fase 3.

```
clases  (maestro + grupo + materia + CÓDIGO fijo + vigencia)
   │ 1..n
horarios  (franja semanal: laboratorio, día, hora inicio, hora fin)
   │                                    horario_excepciones (un día concreto:
   │                                       'cancelada' → apunta a la franja
   │                                       'extra'     → clase + lab + horas)
   ▼
codigos_accesos  (sesión: fecha + horas + lab + ... + horarios_id / excepcion_id)
   ▼
registros_bitacoras  (sin cambios)
```

### `clases`

| Columna | Tipo | Notas |
|---|---|---|
| `idclases` | INT PK | |
| `usuarios_idusuarios` | INT FK | profesor |
| `grupos_idgrupos` | INT FK | |
| `materias_id_materia` | INT FK | |
| `codigo` | VARCHAR(16) **UNIQUE** | lo genera el administrador (5 caracteres, como hoy) y puede cambiarlo |
| `vigente_desde`, `vigente_hasta` | DATE | el semestre |
| `activa` | TINYINT(1) | desactivar sin borrar (conserva las bitácoras) |
| `creada_por`, `creada_en` | INT FK, DATETIME | quién y cuándo |

`UNIQUE (codigo)` es global: un código nunca se repite, ni entre semestres. Con
5 caracteres alfanuméricos sobran combinaciones. Si el administrador quiere el
mismo código para la misma clase el siguiente semestre, alarga la vigencia.

### `horarios` (franja semanal)

| Columna | Tipo | Notas |
|---|---|---|
| `idhorarios` | INT PK | |
| `clases_idclases` | INT FK | |
| `laboratorios_idlaboratorios` | INT FK | |
| `dia_semana` | TINYINT | 1 = lunes … 7 = domingo. Coincide con `WEEKDAY(fecha) + 1` de MySQL |
| `hora_inicio`, `hora_fin` | TIME | `hora_fin > hora_inicio`; no cruza medianoche |

Validaciones en la aplicación al guardar una franja (MySQL no las puede
expresar solo):

- no se solapa con otra franja **del mismo laboratorio** el mismo día;
- el **profesor** de la clase no está en otro laboratorio a esa hora;
- el **grupo** no está en otro laboratorio a esa hora.

Las excepciones de los **próximos siete días** se dibujan encima de la propia
cuadrícula, en la columna del día en que caen, y desaparecen solas en cuanto
pasa su hora de fin (la cuadrícula se refresca al recargar la pestaña o tras
cualquier cambio):

- **Sesión extra**: rayado naranja con borde grueso y un rótulo blanco con
  `EXTRA dd/MM`, las horas, el código, la materia y el motivo. Clic encima:
  quitarla o ver ese día en la pestaña Excepciones.
- **Franja cancelada**: rayado gris sobre la franja y un rótulo
  `CANCELADA dd/MM - motivo`. Desde el menú de la franja se puede quitar la
  cancelación.

### `horario_excepciones` (un día concreto)

| Columna | Tipo | Notas |
|---|---|---|
| `idhorario_excepciones` | INT PK | |
| `tipo` | ENUM('cancelada','extra') | |
| `fecha` | DATE | |
| `horarios_idhorarios` | INT FK NULL | `cancelada`: la franja que ese día no se da |
| `clases_idclases` | INT FK NULL | `extra`: la clase (y con ella el código y el profesor) |
| `laboratorios_idlaboratorios` | INT FK NULL | `extra` |
| `hora_inicio`, `hora_fin` | TIME NULL | `extra` |
| `motivo` | VARCHAR(200) | "Examen", "Reposición", "Puente" |
| `creada_por`, `creada_en` | | |

Una `extra` se valida contra la agenda **efectiva** de ese día en ese
laboratorio: franjas no canceladas más otras extras.

### `codigos_accesos` (ahora "sesión")

- Nuevas: `horarios_idhorarios INT NULL` y
  `horario_excepciones_id INT NULL`. Las filas antiguas (códigos que generaron
  los profesores) quedan con ambas en NULL, como historial.
- Se quita `UNIQUE (codigo)`: el mismo código se repite cada semana. Entra
  `UNIQUE (horarios_idhorarios, fecha)` y `UNIQUE (horario_excepciones_id)`:
  una sesión por franja y día, una por excepción.
- `hora_entrada` y `hora_salida` son las de la franja (o de la extra).
  `hora_registro` es el momento en que se creó la sesión.

Las sesiones se crean **al primer uso**: cuando el primer alumno del día mete
el código, si la franja de hoy no tiene sesión, se inserta. Si dos alumnos lo
hacen a la vez, el segundo INSERT choca con el UNIQUE y se vuelve a leer. No
hace falta ningún proceso que genere sesiones por adelantado, y una clase a la
que nadie fue no deja rastro.

### Administrador real

El administrador pasa a ser el usuario central del sistema y hoy no existe en
la base: es `ADMINISTRADOR / 123` escrito en `Login.cs`. Antes de darle una
agenda que administrar:

- `usuarios.fk_tipo_usuario = 3` → administrador. `RealizarInicioSesion` abre
  `PrincipalAdministrador` para ese tipo; se borra la comparación en duro.
- El seed deja `ADMIN001 / admin123` (solo desarrollo). La migración para la
  base del TESCHI inserta el administrador con una contraseña que se edita en
  el script antes de correrlo.
- Ya que se toca el login, es el momento de cambiar MD5 por PBKDF2 con
  migración perezosa (al entrar con MD5 correcto se re-guarda en PBKDF2). Es
  opcional para esta fase, pero conviene hacerlo aquí.

## Flujo del alumno

```
DigBit arranca en el equipo del Lab 2
   │  lee su laboratorio de la configuración (appSettings Laboratorio, o
   │  DIGBIT_LABORATORIO) y lo resuelve a laboratorios.idlaboratorios
   ▼
Login del alumno → pantalla de código
   │  arriba muestra "Laboratorio 2 · ahora: Redes I, 3A, Prof. Velazco,
   │  hasta 10:00" si hay clase en curso (o "sin clase programada ahora")
   ▼
Teclea el código → Consultas.ResolverCodigo(codigo, idLaboratorio)
   │
   ├─ no existe o clase inactiva ........ "Código no válido."
   ├─ fuera de vigente_desde/hasta ...... "Esa clase no está en el periodo actual."
   ├─ hoy no hay franja ni extra ........ "Hoy no hay clase con ese código."
   ├─ la hay, pero en otro laboratorio .. "Ese código es del Laboratorio 1, no de este."
   ├─ franja cancelada hoy ............... "La clase de hoy está cancelada."
   ├─ antes de inicio − 15 min ........... "Tu clase empieza a las 08:00."
   ├─ después de hora_fin ................ "Tu clase terminó a las 10:00."
   └─ dentro → sesión de hoy (se crea si no existe) → VentanaCodigo
                → una bitácora por alumno y sesión (UNIQUE ya existente)
                → al guardar, fase 3: equipo liberado hasta hora_fin
```

Consulta de resolución, en dos pasos:

```sql
-- 1) la clase
SELECT idclases, usuarios_idusuarios, grupos_idgrupos, materias_id_materia,
       vigente_desde, vigente_hasta, activa, NOW() AS ahora, CURDATE() AS hoy
FROM clases WHERE codigo = @codigo;

-- 2) candidatas de HOY para esa clase (franjas no canceladas + extras)
SELECT 'franja' AS origen, h.idhorarios AS id, h.laboratorios_idlaboratorios AS lab,
       h.hora_inicio, h.hora_fin
FROM horarios h
WHERE h.clases_idclases = @clase
  AND h.dia_semana = WEEKDAY(CURDATE()) + 1
  AND NOT EXISTS (SELECT 1 FROM horario_excepciones x
                  WHERE x.tipo = 'cancelada' AND x.horarios_idhorarios = h.idhorarios
                    AND x.fecha = CURDATE())
UNION ALL
SELECT 'extra', x.idhorario_excepciones, x.laboratorios_idlaboratorios,
       x.hora_inicio, x.hora_fin
FROM horario_excepciones x
WHERE x.tipo = 'extra' AND x.clases_idclases = @clase AND x.fecha = CURDATE();
```

El resto (elegir la candidata de este laboratorio cuya ventana contiene
`NOW()` con el margen de 15 minutos, y crear la sesión) va en C#. Todo con el
reloj del servidor, como hasta ahora.

### Lo que cambia en el código existente

- Cinco consultas de `Consultas.cs` buscan la sesión por el **texto** del
  código (`WHERE codigo = @Codigo`): `ObtenerVentanaCodigo`, `consultaFinal`,
  `ConsultarDatosPdf`, `ObtenerNombreLaboratorioPorCodigo`,
  `consultaRegistro`. Con códigos repetidos cada semana eso es ambiguo: pasan
  a recibir `idcodigos_accesos`. `Datos_User` guarda el id de la sesión además
  del código; `VentanaCodigo.IdCodigo` ya existe.
- `generarCodigo.cs`, `mostrar_Codigo_Profesor.cs` e
  `InsercionDatos.insertarcodigo` desaparecen del flujo del profesor.
- `Kiosco`/`AppContexto`: al arrancar, tras comprobar el servidor, se resuelve
  el laboratorio del equipo. Si no está configurado o no existe en la base, en
  kiosco se muestra la pantalla de error ("este equipo no tiene laboratorio
  asignado; avisa al encargado") con reintento; fuera del kiosco se avisa y se
  sigue, para desarrollar.
- `deploy\configurar_equipo.ps1` recibe `-Laboratorio 'Laboratorio 2'` y lo
  escribe en `DigBit.exe.config`.
- Fase 3: **sin cambios**. La sesión trae `hora_salida` y de ahí sale la
  ventana local.

## Pantallas

### Administrador

| Pantalla | Qué hace |
|---|---|
| **Clases** | Lista de clases (profesor, grupo, materia, código, vigencia, activa). Nueva: elige profesor, grupo y materia; el código se propone aleatorio y se puede editar (se comprueba que no exista); vigencia con dos fechas. Editar: código y vigencia siempre; profesor/grupo/materia solo si la clase aún no tiene sesiones. Desactivar. |
| **Horario por laboratorio** | Selector de laboratorio y cuadrícula semanal con **un cuadro por hora** (lunes a sábado, 07:00 a 21:00). El administrador selecciona uno o varios cuadros libres, arrastrando o con Ctrl+clic, incluso en varios días, y les **asigna una clase** (maestro, materia, grupo, código) desde un diálogo: cada bloque contiguo se convierte en una franja y los solapes se informan por bloque. Clic en una franja: menú con editar, borrar, cancelar solo el próximo día y quitar esa cancelación. El arrastre empieza en cualquier cuadro, libre u ocupado, y `Ctrl+clic` suma cuadros sueltos; con varios cuadros seleccionados el menú ofrece **Aplicar excepción**: en un diálogo se elige la fecha (la primera que caiga en ese día de la semana), si solo se cancelan las franjas de esas horas ese día o si entra una sesión extra de otra clase, y el motivo; si la extra choca con franjas del horario se aplica igual y esas franjas quedan canceladas ese día, porque ya se habló con esos maestros. Los colores de las franjas son **por maestro**. Doble clic: editar o nueva franja con horas a media hora. *Imprimir* a PDF queda pendiente. |
| **Excepciones** | Filtro **Día / Semana / Mes** más laboratorio, con un **calendario propio** siempre visible a la izquierda (mes completo, flechas para cambiar de mes, hoy con borde naranja). Los días que no se pueden elegir salen **sombreados en gris** según el modo: en *Día* se elige cualquier día; en *Semana* sólo los lunes, y al elegir uno se marca la semana entera de lunes a domingo; en *Mes* ningún día es seleccionable y sólo se cambia de mes con las flechas, que marcan el mes completo. La selección se resalta en verde con un borde alrededor de todo el rango. En *Día* se lista la agenda completa de ese día (franjas efectivas y extras), que es desde donde se cancela una clase. En *Semana* (lunes a domingo) y *Mes* se listan **solo las excepciones** del rango, con su fecha, para revisarlas sin ir día por día. Columnas: Fecha, Inicio, Fin, Código, Materia, Grupo, Profesor, Estado ("normal" / "CANCELADA: motivo" / "extra: motivo"); canceladas en gris, extras en naranja claro, y un rótulo con el rango y cuántas excepciones hay. Acciones: *Cancelar este día* sobre una franja normal (con motivo), *Quitar cancelación*, *Sesión extra* (clase, inicio, fin, motivo; validada contra la agenda efectiva) y *Quitar extra*. |

Los botones de usuarios, laboratorios y grupos que ya tiene
`PrincipalAdministrador` se quedan.

### Profesor

- Se quita **Generar código**.
- **Mis clases**: sus clases con código, grupo, materia y vigencia, y debajo
  su horario semanal (solo lectura), incluidas las excepciones de la semana.
- **Bitácora de mis clases** (`BitacoraMisClases`): su horario semanal, como
  cuadrícula de solo lectura, ocupando toda la ventana. Al **pulsar una clase**
  se abre `BitacoraSesion`, una ventana aparte con quién registró su bitácora
  en ella: alumno, matrícula, equipo y una columna por área (Red, Hardware,
  Software) que muestra lo que reportó y queda vacía si no reportó nada, con la
  celda de la falla en naranja intenso. Esa ventana lleva la cuenta atrás hasta
  la hora de salida, se refresca cada diez segundos (y si al abrirla aún no
  había registros, se llena sola cuando entra el primer alumno) y descarga el
  PDF de la sesión. Sustituye a la pantalla que pedía el código a mano
  (`Bitacora_profesor`, eliminada), así que un profesor sólo ve sus propias
  clases. El panel del administrador ya no la abre: su botón "Descargar
  bitácora" lleva a "Ver bitácoras", que hace lo mismo y más.
- **Mis sesiones** (`HistorialCodigosProfesor`): la lista de sus sesiones
  pasadas (fecha, laboratorio, grupo, materia, alumnos registrados) con su PDF,
  con filtros de texto y fecha.

### Alumno

- La misma pantalla de código, con el panel "clase en curso" arriba y los
  mensajes nuevos de la tabla de arriba.

## Orden de trabajo

| Paso | Qué | Estado (2026-09-19) |
|---|---|---|
| 0 | Administrador real (tipo 3, login, seed, migración). PBKDF2 se dejó para después | Hecho |
| 1 | `db/01_schema.sql` con las tablas nuevas, `db/04_migracion_horarios.sql` para la base existente, seed de ejemplo | Hecho; migración probada sobre una base "vieja" con bitácoras |
| 2 | Laboratorio del equipo: `appSettings`, variable de entorno, resolución al arrancar, `configurar_equipo.ps1 -Laboratorio` | Hecho |
| 3 | Capa de datos: `HorariosDatos` (`ResolverCodigo`, sesión al primer uso, agenda, validaciones) y `Consultas.Sesiones` (consultas por id) | Hecho; 25 pruebas en `deploy\probar_horarios.ps1` contra un MySQL 8.0 portátil |
| 4 | Pantallas del administrador: clases, horario, excepciones (`AdministrarHorarios.cs`) | Escrito; compila; sin probar con datos reales |
| 5 | Profesor: fuera generar código; `MisClasesProfesor.cs`; historial y PDF por sesión | Escrito; compila; sin probar con datos reales |
| 6 | Alumno: clase en curso, mensajes nuevos, registro con grupo | Hecho |
| 7 | README y este documento | Hecho |

Las pruebas se hicieron con un MySQL 8.0 portátil (zip, sin instalar) en la
carpeta temporal de la sesión, puerto 3307. En el TESCHI queda: correr la
migración 04 con la contraseña del administrador puesta, configurar el
laboratorio de cada equipo y recorrer las pantallas con datos reales.

## Riesgos y decisiones abiertas

- **Código fijo compartido.** El sistema no comprueba que el alumno que teclea
  el código pertenezca al grupo de esa clase. Decisión del usuario
  (2026-09-19): **no se valida**. El acceso físico al laboratorio lo controlan
  maestros y administradores, y además lo acotan el laboratorio, la franja y la
  bitácora única por sesión. Se llegó a marcar en naranja al alumno de otro
  grupo, pero se **retiró el 2026-09-20**: el naranja ahora señala las
  bitácoras que reportan alguna falla o comentario, que es lo que hay que
  atender. `RegistroAlumnos` sigue guardando carrera y grupo del alumno.
- **Alumno que llega tarde a una sesión extra** o **profesor que cambia de
  laboratorio sin avisar**: sin excepción registrada, el código no entra. Es
  el comportamiento pedido (punto 3), pero conviene que el administrador pueda
  crear la extra en el momento, desde cualquier equipo.
- **Cambio de semestre.** Nuevas clases con nueva vigencia; las anteriores
  caducan solas por fecha. El historial de sesiones y bitácoras se conserva.
- **Reloj.** Todo con `NOW()` del servidor, como en la fase 2. Día de la
  semana con `WEEKDAY(CURDATE())`, también del servidor.
- **Franjas que cruzan medianoche** no se contemplan: el laboratorio abre de
  07:00 a 21:00.
