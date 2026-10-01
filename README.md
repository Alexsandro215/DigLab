# DigBit 2.0

Sistema de registro de bitácoras para los laboratorios de cómputo del TESCHI.
Aplicación de escritorio **WinForms sobre .NET Framework 4.7.2** con base de
datos **MySQL**.

## Cómo funciona

```
Administrador arma el horario de cada laboratorio:
    clase = maestro + grupo + materia + CÓDIGO FIJO, en franjas semanales
        │
        ▼
Alumno inicia sesión en un equipo del laboratorio → teclea el código de su clase
        │   (solo vale en ESE laboratorio y dentro de SU franja; el administrador
        │    puede cancelar una franja un día o programar una sesión extra)
        ▼
Llena la bitácora de fallas (red / hardware / software)
        │   → el equipo queda liberado hasta la hora de salida
        │   → si marcó "equipo propio": el equipo NO se libera; elige apagarlo
        │      o volver al login para el siguiente alumno
        ▼
Profesor y administrador descargan las bitácoras en PDF
    (celda naranja: la falla concreta que reportó el alumno)
```

Tres roles, los tres en la tabla `usuarios`:

| Rol | `fk_tipo_usuario` | Pantalla principal |
|---|---|---|
| Alumno | 1 | `Alumno_Principal` |
| Profesor | 2 | `Profesor_Principal` |
| Administrador | 3 | `PrincipalAdministrador` |

## Requisitos

- Windows 10/11.
- **Build Tools for Visual Studio 2022** (o Visual Studio 2022) con la carga de
  trabajo *Desarrollo de escritorio de .NET*. `build.ps1` localiza MSBuild con
  `vswhere`, así que no importa dónde esté instalado.
- **MySQL 8.x** accesible desde el equipo.
- Conexión a internet la primera vez (para descargar `nuget.exe` y los paquetes).

## Puesta en marcha

### 1. Base de datos

El nombre de la base **tiene que ser `teschi_otru`**: seis consultas de
`Consultas.cs` lo llevan escrito en duro. Los scripts ya lo crean con ese nombre.

```powershell
mysql -u root -p < db\01_schema.sql   # crea la base y las 8 tablas
mysql -u root -p < db\02_seed.sql     # catálogos y usuarios de prueba (solo desarrollo)
```

Si la base **ya existía** (por ejemplo la del servidor del TESCHI), no la
recrees: aplica las migraciones en orden, cada una una sola vez. La de la fase
6 da de alta al administrador (**edita su contraseña en el script antes**),
crea las tablas de horarios y convierte `codigos_accesos` en "sesión":

```powershell
mysql -u root -p < db\03_migracion_fase2.sql
mysql -u root -p < db\04_migracion_horarios.sql
mysql -u root -p < db\05_migracion_sin_conexion.sql
mysql -u root -p < db\06_migracion_contrasenas.sql
mysql -u root -p < db\08_migracion_bajas.sql
mysql -u root -p < db\09_codigo_por_profesor.sql
```

Usuarios que deja el seed:

| Usuario | Contraseña | Rol |
|---|---|---|
| `20230001` | `alumno123` | alumno, grupo ISC-5001 |
| `20230002` | `alumno123` | alumno, grupo ISC-5002 |
| `EMP001` a `EMP006` | `profesor123` | profesores |
| `ADMIN001` | `admin123` | administrador |

El seed deja un **horario realista** de un semestre: cuatro laboratorios, seis
maestros, siete grupos, once materias y dieciocho clases con su código fijo en
bloques de dos horas sin choques. Sirve para ver la cuadrícula del
administrador con datos parecidos a los reales. Algunos códigos útiles con el
equipo configurado como `Laboratorio de Computo 1`:

| Código | Clase | Qué prueba |
|---|---|---|
| `AB12c` | Bases de Datos, ISC-5001, EMP001 | Lunes y miércoles 08:00 a 10:00 en el Lab 1. Además, el seed le deja una sesión extra en el Lab 1 desde la hora en que se carga y durante cuatro horas, para probar el flujo del alumno sin esperar a su franja (recarga el seed cuando venza) |
| `QW7pz` | Redes, ISC-5002, EMP002 | Lab 2: desde el Lab 1 responde "es de otro laboratorio" |
| `EXP01` | POO, ISC-7001, EMP001 | Vigencia terminada hace un mes (en gris en la cuadrícula) |
| `FUT01` | Redes, ISC-5001, EMP001 | Vigencia empieza dentro de un mes (en gris) |
| `NOHOY` | Taller de Titulación, ISC-7001, EMP005 | Sin franjas: "hoy no hay clase"; tiene una sesión extra mañana en el Lab 3 |

