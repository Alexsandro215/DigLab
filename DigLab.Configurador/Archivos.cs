using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml;
using DigLab.Infraestructura;

namespace DigLab.Configurador
{
    /// <summary>
    /// La carpeta de la aplicacion: los .config, la cadena de conexion, sus permisos y
    /// la marca de instalacion. Los archivos los copia el Setup.
    /// </summary>
    internal static class Archivos
    {
        private const char MarcaDigLab = 'D';
        private const char MarcaVigilante = 'V';

        public static string ConfigDigLab(string destino) { return Path.Combine(destino, "DigLab.exe.config"); }

        public static string ConfigVigilante(string destino) { return Path.Combine(destino, "Vigilante", "DigLab.Vigilante.exe.config"); }

        public static string RutaConexion(string destino) { return Path.Combine(destino, "connections.config"); }

        /// <summary>Escribe (o crea) claves de appSettings sin tocar el resto del archivo.</summary>
        public static void EscribirAjustes(string archivo, IDictionary<string, string> ajustes)
        {
            XmlDocument xml = new XmlDocument { PreserveWhitespace = true };
            xml.Load(archivo);
            XmlElement raiz = xml.DocumentElement;
            XmlElement seccion = raiz["appSettings"];
            if (seccion == null)
            {
                seccion = xml.CreateElement("appSettings");
                raiz.AppendChild(seccion);
            }

            foreach (KeyValuePair<string, string> par in ajustes)
            {
                XmlElement nodo = null;
                foreach (XmlNode hijo in seccion.ChildNodes)
                {
                    XmlElement elemento = hijo as XmlElement;
                    if (elemento != null && elemento.Name == "add" && elemento.GetAttribute("key") == par.Key)
                    {
                        nodo = elemento;
                        break;
                    }
                }

                if (nodo == null)
                {
                    nodo = xml.CreateElement("add");
                    nodo.SetAttribute("key", par.Key);
                    seccion.AppendChild(nodo);
                }

                nodo.SetAttribute("value", par.Value ?? "");
            }

            xml.Save(archivo);
        }

        private static IEnumerable<XmlElement> Entradas(string archivo)
        {
            if (!File.Exists(archivo))
            {
                yield break;
            }

            XmlDocument xml = new XmlDocument();
            xml.Load(archivo);
            XmlElement seccion = xml.DocumentElement == null ? null : xml.DocumentElement["appSettings"];
            if (seccion == null)
            {
                yield break;
            }

            foreach (XmlNode hijo in seccion.ChildNodes)
            {
                XmlElement elemento = hijo as XmlElement;
                if (elemento != null && elemento.Name == "add")
                {
                    yield return elemento;
                }
            }
        }

        public static string LeerAjuste(string archivo, string clave)
        {
            foreach (XmlElement elemento in Entradas(archivo))
            {
                if (elemento.GetAttribute("key") == clave)
                {
                    return elemento.GetAttribute("value");
                }
            }

            return null;
        }

        /// <summary>
        /// Guarda lo del equipo antes de que el Setup copie los archivos encima: TODOS los
        /// ajustes con valor, no una lista, porque reponer solo unos ya dejo equipos sin
        /// numero de maquina ni memoria de emergencia. Pueden ir claves (la de recuperacion):
        /// cifrado para esta misma cuenta, aunque sea en la carpeta temporal.
        /// </summary>
        public static void GuardarAjustes(string destino, string ruta)
        {
            StringBuilder texto = new StringBuilder();
            AnotarAjustes(texto, MarcaDigLab, ConfigDigLab(destino));
            AnotarAjustes(texto, MarcaVigilante, ConfigVigilante(destino));
            File.WriteAllBytes(ruta, ProtectedData.Protect(Encoding.UTF8.GetBytes(texto.ToString()), null, DataProtectionScope.CurrentUser));
        }

        private static void AnotarAjustes(StringBuilder texto, char marca, string archivo)
        {
            foreach (XmlElement elemento in Entradas(archivo))
            {
                string valor = elemento.GetAttribute("value");
                if (valor.Length > 0)
                {
                    texto.Append(marca).Append(elemento.GetAttribute("key")).Append('=').Append(valor).Append('\n');
                }
            }
        }

        /// <summary>Los devuelve despues de copiar, sobre los .config nuevos, y comprueba que quedaron.</summary>
        public static void ReponerAjustes(string destino, string ruta)
        {
            string texto = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(ruta), null, DataProtectionScope.CurrentUser));
            Dictionary<string, string> deDigLab = new Dictionary<string, string>();
            Dictionary<string, string> delVigilante = new Dictionary<string, string>();
            foreach (string linea in texto.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int igual = linea.IndexOf('=');
                if (igual > 1)
                {
                    Dictionary<string, string> ajustes = linea[0] == MarcaVigilante ? delVigilante : deDigLab;
                    ajustes[linea.Substring(1, igual - 1)] = linea.Substring(igual + 1);
                }
            }

