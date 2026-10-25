using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using DigBit.conexion;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Una de las cinco cifras grandes de Estadisticas: un recuadro redondeado
    /// como las cifras de la ficha del docente (TarjetaCodigo.Registro, que no se
    /// toca), con el numero en grande, debajo que es (Etiqueta) y un renglon de
    /// detalle que puede llevar un tramo en rojo (ParteRoja). Se pulsa con el
    /// raton, Enter (al bajarla, una vez aunque se deje pulsada) o Espacio (al
    /// soltarlo, como un boton) y lanza Click: la ventana elige su pestana y su
    /// orden. Recien creada, y mientras se lee, ensena "..." en gris (Leyendo).
    /// AccessibleName se arma solo con la etiqueta, el valor y el detalle
    /// enteros; la ayuda va en su ToolTip y en AccessibleDescription. Si el
    /// numero (ya a 18 pt) o el detalle no caben y se cortan con "...", el
    /// ToolTip empieza con la cifra entera. Text es la etiqueta con mayuscula
    /// (no cambia con las cifras: asi la encuentran las pruebas).
    /// </summary>
    internal class CifraGrande : Control
    {
        /// <summary>El alto del recuadro en la ventana de Estadisticas.</summary>
        public const int Alto = 92;
        /// <summary>Lo que se ensena sin dato (en gris, y "sin dato" en el AccessibleName).</summary>
        public const string SinDato = "—";

        /// <summary>Los colores del numero: verde (lo que va bien o se cuenta), rojo bajo el 80 %, naranja en fallas y avisos, gris sin dato o en cero.</summary>
        public static readonly Color ColorVerde = Color.FromArgb(46, 125, 50);
        public static readonly Color ColorRojo = HojaAsistencia.FaltaTexto;
        public static readonly Color ColorNaranja = Color.FromArgb(190, 85, 0);
        public static readonly Color ColorGris = Color.FromArgb(150, 150, 150);

        // Los de las cifras de la ficha del docente (TarjetasProfesor.cs).
        private static readonly Color Fondo = Color.FromArgb(244, 250, 244);
        private static readonly Color FondoEncima = Color.FromArgb(236, 246, 236);
        private static readonly Color Borde = Color.FromArgb(205, 225, 205);
        private static readonly Color TextoEtiqueta = Color.FromArgb(75, 75, 75);
        // Gris oscuro: a 8.5 pt sobre el fondo de la ficha se lee (contraste de 5:1).
        private static readonly Color TextoDetalle = Color.FromArgb(105, 105, 105);
        private const int Radio = 8;
        // Lo que queda a cada lado del texto dentro del recuadro.
        private const int Margen = 6;
        private const int EntreNumeroYEtiqueta = 1;
        private const int EntreEtiquetaYDetalle = 2;
        // Ancho de la ayuda en caracteres: el ToolTip no parte solo un texto largo.
        private const int AnchoDeLaAyuda = 90;
        private const TextFormatFlags EnLinea = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            | TextFormatFlags.VerticalCenter;
        private const int WM_KEYDOWN = 0x0100;

        // El numero en 22 pt y, si no cabe, en 20 y luego en 18.
        private readonly DrawingFont[] fuentesNumero =
        {
            new DrawingFont("Century Gothic", 22F, FontStyle.Bold),
            new DrawingFont("Century Gothic", 20F, FontStyle.Bold),
            new DrawingFont("Century Gothic", 18F, FontStyle.Bold)
        };

        private readonly DrawingFont fuenteEtiqueta = new DrawingFont("Century Gothic", 8.5F);
        private readonly DrawingFont fuenteDetalle = new DrawingFont("Century Gothic", 8.5F);
        // El tramo en rojo, en negrita como el "16 sin derecho" de Mis grupos.
        private readonly DrawingFont fuenteRojo = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private readonly ToolTip ayudaRaton = new ToolTip { InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 30000 };

        private string valor = SinDato;
        private string etiqueta = "";
        private string detalle = "";
        private string parteRoja;
        private string ayuda;
        private Color colorValor = ColorVerde;
        private bool leyendo = true;
        private bool encima;
        private bool pulsado;
        // Espacio abajo sobre la cifra: pulsa al soltarlo, como un boton.
        private bool espacioAbajo;
        // Si el ultimo WM_KEYDOWN era una repeticion (la tecla se dejo pulsada).
        private bool teclaRepetida;
        // Si al pintar se corto el numero, la etiqueta o el detalle: entonces la ayuda los lleva enteros.
        private bool recortada;

        public CifraGrande()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            // Dos clics seguidos son dos pulsaciones, no un doble clic.
            SetStyle(ControlStyles.StandardDoubleClick, false);
            // Sin BackColor propio: las esquinas fuera del recuadro toman el fondo de
            // donde se ponga (blanco en Estadisticas).
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(250, Alto);
            AccessibleRole = AccessibleRole.PushButton;
            Actualizar();
        }

        /// <summary>El numero en grande ya con su formato ("84 %", "128 de 140", "1,284"); null, vacio o en blanco, la raya de SinDato.</summary>
        public string Valor
        {
            get
            {
                return valor;
            }

            set
            {
                string nuevo = ValorOSinDato(value);
                if (nuevo == valor)
                {
                    return;
                }

                valor = nuevo;
                Actualizar();
            }
        }

        /// <summary>Que es, en minusculas: "asistencia de los estudiantes". El AccessibleName y Text la llevan con mayuscula.</summary>
        public string Etiqueta
        {
            get
            {
                return etiqueta;
            }

            set
            {
                string nuevo = EnUnRenglon(value);
                if (nuevo == etiqueta)
                {
                    return;
                }

                etiqueta = nuevo;
                Actualizar();
            }
        }

        /// <summary>El renglon de debajo ("12 estudiantes bajo el 80 %"); si no cabe, se corta con "...".</summary>
        public string Detalle
        {
            get
            {
                return detalle;
            }

            set
            {
                string nuevo = EnUnRenglon(value);
                if (nuevo == detalle)
                {
                    return;
                }

                detalle = nuevo;
                Actualizar();
            }
        }

        /// <summary>El color del numero (ColorVerde, ColorRojo, ColorNaranja o ColorGris; Color.Empty, verde). Sin dato (SinDato) o leyendo, siempre gris.</summary>
        public Color ColorValor
        {
            get
            {
                return colorValor;
            }

            set
            {
                Color nuevo = value.IsEmpty ? ColorVerde : value;
                if (nuevo == colorValor)
                {
                    return;
                }

                colorValor = nuevo;
                Invalidate();
            }
        }

        /// <summary>
        /// El tramo del detalle que va en RojoFalta: "12 sin bitacoras" en "12 sin
        /// bitacoras, 5 canceladas". Si aparece varias veces, la primera que no
        /// empieza ni acaba a media palabra o a medio numero; si no aparece, o es
        /// null, nada en rojo.
        /// </summary>
        public string ParteRoja
        {
            get
            {
                return parteRoja;
            }

            set
            {
                string nuevo = string.IsNullOrEmpty(value) ? null : EnUnRenglon(value);
                if (nuevo == parteRoja)
                {
                    return;
                }

                parteRoja = nuevo;
                Invalidate();
            }
        }

        /// <summary>Lo que explica la cifra: su ToolTip (partido en renglones) y su AccessibleDescription. Mientras lee no se ensena.</summary>
        public string Ayuda
        {
            get
            {
                return ayuda;
            }

            set
            {
                string nuevo = string.IsNullOrEmpty(value) ? null : value;
                if (nuevo == ayuda)
                {
                    return;
                }

                ayuda = nuevo;
                Actualizar();
            }
        }

        /// <summary>Mientras se leen las estadisticas: "..." en gris, sin detalle ni ayuda. Empieza en true.</summary>
        public bool Leyendo
        {
            get
            {
                return leyendo;
            }

            set
            {
                if (value == leyendo)
                {
                    return;
                }

                leyendo = value;
                Actualizar();
            }
        }

        /// <summary>El tamano (22, 20 o 18 pt) con que se pinto el numero la ultima vez (para las pruebas).</summary>
        internal float TamanoPintado { get; private set; }

        /// <summary>Si el detalle se corto con "..." la ultima vez que se pinto (para las pruebas).</summary>
        internal bool DetalleCortado { get; private set; }

        /// <summary>Si el numero no cupo ni a 18 pt y se corto con "..." la ultima vez que se pinto.</summary>
        internal bool NumeroCortado { get; private set; }

        /// <summary>Si la etiqueta se corto con "..." la ultima vez que se pinto.</summary>
        internal bool EtiquetaCortada { get; private set; }

        /// <summary>Todo a la vez, con una sola pintura, y deja de estar Leyendo.</summary>
        public void Mostrar(string nuevoValor, Color color, string nuevoDetalle, string nuevaParteRoja, string nuevaAyuda)
        {
            valor = ValorOSinDato(nuevoValor);
            colorValor = color.IsEmpty ? ColorVerde : color;
            detalle = EnUnRenglon(nuevoDetalle);
            parteRoja = string.IsNullOrEmpty(nuevaParteRoja) ? null : EnUnRenglon(nuevaParteRoja);
            ayuda = string.IsNullOrEmpty(nuevaAyuda) ? null : nuevaAyuda;
            leyendo = false;
            Actualizar();
        }

        /// <summary>Como un clic: lanza Click (tambien lo usa la accion por defecto de MSAA).</summary>
        public void Pulsar()
        {
            OnClick(EventArgs.Empty);
        }

        /// <summary>
        /// "Asistencia de los estudiantes: 84 %, 12 estudiantes bajo el 80 %";
        /// sin dato, "...: sin dato"; mientras lee, "...: leyendo...".
        /// </summary>
        internal string NombreAccesible()
        {
            string nombre = ConMayuscula(etiqueta);
            string antes = nombre.Length > 0 ? nombre + ": " : "";
            if (leyendo)
            {
                return antes + "leyendo...";
            }

            string dicho = valor == SinDato ? "sin dato" : valor;
            return antes + dicho + (detalle.Length > 0 ? ", " + detalle : "");
        }

        private void Actualizar()
        {
            string nombre = NombreAccesible();
            string descripcion = leyendo ? null : ayuda;
            bool otroNombre = nombre != AccessibleName;
            bool otraDescripcion = descripcion != AccessibleDescription;
            AccessibleName = nombre;
            AccessibleDescription = descripcion;
            Text = ConMayuscula(etiqueta);
            PonerAyuda();
            // El lector de pantalla vuelve a leer la cifra que tiene el foco cuando cambia.
            if (IsHandleCreated)
            {
                if (otroNombre)
                {
                    AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
                }

                if (otraDescripcion)
                {
                    AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
                }
            }

            Invalidate();
        }

        /// <summary>Sin saltos de linea (se pinta en un renglon); null, vacio.</summary>
        private static string EnUnRenglon(string texto)
        {
            return string.IsNullOrEmpty(texto) ? "" : texto.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        private static string ValorOSinDato(string texto)
        {
            string limpio = EnUnRenglon(texto);
            return limpio.Trim().Length == 0 ? SinDato : limpio;
        }

        /// <summary>
        /// El ToolTip: la Ayuda y, si el numero, la etiqueta o el detalle salieron
        /// cortados con "...", antes la cifra entera como en el AccessibleName.
        /// Mientras lee, nada.
        /// </summary>
        private void PonerAyuda()
        {
            if (IsDisposed)
            {
                return;
            }

            string texto = null;
            if (!leyendo)
            {
                texto = recortada ? NombreAccesible() + "." + (ayuda == null ? "" : "\n\n" + ayuda) : ayuda;
            }

            ayudaRaton.SetToolTip(this, texto == null ? null : EnRenglones(texto, AnchoDeLaAyuda));
        }

        private static string ConMayuscula(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return "";
            }

            return char.ToUpper(texto[0], CultureInfo.GetCultureInfo("es-MX")) + texto.Substring(1);
        }

        /// <summary>
        /// El texto en renglones de hasta maximo caracteres, partidos en los
        /// espacios (los saltos de linea que ya traiga se respetan): el ToolTip de
        /// Windows solo parte al ancho de la pantalla.
        /// </summary>
        internal static string EnRenglones(string texto, int maximo)
        {
            if (string.IsNullOrEmpty(texto) || texto.Length <= maximo)
            {
                return texto;
            }

            StringBuilder salida = new StringBuilder(texto.Length + 16);
            string[] parrafos = texto.Replace("\r\n", "\n").Split('\n');
            for (int p = 0; p < parrafos.Length; p++)
            {
                if (p > 0)
                {
                    salida.Append('\n');
                }

                int enRenglon = 0;
                foreach (string palabra in parrafos[p].Split(' '))
                {
                    if (palabra.Length == 0)
                    {
                        continue;
                    }

                    if (enRenglon > 0 && enRenglon + 1 + palabra.Length > maximo)
                    {
                        salida.Append('\n');
                        enRenglon = 0;
                    }
                    else if (enRenglon > 0)
                    {
                        salida.Append(' ');
                        enRenglon++;
                    }

                    salida.Append(palabra);
                    enRenglon += palabra.Length;
                }
            }

            return salida.ToString();
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new CifraAccesible(this);
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
            pulsado = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                pulsado = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (pulsado && !espacioAbajo)
            {
                pulsado = false;
                Invalidate();
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            // Si se pierde el raton a media pulsacion (Alt+Tab, una ventana encima), ya no esta pulsada.
            if (pulsado && !espacioAbajo)
            {
                pulsado = false;
                Invalidate();
            }

            base.OnMouseCaptureChanged(e);
        }

        protected override void OnClick(EventArgs e)
        {
            // Solo el boton izquierdo pulsa (Control tambien lanza Click con el derecho).
            MouseEventArgs raton = e as MouseEventArgs;
            if (raton != null && raton.Button != MouseButtons.Left)
            {
                return;
            }

            base.OnClick(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            // Sin el foco, el Espacio que estaba abajo ya no pulsa al soltarse.
            espacioAbajo = false;
            pulsado = false;
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override bool IsInputKey(Keys tecla)
        {
            // Enter llega aqui y no al boton de aceptar del formulario.
            return tecla == Keys.Enter || base.IsInputKey(tecla);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_KEYDOWN)
            {
                // Bit 30 del lParam: la tecla ya estaba abajo (Windows repite el WM_KEYDOWN si se deja pulsada).
                teclaRepetida = (m.LParam.ToInt64() & 0x40000000) != 0;
            }

            base.WndProc(ref m);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyData == Keys.Enter)
            {
                // Pulsa al bajar, una sola vez aunque se deje pulsada.
                e.Handled = true;
                if (!teclaRepetida)
                {
                    Pulsar();
                }
            }
            else if (e.KeyData == Keys.Space)
            {
                // Como un boton y como las tarjetas de Mis grupos: se ve pulsada y pulsa al soltarla.
                e.Handled = true;
                if (!espacioAbajo)
                {
                    espacioAbajo = true;
                    pulsado = true;
                    Invalidate();
                }
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && espacioAbajo)
            {
                espacioAbajo = false;
                pulsado = false;
                Invalidate();
                e.Handled = true;
                Pulsar();
            }

            base.OnKeyUp(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (DrawingFont fuente in fuentesNumero)
                {
                    fuente.Dispose();
                }

                fuenteEtiqueta.Dispose();
                fuenteDetalle.Dispose();
                fuenteRojo.Dispose();
                ayudaRaton.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Mientras la ventana la acomoda puede medir casi nada: solo el fondo.
            if (Width < 8 || Height < 8)
            {
                return;
            }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle recuadro = new Rectangle(1, 1, Width - 3, Height - 3);
            bool resaltado = Focused || pulsado;
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(recuadro, Radio))
            using (SolidBrush fondo = new SolidBrush(encima || pulsado ? FondoEncima : Fondo))
            using (Pen borde = new Pen(resaltado ? Color.DarkGreen : Borde, resaltado ? 2f : 1f))
            {
                g.FillPath(fondo, forma);
                g.DrawPath(borde, forma);
            }

            g.SmoothingMode = SmoothingMode.None;
            int ancho = Math.Max(10, Width - 2 * Margen);
            int altoNumero = TextRenderer.MeasureText(g, "0", fuentesNumero[0], Size.Empty, TextFormatFlags.NoPadding).Height;
            int altoEtiqueta = TextRenderer.MeasureText(g, "Ag", fuenteEtiqueta, Size.Empty, TextFormatFlags.NoPadding).Height;
            int altoDetalle = TextRenderer.MeasureText(g, "Ag", fuenteDetalle, Size.Empty, TextFormatFlags.NoPadding).Height;
            // Siempre el sitio del detalle: asi los numeros de las cinco quedan a la misma altura.
            int total = altoNumero + EntreNumeroYEtiqueta + altoEtiqueta + EntreEtiquetaYDetalle + altoDetalle;
            int y = Math.Max(3, (Height - total) / 2);

            string numero = leyendo ? "..." : valor;
            Color color = leyendo || valor == SinDato ? ColorGris : colorValor;
            DrawingFont fuente = FuenteQueCabe(g, numero, ancho);
            TamanoPintado = fuente.Size;
            TextRenderer.DrawText(g, numero, fuente, new Rectangle(Margen, y, ancho, altoNumero), color,
                EnLinea | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            y += altoNumero + EntreNumeroYEtiqueta;

            TextRenderer.DrawText(g, etiqueta, fuenteEtiqueta, new Rectangle(Margen, y, ancho, altoEtiqueta), TextoEtiqueta,
                EnLinea | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            EtiquetaCortada = Medir(g, etiqueta, fuenteEtiqueta) > ancho;
            y += altoEtiqueta + EntreEtiquetaYDetalle;

            DetalleCortado = false;
            if (!leyendo && detalle.Length > 0)
            {
                PintarDetalle(g, new Rectangle(Margen, y, ancho, altoDetalle));
            }

            bool cortada = !leyendo && (NumeroCortado || EtiquetaCortada || DetalleCortado);
            if (cortada != recortada)
            {
                // La ayuda cambia despues de pintar, no a media pintura.
                recortada = cortada;
                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(PonerAyuda));
                }
            }
        }

        /// <summary>La de 22 pt si el numero cabe; si no, la de 20 y luego la de 18 (que se corta con "..." si aun no cabe).</summary>
        private DrawingFont FuenteQueCabe(Graphics g, string numero, int ancho)
        {
            NumeroCortado = false;
            foreach (DrawingFont fuente in fuentesNumero)
            {
                if (TextRenderer.MeasureText(g, numero, fuente, Size.Empty, TextFormatFlags.NoPadding).Width <= ancho)
                {
                    return fuente;
                }
            }

            NumeroCortado = true;
            return fuentesNumero[fuentesNumero.Length - 1];
        }

        /// <summary>
        /// El detalle centrado en su renglon, con ParteRoja en RojoFalta y en
        /// negrita. Si no cabe, lo mas largo que quepa con "..." al final (del
        /// color del tramo donde se corta).
        /// </summary>
        private void PintarDetalle(Graphics g, Rectangle renglon)
        {
            int inicioRojo = InicioDeRojo();
            int finRojo = inicioRojo < 0 ? -1 : inicioRojo + parteRoja.Length;
            int largo = detalle.Length;
            bool cortado = AnchoDe(g, largo, inicioRojo, finRojo, false) > renglon.Width;
            if (cortado)
            {
                // El mayor largo que quepa con "..." (busqueda binaria: el ancho crece con el largo).
                int bajo = 0;
                int alto = detalle.Length - 1;
                while (bajo < alto)
                {
                    int medio = (bajo + alto + 1) / 2;
                    if (AnchoDe(g, medio, inicioRojo, finRojo, true) <= renglon.Width)
                    {
                        bajo = medio;
                    }
                    else
                    {
                        alto = medio - 1;
                    }
                }

                largo = bajo;
            }

            DetalleCortado = cortado;
            int x = renglon.Left + Math.Max(0, (renglon.Width - AnchoDe(g, largo, inicioRojo, finRojo, cortado)) / 2);
            foreach (Tramo tramo in Tramos(largo, inicioRojo, finRojo, cortado))
            {
                DrawingFont fuente = tramo.Rojo ? fuenteRojo : fuenteDetalle;
                int anchoTramo = Medir(g, tramo.Texto, fuente);
                TextRenderer.DrawText(g, tramo.Texto, fuente, new Rectangle(x, renglon.Top, Math.Max(1, Math.Min(anchoTramo + 2, renglon.Right - x)), renglon.Height),
                    tramo.Rojo ? PestanaCalendario.RojoFalta : TextoDetalle, EnLinea | TextFormatFlags.Left);
                x += anchoTramo;
            }
        }

        /// <summary>
        /// Donde empieza ParteRoja en el detalle (-1: no esta): la primera vez que
        /// no empieza ni acaba a media palabra o a medio numero ("2 sin bitacoras"
        /// no se pinta dentro de "12 sin bitacoras"); si no hay ninguna asi, la primera.
        /// </summary>
        internal int InicioDeRojo()
        {
            if (parteRoja == null)
            {
                return -1;
            }

            int primera = detalle.IndexOf(parteRoja, StringComparison.Ordinal);
            for (int i = primera; i >= 0; i = detalle.IndexOf(parteRoja, i + 1, StringComparison.Ordinal))
            {
                int fin = i + parteRoja.Length;
                bool libreAntes = i == 0 || !char.IsLetterOrDigit(detalle[i - 1]) || !char.IsLetterOrDigit(parteRoja[0]);
                bool libreDespues = fin >= detalle.Length || !char.IsLetterOrDigit(detalle[fin]) || !char.IsLetterOrDigit(parteRoja[parteRoja.Length - 1]);
                if (libreAntes && libreDespues)
                {
                    return i;
                }
            }

            return primera;
        }

        private int AnchoDe(Graphics g, int largo, int inicioRojo, int finRojo, bool conPuntos)
        {
            int ancho = 0;
            foreach (Tramo tramo in Tramos(largo, inicioRojo, finRojo, conPuntos))
            {
                ancho += Medir(g, tramo.Texto, tramo.Rojo ? fuenteRojo : fuenteDetalle);
            }

            return ancho;
        }

        private static int Medir(Graphics g, string texto, DrawingFont fuente)
        {
            return texto.Length == 0 ? 0 : TextRenderer.MeasureText(g, texto, fuente, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        }

        private struct Tramo
        {
            public string Texto;
            public bool Rojo;
        }

        /// <summary>
        /// Los primeros largo caracteres del detalle en tramos (antes del rojo, el
        /// rojo y despues) y, con conPuntos, "..." al final sin los espacios de
        /// antes. Los espacios de las orillas de cada tramo van como espacios duros
        /// para que se midan y se pinten igual.
        /// </summary>
        private List<Tramo> Tramos(int largo, int inicioRojo, int finRojo, bool conPuntos)
        {
            string visible = detalle.Substring(0, Math.Max(0, Math.Min(largo, detalle.Length)));
            if (conPuntos)
            {
                visible = visible.TrimEnd();
            }

            List<Tramo> tramos = new List<Tramo>(4);
            int corte = visible.Length;
            if (inicioRojo < 0 || inicioRojo >= corte)
            {
                Agregar(tramos, visible, false);
            }
            else
            {
                Agregar(tramos, visible.Substring(0, inicioRojo), false);
                Agregar(tramos, visible.Substring(inicioRojo, Math.Min(finRojo, corte) - inicioRojo), true);
                if (finRojo < corte)
                {
                    Agregar(tramos, visible.Substring(finRojo), false);
                }
            }

            if (conPuntos)
            {
                // Del color del tramo donde se corto: rojo si el corte cayo dentro del rojo.
                bool enRojo = inicioRojo >= 0 && corte > inicioRojo && corte < finRojo;
                Agregar(tramos, "...", enRojo);
            }

            return tramos;
        }

        private static void Agregar(List<Tramo> tramos, string texto, bool rojo)
        {
            if (texto.Length == 0)
            {
                return;
            }

            char[] letras = texto.ToCharArray();
            if (letras[0] == ' ')
            {
                letras[0] = '\u00A0';
            }

            if (letras[letras.Length - 1] == ' ')
            {
                letras[letras.Length - 1] = '\u00A0';
            }

            tramos.Add(new Tramo { Texto = new string(letras), Rojo = rojo });
        }

        /// <summary>Boton para MSAA: su accion por defecto es "Pulsar" (como un clic).</summary>
        private sealed class CifraAccesible : ControlAccessibleObject
        {
            private readonly CifraGrande cifra;

            public CifraAccesible(CifraGrande cifra)
                : base(cifra)
            {
                this.cifra = cifra;
            }

            public override string DefaultAction
            {
                get
                {
                    return "Pulsar";
                }
            }

            public override void DoDefaultAction()
            {
                cifra.Pulsar();
            }
        }
    }

    /// <summary>Los colores de la barrita de BarraEnCelda.</summary>
    internal enum EstiloBarra
    {
        /// <summary>Entro, Asistencia, Asistencia del grupo: los del medidor de Mis grupos (verde con derecho, rojo sin el) con la marca del 80 %.</summary>
        Asistencia,

        /// <summary>Clases con bitacoras: los de la linea de tiempo de la ficha del docente (pista verde clara, relleno verde).</summary>
        Clases,

        /// <summary>Fallo en (Equipos): la pista de Clases con el relleno naranja.</summary>
        FalloEn
    }

    /// <summary>
    /// El texto de una celda ("9 de 10", "85 %") arriba y, debajo, una barra
    /// redondeada de 5 px con la fraccion (de 0 a 1). Para el CellPainting de las
    /// tablas de Estadisticas: pinta el fondo, la seleccion y los bordes de la
    /// propia tabla y deja e.Handled = true. Sin fraccion (null), solo el texto
    /// (la raya, "No registro") en gris, centrado en la fila como las demas celdas.
    /// No toca HojaAsistencia: usa sus colores internal y copia la pista y la marca.
    /// </summary>
    internal static class BarraEnCelda
    {
        public const int AltoBarra = 5;
        // Como el texto de las demas celdas: unos 6 px a la izquierda y 7 a la derecha (con la linea de la cuadricula).
        private const int MargenIzquierdo = 6;
        private const int MargenDerecho = 7;
        private const int RadioBarra = 3;

        // Asistencia: la pista de la hoja (PistaBarra, privada en HojaAsistencia.cs) y su marca del minimo.
        private static readonly Color PistaAsistencia = Color.FromArgb(236, 236, 236);
        private static readonly Color MarcaMinimo = Color.FromArgb(70, 70, 70);
        // Clases y equipos: la linea de tiempo de la ficha del docente.
        private static readonly Color PistaClases = Color.FromArgb(214, 232, 214);
        private static readonly Color RellenoClases = Color.FromArgb(46, 125, 50);
        private static readonly Color RellenoFalloEn = Color.FromArgb(190, 85, 0);
        private static readonly Color TextoSinDato = Color.FromArgb(150, 150, 150);
        // En la fila elegida (198,230,198) el gris de siempre casi no se lee (2.2:1); este, 4:1.
        private static readonly Color TextoSinDatoElegida = Color.FromArgb(105, 105, 105);

        /// <summary>X de N como fraccion de 0 a 1; null con N = 0 (sin dato).</summary>
        public static double? Fraccion(int x, int n)
        {
            if (n <= 0)
            {
                return null;
            }

            return Math.Max(0.0, Math.Min(1.0, x / (double)n));
        }

        /// <summary>
        /// Pinta la celda de e con su texto (null: el de la celda, su FormattedValue)
        /// y la barra de la fraccion (0 a 1; null, sin barra y el texto en gris).
        /// El texto sigue la alineacion horizontal de su estilo; el color, el de su
        /// estilo (o el de la fila elegida). Si la fila no deja sitio a la barra,
        /// solo el texto.
        /// </summary>
        public static void Pintar(DataGridViewCellPaintingEventArgs e, string texto, double? fraccion, EstiloBarra estilo)
        {
            if (e == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (fraccion.HasValue && (double.IsNaN(fraccion.Value) || double.IsInfinity(fraccion.Value)))
            {
                fraccion = null;
            }

            if (texto == null)
            {
                texto = Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture);
            }

            // Fondo, seleccion, bordes y foco: los de la tabla (solo las partes que pidio); el contenido, aqui.
            e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
            e.Handled = true;
            if ((e.PaintParts & DataGridViewPaintParts.ContentForeground) == 0)
            {
                return;
            }

            Graphics g = e.Graphics;
            Rectangle celda = e.CellBounds;
            Padding relleno = e.CellStyle.Padding;
            // Sin la linea de la cuadricula de abajo.
            Rectangle zona = new Rectangle(celda.Left + MargenIzquierdo + relleno.Left, celda.Top + relleno.Top,
                celda.Width - MargenIzquierdo - MargenDerecho - relleno.Horizontal, celda.Height - 1 - relleno.Vertical);
            if (zona.Width <= 0 || zona.Height <= 0)
            {
                return;
            }

            bool elegida = (e.State & DataGridViewElementStates.Selected) != 0;
            DrawingFont fuente = e.CellStyle.Font ?? Control.DefaultFont;
            // PreserveGraphicsClipping, como el texto de la propia tabla: respeta el
            // recorte que pone la tabla al pintar (la barra, de GDI+, ya lo respeta).
            TextFormatFlags formato = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping
                | Horizontal(e.CellStyle.Alignment);
            if (!fraccion.HasValue)
            {
                TextRenderer.DrawText(g, texto, fuente, zona, elegida ? TextoSinDatoElegida : TextoSinDato, formato);
                return;
            }

            Color letra = elegida ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
            if (letra.IsEmpty)
            {
                letra = Color.Black;
            }

            // El texto y la barra juntos, centrados en la fila: en una de 26 px el
            // texto de 10 pt (19 px) va arriba del todo y la barra justo debajo. En
            // una fila mas baja el texto encoge su renglon para no quedar bajo la barra.
            int altoTexto = TextRenderer.MeasureText(g, "0", fuente, Size.Empty, TextFormatFlags.NoPadding).Height;
            int arriba = zona.Top + Math.Max(0, (zona.Height - altoTexto - AltoBarra) / 2);
            int yBarra = Math.Min(arriba + altoTexto, zona.Bottom - AltoBarra);
            if (yBarra - zona.Top < altoTexto / 2)
            {
                TextRenderer.DrawText(g, texto, fuente, zona, letra, formato);
                return;
            }

            TextRenderer.DrawText(g, texto, fuente, new Rectangle(zona.Left, arriba, zona.Width, yBarra - arriba), letra, formato);
            DibujarBarra(g, new Rectangle(zona.Left, yBarra, zona.Width, AltoBarra), fraccion.Value, estilo, elegida);
        }

        private static TextFormatFlags Horizontal(DataGridViewContentAlignment alineacion)
        {
            switch (alineacion)
            {
                case DataGridViewContentAlignment.TopCenter:
                case DataGridViewContentAlignment.MiddleCenter:
                case DataGridViewContentAlignment.BottomCenter:
                    return TextFormatFlags.HorizontalCenter;
                case DataGridViewContentAlignment.TopRight:
                case DataGridViewContentAlignment.MiddleRight:
                case DataGridViewContentAlignment.BottomRight:
                    return TextFormatFlags.Right;
                default:
                    return TextFormatFlags.Left;
            }
        }

        private static void DibujarBarra(Graphics g, Rectangle pista, double fraccion, EstiloBarra estilo, bool elegida)
        {
            double valor = Math.Max(0.0, Math.Min(1.0, fraccion));
            Color fondo;
            Color lleno;
            switch (estilo)
            {
                case EstiloBarra.Asistencia:
                    fondo = PistaAsistencia;
                    lleno = ResumenAsistencia.TieneDerecho(valor * 100.0) ? HojaAsistencia.BarraConDerecho : HojaAsistencia.BarraSinDerecho;
                    break;
                case EstiloBarra.FalloEn:
                    fondo = PistaClases;
                    lleno = RellenoFalloEn;
                    break;
                default:
                    fondo = PistaClases;
                    lleno = RellenoClases;
                    break;
            }

            // En la fila elegida (198,230,198) la pista verde clara casi no se ve: va en blanco.
            if (elegida && estilo != EstiloBarra.Asistencia)
            {
                fondo = Color.White;
            }

            SmoothingMode antes = g.SmoothingMode;
            PixelOffsetMode desplazamientoAntes = g.PixelOffsetMode;
            // Con Half la barra (suavizada) y la marca (sin suavizar) caen en las mismas 5 filas de pixeles.
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(pista, RadioBarra))
            using (SolidBrush brocha = new SolidBrush(fondo))
            {
                g.FillPath(brocha, forma);
            }

            int largo = (int)Math.Round(pista.Width * valor);
            if (valor > 0 && largo < 2)
            {
                // Algo mas que cero siempre se ve.
                largo = Math.Min(2, pista.Width);
            }

            if (largo > 0)
            {
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(new Rectangle(pista.X, pista.Y, largo, pista.Height), RadioBarra))
                using (SolidBrush brocha = new SolidBrush(lleno))
                {
                    g.FillPath(brocha, forma);
                }
            }

            g.SmoothingMode = SmoothingMode.None;
            if (estilo == EstiloBarra.Asistencia)
            {
                // La marca del 80 %, del alto de la barra.
                int x = pista.X + (int)Math.Round(pista.Width * ResumenAsistencia.Minimo / 100.0);
                using (SolidBrush marca = new SolidBrush(MarcaMinimo))
                {
                    g.FillRectangle(marca, x, pista.Y, 1, pista.Height);
                }
            }

            g.SmoothingMode = antes;
            g.PixelOffsetMode = desplazamientoAntes;
        }
    }
}
