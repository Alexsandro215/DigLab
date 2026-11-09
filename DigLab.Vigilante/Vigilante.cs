using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DigLab.Infraestructura;
using Microsoft.Win32;

namespace DigLab.Vigilante
{
    /// <summary>
    /// El bucle del vigilante. Corre como SYSTEM en la sesion 0, donde el alumno
    /// no lo puede matar, y reinicia el equipo cuando vence la hora de salida del
    /// codigo con el que el alumno libero el equipo: el fin de su clase (si no puede,
    /// cierra la sesion de Windows). Lo usan el servicio (VigilanteService) y el modo
    /// consola (Program).
    /// </summary>
    internal sealed class Vigilante : IDisposable
    {
        private static readonly TimeSpan Periodo = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan AvisoAntes = TimeSpan.FromMinutes(5);

        // La copia de las memorias de emergencia (CopiarMemorias): poco despues de
        // arrancar, cada 10 minutos, y a los 2 si no se pudo (la red tarda al encender).
        private static readonly TimeSpan PrimeraCopia = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PeriodoCopia = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ReintentoCopia = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan EsperaCopia = TimeSpan.FromSeconds(90);

        private readonly bool simular;
        private readonly object candado = new object();
        private Timer temporizador;
        private Timer temporizadorCopia;
        private int copiando;
        private string ultimaCopia;

        // Lo que el vigilante RECUERDA de la sesion vigente. El archivo lo puede
        // tocar el alumno (tiene permiso de escritura sobre la carpeta): si lo
        // borra o alarga la hora de salida, manda lo recordado.
        private SesionActiva recordada;
        private bool avisada;
        private string ultimoMotivoIgnorado;
        private string anomaliaAvisada;

        public Vigilante(bool simular)
        {
            this.simular = simular;
        }

        public void Iniciar()
        {
            AsegurarCarpeta();
            AsegurarReinicioDelShell();
            Log.Info("Vigilante iniciado" + (simular ? " en modo SIMULACION (no cierra sesion ni avisa)" : "") + ". Traspaso: " + SesionActiva.Ruta);
            temporizador = new Timer(estado => Revisar(), null, TimeSpan.Zero, Periodo);
            temporizadorCopia = new Timer(estado => CopiarMemorias(), null, PrimeraCopia, Timeout.InfiniteTimeSpan);
        }

        public void Detener()
        {
            if (temporizadorCopia != null)
            {
                temporizadorCopia.Dispose();
                temporizadorCopia = null;
            }

            if (temporizador != null)
            {
                temporizador.Dispose();
                temporizador = null;
                Log.Info("Vigilante detenido.");
            }
        }

        // --- Memorias de emergencia ---------------------------------------------

        /// <summary>
        /// Renueva la copia de las memorias de emergencia de este equipo
        /// (C:\DigLab\memorias_emergencia.txt) lanzando DigLab.exe --memorias-emergencia:
        /// DigLab ya sabe leer la base, y asi el vigilante no necesita MySQL. Lo hace el
        /// vigilante porque corre como SYSTEM: en C:\DigLab un estudiante no puede
        /// escribir, y por eso no puede anadir su propia memoria a la lista con la que
        /// el equipo se abre sin servidor. Al log, solo cuando cambia lo que pasa.
        /// </summary>
        internal void CopiarMemorias()
        {
            if (Interlocked.Exchange(ref copiando, 1) == 1)
            {
                return;
            }

            bool bien = false;
            try
            {
                if (simular)
                {
                    Anotar("SIMULACION: no se copian las memorias de emergencia.", true);
                    return;
                }

                int codigo;
                string resultado = LanzarCopia(RutaDigLab(), EsperaCopia, out codigo);
                bien = codigo == 0;
                Anotar("Memorias de emergencia: " + resultado, bien);
            }
            catch (Exception ex)
            {
                Anotar("Memorias de emergencia: no se pudo lanzar la copia (" + ex.Message + ").", false);
            }
            finally
            {
                Interlocked.Exchange(ref copiando, 0);
                try
                {
                    Timer copia = temporizadorCopia;
                    if (copia != null)
                    {
                        copia.Change(bien || simular ? PeriodoCopia : ReintentoCopia, Timeout.InfiniteTimeSpan);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Se detuvo el vigilante mientras tanto.
                }
            }
        }

        private void Anotar(string texto, bool bien)
        {
            if (texto == ultimaCopia)
            {
                return;
            }

            ultimaCopia = texto;
            if (bien)
            {
                Log.Info(texto);
            }
            else
            {
                Log.Aviso(texto);
            }
        }

        /// <summary>DigLab.exe, en la carpeta de arriba de la del vigilante (C:\DigLab\Vigilante\..).</summary>
        internal static string RutaDigLab()
        {
            string carpeta = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            return Path.Combine(carpeta ?? "", "DigLab.exe");
        }

        /// <summary>
        /// Lanza DigLab.exe --memorias-emergencia y espera hasta "espera". Devuelve lo que
        /// dijo, y en codigo su codigo de salida (-1 si no termino o no esta).
        /// </summary>
        internal static string LanzarCopia(string exe, TimeSpan espera, out int codigo)
        {
            codigo = -1;
            if (!File.Exists(exe))
            {
                return "no esta " + exe + ".";
            }

            ProcessStartInfo info = new ProcessStartInfo(exe, "--memorias-emergencia")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            };
            using (Process proceso = Process.Start(info))
            {
                Task<string> salida = proceso.StandardOutput.ReadToEndAsync();
                if (!proceso.WaitForExit((int)espera.TotalMilliseconds))
                {
                    try
                    {
                        proceso.Kill();
                    }
                    catch (Exception)
                    {
                        // Ya termino, o no se deja: la siguiente vuelta lo intenta otra vez.
                    }

                    return "DigLab.exe no termino en " + (int)espera.TotalSeconds + " s; se detuvo.";
                }

                codigo = proceso.ExitCode;
                string texto = salida.Wait(5000) ? salida.Result.Trim() : "";
                return (texto.Length > 0 ? texto : "sin mensaje") + " (codigo " + codigo + ")";
            }
        }

