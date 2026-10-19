# Instalar DigBit

Esta guía deja funcionando el montaje completo: **un equipo que hace de servidor
y de equipo de la administración**, y las **máquinas de laboratorio** que se
conectan a él.

Es el mismo programa en los tres sitios. Lo único que cambia es si se bloquea la
pantalla y con qué usuario de base de datos se conecta.

| | Pantalla bloqueada | Usuario de MySQL | Etiquetas |
|---|---|---|---|
| Equipo de la administración | no | `digbit_admin` | ninguna |
| Equipo docente | no | `digbit_equipo` | ninguna |
| Máquina de laboratorio | **sí** | `digbit_equipo` | laboratorio + número |

Las personas **responsables de laboratorio** (el panel de administración en
solo consulta) no tienen equipo propio ni hay que configurar nada para ellas en
ningún equipo: entran con su cuenta en cualquiera de estos (con DigBit 1.1), y
DigBit pasa su sesión a la cuenta de MySQL de solo lectura `digbit_consulta`.
Basta aplicar una vez `db\18_encargados.sql` **con root** en el servidor (ver
«Migrar una base que ya existe»).

## Con la base de la escuela: `DigBit-Instalador.exe`, un solo archivo

Cuando la base ya existe en un servidor (la del TESCHI está en `sql.teschi.edu.mx`)
no hace falta el paquete entero: basta **un solo `.exe`** que lleva dentro
DigBit, el vigilante y los guiones de instalación.

**Generarlo** (en el equipo de desarrollo, con DigBit cerrado):

```powershell
.\crear_instalador.ps1
```

Deja `DigBit-Instalador.exe` junto a la carpeta del proyecto (unos 6 MB). La
conexión (servidor, base, usuario y puerto) la toma del `connections.config` de
`DigBit\bin\Release`, o de `-Servidor`, `-Base` y `-Usuario`. **La contraseña de la
base no va dentro**: se teclea al instalar cada equipo, así el `.exe` puede
circular sin exponer la base.

**Usarlo**: doble clic en el equipo, aceptar el permiso de administrador y elegir:

| Tipo | Qué queda |
|---|---|
| Administrador | DigBit en `C:\DigBit` como programa normal, con acceso directo en el escritorio |
| Maestro | Igual que Administrador |
| Alumno (máquina del laboratorio) | Modo kiosco: la cuenta del laboratorio entra sola y DigBit es su pantalla, con el vigilante |

Son los nombres que usa el instalador (todavía no se ha rehecho): «Administrador»
es el equipo de la administración, «Maestro» el de un docente y «Alumno» una
máquina del laboratorio.

Luego la contraseña de la base y, solo para Alumno: el laboratorio (sale de la
base; si hay más de uno, hay que elegirlo), el número de la máquina, la cuenta
del laboratorio y su contraseña, y la carpeta de datos si tiene Deep Freeze.
Antes de tocar nada prueba la contraseña contra la base: si la rechaza (distingue
mayúsculas), avisa y no instala. Al terminar ofrece abrir DigBit (Administrador y
Maestro) o reiniciar (Alumno).

Por debajo ejecuta `deploy\configurar_equipo.ps1`, igual que el instalador del
paquete, y lo extraído se borra al cerrar la ventana. Lo de Deep Freeze sigue
siendo tuyo: descongelar antes y volver a congelar después de comprobarlo.

## Qué trae este paquete

```
servidor.config     A que equipo apunta esta copia (lo escribe el instalador)
mysql/              MySQL portátil (no hace falta instalarlo aparte)
deploy\             Los guiones de instalación y diagnóstico (preparar_bd.ps1
                    deja MySQL listo con el esquema y datos de prueba)
db\                 Esquema, datos de prueba y migraciones
DigBit\bin\Release  La aplicación
DigBit.Vigilante\bin\Release  El servicio que cierra la sesión a la hora
```

## La forma facil: el instalador

En la raiz del paquete hay un **`DigBit.Instalador.exe`**. Haz doble clic: pide
permiso de administrador y eliges arriba que clase de equipo estas preparando.
Es la misma ventana para los cuatro casos; segun lo que marques se habilitan
unas casillas u otras.

Por debajo ejecuta exactamente los mismos guiones que se describen mas abajo,
asi que lo que hace es identico. La ventaja es que no hay que teclear una linea
larga sin equivocarse, y que el nombre del laboratorio no se puede escribir mal
porque no se escribe: sale de una lista que lee de la base.

