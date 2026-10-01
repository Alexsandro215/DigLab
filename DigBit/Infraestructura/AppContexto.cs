using System;
using System.Configuration;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;

namespace DigBit.Infraestructura
{
    /// <summary>
    /// Ciclo de vida de la aplicacion, independiente de cualquier formulario.
    /// Antes, Application.Run(new Login()) ataba la vida del proceso a la
    /// ventana de login (que luego se ocultaba y quedaba viva a escondidas), y
    /// "cerrar sesion" abria un Login nuevo encima del anterior sin limpiar nada.
    /// Ahora hay un unico punto para arrancar, cerrar sesion y salir.
    /// </summary>
    internal sealed class AppContexto : ApplicationContext
    {
        private static AppContexto actual;

        public static AppContexto Actual
        {
            get { return actual; }
        }

        public AppContexto()
        {
            actual = this;
        }

        /// <summary>
        /// Comprueba configuracion y servidor antes de mostrar el login. Devuelve
        /// false si no se pudo arrancar y el usuario decidio cerrar (solo posible
        /// fuera del modo kiosco). En kiosco reintenta sola cada 30 segundos.
        /// </summary>
        public bool Iniciar()
        {
            // Si Shell Launcher relanzo DigBit con un equipo ya liberado, se vuelve
            // a ese estado sin pedir login (solo en kiosco). Va ANTES de comprobar
            // el servidor: el traspaso es local y no necesita la base, y si la red
            // fallo justo entonces no hay que tapar el escritorio del alumno con la
            // pantalla de "sin conexion". De paso, la fase 3 se puede probar sin base.
            if (SesionEquipo.Reanudar())
            {
                return true;
            }

            while (true)
            {
                string titulo = "Sin conexion";
                string problema = ComprobarEntorno();
                if (problema == null)
                {
                    titulo = "Equipo sin laboratorio";
                    problema = ComprobarLaboratorio();
                    if (problema == null)
                    {
                        // Con servidor: se suben las bitacoras pendientes y se
                        // refresca la copia local del horario (fase 4).
                        SinConexion.AlConectar(LaboratorioEquipo.Id);
                    }
                }
                else if (SinConexion.IntentarActivar(problema))
                {
                    // Sin servidor pero con copia local valida: se atiende a los
                    // alumnos con ella y se vigila la conexion para volver.
                    problema = null;
                }

                if (problema == null)
                {
                    IniciarVigilanciaConexion();
                    MostrarLogin();
                    return true;
                }

                DialogResult respuesta = PantallaError.Mostrar(
                    titulo,
                    problema,
                    "Reintentar",
                    !Kiosco.Activo,
                    Kiosco.Activo ? 30 : 0);

                if (respuesta != DialogResult.Retry)
                {
                    Log.Info("El usuario cerro la aplicacion desde la pantalla de error de arranque.");
                    return false;
                }

                Log.Info("Reintentando la comprobacion de arranque.");
            }
        }

        public void MostrarLogin()
        {
            // Login se registra solo en su constructor (como los demas principales).
            new Login().Show();
        }

        /// <summary>
        /// Suscribe un formulario principal al control de "ultima ventana visible".
        /// Fuera del kiosco, cerrar la ultima termina el proceso; sin esto quedaba
        /// un proceso sin ventanas reteniendo el mutex de instancia unica y DigBit
        /// "ya no abria". Lo llaman los constructores de Login, Alumno_Principal,
        /// Profesor_Principal y PrincipalAdministrador.
        /// </summary>
        public void Registrar(Form formulario)
        {
            formulario.FormClosed -= Formulario_FormClosed;
            formulario.FormClosed += Formulario_FormClosed;
        }

        /// <summary>
        /// Vuelve a un login limpio: olvida el usuario en memoria y cierra todos
        /// los formularios abiertos, incluidos los ocultos con Hide().
        /// </summary>
        public void CerrarSesion()
        {
            Log.Info("Cierre de sesion de '" + (Datos_User.getUser() ?? "-") + "'.");
            Datos_User.Limpiar();

            // Primero se abre el login nuevo y despues se cierra lo viejo, para que
            // nunca haya un instante sin ventanas visibles (ver Formulario_FormClosed).
            Form[] abiertos = Application.OpenForms.Cast<Form>().ToArray();
            MostrarLogin();
            foreach (Form formulario in abiertos)
            {
                Kiosco.CerrarAutorizado(formulario);
            }
        }

        /// <summary>
        /// Termina el proceso. En modo kiosco se ignora: la unica salida es que
        /// Windows cierre la sesion.
        /// </summary>
        public void Salir()
        {
            if (Kiosco.Activo)
            {
                Log.Aviso("Intento de salir de la aplicacion en modo kiosco; ignorado.");
                return;
            }

            Log.Info("Salida de la aplicacion por el usuario.");
            ExitThread();
        }

        private void Formulario_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (Kiosco.Activo)
            {
                return;
            }

