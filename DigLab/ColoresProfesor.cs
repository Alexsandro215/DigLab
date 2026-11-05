using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigLab
{
    /// <summary>
    /// Un color por profesor, el mismo en Horarios, en la lista de Clases, en
    /// Profesores, en el panel en vivo y en "Mi horario". Antes era
    /// Paleta[id % 12]: con los 16 profesores del horario real, en cada
    /// laboratorio habia dos o tres que compartian color, y el color dejaba de
    /// decir de quien es una clase.
    ///
    /// El administrador puede ELEGIR el color de un profesor
    /// (usuarios.color_horario, db/12). Si no elige, sale solo: cada profesor
    /// tiene un turno (el orden en que se dio de alta su primera clase, ver
    /// HorariosDatos.DatosDeColores) y el turno elige un color de Paleta,
    /// escogidos a mano para que cada uno tenga un "nombre" distinto (salmon,
    /// celeste, lima, violeta...), alternando calidos y frios porque los
    /// profesores que se cargan juntos suelen compartir laboratorio. Se probo
    /// antes repartir el tono por el angulo aureo: con 15 profesores en un mismo
    /// laboratorio salian tres morados y tres rosas casi iguales. Pasados los de
    /// la paleta, se sigue con ese reparto.
    ///
    /// Un color elegido a mano solo mueve a quien lo tenia por su turno, y ese
    /// pasa al color que dejo libre quien lo eligio (un intercambio); los demas
    /// se quedan con el suyo, tambien cuando despues se eligen mas colores o
    /// entra un profesor nuevo. El que elige un profesor sin clases no mueve a
    /// nadie hasta que se le da la primera: entonces el que lo tenia pasa al
    /// color del turno del nuevo, de una sola vez.
    /// </summary>
    internal static class ColoresProfesor
    {
        private static readonly Color[] Paleta =
        {
            Color.FromArgb(250, 170, 160), Color.FromArgb(160, 210, 245), Color.FromArgb(200, 235, 140),
            Color.FromArgb(190, 160, 235), Color.FromArgb(255, 190, 120), Color.FromArgb(130, 215, 205),
            Color.FromArgb(245, 170, 210), Color.FromArgb(150, 170, 240), Color.FromArgb(250, 230, 120),
            Color.FromArgb(150, 220, 160), Color.FromArgb(235, 140, 200), Color.FromArgb(190, 205, 215),
            Color.FromArgb(230, 210, 170), Color.FromArgb(225, 200, 245), Color.FromArgb(210, 175, 140),
            Color.FromArgb(205, 210, 150), Color.FromArgb(255, 210, 180), Color.FromArgb(200, 240, 220)
        };

        // Para elegirlos por su nombre (en el mismo orden que Paleta).
        private static readonly string[] Nombres =
        {
            "salmón", "celeste", "lima", "violeta", "naranja", "turquesa",
            "rosa", "azul", "amarillo", "verde", "fucsia", "gris azulado",
            "arena", "lavanda", "café claro", "oliva", "durazno", "menta"
        };

        /// <summary>
        /// Con texto negro encima se tiene que leer bien: el color de la paleta
        /// que menos contrasta da unas 9 veces; menos de 7 (el nivel AAA de las
        /// pautas WCAG para texto normal) se aclara (ParaTextoNegro).
        /// </summary>
        public const double ContrasteMinimo = 7.0;

        private static readonly Regex Hexadecimal = new Regex("^#[0-9A-Fa-f]{6}$");

        /// <summary>Lo que se reparte de una vez; se sustituye entero y nunca se modifica despues.</summary>
        private sealed class Reparto
        {
            public readonly List<int> Orden;
            public readonly Dictionary<int, string> Elegidos;
            public readonly Dictionary<int, Color> Colores;

            public Reparto(List<int> orden, Dictionary<int, string> elegidos)
            {
                Orden = orden;
                Elegidos = elegidos;
                Colores = Calcular(orden, elegidos);
            }
        }

        // Lo leen a la vez la pantalla y el refresco en segundo plano del panel.
        private static volatile Reparto actual = new Reparto(new List<int>(), new Dictionary<int, string>());
        private static int version;

        /// <summary>
        /// Sube en cada Repartir. El panel lo anota antes de leer en segundo plano:
        /// si al llegar los datos ya cambio, otra pantalla reparto con datos mas
        /// nuevos y no hay que pisarlos.
        /// </summary>
        public static int Version
        {
            get { return System.Threading.Volatile.Read(ref version); }
        }

        /// <summary>
        /// El reparto cambio algun color. Lo lanza Repartir en el hilo que la
        /// llama (siempre el de la pantalla): las ventanas abiertas que copiaron
        /// un color al crear sus controles (leyenda, celdas) lo vuelven a poner.
        /// </summary>
        public static event Action Cambio;

        /// <summary>Los colores que se pueden elegir, en orden.</summary>
        public static IList<Color> Muestras
        {
            get { return Array.AsReadOnly(Paleta); }
        }

        /// <summary>
        /// Reparte los colores: los profesores con alguna clase, en el orden de su
        /// primera clase, y los colores elegidos a mano ('#RRGGBB' por id; un
        /// valor que no lo sea cuenta como automatico). Si la consulta falla, se
        /// quedan los de antes: no se llama.
        /// </summary>
        public static void Repartir(IEnumerable<int> profesoresEnOrden, IDictionary<int, string> elegidos)
        {
            List<int> orden = new List<int>();
            foreach (int id in profesoresEnOrden ?? Enumerable.Empty<int>())
            {
                if (!orden.Contains(id))
                {
                    orden.Add(id);
                }
            }

            Reparto anterior = actual;
            Reparto nuevo = new Reparto(orden, elegidos == null
                ? new Dictionary<int, string>()
                : new Dictionary<int, string>(elegidos));
            actual = nuevo;
            System.Threading.Interlocked.Increment(ref version);

            bool igual = anterior.Colores.Count == nuevo.Colores.Count
                && nuevo.Colores.All(kv => anterior.Colores.ContainsKey(kv.Key) && anterior.Colores[kv.Key].ToArgb() == kv.Value.ToArgb());
            Action aviso = Cambio;
            if (!igual && aviso != null)
            {
                aviso();
            }
        }

        public static Color De(int idProfesor)
        {
            Color color;
            if (actual.Colores.TryGetValue(idProfesor, out color))
            {
                return color;
            }

            // Sin reparto todavia, o un profesor sin clases ni color elegido: un
            // tono fijo por su id, fuera de la paleta para no imitar a uno que si la tiene.
            return Tono(Paleta.Length + Math.Abs(idProfesor));
        }

        /// <summary>
        /// El que tendria si no se le elige ninguno. Para uno nuevo o sin clases,
        /// el que le tocaria si hoy se le diera su primera clase.
        /// </summary>
        public static Color Automatico(int idProfesor)
        {
            Reparto r = actual;
            List<int> orden = new List<int>(r.Orden);
            int clave = idProfesor;
            if (idProfesor <= 0 || !orden.Contains(idProfesor))
            {
                clave = int.MinValue;
                orden.Add(clave);
            }

            Dictionary<int, string> elegidos = r.Elegidos.Where(kv => kv.Key != idProfesor).ToDictionary(kv => kv.Key, kv => kv.Value);
            return Calcular(orden, elegidos)[clave];
        }

        /// <summary>True si ya tiene color: elegido, o el de su turno porque tiene alguna clase.</summary>
        public static bool TieneColor(int idProfesor)
        {
            return actual.Colores.ContainsKey(idProfesor);
        }

        /// <summary>True si tiene turno: alguna clase (activa o no) a su nombre.</summary>
        public static bool TieneTurno(int idProfesor)
        {
            return idProfesor > 0 && actual.Orden.Contains(idProfesor);
        }

        /// <summary>
        /// True si sale con el color de su turno: no eligio uno ni se lo quito
        /// otro (el desplazado tiene el que dejo libre quien eligio el suyo).
        /// </summary>
        public static bool TieneElDeSuTurno(int idProfesor)
        {
            Reparto r = actual;
            int turno = r.Orden.IndexOf(idProfesor);
            Color suyo;
            Color color;
            string elegido;
            return turno >= 0
                && !(r.Elegidos.TryGetValue(idProfesor, out elegido) && DesdeTexto(elegido, out suyo))
                && r.Colores.TryGetValue(idProfesor, out color)
                && color.ToArgb() == Tono(turno).ToArgb();
        }

        /// <summary>Los profesores (por id) que hoy salen con ese color, sin contar a uno.</summary>
        public static List<int> QuienesUsan(Color color, int salvoProfesor)
        {
            return actual.Colores.Where(kv => kv.Key != salvoProfesor && kv.Value.ToArgb() == color.ToArgb()).Select(kv => kv.Key).ToList();
        }

        /// <summary>Los que ELIGIERON ese color (no lo tienen por su turno), sin contar a uno.</summary>
        public static List<int> QuienesEligieron(Color color, int salvoProfesor)
        {
            List<int> ids = new List<int>();
            foreach (KeyValuePair<int, string> elegido in actual.Elegidos)
            {
                Color suyo;
                if (elegido.Key != salvoProfesor && DesdeTexto(elegido.Value, out suyo) && suyo.ToArgb() == color.ToArgb())
                {
                    ids.Add(elegido.Key);
                }
            }

            return ids;
        }

        /// <summary>
        /// Que pasaria si ese profesor (0 si es nuevo) se quedara con ese color
        /// (null = automatico): los otros profesores a los que les cambiaria, con
        /// el color que tendrian. Para avisar en el dialogo antes de guardar. Uno
        /// sin clases no mueve a nadie hasta que tiene la primera: para el se
        /// calcula lo que pasara al darsela (TieneTurno dice cual de los dos es).
        /// </summary>
        public static List<KeyValuePair<int, Color>> Simular(int idProfesor, Color? color)
        {
            Reparto r = actual;
            int clave = idProfesor > 0 ? idProfesor : int.MinValue;
            List<int> orden = r.Orden;
            if (!orden.Contains(clave))
            {
                orden = new List<int>(orden) { clave };
            }

            Dictionary<int, string> elegidos = new Dictionary<int, string>(r.Elegidos);
            if (color.HasValue)
            {
                elegidos[clave] = ATexto(color.Value);
            }
            else
            {
                elegidos.Remove(clave);
            }

            List<KeyValuePair<int, Color>> cambian = new List<KeyValuePair<int, Color>>();
            foreach (KeyValuePair<int, Color> despues in Calcular(orden, elegidos))
            {
                Color antes;
                if (despues.Key != clave && r.Colores.TryGetValue(despues.Key, out antes) && antes.ToArgb() != despues.Value.ToArgb())
                {
                    cambian.Add(despues);
                }
            }

            return cambian;
        }

        /// <summary>"salmón", o null si no es de la paleta.</summary>
        public static string Nombre(Color color)
        {
            int i = Array.FindIndex(Paleta, c => c.ToArgb() == color.ToArgb());
            return i < 0 ? null : Nombres[i];
        }

        /// <summary>'#RRGGBB' -> color. False con cualquier otra cosa (null, vacio, mal escrito).</summary>
        public static bool DesdeTexto(string texto, out Color color)
        {
            color = Color.Empty;
            texto = (texto ?? "").Trim();
            if (!Hexadecimal.IsMatch(texto))
            {
                return false;
            }

            int valor = int.Parse(texto.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            color = Color.FromArgb((valor >> 16) & 0xFF, (valor >> 8) & 0xFF, valor & 0xFF);
            return true;
        }

        /// <summary>Lo que se guarda: '#RRGGBB'.</summary>
        public static string ATexto(Color color)
        {
            return "#" + color.R.ToString("X2", CultureInfo.InvariantCulture) + color.G.ToString("X2", CultureInfo.InvariantCulture)
                + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>Cuanto contrasta con el texto negro (de 1 a 21, segun WCAG).</summary>
        public static double ContrasteConNegro(Color color)
        {
            return (Luminancia(color) + 0.05) / 0.05;
        }

        /// <summary>
        /// El mismo color, aclarado (mezclado con blanco) lo justo para que el
        /// texto negro se lea: los cuadros siempre llevan texto negro.
        /// </summary>
        public static Color ParaTextoNegro(Color color)
        {
            Color resultado = Color.FromArgb(color.R, color.G, color.B);
            for (int paso = 1; paso <= 20 && ContrasteConNegro(resultado) < ContrasteMinimo; paso++)
            {
                double blanco = paso / 20.0;
                resultado = Color.FromArgb(
                    (int)Math.Round(color.R + (255 - color.R) * blanco),
                    (int)Math.Round(color.G + (255 - color.G) * blanco),
                    (int)Math.Round(color.B + (255 - color.B) * blanco));
            }

            return resultado;
        }

        /// <summary>
        /// Primero los elegidos; despues cada uno el de su turno. Al que le
        /// eligieron el suyo (un "desplazado") le toca el que dejo libre quien lo
        /// eligio: el color del turno de ese profesor; si a su vez ese lo eligio
        /// otro, el del turno de ese otro, y asi. Asi el color de un desplazado
        /// solo cambia si cambia quien se lo quito: no depende de los otros
        /// desplazados ni de los profesores que entren despues.
        ///
        /// Solo desplaza el color elegido de un profesor con turno. El de uno sin
        /// clases es suyo (en Profesores sale con el), pero no sale en ningun
        /// horario: si moviera ya al que lo tiene, la cadena no tendria un turno
        /// donde acabar y le tocaria un color de fuera de la paleta, casi igual a
        /// otro (y cambiaria otra vez al darle su primera clase).
        /// </summary>
        internal static Dictionary<int, Color> Calcular(List<int> orden, Dictionary<int, string> elegidos)
        {
            return Calcular(orden, elegidos, false);
        }

        /// <summary>
        /// Lo mismo; con sinTonosDeAlerta, los colores automaticos saltan los que
        /// parecen un aviso (el salmon, el rosa y el fucsia: junto al rojo de "Sin
        /// bitacoras" y de "Cancelada", una clase de ese color parecia cancelada).
        /// Lo usan las clases de cada docente (PersonalizacionClases); los colores
        /// ya elegidos no cambian, y los de los profesores en Horarios tampoco.
        /// </summary>
        internal static Dictionary<int, Color> Calcular(List<int> orden, Dictionary<int, string> elegidos, bool sinTonosDeAlerta)
        {
            // El color de cada turno: el de siempre o, para las clases de un docente, sin los de alerta.
            Func<int, Color> tonoDe = sinTonosDeAlerta ? (Func<int, Color>)TonoSinAlerta : Tono;
            Dictionary<int, int> turnos = new Dictionary<int, int>();
            for (int i = 0; i < orden.Count; i++)
            {
                turnos[orden[i]] = i;
            }

            Dictionary<int, Color> colores = new Dictionary<int, Color>();
            foreach (KeyValuePair<int, string> elegido in elegidos)
            {
                Color color;
                if (DesdeTexto(elegido.Value, out color))
                {
                    colores[elegido.Key] = color;
                }
            }

            // Quien eligio cada color, entre los que tienen turno: si lo
            // eligieron varios, el de turno mas bajo.
            List<KeyValuePair<int, Color>> conTurno = colores.Where(kv => turnos.ContainsKey(kv.Key)).OrderBy(kv => turnos[kv.Key]).ToList();
            Dictionary<int, int> quienEligio = new Dictionary<int, int>();
            foreach (KeyValuePair<int, Color> elegido in conTurno)
            {
                if (!quienEligio.ContainsKey(elegido.Value.ToArgb()))
                {
                    quienEligio[elegido.Value.ToArgb()] = elegido.Key;
                }
            }

            HashSet<int> usados = new HashSet<int>(conTurno.Select(kv => kv.Value.ToArgb()));
            List<int> desplazados = new List<int>();
            for (int turno = 0; turno < orden.Count; turno++)
            {
                int id = orden[turno];
                if (colores.ContainsKey(id))
                {
                    continue;
                }

                Color suyo = tonoDe(turno);
                if (quienEligio.ContainsKey(suyo.ToArgb()))
                {
                    desplazados.Add(id);
                    continue;
                }

                colores[id] = suyo;
                usados.Add(suyo.ToArgb());
            }

            foreach (int id in desplazados)
            {
                Color? destino = null;
                Color actualEnCadena = tonoDe(turnos[id]);
                HashSet<int> vistos = new HashSet<int>();
                while (true)
                {
                    int quien = quienEligio[actualEnCadena.ToArgb()];
                    int turnoDeQuien;
                    if (!vistos.Add(quien) || !turnos.TryGetValue(quien, out turnoDeQuien))
                    {
                        break;
                    }

                    Color libre = tonoDe(turnoDeQuien);
                    if (!quienEligio.ContainsKey(libre.ToArgb()))
                    {
                        if (!usados.Contains(libre.ToArgb()))
                        {
                            destino = libre;
                        }

                        break;
                    }

                    actualEnCadena = libre;
                }

                if (!destino.HasValue)
                {
                    // Solo si dos turnos dieran el mismo color (no pasa con la
                    // paleta ni con el angulo aureo en miles de turnos): uno que
                    // solo depende de su turno, lejos de los turnos reales.
                    int turnoPropio = 1000 + turnos[id];
                    Color propio;
                    do
                    {
                        propio = tonoDe(turnoPropio);
                        turnoPropio += 37;
                    }
                    while (usados.Contains(propio.ToArgb()) || quienEligio.ContainsKey(propio.ToArgb()));

                    destino = propio;
                }

                colores[id] = destino.Value;
                usados.Add(destino.Value.ToArgb());
            }

            return colores;
        }

        // Los de la paleta que parecen un aviso: salmon, rosa y fucsia (TonoSinAlerta).
        private static readonly int[] DeAlerta = { 0, 6, 10 };

        /// <summary>
        /// Como Tono, sin los colores que parecen un aviso: la paleta sin el salmon,
        /// el rosa y el fucsia y, despues, el angulo aureo sin los tonos rojizos o
        /// rosados (menos de 30 o desde 300 grados).
        /// </summary>
        private static Color TonoSinAlerta(int turno)
        {
            int enPaleta = Paleta.Length - DeAlerta.Length;
            if (turno < enPaleta)
            {
                return Paleta.Where((c, i) => !DeAlerta.Contains(i)).ElementAt(turno);
            }

            int faltan = turno - enPaleta;
            for (int k = Paleta.Length; ; k++)
            {
                double tono = (k * 137.508) % 360.0;
                if (tono < 30.0 || tono >= 300.0)
                {
                    continue;
                }

                if (faltan-- == 0)
                {
                    return Tono(k);
                }
            }
        }

        /// <summary>
        /// Un color en el que se lee bien el texto negro: de la paleta, y despues
        /// por el angulo aureo con la luminosidad alterna.
        /// </summary>
        private static Color Tono(int turno)
        {
            if (turno < Paleta.Length)
            {
                return Paleta[turno];
            }

            double tono = (turno * 137.508) % 360.0;
            double luz = turno % 2 == 0 ? 0.84 : 0.77;
            return DesdeHsl(tono, 0.65, luz);
        }

        private static Color DesdeHsl(double tono, double saturacion, double luz)
        {
            double croma = (1 - Math.Abs(2 * luz - 1)) * saturacion;
            double h = tono / 60.0;
            double x = croma * (1 - Math.Abs(h % 2 - 1));
            double r = 0, g = 0, b = 0;
            if (h < 1) { r = croma; g = x; }
            else if (h < 2) { r = x; g = croma; }
            else if (h < 3) { g = croma; b = x; }
            else if (h < 4) { g = x; b = croma; }
            else if (h < 5) { r = x; b = croma; }
            else { r = croma; b = x; }

            double m = luz - croma / 2;
            return Color.FromArgb(Canal(r + m), Canal(g + m), Canal(b + m));
        }

        private static int Canal(double valor)
        {
            return (int)Math.Round(Math.Max(0, Math.Min(1, valor)) * 255);
        }

        /// <summary>Luminancia relativa (WCAG 2): 0 negro, 1 blanco.</summary>
        private static double Luminancia(Color color)
        {
            return 0.2126 * Lineal(color.R) + 0.7152 * Lineal(color.G) + 0.0722 * Lineal(color.B);
        }

        private static double Lineal(int canal)
        {
            double c = canal / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }
}