Lo que el instalador **no** hace, y sigue siendo tuyo: descongelar Deep Freeze
antes y volver a congelar despues.

### La direccion del servidor no se teclea

La casilla **Servidor** ya no hay que rellenarla. El paquete se acuerda de a que
equipo apunta, y lo hace en este orden:

1. la variable de entorno `DIGBIT_SERVIDOR`, si la pones (para pruebas);
2. **si la base esta en este mismo equipo**, `127.0.0.1`;
3. el archivo **`servidor.config`** de la raiz del paquete, que el paso 1 deja
   escrito con el **nombre de red** del equipo servidor.

Por eso el orden de los pasos importa mas que antes: prepara el servidor y
**copia ESE MISMO paquete** (la carpeta entera, con su `servidor.config`) a la
memoria con la que vas a las demas maquinas. Alli la casilla sale rellena sola.

Se anota el nombre de la maquina, no su IP, para que siga valiendo cuando el
DHCP le cambie la direccion. Si tu red no resuelve nombres, escribe la IP una
sola vez en la casilla: al instalar queda anotada en `servidor.config` y el
siguiente equipo ya la trae puesta.

La casilla se puede seguir editando. Si el servidor se muda de equipo, cambia la
ultima linea de `servidor.config` y no hace falta recompilar ni tocar nada mas.

### Paso 1 — "Servidor de base de datos"

**Va primero y se hace una sola vez en todo el despliegue**, en el equipo que
va a guardar los datos. Sin esto no hay base a la que conectar nada.

Se habilitan solo dos casillas, y las dos son contrasenas **que tu inventas
ahora**. No son contrasenas que haya que adivinar: se crean en este momento.

| Casilla | Que crea |
|---|---|
| Para los equipos (`digbit_equipo`) | el usuario de MySQL que llevaran las maquinas de laboratorio y los equipos docentes |
| Para el administrador (`digbit_admin`) | el usuario de MySQL del equipo de la administracion, que gestiona horarios |

**Apunta las dos antes de pulsar Instalar.** Las vas a necesitar en cada equipo
que configures despues, y no hay forma de recuperarlas: se guardan cifradas
dentro de MySQL.

Si ya hay una base montada te avisa y te deja cancelar. Continuar la borra
entera.

Al terminar escribe `servidor.config` con el nombre de este equipo y te lo dice
en la ventana. Eso es lo que hara que los pasos 2, 3 y 4 no pregunten ninguna IP.
Recuerda que la base sigue escuchando **solo en local** hasta que ejecutes
`deploy\abrir_bd_en_red.ps1`; sin eso las maquinas del laboratorio no llegan.

### Paso 2 — "Administrador"

En el mismo equipo, o en el de la administracion. Al marcarlo, el usuario cambia
solo a `digbit_admin`; escribe en **Contrasena** la que acabas de crear para el.

| Casilla | Que poner |
|---|---|
| Servidor | **nada**: ya viene relleno |
| Base | `teschi_otru` |
| Usuario | `digbit_admin` |
| Contrasena | la del paso 1 |

Lo unico que se escribe aqui es la contrasena. Pulsa **Instalar**.

En estos dos equipos, el de la administracion y el de cada docente, **no se instala
nada en el sentido incomodo de la palabra**: no hay kiosco, ni cuenta nueva, ni
servicio, ni inicio de sesion automatico. Son tres cosas y ya esta:

1. la copia de DigBit en `C:\DigBit`,
2. el `connections.config` apuntando al servidor,
3. un **acceso directo "DigBit" en el escritorio**.

Doble clic y entras con `ADMIN001` / `admin123`. La aplicacion es la misma que
la de las maquinas de laboratorio; lo unico que cambia es que aqui no bloquea
la pantalla. Todo el aparato del kiosco es exclusivo de la Parte B.

**Da de alta los laboratorios aqui, antes de tocar ninguna maquina**: el
instalador de las maquinas comprueba contra la base que existan.

### Paso 3 — "Alumno (máquina del laboratorio)"

Una vez por equipo. Descongelado si tiene Deep Freeze.