            // Fuera del kiosco, cerrar la ultima ventana visible termina el proceso
            // (comportamiento normal de escritorio, util al desarrollar).
            bool quedaAlgoVisible = Application.OpenForms.Cast<Form>().Any(f => f != sender && f.Visible);
            if (!quedaAlgoVisible)
            {
                Log.Info("Se cerro la ultima ventana; fin de la aplicacion.");
                ExitThread();
            }
        }

        private Timer vigilanciaConexion;
        private int minutosDesdeRefresco;

        /// <summary>
        /// Fase 4: cada minuto. Sin conexion, prueba el servidor y, si volvio,
        /// sale del modo sin conexion, sube la cola y refresca la copia. Con
        /// conexion, cada 10 minutos refresca la copia y vacia la cola por si algo
        /// quedo pendiente. Todo en silencio; los fallos van al log.
        /// </summary>
        private void IniciarVigilanciaConexion()
        {
            if (vigilanciaConexion != null)
            {
                return;
            }

            vigilanciaConexion = new Timer { Interval = 60000 };
            vigilanciaConexion.Tick += (s, e) => VigilarConexion();
            vigilanciaConexion.Start();
        }

        private void VigilarConexion()
        {
            try
            {
                if (SinConexion.Activo)
                {
                    Conexion.Probar();
                    SinConexion.Desactivar();
                    string problema = LaboratorioEquipo.Resolver();
                    if (problema != null)
                    {
                        Log.Aviso("Al recuperar la conexion: " + problema.Replace(Environment.NewLine, " "));
                    }

                    SinConexion.AlConectar(LaboratorioEquipo.Id);
                    minutosDesdeRefresco = 0;
                    return;
                }

                minutosDesdeRefresco++;
                if (minutosDesdeRefresco >= 10)
                {
                    minutosDesdeRefresco = 0;
                    SinConexion.AlConectar(LaboratorioEquipo.Id);
                }
            }
            catch (Exception ex)
            {
                // Sin conexion todavia (o volvio a caerse): se reintenta en un minuto.
                if (!SinConexion.Activo)
                {
                    Log.Aviso("Vigilancia de conexion: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Fase 6: cada equipo tiene que saber en que laboratorio esta, porque un
        /// codigo solo vale en el suyo. En kiosco es bloqueante (el alumno no
        /// podria hacer nada util); en escritorio solo se deja en el log y la
        /// pantalla de codigo se lo dira al alumno.
        /// </summary>
        private static string ComprobarLaboratorio()
        {
            try
            {
                string problema = LaboratorioEquipo.Resolver();
                if (problema == null)
                {
                    return null;
                }

                if (Kiosco.Activo)
                {
                    return problema;
                }

                Log.Aviso("Laboratorio del equipo sin resolver (modo escritorio): " + problema.Replace(Environment.NewLine, " "));
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Resolver el laboratorio del equipo", ex);
                return Kiosco.Activo
                    ? "No se pudo comprobar el laboratorio de este equipo." + Environment.NewLine + Environment.NewLine + "Detalle: " + ex.Message
                    : null;
            }
        }

        private static string ComprobarEntorno()
        {
            try
            {
                Conexion.Probar();
                Log.Info("Conexion con el servidor comprobada.");
                return null;
            }
            catch (ConfigurationErrorsException ex)
            {
                Log.Error("Configuracion de conexion", ex);
                return "No se pudo leer la configuracion de conexion a la base de datos." +
                       Environment.NewLine + Environment.NewLine + ex.Message;
            }
            catch (Exception ex)
            {
                Log.Error("Prueba de conexion con el servidor", ex);

                // El servidor contesto y rechazo el usuario o la contrasena: no es
                // un problema de red, y decir "comprueba la red" mandaba a buscar
                // donde no era (paso al instalar con la contrasena en mayusculas).
                if (AccesoDenegado(ex))
                {
                    return "El servidor de bitacoras rechazo el usuario o la contrasena de la base." +
                           Environment.NewLine + Environment.NewLine +
                           "La contrasena distingue mayusculas y minusculas. Vuelve a instalar este equipo con " +
                           "DigBit-Instalador.exe y escribela bien, o corrige C:\\DigBit\\connections.config." +
                           Environment.NewLine + Environment.NewLine +
                           "Detalle: " + ex.Message;
                }

                return "No hay conexion con el servidor de bitacoras." +
                       Environment.NewLine + Environment.NewLine +
                       "Comprueba que el equipo tiene red y que el servidor esta encendido." +
                       Environment.NewLine + Environment.NewLine +
                       "Detalle: " + ex.Message;
            }
        }

        /// <summary>Error 1045 de MySQL (Access denied), en la excepcion o en alguna de las internas.</summary>
        private static bool AccesoDenegado(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                MySql.Data.MySqlClient.MySqlException mysql = e as MySql.Data.MySqlClient.MySqlException;
                if ((mysql != null && mysql.Number == 1045) || e.Message.IndexOf("Access denied", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
