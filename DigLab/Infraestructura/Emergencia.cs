using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using DigLab.conexion;
using MySql.Data.MySqlClient;

namespace DigLab.Infraestructura
{
    /// <summary>Una memoria USB conectada: el numero de serie de su firmware y lo que dice Windows de ella.</summary>
    internal sealed class MemoriaUsb
    {
        public string Serie;
        /// <summary>"Kingston DataTraveler 3.0 (14.4 GB)"; "" si Windows no lo dice.</summary>
        public string Modelo;
    }

    /// <summary>Las memorias que abren este equipo ahora, y de donde salio la lista.</summary>
    internal sealed class MemoriasAutorizadas
    {
        public List<HuellaDeMemoria> Huellas = new List<HuellaDeMemoria>();
        /// <summary>Para el log: "la base", "la copia de este equipo" o "la instalacion de este equipo".</summary>
        public string Origen;
    }

    /// <summary>
    /// Acceso de emergencia: cuando el servidor no responde y tampoco hay copia
    /// local utilizable del horario, DigLab no puede validar ningun codigo y el
    /// laboratorio se queda inservible. Esto es la salida para el encargado.
    ///
    /// La llave es una MEMORIA USB concreta, identificada por el numero de serie
    /// que lleva en su firmware. Copiar los archivos a otra memoria NO sirve: el
    /// numero va en el dispositivo, no en los datos. Es una llave fisica, como la
    /// del laboratorio: quien la tiene, entra. La ventaja frente a una contrasena
    /// es que no se puede compartir por WhatsApp.
    ///
    /// Desde db/28 las memorias se registran en la base (Laboratorios > "Memorias
    /// de emergencia...") y CUALQUIERA de las registradas abre CUALQUIER equipo:
    /// asi se instala sin memoria y no se depende de una sola (pedido del
    /// 2026-11-07). Que lista vale, en este orden:
    ///   1. La de la base, si responde: lo que se quita deja de abrir al momento.
    ///   2. Si no, la copia de este equipo (memorias_emergencia.txt, junto a
    ///      DigLab.exe). La escribe el instalador y la renueva el servicio
    ///      DigLab.Vigilante cada 10 minutos (CopiaDeMemorias), los dos con
    ///      permisos de administrador: en C:\DigLab un estudiante no puede
    ///      escribir, asi que no puede anadir la suya. Nunca la escribe DigLab
    ///      desde la sesion del estudiante: la carpeta de datos si la puede tocar.
    ///   3. Mientras no haya ninguna registrada (o la base no tenga db/28), la
    ///      que se autorizo al instalar este equipo (EmergenciaMemoria en
    ///      DigLab.exe.config, como antes).
    ///
    /// En la base, en la copia y en la configuracion va el numero HASHEADO con
    /// PBKDF2, igual que las contrasenas de los usuarios: quien lea esos archivos
    /// no saca nada de ellos. En el log nunca aparece el numero real.
    ///
    /// ESTO ES UNA PUERTA A PROPOSITO en la unica cosa que el sistema existe
    /// para obligar. Si una memoria circula, deja de obligar a nada: por eso cada
    /// uso queda en el log, con el numero de la memoria, y se puede quitar.
    /// </summary>
    internal static class Emergencia
    {
        private const string ClaveHashMemoria = "EmergenciaMemoria";

        /// <summary>La copia de la lista, junto a DigLab.exe.</summary>
        public const string ArchivoCopia = "memorias_emergencia.txt";

        public const string OrigenBase = "la base";
        public const string OrigenCopia = "la copia de este equipo";
        public const string OrigenInstalacion = "la instalacion de este equipo";

        /// <summary>
        /// De donde salen las memorias conectadas: WMI. Solo las pruebas lo cambian,
        /// para no depender de las memorias que haya en la computadora que prueba.
        /// </summary>
        internal static Func<List<MemoriaUsb>> LeerConectadas = LeerConWmi;

