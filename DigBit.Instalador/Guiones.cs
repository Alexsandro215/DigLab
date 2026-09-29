using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;

namespace DigBit.Instalador
{
    /// <summary>
    /// Todo lo que este instalador hace por debajo lo hacen los guiones de
    /// deploy\, que llevan meses de uso y de correcciones. Aqui NO se
    /// reimplementa nada: se construye la linea de comandos y se ejecuta.
    ///
    /// Es a proposito. Si el instalador hiciera su propia version de crear la
    /// cuenta o de escribir el registro, habria dos caminos que mantener y uno
    /// de los dos se quedaria atras.
    /// </summary>
    internal static class Guiones
    {
        /// <summary>
        /// Carpeta que contiene deploy\. Se busca junto al ejecutable y un nivel
        /// por encima, para que valga tanto en la raiz del paquete como en
        /// bin\Release mientras se desarrolla.
        /// </summary>
        public static string RaizPaquete()
        {
            return RaizPaqueteDesde(AppDomain.CurrentDomain.BaseDirectory);
        }

        /// <summary>
        /// La busqueda de verdad, con el punto de partida como parametro para
        /// poder comprobarla sin ejecutar el instalador entero.
        /// </summary>
        public static string RaizPaqueteDesde(string aqui)
        {
            for (int subir = 0; subir <= 4; subir++)
            {
                string candidata = aqui;
                for (int i = 0; i < subir; i++)
                {
                    candidata = Path.GetFullPath(Path.Combine(candidata, ".."));
                }

                if (File.Exists(Path.Combine(candidata, "deploy", "configurar_equipo.ps1")))
                {
                    return candidata;
                }
            }

            return null;
        }

        public static string Guion(string raiz, string nombre)
        {
            return Path.Combine(raiz, "deploy", nombre);
        }

        /// <summary>
        /// Nombre del archivo donde el paquete se acuerda de a que equipo hay
        /// que conectarse. Es el reemplazo del host que el DigBit viejo llevaba
        /// quemado dentro de Conexion.cs ("localhost", y a correr): el equipo no
        /// pregunta a donde va, lo sabe. La diferencia es que este valor se
        /// escribe UNA vez, al preparar el servidor, en vez de compilarse dentro
        /// del ejecutable, asi que se cambia sin recompilar nada.
        /// </summary>
        public const string ArchivoServidor = "servidor.config";

        /// <summary>Tiene prioridad sobre el archivo. Para pruebas y casos raros.</summary>
        public const string VariableServidor = "DIGBIT_SERVIDOR";

        /// <summary>La carpeta de datos que monta preparar_bd.ps1.</summary>
        private const string BaseLocal = @"C:\DigBitDB\data";

        public static string RutaServidor(string raiz)
        {
            return raiz == null ? null : Path.Combine(raiz, ArchivoServidor);
        }

        /// <summary>
        /// A que equipo se conecta este paquete, sin preguntarselo a nadie.
        ///
        /// El orden importa, y la base local va ANTES que servidor.config a
        /// proposito: preparar_bd.ps1 deja MySQL escuchando solo en 127.0.0.1,
        /// asi que en el equipo que hace de servidor la unica direccion que
        /// funciona seguro es esa, aunque servidor.config diga su nombre de red
        /// (que es el que necesitan las maquinas del laboratorio, no esta).
        /// </summary>
        /// <param name="origen">De donde salio el valor, para poder decirlo en pantalla.</param>
        public static string ServidorPorDefecto(string raiz, out string origen)
        {
            string delEntorno = Environment.GetEnvironmentVariable(VariableServidor);
            if (!string.IsNullOrWhiteSpace(delEntorno))
            {
                origen = "de la variable " + VariableServidor;
                return delEntorno.Trim();
            }

            if (Directory.Exists(BaseLocal))
            {
                origen = "la base esta en ESTE equipo";
                return "127.0.0.1";
            }

            string guardado = LeerServidor(raiz);
            if (guardado != null)
            {
                origen = "de " + ArchivoServidor;
                return guardado;
            }

            origen = "este paquete todavia no sabe donde esta la base";
            return string.Empty;
        }

        /// <summary>Primera linea util de servidor.config, o null si no hay.</summary>
        public static string LeerServidor(string raiz)
        {
            string ruta = RutaServidor(raiz);
            if (ruta == null || !File.Exists(ruta)) { return null; }

            try
            {
                foreach (string linea in File.ReadAllLines(ruta))
                {
                    string valor = linea.Trim();
                    if (valor.Length > 0 && !valor.StartsWith("#")) { return valor; }
                }
            }
            catch (IOException)
            {
                // El paquete vive en una memoria USB que igual acaban de quitar.
                // No saber el servidor no es motivo para no dejar instalar: se
                // escribe a mano en la casilla y ya esta.
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }

        /// <summary>
        /// Deja escrito a que equipo apunta el paquete. Devuelve null si pudo, o
        /// el motivo si no: falla si el paquete esta en un CD o en una memoria
        /// protegida contra escritura, y eso NO tiene que parar una instalacion
        /// que por lo demas va bien.
        /// </summary>
        public static string GuardarServidor(string raiz, string valor)
        {
            string ruta = RutaServidor(raiz);
            if (ruta == null) { return "no se cual es la raiz del paquete"; }

            try
            {
                File.WriteAllText(ruta,
                    "# A que equipo se conectan los DigBit instalados desde ESTE paquete." + Environment.NewLine +
                    "# Lo escribe el instalador al preparar el servidor. Si el servidor se" + Environment.NewLine +
                    "# muda, cambia la linea de abajo y los siguientes equipos lo cogen solos." + Environment.NewLine +
                    valor + Environment.NewLine,
                    new UTF8Encoding(false));
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Numeros de serie de las memorias USB conectadas.</summary>
        public static List<string> MemoriasUsb()
        {
            List<string> series = new List<string>();
            try
            {
                using (ManagementObjectSearcher buscador = new ManagementObjectSearcher(
                    "SELECT Model, SerialNumber FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
                using (ManagementObjectCollection discos = buscador.Get())
                {
                    foreach (ManagementBaseObject disco in discos)
                    {
                        using (disco)
                        {
                            object serie = disco["SerialNumber"];
                            object modelo = disco["Model"];
                            if (serie != null && !string.IsNullOrWhiteSpace(serie.ToString()))
                            {
                                series.Add((modelo == null ? "" : modelo + "  ") + serie.ToString().Trim());
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Sin WMI no se puede autorizar ninguna memoria; la casilla se
                // quedara deshabilitada y ya esta.
            }

            return series;
        }

        /// <summary>
        /// Envuelve un valor para pasarlo a PowerShell entre comillas simples,
        /// duplicando las que lleve dentro. Sin esto, una contrasena con un
        /// apostrofo rompe la linea entera o, peor, ejecuta otra cosa.
        /// </summary>
        public static string Comillas(string valor)
        {
            return "'" + (valor ?? string.Empty).Replace("'", "''") + "'";
        }
    }
}
