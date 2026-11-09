using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Una falla que reporto el profesor durante su clase (db/15).</summary>
    public class ReporteProfesor
    {
        public int Id;
        public int SesionId;
        /// <summary>El numero del equipo, o null si es del laboratorio en general.</summary>
        public string Equipo;
        /// <summary>red, hardware, software u otro.</summary>
        public string Area;
        public string Falla;
        public string Comentarios;
        public DateTime RegistradoEn;
        public string Profesor;
        /// <summary>Cuando se dio por resuelta (db/26); null si sigue pendiente.</summary>
        public DateTime? ResueltaEn;
        public string NotaDeResuelta;

        public bool Resuelta
        {
            get { return ResueltaEn.HasValue; }
        }

        /// <summary>"Resuelta el 06/11/2026 a las 10:30. Se cambio el monitor."; vacio si sigue pendiente.</summary>
        public string TextoResuelta
        {
            get
            {
                return !ResueltaEn.HasValue ? ""
                    : FraseDeResuelta(ResueltaEn.Value.ToString("dd/MM/yyyy 'a las' HH:mm", CultureInfo.InvariantCulture), NotaDeResuelta);
            }
        }

        /// <summary>Renglon y, si ya se resolvio, que se resolvio: para las franjas de las bitacoras.</summary>
        public string RenglonConEstado
        {
            get
            {
                string renglon = Renglon;
                return !Resuelta ? renglon : renglon + (renglon.EndsWith(".", StringComparison.Ordinal) ? " " : ". ") + TextoResuelta;
            }
        }

        /// <summary>La misma frase para lo que reporto un estudiante y su docente. cuando: "06/11/2026 a las 10:30".</summary>
        internal static string FraseDeResuelta(string cuando, string nota)
        {
            string queSeHizo = EnUnRenglon(nota).TrimEnd('.');
            return "Resuelta el " + cuando + "." + (queSeHizo.Length == 0 ? "" : " " + queSeHizo + ".");
        }

        /// <summary>"Hardware: Monitor no enciende (PC-12)", o "(todo el laboratorio)": para las alertas.</summary>
        public string Corto
        {
            get
            {
                return ReportesProfesorDatos.NombreDeArea(Area) + ": " + Falla + " (" + (string.IsNullOrEmpty(Equipo) ? "todo el laboratorio" : Equipo) + ")";
            }
        }

        /// <summary>"Hardware: Monitor no enciende, en PC-12. El monitor no da imagen."</summary>
        public string Resumen
        {
            get
            {
                string donde = string.IsNullOrEmpty(Equipo) ? ", en todo el laboratorio" : ", en " + Equipo;
                return ReportesProfesorDatos.NombreDeArea(Area) + ": " + Falla + donde + "."
                    + (string.IsNullOrWhiteSpace(Comentarios) ? "" : " " + Comentarios.Trim());
            }
        }

        /// <summary>
        /// "10:15, PC-07, hardware: monitor no enciende. El monitor no da imagen.":
        /// un renglon de la franja de la bitacora de la clase. Lo de "otro" (el
        /// proyector, la luz) va sin area: "10:40, todo el laboratorio: No prende
        /// el proyector." Siempre en un renglon: los saltos del detalle, espacios.
        /// </summary>
        public string Renglon
        {
            get
            {
                string donde = string.IsNullOrWhiteSpace(Equipo) ? "todo el laboratorio" : Equipo.Trim();
                string area = Area == "otro" ? "" : ", " + ReportesProfesorDatos.NombreDeArea(Area).ToLowerInvariant();
                string falla = EnUnRenglon(Falla).TrimEnd('.');
                // Solo las de la lista (Consultas.OpcionesDeFalla) pasan a minuscula tras los
                // dos puntos: "Monitor no enciende", "monitor...". Lo que escribio el profesor
                // se queda como lo escribio: "MySQL Workbench no abre", "Windows no arranca".
                if (falla.Length > 1 && char.IsUpper(falla[0]) && char.IsLower(falla[1]) && Consultas.OpcionesDeFalla(Area).Contains(falla))
                {
                    falla = char.ToLowerInvariant(falla[0]) + falla.Substring(1);
                }

                string detalle = EnUnRenglon(Comentarios);
                return RegistradoEn.ToString("HH:mm", CultureInfo.InvariantCulture) + ", " + donde + area + ": " + falla + "."
                    + (detalle.Length == 0 ? "" : " " + detalle);
            }
        }

        private static string EnUnRenglon(string texto)
        {
            return string.Join(" ", (texto ?? "").Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }

    /// <summary>
    /// Las fallas que reporta el profesor desde su panel mientras su clase esta
    /// en curso (db/15, reportes_profesor). Cuelgan de la sesion de la clase:
    /// si ningun alumno ha registrado todavia, la sesion se crea al reportar,
    /// como al abrirla con la generacion de codigos. Todo lo comprueba el
    /// servidor con su reloj al guardar: que la clase sea suya, de hoy y este en
    /// curso. Al terminar la clase ya no se puede reportar.
    /// </summary>
    public class ReportesProfesorDatos
    {
        public const int LargoEquipo = 80;
        public const int LargoFalla = 120;
        public const int LargoComentarios = 500;
        /// <summary>Las areas en el orden en que se muestran; "otro" para lo que no es de un equipo (el proyector, la luz...).</summary>
        public static readonly string[] Areas = { "red", "hardware", "software", "otro" };

        private readonly Conexion mconexion = new Conexion();

        public static string NombreDeArea(string area)
        {
            switch (area)
            {
                case "red":
                    return "Red";
                case "hardware":
                    return "Hardware";
                case "software":
                    return "Software";
                default:
                    return "Otro";
            }
        }

        /// <summary>True si la base tiene db/15 y este usuario puede usarla (si no, no se ofrece reportar).</summary>
        public bool Disponible()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return Esquema.ReportesDeProfesor(conexion);
            }
        }

        /// <summary>Los equipos del laboratorio, para elegir de cual es la falla (los mismos del paso 3 del alumno).</summary>
        public List<string> Equipos(int idLaboratorio)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return Consultas.EquiposDelLaboratorio(conexion, idLaboratorio);
            }
        }

        /// <summary>
        /// Guarda la falla en la sesion de hoy de esa clase. Devuelve null si se
        /// guardo, o por que no (la clase ya termino, no es suya...).
        /// </summary>
        /// <param name="equipo">El numero del equipo, o null si es del laboratorio en general.</param>
        public string Guardar(SesionAgenda clase, int idProfesor, string equipo, string area, string falla, string comentarios)
        {
            if (clase == null || idProfesor <= 0)
            {
                return "No se sabe de qué clase es el reporte.";
            }

            if (!Areas.Contains(area))
            {
                return "Elige de qué es la falla.";
            }

            falla = (falla ?? "").Trim();
            if (falla.Length == 0)
            {
                return "Escribe o elige qué falla.";
            }

            equipo = string.IsNullOrWhiteSpace(equipo) ? null : equipo.Trim();
            comentarios = string.IsNullOrWhiteSpace(comentarios) ? null : comentarios.Trim();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.ReportesDeProfesor(conexion))
                {
                    return "Todavía no se pueden reportar fallas desde aquí: a la base de datos le falta una actualización (db/15). Avisa a la administración de DigLab.";
                }

                int? sesion = SesionDeHoy.Leer(conexion, clase) ?? SesionDeHoy.Crear(conexion, clase, idProfesor, "al reportar una falla");
                if (!sesion.HasValue)
                {
                    return "La clase de las " + HorariosDatos.Hora(clase.Inicio) + " ya no está en curso: solo se puede reportar durante la clase.";
                }

                // Con TIMESTAMP: en la base de la escuela hora_salida es texto (VARCHAR).
                using (MySqlCommand comando = new MySqlCommand(@"
                    INSERT INTO reportes_profesor (fk_codigo_accesos, fk_usuario, numero_computadora, area, falla, comentarios, registrado_en)
                    SELECT ca.idcodigos_accesos, @Profesor, @Equipo, @Area, @Falla, @Comentarios, NOW()
                    FROM codigos_accesos ca
                    WHERE ca.idcodigos_accesos = @Sesion AND ca.fecha = CURDATE()
                      AND NOW() BETWEEN TIMESTAMP(ca.fecha, ca.hora_entrada) AND TIMESTAMP(ca.fecha, ca.hora_salida)
                      AND ca.usuarios_idusuarios = @Profesor", conexion))
                {
                    comando.Parameters.AddWithValue("@Sesion", sesion.Value);
                    comando.Parameters.AddWithValue("@Profesor", idProfesor);
                    comando.Parameters.AddWithValue("@Equipo", equipo == null ? (object)DBNull.Value : Recortar(equipo, LargoEquipo));
                    comando.Parameters.AddWithValue("@Area", area);
                    comando.Parameters.AddWithValue("@Falla", Recortar(falla, LargoFalla));
                    comando.Parameters.AddWithValue("@Comentarios", comentarios == null ? (object)DBNull.Value : Recortar(comentarios, LargoComentarios));
                    if (comando.ExecuteNonQuery() != 1)
                    {
                        return "La clase de las " + HorariosDatos.Hora(clase.Inicio) + " ya no está en curso: solo se puede reportar durante la clase.";
                    }
                }

                Log.Info("El profesor " + idProfesor + " reporto una falla de " + area + " en la sesion " + sesion.Value
                    + (equipo == null ? " (todo el laboratorio)." : " (" + equipo + ")."));
                return null;
            }
        }

        /// <summary>Las fallas que reporto el profesor en esa sesion, de la primera a la ultima.</summary>
        public List<ReporteProfesor> DeLaSesion(int idSesion)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.ReportesDeProfesor(conexion))
                {
                    return new List<ReporteProfesor>();
                }

                return Leer(conexion, "rp.fk_codigo_accesos = @Sesion", c => c.Parameters.AddWithValue("@Sesion", idSesion));
            }
        }

        /// <summary>Condicion SQL: nadie ha dado por resuelto el reporte rp. Solo con db/26 (Esquema.FallasResueltas).</summary>
        internal const string ReporteSinResolver =
            "NOT EXISTS (SELECT 1 FROM fallas_resueltas frp WHERE frp.origen = 'docente' AND frp.registro_id = rp.idreportes_profesor)";

        /// <summary>" AND (sin resolver)" con db/26; sin ella, nada: lo que se ponga detras de un WHERE sobre rp.</summary>
        internal static string SoloSinResolver(MySqlConnection conexion)
        {
            return Esquema.FallasResueltas(conexion) ? " AND " + ReporteSinResolver : "";
        }

        /// <summary>
        /// Cuantas fallas reporto el profesor en cada una de esas sesiones, sin las
        /// ya resueltas (para las cifras de su panel). Vacio si la base no tiene db/15.
        /// </summary>
        internal static Dictionary<int, int> ContarPorSesion(MySqlConnection conexion, IEnumerable<int> sesiones)
        {
            Dictionary<int, int> cuenta = new Dictionary<int, int>();
            List<int> lista = sesiones.Distinct().ToList();
            if (lista.Count == 0 || !Esquema.ReportesDeProfesor(conexion))
            {
                return cuenta;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT rp.fk_codigo_accesos, COUNT(*) FROM reportes_profesor rp WHERE rp.fk_codigo_accesos IN ("
                + string.Join(",", lista.Select(s => s.ToString(CultureInfo.InvariantCulture))) + ")" + SoloSinResolver(conexion) + " GROUP BY rp.fk_codigo_accesos", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    cuenta[reader.GetInt32(0)] = Convert.ToInt32(reader[1]);
                }
            }

            return cuenta;
        }

        /// <summary>
        /// Cuantas fallas sin resolver reporto el profesor en cada sesion que tenga alguna
        /// (para la columna Fallas de Bitacoras). Son pocas: se cuentan todas de una vez.
        /// Vacio si la base no tiene db/15.
        /// </summary>
        internal static Dictionary<int, int> ContarTodas(MySqlConnection conexion)
        {
            Dictionary<int, int> cuenta = new Dictionary<int, int>();
            if (!Esquema.ReportesDeProfesor(conexion))
            {
                return cuenta;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT rp.fk_codigo_accesos, COUNT(*) FROM reportes_profesor rp WHERE 1 = 1" + SoloSinResolver(conexion) + " GROUP BY rp.fk_codigo_accesos", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    cuenta[reader.GetInt32(0)] = Convert.ToInt32(reader[1]);
                }
            }

            return cuenta;
        }

        /// <summary>Cuantas de las que reporto el profesor en cada sesion ya se resolvieron. Vacio sin db/15 o sin db/26.</summary>
        internal static Dictionary<int, int> ContarResueltas(MySqlConnection conexion)
        {
            Dictionary<int, int> cuenta = new Dictionary<int, int>();
            if (!Esquema.ReportesDeProfesor(conexion) || !Esquema.FallasResueltas(conexion))
            {
                return cuenta;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT rp.fk_codigo_accesos, COUNT(*) FROM reportes_profesor rp WHERE NOT " + ReporteSinResolver + " GROUP BY rp.fk_codigo_accesos", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    cuenta[reader.GetInt32(0)] = Convert.ToInt32(reader[1]);
                }
            }

            return cuenta;
        }

        /// <summary>Las que cumplen la condicion (sobre rp, reportes_profesor), con el nombre del profesor.</summary>
        internal static List<ReporteProfesor> Leer(MySqlConnection conexion, string condicion, Action<MySqlCommand> parametros)
        {
            List<ReporteProfesor> lista = new List<ReporteProfesor>();
            bool conResueltas = Esquema.FallasResueltas(conexion);
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT rp.idreportes_profesor, rp.fk_codigo_accesos, rp.numero_computadora, rp.area, rp.falla, rp.comentarios, rp.registrado_en,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                       " + (conResueltas ? "fr.resuelta_en, fr.nota" : "NULL") + (conResueltas ? " AS resuelta_nota" : " AS resuelta_en, NULL AS resuelta_nota") + @"
                FROM reportes_profesor rp
                LEFT JOIN usuarios u ON u.idusuarios = rp.fk_usuario
                " + (conResueltas ? "LEFT JOIN fallas_resueltas fr ON fr.origen = 'docente' AND fr.registro_id = rp.idreportes_profesor" : "") + @"
                WHERE " + condicion + @"
                ORDER BY rp.registrado_en, rp.idreportes_profesor", conexion))
            {
                parametros(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ReporteProfesor
                        {
                            Id = reader.GetInt32("idreportes_profesor"),
                            SesionId = reader.GetInt32("fk_codigo_accesos"),
                            Equipo = reader.IsDBNull(reader.GetOrdinal("numero_computadora")) ? null : reader.GetString("numero_computadora"),
                            Area = reader.GetString("area"),
                            Falla = reader.GetString("falla"),
                            Comentarios = reader.IsDBNull(reader.GetOrdinal("comentarios")) ? null : reader.GetString("comentarios"),
                            RegistradoEn = reader.GetDateTime("registrado_en"),
                            Profesor = reader.IsDBNull(reader.GetOrdinal("profesor")) ? "" : reader.GetString("profesor"),
                            ResueltaEn = reader.IsDBNull(reader.GetOrdinal("resuelta_en")) ? (DateTime?)null : Convert.ToDateTime(reader["resuelta_en"], CultureInfo.InvariantCulture),
                            NotaDeResuelta = reader.IsDBNull(reader.GetOrdinal("resuelta_nota")) ? null : Convert.ToString(reader["resuelta_nota"], CultureInfo.InvariantCulture)
                        });
                    }
                }
            }

            return lista;
        }

        private static string Recortar(string texto, int largo)
        {
            return texto.Length <= largo ? texto : texto.Substring(0, largo);
        }
    }
}
