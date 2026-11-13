# DigLab 2.0

> **⚠ Pendiente en la escuela (13/11/2026):** antes de instalar DigLab 1.3, hay que aplicar
> `db/29_actualizaciones.sql` en la base de la escuela con `deploy/migrar_bd.ps1`. Los pasos
> están en [CLAUDE.md](CLAUDE.md). El instalador 1.3 es `instalador/DigLab-Setup.exe`.

Sistema de registro de bitácoras para los laboratorios de cómputo del TESCHI.
Aplicación de escritorio **WinForms sobre .NET Framework 4.7.2** con base de
datos **MySQL**.

> **Antes se llamaba DigBit.** Conservan ese nombre, a propósito, lo que no está en
> este código: las cuentas de MySQL (`digbit_equipo`, `digbit_admin`,
> `digbit_consulta` y `digbit_api`), porque viven en el servidor y cambiarlas pide
> root y volver a configurar cada equipo; la cuenta de Gmail `digbit.teschi@gmail.com`;
> y el repositorio de GitHub. También `digbit_clave_pendiente`, tabla de ayuda de una
> migración ya aplicada. DigLab sigue leyendo un `connections.config` con el nombre
> anterior y, al primer arranque en cada cuenta de Windows, toma la carpeta
> `%LOCALAPPDATA%\DigBit`, con su cola de bitácoras (`NombreAnterior`).

## Cómo funciona

```
La administración arma el horario de cada laboratorio:
    clase = docente + grupo + materia + CÓDIGO FIJO, en franjas semanales
        │
        ▼
Cada estudiante inicia sesión en un equipo del laboratorio → según el laboratorio,
        │   teclea el código de su docente, el generado de su clase de esa
        │   semana o ninguno (ver "Tres formas de entrar a la clase")
        │   (solo vale en ESE laboratorio y dentro de SU franja; la administración
        │    puede cancelar una franja un día o programar una sesión extra)
        ▼
Llena la bitácora de fallas (red / hardware / software)
        │   → el equipo queda liberado hasta la hora de salida
        │   → si marcó "equipo propio": el equipo NO se libera; elige apagarlo
        │      o volver al login para la siguiente persona
        ▼
Docentes y la administración descargan las bitácoras en PDF
    (celda naranja: la falla concreta que se reportó)
        │
        ▼
La administración revisa en Horarios → Faltas qué clases se quedaron sin
    bitácoras: por laboratorio, un mes con los días en rojo y, al elegir uno,
    el horario de esa semana con esas clases en rojo
        │
        ▼
En Estadísticas, por periodo: cuántas veces entró cada estudiante a sus
    clases, qué clases se dieron y qué fallas se reportaron
```

Si el equipo de un estudiante no pudo registrar (no tenía internet o no
funcionaba), su docente le da la asistencia en Mis grupos mientras dura la
clase (db/22; ver «Panel docente»).

Cuatro roles, los cuatro en la tabla `usuarios`:

| Rol | `fk_tipo_usuario` | Pantalla principal |
|---|---|---|
| Estudiante | 1 | `Alumno_Principal` (ventana «DigLab - Estudiante») |
| Docente | 2 | `Profesor_Principal` (ventana «DigLab - Docente») |
| Administración | 3 | `PrincipalAdministrador` (ventana «DigLab - Administración») |
| Responsable de laboratorio | 4 | `PrincipalAdministrador`, en solo consulta (ventana «DigLab - Responsable de laboratorio»; ver «Responsable de laboratorio») |

## Requisitos

- Windows 10/11.
- **Build Tools for Visual Studio 2022** (o Visual Studio 2022) con la carga de
  trabajo *Desarrollo de escritorio de .NET*. `build.ps1` localiza MSBuild con
  `vswhere`, así que no importa dónde esté instalado.
- **MySQL 8.x** accesible desde el equipo.
- Conexión a internet la primera vez (para descargar `nuget.exe` y los paquetes).

## Puesta en marcha

Esta sección es para desarrollar. Para instalar DigLab en los equipos de una
escuela, ver `INSTALAR.md` y la sección 7.

### 1. Base de datos

El nombre de la base **tiene que ser `teschi_otru`**: seis consultas de
`Consultas.cs` lo llevan escrito en duro. Los scripts ya lo crean con ese nombre.

Desde **cmd** (en PowerShell el `<` no funciona):

```bat
:: crea la base y las 14 tablas
mysql -u root -p < db\01_schema.sql
:: catálogos y usuarios de prueba (solo desarrollo)
mysql -u root -p < db\02_seed.sql
```

Si la base **ya existía** (por ejemplo la del servidor del TESCHI), no la
recrees: aplica las migraciones en orden, cada una una sola vez. La de la fase
6 da de alta la cuenta de la administración (**edita su contraseña en el script antes**),
crea las tablas de horarios y convierte `codigos_accesos` en "sesión":

```bat
mysql -u root -p < db\03_migracion_fase2.sql
mysql -u root -p < db\04_migracion_horarios.sql
mysql -u root -p < db\05_migracion_sin_conexion.sql
mysql -u root -p < db\06_migracion_contrasenas.sql
mysql -u root -p < db\08_migracion_bajas.sql
mysql -u root -p < db\09_codigo_por_profesor.sql
mysql -u root -p < db\10_red_y_autoregistro.sql
mysql -u root -p < db\11_modo_de_acceso.sql
mysql -u root -p < db\12_color_profesor.sql
mysql -u root -p < db\13_recuperacion_contrasena.sql
mysql -u root -p < db\14_registro_sin_clase.sql
mysql -u root -p < db\15_reportes_profesor.sql
mysql -u root -p < db\16_ciclo_preliminar.sql
mysql -u root -p < db\17_asistencia_retirada.sql
mysql -u root -p < db\18_encargados.sql
mysql -u root -p < db\19_personalizacion_clases.sql
mysql -u root -p < db\20_vigencia_franjas.sql
mysql -u root -p < db\21_historial_cambios.sql
mysql -u root -p < db\22_asistencia_dada.sql
mysql -u root -p < db\23_horarios_guardados.sql
mysql -u root -p < db\24_docentes_siia.sql
mysql -u root -p < db\25_horas_por_asignar.sql
mysql -u root -p < db\26_fallas_resueltas.sql
mysql -u root -p < db\27_suspensiones.sql
mysql -u root -p < db\28_memorias_emergencia.sql
```

O todas de una vez, con respaldo antes y revisión, desde PowerShell:
`deploy\migrar_bd.ps1` (ver INSTALAR.md, «Migrar una base que ya existe»).

La 10 es la que permite dar de alta estudiantes en la base del TESCHI: ahí
`carrera_grupo_semestre` exigía grupo y semestre, y quien se registra ya no declara
grupo. Sin ella fallan tanto «Registrar estudiante» de la administración como "Crear
cuenta" del login. La 12 guarda el color que la administración elige para cada
docente (en Docentes, al registrar o editar su cuenta); sin ella el color sale
solo, por el orden de su primera clase. La 11 añade el modo de acceso de cada
laboratorio (ver "Tres formas de entrar a la clase") y deja todos en código
fijo, así que aplicarla no cambia nada. La 18 es la de las personas responsables
de laboratorio: el tipo de usuario 4 y su cuenta de MySQL de solo lectura,
`digbit_consulta`, que solo puede crear **root** (ver «Responsable de
laboratorio»). Hace falta también en una base nueva hecha con `01_schema.sql`,
y otra vez si se rehace: la cuenta es del servidor y su contraseña se guarda en
`configuracion`. La 19 crea `personalizacion_clases`, lo que cada docente
elige para sus clases (una imagen en un círculo, un color y un nombre corto):
solo lo ven sus pantallas y no cambia nada de la clase. Desde que se aplica,
cada una de sus materias sale con sus iniciales y su propio color aunque no
elija nada; la administración y las personas responsables de laboratorio lo ven todo como antes. Crea una
tabla nueva y no copia nada: se puede aplicar en horario de clase. La 20 da a
cada franja su propia vigencia (`horarios.vigente_desde` y `vigente_hasta`,
vacías = las de su clase) para que cambiar o quitar una franja no reescriba las
semanas pasadas (ver «Cambios de horario sin reescribir el pasado»). Aplicarla
no cambia nada: DigLab solo parte las franjas cuando, además, la administración
lo enciende en Ajustes, y eso se hace después de actualizar **todos** los
equipos. La 21 crea `historial_cambios`: quién cambió el horario, las clases,
los laboratorios o una bitácora, cuándo y qué decía antes (ver «Historial de
cambios y alertas de cambios de horario»). Crea una tabla nueva y no toca nada
más; con root, además deja leerla a `digbit_consulta`. La 22 crea
`asistencias_dadas`: la asistencia que el docente da, mientras dura su clase, a
quien vino pero no pudo registrar su bitácora (su equipo no tenía internet, no
funcionaba o no había uno libre), con quién, cuándo y por qué; anularla no la
borra. Crea una tabla nueva y no toca nada más (se puede aplicar en horario de
clase), pero se aplica **después** de actualizar **todos** los equipos
(laboratorios, docentes y administración): un DigLab anterior no la lee y vería
falta donde el docente dio la asistencia. Con root, además deja a `digbit_equipo`
leerla, dar y anular (solo `activa`, `anulada_en` y `anulada_por`; nunca borrar)
y a `digbit_consulta`, leerla; con otro usuario, lo dice con un AVISO.
`deploy\migrar_bd.ps1` las aplica todas en orden, con respaldo y revisión
previa (también la 22: pásalo cuando ya estén actualizados todos los equipos).

Usuarios que deja el seed:

| Usuario | Contraseña | Rol |
|---|---|---|
| `20230001` | `alumno123` | estudiante, grupo ISC-5001 |
| `20230002` | `alumno123` | estudiante, grupo ISC-5002 |
| `EMP001` a `EMP006` | `profesor123` | docentes |
| `ADMIN001` | `admin123` | administración |

No deja responsables de laboratorio: se dan de alta desde DigLab, que guarda su
contraseña en PBKDF2 (ver «Responsable de laboratorio»).

El seed deja un **horario realista** de un semestre: cuatro laboratorios, seis
docentes, siete grupos, once materias y dieciocho clases con su código fijo en
bloques de dos horas sin choques. Sirve para ver la cuadrícula de
Horarios con datos parecidos a los reales. Algunos códigos útiles con el
equipo configurado como `Laboratorio de Computo 1`:

| Código | Clase | Qué prueba |
|---|---|---|
| `AB12c` | Bases de Datos, ISC-5001, EMP001 | Lunes y miércoles 08:00 a 10:00 en el Lab 1. Además, el seed le deja una sesión extra en el Lab 1 desde la hora en que se carga y durante cuatro horas, para probar el registro de bitácoras sin esperar a su franja (recarga el seed cuando venza) |
| `QW7pz` | Redes, ISC-5002, EMP002 | Lab 2: desde el Lab 1 responde "es de otro laboratorio" |
| `EXP01` | POO, ISC-7001, EMP001 | Vigencia terminada hace un mes: en la cuadrícula ya no sale; en Clases, en gris |
| `FUT01` | Redes, ISC-5001, EMP001 | Vigencia empieza dentro de un mes: en la cuadrícula sale desde esa semana; en Clases, «Por empezar» |
| `NOHOY` | Taller de Titulación, ISC-7001, EMP005 | Sin franjas: "hoy no hay clase"; tiene una sesión extra mañana en el Lab 3 |