        public void Dispose()
        {
            Detener();
        }

        internal void Revisar()
        {
            // Si la revision anterior sigue en curso (p. ej. WTSSendMessage lento),
            // esta se salta; la siguiente llega en 10 s.
            if (!Monitor.TryEnter(candado))
            {
                return;
            }

            try
            {
                RevisarInterno();
            }
            catch (Exception ex)
            {
                Log.Error("Revision del vigilante", ex);
            }
            finally
            {
                Monitor.Exit(candado);
            }
        }

        private void RevisarInterno()
        {
            DateTime ahora = DateTime.Now;
            DateTime arranque = SesionActiva.ArranqueActual();
            int sesionConsola = SesionConsolaActiva();

            SesionActiva sesion = Elegir(SesionActiva.Leer(), ahora, arranque, sesionConsola);
            if (sesion == null)
            {
                return;
            }

            if (ahora >= sesion.FinLocal)
            {
                AlTerminarLaClase(sesion);
                return;
            }

            // DigLab avisa con su propia ventana mientras vive; el vigilante solo
            // avisa si DigLab ya no esta (latido viejo).
            if (!avisada && ahora >= sesion.FinLocal - AvisoAntes && !sesion.LatidoReciente(ahora))
            {
                avisada = true;
                EnviarAviso(sesion);
            }
        }