        public static string RutaCopia
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ArchivoCopia); }
        }

        /// <summary>La que se autorizo al instalar este equipo (antes de db/28), o null.</summary>
        private static string HuellaDeLaInstalacion
        {
            get
            {
                try
                {
                    string valor = ConfigurationManager.AppSettings[ClaveHashMemoria];
                    return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>Las memorias USB conectadas ahora. Nunca lanza: sin WMI, ninguna.</summary>
        public static List<MemoriaUsb> Conectadas()
        {
            try
            {
                return LeerConectadas() ?? new List<MemoriaUsb>();
            }
            catch (Exception ex)
            {
                Log.Error("Leer las memorias USB conectadas", ex);
                return new List<MemoriaUsb>();
            }
        }

        /// <summary>
        /// La lista que vale ahora (ver el orden arriba). Con conLaBase = false no se
        /// pregunta a la base. Nunca lanza.
        /// </summary>
        public static MemoriasAutorizadas Autorizadas(bool conLaBase)
        {
            if (conLaBase)
            {
                try
                {
                    using (MySqlConnection conexion = new Conexion().GetConexion())
                    {
                        List<HuellaDeMemoria> deLaBase = MemoriasEmergenciaDatos.HuellasVigentes(conexion);
                        if (deLaBase != null)
                        {
                            return ConLaDeLaInstalacion(deLaBase, OrigenBase);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Aviso("Memorias de emergencia: la base no respondio (" + ex.Message + "); vale la copia de este equipo.");
                }
            }

            List<HuellaDeMemoria> copia = LeerCopia(RutaCopia);
            return ConLaDeLaInstalacion(copia ?? new List<HuellaDeMemoria>(), copia != null ? OrigenCopia : OrigenInstalacion);
        }

        /// <summary>Mientras no haya ninguna registrada, la que se autorizo al instalar este equipo, si la tiene.</summary>
        private static MemoriasAutorizadas ConLaDeLaInstalacion(List<HuellaDeMemoria> huellas, string origen)
        {
            MemoriasAutorizadas lista = new MemoriasAutorizadas { Huellas = huellas, Origen = origen };
            string instalada = HuellaDeLaInstalacion;
            if (huellas.Count == 0 && instalada != null)
            {
                lista.Huellas = new List<HuellaDeMemoria> { new HuellaDeMemoria { Id = 0, Huella = instalada } };
                lista.Origen = OrigenInstalacion;
            }

            return lista;
        }

        /// <summary>
        /// El numero de la memoria autorizada que esta entre las conectadas (0: la que
        /// se autorizo al instalar el equipo), o null. Se comparan hashes, nunca numeros
        /// de serie. Lento (un PBKDF2 por cada par): fuera del hilo de la pantalla.
        /// </summary>
        public static int? Reconocer(IEnumerable<MemoriaUsb> conectadas, MemoriasAutorizadas lista)
        {
            foreach (MemoriaUsb memoria in conectadas)
            {
                foreach (HuellaDeMemoria huella in lista.Huellas)
                {
                    try
                    {
                        bool necesitaRehash;
                        if (Contrasenas.Verificar(memoria.Serie, huella.Huella, out necesitaRehash))
                        {
                            return huella.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Comprobar una memoria de emergencia", ex);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// False si el numero de serie no distingue a esa memoria: vacio, muy corto o
        /// un solo caracter repetido ("0000000000"). Hay memorias baratas que dicen
        /// eso todas iguales: registrar una abriria los equipos con cualquiera.
        /// </summary>
        public static bool SerieConfiable(string serie)
        {
            string limpia = new string((serie ?? "").Where(char.IsLetterOrDigit).ToArray());
            return limpia.Length >= 6 && limpia.Distinct().Count() > 1;
        }

        // ---------------------------------------------------------------------
        // La copia de este equipo
        // ---------------------------------------------------------------------

        /// <summary>
        /// La lista de la copia, o null si no hay copia (o no se puede leer). Una copia
        /// sin renglones es una lista vacia: se quitaron todas.
        /// </summary>
        public static List<HuellaDeMemoria> LeerCopia(string ruta)
        {
            string[] lineas;
            try
            {
                if (!File.Exists(ruta))
                {
                    return null;
                }

                lineas = File.ReadAllLines(ruta, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Aviso("No se pudo leer la copia de las memorias de emergencia: " + ex.Message);
                return null;
            }

            List<HuellaDeMemoria> huellas = new List<HuellaDeMemoria>();
            foreach (string linea in lineas)
            {
                if (linea.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] partes = linea.Split('\t');
                int id;
                if (partes.Length == 2 && int.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out id)
                    && !string.IsNullOrWhiteSpace(partes[1]))
                {
                    huellas.Add(new HuellaDeMemoria { Id = id, Huella = partes[1].Trim() });
                }
            }

            return huellas;
        }

        /// <summary>
        /// Deja la lista en la copia, entera o nada: se escribe al lado y se cambia de
        /// un golpe, para que un DigLab que la este leyendo no vea media lista.
        /// </summary>
        public static void EscribirCopia(string ruta, IList<HuellaDeMemoria> huellas, DateTime generada)
        {
            StringBuilder texto = new StringBuilder();
            texto.Append("# DigLab: memorias de emergencia registradas en la base, copiadas el ")
                .Append(generada.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" (hora del servidor).\r\n")
                .Append("# Las escriben el instalador y el servicio DigLab.Vigilante; sin servidor, abren este equipo.\r\n")
                .Append("# Cada renglon: el numero de la memoria y su numero de serie hasheado (nunca en claro).\r\n");
            foreach (HuellaDeMemoria huella in huellas)
            {
                texto.Append(huella.Id.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(huella.Huella).Append("\r\n");
            }

            string nueva = ruta + ".nueva";
            File.WriteAllText(nueva, texto.ToString(), new UTF8Encoding(false));
            if (File.Exists(ruta))
            {
                File.Replace(nueva, ruta, null);
            }
            else
            {
                File.Move(nueva, ruta);
            }
        }

        /// <summary>Para el log de arranque: con que memorias se abriria este equipo sin servidor.</summary>
        public static string DescribirSinServidor()
        {
            List<HuellaDeMemoria> copia = LeerCopia(RutaCopia);
            bool instalada = HuellaDeLaInstalacion != null;
            if (copia != null && copia.Count > 0)
            {
                string cuando;
                try
                {
                    cuando = File.GetLastWriteTime(RutaCopia).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    cuando = "?";
                }

                return "Memorias de emergencia sin servidor: " + copia.Count + " en la copia de este equipo (del " + cuando + ").";
            }

            return "Memorias de emergencia sin servidor: " + (copia == null ? "sin copia de la lista" : "la copia dice que no hay ninguna registrada")
                + (instalada ? "; vale la que se autorizo al instalar este equipo." : "; NINGUNA: sin servidor, el boton avisara y no abrira nada.");
        }

        // ---------------------------------------------------------------------
        // Windows
        // ---------------------------------------------------------------------

        /// <summary>
        /// Las unidades USB conectadas. Va por WMI, que es lo unico que ve el numero
        /// del firmware; la etiqueta del volumen y el numero de serie del volumen se
        /// cambian en un minuto y no valen.
        /// </summary>
        private static List<MemoriaUsb> LeerConWmi()
        {
            List<MemoriaUsb> memorias = new List<MemoriaUsb>();
            using (ManagementObjectSearcher buscador = new ManagementObjectSearcher(
                "SELECT SerialNumber, Model, Size FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
            using (ManagementObjectCollection discos = buscador.Get())
            {
                foreach (ManagementBaseObject disco in discos)
                {
                    using (disco)
                    {
                        object serie = disco["SerialNumber"];
                        if (serie == null || string.IsNullOrWhiteSpace(serie.ToString()))
                        {
                            continue;
                        }

                        memorias.Add(new MemoriaUsb { Serie = serie.ToString().Trim(), Modelo = Modelo(disco["Model"], disco["Size"]) });
                    }
                }
            }

            return memorias;
        }

        /// <summary>"Kingston DataTraveler 3.0 (14.4 GB)", sin el "USB Device" que Windows le pone a todas.</summary>
        private static string Modelo(object modelo, object tamano)
        {
            string texto = Convert.ToString(modelo, CultureInfo.InvariantCulture) ?? "";
            texto = texto.Replace("USB Device", "").Trim();
            ulong bytes;
            if (tamano != null && ulong.TryParse(Convert.ToString(tamano, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out bytes) && bytes > 0)
            {
                texto = (texto + " (" + (bytes / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + " GB)").Trim();
            }

            return texto;
        }
    }
}
