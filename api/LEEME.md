# API de recuperación de contraseña de DigBit

Es lo que hay detrás de **«¿Olvidaste tu contraseña?»** en el inicio de sesión
de DigBit, también en el kiosco de los laboratorios:

1. Quien olvidó su contraseña (estudiante, docente o responsable de
   laboratorio) escribe su matrícula (o su número de empleado).
2. Esta API busca su cuenta y le manda **un código de 6 cifras a su correo**,
   desde `digbit.teschi@gmail.com`.
3. En DigBit escribe el código y su contraseña nueva. Si el código es bueno, la
   API guarda la contraseña nueva y ya puede entrar con ella.

La cuenta de correo y su contraseña viven **solo aquí**, en el hosting. Los
equipos del laboratorio no las tienen: quien usa un equipo puede leer la
configuración de DigBit que hay en él, pero no la de esta carpeta.

## Qué hace falta

- Un hosting con **PHP 7.2 o posterior**, con `pdo_mysql` y `openssl` (casi
  todos los traen) y **https**.
- Que ese hosting pueda conectarse a la base de la escuela
  (`sql.teschi.edu.mx`, puerto 3306). La base está en Google Cloud: si tiene
  restringidas las direcciones que pueden entrar, hay que añadir la del
  hosting. Es lo primero que conviene comprobar.
- Que el hosting deje salir al correo de Gmail (`smtp.gmail.com`, puerto 587).
  Algunos lo bloquean; ver «Si algo falla».
- La migración `db/13_recuperacion_contrasena.sql` aplicada en la base: crea
  dos tablas nuevas y no cambia ningún dato.
- Un usuario de MySQL para la API (paso 2).
- **DigBit 1.1 en los equipos.** En los de una versión anterior, «¿Olvidaste
  tu contraseña?» abre la ventana vieja, que no hace nada: hay que
  actualizarlos una vez (descongelados, si tienen Deep Freeze; ver
  `INSTALAR.md`).

## Instalación

### 1. La contraseña de aplicación de Gmail

Gmail no deja entrar desde un programa con la contraseña normal de la cuenta.
Hace falta una **contraseña de aplicación**:

1. Entra en la cuenta `digbit.teschi@gmail.com` → **Gestionar tu cuenta de
   Google** → **Seguridad**.
2. Activa la **Verificación en dos pasos** (sin ella no aparece la opción
   siguiente).
3. Busca **Contraseñas de aplicaciones**, crea una (por ejemplo «DigBit») y
   copia las 16 letras que te da. Solo se ven una vez.

### 2. El usuario de MySQL de la API

Al final de `db/13_recuperacion_contrasena.sql` están, comentadas, las órdenes
para crear `digbit_api` con lo justo: leer cinco columnas de `usuarios`, cambiar
`password` y usar la tabla de códigos (leer, añadir, cambiar y **borrar**: la
API borra las solicitudes de más de 30 días), con un máximo de 10 conexiones a
la vez (para que la API no acapare las de la base, que son las mismas que usan
los equipos). Las ejecuta **quien administra el servidor de la base** (root),
igual que las de `db/07`; el origen (`'%'`) hay que cambiarlo en todas las
líneas por igual. Con ese usuario, si alguien se hiciera con `config.php`,
podría cambiar contraseñas de estudiantes, docentes y responsables de
laboratorio, pero no leer las guardadas, ni tocar otras tablas, ni (con el
disparador de `db/07`) cambiar la de una cuenta de la administración.

- **No** uses `digbit_equipo`: no tiene permiso sobre la tabla de códigos y la
  API no funcionaría («faltan permisos» en Probar).
- Si `digbit_api` ya se creó sin el `GRANT DELETE` (las primeras versiones de la
  receta no lo traían), la recuperación funciona igual, pero las solicitudes
  viejas no se borran y Probar lo dice con la orden exacta para root:
  `GRANT DELETE ON teschi_otru.recuperacion_contrasena TO 'digbit_api'@'…';`
