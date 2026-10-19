# Pendientes por aplicar en DigBit

Lo que el Capítulo III (segunda versión, 2026-10-12) pide y DigBit todavía no hace, o hace solo en parte. Salió de comparar cada requerimiento con el código de la rama rediseno-y-instalador. Entre paréntesis, los requerimientos del capítulo de los que viene cada punto.

**Estado al 2026-10-16 (sin commit, rama rediseno-y-instalador).** Entre el 2026-10-14 y el 16 se aplicaron tres tandas de mejoras de uso y de estos pendientes, cada parte implementada, revisada y corregida por separado. Están **hechos** los puntos 3, 4 (cumplido como está, por decisión del usuario), 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 29, 30 y 33 (cada uno dice cómo quedó), y **en parte** el 2, el 5, el 27 y el 28 (cada uno dice lo que falta). Las migraciones nuevas db/15 a db/21 solo están aplicadas en la base local de pruebas. Al final, lo que quedó de la auditoría de uso.

## Lo que pidieron el usuario y los interesados

### 1. Apartado de Estadísticas (asistencias «entró X de N veces», reportes, por periodo) (RF140, RF141, RF142)

**Hoy:** No existe: el panel tiene cinco secciones (DigBit/PrincipalAdministrador.cs:314-336) y no hay pantalla ni consulta de estadísticas. Lo más parecido es la pestaña Faltas (un mes por laboratorio, DigBit/PestanaCalendario.cs) y las Alertas de los últimos 7 días.

**Cómo aplicarlo:**

- Agregar una sexta tarjeta «Estadísticas» en PrincipalAdministrador.CrearSecciones que abra una ventana con filtros de periodo, laboratorio y profesor; contar «X de N» clases con bitácoras sobre las que tocaban con HorariosDatos.LeerCalendario (el mismo criterio de Faltas y Alertas) y las bitácoras y fallas por área, laboratorio y equipo con las consultas por sesión (DigBit/conexion/Consultas.Sesiones.cs), con descarga en PDF como los demás reportes. Si «N» son los alumnos de cada grupo, antes hay que cargar las listas de grupo: desde el 2026-09-29 el alumno ya no declara su grupo (docs/fase6-horarios.md).
- Crear la sección "Estadísticas" en el panel del administrador, sobre PantallaAdmin, con selector de periodo (semana, mes, semestre o rango) y de laboratorio. Será una tarjeta más en PrincipalAdministrador.CrearSecciones, líneas 314-336, que hoy reparte cinco columnas al 20 %, así que hay que rehacer esa fila. Llamar una sola vez a HorariosDatos.LeerCalendario(desde, hasta, false), que ya trae todos los laboratorios y profesores, y contar por ProfesorId (y por LaboratorioId si se eligió uno) las clases ConBitacoras, las que Falta y las Cancelada; así se respetan todas las correcciones de Faltas. LeerCalendario admite hasta 400 días por lectura (DiasMaximosCalendario), suficiente para un semestre. No requiere migración.
- Por clase (grupo y materia) y periodo: N = sesiones de la clase con al menos una bitácora; X = sesiones en que registró cada alumno (COUNT(DISTINCT rb.fk_codigo_accesos) agrupado por rb.fk_usuario). Mostrar "entró X de N veces" por alumno y el resumen del grupo. Como el alumno ya no declara grupo, la lista del grupo debe salir de quienes registraron en las clases de ese grupo. Escribir las consultas sin CTE ni funciones de ventana (la base de la escuela es MariaDB 5.5) y ofrecer la descarga en PDF con la plantilla actual.
- Partir de la consulta de AlertasDatos.LeerFallas sin el límite de días y agrupar también por rb.numero_computadora, usando Consultas.CondicionFallaEn para hardware, software y red y Consultas.ContarSinInternet para los avisos; ordenar de los más reportados a los menos. Para abrir en Bitácoras las sesiones de un equipo, ampliar GestionBitacorasAdministrador.Filtrar con el equipo: hoy solo recibe el día, el laboratorio y "solo con fallas", y la búsqueda de Bitácoras no abarca el equipo de cada bitácora.

### 2. Aviso directo al Ing. Benitez de los cambios de horario y de las faltas (RF133, RNF79)

**Hecho en DigBit (2026-10-14/15, con db/21_historial_cambios.sql):** la tabla `historial_cambios` guarda, en la misma transacción que el cambio, cuándo, quién, qué y lo que decía antes y después: alta, cambio, fin y quitar de franjas y clases (también los cambios «desde una fecha» de db/20), cancelaciones y sesiones extra, correcciones de bitácoras, cambios de laboratorios y de su forma de entrar, y en Docentes la baja «con sus clases» y el cambio de código (sin escribir el código). Alertas tiene el tipo nuevo «Cambio de horario» (lo que hizo otra persona desde la última vez que las viste; entra en la notificación de Windows) y la pastilla los cuenta aparte. La ventana «Historial de cambios» (desde Alertas) filtra por fecha, tipo y persona; el encargado la ve en solo consulta. Desde una alerta «Nadie registró bitácora» hay «Marcar como no dada...» y «Ver en Faltas». Sin db/21 todo sigue como antes. Permisos: digbit_consulta lo lee; digbit_equipo nada.

