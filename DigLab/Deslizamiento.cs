using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DigLab
{
    /// <summary>
    /// El cambio de un horario a otro (otro laboratorio, otra semana, otro mes)
    /// con un deslizamiento: lo de antes sale por un lado y lo nuevo entra por el
    /// otro. Hacia lo siguiente (direccion 1) lo nuevo entra por la derecha; hacia
    /// lo anterior (-1), por la izquierda. Se hace con dos fotos de cada vista (la
    /// de antes y la de despues del cambio) en una cortina encima de ella, que se
    /// quita al terminar: la vista de verdad ya quedo con lo nuevo. Sin animaciones
    /// en Windows (Rendimiento, "Animar controles"), con la vista oculta o sin
    /// direccion, el cambio se hace sin mas.
    /// </summary>
    internal static class Deslizamiento
    {
        private const int Duracion = 260;
        private static readonly Dictionary<Control, Cortina> enCurso = new Dictionary<Control, Cortina>();

        /// <summary>Hace el cambio (que repinta las vistas) con el deslizamiento hacia esa direccion.</summary>
        public static void Cambiar(int direccion, Action cambiar, params Control[] vistas)
        {
            Bitmap[] antes = Capturar(direccion, vistas);
            cambiar();
            Animar(antes, direccion, vistas);
        }

        /// <summary>Las fotos de antes (null donde no se anima); se pasan a Animar despues del cambio.</summary>
        public static Bitmap[] Capturar(int direccion, Control[] vistas)
        {
            Bitmap[] fotos = new Bitmap[vistas.Length];
            if (direccion == 0 || !SystemInformation.UIEffectsEnabled)
            {
                return fotos;
            }

            for (int i = 0; i < vistas.Length; i++)
            {
                Control vista = vistas[i];
                if (!Animable(vista))
                {
                    continue;
                }

                Terminar(vista);
                fotos[i] = Foto(vista);
            }

            return fotos;
        }

        /// <summary>Con las vistas ya cambiadas: la foto de despues y el deslizamiento de una a otra.</summary>
        public static void Animar(Bitmap[] antes, int direccion, Control[] vistas)
        {
            for (int i = 0; i < vistas.Length; i++)
            {
                Bitmap previa = antes == null || i >= antes.Length ? null : antes[i];
                if (previa == null)
                {
                    continue;
                }

                Control vista = vistas[i];
                if (direccion == 0 || !Animable(vista) || vista.Size != previa.Size)
                {
                    previa.Dispose();
                    continue;
                }

                Cortina cortina = new Cortina(vista, previa, Foto(vista), Math.Sign(direccion));
                enCurso[vista] = cortina;
                cortina.Empezar();
            }
        }

        /// <summary>Una vista que se ve y cuyo contenedor la deja tener otra encima (no los que acomodan solos).</summary>
        private static bool Animable(Control vista)
        {
            return vista != null && !vista.IsDisposed && vista.IsHandleCreated && vista.Visible && vista.Width > 8 && vista.Height > 8
                && vista.Parent != null && !(vista.Parent is FlowLayoutPanel) && !(vista.Parent is TableLayoutPanel);
        }

        private static Bitmap Foto(Control vista)
        {
            Bitmap foto = new Bitmap(vista.Width, vista.Height);
            vista.DrawToBitmap(foto, new Rectangle(Point.Empty, vista.Size));
            return foto;
        }

        /// <summary>Si la vista se estaba deslizando, se acaba ya (otro cambio antes de terminar).</summary>
        private static void Terminar(Control vista)
        {
            Cortina cortina;
            if (enCurso.TryGetValue(vista, out cortina))
            {
                cortina.Quitar();
            }
        }

        /// <summary>Lo que se ve encima de la vista mientras se desliza: las dos fotos, movidas segun el tiempo.</summary>
        private sealed class Cortina : Control
        {
            private readonly Control vista;
            private readonly Bitmap antes;
            private readonly Bitmap despues;
            private readonly int direccion;
            private readonly Stopwatch reloj = new Stopwatch();
            private readonly Timer tic = new Timer { Interval = 15 };
            private bool quitada;

            public Cortina(Control vista, Bitmap antes, Bitmap despues, int direccion)
            {
                this.vista = vista;
                this.antes = antes;
                this.despues = despues;
                this.direccion = direccion;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Opaque, true);
                Bounds = vista.Bounds;
                Anchor = vista.Anchor;
                TabStop = false;
                tic.Tick += (s, e) =>
                {
                    if (reloj.ElapsedMilliseconds >= Duracion || vista.IsDisposed || !vista.Visible || vista.Bounds != Bounds)
                    {
                        Quitar();
                        return;
                    }

                    Invalidate();
                };
            }

            public void Empezar()
            {
                vista.Parent.Controls.Add(this);
                BringToFront();
                reloj.Start();
                tic.Start();
                Refresh();
            }

            public void Quitar()
            {
                if (quitada)
                {
                    return;
                }

                quitada = true;
                tic.Stop();
                Cortina actual;
                if (enCurso.TryGetValue(vista, out actual) && actual == this)
                {
                    enCurso.Remove(vista);
                }

                if (Parent != null)
                {
                    Parent.Controls.Remove(this);
                }

                Dispose();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // Rapido al principio y suave al llegar (cubica).
                double t = Math.Min(1.0, reloj.ElapsedMilliseconds / (double)Duracion);
                double avance = 1 - Math.Pow(1 - t, 3);
                int ancho = Width;
                int corrido = (int)Math.Round(ancho * avance);
                e.Graphics.DrawImageUnscaled(antes, -direccion * corrido, 0);
                e.Graphics.DrawImageUnscaled(despues, direccion * (ancho - corrido), 0);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    tic.Dispose();
                    antes.Dispose();
                    despues.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }

    /// <summary>
    /// En lugar de una lista desplegable (el laboratorio de un horario), su nombre
    /// entre dos flechas: ◀ el anterior y ▶ el siguiente de la lista, en carrusel
    /// (2026-10-31): despues del ultimo viene el primero y antes del primero, el
    /// ultimo, en un solo paso y deslizandose hacia el lado de la flecha (antes,
    /// en las orillas la flecha se apagaba y para volver al primero habia que
    /// pasar por todos). Pulsar el nombre abre un menu con todos para saltar a
    /// uno. La lista sigue ahi, debajo (tapada, con su mismo lugar, tamano y
    /// visibilidad): la pantalla la llena y la lee como siempre. Con el teclado,
    /// ← y → (TeclasDeFlechas). Direccion dice, dentro del SelectedIndexChanged de
    /// quien la usa, hacia donde fue el cambio, para el deslizamiento (crear las
    /// flechas DESPUES de ese manejador).
    /// </summary>
    internal sealed class FlechasDeLista
    {
        private const int AnchoFlecha = 30;
        private const int Separacion = 4;
        private static readonly Color Verde = Color.FromArgb(0, 100, 0);
        // Una ayuda por selector, que se libera con el (2026-11-03): la de antes era
        // de todos (estatica) y nunca soltaba las flechas ni el nombre, asi que cada
        // ventana de Horarios, Estadisticas o Proximo ciclo cerrada se quedaba
        // entera en memoria (de la flecha a su Click, a esta, a la lista y a la ventana).
        private readonly ToolTip ayuda = new ToolTip();
        private readonly ComboBox lista;
        private readonly string queCambia;
        private readonly Panel selector = new Panel { BackColor = Color.White, TabStop = false };
        private readonly Button anterior;
        private readonly Button siguiente;
        private readonly Label nombre;
        private readonly Func<object, string> textoVisible;
        private int indiceAnterior;
        // Mientras Mover cambia la lista: hacia donde se pidio (-1 o 1). Al dar la
        // vuelta el indice va al reves (del ultimo al primero), pero el deslizamiento
        // sigue el lado de la flecha. 0 fuera de Mover.
        private int direccionPedida;

        public FlechasDeLista(ComboBox lista, string queCambia)
            : this(lista, queCambia, null)
        {
        }

        /// <param name="textoVisible">Lo que dice el nombre entre las flechas cuando no es el
        /// texto del elemento (null: el texto); la ayuda y el menu siguen con el texto entero.</param>
        public FlechasDeLista(ComboBox lista, string queCambia, Func<object, string> textoVisible)
        {
            this.lista = lista;
            this.queCambia = queCambia;
            this.textoVisible = textoVisible;
            lista.TabStop = false;
            anterior = Flecha("◀", queCambia + " anterior (←); del primero pasa al último");
            siguiente = Flecha("▶", queCambia + " siguiente (→); del último pasa al primero");
            anterior.Click += (s, e) => Mover(-1);
            siguiente.Click += (s, e) => Mover(1);
            nombre = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
                Font = new Font("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Verde,
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false,
                AccessibleRole = AccessibleRole.PushButton
            };
            nombre.Disposed += (s, e) => nombre.Font.Dispose();
            nombre.MouseEnter += (s, e) => nombre.Font = Cambiar(nombre.Font, FontStyle.Bold | FontStyle.Underline);
            nombre.MouseLeave += (s, e) => nombre.Font = Cambiar(nombre.Font, FontStyle.Bold);
            nombre.Click += (s, e) => AbrirMenu();
            selector.Controls.Add(anterior);
            selector.Controls.Add(nombre);
            selector.Controls.Add(siguiente);
            selector.Resize += (s, e) => Colocar();
            selector.Disposed += (s, e) => ayuda.Dispose();

            indiceAnterior = lista.SelectedIndex;
            lista.SelectedIndexChanged += (s, e) =>
            {
                indiceAnterior = lista.SelectedIndex;
                Pintar();
            };
            lista.LocationChanged += (s, e) => Seguir();
            lista.SizeChanged += (s, e) => Seguir();
            lista.VisibleChanged += (s, e) => Pintar();
            lista.EnabledChanged += (s, e) => Pintar();
            lista.TextChanged += (s, e) => Pintar();
            lista.ParentChanged += (s, e) => Poner();
            Poner();
            TeclasDeFlechas.Registrar(selector, false, Mover, lista);
        }

        /// <summary>
        /// Hacia donde va el cambio de la lista que se esta atendiendo: 1 al
        /// siguiente, -1 al anterior, 0 nada (tambien al llenarla o vaciarla: sin
        /// deslizamiento al abrir la pantalla). Con las flechas, el lado de la
        /// flecha, tambien al dar la vuelta; con el menu u otro cambio, el del indice.
        /// </summary>
        public int Direccion
        {
            get
            {
                if (indiceAnterior < 0 || lista.SelectedIndex < 0)
                {
                    return 0;
                }

                return direccionPedida != 0 ? direccionPedida : Math.Sign(lista.SelectedIndex - indiceAnterior);
            }
        }

        /// <summary>La orilla derecha del selector: lo que va a la derecha empieza despues.</summary>
        public int Derecha
        {
            get { return lista.Right; }
        }

        /// <summary>
        /// Los que las flechas y el menu se saltan en este momento, por su indice
        /// (en Estadisticas, "Todos los laboratorios" mientras se ven los Equipos).
        /// Si cambia lo que deja pasar, Refrescar.
        /// </summary>
        public Func<int, bool> Saltar { get; set; }

        /// <summary>
        /// Vuelve a pintar el nombre y las flechas: despues de llenar la lista otra
        /// vez (Items.Clear no avisa) o de cambiar lo que Saltar deja pasar.
        /// </summary>
        public void Refrescar()
        {
            indiceAnterior = lista.SelectedIndex;
            Pintar();
        }

        private static Font Cambiar(Font vieja, FontStyle estilo)
        {
            Font nueva = new Font(vieja, estilo);
            vieja.Dispose();
            return nueva;
        }

        private Button Flecha(string texto, string queHace)
        {
            Button flecha = EstiloHorarios.CrearBoton(texto, Point.Empty, new Size(AnchoFlecha, 26));
            flecha.Font = new Font("Segoe UI Symbol", 9F, FontStyle.Bold);
            flecha.FlatAppearance.BorderSize = 1;
            flecha.Padding = new Padding(0);
            flecha.TabStop = false;
            flecha.AccessibleName = queHace;
            flecha.Disposed += (s, e) => flecha.Font.Dispose();
            ayuda.SetToolTip(flecha, queHace);
            return flecha;
        }

        /// <summary>El selector, en el contenedor de la lista y encima de ella.</summary>
        private void Poner()
        {
            if (lista.Parent == null)
            {
                return;
            }

            if (selector.Parent != lista.Parent)
            {
                lista.Parent.Controls.Add(selector);
            }

            Seguir();
        }

        /// <summary>Donde este la lista (la pantalla la coloca), ahi el selector, tapandola.</summary>
        private void Seguir()
        {
            selector.Bounds = new Rectangle(lista.Left, lista.Top, lista.Width, Math.Max(26, lista.Height));
            selector.BringToFront();
            Pintar();
        }

        private void Colocar()
        {
            int alto = selector.Height;
            anterior.SetBounds(0, 0, AnchoFlecha, alto);
            siguiente.SetBounds(selector.Width - AnchoFlecha, 0, AnchoFlecha, alto);
            nombre.SetBounds(AnchoFlecha + Separacion, 0, Math.Max(10, selector.Width - 2 * (AnchoFlecha + Separacion)), alto);
        }

        private void Pintar()
        {
            selector.Visible = lista.Visible;
            object elegido = lista.SelectedItem;
            string texto = elegido == null ? "" : lista.GetItemText(elegido);
            string visible = elegido == null || textoVisible == null ? null : textoVisible(elegido);
            nombre.Text = visible ?? texto;
            nombre.AccessibleName = queCambia + ": " + texto + ". Pulsa para elegir otro.";
            ayuda.SetToolTip(nombre, texto.Length == 0 ? null : texto + Environment.NewLine + "Pulsa para elegir otro; ← y → para el anterior y el siguiente.");
            // En carrusel las dos flechas sirven siempre que haya otro a donde ir (si
            // lo hay hacia un lado, dando la vuelta tambien lo hay hacia el otro).
            bool hayOtro = Destino(1) >= 0;
            nombre.Enabled = hayOtro;
            anterior.Enabled = hayOtro;
            siguiente.Enabled = hayOtro;
        }

        /// <summary>
        /// El anterior (-1) o el siguiente (1), dando la vuelta: del ultimo al
        /// primero y del primero al ultimo, en un solo paso, sin pasar por los que
        /// Saltar se salta.
        /// </summary>
        private void Mover(int paso)
        {
            int nuevo = Destino(paso);
            if (nuevo < 0)
            {
                return;
            }

            direccionPedida = Math.Sign(paso);
            try
            {
                lista.SelectedIndex = nuevo;
            }
            finally
            {
                direccionPedida = 0;
            }
        }

        /// <summary>A donde lleva la flecha hacia ese lado (-1 o 1); -1 si no hay a donde ir.</summary>
        private int Destino(int paso)
        {
            int total = lista.Items.Count;
            int actual = lista.SelectedIndex;
            if (!lista.Enabled || total < 2 || actual < 0 || paso == 0)
            {
                return -1;
            }

            int lado = Math.Sign(paso);
            for (int corrido = 1; corrido < total; corrido++)
            {
                int indice = ((actual + lado * corrido) % total + total) % total;
                if (Saltar == null || !Saltar(indice))
                {
                    return indice;
                }
            }

            return -1;
        }

        /// <summary>Todos los de la lista (menos los que Saltar se salta), el de ahora marcado: elegir uno salta a el.</summary>
        private void AbrirMenu()
        {
            if (!lista.Enabled || lista.Items.Count == 0)
            {
                return;
            }

            ContextMenuStrip menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
            menu.Font = new Font("Century Gothic", 10F);
            for (int i = 0; i < lista.Items.Count; i++)
            {
                if (i != lista.SelectedIndex && Saltar != null && Saltar(i))
                {
                    continue;
                }

                int indice = i;
                ToolStripMenuItem opcion = new ToolStripMenuItem(lista.GetItemText(lista.Items[i])) { Checked = i == lista.SelectedIndex };
                opcion.Click += (s, e) => lista.SelectedIndex = indice;
                menu.Items.Add(opcion);
            }

            menu.Closed += (s, e) => selector.BeginInvoke(new Action(() =>
            {
                menu.Font.Dispose();
                menu.Dispose();
            }));
            menu.Show(nombre, new Point(0, nombre.Height));
        }
    }

    /// <summary>
    /// Un control que usa el mismo ← y → (sin Ctrl o con el): con el foco en el,
    /// TeclasDeFlechas no se las quita. La cuadricula de un horario que se edita
    /// (mueve el cuadro elegido) y el mes de Faltas (cambia de mes).
    /// </summary>
    internal interface IUsaFlechas
    {
        bool UsaFlechas(bool conCtrl);
    }

    /// <summary>
    /// ← y → del teclado en las pantallas de horarios: el laboratorio anterior o
    /// siguiente (FlechasDeLista) y, con Ctrl, la semana, el dia o el mes; donde no
    /// hay laboratorio (Mi horario), ← y → son la semana. Va a lo que se ve en la
    /// ventana activa (la pestana elegida). No se mete con lo que se escribe: en
    /// una caja de texto, una fecha, un numero o una lista abierta, las flechas
    /// siguen moviendo el cursor como siempre; con el foco en otra lista (el
    /// periodo o el docente de Estadisticas) o en unas opciones (Por semana y Por
    /// mes de Graficas), eligen en ellas; con el foco en un control que las usa
    /// (IUsaFlechas: la cuadricula de Horario semanal y de la preparacion de
    /// Proximo ciclo, el mes de Faltas), son de el (2026-11-03: antes cambiaban de
    /// laboratorio y se perdian los cuadros elegidos). Ctrl+← → siguen siendo la
    /// semana o el mes tambien desde la cuadricula.
    /// </summary>
    internal static class TeclasDeFlechas
    {
        private const int WM_KEYDOWN = 0x0100;
        private static readonly List<Entrada> entradas = new List<Entrada>();
        private static bool filtroPuesto;

        private sealed class Entrada
        {
            public Control Control;
            public bool ConCtrl;
            public Action<int> Mover;
            public ComboBox Lista;
        }

        /// <param name="control">Lo que tiene que verse para que las teclas vayan aqui.</param>
        /// <param name="conCtrl">True: con Ctrl+← y Ctrl+→; false: con ← y →.</param>
        /// <param name="lista">La lista que mueven (la de FlechasDeLista), si hay: con el foco en ella
        /// las teclas siguen viniendo aqui; con el foco en otra lista, son de esa.</param>
        public static void Registrar(Control control, bool conCtrl, Action<int> mover, ComboBox lista = null)
        {
            if (!filtroPuesto)
            {
                Application.AddMessageFilter(new Filtro());
                filtroPuesto = true;
            }

            Entrada entrada = new Entrada { Control = control, ConCtrl = conCtrl, Mover = mover, Lista = lista };
            entradas.Add(entrada);
            control.Disposed += (s, e) => entradas.Remove(entrada);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        private sealed class Filtro : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_KEYDOWN)
                {
                    return false;
                }

                Keys tecla = (Keys)(int)m.WParam & Keys.KeyCode;
                if (tecla != Keys.Left && tecla != Keys.Right)
                {
                    return false;
                }

                Keys modificadores = Control.ModifierKeys;
                if ((modificadores & (Keys.Alt | Keys.Shift)) != 0)
                {
                    return false;
                }

                Form activa = Form.ActiveForm;
                Control foco = Control.FromChildHandle(GetFocus());
                bool conCtrl = (modificadores & Keys.Control) != 0;
                if (activa == null || NoSeToca(foco, conCtrl))
                {
                    return false;
                }

                Entrada elegida = entradas.FirstOrDefault(e => e.ConCtrl == conCtrl && !e.Control.IsDisposed && e.Control.Visible
                    && e.Control.FindForm() == activa);
                if (elegida == null)
                {
                    return false;
                }

                elegida.Mover(tecla == Keys.Left ? -1 : 1);
                return true;
            }

            /// <summary>
            /// Con el foco ahi, las flechas son de ese control: lo que se escribe, otra lista, unas
            /// opciones (Por semana / Por mes de Graficas), donde ← y → eligen entre ellas, y lo que
            /// las usa (IUsaFlechas); con Ctrl, en las opciones siguen siendo de aqui (las secciones
            /// de Graficas).
            /// </summary>
            private static bool NoSeToca(Control foco, bool conCtrl)
            {
                return EsDeEscribir(foco) || EsOtraLista(foco) || (!conCtrl && foco is RadioButton) || LasUsa(foco, conCtrl);
            }

            /// <summary>Un control (o el que lo contiene, como una tabla a su celda) que usa esas flechas el mismo.</summary>
            private static bool LasUsa(Control foco, bool conCtrl)
            {
                IUsaFlechas propio = foco as IUsaFlechas ?? (foco == null ? null : foco.Parent as IUsaFlechas);
                return propio != null && propio.UsaFlechas(conCtrl);
            }

            /// <summary>Donde ← y → mueven el cursor o eligen: ahi no se tocan.</summary>
            private static bool EsDeEscribir(Control foco)
            {
                if (foco == null)
                {
                    return false;
                }

                ComboBox lista = foco as ComboBox;
                DataGridView tabla = foco as DataGridView ?? (foco.Parent as DataGridView);
                return foco is TextBoxBase || foco is UpDownBase || foco.Parent is UpDownBase || foco is DateTimePicker
                    || (lista != null && (lista.DropDownStyle != ComboBoxStyle.DropDownList || lista.DroppedDown))
                    || (tabla != null && tabla.IsCurrentCellInEditMode);
            }

            /// <summary>Una lista con el foco que no es la de unas flechas: ← y → eligen en ella, como siempre.</summary>
            private static bool EsOtraLista(Control foco)
            {
                ComboBox lista = foco as ComboBox;
                return lista != null && !entradas.Any(e => e.Lista == lista);
            }
        }
    }
}
