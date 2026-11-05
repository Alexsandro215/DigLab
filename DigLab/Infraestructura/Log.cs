using System;
using System.IO;
using System.Text;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// Registro a archivo, sin dependencias externas. Un archivo por dia en la
    /// carpeta de datos (CarpetaDatos); sin configurar, %LOCALAPPDATA%\DigLab\logs,
    /// que un usuario estandar (la cuenta de laboratorio) siempre puede escribir.
    /// El vigilante la redirige con Configurar, porque corre sin perfil de usuario.
    /// OJO: en un equipo con Deep Freeze, sin carpeta de datos configurada el log
    /// se borra en cada reinicio y no queda nada que mirar al dia siguiente. Los Console.WriteLine
    /// repartidos por el codigo se pierden en un WinExe; esto es lo que queda
    /// para diagnosticar un equipo en el que la app corre como shell.
    ///
    /// Este archivo se compila tambien en DigLab.Vigilante (enlazado): no puede
    /// depender de WinForms ni de nada mas del proyecto.
    /// </summary>
    internal static class Log
    {
        private static readonly object candado = new object();

        private static string carpeta = CarpetaPredeterminada();

        /// <summary>
        /// %LOCALAPPDATA%\DigLab\logs, salvo que el equipo tenga configurada una
        /// carpeta de datos persistente (Deep Freeze borra el perfil al reiniciar).
        /// Si algo falla al resolverla se escribe en el temporal: quedarse sin log
        /// es malo, pero no arrancar es peor.
        /// </summary>
        private static string CarpetaPredeterminada()
        {
            try
            {
                string perfil = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DigLab");
                return Path.Combine(CarpetaDatos.Resolver(perfil), "logs");
            }
            catch (Exception)
            {
                return Path.Combine(Path.GetTempPath(), "DigLab", "logs");
            }
        }

        private static string prefijo = "diglab";

        public static string Carpeta
        {
            get { lock (candado) { return carpeta; } }
        }

        public static string ArchivoDeHoy
        {
            get { lock (candado) { return Path.Combine(carpeta, prefijo + "-" + DateTime.Now.ToString("yyyyMMdd") + ".log"); } }
        }

        /// <summary>Cambia la carpeta y el prefijo de los archivos (el vigilante escribe en ProgramData).</summary>
        public static void Configurar(string carpetaDestino, string prefijoArchivo)
        {
            lock (candado)
            {
                carpeta = carpetaDestino;
                prefijo = prefijoArchivo;
            }
        }

        public static void Info(string mensaje)
        {
            Escribir("INFO ", mensaje);
        }

        public static void Aviso(string mensaje)
        {
            Escribir("AVISO", mensaje);
        }

        public static void Error(string contexto, Exception ex)
        {
            Escribir("ERROR", contexto + Environment.NewLine + Describir(ex));
        }

        private static string Describir(Exception ex)
        {
            if (ex == null)
            {
                return "(sin excepcion)";
            }

            // ToString() ya incluye tipo, mensaje, pila e InnerException encadenadas.
            return ex.ToString();
        }

        private static void Escribir(string nivel, string texto)
        {
            string linea = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + nivel + "] " + texto + Environment.NewLine;

            lock (candado)
            {
                try
                {
                    Directory.CreateDirectory(carpeta);
                    File.AppendAllText(ArchivoDeHoy, linea, Encoding.UTF8);
                }
                catch
                {
                    // Un fallo al escribir el log nunca debe tumbar la aplicacion.
                }
            }
        }
    }
}