**Lo que falta:**

- El resumen por correo al Ing. Benítez sin tener DigBit abierto: una acción nueva en la API de api/ (que ya envía correos) con la dirección en Ajustes. Necesita la API en el hosting (punto 20) y considerar RNF80.
- No se anota lo que se carga con deploy/importar_horarios.ps1 ni lo que se cambie desde un DigBit anterior.

### 3. Dictado del código por voz (RF63, RNF14)

**Hecho (2026-10-14):** botón «Escuchar el código» junto al código en el panel del profesor (TarjetaCodigo), en «Mi horario» (CuadroCodigo) y en «Código de esta clase» del administrador (DialogoAccesoClases). DigBit/DictadoCodigo.cs usa System.Speech con una voz en español instalada (es-MX de preferencia), velocidad reducida y pausas, lee el código carácter por carácter con el mismo texto que el deletreo y lo repite una vez; no congela la ventana, se detiene al pulsar otra vez o al cerrar. Sin voz en español o sin bocinas, el botón queda apagado con una ayuda que explica cómo agregar la voz. Nunca en el kiosco ni en la pantalla del alumno. Desde el 2026-10-15 el código se muestra en mayúsculas y el deletreo solo distingue lo que se confunde (no dice «mayúscula» ni «minúscula»: el código no las distingue). Falta probarlo en los equipos de la escuela (Lenovo ThinkCentre M700 y Dell OptiPlex AIO 7410): si solo tienen voces OneCore, el botón saldrá apagado.

### 4. Configurar color: que el profesor vea y elija su color (RNF07)

**Cumplido como está (decisión del usuario, 2026-10-15):** el color de cada docente en Horarios lo elige la administración en Docentes (SelectorColorProfesor: 18 colores con nombre, «Otro...» o automático; db/12, usuarios.color_horario) y se ve en Horarios, Clases, Docentes, el panel «Los laboratorios ahora» y «Mi horario». Lo que el docente configura son sus clases: con db/19 (personalizacion_clases) elige, para cada una, un color, una imagen y un nombre corto que solo cambian lo que se ve en su panel, Mi horario y Mis grupos, y la imagen y el nombre de la clase en la pantalla del estudiante («Personalizar tu clase»); sin elegir, cada materia sale de un color automático. No cambia ningún dato de la clase que hizo la administración. Los colores de la interfaz (verde oscuro y blanco) siguen fijos en el código.

### 5. Acceso de los encargados de laboratorio y del servicio social a las fallas (Stakeholders)

**Hoy (2026-10-15, sin commit):** Ya existe el tipo 4, «encargado de laboratorio» (TiposUsuario.Encargado en DigBit/Infraestructura/Permisos.cs), con su migración db/18_encargados.sql. Entra al panel del administrador en solo consulta (Permisos.Bloquear en cada acción y Permisos.Exigir en la capa de datos) y, en cualquier equipo, su sesión va con la cuenta de MySQL de solo lectura digbit_consulta (DigBit/conexion/CuentaDeConsulta.cs). Tiene «Mi perfil» (desde el 2026-10-14): cambia su correo y su contraseña, escribiendo solo su propia fila con la cuenta del equipo; su nombre lo cambia el administrador. Ve el historial de cambios. La administración los da de alta en Docentes > «Responsables de laboratorio...». Sigue sin haber pantalla para dar de alta a otro administrador.

**Lo que falta:**

- Aplicar db/18 en sql.teschi.edu.mx con root, junto con la 07 (punto 20): sin digbit_consulta los encargados no pueden entrar. Antes, actualizar DigBit en los equipos donde vayan a entrar.
- Recuperación por correo del encargado: api/recuperar.php ya acepta el tipo 4; vale en cuanto la API esté en el hosting (punto 20).
- La marca «Atendida» de las fallas (atendida_por y atendida_en) no se hizo: el encargado no escribe nada en la base. Si se pide, hay que decidir con qué cuenta la guarda.

### 6. Cambio de la contraseña propia y perfil del administrador (RF10)

**Hecho (2026-10-14):** «Mi perfil» (editar_Perfil.cs, rehecho con el estilo de DigBit) tiene la sección «Contraseña»: la actual, la nueva y su repetición (Contrasenas.Verificar y Contrasenas.Hash, nunca en el log). Cambiar el correo también pide la contraseña actual (es el canal de recuperación) y tres contraseñas actuales equivocadas cierran la sesión. Siempre edita la cuenta de la sesión. El administrador y el encargado tienen la pastilla «Mi perfil» en la cabecera de su panel. Queda una limitación: desde los equipos de alumnos y profesores (digbit_equipo) no se anulan los códigos de recuperación pendientes, que caducan solos a los 15 minutos; anularlos necesita GRANT UPDATE (usado_en) sobre recuperacion_contrasena.

## Detalles funcionales

### 7. Teclado e indicador de espera en el inicio de sesión, y lista de clases del alumno accesible con Tab (RNF19, RNF20)

