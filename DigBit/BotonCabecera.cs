using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Una accion de la cabecera del panel del administrador (Ajustes, Codigos de
    /// las clases, Alertas): una pastilla blanca con borde verde, la forma de las
    /// pastillas del panel del profesor. La de Alertas lleva una campana y, con
    /// alertas nuevas, toma el estilo del bloque "Sin bitacoras" del profesor:
    /// fondo rojo claro, borde rojo y un globo rojo con cuantas son (sin
    /// parpadeo: el panel esta abierto todo el dia).
    ///
    /// Es un Button pintado a mano: Tab, Enter/Espacio y la accion por defecto de
    /// MSAA (la de Pulsar en las pruebas) son las de cualquier boton. Text no
    /// cambia nunca (es lo que buscan las pruebas); el numero va en AccessibleName.
    /// El control mide 3 px mas por lado que la pastilla, para el anillo del foco.
    /// </summary>
    internal class BotonCabecera : Button
    {
        public const int Alto = 32;
        private const int MargenFoco = 3;
        private const int Relleno = 12;
        private const int LadoCampana = 15;
        private const int TrasCampana = 5;
        private const int AltoGlobo = 18;
        private const int AntesDelGlobo = 6;
        private const int DespuesDelGlobo = 4;

        private static readonly Color Verde = Color.DarkGreen;
        private static readonly Color FondoEncima = Color.FromArgb(236, 246, 236);
        private static readonly Color FondoPulsado = Color.FromArgb(214, 232, 214);
        private static readonly Color Rojo = Color.FromArgb(198, 40, 40);
        private static readonly Color TextoRojo = Color.FromArgb(183, 28, 28);
        private static readonly Color FondoRojo = Color.FromArgb(253, 228, 224);
        private static readonly Color FondoRojoEncima = Color.FromArgb(250, 214, 209);
        private static readonly Color FondoRojoPulsado = Color.FromArgb(248, 206, 200);
        private static readonly Color VerdePrincipalEncima = Color.FromArgb(0, 96, 55);
        private static readonly Color VerdePrincipalPulsado = Color.FromArgb(0, 84, 48);

        private const TextFormatFlags FormatoTexto = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        private readonly DrawingFont fuenteGlobo = new DrawingFont("Century Gothic", 8.25F, FontStyle.Bold);
        // La suya, para liberarla al final (Control.Dispose no libera su Font).
        private readonly DrawingFont fuentePropia = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private readonly bool conCampana;
        private bool encima;
        private bool pulsado;
        private int nuevas;

        public BotonCabecera(string texto, bool conCampana = false)
        {
            this.conCampana = conCampana;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.White;
            Font = fuentePropia;
            Cursor = Cursors.Hand;
            TabStop = true;
            Text = texto;
            Ajustar();
        }

        /// <summary>Las alertas nuevas (solo Alertas): con alguna, rojo y el globo con el numero.</summary>
        public int Nuevas
        {
            get
            {
                return nuevas;
            }

            set
            {
                int valor = Math.Max(0, value);
                if (valor == nuevas)
                {
                    return;
                }

                nuevas = valor;
                AccessibleName = nuevas > 0 ? Text + ", " + nuevas + (nuevas == 1 ? " nueva" : " nuevas") : null;
                Ajustar();
                Invalidate();
            }
        }

        /// <summary>El ancho con "99+": lo que se le guarda en la cabecera para que nada se mueva al cambiar la cuenta.</summary>
        public int AnchoReservado { get; private set; }

        /// <summary>
        /// El color de detras, para las esquinas fuera de la pastilla, cuando no es
        /// el del contenedor (sobre la ficha de la clase del profesor, su verde claro).
        /// Vacio: el del contenedor.
        /// </summary>
        public Color FondoDetras { get; set; }

        /// <summary>
        /// La accion que importa ahora (en Mis grupos, "Dar asistencia..." mientras
        /// dura la clase): rellena del verde de la escuela con la letra blanca, como
        /// EstiloAdmin.BotonPrincipal. Sin ella, la pastilla blanca de siempre.
        /// </summary>
        public bool Principal
        {
            get
            {
                return principal;
            }

            set
            {
                if (principal != value)
                {
                    principal = value;
                    Invalidate();
                }
            }
        }

        private bool principal;

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Ajustar();
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Ajustar();
            Invalidate();
        }

        private void Ajustar()
        {
            AnchoReservado = AnchoPara(100);
            Size = new Size(AnchoPara(nuevas), Alto);
        }

        /// <summary>
        /// Lo que mide con ese numero de nuevas. Siempre con un Graphics: sin el,
        /// MeasureText no hace caso de NoPadding y da 7 px de mas.
        /// </summary>
        private int AnchoPara(int numero)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                int ancho = MargenFoco + Relleno + (conCampana ? LadoCampana + TrasCampana : 0) + MedirTexto(g).Width;
                ancho += numero > 0 ? AntesDelGlobo + AnchoGlobo(g, numero) + DespuesDelGlobo : Relleno;
                return ancho + MargenFoco;
            }
        }

        private Size MedirTexto(Graphics g)
        {
            return TextRenderer.MeasureText(g, Text, Font, Size.Empty, FormatoTexto);
        }

        private int AnchoGlobo(Graphics g, int numero)
        {
            return Math.Max(AltoGlobo, TextRenderer.MeasureText(g, TextoGlobo(numero), fuenteGlobo, Size.Empty, FormatoTexto).Width + 10);
        }

        private static string TextoGlobo(int numero)
        {
            return numero > 99 ? "99+" : numero.ToString();
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
                pulsado = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        // Se suelta antes de llamar a base: el Click puede abrir un dialogo modal
        // y la pastilla se quedaria pintada como pulsada mientras tanto.
        protected override void OnMouseUp(MouseEventArgs e)
        {
            pulsado = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                pulsado = true;
                Invalidate();
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                pulsado = false;
                Invalidate();
            }

            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            pulsado = false;
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
                fuenteGlobo.Dispose();
                fuentePropia.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // Fuera de la pastilla, el fondo de donde esta (Button no lo pinta solo).
            g.Clear(!FondoDetras.IsEmpty ? FondoDetras : Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool alerta = nuevas > 0;
            Color borde = alerta ? Rojo : Verde;
            Color texto = alerta ? TextoRojo : Verde;
            Color fondo = alerta
                ? (pulsado ? FondoRojoPulsado : encima ? FondoRojoEncima : FondoRojo)
                : (pulsado ? FondoPulsado : encima ? FondoEncima : Color.White);
            if (principal && !alerta)
            {
                // Los verdes de EstiloAdmin.BotonPrincipal (normal, con el raton encima y pulsado).
                fondo = pulsado ? VerdePrincipalPulsado : encima ? VerdePrincipalEncima : EstiloHorarios.VerdeTeschi;
                borde = fondo;
                texto = Color.White;
            }

            // La pastilla, con el borde de 2 px por dentro (como TarjetaOpcion).
            Rectangle vis = Rectangle.Inflate(ClientRectangle, -MargenFoco, -MargenFoco);
            Rectangle trazo = new Rectangle(vis.X + 1, vis.Y + 1, vis.Width - 3, vis.Height - 3);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(trazo, trazo.Height))
            using (SolidBrush relleno = new SolidBrush(fondo))
            using (Pen lapiz = new Pen(borde, 2f))
            {
                g.FillPath(relleno, forma);
                g.DrawPath(lapiz, forma);
            }

            int x = vis.X + Relleno;
            if (conCampana)
            {
                DibujarCampana(g, new RectangleF(x, vis.Y + (vis.Height - LadoCampana) / 2f, LadoCampana, LadoCampana), texto);
                x += LadoCampana + TrasCampana;
            }

            int anchoTexto = MedirTexto(g).Width;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, vis.Y, anchoTexto + 1, vis.Height), texto, FormatoTexto);

            if (alerta)
            {
                int anchoGlobo = AnchoGlobo(g, nuevas);
                Rectangle globo = new Rectangle(vis.Right - DespuesDelGlobo - anchoGlobo, vis.Y + 4, anchoGlobo, AltoGlobo);
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(globo, AltoGlobo))
                using (SolidBrush relleno = new SolidBrush(Rojo))
                {
                    g.FillPath(relleno, forma);
                }

                TextRenderer.DrawText(g, TextoGlobo(nuevas), fuenteGlobo, globo, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            // El foco del teclado (Tab), no despues de un clic: un anillo 1 px por fuera.
            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath anillo = PanelLaboratoriosAhora.Redondeado(new Rectangle(1, 1, Width - 3, Height - 3), Height))
                using (Pen lapiz = new Pen(borde, 2f))
                {
                    g.DrawPath(lapiz, anillo);
                }
            }
        }

        /// <summary>
        /// Una campana rellena (cuerpo, borde de abajo, badajo y asa) pintada con
        /// GDI+: no depende de ninguna fuente de iconos ni de emojis.
        /// </summary>
        private static void DibujarCampana(Graphics g, RectangleF r, Color color)
        {
            Func<float, float, PointF> en = (u, v) => new PointF(r.Left + u * r.Width, r.Top + v * r.Height);
            Func<float, float, float, float, RectangleF> caja = (u1, v1, u2, v2) => RectangleF.FromLTRB(en(u1, v1).X, en(u1, v1).Y, en(u2, v2).X, en(u2, v2).Y);
            using (GraphicsPath forma = new GraphicsPath(FillMode.Winding))
            using (SolidBrush brocha = new SolidBrush(color))
            {
                forma.StartFigure();
                forma.AddBezier(en(0.15f, 0.76f), en(0.24f, 0.67f), en(0.25f, 0.57f), en(0.25f, 0.45f));
                forma.AddBezier(en(0.25f, 0.45f), en(0.25f, 0.26f), en(0.36f, 0.15f), en(0.50f, 0.15f));
                forma.AddBezier(en(0.50f, 0.15f), en(0.64f, 0.15f), en(0.75f, 0.26f), en(0.75f, 0.45f));
                forma.AddBezier(en(0.75f, 0.45f), en(0.75f, 0.57f), en(0.76f, 0.67f), en(0.85f, 0.76f));
                forma.CloseFigure();

                // El borde de abajo, un poco mas ancho que el cuerpo, con las puntas redondas.
                RectangleF labio = caja(0.08f, 0.73f, 0.92f, 0.82f);
                forma.AddArc(labio.Left, labio.Top, labio.Height, labio.Height, 90, 180);
                forma.AddArc(labio.Right - labio.Height, labio.Top, labio.Height, labio.Height, 270, 180);
                forma.CloseFigure();

                forma.AddEllipse(caja(0.39f, 0.79f, 0.61f, 0.95f));
                forma.AddEllipse(caja(0.43f, 0.05f, 0.57f, 0.19f));
                g.FillPath(brocha, forma);
            }
        }
    }

    /// <summary>
    /// Una opcion de la tarjeta del codigo del profesor ("Quien registro", "Lista de asistencia"):
    /// un recuadro redondeado como las cifras de encima, pero con borde verde de
    /// 2 px para que se lea como boton; su titulo y, debajo, una linea de que hay
    /// (Descripcion). Compacta (cuando no sobra alto, como a 1366x768): solo el
    /// titulo. Es un Button pintado a mano (como BotonCabecera): se usa con el
    /// teclado y las pruebas la pulsan por su texto, que es el titulo y no cambia.
    /// </summary>
    internal class BotonOpcion : Button
    {
        public const int AltoNormal = 52;
        public const int AltoCompacto = 32;
        private static readonly Color Verde = Color.DarkGreen;
        private static readonly Color Apagado = Color.FromArgb(150, 150, 150);
        private static readonly Color FondoEncima = Color.FromArgb(236, 246, 236);
        private static readonly Color FondoPulsado = Color.FromArgb(214, 232, 214);

        private readonly DrawingFont fuenteDescripcion = new DrawingFont("Century Gothic", 8.5F);
        // La del titulo, para liberarla al final (Control.Dispose no libera su Font).
        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
        private string descripcion = "";
        private bool compacta;
        private bool encima;
        private bool pulsado;

        public BotonOpcion(string titulo)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.White;
            Font = fuenteTitulo;
            Cursor = Cursors.Hand;
            TabStop = true;
            Text = titulo;
        }

        /// <summary>La linea de debajo del titulo (tambien su descripcion accesible).</summary>
        public string Descripcion
        {
            get
            {
                return descripcion;
            }

            set
            {
                string valor = value ?? "";
                if (valor == descripcion)
                {
                    return;
                }

                descripcion = valor;
                AccessibleDescription = valor;
                Invalidate();
            }
        }

        /// <summary>Solo el titulo, centrado.</summary>
        public bool Compacta
        {
            get
            {
                return compacta;
            }

            set
            {
                if (value == compacta)
                {
                    return;
                }

                compacta = value;
                Invalidate();
            }
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
                pulsado = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        // Se suelta antes de llamar a base: el Click abre una ventana modal.
        protected override void OnMouseUp(MouseEventArgs e)
        {
            pulsado = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                pulsado = true;
                Invalidate();
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                pulsado = false;
                Invalidate();
            }

            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            pulsado = false;
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuenteDescripcion.Dispose();
                fuenteTitulo.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = Enabled ? Verde : Apagado;
            Color fondo = !Enabled ? Color.White : pulsado ? FondoPulsado : encima ? FondoEncima : Color.White;
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(r, 8))
            using (SolidBrush relleno = new SolidBrush(fondo))
            using (Pen borde = new Pen(color, Focused && ShowFocusCues ? 3f : 2f))
            {
                g.FillPath(relleno, forma);
                g.DrawPath(borde, forma);
            }

            const TextFormatFlags Linea = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            Rectangle dentro = Rectangle.Inflate(r, -12, -6);
            if (compacta || descripcion.Length == 0)
            {
                TextRenderer.DrawText(g, Text, Font, dentro, color, Linea | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int altoTitulo = TextRenderer.MeasureText(g, Text, Font, Size.Empty, Linea).Height;
            int altoLinea = TextRenderer.MeasureText(g, descripcion, fuenteDescripcion, Size.Empty, Linea).Height;
            int y = dentro.Top + Math.Max(0, (dentro.Height - altoTitulo - 3 - altoLinea) / 2);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(dentro.Left, y, dentro.Width, altoTitulo), color, Linea | TextFormatFlags.Left);
            TextRenderer.DrawText(g, descripcion, fuenteDescripcion, new Rectangle(dentro.Left, y + altoTitulo + 3, dentro.Width, altoLinea),
                Enabled ? Color.FromArgb(80, 80, 80) : Apagado, Linea | TextFormatFlags.Left);
        }
    }
}
