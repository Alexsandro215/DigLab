using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DigLab.conexion
{
    /// <summary>
    /// Guarda y comprueba las contrasenas de los usuarios.
    ///
    /// Hasta la fase 7 se guardaba el MD5 de la contrasena, sin sal. Eso era malo
    /// por dos motivos: MD5 esta hecho para ser rapidisimo, asi que quien copie la
    /// tabla puede probar miles de millones de combinaciones por segundo hasta dar
    /// con ella; y sin sal la misma contrasena da siempre el mismo hash, de modo
    /// que hay tablas publicas con los pares ya resueltos y, dentro de la propia
    /// tabla, dos personas con la misma contrasena se delatan solas.
    ///
    /// Ahora se guarda PBKDF2 con SHA-256: lento a proposito (muchas iteraciones)
    /// y con una sal aleatoria distinta por usuario. El formato es texto plano
    /// legible, con todo lo necesario para comprobarlo mas tarde:
    ///
    ///     pbkdf2-sha256$&lt;iteraciones&gt;$&lt;sal en base64&gt;$&lt;hash en base64&gt;
    ///
    /// Guardar las iteraciones dentro permite subirlas en el futuro sin invalidar
    /// lo ya guardado: <see cref="Verificar"/> avisa con necesitaRehash.
    ///
    /// MIGRACION PEREZOSA: no se pueden recuperar las contrasenas viejas para
    /// volver a procesarlas, porque MD5 no se deshace. Asi que se aprovecha el
    /// unico momento en que la aplicacion tiene la contrasena en claro, el inicio
    /// de sesion correcto, para recalcularla y reemplazar lo guardado. Lo hace
    /// Consultas.RealizarInicioSesion. Cuando ya no queden hashes antiguos en la
    /// base se puede borrar el camino de MD5 de este archivo.
    /// </summary>
    internal static class Contrasenas
    {
        private const string Etiqueta = "pbkdf2-sha256";
        private const char Separador = '$';

        /// <summary>Bytes de sal aleatoria por usuario.</summary>
        private const int BytesDeSal = 16;

        /// <summary>Bytes de hash derivado (256 bits, el tamano natural de SHA-256).</summary>
        private const int BytesDeHash = 32;

        /// <summary>
        /// Iteraciones con las que se guardan las contrasenas nuevas. Subir este
        /// numero es seguro: lo ya guardado sigue comprobandose con las suyas y se
        /// actualiza solo la proxima vez que cada quien entre.
        /// </summary>
        public const int IteracionesActuales = 100000;

        /// <summary>Longitud del MD5 en hexadecimal, que es lo que habia antes.</summary>
        private const int LargoMd5Hex = 32;

        /// <summary>
        /// Convierte una contrasena en lo que se guarda en la columna password.
        /// Cada llamada produce un resultado distinto, porque la sal es nueva.
        /// </summary>
        public static string Hash(string contrasena)
        {
            if (contrasena == null)
            {
                throw new ArgumentNullException("contrasena");
            }

            byte[] sal = new byte[BytesDeSal];
            using (RNGCryptoServiceProvider generador = new RNGCryptoServiceProvider())
            {
                generador.GetBytes(sal);
            }

            byte[] hash = Derivar(contrasena, sal, IteracionesActuales);

            return Etiqueta
                + Separador + IteracionesActuales.ToString(CultureInfo.InvariantCulture)
                + Separador + Convert.ToBase64String(sal)
                + Separador + Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Comprueba la contrasena que tecleo el usuario contra lo guardado.
        /// necesitaRehash sale en true cuando la comprobacion fue correcta pero lo
        /// guardado deberia reemplazarse: porque es un MD5 antiguo, o porque se
        /// guardo con menos iteraciones de las que se usan ahora. Quien llama es
        /// responsable de volver a guardar con <see cref="Hash"/>.
        /// </summary>
        public static bool Verificar(string ingresada, string almacenada, out bool necesitaRehash)
        {
            necesitaRehash = false;

            if (ingresada == null || string.IsNullOrWhiteSpace(almacenada))
            {
                return false;
            }

            almacenada = almacenada.Trim();

            if (almacenada.StartsWith(Etiqueta + Separador, StringComparison.Ordinal))
            {
                int iteraciones;
                byte[] sal, esperado;
                if (!Descomponer(almacenada, out iteraciones, out sal, out esperado))
                {
                    return false;
                }

                byte[] calculado = Derivar(ingresada, sal, iteraciones);
                if (!IgualesEnTiempoFijo(calculado, esperado))
                {
                    return false;
                }

                necesitaRehash = iteraciones < IteracionesActuales;
                return true;
            }

            // Camino antiguo: MD5 en hexadecimal, sin sal.
            if (!EsMd5Hex(almacenada))
            {
                return false;
            }

            if (!IgualesEnTiempoFijo(Encoding.ASCII.GetBytes(HashMd5(ingresada)),
                                     Encoding.ASCII.GetBytes(almacenada.ToLowerInvariant())))
            {
                return false;
            }

            necesitaRehash = true;
            return true;
        }

        /// <summary>True si lo guardado es un MD5 de los de antes.</summary>
        public static bool EsFormatoAntiguo(string almacenada)
        {
            return !string.IsNullOrWhiteSpace(almacenada) && EsMd5Hex(almacenada.Trim());
        }

        // --- Interior ---------------------------------------------------------

        private static byte[] Derivar(string contrasena, byte[] sal, int iteraciones)
        {
            using (Rfc2898DeriveBytes derivador = new Rfc2898DeriveBytes(
                contrasena, sal, iteraciones, HashAlgorithmName.SHA256))
            {
                return derivador.GetBytes(BytesDeHash);
            }
        }

        private static bool Descomponer(string almacenada, out int iteraciones, out byte[] sal, out byte[] hash)
        {
            iteraciones = 0;
            sal = null;
            hash = null;

            string[] partes = almacenada.Split(Separador);
            if (partes.Length != 4)
            {
                return false;
            }

            if (!int.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out iteraciones)
                || iteraciones <= 0)
            {
                return false;
            }

            try
            {
                sal = Convert.FromBase64String(partes[2]);
                hash = Convert.FromBase64String(partes[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            return sal.Length > 0 && hash.Length > 0;
        }

        private static bool EsMd5Hex(string valor)
        {
            if (valor.Length != LargoMd5Hex)
            {
                return false;
            }

            foreach (char c in valor)
            {
                bool esHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!esHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static string HashMd5(string entrada)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(entrada));
                StringBuilder texto = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    texto.Append(b.ToString("x2"));
                }

                return texto.ToString();
            }
        }

        /// <summary>
        /// Comparacion que tarda lo mismo acierte o falle. Comparar con == se
        /// detiene en el primer byte distinto, y ese tiempo, medido muchas veces,
        /// filtra informacion sobre el valor correcto.
        /// </summary>
        private static bool IgualesEnTiempoFijo(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            int diferencia = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diferencia |= a[i] ^ b[i];
            }

            return diferencia == 0;
        }
    }
}
