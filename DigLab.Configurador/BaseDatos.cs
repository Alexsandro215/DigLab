using System;
using System.Collections.Generic;
using System.Data.Common;
using MySql.Data.MySqlClient;

namespace DigLab.Configurador
{
    /// <summary>
    /// La conexion que se teclea en el asistente. Llega en variables de entorno, nunca
    /// en la linea de comandos, que cualquiera puede leer en el Administrador de tareas.
    /// Antes de instalar nada se comprueba contra la base: el equipo llego a quedar
    /// instalado con una contrasena mal escrita (bastaba una mayuscula de mas) y DigLab
    /// solo decia "sin conexion".
    /// </summary>
    internal static class BaseDatos
    {
        public const string VarServidor = "DIGLAB_BD_SERVIDOR";
        public const string VarBase = "DIGLAB_BD_BASE";
        public const string VarPuerto = "DIGLAB_BD_PUERTO";
        public const string VarUsuario = "DIGLAB_BD_USUARIO";
        public const string VarClave = "DIGLAB_BD_CLAVE";

        /// <summary>La cadena del asistente, o null si no vino ninguna (una actualizacion).</summary>
        public static string CadenaDelEntorno()
        {
            string servidor = Environment.GetEnvironmentVariable(VarServidor);
            if (string.IsNullOrWhiteSpace(servidor))
            {
                return null;
            }

            string clave = Environment.GetEnvironmentVariable(VarClave) ?? "";
            Salida.Secreto(clave);
            // Con el constructor de cadenas y no pegando trozos: una contrasena con ';'
            // o comillas partia la cadena y el equipo se quedaba sin conexion.
            DbConnectionStringBuilder cadena = new DbConnectionStringBuilder();
            cadena["Database"] = (Environment.GetEnvironmentVariable(VarBase) ?? "").Trim();
            cadena["Server"] = servidor.Trim();
            string puerto = (Environment.GetEnvironmentVariable(VarPuerto) ?? "").Trim();
            if (puerto.Length > 0)
            {
                cadena["Port"] = puerto;
            }

            cadena["User Id"] = (Environment.GetEnvironmentVariable(VarUsuario) ?? "").Trim();
            cadena["Password"] = clave;
            return cadena.ConnectionString;
        }

        /// <summary>
        /// Prueba la cadena y escribe en la salida ESTADO OK, DENEGADO o SINRED, y un
        /// LAB por cada laboratorio activo. Devuelve los nombres, o null si no se pudo.
        /// </summary>
        public static List<string> Probar(string cadena)
        {
            try
            {
                List<string> nombres = Laboratorios(cadena);
                Salida.Escribir("ESTADO", "OK");
                foreach (string nombre in nombres)
                {
                    Salida.Escribir("LAB", nombre);
                }

                // Para avisar si el equipo quedaria sin memoria de emergencia (db/28).
                int? memorias = MemoriasRegistradas(cadena);
                Salida.Escribir("MEMORIAS", memorias.HasValue ? memorias.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NO");
                return nombres;
            }
            catch (Exception ex)
            {
                Salida.Escribir("ESTADO", EsAccesoDenegado(ex) ? "DENEGADO" : "SINRED");
                Salida.Escribir("DETALLE", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Todos los laboratorios, en el orden de la base. Tambien los deshabilitados: sus
        /// equipos se preparan antes de habilitarlos, y hasta entonces dicen que lo estan.
        /// </summary>
        public static List<string> Laboratorios(string cadena)
        {
            List<string> nombres = new List<string>();
            using (MySqlConnection conexion = new MySqlConnection(cadena + ";Connection Timeout=8"))
            using (MySqlCommand consulta = conexion.CreateCommand())
            {
                conexion.Open();
                consulta.CommandText = "SELECT nombre_laboratorio FROM laboratorios ORDER BY idlaboratorios";
                using (MySqlDataReader lector = consulta.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        nombres.Add(Convert.ToString(lector["nombre_laboratorio"]));
                    }
                }
            }

            return nombres;
        }

        /// <summary>
        /// Cuantas memorias de emergencia hay registradas en la base (db/28), o null si la
        /// base no las tiene o esta cuenta no puede leerlas.
        /// </summary>
        public static int? MemoriasRegistradas(string cadena)
        {
            try
            {
                using (MySqlConnection conexion = new MySqlConnection(cadena + ";Connection Timeout=8"))
                using (MySqlCommand consulta = conexion.CreateCommand())
                {
                    conexion.Open();
                    consulta.CommandText = "SELECT COUNT(*) FROM memorias_emergencia WHERE quitada_en IS NULL";
                    return Convert.ToInt32(consulta.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool EsAccesoDenegado(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                MySqlException mysql = e as MySqlException;
                if (mysql != null && mysql.Number == 1045)
                {
                    return true;
                }

                if (e.Message.IndexOf("Access denied", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