**Hecho (2026-10-14):** el inicio de sesión abre con el cursor en USUARIO, Enter inicia sesión (AcceptButton), el orden de Tab es usuario, contraseña, Iniciar sesión, «¿Olvidaste tu contraseña?» y Crear cuenta, «USUARIO» y «CONTRASEÑA» son texto guía, y mientras valida el botón dice «ENTRANDO...», queda desactivado y sale el cursor de espera (la llamada sigue siendo síncrona: sin servidor la ventana no responde hasta el tiempo de espera de la conexión). La lista de clases del alumno tiene una parada de Tab con flechas, sin AcceptButton.

### 8. PDF del horario de un laboratorio (docs/fase6-horarios.md)

**Hecho (2026-10-15):** botón «Imprimir PDF» en la pestaña Horarios: la semana elegida del laboratorio con CuadriculaSemanal sin los códigos de los profesores, con el encabezado institucional, solo las franjas que esa semana se dan, y la hora de generación en el pie. Se abre en el visor (VisorPdf), desde donde se elige si se guarda.

### 9. Constancia de las correcciones de bitácoras (RNF79)

**Hecho (2026-10-14/15):** al corregir un registro, EditarBitacoraAdministrador deja una línea en el registro de diagnóstico (Log.Info, ASCII) con el administrador, la sesión, el alumno y los valores anteriores y nuevos; y, con db/21, un renglón en historial_cambios en la misma transacción (punto 2).

### 10. Reportes PDF: laboratorio en el encabezado y fecha dd/mm/aaaa (RNF62)

**Hecho (2026-10-14):** el PDF de la bitácora (una sola implementación para Bitácoras y para la bitácora de la clase, PdfBitacora en TablaAlumnosPdf.cs) lleva el renglón LABORATORIO y la fecha «Martes 29/09/2026»; PROFESOR y GRUPO van en un renglón y MATERIA a todo lo ancho, sin partir las etiquetas; el título no se encima con los logotipos; una sesión sin nadie dice «Nadie registró su bitácora en esta sesión.». El nombre del archivo es codigo_aaaa-mm-dd_HHmm_laboratorio_profesor.pdf. Al descargar se abre el visor de PDF de DigBit y desde ahí se elige si se guarda y dónde (en el kiosco solo se ve).

### 11. Indicador «(en curso)» de Bitácoras con la hora del servidor (RNF38)

**Hecho (2026-10-14):** Bitácoras lee NOW() del servidor al cargar las sesiones y calcula «(en curso)», «Hoy», «Últimos 7 días» y «Este mes» con la hora del servidor.

### 12. Letra de las tablas de 10 pt (RNF04)

**Hecho (2026-10-15):** EstiloHorarios.CrearGrid usa Century Gothic de 10 pt y la fila elegida en verde claro (ya no el azul de Windows), también en la bitácora de la clase. Queda por ajustar el ancho de algunas columnas largas que a 10 pt se cortan con «...» (Alumnos: Carrera; Profesores: Correo; Bitácoras: Laboratorio y Materia; Alertas: Detalle); el texto entero sale al pasar el ratón.

### 13. Logotipo del TESCHI en el inicio de sesión y en las cabeceras (RNF01)

**Hecho (2026-10-14/15):** el logotipo del TESCHI está en el inicio de sesión (arriba de los campos) y en las cabeceras del alumno, del profesor y del administrador y encargado, junto a «Logos» y con una raya entre ellos, sin tapar la sesión, las pastillas ni la fecha a 1366×768.

### 14. Opción segura por defecto en las confirmaciones de Horarios que faltan (RNF13)

**Hecho (2026-10-15):** en Horarios, las confirmaciones de quitar una franja, desactivar una clase y quitar una cancelación tienen «No» o «Cancelar» por defecto: Enter cancela. Además Esc cierra Horarios (por pasos: primero suelta lo elegido) y F5 actualiza.

### 15. Estilo común en «Mi perfil» y en el diálogo «Acceso de emergencia» (RNF02, RNF03)

**Hecho (2026-10-14):** «Mi perfil» se rehízo con EstiloHorarios y EstiloAdmin (Century Gothic, verde oscuro, sin los RJControls); el diálogo «Acceso de emergencia» tiene el título en verde oscuro y Cancelar con EstiloHorarios.CrearBoton.

### 16. Que Bitácoras y «Mis bitácoras» abran dentro de la pantalla a 1366×768 (RNF08)

**Hecho (2026-10-14):** PantallaAdmin limita el tamaño inicial al área de trabajo de la pantalla, así que Bitácoras, «Mis bitácoras» y Alumnos caben a 1366×768 con la barra de tareas; las columnas de Bitácoras se ajustaron para que «(en curso)» y la fecha no se corten.

### 17. Mensajes de error con causa y acción en lugar del texto técnico (RNF12)