- Si de momento no hay forma de crear `digbit_api`, puedes poner el usuario con
  el que ya entran los equipos de la escuela, sabiendo que ese puede con toda
  la base y que su contraseña pasa a estar también en el hosting.

### 3. Subir la carpeta

Sube la carpeta `api/` entera al hosting, por ejemplo como
`public_html/digbit/`. La dirección que usará DigBit es la de `recuperar.php`,
por ejemplo:

```
https://www.teschi.edu.mx/digbit/recuperar.php
```

Usa la dirección **final**, la que queda en el navegador al abrirla (con o sin
`www`, como la deje el hosting): DigBit no sigue redirecciones.

El `.htaccess` es una lista blanca: desde el navegador **solo se abre
`recuperar.php`**; lo demás (`config.php`, cualquier copia suya que se quede
ahí, `lib/`, la prueba) queda cerrado. Eso vale en hostings con Apache, que son
la mayoría; en uno con nginx el `.htaccess` no se aplica, y entonces conviene
todavía más lo del paso siguiente.

### 4. config.php

Copia `config.example.php` como `config.php` y llénalo:

| Dato | Qué poner |
|---|---|
| `bd_servidor`, `bd_puerto`, `bd_nombre` | `sql.teschi.edu.mx`, `3306`, `teschi_otru` |
| `bd_usuario`, `bd_contrasena` | El usuario del paso 2 |
| `smtp_contrasena` | La contraseña de aplicación del paso 1 (las 16 letras, con o sin espacios) |
| `secreto` | 32 caracteres o más al azar. Por ejemplo, el resultado de `php -r "echo bin2hex(random_bytes(24));"`, o de https://www.random.org/strings/ |
| `clave_api` | Otra cadena al azar, más corta. Es la que se pone en DigBit, en Ajustes |
| `aviso_privacidad_url` | Opcional. La dirección del aviso de privacidad integral del TESCHI, completa y con `https://` (la misma que en DigBit, Ajustes → Aviso de privacidad). El correo la pone al pie; vacía, o sin esa línea, el correo sale sin pie. Ver «El aviso de privacidad», más abajo |

Lo demás ya viene bien para Gmail. Mientras `secreto` o `clave_api` sigan
siendo los de ejemplo, la API no atiende.

**Mejor fuera de la carpeta pública.** Si el hosting lo permite, guarda
`config.php` en una carpeta que no se publique (por ejemplo
`/home/<usuario>/digbit_config.php`) y, en el `.htaccess`, quita el `# ` de la
línea `SetEnv DIGBIT_API_CONFIG ...` y pon esa ruta. Si no, déjalo junto a
`recuperar.php`.

`config.php` lleva contraseñas: **no lo subas a git** (ya está en
`.gitignore`, con sus copias) ni lo mandes por correo o chat. Y no dejes copias
suyas en el hosting (`config.php.bak`...): si algún día se quita el
`.htaccess`, serían descargables.

### 5. Decirle a DigBit dónde está

En DigBit, entra con una cuenta de la administración. En el panel de
administración, arriba, debajo de «Panel de administración», pulsa **Ajustes** y,
en «Recuperar contraseña por correo», **Configurar...**:

1. **Dirección del servicio de recuperación**: la de `recuperar.php` del
   paso 3, con `https://`.
2. **Clave del servicio**: la `clave_api` de `config.php`.
3. Pulsa **Probar**. Comprueba la dirección, la clave, que la base tenga la
   tabla de códigos y que el usuario de la base tenga sus permisos, y que Gmail
   acepte la contraseña de aplicación, **sin mandar ningún correo**. Tiene que
   salir «Todo listo». Si sale con la palomita verde pero dice «Falta un permiso
   para borrar las solicitudes de más de 30 días», la recuperación ya funciona:
   pide a root el `GRANT DELETE` que trae el mensaje (el del final de `db/13`).
