# Fase 3 — Liberar el escritorio y cerrar la sesión a la hora de salida

## Objetivo

En el laboratorio (DigLab como shell de Windows, modo kiosco):

```
Encendido → DigLab (shell) → login del alumno → código → bitácora guardada
        → DigLab LIBERA EL EQUIPO: lanza explorer.exe, se esconde y queda vigilando
        → el alumno usa Windows con normalidad
        → 5 min antes de la hora de salida: aviso "guarda tu trabajo"
        → a la hora de salida: CIERRE DE SESIÓN de Windows
        → autologon → DigLab (shell) → login del siguiente alumno

Con "equipo propio" marcado el equipo NO se libera:
        → bitácora guardada → apagar el equipo, o volver al login
```

Fuera del kiosco (modo escritorio, para desarrollar) nada de esto toca Windows:
no se lanza explorer, no se cierra sesión; al vencer la hora DigLab vuelve al
login.

## Piezas

### 1. `SesionActiva` (archivo de traspaso) — compartido por ambos procesos

`%ProgramData%\DigLab\sesion_activa.txt`, formato `clave=valor`, una línea por
campo, fechas en ISO 8601 con zona (`"o"`). Lo escribe DigLab al liberar el
equipo; lo lee el vigilante cada 10 s.

```
version=1
usuario=20230001
codigo=AB12c
equipo=PC-3
sesionWindows=1                 id de sesión de Windows donde corre DigLab
arranqueSistema=2026-09-18T07:00:12.0000000-06:00   (ahora - uptime)
inicioLocal=2026-09-18T08:00:00.0000000-06:00
finLocal=2026-09-18T10:00:00.0000000-06:00          VentanaCodigo.FinEnRelojLocal
escrito=...
latido=...                      DigLab lo renueva cada 30 s mientras vive
```

Reglas de vigencia (las aplican los dos lados, igual):

- **Caducado**: `ahora > finLocal + 10 min` → se ignora y se borra.
- **De otro arranque**: `|arranqueSistema − arranque actual| > 60 s` → es de
  antes de un reinicio: se ignora y se borra. (Los ids de sesión de Windows se
  repiten entre arranques; por eso no bastan.)
- **De otra sesión de Windows**: `sesionWindows ≠ sesión de consola activa` →
  se ignora (no se cierra una sesión que no es la del alumno).

### 2. DigLab: `Infraestructura\SesionEquipo` (en el proceso shell)

`Liberar(ventana, usuario, codigo)` se llama cuando `Registro_Bitacora` guarda:

