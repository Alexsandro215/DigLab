using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>
    /// Las asistencias que da el profesor durante su clase a quien vino pero no
    /// pudo registrar su bitacora (db/22, asistencias_dadas): los motivos, como
    /// se dicen, desde donde se dio y lo que leen las demas vistas (Faltas, el
    /// panel, la Bitacora de la clase y el historial del alumno). Darlas y
    /// anularlas, y contarlas en la lista, esta en AsistenciaDatos. Sin la 22 (o
    /// sin permiso para leer la tabla) todo lo de aqui sale vacio.
    /// </summary>
    public static class AsistenciasDadas
    {
        /// <summary>Las claves de asistencias_dadas.motivo, en el orden en que se ofrecen.</summary>
        public static readonly string[] Motivos = { "sin_internet", "equipo", "sin_equipo", "otro" };

        public const string SinInternet = "sin_internet";
        public const string EquipoNoFunciona = "equipo";
        public const string SinEquipo = "sin_equipo";
        public const string Otro = "otro";

        /// <summary>La opcion del equipo que se guarda como NULL (no se sabe cual fue).</summary>
        public const string NoLoSe = "No lo sé";

        /// <summary>asistencias_dadas.numero_computadora.</summary>
        public const int LargoEquipo = 100;
        /// <summary>asistencias_dadas.comentario.</summary>
        public const int LargoComentario = 200;
        /// <summary>asistencias_dadas.dada_desde.</summary>
        public const int LargoDesde = 120;

        /// <summary>La opcion en pantalla ("Su equipo no tenia internet", con su acento).</summary>
        public static string NombreDeMotivo(string clave)
        {
            switch (clave)
            {
                case SinInternet:
                    return "Su equipo no tenía internet";
                case EquipoNoFunciona:
                    return "Su equipo no funcionaba";
                case SinEquipo:
                    return "No había un equipo libre";
                default:
                    return "Otro motivo";
            }
        }

        /// <summary>La ayuda de la opcion (solo "Su equipo no funcionaba"), o "".</summary>
        public static string AyudaDeMotivo(string clave)
        {
            return clave == EquipoNoFunciona ? "No encendía, no abría DigBit o se trabó." : "";
        }

        /// <summary>Con este motivo se dice que equipo fue (los otros no tienen equipo).</summary>
        public static bool ConEquipo(string clave)
        {
            return clave == SinInternet || clave == EquipoNoFunciona;
        }

        /// <summary>
        /// En una frase ("su equipo no tenia internet", con su acento); con "otro", el detalle tal
        /// como lo escribio el profesor (sin el punto final). Sin el equipo: quien
        /// lo ensena lo anade si quiere ("(PC-07)").
        /// </summary>
        public static string EnFrase(AsistenciaDada dada)
        {
            return dada == null ? "" : EnFrase(dada.Motivo, dada.Comentario);
        }

        public static string EnFrase(string motivo, string comentario)
        {
            switch (motivo)
            {
                case SinInternet:
                    return "su equipo no tenía internet";
                case EquipoNoFunciona:
                    return "su equipo no funcionaba";
                case SinEquipo:
                    return "no había un equipo libre";
                default:
                    string detalle = string.Join(" ", (comentario ?? "").Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
                    return detalle.Length == 0 ? "otro motivo" : detalle;
            }
        }

        /// <summary>
        /// Desde donde se dio, para asistencias_dadas.dada_desde: "PC-12 @
        /// Laboratorio de Redes (kiosco)". El numero va primero, por si se corta.
        /// </summary>
        public static string Desde()
        {
            string laboratorio = LaboratorioEquipo.NombreConfigurado;
            string desde = LaboratorioEquipo.NumeroMaquina + (string.IsNullOrEmpty(laboratorio) ? "" : " @ " + laboratorio)
                + (Kiosco.Activo ? " (kiosco)" : "");
            return desde.Length <= LargoDesde ? desde : desde.Substring(0, LargoDesde);
        }

        /// <summary>
        /// Cuantas asistencias dadas que cuentan (no anuladas) tiene cada una de esas
        /// sesiones. Vacio sin db/22.
        /// </summary>
        internal static Dictionary<int, int> ContarPorSesion(MySqlConnection conexion, IEnumerable<int> sesiones)
        {
            Dictionary<int, int> cuenta = new Dictionary<int, int>();
            List<int> lista = sesiones.Where(s => s > 0).Distinct().ToList();
            if (lista.Count == 0 || !Esquema.AsistenciaDada(conexion))
            {
                return cuenta;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT fk_codigo_accesos, COUNT(*) FROM asistencias_dadas WHERE activa = 1 AND fk_codigo_accesos IN ("
                + string.Join(",", lista.Select(s => s.ToString(CultureInfo.InvariantCulture))) + ") GROUP BY fk_codigo_accesos", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    cuenta[reader.GetInt32(0)] = Convert.ToInt32(reader[1]);
                }
            }

            return cuenta;
        }

        /// <summary>Lo mismo, con su propia conexion.</summary>
        public static Dictionary<int, int> ContarPorSesion(IEnumerable<int> sesiones)
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                return ContarPorSesion(conexion, sesiones);
            }
        }

        /// <summary>
        /// Las que cuentan de cada sesion con fecha entre desde y hasta (para Faltas:
        /// entra por el indice de la fecha). Vacio sin db/22.
        /// </summary>
        internal static Dictionary<int, int> ContarPorSesionEnFechas(MySqlConnection conexion, DateTime desde, DateTime hasta)
        {
            Dictionary<int, int> cuenta = new Dictionary<int, int>();
            if (!Esquema.AsistenciaDada(conexion))
            {
                return cuenta;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT fk_codigo_accesos, COUNT(*) FROM asistencias_dadas WHERE activa = 1 AND fecha BETWEEN @Desde AND @Hasta GROUP BY fk_codigo_accesos", conexion))
            {
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cuenta[reader.GetInt32(0)] = Convert.ToInt32(reader[1]);
                    }
                }
            }

            return cuenta;
        }

        /// <summary>
        /// Las que cuentan de esa sesion, de la primera a la ultima, con la matricula y
        /// "Apellidos Nombre" del alumno (la Bitacora de la clase y su PDF). Vacio sin db/22.
        /// </summary>
        internal static List<AsistenciaDada> DeLaSesion(MySqlConnection conexion, int idSesion)
        {
            if (!Esquema.AsistenciaDada(conexion))
            {
                return new List<AsistenciaDada>();
            }

            return Leer(conexion, @"
                SELECT " + Columnas + @",
                       u.numero_identificador AS matricula,
                       CONCAT_WS(' ', u.apellido_paterno, u.apellido_materno, u.nombre) AS nombre,
                       NULL AS laboratorio, NULL AS materia, NULL AS grupo, NULL AS docente
                FROM asistencias_dadas d
                INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = d.fk_codigo_accesos
                LEFT JOIN usuarios u ON u.idusuarios = d.fk_alumno
                WHERE d.fk_codigo_accesos = @Id AND d.activa = 1
                ORDER BY d.dada_en, d.idasistencias_dadas", idSesion);
        }

        /// <summary>Lo mismo, con su propia conexion.</summary>
        public static List<AsistenciaDada> DeLaSesion(int idSesion)
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                return DeLaSesion(conexion, idSesion);
            }
        }

        /// <summary>
        /// Todas las del alumno, tambien las anuladas (su historial y la exportacion de
        /// sus datos), de la mas reciente a la mas antigua, con las horas, el
        /// laboratorio, la materia, el grupo y el profesor de su sesion. Vacio sin db/22.
        /// </summary>
        internal static List<AsistenciaDada> DelEstudiante(MySqlConnection conexion, int idAlumno)
        {
            if (!Esquema.AsistenciaDada(conexion))
            {
                return new List<AsistenciaDada>();
            }

            return Leer(conexion, @"
                SELECT " + Columnas + @",
                       u.numero_identificador AS matricula,
                       CONCAT_WS(' ', u.apellido_paterno, u.apellido_materno, u.nombre) AS nombre,
                       l.nombre_laboratorio AS laboratorio, m.nombre_materia AS materia, g.nombre_grupo AS grupo,
                       CONCAT_WS(' ', p.nombre, p.apellido_paterno, p.apellido_materno) AS docente
                FROM asistencias_dadas d
                INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = d.fk_codigo_accesos
                LEFT JOIN usuarios u     ON u.idusuarios = d.fk_alumno
                LEFT JOIN laboratorios l ON l.idlaboratorios = ca.laboratorios_idlaboratorios
                LEFT JOIN materias m     ON m.id_materia = ca.materias_id_materia
                LEFT JOIN grupos g       ON g.idgrupos = ca.grupos_idgrupos
                LEFT JOIN usuarios p     ON p.idusuarios = ca.usuarios_idusuarios
                WHERE d.fk_alumno = @Id
                ORDER BY d.fecha DESC, d.dada_en DESC, d.idasistencias_dadas DESC", idAlumno);
        }

        /// <summary>Lo mismo, con su propia conexion.</summary>
        public static List<AsistenciaDada> DelEstudiante(int idAlumno)
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                return DelEstudiante(conexion, idAlumno);
            }
        }

        // Las columnas de una asistencia dada y de su sesion (alias d y ca). activa con IF:
        // un TINYINT(1) llega como bool (TreatTinyAsBoolean); las horas con TIME(): en la
        // escuela hora_salida es texto.
        private const string Columnas = @"d.idasistencias_dadas, d.fk_clase, d.fecha, d.fk_alumno, d.fk_codigo_accesos, d.fk_profesor,
                       d.dada_en, d.motivo, d.numero_computadora, d.comentario, d.dada_desde,
                       IF(d.activa = 1, 1, 0) AS activa, d.anulada_en, d.anulada_por,
                       TIME(ca.hora_entrada) AS entrada, TIME(ca.hora_salida) AS salida, ca.laboratorios_idlaboratorios AS lab";

        private static List<AsistenciaDada> Leer(MySqlConnection conexion, string consulta, int id)
        {
            List<AsistenciaDada> lista = new List<AsistenciaDada>();
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        AsistenciaDada dada = AsistenciaDatos.LeerDada(reader);
                        dada.DadaDesde = Texto(reader, "dada_desde");
                        dada.Activa = Convert.ToInt32(reader["activa"]) == 1;
                        dada.AnuladaEn = reader.IsDBNull(reader.GetOrdinal("anulada_en"))
                            ? (DateTime?)null : Convert.ToDateTime(reader["anulada_en"], CultureInfo.InvariantCulture);
                        dada.AnuladaPor = reader.IsDBNull(reader.GetOrdinal("anulada_por")) ? (int?)null : Convert.ToInt32(reader["anulada_por"]);
                        dada.Matricula = Texto(reader, "matricula") ?? "";
                        dada.Nombre = Texto(reader, "nombre") ?? "";
                        dada.Laboratorio = Texto(reader, "laboratorio") ?? "";
                        dada.Materia = Texto(reader, "materia") ?? "";
                        dada.Grupo = Texto(reader, "grupo") ?? "";
                        dada.Docente = Texto(reader, "docente") ?? "";
                        lista.Add(dada);
                    }
                }
            }

            return lista;
        }

        private static string Texto(MySqlDataReader reader, string columna)
        {
            int i = reader.GetOrdinal(columna);
            return reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
        }
    }
}
