using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DigBit.conexion;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// La clase de ahora en la pantalla del alumno, como la ficha del panel del
    /// profesor: el circulo que eligio el profesor (db/19) a la izquierda, una
    /// pastilla ("Tu clase", "En curso" o "Empieza a las 07:00") con lo que
    /// queda, la materia en grande, el grupo, el profesor, el laboratorio y el
    /// equipo como campos, y una barra del inicio al fin. Solo se pinta: no se
    /// pulsa. Sin clase (uso libre, errores, sin laboratorio) la pantalla usa
    /// sus etiquetas de siempre.
    /// </summary>
    internal sealed class FichaClaseAlumno : Control
    {
        private const int Radio = 12;
        private const int Margen = 18;
        private const int Circulo = 76;
        private static readonly Color Fondo = Color.FromArgb(244, 250, 244);
        private static readonly Color Borde = Color.FromArgb(205, 225, 205);
        private static readonly Color FondoSinConexion = Color.FromArgb(255, 244, 228);
        private static readonly Color BordeSinConexion = Color.FromArgb(235, 200, 150);
        private static readonly Color TextoPrincipal = Color.FromArgb(30, 30, 30);
        private static readonly Color TextoSecundario = Color.FromArgb(90, 90, 90);
        private static readonly Color TituloCampo = Color.FromArgb(100, 125, 100);
        private static readonly Color VerdeAhora = Color.FromArgb(46, 125, 50);
        private static readonly Color NaranjaPronto = Color.FromArgb(170, 80, 0);
        private static readonly Color FondoBarra = Color.FromArgb(214, 232, 214);

        private readonly DrawingFont fuenteEstado = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteQuedan = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
        private readonly DrawingFont fuenteMateria = new DrawingFont("Century Gothic", 17F, FontStyle.Bold);
        private readonly DrawingFont fuenteTituloCampo = new DrawingFont("Century Gothic", 7.75F, FontStyle.Bold);
        private readonly DrawingFont fuenteValorCampo = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteHora = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private readonly DrawingFont fuenteNota = new DrawingFont("Century Gothic", 9.5F);

        private string materia = "";
        private string grupo = "";
        private string profesor = "";
        private string laboratorio = "";
        private string equipo = "";
        private TimeSpan inicio;
        private TimeSpan fin;
        private bool esLaTuya;
        private bool esExtra;
        private string nota;
        private bool sinConexion;
        private AspectoClase aspecto;
        // El minuto de lo ultimo pintado: el reloj de la pantalla solo repinta si cambio.
        private int minutoPintado = -1;

        public FichaClaseAlumno()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            TabStop = false;
        }

        /// <summary>Lo que mide con lo que tiene que ensenar (con nota, un renglon mas).</summary>
        public int AltoNecesario
        {
            get { return string.IsNullOrEmpty(nota) ? 158 : 182; }
        }

        /// <summary>El circulo de la clase (null: sin circulo, el texto empieza a la izquierda).</summary>
        public AspectoClase Aspecto
        {
            get { return aspecto; }
            set
            {
                aspecto = value;
                Invalidate();
            }
        }

        /// <summary>Pone la clase. esLaTuya: el alumno ya escribio su codigo (o la eligio).</summary>
        public void Mostrar(string materia, string grupo, string profesor, string laboratorio, string equipo,
            TimeSpan inicio, TimeSpan fin, bool esLaTuya, bool esExtra, string nota, bool sinConexion, AspectoClase aspecto)
        {
            this.materia = materia ?? "";
            this.grupo = grupo ?? "";
            this.profesor = profesor ?? "";
            this.laboratorio = laboratorio ?? "";
            this.equipo = equipo ?? "";
            this.inicio = inicio;
            this.fin = fin;
            this.esLaTuya = esLaTuya;
            this.esExtra = esExtra;
            this.nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
            this.sinConexion = sinConexion;
            this.aspecto = aspecto;
            AccessibleName = (esLaTuya ? "Tu clase: " : "Clase de ahora: ") + this.materia + ", grupo " + this.grupo
                + (this.profesor.Length > 0 ? ", con " + this.profesor : "");
            Invalidate();
        }

        /// <summary>Lo llama el reloj de la pantalla cada segundo: repinta solo si cambio el minuto.</summary>
        public void Tic()
        {
            int minuto = (int)DateTime.Now.TimeOfDay.TotalMinutes;
            if (Visible && minuto != minutoPintado)
            {
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            TimeSpan ahora = DateTime.Now.TimeOfDay;
            minutoPintado = (int)ahora.TotalMinutes;

            Rectangle caja = new Rectangle(0, 0, Width - 1, Height - 1);
            if (caja.Width < 40 || caja.Height < 40)
            {
                return;
            }

            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(caja, Radio))
            {
                using (SolidBrush relleno = new SolidBrush(sinConexion ? FondoSinConexion : Fondo))
                {
                    g.FillPath(relleno, forma);
                }

                if (aspecto != null)
                {
                    CirculoClase.PintarLomo(g, forma, caja, aspecto.Color, 6);
                }

                using (Pen borde = new Pen(sinConexion ? BordeSinConexion : Borde))
                {
                    g.DrawPath(borde, forma);
                }
            }

            int x = Margen + (aspecto != null ? 6 : 0);
            int arriba = 16;
            if (aspecto != null)
            {
                int alto = Height - (nota == null ? 0 : 24);
                CirculoClase.Pintar(g, new Rectangle(x, Math.Max(arriba, (alto - Circulo) / 2), Circulo, Circulo), aspecto);
                x += Circulo + 20;
            }

            int derecha = Width - Margen;
            int ancho = Math.Max(60, derecha - x);
            int y = arriba;

            // 1) La pastilla y lo que queda.
            bool enCurso = ahora >= inicio && ahora < fin;
            string estado = esLaTuya ? "Tu clase" : enCurso ? "En curso" : ahora < inicio ? "Empieza a las " + HorariosDatos.Hora(inicio) : "Terminó";
            if (esExtra)
            {
                estado += " (sesión extra)";
            }

            Color colorEstado = enCurso || esLaTuya ? VerdeAhora : ahora < inicio ? NaranjaPronto : TextoSecundario;
            Size medida = TextRenderer.MeasureText(g, estado, fuenteEstado, Size.Empty, TextFormatFlags.NoPadding);
            Rectangle pastilla = new Rectangle(x, y, medida.Width + 20, 22);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(pastilla, pastilla.Height / 2))
            using (SolidBrush relleno = new SolidBrush(colorEstado))
            {
                g.FillPath(relleno, forma);
            }

            TextRenderer.DrawText(g, estado, fuenteEstado, pastilla, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (enCurso)
            {
                TextRenderer.DrawText(g, Quedan(fin - ahora), fuenteQuedan, new Rectangle(pastilla.Right + 10, y, Math.Max(10, derecha - pastilla.Right - 10), 22),
                    VerdeAhora, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            y += 22 + 6;

            // 2) La materia, en grande.
            TextRenderer.DrawText(g, materia, fuenteMateria, new Rectangle(x - 2, y, ancho + 2, 32), TextoPrincipal,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            y += 32 + 6;

            // 3) Los campos: grupo, profesor, laboratorio y equipo. El profesor y el
            // laboratorio se llevan lo que sobre; si no cabe, con "...".
            string[] titulos = { "GRUPO", "DOCENTE", "LABORATORIO", "EQUIPO" };
            string[] valores = { grupo, profesor, laboratorio, equipo };
            int[] anchos = new int[titulos.Length];
            int fijos = 0;
            const int Separacion = 26;
            for (int i = 0; i < titulos.Length; i++)
            {
                int valor = TextRenderer.MeasureText(g, valores[i].Length > 0 ? valores[i] : "-", fuenteValorCampo, Size.Empty, TextFormatFlags.NoPadding).Width;
                int titulo = TextRenderer.MeasureText(g, titulos[i], fuenteTituloCampo, Size.Empty, TextFormatFlags.NoPadding).Width;
                anchos[i] = Math.Max(valor, titulo) + 4;
                fijos += anchos[i];
            }

            int sobra = ancho - fijos - Separacion * (titulos.Length - 1);
            if (sobra < 0)
            {
                // Se recortan el profesor y el laboratorio, a partes iguales.
                int recorte = (-sobra + 1) / 2;
                anchos[1] = Math.Max(60, anchos[1] - recorte);
                anchos[2] = Math.Max(60, anchos[2] - recorte);
            }

            int xc = x;
            for (int i = 0; i < titulos.Length && xc < derecha; i++)
            {
                int a = Math.Min(anchos[i], derecha - xc);
                TextRenderer.DrawText(g, titulos[i], fuenteTituloCampo, new Rectangle(xc, y, a, 14), TituloCampo,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, valores[i].Length > 0 ? valores[i] : "-", fuenteValorCampo, new Rectangle(xc, y + 15, a, 20), TextoPrincipal,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                xc += a + Separacion;
            }

            y += 38 + 8;

            // 4) La barra del inicio al fin, con lo que ya paso.
            string desde = HorariosDatos.Hora(inicio);
            string hasta = HorariosDatos.Hora(fin);
            int anchoHora = TextRenderer.MeasureText(g, "00:00", fuenteHora, Size.Empty, TextFormatFlags.NoPadding).Width + 4;
            TextRenderer.DrawText(g, desde, fuenteHora, new Rectangle(x, y, anchoHora, 16), TextoPrincipal,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, hasta, fuenteHora, new Rectangle(derecha - anchoHora, y, anchoHora, 16), TextoPrincipal,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            Rectangle pista = new Rectangle(x + anchoHora + 8, y + 5, Math.Max(10, ancho - 2 * anchoHora - 16), 7);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(pista, 3))
            using (SolidBrush fondoBarra = new SolidBrush(FondoBarra))
            {
                g.FillPath(fondoBarra, forma);
            }

            double total = (fin - inicio).TotalMinutes;
            double paso = total <= 0 ? 0 : Math.Max(0, Math.Min(1, (ahora - inicio).TotalMinutes / total));
            if (paso > 0)
            {
                Rectangle lleno = new Rectangle(pista.X, pista.Y, Math.Max(7, (int)Math.Round(pista.Width * paso)), pista.Height);
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(lleno, 3))
                using (SolidBrush relleno = new SolidBrush(VerdeAhora))
                {
                    g.FillPath(relleno, forma);
                }
            }

            y += 16 + 8;

            // 5) La nota (sin conexion, "eligela abajo"...), a todo lo ancho.
            if (nota != null)
            {
                TextRenderer.DrawText(g, nota, fuenteNota, new Rectangle(Margen + (aspecto != null ? 6 : 0), y, Width - 2 * Margen, 20),
                    sinConexion ? NaranjaPronto : TextoSecundario,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }

        /// <summary>"quedan 1 h 20 min", "quedan 5 min", "queda 1 min".</summary>
        private static string Quedan(TimeSpan falta)
        {
            int minutos = Math.Max(1, (int)Math.Ceiling(falta.TotalMinutes));
            int horas = minutos / 60;
            int resto = minutos % 60;
            string texto = horas > 0 ? horas + " h" + (resto > 0 ? " " + resto + " min" : "") : resto + " min";
            return (minutos == 1 ? "queda " : "quedan ") + texto;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuenteEstado.Dispose();
                fuenteQuedan.Dispose();
                fuenteMateria.Dispose();
                fuenteTituloCampo.Dispose();
                fuenteValorCampo.Dispose();
                fuenteHora.Dispose();
                fuenteNota.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
