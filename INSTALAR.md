# Instalar DigBit

Esta guía deja funcionando el montaje completo: **un equipo que hace de servidor
y de administrador**, y las **máquinas de laboratorio** que se conectan a él.

Es el mismo programa en los tres sitios. Lo único que cambia es si se bloquea la
pantalla y con qué usuario de base de datos se conecta.

| | Pantalla bloqueada | Usuario de MySQL | Etiquetas |
|---|---|---|---|
| Administrador | no | `digbit_admin` | ninguna |
| Profesor | no | `digbit_equipo` | ninguna |
| Máquina de laboratorio | **sí** | `digbit_equipo` | laboratorio + número |

## Qué trae este paquete

```
preparar_bd.ps1     Deja MySQL listo con el esquema y datos de prueba
servidor.config     A que equipo apunta esta copia (lo escribe el instalador)
mysql/              MySQL portátil (no hace falta instalarlo aparte)
deploy\             Los guiones de instalación y diagnóstico
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
| Para los equipos (`digbit_equipo`) | el usuario de MySQL que llevaran las maquinas de laboratorio y los profesores |
| Para el administrador (`digbit_admin`) | el usuario de MySQL del equipo que gestiona horarios |

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

En el mismo equipo, o en el del encargado. Al marcarlo, el usuario cambia solo
a `digbit_admin`; escribe en **Contrasena** la que acabas de crear para el.

| Casilla | Que poner |
|---|---|
| Servidor | **nada**: ya viene relleno |
| Base | `teschi_otru` |
| Usuario | `digbit_admin` |
| Contrasena | la del paso 1 |

Lo unico que se escribe aqui es la contrasena. Pulsa **Instalar**.

En estos dos equipos, el del administrador y el del profesor, **no se instala
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

### Paso 3 — "Maquina de laboratorio"

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
protege nada frente al alumno, que ya entra con esa sesion.

### Paso 4 — "Profesor"

Como el de administrador pero con `digbit_equipo`: ve sus clases y sus
bitacoras, no administra nada.

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

# Parte A — El equipo servidor y administrador

Esto se hace **una sola vez**, y va primero: los laboratorios se dan de alta
desde aquí, y el instalador de las máquinas comprueba contra la base que existan.

## A1. La base de datos

```powershell
.\preparar_bd.ps1 -Destino C:\DigBitDB
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

```powershell
C:\DigBitDB\bin\mysql.exe -u root < db\07_usuarios_minimos.sql
```

Por qué dos: el `connections.config` de una máquina de laboratorio **lo puede
leer el alumno**, porque DigBit corre con su sesión de Windows. Con
`digbit_equipo` lo peor que consigue es meter una bitácora falsa; no puede
borrar el semestre ni cambiarse de grupo.

## A4. La aplicación, en modo administrador

```powershell
.\deploy\configurar_equipo.ps1 -Modo adm `
    -CadenaConexion "Database=teschi_otru;Server=127.0.0.1;Port=3306;User Id=digbit_admin;Password=LA_QUE_PUSISTE"
```

Copia los archivos a `C:\DigBit`, escribe la conexión y excluye la carpeta en
Defender. **No** bloquea la pantalla, no crea cuentas y no instala el vigilante.

Abre `C:\DigBit\DigBit.exe` y entra con `ADMIN001` / `admin123`.

## A5. Dar de alta los laboratorios

Desde la propia aplicación, con el ratón: **Administrar horarios**. Crea los
laboratorios, las clases y el horario.

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
  mismo que pusiste en el ADM. **Cópialo, no lo teclees.** Si te equivocas, el
  guion se detiene y te enseña la lista buena.
- **`-NumeroMaquina`** es el número pegado en el equipo. Es lo que aparecerá en
  la bitácora cuando alguien reporte "la 12 no tiene internet". Sin esto usa el
  nombre de Windows, que si es `DESKTOP-A7F3K2` no le sirve a nadie.
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
aparecer DigBit sin escritorio. Entra como alumno, registra una bitácora,
comprueba que aparece el escritorio.

Vuelve a pasar el diagnóstico: la marca de instalación tiene que seguir ahí
después del reinicio. Ésa es la prueba de que el disco conserva lo escrito.

**Y ahora sí, congela** (Boot Frozen) y reinicia. Una vez congelado ya no se
corrige nada sin repetir el ciclo entero.

---

# Parte C — El equipo de un profesor

```powershell
.\deploy\configurar_equipo.ps1 -Modo maestro `
    -CadenaConexion "Database=teschi_otru;Server=LA_IP;Port=3306;User Id=digbit_equipo;Password=xxx"
```