4. **Guardar**.

Se guarda en la base (tabla `configuracion`), así que vale **al momento en
todos los equipos con DigBit 1.1**, también en los congelados con Deep Freeze:
no hay que tocar ninguno más. Para desactivarla, borra la dirección y guarda.

Ajustes solo guarda una dirección `https://` de un servidor: la de la propia PC
(`http://127.0.0.1...`, `localhost`) sirve para probar en esa PC, pero en los
demás equipos apuntaría a ellos mismos y no funcionaría. Para probar en una
sola PC, ponla en su `DigBit.exe.config` (`UrlRecuperacion` y
`ClaveRecuperacion`), y en el `config.php` de esa API de prueba pon
`'solo_https' => false` (si no, contesta «solo atiende por https»). Solo para
esa prueba: en el hosting, `true`.

### 6. Probarlo de verdad

Con una cuenta de estudiante de prueba que tenga **tu** correo: en el inicio de
sesión de DigBit, «¿Olvidaste tu contraseña?», escribe la matrícula, espera el
correo (mira también en spam la primera vez), escribe el código y una
contraseña nueva, y entra con ella.

Que ese correo **no sea `digbit.teschi@gmail.com`**: lo que una cuenta se
manda a sí misma siempre llega a Recibidos, así que no dice nada. Prueba con
una cuenta de Gmail y con una **@teschi.edu.mx** (la de la escuela es de
Microsoft 365, que filtra distinto), y ábrelos también en el celular.

## El correo que llega

Llega con diseño, con los colores del logo del TESCHI: arriba el logo del
TESCHI y el de la carrera, una franja verde con «Recupera tu contraseña», el
código en grande y, al pie, DigBit e Ingeniería en Sistemas Computacionales.
Con `aviso_privacidad_url` en `config.php`, debajo de «Este correo se envió
automáticamente; no lo respondas.» lleva además «Tus datos se tratan conforme
al aviso de privacidad del TESCHI: leer el aviso de privacidad completo», con
el enlace a esa dirección (en la versión en texto, la dirección escrita).
Los logos van **dentro del mismo correo** (no se descargan de ningún sitio),
así que se ven aunque el programa de correo bloquee las imágenes de fuera. Los
programas que no enseñan diseño enseñan lo mismo en texto.

Está en la carpeta `correo/`:

| Archivo | Qué es |
|---|---|
| `plantilla.html` | El diseño. Lo que va entre llaves dobles (`{{codigo}}`, `{{saludo}}`, `{{minutos}}`, `{{aviso_privacidad_url}}`, los logos) lo pone `lib/correo.php`. El pie del aviso va entre `<!--{{aviso}}-->` y `<!--{{/aviso}}-->`: sin `aviso_privacidad_url` se quita entero. Si cambias el texto, cambia también la versión en texto, en `correo_de_codigo()` de `lib/correo.php` |
| `teschi.png` | Logo del TESCHI, sobre fondo blanco: con fondo transparente, sus letras negras desaparecen en el modo oscuro de la app de Gmail y de Outlook, que oscurecen el fondo del correo pero no las imágenes |
| `sistemas.png` | Logo de Ingeniería en Sistemas Computacionales (ISC), recortado y sobre fondo blanco por lo mismo. Se ve a 168 px de ancho, del mismo alto que el del TESCHI; si lo cambias por uno de otra proporción, ajusta ese ancho en `correo_de_codigo()`. Si no está, en su lugar va el nombre de la carrera escrito |
| `digbit.png` | El icono de DigBit, en el pie, sobre el gris del pie (`#F7F8F6`) |

Si falta `plantilla.html`, el correo sale solo en texto; si falta un logo (o
no se puede leer), en su lugar va su nombre escrito. El saludo lleva solo el
primer nombre («Hola, Ana:»), y solo si son letras: los nombres se escriben a
mano, también en «Crear cuenta», y así nadie mete su propio texto en un correo
con los logos de la escuela; si no, «Hola:». Para ver cómo llega:
`probar_correo.php` («Si algo falla», al final).