| Casilla | Que poner |
|---|---|
| Servidor | **nada**: lo trae el paquete del paso 1 |
| Usuario | `digbit_equipo` (cambia solo) |
| Contrasena | la de `digbit_equipo` del paso 1 |
| Laboratorio | pulsa **Cargar** y escogelo de la lista |
| Numero de maquina | el numero rotulado en el equipo |
| Cuenta del laboratorio | `laboratorio` sirve |
| Contrasena de esa cuenta | **la inventas tu**; es una cuenta de Windows nueva |
| Carpeta de datos | con Deep Freeze, una ruta en el ThawSpace |
| Autorizar la memoria USB | marcalo con la memoria de emergencia conectada |

La misma contrasena de cuenta de Windows puede repetirse en las 30 maquinas: no
protege nada frente a quien usa el equipo, que ya entra con esa sesion.

### Paso 4 — "Maestro"

El equipo de un docente. Como el de la administracion pero con `digbit_equipo`: cada docente ve sus
clases y sus bitacoras, no administra nada.

---

El resto de esta guia es el camino por consola, que es el que hay que seguir
si el instalador falla o si quieres saber que esta pasando por debajo.

## Antes de empezar

**Abre PowerShell como administrador.** El título de la ventana tiene que
empezar por "Administrador"; si no, casi todo fallará.

```powershell
cd A:\DigBit-Paquete
Set-ExecutionPolicy -Scope Process Bypass -Force
```

**Si el equipo tiene Deep Freeze, descongélalo primero** y reinicia. Con el
disco congelado la instalación termina en verde y desaparece al reiniciar. Es el
error más caro y ningún guion puede evitarlo por ti: `configurar_equipo.ps1` se
niega a seguir si lo detecta congelado, pero sólo si puede detectarlo.

**Toma una instantánea** si es una máquina virtual.

---

# Parte A — El equipo servidor y de la administración

Esto se hace **una sola vez**, y va primero: los laboratorios se dan de alta
desde aquí, y el instalador de las máquinas comprueba contra la base que existan.

## A1. La base de datos

```powershell
.\deploy\preparar_bd.ps1 -Destino C:\DigBitDB
```

Deja MySQL como servicio automático, con el esquema y los usuarios de prueba.
Sobrevive a los reinicios.

> **Si vas a usar la base real del TESCHI**, sáltate este paso y ve al final,
> a "Migrar una base que ya existe".

## A2. Abrirla a la red

Sólo si las máquinas de laboratorio están en otros equipos:

```powershell
.\deploy\abrir_bd_en_red.ps1 -Contrasena 'UnaClaveQueApuntes'
```

Crea el usuario de red y la regla del cortafuegos. **Al terminar imprime la
cadena de conexión con la IP de este equipo: guárdala**, es la que necesitarás
en cada máquina.

En `Server=` vale igual el **nombre** del equipo que su IP, y es lo que usa el
instalador (`servidor.config`) para no depender del DHCP. Por consola puedes
poner cualquiera de los dos.

## A3. Los dos usuarios con permisos mínimos

Edita antes las dos contraseñas al principio de `db\07_usuarios_minimos.sql`, y:

Desde **cmd** (en PowerShell el `<` no funciona):

```bat
C:\DigBitDB\bin\mysql.exe -u root < db\07_usuarios_minimos.sql
```

Por qué dos: el `connections.config` de una máquina de laboratorio **lo puede
leer quien usa el equipo**, porque DigBit corre con su sesión de Windows. Con
`digbit_equipo` puede meter bitácoras falsas, crear cuentas de estudiante (lo
mismo que "Crear cuenta") y cambiar la contraseña de un estudiante o de un
docente, y con ella entrar con esa cuenta. No puede borrar el semestre, cambiar
el esquema, crearse una cuenta docente o de la administración ni tocar la de la
administración. El detalle, al final de `db\07_usuarios_minimos.sql`.

En la base de la escuela (MariaDB 5.5) la 07 la tiene que ejecutar quien tenga
`root` en ese servidor: el usuario `teschi_otru` no puede crear usuarios de MySQL.
Mientras los equipos se conecten con `teschi_otru`, que puede hacerlo todo
(incluido borrar tablas), nada de esto los protege.

## A4. La aplicación, en el equipo de la administración

```powershell
.\deploy\configurar_equipo.ps1 -Modo adm `
    -CadenaConexion "Database=teschi_otru;Server=127.0.0.1;Port=3306;User Id=digbit_admin;Password=LA_QUE_PUSISTE"
