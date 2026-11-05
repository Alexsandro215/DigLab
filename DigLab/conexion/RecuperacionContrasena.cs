using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using DigLab.Infraestructura;

namespace DigLab.conexion
{
    /// <summary>
    /// Habla con la API de recuperacion de contrasena (api/recuperar.php, en el
    /// hosting con PHP de la escuela): pide que le manden al alumno un codigo a su
    /// correo y, con ese codigo, pone la contrasena nueva. DigLab no toca la
    /// contrasena ni los codigos: lo hace la API, que es la que tiene la cuenta de
    /// correo (y asi la contrasena de esa cuenta no esta en ningun equipo).
    /// </summary>
    internal static class RecuperacionContrasena
    {
        private const int SegundosDeEspera = 20;
        private static readonly object candado = new object();
        // Lo ultimo leido de la base (null: nada todavia): sin conexion con ella se sigue usando.
        private static ApiRecuperacion deLaBase;

        /// <summary>
        /// A donde llamar. Lo normal es lo que puso el administrador en Ajustes
        /// (la base, db/13), que vale para todos los equipos. Si este equipo tiene
        /// su propia direccion (UrlRecuperacion en DigLab.exe.config, o la variable
        /// de MAQUINA DIGLAB_URL_RECUPERACION), manda esa, con su ClaveRecuperacion.
        /// La base se lee cada vez, porque el administrador puede haberla cambiado;
        /// si ahora no se puede leer, vale la ultima leida, y si no hay ninguna,
        /// ApiRecuperacion.SinConsultar (no es lo mismo que desactivada).
        /// </summary>
        public static ApiRecuperacion Buscar()
        {
            ApiRecuperacion propia = DeEsteEquipo();
            if (propia.Configurada)
            {
                return propia;
            }

            if (!SinConexion.Activo)
            {
                try
                {
                    Dictionary<string, string> ajustes = AjustesDatos.Leer(AjustesDatos.UrlRecuperacion, AjustesDatos.ClaveRecuperacion);
                    ApiRecuperacion leida = ajustes == null ? ApiRecuperacion.Ninguna
                        : new ApiRecuperacion(Ajuste(ajustes, AjustesDatos.UrlRecuperacion), Ajuste(ajustes, AjustesDatos.ClaveRecuperacion));
                    lock (candado)
                    {
                        deLaBase = leida;
                    }

                    return leida;
                }
                catch (Exception ex)
                {
                    Log.Error("Recuperar contrasena: no se pudo leer su direccion de la base; se usa la ultima leida", ex);
                }
            }

            lock (candado)
            {
                return deLaBase ?? ApiRecuperacion.SinConsultar;
            }
        }

        /// <summary>La de DigLab.exe.config (o las variables de entorno de la maquina), si este equipo tiene una.</summary>
        public static ApiRecuperacion DeEsteEquipo()
        {
            return new ApiRecuperacion(Valor("DIGLAB_URL_RECUPERACION", "UrlRecuperacion"), Valor("DIGLAB_CLAVE_RECUPERACION", "ClaveRecuperacion"));
        }

        /// <summary>Null si la direccion sirve; si no, por que (para el administrador).</summary>
        public static string ProblemaDeDireccion(string url)
        {
            Uri uri;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return "Escribe la dirección completa, como https://www.teschi.edu.mx/diglab/recuperar.php.";
            }

            // http solo para probar en la propia PC: por la red, la contrasena nueva viajaria sin cifrar.
            if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            {
                return "La dirección tiene que empezar con https://: por http la contraseña nueva viajaría sin cifrar.";
            }

            return null;
        }

        /// <summary>
        /// Lo mismo, para la direccion de toda la escuela (Ajustes): ademas, que no
        /// sea la de la propia PC. http://127.0.0.1 o localhost sirven para probar
        /// en un equipo (UrlRecuperacion en su DigLab.exe.config), pero guardadas
        /// en la base cada equipo se llamaria a si mismo y ninguno recuperaria nada.
        /// </summary>
        public static string ProblemaDeDireccionParaTodos(string url)
        {
            string problema = ProblemaDeDireccion(url);
            if (problema != null)
            {
                return problema;
            }

            Uri uri = new Uri(url.Trim(), UriKind.Absolute);
            if (uri.IsLoopback)
            {
                return "Esa es la dirección de esta PC: en los demás equipos apuntaría a ellos mismos y no funcionaría. "
                    + "Pon la dirección https:// del hosting.";
            }

            return null;
        }

        /// <summary>Que le manden un codigo a su correo. Ok con el correo oculto ("a***@...") y los minutos que vale.</summary>
        public static RespuestaRecuperacion Solicitar(ApiRecuperacion api, string matricula)
        {
            Log.Info("Recuperar contrasena: se pide un codigo para " + matricula + ".");
            return Llamar(api, new Dictionary<string, string> { { "accion", "solicitar" }, { "matricula", matricula } });
        }

