using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigLab
{
    /// <summary>
    /// Seccion Bitacoras del administrador. Arriba las SESIONES (una clase en una
    /// fecha, que es la bitacora de esa hora), con filtros que se aplican al
    /// momento; abajo, quien registro en la sesion elegida y que reporto, en una
    /// sola columna en vez de seis. Desde aqui se corrige un registro y se
    /// descargan los PDF de una sesion o de todas las que quedan en la lista.
    /// El profesor la abre como "Mis bitácoras": solo sus sesiones y sin corregir.
    /// El encargado de laboratorio (solo consulta) ve toda la escuela como el
    /// administrador y descarga los PDF, pero no corrige ni ve los codigos (Permisos).
    /// Desde otras pantallas se abre ya filtrada: una alerta (Filtrar), un
    /// profesor (Buscar, su nombre y su numero de empleado) o una sesion del
    /// historial de un alumno (MostrarSesion).
    /// A la derecha de las dos listas (2026-10-31), la vista previa del PDF de
    /// la sesion elegida (VistaPreviaPdf): el mismo de "PDF de esta sesión",
    /// generado en segundo plano cuando la lista se queda quieta. En una pantalla
    /// donde a su lado no caben las columnas de la lista, empieza escondida (una
    /// pestana a la derecha la abre).
    /// </summary>
    public class GestionBitacorasAdministrador : PantallaAdmin
    {
        // Fila de una bitacora que reporta algo: fondo suave. La celda con el
        // reporte (o la cuenta de fallas de la sesion) va en un tono fuerte.
        private static readonly Color ColorConFalla = Color.FromArgb(255, 240, 214);
        private static readonly Color ColorFallaCelda = Color.FromArgb(255, 176, 79);
        // Lo que ya se resolvio (db/26), igual pero en verde.
        private static readonly Color ColorResuelta = VentanaHistorialAlumno.ColorResuelta;
        private static readonly Color ColorResueltaCelda = VentanaHistorialAlumno.ColorResueltaCelda;
        // Avisos de equipos sin internet: texto naranja, el de los avisos del panel.
        private static readonly Color ColorAviso = Color.FromArgb(190, 85, 0);
        // Abajo, una asistencia que dio su docente (no es una bitacora): verde claro,
        // como en la Bitacora de la clase.
        private static readonly Color ColorDada = Color.FromArgb(232, 245, 233);
        private static readonly Color ColorDadaElegida = Color.FromArgb(205, 232, 210);
        private static readonly Color TextoDada = Color.FromArgb(0, 96, 55);
        // La franja con lo que reporto el docente (db/15), como en la Bitacora de la clase.
        private static readonly Color TituloReportes = Color.FromArgb(160, 75, 0);
        private static readonly Color TextoReportes = Color.FromArgb(50, 50, 50);
        private const int BarraFranja = 4;
        private const int RellenoFranja = 6;
        private static readonly string[] DiasCortos = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };
        private static readonly string[] Dias = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };

        private const string TodasLasFechas = "Todas las fechas";
        private const string Hoy = "Hoy";
        private const string UltimaSemana = "Últimos 7 días";
        private const string EsteMes = "Este mes";
        private const string UnDia = "Un día...";
        private const string TodosLosLaboratorios = "Todos los laboratorios";
        private const string TodosLosGrupos = "Todos los grupos";
        // Lo que ocupa la casilla "Solo fallas sin resolver" a la derecha del filtro de laboratorio.
        private const int DesdeElLaboratorioAQuitarFiltros = 240;

        private readonly Consultas consultas = new Consultas();
        // Las asistencias que dio el profesor a quien no pudo registrar (db/22): no son
        // bitacoras, asi que no cuentan en la lista; el detalle las dice y el PDF las
        // lleva en su propia tabla.
        private readonly LectorDeDadas lectorDadas = new LectorDeDadas();
        private bool avisoDadas;
        // Lo que reporto el docente desde su panel (db/15): cuenta en Fallas y, abajo,
        // va en una franja encima de quien registro. Tambien va en el PDF.
        private readonly ReportesProfesorDatos reportesDocente = new ReportesProfesorDatos();
        private bool avisoReportes;
        private readonly int idProfesor;
        // En "Mis bitacoras" el filtro es por grupo ("ISC-7001 · Programación Web"): el
        // docente piensa en sus grupos y suele dar clase en uno o dos laboratorios. La
        // administracion y la responsable siguen filtrando por laboratorio.
        private readonly bool porGrupo;
        // Ver toda la escuela (idProfesor 0) no es poder corregir: el encargado de
        // laboratorio ve todo, como el administrador, pero en solo consulta.
        private readonly bool puedeCorregir;
        private readonly Timer esperaDetalle = new Timer { Interval = 120 };
        private readonly DrawingFont negrita;
        private List<SesionInfo> sesiones = new List<SesionInfo>();
        private List<SesionInfo> visibles = new List<SesionInfo>();
        private int idSesionMostrada = -1;
        private bool cargandoFiltros;
        // Desde una alerta: el laboratorio a elegir cuando se lean las sesiones
        // (su lista sale de ellas) y si ya se leyeron alguna vez.
        private string laboratorioPedido;
        private bool cargada;
        // Desde el historial de un alumno (MostrarSesion): la sesion (su id) a dejar
        // elegida cuando se lean.
        private string sesionPedida;
        // El reloj del servidor menos el de este equipo, de la ultima lectura: "(en
        // curso)" y los periodos (Hoy, Ultimos 7 dias, Este mes) van con la hora del
        // servidor, como el panel principal, aunque el reloj de este equipo vaya mal.
        private TimeSpan desfaseServidor = TimeSpan.Zero;

        private readonly TextBox txtBuscar;
        private readonly ComboBox cboPeriodo;
        private readonly DateTimePicker dtpDia;
        private readonly ComboBox cboLaboratorio;
        private readonly CheckBox chkSoloFallas;
        private readonly LinkLabel lnkQuitarFiltros;
        // Bajo la lista de sesiones, que quiere decir el "*" de Alumnos (solo si hay alguno).
        private readonly Label lblSinConexion;
        private readonly Label lblSesionTitulo;
        private readonly Label lblSesionDetalle;
        private readonly DataGridView gridAlumnos;
        private readonly Button btnPdf;
        private readonly Button btnLote;
        private readonly Button btnCorregir;
        private readonly Button btnResolver;
        // La base guarda lo que ya se resolvio (db/26): de la ultima lectura de las sesiones.
        private bool conFallasResueltas;
        // La franja de lo que reporto su docente, ya sin nada pendiente: en verde.
        private bool franjaDocenteResuelta;

        // --- La vista previa del PDF, a la derecha de las listas ---------------
        // A la vista (true) o escondida (false) si la persona lo eligio ("Ocultar" o
        // la pestana), y su ancho si arrastro la division: para todas las Bitacoras
        // (y Mis bitacoras) mientras DigLab siga abierto. Sin elegir (null), se ve
        // solo si a su lado caben todas las columnas de la lista de sesiones
        // (AnchoVistaInicial): en una pantalla angosta empieza escondida.
        private static bool? vistaElegida;
        private static int anchoVistaRecordado;
        // Lo menos al arrastrar; al abrir, entre 420 (la hoja a casi la mitad) y 640
        // (a tres cuartos), lo que dejen las columnas de la lista de sesiones; con
        // menos de 420, entre 330 y 420 si asi caben las columnas.
        private const int MinimoVista = 330;
        private const int PisoVista = 420;
        private const int TopeVista = 640;
        private const int MinimoListas = 520;
        // Al moverse por la lista con las flechas, el PDF se genera cuando se queda
        // quieta; uno de los recientes sale antes (ya no hay que generarlo).
        private const int EsperaVista = 300;
        private const int EsperaVistaReciente = 120;
        private const int PdfRecientes = 6;
        private readonly SplitContainer divisionVista;
        private readonly Panel cabeceraVista;
        private readonly Label lblVistaTitulo;
        private readonly Label lblVistaPagina;
        private readonly Label lblVistaZoom;
        private readonly BotonCabecera btnVistaAlejar;
        private readonly BotonCabecera btnVistaAcercar;
        private readonly BotonCabecera btnVistaAjustar;
        private readonly LinkLabel lnkOcultarVista;
        private readonly VistaPreviaPdf vista;
        private readonly TiraVistaPrevia tiraVista;
        private readonly DrawingFont fuenteSignoVista = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);
        private readonly Timer esperaVista = new Timer { Interval = EsperaVista };
        // Los ultimos PDF generados para la vista (id de la sesion y el PDF con lo que
        // llevaba), del mas viejo al mas nuevo.
        private readonly List<KeyValuePair<int, PdfDeVista>> pdfRecientes = new List<KeyValuePair<int, PdfDeVista>>();
        // Lo que se leyo abajo de la sesion idSesionMostrada (FirmaDeLaSesion; null si
        // no se pudo leer): un PDF guardado con otra firma ya es viejo y no se ensena.
        private string firmaMostrada;
        // Donde estaba el foco antes de −, + o "Ajustar al ancho" (la lista de
        // sesiones, la de estudiantes o la hoja), y si se pulsaron con el raton.
        private Control focoAntesDelZoom;
        private bool zoomConRaton;
        // Sube con cada sesion pedida: lo que llegue de una anterior ya no se pinta.
        private int pedidoVista;
        // Sube con cada F5: un PDF que se pidio antes ya no entra a los recientes.
        private int tandaRecientes;
        // La sesion que la vista ensena o prepara (0: ninguna, con un texto; -1: por decidir).
        private int idSesionEnVista = -1;
        private bool llenandoLista;
        private bool sesionesLeidas;
        private bool moviendoVista;
        // La persona arrastra la division (SplitterMoving); un cambio de tamano de la ventana, no.
        private bool arrastrandoVista;
        private bool anchoVistaAjustado;
        // Arriba de quien registro: el titulo y la linea de la sesion (crece a dos renglones si no caben).
        private readonly Panel encabezadoSesion;
        // Entre el encabezado y quien registro, lo que reporto el docente (oculta si nada):
        // el hueco de abajo (8 px) y dentro la franja con su marco y sus renglones.
        private readonly Panel pnlReportesDocente;
        private readonly Panel pnlFranjaDocente;
        private readonly Panel pnlRenglonesDocente;
        private readonly Label lblReportesDocenteTitulo;
        private readonly Label lblReportesDocente;

        /// <summary>Un PDF de la vista previa y lo que llevaba cuando se genero (FirmaDeLaSesion).</summary>
        private sealed class PdfDeVista
        {
            public readonly byte[] Pdf;
            public readonly string Firma;

            public PdfDeVista(byte[] pdf, string firma)
            {
                Pdf = pdf;
                Firma = firma;
            }
        }

        /// <summary>Una fila de ObtenerSesionesAdministrador, ya interpretada.</summary>
        private sealed class SesionInfo
        {
            public int Id;
            public DateTime? Fecha;
            public TimeSpan? Entrada;
            public TimeSpan? Salida;
            public string Codigo;
            public string Materia;
            public string Grupo;
            public string NumeroEmpleado;
            public string Profesor;
            public string Laboratorio;
            public int Alumnos;
            public int ConFallas;
            /// <summary>Las fallas que reporto su docente desde su panel (db/15).</summary>
            public int ReportesDocente;
            /// <summary>Lo que ya se dio por resuelto (db/26), de sus estudiantes y de su docente: no cuenta en Fallas.</summary>
            public int Resueltas;
            public int SinConexion;
            /// <summary>Equipos que sus alumnos avisaron sin internet (db/10).</summary>
            public List<EquipoSinInternet> SinInternet;
            public string TextoBusqueda;
            /// <summary>Uso libre de un laboratorio sin codigos (db/14): sin clase ni profesor.</summary>
            public bool UsoLibre;

            /// <summary>La columna Fallas: estudiantes con alguna falla sin resolver mas lo que reporto su docente sin resolver.</summary>
            public int Fallas
            {
                get { return ConFallas + ReportesDocente; }
            }

            /// <summary>Lo que dice la columna: "2", "1 + ✓ 1" (una por resolver y una resuelta) o "✓ 2" (ya todas resueltas).</summary>
            public string TextoFallas
            {
                get
                {
                    return Resueltas == 0 ? Fallas.ToString()
                        : Fallas == 0 ? "✓ " + Resueltas
                        : Fallas + " + ✓ " + Resueltas;
                }
            }

            /// <summary>
            /// Hoy y dentro de su horario con la hora del servidor (EstaEnCurso): todavia
            /// pueden llegar bitacoras. Lo fija Aplicar: la lista, su color y el detalle
            /// dicen lo mismo hasta que se vuelve a filtrar o a leer (F5).
            /// </summary>
            public bool EnCurso;

            // dd/MM/yyyy con InvariantCulture: el separador no sale de la configuracion de Windows.
            public string FechaCorta
            {
                get { return Fecha.HasValue ? DiasCortos[(int)Fecha.Value.DayOfWeek] + " " + Fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—"; }
            }

            public string FechaLarga
            {
                get { return Fecha.HasValue ? Dias[(int)Fecha.Value.DayOfWeek] + " " + Fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "Sin fecha"; }
            }

            public string Horario
            {
                get { return HoraCorta(Entrada) + " – " + HoraCorta(Salida); }
            }

            public bool EstaEnCurso(DateTime ahora)
            {
                return Fecha.HasValue && Fecha.Value.Date == ahora.Date && Entrada.HasValue && Salida.HasValue
                    && ahora.TimeOfDay >= Entrada.Value && ahora.TimeOfDay <= Salida.Value;
            }
        }

        /// <summary>Toda la escuela: el administrador, o el encargado de laboratorio en solo consulta.</summary>
        public GestionBitacorasAdministrador()
            : this(0)
        {
        }

        /// <summary>Con un profesor (su id): su "Mis bitácoras", solo sus sesiones y sin corregir registros.</summary>
        public GestionBitacorasAdministrador(int idProfesor)
            : base(idProfesor == 0 ? "Bitácoras" : "Mis bitácoras",
                (idProfesor == 0 ? "Cada sesión es una clase en una fecha." : "Cada sesión es una de tus clases en una fecha.")
                    + " Elige una para ver quién registró, qué reportó y, a la derecha, cómo sale su PDF. F5 actualiza."
                    // La persona responsable no corrige registros (no ve el boton): a quien avisar.
                    + (idProfesor == 0 && Permisos.SoloConsulta ? " Si ves un error en un registro, avísale a la administración." : ""),
                // 1400 de ancho pedidos: a 1366x768 PantallaAdmin la deja en lo que
                // cabe (unos 1350) y a 10 pt el laboratorio, la materia, la fecha y el
                // horario no se cortan.
                new Size(1400, 780))
        {
            this.idProfesor = idProfesor;
            porGrupo = idProfesor != 0;
            puedeCorregir = idProfesor == 0 && !Permisos.SoloConsulta;
            MinimumSize = new Size(1100, 620);
            // Con la vista previa del PDF a la derecha de las listas, a pantalla
            // completa (como Horarios y Estadisticas): la hoja se lee y las listas
            // siguen con sus columnas.
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;

            // --- Filtros: se aplican al momento, sin boton ---------------------
            txtBuscar = CrearBuscador(new Point(Margen, 112), 300);
            // Sin el codigo cuando no se puede ver (encargado de laboratorio): LeerSesion no lo guarda.
            ayuda.SetToolTip(txtBuscar, (idProfesor == 0 ? "Materia, docente, grupo" : "Materia, grupo")
                + (Permisos.VeCodigosDeProfesor ? ", laboratorio, código o fecha (13/07/2026)" : ", laboratorio o fecha (13/07/2026)"));

            Controls.Add(EstiloHorarios.CrearEtiqueta("Fecha", new Point(350, 86)));
            cboPeriodo = EstiloHorarios.CrearCombo(new Point(350, 111), 160);
            cboPeriodo.Items.AddRange(new object[] { TodasLasFechas, Hoy, UltimaSemana, EsteMes, UnDia });
            cboPeriodo.SelectedIndex = 0;
            cboPeriodo.SelectedIndexChanged += (s, e) =>
            {
                dtpDia.Visible = (string)cboPeriodo.SelectedItem == UnDia;
                Aplicar(null);
            };
            Controls.Add(cboPeriodo);

            dtpDia = new DateTimePicker
            {
                // Siempre dd/MM/yyyy: Short ensenaria la fecha corta que tenga configurada Windows.
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy",
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(520, 111),
                Size = new Size(130, 26),
                Value = DateTime.Today,
                Visible = false
            };
            dtpDia.ValueChanged += (s, e) => Aplicar(null);
            Controls.Add(dtpDia);

            // Por grupo, mas ancho: "ISC-7001 · Programación Web" entero (al leer las sesiones,
            // de lo que mida la opcion mas larga: RellenarLaboratorios).
            int anchoFiltro = porGrupo ? 300 : 220;
            Controls.Add(EstiloHorarios.CrearEtiqueta(porGrupo ? "Grupo" : "Laboratorio", new Point(670, 86)));
            cboLaboratorio = EstiloHorarios.CrearCombo(new Point(670, 111), anchoFiltro);
            cboLaboratorio.SelectedIndexChanged += (s, e) => { if (!cargandoFiltros) Aplicar(null); };
            if (porGrupo)
            {
                ayuda.SetToolTip(cboLaboratorio, "Tus grupos, cada uno con su materia.");
            }

            Controls.Add(cboLaboratorio);

            chkSoloFallas = new CheckBox
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(cboLaboratorio.Right + 20, 113),
                Text = "Solo fallas sin resolver"
            };
            chkSoloFallas.CheckedChanged += (s, e) => Aplicar(null);
            ayuda.SetToolTip(chkSoloFallas, "Las sesiones con alguna falla por resolver (de sus estudiantes o de su docente) o con algún aviso de equipos sin internet.");
            Controls.Add(chkSoloFallas);

            lnkQuitarFiltros = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Location = new Point(cboLaboratorio.Right + DesdeElLaboratorioAQuitarFiltros, 114),
                Text = "Quitar filtros",
                Visible = false
            };
            lnkQuitarFiltros.LinkClicked += (s, e) => QuitarFiltros();
            Controls.Add(lnkQuitarFiltros);

            // --- Arriba: sesiones ---------------------------------------------
            CrearLista(156);
            // Diez columnas: con el margen de 8 de las demas listas, a 1366 de ancho
            // salia la barra de abajo y el docente se cortaba. Aqui, 4.
            grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 7, 4, 7);
            grid.Columns.Add("fecha", "Fecha");
            grid.Columns.Add("horario", "Horario");
            grid.Columns.Add("laboratorio", "Laboratorio");
            grid.Columns.Add("materia", "Materia");
            grid.Columns.Add("grupo", "Grupo");
            grid.Columns.Add("profesor", "Docente");
            grid.Columns.Add("codigo", "Código");
            grid.Columns.Add("alumnos", "Estudiantes");
            grid.Columns.Add("fallas", "Fallas");
            grid.Columns.Add("sin_internet", "Sin internet");
            // A 10 pt, el laboratorio y la materia enteros a 1366 de ancho; la fecha
            // ("mar 13/10/2026") y las cortas, con ListaDigLab; los encabezados de una
            // palabra, enteros. El horario, abajo (con la letra en negrita).
            EstiloHorarios.Pesos(grid, 122, 178, 215, 270, 72, 150, 66, 90, 55, 60);
            grid.Columns["profesor"].Visible = idProfesor == 0;
            // Llena lo que dejan las demas: con la vista previa al lado (a 1366) no
            // queda nada y se quedaba en 5 px. Con este minimo sale la barra de abajo.
            grid.Columns["profesor"].MinimumWidth = 120;
            // El codigo de cada sesion es lo que teclean los alumnos: el encargado de
            // laboratorio no lo ve (la columna, ademas, va vacia: ver LeerSesion).
            grid.Columns["codigo"].Visible = Permisos.VeCodigosDeProfesor;
            grid.Columns["alumnos"].ToolTipText = "Estudiantes que registraron bitácora en la sesión.";
            grid.Columns["fallas"].ToolTipText = (idProfesor == 0
                ? "Estudiantes que reportaron alguna falla o comentario, más las fallas que reportó su docente."
                : "Estudiantes que reportaron alguna falla o comentario, más las fallas que reportaste.")
                + " En naranja, las que faltan por resolver; con ✓, las ya resueltas.";
            grid.Columns["sin_internet"].ToolTipText = "Otros equipos que se avisaron sin internet; los más avisados primero.";
            foreach (DataGridViewColumn columna in grid.Columns)
            {
                // Ya vienen de la mas reciente a la mas antigua; ordenar por el
                // texto "lun 13/07/2026" las desordenaria.
                columna.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            // Ya sin ordenar, el encabezado no guarda sitio para la flecha: ListaDigLab
            // vuelve a medir cada titulo (al cambiar el margen y aqui, al no ordenar).
            PalabraEnteraEnEncabezado(grid.Columns["sin_internet"]);
            grid.Columns["alumnos"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns["fallas"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.CellFormatting += FormatoSesion;
            negrita = new DrawingFont(grid.Font, FontStyle.Bold);
            // La sesion en curso va en verde y en negrita, con su ayuda en la celda y
            // "En curso" en el detalle, sin " (en curso)" en el texto: ese sufijo
            // ensanchaba la columna (TextoEntero solo crece) y a 1366 le quitaba
            // al docente unos 70 px hasta cerrar la ventana. Su minimo, la hora en
            // negrita (TextoEntero la mide con la letra normal).
            grid.Columns["horario"].MinimumWidth = TextRenderer.MeasureText("00:00 – 00:00", negrita).Width + 8 + grid.DefaultCellStyle.Padding.Horizontal;
            grid.Columns["horario"].ToolTipText = "En verde y en negrita, la sesión en curso: todavía pueden llegar bitácoras.";

            // --- Abajo: alumnos de la sesion elegida ----------------------------
            // Las dos listas comparten el alto; la division se arrastra.
            SplitContainer division = new SplitContainer
            {
                Orientation = Orientation.Horizontal,
                Location = grid.Location,
                Size = grid.Size,
                Anchor = grid.Anchor,
                BackColor = Color.White,
                SplitterWidth = 10,
                TabStop = false
            };
            division.Panel1MinSize = 110;
            division.Panel2MinSize = 170;
            Controls.Remove(grid);
            grid.Dock = DockStyle.Fill;
            division.Panel1.Controls.Add(grid);
            // Antes el "2*" solo se explicaba al pasar el raton por la celda.
            lblSinConexion = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.FromArgb(80, 80, 80),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = "* En Estudiantes: alguien registró sin conexión con el servidor; su bitácora se guardó en el equipo y se subió después.",
                Visible = false
            };
            division.Panel1.Controls.Add(lblSinConexion);

            Panel encabezado = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.White };
            lblSesionTitulo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 11.5F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(0, 6),
                Size = new Size(encabezado.Width, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            lblSesionDetalle = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(60, 60, 60),
                Location = new Point(0, 32),
                Size = new Size(encabezado.Width, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            encabezado.Controls.Add(lblSesionTitulo);
            encabezado.Controls.Add(lblSesionDetalle);
            // Con la vista previa al lado (a 1366) el titulo ya no cabia en un renglon.
            encabezadoSesion = encabezado;
            encabezado.Resize += (s, e) => AjustarEncabezadoSesion();
            lblSesionTitulo.TextChanged += (s, e) => AjustarEncabezadoSesion();
            lblSesionDetalle.TextChanged += (s, e) => AjustarEncabezadoSesion();

            gridAlumnos = EstiloHorarios.CrearGrid(Point.Empty, new Size(400, 200));
            gridAlumnos.Dock = DockStyle.Fill;
            gridAlumnos.Columns.Add("alumno", "Estudiante");
            gridAlumnos.Columns.Add("matricula", "Matrícula");
            gridAlumnos.Columns.Add("hora", "Hora");
            gridAlumnos.Columns.Add("equipo", "Equipo");
            gridAlumnos.Columns.Add("reporte", "Reporte");
            EstiloHorarios.Pesos(gridAlumnos, 240, 100, 60, 110, 430);
            gridAlumnos.Columns["hora"].ToolTipText = "Hora de su primer registro. Las bitácoras de antes de guardarse la hora muestran «—».";
            // El reporte completo, aunque sea largo: la fila crece.
            gridAlumnos.Columns["reporte"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            gridAlumnos.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            gridAlumnos.CellFormatting += FormatoAlumno;
            gridAlumnos.SelectionChanged += (s, e) => { if (btnCorregir != null) btnCorregir.Enabled = puedeCorregir && AlumnoSeleccionado() != null; };
            // El doble clic y Enter corrigen. Sin poder corregir (el profesor y el
            // encargado de laboratorio) no hacen nada: Enter tampoco baja de fila.
            if (puedeCorregir)
            {
                gridAlumnos.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) CorregirSeleccionado(); };
            }

            gridAlumnos.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    if (puedeCorregir)
                    {
                        CorregirSeleccionado();
                    }
                }
            };

            // Lo que reporto el docente, como en la Bitacora de la clase: fondo naranja
            // claro, barra a la izquierda y los renglones (ReporteProfesor.Renglon).
            pnlReportesDocente = new Panel { Dock = DockStyle.Top, BackColor = Color.White, Padding = new Padding(0, 0, 0, 8), Visible = false };
            pnlFranjaDocente = new Panel { Dock = DockStyle.Fill, BackColor = ColorConFalla };
            pnlFranjaDocente.Paint += PintarMarcoReportes;
            pnlRenglonesDocente = new Panel { AutoScroll = true, BackColor = ColorConFalla };
            lblReportesDocenteTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = TituloReportes,
                Text = idProfesor == 0 ? "Reportado por su docente:" : "Lo que reportaste:",
                UseMnemonic = false
            };
            lblReportesDocente = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.75F),
                ForeColor = TextoReportes,
                UseMnemonic = false
            };
            pnlRenglonesDocente.Controls.Add(lblReportesDocenteTitulo);
            pnlRenglonesDocente.Controls.Add(lblReportesDocente);
            pnlFranjaDocente.Controls.Add(pnlRenglonesDocente);
            pnlReportesDocente.Controls.Add(pnlFranjaDocente);
            pnlReportesDocente.Resize += (s, e) => AcomodarReportesDocente();
            division.Panel2.Resize += (s, e) => AcomodarReportesDocente();

            // Se acomodan de atras hacia adelante: el encabezado arriba, la franja
            // debajo y quien registro en lo que queda.
            division.Panel2.Controls.Add(gridAlumnos);
            division.Panel2.Controls.Add(pnlReportesDocente);
            division.Panel2.Controls.Add(encabezado);
            gridAlumnos.BringToFront();

            // --- A la derecha: como sale el PDF de la sesion elegida ----------
            // El mismo de "PDF de esta sesión" (GenerarPdfSesion), a lo ancho de su
            // panel; las dos listas, a su izquierda como antes. La division se
            // arrastra; FixedPanel: al agrandar la ventana crecen las listas.
            divisionVista = new SplitContainer
            {
                Orientation = Orientation.Vertical,
                Location = division.Location,
                Size = division.Size,
                Anchor = division.Anchor,
                BackColor = Color.White,
                SplitterWidth = 10,
                FixedPanel = FixedPanel.Panel2,
                TabStop = false
            };

            cabeceraVista = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.White };
            cabeceraVista.Resize += (s, e) => ColocarCabeceraVista();
            lblVistaTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 11.5F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(0, 6),
                Text = "Vista previa del PDF",
                UseMnemonic = false
            };
            lnkOcultarVista = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Location = new Point(200, 8),
                Text = "Ocultar"
            };
            lnkOcultarVista.LinkClicked += (s, e) => PonerVistaVisible(false, true);
            ayuda.SetToolTip(lnkOcultarVista, "Esconde la vista previa y las listas se ensanchan. La pestaña «Vista previa del PDF», a la derecha, la vuelve a abrir.");
            lblVistaPagina = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Size = new Size(120, 24),
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblVistaZoom = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Size = new Size(56, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnVistaAlejar = new BotonCabecera("−") { AccessibleName = "Alejar", Font = fuenteSignoVista };
            btnVistaAcercar = new BotonCabecera("+") { AccessibleName = "Acercar", Font = fuenteSignoVista };
            btnVistaAjustar = new BotonCabecera("Ajustar al ancho");
            ayuda.SetToolTip(btnVistaAlejar, "Alejar (Ctrl + rueda del ratón sobre la hoja)");
            ayuda.SetToolTip(btnVistaAcercar, "Acercar (Ctrl + rueda del ratón sobre la hoja)");
            ayuda.SetToolTip(btnVistaAjustar, "La hoja a todo el ancho del panel");
            cabeceraVista.Controls.AddRange(new Control[] { lblVistaTitulo, lnkOcultarVista, lblVistaPagina, btnVistaAlejar, lblVistaZoom, btnVistaAcercar, btnVistaAjustar });

            vista = new VistaPreviaPdf
            {
                Dock = DockStyle.Fill,
                MargenHojas = 14,
                SeparacionHojas = 14,
                // Con el foco en la hoja (tras un clic en ella) las flechas la mueven
                // a ella y no a la lista: un marco verde lo dice.
                MarcarFoco = true,
                // Si no se puede ver aqui: en un equipo del laboratorio tampoco hay
                // lector de PDF (VisorPdf.Mostrar); en los demas, si.
                AyudaSiFalla = Kiosco.Activo
                    ? "Ábrelo desde tu propio equipo o desde el de la administración."
                    : "«PDF de esta sesión» lo abre con el lector de PDF del equipo."
            };
            vista.EstadoCambiado += (s, e) => ActualizarCabeceraVista();
            // El zoom no se lleva el foco: con el raton vuelve a donde estaba (casi
            // siempre la lista de sesiones, y las flechas siguen cambiando de sesion).
            btnVistaAlejar.Click += (s, e) => { vista.Alejar(); DevolverFocoTrasZoom(); };
            btnVistaAcercar.Click += (s, e) => { vista.Acercar(); DevolverFocoTrasZoom(); };
            btnVistaAjustar.Click += (s, e) => { vista.AjustarAlAncho(); DevolverFocoTrasZoom(); };
            foreach (BotonCabecera boton in new[] { btnVistaAlejar, btnVistaAcercar, btnVistaAjustar })
            {
                // Click llega antes que MouseUp: entre los dos, se sabe que fue con el raton.
                boton.MouseDown += (s, e) => zoomConRaton = e.Button == MouseButtons.Left;
                boton.MouseUp += (s, e) => zoomConRaton = false;
            }

            grid.Enter += (s, e) => focoAntesDelZoom = grid;
            gridAlumnos.Enter += (s, e) => focoAntesDelZoom = gridAlumnos;
            vista.Enter += (s, e) => focoAntesDelZoom = vista;
            divisionVista.Panel2.Controls.Add(vista);
            divisionVista.Panel2.Controls.Add(cabeceraVista);
            vista.BringToFront();

            // Escondida, una pestana angosta a la derecha de las listas la vuelve a abrir.
            tiraVista = new TiraVistaPrevia { Dock = DockStyle.Right, Width = 36, Visible = false };
            tiraVista.Click += (s, e) => PonerVistaVisible(true, true);
            ayuda.SetToolTip(tiraVista, AyudaTiraVista);

            division.Dock = DockStyle.Fill;
            divisionVista.Panel1.Controls.Add(division);
            divisionVista.Panel1.Controls.Add(tiraVista);
            division.BringToFront();
            Controls.Add(divisionVista);
            division.SplitterDistance = division.Height * 45 / 100;
            try
            {
                divisionVista.Panel1MinSize = MinimoListas;
                divisionVista.Panel2MinSize = MinimoVista;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                // Una ventana mas angosta que los dos minimos (no pasa: MinimumSize): se quedan los de siempre.
            }

            PonerAnchoVista(AnchoVistaInicial());
            // Solo lo que arrastra la persona (SplitterMoving solo llega al arrastrar): con
            // el panel fijo a la derecha, cada cambio de tamano de la ventana tambien mueve
            // la division y no debe recordarse como su ancho.
            divisionVista.SplitterMoving += (s, e) => arrastrandoVista = true;
            divisionVista.SplitterMoved += (s, e) =>
            {
                if (arrastrandoVista && !moviendoVista && !divisionVista.Panel2Collapsed)
                {
                    anchoVistaRecordado = AnchoVistaActual;
                }

                arrastrandoVista = false;
            };
            esperaVista.Tick += (s, e) => PrepararVista();
            // Sin elegir, se decide al leer las sesiones por primera vez (OnShown):
            // AcomodarVistaAlAbrir, con lo que piden las columnas de la lista.
            if (vistaElegida == false)
            {
                PonerVistaVisible(false, false);
            }

            ColocarCabeceraVista();
            ActualizarCabeceraVista();

            // --- Botones -------------------------------------------------------
            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            // Mas angostos desde "Resolver fallas...": a 1366 de ancho, el resumen de la izquierda no cabria.
            btnLote = BotonAbajo("PDF de las sesiones", 200);
            btnLote.Click += (s, e) => DescargarLote();
            ayuda.SetToolTip(btnLote, "Un PDF por cada sesión que muestra la lista con los filtros de arriba.");
            btnPdf = BotonAbajo("PDF de esta sesión", 170);
            btnPdf.Click += (s, e) => DescargarPdf();
            SeparacionAbajo(20);
            btnCorregir = BotonAbajo("Corregir registro", 155);
            btnCorregir.Click += (s, e) => CorregirSeleccionado();
            // Corregir un registro es solo de la administracion: el profesor y la persona
            // responsable del laboratorio (solo consulta) nunca lo hacen, asi que no lo
            // ven (un boton siempre apagado parecia descompuesto). A la responsable, el
            // subtitulo le dice a quien avisar si ve un error.
            btnCorregir.Visible = puedeCorregir;
            if (puedeCorregir)
            {
                ayuda.SetToolTip(btnCorregir, "Corrige el equipo o las fallas del registro elegido abajo. También con doble clic.");
            }

            // Tambien solo de la administracion (a los demas ni se les crea: no le quita
            // sitio a la linea de abajo), y si la base guarda lo resuelto (db/26): lo
            // enciende ActualizarBotones.
            if (puedeCorregir)
            {
                btnResolver = BotonAbajo("Resolver fallas...", 150);
                btnResolver.Click += (s, e) => ResolverFallas();
                btnResolver.Visible = false;
                ayuda.SetToolTip(btnResolver, "Marca lo que ya se arregló en esta sesión: deja de salir en naranja y en Alertas, y su PDF lo dice al final.");
            }

            esperaDetalle.Tick += (s, e) =>
            {
                esperaDetalle.Stop();
                CargarDetalle(false, null);
            };

            ActiveControl = txtBuscar;
            ActualizarBotones();
        }

        // =====================================================================
        // Sesiones: carga y filtros
        // =====================================================================

        protected override void Recargar(string seleccionar)
        {
            DataTable tabla = null;
            try
            {
                tabla = consultas.ObtenerSesionesAdministrador(idProfesor);
                desfaseServidor = Consultas.DesfaseServidor(tabla);
                conFallasResueltas = tabla.Columns.Contains("fallas_resueltas");
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Leer las sesiones", ex, "No se pudieron leer las sesiones", Text);
            }

            sesiones = tabla == null ? new List<SesionInfo>() : tabla.Rows.Cast<DataRow>().Select(LeerSesion).ToList();
            // Quien ve los codigos pudo cambiar con la ventana abierta (CodigosSoloResponsables).
            grid.Columns["codigo"].Visible = Permisos.VeCodigosDeProfesor;
            RellenarLaboratorios();

            // Lo que se ve abajo tambien se vuelve a leer (pudo registrar alguien), y
            // el PDF de la vista previa se vuelve a generar.
            idSesionMostrada = -1;
            OlvidarVista();
            sesionesLeidas = true;
            string elegir = seleccionar ?? sesionPedida;
            sesionPedida = null;
            Aplicar(elegir);
            cargada = true;

            // La primera vez, ya con las sesiones en la lista (y la ventana a su
            // tamano): el panel de la vista previa, a lo que la lista no necesita, o
            // escondido si a su lado no caben las columnas.
            if (!anchoVistaAjustado)
            {
                anchoVistaAjustado = true;
                AcomodarVistaAlAbrir();
            }
        }

        /// <summary>
        /// Desde una alerta de fallas: ese dia, ese laboratorio y solo las sesiones
        /// con fallas. Si la ventana aun no ha leido las sesiones (se acaba de
        /// abrir), el laboratorio se elige al leerlas; si ya, se vuelven a leer
        /// (pudo llegar algo despues).
        /// </summary>
        public void Filtrar(DateTime dia, string laboratorio, bool soloFallas)
        {
            cargandoFiltros = true;
            txtBuscar.Text = "";
            cboPeriodo.SelectedItem = UnDia;
            dtpDia.Visible = true;
            dtpDia.Value = dia.Date;
            chkSoloFallas.Checked = soloFallas;
            cargandoFiltros = false;
            laboratorioPedido = laboratorio;
            if (cargada)
            {
                Recargar(null);
            }
        }

        /// <summary>
        /// Todas las sesiones que coinciden con ese texto, sin otros filtros: el
        /// "Ver sus bitacoras" de Profesores (su nombre y su numero de empleado).
        /// Como Filtrar, si la ventana ya leyo las sesiones, las vuelve a leer.
        /// </summary>
        public void Buscar(string texto)
        {
            QuitarOtrosFiltros(texto ?? "");
            if (cargada)
            {
                Recargar(null);
            }
        }

        /// <summary>
        /// Esa sesion elegida (con quien registro abajo), en su dia: el "Ver la
        /// sesion" del historial de un alumno cuando no es de una clase del horario
        /// (uso libre o del sistema anterior), que BitacoraSesion no encuentra.
        /// </summary>
        public void MostrarSesion(int idSesion, DateTime? fecha)
        {
            QuitarOtrosFiltros("");
            if (fecha.HasValue)
            {
                cargandoFiltros = true;
                cboPeriodo.SelectedItem = UnDia;
                dtpDia.Visible = true;
                dtpDia.Value = fecha.Value.Date;
                cargandoFiltros = false;
            }

            sesionPedida = idSesion.ToString(CultureInfo.InvariantCulture);
            if (cargada)
            {
                Recargar(null);
            }
        }

        /// <summary>Los filtros como al abrir, con ese texto en la busqueda (sin aplicarlos: los aplica Recargar).</summary>
        private void QuitarOtrosFiltros(string busqueda)
        {
            cargandoFiltros = true;
            txtBuscar.Text = busqueda;
            cboPeriodo.SelectedIndex = 0;
            dtpDia.Visible = false;
            if (cboLaboratorio.Items.Count > 0)
            {
                cboLaboratorio.SelectedIndex = 0;
            }

            chkSoloFallas.Checked = false;
            laboratorioPedido = null;
            cargandoFiltros = false;
        }

        protected override void AlBuscar()
        {
            Aplicar(null);
        }

        private static SesionInfo LeerSesion(DataRow fila)
        {
            SesionInfo sesion = new SesionInfo
            {
                Id = Entero(fila, "idcodigos_accesos"),
                Fecha = LeerFecha(Texto(fila, "fecha")),
                Entrada = LeerHora(fila["hora_entrada"]),
                Salida = LeerHora(fila["hora_salida"]),
                // El codigo es lo que teclean los alumnos: quien no puede verlo (el
                // encargado de laboratorio) no lo tiene en la lista, la busqueda, el
                // detalle ni el nombre del PDF.
                Codigo = Permisos.VeCodigosDeProfesor ? Texto(fila, "codigo") : "",
                Materia = Texto(fila, "materia"),
                Grupo = Texto(fila, "grupo"),
                NumeroEmpleado = Texto(fila, "numero_empleado"),
                Profesor = Texto(fila, "profesor"),
                Laboratorio = Texto(fila, "laboratorio"),
                Alumnos = Entero(fila, "alumnos"),
                ConFallas = Entero(fila, "con_fallas"),
                ReportesDocente = fila.Table.Columns.Contains("reportes_docente") ? Entero(fila, "reportes_docente") : 0,
                Resueltas = (fila.Table.Columns.Contains("fallas_resueltas") ? Entero(fila, "fallas_resueltas") : 0)
                    + (fila.Table.Columns.Contains("reportes_resueltos") ? Entero(fila, "reportes_resueltos") : 0),
                SinConexion = Entero(fila, "sin_conexion"),
                SinInternet = Consultas.ContarSinInternet(new[] { Texto(fila, "sin_internet") }),
                UsoLibre = fila.Table.Columns.Contains("uso_libre") && Entero(fila, "uso_libre") == 1
            };
            if (sesion.UsoLibre)
            {
                // Sin clase: la materia dice que es uso libre (asi sale en la lista,
                // el titulo del detalle, la busqueda, Corregir y el PDF por lotes).
                sesion.Materia = "Uso libre";
                sesion.Grupo = "";
                sesion.Profesor = "";
            }

            sesion.TextoBusqueda = EstiloAdmin.Normalizar(string.Join(" ", sesion.FechaCorta,
                sesion.Fecha.HasValue ? sesion.Fecha.Value.ToString("yyyy-MM-dd") : "",
                sesion.Codigo, sesion.Materia, sesion.Grupo, sesion.NumeroEmpleado, sesion.Profesor, sesion.Laboratorio,
                string.Join(" ", sesion.SinInternet.Select(e => e.Equipo))));
            return sesion;
        }

        /// <summary>
        /// El filtro de arriba con lo que hay en las sesiones leidas: los laboratorios
        /// o, en Mis bitacoras, los grupos con su materia (ClaseDe).
        /// </summary>
        private void RellenarLaboratorios()
        {
            string anterior = laboratorioPedido ?? cboLaboratorio.SelectedItem as string;
            laboratorioPedido = null;
            cargandoFiltros = true;
            cboLaboratorio.Items.Clear();
            cboLaboratorio.Items.Add(porGrupo ? TodosLosGrupos : TodosLosLaboratorios);
            IEnumerable<string> opciones = porGrupo
                ? sesiones.Select(ClaseDe).Where(c => c.Length > 0).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
                : sesiones.Select(s => s.Laboratorio).Where(l => l.Length > 0).Distinct().OrderBy(l => l);
            foreach (string opcion in opciones)
            {
                cboLaboratorio.Items.Add(opcion);
            }

            int indice = anterior == null ? 0 : cboLaboratorio.Items.IndexOf(anterior);
            cboLaboratorio.SelectedIndex = indice < 0 ? 0 : indice;
            int masLarga = 0;
            foreach (object opcion in cboLaboratorio.Items)
            {
                masLarga = Math.Max(masLarga, TextRenderer.MeasureText(opcion.ToString(), cboLaboratorio.Font).Width);
            }

            if (porGrupo)
            {
                // La caja, de lo que mide el grupo con la materia mas larga (entre 220 y 380):
                // "ISC-5001 · Programación Orientada a Objetos" entero. Lo de su derecha se recorre.
                int anchoCaja = Math.Max(220, Math.Min(380, masLarga + 34));
                if (cboLaboratorio.Width != anchoCaja)
                {
                    cboLaboratorio.Width = anchoCaja;
                    chkSoloFallas.Left = cboLaboratorio.Right + 20;
                    lnkQuitarFiltros.Left = cboLaboratorio.Right + DesdeElLaboratorioAQuitarFiltros;
                }
            }

            // La lista que se abre, tan ancha como la opcion mas larga (sin pasar de 640).
            cboLaboratorio.DropDownWidth = Math.Min(640, Math.Max(cboLaboratorio.Width, masLarga + SystemInformation.VerticalScrollBarWidth + 8));
            cargandoFiltros = false;
        }

        /// <summary>
        /// En Mis bitacoras, la clase de una sesion como sale en el filtro: su grupo y su
        /// materia ("ISC-7001 · Programación Web"), o lo que tenga de los dos. El uso
        /// libre no tiene grupo ni docente: no sale.
        /// </summary>
        private static string ClaseDe(SesionInfo sesion)
        {
            if (sesion.UsoLibre)
            {
                return "";
            }

            string grupo = (sesion.Grupo ?? "").Trim();
            string materia = (sesion.Materia ?? "").Trim();
            return grupo.Length == 0 ? materia : materia.Length == 0 ? grupo : grupo + " · " + materia;
        }

        private bool EnPeriodo(SesionInfo sesion, string periodo, DateTime hoy)
        {
            if (periodo == TodasLasFechas)
            {
                return true;
            }

            if (!sesion.Fecha.HasValue)
            {
                return false;
            }

            DateTime fecha = sesion.Fecha.Value.Date;
            switch (periodo)
            {
                case Hoy:
                    return fecha == hoy;
                case UltimaSemana:
                    return fecha > hoy.AddDays(-7) && fecha <= hoy;
                case EsteMes:
                    return fecha.Year == hoy.Year && fecha.Month == hoy.Month;
                case UnDia:
                    return fecha == dtpDia.Value.Date;
                default:
                    return true;
            }
        }

        /// <summary>Filtra en memoria, vuelve a pintar la lista y deja una sesion elegida.</summary>
        private void Aplicar(string seleccionarId)
        {
            if (cargandoFiltros)
            {
                return;
            }

            string periodo = (string)cboPeriodo.SelectedItem;
            // El laboratorio elegido o, en Mis bitacoras, el grupo con su materia (ClaseDe).
            string elegido = cboLaboratorio.SelectedItem as string;
            bool todos = elegido == null || cboLaboratorio.SelectedIndex <= 0;
            DateTime ahora = DateTime.Now + desfaseServidor;
            DateTime hoy = ahora.Date;
            foreach (SesionInfo sesion in sesiones)
            {
                sesion.EnCurso = sesion.EstaEnCurso(ahora);
            }

            visibles = sesiones.Where(s =>
                    EnPeriodo(s, periodo, hoy)
                    && (todos || (porGrupo ? ClaseDe(s) : s.Laboratorio) == elegido)
                    && (!chkSoloFallas.Checked || s.Fallas > 0 || s.SinInternet.Count > 0)
                    && EstiloAdmin.Coincide(s.TextoBusqueda, txtBuscar.Text))
                .ToList();

            // La columna sale cuando algun alumno ya aviso de otro equipo sin
            // internet (sin db/10 nunca: siempre estaria vacia). Con todas las
            // sesiones, no solo las filtradas, para que no aparezca y desaparezca.
            grid.Columns["sin_internet"].Visible = sesiones.Any(s => s.SinInternet.Count > 0);
            // La leyenda del "*", solo si la lista ensena alguno.
            lblSinConexion.Visible = visibles.Any(s => s.SinConexion > 0);

            // Mientras se vacia y se vuelve a llenar la lista, la vista previa no se
            // entera: al final se queda con la sesion elegida (sin parpadear si es la misma).
            llenandoLista = true;
            LlenarLista(visibles,
                s => new object[]
                {
                    s.FechaCorta,
                    s.Horario,
                    EstiloAdmin.Guion(s.Laboratorio),
                    EstiloAdmin.Guion(s.Materia),
                    EstiloAdmin.Guion(s.Grupo),
                    EstiloAdmin.Guion(s.Profesor),
                    // En mayusculas, como se dicta (el nombre del PDF lo lleva como siempre).
                    EstiloAdmin.Codigo(s.Codigo),
                    s.SinConexion > 0 ? s.Alumnos + "*" : s.Alumnos.ToString(),
                    s.TextoFallas,
                    string.Join(", ", s.SinInternet.Select(e => e.Equipo))
                },
                s => false, s => s.Id.ToString(), seleccionarId);
            // El laboratorio y la materia enteros (a 10 pt y 1366 de ancho se cortaban).
            ColumnasDeLista.TextoEntero(grid, 260, "laboratorio");
            ColumnasDeLista.TextoEntero(grid, 300, "materia");
            if (grid.Columns["profesor"].Visible)
            {
                // Las cortas, a lo que piden (su texto o su encabezado): lo que quede,
                // para el docente. En "Mis bitacoras" no hay columna del docente y se
                // quedan con su peso, para llenar el ancho.
                ColumnasDeLista.TextoEntero(grid, 180, "fecha", "horario", "grupo", "codigo", "alumnos", "fallas", "sin_internet");
            }
            foreach (DataGridViewRow fila in grid.Rows)
            {
                SesionInfo sesion = (SesionInfo)fila.Tag;
                if (sesion.EnCurso)
                {
                    fila.Cells["horario"].ToolTipText = "En curso: todavía pueden llegar bitácoras (F5 actualiza).";
                }

                if (sesion.SinConexion > 0)
                {
                    fila.Cells["alumnos"].ToolTipText = Plural(sesion.SinConexion, "registró", "registraron") + " sin conexión con el servidor.";
                }

                if (sesion.SinInternet.Count > 0)
                {
                    fila.Cells["sin_internet"].ToolTipText = Consultas.DescribirSinInternet(sesion.SinInternet, false) + ".";
                }

                string ayudaFallas = sesion.ReportesDocente == 0 ? ""
                    : (sesion.ConFallas == 0 ? "" : Plural(sesion.ConFallas, "estudiante", "estudiantes") + " con fallas y ")
                        + Plural(sesion.ReportesDocente, "falla", "fallas") + (idProfesor == 0 ? " que reportó su docente." : " que reportaste.");
                if (sesion.Resueltas > 0)
                {
                    ayudaFallas += (ayudaFallas.Length > 0 ? " " : "") + (sesion.Resueltas == 1 ? "1 ya resuelta." : sesion.Resueltas + " ya resueltas.");
                }

                if (ayudaFallas.Length > 0)
                {
                    fila.Cells["fallas"].ToolTipText = ayudaFallas;
                }
            }

            MostrarVacio();

            // Siempre hay una elegida (la mas reciente, si no): abajo nunca queda vacio sin motivo.
            if (grid.SelectedRows.Count == 0 && grid.Rows.Count > 0)
            {
                grid.Rows[0].Selected = true;
                grid.CurrentCell = grid.Rows[0].Cells[0];
            }

            llenandoLista = false;

            // La sesion que ya se ve abajo no se vuelve a leer, pero su linea dice lo
            // mismo que la lista: si acaba de empezar o de terminar, "En curso" cambia.
            SesionInfo mostrada = Seleccionado<SesionInfo>();
            if (mostrada != null && mostrada.Id == idSesionMostrada)
            {
                lblSesionDetalle.Text = DescribirSesion(mostrada);
            }

            int bitacoras = visibles.Sum(s => s.Alumnos);
            int conFallas = visibles.Sum(s => s.ConFallas);
            int delDocente = visibles.Sum(s => s.ReportesDocente);
            int resueltas = visibles.Sum(s => s.Resueltas);
            if (sesiones.Count == 0)
            {
                lblResumen.Text = "Todavía no hay sesiones: aparecen cuando alguien usa el código de una clase.";
            }
            else if (visibles.Count == 0)
            {
                lblResumen.Text = "Ninguna sesión coincide con los filtros.";
            }
            else
            {
                string deLasFallas = resueltas > 0
                    // Corto: a 1366 de ancho, junto a los botones, no cabe mucho mas.
                    ? ", " + (conFallas + delDocente == 0 ? "sin fallas por resolver" : Plural(conFallas + delDocente, "falla por resolver", "fallas por resolver"))
                        + ", " + (resueltas == 1 ? "1 resuelta" : resueltas + " resueltas")
                    : (conFallas > 0 ? ", " + conFallas + " con fallas" : ", ninguna con fallas")
                        + (delDocente == 0 ? ""
                            : idProfesor == 0 ? "; " + Plural(delDocente, "falla reportada", "fallas reportadas") + " por docentes"
                            : "; " + Plural(delDocente, "falla", "fallas") + " que reportaste");
                lblResumen.Text = (visibles.Count == sesiones.Count ? "" : visibles.Count + " de ")
                    + Plural(sesiones.Count, "sesión", "sesiones") + ": "
                    + Plural(bitacoras, "bitácora", "bitácoras") + deLasFallas + ".";
            }

            ActualizarBotones();
        }

        private bool HayFiltros
        {
            get
            {
                return txtBuscar.Text.Trim().Length > 0 || cboPeriodo.SelectedIndex > 0
                    || cboLaboratorio.SelectedIndex > 0 || chkSoloFallas.Checked;
            }
        }

        /// <summary>Con la lista vacia se dice por que, en medio de la lista (ListaDigLab.TextoVacio).</summary>
        private void MostrarVacio()
        {
            lnkQuitarFiltros.Visible = HayFiltros;
            ListaDigLab.PonerTextoVacio(grid, sesiones.Count == 0
                ? "Todavía no hay sesiones. Aparecen cuando alguien usa el código de una clase."
                : "Ninguna sesión coincide con los filtros. «Quitar filtros», arriba, los deja como al abrir.");
        }

        private void QuitarFiltros()
        {
            cargandoFiltros = true;
            txtBuscar.Text = "";
            cboPeriodo.SelectedIndex = 0;
            cboLaboratorio.SelectedIndex = 0;
            chkSoloFallas.Checked = false;
            cargandoFiltros = false;
            Aplicar(null);
        }

        protected override void ActualizarBotones()
        {
            if (btnPdf == null)
            {
                return;
            }

            btnPdf.Enabled = Seleccionado<SesionInfo>() != null;
            btnLote.Enabled = visibles.Count > 0;
            btnLote.Text = visibles.Count == 0 ? "PDF de las sesiones"
                : visibles.Count == 1 ? "PDF de 1 sesión"
                : "PDF de las " + visibles.Count + " sesiones";
            btnCorregir.Enabled = puedeCorregir && AlumnoSeleccionado() != null;
            if (btnResolver != null)
            {
                SesionInfo elegida = Seleccionado<SesionInfo>();
                btnResolver.Visible = conFallasResueltas;
                btnResolver.Enabled = elegida != null && elegida.Fallas + elegida.Resueltas + elegida.SinInternet.Count > 0;
            }

            // Al moverse por la lista con las flechas no se consulta en cada fila.
            esperaDetalle.Stop();
            esperaDetalle.Start();
            ProgramarVista();
        }

        /// <summary>Enter o doble clic en una sesion: a sus alumnos.</summary>
        protected override void AlDobleClic()
        {
            esperaDetalle.Stop();
            CargarDetalle(false, null);
            if (gridAlumnos.Rows.Count > 0)
            {
                if (gridAlumnos.SelectedRows.Count == 0)
                {
                    gridAlumnos.Rows[0].Selected = true;
                    gridAlumnos.CurrentCell = gridAlumnos.Rows[0].Cells[0];
                }

                gridAlumnos.Focus();
            }
        }

        /// <summary>La cuenta de fallas en naranja cuando no es cero; los ceros, en gris.</summary>
        private void FormatoSesion(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            SesionInfo sesion = grid.Rows[e.RowIndex].Tag as SesionInfo;
            string columna = grid.Columns[e.ColumnIndex].Name;
            if (sesion == null)
            {
                return;
            }

            if (columna == "fallas" && sesion.Fallas > 0)
            {
                // Elegida o no, la cuenta sigue en naranja; el resto de la fila marca la seleccion.
                e.CellStyle.BackColor = ColorFallaCelda;
                e.CellStyle.SelectionBackColor = ColorFallaCelda;
                e.CellStyle.SelectionForeColor = Color.Black;
                e.CellStyle.Font = negrita;
            }
            else if (columna == "fallas" && sesion.Resueltas > 0)
            {
                // Ya todas resueltas: en verde. Elegida, la cuenta sigue en verde, como el naranja.
                e.CellStyle.BackColor = ColorResueltaCelda;
                e.CellStyle.SelectionBackColor = ColorResueltaCelda;
                e.CellStyle.SelectionForeColor = Color.Black;
                e.CellStyle.Font = negrita;
            }
            else if ((columna == "fallas" && sesion.Fallas == 0) || (columna == "alumnos" && sesion.Alumnos == 0))
            {
                e.CellStyle.ForeColor = Color.Gray;
            }
            else if (columna == "horario" && sesion.EnCurso)
            {
                e.CellStyle.ForeColor = Color.DarkGreen;
                e.CellStyle.Font = negrita;
            }
            else if (columna == "sin_internet")
            {
                // Elegida, con el color de seleccion de siempre: naranja sobre azul no se lee.
                e.CellStyle.ForeColor = ColorAviso;
                e.CellStyle.Font = negrita;
            }
        }

        // =====================================================================
        // Abajo: alumnos de la sesion elegida
        // =====================================================================

        private void CargarDetalle(bool forzar, string seleccionarAlumno)
        {
            SesionInfo sesion = Seleccionado<SesionInfo>();
            int id = sesion == null ? 0 : sesion.Id;
            if (!forzar && id == idSesionMostrada)
            {
                return;
            }

            idSesionMostrada = id;
            firmaMostrada = null;
            gridAlumnos.Rows.Clear();
            if (sesion == null)
            {
                lblSesionTitulo.Text = visibles.Count == 0 ? "" : "Elige una sesión de la lista.";
                lblSesionDetalle.Text = "";
                PonerReportesDocente(new List<ReporteProfesor>());
                btnCorregir.Enabled = false;
                return;
            }

            lblSesionTitulo.Text = EstiloAdmin.Guion(sesion.Materia) + (sesion.Grupo.Length > 0 ? ", " + sesion.Grupo : "");
            lblSesionDetalle.Text = DescribirSesion(sesion);

            DataTable tabla;
            try
            {
                tabla = consultas.consultaRegistroSesion(id);
            }
            catch (Exception ex)
            {
                lblSesionTitulo.Text += ": no se pudo leer quién registró";
                PonerReportesDocente(new List<ReporteProfesor>());
                AvisoDeFalloAdmin.Mostrar(this, "Leer los alumnos de la sesion " + id, ex, "No se pudo leer quién registró en esta sesión", Text);
                return;
            }

            foreach (DataRow fila in tabla.Rows)
            {
                int indice = gridAlumnos.Rows.Add(
                    NombreAlumno(fila),
                    Texto(fila, "numero_identificador"),
                    Texto(fila, "hora_registro"),
                    EstiloAdmin.Guion(Texto(fila, "numero_computadora")),
                    Consultas.DescribirConEstado(fila));
                gridAlumnos.Rows[indice].Tag = fila;
            }

            // Al final, una fila verde claro por cada asistencia que dio su docente (no son
            // bitacoras: no se corrigen): la hora, el equipo y por que.
            List<AsistenciaDada> lasDadas = LeerDadas(id);
            HashSet<string> conBitacora = new HashSet<string>(tabla.Rows.Cast<DataRow>().Select(f => Texto(f, "fk_usuario")));
            foreach (AsistenciaDada dada in lasDadas)
            {
                int indice = gridAlumnos.Rows.Add(
                    string.IsNullOrWhiteSpace(dada.NombreLista) ? dada.Nombre : dada.NombreLista,
                    dada.Matricula,
                    dada.DadaEn.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    EstiloAdmin.Guion(dada.Equipo),
                    (idProfesor == 0 ? "Se la dio su docente: " : "Se la diste: ") + AsistenciasDadas.EnFrase(dada)
                        + (conBitacora.Contains(dada.AlumnoId.ToString(System.Globalization.CultureInfo.InvariantCulture)) ? ". Después llegó su bitácora" : ""));
                gridAlumnos.Rows[indice].Tag = dada;
            }

            List<ReporteProfesor> reportes = LeerReportesDocente(id);
            PonerReportesDocente(reportes);
            // Sin nadie, el porque en medio de la lista, como en las demas.
            ListaDigLab.PonerTextoVacio(gridAlumnos, sesion.EnCurso
                ? "Nadie ha registrado su bitácora todavía. F5 actualiza."
                : "Nadie registró su bitácora en esta sesión.");

            int conFallas = tabla.Rows.Cast<DataRow>().Count(Consultas.FallaPendiente);
            int resueltas = tabla.Rows.Cast<DataRow>().Count(Consultas.Resuelta) + reportes.Count(r => r.Resuelta);
            int dadas = lasDadas.Count;
            // Con esto se sabe si el PDF guardado de la vista previa sigue al dia (PrepararVista).
            firmaMostrada = FirmaDeLaSesion(tabla, dadas, reportes);
            // En "Mis bitacoras" la sesion es del profesor que la ve: se le habla a el.
            lblSesionTitulo.Text += (tabla.Rows.Count == 0
                    ? ": nadie registró bitácora"
                    : ": " + Plural(tabla.Rows.Count, "estudiante", "estudiantes")
                        + (conFallas > 0 ? ", " + conFallas + " con fallas" : resueltas > 0 ? "" : ", sin fallas"))
                + (reportes.Count == 0 ? ""
                    : (idProfesor == 0 ? "; su docente reportó " : "; reportaste ") + Plural(reportes.Count, "falla", "fallas"))
                + (resueltas == 0 ? "" : "; " + (resueltas == 1 ? "1 ya resuelta" : resueltas + " ya resueltas"))
                + (dadas == 0 ? ""
                    : (idProfesor == 0 ? "; su docente dio " : "; diste ") + Plural(dadas, "asistencia", "asistencias") + " sin bitácora");

            gridAlumnos.ClearSelection();
            if (seleccionarAlumno != null)
            {
                // Solo las bitacoras (las asistencias dadas llevan otro Tag).
                EstiloHorarios.SeleccionarFila(gridAlumnos, f => f.Tag is DataRow && Texto((DataRow)f.Tag, "fk_usuario") == seleccionarAlumno);
            }

            btnCorregir.Enabled = puedeCorregir && AlumnoSeleccionado() != null;
        }

        /// <summary>
        /// "lunes 13/07/2026, de 08:57 a 12:00 en CISCO, con NOMBRE (NUMERO). Código CODIGO."
        /// Sin el codigo si no se puede ver (encargado de laboratorio): LeerSesion lo deja vacio.
        /// </summary>
        private string DescribirSesion(SesionInfo sesion)
        {
            string texto = char.ToUpper(sesion.FechaLarga[0]) + sesion.FechaLarga.Substring(1)
                + ", de " + HoraCorta(sesion.Entrada) + " a " + HoraCorta(sesion.Salida)
                + (sesion.Laboratorio.Length > 0 ? " en " + sesion.Laboratorio : "");
            // En "Mis bitacoras" todas son del mismo profesor: no se repite.
            if (sesion.Profesor.Length > 0 && idProfesor == 0)
            {
                texto += ", con " + sesion.Profesor + (sesion.NumeroEmpleado.Length > 0 ? " (" + sesion.NumeroEmpleado + ")" : "");
            }

            return texto + (sesion.Codigo.Length > 0 ? ". Código " + EstiloAdmin.Codigo(sesion.Codigo) : "")
                + (sesion.EnCurso ? ". En curso: pueden llegar más bitácoras (F5 actualiza)." : ".");
        }

        /// <summary>
        /// Las asistencias que dio su docente sin bitacora en esa sesion (db/22; ninguna
        /// sin ella), con el nombre como la lista oficial (NombreLista). Si no se pueden
        /// leer, ninguna y una sola nota en el log: quien registro se ve igual.
        /// </summary>
        private List<AsistenciaDada> LeerDadas(int idSesion)
        {
            try
            {
                return lectorDadas.DeLaSesion(idSesion);
            }
            catch (Exception ex)
            {
                if (!avisoDadas)
                {
                    avisoDadas = true;
                    Log.Aviso("Bitacoras: no se pudieron leer las asistencias dadas de la sesion " + idSesion + ": " + ex.Message);
                }

                return new List<AsistenciaDada>();
            }
        }

        /// <summary>
        /// Lo que reporto su docente en esa sesion (db/15; nada sin ella). Si no se
        /// puede leer, nada y una sola nota en el log: quien registro se ve igual.
        /// </summary>
        private List<ReporteProfesor> LeerReportesDocente(int idSesion)
        {
            try
            {
                return reportesDocente.DeLaSesion(idSesion);
            }
            catch (Exception ex)
            {
                if (!avisoReportes)
                {
                    avisoReportes = true;
                    Log.Aviso("Bitacoras: no se pudieron leer los reportes del docente de la sesion " + idSesion + ": " + ex.Message);
                }

                return new List<ReporteProfesor>();
            }
        }

        /// <summary>La franja con lo que reporto el docente, un renglon por reporte; sin ninguno, oculta.</summary>
        private void PonerReportesDocente(List<ReporteProfesor> reportes)
        {
            string renglones = string.Join(Environment.NewLine, reportes.Select(r => r.RenglonConEstado));
            // Ya todo resuelto (db/26): en verde, sin naranja.
            franjaDocenteResuelta = reportes.Count > 0 && reportes.All(r => r.Resuelta);
            pnlFranjaDocente.BackColor = franjaDocenteResuelta ? ColorResuelta : ColorConFalla;
            pnlRenglonesDocente.BackColor = pnlFranjaDocente.BackColor;
            lblReportesDocenteTitulo.ForeColor = franjaDocenteResuelta ? TextoDada : TituloReportes;
            pnlFranjaDocente.Invalidate();
            lblReportesDocente.Text = renglones;
            pnlFranjaDocente.AccessibleName = renglones.Length == 0 ? null : lblReportesDocenteTitulo.Text + " " + renglones.Replace(Environment.NewLine, " ");
            pnlRenglonesDocente.AutoScrollPosition = Point.Empty;
            AcomodarReportesDocente();
        }

        /// <summary>
        /// El alto de la franja: lo que miden sus renglones partidos al ancho que queda
        /// a la derecha del titulo, hasta un tercio del panel de abajo; si no cabe, se
        /// desplaza. Debajo, 8 px hasta la lista de quien registro.
        /// </summary>
        private void AcomodarReportesDocente()
        {
            bool conFranja = lblReportesDocente.Text.Length > 0;
            if (pnlReportesDocente.Visible != conFranja)
            {
                pnlReportesDocente.Visible = conFranja;
            }

            int ancho = pnlReportesDocente.Width;
            if (!conFranja || ancho < 100)
            {
                return;
            }

            int dentro = ancho - BarraFranja - 2;
            int xRenglones = 10 + lblReportesDocenteTitulo.PreferredSize.Width + 8;
            int anchoRenglones = Math.Max(80, dentro - xRenglones - 10);
            Control abajo = pnlReportesDocente.Parent;
            int maximo = Math.Max(48, (abajo == null ? 300 : abajo.Height) / 3);
            int altoTitulo = lblReportesDocenteTitulo.PreferredSize.Height;
            if (2 + 2 * RellenoFranja + Math.Max(altoTitulo, lblReportesDocente.GetPreferredSize(new Size(anchoRenglones, 0)).Height) > maximo)
            {
                // Con la barra de desplazamiento, los renglones tienen menos ancho.
                anchoRenglones = Math.Max(80, anchoRenglones - SystemInformation.VerticalScrollBarWidth);
            }

            int altoRenglones = lblReportesDocente.GetPreferredSize(new Size(anchoRenglones, 0)).Height;
            int altoFranja = Math.Min(2 + 2 * RellenoFranja + Math.Max(altoTitulo, altoRenglones), maximo);
            Point desplazado = pnlRenglonesDocente.AutoScrollPosition;
            // Los hijos de un panel desplazado se colocan respecto a lo que se ve: primero, arriba del todo.
            pnlRenglonesDocente.AutoScrollPosition = Point.Empty;
            pnlRenglonesDocente.SetBounds(BarraFranja + 1, 1, dentro, altoFranja - 2);
            pnlRenglonesDocente.AutoScrollMargin = new Size(0, RellenoFranja);
            lblReportesDocenteTitulo.Location = new Point(10, RellenoFranja);
            lblReportesDocente.SetBounds(xRenglones, RellenoFranja, anchoRenglones, altoRenglones);
            if (desplazado != Point.Empty)
            {
                pnlRenglonesDocente.AutoScrollPosition = new Point(-desplazado.X, -desplazado.Y);
            }

            int alto = altoFranja + pnlReportesDocente.Padding.Vertical;
            if (pnlReportesDocente.Height != alto)
            {
                pnlReportesDocente.Height = alto;
            }
        }

        /// <summary>El marco de la franja: la barra naranja de la izquierda y un borde de 1 px del mismo naranja.</summary>
        private void PintarMarcoReportes(object sender, PaintEventArgs e)
        {
            Rectangle r = pnlFranjaDocente.ClientRectangle;
            Color color = franjaDocenteResuelta ? ColorResueltaCelda : ColorFallaCelda;
            using (SolidBrush barra = new SolidBrush(color))
            using (Pen borde = new Pen(color))
            {
                e.Graphics.FillRectangle(barra, 0, 0, BarraFranja + 1, r.Height);
                e.Graphics.DrawRectangle(borde, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        /// <summary>
        /// "Arias Mora Gisela", como la lista oficial, Estudiantes y el orden de la
        /// consulta (UsuarioInfo.ComoEnLaLista; el PDF sigue con "Nombre Apellidos").
        /// </summary>
        private static string NombreAlumno(DataRow fila)
        {
            string nombre = UsuarioInfo.ComoEnLaLista(Texto(fila, "nombre"), Texto(fila, "apellido_paterno"), Texto(fila, "apellido_materno"));
            return Entero(fila, "sin_conexion") == 1 ? nombre + " (sin conexión)" : nombre;
        }

        private DataRow AlumnoSeleccionado()
        {
            return gridAlumnos.SelectedRows.Count == 0 ? null : gridAlumnos.SelectedRows[0].Tag as DataRow;
        }

        /// <summary>Fila suave si el alumno reporto algo y el reporte en tono fuerte; "Sin novedad" en gris.</summary>
        private void FormatoAlumno(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            object etiqueta = gridAlumnos.Rows[e.RowIndex].Tag;
            string columna = gridAlumnos.Columns[e.ColumnIndex].Name;
            if (etiqueta is AsistenciaDada)
            {
                // Una asistencia que dio su docente (no es una bitacora): verde claro.
                e.CellStyle.BackColor = ColorDada;
                e.CellStyle.SelectionBackColor = ColorDadaElegida;
                e.CellStyle.ForeColor = columna == "reporte" ? TextoDada : e.CellStyle.ForeColor;
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                return;
            }

            DataRow fila = etiqueta as DataRow;
            if (fila == null)
            {
                return;
            }

            if (Consultas.Resuelta(fila))
            {
                // Ya se arreglo: como una falla, pero en verde. Elegida, el reporte sigue en verde.
                if (columna == "reporte")
                {
                    e.CellStyle.BackColor = ColorResueltaCelda;
                    e.CellStyle.SelectionBackColor = ColorResueltaCelda;
                    e.CellStyle.SelectionForeColor = Color.Black;
                    e.CellStyle.Font = negrita;
                }
                else
                {
                    e.CellStyle.BackColor = ColorResuelta;
                }

                return;
            }

            if (!Consultas.ConFalla(fila))
            {
                // Si solo aviso de otros equipos sin internet, el reporte no va en
                // gris: es algo que revisar, aunque no de su maquina.
                if (columna == "reporte" && Consultas.AvisoSinInternet(fila).Length > 0)
                {
                    e.CellStyle.ForeColor = ColorAviso;
                }
                else if (columna == "reporte" || (columna == "hora" && Texto(fila, "hora_registro") == "—"))
                {
                    e.CellStyle.ForeColor = Color.Gray;
                }

                return;
            }

            // Elegida, la fila va con el color de seleccion de siempre y el reporte
            // sigue en naranja.
            if (columna == "reporte")
            {
                e.CellStyle.BackColor = ColorFallaCelda;
                e.CellStyle.SelectionBackColor = ColorFallaCelda;
                e.CellStyle.SelectionForeColor = Color.Black;
                e.CellStyle.Font = negrita;
            }
            else
            {
                e.CellStyle.BackColor = ColorConFalla;
            }
        }

        private void CorregirSeleccionado()
        {
            if (Permisos.Bloquear(this, "Corregir un registro de bitacora")) return;

            // En "Mis bitacoras" tampoco se corrige, sin mensaje: el profesor no tiene el boton.
            DataRow fila = AlumnoSeleccionado();
            SesionInfo sesion = Seleccionado<SesionInfo>();
            if (fila == null || sesion == null || !puedeCorregir)
            {
                return;
            }

            string alumno = NombreAlumno(fila);
            string idAlumno = Texto(fila, "fk_usuario");
            using (EditarBitacoraAdministrador editor = new EditarBitacoraAdministrador(fila, alumno,
                sesion.FechaCorta + ", " + sesion.Horario + ", " + EstiloAdmin.Guion(sesion.Materia)))
            {
                if (editor.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            Recargar(sesion.Id.ToString());
            esperaDetalle.Stop();
            CargarDetalle(true, idAlumno);
            Avisar("Se corrigió el registro de " + alumno + ".");
        }

        /// <summary>
        /// "Resolver fallas...": lo que se reporto en la sesion elegida, para marcar lo
        /// que ya se arreglo. Lo marcado sale de naranja y de Alertas, y su PDF lo dice
        /// al final (DialogoFallasResueltas). Solo la administracion: el docente y la
        /// persona responsable no tienen el boton.
        /// </summary>
        private void ResolverFallas()
        {
            if (Permisos.Bloquear(this, "Dar por resueltas las fallas de una sesion")) return;
            SesionInfo sesion = Seleccionado<SesionInfo>();
            if (sesion == null || !puedeCorregir)
            {
                return;
            }

            List<FallaDeSesion> fallas;
            try
            {
                fallas = FallasResueltasDatos.DeLaSesion(sesion.Id);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Leer las fallas de la sesion " + sesion.Id, ex, "No se pudieron leer las fallas de esta sesión", Text);
                return;
            }

            if (fallas.Count == 0)
            {
                Avisar("En esta sesión ya no hay nada reportado: alguien lo corrigió mientras tanto.");
                Recargar(sesion.Id.ToString());
                return;
            }

            int marcadas, desmarcadas;
            string deQue = sesion.FechaCorta + ", " + sesion.Horario + ", " + EstiloAdmin.Guion(sesion.Materia)
                + (sesion.Laboratorio.Length > 0 ? ", en " + sesion.Laboratorio : "") + ".";
            using (DialogoFallasResueltas dialogo = new DialogoFallasResueltas(sesion.Id, fallas, deQue))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                marcadas = dialogo.Resueltas;
                desmarcadas = dialogo.Reabiertas;
            }

            Recargar(sesion.Id.ToString());
            esperaDetalle.Stop();
            CargarDetalle(true, null);
            Avisar(marcadas > 0 && desmarcadas > 0 ? "Se resolvieron " + marcadas + " y " + desmarcadas + " quedaron sin resolver otra vez."
                : marcadas == 1 ? "Quedó resuelta."
                : marcadas > 1 ? "Quedaron resueltas " + marcadas + "."
                : desmarcadas == 1 ? "Quedó sin resolver otra vez." : "Quedaron sin resolver otra vez " + desmarcadas + ".");
        }

        // =====================================================================
        // PDF
        // =====================================================================

        private void DescargarPdf()
        {
            SesionInfo sesion = Seleccionado<SesionInfo>();
            if (sesion == null)
            {
                return;
            }

            // Primero se ve como saldra (VisorPdf); desde ahi se elige donde guardarlo.
            byte[] pdf;
            Cursor = Cursors.WaitCursor;
            try
            {
                pdf = PdfDeSesion(sesion);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Generar el PDF de la sesion " + sesion.Id, ex, "No se pudo generar el PDF", Text);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            string ruta;
            try
            {
                ruta = VisorPdf.Mostrar(this, pdf, NombreArchivo(sesion));
            }
            catch (Exception ex)
            {
                // Un fallo de archivo aqui es el temporal de la vista previa, no uno que se eligio.
                AvisoDeFalloAdmin.MostrarVistaPrevia(this, "Mostrar el PDF de la sesion " + sesion.Id, ex, "No se pudo mostrar el PDF", Text);
                return;
            }

            if (ruta != null)
            {
                Avisar("PDF guardado: " + ruta);
            }
        }

        /// <summary>
        /// Las sesiones de la lista (la que dejan los filtros): primero se ve como
        /// saldran, todas en un PDF, una tras otra (VisorPdf); desde ahi se guarda
        /// ese PDF o, con "Uno por sesion...", un PDF por sesion en una carpeta.
        /// </summary>
        private void DescargarLote()
        {
            List<SesionInfo> lote = visibles.ToList();
            if (lote.Count == 0)
            {
                return;
            }

            byte[] pdf;
            Cursor = Cursors.WaitCursor;
            try
            {
                pdf = PdfDeVariasSesiones(lote);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Generar el PDF de " + lote.Count + " sesiones", ex, "No se pudo generar el PDF", Text);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            string ruta;
            try
            {
                ruta = VisorPdf.Mostrar(this, pdf, NombreDelLote(lote), "Uno por sesión...", dueno => GuardarUnoPorSesion(dueno, lote));
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.MostrarVistaPrevia(this, "Mostrar el PDF de " + lote.Count + " sesiones", ex, "No se pudo mostrar el PDF", Text);
                return;
            }

            if (ruta != null)
            {
                Avisar("PDF guardado: " + ruta);
            }
        }

        /// <summary>Todas en un PDF, cada sesion desde una hoja nueva (el mismo PDF de cada una).</summary>
        private byte[] PdfDeVariasSesiones(List<SesionInfo> lote)
        {
            using (MemoryStream salida = new MemoryStream())
            {
                iTextSharp.text.Document documento = new iTextSharp.text.Document();
                iTextSharp.text.pdf.PdfCopy copia = new iTextSharp.text.pdf.PdfSmartCopy(documento, salida) { CloseStream = false };
                documento.Open();
                foreach (SesionInfo sesion in lote)
                {
                    iTextSharp.text.pdf.PdfReader lector = new iTextSharp.text.pdf.PdfReader(PdfDeSesion(sesion));
                    copia.AddDocument(lector);
                    lector.Close();
                }

                documento.Close();
                return salida.ToArray();
            }
        }

        /// <summary>"Bitacoras_24 sesiones_2026-10-01_a_2026-10-24.pdf" (PdfBitacora.NombreArchivo quita lo que Windows no admite).</summary>
        private static string NombreDelLote(List<SesionInfo> lote)
        {
            List<DateTime> fechas = lote.Where(s => s.Fecha.HasValue).Select(s => s.Fecha.Value.Date).ToList();
            string periodo = fechas.Count == 0 ? ""
                : fechas.Min().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                  + (fechas.Max() == fechas.Min() ? "" : "_a_" + fechas.Max().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return PdfBitacora.NombreArchivo("Bitacoras", lote.Count + (lote.Count == 1 ? " sesion" : " sesiones"), periodo);
        }

        /// <summary>"Uno por sesion..." desde la vista previa: un PDF por cada sesion del lote, en una carpeta.</summary>
        private void GuardarUnoPorSesion(IWin32Window dueno, List<SesionInfo> lote)
        {
            if (Kiosco.Activo)
            {
                // En un equipo del laboratorio (Mis bitacoras del profesor) no se abre
                // el dialogo de carpetas de Windows ni el Explorador al terminar:
                // dejarian recorrer el disco del equipo.
                MessageBox.Show(dueno, "En los equipos del laboratorio DigLab no guarda archivos. Descarga los PDF desde tu computadora.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string carpeta;
            using (FolderBrowserDialog dialogo = new FolderBrowserDialog
            {
                Description = "Carpeta donde guardar " + (lote.Count == 1 ? "el PDF de 1 sesión" : "los PDF de las " + lote.Count + " sesiones de la lista"),
                SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (dialogo.ShowDialog(dueno) != DialogResult.OK)
                {
                    return;
                }

                carpeta = dialogo.SelectedPath;
            }

            int hechos = 0;
            List<string> fallidos = new List<string>();
            // Que hacer, el del primer fallo: casi siempre todos fallan por lo mismo (la carpeta, la red).
            string queHacer = null;
            HashSet<string> nombresUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (SesionInfo sesion in lote)
                {
                    string nombre = NombreArchivo(sesion);
                    if (!nombresUsados.Add(nombre))
                    {
                        // Dos sesiones con el mismo codigo (o sin el), fecha, hora y
                        // profesor: se distinguen por su id.
                        nombre = Path.GetFileNameWithoutExtension(nombre) + "_" + sesion.Id + ".pdf";
                        nombresUsados.Add(nombre);
                    }

                    try
                    {
                        File.WriteAllBytes(Path.Combine(carpeta, nombre), PdfDeSesion(sesion));
                        hechos++;
                    }
                    catch (Exception ex)
                    {
                        // La causa, sin el texto tecnico (va al log).
                        Log.Error("Generar el PDF de la sesion " + sesion.Id + " en el lote", ex);
                        fallidos.Add(nombre + ": " + AvisoDeFalloAdmin.Causa(ex));
                        queHacer = queHacer ?? AvisoDeFalloAdmin.Accion(ex);
                    }
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (fallidos.Count > 0)
            {
                MessageBox.Show(dueno, "No se pudieron guardar " + fallidos.Count + " de " + lote.Count + " PDF:\n\n"
                    + string.Join("\n", fallidos.Take(6)) + (fallidos.Count > 6 ? "\n..." : "")
                    + (string.IsNullOrEmpty(queHacer) ? "" : "\n\n" + queHacer),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (hechos > 0)
            {
                Avisar(Plural(hechos, "PDF guardado", "PDF guardados") + " en " + carpeta);
                Abrir(carpeta);
            }
        }

        /// <summary>El PDF de una sesion en memoria: el de VisorPdf, o el que el lote escribe en la carpeta.</summary>
        private byte[] PdfDeSesion(SesionInfo sesion)
        {
            return PdfDeSesion(sesion.Id, consultas, lectorDadas);
        }

        /// <summary>
        /// El mismo, con las consultas que se den: la vista previa de la derecha lo
        /// genera en segundo plano con unas suyas (las de la ventana son de su hilo).
        /// </summary>
        private static byte[] PdfDeSesion(int idSesion, Consultas deLaBase, LectorDeDadas dadas)
        {
            using (MemoryStream memoria = new MemoryStream())
            {
                GenerarPdfSesion(idSesion, deLaBase, dadas, memoria);
                return memoria.ToArray();
            }
        }

        /// <summary>
        /// El PDF de una sesion en destino: encabezado de la plantilla (con el
        /// laboratorio), logos, la tabla de alumnos y, si las hay, las asistencias
        /// que dio su docente sin bitacora (PdfBitacora, el mismo de la bitacora de
        /// una clase).
        /// </summary>
        /// <returns>La firma de lo que lleva (FirmaDeLaSesion), para la vista previa.</returns>
        private static string GenerarPdfSesion(int idSesion, Consultas deLaBase, LectorDeDadas dadas, Stream destino)
        {
            EncabezadoPdfSesion datos = deLaBase.ConsultarDatosPdfSesion(idSesion);
            if (datos == null)
            {
                // Se le ensena tal cual a quien lo pidio (AvisoDeFalloAdmin).
                throw new ErrorDeDatos("La sesión ya no existe en la base: pulsa F5 para actualizar la lista.");
            }

            // Si no se pueden leer, falla el PDF entero (no sale uno al que le faltan).
            DataTable registros = deLaBase.consultaRegistroSesion(idSesion);
            List<AsistenciaDada> dadasDeLaSesion = dadas.DeLaSesion(idSesion);
            List<ReporteProfesor> reportes = new ReportesProfesorDatos().DeLaSesion(idSesion);
            string firma = FirmaDeLaSesion(registros, dadasDeLaSesion.Count, reportes);
            PdfBitacora.Escribir(datos, registros, dadasDeLaSesion, reportes, destino);
            return firma;
        }

        /// <summary>El de la vista previa de la derecha, con sus propias consultas (corre en segundo plano).</summary>
        private static PdfDeVista GenerarPdfDeVista(int idSesion)
        {
            using (MemoryStream memoria = new MemoryStream())
            {
                string firma = GenerarPdfSesion(idSesion, new Consultas(), new LectorDeDadas(), memoria);
                return new PdfDeVista(memoria.ToArray(), firma);
            }
        }

        /// <summary>
        /// Lo que cambia el PDF de una sesion: cada bitacora, con todo lo que trae
        /// consultaRegistroSesion, cuantas asistencias dio su docente sin bitacora y
        /// que fallas reporto (no se corrigen: basta cuales son).
        /// Si lo que se acaba de leer abajo no tiene la misma firma que un PDF guardado
        /// de la vista previa, ese PDF ya es viejo: registro alguien mas (una clase en
        /// curso, un envio que llego tarde), el docente dio una asistencia o reporto
        /// una falla, o se corrigio algo desde otra ventana.
        /// </summary>
        private static string FirmaDeLaSesion(DataTable registros, int dadas, List<ReporteProfesor> reportes)
        {
            StringBuilder firma = new StringBuilder();
            firma.Append(registros.Rows.Count.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(dadas.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(string.Join(",", reportes.Select(r => r.Id.ToString(CultureInfo.InvariantCulture) + (r.Resuelta ? ":" + r.TextoResuelta : ""))));
            foreach (DataRow fila in registros.Rows)
            {
                firma.Append('\n');
                foreach (object valor in fila.ItemArray)
                {
                    firma.Append(Convert.ToString(valor, CultureInfo.InvariantCulture)).Append('\t');
                }
            }

            return firma.ToString();
        }

        /// <summary>
        /// "codigo_fecha_HHmm_laboratorio_profesor.pdf": el mismo codigo tiene una
        /// sesion por fecha, asi que la fecha y la hora de entrada las distinguen.
        /// Para quien no ve los codigos (encargado de laboratorio) el codigo llega
        /// vacio y queda "fecha_HHmm_laboratorio_profesor.pdf": las partes vacias no
        /// se ponen.
        /// </summary>
        private static string NombreArchivo(SesionInfo sesion)
        {
            return PdfBitacora.NombreArchivo(
                sesion.Codigo,
                sesion.Fecha.HasValue ? sesion.Fecha.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                sesion.Entrada.HasValue ? sesion.Entrada.Value.ToString("hhmm") : "",
                sesion.Laboratorio,
                sesion.Profesor.Length > 0 ? sesion.Profesor : sesion.UsoLibre ? "Uso libre" : "Bitacora");
        }

        private void Abrir(string ruta)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = ruta, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Abrir " + ruta, ex);
                Avisar("Se guardó, pero no se pudo abrir: " + ruta);
            }
        }

        // =====================================================================
        // A la derecha: la vista previa del PDF de la sesion elegida
        // =====================================================================

        // La pestana de la derecha, con la vista previa escondida.
        private const string AyudaTiraVista = "Mostrar la vista previa del PDF de la sesión elegida";
        private const string AyudaTiraVistaAngosta = AyudaTiraVista
            + ". En esta pantalla empieza escondida para que se vean todas las columnas de la lista.";

        /// <summary>
        /// El ancho del panel: el que dejo la persona al arrastrar la division
        /// (mientras DigLab siga abierto) o, si no, lo que no necesitan las columnas
        /// de la lista de sesiones: hasta TopeVista con todo entero (a 1920 la hoja
        /// sale a dos tercios de su tamano), PisoVista si para eso basta con angostar
        /// al docente, y entre MinimoVista y PisoVista si solo asi caben las columnas
        /// (la hoja, a algo menos de la mitad). Si ni asi caben (a 1366, y a 1600 en
        /// la de la administracion), 0: escondida, y la lista con todas sus columnas.
        /// Si la persona la abrio (la pestana), PisoVista: la lista, con su barra de abajo.
        /// </summary>
        private int AnchoVistaInicial()
        {
            if (anchoVistaRecordado > 0)
            {
                return anchoVistaRecordado;
            }

            if (!sesionesLeidas)
            {
                return Math.Max(PisoVista, Math.Min(TopeVista, divisionVista.Width / 3));
            }

            int libre = divisionVista.Width - divisionVista.SplitterWidth;
            int sobra = libre - AnchoQuePideLaLista(false);
            if (sobra >= PisoVista)
            {
                return Math.Min(TopeVista, sobra);
            }

            int sobraAngosta = libre - AnchoQuePideLaLista(true);
            if (sobraAngosta >= PisoVista)
            {
                return PisoVista;
            }

            if (sobraAngosta >= MinimoVista)
            {
                return sobraAngosta;
            }

            return vistaElegida == true ? PisoVista : 0;
        }

        /// <summary>
        /// Al leer las sesiones por primera vez: el panel a lo que deja la lista o, si
        /// a su lado no caben sus columnas y la persona no lo eligio, escondido (la
        /// pestana de la derecha lo abre). Lo que eligio la persona, como lo dejo.
        /// </summary>
        private void AcomodarVistaAlAbrir()
        {
            if (divisionVista.Panel2Collapsed)
            {
                return;
            }

            int ancho = AnchoVistaInicial();
            if (ancho <= 0)
            {
                PonerVistaVisible(false, false);
                ayuda.SetToolTip(tiraVista, AyudaTiraVistaAngosta);
                return;
            }

            PonerAnchoVista(ancho);
        }

        /// <summary>
        /// Lo que mide la lista de sesiones con cada columna entera: las de
        /// TextoEntero ya con su ancho; las que llenan (el docente; en Mis bitacoras
        /// las cortas), lo que pide su texto mas largo de las primeras filas. Con
        /// docenteAngosto, el docente a su minimo (con "..." y entero al pasar el raton).
        /// </summary>
        private int AnchoQuePideLaLista(bool docenteAngosto)
        {
            int total = SystemInformation.VerticalScrollBarWidth + 4;
            DrawingFont fuenteEncabezado = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
            foreach (DataGridViewColumn columna in grid.Columns)
            {
                if (!columna.Visible)
                {
                    continue;
                }

                if (columna.AutoSizeMode == DataGridViewAutoSizeColumnMode.None)
                {
                    total += columna.Width;
                    continue;
                }

                if (docenteAngosto && columna.Name == "profesor")
                {
                    total += columna.MinimumWidth;
                    continue;
                }

                DrawingFont fuente = columna.InheritedStyle.Font ?? grid.Font;
                int margen = 8 + columna.InheritedStyle.Padding.Horizontal;
                int pide = Math.Max(columna.MinimumWidth,
                    TextRenderer.MeasureText(columna.HeaderText, fuenteEncabezado).Width + grid.ColumnHeadersDefaultCellStyle.Padding.Horizontal + 8);
                HashSet<string> medidos = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < grid.Rows.Count && i < 300; i++)
                {
                    string texto = Convert.ToString(grid.Rows[i].Cells[columna.Index].Value);
                    if (!string.IsNullOrEmpty(texto) && medidos.Add(texto))
                    {
                        pide = Math.Max(pide, TextRenderer.MeasureText(texto, fuente).Width + margen);
                    }
                }

                total += pide;
            }

            return total;
        }

        /// <summary>
        /// El titulo y la linea de la sesion elegida, enteros: si no caben en un
        /// renglon (con la vista previa al lado, a 1366), pasan a dos y el
        /// encabezado crece. En un renglon, como siempre (60 px).
        /// </summary>
        private void AjustarEncabezadoSesion()
        {
            if (encabezadoSesion == null || lblSesionDetalle == null)
            {
                return;
            }

            int ancho = Math.Max(100, encabezadoSesion.ClientSize.Width);
            int altoTitulo = AltoEnRenglones(lblSesionTitulo, ancho, 24);
            int altoDetalle = AltoEnRenglones(lblSesionDetalle, ancho, 22);
            lblSesionTitulo.Bounds = new Rectangle(0, 6, ancho, altoTitulo);
            lblSesionDetalle.Bounds = new Rectangle(0, 6 + altoTitulo + 2, ancho, altoDetalle);
            int alto = lblSesionDetalle.Bottom + 6;
            if (encabezadoSesion.Height != alto)
            {
                encabezadoSesion.Height = alto;
            }
        }

        /// <summary>El alto de un renglon o, si el texto no cabe a ese ancho, de dos (con "..." si ni asi).</summary>
        private static int AltoEnRenglones(Label etiqueta, int ancho, int unRenglon)
        {
            if (string.IsNullOrEmpty(etiqueta.Text))
            {
                return unRenglon;
            }

            int renglon = TextRenderer.MeasureText("Ág", etiqueta.Font).Height;
            Size medida = TextRenderer.MeasureText(etiqueta.Text, etiqueta.Font, new Size(ancho - 6, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return medida.Height > renglon + 2 ? Math.Max(unRenglon, 2 * renglon + 4) : unRenglon;
        }

        /// <summary>Lo que mide ahora el panel de la vista previa (el de la derecha).</summary>
        private int AnchoVistaActual
        {
            get { return divisionVista.Width - divisionVista.SplitterWidth - divisionVista.SplitterDistance; }
        }

        /// <summary>
        /// El panel de la derecha a ese ancho, dentro de lo que dejan los dos minimos
        /// (la division se mide desde la izquierda: lo que queda para las listas).
        /// </summary>
        private void PonerAnchoVista(int ancho)
        {
            int libre = divisionVista.Width - divisionVista.SplitterWidth;
            int valor = Math.Min(libre - divisionVista.Panel1MinSize, Math.Max(divisionVista.Panel2MinSize, ancho));
            int distancia = libre - valor;
            if (valor < divisionVista.Panel2MinSize || distancia < divisionVista.Panel1MinSize || distancia == divisionVista.SplitterDistance)
            {
                return;
            }

            moviendoVista = true;
            try
            {
                divisionVista.SplitterDistance = distancia;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                // Con la ventana a medio acomodar: se queda como estaba.
            }
            finally
            {
                moviendoVista = false;
            }
        }

        /// <summary>
        /// "Ocultar" y la pestana de la derecha: el panel fuera (las listas se
        /// ensanchan) o de vuelta, con su ancho y la sesion elegida. Lo que elige la
        /// persona (porLaPersona) se recuerda mientras DigLab siga abierto; lo que
        /// decide AcomodarVistaAlAbrir en una pantalla angosta, no.
        /// </summary>
        private void PonerVistaVisible(bool visible, bool porLaPersona)
        {
            if (porLaPersona)
            {
                vistaElegida = visible;
                ayuda.SetToolTip(tiraVista, AyudaTiraVista);
            }

            bool conFoco = divisionVista.Panel2.ContainsFocus || tiraVista.ContainsFocus;
            divisionVista.Panel2Collapsed = !visible;
            tiraVista.Visible = !visible;
            if (visible)
            {
                PonerAnchoVista(AnchoVistaInicial());
                ProgramarVista();
            }
            else
            {
                // Escondida no se genera nada ni se guarda el PDF que se veia.
                esperaVista.Stop();
                pedidoVista++;
                idSesionEnVista = -1;
                vista.MostrarTexto("");
            }

            if (conFoco && grid.CanFocus)
            {
                grid.Focus();
            }
        }

        private void ColocarCabeceraVista()
        {
            if (btnVistaAjustar == null)
            {
                return;
            }

            int derecha = cabeceraVista.ClientSize.Width;
            lnkOcultarVista.Location = new Point(derecha - lnkOcultarVista.Width, 9);
            int y = 34;
            btnVistaAjustar.Location = new Point(derecha - btnVistaAjustar.Width, y);
            btnVistaAcercar.Location = new Point(btnVistaAjustar.Left - 6 - btnVistaAcercar.Width, y);
            lblVistaZoom.Location = new Point(btnVistaAcercar.Left - lblVistaZoom.Width, y + (btnVistaAcercar.Height - lblVistaZoom.Height) / 2);
            btnVistaAlejar.Location = new Point(lblVistaZoom.Left - btnVistaAlejar.Width, y);

            // "Página 12 de 30" a la izquierda de "−" si cabe con dos cifras; en un
            // panel angosto (a menos de unos 390 px), en su propio renglon debajo:
            // ahi se cortaba en "Págin...".
            int libre = btnVistaAlejar.Left - 10;
            int pide = TextRenderer.MeasureText("Página 88 de 88", lblVistaPagina.Font).Width + 6;
            int alto;
            if (libre >= pide)
            {
                lblVistaPagina.Bounds = new Rectangle(0, y + (btnVistaAlejar.Height - 24) / 2, libre, 24);
                alto = 70;
            }
            else
            {
                lblVistaPagina.Bounds = new Rectangle(0, btnVistaAlejar.Bottom + 2, Math.Max(40, derecha), 24);
                alto = lblVistaPagina.Bottom + 6;
            }

            if (cabeceraVista.Height != alto)
            {
                cabeceraVista.Height = alto;
            }
        }

        /// <summary>
        /// Tras −, + o "Ajustar al ancho" con el raton, el foco vuelve a donde estaba
        /// (casi siempre la lista de sesiones: las flechas siguen cambiando de sesion;
        /// si era la hoja, a la hoja, con su marco verde). Con el teclado se queda en
        /// el boton, para volver a pulsarlo.
        /// </summary>
        private void DevolverFocoTrasZoom()
        {
            if (!zoomConRaton)
            {
                return;
            }

            zoomConRaton = false;
            Control destino = focoAntesDelZoom ?? grid;
            if (destino == vista)
            {
                vista.EnfocarHojas();
            }
            else if (destino.CanFocus)
            {
                destino.Focus();
            }
        }

        /// <summary>
        /// La pagina y el zoom de la hoja que se ve; sin hojas, los botones del zoom
        /// apagados. Con un texto gris en vez de PDF (sin sesion, sin WinRT, un
        /// fallo) ni se ven: BotonCabecera apagado se pinta igual que encendido.
        /// Mientras se prepara se quedan, para que no parpadeen al bajar por la lista.
        /// </summary>
        private void ActualizarCabeceraVista()
        {
            lblVistaPagina.Text = vista.TextoPagina;
            lblVistaZoom.Text = vista.TextoZoom;
            bool hayHojas = vista.HayHojas;
            bool conZoom = hayHojas || vista.Mensaje == null;
            foreach (BotonCabecera boton in new[] { btnVistaAlejar, btnVistaAcercar, btnVistaAjustar })
            {
                boton.Enabled = hayHojas;
                boton.Visible = conZoom;
            }
        }

        /// <summary>
        /// La sesion elegida cambio (o la lista se volvio a llenar): la vista dice
        /// enseguida que se prepara y el PDF se genera cuando la lista se queda
        /// quieta (EsperaVista), para no generar uno por cada fila al moverse con
        /// las flechas. Sin sesion elegida, un texto gris.
        /// </summary>
        private void ProgramarVista()
        {
            if (vista == null || llenandoLista || !sesionesLeidas || divisionVista.Panel2Collapsed)
            {
                return;
            }

            SesionInfo sesion = Seleccionado<SesionInfo>();
            int id = sesion == null ? 0 : sesion.Id;
            if (id == idSesionEnVista)
            {
                // Ya se ve, o ya se prepara, esa.
                return;
            }

            idSesionEnVista = id;
            pedidoVista++;
            esperaVista.Stop();
            if (sesion == null)
            {
                vista.MostrarTexto(TextoSinSesion());
                return;
            }

            // Si este Windows no puede dibujar PDF, ni se generan: se dice por que.
            string sinWinRT = VistaPreviaPdf.MotivoSinWinRT();
            if (sinWinRT != null)
            {
                vista.MostrarSinWinRT(sinWinRT);
                return;
            }

            vista.MostrarPreparando();
            esperaVista.Interval = PdfReciente(id, false) != null ? EsperaVistaReciente : EsperaVista;
            esperaVista.Start();
        }

        private string TextoSinSesion()
        {
            return sesiones.Count == 0 ? "Todavía no hay sesiones, así que no hay ningún PDF que mostrar."
                : visibles.Count == 0 ? "Ninguna sesión coincide con los filtros: no hay ningún PDF que mostrar."
                : "Elige una sesión de la lista para ver aquí cómo sale su PDF.";
        }

        /// <summary>
        /// La lista se quedo quieta: el PDF de la sesion elegida, de los recientes o
        /// generado en segundo plano (con sus propias consultas). Uno de los recientes
        /// solo si lleva lo mismo que acaba de leerse abajo (FirmaDeLaSesion): si no,
        /// se vuelve a generar. Lo que llegue cuando ya se eligio otra se guarda
        /// entre los recientes, pero no se pinta.
        /// </summary>
        private void PrepararVista()
        {
            esperaVista.Stop();
            SesionInfo sesion = Seleccionado<SesionInfo>();
            if (sesion == null || sesion.Id != idSesionEnVista || divisionVista.Panel2Collapsed)
            {
                return;
            }

            // Lo de abajo primero, si su espera aun no llego: con lo que se lea se
            // sabe si el PDF guardado sigue al dia (lo iba a leer de todos modos).
            if (idSesionMostrada != sesion.Id)
            {
                esperaDetalle.Stop();
                CargarDetalle(false, null);
                // Un aviso de fallo (modal) pudo cambiar algo mientras tanto.
                if (Seleccionado<SesionInfo>() != sesion || sesion.Id != idSesionEnVista || divisionVista.Panel2Collapsed)
                {
                    return;
                }
            }

            PdfDeVista reciente = PdfReciente(sesion.Id, true);
            if (reciente != null && idSesionMostrada == sesion.Id && firmaMostrada != null && reciente.Firma == firmaMostrada)
            {
                vista.Mostrar(reciente.Pdf);
                return;
            }

            if (reciente != null)
            {
                // Viejo: abajo ya hay otra cosa (o no se pudo leer). Se genera otra vez.
                pdfRecientes.RemoveAll(p => p.Key == sesion.Id);
            }

            int numero = pedidoVista;
            int tanda = tandaRecientes;
            int id = sesion.Id;
            Task.Run(() => GenerarPdfDeVista(id))
                .ContinueWith(tarea => EnLaVentana(() => AlGenerarVista(numero, id, tarea, tanda)), TaskScheduler.Default);
        }

        /// <param name="tanda">La de los recientes cuando se pidio: si despues se volvio a leer (F5), el PDF ya es viejo y no se guarda.</param>
        private void AlGenerarVista(int numero, int id, Task<PdfDeVista> tarea, int tanda)
        {
            if (IsDisposed)
            {
                return;
            }

            if (tarea.IsFaulted || tarea.IsCanceled)
            {
                // De una sesion que ya no se ve no se dice nada; la de ahora, en gris en medio.
                if (numero != pedidoVista)
                {
                    return;
                }

                Exception ex = tarea.Exception == null ? new TaskCanceledException() : tarea.Exception.GetBaseException();
                Log.Error("Vista previa del PDF de la sesion " + id, ex);
                vista.MostrarTexto(AvisoDeFalloAdmin.Texto("No se pudo preparar la vista previa", ex));
                return;
            }

            if (tanda == tandaRecientes)
            {
                GuardarReciente(id, tarea.Result);
            }

            if (numero != pedidoVista || divisionVista.Panel2Collapsed)
            {
                return;
            }

            vista.Mostrar(tarea.Result.Pdf);

            // Si entre leer lo de abajo y generar el PDF registro alguien mas (una clase
            // en curso), lo de abajo se vuelve a leer: los dos dicen lo mismo.
            if (id == idSesionMostrada && firmaMostrada != null && tarea.Result.Firma != firmaMostrada)
            {
                DataRow alumno = AlumnoSeleccionado();
                esperaDetalle.Stop();
                CargarDetalle(true, alumno == null ? null : Texto(alumno, "fk_usuario"));
            }
        }

        /// <summary>Al hilo de la ventana; si ya se cerro, se pierde.</summary>
        private void EnLaVentana(Action accion)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(accion);
            }
            catch (InvalidOperationException)
            {
                // Se cerro mientras tanto.
            }
        }

        /// <summary>El PDF reciente de esa sesion (null si no esta); usar: pasa a ser el mas nuevo.</summary>
        private PdfDeVista PdfReciente(int id, bool usar)
        {
            int indice = pdfRecientes.FindIndex(p => p.Key == id);
            if (indice < 0)
            {
                return null;
            }

            KeyValuePair<int, PdfDeVista> par = pdfRecientes[indice];
            if (usar)
            {
                pdfRecientes.RemoveAt(indice);
                pdfRecientes.Add(par);
            }

            return par.Value;
        }

        private void GuardarReciente(int id, PdfDeVista pdf)
        {
            pdfRecientes.RemoveAll(p => p.Key == id);
            pdfRecientes.Add(new KeyValuePair<int, PdfDeVista>(id, pdf));
            while (pdfRecientes.Count > PdfRecientes)
            {
                pdfRecientes.RemoveAt(0);
            }
        }

        /// <summary>Se volvio a leer (F5, una correccion): los PDF guardados pueden haber cambiado.</summary>
        private void OlvidarVista()
        {
            pdfRecientes.Clear();
            tandaRecientes++;
            pedidoVista++;
            idSesionEnVista = -1;
            esperaVista.Stop();
        }

        /// <summary>
        /// Con la vista previa escondida, la pestana angosta a la derecha de las
        /// listas que la vuelve a abrir: "Vista previa del PDF" de arriba abajo.
        /// </summary>
        private sealed class TiraVistaPrevia : Control
        {
            private const int AnchoPestana = 26;
            private static readonly Color Borde = Color.FromArgb(205, 222, 210);
            private readonly DrawingFont fuente = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold);
            private bool encima;

            public TiraVistaPrevia()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                BackColor = Color.White;
                Cursor = Cursors.Hand;
                TabStop = true;
                Text = "Vista previa del PDF";
                AccessibleRole = AccessibleRole.PushButton;
                AccessibleName = "Mostrar la vista previa del PDF";
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                int largo = TextRenderer.MeasureText(Text, fuente).Width;
                Rectangle pestana = new Rectangle(Width - AnchoPestana, 0, AnchoPestana, Math.Max(40, Math.Min(Height - 1, largo + 46)));
                using (SolidBrush fondo = new SolidBrush(encima ? EstiloHorarios.VerdePulsado : EstiloHorarios.VerdeSobre))
                using (Pen borde = new Pen(Borde))
                {
                    g.FillRectangle(fondo, pestana);
                    g.DrawRectangle(borde, pestana.X, pestana.Y, pestana.Width - 1, pestana.Height - 1);
                }

                TextRenderer.DrawText(g, "«", fuente, new Rectangle(pestana.X, 6, AnchoPestana, 20), Color.DarkGreen,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                using (StringFormat formato = new StringFormat(StringFormatFlags.DirectionVertical | StringFormatFlags.NoWrap))
                using (SolidBrush tinta = new SolidBrush(Color.DarkGreen))
                {
                    formato.Alignment = StringAlignment.Near;
                    formato.LineAlignment = StringAlignment.Center;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    g.DrawString(Text, fuente, tinta, new RectangleF(pestana.X, 30, AnchoPestana, pestana.Height - 34), formato);
                }

                if (Focused && ShowFocusCues)
                {
                    ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(pestana, -3, -3));
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

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    e.Handled = true;
                    OnClick(EventArgs.Empty);
                    return;
                }

                base.OnKeyDown(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    fuente.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        // =====================================================================
        // Utilidades
        // =====================================================================

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                SesionInfo sesion = Seleccionado<SesionInfo>();
                Cursor = Cursors.WaitCursor;
                try
                {
                    Recargar(sesion == null ? null : sesion.Id.ToString());
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                esperaDetalle.Dispose();
                esperaVista.Dispose();
                negrita.Dispose();
                fuenteSignoVista.Dispose();
                pdfRecientes.Clear();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Un encabezado de varias palabras ("Sin internet") se parte en renglones y
        /// basta con que quepa la mas larga, con el margen del encabezado: la lista la
        /// pinta entera desde su ancho mas 6 px. El minimo de EncabezadoEntero se
        /// quedaba 3 px corto y, con la vista previa al lado, las columnas a su minimo
        /// (Mis bitacoras de 1536 a 1760 de ancho) decian "interne".
        /// </summary>
        private static void PalabraEnteraEnEncabezado(DataGridViewColumn columna)
        {
            string texto = columna.HeaderText ?? "";
            DrawingFont fuente = columna.HeaderCell.InheritedStyle.Font ?? columna.DataGridView.Font;
            int palabra = texto.Split(' ').Max(p => TextRenderer.MeasureText(p, fuente).Width);
            int ancho = palabra + columna.HeaderCell.InheritedStyle.Padding.Horizontal;
            if (ancho > columna.MinimumWidth)
            {
                columna.MinimumWidth = ancho;
            }
        }

        private static string HoraCorta(TimeSpan? hora)
        {
            return hora.HasValue ? hora.Value.ToString(@"hh\:mm") : "?";
        }

        private static string Plural(int n, string uno, string varios)
        {
            return n + " " + (n == 1 ? uno : varios);
        }

        private static string Texto(DataRow fila, string columna)
        {
            return !fila.Table.Columns.Contains(columna) || fila[columna] == DBNull.Value ? "" : fila[columna].ToString().Trim();
        }

        private static int Entero(DataRow fila, string columna)
        {
            int numero;
            return int.TryParse(Texto(fila, columna), out numero) ? numero : 0;
        }

        private static DateTime? LeerFecha(string texto)
        {
            DateTime fecha;
            return DateTime.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha)
                ? fecha
                : (DateTime?)null;
        }

        /// <summary>
        /// La hora puede llegar como TIME (TimeSpan) o, por el COALESCE de la
        /// consulta, como texto: "08:57:07", "15:00" o "0 23:59:59.000000" (ver
        /// HorariosDatos.LeerHora).
        /// </summary>
        private static TimeSpan? LeerHora(object valor)
        {
            return HorariosDatos.LeerHora(valor);
        }
    }
}
