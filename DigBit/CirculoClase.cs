using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DigBit.Infraestructura;

namespace DigBit
{
    /// <summary>
    /// La sena de cada clase del profesor, igual en todas sus pantallas: el
    /// circulo (su imagen o, si no tiene, sus iniciales en negro sobre el color de
    /// la clase) y el lomo o la banda de su color. Pulsar el circulo, este donde
    /// este, abre "Personalizar tu clase" (PersonalizacionClases.Abrir).
    ///
    /// Los circulos ya compuestos se guardan en una cache por imagen o texto,
    /// color, diametro y aro: pintar uno es copiar un Bitmap pequeno, porque la
    /// tarjeta del codigo y el bloque en curso se repintan 20 veces por segundo.
    /// La cache es solo del hilo de la pantalla y se vacia (liberando los Bitmap)
    /// con cada PersonalizacionClases.Cambio y al cerrar la sesion, entre dos
    /// pintados: nadie debe guardarse un Bitmap de aqui.
    ///
    /// Uso tipico en un OnPaint (P3/P4):
    ///   AspectoClase a = PersonalizacionClases.De(idClase);   // null: pintar como siempre
    ///   if (a != null)
    ///   {
    ///       CirculoClase.PintarLomo(g, forma, tarjeta, a.Color, 4);   // despues del fondo, antes del borde
    ///       Rectangle c = CirculoClase.Rect(x, y, 28);
    ///       CirculoClase.Pintar(g, c, a);
    ///       if (encima) CirculoClase.PintarAnillo(g, c);
    ///   }
    /// y en el raton: CirculoClase.Contiene(c, e.Location) -> PersonalizacionClases.Abrir(this, a.ClaseId).
    /// </summary>
    internal static class CirculoClase
    {
        /// <summary>Lo que el anillo de "encima" o del foco sale por fuera del circulo (por lado).</summary>
        public const int MargenAnillo = 3;

        /// <summary>Lo que pinta PintarLomo ademas de su ancho: 1 px oscurecido del lado de dentro.</summary>
        public const int LineaDelLomo = 1;

        private const int MaximoEnCache = 200;
        private const float Oscurecido = 0.35f;

        private static readonly Color Verde = Color.DarkGreen;

        // Palabras que no dan inicial ("Bases de Datos" = BD).
        private static readonly HashSet<string> Enlaces = new HashSet<string>(StringComparer.Ordinal)
        {
            "de", "del", "la", "las", "el", "los", "y", "e", "a", "al", "en", "para", "con", "por", "o", "u"
        };

        private static readonly string[] Romanos = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        private static readonly Regex Palabra = new Regex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant);

        // Solo del hilo de la pantalla.
        private static readonly Dictionary<string, Bitmap> compuestos = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Bitmap> decodificadas = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> malas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static FontFamily familia;

        /// <summary>
        /// Pinta el circulo de la clase en ese rectangulo (cuadrado; si no, el
        /// lado menor, arriba a la izquierda). Con imagen en memoria, la imagen
        /// dentro de un aro de 2 px del color de la clase; sin ella (o mientras
        /// llega), el texto del circulo en negro sobre su color. Siempre con un
        /// borde de 1 px del color oscurecido. aroBlanco: el aro va blanco y sin
        /// borde, para ponerlo sobre una banda del mismo color (Mis grupos).
        /// Con aspecto null no pinta nada. Solo en el hilo de la pantalla.
        /// </summary>
        public static void Pintar(Graphics g, Rectangle circulo, AspectoClase aspecto, bool aroBlanco = false)
        {
            if (g == null || aspecto == null)
            {
                return;
            }

            int d = Math.Min(circulo.Width, circulo.Height);
            if (d < 6)
            {
                return;
            }

            Bitmap compuesto = Compuesto(aspecto, d, aroBlanco);
            if (compuesto != null)
            {
                // Con el tamano explicito: DrawImageUnscaled escalaria si la
                // resolucion del Bitmap no fuera la del Graphics.
                g.DrawImage(compuesto, new Rectangle(circulo.X, circulo.Y, d, d), 0, 0, d, d, GraphicsUnit.Pixel);
            }
        }

