using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Un tramo en que un laboratorio estuvo deshabilitado. Sin Hasta: sigue asi.</summary>
    public class PeriodoDeshabilitado
    {
        public int LaboratorioId;
        public DateTime Desde;
        public DateTime? Hasta;
    }

    /// <summary>
    /// Un laboratorio deshabilitado (laboratorios.activo = 0, db/08) conserva su horario,
    /// pero no corre: sus docentes no ven esas clases ni su codigo, sus equipos no
    /// aceptan registros y no cuenta faltas ni alertas. Sirve para instalar DigLab en un
    /// solo laboratorio sin armar otra plantilla.
    ///
    /// Los tramos se guardan en la tabla configuracion (db/13), sin migracion nueva: al
    /// volver a habilitarlo, esos dias tampoco salen como falta. Sin esa tabla, el
    /// laboratorio se deshabilita igual; solo se pierde esa cuenta.
    /// </summary>
    internal static class LaboratoriosDeshabilitados
    {
        /// <summary>"12:202611041000-202611050730;13:202611041000-;" (el segundo, sin terminar).</summary>
        private const string Clave = "laboratorios_deshabilitados";
        private const string Formato = "yyyyMMddHHmm";

        /// <summary>Lo que se le dice a quien intenta registrar en uno de sus equipos.</summary>
        public const string Mensaje = "Este laboratorio está deshabilitado en DigLab: por ahora aquí no se registran bitácoras. "
            + "Avisa a la persona responsable del laboratorio.";

        /// <summary>
        /// SQL, para seguir un WHERE: que esa columna de laboratorio no sea de uno
        /// deshabilitado. NOT EXISTS porque una sesion extra vieja puede no tener laboratorio.
        /// </summary>
        public static string SinDeshabilitados(MySqlConnection conexion, string columna)
        {
            return Esquema.LaboratoriosDesactivables(conexion)
                ? " AND NOT EXISTS (SELECT 1 FROM laboratorios lh WHERE lh.idlaboratorios = " + columna + " AND lh.activo = 0)"
                : "";
        }

        public static bool Esta(MySqlConnection conexion, int idLaboratorio)
        {
            if (!Esquema.LaboratoriosDesactivables(conexion))
            {
                return false;
            }

            using (MySqlCommand comando = new MySqlCommand("SELECT activo FROM laboratorios WHERE idlaboratorios = @Lab", conexion))
            {
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                object activo = comando.ExecuteScalar();
                return activo != null && activo != DBNull.Value && Convert.ToInt32(activo, CultureInfo.InvariantCulture) == 0;
            }
        }

        /// <summary>Los que hoy estan deshabilitados.</summary>
        public static HashSet<int> Ids(MySqlConnection conexion)
        {
            HashSet<int> ids = new HashSet<int>();
            if (!Esquema.LaboratoriosDesactivables(conexion))
            {
                return ids;
            }

            using (MySqlCommand comando = new MySqlCommand("SELECT idlaboratorios FROM laboratorios WHERE activo = 0", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    ids.Add(reader.GetInt32(0));
                }
            }

            return ids;
        }

        public static List<PeriodoDeshabilitado> LeerPeriodos(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            Dictionary<string, string> leidos = AjustesDatos.Leer(conexion, transaccion, Clave);
            string texto;
            return leidos != null && leidos.TryGetValue(Clave, out texto) ? Desde(texto) : new List<PeriodoDeshabilitado>();
        }

        /// <summary>
        /// Si esa clase se cruza con un tramo de su laboratorio. Basta que se cruce: se
        /// deshabilito a media clase, o se habilito cuando ya iba a la mitad, y nadie
        /// pudo registrar con normalidad.
        /// </summary>
        public static bool Cubre(IEnumerable<PeriodoDeshabilitado> periodos, int idLaboratorio, DateTime inicio, DateTime fin)
        {
            return periodos != null && periodos.Any(p => p.LaboratorioId == idLaboratorio
                && fin > p.Desde && (!p.Hasta.HasValue || inicio < p.Hasta.Value));
        }

        /// <summary>Abre o cierra el tramo de ese laboratorio. En la transaccion de quien lo cambia.</summary>
        public static void Anotar(MySqlConnection conexion, MySqlTransaction transaccion, int idLaboratorio, bool deshabilitado, DateTime ahora)
        {
            if (!Esquema.Ajustes(conexion, transaccion))
            {
                return;
            }

            List<PeriodoDeshabilitado> periodos = LeerPeriodos(conexion, transaccion);
            PeriodoDeshabilitado abierto = periodos.FirstOrDefault(p => p.LaboratorioId == idLaboratorio && !p.Hasta.HasValue);
            if (deshabilitado && abierto == null)
            {
                periodos.Add(new PeriodoDeshabilitado { LaboratorioId = idLaboratorio, Desde = ahora });
            }
            else if (!deshabilitado && abierto != null)
            {
                abierto.Hasta = ahora;
            }

            AjustesDatos.GuardarEn(conexion, transaccion, Clave, Texto(periodos));
        }

        private static List<PeriodoDeshabilitado> Desde(string texto)
        {
            List<PeriodoDeshabilitado> periodos = new List<PeriodoDeshabilitado>();
            foreach (string trozo in (texto ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] partes = trozo.Split(':', '-');
                int laboratorio;
                DateTime desde;
                if (partes.Length != 3 || !int.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out laboratorio) || !Fecha(partes[1], out desde))
                {
                    continue;
                }

                DateTime hasta;
                periodos.Add(new PeriodoDeshabilitado
                {
                    LaboratorioId = laboratorio,
                    Desde = desde,
                    Hasta = Fecha(partes[2], out hasta) ? hasta : (DateTime?)null
                });
            }

            return periodos;
        }

        private static bool Fecha(string texto, out DateTime fecha)
        {
            return DateTime.TryParseExact(texto, Formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);
        }

        /// <summary>
        /// El texto a guardar. Si ya no cabe, sacrifica los tramos cerrados mas viejos: tendrian
        /// que ser mas de cien, y lo que se pierde es solo que esos dias salgan como no contados.
        /// </summary>
        private static string Texto(List<PeriodoDeshabilitado> periodos)
        {
            List<PeriodoDeshabilitado> quedan = new List<PeriodoDeshabilitado>(periodos);
            while (true)
            {
                StringBuilder texto = new StringBuilder();
                foreach (PeriodoDeshabilitado p in quedan)
                {
                    texto.Append(p.LaboratorioId.ToString(CultureInfo.InvariantCulture)).Append(':')
                        .Append(p.Desde.ToString(Formato, CultureInfo.InvariantCulture)).Append('-')
                        .Append(p.Hasta.HasValue ? p.Hasta.Value.ToString(Formato, CultureInfo.InvariantCulture) : "").Append(';');
                }

                PeriodoDeshabilitado viejo = quedan.Where(p => p.Hasta.HasValue).OrderBy(p => p.Desde).FirstOrDefault();
                if (texto.Length <= AjustesDatos.LargoMaximo || viejo == null)
                {
                    return texto.ToString();
                }

                quedan.Remove(viejo);
            }
        }
    }
}