        /// <summary>
        /// Decide con que sesion se trabaja: la del archivo si es vigente y no
        /// contradice a la recordada; la recordada si el archivo desaparecio o
        /// fue alargado. Devuelve null si no hay nada que vigilar.
        /// </summary>
        private SesionActiva Elegir(SesionActiva enArchivo, DateTime ahora, DateTime arranque, int sesionConsola)
        {
            if (recordada != null && recordada.Evaluar(ahora, arranque, sesionConsola) != SesionActiva.Vigencia.Vigente)
            {
                Log.Info("La sesion recordada de " + recordada.Usuario + " ya no aplica; se olvida.");
                Olvidar();
            }

            if (enArchivo == null)
            {
                if (recordada != null && ultimoMotivoIgnorado != "sin-archivo")
                {
                    ultimoMotivoIgnorado = "sin-archivo";
                    Log.Aviso("El traspaso desaparecio o no se puede leer antes de la hora de salida; se mantiene la sesion recordada de "
                        + recordada.Usuario + " hasta " + recordada.FinLocal.ToString("HH:mm") + ".");
                }

                return recordada;
            }

            SesionActiva.Vigencia vigencia = enArchivo.Evaluar(ahora, arranque, sesionConsola);
            if (vigencia != SesionActiva.Vigencia.Vigente)
            {
                string motivo = vigencia.ToString();
                if (ultimoMotivoIgnorado != motivo)
                {
                    ultimoMotivoIgnorado = motivo;
                    Log.Info("Traspaso ignorado (" + motivo + "): " + enArchivo.Usuario + " hasta " + enArchivo.FinLocal.ToString("dd/MM HH:mm")
                        + ", sesion Windows " + enArchivo.SesionWindows + " (consola activa: " + sesionConsola + ").");
                }

                if (vigencia == SesionActiva.Vigencia.Caducada || vigencia == SesionActiva.Vigencia.DeOtroArranque)
                {
                    SesionActiva.Borrar();
                }

                return recordada;
            }

            ultimoMotivoIgnorado = null;

            if (recordada == null)
            {
                recordada = enArchivo;
                avisada = false;
                anomaliaAvisada = null;
                Log.Info("Sesion liberada: " + enArchivo.Usuario + " con el codigo " + enArchivo.Codigo + " hasta "
                    + enArchivo.FinLocal.ToString("HH:mm") + " (sesion Windows " + enArchivo.SesionWindows + ").");
                return recordada;
            }

            // Ya hay una sesion recordada en esta misma sesion de Windows. Se toma
            // el latido nuevo y la hora de salida solo puede ACORTARSE, nunca
            // alargarse, ni con el mismo codigo ni con otro. Un alumno distinto
            // llega siempre tras un cierre de sesion de Windows, y ese cierre
            // olvida la recordada (SesionCambio, o cambia el id de sesion); un
            // archivo con otro usuario/codigo sin cierre de por medio solo puede
            // ser el archivo manipulado.
            recordada.Latido = enArchivo.Latido;
            if (enArchivo.FinLocal < recordada.FinLocal)
            {
                Log.Info("La hora de salida se adelanto a " + enArchivo.FinLocal.ToString("HH:mm") + ".");
                recordada.FinLocal = enArchivo.FinLocal;
            }

            string anomalia = null;
            if (!recordada.EsLaMisma(enArchivo))
            {
                anomalia = "El traspaso cambio a " + enArchivo.Usuario + " / " + enArchivo.Codigo + " (hasta "
                    + enArchivo.FinLocal.ToString("HH:mm") + ") sin cierre de sesion de Windows de por medio";
            }
            else if (enArchivo.FinLocal > recordada.FinLocal)
            {
                anomalia = "El traspaso intenta alargar la hora de salida a " + enArchivo.FinLocal.ToString("HH:mm");
            }

            if (anomalia != null && anomalia != anomaliaAvisada)
            {
                anomaliaAvisada = anomalia;
                Log.Aviso(anomalia + "; se mantiene la sesion recordada de " + recordada.Usuario
                    + " hasta " + recordada.FinLocal.ToString("HH:mm") + ".");
            }

            return recordada;
        }

        private void Olvidar()
        {
            recordada = null;
            avisada = false;
            ultimoMotivoIgnorado = null;
            anomaliaAvisada = null;
        }

        /// <summary>
        /// Windows cerro o abrio una sesion (lo notifica el servicio; en modo
        /// consola no llega). Si es la sesion recordada, se olvida y se borra su
        /// traspaso: el siguiente alumno llega siempre por aqui.
        /// </summary>
        public void SesionCambio(int sesionWindows, string motivo)
        {
            lock (candado)
            {
                if (recordada != null && recordada.SesionWindows == sesionWindows)
                {
                    Log.Info("Sesion de Windows " + sesionWindows + " (" + motivo + "): se olvida la sesion recordada de "
                        + recordada.Usuario + " y se borra su traspaso.");
                    SesionActiva.Borrar();
                    Olvidar();
                }
            }
        }

        /// <summary>
        /// La hora de salida es el fin de la clase con la que el alumno registro: se
        /// REINICIA el equipo (pedido del 2026-11-07: "cuando acabe una clase se reinicie
        /// la pc. No por hora, debe ser por clase"); con Deep Freeze vuelve limpio para la
        /// siguiente. Si Windows no acepta el reinicio, se cierra la sesion, como antes; si
        /// tampoco, se reintenta en la siguiente revision.
        /// </summary>
        private void AlTerminarLaClase(SesionActiva sesion)
        {
            if (simular)
            {
                Log.Aviso("SIMULACION: aqui se reiniciaria el equipo (termino la clase de " + sesion.Usuario
                    + ", sesion de Windows " + sesion.SesionWindows + ").");
            }
            else
            {
                Log.Info("Hora de salida: termino la clase de " + sesion.Usuario + "; se reinicia el equipo.");
                if (!Reiniciar())
                {
                    Log.Aviso("No se pudo reiniciar: se cierra la sesion de Windows " + sesion.SesionWindows + ".");
                    if (!WTSLogoffSession(IntPtr.Zero, sesion.SesionWindows, false))
                    {
                        Log.Aviso("WTSLogoffSession fallo con el error " + Marshal.GetLastWin32Error() + "; se reintentara.");
                        return;
                    }
                }
            }

            SesionActiva.Borrar();
            Olvidar();
        }

        /// <summary>
        /// Lo que se le pasa a shutdown.exe: reiniciar (/r) ya (/t 0), cerrando los
        /// programas sin preguntar (/f; si no, uno con un documento sin guardar pararia el
        /// reinicio y el alumno lo podria cancelar), como reinicio planeado.
        /// </summary>
        internal const string ArgumentosDeReinicio = "/r /f /t 0 /d p:0:0 /c \"DigLab: termin\u00f3 la clase.\"";