```

Copia los archivos a `C:\DigBit`, escribe la conexión y excluye la carpeta en
Defender. **No** bloquea la pantalla, no crea cuentas y no instala el vigilante.

Abre `C:\DigBit\DigBit.exe` y entra con `ADMIN001` / `admin123`.

## A5. Dar de alta los laboratorios

Desde la propia aplicación, con el ratón, en el panel de administración:
**Laboratorios** → **Nuevo laboratorio** para cada uno, y luego **Horarios**
para las clases y el horario.

Esto es imprescindible antes de tocar las máquinas: el instalador comprueba
contra la base que el laboratorio que le pasas exista de verdad.

---

# Parte B — Cada máquina de laboratorio

Esto se repite **una vez por equipo**. Entre una máquina y la siguiente sólo
cambia `-NumeroMaquina`; entre laboratorios, también `-Laboratorio`.

## B1. Si tiene Deep Freeze: descongelar

`Mayús` + doble clic en el icono junto al reloj → contraseña → **Boot Thawed** →
reiniciar. Compruébalo con `.\deploy\diagnostico_equipo.ps1`, sección 0.

## B2. Instalar

```powershell
.\deploy\configurar_equipo.ps1 -Cuenta laboratorio -Contrasena "LaClaveDeLaCuenta" `
    -Laboratorio "Laboratorio de Computo 1" `
    -NumeroMaquina "12" `
    -CadenaConexion "Database=teschi_otru;Server=LA_IP_DEL_SERVIDOR;Port=3306;User Id=digbit_equipo;Password=xxx"
```

Qué es cada cosa:

- **`-Laboratorio`** es el nombre **exacto** de la tabla `laboratorios`, el
  mismo que pusiste en el panel de administración. **Cópialo, no lo teclees.** Si te equivocas, el
  guion se detiene y te enseña la lista buena.
- **`-NumeroMaquina`** es el número pegado en el equipo. Es lo que aparecerá en
  la bitácora cuando alguien reporte "la 12 no tiene internet", y en la lista
  que marca cada estudiante de ese laboratorio (paso 3: salen los equipos que
  registraron bitácoras en los últimos 30 días). Sin esto usa el nombre de
  Windows, que si es `DESKTOP-A7F3K2` no le sirve a nadie y no sale en esa lista.
- **`-CarpetaDatos "T:\DigBitDatos"`** — **sólo con Deep Freeze**, apuntando a un
  ThawSpace. Sin ella se pierden cada noche las bitácoras que aún no llegaron al
  servidor. El guion se niega a seguir si detecta Deep Freeze y no la indicas.
- **`-AutorizarMemoria`** — opcional. Deja autorizada como llave de emergencia
  la memoria USB conectada en ese momento (tiene que haber **exactamente una**).
  Ver más abajo.

## B3. Comprobar

```powershell
.\deploy\diagnostico_equipo.ps1
```

No cambia nada, sólo mira. Todo debería salir en verde.

## B4. Reiniciar, probar, y sólo entonces congelar

Reinicia. El equipo tiene que entrar solo en la cuenta del laboratorio y
aparecer DigBit sin escritorio. Entra con una cuenta de estudiante, registra una bitácora,
comprueba que aparece el escritorio.

Vuelve a pasar el diagnóstico: la marca de instalación tiene que seguir ahí
después del reinicio. Ésa es la prueba de que el disco conserva lo escrito.

**Y ahora sí, congela** (Boot Frozen) y reinicia. Una vez congelado ya no se
corrige nada sin repetir el ciclo entero.

---

# Parte C — El equipo de un docente

```powershell
.\deploy\configurar_equipo.ps1 -Modo maestro `
    -CadenaConexion "Database=teschi_otru;Server=LA_IP;Port=3306;User Id=digbit_equipo;Password=xxx"
