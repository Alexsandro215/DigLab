using System;
using System.Collections.Generic;
using System.Configuration;
using System.Management;
using DigLab.conexion;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// Acceso de emergencia: cuando el servidor no responde y tampoco hay copia
    /// local utilizable del horario, DigLab no puede validar ningun codigo y el
    /// laboratorio se queda inservible. Esto es la salida para el encargado.
    ///
    /// La llave es UNA MEMORIA USB concreta, identificada por el numero de serie
    /// que lleva en su firmware. Copiar los archivos a otra memoria NO sirve: el
    /// numero va en el dispositivo, no en los datos.
    ///
    /// Es una llave fisica, como la del laboratorio: quien la tiene, entra. Por
    /// eso vive en el llavero del encargado y no en un cajon. La ventaja frente
    /// a una contrasena es que no se puede compartir por WhatsApp.
    ///
    /// El numero se guarda HASHEADO con PBKDF2 en DigLab.exe.config, igual que
    /// las contrasenas de los usuarios: el alumno puede leer ese archivo --corre
    /// con su sesion-- y no saca nada de el.
    ///
    /// ESTO ES UNA PUERTA A PROPOSITO en la unica cosa que el sistema existe
    /// para obligar. Si la memoria circula, deja de obligar a nada. Por eso cada
    /// uso queda registrado en el log.
    /// </summary>
    internal static class Emergencia
    {
        private const string ClaveHashMemoria = "EmergenciaMemoria";

        /// <summary>true si este equipo tiene una memoria autorizada.</summary>
        public static bool Configurada
        {
            get { return HashMemoria != null; }
        }

        private static string HashMemoria { get { return Ajuste(ClaveHashMemoria); } }

        private static string Ajuste(string clave)
        {
            try
            {
                string valor = ConfigurationManager.AppSettings[clave];
                return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// true si alguna de las memorias USB conectadas ahora mismo es LA
        /// memoria. Se comparan hashes, no numeros de serie: en el log y en la
        /// configuracion nunca aparece el numero real.
        /// </summary>
        public static bool MemoriaPresente()
        {
            string almacenada = HashMemoria;
            if (almacenada == null)
            {
                return false;
            }

            foreach (string serie in SeriesConectadas())
            {
                try
                {
                    bool necesitaRehash;
                    if (Contrasenas.Verificar(serie, almacenada, out necesitaRehash))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Comprobar la memoria de emergencia", ex);
                }
            }

            return false;
        }

        /// <summary>Cuantas memorias USB hay conectadas, para poder decir "no veo ninguna".</summary>
        public static int MemoriasConectadas()
        {
            return SeriesConectadas().Count;
        }

        /// <summary>
        /// Numeros de serie de las unidades USB conectadas. Va por WMI, que es
        /// lo unico que ve el numero del firmware; la etiqueta del volumen y el
        /// numero de serie del volumen se cambian en un minuto y no valen.
        /// </summary>
        private static List<string> SeriesConectadas()
        {
            List<string> series = new List<string>();
            try
            {
                using (ManagementObjectSearcher buscador = new ManagementObjectSearcher(
                    "SELECT SerialNumber FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
                using (ManagementObjectCollection discos = buscador.Get())
                {
                    foreach (ManagementBaseObject disco in discos)
                    {
                        using (disco)
                        {
                            object serie = disco["SerialNumber"];
                            if (serie != null && !string.IsNullOrWhiteSpace(serie.ToString()))
                            {
                                series.Add(serie.ToString().Trim());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Leer las memorias USB conectadas", ex);
            }

            return series;
        }

        /// <summary>
        /// Para preparar un equipo: el numero de serie de la unica memoria USB
        /// conectada, o null si no hay ninguna o hay varias. Lo usa
        /// deploy\configurar_equipo.ps1 para no tener que teclearlo a mano.
        /// </summary>
        public static string SerieDeLaUnicaMemoria()
        {
            List<string> series = SeriesConectadas();
            return series.Count == 1 ? series[0] : null;
        }
    }
}
