using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// Archivo de traspaso entre DigLab (en la sesion del alumno) y el vigilante
    /// (servicio en la sesion 0): %ProgramData%\DigLab\sesion_activa.txt, una
    /// linea clave=valor por campo, fechas ISO 8601 con zona. Lo escribe DigLab al
    /// liberar el equipo y lo renueva cada 30 s (latido); lo lee el vigilante cada
    /// 10 s. Las reglas de vigencia las aplican los dos lados, iguales.
    ///
    /// Este archivo se compila tambien en DigLab.Vigilante (enlazado): no puede
    /// depender de WinForms ni de nada mas del proyecto.
    /// </summary>
    public class SesionActiva
    {
        public const int VersionActual = 1;

        /// <summary>Pasado este margen tras el fin, el archivo se considera basura.</summary>
        public static readonly TimeSpan ToleranciaCaducidad = TimeSpan.FromMinutes(10);

        /// <summary>Dos procesos del mismo arranque calculan el mismo instante con esta tolerancia.</summary>
        public static readonly TimeSpan ToleranciaArranque = TimeSpan.FromSeconds(60);

        /// <summary>Si el latido es mas viejo que esto, DigLab ya no esta para avisar.</summary>
        public static readonly TimeSpan LatidoMaximo = TimeSpan.FromSeconds(90);

        public enum Vigencia
        {
            Vigente,
            Caducada,
            DeOtroArranque,
            DeOtraSesionWindows
        }

        public int Version = VersionActual;
        public string Usuario;
        public string Codigo;
        public string Equipo;
        public int SesionWindows;
        public DateTime ArranqueSistema;
        public DateTime InicioLocal;
        public DateTime FinLocal;
        public DateTime Escrito;
        public DateTime Latido;

        public static string Carpeta
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DigLab"); }
        }

        public static string Ruta
        {
            get { return Path.Combine(Carpeta, "sesion_activa.txt"); }
        }

        /// <summary>
        /// Instante de arranque del sistema visto desde este proceso (ahora - tiempo
        /// encendido). Los ids de sesion de Windows se repiten entre arranques; esto
        /// es lo que distingue "DigLab se relanzo" de "el equipo se reinicio".
        /// Environment.TickCount da la vuelta a los 49,7 dias de encendido continuo.
        /// </summary>
        public static DateTime ArranqueActual()
        {
            uint milisegundosEncendido = unchecked((uint)Environment.TickCount);
            return DateTime.Now - TimeSpan.FromMilliseconds(milisegundosEncendido);
        }

        public Vigencia Evaluar(DateTime ahora, DateTime arranqueActual, int sesionWindowsActual)
        {
            if (ahora > FinLocal + ToleranciaCaducidad)
            {
                return Vigencia.Caducada;
            }

            if ((ArranqueSistema - arranqueActual).Duration() > ToleranciaArranque)
            {
                return Vigencia.DeOtroArranque;
            }

            if (SesionWindows != sesionWindowsActual)
            {
                return Vigencia.DeOtraSesionWindows;
            }

            return Vigencia.Vigente;
        }

        public bool LatidoReciente(DateTime ahora)
        {
            return ahora - Latido <= LatidoMaximo;
        }

        public bool EsLaMisma(SesionActiva otra)
        {
            return otra != null
                && string.Equals(Usuario, otra.Usuario, StringComparison.Ordinal)
                && string.Equals(Codigo, otra.Codigo, StringComparison.Ordinal)
                && SesionWindows == otra.SesionWindows
                && InicioLocal == otra.InicioLocal;
        }

        // --- Persistencia ---------------------------------------------------

        public void Guardar()
        {
            Directory.CreateDirectory(Carpeta);
            string temporal = Ruta + ".tmp";
            File.WriteAllText(temporal, Serializar(), Encoding.UTF8);
            if (File.Exists(Ruta))
            {
                File.Replace(temporal, Ruta, null);
            }
            else
            {
                File.Move(temporal, Ruta);
            }
        }

        /// <summary>Devuelve null si no hay archivo o no se puede interpretar.</summary>
        public static SesionActiva Leer()
        {
            if (!File.Exists(Ruta))
            {
                return null;
            }

            try
            {
                return Deserializar(File.ReadAllText(Ruta, Encoding.UTF8));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Borrar()
        {
            try
            {
                if (File.Exists(Ruta))
                {
                    File.Delete(Ruta);
                }
            }
            catch (Exception)
            {
                // El siguiente que lo lea lo vera caducado de todas formas.
            }
        }

        public string Serializar()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("version=").Append(Version).Append('\n');
            sb.Append("usuario=").Append(Usuario).Append('\n');
            sb.Append("codigo=").Append(Codigo).Append('\n');
            sb.Append("equipo=").Append(Equipo).Append('\n');
            sb.Append("sesionWindows=").Append(SesionWindows).Append('\n');
            sb.Append("arranqueSistema=").Append(Fecha(ArranqueSistema)).Append('\n');
            sb.Append("inicioLocal=").Append(Fecha(InicioLocal)).Append('\n');
            sb.Append("finLocal=").Append(Fecha(FinLocal)).Append('\n');
            sb.Append("escrito=").Append(Fecha(Escrito)).Append('\n');
            sb.Append("latido=").Append(Fecha(Latido)).Append('\n');
            return sb.ToString();
        }

        public static SesionActiva Deserializar(string texto)
        {
            Dictionary<string, string> campos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string lineaCruda in texto.Split('\n'))
            {
                string linea = lineaCruda.Trim();
                int igual = linea.IndexOf('=');
                if (igual <= 0)
                {
                    continue;
                }

                campos[linea.Substring(0, igual).Trim()] = linea.Substring(igual + 1).Trim();
            }

            SesionActiva s = new SesionActiva();
            s.Version = int.Parse(Obligatorio(campos, "version"), CultureInfo.InvariantCulture);
            if (s.Version != VersionActual)
            {
                throw new FormatException("Version de archivo no soportada: " + s.Version);
            }

            s.Usuario = Obligatorio(campos, "usuario");
            s.Codigo = Obligatorio(campos, "codigo");
            s.Equipo = Opcional(campos, "equipo");
            s.SesionWindows = int.Parse(Obligatorio(campos, "sesionWindows"), CultureInfo.InvariantCulture);
            s.ArranqueSistema = Fecha(Obligatorio(campos, "arranqueSistema"));
            s.InicioLocal = Fecha(Obligatorio(campos, "inicioLocal"));
            s.FinLocal = Fecha(Obligatorio(campos, "finLocal"));
            s.Escrito = Fecha(Obligatorio(campos, "escrito"));
            s.Latido = Fecha(Obligatorio(campos, "latido"));
            return s;
        }

        private static string Obligatorio(Dictionary<string, string> campos, string clave)
        {
            string valor;
            if (!campos.TryGetValue(clave, out valor) || valor.Length == 0)
            {
                throw new FormatException("Falta el campo '" + clave + "'.");
            }

            return valor;
        }

        private static string Opcional(Dictionary<string, string> campos, string clave)
        {
            string valor;
            return campos.TryGetValue(clave, out valor) ? valor : string.Empty;
        }

        private static string Fecha(DateTime valor)
        {
            return valor.ToString("o", CultureInfo.InvariantCulture);
        }

        private static DateTime Fecha(string texto)
        {
            return DateTime.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
    }
}
