# Fase 4 — Modo sin conexión

## Objetivo

Que un equipo del laboratorio siga atendiendo alumnos cuando no alcanza el
servidor MySQL (red caída, servidor apagado), con la última copia del horario
que conoció, y que nada se pierda: las bitácoras registradas mientras tanto se
suben solas cuando vuelve la conexión.

```
Con servidor                      Sin servidor
────────────────────────────      ─────────────────────────────────────────────
arranque → prueba de conexión     arranque → sin conexión → ¿hay copia local
  → laboratorio del equipo          del horario de ESTE laboratorio y tiene
  → sube la cola pendiente          menos de 30 días? → sí: login con franja
  → refresca la copia local         naranja "SIN CONEXIÓN"; no: pantalla de
  → login normal                    error con reintento (como antes)

login: usuario + contraseña       login: solo alumnos, solo matrícula (contra la
  contra la base                    copia local; no hay contraseñas en el equipo)

código → HorariosDatos            código → CacheHorario, mismas reglas, con el
  (reloj del servidor)              reloj del equipo + desfase conocido

bitácora → INSERT                 bitácora → cola local (XML); "una por alumno y
                                    sesión" se comprueba contra la cola

equipo liberado / vigilante       igual (nunca dependieron de la base)

cada minuto: si estaba sin        cada minuto: prueba el servidor; al volver,
  conexión, nada; cada 10 min       refresca la copia y sale del modo sin
  refresca la copia                 conexión

siempre, en segundo plano: EnviadorBitacoras sube la cola en cuanto puede
  (reintentos a 15 s, 30 s, 1, 2 y 5 min, y al volver la red de Windows)
```

## Piezas

### `conexion\CacheHorario.cs` — copia local del horario

`%LOCALAPPDATA%\DigLab\horario_local.xml`. Contiene, para el laboratorio del
equipo: las clases activas (código, vigencia, profesor, grupo, materia), las
franjas del laboratorio, las excepciones de ayer a 14 días vista (canceladas
de franjas del laboratorio y extras en él), la lista de alumnos (solo id y
matrícula, sin el nombre desde el 2026-10-14: la carpeta de datos la puede
leer la cuenta del laboratorio; una copia anterior con nombres se sigue leyendo
y los pierde en cuanto se refresca con servidor; sin conexión la pantalla del
alumno muestra su matrícula) y los equipos que se marcan en el paso 3 de la
bitácora (`<equipos>`: los que registraron bitácoras en el laboratorio en los
últimos 30 días; una copia de antes no lo trae y entonces el alumno escribe el
número). **Nunca contraseñas.** Guarda también `generado`
(NOW() del servidor) y el desfase servidor − equipo en ese momento, que se
aplica al reloj local mientras no hay servidor.