        /// <summary>Con el codigo del correo, la contrasena nueva.</summary>
        public static RespuestaRecuperacion Confirmar(ApiRecuperacion api, string matricula, string codigo, string contrasena)
        {
            RespuestaRecuperacion respuesta = Llamar(api, new Dictionary<string, string>
            {
                { "accion", "confirmar" }, { "matricula", matricula }, { "codigo", codigo }, { "contrasena", contrasena }
            });
            Log.Info("Recuperar contrasena: " + matricula + (respuesta.Ok ? " puso una contrasena nueva con el codigo del correo." : " no pudo: " + respuesta.Mensaje));
            return respuesta;
        }

        /// <summary>El boton "Probar" de Ajustes: direccion, clave, base y cuenta de correo, sin mandar ningun correo.</summary>
        public static RespuestaRecuperacion Probar(ApiRecuperacion api)
        {
            RespuestaRecuperacion respuesta = Llamar(api, new Dictionary<string, string> { { "accion", "probar" } });
            Log.Info("Recuperar contrasena: prueba de " + api.Url + ": " + respuesta.Mensaje);
            return respuesta;
        }

        /// <summary>
        /// Un POST de formulario con la clave. La API responde JSON tambien en los
        /// errores (400, 404, 429...), con un mensaje para el alumno. Lanza
        /// ErrorDeRecuperacion, ya redactado, si no hay respuesta util.
        /// </summary>
        private static RespuestaRecuperacion Llamar(ApiRecuperacion api, IDictionary<string, string> campos)
        {
            if (!api.Configurada)
            {
                throw new ErrorDeRecuperacion("La recuperación por correo todavía no está activada. Pide a la administración de DigLab que te ponga una contraseña nueva.",
                    "no hay dirección");
            }

            string problema = ProblemaDeDireccion(api.Url);
            if (problema != null)
            {
                Log.Aviso("Recuperar contrasena: la direccion de la API no sirve (" + api.Url + "): " + problema);
                throw new ErrorDeRecuperacion("La recuperación por correo está mal configurada. Avisa a la persona responsable del laboratorio.", problema);
            }

            HttpWebRequest pedido = (HttpWebRequest)WebRequest.Create(api.Url);
            pedido.Method = "POST";
            pedido.ContentType = "application/x-www-form-urlencoded";
            pedido.Accept = "application/json";
            pedido.UserAgent = "DigLab/" + typeof(RecuperacionContrasena).Assembly.GetName().Version;
            pedido.Timeout = SegundosDeEspera * 1000;
            pedido.ReadWriteTimeout = SegundosDeEspera * 1000;
            // Sin "Expect: 100-continue": algunos hostings lo contestan con un 417.
            pedido.ServicePoint.Expect100Continue = false;
            // Una redireccion no se sigue: ante un 301/302 .NET repetiria la
            // peticion como GET y sin datos, y con un 307 los mandaria a donde
            // diga el otro servidor, aunque sea por http. Se avisa de a donde va.
            pedido.AllowAutoRedirect = false;
            if (api.Clave.Length > 0)
            {
                pedido.Headers["X-DigLab-Clave"] = api.Clave;
            }

            // Que equipo pide, para el limite por equipo de la API: todos los del
            // laboratorio salen a internet por la misma IP. El numero va primero,
            // por si la API corta un nombre de laboratorio largo.
            string laboratorio = LaboratorioEquipo.NombreConfigurado;
            pedido.Headers["X-DigLab-Equipo"] = Uri.EscapeDataString(
                LaboratorioEquipo.NumeroMaquina + (string.IsNullOrEmpty(laboratorio) ? "" : " @ " + laboratorio));

            byte[] cuerpo = Encoding.UTF8.GetBytes(string.Join("&",
                campos.Select(c => Uri.EscapeDataString(c.Key) + "=" + Uri.EscapeDataString(c.Value ?? ""))));
            HttpWebResponse respuesta;
            try
            {
                pedido.ContentLength = cuerpo.Length;
                using (Stream flujo = pedido.GetRequestStream())
                {
                    flujo.Write(cuerpo, 0, cuerpo.Length);
                }

                respuesta = (HttpWebResponse)pedido.GetResponse();
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse)
            {
                // 4xx/5xx: la API explica que paso en el cuerpo.
                respuesta = (HttpWebResponse)ex.Response;
            }
            catch (WebException ex)
            {
                Log.Error("Recuperar contrasena: no se pudo hablar con la API (" + ex.Status + ")", ex);
                throw new ErrorDeRecuperacion(ex.Status == WebExceptionStatus.Timeout
                    ? "El servicio de recuperación tardó demasiado en contestar. Inténtalo de nuevo en un momento."
                    : "No se pudo conectar con el servicio de recuperación. Revisa que haya internet o avisa a la persona responsable del laboratorio.",
                    ex.Message + " (" + ex.Status + ")");
            }

            using (respuesta)
            {
                int estado = (int)respuesta.StatusCode;
                if (estado >= 300 && estado < 400)
                {
                    string destino = respuesta.Headers[HttpResponseHeader.Location];
                    Log.Aviso("Recuperar contrasena: la direccion " + api.Url + " redirige (HTTP " + estado + ") a " + destino);
                    throw new ErrorDeRecuperacion("La recuperación por correo está mal configurada. Avisa a la persona responsable del laboratorio.",
                        "la dirección redirige (HTTP " + estado + ") a " + (string.IsNullOrEmpty(destino) ? "otra" : destino)
                        + ". Pon en Ajustes la dirección final.");
                }

                string texto;
                try
                {
                    using (StreamReader lector = new StreamReader(respuesta.GetResponseStream(), Encoding.UTF8))
                    {
                        texto = lector.ReadToEnd();
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is WebException)
                {
                    Log.Error("Recuperar contrasena: se corto la respuesta de la API", ex);
                    // El detalle, sin el texto de Windows (ya esta arriba, en el log): Probar
                    // ensena tal cual un detalle que hable de la direccion.
                    throw new ErrorDeRecuperacion("Se cortó la conexión con el servicio de recuperación. Inténtalo de nuevo en un momento.",
                        "la respuesta se corto a medias: " + ex.GetType().Name);
                }

                RespuestaRecuperacion leida = Leer(texto);
                if (leida == null || string.IsNullOrEmpty(leida.Mensaje))
                {
                    Log.Aviso("Recuperar contrasena: la API respondio HTTP " + (int)respuesta.StatusCode + " sin el JSON esperado: "
                        + (texto.Length > 200 ? texto.Substring(0, 200) : texto));
                    throw new ErrorDeRecuperacion("El servicio de recuperación respondió algo inesperado. Avisa a la persona responsable del laboratorio.",
                        "HTTP " + (int)respuesta.StatusCode + " sin la respuesta de la API: ¿es la dirección de recuperar.php?");
                }

                leida.Estado = (int)respuesta.StatusCode;
                return leida;
            }
        }

        private static RespuestaRecuperacion Leer(string json)
        {
            try
            {
                using (MemoryStream flujo = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (RespuestaRecuperacion)new DataContractJsonSerializer(typeof(RespuestaRecuperacion)).ReadObject(flujo);
                }
            }
            catch (SerializationException)
            {
                return null;
            }
        }

        private static string Ajuste(Dictionary<string, string> ajustes, string clave)
        {
            string valor;
            return ajustes.TryGetValue(clave, out valor) ? valor : "";
        }

        private static string Valor(string variable, string clave)
        {
            // Solo la variable de la MAQUINA, que pone un administrador. La del
            // proceso hereda las de la cuenta, y la cuenta del laboratorio puede
            // escribirse las suyas (HKCU\Environment): con una direccion propia
            // se quedaria con la matricula, el codigo y la contrasena nueva de
            // quien recupere su cuenta en ese equipo.
            string valor = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Machine);
            if (string.IsNullOrWhiteSpace(valor))
            {
                valor = ConfigurationManager.AppSettings[clave];
            }

            return (valor ?? "").Trim();
        }
    }

    /// <summary>A donde llama "Olvidaste tu contrasena?": la direccion de la API y la clave que se le manda.</summary>
    internal sealed class ApiRecuperacion
    {
        /// <summary>Desactivada: el administrador no puso ninguna (o falta db/13).</summary>
        public static readonly ApiRecuperacion Ninguna = new ApiRecuperacion("", "");

        /// <summary>No se sabe: no se pudo leer la base y no hay nada leido antes.</summary>
        public static readonly ApiRecuperacion SinConsultar = new ApiRecuperacion("", "") { NoSeSupo = true };

        public ApiRecuperacion(string url, string clave)
        {
            Url = (url ?? "").Trim();
            Clave = (clave ?? "").Trim();
        }

        /// <summary>True si no se pudo saber si esta activada (sin conexion con la base).</summary>
        public bool NoSeSupo { get; private set; }

        public string Url { get; private set; }

        public string Clave { get; private set; }

        /// <summary>False sin direccion: el inicio de sesion dice que pidan una contrasena nueva al administrador de DigLab.</summary>
        public bool Configurada
        {
            get { return Url.Length > 0; }
        }
    }

    /// <summary>Lo que contesta la API: si salio bien y un mensaje para el alumno.</summary>
    [DataContract]
    internal class RespuestaRecuperacion
    {
        [DataMember(Name = "ok")]
        public bool Ok { get; set; }

        [DataMember(Name = "mensaje")]
        public string Mensaje { get; set; }

        /// <summary>El correo al que se mando, oculto ("a***@teschi.edu.mx").</summary>
        [DataMember(Name = "correo", IsRequired = false)]
        public string Correo { get; set; }

        /// <summary>Los minutos que vale el codigo.</summary>
        [DataMember(Name = "minutos", IsRequired = false)]
        public int Minutos { get; set; }

        /// <summary>El codigo HTTP de la respuesta (no viene en el JSON).</summary>
        public int Estado { get; set; }
    }

    /// <summary>
    /// No hubo respuesta util de la API. El mensaje ya esta escrito para el
    /// alumno; Detalle es lo que paso de verdad, para el administrador (Probar).
    /// </summary>
    internal class ErrorDeRecuperacion : Exception
    {
        public ErrorDeRecuperacion(string mensaje, string detalle) : base(mensaje)
        {
            Detalle = detalle;
        }

        public string Detalle { get; private set; }
    }
}