## Cómo se protege

- En la base **no se guarda el código**, sino su HMAC-SHA256 con `secreto`:
  quien lea la tabla no puede usarlo.
- Cada código vale **15 minutos y una sola vez**; con **5 equivocados** se
  anula. Pedir uno nuevo anula los anteriores (solo cuando el nuevo ya salió:
  si el correo falla, sigue valiendo el anterior). También se anulan si la
  administración le cambia el correo o la contraseña.
- Cada código queda atado a la cuenta y al **correo al que salió**: si después
  se corrige el correo (en Editar o en Mi perfil), ese código deja de valer.
- Límites (en `config.php`), comprobados de uno en uno para que varias
  peticiones a la vez no se los salten:
  - **3 códigos por cuenta** en una hora;
  - **6 códigos pedidos desde un mismo equipo** en una hora (DigBit dice qué
    equipo pide: laboratorio y número);
  - **60 códigos enviados desde una misma IP** en una hora. Todos los equipos
    de la escuela pueden salir a internet con la misma IP: si al empezar el
    semestre se queda corto, súbelo (`solicitudes_por_ip_hora`);
  - **200 matrículas equivocadas desde una misma IP** en una hora, aparte: así
    quien teclee basura no deja sin código a los demás;
  - **100 correos en una hora y 400 al día en total**: Gmail deja mandar unos
    500 al día desde una cuenta normal y, pasado eso, deja de enviar.
- Solo cuentas **activas** de estudiantes, docentes y responsables de
  laboratorio. Las de la administración no se recuperan por aquí (se cambian a
  mano en la base), y la API nunca les cambia la contraseña.
- La contraseña nueva cumple las mismas reglas que en DigBit
  (`ReglasCuenta.cs`): de 6 a 128 caracteres, contados como los cuenta DigBit, y
  no solo espacios. «CONTRASEÑA» se admite, como en DigBit.
- Solo por **https** (`solo_https`). DigBit no manda nada por http a otra
  máquina ni sigue redirecciones.
- La contraseña nueva se guarda igual que en DigBit (PBKDF2-SHA256 con 100 000
  iteraciones y sal), así que DigBit la reconoce sin más.
- Ningún error enseña la contraseña de la base ni la del correo; los detalles
  van al registro de errores del hosting (`error_log`).
- Cada solicitud guarda la matrícula, la IP y el equipo que la pidió: la API
  **borra las de más de 30 días** (`DIAS_SOLICITUDES` en `recuperar.php`) al
  atender cada una. Para eso su usuario de la base necesita `DELETE` en la tabla
  de códigos (paso 2). Ese plazo es el que debe decir el aviso de privacidad
  (ver «El aviso de privacidad»).

Si alguien no tiene correo en su cuenta, si el suyo está mal escrito o no le
llega, la administración lo corrige (o le pone una contraseña nueva) en
**Estudiantes** o **Docentes** → Editar; el de una persona responsable de
laboratorio, en **Docentes** → **Responsables de laboratorio...** → Editar (o
Restablecer contraseña).

Si se acaba un cupo pero la cuenta tiene un código que todavía vale, se le dice
que use ese, y DigBit pasa a pedírselo.

Lo que no evita, y conviene saberlo:

- Los cupos por IP y totales son de todos. Quien pida códigos a propósito para
  muchas cuentas reales (desde varios equipos, o desde fuera con la clave de la
  API, que los equipos leen de la base) puede dejar sin códigos a la escuela
  durante una hora, o hasta el día siguiente si agota el total del día. No es
  discreto: cada cuenta recibe los correos que no pidió, y en la tabla queda la
  IP y el equipo. Mientras, queda pedir a la administración de DigBit una
  contraseña nueva.
