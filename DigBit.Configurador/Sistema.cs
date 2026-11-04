using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace DigBit.Configurador
{
    /// <summary>
    /// Lo comun: simular o hacer de verdad, y lanzar las pocas herramientas de Windows
    /// que no tienen una API razonable (reg, sc, powercfg), que estan en todos los
    /// equipos porque vienen con el sistema. Nada de PowerShell: en algunas PCs esta
    /// bloqueado o limitado.
    ///
    /// DOS seguros contra configurar por accidente la PC en la que se desarrolla:
    /// solo escribe si el proceso tiene la marca que pone el Setup (VariableSetup) y
    /// si corre como administrador. Sin la marca, todo se simula.
    /// </summary>
    internal static class Sistema
    {
        /// <summary>La pone el Setup de verdad, nunca una prueba.</summary>
        public const string VariableSetup = "DIGBIT_SETUP";

        public const string CarpetaPorOmision = @"C:\DigBit";

        /// <summary>Solo decir lo que se haria.</summary>
        public static bool Simular { get; private set; }

        public static void Decidir(bool pidioSimular)
        {
            Simular = pidioSimular || Environment.GetEnvironmentVariable(VariableSetup) != "1";
        }

        public static void ExigirPermiso()
        {
            if (Simular)
            {
                return;
            }

            using (WindowsIdentity identidad = WindowsIdentity.GetCurrent())
            {
                if (!new WindowsPrincipal(identidad).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    throw new FalloDeInstalacion("El configurador tiene que correr como administrador.");
                }
            }
        }

        /// <summary>Hace la accion, o solo la cuenta si se esta simulando.</summary>
        public static void Hacer(string descripcion, Action accion)
        {
            if (Simular)
            {
                Salida.Escribir("SIMULADO", descripcion);
                return;
            }

            accion();
            Salida.Hecho(descripcion);
        }

        /// <summary>Una herramienta de Windows, sin ventana. Devuelve su codigo de salida.</summary>
        public static int Ejecutar(string programa, string argumentos)
        {
            ProcessStartInfo inicio = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, programa), argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.SystemDirectory
            };
            using (Process proceso = Process.Start(inicio))
            {
                proceso.OutputDataReceived += (s, e) => { };
                proceso.ErrorDataReceived += (s, e) => { };
                proceso.BeginOutputReadLine();
                proceso.BeginErrorReadLine();
                if (!proceso.WaitForExit(120000))
                {
                    try { proceso.Kill(); } catch (Exception) { }
                    throw new FalloDeInstalacion(programa + " no termino en dos minutos.");
                }

                return proceso.ExitCode;
            }
        }

        public static void ExigirCero(string programa, string argumentos)
        {
            int codigo = Ejecutar(programa, argumentos);
            if (codigo != 0)
            {
                throw new FalloDeInstalacion(programa + " termino con codigo " + codigo + ".");
            }
        }
    }
}
