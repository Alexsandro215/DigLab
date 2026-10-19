using System;
using System.Configuration;
using MySql.Data.MySqlClient;

namespace DigBit.Infraestructura
{
    /// <summary>
    /// Las dos senas de identidad de este equipo: en que laboratorio esta y que
    /// numero de maquina es. Las dos se escriben al instalar (una etiqueta por
    /// equipo, igual para todas las de un mismo laboratorio y distinta entre
    /// laboratorios) y las dos se leen igual: variable de entorno primero,
    /// appSettings despues.
    ///
    /// Laboratorio en el que esta este equipo (fase 6). Un codigo de acceso solo
    /// vale en el laboratorio de su franja, asi que cada equipo tiene que saber
    /// cual es el suyo. Se configura con el NOMBRE exacto del laboratorio
    /// (laboratorios.nombre_laboratorio): appSettings "Laboratorio" en
    /// App.config, o la variable de entorno DIGBIT_LABORATORIO, que tiene
    /// prioridad (igual que el resto de la configuracion). deploy\configurar_equipo.ps1
    /// lo escribe con -Laboratorio.
    /// </summary>
    internal static class LaboratorioEquipo
    {
        private const string ClaveAppSettings = "Laboratorio";
        private const string VariableEntorno = "DIGBIT_LABORATORIO";

        public static int Id { get; private set; }
        public static string Nombre { get; private set; }

        public static bool Resuelto
        {
            get { return Id > 0; }
        }

        /// <summary>Fase 4: sin servidor, el laboratorio sale de la copia local del horario.</summary>
        public static void UsarCopiaLocal(int id, string nombre)
        {
            Id = id;
            Nombre = nombre;
        }

        private const string ClaveNumeroMaquina = "NumeroMaquina";
        private const string VariableNumeroMaquina = "DIGBIT_NUMERO_MAQUINA";

        /// <summary>
        /// Como se identifica esta maquina en la bitacora. Sin configurar usa el
        /// nombre de Windows, que es lo que se hacia siempre; el problema es que
        /// si los equipos se llaman DESKTOP-A7F3K2 eso no le sirve de nada a
        /// quien tenga que ir a buscar "la 12 del laboratorio 1". Con esto se le
        /// pone el numero que de verdad esta pegado en la maquina.
        /// deploy\configurar_equipo.ps1 lo escribe con -NumeroMaquina.
        /// </summary>
        public static string NumeroMaquina
        {
            get
            {
                string configurado = Leer(VariableNumeroMaquina, ClaveNumeroMaquina);
                return configurado ?? Environment.MachineName;
            }
        }

        /// <summary>true si el numero sale de la configuracion y no del nombre de Windows.</summary>
        public static bool NumeroMaquinaConfigurado
        {
            get { return Leer(VariableNumeroMaquina, ClaveNumeroMaquina) != null; }
        }

        /// <summary>Variable de entorno primero, appSettings despues; null si no hay nada.</summary>
        private static string Leer(string variable, string clave)
        {
            string desdeEntorno = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(desdeEntorno))
            {
                return desdeEntorno.Trim();
            }

            try
            {
                string desdeConfig = ConfigurationManager.AppSettings[clave];
                return string.IsNullOrWhiteSpace(desdeConfig) ? null : desdeConfig.Trim();
            }
            catch (ConfigurationErrorsException)
            {
                return null;
            }
        }

        /// <summary>Nombre configurado, o null si no hay ninguno.</summary>
        public static string NombreConfigurado
        {
            get { return Leer(VariableEntorno, ClaveAppSettings); }
        }

        /// <summary>
        /// Busca el laboratorio configurado en la base. Devuelve null si quedo
        /// resuelto, o el problema en texto (sin configurar, o no existe) si no.
        /// Se llama al arrancar, despues de comprobar el servidor. Si la consulta
        /// falla (se fue la red), el laboratorio que hubiera se queda: antes se
        /// ponia a 0 ANTES de consultar y una caida en ese momento dejaba el
        /// equipo rechazando a todos los alumnos hasta reiniciar.
        /// </summary>
        public static string Resolver()
        {
            int id;
            string nombre;
            string problema = Buscar(out id, out nombre);
            if (problema == null)
            {
                Fijar(id, nombre);
            }

            return problema;
        }

        /// <summary>
        /// Solo la consulta, sin tocar el laboratorio de este equipo: la usa la
        /// vigilancia de conexion desde su hilo. Lanza si no hay servidor.
        /// </summary>
        public static string Buscar(out int id, out string nombre)
        {
            id = 0;
            nombre = null;

            // En pantalla, que hacer; lo tecnico (el archivo y la clave), al log.
            string configurado = NombreConfigurado;
            if (configurado == null)
            {
                Log.Aviso("Este equipo no tiene laboratorio: falta 'Laboratorio' en DigBit.exe.config (o la variable DIGBIT_LABORATORIO) con el nombre exacto.");
                return "Este equipo no tiene laboratorio asignado." + Environment.NewLine + Environment.NewLine
                    + "Vuelve a instalarlo con DigBit-Instalador.exe y elige su laboratorio; si no puedes, avisa a la persona responsable del laboratorio.";
            }

            using (MySqlConnection conexion = new Conexion().GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idlaboratorios, nombre_laboratorio FROM laboratorios WHERE nombre_laboratorio = @Nombre", conexion))
            {
                comando.Parameters.AddWithValue("@Nombre", configurado);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        Log.Aviso("El laboratorio de este equipo ('" + configurado + "', DigBit.exe.config) no esta en laboratorios.nombre_laboratorio.");
                        return "El laboratorio de este equipo («" + configurado + "») no está dado de alta en DigBit." + Environment.NewLine + Environment.NewLine
                            + "Pide a la administración de DigBit que lo dé de alta con ese nombre, o vuelve a instalar este equipo con "
                            + "DigBit-Instalador.exe y elige uno de la lista.";
                    }

                    id = reader.GetInt32("idlaboratorios");
                    nombre = reader.GetString("nombre_laboratorio");
                }
            }

            return null;
        }

        /// <summary>Deja fijado el laboratorio de este equipo (lo que encontro Buscar).</summary>
        public static void Fijar(int id, string nombre)
        {
            bool cambia = Id != id || Nombre != nombre;
            Id = id;
            Nombre = nombre;
            if (cambia)
            {
                Log.Info("Laboratorio del equipo: " + Nombre + " (id " + Id + ").");
            }
        }
    }
}
