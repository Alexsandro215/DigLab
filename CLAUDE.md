# DigLab: lo primero al abrir este proyecto

## ⚠ Tarea pendiente en la escuela: aplicar db/29 antes de instalar DigLab 1.3

**Estado: PENDIENTE** (la dejó el usuario el 13/11/2026: «cuando se lea este proyecto, que se haga la migración, ya que no estaré aquí para hacerla»).

La base de la escuela (servidor `sql.teschi.edu.mx`, base `teschi_otru`) tiene las migraciones hasta la `db/28` (comprobado el 13/11/2026). Le falta **`db/29_actualizaciones.sql`**, que crea tres tablas: `actualizaciones`, `actualizaciones_trozos` y `ordenes_equipos`. No toca nada más.

Sin ella, DigLab 1.3 se instala y funciona en todo lo demás. Pero no se pueden publicar versiones para que los equipos se actualicen solos, ni usar «Actualizar ya» o «Apagar equipos»: esas ventanas avisan que falta la db/29.

Hazlo en una computadora de la escuela que tenga este repositorio y llegue a la base:

1. **Confirma con la persona que está en la computadora** antes de escribir en la base: es la base real, con datos de estudiantes. La contraseña de la cuenta `teschi_otru` la teclea esa persona (es la misma que se escribe al instalar DigLab). No la escribas en ningún archivo, commit ni conversación, ni la pidas por escrito.

2. **Comprueba si ya está** (solo lectura). Pide a la persona que corra esto en una ventana de PowerShell y te diga qué sale (te pedirá la contraseña):

   ```powershell
   mysql -h sql.teschi.edu.mx -u teschi_otru -p teschi_otru -e "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name IN ('actualizaciones','actualizaciones_trozos','ordenes_equipos');"
   ```

   Si salen las tres tablas, ya está hecha: salta al paso 5.

3. **Aplícala con el guion de migraciones**, que antes respalda la base entera con mysqldump y la revisa con `db/00_revision_previa.sql`, y se detiene si algo va a fallar. Todas las migraciones son repetibles: las que la escuela ya tiene (de la 03 a la 28) solo dicen que ya estaban. Pide a la persona que lo corra desde la carpeta del repositorio:

   ```powershell
   .\deploy\migrar_bd.ps1 -Servidor sql.teschi.edu.mx -Base teschi_otru -Usuario teschi_otru -Contrasena (Read-Host 'Contrasena de teschi_otru') -BinMysql 'CARPETA_CON_MYSQL_Y_MYSQLDUMP'
   ```

   - `-BinMysql`: la carpeta donde están `mysql.exe` y `mysqldump.exe`. Si ya están en el PATH, quita ese parámetro.
   - El respaldo queda en la carpeta `respaldos` del repositorio. No se sube a git: lleva datos personales.
   - Con `teschi_otru` basta. La parte de permisos para la cuenta `digbit_equipo` (que los equipos solo puedan leer esas tablas) solo la puede hacer root. Sin root sale un AVISO, y es normal.
   - Si sale un BLOQUEA o un error, no sigas: deja la base como está y anota aquí lo que dijo.

4. **Comprueba** que ya están las tres tablas (la consulta del paso 2).

5. **Después:** instala DigLab 1.3 una sola vez en cada equipo, con `instalador/DigLab-Setup.exe` (de este repositorio) en la memoria USB. Desde entonces, las versiones nuevas se publican en Laboratorios › «Versión de DigLab...» y los equipos se actualizan solos.

6. **Marca la tarea como hecha:** cambia arriba «Estado: PENDIENTE» por «Estado: HECHA el dd/mm/aaaa, con lo que dijo el guion». Si se puede, haz commit.

## Lo demás

- Responde en español. El programa se llama DigLab (antes DigBit; ver README.md).
- No escribas en la base de la escuela salvo esta tarea, y solo con el respaldo que hace `migrar_bd.ps1`.
- Los pendientes y las decisiones están en `docs/pendientes.md` (para la 1.3, los puntos 1m, 1n y 1o). Cómo instalar: `INSTALAR.md`.