**Hecho (2026-10-14/16):** ningún aviso enseña ya el texto técnico de .NET o de MySQL: dicen qué no se pudo hacer, la causa (sin conexión con el servidor, cuenta o permisos de la base, falta una migración, base ocupada o de solo lectura, un archivo que Windows no deja guardar, o un error inesperado) y qué hacer, con el detalle técnico solo en el log. Lo resuelven AvisoDeFalloAdmin (administración y responsables de laboratorio), AvisoDeFalloDocente (panel docente; en el kiosco dice que avises a la persona responsable del laboratorio), PantallaAdmin.MostrarError y los avisos propios del inicio de sesión, «Mi perfil», el estudiante y el kiosco. Los avisos que escribe DigBit para quien los lee (ErrorDeDatos, AccionNoPermitida y los de HorariosDatos, reconocidos con EstiloHorarios.EsAvisoDeDigBit) se siguen enseñando tal cual; uno de MySql.Data en inglés ya no. «Probar» de la recuperación por correo en Ajustes tampoco enseña la respuesta en inglés de Gmail: la API dice la causa (conexión, cifrado, cuenta, remitente, destinatario...) y qué revisar en su config.php.

**Lo que queda:** sin conexión, una misma causa puede dar varios avisos seguidos al abrir Horarios o «Nueva clase» (ya pasaba antes).

### 18. Fechas y horas que no dependan de la configuración regional de Windows (RNF62)

**Hecho (2026-10-14/15):** Program.cs fija es-MX como cultura de la aplicación al arrancar (sin los cambios regionales del usuario de Windows), en el hilo actual y en los que se creen; los selectores de fecha de Bitácoras y de Horarios usan Format = Custom con «dd/MM/yyyy»; la copia local y las claves de las sesiones sin conexión escriben las fechas sin depender de la cultura.

## Instalación y operación

### 19. Instalar, actualizar y diagnosticar los equipos sin depender de PowerShell (RNF40, RNF44, RNF47)

**Hoy:** DigBit-Instalador.exe instala ejecutando por debajo deploy/configurar_equipo.ps1 (DigBit.Instalador/FormInstalador.cs:810). Su botón «Solo diagnosticar» lanza deploy/diagnostico_equipo.ps1 y muestra la salida en la ventana (FormInstalador.cs:297-300). La lista de laboratorios se consulta con un .ps1 temporal (FormInstalador.cs:655-715), y la opción de montar un servidor usa preparar_bd.ps1 (FormInstalador.cs:1003-1090). No tiene opción de actualizar: la actualización solo existe como deploy/actualizar_digbit.ps1.

**Cómo aplicarlo:**

- Hacer en C#, dentro del propio .exe, los pasos de configurar_equipo.ps1: copiar DigBit a su carpeta y dejarla de solo lectura para la cuenta del laboratorio (System.Security.AccessControl), escribir connections.config y los appSettings, crear la cuenta local (System.DirectoryServices.AccountManagement), poner las claves de Winlogon y las directivas (Microsoft.Win32.Registry), la exclusión de Defender, instalar el vigilante con sus acciones de recuperación y detectar Deep Freeze; la lista de laboratorios, con MySql.Data desde el instalador. Antes, confirmar con el usuario en qué equipos falla y con qué error; los .ps1 quedan como herramientas de desarrollo y diagnóstico.
- Primero, confirmar con el usuario en qué PCs fallan los .ps1 y con qué error. Después, pasar a C# dentro de DigBit.Instalador: los pasos de configurar_equipo.ps1 (permisos de carpeta, claves del registro, cuenta local, inicio automático, exclusión en Defender, servicio vigilante, memoria de emergencia y detección de Deep Freeze); la consulta de laboratorios, con el MySql.Data.dll de la carga; y las comprobaciones que hoy lanza «Solo diagnosticar». Añadir además el modo «Actualizar»: detener DigBit y el vigilante, copiar, conservar connections.config y los appSettings, comprobar el ejecutable y negarse si Deep Freeze está congelado. Los .ps1 quedarían como herramientas de desarrollo.

### 20. Usuarios de MySQL con permisos mínimos en el servidor de la escuela, y la API con el suyo (RNF53 a RNF55)

**Hoy:** La API (api) está escrita y se probó el 2026-10-09 desde la PC de desarrollo con la base y Gmail reales, usando de forma provisional el usuario de base de datos de los equipos; falta alojarla en un hosting con https que llegue a sql.teschi.edu.mx, crear digbit_api y guardar su dirección y clave en Ajustes. Mientras no esté, «¿Olvidaste tu contraseña?» pide acudir al administrador de DigBit (README.md:728-739).

**Cómo aplicarlo:**

