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

> **Cambio del 2026-10-13: el historial no se reescribe (`db/20_vigencia_franjas.sql`).**
> Una franja valía todas las semanas de la vigencia de su clase, así que moverla,
> quitarla o añadirla a medio semestre cambiaba también las semanas pasadas:
> Faltas las juzgaba con el horario nuevo, las sesiones viejas dejaban de cuadrar
> y borrarla se llevaba sus cancelaciones. Desde la 20 cada franja tiene su propia
> vigencia (`horarios.vigente_desde` y `vigente_hasta`; vacías = las de su clase).
> Una franja **se da** un día si su clase está activa y el día cae dentro de la
> vigencia de la clase y de la suya (`conexion\VigenciaFranjas.cs`), y así lo
> deciden la agenda, la resolución del código, Faltas, la asistencia, la copia
> local y la cola de bitácoras. La regla nueva:
>
> - Una franja **tiene historia** si ya tiene sesiones o cancelaciones, o si su
>   vigencia ya empezó (alguna semana ya se dio).
> - Con **Ajustes → «Los cambios de horario aplican desde una fecha»** encendido
>   (`configuracion.franjas_con_fecha = '1'`), cambiar una franja con historia
>   termina la vieja el día anterior a la fecha D (D desde hoy; desde mañana si
>   hoy ya tuvo bitácoras) y crea la nueva desde D. Sus cancelaciones desde D
>   pasan a la nueva si caen en su día de la semana; si no, hay que quitarlas
>   antes. Quitarla le pone fin en vez de borrarla. Una franja nueva en una clase
>   que ya empezó vale desde la fecha elegida (nunca antes de hoy). Una franja sin
>   historia se edita en su sitio, como antes.
> - Los recortes (lo que le queda a una clase recortada, una extra dentro de su
>   franja cancelada) siguen a su cancelación: al quitar la franja desde D se
>   quitan con ella (si alguno ya tiene bitácoras, no se puede quitar desde esa
>   fecha); al cambiarla desde D, si un recorte no cabe en las horas nuevas o la
>   franja cambia de clase o de laboratorio, hay que quitar antes ese recorte en
>   Excepciones. Si D es hoy, la sesión de hoy de la franja vieja sin bitácoras
>   queda sin enlace (no se borra: puede tener reportes del profesor).
> - `deploy\importar_horarios.ps1` solo pone `vigente_desde` a las franjas nuevas
>   con la 20 y la casilla encendida; apagada, valen toda su clase, como antes.
> - Apagado (o sin la 20), todo como antes, con un aviso de que el cambio
>   alcanza también a las semanas pasadas.
> - Clases, con o sin la 20: una clase con sesiones no se desactiva, se termina
>   en una fecha (`vigente_hasta`, nunca antes de su última sesión); con sesiones
>   pasadas o con bitácoras no cambia de profesor, grupo ni materia; no se cruza
>   con otra activa del mismo profesor, grupo y materia. La baja de un profesor
>   «con sus clases» termina las que tienen sesiones y desactiva las demás.
> - La copia local guarda la vigencia de cada franja (`<franja ... desde hasta>`,
>   opcionales), y una bitácora encolada sin red nunca se enlaza a una franja que
>   ese día no se daba: va a la que la sustituye (misma clase y laboratorio,
>   mismo día, que cubre su hora) o queda sin enlace.
> - El generador de códigos semanales (`CodigosGenerados.DeLaboratorio`) sigue
>   contando **todas** las franjas del laboratorio, vigentes o no: si dejara de
>   contar una, cambiarían los códigos de las demás a media semana.
>
> **Orden en la escuela:** actualizar DigBit en todos los equipos → aplicar la
> 20 → encender la casilla en Ajustes. Un DigBit anterior no conoce las columnas
> y vería a la vez la franja terminada y la nueva.

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
| `vigente_desde`, `vigente_hasta` | DATE NULL | db/20: desde y hasta cuándo se da la franja dentro de la vigencia de su clase; NULL = la de la clase. Una franja cambiada desde una fecha queda con `vigente_hasta` y la nueva con `vigente_desde` |