1. Escribe `SesionActiva` con `finLocal = ventana.FinEnRelojLocal`.
2. Muestra un icono en la bandeja con la cuenta atrás ("Sesión hasta 10:00 —
   45 min") y un menú con **"Cerrar mi sesión ahora"** (alumno que se va antes).
3. **Solo en kiosco**: lanza `C:\Windows\explorer.exe` (al no haber shell,
   arranca el escritorio completo), **espera a confirmar que apareció**, y sólo
   entonces oculta todas las ventanas de DigLab. El proceso sigue vivo: si
   muriera, Shell Launcher lo relanzaría con el login encima del escritorio.

   Tres detalles que costaron una noche de pruebas en una máquina virtual, y que
   conviene no deshacer:

   - **La ruta tiene que ser absoluta.** DigLab se compila AnyCPU y MSBuild le
     pone `Prefer32Bit` por defecto, así que en un Windows de 64 bits corre bajo
     WOW64. Pedir `explorer.exe` a secas hace que `CreateProcess` lo busque en la
     carpeta de sistema, que para un proceso de 32 bits se redirige a
     `SysWOW64`, y **ahí hay otro `explorer.exe`** de 32 bits que no puede ser el
     shell: arranca, no pinta nada y se muere en milisegundos. El síntoma era una
     pantalla completamente negra tras guardar la bitácora. La carpeta de Windows
     no está sujeta a esa redirección, así que dar la ruta entera resuelve al
     bueno.
   - **Se comprueba la ventana de shell, no el proceso.** `GetShellWindow()`
     devuelve la ventana que explorer registra al montar el escritorio. Contar
     procesos llamados "explorer" no vale: el de SysWOW64 también se llama así.
     Y contarlos en *todo el equipo* era peor, porque un encargado con su sesión
     abierta por "Cambiar de usuario" hacía creer a DigLab que el alumno ya tenía
     escritorio.
   - **Nunca esconderse antes de confirmar.** Si se oculta primero y el
     escritorio no arranca, el alumno se queda con la pantalla en negro y sin
     ninguna salida. Ahora se reintenta tres veces con diez segundos de espera y,
     si aun así falla, aparece una pantalla que dice que la bitácora quedó
     registrada y ofrece reintentar.
4. Cada 30 s renueva `latido` y actualiza el icono.
5. A `fin − 5 min`: ventana de aviso no modal, siempre encima.
6. A `fin`:
   - kiosco: `ExitWindowsEx(EWX_LOGOFF | EWX_FORCEIFHUNG)` como **respaldo** por
     si el vigilante no está instalado (con `DIGLAB_SIMULAR_LOGOFF=1` solo lo
     registra en el log, para probar en un equipo de desarrollo). Si el cierre
     es simulado o `ExitWindowsEx` falla, vuelve al login; en kiosco eso deja
     el login a pantalla completa sobre el escritorio, que es lo que toca;
   - escritorio: registra en el log y vuelve al login (`CerrarSesion`).

`Reanudar()`: al arrancar, si hay una `SesionActiva` vigente **de este mismo
arranque y sesión de Windows** y el kiosco está activo, DigLab no muestra el
login: vuelve directamente al estado "equipo liberado" (icono + cuenta atrás,
explorer si no está corriendo). Cubre que Shell Launcher relance DigLab tras un
cierre o un fallo sin obligar al alumno a registrarse otra vez (que además le
daría "ya registraste tu bitácora"). Corre **antes** de la prueba de conexión
con el servidor: el traspaso es local, y si la red falló justo entonces no hay
que tapar el escritorio del alumno con la pantalla de "sin conexión". Por lo
mismo, se puede probar sin base de datos.

`ApagarEquipo()`: apaga la máquina. Lo pide la pantalla del alumno que trajo su
propia computadora (ver 2 bis). Habilita `SeShutdownPrivilege` en el token del
proceso (la cuenta de laboratorio **sí** puede apagar, pero Windows entrega el
privilegio presente y deshabilitado) y llama a
`ExitWindowsEx(EWX_POWEROFF | EWX_FORCEIFHUNG)`. Fuera del kiosco devuelve
`false` sin tocar nada, para que un equipo de desarrollo no se apague; dentro
del kiosco, `DIGLAB_SIMULAR_APAGADO=1` hace lo mismo. Si devuelve `false`, quien
llama vuelve al login.

### 2 bis. El alumno que trajo su propia computadora

Marcar *equipo propio* en la bitácora **no** libera el equipo del laboratorio.
Antes sí lo hacía, y eso dejaba la máquina con el escritorio abierto, la sesión
iniciada y nadie delante hasta el final de la clase: cualquiera se sentaba y la
usaba. Ahora `Registro_Bitacora.TerminarConEquipoPropio` guarda la bitácora y
abre `RegistroExitoso`, a pantalla completa en kiosco y sin forma de cerrarla:

```
Bitácora guardada con "equipo propio"
   │   (no se escribe sesion_activa.txt, no se lanza explorer, no hay bandeja)
   ├─ "Apagar el equipo"  → SesionEquipo.ApagarEquipo()
   ├─ "Volver al inicio"  → AppContexto.CerrarSesion()
   └─ 30 s sin tocar nada → AppContexto.CerrarSesion()
```

La cuenta atrás existe para que la máquina no se quede en esta pantalla cuando
el alumno simplemente se levanta y se va.

### 3. `DigLab.Vigilante` (servicio de Windows, LocalSystem)

Proyecto aparte en la solución, sin dependencias externas. Existe porque el
alumno **no puede matarlo**: corre en la sesión 0 como SYSTEM.

Cada 10 s:

1. Lee `SesionActiva`. Si no hay, o no es vigente por las reglas de arriba, no
   hace nada (y borra la caducada / de otro arranque).
2. Si `ahora ≥ finLocal`: `WTSLogoffSession(sesionWindows)`, registra en el log
   y borra el archivo.
3. Si `ahora ≥ finLocal − 5 min` y el `latido` de DigLab tiene más de 90 s
   (DigLab ya no está para avisar): `WTSSendMessage` a la sesión del alumno con
   el aviso. Una sola vez por sesión.

El alumno tiene permiso de escritura sobre el archivo, así que el vigilante
**recuerda** la sesión que aceptó y manda lo recordado:

- Si el archivo desaparece antes de la hora, cierra igual a la hora recordada.
- La hora de salida sólo puede **acortarse**. Un archivo con una hora posterior
  se ignora (y se registra una vez), tenga el mismo código u **otro
  usuario/código**: un alumno distinto llega siempre tras un cierre de sesión
  de Windows.
- Ese cierre (o el inicio de sesión siguiente) se lo notifica Windows al
  servicio (`OnSessionChange` con `CanHandleSessionChangeEvent`); con él olvida
  la sesión recordada y borra el traspaso. Cubre también que Windows reutilice
  el número de sesión; si el número cambia, basta la regla "de otra sesión".
  En modo `--consola` estas notificaciones no llegan.

Al arrancar crea `%ProgramData%\DigLab` con permiso de **modificar para
`Usuarios`** (la cuenta de laboratorio es estándar y tiene que poder escribir
el traspaso) y escribe su log en `%ProgramData%\DigLab\logs\vigilante-AAAAMMDD.log`.

Modos de ejecución:

- `DigLab.Vigilante.exe` sin argumentos: como servicio (`sc start`).
- `--consola`: el mismo bucle en primer plano, para probar sin instalar.
- `--consola --simular`: además, nunca cierra sesión ni envía avisos; solo lo
  escribe en el log. **Es el modo para probar en un equipo de desarrollo.**

Instalación (fase 5, pero el script va ya): `deploy\instalar_vigilante.ps1`
(`sc create DigLabVigilante binPath= ... start= auto` + `sc start`).

## Por qué dos vigilantes

| | DigLab (en sesión) | Servicio (sesión 0, SYSTEM) |
|---|---|---|
| Puede mostrar UI bonita | Sí | Solo `WTSSendMessage` |
| Lo puede matar el alumno | Sí (si el Administrador de tareas no está bloqueado) | No |
| Sobrevive a un relanzamiento del shell | Con `Reanudar` | Siempre |
| Necesita instalación | No | Sí (una vez por equipo) |

DigLab hace la experiencia; el servicio garantiza el cierre.

## Lo que NO cubre esta fase

- Bloquear `Ctrl+Alt+Supr`, Administrador de tareas, `Win+R`, etc.: directivas
  de la fase 5.
- Modo degradado sin base de datos: fase 4.
- Qué hace el alumno que se va antes y llega otro: el segundo se registra tras
  "Cerrar mi sesión ahora" del primero, o tras el cierre automático.

## Riesgos conocidos

- `Environment.TickCount` da la vuelta a los 49,7 días de encendido continuo;
  los equipos de laboratorio se apagan a diario. Si no fuera así, cambiar a
  `Win32_OperatingSystem.LastBootUpTime`.
- Si el reloj del equipo se cambia **después** de liberar, `finLocal` ya está
  fijado en hora local: adelantar el reloj cierra antes; atrasarlo, después. El
  vigilante como SYSTEM podría releer el servidor, pero eso exige darle la
  cadena de conexión; se decide en la fase 4.