- Pedir al responsable del servidor (usuario root) que ejecute db/07_usuarios_minimos.sql con contraseñas nuevas y, en @desde, la IP o el rango desde el que salen los equipos de la escuela; regenerar DigBit-Instalador.exe y volver a configurar los equipos (descongelados): digbit_equipo en las máquinas de laboratorio y de profesores, digbit_admin solo en el equipo del administrador. En la misma visita, que aplique db/18_encargados.sql, db/19_personalizacion_clases.sql y db/21_historial_cambios.sql (la 21 crea la tabla del historial de cambios y le da SELECT a digbit_consulta; no da nada a digbit_equipo): la 18 crea digbit_consulta, sin la que los encargados de laboratorio no pueden entrar (punto 5); la 19 crea la tabla en la que cada profesor personaliza sus clases y da permiso sobre ella a digbit_equipo (la 07 también lo da si la tabla ya está).
- Para que cambiar el horario no reescriba las semanas pasadas (db/20_vigencia_franjas.sql): actualizar DigBit en todos los equipos de la escuela (laboratorios, profesores y administrador), aplicar la 20 (no cambia nada sola ni pide permisos nuevos) y solo entonces encender en Ajustes «Los cambios de horario aplican desde una fecha» (INSTALAR.md, «Migrar una base que ya existe»). Un equipo con un DigBit anterior vería a la vez la franja terminada y la nueva. Antes, revisar con el usuario las clases de la escuela desactivadas que tienen sesiones (activa = 0): esconden su pasado en Faltas y en la asistencia y la base no guarda cuándo se desactivaron; se pueden reactivar con vigente_hasta en su última sesión (o en la baja de su profesor, usuarios.baja_en), pero es una decisión suya. Las franjas que ya se borraron o se editaron en su lugar no se pueden reconstruir.
- Subir api/ al hosting de la escuela con su config.php (fuera de git), pedir a root que cree digbit_api con la receta de db/13_recuperacion_contrasena.sql:139-162, escribir en Ajustes del administrador la dirección https de recuperar.php y su clave, pulsar «Probar» y «Guardar», y regenerar el instalador para que todos los equipos tengan la versión 1.1.

### 21. Medir las metas cuantitativas en la red de la escuela (RNF11, RNF16, RNF20 a RNF27, RNF30, RNF33, RNF37)

**Hoy:** Las metas siguen en el documento, pero no hay mediciones: no se ha probado la carga de 30 equipos a la vez ni la red y el cortafuegos de la escuela (INSTALAR.md:623-628), y los 260 ms del PBKDF2 se midieron en el equipo de desarrollo (README.md:691-695). Además, horario_local.xml se reescribe completo al arrancar y cada 10 minutos con todos los alumnos activos (DigBit/conexion/CacheHorario.cs:340-364,375-419), lo que puede pesar en RNF27 cuando la base tenga a todos los alumnos.

**Cómo aplicarlo:** En un Lenovo ThinkCentre M700 con DigBit en Release: cronometrar el arranque, el inicio de sesión, la validación del código y el guardado con Stopwatch, escribiendo los tiempos en el registro (Log.Info); medir memoria, CPU y escritura en disco con el Monitor de rendimiento (contadores Working Set, % Processor Time e IO Write Bytes/sec de los procesos DigBit y DigBit.Vigilante) durante una clase de 2 horas; probar la concurrencia con un guion que inserte 120 bitácoras en el mismo minuto contra una copia de la base, nunca contra la de la escuela; comprobar desde un equipo de cada laboratorio la salida a sql.teschi.edu.mx:3306 y al HTTPS de la API; y hacer una prueba de uso cronometrada con alumnos y con un administrador. Si RNF27 no se cumple, reescribir la copia local solo cuando cambie.

## Seguridad

### 22. Que con las credenciales de un equipo no se pueda cambiar la contraseña de otra persona (RNF54)

**Hoy:** Con el usuario de MySQL de los equipos, cuya contraseña está en claro en connections.config y el alumno puede leer, se puede leer la tabla usuarios y cambiar la contraseña de cualquier alumno, profesor o encargado de laboratorio para entrar como él (README.md:856-875); el disparador solo protege a los administradores.

**Cómo aplicarlo:** Pasar el inicio de sesión y el cambio de contraseña a procedimientos almacenados, como propone README.md:863-864, de modo que digbit_equipo deje de leer y escribir directamente la columna password (los procedimientos devolverían la sal y verificarían el hash calculado por DigBit); conservar un camino aparte para la migración desde MD5 y el restablecimiento del administrador.

### 23. Contraseña del inicio automático en claro y salida por el modo seguro (RNF56, RNF57)

**Hoy:** deploy/configurar_equipo.ps1:882 guarda la contraseña de la cuenta del laboratorio en el valor DefaultPassword de Winlogon, en claro (INSTALAR.md:388-391: el secreto de LSA está diseñado pero sin escribir), y deploy/diagnostico_equipo.ps1:421-425 marca como PENDIENTE que en modo seguro Windows usa cmd.exe como shell, con lo que esa contraseña da un símbolo del sistema sin DigBit.

**Cómo aplicarlo:** Guardar la contraseña del inicio automático como secreto de LSA (LsaStorePrivateData, como hace la herramienta Autologon de Sysinternals) en lugar del valor del registro. Cerrar el modo seguro para la cuenta del laboratorio, por ejemplo cambiando el shell alterno de SafeBoot o restringiendo las opciones de arranque. Fijar SYSTEM como dueño de la clave Winlogon de esa cuenta: hoy configurar_equipo.ps1:815-869 solo cambia sus permisos, y diagnostico_equipo.ps1:337-344 solo comprueba el dueño. Añadir al diagnóstico las comprobaciones que falten.

### 24. Firmar los ejecutables y retirar la exclusión del antivirus (RNF57)