`Actualizar(idLab)` la lee de la base y la escribe; `Cargar()` la lee del
disco; `ResolverCodigo(codigo, matricula, out SesionLocal)` aplica las mismas
reglas que `HorariosDatos.ResolverCodigo`, con dos diferencias: sólo conoce
este laboratorio (un código de otro laboratorio responde "hoy no hay clase con
ese código en este laboratorio") y devuelve una `VentanaCodigo` con
`IdCodigo = 0` más una `SesionLocal` con lo necesario para encolar.

Sin códigos y sin clase (uso libre, db/14), `ResolverUsoLibre(matricula, out
SesionLocal)` calcula el tramo sin clase con la copia (`ReglasCodigo.TramoLibreEn`)
y da una `SesionLocal` sin franja, extra ni profesor, cuya clave es el
laboratorio, el inicio del tramo y la fecha: una bitácora por alumno y tramo.
La copia guarda si el laboratorio admite uso libre (atributo `usoLibre`; una
copia de antes no lo trae y entonces no se ofrece).

### `conexion\ColaBitacoras.cs` — bitácoras pendientes

`bitacoras_pendientes.xml`, en la carpeta de datos (la que sobrevive a Deep
Freeze) o en `%LOCALAPPDATA%\DigLab`. `Encolar` guarda la bitácora con su
`SesionLocal`; `YaRegistrada(clave, matrícula)` sostiene la regla de una por
alumno y sesión mientras no hay servidor; `Sincronizar()` recorre la cola con
el servidor a la vista: busca al alumno por matrícula, usa la sesión que ya
conocía o la obtiene o crea (por franja y fecha, o por excepción; si la franja
ya no existe, la crea sin enlace, como historial) e inserta la bitácora con
`INSERT ... SELECT ... WHERE NOT EXISTS` (además del UNIQUE (sesión, alumno) de
la 03, que una base con duplicados viejos no tiene): si ya estaba, la descarta
como duplicada. Una de uso libre va a la sesión del tramo en que cae su hora
**según el horario de ahora en el servidor** (`HorariosDatos.TramoLibreEn`), no
del que vio el equipo: si mientras no había red se puso una clase en medio, no
termina en la sesión del tramo de antes (donde se habría descartado como
repetida). Sin la 14 no se puede guardar y se aparta como imposible. Lleva
`registrado_en` = hora del servidor estimada al registrar, `recibido_en` =
cuándo llegó (db/10) y `registrado_sin_conexion = 1`, salvo que el alumno
hubiera entrado con contraseña y solo se cayera la red al guardar.

Los errores se separan en tres (`ColaBitacoras.Clasificar`):

- **Sin conexión** (red, servidor caído, conexión perdida, credenciales): corta
  la pasada y el enviador espera.
- **Del servidor** (falta una migración, sin permisos, solo lectura, disco
  lleno, bloqueos) **o desconocido**: esa bitácora se salta sin anotarle nada y
  se reintenta en la siguiente pasada. Así un servidor en solo lectura no acaba
  apartando todas las bitácoras del laboratorio.
- **De esa bitácora** (la matrícula ya no existe, un dato que la base rechaza:
  1048, 1264, 1265, 1292, 1366, 1406, 1452): se le anota y, al quinto, se aparta
  a `bitacoras_rechazadas.xml` para que no frene a las demás.

La franja del login dice cuántas hay por enviar y cuántas se apartaron. El
archivo se escribe con `Flush(true)` antes de sustituir el anterior, que queda
como `.anterior`: si un corte de luz deja el nuevo roto, se rescata el anterior.
Si el archivo está ocupado un momento (antivirus, copia de seguridad), se
reintenta después en vez de darlo por vacío y machacarlo.

### `Infraestructura\EnviadorBitacoras.cs` — el envío en segundo plano

Un hilo propio, desde que arranca DigLab hasta que se cierra, también con la
conexión bien: una bitácora puede quedar en la cola porque la red se cayó justo
al guardar. Tras un fallo espera 15 s, 30 s, 1, 2 y 5 minutos (con ±20 % al azar,
para que 30 equipos no reintenten a la vez); con la cola vacía, mira cada 5
minutos. `Encolar` y los avisos de red de Windows (`NetworkChange`) lo
despiertan. Las conexiones de envío tienen 15 s de límite por orden, y tras un
fallo global se vacía el pool de conexiones para no reutilizar una muerta.

Antes de apagar desde DigLab (`AvisoApagado`), si quedan bitácoras se intenta
enviarlas 10 s; si no se puede, se avisa: con la carpeta de datos están a salvo
y se enviarán al encender, sin ella (Deep Freeze) se perderían.

### `Infraestructura\SinConexion.cs` — el estado

`IntentarActivar(motivo)` carga la copia y comprueba que sea del laboratorio
configurado en el equipo y que tenga menos de 30 días; si no, deja el motivo
en el log y devuelve false (la app se comporta como antes: pantalla de error).
`AlConectar(idLab)` despierta al enviador y refresca la copia, sin lanzar
nunca. `TextoAviso()` es la franja naranja del login.

### Dónde se engancha

- `AppContexto.Iniciar`: si la prueba de conexión falla, intenta activar el
  modo; si entra, muestra el login. En cualquier caso arranca la vigilancia de
  conexión (un `Timer` de un minuto).
- `Login`: sin conexión, valida la matrícula contra la copia y abre la pantalla
  del alumno sin comprobar contraseña. Profesores y administradores no entran.
  Si el servidor se cae con el login abierto, el siguiente intento de entrar lo
  detecta y cambia de modo.
- `Buscar_Codigo_Alumno`: resuelve con la copia; si el servidor se cae justo
  al validar, cambia de modo y resuelve con la copia.
- `Alumno_Principal` / `Consultas.GuardarBitacora`: con `IdCodigo = 0` y una
  `SesionLocal` en `Datos_User`, la bitácora va a la cola. Si la red se cae
  justo al guardar en línea, también: el alumno ya se validó con contraseña,
  así que no se marca como sin conexión. El equipo se libera igual (la ventana
  ya viene en reloj local).
- `registros_bitacoras`: columnas `registrado_sin_conexion` y `registrado_en`
  (`db/05_migracion_sin_conexion.sql`). En tablas y PDF el nombre del alumno
  lleva el sufijo `[sin conexion]`; la tabla del administrador tiene la columna
  "Sin conexion".

## Decisiones

- **Sin contraseñas en el equipo.** Copiar los hashes a cada PC los expondría
  a cualquiera con acceso a la carpeta. Sin servidor el alumno entra con su
  matrícula; a cambio, esa bitácora queda marcada para siempre como registrada
  sin conexión y sin identidad verificada. Es el mismo criterio que la fila
  naranja: no bloquear, dejar rastro.
- **Solo alumnos.** Profesores y administradores necesitan la base para todo lo
  que hacen; sin servidor su login se rechaza con un mensaje claro.
- **Copia de hasta 30 días.** Más vieja, el horario puede haber cambiado
  demasiado; mejor la pantalla de error que aceptar códigos caducados.
- **Cambios hechos mientras no había red** (una cancelación, una extra) no se
  conocen hasta que vuelve el servidor. Es inherente al modo.
- **Reloj.** Sin servidor manda el reloj del equipo corregido con el último
  desfase conocido. Si alguien cambió la hora del equipo durante el corte, las
  franjas se evalúan mal; el Vigilante ya tiene el mismo límite.
- **Generación de códigos, sin servidor** (db/11, versión 1.1). El código de
  cada clase sale de un secreto que está en la base, y el equipo sin red no
  puede comprobarlo (ni debe guardar el secreto en disco, donde lo leería un
  alumno). En vez de bloquear la clase entera, el alumno **elige su clase de la
  lista** (las que hay en ese momento en su laboratorio), como en el modo sin
  códigos, y la bitácora queda marcada sin conexión. La copia guarda el modo
  del laboratorio (`modoCodigo`); una copia de antes de la 1.1 cuenta como
  código único. Al volver el servidor, la cola usa la sesión que abrió el
  profesor o, si no la abrió, crea una con el código generado de ese día (sin
  secreto, uno al azar).

## Pruebas

Con el MySQL portátil: se generó la copia, se apagó el servidor, se resolvieron
códigos y se encolaron bitácoras con la copia, DigLab arrancó hasta el login en
modo sin conexión, se volvió a encender el servidor, la cola subió, las
bitácoras quedaron marcadas y una repetida se descartó como duplicada. Los
guiones están en el scratchpad de la sesión (`probar_sin_conexion_1.ps1` y
`_2.ps1`); requieren apagar y encender el servidor, por eso no van en `deploy\`.

El 2026-10-04, con el enviador en segundo plano, contra MySQL 8 y contra una
copia de la base de la escuela en MariaDB 5.5.68 (cargando DigLab.exe desde un
programa de pruebas, sin pulsar botones): guardar en línea con aviso de equipos
sin internet; red caída justo al guardar, simulada con un puerto cerrado (se
encola con su sesión y llega con `registrado_sin_conexion = 0`); una cola escrita
por la versión anterior; una bitácora imposible apartada al quinto intento; el
enviador con el servidor "caído" (otra vez el puerto cerrado) y subiendo en
cuanto vuelve; y `AvisoApagado.PuedeApagar` con y sin servidor, con la carpeta de
datos persistente.

Ese mismo día, DigLab.exe arrancando de verdad con una bitácora pendiente: el
enviador la subió en menos de un segundo, pero el hilo de la pantalla se quedó
colgado en "Probando conexion". Era un interbloqueo de MySql.Data 8.2: `Open()`
espera el semáforo global de los pools capturando el contexto de WinForms, y si
otro hilo lo tiene, la continuación va a un hilo de pantalla que está bloqueado.
Reproducido con una prueba de estrés (scratchpad/estres): con el contexto se
colgaba en 2 s, tres de tres veces; sin él (`Conexion.AbrirSinContexto`), más de
1 600 aperturas en 30 s sin fallo. Falta repetir el arranque real con cola
después de la corrección, el modo sin conexión de verdad (con copia local y el
servidor parado de verdad) y "Apagar" sin carpeta de datos persistente.
