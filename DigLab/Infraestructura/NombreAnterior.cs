using System;
using System.IO;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// El programa se llamaba DigBit. Un equipo que lo tuvo guarda en %LOCALAPPDATA%\DigBit lo
    /// que no se puede perder: la cola de bitacoras que aun no llegaron al servidor y su copia
    /// del horario. La primera vez que DigLab arranca en esa cuenta, esa carpeta pasa a ser la
    /// suya. Nunca lanza: si no se puede (la usa el programa anterior, sin permiso...), arranca
    /// igual con una carpeta nueva.
    /// </summary>
    internal static class NombreAnterior
    {
        /// <summary>Tambien es el nombre de la conexion en un connections.config de antes.</summary>
        public const string Nombre = "DigBit";

        public static void TraerDatos()
        {
            // Con una carpeta o una base de prueba, la del perfil es la de verdad: no se toca.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CarpetaDatos.VariableEntorno))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DIGLAB_CONNECTION_STRING")))
            {
                return;
            }

            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string anterior = Path.Combine(local, Nombre);
                string nueva = Path.Combine(local, "DigLab");
                if (Directory.Exists(anterior) && !Directory.Exists(nueva))
                {
                    Directory.Move(anterior, nueva);
                }
            }
            catch (Exception)
            {
                // El log aun no existe, y esto no puede impedir que el equipo arranque.
            }
        }
    }
}
