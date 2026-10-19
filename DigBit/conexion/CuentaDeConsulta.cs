using System;
using System.Text.RegularExpressions;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>
    /// La cuenta de MySQL de solo lectura con la que trabaja la sesion de un
    /// encargado de laboratorio (db/18): la base le rechaza cualquier cambio, en
    /// cualquier equipo. Su usuario y su contrasena estan en configuracion
    /// (consulta_usuario, consulta_clave) y se leen al entrar con la cuenta del
    /// equipo; con ellos se arma la cadena (mismo servidor y misma base) y se
    /// prueba. Si falta o puede escribir, el encargado no entra: nunca se usa la
    /// cuenta del equipo en su lugar.
    /// </summary>
    internal static class CuentaDeConsulta
    {
        private const string SinCuenta = "La base todavía no tiene la cuenta de solo consulta de las personas responsables de laboratorio "
            + "(falta db/18_encargados.sql). Pide a quien administra el servidor que la aplique.";

        // El usuario de MySQL de la ultima cuenta de consulta preparada (por si en
        // configuracion se llama distinto de digbit_consulta).
        private static volatile string usuarioDeConsulta = "digbit_consulta";

        /// <summary>
        /// True si esa conexion va con la cuenta de solo consulta: lo que decide no
        /// intentar escrituras internas (como crear el secreto de los codigos) es
        /// la cuenta de la conexion, no quien tiene la sesion abierta.
        /// </summary>
        public static bool EsDeConsulta(MySqlConnection conexion)
        {
            try
            {
                string usuario = new MySqlConnectionStringBuilder(conexion.ConnectionString).UserID;
                return string.Equals(usuario, usuarioDeConsulta, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Arma y prueba la cadena de solo consulta. Devuelve null (y la cadena) si
        /// todo esta bien; si no, el texto para el encargado.
        /// </summary>
        public static string Preparar(MySqlConnection delEquipo, out string cadena)
        {
            cadena = null;
            if (!Esquema.Ajustes(delEquipo))
            {
                return SinCuenta;
            }

            string usuario = null;
            string clave = null;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT clave, valor FROM configuracion WHERE clave IN ('consulta_usuario', 'consulta_clave')", delEquipo))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    string valor = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (reader.GetString(0) == "consulta_usuario")
                    {
                        usuario = valor;
                    }
                    else
                    {
                        clave = valor;
                    }
                }
            }

            if (string.IsNullOrEmpty(clave))
            {
                return SinCuenta;
            }

            MySqlConnectionStringBuilder constructor = new MySqlConnectionStringBuilder(Conexion.CadenaDelEquipo());
            constructor.UserID = string.IsNullOrWhiteSpace(usuario) ? "digbit_consulta" : usuario.Trim();
            constructor.Password = clave;
            string candidata = constructor.ConnectionString;
            try
            {
                using (MySqlConnection prueba = Conexion.AbrirCadena(candidata))
                {
                    string permiso = PermisoDeEscritura(prueba);
                    if (permiso != null)
                    {
                        Log.Aviso("La cuenta de solo consulta '" + constructor.UserID + "' tiene un permiso que escribe (" + permiso
                            + "): los encargados no entran hasta dejarla solo con SELECT (db/18).");
                        return "La cuenta de solo consulta de la base puede modificar datos, así que las personas responsables de laboratorio no pueden entrar con ella. "
                            + "Pide a quien administra el servidor que vuelva a aplicar db/18_encargados.sql.";
                    }

                    using (MySqlCommand comando = new MySqlCommand("SELECT COUNT(*) FROM laboratorios", prueba))
                    {
                        comando.ExecuteScalar();
                    }
                }
            }
            catch (MySqlException ex)
            {
                // La causa y que hacer; el mensaje de MySQL, solo al log. Una contrasena o
                // un permiso que no coinciden no se arreglan con la red.
                Log.Error("Entrar con la cuenta de solo consulta '" + constructor.UserID + "'", ex);
                if (ex.Number == 1044 || ex.Number == 1045 || ex.Number == 1142 || ex.Number == 1143)
                {
                    return "El servidor rechazó la cuenta de solo consulta de la base (su contraseña o sus permisos no coinciden con los de db/18). "
                        + "Pide a quien administra el servidor que vuelva a aplicar db/18_encargados.sql.";
                }

                if (ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion)
                {
                    return "No hubo conexión con el servidor al entrar con la cuenta de solo consulta. Revisa la red de este equipo e inténtalo de nuevo.";
                }

                return "No se pudo entrar con la cuenta de solo consulta de la base. Inténtalo de nuevo; si sigue fallando, "
                    + "pide a quien administra el servidor que revise db/18_encargados.sql (el detalle quedó en el registro de diagnóstico de este equipo).";
            }

            Log.Info("Sesion de solo consulta: se trabaja con la cuenta '" + constructor.UserID + "' de la base.");
            usuarioDeConsulta = constructor.UserID;
            cadena = candidata;
            return null;
        }

        /// <summary>
        /// El primer permiso de esa cuenta que no sea de solo lectura (SELECT,
        /// USAGE o SHOW VIEW), o null. Sale de SHOW GRANTS: "UPDATE (password)"
        /// tambien escribe.
        /// </summary>
        internal static string PermisoDeEscritura(MySqlConnection conexion)
        {
            using (MySqlCommand comando = new MySqlCommand("SHOW GRANTS FOR CURRENT_USER()", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    string linea = reader.GetString(0);
                    if (Regex.IsMatch(linea, @"WITH\s+GRANT\s+OPTION", RegexOptions.IgnoreCase))
                    {
                        return "GRANT OPTION";
                    }

                    Match privilegios = Regex.Match(linea, @"^\s*GRANT\s+(.+?)\s+ON\s", RegexOptions.IgnoreCase);
                    if (!privilegios.Success)
                    {
                        // "GRANT rol TO ...": un rol inactivo no da permisos a esta
                        // sesion (DigBit nunca hace SET ROLE), y uno activo (por
                        // defecto u obligatorio) ya sale desplegado en otras lineas
                        // con sus privilegios (MySQL 8). MariaDB 5.5 no tiene roles.
                        continue;
                    }

                    foreach (string permiso in Regex.Replace(privilegios.Groups[1].Value, @"\([^)]*\)", "").Split(','))
                    {
                        string nombre = Regex.Replace(permiso.Trim().ToUpperInvariant(), @"\s+", " ");
                        if (nombre != "SELECT" && nombre != "USAGE" && nombre != "SHOW VIEW")
                        {
                            return nombre;
                        }
                    }
                }
            }

            return null;
        }
    }
}
