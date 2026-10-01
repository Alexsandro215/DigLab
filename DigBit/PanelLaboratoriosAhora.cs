using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DrawingFont = System.Drawing.Font;

namespace DigBit
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
    /// profesor, el mismo que en la cuadricula de Horarios.
    /// </summary>
    internal class PanelLaboratoriosAhora : Control
    {
        private const int AnchoEtiquetas = 112;
        private const int AltoCabecera = 30;
        private const int Hueco = 10;
        private const int AltoAviso = 30;
        private const int Relleno = 10;

        // Por debajo de este ancho una tarjeta ya no se lee (se probo con 8 y 10
        // laboratorios): en vez de aplastar las columnas, se desplazan de lado.
        private const int AnchoMinimoColumna = 165;
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
        private static readonly Color Naranja = Color.FromArgb(190, 85, 0);
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

        private PanoramaLaboratorios panorama;
        private DateTime ahora;
        private string aviso;
        private int desplazamiento;

        // Donde quedaron las columnas en el ultimo pintado, para saber que
        // laboratorio hay bajo el raton.
        private Rectangle zonaPintada;
        private int anchoPintado;
        private int laboratorioBajoRaton;

        /// <summary>Clic en la columna de un laboratorio (su id): el panel principal abre su horario.</summary>
        public event Action<int> LaboratorioElegido;

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
            int id = laboratorio == null ? 0 : laboratorio.Id;
            if (id == laboratorioBajoRaton)
            {
                return;
            }

            laboratorioBajoRaton = id;
            Cursor = laboratorio == null ? Cursors.Default : Cursors.Hand;
            ayuda.SetToolTip(this, laboratorio == null ? null : "Ver el horario de " + laboratorio.Texto);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            laboratorioBajoRaton = 0;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            OpcionCombo laboratorio = LaboratorioEn(e.Location);
            if (e.Button == MouseButtons.Left && laboratorio != null && LaboratorioElegido != null)
            {
                LaboratorioElegido(laboratorio.Id);
            }
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
            int altoFila = (area.Height - AltoCabecera - Hueco * 3) / 3;
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

                Rectangle cabecera = new Rectangle(x, area.Top, anchoColumna, AltoCabecera);
                TextRenderer.DrawText(g, laboratorio.Texto, fuenteCabecera, new Rectangle(x + 2, area.Top, anchoColumna - 4, AltoCabecera - 6), Verde,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | Recortado);
                using (Pen linea = new Pen(Verde, 2f))
                {
                    g.DrawLine(linea, cabecera.Left, cabecera.Bottom - 2, cabecera.Right, cabecera.Bottom - 2);
                }

                List<SesionAgenda> agenda = panorama.Agenda.Where(s => s.LaboratorioId == laboratorio.Id).ToList();
                for (int fila = 0; fila < 3; fila++)
                {
                    SesionAgenda enEsaHora = HorasDelPanel.EnLaHora(agenda, horas[fila], hora);
                    DibujarCelda(g, new Rectangle(x, FilaY(area, fila, altoFila), anchoColumna, altoFila), enEsaHora, fila, hora);
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

        private static int FilaY(Rectangle area, int fila, int altoFila)
        {
            return area.Top + AltoCabecera + Hueco + fila * (altoFila + Hueco);
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

            foreach (Tuple<string, Color> estado in Estado(sesion, fila, hora))
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
        /// solo la de ahora lleva el detalle, para no repetirlo.
        /// </summary>
        private static IEnumerable<Tuple<string, Color>> Estado(SesionAgenda sesion, int fila, TimeSpan hora)
        {
            if (sesion.Cancelada)
            {
                yield return Tuple.Create("Cancelada" + (string.IsNullOrEmpty(sesion.Motivo) ? "" : ": " + sesion.Motivo), TextoSecundario);
                yield break;
            }

            if (sesion.Inicio > hora)
            {
                TimeSpan falta = sesion.Inicio - hora;
                yield return Tuple.Create("Empieza en " + Duracion(falta), Verde);
                if (falta <= TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos))
                {
                    yield return Tuple.Create("Ya se acepta el código", TextoSecundario);
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

            if (enCurso)
            {
                yield return Tuple.Create("Quedan " + Duracion(sesion.Fin - hora), Verde);
            }

            if (sesion.Alumnos == 0)
            {
                // En curso puede ser que aun no lleguen; si ya termino, es de revisar.
                yield return Tuple.Create(enCurso ? "Aún sin bitácoras" : "Nadie registró bitácora", enCurso ? TextoSecundario : Naranja);
                yield break;
            }

            string registro = sesion.Alumnos + (sesion.Alumnos == 1 ? " bitácora" : " bitácoras");
            if (sesion.ConFallas == 0)
            {
                yield return Tuple.Create(registro + ", sin fallas", Verde);
            }
            else
            {
                yield return Tuple.Create(registro + ", " + sesion.ConFallas + (sesion.ConFallas == 1 ? " con falla" : " con fallas"), Naranja);
            }
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

    /// <summary>Boton grande del panel del administrador: titulo y una linea de lo que hay dentro.</summary>
    internal class TarjetaOpcion : Control
    {
        private static readonly Color FondoEncima = Color.FromArgb(236, 246, 236);

        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 14F, FontStyle.Bold);
        private readonly DrawingFont fuenteDescripcion = new DrawingFont("Century Gothic", 9F);
        private bool encima;

        public TarjetaOpcion(string titulo, string descripcion)
        {
            Titulo = titulo;
            Descripcion = descripcion;
            Text = titulo;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public string Titulo { get; private set; }

        public string Descripcion { get; private set; }

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

                using (Pen borde = new Pen(Color.DarkGreen, Focused ? 3f : 2f))
                {
                    g.DrawPath(borde, forma);
                }
            }

            Rectangle texto = Rectangle.Inflate(r, -14, -10);
            int altoTitulo = TextRenderer.MeasureText(g, Titulo, fuenteTitulo).Height;
            TextRenderer.DrawText(g, Titulo, fuenteTitulo, new Rectangle(texto.Left, texto.Top, texto.Width, altoTitulo), Color.DarkGreen,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            int yDescripcion = texto.Top + altoTitulo + 4;
            TextRenderer.DrawText(g, Descripcion, fuenteDescripcion,
                new Rectangle(texto.Left, yDescripcion, texto.Width, texto.Bottom - yDescripcion), Color.FromArgb(80, 80, 80),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
}
