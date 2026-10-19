using System.Linq;
using System.Text.RegularExpressions;

namespace DigBit.conexion
{
    /// <summary>
    /// Lo que tiene que cumplir un correo o una contrasena para que de verdad
    /// sirva despues. Lo usan el alta y la edicion de usuarios, "Restablecer
    /// contrasena", "Mi perfil" y "Olvidaste tu contrasena?", y coincide con la
    /// API de recuperacion (api/recuperar.php): si DigBit deja guardar algo que la
    /// API rechaza, el alumno se entera el dia que intenta recuperar su cuenta.
    /// </summary>
    internal static class ReglasCuenta
    {
        public const int LargoMinimoContrasena = 6;
        public const int LargoMaximoContrasena = 128;

        /// <summary>La regla, en gris debajo de cada "Contrasena nueva", antes de que salga el error.</summary>
        public static string TextoLargoMinimo
        {
            get { return "Al menos " + LargoMinimoContrasena + " caracteres."; }
        }

        /// <summary>usuarios.correo es VARCHAR(45) en la base de la escuela: lo que pase, se recortaria sin avisar.</summary>
        public const int LargoMaximoCorreo = 45;

        // Como FILTER_VALIDATE_EMAIL de PHP, o algo mas estricto: solo ASCII, sin
        // puntos al principio, al final ni seguidos, etiquetas de dominio sin
        // guion en los extremos y un dominio de nivel superior que empieza por
        // letra. PHP rechaza, por ejemplo, "a@gmail.com.", "a..b@gmail.com",
        // "josé@gmail.com", "a@gmail.com," y "a@localhost".
        private static readonly Regex Correo = new Regex(
            @"\A[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*" +
            // \z y no $: en .NET, $ tambien acepta un salto de linea al final.
            @"@([A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]([A-Za-z0-9-]*[A-Za-z0-9])?\z");

        /// <summary>True si la API podra mandarle el codigo de recuperacion a ese correo.</summary>
        public static bool CorreoValido(string correo)
        {
            if (string.IsNullOrEmpty(correo) || correo.Length > 254 || !Correo.IsMatch(correo))
            {
                return false;
            }

            int arroba = correo.IndexOf('@');
            return arroba <= 64 && correo.Substring(arroba + 1).Split('.').All(etiqueta => etiqueta.Length <= 63);
        }

        /// <summary>Null si el correo sirve (o esta vacio: es opcional); si no, por que.</summary>
        public static string ProblemaDeCorreo(string correo)
        {
            correo = (correo ?? "").Trim();
            if (correo.Length == 0)
            {
                return null;
            }

            if (correo.Length > LargoMaximoCorreo)
            {
                return "El correo es demasiado largo (" + LargoMaximoCorreo + " caracteres como mucho).";
            }

            return CorreoValido(correo) ? null : "El correo no parece válido (sin acentos, comas ni puntos al final).";
        }

        /// <summary>
        /// Null si la contrasena sirve; si no, por que. Las mismas reglas que el
        /// inicio de sesion: con solo espacios cree que el campo esta vacio y no
        /// deja entrar nunca. Su texto de guia ("CONTRASENA" con enie) ya no
        /// estorba: es una marca de agua, no el texto de la caja. La API
        /// (confirmar en api/recuperar.php) aplica estas mismas reglas al recuperarla.
        /// </summary>
        public static string ProblemaDeContrasena(string contrasena)
        {
            if (string.IsNullOrEmpty(contrasena) || contrasena.Length < LargoMinimoContrasena)
            {
                return "La contraseña debe tener al menos " + LargoMinimoContrasena + " caracteres.";
            }

            if (string.IsNullOrWhiteSpace(contrasena))
            {
                return "La contraseña no puede ser solo espacios.";
            }

            if (contrasena.Length > LargoMaximoContrasena)
            {
                return "La contraseña es demasiado larga (" + LargoMaximoContrasena + " caracteres como mucho).";
            }

            return null;
        }
    }
}