**Hoy:** DigBit.exe no está firmado y Microsoft Defender lo marca como falso positivo, por lo que el instalador excluye C:/DigBit del antivirus (INSTALAR.md:630-632; deploy/diagnostico_equipo.ps1:254-259).

**Cómo aplicarlo:** Conseguir un certificado de firma de código de la institución, firmar DigBit.exe, DigBit.Vigilante.exe y DigBit-Instalador.exe con signtool como último paso de crear_instalador.ps1 y, cuando Defender deje de marcarlos, quitar la exclusión de la carpeta y de los procesos.

### 25. Limpiar datos sensibles del historial de git y renovar la contraseña de aplicación de Gmail (RNF60)

**Hoy:** Los archivos de código muerto con una conexión antigua de desarrollo se borraron del árbol en el commit a5d24cf, pero siguen en el historial desde el commit inicial 94b46e2. También están en el repositorio público antiguo: la rama main del remoto publico-antiguo es justo 94b46e2. Los commits locales de 3bd3d15 a 2d52ff9, aún sin subir, conservan la matrícula de un alumno real en deploy/probar_esquema_teschi.ps1; el árbol de trabajo ya no la tiene. Cinco de esos commits (3bd3d15 a 7a5a99c) están también en la rama main local, que va 5 commits por delante de origin/main. Según la nota del 2026-10-09, la contraseña de aplicación de Gmail apareció en una captura del chat.

**Cómo aplicarlo:** Antes de subir cualquiera de las dos ramas (rediseno-y-instalador o main), reescribir esos commits en ambas (con git filter-repo o un rebase) para quitar la matrícula, y pedir a ese alumno que cambie su contraseña. Confirmar que la contraseña de desarrollo del historial no se usa en ningún servidor. Generar una contraseña de aplicación nueva en la cuenta de Gmail del sistema y ponerla solo en el config.php del hosting.

### 26. Retirar las contraseñas heredadas en MD5 (RNF51)

**Hoy:** Toda contraseña nueva o cambiada se guarda con PBKDF2, pero las del sistema anterior siguen en MD5 hasta que su dueño entra (DigBit/conexion/Contrasenas.cs:117-130); README.md:706-710 deja pendiente borrar ese camino.

**Cómo aplicarlo:** Al cierre del semestre, contar con la consulta final de db/06_migracion_contrasenas.sql cuántas cuentas siguen en MD5; a esas, asignarles una contraseña provisional desde Alumnos o Profesores (que ya guarda PBKDF2) o pedirles la recuperación por correo, y cuando la cuenta llegue a cero, borrar el camino MD5 de Contrasenas.cs.

## Datos personales y aspectos legales

### 27. Aviso de privacidad del TESCHI en el sistema (RNF72)

**Hecho el mecanismo (2026-10-15/16):** en Ajustes, «Aviso de privacidad» → «Configurar...» guarda el aviso simplificado (hasta 5 000 caracteres, en trozos de la tabla configuracion, sin migración nueva) y la dirección del aviso integral, con una vista previa de cómo se verá. Con texto guardado, «Crear cuenta» lo muestra con la casilla obligatoria «Leí el aviso de privacidad» (sin marcarla no se crea la cuenta; queda constancia en el log) y el enlace al integral, y «¿Olvidaste tu contraseña?» dice que los datos se tratan conforme al aviso, con el mismo enlace. En los equipos del laboratorio el enlace no abre el navegador: enseña la dirección. El correo de recuperación lleva un pie con el enlace si el config.php de la API tiene aviso_privacidad_url (api/config.example.php y api/LEEME.md; la dirección se escribe en los dos sitios, porque la cuenta de la API no puede leer la tabla configuracion). Sin aviso guardado, todo queda como antes.

**Lo que falta:** que la Unidad de Transparencia del TESCHI dé el texto (simplificado e integral), que mencione que la base se aloja en Google Cloud, que el código de recuperación se envía por Gmail y el plazo de 30 días de las solicitudes de recuperación (api/LEEME.md lista los datos que se guardan); cargarlo en Ajustes y en el config.php de la API, e incluirlo en el manual de usuario.

### 28. Atención de derechos ARCO desde el panel del administrador (RNF74)

**Hecho en parte (2026-10-15/16):** en Estudiantes y en Docentes, «Datos personales...» ofrece:

- «Exportar sus datos (CSV)...»: el perfil y, del estudiante, sus bitácoras; del docente, sus clases con horario y vigencia, sus sesiones (cuántas bitácoras, sin nombres de estudiantes) y, con db/15, las fallas que reportó. No en el kiosco. Queda en el log quién lo exportó, sin datos de la persona. También desde «Ver sus bitácoras» → «Exportar sus datos...».
- «Anonimizar la cuenta...» de una cuenta dada de baja (cancelación u oposición): pide otra vez la contraseña de la administración (tres fallos cierran la sesión) y deja «Persona ANON-<id>», sin apellido materno ni correo y con una contraseña que ya no sirve; anula sus códigos de recuperación pendientes. Sus bitácoras, sesiones y clases se conservan por id. La cuenta anonimizada no se reactiva ni se edita. Un docente con clases que siguen después de hoy no se anonimiza. Constancia en el historial de cambios, tipo «Cuentas anonimizadas» (sin db/21, solo en el log). La persona responsable de laboratorio no tiene estas acciones.

