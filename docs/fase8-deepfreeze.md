# Fase 8 — Deep Freeze y el reinicio de cada semestre

Los equipos de los laboratorios del TESCHI tienen **Deep Freeze** (Faronics), y
la imagen se rehace **cada semestre**. Los dos hechos cambian el despliegue más
que ninguna otra decisión técnica de este proyecto, y conviene entender por qué
antes de tocar una máquina.

## Qué hace Deep Freeze

Con el disco **congelado** (*Frozen*), Windows funciona con normalidad durante
la sesión: se pueden crear archivos, instalar programas, cambiar el registro.
Pero en el siguiente arranque el disco vuelve, bloque a bloque, al estado que
tenía cuando se congeló. No es borrar lo nuevo: es que lo nuevo nunca llegó a
existir de forma permanente.

Para que un cambio quede hay que **descongelar** (*Thawed*), reiniciar, hacer el
cambio, y volver a congelar.

## Lo bueno: es el mejor aliado del kiosco

Casi todo lo que nos preocupaba del modo kiosco deja de importar:

| Riesgo | Con Deep Freeze |
|---|---|
| Un alumno consigue salirse y rompe algo | Se arregla solo al reiniciar |
| Alguien borra `C:\DigLab` o el servicio | Vuelve al reiniciar |
| Defender pone `DigLab.exe` en cuarentena | La exclusión y el ejecutable vuelven al reiniciar |
| Alguien cambia el shell en el registro | Vuelve al reiniciar |
| La imagen se degrada con el uso | Nunca se degrada |

Un equipo congelado y bien configurado es, a efectos prácticos, **inmune**. Eso
vale más que cualquier directiva de grupo, y es la razón por la que el enfoque
de DigLab como shell de Windows es defendible aquí aunque en un equipo normal
sería frágil.

## Lo malo: se pierden los datos del día a día

Deep Freeze no distingue entre basura que dejó un alumno y una bitácora que
todavía no llegó al servidor. Borra las dos. Sin configurar nada, DigLab
escribe cuatro cosas en el disco del sistema:

| Qué | Dónde iba | Se pierde al reiniciar |
|---|---|---|
| Bitácoras registradas sin servidor | `%LOCALAPPDATA%\DigLab\bitacoras_pendientes.xml` | **Sí, y son datos de alumnos** |
| Copia local del horario | `%LOCALAPPDATA%\DigLab\horario_local.xml` | Sí |
| Registros de DigLab | `%LOCALAPPDATA%\DigLab\logs` | Sí |
| Registros del vigilante | `%ProgramData%\DigLab\logs` | Sí |

La primera fila es pérdida de información real. La segunda es peor de lo que
parece: **el modo sin conexión de la fase 4 deja de funcionar**. Toda su gracia
es que un equipo sin red pueda validar horarios con la última copia descargada,
y esa copia se borraría cada noche; sólo existiría dentro de la misma sesión en
la que se descargó, que es justo cuando había red.

### La solución: `-CarpetaDatos`

DigLab y el vigilante leen un ajuste `CarpetaDatos` (`appSettings`, o la variable
de entorno `DIGLAB_CARPETA_DATOS`, que tiene prioridad) que apunta a una carpeta
**fuera del disco congelado**: un **ThawSpace** de Deep Freeze (la unidad virtual
persistente que crea el propio producto, por convención `T:`) o una partición
que no esté congelada.

```powershell
.\deploy\configurar_equipo.ps1 -Cuenta laboratorio -Contrasena LaContrasena `
    -Laboratorio "Laboratorio de Computo 1" `
    -CadenaConexion "Database=teschi_otru;Server=SERVIDOR;User Id=diglab;Password=xxx" `
    -CarpetaDatos T:\DigLabDatos
