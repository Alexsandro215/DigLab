using System.Linq;
using System.Text.RegularExpressions;

namespace DigLab.conexion
{
    /// <summary>
    /// Lo que tiene que cumplir un correo o una contrasena para que de verdad
    /// sirva despues. Lo usan el alta y la edicion de usuarios, "Restablecer
    /// contrasena", "Mi perfil" y "Olvidaste tu contrasena?", y coincide con la
    /// API de recuperacion (api/recuperar.php): si DigLab deja guardar algo que la
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

        /// <summary>usuarios.numero_identificador es VARCHAR(45) en la base de la escuela.</summary>
        public const int LargoMaximoUsuario = 45;

        /// <summary>
        /// Si ese usuario se puede escribir para entrar, en todas partes (inicio de
        /// sesion, recuperar contrasena, alta de cuentas y la API). El del SIIA puede
        /// llevar guion bajo, punto u otros simbolos, asi que solo se rechaza lo que
        /// nunca es un usuario: espacios y caracteres de control (errores al teclear
        /// o al pegar) y emojis (no caben en el utf8 de 3 bytes de la base). Ningun
        /// simbolo es un riesgo: todas las consultas llevan el usuario como parametro.
        /// </summary>
        public static bool UsuarioValido(string usuario)
        {
            return !string.IsNullOrEmpty(usuario) && usuario.Length <= LargoMaximoUsuario
                && !usuario.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || char.IsSurrogate(c));
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
