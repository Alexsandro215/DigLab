using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>
    /// Consultas por SESION (idcodigos_accesos) en vez de por texto de codigo.
    /// Desde la fase 6 el codigo se repite en cada sesion de la misma clase, asi
    /// que buscar por texto es ambiguo: las pantallas pasan el id de la sesion.
    /// </summary>
    internal partial class Consultas
    {
        /// <summary>La ventana de una sesion, con nombres de laboratorio, grupo, materia y profesor.</summary>
        public VentanaCodigo ObtenerVentanaSesion(int idSesion)
        {
            const string consulta = @"
                SELECT ca.idcodigos_accesos, ca.codigo,
                       TIMESTAMP(ca.fecha, ca.hora_entrada) AS inicio,
                       TIMESTAMP(ca.fecha, ca.hora_salida)  AS fin,
                       NOW() AS ahora,
                       COALESCE(ca.laboratorios_idlaboratorios, 0) AS lab_id,
                       COALESCE(l.nombre_laboratorio, '') AS laboratorio,
                       COALESCE(ca.grupos_idgrupos, 0) AS grupo_id,
                       COALESCE(g.nombre_grupo, '') AS grupo,
                       COALESCE(m.nombre_materia, '') AS materia,
                       CONCAT_WS(' ', p.nombre, p.apellido_paterno, p.apellido_materno) AS profesor,
                       ca.horarios_idhorarios, ca.horario_excepciones_id,
                       COALESCE(ca.materias_id_materia, 0) AS materia_id,
                       COALESCE(ca.usuarios_idusuarios, 0) AS profesor_id
                FROM codigos_accesos ca
                LEFT JOIN laboratorios l ON l.idlaboratorios = ca.laboratorios_idlaboratorios
                LEFT JOIN grupos g       ON g.idgrupos = ca.grupos_idgrupos
                LEFT JOIN materias m     ON m.id_materia = ca.materias_id_materia
                LEFT JOIN usuarios p     ON p.idusuarios = ca.usuarios_idusuarios
                WHERE ca.idcodigos_accesos = @Id";

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            {
                comando.Parameters.AddWithValue("@Id", idSesion);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    if (reader.IsDBNull(reader.GetOrdinal("inicio")) || reader.IsDBNull(reader.GetOrdinal("fin")))
                    {
                        throw new InvalidOperationException("La sesion " + idSesion + " no tiene fecha u horas de entrada y salida.");
                    }

                    VentanaCodigo ventana = new VentanaCodigo
                    {
                        IdCodigo = reader.GetInt32("idcodigos_accesos"),
                        Codigo = reader.GetString("codigo"),
                        Inicio = reader.GetDateTime("inicio"),
                        Fin = reader.GetDateTime("fin"),
                        AhoraServidor = reader.GetDateTime("ahora"),
                        LeidoEnRelojLocal = DateTime.Now,
                        IdLaboratorio = reader.GetInt32("lab_id"),
                        Laboratorio = reader.GetString("laboratorio"),
                        IdGrupo = reader.GetInt32("grupo_id"),
                        Grupo = reader.GetString("grupo"),
                        Materia = reader.GetString("materia"),
                        Profesor = reader.IsDBNull(reader.GetOrdinal("profesor")) ? "" : reader.GetString("profesor"),
                        FranjaId = reader.IsDBNull(reader.GetOrdinal("horarios_idhorarios")) ? (int?)null : Convert.ToInt32(reader["horarios_idhorarios"]),
                        ExcepcionId = reader.IsDBNull(reader.GetOrdinal("horario_excepciones_id")) ? (int?)null : Convert.ToInt32(reader["horario_excepciones_id"]),
                        IdMateria = Convert.ToInt32(reader["materia_id"]),
                        IdProfesor = Convert.ToInt32(reader["profesor_id"])
                    };
                    ventana.Estado = CalcularEstado(ventana);
                    return ventana;
                }
            }
        }

        /// <summary>
        /// La sesion mas reciente de una franja del horario (0 si esa clase aun
        /// no se ha usado ningun dia). Es lo que abre el profesor al pulsar su
        /// clase en la cuadricula.
        /// </summary>
        public int ObtenerIdSesionDeFranja(int idFranja)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idcodigos_accesos FROM codigos_accesos WHERE horarios_idhorarios = @Id ORDER BY fecha DESC LIMIT 1", conexion))
            {
                comando.Parameters.AddWithValue("@Id", idFranja);
                object resultado = comando.ExecuteScalar();
                return resultado == null ? 0 : Convert.ToInt32(resultado);
            }
        }

        /// <summary>
        /// La sesion de una franja en una fecha concreta (0 si ese dia nadie ha
        /// registrado todavia). Es la de la clase de hoy del panel del profesor:
        /// sin la fecha saldria la de la semana pasada.
        /// </summary>
        public int ObtenerIdSesionDeFranja(int idFranja, DateTime fecha)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idcodigos_accesos FROM codigos_accesos WHERE horarios_idhorarios = @Id AND fecha = @Fecha LIMIT 1", conexion))
            {
                comando.Parameters.AddWithValue("@Id", idFranja);
                comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                object resultado = comando.ExecuteScalar();
                return resultado == null ? 0 : Convert.ToInt32(resultado);
            }
        }

        /// <summary>La sesion de una excepcion 'extra' (0 si nadie la ha usado todavia).</summary>
        public int ObtenerIdSesionDeExcepcion(int idExcepcion)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idcodigos_accesos FROM codigos_accesos WHERE horario_excepciones_id = @Id", conexion))
            {
                comando.Parameters.AddWithValue("@Id", idExcepcion);
                object resultado = comando.ExecuteScalar();
                return resultado == null ? 0 : Convert.ToInt32(resultado);
            }
        }

        /// <summary>La sesion mas reciente con ese texto de codigo (0 si no hay). Para pantallas que aun piden el codigo a mano.</summary>
        public int ObtenerIdSesionMasReciente(string codigo)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idcodigos_accesos FROM codigos_accesos WHERE codigo = @Codigo ORDER BY fecha DESC, hora_entrada DESC, idcodigos_accesos DESC LIMIT 1", conexion))
            {
                comando.Parameters.AddWithValue("@Codigo", codigo);
                object resultado = comando.ExecuteScalar();
                return resultado == null ? 0 : Convert.ToInt32(resultado);
            }
        }

        /// <summary>
        /// Encabezado del PDF de una sesion (horas, fecha, profesor, grupo, materia
        /// y laboratorio), o null si la sesion ya no existe. El profesor sale de la
        /// sesion, no de quien abre la pantalla: los PDF de Bitacoras y de la
        /// bitacora de una clase dicen lo mismo.
        /// </summary>
        public EncabezadoPdfSesion ConsultarDatosPdfSesion(int idSesion)
        {
            // Uso libre (db/14): sin materia, grupo ni profesor; la materia lo dice
            // y el laboratorio va en su propia linea.
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT ca.hora_entrada, ca.hora_salida, ca.fecha, ca.hora_registro,
                       IF(" + HorariosDatos.CondicionUsoLibre + @", '-', COALESCE(g.nombre_grupo, '')) AS GrupoNombre,
                       IF(" + HorariosDatos.CondicionUsoLibre + @", 'Uso libre del laboratorio', COALESCE(m.nombre_materia, '')) AS MateriaNombre,
                       COALESCE(l.nombre_laboratorio, '') AS LaboratorioNombre,
                       CONCAT_WS(' ', p.nombre, p.apellido_paterno, p.apellido_materno) AS ProfesorNombre
                FROM codigos_accesos ca
                LEFT JOIN grupos g       ON ca.grupos_idgrupos = g.idgrupos
                LEFT JOIN materias m     ON ca.materias_id_materia = m.id_materia
                LEFT JOIN laboratorios l ON ca.laboratorios_idlaboratorios = l.idlaboratorios
                LEFT JOIN usuarios p     ON ca.usuarios_idusuarios = p.idusuarios
                WHERE ca.idcodigos_accesos = @Id", conexion))
            {
                comando.Parameters.AddWithValue("@Id", idSesion);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    DateTime fecha = reader.IsDBNull(reader.GetOrdinal("fecha"))
                        ? reader.GetDateTime("hora_registro")
                        : reader.GetDateTime("fecha");

                    // "Martes 29/09/2026": dd/MM/yyyy con InvariantCulture, para que el
                    // separador no salga de la configuracion regional de Windows.
                    return new EncabezadoPdfSesion
                    {
                        HoraEntrada = HorariosDatos.HoraLegible(reader["hora_entrada"]),
                        HoraSalida = HorariosDatos.HoraLegible(reader["hora_salida"]),
                        Fecha = HorariosDatos.NombresDia[((int)fecha.DayOfWeek + 6) % 7 + 1] + " "
                            + fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        Profesor = reader.IsDBNull(reader.GetOrdinal("ProfesorNombre")) ? "" : reader.GetString("ProfesorNombre").Trim(),
                        Grupo = reader.GetString("GrupoNombre"),
                        Materia = reader.GetString("MateriaNombre"),
                        Laboratorio = reader.GetString("LaboratorioNombre")
                    };
                }
            }
        }

        /// <summary>
        /// Lo que guarda la aplicacion cuando el alumno NO reporta nada: vacio,
        /// "Ninguno" (a veces con espacios) o "EP"/"Equipo Propio" si trajo su
        /// propio equipo. Cualquier otro texto es una falla o un comentario.
        /// </summary>
        private const string SinNovedad = "('', 'Ninguno', 'EP', 'Equipo Propio')";

        /// <summary>Condicion SQL: la bitacora rb reporta alguna falla o comentario. La usa tambien el panel del administrador.</summary>
        internal const string CondicionConFalla =
            "TRIM(COALESCE(rb.falla_red, '')) NOT IN " + SinNovedad +
            " OR TRIM(COALESCE(rb.comentarios_red, '')) NOT IN " + SinNovedad +
            " OR TRIM(COALESCE(rb.falla_hardware, '')) NOT IN " + SinNovedad +
            " OR TRIM(COALESCE(rb.comentarios_hardware, '')) NOT IN " + SinNovedad +
            " OR TRIM(COALESCE(rb.falla_software, '')) NOT IN " + SinNovedad +
            " OR TRIM(COALESCE(rb.comentarios_software, '')) NOT IN " + SinNovedad;

        /// <summary>Condicion SQL (entre parentesis): la bitacora rb reporta algo en esa area ("red", "hardware" o "software").</summary>
        internal static string CondicionFallaEn(string area)
        {
            if (Array.IndexOf(AreasDeFalla, area) < 0)
            {
                throw new ArgumentException("Área de falla desconocida: " + area);
            }

            return "(TRIM(COALESCE(rb.falla_" + area + ", '')) NOT IN " + SinNovedad
                + " OR TRIM(COALESCE(rb.comentarios_" + area + ", '')) NOT IN " + SinNovedad + ")";
        }

        /// <summary>Condicion SQL: nadie ha dado por resuelto lo que reporta la bitacora rb. Solo con db/26 (Esquema.FallasResueltas).</summary>
        internal const string BitacoraSinResolver =
            "NOT EXISTS (SELECT 1 FROM fallas_resueltas frp WHERE frp.origen = 'bitacora' AND frp.registro_id = rb.idregistros_bitacoras)";

        /// <summary>
        /// Condicion SQL (entre parentesis): la bitacora rb reporta algo y sigue sin
        /// resolver, lo que sale en naranja y en Alertas. Sin db/26, CondicionConFalla.
        /// Estadisticas sigue con CondicionConFalla: cuenta todo lo que se reporto.
        /// </summary>
        internal static string CondicionFallaPendiente(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return Esquema.FallasResueltas(conexion, transaccion)
                ? "((" + CondicionConFalla + ") AND " + BitacoraSinResolver + ")"
                : "(" + CondicionConFalla + ")";
        }

        /// <summary>
        /// Condicion SQL (entre parentesis): lo que reporta la bitacora rb, una falla, un
        /// comentario o un aviso de otros equipos, ya se dio por resuelto. Sin db/26, nunca.
        /// </summary>
        internal static string CondicionYaResuelta(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            if (!Esquema.FallasResueltas(conexion, transaccion))
            {
                return "(1 = 0)";
            }

            return "(NOT " + BitacoraSinResolver + " AND ((" + CondicionConFalla + ")"
                + (Esquema.EquiposSinInternet(conexion, transaccion) ? " OR rb.equipos_sin_internet <> ''" : "") + "))";
        }

        /// <summary>Lo que se pone en un SELECT sobre rb para saber si esta resuelta, y el JOIN que pide (ver consultaRegistroSesion).</summary>
        internal static string ColumnasResuelta(bool conResueltas)
        {
            return conResueltas
                ? "IF(fr.idfallas_resueltas IS NULL, 0, 1) AS falla_resuelta, COALESCE(DATE_FORMAT(fr.resuelta_en, '%d/%m/%Y a las %H:%i'), '') AS resuelta_en, COALESCE(fr.nota, '') AS resuelta_nota"
                : "0 AS falla_resuelta, '' AS resuelta_en, '' AS resuelta_nota";
        }

        internal static string JoinResuelta(bool conResueltas)
        {
            return conResueltas ? "LEFT JOIN fallas_resueltas fr ON fr.origen = 'bitacora' AND fr.registro_id = rb.idregistros_bitacoras" : "";
        }

        /// <summary>
        /// Condicion SQL: la bitacora rb es de alguien que trajo su propio equipo
        /// (la bandera 16 de Estadisticas). Una sola definicion: la de
        /// AsistenciaDatos (paquete 1 de Estadisticas), que ya la usa en E6.
        /// </summary>
        internal const string CondicionEquipoPropio = AsistenciaDatos.CondicionEquipoPropio;

        /// <summary>
        /// Alumnos registrados en una sesion, para la tabla y el PDF. Incluye
        /// con_falla: 1 si esa bitacora reporta alguna falla o comentario, para
        /// resaltarla frente a las que no reportaron nada; hora_registro ("HH:mm"
        /// de su primer registro, o "—" si es de antes de guardarse la hora); y
        /// avisa_sin_internet, los otros equipos que vio sin internet ("11,13",
        /// vacio si no aviso o sin la migracion 10).
        /// </summary>
        public DataTable consultaRegistroSesion(int idSesion)
        {
            DataTable tabla = new DataTable();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                string avisos = Esquema.EquiposSinInternet(conexion) ? "COALESCE(rb.equipos_sin_internet, '')" : "''";
                bool conResueltas = Esquema.FallasResueltas(conexion);

                // sin_conexion con IF: la columna es TINYINT(1) y con
                // TreatTinyAsBoolean llegaria como True/False, no como 1/0.
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT rb.idregistros_bitacoras, rb.fk_codigo_accesos, rb.fk_usuario, u.nombre, u.apellido_paterno, u.apellido_materno, u.numero_identificador,
                           rb.numero_computadora,
                           COALESCE(rb.falla_red, '') AS falla_red, COALESCE(rb.comentarios_red, '') AS comentarios_red,
                           COALESCE(rb.falla_hardware, '') AS falla_hardware, COALESCE(rb.comentarios_hardware, '') AS comentarios_hardware,
                           COALESCE(rb.falla_software, '') AS falla_software, COALESCE(rb.comentarios_software, '') AS comentarios_software,
                           IF(rb.registrado_sin_conexion = 1, 1, 0) AS sin_conexion,
                           COALESCE(DATE_FORMAT(rb.registrado_en, '%H:%i'), '') AS hora_registro,
                           CASE WHEN " + CondicionConFalla + @" THEN 1 ELSE 0 END AS con_falla,
                           " + avisos + @" AS avisa_sin_internet,
                           " + ColumnasResuelta(conResueltas) + @"
                    FROM registros_bitacoras rb
                    INNER JOIN usuarios u ON rb.fk_usuario = u.idusuarios
                    " + JoinResuelta(conResueltas) + @"
                    WHERE rb.fk_codigo_accesos = @Id
                    ORDER BY u.apellido_paterno, u.apellido_materno, u.nombre", conexion))
                {
                    comando.Parameters.AddWithValue("@Id", idSesion);
                    using (MySqlDataAdapter adapter = new MySqlDataAdapter(comando))
                    {
                        adapter.Fill(tabla);
                    }
                }
            }

            tabla.Columns.Add("nombre_completo", typeof(string));
            foreach (DataRow fila in tabla.Rows)
            {
                // Fase 4: la bitacora registrada sin servidor entro sin contrasena;
                // se deja ver en tablas y PDF junto al nombre.
                string marca = Convert.ToInt32(fila["sin_conexion"]) == 1 ? " [sin conexión]" : "";
                fila["nombre_completo"] = fila["nombre"] + " " + fila["apellido_paterno"] + " " + fila["apellido_materno"] + marca;

                // Las bitacoras en linea de antes de guardar la hora no la tienen.
                if (string.IsNullOrEmpty(fila["hora_registro"].ToString()))
                {
                    fila["hora_registro"] = "—";
                }
            }

            return tabla;
        }

        /// <summary>
        /// Guarda la bitacora en la cola local (ColaBitacoras), de donde la sube
        /// sola EnviadorBitacoras. Mismas comprobaciones que en linea: ventana
        /// vigente y una sola bitacora por alumno y sesion (contra la cola). Sin
        /// mensajes: si no se guarda, el porque queda en UltimoMotivo y lo dice la
        /// pantalla del alumno, como el exito.
        /// </summary>
        /// <param name="identidadVerificada">True si el alumno entro con contrasena y se valido todo en linea (la red se cayo al guardar).</param>
        private ResultadoGuardado GuardarSinConexion(SesionLocal sesion, bool identidadVerificada, string matricula, string computadora,
            string fallared, string comentariored, string fallahardware, string comentariohardware, string fallasoftware, string comentariosoftware,
            string equiposSinInternet)
        {
            VentanaCodigo ventana = Datos_User.VentanaActual;
            if (sesion == null || ventana == null)
            {
                return NoGuardada(MotivoNoGuardada.NoExiste, "No hay una sesión válida para guardar tu bitácora: vuelve a escribir el código o a elegir tu clase.");
            }

            if (ventana.Restante <= TimeSpan.Zero)
            {
                // Sin servidor, como ReglasCodigo: solo la hora.
                return NoGuardada(MotivoNoGuardada.Terminada, FueraDeHora(ventana, null, true));
            }

            if (ColaBitacoras.YaRegistrada(sesion.Clave, matricula))
            {
                return NoGuardada(MotivoNoGuardada.Duplicada, "Ya registraste tu bitácora en esta clase.");
            }

            try
            {
                ColaBitacoras.Encolar(new BitacoraPendiente
                {
                    // La hora del SERVIDOR estimada, no la del equipo: es la que
                    // se guarda como registrado_en y la del reloj puede estar mal.
                    Registrada = DateTime.Now + ventana.Desfase,
                    Matricula = matricula,
                    Sesion = sesion,
                    Computadora = computadora,
                    FallaRed = fallared,
                    ComentarioRed = comentariored,
                    FallaHardware = fallahardware,
                    ComentarioHardware = comentariohardware,
                    FallaSoftware = fallasoftware,
                    ComentarioSoftware = comentariosoftware,
                    EquiposSinInternet = equiposSinInternet ?? "",
                    IdentidadVerificada = identidadVerificada
                });
            }
            catch (Exception ex)
            {
                // La cola es un archivo de este equipo: el fallo es de su disco, no de la red.
                Log.Error("Guardar la bitacora en la cola local", ex);
                return NoGuardada(MotivoNoGuardada.SinCarpeta, "No se pudo guardar tu bitácora en este equipo (no se pudo escribir en su carpeta de datos). "
                    + "Inténtalo de nuevo; si sigue, " + MensajeDeFallo.AvisaAlLaboratorio + ".");
            }

            return ResultadoGuardado.EnEsteEquipo;
        }

        /// <summary>
        /// Con NoGuardada, por que no se guardo la ultima bitacora (GuardarBitacora).
        /// Antes salia en una ventana de Windows y, al cerrarla, la pantalla del alumno
        /// quedaba igual, con Registrar activo y sin camino; ahora la pantalla decide
        /// que hacer con el (Alumno_Principal.Registrar).
        /// </summary>
        public MotivoNoGuardada UltimoMotivo { get; private set; }

        /// <summary>Con NoGuardada, el texto para el alumno, ya redactado.</summary>
        public string UltimoMensaje { get; private set; }

        private ResultadoGuardado NoGuardada(MotivoNoGuardada motivo, string mensaje)
        {
            UltimoMotivo = motivo;
            UltimoMensaje = mensaje;
            return ResultadoGuardado.NoGuardada;
        }

        /// <summary>
        /// Por que no se guarda la bitacora si al pulsar Registrar la clase ya no
        /// esta abierta: lo mismo que dice ReglasCodigo al escribir el codigo. Un
        /// codigo nuevo solo con la generacion de codigos (cada clase tiene el
        /// suyo); con el codigo unico o sin codigos no hay "otro", y si hace falta
        /// una sesion extra la crea el administrador.
        /// </summary>
        /// <param name="modo">El del laboratorio, o null si no se pudo leer.</param>
        internal static string FueraDeHora(VentanaCodigo ventana, ModoCodigo? modo, bool sinServidor = false)
        {
            if (!sinServidor && ventana.Estado == EstadoCodigo.AunNoEmpieza)
            {
                return "Tu clase empieza a las " + ventana.Inicio.ToString("HH:mm") + ": podrás registrar tu bitácora desde las "
                    + ventana.Inicio.AddMinutes(-MargenAntesDelInicioMinutos).ToString("HH:mm") + ".";
            }

            string fin = ventana.Fin.ToString("HH:mm");
            if (ventana.EsUsoLibre)
            {
                // La pantalla del alumno vuelve al paso 1 con este texto (Alumno_Principal.SoltarClase).
                return "El uso libre terminó a las " + fin + ": tu bitácora va en lo que toca ahora, elígelo abajo si no se elige solo. "
                    + "Lo que marcaste se conserva.";
            }

            if (sinServidor)
            {
                return "Tu clase terminó a las " + fin + ".";
            }

            return modo == ModoCodigo.Renovable
                ? "Tu clase terminó a las " + fin + ". Si ya empezó tu siguiente clase, pide a tu docente el código nuevo."
                : "Tu clase terminó a las " + fin + ". Pide a tu docente que avise a la administración de DigLab si necesitas una sesión extra.";
        }

        /// <summary>
        /// Sesiones (una clase en una fecha) para la pantalla del administrador:
        /// una fila por sesion con cuantos alumnos registraron, cuantos
        /// reportaron alguna falla y cuantos entraron sin conexion. El detalle de
        /// cada sesion es consultaRegistroSesion. Con un profesor (su id), solo
        /// las suyas: es su "Mis bitacoras". La tabla lleva el desfase del reloj del
        /// servidor con el de este equipo (DesfaseServidor), para decidir que sesion
        /// sigue en curso con la hora del servidor.
        /// </summary>
        public DataTable ObtenerSesionesAdministrador(int idProfesor = 0)
        {
            DataTable tabla = new DataTable();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                // Las dos horas en el mismo instante: el desfase no lleva lo que tarda la consulta.
                using (MySqlCommand ahora = new MySqlCommand("SELECT NOW()", conexion))
                {
                    DateTime servidor = Convert.ToDateTime(ahora.ExecuteScalar());
                    tabla.ExtendedProperties[ClaveDesfaseServidor] = servidor - DateTime.Now;
                }

                // sin_internet: las listas de equipos que avisaron sus alumnos,
                // unidas con '|' para ContarSinInternet, en la misma pasada por las
                // bitacoras. GROUP_CONCAT se corta en silencio a los 1024
                // caracteres si no se le sube el limite (solo para esta conexion).
                string avisos = "''";
                bool conAvisos = Esquema.EquiposSinInternet(conexion);
                bool conResueltas = Esquema.FallasResueltas(conexion);
                if (conAvisos)
                {
                    using (MySqlCommand limite = new MySqlCommand("SET SESSION group_concat_max_len = 1048576", conexion))
                    {
                        limite.ExecuteNonQuery();
                    }

                    // Sin los de las bitacoras ya resueltas (db/26): esos ya se atendieron.
                    avisos = conResueltas
                        ? "COALESCE(CAST(GROUP_CONCAT(CASE WHEN " + BitacoraSinResolver + " THEN NULLIF(rb.equipos_sin_internet, '') END SEPARATOR '|') AS CHAR), '')"
                        : "COALESCE(CAST(GROUP_CONCAT(NULLIF(rb.equipos_sin_internet, '') SEPARATOR '|') AS CHAR), '')";
                }

                LlenarSesiones(conexion, tabla, idProfesor, avisos, conResueltas);

                // Lo que reporto el docente desde su panel (db/15): en Bitacoras cuenta
                // como falla de la sesion, junto con las de sus estudiantes. Lo ya
                // resuelto (db/26) va aparte.
                Dictionary<int, int> reportes = ReportesProfesorDatos.ContarTodas(conexion);
                Dictionary<int, int> reportesResueltos = ReportesProfesorDatos.ContarResueltas(conexion);
                tabla.Columns.Add("reportes_docente", typeof(int));
                tabla.Columns.Add("reportes_resueltos", typeof(int));
                foreach (DataRow fila in tabla.Rows)
                {
                    int cuantos;
                    int id = Convert.ToInt32(fila["idcodigos_accesos"]);
                    fila["reportes_docente"] = reportes.TryGetValue(id, out cuantos) ? cuantos : 0;
                    fila["reportes_resueltos"] = reportesResueltos.TryGetValue(id, out cuantos) ? cuantos : 0;
                }
            }

            return tabla;
        }

        private const string ClaveDesfaseServidor = "desfase_servidor";

        /// <summary>
        /// El reloj del servidor menos el de este equipo al leer las sesiones
        /// (ObtenerSesionesAdministrador); cero si la tabla no lo trae.
        /// </summary>
        public static TimeSpan DesfaseServidor(DataTable sesiones)
        {
            object desfase = sesiones == null ? null : sesiones.ExtendedProperties[ClaveDesfaseServidor];
            return desfase is TimeSpan ? (TimeSpan)desfase : TimeSpan.Zero;
        }

        private static void LlenarSesiones(MySqlConnection conexion, DataTable tabla, int idProfesor, string avisos, bool conResueltas)
        {
            // Sin esta columna (sin db/26), Bitacoras no ofrece "Resolver fallas...".
            string resueltas = !conResueltas ? "" : @"
                       COUNT(DISTINCT CASE WHEN " + CondicionYaResuelta(conexion) + @" THEN rb.fk_usuario END) AS fallas_resueltas,";

            // LEFT JOIN con el profesor: las de uso libre (db/14) no tienen. El
            // profesor (su "Mis bitacoras") no las ve: el WHERE pide las suyas.
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT ca.idcodigos_accesos,
                       DATE_FORMAT(COALESCE(ca.fecha, DATE(ca.hora_registro)), '%Y-%m-%d') AS fecha,
                       COALESCE(ca.hora_entrada, '') AS hora_entrada,
                       COALESCE(ca.hora_salida, '')  AS hora_salida,
                       ca.codigo,
                       COALESCE(m.nombre_materia, '')     AS materia,
                       COALESCE(g.nombre_grupo, '')       AS grupo,
                       prof.numero_identificador          AS numero_empleado,
                       CONCAT_WS(' ', prof.nombre, prof.apellido_paterno, prof.apellido_materno) AS profesor,
                       COALESCE(l.nombre_laboratorio, '') AS laboratorio,
                       COUNT(DISTINCT rb.fk_usuario)      AS alumnos,
                       COUNT(DISTINCT CASE WHEN " + CondicionFallaPendiente(conexion) + @" THEN rb.fk_usuario END) AS con_fallas," + resueltas + @"
                       COUNT(DISTINCT CASE WHEN rb.registrado_sin_conexion = 1 THEN rb.fk_usuario END) AS sin_conexion,
                       " + avisos + @" AS sin_internet,
                       IF(" + HorariosDatos.CondicionUsoLibre + @", 1, 0) AS uso_libre
                FROM codigos_accesos ca
                LEFT JOIN usuarios prof    ON prof.idusuarios = ca.usuarios_idusuarios
                LEFT JOIN materias m       ON m.id_materia = ca.materias_id_materia
                LEFT JOIN grupos g         ON g.idgrupos = ca.grupos_idgrupos
                LEFT JOIN laboratorios l   ON l.idlaboratorios = ca.laboratorios_idlaboratorios
                LEFT JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos
                WHERE @Profesor = 0 OR ca.usuarios_idusuarios = @Profesor
                GROUP BY ca.idcodigos_accesos, ca.fecha, ca.hora_registro, ca.hora_entrada, ca.hora_salida, ca.codigo,
                         m.nombre_materia, g.nombre_grupo, prof.numero_identificador,
                         prof.nombre, prof.apellido_paterno, prof.apellido_materno, l.nombre_laboratorio,
                         ca.usuarios_idusuarios, ca.horarios_idhorarios, ca.horario_excepciones_id
                ORDER BY fecha DESC, ca.hora_entrada DESC, ca.idcodigos_accesos DESC", conexion))
            using (MySqlDataAdapter adapter = new MySqlDataAdapter(comando))
            {
                comando.Parameters.AddWithValue("@Profesor", idProfesor);
                adapter.Fill(tabla);
            }
        }

        /// <summary>
        /// Los avisos de equipos sin internet de las bitacoras que cumplen la
        /// condicion (sobre rb y ca, con su parametro @Valor si lo usa): por
        /// sesion, la lista de cada bitacora que aviso. Vacio sin la migracion 10.
        /// </summary>
        internal static Dictionary<int, List<string>> LeerAvisosSinInternet(MySqlConnection conexion, string condicion, object valor)
        {
            Dictionary<int, List<string>> avisos = new Dictionary<int, List<string>>();
            if (!Esquema.EquiposSinInternet(conexion))
            {
                return avisos;
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT rb.fk_codigo_accesos, rb.equipos_sin_internet
                FROM registros_bitacoras rb
                INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos
                WHERE rb.equipos_sin_internet <> '' AND " + condicion
                    + (Esquema.FallasResueltas(conexion) ? " AND " + BitacoraSinResolver : ""), conexion))
            {
                comando.Parameters.AddWithValue("@Valor", valor);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int sesion = Convert.ToInt32(reader["fk_codigo_accesos"]);
                        List<string> deLaSesion;
                        if (!avisos.TryGetValue(sesion, out deLaSesion))
                        {
                            deLaSesion = new List<string>();
                            avisos[sesion] = deLaSesion;
                        }

                        deLaSesion.Add(reader["equipos_sin_internet"].ToString());
                    }
                }
            }

            return avisos;
        }

        /// <summary>True si la bitacora reporta alguna falla o comentario (se resalta).</summary>
        public static bool ConFalla(DataRow fila)
        {
            return fila.Table.Columns.Contains("con_falla")
                && fila["con_falla"] != DBNull.Value
                && Convert.ToInt32(fila["con_falla"]) == 1;
        }

        /// <summary>True si lo que reporta esa bitacora (una falla, un comentario o un aviso de otros equipos) ya se dio por resuelto (db/26).</summary>
        public static bool Resuelta(DataRow fila)
        {
            return fila.Table.Columns.Contains("falla_resuelta")
                && fila["falla_resuelta"] != DBNull.Value
                && Convert.ToInt32(fila["falla_resuelta"]) == 1
                && (ConFalla(fila) || AvisoSinInternet(fila).Length > 0);
        }

        /// <summary>True si reporta una falla o un comentario sin resolver: lo que va en naranja.</summary>
        public static bool FallaPendiente(DataRow fila)
        {
            return ConFalla(fila) && !Resuelta(fila);
        }

        /// <summary>"Resuelta el 06/11/2026 a las 10:30. Se cambio el teclado."; vacio si no esta resuelta.</summary>
        public static string TextoResuelta(DataRow fila)
        {
            return !Resuelta(fila) ? "" : ReporteProfesor.FraseDeResuelta(Convert.ToString(fila["resuelta_en"]), Convert.ToString(fila["resuelta_nota"]));
        }

        /// <summary>Lo que reporto (DescribirFallas) y, si ya se resolvio, que se resolvio: para las pantallas.</summary>
        public static string DescribirConEstado(DataRow fila)
        {
            string fallas = DescribirFallas(fila);
            return Resuelta(fila) ? fallas + ". " + TextoResuelta(fila) : fallas;
        }

        /// <summary>Las tres areas que puede reportar el alumno, en el orden en que se muestran.</summary>
        public static readonly string[] AreasDeFalla = { "red", "hardware", "software" };

        // ---------------------------------------------------------------------
        // Equipos de alrededor sin internet (db/10)
        // ---------------------------------------------------------------------

        public const int MaximoEquiposSinInternet = 5;
        // Lo que cabe en registros_bitacoras.equipos_sin_internet: en la base de
        // la escuela (sql_mode vacio) lo que sobrara se cortaria sin avisar.
        private const int LargoMaximoAviso = 120;
        private static readonly Regex NumeroDeEquipo = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 \-]{0,19}$");
        private static readonly Regex SeparadorDeEquipos = new Regex(@"\s*(?:,|;|\by\b)\s*", RegexOptions.IgnoreCase);
        private static readonly Regex NumeroConPrefijo = new Regex(@"^(.*?)(\d+)$");

        /// <summary>
        /// "11, 13 y 14" -> "PC-11,PC-13,PC-14", listo para guardar: sin
        /// repetidos, como mucho MaximoEquiposSinInternet, cada uno con al menos
        /// una cifra, y con el nombre que tiene la misma maquina se avise desde
        /// donde se avise (ver ComoEsteEquipo). "8 9" son dos; "pc 8" es uno.
        /// Null y el error redactado para el alumno si no se puede guardar.
        /// </summary>
        /// <param name="esteEquipo">El numero configurado de esta maquina, o null (entonces se guarda lo escrito).</param>
        public static string NormalizarEquiposSinInternet(string texto, string esteEquipo, out string error)
        {
            const string SoloNumeros = "Escribe los números de las computadoras, separados por comas (por ejemplo: 11, 13).";
            error = null;
            List<string> equipos = new List<string>();
            HashSet<string> claves = new HashSet<string>(StringComparer.Ordinal);
            foreach (string trozo in SeparadorDeEquipos.Split(texto ?? ""))
            {
                // Una palabra sin cifras es el prefijo de la que la sigue ("pc 8").
                string prefijo = "";
                foreach (string palabra in trozo.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!palabra.Any(char.IsDigit))
                    {
                        prefijo += palabra + " ";
                        continue;
                    }

                    string equipo = (prefijo + palabra).Trim();
                    prefijo = "";
                    if (!NumeroDeEquipo.IsMatch(equipo))
                    {
                        error = SoloNumeros;
                        return null;
                    }

                    equipo = ComoEsteEquipo(equipo, esteEquipo);
                    if (claves.Add(ClaveDeEquipo(equipo)))
                    {
                        equipos.Add(equipo);
                    }
                }

                if (prefijo.Length > 0)
                {
                    // Palabras sin ningun numero: "todas", "la de enfrente".
                    error = SoloNumeros;
                    return null;
                }
            }

            if (equipos.Count == 0)
            {
                error = "Escribe el número de la computadora sin internet, o elige «No».";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(esteEquipo) && claves.Contains(ClaveDeEquipo(esteEquipo)))
            {
                error = "La " + esteEquipo.Trim() + " es esta computadora: si no tiene internet, elige «Reportar una falla» en el paso 2.";
                return null;
            }

            if (equipos.Count > MaximoEquiposSinInternet)
            {
                error = "Avisa solo de las de tu alrededor (hasta " + MaximoEquiposSinInternet + ").";
                return null;
            }

            string lista = string.Join(",", equipos);
            if (lista.Length > LargoMaximoAviso)
            {
                error = "Son demasiadas para un aviso: escribe solo las que tengas más cerca.";
                return null;
            }

            return lista;
        }

        /// <summary>
        /// El nombre que se guarda: un numero solo ("8") o uno con el mismo
        /// prefijo que este equipo escrito de otra forma ("pc 8", "PC08",
        /// "PC-008") lleva el prefijo de este equipo y la cifra SIN ceros a la
        /// izquierda: "PC-8". Sin ceros porque desde la PC-10 no se sabe si el
        /// laboratorio los usa, y asi la misma maquina se llama igual la avise
        /// quien la avise. Uno de otro formato se queda como se escribio; si este
        /// equipo es solo un numero, "PC-8" se guarda como "8".
        /// </summary>
        private static string ComoEsteEquipo(string equipo, string esteEquipo)
        {
            if (string.IsNullOrWhiteSpace(esteEquipo))
            {
                return equipo;
            }

            Match este = NumeroConPrefijo.Match(esteEquipo.Trim());
            Match escrito = NumeroConPrefijo.Match(equipo);
            if (!este.Success || !escrito.Success)
            {
                return equipo;
            }

            // Si este equipo es solo un numero ("7"), una etiqueta sin cifras delante
            // ("PC-8", "pc 8") es la misma forma de llamarlas: se guarda "8".
            string prefijo = escrito.Groups[1].Value;
            string prefijoEste = SoloLetrasYCifras(este.Groups[1].Value);
            bool mismoPrefijo = SoloLetrasYCifras(prefijo) == prefijoEste
                || (prefijoEste.Length == 0 && !prefijo.Any(char.IsDigit));
            if (prefijo.Length > 0 && !mismoPrefijo)
            {
                return equipo;
            }

            return este.Groups[1].Value + SinCeros(escrito.Groups[2].Value);
        }

        /// <summary>Para comparar maquinas: "PC-07", "pc 7" y "PC7" son la misma ("PC|7").</summary>
        internal static string ClaveDeEquipo(string equipo)
        {
            string limpio = (equipo ?? "").Trim();
            Match partes = NumeroConPrefijo.Match(limpio);
            return partes.Success
                ? SoloLetrasYCifras(partes.Groups[1].Value) + "|" + SinCeros(partes.Groups[2].Value)
                : SoloLetrasYCifras(limpio);
        }

        private static string SoloLetrasYCifras(string texto)
        {
            return new string(texto.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static string SinCeros(string cifras)
        {
            string numero = cifras.TrimStart('0');
            return numero.Length == 0 ? "0" : numero;
        }

        /// <summary>
        /// Cuenta los avisos: cada texto es la lista de una bitacora ("12,7") o
        /// varias unidas con '|'. Una maquina cuenta una vez por bitacora, y se
        /// junta aunque se haya escrito distinto (ClaveDeEquipo). Las mas avisadas
        /// primero; a igual numero, en orden (PC-2 antes que PC-10).
        /// </summary>
        public static List<EquipoSinInternet> ContarSinInternet(IEnumerable<string> listas)
        {
            Dictionary<string, EquipoSinInternet> porEquipo = new Dictionary<string, EquipoSinInternet>(StringComparer.Ordinal);
            foreach (string bitacora in listas.Where(l => !string.IsNullOrWhiteSpace(l)).SelectMany(l => l.Split('|')))
            {
                HashSet<string> enEsta = new HashSet<string>(StringComparer.Ordinal);
                foreach (string equipo in bitacora.Split(',').Select(e => e.Trim()).Where(e => e.Length > 0))
                {
                    string clave = ClaveDeEquipo(equipo);
                    if (!enEsta.Add(clave))
                    {
                        continue;
                    }

                    EquipoSinInternet contado;
                    if (!porEquipo.TryGetValue(clave, out contado))
                    {
                        contado = new EquipoSinInternet { Equipo = equipo };
                        porEquipo[clave] = contado;
                    }

                    contado.Alumnos++;
                }
            }

            return porEquipo.Values
                .OrderByDescending(e => e.Alumnos)
                .ThenBy(e => OrdenDeEquipo(e.Equipo))
                .ToList();
        }

        /// <summary>Prefijo y numero, para ordenar PC-2 antes que PC-10.</summary>
        private static Tuple<string, long> OrdenDeEquipo(string equipo)
        {
            Match partes = NumeroConPrefijo.Match(equipo ?? "");
            long numero;
            return partes.Success && long.TryParse(SinCeros(partes.Groups[2].Value), out numero)
                ? Tuple.Create(SoloLetrasYCifras(partes.Groups[1].Value), numero)
                : Tuple.Create(SoloLetrasYCifras(equipo ?? ""), -1L);
        }

        // ---------------------------------------------------------------------
        // Los equipos del laboratorio, para marcarlos en el paso 3 (sin migracion:
        // no hay tabla de equipos, solo las bitacoras que registraron)
        // ---------------------------------------------------------------------

        /// <summary>Dias hacia atras: los mismos que la columna "Equipos (30 dias)" de Laboratorios en el ADM.</summary>
        public const int DiasEquiposRegistrados = 30;

        // Lo que se ofrece para marcar: letras, cifras, espacio, punto, guion y
        // guion bajo, empezando por letra o cifra, hasta 30. Nada de ',' ni '|'
        // (separan los avisos guardados) ni '&' o caracteres de control (en la
        // casilla y en la copia local).
        private static readonly Regex NombreDeEquipoRegistrado = new Regex(@"^[\p{L}\p{Nd}][\p{L}\p{Nd} ._\-]{0,29}\z");
        // Los que pone Windows si nadie fijo el numero: no dicen que maquina es.
        private static readonly Regex NombreDeWindows = new Regex(@"^(DESKTOP|LAPTOP|WIN)-", RegexOptions.IgnoreCase);

        /// <summary>
        /// El nombre que pone Windows si nadie fijo el numero de la maquina
        /// (DESKTOP-..., LAPTOP-..., WIN-...): no dice que equipo es. Lo usan la
        /// lista del paso 3 (LimpiarEquiposRegistrados) y Estadisticas (Equipos).
        /// </summary>
        internal static bool EsNombreDeWindows(string equipo)
        {
            return NombreDeWindows.IsMatch((equipo ?? "").Trim());
        }

        /// <summary>
        /// Los equipos que registraron bitacoras en este laboratorio en los ultimos
        /// DiasEquiposRegistrados dias, ya limpios (LimpiarEquiposRegistrados).
        /// Solo las del DigLab nuevo (registrado_en): el sistema anterior guardaba
        /// el nombre de Windows de donde corriera, a veces el de una persona, y no
        /// sabia de laboratorios. Puede lanzar: quien llama decide (sin lista, el
        /// alumno escribe el numero).
        /// </summary>
        public static List<string> EquiposDelLaboratorio(MySqlConnection conexion, int idLaboratorio)
        {
            List<KeyValuePair<string, long>> leidos = new List<KeyValuePair<string, long>>();
            // GROUP BY BINARY: si no, la base junta "pc-07" con "PC-07" y se queda
            // con cualquiera; se juntan en C#, con la escritura mas reciente. MIN y
            // UNIX_TIMESTAMP: valen con ONLY_FULL_GROUP_BY (MySQL 8) y en MariaDB 5.5,
            // y una fecha en ceros no rompe la lectura.
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT MIN(rb.numero_computadora) AS equipo, UNIX_TIMESTAMP(MAX(rb.registrado_en)) AS ultima
                FROM registros_bitacoras rb
                INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos
                WHERE ca.laboratorios_idlaboratorios = @Lab
                  AND COALESCE(ca.fecha, DATE(ca.hora_registro)) >= CURDATE() - INTERVAL @Dias DAY
                  AND rb.registrado_en IS NOT NULL
                  AND rb.numero_computadora IS NOT NULL AND rb.numero_computadora <> ''
                GROUP BY BINARY rb.numero_computadora", conexion))
            {
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Dias", DiasEquiposRegistrados);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long ultima = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1));
                        leidos.Add(new KeyValuePair<string, long>(reader.GetString(0), ultima));
                    }
                }
            }

            return LimpiarEquiposRegistrados(leidos);
        }

        /// <summary>
        /// Lo leido de la base (nombre y cuando registro por ultima vez), listo para
        /// marcar: sin los nombres que no se podrian guardar en un aviso ni los que
        /// pone Windows (DESKTOP-...), con las formas de llamar a la misma maquina
        /// juntas (MismoEquipo: "PC-07" y "pc 7", o "7" y "DATABASE-07" si se
        /// renombro; queda la mas reciente) y en orden (PC-2 antes que PC-10).
        /// </summary>
        internal static List<string> LimpiarEquiposRegistrados(IEnumerable<KeyValuePair<string, long>> leidos)
        {
            List<string> quedan = new List<string>();
            foreach (KeyValuePair<string, long> leido in leidos
                .Select(l => new KeyValuePair<string, long>((l.Key ?? "").Trim(), l.Value))
                .Where(l => NombreDeEquipoRegistrado.IsMatch(l.Key) && !EsNombreDeWindows(l.Key))
                .OrderByDescending(l => l.Value)
                .ThenBy(l => l.Key, StringComparer.Ordinal))
            {
                // De la mas reciente a la mas vieja: la que ya esta gana.
                if (!quedan.Any(q => MismoEquipo(q, leido.Key)))
                {
                    quedan.Add(leido.Key);
                }
            }

            return quedan
                .OrderBy(OrdenDeEquipo)
                .ThenBy(e => e, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Si dos nombres son la misma maquina: escrita distinto ("PC-07", "pc 7",
        /// "PC7"), o una solo con el numero y la otra con una etiqueta sin cifras
        /// delante ("7" y "PC-07"), como en ComoEsteEquipo.
        /// </summary>
        internal static bool MismoEquipo(string uno, string otro)
        {
            if (string.IsNullOrWhiteSpace(uno) || string.IsNullOrWhiteSpace(otro))
            {
                return false;
            }

            if (ClaveDeEquipo(uno) == ClaveDeEquipo(otro))
            {
                return true;
            }

            Match a = NumeroConPrefijo.Match(uno.Trim());
            Match b = NumeroConPrefijo.Match(otro.Trim());
            if (!a.Success || !b.Success || SinCeros(a.Groups[2].Value) != SinCeros(b.Groups[2].Value))
            {
                return false;
            }

            string prefijoA = a.Groups[1].Value;
            string prefijoB = b.Groups[1].Value;
            return (prefijoA.Length == 0 && !prefijoB.Any(char.IsDigit)) || (prefijoB.Length == 0 && !prefijoA.Any(char.IsDigit));
        }

        /// <summary>
        /// El aviso del paso 3 cuando hay lista: lo marcado, tal cual sale en la
        /// lista, y lo escrito para las que no estan (NormalizarEquiposSinInternet).
        /// Una escrita que es de la lista se guarda con el nombre de la lista: en la
        /// "11", escribir "7" es la "PC-07", no otra. Los mismos limites que al
        /// escribirlas (MaximoEquiposSinInternet, LargoMaximoAviso). Null y el error
        /// redactado para el alumno si no se puede guardar.
        /// </summary>
        /// <param name="lista">Lo que se le ofrecio para marcar (vacia si no hubo lista).</param>
        /// <param name="esteEquipo">El numero configurado de esta maquina, o null.</param>
        public static string UnirEquiposSinInternet(IList<string> marcados, IList<string> lista, string escrito, string esteEquipo, out string error)
        {
            error = null;
            lista = lista ?? new List<string>();
            List<string> equipos = new List<string>();
            foreach (string marcado in marcados ?? new List<string>())
            {
                string equipo = (marcado ?? "").Trim();
                if (equipo.Length > 0 && !equipos.Any(e => MismoEquipo(e, equipo)))
                {
                    equipos.Add(equipo);
                }
            }

            // Solo comas, ";" o "y" (lo que queda al borrar un numero) no es haber escrito nada.
            if (!string.IsNullOrWhiteSpace(SeparadorDeEquipos.Replace(escrito ?? "", "")))
            {
                string escritos = NormalizarEquiposSinInternet(escrito, esteEquipo, out error);
                if (error != null)
                {
                    return null;
                }

                foreach (string equipo in escritos.Split(','))
                {
                    string deLaLista = lista.FirstOrDefault(l => MismoEquipo(l, equipo)) ?? equipo;
                    if (!equipos.Any(e => MismoEquipo(e, deLaLista)))
                    {
                        equipos.Add(deLaLista);
                    }
                }
            }

            if (equipos.Count == 0)
            {
                error = lista.Count > 0
                    ? "Marca la computadora sin internet (o escribe su número), o elige «No»."
                    : "Escribe el número de la computadora sin internet, o elige «No».";
                return null;
            }

            string esta = equipos.FirstOrDefault(e => MismoEquipo(e, esteEquipo));
            if (esta != null)
            {
                error = "La " + esta + " es esta computadora: si no tiene internet, elige «Reportar una falla» en el paso 2.";
                return null;
            }

            if (equipos.Count > MaximoEquiposSinInternet)
            {
                error = "Avisa solo de las de tu alrededor (hasta " + MaximoEquiposSinInternet + ").";
                return null;
            }

            string aviso = string.Join(",", equipos);
            if (aviso.Length > LargoMaximoAviso)
            {
                error = "Son demasiadas para un aviso: marca solo las que tengas más cerca.";
                return null;
            }

            return aviso;
        }

        /// <summary>
        /// Corto, para una celda: "Sin internet: 12 (2 avisos)" o "Sin internet:
        /// 12 (2), 7 (1), 9 (1) y 2 más" (solo las tres mas avisadas). Largo:
        /// "Sin internet: equipo 12 (lo avisaron 2 alumnos) y equipo 7 (lo avisó 1
        /// alumno)", todas. Vacio si no hay ninguno.
        /// </summary>
        public static string DescribirSinInternet(IList<EquipoSinInternet> equipos, bool corto)
        {
            if (equipos == null || equipos.Count == 0)
            {
                return "";
            }

            if (corto)
            {
                const int Visibles = 3;
                return "Sin internet: " + (equipos.Count == 1
                    ? equipos[0].Equipo + " (" + equipos[0].Alumnos + (equipos[0].Alumnos == 1 ? " aviso)" : " avisos)")
                    : string.Join(", ", equipos.Take(Visibles).Select(e => e.Equipo + " (" + e.Alumnos + ")"))
                      + (equipos.Count > Visibles ? " y " + (equipos.Count - Visibles) + " más" : ""));
            }

            return "Sin internet: " + Unir(equipos.Select(e => "equipo " + e.Equipo
                + (e.Alumnos == 1 ? " (lo avisó 1 estudiante)" : " (lo avisaron " + e.Alumnos + " estudiantes)")).ToList());
        }

        /// <summary>
        /// Lo que aviso un alumno de los equipos de alrededor, para el reporte de
        /// su fila (tablas y PDF): "Avisó sin internet: 11 y 13", o "" si nada.
        /// </summary>
        public static string AvisoSinInternet(DataRow fila)
        {
            if (!fila.Table.Columns.Contains("avisa_sin_internet"))
            {
                return "";
            }

            List<string> equipos = fila["avisa_sin_internet"].ToString().Split(',').Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
            return equipos.Count == 0 ? "" : "Avisó sin internet: " + Unir(equipos);
        }

        /// <summary>"a", "a y b", "a, b y c".</summary>
        private static string Unir(IList<string> partes)
        {
            return partes.Count == 1
                ? partes[0]
                : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[partes.Count - 1];
        }

        /// <summary>
        /// Las fallas de la lista de cada area, las que elige el alumno al
        /// registrar (sin "Ninguno" ni "Otro", que pone cada pantalla). Lo que se
        /// guarda es este texto: el administrador lo ve tal cual.
        /// </summary>
        public static string[] OpcionesDeFalla(string area)
        {
            switch (area)
            {
                case "red":
                    return new[] { "Sin cable de red", "Con cable de red sin internet", "Desconectado de cable de red" };
                case "hardware":
                    return new[] { "No sirve el teclado", "No sirve el mouse", "Monitor no enciende", "Computadora no enciende" };
                case "software":
                    return new[] { "Muy lenta", "Se apaga sola", "Pantallazo azul", "Programas faltantes" };
                default:
                    return new string[0];
            }
        }

        /// <summary>
        /// Como se lee en pantalla una falla de la lista (OpcionesDeFalla): las tres
        /// de red se distinguen de un vistazo ("Sin cable de red" y "Desconectado de
        /// cable de red" casi no se distinguian). Un solo sitio para las pantallas que
        /// la ofrecen: la del estudiante, "Reportar una falla" del docente y
        /// "Corregir registro". Lo que se guarda (y DescribirFallas, el PDF y los
        /// reportes) sigue siendo el valor de siempre; lo que no es de la lista, tal cual.
        /// </summary>
        public static string EtiquetaDeFalla(string valor)
        {
            switch (valor)
            {
                case "Sin cable de red":
                    return "No tiene cable de red";
                case "Desconectado de cable de red":
                    return "El cable está suelto o desconectado";
                case "Con cable de red sin internet":
                    return "Tiene cable pero no hay internet";
                default:
                    return valor ?? "";
            }
        }

        /// <summary>
        /// Al reves que EtiquetaDeFalla: lo que se guarda de lo que se lee en una caja
        /// donde tambien se puede escribir (Corregir registro). Lo escrito a mano, tal cual.
        /// </summary>
        public static string ValorDeFalla(string texto)
        {
            string limpio = (texto ?? "").Trim();
            foreach (string area in AreasDeFalla)
            {
                foreach (string valor in OpcionesDeFalla(area))
                {
                    if (string.Equals(EtiquetaDeFalla(valor), limpio, StringComparison.OrdinalIgnoreCase))
                    {
                        return valor;
                    }
                }
            }

            return limpio;
        }

        /// <summary>
        /// El id de un usuario por su matricula; 0 si no existe. Sin avisos: un
        /// error de conexion sube, para que la pantalla del alumno pase a la copia
        /// sin conexion (idusuario lo tapaba con un mensaje y lo dejaba atascado).
        /// </summary>
        public int IdDeUsuario(string numeroIdentificador)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idusuarios FROM usuarios WHERE numero_identificador = @Numero", conexion))
            {
                comando.Parameters.AddWithValue("@Numero", numeroIdentificador ?? "");
                object resultado = comando.ExecuteScalar();
                return resultado == null || resultado == DBNull.Value ? 0 : Convert.ToInt32(resultado);
            }
        }

        /// <summary>"Nombre Apellidos" de un usuario por su matricula o numero de empleado; null si no existe. Sin avisos: el error sube.</summary>
        public string NombreDeUsuario(string numeroIdentificador)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT CONCAT_WS(' ', nombre, apellido_paterno, apellido_materno) FROM usuarios WHERE numero_identificador = @Numero", conexion))
            {
                comando.Parameters.AddWithValue("@Numero", numeroIdentificador ?? "");
                object resultado = comando.ExecuteScalar();
                return resultado == null || resultado == DBNull.Value ? null : resultado.ToString();
            }
        }

        /// <summary>
        /// True si ese valor es algo que el alumno reporto. Mismo criterio que la
        /// consulta: vacio, "Ninguno" (con espacios o sin ellos) y "EP" o
        /// "Equipo Propio" no son novedad.
        /// </summary>
        public static bool EsNovedad(object valor)
        {
            if (valor == null || valor == DBNull.Value)
            {
                return false;
            }

            string texto = valor.ToString().Trim();
            return texto.Length > 0
                && !texto.Equals("Ninguno", StringComparison.OrdinalIgnoreCase)
                && !texto.Equals("EP", StringComparison.OrdinalIgnoreCase)
                && !texto.Equals("Equipo Propio", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True si esa area concreta (red, hardware o software) reporta algo.</summary>
        public static bool ConFallaEn(DataRow fila, string area)
        {
            return (fila.Table.Columns.Contains("falla_" + area) && EsNovedad(fila["falla_" + area]))
                || (fila.Table.Columns.Contains("comentarios_" + area) && EsNovedad(fila["comentarios_" + area]));
        }

        /// <summary>True si esa columna es una de las seis de falla o comentario.</summary>
        public static bool EsColumnaDeFalla(string columna, out string area)
        {
            foreach (string candidata in AreasDeFalla)
            {
                if (columna == "falla_" + candidata || columna == "comentarios_" + candidata)
                {
                    area = candidata;
                    return true;
                }
            }

            area = null;
            return false;
        }

        /// <summary>
        /// Lo que el alumno escribio en un area: su comentario, o el tipo de
        /// falla si no dejo comentario. Cadena vacia si no reporto nada ahi.
        /// </summary>
        public static string DetalleDeFalla(DataRow fila, string area)
        {
            if (!ConFallaEn(fila, area))
            {
                return "";
            }

            return fila.Table.Columns.Contains("comentarios_" + area) && EsNovedad(fila["comentarios_" + area])
                ? fila["comentarios_" + area].ToString().Trim()
                : fila["falla_" + area].ToString().Trim();
        }

        /// <summary>
        /// Solo lo que el alumno reporto, para la columna de comentarios de las
        /// tablas y el PDF: "Red: sin internet | Hardware: teclado", y al final lo
        /// que aviso de otros equipos (AvisoSinInternet). Si no reporto nada,
        /// "Sin novedad".
        /// </summary>
        public static string DescribirFallas(DataRow fila)
        {
            List<string> partes = new List<string>();
            foreach (string area in AreasDeFalla)
            {
                if (!ConFallaEn(fila, area))
                {
                    continue;
                }

                partes.Add(char.ToUpper(area[0]) + area.Substring(1) + ": " + DetalleDeFalla(fila, area));
            }

            string aviso = AvisoSinInternet(fila);
            if (aviso.Length > 0)
            {
                partes.Add(aviso);
            }

            return partes.Count == 0 ? "Sin novedad" : string.Join(" | ", partes);
        }

        /// <summary>
        /// Guarda la bitacora del alumno en una sesion. Sustituye a consultaFinal
        /// (que buscaba la sesion por el texto del codigo). Dice donde quedo: en
        /// el servidor, o en este equipo (sin conexion, o porque la red se cayo
        /// justo al guardar), de donde se envia sola.
        /// </summary>
        /// <param name="equiposSinInternet">Otros equipos que el alumno vio sin internet ("11,13"), o null.</param>
        public ResultadoGuardado GuardarBitacora(int idSesion, string matricula, string computadora, string fallared, string comentariored,
            string fallahardware, string comentariohardware, string fallasotware, string comentariosoftware, string equiposSinInternet)
        {
            UltimoMotivo = MotivoNoGuardada.Ninguno;
            UltimoMensaje = "";

            // Fase 4: sin servidor la bitacora se guarda en la cola local del equipo.
            if (SinConexion.Activo || (idSesion == 0 && Datos_User.SesionSinConexion != null))
            {
                return GuardarSinConexion(Datos_User.SesionSinConexion, false, matricula, computadora, fallared, comentariored,
                    fallahardware, comentariohardware, fallasotware, comentariosoftware, equiposSinInternet);
            }

            try
            {
                VentanaCodigo ventana = ObtenerVentanaSesion(idSesion);
                if (ventana == null)
                {
                    return NoGuardada(MotivoNoGuardada.NoExiste, "La sesión de esta clase ya no existe: vuelve a escribir el código o a elegir tu clase.");
                }

                if (ventana.Estado != EstadoCodigo.Vigente)
                {
                    // Con el modo del laboratorio se sabe que hacer; sin el, lo de siempre.
                    ModoCodigo? modo = null;
                    try
                    {
                        using (MySqlConnection conexion = mconexion.GetConexion())
                        {
                            modo = ModosDeAcceso.Leer(conexion, ventana.IdLaboratorio);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Aviso("Bitacora de " + matricula + " fuera de hora: no se pudo leer el modo del laboratorio: " + ex.Message);
                    }

                    return NoGuardada(MotivoNoGuardada.Terminada, FueraDeHora(ventana, modo));
                }

                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    int idUsuario;
                    using (MySqlCommand cmdUsuario = new MySqlCommand("SELECT idusuarios FROM usuarios WHERE numero_identificador = @matricula", conexion))
                    {
                        cmdUsuario.Parameters.AddWithValue("@matricula", matricula);
                        object resultado = cmdUsuario.ExecuteScalar();
                        if (resultado == null)
                        {
                            throw new BitacoraImposibleException("No se encontro al alumno con matricula " + matricula + ".");
                        }

                        idUsuario = Convert.ToInt32(resultado);
                    }

                    // Solo la primera bitacora del alumno en la sesion cuenta, y con ella
                    // la hora de registro (registrado_en, reloj del servidor). Si vuelve
                    // a registrarse (se fue la luz, se reinicio el equipo) no se inserta
                    // otra ni se cambia la hora: con clases seguidas del mismo profesor,
                    // la hora es lo que dice cuando llego. No basta el UNIQUE
                    // (fk_codigo_accesos, fk_usuario) de la 03: una base con duplicados
                    // viejos (la base vieja de la escuela) no lo tiene.
                    // Los equipos sin internet y la hora de llegada, solo si la base ya
                    // tiene sus columnas (db/10): sin ellas todo sigue como antes.
                    bool conAviso = !string.IsNullOrEmpty(equiposSinInternet) && Esquema.EquiposSinInternet(conexion);
                    bool conLlegada = Esquema.HoraDeLlegada(conexion);
                    if (!string.IsNullOrEmpty(equiposSinInternet) && !conAviso)
                    {
                        Log.Aviso("El alumno " + matricula + " aviso de equipos sin internet (" + equiposSinInternet
                            + "), pero la base aun no tiene donde guardarlo (falta db/10).");
                    }

                    using (MySqlCommand cmdInsertar = new MySqlCommand(@"
                        INSERT INTO registros_bitacoras
                            (fk_codigo_accesos, fk_usuario, numero_computadora, falla_red, comentarios_red, falla_hardware, comentarios_hardware,
                             falla_software, comentarios_software, registrado_en"
                            + (conAviso ? ", equipos_sin_internet" : "") + (conLlegada ? ", recibido_en" : "") + @")
                        SELECT @IdCodigoAcceso, @codigo_usuario, @computadora, @fallared, @comentariored, @fallahardware, @comentariohardware,
                               @fallasoftware, @comentariosoftware, NOW()"
                            + (conAviso ? ", @equipos" : "") + (conLlegada ? ", NOW()" : "") + @"
                        FROM DUAL
                        WHERE NOT EXISTS (SELECT 1 FROM registros_bitacoras WHERE fk_codigo_accesos = @IdCodigoAcceso AND fk_usuario = @codigo_usuario)", conexion))
                    {
                        cmdInsertar.Parameters.AddWithValue("@equipos", equiposSinInternet ?? "");
                        cmdInsertar.Parameters.AddWithValue("@IdCodigoAcceso", idSesion);
                        cmdInsertar.Parameters.AddWithValue("@codigo_usuario", idUsuario);
                        cmdInsertar.Parameters.AddWithValue("@computadora", computadora);
                        cmdInsertar.Parameters.AddWithValue("@fallared", fallared);
                        cmdInsertar.Parameters.AddWithValue("@comentariored", comentariored);
                        cmdInsertar.Parameters.AddWithValue("@fallahardware", fallahardware);
                        cmdInsertar.Parameters.AddWithValue("@comentariohardware", comentariohardware);
                        cmdInsertar.Parameters.AddWithValue("@fallasoftware", fallasotware);
                        cmdInsertar.Parameters.AddWithValue("@comentariosoftware", comentariosoftware);

                        int filas;
                        try
                        {
                            filas = cmdInsertar.ExecuteNonQuery();
                        }
                        catch (MySqlException ex) when (ex.Number == 1213)
                        {
                            // Dos alumnos de la misma sesion a la vez: el NOT EXISTS
                            // puede interbloquearse. InnoDB deshizo la orden; una vez mas.
                            Log.Aviso("Bitacora de " + matricula + ": interbloqueo al guardar; se reintenta una vez.");
                            filas = cmdInsertar.ExecuteNonQuery();
                        }

                        if (filas == 0)
                        {
                            // Ya tenia una: la primera se queda como estaba, con su hora.
                            Log.Info("Bitacora de " + matricula + " no guardada: ya tenia una en la sesion " + idSesion + ".");
                            return NoGuardada(MotivoNoGuardada.Duplicada, "Ya registraste tu bitácora en esta clase.");
                        }

                        if (filas != 1)
                        {
                            // -1: el servidor aborto la orden. No se da por guardada: va a la cola.
                            throw new EnvioIncompletoException("El servidor no confirmo la bitacora de " + matricula + " (respuesta " + filas + ").");
                        }

                        // Sin mensaje: la pantalla del alumno confirma el registro.
                        return ResultadoGuardado.EnServidor;
                    }
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return NoGuardada(MotivoNoGuardada.Duplicada, "Ya registraste tu bitácora en esta clase.");
            }
            catch (BitacoraImposibleException ex)
            {
                // Su matricula ya no esta en la base (se borro su cuenta despues de
                // entrar): el texto de la excepcion es para el log.
                Log.Error("Guardar la bitacora en la sesion " + idSesion, ex);
                return NoGuardada(MotivoNoGuardada.SinMatricula, "Tu matrícula no está registrada en el servidor. Avisa a la persona responsable del laboratorio.");
            }
            catch (Exception ex) when (ColaBitacoras.Clasificar(ex) != ClaseDeFallo.DeLaBitacora)
            {
                // Se cayo la red (o el servidor no acepta la bitacora) justo al
                // guardar. Antes se perdia con un "Error al insertar". Ahora va a la
                // cola, como sin conexion, y se envia sola. Es seguro aunque el
                // INSERT hubiera llegado a confirmarse antes del corte: al subirla,
                // el NOT EXISTS la descarta como repetida.
                Log.Error("Guardar la bitacora en la sesion " + idSesion + "; se guarda en este equipo para enviarla despues", ex);
                VentanaCodigo validada = Datos_User.VentanaActual;
                if (validada == null || validada.IdCodigo != idSesion)
                {
                    return NoGuardada(MotivoNoGuardada.SinConexion, "No se pudo guardar tu bitácora: no hubo conexión con el servidor. Inténtalo de nuevo en un momento.");
                }

                ResultadoGuardado guardado = GuardarSinConexion(SesionLocal.DesdeVentana(validada), true, matricula, computadora, fallared, comentariored,
                    fallahardware, comentariohardware, fallasotware, comentariosoftware, equiposSinInternet);
                if (guardado == ResultadoGuardado.EnEsteEquipo && ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion)
                {
                    // Que el siguiente alumno no espere el tiempo de conexion en cada
                    // paso: si hay copia local, a modo sin conexion hasta que vuelva.
                    // Solo si de verdad no se llega al servidor: con un bloqueo o un
                    // permiso que falta, el siguiente entraria sin contrasena.
                    SinConexion.IntentarActivar(ex.Message);
                }

                return guardado;
            }
            catch (Exception ex)
            {
                // Aqui solo llega lo que es de la bitacora (Clasificar): un dato que la base
                // rechaza, como un comentario demasiado largo o con simbolos que no admite.
                Log.Error("Guardar la bitacora en la sesion " + idSesion, ex);
                return NoGuardada(MotivoNoGuardada.DatoRechazado, "El servidor no aceptó tu bitácora (un comentario muy largo o con símbolos que no admite, por ejemplo). "
                    + "Acorta o revisa lo que escribiste en «Otro» e inténtalo de nuevo; si sigue, " + MensajeDeFallo.AvisaAlLaboratorio + ".");
            }
        }
    }

    /// <summary>Donde quedo una bitacora al pulsar Registrar.</summary>
    internal enum ResultadoGuardado
    {
        NoGuardada,
        EnServidor,
        /// <summary>En la cola de este equipo: se envia sola (EnviadorBitacoras).</summary>
        EnEsteEquipo
    }

    /// <summary>Por que no se guardo una bitacora (Consultas.UltimoMotivo).</summary>
    internal enum MotivoNoGuardada
    {
        Ninguno,
        /// <summary>Ya tenia una en esa sesion (en el servidor o en la cola de este equipo): solo cuenta la primera.</summary>
        Duplicada,
        /// <summary>La clase (o el uso libre) ya termino, o aun no empieza.</summary>
        Terminada,
        /// <summary>La sesion ya no existe, o no hay una valida para guardar.</summary>
        NoExiste,
        /// <summary>Su matricula ya no esta en la base.</summary>
        SinMatricula,
        /// <summary>Sin servidor, no se pudo escribir en la carpeta de datos de este equipo.</summary>
        SinCarpeta,
        /// <summary>El servidor rechazo un dato (un comentario demasiado largo, por ejemplo).</summary>
        DatoRechazado,
        /// <summary>Se cayo la red al guardar y no se pudo dejar en este equipo.</summary>
        SinConexion
    }

    /// <summary>Lo que va arriba en el PDF de una sesion (Consultas.ConsultarDatosPdfSesion), ya en texto.</summary>
    internal sealed class EncabezadoPdfSesion
    {
        /// <summary>"07:00".</summary>
        public string HoraEntrada;
        public string HoraSalida;
        /// <summary>"Martes 29/09/2026": el dia de la semana y dd/MM/yyyy.</summary>
        public string Fecha;
        /// <summary>"Nombre Apellidos"; vacio en el uso libre.</summary>
        public string Profesor;
        public string Grupo;
        public string Materia;
        public string Laboratorio;
    }
}