        // shutdown.exe cuando ya hay un apagado o reinicio en marcha (DigLab lo pidio antes).
        private const int ErrorApagadoEnCurso = 1115;

        /// <summary>Pide el reinicio con shutdown.exe, de Windows. True si quedo en marcha.</summary>
        private static bool Reiniciar()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), ArgumentosDeReinicio)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process proceso = Process.Start(info))
                {
                    if (!proceso.WaitForExit(30000))
                    {
                        Log.Aviso("shutdown.exe no termino en 30 s.");
                        return false;
                    }

                    if (proceso.ExitCode == 0 || proceso.ExitCode == ErrorApagadoEnCurso)
                    {
                        return true;
                    }

                    Log.Aviso("shutdown.exe /r salio con el codigo " + proceso.ExitCode + ".");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Reiniciar el equipo al terminar la clase", ex);
                return false;
            }
        }

        private void EnviarAviso(SesionActiva sesion)
        {
            string titulo = "DigLab";
            string hora = sesion.FinLocal.ToString("HH:mm");
            // Lo que lee la persona va con su acento (escrito \u00f3 porque este archivo es ASCII); el log, en ASCII.
            string mensaje = "Tu sesi\u00f3n en este equipo termina a las " + hora + " y entonces el equipo se reinicia. "
                + "Guarda tu trabajo y cierra tus programas.";

            if (simular)
            {
                Log.Aviso("SIMULACION: aqui se avisaria en la sesion " + sesion.SesionWindows + " que la suya termina a las " + hora + ".");
                return;
            }

            int respuesta;
            bool enviado = WTSSendMessage(IntPtr.Zero, sesion.SesionWindows, titulo, titulo.Length * 2, mensaje, mensaje.Length * 2,
                MB_OK | MB_ICONWARNING | MB_SETFOREGROUND | MB_TOPMOST, 0, out respuesta, false);
            if (enviado)
            {
                Log.Info("Aviso enviado a la sesion " + sesion.SesionWindows + " (DigLab no estaba para avisar).");
            }
            else
            {
                Log.Aviso("WTSSendMessage fallo con el error " + Marshal.GetLastWin32Error() + ".");
            }
        }

        /// <summary>
        /// Crea %ProgramData%\DigLab (y logs) y da permiso de Modificar a Usuarios:
        /// la cuenta de laboratorio es estandar y tiene que poder escribir el
        /// traspaso. Si no se puede (consola sin admin) se registra y se sigue.
        /// </summary>
        private static void AsegurarCarpeta()
        {
            try
            {
                DirectoryInfo carpeta = Directory.CreateDirectory(SesionActiva.Carpeta);
                Directory.CreateDirectory(Path.Combine(SesionActiva.Carpeta, "logs"));

                DirectorySecurity seguridad = carpeta.GetAccessControl();
                seguridad.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                carpeta.SetAccessControl(seguridad);
            }
            catch (Exception ex)
            {
                Log.Aviso("No se pudo preparar " + SesionActiva.Carpeta + " con permiso para Usuarios: " + ex.Message);
            }
        }

        /// <summary>
        /// Para actualizar desde la propia cuenta del kiosco, el instalador le pide a Windows que
        /// no vuelva a abrir DigLab (AutoRestartShell=0) y lo devuelve al terminar. Si esa
        /// instalacion se cancela a medias, nadie lo devuelve: aqui, al arrancar, se repara.
        /// </summary>
        private void AsegurarReinicioDelShell()
        {
            if (simular)
            {
                return;
            }

            try
            {
                using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey winlogon = equipo.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                {
                    if (winlogon != null && Convert.ToString(winlogon.GetValue("AutoRestartShell")) == "0")
                    {
                        winlogon.SetValue("AutoRestartShell", 1, RegistryValueKind.DWord);
                        Log.Aviso("AutoRestartShell estaba apagado (una actualizacion que no termino): se vuelve a encender.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Aviso("No se pudo revisar AutoRestartShell: " + ex.Message);
            }
        }

        // --- Windows ------------------------------------------------------------

        private const int MB_OK = 0x00000000;
        private const int MB_ICONWARNING = 0x00000030;
        private const int MB_SETFOREGROUND = 0x00010000;
        private const int MB_TOPMOST = 0x00040000;

        [DllImport("kernel32.dll")]
        private static extern int WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSLogoffSession(IntPtr hServer, int sessionId, bool bWait);

        [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WTSSendMessage(IntPtr hServer, int sessionId, string pTitle, int titleLength,
            string pMessage, int messageLength, int style, int timeout, out int pResponse, bool bWait);

        private static int SesionConsolaActiva()
        {
            return WTSGetActiveConsoleSessionId();
        }
    }
}
