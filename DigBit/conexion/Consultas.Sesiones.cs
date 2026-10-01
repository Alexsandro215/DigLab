using System;
using System.Collections.Generic;
using System.Data;
using System.Windows.Forms;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
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
                       CONCAT_WS(' ', p.nombre, p.apellido_paterno, p.apellido_materno) AS profesor
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
                        Profesor = reader.IsDBNull(reader.GetOrdinal("profesor")) ? "" : reader.GetString("profesor")
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

        /// <summary>Encabezado del PDF de una sesion (horas, grupo, materia, fecha).</summary>
        public Usuario ConsultarDatosPdfSesion(int idSesion)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT ca.hora_entrada, ca.hora_salida, ca.fecha, ca.hora_registro,
                       COALESCE(g.nombre_grupo, '') AS GrupoNombre,
                       COALESCE(m.nombre_materia, '') AS MateriaNombre
                FROM codigos_accesos ca
                LEFT JOIN grupos g   ON ca.grupos_idgrupos = g.idgrupos
                LEFT JOIN materias m ON ca.materias_id_materia = m.id_materia
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

                    return new Usuario
                    {
                        HoraEntrada = reader["hora_entrada"].ToString(),
                        HoraSalida = reader["hora_salida"].ToString(),
                        GruposId = reader.GetString("GrupoNombre"),
                        MateriasId = reader.GetString("MateriaNombre"),
                        HoraRegistro = fecha.ToString("yyyy-MM-dd")
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

        /// <summary>
        /// Alumnos registrados en una sesion, para la tabla y el PDF. Incluye
        /// con_falla: 1 si esa bitacora reporta alguna falla o comentario, para
        /// resaltarla frente a las que no reportaron nada; y hora_registro ("HH:mm"
        /// de su primer registro, o "—" si es de antes de guardarse la hora).
        /// </summary>
        public DataTable consultaRegistroSesion(int idSesion)
        {
            DataTable tabla = new DataTable();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT rb.fk_codigo_accesos, rb.fk_usuario, u.nombre, u.apellido_paterno, u.apellido_materno, u.numero_identificador,
                       rb.numero_computadora,
                       COALESCE(rb.falla_red, '') AS falla_red, COALESCE(rb.comentarios_red, '') AS comentarios_red,
                       COALESCE(rb.falla_hardware, '') AS falla_hardware, COALESCE(rb.comentarios_hardware, '') AS comentarios_hardware,
                       COALESCE(rb.falla_software, '') AS falla_software, COALESCE(rb.comentarios_software, '') AS comentarios_software,
                       rb.registrado_sin_conexion AS sin_conexion,
                       COALESCE(DATE_FORMAT(rb.registrado_en, '%H:%i'), '') AS hora_registro,
                       CASE WHEN " + CondicionConFalla + @" THEN 1 ELSE 0 END AS con_falla
                FROM registros_bitacoras rb
                INNER JOIN usuarios u ON rb.fk_usuario = u.idusuarios
                WHERE rb.fk_codigo_accesos = @Id
                ORDER BY u.apellido_paterno, u.apellido_materno, u.nombre", conexion))
            {
                comando.Parameters.AddWithValue("@Id", idSesion);
                using (MySqlDataAdapter adapter = new MySqlDataAdapter(comando))
                {
                    adapter.Fill(tabla);
                }
            }

            tabla.Columns.Add("nombre_completo", typeof(string));
            foreach (DataRow fila in tabla.Rows)
            {
                // Fase 4: la bitacora registrada sin servidor entro sin contrasena;
                // se deja ver en tablas y PDF junto al nombre.
                string marca = Convert.ToInt32(fila["sin_conexion"]) == 1 ? " [sin conexion]" : "";
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
        /// Fase 4: guarda la bitacora en la cola local (ColaBitacoras) con los datos
        /// de la sesion resuelta sin servidor. Mismas comprobaciones que en linea:
        /// ventana vigente y una sola bitacora por alumno y sesion (contra la cola).
        /// </summary>
        private static bool GuardarSinConexion(string matricula, string computadora, string fallared, string comentariored,
            string fallahardware, string comentariohardware, string fallasoftware, string comentariosoftware)
        {
            SesionLocal sesion = Datos_User.SesionSinConexion;
            VentanaCodigo ventana = Datos_User.VentanaActual;
            if (sesion == null || ventana == null)
            {
                MessageBox.Show("No hay una sesion valida para guardar la bitacora. Vuelve a ingresar el codigo.", "Sin sesion", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (ventana.Restante <= TimeSpan.Zero)
            {
                MessageBox.Show("El codigo ya no esta vigente (valido hasta las " + ventana.Fin.ToString("HH:mm") + ").", "Codigo expirado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (ColaBitacoras.YaRegistrada(sesion.Clave, matricula))
            {
                MessageBox.Show("Ya registraste tu bitacora con este codigo.", "Registro duplicado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            try
            {
                ColaBitacoras.Encolar(new BitacoraPendiente
                {
                    Registrada = DateTime.Now,
                    Matricula = matricula,
                    Sesion = sesion,
                    Computadora = computadora,
                    FallaRed = fallared,
                    ComentarioRed = comentariored,
                    FallaHardware = fallahardware,
                    ComentarioHardware = comentariohardware,
                    FallaSoftware = fallasoftware,
                    ComentarioSoftware = comentariosoftware
                });
            }
            catch (Exception ex)
            {
                Log.Error("Guardar la bitacora en la cola local", ex);
                MessageBox.Show("No se pudo guardar la bitacora en este equipo: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            MessageBox.Show("Sin conexion con el servidor: tu bitacora quedo guardada en este equipo y se enviara sola cuando vuelva la conexion.", "Guardada sin conexion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        /// <summary>
        /// Sesiones (una clase en una fecha) para la pantalla del administrador:
        /// una fila por sesion con cuantos alumnos registraron, cuantos
        /// reportaron alguna falla y cuantos entraron sin conexion. El detalle de
        /// cada sesion es consultaRegistroSesion. Con un profesor (su id), solo
        /// las suyas: es su "Mis bitacoras".
        /// </summary>
        public DataTable ObtenerSesionesAdministrador(int idProfesor = 0)
        {
            DataTable tabla = new DataTable();
            using (MySqlConnection conexion = mconexion.GetConexion())
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
                       COUNT(DISTINCT CASE WHEN " + CondicionConFalla + @" THEN rb.fk_usuario END) AS con_fallas,
                       COUNT(DISTINCT CASE WHEN rb.registrado_sin_conexion = 1 THEN rb.fk_usuario END) AS sin_conexion
                FROM codigos_accesos ca
                INNER JOIN usuarios prof   ON prof.idusuarios = ca.usuarios_idusuarios
                LEFT JOIN materias m       ON m.id_materia = ca.materias_id_materia
                LEFT JOIN grupos g         ON g.idgrupos = ca.grupos_idgrupos
                LEFT JOIN laboratorios l   ON l.idlaboratorios = ca.laboratorios_idlaboratorios
                LEFT JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos
                WHERE @Profesor = 0 OR ca.usuarios_idusuarios = @Profesor
                GROUP BY ca.idcodigos_accesos, ca.fecha, ca.hora_registro, ca.hora_entrada, ca.hora_salida, ca.codigo,
                         m.nombre_materia, g.nombre_grupo, prof.numero_identificador,
                         prof.nombre, prof.apellido_paterno, prof.apellido_materno, l.nombre_laboratorio
                ORDER BY fecha DESC, ca.hora_entrada DESC, ca.idcodigos_accesos DESC", conexion))
            using (MySqlDataAdapter adapter = new MySqlDataAdapter(comando))
            {
                comando.Parameters.AddWithValue("@Profesor", idProfesor);
                adapter.Fill(tabla);
            }

            return tabla;
        }

        /// <summary>True si la bitacora reporta alguna falla o comentario (se resalta).</summary>
        public static bool ConFalla(DataRow fila)
        {
            return fila.Table.Columns.Contains("con_falla")
                && fila["con_falla"] != DBNull.Value
                && Convert.ToInt32(fila["con_falla"]) == 1;
        }

        /// <summary>Las tres areas que puede reportar el alumno, en el orden en que se muestran.</summary>
        public static readonly string[] AreasDeFalla = { "red", "hardware", "software" };

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
        /// tablas y el PDF: "Red: sin internet | Hardware: teclado". Si no
        /// reporto nada, "Sin novedad".
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

            return partes.Count == 0 ? "Sin novedad" : string.Join(" | ", partes);
        }

        /// <summary>
        /// Guarda la bitacora del alumno en una sesion. Sustituye a consultaFinal
        /// (que buscaba la sesion por el texto del codigo). Mismos mensajes.
        /// </summary>
        public bool consultaFinalSesion(int idSesion, string matricula, string computadora, string fallared, string comentariored,
            string fallahardware, string comentariohardware, string fallasotware, string comentariosoftware)
        {
            // Fase 4: sin servidor la bitacora se guarda en la cola local del equipo.
            if (SinConexion.Activo || (idSesion == 0 && Datos_User.SesionSinConexion != null))
            {
                return GuardarSinConexion(matricula, computadora, fallared, comentariored, fallahardware, comentariohardware, fallasotware, comentariosoftware);
            }

            try
            {
                VentanaCodigo ventana = ObtenerVentanaSesion(idSesion);
                if (ventana == null)
                {
                    MessageBox.Show("La sesion de esta clase ya no existe.", "Codigo no valido", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                if (ventana.Estado != EstadoCodigo.Vigente)
                {
                    MessageBox.Show("El codigo ya no esta vigente (valido hasta las " + ventana.Fin.ToString("HH:mm") + "). Pidele uno nuevo a tu profesor.", "Codigo expirado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
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
                            throw new Exception("No se encontro al alumno con matricula " + matricula + ".");
                        }

                        idUsuario = Convert.ToInt32(resultado);
                    }

                    // Solo la primera bitacora del alumno en la sesion cuenta, y con ella
                    // la hora de registro (registrado_en, reloj del servidor). Si vuelve
                    // a registrarse (se fue la luz, se reinicio el equipo) no se inserta
                    // otra ni se cambia la hora: con clases seguidas del mismo profesor,
                    // la hora es lo que dice cuando llego. No basta el UNIQUE
                    // (fk_codigo_accesos, fk_usuario): la base del TESCHI no puede
                    // llevarlo por sus duplicados historicos.
                    using (MySqlCommand cmdInsertar = new MySqlCommand(@"
                        INSERT INTO registros_bitacoras
                            (fk_codigo_accesos, fk_usuario, numero_computadora, falla_red, comentarios_red, falla_hardware, comentarios_hardware,
                             falla_software, comentarios_software, registrado_en)
                        SELECT @IdCodigoAcceso, @codigo_usuario, @computadora, @fallared, @comentariored, @fallahardware, @comentariohardware,
                               @fallasoftware, @comentariosoftware, NOW()
                        FROM DUAL
                        WHERE NOT EXISTS (SELECT 1 FROM registros_bitacoras WHERE fk_codigo_accesos = @IdCodigoAcceso AND fk_usuario = @codigo_usuario)", conexion))
                    {
                        cmdInsertar.Parameters.AddWithValue("@IdCodigoAcceso", idSesion);
                        cmdInsertar.Parameters.AddWithValue("@codigo_usuario", idUsuario);
                        cmdInsertar.Parameters.AddWithValue("@computadora", computadora);
                        cmdInsertar.Parameters.AddWithValue("@fallared", fallared);
                        cmdInsertar.Parameters.AddWithValue("@comentariored", comentariored);
                        cmdInsertar.Parameters.AddWithValue("@fallahardware", fallahardware);
                        cmdInsertar.Parameters.AddWithValue("@comentariohardware", comentariohardware);
                        cmdInsertar.Parameters.AddWithValue("@fallasoftware", fallasotware);
                        cmdInsertar.Parameters.AddWithValue("@comentariosoftware", comentariosoftware);

                        if (cmdInsertar.ExecuteNonQuery() == 0)
                        {
                            // Ya tenia una: la primera se queda como estaba, con su hora.
                            Log.Info("Bitacora de " + matricula + " no guardada: ya tenia una en la sesion " + idSesion + ".");
                            MessageBox.Show("Ya registraste tu bitacora con este codigo.", "Registro duplicado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return false;
                        }

                        // Sin mensaje: la pantalla del alumno confirma el registro.
                        return true;
                    }
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                MessageBox.Show("Ya registraste tu bitacora con este codigo.", "Registro duplicado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Guardar la bitacora en la sesion " + idSesion, ex);
                MessageBox.Show("Error al insertar el registro en la bitacora: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}