```

Igual que el de la administración, pero con el usuario restringido: cada
docente ve sus clases y sus bitácoras, no administra nada. Si el laboratorio usa la
generación de códigos, también abre sus clases desde aquí y ve el código de
cada una durante ella (necesita la versión 1.1; ver "Migrar una base que ya
existe").

---

# Todas las contrasenas, en una tabla

Son seis cosas distintas y es facil confundirlas, sobre todo porque tres se
llaman "contrasena" en pantallas parecidas.

| Que | Quien la pone | Donde hace falta despues | Donde acaba guardada |
|---|---|---|---|
| MySQL `digbit_equipo` | la inventas al preparar el servidor | cada maquina de laboratorio y cada equipo docente | `connections.config` de cada equipo, **en claro** |
| MySQL `digbit_admin` | la inventas al preparar el servidor | el equipo de la administracion | `connections.config` de ese equipo, **en claro** |
| MySQL `digbit_consulta` | la pone db/18, al azar | ninguna a mano: DigBit la lee de la base cuando entra una persona responsable de laboratorio | tabla `configuracion` (`consulta_clave`), **en claro** |
| Cuenta de Windows del laboratorio | la inventas al instalar cada maquina | Windows, para el inicio automatico | el registro, **en claro** |
| MySQL `root` | nadie: se queda vacia | solo desde el propio servidor | — |
| Usuarios de DigBit (`ADMIN001`...) | vienen sembradas | entrar en la aplicacion | tabla `usuarios`, cifradas |

Las dos primeras son las que hay que apuntar y guardar: sin ellas no se puede
configurar ni un equipo mas. Las demas o se inventan sobre la marcha, o las pone
una migracion, o no existen.

**Las que dicen "en claro" son a proposito, y son el limite de este
diseno.** El `connections.config` de una maquina de laboratorio lo puede leer
quien usa el equipo, porque DigBit corre con su sesion de Windows; por eso esa
maquina lleva `digbit_equipo` y nunca `digbit_admin`. Quien lo lea puede meter
bitacoras falsas, crear cuentas de estudiante, cambiar la contrasena de
estudiantes y docentes (ver A3) y leer el secreto de los codigos generados, con
el que se calcula el codigo de cualquier clase y dia; pero no puede borrar el
semestre, cambiarse de grupo ni tocar la cuenta de la administracion. Tambien
puede leer la contrasena de `digbit_consulta`, que cada equipo necesita para
pasar a esa cuenta cuando entra una persona responsable de laboratorio; pero
con ella solo se lee lo que el equipo ya lee. La del inicio
de sesion automatico esta en el registro en claro porque Windows lo guarda asi;
protegerla de verdad necesita un secreto de LSA, que esta disenado pero sin
escribir.

`root` sin contrasena parece peor de lo que es: MySQL escucha solo en
`127.0.0.1` salvo que ejecutes `abrir_bd_en_red.ps1`, asi que hace falta estar
sentado en el servidor. Aun asi, ponle una si el servidor es una maquina a la
que accede mas gente.

---

# Usuarios para entrar

Los que trae la base de prueba. **En la base real del TESCHI no existen.**

| Usuario | Contraseña | Rol |
|---|---|---|
| `ADMIN001` | `admin123` | Administración |
| `EMP001` a `EMP006` | `profesor123` | Docentes |
| `20230001` a `20230006` | `alumno123` | Estudiantes |

Están sembrados en MD5 **a propósito**, para ejercitar la migración: la primera
vez que se entra con cada uno, su contraseña se reescribe sola en PBKDF2.

No trae responsables de laboratorio: se dan de alta en el panel de
administración (**Docentes → Responsables de laboratorio... → Registrar
responsable**), y para que puedan entrar hace falta aplicar también a esta base
`db\18_encargados.sql` con root
(`C:\DigBitDB\bin\mysql.exe -u root < db\18_encargados.sql`, desde cmd).

---

# En la pantalla de inicio del kiosco

Además del login, en modo kiosco hay dos botones abajo a la derecha:

**Apagar el equipo.** Con confirmación. Sin él, la única forma de apagar sería
`Ctrl+Alt+Supr`, que mucha gente no conoce.

**Acceso de emergencia.** Pide la memoria USB autorizada y abre el escritorio
**sin bitácora**, para cuando no hay servidor y el laboratorio se quedaría
inservible. Se identifica por el número de serie del firmware de la memoria: no
se puede duplicar copiando archivos. El acceso dura hasta que el equipo se
reinicie, y cada uso queda en el log.

Es una puerta a propósito en lo único que el sistema existe para obligar. Esa
memoria va en el llavero de la persona responsable del laboratorio, no en un
cajón.

**Si se va la luz a media clase**, quien ya registró su bitácora vuelve
a entrar con su código y DigBit le pregunta si quiere seguir usando el equipo
hasta la hora de salida. No registra una segunda bitácora.

---

# Mantenimiento

## Actualizar sin reconfigurar

Cuando cambie sólo la aplicación:

```powershell
.\deploy\actualizar_digbit.ps1
```

Conserva la configuración del equipo y sustituye los binarios. Si lo que cambió
es un guion de `deploy\`, basta con copiar ese archivo.

Con **Deep Freeze**, en este orden: descongela (Boot Thawed) y reinicia,
ejecuta `actualizar_digbit.ps1`, reinicia, comprueba con
`diagnostico_equipo.ps1` que dice la versión nueva, y vuelve a congelar. Si el
disco está congelado el guion se niega: la copia iría a la capa temporal y el
reinicio la borraría.

## Volver a entrar como administrador

`Ctrl+Alt+Supr` → **Cambiar de usuario**. En VirtualBox esa combinación se manda
desde el menú **Entrada → Teclado → Insertar Ctrl-Alt-Supr**, porque el equipo
anfitrión se queda con la real.

También sirve mantener `Mayús` pulsada durante el arranque para saltar el inicio
automático.

## Deshacerlo

Para quitar sólo el kiosco y dejar el equipo utilizable, conservando los
archivos, la cuenta, la carpeta de datos y la base:

```powershell
.\deploy\configurar_equipo.ps1 -Cuenta laboratorio -Revertir
.\deploy\preparar_bd.ps1 -Quitar
```

Quita el kiosco, las directivas, el inicio automático y el servicio. **No** borra
la carpeta de datos: ahí pueden quedar bitácoras sin enviar.

Para dejar el equipo **como si DigBit nunca se hubiera instalado**, que es lo
que hace falta antes de reinstalar desde cero:

```powershell
.\deploy\limpiar_equipo.ps1 -SoloMostrar        # primero mirar
.\deploy\limpiar_equipo.ps1 -Todo               # y entonces hacerlo
```

Sin conmutadores no borra nada que tenga datos dentro: la base y la carpeta de
datos se conservan y te dice al final que las dejó. `-Todo` incluye las dos, y
`-Cuenta laboratorio -Todo` añade la cuenta de Windows del laboratorio y su perfil.

> `-Todo` **borra la base entera**, con las bitácoras, los laboratorios y los
> horarios. En el equipo servidor eso es el semestre. Respalda antes si hay algo
> dentro que importe.

## Si algo falla

```powershell
.\deploy\diagnostico_equipo.ps1
```

Dice en qué estado quedó el equipo, línea por línea, y qué hacer con cada cosa
que salga en rojo. Es lo primero que hay que mirar, siempre.

Los registros están en la carpeta de datos, o si no se configuró:
`%LOCALAPPDATA%\DigBit\logs` (la aplicación) y `%ProgramData%\DigBit\logs`
(el vigilante).

---

# Migrar una base que ya existe

Para la base real del TESCHI, en lugar de `preparar_bd.ps1`:

```powershell
.\deploy\migrar_bd.ps1 -Servidor LA_IP -Usuario root -ContrasenaAdmin "LaDelAdministrador"
```

Respalda con `mysqldump`, pasa una revisión que **no cambia nada**, y se detiene
si encuentra algo que vaya a fallar. Sólo entonces aplica las migraciones (03,
04, 05, 06 y de la 08 a la 21), en orden, saltando lo que ya esté y parando al
primer error. Hazlo fuera de horario de clase: en MariaDB 5.5 algunas
copian la tabla entera con las escrituras bloqueadas (la 10 la de bitácoras, la
12 la de usuarios, la 14 la de sesiones). La 19 no copia ninguna: solo crea la
tabla donde cada docente guarda lo que elige para sus clases. La 20 añade dos
columnas vacías a `horarios`, que es pequeña. La 21 tampoco copia nada: crea la
tabla del historial de cambios.

Sin la 10 no se puede dar de alta a ninguna cuenta de estudiante (ni desde la
administración ni con "Crear cuenta" en el laboratorio): en la base de la
escuela la carrera exigía grupo y semestre. Sin la 12 el color de cada docente
sale solo y no se puede elegir en Docentes. La 11 deja todos los laboratorios
con el código único de cada docente: aplicarla no cambia nada. Con la 19, en las
computadoras docentes con la versión que la usa, cada clase sale en el panel
docente, Mi horario y Mis grupos con un círculo y un color propios, y cada
docente puede personalizarla (imagen, color y nombre corto, solo para su
cuenta); sin ella todo se ve como antes. Si los equipos entran con `digbit_equipo`, aplícala con root
(le da permiso sobre la tabla nueva) y, si alguna vez vuelves a pasar
`db\07_usuarios_minimos.sql`, esa ya lo incluye.

Con la 21 (**historial de cambios**), cada cambio del horario, de los
laboratorios y cada corrección de una bitácora queda en la base con quién lo
hizo, cuándo y qué decía antes, y la administración ve en Alertas los cambios de
horario que hizo otra persona (filtro «Cambios de horario», también con el
aviso de Windows) y el «Historial de cambios...» completo. El orden da igual: sin ella
todo funciona como antes, y con ella solo dejan rastro los cambios hechos desde
un DigBit de esta versión (actualiza el del equipo de la administración).
Aplícala con root: así también deja leerla a `digbit_consulta`, para que las
personas responsables de laboratorio vean esas alertas y el historial; con otro usuario,
`migrar_bd.ps1` lo dice con un AVISO. A `digbit_equipo` no le da nada: los
equipos no hacen esos cambios.

Para que **cambiar el horario no reescriba las semanas pasadas** (la 20: cada
franja con su propia vigencia; ver README, «Cambios de horario sin reescribir
el pasado»), el orden importa:

1. Actualiza DigBit en **todos** los equipos que lo usan: las máquinas de cada
   laboratorio (descongeladas: ver «Actualizar sin reconfigurar»), las
   computadoras docentes y la de la administración.
   `diagnostico_equipo.ps1` dice la versión de cada uno.
2. Aplica la 20 (con `migrar_bd.ps1` o sola). Sola no cambia nada: las franjas
   quedan sin fecha propia, como estaban. No necesita permisos nuevos.
3. Entonces, en el panel de administración, **Ajustes → «Los cambios de horario
   aplican desde una fecha»**. Desde ese momento, cambiar o quitar una franja que
   ya se dio le pone fin y la nueva empieza en la fecha que elijas.

Si lo enciendes con algún equipo viejo, ese equipo no conoce las fechas de las
franjas: vería a la vez la franja terminada y la nueva (dos clases a la misma
hora en su lista, o el código abriendo la vieja). Mientras esté apagado, todo
funciona como antes y Horarios avisa al editar una franja que ya se dio de que
el cambio alcanza también a las semanas pasadas. Las clases con sesiones ya no
se desactivan sino que se terminan en una fecha, con o sin la 20.

Para que cada estudiante entre **sin códigos** o con **generación de códigos**,
el orden importa:

1. Aplica la 11 (sola no cambia nada). Para la generación de códigos, también
   la 13 (guarda el secreto del que salen los códigos). Para que sin códigos
   también se pueda registrar cuando no hay clase (uso libre), la 14.
2. Actualiza DigBit a la **1.1** en las máquinas de ese laboratorio
   (`actualizar_digbit.ps1`, descongeladas: ver «Actualizar sin reconfigurar»)
   y en las computadoras de sus docentes.
   `diagnostico_equipo.ps1` dice la versión de cada equipo. Una anterior
   seguiría pidiendo el código del docente.
3. Entonces, en el panel de administración: **Códigos de las clases** (en el
   encabezado o en Ajustes): la forma de toda la escuela y, abajo, los
   laboratorios con otra forma; uno suelto, también en Laboratorios →
   Códigos…. Al guardar pide otra vez su contraseña (con tres equivocadas se
   cierra la sesión). Vale en cuanto se guarda, sin reiniciar nada; las
   clases que ya empezaron conservan su código hasta que terminan.
   Hazlo desde el equipo de la administración: al elegir la generación de códigos
   se crea el secreto, y los equipos no pueden crearlo.

Los equipos tienen que poder leer `configuracion` (la 13 aplicada por root, o
la 07, le dan ese permiso a `digbit_equipo`): si no, abren las clases con un
código al azar y los que ve la administración no valen en ellos; su log lo dice
("Codigos generados cada semana: este usuario de MySQL no ve la tabla
configuracion").

Con generación de códigos, al empezar cada semana cada clase tiene un código
distinto para cada día. Solo la administración los ve todos (Horarios, en cada
clase de la semana). Cada docente tiene que **entrar a DigBit durante su
clase** (desde 15 minutos antes): su panel abre la clase y le enseña el código
para dictarlo. Si un día no puede, la administración le da el código (lo ve en
Horarios) o abre la clase con clic derecho en «Los laboratorios ahora».

Para **«¿Olvidaste tu contraseña?»** por correo:

1. Aplica la 13: crea dos tablas nuevas, no cambia ningún dato ni bloquea nada.
   Si la ejecuta root, además deja a `digbit_equipo` leer los ajustes; si la
   ejecuta otro usuario y los equipos entran con `digbit_equipo`, sale un AVISO:
   que la aplique también root. (No vuelvas a pasar la 07 para esto: además de
   los permisos, vuelve a fijar las contraseñas de los dos usuarios.)
2. Instala la API en el hosting de la escuela, con la contraseña de aplicación
   de `digbit.teschi@gmail.com` y su propio usuario de MySQL (`digbit_api`, que
   crea root con lo que dice el final de la 13): todo en `api/LEEME.md`.
3. En el panel de administración: **Ajustes** (arriba, debajo del título) →
   «Recuperar contraseña por correo» → **Configurar...** → la dirección
   `https://` de `recuperar.php` y su clave → **Probar** → **Guardar**. Vale al
   momento en todos los equipos con la 1.1, sin descongelar ninguno. Ajustes no
   deja guardar la de la propia PC (`http://127.0.0.1...`): en los demás
   equipos no existe.
