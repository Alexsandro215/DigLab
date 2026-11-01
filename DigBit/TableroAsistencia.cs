using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DigBit.conexion;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// El tablero de "Mis grupos": cuatro tarjetas encima de la hoja de
    /// asistencia con lo que el profesor busca de un vistazo. T1, la asistencia
    /// del grupo (el mismo % de la esquina de la hoja) con una minigrafica de
    /// cada clase; T2, quienes no tienen derecho a evaluacion (menos del 80 %);
    /// T3, quienes faltaron a las dos ultimas clases que cuentan; T4, la clase de
    /// hoy (antes de empezar, en curso, en la pausa entre dos bloques o ya
    /// terminada) o, si hoy no hay, la ultima. Todo sale del resumen de la lista
    /// (ResumenAsistencia) con las reglas de la hoja: lo pendiente de hoy (P) no
    /// cuenta ni como asistencia ni como falta, una asistencia quitada es falta y
    /// una que dio el profesor sin bitacora (db/22) es asistencia (las ayudas
    /// dicen cuantas fueron).
    ///
    /// Las tarjetas con alumnos que atender se pulsan (raton, Enter o Espacio;
    /// las flechas pasan de una a otra) y la ventana los marca en la hoja
    /// (TarjetaPulsada, IdsDe y Marcada). Mostrar arma los textos y una firma con
    /// todos ellos: si la firma no cambio, no repinta, asi la relectura de cada
    /// 30 s no parpadea. ActualizarHora solo rehace la de hoy (su reloj). Con 0
    /// alumnos, T1, T2 y T3 se juntan en una sola tarjeta; con un error o sin
    /// grupos, una sola tarjeta del ancho del tablero lo dice.
    /// </summary>
    internal class TableroAsistencia : Control
    {
        private const string SinDato = "—";
        private const int Minimo = ResumenAsistencia.Minimo;
        private const int MaximoEnAyuda = 25;
        private const int RadioTarjeta = 10;
        private const int AltoPastilla = 20;
        private const int AltoBarra = 8;
        private const int RadioBarra = 4;
        private const int EntreTramos = 2;
        private const int TramoMinimo = 3;
        private const string QuitarMarca = "Pulsa otra vez para quitar la marca.";
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

        // Todo se mide asi y con un Graphics (sin el, MeasureText da 7 px de mas)
        // y se pinta con los mismos indicadores, para que empiece donde se midio.
        private const TextFormatFlags SinMargen = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        private const TextFormatFlags Renglon = SinMargen | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis;
        private const TextFormatFlags Centrado = SinMargen | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

        // La tarjeta: las de las cifras del panel del profesor (FondoFicha y BordeFicha de TarjetaCodigo).
        private static readonly Color FondoFicha = Color.FromArgb(244, 250, 244);
        private static readonly Color BordeFicha = Color.FromArgb(205, 225, 205);
        private static readonly Color FondoEncima = Color.FromArgb(236, 246, 236);
        private static readonly Color FondoMarcada = Color.FromArgb(226, 242, 226);
        private static readonly Color VerdeExcel = Color.FromArgb(33, 115, 70);
        private static readonly Color Naranja = Color.FromArgb(237, 125, 49);
        private static readonly Color ColorRotulo = Color.FromArgb(90, 115, 90);
        private static readonly Color ColorTexto = Color.FromArgb(75, 75, 75);
        private static readonly Color ColorLinea = Color.FromArgb(60, 60, 60);
        // La cifra: verde si va bien (o es una cuenta), rojo si hay que atender, gris sin dato.
        private static readonly Color Verde = Color.FromArgb(46, 125, 50);
        private static readonly Color Rojo = Color.FromArgb(156, 0, 6);
        private static readonly Color Gris = Color.FromArgb(150, 150, 150);
        private static readonly Color ColorLeyendo = Color.FromArgb(200, 200, 200);
        private static readonly Color GrisSinAlumnos = Color.FromArgb(115, 125, 115);
        // Los de la hoja (HojaAsistencia): P pendiente, F naranja (sin estar en el salon) y F rosa (falta).
        private static readonly Color HoyFondo = Color.FromArgb(255, 235, 156);
        private static readonly Color HoyTexto = Color.FromArgb(156, 87, 0);
        private static readonly Color BordeHoy = Color.FromArgb(214, 180, 70);
        private static readonly Color RetiradaFondo = Color.FromArgb(248, 203, 173);
        private static readonly Color RetiradaTexto = Color.FromArgb(131, 60, 12);
        private static readonly Color FaltaFondo = Color.FromArgb(255, 199, 206);
        private static readonly Color FaltaTexto = Color.FromArgb(156, 0, 6);
        private static readonly Color BordeFalta = Color.FromArgb(225, 150, 160);
        private static readonly Color BordeMuestra = Color.FromArgb(128, 128, 128);
        // La minigrafica de T1.
        private static readonly Color ColumnaAnterior = Color.FromArgb(176, 203, 184);
        private static readonly Color LineaBase = Color.FromArgb(200, 200, 200);
        private static readonly Color LineaCien = Color.FromArgb(225, 232, 225);
        // Las pastillas de T4 ("En curso" va en DarkGreen con letra blanca).
        private static readonly Color FondoHoy = Color.FromArgb(226, 242, 226);
        private static readonly Color FondoTermino = Color.FromArgb(236, 236, 236);
        private static readonly Color LetraTermino = Color.FromArgb(90, 90, 90);
        // Los avisos del ancho del tablero.
        private static readonly Color FondoError = Color.FromArgb(253, 228, 224);
        private static readonly Color BordeError = Color.FromArgb(198, 40, 40);
        private static readonly Color TextoError = Color.FromArgb(183, 28, 28);
        private static readonly Color FondoSinGrupos = Color.FromArgb(245, 245, 245);
        private static readonly Color BordeSinGrupos = Color.FromArgb(215, 215, 215);
        private static readonly Color TextoSinGrupos = Color.FromArgb(105, 105, 105);

        /// <summary>Donde va cada cosa dentro de una tarjeta (x e y relativos a ella), en cada modo.</summary>
        private sealed class Medidas
        {
            public int Separacion;
            public int Lado;
            public int YRotulo;
            public int YCifra;
            public int AltoCifra;
            public int EntreComplemento;
            public int YComplemento;
            public int TrasCifra;
            public int ZonaArriba;
            public int ZonaAbajo;
            // Los dos renglones de la zona derecha de T4; con uno solo, en RenglonSolo.
            public int[] Renglones;
            public int RenglonSolo;
            public int YPie1;
            // Sin segundo renglon del pie (compacto): -1.
            public int YPie2;
            public int YPastilla;
            public int LadoMuestra;
            public int YBarra;
            public int ReservaBarra;
            public int AntesDelTexto;
            public int AltoGrafica;
        }

        // El complemento y el renglon solo de T4 van con la base de la cifra
        // (medido a 96 ppp: la de Century Gothic 22 negrita cae 15 px mas abajo
        // que la de 11 y 18 mas que la de 9; la de 18 negrita, 10 mas que las de
        // 9.5 y 8.5).
        private static readonly Medidas Amplias = new Medidas
        {
            Separacion = 14, Lado = 14, YRotulo = 10, YCifra = 28, AltoCifra = 37, EntreComplemento = 6, YComplemento = 43,
            TrasCifra = 20, ZonaArriba = 30, ZonaAbajo = 64, Renglones = new[] { 30, 47 }, RenglonSolo = 46, YPie1 = 70, YPie2 = 86,
            YPastilla = 8, LadoMuestra = 12, YBarra = 72, ReservaBarra = 97, AntesDelTexto = 10, AltoGrafica = 30
        };

        private static readonly Medidas Compactas = new Medidas
        {
            Separacion = 12, Lado = 12, YRotulo = 8, YCifra = 24, AltoCifra = 28, EntreComplemento = 5, YComplemento = 34,
            TrasCifra = 16, ZonaArriba = 24, ZonaAbajo = 54, Renglones = new[] { 24, 39 }, RenglonSolo = 34, YPie1 = 60, YPie2 = -1,
            YPastilla = 6, LadoMuestra = 11, YBarra = 64, ReservaBarra = 95, AntesDelTexto = 8, AltoGrafica = 22
        };

        private enum Vista { Leyendo, Datos, Error, SinGrupos }

        private enum TipoMuestra { Ninguna, Pendiente, Retirada, Falta }

        /// <summary>Un renglon de la zona derecha de T4: su cuadrito (como la casilla de la hoja) y su texto.</summary>
        private sealed class Linea
        {
            public TipoMuestra Muestra;
            public string Texto;
            // Si Texto no cabe (compacto): este.
            public string Corto;
            public string Mostrado;
            public Color Color = ColorLinea;

            public Linea(string texto, string corto = null)
            {
                Texto = texto;
                Corto = corto;
                Mostrado = texto;
            }

            public Linea(TipoMuestra muestra, string texto) : this(texto)
            {
                Muestra = muestra;
            }
        }

        /// <summary>La barra de T4: verde con asistencia, naranja sin estar en el salon y la pista con lo que queda.</summary>
        private sealed class Barra
        {
            public int Asistieron;
            public int Retiradas;
            public int Total;
            // La clase sigue: la pista (los pendientes) es amarilla; si ya termino, rosa (los que faltaron).
            public bool Sigue;
            // "quedan 17 min" o "sigue 02:00", a su derecha; sin texto, la barra va a todo lo ancho.
            public string Derecha;
        }

        /// <summary>Una columna de la minigrafica de T1 (una clase).</summary>
        private sealed class Columna
        {
            public Rectangle Rect;
            public Rectangle Zona;
            public Color Relleno;
            public bool EnCurso;
            public string Ayuda;
        }

        /// <summary>Lo que dice una tarjeta y, tras Colocar, donde va cada cosa.</summary>
        private sealed class Tarjeta
        {
            // 1 a 4 (la que junta T1, T2 y T3 sin alumnos es la 1); 0, un aviso.
            public readonly int Numero;
            public readonly Rectangle Rect;
            public string Rotulo = "";
            public string Cifra = "";
            public Color ColorCifra = Gris;
            // "Aun sin alumnos": la letra mas chica, centrada en el alto de la cifra.
            public bool CifraChica;
            public string Complemento = "";
            public string Pastilla;
            public Color FondoPastilla;
            public Color LetraPastilla;
            // La clase de hoy en curso: borde DarkGreen de 2 px.
            public bool EnCurso;
            public List<Linea> Lineas = new List<Linea>();
            public Barra Barra;
            // T1: las clases de la minigrafica (Colocar arma sus columnas).
            public List<AsistenciaDelDia> Dias;
            public List<Columna> Columnas;
            // T2 y T3: los nombres, en su orden (Colocar arma el pie con los que caben).
            public List<string> Nombres;
            public string Pie1 = "";
            public string Pie2 = "";
            public string Ayuda;
            // Lo que hace pulsarla, al final de la ayuda (si se pulsa y no esta marcada).
            public string AyudaPulsar;
            public bool SePulsa;
            public List<int> Ids = new List<int>();
            public string Accesible = "";
            // Un aviso del ancho del tablero (error o sin grupos), centrado y partido en renglones.
            public string Aviso;
            public Color FondoAviso;
            public Color BordeAviso;
            public Color LetraAviso;

            public Rectangle RectPastilla;
            public Rectangle RectRotulo;
            public Rectangle RectCifra;
            public Rectangle RectComplemento;
            public Rectangle RectZona;
            public Rectangle RectGrafica;
            public Rectangle RectBarra;
            public Rectangle RectDerecha;
            public Rectangle RectPie1;
            public Rectangle RectPie2;

            public Tarjeta(int numero, Rectangle rect)
            {
                Numero = numero;
                Rect = rect;
            }

            /// <summary>Todos sus textos y numeros: si no cambian, no hace falta repintarla.</summary>
            public string Firma()
            {
                StringBuilder s = new StringBuilder();
                s.Append(Numero).Append('|').Append(Rect.X).Append(',').Append(Rect.Width).Append(',').Append(Rect.Height)
                    .Append('|').Append(Rotulo).Append('|').Append(Cifra).Append('|').Append(ColorCifra.ToArgb()).Append('|').Append(CifraChica)
                    .Append('|').Append(Complemento).Append('|').Append(Pastilla).Append('|').Append(EnCurso)
                    .Append('|').Append(Pie1).Append('|').Append(Pie2).Append('|').Append(SePulsa).Append('|').Append(Aviso)
                    .Append('|').Append(Ayuda).Append('|').Append(AyudaPulsar);
                foreach (Linea linea in Lineas)
                {
                    s.Append("|l").Append((int)linea.Muestra).Append(linea.Mostrado).Append(linea.Color.ToArgb());
                }

                if (Barra != null)
                {
                    s.Append("|b").Append(Barra.Asistieron).Append(',').Append(Barra.Retiradas).Append(',').Append(Barra.Total)
                        .Append(',').Append(Barra.Sigue).Append(',').Append(Barra.Derecha);
                }

                if (Columnas != null)
                {
                    foreach (Columna c in Columnas)
                    {
                        s.Append("|c").Append(c.Rect.X).Append(',').Append(c.Rect.Height).Append(',').Append(c.Relleno.ToArgb()).Append(c.Ayuda);
                    }
                }

                return s.ToString();
            }
        }

        private readonly DrawingFont fuenteRotulo = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private readonly DrawingFont fuenteCifra = new DrawingFont("Century Gothic", 22F, FontStyle.Bold);
        private readonly DrawingFont fuenteCifraCompacta = new DrawingFont("Century Gothic", 18F, FontStyle.Bold);
        private readonly DrawingFont fuenteComplemento = new DrawingFont("Century Gothic", 11F);
        private readonly DrawingFont fuenteComplementoCompacta = new DrawingFont("Century Gothic", 9.5F);
        private readonly DrawingFont fuentePie = new DrawingFont("Century Gothic", 8.5F);
        private readonly DrawingFont fuenteLinea = new DrawingFont("Century Gothic", 9F);
        private readonly DrawingFont fuenteQuedan = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private readonly DrawingFont fuentePastilla = new DrawingFont("Century Gothic", 8.25F, FontStyle.Bold);
        private readonly DrawingFont fuenteMuestra = new DrawingFont("Calibri", 8F, FontStyle.Bold);
        private readonly DrawingFont fuenteSinAlumnos = new DrawingFont("Century Gothic", 14F, FontStyle.Bold);
        private readonly ToolTip ayuda = new ToolTip { InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 20000 };

        private Vista vista = Vista.Leyendo;
        private ResumenAsistencia resumen;
        private List<TramoHorario> bloques = new List<TramoHorario>();
        private TimeSpan ahora;
        private string textoError = "";
        private List<Tarjeta> tarjetas = new List<Tarjeta>();
        private string firmaPintada;
        private bool amplio;
        // La ve otra persona (la ficha del docente): "Su docente le dio..." en vez de "Le diste...".
        private bool enTerceraPersona;
        private int? marcada;
        // La tarjeta bajo el raton y la que tiene el foco del teclado (su Numero; 0, ninguna).
        private int encima;
        private int foco;
        private bool ratonDentro;
        private string textoAyuda;

        /// <summary>Pulsaron esa tarjeta (2, 3 o 4; con el raton, Enter o Espacio): la ventana marca o quita la marca.</summary>
        public event Action<int> TarjetaPulsada;

        public TableroAsistencia()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            // Dos clics seguidos son dos pulsaciones (marcar y quitar la marca), no un doble clic.
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            BackColor = Color.White;
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = "Leyendo la lista...";
        }

        /// <summary>La ve otra persona y no su docente (la ficha del docente): "Su docente le dio..." en vez de "Le diste...". Antes de Mostrar.</summary>
        public bool EnTerceraPersona
        {
            get { return enTerceraPersona; }
            set { enTerceraPersona = value; }
        }

        /// <summary>Tarjetas de 108 px (a partir de 1600 x 860) o compactas de 84; lo decide la ventana en cada Resize.</summary>
        public bool Amplio
        {
            get
            {
                return amplio;
            }

            set
            {
                if (value == amplio)
                {
                    return;
                }

                amplio = value;
                Rehacer(true);
            }
        }

        /// <summary>La tarjeta cuyos alumnos estan marcados en la hoja (1 a 4), o null. Al ponerla, se repinta.</summary>
        public int? Marcada
        {
            get
            {
                return marcada;
            }

            set
            {
                int? valor = value.HasValue && value.Value >= 1 && value.Value <= 4 ? value : null;
                if (valor == marcada)
                {
                    return;
                }

                marcada = valor;
                Invalidate();
                // Su ayuda termina distinto ("Pulsa otra vez para quitar la marca.").
                RefrescarRaton();
            }
        }

        /// <summary>Al elegir otro grupo o con Actualizar: los cuatro rotulos con "—" en gris claro, sin pies ni pastillas.</summary>
        public void MostrarLeyendo()
        {
            vista = Vista.Leyendo;
            resumen = null;
            marcada = null;
            Rehacer(false);
        }

        /// <summary>
        /// Lo leido de la clase elegida. Si todos sus textos y numeros son los de
        /// antes (la relectura de cada 30 s sin cambios), no repinta.
        /// </summary>
        /// <param name="bloquesDeHoy">Los bloques de hoy de la clase, en orden (vacia si hoy no le toca).</param>
        /// <param name="ahora">La hora del servidor.</param>
        public void Mostrar(ResumenAsistencia r, IList<TramoHorario> bloquesDeHoy, TimeSpan ahora)
        {
            if (r == null || r.Lista == null)
            {
                MostrarLeyendo();
                return;
            }

            vista = Vista.Datos;
            resumen = r;
            bloques = bloquesDeHoy == null
                ? new List<TramoHorario>()
                : bloquesDeHoy.Where(b => b != null).OrderBy(b => b.Inicio).ToList();
            this.ahora = ahora;
            Rehacer(false);
        }

        /// <summary>Solo rehace la de hoy (en curso, pausa, terminada; "quedan" o "empieza en"), sin leer la base.</summary>
        public void ActualizarHora(TimeSpan ahora)
        {
            this.ahora = ahora;
            if (vista != Vista.Datos || resumen == null || tarjetas.Count == 0)
            {
                return;
            }

            int indice = tarjetas.FindIndex(t => t.Numero == 4);
            if (indice < 0)
            {
                return;
            }

            Tarjeta antes = tarjetas[indice];
            Tarjeta nueva = ArmarHoy(antes.Rect);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                Colocar(g, nueva);
            }

            tarjetas[indice] = nueva;
            if (nueva.Firma() != antes.Firma())
            {
                Invalidate(nueva.Rect);
            }

            firmaPintada = FirmaDelTablero();
            Despues();
        }

        /// <summary>No se pudo leer (la lectura no callada): una sola tarjeta roja del ancho del tablero.</summary>
        public void MostrarError(string texto)
        {
            vista = Vista.Error;
            textoError = texto ?? "";
            resumen = null;
            marcada = null;
            Rehacer(false);
        }

        /// <summary>El profesor no tiene grupos en este ciclo: una sola tarjeta gris del ancho del tablero.</summary>
        public void MostrarSinGrupos()
        {
            vista = Vista.SinGrupos;
            resumen = null;
            marcada = null;
            Rehacer(false);
        }

        /// <summary>Los alumnos (AlumnoDeAsistencia.Id) que marca esa tarjeta; vacia si ninguno.</summary>
        public IList<int> IdsDe(int tarjeta)
        {
            Tarjeta t = tarjetas.FirstOrDefault(x => x.Numero == tarjeta && x.Aviso == null);
            return t == null ? new List<int>() : new List<int>(t.Ids);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
                fuenteRotulo.Dispose();
                fuenteCifra.Dispose();
                fuenteCifraCompacta.Dispose();
                fuenteComplemento.Dispose();
                fuenteComplementoCompacta.Dispose();
                fuentePie.Dispose();
                fuenteLinea.Dispose();
                fuenteQuedan.Dispose();
                fuentePastilla.Dispose();
                fuenteMuestra.Dispose();
                fuenteSinAlumnos.Dispose();
            }

            base.Dispose(disposing);
        }

        // =====================================================================
        // Armar: los textos de cada tarjeta
        // =====================================================================

        private Medidas Plan
        {
            get { return amplio ? Amplias : Compactas; }
        }

        /// <summary>Vuelve a armar todas las tarjetas; repinta si cambio la firma (o si se pide).</summary>
        private void Rehacer(bool siempre)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                tarjetas = Armar(g);
            }

            string firma = FirmaDelTablero();
            if (siempre || firma != firmaPintada)
            {
                Invalidate();
            }

            firmaPintada = firma;
            Despues();
        }

        /// <summary>Tras rehacer: el nombre accesible, el foco del teclado, el cursor y la ayuda bajo el raton.</summary>
        private void Despues()
        {
            string nombre = Accesible();
            AccessibleName = nombre.Length == 0 ? null : nombre;
            int antes = foco;
            ValidarFoco();
            if (foco != antes && Focused)
            {
                Invalidate();
            }

            RefrescarRaton();
        }

        private string FirmaDelTablero()
        {
            StringBuilder s = new StringBuilder();
            s.Append(amplio ? 'A' : 'C').Append(Width).Append('x').Append(Height);
            foreach (Tarjeta t in tarjetas)
            {
                s.Append('#').Append(t.Firma());
            }

            return s.ToString();
        }

        /// <summary>El hueco de cada tarjeta (0 a 3): las cuatro iguales y la ultima se queda el resto.</summary>
        private Rectangle Hueco(int i)
        {
            int separacion = Plan.Separacion;
            int ancho = Math.Max(0, (Width - 3 * separacion) / 4);
            int x = i * (ancho + separacion);
            return new Rectangle(x, 0, i == 3 ? Math.Max(0, Width - x) : ancho, Height);
        }

        private List<Tarjeta> Armar(Graphics g)
        {
            List<Tarjeta> lista = new List<Tarjeta>();
            if (Width <= 0 || Height <= 0)
            {
                return lista;
            }

            Rectangle todo = new Rectangle(0, 0, Width, Height);
            if (vista == Vista.Error)
            {
                lista.Add(TarjetaDeAviso(todo, textoError, FondoError, BordeError, TextoError, textoError));
            }
            else if (vista == Vista.SinGrupos)
            {
                lista.Add(TarjetaDeAviso(todo, "No tienes grupos en este ciclo: te los asigna la administración de DigBit en Horarios.",
                    FondoSinGrupos, BordeSinGrupos, TextoSinGrupos, null));
            }
            else if (vista == Vista.Leyendo || resumen == null)
            {
                // Los rotulos con "—": nada se mueve mientras se lee.
                string[] rotulos = { "Asistencia del grupo", "Sin derecho a evaluación", "Faltaron a las 2 últimas", "Clase de hoy" };
                for (int i = 0; i < rotulos.Length; i++)
                {
                    lista.Add(new Tarjeta(i + 1, Hueco(i)) { Rotulo = rotulos[i], Cifra = SinDato, ColorCifra = ColorLeyendo });
                }
            }
            else if (resumen.Alumnos.Count == 0)
            {
                lista.Add(ArmarSinAlumnos(Rectangle.FromLTRB(Hueco(0).Left, 0, Hueco(2).Right, Height), resumen.Lista != null && resumen.Lista.ConDadas, enTerceraPersona));
                lista.Add(ArmarHoy(Hueco(3)));
            }
            else
            {
                lista.Add(ArmarGrupo(Hueco(0)));
                lista.Add(ArmarSinDerecho(Hueco(1)));
                lista.Add(ArmarSeguidas(Hueco(2)));
                lista.Add(ArmarHoy(Hueco(3)));
            }

            foreach (Tarjeta t in lista)
            {
                Colocar(g, t);
            }

            return lista;
        }

        private static Tarjeta TarjetaDeAviso(Rectangle rect, string texto, Color fondo, Color borde, Color letra, string ayudaAviso)
        {
            return new Tarjeta(0, rect)
            {
                Aviso = texto,
                FondoAviso = fondo,
                BordeAviso = borde,
                LetraAviso = letra,
                Ayuda = string.IsNullOrEmpty(ayudaAviso) ? null : ayudaAviso,
                Accesible = texto
            };
        }

        /// <summary>Con 0 alumnos, T1, T2 y T3 juntas: la lista aun no se arma (con db/22, tambien con las asistencias que de el profesor).</summary>
        private static Tarjeta ArmarSinAlumnos(Rectangle rect, bool conDadas, bool tercera)
        {
            Tarjeta t = new Tarjeta(1, rect)
            {
                Rotulo = "Lista de asistencia",
                Cifra = "Aún sin estudiantes",
                CifraChica = true,
                ColorCifra = GrisSinAlumnos,
                // Con db/22, mas corto en el pie para que quepa a 1366 (747 px de unos 765); entero en la ayuda.
                Pie1 = conDadas
                    ? "Se arma sola cuando registren su bitácora o " + (tercera ? "su docente les dé" : "les des") + " la asistencia; quien llegue después entra con falta en las clases anteriores."
                    : "Se arma sola con la primera clase en que registren su bitácora; quien llegue después entra con falta en las clases anteriores.",
                Accesible = "Lista de asistencia: aún sin estudiantes"
            };
            t.Ayuda = conDadas
                ? "Se arma sola con la primera clase en que registren su bitácora o " + (tercera ? "su docente les dé" : "les des") + " la asistencia; quien llegue después entra con falta en las clases anteriores."
                : t.Pie1;
            return t;
        }

        /// <summary>T1, la asistencia del grupo: el % de la esquina de la hoja, con una columna por clase.</summary>
        private Tarjeta ArmarGrupo(Rectangle hueco)
        {
            ResumenAsistencia r = resumen;
            Tarjeta t = new Tarjeta(1, hueco) { Rotulo = "Asistencia del grupo" };
            int n = r.Alumnos.Count;
            int clases = r.PorDia.Count;
            double? promedio = r.Promedio;
            if (!promedio.HasValue)
            {
                t.Cifra = SinDato;
                t.ColorCifra = Gris;
                t.Pie1 = "Aún no hay clases que cuenten.";
                t.Ayuda = "Aún no hay clases que cuenten: lo pendiente de hoy no cuenta ni como asistencia ni como falta.";
                t.Accesible = "Asistencia del grupo: aún no hay clases que cuenten";
                return t;
            }

            // El mismo % (truncado) y el mismo limite que la celda % de la fila Asistieron.
            t.Cifra = ResumenAsistencia.PorcentajeMostrado(promedio.Value) + " %";
            t.ColorCifra = ResumenAsistencia.TieneDerecho(promedio.Value) ? Verde : Rojo;
            t.Pie1 = Alumnos(n) + " y " + Clases(clases) + (r.Hoy != null ? ", con la de hoy." : ".");
            int tarde = Tarde(r);
            string llegaron = tarde > 0 && clases >= 2
                ? (tarde == 1 ? "1 entró" : tarde + " entraron") + " a la lista después de la primera clase."
                : null;
            t.Pie2 = llegaron ?? "Es el mismo % de abajo a la derecha de la lista.";

            int asistencias = r.Alumnos.Sum(a => a.Asistencias);
            int cuentan = asistencias + r.Alumnos.Sum(a => a.Faltas);
            int pendientes = r.Hoy == null ? 0 : Math.Max(0, r.Hoy.Pendientes);
            int quitadas = r.Alumnos.Sum(a => a.Retiradas);
            // Las que dio el profesor sin bitacora (db/22): ya estan en el %, como asistencias.
            int dadas = r.Alumnos.Sum(a => a.Dadas);
            List<string> renglones = new List<string>
            {
                Cuenta(asistencias, "asistencia", "asistencias") + " de " + cuentan + (cuentan == 1 ? " que cuenta" : " que cuentan")
                    + " (" + Alumnos(n) + ", " + Clases(clases)
                    + (pendientes > 0 ? ", sin contar " + Cuenta(pendientes, "pendiente", "pendientes") + " de hoy" : "") + ").",
                "Es el mismo % de abajo a la derecha de la lista."
            };
            if (quitadas > 0)
            {
                renglones.Add("Asistencias quitadas en el periodo: " + quitadas + ".");
            }

            if (dadas > 0)
            {
                renglones.Add((enTerceraPersona ? "Asistencias que dio su docente sin bitácora en el periodo: " : "Asistencias que diste sin bitácora en el periodo: ") + dadas + ".");
            }

            // Compacto: el segundo renglon del pie pasa a la ayuda.
            if (!amplio && llegaron != null)
            {
                renglones.Add(llegaron);
            }

            t.Ayuda = string.Join(Environment.NewLine, renglones);
            t.Accesible = "Asistencia del grupo: " + t.Cifra;
            if (clases >= 3)
            {
                t.Dias = r.PorDia;
            }

            return t;
        }

        /// <summary>T2, sin derecho a evaluacion: del % mas bajo al mas alto (como ResumenAsistencia.SinDerecho).</summary>
        private Tarjeta ArmarSinDerecho(Rectangle hueco)
        {
            ResumenAsistencia r = resumen;
            Tarjeta t = new Tarjeta(2, hueco) { Rotulo = "Sin derecho a evaluación" };
            int n = r.Alumnos.Count;
            int clases = r.PorDia.Count;
            if (!r.Promedio.HasValue)
            {
                t.Cifra = SinDato;
                t.ColorCifra = Gris;
                t.Pie1 = "Aún no hay clases que cuenten.";
                t.Ayuda = "Con " + Minimo + " % o más de asistencia se tiene derecho a evaluación. Aún no hay clases que cuenten.";
                t.Accesible = "Sin derecho a evaluación: aún no hay clases que cuenten";
                return t;
            }

            List<AlumnoResumido> sin = r.SinDerecho;
            int cuantos = sin.Count;
            t.Cifra = cuantos.ToString(CultureInfo.InvariantCulture);
            t.ColorCifra = cuantos > 0 ? Rojo : Verde;
            t.Complemento = "de " + Alumnos(n);
            t.Accesible = "Sin derecho a evaluación: " + cuantos + " " + t.Complemento;
            if (cuantos == 0)
            {
                t.Pie1 = "Nadie: todo el grupo tiene " + Minimo + " % o más.";
                t.Ayuda = "Con " + Minimo + " % o más de asistencia se tiene derecho a evaluación: ahora todo el grupo lo tiene.";
                return t;
            }

            t.Nombres = sin.Select(NombreDe).ToList();
            // Al inicio del semestre una sola falta ya deja a muchos bajo el minimo: se dice por que.
            string nota = clases == 1 ? "Con 1 clase, quien faltó lleva 0 %."
                : clases <= 4 ? "Con " + clases + " clases, una falta ya deja " + ResumenAsistencia.PorcentajeMostrado(100.0 * (clases - 1) / clases) + " %."
                : null;
            t.Pie2 = nota ?? (cuantos == 1 ? "Pulsa para marcar su fila en la lista." : "Pulsa para marcar sus filas en la lista.");

            List<string> renglones = sin.Take(MaximoEnAyuda)
                .Select(a => NombreDe(a) + ": " + ResumenAsistencia.PorcentajeMostrado(a.Porcentaje.Value) + " % (" + a.Asistencias + " de " + (a.Asistencias + a.Faltas) + ")")
                .ToList();
            if (cuantos > MaximoEnAyuda)
            {
                renglones.Add("y " + (cuantos - MaximoEnAyuda) + " más.");
            }

            if (!amplio && nota != null)
            {
                renglones.Add(nota);
            }

            t.Ayuda = string.Join(Environment.NewLine, renglones);
            t.AyudaPulsar = "Pulsa la tarjeta para marcar " + (cuantos == 1 ? "su fila" : "sus filas") + " en la lista; otra vez, para quitar la marca.";
            t.Ids = sin.Select(a => a.Alumno.Id).ToList();
            t.SePulsa = t.Ids.Count > 0;
            return t;
        }

        /// <summary>T3, faltaron a las dos ultimas que cuentan (las F y las quitadas; lo pendiente de hoy se salta).</summary>
        private Tarjeta ArmarSeguidas(Rectangle hueco)
        {
            ResumenAsistencia r = resumen;
            Tarjeta t = new Tarjeta(3, hueco) { Rotulo = "Faltaron a las 2 últimas" };
            int n = r.Alumnos.Count;
            if (r.PorDia.Count < 2)
            {
                t.Cifra = SinDato;
                t.ColorCifra = Gris;
                t.Pie1 = "Se ve desde la segunda clase.";
                t.Ayuda = "Quién faltó a las dos últimas clases que cuentan: se ve desde la segunda clase.";
                t.Accesible = "Faltaron a las 2 últimas: se ve desde la segunda clase";
                return t;
            }

            const string Regla = "Cuentan las F y las asistencias quitadas; lo pendiente de hoy no.";
            List<AlumnoResumido> con = r.ConFaltasSeguidas;
            int cuantos = con.Count;
            t.Cifra = cuantos.ToString(CultureInfo.InvariantCulture);
            t.ColorCifra = cuantos > 0 ? Rojo : Verde;
            t.Complemento = "de " + Alumnos(n);
            t.Accesible = "Faltaron a las 2 últimas: " + cuantos + " " + t.Complemento;
            if (cuantos == 0)
            {
                t.Pie1 = "Nadie faltó a las dos últimas.";
                t.Ayuda = "Nadie faltó a las dos últimas clases que cuentan." + Environment.NewLine + Regla;
                return t;
            }

            t.Nombres = con.Select(a => NombreDe(a) + (a.FaltasSeguidas >= 3 ? " (" + a.FaltasSeguidas + " seguidas)" : "")).ToList();
            t.Pie2 = cuantos == 1 ? "Pulsa para marcar su fila en la lista." : "Pulsa para marcar sus filas en la lista.";

            List<string> renglones = new List<string>();
            foreach (AlumnoResumido a in con.Take(MaximoEnAyuda))
            {
                // La racha la cuenta ResumenAsistencia, con su primer dia: una sola regla.
                DateTime? desde = a.InicioDeRacha;
                renglones.Add(NombreDe(a) + ": " + a.FaltasSeguidas + " faltas seguidas" + (desde.HasValue ? ", desde el " + Fecha(desde.Value) : "") + ".");
            }

            if (cuantos > MaximoEnAyuda)
            {
                renglones.Add("y " + (cuantos - MaximoEnAyuda) + " más.");
            }

            renglones.Add(Regla);
            t.Ayuda = string.Join(Environment.NewLine, renglones);
            t.AyudaPulsar = "Pulsa la tarjeta para marcar " + (cuantos == 1 ? "su fila" : "sus filas") + " en la lista; otra vez, para quitar la marca.";
            t.Ids = con.Select(a => a.Alumno.Id).ToList();
            t.SePulsa = t.Ids.Count > 0;
            return t;
        }

        /// <summary>
        /// T4, la clase de hoy. En este orden: en curso (un bloque de hoy sigue y
        /// la lista trae su columna); en la pausa entre dos bloques; antes de
        /// empezar; ya empezo pero la lectura aun no trae su columna (la pide el
        /// reloj de la ventana); terminada; y sin clase hoy, la ultima. Con
        /// columna de hoy pero sin bloques (cambio el horario), como en curso sin
        /// el reloj.
        /// </summary>
        private Tarjeta ArmarHoy(Rectangle hueco)
        {
            ResumenAsistencia r = resumen;
            Tarjeta t = new Tarjeta(4, hueco);
            int n = r.Alumnos.Count;
            TimeSpan hora = ahora;
            List<TramoHorario> deHoy = bloques;
            TramoHorario enBloque = deHoy.FirstOrDefault(b => b.Inicio <= hora && hora < b.Fin);
            TramoHorario siguiente = deHoy.FirstOrDefault(b => b.Inicio > hora);
            AsistenciaDelDia hoy = r.Hoy;
            AsistenciaDelDia ultima = r.PorDia.LastOrDefault(d => !d.Dia.EnCurso);
            string horario = deHoy.Count == 0 ? null
                : "Hoy: " + Enumerar(deHoy.Select(b => "de " + Hora(b.Inicio) + " a " + Hora(b.Fin)).ToList()) + ".";
            List<string> ayudas = new List<string>();
            string estado = null;

            if (hoy != null && (deHoy.Count == 0 || enBloque != null || siguiente != null))
            {
                // En curso o en la pausa (tambien antes del primer bloque si ya registran).
                bool corriendo = deHoy.Count == 0 || enBloque != null;
                bool antesDelPrimero = !corriendo && siguiente == deHoy[0];
                t.Rotulo = "Asistencia de hoy";
                string cuentas = ComoVaHoy(t, hoy, n);
                if (corriendo)
                {
                    PonerPastilla(t, "En curso", Color.DarkGreen, Color.White);
                    t.EnCurso = true;
                    estado = "en curso";
                }
                else
                {
                    PonerPastilla(t, "Hoy " + Hora(siguiente.Inicio), FondoHoy, Color.DarkGreen);
                }

                if (enBloque != null)
                {
                    t.Barra.Derecha = "quedan " + PanelLaboratoriosAhora.Duracion(enBloque.Fin - hora);
                    t.Pie2 = "Termina a las " + Hora(enBloque.Fin) + "." + (siguiente != null ? " Sigue a las " + Hora(siguiente.Inicio) + "." : "");
                }
                else if (!corriendo)
                {
                    string cuando = Hora(siguiente.Inicio);
                    t.Barra.Derecha = (antesDelPrimero ? "empieza " : "sigue ") + cuando;
                    // Antes del primer bloque ya hay columna si alguien registro antes (se puede desde 15 min antes).
                    t.Pie2 = antesDelPrimero ? "Empieza a las " + cuando + "; ya hay registros." : "Sigue a las " + cuando + ": aún se puede registrar.";
                    estado = (antesDelPrimero ? "empieza" : "sigue") + " a las " + cuando;
                }

                ayudas.Add(horario ?? (enTerceraPersona ? "Esta clase ya no está en su horario de hoy, pero su columna sigue en la lista mientras dura."
                    : "Esta clase ya no está en tu horario de hoy, pero su columna sigue en la lista mientras dura."));
                if (!amplio)
                {
                    ayudas.Add(t.Pie2);
                }

                ayudas.Add(cuentas);
                // Con db/22 y un bloque en curso, el profesor puede darla (la hoja lo ofrece igual: PuedeCambiar).
                // En tercera persona (la ficha del docente, solo para ver) la hoja no deja: se dice quien puede.
                if (enBloque != null && r.Lista != null && r.Lista.ConDadas && hoy.Pendientes > 0)
                {
                    ayudas.Add(enTerceraPersona
                        ? "Si alguno está en el salón y su equipo no pudo registrar, su docente puede darle la asistencia desde Mis grupos."
                        : "Si alguno está en el salón y su equipo no pudo registrar, pulsa su casilla de hoy para darle la asistencia.");
                }

                if (t.SePulsa)
                {
                    t.AyudaPulsar = "Pulsa la tarjeta para marcar en la lista a " + (t.Ids.Count == 1 ? "quien aún no registra" : "quienes aún no registran")
                        + "; otra vez, para quitar la marca.";
                }
            }
            else if (deHoy.Count > 0 && hora < deHoy[0].Inicio)
            {
                // Antes de empezar.
                TramoHorario primero = deHoy[0];
                string falta = PanelLaboratoriosAhora.Duracion(primero.Inicio - hora);
                t.Rotulo = "Clase de hoy";
                PonerPastilla(t, "Hoy " + Hora(primero.Inicio), FondoHoy, Color.DarkGreen);
                t.Cifra = Hora(primero.Inicio);
                t.ColorCifra = Verde;
                t.Complemento = "a " + Hora(primero.Fin);
                t.Lineas.Add(new Linea("Empieza en " + falta + ".", "En " + falta + "."));
                t.Pie1 = amplio ? "Su columna aparece en la lista al empezar." : "Su columna aparece al empezar.";
                t.Pie2 = UltimaClase(ultima);
                ayudas.Add(horario);
                if (!amplio)
                {
                    ayudas.Add("Su columna aparece en la lista al empezar.");
                    ayudas.Add(t.Pie2);
                }
            }
            else if (deHoy.Count > 0 && hora < deHoy.Max(b => b.Fin))
            {
                // Ya empezo un bloque, pero la lectura aun no trae la columna de hoy: la pide el reloj de la ventana.
                TramoHorario actual = enBloque ?? siguiente ?? deHoy[0];
                t.Rotulo = "Clase de hoy";
                t.Cifra = Hora(actual.Inicio);
                t.ColorCifra = Verde;
                t.Complemento = "a " + Hora(actual.Fin);
                if (enBloque != null)
                {
                    string quedan = PanelLaboratoriosAhora.Duracion(enBloque.Fin - hora);
                    PonerPastilla(t, "En curso", Color.DarkGreen, Color.White);
                    t.Lineas.Add(new Linea("Quedan " + quedan + ".", quedan + "."));
                    estado = "en curso";
                }
                else
                {
                    string falta = PanelLaboratoriosAhora.Duracion(actual.Inicio - hora);
                    PonerPastilla(t, "Hoy " + Hora(actual.Inicio), FondoHoy, Color.DarkGreen);
                    t.Lineas.Add(new Linea("Empieza en " + falta + ".", "En " + falta + "."));
                }

                t.Pie1 = "Leyendo la lista...";
                t.Pie2 = UltimaClase(ultima);
                ayudas.Add(horario);
                ayudas.Add("Leyendo la lista: la columna de hoy aparece en cuanto llegue.");
                if (!amplio)
                {
                    ayudas.Add(t.Pie2);
                }
            }
            else if (deHoy.Count > 0)
            {
                // Termino. Si la lectura es de mientras duraba, su columna ya cuenta como terminada.
                PonerPastilla(t, "Terminó", FondoTermino, LetraTermino);
                estado = "terminó";
                string deA = "De " + Hora(deHoy.Min(b => b.Inicio)) + " a " + Hora(deHoy.Max(b => b.Fin)) + ".";
                AsistenciaDelDia dia = hoy ?? (ultima != null && ultima.Dia.Fecha == r.Lista.AhoraServidor.Date ? ultima : null);
                ayudas.Add(horario);
                // Una columna de hoy en la que nadie registro desaparece en la relectura
                // (AsistenciaDatos solo la deja si alguien registro o si el profesor dio
                // alguna asistencia, db/22): es "Nadie registro". Con solo asistencias
                // dadas, Asistieron > 0: cuenta como clase con asistencia.
                if (dia != null && dia.EnLista > 0 && dia.Asistieron + dia.Retiradas > 0)
                {
                    t.Rotulo = "Asistencia de hoy";
                    ayudas.Add(ComoTermino(t, dia));
                    t.Pie2 = deA;
                }
                else
                {
                    t.Rotulo = "Clase de hoy";
                    t.Cifra = "0";
                    t.ColorCifra = Gris;
                    t.Complemento = n > 0 ? "de " + n : "";
                    t.Pie1 = amplio ? "Nadie registró: esta clase no cuenta en la lista." : "Nadie registró: no cuenta en la lista.";
                    t.Pie2 = deA;
                    ayudas.Add("Nadie registró su bitácora: esta clase no cuenta en la lista.");
                }
            }
            else
            {
                // Hoy no le toca: la ultima clase con registro.
                const string SinClase = "Hoy no tienes clase con este grupo.";
                ayudas.Add(SinClase);
                t.Pie2 = SinClase;
                if (ultima != null && ultima.EnLista > 0)
                {
                    t.Rotulo = "Última clase, " + Fecha(ultima.Dia.Fecha);
                    ayudas.Add(ComoTermino(t, ultima));
                }
                else
                {
                    t.Rotulo = "Última clase";
                    t.Cifra = SinDato;
                    t.ColorCifra = Gris;
                    t.Pie1 = "Aún no hay clases con registro.";
                    ayudas.Add(t.Pie1);
                }
            }

            t.Ayuda = string.Join(Environment.NewLine, ayudas.Where(a => !string.IsNullOrEmpty(a)).Distinct());
            t.Accesible = AccesibleDe(t, estado);
            return t;
        }

        /// <summary>La clase de hoy mientras dura: cuantos registraron, los pendientes (P) y los que no estaban en el salon.</summary>
        private string ComoVaHoy(Tarjeta t, AsistenciaDelDia hoy, int n)
        {
            t.Barra = new Barra { Asistieron = hoy.Asistieron, Retiradas = hoy.Retiradas, Total = n, Sigue = true };
            if (n == 0)
            {
                t.Cifra = "0";
                t.ColorCifra = Gris;
                t.Lineas.Add(new Linea("Aún nadie registra."));
                return "Aún nadie registra.";
            }

            int pendientes = Math.Max(0, hoy.Pendientes);
            t.Cifra = hoy.Asistieron.ToString(CultureInfo.InvariantCulture);
            t.ColorCifra = hoy.Asistieron > 0 ? Verde : Gris;
            t.Complemento = "de " + n;
            if (pendientes > 0)
            {
                t.Lineas.Add(new Linea(TipoMuestra.Pendiente, pendientes == 1 ? "1 aún no registra" : pendientes + " aún no registran"));
            }
            else
            {
                t.Lineas.Add(new Linea("Ya registró todo el grupo.", "Nadie falta.") { Color = Verde });
            }

            if (hoy.Retiradas > 0)
            {
                t.Lineas.Add(new Linea(TipoMuestra.Retirada, hoy.Retiradas + " sin estar en el salón"));
            }

            t.Ids = resumen.Alumnos.Where(a => a.PendienteHoy).Select(a => a.Alumno.Id).ToList();
            t.SePulsa = t.Ids.Count > 0;
            return "Con asistencia: " + hoy.Asistieron + ". Aún no registran (P): " + pendientes + "."
                + (hoy.Retiradas > 0 ? " Registraron sin estar en el salón: " + hoy.Retiradas + ", se les quitó la asistencia." : "")
                + (hoy.Dadas > 0 ? (enTerceraPersona ? (hoy.Dadas == 1 ? " A 1 se la dio su docente (sin bitácora)." : " A " + hoy.Dadas + " se la dio su docente (sin bitácora).")
                    : hoy.Dadas == 1 ? " A 1 le diste la asistencia (sin bitácora)." : " A " + hoy.Dadas + " les diste la asistencia (sin bitácora).") : "");
        }

        /// <summary>Una clase que ya termino (la de hoy o la ultima): cuantos asistieron, faltaron y no estaban en el salon.</summary>
        private string ComoTermino(Tarjeta t, AsistenciaDelDia dia)
        {
            int faltaron = Math.Max(0, dia.EnLista - dia.Asistieron - dia.Retiradas);
            t.Cifra = dia.Asistieron.ToString(CultureInfo.InvariantCulture);
            t.ColorCifra = dia.Asistieron > 0 ? Verde : Gris;
            t.Complemento = "de " + dia.EnLista;
            if (faltaron > 0)
            {
                t.Lineas.Add(new Linea(TipoMuestra.Falta, faltaron == 1 ? "1 faltó" : faltaron + " faltaron"));
            }
            else
            {
                t.Lineas.Add(new Linea("No faltó nadie.") { Color = Verde });
            }

            if (dia.Retiradas > 0)
            {
                t.Lineas.Add(new Linea(TipoMuestra.Retirada, dia.Retiradas + " sin estar en el salón"));
            }

            t.Barra = new Barra { Asistieron = dia.Asistieron, Retiradas = dia.Retiradas, Total = dia.EnLista, Sigue = false };
            // Los que no tienen asistencia ese dia: los que faltaron y a los que se les quito.
            DateTime fecha = dia.Dia.Fecha;
            t.Ids = resumen.Lista.Alumnos.Where(a => !a.Presente.Contains(fecha)).Select(a => a.Id).ToList();
            t.SePulsa = t.Ids.Count > 0;
            if (t.SePulsa)
            {
                t.AyudaPulsar = "Pulsa la tarjeta para marcar en la lista a " + (t.Ids.Count == 1 ? "quien no tiene" : "quienes no tienen")
                    + " asistencia ese día; otra vez, para quitar la marca.";
            }

            return "Con asistencia: " + dia.Asistieron + ". Faltaron: " + faltaron + "."
                + (dia.Retiradas > 0 ? " Registraron sin estar en el salón: " + dia.Retiradas + ", se les quitó la asistencia." : "")
                + (dia.Dadas > 0 ? (enTerceraPersona ? " Su docente le dio la asistencia a " : " Le diste la asistencia a ") + dia.Dadas + " (sin bitácora)." : "");
        }

        private static void PonerPastilla(Tarjeta t, string texto, Color fondo, Color letra)
        {
            t.Pastilla = texto;
            t.FondoPastilla = fondo;
            t.LetraPastilla = letra;
        }

        private static string UltimaClase(AsistenciaDelDia ultima)
        {
            return ultima == null
                ? "Será la primera clase con registro."
                : "Última clase: " + Fecha(ultima.Dia.Fecha) + ", con asistencia " + ultima.Asistieron + " de " + ultima.EnLista + ".";
        }

        /// <summary>"Asistencia de hoy: 15 de 19, en curso; 3 aún no registran; 1 sin estar en el salón".</summary>
        private static string AccesibleDe(Tarjeta t, string estado)
        {
            List<string> partes = new List<string>();
            string cifra = t.Cifra == SinDato ? "" : t.Cifra + (t.Complemento.Length > 0 ? " " + t.Complemento : "");
            if (estado != null)
            {
                cifra = cifra.Length > 0 ? cifra + ", " + estado : estado;
            }

            if (cifra.Length > 0)
            {
                partes.Add(cifra);
            }

            foreach (Linea linea in t.Lineas)
            {
                partes.Add(EnFrase(linea.Texto));
            }

            if (t.Pie1.Length > 0)
            {
                partes.Add(EnFrase(t.Pie1));
            }

            return t.Rotulo + ": " + string.Join("; ", partes);
        }

        /// <summary>Las cuatro cifras rotuladas, para el lector de pantalla.</summary>
        private string Accesible()
        {
            if (vista == Vista.Leyendo)
            {
                return "Leyendo la lista...";
            }

            List<string> partes = tarjetas.Select(t => (t.Accesible ?? "").Trim().TrimEnd('.').Trim()).Where(s => s.Length > 0).ToList();
            return partes.Count == 0 ? "" : string.Join(". ", partes) + ".";
        }

        // =====================================================================
        // Colocar: donde va cada cosa (se mide con un Graphics)
        // =====================================================================

        private DrawingFont FuenteCifra(Tarjeta t)
        {
            return t.CifraChica ? fuenteSinAlumnos : amplio ? fuenteCifra : fuenteCifraCompacta;
        }

        private DrawingFont FuenteComplemento
        {
            get { return amplio ? fuenteComplemento : fuenteComplementoCompacta; }
        }

        private DrawingFont FuenteLinea
        {
            get { return amplio ? fuenteLinea : fuentePie; }
        }

        private void Colocar(Graphics g, Tarjeta t)
        {
            if (t.Aviso != null)
            {
                return;
            }

            Medidas m = Plan;
            Rectangle r = t.Rect;
            int x = r.Left + m.Lado;
            int derecha = Math.Max(x, r.Right - m.Lado);
            int ancho = derecha - x;

            // La pastilla, pegada al borde derecho util; el rotulo llega hasta ella.
            int finRotulo = derecha;
            if (t.Pastilla != null)
            {
                int anchoPastilla = Medir(g, t.Pastilla, fuentePastilla) + 16;
                t.RectPastilla = new Rectangle(derecha - anchoPastilla, r.Top + m.YPastilla, anchoPastilla, AltoPastilla);
                finRotulo = t.RectPastilla.Left - 8;
            }

            t.RectRotulo = new Rectangle(x, r.Top + m.YRotulo, Math.Max(0, finRotulo - x), Alto(g, fuenteRotulo));

            DrawingFont letraCifra = FuenteCifra(t);
            int anchoCifra = Medir(g, t.Cifra, letraCifra);
            int altoCifra = Alto(g, letraCifra);
            int yCifra = t.CifraChica ? m.YCifra + Math.Max(0, (m.AltoCifra - altoCifra) / 2) : m.YCifra;
            t.RectCifra = new Rectangle(x, r.Top + yCifra, Math.Min(anchoCifra + 2, ancho), altoCifra);
            int tras = x + anchoCifra;
            if (t.Complemento.Length > 0)
            {
                DrawingFont letra = FuenteComplemento;
                int anchoComplemento = Medir(g, t.Complemento, letra);
                int xc = tras + m.EntreComplemento;
                t.RectComplemento = new Rectangle(xc, r.Top + m.YComplemento, Math.Max(0, Math.Min(anchoComplemento + 2, derecha - xc)), Alto(g, letra));
                tras = xc + anchoComplemento;
            }

            // La zona derecha: tras la cifra o su complemento, hasta el borde util.
            t.RectZona = Rectangle.FromLTRB(Math.Min(derecha, tras + m.TrasCifra), r.Top + m.ZonaArriba, derecha, r.Top + m.ZonaAbajo);
            DrawingFont letraLinea = FuenteLinea;
            foreach (Linea linea in t.Lineas)
            {
                int cabe = t.RectZona.Width - (linea.Muestra != TipoMuestra.Ninguna ? m.LadoMuestra + 6 : 0);
                linea.Mostrado = linea.Corto != null && Medir(g, linea.Texto, letraLinea) > cabe ? linea.Corto : linea.Texto;
            }

            if (t.Dias != null)
            {
                ColocarGrafica(t, m);
            }

            if (t.Barra != null)
            {
                int yBarra = r.Top + m.YBarra;
                int anchoBarra = ancho;
                if (!string.IsNullOrEmpty(t.Barra.Derecha))
                {
                    // Del ancho fijo (asi no se mueve cada minuto), salvo que el texto no quepa.
                    int anchoTexto = Medir(g, t.Barra.Derecha, fuenteQuedan);
                    int altoTexto = Alto(g, fuenteQuedan);
                    anchoBarra = Math.Max(20, Math.Min(ancho - m.ReservaBarra, ancho - anchoTexto - m.AntesDelTexto));
                    int yTexto = yBarra + AltoBarra / 2 - altoTexto / 2;
                    t.RectDerecha = Rectangle.FromLTRB(Math.Min(derecha, x + anchoBarra + m.AntesDelTexto), yTexto, derecha, yTexto + altoTexto);
                }

                t.RectBarra = new Rectangle(x, yBarra, anchoBarra, AltoBarra);
            }

            int altoPie = Alto(g, fuentePie);
            t.RectPie1 = new Rectangle(x, r.Top + m.YPie1, ancho, altoPie);
            t.RectPie2 = m.YPie2 >= 0 ? new Rectangle(x, r.Top + m.YPie2, ancho, altoPie) : Rectangle.Empty;
            if (t.Nombres != null)
            {
                t.Pie1 = NombresQueCaben(g, t.Nombres, fuentePie, ancho);
            }
        }

        /// <summary>
        /// La minigrafica de T1: una columna por clase, de alto lo que asistio el
        /// grupo, pegada a la derecha. De 3 a 10 px de ancho con 2 entre ellas; si
        /// ni con 3 caben todas, las ultimas que quepan.
        /// </summary>
        private static void ColocarGrafica(Tarjeta t, Medidas m)
        {
            Rectangle zona = t.RectZona;
            int total = t.Dias.Count;
            if (total < 3 || zona.Width < TramoMinimo)
            {
                return;
            }

            int ancho = Math.Min(10, (zona.Width + 2) / total - 2);
            int mostradas = total;
            if (ancho < 3)
            {
                ancho = 3;
                mostradas = Math.Max(1, Math.Min(total, (zona.Width + 2) / 5));
            }

            // Centrada en la zona, con 1 px arriba para la linea del 100 % y 1 abajo para la de base.
            int alto = m.AltoGrafica;
            int arriba = zona.Top + Math.Max(0, (zona.Height - alto - 2) / 2) + 1;
            int abajo = arriba + alto;
            int x0 = zona.Right - (mostradas * (ancho + 2) - 2);
            int ultimaTerminada = t.Dias.FindLastIndex(d => !d.Dia.EnCurso);
            t.Columnas = new List<Columna>();
            for (int k = 0; k < mostradas; k++)
            {
                int i = total - mostradas + k;
                AsistenciaDelDia dia = t.Dias[i];
                double valor = dia.EnLista == 0 ? 0 : Math.Min(1.0, (double)dia.Asistieron / dia.EnLista);
                int lleno = dia.Asistieron > 0 ? Math.Max(1, (int)Math.Round(alto * valor)) : 0;
                int xc = x0 + k * (ancho + 2);
                t.Columnas.Add(new Columna
                {
                    Rect = new Rectangle(xc, abajo - lleno, ancho, lleno),
                    Zona = new Rectangle(xc - 1, arriba - 1, ancho + 2, alto + 2),
                    EnCurso = dia.Dia.EnCurso,
                    Relleno = dia.Dia.EnCurso ? HoyFondo : i == ultimaTerminada ? VerdeExcel : ColumnaAnterior,
                    Ayuda = AyudaDeColumna(dia)
                });
            }

            t.RectGrafica = Rectangle.FromLTRB(x0, arriba, zona.Right, abajo);
            if (mostradas < total)
            {
                t.Ayuda += Environment.NewLine + "Se ven las últimas " + mostradas + " de " + total + " clases.";
            }
        }

        private static string AyudaDeColumna(AsistenciaDelDia dia)
        {
            if (dia.Dia.EnCurso)
            {
                return "Hoy, " + Fecha(dia.Dia.Fecha) + " (sigue): con asistencia " + dia.Asistieron + " de " + dia.EnLista + " hasta ahora.";
            }

            return Capital(Fecha(dia.Dia.Fecha)) + ": asistieron " + dia.Asistieron + " de " + dia.EnLista + " (" + ResumenAsistencia.PorcentajeMostrado(dia.Porcentaje) + " %).";
        }

        /// <summary>"Acosta Paredes Victor, Carrillo Pacheco Adrian y 14 más": en su orden, los que caben en ese ancho.</summary>
        private static string NombresQueCaben(Graphics g, IList<string> nombres, DrawingFont fuente, int ancho)
        {
            if (nombres.Count == 0)
            {
                return "";
            }

            string todos = Enumerar(nombres);
            if (Medir(g, todos, fuente) <= ancho)
            {
                return todos;
            }

            // Cada nombre mas lo alarga: se agregan mientras quepan.
            string mejor = null;
            for (int k = 1; k < nombres.Count; k++)
            {
                string texto = string.Join(", ", nombres.Take(k)) + " y " + (nombres.Count - k) + " más";
                if (Medir(g, texto, fuente) > ancho)
                {
                    break;
                }

                mejor = texto;
            }

            if (mejor != null)
            {
                return mejor;
            }

            if (nombres.Count == 1)
            {
                return Recortar(g, nombres[0], fuente, ancho);
            }

            // Ni el primero cabe entero: recortado, sin perder cuantos mas son.
            string cola = " y " + (nombres.Count - 1) + " más";
            return Recortar(g, nombres[0], fuente, ancho - Medir(g, cola, fuente)) + cola;
        }

        /// <summary>El texto con "..." para que quepa en ese ancho.</summary>
        private static string Recortar(Graphics g, string texto, DrawingFont fuente, int ancho)
        {
            if (Medir(g, texto, fuente) <= ancho)
            {
                return texto;
            }

            int bajo = 0;
            int alto = texto.Length;
            while (bajo < alto)
            {
                int medio = (bajo + alto + 1) / 2;
                if (Medir(g, texto.Substring(0, medio).TrimEnd() + "...", fuente) <= ancho)
                {
                    bajo = medio;
                }
                else
                {
                    alto = medio - 1;
                }
            }

            return texto.Substring(0, bajo).TrimEnd() + "...";
        }

        private static int Medir(Graphics g, string texto, DrawingFont fuente)
        {
            return string.IsNullOrEmpty(texto) ? 0 : TextRenderer.MeasureText(g, texto, fuente, Size.Empty, SinMargen).Width;
        }

        private static int Alto(Graphics g, DrawingFont fuente)
        {
            return TextRenderer.MeasureText(g, "Ág", fuente, Size.Empty, SinMargen).Height;
        }

        // =====================================================================
        // Pintado
        // =====================================================================

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            foreach (Tarjeta t in tarjetas)
            {
                if (t.Rect.IntersectsWith(e.ClipRectangle))
                {
                    Dibujar(g, t);
                }
            }

            // El foco del teclado (Tab), no despues de un clic: punteado, 3 px por dentro de la tarjeta.
            if (Focused && ShowFocusCues && tarjetas.Count > 0)
            {
                Tarjeta conFoco = tarjetas.FirstOrDefault(t => t.Numero == foco && foco != 0) ?? tarjetas[0];
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(conFoco.Rect, -3, -3));
            }

            base.OnPaint(e);
        }

        private void Dibujar(Graphics g, Tarjeta t)
        {
            if (t.Rect.Width < 8 || t.Rect.Height < 8)
            {
                return;
            }

            bool esMarcada = t.Aviso == null && marcada.HasValue && marcada.Value == t.Numero;
            bool resaltada = t.Aviso == null && encima == t.Numero && Pulsable(t);
            Color fondo = t.Aviso != null ? t.FondoAviso : esMarcada ? FondoMarcada : resaltada ? FondoEncima : FondoFicha;
            // La de hoy en curso conserva su borde DarkGreen aunque este marcada.
            Color borde = t.Aviso != null ? t.BordeAviso : t.EnCurso ? Color.DarkGreen : esMarcada ? VerdeExcel : BordeFicha;
            bool grueso = t.Aviso == null && (t.EnCurso || esMarcada);
            Rectangle trazo = grueso
                ? new Rectangle(t.Rect.X + 1, t.Rect.Y + 1, t.Rect.Width - 3, t.Rect.Height - 3)
                : new Rectangle(t.Rect.X, t.Rect.Y, t.Rect.Width - 1, t.Rect.Height - 1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(trazo, RadioTarjeta))
            using (SolidBrush relleno = new SolidBrush(fondo))
            using (Pen lapiz = new Pen(borde, grueso ? 2f : 1f))
            {
                g.FillPath(relleno, forma);
                g.DrawPath(lapiz, forma);
            }

            if (t.Aviso != null)
            {
                DibujarAviso(g, t);
                return;
            }

            if (t.Pastilla != null)
            {
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(t.RectPastilla, AltoPastilla / 2))
                using (SolidBrush relleno = new SolidBrush(t.FondoPastilla))
                {
                    g.FillPath(relleno, forma);
                }

                TextRenderer.DrawText(g, t.Pastilla, fuentePastilla, t.RectPastilla, t.LetraPastilla, Centrado);
            }

            TextRenderer.DrawText(g, t.Rotulo, fuenteRotulo, t.RectRotulo, ColorRotulo, Renglon);
            TextRenderer.DrawText(g, t.Cifra, FuenteCifra(t), t.RectCifra, t.ColorCifra, Renglon);
            if (t.Complemento.Length > 0)
            {
                TextRenderer.DrawText(g, t.Complemento, FuenteComplemento, t.RectComplemento, ColorTexto, Renglon);
            }

            DibujarGrafica(g, t);
            DibujarLineas(g, t);
            if (t.Barra != null)
            {
                DibujarBarra(g, t.Barra, t.RectBarra);
                if (!string.IsNullOrEmpty(t.Barra.Derecha))
                {
                    TextRenderer.DrawText(g, t.Barra.Derecha, fuenteQuedan, t.RectDerecha, Verde, Renglon | TextFormatFlags.Right);
                }
            }

            if (t.Pie1.Length > 0)
            {
                TextRenderer.DrawText(g, t.Pie1, fuentePie, t.RectPie1, ColorTexto, Renglon);
            }

            if (t.Pie2.Length > 0 && !t.RectPie2.IsEmpty)
            {
                TextRenderer.DrawText(g, t.Pie2, fuentePie, t.RectPie2, ColorTexto, Renglon);
            }
        }

        /// <summary>El error o "sin grupos": centrado y partido en renglones.</summary>
        private void DibujarAviso(Graphics g, Tarjeta t)
        {
            const TextFormatFlags Partido = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
            Rectangle dentro = Rectangle.Inflate(t.Rect, -18, -8);
            if (dentro.Width <= 0 || dentro.Height <= 0 || string.IsNullOrEmpty(t.Aviso))
            {
                return;
            }

            int alto = Math.Min(dentro.Height, TextRenderer.MeasureText(g, t.Aviso, fuenteLinea, new Size(dentro.Width, int.MaxValue), Partido).Height);
            Rectangle donde = new Rectangle(dentro.Left, dentro.Top + (dentro.Height - alto) / 2, dentro.Width, alto);
            TextRenderer.DrawText(g, t.Aviso, fuenteLinea, donde, t.LetraAviso, Partido | TextFormatFlags.EndEllipsis);
        }

        /// <summary>Los renglones de la zona derecha de T4: su cuadrito (P, F naranja o F rosa) y su texto, nunca en amarillo ni naranja.</summary>
        private void DibujarLineas(Graphics g, Tarjeta t)
        {
            if (t.Lineas.Count == 0 || t.RectZona.Width <= 0)
            {
                return;
            }

            Medidas m = Plan;
            DrawingFont letra = FuenteLinea;
            int altoLinea = Alto(g, letra);
            for (int i = 0; i < t.Lineas.Count && i < 2; i++)
            {
                Linea linea = t.Lineas[i];
                int y = t.Rect.Top + (t.Lineas.Count >= 2 ? m.Renglones[i] : m.RenglonSolo);
                int x = t.RectZona.Left;
                if (linea.Muestra != TipoMuestra.Ninguna)
                {
                    DibujarMuestra(g, new Rectangle(x, y + (altoLinea - m.LadoMuestra) / 2, m.LadoMuestra, m.LadoMuestra), linea.Muestra);
                    x += m.LadoMuestra + 6;
                }

                TextRenderer.DrawText(g, linea.Mostrado, letra, new Rectangle(x, y, Math.Max(0, t.RectZona.Right - x), altoLinea), linea.Color, Renglon);
            }
        }

        /// <summary>Un cuadrito con el relleno y la letra de la casilla de la hoja.</summary>
        private void DibujarMuestra(Graphics g, Rectangle r, TipoMuestra tipo)
        {
            Color fondo = tipo == TipoMuestra.Pendiente ? HoyFondo : tipo == TipoMuestra.Retirada ? RetiradaFondo : FaltaFondo;
            Color letra = tipo == TipoMuestra.Pendiente ? HoyTexto : tipo == TipoMuestra.Retirada ? RetiradaTexto : FaltaTexto;
            g.SmoothingMode = SmoothingMode.None;
            using (SolidBrush relleno = new SolidBrush(fondo))
            {
                g.FillRectangle(relleno, r);
            }

            using (Pen lapiz = new Pen(BordeMuestra))
            {
                g.DrawRectangle(lapiz, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            TextRenderer.DrawText(g, tipo == TipoMuestra.Pendiente ? "P" : "F", fuenteMuestra, r, letra, Centrado);
        }

        /// <summary>La minigrafica de T1, con su linea de base y la del 100 % (sin la del 80 %: ese es por alumno).</summary>
        private static void DibujarGrafica(Graphics g, Tarjeta t)
        {
            if (t.Columnas == null || t.Columnas.Count == 0)
            {
                return;
            }

            Rectangle area = t.RectGrafica;
            g.SmoothingMode = SmoothingMode.None;
            using (SolidBrush cien = new SolidBrush(LineaCien))
            {
                g.FillRectangle(cien, area.Left, area.Top - 1, area.Width, 1);
            }

            foreach (Columna c in t.Columnas)
            {
                if (c.Rect.Height <= 0)
                {
                    continue;
                }

                using (SolidBrush relleno = new SolidBrush(c.Relleno))
                {
                    g.FillRectangle(relleno, c.Rect);
                }

                if (c.EnCurso)
                {
                    using (Pen lapiz = new Pen(BordeHoy))
                    {
                        g.DrawRectangle(lapiz, c.Rect.X, c.Rect.Y, c.Rect.Width - 1, c.Rect.Height - 1);
                    }
                }
            }

            using (SolidBrush pincel = new SolidBrush(LineaBase))
            {
                g.FillRectangle(pincel, area.Left, area.Bottom, area.Width, 1);
            }
        }

        /// <summary>
        /// La barra de T4: verde Excel con asistencia y naranja sin estar en el
        /// salon (el par se distingue con daltonismo); lo que queda es la pista,
        /// amarilla con la clase en curso (los pendientes) o rosa si ya termino
        /// (los que faltaron). Entre tramos, 2 px del color de la tarjeta.
        /// </summary>
        private static void DibujarBarra(Graphics g, Barra barra, Rectangle r)
        {
            if (r.Width <= 0)
            {
                return;
            }

            int total = Math.Max(0, barra.Total);
            int asistieron = Math.Max(0, Math.Min(barra.Asistieron, total));
            int retiradas = Math.Max(0, Math.Min(barra.Retiradas, total - asistieron));
            int[] cuentas = total == 0 ? new[] { 0, 0, 1 } : new[] { asistieron, retiradas, total - asistieron - retiradas };
            int[] anchos = Repartir(r.Width, cuentas);
            Color[] colores = { VerdeExcel, Naranja, barra.Sigue ? HoyFondo : FaltaFondo };
            int primera = Array.FindIndex(anchos, a => a > 0);
            int ultima = Array.FindLastIndex(anchos, a => a > 0);
            GraphicsState estado = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Los bordes rectos, nitidos: cada pixel cubre de su coordenada a la siguiente.
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int x = r.Left;
            for (int i = 0; i < anchos.Length; i++)
            {
                if (anchos[i] <= 0)
                {
                    continue;
                }

                using (GraphicsPath forma = Tramo(new RectangleF(x, r.Top, anchos[i], r.Height), i == primera, i == ultima))
                using (SolidBrush relleno = new SolidBrush(colores[i]))
                {
                    g.FillPath(relleno, forma);
                }

                if (i == 2)
                {
                    using (GraphicsPath contorno = Tramo(new RectangleF(x + 0.5f, r.Top + 0.5f, anchos[i] - 1, r.Height - 1), i == primera, true))
                    using (Pen lapiz = new Pen(barra.Sigue ? BordeHoy : BordeFalta))
                    {
                        g.DrawPath(lapiz, contorno);
                    }
                }

                x += anchos[i] + EntreTramos;
            }

            g.Restore(estado);
        }

        /// <summary>
        /// El ancho de cada tramo (verde, naranja, pista) en proporcion a su
        /// cuenta: los que no son cero miden al menos 3 px, con 2 px entre ellos;
        /// el ultimo se queda el resto.
        /// </summary>
        private static int[] Repartir(int ancho, int[] cuentas)
        {
            int[] anchos = new int[cuentas.Length];
            List<int> conAlgo = new List<int>();
            for (int i = 0; i < cuentas.Length; i++)
            {
                if (cuentas[i] > 0)
                {
                    conAlgo.Add(i);
                }
            }

            if (conAlgo.Count == 0 || ancho <= 0)
            {
                return anchos;
            }

            int disponible = Math.Max(0, ancho - EntreTramos * (conAlgo.Count - 1));
            int total = cuentas.Sum();
            int usado = 0;
            for (int k = 0; k < conAlgo.Count; k++)
            {
                int i = conAlgo[k];
                int faltan = conAlgo.Count - 1 - k;
                int medida = k == conAlgo.Count - 1
                    ? disponible - usado
                    : (int)Math.Round(disponible * (double)cuentas[i] / total);
                medida = Math.Max(TramoMinimo, Math.Min(medida, disponible - usado - TramoMinimo * faltan));
                anchos[i] = Math.Max(0, medida);
                usado += anchos[i];
            }

            return anchos;
        }

        /// <summary>Un tramo de la barra, redondeado solo en los extremos de la barra.</summary>
        private static GraphicsPath Tramo(RectangleF r, bool izquierda, bool derecha)
        {
            GraphicsPath forma = new GraphicsPath();
            float d = Math.Min(2f * RadioBarra, Math.Min(r.Width, r.Height));
            if (d < 1f || (!izquierda && !derecha))
            {
                forma.AddRectangle(r);
                return forma;
            }

            if (izquierda)
            {
                forma.AddArc(r.Left, r.Top, d, d, 180, 90);
            }
            else
            {
                forma.AddLine(r.Left, r.Bottom, r.Left, r.Top);
            }

            if (derecha)
            {
                forma.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                forma.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            }
            else
            {
                forma.AddLine(r.Right, r.Top, r.Right, r.Bottom);
            }

            if (izquierda)
            {
                forma.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            }

            forma.CloseFigure();
            return forma;
        }

        // =====================================================================
        // Raton, teclado y ayuda
        // =====================================================================

        /// <summary>Se pulsa si tiene alumnos que marcar o si es la marcada (para quitar la marca).</summary>
        private bool Pulsable(Tarjeta t)
        {
            return t != null && t.Aviso == null && (t.SePulsa || (marcada.HasValue && marcada.Value == t.Numero));
        }

        private Tarjeta TarjetaEn(Point p)
        {
            foreach (Tarjeta t in tarjetas)
            {
                if (t.Rect.Contains(p))
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>La ayuda bajo el raton: la de la columna de la minigrafica o la de la tarjeta, con lo que hace pulsarla.</summary>
        private string AyudaEn(Point p)
        {
            Tarjeta t = TarjetaEn(p);
            if (t == null)
            {
                return null;
            }

            if (t.Columnas != null)
            {
                foreach (Columna c in t.Columnas)
                {
                    if (c.Zona.Contains(p))
                    {
                        return c.Ayuda;
                    }
                }
            }

            string texto = t.Ayuda;
            string pulsar = t.Aviso == null && marcada.HasValue && marcada.Value == t.Numero ? QuitarMarca : t.SePulsa ? t.AyudaPulsar : null;
            if (!string.IsNullOrEmpty(pulsar))
            {
                texto = string.IsNullOrEmpty(texto) ? pulsar : texto + Environment.NewLine + pulsar;
            }

            return string.IsNullOrEmpty(texto) ? null : texto;
        }

        /// <summary>SetToolTip solo si cambia el texto (como TarjetaTresDias): si no, la ayuda parpadea.</summary>
        private void PonerAyuda(string texto)
        {
            if (texto == textoAyuda)
            {
                return;
            }

            textoAyuda = texto;
            ayuda.SetToolTip(this, texto);
        }

        /// <summary>Tras rehacer o marcar: lo que hay bajo el raton (resalte, cursor y ayuda).</summary>
        private void RefrescarRaton()
        {
            if (!ratonDentro || !IsHandleCreated)
            {
                return;
            }

            Point p = PointToClient(MousePosition);
            Tarjeta t = TarjetaEn(p);
            int nueva = t == null ? 0 : t.Numero;
            if (nueva != encima)
            {
                encima = nueva;
                Invalidate();
            }

            Cursor quiero = Pulsable(t) ? Cursors.Hand : Cursors.Default;
            if (Cursor != quiero)
            {
                Cursor = quiero;
            }

            PonerAyuda(AyudaEn(p));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Rehacer(true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ratonDentro = true;
            Tarjeta t = TarjetaEn(e.Location);
            int nueva = t == null ? 0 : t.Numero;
            if (nueva != encima)
            {
                Tarjeta antes = tarjetas.FirstOrDefault(x => x.Numero == encima && encima != 0);
                encima = nueva;
                if (antes != null)
                {
                    Invalidate(antes.Rect);
                }

                if (t != null)
                {
                    Invalidate(t.Rect);
                }
            }

            Cursor quiero = Pulsable(t) ? Cursors.Hand : Cursors.Default;
            if (Cursor != quiero)
            {
                Cursor = quiero;
            }

            PonerAyuda(AyudaEn(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            ratonDentro = false;
            if (encima != 0)
            {
                Tarjeta antes = tarjetas.FirstOrDefault(x => x.Numero == encima);
                encima = 0;
                if (antes != null)
                {
                    Invalidate(antes.Rect);
                }
            }

            Cursor = Cursors.Default;
            PonerAyuda(null);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Pulsable(TarjetaEn(e.Location)) && CanFocus)
            {
                Focus();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Tarjeta t = TarjetaEn(e.Location);
            if (Pulsable(t))
            {
                foco = t.Numero;
                Pulsar(t.Numero);
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys tecla = keyData & Keys.KeyCode;
            if ((keyData & (Keys.Control | Keys.Alt)) == Keys.None
                && (tecla == Keys.Left || tecla == Keys.Right || tecla == Keys.Enter || tecla == Keys.Space))
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || e.Modifiers != Keys.None)
            {
                return;
            }

            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                MoverFoco(e.KeyCode == Keys.Right ? 1 : -1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                PulsarFoco();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Space)
            {
                // Como un boton: al soltarla (asi dejarla pulsada no marca y desmarca sin parar).
                e.Handled = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (!e.Handled && e.KeyCode == Keys.Space && e.Modifiers == Keys.None)
            {
                PulsarFoco();
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            ValidarFoco();
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            base.OnChangeUICues(e);
            Invalidate();
        }

        /// <summary>Las flechas pasan por las tarjetas que se pulsan, en orden.</summary>
        private void MoverFoco(int paso)
        {
            List<int> pulsables = tarjetas.Where(t => Pulsable(t)).Select(t => t.Numero).ToList();
            if (pulsables.Count == 0)
            {
                return;
            }

            int i = pulsables.IndexOf(foco);
            int nuevo = i < 0
                ? (paso > 0 ? pulsables[0] : pulsables[pulsables.Count - 1])
                : pulsables[Math.Max(0, Math.Min(pulsables.Count - 1, i + paso))];
            if (nuevo != foco)
            {
                foco = nuevo;
                Invalidate();
            }
        }

        private void PulsarFoco()
        {
            Tarjeta t = tarjetas.FirstOrDefault(x => x.Numero == foco && foco != 0);
            if (Pulsable(t))
            {
                Pulsar(t.Numero);
            }
        }

        /// <summary>El foco, en una que se pulse: la que tenia, la marcada o la primera; si ninguna se pulsa, ninguna.</summary>
        private void ValidarFoco()
        {
            Tarjeta actual = tarjetas.FirstOrDefault(t => t.Numero == foco && foco != 0);
            if (Pulsable(actual))
            {
                return;
            }

            Tarjeta otra = tarjetas.FirstOrDefault(t => marcada.HasValue && t.Numero == marcada.Value && Pulsable(t))
                ?? tarjetas.FirstOrDefault(t => Pulsable(t));
            foco = otra == null ? 0 : otra.Numero;
        }

        private void Pulsar(int numero)
        {
            Action<int> pulsada = TarjetaPulsada;
            if (pulsada != null)
            {
                pulsada(numero);
            }
        }

        // =====================================================================
        // Cuentas y textos
        // =====================================================================

        /// <summary>Los que entraron a la lista despues de la primera clase (su primera bitacora o asistencia dada es de despues).</summary>
        private static int Tarde(ResumenAsistencia r)
        {
            if (r.Lista.Dias.Count == 0)
            {
                return 0;
            }

            DateTime primera = r.Lista.Dias.Min(d => d.Fecha);
            return r.Lista.Alumnos.Count(a => a.Entro != DateTime.MaxValue && a.Entro > primera);
        }

        private static string NombreDe(AlumnoResumido a)
        {
            return string.IsNullOrWhiteSpace(a.Alumno.Nombre) ? a.Alumno.Matricula : a.Alumno.Nombre;
        }

        /// <summary>"lun 12/10", como el encabezado de la hoja.</summary>
        private static string Fecha(DateTime fecha)
        {
            return fecha.ToString("ddd", Espanol).Replace(".", "") + " " + fecha.ToString("dd/MM", Espanol);
        }

        private static string Hora(TimeSpan hora)
        {
            return HorariosDatos.Hora(hora);
        }

        private static string Capital(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0], Espanol) + texto.Substring(1);
        }

        /// <summary>Para unir textos en una frase: sin el punto final y con minuscula.</summary>
        private static string EnFrase(string texto)
        {
            string limpio = (texto ?? "").Trim().TrimEnd('.');
            return limpio.Length == 0 ? limpio : char.ToLower(limpio[0], Espanol) + limpio.Substring(1);
        }

        private static string Enumerar(IList<string> partes)
        {
            if (partes.Count == 0)
            {
                return "";
            }

            return partes.Count == 1 ? partes[0] : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[partes.Count - 1];
        }

        private static string Alumnos(int n)
        {
            return n == 1 ? "1 estudiante" : n + " estudiantes";
        }

        private static string Clases(int n)
        {
            return n == 1 ? "1 clase" : n + " clases";
        }

        private static string Cuenta(int n, string uno, string varios)
        {
            return n == 1 ? "1 " + uno : n + " " + varios;
        }
    }
}