        /// <summary>
        /// El anillo DarkGreen de 2 px que se pinta por fuera del circulo con el
        /// raton encima o con el foco (ocupa MargenAnillo px por lado). El cursor
        /// Hand lo pone quien pinta.
        /// </summary>
        public static void PintarAnillo(Graphics g, Rectangle circulo)
        {
            if (g == null)
            {
                return;
            }

            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen lapiz = new Pen(Verde, 2f))
            {
                g.DrawEllipse(lapiz, new RectangleF(circulo.X - 2f, circulo.Y - 2f, circulo.Width + 3f, circulo.Height + 3f));
            }

            g.SmoothingMode = antes;
        }

        /// <summary>
        /// El lomo: una tira de ancho px del color de la clase pegada al borde
        /// izquierdo de la tarjeta y 1 px de su color oscurecido del lado de
        /// dentro, recortada a la forma (el redondeado). Se pinta despues del
        /// fondo de estado y antes del borde, que queda encima. Respeta el
        /// recorte que ya tenga el Graphics (no usa ResetClip).
        /// </summary>
        public static void PintarLomo(Graphics g, GraphicsPath forma, Rectangle tarjeta, Color color, int ancho = 4)
        {
            if (g == null || ancho <= 0)
            {
                return;
            }

            GraphicsState estado = g.Save();
            try
            {
                if (forma != null)
                {
                    g.SetClip(forma, CombineMode.Intersect);
                }

                using (SolidBrush relleno = new SolidBrush(color))
                using (SolidBrush linea = new SolidBrush(ColorDelBorde(color)))
                {
                    g.FillRectangle(relleno, tarjeta.Left, tarjeta.Top, ancho, tarjeta.Height);
                    g.FillRectangle(linea, tarjeta.Left + ancho, tarjeta.Top, LineaDelLomo, tarjeta.Height);
                }
            }
            finally
            {
                g.Restore(estado);
            }
        }

        /// <summary>
        /// La banda de color de arriba de una tarjeta (Mis grupos: 46 px),
        /// recortada a la forma. Despues del fondo y antes del borde. Sobre ella,
        /// el circulo va con aroBlanco y el texto en negro.
        /// </summary>
        public static void PintarBanda(Graphics g, GraphicsPath forma, Rectangle banda, Color color)
        {
            if (g == null)
            {
                return;
            }

            GraphicsState estado = g.Save();
            try
            {
                if (forma != null)
                {
                    g.SetClip(forma, CombineMode.Intersect);
                }

                using (SolidBrush relleno = new SolidBrush(color))
                {
                    g.FillRectangle(relleno, banda);
                }
            }
            finally
            {
                g.Restore(estado);
            }
        }

        /// <summary>El rectangulo de un circulo de ese diametro con la esquina de arriba a la izquierda en (x, y).</summary>
        public static Rectangle Rect(int x, int y, int diametro)
        {
            return new Rectangle(x, y, diametro, diametro);
        }

        /// <summary>El circulo de ese diametro centrado en vertical en la zona, pegado a su borde izquierdo.</summary>
        public static Rectangle RectCentrado(Rectangle zona, int diametro)
        {
            return new Rectangle(zona.X, zona.Y + (zona.Height - diametro) / 2, diametro, diametro);
        }

        /// <summary>El circulo con su anillo: lo que hay que invalidar al entrar o salir el raton.</summary>
        public static Rectangle ZonaPulsable(Rectangle circulo)
        {
            return Rectangle.Inflate(circulo, MargenAnillo, MargenAnillo);
        }

        /// <summary>True si el punto cae en el circulo (redondo, con 2 px de holgura): para el raton.</summary>
        public static bool Contiene(Rectangle circulo, Point punto)
        {
            if (circulo.Width <= 0 || circulo.Height <= 0)
            {
                return false;
            }

            double radio = Math.Min(circulo.Width, circulo.Height) / 2.0 + 2;
            double dx = punto.X - (circulo.X + circulo.Width / 2.0);
            double dy = punto.Y - (circulo.Y + circulo.Height / 2.0);
            return dx * dx + dy * dy <= radio * radio;
        }

        /// <summary>El color del borde del circulo y de la linea del lomo (como la muestra de SelectorColorProfesor).</summary>
        public static Color ColorDelBorde(Color color)
        {
            return EstiloHorarios.Oscurecer(color, Oscurecido);
        }

        /// <summary>La ayuda del circulo, la misma en todas partes (con los nombres oficiales).</summary>
        public static string Ayuda(AspectoClase aspecto)
        {
            if (aspecto == null)
            {
                return null;
            }

            return "Personaliza " + aspecto.Materia + " (" + aspecto.Grupo + "): tu imagen, tu color y tu nombre. Tus estudiantes ven el círculo al registrarse.";
        }

        /// <summary>"Personalizar Programacion Web, grupo ISC-7001": el nombre para MSAA del circulo.</summary>
        public static string NombreAccesible(AspectoClase aspecto)
        {
            return aspecto == null ? BotonCirculoClase.Texto : "Personalizar " + aspecto.Materia + ", grupo " + aspecto.Grupo;
        }

        /// <summary>
        /// Lo que va en el circulo sin imagen: el nombre corto tal como se
        /// escribio si tiene 4 caracteres o menos; si no, las iniciales de la materia.
        /// </summary>
        public static string TextoDelCirculo(string materia, string nombreCorto)
        {
            string corto = (nombreCorto ?? "").Trim();
            return corto.Length > 0 && corto.Length <= 4 ? corto : Iniciales(materia);
        }

        /// <summary>
        /// Las iniciales de una materia: sin "de", "la", "y"... la primera letra de
        /// hasta 3 palabras, en mayuscula y sin acento; con una sola palabra, sus
        /// dos primeras letras. Un numero final, romano (I a X) o en cifras, se
        /// anade en cifras para que se distingan I y II. Programacion Orientada a
        /// Objetos = POO; Bases de Datos = BD; Contabilidad = CO; Taller de
        /// Investigacion II = TI2.
        /// </summary>
        public static string Iniciales(string materia)
        {
            List<string> palabras = Palabra.Matches(materia ?? "").Cast<Match>().Select(m => m.Value).ToList();
            string numero = "";
            if (palabras.Count > 1)
            {
                string ultima = palabras[palabras.Count - 1];
                int romano = Array.IndexOf(Romanos, ultima.ToUpperInvariant());
                if (ultima.All(char.IsDigit))
                {
                    numero = ultima;
                    palabras.RemoveAt(palabras.Count - 1);
                }
                else if (romano >= 0)
                {
                    numero = (romano + 1).ToString(CultureInfo.InvariantCulture);
                    palabras.RemoveAt(palabras.Count - 1);
                }
            }

            List<string> utiles = palabras.Where(p => !Enlaces.Contains(SinAcentos(p).ToLowerInvariant())).ToList();
            if (utiles.Count == 0)
            {
                utiles = palabras;
            }

            if (utiles.Count == 0)
            {
                return numero.Length > 0 ? numero : "?";
            }

            string letras = utiles.Count == 1
                ? utiles[0].Substring(0, Math.Min(2, utiles[0].Length))
                : string.Concat(utiles.Take(3).Select(p => p.Substring(0, 1)));
            return SinAcentos(letras).ToUpperInvariant() + numero;
        }

        /// <summary>
        /// Libera los circulos compuestos y las imagenes decodificadas. Lo llama
        /// PersonalizacionClases (al cambiar lo que se ve y al cerrar la sesion);
        /// solo en el hilo de la pantalla, entre dos pintados.
        /// </summary>
        public static void VaciarCache()
        {
            foreach (Bitmap b in compuestos.Values)
            {
                b.Dispose();
            }

            foreach (Bitmap b in decodificadas.Values)
            {
                b.Dispose();
            }

            compuestos.Clear();
            decodificadas.Clear();
        }

        /// <summary>
        /// Pinta un circulo sin cache: el color, la imagen (el trozo origen de
        /// ella, que puede ser null) o el texto. Para la vista previa del dialogo,
        /// que cambia con cada arrastre; las pantallas usan Pintar.
        /// </summary>
        internal static void Dibujar(Graphics g, Rectangle circulo, Color color, Image imagen, RectangleF origen, string texto, bool aroBlanco)
        {
            int d = Math.Min(circulo.Width, circulo.Height);
            if (d < 6)
            {
                return;
            }

            SmoothingMode antesSuavizado = g.SmoothingMode;
            InterpolationMode antesInterpolacion = g.InterpolationMode;
            PixelOffsetMode antesDesplazamiento = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            try
            {
                RectangleF disco = new RectangleF(circulo.X, circulo.Y, d, d);
                // El interior: 2 px menos por lado (el aro).
                RectangleF interior = RectangleF.Inflate(disco, -2f, -2f);
                using (SolidBrush fondo = new SolidBrush(aroBlanco ? Color.White : color))
                {
                    g.FillEllipse(fondo, disco);
                }

                if (aroBlanco)
                {
                    using (SolidBrush relleno = new SolidBrush(color))
                    {
                        g.FillEllipse(relleno, interior);
                    }
                }

                if (imagen != null && origen.Width > 0 && origen.Height > 0)
                {
                    DibujarImagen(g, imagen, origen, interior);
                }
                else
                {
                    DibujarTexto(g, texto, aroBlanco ? interior : disco);
                }

                if (!aroBlanco)
                {
                    using (Pen borde = new Pen(ColorDelBorde(color), 1f))
                    {
                        g.DrawEllipse(borde, disco.X + 0.5f, disco.Y + 0.5f, d - 1f, d - 1f);
                    }
                }
            }
            finally
            {
                g.SmoothingMode = antesSuavizado;
                g.InterpolationMode = antesInterpolacion;
                g.PixelOffsetMode = antesDesplazamiento;
            }
        }

        /// <summary>El compuesto de la cache (o se compone ahora). Null si no se pudo.</summary>
        private static Bitmap Compuesto(AspectoClase aspecto, int d, bool aroBlanco)
        {
            Bitmap imagen = Decodificada(aspecto.ImagenSha1);
            string texto = aspecto.TextoCirculo ?? Iniciales(aspecto.Materia);
            string clave = (imagen != null ? "I:" + aspecto.ImagenSha1 : "T:" + texto) + "|" + aspecto.Color.ToArgb().ToString(CultureInfo.InvariantCulture)
                + "|" + d.ToString(CultureInfo.InvariantCulture) + (aroBlanco ? "|b" : "|c");
            Bitmap compuesto;
            if (compuestos.TryGetValue(clave, out compuesto))
            {
                return compuesto;
            }

            if (compuestos.Count >= MaximoEnCache)
            {
                // Muchos tamanos distintos (Mi horario al cambiar de tamano): se empieza de nuevo.
                foreach (Bitmap b in compuestos.Values)
                {
                    b.Dispose();
                }

                compuestos.Clear();
            }

            try
            {
                compuesto = new Bitmap(d, d, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(compuesto))
                {
                    g.Clear(Color.Transparent);
                    Dibujar(g, new Rectangle(0, 0, d, d), aspecto.Color, imagen,
                        imagen == null ? RectangleF.Empty : new RectangleF(0, 0, imagen.Width, imagen.Height), texto, aroBlanco);
                }

                compuestos[clave] = compuesto;
                return compuesto;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OutOfMemoryException || ex is System.Runtime.InteropServices.ExternalException)
            {
                if (compuesto != null)
                {
                    compuesto.Dispose();
                }

                Log.Error("Componer el circulo de una clase", ex);
                return null;
            }
        }

        /// <summary>La imagen de ese SHA1 si esta en memoria y se puede leer; si no, null (y van las iniciales).</summary>
        private static Bitmap Decodificada(string sha1)
        {
            if (string.IsNullOrEmpty(sha1) || malas.Contains(sha1))
            {
                return null;
            }

            Bitmap imagen;
            if (decodificadas.TryGetValue(sha1, out imagen))
            {
                return imagen;
            }

            byte[] datos = PersonalizacionClases.BytesDe(sha1);
            if (datos == null)
            {
                // Todavia no llega: las iniciales hasta entonces.
                return null;
            }

            imagen = ImagenDeClase.DesdeBytes(datos);
            if (imagen == null)
            {
                malas.Add(sha1);
                Log.Aviso("La imagen guardada de una clase (sha1 " + sha1 + ") no se puede leer: se ensenan sus iniciales.");
                return null;
            }

            decodificadas[sha1] = imagen;
            return imagen;
        }

        /// <summary>La imagen recortada a un circulo, con antialias: se escala una vez y se rellena la elipse con ella.</summary>
        private static void DibujarImagen(Graphics g, Image imagen, RectangleF origen, RectangleF interior)
        {
            int lado = Math.Max(1, (int)Math.Ceiling(interior.Width));
            using (Bitmap escalada = new Bitmap(lado, lado, PixelFormat.Format32bppPArgb))
            {
                using (Graphics ge = Graphics.FromImage(escalada))
                using (ImageAttributes bordes = new ImageAttributes())
                {
                    ge.Clear(Color.Transparent);
                    ge.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    ge.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    bordes.SetWrapMode(WrapMode.TileFlipXY);
                    ge.DrawImage(imagen, new Rectangle(0, 0, lado, lado), origen.X, origen.Y, origen.Width, origen.Height, GraphicsUnit.Pixel, bordes);
                }

                using (TextureBrush pincel = new TextureBrush(escalada, WrapMode.Clamp))
                {
                    pincel.TranslateTransform(interior.X, interior.Y);
                    g.FillEllipse(pincel, interior);
                }
            }
        }

        /// <summary>
        /// El texto en Century Gothic negrita, en negro y centrado: de d x 0,42,
        /// 0,34 o 0,28 px segun tenga 1-2, 3 o 4 caracteres, bajando de medio en
        /// medio hasta caber en d - 6. Como figura (no con TextRenderer, que deja
        /// huecos transparentes en un Bitmap con transparencia).
        /// </summary>
        private static void DibujarTexto(Graphics g, string texto, RectangleF zona)
        {
            texto = (texto ?? "").Trim();
            if (texto.Length == 0)
            {
                return;
            }

            float d = Math.Min(zona.Width, zona.Height);
            float factor = texto.Length <= 2 ? 0.42f : texto.Length == 3 ? 0.34f : 0.28f;
            float tamano = d * factor;
            float maximo = d - 6f;
            FontFamily letra = Familia();
            GraphicsPath figura = null;
            try
            {
                while (true)
                {
                    figura = new GraphicsPath();
                    figura.AddString(texto, letra, (int)FontStyle.Bold, tamano, PointF.Empty, StringFormat.GenericTypographic);
                    RectangleF caja = figura.GetBounds();
                    if (caja.Width <= maximo || tamano <= 5f)
                    {
                        using (Matrix mover = new Matrix())
                        {
                            mover.Translate(zona.X + zona.Width / 2f - (caja.X + caja.Width / 2f), zona.Y + zona.Height / 2f - (caja.Y + caja.Height / 2f));
                            figura.Transform(mover);
                        }

                        g.FillPath(Brushes.Black, figura);
                        return;
                    }

                    figura.Dispose();
                    figura = null;
                    tamano -= 0.5f;
                }
            }
            finally
            {
                if (figura != null)
                {
                    figura.Dispose();
                }
            }
        }

        private static FontFamily Familia()
        {
            if (familia == null)
            {
                try
                {
                    familia = new FontFamily("Century Gothic");
                }
                catch (ArgumentException)
                {
                    familia = FontFamily.GenericSansSerif;
                }
            }

            return familia;
        }

        private static string SinAcentos(string texto)
        {
            StringBuilder limpio = new StringBuilder(texto.Length);
            foreach (char c in texto.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    limpio.Append(c);
                }
            }

            return limpio.ToString().Normalize(NormalizationForm.FormC);
        }
    }

    /// <summary>
    /// El circulo de una clase como boton: Tab, Enter y Espacio, y la accion por
    /// defecto de MSAA (la de Pulsar en las pruebas) son las de cualquier boton,
    /// sin codigo extra. Text es siempre "Personalizar la clase" (lo que buscan
    /// las pruebas); el nombre de la clase va en AccessibleName y en la ayuda.
    /// El control mide MargenAnillo px mas por lado que el circulo, para el
    /// anillo del raton y del foco. Patron de BotonCabecera.
    ///
    /// Uso: btn = new BotonCirculoClase(48) { FondoDetras = FondoFicha };
    ///      btn.Click += (s, e) => { if (btn.Aspecto != null) Personalizar(btn.Aspecto.ClaseId); };
    ///      y al pintar o con PersonalizacionClases.Cambio: btn.Aspecto = PersonalizacionClases.De(id)
    ///      (null: el dueno lo oculta).
    /// </summary>
    internal class BotonCirculoClase : Button
    {
        public const string Texto = "Personalizar la clase";

        private readonly ToolTip ayuda = new ToolTip();
        private AspectoClase aspecto;
        private int diametro;
        private Color fondoDetras;
        private bool aroBlanco;
        private bool encima;

        public BotonCirculoClase() : this(48)
        {
        }

        public BotonCirculoClase(int diametro)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            TabStop = true;
            Text = Texto;
            AccessibleName = Texto;
            Diametro = diametro;
        }

        /// <summary>El del circulo; el control mide Diametro + 2 * MargenAnillo.</summary>
        public int Diametro
        {
            get
            {
                return diametro;
            }

            set
            {
                int valor = Math.Max(12, value);
                if (valor == diametro)
                {
                    return;
                }

                diametro = valor;
                Size = new Size(diametro + 2 * CirculoClase.MargenAnillo, diametro + 2 * CirculoClase.MargenAnillo);
                Invalidate();
            }
        }

        /// <summary>
        /// La clase que ensena (null: nada, y el dueno lo oculta). Se compara por
        /// referencia: PersonalizacionClases sustituye el aspecto entero cuando
        /// cambia, asi que ponerlo en cada pintado no repinta de mas.
        /// </summary>
        public AspectoClase Aspecto
        {
            get
            {
                return aspecto;
            }

            set
            {
                if (ReferenceEquals(value, aspecto))
                {
                    return;
                }

                aspecto = value;
                AccessibleName = CirculoClase.NombreAccesible(value);
                ayuda.SetToolTip(this, CirculoClase.Ayuda(value));
                Invalidate();
            }
        }

        /// <summary>El id de la clase que ensena, o 0.</summary>
        public int ClaseId
        {
            get { return aspecto == null ? 0 : aspecto.ClaseId; }
        }

        /// <summary>
        /// El color de detras, para las esquinas fuera del circulo, cuando no es
        /// el del contenedor (sobre la ficha de la clase del profesor, su verde
        /// claro). Vacio: el del contenedor.
        /// </summary>
        public Color FondoDetras
        {
            get
            {
                return fondoDetras;
            }

            set
            {
                if (value == fondoDetras)
                {
                    return;
                }

                fondoDetras = value;
                Invalidate();
            }
        }

        /// <summary>Aro blanco y sin borde (sobre una banda del color de la clase).</summary>
        public bool AroBlanco
        {
            get
            {
                return aroBlanco;
            }

            set
            {
                if (value == aroBlanco)
                {
                    return;
                }

                aroBlanco = value;
                Invalidate();
            }
        }

        /// <summary>El circulo dentro del control.</summary>
        public Rectangle RectCirculo
        {
            get { return new Rectangle(CirculoClase.MargenAnillo, CirculoClase.MargenAnillo, diametro, diametro); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            encima = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            encima = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            Invalidate();
            base.OnChangeUICues(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // Fuera del circulo, el fondo de donde esta (Button no lo pinta solo).
            g.Clear(!fondoDetras.IsEmpty ? fondoDetras : Parent != null ? Parent.BackColor : Color.White);
            if (aspecto == null)
            {
                return;
            }

            Rectangle circulo = RectCirculo;
            CirculoClase.Pintar(g, circulo, aspecto, aroBlanco);
            if (encima || (Focused && ShowFocusCues))
            {
                CirculoClase.PintarAnillo(g, circulo);
            }
        }
    }
}
