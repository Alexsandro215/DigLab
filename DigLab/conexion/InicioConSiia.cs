using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>
    /// La validacion con el SIIA del TESCHI, la misma para docentes y
    /// estudiantes (cada uno con su interruptor en Ajustes, tabla configuracion
    /// de db/13). En dos pasos, como el propio SIIA: 1) el numero y la
    /// contrasena (POST login.ashx, sin NIP), que dice si ya tiene NIP; 2) el
    /// NIP (POST nip.ashx, accion "verificar", con el tempToken del paso 1). El
    /// token final se descarta; el tempToken solo vive en la memoria entre los
    /// dos pasos. DigLab nunca escribe en el SIIA (ni crea ni cambia NIP).
    ///
    /// La contrasena y el NIP nunca van al log, a una excepcion ni a la base en
    /// claro: lo unico que se guarda es la huella PBKDF2 de los dos juntos
    /// (Combinar y Contrasenas.Hash), para dejar entrar si el SIIA no responde.
    /// El log dice solo el resultado y el numero. Quien decide que hacer con cada
    /// resultado es Consultas (Consultas.Siia.cs); aqui solo se habla con el SIIA.
    /// </summary>
    internal static class InicioConSiia
    {
        /// <summary>login.ashx del SIIA del TESCHI: la que vale sin nada guardado en Ajustes.</summary>
        public const string UrlPorOmision = "https://siia.teschi.edu.mx/Teschi/api/login.ashx";

        /// <summary>La pagina oficial del SIIA para recuperar la contrasena (con la CURP): la que vale sin nada guardado.</summary>
        public const string RecuperarPorOmision = "https://siia.teschi.edu.mx/Teschi/recuperarPass.aspx";

        /// <summary>La pagina de inicio del SIIA, donde se crea el NIP: la que vale sin nada guardado.</summary>
        public const string PaginaPorOmision = "https://siia.teschi.edu.mx/Teschi/";

        /// <summary>Lo que se le dice a quien todavia no tiene NIP del SIIA (DigLab no lo crea: nunca escribe en el SIIA).</summary>
        public const string TextoSinNip = "Aún no tienes NIP del SIIA. Entra una vez a la página del SIIA para crearlo y vuelve aquí.";

        // Lo que separa la contrasena del NIP en su huella: no se teclea en una caja de texto.
        private const char SeparadorDeHuella = '\u001F';

        /// <summary>
        /// Lo que se guarda como huella de quien entra con el SIIA: la contrasena y
        /// el NIP juntos, con Contrasenas.Hash (PBKDF2 con sal), nunca en claro.
        /// Sin el NIP correcto, la huella no coincide.
        /// </summary>
        public static string Combinar(string contrasena, string nip)
        {
            return (contrasena ?? "") + SeparadorDeHuella + (nip ?? "");
        }

        /// <summary>El NIP del SIIA: exactamente 4 numeros.</summary>
        public static bool NipValido(string nip)
        {
            return nip != null && nip.Length == 4 && nip.All(c => c >= '0' && c <= '9');
        }

        /// <summary>
        /// Lo que se espera al SIIA, en total (tambien la busqueda del nombre del
        /// servidor, que HttpWebRequest no cuenta en su Timeout). Corto: sin
        /// internet el docente no se queda mirando "ENTRANDO..." mas de esto.
        /// </summary>
        public const int SegundosDeEspera = 9;

        // Una respuesta de login.ashx es pequena; mas que esto no es la suya.
        private const int LargoMaximoRespuesta = 64 * 1024;

        static InicioConSiia()
        {
            // HTTPS con TLS 1.2 tambien en un Windows viejo, donde el valor por
            // omision de .NET 4.7.2 (el del sistema) puede no incluirlo.
            if ((ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12) == 0)
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
        }

        /// <summary>
        /// Como esta en Ajustes, leido con esa conexion. Apagado si la base no
        /// tiene la tabla configuracion (falta db/13): todo como antes.
        /// </summary>
        public static ConfiguracionSiia Leer(MySqlConnection conexion)
        {
            if (conexion == null || !Esquema.Ajustes(conexion))
            {
                return ConfiguracionSiia.Apagada;
            }

            bool docentes = false;
            bool estudiantes = false;
            string url = "";
            string recuperar = "";
            string pagina = "";
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT clave, valor FROM configuracion WHERE clave IN (@Docentes, @Estudiantes, @Url, @Recuperar, @Pagina)", conexion))
            {
                comando.Parameters.AddWithValue("@Docentes", AjustesDatos.SiiaActivo);
                comando.Parameters.AddWithValue("@Estudiantes", AjustesDatos.SiiaEstudiantes);
                comando.Parameters.AddWithValue("@Url", AjustesDatos.SiiaUrl);
                comando.Parameters.AddWithValue("@Recuperar", AjustesDatos.SiiaRecuperarUrl);
                comando.Parameters.AddWithValue("@Pagina", AjustesDatos.SiiaPaginaUrl);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        string valor = lector.IsDBNull(1) ? "" : lector.GetString(1).Trim();
                        string clave = lector.GetString(0);
                        if (clave == AjustesDatos.SiiaActivo)
                        {
                            docentes = valor == "1";
                        }
                        else if (clave == AjustesDatos.SiiaEstudiantes)
                        {
                            estudiantes = valor == "1";
                        }
                        else if (clave == AjustesDatos.SiiaUrl)
                        {
                            url = valor;
                        }
                        else if (clave == AjustesDatos.SiiaRecuperarUrl)
                        {
                            recuperar = valor;
                        }
                        else
                        {
                            pagina = valor;
                        }
                    }
                }
            }

            return docentes || estudiantes ? new ConfiguracionSiia(docentes, estudiantes, url, recuperar, pagina) : ConfiguracionSiia.Apagada;
        }

        /// <summary>
        /// Lo mismo con una conexion propia, para "Olvidaste tu contrasena?" y
        /// Ajustes. Nunca lanza: sin servidor (o si falla) se da por apagado.
        /// </summary>
        public static ConfiguracionSiia LeerSinFallar()
        {
            if (SinConexion.Activo)
            {
                return ConfiguracionSiia.Apagada;
            }

            try
            {
                using (MySqlConnection conexion = new Conexion().GetConexion())
                {
                    return Leer(conexion);
                }
            }
            catch (Exception ex)
            {
                Log.Error("SIIA: no se pudo leer de la base si los docentes entran con el SIIA; se da por apagado", ex);
                return ConfiguracionSiia.Apagada;
            }
        }

        /// <summary>El tipo de la cuenta de DigLab con ese numero (TiposUsuario), o 0 si no hay. Lanza si no se puede leer.</summary>
        public static int TipoDeCuenta(string numeroIdentificador)
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT fk_tipo_usuario FROM usuarios WHERE numero_identificador = @Numero", conexion))
            {
                comando.Parameters.AddWithValue("@Numero", numeroIdentificador ?? "");
                object tipo = comando.ExecuteScalar();
                return tipo == null || tipo == DBNull.Value ? 0 : Convert.ToInt32(tipo, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Lo que se le dice a quien entra con el SIIA y olvido su contrasena:
        /// DigLab no la recupera; es la del SIIA y se recupera en su pagina
        /// (VentanaPaginaSiia la ensena y, fuera del kiosco, la abre).
        /// </summary>
        public const string TextoRecuperarEnSiia = "Tu contraseña es la del SIIA. Recupérala en la página del SIIA con tu CURP.";

        /// <summary>Lo mismo en una frase, con la direccion, para cuando no se sabe si quien lee entra con el SIIA.</summary>
        public static string TextoSiEresDocente(ConfiguracionSiia siia)
        {
            string quien = siia != null && siia.Docentes && siia.Estudiantes ? "Si eres docente o estudiante"
                : siia != null && siia.Estudiantes ? "Si eres estudiante" : "Si eres docente";
            return quien + ", tu contraseña es la del SIIA: recupérala en la página del SIIA con tu CURP ("
                + (siia != null ? siia.RecuperarUrl : RecuperarPorOmision) + ").";
        }

        /// <summary>Null si sirve como pagina para recuperar la contrasena: solo una direccion https:// completa (se abre en el navegador).</summary>
        public static string ProblemaDePaginaParaRecuperar(string url)
        {
            return EsPaginaHttps(url) ? null
                : "La página para recuperar la contraseña tiene que ser una dirección completa con https://, como " + RecuperarPorOmision + ".";
        }

        /// <summary>Null si sirve como pagina del SIIA para crear el NIP: solo una direccion https:// completa.</summary>
        public static string ProblemaDePaginaDelSiia(string url)
        {
            return EsPaginaHttps(url) ? null
                : "La página del SIIA tiene que ser una dirección completa con https://, como " + PaginaPorOmision + ".";
        }

        /// <summary>Lo unico que DigLab le pasa al navegador: una direccion https completa.</summary>
        public static bool EsPaginaHttps(string url)
        {
            Uri uri;
            return Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host);
        }

        /// <summary>
        /// Null si la direccion sirve para mandarle una contrasena: https:// (o
        /// http:// solo a esta misma PC, para probar con un SIIA simulado); si no,
        /// que escribir.
        /// </summary>
        public static string ProblemaDeDireccion(string url)
        {
            Uri uri;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || string.IsNullOrEmpty(uri.Host))
            {
                return "Escribe la dirección completa, como " + UrlPorOmision + ".";
            }

            if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            {
                return "La dirección tiene que empezar con https://: por http las contraseñas de los docentes viajarían sin cifrar.";
            }

            return null;
        }

        /// <summary>
        /// Paso 1, como el propio SIIA: si acepta ese numero y esa contrasena
        /// (POST login.ashx, sin NIP). Con Aceptado, si tiene NIP (NipRequerido) y
        /// el tempToken para el paso 2, que solo vive en la memoria; sin NIP
        /// configurado, SinNip. No lanza: lo que pase va en la respuesta (sin
        /// respuesta, con su causa para el log). Espera como mucho
        /// SegundosDeEspera, en segundo plano: quien lo espera con await desde la
        /// ventana no la congela.
        /// </summary>
        public static Task<RespuestaSiia> ConsultarAsync(string url, string usuario, string contrasena)
        {
            return LlamarAsync(url, CuerpoJson(new PedidoLoginSiia { Usuario = usuario ?? "", Password = contrasena ?? "" }), InterpretarLogin);
        }

        /// <summary>
        /// Paso 2: el NIP, con el tempToken del paso 1 (POST nip.ashx, accion
        /// "verificar", el paso 2A oficial: solo comprueba, no escribe nada en el
        /// SIIA). Aceptado: acceso correcto (el token que devuelve se descarta);
        /// NipIncorrecto: otro intento; TokenVencido: volver al paso 1. Nunca se
        /// llama con otra accion (configurar, cambiar o recuperar).
        /// </summary>
        public static Task<RespuestaSiia> VerificarNipAsync(string url, string tempToken, string nip)
        {
            string direccionNip = DireccionDelNip(url);
            if (direccionNip == null)
            {
                return Task.FromResult(RespuestaSiia.Sin("sin la direccion de nip.ashx"));
            }

            return LlamarAsync(direccionNip, CuerpoJson(new PedidoNipSiia { Accion = "verificar", TempToken = tempToken ?? "", Nip = nip ?? "" }), InterpretarNip);
        }

        /// <summary>nip.ashx junto a login.ashx (la misma carpeta de la API del SIIA); null si la direccion no sirve.</summary>
        public static string DireccionDelNip(string url)
        {
            Uri uri;
            if (ProblemaDeDireccion(url) != null || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                return null;
            }

            return new Uri(uri, "nip.ashx").AbsoluteUri;
        }

        private static async Task<RespuestaSiia> LlamarAsync(string url, byte[] cuerpo, Func<HttpWebResponse, RespuestaSiia> interpretar)
        {
            string problema = ProblemaDeDireccion(url);
            if (problema != null)
            {
                // Ni se intenta: por una direccion asi no sale ninguna contrasena.
                Log.Aviso("SIIA: la direccion guardada en Ajustes no sirve (" + url + "): " + problema);
                Array.Clear(cuerpo, 0, cuerpo.Length);
                return RespuestaSiia.Sin("la direccion de Ajustes no sirve");
            }

            LlamadaSiia llamada = new LlamadaSiia(url.Trim());
            Task<RespuestaSiia> tarea = Task.Run(() => llamada.Enviar(cuerpo, interpretar));
            Task primera = await Task.WhenAny(tarea, Task.Delay(TimeSpan.FromSeconds(SegundosDeEspera + 1))).ConfigureAwait(false);
            if (primera != tarea)
            {
                // Lo que quede de la llamada se corta y su resultado se ignora.
                llamada.Abortar();
                return RespuestaSiia.Sin("tiempo agotado (" + SegundosDeEspera + " s)");
            }

            return await tarea.ConfigureAwait(false);
        }

        /// <summary>
        /// "Comprobar" de Ajustes: solo que la direccion responde como login.ashx,
        /// con un GET sin datos ni credenciales (debe contestar 405, Metodo no
        /// permitido). No lanza.
        /// </summary>
        public static ComprobacionSiia Comprobar(string url)
        {
            string problema = ProblemaDeDireccion(url);
            if (problema != null)
            {
                return new ComprobacionSiia(false, problema);
            }

            LlamadaSiia llamada = new LlamadaSiia(url.Trim());
            Task<ComprobacionSiia> tarea = Task.Run(() => llamada.Comprobar());
            if (!tarea.Wait(TimeSpan.FromSeconds(SegundosDeEspera + 1)))
            {
                llamada.Abortar();
                return new ComprobacionSiia(false, "El SIIA tardó demasiado en contestar (más de " + SegundosDeEspera + " segundos). "
                    + "Revisa que este equipo tenga internet e inténtalo de nuevo en un momento.");
            }

            return tarea.Result;
        }

        /// <summary>Una llamada al SIIA, que se puede cortar desde fuera si tarda (Abortar).</summary>
        private sealed class LlamadaSiia
        {
            private readonly string url;
            private readonly object candado = new object();
            private HttpWebRequest pedido;
            private bool abortada;

            public LlamadaSiia(string url)
            {
                this.url = url;
            }

            public void Abortar()
            {
                lock (candado)
                {
                    abortada = true;
                    if (pedido != null)
                    {
                        try
                        {
                            pedido.Abort();
                        }
                        catch (Exception)
                        {
                            // Ya termino o nunca empezo: no hay nada que cortar.
                        }
                    }
                }
            }

            private HttpWebRequest Preparar(string metodo)
            {
                HttpWebRequest nuevo = (HttpWebRequest)WebRequest.Create(url);
                nuevo.Method = metodo;
                nuevo.Accept = "application/json";
                nuevo.UserAgent = "DigLab/" + typeof(InicioConSiia).Assembly.GetName().Version;
                nuevo.Timeout = SegundosDeEspera * 1000;
                nuevo.ReadWriteTimeout = SegundosDeEspera * 1000;
                // Una redireccion no se sigue: .NET repetiria el POST como GET, o
                // mandaria la contrasena a donde diga el otro servidor.
                nuevo.AllowAutoRedirect = false;
                nuevo.ServicePoint.Expect100Continue = false;
                lock (candado)
                {
                    if (abortada)
                    {
                        return null;
                    }

                    pedido = nuevo;
                }

                return nuevo;
            }

            /// <summary>
            /// Un POST con ese JSON (login.ashx o nip.ashx) y lo que quiere decir su
            /// respuesta. El cuerpo lleva la contrasena o el NIP: se borra en cuanto
            /// sale. Nunca lanza.
            /// </summary>
            public RespuestaSiia Enviar(byte[] cuerpo, Func<HttpWebResponse, RespuestaSiia> interpretar)
            {
                try
                {
                    HttpWebRequest nuevo = Preparar("POST");
                    if (nuevo == null)
                    {
                        return RespuestaSiia.Sin("tiempo agotado");
                    }

                    nuevo.ContentType = "application/json; charset=utf-8";
                    nuevo.ContentLength = cuerpo.Length;
                    using (Stream flujo = nuevo.GetRequestStream())
                    {
                        flujo.Write(cuerpo, 0, cuerpo.Length);
                    }

                    using (HttpWebResponse respuesta = Responder(nuevo))
                    {
                        return interpretar(respuesta);
                    }
                }
                catch (WebException ex)
                {
                    // Sin respuesta HTTP: la red, el nombre, la conexion segura, el tiempo.
                    // ex no lleva la contrasena (solo la direccion y la causa).
                    Log.Error("SIIA: no se pudo hablar con " + Sitio(url) + " (" + ex.Status + ")", ex);
                    return RespuestaSiia.Sin(ex.Status.ToString());
                }
                catch (Exception ex)
                {
                    Log.Error("SIIA: fallo inesperado al consultar " + Sitio(url), ex);
                    return RespuestaSiia.Sin(ex.GetType().Name);
                }
                finally
                {
                    Array.Clear(cuerpo, 0, cuerpo.Length);
                }
            }

            /// <summary>El GET de "Comprobar". Nunca lanza.</summary>
            public ComprobacionSiia Comprobar()
            {
                try
                {
                    HttpWebRequest nuevo = Preparar("GET");
                    if (nuevo == null)
                    {
                        return new ComprobacionSiia(false, "Se canceló la comprobación.");
                    }

                    using (HttpWebResponse respuesta = Responder(nuevo))
                    {
                        int estado = (int)respuesta.StatusCode;
                        Log.Info("SIIA: comprobar " + url + ": HTTP " + estado + ".");
                        switch (estado)
                        {
                            case 405:
                                return new ComprobacionSiia(true, "La dirección responde como el inicio de sesión del SIIA: a una consulta sin datos "
                                    + "contesta «Método no permitido» (405), como debe. No se mandó ninguna contraseña.");
                            case 404:
                                return new ComprobacionSiia(false, "El servidor responde, pero esa dirección no existe en él (404). Revisa que esté bien escrita "
                                    + "y que termine en login.ashx.");
                            default:
                                if (estado >= 300 && estado < 400)
                                {
                                    string destino = respuesta.Headers[HttpResponseHeader.Location];
                                    return new ComprobacionSiia(false, "La dirección redirige a otra (" + estado + (string.IsNullOrEmpty(destino) ? "" : ", a " + destino)
                                        + "). Pon la dirección final, con https://.");
                                }

                                if (estado >= 500)
                                {
                                    return new ComprobacionSiia(false, "El SIIA respondió con una falla suya (" + estado + "). Inténtalo de nuevo en un momento; "
                                        + "mientras falle, los docentes entran con la última contraseña que el SIIA les aceptó.");
                                }

                                return new ComprobacionSiia(false, "Responde, pero no como el inicio de sesión del SIIA: a una consulta sin datos debería contestar "
                                    + "«Método no permitido» (405) y contestó " + estado + ". Revisa que la dirección termine en login.ashx.");
                        }
                    }
                }
                catch (WebException ex)
                {
                    Log.Error("SIIA: comprobar " + url, ex);
                    switch (ex.Status)
                    {
                        case WebExceptionStatus.NameResolutionFailure:
                            return new ComprobacionSiia(false, "No se encontró el servidor de esa dirección: revisa que esté bien escrita y que este equipo tenga internet.");
                        case WebExceptionStatus.ConnectFailure:
                            return new ComprobacionSiia(false, "El servidor de esa dirección no aceptó la conexión: revisa la dirección e inténtalo de nuevo en un momento.");
                        case WebExceptionStatus.Timeout:
                        case WebExceptionStatus.RequestCanceled:
                            return new ComprobacionSiia(false, "El SIIA tardó demasiado en contestar (más de " + SegundosDeEspera + " segundos). "
                                + "Revisa que este equipo tenga internet e inténtalo de nuevo en un momento.");
                        case WebExceptionStatus.TrustFailure:
                        case WebExceptionStatus.SecureChannelFailure:
                            return new ComprobacionSiia(false, "No se pudo abrir una conexión segura (https con TLS 1.2) con esa dirección: revisa la dirección "
                                + "y que su certificado sea válido.");
                        default:
                            return new ComprobacionSiia(false, "No se pudo hablar con esa dirección: revisa que este equipo tenga internet; el detalle quedó "
                                + "en el registro de diagnóstico de este equipo.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("SIIA: comprobar " + url, ex);
                    return new ComprobacionSiia(false, "No se pudo comprobar por un error inesperado; el detalle quedó en el registro de diagnóstico de este equipo.");
                }
            }

            /// <summary>La respuesta tambien con 4xx o 5xx (que llegan como WebException con respuesta).</summary>
            private static HttpWebResponse Responder(HttpWebRequest pedido)
            {
                try
                {
                    return (HttpWebResponse)pedido.GetResponse();
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse)
                {
                    return (HttpWebResponse)ex.Response;
                }
            }
        }

        /// <summary>Un pedido en JSON (UTF-8, con el escapado de JSON).</summary>
        private static byte[] CuerpoJson(object pedido)
        {
            using (MemoryStream flujo = new MemoryStream())
            {
                new DataContractJsonSerializer(pedido.GetType()).WriteObject(flujo, pedido);
                return flujo.ToArray();
            }
        }

        /// <summary>
        /// Lo comun de las dos respuestas: los codigos sin cuerpo util. Null si hay
        /// que mirar el cuerpo (200, y 401 o 422, que en nip.ashx dicen por que).
        /// </summary>
        private static RespuestaSiia PorCodigo(HttpWebResponse respuesta, int estado)
        {
            switch (estado)
            {
                case 200:
                case 401:
                case 422:
                    return null;
                case 403:
                    return RespuestaSiia.De(ResultadoSiia.Inactivo, "HTTP 403");
                case 429:
                    return RespuestaSiia.Bloqueo(SegundosDeReintento(respuesta.Headers[HttpResponseHeader.RetryAfter]));
                default:
                    if (estado >= 300 && estado < 400)
                    {
                        Log.Aviso("SIIA: la direccion redirige (HTTP " + estado + ") a " + (respuesta.Headers[HttpResponseHeader.Location] ?? "otra")
                            + ": pon en Ajustes la direccion final.");
                    }

                    return RespuestaSiia.Sin("HTTP " + estado);
            }
        }

        /// <summary>El cuerpo como JSON (un objeto); null si no lo es. El cuerpo nunca va al log.</summary>
        private static XElement Objeto(HttpWebResponse respuesta)
        {
            byte[] cuerpo;
            try
            {
                cuerpo = LeerCuerpo(respuesta);
            }
            catch (Exception ex) when (ex is IOException || ex is WebException)
            {
                return null;
            }

            XElement raiz = cuerpo == null ? null : LeerJson(cuerpo);
            return raiz != null && Tipo(raiz) == "object" ? raiz : null;
        }

        /// <summary>Un booleano del JSON: null si no esta o no es booleano.</summary>
        private static bool? Booleano(XElement raiz, string nombre)
        {
            XElement elemento = raiz.Element(nombre);
            return elemento == null || Tipo(elemento) != "boolean" ? (bool?)null : elemento.Value == "true";
        }

        /// <summary>Un texto del JSON ("" si no esta, es null o no es texto).</summary>
        private static string TextoDe(XElement raiz, string nombre)
        {
            XElement elemento = raiz.Element(nombre);
            return elemento == null || Tipo(elemento) != "string" ? "" : elemento.Value;
        }

        /// <summary>
        /// El paso 1 (login.ashx sin NIP). 200 con ok=true: la contrasena es
        /// buena, y con requiereNip y tieneNip se sabe si sigue el paso 2
        /// (tieneNip=true, con su tempToken) o si aun no tiene NIP (SinNip); con
        /// requiereNip=false el acceso ya es completo. 200 con ok=false o 401:
        /// rechazo; 422 que habla del NIP sin configurar: SinNip; 403: inactiva;
        /// 429: bloqueo (Retry-After). Lo demas (redirecciones, 400, 404, 405,
        /// 5xx, un cuerpo que no es el JSON esperado) es que no dio una respuesta
        /// util. Los campos se leen por su nombre, sin depender del orden, y un
        /// null (token, tipoCarrera) no estorba.
        /// </summary>
        private static RespuestaSiia InterpretarLogin(HttpWebResponse respuesta)
        {
            int estado = (int)respuesta.StatusCode;
            RespuestaSiia porCodigo = PorCodigo(respuesta, estado);
            if (porCodigo != null)
            {
                return porCodigo;
            }

            XElement raiz = Objeto(respuesta);
            if (estado == 401)
            {
                return RespuestaSiia.De(ResultadoSiia.Rechazado, "HTTP 401");
            }

            if (estado == 422)
            {
                return raiz != null && HablaDe(TextoDe(raiz, "mensaje"), "configur")
                    ? RespuestaSiia.De(ResultadoSiia.SinNip, "HTTP 422 (sin NIP)") : RespuestaSiia.De(ResultadoSiia.Rechazado, "HTTP 422");
            }

            if (raiz == null)
            {
                return RespuestaSiia.Sin("HTTP 200 sin el JSON esperado");
            }

            bool? ok = Booleano(raiz, "ok");
            if (!ok.HasValue)
            {
                return RespuestaSiia.Sin("HTTP 200 sin \"ok\"");
            }

            if (!ok.Value)
            {
                return RespuestaSiia.De(ResultadoSiia.Rechazado, "HTTP 200 con ok=false");
            }

            XElement usuario = raiz.Element("usuario");
            DatosSiia datos = usuario != null && Tipo(usuario) == "object" ? new DatosSiia(usuario) : null;
            if (Booleano(raiz, "requiereNip") == false)
            {
                // Acceso completo sin NIP (el token que traiga se descarta).
                return RespuestaSiia.Acepto(datos, false, null, 0);
            }

            bool? tieneNip = Booleano(raiz, "tieneNip");
            if (tieneNip == false)
            {
                return RespuestaSiia.De(ResultadoSiia.SinNip, "HTTP 200, tieneNip=false");
            }

            string tempToken = TextoDe(raiz, "tempToken");
            if (tempToken.Length == 0)
            {
                return RespuestaSiia.Sin("HTTP 200 sin tempToken para el NIP");
            }

            XElement expira = raiz.Element("expiraEnMinutos");
            int minutos;
            if (expira == null || !int.TryParse(expira.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutos) || minutos <= 0 || minutos > 60)
            {
                minutos = 10;
            }

            return RespuestaSiia.Acepto(datos, true, tempToken, minutos);
        }

        /// <summary>
        /// El paso 2 (nip.ashx, verificar). 200 con ok=true: acceso correcto (el
        /// token se descarta); 200 con ok=false, 401 o 422: NIP incorrecto, o el
        /// tempToken vencio si el mensaje habla del token; 403, 429 y lo demas,
        /// como en el paso 1.
        /// </summary>
        private static RespuestaSiia InterpretarNip(HttpWebResponse respuesta)
        {
            int estado = (int)respuesta.StatusCode;
            RespuestaSiia porCodigo = PorCodigo(respuesta, estado);
            if (porCodigo != null)
            {
                return porCodigo;
            }

            XElement raiz = Objeto(respuesta);
            string mensaje = raiz == null ? "" : TextoDe(raiz, "mensaje");
            bool vencido = HablaDe(mensaje, "token") || HablaDe(mensaje, "expir") || HablaDe(mensaje, "venc");
            if (estado == 401 || estado == 422)
            {
                return RespuestaSiia.De(vencido ? ResultadoSiia.TokenVencido : ResultadoSiia.NipIncorrecto, "HTTP " + estado);
            }

            if (raiz == null)
            {
                return RespuestaSiia.Sin("HTTP 200 sin el JSON esperado");
            }

            bool? ok = Booleano(raiz, "ok");
            if (!ok.HasValue)
            {
                return RespuestaSiia.Sin("HTTP 200 sin \"ok\"");
            }

            return ok.Value
                ? RespuestaSiia.Acepto(null, false, null, 0)
                : RespuestaSiia.De(vencido ? ResultadoSiia.TokenVencido : ResultadoSiia.NipIncorrecto, "HTTP 200 con ok=false");
        }

        /// <summary>Si el mensaje del SIIA dice esa palabra (sin mayusculas ni acentos).</summary>
        private static bool HablaDe(string mensaje, string palabra)
        {
            return EnlacesSiia.Normalizar(mensaje).Contains(palabra);
        }

        private static byte[] LeerCuerpo(HttpWebResponse respuesta)
        {
            using (Stream flujo = respuesta.GetResponseStream())
            using (MemoryStream copia = new MemoryStream())
            {
                byte[] trozo = new byte[8192];
                int leidos;
                while ((leidos = flujo.Read(trozo, 0, trozo.Length)) > 0)
                {
                    copia.Write(trozo, 0, leidos);
                    if (copia.Length > LargoMaximoRespuesta)
                    {
                        return null;
                    }
                }

                return copia.ToArray();
            }
        }

        /// <summary>El JSON como XML (JsonReaderWriterFactory), que admite cualquier forma; null si no es JSON.</summary>
        private static XElement LeerJson(byte[] cuerpo)
        {
            try
            {
                using (XmlDictionaryReader lector = JsonReaderWriterFactory.CreateJsonReader(cuerpo, XmlDictionaryReaderQuotas.Max))
                {
                    return XElement.Load(lector);
                }
            }
            catch (Exception ex) when (ex is XmlException || ex is SerializationException || ex is InvalidOperationException || ex is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>"object", "string", "number", "boolean", "array" o "null", como lo marca JsonReaderWriterFactory.</summary>
        internal static string Tipo(XElement elemento)
        {
            XAttribute tipo = elemento.Attribute("type");
            return tipo == null ? "string" : tipo.Value;
        }

        /// <summary>Retry-After en segundos (o una fecha); 0 si no lo dice.</summary>
        private static int SegundosDeReintento(string valor)
        {
            valor = (valor ?? "").Trim();
            int segundos;
            if (int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out segundos))
            {
                return Math.Max(0, Math.Min(segundos, 24 * 3600));
            }

            DateTime cuando;
            if (DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out cuando))
            {
                return (int)Math.Max(0, Math.Min((cuando - DateTime.UtcNow).TotalSeconds, 24 * 3600));
            }

            return 0;
        }

        /// <summary>El servidor de una direccion, para el log ("siia.teschi.edu.mx").</summary>
        private static string Sitio(string url)
        {
            Uri uri;
            return Uri.TryCreate(url ?? "", UriKind.Absolute, out uri) ? uri.Host : "el SIIA";
        }

        [DataContract]
        private sealed class PedidoLoginSiia
        {
            [DataMember(Name = "usuario", Order = 1)]
            public string Usuario { get; set; }

            [DataMember(Name = "password", Order = 2)]
            public string Password { get; set; }
        }

        /// <summary>El paso 2A oficial: {"accion":"verificar","tempToken":"...","nip":"1234"}. Solo esa accion.</summary>
        [DataContract]
        private sealed class PedidoNipSiia
        {
            [DataMember(Name = "accion", Order = 1)]
            public string Accion { get; set; }

            [DataMember(Name = "tempToken", Order = 2)]
            public string TempToken { get; set; }

            [DataMember(Name = "nip", Order = 3)]
            public string Nip { get; set; }
        }
    }

    /// <summary>
    /// Quien entra con el SIIA (Ajustes): los docentes, los estudiantes (cada uno
    /// con su interruptor) y las direcciones del SIIA (login.ashx, la pagina para
    /// recuperar la contrasena y la de inicio, donde se crea el NIP).
    /// </summary>
    internal sealed class ConfiguracionSiia
    {
        /// <summary>Apagado (o sin db/13): todos entran con su contrasena de DigLab.</summary>
        public static readonly ConfiguracionSiia Apagada = new ConfiguracionSiia(false, false, "");

        public ConfiguracionSiia(bool docentes, bool estudiantes, string url, string recuperarUrl = null, string paginaUrl = null)
        {
            Docentes = docentes;
            Estudiantes = estudiantes;
            Url = string.IsNullOrWhiteSpace(url) ? InicioConSiia.UrlPorOmision : url.Trim();
            // Solo una https completa: es lo que se le pasa al navegador.
            RecuperarUrl = InicioConSiia.EsPaginaHttps(recuperarUrl) ? recuperarUrl.Trim() : InicioConSiia.RecuperarPorOmision;
            PaginaUrl = InicioConSiia.EsPaginaHttps(paginaUrl) ? paginaUrl.Trim() : InicioConSiia.PaginaPorOmision;
        }

        /// <summary>La pagina de inicio del SIIA, donde se crea el NIP: https, la de por omision si no hay otra (o no sirve).</summary>
        public string PaginaUrl { get; private set; }

        /// <summary>Los docentes entran con su numero de empleado y su contrasena y su NIP del SIIA (una vez enlazados).</summary>
        public bool Docentes { get; private set; }

        /// <summary>Los estudiantes entran con su matricula y su contrasena y su NIP del SIIA (su cuenta se crea o se usa sola).</summary>
        public bool Estudiantes { get; private set; }

        /// <summary>Alguien entra con el SIIA.</summary>
        public bool Activo
        {
            get { return Docentes || Estudiantes; }
        }

        /// <summary>Si una cuenta de DigLab de ese tipo (0: el numero no esta en DigLab) se comprueba con el SIIA.</summary>
        public bool ParaElTipo(int tipo)
        {
            return tipo == 0 ? Activo : tipo == TiposUsuario.Profesor ? Docentes : tipo == TiposUsuario.Alumno && Estudiantes;
        }

        /// <summary>La direccion de login.ashx (la de por omision si no hay otra guardada).</summary>
        public string Url { get; private set; }

        /// <summary>La pagina del SIIA para recuperar la contrasena: https, la de por omision si no hay otra (o no sirve).</summary>
        public string RecuperarUrl { get; private set; }

        /// <summary>El servidor, para los mensajes: "siia.teschi.edu.mx".</summary>
        public string Sitio
        {
            get
            {
                Uri uri;
                return Uri.TryCreate(Url, UriKind.Absolute, out uri) ? uri.Host : "siia.teschi.edu.mx";
            }
        }
    }

    /// <summary>Lo que contesto el SIIA al inicio de sesion, en lo que importa a DigLab.</summary>
    internal enum ResultadoSiia
    {
        /// <summary>200 con ok=true: en el paso 1, la contrasena es la buena (NipRequerido: falta el paso 2); en el paso 2, el NIP.</summary>
        Aceptado,
        /// <summary>401 (o 200 con ok=false): numero o contrasena incorrectos.</summary>
        Rechazado,
        /// <summary>403: la cuenta del SIIA esta inactiva.</summary>
        Inactivo,
        /// <summary>429: demasiados intentos; hay que esperar.</summary>
        Bloqueado,
        /// <summary>Sin respuesta util: sin red, tiempo agotado, 5xx, otro codigo o un cuerpo ilegible.</summary>
        SinRespuesta,
        /// <summary>La contrasena es buena, pero aun no tiene NIP (tieneNip=false o 422): DigLab no lo crea.</summary>
        SinNip,
        /// <summary>Paso 2: el NIP no es el bueno; se puede intentar otra vez mientras valga el tempToken.</summary>
        NipIncorrecto,
        /// <summary>Paso 2: vencio el tempToken del paso 1; hay que volver a empezar.</summary>
        TokenVencido
    }

    internal sealed class RespuestaSiia
    {
        private RespuestaSiia()
        {
        }

        public ResultadoSiia Resultado { get; private set; }

        /// <summary>Para el log, sin datos de la persona: "HTTP 401", "Timeout", "tiempo agotado (9 s)"...</summary>
        public string Causa { get; private set; }

        /// <summary>Con Bloqueado: los segundos que pide esperar (Retry-After); 0 si no los dijo.</summary>
        public int SegundosParaReintentar { get; private set; }

        /// <summary>Con Aceptado: los datos del SIIA (nombre, correo y tipo); null si no los mando.</summary>
        public DatosSiia Datos { get; private set; }

        /// <summary>Paso 1 aceptado: falta el NIP (paso 2, con TempToken).</summary>
        public bool NipRequerido { get; private set; }

        /// <summary>
        /// Paso 1: el tempToken para verificar el NIP. Solo vive en la memoria entre
        /// los dos pasos: nunca se guarda ni va al log.
        /// </summary>
        public string TempToken { get; private set; }

        /// <summary>Paso 1: cuantos minutos vale el tempToken (10 si no lo dice).</summary>
        public int MinutosDelToken { get; private set; }

        internal static RespuestaSiia De(ResultadoSiia resultado, string causa)
        {
            return new RespuestaSiia { Resultado = resultado, Causa = causa };
        }

        internal static RespuestaSiia Sin(string causa)
        {
            return De(ResultadoSiia.SinRespuesta, causa);
        }

        internal static RespuestaSiia Bloqueo(int segundos)
        {
            return new RespuestaSiia { Resultado = ResultadoSiia.Bloqueado, Causa = "HTTP 429 (" + segundos + " s)", SegundosParaReintentar = segundos };
        }

        internal static RespuestaSiia Acepto(DatosSiia datos, bool nipRequerido, string tempToken, int minutos)
        {
            return new RespuestaSiia
            {
                Resultado = ResultadoSiia.Aceptado,
                Causa = nipRequerido ? "HTTP 200, falta el NIP" : "HTTP 200",
                Datos = datos,
                NipRequerido = nipRequerido,
                TempToken = tempToken,
                MinutosDelToken = minutos
            };
        }
    }

    /// <summary>
    /// Los datos de la cuenta que manda el SIIA al aceptar (su objeto "usuario").
    /// Solo lo que DigLab usa: el nombre, los apellidos, el correo (solo para la
    /// solicitud de enlace de un docente), el area (la carrera de un estudiante)
    /// y el tipo ('E' estudiante, 'P' docente, 'A' administrativo). Se leen como
    /// texto aunque lleguen como numero; lo demas (cambioPw, permisos...) no.
    /// </summary>
    internal sealed class DatosSiia
    {
        internal DatosSiia(XElement usuario)
        {
            Usuario = Texto(usuario, "usuario");
            Nombre = Texto(usuario, "nombre");
            Paterno = Texto(usuario, "paterno");
            Materno = Texto(usuario, "materno");
            NombreCompleto = Texto(usuario, "nombreCompleto");
            Correo = Texto(usuario, "correo");
            Area = Texto(usuario, "area");
            Tipo = Texto(usuario, "tipo").ToUpperInvariant();
        }

        public string Usuario { get; private set; }
        public string Nombre { get; private set; }
        public string Paterno { get; private set; }
        public string Materno { get; private set; }
        public string NombreCompleto { get; private set; }
        public string Correo { get; private set; }

        /// <summary>"INGENIERÍA EN SISTEMAS COMPUTACIONALES": la carrera de un estudiante.</summary>
        public string Area { get; private set; }

        /// <summary>'E', 'P', 'A' (en mayusculas) o vacio si no lo mando.</summary>
        public string Tipo { get; private set; }

        public bool EsDocente
        {
            get { return Tipo == "P"; }
        }

        public bool EsEstudiante
        {
            get { return Tipo == "E"; }
        }

        public bool EsAdministrativo
        {
            get { return Tipo == "A"; }
        }

        private static string Texto(XElement padre, string nombre)
        {
            XElement elemento = padre.Element(nombre);
            if (elemento == null)
            {
                return "";
            }

            string tipo = InicioConSiia.Tipo(elemento);
            return tipo == "string" || tipo == "number" ? elemento.Value.Trim() : "";
        }
    }

    /// <summary>Lo que dijo "Comprobar" de Ajustes, ya redactado para la administracion.</summary>
    internal sealed class ComprobacionSiia
    {
        public ComprobacionSiia(bool bien, string texto)
        {
            Bien = bien;
            Texto = texto;
        }

        public bool Bien { get; private set; }

        public string Texto { get; private set; }
    }
}
