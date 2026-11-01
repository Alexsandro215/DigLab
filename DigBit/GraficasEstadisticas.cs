using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    // =========================================================================
    // Graficas de Estadisticas (pestana Graficas y reporte para auditoria,
    // 2026-10-31). Cada grafica se dibuja UNA vez con GDI+ (Grafica.Dibujar): en
    // pantalla en su control (LienzoGrafica) y en el PDF como imagen al triple de
    // puntos (Grafica.ComoImagen), con la letra del PDF. Sin dependencias nuevas.
    //
    // Reglas (metodo dataviz): la forma sale de la pregunta; barras de 24 px como
    // mucho, con el extremo de datos redondeado (4 px) y la base recta; 2 px de
    // fondo entre segmentos que se tocan; rejilla y ejes en lineas finas y
    // suaves; leyenda solo con dos series o mas; etiquetas directas con medida
    // (si no caben, no se ponen: la tabla de cada seccion tiene la cifra); el
    // texto nunca va del color de la serie. Ver ColoresGrafica.
    // =========================================================================

    /// <summary>
    /// La paleta de las graficas, con los colores del logo del TESCHI #007040,
    /// #78B838 y #C80838 (su gris #C8C0B8, a 1.8:1 sobre blanco, no sirve para
    /// marcas que llevan datos) y validada con validate_palette.js de dataviz
    /// contra el fondo blanco (2026-10-31): estados verde, ambar y rojo pasan la
    /// separacion para daltonismo (ambar a 2.3:1, con leyenda y tabla); la pista
    /// verde claro y el verde, como rampa ordinal (2.4:1 el extremo claro); los
    /// tres tipos de falla (azul, verde azulado y violeta), en ese orden, pasan
    /// todas las pruebas de vecinos con 3:1 o mas.
    /// </summary>
    internal static class ColoresGrafica
    {
        public static readonly Color Fondo = Color.White;
        /// <summary>Texto principal: valores y titulos de las graficas.</summary>
        public static readonly Color Tinta = Color.FromArgb(40, 44, 41);
        /// <summary>Texto secundario (subtitulos, etiquetas de ejes, notas): el de la fase de UX.</summary>
        public static readonly Color TintaSecundaria = EstiloAdmin.TextoSecundario;
        public static readonly Color Rejilla = EstiloHorarios.LineaSuave;
        public static readonly Color Base = Color.FromArgb(190, 197, 192);
        /// <summary>Verde del TESCHI: lo que si paso (horas usadas, clases que se dieron, asistencia).</summary>
        public static readonly Color Verde = EstiloHorarios.VerdeTeschi;
        /// <summary>Verde claro del TESCHI: lo que decia el horario (la pista de lo que si paso).</summary>
        public static readonly Color VerdeClaro = Color.FromArgb(120, 184, 56);
        /// <summary>Se dio sin bitacoras, con asistencias que dio su docente (ambar en Faltas).</summary>
        public static readonly Color Ambar = Color.FromArgb(224, 161, 0);
        /// <summary>Rojo del TESCHI: sin registro y asistencia bajo el 80 %.</summary>
        public static readonly Color Rojo = Color.FromArgb(200, 8, 56);
        /// <summary>Pista de las barras de 0 a 100 %: sin dato, solo el sitio.</summary>
        public static readonly Color Pista = Color.FromArgb(236, 240, 237);
        public static readonly Color Hardware = Color.FromArgb(42, 120, 214);
        public static readonly Color Software = Color.FromArgb(18, 150, 138);
        public static readonly Color Red = Color.FromArgb(156, 79, 176);
        /// <summary>Reportes de un equipo: el naranja de las fallas de las pestanas (NaranjaTexto).</summary>
        public static readonly Color Fallas = Color.FromArgb(190, 85, 0);
    }

    /// <summary>Las letras de una grafica: en pantalla, las de DigBit; en el PDF, Arial (va con la Helvetica del texto).</summary>
    internal sealed class EstiloGrafica : IDisposable
    {
        public readonly DrawingFont Titulo;
        public readonly DrawingFont Texto;
        public readonly DrawingFont Pequena;
        public readonly DrawingFont Valor;
        /// <summary>Alto de una fila de barras sola (px) y grueso de su barra.</summary>
        public readonly float PasoFila;
        public readonly float GruesoBarra;

        private EstiloGrafica(string familia, float titulo, float texto, float pequena, float pasoFila, float gruesoBarra)
        {
            Titulo = new DrawingFont(familia, titulo, FontStyle.Bold);
            Texto = new DrawingFont(familia, texto);
            Pequena = new DrawingFont(familia, pequena);
            Valor = new DrawingFont(familia, texto, FontStyle.Bold);
            PasoFila = pasoFila;
            GruesoBarra = gruesoBarra;
        }

        public static EstiloGrafica Pantalla()
        {
            return new EstiloGrafica("Century Gothic", 10.5F, 9F, 8.5F, 27F, 14F);
        }

        public static EstiloGrafica Pdf()
        {
            return new EstiloGrafica("Arial", 9.5F, 8F, 7.5F, 22F, 11F);
        }

        public void Dispose()
        {
            Titulo.Dispose();
            Texto.Dispose();
            Pequena.Dispose();
            Valor.Dispose();
        }
    }

    /// <summary>Una muestra de la leyenda: un cuadrito (barras) o una raya (lineas) y su texto.</summary>
    internal sealed class MuestraLeyenda
    {
        public Color Color;
        public string Texto = "";
        public bool EsLinea;
        /// <summary>Una raya discontinua (la marca del 80 %).</summary>
        public bool Discontinua;
    }

    /// <summary>Un tramo de una barra apilada: su valor y su color.</summary>
    internal struct Segmento
    {
        public double Valor;
        public Color Color;

        public Segmento(double valor, Color color)
        {
            Valor = valor;
            Color = color;
        }
    }

    /// <summary>Lo comun: medir y dibujar texto, barras redondeadas y la cabecera (titulo, subtitulo y leyenda).</summary>
    internal abstract class Grafica
    {
        private static readonly object CandadoMedida = new object();
        private static Bitmap lienzoDeMedida;
        private static Graphics medida;

        public string Titulo = "";
        /// <summary>Lo que se ve y en que unidades ("Horas, en el periodo").</summary>
        public string Subtitulo = "";
        /// <summary>Con dos series o mas; vacia con una (el titulo ya la nombra).</summary>
        public List<MuestraLeyenda> Leyenda = new List<MuestraLeyenda>();

        /// <summary>Las cifras en texto, para el lector de pantalla (AccessibleDescription).</summary>
        public abstract string EnTexto();

        /// <summary>El alto que pide a ese ancho (px logicos).</summary>
        public float Alto(EstiloGrafica e, float ancho)
        {
            lock (CandadoMedida)
            {
                Graphics g = Medida();
                return AltoCabecera(g, e, ancho) + AltoCuerpo(g, e, ancho);
            }
        }

        /// <summary>La grafica entera en ese rectangulo (px logicos): fondo, cabecera y cuerpo.</summary>
        public void Dibujar(Graphics g, EstiloGrafica e, RectangleF area)
        {
            using (SolidBrush fondo = new SolidBrush(ColoresGrafica.Fondo))
            {
                g.FillRectangle(fondo, area);
            }

            SmoothingMode suavizado = g.SmoothingMode;
            PixelOffsetMode desplazamiento = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            try
            {
                float y = DibujarCabecera(g, e, area);
                DibujarCuerpo(g, e, new RectangleF(area.X, y, area.Width, Math.Max(10F, area.Bottom - y)));
            }
            finally
            {
                g.SmoothingMode = suavizado;
                g.PixelOffsetMode = desplazamiento;
            }
        }

        /// <summary>
        /// La grafica en un mapa de bits de ancho logico x (escala) puntos: en el PDF
        /// se pone al ancho de la hoja (ancho logico = puntos del PDF x 4/3) y sale
        /// nitida al imprimir (con escala 3, unos 290 ppp).
        /// </summary>
        public Bitmap ComoImagen(EstiloGrafica e, float anchoLogico, float escala)
        {
            float alto = Alto(e, anchoLogico);
            Bitmap imagen = new Bitmap((int)Math.Ceiling(anchoLogico * escala), (int)Math.Ceiling(alto * escala), PixelFormat.Format24bppRgb);
            try
            {
                imagen.SetResolution(96F, 96F);
                using (Graphics g = Graphics.FromImage(imagen))
                {
                    g.Clear(ColoresGrafica.Fondo);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.ScaleTransform(escala, escala);
                    Dibujar(g, e, new RectangleF(0F, 0F, anchoLogico, alto));
                }

                return imagen;
            }
            catch
            {
                imagen.Dispose();
                throw;
            }
        }

        protected abstract float AltoCuerpo(Graphics g, EstiloGrafica e, float ancho);

        protected abstract void DibujarCuerpo(Graphics g, EstiloGrafica e, RectangleF area);

        // ----- Cabecera -----

        private float AltoCabecera(Graphics g, EstiloGrafica e, float ancho)
        {
            float alto = e.Titulo.GetHeight(g) + 2F;
            if (Subtitulo.Length > 0)
            {
                alto += MedirEnRenglones(g, Subtitulo, e.Pequena, ancho).Height + 2F;
            }

            if (Leyenda.Count > 0)
            {
                alto += AltoLeyenda(g, e, ancho) + 2F;
            }

            return alto + 10F;
        }

        private float DibujarCabecera(Graphics g, EstiloGrafica e, RectangleF area)
        {
            float y = area.Y;
            Texto(g, Recortar(g, Titulo, e.Titulo, area.Width), e.Titulo, ColoresGrafica.Tinta, area.X, y);
            y += e.Titulo.GetHeight(g) + 2F;
            if (Subtitulo.Length > 0)
            {
                SizeF tam = MedirEnRenglones(g, Subtitulo, e.Pequena, area.Width);
                using (SolidBrush brocha = new SolidBrush(ColoresGrafica.TintaSecundaria))
                using (StringFormat formato = FormatoEnRenglones())
                {
                    g.DrawString(Subtitulo, e.Pequena, brocha, new RectangleF(area.X, y, area.Width, tam.Height + 2F), formato);
                }

                y += tam.Height + 2F;
            }

            if (Leyenda.Count > 0)
            {
                y += DibujarLeyenda(g, e, area.X, y, area.Width) + 2F;
            }

            return y + 10F;
        }

        private float AltoLeyenda(Graphics g, EstiloGrafica e, float ancho)
        {
            return Leyenda.Count == 0 ? 0F : RenglonesDeLeyenda(g, e, ancho) * (e.Pequena.GetHeight(g) + 4F);
        }

        private int RenglonesDeLeyenda(Graphics g, EstiloGrafica e, float ancho)
        {
            int renglones = 1;
            float x = 0F;
            foreach (MuestraLeyenda m in Leyenda)
            {
                float pide = AnchoMuestra(g, e, m);
                if (x > 0F && x + pide > ancho)
                {
                    renglones++;
                    x = 0F;
                }

                x += pide;
            }

            return renglones;
        }

        private static float AnchoMuestra(Graphics g, EstiloGrafica e, MuestraLeyenda m)
        {
            return (m.EsLinea ? 18F : 10F) + 5F + Medir(g, m.Texto, e.Pequena).Width + 16F;
        }

        private float DibujarLeyenda(Graphics g, EstiloGrafica e, float x0, float y0, float ancho)
        {
            float alto = e.Pequena.GetHeight(g);
            float x = x0;
            float y = y0;
            foreach (MuestraLeyenda m in Leyenda)
            {
                float pide = AnchoMuestra(g, e, m);
                if (x > x0 && x + pide > x0 + ancho)
                {
                    x = x0;
                    y += alto + 4F;
                }

                float centro = y + alto / 2F;
                if (m.EsLinea)
                {
                    using (Pen lapiz = new Pen(m.Color, 2F))
                    {
                        if (m.Discontinua)
                        {
                            lapiz.DashPattern = new[] { 3F, 2F };
                        }

                        g.DrawLine(lapiz, x, centro, x + 18F, centro);
                    }

                    x += 18F + 5F;
                }
                else
                {
                    using (GraphicsPath forma = Redondeado(new RectangleF(x, centro - 5F, 10F, 10F), 2F, true, true, true, true))
                    using (SolidBrush brocha = new SolidBrush(m.Color))
                    {
                        g.FillPath(brocha, forma);
                    }

                    x += 10F + 5F;
                }

                Texto(g, m.Texto, e.Pequena, ColoresGrafica.TintaSecundaria, x, y);
                x += Medir(g, m.Texto, e.Pequena).Width + 16F;
            }

            return y + alto - y0;
        }

        // ----- Ayudantes de dibujo -----

        /// <summary>Para medir sin ventana (el alto de cada grafica antes de pintarla).</summary>
        private static Graphics Medida()
        {
            if (medida == null)
            {
                lienzoDeMedida = new Bitmap(1, 1);
                lienzoDeMedida.SetResolution(96F, 96F);
                medida = Graphics.FromImage(lienzoDeMedida);
                medida.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            }

            return medida;
        }

        protected static StringFormat FormatoDeUnRenglon()
        {
            StringFormat formato = (StringFormat)StringFormat.GenericTypographic.Clone();
            formato.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            formato.Trimming = StringTrimming.None;
            return formato;
        }

        protected static StringFormat FormatoEnRenglones()
        {
            StringFormat formato = (StringFormat)StringFormat.GenericTypographic.Clone();
            formato.Trimming = StringTrimming.Word;
            return formato;
        }

        internal static SizeF Medir(Graphics g, string texto, DrawingFont fuente)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return new SizeF(0F, fuente.GetHeight(g));
            }

            using (StringFormat formato = FormatoDeUnRenglon())
            {
                return g.MeasureString(texto, fuente, new PointF(0F, 0F), formato);
            }
        }

        protected static SizeF MedirEnRenglones(Graphics g, string texto, DrawingFont fuente, float ancho)
        {
            using (StringFormat formato = FormatoEnRenglones())
            {
                SizeF tam = g.MeasureString(texto, fuente, new SizeF(Math.Max(20F, ancho), 10000F), formato);
                return new SizeF(tam.Width, Math.Max(tam.Height, fuente.GetHeight(g)));
            }
        }

        /// <summary>Texto en (x, y) arriba a la izquierda; con derecha, (x, y) es su esquina derecha.</summary>
        internal static void Texto(Graphics g, string texto, DrawingFont fuente, Color color, float x, float y, bool derecha = false, bool centrado = false)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return;
            }

            if (derecha || centrado)
            {
                float ancho = Medir(g, texto, fuente).Width;
                x -= derecha ? ancho : ancho / 2F;
            }

            using (SolidBrush brocha = new SolidBrush(color))
            using (StringFormat formato = FormatoDeUnRenglon())
            {
                g.DrawString(texto, fuente, brocha, x, y, formato);
            }
        }

        /// <summary>El texto entero si cabe; si no, cortado con "..." (entero en la tabla y en la ayuda).</summary>
        internal static string Recortar(Graphics g, string texto, DrawingFont fuente, float ancho)
        {
            texto = texto ?? "";
            if (Medir(g, texto, fuente).Width <= ancho)
            {
                return texto;
            }

            int bajo = 0;
            int alto = texto.Length;
            while (bajo < alto)
            {
                int medio = (bajo + alto + 1) / 2;
                if (Medir(g, texto.Substring(0, medio).TrimEnd() + "…", fuente).Width <= ancho)
                {
                    bajo = medio;
                }
                else
                {
                    alto = medio - 1;
                }
            }

            return bajo == 0 ? "" : texto.Substring(0, bajo).TrimEnd() + "…";
        }

        /// <summary>Un rectangulo con esas esquinas redondeadas (el radio no pasa de la mitad del lado corto).</summary>
        internal static GraphicsPath Redondeado(RectangleF r, float radio, bool arribaIzquierda, bool arribaDerecha, bool abajoDerecha, bool abajoIzquierda)
        {
            GraphicsPath forma = new GraphicsPath();
            radio = Math.Max(0F, Math.Min(radio, Math.Min(r.Width, r.Height) / 2F));
            float d = radio * 2F;
            if (radio <= 0.01F)
            {
                forma.AddRectangle(r);
                return forma;
            }

            if (arribaIzquierda)
            {
                forma.AddArc(r.X, r.Y, d, d, 180F, 90F);
            }
            else
            {
                forma.AddLine(r.X, r.Y, r.X, r.Y);
            }

            if (arribaDerecha)
            {
                forma.AddArc(r.Right - d, r.Y, d, d, 270F, 90F);
            }
            else
            {
                forma.AddLine(r.Right, r.Y, r.Right, r.Y);
            }

            if (abajoDerecha)
            {
                forma.AddArc(r.Right - d, r.Bottom - d, d, d, 0F, 90F);
            }
            else
            {
                forma.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            }

            if (abajoIzquierda)
            {
                forma.AddArc(r.X, r.Bottom - d, d, d, 90F, 90F);
            }
            else
            {
                forma.AddLine(r.X, r.Bottom, r.X, r.Bottom);
            }

            forma.CloseFigure();
            return forma;
        }

        /// <summary>Una barra horizontal que crece desde la izquierda: recta en la base, redondeada (4 px) en su extremo si redondear.</summary>
        protected static void BarraH(Graphics g, Color color, RectangleF r, bool redondear)
        {
            if (r.Width <= 0.2F || r.Height <= 0.2F)
            {
                return;
            }

            using (GraphicsPath forma = Redondeado(r, redondear ? 4F : 0F, false, redondear, redondear, false))
            using (SolidBrush brocha = new SolidBrush(color))
            {
                g.FillPath(brocha, forma);
            }
        }

        /// <summary>Una columna que crece desde abajo: recta en la base, redondeada arriba si redondear.</summary>
        protected static void Columna(Graphics g, Color color, RectangleF r, bool redondear)
        {
            if (r.Width <= 0.2F || r.Height <= 0.2F)
            {
                return;
            }

            using (GraphicsPath forma = Redondeado(r, redondear ? 4F : 0F, redondear, redondear, false, false))
            using (SolidBrush brocha = new SolidBrush(color))
            {
                g.FillPath(brocha, forma);
            }
        }

        protected static void Linea(Graphics g, Color color, float grosor, float x1, float y1, float x2, float y2)
        {
            using (Pen lapiz = new Pen(color, grosor))
            {
                g.DrawLine(lapiz, x1, y1, x2, y2);
            }
        }

        /// <summary>El paso "bonito" del eje (1, 2, 2.5 o 5 por una potencia de 10) para unas 4 lineas hasta maximo.</summary>
        protected static double PasoDelEje(double maximo, int lineas)
        {
            if (maximo <= 0)
            {
                return 1;
            }

            double bruto = maximo / Math.Max(1, lineas);
            double potencia = Math.Pow(10, Math.Floor(Math.Log10(bruto)));
            foreach (double m in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
            {
                if (m * potencia >= bruto)
                {
                    return m * potencia;
                }
            }

            return 10 * potencia;
        }

        /// <summary>"1,284" o "2.5" (con un decimal si lo tiene), sin ceros de mas.</summary>
        internal static string Numero(double valor)
        {
            return valor.ToString(Math.Abs(valor - Math.Round(valor)) < 0.05 ? "#,0" : "#,0.#", CultureInfo.InvariantCulture);
        }
    }

    // =========================================================================
    // Barras horizontales: por laboratorio, por docente, por grupo, por equipo
    // =========================================================================

    /// <summary>Una barra de una fila: una pista detras (lo que decia el horario) y sus segmentos apilados desde cero.</summary>
    internal sealed class BarraDeFila
    {
        public double? Pista;
        public List<Segmento> Segmentos = new List<Segmento>();

        public double Largo
        {
            get { return Math.Max(Pista ?? 0, Segmentos.Sum(s => Math.Max(0, s.Valor))); }
        }
    }

    /// <summary>Una fila: su nombre a la izquierda, una barra (o varias, agrupadas) y su cifra al final.</summary>
    internal sealed class FilaDeBarras
    {
        public string Etiqueta = "";
        public List<BarraDeFila> Barras = new List<BarraDeFila>();
        /// <summary>Lo que va despues de la barra mas larga ("96 de 120 h · 80 %").</summary>
        public string Valor = "";
    }

    /// <summary>
    /// Barras horizontales (comparar magnitudes entre cosas con nombre): una por fila,
    /// apiladas, con pista o agrupadas (tres por fila). Cada fila lleva su cifra al
    /// final, asi que no hay eje: la escala se lee en las cifras y en la tabla. Con
    /// Maximo fijo (100 en porcentajes) las pistas de 0 a 100 % van en gris claro.
    /// </summary>
    internal sealed class BarrasHorizontales : Grafica
    {
        public List<FilaDeBarras> Filas = new List<FilaDeBarras>();
        /// <summary>El final de la escala; 0 = la barra mas larga.</summary>
        public double Maximo;
        /// <summary>Una linea de referencia a lo alto de todas las filas (el 80 %), o null.</summary>
        public double? Marca;
        public string TextoMarca = "";
        /// <summary>Barras de 0 a Maximo: cada una con su pista gris (sin datos) detras.</summary>
        public bool ConPistaCompleta;
        // En las partes de EnPartes: todas las filas, para que nombres y cifras midan lo mismo en cada parte.
        private List<FilaDeBarras> filasParaMedir;

        public override string EnTexto()
        {
            return Titulo + ". " + string.Join("; ", Filas.Select(f => f.Etiqueta + ": " + f.Valor)) + ".";
        }

        /// <summary>
        /// Esta grafica en partes que quepan en ese alto (el PDF: una hoja), con la misma
        /// escala en todas; las de despues dicen "(continua)" y no repiten la leyenda.
        /// </summary>
        public List<BarrasHorizontales> EnPartes(EstiloGrafica e, float ancho, float altoMaximo)
        {
            List<BarrasHorizontales> partes = new List<BarrasHorizontales>();
            if (Alto(e, ancho) <= altoMaximo || Filas.Count < 2)
            {
                partes.Add(this);
                return partes;
            }

            double maximo = Maximo > 0 ? Maximo : Math.Max(1e-9, Filas.SelectMany(f => f.Barras).Select(b => b.Largo).DefaultIfEmpty(0).Max());
            BarrasHorizontales actual = null;
            foreach (FilaDeBarras fila in Filas)
            {
                if (actual != null)
                {
                    actual.Filas.Add(fila);
                    if (actual.Alto(e, ancho) <= altoMaximo)
                    {
                        continue;
                    }

                    actual.Filas.RemoveAt(actual.Filas.Count - 1);
                }

                actual = new BarrasHorizontales
                {
                    Titulo = partes.Count == 0 ? Titulo : Titulo + " (continúa)",
                    Subtitulo = partes.Count == 0 ? Subtitulo : "",
                    Leyenda = partes.Count == 0 ? Leyenda : new List<MuestraLeyenda>(),
                    Maximo = maximo,
                    Marca = Marca,
                    TextoMarca = TextoMarca,
                    ConPistaCompleta = ConPistaCompleta,
                    filasParaMedir = Filas
                };
                actual.Filas.Add(fila);
                partes.Add(actual);
            }

            return partes;
        }

        private float PasoDeFila(EstiloGrafica e, FilaDeBarras fila)
        {
            int barras = Math.Max(1, fila.Barras.Count);
            if (barras == 1)
            {
                return e.PasoFila;
            }

            // Agrupadas: barras mas finas, con 2 px de fondo entre ellas y aire entre filas.
            float grueso = Math.Max(6F, e.GruesoBarra * 0.62F);
            return barras * grueso + (barras - 1) * 2F + e.PasoFila - e.GruesoBarra + 4F;
        }

        private float GruesoDe(EstiloGrafica e, FilaDeBarras fila)
        {
            return fila.Barras.Count > 1 ? Math.Max(6F, e.GruesoBarra * 0.62F) : e.GruesoBarra;
        }

        protected override float AltoCuerpo(Graphics g, EstiloGrafica e, float ancho)
        {
            float alto = Filas.Sum(f => PasoDeFila(e, f));
            if (Marca.HasValue)
            {
                alto += e.Pequena.GetHeight(g) + 4F;
            }

            return alto + 6F;
        }

        protected override void DibujarCuerpo(Graphics g, EstiloGrafica e, RectangleF area)
        {
            if (Filas.Count == 0)
            {
                return;
            }

            // Columna de nombres (hasta el 38 % del ancho) y la de cifras (lo que pide la mas larga).
            List<FilaDeBarras> medir = filasParaMedir ?? Filas;
            float anchoNombres = Math.Min(area.Width * 0.38F, medir.Max(f => Medir(g, f.Etiqueta, e.Texto).Width) + 12F);
            float anchoValores = Math.Min(area.Width * 0.3F, medir.Max(f => Medir(g, f.Valor, e.Valor).Width) + 16F);
            float x0 = area.X + anchoNombres;
            float largo = Math.Max(20F, area.Width - anchoNombres - anchoValores);
            double maximo = Maximo > 0 ? Maximo : Math.Max(1e-9, Filas.SelectMany(f => f.Barras).Select(b => b.Largo).DefaultIfEmpty(0).Max());
            float y = area.Y;
            float arribaMarca = y;
            if (Marca.HasValue)
            {
                // El rotulo de la marca encima de la primera fila, centrado en ella.
                float xMarca = x0 + (float)(largo * Math.Min(1.0, Marca.Value / maximo));
                Texto(g, TextoMarca, e.Pequena, ColoresGrafica.TintaSecundaria, xMarca, y, false, true);
                y += e.Pequena.GetHeight(g) + 4F;
                arribaMarca = y - 2F;
            }

            float alturaTexto = e.Texto.GetHeight(g);
            foreach (FilaDeBarras fila in Filas)
            {
                float paso = PasoDeFila(e, fila);
                float grueso = GruesoDe(e, fila);
                int barras = Math.Max(1, fila.Barras.Count);
                float altoBarras = barras * grueso + (barras - 1) * 2F;
                float yBarras = y + (paso - altoBarras) / 2F;
                Texto(g, Recortar(g, fila.Etiqueta, e.Texto, anchoNombres - 10F), e.Texto, ColoresGrafica.Tinta, area.X, y + (paso - alturaTexto) / 2F);

                float finMasLargo = x0;
                for (int i = 0; i < fila.Barras.Count; i++)
                {
                    BarraDeFila barra = fila.Barras[i];
                    float yb = yBarras + i * (grueso + 2F);
                    if (ConPistaCompleta)
                    {
                        BarraH(g, ColoresGrafica.Pista, new RectangleF(x0, yb, largo, grueso), true);
                    }

                    if (barra.Pista.HasValue && barra.Pista.Value > 0)
                    {
                        float anchoPista = Math.Max(2F, (float)(largo * Math.Min(1.0, barra.Pista.Value / maximo)));
                        BarraH(g, ColoresGrafica.VerdeClaro, new RectangleF(x0, yb, anchoPista, grueso), true);
                        finMasLargo = Math.Max(finMasLargo, x0 + anchoPista);
                    }

                    float x = x0;
                    List<Segmento> conValor = barra.Segmentos.Where(s => s.Valor > 0).ToList();
                    for (int s = 0; s < conValor.Count; s++)
                    {
                        float ancho = (float)(largo * Math.Min(1.0, conValor[s].Valor / maximo));
                        // Algo mas que cero siempre se ve.
                        ancho = Math.Max(ancho, 2F);
                        bool ultimo = s == conValor.Count - 1;
                        BarraH(g, conValor[s].Color, new RectangleF(x, yb, ancho, grueso), ultimo);
                        x += ancho;
                        if (!ultimo)
                        {
                            // 2 px de fondo entre segmentos que se tocan.
                            Linea(g, ColoresGrafica.Fondo, 2F, x, yb - 0.5F, x, yb + grueso + 0.5F);
                        }
                    }

                    finMasLargo = Math.Max(finMasLargo, x);
                }

                if (ConPistaCompleta)
                {
                    finMasLargo = x0 + largo;
                }

                Texto(g, fila.Valor, e.Valor, ColoresGrafica.Tinta, finMasLargo + 6F, y + (paso - alturaTexto) / 2F);
                y += paso;
            }

            if (Marca.HasValue)
            {
                float xMarca = x0 + (float)(largo * Math.Min(1.0, Marca.Value / maximo));
                using (Pen lapiz = new Pen(ColoresGrafica.Tinta, 1F) { DashPattern = new[] { 3F, 2F } })
                {
                    g.DrawLine(lapiz, xMarca, arribaMarca, xMarca, y);
                }
            }
        }
    }

    // =========================================================================
    // Columnas por semana o por mes
    // =========================================================================

    /// <summary>Una columna de un tramo: la pista (lo del horario) y sus segmentos apilados desde abajo.</summary>
    internal sealed class ColumnaDeTramo
    {
        public string Etiqueta = "";
        public double? Pista;
        public List<Segmento> Segmentos = new List<Segmento>();

        public double Alto
        {
            get { return Math.Max(Pista ?? 0, Segmentos.Sum(s => Math.Max(0, s.Valor))); }
        }
    }

    /// <summary>
    /// Columnas en el tiempo (semanas o meses): con pista (horas del horario y, encima,
    /// las usadas) o apiladas (clases que se dieron y sin registro). Eje con cero y
    /// lineas finas; los nombres de los tramos se saltan si no caben.
    /// </summary>
    internal sealed class ColumnasPorTramo : Grafica
    {
        public List<ColumnaDeTramo> Columnas = new List<ColumnaDeTramo>();
        /// <summary>" h" o "" (lo que va detras de cada cifra del eje).</summary>
        public string Unidad = "";
        public float AltoDibujo = 170F;

        public override string EnTexto()
        {
            return Titulo + ". " + string.Join("; ", Columnas.Select(c => c.Etiqueta + ": "
                + (c.Pista.HasValue ? Numero(c.Segmentos.Sum(s => s.Valor)) + " de " + Numero(c.Pista.Value) : string.Join(", ", c.Segmentos.Select(s => Numero(s.Valor))))
                + Unidad)) + ".";
        }

        protected override float AltoCuerpo(Graphics g, EstiloGrafica e, float ancho)
        {
            return AltoDibujo + e.Pequena.GetHeight(g) + 10F;
        }

        protected override void DibujarCuerpo(Graphics g, EstiloGrafica e, RectangleF area)
        {
            if (Columnas.Count == 0)
            {
                return;
            }

            double maximo = Math.Max(1e-9, Columnas.Max(c => c.Alto));
            double paso = PasoDelEje(maximo, 5);
            double tope = Math.Max(paso, Math.Ceiling(maximo / paso - 1e-9) * paso);
            float altoTexto = e.Pequena.GetHeight(g);
            float anchoEje = 0F;
            for (double v = 0; v <= tope + 1e-9; v += paso)
            {
                anchoEje = Math.Max(anchoEje, Medir(g, Numero(v) + Unidad, e.Pequena).Width);
            }

            float x0 = area.X + anchoEje + 8F;
            float arriba = area.Y + altoTexto / 2F;
            float abajo = area.Y + AltoDibujo;
            float ancho = Math.Max(20F, area.Right - x0 - 4F);

            // Rejilla y cifras del eje (con cero).
            for (double v = 0; v <= tope + 1e-9; v += paso)
            {
                float yv = abajo - (float)((abajo - arriba) * v / tope);
                Linea(g, v == 0 ? ColoresGrafica.Base : ColoresGrafica.Rejilla, 1F, x0, yv, x0 + ancho, yv);
                Texto(g, Numero(v) + Unidad, e.Pequena, ColoresGrafica.TintaSecundaria, x0 - 6F, yv - altoTexto / 2F, true);
            }

            float hueco = ancho / Columnas.Count;
            float grueso = Math.Max(4F, Math.Min(24F, hueco * 0.62F));
            float anchoEtiqueta = Columnas.Max(c => Medir(g, c.Etiqueta, e.Pequena).Width);
            int saltar = Math.Max(1, (int)Math.Ceiling((anchoEtiqueta + 8F) / hueco));
            for (int i = 0; i < Columnas.Count; i++)
            {
                ColumnaDeTramo c = Columnas[i];
                float centro = x0 + hueco * (i + 0.5F);
                float x = centro - grueso / 2F;
                if (c.Pista.HasValue && c.Pista.Value > 0)
                {
                    float alto = (float)((abajo - arriba) * c.Pista.Value / tope);
                    Columna(g, ColoresGrafica.VerdeClaro, new RectangleF(x, abajo - alto, grueso, alto), true);
                }

                float y = abajo;
                List<Segmento> conValor = c.Segmentos.Where(s => s.Valor > 0).ToList();
                for (int s = 0; s < conValor.Count; s++)
                {
                    float alto = Math.Max(2F, (float)((abajo - arriba) * conValor[s].Valor / tope));
                    bool ultimo = s == conValor.Count - 1;
                    Columna(g, conValor[s].Color, new RectangleF(x, y - alto, grueso, alto), ultimo);
                    y -= alto;
                    if (!ultimo)
                    {
                        Linea(g, ColoresGrafica.Fondo, 2F, x - 0.5F, y, x + grueso + 0.5F, y);
                    }
                }

                // Las etiquetas que caben (la ultima siempre, si no choca con la anterior).
                bool ultima = i == Columnas.Count - 1;
                if (i % saltar == 0 || (ultima && (i % saltar) * hueco >= anchoEtiqueta + 8F))
                {
                    Texto(g, c.Etiqueta, e.Pequena, ColoresGrafica.TintaSecundaria, centro, abajo + 4F, false, true);
                }
            }
        }
    }

    // =========================================================================
    // Linea por semana o por mes (la asistencia)
    // =========================================================================

    /// <summary>Un punto de la linea (null: ese tramo no tiene nada que contar, la linea se corta).</summary>
    internal sealed class PuntoDeTramo
    {
        public string Etiqueta = "";
        public double? Valor;
    }

    /// <summary>
    /// Una linea de 0 a 100 % en el tiempo, con la referencia del 80 % (discontinua:
    /// es un umbral) y la cifra del ultimo punto y la del mas bajo.
    /// </summary>
    internal sealed class LineaPorTramo : Grafica
    {
        public List<PuntoDeTramo> Puntos = new List<PuntoDeTramo>();
        public double? Marca;
        public string TextoMarca = "";
        public float AltoDibujo = 160F;

        public override string EnTexto()
        {
            return Titulo + ". " + string.Join("; ", Puntos.Select(p => p.Etiqueta + ": " + (p.Valor.HasValue ? Porciento(p.Valor.Value) : "sin días que contar"))) + ".";
        }

        private static string Porciento(double valor)
        {
            return conexion.ResumenAsistencia.PorcentajeMostrado(valor).ToString(CultureInfo.InvariantCulture) + " %";
        }

        protected override float AltoCuerpo(Graphics g, EstiloGrafica e, float ancho)
        {
            return AltoDibujo + e.Pequena.GetHeight(g) + 10F;
        }

        protected override void DibujarCuerpo(Graphics g, EstiloGrafica e, RectangleF area)
        {
            if (Puntos.Count == 0)
            {
                return;
            }

            float altoTexto = e.Pequena.GetHeight(g);
            float anchoEje = Medir(g, "100 %", e.Pequena).Width;
            float x0 = area.X + anchoEje + 8F;
            float arriba = area.Y + altoTexto / 2F;
            float abajo = area.Y + AltoDibujo;
            // A la derecha, sitio para la cifra del ultimo punto.
            float anchoUltimo = Medir(g, "100 %", e.Valor).Width + 10F;
            float ancho = Math.Max(20F, area.Right - x0 - anchoUltimo);
            for (int v = 0; v <= 100; v += 20)
            {
                float yv = abajo - (abajo - arriba) * v / 100F;
                Linea(g, v == 0 ? ColoresGrafica.Base : ColoresGrafica.Rejilla, 1F, x0, yv, x0 + ancho, yv);
                Texto(g, v.ToString(CultureInfo.InvariantCulture) + " %", e.Pequena, ColoresGrafica.TintaSecundaria, x0 - 6F, yv - altoTexto / 2F, true);
            }

            if (Marca.HasValue)
            {
                float ym = abajo - (abajo - arriba) * (float)(Marca.Value / 100.0);
                using (Pen lapiz = new Pen(ColoresGrafica.Rojo, 1F) { DashPattern = new[] { 4F, 3F } })
                {
                    g.DrawLine(lapiz, x0, ym, x0 + ancho, ym);
                }
            }

            float hueco = ancho / Puntos.Count;
            Func<int, PointF> donde = i => new PointF(x0 + hueco * (i + 0.5F),
                abajo - (abajo - arriba) * (float)(Math.Max(0, Math.Min(100, Puntos[i].Valor ?? 0)) / 100.0));

            // La linea, cortada donde no hay dato.
            using (Pen lapiz = new Pen(ColoresGrafica.Verde, 2F) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                List<PointF> tramo = new List<PointF>();
                for (int i = 0; i <= Puntos.Count; i++)
                {
                    if (i < Puntos.Count && Puntos[i].Valor.HasValue)
                    {
                        tramo.Add(donde(i));
                        continue;
                    }

                    if (tramo.Count > 1)
                    {
                        g.DrawLines(lapiz, tramo.ToArray());
                    }

                    tramo.Clear();
                }
            }

            // Los puntos: 8 px con 2 px de fondo alrededor (se leen sobre la linea y la marca).
            int ultimo = -1;
            int masBajo = -1;
            for (int i = 0; i < Puntos.Count; i++)
            {
                if (!Puntos[i].Valor.HasValue)
                {
                    continue;
                }

                ultimo = i;
                if (masBajo < 0 || Puntos[i].Valor.Value < Puntos[masBajo].Valor.Value)
                {
                    masBajo = i;
                }

                PointF p = donde(i);
                using (SolidBrush anillo = new SolidBrush(ColoresGrafica.Fondo))
                using (SolidBrush punto = new SolidBrush(ColoresGrafica.Verde))
                {
                    g.FillEllipse(anillo, p.X - 6F, p.Y - 6F, 12F, 12F);
                    g.FillEllipse(punto, p.X - 4F, p.Y - 4F, 8F, 8F);
                }
            }

            float altoValor = e.Valor.GetHeight(g);
            if (ultimo >= 0)
            {
                PointF p = donde(ultimo);
                Texto(g, Porciento(Puntos[ultimo].Valor.Value), e.Valor, ColoresGrafica.Tinta, p.X + 9F, p.Y - altoValor / 2F);
            }

            if (masBajo >= 0 && masBajo != ultimo && Puntos[masBajo].Valor.Value < Puntos[ultimo].Valor.Value - 0.5)
            {
                PointF p = donde(masBajo);
                // Debajo del punto (encima si no cabe sobre el eje).
                float y = p.Y + 7F + altoValor <= abajo - 1F ? p.Y + 7F : p.Y - 7F - altoValor;
                Texto(g, Porciento(Puntos[masBajo].Valor.Value), e.Valor, ColoresGrafica.Tinta, p.X, y, false, true);
            }

            float anchoEtiqueta = Puntos.Max(pt => Medir(g, pt.Etiqueta, e.Pequena).Width);
            int saltar = Math.Max(1, (int)Math.Ceiling((anchoEtiqueta + 8F) / hueco));
            for (int i = 0; i < Puntos.Count; i++)
            {
                bool ultima = i == Puntos.Count - 1;
                if (i % saltar == 0 || (ultima && (i % saltar) * hueco >= anchoEtiqueta + 8F))
                {
                    Texto(g, Puntos[i].Etiqueta, e.Pequena, ColoresGrafica.TintaSecundaria, x0 + hueco * (i + 0.5F), abajo + 4F, false, true);
                }
            }

            if (Marca.HasValue && TextoMarca.Length > 0)
            {
                float ym = abajo - (abajo - arriba) * (float)(Marca.Value / 100.0);
                Texto(g, TextoMarca, e.Pequena, ColoresGrafica.TintaSecundaria, x0 + 4F, ym - altoTexto - 1F);
            }
        }
    }
}
