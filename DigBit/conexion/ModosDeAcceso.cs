using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Como entran los alumnos a sus clases en un laboratorio (laboratorios.modo_codigo, db/11).</summary>
    public enum ModoCodigo
    {
        /// <summary>Con el codigo unico (fijo) de cada profesor (db/09). Lo de siempre y lo unico que hay sin db/11.</summary>
        Fijo,
        /// <summary>
        /// Generacion de codigos: uno distinto por clase y dia, generado al
        /// empezar cada semana (CodigosGenerados). El administrador los ve todos;
        /// el profesor, el de su clase al entrar a DigBit durante ella.
        /// </summary>
        Renovable,
        /// <summary>Sin codigo: el alumno solo llena su bitacora (con dos clases a la vez, elige la suya).</summary>
        SinCodigo
    }

    /// <summary>
    /// El modo de acceso de cada laboratorio. No se guarda en memoria: se vuelve
    /// a leer en cada decision (resolver, abrir una clase, subir la cola,
    /// refrescar paneles y copia), asi que un cambio vale desde el siguiente
    /// intento. Ante cualquier duda es Fijo, que es lo de siempre.
    /// </summary>
    internal static class ModosDeAcceso
    {
        private static readonly object candado = new object();
        private static bool avisado;

        public static ModoCodigo Desde(string valor)
        {
            switch ((valor ?? "").Trim().ToLowerInvariant())
            {
                case "renovable":
                    return ModoCodigo.Renovable;
                case "sin_codigo":
                    return ModoCodigo.SinCodigo;
                default:
                    // 'fijo', '' (un valor raro con el sql_mode vacio) o nada.
                    return ModoCodigo.Fijo;
            }
        }

        /// <summary>El valor de laboratorios.modo_codigo.</summary>
        public static string Valor(ModoCodigo modo)
        {
            switch (modo)
            {
                case ModoCodigo.Renovable:
                    return "renovable";
                case ModoCodigo.SinCodigo:
                    return "sin_codigo";
                default:
                    return "fijo";
            }
        }

        /// <summary>
        /// El nombre de cada forma, el de las opciones de "Codigos de las clases":
        /// "Sin códigos", "Código único" o "Generación de códigos". Son los tres que
        /// eligio el usuario: ningun texto usa otro (ni "fijo" ni "rotativos").
        /// </summary>
        public static string Nombre(ModoCodigo modo)
        {
            switch (modo)
            {
                case ModoCodigo.Renovable:
                    return "Generación de códigos";
                case ModoCodigo.SinCodigo:
                    return "Sin códigos";
                default:
                    return "Código único";
            }
        }

        /// <summary>El mismo nombre dentro de una frase: "código único", "generación de códigos".</summary>
        public static string Descripcion(ModoCodigo modo)
        {
            switch (modo)
            {
                case ModoCodigo.Renovable:
                    return "generación de códigos";
                case ModoCodigo.SinCodigo:
                    return "sin códigos";
                default:
                    return "código único";
            }
        }

        /// <summary>
        /// El modo de un laboratorio. Fijo sin db/11, si el laboratorio no existe
        /// y si leerlo da un error de esquema o de permisos: nunca debe hacer que
        /// la pantalla del alumno pase al modo sin conexion. Los errores de
        /// conexion si se propagan, como los de cualquier consulta.
        /// </summary>
        public static ModoCodigo Leer(MySqlConnection conexion, int idLaboratorio, MySqlTransaction transaccion = null)
        {
            try
            {
                if (idLaboratorio <= 0 || !Esquema.ModoDeAcceso(conexion, transaccion))
                {
                    return ModoCodigo.Fijo;
                }

                using (MySqlCommand comando = new MySqlCommand("SELECT modo_codigo FROM laboratorios WHERE idlaboratorios = @Lab", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                    object valor = comando.ExecuteScalar();
                    return valor == null || valor == DBNull.Value ? ModoCodigo.Fijo : Desde(Convert.ToString(valor, CultureInfo.InvariantCulture));
                }
            }
            catch (MySqlException ex) when (EsDeEsquema(ex))
            {
                Avisar(ex);
                return ModoCodigo.Fijo;
            }
        }

        /// <summary>El modo de todos los laboratorios (los que no salgan son Fijo).</summary>
        public static Dictionary<int, ModoCodigo> LeerTodos(MySqlConnection conexion)
        {
            Dictionary<int, ModoCodigo> modos = new Dictionary<int, ModoCodigo>();
            try
            {
                if (!Esquema.ModoDeAcceso(conexion))
                {
                    return modos;
                }

                using (MySqlCommand comando = new MySqlCommand("SELECT idlaboratorios, modo_codigo FROM laboratorios", conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        modos[Convert.ToInt32(reader["idlaboratorios"])] = Desde(Convert.ToString(reader["modo_codigo"], CultureInfo.InvariantCulture));
                    }
                }
            }
            catch (MySqlException ex) when (EsDeEsquema(ex))
            {
                Avisar(ex);
                modos.Clear();
            }

            return modos;
        }

        public static ModoCodigo De(IDictionary<int, ModoCodigo> modos, int idLaboratorio)
        {
            ModoCodigo modo;
            return modos != null && modos.TryGetValue(idLaboratorio, out modo) ? modo : ModoCodigo.Fijo;
        }

        /// <summary>El modo de la mayoria (a igual numero, el de siempre); Fijo si no hay ninguno.</summary>
        public static ModoCodigo Comun(IEnumerable<ModoCodigo> modos)
        {
            return (modos ?? Enumerable.Empty<ModoCodigo>())
                .GroupBy(m => m)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => (int)g.Key)
                .Select(g => g.Key)
                .DefaultIfEmpty(ModoCodigo.Fijo)
                .First();
        }

        /// <summary>La clave de la forma general en configuracion (db/13).</summary>
        public const string ClaveGeneral = "modo_codigo_general";

        /// <summary>
        /// Cambio el modo de algun laboratorio o la forma general
        /// (LaboratoriosDatos.CambiarModos), en el hilo que lo cambio. Lo oyen las
        /// ventanas abiertas que lo ensenan: Laboratorios, Horarios y el panel.
        /// </summary>
        public static event Action Cambio;

        internal static void AvisarCambio()
        {
            Action aviso = Cambio;
            if (aviso != null)
            {
                aviso();
            }
        }

        /// <summary>
        /// La forma general guardada ("Codigos de las clases", arriba): la de los
        /// laboratorios que no estan en "con otra forma" y la de los nuevos. Va en
        /// configuracion para que no se voltee sola cuando los laboratorios con otra
        /// forma llegan a ser mas. Null sin db/13, sin ese ajuste o con un valor
        /// raro: entonces vale la de la mayoria (General).
        /// </summary>
        public static ModoCodigo? LeerGeneral(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            return LeerAjustes(conexion, transaccion).General;
        }

        /// <summary>
        /// Los dos ajustes de "Codigos de las clases" que van en configuracion
        /// (db/13), en una sola consulta: el panel la repite cada 30 s. Sin db/13,
        /// si esta cuenta no ve la tabla o con un error de esquema, los dos en
        /// null. Recuerda lo leido de los docentes (CodigosSoloResponsables.Activa).
        /// </summary>
        public static AjustesDeCodigos LeerAjustes(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            AjustesDeCodigos ajustes = new AjustesDeCodigos();
            try
            {
                if (!Esquema.Ajustes(conexion, transaccion))
                {
                    CodigosSoloResponsables.Recordar(false);
                    return ajustes;
                }

                string general = "";
                string soloResponsables = "";
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT clave, valor FROM configuracion WHERE clave IN (@General, @Solo)", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@General", ClaveGeneral);
                    comando.Parameters.AddWithValue("@Solo", CodigosSoloResponsables.Clave);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string valor = reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture).Trim().ToLowerInvariant();
                            if (string.Equals(reader.GetString(0), ClaveGeneral, StringComparison.OrdinalIgnoreCase))
                            {
                                general = valor;
                            }
                            else
                            {
                                soloResponsables = valor;
                            }
                        }
                    }
                }

                ajustes.General = general == "fijo" || general == "renovable" || general == "sin_codigo" ? Desde(general) : (ModoCodigo?)null;
                ajustes.SoloResponsables = CodigosSoloResponsables.Desde(soloResponsables);
                CodigosSoloResponsables.Recordar(ajustes.SoloResponsables.Value);
            }
            catch (MySqlException ex) when (EsDeEsquema(ex))
            {
                // Sin poder leerla se queda lo ultimo que se supo de los docentes.
                ajustes = new AjustesDeCodigos();
            }

            return ajustes;
        }

        /// <summary>La forma general guardada o, si no hay, la de la mayoria de esos modos.</summary>
        public static ModoCodigo General(ModoCodigo? guardada, IEnumerable<ModoCodigo> modos)
        {
            return guardada ?? Comun(modos);
        }

        /// <summary>
        /// "código único en todos los laboratorios" o "sin códigos, salvo 2
        /// laboratorios con otra forma", con los modos de los que estan en uso.
        /// </summary>
        public static string Resumen(ModoCodigo general, ICollection<ModoCodigo> modosEnUso)
        {
            int otros = modosEnUso == null ? 0 : modosEnUso.Count(m => m != general);
            if (modosEnUso == null || modosEnUso.Count == 0)
            {
                return Descripcion(general) + " (aún no hay laboratorios)";
            }

            return Descripcion(general) + (otros == 0 ? " en todos los laboratorios"
                : ", salvo " + otros + (otros == 1 ? " laboratorio con otra forma" : " laboratorios con otra forma"));
        }

        /// <summary>Columna o tabla que no esta, o sin permiso para leerla.</summary>
        private static bool EsDeEsquema(MySqlException ex)
        {
            return ex.Number == 1054 || ex.Number == 1142 || ex.Number == 1143 || ex.Number == 1146;
        }

        private static void Avisar(MySqlException ex)
        {
            lock (candado)
            {
                if (avisado)
                {
                    return;
                }

                avisado = true;
            }

            Log.Aviso("No se pudo leer el modo de acceso de los laboratorios (" + ex.Number + ": " + ex.Message
                + "). Se usa el codigo unico de cada profesor.");
        }
    }

    /// <summary>Lo que ModosDeAcceso.LeerAjustes lee de configuracion; null lo que no esta (o sin db/13).</summary>
    public class AjustesDeCodigos
    {
        /// <summary>La forma general guardada ("Codigos de las clases", arriba).</summary>
        public ModoCodigo? General;

        /// <summary>"Los docentes no ven los codigos" (CodigosSoloResponsables); con db/13 nunca es null.</summary>
        public bool? SoloResponsables;
    }

    /// <summary>
    /// "Los docentes no ven los codigos: solo la administracion y los
    /// responsables de laboratorio", para toda la escuela (casilla de "Codigos
    /// de las clases"): configuracion codigos_solo_responsables = '1' (db/13,
    /// sin migracion propia; sin la clave, como siempre). Vale para el codigo
    /// unico y para la generacion de codigos; sin codigos no hay nada que
    /// ocultar. Con ella el docente no ve el codigo en ningun sitio (su panel,
    /// Mi horario, Escuchar el codigo, sus bitacoras, en su equipo y en el del
    /// laboratorio) y se lo pide a la persona responsable del laboratorio, que
    /// entonces si lo ve, como la administracion (Permisos.VeCodigosDeProfesor).
    ///
    /// Es una regla de las pantallas, no de la base: la cuenta de MySQL de los
    /// equipos (digbit_equipo) sigue pudiendo leer los codigos, como antes.
    /// </summary>
    internal static class CodigosSoloResponsables
    {
        /// <summary>La clave en configuracion: '1' encendida; '0' o sin ella, apagada.</summary>
        public const string Clave = "codigos_solo_responsables";

        /// <summary>Lo que dice el docente en lugar del codigo.</summary>
        public const string PideElCodigo = "Pídele el código a la persona responsable del laboratorio.";

        // Lo ultimo que se leyo de la base (de otro hilo: el panel lee en segundo plano).
        private static volatile bool activa;

        /// <summary>
        /// Lo ultimo que se leyo (cada lectura de ModosDeAcceso.LeerAjustes: el
        /// panorama del panel cada 30 s, Laboratorios, Mi horario y Horarios).
        /// Falso hasta la primera lectura y sin db/13.
        /// </summary>
        public static bool Activa
        {
            get { return activa; }
        }

        public static bool Desde(string valor)
        {
            return (valor ?? "").Trim() == "1";
        }

        public static string Valor(bool encendida)
        {
            return encendida ? "1" : "0";
        }

        internal static void Recordar(bool encendida)
        {
            if (activa != encendida)
            {
                activa = encendida;
                Log.Info("Codigos de las clases: los docentes " + (encendida ? "NO ven" : "ven") + " los codigos (" + Clave + ").");
            }
        }
    }

    /// <summary>
    /// Los codigos de una sesion fuera del modo fijo: 5 cifras (10000 a 99999),
    /// faciles de dictar (las letras se confunden de viva voz: B/V, C/S/Z, M/N) y
    /// distintos a simple vista del codigo fijo, que lleva letras.
    /// </summary>
    internal static class CodigosDeSesion
    {
        public const int Largo = 5;
        private const uint Valores = 90000;
        // Para no sesgar: se descartan los valores del ultimo tramo incompleto.
        private const uint Limite = uint.MaxValue - (uint.MaxValue % Valores);
        // Seguro entre hilos (el panel del profesor genera desde Task.Run); el
        // Random estatico de CodigoGenerador no lo es.
        private static readonly RandomNumberGenerator Azar = new RNGCryptoServiceProvider();

        public static string Generar()
        {
            byte[] bytes = new byte[4];
            uint valor;
            do
            {
                Azar.GetBytes(bytes);
                valor = BitConverter.ToUInt32(bytes, 0);
            }
            while (valor >= Limite);

            return (10000 + valor % Valores).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Uno que no use ninguna otra sesion de ese dia (en ningun laboratorio)
        /// ni ninguna clase o profesor como codigo fijo: asi "es de otro
        /// laboratorio" solo tiene una lectura.
        /// </summary>
        public static string NuevoLibre(MySqlConnection conexion, DateTime fecha, MySqlTransaction transaccion = null)
        {
            string fijos = Esquema.CodigoPorProfesor(conexion, transaccion)
                ? " + (SELECT COUNT(*) FROM usuarios WHERE codigo_clase = @Codigo)"
                : "";
            for (int intento = 0; intento < 30; intento++)
            {
                string codigo = Generar();
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT (SELECT COUNT(*) FROM codigos_accesos WHERE codigo = @Codigo AND fecha = @Fecha)"
                    + " + (SELECT COUNT(*) FROM clases WHERE codigo = @Codigo)" + fijos, conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Codigo", codigo);
                    comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                    if (Convert.ToInt32(comando.ExecuteScalar()) == 0)
                    {
                        return codigo;
                    }
                }
            }

            // Llega a la vista ("No se pudo abrir la clase: ..."): con acentos, la causa y que hacer.
            throw new ErrorDeDatos("No se encontró un código libre para la sesión (todos los que se probaron ya estaban en uso hoy). "
                + "Vuelve a intentarlo en un momento.");
        }
    }

    /// <summary>
    /// Generacion de codigos (modo renovable, db/11): un codigo distinto para
    /// cada clase en cada dia, que queda fijado desde el lunes de su semana sin
    /// guardar nada por adelantado. Sale de un secreto de la escuela
    /// (configuracion, db/13), de la clase y de la fecha (HMAC-SHA256): asi el
    /// administrador los ve todos en Horarios y la sesion que se abre en la
    /// hora de la clase lleva ese mismo. Como nada se guarda antes, cambiar el
    /// horario a media semana no deja codigos viejos por ahi.
    ///
    /// Quien tenga las credenciales de MySQL de los equipos puede leer el
    /// secreto, igual que ya puede leer los codigos de los profesores y las
    /// sesiones de hoy: que solo los vea el administrador es una regla de las
    /// pantallas, no de la base.
    /// </summary>
    internal static class CodigosGenerados
    {
        /// <summary>La clave del secreto en configuracion.</summary>
        public const string ClaveSecreto = "codigos_secreto";

        // Si el codigo de una clase coincide con el de otra del mismo laboratorio y
        // dia, se prueba el siguiente; con 90000 valores casi nunca pasa de uno.
        private const int Vueltas = 64;

        private static readonly object candado = new object();
        // Por base de datos (servidor y nombre): una vez creado no cambia nunca.
        private static readonly Dictionary<string, byte[]> secretos = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private static bool avisado;

        /// <summary>
        /// El secreto de la escuela. Si aun no hay, lo crea quien pueda (el
        /// administrador; los equipos solo leen configuracion, db/07) con INSERT
        /// IGNORE y lo vuelve a leer: si dos lo crean a la vez, vale el primero.
        /// En solo consulta (el encargado de laboratorio) no se crea nada.
        /// Null sin db/13, si no hay y este usuario no puede crearlo, o si el
        /// guardado no sirve: entonces la sesion lleva un codigo al azar, como antes.
        /// </summary>
        public static byte[] Secreto(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            string llave = conexion.DataSource + "/" + conexion.Database;
            lock (candado)
            {
                byte[] guardado;
                if (secretos.TryGetValue(llave, out guardado))
                {
                    return guardado;
                }
            }

            try
            {
                if (!Esquema.Ajustes(conexion, transaccion))
                {
                    // Sin db/13, o este usuario no ve la tabla: si el administrador si
                    // la ve, sus codigos no coincidiran con los que se abran aqui.
                    Avisar("este usuario de MySQL no ve la tabla configuracion (falta db/13, o el permiso "
                        + "GRANT SELECT ON configuracion TO digbit_equipo): los codigos que vea el administrador no valdran en este equipo");
                    return null;
                }

                string valor = LeerSecreto(conexion, transaccion);
                // Con la cuenta de solo consulta (la sesion de un encargado) ni se
                // intenta el INSERT. Lo decide la cuenta de ESTA conexion, no quien
                // tiene la sesion: la cola del equipo envia con la cuenta del equipo.
                if (valor == null && CuentaDeConsulta.EsDeConsulta(conexion))
                {
                    Avisar("aun no hay secreto y con la cuenta de solo consulta no se crea; lo crea el administrador");
                    return null;
                }

                if (valor == null)
                {
                    byte[] nuevo = new byte[32];
                    using (RandomNumberGenerator azar = new RNGCryptoServiceProvider())
                    {
                        azar.GetBytes(nuevo);
                    }

                    try
                    {
                        using (MySqlCommand comando = new MySqlCommand(
                            "INSERT IGNORE INTO configuracion (clave, valor) VALUES (@Clave, @Valor)", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Clave", ClaveSecreto);
                            comando.Parameters.AddWithValue("@Valor", Convert.ToBase64String(nuevo));
                            if (comando.ExecuteNonQuery() == 1)
                            {
                                Log.Info("Se creo el secreto de los codigos generados cada semana.");
                            }
                        }
                    }
                    catch (MySqlException ex) when (ex.Number == 1142 || ex.Number == 1143)
                    {
                        // El usuario de los equipos: lo creara el administrador.
                        Avisar("aun no hay secreto y este usuario de MySQL no puede crearlo; lo crea el administrador al elegir "
                            + "ese modo o al ver el horario de un laboratorio que lo usa");
                        return null;
                    }

                    valor = LeerSecreto(conexion, transaccion);
                }

                byte[] bytes = valor == null ? null : Convert.FromBase64String(valor);
                if (bytes == null || bytes.Length < 16)
                {
                    Avisar("el secreto guardado en configuracion no sirve");
                    return null;
                }

                // Dentro de una transaccion, el que acaba de crearse aun podria
                // deshacerse: entonces no se recuerda.
                if (transaccion == null)
                {
                    lock (candado)
                    {
                        secretos[llave] = bytes;
                    }
                }

                return bytes;
            }
            catch (FormatException)
            {
                Avisar("el secreto guardado en configuracion no sirve");
                return null;
            }
            catch (MySqlException ex) when (ex.Number == 1054 || ex.Number == 1142 || ex.Number == 1143 || ex.Number == 1146)
            {
                Avisar("no se pudo leer el secreto (" + ex.Number + ": " + ex.Message + ")");
                return null;
            }
        }

        private static string LeerSecreto(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            using (MySqlCommand comando = new MySqlCommand("SELECT valor FROM configuracion WHERE clave = @Clave", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Clave", ClaveSecreto);
                object valor = comando.ExecuteScalar();
                string texto = valor == null || valor == DBNull.Value ? null : Convert.ToString(valor, CultureInfo.InvariantCulture).Trim();
                return string.IsNullOrEmpty(texto) ? null : texto;
            }
        }

        private static void Avisar(string que)
        {
            lock (candado)
            {
                if (avisado)
                {
                    return;
                }

                avisado = true;
            }

            Log.Aviso("Codigos generados cada semana: " + que + ". Mientras tanto, cada clase se abre con un codigo al azar.");
        }

        /// <summary>"f12" (franja 12) o "x34" (sesion extra 34).</summary>
        public static string Clave(bool esExtra, int id)
        {
            return (esExtra ? "x" : "f") + id.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>El codigo de una clase en una fecha en su vuelta (0, la primera): 5 cifras, de 10000 a 99999.</summary>
        public static string Calcular(byte[] secreto, bool esExtra, int id, DateTime fecha, int vuelta)
        {
            string mensaje = Clave(esExtra, id) + "@" + fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                + "#" + vuelta.ToString(CultureInfo.InvariantCulture);
            using (HMACSHA256 hmac = new HMACSHA256(secreto))
            {
                byte[] resumen = hmac.ComputeHash(Encoding.ASCII.GetBytes(mensaje));
                // 64 bits repartidos en 90000 valores: el sesgo es despreciable.
                ulong valor = BitConverter.ToUInt64(resumen, 0);
                return (10000 + valor % 90000UL).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Los de un laboratorio en un dia, por Clave: el de cada una de esas
        /// franjas y sesiones extra. En un mismo laboratorio y dia no se repiten:
        /// por orden (las franjas y luego las extras, cada una por su id), la que
        /// coincide con una anterior pasa a su siguiente vuelta. Una extra nueva
        /// va la ultima, asi que nunca cambia el codigo de las demas.
        /// </summary>
        public static Dictionary<string, string> DelDia(byte[] secreto, DateTime fecha, IEnumerable<int> franjas, IEnumerable<int> extras)
        {
            Dictionary<string, string> codigos = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> usados = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<KeyValuePair<bool, int>> orden = franjas.Distinct().OrderBy(id => id).Select(id => new KeyValuePair<bool, int>(false, id))
                .Concat(extras.Distinct().OrderBy(id => id).Select(id => new KeyValuePair<bool, int>(true, id)));
            foreach (KeyValuePair<bool, int> clase in orden)
            {
                string codigo = null;
                for (int vuelta = 0; vuelta < Vueltas; vuelta++)
                {
                    codigo = Calcular(secreto, clase.Key, clase.Value, fecha.Date, vuelta);
                    if (usados.Add(codigo))
                    {
                        break;
                    }
                }

                codigos[Clave(clase.Key, clase.Value)] = codigo;
            }

            return codigos;
        }

        /// <summary>
        /// Los de un laboratorio de esas fechas: fecha -> Clave -> codigo. Cuentan
        /// todas sus franjas de ese dia de la semana y todas sus extras de ese
        /// dia, esten o no activas, vigentes o canceladas: el codigo de una clase
        /// no debe cambiar porque otra se desactive. Null sin secreto.
        /// </summary>
        public static Dictionary<DateTime, Dictionary<string, string>> DeLaboratorio(MySqlConnection conexion, int idLaboratorio, DateTime desde, DateTime hasta,
            MySqlTransaction transaccion = null)
        {
            byte[] secreto = Secreto(conexion, transaccion);
            if (secreto == null)
            {
                return null;
            }

            // Dia de la semana (1 = lunes) e id de cada franja del laboratorio.
            List<KeyValuePair<int, int>> franjas = new List<KeyValuePair<int, int>>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idhorarios, dia_semana FROM horarios WHERE laboratorios_idlaboratorios = @Lab", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        franjas.Add(new KeyValuePair<int, int>(Convert.ToInt32(lector["dia_semana"]), Convert.ToInt32(lector["idhorarios"])));
                    }
                }
            }

            List<KeyValuePair<DateTime, int>> extras = new List<KeyValuePair<DateTime, int>>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idhorario_excepciones, fecha FROM horario_excepciones "
                + "WHERE tipo = 'extra' AND laboratorios_idlaboratorios = @Lab AND fecha BETWEEN @Desde AND @Hasta", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        extras.Add(new KeyValuePair<DateTime, int>(Convert.ToDateTime(lector["fecha"]).Date, Convert.ToInt32(lector["idhorario_excepciones"])));
                    }
                }
            }

            Dictionary<DateTime, Dictionary<string, string>> codigos = new Dictionary<DateTime, Dictionary<string, string>>();
            for (DateTime dia = desde.Date; dia <= hasta.Date; dia = dia.AddDays(1))
            {
                int diaSemana = ((int)dia.DayOfWeek + 6) % 7 + 1;
                DateTime fecha = dia;
                codigos[dia] = DelDia(secreto, dia, franjas.Where(f => f.Key == diaSemana).Select(f => f.Value),
                    extras.Where(x => x.Key == fecha).Select(x => x.Value));
            }

            return codigos;
        }

        /// <summary>
        /// Los de esos laboratorios en un dia, por laboratorio y Clave: lo mismo
        /// que DeLaboratorio de ese dia, pero de todos en dos consultas (el panel
        /// en vivo, con CodigosSoloResponsables, lo pide cada 30 s). Null sin secreto.
        /// </summary>
        public static Dictionary<int, Dictionary<string, string>> DelDiaDe(MySqlConnection conexion, DateTime fecha, ICollection<int> laboratorios)
        {
            if (laboratorios == null || laboratorios.Count == 0)
            {
                return new Dictionary<int, Dictionary<string, string>>();
            }

            byte[] secreto = Secreto(conexion);
            if (secreto == null)
            {
                return null;
            }

            string labs = string.Join(",", laboratorios.Distinct().Select(id => id.ToString(CultureInfo.InvariantCulture)));
            Dictionary<int, List<int>> franjas = laboratorios.Distinct().ToDictionary(id => id, id => new List<int>());
            Dictionary<int, List<int>> extras = laboratorios.Distinct().ToDictionary(id => id, id => new List<int>());
            int diaSemana = ((int)fecha.DayOfWeek + 6) % 7 + 1;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idhorarios, laboratorios_idlaboratorios FROM horarios WHERE dia_semana = @Dia AND laboratorios_idlaboratorios IN (" + labs + ")", conexion))
            {
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        franjas[Convert.ToInt32(lector["laboratorios_idlaboratorios"])].Add(Convert.ToInt32(lector["idhorarios"]));
                    }
                }
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idhorario_excepciones, laboratorios_idlaboratorios FROM horario_excepciones "
                + "WHERE tipo = 'extra' AND fecha = @Fecha AND laboratorios_idlaboratorios IN (" + labs + ")", conexion))
            {
                comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        extras[Convert.ToInt32(lector["laboratorios_idlaboratorios"])].Add(Convert.ToInt32(lector["idhorario_excepciones"]));
                    }
                }
            }

            return franjas.Keys.ToDictionary(id => id, id => DelDia(secreto, fecha.Date, franjas[id], extras[id]));
        }

        /// <summary>
        /// El de una franja o sesion extra en esa fecha, en el laboratorio en que
        /// esta ahora. Null sin secreto, si ya no existe o si la franja no es de
        /// ese dia de la semana.
        /// </summary>
        public static string De(MySqlConnection conexion, bool esExtra, int id, DateTime fecha, MySqlTransaction transaccion = null)
        {
            int laboratorio;
            return De(conexion, esExtra, id, fecha, transaccion, out laboratorio);
        }

        private static string De(MySqlConnection conexion, bool esExtra, int id, DateTime fecha, MySqlTransaction transaccion, out int laboratorio)
        {
            laboratorio = 0;
            if (Secreto(conexion, transaccion) == null)
            {
                return null;
            }

            object leido;
            using (MySqlCommand comando = new MySqlCommand(esExtra
                ? "SELECT laboratorios_idlaboratorios FROM horario_excepciones WHERE idhorario_excepciones = @Id AND tipo = 'extra'"
                : "SELECT laboratorios_idlaboratorios FROM horarios WHERE idhorarios = @Id", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                leido = comando.ExecuteScalar();
            }

            if (leido == null || leido == DBNull.Value)
            {
                return null;
            }

            laboratorio = Convert.ToInt32(leido);
            Dictionary<DateTime, Dictionary<string, string>> codigos = DeLaboratorio(conexion, laboratorio, fecha, fecha, transaccion);
            string codigo;
            return codigos != null && codigos[fecha.Date].TryGetValue(Clave(esExtra, id), out codigo) ? codigo : null;
        }

        /// <summary>
        /// El codigo con que se abre la sesion de esa clase en esa fecha: el
        /// generado, salvo que ya lo lleve otra sesion de ese laboratorio y dia
        /// (pasa si el horario cambio a media semana justo con un codigo que
        /// coincide): entonces null y el que abre usa uno al azar, porque dos
        /// clases seguidas con el mismo codigo podrian mandar al alumno a la
        /// equivocada. Null tambien sin secreto.
        /// </summary>
        public static string ParaSesion(MySqlConnection conexion, bool esExtra, int id, DateTime fecha)
        {
            int laboratorio;
            string codigo = De(conexion, esExtra, id, fecha, null, out laboratorio);
            if (codigo == null)
            {
                return null;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*) FROM codigos_accesos WHERE codigo = @Codigo AND fecha = @Fecha AND laboratorios_idlaboratorios = @Lab", conexion))
            {
                comando.Parameters.AddWithValue("@Codigo", codigo);
                comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                comando.Parameters.AddWithValue("@Lab", laboratorio);
                if (Convert.ToInt32(comando.ExecuteScalar()) == 0)
                {
                    return codigo;
                }
            }

            Log.Aviso("El codigo generado de la " + (esExtra ? "sesion extra " : "franja ") + id + " del " + fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                + " ya lo lleva otra sesion de ese laboratorio y dia (cambio el horario a media semana): se abre con uno al azar.");
            return null;
        }

        /// <summary>El lunes de la semana de esa fecha.</summary>
        public static DateTime Lunes(DateTime fecha)
        {
            return fecha.Date.AddDays(-(((int)fecha.DayOfWeek + 6) % 7));
        }

        /// <summary>Si los codigos de esa fecha ya se generaron: los de una semana, el lunes (hoy, segun el servidor).</summary>
        public static bool YaGenerados(DateTime fecha, DateTime hoy)
        {
            return Lunes(fecha) <= Lunes(hoy);
        }
    }

    /// <summary>
    /// Los codigos de las clases de un laboratorio con generacion de codigos
    /// en unas fechas, para el administrador (HorariosDatos.CodigosGeneradosDe):
    /// el de la sesion si ya se abrio, y si no, el generado.
    /// </summary>
    public class CodigosDeLaSemana
    {
        private readonly Dictionary<string, string> codigos = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>La fecha de hoy en el servidor.</summary>
        public DateTime Hoy;

        /// <summary>Sin secreto (falta db/13, o no se pudo crear): solo salen los de las sesiones ya abiertas.</summary>
        public bool SinSecreto;

        internal void Poner(bool esExtra, int id, DateTime fecha, string codigo)
        {
            codigos[Llave(esExtra, id, fecha)] = codigo;
        }

        /// <summary>El de esa franja en esa fecha, o null (su semana aun no empieza, o sin secreto ni sesion).</summary>
        public string DeFranja(int idFranja, DateTime fecha)
        {
            return Buscar(false, idFranja, fecha);
        }

        /// <summary>El de esa sesion extra (en su fecha), o null.</summary>
        public string DeExtra(int idExtra, DateTime fecha)
        {
            return Buscar(true, idExtra, fecha);
        }

        /// <summary>Si los de esa fecha ya se generaron (su semana ya empezo).</summary>
        public bool Generados(DateTime fecha)
        {
            return CodigosGenerados.YaGenerados(fecha, Hoy);
        }

        private string Buscar(bool esExtra, int id, DateTime fecha)
        {
            string codigo;
            return codigos.TryGetValue(Llave(esExtra, id, fecha), out codigo) ? codigo : null;
        }

        private static string Llave(bool esExtra, int id, DateTime fecha)
        {
            return CodigosGenerados.Clave(esExtra, id) + "@" + fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Lo que devuelve abrir la sesion de una clase en el modo renovable.</summary>
    public class AperturaSesion
    {
        /// <summary>La sesion de hoy de esa clase (codigos_accesos), o null si no se pudo abrir.</summary>
        public int? SesionId;
        /// <summary>Su codigo: el que se dicta a los alumnos.</summary>
        public string Codigo;
        /// <summary>Por que no se abrio, redactado para quien la pidio.</summary>
        public string Motivo;
        /// <summary>El profesor de la sesion (para no darle a uno el codigo de la clase de otro).</summary>
        public int ProfesorId;

        public bool Abierta
        {
            get { return SesionId.HasValue; }
        }
    }

    /// <summary>Lo que la pantalla del alumno necesita saber de su laboratorio ahora mismo.</summary>
    public class SituacionLaboratorio
    {
        public DateTime AhoraServidor;
        public ModoCodigo Modo;
        /// <summary>Agenda de hoy en este laboratorio, con la sesion de cada clase si ya existe.</summary>
        public List<SesionAgenda> Agenda = new List<SesionAgenda>();
        /// <summary>La base guarda los equipos de alrededor sin internet (db/10).</summary>
        public bool ConAvisosDeRed;
        /// <summary>Sin codigos y con db/14: sin clase abierta, el alumno registra uso libre.</summary>
        public bool ConUsoLibre;
        /// <summary>Este laboratorio esta deshabilitado (LaboratoriosDeshabilitados): aqui no se registra nada.</summary>
        public bool Deshabilitado;
        /// <summary>
        /// Los equipos que registraron bitacoras aqui hace poco, para marcarlos en
        /// el paso 3 (Consultas.EquiposDelLaboratorio). Null si no se leyeron.
        /// </summary>
        public List<string> Equipos;
    }
}
