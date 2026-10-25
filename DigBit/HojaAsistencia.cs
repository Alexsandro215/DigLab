using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>Lo que el profesor pide hacer con la asistencia de hoy de un alumno (menu de su casilla).</summary>
    internal enum AccionAsistencia
    {
        /// <summary>Registro su bitacora pero no esta en el salon (db/17).</summary>
        Quitar,
        /// <summary>Se le quito y si esta en el salon (db/17).</summary>
        Devolver,
        /// <summary>Esta en el salon pero su equipo no pudo registrar su bitacora (db/22).</summary>
        Dar,
        /// <summary>Se la dio el profesor y ya no esta en el salon (db/22).</summary>
        QuitarLaQueDiste
    }

    /// <summary>
    /// La lista de asistencia de una clase como la hoja de Excel que el profesor
    /// ya conoce (lo pidio el usuario: "debe ser familiar"). Arriba, en una linea,
    /// el titulo y los datos de la lista: materia, grupo, profesor y periodo.
    /// Debajo, una fila por alumno con su numero de lista, su matricula y su
    /// nombre; una columna estrecha por clase, con la fecha en vertical, con A o
    /// F; y al final sus asistencias, sus faltas y su porcentaje. Letra Calibri,
    /// cuadricula gris, encabezado verde como el de Excel y las faltas con el
    /// formato condicional de siempre (relleno rojo claro con texto rojo oscuro).
    /// La ultima fila cuenta cuantos asistieron cada dia. Ctrl+C copia lo
    /// seleccionado, con sus encabezados, para pegarlo en Excel, y CopiarTodo
    /// ("Copiar la lista" de la ventana) la hoja entera, sin cambiar lo elegido.
    /// Pegadas a la cuadricula, la simbologia (solo lo que aplica a esa lista) y
    /// la ayuda, que dice como pasarla a Excel (en kiosco no: alli no hay Excel).
    ///
    /// Mientras dura la clase de hoy, la casilla de hoy de quien ya registro se
    /// pulsa para quitarle la asistencia (registro su bitacora pero no esta en el
    /// salon) o devolversela: lo hace quien la usa (CambiarAsistencia). La fila de
    /// quien registro sin estar se ve en naranja. Con db/22, la P de quien esta en
    /// el salon pero no pudo registrar se pulsa para darle la asistencia (su
    /// casilla se ve verde, como el estilo "Bueno" de Excel) y la verde, para
    /// quitarsela; debajo, un enlace da la asistencia a quien aun no esta en la
    /// lista (DarSinLista). La dada va como "A" al copiar y en el CSV, que al
    /// final dice cuales fueron, cuando y por que.
    ///
    /// Todo se coloca a mano (Acomodar): la cuadricula mide lo que su contenido,
    /// sin pasarse del alto que le dejan el membrete y lo de abajo. Desde fuera se
    /// marcan filas (MarcarAlumnos: los alumnos de una tarjeta del tablero); en
    /// cuanto el profesor pulsa o se mueve en la hoja, la marca se va (MarcaQuitada).
    /// </summary>
    internal class HojaAsistencia : Panel
    {
        /// <summary>El verde de Excel: encabezado y casilla activa.</summary>
        internal static readonly Color VerdeExcel = Color.FromArgb(33, 115, 70);
        private static readonly Color ColorCuadricula = Color.FromArgb(212, 212, 212);
        private static readonly Color BordeEncabezado = Color.FromArgb(25, 90, 55);
        // Los formatos condicionales de Excel: rojo claro, amarillo y naranja.
        internal static readonly Color FaltaFondo = Color.FromArgb(255, 199, 206);
        internal static readonly Color FaltaTexto = Color.FromArgb(156, 0, 6);
        internal static readonly Color HoyFondo = Color.FromArgb(255, 235, 156);
        internal static readonly Color HoyTexto = Color.FromArgb(156, 87, 0);
        internal static readonly Color RetiradaFondo = Color.FromArgb(248, 203, 173);
        internal static readonly Color RetiradaTexto = Color.FromArgb(131, 60, 12);
        internal static readonly Color FilaRetirada = Color.FromArgb(252, 228, 214);
        // La asistencia que dio el profesor sin bitacora (db/22): el estilo "Bueno" de Excel.
        internal static readonly Color DadaFondo = Color.FromArgb(198, 239, 206);
        internal static readonly Color DadaTexto = Color.FromArgb(0, 97, 0);
        private static readonly Color TotalFondo = Color.FromArgb(242, 242, 242);
        private static readonly Color TextoGris = Color.FromArgb(110, 110, 110);
        // Lo de alrededor de la cuadricula: el rotulo de la simbologia y lo que dice cada muestra.
        private static readonly Color TextoRotulo = Color.FromArgb(100, 100, 100);
        private static readonly Color TextoMuestra = Color.FromArgb(60, 60, 60);
        // Las barras de datos de Excel (verde y rojo) y su pista.
        internal static readonly Color BarraConDerecho = Color.FromArgb(99, 190, 123);
        internal static readonly Color BarraSinDerecho = Color.FromArgb(248, 105, 107);
        private static readonly Color PistaBarra = Color.FromArgb(236, 236, 236);
        private static readonly Color TextoConDerecho = Color.FromArgb(0, 97, 0);
        // Las lineas que separan los bloques: nombres, clases, asistencias, faltas, % y la barra.
        private static readonly Color Divisoria = Color.FromArgb(128, 128, 128);
        private const int GruesoDivisoria = 3;
        /// <summary>Desde aqui, con derecho a evaluacion; por debajo, el porcentaje y la barra salen en rojo.</summary>
        private const int MinimoDeAsistencia = ResumenAsistencia.Minimo;
        private const int AnchoBarra = 200;
        private const int AnchoDia = 32;
        // Lo que separa las piezas, de arriba abajo: membrete, cuadricula, simbologia (o el aviso) y ayuda.
        private const int AntesDeLaCuadricula = 8;
        private const int AntesDeLaSimbologia = 8;
        private const int AntesDeLaAyuda = 4;
        private const string TextoSinAlumnos = "Aún no hay estudiantes: la lista se arma con la primera clase en que registren su bitácora.";
        private const string TextoSinAlumnosConDadas = "Aún no hay estudiantes: la lista se arma con la primera clase en que registren su bitácora o les des la asistencia.";
        // La ayuda de debajo de la simbologia, segun lo que la base deja cambiar (db/17 quitar, db/22 dar).
        private const string AyudaQuitar = "Mientras dura la clase de hoy, pulsa la casilla de hoy de quien registró sin estar en el salón "
            + "para quitarle la asistencia: su fila se pinta de naranja. Vuelve a pulsarla para devolvérsela.";
        // Sin "su bitacora" tras "registrar": asi cabe en un renglon a 1920 (1460 px de 1518).
        private const string AyudaQuitarYDar = "Mientras dura la clase, pulsa la casilla de hoy de un estudiante: si registró sin estar en el salón, "
            + "quítale la asistencia (su fila se pinta de naranja); si está en el salón y su equipo no pudo registrar, "
            + "dásela (su casilla se pinta de verde). Vuelve a pulsarla para deshacerlo.";
        private const string AyudaDar = "Mientras dura la clase, pulsa la P de quien está en el salón pero su equipo no pudo registrar su bitácora "
            + "para darle la asistencia: su casilla se pinta de verde. Vuelve a pulsarla para quitársela.";
        internal const string TextoDarSinLista = "Dar asistencia a alguien que no está en la lista...";
        // Lo mas que se ensena de un detalle en el menu de la casilla: el menu no crece sin fin.
        private const int DetalleEnMenu = 60;
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

        private readonly DrawingFont fuente = new DrawingFont("Calibri", 11F);
        private readonly DrawingFont fuenteNegrita = new DrawingFont("Calibri", 11F, FontStyle.Bold);
        private readonly DrawingFont fuenteTitulo = new DrawingFont("Calibri", 14F, FontStyle.Bold);
        private readonly DrawingFont fuenteChica = new DrawingFont("Calibri", 10F);
        private readonly DrawingFont fuenteRotulo = new DrawingFont("Calibri", 9F, FontStyle.Bold);
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };

        private readonly FlowLayoutPanel membrete;
        private readonly Texto lblTitulo;
        private readonly Cuadricula grid;
        private readonly FlowLayoutPanel simbologia;
        private readonly FlowLayoutPanel muestraPendiente;
        private readonly FlowLayoutPanel muestraRetirada;
        // La A verde (db/22): si alguien la tiene, o si hoy se puede dar.
        private readonly FlowLayoutPanel muestraDada;
        private readonly Texto lblComoQuitar;
        // "Dar asistencia a alguien que no esta en la lista..." (db/22, mientras dura la clase).
        private readonly LinkLabel lnkDarSinLista;
        // Como pasarla a Excel: con la simbologia (hay alumnos), debajo de todo.
        private readonly Texto lblComoCopiar;
        private readonly Texto lblSinAlumnos;
        private readonly ContextMenuStrip menu;

        private readonly DrawingFont fuenteMenu = new DrawingFont("Century Gothic", 9.5F);
        private readonly DrawingFont fuenteMenuNegrita = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);

        private AsistenciaDeClase asistencia;
        // El docente del membrete de la lista que se ve (para el CSV).
        private string docente = "";
        private DiaDeAsistencia hoy;
        private AlumnoDeAsistencia alumnoDelMenu;
        private string datosPuestos;
        // Las columnas tras las que va una linea gruesa (ver Mostrar).
        private HashSet<string> divisorias = new HashSet<string>();
        private readonly DrawingFont fuenteBarra = new DrawingFont("Calibri", 10F, FontStyle.Bold);
        // Lo que va debajo de la cuadricula. No se lee de Visible: con la hoja
        // oculta, Visible dice false aunque se vaya a ver al mostrarla.
        private bool verSimbologia;
        private bool verAyuda;
        private bool verAviso;
        private bool verEnlace;
        // Lo que GDI deja a la izquierda del texto del enlace (LinkLabel no tiene NoPadding): el
        // enlace va tanto mas a la izquierda, para que su texto empiece donde los demas (-1: sin medir).
        private int sangriaEnlace = -1;
        // Se lee otra lista: la cuadricula conserva la caja de la anterior (cajaPuesta: ya hubo una).
        private bool cargando;
        private bool cajaPuesta;
        // La seleccion la cambia la hoja o quien la usa, no el profesor: no se avisa MarcaQuitada.
        private bool marcando;
        // El color que el profesor eligio para la clase (db/19), o null: sin cinta.
        private Color? colorDeLaClase;

        /// <summary>El profesor quiere quitar, devolver, dar o anular (la que dio) la asistencia de hoy a ese alumno.</summary>
        public event Action<AlumnoDeAsistencia, DateTime, AccionAsistencia> CambiarAsistencia;

        /// <summary>El profesor quiere dar la asistencia de hoy a alguien que aun no esta en la lista (por su matricula).</summary>
        public event Action DarSinLista;

        /// <summary>
        /// El profesor pulso o se movio en la hoja (cambio la seleccion): la marca
        /// puesta con MarcarAlumnos ya no esta.
        /// </summary>
        public event Action MarcaQuitada;

        public HojaAsistencia()
        {
            BackColor = Color.White;

            // El membrete en una linea: el titulo y cada dato como un par que no se
            // parte; si no caben, los que sobran bajan a un segundo renglon desde x 0.
            membrete = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Location = Point.Empty
            };
            lblTitulo = new Texto
            {
                Font = fuenteTitulo,
                ForeColor = VerdeExcel,
                Margin = new Padding(0, 0, 24, 0),
                Text = "LISTA DE ASISTENCIA"
            };
            membrete.Controls.Add(lblTitulo);

            // La hoja mide lo que su contenido (sin un hueco entre la ultima columna
            // y la barra, ni debajo de la ultima fila); alrededor queda la pagina en blanco.
            grid = new Cuadricula
            {
                Location = Point.Empty,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                // En el kiosco la lista no sale del equipo: ni "Copiar la lista" ni Ctrl+C.
                ClipboardCopyMode = Kiosco.Activo ? DataGridViewClipboardCopyMode.Disable : DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                CellBorderStyle = DataGridViewCellBorderStyle.Single,
                GridColor = ColorCuadricula,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 74,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                EnableHeadersVisualStyles = false,
                Font = fuente,
                ShowCellToolTips = true,
                StandardTab = true,
                ScrollBars = ScrollBars.Both
            };
            grid.RowTemplate.Height = 21;
            grid.DefaultCellStyle.Font = fuente;
            grid.DefaultCellStyle.ForeColor = Color.Black;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = Color.White;
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.DefaultCellStyle.Padding = new Padding(3, 0, 3, 0);
            grid.ColumnHeadersDefaultCellStyle.Font = fuenteNegrita;
            grid.ColumnHeadersDefaultCellStyle.BackColor = VerdeExcel;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.CellPainting += PintarCelda;
            grid.CellMouseClick += CasillaPulsada;
            // La casilla activa se dibuja con su marco verde: al moverse, se repinta.
            grid.CurrentCellChanged += (s, e) => grid.Invalidate();
            grid.Enter += (s, e) => grid.Invalidate();
            grid.Leave += (s, e) => grid.Invalidate();
            // Si la seleccion cambia y no fue la hoja, fue el profesor: la marca se va.
            grid.SelectionChanged += (s, e) =>
            {
                if (!marcando && MarcaQuitada != null)
                {
                    MarcaQuitada();
                }
            };

            // La simbologia: su rotulo y solo las muestras que aplican (ver PonerSimbologia).
            // Si no cabe en un renglon, en dos, sin separar una casilla de lo que dice.
            simbologia = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Visible = false
            };
            simbologia.Controls.Add(new Texto
            {
                Font = fuenteRotulo,
                ForeColor = TextoRotulo,
                Margin = new Padding(0, 3, 10, 0),
                Text = "SIMBOLOGÍA"
            });
            Muestra("A", Color.White, Color.Black, "Registró su bitácora");
            muestraDada = Muestra("A", DadaFondo, DadaTexto, "Se la diste sin bitácora");
            Muestra("F", FaltaFondo, FaltaTexto, "Falta");
            muestraPendiente = Muestra("P", HoyFondo, HoyTexto, "Pendiente: aún no registra");
            muestraRetirada = Muestra("F", RetiradaFondo, RetiradaTexto, "Registró sin estar en el salón");
            Muestra("", BarraConDerecho, Color.White, "Con derecho (" + MinimoDeAsistencia + " % o más)");
            FlowLayoutPanel ultimaMuestra = Muestra("", BarraSinDerecho, Color.White, "Sin derecho (menos del " + MinimoDeAsistencia + " %)");
            // La ultima, sin el hueco de detras: el renglon mide lo que se ve.
            ultimaMuestra.Margin = new Padding(0);

            // La ayuda va aparte, debajo de la simbologia: si no cabe, se parte en renglones.
            lblComoQuitar = new Texto
            {
                Partir = true,
                Font = fuenteChica,
                ForeColor = TextoGris,
                Text = AyudaQuitar,
                Visible = false
            };
            // En su propio renglon, debajo de la ayuda (o del aviso sin estudiantes).
            lnkDarSinLista = new LinkLabel
            {
                AutoSize = true,
                Font = fuenteChica,
                LinkColor = VerdeExcel,
                ActiveLinkColor = Color.DarkGreen,
                VisitedLinkColor = VerdeExcel,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Text = TextoDarSinLista,
                UseMnemonic = false,
                Visible = false
            };
            lnkDarSinLista.LinkClicked += (s, e) =>
            {
                if (DarSinLista != null)
                {
                    DarSinLista();
                }
            };
            ayuda.SetToolTip(lnkDarSinLista, "Para quien está en el salón, no pudo registrar su bitácora y aún no está en la lista "
                + "(es su primera clase contigo): escribe su matrícula, comprueba su nombre y dale la asistencia de hoy.");
            lblComoCopiar = new Texto
            {
                Partir = true,
                Font = fuenteChica,
                ForeColor = TextoGris,
                Text = "Para pasarla a Excel, pulsa «Copiar la lista» y pégala, o guárdala en un archivo con «Guardar en Excel (CSV)...»; "
                    + "Ctrl+C copia solo las casillas que elijas.",
                Visible = false
            };
            // Sin alumnos, en lugar de la simbologia.
            lblSinAlumnos = new Texto
            {
                Partir = true,
                Font = fuente,
                ForeColor = TextoGris,
                Text = TextoSinAlumnos,
                Visible = false
            };

            menu = new ContextMenuStrip { Font = fuenteMenu };

            Controls.AddRange(new Control[] { membrete, grid, simbologia, lblComoQuitar, lnkDarSinLista, lblComoCopiar, lblSinAlumnos });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                ayuda.Dispose();
                menu.Dispose();
                fuente.Dispose();
                fuenteNegrita.Dispose();
                fuenteTitulo.Dispose();
                fuenteChica.Dispose();
                fuenteRotulo.Dispose();
                fuenteMenu.Dispose();
                fuenteMenuNegrita.Dispose();
                fuenteBarra.Dispose();
            }
        }

        /// <summary>
        /// Un bloque de la clase de hoy esta en curso: se puede quitar, devolver o
        /// dar la asistencia (lo decide la ventana; en el hueco entre dos bloques,
        /// no). Quien decide de verdad es el servidor, con su reloj.
        /// </summary>
        public bool PuedeCambiar { get; set; } = true;

        /// <summary>Hay un menu abierto: no conviene rehacer la hoja debajo de el.</summary>
        public bool MenuAbierto
        {
            get { return menu.Visible; }
        }

        /// <summary>
        /// El color que el profesor eligio para la clase (db/19): una cinta de 4 px
        /// con 1 px de su color oscurecido debajo, en el hueco entre el membrete y
        /// la cuadricula, del ancho de esta. No cambia nada mas de la hoja (ni el
        /// membrete, oficial, ni lo que se copia). Null: sin cinta, como siempre.
        /// </summary>
        public Color? ColorDeLaClase
        {
            get
            {
                return colorDeLaClase;
            }

            set
            {
                if (colorDeLaClase == value)
                {
                    return;
                }

                colorDeLaClase = value;
                Invalidate(new Rectangle(0, membrete.Bottom, ClientSize.Width, AntesDeLaCuadricula));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (colorDeLaClase.HasValue && grid.Width > 0)
            {
                Color color = colorDeLaClase.Value;
                using (SolidBrush cinta = new SolidBrush(color))
                using (SolidBrush linea = new SolidBrush(CirculoClase.ColorDelBorde(color)))
                {
                    e.Graphics.FillRectangle(cinta, grid.Left, membrete.Bottom + 2, grid.Width, 4);
                    e.Graphics.FillRectangle(linea, grid.Left, membrete.Bottom + 6, grid.Width, 1);
                }
            }
        }

        /// <summary>
        /// Una muestra de la simbologia: la casilla con su formato y que quiere decir,
        /// juntas en un par que no se separa al pasar al segundo renglon.
        /// </summary>
        private FlowLayoutPanel Muestra(string texto, Color fondo, Color letra, string descripcion)
        {
            Label casilla = new Label
            {
                AutoSize = false,
                BackColor = fondo,
                BorderStyle = BorderStyle.FixedSingle,
                Font = fuenteNegrita,
                ForeColor = letra,
                Margin = new Padding(0, 0, 6, 0),
                // Sin texto: una muestra de la barra.
                Size = new Size(texto.Length == 0 ? 40 : texto.Length > 1 ? 44 : 26, 20),
                Text = texto,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Texto queEs = new Texto
            {
                Font = fuenteChica,
                ForeColor = TextoMuestra,
                Margin = new Padding(0, 2, 0, 0),
                Text = descripcion
            };
            FlowLayoutPanel par = Par(casilla, queEs, new Padding(0, 0, 22, 0));
            simbologia.Controls.Add(par);
            return par;
        }

        /// <summary>Dos piezas que van juntas: el renglon se parte antes o despues de ellas, nunca en medio.</summary>
        private static FlowLayoutPanel Par(Control primera, Control segunda, Padding margen)
        {
            FlowLayoutPanel par = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = margen,
                Padding = new Padding(0)
            };
            par.Controls.Add(primera);
            par.Controls.Add(segunda);
            return par;
        }

        // =====================================================================
        // Contenido
        // =====================================================================

        /// <summary>
        /// Mientras se lee: el membrete con lo que se sabe y la cuadricula vacia. La
        /// cuadricula conserva la caja de la lista anterior y la simbologia se queda
        /// donde esta: al llegar la lista nueva todo se ajusta una sola vez, y al
        /// cambiar de grupo nada salta dos veces.
        /// </summary>
        public void MostrarCargando(string materia, string grupo, string profesor)
        {
            marcando = true;
            try
            {
                asistencia = null;
                hoy = null;
                cargando = true;
                PonerDatos(materia, grupo, profesor, null);
                grid.Columns.Clear();
                grid.Rows.Clear();
                // La ayuda, el enlace y el aviso eran de la lista anterior; quitarlos no mueve nada.
                verAyuda = false;
                lblComoQuitar.Visible = false;
                verEnlace = false;
                lnkDarSinLista.Visible = false;
                verAviso = false;
                lblSinAlumnos.Visible = false;
                Acomodar();
            }
            finally
            {
                marcando = false;
            }
        }

        /// <summary>
        /// Un problema, en rojo, donde va la simbologia. La ventana ya no lo usa (lo
        /// dice su tablero); se conserva por si otra pantalla lo necesita.
        /// </summary>
        public void MostrarError(string texto)
        {
            verSimbologia = false;
            simbologia.Visible = false;
            lblComoCopiar.Visible = false;
            verAyuda = false;
            lblComoQuitar.Visible = false;
            verEnlace = false;
            lnkDarSinLista.Visible = false;
            lblSinAlumnos.ForeColor = FaltaTexto;
            lblSinAlumnos.Text = texto ?? "";
            verAviso = true;
            lblSinAlumnos.Visible = true;
            Acomodar();
        }

        /// <summary>Materia, grupo, profesor y periodo, como los datos de arriba de la hoja.</summary>
        private void PonerDatos(string materia, string grupo, string profesor, string periodo)
        {
            // Se rehace cada 30 s con la clase en curso: solo si cambia algo.
            string clave = materia + "|" + grupo + "|" + profesor + "|" + periodo;
            if (clave == datosPuestos)
            {
                return;
            }

            datosPuestos = clave;
            membrete.SuspendLayout();
            foreach (Control viejo in membrete.Controls.Cast<Control>().Where(c => c != lblTitulo).ToList())
            {
                foreach (Control dentro in viejo.Controls)
                {
                    ayuda.SetToolTip(dentro, null);
                }

                viejo.Dispose();
            }

            Dato("Materia:", materia);
            Dato("Grupo:", grupo);
            if (!string.IsNullOrWhiteSpace(profesor))
            {
                Dato("Docente:", profesor);
            }

            if (periodo != null)
            {
                Dato("Periodo:", periodo);
            }

            // El ultimo, sin el hueco de detras: el renglon mide lo que se ve (con
            // Programacion Orientada a Objetos, 1042 px, que caben en los 1058 de 1366x768).
            membrete.Controls[membrete.Controls.Count - 1].Margin = new Padding(0, 4, 0, 0);
            membrete.ResumeLayout();
        }

        /// <summary>Un dato del membrete: su rotulo en negrita y su valor, juntos en un par.</summary>
        private void Dato(string rotulo, string valor)
        {
            Texto lblRotulo = new Texto { Font = fuenteNegrita, ForeColor = Color.Black, Margin = new Padding(0, 0, 2, 0), Text = rotulo };
            Texto lblValor = new Texto { Font = fuente, ForeColor = Color.Black, Margin = new Padding(0), Text = valor ?? "" };
            membrete.Controls.Add(Par(lblRotulo, lblValor, new Padding(0, 4, 26, 0)));
        }

        /// <summary>La hoja de esa clase. Conserva lo que se veia (desplazamiento y casilla activa) si es la misma.</summary>
        public void Mostrar(AsistenciaDeClase leida, string profesor)
        {
            // Lo que aqui cambia la seleccion no lo hace el profesor: no quita la marca
            // (quien la usa la vuelve a poner con la lista nueva).
            marcando = true;
            try
            {
                Llenar(leida, profesor);
            }
            finally
            {
                marcando = false;
            }
        }

        /// <summary>El id del alumno de esa fila de la cuadricula, o null (la fila Asistieron, o fuera de rango).</summary>
        private int? IdDeLaFila(int fila)
        {
            if (fila < 0 || fila >= grid.Rows.Count)
            {
                return null;
            }

            AlumnoDeAsistencia alumno = grid.Rows[fila].Tag as AlumnoDeAsistencia;
            return alumno == null ? (int?)null : alumno.Id;
        }

        /// <summary>La fila de ese alumno en la cuadricula nueva; si ya no esta (o no habia), el mismo numero de antes.</summary>
        private int FilaDe(int? idAlumno, int antes)
        {
            if (idAlumno.HasValue)
            {
                foreach (DataGridViewRow fila in grid.Rows)
                {
                    AlumnoDeAsistencia alumno = fila.Tag as AlumnoDeAsistencia;
                    if (alumno != null && alumno.Id == idAlumno.Value)
                    {
                        return fila.Index;
                    }
                }
            }

            return antes;
        }

        private void Llenar(AsistenciaDeClase leida, string profesor)
        {
            bool misma = asistencia != null && asistencia.ClaseId == leida.ClaseId;
            int primeraFila = misma ? grid.FirstDisplayedScrollingRowIndex : -1;
            int desplazamiento = misma ? grid.HorizontalScrollingOffset : 0;
            Point activa = misma && grid.CurrentCell != null ? new Point(grid.CurrentCell.ColumnIndex, grid.CurrentCell.RowIndex) : new Point(-1, -1);
            // De quien eran esas filas: si alguien entra a la lista (o sale), la fila elegida y la de
            // arriba siguen siendo las de esos estudiantes, no las del mismo numero de renglon.
            int? idActiva = activa.Y >= 0 ? IdDeLaFila(activa.Y) : null;
            bool activaEnTotales = activa.Y >= 0 && activa.Y == grid.Rows.Count - 1 && idActiva == null;
            int? idArriba = primeraFila >= 0 ? IdDeLaFila(primeraFila) : null;

            cargando = false;
            asistencia = leida;
            docente = profesor ?? "";
            hoy = leida.Dias.LastOrDefault(d => d.EnCurso);
            int total = leida.Dias.Count;
            // Lo que cubren sus columnas: hasta hoy si hoy cuenta, o hasta su ultima clase.
            DateTime? ultima = total == 0 ? (DateTime?)null : leida.Dias[total - 1].Fecha;
            PonerDatos(leida.Materia, leida.Grupo, profesor,
                ultima == null ? "desde el " + leida.VigenteDesde.ToString("dd/MM/yyyy")
                : "del " + leida.VigenteDesde.ToString("dd/MM/yyyy") + (ultima.Value == leida.AhoraServidor.Date ? " a hoy" : " al " + ultima.Value.ToString("dd/MM/yyyy")));
            PonerSimbologia(leida);

            grid.SuspendLayout();
            grid.Columns.Clear();
            grid.Rows.Clear();
            grid.Columns.Add(Columna("no", "No.", 40, DataGridViewContentAlignment.MiddleCenter, true));
            grid.Columns.Add(Columna("matricula", "Matrícula", 112, DataGridViewContentAlignment.MiddleLeft, true));
            grid.Columns.Add(Columna("alumno", "Nombre completo", 260, DataGridViewContentAlignment.MiddleLeft, true));
            for (int i = 0; i < total; i++)
            {
                DiaDeAsistencia dia = leida.Dias[i];
                DataGridViewTextBoxColumn columna = Columna("d" + i, EncabezadoDe(dia), AnchoDia, DataGridViewContentAlignment.MiddleCenter, false);
                columna.Tag = dia;
                columna.DefaultCellStyle.Font = fuenteNegrita;
                columna.ToolTipText = dia.EnCurso
                    ? "Hoy, " + dia.Fecha.ToString("dddd dd/MM/yyyy", Espanol) + ". La clase sigue: P es «pendiente» (aún no registra) y no cuenta como falta mientras dure."
                    : Espanol.TextInfo.ToTitleCase(dia.Fecha.ToString("dddd dd/MM/yyyy", Espanol)) + ", desde las " + HorariosDatos.Hora(dia.Inicio) + ".";
                grid.Columns.Add(columna);
            }

            grid.Columns.Add(Columna("asist", "Asist.", 56, DataGridViewContentAlignment.MiddleCenter, false));
            grid.Columns.Add(Columna("faltas", "Faltas", 56, DataGridViewContentAlignment.MiddleCenter, false));
            grid.Columns.Add(Columna("porciento", "%", 56, DataGridViewContentAlignment.MiddleCenter, false));
            DataGridViewTextBoxColumn derecho = Columna("derecho", "Derecho a evaluación (" + MinimoDeAsistencia + " %)", AnchoBarra,
                DataGridViewContentAlignment.MiddleLeft, false);
            derecho.ToolTipText = "Asistencia total de cada estudiante. Con " + MinimoDeAsistencia + " % o más tiene derecho a evaluación; por debajo, la barra sale en rojo.";
            grid.Columns.Add(derecho);

            // Donde van las lineas gruesas: tras los nombres, tras las clases, tras Asist., tras Faltas y tras el %.
            divisorias = new HashSet<string> { "alumno", "asist", "faltas", "porciento" };
            if (total > 0)
            {
                divisorias.Add("d" + (total - 1));
            }

            int numero = 0;
            int[] asistieron = new int[total];
            // De ellos, a cuantos se la dio el profesor sin bitacora (db/22).
            int[] dadas = new int[total];
            int asistenciasDelGrupo = 0;
            int faltasDelGrupo = 0;
            foreach (AlumnoDeAsistencia alumno in leida.Alumnos)
            {
                numero++;
                DataGridViewRow fila = grid.Rows[grid.Rows.Add()];
                fila.Tag = alumno;
                // La ayuda del nombre: los dias en que registro sin estar (su fila va en
                // naranja) y en los que le dio la asistencia sin bitacora (su fila no se pinta).
                List<string> delNombre = new List<string>();
                if (alumno.Retiradas.Count > 0)
                {
                    fila.DefaultCellStyle.BackColor = FilaRetirada;
                    fila.DefaultCellStyle.SelectionBackColor = FilaRetirada;
                    delNombre.Add("Registró su bitácora sin estar en el salón: "
                        + string.Join(", ", alumno.Retiradas.OrderBy(d => d).Select(d => d.ToString("dd/MM"))) + ".");
                }

                if (alumno.SinBitacora.Count > 0)
                {
                    delNombre.Add("Le diste la asistencia sin bitácora: "
                        + string.Join(", ", alumno.SinBitacora.OrderBy(d => d).Select(d => d.ToString("dd/MM"))) + ".");
                }

                if (delNombre.Count > 0)
                {
                    fila.Cells["alumno"].ToolTipText = string.Join(Environment.NewLine, delNombre);
                }

                fila.Cells["no"].Value = numero;
                fila.Cells["matricula"].Value = alumno.Matricula;
                fila.Cells["alumno"].Value = alumno.Nombre;
                // Su primer dia en la clase (bitacora o asistencia dada): antes aun no estaba en la lista.
                DateTime entro = alumno.Entro;
                int vino = 0;
                int falto = 0;
                for (int i = 0; i < total; i++)
                {
                    DiaDeAsistencia dia = leida.Dias[i];
                    DataGridViewCell celda = fila.Cells["d" + i];
                    AsistenciaDada dada;
                    alumno.Dadas.TryGetValue(dia.Fecha, out dada);
                    // Hoy, mientras dura un bloque: lo que deja la base (db/17 quitar y devolver, db/22 dar y anular).
                    bool cambiable = dia.EnCurso && PuedeCambiar;
                    if (alumno.Presente.Contains(dia.Fecha) && alumno.SinBitacora.Contains(dia.Fecha))
                    {
                        // Se la dio el profesor y no hay bitacora que cuente: la A verde. La fila no se pinta.
                        vino++;
                        asistieron[i]++;
                        dadas[i]++;
                        Formato(celda, "A", DadaFondo, DadaTexto);
                        celda.ToolTipText = (dada == null ? "Le diste la asistencia." : QueLeDiste(dada)) + " No registró su bitácora."
                            + (cambiable && leida.ConDadas ? " Pulsa aquí para quitársela." : "");
                    }
                    else if (alumno.Presente.Contains(dia.Fecha))
                    {
                        vino++;
                        asistieron[i]++;
                        celda.Value = "A";
                        celda.ToolTipText = (dada == null ? "Registró su bitácora."
                                : "Registró su bitácora; también le diste la asistencia a las " + Hora(dada.DadaEn) + " (cuenta una sola vez).")
                            + (cambiable && leida.ConRetiro ? " Si no está en el salón, pulsa aquí para quitarle la asistencia." : "");
                    }
                    else if (alumno.Retiradas.Contains(dia.Fecha))
                    {
                        falto++;
                        Formato(celda, "F", RetiradaFondo, RetiradaTexto);
                        celda.ToolTipText = "Registró su bitácora pero no estaba en el salón: se le quitó la asistencia."
                            + (cambiable && leida.ConRetiro ? " Pulsa aquí para devolvérsela." : "");
                    }
                    else if (dia.EnCurso)
                    {
                        // Pendiente: aun no se sabe; no cuenta como falta ni en el % mientras la clase dure.
                        Formato(celda, "P", HoyFondo, HoyTexto);
                        celda.ToolTipText = "Pendiente: aún no registra su bitácora y la clase sigue. Si termina sin registrar, será falta."
                            + (cambiable && leida.ConDadas ? " Si está en el salón y su equipo no pudo registrar su bitácora, pulsa aquí para darle la asistencia." : "");
                    }
                    else
                    {
                        falto++;
                        Formato(celda, "F", FaltaFondo, FaltaTexto);
                        celda.ToolTipText = entro != DateTime.MaxValue && entro > dia.Fecha
                            ? "Entró a la lista el " + entro.ToString("dd/MM") + ": en las clases de antes cuenta como falta."
                            : "No registró su bitácora: falta.";
                    }
                }

                asistenciasDelGrupo += vino;
                faltasDelGrupo += falto;
                fila.Cells["asist"].Value = vino;
                fila.Cells["faltas"].Value = falto;
                PonerPorcentaje(fila, vino, falto, false);
            }

            // La fila de abajo: cuantos asistieron cada dia.
            if (leida.Alumnos.Count > 0)
            {
                DataGridViewRow fila = grid.Rows[grid.Rows.Add()];
                fila.Tag = "total";
                fila.DefaultCellStyle.BackColor = TotalFondo;
                fila.DefaultCellStyle.SelectionBackColor = TotalFondo;
                fila.DefaultCellStyle.Font = fuenteNegrita;
                fila.Cells["alumno"].Value = "Asistieron";
                fila.Cells["alumno"].Style.Alignment = DataGridViewContentAlignment.MiddleRight;
                for (int i = 0; i < total; i++)
                {
                    fila.Cells["d" + i].Value = asistieron[i];
                    fila.Cells["d" + i].ToolTipText = AyudaAsistieron(asistieron[i], dadas[i], leida.Alumnos.Count);
                }

                fila.Cells["asist"].Value = asistenciasDelGrupo;
                fila.Cells["faltas"].Value = faltasDelGrupo;
                PonerPorcentaje(fila, asistenciasDelGrupo, faltasDelGrupo, true);
                fila.Cells["porciento"].ToolTipText = "Asistencia del grupo en todas sus clases (sin contar lo pendiente de hoy).";
            }

            AutoajustarNombre();
            Acomodar();
            grid.ResumeLayout();

            // Lo que se veia, si es la misma clase; si no, de arriba y con las clases mas
            // recientes a la vista. La casilla activa va antes que el desplazamiento:
            // ponerla lleva la hoja hasta ella, y lo que se veia es lo que manda.
            grid.CurrentCell = null;
            grid.ClearSelection();
            if (misma && activa.Y >= 0)
            {
                activa.Y = activaEnTotales ? grid.Rows.Count - 1 : FilaDe(idActiva, activa.Y);
            }

            if (misma && primeraFila >= 0)
            {
                primeraFila = FilaDe(idArriba, primeraFila);
            }

            if (misma && activa.X >= 0 && activa.Y >= 0 && activa.Y < grid.Rows.Count && activa.X < grid.Columns.Count)
            {
                grid.CurrentCell = grid[activa.X, activa.Y];
                grid.ClearSelection();
            }

            if (primeraFila >= 0 && primeraFila < grid.Rows.Count)
            {
                grid.FirstDisplayedScrollingRowIndex = primeraFila;
            }

            if (misma)
            {
                grid.HorizontalScrollingOffset = desplazamiento;
            }
            else if (total > 0)
            {
                grid.FirstDisplayedScrollingColumnIndex = grid.Columns.Count - 1;
            }
        }

        /// <summary>
        /// La simbologia, con solo lo que aplica a esta lista (sin alumnos, el aviso en
        /// su lugar); la ayuda para cambiar la asistencia de hoy, segun lo que deja la
        /// base (db/17 quitar, db/22 dar); y, con db/22 y mientras dura la clase, el
        /// enlace para dar la asistencia a quien aun no esta en la lista.
        /// </summary>
        private void PonerSimbologia(AsistenciaDeClase leida)
        {
            bool conAlumnos = leida.Alumnos.Count > 0;
            // P, solo mientras sigue la clase; la naranja, si alguien la tiene o si hoy se puede quitar;
            // la verde, si alguien la tiene o si hoy se puede dar.
            muestraPendiente.Visible = hoy != null;
            muestraRetirada.Visible = leida.Alumnos.Any(a => a.Retiradas.Count > 0) || (hoy != null && leida.ConRetiro);
            muestraDada.Visible = leida.Alumnos.Any(a => a.SinBitacora.Count > 0) || (hoy != null && leida.ConDadas);
            verSimbologia = conAlumnos;
            simbologia.Visible = conAlumnos;
            // Como pasarla a Excel va y viene con la simbologia: con alumnos, hay que copiar.
            // En kiosco no: alli no hay donde pegarla y no se ofrece copiar (VentanaMisGrupos).
            lblComoCopiar.Visible = conAlumnos && !Kiosco.Activo;
            verAviso = !conAlumnos;
            lblSinAlumnos.ForeColor = TextoGris;
            lblSinAlumnos.Text = leida.ConDadas ? TextoSinAlumnosConDadas : TextoSinAlumnos;
            lblSinAlumnos.Visible = !conAlumnos;
            verAyuda = hoy != null && (leida.ConRetiro || leida.ConDadas) && PuedeCambiar && conAlumnos;
            lblComoQuitar.Text = leida.ConRetiro && leida.ConDadas ? AyudaQuitarYDar : leida.ConDadas ? AyudaDar : AyudaQuitar;
            lblComoQuitar.Visible = verAyuda;
            // Haya o no estudiantes: sin lista, es la primera clase y no hay casilla que pulsar.
            verEnlace = hoy != null && leida.ConDadas && PuedeCambiar;
            lnkDarSinLista.Visible = verEnlace;
        }

        /// <summary>"Le diste la asistencia a las 10:42: su equipo no tenia internet (PC-07)." (con "otro", el detalle).</summary>
        private static string QueLeDiste(AsistenciaDada dada)
        {
            string equipo = AsistenciasDadas.ConEquipo(dada.Motivo) && !string.IsNullOrWhiteSpace(dada.Equipo) ? " (" + dada.Equipo.Trim() + ")" : "";
            return "Le diste la asistencia a las " + Hora(dada.DadaEn) + ": " + AsistenciasDadas.EnFrase(dada) + equipo + ".";
        }

        /// <summary>"10:42": la hora del servidor en que se dio.</summary>
        private static string Hora(DateTime cuando)
        {
            return cuando.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// La ayuda de la fila Asistieron de un dia: "18 de 24 registraron su
        /// bitacora."; con asistencias dadas ese dia, "18 de 24 tienen asistencia: 16
        /// registraron su bitacora y a 2 les diste la asistencia.".
        /// </summary>
        private static string AyudaAsistieron(int asistieron, int dadas, int enLista)
        {
            if (dadas <= 0)
            {
                return asistieron + " de " + enLista + " registraron su bitácora.";
            }

            int registraron = Math.Max(0, asistieron - dadas);
            return asistieron + " de " + enLista + (asistieron == 1 ? " tiene" : " tienen") + " asistencia: "
                + (registraron == 0 ? "nadie registró su bitácora" : registraron == 1 ? "1 registró su bitácora" : registraron + " registraron su bitácora")
                + " y " + (dadas == 1 ? "a 1 le diste la asistencia." : "a " + dadas + " les diste la asistencia.");
        }

        /// <summary>
        /// El % (asistencias entre asistencias y faltas: lo pendiente de hoy no
        /// cuenta) y la barra de derecho a evaluacion, que lo guarda en su Tag para
        /// pintarse. En el texto de la barra, lo que se copia a Excel.
        /// </summary>
        private void PonerPorcentaje(DataGridViewRow fila, int asistencias, int faltas, bool delGrupo)
        {
            DataGridViewCell porciento = fila.Cells["porciento"];
            DataGridViewCell barra = fila.Cells["derecho"];
            if (asistencias + faltas == 0)
            {
                porciento.Value = "";
                barra.Tag = null;
                barra.Value = "Pendiente";
                barra.ToolTipText = "Aún no hay clases que cuenten: la de hoy sigue.";
                return;
            }

            double valor = 100.0 * asistencias / (asistencias + faltas);
            // Truncado, con la misma regla que el tablero y la lista (ResumenAsistencia).
            int redondeado = ResumenAsistencia.PorcentajeMostrado(valor);
            bool conDerecho = ResumenAsistencia.TieneDerecho(valor);
            porciento.Value = redondeado + "%";
            if (!conDerecho)
            {
                Formato(porciento, redondeado + "%", fila.DefaultCellStyle.BackColor.IsEmpty ? Color.White : fila.DefaultCellStyle.BackColor, FaltaTexto);
                porciento.Style.Font = fuenteNegrita;
            }

            barra.Tag = valor;
            barra.Value = delGrupo ? "Grupo" : conDerecho ? "Con derecho" : "Sin derecho";
            barra.ToolTipText = delGrupo
                ? "Asistencia del grupo: " + redondeado + " %."
                : (conDerecho ? "Con derecho a evaluación: " : "Sin derecho a evaluación: ") + redondeado + " % de asistencia (el mínimo es " + MinimoDeAsistencia + " %).";
        }

        /// <summary>"lun 12/10", o "hoy 13/10" mientras dura la clase de hoy.</summary>
        private static string EncabezadoDe(DiaDeAsistencia dia)
        {
            return (dia.EnCurso ? "hoy" : dia.Fecha.ToString("ddd", Espanol).Replace(".", "")) + " " + dia.Fecha.ToString("dd/MM");
        }

        private static void Formato(DataGridViewCell celda, string texto, Color fondo, Color letra)
        {
            celda.Value = texto;
            celda.Style.BackColor = fondo;
            celda.Style.ForeColor = letra;
            celda.Style.SelectionBackColor = fondo;
            celda.Style.SelectionForeColor = letra;
        }

        private static DataGridViewTextBoxColumn Columna(string nombre, string encabezado, int ancho, DataGridViewContentAlignment alineacion, bool fija)
        {
            DataGridViewTextBoxColumn columna = new DataGridViewTextBoxColumn
            {
                Name = nombre,
                HeaderText = encabezado,
                Width = ancho,
                Frozen = fija,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,
                Resizable = DataGridViewTriState.False
            };
            columna.DefaultCellStyle.Alignment = alineacion;
            return columna;
        }

        /// <summary>Como el autoajuste de Excel: la columna del nombre mide lo que el nombre mas largo.</summary>
        private void AutoajustarNombre()
        {
            if (!grid.Columns.Contains("alumno"))
            {
                return;
            }

            int nombre = TextRenderer.MeasureText(grid.Columns["alumno"].HeaderText, fuenteNegrita).Width;
            foreach (DataGridViewRow fila in grid.Rows)
            {
                nombre = Math.Max(nombre, TextRenderer.MeasureText(Convert.ToString(fila.Cells["alumno"].Value), fuente).Width);
            }

            grid.Columns["alumno"].Width = Math.Max(200, Math.Min(420, nombre + 18));
        }

        // =====================================================================
        // Disposicion: todo a mano
        // =====================================================================

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Acomodar();
        }

        /// <summary>
        /// El membrete en (0, 0); la cuadricula 8 px debajo, del tamano de su
        /// contenido sin pasarse del alto que queda; y pegadas a ella, 8 px debajo, la
        /// simbologia y 4 px despues cada ayuda: la de cambiar la asistencia
        /// (mientras dura la clase), el enlace para darla a quien no esta en la lista
        /// (db/22, mientras dura la clase) y la de pasarla a Excel; sin alumnos, el
        /// aviso y, debajo, el enlace. Mientras se lee otra lista, la cuadricula
        /// conserva su caja.
        /// </summary>
        private void Acomodar()
        {
            // Aun en el constructor, o la ventana minimizada: se acomoda al volver a tener medida.
            if (lblSinAlumnos == null || ClientSize.Width < 100 || ClientSize.Height < 100)
            {
                return;
            }

            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;
            SuspendLayout();
            try
            {
                // El membrete: en una linea si cabe; los datos que no, al segundo renglon.
                if (membrete.MaximumSize.Width != ancho)
                {
                    membrete.MaximumSize = new Size(ancho, 0);
                }

                LimitarDatos(ancho);
                Size tamMembrete = membrete.PreferredSize;
                membrete.SetBounds(0, 0, tamMembrete.Width, tamMembrete.Height);
                int y = tamMembrete.Height + AntesDeLaCuadricula;

                // Lo de abajo se mide antes: lo que mide es lo que no le queda a la cuadricula.
                foreach (Control abajo in new Control[] { simbologia, lblComoQuitar, lnkDarSinLista, lblComoCopiar, lblSinAlumnos })
                {
                    if (abajo.MaximumSize.Width != ancho)
                    {
                        abajo.MaximumSize = new Size(ancho, 0);
                    }
                }

                Size tamSimbologia = simbologia.PreferredSize;
                Size tamAyuda = lblComoQuitar.PreferredSize;
                Size tamEnlace = lnkDarSinLista.PreferredSize;
                Size tamCopiar = lblComoCopiar.PreferredSize;
                Size tamAviso = lblSinAlumnos.PreferredSize;
                // La de pasarla a Excel va con la simbologia (hay alumnos), despues de la de quitar
                // y del enlace; en kiosco no va (PonerSimbologia).
                bool verCopiar = verSimbologia && !Kiosco.Activo;
                int pie = (verSimbologia ? AntesDeLaSimbologia + tamSimbologia.Height : 0)
                    + (verAyuda ? (verSimbologia ? AntesDeLaAyuda : AntesDeLaSimbologia) + tamAyuda.Height : 0)
                    + (verEnlace ? AntesDeLaAyuda + tamEnlace.Height : 0)
                    + (verCopiar ? AntesDeLaAyuda + tamCopiar.Height : 0)
                    + (verAviso ? AntesDeLaSimbologia + tamAviso.Height : 0);
                int maxAlto = Math.Max(60, alto - y - pie);

                Size caja;
                if (!cargando)
                {
                    caja = CajaDeLaCuadricula(ancho, maxAlto);
                    cajaPuesta = true;
                }
                else if (cajaPuesta)
                {
                    // La de la lista anterior: al llegar la nueva se ajusta una sola vez.
                    caja = new Size(Math.Min(grid.Width, ancho), Math.Min(grid.Height, maxAlto));
                }
                else
                {
                    // La primera lectura: aun no hay caja; vacia, la cuadricula es pagina en blanco.
                    caja = new Size(ancho, maxAlto);
                }

                caja = new Size(Math.Max(100, caja.Width), Math.Max(60, caja.Height));
                grid.SetBounds(0, y, caja.Width, caja.Height);
                if (colorDeLaClase.HasValue)
                {
                    // La cinta sigue al membrete y al ancho de la cuadricula: fuera la de antes.
                    Invalidate(new Rectangle(0, 0, ancho, y));
                }

                int siguiente = y + caja.Height;
                if (verSimbologia)
                {
                    siguiente += AntesDeLaSimbologia;
                    simbologia.SetBounds(0, siguiente, tamSimbologia.Width, tamSimbologia.Height);
                    siguiente += tamSimbologia.Height;
                }

                if (verAyuda)
                {
                    siguiente += verSimbologia ? AntesDeLaAyuda : AntesDeLaSimbologia;
                    lblComoQuitar.SetBounds(0, siguiente, tamAyuda.Width, tamAyuda.Height);
                    siguiente += tamAyuda.Height;
                }

                // Con alumnos, el enlace va debajo de la ayuda y antes de lo de Excel; sin ellos, debajo del aviso.
                int xEnlace = -SangriaEnlace();
                if (verEnlace && !verAviso)
                {
                    siguiente += AntesDeLaAyuda;
                    lnkDarSinLista.SetBounds(xEnlace, siguiente, tamEnlace.Width, tamEnlace.Height);
                    siguiente += tamEnlace.Height;
                }

                if (verCopiar)
                {
                    siguiente += AntesDeLaAyuda;
                    lblComoCopiar.SetBounds(0, siguiente, tamCopiar.Width, tamCopiar.Height);
                    siguiente += tamCopiar.Height;
                }

                if (verAviso)
                {
                    siguiente += AntesDeLaSimbologia;
                    lblSinAlumnos.SetBounds(0, siguiente, tamAviso.Width, tamAviso.Height);
                    siguiente += tamAviso.Height;
                    if (verEnlace)
                    {
                        siguiente += AntesDeLaAyuda;
                        lnkDarSinLista.SetBounds(xEnlace, siguiente, tamEnlace.Width, tamEnlace.Height);
                    }
                }
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        /// <summary>
        /// El relleno que GDI pone a la izquierda del texto del enlace: la mitad de lo
        /// que mide de mas sin NoPadding (siempre con un Graphics, como Texto). Con el
        /// dibujo de GDI+ (UseCompatibleTextRendering) no hay ese relleno: 0.
        /// </summary>
        private int SangriaEnlace()
        {
            if (sangriaEnlace < 0)
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    const TextFormatFlags Linea = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                    int conRelleno = TextRenderer.MeasureText(g, lnkDarSinLista.Text, lnkDarSinLista.Font, Size.Empty, Linea).Width;
                    int sinRelleno = TextRenderer.MeasureText(g, lnkDarSinLista.Text, lnkDarSinLista.Font, Size.Empty, Linea | TextFormatFlags.NoPadding).Width;
                    sangriaEnlace = lnkDarSinLista.UseCompatibleTextRendering ? 0 : Math.Max(0, (conRelleno - sinRelleno) / 2);
                }
            }

            return sangriaEnlace;
        }

        /// <summary>
        /// La caja de la cuadricula: lo que miden sus filas (74 del encabezado y 21
        /// cada una) y sus columnas (tras el autoajuste del nombre), con la barra
        /// vertical si no cabe en el alto y la horizontal si no cabe en el ancho.
        /// </summary>
        private Size CajaDeLaCuadricula(int anchoDisp, int maxAlto)
        {
            int barraVertical = SystemInformation.VerticalScrollBarWidth;
            int barraHorizontal = SystemInformation.HorizontalScrollBarHeight;
            int contenido = grid.ColumnHeadersHeight + grid.Rows.Count * grid.RowTemplate.Height + 2;
            int columnas = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Sum(c => c.Width);
            bool conVertical = contenido > maxAlto;
            bool conHorizontal = columnas + (conVertical ? barraVertical : 0) + 2 > anchoDisp;
            if (conHorizontal)
            {
                // La barra horizontal le quita alto: quiza ya no quepa sin la vertical.
                contenido += barraHorizontal;
                conVertical = contenido > maxAlto;
            }

            return new Size(Math.Min(anchoDisp, columnas + (conVertical ? barraVertical : 0) + 2), Math.Min(contenido, maxAlto));
        }

        /// <summary>
        /// Un dato del membrete que no cabe ni solo en su renglon (una materia o un
        /// nombre muy largos) se corta con "..." y lleva el texto completo en su
        /// ToolTip; los demas van enteros.
        /// </summary>
        private void LimitarDatos(int ancho)
        {
            foreach (FlowLayoutPanel par in membrete.Controls.OfType<FlowLayoutPanel>())
            {
                Texto rotulo = par.Controls.Count == 2 ? par.Controls[0] as Texto : null;
                Texto valor = par.Controls.Count == 2 ? par.Controls[1] as Texto : null;
                if (rotulo == null || valor == null)
                {
                    continue;
                }

                int libre = Math.Max(40, ancho - par.Margin.Horizontal - rotulo.Margin.Horizontal - rotulo.PreferredSize.Width - valor.Margin.Horizontal);
                if (valor.MaximumSize.Width != libre)
                {
                    valor.MaximumSize = new Size(libre, 0);
                }

                string completo = valor.Cortado ? valor.Text : "";
                if (ayuda.GetToolTip(valor) != completo)
                {
                    ayuda.SetToolTip(valor, completo.Length == 0 ? null : completo);
                }
            }
        }

        // =====================================================================
        // Marcar alumnos desde fuera (las tarjetas del tablero)
        // =====================================================================

        /// <summary>
        /// Marca las filas de esos alumnos (ids de AlumnoDeAsistencia) con el tono
        /// verde de la seleccion y lleva la casilla activa al nombre de la primera.
        /// Con desplazar, si la primera no esta a la vista, la hoja baja hasta ella;
        /// sin el (al volver a marcar tras la relectura de cada 30 s), lo que se ve
        /// no se mueve. Si ninguna fila es de esos alumnos, solo quita lo que habia.
        /// </summary>
        public void MarcarAlumnos(ICollection<int> ids, bool desplazar = true)
        {
            marcando = true;
            try
            {
                grid.ClearSelection();
                if (ids == null || ids.Count == 0 || !grid.Columns.Contains("alumno"))
                {
                    return;
                }

                List<DataGridViewRow> filas = new List<DataGridViewRow>();
                foreach (DataGridViewRow fila in grid.Rows)
                {
                    AlumnoDeAsistencia alumno = fila.Tag as AlumnoDeAsistencia;
                    if (alumno != null && ids.Contains(alumno.Id))
                    {
                        filas.Add(fila);
                    }
                }

                if (filas.Count == 0)
                {
                    return;
                }

                // Lo que se ve antes de mover la casilla activa: ponerla lleva la hoja hasta ella.
                int primera = filas[0].Index;
                int arriba = grid.FirstDisplayedScrollingRowIndex;
                int izquierda = grid.HorizontalScrollingOffset;
                bool aLaVista = arriba >= 0 && primera >= arriba && primera < arriba + grid.DisplayedRowCount(false);

                grid.CurrentCell = filas[0].Cells["alumno"];
                foreach (DataGridViewRow fila in filas)
                {
                    foreach (DataGridViewColumn columna in grid.Columns)
                    {
                        if (columna.Visible)
                        {
                            fila.Cells[columna.Index].Selected = true;
                        }
                    }
                }

                if (desplazar)
                {
                    if (!aLaVista)
                    {
                        grid.FirstDisplayedScrollingRowIndex = Math.Max(0, primera - 2);
                    }
                }
                else
                {
                    if (arriba >= 0 && arriba < grid.Rows.Count && grid.FirstDisplayedScrollingRowIndex != arriba)
                    {
                        grid.FirstDisplayedScrollingRowIndex = arriba;
                    }

                    if (grid.HorizontalScrollingOffset != izquierda)
                    {
                        grid.HorizontalScrollingOffset = izquierda;
                    }
                }
            }
            finally
            {
                marcando = false;
            }
        }

        /// <summary>Quita la marca puesta con MarcarAlumnos (la casilla activa se queda donde esta).</summary>
        public void QuitarMarca()
        {
            marcando = true;
            try
            {
                grid.ClearSelection();
            }
            finally
            {
                marcando = false;
            }
        }

        // =====================================================================
        // Copiar la hoja entera (para pegarla en Excel)
        // =====================================================================

        /// <summary>Como fue CopiarTodo.</summary>
        internal enum Copia
        {
            Hecha,
            /// <summary>Aun no hay lista (se esta leyendo, o no se pudo leer).</summary>
            SinLista,
            /// <summary>La lista no tiene alumnos: no hay nada que copiar.</summary>
            SinAlumnos,
            /// <summary>El portapapeles no se dejo (otro programa lo tenia abierto): en detalle, por que.</summary>
            Fallo
        }

        /// <summary>
        /// La hoja entera al portapapeles, como Ctrl+A y Ctrl+C: los encabezados, un
        /// renglon por alumno y la fila Asistieron, como texto con tabuladores, HTML y
        /// CSV (Excel pega cada valor en su casilla). Lo elegido, la casilla activa y
        /// lo que se ve no cambian, y la marca del tablero se queda.
        /// </summary>
        public Copia CopiarTodo(out string detalle)
        {
            detalle = null;
            if (asistencia == null || cargando)
            {
                return Copia.SinLista;
            }

            if (asistencia.Alumnos.Count == 0 || grid.Rows.Count == 0)
            {
                return Copia.SinAlumnos;
            }

            // En el kiosco la hoja no se copia (ClipboardCopyMode.Disable): sin esto,
            // GetClipboardContent lanzaria NotSupportedException.
            if (grid.ClipboardCopyMode == DataGridViewClipboardCopyMode.Disable)
            {
                detalle = "En los equipos del laboratorio la lista no se copia: cópiala desde tu propio equipo.";
                return Copia.Fallo;
            }

            DataObject datos;
            List<DataGridViewCell> elegidas = grid.SelectedCells.Cast<DataGridViewCell>().ToList();
            int arriba = grid.FirstDisplayedScrollingRowIndex;
            int izquierda = grid.HorizontalScrollingOffset;
            // Elegirlo todo es solo para copiar: no es el profesor, la marca no se va (MarcaQuitada).
            marcando = true;
            try
            {
                grid.SelectAll();
                datos = grid.GetClipboardContent();
            }
            finally
            {
                grid.ClearSelection();
                foreach (DataGridViewCell celda in elegidas)
                {
                    celda.Selected = true;
                }

                if (arriba >= 0 && arriba < grid.Rows.Count && grid.FirstDisplayedScrollingRowIndex != arriba)
                {
                    grid.FirstDisplayedScrollingRowIndex = arriba;
                }

                if (grid.HorizontalScrollingOffset != izquierda)
                {
                    grid.HorizontalScrollingOffset = izquierda;
                }

                marcando = false;
            }

            if (datos == null)
            {
                return Copia.SinAlumnos;
            }

            try
            {
                // Con reintentos: otro programa puede tener el portapapeles abierto un momento.
                Clipboard.SetDataObject(datos, true, 5, 100);
                return Copia.Hecha;
            }
            catch (ExternalException ex)
            {
                // La causa y que hacer; el texto de Windows, al registro.
                detalle = "Otro programa estaba usando el portapapeles: espera un momento y vuelve a pulsar «Copiar la lista».";
                Log.Aviso("Mis grupos: no se pudo copiar la lista de la clase " + asistencia.ClaseId + ": " + ex.Message);
                return Copia.Fallo;
            }
        }

        // =====================================================================
        // La hoja entera en CSV (para guardarla y abrirla con Excel)
        // =====================================================================

        /// <summary>
        /// La hoja entera como un archivo CSV que Excel abre con doble clic
        /// ("Guardar en Excel (CSV)..." de la ventana). En las primeras lineas, el
        /// membrete: el titulo, la materia, el grupo, el docente, el periodo (con
        /// sus fechas, no "a hoy": el archivo se abre otro dia) y cuando se leyo;
        /// una linea en blanco; los encabezados (cada clase con su dia y su fecha
        /// completa), un renglon por estudiante con lo mismo que la hoja y la fila
        /// Asistieron; y al final que quiere decir cada letra. Separado con el
        /// separador de listas de Windows (el que usa Excel en este equipo), con
        /// comillas donde hace falta y lineas CRLF; quien lo escribe lo guarda en
        /// UTF-8 con BOM, para que Excel lea los acentos. Un dato que empieza con
        /// =, +, - o @ lleva un apostrofo delante: Excel no lo toma por una formula
        /// (los nombres los escribe quien crea su cuenta). Null si no hay nada que
        /// guardar, y por que en resultado (SinLista o SinAlumnos, como CopiarTodo).
        /// </summary>
        public string ComoCsv(out Copia resultado)
        {
            if (asistencia == null || cargando)
            {
                resultado = Copia.SinLista;
                return null;
            }

            if (asistencia.Alumnos.Count == 0 || grid.Rows.Count == 0)
            {
                resultado = Copia.SinAlumnos;
                return null;
            }

            string separador = SeparadorDeListas();
            List<DataGridViewColumn> columnas = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
            System.Text.StringBuilder csv = new System.Text.StringBuilder();
            Action<IEnumerable<string>> renglon = campos => csv.Append(string.Join(separador, campos.Select(c => CampoCsv(c, separador)))).Append("\r\n");

            int total = asistencia.Dias.Count;
            DateTime? ultima = total == 0 ? (DateTime?)null : asistencia.Dias[total - 1].Fecha;
            string periodo = ultima == null ? "desde el " + asistencia.VigenteDesde.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "del " + asistencia.VigenteDesde.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " al " + ultima.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            renglon(new[] { "LISTA DE ASISTENCIA" });
            renglon(new[] { "Materia:", asistencia.Materia });
            renglon(new[] { "Grupo:", asistencia.Grupo });
            if (!string.IsNullOrWhiteSpace(docente))
            {
                renglon(new[] { "Docente:", docente });
            }

            renglon(new[] { "Periodo:", periodo });
            renglon(new[] { "Actualizada:", asistencia.AhoraServidor.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) });
            csv.Append("\r\n");

            // Cada clase con su fecha completa ("lun 12/10/2026"), tambien la de hoy.
            renglon(columnas.Select(c =>
            {
                DiaDeAsistencia dia = c.Tag as DiaDeAsistencia;
                return dia == null ? c.HeaderText
                    : dia.Fecha.ToString("ddd", Espanol).Replace(".", "") + " " + dia.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }));
            foreach (DataGridViewRow fila in grid.Rows)
            {
                renglon(columnas.Select(c => Convert.ToString(fila.Cells[c.Index].Value, CultureInfo.CurrentCulture)));
            }

            // Las asistencias que dio el profesor (db/22) en los dias de la lista: en la casilla
            // van como "A" (las cuentas y las formulas no cambian) y al final, cuales fueron.
            HashSet<DateTime> diasDeLaLista = new HashSet<DateTime>(asistencia.Dias.Select(d => d.Fecha.Date));
            List<Tuple<AlumnoDeAsistencia, AsistenciaDada>> dadas = asistencia.Alumnos
                .SelectMany(a => a.Dadas.Values.Where(d => diasDeLaLista.Contains(d.Fecha.Date)).Select(d => Tuple.Create(a, d)))
                .ToList();

            csv.Append("\r\n");
            renglon(new[] { "Simbología:", (dadas.Count > 0
                    ? "A: asistencia (registró su bitácora o, si su equipo no pudo registrarla, se la dio su docente: abajo, cuáles)."
                    : "A: registró su bitácora.")
                + " F: falta (también si registró sin estar en el salón y se le quitó la asistencia)."
                + (hoy != null ? " P: pendiente, aún no registra y la clase de hoy sigue (no cuenta como falta)." : "") });
            renglon(new[] { "Derecho a evaluación:", "con " + MinimoDeAsistencia + " % o más de asistencia." });
            if (dadas.Count > 0)
            {
                csv.Append("\r\n");
                renglon(new[] { "Asistencias que dio el docente sin bitácora:" });
                renglon(new[] { "Fecha", "Matrícula", "Nombre completo", "Se la dio a las", "Motivo", "Equipo", "Detalle" });
                // Por dia y, dentro de cada dia, en el orden de la lista.
                List<AlumnoDeAsistencia> orden = asistencia.Alumnos;
                foreach (Tuple<AlumnoDeAsistencia, AsistenciaDada> par in dadas
                    .OrderBy(p => p.Item2.Fecha).ThenBy(p => orden.IndexOf(p.Item1)).ThenBy(p => p.Item2.DadaEn))
                {
                    AlumnoDeAsistencia alumno = par.Item1;
                    AsistenciaDada dada = par.Item2;
                    DateTime fecha = dada.Fecha.Date;
                    // Si ese dia tambien cuenta su bitacora (llego despues, de la cola sin conexion), se dice.
                    bool llegoSuBitacora = !alumno.SinBitacora.Contains(fecha) && alumno.BitacorasDelDia.ContainsKey(fecha);
                    renglon(new[]
                    {
                        fecha.ToString("ddd", Espanol).Replace(".", "") + " " + fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        alumno.Matricula,
                        alumno.Nombre,
                        Hora(dada.DadaEn),
                        AsistenciasDadas.NombreDeMotivo(dada.Motivo) + (llegoSuBitacora ? " (después llegó su bitácora)" : ""),
                        AsistenciasDadas.ConEquipo(dada.Motivo) ? (dada.Equipo ?? "").Trim() : "",
                        (dada.Comentario ?? "").Trim()
                    });
                }
            }

            resultado = Copia.Hecha;
            return csv.ToString();
        }

        /// <summary>El separador de listas de Windows (con el que Excel abre un CSV en este equipo); "," si no hay uno util.</summary>
        internal static string SeparadorDeListas()
        {
            string separador = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            return string.IsNullOrEmpty(separador) || separador.Length > 3 || separador.Any(c => char.IsLetterOrDigit(c) || c == '"' || c == '\r' || c == '\n')
                ? "," : separador;
        }

        /// <summary>
        /// Un dato del CSV: con apostrofo si Excel lo tomaria por formula (empieza
        /// con =, +, -, @, tabulador o salto; un numero como "-1" o "+5" no, para
        /// que siga siendo numero), y entre comillas si lleva el separador,
        /// comillas o saltos.
        /// </summary>
        internal static string CampoCsv(string valor, string separador)
        {
            string texto = valor ?? "";
            double numero;
            bool esNumero = (texto.StartsWith("-", StringComparison.Ordinal) || texto.StartsWith("+", StringComparison.Ordinal))
                && double.TryParse(texto, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out numero);
            if (texto.Length > 0 && "=+-@\t\r\n".IndexOf(texto[0]) >= 0 && !esNumero)
            {
                texto = "'" + texto;
            }

            bool entreComillas = texto.IndexOf(separador, StringComparison.Ordinal) >= 0 || texto.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0
                || (texto.Length > 0 && (char.IsWhiteSpace(texto[0]) || char.IsWhiteSpace(texto[texto.Length - 1])));
            return entreComillas ? "\"" + texto.Replace("\"", "\"\"") + "\"" : texto;
        }

        // =====================================================================
        // Pintado: como Excel
        // =====================================================================

        private void PintarCelda(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex < 0)
            {
                return;
            }

            if (e.RowIndex < 0)
            {
                PintarEncabezado(e);
                return;
            }

            DataGridViewCell celda = grid[e.ColumnIndex, e.RowIndex];
            // Sin el azul de seleccion de Windows: el fondo de siempre, el texto, y
            // encima lo que pinta Excel (tono verde en lo seleccionado, marco en la activa).
            e.PaintBackground(e.CellBounds, false);
            if (grid.Columns[e.ColumnIndex].Name == "derecho")
            {
                PintarBarra(e, celda);
            }
            else
            {
                e.PaintContent(e.CellBounds);
            }

            Rectangle r = e.CellBounds;
            if (celda.Selected && grid.SelectedCells.Count > 1)
            {
                using (SolidBrush tono = new SolidBrush(Color.FromArgb(36, VerdeExcel)))
                {
                    e.Graphics.FillRectangle(tono, r.X, r.Y, r.Width - 1, r.Height - 1);
                }
            }

            if (Equals(grid.Rows[e.RowIndex].Tag, "total"))
            {
                using (Pen linea = new Pen(Color.FromArgb(120, 120, 120)))
                {
                    e.Graphics.DrawLine(linea, r.Left, r.Top, r.Right, r.Top);
                }
            }

            if (grid.CurrentCell == celda && grid.Focused)
            {
                using (Pen marco = new Pen(VerdeExcel, 2))
                {
                    e.Graphics.DrawRectangle(marco, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
                }
            }

            PintarDivisoria(e);
            e.Handled = true;
        }

        /// <summary>La linea gruesa gris a la derecha de la ultima columna de cada bloque (tambien en el encabezado).</summary>
        private void PintarDivisoria(DataGridViewCellPaintingEventArgs e)
        {
            if (!divisorias.Contains(grid.Columns[e.ColumnIndex].Name))
            {
                return;
            }

            using (SolidBrush gris = new SolidBrush(Divisoria))
            {
                e.Graphics.FillRectangle(gris, e.CellBounds.Right - GruesoDivisoria, e.CellBounds.Top, GruesoDivisoria, e.CellBounds.Height);
            }
        }

        /// <summary>
        /// Como las barras de datos de Excel: una pista gris con la asistencia en
        /// verde (desde el minimo, con derecho) o en rojo (por debajo), una marca en
        /// el minimo y, a la derecha, si tiene derecho. Sin clases que cuenten, "Pendiente".
        /// </summary>
        private void PintarBarra(DataGridViewCellPaintingEventArgs e, DataGridViewCell celda)
        {
            Rectangle r = e.CellBounds;
            const int anchoTexto = 82;
            Rectangle pista = new Rectangle(r.X + 7, r.Y + 5, r.Width - 7 - anchoTexto - 8 - GruesoDivisoria, r.Height - 11);
            Rectangle texto = new Rectangle(pista.Right + 8, r.Y, anchoTexto, r.Height - 1);
            if (!(celda.Tag is double))
            {
                TextRenderer.DrawText(e.Graphics, Convert.ToString(celda.Value), fuenteBarra, new Rectangle(r.X + 7, r.Y, r.Width - 14, r.Height - 1),
                    TextoGris, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            double valor = (double)celda.Tag;
            bool conDerecho = ResumenAsistencia.TieneDerecho(valor);
            bool delGrupo = Equals(grid.Rows[e.RowIndex].Tag, "total");
            using (SolidBrush fondoPista = new SolidBrush(PistaBarra))
            using (SolidBrush relleno = new SolidBrush(conDerecho ? BarraConDerecho : BarraSinDerecho))
            using (Pen marca = new Pen(Color.FromArgb(70, 70, 70)) { DashStyle = DashStyle.Dot })
            {
                e.Graphics.FillRectangle(fondoPista, pista);
                int largo = (int)Math.Round(pista.Width * Math.Max(0, Math.Min(100, valor)) / 100.0);
                if (largo > 0)
                {
                    e.Graphics.FillRectangle(relleno, pista.X, pista.Y, largo, pista.Height);
                }

                int x = pista.X + (int)Math.Round(pista.Width * MinimoDeAsistencia / 100.0);
                e.Graphics.DrawLine(marca, x, pista.Top - 3, x, pista.Bottom + 2);
            }

            TextRenderer.DrawText(e.Graphics, delGrupo ? "Grupo" : conDerecho ? "Con derecho" : "Sin derecho", fuenteBarra, texto,
                delGrupo ? Color.FromArgb(60, 60, 60) : conDerecho ? TextoConDerecho : FaltaTexto,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        /// <summary>Verde con letra blanca; las fechas, en vertical (de abajo arriba), y hoy en amarillo.</summary>
        private void PintarEncabezado(DataGridViewCellPaintingEventArgs e)
        {
            DataGridViewColumn columna = grid.Columns[e.ColumnIndex];
            DiaDeAsistencia dia = columna.Tag as DiaDeAsistencia;
            Rectangle r = e.CellBounds;
            Color fondo = dia != null && dia.EnCurso ? HoyFondo : VerdeExcel;
            Color letra = dia != null && dia.EnCurso ? HoyTexto : Color.White;
            using (SolidBrush pincel = new SolidBrush(fondo))
            {
                e.Graphics.FillRectangle(pincel, r);
            }

            using (Pen borde = new Pen(dia != null && dia.EnCurso ? Color.FromArgb(214, 180, 70) : BordeEncabezado))
            {
                e.Graphics.DrawLine(borde, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);
                e.Graphics.DrawLine(borde, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1);
            }

            if (dia == null)
            {
                TextRenderer.DrawText(e.Graphics, columna.HeaderText, fuenteNegrita, new Rectangle(r.X + 3, r.Y, r.Width - 6, r.Height - 2), letra,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                PintarDivisoria(e);
                e.Handled = true;
                return;
            }

            GraphicsState estado = e.Graphics.Save();
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            SizeF tamano = e.Graphics.MeasureString(columna.HeaderText, fuenteNegrita);
            e.Graphics.TranslateTransform(r.Left + r.Width / 2f, r.Bottom - (r.Height - tamano.Width) / 2f);
            e.Graphics.RotateTransform(-90);
            using (SolidBrush pincel = new SolidBrush(letra))
            {
                e.Graphics.DrawString(columna.HeaderText, fuenteNegrita, pincel, 0, -tamano.Height / 2f);
            }

            e.Graphics.Restore(estado);
            PintarDivisoria(e);
            e.Handled = true;
        }

        // =====================================================================
        // Cambiar la asistencia de hoy: quitar, devolver, dar o anular la que dio
        // =====================================================================

        /// <summary>
        /// El menu de la casilla de hoy (clic izquierdo en ella o derecho en
        /// cualquiera de su fila), mientras dura un bloque de la clase y si la base
        /// deja cambiar algo (db/17 o db/22). Primero, apagado, de quien es; despues,
        /// lo que se puede hacer segun su casilla de hoy; con db/22, al final, dar la
        /// asistencia a alguien que no esta en la lista.
        /// </summary>
        private void CasillaPulsada(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || asistencia == null || hoy == null || !(asistencia.ConRetiro || asistencia.ConDadas) || !PuedeCambiar)
            {
                return;
            }

            AlumnoDeAsistencia alumno = grid.Rows[e.RowIndex].Tag as AlumnoDeAsistencia;
            DiaDeAsistencia dia = grid.Columns[e.ColumnIndex].Tag as DiaDeAsistencia;
            // La casilla de hoy con el izquierdo; cualquiera de su fila con el derecho.
            if (alumno == null || (e.Button == MouseButtons.Left && dia != hoy) || (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right))
            {
                return;
            }

            ArmarMenu(alumno);
            Rectangle casilla = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
            menu.Show(grid, new Point(casilla.Left + e.X, casilla.Top + e.Y));
        }

        /// <summary>Las opciones del menu de ese alumno, segun su casilla de hoy (ver CasillaPulsada).</summary>
        private void ArmarMenu(AlumnoDeAsistencia alumno)
        {
            alumnoDelMenu = alumno;
            DateTime fecha = hoy.Fecha;
            // Las opciones del menu anterior se liberan (Clear solo las quita).
            foreach (ToolStripItem vieja in menu.Items.Cast<ToolStripItem>().ToList())
            {
                vieja.Dispose();
            }

            menu.Items.Clear();
            menu.Items.Add(new ToolStripMenuItem(alumno.Nombre + ", hoy") { Enabled = false, Font = fuenteMenuNegrita });
            if (alumno.Presente.Contains(fecha) && alumno.SinBitacora.Contains(fecha))
            {
                // La A verde: se la dio el profesor sin bitacora.
                AsistenciaDada dada;
                alumno.Dadas.TryGetValue(fecha, out dada);
                if (dada != null)
                {
                    string frase = AsistenciasDadas.EnFrase(dada);
                    if (frase.Length > DetalleEnMenu)
                    {
                        frase = frase.Substring(0, DetalleEnMenu).TrimEnd() + "...";
                    }

                    menu.Items.Add(new ToolStripMenuItem("Se la diste a las " + Hora(dada.DadaEn) + ": " + frase) { Enabled = false });
                }

                menu.Items.Add("Quitarle la asistencia que le diste: no está en el salón", null,
                    (s, a) => Pedir(alumnoDelMenu, AccionAsistencia.QuitarLaQueDiste));
            }
            else if (alumno.Presente.Contains(fecha))
            {
                // La A de siempre: registro su bitacora (si tambien se la dio, quitar anula las dos).
                if (asistencia.ConRetiro)
                {
                    menu.Items.Add("Quitarle la asistencia: registró su bitácora pero no está en el salón", null,
                        (s, a) => Pedir(alumnoDelMenu, AccionAsistencia.Quitar));
                }
                else
                {
                    menu.Items.Add(new ToolStripMenuItem("Registró su bitácora hoy: ya tiene asistencia") { Enabled = false });
                }
            }
            else if (alumno.Retiradas.Contains(fecha))
            {
                menu.Items.Add("Devolverle la asistencia: sí está en el salón", null, (s, a) => Pedir(alumnoDelMenu, AccionAsistencia.Devolver));
            }
            else if (asistencia.ConDadas)
            {
                menu.Items.Add("Darle la asistencia: está en el salón, pero su equipo no pudo registrar su bitácora...", null,
                    (s, a) => Pedir(alumnoDelMenu, AccionAsistencia.Dar));
            }
            else
            {
                menu.Items.Add(new ToolStripMenuItem("Aún no registra su bitácora hoy: no hay asistencia que quitar") { Enabled = false });
            }

            if (asistencia.ConDadas)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(TextoDarSinLista, null, (s, a) =>
                {
                    if (DarSinLista != null)
                    {
                        DarSinLista();
                    }
                });
            }
        }

        private void Pedir(AlumnoDeAsistencia alumno, AccionAsistencia accion)
        {
            if (alumno != null && hoy != null && CambiarAsistencia != null)
            {
                CambiarAsistencia(alumno, hoy.Fecha, accion);
            }
        }

        /// <summary>DataGridView con doble buffer: sin parpadeo al desplazarse ni al rehacerse cada 30 s.</summary>
        private sealed class Cuadricula : DataGridView
        {
            public Cuadricula()
            {
                DoubleBuffered = true;
            }
        }

        /// <summary>
        /// Un texto de alrededor de la cuadricula (membrete, simbologia, ayuda y
        /// aviso), medido y pintado sin el relleno que GDI pone a los lados
        /// (NoPadding, como BotonCabecera): mide lo que su texto, unos 8 px menos que
        /// un Label, y asi la ayuda (1055 px) cabe en una linea a 1366x768. Con
        /// Partir, se parte en renglones al ancho de MaximumSize; sin el, va en una
        /// linea y, si MaximumSize no la deja entera, se corta con "...".
        /// </summary>
        private sealed class Texto : Label
        {
            private const TextFormatFlags EnUnaLinea = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            private const TextFormatFlags EnRenglones = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

            public Texto()
            {
                AutoSize = true;
                UseMnemonic = false;
            }

            /// <summary>Se parte en renglones al ancho de MaximumSize (la ayuda y el aviso).</summary>
            public bool Partir { get; set; }

            private bool SeParte
            {
                get { return Partir && MaximumSize.Width > 0; }
            }

            /// <summary>No cabe entero en su linea: se ve cortado con "...".</summary>
            public bool Cortado
            {
                get
                {
                    if (SeParte || MaximumSize.Width <= 0)
                    {
                        return false;
                    }

                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    {
                        return TextRenderer.MeasureText(g, Text, Font, Size.Empty, EnUnaLinea).Width > MaximumSize.Width;
                    }
                }
            }

            /// <summary>
            /// Lo que mide el texto, siempre con un Graphics: sin el, MeasureText no
            /// hace caso de NoPadding y da 7 px de mas.
            /// </summary>
            public override Size GetPreferredSize(Size proposedSize)
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    Size medida = SeParte
                        ? TextRenderer.MeasureText(g, Text, Font, new Size(MaximumSize.Width, int.MaxValue), EnRenglones)
                        : TextRenderer.MeasureText(g, Text, Font, Size.Empty, EnUnaLinea);
                    int ancho = MaximumSize.Width > 0 ? Math.Min(medida.Width, MaximumSize.Width) : medida.Width;
                    return new Size(ancho + Padding.Horizontal, medida.Height + Padding.Vertical);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Rectangle donde = new Rectangle(Padding.Left, Padding.Top, ClientSize.Width - Padding.Horizontal, ClientSize.Height - Padding.Vertical);
                TextFormatFlags formato;
                if (SeParte)
                {
                    // Al ancho con que se midio, para que se parta en los mismos sitios.
                    donde.Width = Math.Max(donde.Width, MaximumSize.Width - Padding.Horizontal);
                    formato = EnRenglones;
                }
                else
                {
                    int entero = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty, EnUnaLinea).Width;
                    formato = entero > donde.Width ? EnUnaLinea | TextFormatFlags.EndEllipsis : EnUnaLinea;
                }

                TextRenderer.DrawText(e.Graphics, Text, Font, donde, ForeColor, formato);
            }
        }
    }
}