También deja una cancelación de ejemplo (AB12c del próximo miércoles, "Junta
académica") y una sesión extra mañana en el Lab 3, que se ven resaltadas en la
cuadrícula de Horarios, y cuatro sesiones de la semana pasada con once
bitácoras entre todas, unas con fallas y otras sin novedad, para ver la
pantalla de bitácoras y las filas naranja en el PDF.
Los guiones de prueba crean sus propios datos (códigos `TST…`) y los borran.

### 2. Credenciales de conexión

`build.ps1` copia `DigLab\connections.config.example` a
`DigLab\connections.config` si no existe. Edita ese archivo con tus datos:

```xml
<add name="DigLab"
     connectionString="Database=teschi_otru;Server=localhost;User Id=diglab;Password=TU_CONTRASENA"
     providerName="MySql.Data.MySqlClient" />
```

`connections.config` está en `.gitignore`: nunca se versiona.

Alternativa sin tocar archivos, útil para despliegues: la variable de entorno
`DIGLAB_CONNECTION_STRING` tiene prioridad sobre el archivo.

### 3. Compilar y ejecutar

```powershell
.\build.cmd                          # restaura paquetes, compila en Debug y lanza la app
.\build.cmd -Configuration Release   # lo mismo en Release
.\build.cmd -Rebuild                 # recompila desde cero
.\build.cmd -NoRun                   # solo compila
.\build.cmd -Wait                    # espera a que cierres la app
```

`build.cmd` es un envoltorio de `build.ps1` que evita el bloqueo de *execution
policy*; puedes llamar a cualquiera de los dos.

**Qué hace el script, en orden:**

1. Localiza MSBuild con `vswhere`.
2. Localiza `nuget.exe` (en el `PATH` o en `tools\`; si no está, lo descarga).
3. Ejecuta `nuget restore` → llena `packages\`.
4. Cierra instancias abiertas de DigLab (bloquean el `.exe` al enlazar).
5. Crea `connections.config` desde la plantilla si falta.
6. Compila `DigLab.sln`: DigLab, el servicio `DigLab.Vigilante`, `DigLab.Configurador` y `DigLab.Instalador`.
7. Lanza `DigLab\bin\<Configuration>\DigLab.exe`.

### 4. Modo kiosco y registro de errores

DigLab está preparado para correr como **shell de Windows** en los equipos del
laboratorio (sin escritorio ni barra de tareas). Ese comportamiento se activa
con una bandera; apagada, la app es una ventana normal, que es lo cómodo para
desarrollar.

| Dónde | Valor | Prioridad |
|---|---|---|
| Variable de entorno `DIGLAB_MODO_KIOSCO` | `1` / `true` | Alta |
| `App.config` → `<appSettings>` → `ModoKiosco` | `true` / `false` (por defecto `false`) | Baja |

Con el kiosco **activado**:

- Los formularios principales ocupan toda la pantalla, sin borde.
- `Alt+F4` no cierra nada; no hay botón de minimizar ni de salir en el login.
- **Salir** dentro de la app cierra la sesión (olvida la cuenta y vuelve a un
  login limpio), nunca termina el proceso.
- Si al arrancar no hay conexión con MySQL, aparece una pantalla de error sin
  botón *Cerrar* que **reintenta sola cada 30 segundos**, para que un equipo que
  arranca antes que la red se recupere sin intervención.

Con el kiosco **desactivado**, la misma pantalla de error ofrece *Reintentar* y
*Cerrar*, y cerrar la última ventana termina la aplicación.

En ambos modos:

- Solo puede haber **una instancia**; abrir una segunda trae al frente la que ya
  existe y termina.
- Cualquier excepción no controlada se registra y se muestra en una pantalla
  propia (nada de diálogos genéricos de .NET), y la app sigue funcionando.
- El **registro de diagnóstico** queda en `%LOCALAPPDATA%\DigLab\logs\diglab-AAAAMMDD.log`
  (un archivo por día). Es el primer sitio donde mirar cuando un equipo falla.
- El tiempo de espera de conexión es de **5 segundos** (el driver trae 15 por
  defecto, y toda consulta va en el hilo de interfaz). Si la cadena de conexión
  trae su propio `Connection Timeout`, se respeta.

Antes de mostrar el login, la app **comprueba la conexión** con el servidor. Si
falla, lo dice con claridad (configuración ilegible, servidor inalcanzable, base
inexistente) y señala la ruta del log.

### 5. Códigos de acceso y horarios por laboratorio

Los códigos **no los genera cada docente**: los asigna la administración al armar
el horario (diseño completo en `docs/fase6-horarios.md`).

- Una **clase** es docente + grupo + materia con un **código fijo** (5
  caracteres, único en toda la base) y una vigencia (el semestre).
- El **horario** de cada laboratorio son franjas semanales (día, hora de inicio,
  hora de fin) de esas clases. Al guardar una franja se rechaza que la misma
  persona docente o el mismo grupo estén a la misma hora en otro laboratorio. Si
  se cruza con otra clase del mismo laboratorio, con db/25 pregunta si la
  reemplaza, y lo que le quita queda en «Falta asignar», las horas que faltan por
  poner; sin la 25, se rechaza.
- Las **excepciones** son por día: cancelar una franja, o programar una **sesión
  extra** (el código de una clase en un laboratorio y horario temporales).
- Cada equipo sabe **en qué laboratorio está** (`appSettings` → `Laboratorio`,
  o la variable `DIGLAB_LABORATORIO`, que tiene prioridad). En kiosco, sin
  laboratorio configurado la app no arranca y lo dice en pantalla.

Cuando alguien teclea un código, la app lo resuelve **para ese laboratorio y
ese momento**, siempre con el reloj del servidor MySQL:

```
        ┌── 15 min de margen ──┐
  ──────┼──────────────────────┼════════ VIGENTE ════════┼──────────────
     "tu clase empieza      hora_inicio               hora_fin     "tu clase
      a las HH:mm"          de la franja              de la franja  terminó"
```

y rechaza con un mensaje claro cada caso: código inexistente o inactivo, clase
fuera de su periodo, hoy no hay clase, es de otro laboratorio, clase cancelada,
todavía no empieza, ya terminó, ya registraste. La pantalla del código muestra
además la clase en curso del laboratorio.

La **sesión** (una clase en una fecha) es una fila de `codigos_accesos` que se
crea sola la primera vez que alguien usa el código ese día (con la generación
de códigos, normalmente cuando su docente abre la clase); de ahí cuelgan las
bitácoras, los PDF y la liberación del equipo. Sigue habiendo **una bitácora por
estudiante y sesión** (`UNIQUE (fk_codigo_accesos, fk_usuario)`), y la ventana se
vuelve a comprobar al guardar.

#### Cambios de horario sin reescribir el pasado (db/20)

Faltas, la asistencia y las sesiones viejas se juzgan con el horario de cada
semana. Antes de la 20 una franja valía toda la vigencia de su clase, así que
moverla o quitarla a medio semestre cambiaba también las semanas pasadas. Con la
20 cada franja tiene su propia vigencia (`vigente_desde`, `vigente_hasta`;
vacías = las de su clase) y todo lo que decide si una franja «se da» un día usa
la de la clase recortada a la suya (`conexion\VigenciaFranjas.cs`).

- Una franja **tiene historia** si ya tiene sesiones o cancelaciones, o si ya se
  dio alguna semana.
- Con la casilla de Ajustes **«Los cambios de horario aplican desde una fecha»**
  encendida, cambiar una franja con historia (día, horas, laboratorio o clase)
  termina la vieja el día anterior a la fecha elegida (hoy o después; mañana si
  hoy ya hubo bitácoras o asistencias dadas en ella) y crea la nueva desde esa
  fecha. Sus cancelaciones de esa fecha en adelante pasan a la nueva si caen en
  su día; si
  no, se pide quitarlas antes. Quitar una franja con historia le pone fin en vez
  de borrarla, y una franja nueva en una clase que ya empezó vale desde la fecha
  elegida. Una franja sin historia se edita como siempre.
- Con la casilla apagada (o sin la 20) todo es como antes, pero Horarios avisa
  de que el cambio alcanza también a las semanas pasadas.
- Las **clases**, con o sin la 20: una clase con sesiones no se desactiva, se
  **termina** en una fecha (nunca antes de su última sesión), y con sesiones de
  días pasados, con bitácoras o con asistencias dadas (db/22) no cambia de
  docente, grupo ni materia. Dar de baja una cuenta docente con «Terminar también sus clases» termina las que tienen sesiones (hoy, o el
  día de su última sesión) y desactiva las demás.
- En los equipos, la copia local guarda la vigencia de cada franja (una copia
  vieja sin ella vale lo de antes), y una bitácora sin conexión nunca se enlaza
  a una franja que ese día no se daba: va a la que la sustituye o queda sin
  enlace, y Faltas la reconoce por docente, materia, grupo y horas.

**El orden importa**: actualizar DigLab en **todos** los equipos, aplicar la 20
y solo entonces encender la casilla en Ajustes. Un DigLab anterior no conoce
estas columnas y vería a la vez la franja terminada y la nueva (dos clases a la
misma hora, o la vieja en vez de la nueva).

#### Próximo ciclo: preparar y aplicar otro horario (db/16 y db/23)

La pestaña **Próximo ciclo** de Horarios prepara otro horario sin tocar el que se
usa, en tres pasos: **1. Plantillas** (las guardadas y los horarios ya aplicados),
**2. Preparación** (se usa como Horario semanal, también con «Falta asignar»,
«Imprimir PDF» y «Ver en el horario») y **3. Aplicar con fechas**
(`PestanaAplicacion`). Una plantilla es un horario guardado con nombre; en db/16,
db/23 y el código se llama «configuración de horario» o «ciclo preliminar».
En el paso 3, a la izquierda se decide (cuándo empieza, en dos tarjetas; cuándo
termina; el nombre) y a la derecha se ve cómo queda: **«Así quedarán las fechas»**
dibuja en una línea de tiempo el horario de ahora, el nuevo y la marca de hoy, con
los días sin clases en rayado y lo que se encima en rojo. «Antes de aplicar» revisa
el horario con esas fechas y dice en una frase si se puede aplicar: lo rojo no deja
aplicar, lo ámbar avisa y lo que está bien queda plegado. El botón dice qué va a
pasar («Aplicar ahora mismo...» o «Aplicar desde el 01/02/2027...») y vuelve a pedir
la contraseña de la administración.

Una plantilla o un horario aplicado se abre **en su propia ventana, solo para verlo**, sin
pestañas ni pasos: su semana por laboratorio y la tabla de docentes y clases (**«Ver docentes
y clases»**, en otra ventana). Crear uno con ella, cambiarle el nombre y borrarla están en la
lista del paso 1.

**«Crear horario nuevo...»** (en el paso 1) es el único botón para empezar uno. Pregunta cómo, y explica qué trae cada opción: copiar un horario
completo, con clases y horas; solo las clases, sin horas, para acomodarlas de nuevo; en
blanco; o **desde un Excel en cuadrícula** (abajo). El horario en uso se copia como está hoy, con lo que se le cambió durante el ciclo y
lo que le falta asignar, también de los laboratorios deshabilitados; una plantilla o un
horario que ya terminó, como se guardó. No hay que guardar aparte el horario de ahora: al
aplicar otro, la casilla «Guardar también el horario de ahora» viene marcada.

**El horario en Excel** (sin migración, `HorarioEnExcel.cs`): una hoja por laboratorio,
con los días en columnas y las horas en renglones. Cada casilla tiene **una flecha para
elegir la clase** de una lista, sin escribir: las clases del horario y las de Horarios que
siguen vigentes, como «Marco Llinas · 6ISC11 · Redes de computadoras» (docente y grupo
primero, porque Excel corta la lista al ancho de la columna; sin docentes de baja). Una
que no esté se escribe: materia, grupo y docente, cada uno en su renglón (o «Materia /
Grupo / Docente»); Excel avisa que no está en la lista y deja seguir. Una clase de dos
horas va en cada hora o en casillas combinadas. Cada casilla con clase se pinta del
**color de su docente** (el mismo de DigLab) con formato condicional, sin macros: la que
empieza como una opción de la lista o cuyo último renglón es el nombre completo; al
borrarla, el color se va. «Sin docente», en gris. En la preparación,
**«Excel ▾»** (o «Más acciones» si no cabe) descarga el horario así, con lo que ya tiene,
una hoja «Cómo llenarlo» y otra «Registrados» con los docentes, grupos y materias de
DigLab, y carga uno ya lleno; también se puede **arrastrar el archivo** hasta el horario,
o hasta el paso 2 vacío para crear uno con él. Solo valen los docentes, grupos y materias
registrados: si algo no lo está, o una hora no se entiende, dice qué y en qué casilla
(«¿es "Base de datos"?») y **no carga nada**. Si todo está bien, enseña por laboratorio lo
que trae el Excel frente a lo que tiene el horario, las clases que se quitan y los choques,
y al confirmar **reemplaza lo de esos laboratorios** en una sola transacción; los demás no
cambian. Una clase que ya estaba (mismo docente, grupo y materia) conserva sus horas de
otros laboratorios. Lee el .xlsx sin bibliotecas (`Infraestructura\LectorExcel.cs`); un
.xls antiguo pide guardarlo como .xlsx. Nada se usa hasta aplicarlo en el paso 3.

El horario nuevo empieza de una de dos formas:

- **«Ahora mismo»**: sustituye el horario en ese momento, también si el de ahora empezó
  hoy: el nuevo empieza hoy, al aplicarlo, y el de antes termina (ver «Sustituir lo que
  vale hoy», abajo). Con su casilla marcada, el de ahora queda antes como plantilla. En
  las listas de asistencia, lo ya registrado hoy cuenta una sola vez: en la clase nueva
  si la misma clase (docente, grupo y materia) sigue, o en la de antes si no. En
  Faltas cuenta en la clase nueva si se cruza con sus horas y aún no termina; si
  no, sale como «sesión fuera del horario». Las clases de hoy del horario nuevo que
  ya terminaron se cancelan con la marca de «No tomar en cuenta» (de Faltas): ahí
  salen canceladas, no en rojo, no salen en Alertas, y «Volver a contar...» las
  devuelve.
- **«Otro día»**: empieza en una fecha posterior a hoy, y se elige también el último
  día del horario de antes (con «Ahora mismo» es ayer y no se pregunta).

Aplicar agrega clases y franjas y adelanta la fecha de fin de las de antes. Según el
caso, también da código al docente que no lo tenía, pasa lo que falta asignar,
agrega las cancelaciones de «Ahora mismo» y guarda una plantilla con el horario de
ahora, si se deja marcada su casilla. Con «Otro día» no borra ni desactiva nada; con
«Ahora mismo» borra lo de hoy que no tiene nada registrado y las sesiones extra de hoy
en adelante que nadie abrió. Anota un solo renglón en el historial (`conexion\CicloPreliminarDatos.Aplicar.cs`). «Deshacer la
aplicación...» funciona mientras el horario no haya empezado ni tenga sesiones y no
haya otro en preparación; lo que empieza hoy ya no se deshace.

**Todas las clases de antes terminan en ese último día**, también las que siguen:
esas se crean otra vez como clases nuevas, con el mismo código de su docente. Solo
se quedan las que empiezan después, junto al horario nuevo: si una de esas es la misma
clase que trae el horario nuevo, sale en rojo y no deja aplicar.

**Lo que no viene en el horario nuevo**, sea una clase que se quitó de la plantilla
o un laboratorio entero, simplemente no se vuelve a crear:

- En Horario semanal sale en gris el resto de la semana de su último día, si nada
  vigente ocupa esas horas, y desde la semana siguiente ya no sale. Sus semanas
  pasadas siguen igual, y su historia queda en Faltas.
- Desde el día siguiente a su último día ya no se registra en ella con código ni
  cuenta faltas; el código de su docente sigue valiendo para sus otras clases. Un
  equipo sin red la acepta hasta renovar su copia local, y esas bitácoras se envían
  al volver la red.
- Un laboratorio que se queda así no queda deshabilitado: sigue en uso, sin clases,
  hasta que se le pongan o se aplique un horario que lo incluya.

**Sustituir lo que vale hoy** lo hace «Ahora mismo»: el horario en uso solo se quita al
aplicar otro. Hasta el 2026-11-12 también había un botón «Dar de baja el horario
actual...», que dejaba todos los laboratorios sin clases sin poner otro horario; se quitó
a pedido del usuario. Pide la contraseña de la administración, no se deshace, no necesita
migración y anota un solo renglón en el historial (`conexion\CicloPreliminarDatos.Baja.cs`).
Se niega si hay un horario aplicado que todavía no empieza (primero hay que deshacerlo) o
si hay registros con fecha posterior a hoy. Con cada clase que vale hoy:

| La clase | Qué le pasa |
|---|---|
| Empezó antes de hoy | Termina ayer |
| Empezó hoy, sin nada registrado | Se quita, con sus horas |
| Empezó hoy, con algo registrado | Queda terminada, sin días propios |

- **Sin días propios** quiere decir que su `vigente_hasta` es la víspera de su
  `vigente_desde`. No se borra (las asistencias dadas son documento oficial) ni se
  desactiva (desactivada desaparece de Mis grupos y de Estadísticas, y con ella lo de
  hoy). En Clases sale «Se dio de baja», «Terminada».
- **Lo de hoy sigue contando una sola vez**: en la clase de la misma terna (docente,
  grupo y materia) que valga hoy o, si no la hay, en la última que empezó hoy o, si
  tampoco, en la de antes. Si ese día se sustituyó varias veces, las asistencias dadas de
  todas pasan a esa (`AsistenciaDatos.OtraSeQuedaElDia` y `DadasQueCuentan`).
- **Nadie carga una falta por la sustitución**: si se sustituye con la clase en curso
  o por venir, sus horas de hoy se cancelan y quien no registró tiene `-` («No cuenta: se
  cambió el horario»), no F, y no cuenta para su porcentaje, porque ya no podía registrar
  ni recibir la asistencia (`AsistenciaDatos.NoTuvoHoraEseDia`). Lo mismo en el primer día
  de la clase nueva, si ese día no tiene hora propia. Una clase que ya se había dado entera
  da faltas como siempre. Las cancelaciones salen en «Cambios de un día», en «Una semana» o
  «Un mes» (en «Un día» no, porque esa clase ya terminó), con el motivo «Se cambió el horario
  a las...». Como esa clase ya no vuelve, ni «Volver a darla» ni «Volver a contar» las ofrecen.
- Las sesiones extra de hoy en adelante que nadie abrió se quitan, porque ya no se
  darían. Las que tienen sesión se quedan, con lo registrado.
- Las clases que empiezan después de hoy no se tocan; la revisión y el botón lo avisan. Si
  una sigue cancelada un día en que se quita la extra que ocupaba sus horas, también lo
  avisan, y se le puede devolver ese día con «Volver a darla».
- Un horario aplicado del que no quedó ninguna clase vuelve a ser plantilla y deja libre su
  nombre, que puede llevar el horario nuevo. Si quedó alguna, se queda como aplicado y dice
  «Aplicado, terminó», con su fecha de fin real.
- Lo que falta asignar pasa a la copia y vuelve a salir al copiarla y aplicarla.
- Un equipo sin conexión sigue con su copia del horario hasta que vuelva la red, y sus
  bitácoras se envían entonces. Con el botón, hasta aplicar otro horario, en un laboratorio
  «Sin códigos» quien entra queda en uso libre.

#### Deshabilitar un laboratorio (sin migración nueva)

En **Laboratorios → «Habilitar o deshabilitar...»** se dejan marcados los
laboratorios donde ya se usa DigLab; los que se desmarcan no corren por ahora, por
ejemplo para instalar DigLab en uno solo sin armar otra plantilla. Un laboratorio
deshabilitado (`laboratorios.activo = 0`, db/08):

- conserva su horario y su historial;
- no recibe registros nuevos: sus equipos rechazan códigos, listas y uso libre y lo
  dicen en pantalla. Sin conexión hacen lo mismo si su copia local ya lo sabía (se
  renueva cada 10 minutos). Quien ya tenía el código aceptado termina su bitácora, y
  lo que un equipo tenía en cola se sube igual;
- sus docentes no ven esas clases en el panel ni en Mi horario, ni un código para
  ellas; en Mis grupos siguen saliendo, con su horario y su lista, sin código;
- las clases que se cruzan con el tiempo en que estuvo deshabilitado no cuentan como
  faltas, tampoco en Estadísticas;
- no avisa de clases sin bitácoras ni de fallas mientras está deshabilitado. Sí salen
  los cambios de horario que haga otra persona y los docentes por enlazar. Si se
  habilita a media clase, esa clase puede avisar «en curso sin nadie», aunque no
  cuente como falta.

Los periodos se guardan en `configuracion` (db/13; clave
`laboratorios_deshabilitados`, `conexion\LaboratoriosDeshabilitados.cs`), así que al
volver a habilitarlo siguen sin contar; sin la 13 se deshabilita igual, pero ese
tiempo cuenta. Al habilitarlo, los equipos con conexión vuelven a aceptar registros
sin tocarlos, en cerca de un minuto como mucho; uno sin conexión, cuando la recupere.
Aplicar un horario no se detiene por un laboratorio deshabilitado: lo avisa en ámbar.

Límite conocido: mientras está deshabilitado, la administración no ve su horario en
Horario semanal, y en Laboratorios se apagan «Ver su horario» y «Códigos...». Sus
horas se consultan en la ficha de cada docente. Al elegirlo, «Quitar» pasa a
«Habilitar», que lo habilita de un clic. «Quitar» también deshabilita uno con
historial y sin clases vigentes.

#### Historial de cambios y alertas de cambios de horario (db/21)

Con la 21, cada cambio queda en `historial_cambios` con quién lo hizo, cuándo
(reloj del servidor), qué decía antes y qué dice ahora, escrito en la **misma
transacción** que el cambio (`conexion\HistorialCambios.cs`): si el cambio
queda, su renglón también. Se anotan:

- franjas: alta, cambio (en su sitio o desde una fecha), fin desde una fecha y
  borrado;
- clases: alta, cambio, fin, activar y desactivar;
- cancelaciones y sesiones extra, al ponerlas y al quitarlas, también las que
  pone o recorta una excepción sobre un tramo y las de «Marcar como no dada»;
- correcciones de bitácoras (Bitácoras → «Corregir registro»), con solo los
  campos que cambiaron;
- laboratorios (alta, nombre, borrar, deshabilitar y habilitar) y su forma de
  entrar, también la de toda la escuela;
- aplicar o deshacer un horario de Próximo ciclo (y, hasta el 2026-11-12, darlo de baja),
  en un solo renglón, no uno por clase.

Nunca se escribe el código de una clase (la persona responsable de laboratorio también
lee el historial): solo que cambió. Los renglones no se cambian ni se borran.

En **Alertas** salen, con el filtro «Cambios de horario», los cambios de
franjas, clases, cancelaciones y sesiones extra que hizo **otra** persona en el
periodo, cada uno con lo que pasó («Clase cancelada», «Franja cambiada»,
«Sesión extra nueva»...) y quién lo hizo; son nuevos los de después de la
última vez que esa cuenta vio las alertas, y entran en la notificación de
Windows como las demás.
«Historial de cambios...» (arriba a la derecha en Alertas) los enseña todos,
con filtros por fechas, tipo, persona y texto, y abajo lo de antes y lo de
ahora del elegido; es de solo lectura y también lo ve la persona responsable de laboratorio. Desde una
alerta «Nadie registró bitácora», «Marcar como no dada...» hace lo mismo que el
menú de Faltas (no para la persona responsable de laboratorio) y «Ver en Faltas» abre Horarios en esa
pestaña, en ese laboratorio y esa semana.

Sin la 21 todo funciona como antes y no se anota nada (`conexion\Esquema.cs`).
Solo dejan rastro los cambios hechos desde un DigLab con esta versión (el del
equipo de la administración). También se anotan los de Docentes: al cambiar el
código de una persona docente, «cambia el código» en cada clase suya vigente (sin
escribir el código), y al dar de baja su cuenta con «Terminar también sus clases», «Clase terminada» o
«Clase desactivada» con «al dar de baja a su docente». No se anota lo que se
carga con `deploy\importar_horarios.ps1`.

#### Tres formas de entrar a la clase (db/11, versión 1.1)

La administración elige en **Códigos de las clases** (en el encabezado de su
panel o en Ajustes) cómo entra cada estudiante (`laboratorios.modo_codigo`):
arriba, la forma de **toda la escuela**; abajo, solo los **laboratorios con
otra forma**. «Agregar laboratorios…» los busca por cualquier parte del nombre
y deja marcar varios, así que con muchos laboratorios iguales la lista queda
corta. La forma general se guarda en `configuracion` (clave
`modo_codigo_general`, db/13) para que no se voltee sola cuando los de otra
forma llegan a ser más, y es la que toman los laboratorios nuevos (sin db/13,
la de la mayoría). Un laboratorio suelto también se cambia en **Laboratorios**
(con buscador): se elige y «Códigos…». Las ventanas abiertas (Laboratorios,
Horarios, el panel) se actualizan solas al guardar.

Para guardar vuelve a escribir su contraseña (`DialogoContrasenaAdmin`): si
dejó la sesión abierta y no está, nadie más puede cambiarlo. Con tres
contraseñas equivocadas en la sesión (aunque cierre la ventana y vuelva a
abrirla) la sesión se cierra sola, sin hacer el cambio, y el login lo avisa;
desde el tercer fallo ya no se pregunta ni se comprueba nada más. El log dice
quién lo confirmó o falló, nunca la contraseña.

| Modo | Qué hace cada estudiante | De dónde sale el código |
|---|---|---|
| **Sin códigos** (`sin_codigo`) | Solo llena su bitácora: si en ese momento hay una sola clase en su laboratorio, se elige sola; si hay dos (una termina y otra empieza), elige la suya; si no hay ninguna, queda como **uso libre** del laboratorio (db/14) | No hay código |
| **Con código único** (`fijo`, el de siempre) | Teclea el código de su docente | Uno por docente, para todas sus materias; se asigna en Docentes |
| **Generación de códigos** (`renovable`) | Teclea 5 cifras que valen solo en esa clase, ese día | Se generan al empezar cada semana, uno distinto por clase y día. Solo la administración los ve (en Horarios, en cada clase); cada docente ve el de su clase al entrar a DigLab durante ella (desde 15 minutos antes) |

- Con **generación de códigos** no se guarda nada por adelantado: cada código
  sale de un secreto de la escuela (`configuracion`, clave `codigos_secreto`,
  db/13), de la clase y de la fecha (HMAC-SHA256,
  `conexion\ModosDeAcceso.cs`, `CodigosGenerados`). Así un cambio de horario a
  media semana no deja códigos viejos y desde el panel docente no se ven los de otros
  días. En un mismo laboratorio y día no se repiten. El secreto lo crea la
  administración al elegir el modo (o al ver el horario de un laboratorio que
  lo usa): los equipos solo pueden leer `configuracion`. Sin él, cada clase se
  abre con un código al azar, como antes.
- La sesión se abre cuando su docente entra a DigLab en su hora (o la
  administración, con clic derecho sobre la clase en «Los laboratorios ahora»),
  con ese mismo código. Si alguien teclea el código de una clase que aún
  nadie abrió (se lo pudo dar la administración), también entra.
- Quien tenga las credenciales de MySQL de los equipos podría leer el secreto,
  igual que ya puede leer los códigos de los docentes y las sesiones de hoy:
  que solo la administración vea los códigos es una regla de las pantallas, no
  de la base.
- **Los docentes no ven los códigos** (casilla de toda la escuela en «Códigos de
  las clases»; `configuracion`, clave `codigos_solo_responsables` = `1`, sin
  migración): con código único o con generación de códigos, el docente no ve
  ningún código (panel, Mi horario, Escuchar el código, Mis bitácoras, también
  en el kiosco) y se lo pide a la persona responsable del laboratorio, que lo ve
  como la administración en «Los laboratorios ahora» y en Horarios. Límite
  conocido: también es solo de las pantallas; `digbit_equipo` sigue pudiendo
  leer los códigos.
- **Sin servidor**, con generación de códigos cada estudiante elige su clase de la
  lista (el equipo no puede comprobar el código) y la bitácora queda marcada
  sin conexión (`docs/fase4-sin-conexion.md`); al subirla, la sesión lleva el
  código generado de ese día.
- **Uso libre** (sin códigos y sin clase, db/14): la bitácora va a una sesión
  con código `LIBRE` y sin docente, materia ni grupo, una por laboratorio,
  día y tramo sin clase (desde que terminó la última clase, o las 00:00, hasta
  15 minutos antes de la siguiente, o el fin del día). Una sesión es del tramo
  en que **empieza** y se guarda hasta el fin del día; el equipo se libera
  hasta el fin del tramo de *ahora*: si se cancela la clase siguiente, el
  tramo crece; si se pone una en medio, el tramo de después tiene su propia
  sesión (los equipos no pueden corregir las horas guardadas: db/07 no les
  deja cambiar `codigos_accesos`). Si dos tramos se juntan, cuenta la sesión
  en que ya se registró: no se registra dos veces. Sin
  servidor funciona con la copia local y la cola, que al subir busca el tramo
  de esa hora según el horario de ahora. En Bitácoras sale como «Uso libre»;
  el panel docente no las enseña y Faltas no las cuenta. Sin la 14, sin clase no se
  registra.
- **Sin la 11** todo es código único. Cada decisión vuelve a leer el modo, así
  que un cambio vale en cuanto se guarda; ante cualquier error al leerlo, se
  usa el código único. Las clases que ya empezaron conservan su código hasta
  que terminan: al pasar del código único a la generación de códigos, las que
  aún no tenían sesión la reciben al guardar, con el código de su docente
  (`LaboratoriosDatos.CambiarModo`).
- Las versiones **anteriores a la 1.1** no saben de los modos y seguirían
  pidiendo el código de cada docente: actualiza los equipos antes de cambiarlo
  (`deploy\diagnostico_equipo.ps1` dice la versión; si se instaló con el asistente,
  Agregar o quitar programas).
- En los laboratorios sin código único, Horarios y "Mi horario" no pintan el
  código de su docente. Con generación de códigos, el Horario de la
  administración pinta en cada clase su código de ese día (solo de las semanas
  que ya empezaron) y la agenda de Cambios de un día lo dice en su columna Código.

La pantalla de Bitácoras de la administración trabaja en **dos niveles**: arriba
las sesiones (fecha, horario, código, materia, grupo, docente, laboratorio y
cuántas personas registraron, cuántas reportaron alguna falla y cuántas sin conexión)
y abajo quienes registraron en la sesión elegida. Los filtros de búsqueda, docente y
fecha actúan sobre las sesiones; «PDF de esta sesión» baja la sesión elegida y
«PDF de las sesiones», todas las filtradas. En «Corregir registro» se edita el
registro seleccionado abajo.

Con `db/26`, **«Resolver fallas...»** marca lo que ya se arregló de la sesión
elegida, cada cosa con su casilla y, si se quiere, lo que se hizo. Lo marcado deja
de salir en naranja y en Alertas, y su PDF lo dice al final, en «Fallas resueltas».
Desmarcarlo lo deja pendiente otra vez. Lo que se reportó no cambia y Estadísticas
lo sigue contando; cada cambio queda en el historial. Solo la administración lo
hace: el docente y la persona responsable solo lo ven.

En las tablas y los PDF, la bitácora que **reporta alguna falla o comentario**
sale en **naranja claro**, y dentro de ella la **falla concreta va en naranja
intenso y negrita**, así que la administración la localiza sin leer toda la
fila. La que no reportó nada queda en blanco con el texto "Sin novedad", y en
los informes sólo se listan las áreas que sí se reportaron, por ejemplo
"Hardware: el teclado no responde". En la lista de sesiones, la columna "Con
fallas" dice cuántas hubo y se resalta cuando no es cero.

La lógica vive en `conexion\HorariosDatos.cs` (`ResolverCodigo`, agenda,
validaciones) y `conexion\Consultas.Sesiones.cs` (consultas por sesión); el
resultado (`VentanaCodigo`) queda en `Datos_User.VentanaActual`.

#### Alertas de la administración (versión 1.1)

En la cabecera del panel, debajo de «Panel de administración», van las pastillas:
**Mi perfil**, **Ajustes**, **Códigos de las clases** y **Alertas** (`BotonCabecera`; la
persona responsable de laboratorio ve **Mi perfil**, **Solo consulta** y **Alertas**). La de
Alertas lleva una campana y, si hay alertas que esa cuenta no ha visto,
se pone en rojo claro con un globo rojo que dice cuántas son; se vuelve a leer
cada dos minutos.
Abre `VentanaAlertas`; los datos salen de `conexion\AlertasDatos.cs`, que no
guarda nada: se calculan con lo que ya hay en la base, así que si después
llegan bitácoras (por ejemplo, de la cola de un equipo sin conexión) la alerta
desaparece sola.

- **Nadie registró bitácora**: una clase o sesión extra que terminó sin
  ninguna bitácora (su docente no vino o no pidió registrar). Es la regla de
  Horarios → Faltas, con sus correcciones, y solo desde que cada laboratorio
  usa DigLab. No avisa si su docente dio asistencias en ella (db/22): la clase
  se dio, y Faltas la pinta en ámbar.
- **En curso y nadie ha registrado**: una clase de hoy que lleva 20 minutos
  sin bitácoras. Con el código único no avisa si el grupo llegó antes y
  registró en la clase anterior de la misma persona docente. Si su docente ya
  dio asistencias, en lugar de «¿Llegó su docente?» dice cuántas y que quizá los
  equipos del laboratorio no tienen internet.
- **Fallas reportadas**: por laboratorio y día, cuántas bitácoras reportaron
  fallas (y de qué), desde qué equipos y qué equipos avisaron sin internet
  (db/10); también en el uso libre.

Se filtran por periodo (hoy, 7 o 30 días), tipo y texto. Desde una alerta se
abre quién registró en esa clase, las bitácoras con fallas de ese laboratorio
ese día o su horario. Hasta dónde las vio cada cuenta de la administración se guarda en
`configuracion` (`alertas_vistas_<número>`, db/13), así que vale en cualquier
equipo; sin la 13, solo durante la sesión. Lo de una persona responsable de laboratorio se
guarda en su equipo (`alertas_vistas_<número>.txt`, en la carpeta de datos),
porque su cuenta no puede escribir en la base.

Con el panel abierto, una alerta que aparece también se avisa con una
notificación de Windows (DigLab pone su icono en el área de notificación
mientras el panel está abierto), **como mucho una por hora** para no saturar
a la administración: la primera sale al momento y lo que aparezca en esa hora se
junta en la siguiente; lo que mientras tanto vio o se resolvió solo ya no se
avisa. Las que ya había al abrir el panel no se avisan (para esas está el
número en rojo). Una clase avisada en curso no se vuelve a avisar al terminar
sin bitácoras; las fallas de un laboratorio, sí, con cada reporte nuevo. Al
pulsar la notificación se abren las alertas. La regla está en
`AvisosDeAlertas` (`conexion\AlertasDatos.cs`).

#### Estadísticas (versión 1.1)

El panel de la administración tiene seis secciones: Bitácoras, Estudiantes,
Docentes, Laboratorios, Horarios y **Estadísticas** (y debajo, «Los
laboratorios ahora»). Estadísticas (`VentanaEstadisticas`) dice, en el periodo
que elijas, cuántas veces entró cada estudiante a sus clases, qué clases se
dieron y qué fallas se reportaron. Solo lee: no escribe nada ni necesita
migración.

- **Filtros**: el periodo («Este ciclo», que empieza el día en que empezó la
  clase vigente más antigua, con el nombre de la plantilla si se aplicó en
  Horarios → Próximo ciclo; un renglón por cada ciclo que ya terminó, como
  «Ciclo abril-septiembre 2026», que se calcula solo con sus clases y con el
  derecho a evaluación; «Este mes»; «Esta semana»; «Últimos 30 días»; o
  «Elegir fechas...», cualquier día desde el 01/01/2000 hasta hoy, con 400
  días como mucho entre los dos y la casilla «Hasta hoy»), el laboratorio, la
  persona docente y el grupo.
- **Cinco cifras**, que al pulsarlas llevan a su pestaña ya ordenada:
  - asistencia de los estudiantes, con cuántos van bajo el 80 %;
  - clases que se dieron: «X de N», donde una clase se dio si alguien registró
    su bitácora o si su docente dio la asistencia a quien no pudo registrar, y N
    son las que ya pasaron y contaban (sin las canceladas); «sin registro» va en
    rojo;
  - bitácoras registradas;
  - fallas reportadas: hardware, software y red, y con db/15 también las de los
    docentes;
  - equipos avisados sin internet (db/10).
- **Seis pestañas**:
  - Estudiantes: entró X de N, asistencia y faltas; al elegir a un estudiante,
    «Sus clases» y «Ver sus bitácoras».
  - Clases: se dieron, sin registro, clases con bitácoras, la asistencia del
    grupo y su lista.
  - Grupos.
  - **Docentes**: solo la asistencia de cada docente, sin datos de estudiantes.
    Columnas: se dieron, sin registro, canceladas, asistencias que dio (db/22)
    y sus reportes (db/15). Al elegir a un docente salen sus clases día por día,
    con «¿Se dio?» («Sí (16 bitácoras)», «Sí (2 asistencias dadas, sin
    bitácoras)», «Sin registro», «Cancelada», «Aún no pasa» o «No cuenta»), y
    doble clic abre la bitácora de esa clase.
  - Laboratorios: desde cuándo registra cada uno con DigLab.
  - Equipos: fallas por equipo, en cuántas sesiones falló, avisos sin internet
    y el último reporte.
- **Las mismas reglas que el resto**:
  - la asistencia es la de Mis grupos: `AsistenciaDatos.LeerEnLote` lee de una
    vez las listas de todas las clases;
  - las clases son las de Faltas (`HorariosDatos.LeerCalendario`);
  - así, las cifras cuadran con Mis grupos, Faltas, Alertas y Bitácoras.

  Los datos los calcula `conexion\EstadisticasDatos.cs`; las cifras y las barras
  de las tablas están en `PiezasEstadisticas.cs`.
- **Guardar en Excel (CSV)...** guarda la pestaña que se ve, con un membrete con
  el periodo y los filtros. En esta versión no hay PDF. En el kiosco no hay CSV y
  las tablas no se copian.
- **Lectura**: lee en segundo plano y guarda lo leído 10 minutos para la misma
  cuenta. **F5** o «Actualizar» vuelve a leer. Sin conexión se sigue viendo lo
  último leído, con su hora.

La persona responsable de laboratorio la ve en solo consulta (ver «Responsable
de laboratorio»). El personal docente no tiene estadísticas: lo suyo está en
Mis grupos.

#### Panel docente (versión 1.1)

Arriba, sus secciones (Mi horario, Mis grupos, Mis bitácoras, Mi perfil), como
en el panel de administración. Debajo, a la izquierda, la ficha de la clase de
la que se habla (la abierta o la próxima de hoy): cómo va y cuánto falta, la materia, el
grupo, el laboratorio y, en curso, una línea de tiempo de su hora de inicio a
la de fin; luego su código en grande (lo que dicta a su grupo), cómo se
deletrea y tres cifras de cómo va: bitácoras registradas, fallas reportadas y
equipos sin internet. El borde late en verde mientras la clase está abierta. A
la derecha, sus clases de ayer, hoy y mañana (si ayer no hubo clases o mañana no las hay, la
columna toma el último o el siguiente día con clases y lo dice con su fecha) como el horario de Horarios: una
fila por hora, los huecos a la vista y, en cada clase, la materia, el grupo, el
laboratorio, el horario y una pastilla con cuántas bitácoras hubo (verde), "Sin
bitácoras" (rojo) o "Cancelada" (gris); la que está en curso late en verde. Si
no caben todas las horas, se desplazan y la vista sigue la hora de ahora. Cuenta
las bitácoras como Faltas: lo que le queda a una clase recortada cuenta con las
de su franja, una clase seguida con código fijo con los que llegaron antes, y en
un laboratorio que aún no usa DigLab no se juzga ("No cuenta"). Al pasar el
ratón por una clase sale todo su detalle y, al pulsarla, quién registró.
"Mi horario" pone su código único en grande arriba a la derecha.

**Mis grupos y «Dar asistencia» (db/22).** Mis grupos enseña la lista de
asistencia de cada grupo como una hoja de Excel, armada con quienes registran su
bitácora. Con db/17, durante la clase la persona docente quita la asistencia a
quien registró y no está en el salón, o se la devuelve.

**«Guardar en Excel...»** la guarda como libro de Excel (.xlsx) con la cara que
tiene en pantalla (antes era un CSV sin formato): el título y los datos de la lista
arriba, el encabezado verde con las fechas en vertical, cada casilla con sus colores,
el % como porcentaje, el derecho a evaluación en verde o rojo, la fila Asistieron y la
simbología; congelada bajo los encabezados y, al imprimir, horizontal, a una hoja de
ancho y con el encabezado en cada página. Las asistencias dadas van en otra pestaña.
Lo escribe `Infraestructura\LibroExcel.cs`, sin bibliotecas de más. En el diálogo
también se puede elegir el CSV de antes.

Con la 22, **mientras dura la clase** (con el reloj del servidor), también **da
la asistencia** a quien está en el salón y no pudo registrar su bitácora:

- **Cómo se da**: en su casilla P de hoy, «Darle la asistencia...». Si aún no
  está en la lista, con «Dar asistencia a alguien que no está en la lista...»,
  por matrícula exacta (`DialogoDarAsistencia`).
- **Qué se elige**: el motivo (su equipo no tenía internet, no funcionaba, no
  había uno libre u otro, con detalle), el equipo si se sabe y un detalle.
- **La casilla**: queda en una A verde. Durante la clase se puede quitar
  («Quitarle la asistencia que le diste: no está en el salón»); anularla no la
  borra.
- **Cómo cuenta**: como asistencia en todas partes y nunca como bitácora. Si su
  bitácora llega después, cuenta una vez.
- **CSV**: la pone como «A» y añade al final «Asistencias que dio el docente sin
  bitácora».
- **En el horario de tres días**: una clase sin bitácoras en la que dio
  asistencias sale con la pastilla «Sin bitácoras» en gris, no en rojo, y, si
  cabe, «Diste 2 asistencias sin bitácora».
- **En el kiosco** (el docente en un equipo del laboratorio): pide su contraseña
  antes de cualquier cambio de la lista y no la vuelve a pedir en 2 minutos;
  tres equivocadas cierran la sesión.

La administración lo ve en la Bitácora de la clase y su PDF, en Bitácoras, en
el historial del estudiante, en Faltas (en ámbar: la clase se dio), en Alertas
y en Estadísticas. Decide el servidor, con su reloj (`AsistenciaDatos`,
`conexion\SesionDeHoy.cs` y `conexion\AsistenciasDadas.cs`). Sin la 22 no se
ofrece y todo se ve como antes.

#### Responsable de laboratorio: el panel en solo consulta (db/18, versión 1.1)

La persona responsable de laboratorio, y quien le ayuda del servicio social, entra al
**mismo panel que la administración, en solo consulta**: «Los laboratorios
ahora», las alertas (con su campana y la notificación de Windows), Bitácoras con
sus PDF, Estudiantes, Docentes, Laboratorios, Horarios con sus pestañas (la
quinta, «Horarios guardados», es la lista de plantillas y horarios aplicados) y
Estadísticas, pero sin poder cambiar nada. Tampoco ve lo que es solo de la
administración: ni Ajustes (con la clave de la API), ni «Códigos de las clases»,
ni ningún código de clase (los generados cada semana, el de cada docente ni el
de cada sesión). En lugar de esas dos pastillas lleva **Solo consulta**, que
explica qué incluye, y la ventana se llama «DigLab - Responsable de laboratorio» (sus
secciones llevan «(solo consulta)» en el título).

En **Estadísticas (solo consulta)** ve lo mismo que la administración, pero
solo guarda en Excel (CSV) las pestañas **Laboratorios, Grupos y Equipos**, que
no llevan datos personales. En Estudiantes, Clases y Docentes el botón queda
apagado y explica que esos datos solo los guarda en un archivo la
administración de DigLab (`EstadisticasDatos.SeGuardaEnCsv`). «Ver sus
bitácoras» se abre con «Exportar sus datos...» apagado, como en Estudiantes.

Lo decide un solo sitio, `Permisos` (`DigLab\Infraestructura\Permisos.cs`), por
el tipo de la cuenta: las pantallas no crean (u ocultan) lo que escribe, cada
acción que escribe empieza con `Permisos.Bloquear` y cada método de datos que
escribe, con `Permisos.Exigir`. Y la base tampoco lo deja: al entrar una
persona responsable de laboratorio, en **cualquier equipo**, DigLab deja la cuenta de MySQL del equipo y
hace toda su sesión con **`digbit_consulta`**, que solo puede leer. Su
contraseña está en `configuracion`, así que no se configura nada en ningún
equipo. Si esa cuenta falta o puede escribir algo, no se entra: nunca
se usa la del equipo en su lugar. Lo que ya vio de las alertas se guarda en su
equipo (ver «Alertas de la administración»): en otro equipo empieza con todas como
nuevas.

**Cómo se da de alta.** La administración, en **Docentes → «Responsables de
laboratorio...»**: «Registrar responsable» (número de empleado, nombre, correo y
contraseña), editar, restablecer la contraseña, dar de baja y reactivar. Para
el servicio social, una cuenta por persona: `SS` más su matrícula (por ejemplo
`SS2023123456`) y otro correo que el de su cuenta de estudiante, o ninguno, porque
el número y el correo no se repiten; al terminar su servicio se da de baja.
Quien ya tiene cuenta docente necesita otra como responsable de laboratorio,
también con otro número y otro correo: una cuenta no se convierte en otra. En
«Mi perfil» cambia su correo y su contraseña; si la olvida, se la restablece la
administración, o la recupera con «¿Olvidaste tu contraseña?» cuando la API de
recuperación esté en el hosting.

**Lo que necesita el servidor: `db\18_encargados.sql`, aplicada con root.**
Pone el tipo 4 en `tipo_usuarios` (en la base del TESCHI; sin él, dar de alta a
una persona responsable falla por la clave foránea), crea `digbit_consulta` solo con
`SELECT` y guarda su contraseña, al azar, en `configuracion`. Con otro usuario,
como `teschi_otru`, solo hace lo primero y lo avisa: sin `digbit_consulta` las
personas responsables no pueden entrar, y al intentarlo DigLab les dice que falta la cuenta
de solo consulta. El orden: la 18, actualizar DigLab en los equipos donde
entrarán, y solo entonces darlos de alta. Un DigLab anterior no conoce el tipo
4: avisa «Tipo de usuario no manejado» y se queda sin ventana (en el kiosco,
hay que reiniciar el equipo), aunque no le da ningún permiso. Desde esta
versión, un tipo de cuenta que no conoce deja el login a la vista con un aviso.

### 6. Equipo liberado y reinicio al terminar la clase

Cuando alguien guarda su bitácora, DigLab **libera el equipo** hasta la hora
de salida del código:

```
Bitácora guardada
   │
   ├─ escribe %ProgramData%\DigLab\sesion_activa.txt   (traspaso para el vigilante)
   ├─ kiosco: lanza explorer.exe y esconde sus ventanas → quien registró usa Windows
   ├─ icono en la bandeja con la cuenta atrás y "Cerrar mi sesión ahora"
   ├─ fin − 5 min y fin − 1 min: aviso "guarda tu trabajo: al terminar, el equipo se reinicia"
   └─ fin: REINICIO del equipo (kiosco; si no se puede, cierre de sesión) / vuelta al login (escritorio)
```

La hora de salida es el **fin de la clase** con cuyo código se registró (en el uso
libre, el inicio de la siguiente clase o el final del día): no es un reinicio a una
hora fija, sino uno por clase (pedido del 2026-11-07). Con Deep Freeze el equipo
vuelve limpio para la siguiente. Los programas se cierran sin preguntar, por eso los
dos avisos. Quien se va antes con «Cerrar mi sesión ahora» solo cierra su sesión.

Si el kiosco usa la cuenta que el equipo ya tenía (sección 7), justo antes de
lanzar `explorer.exe` DigLab quita del registro de esa cuenta tres límites:
Administrador de tareas, `Win+R` y Panel de control.

**Excepción: quien trajo su propia computadora.** Si marcó *equipo
propio*, el equipo del laboratorio **no se libera**: esa persona no lo va a usar,
y dejarlo con el escritorio abierto y sin nadie delante hasta el final de la
clase era un hueco. En su lugar aparece `RegistroExitoso.cs`, que confirma el
registro y ofrece las dos únicas salidas sensatas:

| Botón | Qué hace |
|---|---|
| **Apagar el equipo** | `SesionEquipo.ApagarEquipo()`: habilita `SeShutdownPrivilege` en el token del proceso y llama a `ExitWindowsEx(EWX_POWEROFF)`. Fuera del kiosco no apaga nada, y `DIGLAB_SIMULAR_APAGADO=1` lo desactiva también dentro. |
| **Volver al inicio** | `AppContexto.CerrarSesion()`: login limpio para la siguiente persona. |

Si nadie pulsa nada en 30 segundos vuelve sola al login. La pantalla no se
puede cerrar con Alt+F4 ni con "Finalizar tarea".

El cierre lo **garantiza** `DigLab.Vigilante`, un servicio de Windows que corre
como SYSTEM (con una cuenta estándar, quien usa el equipo no puede matarlo; con una
administradora, sí: ver sección 7): lee el traspaso cada 10 segundos y a
la hora de salida reinicia el equipo con `shutdown.exe /r /f /t 0` (si Windows no
lo acepta, cierra esa sesión con `WTSLogoffSession`, como antes).
Si DigLab ya no está para avisar, el servicio manda el aviso él mismo. DigLab
hace lo mismo desde dentro de la sesión como respaldo (`ExitWindowsEx` con
`EWX_REBOOT | EWX_FORCE`; `DIGLAB_SIMULAR_REINICIO=1`, o las de simular el cierre o
el apagado, lo desactivan).

Reglas del traspaso, iguales en los dos procesos (`Infraestructura\SesionActiva.cs`):

- Se ignora y se borra si venció hace más de 10 minutos o si es de **otro
  arranque** del equipo (los números de sesión de Windows se repiten entre
  reinicios).
- Se ignora si es de **otra sesión de Windows** distinta de la de consola.
- Una vez que el vigilante recuerda una sesión, la hora de salida **sólo puede
  acortarse**: reescribir el archivo con una hora posterior, con otro código o
  borrarlo no sirve. El vigilante olvida la sesión cuando Windows le notifica
  un cierre o inicio de sesión real, que es como llega la siguiente persona.
- Si Winlogon relanza DigLab (`AutoRestartShell`) con un traspaso vigente de este mismo
  arranque y sesión, DigLab **reanuda** el estado "equipo liberado" sin pedir
  login y sin necesitar la base de datos.

Instalación del servicio, una vez por equipo, como administrador:

```powershell
.\build.cmd -Configuration Release -NoRun
.\deploy\instalar_vigilante.ps1               # instala desde DigLab.Vigilante\bin\Release y lo arranca
.\deploy\instalar_vigilante.ps1 -Desinstalar
```

Su registro queda en `%ProgramData%\DigLab\logs\vigilante-AAAAMMDD.log`.

**Probar sin base de datos y sin cerrar la sesión de verdad:**

| Qué | Cómo |
|---|---|
| Vigilante en primer plano, sólo registra lo que haría | `DigLab.Vigilante.exe --consola --simular` (`--segundos N` para que termine solo) |
| DigLab en kiosco sin cerrar la sesión de Windows | variables `DIGLAB_MODO_KIOSCO=1` y `DIGLAB_SIMULAR_LOGOFF=1` |

Los guiones `deploy\probar_vigilante.ps1` (seis escenarios: vigente, otro
arranque, alargar, otro código, archivo borrado, adelantar) y
`deploy\probar_reanudar.ps1` (DigLab reanuda, avisa, "cierra" y vuelve al
login) escriben el traspaso a mano y muestran lo que registró cada proceso. No
necesitan base de datos ni permisos de administrador.

`deploy\probar_vigilante_limites.ps1` es la contraparte adversaria: le da
entradas **torcidas** —la hora de salida ya pasada, una franja invertida, una
clase que cruza la medianoche, el archivo cortado a la mitad, vacío, binario o
en sólo lectura, un traspaso de otra sesión de Windows— y comprueba sola que en
cada caso hace algo sensato, que no cierra dos veces, y sobre todo que **no se
muere**. Si una entrada rara mata al servicio, Windows lo reinicia unas cuantas
veces y se rinde, y a partir de ahí el equipo se queda sin nadie que cierre
sesiones, en silencio. Doce casos, unos cuatro minutos.

**Si se va la luz a media clase.** Alguien registró su bitácora, el equipo se
apagó de golpe y vuelve a arrancar dentro de la misma hora. El traspaso guardado
ya no sirve —el arranque del sistema es otro, así que `SesionActiva.Evaluar` lo
marca `DeOtroArranque`— y hasta hace poco, al volver a teclear su código, DigLab
le contestaba *"Registro duplicado"* y se quedaba sin equipo el resto de la clase,
sin ninguna salida. Ahora le pregunta:

> Ya registraste tu bitácora en esta clase.
> ¿Quieres volver a usar el equipo hasta las 10:00?

Si dice que sí, se le libera el equipo otra vez con la misma hora de salida, sin
registrar una segunda bitácora. La comparación se hace contra el **reloj del
servidor**, no el del equipo: con un reloj mal puesto no queremos ni dejar fuera
a quien está en clase ni dar acceso a quien ya no.

**Apagar el equipo.** En el kiosco no hay menú Inicio, así que el login enseña
abajo a la derecha un botón **Apagar el equipo**, con confirmación y con "No"
por defecto. Sin él, la única forma sería `Ctrl+Alt+Supr`, que mucha gente no
conoce, o el botón físico.

### 6b. Sin conexión con el servidor

Cada equipo guarda una **copia local del horario de su laboratorio** (clases,
franjas que aún se dan con su vigencia propia de la 20, excepciones de dos
semanas y la lista de estudiantes, nunca contraseñas)
en `%LOCALAPPDATA%\DigLab\horario_local.xml`, refrescada al arrancar y cada
10 minutos. Si el servidor no responde y la copia tiene menos de 30 días,
DigLab sigue atendiendo estudiantes con ella (diseño en `docs/fase4-sin-conexion.md`):

- El login muestra una franja naranja "SIN CONEXIÓN". Solo entran estudiantes, y
  solo con su matrícula: la contraseña no se puede comprobar.
- Con el código único, el código se resuelve con la copia y el reloj del
  equipo, con las mismas reglas. Sin códigos o con la generación de códigos, cada
  estudiante elige su clase de la lista (el equipo no puede comprobar un código
  generado: el secreto no se guarda en disco). Solo se conoce el horario de
  este laboratorio.
- La bitácora se guarda en `bitacoras_pendientes.xml` y la sube sola un hilo
  en segundo plano (`EnviadorBitacoras`): reintenta a los 15 s, 30 s, 1, 2 y
  5 minutos, y en cuanto Windows avisa de que volvió la red. También si la red
  se cae justo al guardar con el servidor: en pantalla sale "se enviará sola" en
  vez de un error. En la base queda con `registrado_sin_conexion = 1` (salvo
  que hubiera entrado con contraseña) y `recibido_en` dice cuándo llegó; en
  tablas y PDF el nombre lleva `[sin conexion]`.
- Apagar desde DigLab con bitácoras sin enviar intenta enviarlas 10 s y, si no
  puede, avisa: con Deep Freeze y sin carpeta de datos se perderían.
- La liberación del equipo y el Vigilante funcionan igual: nunca dependieron
  de la base.
- **Aviso al estudiante**: si viene a una clase, se le pide que avise a su
  docente («Si vienes a una clase, avísale ahora a tu docente para que te dé la
  asistencia.»), que con db/22 se la da en Mis grupos mientras dura la clase.
  - Dónde sale: en la pantalla «Sin conexión» y en el inicio de sesión del
    kiosco, en la pantalla del estudiante sin una copia del horario que sirva, y
    cuando su bitácora se quedó guardada en el equipo, que su docente aún no ve.
  - En el kiosco sale también en las demás pantallas de arranque que dejan el
    equipo inservible: sin `connections.config` (o dañado), cuenta de la base
    rechazada, sin laboratorio asignado o no dado de alta, y cuando no se pudo
    comprobar el laboratorio del equipo.
  - En ese último caso, «Listo» espera a que pulse «Entendido» (o 15 s) antes
    de liberar el equipo. Con equipo propio sale en `RegistroExitoso`, y el globo
    al abrir el escritorio lo recuerda.
  - En el uso libre no sale.

Sin copia local (equipo recién instalado) o con una de otro laboratorio, la
app se comporta como antes: pantalla "Sin conexión" con reintento.

### 7. Equipos del laboratorio: DigLab como shell de Windows

La forma de instalar es **`DigLab-Setup.exe`**: un asistente normal de Windows, sin
PowerShell en el equipo, que también actualiza y desinstala (esas dos, sin confirmar
todavía: ver «Estado del proyecto»). Sirve para los tres tipos de equipo
(administración, docente y laboratorio) y siempre instala en `C:\DigLab`. No prepara
el servidor de base de datos. La guía completa está en `INSTALAR.md`, «El instalador:
`DigLab-Setup.exe`».

Se genera con `crear_setup.ps1` (Inno Setup 7 e `instalador\DigLab.iss`), que lo deja
fuera del repositorio: el de `instalador\DigLab-Setup.exe` es una copia del armado con
este código. Lo que el asistente no puede hacer solo lo hace `DigLab.Configurador.exe`,
en C#. Las contraseñas le llegan en variables de entorno, y la de la base no va dentro
del instalador: se escribe al instalar y se comprueba contra la base; si no hay
conexión, pregunta si instalar igual. El usuario de la base precargado es uno solo, y
el asistente no lo cambia según el tipo de equipo: en su página pon el que toca
(`digbit_admin` solo en el de la administración; `digbit_equipo` en los demás; sección
10).

En un equipo de laboratorio, el asistente pregunta con qué cuenta de Windows entra
el kiosco:

| | La que ya tiene el equipo (por omisión) | Una cuenta nueva |
|---|---|---|
| Cuál es | La cuenta con la que se abre el instalador; sale en su página | La que se escriba; por omisión, `laboratorio` |
| La cuenta | No se crea; conserva su contraseña y sus grupos, aunque sea administradora. Cambian su shell, sus directivas y su inicio automático | Se crea sin permisos de administrador. Si el nombre ya existe y no es administradora, se usa esa y se le cambia la contraseña sin preguntar |
| Contraseña | La actual; Windows la comprueba antes de instalar (vacía si no tiene) | La que se invente |
| Tras la bitácora | Su escritorio, sin los límites de Administrador de tareas, `Win+R` y Panel de control | El escritorio con todos los límites |
| Al desinstalar | Se le quitan el shell, las directivas, el permiso y el inicio automático, aunque ya entrara sola antes; conserva su contraseña y nunca se ofrece borrarla | Se le quita lo mismo, y pregunta si borrarla |

**Para llegar a un escritorio sin bitácora**, en las dos: una memoria de emergencia.
Desde `db/28` ya no se autorizan al instalar: se registran en DigLab
(**Laboratorios → Memorias de emergencia...**) y cualquiera abre cualquier equipo; el
asistente copia la lista de la base al equipo y el vigilante la renueva cada 10
minutos. En el kiosco, **Acceso de emergencia** abre el escritorio de esa cuenta sin
bitácora hasta reiniciar (ver `INSTALAR.md`). O `Ctrl+Alt+Supr` → **Cambiar de
usuario**, a otra cuenta y con su contraseña. Sin memorias registradas y sin otra
cuenta no hay camino: el asistente avisa antes de instalar.

Como el asistente pide permisos de administrador, lo normal es que la cuenta que ya
tiene el equipo sea administradora. Entonces, quien registre su bitácora puede instalar
programas o desactivar el kiosco, y todos comparten sus archivos. Se aceptó el
2026-11-04 porque los equipos del laboratorio se reinstalan cada semestre. También
puede leer `connections.config`, con la contraseña de la base: por eso los
laboratorios deben entrar con `digbit_equipo` (sección 10). Si la cuenta con la que se
instala es de Microsoft, esta opción la rechaza.

Cómo funciona el kiosco en la cuenta que ya tiene el equipo:

- **Los límites:** con `LiberarDirectivas=true` en `DigLab.exe.config`,
  `Infraestructura\DirectivasKiosco.cs` pone `DisableTaskMgr`, `NoRun` y
  `NoControlPanel` al arrancar y los quita justo antes de lanzar el escritorio. Los
  demás se quedan (bloquear el equipo, cambiar la contraseña, cerrar sesión, protector
  de pantalla y atajos de accesibilidad): bloquear o cambiar la contraseña impedirían
  que vuelva a entrar sola.
- **El permiso:** Windows no deja a una cuenta escribir sus propias directivas sin
  elevar, aunque sea administradora. El instalador le permite escribir valores solo
  en `Policies\System` y `Policies\Explorer`, y lo quita al desinstalar.
- **La marca:** `CuentaOriginal=1` en `HKLM\SOFTWARE\DigLab` hace que el desinstalador
  no ofrezca borrarla.
- **Actualizar o desinstalar desde la sesión de esa cuenta** (abierta con la memoria
  de emergencia): DigLab es su shell y Windows lo reabriría al cerrarlo, con su archivo
  en uso. Por eso `AutoRestartShell` se apaga antes de cerrarlo y se devuelve al
  terminar. Si una actualización no termina, lo vuelve a encender el vigilante al
  arrancar.

El resto de esta sección describe el guion anterior, `deploy\configurar_equipo.ps1`,
que sigue disponible. Las tres protecciones de más abajo (carpeta en sólo lectura,
clave `Shell` protegida y `ForceAutoLogon`) también las aplica el instalador, aunque
con una cuenta administradora no frenan a quien ya tiene su escritorio. El instalador
excluye de Defender solo los `.exe`, no la carpeta.

En cada equipo, como administrador y con DigLab compilado en Release:

```powershell
.\build.cmd -Configuration Release -NoRun
.\deploy\configurar_equipo.ps1 -Cuenta laboratorio -Contrasena 'LaContrasena' `
    -Laboratorio 'Laboratorio de Computo 1' `
    -CadenaConexion 'Database=teschi_otru;Server=SERVIDOR;User Id=diglab;Password=xxx'
```

El guion copia DigLab y el vigilante a `C:\DigLab`, activa el modo kiosco y
escribe el laboratorio del equipo en la copia desplegada, crea la cuenta local
estándar `laboratorio` si no existe y la deja así:

- DigLab es su **shell**: al entrar no hay escritorio ni barra de tareas, sólo
  el login de DigLab. Las demás cuentas, también las de administrador, no se tocan:
  siguen entrando con explorer.exe. El guion no admite una cuenta administradora
  para el kiosco.
- Sin Administrador de tareas, sin `Win+R`, sin Panel de control ni Configuración,
  sin bloquear el equipo, sin cambiar contraseña, sin cerrar sesión desde Inicio ni
  desde `Ctrl+Alt+Supr`, sin protector de pantalla con contraseña.
- Inicio de sesión automático con esa cuenta y sin pedir contraseña al
  despertar.
- Servicio `DigLab.Vigilante` instalado y arrancado.

Después de reiniciar, el ciclo es: encendido → DigLab → login → código →
bitácora → escritorio → al terminar la clase el equipo se reinicia → DigLab otra
vez. Si DigLab termina por lo que sea, Winlogon lo relanza (`AutoRestartShell`)
y DigLab reanuda el estado en que estaba.

Para entrar como administrador en un equipo ya configurado: `Ctrl+Alt+Supr` →
**Cambiar de usuario**, o mantener `Mayús` pulsada durante el arranque para
saltar el inicio automático. `-Revertir` deshace shell, directivas, inicio
automático y servicio; `-SoloMostrar` enseña lo que haría sin cambiar nada y no
necesita administrador.

**Los otros dos equipos.** El mismo guion prepara el equipo de la administración y
el de cada docente: es la misma aplicación, sólo que sin bloquear la pantalla.

```powershell
.\deploy\configurar_equipo.ps1 -Modo adm `
    -CadenaConexion "Database=teschi_otru;Server=SERVIDOR;Port=3306;User Id=digbit_admin;Password=xxx"

.\deploy\configurar_equipo.ps1 -Modo maestro `
    -CadenaConexion "Database=teschi_otru;Server=SERVIDOR;Port=3306;User Id=digbit_equipo;Password=xxx"
```

Copia los archivos, escribe la conexión, excluye la carpeta en Defender y la deja
en sólo lectura. Nada más: sin cuenta de laboratorio, sin shell, sin directivas,
sin inicio de sesión automático y sin vigilante. `-Modo maquina` es el valor por
defecto y es todo lo que se describe arriba.

El orden importa: **el equipo de la administración va primero**, porque los laboratorios se dan
de alta desde ahí y el instalador de las máquinas comprueba contra la base que el
nombre que le pasas existe de verdad.

Tres cosas que el guion endurece porque Windows no las deja bien por defecto, y
que el diagnóstico comprueba una a una:

- **`C:\DigLab` en sólo lectura para la cuenta del laboratorio.** Una carpeta
  creada en la raíz de `C:` hereda `Authenticated Users:(M)`, así que por
  defecto quien use el equipo puede sustituir `DigLab.exe` por un `cmd.exe` y el siguiente
  inicio automático le da un símbolo del sistema. Y encima esa carpeta está
  excluida del antivirus.
- **La clave `Shell` protegida dentro de la propia colmena de la cuenta del laboratorio.** Vive en
  `HKCU`, que hereda control total, así que podía ponerla en `explorer.exe` y
  entrar al escritorio sin pasar por DigLab — sin reiniciar siquiera, porque el
  relevo entre estudiantes es un cierre de sesión. Se le aplica el mismo patrón que
  Windows ya usa para las directivas: herencia rota y sólo lectura.
- **`ForceAutoLogon=1`.** `AutoAdminLogon` sólo actúa al **arrancar**. Cuando se
  cierra la sesión sin reiniciar (quien se va antes, o si el reinicio al terminar la
  clase no se pudo) no hay arranque, así que sin esto el equipo se quedaría pidiendo
  una contraseña que la siguiente persona no tiene. Rompe el escritorio remoto, cosa que en estos equipos no se usa.

Las directivas de la cuenta (`DisableTaskMgr`, `NoRun`, …) **no** hace
falta protegerlas: Windows ya deja `HKCU\...\Policies` con la herencia rota y en
sólo lectura para el usuario. La excepción es el permiso del kiosco en la cuenta que
el equipo ya tenía, descrito arriba.

La contraseña del inicio automático queda en el registro en texto claro
(`Winlogon\DefaultPassword`), como en cualquier inicio automático de Windows:
usa una contraseña exclusiva de esa cuenta. Con la cuenta que el equipo ya tenía, la
que queda ahí es la suya de siempre: no la uses en otro sitio.

### 8. Contraseñas

Se guardan con **PBKDF2-SHA256, sal aleatoria por usuario y 100 000
iteraciones** (`conexion\Contrasenas.cs`). En la columna `password` queda un
texto con todo lo necesario para comprobarlo después:

```
pbkdf2-sha256$100000$<sal en base64>$<hash en base64>
```

Antes se guardaba el MD5 a secas, que es mala idea por dos motivos. MD5 está
hecho para ser rapidísimo, así que quien copie la tabla prueba miles de
millones de combinaciones por segundo; medido en esta máquina, el método nuevo
tarda unos 260 ms por intento, unas 260 millones de veces más. Y sin sal la
misma contraseña da siempre el mismo hash, de modo que hay tablas públicas con
los pares ya resueltos y, dentro de la propia tabla, dos personas con la misma
contraseña se delatan solas.

La migración es **perezosa** porque MD5 no se deshace: no hay forma de
recuperar las contraseñas guardadas para volver a procesarlas. Así que se
aprovecha el único instante en que la aplicación las tiene en claro, un inicio
de sesión correcto, para recalcularlas y reemplazar lo guardado. Nadie tiene
que cambiar nada ni se entera. Las iteraciones van dentro del propio texto, así
que subirlas más adelante tampoco invalida lo ya guardado.

Para saber cuánto falta, la consulta al final de
`db\06_migracion_contrasenas.sql` cuenta cuántos siguen en MD5. Cuando llegue a
cero y lleve así un tiempo razonable (piensa en quien no entra en
todo el semestre), se puede borrar el camino de MD5 de `Contrasenas.cs`.
Mientras quede alguno, borrarlo lo dejaría fuera.

#### Recuperar la contraseña por correo (db/13, versión 1.1)

**«¿Olvidaste tu contraseña?»**, en el inicio de sesión (también en el
kiosco), pide la matrícula o el número de empleado y le manda **un código de 6
cifras a su correo**; con él, en el mismo diálogo, se pone una contraseña nueva.
Sirve para las cuentas activas de estudiantes, docentes y responsables de
laboratorio; la de la administración no se recupera por ahí.

DigLab no manda el correo: lo hace una **API pequeña en PHP** (carpeta `api/`)
en el hosting de la escuela, que es la única que tiene la cuenta
`digbit.teschi@gmail.com` y su contraseña de aplicación. Así esa contraseña no
está en ningún equipo, donde cualquiera que lo use podría leerla. La API guarda el código
como HMAC (no el código), lo deja valer 15 minutos, una vez y con 5 intentos, y
limita los códigos por cuenta y por IP. La contraseña nueva queda en el mismo
PBKDF2 de arriba.

La dirección de la API y su clave las pone la administración **una vez**, en
**Ajustes → Recuperar contraseña por correo → Configurar...** (arriba en su
panel), con un botón **Probar**
que lo comprueba todo sin mandar correos. Se guardan en la base (tabla
`configuracion`), así que valen al momento en todos los equipos con la 1.1,
también en los congelados (los de una versión anterior hay que actualizarlos
una vez; en ellos el enlace abre la ventana vieja, que no hace nada). Tiene
que ser una dirección `https://` del hosting: Ajustes no deja guardar la de la
propia PC (`127.0.0.1`, `localhost`), que en los demás equipos no existe. Un
equipo puede usar otra con `UrlRecuperacion` y
`ClaveRecuperacion` en `DigLab.exe.config`, pero lo normal es dejarlas vacías.
Sin dirección, el enlace dice que pidan a la administración de DigLab una
contraseña nueva.

La administración también puede, en **Estudiantes** o **Docentes** (**Editar**
y **Restablecer contraseña**), cambiar el correo de una cuenta y ponerle una
contraseña nueva; la de una persona responsable de laboratorio, en **Docentes →
«Responsables de laboratorio...»**. Quien entra con el SIIA no tiene aquí
contraseña que cambiar, y a un estudiante con el SIIA tampoco se le pide correo.

El **aviso de privacidad** se pone en **Ajustes → Aviso de privacidad →
Configurar...** (el aviso simplificado y la dirección del integral; db/13, en
`configuracion`). Si tiene texto, «Crear cuenta» lo enseña y pide marcar «Leí el
aviso de privacidad», y «¿Olvidaste tu contraseña?» lo enlaza; en el kiosco no
se abre el navegador. Con `aviso_privacidad_url` en el `config.php` de la API,
el correo del código lleva al pie el mismo enlace.

Cómo se instala la API en el hosting, cómo se saca la contraseña de aplicación
de Gmail y qué hacer si algo falla: `api/LEEME.md`.

#### Entrar con el SIIA del TESCHI (db/24, versión 1.1)

En **Ajustes → Inicio de sesión con el SIIA → Configurar...** hay dos
interruptores, apagados por omisión: **los docentes** y **los estudiantes**
entran con su número de empleado (o su matrícula), su contraseña y su **NIP**
del SIIA, en dos pasos como el propio SIIA: primero el número y la contraseña
(`login.ashx`, sin NIP) y luego, en la misma ventana, el NIP (`nip.ashx`, acción
`verificar`, el paso que solo comprueba). DigLab nunca escribe en el SIIA: no
crea ni cambia el NIP ni la contraseña; a quien aún no tiene NIP, o la olvidó,
lo manda a la página del SIIA: «¿Olvidaste tu contraseña?» (o tu NIP) abre
directo la página de recuperación del SIIA en el navegador; en el kiosco, que
nunca abre el navegador, enseña la dirección y su código QR para el celular. Las direcciones (`login.ashx`, la página para
recuperar la contraseña y la de inicio del SIIA) se cambian ahí mismo, con
**Comprobar** (un GET sin datos, que debe dar 405). La administración y las
personas responsables de laboratorio siguen con su contraseña de DigLab.

- **Docentes:** la primera vez que un docente entra con el SIIA, DigLab le pide
  que se acerque con la administración y le diga «Soy el número de empleado
  ####, enlázame». Queda en **Alertas → Docentes por enlazar** (con
  notificación de Windows, como mucho una por hora) y en **Docentes**, con la
  columna «Enlace con el SIIA» (Enlazado / Pendiente / Sin enlazar) y una franja
  con **Enlazar...**: se busca su número, se elige quién es en la lista (la
  coincidencia por nombre sale hasta arriba) y **Enlazar**. Desde ese momento
  entra con ese número y conserva sus clases; si estaba esperando, entra solo en
  unos segundos. **Descartar solicitud** y **Desenlazar del SIIA** deshacen lo
  que no corresponde. DigLab no crea ni enlaza docentes por su cuenta.
- **En el paso del NIP** se ve el nombre que dio el SIIA («Hola, Lucía Pérez
  Ruiz»), ya con la contraseña comprobada, y, si hay aviso de privacidad en
  Ajustes, el enlace para leerlo.
- **Estudiantes:** entran con su cuenta de esa matrícula o, si no la hay, se
  crea sola con su nombre del SIIA y su carrera (si el área del SIIA casa con
  una sola carrera de DigLab, sin mayúsculas ni acentos y perdonando una o dos
  letras: «Ingeneria en Sistemas Computacionales» casa con «INGENIERÍA EN
  SISTEMAS COMPUTACIONALES»); el inicio de sesión ya no ofrece «Crear cuenta»,
  tampoco en el kiosco. Una cuenta que ya existía y no tiene carrera la toma igual
  la siguiente vez que entra con el SIIA (queda en el historial); la que ya tiene
  una no se toca, y si no casa se reintenta al volver a entrar. En la lista de
  **Estudiantes** y en su **Editar** solo
  queda lo que da ese inicio de sesión (matrícula, nombre y carrera): semestre y
  correo ya no se ven ni se buscan, aunque una cuenta de antes los conserva en la
  base y salen al exportar sus datos. Quien reportó fallas va en naranja, con y
  sin el SIIA, y «Con falla» dice cuántas de sus bitácoras las reportan.
- **Volver a cargar el horario** con `deploy\importar_horarios.ps1` después de
  enlazar: el docente que en el CSV sigue con su número provisional se encuentra
  por ese número de antes (lo guarda el enlace) y no se duplica.

> **Para la administración:** antes de encender el inicio de sesión con el SIIA
> para los docentes, conviene tener a mano la lista de docentes: cada uno, la
> primera vez que entra con el SIIA, espera a que lo enlaces con su cuenta (la
> que tiene sus clases), y mientras no lo enlaces no entra. Los que se cargaron
> con el horario real tienen un número provisional (letras): al enlazarlos pasa
> a ser su número de empleado. Enciéndelo cuando todos los equipos tengan esta
> versión de DigLab, y aplica antes `db/24_docentes_siia.sql`.

Si el SIIA no responde (sin internet, más de 9 segundos, una falla o una
respuesta que no se entiende), solo entra quien ya entró antes con el SIIA en
DigLab (los docentes, ya enlazados), con la contraseña y el NIP de la última
vez: se comprueban contra su **huella** (PBKDF2 de los dos juntos). Un
estudiante sin huella ve «Pide a tu profesor que te dé asistencia». Si el SIIA
rechaza, dice que la cuenta está inactiva o bloquea por intentos, no se entra.
La contraseña, el NIP y los tokens del SIIA nunca se guardan ni van al registro.

### 9. Deep Freeze en los equipos del laboratorio

Los equipos del TESCHI tienen **Deep Freeze**: el disco del sistema vuelve a su
estado congelado en cada reinicio, y la imagen se rehace cada semestre. Eso
tiene una cara buena y una mala.

La buena es que el kiosco se vuelve prácticamente inmune: lo que se
rompa, lo que alguien borre, o lo que Defender ponga en cuarentena, vuelve a su
sitio al reiniciar.

La mala es que Deep Freeze tampoco distingue las **bitácoras que todavía no
llegaron al servidor**, que hasta ahora vivían en el perfil del usuario. Por eso
DigLab y el vigilante leen un ajuste `CarpetaDatos` que apunta fuera del disco
congelado, normalmente un ThawSpace:

```powershell
.\deploy\configurar_equipo.ps1 -Cuenta laboratorio -Contrasena LaContrasena `
    -Laboratorio "Laboratorio de Computo 1" `
    -CadenaConexion "Database=teschi_otru;Server=SERVIDOR;User Id=diglab;Password=xxx" `
    -CarpetaDatos T:\DigLabDatos
```

Ahí van la cola de bitácoras, la copia local del horario y los registros. Si el
equipo tiene Deep Freeze y no se indica `-CarpetaDatos`, el guion **se detiene**
en vez de avisar, porque lo que se pierde son datos de estudiantes.

El orden del despliegue importa: **descongelar, reiniciar, configurar, probar,
congelar**. Configurar un equipo congelado termina en verde y no deja nada.
`configurar_equipo.ps1` se niega a seguir si detecta el disco congelado, y
`diagnostico_equipo.ps1` lo dice en su sección 0.

El procedimiento completo, el reinicio de cada semestre y cómo comprobar que un
disco conserva lo que se escribe, en `docs/fase8-deepfreeze.md`.

`DigLab-Setup.exe` no detecta Deep Freeze ni pide la carpeta de datos: hay que
descongelar antes de instalar o actualizar, porque con el disco congelado termina bien
y no queda nada. Sin esa carpeta, las bitácoras sin enviar se pierden al reiniciar. Se
aceptó el 2026-11-01: si la bitácora de alguien no llega, su docente le da la
asistencia (db/22). Para conservarlas, usa el guion de arriba con `-CarpetaDatos`. Al
actualizar, el instalador conserva la carpeta que ya tenga configurada.

### 10. Dos usuarios de MySQL, no uno

Cada equipo del laboratorio lleva su `connections.config`, y **quien usa el equipo
puede leerlo**: DigLab corre con su propia sesión de Windows, así que todo lo que la
aplicación necesita para conectarse, esa persona también lo ve. Ocultar la carpeta no
sirve — oculto es un atributo, no un permiso — y dejarla sin permiso de lectura
tampoco, porque entonces DigLab tampoco podría leerla.

El ataque realista, entonces, no es inyectar SQL. Se revisaron las 165
sentencias de la aplicación una por una: todas usan parámetros, no hay ninguna
construida pegando texto del usuario. El ataque es más simple: leer las
credenciales y conectarse **directo** a la base desde cualquier cliente, sin
pasar por DigLab. Contra eso lo único que sirve es que ese usuario no pueda gran
cosa.

```bat
:: edita las dos contraseñas y el rango de red, y ejecuta desde cmd (como root
:: del servidor; sirve igual en MySQL 8 que en la MariaDB 5.5 de la escuela):
mysql -u root -p < db\07_usuarios_minimos.sql
```

| | `digbit_equipo` (los ~30 equipos) | `digbit_admin` (sólo el equipo de la administración) | `digbit_consulta` (responsables de laboratorio, db/18) |
|---|---|---|---|
| Catálogos, horario, clases | leer | leer y escribir | leer |
| Sesiones (`codigos_accesos`) | leer, crear la del día | todo | leer |
| Bitácoras | leer, añadir | todo | leer |
| `usuarios` | leer; actualizar **sólo** `password`, nombre, apellidos y correo; crear cuentas de estudiante ("Crear cuenta") | todo | leer |
| `carrera_grupo_semestre` | leer, crear (cuenta nueva, o la carrera que le falta a quien entra con el SIIA) y actualizar ("Mi perfil") | todo | leer |
| `configuracion` (ajustes de toda la escuela, db/13) | **sólo** leer: la dirección y la clave de la API de recuperación, el secreto de los códigos generados y la forma general de los códigos | todo | leer |
| `recuperacion_contrasena` (db/13) | nada: la usa solo la API, con su propio usuario | todo | nada |
| `personalizacion_clases` (lo que cada docente elige para sus clases, db/19) | leer, añadir, cambiar y borrar | todo | nada: la persona responsable no ve las pantallas docentes |
| `historial_cambios` (quién cambió qué y cuándo, db/21) | **sólo** añadir (db/24): la cuenta o la carrera de un estudiante que entra con el SIIA | todo (lo escribe al cambiar) | leer: alertas de cambios de horario e historial |
| `asistencias_dadas` (la asistencia que da el docente a quien no pudo registrar su bitácora, db/22) | leer, añadir y anular: **sólo** `activa`, `anulada_en` y `anulada_por`; nunca borrar | todo | leer |
| Cambiar el esquema, otras bases | no | no | no |

El reparto no se hizo a ojo: se inventariaron las 165 consultas rastreando **quién
llama a cada método**, para saber si la dispara una cuenta de estudiante, una docente o una de la
administración. Un permiso de menos rompe una clase entera; uno de más anula el
ejercicio.

Dos detalles que importan más de lo que parecen:

**El permiso sobre `usuarios` es por columna.** Con un `UPDATE` a secas,
cualquiera con esas credenciales se pondría `fk_tipo_usuario = 3` y tendría
una cuenta de la administración. Como esa columna no está en la lista, MySQL lo rechaza con el
error 1143.

**El equipo sólo puede crear cuentas de estudiante.** "Crear cuenta" (el login, también en
el kiosco, mientras los estudiantes no entren con el SIIA) necesita `INSERT` en `usuarios`, y con él a secas cualquiera se
crearía una cuenta de tipo 3. Por eso también es por columna y **sin**
`fk_tipo_usuario`: la cuenta nueva toma el valor por defecto de esa columna,
1 = estudiante, que pone `db/10_red_y_autoregistro.sql`. Un `INSERT` que nombre el
tipo se rechaza con el 1143.

**Las cuentas de la administración no se tocan desde un equipo.** El equipo tiene que
poder cambiar contraseñas (la migración perezosa a PBKDF2, "Mi perfil"), y MySQL
no deja limitarlo a la fila de quien entró. Sin más, bastaría copiar a
`ADMIN001` un hash cuya contraseña se conoce. Un disparador de la 07 lo impide:
con `digbit_equipo`, ninguna fila de tipo 3 se modifica. Tampoco con
`digbit_api`, el usuario de la API de recuperación por correo (db/13), cuyo
`config.php` vive en un hosting web.

Comprobado ejecutándolo con `digbit_equipo` en MariaDB 5.5.68 (la versión de la
escuela, sobre una copia de su base) y en MySQL 8.0: crea una cuenta de estudiante (sale de
tipo 1) y cambia la contraseña de un estudiante; nombrar el tipo, darse el
tipo de la administración, tocar la cuenta de la administración, `REPLACE` u `ON DUPLICATE KEY
UPDATE`, borrar bitácoras o tablas, todo se rechaza.

**Y una tercera, solo para leer: `digbit_consulta` (db/18).** Con ella trabaja
la sesión de una persona responsable de laboratorio, en cualquier equipo (ver «Responsable
de laboratorio»): solo `SELECT`, y solo en las tablas que enseña el panel de
administración; nunca `recuperacion_contrasena` ni `tipo_usuarios`. No va en
ningún `connections.config`: al entrar una persona responsable, DigLab lee su contraseña
de `configuracion` con la cuenta del equipo y arma la cadena con el mismo
servidor. La crea root con `db\18_encargados.sql` (la 07 no la toca), y DigLab
no deja entrar a una persona responsable si esa cuenta puede escribir algo.

Lo que **no** cubre:

- `digbit_equipo` puede leer la tabla `usuarios` completa, contraseñas
  incluidas, porque le hace falta para validar el login. Lo que lo hace
  soportable es que desde la sección 8 son PBKDF2 con 100 000 iteraciones.
- Puede cambiar la contraseña de cualquier cuenta de **estudiante o docente** (los
  equipos docentes entran con este mismo usuario). Con eso, quien lea
  `connections.config` puede entrar con la cuenta de otra persona, estudiante o docente. Cerrarlo
  del todo pide mover el cambio de contraseña a un procedimiento almacenado.
- Puede meter bitácoras falsas y crear cuentas de estudiante con cualquier
  matrícula libre (la aplicación sólo acepta las de 10 cifras, un cliente de
  MySQL no).
- Puede leer el secreto de los códigos generados (`configuracion`), y con él
  calcular el código de cualquier clase y día, igual que ya puede leer los
  códigos de los docentes y las sesiones de hoy. Que solo la administración vea
  los códigos es una regla de las pantallas.
- Puede leer en `configuracion` la contraseña de `digbit_consulta`, pero con
  ella solo se lee lo que `digbit_equipo` ya lee. Por eso el disparador tampoco
  protege las cuentas de responsables de laboratorio: quien cambie la contraseña de una y
  entre con ella solo ve, sin poder cambiar nada, lo que ya podía leer.
- Puede leer y cambiar lo que cualquier docente eligió para sus clases
  (`personalizacion_clases`, db/19: imagen, color y nombre corto). Solo lo ve
  esa persona y no toca la clase; es del mismo tipo que cambiar su contraseña.
  Cada guardado desde DigLab queda en el log con el número de empleado, y se
  deshace con «Restablecer».

### ¿Por qué hace falta `nuget restore` aparte?

El proyecto usa `packages.config` (formato antiguo), no `PackageReference`.
Con ese formato `msbuild -t:Restore` **no restaura nada** y las rutas
`HintPath` del `.csproj` apuntan a `..\packages\`, que está en `.gitignore`.
Sin el restore, un clon limpio falla con ~20 errores `CS0246`
(`MySql`, `iTextSharp`, `Google` no encontrados). El script se encarga; si
compilas desde Visual Studio, el IDE lo hace solo al abrir la solución.

Ese mismo formato tiene una trampa: estar en `packages.config` **no** basta
para que un paquete llegue a `bin\`. Hace falta además un `<Reference>` con su
`HintPath` en el `.csproj`, porque `packages.config` no arrastra dependencias
transitivas. Seis paquetes estaban sólo en la lista y no en las referencias, de
modo que nuget los bajaba y msbuild no los copiaba: `System.Buffers`,
`System.Memory`, `System.Numerics.Vectors`,
`System.Runtime.CompilerServices.Unsafe`, `System.Threading.Tasks.Extensions` y
`K4os.Hash.xxHash`. El fallo no se veía casi nunca porque casi ningún camino
los toca, pero `MySqlConnection.BeginTransaction` necesita el de
`Threading.Tasks`, así que **el alta de estudiantes reventaba** con
`FileNotFoundException` (es la única operación que usa transacción). Si añades
un paquete nuevo, comprueba que su DLL aparece en `bin\`.

## Estructura del repositorio

```
DigLab.sln                     DigLab, DigLab.Vigilante, DigLab.Configurador y DigLab.Instalador
build.cmd / build.ps1          Compilación y arranque con un comando
crear_setup.ps1                Arma DigLab-Setup.exe con Inno Setup 7; no lo arma si dentro aparece la contraseña de su connections.config o de api\config.php
crear_instalador.ps1           Arma el instalador anterior, DigLab-Instalador.exe
instalador/
  DigLab.iss                   El asistente: sus páginas, sus comprobaciones y las llamadas a DigLab.Configurador
  DigLab-Setup.exe             Copia del instalador armado con este código, con el servidor, la base y el usuario de la escuela, sin contraseña
db/
  01_schema.sql                Esquema de las 14 tablas (reconstruido desde el código)
  02_seed.sql                  Datos de prueba
  03_migracion_fase2.sql       Para una base que ya existía: una bitácora por estudiante y código
  04_migracion_horarios.sql    Para una base que ya existía: cuenta real de la administración, horarios, sesiones
  05_migracion_sin_conexion.sql   Para una base que ya existía: marca de bitácoras registradas sin servidor
  06_migracion_contrasenas.sql    Para una base que ya existía: ensancha password para PBKDF2 y mide la migración
  00_revision_previa.sql       Antes de migrar: dice qué va a fallar, sin cambiar nada
  07_usuarios_minimos.sql      Los dos usuarios de MySQL con permisos mínimos (equipos y administración)
  08_migracion_bajas.sql       Para una base que ya existía: dar de baja cuentas y desactivar laboratorios sin borrar su historial
  09_codigo_por_profesor.sql   Para una base que ya existía: un código por docente para todas sus clases
  10_red_y_autoregistro.sql    Para una base que ya existía: equipos sin internet, hora de llegada y alta de estudiantes sin grupo
  11_modo_de_acceso.sql        Para una base que ya existía: modo de acceso de cada laboratorio (todos en código fijo)
  12_color_profesor.sql        Para una base que ya existía: el color elegido para cada docente
  13_recuperacion_contrasena.sql   Para una base que ya existía: códigos de recuperación por correo y ajustes de toda la escuela
  14_registro_sin_clase.sql    Para una base que ya existía: sesiones de uso libre (sin clase) en los laboratorios sin códigos
  15_reportes_profesor.sql     Para una base que ya existía: las fallas que reporta cada docente durante su clase
  16_ciclo_preliminar.sql      Para una base que ya existía: las tablas de las configuraciones de horario (el horario del próximo ciclo)
  17_asistencia_retirada.sql   Para una base que ya existía: las asistencias que cada docente quita durante su clase
  18_encargados.sql            Responsables de laboratorio: el tipo 4 y la cuenta de MySQL de solo lectura digbit_consulta (con root)
  19_personalizacion_clases.sql   Para una base que ya existía: lo que cada docente elige para sus clases (imagen, color y nombre corto)
  20_vigencia_franjas.sql      Para una base que ya existía: la vigencia propia de cada franja (cambiar el horario sin reescribir el pasado)
  21_historial_cambios.sql     Para una base que ya existía: quién cambió el horario, los laboratorios o una bitácora, cuándo y qué decía antes
  22_asistencia_dada.sql       Para una base que ya existía: la asistencia que da el docente a quien no pudo registrar su bitácora, con quién, cuándo y por qué
  23_horarios_guardados.sql    Para una base que ya existía (después de la 16): guardar varias configuraciones de horario con nombre, prepararlas y aplicarlas con sus fechas
  24_docentes_siia.sql         Para una base que ya existía: entrar con el SIIA (el enlace de cada docente con su número de empleado y quién espera que lo enlacen)
  25_horas_por_asignar.sql     Para una base que ya existía: las horas que faltan por asignar en el horario en uso (las que otra clase desplazó o se quitaron para darlas en otro horario)
  26_fallas_resueltas.sql      Para una base que ya existía: las fallas reportadas que ya se resolvieron (salen de naranja y de Alertas, y van al final del PDF)
  27_suspensiones.sql          Para una base que ya existía: suspender el acceso de un estudiante por unos días, con la razón (vuelve a entrar solo al vencer el plazo)
  28_memorias_emergencia.sql   Para una base que ya existía: las memorias USB de emergencia, registradas desde DigLab; cualquiera abre cualquier equipo (ya no se autorizan al instalar)
api/                           API en PHP de "¿Olvidaste tu contraseña?" para el hosting de la escuela (ver api/LEEME.md)
  recuperar.php                Solicitar un código, confirmarlo con la contraseña nueva, y probar la instalación
  lib/comun.php, lib/correo.php   Base de datos, contraseñas (PBKDF2 como DigLab) y un cliente SMTP para Gmail
  config.example.php           Plantilla de config.php (que lleva las contraseñas y no se versiona)
deploy/
  instalar_vigilante.ps1       Instala/quita el servicio DigLab.Vigilante (como administrador)
  probar_vigilante.ps1         Escenarios del vigilante en simulación, sin base de datos
  probar_reanudar.ps1          DigLab reanuda un equipo liberado, sin base de datos
  configurar_equipo.ps1        Deja un equipo del laboratorio con DigLab como shell (fase 5)
  diagnostico_equipo.ps1       Dice en qué estado quedó un equipo; no cambia nada
  migrar_bd.ps1                Respalda, revisa y aplica las migraciones; se detiene si algo bloquea
  probar_vigilante_limites.ps1 El vigilante contra entradas torcidas: 12 casos que se comprueban solos
  deepfreeze.ps1               Detecta Deep Freeze y si el disco está congelado (fase 8)
  actualizar_diglab.ps1        Sustituye la copia desplegada conservando la configuración
  permitir_en_defender.ps1     Excluye DigLab y recupera lo que Defender puso en cuarentena
  abrir_bd_en_red.ps1          Abre el MySQL de pruebas al resto de la red
  probar_horarios.ps1          Escenarios de horarios y códigos, sin base de datos
  probar_configuraciones.ps1   Las configuraciones de horario (guardar, preparar, editar, permisos) contra una base de desarrollo; no deja datos
  probar_aplicar.ps1           Aplicar y deshacer una configuración de horario contra una base de desarrollo; no deja datos
docs/
  fase3-diseno.md              Diseño de la liberación del equipo y el cierre de sesión
  fase6-horarios.md            Diseño de los horarios por laboratorio (agenda de la administración)
  fase4-sin-conexion.md        Diseño del modo sin conexión (copia local y cola de bitácoras)
  fase7-contrasenas.md         Diseño del paso de MD5 a PBKDF2 y de la migración perezosa
  fase8-deepfreeze.md          Deep Freeze, la carpeta de datos persistente y el reinicio semestral
DigLab/
  Program.cs                   Punto de entrada: instancia única, excepciones, AppContexto
  App.config                   Enlaces de ensamblado y ModoKiosco; conexión delegada a connections.config
  connections.config.example   Plantilla versionada de la cadena de conexión
  Infraestructura/
    AppContexto.cs             Ciclo de vida: reanudar, comprobar entorno, login, cerrar sesión, salir
    Kiosco.cs                  Modo kiosco (pantalla completa, sin Alt+F4)
    DirectivasKiosco.cs        Kiosco en la cuenta que el equipo ya tenía: pone tres límites al arrancar y los quita al entregar el escritorio
    Emergencia.cs              Las memorias de emergencia: qué lista vale (la base, la copia del equipo o la de la instalación) y si la USB conectada es una de ellas
    CarpetaDatos.cs            Carpeta que sobrevive a Deep Freeze (compartida con el vigilante)
    LaboratorioEquipo.cs       Laboratorio en el que está este equipo (appSettings / variable)
    SinConexion.cs             Estado del modo sin conexión: activar con la copia, sincronizar al volver
    EnviadorBitacoras.cs       Hilo que sube la cola de bitácoras en segundo plano, con reintentos
    AvisoApagado.cs            Antes de apagar: envía lo pendiente o avisa de que se perdería
    Log.cs                     Registro de diagnóstico (compartido con el vigilante)
    Permisos.cs                Lo que puede cambiar la sesión: la persona responsable de laboratorio (tipo 4), solo consulta
    LibroExcel.cs, LectorExcel.cs   Escribir y leer libros de Excel (.xlsx) sin bibliotecas: la lista de asistencia y el horario en cuadrícula
    SesionActiva.cs            Archivo de traspaso DigLab ↔ vigilante (compartido)
    SesionEquipo.cs            Equipo liberado: bandeja, aviso, cierre de sesión
  conexion/                    Capa de acceso a datos
    Conexion.cs                Lee la cadena (variable de entorno → connections.config) y la prueba
    Consultas.cs               Consultas de lectura y actualización
    Consultas.Sesiones.cs      Consultas por sesión (ventana, PDF, bitácora, marca "con falla")
    Contrasenas.cs             PBKDF2 con sal, verificación y migración perezosa desde MD5
    HorariosDatos.cs           Clases, horario por laboratorio, excepciones, agenda y resolución del código
    CacheHorario.cs            Copia local del horario del laboratorio y resolución del código sin servidor
    ColaBitacoras.cs           Cola de bitácoras registradas sin servidor y su subida
    InsercionDatos.cs          Alta de usuarios heredada (las secciones nuevas usan los *Datos)
    AlumnosDatos.cs, ProfesoresDatos.cs, LaboratoriosDatos.cs   Lo que leen y escriben las secciones de la administración
    UsuariosDatos.cs           Lo común a las cuentas de estudiantes, docentes y responsables: alta, edición, contraseña, baja
    EncargadosDatos.cs         Las personas responsables de laboratorio (tipo 4) para su sección
    CuentaDeConsulta.cs        La cuenta de MySQL de solo lectura con que trabaja la sesión de una persona responsable (db/18)
    LaboratoriosDeshabilitados.cs   Laboratorios deshabilitados: sus periodos en configuracion y qué no corre en ellos
    CicloPreliminarDatos.cs, CicloPreliminarDatos.Aplicar.cs, CicloPreliminarDatos.Baja.cs   Próximo ciclo: las plantillas, y aplicar, deshacer y sustituir lo de hoy
    CicloPreliminarDatos.Excel.cs   Próximo ciclo: lo registrado para leer un Excel y cargarlo en la preparación (todo o nada)
    RevisionCiclo.cs, RevisionAplicacion.cs   Lo que se revisa antes de aplicar: en rojo no deja, en ámbar avisa
    CatalogosDatos.cs          Carreras, semestres y grupos para los diálogos
    Esquema.cs                 Qué migraciones tiene la base (08 a 25), para no fallar si faltan
    HistorialCambios.cs        Quién cambió qué y cuándo (db/21): lo anota cada cambio en su transacción y lo leen Alertas y el historial
    AsistenciasDadas.cs        Las asistencias que da el docente (db/22): motivos, textos y lo que leen la Bitácora de la clase, el historial y Faltas
    SesionDeHoy.cs             El bloque de hoy en curso de una clase y su sesión (la lee o la crea) para dar una asistencia o reportar una falla
    EstadisticasDatos.cs       Estadísticas: una lectura (asistencia en lote, Faltas, fallas y avisos) y el cálculo de las cifras y las pestañas, sin volver a la base
    VigenciaFranjas.cs         Cuándo se da una franja: la vigencia de su clase recortada a la suya (db/20), en SQL y en memoria
    PersonalizacionClasesDatos.cs   Lo que cada docente elige para sus clases (db/19): leerlo ya resuelto por clase, sus imágenes y guardarlo
    RecuperacionContrasena.cs  Habla con la API de recuperación: pedir el código, confirmarlo y probarla
    InicioConSiia.cs           Habla con el SIIA: número y contraseña (login.ashx), el NIP (nip.ashx, verificar) y «Comprobar»
    Consultas.Siia.cs          Qué pasa al entrar con el SIIA: docentes (enlazados o en espera) y estudiantes (su cuenta se usa o se crea), y la huella
    EnlacesSiia.cs             Docentes por enlazar: la solicitud, enlazar, descartar y desenlazar (con su historial)
    AjustesDatos.cs            Ajustes de toda la escuela guardados en la base (la dirección de esa API)
    ReglasCodigo.cs            Qué clase abre el código de cada docente en este laboratorio a esta hora
    AlertasDatos.cs            Alertas de la administración: clases sin bitácoras (como Faltas), en curso sin nadie, fallas por laboratorio y día y cambios de horario de otras personas
    ModosDeAcceso.cs           Modo de acceso de cada laboratorio (sin códigos, código único, generación de códigos), códigos de sesión y CodigosGenerados
    Codigo_Gemerador.cs        Generador de códigos aleatorios
    Datos_User.cs              Sesión en memoria (usuario, código y ventana actuales)
    VentanaCodigo.cs           Inicio, fin y estado de un código
    Usuario.cs, OpcionCombo.cs DTOs
  RJControls/                  Controles personalizados (botones, cajas de texto, combos)
  AvisoSesion.cs, PantallaError.cs   Ventanas de aviso de fin de sesión y de error
  RegistroExitoso.cs           Equipo propio: registro guardado, apagar o volver al login
  PrincipalAdministrador.cs    Administración: las seis secciones (con Estadísticas) y «Los laboratorios ahora» (la persona responsable de laboratorio, en solo consulta)
  VentanaAlertas.cs            Administración: las alertas, con filtros, y de ahí a la clase, sus bitácoras, su horario o Faltas; marcar una clase como no dada
  VentanaHistorialCambios.cs   Administración y responsables de laboratorio: el historial de cambios (db/21), con filtros y lo de antes y lo de ahora
  VentanaEstadisticas.cs       Administración y responsables de laboratorio: Estadísticas (filtros, cinco cifras, seis pestañas y CSV)
  PiezasEstadisticas.cs        Las cifras grandes de Estadísticas y las barras dentro de las celdas de sus tablas
  BotonCabecera.cs             Las pastillas de la cabecera del panel de administración (Mi perfil, Ajustes, Códigos, Alertas con campana y globo; Solo consulta)
  DialogoContrasenaAdmin.cs    Vuelve a pedir la contraseña de la administración antes de un cambio delicado; tres fallos cierran la sesión
  DialogoAccesoClases.cs       Cómo entra cada estudiante (por escuela o laboratorio) y el código de una clase abierta por la administración
  DialogoAjustes.cs            Administración: Ajustes (cambios de horario, la API de recuperación con «Probar», el aviso de privacidad y el inicio de sesión con el SIIA)
  DialogoEnlazarSiia.cs        Administración: «Docentes por enlazar» (quién entró con el SIIA y con qué docente se enlaza)
  VentanaEnlacePendiente.cs    Lo que ve el docente que el SIIA acepta y aún no está enlazado (se revisa sola y entra en cuanto lo enlazan)
  DialogoRecuperarContrasena.cs   "¿Olvidaste tu contraseña?": matrícula, código del correo y contraseña nueva
  PanelLaboratoriosAhora.cs    El panel: hora anterior, actual y siguiente de cada laboratorio
  PantallaAdmin.cs             Base de las listas de la administración y sus diálogos
  AdministrarAlumnos.cs, AdministrarProfesores.cs, AdministrarLaboratorios.cs   Secciones de la administración (Estudiantes, Docentes, Laboratorios)
  AdministrarEncargados.cs     Responsables de laboratorio, desde Docentes: alta, edición, contraseña y baja
  GestionBitacorasAdministrador.cs   Bitácoras: sesiones con filtros, quién registró y PDF (también «Mis bitácoras» del panel docente)
  AdministrarHorarios.cs       Administración: Horarios por laboratorio (pestañas Horario semanal, Clases, Cambios de un día, Faltas y Próximo ciclo; choques de la semana)
  PestanaProximoCiclo.cs, PestanaAplicacion.cs   Horarios → Próximo ciclo: plantillas, preparación y aplicar con fechas
  HorarioEnExcel.cs, DialogosExcel.cs   Próximo ciclo: el horario en Excel (la plantilla en cuadrícula, leerla contra lo registrado, qué corregir y qué cambia)
  ColoresProfesor.cs           Un color por docente (el elegido o uno automático), el mismo en Horarios, Docentes y el panel
  SelectorColorProfesor.cs     El selector de color del diálogo de docente: muestra, automático, paleta y "Otro..."
  Profesor_Principal.cs, TarjetasProfesor.cs   Docente: su código con las bitácoras de la clase, sus clases de ayer, hoy y mañana, y sus secciones
  BitacoraMisClases.cs         Docente: "Mi horario" con su código en grande; al pulsar una clase abre su bitácora
  PersonalizacionClases.cs     Docente: cómo ve cada una de sus clases (círculo, color automático o elegido, nombre corto), en memoria
  CirculoClase.cs              El círculo de cada clase (su imagen o sus iniciales sobre su color), el lomo de color y el botón del círculo
  ImagenDeClase.cs             Abre, endereza, recorta y reduce la imagen de una clase (128 x 128, como mucho 60 000 bytes)
  DialogoPersonalizarClase.cs  Docente: "Personalizar tu clase": imagen, color, nombre corto y a qué grupos se aplica
  DialogoDarAsistencia.cs      Docente: «Dar asistencia» en Mis grupos (desde la casilla o por matrícula), con motivo, equipo y detalle (db/22)
  BitacoraSesion.cs            Quién registró en una clase: tabla en vivo, cuenta atrás y PDF
  TablaAlumnosPdf.cs           La tabla de estudiantes de los PDF de bitácoras
  Alumno_Principal.cs          Estudiante: el código, cómo está el equipo y si ve otros sin internet
  *.cs / *.Designer.cs / *.resx   Formularios WinForms
  img/, Resources/             Imágenes y plantilla HTML del PDF
DigLab.Vigilante/              Servicio de Windows (LocalSystem): reinicia el equipo al terminar la clase (hora de salida)
  Program.cs                   Servicio, o --consola [--simular] [--segundos N] para probar
  Vigilante.cs                 El bucle: lee el traspaso cada 10 s, avisa, cierra la sesión; y cada 10 min renueva la copia de las memorias de emergencia (DigLab.exe --memorias-emergencia, como SYSTEM)
  VigilanteService.cs          Envoltorio ServiceBase; recibe los cambios de sesión de Windows
DigLab.Configurador/           Lo que DigLab-Setup.exe no puede hacer solo, sin ventana y en C#
  Program.cs                   Las órdenes: probar, estado, validar-cuenta, preparar, configurar, diagnostico y revertir (y prueba-*, solo de desarrollo)
  Pasos.cs                     Lo instalado; preparar, configurar (con la copia de las memorias de emergencia) y revertir, en orden
  Cuenta.cs, Kiosco.cs         La cuenta del kiosco, su shell, sus directivas y el inicio automático
  Servicio.cs, Defender.cs     El vigilante y la exclusión de Microsoft Defender
  Archivos.cs, BaseDatos.cs    Los .config, la carpeta en sólo lectura, la conexión y la lista de laboratorios
  Diagnostico.cs               Cómo quedó el equipo, para la última página del asistente
  Sistema.cs, Salida.cs        Los comandos (sin la variable DIGLAB_SETUP=1, que pone el Setup, solo simula) y lo que le cuenta al Setup
DigLab.Instalador/             El instalador anterior, que envuelve los guiones de deploy/
```

Tablas de la base de datos:

| Tabla | Contenido |
|---|---|
| `usuarios` | Cuentas de estudiantes, docentes, la administración y responsables de laboratorio (`fk_tipo_usuario`), con la contraseña en PBKDF2 (ver sección 8) |
| `carrera_grupo_semestre` | Carrera, grupo y semestre de cada estudiante |
| `carreras`, `grupos`, `semestres`, `materias`, `laboratorios` | Catálogos |
| `clases`, `horarios`, `horario_excepciones` | Cada clase (docente + grupo + materia), sus franjas por laboratorio y los cambios puntuales |
| `codigos_accesos` | Una fila por sesión (una clase en una fecha) con el código con que se abrió, según el modo del laboratorio: el de su docente (`usuarios.codigo_clase`), el generado de esa clase y día, o uno al azar (sin códigos) |
| `registros_bitacoras` | Bitácora que llena cada estudiante con un código |
| `recuperacion_contrasena` | Códigos de recuperación por correo (su HMAC, no el código); los usa solo la API |
| `configuracion` | Ajustes de toda la escuela, como la dirección de la API de recuperación, el secreto de los códigos generados, los periodos de los laboratorios deshabilitados y la contraseña de `digbit_consulta` (los equipos solo la leen) |
| `personalizacion_clases` | Lo que cada docente elige para sus clases, por materia (en todos sus grupos) o por grupo: imagen, color y nombre corto. Solo lo ven sus pantallas (db/19) |
| `historial_cambios` | Quién cambió el horario, las clases, los laboratorios o una bitácora, cuándo, y qué decía antes y qué dice ahora. Solo se añade (db/21) |
| `asistencias_dadas` | La asistencia que da el docente, durante su clase, a quien no pudo registrar su bitácora: quién, cuándo, por qué y desde qué equipo. Anularla no la borra (db/22) |

## Estado del proyecto y trabajo pendiente

Este repositorio está en proceso de saneamiento. Lo que ya está resuelto y lo
que falta, en orden de prioridad:

- [x] Compilación reproducible desde un clon limpio (`nuget restore` en el script).
- [x] Esquema de base de datos versionado.
- [x] Arranque robusto: instancia única, excepciones no controladas a log y
      pantalla propia, comprobación de conexión al arrancar, modo kiosco opcional,
      "Salir" = cerrar sesión.
- [x] Sustituir la cuenta de administración escrita en duro por una cuenta real
      (`fk_tipo_usuario = 3`; `ADMIN001` en el seed, migración 04 en la base real).
- [x] Contraseñas con PBKDF2-SHA256 y sal por usuario en vez de MD5 a secas
      (`conexion\Contrasenas.cs`). Los hashes viejos siguen sirviendo y cada uno
      se reemplaza solo la primera vez que esa persona entra bien: nadie tiene
      que cambiar su contraseña. Falta borrar el camino de MD5 cuando la
      consulta de `db\06_migracion_contrasenas.sql` diga que ya no queda
      ninguno.
- [x] Alta de docentes sólo para la administración. «Crear cuenta» del login
      solo crea cuentas de estudiante (con `digbit_equipo` la base no deja crear otra cosa,
      db/07); las de docentes, estudiantes y responsables de laboratorio las da de alta
      la administración desde sus secciones. Lo que puede cambiar cada sesión lo
      decide `Permisos` (`DigLab\Infraestructura\Permisos.cs`) por el tipo de la
      cuenta: la persona responsable de laboratorio (tipo 4, db/18) ve el mismo panel en
      solo consulta, con la cuenta de MySQL de solo lectura `digbit_consulta`.
- [x] Códigos con ventana de validez real (reloj del servidor) y una sola
      bitácora por estudiante y código.
- [x] Liberar el equipo al guardar la bitácora y cerrar la sesión de Windows a
      la hora de salida (DigLab + servicio `DigLab.Vigilante`). Probado en
      simulación; falta probarlo en un equipo del laboratorio con el servicio
      instalado.
- [x] Guardar carrera/grupo al registrar una cuenta de estudiante.
- [x] Horarios por laboratorio (fase 6): la administración arma la agenda de
      cada laboratorio (docente, grupo, materia, código fijo) con cambios de un
      día (pestañas Horario semanal, Clases, Cambios de un día, Faltas y Próximo
      ciclo); el personal docente deja de generar códigos; cada equipo conoce su
      laboratorio; fila naranja para la bitácora que reporta una falla. Capa de
      datos probada contra un MySQL local; las pantallas compilan y falta
      probarlas con datos reales en el TESCHI.
- [x] Modo sin conexión (fase 4): copia local del horario, estudiantes entran con
      su matrícula, bitácoras en cola que se suben solas, marca `[sin conexion]`
      en los informes. Probado apagando y encendiendo un MySQL local.
- [ ] Modo kiosco completo (fase 5): `DigLab-Setup.exe` deja DigLab como shell de
      la cuenta elegida, con inicio de sesión automático y el servicio instalado.
      Probado en una máquina virtual, con un armado anterior al de `instalador\`,
      hasta entrar con el SIIA. Falta confirmar el escritorio sin límites, el cierre
      de sesión al vencer, la memoria de emergencia, actualizar y desinstalar, y
      ejecutarlo en los equipos del laboratorio.

Hay archivos `.cs` en el árbol que **no forman parte de la compilación**
(no están en `DigLab.csproj`) y están pendientes de borrar: `conexion.cs`,
`cone/Conexion.cs`, `Agregar_Laboratorio.*`, `Eliminar_Laboratorio.*`,
`RegistroProfes.*` y `RJControls/textBox.cs`. Dos de ellos contienen
credenciales de desarrollo antiguas; no los uses como referencia.
