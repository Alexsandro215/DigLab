using System;
using System.Collections.Generic;
using System.Linq;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>
    /// Ajustes de toda la escuela guardados en la base (tabla configuracion,
    /// db/13_recuperacion_contrasena.sql): el administrador los pone una vez y
    /// valen al momento en todos los equipos, tambien en los congelados con Deep
    /// Freeze, sin tocar ninguno. Los equipos solo pueden leerlos (db/07).
    /// </summary>
    internal static class AjustesDatos
    {
        /// <summary>La direccion de api/recuperar.php ("Olvidaste tu contrasena?").</summary>
        public const string UrlRecuperacion = "recuperacion_url";

        /// <summary>La clave que DigBit le manda a esa API (clave_api de su config.php).</summary>
        public const string ClaveRecuperacion = "recuperacion_clave";

        /// <summary>El aviso de privacidad simplificado del TESCHI (texto; vacio = no se muestra).</summary>
        public const string AvisoPrivacidadTexto = "aviso_privacidad_texto";

        /// <summary>La direccion del aviso de privacidad integral (vacio = sin enlace).</summary>
        public const string AvisoPrivacidadUrl = "aviso_privacidad_url";

        /// <summary>"1": los docentes entran con su contrasena del SIIA del TESCHI (InicioConSiia); sin valor, apagado.</summary>
        public const string SiiaActivo = "siia_docentes";

        /// <summary>"1": los estudiantes entran con su matricula y su contrasena del SIIA; sin valor, apagado.</summary>
        public const string SiiaEstudiantes = "siia_estudiantes";

        /// <summary>La direccion de login.ashx del SIIA (vacio = InicioConSiia.UrlPorOmision).</summary>
        public const string SiiaUrl = "siia_url";

        /// <summary>La pagina del SIIA para recuperar la contrasena, solo https (vacio = InicioConSiia.RecuperarPorOmision).</summary>
        public const string SiiaRecuperarUrl = "siia_recuperar_url";

        /// <summary>La pagina de inicio del SIIA, donde se crea el NIP, solo https (vacio = InicioConSiia.PaginaPorOmision).</summary>
        public const string SiiaPaginaUrl = "siia_pagina_url";

        /// <summary>
        /// Lo mas largo que se guarda de un ajuste (el aviso de privacidad, que
        /// Ajustes deja pegar hasta aqui): TrozosMaximos trozos de LargoTrozo.
        /// </summary>
        public const int LargoMaximo = TrozosMaximos * LargoTrozo;

        // configuracion.valor es VARCHAR(500) (db/13): un valor mas largo (un aviso
        // de privacidad entero no cabe) se guarda en trozos, la clave con el
        // primero y "clave#2", "clave#3"... con los demas. Leer los une y Guardar
        // los parte: quien los usa ve un solo valor, como con los cortos. Sin
        // migracion nueva, y un DigBit anterior ni los mira.
        private const int LargoTrozo = 500;
        private const int TrozosMaximos = 10;
        private const string MarcaTrozo = "#";

        /// <summary>
        /// Los ajustes que haya con esas claves (los que no estan, no salen); un
        /// valor guardado en trozos llega entero. Null si la base no tiene la
        /// tabla (falta db/13).
        /// </summary>
        public static Dictionary<string, string> Leer(params string[] claves)
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                return Leer(conexion, null, claves);
            }
        }

        /// <summary>Leer en una conexion (y transaccion) que ya se tiene.</summary>
        internal static Dictionary<string, string> Leer(MySqlConnection conexion, MySqlTransaction transaccion, params string[] claves)
        {
            if (!Esquema.Ajustes(conexion, transaccion))
            {
                return null;
            }

            Dictionary<string, string> valores = new Dictionary<string, string>(StringComparer.Ordinal);
            if (claves.Length == 0)
            {
                return valores;
            }

            // Cada clave con sus posibles trozos.
            List<string> buscadas = new List<string>();
            foreach (string clave in claves)
            {
                buscadas.Add(clave);
                for (int trozo = 2; trozo <= TrozosMaximos; trozo++)
                {
                    buscadas.Add(Trozo(clave, trozo));
                }
            }

            Dictionary<string, string> leidos = new Dictionary<string, string>(StringComparer.Ordinal);
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT clave, valor FROM configuracion WHERE clave IN ("
                + string.Join(", ", buscadas.Select((c, i) => "@C" + i)) + ")", conexion, transaccion))
            {
                for (int i = 0; i < buscadas.Count; i++)
                {
                    comando.Parameters.AddWithValue("@C" + i, buscadas[i]);
                }

                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        if (!lector.IsDBNull(1))
                        {
                            leidos[lector.GetString(0)] = lector.GetString(1);
                        }
                    }
                }
            }

            foreach (string clave in claves)
            {
                string valor;
                if (!leidos.TryGetValue(clave, out valor))
                {
                    continue;
                }

                // Los trozos seguidos; uno que falta corta ahi (no deberia pasar: se guardan juntos).
                string resto;
                for (int trozo = 2; trozo <= TrozosMaximos && leidos.TryGetValue(Trozo(clave, trozo), out resto); trozo++)
                {
                    valor += resto;
                }

                valores[clave] = valor;
            }

            return valores;
        }

        /// <summary>
        /// Guarda los ajustes, todos o ninguno. Vacio (o null) borra el ajuste.
        /// Uno mas largo que LargoTrozo va en trozos; mas largo que LargoMaximo,
        /// ErrorDeDatos. Solo el administrador: el usuario de MySQL de los
        /// equipos no puede.
        /// </summary>
        public static void Guardar(IDictionary<string, string> valores)
        {
            Permisos.Exigir("Guardar los ajustes");
            foreach (KeyValuePair<string, string> par in valores)
            {
                if ((par.Value ?? "").Trim().Length > LargoMaximo)
                {
                    throw new ErrorDeDatos("El texto es demasiado largo: caben " + LargoMaximo.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-MX"))
                        + " caracteres. Acórtalo y vuelve a guardar.");
                }
            }

            using (MySqlConnection conexion = new Conexion().GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                if (!Esquema.Ajustes(conexion, transaccion))
                {
                    throw new ErrorDeDatos("La base no tiene dónde guardar los ajustes: falta aplicar db/13_recuperacion_contrasena.sql. "
                        + "Pide a quien administra el servidor que la aplique.");
                }

                foreach (KeyValuePair<string, string> par in valores)
                {
                    GuardarEn(conexion, transaccion, par.Key, par.Value);
                }

                transaccion.Commit();
            }

            // Solo los nombres: la clave de la API no va al log.
            Log.Info("Ajustes guardados: " + string.Join(", ", valores.Select(p => p.Key + (string.IsNullOrWhiteSpace(p.Value) ? " (borrado)"
                : p.Value.Trim().Length > LargoTrozo ? " (" + Partir(p.Value.Trim()).Count + " trozos)" : ""))) + ".");
        }

        /// <summary>Un ajuste, en la transaccion de quien llama (que ya comprobo el permiso y que hay tabla).</summary>
        internal static void GuardarEn(MySqlConnection conexion, MySqlTransaction transaccion, string claveDelAjuste, string valor)
        {
            List<string> trozos = Partir((valor ?? "").Trim());
            for (int trozo = 1; trozo <= TrozosMaximos; trozo++)
            {
                string clave = trozo == 1 ? claveDelAjuste : Trozo(claveDelAjuste, trozo);
                bool queda = trozo <= trozos.Count;
                using (MySqlCommand comando = new MySqlCommand(queda
                    ? "INSERT INTO configuracion (clave, valor) VALUES (@Clave, @Valor) ON DUPLICATE KEY UPDATE valor = VALUES(valor)"
                    : "DELETE FROM configuracion WHERE clave = @Clave",
                    conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Clave", clave);
                    comando.Parameters.AddWithValue("@Valor", queda ? trozos[trozo - 1] : "");
                    comando.ExecuteNonQuery();
                }
            }
        }

        /// <summary>"aviso_privacidad_texto#2": la clave del trozo 2 en adelante.</summary>
        private static string Trozo(string clave, int numero)
        {
            return clave + MarcaTrozo + numero.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// El valor en trozos de LargoTrozo (vacio: ninguno). Un caracter de dos
        /// mitades (fuera del BMP) no se parte entre dos trozos.
        /// </summary>
        private static List<string> Partir(string valor)
        {
            List<string> trozos = new List<string>();
            int desde = 0;
            while (desde < valor.Length)
            {
                int largo = Math.Min(LargoTrozo, valor.Length - desde);
                if (largo == LargoTrozo && char.IsHighSurrogate(valor[desde + largo - 1]))
                {
                    largo--;
                }

                trozos.Add(valor.Substring(desde, largo));
                desde += largo;
            }

            return trozos;
        }
    }
}
