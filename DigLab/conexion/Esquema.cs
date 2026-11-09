using System;
using System.Collections.Generic;
using System.Diagnostics;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>
    /// Que migraciones tiene la base a la que se conecta. La aplicacion nueva
    /// puede llegar a los equipos antes de que se migre la base: en vez de romper
    /// el inicio de sesion con "Unknown column", se comporta como antes hasta que
    /// la columna exista.
    /// </summary>
    internal static class Esquema
    {
        /// <summary>
        /// Cuanto se da por bueno que a una cuenta le falta una migracion sin volver a
        /// preguntar (en ticks de Stopwatch: un minuto). Lo que si esta no se deshace
        /// y se recuerda siempre; lo que falta, solo este rato. Asi los paneles no
        /// preguntan a information_schema en cada refresco (en la escuela, sin las
        /// 14 a 22, eran unas 6 preguntas por lectura de Estadisticas) y una migracion
        /// aplicada con DigLab abierto se nota en un minuto como mucho.
        /// </summary>
        private static readonly long VidaDelNo = Stopwatch.Frequency * 60;

        private static readonly Columna Bajas = new Columna("usuarios", "activo",
            "La base no tiene usuarios.activo: falta db/08_migracion_bajas.sql. "
            + "Hasta aplicarla no se puede dar de baja a nadie y todos pueden entrar.");

        private static readonly Columna LaboratoriosActivos = new Columna("laboratorios", "activo",
            "La base no tiene laboratorios.activo: falta db/08_migracion_bajas.sql. "
            + "Hasta aplicarla solo se pueden quitar los laboratorios que nunca se usaron.");

        private static readonly Columna CodigosDeProfesor = new Columna("usuarios", "codigo_clase",
            "La base no tiene usuarios.codigo_clase: falta db/09_codigo_por_profesor.sql. "
            + "Hasta aplicarla cada clase sigue con su propio codigo.");

        /// <summary>True si usuarios tiene activo y baja_en (db/08_migracion_bajas.sql).</summary>
        public static bool TieneBajas(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return Bajas.Existe(conexion, transaccion);
        }

        /// <summary>True si cada profesor tiene un codigo para todas sus clases (db/09_codigo_por_profesor.sql).</summary>
        public static bool CodigoPorProfesor(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return CodigosDeProfesor.Existe(conexion, transaccion);
        }

        /// <summary>True si laboratorios tiene activo (db/08_migracion_bajas.sql).</summary>
        public static bool LaboratoriosDesactivables(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return LaboratoriosActivos.Existe(conexion, transaccion);
        }

        private static readonly Columna AvisosSinInternet = new Columna("registros_bitacoras", "equipos_sin_internet",
            "La base no tiene registros_bitacoras.equipos_sin_internet: falta db/10_red_y_autoregistro.sql. "
            + "Hasta aplicarla no se pregunta al alumno por los equipos de su alrededor sin internet.");

        private static readonly Columna Llegadas = new Columna("registros_bitacoras", "recibido_en",
            "La base no tiene registros_bitacoras.recibido_en: falta db/10_red_y_autoregistro.sql. "
            + "Hasta aplicarla no se sabe cuando llego al servidor una bitacora que se subio desde la cola.");

        /// <summary>True si la bitacora guarda los equipos de alrededor sin internet (db/10_red_y_autoregistro.sql).</summary>
        public static bool EquiposSinInternet(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return AvisosSinInternet.Existe(conexion, transaccion);
        }

        /// <summary>True si la bitacora guarda cuando llego al servidor (db/10_red_y_autoregistro.sql).</summary>
        public static bool HoraDeLlegada(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return Llegadas.Existe(conexion, transaccion);
        }

        private static readonly Columna ColumnaModo = new Columna("laboratorios", "modo_codigo",
            "La base no tiene laboratorios.modo_codigo: falta db/11_modo_de_acceso.sql. "
            + "Hasta aplicarla todos los laboratorios usan el codigo fijo de cada profesor y el administrador no puede cambiarlo.");

        /// <summary>True si cada laboratorio guarda como entran los alumnos a sus clases (db/11_modo_de_acceso.sql).</summary>
        public static bool ModoDeAcceso(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return ColumnaModo.Existe(conexion, transaccion);
        }

        private static readonly Columna ColumnaColor = new Columna("usuarios", "color_horario",
            "La base no tiene usuarios.color_horario: falta db/12_color_profesor.sql. "
            + "Hasta aplicarla el color de cada profesor sale solo y no se puede elegir.");

        /// <summary>True si se guarda el color elegido para cada profesor (db/12_color_profesor.sql).</summary>
        public static bool ColorDeProfesor(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return ColumnaColor.Existe(conexion, transaccion);
        }

        // Una columna de la tabla basta para saber que la tabla esta (y que se puede leer).
        private static readonly Columna TablaAjustes = new Columna("configuracion", "valor",
            "La base no tiene la tabla configuracion (o este usuario no puede leerla): falta db/13_recuperacion_contrasena.sql. "
            + "Hasta aplicarla, \"Olvidaste tu contrasena?\" solo funciona en los equipos con UrlRecuperacion en DigLab.exe.config.");

        /// <summary>True si la base guarda los ajustes de toda la escuela (db/13_recuperacion_contrasena.sql).</summary>
        public static bool Ajustes(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaAjustes.Existe(conexion, transaccion);
        }

        private static readonly Columna TablaRecuperacion = new Columna("recuperacion_contrasena", "usado_en",
            "La base no tiene la tabla recuperacion_contrasena: falta db/13_recuperacion_contrasena.sql. "
            + "Sin ella no hay codigos de recuperacion por correo que anular al cambiar un correo o una contrasena.");

        private static readonly Columna ColumnaEnlaceSiia = new Columna("usuarios", "siia_enlazado_en",
            "La base no tiene usuarios.siia_enlazado_en: falta db/24_docentes_siia.sql. "
            + "Hasta aplicarla no se enlazan docentes con el SIIA y el inicio de sesion de docentes con el SIIA queda apagado.");

        private static readonly Columna TablaSolicitudesSiia = new Columna("siia_solicitudes", "estado",
            "La base no tiene la tabla siia_solicitudes (o este usuario no puede leerla): falta db/24_docentes_siia.sql. "
            + "Hasta aplicarla no se enlazan docentes con el SIIA y el inicio de sesion de docentes con el SIIA queda apagado.");

        /// <summary>
        /// True si la base guarda que docente esta enlazado con su numero de
        /// empleado del SIIA y quien espera que lo enlacen (db/24_docentes_siia.sql).
        /// </summary>
        public static bool EnlacesSiia(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return ColumnaEnlaceSiia.Existe(conexion, transaccion) && TablaSolicitudesSiia.Existe(conexion, transaccion);
        }

        /// <summary>True si la base guarda los codigos de "Olvidaste tu contrasena?" (db/13_recuperacion_contrasena.sql).</summary>
        public static bool Recuperacion(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaRecuperacion.Existe(conexion, transaccion);
        }

        // Solo la ve quien tiene permiso sobre ella (db/15 se lo da a digbit_equipo):
        // sin el, tampoco podria guardar, y el boton no aparece.
        private static readonly Columna TablaReportesProfesor = new Columna("reportes_profesor", "registrado_en",
            "La base no tiene la tabla reportes_profesor (o este usuario no puede leerla): falta db/15_reportes_profesor.sql. "
            + "Hasta aplicarla el profesor no puede reportar fallas desde su panel.");

        /// <summary>True si el profesor puede reportar fallas de su clase en curso (db/15_reportes_profesor.sql).</summary>
        public static bool ReportesDeProfesor(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaReportesProfesor.Existe(conexion, transaccion);
        }

        // Solo la ve quien tiene permiso sobre ella (db/17 se lo da a digbit_equipo):
        // sin el, tampoco podria quitar ni devolver, y la lista solo se consulta.
        private static readonly Columna TablaAsistenciasRetiradas = new Columna("asistencias_retiradas", "retirada_en",
            "La base no tiene la tabla asistencias_retiradas (o este usuario no puede leerla): falta db/17_asistencia_retirada.sql. "
            + "Hasta aplicarla el profesor no puede quitar la asistencia a quien registro sin estar en el salon.");

        /// <summary>True si el profesor puede quitar asistencias durante su clase (db/17_asistencia_retirada.sql).</summary>
        public static bool AsistenciaRetirable(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaAsistenciasRetiradas.Existe(conexion, transaccion);
        }

        // Solo la ve quien tiene permiso sobre ella (db/22 se lo da a digbit_equipo y a
        // digbit_consulta): sin el, el profesor no puede dar asistencias y las listas
        // se cuentan como antes, solo con las bitacoras.
        private static readonly Columna TablaAsistenciasDadas = new Columna("asistencias_dadas", "anulada_por",
            "La base no tiene la tabla asistencias_dadas (o este usuario no puede leerla): falta db/22_asistencia_dada.sql. "
            + "Hasta aplicarla el profesor no puede dar la asistencia a quien no pudo registrar su bitacora.");

        /// <summary>
        /// True si el profesor puede dar la asistencia, durante su clase, a quien no
        /// pudo registrar su bitacora, y las listas la cuentan (db/22_asistencia_dada.sql).
        /// </summary>
        public static bool AsistenciaDada(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaAsistenciasDadas.Existe(conexion, transaccion);
        }

        // La ultima tabla que crea la migracion. Solo el administrador la usa: los
        // equipos (digbit_equipo, db/07) no tienen permiso sobre estas tablas, y la
        // persona responsable (digbit_consulta) solo las lee con db/23 o la 18 nueva.
        private static readonly Columna TablasCicloPreliminar = new Columna("ciclo_recortes", "hasta_antes",
            "La base no tiene las tablas de las configuraciones de horario (o este usuario no puede leerlas): falta db/16_ciclo_preliminar.sql. "
            + "Hasta aplicarla, las pestanas Configuraciones y Aplicacion solo lo explican.");

        /// <summary>True si estan las tablas de las configuraciones de horario (db/16_ciclo_preliminar.sql).</summary>
        public static bool CicloPreliminar(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablasCicloPreliminar.Existe(conexion, transaccion);
        }

        // La ultima columna que anade la migracion. Con digbit_consulta sin el
        // permiso de db/23 tampoco se ve, y las pestanas lo explican.
        private static readonly Columna TablaGuardados = new Columna("ciclo_cargas", "carga_origen",
            "La base no tiene lo de las configuraciones de horario: falta db/23_horarios_guardados.sql. "
            + "Hasta aplicarla, las pestanas Configuraciones y Aplicacion solo lo explican.");

        /// <summary>
        /// True si se pueden guardar varias configuraciones de horario con nombre,
        /// prepararlas y aplicarlas (db/23_horarios_guardados.sql, sobre la 16).
        /// </summary>
        public static bool HorariosGuardados(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaGuardados.Existe(conexion, transaccion);
        }

        // Solo la ve quien tiene permiso sobre ella (db/19 se lo da a digbit_equipo y no
        // a digbit_consulta): sin el, el profesor no personaliza y todo se ve como antes.
        private static readonly Columna TablaPersonalizacion = new Columna("personalizacion_clases", "actualizado_en",
            "La base no tiene la tabla personalizacion_clases (o este usuario no puede leerla): falta db/19_personalizacion_clases.sql. "
            + "Hasta aplicarla el profesor no puede personalizar sus clases (imagen, color y nombre corto).");

        /// <summary>
        /// True si el profesor puede personalizar sus clases (db/19_personalizacion_clases.sql).
        /// Solo desde las pantallas del profesor: con la sesion de un encargado
        /// escribiria en su log que falta la 19, y no seria cierto.
        /// </summary>
        public static bool PersonalizacionDeClases(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaPersonalizacion.Existe(conexion, transaccion);
        }

        private static readonly Columna ColumnaVigenciaFranja = new Columna("horarios", "vigente_hasta",
            "La base no tiene horarios.vigente_desde/vigente_hasta: falta db/20_vigencia_franjas.sql. "
            + "Hasta aplicarla cada franja vale toda la vigencia de su clase y cambiarla reescribe las semanas pasadas.");

        /// <summary>True si cada franja puede tener su propia vigencia (db/20_vigencia_franjas.sql).</summary>
        public static bool VigenciaDeFranjas(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return ColumnaVigenciaFranja.Existe(conexion, transaccion);
        }

        // Solo la ven el administrador (digbit_admin) y el encargado (digbit_consulta,
        // solo leerla): los equipos no la usan. Sin ella nada se anota y no hay
        // alertas de cambios de horario, pero todo lo demas funciona igual.
        private static readonly Columna TablaHistorial = new Columna("historial_cambios", "despues",
            "La base no tiene la tabla historial_cambios (o este usuario no puede leerla): falta db/21_historial_cambios.sql. "
            + "Hasta aplicarla, los cambios del horario y de los laboratorios y las correcciones de bitacoras no dejan constancia "
            + "en la base de quien ni cuando, y no hay alertas de cambios de horario.");

        /// <summary>True si la base guarda quien cambio que y cuando (db/21_historial_cambios.sql).</summary>
        public static bool HistorialDeCambios(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaHistorial.Existe(conexion, transaccion);
        }

        // Solo la ven el administrador (digbit_admin) y el encargado (digbit_consulta,
        // solo leerla, db/25 o la 18 nueva): los equipos no la usan. Sin ella todo
        // sigue como antes: sin reemplazo ni "Falta asignar".
        private static readonly Columna TablaPorAsignar = new Columna("horas_por_asignar", "resuelta_por",
            "La base no tiene la tabla horas_por_asignar (o este usuario no puede leerla): falta db/25_horas_por_asignar.sql. "
            + "Hasta aplicarla, en Horario semanal poner una clase o cambiarle las horas encima de otra del mismo laboratorio "
            + "no se guarda (como antes), \"Quitar horas\" no pregunta si se dara en otro horario y no sale \"Falta asignar\".");

        /// <summary>
        /// True si la base guarda las horas que faltan por asignar en el horario en
        /// uso (db/25_horas_por_asignar.sql): reemplazar una clase y "Falta asignar".
        /// </summary>
        public static bool HorasPorAsignar(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaPorAsignar.Existe(conexion, transaccion);
        }

        // La escribe la administracion (digbit_admin); los equipos (digbit_equipo) y el
        // encargado (digbit_consulta) solo la leen, con el permiso de db/26. Sin ella
        // todo sigue como antes: lo reportado en naranja y sin "Resolver fallas...".
        private static readonly Columna TablaFallasResueltas = new Columna("fallas_resueltas", "resuelta_en",
            "La base no tiene la tabla fallas_resueltas (o este usuario no puede leerla): falta db/26_fallas_resueltas.sql. "
            + "Hasta aplicarla, lo reportado se queda en naranja y en Alertas y no se puede dar por resuelto.");

        /// <summary>True si se pueden dar por resueltas las fallas reportadas (db/26_fallas_resueltas.sql).</summary>
        public static bool FallasResueltas(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaFallasResueltas.Existe(conexion, transaccion);
        }

        // La escribe la administracion (digbit_admin); los equipos (digbit_equipo) y el
        // encargado (digbit_consulta) solo la leen, con el permiso de db/27. Sin ella, o
        // sin ese permiso, nadie queda suspendido en ese equipo: por eso va al log.
        private static readonly Columna TablaSuspensiones = new Columna("suspensiones", "hasta",
            "La base no tiene la tabla suspensiones (o este usuario no puede leerla): falta db/27_suspensiones.sql. "
            + "Hasta aplicarla, en este equipo no vale ninguna suspensión de acceso y no se puede suspender a nadie.");

        /// <summary>True si se puede suspender el acceso de un estudiante y este equipo lo ve (db/27_suspensiones.sql).</summary>
        public static bool Suspensiones(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaSuspensiones.Existe(conexion, transaccion);
        }

        // La escribe la administracion (digbit_admin); los equipos (digbit_equipo) solo la
        // leen, con el permiso de db/28. Sin ella, o sin ese permiso, cada equipo sigue con
        // la memoria que se autorizo al instalarlo: por eso va al log.
        private static readonly Columna TablaMemoriasEmergencia = new Columna("memorias_emergencia", "huella",
            "La base no tiene la tabla memorias_emergencia (o este usuario no puede leerla): falta db/28_memorias_emergencia.sql. "
            + "Hasta aplicarla, este equipo solo acepta la memoria de emergencia que se autorizó al instalarlo.");

        /// <summary>True si las memorias de emergencia se registran en la base y este equipo las ve (db/28_memorias_emergencia.sql).</summary>
        public static bool MemoriasEmergencia(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return TablaMemoriasEmergencia.Existe(conexion, transaccion);
        }

        private static bool usoLibre;
        private static bool avisadoUsoLibre;
        private static readonly NoReciente sinUsoLibre = new NoReciente();

        /// <summary>
        /// True si codigos_accesos admite sesiones sin clase, sin profesor, materia
        /// ni grupo (db/14_registro_sin_clase.sql): las del uso libre de un
        /// laboratorio sin codigos. Una vez visto, no se vuelve a preguntar; si
        /// falta, no se pregunta otra vez con esa cuenta hasta pasado un minuto.
        /// </summary>
        public static bool UsoLibre(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            string cuenta = CuentaDe(conexion);
            lock (candadoTipo)
            {
                if (usoLibre)
                {
                    return true;
                }

                if (sinUsoLibre.Vigente(cuenta))
                {
                    return false;
                }
            }

            int nulas;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS " +
                "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND IS_NULLABLE = 'YES' " +
                "AND COLUMN_NAME IN ('usuarios_idusuarios', 'materias_id_materia', 'grupos_idgrupos')", conexion, transaccion))
            {
                nulas = Convert.ToInt32(comando.ExecuteScalar());
            }

            bool admite = nulas == 3;
            lock (candadoTipo)
            {
                usoLibre = admite;
                if (!admite)
                {
                    sinUsoLibre.Anotar(cuenta);
                }

                if (!admite && !avisadoUsoLibre)
                {
                    avisadoUsoLibre = true;
                    Log.Aviso("La base no admite sesiones sin clase (codigos_accesos exige profesor, materia y grupo): falta "
                        + "db/14_registro_sin_clase.sql. Hasta aplicarla, sin codigos y sin clase el alumno no puede registrar uso libre.");
                }
            }

            return admite;
        }

        private static bool sesionesUnicas;
        private static readonly NoReciente sinSesionesUnicas = new NoReciente();

        /// <summary>
        /// True si codigos_accesos tiene UNA sesion por franja y dia y UNA por
        /// sesion extra (los UNIQUE de db/04, buscados por columnas). Es lo que
        /// garantiza un solo codigo por clase en el modo renovable aunque la
        /// abran a la vez dos paneles y un equipo. El si lo recuerda siempre; el
        /// no, un minuto con esa cuenta.
        /// </summary>
        public static bool SesionesUnicas(MySqlConnection conexion)
        {
            string cuenta = CuentaDe(conexion);
            lock (candadoTipo)
            {
                if (sesionesUnicas)
                {
                    return true;
                }

                if (sinSesionesUnicas.Vigente(cuenta))
                {
                    return false;
                }
            }

            int unicos;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(DISTINCT COLUMN_NAME) FROM information_schema.STATISTICS " +
                "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'codigos_accesos' AND NON_UNIQUE = 0 " +
                "AND SEQ_IN_INDEX = 1 AND COLUMN_NAME IN ('horarios_idhorarios', 'horario_excepciones_id')", conexion))
            {
                unicos = Convert.ToInt32(comando.ExecuteScalar());
            }

            lock (candadoTipo)
            {
                sesionesUnicas = unicos == 2;
                if (!sesionesUnicas)
                {
                    sinSesionesUnicas.Anotar(cuenta);
                }
            }

            return unicos == 2;
        }

        private static readonly object candadoTipo = new object();
        private static bool alumnoPorDefecto;
        private static readonly NoReciente sinAlumnoPorDefecto = new NoReciente();

        /// <summary>
        /// True si usuarios.fk_tipo_usuario vale 1 (alumno) por defecto
        /// (db/10_red_y_autoregistro.sql). Entonces el alta desde el laboratorio
        /// no nombra esa columna, que es lo unico que permite el usuario de MySQL
        /// de los equipos (db/07). Una vez visto, no se vuelve a preguntar; si
        /// falta, no se pregunta otra vez con esa cuenta hasta pasado un minuto.
        /// </summary>
        public static bool AlumnoPorDefecto(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            string cuenta = CuentaDe(conexion);
            lock (candadoTipo)
            {
                if (alumnoPorDefecto)
                {
                    return true;
                }

                if (sinAlumnoPorDefecto.Vigente(cuenta))
                {
                    return false;
                }
            }

            object defecto;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COLUMN_DEFAULT FROM information_schema.COLUMNS " +
                "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios' AND COLUMN_NAME = 'fk_tipo_usuario'", conexion, transaccion))
            {
                defecto = comando.ExecuteScalar();
            }

            // MariaDB 10.2.7 y posteriores ponen comillas a los valores por defecto de texto; un numero sale tal cual.
            bool es1 = defecto != null && defecto != DBNull.Value && defecto.ToString().Trim('\'') == "1";
            lock (candadoTipo)
            {
                alumnoPorDefecto = es1;
                if (!es1)
                {
                    sinAlumnoPorDefecto.Anotar(cuenta);
                }
            }

            return es1;
        }

        private static bool carreraSinGrupo;
        private static bool avisadoCarrera;
        private static readonly NoReciente sinCarreraSinGrupo = new NoReciente();

        /// <summary>
        /// True si carrera_grupo_semestre admite un alumno sin grupo ni semestre
        /// (db/10_red_y_autoregistro.sql; la 01 ya la crea asi). En la base de la
        /// escuela los dos eran obligatorios, y como el alumno ya no declara
        /// grupo, sin la migracion no se puede guardar la carrera de un alumno
        /// nuevo. Una vez visto, no se vuelve a preguntar; si falta, no se pregunta
        /// otra vez con esa cuenta hasta pasado un minuto.
        /// </summary>
        public static bool CarreraSinGrupo(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            string cuenta = CuentaDe(conexion);
            lock (candadoTipo)
            {
                if (carreraSinGrupo)
                {
                    return true;
                }

                if (sinCarreraSinGrupo.Vigente(cuenta))
                {
                    return false;
                }
            }

            int obligatorias;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS " +
                "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'carrera_grupo_semestre' " +
                "AND COLUMN_NAME IN ('grupos_idgrupos', 'semestres_idsemestres') AND IS_NULLABLE = 'NO'", conexion, transaccion))
            {
                obligatorias = Convert.ToInt32(comando.ExecuteScalar());
            }

            bool admite = obligatorias == 0;
            lock (candadoTipo)
            {
                carreraSinGrupo = admite;
                if (!admite)
                {
                    sinCarreraSinGrupo.Anotar(cuenta);
                }

                if (!admite && !avisadoCarrera)
                {
                    avisadoCarrera = true;
                    Log.Aviso("En la base, carrera_grupo_semestre exige grupo y semestre: falta db/10_red_y_autoregistro.sql. "
                        + "Hasta aplicarla no se puede dar de alta a un alumno (ni el administrador ni el propio alumno).");
                }
            }

            return admite;
        }

        private static bool tipoEncargado;
        private static bool avisadoEncargado;
        private static readonly NoReciente sinTipoEncargado = new NoReciente();

        /// <summary>
        /// True si se pueden dar de alta encargados de laboratorio (tipo 4): la base
        /// no tiene catalogo de tipos (las de 01_schema) o ya tiene el 4 (db/18).
        /// En la del TESCHI, tipo_usuarios tiene clave foranea desde usuarios. Una
        /// vez visto, no se vuelve a preguntar; si falta, no se pregunta otra vez
        /// con esa cuenta hasta pasado un minuto.
        /// </summary>
        public static bool TipoEncargado(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            string cuenta = CuentaDe(conexion);
            lock (candadoTipo)
            {
                if (tipoEncargado)
                {
                    return true;
                }

                if (sinTipoEncargado.Vigente(cuenta))
                {
                    return false;
                }
            }

            bool hayCatalogo;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tipo_usuarios'", conexion, transaccion))
            {
                hayCatalogo = Convert.ToInt32(comando.ExecuteScalar()) > 0;
            }

            bool listo = !hayCatalogo;
            if (hayCatalogo)
            {
                using (MySqlCommand comando = new MySqlCommand("SELECT COUNT(*) FROM tipo_usuarios WHERE idtipo_usuarios = 4", conexion, transaccion))
                {
                    listo = Convert.ToInt32(comando.ExecuteScalar()) > 0;
                }
            }

            lock (candadoTipo)
            {
                tipoEncargado = listo;
                if (!listo)
                {
                    sinTipoEncargado.Anotar(cuenta);
                }

                if (!listo && !avisadoEncargado)
                {
                    avisadoEncargado = true;
                    Log.Aviso("La base no tiene el tipo de usuario 4 (encargado de laboratorio): falta db/18_encargados.sql. "
                        + "Hasta aplicarla no se pueden dar de alta encargados.");
                }
            }

            return listo;
        }

        /// <summary>
        /// La cuenta de MySQL de una conexion ("usuario@servidor:puerto/base").
        /// Lo que se ve en information_schema depende de sus permisos, y un mismo
        /// proceso pasa por varias: la del equipo y, mientras entra un encargado
        /// de laboratorio, la de solo consulta (Conexion.UsarCuentaDeSesion).
        /// </summary>
        internal static string CuentaDe(MySqlConnection conexion)
        {
            try
            {
                MySqlConnectionStringBuilder datos = new MySqlConnectionStringBuilder(conexion.ConnectionString);
                return (datos.UserID ?? "") + "@" + (datos.Server ?? "") + ":" + datos.Port + "/" + (conexion.Database ?? "");
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// Las cuentas a las que les falto algo hace menos de VidaDelNo, medido con el
        /// contador del sistema (el reloj de Windows se puede cambiar). No se protege
        /// sola: quien la usa la guarda con su candado.
        /// </summary>
        private sealed class NoReciente
        {
            private readonly Dictionary<string, long> faltoEn = new Dictionary<string, long>();

            /// <summary>True si a esa cuenta le falto hace menos de VidaDelNo.</summary>
            public bool Vigente(string cuenta)
            {
                long cuando;
                return faltoEn.TryGetValue(cuenta, out cuando) && Stopwatch.GetTimestamp() - cuando < VidaDelNo;
            }

            public void Anotar(string cuenta)
            {
                faltoEn[cuenta] = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// Una columna que llega con una migracion. Una vez vista, no se vuelve a
        /// preguntar; si falta, no se pregunta otra vez hasta pasado un minuto
        /// (VidaDelNo): asi se nota la migracion sin reiniciar y sin preguntar en
        /// cada llamada. Lo visto y lo que falto valen para la cuenta con la que se
        /// vio: la de solo consulta no lee todas las tablas que lee la del equipo,
        /// y al reves.
        /// </summary>
        private class Columna
        {
            private readonly string tabla;
            private readonly string nombre;
            private readonly string aviso;
            private readonly object candado = new object();
            private readonly HashSet<string> cuentasQueLaVen = new HashSet<string>();
            private readonly NoReciente cuentasQueNoLaVen = new NoReciente();
            private bool avisado;

            public Columna(string tabla, string nombre, string aviso)
            {
                this.tabla = tabla;
                this.nombre = nombre;
                this.aviso = aviso;
            }

            public bool Existe(MySqlConnection conexion, MySqlTransaction transaccion)
            {
                string cuenta = CuentaDe(conexion);
                lock (candado)
                {
                    if (cuentasQueLaVen.Contains(cuenta))
                    {
                        return true;
                    }

                    if (cuentasQueNoLaVen.Vigente(cuenta))
                    {
                        return false;
                    }
                }

                bool hay;
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.COLUMNS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @Tabla AND COLUMN_NAME = @Columna", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Tabla", tabla);
                    comando.Parameters.AddWithValue("@Columna", nombre);
                    hay = Convert.ToInt32(comando.ExecuteScalar()) > 0;
                }

                lock (candado)
                {
                    if (hay)
                    {
                        cuentasQueLaVen.Add(cuenta);
                    }
                    else
                    {
                        cuentasQueNoLaVen.Anotar(cuenta);
                        if (!avisado)
                        {
                            avisado = true;
                            Log.Aviso(aviso);
                        }
                    }
                }

                return hay;
            }
        }
    }
}