- Quien tenga la clave también puede gastar los códigos de otra persona
  equivocándose a propósito; esa persona tendría que esperar o pedir a la
  administración de DigBit una contraseña nueva.

## El aviso de privacidad

La recuperación trata datos personales, así que el aviso de privacidad del
TESCHI tiene que contarlo. Lo que conviene que diga (lo redacta la escuela):

- **Qué se guarda en cada solicitud**: la matrícula o el número de empleado, la
  IP desde la que se pidió, el equipo (laboratorio y número), la fecha y hora y
  cuántos intentos hubo. El código y el correo **no** se guardan en claro (solo
  su HMAC). El correo de la cuenta lo usa la API para mandar el código desde
  `digbit.teschi@gmail.com` (Gmail).
- **Para qué**: mandar el código, comprobarlo y poner los límites que evitan
  abusos (por cuenta, equipo, IP y en total).
- **Cuánto tiempo**: **30 días**. La API borra sola cada solicitud de más de 30
  días al atender la siguiente (`DIAS_SOLICITUDES` en `recuperar.php`; si lo
  cambias, cambia también el aviso). El registro de errores del hosting
  (`error_log`) guarda además el id de la cuenta y la IP de cada contraseña
  cambiada, y el correo oculto (`a***@…`) de un envío que falló, el tiempo que
  el hosting conserve ese registro.

Para que el correo del código enlace al aviso, pon su dirección en
`aviso_privacidad_url` (`config.php`), la misma que en DigBit (Ajustes →
**Aviso de privacidad** → Configurar..., «Dirección del aviso integral»). Tiene
que ser completa, con `https://` (o `http://`), sin espacios ni acentos (si los
lleva, cópiala de la barra del navegador, que los escribe como `%C3%B1`...).
Si no lo es, el correo sale sin el pie y `error_log` lo dice;
`php probar_correo.php` también dice si el pie sale y con qué dirección.

## Si algo falla

Lo primero: **Probar** en DigBit (Ajustes → «Recuperar contraseña por correo» →
Configurar...). Dice qué parte falla:

| Mensaje | Qué revisar |
|---|---|
| No se encontró el servidor de esa dirección / El servidor de esa dirección no aceptó la conexión | La dirección, o que haya internet en esa PC (el detalle de Windows queda en el registro de diagnóstico del equipo) |
| No se pudo abrir una conexión segura (https) con el servicio | La dirección y el certificado del hosting |
| No contestó como se esperaba: la dirección redirige a … | Pon en Ajustes esa dirección final |
| No contestó como se esperaba: HTTP 404 … | La dirección no es la de `recuperar.php` |
| … no está autorizado … Revisa que la clave sea la misma que tiene el servicio | La clave de Ajustes no es la `clave_api` de `config.php` |
| … no reconoce lo que pidió este equipo: DigBit y el servicio no son de la misma versión | Sube la carpeta `api/` de la misma versión que el DigBit de los equipos |
| … solo atiende por https | Con `https://` en el hosting: su proxy no le dice a PHP que la conexión es https (falta la variable `HTTPS` o la cabecera `X-Forwarded-Proto`); que lo revise quien lo administra. Con `http://127.0.0.1` al probar en una sola PC: `'solo_https' => false` en el `config.php` de esa prueba |
| El servicio de recuperación no está configurado | `secreto` o `clave_api` siguen siendo los de ejemplo, o falta `config.php` |
| … no puede usar la base: falta aplicar db/13 | Aplicar `db/13_recuperacion_contrasena.sql` |
| … no puede usar la base: al usuario de la base le faltan permisos | Los GRANT del final de `db/13` para el usuario de `config.php` (`digbit_equipo` no sirve) |
| … no puede usar la base: el usuario o la contraseña no son correctos | `bd_usuario` y `bd_contrasena` |
| … no puede usar la base: no llega al servidor de la base | `bd_servidor` y `bd_puerto`, y que la base admita la IP del hosting |
| … no puede usar la base: a la tabla de códigos le faltan columnas | La tabla es de una versión anterior de la 13: vuelve a aplicarla (es repetible) |
| … Falta un permiso para borrar las solicitudes de más de 30 días; pide a root: GRANT DELETE … | Todo funciona, pero no se borran las solicitudes viejas: que root ejecute esa orden (es el `GRANT DELETE` del final de `db/13`) |
| … pero el correo … falló: el servidor no aceptó la cuenta | `smtp_usuario` y `smtp_contrasena`: la contraseña de aplicación de Gmail (16 letras), no la de la cuenta |
| … pero el correo … falló: no se pudo conectar con smtp.gmail.com:587 | El hosting no deja salir al correo. Prueba el puerto 465 con `'smtp_seguridad' => 'ssl'` (si ya ibas por el 465, el mensaje te propone el 587 con `'tls'`); si tampoco, deja `smtp_servidor` vacío para usar `mail()` del hosting (llega peor: suele ir a spam) |
| … pero el correo … falló: no se pudo cifrar la conexión | Lo mismo: prueba el puerto 465 con `'smtp_seguridad' => 'ssl'` |
| … pero el correo … falló: el servidor de correo contestó algo inesperado | Lo que contestó está en `error_log` del hosting |

