using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace DigBit.Configurador
{
    /// <summary>
    /// La exclusion de Microsoft Defender, decidida con el usuario (2026-11-04): SOLO los
    /// .exe de DigBit, en los tres tipos de equipo, se ve en el asistente y se quita al
    /// desinstalar. Defender detecta a DigBit como falso positivo (Bearfoos.A!ml: un .exe
    /// sin firmar que se registra como shell) y en un kiosco eso deja el equipo arrancando
    /// en negro. Se pone ANTES de copiar, o se lleva el archivo recien puesto. Lo correcto
    /// a la larga es firmar el ejecutable.
    ///
    /// Por archivo, ni por carpeta ni por proceso: como la version anterior excluia
    /// tambien la carpeta entera, al actualizar se quita eso.
    /// </summary>
    internal static class Defender
    {
        private const string Espacio = @"\\.\root\Microsoft\Windows\Defender";
        private const string Clase = "MSFT_MpPreference";

        public static string[] Archivos(string destino, bool conVigilante)
        {
            List<string> lista = new List<string> { Path.Combine(destino, "DigBit.exe") };
            if (conVigilante)
            {
                lista.Add(Path.Combine(destino, "Vigilante", "DigBit.Vigilante.exe"));
            }

            return lista.ToArray();
        }

        /// <summary>Pone las exclusiones. Sin Defender (otro antivirus), solo avisa.</summary>
        public static void Poner(string destino, bool conVigilante)
        {
            string[] archivos = Archivos(destino, conVigilante);
            Sistema.Hacer("Excluir de Microsoft Defender: " + string.Join(", ", archivos), () =>
            {
                try
                {
                    // Lo de la version anterior: la carpeta entera y los procesos.
                    Invocar("Remove", "ExclusionPath", new[] { destino }, true);
                    Invocar("Remove", "ExclusionProcess", Archivos(destino, true), true);
                    Invocar("Add", "ExclusionPath", archivos, false);
                }
                catch (ManagementException ex)
                {
                    Salida.Aviso("No se pudo poner la exclusion de Defender (" + ex.Message + "). Si este equipo usa otro antivirus, excluye esos archivos en el suyo: puede poner DigBit en cuarentena.");
                }
            });
        }

        public static void Quitar(string destino)
        {
            Sistema.Hacer("Quitar las exclusiones de Microsoft Defender", () =>
            {
                try
                {
                    Invocar("Remove", "ExclusionPath", new[] { destino }, true);
                    Invocar("Remove", "ExclusionPath", Archivos(destino, true), true);
                    Invocar("Remove", "ExclusionProcess", Archivos(destino, true), true);
                }
                catch (ManagementException ex)
                {
                    Salida.Aviso("No se pudieron quitar las exclusiones de Defender: " + ex.Message);
                }
            });
        }

        /// <summary>Las rutas excluidas, o null si Defender no responde (otro antivirus, o sin permiso).</summary>
        public static string[] Excluidas()
        {
            try
            {
                using (ManagementObjectSearcher buscador = new ManagementObjectSearcher(Espacio, "SELECT ExclusionPath FROM " + Clase))
                using (ManagementObjectCollection resultado = buscador.Get())
                {
                    foreach (ManagementBaseObject objeto in resultado)
                    {
                        using (objeto)
                        {
                            return objeto["ExclusionPath"] as string[] ?? new string[0];
                        }
                    }
                }
            }
            catch (ManagementException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }

        private static void Invocar(string metodo, string parametro, string[] valores, bool sinQuejas)
        {
            try
            {
                using (ManagementClass clase = new ManagementClass(Espacio, Clase, null))
                using (ManagementBaseObject entrada = clase.GetMethodParameters(metodo))
                {
                    entrada[parametro] = valores;
                    clase.InvokeMethod(metodo, entrada, null);
                }
            }
            catch (ManagementException)
            {
                if (!sinQuejas)
                {
                    throw;
                }
            }
        }
    }
}