También deja una cancelación de ejemplo (AB12c del próximo miércoles, "Junta
académica") y una sesión extra mañana en el Lab 3, que se ven resaltadas en la
cuadrícula del administrador, y cuatro sesiones de la semana pasada con once
bitácoras entre todas, unas con fallas y otras sin novedad, para ver la
pantalla de bitácoras y las filas naranja en el PDF.
Los guiones de prueba crean sus propios datos (códigos `TST…`) y los borran.

### 2. Credenciales de conexión

`build.ps1` copia `DigBit\connections.config.example` a
`DigBit\connections.config` si no existe. Edita ese archivo con tus datos:

```xml
<add name="DigBit"
     connectionString="Database=teschi_otru;Server=localhost;User Id=digbit;Password=TU_CONTRASENA"
     providerName="MySql.Data.MySqlClient" />
```

`connections.config` está en `.gitignore`: nunca se versiona.

Alternativa sin tocar archivos, útil para despliegues: la variable de entorno
`DIGBIT_CONNECTION_STRING` tiene prioridad sobre el archivo.

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
4. Cierra instancias abiertas de DigBit (bloquean el `.exe` al enlazar).
5. Crea `connections.config` desde la plantilla si falta.
6. Compila `DigBit.sln`: DigBit y el servicio `DigBit.Vigilante`.
7. Lanza `DigBit\bin\<Configuration>\DigBit.exe`.

### 4. Modo kiosco y registro de errores

DigBit está preparado para correr como **shell de Windows** en los equipos del
laboratorio (sin escritorio ni barra de tareas). Ese comportamiento se activa
con una bandera; apagada, la app es una ventana normal, que es lo cómodo para
desarrollar.

| Dónde | Valor | Prioridad |
|---|---|---|
| Variable de entorno `DIGBIT_MODO_KIOSCO` | `1` / `true` | Alta |
| `App.config` → `<appSettings>` → `ModoKiosco` | `true` / `false` (por defecto `false`) | Baja |

Con el kiosco **activado**:

- Los formularios principales ocupan toda la pantalla, sin borde.
- `Alt+F4` no cierra nada; no hay botón de minimizar ni de salir en el login.
- **Salir** dentro de la app cierra la sesión (olvida al usuario y vuelve a un
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
- El **registro de diagnóstico** queda en `%LOCALAPPDATA%\DigBit\logs\digbit-AAAAMMDD.log`
  (un archivo por día). Es el primer sitio donde mirar cuando un equipo falla.
- El tiempo de espera de conexión es de **5 segundos** (el driver trae 15 por
  defecto, y toda consulta va en el hilo de interfaz). Si la cadena de conexión
  trae su propio `Connection Timeout`, se respeta.

Antes de mostrar el login, la app **comprueba la conexión** con el servidor. Si
falla, lo dice con claridad (configuración ilegible, servidor inalcanzable, base
inexistente) y señala la ruta del log.

### 5. Códigos de acceso y horarios por laboratorio

Los códigos **no los genera el profesor**: los asigna el administrador al armar
el horario (diseño completo en `docs/fase6-horarios.md`).

- Una **clase** es maestro + grupo + materia con un **código fijo** (5
  caracteres, único en toda la base) y una vigencia (el semestre).
- El **horario** de cada laboratorio son franjas semanales (día, hora de inicio,
  hora de fin) de esas clases. Al guardar una franja se rechaza cualquier
  solape: mismo laboratorio, mismo profesor o mismo grupo a la misma hora.
- Las **excepciones** son por día: cancelar una franja, o programar una **sesión
  extra** (el código de una clase en un laboratorio y horario temporales).
- Cada equipo sabe **en qué laboratorio está** (`appSettings` → `Laboratorio`,
  o la variable `DIGBIT_LABORATORIO`, que tiene prioridad). En kiosco, sin
  laboratorio configurado la app no arranca y lo dice en pantalla.

Cuando el alumno teclea un código, la app lo resuelve **para ese laboratorio y
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
crea sola la primera vez que alguien usa el código ese día; de ahí cuelgan las
bitácoras, los PDF y la liberación del equipo. Sigue habiendo **una bitácora por
alumno y sesión** (`UNIQUE (fk_codigo_accesos, fk_usuario)`), y la ventana se
vuelve a comprobar al guardar.

La pantalla de bitácoras del administrador trabaja en **dos niveles**: arriba
las sesiones (fecha, horario, código, materia, grupo, profesor, laboratorio y
cuántos alumnos registraron, cuántos reportaron alguna falla y cuántos sin conexión)
y abajo los alumnos de la sesión elegida. Los filtros de búsqueda, docente y
fecha actúan sobre las sesiones; *Descargar PDF* baja la sesión elegida y
*Descargar lote*, todas las filtradas. En "Modificar bitácoras" se edita el
registro del alumno seleccionado abajo.

En las tablas y los PDF, la bitácora que **reporta alguna falla o comentario**
sale en **naranja claro**, y dentro de ella la **falla concreta va en naranja
intenso y negrita**, así que el administrador la localiza sin leer toda la
fila. La que no reportó nada queda en blanco con el texto "Sin novedad", y en
los informes sólo se listan las áreas que el alumno sí reportó, por ejemplo
"Hardware: el teclado no responde". En la lista de sesiones, la columna "Con
fallas" dice cuántas hubo y se resalta cuando no es cero.

La lógica vive en `conexion\HorariosDatos.cs` (`ResolverCodigo`, agenda,
validaciones) y `conexion\Consultas.Sesiones.cs` (consultas por sesión); el
resultado (`VentanaCodigo`) queda en `Datos_User.VentanaActual`.

### 6. Equipo liberado y cierre de sesión a la hora de salida

Cuando el alumno guarda su bitácora, DigBit **libera el equipo** hasta la hora
de salida del código:

```
Bitácora guardada
   │
   ├─ escribe %ProgramData%\DigBit\sesion_activa.txt   (traspaso para el vigilante)
   ├─ kiosco: lanza explorer.exe y esconde sus ventanas → el alumno usa Windows
   ├─ icono en la bandeja con la cuenta atrás y "Cerrar mi sesión ahora"
   ├─ fin − 5 min: aviso "guarda tu trabajo"
   └─ fin: cierre de sesión de Windows (kiosco) / vuelta al login (escritorio)
```

**Excepción: el alumno que trajo su propia computadora.** Si marcó *equipo
propio*, el equipo del laboratorio **no se libera**: ese alumno no lo va a usar,
y dejarlo con el escritorio abierto y sin nadie delante hasta el final de la
clase era un hueco. En su lugar aparece `RegistroExitoso.cs`, que confirma el
registro y ofrece las dos únicas salidas sensatas:

| Botón | Qué hace |
|---|---|
| **Apagar el equipo** | `SesionEquipo.ApagarEquipo()`: habilita `SeShutdownPrivilege` en el token del proceso y llama a `ExitWindowsEx(EWX_POWEROFF)`. Fuera del kiosco no apaga nada, y `DIGBIT_SIMULAR_APAGADO=1` lo desactiva también dentro. |
| **Volver al inicio** | `AppContexto.CerrarSesion()`: login limpio para el siguiente alumno. |

Si nadie pulsa nada en 30 segundos vuelve sola al login. La pantalla no se
puede cerrar con Alt+F4 ni con "Finalizar tarea".

El cierre lo **garantiza** `DigBit.Vigilante`, un servicio de Windows que corre
como SYSTEM (el alumno no puede matarlo): lee el traspaso cada 10 segundos y a
la hora de salida cierra la sesión de Windows del alumno con `WTSLogoffSession`.
Si DigBit ya no está para avisar, el servicio manda el aviso él mismo. DigBit
hace lo mismo desde dentro de la sesión como respaldo.

Reglas del traspaso, iguales en los dos procesos (`Infraestructura\SesionActiva.cs`):

- Se ignora y se borra si venció hace más de 10 minutos o si es de **otro
  arranque** del equipo (los números de sesión de Windows se repiten entre
  reinicios).
- Se ignora si es de **otra sesión de Windows** distinta de la de consola.
- Una vez que el vigilante recuerda una sesión, la hora de salida **sólo puede
  acortarse**: reescribir el archivo con una hora posterior, con otro código o
  borrarlo no sirve. El vigilante olvida la sesión cuando Windows le notifica
  un cierre o inicio de sesión real, que es como llega el siguiente alumno.
- Si Shell Launcher relanza DigBit con un traspaso vigente de este mismo
  arranque y sesión, DigBit **reanuda** el estado "equipo liberado" sin pedir
  login y sin necesitar la base de datos.

Instalación del servicio, una vez por equipo, como administrador:

```powershell
.\build.cmd -Configuration Release -NoRun
.\deploy\instalar_vigilante.ps1               # instala desde DigBit.Vigilante\bin\Release y lo arranca
.\deploy\instalar_vigilante.ps1 -Desinstalar
```

Su registro queda en `%ProgramData%\DigBit\logs\vigilante-AAAAMMDD.log`.

**Probar sin base de datos y sin cerrar la sesión de verdad:**

| Qué | Cómo |
|---|---|
| Vigilante en primer plano, sólo registra lo que haría | `DigBit.Vigilante.exe --consola --simular` (`--segundos N` para que termine solo) |
| DigBit en kiosco sin cerrar la sesión de Windows | variables `DIGBIT_MODO_KIOSCO=1` y `DIGBIT_SIMULAR_LOGOFF=1` |

Los guiones `deploy\probar_vigilante.ps1` (seis escenarios: vigente, otro
arranque, alargar, otro código, archivo borrado, adelantar) y
`deploy\probar_reanudar.ps1` (DigBit reanuda, avisa, "cierra" y vuelve al
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

**Si se va la luz a media clase.** El alumno registró su bitácora, el equipo se
apagó de golpe y vuelve a arrancar dentro de la misma hora. El traspaso guardado
ya no sirve —el arranque del sistema es otro, así que `SesionActiva.Evaluar` lo
marca `DeOtroArranque`— y hasta hace poco, al volver a teclear su código, DigBit
le contestaba *"Registro duplicado"* y lo dejaba sin equipo el resto de la clase,
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
franjas, excepciones de dos semanas y la lista de alumnos, nunca contraseñas)
en `%LOCALAPPDATA%\DigBit\horario_local.xml`, refrescada al arrancar y cada
10 minutos. Si el servidor no responde y la copia tiene menos de 30 días,
DigBit sigue atendiendo alumnos con ella (diseño en `docs/fase4-sin-conexion.md`):

- El login muestra una franja naranja "SIN CONEXIÓN". Solo entran alumnos, y
  solo con su matrícula: la contraseña no se puede comprobar.
- El código se resuelve con la copia y el reloj del equipo, con las mismas
  reglas. Solo se conoce el horario de este laboratorio.
- La bitácora se guarda en `bitacoras_pendientes.xml` y se sube sola cuando
  vuelve la conexión (se prueba cada minuto). En la base queda con
  `registrado_sin_conexion = 1`; en tablas y PDF el nombre lleva
  `[sin conexion]` y la tabla del administrador tiene la columna "Sin conexion".
- La liberación del equipo y el Vigilante funcionan igual: nunca dependieron
  de la base.

Sin copia local (equipo recién instalado) o con una de otro laboratorio, la
app se comporta como antes: pantalla "Sin conexión" con reintento.

### 7. Equipos del laboratorio: DigBit como shell de Windows

En cada equipo, como administrador y con DigBit compilado en Release:

```powershell
.\build.cmd -Configuration Release -NoRun
.\deploy\configurar_equipo.ps1 -Cuenta laboratorio -Contrasena 'LaContrasena' `
    -Laboratorio 'Laboratorio de Computo 1' `
    -CadenaConexion 'Database=teschi_otru;Server=SERVIDOR;User Id=digbit;Password=xxx'
```

El guion copia DigBit y el vigilante a `C:\DigBit`, activa el modo kiosco y
escribe el laboratorio del equipo en la copia desplegada, crea la cuenta local
estándar `laboratorio` si no existe y la deja así:

- DigBit es su **shell**: al entrar no hay escritorio ni barra de tareas, sólo
  el login de DigBit. Las cuentas de administrador no se tocan: siguen entrando
  con explorer.exe.
- Sin Administrador de tareas, sin `Win+R`, sin bloquear el equipo, sin cambiar
  contraseña, sin cerrar sesión desde Inicio ni desde `Ctrl+Alt+Supr`, sin
  protector de pantalla con contraseña.
- Inicio de sesión automático con esa cuenta y sin pedir contraseña al
  despertar.
- Servicio `DigBit.Vigilante` instalado y arrancado.

Después de reiniciar, el ciclo es: encendido → DigBit → login → código →
bitácora → escritorio → a la hora de salida se cierra la sesión → DigBit otra
vez. Si DigBit termina por lo que sea, Winlogon lo relanza (`AutoRestartShell`)
y DigBit reanuda el estado en que estaba.

Para entrar como administrador en un equipo ya configurado: `Ctrl+Alt+Supr` →
**Cambiar de usuario**, o mantener `Mayús` pulsada durante el arranque para
saltar el inicio automático. `-Revertir` deshace shell, directivas, inicio
automático y servicio; `-SoloMostrar` enseña lo que haría sin cambiar nada y no
necesita administrador.

**Los otros dos equipos.** El mismo guion prepara la máquina del administrador y
la de un profesor: es la misma aplicación, sólo que sin bloquear la pantalla.

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

El orden importa: **el administrador va primero**, porque los laboratorios se dan
de alta desde ahí y el instalador de las máquinas comprueba contra la base que el
nombre que le pasas existe de verdad.

Tres cosas que el guion endurece porque Windows no las deja bien por defecto, y
que el diagnóstico comprueba una a una:

- **`C:\DigBit` en sólo lectura para la cuenta del laboratorio.** Una carpeta
  creada en la raíz de `C:` hereda `Authenticated Users:(M)`, así que por
  defecto el alumno puede sustituir `DigBit.exe` por un `cmd.exe` y el siguiente
  inicio automático le da un símbolo del sistema. Y encima esa carpeta está
  excluida del antivirus.
- **La clave `Shell` protegida dentro de la propia colmena del alumno.** Vive en
  `HKCU`, que hereda control total, así que podía ponerla en `explorer.exe` y
  entrar al escritorio sin pasar por DigBit — sin reiniciar siquiera, porque el
  relevo entre alumnos es un cierre de sesión. Se le aplica el mismo patrón que
  Windows ya usa para las directivas: herencia rota y sólo lectura.
- **`ForceAutoLogon=1`.** `AutoAdminLogon` sólo actúa al **arrancar**. Cuando el
  vigilante cierra la sesión al terminar la clase no hay arranque, así que sin
  esto el equipo se quedaría pidiendo una contraseña que el siguiente alumno no
  tiene. Rompe el escritorio remoto, cosa que en estos equipos no se usa.

Las cinco directivas de la cuenta (`DisableTaskMgr`, `NoRun`, …) **no** hace
falta protegerlas: Windows ya deja `HKCU\...\Policies` con la herencia rota y en
sólo lectura para el usuario.

La contraseña del inicio automático queda en el registro en texto claro
(`Winlogon\DefaultPassword`), como en cualquier inicio automático de Windows:
usa una contraseña exclusiva de esa cuenta.

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
cero y lleve así un tiempo razonable (piensa en el profesor que no entra en
todo el semestre), se puede borrar el camino de MD5 de `Contrasenas.cs`.
Mientras quede alguno, borrarlo lo dejaría fuera.

### 9. Deep Freeze en los equipos del laboratorio

Los equipos del TESCHI tienen **Deep Freeze**: el disco del sistema vuelve a su
estado congelado en cada reinicio, y la imagen se rehace cada semestre. Eso
tiene una cara buena y una mala.

La buena es que el kiosco se vuelve prácticamente inmune: lo que un alumno
rompa, lo que alguien borre, o lo que Defender ponga en cuarentena, vuelve a su
sitio al reiniciar.

La mala es que Deep Freeze tampoco distingue las **bitácoras que todavía no
llegaron al servidor**, que hasta ahora vivían en el perfil del usuario. Por eso
DigBit y el vigilante leen un ajuste `CarpetaDatos` que apunta fuera del disco
congelado, normalmente un ThawSpace:

```powershell
.\deploy\configurar_equipo.ps1 -Cuenta laboratorio -Contrasena LaContrasena `
    -Laboratorio "Laboratorio de Computo 1" `
    -CadenaConexion "Database=teschi_otru;Server=SERVIDOR;User Id=digbit;Password=xxx" `
    -CarpetaDatos T:\DigBitDatos
```

Ahí van la cola de bitácoras, la copia local del horario y los registros. Si el
equipo tiene Deep Freeze y no se indica `-CarpetaDatos`, el guion **se detiene**
en vez de avisar, porque lo que se pierde son datos de alumnos.

El orden del despliegue importa: **descongelar, reiniciar, configurar, probar,
congelar**. Configurar un equipo congelado termina en verde y no deja nada.
`configurar_equipo.ps1` se niega a seguir si detecta el disco congelado, y
`diagnostico_equipo.ps1` lo dice en su sección 0.

El procedimiento completo, el reinicio de cada semestre y cómo comprobar que un
disco conserva lo que se escribe, en `docs/fase8-deepfreeze.md`.

### 10. Dos usuarios de MySQL, no uno

Cada equipo del laboratorio lleva su `connections.config`, y **el alumno puede
leerlo**: DigBit corre con su propia sesión de Windows, así que todo lo que la
aplicación necesita para conectarse, él también lo ve. Ocultar la carpeta no
sirve — oculto es un atributo, no un permiso — y dejarla sin permiso de lectura
tampoco, porque entonces DigBit tampoco podría leerla.

El ataque realista, entonces, no es inyectar SQL. Se revisaron las 165
sentencias de la aplicación una por una: todas usan parámetros, no hay ninguna
construida pegando texto del usuario. El ataque es más simple: leer las
credenciales y conectarse **directo** a la base desde cualquier cliente, sin
pasar por DigBit. Contra eso lo único que sirve es que ese usuario no pueda gran
cosa.

```sql
-- edita las dos contraseñas y el rango de red, y ejecuta:
mysql -u root -p < db/07_usuarios_minimos.sql
```

| | `digbit_equipo` (los ~30 equipos) | `digbit_admin` (sólo el ADM) |
|---|---|---|
| Catálogos, horario, clases | leer | leer y escribir |
| Sesiones (`codigos_accesos`) | leer, crear la del día | todo |
| Bitácoras | leer, añadir | todo |
| `usuarios` | leer; actualizar **sólo** `password`, nombre, apellidos y correo | todo |
| Cambiar el esquema, otras bases | no | no |

El reparto no se hizo a ojo: se inventariaron las 165 consultas rastreando **quién
llama a cada método**, para saber si la dispara el alumno, el profesor o el
administrador. Un permiso de menos rompe una clase entera; uno de más anula el
ejercicio.

Dos detalles que importan más de lo que parecen:

**El permiso sobre `usuarios` es por columna.** Con un `UPDATE` a secas,
cualquiera con esas credenciales se pondría `fk_tipo_usuario = 3` y sería
administrador. Como esa columna no está en la lista, MySQL lo rechaza con el
error 1143.

**El equipo no puede INSERTAR en `usuarios`.** Eso sólo hacía falta para que un
alumno se diera de alta él mismo desde el kiosco, y con ese permiso podría
crearse una cuenta de tipo 3 desde cualquier cliente de MySQL. En modo kiosco el
botón **REGÍSTRATE** ya no se muestra (`Login.cs`); a los alumnos los da de alta
la escuela.

Comprobado ejecutándolo: 19 de 19 pruebas: todo lo que el alumno y el profesor
necesitan funciona, y las once vías de escalada quedan bloqueadas — hacerse
administrador, crearse un usuario, borrar bitácoras, inventarse una franja de
horario, tocar el esquema, leer otra base. Y la suite de horarios entera (25/25)
pasa con `digbit_admin`.

Lo que **no** cubre: `digbit_equipo` puede leer la tabla `usuarios` completa,
contraseñas incluidas, porque le hace falta para validar el login. Lo que lo
hace soportable es que desde la sección 8 son PBKDF2 con 100 000 iteraciones.
Con MD5 habría sido un desastre.

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
`Threading.Tasks`, así que **el alta de alumnos reventaba** con
`FileNotFoundException` (es la única operación que usa transacción). Si añades
un paquete nuevo, comprueba que su DLL aparece en `bin\`.

## Estructura del repositorio

```
DigBit.sln                     DigBit + DigBit.Vigilante
build.cmd / build.ps1          Compilación y arranque con un comando
db/
  01_schema.sql                Esquema de las 8 tablas (reconstruido desde el código)
  02_seed.sql                  Datos de prueba
  03_migracion_fase2.sql       Para una base que ya existía: una bitácora por alumno y código
  04_migracion_horarios.sql    Para una base que ya existía: administrador real, horarios, sesiones
  05_migracion_sin_conexion.sql   Para una base que ya existía: marca de bitácoras registradas sin servidor
  06_migracion_contrasenas.sql    Para una base que ya existía: ensancha password para PBKDF2 y mide la migración
  00_revision_previa.sql       Antes de migrar: dice qué va a fallar, sin cambiar nada
  07_usuarios_minimos.sql      Los dos usuarios de MySQL con permisos mínimos (equipos y administrador)
  08_migracion_bajas.sql       Para una base que ya existía: dar de baja usuarios y desactivar laboratorios sin borrar su historial
  09_codigo_por_profesor.sql   Para una base que ya existía: un código por profesor para todas sus clases
deploy/
  instalar_vigilante.ps1       Instala/quita el servicio DigBit.Vigilante (como administrador)
  probar_vigilante.ps1         Escenarios del vigilante en simulación, sin base de datos
  probar_reanudar.ps1          DigBit reanuda un equipo liberado, sin base de datos
  configurar_equipo.ps1        Deja un equipo del laboratorio con DigBit como shell (fase 5)
  diagnostico_equipo.ps1       Dice en qué estado quedó un equipo; no cambia nada
  migrar_bd.ps1                Respalda, revisa y aplica las migraciones; se detiene si algo bloquea
  probar_vigilante_limites.ps1 El vigilante contra entradas torcidas: 12 casos que se comprueban solos
  deepfreeze.ps1               Detecta Deep Freeze y si el disco está congelado (fase 8)
  actualizar_digbit.ps1        Sustituye la copia desplegada conservando la configuración
  permitir_en_defender.ps1     Excluye DigBit y recupera lo que Defender puso en cuarentena
  abrir_bd_en_red.ps1          Abre el MySQL de pruebas al resto de la red
  probar_horarios.ps1          Escenarios de horarios y códigos, sin base de datos
docs/
  fase3-diseno.md              Diseño de la liberación del equipo y el cierre de sesión
  fase6-horarios.md            Diseño de los horarios por laboratorio (agenda del administrador)
  fase4-sin-conexion.md        Diseño del modo sin conexión (copia local y cola de bitácoras)
  fase7-contrasenas.md         Diseño del paso de MD5 a PBKDF2 y de la migración perezosa
  fase8-deepfreeze.md          Deep Freeze, la carpeta de datos persistente y el reinicio semestral
DigBit/
  Program.cs                   Punto de entrada: instancia única, excepciones, AppContexto
  App.config                   Enlaces de ensamblado y ModoKiosco; conexión delegada a connections.config
  connections.config.example   Plantilla versionada de la cadena de conexión
  Infraestructura/
    AppContexto.cs             Ciclo de vida: reanudar, comprobar entorno, login, cerrar sesión, salir
    Kiosco.cs                  Modo kiosco (pantalla completa, sin Alt+F4)
    CarpetaDatos.cs            Carpeta que sobrevive a Deep Freeze (compartida con el vigilante)
    LaboratorioEquipo.cs       Laboratorio en el que está este equipo (appSettings / variable)
    SinConexion.cs             Estado del modo sin conexión: activar con la copia, sincronizar al volver
    Log.cs                     Registro de diagnóstico (compartido con el vigilante)
    SesionActiva.cs            Archivo de traspaso DigBit ↔ vigilante (compartido)
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
    AlumnosDatos.cs, ProfesoresDatos.cs, LaboratoriosDatos.cs   Lo que leen y escriben las secciones del administrador
    UsuariosDatos.cs           Lo común a alumnos y profesores: alta, edición, contraseña, baja
    CatalogosDatos.cs          Carreras, semestres y grupos para los diálogos
    Esquema.cs                 Qué migraciones tiene la base (08, 09), para no fallar si faltan
    ReglasCodigo.cs            Qué clase abre el código de un profesor en este laboratorio a esta hora
    Codigo_Gemerador.cs        Generador de códigos aleatorios
    Datos_User.cs              Sesión en memoria (usuario, código y ventana actuales)
    VentanaCodigo.cs           Inicio, fin y estado de un código
    Usuario.cs, OpcionCombo.cs DTOs
  RJControls/                  Controles personalizados (botones, cajas de texto, combos)
  AvisoSesion.cs, PantallaError.cs   Ventanas de aviso de fin de sesión y de error
  RegistroExitoso.cs           Equipo propio: registro guardado, apagar o volver al login
  PrincipalAdministrador.cs    Administrador: las cinco secciones y el panel de laboratorios en vivo
  PanelLaboratoriosAhora.cs    El panel: hora anterior, actual y siguiente de cada laboratorio
  PantallaAdmin.cs             Base de las listas del administrador y sus diálogos
  AdministrarAlumnos.cs, AdministrarProfesores.cs, AdministrarLaboratorios.cs   Secciones del administrador
  GestionBitacorasAdministrador.cs   Bitácoras: sesiones con filtros, quién registró y PDF (también "Mis bitácoras")
  AdministrarHorarios.cs       Administrador: clases, horario por laboratorio, excepciones
  Profesor_Principal.cs, TarjetasProfesor.cs   Profesor: su código, su clase de ahora y sus secciones
  BitacoraMisClases.cs         Profesor: "Mi horario"; al pulsar una clase abre su bitácora
  BitacoraSesion.cs            Quién registró en una clase: tabla en vivo, cuenta atrás y PDF
  TablaAlumnosPdf.cs           La tabla de alumnos de los PDF de bitácoras
  Alumno_Principal.cs          Alumno: el código y cómo está el equipo, en dos pasos
  *.cs / *.Designer.cs / *.resx   Formularios WinForms
  img/, Resources/             Imágenes y plantilla HTML del PDF
DigBit.Vigilante/              Servicio de Windows (LocalSystem): cierra la sesión a la hora de salida
  Program.cs                   Servicio, o --consola [--simular] [--segundos N] para probar
  Vigilante.cs                 El bucle: lee el traspaso cada 10 s, avisa, cierra la sesión
  VigilanteService.cs          Envoltorio ServiceBase; recibe los cambios de sesión de Windows
```

Tablas de la base de datos:

| Tabla | Contenido |
|---|---|
| `usuarios` | Alumnos y profesores, con la contraseña en PBKDF2 (ver sección 8) |
| `carrera_grupo_semestre` | Carrera, grupo y semestre de cada alumno |
| `carreras`, `grupos`, `semestres`, `materias`, `laboratorios` | Catálogos |
| `clases`, `horarios`, `horario_excepciones` | Cada clase (profesor + grupo + materia), sus franjas por laboratorio y los cambios puntuales |
| `codigos_accesos` | Una fila por sesión (una clase en una fecha) con el código que se usó, que es el de su profesor (`usuarios.codigo_clase`) |
| `registros_bitacoras` | Bitácora que llena cada alumno con un código |

## Estado del proyecto y trabajo pendiente

Este repositorio está en proceso de saneamiento. Lo que ya está resuelto y lo
que falta, en orden de prioridad:

- [x] Compilación reproducible desde un clon limpio (`nuget restore` en el script).
- [x] Esquema de base de datos versionado.
- [x] Arranque robusto: instancia única, excepciones no controladas a log y
      pantalla propia, comprobación de conexión al arrancar, modo kiosco opcional,
      "Salir" = cerrar sesión.
- [x] Sustituir el administrador escrito en duro por un usuario real
      (`fk_tipo_usuario = 3`; `ADMIN001` en el seed, migración 04 en la base real).
- [x] Contraseñas con PBKDF2-SHA256 y sal por usuario en vez de MD5 a secas
      (`conexion\Contrasenas.cs`). Los hashes viejos siguen sirviendo y cada uno
      se reemplaza solo la primera vez que esa persona entra bien: nadie tiene
      que cambiar su contraseña. Falta borrar el camino de MD5 cuando la
      consulta de `db\06_migracion_contrasenas.sql` diga que ya no queda
      ninguno.
- [x] Alta de profesores sólo para el administrador. El botón de registro del
      login lleva únicamente al alta de alumnos; el alta de profesores se abre
      desde el panel del administrador y se cierra sola, dejando aviso en el
      log, si la abre cualquier otro (`Datos_User.TipoUsuario`). Se quitó el
      código de navegación entre ambas pantallas, que estaba sin cablear desde
      el commit inicial.
- [x] Códigos con ventana de validez real (reloj del servidor) y una sola
      bitácora por alumno y código.
- [x] Liberar el equipo al guardar la bitácora y cerrar la sesión de Windows a
      la hora de salida (DigBit + servicio `DigBit.Vigilante`). Probado en
      simulación; falta probarlo en un equipo del laboratorio con el servicio
      instalado.
- [x] Guardar carrera/grupo al registrar un alumno.
- [x] Horarios por laboratorio (fase 6): el administrador arma la agenda de
      cada laboratorio (maestro, grupo, materia, código fijo) con excepciones
      por día; el profesor deja de generar códigos; cada equipo conoce su
      laboratorio; fila naranja para la bitácora que reporta una falla. Capa de
      datos probada contra un MySQL local; las pantallas compilan y falta
      probarlas con datos reales en el TESCHI.
- [x] Modo sin conexión (fase 4): copia local del horario, alumnos entran con
      su matrícula, bitácoras en cola que se suben solas, marca `[sin conexion]`
      en los informes. Probado apagando y encendiendo un MySQL local.
- [ ] Modo kiosco completo (fase 5): `deploy\configurar_equipo.ps1` deja DigBit
      como shell de la cuenta de laboratorio, con directivas de bloqueo, inicio
      de sesión automático y el servicio instalado. El guion está escrito y
      validado en modo `-SoloMostrar`; falta ejecutarlo en los equipos del
      laboratorio.

Hay archivos `.cs` en el árbol que **no forman parte de la compilación**
(no están en `DigBit.csproj`) y están pendientes de borrar: `conexion.cs`,
`cone/Conexion.cs`, `Agregar_Laboratorio.*`, `Eliminar_Laboratorio.*`,
`RegistroProfes.*` y `RJControls/textBox.cs`. Dos de ellos contienen
credenciales de desarrollo antiguas; no los uses como referencia.