**Lo que falta:** la constancia de las solicitudes ARCO (quién pidió qué y cuándo, y cómo se atendió). El historial de cambios de antes de anonimizar a alguien puede seguir nombrándole: solo se añade, no se reescribe.

### 29. Menos datos personales en las máquinas del laboratorio (RNF71, RNF73)

**Hecho (2026-10-14):** la copia local (CacheHorario) guarda de los alumnos solo el id y la matrícula; una copia anterior con nombres se sigue leyendo y los pierde en cuanto se refresca con servidor; sin conexión, la pantalla del alumno muestra su matrícula. Queda el riesgo de que la lista de id y matrícula siga legible para la cuenta del laboratorio (documentarlo en el manual técnico). Con Deep Freeze y la carpeta de datos fuera del ThawSpace, una copia vieja con nombres vuelve en cada reinicio: actualizar el equipo descongelado y dejar que DigBit refresque la copia antes de congelarlo.

### 30. Depurar las solicitudes de recuperación antiguas (RNF73)

**Hecho (2026-10-14):** api/recuperar.php borra, al atender una solicitud, las filas de recuperacion_contrasena con más de 30 días. La cuenta de la API necesita DELETE sobre esa tabla: la receta de db/13_recuperacion_contrasena.sql ya lo incluye; mientras no lo tenga, la recuperación funciona igual y «Probar» de Ajustes muestra el GRANT que falta. Falta indicar el plazo en el aviso de privacidad (punto 27).

### 31. Correo de recuperación por un servicio externo (Gmail) (RNF80)

**Hoy:** La API de recuperación (carpeta api/, en el hosting de la escuela) envía el código por el SMTP de Gmail con la cuenta de Gmail del sistema (smtp_servidor = smtp.gmail.com en api/config.example.php): el nombre, el correo del usuario y el código pasan por un servicio externo a la red institucional, lo que RNF80 no permite tal como está redactado.

**Cómo aplicarlo:**

- Configurar en api/config.php el servidor de correo institucional del TESCHI (smtp_servidor, smtp_puerto, smtp_usuario y remitente) o dejar smtp_servidor vacío para enviar con mail() del hosting de la escuela; si la institución autoriza la cuenta de Gmail del proyecto, ajustar la redacción de RNF80 para registrarla como excepción aprobada.
- Cambiar en el config.php de la API smtp_servidor, smtp_usuario y remitente por los del correo institucional @teschi.edu.mx (ya son configurables), o ajustar la redacción de RNF80 para admitir el servicio de correo que autorice la institución.

### 32. Licencias de bibliotecas (AGPL y GPL), licencia de Deep Freeze y autoría (RNF76, RNF77, RNF78)

**Hoy:** AssemblyCompany vacío y «Copyright © 2024» sin autores (DigBit/Properties/AssemblyInfo.cs:11-13; DigBit.Vigilante/Properties/AssemblyInfo.cs:7); el instalador no declara copyright; no hay LICENSE, AUTORES ni pantalla «Acerca de»; el código está en un repositorio privado personal (github.com/Alexsandro215/DigBit-2.0).

**Cómo aplicarlo:**

- Decidir con la institución entre publicar el código con una licencia compatible y ponerlo a disposición de los usuarios, comprar las licencias comerciales, o sustituir iTextSharp por una biblioteca de licencia permisiva (por ejemplo, PDFsharp/MigraDoc, MIT) y MySql.Data por MySqlConnector; documentar la decisión en el manual técnico.
- Que el área responsable de los laboratorios adquiera o renueve la licencia de Faronics Deep Freeze, o lo retire: DigBit no depende de Deep Freeze (solo exige la carpeta de datos cuando lo detecta). Mientras tanto, no dar por cubierta ninguna defensa del kiosco con el argumento de que «un reinicio lo arregla».
- Llenar AssemblyCompany («Tecnológico de Estudios Superiores de Chimalhuacán») y AssemblyCopyright con los autores y el año en los tres AssemblyInfo.cs; agregar AUTORES.md con los residentes, los asesores y el reconocimiento al sistema base BitLab, y un apartado «Acerca de DigBit» (por ejemplo, en Ajustes); acordar con el Ing. Oscar Fernández Trujano la cesión o licencia al TESCHI, entregarle el repositorio y valorar el registro ante INDAUTOR.

### 33. Lenguaje incluyente en los textos del sistema (RNF64)

**Hecho (2026-10-15/16):** los textos de pantallas, mensajes, PDF, el correo de recuperación, README.md e INSTALAR.md usan formas incluyentes, con el trato en segunda persona: «estudiante(s)», «docente(s)» y «personal docente», «la administración (de DigBit)» y «la persona responsable del laboratorio». Las ventanas principales se llaman «DigBit - Estudiante», «DigBit - Docente», «DigBit - Administración» y «DigBit - Responsable de laboratorio», y las secciones «Estudiantes», «Docentes» y «Responsables de laboratorio». No se cambiaron los comentarios del código, el log, los nombres de tablas y columnas ni los valores guardados.