4. Los equipos con una versión anterior a la 1.1 hay que actualizarlos una vez
   (descongelados): en ellos el enlace abre la ventana vieja, que no hace nada.

Mientras no esté, el enlace dice que pidan a la administración de DigBit una
contraseña nueva, que se pone en Estudiantes o Docentes → Restablecer contraseña
(la de una persona responsable de laboratorio, en Docentes → Responsables de
laboratorio... → Restablecer contraseña).

El **aviso de privacidad** (lo ve quien crea su cuenta en «Crear cuenta», que
tiene que marcar «Leí el aviso de privacidad», y lo enlaza «¿Olvidaste tu
contraseña?») se pone en **Ajustes → Aviso de privacidad → Configurar...**: el
aviso simplificado y, si la hay, la dirección del aviso integral. Sin texto no
sale nada. Para que el correo del código lleve al pie el mismo enlace, pon esa
dirección también en `aviso_privacidad_url` del `config.php` de la API (ver
`api/LEEME.md`, que dice además qué datos guarda la recuperación y por cuánto
tiempo, para el aviso).

Para las personas **responsables de laboratorio** (ven el panel de
administración sin poder cambiar nada):

1. Aplica la 18 **con root**: pone el tipo 4 en `tipo_usuarios`, crea la cuenta
   de MySQL de solo lectura `digbit_consulta` y guarda su contraseña, al azar,
   en `configuracion`. Con `teschi_otru` solo hace lo primero y sale un AVISO:
   sin esa cuenta no pueden entrar. Volver a pasarla conserva la contraseña y
   deja otra vez la cuenta en solo lectura.
