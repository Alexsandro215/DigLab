using System;
using System.Configuration;
using System.IO;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// Carpeta donde DigLab deja lo que NO se puede perder al reiniciar: la cola
    /// de bitacoras que aun no llegaron al servidor, la copia local del horario y
    /// los registros.
    ///
    /// Los equipos del laboratorio tienen Deep Freeze: el disco del sistema
    /// vuelve a su estado congelado en cada reinicio. Todo lo que este bajo
    /// %LOCALAPPDATA% o %ProgramData% desaparece al apagar el equipo, incluidas
    /// las bitacoras que un alumno registro mientras el servidor estaba caido.
    /// Por eso la carpeta es configurable: se apunta a un ThawSpace de Deep
    /// Freeze (por convencion T:\) o a una particion que no este congelada.
    ///
    /// Prioridad, igual que el resto de la configuracion: la variable de entorno
    /// DIGLAB_CARPETA_DATOS, appSettings "CarpetaDatos", y si no hay ninguna el
    /// respaldo que pasa cada programa (LOCALAPPDATA en DigLab, ProgramData en
    /// el vigilante, que corre como servicio y no tiene perfil de usuario).
    ///
    /// Nunca lanza y nunca escribe en el log: un equipo mal configurado tiene
    /// que arrancar igual, aunque pierda los datos al reiniciar, y el log mismo
    /// depende de esta clase. Lo que haya pasado se cuenta en
    /// <see cref="Diagnostico"/>, que Program.cs registra al arrancar.
    ///
    /// Este archivo se compila tambien en DigLab.Vigilante (enlazado): no puede
    /// depender de WinForms ni de nada mas del proyecto.
    /// </summary>
    internal static class CarpetaDatos
    {
        public const string VariableEntorno = "DIGLAB_CARPETA_DATOS";
        public const string ClaveAppSettings = "CarpetaDatos";

        private static readonly object candado = new object();
        private static bool comprobada;
        private static bool usable;
        private static string diagnostico = "Carpeta de datos: sin configurar, se usa la del perfil (se pierde al reiniciar si el equipo tiene Deep Freeze).";

        /// <summary>Lo que quedo de resolver la carpeta, para el log de arranque.</summary>
        public static string Diagnostico
        {
            get { lock (candado) { return diagnostico; } }
        }

        /// <summary>true si se configuro una carpeta y se pudo escribir en ella.</summary>
        public static bool Persistente
        {
            get { lock (candado) { return comprobada && usable; } }
        }

        /// <summary>Lo que dice la configuracion, sin comprobar. null si no hay nada.</summary>
        public static string Configurada
        {
            get
            {
                string desdeEntorno = LeerEntorno();
                if (desdeEntorno != null)
                {
                    return desdeEntorno;
                }

                try
                {
                    string desdeConfig = ConfigurationManager.AppSettings[ClaveAppSettings];
                    return string.IsNullOrWhiteSpace(desdeConfig) ? null : desdeConfig.Trim();
                }
                catch (Exception)
                {
                    // Un App.config roto no puede impedir que el equipo arranque.
                    return null;
                }
            }
        }

        /// <summary>
        /// Carpeta a usar. Devuelve la configurada si se puede escribir en ella,
        /// y si no <paramref name="respaldo"/>. La comprobacion se hace una sola
        /// vez por proceso: si alguien desconecta el disco con la app abierta,
        /// lo que falla es la escritura, no esto.
        /// </summary>
        public static string Resolver(string respaldo)
        {
            string configurada = Configurada;
            if (configurada == null)
            {
                return respaldo;
            }

            lock (candado)
            {
                if (!comprobada)
                {
                    comprobada = true;
                    string problema = Probar(configurada);
                    usable = problema == null;
                    diagnostico = usable
                        ? "Carpeta de datos: " + configurada + " (persistente, sobrevive al reinicio)."
                        : "Carpeta de datos: NO se puede usar " + configurada + " (" + problema + "). Se usa " + respaldo
                          + ", que en un equipo con Deep Freeze se pierde al reiniciar.";
                }

                return usable ? configurada : respaldo;
            }
        }

        private static string LeerEntorno()
        {
            try
            {
                string valor = Environment.GetEnvironmentVariable(VariableEntorno);
                return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Crea la carpeta y escribe un archivo de prueba. Devuelve el problema, o null si todo bien.</summary>
        private static string Probar(string carpeta)
        {
            try
            {
                Directory.CreateDirectory(carpeta);
                string prueba = Path.Combine(carpeta, ".diglab_escritura");
                File.WriteAllText(prueba, DateTime.Now.ToString("s"));
                File.Delete(prueba);
                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
