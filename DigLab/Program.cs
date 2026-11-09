using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;

namespace DigLab
{
    internal static class Program
    {
        // "Local\" limita el mutex a la sesion de Windows actual: una instancia por
        // sesion, que es exactamente lo que necesita un shell.
        private const string NombreMutex = @"Local\DigLab.InstanciaUnica";

        /// <summary>DigLab.exe --memorias-emergencia: ver <see cref="CopiarMemorias"/>.</summary>
        internal const string ArgumentoCopiarMemorias = "--memorias-emergencia";

        private static bool mostrandoError;

        /// <summary>
        /// Copia las memorias de emergencia de la base a memorias_emergencia.txt, junto a
        /// DigLab.exe (MemoriasEmergenciaDatos.CopiarAlEquipo). Lo lanzan el servicio
        /// DigLab.Vigilante cada 10 minutos y el instalador, como SYSTEM o administrador:
        /// en C:\DigLab un estudiante no puede escribir, y si lo lanza el, falla al
        /// escribir y no cambia nada. Dice lo que paso en una linea y en el codigo de
        /// salida (0 bien; 1 sin servidor; 2 sin poder escribir; 3 la base no las tiene).
        /// </summary>
        private static int CopiarMemorias()
        {
            int codigo;
            string detalle;
            try
            {
                codigo = MemoriasEmergenciaDatos.CopiarAlEquipo(Emergencia.RutaCopia, out detalle);
            }
            catch (Exception ex)
            {
                codigo = 1;
                detalle = "Fallo inesperado: " + ex.Message;
            }

            try
            {
                // En UTF-8, que es como lo lee el vigilante: la consola usaria otra codificacion.
                // Sin nadie que la lea, OpenStandardOutput da un flujo vacio.
                using (StreamWriter salida = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                {
                    salida.WriteLine(detalle);
                }
            }
            catch (Exception)
            {
                // Sin salida a donde escribir: basta el codigo.
            }

            return codigo;
        }

        /// <summary>
        /// Punto de entrada principal para la aplicación.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Antes que nada: el log ya escribe fechas.
            FijarCultura();

            // El servicio DigLab.Vigilante y el instalador (con permisos de administrador):
            // solo copiar las memorias de emergencia de la base al equipo, sin ventana.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), ArgumentoCopiarMemorias) > 0)
            {
                Environment.ExitCode = CopiarMemorias();
                return;
            }

            // Antes de que el log cree la carpeta nueva.
            NombreAnterior.TraerDatos();

            bool primeraInstancia;
            using (Mutex mutex = new Mutex(true, NombreMutex, out primeraInstancia))
            {
                if (!primeraInstancia)
                {
                    ActivarInstanciaExistente();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Tiene que ir antes de crear cualquier control. Sin esto, una
                // excepcion no capturada muestra el dialogo generico de .NET y, si
                // DigLab es el shell, deja la sesion en negro.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                    ManejarExcepcion("Excepcion no controlada en el hilo de interfaz", e.Exception, false);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    ManejarExcepcion("Excepcion no controlada", e.ExceptionObject as Exception, e.IsTerminating);

                Log.Info("Arranque de DigLab " + Assembly.GetExecutingAssembly().GetName().Version
                    + " | kiosco=" + Kiosco.Activo
                    + " | equipo=" + Environment.MachineName
                    + " | usuario de Windows=" + Environment.UserName
                    + " | carpeta=" + AppDomain.CurrentDomain.BaseDirectory);

                Log.Info(CarpetaDatos.Diagnostico);

                // Antes de ensenar nada: si la sesion anterior las dejo sueltas, aqui se ponen.
                DirectivasKiosco.Bloquear();

                // Que se vea en el log si este equipo tiene salida de emergencia.
                // Enterarse el dia que hace falta, con el servidor caido y una
                // clase esperando, es tarde.
                Log.Info(Emergencia.DescribirSinServidor());

                // El envio de las bitacoras que quedaron en el equipo va en su propio
                // hilo desde ya: tambien si DigLab se relanza con el equipo liberado
                // (SesionEquipo.Reanudar) o mientras se ve la pantalla de error.
                EnviadorBitacoras.Iniciar();

                try
                {
                    AppContexto contexto = new AppContexto();
                    if (contexto.Iniciar())
                    {
                        Application.Run(contexto);
                    }
                }
                catch (Exception ex)
                {
                    ManejarExcepcion("Fallo durante el arranque", ex, true);
                }

                EnviadorBitacoras.Detener();
                Log.Info("Fin de la aplicacion.");
                GC.KeepAlive(mutex);
            }
        }

        /// <summary>
        /// Espanol de Mexico en todo DigLab, tenga Windows la configuracion
        /// regional que tenga: de la cultura salen los separadores de
        /// "dd/MM/yyyy" y "HH:mm" y los nombres de dias y meses. Vale tambien para
        /// los hilos que se crean despues (Task.Run, el enviador de bitacoras), y
        /// GetCultureInfo la da sin los cambios del usuario de Windows. Lo que se
        /// guarda (base, copia local, log) usa InvariantCulture o formatos como
        /// yyyy-MM-dd, que con es-MX salen igual. Los DateTimePicker no la miran:
        /// con Format = Short muestran la fecha corta de Windows.
        /// </summary>
        private static void FijarCultura()
        {
            CultureInfo mexico = CultureInfo.GetCultureInfo("es-MX");
            CultureInfo.DefaultThreadCurrentCulture = mexico;
            CultureInfo.DefaultThreadCurrentUICulture = mexico;
            Thread.CurrentThread.CurrentCulture = mexico;
            Thread.CurrentThread.CurrentUICulture = mexico;
        }

        private static void ManejarExcepcion(string contexto, Exception ex, bool terminando)
        {
            Log.Error(contexto, ex);

            // Si falla la propia pantalla de error no hay que volver a entrar aqui.
            if (mostrandoError)
            {
                return;
            }

            mostrandoError = true;
            try
            {
                // La causa (si fue la red, se dice) y que hacer; el texto tecnico solo
                // va al registro, cuya ruta ensena la pantalla de error.
                bool red = FueLaRed(ex);
                string mensaje = (terminando
                        ? "Ocurrió un error grave y la aplicación tiene que cerrarse."
                        : "Ocurrió un error inesperado. La aplicación continuará funcionando.")
                    + (red ? " La causa fue que se perdió la conexión con el servidor." : "");
                mensaje += Environment.NewLine + Environment.NewLine
                    + (red ? "Revisa la red; si" : "Si") + " el problema se repite, avisa a la persona responsable del laboratorio.";

                PantallaError.Mostrar("Error inesperado", mensaje, terminando ? "Cerrar" : "Continuar", false, 0);
            }
            catch (Exception exPantalla)
            {
                Log.Error("Fallo al mostrar la pantalla de error", exPantalla);
            }
            finally
            {
                mostrandoError = false;
            }
        }

        /// <summary>
        /// Si se puede culpar a la red (MensajeDeFallo.EsDeRed), sin lanzar nunca:
        /// sin MySql.Data (copia incompleta, un antivirus) no se puede clasificar,
        /// y la pantalla de error tiene que salir igual. En la duda, la red no.
        /// </summary>
        private static bool FueLaRed(Exception ex)
        {
            try
            {
                return MensajeDeFallo.EsDeRed(ex);
            }
            catch (Exception exRed)
            {
                Log.Error("No se pudo saber si el error fue de red", exRed);
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        // Segunda instancia: en vez de abrir otra ventana, se trae al frente la que
        // ya existe y se termina en silencio.
        private static void ActivarInstanciaExistente()
        {
            try
            {
                Process actual = Process.GetCurrentProcess();
                foreach (Process proceso in Process.GetProcessesByName(actual.ProcessName))
                {
                    if (proceso.Id == actual.Id)
                    {
                        continue;
                    }

                    IntPtr ventana = proceso.MainWindowHandle;
                    if (ventana != IntPtr.Zero)
                    {
                        ShowWindow(ventana, SW_RESTORE);
                        SetForegroundWindow(ventana);
                    }
                    else
                    {
                        // No deberia pasar (los principales terminan el proceso al
                        // cerrarse), pero si pasa que al menos quede rastro.
                        Log.Aviso("Ya hay una instancia de DigLab (pid " + proceso.Id + ") sin ventana visible; no se abre otra.");
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo activar la instancia existente", ex);
            }
        }
    }
}
