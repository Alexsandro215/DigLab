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

        // La version nueva publicada en la base (db/29, BuscarVersion): a los 2 minutos de
        // arrancar y cada 10; la descarga puede tardar con una red lenta.
        private static readonly TimeSpan PrimeraBusqueda = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan PeriodoBusqueda = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan EsperaBusqueda = TimeSpan.FromMinutes(15);
        // Si una version no quedo instalada, no se reintenta antes de esto: un instalador que
        // falla no debe dejar al equipo cerrando la sesion cada 10 minutos.
        private static readonly TimeSpan EntreIntentos = TimeSpan.FromHours(6);
        private const string ArgumentoBuscar = "--buscar-actualizacion";
        private const int CodigoLista = 10;

        // Las ordenes de la administracion (apagar, OrdenesEquiposDatos de DigLab): cada minuto,
        // desde poco despues de arrancar. Con quien usa el equipo, Windows avisa 2 minutos antes.
        private static readonly TimeSpan PrimeraOrden = TimeSpan.FromSeconds(75);
        private static readonly TimeSpan PeriodoOrdenes = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan EsperaOrdenes = TimeSpan.FromSeconds(40);
        private const string ArgumentoOrdenes = "--revisar-ordenes";
        private const int CodigoApagar = 20;
        private const int SegundosAvisoEnUso = 120;
        private const int SegundosAvisoLibre = 15;

        private readonly bool simular;
        private readonly object candado = new object();
        private Timer temporizador;
        private Timer temporizadorCopia;
        private Timer temporizadorVersion;
        private Timer temporizadorOrdenes;
        private int copiando;
        private int buscando;
        private int revisandoOrdenes;
        private string ultimaCopia;
        private string ultimaBusqueda;
        private string ultimaOrden;

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
            temporizadorVersion = new Timer(estado => BuscarVersion(), null, PrimeraBusqueda, Timeout.InfiniteTimeSpan);
            temporizadorOrdenes = new Timer(estado => RevisarOrdenes(), null, PrimeraOrden, Timeout.InfiniteTimeSpan);
        }

        public void Detener()
        {
            if (temporizadorOrdenes != null)
            {
                temporizadorOrdenes.Dispose();
                temporizadorOrdenes = null;
            }

            if (temporizadorVersion != null)
            {
                temporizadorVersion.Dispose();
                temporizadorVersion = null;
            }

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
            string dicho;
            string resultado = LanzarDigLab(exe, "--memorias-emergencia", espera, out codigo, out dicho);
            return resultado;
        }

        /// <summary>
        /// Lanza DigLab.exe con ese argumento, sin ventana, y espera hasta "espera". Devuelve lo
        /// que dijo con su codigo para el log; en dicho, solo lo que dijo; en codigo, su codigo de
        /// salida (-1 si no termino o no esta).
        /// </summary>
        internal static string LanzarDigLab(string exe, string argumento, TimeSpan espera, out int codigo, out string dicho)
        {
            codigo = -1;
            dicho = "";
            if (!File.Exists(exe))
            {
                return "no esta " + exe + ".";
            }

            ProcessStartInfo info = new ProcessStartInfo(exe, argumento)
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
                dicho = texto;
                return (texto.Length > 0 ? texto : "sin mensaje") + " (codigo " + codigo + ")";
            }
        }

        // --- Version nueva de DigLab (db/29) ---------------------------------------

        /// <summary>
        /// Si la administracion publico una version nueva en la base (db/29), la instala en
        /// silencio cuando nadie usa el equipo. DigLab.exe --buscar-actualizacion (como SYSTEM)
        /// la deja descargada y comprobada en C:\DigLab\Actualizacion, donde un estudiante no
        /// puede escribir; aqui se vuelve a comprobar su huella y se corre el instalador, que
        /// al terminar reinicia el equipo para que vuelva a ser kiosco.
        /// </summary>
        internal void BuscarVersion()
        {
            if (Interlocked.Exchange(ref buscando, 1) == 1)
            {
                return;
            }

            bool instalando = false;
            try
            {
                if (simular)
                {
                    AnotarVersion("SIMULACION: no se busca una version nueva de DigLab.", true);
                    return;
                }

                int codigo;
                string dicho;
                string resultado = LanzarDigLab(RutaDigLab(), ArgumentoBuscar, EsperaBusqueda, out codigo, out dicho);
                if (codigo != CodigoLista)
                {
                    AnotarVersion("Version de DigLab: " + resultado, codigo == 0 || codigo == 3);
                    return;
                }

                // "LISTA version huella ruta": la ruta al final, por si lleva espacios.
                string[] partes = dicho.Split(new[] { ' ' }, 4);
                if (partes.Length < 4 || partes[0] != "LISTA")
                {
                    AnotarVersion("Version de DigLab: respuesta que no se entiende: " + dicho, false);
                    return;
                }

                string version = partes[1];
                string huella = partes[2].ToLowerInvariant();
                string ruta = partes[3].Trim();

                string porQue;
                if (!EquipoLibre(out porQue))
                {
                    AnotarVersion("DigLab " + version + " esta lista; se instalara cuando nadie use el equipo (" + porQue + ").", true);
                    return;
                }

                if (YaSeIntento(version))
                {
                    AnotarVersion("DigLab " + version + " ya se intento instalar hace menos de " + (int)EntreIntentos.TotalHours
                        + " horas y no quedo; se reintentara despues.", false);
                    return;
                }

                if (!File.Exists(ruta) || HuellaDe(ruta) != huella)
                {
                    AnotarVersion("El instalador de DigLab " + version + " no coincide con su huella: no se instala.", false);
                    return;
                }

                MarcarIntento(version);
                string registro = Path.Combine(SesionActiva.Carpeta, "logs", "actualizacion-" + version + "-"
                    + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".log");
                Log.Info("Se instala DigLab " + version + " en silencio (" + ruta + "); al terminar, el equipo se reinicia. Registro: " + registro);
                ProcessStartInfo info = new ProcessStartInfo(ruta, "/VERYSILENT /SUPPRESSMSGBOXES /LOG=\"" + registro + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(ruta) ?? ""
                };
                using (Process.Start(info))
                {
                    // No se espera: el instalador detiene este servicio para reemplazarlo.
                }

                instalando = true;
            }
            catch (Exception ex)
            {
                AnotarVersion("Version de DigLab: no se pudo buscar o instalar (" + ex.Message + ").", false);
            }
            finally
            {
                Interlocked.Exchange(ref buscando, 0);
                try
                {
                    Timer version = temporizadorVersion;
                    if (version != null && !instalando)
                    {
                        version.Change(PeriodoBusqueda, Timeout.InfiniteTimeSpan);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Se detuvo el vigilante mientras tanto.
                }
            }
        }

        /// <summary>
        /// "Actualizar a la ..." del inicio de sesion del kiosco (Actualizador.PedirInstalarAhora):
        /// se busca e instala ya, sin esperar los 10 minutos. Vale tambien para reintentar una
        /// version que no quedo: lo pidio una persona frente al equipo.
        /// </summary>
        private void AtenderPedidoDeActualizar()
        {
            string pedido = Path.Combine(SesionActiva.Carpeta, ArchivoPedido);
            if (simular || !File.Exists(pedido))
            {
                return;
            }

            try
            {
                File.Delete(pedido);
            }
            catch (Exception ex)
            {
                Log.Aviso("No se pudo borrar el pedido de actualizar: " + ex.Message);
                return;
            }

            Log.Info("Desde el kiosco pidieron instalar ya la version nueva de DigLab.");
            OlvidarIntento();
            ultimaBusqueda = null;
            Task.Run(() => BuscarVersion());
        }

        private const string ArchivoPedido = "actualizar_ahora.txt";

        private static void OlvidarIntento()
        {
            try
            {
                using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey clave = equipo.OpenSubKey(ClaveDigLab, true))
                {
                    if (clave != null)
                    {
                        clave.DeleteValue(ValorIntento, false);
                    }
                }
            }
            catch (Exception)
            {
                // Si no se puede, vale la espera de siempre.
            }
        }

        private void AnotarVersion(string texto, bool bien)
        {
            if (texto == ultimaBusqueda)
            {
                return;
            }

            ultimaBusqueda = texto;
            if (bien)
            {
                Log.Info(texto);
            }
            else
            {
                Log.Aviso(texto);
            }
        }

        /// <summary>
        /// Nadie usa el equipo: ningun estudiante con su sesion liberada (la hora de salida que
        /// recuerda el vigilante) y sin escritorio abierto en la consola (explorer.exe corre
        /// solo tras la bitacora o el acceso de emergencia; en el inicio de sesion del kiosco,
        /// la pantalla es DigLab).
        /// </summary>
        private bool EquipoLibre(out string porQue)
        {
            lock (candado)
            {
                if (recordada != null)
                {
                    porQue = "lo usa " + recordada.Usuario + " hasta las " + recordada.FinLocal.ToString("HH:mm");
                    return false;
                }
            }

            int consola = SesionConsolaActiva();
            foreach (Process explorador in Process.GetProcessesByName("explorer"))
            {
                using (explorador)
                {
                    if (explorador.SessionId == consola)
                    {
                        porQue = "hay un escritorio abierto";
                        return false;
                    }
                }
            }

            porQue = "";
            return true;
        }

        // --- Apagar desde la administracion (ordenes_equipos, db/29) ----------------

        /// <summary>
        /// Cada minuto: DigLab.exe --revisar-ordenes (como SYSTEM) dice si la administracion
        /// mando apagar los equipos de este laboratorio mientras este ya estaba encendido. Si
        /// si, se apaga con shutdown.exe y el aviso de Windows: 2 minutos si alguien usa el
        /// equipo (para que guarde su trabajo), unos segundos si esta libre. Cada orden se
        /// cumple una vez (HKLM\SOFTWARE\DigLab\OrdenCumplida).
        /// </summary>
        internal void RevisarOrdenes()
        {
            if (Interlocked.Exchange(ref revisandoOrdenes, 1) == 1)
            {
                return;
            }

            bool sinTabla = false;
            try
            {
                int codigo;
                string dicho;
                string resultado = LanzarDigLab(RutaDigLab(), ArgumentoOrdenes, EsperaOrdenes, out codigo, out dicho);
                // La base sin db/29 (codigo 3): se vuelve a mirar en 10 minutos, no cada minuto.
                sinTabla = codigo == 3;
                if (codigo != CodigoApagar)
                {
                    // Al log solo cuando cambia: cada minuto, "Sin ordenes" lo llenaria.
                    AnotarOrden("Ordenes de la administracion: " + resultado, codigo == 0 || codigo == 3);
                    return;
                }

                string[] partes = dicho.Split(' ');
                if (partes.Length != 2 || partes[0] != "APAGAR")
                {
                    AnotarOrden("Ordenes de la administracion: respuesta que no se entiende: " + dicho, false);
                    return;
                }

                string orden = partes[1];
                if (OrdenYaCumplida(orden))
                {
                    return;
                }

                string porQue;
                bool libre = EquipoLibre(out porQue);
                int segundos = libre ? SegundosAvisoLibre : SegundosAvisoEnUso;
                if (simular)
                {
                    Log.Aviso("SIMULACION: aqui se apagaria el equipo (orden " + orden + " de la administracion, en " + segundos + " s"
                        + (libre ? "" : "; " + porQue) + ").");
                    MarcarOrdenCumplida(orden);
                    return;
                }

                Log.Info("La administracion mando apagar los equipos (orden " + orden + "): se apaga en " + segundos + " s"
                    + (libre ? "." : " (" + porQue + "; Windows le avisa)."));
                MarcarOrdenCumplida(orden);
                if (!Apagar(segundos))
                {
                    // Que se reintente en la siguiente vuelta.
                    OlvidarOrdenCumplida();
                }
            }
            catch (Exception ex)
            {
                AnotarOrden("Ordenes de la administracion: no se pudieron revisar (" + ex.Message + ").", false);
            }
            finally
            {
                Interlocked.Exchange(ref revisandoOrdenes, 0);
                try
                {
                    Timer ordenes = temporizadorOrdenes;
                    if (ordenes != null)
                    {
                        ordenes.Change(sinTabla ? PeriodoBusqueda : PeriodoOrdenes, Timeout.InfiniteTimeSpan);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Se detuvo el vigilante mientras tanto.
                }
            }
        }

        /// <summary>
        /// Lo que se le pasa a shutdown.exe: apagar (/s) en esos segundos, cerrando los
        /// programas (/f), como apagado planeado y con el porque, que Windows ensena a quien
        /// usa el equipo. El texto va con sus acentos (escritos ó: este archivo es ASCII).
        /// </summary>
        internal static string ArgumentosDeApagado(int segundos)
        {
            string mensaje = segundos >= 60
                ? "La administración apaga los equipos del laboratorio. Guarda tu trabajo: este equipo se apaga en "
                  + (segundos / 60) + " minutos."
                : "La administración apaga los equipos del laboratorio.";
            return "/s /f /t " + segundos + " /d p:0:0 /c \"DigLab: " + mensaje + "\"";
        }

        private static bool Apagar(int segundos)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), ArgumentosDeApagado(segundos))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process proceso = Process.Start(info))
                {
                    if (!proceso.WaitForExit(30000))
                    {
                        Log.Aviso("shutdown.exe /s no termino en 30 s.");
                        return false;
                    }

                    if (proceso.ExitCode == 0 || proceso.ExitCode == ErrorApagadoEnCurso)
                    {
                        return true;
                    }

                    Log.Aviso("shutdown.exe /s salio con el codigo " + proceso.ExitCode + ".");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Apagar el equipo por orden de la administracion", ex);
                return false;
            }
        }

        private void AnotarOrden(string texto, bool bien)
        {
            if (texto == ultimaOrden)
            {
                return;
            }

            ultimaOrden = texto;
            if (bien)
            {
                Log.Info(texto);
            }
            else
            {
                Log.Aviso(texto);
            }
        }

        private const string ValorOrden = "OrdenCumplida";

        private static bool OrdenYaCumplida(string orden)
        {
            try
            {
                using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey clave = equipo.OpenSubKey(ClaveDigLab))
                {
                    return clave != null && Convert.ToString(clave.GetValue(ValorOrden)) == orden;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void MarcarOrdenCumplida(string orden)
        {
            try
            {
                using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey clave = equipo.CreateSubKey(ClaveDigLab))
                {
                    clave.SetValue(ValorOrden, orden, RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                Log.Aviso("No se pudo anotar la orden " + orden + " como cumplida: " + ex.Message);
            }
        }

        private static void OlvidarOrdenCumplida()
        {
            try
            {
                using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey clave = equipo.OpenSubKey(ClaveDigLab, true))
                {
                    if (clave != null)
                    {
                        clave.DeleteValue(ValorOrden, false);
                    }
                }
            }
            catch (Exception)
            {
                // Se intentara con la marca puesta: la orden caduca sola.
            }
        }

        private const string ClaveDigLab = @"SOFTWARE\DigLab";
        private const string ValorIntento = "ActualizacionIntentada";

        private static bool YaSeIntento(string version)
        {
            try
            {
                using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey clave = equipo.OpenSubKey(ClaveDigLab))
                {
                    string[] valor = Convert.ToString(clave == null ? null : clave.GetValue(ValorIntento)).Split('|');
                    long marca;
                    return valor.Length == 2 && valor[0] == version && long.TryParse(valor[1], out marca)
                        && DateTime.UtcNow - new DateTime(marca, DateTimeKind.Utc) < EntreIntentos;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void MarcarIntento(string version)
        {
            try
            {
                using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey clave = equipo.CreateSubKey(ClaveDigLab))
                {
                    clave.SetValue(ValorIntento, version + "|" + DateTime.UtcNow.Ticks, RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                Log.Aviso("No se pudo anotar el intento de instalar DigLab " + version + ": " + ex.Message);
            }
        }

        private static string HuellaDe(string ruta)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream archivo = File.OpenRead(ruta))
            {
                StringBuilder texto = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(archivo))
                {
                    texto.Append(b.ToString("x2"));
                }

                return texto.ToString();
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
            AtenderPedidoDeActualizar();
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
