using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// Las tres filas del panel son horas fijas alrededor de la actual: a las
    /// 13:30, la anterior es 12:00-13:00, la actual 13:00-14:00 y la proxima
    /// 14:00-15:00, y se recorren solas al cambiar la hora. En cada hora se
    /// muestra lo que ocupa el laboratorio; una clase de dos horas sale en dos
    /// filas, como en la cuadricula de Horarios.
    /// </summary>
    internal static class HorasDelPanel
    {
        /// <summary>Inicio de la hora de una fila: -1 la anterior, 0 la actual, 1 la proxima.</summary>
        public static TimeSpan InicioDeFila(TimeSpan ahora, int desplazamiento)
        {
            return TimeSpan.FromHours(Math.Floor(ahora.TotalHours) + desplazamiento);
        }

        /// <summary>
        /// Lo que ocupa el laboratorio en [inicio, inicio + 1 h), o null si esta
        /// libre. Si se solapan varias: una clase real antes que una cancelada
        /// (que solo se ve si no hay otra, para que se sepa por que esta libre),
        /// luego la que esta en curso, luego la que mas tiempo ocupa de esa hora.
        /// </summary>
        public static SesionAgenda EnLaHora(IEnumerable<SesionAgenda> agenda, TimeSpan inicio, TimeSpan ahora)
        {
            TimeSpan fin = inicio + TimeSpan.FromHours(1);
            return agenda.Where(s => s.Inicio < fin && s.Fin > inicio)
                .OrderBy(s => s.Cancelada)
                .ThenByDescending(s => s.Inicio <= ahora && ahora < s.Fin)
                .ThenByDescending(s => Min(s.Fin, fin) - Max(s.Inicio, inicio))
                .ThenBy(s => s.Inicio)
                .FirstOrDefault();
        }

        private static TimeSpan Min(TimeSpan a, TimeSpan b)
        {
            return a < b ? a : b;
        }

        private static TimeSpan Max(TimeSpan a, TimeSpan b)
        {
            return a > b ? a : b;
        }
    }

    /// <summary>
    /// Cuadricula del panel del administrador: una columna por laboratorio y tres
    /// filas (la hora anterior, la actual y la siguiente; ver HorasDelPanel). Se
    /// dibuja a mano: son pocas celdas, cambian cada segundo (tiempo restante,
    /// barra de avance) y asi no parpadea. Cada clase lleva el color de su
    /// profesor, el mismo que en la cuadricula de Horarios. Clic en una clase:
    /// quien registro (ClaseElegida); en el nombre del laboratorio, en "Libre" o
    /// en una clase cancelada (ahi se quita la cancelacion), su horario
    /// (LaboratorioElegido).
    /// </summary>
    internal class PanelLaboratoriosAhora : Control
    {
        private const int AnchoEtiquetas = 112;
        // Con un nombre que no cabe en una linea ("Laboratorio de Computo 1", "2" y
        // "3" salian iguales, "Laboratorio de Com..."), la cabecera pasa a dos
        // (AltoDeCabecera) y la segunda se acorta por en medio: el numero se ve.
        private const int AltoCabecera = 30;
        private const int Hueco = 10;
        private const int AltoAviso = 30;
        private const int Relleno = 10;

        // Por debajo de este ancho una tarjeta ya no se lee (se probo con 8 y 10
        // laboratorios): en vez de aplastar las columnas, se desplazan de lado. Con
        // 165, siete laboratorios a 1366 de ancho pedian la barra por 1 px.
        private const int AnchoMinimoColumna = 160;
        private const int AnchoDifuminado = 28;

        // TextRenderer ignora el recorte del Graphics salvo que se le pida: sin
        // esto, al desplazar, los textos de las columnas pisan las etiquetas.
        private const TextFormatFlags Recortado = TextFormatFlags.PreserveGraphicsClipping;

        private static readonly Color Verde = Color.DarkGreen;
        private static readonly Color FondoAhora = Color.FromArgb(236, 246, 236);
        private static readonly Color Borde = Color.FromArgb(205, 214, 205);
        private static readonly Color TextoPrincipal = Color.FromArgb(30, 30, 30);
        private static readonly Color TextoSecundario = Color.FromArgb(75, 75, 75);
        private static readonly Color TextoApagado = Color.FromArgb(150, 150, 150);
        // Las alertas van sobre el color del profesor, a veces fuerte (rosa,
        // lila): con el naranja de siempre (190,85,0) el contraste bajaba a 2:1.
        // Este, casi marron, se queda en unos 4:1 sobre el peor de la paleta.
        private static readonly Color Naranja = Color.FromArgb(125, 45, 0);
        private static readonly Color FondoCancelada = Color.FromArgb(238, 238, 238);
        // El mismo naranja que la franja "sin conexion" del login.
        private static readonly Color FondoAviso = Color.FromArgb(255, 204, 128);

        private readonly DrawingFont fuenteMateria = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteNormal = new DrawingFont("Century Gothic", 9F);
        private readonly DrawingFont fuenteEstado = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private readonly DrawingFont fuenteCabecera = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteFila = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
        private readonly DrawingFont fuenteVacia = new DrawingFont("Century Gothic", 9.5F, FontStyle.Italic);

        private readonly HScrollBar barra;
        private readonly ToolTip ayuda = new ToolTip();

        // Las clases en curso "respiran" (ver Resplandor): el temporizador solo
        // vuelve a pintar esas tarjetas, y solo si hay alguna.
        private readonly Timer animacion = new Timer { Interval = Resplandor.IntervaloMs };
        private readonly List<Rectangle> celdasEnCurso = new List<Rectangle>();

        // Las celdas con clase del ultimo pintado: la ayuda del raton da el
        // detalle de los equipos sin internet, que en la tarjeta se corta.
        private readonly List<KeyValuePair<Rectangle, SesionAgenda>> celdasConClase = new List<KeyValuePair<Rectangle, SesionAgenda>>();

        private PanoramaLaboratorios panorama;
        private DateTime ahora;
        private string aviso;
        private int desplazamiento;

        // Donde quedaron las columnas en el ultimo pintado, para saber que
        // laboratorio hay bajo el raton.
        private Rectangle zonaPintada;
        private int anchoPintado;
        private string textoAyuda;
        // El alto de la cabecera del ultimo pintado: una o dos lineas (AltoDeCabecera).
        // Se vuelve a medir solo si cambian los laboratorios o el ancho: el halo de
        // las clases en curso repinta 20 veces por segundo.
        private int altoCabecera = AltoCabecera;
        private PanoramaLaboratorios panoramaMedido;
        private int anchoMedido = -1;
        private int altoLineaCabecera;

        /// <summary>
        /// Clic en la columna de un laboratorio fuera de sus clases (su nombre, una
        /// hora "Libre", una clase cancelada), con su id: el panel principal abre su
        /// horario. Sin nadie en ClaseElegida, tambien el clic en una clase.
        /// </summary>
        public event Action<int> LaboratorioElegido;

        /// <summary>
        /// Clic en una clase: el panel principal abre quien registro y que
        /// reportaron (BitacoraSesion), como desde Alertas. No escribe nada: vale
        /// igual para el encargado de laboratorio.
        /// </summary>
        public event Action<SesionAgenda> ClaseElegida;

        /// <summary>
        /// Clic derecho en una clase de un laboratorio con generacion de codigos:
        /// el administrador la abre (o ve su codigo) para darselo al grupo
        /// si el profesor no puede. Sin nadie que lo escuche (el encargado de
        /// laboratorio, en solo consulta) no hay menu ni se anuncia en la ayuda.
        /// </summary>
        public event Action<SesionAgenda> AbrirClasePedida;

        public PanelLaboratoriosAhora()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;

            barra = new HScrollBar { Dock = DockStyle.Bottom, Visible = false };
            barra.ValueChanged += (s, e) =>
            {
                desplazamiento = barra.Value;
                Invalidate();
            };
            Controls.Add(barra);

            animacion.Tick += (s, e) =>
            {
                foreach (Rectangle celda in celdasEnCurso)
                {
                    Invalidate(Rectangle.Inflate(celda, Resplandor.Margen + 2, Resplandor.Margen + 2));
                }
            };
            animacion.Start();
        }

        /// <summary>Hora (del servidor) con la que se decide que es anterior, ahora y proxima.</summary>
        public DateTime Ahora
        {
            get { return ahora; }
            set
            {
                if (value == ahora)
                {
                    return;
                }

                ahora = value;
                Invalidate();
            }
        }

        public PanoramaLaboratorios Panorama
        {
            get { return panorama; }
        }

        public void Mostrar(PanoramaLaboratorios datos)
        {
            panorama = datos;
            aviso = null;
            AjustarDesplazamiento();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AjustarDesplazamiento();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            OpcionCombo laboratorio = LaboratorioEn(e.Location);
            SesionAgenda sesion = laboratorio == null ? null : SesionEn(e.Location);
            string texto = laboratorio == null ? null
                : AbreSuBitacora(sesion) ? "Clic: quién registró en esta clase y qué reportaron."
                : "Clic: ver el horario de " + laboratorio.Texto + ".";
            if (sesion != null && sesion.SinInternet.Count > 0)
            {
                texto = Consultas.DescribirSinInternet(sesion.SinInternet, false) + "." + Environment.NewLine + texto;
            }

            // Las que dio su docente a quien no pudo registrar (db/22); en la tarjeta, solo si nadie registro.
            if (sesion != null && sesion.AsistenciasDadas > 0)
            {
                texto = "Su docente dio " + Asistencias(sesion.AsistenciasDadas) + " sin bitácora (a quien estaba en el salón y no pudo registrar)."
                    + Environment.NewLine + texto;
            }

            if (sesion != null && !sesion.Utilizable)
            {
                texto = "No se puede usar: la clase está desactivada o fuera de su periodo, o la cuenta de su docente está de baja."
                    + Environment.NewLine + texto;
            }

            if (sesion != null && AbrirClasePedida != null && SePuedeAbrir(sesion))
            {
                texto += Environment.NewLine + (sesion.SesionId.HasValue ? "Clic derecho: ver el código de esta clase." : "Clic derecho: abrir la clase y ver su código.");
            }

            if (texto == textoAyuda)
            {
                return;
            }

            textoAyuda = texto;
            Cursor = laboratorio == null ? Cursors.Default : Cursors.Hand;
            ayuda.SetToolTip(this, texto);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            textoAyuda = null;
            Cursor = Cursors.Default;
        }

        /// <summary>La clase de la celda que hay en ese punto (del ultimo pintado), o null.</summary>
        private SesionAgenda SesionEn(Point punto)
        {
            foreach (KeyValuePair<Rectangle, SesionAgenda> celda in celdasConClase)
            {
                if (celda.Key.Contains(punto))
                {
                    return celda.Value;
                }
            }

            return null;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            OpcionCombo laboratorio = LaboratorioEn(e.Location);
            SesionAgenda sesion = laboratorio == null ? null : SesionEn(e.Location);
            if (e.Button == MouseButtons.Left && AbreSuBitacora(sesion))
            {
                // La clase: quien registro (antes abria el horario, como el resto de la columna).
                ClaseElegida(sesion);
            }
            else if (e.Button == MouseButtons.Left && laboratorio != null && LaboratorioElegido != null)
            {
                LaboratorioElegido(laboratorio.Id);
            }

            if (e.Button == MouseButtons.Right && sesion != null && SePuedeAbrir(sesion) && AbrirClasePedida != null)
            {
                ContextMenuStrip menu = new ContextMenuStrip { Font = new Font("Century Gothic", 9.5F) };
                menu.Items.Add(sesion.SesionId.HasValue ? "Ver el código de esta clase" : "Abrir la clase y ver su código", null,
                    (s, a) => AbrirClasePedida(sesion));
                menu.Closed += (s, a) => BeginInvoke(new Action(menu.Dispose));
                menu.Show(this, e.Location);
            }
        }

        /// <summary>
        /// Si el clic en esa celda abre quien registro: una clase sin cancelar y
        /// alguien que lo escuche. Cancelada, nadie registra: su horario.
        /// </summary>
        private bool AbreSuBitacora(SesionAgenda sesion)
        {
            return sesion != null && !sesion.Cancelada && ClaseElegida != null;
        }

        /// <summary>
        /// Una clase de un laboratorio con generacion de codigos, sin cancelar,
        /// desde 15 minutos antes hasta su fin, que el servidor deja abrir (no una
        /// extra de una clase desactivada o fuera de vigencia).
        /// </summary>
        private bool SePuedeAbrir(SesionAgenda sesion)
        {
            return panorama != null && sesion.Utilizable && panorama.ModoDe(sesion.LaboratorioId) == ModoCodigo.Renovable
                && ReglasCodigo.Abierta(sesion.Inicio, sesion.Fin, sesion.Cancelada, ahora.TimeOfDay);
        }

        /// <summary>El laboratorio de la columna que hay en ese punto, o null (etiquetas, huecos, fuera).</summary>
        private OpcionCombo LaboratorioEn(Point punto)
        {
            if (panorama == null || anchoPintado <= 0 || !zonaPintada.Contains(punto))
            {
                return null;
            }

            int x = punto.X - zonaPintada.Left + desplazamiento;
            int columna = x / (anchoPintado + Hueco);
            bool enElHueco = x % (anchoPintado + Hueco) >= anchoPintado;
            return enElHueco || columna < 0 || columna >= panorama.Laboratorios.Count ? null : panorama.Laboratorios[columna];
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!barra.Visible)
            {
                return;
            }

            int maximo = barra.Maximum - barra.LargeChange + 1;
            barra.Value = Math.Max(0, Math.Min(maximo, barra.Value - Math.Sign(e.Delta) * barra.SmallChange));
        }

        /// <summary>Muestra la barra si las columnas no caben con su ancho minimo, y ajusta su recorrido.</summary>
        private void AjustarDesplazamiento()
        {
            int columnas = panorama == null ? 0 : panorama.Laboratorios.Count;
            int disponible = ClientSize.Width - AnchoEtiquetas;
            int necesario = columnas * AnchoMinimoColumna + Math.Max(0, columnas - 1) * Hueco;
            bool haceFalta = columnas > 0 && disponible > 0 && necesario > disponible;

            if (!haceFalta)
            {
                barra.Visible = false;
                desplazamiento = 0;
                return;
            }

            // Primero el valor y luego el maximo: al reves, bajar el maximo por
            // debajo del valor actual lanza una excepcion.
            int maximo = necesario - disponible;
            if (barra.Value > maximo)
            {
                barra.Value = maximo;
            }

            barra.Minimum = 0;
            barra.LargeChange = disponible;
            barra.SmallChange = AnchoMinimoColumna + Hueco;
            barra.Maximum = necesario - 1;
            barra.Visible = true;
            desplazamiento = barra.Value;
        }

        /// <summary>Franja de aviso arriba (sin conexion); null la quita. Lo ultimo que se cargo se sigue viendo.</summary>
        public void Avisar(string texto)
        {
            aviso = texto;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuenteMateria.Dispose();
                fuenteNormal.Dispose();
                fuenteEstado.Dispose();
                fuenteCabecera.Dispose();
                fuenteFila.Dispose();
                fuenteVacia.Dispose();
                ayuda.Dispose();
                animacion.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            celdasEnCurso.Clear();
            celdasConClase.Clear();
            Rectangle area = ClientRectangle;
            if (barra.Visible)
            {
                area.Height -= barra.Height + 4;
            }

            if (!string.IsNullOrEmpty(aviso))
            {
                Rectangle franja = new Rectangle(area.Left, area.Top, area.Width, AltoAviso);
                using (SolidBrush brocha = new SolidBrush(FondoAviso))
                {
                    g.FillRectangle(brocha, franja);
                }

                TextRenderer.DrawText(g, aviso, fuenteEstado, Rectangle.Inflate(franja, -Relleno, 0), Color.Black,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                area = new Rectangle(area.Left, area.Top + AltoAviso + Hueco, area.Width, area.Height - AltoAviso - Hueco);
            }

            if (panorama == null)
            {
                Centrado(g, area, "Cargando los laboratorios...");
                return;
            }

            if (panorama.Laboratorios.Count == 0)
            {
                Centrado(g, area, "No hay laboratorios dados de alta.");
                return;
            }

            if (ahora.Date != panorama.Fecha)
            {
                // Paso la medianoche y la agenda es la de ayer; el formulario ya pidio la de hoy.
                Centrado(g, area, "Cargando la agenda de hoy...");
                return;
            }

            int columnas = panorama.Laboratorios.Count;
            int anchoColumna = barra.Visible
                ? AnchoMinimoColumna
                : (area.Width - AnchoEtiquetas - Hueco * (columnas - 1)) / columnas;
            altoCabecera = AltoDeCabecera(g, anchoColumna - 4);
            int altoFila = (area.Height - altoCabecera - Hueco * 3) / 3;
            TimeSpan hora = ahora.TimeOfDay;

            // La fila "Ahora" se resalta de lado a lado.
            Rectangle banda = new Rectangle(area.Left, FilaY(area, 1, altoFila) - Hueco / 2, area.Width, altoFila + Hueco);
            using (SolidBrush brocha = new SolidBrush(FondoAhora))
            using (GraphicsPath forma = Redondeado(banda, 10))
            {
                g.FillPath(brocha, forma);
            }

            string[] nombresFila = { "Anterior", "Ahora", "Próxima" };
            TimeSpan[] horas = new TimeSpan[3];
            int altoNombre = TextRenderer.MeasureText(g, "Ág", fuenteFila).Height;
            int altoRango = TextRenderer.MeasureText(g, "Ág", fuenteNormal).Height;
            for (int fila = 0; fila < 3; fila++)
            {
                horas[fila] = HorasDelPanel.InicioDeFila(hora, fila - 1);
                Color color = fila == 1 ? Verde : TextoSecundario;
                int y = FilaY(area, fila, altoFila) + (altoFila - altoNombre - altoRango) / 2;
                int x = area.Left + Relleno;
                TextRenderer.DrawText(g, nombresFila[fila], fuenteFila, new Rectangle(x, y, AnchoEtiquetas - Relleno, altoNombre), color,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, HoraEnPunto(horas[fila]) + " – " + HoraEnPunto(horas[fila] + TimeSpan.FromHours(1)), fuenteNormal,
                    new Rectangle(x, y + altoNombre, AnchoEtiquetas - Relleno, altoRango), color,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);
            }

            // Las columnas se desplazan por debajo de las etiquetas, que se quedan fijas.
            Rectangle zonaColumnas = new Rectangle(area.Left + AnchoEtiquetas, area.Top, area.Width - AnchoEtiquetas, area.Height);
            zonaPintada = zonaColumnas;
            anchoPintado = anchoColumna;
            g.SetClip(zonaColumnas);
            for (int columna = 0; columna < columnas; columna++)
            {
                OpcionCombo laboratorio = panorama.Laboratorios[columna];
                int x = zonaColumnas.Left + columna * (anchoColumna + Hueco) - desplazamiento;
                if (x + anchoColumna < zonaColumnas.Left || x > zonaColumnas.Right)
                {
                    continue;
                }

                Rectangle cabecera = new Rectangle(x, area.Top, anchoColumna, altoCabecera);
                DibujarCabecera(g, laboratorio.Texto, new Rectangle(x + 2, area.Top, anchoColumna - 4, altoCabecera - 6));
                using (Pen linea = new Pen(Verde, 2f))
                {
                    g.DrawLine(linea, cabecera.Left, cabecera.Bottom - 2, cabecera.Right, cabecera.Bottom - 2);
                }

                List<SesionAgenda> agenda = panorama.Agenda.Where(s => s.LaboratorioId == laboratorio.Id).ToList();
                for (int fila = 0; fila < 3; fila++)
                {
                    SesionAgenda enEsaHora = HorasDelPanel.EnLaHora(agenda, horas[fila], hora);
                    Rectangle celda = new Rectangle(x, FilaY(area, fila, altoFila), anchoColumna, altoFila);
                    DibujarCelda(g, celda, enEsaHora, fila, hora);
                    if (enEsaHora != null)
                    {
                        // Solo lo que se ve: una columna desplazada bajo las etiquetas no cuenta.
                        celdasConClase.Add(new KeyValuePair<Rectangle, SesionAgenda>(Rectangle.Intersect(celda, zonaColumnas), enEsaHora));
                    }
                }
            }

            // Un difuminado en el borde avisa de que hay mas laboratorios de ese lado.
            if (barra.Visible)
            {
                int maximo = barra.Maximum - barra.LargeChange + 1;
                if (desplazamiento > 0)
                {
                    Difuminar(g, zonaColumnas, banda, false);
                }

                if (desplazamiento < maximo)
                {
                    Difuminar(g, zonaColumnas, banda, true);
                }
            }

            g.ResetClip();
        }

        /// <summary>Degradado hacia el fondo en un borde de la zona de columnas, respetando la banda de "Ahora".</summary>
        private static void Difuminar(Graphics g, Rectangle zona, Rectangle banda, bool derecha)
        {
            int x = derecha ? zona.Right - AnchoDifuminado : zona.Left;
            Rectangle[] tramos =
            {
                new Rectangle(x, zona.Top, AnchoDifuminado, banda.Top - zona.Top),
                new Rectangle(x, banda.Top, AnchoDifuminado, banda.Height),
                new Rectangle(x, banda.Bottom, AnchoDifuminado, zona.Bottom - banda.Bottom)
            };
            Color[] fondos = { Color.White, FondoAhora, Color.White };
            for (int i = 0; i < tramos.Length; i++)
            {
                if (tramos[i].Height <= 0)
                {
                    continue;
                }

                Color transparente = Color.FromArgb(0, fondos[i]);
                using (LinearGradientBrush brocha = new LinearGradientBrush(
                    new Rectangle(tramos[i].X - 1, tramos[i].Y, tramos[i].Width + 2, tramos[i].Height),
                    derecha ? transparente : fondos[i], derecha ? fondos[i] : transparente, LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(brocha, tramos[i]);
                }
            }
        }

        private int FilaY(Rectangle area, int fila, int altoFila)
        {
            return area.Top + altoCabecera + Hueco + fila * (altoFila + Hueco);
        }

        private const TextFormatFlags LineaDeCabecera = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPrefix | Recortado;

        /// <summary>
        /// AltoCabecera si todos los nombres caben en una linea de ese ancho; si
        /// alguno no, el de dos lineas para todas las columnas (asi las clases de
        /// todos los laboratorios empiezan a la misma altura).
        /// </summary>
        private int AltoDeCabecera(Graphics g, int ancho)
        {
            if (panorama == panoramaMedido && ancho == anchoMedido)
            {
                return altoCabecera;
            }

            panoramaMedido = panorama;
            anchoMedido = ancho;
            bool caben = panorama.Laboratorios.All(l => TextRenderer.MeasureText(g, l.Texto, fuenteCabecera, Size.Empty, LineaDeCabecera).Width <= ancho);
            return caben ? AltoCabecera : 2 * AltoLineaCabecera(g) + 10;
        }

        private int AltoLineaCabecera(Graphics g)
        {
            if (altoLineaCabecera == 0)
            {
                altoLineaCabecera = TextRenderer.MeasureText(g, "Ág", fuenteCabecera).Height;
            }

            return altoLineaCabecera;
        }

        /// <summary>
        /// El nombre de un laboratorio en su cabecera. Si cabe, en una linea, junto
        /// a la raya. Si no, en dos: en la primera las palabras que quepan y en la
        /// segunda el resto, acortado por en medio si tampoco cabe ("Laborat...puto
        /// 101"): el final, que suele ser el numero, es lo que los distingue.
        /// </summary>
        private void DibujarCabecera(Graphics g, string texto, Rectangle caja)
        {
            int altoLinea = AltoLineaCabecera(g);
            Rectangle abajo = new Rectangle(caja.X, caja.Bottom - altoLinea, caja.Width, altoLinea);
            if (caja.Height < 2 * altoLinea)
            {
                // Cabecera de una linea: todos caben.
                TextRenderer.DrawText(g, texto, fuenteCabecera, caja, Verde,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Recortado);
                return;
            }

            string[] palabras = (texto ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (palabras.Length < 2 || TextRenderer.MeasureText(g, texto, fuenteCabecera, Size.Empty, LineaDeCabecera).Width <= caja.Width)
            {
                TextRenderer.DrawText(g, texto, fuenteCabecera, abajo, Verde, LineaDeCabecera | TextFormatFlags.PathEllipsis);
                return;
            }

            int enLaPrimera = 1;
            while (enLaPrimera < palabras.Length - 1
                && TextRenderer.MeasureText(g, string.Join(" ", palabras, 0, enLaPrimera + 1), fuenteCabecera, Size.Empty, LineaDeCabecera).Width <= caja.Width)
            {
                enLaPrimera++;
            }

            Rectangle arriba = new Rectangle(caja.X, abajo.Top - altoLinea, caja.Width, altoLinea);
            TextRenderer.DrawText(g, string.Join(" ", palabras, 0, enLaPrimera), fuenteCabecera, arriba, Verde, LineaDeCabecera | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, string.Join(" ", palabras, enLaPrimera, palabras.Length - enLaPrimera), fuenteCabecera, abajo, Verde,
                LineaDeCabecera | TextFormatFlags.PathEllipsis);
        }

        /// <summary>"13:00". La fila de la hora siguiente a las 23:00 es "00:00".</summary>
        private static string HoraEnPunto(TimeSpan inicio)
        {
            int horaDelDia = (((int)Math.Floor(inicio.TotalHours)) % 24 + 24) % 24;
            return horaDelDia.ToString("00") + ":00";
        }

        private void DibujarCelda(Graphics g, Rectangle celda, SesionAgenda sesion, int fila, TimeSpan hora)
        {
            Rectangle contorno = new Rectangle(celda.X, celda.Y, celda.Width - 1, celda.Height - 1);

            if (sesion == null)
            {
                const string texto = "Libre";
                using (GraphicsPath forma = Redondeado(contorno, 8))
                using (Pen borde = new Pen(Borde) { DashStyle = DashStyle.Dash })
                {
                    g.DrawPath(borde, forma);
                }

                TextRenderer.DrawText(g, texto, fuenteVacia, celda, TextoApagado,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | Recortado);
                return;
            }

            // Una clase que ya termino, mas tenue; la que esta en curso, con borde
            // verde y barra de avance en la fila de ahora.
            bool termino = sesion.Fin <= hora;
            bool enCurso = !sesion.Cancelada && sesion.Inicio <= hora && hora < sesion.Fin;
            Color fondo = sesion.Cancelada ? FondoCancelada : CuadriculaSemanal.ColorDeProfesor(sesion.ProfesorId);
            if (termino)
            {
                fondo = Mezclar(fondo, Color.White, 0.5f);
            }

            enCurso = enCurso && fila == 1;
            if (enCurso)
            {
                Resplandor.Dibujar(g, contorno, 8);
                celdasEnCurso.Add(contorno);
            }

            using (GraphicsPath forma = Redondeado(contorno, 8))
            {
                using (SolidBrush brocha = new SolidBrush(fondo))
                {
                    g.FillPath(brocha, forma);
                }

                using (Pen borde = new Pen(enCurso ? Resplandor.Borde() : Borde, enCurso ? 2.5f : 1f))
                {
                    g.DrawPath(borde, forma);
                }
            }

            int y = celda.Top + 8;
            int ancho = celda.Width - 2 * Relleno;
            Linea(g, sesion.Materia, fuenteMateria, sesion.Cancelada ? TextoApagado : TextoPrincipal, celda, ancho, ref y);
            Linea(g, sesion.Grupo + " – " + sesion.Profesor, fuenteNormal, sesion.Cancelada ? TextoApagado : TextoSecundario, celda, ancho, ref y);
            Linea(g, HorariosDatos.Hora(sesion.Inicio) + " – " + HorariosDatos.Hora(sesion.Fin) + (sesion.EsExtra ? " (sesión extra)" : ""),
                fuenteNormal, sesion.Cancelada ? TextoApagado : TextoSecundario, celda, ancho, ref y);
            y += 3;

            Func<string, bool> cabe = linea => TextRenderer.MeasureText(g, linea, fuenteEstado).Width <= ancho;
            int renglones = Math.Max(0, (celda.Bottom - 14 - y) / TextRenderer.MeasureText(g, "Ág", fuenteEstado).Height);
            // Si los docentes no ven los codigos (CodigosSoloResponsables), el de la
            // clase para darselo al grupo; si no, como siempre.
            string codigo = panorama.CodigosSoloResponsables && !string.IsNullOrEmpty(sesion.CodigoParaDar)
                ? TarjetaCodigo.Mayusculas(sesion.CodigoParaDar) : null;
            foreach (Tuple<string, Color> estado in Estado(sesion, fila, hora, panorama.ModoDe(sesion.LaboratorioId), cabe, renglones, codigo))
            {
                Linea(g, estado.Item1, fuenteEstado, estado.Item2, celda, ancho, ref y);
            }

            if (enCurso)
            {
                double total = (sesion.Fin - sesion.Inicio).TotalSeconds;
                double avance = total <= 0 ? 1 : Math.Max(0, Math.Min(1, (hora - sesion.Inicio).TotalSeconds / total));
                Rectangle barra = new Rectangle(celda.Left + Relleno, celda.Bottom - 12, ancho, 5);
                using (SolidBrush vacio = new SolidBrush(Color.FromArgb(150, Color.White)))
                using (SolidBrush lleno = new SolidBrush(Verde))
                {
                    g.FillRectangle(vacio, barra);
                    g.FillRectangle(lleno, new Rectangle(barra.X, barra.Y, (int)(barra.Width * avance), barra.Height));
                }
            }
        }

        /// <summary>
        /// Lineas de estado de una clase. Dependen de si ya termino, esta en curso
        /// o no ha empezado; una clase en curso de dos horas sale en dos filas y
        /// solo la de ahora lleva el detalle, para no repetirlo. cabe: si un texto
        /// cabe en una linea de la tarjeta; renglones: cuantas lineas de estado
        /// caben en ella. codigo: si los docentes no ven los codigos
        /// (CodigosSoloResponsables), el de la clase, que va ("Codigo: 48213") en la
        /// que aun no empieza y en la que esta en curso, antes que el tiempo
        /// (CodigoYTiempo); null, como siempre.
        /// </summary>
        private static IEnumerable<Tuple<string, Color>> Estado(SesionAgenda sesion, int fila, TimeSpan hora, ModoCodigo modo, Func<string, bool> cabe, int renglones,
            string codigo)
        {
            if (sesion.Cancelada)
            {
                yield return Tuple.Create("Cancelada" + (string.IsNullOrEmpty(sesion.Motivo) ? "" : ": " + sesion.Motivo), TextoSecundario);
                yield break;
            }

            if (!sesion.Utilizable && hora < sesion.Fin)
            {
                // Una extra de una clase desactivada o fuera de periodo, o la clase de
                // un profesor dado de baja: en ningun modo puede registrar nadie. La
                // causa, en la ayuda del raton (son tres y no caben aqui).
                yield return Tuple.Create("No se puede usar hoy", Naranja);
                yield break;
            }

            if (sesion.Inicio > hora)
            {
                TimeSpan falta = sesion.Inicio - hora;
                if (codigo != null)
                {
                    // El codigo primero: es lo que el docente le pide a la responsable.
                    foreach (Tuple<string, Color> linea in CodigoYTiempo(codigo, "Empieza en " + Duracion(falta), cabe, renglones))
                    {
                        yield return linea;
                    }

                    yield break;
                }

                yield return Tuple.Create("Empieza en " + Duracion(falta), Verde);
                if (falta <= TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos))
                {
                    yield return modo == ModoCodigo.Renovable ? CodigoDeLaClase(sesion)
                        : Tuple.Create(modo == ModoCodigo.SinCodigo ? "Ya pueden registrar" : "Ya se acepta el código", TextoSecundario);
                }

                yield break;
            }

            bool enCurso = hora < sesion.Fin;
            if (enCurso && fila == 0)
            {
                yield return Tuple.Create("Sigue en curso", Verde);
                yield break;
            }

            if (enCurso && fila == 2)
            {
                yield return Tuple.Create("Continúa hasta las " + HorariosDatos.Hora(sesion.Fin), Verde);
                yield break;
            }

            int usadas = 0;
            if (enCurso)
            {
                if (codigo != null)
                {
                    foreach (Tuple<string, Color> linea in CodigoYTiempo(codigo, "Quedan " + Duracion(sesion.Fin - hora), cabe, renglones))
                    {
                        usadas++;
                        yield return linea;
                    }
                }
                else
                {
                    yield return Tuple.Create("Quedan " + Duracion(sesion.Fin - hora), Verde);
                    usadas = 1;
                    if (modo == ModoCodigo.Renovable)
                    {
                        yield return CodigoDeLaClase(sesion);
                        usadas = 2;
                    }
                }
            }

            // Con lo que se registro en la clase de antes de su terna ("Ahora mismo"), como en Faltas.
            int bitacoras = sesion.BitacorasDeLaClase;
            if (bitacoras == 0 && sesion.AsistenciasDadas > 0)
            {
                // Nadie registro, pero su docente dio la asistencia a quien no pudo
                // (db/22; quiza sin internet): la clase se dio, no es de revisar. En
                // una linea si cabe; si no, en dos si hay sitio; si tampoco, una corta
                // (la ayuda del raton lo dice entero). Antes van "Quedan..." y, con la
                // generacion de codigos, el codigo.
                string nadie = enCurso ? "Aún sin bitácoras" : "Nadie registró bitácora";
                int n = sesion.AsistenciasDadas;
                string dio = "su docente dio " + Asistencias(n);
                string dadas = n == 1 ? " dada" : " dadas";
                int libres = renglones - usadas;
                if (cabe(nadie + "; " + dio))
                {
                    yield return Tuple.Create(nadie + "; " + dio, TextoSecundario);
                }
                else if (libres >= 2)
                {
                    yield return Tuple.Create(nadie, TextoSecundario);
                    yield return Tuple.Create(cabe("Su docente dio " + Asistencias(n)) ? "Su docente dio " + Asistencias(n) : Asistencias(n) + dadas, TextoSecundario);
                }
                else
                {
                    yield return Tuple.Create(cabe("Sin bitácoras; " + Asistencias(n) + dadas) ? "Sin bitácoras; " + Asistencias(n) + dadas
                        : "Sin bitácoras; " + n + dadas, TextoSecundario);
                }

                yield break;
            }

            if (bitacoras == 0)
            {
                // En curso puede ser que aun no lleguen; si ya termino, es de revisar.
                yield return Tuple.Create(enCurso ? "Aún sin bitácoras" : "Nadie registró bitácora", enCurso ? TextoSecundario : Naranja);
                yield break;
            }

            string registro = bitacoras + (bitacoras == 1 ? " bitácora" : " bitácoras");
            if (sesion.ConFallas == 0)
            {
                yield return Tuple.Create(registro + ", sin fallas", Verde);
            }
            else
            {
                yield return Tuple.Create(registro + ", " + sesion.ConFallas + (sesion.ConFallas == 1 ? " con falla" : " con fallas"), Naranja);
            }

            // Lo que avisaron de las maquinas de alrededor (db/10); el detalle, en la ayuda del raton.
            if (sesion.SinInternet.Count > 0)
            {
                yield return Tuple.Create(Consultas.DescribirSinInternet(sesion.SinInternet, true), Naranja);
            }
        }

        /// <summary>
        /// Generacion de codigos: el de la sesion, o que el profesor aun no la abre
        /// (el administrador la abre con el clic derecho). El codigo generado solo
        /// lo ve el administrador: al encargado de laboratorio, "Clase abierta".
        /// </summary>
        private static Tuple<string, Color> CodigoDeLaClase(SesionAgenda sesion)
        {
            return sesion.SesionId.HasValue && !string.IsNullOrEmpty(sesion.CodigoSesion)
                ? Tuple.Create(Permisos.VeCodigosGenerados ? "Código " + sesion.CodigoSesion : "Clase abierta", TextoSecundario)
                // Nadie la puede abrir (ver Estado).
                : !sesion.Utilizable ? Tuple.Create("No se puede usar hoy", Naranja)
                // Corto: en una tarjeta estrecha "Sin codigo: el profesor no la ha..." se cortaba.
                : Tuple.Create("Su docente aún no la abre", Naranja);
        }

        /// <summary>"Código: 48213"; en una tarjeta en la que asi no cabe, solo el codigo (acortado, no se leeria).</summary>
        private static string ConElCodigo(string codigo, Func<string, bool> cabe)
        {
            return cabe("Código: " + codigo) ? "Código: " + codigo : codigo;
        }

        /// <summary>
        /// Con CodigosSoloResponsables, el codigo y cuanto falta ("Empieza en..." o
        /// "Quedan..."): primero el codigo, que es lo que el docente le pide a la
        /// responsable. Si solo cabe un renglon (a 1366 la tarjeta es baja), los dos
        /// en uno ("Código AB12C · quedan 35 min") si cabe; si no, solo el codigo:
        /// en curso la barra ya dice cuanto falta.
        /// </summary>
        private static IEnumerable<Tuple<string, Color>> CodigoYTiempo(string codigo, string tiempo, Func<string, bool> cabe, int renglones)
        {
            if (renglones >= 2)
            {
                yield return Tuple.Create(ConElCodigo(codigo, cabe), TextoPrincipal);
                yield return Tuple.Create(tiempo, Verde);
                yield break;
            }

            string juntos = "Código " + codigo + " · " + char.ToLowerInvariant(tiempo[0]) + tiempo.Substring(1);
            yield return Tuple.Create(cabe(juntos) ? juntos : ConElCodigo(codigo, cabe), TextoPrincipal);
        }

        /// <summary>"1 asistencia", "3 asistencias".</summary>
        private static string Asistencias(int cuantas)
        {
            return cuantas == 1 ? "1 asistencia" : cuantas + " asistencias";
        }

        /// <summary>"25 min", "2 h", "1 h 10 min"; los minutos se redondean hacia arriba.</summary>
        internal static string Duracion(TimeSpan tiempo)
        {
            int minutos = Math.Max(1, (int)Math.Ceiling(tiempo.TotalMinutes));
            if (minutos < 60)
            {
                return minutos + " min";
            }

            int horas = minutos / 60;
            int resto = minutos % 60;
            return horas + " h" + (resto == 0 ? "" : " " + resto + " min");
        }

        private static void Linea(Graphics g, string texto, DrawingFont fuente, Color color, Rectangle celda, int ancho, ref int y)
        {
            int alto = TextRenderer.MeasureText(g, "Ág", fuente).Height;
            if (y + alto > celda.Bottom - 14)
            {
                return;
            }

            TextRenderer.DrawText(g, texto, fuente, new Rectangle(celda.Left + Relleno, y, ancho, alto), color,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Recortado);
            y += alto;
        }

        private void Centrado(Graphics g, Rectangle area, string texto)
        {
            TextRenderer.DrawText(g, texto, fuenteVacia, area, TextoApagado,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        private static Color Mezclar(Color color, Color con, float proporcion)
        {
            return Color.FromArgb(
                (int)(color.R + (con.R - color.R) * proporcion),
                (int)(color.G + (con.G - color.G) * proporcion),
                (int)(color.B + (con.B - color.B) * proporcion));
        }

        internal static GraphicsPath Redondeado(Rectangle r, int radio)
        {
            int d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
            GraphicsPath forma = new GraphicsPath();
            if (d <= 0)
            {
                forma.AddRectangle(r);
                return forma;
            }

            forma.AddArc(r.Left, r.Top, d, d, 180, 90);
            forma.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            forma.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            forma.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            forma.CloseFigure();
            return forma;
        }
    }

    /// <summary>
    /// El halo de "en curso": capas de verde cada vez mas transparentes alrededor
    /// de una tarjeta, con una intensidad que sube y baja cada 2 s, como si
    /// respirara. Todas laten a la vez (la fase sale del reloj del sistema). Lo
    /// usan el panel del administrador y la tarjeta "Ahora" del profesor.
    /// </summary>
    internal static class Resplandor
    {
        /// <summary>Hasta donde llega el halo fuera de la tarjeta, en pixeles.</summary>
        public const int Margen = 9;
        /// <summary>Cada cuanto se vuelve a pintar (unos 20 cuadros por segundo).</summary>
        public const int IntervaloMs = 50;
        private const int PeriodoMs = 2000;
        private static readonly Color Halo = Color.FromArgb(20, 200, 70);
        private static readonly Color BordeTenue = Color.DarkGreen;
        private static readonly Color BordeIntenso = Color.FromArgb(20, 185, 60);

        /// <summary>De 0 a 1 y vuelta, con un periodo de 2 s.</summary>
        public static float Intensidad()
        {
            double fase = (Environment.TickCount & int.MaxValue) % PeriodoMs / (double)PeriodoMs;
            return (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * fase));
        }

        /// <summary>El halo, por fuera del contorno de la tarjeta; se pinta antes que ella.</summary>
        public static void Dibujar(Graphics g, Rectangle contorno, int radio)
        {
            float intensidad = 0.25f + 0.75f * Intensidad();
            for (int capa = Margen; capa >= 1; capa--)
            {
                // Mas opaco pegado a la tarjeta, casi nada en el borde de fuera.
                int alfa = (int)(intensidad * 140f * (Margen - capa + 1) / Margen);
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(Rectangle.Inflate(contorno, capa, capa), radio + capa))
                using (Pen lapiz = new Pen(Color.FromArgb(alfa, Halo), 2f))
                {
                    g.DrawPath(lapiz, forma);
                }
            }
        }

        /// <summary>El borde de la tarjeta, que late con el halo: de verde oscuro a verde vivo.</summary>
        public static Color Borde()
        {
            float t = Intensidad();
            return Color.FromArgb(
                (int)(BordeTenue.R + (BordeIntenso.R - BordeTenue.R) * t),
                (int)(BordeTenue.G + (BordeIntenso.G - BordeTenue.G) * t),
                (int)(BordeTenue.B + (BordeIntenso.B - BordeTenue.B) * t));
        }
    }

    /// <summary>Lo que comparten las listas de la administracion para que nada se corte a 10 pt.</summary>
    internal static class ColumnasDeLista
    {
        /// <summary>
        /// Esas columnas, nunca mas angostas que su encabezado: una palabra
        /// ("Estudiantes") no pasa a la linea siguiente y se cortaria. Las listas
        /// de EstiloHorarios.CrearGrid (ListaDigLab) ya lo hacen solas con cada
        /// columna (2026-10-31); esto queda para una lista que no lo sea. Un
        /// encabezado que se parte en renglones, en dos como mucho.
        /// </summary>
        public static void EncabezadoEntero(DataGridView grid, params string[] columnas)
        {
            foreach (string nombre in columnas)
            {
                DataGridViewColumn columna = grid.Columns[nombre];
                int ancho = ListaDigLab.AnchoDelEncabezado(columna);
                if (ancho > columna.MinimumWidth)
                {
                    columna.MinimumWidth = ancho;
                }
            }
        }

        /// <summary>
        /// Esas columnas largas, tan anchas como su texto mas largo (hasta maximo;
        /// uno mas largo se corta y sale entero al pasar el raton), con ancho fijo:
        /// las demas se reparten lo que queda por sus pesos. Con relleno no basta,
        /// porque la lista cambia los pesos sola en cuanto alguna columna llega a
        /// su minimo (ListaDigLab lo sube al leer) y el reparto deja de ser el
        /// pensado. Solo crece, como ListaDigLab: al filtrar, las columnas no
        /// bailan. Si no cabe todo, sale la barra de abajo.
        /// </summary>
        public static void TextoEntero(DataGridView grid, int maximo, params string[] columnas)
        {
            foreach (string nombre in columnas)
            {
                DataGridViewColumn columna = grid.Columns[nombre];
                if (!columna.Visible)
                {
                    continue;
                }

                DrawingFont fuente = columna.InheritedStyle.Font ?? grid.Font;
                // El margen de la celda (8 + 8 desde que las listas tienen aire): sin
                // contarlo, la fecha y el horario de Bitacoras salian cortados.
                int margen = 8 + columna.InheritedStyle.Padding.Horizontal;
                HashSet<string> medidos = new HashSet<string>(StringComparer.Ordinal);
                int pide = 0;
                foreach (DataGridViewRow fila in grid.Rows)
                {
                    string texto = Convert.ToString(fila.Cells[columna.Index].Value);
                    if (!string.IsNullOrEmpty(texto) && medidos.Add(texto))
                    {
                        // Lo que pide la celda: el texto y su margen (como PreferredSize).
                        pide = Math.Max(pide, TextRenderer.MeasureText(texto, fuente).Width + margen);
                    }
                }

                pide = Math.Max(Math.Min(pide, maximo), columna.MinimumWidth);
                if (medidos.Count == 0)
                {
                    // Sin nada que medir (lista vacia): sigue con su peso.
                    continue;
                }

                bool fija = columna.AutoSizeMode == DataGridViewAutoSizeColumnMode.None;
                if (!fija || pide > columna.Width)
                {
                    columna.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    columna.Width = fija ? Math.Max(columna.Width, pide) : pide;
                }
            }
        }
    }

    /// <summary>
    /// Boton grande del panel del administrador: titulo y una linea de lo que hay
    /// dentro. Con DescripcionCorta, la corta se pinta cuando la descripcion no
    /// cabe en el alto que le queda (con seis tarjetas, a 1280 y en el minimo del panel).
    /// </summary>
    internal class TarjetaOpcion : Control
    {
        private static readonly Color FondoEncima = Color.FromArgb(236, 246, 239);
        // Borde fino y suave con una franja verde a la izquierda (2026-10-31: antes,
        // borde grueso verde oscuro y el texto pegado a el).
        private static readonly Color BordeSuave = Color.FromArgb(205, 222, 210);
        private const int Franja = 5;

        // Las de la descripcion en OnPaint; con las mismas se mide si cabe.
        private const TextFormatFlags BanderasDescripcion = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
            | TextFormatFlags.EndEllipsis;

        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 14F, FontStyle.Bold);
        private readonly DrawingFont fuenteDescripcion = new DrawingFont("Century Gothic", 9F);
        private bool encima;
        private string descripcion;
        private string descripcionCorta;

        public TarjetaOpcion(string titulo, string descripcion)
        {
            Titulo = titulo;
            this.descripcion = descripcion;
            Text = titulo;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public string Titulo { get; private set; }

        /// <summary>La linea de debajo del titulo; se puede cambiar (por ejemplo, con cuantos van).</summary>
        public string Descripcion
        {
            get
            {
                return descripcion;
            }

            set
            {
                if (value == descripcion)
                {
                    return;
                }

                descripcion = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Opcional: la que se pinta si Descripcion pide mas renglones de los que
        /// caben (DescripcionQueCabe). Sin ella, siempre la larga.
        /// </summary>
        public string DescripcionCorta
        {
            get
            {
                return descripcionCorta;
            }

            set
            {
                if (value == descripcionCorta)
                {
                    return;
                }

                descripcionCorta = value;
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
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
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

        protected override bool IsInputKey(Keys tecla)
        {
            return tecla == Keys.Enter || base.IsInputKey(tecla);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
            }

            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuenteTitulo.Dispose();
                fuenteDescripcion.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);

            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(r, 10))
            {
                using (SolidBrush brocha = new SolidBrush(encima ? FondoEncima : Color.White))
                {
                    g.FillPath(brocha, forma);
                }

                // La franja de la izquierda, recortada a la forma.
                Region antes = g.Clip;
                g.SetClip(forma, CombineMode.Intersect);
                using (SolidBrush franja = new SolidBrush(EstiloHorarios.VerdeTeschi))
                {
                    g.FillRectangle(franja, r.Left, r.Top, Franja, r.Height + 1);
                }

                g.Clip = antes;
                using (Pen borde = new Pen(Focused || encima ? EstiloHorarios.VerdeTeschi : BordeSuave, Focused ? 2f : 1f))
                {
                    g.DrawPath(borde, forma);
                }
            }

            Rectangle texto = new Rectangle(r.Left + Franja + 14, r.Top + 12, r.Width - Franja - 28, r.Height - 22);
            int altoTitulo = TextRenderer.MeasureText(g, Titulo, fuenteTitulo).Height;
            TextRenderer.DrawText(g, Titulo, fuenteTitulo, new Rectangle(texto.Left, texto.Top, texto.Width, altoTitulo), Color.DarkGreen,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            int yDescripcion = texto.Top + altoTitulo + 4;
            Rectangle sitio = new Rectangle(texto.Left, yDescripcion, texto.Width, texto.Bottom - yDescripcion);
            TextRenderer.DrawText(g, DescripcionQueCabe(g, sitio), fuenteDescripcion, sitio, Color.FromArgb(95, 99, 96), BanderasDescripcion);
        }

        /// <summary>
        /// La descripcion si cabe entera en el sitio; si no y hay corta, la corta.
        /// Un renglon cuenta como entero aunque pierda su ultima fila de pixeles,
        /// que en Century Gothic 9 va vacia: en los 50 px que deja la fila de
        /// tarjetas caben 3 de 17.
        /// </summary>
        private string DescripcionQueCabe(Graphics g, Rectangle sitio)
        {
            if (string.IsNullOrEmpty(descripcionCorta) || string.IsNullOrEmpty(descripcion) || sitio.Width <= 0)
            {
                return descripcion;
            }

            int renglon = TextRenderer.MeasureText(g, "Ág", fuenteDescripcion).Height;
            int caben = Math.Max(1, (sitio.Height + 1) / Math.Max(1, renglon));
            int pide = TextRenderer.MeasureText(g, descripcion, fuenteDescripcion, new Size(sitio.Width, int.MaxValue), BanderasDescripcion).Height;
            return pide <= caben * renglon ? descripcion : descripcionCorta;
        }
    }
}
