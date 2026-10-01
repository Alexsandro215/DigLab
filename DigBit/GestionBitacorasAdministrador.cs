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
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.tool.xml;
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
    /// </summary>
    public class GestionBitacorasAdministrador : PantallaAdmin
    {
        // Fila de una bitacora que reporta algo: fondo suave. La celda con el
        // reporte (o la cuenta de fallas de la sesion) va en un tono fuerte.
        private static readonly Color ColorConFalla = Color.FromArgb(255, 240, 214);
        private static readonly Color ColorFallaCelda = Color.FromArgb(255, 176, 79);
        private static readonly string[] DiasCortos = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };
        private static readonly string[] Dias = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };

        private const string TodasLasFechas = "Todas las fechas";
        private const string Hoy = "Hoy";
        private const string UltimaSemana = "Últimos 7 días";
        private const string EsteMes = "Este mes";
        private const string UnDia = "Un día...";
        private const string TodosLosLaboratorios = "Todos los laboratorios";

        private readonly Consultas consultas = new Consultas();
        private readonly int idProfesor;
        private readonly Timer esperaDetalle = new Timer { Interval = 120 };
        private readonly DrawingFont negrita;
        private List<SesionInfo> sesiones = new List<SesionInfo>();
        private List<SesionInfo> visibles = new List<SesionInfo>();
        private int idSesionMostrada = -1;
        private bool cargandoFiltros;

        private readonly TextBox txtBuscar;
        private readonly ComboBox cboPeriodo;
        private readonly DateTimePicker dtpDia;
        private readonly ComboBox cboLaboratorio;
        private readonly CheckBox chkSoloFallas;
        private readonly LinkLabel lnkQuitarFiltros;
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
            public string TextoBusqueda;

            public string FechaCorta
            {
                get { return Fecha.HasValue ? DiasCortos[(int)Fecha.Value.DayOfWeek] + " " + Fecha.Value.ToString("dd/MM/yyyy") : "—"; }
            }

            public string FechaLarga
            {
                get { return Fecha.HasValue ? Dias[(int)Fecha.Value.DayOfWeek] + " " + Fecha.Value.ToString("dd/MM/yyyy") : "Sin fecha"; }
            }

            public string Horario
            {
                get { return HoraCorta(Entrada) + " – " + HoraCorta(Salida); }
            }

            /// <summary>Hoy y dentro de su horario: todavia pueden llegar bitacoras.</summary>
            public bool EnCurso
            {
                get
                {
                    DateTime ahora = DateTime.Now;
                    return Fecha.HasValue && Fecha.Value.Date == ahora.Date && Entrada.HasValue && Salida.HasValue
                        && ahora.TimeOfDay >= Entrada.Value && ahora.TimeOfDay <= Salida.Value;
                }
            }
        }

        public GestionBitacorasAdministrador()
            : this(0)
        {
        }

        /// <summary>Con un profesor (su id): su "Mis bitácoras", solo sus sesiones y sin corregir registros.</summary>
        public GestionBitacorasAdministrador(int idProfesor)
            : base(idProfesor == 0 ? "Bitácoras" : "Mis bitácoras",
                (idProfesor == 0 ? "Cada sesión es una clase en una fecha." : "Cada sesión es una de tus clases en una fecha.")
                    + " Elige una para ver quién registró y qué reportó. F5 actualiza.",
                new Size(1240, 780))
        {
            this.idProfesor = idProfesor;
            MinimumSize = new Size(1100, 620);
            KeyPreview = true;

            // --- Filtros: se aplican al momento, sin boton ---------------------
            txtBuscar = CrearBuscador(new Point(Margen, 112), 300);
            ayuda.SetToolTip(txtBuscar, (idProfesor == 0 ? "Materia, profesor, grupo" : "Materia, grupo")
                + ", laboratorio, código o fecha (13/07/2026)");

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
                Format = DateTimePickerFormat.Short,
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
            grid.Columns.Add("profesor", "Profesor");
            grid.Columns.Add("codigo", "Código");
            grid.Columns.Add("alumnos", "Alumnos");
            grid.Columns.Add("fallas", "Fallas");
            EstiloHorarios.Pesos(grid, 100, 100, 170, 210, 75, 200, 65, 70, 60);
            grid.Columns["profesor"].Visible = idProfesor == 0;
            grid.Columns["alumnos"].ToolTipText = "Alumnos que registraron bitácora en la sesión.";
            grid.Columns["fallas"].ToolTipText = "Alumnos que reportaron alguna falla o comentario.";
            foreach (DataGridViewColumn columna in grid.Columns)
            {
                // Ya vienen de la mas reciente a la mas antigua; ordenar por el
                // texto "lun 13/07/2026" las desordenaria.
                columna.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            grid.Columns["alumnos"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns["fallas"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.CellFormatting += FormatoSesion;
            negrita = new DrawingFont(grid.Font, FontStyle.Bold);

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
            gridAlumnos.Columns.Add("alumno", "Alumno");
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
            gridAlumnos.SelectionChanged += (s, e) => { if (btnCorregir != null) btnCorregir.Enabled = AlumnoSeleccionado() != null; };
            gridAlumnos.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) CorregirSeleccionado(); };
            gridAlumnos.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    CorregirSeleccionado();
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
            ayuda.SetToolTip(btnCorregir, "Corrige el equipo o las fallas que registró el alumno elegido abajo. También con doble clic.");
            // Corregir un registro es del administrador; el profesor solo consulta y descarga.
            btnCorregir.Visible = idProfesor == 0;

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
            }
            catch (Exception ex)
            {
                MostrarError("Leer las sesiones", ex, "No se pudieron leer las sesiones");
            }

            sesiones = tabla == null ? new List<SesionInfo>() : tabla.Rows.Cast<DataRow>().Select(LeerSesion).ToList();
            RellenarLaboratorios();

            // Lo que se ve abajo tambien se vuelve a leer (pudo registrar alguien).
            idSesionMostrada = -1;
            Aplicar(seleccionar);
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
                Codigo = Texto(fila, "codigo"),
                Materia = Texto(fila, "materia"),
                Grupo = Texto(fila, "grupo"),
                NumeroEmpleado = Texto(fila, "numero_empleado"),
                Profesor = Texto(fila, "profesor"),
                Laboratorio = Texto(fila, "laboratorio"),
                Alumnos = Entero(fila, "alumnos"),
                ConFallas = Entero(fila, "con_fallas"),
                SinConexion = Entero(fila, "sin_conexion")
            };
            sesion.TextoBusqueda = EstiloAdmin.Normalizar(string.Join(" ", sesion.FechaCorta,
                sesion.Fecha.HasValue ? sesion.Fecha.Value.ToString("yyyy-MM-dd") : "",
                sesion.Codigo, sesion.Materia, sesion.Grupo, sesion.NumeroEmpleado, sesion.Profesor, sesion.Laboratorio));
            return sesion;
        }

        private void RellenarLaboratorios()
        {
            string anterior = cboLaboratorio.SelectedItem as string;
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
            DateTime hoy = DateTime.Today;

            visibles = sesiones.Where(s =>
                    EnPeriodo(s, periodo, hoy)
                    && (todosLosLaboratorios || s.Laboratorio == laboratorio)
                    && (!chkSoloFallas.Checked || s.ConFallas > 0)
                    && EstiloAdmin.Coincide(s.TextoBusqueda, txtBuscar.Text))
                .ToList();

            LlenarLista(visibles,
                s => new object[]
                {
                    s.FechaCorta,
                    s.Horario + (s.EnCurso ? " (en curso)" : ""),
                    EstiloAdmin.Guion(s.Laboratorio),
                    EstiloAdmin.Guion(s.Materia),
                    EstiloAdmin.Guion(s.Grupo),
                    EstiloAdmin.Guion(s.Profesor),
                    s.Codigo,
                    s.SinConexion > 0 ? s.Alumnos + "*" : s.Alumnos.ToString(),
                    s.ConFallas.ToString()
                },
                s => false, s => s.Id.ToString(), seleccionarId);
            foreach (DataGridViewRow fila in grid.Rows)
            {
                SesionInfo sesion = (SesionInfo)fila.Tag;
                if (sesion.SinConexion > 0)
                {
                    fila.Cells["alumnos"].ToolTipText = Plural(sesion.SinConexion, "registró", "registraron") + " sin conexión con el servidor.";
                }
            }

            MostrarVacio();

            // Siempre hay una elegida (la mas reciente, si no): abajo nunca queda vacio sin motivo.
            if (grid.SelectedRows.Count == 0 && grid.Rows.Count > 0)
            {
                grid.Rows[0].Selected = true;
                grid.CurrentCell = grid.Rows[0].Cells[0];
            }

            int bitacoras = visibles.Sum(s => s.Alumnos);
            int conFallas = visibles.Sum(s => s.ConFallas);
            if (sesiones.Count == 0)
            {
                lblResumen.Text = "Todavía no hay sesiones: aparecen cuando un alumno usa el código de una clase.";
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
                ? "Todavía no hay sesiones. Aparecen cuando un alumno usa el código de una clase."
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
            btnCorregir.Enabled = AlumnoSeleccionado() != null;

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
                MostrarError("Leer los alumnos de la sesion " + id, ex, "No se pudo leer quién registró en esta sesión");
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
            lblSesionTitulo.Text += tabla.Rows.Count == 0
                ? ": nadie registró bitácora"
                : ": " + Plural(tabla.Rows.Count, "alumno", "alumnos") + (conFallas > 0 ? ", " + conFallas + " con fallas" : ", sin fallas");

            gridAlumnos.ClearSelection();
            if (seleccionarAlumno != null)
            {
                EstiloHorarios.SeleccionarFila(gridAlumnos, f => Texto((DataRow)f.Tag, "fk_usuario") == seleccionarAlumno);
            }

            btnCorregir.Enabled = AlumnoSeleccionado() != null;
        }

        /// <summary>"lunes 13/07/2026, de 08:57 a 12:00 en CISCO, con NOMBRE (NUMERO). Código CODIGO."</summary>
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

            return texto + ". Código " + sesion.Codigo + (sesion.EnCurso ? ". En curso: pueden llegar más bitácoras (F5 actualiza)." : ".");
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
                if (columna == "reporte" || (columna == "hora" && Texto(fila, "hora_registro") == "—"))
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
            DataRow fila = AlumnoSeleccionado();
            SesionInfo sesion = Seleccionado<SesionInfo>();
            if (fila == null || sesion == null || idProfesor != 0)
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

            string ruta;
            using (SaveFileDialog dialogo = new SaveFileDialog
            {
                Title = "Guardar la bitácora en PDF",
                Filter = "Archivo PDF (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                FileName = NombreArchivo(sesion),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                ruta = dialogo.FileName;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                GenerarPdfSesion(sesion, ruta);
            }
            catch (IOException ex)
            {
                Log.Error("Guardar el PDF " + ruta, ex);
                MessageBox.Show(this, "No se pudo escribir el archivo. ¿Lo tienes abierto en el lector de PDF?\n\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (Exception ex)
            {
                MostrarError("Generar el PDF de la sesion " + sesion.Id, ex, "No se pudo generar el PDF");
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            Avisar("PDF guardado: " + ruta);
            Abrir(ruta);
        }

        /// <summary>Un PDF por cada sesion de la lista (la que dejan los filtros), en una carpeta.</summary>
        private void DescargarLote()
        {
            List<SesionInfo> lote = visibles.ToList();
            if (lote.Count == 0)
            {
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
            HashSet<string> nombresUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (SesionInfo sesion in lote)
                {
                    string nombre = NombreArchivo(sesion);
                    if (!nombresUsados.Add(nombre))
                    {
                        // Dos sesiones con el mismo codigo, fecha y hora: se distinguen por su id.
                        nombre = Path.GetFileNameWithoutExtension(nombre) + "_" + sesion.Id + ".pdf";
                        nombresUsados.Add(nombre);
                    }

                    try
                    {
                        GenerarPdfSesion(sesion, Path.Combine(carpeta, nombre));
                        hechos++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Generar el PDF de la sesion " + sesion.Id + " en el lote", ex);
                        fallidos.Add(nombre + ": " + ex.Message);
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
                    + string.Join("\n", fallidos.Take(6)) + (fallidos.Count > 6 ? "\n..." : ""),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (hechos > 0)
            {
                Avisar(Plural(hechos, "PDF guardado", "PDF guardados") + " en " + carpeta);
                Abrir(carpeta);
            }
        }

        /// <summary>El PDF de una sesion: encabezado de la plantilla, logos y la tabla de alumnos.</summary>
        private void GenerarPdfSesion(SesionInfo sesion, string ruta)
        {
            Usuario datos = consultas.ConsultarDatosPdfSesion(sesion.Id);
            if (datos == null)
            {
                throw new InvalidOperationException("La sesión ya no existe en la base (F5 actualiza la lista).");
            }

            string html = Properties.Resources.plantilla.ToString()
                .Replace("@HORARIODEENTRADA", datos.HoraEntrada)
                .Replace("@HORARIODESALIDA", datos.HoraSalida)
                .Replace("@PROFESOR", sesion.Profesor)
                .Replace("@GRUPO", datos.GruposId)
                .Replace("@MATERIA", datos.MateriasId)
                .Replace("@FECHA", datos.HoraRegistro);

            using (FileStream stream = new FileStream(ruta, FileMode.Create))
            {
                Document pdfDoc = new Document(PageSize.A4, 25, 25, 25, 25);
                PdfWriter writer = PdfWriter.GetInstance(pdfDoc, stream);
                pdfDoc.Open();
                pdfDoc.AddTitle("Bitacora DigBit");
                pdfDoc.AddAuthor("DigBit");

                iTextSharp.text.Image logoTeschi = iTextSharp.text.Image.GetInstance(Properties.Resources.logoteschi, System.Drawing.Imaging.ImageFormat.Png);
                logoTeschi.ScaleToFit(100, 60);
                logoTeschi.Alignment = iTextSharp.text.Image.UNDERLYING;
                logoTeschi.SetAbsolutePosition(470, 750);
                pdfDoc.Add(logoTeschi);

                iTextSharp.text.Image logoEDOMEX = iTextSharp.text.Image.GetInstance(Properties.Resources.estadoMexico, System.Drawing.Imaging.ImageFormat.Png);
                logoEDOMEX.ScaleToFit(100, 60);
                logoEDOMEX.Alignment = iTextSharp.text.Image.UNDERLYING;
                logoEDOMEX.SetAbsolutePosition(40, 750);
                pdfDoc.Add(logoEDOMEX);

                using (StringReader sr = new StringReader(html))
                {
                    XMLWorkerHelper.GetInstance().ParseXHtml(writer, pdfDoc, sr);
                }

                TablaAlumnosPdf.Agregar(pdfDoc, consultas.consultaRegistroSesion(sesion.Id));
                pdfDoc.Close();
            }
        }

        /// <summary>
        /// "codigo_fecha_HHmm_profesor.pdf": el mismo codigo tiene una sesion por
        /// fecha, asi que la fecha y la hora de entrada las distinguen.
        /// </summary>
        private static string NombreArchivo(SesionInfo sesion)
        {
            string[] partes =
            {
                sesion.Codigo,
                sesion.Fecha.HasValue ? sesion.Fecha.Value.ToString("yyyy-MM-dd") : "",
                sesion.Entrada.HasValue ? sesion.Entrada.Value.ToString("hhmm") : "",
                sesion.Profesor.Length > 0 ? sesion.Profesor : "Bitacora"
            };
            string nombre = string.Join("_", partes.Where(p => !string.IsNullOrWhiteSpace(p)));
            foreach (char invalido in Path.GetInvalidFileNameChars())
            {
                nombre = nombre.Replace(invalido, '_');
            }

            return nombre + ".pdf";
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

        /// <summary>La hora puede llegar como TIME (TimeSpan) o, por el COALESCE de la consulta, como texto "08:57:07".</summary>
        private static TimeSpan? LeerHora(object valor)
        {
            if (valor is TimeSpan)
            {
                return (TimeSpan)valor;
            }

            TimeSpan hora;
            return valor != null && valor != DBNull.Value && TimeSpan.TryParse(valor.ToString(), CultureInfo.InvariantCulture, out hora)
                ? hora
                : (TimeSpan?)null;
        }
    }
}