```

El guion crea la carpeta, le da permiso de escritura a `Usuarios` y a `SYSTEM`
(por SID, no por nombre: en Windows en español el grupo se llama `Usuarios`), y
escribe el ajuste en los **dos** archivos de configuración, el de DigLab y el
del vigilante.

Si el equipo tiene Deep Freeze y **no** se indica `-CarpetaDatos`, el guion se
detiene. Es deliberado: un aviso se ignora, y lo que está en juego son bitácoras
de alumnos. Para un equipo que nunca se queda sin servidor y donde se acepte
perder los registros, `-AceptarSinPersistencia` sigue adelante.

Si la carpeta configurada no se puede escribir (el ThawSpace no existe, la
unidad cambió de letra), DigLab **no se cae**: vuelve al perfil del usuario y lo
deja dicho en la primera línea del log. Perder los registros es malo; no
arrancar en mitad de una clase es peor.

## El procedimiento, en orden

El orden importa. Configurar un equipo congelado termina en verde y no deja
nada, y ése es el fallo más caro de diagnosticar porque todo *parece* correcto.

1. **Descongelar.** `Mayús` + doble clic en el icono de Deep Freeze junto al
   reloj, contraseña, **Boot Thawed**, reiniciar.
2. **Comprobar que está descongelado.** `deploy\diagnostico_equipo.ps1` lo dice
   en su sección 0. `configurar_equipo.ps1` se niega a seguir si detecta el
   disco congelado.
3. **Crear el ThawSpace** si no existe (se hace desde la consola de Deep Freeze,
   no desde aquí).
4. **Configurar**: `configurar_equipo.ps1` con `-CarpetaDatos`.
5. **Probar de verdad**, todavía descongelado: reiniciar, entrar, registrar una
   bitácora, comprobar que aparece el escritorio, y mirar el diagnóstico. Una vez
   congelado ya no se corrige nada sin repetir el ciclo entero.
6. **Congelar.** Boot Frozen, reiniciar.
7. **Confirmar que sobrevivió.** Volver a pasar el diagnóstico: la marca
   `C:\DigLab\.instalado` sigue ahí después de un reinicio.

### Cómo saber si un disco conserva lo que se escribe

`configurar_equipo.ps1` deja una marca con la fecha de instalación en
`C:\DigLab\.instalado`. El diagnóstico la compara con la hora del último
arranque de Windows: si la marca sigue ahí **después** de un reinicio posterior
a la instalación, el disco conservó lo escrito. Es la única comprobación que no
depende del fabricante ni de que `DFC.exe` conteste, y por eso la más fiable,
pero necesita que haya pasado un reinicio, así que sólo sirve al diagnosticar.

Para saberlo antes, `deploy\deepfreeze.ps1` busca el servicio `DFServ` y
pregunta a `DFC.exe get /ISFROZEN` (devuelve 0 descongelado, 1 congelado). Dos
avisos. En la edición Enterprise `DFC.exe` pide una contraseña con permisos de
línea de comandos, y cuando no puede contestar el guion devuelve "no se sabe"
en vez de inventárselo. Y `DFC.exe` vive en `System32` en los Windows de 32 bits
y en **`SysWOW64`** en los de 64, así que hay que mirar las dos rutas: es la
misma trampa de redirección WOW64 que nos dejó la pantalla en negro al lanzar
`explorer.exe` (ver `docs/fase3-diseno.md`).

## El reinicio de cada semestre

La imagen se rehace cada semestre, así que la instalación **no** es algo que se
hace una vez y se olvida: hay que poder repetirla de cero, deprisa y sin
recordar nada. De ahí que todo esté en guiones y no en una lista de pasos
manuales.

Lo que hay que rehacer en cada imagen nueva:

- `configurar_equipo.ps1` con los mismos parámetros (guardar la línea exacta de
  cada laboratorio).
- La exclusión de Defender, que va dentro del guion.
- El ThawSpace, si la herramienta de imagen no lo conserva.

Lo que **no** hay que rehacer: la base de datos, que vive en el servidor y no en
los equipos, y el contenido de la carpeta de datos, que por definición está
fuera de la imagen.

Conviene dejar en la imagen base, ya congelada, un equipo por laboratorio
configurado y probado, y clonar desde ahí. Y guardar en un sitio accesible, no
sólo en este repositorio, la línea de `configurar_equipo.ps1` de cada
laboratorio: quien rehaga la imagen dentro de seis meses puede no ser quien la
hizo hoy.

## Sobre las directrices de dominio

En estos equipos no se aplican con rigor las directrices de dominio, y eso juega
a favor: las exclusiones de Defender que pone `configurar_equipo.ps1` no
deberían chocar con una directiva de grupo que las bloquee, cosa que en un
dominio estricto habría que pedir a quien lo administre. Si algún día se
endurece, el guion ya avisa cuando la exclusión no llega a quedar puesta.

Lo que no cambia es que `DigLab.exe` **no está firmado** y Defender lo detecta
como `Trojan:Win32/Bearfoos.A!ml`. La exclusión es un parche; la solución de
fondo sigue siendo firmar el ejecutable con un certificado de firma de código.

## Si los equipos están en el dominio: la contraseña de la cuenta de máquina

Esto no es de DigLab, pero rompe el arranque entero y aparece a los treinta días
de congelar, así que conviene saberlo antes.

Un equipo unido a un dominio cambia solo la contraseña de su **cuenta de
máquina** cada 30 días por defecto. Con Deep Freeze, el equipo la cambia, se lo
comunica al controlador de dominio, y al reiniciar **vuelve a la contraseña
antigua**, que ya no sirve. El resultado es el clásico *"La relación de
confianza entre esta estación de trabajo y el dominio principal ha fallado"*, y
nadie puede iniciar sesión.

Faronics lo documenta y la solución estándar es impedir ese cambio en los
equipos congelados, con una directiva o con el registro:

- Directiva de grupo: *Configuración del equipo, Configuración de Windows,
  Configuración de seguridad, Directivas locales, Opciones de seguridad,*
  **Miembro de dominio: deshabilitar los cambios de contraseña de cuenta de
  equipo**.
- O el valor `DisablePasswordChange = 1` en
  `HKLM\SYSTEM\CurrentControlSet\Services\Netlogon\Parameters`.

Como alternativa menos tajante, `MaximumPasswordAge` admite alargar el plazo en
vez de desactivar el cambio.

Si el equipo ya perdió la confianza, se arregla restableciendo la contraseña de
la cuenta de máquina (`Reset-ComputerMachinePassword`) **descongelado**, y
volviendo a congelar.

Conviene confirmar primero si los equipos del TESCHI están realmente unidos a un
dominio. Si trabajan con cuentas locales, nada de esto aplica.
