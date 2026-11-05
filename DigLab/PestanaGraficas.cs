using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// Lo de la pestana Graficas de Estadisticas, una seccion a la vez (2026-10-31; el
    /// usuario: "estan dificiles de leer... necesitan un orden": antes iban las cuatro
    /// seguidas, con las graficas de dos en dos y las tablas entre una y otra). Arriba,
    /// fijos, los cuatro pasos numerados (1. Uso de los laboratorios ... 4. Fallas y
    /// equipos); debajo, la seccion elegida en una columna que se lee de arriba abajo
    /// (HojaDeGraficas). Al cambiar de seccion se desliza como los horarios; Ctrl+← y
    /// Ctrl+→ tambien cambian (← y → son del laboratorio). La seccion y si las tablas
    /// estan abiertas se recuerdan mientras DigLab siga abierto, tambien al cambiar los
    /// filtros de arriba (se vuelve a armar con cada calculo).
    /// </summary>
    internal sealed class PanelDeGraficas : Panel
    {
        private const int AltoPaso = 34;
        // Lo que deja cada paso a los lados de su titulo (con menos, el boton lo parte en dos renglones).
        private const int HolguraPaso = 16;
        private const int EntrePasos = 8;
        private const int ArribaPasos = 8;
        private static int seccionRecordada;
        private static bool tablasAbiertas;

        private readonly Panel barra = new Panel { BackColor = Color.White, Visible = false };
        private readonly List<Button> pasos = new List<Button>();
        private readonly HojaDeGraficas hoja = new HojaDeGraficas();
        private readonly ToolTip ayuda = new ToolTip();
        private List<SeccionGraficas> secciones = new List<SeccionGraficas>();
        private int elegida;
        // Si van los pasos (hay secciones). No barra.Visible: con la pestana oculta
        // siempre dice false, y al armarse en otra pestana la seccion los taparia.
        private bool conPasos;

        public PanelDeGraficas()
        {
            BackColor = Color.White;
            AccessibleName = "Gráficas";
            // El tabulador primero por los pasos (arriba) y despues por la seccion: con el orden de
            // alta, la barra iba despues de "Ver las cifras exactas" y "Siguiente", que estan abajo.
            barra.TabIndex = 0;
            hoja.TabIndex = 1;
            Controls.Add(hoja);
            Controls.Add(barra);
            hoja.Ir += paso => Elegir(elegida + paso);
            hoja.TablasAlternadas += () =>
            {
                tablasAbiertas = !tablasAbiertas;
                PonerHoja(false);
            };
            TeclasDeFlechas.Registrar(barra, true, paso => Elegir(elegida + Math.Sign(paso)));
        }

        /// <summary>Sin secciones: "Leyendo..." o el error de la lectura, centrado.</summary>
        public void Avisar(string texto, bool esError)
        {
            secciones = new List<SeccionGraficas>();
            conPasos = false;
            barra.Visible = false;
            Acomodar();
            hoja.Avisar(texto, esError);
        }

        /// <summary>Las secciones de un calculo: se sigue viendo la misma seccion (y la misma zona, si aun la hay).</summary>
        public void Poner(List<SeccionGraficas> nuevas)
        {
            if (nuevas == null || nuevas.Count == 0)
            {
                Avisar("", false);
                return;
            }

            bool desdeArriba = secciones.Count == 0;
            secciones = nuevas;
            elegida = Math.Max(0, Math.Min(seccionRecordada, secciones.Count - 1));
            PonerPasos();
            conPasos = true;
            barra.Visible = true;
            Acomodar();
            PonerHoja(desdeArriba);
        }

        /// <summary>Un boton por seccion; la que se ve, en verde lleno.</summary>
        private void PonerPasos()
        {
            while (pasos.Count < secciones.Count)
            {
                int indice = pasos.Count;
                Button paso = EstiloHorarios.CrearBoton("", Point.Empty, new Size(120, AltoPaso));
                paso.Click += (s, e) => Elegir(indice);
                pasos.Add(paso);
                barra.Controls.Add(paso);
            }

            while (pasos.Count > secciones.Count)
            {
                Button sobra = pasos[pasos.Count - 1];
                pasos.RemoveAt(pasos.Count - 1);
                barra.Controls.Remove(sobra);
                sobra.Dispose();
            }

            for (int i = 0; i < pasos.Count; i++)
            {
                SeccionGraficas seccion = secciones[i];
                Button paso = pasos[i];
                bool esta = i == elegida;
                paso.Text = seccion.Numero + ". " + seccion.Titulo;
                paso.BackColor = esta ? EstiloHorarios.VerdeTeschi : Color.White;
                paso.ForeColor = esta ? Color.White : Color.DarkGreen;
                paso.FlatAppearance.MouseOverBackColor = esta ? EstiloHorarios.VerdeTeschi : EstiloHorarios.VerdeSobre;
                paso.FlatAppearance.MouseDownBackColor = esta ? EstiloHorarios.VerdeTeschi : EstiloHorarios.VerdePulsado;
                paso.Cursor = esta ? Cursors.Default : Cursors.Hand;
                paso.AccessibleName = "Sección " + seccion.Numero + " de " + secciones.Count + ": " + seccion.Titulo + (esta ? ", la que se ve" : "");
                ayuda.SetToolTip(paso, seccion.Numero + ". " + seccion.Titulo + (esta ? " (la que se ve)" : "") + Environment.NewLine
                    + "Ctrl+← y Ctrl+→: la sección anterior y la siguiente.");
            }
        }

        /// <summary>Otra seccion, deslizandose hacia su lado (la siguiente entra por la derecha).</summary>
        private void Elegir(int indice)
        {
            if (!conPasos || indice < 0 || indice >= secciones.Count || indice == elegida)
            {
                return;
            }

            int direccion = Math.Sign(indice - elegida);
            Deslizamiento.Cambiar(direccion, () =>
            {
                elegida = indice;
                seccionRecordada = indice;
                PonerPasos();
                PonerHoja(true);
            }, hoja);
        }

        private void PonerHoja(bool desdeArriba)
        {
            hoja.Poner(secciones[elegida], elegida > 0 ? secciones[elegida - 1] : null, elegida + 1 < secciones.Count ? secciones[elegida + 1] : null,
                tablasAbiertas, desdeArriba);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Acomodar();
        }

        /// <summary>Los pasos arriba, en la misma columna que la seccion; la seccion, en el resto.</summary>
        private void Acomodar()
        {
            int arriba = 0;
            if (conPasos && pasos.Count > 0)
            {
                int columna = HojaDeGraficas.AnchoDeColumna(ClientSize.Width);
                int[] anchos = AnchosDePasos(columna - (pasos.Count - 1) * EntrePasos);
                int alto = AltoDePasos(anchos);
                barra.SetBounds(0, 0, ClientSize.Width, ArribaPasos + alto + 4);
                int x = HojaDeGraficas.MargenX;
                for (int i = 0; i < pasos.Count; i++)
                {
                    pasos[i].SetBounds(x, ArribaPasos, anchos[i], alto);
                    x += anchos[i] + EntrePasos;
                }

                arriba = barra.Height;
            }

            hoja.SetBounds(0, arriba, ClientSize.Width, Math.Max(10, ClientSize.Height - arriba));
        }

        /// <summary>
        /// El ancho de cada paso en "libre" (la columna sin lo de entre pasos): todos iguales si en
        /// ese ancho cabe en un renglon el titulo de cada uno. Si no (a 1000 px, "3. Asistencia de
        /// los estudiantes" se partia en dos renglones y en 34 px se cortaba el de arriba), el que no
        /// cabe toma lo que pide su titulo y los demas, lo mismo entre ellos. Con aun menos ancho
        /// (ni asi caben), iguales: AltoDePasos les da dos renglones.
        /// </summary>
        private int[] AnchosDePasos(int libre)
        {
            int n = pasos.Count;
            int igual = Math.Max(60, libre / n);
            int[] piden = pasos.Select(p => TextRenderer.MeasureText(p.Text, p.Font).Width + HolguraPaso).ToArray();
            int[] anchos = Enumerable.Repeat(igual, n).ToArray();
            if (piden.Max() <= igual || piden.Sum() > libre)
            {
                return anchos;
            }

            // Lo mas parecidos posible: quien pide mas que el reparto se queda con lo suyo y el resto
            // se reparte otra vez entre los demas, hasta que todos quepan.
            List<int> sinFijar = Enumerable.Range(0, n).ToList();
            int queda = libre;
            bool fijoAlguno = true;
            while (fijoAlguno && sinFijar.Count > 0)
            {
                fijoAlguno = false;
                int reparto = queda / sinFijar.Count;
                foreach (int i in sinFijar.Where(k => piden[k] > reparto).ToList())
                {
                    anchos[i] = piden[i];
                    queda -= piden[i];
                    sinFijar.Remove(i);
                    fijoAlguno = true;
                }
            }

            foreach (int i in sinFijar)
            {
                anchos[i] = queda / sinFijar.Count;
            }

            return anchos;
        }

        /// <summary>AltoPaso o, si con esos anchos algun titulo no cabe en un renglon, el alto de los suyos: nunca cortado.</summary>
        private int AltoDePasos(int[] anchos)
        {
            int alto = AltoPaso;
            for (int i = 0; i < pasos.Count; i++)
            {
                Button paso = pasos[i];
                if (TextRenderer.MeasureText(paso.Text, paso.Font).Width + HolguraPaso > anchos[i])
                {
                    Size pide = TextRenderer.MeasureText(paso.Text, paso.Font, new Size(Math.Max(20, anchos[i] - HolguraPaso), int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    alto = Math.Max(alto, pide.Height + 16);
                }
            }

            return alto;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// La seccion elegida de Graficas en una columna (a lo mas AnchoMaximo, desde la
    /// orilla izquierda como las demas pestanas), con desplazamiento, en el orden en que
    /// se lee: su titulo y su pregunta, la lectura (lo que dicen sus numeros), sus
    /// cifras, cada grafica a lo ancho, una debajo de otra, las cifras exactas (sus
    /// tablas, plegadas: un boton las abre), sus notas y, al final, la seccion anterior
    /// y la siguiente.
    /// </summary>
    internal sealed class HojaDeGraficas : Panel
    {
        internal const int MargenX = 14;
        private const int AnchoMaximo = 1240;

        private readonly DrawingFont fuenteSeccion = new DrawingFont("Century Gothic", 13F, FontStyle.Bold);
        private readonly DrawingFont fuenteDescripcion = new DrawingFont("Century Gothic", 9.5F);
        private readonly DrawingFont fuenteLectura = new DrawingFont("Century Gothic", 10.5F);
        private readonly DrawingFont fuenteNota = new DrawingFont("Century Gothic", 8.5F);
        private readonly DrawingFont fuenteSinDatos = new DrawingFont("Century Gothic", 10F, FontStyle.Italic);
        private readonly EstiloGrafica estilo = EstiloGrafica.Pantalla();
        private readonly List<Bloque> bloques = new List<Bloque>();
        private string aviso = "";
        private bool avisoEsError;

        /// <summary>-1: la seccion anterior; 1: la siguiente (los botones del final).</summary>
        public event Action<int> Ir;

        /// <summary>"Ver las cifras exactas" u "Ocultar las cifras exactas".</summary>
        public event Action TablasAlternadas;

        /// <summary>Lo que se coloca: un control, su alto a un ancho y, si no ocupa toda la columna, su ancho.</summary>
        private sealed class Bloque
        {
            public Control Control;
            public Func<int, int> AltoPara;
            /// <summary>Espacio antes.</summary>
            public int Antes;
            /// <summary>Lo que pide de ancho (los botones); null: toda la columna.</summary>
            public Func<int> Ancho;
        }

        public HojaDeGraficas()
        {
            AutoScroll = true;
            BackColor = Color.White;
            DoubleBuffered = true;
            ResizeRedraw = true;
            TabStop = true;
            AccessibleName = "Sección de gráficas";
            // Un clic en cualquier sitio de la seccion (no en sus botones) le da el foco: la rueda y las flechas la desplazan.
            ControlAdded += (s, e) =>
            {
                if (!(e.Control is ButtonBase))
                {
                    e.Control.MouseDown += (o, m) => Focus();
                }
            };
        }

        /// <summary>El ancho de la columna con ese ancho de pestana (los pasos de arriba usan el mismo).</summary>
        internal static int AnchoDeColumna(int anchoTotal)
        {
            // Siempre con el sitio de la barra de desplazamiento: si no, al salir esta
            // cambia el ancho, se vuelve a acomodar y puede ir y venir.
            return Math.Min(AnchoMaximo, Math.Max(200, anchoTotal - SystemInformation.VerticalScrollBarWidth - 2 * MargenX - 2));
        }

        public void Avisar(string texto, bool esError)
        {
            Vaciar();
            aviso = texto ?? "";
            avisoEsError = esError;
            AutoScrollMinSize = Size.Empty;
            Invalidate();
        }

        /// <param name="anterior">La seccion de antes (null en la primera): su boton va al final.</param>
        /// <param name="siguiente">La de despues (null en la ultima).</param>
        /// <param name="conTablas">Con las cifras exactas abiertas.</param>
        /// <param name="desdeArriba">Otra seccion: desde arriba; la misma (otro filtro, abrir las tablas): en la misma zona.</param>
        public void Poner(SeccionGraficas seccion, SeccionGraficas anterior, SeccionGraficas siguiente, bool conTablas, bool desdeArriba)
        {
            int desplazado = desdeArriba ? 0 : -AutoScrollPosition.Y;
            SuspendLayout();
            try
            {
                Vaciar();
                aviso = "";
                Agregar(Texto(seccion.Numero + ". " + seccion.Titulo, fuenteSeccion, EstiloHorarios.VerdeTeschi), 10);
                Agregar(Texto(seccion.Descripcion, fuenteDescripcion, EstiloAdmin.TextoSecundario), 2);
                if (seccion.SinDatos != null)
                {
                    Agregar(Texto(seccion.SinDatos, fuenteSinDatos, ColoresGrafica.Tinta), 14);
                }
                else
                {
                    if (!string.IsNullOrEmpty(seccion.Lectura))
                    {
                        Agregar(Texto(seccion.Lectura, fuenteLectura, ColoresGrafica.Tinta), 12);
                    }

                    if (seccion.Cifras.Count > 0)
                    {
                        FilaDeCifras cifras = new FilaDeCifras(seccion.Cifras);
                        Agregar(cifras, cifras.AltoPara, 14, null);
                    }

                    foreach (Grafica grafica in seccion.Graficas)
                    {
                        LienzoGrafica lienzo = new LienzoGrafica(grafica, estilo);
                        Agregar(lienzo, lienzo.AltoPara, 28, null);
                    }

                    if (seccion.Tablas.Count > 0)
                    {
                        int n = seccion.Tablas.Count;
                        Button cifrasExactas = Boton(conTablas ? "Ocultar las cifras exactas" : "Ver las cifras exactas (" + n + (n == 1 ? " tabla)" : " tablas)"));
                        cifrasExactas.AccessibleDescription = string.Join(", ", seccion.Tablas.Select(t => t.Titulo));
                        cifrasExactas.Click += (s, e) => Despues(() => TablasAlternadas?.Invoke());
                        Agregar(cifrasExactas, ancho => cifrasExactas.Height, 28, () => cifrasExactas.Width);
                        if (conTablas)
                        {
                            foreach (TablaDeCifras tabla in seccion.Tablas)
                            {
                                // A lo ancho de la columna: se dibuja a su medida (y su primera columna, con algo mas).
                                TablaDibujada dibujada = new TablaDibujada(tabla);
                                Agregar(dibujada, dibujada.AltoPara, 16, null);
                            }
                        }
                    }
                }

                bool primera = true;
                foreach (string nota in seccion.Notas)
                {
                    Agregar(Texto(nota, fuenteNota, EstiloAdmin.TextoSecundario), primera ? 22 : 4);
                    primera = false;
                }

                if (anterior != null || siguiente != null)
                {
                    Panel navegacion = Navegacion(anterior, siguiente);
                    Agregar(navegacion, ancho => navegacion.Height, 26, null);
                }

                Acomodar();
            }
            finally
            {
                ResumeLayout(false);
            }

            // Sin otro acomodo, la barra vertical no se recalculaba con el AutoScrollMinSize
            // nuevo (se puso con el acomodo suspendido): lo de abajo no se alcanzaba.
            PerformLayout();
            AutoScrollPosition = new Point(0, desplazado);
            Invalidate(true);
        }

        /// <summary>Al final, la seccion anterior a la izquierda y la siguiente a la derecha.</summary>
        private Panel Navegacion(SeccionGraficas anterior, SeccionGraficas siguiente)
        {
            Panel navegacion = new Panel { BackColor = Color.White, Height = 34 };
            Button adelante = null;
            if (anterior != null)
            {
                Button atras = Boton("« Anterior: " + anterior.Numero + ". " + anterior.Titulo);
                atras.Click += (s, e) => Despues(() => Ir?.Invoke(-1));
                navegacion.Controls.Add(atras);
            }

            if (siguiente != null)
            {
                adelante = Boton("Siguiente: " + siguiente.Numero + ". " + siguiente.Titulo + " »");
                adelante.Click += (s, e) => Despues(() => Ir?.Invoke(1));
                navegacion.Controls.Add(adelante);
            }

            navegacion.Resize += (s, e) =>
            {
                if (adelante != null)
                {
                    adelante.Location = new Point(Math.Max(0, navegacion.Width - adelante.Width), 0);
                }
            };
            return navegacion;
        }

        /// <summary>Un boton como los de DigLab, a la medida de su texto.</summary>
        private static Button Boton(string texto)
        {
            Button boton = EstiloHorarios.CrearBoton(texto, Point.Empty, new Size(10, 34));
            boton.Width = TextRenderer.MeasureText(texto, boton.Font).Width + 36;
            return boton;
        }

        /// <summary>Despues del clic: lo que hace vuelve a armar la seccion y quita el boton que se pulso.</summary>
        private void Despues(Action accion)
        {
            BeginInvoke(new Action(() =>
            {
                accion();
                if (!IsDisposed)
                {
                    Focus();
                }
            }));
        }

        private static Label Texto(string texto, DrawingFont fuente, Color color)
        {
            return new Label { AutoSize = false, Text = texto, Font = fuente, ForeColor = color, BackColor = Color.White, UseMnemonic = false };
        }

        private void Agregar(Label etiqueta, int antes)
        {
            Agregar(etiqueta, ancho => TextRenderer.MeasureText(etiqueta.Text, etiqueta.Font, new Size(Math.Max(40, ancho), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 2, antes, null);
        }

        private void Agregar(Control control, Func<int, int> altoPara, int antes, Func<int> ancho)
        {
            bloques.Add(new Bloque { Control = control, AltoPara = altoPara, Antes = antes, Ancho = ancho });
            Controls.Add(control);
        }

        private void Vaciar()
        {
            foreach (Bloque bloque in bloques)
            {
                Controls.Remove(bloque.Control);
                bloque.Control.Dispose();
            }

            bloques.Clear();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Acomodar();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // Armada con la pestana oculta, Windows no pone la barra vertical al ensenarse
            // (AutoScrollMinSize ya era mas alto que la hoja) hasta otro acomodo: lo de abajo
            // no se alcanzaba (2026-10-31). Al ensenarse, se acomoda otra vez.
            if (Visible)
            {
                PerformLayout();
            }
        }

        /// <summary>Los bloques de arriba abajo en la columna; los botones, a su medida.</summary>
        private void Acomodar()
        {
            if (bloques.Count == 0 || Width < 100)
            {
                return;
            }

            int columna = AnchoDeColumna(Width);
            int y = 4;
            int desplazamiento = AutoScrollPosition.Y;
            SuspendLayout();
            foreach (Bloque bloque in bloques)
            {
                y += bloque.Antes;
                int ancho = bloque.Ancho == null ? columna : Math.Max(40, Math.Min(columna, bloque.Ancho()));
                int alto = bloque.AltoPara(ancho);
                bloque.Control.SetBounds(MargenX, y + desplazamiento, ancho, alto);
                y += alto;
            }

            ResumeLayout(false);
            AutoScrollMinSize = new Size(0, y + 24);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (bloques.Count == 0 && aviso.Length > 0)
            {
                TextRenderer.DrawText(e.Graphics, aviso, fuenteDescripcion, new Rectangle(24, 0, Math.Max(10, ClientSize.Width - 48), ClientSize.Height),
                    avisoEsError ? Color.FromArgb(183, 28, 28) : Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // Con el foco aqui, las flechas y Re Pag / Av Pag desplazan.
            int paso = e.KeyCode == Keys.Down ? 40 : e.KeyCode == Keys.Up ? -40 : e.KeyCode == Keys.PageDown ? ClientSize.Height - 40
                : e.KeyCode == Keys.PageUp ? -(ClientSize.Height - 40) : e.KeyCode == Keys.Home ? int.MinValue / 2 : e.KeyCode == Keys.End ? int.MaxValue / 2 : 0;
            if (paso != 0)
            {
                AutoScrollPosition = new Point(0, Math.Max(0, -AutoScrollPosition.Y + paso));
                e.Handled = true;
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys tecla = keyData & Keys.KeyCode;
            return tecla == Keys.Up || tecla == Keys.Down || tecla == Keys.PageUp || tecla == Keys.PageDown || base.IsInputKey(keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Vaciar();
                fuenteSeccion.Dispose();
                fuenteDescripcion.Dispose();
                fuenteLectura.Dispose();
                fuenteNota.Dispose();
                fuenteSinDatos.Dispose();
                estilo.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Una grafica en pantalla: la misma Grafica.Dibujar que el PDF, con ClearType.</summary>
    internal sealed class LienzoGrafica : Control
    {
        private readonly Grafica grafica;
        private readonly EstiloGrafica estilo;

        public LienzoGrafica(Grafica grafica, EstiloGrafica estilo)
        {
            this.grafica = grafica;
            this.estilo = estilo;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.White;
            AccessibleRole = AccessibleRole.Chart;
            AccessibleName = grafica.Titulo;
            AccessibleDescription = grafica.EnTexto();
        }

        public int AltoPara(int ancho)
        {
            return (int)Math.Ceiling(grafica.Alto(estilo, ancho));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            grafica.Dibujar(e.Graphics, estilo, new RectangleF(0F, 0F, ClientSize.Width, ClientSize.Height));
        }
    }

    /// <summary>La fila de cifras de una seccion: cada una con su numero grande y su rotulo en gris debajo.</summary>
    internal sealed class FilaDeCifras : Control
    {
        private const int Entre = 40;
        private readonly List<CifraResumen> cifras;
        private readonly DrawingFont fuenteValor = new DrawingFont("Century Gothic", 15F, FontStyle.Bold);
        private readonly DrawingFont fuenteEtiqueta = new DrawingFont("Century Gothic", 8.5F);

        public FilaDeCifras(List<CifraResumen> cifras)
        {
            this.cifras = cifras;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.White;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = string.Join("; ", cifras.Select(c => c.Valor + " " + c.Etiqueta));
        }

        private int AnchoDe(CifraResumen c)
        {
            return Math.Max(TextRenderer.MeasureText(c.Valor, fuenteValor).Width, TextRenderer.MeasureText(c.Etiqueta, fuenteEtiqueta).Width);
        }

        private int AltoRenglon
        {
            get { return fuenteValor.Height + fuenteEtiqueta.Height + 6; }
        }

        public int AltoPara(int ancho)
        {
            int renglones = 1;
            int x = 0;
            foreach (CifraResumen c in cifras)
            {
                int pide = AnchoDe(c);
                if (x > 0 && x + pide > ancho)
                {
                    renglones++;
                    x = 0;
                }

                x += pide + Entre;
            }

            return renglones * AltoRenglon;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int x = 0;
            int y = 0;
            foreach (CifraResumen c in cifras)
            {
                int pide = AnchoDe(c);
                if (x > 0 && x + pide > ClientSize.Width)
                {
                    x = 0;
                    y += AltoRenglon;
                }

                TextRenderer.DrawText(e.Graphics, c.Valor, fuenteValor, new Point(x - 4, y), c.Color ?? ColoresGrafica.Tinta, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, c.Etiqueta, fuenteEtiqueta, new Point(x - 2, y + fuenteValor.Height), EstiloAdmin.TextoSecundario, TextFormatFlags.NoPrefix);
                x += pide + Entre;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuenteValor.Dispose();
                fuenteEtiqueta.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Una tablita de cifras en pantalla, con el aspecto de las tablas de DigLab
    /// (encabezado verde claro, lineas horizontales suaves): su titulo, el
    /// encabezado y todas sus filas (sin desplazamiento propio: se desplaza la pestana).
    /// </summary>
    internal sealed class TablaDibujada : Control
    {
        private const int AltoTitulo = 24;
        private const int AltoEncabezado = 28;
        private const int AltoFila = 24;
        private const int Relleno = 8;
        private static readonly Color FondoEncabezado = Color.FromArgb(240, 248, 240);
        private readonly TablaDeCifras tabla;
        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteEncabezado = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private readonly DrawingFont fuente = new DrawingFont("Century Gothic", 9F);
        private readonly DrawingFont fuenteTotal = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);

        public TablaDibujada(TablaDeCifras tabla)
        {
            this.tabla = tabla;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.White;
            AccessibleRole = AccessibleRole.Table;
            AccessibleName = tabla.Titulo;
            AccessibleDescription = string.Join(" | ", tabla.Columnas.Select(c => c.Titulo)) + ". "
                + string.Join("; ", tabla.Filas.Select(f => string.Join(", ", f.Celdas)));
        }

        public int AltoPara(int ancho)
        {
            return AltoTitulo + AltoEncabezado + Math.Max(1, tabla.Filas.Count) * AltoFila + 2;
        }

        /// <summary>Lo que piden sus columnas enteras (sin estirar la primera).</summary>
        public int AnchoNatural
        {
            get { return Anchos(int.MaxValue / 4, false).Sum(); }
        }

        /// <summary>
        /// Los anchos: lo que pide cada columna; si sobra, para la primera (los nombres)
        /// hasta 320 px; si falta, encogen las de texto (con "...", entero en la ayuda).
        /// </summary>
        private int[] Anchos(int disponible, bool estirar = true)
        {
            int n = tabla.Columnas.Count;
            int[] anchos = new int[n];
            for (int i = 0; i < n; i++)
            {
                int pide = TextRenderer.MeasureText(tabla.Columnas[i].Titulo, fuenteEncabezado).Width;
                foreach (FilaDeTabla fila in tabla.Filas)
                {
                    if (i < fila.Celdas.Length)
                    {
                        pide = Math.Max(pide, TextRenderer.MeasureText(fila.Celdas[i] ?? "", fila.Total ? fuenteTotal : fuente).Width);
                    }
                }

                anchos[i] = pide + 2 * Relleno;
            }

            int total = anchos.Sum();
            if (total < disponible && n > 0 && estirar)
            {
                anchos[0] += Math.Min(disponible - total, Math.Max(0, 320 - anchos[0]));
            }
            else if (total > disponible)
            {
                int sobra = total - disponible;
                List<int> deTexto = Enumerable.Range(0, n).Where(i => !tabla.Columnas[i].Derecha).ToList();
                int deTextoAncho = deTexto.Sum(i => anchos[i]);
                foreach (int i in deTexto)
                {
                    int quitar = deTextoAncho == 0 ? 0 : (int)Math.Ceiling(sobra * (double)anchos[i] / deTextoAncho);
                    anchos[i] = Math.Max(60, anchos[i] - quitar);
                }
            }

            return anchos;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int[] anchos = Anchos(ClientSize.Width);
            int anchoTabla = Math.Min(ClientSize.Width, anchos.Sum());
            TextRenderer.DrawText(g, tabla.Titulo, fuenteTitulo, new Point(0, 2), ColoresGrafica.Tinta, TextFormatFlags.NoPrefix);
            int y = AltoTitulo;
            using (SolidBrush fondo = new SolidBrush(FondoEncabezado))
            {
                g.FillRectangle(fondo, 0, y, anchoTabla, AltoEncabezado);
            }

            int x = 0;
            for (int i = 0; i < tabla.Columnas.Count; i++)
            {
                Celda(g, tabla.Columnas[i].Titulo, fuenteEncabezado, Color.DarkGreen, new Rectangle(x, y, anchos[i], AltoEncabezado), tabla.Columnas[i].Derecha);
                x += anchos[i];
            }

            y += AltoEncabezado;
            using (Pen linea = new Pen(EstiloHorarios.LineaSuave))
            using (SolidBrush fondoTotal = new SolidBrush(EstiloHorarios.VerdeSobre))
            {
                foreach (FilaDeTabla fila in tabla.Filas)
                {
                    if (fila.Total)
                    {
                        g.FillRectangle(fondoTotal, 0, y, anchoTabla, AltoFila);
                    }

                    x = 0;
                    for (int i = 0; i < tabla.Columnas.Count; i++)
                    {
                        Color color;
                        if (!fila.Colores.TryGetValue(i, out color))
                        {
                            color = ColoresGrafica.Tinta;
                        }

                        Celda(g, i < fila.Celdas.Length ? fila.Celdas[i] : "", fila.Total ? fuenteTotal : fuente, color, new Rectangle(x, y, anchos[i], AltoFila), tabla.Columnas[i].Derecha);
                        x += anchos[i];
                    }

                    y += AltoFila;
                    g.DrawLine(linea, 0, y - 1, anchoTabla, y - 1);
                }
            }
        }

        private static void Celda(Graphics g, string texto, DrawingFont fuente, Color color, Rectangle r, bool derecha)
        {
            TextRenderer.DrawText(g, texto ?? "", fuente, new Rectangle(r.X + Relleno, r.Y, Math.Max(1, r.Width - 2 * Relleno), r.Height), color,
                (derecha ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuenteTitulo.Dispose();
                fuenteEncabezado.Dispose();
                fuente.Dispose();
                fuenteTotal.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