Validaciones en la aplicación al guardar una franja (MySQL no las puede
expresar solo), entre franjas que se dan en algún día en común (con la 20, una
terminada no choca con la que la sustituye):

- no se solapa con otra franja **del mismo laboratorio** el mismo día;
- el **profesor** de la clase no está en otro laboratorio a esa hora;
- el **grupo** no está en otro laboratorio a esa hora.

La cuadrícula enseña **una semana** (◀ ▶ y *Esta semana*; al abrir, la de
hoy, o la siguiente si es domingo): cada columna dice su fecha (hoy, con otro
fondo) y encima se dibujan las excepciones de esa semana, también las que ya
pasaron:

- **Sesión extra**: como una clase más (el color de su profesor, quién, la
  materia y el grupo), con marco naranja y `EXTRA dd/MM` donde va el código.
  Si cae sobre una franja que ese día **no** está cancelada (un choque), va
  rayada en naranja encima de la franja. Clic encima: quitarla o ver ese día
  en la pestaña Excepciones.
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
laboratorio: franjas no canceladas más otras extras. No se puede poner una
cuya hora de fin ya pasó (nadie la podría usar); cancelar hacia atrás sí,
porque es como se justifica una clase que no se dio.

**Excepción sobre clases del horario** (`HorariosDatos.AplicarExcepcion`, desde
la cuadrícula): a lo que se cruza se le quita **solo lo que se cruza**, sin
migrar nada. La franja se cancela ese día y lo que le sobra queda como sesión
extra de su misma clase (motivo "Le queda de su horario"); una extra que se
cruce se acorta, se parte o se quita. Así, una extra de Abraham de 07:00 a
09:00 sobre Alejandro de 08:00 a 10:00 deja a Alejandro de 09:00 a 10:00. Todo
en una transacción por bloque: si la extra nueva choca con algo (el profesor o
el grupo en otro laboratorio) o hay que tocar algo con bitácoras, no cambia
nada. Para deshacer en grupo: *Quitar extra* pregunta si se devuelve su
horario a lo que esa extra canceló o recortó (`CancelacionesBajoExtra`), y
*Quitar cancelación* quita también lo que le quedaba a la clase
(`RestosDeCancelacion`); ninguno toca algo que ya tenga bitácoras.

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

