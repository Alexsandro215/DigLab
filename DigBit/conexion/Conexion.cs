using MySql.Data.MySqlClient;
using System;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Threading;
using DigBit.Infraestructura;

namespace DigBit
{
    internal class Conexion
    {
        // Entrada de <connectionStrings>; los datos reales viven en connections.config.
        private const string NombreConexion = "DigBit";

        // Permite sobrescribir la cadena sin editar archivos (util para despliegues).
        private const string VariableEntorno = "DIGBIT_CONNECTION_STRING";

        private const string Ayuda = "Copia 'connections.config.example' como 'connections.config' " +
            "junto al ejecutable y coloca ahi los datos de la base de datos.";

        // MySql.Data espera 15 s por defecto antes de rendirse, y todas las consultas
        // van en el hilo de interfaz: con el servidor caido la ventana se congela ese
        // tiempo en cada consulta. En una red local 5 s es de sobra. Si la cadena ya
        // trae su propio timeout, se respeta.
        private const uint TimeoutConexionSegundos = 5;

        // Las decisiones de horario usan el reloj del servidor (NOW(), CURDATE()),
        // no el de la PC. Pero el servidor del TESCHI (sql.teschi.edu.mx) corre en
        // UTC, y sin esto todas las ventanas de clase quedarian corridas 6 horas.
        // Se fija la zona de cada conexion en la hora del centro de Mexico, que
        // desde octubre de 2022 no cambia en verano. Con un desfase numerico no
        // hacen falta las tablas de zonas horarias de MySQL, y en un servidor que
        // ya este en hora de Mexico no cambia nada. El esquema no tiene columnas
        // TIMESTAMP, asi que ningun dato guardado se reinterpreta.
        private const string ZonaHoraria = "-06:00";

        // Por encima de esto se avisa en el log: el modo sin conexion usa el
        // reloj de la PC y no cuadraria con el horario.
        private const int DesfaseRelojAvisoSegundos = 300;

        private readonly string sCadenaConexion;

        // La sesion de un encargado de laboratorio (solo consulta): mientras dura,
        // todo GetConexion va con la cuenta de MySQL de solo lectura
        // (CuentaDeConsulta), en cualquier equipo. El envio de la cola sigue con
        // la cuenta del equipo: es trabajo del equipo, no de quien entro.
        private static volatile string cadenaDeSesion;

        public Conexion()
        {
            sCadenaConexion = AplicarTimeoutPorDefecto(ObtenerCadenaConexion());
        }

        public MySqlConnection GetConexion()
        {
            return Abrir(cadenaDeSesion ?? sCadenaConexion);
        }

        /// <summary>Desde ahora, hasta cerrar la sesion, todo se lee con esta cadena (la de solo consulta).</summary>
        public static void UsarCuentaDeSesion(string cadena)
        {
            cadenaDeSesion = string.IsNullOrEmpty(cadena) ? null : AplicarTimeoutPorDefecto(cadena);
        }

        /// <summary>Otra vez la cuenta del equipo (al cerrar la sesion o al entrar otro).</summary>
        public static void UsarCuentaDelEquipo()
        {
            cadenaDeSesion = null;
        }

        /// <summary>La sesion va con una cuenta propia (la de solo consulta).</summary>
        public static bool ConCuentaDeSesion
        {
            get { return cadenaDeSesion != null; }
        }

        /// <summary>Para quien arma otra cadena con el mismo servidor y la misma base (CuentaDeConsulta).</summary>
        internal static string CadenaDelEquipo()
        {
            return AplicarTimeoutPorDefecto(ObtenerCadenaConexion());
        }

        /// <summary>Abre una cadena cualquiera con la zona horaria de siempre (para probar la de solo consulta).</summary>
        internal static MySqlConnection AbrirCadena(string cadena)
        {
            return Abrir(AplicarTimeoutPorDefecto(cadena));
        }

        /// <summary>
        /// Para el envio de la cola en segundo plano: ademas del tiempo de
        /// conexion, cada orden se rinde a los 15 s (el driver espera 30 por
        /// defecto). Asi un corte a media subida no deja el envio colgado.
        /// </summary>
        public MySqlConnection GetConexionParaEnvio()
        {
            string cadena = sCadenaConexion;
            try
            {
                MySqlConnectionStringBuilder constructor = new MySqlConnectionStringBuilder(sCadenaConexion);
                constructor.DefaultCommandTimeout = 15;
                cadena = constructor.ConnectionString;
            }
            catch (Exception)
            {
                // Cadena que el constructor no entiende: Open() dara el error real.
            }

            return Abrir(cadena);
        }

        private static MySqlConnection Abrir(string cadena)
        {
            MySqlConnection conexion = new MySqlConnection(cadena);
            AbrirSinContexto(conexion);
            try
            {
                FijarZonaHoraria(conexion);
            }
            catch
            {
                conexion.Dispose();
                throw;
            }

            return conexion;
        }