**Lo que falta:** el instalador (DigBit-Instalador.exe) sigue diciendo «Administrador / Maestro / Alumno»; se cambia al rehacerlo sin PowerShell (punto 19). Revisarlo con la política de igualdad de género del TESCHI si la tiene por escrito.

## Documentación y pruebas

### 34. Manual de usuario de la versión 1.1 y manual técnico actualizado; Bitácoras en README.md (RNF49)

**Hoy:** Hay guía de instalación (INSTALAR.md), descripción técnica (README.md), diseño por fase (docs) y guía de la API (api/LEEME.md), pero no hay manual de usuario de la 1.1. Los de «ANTIGUOS MANUALES» son del sistema anterior y están fuera de git porque llevan la contraseña del servidor (.gitignore:54-58). El README está desfasado en varios puntos: README.md:1078-1082 todavía dice que hay archivos con credenciales por borrar, y ya se borraron en el commit a5d24cf; su lista «Estado del proyecto» no recoge la versión 1.1; su «Estructura del repositorio» no menciona DigBit.Instalador, crear_instalador.ps1, empaquetar.ps1 ni los guiones importar_horarios, preparar_bd, limpiar_equipo, probar_modos y probar_esquema_teschi (README.md:902); describe probar_horarios.ps1 como «sin base de datos» (README.md:940), aunque necesita una base de desarrollo; y README.md:69 pide aplicar las migraciones «cada una una sola vez», aunque son repetibles.

**Cómo aplicarlo:**

- Escribir un manual de usuario por rol, con capturas de la 1.1: alumno (iniciar sesión, «Crear cuenta», «¿Olvidaste tu contraseña?», registrar la bitácora, equipo propio), profesor (panel, código de la clase, Mi horario, Mis bitácoras y PDF), administrador (secciones, Horarios y Faltas, Códigos de las clases, Alertas y Ajustes) y encargado o prestador de servicio social (su panel en solo consulta, llave de emergencia, diagnóstico y actualización), incluyendo el aviso de privacidad (RNF72). Armar el manual técnico con README, INSTALAR, docs y api/LEEME (arquitectura, base de datos y migraciones, despliegue y mantenimiento) y corregir los párrafos desfasados del README.
- Reescribir ese párrafo con la pantalla actual (filtros por periodo, laboratorio y "Solo con fallas"; columnas Fallas y Sin internet; botones "PDF de esta sesión", "PDF de las N sesiones" y "Corregir registro"; acceso desde las alertas) antes de usarlo como fuente del manual.

### 35. Pruebas de la API de recuperación y de los permisos de MySQL en el repositorio (RNF48)

**Hoy:** Las pruebas de la API, de los permisos de MySQL (MySQL 8 y MariaDB 5.5) y de DigBit con la recuperación se hicieron con guiones de la carpeta temporal de la sesión (C:/Users/Usuario/AppData/Local/Temp/claude/a--Digbit-2-0-Produccion/a367f222-a048-4689-bee2-1427e868f141/scratchpad/api_prueba), que no están en el repositorio. En esa misma carpeta hay otras 35 pruebas probar_*.ps1 (alertas, códigos generados, PDF, arranque, cierre por contraseña, horarios, paso 3...) que tampoco están. En el repositorio solo está api/probar_correo.php, que envía a mano un correo de ejemplo. deploy/probar_horarios.ps1 y deploy/probar_modos.ps1 se niegan a correr contra el servidor de la escuela, igual que deploy/probar_esquema_teschi.ps1:68-70.

**Cómo aplicarlo:** Pasar esos guiones, sin contraseñas ni datos reales, a deploy/ o a api/pruebas/, documentarlos en el README y añadir a probar_horarios.ps1 la misma comprobación de teschi.edu.mx.

## De la auditoría de uso (2026-10-14) que sigue sin hacer

Se hicieron casi todas sus propuestas (cierre por inactividad en el laboratorio, bandeja que dice hasta qué hora es el equipo, Mi horario por semanas, código en mayúsculas, «16 de 24 estudiantes», «Copiar la lista» y «Guardar en Excel (CSV)...» en Mis grupos, «Ayer» y «Mañana» del panel docente que saltan los días sin clases (con su fecha cuando no son ayer o mañana), Faltas que de entrada no cuenta los laboratorios que aún no usan DigBit, fallas del profesor a la vista en la bitácora de la clase, pulsar una clase en «Los laboratorios ahora», historial de cada alumno, «Ver sus bitácoras» en Docentes, Ajustes a 1366×768, nombres únicos de las formas de entrar, acentos y separadores). Quedan:

- **Mis grupos:** guardar la lista en PDF con el membrete (en CSV ya se puede; en el kiosco no se ofrece ninguna de las dos).
- **T3:** «Cerrar» y «Actualizar» siempre en el mismo sitio y con el mismo tamaño en todas las ventanas.
- **Detalles:** el enlace del alumno dice «Mi perfil (nombre, correo y contraseña)» sin la carrera, porque no cabe; la caja del código de un profesor en «Editar profesor» no pasa a mayúsculas; «Mi horario» hace una consulta más por clase al abrirse.
