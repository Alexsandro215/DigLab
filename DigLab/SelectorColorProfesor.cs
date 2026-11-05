using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// El color de un profesor en el dialogo de alta y edicion: una muestra de
    /// como se veran sus clases, "Automatico" (el que le toca por orden) y los
    /// colores de la paleta, con una marca en la esquina de los que ya usa otro
    /// profesor; "Otro..." abre el selector de Windows y aclara el color si con
    /// texto negro encima no se leeria. Sin db/12 solo ensena la muestra y lo
    /// que falta.
    /// </summary>
    internal class SelectorColorProfesor : Panel
    {
        public const int Alto = 148;
        private const int Lado = 26;

        private readonly int idProfesor;
        private readonly Func<int, string> nombreDe;
        private readonly ToolTip ayuda;
        private readonly Panel muestra;
        private readonly FlowLayoutPanel paleta;
        private readonly Label lblNota;
        private readonly List<Casilla> casillas = new List<Casilla>();
        private readonly DrawingFont fuenteNombre = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
        private readonly DrawingFont fuenteTexto = new DrawingFont("Century Gothic", 9F);
        private Color? elegido;
        private Color? personalizado;
        private string nombreMuestra = "";
        private string codigoMuestra = "";
        private string notaAclarado;

        /// <summary>Cambio el color elegido.</summary>
        public event Action Cambio;

        /// <param name="idProfesor">0 si es nuevo.</param>
        /// <param name="colorGuardado">'#RRGGBB', o vacio si es automatico.</param>
        /// <param name="habilitado">False sin db/12: no se puede elegir.</param>
        /// <param name="nombreDe">El nombre de otro profesor por su id, para decir quien usa ya un color.</param>
        public SelectorColorProfesor(int idProfesor, string colorGuardado, bool habilitado, Func<int, string> nombreDe, ToolTip ayuda, int ancho)
        {
            this.idProfesor = idProfesor;
            this.nombreDe = nombreDe ?? (id => null);
            this.ayuda = ayuda;
            Habilitado = habilitado;
            Size = new Size(ancho, Alto);
            BackColor = Color.White;

            Color guardado;
            if (ColoresProfesor.DesdeTexto(colorGuardado, out guardado))
            {
                elegido = guardado;
                if (ColoresProfesor.Nombre(guardado) == null)
                {
                    personalizado = guardado;
                }
            }

            Controls.Add(EstiloHorarios.CrearEtiqueta("Color en el horario", new Point(0, 0)));

            muestra = new Panel { Location = new Point(0, 24), Size = new Size(206, 56), BorderStyle = BorderStyle.FixedSingle };
            muestra.Paint += PintarMuestra;
            Controls.Add(muestra);

            int x = 220;
            paleta = new FlowLayoutPanel
            {
                Location = new Point(x, 22),
                Size = new Size(ancho - x, 2 * (Lado + 4) + 2),
                Margin = new Padding(0),
                Padding = new Padding(0),
                WrapContents = true,
                Visible = habilitado
            };
            Controls.Add(paleta);

            lblNota = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.FromArgb(90, 90, 90),
                // Tres lineas de 17 px: la mas larga ("... pasara a ... Se aclaro...") cabe entera.
                Location = new Point(habilitado ? x : 220, habilitado ? 88 : 26),
                Size = new Size(ancho - 220, habilitado ? 54 : 54),
                AutoEllipsis = true
            };
            Controls.Add(lblNota);

            if (habilitado)
            {
                AgregarCasilla(null, false);
                foreach (Color color in ColoresProfesor.Muestras)
                {
                    AgregarCasilla(color, false);
                }

                AgregarCasilla(personalizado, true);
            }

            Disposed += (s, e) =>
            {
                fuenteNombre.Dispose();
                fuenteTexto.Dispose();
            };
            Actualizar();
        }

        /// <summary>False sin db/12.</summary>
        public bool Habilitado { get; private set; }

        /// <summary>'#RRGGBB', o null para el automatico.</summary>
        public string ColorElegido
        {
            get { return elegido.HasValue ? ColoresProfesor.ATexto(elegido.Value) : null; }
        }

        /// <summary>El color con el que saldran sus clases: el elegido o el automatico.</summary>
        public Color ColorFinal
        {
            get { return elegido ?? ColoresProfesor.Automatico(idProfesor); }
        }

        /// <summary>Lo que se ve en la muestra: su nombre corto y su codigo, como en la cuadricula.</summary>
        public void Mostrar(string nombre, string codigo)
        {
            nombreMuestra = nombre ?? "";
            codigoMuestra = codigo ?? "";
            muestra.Invalidate();
        }

        private void AgregarCasilla(Color? color, bool otro)
        {
            Casilla casilla = new Casilla(color, otro, Lado) { Margin = new Padding(0, 0, 4, 4) };
            casilla.Click += (s, e) => Elegir(casilla);
            casillas.Add(casilla);
            paleta.Controls.Add(casilla);
        }

        private void Elegir(Casilla casilla)
        {
            if (casilla.EsOtro && personalizado.HasValue && !casilla.Elegida)
            {
                // Ya tiene un color propio y no es el elegido: un clic lo vuelve a
                // elegir. Para cambiarlo, otro clic (abre el selector).
                elegido = personalizado;
            }
            else if (casilla.EsOtro)
            {
                notaAclarado = null;
                using (ColorDialog dialogo = new ColorDialog { FullOpen = true, AnyColor = true, Color = personalizado ?? elegido ?? ColorFinal })
                {
                    if (personalizado.HasValue)
                    {
                        dialogo.CustomColors = new[] { ColorTranslator.ToWin32(personalizado.Value) };
                    }

                    if (dialogo.ShowDialog(FindForm()) != DialogResult.OK)
                    {
                        return;
                    }

                    Color pedido = Color.FromArgb(dialogo.Color.R, dialogo.Color.G, dialogo.Color.B);
                    Color usable = ColoresProfesor.ParaTextoNegro(pedido);
                    if (ColoresProfesor.Nombre(usable) != null)
                    {
                        // Es uno de la paleta: se elige su casilla, sin duplicarlo en "Otro".
                        elegido = usable;
                    }
                    else
                    {
                        if (usable.ToArgb() != pedido.ToArgb())
                        {
                            notaAclarado = "Se aclaró un poco para que el texto negro de sus clases se lea.";
                        }

                        personalizado = usable;
                        casilla.Color = usable;
                        elegido = usable;
                    }
                }
            }
            else
            {
                notaAclarado = null;
                elegido = casilla.Color;
            }

            Actualizar();
            if (Cambio != null)
            {
                Cambio();
            }
        }

        /// <summary>Marca la casilla elegida, las que ya usa otro profesor, y redacta la nota.</summary>
        private void Actualizar()
        {
            foreach (Casilla casilla in casillas)
            {
                casilla.Elegida = casilla.EsOtro
                    ? elegido.HasValue && personalizado.HasValue && elegido.Value.ToArgb() == personalizado.Value.ToArgb()
                    : casilla.Color.HasValue
                        ? elegido.HasValue && elegido.Value.ToArgb() == casilla.Color.Value.ToArgb()
                        : !elegido.HasValue;

                if (!casilla.EsOtro && !casilla.Color.HasValue)
                {
                    casilla.Automatico = ColoresProfesor.Automatico(idProfesor);
                }

                List<int> usan = casilla.Color.HasValue ? ColoresProfesor.QuienesUsan(casilla.Color.Value, idProfesor) : new List<int>();
                casilla.Usado = usan.Count > 0;
                if (ayuda != null)
                {
                    string que = casilla.EsOtro
                        ? (casilla.Color.HasValue ? "Otro color: el tuyo (otro clic para cambiarlo)" : "Otro color...")
                        : casilla.Color.HasValue ? Capital(ColoresProfesor.Nombre(casilla.Color.Value)) : "Automático: el que le toca por orden";
                    List<int> idsEligieron = casilla.Color.HasValue ? ColoresProfesor.QuienesEligieron(casilla.Color.Value, idProfesor) : new List<int>();
                    List<int> loTienen = usan.Where(id => !idsEligieron.Contains(id)).ToList();
                    // "Por su turno" solo si es el de su turno: un desplazado tiene
                    // el que dejo libre quien eligio el suyo.
                    string porQue = loTienen.All(ColoresProfesor.TieneElDeSuTurno) ? " por su turno" : "";
                    string cuando = ColoresProfesor.TieneTurno(idProfesor) ? "" : " al darle su primera clase";
                    string quien = idsEligieron.Count > 0 ? ". Ya lo eligió " + Unir(Nombres(idsEligieron))
                        : loTienen.Count > 0 ? ". Lo tiene " + Unir(Nombres(loTienen)) + porQue + " (si lo eliges, pasará a otro color" + cuando + ")"
                        : "";
                    ayuda.SetToolTip(casilla, que + quien + ".");
                }
            }

            muestra.Invalidate();
            lblNota.ForeColor = Color.FromArgb(90, 90, 90);
            if (!Habilitado)
            {
                lblNota.Text = "Para elegir el color de cada docente falta aplicar db/12_color_profesor.sql en la base. Mientras, sale solo.";
                return;
            }

            // A quien le cambia el color (y a cual): al guardar, o, si todavia no
            // tiene clases, cuando se le de la primera (hasta entonces no mueve a nadie).
            bool tieneTurno = ColoresProfesor.TieneTurno(idProfesor);
            List<string> frases = ColoresProfesor.Simular(idProfesor, elegido)
                .Select(c => new { Nombre = nombreDe(c.Key), Color = ColoresProfesor.Nombre(c.Value) })
                .Where(c => !string.IsNullOrEmpty(c.Nombre))
                .OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => c.Nombre + " pasará a " + (c.Color ?? "otro color"))
                .ToList();
            string cambios = frases.Count == 0 ? ""
                : (tieneTurno ? " Al guardar, " : " Cuando le des su primera clase, ") + Unir(frases) + ".";

            if (!elegido.HasValue)
            {
                lblNota.Text = (tieneTurno
                    ? "Automático: el que le toca por el orden de su primera clase."
                    : "Automático: se le asigna al darle su primera clase (sería este).") + cambios;
                return;
            }

            string nombre = ColoresProfesor.Nombre(elegido.Value);
            List<string> mismoElegido = Nombres(ColoresProfesor.QuienesEligieron(elegido.Value, idProfesor));
            if (mismoElegido.Count > 0)
            {
                // Lo ELIGIO otro: los dos salen igual.
                lblNota.ForeColor = Color.FromArgb(160, 70, 0);
                lblNota.Text = (nombre == null ? "Ese color" : Capital(nombre)) + " ya lo eligió " + Unir(mismoElegido)
                    + ": en el horario no se distinguirán." + cambios + (notaAclarado == null ? "" : " " + notaAclarado);
            }
            else if (cambios.Length > 0)
            {
                lblNota.ForeColor = Color.FromArgb(30, 80, 140);
                lblNota.Text = (nombre == null ? "Color elegido a mano." : Capital(nombre) + ".") + cambios
                    + (notaAclarado == null ? "" : " " + notaAclarado);
            }
            else
            {
                lblNota.Text = (nombre == null ? "Color elegido a mano" : Capital(nombre)) + ": nadie más lo usa."
                    + (notaAclarado == null ? "" : " " + notaAclarado);
            }
        }

        /// <summary>Los nombres de esos profesores, en orden alfabetico (sin los que no se conocen).</summary>
        private List<string> Nombres(IEnumerable<int> ids)
        {
            return ids
                .Select(id => nombreDe(id))
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string Unir(IList<string> nombres)
        {
            if (nombres.Count > 3)
            {
                return string.Join(", ", nombres.Take(3)) + " y " + (nombres.Count - 3) + " más";
            }

            return EstiloAdmin.Enumerar(nombres);
        }

        private static string Capital(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0]) + texto.Substring(1);
        }

        /// <summary>Como una clase en la cuadricula: el nombre en negrita, el codigo a la derecha.</summary>
        private void PintarMuestra(object sender, PaintEventArgs e)
        {
            Rectangle zona = new Rectangle(0, 0, muestra.ClientSize.Width, muestra.ClientSize.Height);
            using (SolidBrush fondo = new SolidBrush(ColorFinal))
            {
                e.Graphics.FillRectangle(fondo, zona);
            }

            using (Pen borde = new Pen(EstiloHorarios.Oscurecer(ColorFinal, 0.35f), 2))
            {
                e.Graphics.DrawLine(borde, 0, 0, zona.Width, 0);
            }

            const TextFormatFlags Linea = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Top;
            Rectangle arriba = new Rectangle(6, 6, zona.Width - 12, 20);
            int anchoCodigo = codigoMuestra.Length == 0 ? 0 : TextRenderer.MeasureText(e.Graphics, codigoMuestra, fuenteNombre).Width;
            TextRenderer.DrawText(e.Graphics, nombreMuestra.Length == 0 ? "Nombre y apellido" : nombreMuestra, fuenteNombre,
                new Rectangle(arriba.X, arriba.Y, arriba.Width - anchoCodigo - 6, arriba.Height), Color.Black, Linea | TextFormatFlags.Left);
            if (anchoCodigo > 0)
            {
                TextRenderer.DrawText(e.Graphics, codigoMuestra, fuenteNombre, arriba, Color.Black, Linea | TextFormatFlags.Right);
            }

            TextRenderer.DrawText(e.Graphics, "Así se verán sus clases", fuenteTexto, new Rectangle(6, 28, zona.Width - 12, 20), Color.Black, Linea | TextFormatFlags.Left);
        }

        /// <summary>Un cuadrito de color: el automatico lleva una "A"; "Otro" un "+" o el color elegido.</summary>
        private class Casilla : Control
        {
            private Color? color;
            private System.Drawing.Color automatico = System.Drawing.Color.FromArgb(245, 245, 245);
            private bool elegida;
            private bool usado;

            public Casilla(Color? color, bool esOtro, int lado)
            {
                this.color = color;
                EsOtro = esOtro;
                Size = new Size(lado, lado);
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                TabStop = false;
            }

            public bool EsOtro { get; private set; }

            /// <summary>Null: el automatico, o "Otro" sin elegir todavia.</summary>
            public Color? Color
            {
                get { return color; }
                set { color = value; Invalidate(); }
            }

            /// <summary>En la casilla "Automatico": el color que le tocaria.</summary>
            public System.Drawing.Color Automatico
            {
                get { return automatico; }
                set { automatico = value; Invalidate(); }
            }

            public bool Elegida
            {
                get { return elegida; }
                set { elegida = value; Invalidate(); }
            }

            public bool Usado
            {
                get { return usado; }
                set { usado = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Rectangle zona = new Rectangle(2, 2, Width - 5, Height - 5);
                g.Clear(System.Drawing.Color.White);

                System.Drawing.Color relleno = color ?? (EsOtro ? System.Drawing.Color.White : automatico);
                using (SolidBrush pincel = new SolidBrush(relleno))
                {
                    g.FillRectangle(pincel, zona);
                }

                using (Pen borde = new Pen(elegida ? System.Drawing.Color.Black : System.Drawing.Color.FromArgb(150, 150, 150), elegida ? 2.5f : 1f))
                {
                    g.DrawRectangle(borde, zona);
                }

                // "A" en el automatico; "+" en "Otro" mientras no tiene color.
                string letra = color.HasValue ? null : EsOtro ? "+" : "A";
                if (letra != null)
                {
                    using (DrawingFont fuente = new DrawingFont("Century Gothic", 9F, FontStyle.Bold))
                    {
                        TextRenderer.DrawText(g, letra, fuente, zona, System.Drawing.Color.FromArgb(60, 60, 60),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }
                }

                if (usado)
                {
                    // Ya lo usa otro profesor: un triangulo oscuro en la esquina.
                    Point[] esquina = { new Point(zona.Right - 9, zona.Top), new Point(zona.Right, zona.Top), new Point(zona.Right, zona.Top + 9) };
                    using (SolidBrush marca = new SolidBrush(System.Drawing.Color.FromArgb(70, 70, 70)))
                    {
                        g.FillPolygon(marca, esquina);
                    }
                }

                if (elegida && color.HasValue)
                {
                    using (Pen palomita = new Pen(System.Drawing.Color.Black, 2f))
                    {
                        int cx = zona.Left + zona.Width / 2;
                        int cy = zona.Top + zona.Height / 2;
                        g.DrawLines(palomita, new[] { new Point(cx - 5, cy), new Point(cx - 1, cy + 4), new Point(cx + 6, cy - 4) });
                    }
                }
            }
        }
    }
}
