using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigBit
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
    /// </summary>
    public class GestionBitacorasAdministrador : PantallaAdmin
    {
        // Fila de una bitacora que reporta algo: fondo suave. La celda con el
        // reporte (o la cuenta de fallas de la sesion) va en un tono fuerte.
        private static readonly Color ColorConFalla = Color.FromArgb(255, 240, 214);
        private static readonly Color ColorFallaCelda = Color.FromArgb(255, 176, 79);
        // Avisos de equipos sin internet: texto naranja, el de los avisos del panel.
        private static readonly Color ColorAviso = Color.FromArgb(190, 85, 0);
        private static readonly string[] DiasCortos = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };
        private static readonly string[] Dias = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };

        private const string TodasLasFechas = "Todas las fechas";
        private const string Hoy = "Hoy";
        private const string UltimaSemana = "Últimos 7 días";
        private const string EsteMes = "Este mes";
        private const string UnDia = "Un día...";
        private const string TodosLosLaboratorios = "Todos los laboratorios";

        private readonly Consultas consultas = new Consultas();
        // Las asistencias que dio el profesor a quien no pudo registrar (db/22): no son
        // bitacoras, asi que no cuentan en la lista; el detalle las dice y el PDF las
        // lleva en su propia tabla.
        private readonly LectorDeDadas lectorDadas = new LectorDeDadas();
        private bool avisoDadas;
        private readonly int idProfesor;
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
            public int SinConexion;
            /// <summary>Equipos que sus alumnos avisaron sin internet (db/10).</summary>
            public List<EquipoSinInternet> SinInternet;
            public string TextoBusqueda;
            /// <summary>Uso libre de un laboratorio sin codigos (db/14): sin clase ni profesor.</summary>
            public bool UsoLibre;

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
                    + " Elige una para ver quién registró y qué reportó. F5 actualiza.",
                // 1400 de ancho pedidos: a 1366x768 PantallaAdmin la deja en lo que
                // cabe (unos 1350) y a 10 pt el laboratorio, la materia, la fecha y el
                // horario no se cortan.
                new Size(1400, 780))
        {
            this.idProfesor = idProfesor;
            puedeCorregir = idProfesor == 0 && !Permisos.SoloConsulta;
            MinimumSize = new Size(1100, 620);
            KeyPreview = true;

            // --- Filtros: se aplican al momento, sin boton ---------------------
            txtBuscar = CrearBuscador(new Point(Margen, 112), 300);
            // Sin el codigo cuando no se puede ver (encargado de laboratorio): LeerSesion no lo guarda.
            ayuda.SetToolTip(txtBuscar, (idProfesor == 0 ? "Materia, docente, grupo" : "Materia, grupo")
                + (Permisos.VeCodigosDeProfesor ? ", laboratorio, código o fecha (13/07/2026)" : ", laboratorio o fecha (13/07/2026)"));

            Controls.Add(EstiloHorarios.CrearEtiqueta("Fecha", new Point(350, 88)));
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

            Controls.Add(EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(670, 88)));
            cboLaboratorio = EstiloHorarios.CrearCombo(new Point(670, 111), 220);
            cboLaboratorio.SelectedIndexChanged += (s, e) => { if (!cargandoFiltros) Aplicar(null); };
            Controls.Add(cboLaboratorio);

            chkSoloFallas = new CheckBox
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(910, 113),
                Text = "Solo con fallas"
            };
            chkSoloFallas.CheckedChanged += (s, e) => Aplicar(null);
            ayuda.SetToolTip(chkSoloFallas, "Las sesiones con alguna falla reportada o con algún aviso de equipos sin internet.");
            Controls.Add(chkSoloFallas);

            lnkQuitarFiltros = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Location = new Point(1050, 114),
                Text = "Quitar filtros",
                Visible = false
            };
            lnkQuitarFiltros.LinkClicked += (s, e) => QuitarFiltros();
            Controls.Add(lnkQuitarFiltros);

            // --- Arriba: sesiones ---------------------------------------------
            CrearLista(156);
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
            // ("mar 13/10/2026") y las cortas, con ListaDigBit; los encabezados de una
            // palabra, enteros. El horario, abajo (con la letra en negrita).
            EstiloHorarios.Pesos(grid, 122, 178, 215, 270, 72, 150, 66, 90, 55, 60);
            grid.Columns["profesor"].Visible = idProfesor == 0;
            // El codigo de cada sesion es lo que teclean los alumnos: el encargado de
            // laboratorio no lo ve (la columna, ademas, va vacia: ver LeerSesion).
            grid.Columns["codigo"].Visible = Permisos.VeCodigosDeProfesor;
            grid.Columns["alumnos"].ToolTipText = "Estudiantes que registraron bitácora en la sesión.";
            grid.Columns["fallas"].ToolTipText = "Estudiantes que reportaron alguna falla o comentario.";
            grid.Columns["sin_internet"].ToolTipText = "Otros equipos que se avisaron sin internet; los más avisados primero.";
            foreach (DataGridViewColumn columna in grid.Columns)
            {
                // Ya vienen de la mas reciente a la mas antigua; ordenar por el
                // texto "lun 13/07/2026" las desordenaria.
                columna.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            // Ya sin ordenar: el encabezado no guarda sitio para la flecha.
            ColumnasDeLista.EncabezadoEntero(grid, "grupo", "codigo", "alumnos", "fallas", "sin_internet");
            grid.Columns["alumnos"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns["fallas"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.CellFormatting += FormatoSesion;
            negrita = new DrawingFont(grid.Font, FontStyle.Bold);
            // La sesion en curso va en verde y en negrita, con su ayuda en la celda y
            // "En curso" en el detalle, sin " (en curso)" en el texto: ese sufijo
            // ensanchaba la columna (TextoEntero solo crece) y a 1366 le quitaba
            // al docente unos 70 px hasta cerrar la ventana. Su minimo, la hora en
            // negrita (TextoEntero la mide con la letra normal).
            grid.Columns["horario"].MinimumWidth = TextRenderer.MeasureText("00:00 – 00:00", negrita).Width + 8;
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
            grid.Paint += PintarListaVacia;
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

            division.Panel2.Controls.Add(gridAlumnos);
            division.Panel2.Controls.Add(encabezado);
            gridAlumnos.BringToFront();
            Controls.Add(division);
            division.SplitterDistance = division.Height * 45 / 100;

            // --- Botones -------------------------------------------------------
            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            btnLote = BotonAbajo("PDF de las sesiones", 230);
            btnLote.Click += (s, e) => DescargarLote();
            ayuda.SetToolTip(btnLote, "Un PDF por cada sesión que muestra la lista con los filtros de arriba.");
            btnPdf = BotonAbajo("PDF de esta sesión", 180);
            btnPdf.Click += (s, e) => DescargarPdf();
            SeparacionAbajo(20);
            btnCorregir = BotonAbajo("Corregir registro", 170);
            btnCorregir.Click += (s, e) => CorregirSeleccionado();
            // Corregir un registro es del administrador; el profesor solo consulta y descarga.
            btnCorregir.Visible = idProfesor == 0;
            if (puedeCorregir)
            {
                ayuda.SetToolTip(btnCorregir, "Corrige el equipo o las fallas del registro elegido abajo. También con doble clic.");
            }
            else
            {
                // El encargado de laboratorio lo ve apagado y, al pasar el raton, el
                // motivo. Para el profesor no hace nada: ni siquiera lo ve.
                Permisos.Desactivar(ayuda, btnCorregir, "Solo la administración de DigBit puede corregir un registro. Si ves un error, avísale.");
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
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Leer las sesiones", ex, "No se pudieron leer las sesiones", Text);
            }

            sesiones = tabla == null ? new List<SesionInfo>() : tabla.Rows.Cast<DataRow>().Select(LeerSesion).ToList();
            // Quien ve los codigos pudo cambiar con la ventana abierta (CodigosSoloResponsables).
            grid.Columns["codigo"].Visible = Permisos.VeCodigosDeProfesor;
            RellenarLaboratorios();

            // Lo que se ve abajo tambien se vuelve a leer (pudo registrar alguien).
            idSesionMostrada = -1;
            string elegir = seleccionar ?? sesionPedida;
            sesionPedida = null;
            Aplicar(elegir);
            cargada = true;
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

        private void RellenarLaboratorios()
        {
            string anterior = laboratorioPedido ?? cboLaboratorio.SelectedItem as string;
            laboratorioPedido = null;
            cargandoFiltros = true;
            cboLaboratorio.Items.Clear();
            cboLaboratorio.Items.Add(TodosLosLaboratorios);
            foreach (string laboratorio in sesiones.Select(s => s.Laboratorio).Where(l => l.Length > 0).Distinct().OrderBy(l => l))
            {
                cboLaboratorio.Items.Add(laboratorio);
            }

            int indice = anterior == null ? 0 : cboLaboratorio.Items.IndexOf(anterior);
            cboLaboratorio.SelectedIndex = indice < 0 ? 0 : indice;
            cargandoFiltros = false;
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
            string laboratorio = cboLaboratorio.SelectedItem as string;
            bool todosLosLaboratorios = laboratorio == null || laboratorio == TodosLosLaboratorios;
            DateTime ahora = DateTime.Now + desfaseServidor;
            DateTime hoy = ahora.Date;
            foreach (SesionInfo sesion in sesiones)
            {
                sesion.EnCurso = sesion.EstaEnCurso(ahora);
            }

            visibles = sesiones.Where(s =>
                    EnPeriodo(s, periodo, hoy)
                    && (todosLosLaboratorios || s.Laboratorio == laboratorio)
                    && (!chkSoloFallas.Checked || s.ConFallas > 0 || s.SinInternet.Count > 0)
                    && EstiloAdmin.Coincide(s.TextoBusqueda, txtBuscar.Text))
                .ToList();

            // La columna sale cuando algun alumno ya aviso de otro equipo sin
            // internet (sin db/10 nunca: siempre estaria vacia). Con todas las
            // sesiones, no solo las filtradas, para que no aparezca y desaparezca.
            grid.Columns["sin_internet"].Visible = sesiones.Any(s => s.SinInternet.Count > 0);
            // La leyenda del "*", solo si la lista ensena alguno.
            lblSinConexion.Visible = visibles.Any(s => s.SinConexion > 0);

            LlenarLista(visibles,
                s => new object[]
                {
                    s.FechaCorta,
                    s.Horario,
                    EstiloAdmin.Guion(s.Laboratorio),
                    EstiloAdmin.Guion(s.Materia),
                    EstiloAdmin.Guion(s.Grupo),
                    EstiloAdmin.Guion(s.Profesor),
                    s.Codigo,
                    s.SinConexion > 0 ? s.Alumnos + "*" : s.Alumnos.ToString(),
                    s.ConFallas.ToString(),
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
            }

            MostrarVacio();

            // Siempre hay una elegida (la mas reciente, si no): abajo nunca queda vacio sin motivo.
            if (grid.SelectedRows.Count == 0 && grid.Rows.Count > 0)
            {
                grid.Rows[0].Selected = true;
                grid.CurrentCell = grid.Rows[0].Cells[0];
            }

            // La sesion que ya se ve abajo no se vuelve a leer, pero su linea dice lo
            // mismo que la lista: si acaba de empezar o de terminar, "En curso" cambia.
            SesionInfo mostrada = Seleccionado<SesionInfo>();
            if (mostrada != null && mostrada.Id == idSesionMostrada)
            {
                lblSesionDetalle.Text = DescribirSesion(mostrada);
            }

            int bitacoras = visibles.Sum(s => s.Alumnos);
            int conFallas = visibles.Sum(s => s.ConFallas);
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
                lblResumen.Text = (visibles.Count == sesiones.Count ? "" : visibles.Count + " de ")
                    + Plural(sesiones.Count, "sesión", "sesiones") + ": "
                    + Plural(bitacoras, "bitácora", "bitácoras")
                    + (conFallas > 0 ? ", " + conFallas + " con fallas" : ", ninguna con fallas") + ".";
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

        private void MostrarVacio()
        {
            lnkQuitarFiltros.Visible = HayFiltros;
            grid.Invalidate();
        }

        /// <summary>Con la lista vacia se dice por que, en medio de la lista.</summary>
        private void PintarListaVacia(object sender, PaintEventArgs e)
        {
            if (visibles.Count > 0)
            {
                return;
            }

            string texto = sesiones.Count == 0
                ? "Todavía no hay sesiones. Aparecen cuando alguien usa el código de una clase."
                : "Ninguna sesión coincide con los filtros. «Quitar filtros», arriba, los deja como al abrir.";
            System.Drawing.Rectangle area = new System.Drawing.Rectangle(0, grid.ColumnHeadersHeight, grid.ClientSize.Width, grid.ClientSize.Height - grid.ColumnHeadersHeight);
            TextRenderer.DrawText(e.Graphics, texto, grid.Font, area, Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
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

            // Al moverse por la lista con las flechas no se consulta en cada fila.
            esperaDetalle.Stop();
            esperaDetalle.Start();
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

            if (columna == "fallas" && sesion.ConFallas > 0)
            {
                // Elegida o no, la cuenta sigue en naranja; el resto de la fila marca la seleccion.
                e.CellStyle.BackColor = ColorFallaCelda;
                e.CellStyle.SelectionBackColor = ColorFallaCelda;
                e.CellStyle.SelectionForeColor = Color.Black;
                e.CellStyle.Font = negrita;
            }
            else if ((columna == "fallas" && sesion.ConFallas == 0) || (columna == "alumnos" && sesion.Alumnos == 0))
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
            gridAlumnos.Rows.Clear();
            if (sesion == null)
            {
                lblSesionTitulo.Text = visibles.Count == 0 ? "" : "Elige una sesión de la lista.";
                lblSesionDetalle.Text = "";
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
                    Consultas.DescribirFallas(fila));
                gridAlumnos.Rows[indice].Tag = fila;
            }

            int conFallas = tabla.Rows.Cast<DataRow>().Count(Consultas.ConFalla);
            int dadas = ContarDadas(id);
            // En "Mis bitacoras" la sesion es del profesor que la ve: se le habla a el.
            lblSesionTitulo.Text += (tabla.Rows.Count == 0
                    ? ": nadie registró bitácora"
                    : ": " + Plural(tabla.Rows.Count, "estudiante", "estudiantes") + (conFallas > 0 ? ", " + conFallas + " con fallas" : ", sin fallas"))
                + (dadas == 0 ? ""
                    : (idProfesor == 0 ? "; su docente dio " : "; diste ") + Plural(dadas, "asistencia", "asistencias") + " sin bitácora");

            gridAlumnos.ClearSelection();
            if (seleccionarAlumno != null)
            {
                EstiloHorarios.SeleccionarFila(gridAlumnos, f => Texto((DataRow)f.Tag, "fk_usuario") == seleccionarAlumno);
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

            return texto + (sesion.Codigo.Length > 0 ? ". Código " + sesion.Codigo : "")
                + (sesion.EnCurso ? ". En curso: pueden llegar más bitácoras (F5 actualiza)." : ".");
        }

        /// <summary>
        /// Cuantas asistencias dio su docente sin bitacora en esa sesion (db/22; 0 sin
        /// ella). Si no se pueden leer, 0 y una sola nota en el log: quien registro se
        /// ve igual.
        /// </summary>
        private int ContarDadas(int idSesion)
        {
            try
            {
                return lectorDadas.Contar(idSesion);
            }
            catch (Exception ex)
            {
                if (!avisoDadas)
                {
                    avisoDadas = true;
                    Log.Aviso("Bitacoras: no se pudieron leer las asistencias dadas de la sesion " + idSesion + ": " + ex.Message);
                }

                return 0;
            }
        }

        /// <summary>"Apellidos, Nombre", como la lista de Alumnos y el orden de la consulta.</summary>
        private static string NombreAlumno(DataRow fila)
        {
            string apellidos = (Texto(fila, "apellido_paterno") + " " + Texto(fila, "apellido_materno")).Trim();
            string nombre = apellidos.Length > 0 ? apellidos + ", " + Texto(fila, "nombre") : Texto(fila, "nombre");
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

            DataRow fila = gridAlumnos.Rows[e.RowIndex].Tag as DataRow;
            if (fila == null)
            {
                return;
            }

            string columna = gridAlumnos.Columns[e.ColumnIndex].Name;
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

        /// <summary>Un PDF por cada sesion de la lista (la que dejan los filtros), en una carpeta.</summary>
        private void DescargarLote()
        {
            List<SesionInfo> lote = visibles.ToList();
            if (lote.Count == 0)
            {
                return;
            }

            if (Kiosco.Activo)
            {
                // En un equipo del laboratorio (Mis bitacoras del profesor) no se abre
                // el dialogo de carpetas de Windows ni el Explorador al terminar:
                // dejarian recorrer el disco del equipo.
                MessageBox.Show(this, "En los equipos del laboratorio DigBit no guarda archivos. Descarga los PDF desde tu computadora.",
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
                if (dialogo.ShowDialog(this) != DialogResult.OK)
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
                MessageBox.Show(this, "No se pudieron guardar " + fallidos.Count + " de " + lote.Count + " PDF:\n\n"
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

        /// <summary>El PDF de una sesion en memoria: el de la vista previa, o el que el lote escribe en la carpeta.</summary>
        private byte[] PdfDeSesion(SesionInfo sesion)
        {
            using (MemoryStream memoria = new MemoryStream())
            {
                GenerarPdfSesion(sesion, memoria);
                return memoria.ToArray();
            }
        }

        /// <summary>
        /// El PDF de una sesion en destino: encabezado de la plantilla (con el
        /// laboratorio), logos, la tabla de alumnos y, si las hay, las asistencias
        /// que dio su docente sin bitacora (PdfBitacora, el mismo de la bitacora de
        /// una clase).
        /// </summary>
        private void GenerarPdfSesion(SesionInfo sesion, Stream destino)
        {
            EncabezadoPdfSesion datos = consultas.ConsultarDatosPdfSesion(sesion.Id);
            if (datos == null)
            {
                // Se le ensena tal cual a quien lo pidio (AvisoDeFalloAdmin).
                throw new ErrorDeDatos("La sesión ya no existe en la base: pulsa F5 para actualizar la lista.");
            }

            // Si no se pueden leer, falla el PDF entero (no sale uno al que le faltan).
            PdfBitacora.Escribir(datos, consultas.consultaRegistroSesion(sesion.Id), lectorDadas.DeLaSesion(sesion.Id), destino);
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
                negrita.Dispose();
            }

            base.Dispose(disposing);
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