Con la **generación de códigos** (db/11, modo `renovable`) tampoco se crea
nada por adelantado: el código de cada clase y día sale de un secreto de la
escuela (`configuracion`, db/13) con HMAC-SHA256, así que existe desde el lunes
de su semana sin guardarlo (`CodigosGenerados` en `conexion\ModosDeAcceso.cs`).
La sesión se sigue creando al abrir la clase (el panel del profesor en su
hora, o el administrador) o cuando un alumno teclea ese código, y lleva ese
mismo código. Por eso una clase a la que nadie fue sigue sin dejar rastro, y
"Mis bitácoras" del profesor nunca enseña códigos de días que no han llegado.

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
| **Horarios** | Selector de laboratorio y cuadrícula semanal con **un cuadro por hora** (lunes a sábado, 07:00 a 21:00). El administrador selecciona uno o varios cuadros libres, arrastrando o con Ctrl+clic, incluso en varios días, y les **asigna una clase** (maestro, materia, grupo, código) desde un diálogo: cada bloque contiguo se convierte en una franja y los solapes se informan por bloque. Arriba, la **semana** que se ve. Clic en una franja: menú con editar, borrar, cancelar solo ese día (el de su columna en la semana que se ve; si ya pasó, queda como no dada) y quitar esa cancelación. El arrastre empieza en cualquier cuadro, libre u ocupado, y `Ctrl+clic` suma cuadros sueltos; con varios cuadros seleccionados el menú ofrece *Asignar clase* (solo a los libres, para todas las semanas) y **Aplicar excepción** (solo ese día de la semana elegida): en un diálogo se puede cambiar de semana, se elige si solo se quitan esas horas o si entra una sesión extra de una clase, y el motivo; un resumen dice qué pasará con cada clase que se cruza ("se le quita 08:00-09:00; ese día da 09:00-10:00") y al aplicar se enseña lo que se hizo y la cuadrícula va a esa semana. Los colores de las franjas son **por maestro**. Doble clic: editar o nueva franja con horas a media hora. *Imprimir* a PDF queda pendiente. Con **generación de códigos** (db/11), cada clase enseña, en el sitio del código, el suyo de ese día: el de su sesión si ya se abrió y, si no, el generado; solo de las semanas que ya empezaron (los de una semana se generan su lunes) y solo aquí, no en el horario de un profesor. |
| **Clases** | Lista de clases (profesor, grupo, materia, código, vigencia, activa). Arriba, un **buscador** (profesor, materia, grupo, código o número de empleado; sin importar acentos ni mayúsculas; con varias palabras salen las clases que las tienen todas) y una lista **Profesor** con el color y el número de clases de cada uno; los dos filtran lo ya leído, sin volver a la base. Nueva: elige profesor, grupo y materia; el código se propone aleatorio y se puede editar (se comprueba que no exista); vigencia con dos fechas. Editar: código y vigencia siempre (la vigencia sin dejar fuera ninguna de sus sesiones); profesor/grupo/materia solo si la clase aún no tiene sesiones pasadas ni con bitácoras (lo comprueba también la capa de datos). Desactivar si no tiene sesiones; con sesiones, *Terminar clase...* en una fecha. |
| **Excepciones** | Filtro **Día / Semana / Mes** más laboratorio, con un **calendario propio** siempre visible a la izquierda (mes completo, flechas para cambiar de mes, hoy con borde naranja). Los días que no se pueden elegir salen **sombreados en gris** según el modo: en *Día* se elige cualquier día; en *Semana* sólo los lunes, y al elegir uno se marca la semana entera de lunes a domingo; en *Mes* ningún día es seleccionable y sólo se cambia de mes con las flechas, que marcan el mes completo. La selección se resalta en verde con un borde alrededor de todo el rango. En *Día* se lista la agenda completa de ese día (franjas efectivas y extras), que es desde donde se cancela una clase. En *Semana* (lunes a domingo) y *Mes* se listan **solo las excepciones** del rango, con su fecha, para revisarlas sin ir día por día. Columnas: Fecha, Inicio, Fin, Código, Materia, Grupo, Profesor, Estado ("normal" / "CANCELADA: motivo" / "extra: motivo"); canceladas en gris, extras en naranja claro, y un rótulo con el rango y cuántas excepciones hay. Acciones: *Cancelar este día* sobre una franja normal (con motivo), *Quitar cancelación* (con lo que le quedaba a la clase si se recortó), *Sesión extra* (clase, inicio, fin, motivo; validada contra la agenda efectiva) y *Quitar extra* (pregunta si se devuelve lo que canceló). La columna **Código** dice con qué se entra ese día: el código del profesor, el generado de esa clase (generación de códigos) o «—» (sin códigos). |
| **Faltas** (antes «Calendario») | Qué clases se quedaron **sin bitácoras** (`PestanaCalendario`, `HorariosDatos.LeerCalendario`). Se elige un **laboratorio** arriba (de entrada, el que se miraba en Horario) y a la izquierda sale el **calendario del mes** (◀ ▶ para cambiar de mes): cada día en **rojo** si alguna de sus clases se quedó sin bitácoras (con cuántas), **verde** si todas las que contaban registraron, gris si no contaban o aún no pasan, blanco sin clases; hoy con borde naranja. **Al pulsar un día** se elige su semana (fila con marco verde; también con las flechas del teclado) y, debajo del calendario, un resumen del mes. La casilla *Contar también los laboratorios que aún no usan DigBit* empieza **sin marcar** (se recuerda en la sesión), igual que en las Alertas: un laboratorio cuenta desde su primera bitácora con DigBit y lo de antes, como los laboratorios que aún no lo usan, sale en gris, así que el rojo es siempre una falta de verdad. Marcada, toda clase que ya pasó sin bitácoras sale en rojo, también en los laboratorios que todavía registran en el sistema anterior, donde ese rojo no quiere decir que no se dio la clase. Se leen las seis semanas que se ven del mes. A la derecha, el horario de la semana elegida **tal cual, sin los colores ni los códigos de cada docente**: en **rojo** las clases que ya pasaron y en las que nadie registró bitácora; en blanco las que sí, con cuántas; en gris las que aún no terminan o no cuentan (el laboratorio todavía no registraba con DigBit, la persona docente estaba de baja, la extra se puso cuando ya había pasado); rayadas las canceladas. Un laboratorio cuenta desde su primera sesión enlazada al horario. Las sesiones sin enlace se emparejan por laboratorio, día, profesor y horas; las que no son de ninguna clase se listan aparte. En los laboratorios con código fijo, quien llega en los 15 minutos antes de la segunda de dos clases seguidas del mismo profesor queda en la sesión de la primera: se le cuentan a la segunda (por `registrado_en`, hasta 10 minutos después de su inicio, lo que tarda en llenarse la bitácora); y lo que le queda a una clase recortada (aunque se recortara con la clase empezada o ya pasada) cuenta con las bitácoras de su franja. Cada semana se juzga con las franjas que se daban entonces (su vigencia propia, db/20): con la casilla de Ajustes encendida, mover, quitar o añadir una franja no cambia las semanas anteriores. Sin la 20, o con la casilla apagada, las semanas pasadas se juzgan con el horario de hoy (si se mueve o se añade una franja, sus semanas anteriores salen con la nueva). Clic en una clase: ver quién registró, o marcar una en rojo como no dada (se cancela ese día, con motivo). |

