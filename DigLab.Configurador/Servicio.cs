using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace DigLab.Configurador
{
    /// <summary>
    /// El servicio DigLab.Vigilante: corre como LocalSystem y cierra la sesion de Windows
    /// cuando vence el codigo de acceso. Se consulta con ServiceController, que no depende
    /// del idioma de Windows (la salida de sc.exe esta traducida).
    /// </summary>
    internal static class Servicio
    {
        public const string Nombre = "DigLabVigilante";

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSLogoffSession(IntPtr servidor, int sesion, [MarshalAs(UnmanagedType.Bool)] bool esperar);

        private static readonly string[] RamasModoSeguro =
        {
            @"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\" + Nombre,
            @"SYSTEM\CurrentControlSet\Control\SafeBoot\Network\" + Nombre
        };

        private const int EsperaParaVerSiRevive = 3000;
        private const int EsperaParaSoltarRegistro = 6000;

        /// <summary>Si DetenerTodo le pidio a Windows no reabrir DigLab: al desinstalar se devuelve ahi mismo.</summary>
        public static bool ApagoReinicioDelShell { get; private set; }

        public static bool Existe()
        {
            return ServiceController.GetServices().Any(s => string.Equals(s.ServiceName, Nombre, StringComparison.OrdinalIgnoreCase));
        }

        public static bool Corriendo()
        {
            if (!Existe())
            {
                return false;
            }

            using (ServiceController servicio = new ServiceController(Nombre))
            {
                return servicio.Status == ServiceControllerStatus.Running;
            }
        }

        public static bool EnModoSeguro()
        {
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                foreach (string rama in RamasModoSeguro)
                {
                    using (RegistryKey clave = equipo.OpenSubKey(rama))
                    {
                        if (clave == null)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Detiene DigLab y el vigilante para poder copiar encima. Si el servicio acaba de
        /// arrancar esta en StartPending y detenerlo falla por tiempo aunque acabe parandose
        /// solo: sin reintento, eso abortaba el despliegue entero por nada.
        ///
        /// Matar el proceso NO basta en un kiosco: DigLab es el shell de la cuenta del
        /// laboratorio y Winlogon (AutoRestartShell=1) lo relanza en cuanto muere, y el
        /// archivo vuelve a quedar bloqueado antes de copiarlo. Hay que cerrar esa sesion de
        /// Windows entera. Solo ahi: en otro equipo, cerrarle la sesion a quien tiene DigLab
        /// abierto le haria perder lo demas que tenga, y con cerrar DigLab ya basta.
        /// </summary>
        public static void DetenerTodo(string destino)
        {
            if (string.Equals(Archivos.LeerAjuste(Archivos.ConfigDigLab(destino), "ModoKiosco"), "true", StringComparison.OrdinalIgnoreCase))
            {
                CerrarSesionesConDigLab();

                // Con el kiosco en la cuenta con la que se instala (desde la llave de emergencia
                // o tras una bitacora) su sesion no se puede cerrar: se cerraria el instalador.
                if (SesionesConDigLab().Contains(SesionMia()))
                {
                    Salida.Escribir("INFO", "DigLab es la pantalla de esta misma sesión: Windows deja de reabrirlo mientras se instala.");
                    Kiosco.ReinicioDelShell(false);
                    ApagoReinicioDelShell = true;
                }
            }

            if (Existe())
            {
                using (ServiceController servicio = new ServiceController(Nombre))
                {
                    for (int intento = 1; intento <= 3 && servicio.Status != ServiceControllerStatus.Stopped; intento++)
                    {
                        try
                        {
                            if (servicio.CanStop)
                            {
                                servicio.Stop();
                            }

                            servicio.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                        }
                        catch (Exception)
                        {
                            Thread.Sleep(3000);
                        }

                        servicio.Refresh();
                    }

                    if (servicio.Status != ServiceControllerStatus.Stopped)
                    {
                        throw new FalloDeInstalacion("No se pudo detener el servicio " + Nombre + " (sigue en " + servicio.Status + "). Reinicia el equipo y vuelve a instalar.");
                    }
                }
            }

            if (!Matar())
            {
                return;
            }

            // Si resucito es que era el shell de alguna sesion, aunque el .config no dijera
            // kiosco (por ejemplo, una instalacion que se quedo a medias).
            Thread.Sleep(EsperaParaVerSiRevive);
            if (SesionesConDigLab().Count > 0)
            {
                CerrarSesionesConDigLab();
                Matar();
                Thread.Sleep(EsperaParaVerSiRevive);
                if (SesionesConDigLab().Count > 0)
                {
                    throw new FalloDeInstalacion("DigLab vuelve a arrancar solo, así que su archivo sigue en uso: es el programa de inicio de una sesión de Windows. Reinicia manteniendo Mayús pulsada, entra con tu cuenta y vuelve a instalar antes de que entre la cuenta del laboratorio.");
                }
            }
        }

        /// <summary>True si habia algo que cerrar.</summary>
        private static bool Matar()
        {
            bool habia = false;
            foreach (string nombre in new[] { "DigLab", "DigLab.Vigilante" })
            {
                foreach (Process proceso in Process.GetProcessesByName(nombre))
                {
                    using (proceso)
                    {
                        habia = true;
                        try
                        {
                            proceso.Kill();
                            proceso.WaitForExit(5000);
                        }
                        catch (Exception)
                        {
                            // Ya termino, o lo dira el Setup al no poder copiar.
                        }
                    }
                }
            }

            return habia;
        }

        private static int SesionMia()
        {
            using (Process yo = Process.GetCurrentProcess())
            {
                return yo.SessionId;
            }
        }

        /// <summary>Las sesiones de Windows con DigLab corriendo. La de quien instala cuenta.</summary>
        private static List<int> SesionesConDigLab()
        {
            List<int> sesiones = new List<int>();
            foreach (Process proceso in Process.GetProcessesByName("DigLab"))
            {
                using (proceso)
                {
                    if (!sesiones.Contains(proceso.SessionId))
                    {
                        sesiones.Add(proceso.SessionId);
                    }
                }
            }

            return sesiones;
        }

        /// <summary>
        /// Cierra las sesiones de OTROS usuarios donde corre DigLab. Con quien instala ya en su
        /// escritorio, Windows no vuelve a entrar solo con la cuenta del laboratorio.
        /// </summary>
        private static void CerrarSesionesConDigLab()
        {
            int mia = SesionMia();
            List<int> ajenas = SesionesConDigLab().Where(s => s != mia).ToList();
            if (ajenas.Count == 0)
            {
                return;
            }

            foreach (int sesion in ajenas)
            {
                Salida.Escribir("INFO", "Se cierra la sesión de Windows " + sesion + ", donde DigLab es la pantalla.");
                if (!WTSLogoffSession(IntPtr.Zero, sesion, false))
                {
                    Salida.Aviso("Windows no cerró la sesión " + sesion + " (código " + Marshal.GetLastWin32Error() + ").");
                }
            }

            for (int espera = 0; espera < 30 && SesionesConDigLab().Any(ajenas.Contains); espera++)
            {
                Thread.Sleep(1000);
            }

            // Muerto el proceso, Windows aun tarda en soltar el registro de esa cuenta.
            Thread.Sleep(EsperaParaSoltarRegistro);
        }

        /// <summary>Solo desarrollo: que la llamada a Windows exista, con una sesion que no existe.</summary>
        public static void ProbarLlamadaDeCierre()
        {
            const int NoExiste = 65000;
            bool cerro = WTSLogoffSession(IntPtr.Zero, NoExiste, false);
            Salida.Escribir(cerro ? "MAL" : "BIEN", "la llamada para cerrar sesiones existe; con una sesión inexistente responde error " + Marshal.GetLastWin32Error());
        }

        public static void Instalar(string rutaExe)
        {
            if (!File.Exists(rutaExe))
            {
                throw new FalloDeInstalacion("No se encontró " + rutaExe + ".");
            }

            Quitar();
            Sistema.ExigirCero("sc.exe", "create " + Nombre + " binPath= \"\\\"" + rutaExe + "\\\"\" start= auto DisplayName= \"DigLab Vigilante\" obj= LocalSystem");
            Sistema.Ejecutar("sc.exe", "description " + Nombre + " \"Cierra la sesion de Windows de quien usa el equipo cuando vence su codigo de acceso de DigLab.\"");
            Sistema.Ejecutar("sc.exe", "failure " + Nombre + " reset= 86400 actions= restart/5000/restart/5000/restart/5000");
            Arrancar();

            // El modo seguro arranca sin los servicios que no esten en su lista: Mayus +
            // Reiniciar dejaria un simbolo del sistema SIN vigilante.
            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                foreach (string rama in RamasModoSeguro)
                {
                    using (RegistryKey clave = equipo.CreateSubKey(rama))
                    {
                        clave.SetValue("", "Service", RegistryValueKind.String);
                    }
                }
            }
        }

        public static void Arrancar()
        {
            using (ServiceController servicio = new ServiceController(Nombre))
            {
                try
                {
                    if (servicio.Status != ServiceControllerStatus.Running)
                    {
                        servicio.Start();
                        servicio.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    }
                }
                catch (Exception ex)
                {
                    throw new FalloDeInstalacion("El servicio " + Nombre + " no arranca: " + ex.Message + " Mira " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DigLab", "logs") + ".");
                }
            }
        }

        /// <summary>
        /// Lo quita, y comprueba que de verdad se fue: si algo lo tiene abierto (services.msc,
        /// el Visor de eventos), Windows lo deja "marcado para eliminacion" y sigue ahi.
        /// </summary>
        public static void Quitar()
        {
            if (Existe())
            {
                using (ServiceController servicio = new ServiceController(Nombre))
                {
                    try
                    {
                        if (servicio.Status != ServiceControllerStatus.Stopped && servicio.CanStop)
                        {
                            servicio.Stop();
                            servicio.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                        }
                    }
                    catch (Exception)
                    {
                        // Se borra igual; abajo se comprueba si se fue.
                    }
                }

                Sistema.Ejecutar("sc.exe", "delete " + Nombre);
                for (int espera = 0; espera < 10 && Existe(); espera++)
                {
                    Thread.Sleep(1000);
                }

                if (Existe())
                {
                    throw new FalloDeInstalacion(Nombre + " sigue marcado para eliminación: cierra Servicios y el Visor de eventos, reinicia y vuelve a instalar.");
                }
            }

            using (RegistryKey equipo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                foreach (string rama in RamasModoSeguro)
                {
                    equipo.DeleteSubKeyTree(rama, false);
                }
            }
        }
    }
}