Lo que contestó el servidor de correo (a veces en inglés) no sale en DigBit: queda en `error_log`.

Y lo que puede ver quien recupera su contraseña:

| Mensaje | Qué pasa |
|---|---|
| Tu cuenta no tiene un correo registrado | La administración le pone uno en Editar |
| El correo de tu cuenta (x***@…) no es válido | Está mal escrito (acentos, comas, punto al final): la administración lo corrige |
| No se pudo enviar el correo con tu código | Gmail no lo aceptó o el hosting no llegó al correo: **Probar** en Ajustes dice por qué, y el detalle está en `error_log` |
| Ahora se están pidiendo demasiados códigos | Se llegó al tope total del día o de la hora (`correos_por_dia`, `correos_por_hora`); queda anotado en `error_log` |
| Se pidieron demasiados códigos desde este equipo | Más de 6 en una hora desde esa computadora (`codigos_por_equipo_hora`) |
| Ese código ya no vale: el correo de tu cuenta cambió | Se corrigió el correo después de pedirlo; que pida otro |
| El servicio de recuperación está ocupado | Muchas peticiones a la vez, o el usuario de la base llegó a su máximo de conexiones; que lo intente en unos segundos |
| La recuperación por correo todavía no está activada | No hay dirección en Ajustes (o el equipo no puede leerla: el AVISO de la 13) |
| La contraseña nueva no puede ser solo espacios | La misma regla que DigBit: que escriba otra |
| Ahora no hay conexión con el servidor… | El equipo no llega a la base, así que no sabe la dirección de la API |

Si el hosting da consola (SSH), `php probar_correo.php tu@correo.mx` manda un
correo de prueba de verdad con la configuración de `config.php`. Si guardaste
`config.php` fuera de la carpeta pública, pon su ruta detrás (desde la consola
no se lee el `SetEnv` del `.htaccess`):
`php probar_correo.php tu@correo.mx /home/<usuario>/digbit_config.php`.

## Archivos

```
recuperar.php        La API: solicitar, confirmar y probar (lo único que se abre desde fuera)
config.example.php   Plantilla de config.php
lib/comun.php        Configuración, base de datos, respuestas, contraseñas
lib/correo.php       El correo del código y un cliente SMTP pequeño (STARTTLS o SSL, AUTH LOGIN), sin librerías
correo/              El diseño del correo (plantilla.html) y los logos que van dentro
probar_correo.php    Prueba del correo desde la consola
.htaccess            Cierra todo menos recuperar.php
```