2. Actualiza DigBit (la **1.1**, ya con responsables de laboratorio) en los
   equipos donde vayan a entrar. Un DigBit anterior no conoce el tipo 4: avisa
   «Tipo de usuario no manejado» y se queda sin ventana (en el kiosco,
   `Ctrl+Alt+Supr` → Cerrar sesión).
3. Entonces, en el panel de administración: **Docentes → Responsables de
   laboratorio... → Registrar responsable**.

Fuera de actualizar DigBit, no hay que tocar ningún equipo: ni reinstalarlo de
otro tipo ni cambiar su `connections.config`. Al entrar una persona responsable
de laboratorio, DigBit lee
de la base la contraseña de `digbit_consulta` y trabaja con esa cuenta durante
su sesión, en el equipo que sea.

Para ver qué haría sin tocar nada:

```powershell
.\deploy\migrar_bd.ps1 -Servidor LA_IP -Usuario root -SoloRevisar
```

Lo que más suele bloquear: si en la base ya hay una cuenta con dos bitácoras del
mismo código, la migración de la fase 2 no puede aplicarse. La revisión te
enseña esas filas y cuál conservar.

---

# Lo que este montaje no prueba

- **Deep Freeze de verdad**, si las máquinas de prueba no lo tienen.
- **El comportamiento con 30 equipos a la vez** contra el mismo servidor.
- **La red de la escuela**: cortafuegos entre segmentos, directivas de dominio.
- **Defender con una consola central** que pueda revertir las exclusiones.

Y una advertencia que no depende de esta guía: `DigBit.exe` **no está firmado**,
y Defender lo detecta como falso positivo. El instalador pone la exclusión antes
de copiar, y por eso funciona. La solución de fondo es firmarlo.