Igual que el del administrador, pero con el usuario restringido: un profesor ve
sus clases y sus bitácoras, no administra nada.

---

# Todas las contrasenas, en una tabla

Son cinco cosas distintas y es facil confundirlas, sobre todo porque tres se
llaman "contrasena" en pantallas parecidas.

| Que | Quien la pone | Donde hace falta despues | Donde acaba guardada |
|---|---|---|---|
| MySQL `digbit_equipo` | la inventas al preparar el servidor | cada maquina de laboratorio y cada equipo de profesor | `connections.config` de cada equipo, **en claro** |
| MySQL `digbit_admin` | la inventas al preparar el servidor | el equipo del administrador | `connections.config` del ADM, **en claro** |
| Cuenta de Windows del laboratorio | la inventas al instalar cada maquina | Windows, para el inicio automatico | el registro, **en claro** |
| MySQL `root` | nadie: se queda vacia | solo desde el propio servidor | — |
| Usuarios de DigBit (`ADMIN001`...) | vienen sembradas | entrar en la aplicacion | tabla `usuarios`, cifradas |

Las dos primeras son las que hay que apuntar y guardar: sin ellas no se puede
configurar ni un equipo mas. Las demas o se inventan sobre la marcha o no
existen.

**Las tres que dicen "en claro" son a proposito, y son el limite de este
diseno.** El `connections.config` de una maquina de laboratorio lo puede leer
el alumno, porque DigBit corre con su sesion de Windows; por eso esa maquina
lleva `digbit_equipo`, que solo puede insertar bitacoras, y nunca
`digbit_admin`. Lo peor que consigue quien lo lea es meter una bitacora falsa:
no puede borrar el semestre ni cambiarse de grupo. La del inicio de sesion
automatico esta en el registro en claro porque Windows lo guarda asi; protegerla
de verdad necesita un secreto de LSA, que esta disenado pero sin escribir.

`root` sin contrasena parece peor de lo que es: MySQL escucha solo en
`127.0.0.1` salvo que ejecutes `abrir_bd_en_red.ps1`, asi que hace falta estar
sentado en el servidor. Aun asi, ponle una si el servidor es una maquina a la
que accede mas gente.

---

# Usuarios para entrar

Los que trae la base de prueba. **En la base real del TESCHI no existen.**

| Usuario | Contraseña | Rol |
|---|---|---|
| `ADMIN001` | `admin123` | Administrador |
| `EMP001` a `EMP006` | `profesor123` | Profesores |
| `20230001` a `20230006` | `alumno123` | Alumnos |

Están sembrados en MD5 **a propósito**, para ejercitar la migración: la primera
vez que cada uno entra, su contraseña se reescribe sola en PBKDF2.

---

# En la pantalla del alumno

Además del login, en modo kiosco hay dos botones abajo a la derecha:

**Apagar el equipo.** Con confirmación. Sin él, la única forma de apagar sería
`Ctrl+Alt+Supr`, que mucha gente no conoce.

**Acceso de emergencia.** Pide la memoria USB autorizada y abre el escritorio
**sin bitácora**, para cuando no hay servidor y el laboratorio se quedaría
inservible. Se identifica por el número de serie del firmware de la memoria: no
se puede duplicar copiando archivos. El acceso dura hasta que el equipo se
reinicie, y cada uso queda en el log.

Es una puerta a propósito en lo único que el sistema existe para obligar. Esa
memoria va en el llavero del encargado, no en un cajón.

**Si se va la luz a media clase**, el alumno que ya registró su bitácora vuelve
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
.\preparar_bd.ps1 -Quitar
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
`-Cuenta laboratorio -Todo` añade la cuenta del alumno y su perfil.

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
si encuentra algo que vaya a fallar. Sólo entonces aplica las cuatro
migraciones, en orden y parando al primer error.

Para ver qué haría sin tocar nada:

```powershell
.\deploy\migrar_bd.ps1 -Servidor LA_IP -Usuario root -SoloRevisar
```

Lo que más suele bloquear: si en la base ya hay un alumno con dos bitácoras del
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
