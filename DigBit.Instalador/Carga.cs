using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace DigBit.Instalador
{
    /// <summary>
    /// El instalador de un solo archivo: DigBit, el vigilante y los guiones de
    /// deploy\ viajan DENTRO del .exe, como un zip incrustado que mete
    /// crear_instalador.ps1. Al abrirlo se extraen a una carpeta temporal, que
    /// hace el papel del paquete, y al cerrarlo se borra.
    ///
    /// Se puede borrar porque nada de lo instalado apunta a ella:
    /// configurar_equipo.ps1 copia DigBit a C:\DigBit y registra el vigilante
    /// desde C:\DigBit\Vigilante.
    ///
    /// Sin el recurso (la compilacion normal de la solucion) no cambia nada: el
    /// instalador busca el paquete junto al ejecutable, como siempre.
    /// </summary>
    internal static class Carga
    {
        /// <summary>Nombre del recurso; lo fija el LogicalName del .csproj.</summary>
        public const string NombreRecurso = "DigBit.Instalador.Carga.zip";

        /// <summary>
        /// Servidor, base, usuario y puerto con los que se armo el .exe, en
        /// lineas clave=valor. NUNCA la contrasena: esa se teclea al instalar.
        /// </summary>
        public const string ArchivoConexion = "conexion.txt";

        public static bool Incluida
        {
            get { return Assembly.GetExecutingAssembly().GetManifestResourceInfo(NombreRecurso) != null; }
        }

        /// <summary>Extrae la carga a una carpeta temporal nueva y devuelve su ruta.</summary>
        public static string Extraer()
        {
            string carpeta = Path.Combine(Path.GetTempPath(), "DigBit-Instalador-" + Guid.NewGuid().ToString("N"));
            string prefijo = Path.GetFullPath(carpeta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(carpeta);

            try
            {
                using (Stream recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream(NombreRecurso))
                {
                    if (recurso == null)
                    {
                        throw new InvalidOperationException("Este instalador no trae la carga " + NombreRecurso + ".");
                    }

                    using (ZipArchive zip = new ZipArchive(recurso, ZipArchiveMode.Read))
                    {
                        foreach (ZipArchiveEntry entrada in zip.Entries)
                        {
                            string destino = Path.GetFullPath(Path.Combine(carpeta, entrada.FullName));

                            // Una entrada con ..\ escribiria fuera de la carpeta (el
                            // "zip slip"). El zip lo arma crear_instalador.ps1, pero el
                            // .exe viaja en memorias USB: no se da nada por supuesto.
                            if (!destino.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidDataException("El instalador trae una entrada fuera de su carpeta: " + entrada.FullName);
                            }

                            if (entrada.FullName.EndsWith("/", StringComparison.Ordinal) || entrada.FullName.EndsWith("\\", StringComparison.Ordinal))
                            {
                                Directory.CreateDirectory(destino);
                                continue;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(destino));
                            entrada.ExtractToFile(destino, true);
                        }
                    }
                }
            }
            catch
            {
                // Lo extraido a medias no sirve para instalar y ocupa sitio.
                Borrar(carpeta);
                throw;
            }

            return carpeta;
        }

        /// <summary>
        /// La conexion que trae el .exe (claves Servidor, Base, Usuario, Puerto),
        /// o un diccionario vacio si no trae.
        /// </summary>
        public static Dictionary<string, string> LeerConexion(string raiz)
        {
            Dictionary<string, string> valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string ruta = raiz == null ? null : Path.Combine(raiz, ArchivoConexion);
            if (ruta == null || !File.Exists(ruta))
            {
                return valores;
            }

            foreach (string linea in File.ReadAllLines(ruta, Encoding.UTF8))
            {
                string texto = linea.Trim();
                int igual = texto.IndexOf('=');
                if (texto.Length == 0 || texto.StartsWith("#", StringComparison.Ordinal) || igual <= 0)
                {
                    continue;
                }

                valores[texto.Substring(0, igual).Trim()] = texto.Substring(igual + 1).Trim();
            }

            return valores;
        }

        /// <summary>Borra la carpeta extraida. Sin quejas: si algo la tiene abierta, se queda en %TEMP%.</summary>
        public static void Borrar(string carpeta)
        {
            try
            {
                if (carpeta != null && Directory.Exists(carpeta))
                {
                    Directory.Delete(carpeta, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