La ventana abre **maximizada** en la pestaña Horarios (las cuatro, en este orden: Horarios, Clases, Excepciones y Faltas) y usan todo el espacio: en
la cuadrícula cada clase enseña el profesor y el código en la primera línea,
la materia con el grupo a la derecha en la segunda y la hora si cabe.

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
  atender. Desde el 2026-09-29 el alumno tampoco declara grupo: al darse de
  alta ("Crear cuenta", `DialogoUsuario`) guarda su carrera y, si quiere, su
  semestre.
- **Alumno que llega tarde a una sesión extra** o **profesor que cambia de
  laboratorio sin avisar**: sin excepción registrada, el código no entra. Es
  el comportamiento pedido (punto 3), pero conviene que el administrador pueda
  crear la extra en el momento, desde cualquier equipo.
- **Cambio de semestre.** Nuevas clases con nueva vigencia; las anteriores
  caducan solas por fecha. El historial de sesiones y bitácoras se conserva.
- **Equipos con un DigBit anterior a la 20** (Deep Freeze, reimaginados cada
  semestre): deciden con su propio SQL y no conocen la vigencia de las franjas.
  Con la casilla de Ajustes encendida verían a la vez la franja terminada y la
  nueva. Por eso la casilla se enciende solo después de actualizarlos todos, y
  un equipo reimaginado con una versión vieja hay que actualizarlo antes de
  usarlo.
- **Historia que ya se perdió en la escuela.** Antes de la 20, desactivar una
  clase escondía su pasado en Faltas y en la asistencia, y la base no guarda
  cuándo se desactivó; las franjas borradas dejaron sus sesiones sin enlace y
  las editadas en su sitio cambiaron sus semanas pasadas. Lo editado y borrado
  no se puede reconstruir; las clases desactivadas con sesiones se pueden
  reactivar terminándolas en su última sesión, si el usuario lo decide
  (`docs/pendientes.md`, punto 20).
- **Franja partida a media semana con generación de códigos.** La que la
  sustituye tiene otro código el resto de esa semana (el código sale del id de
  la franja); si el administrador ya lo había dictado, conviene que el cambio
  empiece el lunes siguiente.
- **Reloj.** Todo con `NOW()` del servidor, como en la fase 2. Día de la
  semana con `WEEKDAY(CURDATE())`, también del servidor.
- **Franjas que cruzan medianoche** no se contemplan: el laboratorio abre de
  07:00 a 21:00.