            Reponer(ConfigDigLab(destino), deDigLab);
            Reponer(ConfigVigilante(destino), delVigilante);
        }

        private static void Reponer(string archivo, Dictionary<string, string> ajustes)
        {
            if (ajustes.Count == 0 || !File.Exists(archivo))
            {
                return;
            }

            EscribirAjustes(archivo, ajustes);
            foreach (KeyValuePair<string, string> par in ajustes)
            {
                if (LeerAjuste(archivo, par.Key) != par.Value)
                {
                    throw new FalloDeInstalacion("El ajuste «" + par.Key + "» no se repuso en " + Path.GetFileName(archivo) + ": la configuración del equipo no quedó como estaba.");
                }
            }
        }

        /// <summary>
        /// Escribe la conexion CIFRADA para este equipo (CadenaCifrada, DPAPI de la
        /// maquina): el estudiante puede abrir el archivo y no debe ver la contrasena.
        /// </summary>
        public static void EscribirConexion(string destino, string cadena)
        {
            string texto = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                + "<connectionStrings>\r\n"
                + "  <!-- Cifrada para este equipo (DigLab-Setup.exe): en otra computadora no sirve. -->\r\n"
                + "  <add name=\"DigLab\" connectionString=\"" + SecurityElement.Escape(CadenaCifrada.Cifrar(cadena)) + "\" providerName=\"MySql.Data.MySqlClient\" />\r\n"
                + "</connectionStrings>\r\n";
            File.WriteAllText(RutaConexion(destino), texto, new UTF8Encoding(false));
        }

        /// <summary>
        /// La cadena que ya tiene el equipo, en claro, o null. Sin la plantilla: un equipo
        /// con ella no tiene conexion, aunque el archivo exista. Una cifrada en otro equipo
        /// (copiada, o el equipo se reimagino) tampoco: el Setup vuelve a pedir la base.
        /// </summary>
        public static string LeerConexion(string destino)
        {
            string guardada = ConexionGuardada(destino);
            if (guardada == null)
            {
                return null;
            }

            try
            {
                return CadenaCifrada.Descifrar(guardada);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
        }

        /// <summary>True si el equipo tiene una conexion de verdad SIN cifrar (de antes del cifrado, o de configurar_equipo.ps1).</summary>
        public static bool ConexionEnClaro(string destino)
        {
            string guardada = ConexionGuardada(destino);
            return guardada != null && !CadenaCifrada.EstaCifrada(guardada);
        }

        /// <summary>Lo que dice el archivo, tal cual (cifrado o no), o null si no hay o es la plantilla.</summary>
        private static string ConexionGuardada(string destino)
        {
            string ruta = RutaConexion(destino);
            if (!File.Exists(ruta))
            {
                return null;
            }

            XmlDocument xml = new XmlDocument();
            xml.Load(ruta);
            foreach (XmlNode hijo in xml.GetElementsByTagName("add"))
            {
                XmlElement elemento = hijo as XmlElement;
                // "DigBit": un connections.config de cuando el programa se llamaba asi.
                if (elemento != null && (elemento.GetAttribute("name") == "DigLab" || elemento.GetAttribute("name") == "DigBit"))
                {
                    string cadena = elemento.GetAttribute("connectionString");
                    return cadena.IndexOf("SERVIDOR", StringComparison.Ordinal) >= 0 ? null : cadena;
                }
            }

            return null;
        }

        /// <summary>
        /// Una carpeta en la raiz de C: hereda "Usuarios autenticados: modificar", asi que
        /// por defecto el alumno podria reescribir DigLab.exe, que es el shell de su
        /// sesion: renombrarlo y dejar un cmd.exe con ese nombre le da un simbolo del
        /// sistema, y editar el .config le basta para apagar el modo kiosco. Se rompe la
        /// herencia y se deja en solo lectura para Usuarios. Con SIDs y no con nombres:
        /// en Windows en espanol el grupo se llama "Usuarios".
        /// </summary>
        public static void SoloLectura(string destino)
        {
            DirectorySecurity acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            const InheritanceFlags Hereda = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"), FileSystemRights.FullControl, Hereda, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-18"), FileSystemRights.FullControl, Hereda, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadAndExecute, Hereda, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(destino).SetAccessControl(acl);
        }

        /// <summary>Para demostrar despues si el disco conserva lo que se escribe; la lee el diagnostico.</summary>
        public static void Marca(string destino)
        {
            File.WriteAllText(Path.Combine(destino, ".instalado"),
                DateTime.Now.ToString("o", CultureInfo.InvariantCulture), new UTF8Encoding(false));
        }
    }
}
