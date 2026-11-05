using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// "Asi quedaran las fechas" de 3. Aplicar con fechas: dos barras sobre una misma
    /// linea de tiempo, el horario de ahora hasta su ultimo dia y el nuevo de su primer
    /// a su ultimo dia, con la marca de hoy y los meses debajo. Entre las dos, lo que
    /// queda sin clases (rayado) o lo que se encima (en rojo). Solo ensena: las fechas
    /// se eligen en la pagina (PestanaAplicacion) y las revisa RevisionAplicacion.
    /// </summary>
    internal sealed class LineaDeFechasHorario : Control
    {
        public const int AltoPreferido = 112;
        private const int AnchoRotulo = 124;
        private const int ArribaFilas = 24;
        private const int AltoFila = 30;
        private const int EntreFilas = 6;
        private const int MargenTexto = 8;
        private const TextFormatFlags FormatoTexto = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        private static readonly string[] Meses = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };
        private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;
        private static readonly Color ColorDeAhora = Color.FromArgb(178, 192, 206);
        private static readonly Color TextoOscuro = Color.FromArgb(40, 40, 40);
        private static readonly Color TextoGris = Color.FromArgb(105, 105, 105);
        private static readonly Color TextoRojo = Color.FromArgb(170, 30, 30);
        private static readonly Color TextoAmbar = Color.FromArgb(140, 95, 0);
        private static readonly Color LineaDelMes = Color.FromArgb(232, 232, 232);
        private static readonly Color RayadoSinClases = Color.FromArgb(232, 190, 90);

        private readonly DrawingFont fuente = new DrawingFont("Century Gothic", 9F);
        private readonly DrawingFont fuenteNegrita = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private readonly DrawingFont fuenteChica = new DrawingFont("Century Gothic", 8.25F);

        private bool conDatos;
        private bool hayEnUso;
        private bool sustituyeHoy;
        private bool empiezaAhora;
        private string nombreEnUso;
        private string nombreNuevo = "";
        private DateTime finEnUso;
        private DateTime desde;
        private DateTime hasta;
        private DateTime hoy;

        public LineaDeFechasHorario()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Así quedarán las fechas";
        }

        /// <summary>
        /// Lo que se dibuja. hayEnUso: si hoy hay clases de un horario de ahora;
        /// nombreEnUso: el suyo si es uno solo, o null; sustituyeHoy: ese horario
        /// empezo hoy mismo y el nuevo lo sustituye.
        /// </summary>
        public void Poner(bool conHorarioEnUso, string nombreDelEnUso, bool empezoHoyYSeSustituye, DateTime ultimoDiaEnUso,
            string nombreDelNuevo, DateTime primerDia, DateTime ultimoDia, DateTime diaDeHoy, bool ahoraMismo)
        {
            hayEnUso = conHorarioEnUso;
            nombreEnUso = string.IsNullOrWhiteSpace(nombreDelEnUso) ? null : nombreDelEnUso.Trim();
            sustituyeHoy = empezoHoyYSeSustituye;
            finEnUso = ultimoDiaEnUso.Date;
            nombreNuevo = nombreDelNuevo ?? "";
            desde = primerDia.Date;
            hasta = ultimoDia.Date;
            hoy = diaDeHoy.Date;
            empiezaAhora = ahoraMismo;
            conDatos = true;
            AccessibleDescription = TextosDeAhora().First() + ". " + TextosDelNuevo().First() + ".";
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuente.Dispose();
                fuenteNegrita.Dispose();
                fuenteChica.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!conDatos || Width < AnchoRotulo + 40)
            {
                return;
            }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Del dia mas temprano que importa al mas tardio; antes, un trozo del pasado
            // para que el horario de ahora se vea venir de atras.
            DateTime primero = new[] { hoy, desde, hayEnUso ? finEnUso.AddDays(1) : hoy }.Min();
            DateTime ultimo = new[] { hoy.AddDays(1), desde.AddDays(1), hasta.AddDays(1), hayEnUso ? finEnUso.AddDays(1) : hoy }.Max();
            double rango = Math.Max(1, (ultimo - primero).TotalDays);
            DateTime ejeIni = primero.AddDays(-Math.Max(7, rango * 0.12));
            DateTime ejeFin = ultimo.AddDays(rango * 0.01);
            int xIni = AnchoRotulo;
            int xFin = Width - 2;
            Func<DateTime, int> x = dia => xIni + (int)Math.Round((dia - ejeIni).TotalDays / (ejeFin - ejeIni).TotalDays * (xFin - xIni));

            int yAhora = ArribaFilas;
            int yNuevo = yAhora + AltoFila + EntreFilas;
            int yEje = yNuevo + AltoFila + 4;

            DibujarMeses(g, ejeIni, ejeFin, x, yAhora, yEje);
            Rotulo(g, "Horario de ahora", yAhora, TextoOscuro);
            Rotulo(g, "Horario nuevo", yNuevo, EstiloHorarios.VerdeTeschi);
            DibujarDeAhora(g, x, xIni, xFin, yAhora);
            bool textoAfuera = DibujarNuevo(g, x, xIni, xFin, yNuevo);
            DibujarEntreLosDos(g, x, xIni, yAhora, yNuevo, !textoAfuera);
            DibujarHoy(g, x(hoy), xIni, xFin, yEje);
        }

        private void Rotulo(Graphics g, string texto, int y, Color color)
        {
            TextRenderer.DrawText(g, texto, fuenteNegrita, new Rectangle(0, y, AnchoRotulo - 6, AltoFila), color, FormatoTexto);
        }

        /// <summary>Una raya suave al empezar cada mes y su nombre debajo; el año en el primero y en enero.</summary>
        private void DibujarMeses(Graphics g, DateTime ejeIni, DateTime ejeFin, Func<DateTime, int> x, int arriba, int yEje)
        {
            int finDelAnterior = -1000;
            bool primero = true;
            using (Pen raya = new Pen(LineaDelMes))
            {
                g.DrawLine(raya, AnchoRotulo, yEje, Width - 2, yEje);
                for (DateTime mes = new DateTime(ejeIni.Year, ejeIni.Month, 1).AddMonths(1); mes < ejeFin; mes = mes.AddMonths(1))
                {
                    int xMes = x(mes);
                    g.DrawLine(raya, xMes, arriba, xMes, yEje + 3);
                    string texto = Meses[mes.Month - 1] + (primero || mes.Month == 1 ? " " + mes.Year.ToString(Invariante) : "");
                    int ancho = Medir(texto, fuenteChica);
                    if (xMes + 3 >= finDelAnterior + 6 && xMes + 3 + ancho <= Width)
                    {
                        TextRenderer.DrawText(g, texto, fuenteChica, new Rectangle(xMes + 3, yEje + 2, ancho + 2, 16), TextoGris, FormatoTexto);
                        finDelAnterior = xMes + 3 + ancho;
                        primero = false;
                    }
                }
            }
        }

        private void DibujarDeAhora(Graphics g, Func<DateTime, int> x, int xIni, int xFin, int y)
        {
            if (!hayEnUso)
            {
                // Despues de la raya de hoy, para que no le pase por encima.
                int izquierda = Math.Max(xIni, x(hoy)) + MargenTexto;
                TextRenderer.DrawText(g, "Hoy no hay ningún horario en uso", fuente, new Rectangle(izquierda, y, Math.Max(10, xFin - izquierda), AltoFila), TextoGris, FormatoTexto);
                return;
            }

            Rectangle barra = new Rectangle(xIni, y + 3, Math.Max(6, x(finEnUso.AddDays(1)) - xIni), AltoFila - 6);
            Barra(g, barra, ColorDeAhora, true);
            PonerTexto(g, TextosDeAhora(), barra, xIni, xFin, TextoOscuro, TextoOscuro, true);
        }

        /// <summary>Devuelve si su texto se quedo fuera de la barra, a su izquierda o a su derecha.</summary>
        private bool DibujarNuevo(Graphics g, Func<DateTime, int> x, int xIni, int xFin, int y)
        {
            if (hasta < desde)
            {
                TextRenderer.DrawText(g, "Su último día es antes del primero", fuente, new Rectangle(xIni + MargenTexto, y, xFin - xIni - MargenTexto, AltoFila), TextoRojo, FormatoTexto);
                return true;
            }

            int izquierda = x(desde);
            Rectangle barra = new Rectangle(izquierda, y + 3, Math.Max(6, x(hasta.AddDays(1)) - izquierda), AltoFila - 6);
            Barra(g, barra, EstiloHorarios.VerdeTeschi, false);
            // Lo que queda sin clases va justo antes de la barra: su texto, antes de eso.
            bool conRayado = hayEnUso && (desde - finEnUso).Days > 1;
            return !PonerTexto(g, TextosDelNuevo(), barra, xIni, xFin, Color.White, TextoOscuro, false, conRayado ? x(finEnUso.AddDays(1)) : barra.Left);
        }

        /// <summary>Lo que hay entre el ultimo dia de uno y el primero del otro: dias sin clases, o dias que se enciman.</summary>
        private void DibujarEntreLosDos(Graphics g, Func<DateTime, int> x, int xIni, int yAhora, int yNuevo, bool conTexto)
        {
            if (!hayEnUso || hasta < desde)
            {
                return;
            }

            int dias = (desde - finEnUso).Days - 1;
            if (dias < 0)
            {
                // Se enciman: lo dice tambien la revision, en rojo.
                int ini = x(desde);
                using (SolidBrush rojo = new SolidBrush(Color.FromArgb(90, 198, 40, 40)))
                {
                    g.FillRectangle(rojo, ini, yAhora, Math.Max(3, x(finEnUso.AddDays(1)) - ini), yNuevo + AltoFila - yAhora);
                }

                return;
            }

            if (dias == 0)
            {
                return;
            }

            int izquierda = x(finEnUso.AddDays(1));
            int ancho = Math.Max(3, x(desde) - izquierda);
            Rectangle rayado = new Rectangle(izquierda, yNuevo + 3, ancho, AltoFila - 6);
            using (HatchBrush brocha = new HatchBrush(HatchStyle.WideUpwardDiagonal, RayadoSinClases, Color.White))
            {
                g.FillRectangle(brocha, rayado);
            }

            if (conTexto)
            {
                string texto = "Sin clases: " + Duracion(dias);
                int medido = Medir(texto, fuente);
                if (medido + MargenTexto <= izquierda - xIni)
                {
                    TextRenderer.DrawText(g, texto, fuente, new Rectangle(izquierda - medido - MargenTexto, yNuevo, medido + 4, AltoFila), TextoAmbar, FormatoTexto);
                }
                else if (medido + 12 <= ancho)
                {
                    using (SolidBrush fondo = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(fondo, izquierda + (ancho - medido) / 2 - 3, yNuevo + 6, medido + 6, AltoFila - 12);
                    }

                    TextRenderer.DrawText(g, texto, fuente, new Rectangle(izquierda + (ancho - medido) / 2, yNuevo, medido + 4, AltoFila), TextoAmbar, FormatoTexto);
                }
            }
        }

        private void DibujarHoy(Graphics g, int xHoy, int xIni, int xFin, int yEje)
        {
            using (Pen raya = new Pen(PestanaProximoCiclo.AzulOscuro, 1.5F) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(raya, xHoy, ArribaFilas - 4, xHoy, yEje);
            }

            const string Texto = "Hoy";
            int ancho = Medir(Texto, fuenteNegrita) + 16;
            Rectangle pastilla = new Rectangle(Math.Min(Math.Max(xIni, xHoy - ancho / 2), xFin - ancho), 0, ancho, 20);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(pastilla, 9))
            using (SolidBrush relleno = new SolidBrush(PestanaProximoCiclo.AzulOscuro))
            {
                g.FillPath(relleno, forma);
            }

            TextRenderer.DrawText(g, Texto, fuenteNegrita, pastilla, Color.White, FormatoTexto | TextFormatFlags.HorizontalCenter);
        }

        /// <summary>Una barra redondeada; la de ahora viene del pasado: entra desvanecida por la izquierda.</summary>
        private static void Barra(Graphics g, Rectangle barra, Color color, bool vieneDeAtras)
        {
            Rectangle forma = vieneDeAtras ? new Rectangle(barra.X - 12, barra.Y, barra.Width + 12, barra.Height) : barra;
            Region antes = g.Clip;
            if (vieneDeAtras)
            {
                g.SetClip(barra, CombineMode.Intersect);
            }

            using (GraphicsPath camino = PanelLaboratoriosAhora.Redondeado(forma, Math.Min(7, Math.Max(1, Math.Min(forma.Width, forma.Height) / 2 - 1))))
            using (SolidBrush relleno = new SolidBrush(color))
            {
                g.FillPath(relleno, camino);
            }

            if (vieneDeAtras && barra.Width > 8)
            {
                Rectangle desvanecido = new Rectangle(barra.X, barra.Y, Math.Min(barra.Width, 26), barra.Height);
                // La brocha, un poco mas ancha: justo en su borde vuelve a empezar y dejaba una raya blanca.
                Rectangle deLaBrocha = new Rectangle(desvanecido.X - 2, desvanecido.Y, desvanecido.Width + 4, desvanecido.Height);
                using (LinearGradientBrush brocha = new LinearGradientBrush(deLaBrocha, Color.White, Color.FromArgb(0, Color.White), LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(brocha, desvanecido);
                }
            }

            g.Clip = antes;
        }

        /// <summary>
        /// El texto mas largo que quepa en el sitio preferido (con primeroAfuera, a la
        /// derecha de la barra; si no, dentro); si ninguno cabe ahi, en el otro de esos
        /// dos y luego a la izquierda, hasta limiteIzquierdo. Si no cabe en ninguno, el
        /// mas corto cortado con "..." en el mayor. Devuelve si quedo dentro de la barra.
        /// </summary>
        private bool PonerTexto(Graphics g, string[] textos, Rectangle barra, int xIni, int xFin, Color colorDentro, Color colorFuera, bool primeroAfuera, int limiteIzquierdo = -1)
        {
            int finIzquierda = limiteIzquierdo < 0 ? barra.Left : limiteIzquierdo;
            int libreDentro = barra.Width - 2 * MargenTexto;
            int libreDerecha = xFin - barra.Right - MargenTexto;
            int libreIzquierda = finIzquierda - xIni - MargenTexto;
            Rectangle dentro = new Rectangle(barra.X + MargenTexto, barra.Y - 3, Math.Max(10, libreDentro), AltoFila);
            Rectangle derecha = new Rectangle(barra.Right + MargenTexto, barra.Y - 3, Math.Max(10, libreDerecha), AltoFila);
            string[] enDentro = textos.Where(t => Medir(t, fuente) <= libreDentro).ToArray();
            string[] enDerecha = textos.Where(t => Medir(t, fuente) <= libreDerecha).ToArray();
            string[] enIzquierda = textos.Where(t => Medir(t, fuente) <= libreIzquierda).ToArray();
            if (!primeroAfuera && enDentro.Length > 0)
            {
                TextRenderer.DrawText(g, enDentro[0], fuente, dentro, colorDentro, FormatoTexto);
                return true;
            }

            if (enDerecha.Length > 0)
            {
                TextRenderer.DrawText(g, enDerecha[0], fuente, derecha, colorFuera, FormatoTexto);
                return false;
            }

            if (enDentro.Length > 0)
            {
                TextRenderer.DrawText(g, enDentro[0], fuente, dentro, colorDentro, FormatoTexto);
                return true;
            }

            if (enIzquierda.Length > 0)
            {
                int medido = Medir(enIzquierda[0], fuente);
                TextRenderer.DrawText(g, enIzquierda[0], fuente, new Rectangle(finIzquierda - MargenTexto - medido, barra.Y - 3, medido + 2, AltoFila), colorFuera, FormatoTexto);
                return false;
            }

            // No cabe entero en ningun sitio: en el mayor, cortado.
            string corto = textos[textos.Length - 1];
            if (libreDentro >= libreDerecha && libreDentro >= libreIzquierda)
            {
                TextRenderer.DrawText(g, corto, fuente, dentro, colorDentro, FormatoTexto);
                return true;
            }

            if (libreDerecha >= libreIzquierda)
            {
                TextRenderer.DrawText(g, corto, fuente, derecha, colorFuera, FormatoTexto);
            }
            else
            {
                TextRenderer.DrawText(g, corto, fuente, new Rectangle(xIni, barra.Y - 3, Math.Max(10, libreIzquierda), AltoFila), colorFuera, FormatoTexto);
            }

            return false;
        }

        /// <summary>Lo que dice la barra de ahora, del texto mas largo al mas corto.</summary>
        private string[] TextosDeAhora()
        {
            if (!hayEnUso)
            {
                return new[] { "Hoy no hay ningún horario en uso" };
            }

            string quien = nombreEnUso == null ? "" : "«" + nombreEnUso + "» ";
            string largo;
            string corto;
            if (sustituyeHoy)
            {
                largo = "empezó hoy y se sustituye";
                corto = "se sustituye hoy";
            }
            else if (empiezaAhora || finEnUso == hoy.AddDays(-1))
            {
                largo = "termina ayer, " + HistorialCambios.DiaYFecha(finEnUso);
                corto = "termina ayer, " + HistorialCambios.Fecha(finEnUso);
            }
            else if (finEnUso < hoy)
            {
                largo = "terminó el " + HistorialCambios.DiaYFecha(finEnUso);
                corto = "terminó el " + HistorialCambios.Fecha(finEnUso);
            }
            else if (finEnUso == hoy)
            {
                largo = "sigue hasta hoy, " + HistorialCambios.DiaYFecha(finEnUso);
                corto = "sigue hasta hoy, " + HistorialCambios.Fecha(finEnUso);
            }
            else
            {
                largo = "sigue hasta el " + HistorialCambios.DiaYFecha(finEnUso);
                corto = "sigue hasta el " + HistorialCambios.Fecha(finEnUso);
            }

            return new[] { quien + largo, quien + corto, Mayuscula(largo), Mayuscula(corto), HistorialCambios.Fecha(finEnUso) };
        }

        /// <summary>Lo que dice la barra nueva, del texto mas largo al mas corto.</summary>
        private string[] TextosDelNuevo()
        {
            if (hasta < desde)
            {
                return new[] { "Su último día es antes del primero" };
            }

            string quien = nombreNuevo.Trim().Length == 0 ? "" : "«" + nombreNuevo.Trim() + "»: ";
            string duracion = Duracion((hasta - desde).Days + 1);
            string largo = (empiezaAhora ? "hoy, " + HistorialCambios.DiaYFecha(desde) + ", al " : "del " + HistorialCambios.DiaYFecha(desde) + " al ")
                + HistorialCambios.DiaYFecha(hasta);
            string medio = HistorialCambios.Fecha(desde) + " al " + HistorialCambios.Fecha(hasta);
            return new[]
            {
                quien + largo + " · " + duracion,
                quien + largo,
                quien + medio + " · " + duracion,
                quien + medio,
                medio + " · " + duracion,
                medio
            };
        }

        /// <summary>"12 días", "3 semanas": a partir de dos semanas, en semanas.</summary>
        public static string Duracion(int dias)
        {
            if (dias < 14)
            {
                return dias == 1 ? "1 día" : dias.ToString(Invariante) + " días";
            }

            return ((int)Math.Round(dias / 7.0)).ToString(Invariante) + " semanas";
        }

        private static string Mayuscula(string texto)
        {
            return texto.Length == 0 ? texto : char.ToUpper(texto[0], CultureInfo.GetCultureInfo("es-MX")) + texto.Substring(1);
        }

        private static int Medir(string texto, DrawingFont fuenteDelTexto)
        {
            return TextRenderer.MeasureText(texto, fuenteDelTexto, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        }
    }
}
