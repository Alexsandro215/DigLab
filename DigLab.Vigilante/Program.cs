using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using DigLab.Infraestructura;

namespace DigLab.Vigilante
{
    /// <summary>
    /// Sin argumentos: corre como servicio de Windows (instalado con
    /// deploy\instalar_vigilante.ps1).
    ///   --consola              el mismo bucle en primer plano, para probar sin instalar
    ///   --consola --simular    ademas, nunca cierra sesion ni avisa: solo lo registra
    ///   --segundos N           (con --consola) termina solo pasados N segundos
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool consola = false;
            bool simular = false;
            int segundos = 0;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--consola", StringComparison.OrdinalIgnoreCase))
                {
                    consola = true;
                }
                else if (string.Equals(arg, "--simular", StringComparison.OrdinalIgnoreCase))
                {
                    simular = true;
                }
                else if (string.Equals(arg, "--segundos", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int.TryParse(args[++i], out segundos);
                }
                else
                {
                    Console.Error.WriteLine("Argumento no reconocido: " + arg);
                    Console.Error.WriteLine("Uso: DigLab.Vigilante.exe [--consola [--simular] [--segundos N]]");
                    return 2;
                }
            }

            // SesionActiva.Carpeta (%ProgramData%\DigLab) es el respaldo: el servicio
            // corre como LocalSystem y no tiene perfil. Si el equipo tiene carpeta de
            // datos configurada, el log va ahi y sobrevive a Deep Freeze.
            Log.Configurar(Path.Combine(CarpetaDatos.Resolver(SesionActiva.Carpeta), "logs"), "vigilante");
            Log.Info(CarpetaDatos.Diagnostico);

            if (!consola)
            {
                ServiceBase.Run(new VigilanteService());
                return 0;
            }

            using (Vigilante vigilante = new Vigilante(simular))
            {
                Console.WriteLine("DigLab.Vigilante en consola" + (simular ? " (SIMULACION: no cierra sesion ni avisa)" : "") + ".");
                Console.WriteLine("Traspaso: " + SesionActiva.Ruta);
                Console.WriteLine("Log:      " + Log.ArchivoDeHoy);
                vigilante.Iniciar();

                if (segundos > 0)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(segundos));
                }
                else
                {
                    Console.WriteLine("Ctrl+C para salir.");
                    using (ManualResetEvent fin = new ManualResetEvent(false))
                    {
                        Console.CancelKeyPress += (s, e) =>
                        {
                            e.Cancel = true;
                            fin.Set();
                        };
                        fin.WaitOne();
                    }
                }

                vigilante.Detener();
            }

            return 0;
        }
    }
}