        /// <summary>
        /// Open() sin el contexto de sincronizacion del hilo. En MySql.Data 8.2,
        /// Open() es la version asincrona esperada a la fuerza, y por dentro espera
        /// el semaforo global de los pools sin ConfigureAwait(false): si en ese
        /// momento lo tiene otro hilo (el envio de la cola, una consulta en segundo
        /// plano, ClearAllPools), la continuacion se manda al hilo de la pantalla,
        /// que esta bloqueado esperando a Open(), y la pantalla se queda colgada
        /// para siempre. Reproducido: con el contexto de WinForms se colgaba en 2 s;
        /// sin el, 1 650 aperturas en 30 s sin fallo (scratchpad/estres).
        /// </summary>
        private static void AbrirSinContexto(MySqlConnection conexion)
        {
            SynchronizationContext previo = SynchronizationContext.Current;
            if (previo == null)
            {
                conexion.Open();
                return;
            }

            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                conexion.Open();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previo);
            }
        }

        private static void FijarZonaHoraria(MySqlConnection conexion)
        {
            using (MySqlCommand comando = new MySqlCommand("SET time_zone = '" + ZonaHoraria + "'", conexion))
            {
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Abre y cierra una conexion de prueba. Si algo falla (configuracion,
        /// red, credenciales, base inexistente) deja pasar la excepcion original
        /// para que el arranque la muestre con contexto.
        /// </summary>
        public static void Probar()
        {
            Probar(true);
        }

        /// <summary>
        /// Con anotar = false no deja la linea "Probando conexion" en el log: la
        /// vigilancia sin conexion prueba cada minuto, y eso llenaba el log.
        /// </summary>
        public static void Probar(bool anotar)
        {
            string cadena = AplicarTimeoutPorDefecto(ObtenerCadenaConexion());
            if (anotar)
            {
                Log.Info("Probando conexion: " + DescribirSinSecretos(cadena));
            }

            using (MySqlConnection conexion = new MySqlConnection(cadena))
            {
                AbrirSinContexto(conexion);
                FijarZonaHoraria(conexion);
                using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
                {
                    DateTime servidor = Convert.ToDateTime(comando.ExecuteScalar());
                    TimeSpan desfase = DateTime.Now - servidor;
                    string mensaje = "Hora del servidor (UTC" + ZonaHoraria + "): " + servidor.ToString("yyyy-MM-dd HH:mm:ss")
                        + "; el reloj de este equipo va " + DescribirDesfase(desfase) + ".";
                    if (Math.Abs(desfase.TotalSeconds) > DesfaseRelojAvisoSegundos)
                    {
                        Log.Aviso(mensaje + " Sin conexion, el horario se decidiria con ese reloj.");
                    }
                    else
                    {
                        Log.Info(mensaje);
                    }
                }
            }
        }

        private static string DescribirDesfase(TimeSpan desfase)
        {
            double segundos = Math.Abs(desfase.TotalSeconds);
            if (segundos < 2)
            {
                return "en hora";
            }

            string cuanto = segundos < 120 ? (int)segundos + " s" : (int)(segundos / 60) + " min";
            return (desfase.Ticks > 0 ? "adelantado " : "atrasado ") + cuanto;
        }

        // Para el log: servidor, base, usuario y ajustes efectivos; nunca la contrasena.
        private static string DescribirSinSecretos(string cadena)
        {
            try
            {
                MySqlConnectionStringBuilder c = new MySqlConnectionStringBuilder(cadena);
                return "server=" + c.Server + " port=" + c.Port + " database=" + c.Database
                    + " user=" + c.UserID + " timeout=" + c.ConnectionTimeout + "s sslmode=" + c.SslMode;
            }
            catch (Exception)
            {
                return "(cadena no interpretable por el driver)";
            }
        }

        private static string AplicarTimeoutPorDefecto(string cadena)
        {
            if (Regex.IsMatch(cadena, @"connect(ion)?\s*timeout", RegexOptions.IgnoreCase))
            {
                return cadena;
            }

            try
            {
                MySqlConnectionStringBuilder constructor = new MySqlConnectionStringBuilder(cadena);
                constructor.ConnectionTimeout = TimeoutConexionSegundos;
                return constructor.ConnectionString;
            }
            catch (Exception)
            {
                // Cadena que el constructor no entiende: se deja tal cual para que
                // Open() de el error real en vez de uno de sintaxis aqui.
                return cadena;
            }
        }

        internal static string ObtenerCadenaConexion()
        {
            string desdeEntorno = Environment.GetEnvironmentVariable(VariableEntorno);
            if (!string.IsNullOrEmpty(desdeEntorno))
            {
                return desdeEntorno;
            }

            ConnectionStringSettings ajuste;
            try
            {
                ajuste = ConfigurationManager.ConnectionStrings[NombreConexion];
            }
            catch (ConfigurationErrorsException ex)
            {
                throw new ConfigurationErrorsException("No se pudo leer la configuracion de conexion. " + Ayuda, ex);
            }

            if (ajuste == null || string.IsNullOrEmpty(ajuste.ConnectionString))
            {
                throw new ConfigurationErrorsException(
                    "Falta la cadena de conexion '" + NombreConexion + "'. " + Ayuda);
            }

            return ajuste.ConnectionString;
        }
    }
}
