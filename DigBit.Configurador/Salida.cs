using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DigBit.Configurador
{
    /// <summary>
    /// Lo que el configurador le cuenta al Setup: un renglon por linea en el archivo
    /// de --salida (UTF-8 con BOM, que es como lo lee Inno Setup), con una palabra al
    /// principio: PASO, HECHO, AVISO, ERROR, ESTADO, LAB... Al escribir de verdad,
    /// tambien al log de instalacion. Las contrasenas nunca salen: lo que venga de una
    /// excepcion podria traerlas (Secretos).
    /// </summary>
    internal static class Salida
    {
        private static readonly Encoding ConBom = new UTF8Encoding(true);
        private static readonly List<string> secretos = new List<string>();
        private static string archivo;
        private static string log;

        public static void Preparar(string rutaSalida, bool conLog)
        {
            archivo = rutaSalida;
            if (conLog)
            {
                try
                {
                    string carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DigBit", "logs");
                    Directory.CreateDirectory(carpeta);
                    log = Path.Combine(carpeta, "instalacion-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                }
                catch (Exception)
                {
                    // Sin log se instala igual: el Setup ve la salida.
                    log = null;
                }
            }
        }

        /// <summary>Un valor que nunca debe salir escrito (la contrasena de la base o la de la cuenta).</summary>
        public static void Secreto(string valor)
        {
            if (!string.IsNullOrEmpty(valor))
            {
                secretos.Add(valor);
            }
        }

        public static void Escribir(string palabra, string texto)
        {
            string limpio = texto ?? "";
            foreach (string secreto in secretos)
            {
                limpio = limpio.Replace(secreto, "***");
            }

            // Un solo renglon: el Setup lee por lineas.
            string linea = palabra + " " + limpio.Replace("\r", " ").Replace("\n", " ");
            if (archivo != null)
            {
                File.AppendAllText(archivo, linea + "\r\n", ConBom);
            }

            if (log != null)
            {
                try
                {
                    File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss") + " " + linea + "\r\n", ConBom);
                }
                catch (Exception)
                {
                    // El log no es motivo para parar una instalacion.
                }
            }
        }

        public static void Paso(string texto) { Escribir("PASO", texto); }

        public static void Hecho(string texto) { Escribir("HECHO", texto); }

        public static void Aviso(string texto) { Escribir("AVISO", texto); }

        public static void Error(string texto) { Escribir("ERROR", texto); }
    }

    /// <summary>Un fallo con un texto pensado para quien instala (no una excepcion cualquiera).</summary>
    internal sealed class FalloDeInstalacion : Exception
    {
        public FalloDeInstalacion(string mensaje)
            : base(mensaje)
        {
        }
    }
}
