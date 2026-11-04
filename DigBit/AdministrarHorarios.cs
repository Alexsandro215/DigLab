using DigBit.conexion;
using DigBit.Infraestructura;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;
using iText = iTextSharp.text;
using iTextPdf = iTextSharp.text.pdf;

namespace DigBit
{
    /// <summary>
    /// Fase 6 (administrador): clases con codigo fijo, horario semanal por
    /// laboratorio y excepciones por dia. Ver docs/fase6-horarios.md.
    /// Todo construido en codigo, sin Designer (como HistorialCodigosProfesor).
    /// </summary>
    public class AdministrarHorarios : Form
    {
        private readonly HorariosDatos datos = new HorariosDatos();
        private readonly Consultas consultas = new Consultas();
        private int? idAdministrador;

        private Label lblTitulo;
        // "Solo consulta", junto al titulo grande (la persona responsable del laboratorio).
        private Label lblSoloConsulta;
        // Lo que se hace en la pestana que se ve (FraseDe), en el renglon de abajo del titulo.
        private Label lblSubtitulo;
        private Button btnCerrar;
        private TabControl tabs;
        // Las pestanas se dibujan a mano (DibujarPestana): con la letra y el verde de DigBit.
        private readonly DrawingFont fuentePestana = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
        private TabPage tabClases;
        private TabPage tabHorario;
        private TabPage tabExcepciones;
        private TabPage tabCalendario;
        // Pestana Faltas: que clases se quedaron sin bitacoras (PestanaCalendario).
        private PestanaCalendario calendarioBitacoras;
        // Pestana Proximo ciclo (decisiones del 2026-10-25): plantillas, preparacion
        // y aplicar con fechas (PestanaProximoCiclo), en azul grisaceo; en solo
        // consulta, "Horarios guardados". Null si quien abrio la ventana no es la
        // administracion ni la persona responsable del laboratorio (VeConfiguraciones).
        private TabPage tabProximo;
        private PestanaProximoCiclo proximoCiclo;
        // Mientras un laboratorio elegido en una pestana se pone en las demas
        // (CompartirLaboratorio): las que no se ven no se vuelven a leer.
        private bool compartiendo;

        // Pestana Clases
        private DataGridView dgvClases;
        private Button btnClaseNueva;
        private Button btnClaseEditar;
        private Button btnClaseVerHorario;
        private Button btnClaseEstado;
        private Button btnClaseActualizar;
        private Label lblClasesCantidad;
        // Arriba a la derecha: lo que paso con la ultima accion ("Listo: se creo...",
        // "Esta clase todavia no tiene horas...").
        private Label lblClasesEstado;
        // Buscador y lista de profesores: filtran lo ya leido, sin volver a la base.
        private TextBox txtBuscarClase;
        private ComboBox cboProfesorClases;
        private readonly Timer esperaBusquedaClases = new Timer { Interval = 250 };
        private List<ClaseInfo> clasesLeidas = new List<ClaseInfo>();
        // Hoy segun el servidor al leer las clases: decide si una ya termino o aun no empieza.
        private DateTime hoyClases = DateTime.Today;

        // Pestana Horario semanal
        private ComboBox cboLabHorario;
        // Las flechas del laboratorio (Deslizamiento): Horario semanal y Cambios de un dia.
        private FlechasDeLista flechasLabHorario;
        private FlechasDeLista flechasLabExcepciones;
        // La leyenda en dos renglones: arriba, como esta el laboratorio (el acceso y
        // si "Cambiar horas" vale desde una fecha o tambien para lo pasado, en verde
        // o en ambar); debajo, en gris, como se usa la cuadricula.
        private Label lblEstadoHorario;
        private Label lblLeyendaHorario;
        // A la derecha de los docentes: los choques del horario en uso en la semana
        // que se ve (todos los laboratorios) y el salto a Faltas.
        private PastillaConfiguracion pastChoques;
        private readonly DrawingFont fuentePastilla = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private List<ChoqueEnUso> choquesDeLaSemana = new List<ChoqueEnUso>();
        // Con db/25: "Falta asignar: N h" (todos los laboratorios), a la izquierda de
        // los choques; lo que se leyo, y si se puede reemplazar (sin db/25, como antes).
        private PastillaConfiguracion pastPorAsignar;
        private List<HoraPorAsignar> porAsignar = new List<HoraPorAsignar>();
        private bool conPorAsignar;
        // "Ponerla..." de la lista: la clase que el siguiente "Poner clase..." trae elegida.
        private HoraPorAsignar porPoner;
        // "Ver en el horario" de una clase que todavia no tiene horas (la recien
        // creada con "Nueva clase..."): lleva a Horario semanal y, como porPoner, el
        // siguiente "Poner clase..." la trae elegida. Esc la olvida.
        private ClaseInfo sinHorasPorPoner;
        private LinkLabel lnkFaltasDeLaSemana;
        // Una etiqueta por profesor del laboratorio, con su color (PonerLeyenda).
        private FlowLayoutPanel leyendaProfesores;
        // Una para todos los chips de la leyenda, que se rehacen en cada recarga.
        private readonly DrawingFont fuenteChip = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private int profesorFijado;
        private Panel contenedorSemana;
        private CuadriculaSemanal lienzo;
        // La semana que se ve: cada columna es una fecha de ella y salen sus
        // cancelaciones y sesiones extra, tambien las que ya pasaron. Al abrir, la
        // de hoy; luego, la que se elija.
        private SelectorSemana selectorSemana;
        private bool semanaElegida;
        // Si los cambios de horario aplican desde una fecha (VigenciaFranjas.ConFecha),
        // leido en cada CargarFranjas: entonces asignar una clase ya no es "todas las semanas".
        private bool cambiosConFecha;
        private ContextMenuStrip menuFranja;
        private ContextMenuStrip menuHueco;
        private ContextMenuStrip menuExtra;
        private ExcepcionInfo extraEnMenu;
        private Button btnAsignar;
        private Button btnExcepcion;


        private Button btnFranjaEditar;
        private Button btnFranjaBorrar;
        // "Imprimir PDF": la semana que se ve, sin codigos (no lo hay en kiosco).
        private Button btnImprimir;


        // Pestana Cambios de un dia (antes Excepciones)
        private ComboBox cboPeriodo;
        private CalendarioFiltro calendario;
        private ComboBox cboLabExcepciones;
        private Label lblDiaAgenda;
        private LinkLabel lnkSemanaDeCambios;
        private DataGridView dgvAgenda;
        private Button btnAgendaActualizar;
        private Button btnCancelarDia;
        private Button btnQuitarCancelacion;
        private Button btnSesionExtra;
        private Button btnQuitarExtra;
        // Con "Una semana" o "Un mes" (solo los cambios) no se cancela ni se da nada: lo dice en lugar de los botones.
        private Label lblSoloEnUnDia;

        // El ultimo laboratorio que se miro en esta sesion: al volver a abrir
        // Horarios se sigue donde se quedo, en vez de en el primero de la lista.
        private static int ultimoLaboratorio;

        private readonly int laboratorioInicial;
        private readonly ToolTip ayuda = new ToolTip();
        // Solo consulta (el encargado de laboratorio, ver Permisos): se ve todo,
        // pero sin los botones, menus y dobles clics que escriben.
        private readonly bool soloConsulta = Permisos.SoloConsulta;

        public AdministrarHorarios() : this(0)
        {
        }

        /// <summary>Con un laboratorio (desde el panel del administrador), abre su horario semanal.</summary>
        public AdministrarHorarios(int laboratorioInicial)
        {
            this.laboratorioInicial = laboratorioInicial;
            InicializarComponentes();
            CargarLaboratorios();
            CargarClases();
            // Abre en Horario semanal, la primera pestana (ya elegida: no pasa por
            // tabs_SelectedIndexChanged, asi que su frase se pone aqui).
            tabs.SelectedTab = tabHorario;
            PonerFrase();
            ActiveControl = cboLabHorario;

            // Si otra ventana reparte los colores (Profesores al guardar, el panel
            // cada 30 s), la leyenda y la lista de Clases, que copiaron el color al
            // crearse, se vuelven a pintar.
            ColoresProfesor.Cambio += ColoresCambiaron;
            // Si cambian los codigos de las clases (panel, Ajustes, Laboratorios), la
            // leyenda "Acceso: ..." y los codigos de cada clase se vuelven a leer.
            ModosDeAcceso.Cambio += ModosCambiaron;
        }

        private void ModosCambiaron()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(ModosCambiaron));
                return;
            }

            CargarFranjas();
            // La columna Codigo de la agenda depende del modo: si se esta viendo, se
            // rehace (un codigo del profesor que ya no vale no debe quedar a la vista).
            if (tabs.SelectedTab == tabExcepciones)
            {
                CargarAgenda();
            }
        }

        private void ColoresCambiaron()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(ColoresCambiaron));
                return;
            }

            foreach (Control chip in leyendaProfesores.Controls)
            {
                chip.BackColor = ColoresProfesor.De((int)chip.Tag);
            }

            foreach (DataGridViewRow fila in dgvClases.Rows)
            {
                ClaseInfo clase = fila.Tag as ClaseInfo;
                if (clase != null)
                {
                    PintarCeldaProfesor(fila.Cells[3], clase.ProfesorId);
                }
            }

            lienzo.Invalidate();
            cboProfesorClases.Invalidate();
        }

        /// <summary>La celda del profesor lleva su color, el mismo con el que se pintan sus franjas.</summary>
        private static void PintarCeldaProfesor(DataGridViewCell celda, int idProfesor)
        {
            Color color = CuadriculaSemanal.ColorDeProfesor(idProfesor);
            celda.Style.BackColor = color;
            celda.Style.SelectionBackColor = EstiloHorarios.Oscurecer(color, 0.18f);
            celda.Style.SelectionForeColor = Color.Black;
        }

        /// <summary>Lleva la ventana ya abierta al horario semanal de ese laboratorio.</summary>
        public void MostrarLaboratorio(int idLaboratorio)
        {
            tabs.SelectedTab = tabHorario;
            SeleccionarPorId(cboLabHorario, idLaboratorio);
        }

        /// <summary>
        /// Lleva a la pestana Faltas con ese laboratorio y la semana de esa fecha
        /// (desde Alertas: "Ver en Faltas"; desde Horario semanal: "Ver las faltas
        /// de esta semana"). Vale antes y despues de ensenar la ventana, y con
        /// Faltas ya abierta en otro laboratorio u otro mes. Las demas pestanas
        /// siguen a ese laboratorio.
        /// </summary>
        public void IrAFaltas(int idLaboratorio, DateTime fecha)
        {
            // Sin pasar por tabs_SelectedIndexChanged: leeria el calendario con el
            // laboratorio de antes y aqui se vuelve a leer con el pedido.
            tabs.SelectedIndexChanged -= tabs_SelectedIndexChanged;
            try
            {
                tabs.SelectedTab = tabCalendario;
            }
            finally
            {
                tabs.SelectedIndexChanged += tabs_SelectedIndexChanged;
            }

            PonerFrase();
            CompartirLaboratorio(idLaboratorio, null);
            // La primera vez, Cargar elige ese laboratorio y lee el mes de hoy; si
            // ya estaba leido, lo vuelve a leer y aqui se cambia de laboratorio.
            calendarioBitacoras.Cargar(idLaboratorio);
            calendarioBitacoras.ElegirLaboratorio(idLaboratorio);

            // La semana de esa fecha (si es de otro mes, lo lee).
            calendarioBitacoras.IrA(fecha.Date);
        }

        /// <summary>
        /// El laboratorio elegido en una pestana pasa a las demas (Horario semanal,
        /// Cambios de un dia, Faltas y la preparacion de Proximo ciclo), sin volver
        /// a leer las que no se ven: cada una se lee al elegirla. origen: la que lo
        /// eligio (no se toca), o null.
        /// </summary>
        private void CompartirLaboratorio(int idLaboratorio, object origen)
        {
            if (compartiendo || idLaboratorio <= 0)
            {
                return;
            }

            compartiendo = true;
            try
            {
                ultimoLaboratorio = idLaboratorio;
                if (origen != cboLabHorario)
                {
                    SeleccionarSiEsta(cboLabHorario, idLaboratorio);
                }

                if (origen != cboLabExcepciones)
                {
                    SeleccionarSiEsta(cboLabExcepciones, idLaboratorio);
                }

                if (origen != calendarioBitacoras)
                {
                    calendarioBitacoras.ElegirLaboratorio(idLaboratorio);
                }

                if (proximoCiclo != null && origen != proximoCiclo)
                {
                    proximoCiclo.ElegirLaboratorio(idLaboratorio);
                }
            }
            finally
            {
                compartiendo = false;
            }
        }

        /// <summary>Elige ese laboratorio si esta en la lista (uno desactivado solo sale en Proximo ciclo); si no, deja el que estaba.</summary>
        private static void SeleccionarSiEsta(ComboBox combo, int id)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                OpcionCombo opcion = combo.Items[i] as OpcionCombo;
                if (opcion != null && opcion.Id == id)
                {
                    if (combo.SelectedIndex != i)
                    {
                        combo.SelectedIndex = i;
                    }

                    return;
                }
            }
        }

        // =====================================================================
        // Construccion de la interfaz
        // =====================================================================

        private void InicializarComponentes()
        {
            BackColor = Color.White;
            ClientSize = new Size(1000, 640);
            // Lo bastante ancha para la fila de botones de Horarios (con "Imprimir PDF").
            MinimumSize = new Size(1120, 700);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            // Abre a pantalla completa: la cuadricula se estira y se leen enteros
            // los nombres, la materia, el grupo y el codigo de cada clase.
            WindowState = FormWindowState.Maximized;
            // El icono de DigBit en su barra: el del panel del administrador, como Mi horario.
            Icon = (Icon)new System.ComponentModel.ComponentResourceManager(typeof(PrincipalAdministrador)).GetObject("$this.Icon");
            // En solo consulta lo dice la barra de titulo (el titulo grande no cambia).
            Text = soloConsulta ? "Horarios por laboratorio (solo consulta)" : "Horarios por laboratorio";

            lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = EstiloHorarios.VerdeTeschi,
                Location = new Point(28, 14),
                Text = "Horarios por laboratorio"
            };

            // Solo consulta: lo dice tambien junto al titulo grande, no solo la barra de
            // titulo; se pulsa para ver que puede hacer aqui (como en las demas secciones).
            lblSoloConsulta = EstiloAdmin.EtiquetaSoloConsulta(ayuda);
            lblSoloConsulta.Visible = soloConsulta;
            lblSoloConsulta.Location = new Point(lblTitulo.Left + TextRenderer.MeasureText(lblTitulo.Text, lblTitulo.Font).Width + 8, 24);

            // Lo que se hace en la pestana que se ve (PonerFrase, al elegirla); en un renglon.
            lblSubtitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Regular),
                // Gris, como el subtitulo de las demas ventanas de la administracion.
                ForeColor = EstiloAdmin.TextoSecundario,
                Location = new Point(30, 48),
                Text = ""
            };

            // A 20 del borde del ancho que ya tiene (MinimumSize pudo agrandarla).
            btnCerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(ClientSize.Width - 20 - 130, 16), new Size(130, 38));
            btnCerrar.Click += (s, e) => Close();
            btnCerrar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ayuda.SetToolTip(btnCerrar, "Cierra Horarios por laboratorio (Esc). F5 vuelve a leer la pestaña que ves.");

            // Del tamano que de verdad tiene la ventana: MinimumSize ya la agrando y,
            // con el de diseno, abajo quedaba un hueco que las anclas conservaban.
            // Las pestanas, dibujadas a mano y mas grandes que las de Windows (que se
            // perdian): la elegida en verde con letra blanca, las demas en verde claro.
            tabs = new TabControl
            {
                Location = new Point(20, 76),
                Size = new Size(ClientSize.Width - 40, ClientSize.Height - 76 - 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                DrawMode = TabDrawMode.OwnerDrawFixed,
                SizeMode = TabSizeMode.Fixed,
                // No mas altas: a 1366x768 la cuadricula de Horario semanal cabe justa.
                // 156 de ancho: "Horarios guardados", la mas larga, mide 139 px; las
                // 5 ocupan 780 px, menos que el ancho minimo de la ventana.
                ItemSize = new Size(156, 26)
            };
            tabs.DrawItem += DibujarPestana;
            Disposed += (s, e) =>
            {
                fuentePestana.Dispose();
                fuentePastilla.Dispose();
            };
            tabs.SelectedIndexChanged += tabs_SelectedIndexChanged;

            // En este orden: lo de todos los dias (la semana), de que esta hecho el
            // horario (las clases), lo que cambia un solo dia, lo que ya paso (Faltas)
            // y, aparte y en azul grisaceo, lo que todavia no se usa (Proximo ciclo).
            tabHorario = new TabPage("Horario semanal") { BackColor = Color.White };
            tabClases = new TabPage("Clases") { BackColor = Color.White };
            tabExcepciones = new TabPage("Cambios de un día") { BackColor = Color.White };
            tabCalendario = new TabPage("Faltas") { BackColor = Color.White };

            ConstruirPestanaClases();
            ConstruirPestanaHorario();
            ConstruirPestanaExcepciones();
            calendarioBitacoras = new PestanaCalendario(IdAdministrador) { Dock = DockStyle.Fill };
            calendarioBitacoras.VerSemanaEnHorario += (laboratorio, lunes) =>
            {
                CompartirLaboratorio(laboratorio, calendarioBitacoras);
                IrASemanaDeHorarios(lunes);
            };
            calendarioBitacoras.LaboratorioCambiado += id => CompartirLaboratorio(id, calendarioBitacoras);
            tabCalendario.Controls.Add(calendarioBitacoras);

            // En solo consulta, "Horarios guardados": solo la lista, para verlos
            // (especificacion v2, 4.4 y 4.6; PestanaProximoCiclo lo decide).
            if (VeConfiguraciones)
            {
                tabProximo = new TabPage(PestanaProximoCiclo.NombrePestana) { BackColor = Color.White };
                proximoCiclo = new PestanaProximoCiclo(IdAdministrador, IrASemanaDeHorarios) { Dock = DockStyle.Fill };
                proximoCiclo.LaboratorioCambiado += id => CompartirLaboratorio(id, proximoCiclo);
                tabProximo.Controls.Add(proximoCiclo);
            }

            tabs.TabPages.Add(tabHorario);
            tabs.TabPages.Add(tabClases);
            tabs.TabPages.Add(tabExcepciones);
            tabs.TabPages.Add(tabCalendario);
            if (tabProximo != null)
            {
                tabs.TabPages.Add(tabProximo);
            }

            Controls.Add(lblTitulo);
            Controls.Add(lblSoloConsulta);
            Controls.Add(lblSubtitulo);
            Controls.Add(btnCerrar);
            Controls.Add(tabs);
        }

        /// <summary>
        /// El renglon de abajo del titulo: para que sirve la pestana que se ve
        /// (uno por pestana, segun la cuenta). Al elegir una pestana.
        /// </summary>
        private void PonerFrase()
        {
            TabPage pestana = tabs.SelectedTab;
            string frase;
            if (pestana == tabClases)
            {
                frase = soloConsulta
                    ? "Cada clase es un docente con una materia y un grupo, y las fechas en que vale."
                    : "Cada clase es un docente con una materia y un grupo, y las fechas en que vale. Sus horas se ponen en Horario semanal.";
            }
            else if (pestana == tabExcepciones)
            {
                frase = soloConsulta
                    ? "Las clases canceladas un solo día y las sesiones extra. Las registra la administración."
                    : "Cancelar una clase un solo día o dar una sesión extra. Las demás semanas no cambian.";
            }
            else if (pestana == tabCalendario)
            {
                // Los colores, solo en la leyenda de la pestana (LeyendaDelMes): antes se
                // explicaban en tres sitios y ninguno completo.
                frase = "Qué clases ya pasadas se dieron y cuáles no, por laboratorio y por semana.";
            }
            else if (tabProximo != null && pestana == tabProximo)
            {
                frase = PestanaProximoCiclo.Frase;
            }
            else
            {
                frase = soloConsulta
                    ? "El horario que se usa en cada laboratorio, semana por semana. Pasa el ratón por una clase para ver sus datos."
                    : "El horario que se usa en cada laboratorio, semana por semana. Aquí se ponen, cambian y quitan las horas de cada clase.";
            }

            lblSubtitulo.Text = frase;
        }

        private void ConstruirPestanaClases()
        {
            // Arriba, para dar rapido con un profesor: lo que se teclea filtra al
            // dejar de escribir (nombre, materia, grupo, codigo o no. de empleado,
            // sin importar acentos ni mayusculas), y la lista deja solo uno.
            // Sin los codigos de los profesores (solo consulta), el codigo no se
            // ensena ni se busca: la guia y la ayuda no lo mencionan.
            bool conCodigo = Permisos.VeCodigosDeProfesor;
            Label lblBuscar = EstiloHorarios.CrearEtiqueta("Buscar", new Point(12, 15));
            txtBuscarClase = EstiloHorarios.CrearTexto(new Point(80, 11), 330);
            EstiloHorarios.PonerTextoGuia(txtBuscarClase, conCodigo ? "Docente, materia, grupo o código" : "Docente, materia o grupo");
            ayuda.SetToolTip(txtBuscarClase, (conCodigo ? "Docente, materia, grupo, código o número de empleado" : "Docente, materia, grupo o número de empleado")
                + "; sin importar acentos ni mayúsculas.\n"
                + "Varias palabras: salen las clases que tengan todas («rivera 6isc»). Flecha abajo: a la lista. Esc: borrar.");
            txtBuscarClase.TextChanged += (s, e) => { esperaBusquedaClases.Stop(); esperaBusquedaClases.Start(); };
            txtBuscarClase.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && dgvClases.Rows.Count > 0)
                {
                    dgvClases.Focus();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape && txtBuscarClase.Text.Length > 0)
                {
                    txtBuscarClase.Clear();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            esperaBusquedaClases.Tick += (s, e) => { esperaBusquedaClases.Stop(); MostrarClases(); };

            Label lblProfesor = EstiloHorarios.CrearEtiqueta("Docente", new Point(430, 15));
            cboProfesorClases = EstiloHorarios.CrearCombo(new Point(510, 11), 400);
            cboProfesorClases.DrawMode = DrawMode.OwnerDrawFixed;
            cboProfesorClases.ItemHeight = 20;
            cboProfesorClases.MaxDropDownItems = 20;
            cboProfesorClases.DrawItem += DibujarProfesorConColor;
            cboProfesorClases.SelectedIndexChanged += ProfesorClasesCambiado;

            dgvClases = EstiloHorarios.CrearGrid(new Point(12, 46), new Size(928, 406));
            dgvClases.Columns.Add("Codigo", "Código");
            dgvClases.Columns.Add("Materia", "Materia");
            dgvClases.Columns.Add("Grupo", "Grupo");
            dgvClases.Columns.Add("Profesor", "Docente");
            dgvClases.Columns.Add("Desde", "Vigente desde");
            dgvClases.Columns.Add("Hasta", "Vigente hasta");
            // Estado: activa, desactivada, terminada (vigencia acabada), termina hoy o
            // por empezar. Sesiones: todas, con y sin bitacoras (con alguna se termina, no se desactiva).
            dgvClases.Columns.Add("Activa", "Estado");
            dgvClases.Columns.Add("Sesiones", "Sesiones");
            // Con la letra de 10 pt la materia mas larga cabe entera a 1366: las fechas le
            // dan algo de lo que les sobra (2026-11-03: con 24 se cortaba "Fundamentos de
            // Gestion Empresarial", que pide 288 px y tenia 279).
            EstiloHorarios.Pesos(dgvClases, 9, 26, 10, 23, 11, 11, 10, 9);
            // Con el codigo de su profesor entran los alumnos: solo el administrador
            // lo ve. Se oculta, no se quita: las celdas se usan por posicion.
            dgvClases.Columns["Codigo"].Visible = conCodigo;
            dgvClases.SelectionChanged += (s, e) => ActualizarBotonesClases();
            // Y al cambiar la fila actual (de ella sale la clase): elegida desde el codigo,
            // la fila puede cambiar sin que cambie la seleccion, y el boton diria otra cosa.
            dgvClases.CurrentCellChanged += (s, e) => ActualizarBotonesClases();
            if (!soloConsulta)
            {
                // Doble clic: editar la clase (en solo consulta no hace nada).
                dgvClases.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditarClase(); };
            }

            lblClasesCantidad = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(12, 472),
                Text = "Clases: 0"
            };

            btnClaseActualizar = EstiloHorarios.CrearBoton("Actualizar", new Point(392, 462), new Size(120, 38));
            btnClaseActualizar.Click += (s, e) => CargarClases();

            btnClaseNueva = EstiloHorarios.CrearBoton("Nueva clase...", new Point(524, 462), new Size(150, 38));
            btnClaseNueva.Click += (s, e) => NuevaClase();
            ayuda.SetToolTip(btnClaseNueva, "Una clase nueva: docente, grupo, materia y las fechas en que vale. Sus horas se ponen después en Horario semanal.");

            btnClaseEditar = EstiloHorarios.CrearBoton("Cambiar...", new Point(656, 462), new Size(120, 38));
            btnClaseEditar.Click += (s, e) => EditarClase();
            ayuda.SetToolTip(btnClaseEditar, "Cambia el código, las fechas o (si aún no tiene historia) el docente, el grupo o la materia de la clase elegida (doble clic).");

            // Tambien en solo consulta: solo lleva a otra pestana.
            btnClaseVerHorario = EstiloHorarios.CrearBoton("Ver en el horario", new Point(656, 462), new Size(160, 38));
            btnClaseVerHorario.Click += (s, e) => VerClaseEnHorario();
            ayuda.SetToolTip(btnClaseVerHorario, "Lleva a Horario semanal, al laboratorio donde la clase elegida tiene más horas, con su docente resaltado.");

            // "Activar", "Desactivar" o, con sesiones, "Terminar clase..." (ActualizarBotonesClases):
            // dice que hara con la clase elegida; sin ninguna, "Desactivar" apagado.
            btnClaseEstado = EstiloHorarios.CrearBoton("Desactivar", new Point(788, 462), new Size(170, 38));
            btnClaseEstado.Click += (s, e) => CambiarEstadoClase();

            // Solo consulta: quedan Actualizar y Ver en el horario (AcomodarClases los lleva a la derecha).
            Permisos.Ocultar(btnClaseNueva, btnClaseEditar, btnClaseEstado);
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnClaseEditar, () => "Elige primero una clase de la lista.");
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnClaseVerHorario, () => "Elige primero una clase de la lista.");
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnClaseEstado, () => "Elige primero una clase de la lista.");

            // A la derecha del buscador, en dos renglones si hace falta (entera, al pasar el raton).
            lblClasesEstado = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(926, 6),
                Size = new Size(10, 36),
                TextAlign = ContentAlignment.MiddleLeft
            };

            tabClases.Controls.Add(lblBuscar);
            tabClases.Controls.Add(txtBuscarClase);
            tabClases.Controls.Add(lblProfesor);
            tabClases.Controls.Add(cboProfesorClases);
            tabClases.Controls.Add(lblClasesEstado);
            tabClases.Controls.Add(dgvClases);
            tabClases.Controls.Add(lblClasesCantidad);
            tabClases.Controls.Add(btnClaseActualizar);
            tabClases.Controls.Add(btnClaseNueva);
            tabClases.Controls.Add(btnClaseEditar);
            tabClases.Controls.Add(btnClaseVerHorario);
            tabClases.Controls.Add(btnClaseEstado);
            tabClases.Resize += (s, e) => AcomodarClases();
        }

        /// <summary>
        /// La linea de estado de Clases: verde lo que salio bien, ambar lo que
        /// falta por hacer; vacia, no se ve. Entera al pasar el raton.
        /// </summary>
        private void EstadoClases(string texto, bool aviso)
        {
            lblClasesEstado.Text = texto ?? "";
            lblClasesEstado.ForeColor = aviso ? Color.FromArgb(156, 87, 0) : Color.DarkGreen;
            ayuda.SetToolTip(lblClasesEstado, string.IsNullOrEmpty(texto) ? null : texto);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            AcomodarClases();
            AcomodarExcepciones();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ColoresProfesor.Cambio -= ColoresCambiaron;
            ModosDeAcceso.Cambio -= ModosCambiaron;
            ayuda.Dispose();
            esperaBusquedaClases.Dispose();
            base.OnFormClosed(e);
        }

        /// <summary>
        /// Una pestana con la letra y el verde de DigBit: la elegida, verde oscuro
        /// con letra blanca; las demas, verde claro con letra verde. Con el foco del
        /// teclado (Ctrl+Tab, flechas), el recuadro punteado.
        /// </summary>
        private void DibujarPestana(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= tabs.TabPages.Count)
            {
                return;
            }

            bool elegida = e.Index == tabs.SelectedIndex;
            // Proximo ciclo, en azul grisaceo: es otro horario, no el que se usa.
            bool otroHorario = tabProximo != null && tabs.TabPages[e.Index] == tabProximo;
            Color oscuro = otroHorario ? PestanaProximoCiclo.AzulOscuro : Color.DarkGreen;
            Color claro = otroHorario ? PestanaProximoCiclo.AzulClaro : Color.FromArgb(232, 244, 232);
            Rectangle zona = e.Bounds;
            using (SolidBrush fondo = new SolidBrush(elegida ? oscuro : claro))
            {
                e.Graphics.FillRectangle(fondo, zona);
            }

            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, fuentePestana, zona, elegida ? Color.White : oscuro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (elegida && (e.State & DrawItemState.Focus) != 0 && tabs.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(zona, -4, -4), Color.White, oscuro);
            }
        }

        /// <summary>
        /// Esc cierra Horarios (como Cerrar). No le quita Esc a lo que ya lo usa:
        /// una lista desplegada la cierra (no llega aqui) y el buscador de Clases
        /// con texto lo borra (su KeyDown). Con cuadros o una franja elegidos en la
        /// cuadricula, el primer Esc los suelta (como "Quitar la seleccion") y el
        /// siguiente cierra: soltar una seleccion no cierra la ventana. Solo
        /// cuentan los cuadros de los dias: el de una hora (columna 0) queda
        /// elegido con un clic o con las flechas pero no se pinta, y soltarlo no
        /// cambiaria nada a la vista.
        /// </summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape && tabs.SelectedTab == tabHorario
                && (lienzo.SelectedCells.Cast<DataGridViewCell>().Any(c => c.ColumnIndex >= 1) || lienzo.Seleccionada != null))
            {
                lienzo.LimpiarSeleccion();
                ActualizarBotonesFranjas();
                return true;
            }

            // Sin nada elegido, Esc olvida la clase traida de "Falta asignar" (o la sin
            // horas de "Ver en el horario") antes de cerrar.
            if (keyData == Keys.Escape && tabs.SelectedTab == tabHorario && (porPoner != null || sinHorasPorPoner != null))
            {
                porPoner = null;
                sinHorasPorPoner = null;
                PonerLeyendaHorario();
                return true;
            }

            // Proximo ciclo: Esc vuelve atras dentro de la pestana (cuadros elegidos,
            // lista de clases, plantilla abierta, paso 3) y, sin a donde, cierra.
            if (keyData == Keys.Escape && proximoCiclo != null && tabs.SelectedTab == tabProximo && proximoCiclo.ManejarEscape())
            {
                return true;
            }

            if (keyData == Keys.Escape && !(txtBuscarClase.Focused && txtBuscarClase.TextLength > 0))
            {
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        /// <summary>F5: vuelve a leer la pestana que se ve, como sus botones Actualizar.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    ActualizarPestana();
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ActualizarPestana()
        {
            if (tabs.SelectedTab == tabClases)
            {
                CargarClases();
            }
            else if (tabs.SelectedTab == tabHorario)
            {
                CargarFranjas();
            }
            else if (tabs.SelectedTab == tabExcepciones)
            {
                CargarAgenda();
            }
            else if (tabs.SelectedTab == tabCalendario)
            {
                // Ya cargada: vuelve a leer lo que se ve (su Actualizar).
                OpcionCombo laboratorio = LaboratorioHorario();
                calendarioBitacoras.Cargar(laboratorio == null ? 0 : laboratorio.Id);
            }
            else if (proximoCiclo != null && tabs.SelectedTab == tabProximo)
            {
                proximoCiclo.Cargar();
            }
        }

        // Las pestanas Clases y Excepciones se acomodan a mano a su tamano cada vez
        // que cambia: con anclas no crecian, porque se ponian cuando la ventana ya
        // estaba maximizada y se quedaban con el tamano de diseno.

        /// <summary>La lista ocupa todo lo que deja el buscador; los botones, abajo a la derecha.</summary>
        private void AcomodarClases()
        {
            int ancho = tabClases.ClientSize.Width;
            int alto = tabClases.ClientSize.Height;
            if (ancho < 400 || alto < 200)
            {
                return;
            }

            int abajo = alto - btnClaseEstado.Height - 12;
            dgvClases.SetBounds(12, dgvClases.Top, ancho - 24, abajo - 10 - dgvClases.Top);
            if (soloConsulta)
            {
                // Nueva clase, Cambiar y Activar/Desactivar no se ven: Ver en el horario
                // y Actualizar, en la esquina.
                btnClaseVerHorario.Location = new Point(ancho - 12 - btnClaseVerHorario.Width, abajo);
                btnClaseActualizar.Location = new Point(btnClaseVerHorario.Left - 12 - btnClaseActualizar.Width, abajo);
            }
            else
            {
                btnClaseEstado.Location = new Point(ancho - 12 - btnClaseEstado.Width, abajo);
                btnClaseVerHorario.Location = new Point(btnClaseEstado.Left - 12 - btnClaseVerHorario.Width, abajo);
                btnClaseEditar.Location = new Point(btnClaseVerHorario.Left - 12 - btnClaseEditar.Width, abajo);
                btnClaseNueva.Location = new Point(btnClaseEditar.Left - 12 - btnClaseNueva.Width, abajo);
                btnClaseActualizar.Location = new Point(btnClaseNueva.Left - 12 - btnClaseActualizar.Width, abajo);
            }

            lblClasesCantidad.Location = new Point(12, abajo + 10);
            int izquierdaEstado = cboProfesorClases.Right + 16;
            lblClasesEstado.SetBounds(izquierdaEstado, 6, Math.Max(10, ancho - 12 - izquierdaEstado), 36);
        }

        /// <summary>El calendario se queda a la izquierda; la agenda crece hacia la derecha y hacia abajo.</summary>
        private void AcomodarExcepciones()
        {
            int ancho = tabExcepciones.ClientSize.Width;
            int alto = tabExcepciones.ClientSize.Height;
            if (ancho < 400 || alto < 200)
            {
                return;
            }

            int abajo = alto - btnQuitarExtra.Height - 12;
            btnAgendaActualizar.Left = ancho - 12 - btnAgendaActualizar.Width;
            // El enlace a la semana, a la derecha del titulo del dia.
            lnkSemanaDeCambios.Location = new Point(ancho - 12 - lnkSemanaDeCambios.Width, lblDiaAgenda.Top + 2);
            lblDiaAgenda.Width = Math.Max(100, lnkSemanaDeCambios.Left - 12 - lblDiaAgenda.Left);
            // En solo consulta no estan los botones de abajo: la agenda llega hasta abajo.
            int finAgenda = soloConsulta ? alto - 12 : abajo - 10;
            dgvAgenda.SetBounds(dgvAgenda.Left, dgvAgenda.Top, ancho - 12 - dgvAgenda.Left, finAgenda - dgvAgenda.Top);
            btnCancelarDia.Top = abajo;
            btnQuitarCancelacion.Location = new Point(btnCancelarDia.Right + 12, abajo);
            btnQuitarExtra.Location = new Point(ancho - 12 - btnQuitarExtra.Width, abajo);
            btnSesionExtra.Location = new Point(btnQuitarExtra.Left - 12 - btnSesionExtra.Width, abajo);
            lblSoloEnUnDia.SetBounds(btnCancelarDia.Left, abajo, ancho - 12 - btnCancelarDia.Left, btnCancelarDia.Height);
        }

        private void ConstruirPestanaHorario()
        {
            Label lblLab = EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(12, 16));

            cboLabHorario = EstiloHorarios.CrearCombo(new Point(110, 12), 300);
            cboLabHorario.SelectedIndexChanged += (s, e) =>
            {
                OpcionCombo elegido = cboLabHorario.SelectedItem as OpcionCombo;
                if (elegido != null)
                {
                    ultimoLaboratorio = elegido.Id;
                }

                // Puesto desde otra pestana (CompartirLaboratorio): se lee al volver aqui.
                if (!compartiendo || tabs.SelectedTab == tabHorario)
                {
                    // Con las flechas (o la lista), el horario se desliza hacia ese lado.
                    Deslizamiento.Cambiar(flechasLabHorario == null ? 0 : flechasLabHorario.Direccion, CargarFranjas, lienzo);
                }

                if (elegido != null)
                {
                    CompartirLaboratorio(elegido.Id, cboLabHorario);
                }
            };
            flechasLabHorario = new FlechasDeLista(cboLabHorario, "Laboratorio");

            // Botones en la misma fila del selector: la cuadricula se queda con
            // todo el alto de la pestana. Los tres primeros, en el mismo sitio y con
            // las mismas palabras que en la preparacion de Proximo ciclo
            // (TextosDelHorario); "Cambio de un dia..." solo existe en el horario en uso.
            btnAsignar = EstiloHorarios.CrearBoton(TextosDelHorario.PonerClase, new Point(364, 8), new Size(126, 34));
            btnAsignar.Click += (s, e) => AsignarClaseASeleccion();
            ayuda.SetToolTip(btnAsignar, TextosDelHorario.AyudaPonerClase);

            btnFranjaEditar = EstiloHorarios.CrearBoton(TextosDelHorario.CambiarHoras, new Point(498, 8), new Size(140, 34));
            btnFranjaEditar.Click += (s, e) => EditarFranja();
            ayuda.SetToolTip(btnFranjaEditar, TextosDelHorario.AyudaCambiarHoras);

            btnFranjaBorrar = EstiloHorarios.CrearBoton(TextosDelHorario.QuitarHoras, new Point(646, 8), new Size(126, 34));
            btnFranjaBorrar.Click += (s, e) => BorrarFranja();
            ayuda.SetToolTip(btnFranjaBorrar, TextosDelHorario.AyudaQuitarHoras);

            btnExcepcion = EstiloHorarios.CrearBoton("Cambio de un día...", new Point(784, 8), new Size(170, 34));
            btnExcepcion.Click += (s, e) => AplicarExcepcionASeleccion();
            ayuda.SetToolTip(btnExcepcion, "Solo un día de la semana que ves: quitar las horas elegidas (no habrá clase) o dar una sesión extra en ellas.\n"
                + "Las demás semanas no cambian.");

            // Apagados sin nada elegido (ActualizarBotonesFranjas); al pasar el raton dicen que elegir.
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnAsignar, () => conPorAsignar
                ? "Elige primero cuadros en la cuadrícula, libres u ocupados (arrastra o usa Ctrl+clic)."
                : "Elige primero cuadros libres en la cuadrícula (arrastra o usa Ctrl+clic).");
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnFranjaEditar, () => "Elige primero una clase en la cuadrícula (clic en ella).");
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnFranjaBorrar, () => "Elige primero una clase en la cuadrícula (clic en ella).");
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnExcepcion, () => "Elige primero cuadros en la cuadrícula, libres u ocupados (arrastra o usa Ctrl+clic).");

            // Solo consulta: los cuatro escriben.
            Permisos.Ocultar(btnAsignar, btnExcepcion, btnFranjaEditar, btnFranjaBorrar);

            // La semana que se ve, en PDF y sin los codigos de los profesores (se
            // puede ensenar o pegar en la puerta): tambien en solo consulta. En
            // kiosco no: el visor deja guardar con el dialogo de archivos de Windows.
            btnImprimir = EstiloHorarios.CrearBoton("Imprimir PDF", new Point(924, 8), new Size(126, 34));
            btnImprimir.Click += (s, e) => ImprimirPdf();
            btnImprimir.Visible = !Kiosco.Activo;
            ayuda.SetToolTip(btnImprimir, "La semana que ves, en PDF, con el encabezado del TESCHI y sin los códigos para entrar a las clases.\n"
                + "Las clases en gris no salen: esa semana no se dan.\n"
                + "Primero la ves; después eliges si la guardas para imprimirla.");

            // La semana que se ve, con sus fechas en las columnas.
            selectorSemana = new SelectorSemana { Location = new Point(12, 46) };
            selectorSemana.Cambio += () => Deslizamiento.Cambiar(selectorSemana.Direccion, () =>
            {
                semanaElegida = true;
                CargarFranjas();
            }, lienzo);
            selectorSemana.SizeChanged += (s, e) => AjustarAltoCuadricula();
            // Ctrl+flechas del teclado: la semana (las flechas solas, el laboratorio).
            TeclasDeFlechas.Registrar(selectorSemana, true, selectorSemana.Mover);
            ayuda.SetToolTip(selectorSemana, "La semana que se ve: cada columna dice su fecha y salen sus cancelaciones y sesiones extra,\n"
                + "también las que ya pasaron, y las franjas que se dan esa semana (una cambiada desde una fecha, con su horario de entonces).");

            // A la derecha del selector, dos renglones (AjustarAltoCuadricula los
            // acomoda): arriba el estado del laboratorio (acceso y si "Cambiar horas"
            // vale desde una fecha), en color; debajo, como se usa, en gris. Cada uno
            // en un renglon (a 1366 la cuadricula no pierde alto); entero, al pasar el raton.
            lblEstadoHorario = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(420, 44),
                Size = new Size(520, 16)
            };
            lblLeyendaHorario = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(420, 60),
                Size = new Size(520, 16),
                Text = Leyenda
            };
            // CargarFranjas las vuelve a poner al saber el acceso y si los cambios aplican desde una fecha.
            ayuda.SetToolTip(lblLeyendaHorario, AyudaLeyenda());

            leyendaProfesores = new FlowLayoutPanel
            {
                Location = new Point(12, 76),
                Size = new Size(928, 0),
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            Disposed += (s, e) => fuenteChip.Dispose();

            // Los choques que ya tiene el horario en uso esa semana, en todos los
            // laboratorios (decision 3): la misma regla que la revision de Proximo
            // ciclo (la misma persona docente o el mismo grupo en dos laboratorios a
            // la vez, decision del 2026-10-31). Solo se ve si los hay; clic: cuales.
            pastChoques = new PastillaConfiguracion("Choques", 0, 22, fuentePastilla, true) { Visible = false };
            pastChoques.Colores(PiezasConfiguracion.FondoRojo, PiezasConfiguracion.BordeRojo, PiezasConfiguracion.TextoRojo);
            pastChoques.Click += (s, e) => VerChoques();
            ayuda.SetToolTip(pastChoques, "Clases del horario en uso que chocan en la semana que ves: la misma persona docente o el mismo grupo,\n"
                + "el mismo día y a la misma hora, en dos laboratorios distintos. En este laboratorio salen en rojo. Clic: ver cuáles.");

            // Con db/25, lo que le falta a alguna clase (todos los laboratorios), en
            // ambar: un aviso que no impide nada. Solo se ve si falta algo; clic: cuales.
            pastPorAsignar = new PastillaConfiguracion("Falta asignar", 0, 22, fuentePastilla, true) { Visible = false };
            pastPorAsignar.Colores(PiezasConfiguracion.FondoAmbar, PiezasConfiguracion.BordeAmbar, PiezasConfiguracion.TextoAmbar);
            pastPorAsignar.Click += (s, e) => VerPorAsignar();
            ayuda.SetToolTip(pastPorAsignar, "Horas que le faltan a alguna clase en el horario en uso, en todos los laboratorios: las que otra clase desplazó\n"
                + "o las que se quitaron para darlas en otro horario. Bajan solas al ponerle horas a la clase. Clic: ver cuáles.");

            lnkFaltasDeLaSemana = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9F),
                LinkColor = Color.DarkGreen,
                Text = "Ver las faltas de esta semana >"
            };
            lnkFaltasDeLaSemana.LinkClicked += (s, e) =>
            {
                OpcionCombo laboratorio = LaboratorioHorario();
                if (laboratorio != null)
                {
                    IrAFaltas(laboratorio.Id, selectorSemana.Lunes);
                }
            };
            ayuda.SetToolTip(lnkFaltasDeLaSemana, "Lleva a Faltas, en este laboratorio y esta semana: qué clases se quedaron sin bitácoras.");

            contenedorSemana = new Panel
            {
                Location = new Point(12, 76),
                Size = new Size(928, 430),
                AutoScroll = false,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            tabHorario.SizeChanged += (s, e) => AjustarAltoCuadricula();
            Shown += (s, e) => AjustarAltoCuadricula();

            // Solo consulta: no se eligen cuadros (ni sale su menu). Sin los codigos
            // de los profesores, no los llevan los cuadros, su ayuda ni su valor.
            // Las horas se amplian si una clase empieza antes de la apertura o
            // acaba despues del cierre (una de las 06:00 ya no se ve desde las 07:00).
            lienzo = new CuadriculaSemanal
            {
                Dock = DockStyle.Fill,
                SoloLectura = soloConsulta,
                SinCodigos = !Permisos.VeCodigosDeProfesor,
                HorasSegunClases = true,
                // Lo que choca, en rojo con su motivo (como en la preparacion de Proximo ciclo).
                AspectoDeFranja = AspectoDeChoque
            };
            lienzo.FranjaClic += lienzo_FranjaClic;
            if (!soloConsulta)
            {
                // Los dobles clics abren los dialogos de cambiar horas y de poner una clase.
                lienzo.FranjaDobleClic += (f) => EditarFranja();
                lienzo.HuecoDobleClic += lienzo_HuecoDobleClic;
                // Los botones se encienden con lo que se va eligiendo (arrastre, Ctrl+clic, flechas).
                lienzo.SelectionChanged += (s, e) => ActualizarBotonesFranjas();
            }

            lienzo.HuecosSeleccionados += lienzo_HuecosSeleccionados;
            lienzo.ExcepcionClic += lienzo_ExcepcionClic;
            contenedorSemana.Controls.Add(lienzo);

            // Menus de opciones de la cuadricula: los mismos textos que la preparacion
            // de Proximo ciclo (TextosDelHorario), mas lo de un solo dia.
            menuFranja = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuFranja.Items.Add(TextosDelHorario.CambiarHoras, null, (s, e) => EditarFranja());
            menuFranja.Items.Add(TextosDelHorario.QuitarEstasHoras, null, (s, e) => BorrarFranja());
            menuFranja.Items.Add(new ToolStripSeparator());
            menuFranja.Items.Add("Cancelar esta clase ese día...", null, (s, e) => CancelarDiaDeLaSemana());
            menuFranja.Items.Add("Volver a darla ese día...", null, (s, e) => QuitarCancelacionDeFranja());
            menuFranja.Items.Add("Ver esta clase en Clases", null, (s, e) => VerFranjaEnClases());
            menuFranja.Items.Add("Ver ese día en Cambios de un día", null, (s, e) =>
            {
                FranjaInfo franja = FranjaSeleccionada();
                OpcionCombo laboratorio = LaboratorioHorario();
                if (franja != null && laboratorio != null)
                {
                    MostrarExcepcionesDe(laboratorio, lienzo.FechaDeColumna(franja.DiaSemana));
                }
            });
            menuFranja.Items.Add(new ToolStripSeparator());
            menuFranja.Items.Add(TextosDelHorario.PonerEligiendo, null, (s, e) => NuevaFranja(null, null));
            // Solo consulta: quedan "Ver esta clase en Clases" y "Ver ese dia en
            // Cambios de un dia" (se ocultan, no se quitan: MostrarMenuFranja usa la posicion).
            Permisos.Ocultar(menuFranja.Items[0], menuFranja.Items[1], menuFranja.Items[2], menuFranja.Items[3], menuFranja.Items[4],
                menuFranja.Items[7], menuFranja.Items[8]);

            menuExtra = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuExtra.Items.Add("Quitar esta sesión extra...", null, (s, e) => QuitarExtraDeCuadricula());
            menuExtra.Items.Add("Ver ese día en Cambios de un día", null, (s, e) => { if (extraEnMenu != null) MostrarExcepcionesDe(LaboratorioHorario(), extraEnMenu.Fecha); });
            // Solo consulta: queda "Ver ese dia en Cambios de un dia" (se oculta, no se
            // quita: lienzo_ExcepcionClic le pone el texto por posicion).
            Permisos.Ocultar(menuExtra.Items[0]);

            menuHueco = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuHueco.Items.Add(TextosDelHorario.SinCuadrosLibres, null, (s, e) => AsignarClaseASeleccion());
            menuHueco.Items.Add("Cambio de un día...", null, (s, e) => AplicarExcepcionASeleccion());
            menuHueco.Items.Add(new ToolStripSeparator());
            menuHueco.Items.Add(new ToolStripMenuItem(TextosDelHorario.QuitarSeleccion, null, (s, e) =>
            {
                lienzo.LimpiarSeleccion();
                ActualizarBotonesFranjas();
            }) { ShortcutKeyDisplayString = "Esc" });

            tabHorario.Controls.Add(lblLab);
            tabHorario.Controls.Add(cboLabHorario);
            tabHorario.Controls.Add(btnAsignar);
            tabHorario.Controls.Add(btnFranjaEditar);
            tabHorario.Controls.Add(btnFranjaBorrar);
            tabHorario.Controls.Add(btnExcepcion);
            tabHorario.Controls.Add(btnImprimir);
            tabHorario.Controls.Add(selectorSemana);
            tabHorario.Controls.Add(lblEstadoHorario);
            tabHorario.Controls.Add(lblLeyendaHorario);
            tabHorario.Controls.Add(leyendaProfesores);
            tabHorario.Controls.Add(pastChoques);
            tabHorario.Controls.Add(pastPorAsignar);
            tabHorario.Controls.Add(lnkFaltasDeLaSemana);
            tabHorario.Controls.Add(contenedorSemana);
        }

        /// <summary>La cuadricula ocupa todo el ancho y todo el alto que quede en la pestana.</summary>
        private void AjustarAltoCuadricula()
        {
            if (contenedorSemana == null || tabHorario == null)
            {
                return;
            }

            int ancho = tabHorario.ClientSize.Width - 24;
            if (ancho > 300)
            {
                contenedorSemana.Width = ancho;
            }

            // Fila de arriba: el laboratorio a la izquierda, la semana en medio (o
            // tras el laboratorio, si no cabe) e "Imprimir PDF" a la derecha; si no
            // cabe (una ventana estrecha), baja a la derecha de la segunda fila.
            // Segunda fila: los botones de la cuadricula y, a su derecha, la leyenda.
            const int filaUno = 8;
            const int filaDos = 48;
            if (selectorSemana != null && flechasLabHorario != null)
            {
                int centrado = contenedorSemana.Left + (contenedorSemana.Width - selectorSemana.Width) / 2;
                selectorSemana.Location = new Point(Math.Max(flechasLabHorario.Derecha + 24, centrado), filaUno + (34 - selectorSemana.Height) / 2);
            }

            int derechaBotones = contenedorSemana.Left;
            if (!soloConsulta && btnAsignar != null)
            {
                int x = contenedorSemana.Left;
                foreach (Button boton in new[] { btnAsignar, btnFranjaEditar, btnFranjaBorrar, btnExcepcion })
                {
                    boton.Location = new Point(x, filaDos);
                    x = boton.Right + 8;
                }

                derechaBotones = btnExcepcion.Right;
            }

            bool imprimirAbajo = false;
            if (btnImprimir != null && selectorSemana != null)
            {
                imprimirAbajo = selectorSemana.Right + 14 + btnImprimir.Width > contenedorSemana.Right;
                btnImprimir.Location = new Point(contenedorSemana.Right - btnImprimir.Width, imprimirAbajo ? filaDos : filaUno);
            }

            // La leyenda, a la derecha de los botones hasta el borde: el estado
            // arriba y como se usa debajo, un renglon cada uno.
            int debajoDelSelector = filaDos + 38;
            if (selectorSemana != null && lblLeyendaHorario != null && lblEstadoHorario != null)
            {
                int izquierda = derechaBotones + 14;
                int derecha = imprimirAbajo && btnImprimir.Visible ? btnImprimir.Left - 10 : contenedorSemana.Right;
                int anchoLeyenda = Math.Max(60, derecha - izquierda);
                int altoLinea = lblLeyendaHorario.Font.Height + 1;
                int arriba = filaDos + (34 - 2 * altoLinea) / 2;
                lblEstadoHorario.SetBounds(izquierda, arriba, anchoLeyenda, altoLinea);
                lblLeyendaHorario.SetBounds(izquierda, arriba + altoLinea, anchoLeyenda, altoLinea);
                debajoDelSelector = Math.Max(debajoDelSelector, lblLeyendaHorario.Bottom + 2);
            }

            // La fila de los docentes ocupa las lineas que necesite (con muchos, dos)
            // y, a su derecha, la pastilla de los choques y "Ver las faltas de esta
            // semana >". La cuadricula empieza justo debajo.
            if (leyendaProfesores != null && lnkFaltasDeLaSemana != null && pastChoques != null && pastPorAsignar != null)
            {
                int derecha = contenedorSemana.Right;
                int anchoDerecha = lnkFaltasDeLaSemana.PreferredWidth + (pastChoques.Visible ? pastChoques.Width + 12 : 0)
                    + (pastPorAsignar.Visible ? pastPorAsignar.Width + 8 : 0) + 12;
                bool hayLeyenda = leyendaProfesores.Controls.Count > 0;
                int anchoChips = Math.Max(100, contenedorSemana.Width - anchoDerecha);
                leyendaProfesores.Top = debajoDelSelector;
                leyendaProfesores.Width = anchoChips;
                int altoChips = hayLeyenda ? leyendaProfesores.GetPreferredSize(new Size(anchoChips, 0)).Height : 0;
                // Tan alta como un renglon de docentes aunque no haya ninguno: el enlace y la pastilla (22 px) caben.
                int altoFila = Math.Max(altoChips, 25);
                leyendaProfesores.Height = altoChips;
                // Centrados con el primer renglon de docentes (un chip mide unos 22 px).
                int centro = debajoDelSelector + Math.Min(altoFila, 26) / 2 - 2;
                lnkFaltasDeLaSemana.Location = new Point(derecha - lnkFaltasDeLaSemana.PreferredWidth, centro - lnkFaltasDeLaSemana.PreferredHeight / 2);
                pastChoques.Location = new Point(lnkFaltasDeLaSemana.Left - 12 - pastChoques.Width, centro - pastChoques.Height / 2);
                int izquierdaDeChoques = pastChoques.Visible ? pastChoques.Left - 8 : lnkFaltasDeLaSemana.Left - 12;
                pastPorAsignar.Location = new Point(izquierdaDeChoques - pastPorAsignar.Width, centro - pastPorAsignar.Height / 2);
                contenedorSemana.Top = leyendaProfesores.Top + altoFila + 2;
            }

            // La cuadricula usa todo el alto que queda: con la ventana maximizada
            // cada hora gana alto y cabe mas texto en cada clase.
            int disponible = tabHorario.ClientSize.Height - contenedorSemana.Top - 10;
            int alto = Math.Min(disponible, CuadriculaSemanal.AltoPara(disponible));
            if (alto > 120)
            {
                contenedorSemana.Height = alto;
            }
        }

        /// <summary>
        /// Una etiqueta por profesor del laboratorio, con su color: asi el color
        /// dice de quien es cada clase sin adivinarlo. Un clic en una desvae las
        /// franjas de los demas; otro clic las vuelve a ensenar todas.
        /// </summary>
        private void PonerLeyenda(List<FranjaInfo> franjas)
        {
            // Los que dan clase en la semana que se ve: las franjas que se dan en la
            // fecha de su columna. Una cambiada a media semana se cuenta una vez (la
            // vieja o la nueva, la que se da ese dia), y las que ya terminaron o aun
            // no empiezan, ninguna.
            var profesores = franjas
                .Where(lienzo.VigenteEnSuColumna)
                .GroupBy(f => f.ProfesorId)
                .Select(g => new
                {
                    Id = g.Key,
                    Corto = string.IsNullOrEmpty(g.First().ProfesorCorto) ? g.First().Profesor : g.First().ProfesorCorto,
                    Nombre = g.First().Profesor,
                    Horas = g.Sum(f => (f.Fin - f.Inicio).TotalHours)
                })
                .OrderBy(p => p.Corto, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            leyendaProfesores.SuspendLayout();
            foreach (Control viejo in leyendaProfesores.Controls.Cast<Control>().ToList())
            {
                ayuda.SetToolTip(viejo, null);
                viejo.Dispose();
            }

            if (!profesores.Any(p => p.Id == profesorFijado))
            {
                profesorFijado = 0;
            }

            foreach (var p in profesores)
            {
                int id = p.Id;
                Label chip = new Label
                {
                    AutoSize = true,
                    BackColor = ColoresProfesor.De(id),
                    Cursor = Cursors.Hand,
                    Font = fuenteChip,
                    ForeColor = Color.Black,
                    Margin = new Padding(0, 0, 6, 4),
                    Padding = new Padding(8, 3, 8, 3),
                    Tag = id,
                    Text = p.Corto
                };
                string horas = p.Horas == Math.Floor(p.Horas) ? ((int)p.Horas).ToString() : p.Horas.ToString("0.#");
                ayuda.SetToolTip(chip, p.Nombre + Environment.NewLine
                    + horas + (p.Horas == 1 ? " hora" : " horas") + " a la semana en este laboratorio." + Environment.NewLine
                    + "Clic: dejar a la vista solo sus clases (otro clic, todas).");
                // Solo con clic: al pasar el raton no cambia nada (el administrador
                // lo pidio asi; resaltar al pasar distraia al recorrer la leyenda).
                chip.Click += (s, e) =>
                {
                    profesorFijado = profesorFijado == id ? 0 : id;
                    MarcarProfesorFijado();
                    lienzo.ProfesorResaltado = profesorFijado;
                };
                leyendaProfesores.Controls.Add(chip);
            }

            leyendaProfesores.ResumeLayout();
            MarcarProfesorFijado();
            lienzo.ProfesorResaltado = profesorFijado;
            AjustarAltoCuadricula();
        }

        private void MarcarProfesorFijado()
        {
            foreach (Control chip in leyendaProfesores.Controls)
            {
                ((Label)chip).BorderStyle = profesorFijado != 0 && (int)chip.Tag == profesorFijado ? BorderStyle.FixedSingle : BorderStyle.None;
            }
        }

        /// <summary>Los colores de los profesores (ColoresProfesor), con las clases y los colores elegidos de la base.</summary>
        private void RepartirColores()
        {
            try
            {
                RepartoDeColores reparto = datos.DatosDeColores();
                ColoresProfesor.Repartir(reparto.Orden, reparto.Elegidos);
            }
            catch (Exception ex)
            {
                Log.Error("AdministrarHorarios: repartir los colores de los profesores", ex);
            }
        }

        private void ConstruirPestanaExcepciones()
        {
            // Lo que se muestra: un dia (todas sus clases, para cancelar una o dar
            // una sesion extra) o una semana / un mes (solo los cambios, para revisarlos).
            Label lblPeriodo = EstiloHorarios.CrearEtiqueta("Mostrar", new Point(12, 16));

            cboPeriodo = EstiloHorarios.CrearCombo(new Point(90, 12), 236);
            cboPeriodo.Items.Add("Un día: todas sus clases");
            cboPeriodo.Items.Add("Una semana: solo los cambios");
            cboPeriodo.Items.Add("Un mes: solo los cambios");
            cboPeriodo.SelectedIndex = 0;
            cboPeriodo.SelectedIndexChanged += (s, e) =>
            {
                calendario.Modo = (ModoCalendario)cboPeriodo.SelectedIndex;
                CargarAgendaSiSeVe();
            };

            // Calendario propio a la izquierda: siempre visible, con los dias que
            // no se pueden elegir sombreados segun el modo (en Semana solo los
            // lunes; en Mes ninguno, solo se cambia de mes con las flechas).
            calendario = new CalendarioFiltro
            {
                Location = new Point(12, 50),
                Size = new Size(240, 228)
            };
            calendario.SeleccionCambiada += (f) => CargarAgendaSiSeVe();
            // Ctrl+flechas del teclado: el dia, la semana o el mes, segun Mostrar (las
            // flechas solas, el laboratorio), como la semana de Horario semanal y el
            // mes de Faltas; la agenda se desliza hacia ese lado.
            TeclasDeFlechas.Registrar(calendario, true, paso => Deslizamiento.Cambiar(paso, () => calendario.Mover(paso), dgvAgenda));
            ayuda.SetToolTip(calendario, "Pulsa un día (en «Una semana», un lunes). Ctrl+← y Ctrl+→: el día, la semana o el mes anterior o siguiente.");

            Label lblLab = EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(344, 16));

            cboLabExcepciones = EstiloHorarios.CrearCombo(new Point(446, 12), 240);
            cboLabExcepciones.SelectedIndexChanged += (s, e) =>
            {
                // Puesto desde otra pestana (CompartirLaboratorio): se lee al volver aqui.
                Deslizamiento.Cambiar(flechasLabExcepciones == null ? 0 : flechasLabExcepciones.Direccion, CargarAgendaSiSeVe, dgvAgenda);
                OpcionCombo elegido = LaboratorioExcepciones();
                if (elegido != null)
                {
                    CompartirLaboratorio(elegido.Id, cboLabExcepciones);
                }
            };
            flechasLabExcepciones = new FlechasDeLista(cboLabExcepciones, "Laboratorio");

            lblDiaAgenda = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(264, 52),
                Size = new Size(676, 20),
                Text = ""
            };

            // A la semana de lo que se ve, en Horario semanal (mismo laboratorio).
            lnkSemanaDeCambios = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9F),
                LinkColor = Color.DarkGreen,
                Location = new Point(700, 54),
                Text = "Ver la semana en Horario semanal >"
            };
            lnkSemanaDeCambios.LinkClicked += (s, e) => IrASemanaDeHorarios(calendario.Seleccion);
            ayuda.SetToolTip(lnkSemanaDeCambios, "Lleva a Horario semanal, en este laboratorio y en la semana del día elegido.");

            btnAgendaActualizar = EstiloHorarios.CrearBoton("Actualizar", new Point(820, 8), new Size(120, 34));
            btnAgendaActualizar.Click += (s, e) => CargarAgenda();

            dgvAgenda = EstiloHorarios.CrearGrid(new Point(264, 78), new Size(676, 374));
            dgvAgenda.Columns.Add("Fecha", "Fecha");
            dgvAgenda.Columns.Add("Inicio", "Inicio");
            dgvAgenda.Columns.Add("Fin", "Fin");
            dgvAgenda.Columns.Add("Codigo", "Código");
            dgvAgenda.Columns.Add("Materia", "Materia");
            dgvAgenda.Columns.Add("Grupo", "Grupo");
            dgvAgenda.Columns.Add("Profesor", "Docente");
            dgvAgenda.Columns.Add("Estado", "Estado");
            // Las cortas, casi a su medida (ListaDigBit no las deja cortarse); el resto para la
            // materia, el docente y el estado. Casi todas las filas dicen "Se da": el estado
            // cede a los otros dos (2026-11-03: con 25, 19 y 24 se cortaban "Programacion
            // Orientada a Objetos" y "Gabriela Generacion Prueba"); con 10 pt a 1366 caben
            // enteros y, de un cambio, "Cancelada" con un motivo muy corto; uno mas largo,
            // entero al pasar el raton.
            EstiloHorarios.Pesos(dgvAgenda, 8, 5, 5, 7, 29, 7, 25, 14);
            // El codigo de cada clase, solo el administrador (CargarAgenda tampoco lo calcula).
            dgvAgenda.Columns["Codigo"].Visible = Permisos.VeCodigosDeProfesor;
            dgvAgenda.SelectionChanged += (s, e) => ActualizarBotonesAgenda();
            dgvAgenda.CurrentCellChanged += (s, e) => ActualizarBotonesAgenda();

            // Cada uno dice que hace con la fila elegida de la agenda.
            btnCancelarDia = EstiloHorarios.CrearBoton("Cancelar esta clase ese día...", new Point(12, 462), new Size(250, 38));
            btnCancelarDia.Click += (s, e) => CancelarFranjaDelDia();
            ayuda.SetToolTip(btnCancelarDia, "Cancela la clase elegida solo ese día. Las demás semanas no cambian.");

            btnQuitarCancelacion = EstiloHorarios.CrearBoton("Volver a darla ese día...", new Point(274, 462), new Size(214, 38));
            btnQuitarCancelacion.Click += (s, e) => QuitarCancelacion();
            ayuda.SetToolTip(btnQuitarCancelacion, "Quita la cancelación de la clase elegida: vuelve a darse entera ese día.");

            btnSesionExtra = EstiloHorarios.CrearBoton("Dar una sesión extra...", new Point(540, 462), new Size(200, 38));
            btnSesionExtra.Click += (s, e) => NuevaSesionExtra();
            ayuda.SetToolTip(btnSesionExtra, "Una clase más ese día en este laboratorio (propone empezar al acabar la clase elegida).");

            btnQuitarExtra = EstiloHorarios.CrearBoton("Quitar la sesión extra...", new Point(748, 462), new Size(200, 38));
            btnQuitarExtra.Click += (s, e) => QuitarExtra();
            ayuda.SetToolTip(btnQuitarExtra, "Quita la sesión extra elegida (antes te pregunta).");

            // Apagados, dicen que elegir al pasar el raton.
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnCancelarDia, () => "Elige en la lista una clase que se da ese día (no cancelada ni sesión extra).");
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnQuitarCancelacion, () => "Elige en la lista una clase cancelada.");
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnQuitarExtra, () => "Elige en la lista una sesión extra.");

            // Con "Una semana" o "Un mes" en lugar de los botones (AcomodarExcepciones lo pone en su fila).
            lblSoloEnUnDia = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.Gray,
                Text = "Para cancelar una clase o dar una sesión extra, elige en Mostrar «Un día: todas sus clases».",
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };

            // Solo consulta: los cuatro escriben (AcomodarExcepciones alarga la agenda).
            Permisos.Ocultar(btnCancelarDia, btnQuitarCancelacion, btnSesionExtra, btnQuitarExtra);

            tabExcepciones.Controls.Add(lblPeriodo);
            tabExcepciones.Controls.Add(cboPeriodo);
            tabExcepciones.Controls.Add(calendario);
            tabExcepciones.Controls.Add(lblLab);
            tabExcepciones.Controls.Add(cboLabExcepciones);
            tabExcepciones.Controls.Add(lblDiaAgenda);
            tabExcepciones.Controls.Add(lnkSemanaDeCambios);
            tabExcepciones.Controls.Add(btnAgendaActualizar);
            tabExcepciones.Controls.Add(dgvAgenda);
            tabExcepciones.Controls.Add(btnCancelarDia);
            tabExcepciones.Controls.Add(btnQuitarCancelacion);
            tabExcepciones.Controls.Add(btnSesionExtra);
            tabExcepciones.Controls.Add(btnQuitarExtra);
            tabExcepciones.Controls.Add(lblSoloEnUnDia);
            tabExcepciones.Resize += (s, e) => AcomodarExcepciones();
        }

        // =====================================================================
        // Comun
        // =====================================================================

        private void tabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            PonerFrase();
            // Cada pestana se refresca al entrar: lo que se cambia en una afecta a las otras.
            if (tabs.SelectedTab == tabClases)
            {
                EstadoClases("", false);
                CargarClases();
            }
            else if (tabs.SelectedTab == tabHorario)
            {
                CargarFranjas();
            }
            else if (tabs.SelectedTab == tabExcepciones)
            {
                CargarAgenda();
            }
            else if (tabs.SelectedTab == tabCalendario)
            {
                // La primera vez, el laboratorio que se miraba en Horario semanal.
                OpcionCombo laboratorio = LaboratorioHorario();
                calendarioBitacoras.Cargar(laboratorio == null ? 0 : laboratorio.Id);
            }
            else if (proximoCiclo != null && tabs.SelectedTab == tabProximo)
            {
                proximoCiclo.Cargar();
            }
        }

        /// <summary>
        /// La pestana Proximo ciclo (o Horarios guardados): la administracion y, en
        /// solo consulta, la persona responsable del laboratorio (especificacion v2,
        /// 4.0 y R7). Horarios se abre desde su panel; si otra cuenta llegara a
        /// abrirla, no la ve.
        /// </summary>
        private static bool VeConfiguraciones
        {
            get { return Datos_User.EsAdministrador || Permisos.EsEncargado; }
        }

        /// <summary>
        /// Lleva a Horario semanal en la semana de ese lunes (vale cualquier dia de
        /// ella), en el laboratorio que se miraba: "Ver en Horario semanal" de Proximo
        /// ciclo, "Ver la semana en Horario semanal" de Cambios de un dia y "Ver esta
        /// semana en Horario semanal" de Faltas.
        /// </summary>
        private void IrASemanaDeHorarios(DateTime lunes)
        {
            semanaElegida = true;
            // El setter no dispara Cambio: la lee CargarFranjas, al elegir la pestana o aqui.
            selectorSemana.Lunes = lunes;
            if (tabs.SelectedTab == tabHorario)
            {
                CargarFranjas();
            }
            else
            {
                tabs.SelectedTab = tabHorario;
            }
        }

        private void CargarLaboratorios()
        {
            // Sin los desactivados: no se les asigna horario nuevo.
            List<OpcionCombo> laboratorios;
            try
            {
                laboratorios = datos.ObtenerLaboratoriosActivos();
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al log (AvisoDeFalloAdmin).
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "Horarios: leer laboratorios", ex, "No se pudieron leer los laboratorios", Text);
                laboratorios = new List<OpcionCombo>();
            }

            cboLabHorario.Items.Clear();
            cboLabExcepciones.Items.Clear();
            foreach (OpcionCombo laboratorio in laboratorios)
            {
                cboLabHorario.Items.Add(laboratorio);
                cboLabExcepciones.Items.Add(new OpcionCombo { Id = laboratorio.Id, Texto = laboratorio.Texto });
            }

            // El que se pidio (desde el panel), el ultimo que se miro, el de este
            // equipo si tiene laboratorio asignado, o el primero.
            int preferido = laboratorioInicial != 0 ? laboratorioInicial
                : ultimoLaboratorio != 0 ? ultimoLaboratorio
                : LaboratorioEquipo.Resuelto ? LaboratorioEquipo.Id : 0;
            SeleccionarPorId(cboLabHorario, preferido);
            SeleccionarPorId(cboLabExcepciones, preferido);
            // Items.Clear no avisa a las flechas (sin laboratorios se quedaria el nombre de antes).
            foreach (FlechasDeLista flechas in new[] { flechasLabHorario, flechasLabExcepciones })
            {
                if (flechas != null)
                {
                    flechas.Refrescar();
                }
            }
        }

        private static void SeleccionarPorId(ComboBox combo, int id)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                OpcionCombo opcion = combo.Items[i] as OpcionCombo;
                if (opcion != null && opcion.Id == id)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            if (combo.Items.Count > 0 && combo.SelectedIndex < 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        /// <summary>Id del administrador conectado para 'creada_por'; 0 si no se puede resolver.</summary>
        private int IdAdministrador()
        {
            if (idAdministrador.HasValue)
            {
                return idAdministrador.Value;
            }

            int id = 0;
            string usuario = Datos_User.getUser();
            if (!string.IsNullOrWhiteSpace(usuario))
            {
                try
                {
                    id = consultas.ObtenerIdPorMatricula(usuario);
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios: no se pudo resolver el id del administrador '" + usuario + "'.", ex);
                    id = 0;
                }
            }

            idAdministrador = id;
            return id;
        }

        /// <summary>
        /// "Nueva clase..." desde Asignar clase o Aplicar excepcion: si falta la
        /// clase no hay que cerrar el dialogo, ir a la pestana Clases y volver.
        /// Devuelve las clases activas de nuevo, o null si se cancelo.
        /// </summary>
        private List<ClaseInfo> CrearClaseDesdeDialogo(IWin32Window dueno)
        {
            if (Permisos.Bloquear(dueno, "Crear una clase (desde Asignar clase o Aplicar excepcion)")) return null;
            using (DialogoClase dialogo = new DialogoClase(datos, consultas, null, IdAdministrador()))
            {
                if (dialogo.ShowDialog(dueno) != DialogResult.OK)
                {
                    return null;
                }
            }

            CargarClases();
            return ClasesActivas();
        }

        private List<ClaseInfo> ClasesActivas()
        {
            try
            {
                return datos.ObtenerClases(true);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.ClasesActivas", "No se pudieron leer las clases activas.", ex);
                return new List<ClaseInfo>();
            }
        }

        /// <summary>
        /// Lo que no se pudo hacer, por que y que hacer (AvisoDeFalloAdmin), sin el
        /// texto tecnico de la excepcion: ese va al log, con el contexto (en ASCII).
        /// </summary>
        private void MostrarError(string contexto, string mensaje, Exception ex)
        {
            AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), contexto, ex, mensaje.TrimEnd('.'), Text);
        }

        // =====================================================================
        // Pestana Clases
        // =====================================================================

        private void CargarClases()
        {
            RepartirColores();
            try
            {
                clasesLeidas = datos.ObtenerClases(false);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.CargarClases", "No se pudieron leer las clases.", ex);
                clasesLeidas = new List<ClaseInfo>();
            }

            try
            {
                hoyClases = consultas.AhoraServidor().Date;
            }
            catch (Exception ex)
            {
                Log.Error("AdministrarHorarios.CargarClases (hora del servidor)", ex);
                hoyClases = DateTime.Today;
            }

            LlenarProfesoresClases();
            MostrarClases();
        }

        /// <summary>
        /// "Todo el personal docente" y uno por profesor con clases, con su color y
        /// cuantas tiene. Se queda en el que estaba elegido si sigue en la lista.
        /// </summary>
        private void LlenarProfesoresClases()
        {
            OpcionCombo antes = cboProfesorClases.SelectedItem as OpcionCombo;
            List<OpcionCombo> profesores = clasesLeidas
                .GroupBy(c => c.ProfesorId)
                .Select(g => new OpcionCombo
                {
                    Id = g.Key,
                    Texto = g.First().Profesor + "  (" + g.Count() + (g.Count() == 1 ? " clase)" : " clases)")
                })
                .OrderBy(p => p.Texto, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            // Sin disparar MostrarClases: quien llama la pinta una sola vez.
            cboProfesorClases.SelectedIndexChanged -= ProfesorClasesCambiado;
            cboProfesorClases.BeginUpdate();
            cboProfesorClases.Items.Clear();
            cboProfesorClases.Items.Add(new OpcionCombo { Id = 0, Texto = "Todo el personal docente" });
            foreach (OpcionCombo profesor in profesores)
            {
                cboProfesorClases.Items.Add(profesor);
            }

            // La lista desplegada, tan ancha como el nombre mas largo (con su cuadrito).
            int anchoLista = profesores.Count == 0 ? 0
                : profesores.Max(p => TextRenderer.MeasureText(p.Texto, cboProfesorClases.Font).Width) + 36 + SystemInformation.VerticalScrollBarWidth;
            cboProfesorClases.DropDownWidth = Math.Max(cboProfesorClases.Width, anchoLista);
            cboProfesorClases.EndUpdate();
            OpcionCombo mismo = antes == null ? null : profesores.FirstOrDefault(p => p.Id == antes.Id);
            cboProfesorClases.SelectedItem = (object)mismo ?? cboProfesorClases.Items[0];
            cboProfesorClases.SelectedIndexChanged += ProfesorClasesCambiado;
        }

        private void ProfesorClasesCambiado(object sender, EventArgs e)
        {
            MostrarClases();
        }

        /// <summary>El cuadrito con el color del profesor (el de sus franjas) y su nombre.</summary>
        private void DibujarProfesorConColor(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0)
            {
                return;
            }

            OpcionCombo opcion = (OpcionCombo)cboProfesorClases.Items[e.Index];
            int x = e.Bounds.X + 4;
            if (opcion.Id != 0)
            {
                Rectangle cuadro = new Rectangle(x, e.Bounds.Y + 3, 14, e.Bounds.Height - 6);
                using (SolidBrush relleno = new SolidBrush(CuadriculaSemanal.ColorDeProfesor(opcion.Id)))
                {
                    e.Graphics.FillRectangle(relleno, cuadro);
                }

                e.Graphics.DrawRectangle(Pens.Gray, cuadro);
            }

            x += 20;
            TextRenderer.DrawText(e.Graphics, opcion.Texto, e.Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x, e.Bounds.Height),
                e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        /// <summary>Pinta las clases leidas que pasan el buscador y la lista de profesores.</summary>
        private void MostrarClases()
        {
            OpcionCombo profesor = cboProfesorClases.SelectedItem as OpcionCombo;
            int idProfesor = profesor == null ? 0 : profesor.Id;
            string busqueda = txtBuscarClase.Text;
            // Sin los codigos de los profesores (solo consulta), ni se buscan ni se
            // dejan en la columna oculta (la leerian Ctrl+C y los lectores de pantalla).
            bool conCodigo = Permisos.VeCodigosDeProfesor;
            List<ClaseInfo> clases = clasesLeidas
                .Where(c => (idProfesor == 0 || c.ProfesorId == idProfesor)
                    && EstiloAdmin.Coincide(EstiloAdmin.Normalizar(string.Join(" ", conCodigo ? c.Codigo : "", c.Materia, c.Grupo, c.Profesor, c.NumeroEmpleado)), busqueda))
                .ToList();

            ClaseInfo anterior = ClaseSeleccionada();
            dgvClases.Rows.Clear();
            foreach (ClaseInfo clase in clases)
            {
                int fila = dgvClases.Rows.Add(
                    conCodigo ? EstiloAdmin.Codigo(clase.Codigo) : "",
                    clase.Materia,
                    clase.Grupo,
                    clase.Profesor,
                    clase.VigenteDesde.ToString("dd/MM/yyyy"),
                    clase.VigenteHasta.ToString("dd/MM/yyyy"),
                    EstadoDeClase(clase),
                    clase.SesionesTotales);
                dgvClases.Rows[fila].Tag = clase;
                PintarCeldaProfesor(dgvClases.Rows[fila].Cells[3], clase.ProfesorId);

                // Desactivada o ya terminada: en gris (su pasado sigue en Faltas).
                if (!clase.Activa || clase.VigenteHasta.Date < hoyClases)
                {
                    dgvClases.Rows[fila].DefaultCellStyle.ForeColor = Color.Gray;
                }
            }

            // Corto a proposito: a su derecha empiezan los botones.
            lblClasesCantidad.Text = clases.Count == clasesLeidas.Count
                ? clases.Count + (clases.Count == 1 ? " clase" : " clases") + " (color: su docente)"
                : clases.Count == 0
                    ? "Ninguna de las " + clasesLeidas.Count + " clases coincide"
                    : clases.Count + " de " + clasesLeidas.Count + " clases";
            if (anterior != null)
            {
                EstiloHorarios.SeleccionarFila(dgvClases, r => ((ClaseInfo)r.Tag).Id == anterior.Id);
            }

            ActualizarBotonesClases();
        }

        private ClaseInfo ClaseSeleccionada()
        {
            return dgvClases.CurrentRow == null ? null : dgvClases.CurrentRow.Tag as ClaseInfo;
        }

        /// <summary>"Activa", "Desactivada", "Terminada", "Termina hoy" o "Por empezar", con el reloj del servidor de la ultima lectura.</summary>
        private string EstadoDeClase(ClaseInfo clase)
        {
            if (!clase.Activa)
            {
                return "Desactivada";
            }

            if (clase.VigenteHasta.Date < hoyClases)
            {
                return "Terminada";
            }

            if (clase.VigenteHasta.Date == hoyClases)
            {
                return "Termina hoy";
            }

            return clase.VigenteDesde.Date > hoyClases ? "Por empezar" : "Activa";
        }

        private void ActualizarBotonesClases()
        {
            ClaseInfo clase = ClaseSeleccionada();
            btnClaseEditar.Enabled = clase != null;
            btnClaseVerHorario.Enabled = clase != null;
            btnClaseEstado.Enabled = clase != null;
            // Con sesiones no se desactiva (desapareceria tambien su pasado): se termina.
            bool terminar = clase != null && clase.Activa && clase.SesionesTotales > 0;
            btnClaseEstado.Text = clase == null ? "Desactivar" : terminar ? "Terminar clase..." : (clase.Activa ? "Desactivar" : "Activar");
            ayuda.SetToolTip(btnClaseEstado, terminar
                ? "Ya tiene sesiones: se termina en una fecha (su último día) en vez de desactivarse.\nLo que ya pasó se queda como fue: Faltas, la asistencia y las bitácoras."
                : null);
        }

        private void NuevaClase()
        {
            if (Permisos.Bloquear(this, "Crear una clase")) return;
            HashSet<int> antes = new HashSet<int>(clasesLeidas.Select(c => c.Id));
            using (DialogoClase dialogo = new DialogoClase(datos, consultas, null, IdAdministrador()))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            CargarClases();
            // La nueva, elegida (sin filtros que la escondan) y con lo que le falta: sus horas.
            ClaseInfo nueva = clasesLeidas.FirstOrDefault(c => !antes.Contains(c.Id));
            if (nueva != null)
            {
                MostrarEnClases(nueva.Id);
                EstadoClases("Listo: se creó " + nueva.Materia + " con " + nueva.Grupo + ". Todavía no tiene horas: pulsa «Ver en el horario» "
                    + "y ponlas con «Poner clase...».", false);
            }
        }

        /// <summary>Elige esa clase en la lista de Clases, quitando el buscador y el docente si la esconden.</summary>
        private void MostrarEnClases(int idClase)
        {
            if (!dgvClases.Rows.Cast<DataGridViewRow>().Any(r => ((ClaseInfo)r.Tag).Id == idClase))
            {
                esperaBusquedaClases.Stop();
                txtBuscarClase.Text = "";
                esperaBusquedaClases.Stop();
                // Sin disparar MostrarClases dos veces.
                cboProfesorClases.SelectedIndexChanged -= ProfesorClasesCambiado;
                if (cboProfesorClases.Items.Count > 0)
                {
                    cboProfesorClases.SelectedIndex = 0;
                }

                cboProfesorClases.SelectedIndexChanged += ProfesorClasesCambiado;
                MostrarClases();
            }

            EstiloHorarios.SeleccionarFila(dgvClases, r => ((ClaseInfo)r.Tag).Id == idClase);
            ActualizarBotonesClases();
        }

        /// <summary>
        /// "Ver esta clase en Clases" (menu de una clase de Horario semanal): la
        /// pestana Clases con su fila elegida.
        /// </summary>
        private void VerFranjaEnClases()
        {
            FranjaInfo franja = FranjaSeleccionada();
            if (franja == null)
            {
                return;
            }

            // tabs_SelectedIndexChanged: CargarClases.
            tabs.SelectedTab = tabClases;
            EstadoClases("", false);
            MostrarEnClases(franja.ClaseId);
            dgvClases.Focus();
        }

        /// <summary>
        /// "Ver en el horario": Horario semanal en el laboratorio donde la clase
        /// elegida tiene mas horas, en la semana de hoy (o en la de su inicio si
        /// aun no empieza, o en la ultima si ya termino), con su docente resaltado.
        /// Sin horas, lo dice y como ponerlas.
        /// </summary>
        private void VerClaseEnHorario()
        {
            ClaseInfo clase = ClaseSeleccionada();
            if (clase == null)
            {
                return;
            }

            List<FranjaInfo> franjas;
            try
            {
                franjas = datos.ObtenerFranjasDeClase(clase.Id);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.VerClaseEnHorario", "No se pudo leer el horario de la clase.", ex);
                return;
            }

            DateTime hoy = hoyClases;
            DateTime semana = clase.VigenteDesde.Date > hoy ? clase.VigenteDesde.Date
                : clase.VigenteHasta.Date < hoy ? clase.VigenteHasta.Date
                : SelectorSemana.SemanaActual(hoy);
            if (franjas.Count == 0)
            {
                // Sin horas (la recien creada): a Horario semanal, en el laboratorio que
                // se miraba y la semana en que empieza (o esta), con ella elegida para
                // el siguiente "Poner clase..." (2026-11-03: antes se quedaba en Clases
                // aunque "Nueva clase..." decia que este boton llevaba al horario).
                if (soloConsulta || LaboratorioHorario() == null)
                {
                    EstadoClases(clase.Materia + " con " + clase.Grupo + " todavía no tiene horas"
                        + (soloConsulta ? "." : ": en Horario semanal elige cuadros libres y pulsa «Poner clase...»."), true);
                    return;
                }

                EstadoClases("", false);
                porPoner = null;
                sinHorasPorPoner = clase;
                profesorFijado = clase.ProfesorId;
                lienzo.LimpiarSeleccion();
                IrASemanaDeHorarios(semana);
                ActiveControl = lienzo;
                return;
            }
            // Las que se dan esa semana (si ninguna, todas): el laboratorio donde suma mas horas.
            DateTime lunes = CuadriculaSemanal.LunesDe(semana);
            List<FranjaInfo> deEsaSemana = franjas.Where(f => f.VigenteDesde.Date <= lunes.AddDays(6) && f.VigenteHasta.Date >= lunes).ToList();
            int laboratorio = (deEsaSemana.Count > 0 ? deEsaSemana : franjas)
                .GroupBy(f => f.LaboratorioId)
                .OrderByDescending(g => g.Sum(f => (f.Fin - f.Inicio).TotalHours))
                .First().Key;
            if (!cboLabHorario.Items.OfType<OpcionCombo>().Any(o => o.Id == laboratorio))
            {
                FranjaInfo una = franjas.First(f => f.LaboratorioId == laboratorio);
                EstadoClases("Sus horas están en " + una.Laboratorio + ", que está deshabilitado: habilítalo en Laboratorios para verlo en Horario semanal.", true);
                return;
            }

            EstadoClases("", false);
            profesorFijado = clase.ProfesorId;
            CompartirLaboratorio(laboratorio, null);
            IrASemanaDeHorarios(semana);
        }

        private void EditarClase()
        {
            if (Permisos.Bloquear(this, "Editar una clase")) return;
            ClaseInfo clase = ClaseSeleccionada();
            if (clase == null)
            {
                MessageBox.Show("Selecciona una clase.", "Clase requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (DialogoClase dialogo = new DialogoClase(datos, consultas, clase, IdAdministrador()))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    CargarClases();
                }
            }
        }

        private void CambiarEstadoClase()
        {
            if (Permisos.Bloquear(this, "Activar, desactivar o terminar una clase")) return;
            ClaseInfo clase = ClaseSeleccionada();
            if (clase == null)
            {
                MessageBox.Show("Selecciona una clase.", "Clase requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Con sesiones no se desactiva: desactivada desapareceria tambien su
            // pasado de Faltas y de la asistencia. Se termina en una fecha (R4).
            if (clase.Activa && clase.SesionesTotales > 0)
            {
                TerminarClase(clase);
                return;
            }

            string pregunta = clase.Activa
                ? "¿Desactivar la clase " + clase.Codigo + " (" + clase.Descripcion + ")?" + Environment.NewLine
                  + "Su código dejará de entrar y sus horas no se mostrarán a sus estudiantes. Las bitácoras se conservan."
                : "¿Activar de nuevo la clase " + clase.Codigo + " (" + clase.Descripcion + ")?";
            // Desactivar: Enter responde No (la opcion segura); activar no quita nada.
            if (MessageBox.Show(pregunta, clase.Activa ? "Desactivar clase" : "Activar clase", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                clase.Activa ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                datos.CambiarEstadoClase(clase.Id, !clase.Activa);
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                // Tiene sesiones (de otro equipo, desde que se leyo) o chocaria con otra igual al activarla.
                MessageBox.Show(ex.Message, "No se pudo cambiar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.CambiarEstadoClase", "No se pudo cambiar el estado de la clase.", ex);
            }

            CargarClases();
        }

        /// <summary>
        /// "Terminar clase...": se elige su ultimo dia (por omision hoy, segun el
        /// servidor), nunca antes de su ultima sesion ni de su inicio ni despues
        /// de cuando ya termina (HorariosDatos.TerminarClase).
        /// </summary>
        private void TerminarClase(ClaseInfo clase)
        {
            DateTime hoy = DateTime.Today;
            try
            {
                hoy = consultas.AhoraServidor().Date;
            }
            catch (Exception ex)
            {
                Log.Error("AdministrarHorarios.TerminarClase (hora del servidor)", ex);
            }

            DateTime minimo = clase.VigenteDesde.Date;
            if (clase.UltimaSesion.HasValue && clase.UltimaSesion.Value.Date > minimo)
            {
                minimo = clase.UltimaSesion.Value.Date;
            }

            DateTime maximo = clase.VigenteHasta.Date;
            if (minimo >= maximo)
            {
                MessageBox.Show("La clase " + clase.Descripcion + " ya termina el " + maximo.ToString("dd/MM/yyyy")
                    + (clase.UltimaSesion.HasValue ? " y tiene sesiones hasta el " + clase.UltimaSesion.Value.ToString("dd/MM/yyyy") : "")
                    + ": no puede terminar antes.", "Terminar clase", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DateTime valor = hoy < minimo ? minimo : hoy > maximo ? maximo : hoy;
            DateTime fin;
            using (DialogoFecha dialogo = new DialogoFecha("Terminar la clase", DescripcionDeTerminar(clase), "Último día", valor, minimo, maximo, "Terminar clase"))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                fin = dialogo.Fecha;
            }

            try
            {
                datos.TerminarClase(clase.Id, fin);
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                MessageBox.Show(ex.Message, "No se pudo terminar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.TerminarClase", "No se pudo terminar la clase.", ex);
            }

            CargarClases();
            // Sus franjas dejan de salir despues de ese dia.
            CargarFranjas();
        }

        /// <summary>Lo que dice "Terminar clase...": que pasa, que se conserva y hasta cuando no puede terminar antes.</summary>
        private static string DescripcionDeTerminar(ClaseInfo clase)
        {
            return clase.Descripcion + "." + Environment.NewLine + Environment.NewLine
                + "Su último día de clase será la fecha que elijas: después su código ya no entra y sus horas dejan de salir en el horario. "
                + "Lo que ya pasó se queda como fue (Faltas, la asistencia y las bitácoras)." + Environment.NewLine + Environment.NewLine
                + "Tiene " + clase.SesionesTotales + (clase.SesionesTotales == 1 ? " sesión" : " sesiones")
                + (clase.UltimaSesion.HasValue ? ", la última el " + clase.UltimaSesion.Value.ToString("dd/MM/yyyy") + ": no puede terminar antes" : "")
                + ". Ahora su vigencia acaba el " + clase.VigenteHasta.ToString("dd/MM/yyyy") + ".";
        }

        // =====================================================================
        // Pestana Horario semanal
        // =====================================================================

        // Como se usa la cuadricula, en un renglon (el de abajo de la leyenda): cabe
        // entero a 1366 (unos 672 de los 694 px que le dejan los botones; medido el
        // 2026-11-03, la de antes pedia 766 y se cortaba). Lo demas (Ctrl+clic, el
        // marco naranja, lo rojo que choca) lo dice la ayuda, y cada clase su "Choca: ...".
        private const string LeyendaHorario = "Arrastra en los cuadros para poner una clase; clic en una clase: sus opciones. "
            + "Naranja: extra; rayado: cancelada.";

        // Solo consulta: no se eligen cuadros ni hay opciones que escriban.
        private const string LeyendaHorarioConsulta = "Pasa el ratón por una clase para ver sus datos; clic en un docente: solo sus clases. "
            + "Marco naranja: sesión extra; rayado: cancelada.";

        /// <summary>La leyenda corta de la cuadricula: la del administrador o la de solo consulta.</summary>
        private string Leyenda
        {
            get { return soloConsulta ? LeyendaHorarioConsulta : LeyendaHorario; }
        }

        /// <summary>
        /// La explicacion entera de la leyenda, al pasar el raton. En solo consulta
        /// no se eligen cuadros ni hay opciones que escriban. Con los cambios desde
        /// una fecha, poner una clase no es "todas las semanas".
        /// </summary>
        private string AyudaLeyenda()
        {
            return soloConsulta
                ? "Un cuadro por hora. Pasa el ratón por una clase para ver quién la da, la materia, el grupo y su vigencia.\n"
                  + "Cada docente tiene su color, el mismo en todo DigBit. Haz clic en su nombre, debajo, para ver solo sus clases (otro clic, todas).\n"
                  + "Marco naranja: sesión extra (rayada si choca con una clase); rayado gris: clase cancelada ese día. En rojo: lo que choca."
                : "Un cuadro por hora. Arrastra o usa Ctrl+clic para elegir cuadros (libres u ocupados, en uno o varios días)\n"
                  + "y pon en ellos una clase (" + (cambiosConFecha ? "cada semana, desde una fecha" : "todas las semanas")
                  + ") o haz un cambio de un día (solo ese día de la semana que ves).\n"
                  + "Clic en una clase: sus opciones. Cada docente tiene su color, el mismo en todo DigBit. Haz clic en su\n"
                  + "nombre, debajo, para ver solo sus clases (otro clic, todas). Marco naranja: sesión extra (rayada si choca\n"
                  + "con una clase); rayado gris: clase cancelada ese día. En rojo: lo que choca.\n"
                  + "Con el teclado, en la cuadrícula: las flechas mueven el cuadro y Mayús+flechas eligen varios; Ctrl+← →, la semana.";
        }

        /// <summary>
        /// El renglon de arriba de la leyenda: como se entra al laboratorio y, para
        /// la administracion, si "Cambiar horas" vale desde una fecha (verde) o
        /// tambien para las semanas pasadas (ambar), que antes solo se descubria al
        /// abrir el dialogo. conFecha null: no se pudo saber (no se dice).
        /// </summary>
        private void PonerEstadoHorario(string acceso, bool? conFecha)
        {
            string cambios = soloConsulta || !conFecha.HasValue ? ""
                : conFecha.Value ? "Cambiar horas: vale desde una fecha."
                : "Cambiar horas: vale también para las semanas pasadas (falta db/20).";
            lblEstadoHorario.Text = (acceso + " " + cambios).Trim();
            lblEstadoHorario.ForeColor = conFecha.HasValue && !conFecha.Value && !soloConsulta ? Color.FromArgb(190, 85, 0) : Color.DarkGreen;
            string ayudaCambios = soloConsulta || !conFecha.HasValue ? ""
                : conFecha.Value
                    ? "Cambiar o quitar horas que ya tienen historia vale desde la fecha que elijas: lo que ya pasó se queda como fue (Faltas, la asistencia y las bitácoras)."
                    : "Cambiar o quitar horas que ya tienen historia cambia también las semanas que ya pasaron (Faltas las vuelve a contar).\n"
                      + "Para que valga solo desde una fecha falta aplicar db/20_vigencia_franjas.sql en la base (ya no hay casilla en Ajustes: con ella, siempre).";
            ayuda.SetToolTip(lblEstadoHorario, string.IsNullOrEmpty(lblEstadoHorario.Text) ? null
                : lblEstadoHorario.Text + (ayudaCambios.Length > 0 ? "\n" + ayudaCambios : ""));
        }

        private OpcionCombo LaboratorioHorario()
        {
            return cboLabHorario.SelectedItem as OpcionCombo;
        }

        /// <summary>Generacion de codigos: si los de la semana que se ve ya salen en cada clase, o cuando se generan.</summary>
        private static string DetalleCodigos(CodigosDeLaSemana codigos, DateTime lunes)
        {
            if (codigos == null)
            {
                return "";
            }

            // Cortos: con "Cambiar horas: ..." detras caben en el renglon de arriba de la leyenda a 1366.
            if (!codigos.Generados(lunes))
            {
                return " (se generan el lunes " + lunes.ToString("dd/MM") + ")";
            }

            return codigos.SinSecreto
                ? " (sin db/13: solo de clases ya abiertas)"
                : " (el de cada clase; solo tú los ves)";
        }

        private void CargarFranjas()
        {
            RepartirColores();
            OpcionCombo laboratorio = LaboratorioHorario();
            List<FranjaInfo> franjas = new List<FranjaInfo>();
            // Las de todos los laboratorios, en una lectura: las de este van a la
            // cuadricula y todas cuentan para los choques de la semana (un mismo
            // grupo o docente en dos laboratorios a la vez).
            List<FranjaInfo> todas = new List<FranjaInfo>();
            // Como entran los alumnos en este laboratorio (null sin db/11: todos con
            // el codigo fijo de su profesor). Sin codigo fijo, el de cada profesor
            // no le dice nada a nadie y no se pinta en la cuadricula.
            Dictionary<int, ModoCodigo> modos = null;
            if (laboratorio != null)
            {
                try
                {
                    todas = datos.ObtenerTodasLasFranjas();
                    franjas = todas.Where(f => f.LaboratorioId == laboratorio.Id).ToList();
                }
                catch (Exception ex)
                {
                    MostrarError("AdministrarHorarios.CargarFranjas", "No se pudo leer el horario del laboratorio.", ex);
                }

                try
                {
                    modos = datos.LeerModos();
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.CargarFranjas (modo de acceso)", ex);
                }
            }

            lienzo.LaboratoriosSinCodigoFijo = modos == null ? null : modos.Where(m => m.Value != ModoCodigo.Fijo).Select(m => m.Key);
            ModoCodigo modo = modos != null && laboratorio != null ? ModosDeAcceso.De(modos, laboratorio.Id) : ModoCodigo.Fijo;

            // La semana que se ve (al abrir, la de hoy segun el servidor) y sus
            // excepciones, tambien las ya pasadas, para dibujarlas encima.
            DateTime ahora = DateTime.Now;
            try
            {
                ahora = consultas.AhoraServidor();
            }
            catch (Exception ex)
            {
                Log.Error("AdministrarHorarios.CargarFranjas (hora del servidor)", ex);
            }

            selectorSemana.Hoy = ahora.Date;
            if (!semanaElegida)
            {
                selectorSemana.Lunes = SelectorSemana.SemanaActual(ahora.Date);
            }

            // Si poner una clase es para todas las semanas o desde una fecha (el
            // menu, la ayuda y el renglon de arriba de la leyenda lo dicen; en solo
            // consulta no se pone nada). Null: no se pudo saber.
            bool? conFecha = null;
            try
            {
                cambiosConFecha = !soloConsulta && LeerCambiosConFecha();
                conFecha = cambiosConFecha;
            }
            catch (Exception ex)
            {
                Log.Error("AdministrarHorarios.CargarFranjas (cambios de horario desde una fecha)", ex);
                cambiosConFecha = false;
            }

            DateTime lunes = selectorSemana.Lunes;
            List<ExcepcionInfo> excepciones = new List<ExcepcionInfo>();
            // Las de todos los laboratorios, en una lectura: las de este van a la
            // cuadricula y las cancelaciones de todos quitan de los choques las
            // clases que ese dia no se dan.
            List<ExcepcionInfo> deTodos = new List<ExcepcionInfo>();
            if (laboratorio != null)
            {
                try
                {
                    deTodos = datos.ObtenerExcepciones(lunes, lunes.AddDays(6), 0);
                    excepciones = deTodos.Where(x => x.LaboratorioId == laboratorio.Id).ToList();
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.CargarFranjas (excepciones)", ex);
                }
            }

            // Generacion de codigos: el de cada clase de la semana que se ve, si ya
            // empezo (se generan el lunes). Solo aqui: el horario de un profesor y
            // las Faltas no los ensenan. Solo el administrador (regla de db/11): en
            // solo consulta no se piden, asi que tampoco se crea el secreto, y la
            // leyenda queda "Acceso: <forma>."
            CodigosDeLaSemana codigos = null;
            if (laboratorio != null && modo == ModoCodigo.Renovable && Permisos.VeCodigosGenerados)
            {
                try
                {
                    codigos = datos.CodigosGeneradosDe(laboratorio.Id, lunes, lunes.AddDays(6));
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.CargarFranjas (codigos de la semana)", ex);
                }
            }

            PonerEstadoHorario(modos != null && laboratorio != null ? "Acceso: " + ModosDeAcceso.Descripcion(modo) + DetalleCodigos(codigos, lunes) + "." : "",
                conFecha);

            // Los choques que ya tiene el horario en uso esa semana (lunes a domingo),
            // entre las clases de los laboratorios activos que ese dia se dan (sin las
            // canceladas): la regla de la revision de Proximo ciclo (decision 3;
            // docente o grupo en dos laboratorios). Los de este laboratorio, en rojo.
            HashSet<int> activos = new HashSet<int>(cboLabHorario.Items.OfType<OpcionCombo>().Select(o => o.Id));
            choquesDeLaSemana = ChoqueEnUso.DeLaSemana(ChoqueEnUso.SinLasCanceladas(todas.Where(f => activos.Contains(f.LaboratorioId)), lunes, deTodos), lunes);
            motivosDeChoque = ChoqueEnUso.MotivosPorFranja(choquesDeLaSemana);
            pastChoques.Mostrado = "Choques: " + choquesDeLaSemana.Count;
            pastChoques.Visible = choquesDeLaSemana.Count > 0;
            LeerPorAsignar();
            PonerLeyendaHorario();

            FranjaInfo anterior = FranjaSeleccionada();
            lienzo.CodigosGenerados = codigos;
            lienzo.Ahora = ahora;
            lienzo.Hoy = ahora.Date;
            lienzo.Semana = lunes;
            lienzo.Franjas = franjas;
            lienzo.Excepciones = excepciones;
            // La que estaba elegida, si esta semana sale (una que ya no se coloca no se edita a ciegas).
            lienzo.Seleccionada = anterior == null ? null : franjas.FirstOrDefault(f => f.Id == anterior.Id && lienzo.SeColoca(f));
            PonerLeyenda(franjas);
            ActualizarBotonesFranjas();
        }

        // Los motivos de cada franja que choca esa semana (ChoqueEnUso.MotivosPorFranja): AspectoDeChoque.
        private Dictionary<int, string> motivosDeChoque = new Dictionary<int, string>();

        /// <summary>
        /// Con db/25: lo que falta asignar en todos los laboratorios, para la
        /// pastilla ambar, y si poner o cambiar horas puede reemplazar a otra
        /// clase. Sin db/25 (null; Esquema ya lo dijo en el log), como antes: ni
        /// pastilla ni reemplazo. Si no se puede leer, tampoco (se anota por que).
        /// </summary>
        private void LeerPorAsignar()
        {
            List<HoraPorAsignar> leidas = null;
            try
            {
                leidas = datos.LeerHorasPorAsignar();
            }
            catch (Exception ex)
            {
                Log.Error("AdministrarHorarios.LeerPorAsignar (horas por asignar, db/25): sin reemplazo ni \"Falta asignar\" hasta volver a leer", ex);
            }

            conPorAsignar = leidas != null && !soloConsulta;
            porAsignar = leidas ?? new List<HoraPorAsignar>();
            if (porPoner != null && !porAsignar.Any(x => x.ClaseId == porPoner.ClaseId))
            {
                // Ya se puso (o ya no hace falta): no se sigue trayendo elegida.
                porPoner = null;
            }

            int minutos = porAsignar.Sum(x => x.Minutos);
            pastPorAsignar.Mostrado = "Falta asignar: " + HorariosDatos.Duracion(minutos);
            pastPorAsignar.Visible = minutos > 0;
            ayuda.SetToolTip(btnAsignar, conPorAsignar
                ? "Pon una clase en los cuadros que elegiste (arrastra o usa Ctrl+clic en la cuadrícula). Si ocupan otra clase de este\n"
                  + "laboratorio, antes te pregunta si la reemplaza: lo que se le quite queda en «Falta asignar» para ponerlo en otro horario."
                : TextosDelHorario.AyudaPonerClase);
        }

        /// <summary>
        /// La clase que el siguiente "Poner clase..." trae elegida: la traida de
        /// "Falta asignar" (porPoner) o la sin horas de "Ver en el horario"
        /// (sinHorasPorPoner); 0 si ninguna.
        /// </summary>
        private int ClasePreelegida()
        {
            return porPoner != null ? porPoner.ClaseId : sinHorasPorPoner != null ? sinHorasPorPoner.Id : 0;
        }

        /// <summary>Ya se le pusieron horas a esa clase: no se sigue trayendo elegida.</summary>
        private void YaSePuso(int claseId)
        {
            if (porPoner != null && porPoner.ClaseId == claseId)
            {
                porPoner = null;
            }

            if (sinHorasPorPoner != null && sinHorasPorPoner.Id == claseId)
            {
                sinHorasPorPoner = null;
            }
        }

        /// <summary>
        /// La leyenda de abajo: la de siempre o, con una clase traida de "Falta
        /// asignar" (porPoner) o sin horas (sinHorasPorPoner), que elegir para
        /// ponerla, en ambar.
        /// </summary>
        private void PonerLeyendaHorario()
        {
            if (porPoner != null && !soloConsulta)
            {
                HoraPorAsignar suya = porAsignar.FirstOrDefault(x => x.ClaseId == porPoner.ClaseId) ?? porPoner;
                int faltan = porAsignar.Where(x => x.ClaseId == porPoner.ClaseId).Sum(x => x.Minutos);
                int minutos = faltan > 0 ? faltan : suya.Minutos;
                lblLeyendaHorario.Text = "Para poner " + suya.Clase + " (" + (minutos <= 60 ? "falta " : "faltan ") + HorariosDatos.Duracion(minutos)
                    + "): elige cuadros y pulsa «Poner clase...», ya la trae elegida. Esc: olvidarla.";
                lblLeyendaHorario.ForeColor = PiezasConfiguracion.TextoAmbar;
                ayuda.SetToolTip(lblLeyendaHorario, lblLeyendaHorario.Text + "\n" + suya.Profesor + ". Salió de " + suya.Origen + ".");
                return;
            }

            // La sin horas de "Ver en el horario" (Clases), igual.
            if (sinHorasPorPoner != null && !soloConsulta)
            {
                lblLeyendaHorario.Text = "Para poner " + sinHorasPorPoner.Materia + " - " + sinHorasPorPoner.Grupo
                    + " (aún sin horas): elige cuadros y pulsa «Poner clase...». Esc: olvidarla.";
                lblLeyendaHorario.ForeColor = PiezasConfiguracion.TextoAmbar;
                ayuda.SetToolTip(lblLeyendaHorario, lblLeyendaHorario.Text + "\n" + sinHorasPorPoner.Profesor + ". «Poner clase...» ya la trae elegida.");
                return;
            }

            lblLeyendaHorario.Text = Leyenda;
            lblLeyendaHorario.ForeColor = Color.Gray;
            ayuda.SetToolTip(lblLeyendaHorario, AyudaLeyenda());
        }

        /// <summary>
        /// "Falta asignar: N h": la lista. "Ponerla..." lleva al laboratorio
        /// elegido con la clase preelegida para el siguiente "Poner clase..."
        /// (porPoner); "Ya no hace falta" la resuelve. En solo consulta, solo la lista.
        /// </summary>
        private void VerPorAsignar()
        {
            if (porAsignar.Count == 0)
            {
                return;
            }

            OpcionCombo laboratorio = LaboratorioHorario();
            List<OpcionCombo> laboratorios = cboLabHorario.Items.OfType<OpcionCombo>().ToList();
            Func<HoraPorAsignar, bool> resolver = null;
            if (!soloConsulta)
            {
                resolver = p =>
                {
                    if (Permisos.Bloquear(this, "Dar por resueltas unas horas por asignar")) return false;
                    if (!DialogoConfirmacion.Preguntar(this, "Ya no hace falta",
                        "¿Ya no hace falta asignar " + HorariosDatos.Duracion(p.Minutos) + " a " + p.Clase + "?",
                        p.Profesor + ". Salieron de " + p.Origen + ". Salen de «Falta asignar» y queda en el historial de cambios; "
                        + "el horario de la clase no cambia.", "Ya no hace falta", false))
                    {
                        return false;
                    }

                    try
                    {
                        datos.YaNoHaceFalta(p.Id);
                        return true;
                    }
                    catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
                    {
                        MessageBox.Show(this, ex.Message, "Ya no hace falta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    catch (Exception ex)
                    {
                        MostrarError("AdministrarHorarios.VerPorAsignar (ya no hace falta)", "No se pudo quitar de «Falta asignar».", ex);
                    }

                    return false;
                };
            }

            HoraPorAsignar elegida = null;
            int donde = 0;
            bool cambios;
            using (DialogoFaltaAsignar dialogo = new DialogoFaltaAsignar(porAsignar, laboratorios, laboratorio == null ? 0 : laboratorio.Id, resolver))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    elegida = dialogo.Elegida;
                    donde = dialogo.LaboratorioElegido;
                }

                cambios = dialogo.HuboCambios;
            }

            if (elegida == null)
            {
                if (cambios)
                {
                    CargarFranjas();
                }

                return;
            }

            porPoner = elegida;
            sinHorasPorPoner = null;
            lienzo.LimpiarSeleccion();
            if (laboratorio != null && laboratorio.Id == donde)
            {
                CargarFranjas();
            }
            else
            {
                // El combo lo lee (CargarFranjas) y lo pasa a las demas pestanas.
                SeleccionarSiEsta(cboLabHorario, donde);
            }

            ActiveControl = lienzo;
        }

        /// <summary>
        /// Una clase que choca, en rojo con "Choca: mismo grupo" (como en la
        /// preparacion de Proximo ciclo); las demas, como siempre (null).
        /// </summary>
        private AspectoEnCalendario AspectoDeChoque(FranjaInfo franja, DateTime fecha)
        {
            string motivos;
            if (franja == null || !motivosDeChoque.TryGetValue(franja.Id, out motivos) || !lienzo.VigenteEnSuColumna(franja))
            {
                return null;
            }

            return new AspectoEnCalendario { Fondo = PiezasConfiguracion.FondoRojo, Texto = PiezasConfiguracion.LetraRojaCuadro, Pie = "Choca: " + motivos };
        }

        /// <summary>"Choques: N" de Horario semanal: cuales son, y lleva al laboratorio del elegido.</summary>
        private void VerChoques()
        {
            if (choquesDeLaSemana.Count == 0)
            {
                return;
            }

            OpcionCombo laboratorio = LaboratorioHorario();
            using (DialogoChoquesDelHorario dialogo = new DialogoChoquesDelHorario(choquesDeLaSemana, selectorSemana.Lunes, laboratorio == null ? 0 : laboratorio.Id))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK || dialogo.Elegido == null)
                {
                    return;
                }

                // El laboratorio de la clase elegida (si es este, el de la otra: un choque siempre es entre dos), con su docente resaltado.
                ChoqueEnUso choque = dialogo.Elegido;
                FranjaInfo ver = laboratorio != null && choque.A.LaboratorioId == laboratorio.Id && choque.B.LaboratorioId != laboratorio.Id ? choque.B : choque.A;
                profesorFijado = ver.ProfesorId;
                lienzo.LimpiarSeleccion();
                if (laboratorio != null && laboratorio.Id == ver.LaboratorioId)
                {
                    // Mismo laboratorio: solo se resalta su docente.
                    MarcarProfesorFijado();
                    lienzo.ProfesorResaltado = profesorFijado;
                    ActualizarBotonesFranjas();
                }
                else
                {
                    // El combo lo lee (CargarFranjas) y lo pasa a las demas pestanas.
                    SeleccionarSiEsta(cboLabHorario, ver.LaboratorioId);
                }
            }
        }

        /// <summary>
        /// "Imprimir PDF": la semana que se ve del laboratorio elegido, tal cual
        /// (con sus cancelaciones y sesiones extra) y sin los codigos de los
        /// profesores ni lo que esa semana no se da (lo gris), en el visor de PDF
        /// de DigBit, como las bitacoras: ahi se ve y, si se quiere, se guarda
        /// para imprimirla.
        /// </summary>
        private void ImprimirPdf()
        {
            OpcionCombo laboratorio = LaboratorioHorario();
            if (laboratorio == null || Kiosco.Activo)
            {
                return;
            }

            DateTime lunes = lienzo.Semana ?? selectorSemana.Lunes;
            byte[] pdf;
            Cursor = Cursors.WaitCursor;
            try
            {
                // El pie dice cuando se genero: la hora del servidor al imprimir, no
                // la de la ultima lectura (lienzo.Ahora), que con Horarios abierta
                // desde la manana seria la de entonces. Sin servidor, la de este
                // equipo, como en CargarFranjas: la hoja sale igual.
                DateTime ahora = DateTime.Now;
                try
                {
                    ahora = consultas.AhoraServidor();
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.ImprimirPdf (hora del servidor)", ex);
                }

                pdf = PdfHorario.Crear(laboratorio.Texto, lunes, lienzo.Franjas, lienzo.Excepciones, ahora);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MostrarError("AdministrarHorarios.ImprimirPdf", "No se pudo preparar el PDF del horario.", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            try
            {
                VisorPdf.Mostrar(this, pdf, PdfBitacora.NombreArchivo("Horario", laboratorio.Texto, lunes.ToString("yyyy-MM-dd")));
            }
            catch (Exception ex)
            {
                // Un fallo de archivo aqui es el temporal de la vista previa, no uno que se eligio.
                AvisoDeFalloAdmin.MostrarVistaPrevia(EstiloHorarios.Dueno(this), "AdministrarHorarios.ImprimirPdf (visor)", ex,
                    "No se pudo mostrar el PDF del horario", Text);
            }
        }

        private void lienzo_ExcepcionClic(ExcepcionInfo extra)
        {
            extraEnMenu = extra;
            menuExtra.Items[0].Text = "Quitar la sesión extra de " + extra.Materia + " con " + extra.Grupo + " del " + extra.Fecha.ToString("dd/MM") + " (" + HorariosDatos.Hora(extra.Inicio) + "-" + HorariosDatos.Hora(extra.Fin) + ")...";
            menuExtra.Show(Cursor.Position);
        }

        /// <summary>Quita la sesion extra sobre la que se hizo clic en la cuadricula.</summary>
        private void QuitarExtraDeCuadricula()
        {
            if (Permisos.Bloquear(this, "Quitar una sesion extra (cuadricula)")) return;
            if (extraEnMenu == null)
            {
                return;
            }

            QuitarSesionExtra(extraEnMenu.Id, extraEnMenu.Materia + " con " + extraEnMenu.Grupo + " (" + extraEnMenu.Profesor + ", "
                + HorariosDatos.Hora(extraEnMenu.Inicio) + " a " + HorariosDatos.Hora(extraEnMenu.Fin) + " del " + extraEnMenu.Fecha.ToString("dd/MM/yyyy") + ")");
            CargarFranjas();
        }

        private void QuitarCancelacionDeFranja()
        {
            if (Permisos.Bloquear(this, "Quitar la cancelacion de una franja (cuadricula)")) return;
            FranjaInfo franja = FranjaSeleccionada();
            ExcepcionInfo cancelacion = lienzo.CancelacionDe(franja);
            if (cancelacion == null)
            {
                MessageBox.Show("Esa clase no está cancelada en la semana que ves: no hay nada que volver a dar.", "Volver a darla", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            QuitarCancelacionDe(cancelacion.Id, franja.Materia + " con " + franja.Grupo + " (" + franja.Profesor + ")",
                franja.LaboratorioId, cancelacion.Fecha, franja.Inicio, franja.Fin, franja.ClaseId);
            CargarFranjas();
        }

        /// <summary>
        /// Quita una sesion extra. Si al ponerla se cancelaron o recortaron ese dia
        /// clases de otros profesores (AplicarExcepcion), pregunta si se les
        /// devuelve su horario de siempre. Devuelve si se quito.
        /// </summary>
        private bool QuitarSesionExtra(int idExtra, string descripcion)
        {
            List<ExcepcionInfo> debajo;
            try
            {
                debajo = datos.CancelacionesBajoExtra(idExtra);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarSesionExtra", "No se pudo leer qué hay debajo de esa sesión extra.", ex);
                return false;
            }

            // Lo que no podria volver entero: otra sesion extra (no esta ni un resto
            // de esa misma clase) sigue encima ese dia. La base lo comprueba igual.
            List<string> choques = new List<string>();
            if (debajo.Count > 0)
            {
                try
                {
                    List<ExcepcionInfo> delDia = datos.ObtenerExcepciones(debajo[0].Fecha, debajo[0].Fecha, debajo[0].LaboratorioId);
                    foreach (ExcepcionInfo x in debajo)
                    {
                        foreach (ExcepcionInfo otra in delDia.Where(o => o.EsExtra && o.Id != idExtra && o.ClaseId != x.ClaseId && o.Inicio < x.Fin && o.Fin > x.Inicio))
                        {
                            choques.Add(x.Codigo + " (" + HorariosDatos.Hora(x.Inicio) + "-" + HorariosDatos.Hora(x.Fin) + ") chocaría con la sesión extra de "
                                + otra.Codigo + " " + otra.Materia + " (" + HorariosDatos.Hora(otra.Inicio) + "-" + HorariosDatos.Hora(otra.Fin) + ")");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.QuitarSesionExtra (choques)", ex);
                }
            }

            bool devolver = false;
            if (debajo.Count > 0 && choques.Count > 0)
            {
                if (MessageBox.Show("Vas a quitar la sesión extra de " + descripcion + "." + Environment.NewLine + Environment.NewLine
                    + "Lo que canceló ese día no puede volver entero: " + string.Join("; ", choques) + "." + Environment.NewLine + Environment.NewLine
                    + "¿Quitar solo esta sesión extra? Lo demás se queda como está.",
                    "Quitar sesión extra", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    return false;
                }
            }
            else if (debajo.Count > 0)
            {
                string cuales = string.Join(Environment.NewLine, debajo.Select(x => "   • " + x.Codigo + " " + x.Materia + " - " + x.Grupo
                    + " (" + x.Profesor + ", " + HorariosDatos.Hora(x.Inicio) + "-" + HorariosDatos.Hora(x.Fin) + ")"));
                DialogResult respuesta = MessageBox.Show("Vas a quitar la sesión extra de " + descripcion + "." + Environment.NewLine + Environment.NewLine
                    + "Ese día, en esas horas, " + (debajo.Count == 1 ? "esta clase está cancelada" : "estas clases están canceladas") + " (o recortada"
                    + (debajo.Count == 1 ? "" : "s") + "):" + Environment.NewLine + cuales + Environment.NewLine + Environment.NewLine
                    + "¿Le devuelves su horario de siempre ese día?" + Environment.NewLine
                    + "Sí: vuelve" + (debajo.Count == 1 ? "" : "n") + " entera" + (debajo.Count == 1 ? "" : "s") + ". "
                    + "No: solo se quita la sesión extra; lo demás se queda como está.",
                    "Quitar sesión extra", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
                if (respuesta == DialogResult.Cancel)
                {
                    return false;
                }

                devolver = respuesta == DialogResult.Yes;
            }
            else if (MessageBox.Show("¿Quitar la sesión extra de " + descripcion + "?", "Quitar sesión extra",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return false;
            }

            try
            {
                datos.QuitarExtra(idExtra, devolver);
                return true;
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                MessageBox.Show(ex.Message, "No se pudo quitar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarSesionExtra", "No se pudo quitar la sesión extra.", ex);
            }

            return false;
        }

        /// <summary>
        /// Quita una cancelacion y, si la clase se habia recortado, lo que le
        /// quedaba como sesion extra: vuelve entera ese dia. Avisa si ese dia hay
        /// en esas horas una sesion extra de otra clase (chocarian). Devuelve si se quito.
        /// </summary>
        private bool QuitarCancelacionDe(int idCancelacion, string descripcion, int idLaboratorio, DateTime fecha, TimeSpan inicio, TimeSpan fin, int idClase)
        {
            List<RestoDeClase> restos;
            List<ExcepcionInfo> encima;
            try
            {
                restos = datos.RestosDeCancelacion(idCancelacion);
                encima = datos.ObtenerExcepciones(fecha, fecha, idLaboratorio)
                    .Where(x => x.EsExtra && x.ClaseId != idClase && x.Inicio < fin && x.Fin > inicio)
                    .ToList();
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarCancelacionDe", "No se pudo leer esa cancelación.", ex);
                return false;
            }

            // Con otra clase encima no vuelve: las dos chocarian (la base tampoco lo deja).
            if (encima.Count > 0)
            {
                MessageBox.Show("No se puede volver a dar " + descripcion + " el " + fecha.ToString("dd/MM/yyyy") + ": ese día, en esas horas, hay "
                    + (encima.Count == 1 ? "una sesión extra" : "sesiones extra") + " de "
                    + string.Join("; ", encima.Select(x => x.Codigo + " " + x.Materia + " (" + x.Profesor + ", " + HorariosDatos.Hora(x.Inicio) + "-" + HorariosDatos.Hora(x.Fin) + ")"))
                    + " y las dos clases chocarían." + Environment.NewLine + "Quita antes esa sesión extra (o recórtala con un cambio de un día).",
                    "Volver a darla", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string pregunta = "¿Volver a dar " + descripcion + " el " + fecha.ToString("dd/MM/yyyy") + "?" + Environment.NewLine
                + "Se quita su cancelación: vuelve a darse entera ese día, de " + HorariosDatos.Hora(inicio) + " a " + HorariosDatos.Hora(fin)
                + (restos.Count == 0 ? "." : " (se quita lo que le quedaba como sesión extra: " + string.Join(" y ", restos) + ").");
            if (MessageBox.Show(pregunta, "Volver a darla", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return false;
            }

            try
            {
                datos.QuitarCancelacion(idCancelacion);
                return true;
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                MessageBox.Show(ex.Message, "No se pudo quitar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarCancelacionDe", "No se pudo quitar la cancelación.", ex);
            }

            return false;
        }

        private static bool FranjaVigente(FranjaInfo franja, DateTime hoy)
        {
            return franja.Activa && hoy.Date >= franja.VigenteDesde.Date && hoy.Date <= franja.VigenteHasta.Date;
        }

        private static string NombreDia(int dia)
        {
            return dia >= 1 && dia < HorariosDatos.NombresDia.Length ? HorariosDatos.NombresDia[dia] : dia.ToString();
        }

        private FranjaInfo FranjaSeleccionada()
        {
            return lienzo == null ? null : lienzo.Seleccionada;
        }

        /// <summary>
        /// Cada boton, encendido solo si tiene sobre que actuar: "Poner clase..."
        /// con cuadros libres elegidos, "Cambio de un dia..." con cuadros elegidos
        /// (libres u ocupados), y "Cambiar horas..." y "Quitar horas..." con una
        /// clase elegida. Apagados, dicen que elegir al pasar el raton.
        /// </summary>
        private void ActualizarBotonesFranjas()
        {
            bool hayLaboratorio = LaboratorioHorario() != null;
            bool hay = FranjaSeleccionada() != null;
            bool cuadros = hayLaboratorio && lienzo.SelectedCells.Cast<DataGridViewCell>().Any(c => c.ColumnIndex >= 1);
            // Con db/25 tambien sobre cuadros ocupados: los reemplaza (antes pregunta).
            btnAsignar.Enabled = cuadros && (conPorAsignar || lienzo.ObtenerHuecosSeleccionados().Count > 0);
            btnExcepcion.Enabled = cuadros;
            btnFranjaEditar.Enabled = hay;
            btnFranjaBorrar.Enabled = hay;
            btnImprimir.Enabled = hayLaboratorio;
        }

        private void lienzo_FranjaClic(FranjaInfo franja)
        {
            ActualizarBotonesFranjas();
            // En solo consulta el menu solo lleva "Ver esta clase en Clases" y "Ver
            // ese dia en Cambios de un dia" (lo demas escribe y no se ve).
            if (franja != null)
            {
                MostrarMenuFranja(franja);
            }
        }

        private void lienzo_HuecoDobleClic(int dia, TimeSpan hora)
        {
            NuevaFranja(dia, hora);
        }

        /// <summary>Se soltaron uno o varios cuadros: menu para poner una clase o hacer un cambio de un dia.</summary>
        private void lienzo_HuecosSeleccionados(List<RangoHueco> rangos)
        {
            ActualizarBotonesFranjas();
            if (LaboratorioHorario() == null || rangos.Count == 0)
            {
                return;
            }

            // Poner una clase solo usa los cuadros libres (y es para todas las
            // semanas, o cada semana desde una fecha con los cambios desde una
            // fecha); el cambio de un dia, todo lo elegido y solo ese dia de la
            // semana que se ve: cada opcion dice a que horas y cuando.
            // Con db/25 tambien los ocupados: los reemplaza (antes pregunta).
            List<RangoHueco> libres = conPorAsignar ? rangos : lienzo.ObtenerHuecosSeleccionados();
            string reemplaza = conPorAsignar && lienzo.HayOcupadosSeleccionados() ? ", reemplazando lo que haya" : "";
            menuHueco.Items[0].Text = libres.Count == 0 ? TextosDelHorario.SinCuadrosLibres
                : cambiosConFecha ? "Poner una clase el " + TextosDelHorario.Rangos(libres) + " (cada semana, desde una fecha" + reemplaza + ")..."
                : reemplaza.Length > 0 ? "Poner una clase el " + TextosDelHorario.Rangos(libres) + " (todas las semanas" + reemplaza + ")..."
                : TextosDelHorario.MenuPonerEnHuecos(libres);
            menuHueco.Items[0].Enabled = libres.Count > 0;
            menuHueco.Items[1].Text = "Cambio de un día: solo el " + RangosConFecha(rangos) + "...";
            menuHueco.Show(Cursor.Position);
        }

        /// <summary>
        /// "miercoles 28/10 de 09:00 a 11:00", con la fecha de la semana que se ve
        /// (como TextosDelHorario.Rangos); con mas de tres, las dos primeras "y N mas".
        /// </summary>
        private string RangosConFecha(List<RangoHueco> rangos)
        {
            List<string> partes = rangos.Select(r => TextosDelHorario.Dia(r.Dia) + " " + lienzo.FechaDeColumna(r.Dia).ToString("dd/MM") + " "
                + TextosDelHorario.Horas(r.Inicio, r.Fin)).ToList();
            if (partes.Count > 3)
            {
                partes = partes.Take(2).Concat(new[] { (partes.Count - 2) + " más" }).ToList();
            }

            return RevisionCiclo.Enumerar(partes);
        }

        /// <summary>
        /// "Poner clase...": cada bloque de cuadros libres elegido pasa a ser unas
        /// horas de la clase elegida. Lo que no deja ponerlas (sin db/25, otras
        /// horas del mismo laboratorio; siempre, la misma persona docente o el
        /// mismo grupo en otro laboratorio: un choque) se dice por bloque; los
        /// demas se ponen. Con db/25 tambien los
        /// cuadros ocupados: la otra clase del mismo laboratorio se reemplaza
        /// (PonerReemplazando, antes pregunta) y lo de la misma persona docente o
        /// el mismo grupo en otro laboratorio sigue sin ponerse.
        /// </summary>
        private void AsignarClaseASeleccion()
        {
            if (Permisos.Bloquear(this, "Poner una clase en cuadros del horario")) return;
            OpcionCombo laboratorio = LaboratorioHorario();
            // El boton y el menu solo se encienden con un laboratorio y cuadros libres
            // (con db/25, tambien ocupados) elegidos.
            List<RangoHueco> rangos = conPorAsignar ? lienzo.ObtenerRangosSeleccionados(false) : lienzo.ObtenerHuecosSeleccionados();
            if (laboratorio == null || rangos.Count == 0)
            {
                ActualizarBotonesFranjas();
                return;
            }

            int claseId;
            DateTime? desde;
            // Con los cambios desde una fecha, "A partir del" propone el lunes de la semana que se ve (o hoy).
            DateTime? hoy = HoyConFecha();
            DateTime? propuesta = hoy.HasValue ? NoAntesDeHoy(selectorSemana.Lunes, hoy.Value) : (DateTime?)null;
            List<ClaseInfo> clases = ClasesActivas();
            using (DialogoAsignarClase dialogo = new DialogoAsignarClase(clases, laboratorio, rangos, CrearClaseDesdeDialogo, hoy, propuesta))
            {
                // Traida de "Falta asignar" (Ponerla...) o de "Ver en el horario" (sin horas): ya elegida.
                int idPreelegida = ClasePreelegida();
                ClaseInfo preelegida = idPreelegida == 0 ? null : clases.FirstOrDefault(c => c.Id == idPreelegida);
                if (preelegida != null)
                {
                    dialogo.Preelegir(preelegida);
                }

                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                claseId = dialogo.ClaseId;
                desde = dialogo.Desde;
            }

            if (conPorAsignar)
            {
                PonerReemplazando(laboratorio, claseId, desde);
                return;
            }

            int creadas = 0;
            List<string> problemas = new List<string>();
            // Lo que hay que hacer si alguno fallo por algo que no es un conflicto
            // (la red, la base): una sola vez, despues de la lista.
            string queHacer = null;
            foreach (RangoHueco rango in rangos)
            {
                try
                {
                    // Sin los cambios desde una fecha, desde es null: como siempre.
                    datos.CrearFranja(claseId, laboratorio.Id, rango.Dia, rango.Inicio, rango.Fin, desde);
                    creadas++;
                }
                catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
                {
                    problemas.Add(rango.Descripcion + ": " + ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.AsignarClaseASeleccion", ex);
                    problemas.Add(rango.Descripcion + ": " + AvisoDeFalloAdmin.Causa(ex) + ".");
                    // Uno ya explicado (ErrorDeDatos) no trae accion (""): la de otro que venga despues si se dice.
                    if (string.IsNullOrEmpty(queHacer))
                    {
                        queHacer = AvisoDeFalloAdmin.Accion(ex);
                    }
                }
            }

            if (creadas > 0)
            {
                YaSePuso(claseId);
            }

            CargarFranjas();
            if (problemas.Count > 0)
            {
                MessageBox.Show((creadas == 1 ? "Se puso la clase en 1 horario. " : creadas > 1 ? "Se puso la clase en " + creadas + " horarios. " : "")
                    + "No se pudo poner en:" + Environment.NewLine + Environment.NewLine
                    + string.Join(Environment.NewLine, problemas)
                    + (string.IsNullOrEmpty(queHacer) ? "" : Environment.NewLine + Environment.NewLine + queHacer),
                    queHacer != null ? TextosDelHorario.TituloPoner
                    : problemas.All(p => TextosDelHorario.TituloDeCruce(p) == "Laboratorio ocupado") ? "Laboratorio ocupado" : "Choques de horario",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Poner clase con db/25: los cuadros elegidos sin los que ya son de esa
        /// clase; por bloque, lo que no deja ponerla (docente o grupo en otro
        /// laboratorio) se dice y ese bloque no se pone; lo de otras clases de este
        /// laboratorio se confirma una vez para todos («Reemplazar») y queda en
        /// «Falta asignar». Cancelar no pone nada.
        /// </summary>
        private void PonerReemplazando(OpcionCombo laboratorio, int claseId, DateTime? desde)
        {
            List<RangoHueco> rangos = lienzo.ObtenerRangosSeleccionadosSin(claseId);
            if (rangos.Count == 0)
            {
                MessageBox.Show(this, "Esas horas ya son de esa clase: no hay nada que poner.", TextosDelHorario.TituloPoner, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> problemas = new List<string>();
            string queHacer = null;
            List<RangoHueco> aPoner = new List<RangoHueco>();
            List<FranjaReemplazada> quitadas = new List<FranjaReemplazada>();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (RangoHueco rango in rangos)
                {
                    try
                    {
                        PlanReemplazo plan = datos.PlanDeReemplazo(null, claseId, laboratorio.Id, rango.Dia, rango.Inicio, rango.Fin, desde);
                        if (plan.Bloqueo != null)
                        {
                            problemas.Add(rango.Descripcion + ": " + plan.Bloqueo);
                            continue;
                        }

                        aPoner.Add(rango);
                        quitadas.AddRange(plan.Quitadas);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("AdministrarHorarios.PonerReemplazando (plan)", ex);
                        problemas.Add(rango.Descripcion + ": " + AvisoDeFalloAdmin.Causa(ex) + ".");
                        if (string.IsNullOrEmpty(queHacer))
                        {
                            queHacer = AvisoDeFalloAdmin.Accion(ex);
                        }
                    }
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (aPoner.Count > 0 && quitadas.Count > 0 && !ConfirmarReemplazo.Preguntar(this, quitadas, cambiosConFecha ? desde : null))
            {
                return;
            }

            int creadas = 0;
            foreach (RangoHueco rango in aPoner)
            {
                try
                {
                    datos.PonerClaseReemplazando(claseId, laboratorio.Id, rango.Dia, rango.Inicio, rango.Fin, desde);
                    creadas++;
                }
                catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
                {
                    problemas.Add(rango.Descripcion + ": " + ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.PonerReemplazando", ex);
                    problemas.Add(rango.Descripcion + ": " + AvisoDeFalloAdmin.Causa(ex) + ".");
                    if (string.IsNullOrEmpty(queHacer))
                    {
                        queHacer = AvisoDeFalloAdmin.Accion(ex);
                    }
                }
            }

            if (creadas > 0)
            {
                YaSePuso(claseId);
            }

            CargarFranjas();
            if (problemas.Count > 0)
            {
                MessageBox.Show((creadas == 1 ? "Se puso la clase en 1 horario. " : creadas > 1 ? "Se puso la clase en " + creadas + " horarios. " : "")
                    + "No se pudo poner en:" + Environment.NewLine + Environment.NewLine
                    + string.Join(Environment.NewLine, problemas)
                    + (string.IsNullOrEmpty(queHacer) ? "" : Environment.NewLine + Environment.NewLine + queHacer),
                    queHacer == null ? "Choques de horario" : TextosDelHorario.TituloPoner, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// "Cambio de un dia...": sobre los cuadros elegidos, esten libres u
        /// ocupados, solo el dia de cada columna en la semana elegida: o se quitan
        /// esas horas, o entra una sesion extra de una clase. A lo que se cruza se
        /// le quita solo lo que se cruza (HorariosDatos.AplicarExcepcion), cada
        /// bloque todo o nada. Al terminar la cuadricula va a esa semana y se
        /// dice que se hizo con cada clase.
        /// </summary>
        private void AplicarExcepcionASeleccion()
        {
            if (Permisos.Bloquear(this, "Hacer un cambio de un dia en cuadros del horario")) return;
            OpcionCombo laboratorio = LaboratorioHorario();
            // El boton y el menu solo se encienden con un laboratorio y cuadros elegidos.
            List<RangoHueco> rangos = lienzo.ObtenerRangosSeleccionados(false);
            if (laboratorio == null || rangos.Count == 0)
            {
                ActualizarBotonesFranjas();
                return;
            }

            List<ExcepcionPlaneada> plan;
            bool esExtra;
            int claseId;
            string motivo;
            DateTime lunes;
            using (DialogoExcepcionCuadricula dialogo = new DialogoExcepcionCuadricula(ClasesActivas(), laboratorio, rangos, lienzo.Franjas, lienzo.Excepciones,
                lienzo.Ahora, selectorSemana.Lunes, LeerExcepcionesDeLaSemana, CrearClaseDesdeDialogo))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                plan = dialogo.Plan;
                esExtra = dialogo.EsExtra;
                claseId = dialogo.ClaseId;
                motivo = dialogo.Motivo;
                lunes = dialogo.Lunes;
            }

            List<string> hecho = new List<string>();
            List<string> problemas = new List<string>();
            // Como en Poner clase: que hacer si alguno fallo por la red o la base, una vez al final.
            string queHacer = null;
            int administrador = IdAdministrador();
            foreach (ExcepcionPlaneada p in plan)
            {
                string bloque = NombreDia(p.Rango.Dia) + " " + p.Fecha.ToString("dd/MM/yyyy") + ", de " + HorariosDatos.Hora(p.Rango.Inicio)
                    + " a " + HorariosDatos.Hora(p.Rango.Fin);
                try
                {
                    ResultadoExcepcion resultado = datos.AplicarExcepcion(laboratorio.Id, p.Fecha, p.Rango.Inicio, p.Rango.Fin,
                        esExtra ? claseId : (int?)null, motivo, administrador);
                    hecho.Add(bloque + ":" + Environment.NewLine + (resultado.Lineas.Count == 0
                        ? "   • No había clases en esas horas: nada que quitar."
                        : string.Join(Environment.NewLine, resultado.Lineas.Select(l => "   • " + l))));
                }
                catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
                {
                    problemas.Add(bloque + ": " + ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.AplicarExcepcionASeleccion", ex);
                    problemas.Add(bloque + ": " + AvisoDeFalloAdmin.Causa(ex) + ".");
                    if (string.IsNullOrEmpty(queHacer))
                    {
                        queHacer = AvisoDeFalloAdmin.Accion(ex);
                    }
                }
            }

            // A la semana donde se aplico: ahi se ve el resultado.
            lienzo.LimpiarSeleccion();
            semanaElegida = true;
            selectorSemana.Lunes = lunes;
            CargarFranjas();

            string resumen = hecho.Count == 0 ? "" : string.Join(Environment.NewLine + Environment.NewLine, hecho);
            if (problemas.Count > 0)
            {
                resumen += (resumen.Length > 0 ? Environment.NewLine + Environment.NewLine : "")
                    + "No se hizo (en " + (problemas.Count == 1 ? "ese bloque" : "esos bloques") + " no se cambió nada):" + Environment.NewLine
                    + string.Join(Environment.NewLine, problemas.Select(p => "   • " + p))
                    + (string.IsNullOrEmpty(queHacer) ? "" : Environment.NewLine + Environment.NewLine + queHacer);
            }

            MessageBox.Show(resumen, problemas.Count == 0 ? "Cambio de un día hecho" : hecho.Count == 0 ? "No se hizo el cambio de un día" : "Cambio de un día hecho en parte",
                MessageBoxButtons.OK, problemas.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        /// <summary>Las cancelaciones y sesiones extra del laboratorio del horario en la semana de ese lunes.</summary>
        private List<ExcepcionInfo> LeerExcepcionesDeLaSemana(DateTime lunes)
        {
            OpcionCombo laboratorio = LaboratorioHorario();
            return laboratorio == null ? new List<ExcepcionInfo>() : datos.ObtenerExcepciones(lunes, lunes.AddDays(6), laboratorio.Id);
        }

        /// <summary>Lleva a la pestana Cambios de un dia con ese laboratorio y esa fecha (en "Un dia").</summary>
        private void MostrarExcepcionesDe(OpcionCombo laboratorio, DateTime fecha)
        {
            if (laboratorio != null)
            {
                // Sin leer la agenda: se lee una vez, al elegir la pestana.
                CompartirLaboratorio(laboratorio.Id, null);
            }

            compartiendo = true;
            try
            {
                cboPeriodo.SelectedIndex = 0;
                calendario.Modo = ModoCalendario.Dia;
                calendario.Seleccion = fecha.Date;
            }
            finally
            {
                compartiendo = false;
            }

            if (tabs.SelectedTab == tabExcepciones)
            {
                CargarAgenda();
            }
            else
            {
                // tabs_SelectedIndexChanged: CargarAgenda.
                tabs.SelectedTab = tabExcepciones;
            }
        }

        /// <summary>
        /// Clic en una clase de la cuadricula: se elige y sale el menu de opciones,
        /// que la nombra por su materia y su grupo (el codigo es el mismo en todas
        /// las clases de su docente). En solo consulta, solo los saltos.
        /// </summary>
        private void MostrarMenuFranja(FranjaInfo franja)
        {
            // Los cambios de un dia son de un dia: el de la columna, en la semana que se ve.
            DateTime fecha = lienzo.FechaDeColumna(franja.DiaSemana);
            string dia = TextosDelHorario.Dia(franja.DiaSemana) + " " + fecha.ToString("dd/MM");
            menuFranja.Items[6].Text = "Ver el " + dia + " en Cambios de un día";
            if (!soloConsulta)
            {
                bool vigente = FranjaVigente(franja, fecha);
                ExcepcionInfo cancelacion = lienzo.CancelacionDe(franja);
                menuFranja.Items[0].Text = TextosDelHorario.MenuCambiarHoras(franja.Materia, franja.Grupo, franja.DiaSemana, franja.Inicio, franja.Fin);
                menuFranja.Items[3].Text = "Cancelar esta clase solo el " + dia + (YaPaso(fecha, franja.Fin) ? " (ya pasó)" : "") + "...";
                menuFranja.Items[3].Visible = cancelacion == null;
                menuFranja.Items[3].Enabled = vigente;
                menuFranja.Items[4].Visible = cancelacion != null;
                if (cancelacion != null)
                {
                    menuFranja.Items[4].Text = "Volver a darla el " + TextosDelHorario.Dia(((int)cancelacion.Fecha.DayOfWeek + 6) % 7 + 1) + " "
                        + cancelacion.Fecha.ToString("dd/MM") + " (quitar la cancelación)...";
                }
            }

            menuFranja.Show(Cursor.Position);
        }

        /// <summary>Si esa clase (fecha y hora de fin) ya termino, con el reloj del servidor de la ultima carga.</summary>
        private bool YaPaso(DateTime fecha, TimeSpan fin)
        {
            DateTime ahora = lienzo.Ahora;
            return fecha.Date < ahora.Date || (fecha.Date == ahora.Date && fin <= ahora.TimeOfDay);
        }

        /// <summary>
        /// Cancela la clase elegida solo el dia de su columna en la semana que se
        /// ve. Si ya paso, queda como no dada con motivo: asi deja de contar en Faltas.
        /// </summary>
        private void CancelarDiaDeLaSemana()
        {
            if (Permisos.Bloquear(this, "Cancelar una franja un dia (cuadricula)")) return;
            FranjaInfo franja = FranjaSeleccionada();
            if (franja == null)
            {
                return;
            }

            DateTime fecha = lienzo.FechaDeColumna(franja.DiaSemana);
            string descripcion = franja.Materia + " con " + franja.Grupo + " (" + franja.Profesor + "), " + TextosDelHorario.Horas(franja.Inicio, franja.Fin)
                + " del " + TextosDelHorario.Dia(franja.DiaSemana) + " " + fecha.ToString("dd/MM/yyyy");
            string texto = "Se cancela solo ese día: " + descripcion + ". Las demás semanas no cambian."
                + (YaPaso(fecha, franja.Fin) ? " Esa clase ya pasó: queda como no dada (en Faltas sale cancelada, no en rojo)." : "");
            using (DialogoMotivo dialogo = DialogoMotivo.CancelarLaClase("Cancelar la clase solo ese día", texto, "Motivo (opcional)"))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    datos.CancelarFranja(franja.Id, fecha, dialogo.Motivo, IdAdministrador());
                    CargarFranjas();
                }
                catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
                {
                    MessageBox.Show(ex.Message, "No se pudo cancelar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MostrarError("AdministrarHorarios.CancelarDiaDeLaSemana", "No se pudo cancelar la clase.", ex);
                }
            }
        }

        private void NuevaFranja(int? dia, TimeSpan? hora)
        {
            if (Permisos.Bloquear(this, "Crear una franja")) return;
            OpcionCombo laboratorio = LaboratorioHorario();
            if (laboratorio == null)
            {
                MessageBox.Show("No hay un laboratorio elegido: elige uno arriba, en «Laboratorio».", TextosDelHorario.TituloPoner, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Con los cambios desde una fecha, "A partir del" propone el dia de la
            // columna del doble clic o, sin el, el lunes de la semana que se ve (o hoy).
            DateTime? hoy = HoyConFecha();
            DateTime? propuesta = hoy.HasValue ? NoAntesDeHoy(dia.HasValue ? lienzo.FechaDeColumna(dia.Value) : selectorSemana.Lunes, hoy.Value) : (DateTime?)null;
            List<ClaseInfo> clases = ClasesActivas();
            using (DialogoFranja dialogo = new DialogoFranja(datos, clases, laboratorio, null, dia, hora, hoy, propuesta))
            {
                // Con db/25 reemplaza (antes pregunta); la traida de "Falta asignar" (o la
                // sin horas de "Ver en el horario"), ya elegida.
                dialogo.Reemplazar = conPorAsignar;
                int idPreelegida = ClasePreelegida();
                ClaseInfo preelegida = idPreelegida == 0 ? null : clases.FirstOrDefault(c => c.Id == idPreelegida);
                if (preelegida != null)
                {
                    dialogo.Preelegir(preelegida);
                }

                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    YaSePuso(dialogo.ClaseGuardada);
                    CargarFranjas();
                }
            }
        }

        private void EditarFranja()
        {
            if (Permisos.Bloquear(this, "Editar una franja")) return;
            FranjaInfo franja = FranjaSeleccionada();
            OpcionCombo laboratorio = LaboratorioHorario();
            if (franja == null || laboratorio == null)
            {
                // El boton se apaga sin una clase elegida: aqui solo se llega si se solto entretanto.
                ActualizarBotonesFranjas();
                return;
            }

            // El dialogo lee lo que ya paso con la franja (HistoriaDeFranja) y decide como se guarda.
            using (DialogoFranja dialogo = new DialogoFranja(datos, ClasesActivas(), laboratorio, franja, null, null, null, null))
            {
                // Con db/25, encima de otra clase de este laboratorio la reemplaza (antes pregunta).
                dialogo.Reemplazar = conPorAsignar;
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    CargarFranjas();
                }
            }
        }

        /// <summary>
        /// " Hoy ya hubo clase con bitacoras: lo mas pronto, desde manana." si la
        /// sesion de hoy de la franja ya tiene bitacoras o (db/22) asistencias que
        /// dio su profesor, dicho segun lo que tenga; si no, "".
        /// </summary>
        internal static string AvisoDeHoy(HistoriaFranja historia)
        {
            return historia.SesionHoyConDadas ? " Hoy ya hubo clase con bitácoras o asistencias que dio su docente: lo más pronto, desde mañana."
                : historia.SesionHoyConBitacoras ? " Hoy ya hubo clase con bitácoras: lo más pronto, desde mañana."
                : "";
        }

        private void BorrarFranja()
        {
            if (Permisos.Bloquear(this, "Borrar una franja")) return;
            FranjaInfo franja = FranjaSeleccionada();
            if (franja == null)
            {
                // El boton se apaga sin una clase elegida: aqui solo se llega si se solto entretanto.
                ActualizarBotonesFranjas();
                return;
            }

            // "Bases de Datos con ISC-5002 (lunes de 08:00 a 10:00)", como el menu de la clase.
            string cual = franja.Materia + " con " + franja.Grupo + " (" + TextosDelHorario.Dia(franja.DiaSemana) + " "
                + TextosDelHorario.Horas(franja.Inicio, franja.Fin) + ")";

            // Lo que ya paso con ella decide como se quita: si no se puede leer, se
            // pregunta y se borra como siempre (la capa de datos protege igual).
            HistoriaFranja historia = null;
            try
            {
                historia = datos.HistoriaDeFranja(franja.Id);
            }
            catch (Exception ex)
            {
                Log.Error("AdministrarHorarios.BorrarFranja (historia de la franja " + franja.Id + ")", ex);
            }

            if (conPorAsignar)
            {
                QuitarConPregunta(franja, cual, historia);
                CargarFranjas();
                return;
            }

            try
            {
                if (historia != null && historia.ConFecha && historia.TieneHistoria)
                {
                    // Con historia y los cambios desde una fecha: termina el dia antes de la elegida.
                    if (franja.VigenteHasta.Date < historia.PrimerDiaPosible)
                    {
                        MessageBox.Show("Las horas de " + cual + " ya terminaron el " + franja.VigenteHasta.ToString("dd/MM/yyyy")
                            + ": no hay nada que quitar. Lo que ya pasó se queda como fue.", "Quitar horas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    string descripcion = "Las horas de " + cual + " dejan de darse desde la fecha que elijas. "
                        + "Lo que ya pasó se queda como fue (Faltas, la asistencia y las bitácoras)."
                        + (historia.CancelacionesFuturas > 0 ? " Se borran sus cancelaciones de esa fecha en adelante y, con ellas, sus recortes "
                            + "(lo que le quedaba a la clase esos días)." : "")
                        + AvisoDeHoy(historia);
                    DateTime desde;
                    using (DialogoFecha dialogo = new DialogoFecha("Quitar horas", descripcion, "Quitar desde", historia.PrimerDiaPosible,
                        historia.PrimerDiaPosible, franja.VigenteHasta.Date, "Quitar"))
                    {
                        if (dialogo.ShowDialog(this) != DialogResult.OK)
                        {
                            return;
                        }

                        desde = dialogo.Fecha;
                    }

                    datos.QuitarFranjaDesde(franja.Id, desde);
                }
                else
                {
                    // Sin historia, o con los cambios desde una fecha apagados: se borra,
                    // y si ya tiene historia se avisa de que tambien se va su pasado.
                    // Enter responde No: quitarla es a proposito.
                    string pregunta = "¿Quitar del horario las horas de " + cual + "?"
                        + (historia != null && historia.TieneHistoria ? Environment.NewLine + Environment.NewLine + DialogoFranja.AvisoSinFecha(historia, true) : "");
                    if (MessageBox.Show(pregunta, "Quitar horas", MessageBoxButtons.YesNo,
                        historia != null && historia.TieneHistoria ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        return;
                    }

                    // Con ellos encendidos (sin historia), QuitarFranjaDesde la borra; si
                    // desde que se leyo gano historia, la quita desde hoy en vez de reescribirla.
                    if (historia != null && historia.ConFecha)
                    {
                        datos.QuitarFranjaDesde(franja.Id, historia.PrimerDiaPosible);
                    }
                    else
                    {
                        datos.BorrarFranja(franja.Id);
                    }
                }
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                MessageBox.Show(ex.Message, "No se pudo quitar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.BorrarFranja", "No se pudieron quitar esas horas.", ex);
            }

            CargarFranjas();
        }

        /// <summary>
        /// "Quitar horas..." con db/25: lo mismo que antes (desde una fecha si tiene
        /// historia y los cambios desde una fecha; si no, se borra, con el aviso de
        /// su pasado), en un solo dialogo que ademas pregunta "¿Se va a dar en otro
        /// horario?": con «Si» (la opcion por omision) sus horas quedan en «Falta
        /// asignar»; con «No», la clase tiene menos horas.
        /// </summary>
        private void QuitarConPregunta(FranjaInfo franja, string cual, HistoriaFranja historia)
        {
            int minutos = HorariosDatos.MinutosEntre(franja.Inicio, franja.Fin);
            try
            {
                bool conFecha = historia != null && historia.ConFecha && historia.TieneHistoria;
                if (conFecha && franja.VigenteHasta.Date < historia.PrimerDiaPosible)
                {
                    MessageBox.Show("Las horas de " + cual + " ya terminaron el " + franja.VigenteHasta.ToString("dd/MM/yyyy")
                        + ": no hay nada que quitar. Lo que ya pasó se queda como fue.", "Quitar horas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string descripcion = conFecha
                    ? "Las horas de " + cual + " dejan de darse desde la fecha que elijas. Lo que ya pasó se queda como fue (Faltas, la asistencia y las bitácoras)."
                      + (historia.CancelacionesFuturas > 0 ? " Se borran sus cancelaciones de esa fecha en adelante y, con ellas, sus recortes "
                          + "(lo que le quedaba a la clase esos días)." : "")
                      + AvisoDeHoy(historia)
                    : "Se quitan del horario las horas de " + cual + ".";
                string aviso = !conFecha && historia != null && historia.TieneHistoria ? DialogoFranja.AvisoSinFecha(historia, true) : null;
                bool porAsignarlas;
                DateTime desde;
                using (DialogoQuitarHoras dialogo = new DialogoQuitarHoras(descripcion, aviso,
                    conFecha ? historia.PrimerDiaPosible : (DateTime?)null, conFecha ? franja.VigenteHasta.Date : (DateTime?)null, minutos))
                {
                    if (dialogo.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    porAsignarlas = dialogo.PorAsignar;
                    desde = dialogo.Fecha;
                }

                if (conFecha)
                {
                    datos.QuitarFranjaDesde(franja.Id, desde, porAsignarlas);
                }
                else if (historia != null && historia.ConFecha)
                {
                    // Como antes: si desde que se leyo gano historia, la quita desde hoy en vez de reescribirla.
                    datos.QuitarFranjaDesde(franja.Id, historia.PrimerDiaPosible, porAsignarlas);
                }
                else
                {
                    datos.BorrarFranja(franja.Id, porAsignarlas);
                }
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                MessageBox.Show(ex.Message, "No se pudo quitar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarConPregunta", "No se pudieron quitar esas horas.", ex);
            }
        }

        /// <summary>
        /// Con los cambios de horario desde una fecha encendidos
        /// (VigenciaFranjas.ConFecha): hoy segun el servidor, para el "A partir
        /// del" de una franja nueva. Si no (o si no se puede saber), null: la
        /// franja se crea como siempre y la capa de datos decide.
        /// </summary>
        private DateTime? HoyConFecha()
        {
            try
            {
                if (!LeerCambiosConFecha())
                {
                    return null;
                }

                return consultas.AhoraServidor().Date;
            }
            catch (Exception ex)
            {
                Log.Error("AdministrarHorarios: saber si los cambios de horario aplican desde una fecha", ex);
                return null;
            }
        }

        /// <summary>VigenciaFranjas.ConFecha en una conexion nueva (lanza si no se puede leer).</summary>
        private static bool LeerCambiosConFecha()
        {
            using (MySql.Data.MySqlClient.MySqlConnection conexion = new Conexion().GetConexion())
            {
                return VigenciaFranjas.ConFecha(conexion);
            }
        }

        /// <summary>
        /// Lo que propone "A partir del" de una franja nueva: esa fecha (el lunes
        /// de la semana que se ve, o el dia de la columna del doble clic), pero
        /// nunca antes de hoy. Asi, asignar en la semana que viene el hueco que
        /// deja una franja que termina esta semana no choca con ella.
        /// </summary>
        private static DateTime NoAntesDeHoy(DateTime fecha, DateTime hoy)
        {
            return fecha.Date > hoy.Date ? fecha.Date : hoy.Date;
        }

        // =====================================================================
        // Pestana Excepciones
        // =====================================================================

        private OpcionCombo LaboratorioExcepciones()
        {
            return cboLabExcepciones.SelectedItem as OpcionCombo;
        }

        /// <summary>La agenda, salvo si se esta poniendo desde otra pestana (compartiendo): se lee al elegir esta.</summary>
        private void CargarAgendaSiSeVe()
        {
            if (!compartiendo || tabs.SelectedTab == tabExcepciones)
            {
                CargarAgenda();
            }
        }

        /// <summary>Rango que abarca el filtro: un dia, su semana (lunes a domingo) o su mes.</summary>
        private void RangoDelPeriodo(DateTime fecha, out DateTime desde, out DateTime hasta)
        {
            switch (cboPeriodo.SelectedIndex)
            {
                case 1:
                    desde = fecha.AddDays(-(((int)fecha.DayOfWeek + 6) % 7));
                    hasta = desde.AddDays(6);
                    break;
                case 2:
                    desde = new DateTime(fecha.Year, fecha.Month, 1);
                    hasta = desde.AddMonths(1).AddDays(-1);
                    break;
                default:
                    desde = fecha;
                    hasta = fecha;
                    break;
            }
        }

        private void CargarAgenda()
        {
            OpcionCombo laboratorio = LaboratorioExcepciones();
            DateTime fecha = calendario.Seleccion;
            DateTime desde, hasta;
            RangoDelPeriodo(fecha, out desde, out hasta);
            bool soloExcepciones = cboPeriodo.SelectedIndex != 0;

            List<FilaAgenda> filas = new List<FilaAgenda>();
            if (laboratorio != null)
            {
                try
                {
                    if (soloExcepciones)
                    {
                        // Semana o mes: una sola consulta con las excepciones del rango.
                        filas = datos.ObtenerExcepciones(desde, hasta, laboratorio.Id)
                            .Select(FilaAgenda.De)
                            .OrderBy(f => f.Fecha).ThenBy(f => f.Inicio)
                            .ToList();
                    }
                    else
                    {
                        filas = datos.AgendaDelDia(laboratorio.Id, fecha).Select(s => FilaAgenda.De(s, fecha)).ToList();
                    }
                }
                catch (Exception ex)
                {
                    MostrarError("AdministrarHorarios.CargarAgenda", "No se pudo leer la agenda.", ex);
                }
            }

            // La columna Codigo dice con que entran ese dia: el del profesor (codigo
            // unico), el generado de esa clase (solo de las semanas que ya
            // empezaron) o nada (sin codigos). Sin los codigos de los profesores
            // (solo consulta) la columna no se ve y no se calcula; los generados,
            // solo el administrador (asi tampoco se crea el secreto).
            CodigosDeLaSemana codigos = null;
            ModoCodigo modo = ModoCodigo.Fijo;
            bool conCodigos = Permisos.VeCodigosDeProfesor;
            if (laboratorio != null && filas.Count > 0 && conCodigos)
            {
                try
                {
                    modo = ModosDeAcceso.De(datos.LeerModos(), laboratorio.Id);
                    if (modo == ModoCodigo.Renovable && Permisos.VeCodigosGenerados)
                    {
                        codigos = datos.CodigosGeneradosDe(laboratorio.Id, desde, hasta);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.CargarAgenda (codigos)", ex);
                }
            }

            if (conCodigos)
            {
                foreach (FilaAgenda f in filas)
                {
                    f.CodigoDelDia = modo == ModoCodigo.Fijo ? f.Codigo
                        : modo == ModoCodigo.SinCodigo ? "—"
                        : f.Cancelada || codigos == null ? ""
                        : (f.EsExtra ? codigos.DeExtra(f.ExcepcionId.GetValueOrDefault(), f.Fecha) : codigos.DeFranja(f.FranjaId.GetValueOrDefault(), f.Fecha)) ?? "";
                }
            }

            // El titulo: lo que se ve y cuantos cambios hay; sin nada, que hacer.
            int cambios = filas.Count(f => f.Cancelada || f.EsExtra);
            string nombreLab = laboratorio == null ? "" : laboratorio.Texto;
            string titulo;
            if (soloExcepciones)
            {
                titulo = "Del " + desde.ToString("dd/MM/yyyy") + " al " + hasta.ToString("dd/MM/yyyy") + ": "
                    + (cambios == 0 ? "no hay clases canceladas ni sesiones extra en estas fechas."
                        : cambios + (cambios == 1 ? " cambio" : " cambios") + " (clases canceladas un día y sesiones extra).");
            }
            else
            {
                string dia = NombreDia(((int)fecha.DayOfWeek + 6) % 7 + 1) + " " + fecha.ToString("dd/MM/yyyy") + ": ";
                titulo = filas.Count == 0
                    ? dia + "ese día no hay clases en " + nombreLab + "." + (soloConsulta ? "" : " Para dar una, pulsa «Dar una sesión extra...».")
                    : dia + filas.Count + (filas.Count == 1 ? " clase, " : " clases, ") + cambios + (cambios == 1 ? " cambio." : " cambios.");
            }

            lblDiaAgenda.Text = titulo + (codigos != null && !codigos.Generados(hasta) ? " Los códigos de una semana se generan su lunes." : "");
            ayuda.SetToolTip(lblDiaAgenda, lblDiaAgenda.Text);

            dgvAgenda.Rows.Clear();
            foreach (FilaAgenda f in filas)
            {
                string estado = f.Cancelada
                    ? "Cancelada" + (string.IsNullOrWhiteSpace(f.Motivo) ? "" : ": " + f.Motivo)
                    : f.EsExtra
                        ? "Sesión extra" + (string.IsNullOrWhiteSpace(f.Motivo) ? "" : ": " + f.Motivo)
                        : "Se da";

                int fila = dgvAgenda.Rows.Add(
                    NombreDia(((int)f.Fecha.DayOfWeek + 6) % 7 + 1).Substring(0, 3) + " " + f.Fecha.ToString("dd/MM"),
                    HorariosDatos.Hora(f.Inicio),
                    HorariosDatos.Hora(f.Fin),
                    f.CodigoDelDia,
                    f.Materia,
                    f.Grupo,
                    f.Profesor,
                    estado);
                dgvAgenda.Rows[fila].Tag = f;
                if (f.Cancelada)
                {
                    dgvAgenda.Rows[fila].DefaultCellStyle.ForeColor = Color.Gray;
                    dgvAgenda.Rows[fila].DefaultCellStyle.BackColor = Color.FromArgb(235, 235, 235);
                }
                else if (f.EsExtra)
                {
                    dgvAgenda.Rows[fila].DefaultCellStyle.BackColor = Color.FromArgb(255, 230, 200);
                }
            }

            ActualizarBotonesAgenda();
        }

        private FilaAgenda SesionSeleccionada()
        {
            return dgvAgenda.CurrentRow == null ? null : dgvAgenda.CurrentRow.Tag as FilaAgenda;
        }

        private void ActualizarBotonesAgenda()
        {
            FilaAgenda sesion = SesionSeleccionada();
            bool hayLab = LaboratorioExcepciones() != null;
            btnSesionExtra.Enabled = hayLab;
            btnCancelarDia.Enabled = sesion != null && !sesion.EsExtra && !sesion.Cancelada && sesion.FranjaId.HasValue;
            btnQuitarCancelacion.Enabled = sesion != null && sesion.Cancelada && sesion.FranjaId.HasValue;
            btnQuitarExtra.Enabled = sesion != null && sesion.EsExtra && sesion.ExcepcionId.HasValue;
            // Con "Una semana" o "Un mes" (solo los cambios) no se cancela ni se da
            // nada desde aqui: en vez de los botones, como hacerlo. En solo consulta, nada.
            if (!soloConsulta)
            {
                bool unDia = cboPeriodo.SelectedIndex == 0;
                btnCancelarDia.Visible = unDia;
                btnQuitarCancelacion.Visible = unDia;
                btnSesionExtra.Visible = unDia;
                btnQuitarExtra.Visible = unDia;
                lblSoloEnUnDia.Visible = !unDia;
            }
        }

        private void CancelarFranjaDelDia()
        {
            if (Permisos.Bloquear(this, "Cancelar una franja un dia (Cambios de un dia)")) return;
            FilaAgenda sesion = SesionSeleccionada();
            if (sesion == null || sesion.EsExtra || sesion.Cancelada || !sesion.FranjaId.HasValue)
            {
                // El boton se apaga sin una clase que se da ese dia: aqui solo se llega si cambio la fila entretanto.
                ActualizarBotonesAgenda();
                return;
            }

            DateTime fecha = sesion.Fecha;
            string descripcion = sesion.Materia + " con " + sesion.Grupo + " (" + sesion.Profesor + "), de " + HorariosDatos.Hora(sesion.Inicio)
                + " a " + HorariosDatos.Hora(sesion.Fin) + " del " + fecha.ToString("dd/MM/yyyy");
            using (DialogoMotivo dialogo = DialogoMotivo.CancelarLaClase("Cancelar la clase solo ese día",
                "Se cancela solo ese día: " + descripcion + ". Las demás semanas no cambian.", "Motivo (opcional)"))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    datos.CancelarFranja(sesion.FranjaId.Value, fecha, dialogo.Motivo, IdAdministrador());
                }
                catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
                {
                    MessageBox.Show(ex.Message, "No se pudo cancelar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MostrarError("AdministrarHorarios.CancelarFranjaDelDia", "No se pudo cancelar la clase.", ex);
                }
            }

            CargarAgenda();
        }

        private void QuitarCancelacion()
        {
            if (Permisos.Bloquear(this, "Quitar una cancelacion (Cambios de un dia)")) return;
            FilaAgenda sesion = SesionSeleccionada();
            OpcionCombo laboratorio = LaboratorioExcepciones();
            if (sesion == null || !sesion.Cancelada || !sesion.FranjaId.HasValue || laboratorio == null)
            {
                // El boton se apaga sin una clase cancelada elegida.
                ActualizarBotonesAgenda();
                return;
            }

            DateTime fecha = sesion.Fecha;
            try
            {
                // En Semana y Mes la fila ya trae el id de la excepcion; en Dia hay que buscarlo.
                int? idCancelacion = sesion.ExcepcionId;
                if (!idCancelacion.HasValue)
                {
                    ExcepcionInfo cancelacion = datos.ObtenerExcepciones(fecha, fecha, laboratorio.Id)
                        .FirstOrDefault(x => !x.EsExtra && x.FranjaId.HasValue && x.FranjaId.Value == sesion.FranjaId.Value);
                    idCancelacion = cancelacion == null ? (int?)null : cancelacion.Id;
                }

                if (!idCancelacion.HasValue)
                {
                    MessageBox.Show("No se encontró la cancelación de esa clase: se quitó desde otro equipo o desde otra ventana. La lista se vuelve a leer.",
                        "Volver a darla", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    CargarAgenda();
                    return;
                }

                QuitarCancelacionDe(idCancelacion.Value, sesion.Materia + " con " + sesion.Grupo + " (" + sesion.Profesor + ")", laboratorio.Id, fecha,
                    sesion.Inicio, sesion.Fin, sesion.ClaseId);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarCancelacion", "No se pudo quitar la cancelación.", ex);
            }

            CargarAgenda();
        }

        private void NuevaSesionExtra()
        {
            if (Permisos.Bloquear(this, "Crear una sesion extra")) return;
            OpcionCombo laboratorio = LaboratorioExcepciones();
            if (laboratorio == null)
            {
                MessageBox.Show("No hay un laboratorio elegido: elige uno arriba, en «Laboratorio».", "Dar una sesión extra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            FilaAgenda sesion = SesionSeleccionada();
            TimeSpan? inicioSugerido = sesion == null ? (TimeSpan?)null : sesion.Fin;
            DateTime fechaExtra = sesion != null ? sesion.Fecha : calendario.Seleccion;
            using (DialogoExtra dialogo = new DialogoExtra(datos, ClasesActivas(), laboratorio, fechaExtra, inicioSugerido, IdAdministrador()))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    CargarAgenda();
                }
            }
        }

        private void QuitarExtra()
        {
            if (Permisos.Bloquear(this, "Quitar una sesion extra (Cambios de un dia)")) return;
            FilaAgenda sesion = SesionSeleccionada();
            if (sesion == null || !sesion.EsExtra || !sesion.ExcepcionId.HasValue)
            {
                // El boton se apaga sin una sesion extra elegida.
                ActualizarBotonesAgenda();
                return;
            }

            QuitarSesionExtra(sesion.ExcepcionId.Value, sesion.Materia + " con " + sesion.Grupo + " (" + sesion.Profesor + ", "
                + HorariosDatos.Hora(sesion.Inicio) + " a " + HorariosDatos.Hora(sesion.Fin) + " del " + sesion.Fecha.ToString("dd/MM/yyyy") + ")");
            CargarAgenda();
        }
    }

    // =========================================================================
    // Una fila de la pestana Excepciones: vale para la agenda de un dia y para
    // la lista de excepciones de una semana o un mes
    // =========================================================================

    internal class FilaAgenda
    {
        public DateTime Fecha;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public string Codigo;
        public string Materia;
        public string Grupo;
        public string Profesor;
        public string Motivo = "";
        public bool Cancelada;
        public bool EsExtra;
        public int? FranjaId;
        /// <summary>Id de la excepcion cuando se conoce (siempre en semana/mes).</summary>
        public int? ExcepcionId;
        public int ClaseId;
        /// <summary>Lo que sale en la columna Codigo: con que entran ese dia (segun el modo del laboratorio).</summary>
        public string CodigoDelDia = "";

        public string Descripcion
        {
            get { return Materia + " - " + Grupo + " - " + Profesor; }
        }

        public static FilaAgenda De(SesionAgenda sesion, DateTime fecha)
        {
            return new FilaAgenda
            {
                Fecha = fecha,
                Inicio = sesion.Inicio,
                Fin = sesion.Fin,
                ClaseId = sesion.ClaseId,
                Codigo = sesion.Codigo,
                Materia = sesion.Materia,
                Grupo = sesion.Grupo,
                Profesor = sesion.Profesor,
                Motivo = sesion.Motivo ?? "",
                Cancelada = sesion.Cancelada,
                EsExtra = sesion.EsExtra,
                FranjaId = sesion.FranjaId,
                ExcepcionId = sesion.ExcepcionId
            };
        }

        public static FilaAgenda De(ExcepcionInfo excepcion)
        {
            return new FilaAgenda
            {
                Fecha = excepcion.Fecha,
                Inicio = excepcion.Inicio,
                Fin = excepcion.Fin,
                ClaseId = excepcion.ClaseId,
                Codigo = excepcion.Codigo,
                Materia = excepcion.Materia,
                Grupo = excepcion.Grupo,
                Profesor = excepcion.Profesor,
                Motivo = excepcion.Motivo ?? "",
                Cancelada = !excepcion.EsExtra,
                EsExtra = excepcion.EsExtra,
                FranjaId = excepcion.FranjaId,
                ExcepcionId = excepcion.Id
            };
        }
    }


    // =========================================================================
    // Calendario del filtro de excepciones: mes completo, con los dias que no se
    // pueden elegir sombreados segun el modo (Dia: todos; Semana: solo los
    // lunes; Mes: ninguno, solo se cambia de mes con las flechas)
    // =========================================================================

    internal enum ModoCalendario
    {
        Dia = 0,
        Semana = 1,
        Mes = 2
    }

    internal class CalendarioFiltro : Panel
    {
        public static readonly string[] Meses =
        {
            "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
            "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        private static readonly string[] Dias = { "Lu", "Ma", "Mi", "Ju", "Vi", "Sa", "Do" };
        private const int AltoCabecera = 30;
        private const int AltoDias = 20;
        private const int Semanas = 6;

        /// <summary>Solo cuando el usuario elige: los cambios por codigo no lo disparan.</summary>
        public event Action<DateTime> SeleccionCambiada;

        private ModoCalendario modo = ModoCalendario.Dia;
        private DateTime seleccion = DateTime.Today;
        private DateTime mesVisible = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime? bajoRaton;

        private readonly DrawingFont fuenteMes = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteDias = new DrawingFont("Century Gothic", 8F, FontStyle.Bold);
        private readonly DrawingFont fuenteNumero = new DrawingFont("Century Gothic", 9F, FontStyle.Regular);
        private readonly DrawingFont fuenteNumeroHoy = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);

        public CalendarioFiltro()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
            BorderStyle = BorderStyle.FixedSingle;
            seleccion = Ajustar(DateTime.Today, modo);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuenteMes.Dispose();
                fuenteDias.Dispose();
                fuenteNumero.Dispose();
                fuenteNumeroHoy.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Dia, lunes de la semana o primero del mes, segun el modo.</summary>
        public DateTime Seleccion
        {
            get { return seleccion; }
            set
            {
                seleccion = Ajustar(value, modo);
                mesVisible = new DateTime(seleccion.Year, seleccion.Month, 1);
                Invalidate();
            }
        }

        public ModoCalendario Modo
        {
            get { return modo; }
            set
            {
                modo = value;
                seleccion = Ajustar(seleccion, modo);
                mesVisible = new DateTime(seleccion.Year, seleccion.Month, 1);
                Invalidate();
            }
        }

        private static DateTime Ajustar(DateTime fecha, ModoCalendario modo)
        {
            switch (modo)
            {
                case ModoCalendario.Semana:
                    return fecha.Date.AddDays(-(((int)fecha.DayOfWeek + 6) % 7));
                case ModoCalendario.Mes:
                    return new DateTime(fecha.Year, fecha.Month, 1);
                default:
                    return fecha.Date;
            }
        }

        /// <summary>En Semana solo se pueden elegir lunes; en Mes ningun dia.</summary>
        private bool SePuedeElegir(DateTime fecha)
        {
            switch (modo)
            {
                case ModoCalendario.Semana:
                    return fecha.DayOfWeek == DayOfWeek.Monday;
                case ModoCalendario.Mes:
                    return false;
                default:
                    return true;
            }
        }

        private bool EstaEnSeleccion(DateTime fecha)
        {
            switch (modo)
            {
                case ModoCalendario.Semana:
                    return fecha >= seleccion && fecha <= seleccion.AddDays(6);
                case ModoCalendario.Mes:
                    return fecha.Year == seleccion.Year && fecha.Month == seleccion.Month;
                default:
                    return fecha == seleccion;
            }
        }

        // --- Geometria --------------------------------------------------------

        private int AnchoCelda
        {
            get { return Math.Max(1, ClientSize.Width / 7); }
        }

        private int AltoCelda
        {
            get { return Math.Max(1, (ClientSize.Height - AltoCabecera - AltoDias) / Semanas); }
        }

        /// <summary>Primer dia de la cuadricula: el lunes de la semana del dia 1.</summary>
        private DateTime PrimerDiaVisible
        {
            get { return mesVisible.AddDays(-(((int)mesVisible.DayOfWeek + 6) % 7)); }
        }

        private Rectangle RectanguloCelda(int fila, int columna)
        {
            return new Rectangle(columna * AnchoCelda, AltoCabecera + AltoDias + fila * AltoCelda, AnchoCelda, AltoCelda);
        }

        private Rectangle FlechaIzquierda
        {
            get { return new Rectangle(4, 4, 26, AltoCabecera - 8); }
        }

        private Rectangle FlechaDerecha
        {
            get { return new Rectangle(ClientSize.Width - 30, 4, 26, AltoCabecera - 8); }
        }

        private DateTime? FechaEn(Point punto)
        {
            if (punto.Y < AltoCabecera + AltoDias)
            {
                return null;
            }

            int fila = (punto.Y - AltoCabecera - AltoDias) / AltoCelda;
            int columna = punto.X / AnchoCelda;
            if (fila < 0 || fila >= Semanas || columna < 0 || columna > 6)
            {
                return null;
            }

            return PrimerDiaVisible.AddDays(fila * 7 + columna);
        }

        // --- Interaccion ------------------------------------------------------

        /// <summary>
        /// Ctrl+← → (TeclasDeFlechas), como en Horario semanal y Faltas: el dia, la
        /// semana o el mes anterior (-1) o siguiente (1), segun el modo, y avisa
        /// como un clic (2026-11-03: antes no habia teclado, el calendario no toma el foco).
        /// </summary>
        public void Mover(int paso)
        {
            int lado = Math.Sign(paso);
            if (lado == 0)
            {
                return;
            }

            seleccion = modo == ModoCalendario.Mes ? seleccion.AddMonths(lado)
                : modo == ModoCalendario.Semana ? seleccion.AddDays(7 * lado)
                : seleccion.AddDays(lado);
            mesVisible = new DateTime(seleccion.Year, seleccion.Month, 1);
            Invalidate();
            if (SeleccionCambiada != null)
            {
                SeleccionCambiada(seleccion);
            }
        }

        private void CambiarMes(int meses)
        {
            mesVisible = mesVisible.AddMonths(meses);
            if (modo == ModoCalendario.Mes)
            {
                // En Mes las flechas SON la seleccion.
                seleccion = mesVisible;
                Invalidate();
                if (SeleccionCambiada != null)
                {
                    SeleccionCambiada(seleccion);
                }

                return;
            }

            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (FlechaIzquierda.Contains(e.Location))
            {
                CambiarMes(-1);
                return;
            }

            if (FlechaDerecha.Contains(e.Location))
            {
                CambiarMes(1);
                return;
            }

            DateTime? fecha = FechaEn(e.Location);
            if (!fecha.HasValue || !SePuedeElegir(fecha.Value))
            {
                return;
            }

            seleccion = Ajustar(fecha.Value, modo);
            if (fecha.Value.Month != mesVisible.Month || fecha.Value.Year != mesVisible.Year)
            {
                // Se pulso un dia del mes anterior o siguiente: se navega alli.
                mesVisible = new DateTime(fecha.Value.Year, fecha.Value.Month, 1);
            }

            Invalidate();
            if (SeleccionCambiada != null)
            {
                SeleccionCambiada(seleccion);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            DateTime? fecha = FechaEn(e.Location);
            DateTime? nuevo = fecha.HasValue && SePuedeElegir(fecha.Value) ? fecha : null;
            bool sobreFlecha = FlechaIzquierda.Contains(e.Location) || FlechaDerecha.Contains(e.Location);
            Cursor = nuevo.HasValue || sobreFlecha ? Cursors.Hand : Cursors.Default;
            if (nuevo != bajoRaton)
            {
                bajoRaton = nuevo;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
            if (bajoRaton.HasValue)
            {
                bajoRaton = null;
                Invalidate();
            }
        }

        // --- Pintado ----------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int ancho = ClientSize.Width;

            // Cabecera: flechas y nombre del mes.
            using (SolidBrush fondo = new SolidBrush(Color.FromArgb(240, 248, 240)))
            {
                g.FillRectangle(fondo, 0, 0, ancho, AltoCabecera);
            }

            TextRenderer.DrawText(g, "<", fuenteMes, FlechaIzquierda, Color.DarkGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, ">", fuenteMes, FlechaDerecha, Color.DarkGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Meses[mesVisible.Month] + " " + mesVisible.Year, fuenteMes,
                new Rectangle(30, 0, ancho - 60, AltoCabecera), Color.DarkGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // Iniciales de los dias.
            for (int c = 0; c < 7; c++)
            {
                TextRenderer.DrawText(g, Dias[c], fuenteDias, new Rectangle(c * AnchoCelda, AltoCabecera, AnchoCelda, AltoDias),
                    modo == ModoCalendario.Semana && c == 0 ? Color.DarkGreen : Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            DateTime hoy = DateTime.Today;
            DateTime primero = PrimerDiaVisible;
            using (Pen lineas = new Pen(Color.FromArgb(232, 232, 232)))
            {
                for (int fila = 0; fila < Semanas; fila++)
                {
                    for (int columna = 0; columna < 7; columna++)
                    {
                        DateTime fecha = primero.AddDays(fila * 7 + columna);
                        Rectangle celda = RectanguloCelda(fila, columna);
                        bool elegible = SePuedeElegir(fecha);
                        bool enSeleccion = EstaEnSeleccion(fecha);
                        bool otroMes = fecha.Month != mesVisible.Month;

                        // Sombreado: gris para lo que no se puede elegir, verde para la seleccion.
                        Color fondoCelda = Color.White;
                        if (enSeleccion)
                        {
                            fondoCelda = Color.FromArgb(198, 230, 198);
                        }
                        else if (!elegible)
                        {
                            fondoCelda = Color.FromArgb(238, 238, 238);
                        }
                        else if (bajoRaton.HasValue && bajoRaton.Value == fecha)
                        {
                            fondoCelda = Color.FromArgb(226, 242, 226);
                        }

                        using (SolidBrush pincel = new SolidBrush(fondoCelda))
                        {
                            g.FillRectangle(pincel, celda);
                        }

                        g.DrawRectangle(lineas, celda.X, celda.Y, celda.Width, celda.Height);

                        Color texto = !elegible && !enSeleccion
                            ? Color.FromArgb(170, 170, 170)
                            : otroMes ? Color.FromArgb(150, 150, 150) : Color.Black;
                        TextRenderer.DrawText(g, fecha.Day.ToString(), fecha == hoy ? fuenteNumeroHoy : fuenteNumero, celda, texto,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                        if (fecha == hoy)
                        {
                            using (Pen lapiz = new Pen(Color.FromArgb(200, 120, 0), 2))
                            {
                                g.DrawRectangle(lapiz, celda.X + 2, celda.Y + 2, celda.Width - 4, celda.Height - 4);
                            }
                        }
                    }
                }
            }

            // Borde verde alrededor de toda la seleccion (dia, semana o mes visible).
            using (Pen lapiz = new Pen(Color.DarkGreen, 2))
            {
                for (int fila = 0; fila < Semanas; fila++)
                {
                    for (int columna = 0; columna < 7; columna++)
                    {
                        DateTime fecha = primero.AddDays(fila * 7 + columna);
                        if (!EstaEnSeleccion(fecha))
                        {
                            continue;
                        }

                        Rectangle celda = RectanguloCelda(fila, columna);
                        DateTime izquierda = fecha.AddDays(-1);
                        DateTime derecha = fecha.AddDays(1);
                        DateTime arriba = fecha.AddDays(-7);
                        DateTime abajo = fecha.AddDays(7);
                        if (columna == 0 || !EstaEnSeleccion(izquierda)) g.DrawLine(lapiz, celda.Left + 1, celda.Top, celda.Left + 1, celda.Bottom);
                        if (columna == 6 || !EstaEnSeleccion(derecha)) g.DrawLine(lapiz, celda.Right - 1, celda.Top, celda.Right - 1, celda.Bottom);
                        if (fila == 0 || !EstaEnSeleccion(arriba)) g.DrawLine(lapiz, celda.Left, celda.Top + 1, celda.Right, celda.Top + 1);
                        if (fila == Semanas - 1 || !EstaEnSeleccion(abajo)) g.DrawLine(lapiz, celda.Left, celda.Bottom - 1, celda.Right, celda.Bottom - 1);
                    }
                }
            }
        }
    }

    // =========================================================================
    // Estilo compartido por el formulario y sus dialogos
    // =========================================================================

    internal static class EstiloHorarios
    {
        /// <summary>
        /// "Laboratorio CISCO": el nombre de un laboratorio que no dice que lo es
        /// (en el horario de un profesor, "CISCO" solo no se entiende). Si ya
        /// empieza por "Laboratorio" (o "Lab"), tal cual.
        /// </summary>
        public static string NombreLaboratorio(string nombre)
        {
            nombre = (nombre ?? "").Trim();
            return nombre.Length == 0 || nombre.StartsWith("lab", StringComparison.OrdinalIgnoreCase) ? nombre : "Laboratorio " + nombre;
        }

        public const int HoraApertura = 7;
        public const int HoraCierre = 21;

        // Los colores de la administracion, los del inicio de sesion (2026-10-31):
        // el verde del TESCHI, sus tonos claros al pasar el raton y lineas suaves.
        public static readonly Color VerdeTeschi = Color.FromArgb(0, 112, 64);
        public static readonly Color VerdeSobre = Color.FromArgb(236, 246, 239);
        public static readonly Color VerdePulsado = Color.FromArgb(214, 236, 221);
        public static readonly Color LineaSuave = Color.FromArgb(226, 232, 228);

        /// <summary>
        /// Boton de la administracion: blanco con borde fino verde y, al pasar el
        /// raton, un verde muy claro (antes, borde grueso: todo se veia cargado).
        /// Apagado se ve apagado (BotonesApagados), tambien el principal y el rojo.
        /// </summary>
        public static Button CrearBoton(string texto, Point posicion, Size tamaño)
        {
            Button boton = new Button
            {
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = VerdeTeschi,
                Location = posicion,
                Size = tamaño,
                Text = texto,
                UseVisualStyleBackColor = false
            };
            boton.FlatAppearance.BorderColor = VerdeTeschi;
            boton.FlatAppearance.BorderSize = 1;
            boton.FlatAppearance.MouseOverBackColor = VerdeSobre;
            boton.FlatAppearance.MouseDownBackColor = VerdePulsado;
            BotonesApagados.Seguir(boton);
            return boton;
        }

        public static Label CrearEtiqueta(string texto, Point posicion)
        {
            return new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = VerdeTeschi,
                Location = posicion,
                Text = texto
            };
        }

        public static ComboBox CrearCombo(Point posicion, int ancho)
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = posicion,
                Size = new Size(ancho, 26)
            };
        }

        public static TextBox CrearTexto(Point posicion, int ancho)
        {
            return new TextBox
            {
                Font = new DrawingFont("Century Gothic", 10F),
                Location = posicion,
                Size = new Size(ancho, 26)
            };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr ventana, int mensaje, IntPtr wParam, string lParam);

        /// <summary>
        /// Texto gris dentro de la caja mientras esta vacia (EM_SETCUEBANNER),
        /// tambien con el cursor dentro: la de Clases lo tiene nada mas abrir.
        /// </summary>
        public static void PonerTextoGuia(TextBox caja, string guia)
        {
            const int EM_SETCUEBANNER = 0x1501;
            EventHandler poner = (s, e) => SendMessage(caja.Handle, EM_SETCUEBANNER, new IntPtr(1), guia);
            if (caja.IsHandleCreated)
            {
                poner(caja, EventArgs.Empty);
            }

            caja.HandleCreated += poner;
        }

        /// <summary>
        /// La fila elegida en las listas: verde claro con letra negra, no el azul de
        /// Windows. Las celdas con color propio (el del profesor, la falla) ponen el
        /// suyo en su estilo y lo conservan.
        /// </summary>
        public static readonly Color FilaElegida = Color.FromArgb(198, 230, 198);

        /// <summary>
        /// Las listas del administrador (Clases, Bitacoras, Alertas, Alumnos...): letra de 10 pt (RNF04).
        /// Sus columnas cortas (fechas, horas, numeros) no se cortan: ver ListaDigBit.
        /// </summary>
        public static DataGridView CrearGrid(Point posicion, Size tamaño)
        {
            // Con aire (2026-10-31): renglones de 34, margen dentro de cada celda,
            // solo lineas horizontales suaves y el encabezado en un renglon.
            DataGridView grid = new ListaDigBit
            {
                Location = posicion,
                Size = tamaño,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 40,
                GridColor = LineaSuave,
                Font = new DrawingFont("Century Gothic", 10F),
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                ScrollBars = ScrollBars.Both,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            grid.RowTemplate.Height = 34;
            grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersDefaultCellStyle.Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.DarkGreen;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 248, 240);
            // Con las mejoras de accesibilidad de .NET 4.7.2 el encabezado de la
            // columna de la celda elegida se pinta con el azul de Windows: que no cambie.
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = grid.ColumnHeadersDefaultCellStyle.ForeColor;
            grid.DefaultCellStyle.SelectionBackColor = FilaElegida;
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            return grid;
        }

        /// <summary>
        /// Un poco mas oscuro (0.18 = 18 %). ControlPaint.Dark no sirve para esto:
        /// con valores pequenos devuelve un tono casi del todo oscuro, y un rosa
        /// pastel se volvia rojo intenso al seleccionar la fila.
        /// </summary>
        public static Color Oscurecer(Color color, float cuanto)
        {
            float factor = 1f - Math.Max(0f, Math.Min(1f, cuanto));
            return Color.FromArgb(color.A, (int)(color.R * factor), (int)(color.G * factor), (int)(color.B * factor));
        }

        public static void Pesos(DataGridView grid, params float[] pesos)
        {
            for (int i = 0; i < pesos.Length && i < grid.Columns.Count; i++)
            {
                grid.Columns[i].FillWeight = pesos[i];
            }

            // La lista los recuerda: si los cambia sola (una columna en su minimo), los vuelve a poner.
            ListaDigBit lista = grid as ListaDigBit;
            if (lista != null)
            {
                lista.RecordarPesos();
                // Los que se pidieron, aunque la lista ya los haya movido al ponerlos.
                for (int i = 0; i < pesos.Length && i < grid.Columns.Count; i++)
                {
                    lista.RecordarPeso(grid.Columns[i], pesos[i]);
                }
            }
        }

        public static void SeleccionarFila(DataGridView grid, Func<DataGridViewRow, bool> condicion)
        {
            foreach (DataGridViewRow fila in grid.Rows)
            {
                if (fila.Tag != null && condicion(fila))
                {
                    grid.ClearSelection();
                    fila.Selected = true;
                    // En la primera columna que se ve: una celda oculta no puede ser
                    // la actual (lanza), y en solo consulta la columna Codigo de
                    // Clases, la primera, esta oculta.
                    DataGridViewColumn primera = grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
                    if (primera != null)
                    {
                        grid.CurrentCell = fila.Cells[primera.Index];
                    }

                    return;
                }
            }
        }

        /// <summary>Combo editable con las medias horas del laboratorio (07:00 a 21:30). Admite teclear otra hora.</summary>
        public static ComboBox CrearComboHoras(Point posicion, TimeSpan? valor)
        {
            ComboBox combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = posicion,
                Size = new Size(90, 26),
                MaxDropDownItems = 12
            };
            for (TimeSpan hora = TimeSpan.FromHours(HoraApertura); hora <= TimeSpan.FromHours(HoraCierre) + TimeSpan.FromMinutes(30); hora += TimeSpan.FromMinutes(30))
            {
                combo.Items.Add(HorariosDatos.Hora(hora));
            }

            if (valor.HasValue)
            {
                combo.Text = HorariosDatos.Hora(valor.Value);
            }

            return combo;
        }

        /// <summary>La hora mas tarde que se puede escribir (LeerHora no acepta las 24:00).</summary>
        public static readonly TimeSpan UltimoMinuto = new TimeSpan(23, 59, 0);

        /// <summary>
        /// El fin que se propone para unas horas que empiezan a esa hora: dos horas
        /// despues, sin pasar del cierre; si ya empiezan en el cierre o despues
        /// (horas ampliadas por una clase de la noche), sin pasar de las 23:59
        /// (2026-11-03: antes proponia el cierre, un fin antes del inicio).
        /// </summary>
        public static TimeSpan FinPropuesto(TimeSpan inicio)
        {
            TimeSpan tope = inicio < TimeSpan.FromHours(HoraCierre) ? TimeSpan.FromHours(HoraCierre) : UltimoMinuto;
            TimeSpan fin = inicio + TimeSpan.FromHours(2);
            return fin > tope ? tope : fin;
        }

        public static bool LeerHora(ComboBox combo, out TimeSpan hora)
        {
            string texto = (combo.Text ?? string.Empty).Trim();
            if (TimeSpan.TryParseExact(texto, new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out hora)
                && hora >= TimeSpan.Zero && hora < TimeSpan.FromHours(24))
            {
                return true;
            }

            hora = TimeSpan.Zero;
            return false;
        }

        public static void Rellenar(ComboBox combo, IEnumerable<OpcionCombo> opciones, int? seleccionado)
        {
            combo.Items.Clear();
            foreach (OpcionCombo opcion in opciones)
            {
                combo.Items.Add(opcion);
            }

            if (seleccionado.HasValue)
            {
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (((OpcionCombo)combo.Items[i]).Id == seleccionado.Value)
                    {
                        combo.SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        public static void PrepararDialogo(Form dialogo, string titulo, Size tamaño)
        {
            dialogo.BackColor = Color.White;
            dialogo.ClientSize = tamaño;
            dialogo.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialogo.MaximizeBox = false;
            dialogo.MinimizeBox = false;
            dialogo.ShowInTaskbar = false;
            dialogo.StartPosition = FormStartPosition.CenterParent;
            dialogo.Text = titulo;
        }

        /// <summary>
        /// Una InvalidOperationException que lanzo DigBit (HorariosDatos, Permisos):
        /// ya dice en espanol que pasa y que hacer, y se ensena tal cual. Una de
        /// otra biblioteca (MySql.Data: "Connection must be valid and open", si la
        /// red se cae a media transaccion) es texto tecnico en ingles: va con los
        /// demas fallos, por AvisoDeFalloAdmin (como PestanaCalendario).
        /// </summary>
        public static bool EsAvisoDeDigBit(Exception ex)
        {
            return ex != null && string.Equals(ex.Source, typeof(EstiloHorarios).Assembly.GetName().Name, StringComparison.Ordinal);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr ventana);

        /// <summary>
        /// El dueno de un aviso: la ventana, si ya tiene la suya, se ve y es la
        /// que se puede usar. Si no, ninguno, y el MessageBox toma la ventana
        /// activa, como antes: mientras se construye (pedirle el Handle la crearia
        /// antes de tiempo), minimizada, o deshabilitada porque un dialogo modal
        /// la tapa (Asignar clase > Nueva clase...: el aviso va sobre el dialogo,
        /// no sobre Horarios, que esta detras y sin poder usarse).
        /// </summary>
        public static IWin32Window Dueno(Control ventana)
        {
            if (ventana == null || ventana.IsDisposed || !ventana.IsHandleCreated || !ventana.Visible)
            {
                return null;
            }

            Form forma = ventana as Form ?? ventana.FindForm();
            if (forma != null && forma.WindowState == FormWindowState.Minimized)
            {
                return null;
            }

            return IsWindowEnabled(ventana.Handle) ? ventana : null;
        }

        /// <summary>
        /// Una fecha dd/MM/aaaa y, a su derecha, su dia de la semana ("martes"): al
        /// elegir desde cuando aplica un cambio de horario importa en que dia cae
        /// (el selector lo ensena con el idioma de Windows, que puede no ser el
        /// de DigBit). Los dos se anaden a padre. Poner MinDate antes que MaxDate y Value.
        /// </summary>
        public static DateTimePicker CrearFechaConDia(Control padre, Point posicion)
        {
            DateTimePicker fecha = CrearFecha(posicion);
            Label dia = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(posicion.X + fecha.Width + 10, posicion.Y + 4)
            };
            EventHandler poner = (s, e) => dia.Text = HorariosDatos.NombresDia[((int)fecha.Value.DayOfWeek + 6) % 7 + 1].ToLowerInvariant();
            fecha.ValueChanged += poner;
            poner(fecha, EventArgs.Empty);
            padre.Controls.Add(fecha);
            padre.Controls.Add(dia);
            return fecha;
        }

        /// <summary>
        /// Un selector de fecha que siempre ensena dd/MM/aaaa: con Short ensenaba la
        /// fecha corta de Windows, que en otro equipo puede ser otra (punto 18).
        /// </summary>
        public static DateTimePicker CrearFecha(Point posicion)
        {
            return new DateTimePicker
            {
                Font = new DrawingFont("Century Gothic", 10F),
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy",
                Location = posicion,
                Size = new Size(130, 26)
            };
        }
    }

    /// <summary>
    /// Las listas de EstiloHorarios.CrearGrid. Con la letra de 10 pt, una columna
    /// corta (una fecha como "mie 30/09/2026", una hora, un numero) no se corta
    /// nunca: cada vez que se llena o se desplaza, su ancho minimo pasa a ser lo
    /// que pide su texto mas largo de lo que se ve. Las largas (materia, correo,
    /// detalle) se reparten el resto por su peso y, si no caben, se cortan con
    /// "..." (enteras al pasar el raton).
    ///
    /// Ademas (2026-10-31), nunca se ven rotas: el titulo de cada columna se lee
    /// entero (AnchoDelEncabezado, al agregarla y si cambia su titulo; si no cabe
    /// todo, sale la barra de abajo) y, sin filas, TextoVacio dice por que en
    /// medio de la lista ("Nadie coincide con la búsqueda...") en vez de un
    /// rectangulo blanco que no se sabe si esta vacio o no cargo.
    /// </summary>
    internal class ListaDigBit : DataGridView
    {
        // Lo que pide como mucho una columna corta: una fecha con su dia cabe; un
        // nombre o una materia, no (esas se reparten el ancho por su peso).
        private const int AnchoCorto = 150;
        // Las filas que se miden, desde la primera que se ve: nunca todas, una
        // lista de miles de sesiones se llena igual de rapido.
        private const int FilasMedidas = 60;
        // Lo que el encabezado deja alrededor del texto, ademas de su Padding (los
        // margenes de la celda), y el sitio de la flecha de ordenar. Medidos en las
        // fotos de prueba: con menos, "Estudiantes" o "internet" perdian su ultima letra.
        private const int MargenDelEncabezado = 6;
        private const int FlechaDeOrden = 18;
        private static readonly Color TextoVacioGris = Color.FromArgb(110, 110, 110);
        private static readonly Color TextoVacioRojo = Color.FromArgb(190, 30, 30);
        private bool medidaPendiente;
        // Mientras RepartirSiCabe cambia el modo de las columnas (no vuelve a entrar), y
        // si ya hay una revision pedida tras cambiar de tamano.
        private bool repartiendo;
        private bool repartoPendiente;
        // Cambiaron las filas desde la ultima medida: al medir, tambien se reparte.
        private bool repartirAlMedir;
        // Los pesos de la pantalla (RecordarPesos) y si el usuario esta arrastrando el borde de una columna.
        private readonly Dictionary<DataGridViewColumn, float> pesosDeDiseno = new Dictionary<DataGridViewColumn, float>();
        private bool anchoConRaton;
        private string textoVacio = "";
        private bool textoVacioEsError;
        // El minimo que puso el titulo de cada columna, y el que tenia antes: si nadie
        // lo cambio, al cambiar el titulo puede bajar (una columna enlazada nace con
        // el nombre de su campo, "comentarios_hardware", y despues se llama "Hardware").
        private readonly Dictionary<DataGridViewColumn, KeyValuePair<int, int>> minimosDelTitulo = new Dictionary<DataGridViewColumn, KeyValuePair<int, int>>();
        // Lo mismo de las que acaban de salir, por su nombre (OnColumnRemoved).
        private readonly Dictionary<string, KeyValuePair<int, int>> quitadas = new Dictionary<string, KeyValuePair<int, int>>(StringComparer.Ordinal);

        /// <summary>
        /// Lo que se dice en medio de la lista mientras no tiene filas: por que esta
        /// vacia y que hacer ("Nadie coincide con la búsqueda. Borra la búsqueda o
        /// cambia «Mostrar».", "Todavía no hay..."). Vacio: no se dice nada (por
        /// ejemplo, mientras se lee). Se pone junto con el resumen de la pantalla.
        /// </summary>
        public string TextoVacio
        {
            get { return textoVacio; }
            set
            {
                value = value ?? "";
                if (value != textoVacio)
                {
                    textoVacio = value;
                    Invalidate();
                }
            }
        }

        /// <summary>El TextoVacio es un fallo (no se pudo leer): va en rojo, no en gris.</summary>
        public bool TextoVacioEsError
        {
            get { return textoVacioEsError; }
            set
            {
                if (value != textoVacioEsError)
                {
                    textoVacioEsError = value;
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// TextoVacio de una lista que se tiene como DataGridView (la de PantallaAdmin y
        /// sus pantallas): si no es una ListaDigBit, no hace nada.
        /// </summary>
        public static void PonerTextoVacio(DataGridView lista, string texto, bool esError = false)
        {
            ListaDigBit digbit = lista as ListaDigBit;
            if (digbit != null)
            {
                digbit.TextoVacioEsError = esError;
                digbit.TextoVacio = texto;
            }
        }

        /// <summary>
        /// Lo minimo para que el titulo de esa columna se lea entero: en un renglon,
        /// su texto; si la lista parte los titulos (WrapMode), en dos renglones como
        /// mucho, partido donde quede mas angosto ("Código de / clase", nunca en tres;
        /// "Estudiantes" entera). Con su margen y, si se puede ordenar, la flecha.
        /// 0 si no tiene titulo o aun no esta en una lista.
        /// </summary>
        public static int AnchoDelEncabezado(DataGridViewColumn columna)
        {
            string texto = columna == null || columna.DataGridView == null ? "" : (columna.HeaderText ?? "").Trim();
            if (texto.Length == 0)
            {
                return 0;
            }

            DataGridViewCellStyle estilo = columna.HeaderCell.InheritedStyle;
            DrawingFont fuente = estilo.Font ?? columna.DataGridView.Font;
            const TextFormatFlags Medir = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            string[] palabras = texto.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int ancho;
            if (estilo.WrapMode == DataGridViewTriState.True && palabras.Length > 1)
            {
                ancho = int.MaxValue;
                for (int i = 1; i < palabras.Length; i++)
                {
                    int arriba = TextRenderer.MeasureText(string.Join(" ", palabras, 0, i), fuente, Size.Empty, Medir).Width;
                    int abajo = TextRenderer.MeasureText(string.Join(" ", palabras, i, palabras.Length - i), fuente, Size.Empty, Medir).Width;
                    ancho = Math.Min(ancho, Math.Max(arriba, abajo));
                }
            }
            else
            {
                ancho = TextRenderer.MeasureText(texto, fuente, Size.Empty, Medir).Width;
            }

            return ancho + estilo.Padding.Horizontal + MargenDelEncabezado + (ConFlecha(columna) ? FlechaDeOrden : 0);
        }

        /// <summary>
        /// Si el titulo guarda sitio para la flecha de ordenar. Solo la columna por la
        /// que se ordena la pinta: a las demas no se les guarda (WinForms tampoco
        /// aparta ese sitio cuando no cabe: el titulo usa todo el ancho), y en Docentes
        /// a 1366 las columnas de un digito le quitaban unos 60 px al nombre. Al
        /// ordenar se vuelven a medir (OnSorted). Con orden programatico la lista no
        /// avisa al cambiar la flecha: se le guarda siempre.
        /// </summary>
        private static bool ConFlecha(DataGridViewColumn columna)
        {
            switch (columna.SortMode)
            {
                case DataGridViewColumnSortMode.NotSortable:
                    return false;
                case DataGridViewColumnSortMode.Programmatic:
                    return true;
                default:
                    return columna.DataGridView.SortedColumn == columna;
            }
        }

        /// <summary>Se ordeno por otra columna: la que pinta la flecha le guarda sitio y la de antes lo devuelve.</summary>
        protected override void OnSorted(EventArgs e)
        {
            base.OnSorted(e);
            MedirTitulos();
            RepartirSiCabe();
        }

        protected override void OnColumnAdded(DataGridViewColumnEventArgs e)
        {
            base.OnColumnAdded(e);
            // Al enlazar una tabla, la lista cambia cada columna por una copia (con el
            // mismo nombre y el minimo que le puso su titulo): la copia sigue siendo nuestra.
            KeyValuePair<int, int> antes;
            string nombre = e.Column.Name ?? "";
            if (quitadas.TryGetValue(nombre, out antes))
            {
                quitadas.Remove(nombre);
                if (e.Column.MinimumWidth == antes.Value)
                {
                    minimosDelTitulo[e.Column] = antes;
                }
            }

            PonerMinimoDelTitulo(e.Column);
        }

        /// <summary>
        /// Sale de la lista: si nadie cambio el minimo que le puso su titulo, se
        /// recuerda por su nombre, para la copia que la sustituya (ver OnColumnAdded).
        /// Si no, al enlazar se quedaba el minimo de "comentarios_hardware" aunque la
        /// columna despues se llamara "Hardware".
        /// </summary>
        protected override void OnColumnRemoved(DataGridViewColumnEventArgs e)
        {
            base.OnColumnRemoved(e);
            pesosDeDiseno.Remove(e.Column);
            KeyValuePair<int, int> antes;
            if (minimosDelTitulo.TryGetValue(e.Column, out antes))
            {
                minimosDelTitulo.Remove(e.Column);
                if (e.Column.MinimumWidth == antes.Value)
                {
                    quitadas[e.Column.Name ?? ""] = antes;
                }
            }
        }

        /// <summary>Cambio el titulo de una columna (su celda de encabezado, fila -1).</summary>
        protected override void OnCellValueChanged(DataGridViewCellEventArgs e)
        {
            base.OnCellValueChanged(e);
            if (e.RowIndex == -1 && e.ColumnIndex >= 0 && e.ColumnIndex < Columns.Count)
            {
                PonerMinimoDelTitulo(Columns[e.ColumnIndex]);
            }
        }

        /// <summary>Ya no se ordena por esa columna (o si): su titulo deja (o guarda) el sitio de la flecha.</summary>
        protected override void OnColumnSortModeChanged(DataGridViewColumnEventArgs e)
        {
            base.OnColumnSortModeChanged(e);
            PonerMinimoDelTitulo(e.Column);
        }

        /// <summary>Cambio la letra, el margen o el partir de los titulos: se vuelven a medir.</summary>
        protected override void OnColumnHeadersDefaultCellStyleChanged(EventArgs e)
        {
            base.OnColumnHeadersDefaultCellStyleChanged(e);
            MedirTitulos();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            MedirTitulos();
        }

        private void MedirTitulos()
        {
            foreach (DataGridViewColumn columna in Columns)
            {
                PonerMinimoDelTitulo(columna);
            }
        }

        /// <summary>
        /// El minimo de la columna, para que su titulo se lea entero. Solo crece,
        /// salvo el que puso el propio titulo y nadie cambio despues: ese sigue al
        /// titulo nuevo (sin bajar del que tenia la columna antes).
        /// </summary>
        private void PonerMinimoDelTitulo(DataGridViewColumn columna)
        {
            int pide = AnchoDelEncabezado(columna);
            KeyValuePair<int, int> antes;
            bool siguePuesto = minimosDelTitulo.TryGetValue(columna, out antes) && columna.MinimumWidth == antes.Value;
            int anterior = siguePuesto ? antes.Key : columna.MinimumWidth;
            int nuevo = Math.Max(anterior, pide);
            if (nuevo == columna.MinimumWidth)
            {
                return;
            }

            columna.MinimumWidth = nuevo;
            minimosDelTitulo[columna] = new KeyValuePair<int, int>(anterior, nuevo);
        }

        /// <summary>Sin filas, TextoVacio en medio de lo que queda bajo los titulos.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Rows.Count > 0 || textoVacio.Length == 0)
            {
                return;
            }

            int arriba = ColumnHeadersVisible ? ColumnHeadersHeight : 0;
            Rectangle area = new Rectangle(24, arriba, Math.Max(10, ClientSize.Width - 48), Math.Max(10, ClientSize.Height - arriba));
            TextRenderer.DrawText(e.Graphics, textoVacio, Font, area, textoVacioEsError ? TextoVacioRojo : TextoVacioGris,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        protected override void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e)
        {
            base.OnRowsRemoved(e);
            if (Rows.Count == 0 && textoVacio.Length > 0)
            {
                Invalidate();
            }

            // Sin filas no hay OnRowsAdded que mida: el reparto se revisa igual.
            repartirAlMedir = true;
            PedirMedida();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // Al cambiar el tamano, el reparto de las de relleno puede pasarse unos px
            // (RepartirSiCabe): se revisa una vez, cuando termina de acomodarse.
            if (IsHandleCreated && !repartoPendiente)
            {
                repartoPendiente = true;
                BeginInvoke(new Action(() =>
                {
                    repartoPendiente = false;
                    if (!IsDisposed)
                    {
                        RepartirSiCabe();
                    }
                }));
            }
        }

        protected override void OnRowsAdded(DataGridViewRowsAddedEventArgs e)
        {
            base.OnRowsAdded(e);
            repartirAlMedir = true;
            PedirMedida();
        }

        protected override void OnScroll(ScrollEventArgs e)
        {
            base.OnScroll(e);
            if (e.ScrollOrientation == ScrollOrientation.VerticalScroll)
            {
                PedirMedida();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Llenada antes de tener ventana: se mide ahora.
            if (medidaPendiente)
            {
                medidaPendiente = false;
                PedirMedida();
            }
        }

        /// <summary>Una sola medida por tanda de filas, cuando terminan de agregarse.</summary>
        private void PedirMedida()
        {
            if (medidaPendiente)
            {
                return;
            }

            medidaPendiente = true;
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(MedirColumnasCortas));
            }
        }

        private void MedirColumnasCortas()
        {
            medidaPendiente = false;
            if (IsDisposed)
            {
                return;
            }

            // Repartir, solo si cambiaron las filas o algun minimo (al desplazarse, no).
            bool repartir = repartirAlMedir;
            repartirAlMedir = false;
            if (Rows.Count == 0)
            {
                // Vacia (se quito su barra vertical): que no quede la de abajo por nada.
                if (repartir)
                {
                    RepartirSiCabe();
                }

                return;
            }

            int desde = Math.Max(0, FirstDisplayedScrollingRowIndex);
            int hasta = Math.Min(Rows.Count, desde + FilasMedidas);
            foreach (DataGridViewColumn columna in Columns)
            {
                if (!columna.Visible || columna.InheritedAutoSizeMode != DataGridViewAutoSizeColumnMode.Fill)
                {
                    continue;
                }

                // Con su formato (CellFormatting) y su letra, como al pintarla.
                int pide = 0;
                for (int i = desde; i < hasta && pide <= AnchoCorto; i++)
                {
                    pide = Math.Max(pide, Rows[i].Cells[columna.Index].PreferredSize.Width);
                }

                // Solo crece: al filtrar, las columnas no bailan.
                if (pide <= AnchoCorto && pide > columna.MinimumWidth)
                {
                    columna.MinimumWidth = pide;
                    repartir = true;
                }
            }

            if (repartir)
            {
                RepartirSiCabe();
            }
        }

        /// <summary>
        /// Los pesos de ahora son los de la pantalla (EstiloHorarios.Pesos los guarda
        /// al ponerlos; tambien los que deja el usuario al arrastrar un borde): ver
        /// RepartirSiCabe.
        /// </summary>
        public void RecordarPesos()
        {
            pesosDeDiseno.Clear();
            foreach (DataGridViewColumn columna in Columns)
            {
                pesosDeDiseno[columna] = columna.FillWeight;
            }
        }

        /// <summary>El peso de la pantalla para esa columna (EstiloHorarios.Pesos).</summary>
        public void RecordarPeso(DataGridViewColumn columna, float peso)
        {
            if (columna != null && columna.DataGridView == this)
            {
                pesosDeDiseno[columna] = peso;
            }
        }

        protected override void OnColumnWidthChanged(DataGridViewColumnEventArgs e)
        {
            base.OnColumnWidthChanged(e);
            if (!repartiendo && MouseButtons == MouseButtons.Left)
            {
                anchoConRaton = true;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            // El usuario arrastro el borde de una columna: lo que dejo es lo que vale.
            if (anchoConRaton)
            {
                anchoConRaton = false;
                if (pesosDeDiseno.Count > 0)
                {
                    RecordarPesos();
                }
            }
        }

        /// <summary>
        /// Las columnas de relleno de un DataGridView se reparten mal cuando alguna
        /// queda en su minimo, y con los minimos de los titulos (2026-10-31) pasa a menudo:
        ///  - Al agrandarse, la lista reparte solo lo que crecio, sobre los anchos que
        ///    tenia angosta (al abrirse): las cortas se quedaban anchas y la materia se
        ///    cortaba (Clases a 1366, aunque cabia entera). Tambien cambia sola los pesos.
        ///  - Reparte en una sola pasada: si al dar su minimo a una se le acaba el sitio
        ///    a otra, se pasa unos px y sale la barra de abajo aunque todo cabia
        ///    (Docentes a 1366). Tampoco reparte otra vez al salir o quitarse la barra vertical.
        /// Aqui se reparte bien, desde cero: con los pesos de la pantalla (RecordarPesos;
        /// si no hay, los de la primera vez), la que no llega a su minimo se queda en el
        /// y el resto se reparte entre las demas, hasta que todas caben. A la lista se le
        /// dan como pesos esos anchos: su pasada ya no tiene que recortar nada y cabe
        /// justo. Si ni los minimos caben, reparte ella (y sale la barra de abajo).
        /// Solo con las columnas que heredan el relleno de la lista (las de Estadisticas
        /// llevan el suyo: ahi no se toca). Se llama al llenarse, al cambiar algun
        /// minimo y al cambiar de tamano (nunca a cada paso).
        /// </summary>
        private void RepartirSiCabe()
        {
            if (repartiendo || AutoSizeColumnsMode != DataGridViewAutoSizeColumnsMode.Fill || Columns.Count == 0)
            {
                return;
            }

            List<DataGridViewColumn> relleno = new List<DataGridViewColumn>();
            int fijas = 0;
            foreach (DataGridViewColumn columna in Columns)
            {
                if (!columna.Visible)
                {
                    continue;
                }

                if (columna.InheritedAutoSizeMode != DataGridViewAutoSizeColumnMode.Fill)
                {
                    fijas += columna.Width;
                }
                else if (columna.AutoSizeMode == DataGridViewAutoSizeColumnMode.NotSet)
                {
                    relleno.Add(columna);
                }
                else
                {
                    // Una con su propio relleno: como siempre.
                    return;
                }
            }

            if (relleno.Count == 0)
            {
                return;
            }

            foreach (DataGridViewColumn columna in relleno)
            {
                if (!pesosDeDiseno.ContainsKey(columna))
                {
                    pesosDeDiseno[columna] = columna.FillWeight;
                }
            }

            // 2 px de holgura: si la lista mide un poco distinto, sobra en vez de faltar.
            int disponible = ClientSize.Width - (VerticalScrollBar.Visible ? VerticalScrollBar.Width : 0)
                - (RowHeadersVisible ? RowHeadersWidth : 0) - fijas - 2;
            Dictionary<DataGridViewColumn, float> anchos = AnchosConMinimos(relleno, disponible);
            repartiendo = true;
            try
            {
                // Sin relleno, los pesos se ponen sin que la lista reparta a cada uno; al
                // volver al relleno reparte una sola vez.
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                foreach (DataGridViewColumn columna in relleno)
                {
                    columna.FillWeight = anchos != null ? Math.Max(1f, anchos[columna]) : pesosDeDiseno[columna];
                }

                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            }
            finally
            {
                repartiendo = false;
            }
        }

        /// <summary>
        /// El ancho de cada columna de relleno en ese espacio, por sus pesos de la
        /// pantalla y sin bajar de su minimo; null si ni los minimos caben.
        /// </summary>
        private Dictionary<DataGridViewColumn, float> AnchosConMinimos(List<DataGridViewColumn> relleno, int disponible)
        {
            if (disponible <= 0 || relleno.Sum(c => c.MinimumWidth) > disponible)
            {
                return null;
            }

            HashSet<DataGridViewColumn> enSuMinimo = new HashSet<DataGridViewColumn>();
            Dictionary<DataGridViewColumn, float> anchos = new Dictionary<DataGridViewColumn, float>();
            while (true)
            {
                float queda = disponible - enSuMinimo.Sum(c => c.MinimumWidth);
                float pesos = relleno.Where(c => !enSuMinimo.Contains(c)).Sum(c => Math.Max(0.01f, pesosDeDiseno[c]));
                bool otraVuelta = false;
                foreach (DataGridViewColumn columna in relleno)
                {
                    if (enSuMinimo.Contains(columna))
                    {
                        anchos[columna] = columna.MinimumWidth;
                        continue;
                    }

                    float ancho = pesos <= 0 ? columna.MinimumWidth : queda * Math.Max(0.01f, pesosDeDiseno[columna]) / pesos;
                    if (ancho < columna.MinimumWidth)
                    {
                        enSuMinimo.Add(columna);
                        otraVuelta = true;
                    }

                    anchos[columna] = ancho;
                }

                // Cada vuelta deja al menos una mas en su minimo: termina.
                if (!otraVuelta)
                {
                    return anchos;
                }
            }
        }
    }

    // =========================================================================
    // Selector de semana: ◀ Semana del 05/10 al 10/10/2026 ▶ [Esta semana]
    // =========================================================================

    /// <summary>
    /// Elige una semana (se guarda su lunes). Lo usan la pestana Horario, el
    /// dialogo de excepciones y el Calendario de bitacoras.
    /// </summary>
    internal class SelectorSemana : FlowLayoutPanel
    {
        private readonly Button btnAnterior;
        private readonly Label lblSemana;
        private readonly Button btnSiguiente;
        private readonly Button btnActual;
        private readonly DrawingFont fuenteFlecha = new DrawingFont("Segoe UI Symbol", 9F, FontStyle.Bold);
        private readonly DrawingFont fuenteSemana = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold);
        private readonly DrawingFont fuenteBoton = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private DateTime lunes = CuadriculaSemanal.LunesDe(DateTime.Today);
        private DateTime hoy = DateTime.Today;

        /// <summary>El usuario cambio de semana (con las flechas o "Esta semana"; no al poner Lunes).</summary>
        public event Action Cambio;

        /// <summary>En Cambio: 1 si fue a una semana posterior, -1 si a una anterior (para el Deslizamiento).</summary>
        public int Direccion { get; private set; }

        public SelectorSemana()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            WrapContents = false;
            Margin = new Padding(0);
            Padding = new Padding(0);

            btnAnterior = Boton("◀", fuenteFlecha, 30);
            btnAnterior.Click += (s, e) => Mover(-1);
            lblSemana = new Label
            {
                AutoSize = true,
                Font = fuenteSemana,
                ForeColor = Color.DarkGreen,
                Margin = new Padding(4, 4, 4, 0)
            };
            btnSiguiente = Boton("▶", fuenteFlecha, 30);
            btnSiguiente.Click += (s, e) => Mover(1);
            btnActual = Boton("Esta semana", fuenteBoton, 96);
            btnActual.Margin = new Padding(8, 0, 0, 0);
            btnActual.Click += (s, e) =>
            {
                DateTime actual = SemanaActual(hoy);
                Direccion = actual.CompareTo(lunes);
                Lunes = actual;
                if (Cambio != null)
                {
                    Cambio();
                }
            };

            Controls.Add(btnAnterior);
            Controls.Add(lblSemana);
            Controls.Add(btnSiguiente);
            Controls.Add(btnActual);
            Pintar();
        }

        private static Button Boton(string texto, DrawingFont fuente, int ancho)
        {
            Button boton = EstiloHorarios.CrearBoton(texto, Point.Empty, new Size(ancho, 26));
            boton.Font = fuente;
            boton.FlatAppearance.BorderSize = 1;
            boton.Margin = new Padding(0);
            boton.Padding = new Padding(0);
            return boton;
        }

        /// <summary>El lunes de la semana que se ve (al ponerlo vale cualquier dia de ella).</summary>
        public DateTime Lunes
        {
            get { return lunes; }
            set
            {
                lunes = CuadriculaSemanal.LunesDe(value);
                Pintar();
            }
        }

        /// <summary>Hoy segun el servidor: de el salen "Esta semana" y "(esta semana)".</summary>
        public DateTime Hoy
        {
            get { return hoy; }
            set
            {
                hoy = value.Date;
                Pintar();
            }
        }

        /// <summary>La semana que toca ver: la de hoy, o la siguiente si hoy es domingo (no hay clases).</summary>
        public static DateTime SemanaActual(DateTime hoy)
        {
            return CuadriculaSemanal.LunesDe(hoy.DayOfWeek == DayOfWeek.Sunday ? hoy.AddDays(1) : hoy);
        }

        /// <summary>"Semana del 05/10 al 10/10/2026", de lunes a sabado.</summary>
        public static string Describir(DateTime lunes)
        {
            DateTime sabado = lunes.AddDays(CuadriculaSemanal.Dias - 1);
            return "Semana del " + lunes.ToString("dd/MM") + " al " + sabado.ToString("dd/MM/yyyy");
        }

        /// <summary>Una semana despues (1) o antes (-1), como las flechas (tambien Ctrl+flechas del teclado).</summary>
        public void Mover(int semanas)
        {
            Direccion = Math.Sign(semanas);
            Lunes = lunes.AddDays(7 * semanas);
            if (Cambio != null)
            {
                Cambio();
            }
        }

        private void Pintar()
        {
            DateTime estaSemana = CuadriculaSemanal.LunesDe(hoy);
            lblSemana.Text = Describir(lunes) + (lunes == estaSemana ? " (esta semana)" : lunes == estaSemana.AddDays(7) ? " (la próxima)" : "");
            btnActual.Enabled = lunes != SemanaActual(hoy);
        }

        protected override void Dispose(bool disposing)
        {
            // Primero los botones y la etiqueta, que usan estas fuentes.
            base.Dispose(disposing);
            if (disposing)
            {
                fuenteFlecha.Dispose();
                fuenteSemana.Dispose();
                fuenteBoton.Dispose();
            }
        }
    }

    // =========================================================================
    // Cuadricula semanal: un cuadro por dia y HORA. El administrador selecciona
    // uno o varios cuadros libres (arrastrando o con Ctrl+clic, incluso en varios
    // dias) y les asigna una clase; cada bloque contiguo se convierte en una
    // franja. Clic en una franja: se selecciona y la pestana muestra su menu.
    // =========================================================================

    /// <summary>Bloque contiguo de cuadros libres seleccionados en un dia.</summary>
    internal class RangoHueco
    {
        public int Dia;
        public TimeSpan Inicio;
        public TimeSpan Fin;

        public string Descripcion
        {
            get { return HorariosDatos.NombresDia[Dia] + " " + HorariosDatos.Hora(Inicio) + "-" + HorariosDatos.Hora(Fin); }
        }
    }

    /// <summary>Como se pinta una clase en el Calendario de bitacoras (CuadriculaSemanal.AspectoDeFranja).</summary>
    internal class AspectoEnCalendario
    {
        public Color Fondo = Color.White;
        public Color Texto = Color.Black;
        /// <summary>La linea de estado: "3 bitácoras", "Sin bitácoras", "Por venir"...</summary>
        public string Pie = "";
    }

    internal class CuadriculaSemanal : DataGridView, IUsaFlechas
    {
        // Los dias en que se ponen clases: de lunes a sabado (los dialogos de horas
        // solo ofrecen esos). La escuela da clases de lunes a viernes.
        public const int Dias = 6;
        // La columna del domingo (2026-11-03): oculta, salvo que la semana que se ve
        // (o el horario, sin fechas) tenga algo ese dia (horas de antes o importadas):
        // asi se ven y se pueden cambiar o quitar, en vez de guardarse sin verse y
        // seguir contando en los choques. Sus cuadros no se eligen (no se pone nada
        // nuevo el domingo): sus clases se cambian o se quitan con su menu.
        public const int Domingo = 7;
        public const int AltoHora = 30;
        // El alto minimo de una hora cuando hay mas horas que las del dia (AjustarFilas).
        public const int AltoHoraAmpliada = 22;
        public const int AltoCabecera = 24;
        public const int AnchoHoras = 96;

        public event Action<FranjaInfo> FranjaClic;
        public event Action<FranjaInfo> FranjaDobleClic;
        public event Action<List<RangoHueco>> HuecosSeleccionados;
        public event Action<int, TimeSpan> HuecoDobleClic;
        /// <summary>Clic sobre una sesion extra dibujada en la cuadricula.</summary>
        public event Action<ExcepcionInfo> ExcepcionClic;

        // Excepciones de la semana que se ve (Semana) o, sin semana, de hoy a 6
        // dias vista: se dibujan encima del horario en la columna de su fecha (la
        // del encabezado), tambien las de hoy que ya pasaron.
        private List<ExcepcionInfo> excepciones = new List<ExcepcionInfo>();
        private DateTime ahora = DateTime.Now;
        private DateTime? semana;
        private ExcepcionInfo[,] extraEnCelda;
        private readonly Dictionary<int, ExcepcionInfo> cancelacionPorFranja = new Dictionary<int, ExcepcionInfo>();
        private static readonly Color ColorExtra = Color.FromArgb(255, 140, 0);
        private static readonly Color ColorCancelada = Color.FromArgb(110, 110, 110);

        /// <summary>
        /// En solo lectura no se pueden seleccionar cuadros libres ni asignar
        /// nada, y los dobles clics no hacen nada: la cuadricula sirve solo para
        /// elegir una clase. La usan asi la pantalla de bitacoras del profesor,
        /// las Faltas y Horarios en solo consulta (el encargado de laboratorio).
        /// </summary>
        public bool SoloLectura { get; set; }

        private List<FranjaInfo> franjas = new List<FranjaInfo>();
        private FranjaInfo seleccionada;
        private DateTime hoy = DateTime.Today;

        // Lo que hay bajo el raton: una franja entera (todas sus celdas se
        // sombrean) o un cuadro libre.
        private FranjaInfo franjaBajoRaton;
        private int filaBajoRaton = -1;
        private int columnaBajoRaton = -1;

        // 1 en pantalla. Para el PDF (PdfHorario), las letras y las medidas que
        // deciden como se acomoda cada clase, en la misma proporcion: el dibujo
        // sale igual, pero con mas puntos y se imprime nitido.
        private readonly float escala;

        private readonly DrawingFont fuenteCodigo;
        private readonly DrawingFont fuenteTexto;
        private readonly DrawingFont fuenteHoras;
        // Con la ventana grande las celdas son altas: letra mayor.
        private readonly DrawingFont fuenteCodigoGrande;
        private readonly DrawingFont fuenteTextoGrande;

        private int profesorResaltado;

        /// <summary>
        /// Distinto de 0: las franjas de los demas profesores se pintan desvaidas,
        /// para ver de un vistazo todas las de uno (la leyenda de Horarios).
        /// </summary>
        public int ProfesorResaltado
        {
            get { return profesorResaltado; }
            set
            {
                if (profesorResaltado != value)
                {
                    profesorResaltado = value;
                    Invalidate();
                }
            }
        }

        public List<FranjaInfo> Franjas
        {
            get { return franjas; }
            set
            {
                franjas = value ?? new List<FranjaInfo>();
                bool otrasHoras = AjustarHorasSinFechas();
                Reconstruir();
                if (otrasHoras)
                {
                    AjustarFilas();
                }
            }
        }

        public FranjaInfo Seleccionada
        {
            get { return seleccionada; }
            set { seleccionada = value; Invalidate(); }
        }

        public DateTime Hoy
        {
            get { return hoy; }
            set { hoy = value; Invalidate(); }
        }

        public List<ExcepcionInfo> Excepciones
        {
            get { return excepciones; }
            set
            {
                excepciones = value ?? new List<ExcepcionInfo>();
                // Una sesion extra fuera de la apertura o el cierre tambien amplia las horas (HorasSegunClases).
                bool otrasHoras = AjustarHorasSinFechas();
                Reconstruir();
                if (otrasHoras)
                {
                    AjustarFilas();
                }
            }
        }

        /// <summary>Reloj del servidor: sin Semana, de el salen las fechas de las columnas.</summary>
        public DateTime Ahora
        {
            get { return ahora; }
            set { ahora = value; }
        }

        /// <summary>
        /// La semana que se ve (cualquier dia de ella; se guarda su lunes): cada
        /// columna es esa fecha y salen sus excepciones, tambien las ya pasadas.
        /// Null: cada columna es la proxima fecha de ese dia desde hoy (hoy
        /// incluido). Ponerla antes que Excepciones.
        /// </summary>
        public DateTime? Semana
        {
            get { return semana; }
            set
            {
                semana = value.HasValue ? LunesDe(value.Value) : (DateTime?)null;
                // Otra semana puede tener otras clases (HorasSegunClases): sus horas.
                bool otrasHoras = AjustarHorasSinFechas();
                Reconstruir();
                if (otrasHoras)
                {
                    AjustarFilas();
                }
            }
        }

        /// <summary>El lunes de la semana de esa fecha.</summary>
        public static DateTime LunesDe(DateTime fecha)
        {
            return fecha.Date.AddDays(-(((int)fecha.DayOfWeek + 6) % 7));
        }

        /// <summary>La fecha de la columna de ese dia (1 = lunes): la de la semana que se ve o, sin semana, la proxima desde hoy.</summary>
        public DateTime FechaDeColumna(int dia)
        {
            if (semana.HasValue)
            {
                return semana.Value.AddDays(dia - 1);
            }

            int diaHoy = ((int)ahora.DayOfWeek + 6) % 7 + 1;
            return ahora.Date.AddDays((dia - diaHoy + 7) % 7);
        }

        /// <summary>
        /// Calendario de bitacoras: el fondo, la letra y una linea de estado de cada
        /// franja en la fecha de su columna (null: como siempre, con el color de su
        /// profesor), y de cada sesion extra.
        /// </summary>
        public Func<FranjaInfo, DateTime, AspectoEnCalendario> AspectoDeFranja { get; set; }

        public Func<ExcepcionInfo, AspectoEnCalendario> AspectoDeExtra { get; set; }

        /// <summary>
        /// Solo en Mi horario del profesor (db/19): lo que eligio para cada clase
        /// (por idclases), que da el color del bloque, su circulo y su nombre corto.
        /// Null: como siempre (Horarios del administrador, Faltas, "Horario de X").
        /// Ponerlo antes que Franjas, que rehace las ayudas de los cuadros.
        /// </summary>
        public Func<int, AspectoClase> PersonalDe { get; set; }

        /// <summary>Pulso el circulo de una clase (solo con PersonalDe): su idclases.</summary>
        public event Action<int> CirculoClic;

        private const string AyudaDelCirculo = "Pulsa su círculo para personalizarla: tu imagen, tu color y tu nombre (tus estudiantes ven el círculo al registrarse).";

        // El circulo con el raton encima (vacio: ninguno): lleva el anillo.
        private Rectangle circuloBajoRaton;

        /// <summary>
        /// Sin los codigos de los profesores (con ellos entran los alumnos): para lo
        /// que se ensena o se imprime fuera de Horarios, y en Horarios para quien
        /// no los ve (Permisos.VeCodigosDeProfesor). Ponerlo antes que Franjas,
        /// que es lo que rehace las descripciones y el valor de cada cuadro.
        /// </summary>
        public bool SinCodigos { get; set; }

        private bool sinFechas;

        /// <summary>
        /// Una semana tipo, sin fechas: la de una plantilla de horario
        /// (PestanaProximoCiclo). La cabecera dice solo el dia ("Lunes"), sin
        /// fecha ni "Hoy" y sin resaltar hoy; la ayuda de cada clase no dice
        /// "Vigente del ... al ..."; y cada franja se da como vigente siempre (se
        /// coloca y se pinta con su color, como si fuera del 01/01/2000 al
        /// 31/12/2099), sin mirar sus fechas, Activa ni Semana. Las excepciones y
        /// los codigos generados no salen. Ponerlo antes que Franjas (si se cambia
        /// despues, la cuadricula se rehace sola). Las horas se amplian mas alla de
        /// la apertura y el cierre si alguna franja lo pide (AjustarHorasSinFechas).
        /// </summary>
        public bool SinFechas
        {
            get { return sinFechas; }
            set
            {
                if (sinFechas != value)
                {
                    sinFechas = value;
                    bool otrasHoras = AjustarHorasSinFechas();
                    Reconstruir();
                    if (otrasHoras)
                    {
                        AjustarFilas();
                    }
                }
            }
        }

        /// <summary>
        /// Con fechas (Horario semanal, Faltas, el PDF), las horas tambien se amplian
        /// mas alla de la apertura y el cierre si una clase de la semana que se ve
        /// (o una sesion extra) empieza antes o acaba despues: una de las 06:00 ya no
        /// se dibuja desde las 07:00. Sin ella, de la apertura al cierre. Ponerlo
        /// antes que Franjas. El horario de un profesor no lo usa: pone las horas de
        /// sus clases con MostrarHoras (que tambien pasan de la apertura y el cierre).
        /// </summary>
        public bool HorasSegunClases { get; set; }

        // Las horas las puso quien la usa (MostrarHoras: el horario de un profesor,
        // solo las de sus clases): poner franjas o excepciones no las devuelve al dia de siempre.
        private bool horasDeQuienLaUsa;

        /// <summary>
        /// Sin fechas (una plantilla de horario guardada o en preparacion) o con
        /// HorasSegunClases, las horas van de la apertura al cierre, o mas si alguna
        /// clase que se ve empieza antes o acaba despues (las importadas o las de
        /// pruebas pueden ir de 00:00 a 23:59): si no, se verian desde la apertura
        /// y sus choques no se verian. Las filas se encogen hasta AltoHoraAmpliada
        /// y, si ni asi caben, sale la barra (AjustarFilas). Si no, no cambia nada
        /// (el horario de un profesor las pone con MostrarHoras), salvo volver al
        /// dia de siempre si se habia ampliado. True si cambio las filas.
        /// </summary>
        private bool AjustarHorasSinFechas()
        {
            if (!sinFechas && !HorasSegunClases)
            {
                return !horasDeQuienLaUsa && (horaDesde < EstiloHorarios.HoraApertura || horaHasta > EstiloHorarios.HoraCierre)
                    && CambiarHoras(EstiloHorarios.HoraApertura, EstiloHorarios.HoraCierre);
            }

            int desde = EstiloHorarios.HoraApertura;
            int hasta = EstiloHorarios.HoraCierre;
            List<KeyValuePair<TimeSpan, TimeSpan>> tramos = franjas
                .Where(f => f != null && f.DiaSemana >= 1 && f.DiaSemana <= Domingo && (sinFechas || SeColoca(f)))
                .Select(f => new KeyValuePair<TimeSpan, TimeSpan>(f.Inicio, f.Fin))
                .ToList();
            if (!sinFechas)
            {
                tramos.AddRange(excepciones.Where(x => x != null && x.EsExtra && ExtraSeDibuja(x))
                    .Select(x => new KeyValuePair<TimeSpan, TimeSpan>(x.Inicio, x.Fin)));
            }

            foreach (KeyValuePair<TimeSpan, TimeSpan> tramo in tramos)
            {
                if (tramo.Value <= tramo.Key)
                {
                    continue;
                }

                desde = Math.Min(desde, (int)Math.Floor(tramo.Key.TotalHours));
                hasta = Math.Max(hasta, (int)Math.Ceiling(tramo.Value.TotalHours));
            }

            desde = Math.Max(0, desde);
            hasta = Math.Min(24, Math.Max(hasta, desde + 1));
            return CambiarHoras(desde, hasta);
        }

        /// <summary>La cancelacion pendiente (proxima) de una franja, o null.</summary>
        public ExcepcionInfo CancelacionDe(FranjaInfo franja)
        {
            ExcepcionInfo x;
            return franja != null && cancelacionPorFranja.TryGetValue(franja.Id, out x) ? x : null;
        }

        /// <summary>La sesion extra dibujada en una celda, o null.</summary>
        private ExcepcionInfo ExtraEn(int fila, int columna)
        {
            return extraEnCelda == null || fila < 0 || fila >= extraEnCelda.GetLength(0) || columna < 1 || columna > Domingo ? null : extraEnCelda[fila, columna];
        }

        /// <summary>
        /// Si esa sesion extra sale en la cuadricula: de la semana que se ve (o de
        /// hoy a 6 dias vista); una del domingo, en su columna (que entonces se ve).
        /// Quien recorta las horas a lo que se ve (el horario de un profesor) cuenta con esto.
        /// </summary>
        public bool ExtraSeDibuja(ExcepcionInfo x)
        {
            int dia = ((int)x.Fecha.DayOfWeek + 6) % 7 + 1;
            return x.EsExtra && dia >= 1 && dia <= Domingo && ExcepcionVigente(x);
        }

        /// <summary>
        /// Las que se ven: las de la fecha de alguna columna. Tambien las de hoy que
        /// ya pasaron: si no, lo que el administrador acaba de poner para hoy no
        /// se veia por ningun lado.
        /// </summary>
        private bool ExcepcionVigente(ExcepcionInfo x)
        {
            // Sin fechas (una configuracion de horario) no hay dias que cancelar ni extras.
            if (SinFechas)
            {
                return false;
            }

            DateTime desde = semana ?? ahora.Date;
            return x.Fecha.Date >= desde && x.Fecha.Date <= desde.AddDays(6);
        }

        public CuadriculaSemanal()
            : this(1f)
        {
        }

        /// <param name="escala">Para dibujarla en un mapa de bits (el PDF del horario): 2 la dibuja al doble de puntos, igual que en pantalla.</param>
        internal CuadriculaSemanal(float escala)
        {
            this.escala = escala;
            fuenteCodigo = new DrawingFont("Century Gothic", 8.5F * escala, FontStyle.Bold);
            fuenteTexto = new DrawingFont("Century Gothic", 8F * escala, FontStyle.Regular);
            fuenteHoras = new DrawingFont("Century Gothic", 8F * escala, FontStyle.Regular);
            fuenteCodigoGrande = new DrawingFont("Century Gothic", 10.5F * escala, FontStyle.Bold);
            fuenteTextoGrande = new DrawingFont("Century Gothic", 9.5F * escala, FontStyle.Regular);

            DoubleBuffered = true;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToResizeColumns = false;
            AllowUserToOrderColumns = false;
            ReadOnly = true;
            RowHeadersVisible = false;
            MultiSelect = true;
            SelectionMode = DataGridViewSelectionMode.CellSelect;
            ScrollBars = ScrollBars.None;
            BackgroundColor = Color.White;
            BorderStyle = BorderStyle.None;
            CellBorderStyle = DataGridViewCellBorderStyle.Single;
            GridColor = Color.FromArgb(215, 215, 215);
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ColumnHeadersHeight = Escalado(AltoCabecera);
            ColumnHeadersDefaultCellStyle.Font = new DrawingFont("Century Gothic", 9F * escala, FontStyle.Bold);
            ColumnHeadersDefaultCellStyle.ForeColor = Color.DarkGreen;
            ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 248, 240);
            ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            EnableHeadersVisualStyles = false;
            RowTemplate.Height = Escalado(AltoHora);
            DefaultCellStyle.Font = fuenteTexto;
            DefaultCellStyle.SelectionBackColor = Color.White;
            DefaultCellStyle.SelectionForeColor = Color.Black;
            StandardTab = true;

            Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "",
                Width = Escalado(AnchoHoras),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,
                Frozen = true
            });
            // De lunes a domingo; el domingo, oculto hasta que tenga algo (Reconstruir).
            for (int dia = 1; dia <= Domingo; dia++)
            {
                Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = HorariosDatos.NombresDia[dia],
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                    FillWeight = 100,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    Visible = dia != Domingo
                });
            }

            CrearFilas();

            CellPainting += CuadriculaSemanal_CellPainting;
            CellMouseDoubleClick += CuadriculaSemanal_CellMouseDoubleClick;
        }

        /// <summary>Una medida de pantalla a la escala de la cuadricula (en pantalla, la misma).</summary>
        private int Escalado(int pixeles)
        {
            return (int)Math.Round(pixeles * escala);
        }

        /// <summary>
        /// La cabecera de la columna de hoy con otro fondo (en pantalla, como
        /// siempre). En el PDF no: en una hoja impresa "hoy" no dice nada.
        /// </summary>
        public bool ResaltarHoy { get; set; } = true;

        // Las horas que se ven: de la apertura al cierre, salvo que quien la use
        // las recorte (el horario de un profesor solo ensena las de sus clases).
        private int horaDesde = EstiloHorarios.HoraApertura;
        private int horaHasta = EstiloHorarios.HoraCierre;

        /// <summary>Una fila por hora, de horaDesde a horaHasta.</summary>
        private void CrearFilas()
        {
            for (int i = 0; i < horaHasta - horaDesde; i++)
            {
                int fila = Rows.Add();
                Rows[fila].Height = Escalado(AltoHora);
                // "07:00-08:00" ... "20:00-21:00": se ve que la ultima fila llega a las 21:00.
                // Sin fechas puede llegar a la medianoche: "23:00-24:00", no "23:00-00:00".
                TimeSpan finFila = HoraDeFila(i) + TimeSpan.FromHours(1);
                Rows[fila].Cells[0].Value = HorariosDatos.Hora(HoraDeFila(i)) + "-" + (finFila.TotalHours >= 24 ? "24:00" : HorariosDatos.Hora(finFila));
            }
        }

        /// <summary>Rehace las filas para esas horas (sin recortarlas). True si cambiaron; quien llama vuelve a colocar las franjas.</summary>
        private bool CambiarHoras(int desde, int hasta)
        {
            if (desde == horaDesde && hasta == horaHasta)
            {
                return false;
            }

            horaDesde = desde;
            horaHasta = hasta;
            FijarBajoRaton(null, -1, -1);
            Rows.Clear();
            CrearFilas();
            return true;
        }

        /// <summary>
        /// Solo las horas de desde a hasta (enteras, de 00 a 24): las de las clases
        /// del horario de un profesor. Tambien antes de la apertura o despues del
        /// cierre (2026-11-03: antes se recortaban a 07-21 y una clase de las 21:00
        /// no se veia ni se podia abrir), como HorasSegunClases en Horarios. Se
        /// quedan aunque cambien las franjas. Rehace las filas y vuelve a colocar
        /// franjas y excepciones.
        /// </summary>
        public void MostrarHoras(int desde, int hasta)
        {
            horasDeQuienLaUsa = true;
            desde = Math.Max(0, Math.Min(desde, 23));
            hasta = Math.Min(24, Math.Max(hasta, desde + 1));
            if (!CambiarHoras(desde, hasta))
            {
                return;
            }

            Reconstruir();
            AjustarFilas();
        }

        /// <summary>
        /// Todas las clases son de un mismo profesor (su horario, en varios
        /// laboratorios): cada clase dice su materia, su grupo y en que laboratorio
        /// es (su nombre ya esta en el titulo y su color lo identifica), sin
        /// repetir las horas, que estan a la izquierda (DibujarTextoDeUnProfesor);
        /// la cabecera de hoy dice "Hoy, ...". Ponerlo antes que Franjas.
        /// </summary>
        public bool DeUnProfesor { get; set; }

        /// <summary>La columna del sabado (el horario de un profesor la quita si ese dia no tiene nada).</summary>
        public bool SabadoVisible
        {
            get { return Columns[Dias].Visible; }
            set { Columns[Dias].Visible = value; }
        }

        /// <summary>
        /// La columna del domingo aunque la semana que se ve no tenga nada ese dia:
        /// el horario de un profesor, si su semana de hoy lo usa (asi no cambia de
        /// forma al moverse). Sin esto, se ve solo cuando la semana tiene algo el
        /// domingo (Reconstruir). Ponerlo antes que Franjas.
        /// </summary>
        public bool ConDomingo { get; set; }

        /// <summary>Si la columna del domingo se ve (tiene algo, o ConDomingo).</summary>
        public bool DomingoVisible
        {
            get { return Columns[Domingo].Visible; }
        }

        /// <summary>
        /// Con el foco aqui, ← y → mueven el cuadro elegido (y no cambian de
        /// laboratorio, que soltaba los cuadros elegidos), salvo en solo lectura,
        /// donde no se eligen cuadros. Ctrl+← → siguen siendo de quien la usa (la semana).
        /// </summary>
        public bool UsaFlechas(bool conCtrl)
        {
            return !conCtrl && !SoloLectura;
        }

        // El domingo no se eligen cuadros (ver Domingo): ni con el raton ni con el
        // teclado. Sus clases se eligen con un clic (OnMouseUp) y tienen su menu.
        protected override void SetSelectedCellCore(int columnIndex, int rowIndex, bool selected)
        {
            if (selected && columnIndex == Domingo)
            {
                return;
            }

            base.SetSelectedCellCore(columnIndex, rowIndex, selected);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuenteCodigo.Dispose();
                fuenteTexto.Dispose();
                fuenteHoras.Dispose();
                fuenteCodigoGrande.Dispose();
                fuenteTextoGrande.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override bool ShowFocusCues
        {
            get { return false; }
        }

        /// <summary>Alto total que conviene al control para un alto disponible: las 14 filas del dia entero, iguales, de al menos 30 px.</summary>
        public static int AltoPara(int disponible)
        {
            int filas = EstiloHorarios.HoraCierre - EstiloHorarios.HoraApertura;
            int altoFila = Math.Max(AltoHora, (disponible - AltoCabecera - 8) / filas);
            return AltoCabecera + filas * altoFila + 8;
        }

        // Las filas se estiran para que las 14 horas llenen todo el alto
        // disponible: el horario completo se ve sin desplazarse y, a pantalla
        // completa, cada clase tiene sitio para su texto entero.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AjustarFilas();
        }

        private void AjustarFilas()
        {
            if (Rows.Count == 0)
            {
                return;
            }

            // Se reparte el alto entre las filas dejando un margen abajo para que
            // la ultima (20:00-21:00) se vea entera con su borde. Todas miden lo
            // mismo: el texto de cada clase se reparte asi entre sus cuadros. Si
            // no caben con AltoHora (con mas horas que las del dia, ampliadas por
            // una clase antes de la apertura o despues del cierre, o en un sitio
            // bajo como Faltas a 1366, donde las 14 del dia piden 450 px y hay
            // 431), cada una puede encogerse hasta AltoHoraAmpliada antes de que
            // salga la barra: a 1366x768 caben hasta 19 sin barra.
            int disponible = ClientSize.Height - ColumnHeadersHeight - 6;
            int minimo = Escalado(AltoHoraAmpliada);
            int alto = Math.Max(minimo, disponible / Rows.Count);
            foreach (DataGridViewRow fila in Rows)
            {
                if (fila.Height != alto)
                {
                    fila.Height = alto;
                }
            }

            // Si ni con el alto minimo caben (ventana muy baja), barra para bajar
            // en vez de dejar las ultimas horas cortadas.
            ScrollBars ajuste = alto * Rows.Count > disponible + 6 ? ScrollBars.Vertical : ScrollBars.None;
            if (ScrollBars != ajuste)
            {
                ScrollBars = ajuste;
            }
        }

        private TimeSpan HoraDeFila(int fila)
        {
            return TimeSpan.FromHours(horaDesde + fila);
        }

        private int FilaDeHora(TimeSpan hora)
        {
            return (int)Math.Floor(hora.TotalHours - horaDesde);
        }

        /// <summary>La ultima fila que toca algo que termina a esa hora.</summary>
        private int UltimaFilaHasta(TimeSpan fin)
        {
            return (int)Math.Ceiling(fin.TotalHours - horaDesde) - 1;
        }

        // ---------------------------------------------------------------------
        // Seleccion
        // ---------------------------------------------------------------------

        /// <summary>Bloques contiguos de cuadros libres seleccionados, por dia.</summary>
        public List<RangoHueco> ObtenerHuecosSeleccionados()
        {
            return ObtenerRangosSeleccionados(true);
        }

        /// <summary>
        /// Bloques contiguos de cuadros seleccionados, por dia. Con soloLibres en
        /// false entran tambien los cuadros ocupados por una franja (para aplicar
        /// una excepcion encima del horario).
        /// </summary>
        public List<RangoHueco> ObtenerRangosSeleccionados(bool soloLibres)
        {
            return RangosDe(celda => !soloLibres || celda.Tag == null);
        }

        /// <summary>
        /// Bloques contiguos de cuadros seleccionados, libres u ocupados, sin los
        /// que ya son de esa clase: poner una clase reemplazando (db/25) no se pone
        /// encima de si misma, como antes solo usaba los cuadros libres.
        /// </summary>
        public List<RangoHueco> ObtenerRangosSeleccionadosSin(int idClase)
        {
            return RangosDe(celda => !(celda.Tag is FranjaInfo) || ((FranjaInfo)celda.Tag).ClaseId != idClase);
        }

        /// <summary>Si entre los cuadros seleccionados hay alguno ocupado por una clase.</summary>
        public bool HayOcupadosSeleccionados()
        {
            return SelectedCells.Cast<DataGridViewCell>().Any(c => c.ColumnIndex >= 1 && c.Tag is FranjaInfo);
        }

        private List<RangoHueco> RangosDe(Func<DataGridViewCell, bool> entra)
        {
            List<RangoHueco> rangos = new List<RangoHueco>();
            for (int dia = 1; dia <= Dias; dia++)
            {
                List<int> filas = new List<int>();
                foreach (DataGridViewCell celda in SelectedCells)
                {
                    if (celda.ColumnIndex == dia && entra(celda))
                    {
                        filas.Add(celda.RowIndex);
                    }
                }

                filas.Sort();
                int i = 0;
                while (i < filas.Count)
                {
                    int inicio = filas[i];
                    int fin = inicio;
                    while (i + 1 < filas.Count && filas[i + 1] == fin + 1)
                    {
                        i++;
                        fin = filas[i];
                    }

                    rangos.Add(new RangoHueco { Dia = dia, Inicio = HoraDeFila(inicio), Fin = HoraDeFila(fin) + TimeSpan.FromHours(1) });
                    i++;
                }
            }

            return rangos;
        }

        public void LimpiarSeleccion()
        {
            ClearSelection();
            seleccionada = null;
            Invalidate();
        }

        // Sombreado al pasar el raton: toda la franja si hay una, o el cuadro libre.
        protected override void OnCellMouseEnter(DataGridViewCellEventArgs e)
        {
            base.OnCellMouseEnter(e);
            if (e.RowIndex < 0 || e.ColumnIndex < 1)
            {
                FijarBajoRaton(null, -1, -1);
                return;
            }

            // Una sesion extra sobre una franja cancelada tapa la franja: se sombrea la extra.
            FranjaInfo franja = FranjaVisible(e.RowIndex, e.ColumnIndex);
            FijarBajoRaton(franja, e.RowIndex, e.ColumnIndex);
            Cursor = franja != null || ExtraEn(e.RowIndex, e.ColumnIndex) != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            FijarBajoRaton(null, -1, -1);
            PonerCirculoBajoRaton(Rectangle.Empty);
            Cursor = Cursors.Default;
        }

        // Mi horario (PersonalDe): el anillo del circulo de una clase con el raton encima.
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (PersonalDe == null && circuloBajoRaton.IsEmpty)
            {
                return;
            }

            HitTestInfo golpe = HitTest(e.X, e.Y);
            int idClase;
            Rectangle circulo = Rectangle.Empty;
            bool encima = golpe.Type == DataGridViewHitTestType.Cell
                && CirculoEnCelda(golpe.RowIndex, golpe.ColumnIndex, out idClase, out circulo) && CirculoClase.Contiene(circulo, e.Location);
            PonerCirculoBajoRaton(encima ? circulo : Rectangle.Empty);
        }

        private void PonerCirculoBajoRaton(Rectangle circulo)
        {
            if (circulo == circuloBajoRaton)
            {
                return;
            }

            if (!circuloBajoRaton.IsEmpty)
            {
                Invalidate(CirculoClase.ZonaPulsable(circuloBajoRaton));
            }

            circuloBajoRaton = circulo;
            if (!circulo.IsEmpty)
            {
                Invalidate(CirculoClase.ZonaPulsable(circulo));
            }
        }

        private void FijarBajoRaton(FranjaInfo franja, int fila, int columna)
        {
            bool cambio = franja != franjaBajoRaton || fila != filaBajoRaton || columna != columnaBajoRaton;
            franjaBajoRaton = franja;
            filaBajoRaton = fila;
            columnaBajoRaton = columna;
            if (cambio)
            {
                Invalidate();
            }
        }

        // Clic en una franja: se selecciona entera y no entra en la seleccion de
        // cuadros (no se llama a base). Clic en un cuadro libre: seleccion normal,
        // con arrastre y Ctrl+clic.
        protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
        {
            if (SoloLectura)
            {
                // Sin seleccion de cuadros: el clic solo elige la franja (OnMouseUp).
                return;
            }

            // El arrastre puede empezar en cualquier cuadro, libre u ocupado: la
            // seleccion es la normal del DataGridView. Que fue un clic simple sobre
            // una franja se decide al soltar (OnMouseUp).
            if (e.RowIndex >= 0 && e.ColumnIndex >= 1 && seleccionada != null)
            {
                seleccionada = null;
                if (FranjaClic != null)
                {
                    FranjaClic(null);
                }
            }

            base.OnCellMouseDown(e);
        }

        // Al soltar el raton tras seleccionar cuadros libres, la pestana ofrece
        // asignarles una clase.
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            HitTestInfo golpe = HitTest(e.X, e.Y);
            if (golpe.Type != DataGridViewHitTestType.Cell || golpe.ColumnIndex < 1)
            {
                return;
            }

            // Mi horario (PersonalDe): el circulo de una clase la personaliza; el
            // resto del bloque sigue abriendo su bitacora.
            int idCirculo;
            Rectangle circulo;
            if (CirculoClic != null && CirculoEnCelda(golpe.RowIndex, golpe.ColumnIndex, out idCirculo, out circulo)
                && CirculoClase.Contiene(circulo, e.Location))
            {
                PonerCirculoBajoRaton(Rectangle.Empty);
                CirculoClic(idCirculo);
                return;
            }

            // Clic simple sobre una sesion extra dibujada encima: sus opciones.
            ExcepcionInfo extra = ExtraEn(golpe.RowIndex, golpe.ColumnIndex);
            if (extra != null && SelectedCells.Count <= 1)
            {
                ClearSelection();
                if (ExcepcionClic != null)
                {
                    ExcepcionClic(extra);
                }

                return;
            }

            // Clic simple sobre una franja (una sola celda seleccionada): es la
            // franja, se selecciona entera y sale su menu.
            FranjaInfo franja = Rows[golpe.RowIndex].Cells[golpe.ColumnIndex].Tag as FranjaInfo;
            if (franja != null && (SoloLectura || SelectedCells.Count <= 1))
            {
                ClearSelection();
                Seleccionada = franja;
                if (FranjaClic != null)
                {
                    FranjaClic(franja);
                }

                return;
            }

            if (SoloLectura)
            {
                // Clic fuera de una franja: suelta la que estaba elegida (como al
                // administrador, que la suelta en OnCellMouseDown).
                if (seleccionada != null)
                {
                    Seleccionada = null;
                    if (FranjaClic != null)
                    {
                        FranjaClic(null);
                    }
                }

                return;
            }

            // Varios cuadros (libres u ocupados, por arrastre o Ctrl+clic): la
            // pestana ofrece asignar clase o aplicar una excepcion.
            List<RangoHueco> rangos = ObtenerRangosSeleccionados(false);
            if (rangos.Count > 0 && HuecosSeleccionados != null)
            {
                HuecosSeleccionados(rangos);
            }
        }

        private void CuadriculaSemanal_CellMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            // En solo lectura el doble clic no abre nada (editar o crear una franja escriben).
            if (SoloLectura || e.RowIndex < 0 || e.ColumnIndex < 1)
            {
                return;
            }

            FranjaInfo franja = FranjaVisible(e.RowIndex, e.ColumnIndex);
            if (franja != null)
            {
                if (FranjaDobleClic != null) FranjaDobleClic(franja);
            }
            else if (ExtraEn(e.RowIndex, e.ColumnIndex) == null && e.ColumnIndex != Domingo)
            {
                // En una sesion extra no: su menu ya salio con el primer clic. El
                // domingo tampoco: ahi no se pone nada nuevo (ver Domingo).
                if (HuecoDobleClic != null) HuecoDobleClic(e.ColumnIndex, HoraDeFila(e.RowIndex));
            }
        }

        // ---------------------------------------------------------------------
        // Contenido y pintado
        // ---------------------------------------------------------------------

        private void Reconstruir()
        {
            foreach (DataGridViewRow fila in Rows)
            {
                for (int c = 1; c <= Domingo; c++)
                {
                    fila.Cells[c].Tag = null;
                    fila.Cells[c].Value = null;
                    fila.Cells[c].ToolTipText = "";
                }
            }

            // El domingo se ve si algo de lo que se ve cae en el (o ConDomingo).
            bool algoElDomingo = ConDomingo;

            int ultimaFila = Rows.Count - 1;
            // Las cancelaciones, antes que las franjas: la descripcion de una
            // cancelada no lleva su codigo generado (CodigoGeneradoDe).
            cancelacionPorFranja.Clear();
            foreach (ExcepcionInfo x in excepciones.Where(ExcepcionVigente))
            {
                if (!x.EsExtra && x.FranjaId.HasValue && !cancelacionPorFranja.ContainsKey(x.FranjaId.Value))
                {
                    cancelacionPorFranja[x.FranjaId.Value] = x;
                }
            }

            // Solo las que tocan los dias que se ven (SeColoca): una que ya termino
            // (cambiada o quitada desde una fecha) o que aun no empieza no ocupa
            // cuadros. Primero las que esa semana no se dan (clase desactivada, o
            // cambiada a media semana: en gris): si se cruzan con una que si, gana
            // la que si.
            foreach (FranjaInfo franja in franjas.Where(SeColoca).OrderBy(f => VigenteEnSuColumna(f) ? 1 : 0).ThenBy(f => f.Inicio))
            {
                if (franja.DiaSemana < 1 || franja.DiaSemana > Domingo)
                {
                    continue;
                }

                algoElDomingo |= franja.DiaSemana == Domingo;
                int desde = Math.Max(0, FilaDeHora(franja.Inicio));
                int hasta = Math.Min(ultimaFila, UltimaFilaHasta(franja.Fin));
                if (hasta < desde)
                {
                    continue;
                }

                string generado = CodigoGeneradoDe(franja);
                string descripcion = DescripcionFranja(franja, generado == null && ConCodigo(franja.LaboratorioId), DeUnProfesor, !SinFechas)
                    + (generado == null ? "" : Environment.NewLine + "Código del " + FechaDeColumna(franja.DiaSemana).ToString("dd/MM") + ": " + generado
                        + " (solo tú lo ves; su docente, al entrar a DigBit durante la clase)");
                AspectoEnCalendario aspecto = AspectoDeFranja == null ? null : AspectoDeFranja(franja, FechaDeColumna(franja.DiaSemana));
                if (aspecto != null && aspecto.Pie.Length > 0)
                {
                    descripcion += Environment.NewLine + aspecto.Pie;
                }

                if (PersonalDeClase(franja.ClaseId) != null)
                {
                    descripcion += Environment.NewLine + AyudaDelCirculo;
                }

                // El valor no se pinta, pero lo copian Ctrl+C y lo leen los lectores
                // de pantalla: el codigo que se ve en el cuadro (2026-11-03: antes,
                // siempre el fijo, tambien donde no vale: con generacion de codigos o
                // sin codigos). El generado del dia, el fijo si en ese laboratorio
                // se entra con el, o nada (sin codigos, o quien no los ve).
                string codigoQueSeVe = generado ?? (ConCodigo(franja.LaboratorioId) && !string.IsNullOrEmpty(franja.Codigo) ? EstiloAdmin.Codigo(franja.Codigo) : "");
                for (int f = desde; f <= hasta; f++)
                {
                    DataGridViewCell celda = Rows[f].Cells[franja.DiaSemana];
                    celda.Tag = franja;
                    celda.Value = f == desde ? codigoQueSeVe : "";
                    celda.ToolTipText = descripcion;
                }
            }

            // Las sesiones extra vigentes encima, por celda.
            extraEnCelda = new ExcepcionInfo[Rows.Count, Domingo + 1];
            foreach (ExcepcionInfo x in excepciones.Where(e => e.EsExtra && ExcepcionVigente(e)))
            {
                int dia = ((int)x.Fecha.DayOfWeek + 6) % 7 + 1;
                if (dia < 1 || dia > Domingo)
                {
                    continue;
                }

                algoElDomingo |= dia == Domingo;
                string descripcion = DescripcionExtra(x);
                bool conCirculo = PersonalDeClase(x.ClaseId) != null;
                // Lo que copia Ctrl+C donde se pinta como una clase: su codigo, como en MarcaDeExtra.
                string codigoExtra = DeUnProfesor ? ""
                    : CodigoGeneradoDe(x) ?? (ConCodigo(x.LaboratorioId) && !string.IsNullOrEmpty(x.Codigo) ? EstiloAdmin.Codigo(x.Codigo) : "");
                int desde = Math.Max(0, FilaDeHora(x.Inicio));
                int hasta = Math.Min(ultimaFila, UltimaFilaHasta(x.Fin));
                for (int f = desde; f <= hasta; f++)
                {
                    extraEnCelda[f, dia] = x;
                    // Con su circulo solo donde se pinta como una clase (no sobre una que ese dia se da).
                    FranjaInfo debajo = Rows[f].Cells[dia].Tag as FranjaInfo;
                    bool comoClase = debajo == null || !SeDaEseDia(debajo);
                    Rows[f].Cells[dia].ToolTipText = conCirculo && comoClase
                        ? descripcion + Environment.NewLine + AyudaDelCirculo : descripcion;
                    if (comoClase)
                    {
                        Rows[f].Cells[dia].Value = f == desde ? codigoExtra : "";
                    }
                }
            }

            if (Columns[Domingo].Visible != algoElDomingo)
            {
                Columns[Domingo].Visible = algoElDomingo;
            }

            // Cada columna dice su fecha: sin ella no se sabia de que dia eran las excepciones.
            // En el horario de un profesor, la de hoy lo dice: "Hoy, lunes 12/10".
            for (int dia = 1; dia <= Domingo; dia++)
            {
                if (SinFechas)
                {
                    // Una semana tipo (una configuracion de horario): solo el dia, sin resaltar hoy.
                    Columns[dia].HeaderText = HorariosDatos.NombresDia[dia];
                    Columns[dia].HeaderCell.Style.BackColor = Color.Empty;
                    continue;
                }

                DateTime fecha = FechaDeColumna(dia);
                Columns[dia].HeaderText = DeUnProfesor && fecha == ahora.Date
                    ? "Hoy, " + HorariosDatos.NombresDia[dia].ToLowerInvariant() + " " + fecha.ToString("dd/MM")
                    : HorariosDatos.NombresDia[dia] + " " + fecha.ToString("dd/MM");
                // Hoy, con otro fondo: se ve de un vistazo que ya paso y que viene.
                Columns[dia].HeaderCell.Style.BackColor = ResaltarHoy && fecha == ahora.Date ? Color.FromArgb(200, 230, 200) : Color.Empty;
            }

            ClearSelection();
            Invalidate();
        }

        /// <summary>Lo que no cabe en el cuadro de una sesion extra: quien, que, cuando y por que.</summary>
        private string DescripcionExtra(ExcepcionInfo x)
        {
            AspectoEnCalendario aspecto = AspectoDeExtra == null ? null : AspectoDeExtra(x);
            string generado = CodigoGeneradoDe(x);
            // Con codigo fijo, el de su clase, como en la ayuda de las franjas (DescripcionFranja).
            bool conCodigo = generado == null && ConCodigo(x.LaboratorioId) && !string.IsNullOrEmpty(x.Codigo);
            return x.Profesor + Environment.NewLine
                + x.Materia + " - " + x.Grupo + Environment.NewLine
                + (DeUnProfesor && !string.IsNullOrEmpty(x.Laboratorio) ? "Laboratorio " + x.Laboratorio + Environment.NewLine : "")
                + "Sesión extra el " + HorariosDatos.NombresDia[((int)x.Fecha.DayOfWeek + 6) % 7 + 1] + " " + x.Fecha.ToString("dd/MM/yyyy")
                + ", " + HorariosDatos.Hora(x.Inicio) + "-" + HorariosDatos.Hora(x.Fin)
                + (conCodigo ? ", código " + EstiloAdmin.Codigo(x.Codigo) : "")
                + (generado == null ? "" : Environment.NewLine + "Código: " + generado + " (solo tú lo ves; su docente, al entrar a DigBit durante la clase)")
                + (x.Motivo.Length > 0 ? Environment.NewLine + "Motivo: " + x.Motivo : "")
                + (aspecto != null && aspecto.Pie.Length > 0 ? Environment.NewLine + aspecto.Pie : "");
        }

        /// <summary>Una sesion extra como si fuera una franja de ese dia: para dibujarla igual que una clase.</summary>
        private static FranjaInfo ComoFranja(ExcepcionInfo x)
        {
            return new FranjaInfo
            {
                Id = -x.Id,
                ClaseId = x.ClaseId,
                LaboratorioId = x.LaboratorioId,
                Laboratorio = x.Laboratorio,
                DiaSemana = ((int)x.Fecha.DayOfWeek + 6) % 7 + 1,
                Inicio = x.Inicio,
                Fin = x.Fin,
                Codigo = x.Codigo,
                ProfesorId = x.ProfesorId,
                Profesor = x.Profesor,
                ProfesorCorto = x.ProfesorCorto,
                Grupo = x.Grupo,
                Materia = x.Materia,
                VigenteDesde = x.Fecha,
                VigenteHasta = x.Fecha,
                Activa = true
            };
        }

        /// <summary>
        /// La franja que se ve en una celda, o null: si una sesion extra la tapa
        /// (porque ese dia la franja esta cancelada o no se da), lo que se ve es la extra.
        /// </summary>
        private FranjaInfo FranjaVisible(int fila, int columna)
        {
            FranjaInfo franja = Rows[fila].Cells[columna].Tag as FranjaInfo;
            return franja != null && ExtraEn(fila, columna) != null && !SeDaEseDia(franja) ? null : franja;
        }

        /// <summary>
        /// Si la franja sale en la cuadricula: su vigencia (la de su clase
        /// recortada a la suya, db/20) toca los dias que se ven, de lunes a sabado
        /// de la semana que se ve (hasta el domingo, una del domingo) o, sin
        /// semana, de hoy a 6 dias vista. Una que ya termino o que aun no empieza
        /// no ocupa cuadros ni estorba a "Asignar clase"; una que los toca pero no
        /// se da en la fecha de su columna (cambiada a media semana) sale en gris.
        /// </summary>
        public bool SeColoca(FranjaInfo franja)
        {
            if (SinFechas)
            {
                return true;
            }

            DateTime desde = semana ?? ahora.Date;
            DateTime hasta = desde.AddDays(semana.HasValue ? Math.Max(Dias, Math.Min(franja.DiaSemana, Domingo)) - 1 : 6);
            return franja.VigenteDesde.Date <= hasta && franja.VigenteHasta.Date >= desde;
        }

        /// <summary>Si la franja se da en la fecha de su columna: clase activa y vigente ese dia (con la vigencia de la franja).</summary>
        public bool VigenteEnSuColumna(FranjaInfo franja)
        {
            // Sin fechas (una configuracion de horario): vigente siempre.
            if (SinFechas)
            {
                return true;
            }

            DateTime fecha = FechaDeColumna(franja.DiaSemana);
            return franja.Activa && fecha >= franja.VigenteDesde.Date && fecha <= franja.VigenteHasta.Date;
        }

        /// <summary>Vigente en la fecha de su columna y sin cancelar ese dia: una sesion extra encima seria un choque.</summary>
        private bool SeDaEseDia(FranjaInfo franja)
        {
            return VigenteEnSuColumna(franja) && CancelacionDe(franja) == null;
        }

        /// <summary>El color del profesor en todo DigBit (ver ColoresProfesor).</summary>
        public static Color ColorDeProfesor(int idProfesor)
        {
            return ColoresProfesor.De(idProfesor);
        }

        /// <summary>Lo que el profesor eligio para esa clase (Mi horario, db/19), o null; sin PersonalDe, siempre null.</summary>
        private AspectoClase PersonalDeClase(int idClase)
        {
            return PersonalDe == null ? null : PersonalDe(idClase);
        }

        /// <summary>El color de una clase vigente: el que eligio el profesor (Mi horario) o, como siempre, el suyo.</summary>
        private Color ColorDeClase(int idClase, int idProfesor)
        {
            AspectoClase personal = PersonalDeClase(idClase);
            return personal != null ? personal.Color : ColorDeProfesor(idProfesor);
        }

        /// <summary>
        /// Lo que no cabe en el cuadro: el nombre entero, el laboratorio (en el
        /// horario de un profesor), el codigo (si dice algo) y los dias en que se
        /// da; si la franja tiene vigencia propia (db/20: se cambio o se quito
        /// desde una fecha), desde y hasta cuando es este horario. Sin vigencia
        /// (SinFechas: una configuracion de horario), nada de fechas.
        /// </summary>
        private static string DescripcionFranja(FranjaInfo f, bool conCodigo, bool conLaboratorio, bool conVigencia)
        {
            return f.Profesor + Environment.NewLine
                + f.Materia + " - " + f.Grupo + Environment.NewLine
                + (conLaboratorio && !string.IsNullOrEmpty(f.Laboratorio) ? "Laboratorio " + f.Laboratorio + Environment.NewLine : "")
                + HorariosDatos.NombresDia[f.DiaSemana] + " " + HorariosDatos.Hora(f.Inicio) + "-" + HorariosDatos.Hora(f.Fin)
                + (conCodigo ? ", código " + EstiloAdmin.Codigo(f.Codigo) : "")
                + (conVigencia
                    ? Environment.NewLine + "Vigente del " + f.VigenteDesde.ToString("dd/MM/yyyy") + " al " + f.VigenteHasta.ToString("dd/MM/yyyy") + VigenciaPropia(f)
                    : "");
        }

        /// <summary>
        /// "Este horario desde el lunes 20/10; su clase, del 01/09/2026 al
        /// 12/12/2026": solo si la franja empieza despues o termina antes que su
        /// clase (db/20). Si no, vacio.
        /// </summary>
        private static string VigenciaPropia(FranjaInfo f)
        {
            bool desde = f.FranjaDesde.HasValue && f.FranjaDesde.Value.Date > f.ClaseDesde.Date;
            bool hasta = f.FranjaHasta.HasValue && f.FranjaHasta.Value.Date < f.ClaseHasta.Date;
            if (!desde && !hasta)
            {
                return "";
            }

            string cuando = desde && hasta ? "del " + DiaYFecha(f.FranjaDesde.Value) + " al " + DiaYFecha(f.FranjaHasta.Value)
                : desde ? "desde el " + DiaYFecha(f.FranjaDesde.Value)
                : "hasta el " + DiaYFecha(f.FranjaHasta.Value);
            return Environment.NewLine + "Este horario " + cuando + "; su clase, del " + f.ClaseDesde.ToString("dd/MM/yyyy")
                + " al " + f.ClaseHasta.ToString("dd/MM/yyyy");
        }

        /// <summary>"lunes 20/10".</summary>
        private static string DiaYFecha(DateTime fecha)
        {
            return HorariosDatos.NombresDia[((int)fecha.DayOfWeek + 6) % 7 + 1].ToLowerInvariant() + " " + fecha.ToString("dd/MM");
        }

        /// <summary>Hacia el blanco: 0 lo deja igual, 1 lo vuelve blanco.</summary>
        private static Color Desvaido(Color color, float cuanto)
        {
            return Color.FromArgb(
                color.R + (int)((255 - color.R) * cuanto),
                color.G + (int)((255 - color.G) * cuanto),
                color.B + (int)((255 - color.B) * cuanto));
        }

        private void CuadriculaSemanal_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            // En el mapa de bits del PDF, letra suavizada en gris: ClearType deja
            // bordes de color que se notan al imprimir. Tambien la de las cabeceras,
            // que pinta el DataGridView con este mismo Graphics.
            if (escala != 1f)
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            }

            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (e.ColumnIndex == 0)
            {
                e.PaintBackground(e.ClipBounds, false);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(e.Value), fuenteHoras, e.CellBounds, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                e.Handled = true;
                return;
            }

            DataGridViewCell celda = Rows[e.RowIndex].Cells[e.ColumnIndex];
            FranjaInfo franja = celda.Tag as FranjaInfo;
            ExcepcionInfo extra = ExtraEn(e.RowIndex, e.ColumnIndex);

            // Una sesion extra en un cuadro libre, o sobre una franja que ese dia no
            // se da (cancelada, o de una clase desactivada o fuera de vigencia): se
            // pinta como una clase mas (quien, que, horas), con su marco.
            if (extra != null && (franja == null || !SeDaEseDia(franja)))
            {
                PintarExtraComoClase(e, extra);
                if (celda.Selected)
                {
                    using (Pen lapiz = new Pen(Color.DarkGreen, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    {
                        e.Graphics.DrawRectangle(lapiz, e.CellBounds.X + 2, e.CellBounds.Y + 2, e.CellBounds.Width - 5, e.CellBounds.Height - 5);
                    }
                }

                e.Handled = true;
                return;
            }

            if (franja == null)
            {
                // Cuadro libre: blanco (gris suave bajo el raton), o verde claro con
                // borde punteado si esta seleccionado.
                e.PaintBackground(e.ClipBounds, false);

                if (franjaBajoRaton == null && e.RowIndex == filaBajoRaton && e.ColumnIndex == columnaBajoRaton && !celda.Selected)
                {
                    using (SolidBrush pincel = new SolidBrush(Color.FromArgb(243, 243, 243)))
                    {
                        e.Graphics.FillRectangle(pincel, e.CellBounds);
                    }
                }

                if (celda.Selected)
                {
                    using (SolidBrush pincel = new SolidBrush(Color.FromArgb(222, 240, 222)))
                    {
                        e.Graphics.FillRectangle(pincel, e.CellBounds);
                    }

                    using (Pen lapiz = new Pen(Color.DarkGreen) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    {
                        e.Graphics.DrawRectangle(lapiz, e.CellBounds.X + 1, e.CellBounds.Y + 1, e.CellBounds.Width - 3, e.CellBounds.Height - 3);
                    }
                }

                e.Handled = true;
                return;
            }

            // Vigente en la fecha de su columna (la semana que se ve), no solo hoy.
            DateTime fechaColumna = FechaDeColumna(e.ColumnIndex);
            AspectoEnCalendario aspecto = AspectoDeFranja == null ? null : AspectoDeFranja(franja, fechaColumna);
            bool vigente = VigenteEnSuColumna(franja);
            Color fondo = aspecto != null ? aspecto.Fondo : vigente ? ColorDeClase(franja.ClaseId, franja.ProfesorId) : Color.FromArgb(232, 232, 232);
            Color texto = aspecto != null ? aspecto.Texto : vigente ? Color.Black : Color.Gray;
            if (aspecto == null && profesorResaltado != 0 && franja.ProfesorId != profesorResaltado)
            {
                fondo = Desvaido(fondo, 0.72f);
                texto = Color.FromArgb(150, 150, 150);
            }
            bool bajoRaton = franjaBajoRaton != null && franjaBajoRaton.Id == franja.Id;
            if (bajoRaton)
            {
                // Toda la franja un tono mas oscuro mientras el raton esta encima.
                fondo = ControlPaint.Dark(fondo, 0.08f);
            }

            using (SolidBrush pincel = new SolidBrush(fondo))
            {
                e.Graphics.FillRectangle(pincel, e.CellBounds);
            }

            bool primera = e.RowIndex == 0 || Rows[e.RowIndex - 1].Cells[e.ColumnIndex].Tag != franja;
            bool ultima = e.RowIndex == Rows.Count - 1 || Rows[e.RowIndex + 1].Cells[e.ColumnIndex].Tag != franja;
            bool esSeleccionada = seleccionada != null && seleccionada.Id == franja.Id;
            using (Pen lapiz = new Pen(esSeleccionada ? Color.DarkGreen : ControlPaint.Dark(fondo, 0.15f), esSeleccionada ? 2 : 1))
            {
                Rectangle r = e.CellBounds;
                int d = esSeleccionada ? 1 : 0;
                e.Graphics.DrawLine(lapiz, r.Left + d, r.Top, r.Left + d, r.Bottom);
                e.Graphics.DrawLine(lapiz, r.Right - 1 - d, r.Top, r.Right - 1 - d, r.Bottom);
                if (primera)
                {
                    e.Graphics.DrawLine(lapiz, r.Left, r.Top + d, r.Right, r.Top + d);
                }

                if (ultima)
                {
                    e.Graphics.DrawLine(lapiz, r.Left, r.Bottom - 1 - d, r.Right, r.Bottom - 1 - d);
                }
            }

            // El texto ocupa todo el bloque de la franja (todas sus horas), no solo
            // su primer cuadro: cada cuadro pinta, recortado a si mismo, su trozo
            // del mismo dibujo. Asi una clase de dos horas tiene sitio para el
            // nombre, la materia y el grupo enteros, y el codigo.
            DibujarTextoFranja(e.Graphics, e.CellBounds, BloqueDe(franja, e.RowIndex, e.ColumnIndex, e.CellBounds), franja, texto,
                null, aspecto == null ? null : aspecto.Pie);

            // Franja cancelada en la fecha de su columna: rayado gris y aviso en su
            // ultimo cuadro (en el primero taparia quien da la clase).
            ExcepcionInfo cancelacion = CancelacionDe(franja);
            if (cancelacion != null)
            {
                using (System.Drawing.Drawing2D.HatchBrush rayas = new System.Drawing.Drawing2D.HatchBrush(
                    System.Drawing.Drawing2D.HatchStyle.WideUpwardDiagonal, Color.FromArgb(150, ColorCancelada), Color.FromArgb(60, Color.White)))
                {
                    e.Graphics.FillRectangle(rayas, e.CellBounds);
                }

                if (ultima)
                {
                    Rectangle aviso = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + e.CellBounds.Height / 2 - 1, e.CellBounds.Width - 8, e.CellBounds.Height / 2 - 1);
                    string textoAviso = "CANCELADA " + cancelacion.Fecha.ToString("dd/MM") + (cancelacion.Motivo.Length > 0 ? " - " + cancelacion.Motivo : "");
                    using (SolidBrush fondoAviso = new SolidBrush(Color.FromArgb(235, Color.White)))
                    {
                        e.Graphics.FillRectangle(fondoAviso, aviso);
                    }

                    TextRenderer.DrawText(e.Graphics, textoAviso, e.CellBounds.Height >= Escalado(44) ? fuenteCodigoGrande : fuenteCodigo, aviso, Color.FromArgb(160, 30, 30),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
            }

            // Sesion extra encima de una franja que ese dia SI se da: un choque. Se
            // deja ver la franja debajo, con el rayado naranja encima.
            if (extra != null)
            {
                PintarExtra(e, extra);
            }

            // Cuadro ocupado dentro de una seleccion (para una excepcion encima).
            if (celda.Selected)
            {
                using (Pen lapiz = new Pen(Color.DarkGreen, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    e.Graphics.DrawRectangle(lapiz, e.CellBounds.X + 2, e.CellBounds.Y + 2, e.CellBounds.Width - 5, e.CellBounds.Height - 5);
                }
            }

            e.Handled = true;
        }

        /// <summary>
        /// El rectangulo de toda la franja en esa columna, del primer cuadro al
        /// ultimo. Todas las filas miden lo mismo (AjustarFilas).
        /// </summary>
        private Rectangle BloqueDe(FranjaInfo franja, int fila, int columna, Rectangle celda)
        {
            int primera = fila;
            while (primera > 0 && Rows[primera - 1].Cells[columna].Tag == franja)
            {
                primera--;
            }

            int ultima = fila;
            while (ultima < Rows.Count - 1 && Rows[ultima + 1].Cells[columna].Tag == franja)
            {
                ultima++;
            }

            return new Rectangle(celda.X, celda.Y - (fila - primera) * celda.Height, celda.Width, (ultima - primera + 1) * celda.Height);
        }

        /// <summary>Donde va el texto de un bloque: 5 px a los lados y 3 arriba y abajo.</summary>
        private static Rectangle ZonaDeTexto(Rectangle bloque)
        {
            return new Rectangle(bloque.X + 5, bloque.Y + 3, bloque.Width - 10, bloque.Height - 6);
        }

        /// <summary>
        /// Mi horario: el circulo de la clase, arriba a la izquierda del texto, de
        /// 32 px (o el alto del texto menos 4), si el texto tiene al menos 170 x 28;
        /// si no, vacio. Lo usan el pintado y el raton.
        /// </summary>
        private static Rectangle CirculoDeZona(Rectangle zona)
        {
            return zona.Width >= 170 && zona.Height >= 28 ? CirculoClase.Rect(zona.X, zona.Y, Math.Min(32, zona.Height - 4)) : Rectangle.Empty;
        }

        /// <summary>
        /// Mi horario (PersonalDe): la clase y el circulo del bloque que se ve en esa
        /// celda, como los pinta CuadriculaSemanal_CellPainting: el de la franja, o
        /// el de la sesion extra pintada como una clase. Sobre un choque (una extra
        /// encima de una clase que ese dia se da) es el de la franja, que se sigue
        /// viendo bajo el rayado: si no, se veria y no responderia.
        /// </summary>
        private bool CirculoEnCelda(int fila, int columna, out int idClase, out Rectangle circulo)
        {
            idClase = 0;
            circulo = Rectangle.Empty;
            if (PersonalDe == null || !DeUnProfesor || fila < 0 || fila >= Rows.Count || columna < 1 || columna > Domingo)
            {
                return false;
            }

            Rectangle celda = GetCellDisplayRectangle(columna, fila, false);
            FranjaInfo franja = Rows[fila].Cells[columna].Tag as FranjaInfo;
            ExcepcionInfo extra = ExtraEn(fila, columna);
            Rectangle bloque;
            if (extra != null && (franja == null || !SeDaEseDia(franja)))
            {
                idClase = extra.ClaseId;
                bloque = BloqueDeExtra(extra, fila, columna, celda);
            }
            else if (franja != null)
            {
                idClase = franja.ClaseId;
                bloque = BloqueDe(franja, fila, columna, celda);
            }
            else
            {
                return false;
            }

            if (celda.IsEmpty || PersonalDeClase(idClase) == null)
            {
                return false;
            }

            circulo = CirculoDeZona(ZonaDeTexto(bloque));
            return !circulo.IsEmpty;
        }

        /// <summary>
        /// Primera linea: QUIEN da la clase (el color es suyo), en negrita, y a la
        /// derecha el codigo de la clase. Debajo, la materia con su grupo a la
        /// derecha, la linea de estado (pie, en el Calendario) y la hora si queda
        /// sitio. Se dibuja en el bloque entero y se recorta al cuadro que se esta
        /// pintando. Con marca (una sesion extra: "UNICO7 (extra 09/10)" o "EXTRA
        /// 09/10", MarcaDeExtra), esa va en naranja en el sitio del codigo. En el
        /// horario de un profesor, DibujarTextoDeUnProfesor.
        /// </summary>
        private void DibujarTextoFranja(Graphics g, Rectangle celda, Rectangle bloque, FranjaInfo franja, Color texto, string marca = null, string pie = null,
            Color? colorMarca = null)
        {
            const TextFormatFlags Comun = TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
            const TextFormatFlags UnaLinea = Comun | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            bool grande = celda.Height >= Escalado(44);
            DrawingFont fuenteNombre = grande ? fuenteCodigoGrande : fuenteCodigo;
            DrawingFont fuenteDetalle = grande ? fuenteTextoGrande : fuenteTexto;
            Rectangle zona = ZonaDeTexto(bloque);

            System.Drawing.Drawing2D.GraphicsState estado = g.Save();
            try
            {
                g.IntersectClip(celda);

                // En el horario de un profesor va otro texto: la materia arriba.
                if (DeUnProfesor)
                {
                    DibujarTextoDeUnProfesor(g, zona, franja, texto, marca, pie, fuenteNombre, fuenteDetalle);
                    return;
                }

                string nombre = string.IsNullOrEmpty(franja.ProfesorCorto) ? franja.Profesor : franja.ProfesorCorto;
                int altoNombre = TextRenderer.MeasureText(g, "Ág", fuenteNombre).Height;
                Rectangle arriba = new Rectangle(zona.X, zona.Y, zona.Width, altoNombre);

                // Manda el nombre: el codigo (o la marca) solo si le deja al nombre un
                // ancho util. Con generacion de codigos, el de ese dia.
                string generado = CodigoGeneradoDe(franja);
                string codigo = !string.IsNullOrEmpty(marca) ? marca
                    : generado != null ? generado
                    : ConCodigo(franja.LaboratorioId) && !string.IsNullOrEmpty(franja.Codigo) ? EstiloAdmin.Codigo(franja.Codigo) : "";
                Color colorCodigo = !string.IsNullOrEmpty(marca) ? colorMarca ?? Color.FromArgb(190, 90, 0) : texto;
                int anchoCodigo = codigo.Length == 0 ? 0 : TextRenderer.MeasureText(g, codigo, fuenteNombre).Width;
                bool cabeCodigo = anchoCodigo > 0 && arriba.Width - anchoCodigo >= Escalado(90);

                Rectangle zonaNombre = cabeCodigo ? new Rectangle(arriba.X, arriba.Y, arriba.Width - anchoCodigo - Escalado(8), arriba.Height) : arriba;
                TextRenderer.DrawText(g, nombre, fuenteNombre, zonaNombre, texto, UnaLinea | TextFormatFlags.Left);
                if (cabeCodigo)
                {
                    TextRenderer.DrawText(g, codigo, fuenteNombre, arriba, colorCodigo, UnaLinea | TextFormatFlags.Right);
                }

                // Debajo, por orden y solo en lineas enteras (en una clase de una hora
                // no caben todas): la materia, en las lineas que hagan falta, con el
                // grupo a la derecha como el codigo (asi no se lo come el "..."); el
                // codigo, si no cupo arriba; y la hora.
                const TextFormatFlags Varias = Comun | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                    | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis;
                int altoLinea = TextRenderer.MeasureText(g, "Ág", fuenteDetalle).Height;
                int y = arriba.Bottom + 1;

                int Escribir(string parte, int ancho)
                {
                    int resta = zona.Bottom - y;
                    if (string.IsNullOrEmpty(parte) || altoLinea <= 0 || resta < altoLinea)
                    {
                        return 0;
                    }

                    int necesario = TextRenderer.MeasureText(g, parte, fuenteDetalle, new Size(ancho, int.MaxValue),
                        Comun | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
                    int alto = Math.Min(necesario, resta - resta % altoLinea);
                    TextRenderer.DrawText(g, parte, fuenteDetalle, new Rectangle(zona.X, y, ancho, alto), texto, Varias);
                    return alto;
                }

                string grupo = franja.Grupo ?? "";
                int anchoGrupo = grupo.Length == 0 ? 0 : TextRenderer.MeasureText(g, grupo, fuenteDetalle).Width;
                bool grupoAparte = anchoGrupo > 0 && zona.Width - anchoGrupo - Escalado(8) >= Escalado(90);
                if (grupoAparte && zona.Bottom - y >= altoLinea)
                {
                    TextRenderer.DrawText(g, grupo, fuenteDetalle, new Rectangle(zona.X, y, zona.Width, altoLinea), texto, UnaLinea | TextFormatFlags.Right);
                }

                y += grupoAparte
                    ? Escribir(franja.Materia, zona.Width - anchoGrupo - Escalado(8))
                    : Escribir(grupo.Length == 0 ? franja.Materia : franja.Materia + " - " + grupo, zona.Width);
                y += Escribir(pie, zona.Width);
                y += Escribir(codigo.Length > 0 && !cabeCodigo ? (string.IsNullOrEmpty(marca) ? "Código " + codigo : codigo) : null, zona.Width);
                Escribir(HorariosDatos.Hora(franja.Inicio) + "-" + HorariosDatos.Hora(franja.Fin), zona.Width);
            }
            finally
            {
                g.Restore(estado);
            }
        }

        /// <summary>
        /// El texto de una clase en el horario de un profesor (quien la da ya lo
        /// dicen el titulo y el color; su codigo sale una vez, arriba). Arriba la
        /// materia en negrita: en dos lineas si no cabe en una y el bloque tiene
        /// alto para ellas, el grupo y el laboratorio; si no, en una con "...".
        /// Debajo "Grupo X" y el laboratorio (en dos lineas si hace falta y hay
        /// alto), o los dos en una sola linea si no queda mas: el laboratorio no
        /// se pierde nunca. Despues, si queda sitio, la marca de una sesion extra,
        /// el pie y la hora solo si no es en punto (las filas, de hora en hora, no
        /// lo ensenan). Siempre lineas enteras, en el bloque entero: DibujarTextoFranja
        /// ya lo recorto al cuadro que se esta pintando. En Mi horario (PersonalDe),
        /// el circulo de la clase arriba a la izquierda, lo que empieza a su altura a
        /// su derecha, y el nombre corto en lugar de la materia.
        /// </summary>
        private void DibujarTextoDeUnProfesor(Graphics g, Rectangle zona, FranjaInfo franja, Color texto, string marca, string pie,
            DrawingFont fuenteNombre, DrawingFont fuenteDetalle)
        {
            const TextFormatFlags Comun = TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
            const TextFormatFlags UnaLinea = Comun | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis;
            const TextFormatFlags Varias = Comun | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis;
            int altoNombre = TextRenderer.MeasureText(g, "Ág", fuenteNombre).Height;
            int altoLinea = TextRenderer.MeasureText(g, "Ág", fuenteDetalle).Height;
            if (altoNombre <= 0 || altoLinea <= 0)
            {
                return;
            }

            AspectoClase personal = PersonalDeClase(franja.ClaseId);
            Rectangle circulo = personal == null ? Rectangle.Empty : CirculoDeZona(zona);
            if (!circulo.IsEmpty)
            {
                CirculoClase.Pintar(g, circulo, personal);
                if (circulo == circuloBajoRaton)
                {
                    CirculoClase.PintarAnillo(g, circulo);
                }
            }

            // Donde empieza y cuanto mide una linea que empieza en esa y: a la derecha
            // del circulo si empieza a su altura; si no, toda la zona.
            int XEn(int yLinea)
            {
                return !circulo.IsEmpty && yLinea < circulo.Bottom ? circulo.Right + 6 : zona.X;
            }

            int AnchoEn(int yLinea)
            {
                return !circulo.IsEmpty && yLinea < circulo.Bottom ? zona.Right - XEn(yLinea) : zona.Width;
            }

            // Cuantas lineas pide un texto en ese ancho.
            int LineasDe(string parte, DrawingFont fuente, int alto, int ancho)
            {
                int necesario = TextRenderer.MeasureText(g, parte, fuente, new Size(ancho, int.MaxValue),
                    Comun | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
                return Math.Max(1, (necesario + alto - 1) / alto);
            }

            string materia = personal != null ? personal.Nombre : franja.Materia ?? "";
            string grupo = string.IsNullOrEmpty(franja.Grupo) ? "" : "Grupo " + franja.Grupo;
            string laboratorio = EstiloHorarios.NombreLaboratorio(franja.Laboratorio);
            int detalles = (grupo.Length > 0 ? 1 : 0) + (laboratorio.Length > 0 ? 1 : 0);
            int lineasMateria = LineasDe(materia, fuenteNombre, altoNombre, AnchoEn(zona.Y)) > 1 && zona.Height >= 2 * altoNombre + 1 + detalles * altoLinea ? 2 : 1;
            int y = zona.Y + lineasMateria * altoNombre + 1;
            int quedan = Math.Max(0, zona.Bottom - y) / altoLinea;
            if (quedan == 0 && laboratorio.Length > 0)
            {
                // Filas tan bajas que bajo la materia no cabe ni una linea: el
                // laboratorio va en la suya.
                materia = materia.Length == 0 ? laboratorio : materia + ", " + laboratorio;
            }

            TextRenderer.DrawText(g, materia, fuenteNombre, new Rectangle(XEn(zona.Y), zona.Y, AnchoEn(zona.Y), lineasMateria * altoNombre), texto,
                lineasMateria > 1 ? Varias : UnaLinea);

            // Escribe en y con la letra del detalle, en lineas enteras: las que pida
            // el texto, como mucho maximo y las que quepan. Devuelve el alto que uso.
            int Escribir(string parte, int maximo)
            {
                int lineas = string.IsNullOrEmpty(parte) ? 0
                    : Math.Min(Math.Min(maximo, LineasDe(parte, fuenteDetalle, altoLinea, AnchoEn(y))), Math.Max(0, zona.Bottom - y) / altoLinea);
                if (lineas > 0)
                {
                    TextRenderer.DrawText(g, parte, fuenteDetalle, new Rectangle(XEn(y), y, AnchoEn(y), lineas * altoLinea), texto,
                        lineas > 1 ? Varias : UnaLinea);
                }

                return lineas * altoLinea;
            }

            if (quedan == 1)
            {
                // Una sola linea: el grupo y el laboratorio juntos, con "..." si no caben.
                y += Escribir(grupo.Length == 0 || laboratorio.Length == 0 ? grupo + laboratorio : grupo + ", " + laboratorio, 1);
            }
            else
            {
                y += Escribir(grupo, 1);
                y += Escribir(laboratorio, 2);
            }

            bool enPunto = franja.Inicio.Minutes == 0 && franja.Inicio.Seconds == 0 && franja.Fin.Minutes == 0 && franja.Fin.Seconds == 0;
            y += Escribir(marca, int.MaxValue);
            y += Escribir(pie, int.MaxValue);
            Escribir(enPunto ? null : "De " + HorariosDatos.Hora(franja.Inicio) + " a " + HorariosDatos.Hora(franja.Fin), int.MaxValue);
        }

        /// <summary>
        /// Los laboratorios donde el codigo fijo del profesor no dice nada (se
        /// entra sin codigo, o con codigos generados): ahi sus clases no lo
        /// ensenan. Ponerlo antes que Franjas, que es lo que rehace las descripciones.
        /// </summary>
        public IEnumerable<int> LaboratoriosSinCodigoFijo
        {
            get { return sinCodigoFijo; }
            set
            {
                sinCodigoFijo = new HashSet<int>(value ?? Enumerable.Empty<int>());
                Invalidate();
            }
        }

        private HashSet<int> sinCodigoFijo = new HashSet<int>();

        private bool ConCodigo(int idLaboratorio)
        {
            return !SinCodigos && !sinCodigoFijo.Contains(idLaboratorio);
        }

        /// <summary>
        /// Generacion de codigos, solo en Horarios del administrador: el codigo de
        /// cada clase en la fecha de su columna (el de la sesion si ya se abrio),
        /// donde va el del profesor. Null: no se ensenan (el horario de un
        /// profesor, las Faltas). Ponerlo antes que Semana y Franjas, que son lo
        /// que rehace las descripciones.
        /// </summary>
        public CodigosDeLaSemana CodigosGenerados
        {
            get { return codigosGenerados; }
            set
            {
                codigosGenerados = value;
                Invalidate();
            }
        }

        private CodigosDeLaSemana codigosGenerados;

        /// <summary>El codigo generado de la franja en la fecha de su columna, si se ensenan y ese dia se da; si no, null.</summary>
        private string CodigoGeneradoDe(FranjaInfo franja)
        {
            return codigosGenerados == null || SinCodigos || SinFechas || DeUnProfesor || franja.Id <= 0 || !SeDaEseDia(franja) ? null
                : codigosGenerados.DeFranja(franja.Id, FechaDeColumna(franja.DiaSemana));
        }

        /// <summary>El codigo generado de una sesion extra, si se ensenan; si no, null.</summary>
        private string CodigoGeneradoDe(ExcepcionInfo extra)
        {
            return codigosGenerados == null || SinCodigos || SinFechas || DeUnProfesor || !extra.EsExtra ? null
                : codigosGenerados.DeExtra(extra.Id, extra.Fecha);
        }

        /// <summary>
        /// Sesion extra en un cuadro libre o sobre una franja cancelada ese dia:
        /// como una clase (el color de su profesor, o el del Calendario), con su
        /// texto en todo su bloque, su marca donde va el codigo (MarcaDeExtra: su
        /// codigo y "extra", o "EXTRA dd/MM") y el marco naranja de las extras.
        /// </summary>
        private void PintarExtraComoClase(DataGridViewCellPaintingEventArgs e, ExcepcionInfo extra)
        {
            AspectoEnCalendario aspecto = AspectoDeExtra == null ? null : AspectoDeExtra(extra);
            Color fondo = aspecto != null ? aspecto.Fondo : ColorDeClase(extra.ClaseId, extra.ProfesorId);
            Color texto = aspecto != null ? aspecto.Texto : Color.Black;
            if (aspecto == null && profesorResaltado != 0 && extra.ProfesorId != profesorResaltado)
            {
                fondo = Desvaido(fondo, 0.72f);
                texto = Color.FromArgb(150, 150, 150);
            }

            bool primera = e.RowIndex == 0 || ExtraEn(e.RowIndex - 1, e.ColumnIndex) != extra;
            bool ultima = e.RowIndex == Rows.Count - 1 || ExtraEn(e.RowIndex + 1, e.ColumnIndex) != extra;
            bool bajoRaton = columnaBajoRaton == e.ColumnIndex && filaBajoRaton >= 0 && ExtraEn(filaBajoRaton, columnaBajoRaton) == extra;
            if (bajoRaton)
            {
                fondo = ControlPaint.Dark(fondo, 0.08f);
            }

            using (SolidBrush pincel = new SolidBrush(fondo))
            {
                e.Graphics.FillRectangle(pincel, e.CellBounds);
            }

            using (Pen lapiz = new Pen(aspecto != null ? ControlPaint.Dark(fondo, 0.3f) : ColorExtra, 2))
            {
                Rectangle r = e.CellBounds;
                e.Graphics.DrawLine(lapiz, r.Left + 1, r.Top, r.Left + 1, r.Bottom);
                e.Graphics.DrawLine(lapiz, r.Right - 2, r.Top, r.Right - 2, r.Bottom);
                if (primera)
                {
                    e.Graphics.DrawLine(lapiz, r.Left, r.Top + 1, r.Right, r.Top + 1);
                }

                if (ultima)
                {
                    e.Graphics.DrawLine(lapiz, r.Left, r.Bottom - 2, r.Right, r.Bottom - 2);
                }
            }

            // En Faltas la marca va del color del texto: naranja sobre rojo no se lee.
            Rectangle bloque = BloqueDeExtra(extra, e.RowIndex, e.ColumnIndex, e.CellBounds);
            DibujarTextoFranja(e.Graphics, e.CellBounds, bloque, ComoFranja(extra), texto,
                MarcaDeExtra(e.Graphics, e.CellBounds, bloque, extra), aspecto == null ? null : aspecto.Pie, aspecto == null ? (Color?)null : texto);
        }

        /// <summary>
        /// Lo que dice una sesion extra donde las clases ensenan su codigo. Si quien
        /// mira ve los codigos (SinCodigos, Permisos), su codigo como las demas: el
        /// generado o el de su sesion con generacion de codigos; si no, el de su
        /// clase. Y que es extra: "UNICO7 (extra 24/10)" si cabe junto al nombre
        /// (con la misma regla que DibujarTextoFranja), si no "UNICO7 (extra)" y,
        /// si tampoco, "UNICO7" (el marco y el naranja ya dicen que es extra); con un
        /// codigo tan largo que ni solo cabe, el texto entero, que DibujarTextoFranja
        /// baja a su propia linea, como el de una clase. Sin codigo que ensenar (no
        /// los ve, el horario de un profesor, un laboratorio sin codigos), "EXTRA 24/10".
        /// </summary>
        private string MarcaDeExtra(Graphics g, Rectangle celda, Rectangle bloque, ExcepcionInfo extra)
        {
            string fecha = extra.Fecha.ToString("dd/MM");
            string codigo = DeUnProfesor ? null
                : CodigoGeneradoDe(extra) ?? (ConCodigo(extra.LaboratorioId) && !string.IsNullOrEmpty(extra.Codigo) ? EstiloAdmin.Codigo(extra.Codigo) : null);
            if (codigo == null)
            {
                return "EXTRA " + fecha;
            }

            DrawingFont fuente = celda.Height >= Escalado(44) ? fuenteCodigoGrande : fuenteCodigo;
            int cabe = ZonaDeTexto(bloque).Width - Escalado(90);
            string[] opciones = { codigo + " (extra " + fecha + ")", codigo + " (extra)", codigo };
            foreach (string opcion in opciones)
            {
                if (TextRenderer.MeasureText(g, opcion, fuente).Width <= cabe)
                {
                    return opcion;
                }
            }

            return opciones[0];
        }

        /// <summary>El rectangulo de toda la sesion extra en esa columna (como BloqueDe con las franjas).</summary>
        private Rectangle BloqueDeExtra(ExcepcionInfo extra, int fila, int columna, Rectangle celda)
        {
            int primera = fila;
            while (primera > 0 && ExtraEn(primera - 1, columna) == extra)
            {
                primera--;
            }

            int ultima = fila;
            while (ultima < Rows.Count - 1 && ExtraEn(ultima + 1, columna) == extra)
            {
                ultima++;
            }

            return new Rectangle(celda.X, celda.Y - (fila - primera) * celda.Height, celda.Width, (ultima - primera + 1) * celda.Height);
        }

        /// <summary>
        /// Sesion extra encima de una franja que NO esta cancelada ese dia (un
        /// choque): rayado naranja con borde grueso, y en su primer cuadro un
        /// rotulo blanco con "EXTRA dd/MM", horas, codigo y materia.
        /// </summary>
        private void PintarExtra(DataGridViewCellPaintingEventArgs e, ExcepcionInfo extra)
        {
            using (System.Drawing.Drawing2D.HatchBrush rayas = new System.Drawing.Drawing2D.HatchBrush(
                System.Drawing.Drawing2D.HatchStyle.WideUpwardDiagonal, Color.FromArgb(170, ColorExtra), Color.FromArgb(90, 255, 230, 190)))
            {
                e.Graphics.FillRectangle(rayas, e.CellBounds);
            }

            bool primera = e.RowIndex == 0 || ExtraEn(e.RowIndex - 1, e.ColumnIndex) != extra;
            bool ultima = e.RowIndex == Rows.Count - 1 || ExtraEn(e.RowIndex + 1, e.ColumnIndex) != extra;
            bool bajoRaton = e.RowIndex == filaBajoRaton && e.ColumnIndex == columnaBajoRaton;
            using (Pen lapiz = new Pen(bajoRaton ? Color.FromArgb(200, 90, 0) : ColorExtra, 2))
            {
                Rectangle r = e.CellBounds;
                e.Graphics.DrawLine(lapiz, r.Left + 1, r.Top, r.Left + 1, r.Bottom);
                e.Graphics.DrawLine(lapiz, r.Right - 2, r.Top, r.Right - 2, r.Bottom);
                if (primera)
                {
                    e.Graphics.DrawLine(lapiz, r.Left, r.Top + 1, r.Right, r.Top + 1);
                }

                if (ultima)
                {
                    e.Graphics.DrawLine(lapiz, r.Left, r.Bottom - 2, r.Right, r.Bottom - 2);
                }
            }

            if (primera)
            {
                bool grande = e.CellBounds.Height >= Escalado(44);
                Rectangle rotulo = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + 3, e.CellBounds.Width - 8, e.CellBounds.Height - 6);
                using (SolidBrush fondo = new SolidBrush(Color.FromArgb(235, Color.White)))
                {
                    e.Graphics.FillRectangle(fondo, rotulo);
                }

                Rectangle arriba = new Rectangle(rotulo.X + 3, rotulo.Y, rotulo.Width - 6, rotulo.Height / 2);
                Rectangle abajo = new Rectangle(rotulo.X + 3, rotulo.Y + rotulo.Height / 2, rotulo.Width - 6, rotulo.Height / 2);
                const TextFormatFlags unaLinea = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(e.Graphics, "EXTRA " + extra.Fecha.ToString("dd/MM") + "  " + HorariosDatos.Hora(extra.Inicio) + "-" + HorariosDatos.Hora(extra.Fin),
                    grande ? fuenteCodigoGrande : fuenteCodigo, arriba, Color.FromArgb(190, 90, 0), unaLinea);
                string generado = CodigoGeneradoDe(extra);
                TextRenderer.DrawText(e.Graphics, (DeUnProfesor ? extra.Laboratorio + ", " : generado != null ? generado + " "
                        : ConCodigo(extra.LaboratorioId) ? EstiloAdmin.Codigo(extra.Codigo) + " " : "")
                    + extra.Materia + " - " + extra.Grupo
                    + (extra.Motivo.Length > 0 ? " (" + extra.Motivo + ")" : ""),
                    grande ? fuenteTextoGrande : fuenteTexto, abajo, Color.Black, unaLinea);
            }
        }
    }

    // =========================================================================
    // PDF del horario de un laboratorio (Horarios, "Imprimir PDF")
    // =========================================================================

    /// <summary>
    /// La semana de un laboratorio en una hoja A4 apaisada: el encabezado
    /// institucional (los logos del Estado de Mexico y del TESCHI y los tres
    /// renglones de la escuela, como en los PDF de bitacoras), el laboratorio y
    /// la semana, de quien es cada color y la cuadricula como se ve en
    /// Horarios, dibujada con CuadriculaSemanal sin los codigos de los
    /// profesores (con ellos entran los alumnos, y la hoja se ensena o se pega
    /// en la puerta del laboratorio) ni lo que esa semana no se da (en
    /// pantalla, en gris).
    /// </summary>
    internal static class PdfHorario
    {
        // La cuadricula se dibuja al doble de puntos que en pantalla: impresa sale nitida.
        private const float Escala = 2f;
        // Cada hora, al menos asi de alta en pantalla: sale con la letra grande de
        // la cuadricula (CuadriculaSemanal la usa desde 44).
        private const int AltoHoraMinimo = 46;
        // Lo que se guarda abajo de la hoja para el pie.
        private const float AltoPie = 18f;

        private static readonly iText.BaseColor Verde = new iText.BaseColor(0, 100, 0);
        private static readonly iText.BaseColor Gris = new iText.BaseColor(110, 110, 110);

        /// <param name="franjas">Las del laboratorio (las de la cuadricula de Horarios): solo salen las que se dan esa semana (no las grises).</param>
        /// <param name="excepciones">Las cancelaciones y sesiones extra de esa semana en el laboratorio.</param>
        /// <param name="ahora">Hora del servidor: la del pie ("Generado con DigBit el ...").</param>
        public static byte[] Crear(string laboratorio, DateTime lunes, List<FranjaInfo> franjas, List<ExcepcionInfo> excepciones, DateTime ahora)
        {
            franjas = franjas ?? new List<FranjaInfo>();
            excepciones = excepciones ?? new List<ExcepcionInfo>();
            using (MemoryStream salida = new MemoryStream())
            using (CuadriculaSemanal cuadricula = new CuadriculaSemanal(Escala) { SoloLectura = true, SinCodigos = true, ResaltarHoy = false, HorasSegunClases = true })
            {
                cuadricula.Ahora = ahora;
                cuadricula.Hoy = ahora.Date;
                cuadricula.Semana = lunes;
                // Solo lo que esa semana se da, en la fecha de su columna (el domingo,
                // como en pantalla, solo si tiene algo). En pantalla, una clase
                // desactivada o una franja cambiada a media semana sale en gris (se
                // puede elegir y editar); en la hoja que se pega en la puerta seria una
                // clase que no hay. Los profesores y el pie salen de esta misma lista.
                franjas = franjas.Where(f => f.DiaSemana >= 1 && f.DiaSemana <= CuadriculaSemanal.Domingo && cuadricula.VigenteEnSuColumna(f)).ToList();
                cuadricula.Franjas = franjas;
                cuadricula.Excepciones = excepciones;

                iText.Document documento = new iText.Document(iText.PageSize.A4.Rotate(), 28f, 28f, 20f, 20f);
                iTextPdf.PdfWriter escritor = iTextPdf.PdfWriter.GetInstance(documento, salida);
                documento.Open();
                documento.AddTitle("Horario " + laboratorio);
                documento.AddAuthor("DigBit");

                documento.Add(Encabezado());
                documento.Add(new iText.Paragraph("Horario del " + EstiloHorarios.NombreLaboratorio(laboratorio), Letra(iText.FontFactory.HELVETICA_BOLD, 14f, Verde))
                {
                    Alignment = iText.Element.ALIGN_CENTER,
                    SpacingBefore = 2f
                });
                documento.Add(new iText.Paragraph(SelectorSemana.Describir(lunes), Letra(iText.FontFactory.HELVETICA, 10.5f, iText.BaseColor.BLACK))
                {
                    Alignment = iText.Element.ALIGN_CENTER,
                    SpacingAfter = 4f
                });
                documento.Add(Profesores(cuadricula, franjas, excepciones));

                // La cuadricula, en todo lo que queda de la hoja encima del pie (con la
                // proporcion de ese espacio: ScaleToFit no la deforma ni deja huecos).
                float ancho = documento.PageSize.Width - documento.LeftMargin - documento.RightMargin;
                float alto = Math.Max(150f, escritor.GetVerticalPosition(false) - documento.BottomMargin - AltoPie - 8f);
                using (Bitmap dibujo = Dibujar(cuadricula, ancho, alto))
                {
                    iText.Image imagen = iText.Image.GetInstance(dibujo, System.Drawing.Imaging.ImageFormat.Png);
                    imagen.ScaleToFit(ancho, alto);
                    imagen.Alignment = iText.Element.ALIGN_CENTER;
                    documento.Add(imagen);
                }

                iTextPdf.ColumnText.ShowTextAligned(escritor.DirectContent, iText.Element.ALIGN_LEFT,
                    new iText.Phrase(Pie(cuadricula, franjas, excepciones, ahora), Letra(iText.FontFactory.HELVETICA, 8f, Gris)),
                    documento.LeftMargin, documento.BottomMargin + 4f, 0f);
                documento.Close();
                return salida.ToArray();
            }
        }

        /// <summary>
        /// La de una plantilla de Proximo ciclo (2026-11-03, "Imprimir PDF" de la
        /// preparacion y de las guardadas y aplicadas): la semana tipo, sin fechas
        /// (la cabecera dice solo el dia) ni codigos, con lo que es en lugar de la
        /// semana ("Próximo ciclo «X», en preparación: semana tipo, sin fechas").
        /// </summary>
        /// <param name="queEs">El renglon de debajo del titulo: que plantilla es.</param>
        public static byte[] CrearDePlantilla(string laboratorio, string queEs, List<FranjaInfo> franjas, DateTime ahora)
        {
            franjas = franjas ?? new List<FranjaInfo>();
            List<ExcepcionInfo> sinExcepciones = new List<ExcepcionInfo>();
            using (MemoryStream salida = new MemoryStream())
            using (CuadriculaSemanal cuadricula = new CuadriculaSemanal(Escala) { SoloLectura = true, SinCodigos = true, ResaltarHoy = false, SinFechas = true })
            {
                cuadricula.Ahora = ahora;
                cuadricula.Hoy = ahora.Date;
                franjas = franjas.Where(f => f.DiaSemana >= 1 && f.DiaSemana <= CuadriculaSemanal.Domingo).ToList();
                cuadricula.Franjas = franjas;

                iText.Document documento = new iText.Document(iText.PageSize.A4.Rotate(), 28f, 28f, 20f, 20f);
                iTextPdf.PdfWriter escritor = iTextPdf.PdfWriter.GetInstance(documento, salida);
                documento.Open();
                documento.AddTitle("Horario " + laboratorio);
                documento.AddAuthor("DigBit");

                documento.Add(Encabezado());
                documento.Add(new iText.Paragraph("Horario del " + EstiloHorarios.NombreLaboratorio(laboratorio), Letra(iText.FontFactory.HELVETICA_BOLD, 14f, Verde))
                {
                    Alignment = iText.Element.ALIGN_CENTER,
                    SpacingBefore = 2f
                });
                documento.Add(new iText.Paragraph(queEs ?? "", Letra(iText.FontFactory.HELVETICA, 10.5f, iText.BaseColor.BLACK))
                {
                    Alignment = iText.Element.ALIGN_CENTER,
                    SpacingAfter = 4f
                });
                documento.Add(franjas.Count == 0
                    ? new iText.Paragraph("Esta plantilla no tiene clases en este laboratorio.", Letra(iText.FontFactory.HELVETICA, 9f, iText.BaseColor.BLACK))
                    {
                        Alignment = iText.Element.ALIGN_CENTER,
                        SpacingAfter = 4f
                    }
                    : Profesores(cuadricula, franjas, sinExcepciones));

                float ancho = documento.PageSize.Width - documento.LeftMargin - documento.RightMargin;
                float alto = Math.Max(150f, escritor.GetVerticalPosition(false) - documento.BottomMargin - AltoPie - 8f);
                using (Bitmap dibujo = Dibujar(cuadricula, ancho, alto))
                {
                    iText.Image imagen = iText.Image.GetInstance(dibujo, System.Drawing.Imaging.ImageFormat.Png);
                    imagen.ScaleToFit(ancho, alto);
                    imagen.Alignment = iText.Element.ALIGN_CENTER;
                    documento.Add(imagen);
                }

                iTextPdf.ColumnText.ShowTextAligned(escritor.DirectContent, iText.Element.ALIGN_LEFT,
                    new iText.Phrase(Pie(cuadricula, franjas, sinExcepciones, ahora), Letra(iText.FontFactory.HELVETICA, 8f, Gris)),
                    documento.LeftMargin, documento.BottomMargin + 4f, 0f);
                documento.Close();
                return salida.ToArray();
            }
        }

        /// <summary>Los logos a los lados y los tres renglones de la escuela en medio (los de la plantilla de bitacoras).</summary>
        private static iTextPdf.PdfPTable Encabezado()
        {
            iTextPdf.PdfPTable tabla = new iTextPdf.PdfPTable(3) { WidthPercentage = 100f };
            tabla.SetWidths(new float[] { 20f, 60f, 20f });
            tabla.AddCell(CeldaLogo(Properties.Resources.estadoMexico, iText.Element.ALIGN_LEFT));
            iTextPdf.PdfPCell escuela = new iTextPdf.PdfPCell(new iText.Phrase(
                "TECNOLÓGICO DE ESTUDIOS SUPERIORES DE CHIMALHUACÁN\nORGANISMO PÚBLICO DESCENTRALIZADO DEL ESTADO DE MÉXICO\n"
                + "DIVISIÓN ACADÉMICA DE SISTEMAS COMPUTACIONALES", Letra(iText.FontFactory.HELVETICA, 9.5f, iText.BaseColor.BLACK)))
            {
                Border = iText.Rectangle.NO_BORDER,
                HorizontalAlignment = iText.Element.ALIGN_CENTER,
                VerticalAlignment = iText.Element.ALIGN_MIDDLE
            };
            escuela.SetLeading(0f, 1.3f);
            tabla.AddCell(escuela);
            tabla.AddCell(CeldaLogo(Properties.Resources.logoteschi, iText.Element.ALIGN_RIGHT));
            return tabla;
        }

        private static iTextPdf.PdfPCell CeldaLogo(Bitmap recurso, int alineacion)
        {
            // GetInstance copia la imagen: el Bitmap del recurso (uno nuevo en cada lectura) se libera ya.
            using (recurso)
            {
                iText.Image logo = iText.Image.GetInstance(recurso, System.Drawing.Imaging.ImageFormat.Png);
                logo.ScaleToFit(110f, 50f);
                return new iTextPdf.PdfPCell(logo, false)
                {
                    Border = iText.Rectangle.NO_BORDER,
                    HorizontalAlignment = alineacion,
                    VerticalAlignment = iText.Element.ALIGN_MIDDLE
                };
            }
        }

        /// <summary>
        /// De quien es cada color: los profesores de lo que sale esa semana (las
        /// franjas que se dan en la fecha de su columna y las sesiones extra), con
        /// su nombre completo; en la cuadricula sale el corto.
        /// </summary>
        private static iText.Paragraph Profesores(CuadriculaSemanal cuadricula, List<FranjaInfo> franjas, List<ExcepcionInfo> excepciones)
        {
            var profesores = franjas
                .Where(f => f.DiaSemana >= 1 && f.DiaSemana <= CuadriculaSemanal.Domingo && cuadricula.VigenteEnSuColumna(f))
                .Select(f => new { Id = f.ProfesorId, Nombre = f.Profesor ?? "" })
                .Concat(excepciones.Where(cuadricula.ExtraSeDibuja).Select(x => new { Id = x.ProfesorId, Nombre = x.Profesor ?? "" }))
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .OrderBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            iText.Font letra = Letra(iText.FontFactory.HELVETICA, 9f, iText.BaseColor.BLACK);
            iText.Paragraph parrafo = new iText.Paragraph { Alignment = iText.Element.ALIGN_CENTER, Leading = 15f, SpacingAfter = 4f };
            if (profesores.Count == 0)
            {
                parrafo.Add(new iText.Chunk("Esta semana no hay clases en este laboratorio.", letra));
                return parrafo;
            }

            foreach (var profesor in profesores)
            {
                // Un cuadrito de su color (espacios con fondo) pegado a su nombre.
                Color color = ColoresProfesor.De(profesor.Id);
                iText.Chunk muestra = new iText.Chunk("    ", letra);
                muestra.SetBackground(new iText.BaseColor(color.R, color.G, color.B), 0f, 1.5f, 0f, 1.5f);
                parrafo.Add(muestra);
                parrafo.Add(new iText.Chunk(" " + profesor.Nombre + "      ", letra));
            }

            return parrafo;
        }

        /// <summary>Lo que quiere decir el rayado y el marco (si esa semana los hay) y cuando se genero.</summary>
        private static string Pie(CuadriculaSemanal cuadricula, List<FranjaInfo> franjas, List<ExcepcionInfo> excepciones, DateTime ahora)
        {
            List<string> partes = new List<string>();
            if (franjas.Any(f => cuadricula.SeColoca(f) && cuadricula.CancelacionDe(f) != null))
            {
                partes.Add("Rayado gris: clase cancelada ese día.");
            }

            if (excepciones.Any(cuadricula.ExtraSeDibuja))
            {
                partes.Add("Marco naranja: sesión extra.");
            }

            partes.Add("Generado con DigBit el " + ahora.ToString("dd/MM/yyyy") + " a las " + ahora.ToString("HH:mm") + ".");
            return string.Join(" ", partes);
        }

        /// <summary>
        /// La cuadricula en un mapa de bits con la proporcion del espacio que le
        /// queda en la hoja (ancho x alto, en puntos de la hoja): en pantalla tan
        /// grande que cada hora mida al menos AltoHoraMinimo, y Escala veces eso.
        /// </summary>
        private static Bitmap Dibujar(CuadriculaSemanal cuadricula, float ancho, float alto)
        {
            float pantallaPorPunto = (CuadriculaSemanal.AltoCabecera + 6 + cuadricula.Rows.Count * AltoHoraMinimo) / alto;
            Size tamano = new Size((int)Math.Round(ancho * pantallaPorPunto * Escala), (int)Math.Round(alto * pantallaPorPunto * Escala));
            cuadricula.Size = tamano;
            cuadricula.PerformLayout();
            Bitmap dibujo = new Bitmap(tamano.Width, tamano.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                // Sin ensenarla: DrawToBitmap crea su ventana y la pinta en el mapa de bits.
                cuadricula.DrawToBitmap(dibujo, new Rectangle(Point.Empty, tamano));
                return dibujo;
            }
            catch
            {
                dibujo.Dispose();
                throw;
            }
        }

        private static iText.Font Letra(string nombre, float tamano, iText.BaseColor color)
        {
            return iText.FontFactory.GetFont(nombre, tamano, color);
        }
    }

    // =========================================================================
    // Dialogo: nueva clase / editar clase
    // =========================================================================

    internal class DialogoClase : Form
    {
        private readonly HorariosDatos datos;
        private readonly Consultas consultas;
        private readonly ClaseInfo existente;
        private readonly int creadaPor;

        private ComboBox cboProfesor;
        private ComboBox cboGrupo;
        private ComboBox cboMateria;
        private TextBox txtCodigo;
        private Button btnProponer;
        private DateTimePicker dtpDesde;
        private DateTimePicker dtpHasta;
        private Label lblAviso;
        private Button btnGuardar;
        private Button btnCancelar;

        // Desde db/09 el codigo es del profesor: aqui solo se ve y se cambia en Profesores.
        private readonly bool codigoPorProfesor;
        // Algun laboratorio entra sin el codigo fijo (db/11): ahi el codigo no se usa.
        private readonly bool hayOtrosModos;
        private LinkLabel lnkNuevoGrupo;

        // Con su carrera, para ofrecer solo las materias de la carrera del grupo.
        private List<OpcionDeCarrera> grupos = new List<OpcionDeCarrera>();
        private List<OpcionDeCarrera> materias = new List<OpcionDeCarrera>();

        public DialogoClase(HorariosDatos datos, Consultas consultas, ClaseInfo existente, int creadaPor)
        {
            this.datos = datos;
            this.consultas = consultas;
            this.existente = existente;
            this.creadaPor = creadaPor;
            try
            {
                codigoPorProfesor = datos.UsaCodigoPorProfesor();
            }
            catch (Exception ex)
            {
                Log.Error("DialogoClase: saber si el codigo es por profesor", ex);
            }

            try
            {
                Dictionary<int, ModoCodigo> modos = datos.LeerModos();
                hayOtrosModos = modos != null && modos.Values.Any(m => m != ModoCodigo.Fijo);
            }
            catch (Exception ex)
            {
                Log.Error("DialogoClase: modos de acceso", ex);
            }

            InicializarComponentes();
            CargarDatos();
        }

        private void InicializarComponentes()
        {
            EstiloHorarios.PrepararDialogo(this, existente == null ? "Nueva clase" : "Editar clase", new Size(520, 432));

            Label lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Text = existente == null ? "Nueva clase" : "Editar clase"
            };

            Label lblProfesor = EstiloHorarios.CrearEtiqueta("Docente", new Point(24, 70));
            cboProfesor = EstiloHorarios.CrearCombo(new Point(140, 66), 350);

            Label lblGrupo = EstiloHorarios.CrearEtiqueta("Grupo", new Point(24, 114));
            cboGrupo = EstiloHorarios.CrearCombo(new Point(140, 110), 250);

            // Los grupos son de las clases: si falta el de esta, se crea aqui mismo.
            lnkNuevoGrupo = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9F),
                LinkColor = Color.DarkGreen,
                Location = new Point(398, 115),
                Text = "Nuevo grupo"
            };
            lnkNuevoGrupo.LinkClicked += (s, e) => CrearGrupo();

            Label lblMateria = EstiloHorarios.CrearEtiqueta("Materia", new Point(24, 158));
            cboMateria = EstiloHorarios.CrearCombo(new Point(140, 154), 350);

            Label lblCodigo = EstiloHorarios.CrearEtiqueta("Código", new Point(24, 202));
            txtCodigo = EstiloHorarios.CrearTexto(new Point(140, 198), 140);
            txtCodigo.MaxLength = 16;
            txtCodigo.CharacterCasing = CharacterCasing.Normal;
            txtCodigo.Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);

            btnProponer = EstiloHorarios.CrearBoton("Proponer otro", new Point(292, 194), new Size(140, 34));
            btnProponer.Click += (s, e) => ProponerCodigo();

            Label lblNotaCodigo = null;
            if (codigoPorProfesor)
            {
                txtCodigo.ReadOnly = true;
                txtCodigo.TabStop = false;
                btnProponer.Visible = false;
                // Tres lineas a 8.5 pt (con 40 de alto la tercera salia cortada): de
                // 190 a 242, sin tocar las fechas de abajo (254).
                lblNotaCodigo = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 8.5F),
                    ForeColor = Color.FromArgb(90, 90, 90),
                    Location = new Point(292, 190),
                    Size = new Size(210, 52),
                    Text = "Es el de su docente: el mismo en todas sus clases. Se cambia en Docentes."
                };
                cboProfesor.SelectedIndexChanged += (s, e) => MostrarCodigoDelProfesor();
            }

            Label lblDesde = EstiloHorarios.CrearEtiqueta("Vigente desde", new Point(24, 258));
            dtpDesde = EstiloHorarios.CrearFecha(new Point(140, 254));
            dtpDesde.Value = DateTime.Today;

            Label lblHasta = EstiloHorarios.CrearEtiqueta("hasta", new Point(290, 258));
            dtpHasta = EstiloHorarios.CrearFecha(new Point(360, 254));
            dtpHasta.Value = DateTime.Today.AddMonths(5);

            lblAviso = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(24, 296),
                Size = new Size(470, 50),
                Text = (codigoPorProfesor
                    ? "El código es el de su docente y vale en todas sus clases, pero cada una solo en su laboratorio y a su hora."
                    : "El código lo teclea cada estudiante: identifica esta clase todo el semestre y solo entra en su laboratorio y su horario.")
                    + (hayOtrosModos ? " Solo se usa en los laboratorios con código único." : "")
            };

            // Guardar, la accion principal, relleno; Cancelar a su derecha, a 24 del borde.
            btnGuardar = EstiloAdmin.BotonPrincipal("Guardar", new Point(214, 368), new Size(135, 40));
            btnGuardar.Click += (s, e) => Guardar();

            btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(361, 368), new Size(135, 40));
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = btnGuardar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(lblProfesor);
            Controls.Add(cboProfesor);
            Controls.Add(lblGrupo);
            Controls.Add(cboGrupo);
            Controls.Add(lnkNuevoGrupo);
            Controls.Add(lblMateria);
            Controls.Add(cboMateria);
            Controls.Add(lblCodigo);
            Controls.Add(txtCodigo);
            Controls.Add(btnProponer);
            if (lblNotaCodigo != null)
            {
                Controls.Add(lblNotaCodigo);
            }
            Controls.Add(lblDesde);
            Controls.Add(dtpDesde);
            Controls.Add(lblHasta);
            Controls.Add(dtpHasta);
            Controls.Add(lblAviso);
            Controls.Add(btnGuardar);
            Controls.Add(btnCancelar);
        }

        private void CargarDatos()
        {
            List<OpcionCombo> profesores;
            try
            {
                profesores = datos.ObtenerProfesores(existente == null ? (int?)null : existente.ProfesorId);
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al log (AvisoDeFalloAdmin).
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "DialogoClase.ObtenerProfesores", ex, "No se pudo leer la lista de docentes", Text);
                profesores = new List<OpcionCombo>();
            }

            EstiloHorarios.Rellenar(cboProfesor, profesores, existente == null ? (int?)null : existente.ProfesorId);
            try
            {
                grupos = datos.GruposConCarrera();
                materias = datos.MateriasConCarrera();
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "DialogoClase: grupos y materias", ex, "No se pudieron leer los grupos y las materias", Text);
            }

            EstiloHorarios.Rellenar(cboGrupo, grupos, existente == null ? (int?)null : existente.GrupoId);
            FiltrarMaterias(existente == null ? (int?)null : existente.MateriaId);
            cboGrupo.SelectedIndexChanged += (s, e) => FiltrarMaterias(null);

            if (existente == null)
            {
                if (codigoPorProfesor)
                {
                    MostrarCodigoDelProfesor();
                }
                else
                {
                    ProponerCodigo();
                }

                AjustarAviso();
                return;
            }

            txtCodigo.Text = existente.Codigo;
            dtpDesde.Value = existente.VigenteDesde.Date;
            dtpHasta.Value = existente.VigenteHasta.Date;

            if (existente.SesionesTotales > 0)
            {
                // Con sesiones (con o sin bitacoras), profesor, grupo y materia ya no
                // se tocan: lo pasado quedaria a nombre de otros. La capa de datos lo
                // exige igual (ActualizarClase), y que la vigencia no deje fuera sus sesiones.
                cboProfesor.Enabled = false;
                cboGrupo.Enabled = false;
                lnkNuevoGrupo.Enabled = false;
                cboMateria.Enabled = false;
                lblAviso.ForeColor = Color.DarkGreen;
                string cuando = existente.PrimeraSesion.HasValue && existente.UltimaSesion.HasValue
                    ? (existente.PrimeraSesion.Value == existente.UltimaSesion.Value
                        ? ", el " + existente.PrimeraSesion.Value.ToString("dd/MM/yyyy")
                        : ", del " + existente.PrimeraSesion.Value.ToString("dd/MM/yyyy") + " al " + existente.UltimaSesion.Value.ToString("dd/MM/yyyy"))
                    : "";
                lblAviso.Text = "Ya tiene " + existente.SesionesTotales + (existente.SesionesTotales == 1 ? " sesión" : " sesiones") + cuando
                    + ": su docente, grupo y materia no se cambian y su vigencia tiene que incluir esas fechas"
                    + (codigoPorProfesor ? "." : " (el código sí se puede cambiar).")
                    + " Para cambiar de docente, usa «Terminar clase...» y crea otra clase.";
            }

            AjustarAviso();
        }

        /// <summary>
        /// El aviso de abajo, tan alto como su texto: con sesiones son cuatro lineas
        /// (en 50 de alto la ultima salia cortada). El dialogo crece con el y los
        /// botones bajan; nunca se encoge.
        /// </summary>
        private void AjustarAviso()
        {
            int pide = TextRenderer.MeasureText(lblAviso.Text ?? "", lblAviso.Font, new Size(lblAviso.Width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 2;
            int extra = pide - lblAviso.Height;
            if (extra <= 0)
            {
                return;
            }

            lblAviso.Height += extra;
            btnGuardar.Top += extra;
            btnCancelar.Top += extra;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + extra);
        }

        /// <summary>El codigo del profesor elegido; "(nuevo)" si aun no tiene: se le asigna al guardar.</summary>
        private void MostrarCodigoDelProfesor()
        {
            OpcionCombo profesor = cboProfesor.SelectedItem as OpcionCombo;
            if (profesor == null)
            {
                txtCodigo.Text = "";
                return;
            }

            try
            {
                string codigo = datos.CodigoDelProfesor(profesor.Id);
                txtCodigo.Text = string.IsNullOrEmpty(codigo) ? "(nuevo)" : codigo;
            }
            catch (Exception ex)
            {
                Log.Error("DialogoClase: leer el codigo del profesor " + profesor.Id, ex);
                txtCodigo.Text = "";
            }
        }

        /// <summary>
        /// Las materias de la carrera del grupo elegido (y las que no tienen
        /// carrera), en vez de las de todas las carreras. Sin grupo, o si su
        /// carrera no tiene materias, todas. La que ya estaba elegida se conserva
        /// si es de la nueva carrera; la de una clase que se edita, siempre.
        /// </summary>
        private void FiltrarMaterias(int? seleccionar)
        {
            OpcionDeCarrera grupo = cboGrupo.SelectedItem as OpcionDeCarrera;
            OpcionCombo elegida = cboMateria.SelectedItem as OpcionCombo;
            int? materia = seleccionar ?? (elegida == null ? (int?)null : elegida.Id);
            int? materiaDeLaClase = existente == null ? (int?)null : existente.MateriaId;

            List<OpcionDeCarrera> ofrecidas = materias;
            if (grupo != null && grupo.CarreraId.HasValue)
            {
                List<OpcionDeCarrera> deSuCarrera = materias.Where(m => !m.CarreraId.HasValue || m.CarreraId == grupo.CarreraId).ToList();
                if (deSuCarrera.Count > 0)
                {
                    ofrecidas = deSuCarrera;
                }
            }

            if (materia.HasValue && !ofrecidas.Any(m => m.Id == materia.Value))
            {
                // La de una clase existente sale aunque sea de otra carrera; una
                // elegida a mano para otro grupo se suelta, en vez de quedarse
                // colgada al final de la lista sin que se note.
                OpcionDeCarrera suya = materia == materiaDeLaClase ? materias.FirstOrDefault(m => m.Id == materia.Value) : null;
                if (suya != null)
                {
                    ofrecidas = new List<OpcionDeCarrera>(ofrecidas) { suya };
                }
                else
                {
                    materia = null;
                }
            }

            EstiloHorarios.Rellenar(cboMateria, ofrecidas, materia);
        }

        private void CrearGrupo()
        {
            using (DialogoGrupo dialogo = new DialogoGrupo(0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        grupos = datos.GruposConCarrera();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("DialogoClase: releer los grupos", ex);
                    }

                    EstiloHorarios.Rellenar(cboGrupo, grupos, dialogo.IdGuardado);
                }
            }
        }

        private void ProponerCodigo()
        {
            try
            {
                txtCodigo.Text = datos.ProponerCodigo();
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                // Todos los que probo ya estaban en uso: el aviso ya dice que hacer.
                Log.Aviso("DialogoClase.ProponerCodigo: ningun codigo libre entre los que se probaron.");
                MessageBox.Show(EstiloHorarios.Dueno(this), ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "DialogoClase.ProponerCodigo", ex, "No se pudo proponer un código", Text);
            }
        }

        private void Guardar()
        {
            OpcionCombo profesor = cboProfesor.SelectedItem as OpcionCombo;
            OpcionCombo grupo = cboGrupo.SelectedItem as OpcionCombo;
            OpcionCombo materia = cboMateria.SelectedItem as OpcionCombo;
            if (profesor == null || grupo == null || materia == null)
            {
                MessageBox.Show("Elige docente, grupo y materia.", "Datos incompletos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Con un codigo por profesor el que se muestra es el suyo y lo pone HorariosDatos.
            string codigo = codigoPorProfesor ? "" : txtCodigo.Text.Trim();
            if (!codigoPorProfesor && codigo.Length == 0)
            {
                MessageBox.Show("Escribe un código o pulsa «Proponer otro».", "Código requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtCodigo.Focus();
                return;
            }

            if (!codigoPorProfesor && codigo.Any(char.IsWhiteSpace))
            {
                MessageBox.Show("El código no puede llevar espacios.", "Código no válido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtCodigo.Focus();
                return;
            }

            if (dtpHasta.Value.Date < dtpDesde.Value.Date)
            {
                MessageBox.Show("La fecha de fin de vigencia no puede ser anterior a la de inicio.", "Vigencia no válida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (!codigoPorProfesor && !datos.CodigoDisponible(codigo, existente == null ? (int?)null : existente.Id))
                {
                    MessageBox.Show("El código «" + codigo + "» ya está en uso por otra clase. Elige otro.", "Código repetido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtCodigo.Focus();
                    return;
                }

                if (existente == null)
                {
                    datos.CrearClase(profesor.Id, grupo.Id, materia.Id, codigo, dtpDesde.Value.Date, dtpHasta.Value.Date, creadaPor);
                }
                else
                {
                    datos.ActualizarClase(existente.Id, profesor.Id, grupo.Id, materia.Id, codigo, dtpDesde.Value.Date, dtpHasta.Value.Date);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                MessageBox.Show(ex.Message, "No se pudo guardar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al log. El dialogo sigue abierto.
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "DialogoClase.Guardar", ex, "No se pudo guardar la clase", Text);
            }
        }
    }

    // =========================================================================
    // Dialogo: nueva franja / editar franja
    // =========================================================================

    internal class DialogoFranja : Form
    {
        private readonly HorariosDatos datos;
        private readonly List<ClaseInfo> clasesActivas;
        private readonly OpcionCombo laboratorio;
        private readonly FranjaInfo existente;
        private readonly int? diaInicial;
        private readonly TimeSpan? horaInicial;

        private SelectorClase selector;
        private ComboBox cboDia;
        private ComboBox cboInicio;
        private ComboBox cboFin;
        private Button btnGuardar;
        private Button btnCancelar;

        // La que se edita: lo que ya paso con ella (HorariosDatos.HistoriaDeFranja).
        // Null en una nueva, o si no se pudo leer (entonces se guarda como antes).
        private readonly HistoriaFranja historia;
        // Una nueva con los cambios desde una fecha encendidos: hoy (servidor); null sin ellos.
        private readonly DateTime? hoyConFecha;
        // Lo que propone su "A partir del" (hoy o despues; ver DesdePorOmision).
        private readonly DateTime propuesta;
        // "Aplica desde" (la que se edita) o "A partir del" (una nueva); null si no se pide.
        private DateTimePicker dtpDesde;
        private Label lblDesdeNota;
        // La nota gris de abajo: que pasa si se cruza con otras horas.
        private Label lblNotaCruces;
        private bool reemplazar;

        /// <summary>
        /// Con db/25 (Horario semanal): encima de otra clase de este laboratorio
        /// la reemplaza en vez de chocar (antes lo confirma) y lo que se le quite
        /// queda en «Falta asignar». Sin db/25, false: como siempre.
        /// </summary>
        public bool Reemplazar
        {
            get { return reemplazar; }
            set
            {
                reemplazar = value;
                lblNotaCruces.Text = value
                    ? "Si se cruza con otra clase de este laboratorio, te pregunta si la reemplaza: lo que se le quite queda en «Falta asignar». "
                      + "No se guarda si se cruza con la misma persona docente o el mismo grupo en otro laboratorio."
                    : NotaCruces;
            }
        }

        /// <summary>La clase con que se guardo (0 si no se guardo).</summary>
        public int ClaseGuardada { get; private set; }

        private const string NotaCruces = "No se guarda si se cruza con otras horas del mismo laboratorio, ni si choca con la misma persona docente o el mismo grupo en otro laboratorio.";

        /// <summary>Una nueva con una clase ya elegida (la traida de "Falta asignar").</summary>
        public void Preelegir(ClaseInfo clase)
        {
            if (existente == null && clase != null)
            {
                selector.Seleccionar(clase);
            }
        }

        /// <param name="hoyConFecha">Solo para una nueva: hoy segun el servidor si los cambios de horario aplican desde una fecha; si no, null.</param>
        /// <param name="desdePropuesto">Solo para una nueva con hoyConFecha: lo que propone "A partir del" (el dia de la columna, o el lunes de la semana que se ve); null o antes de hoy, hoy.</param>
        public DialogoFranja(HorariosDatos datos, List<ClaseInfo> clasesActivas, OpcionCombo laboratorio, FranjaInfo existente, int? diaInicial, TimeSpan? horaInicial,
            DateTime? hoyConFecha, DateTime? desdePropuesto)
        {
            this.datos = datos;
            this.clasesActivas = clasesActivas;
            this.laboratorio = laboratorio;
            this.existente = existente;
            this.diaInicial = diaInicial;
            this.horaInicial = horaInicial;
            this.hoyConFecha = existente == null ? hoyConFecha : null;
            if (this.hoyConFecha.HasValue)
            {
                propuesta = desdePropuesto.HasValue && desdePropuesto.Value.Date > this.hoyConFecha.Value ? desdePropuesto.Value.Date : this.hoyConFecha.Value;
            }
            if (existente != null)
            {
                try
                {
                    historia = datos.HistoriaDeFranja(existente.Id);
                }
                catch (Exception ex)
                {
                    Log.Error("DialogoFranja: leer la historia de la franja " + existente.Id, ex);
                }
            }

            InicializarComponentes();
            CargarDatos();
        }

        /// <summary>La que se edita ya tiene historia y los cambios aplican desde una fecha: se parte en vez de cambiarse en su sitio.</summary>
        private bool PartirDesde
        {
            get { return historia != null && historia.ConFecha && historia.TieneHistoria; }
        }

        /// <summary>Partiendola ya no queda nada: termino antes del primer dia en que puede aplicar un cambio.</summary>
        private bool YaTermino
        {
            get { return PartirDesde && existente.VigenteHasta.Date < historia.PrimerDiaPosible; }
        }

        /// <summary>
        /// El aviso de cuando los cambios de horario no aplican desde una fecha
        /// (ConFecha apagado, o sin db/20) y la franja ya tiene historia: que
        /// tambien cambia (o, al quitarla, borra) lo pasado, y como evitarlo.
        /// </summary>
        internal static string AvisoSinFecha(HistoriaFranja historia, bool quitar)
        {
            int semanas = historia.SemanasPasadas;
            string pasado;
            if (quitar)
            {
                pasado = semanas > 1 ? "Ojo: también desaparecen de las " + semanas + " semanas que ya pasaron en Faltas."
                    : semanas == 1 ? "Ojo: también desaparecen de la semana que ya pasó en Faltas."
                    : "Ojo: también se pierde lo que ya pasó con ellas: sus sesiones se quedan sin horario y sus cancelaciones se borran.";
            }
            else
            {
                pasado = semanas > 1 ? "Ojo: también cambia las " + semanas + " semanas que ya pasaron (Faltas las vuelve a contar)."
                    : semanas == 1 ? "Ojo: también cambia la semana que ya pasó (Faltas la vuelve a contar)."
                    : "Ojo: también cambia lo que ya pasó con estas horas (sus sesiones y cancelaciones).";
            }

            // Ya no hay casilla en Ajustes (2026-10-31): con db/20, siempre desde una fecha.
            return pasado + " Para que " + (quitar ? "se quiten" : "valga") + " solo desde una fecha, falta aplicar "
                + "db/20_vigencia_franjas.sql en la base (pídeselo a quien administra el servidor).";
        }

        /// <summary>
        /// Desde cuando vale por omision una franja nueva de esa clase: la fecha
        /// propuesta (hoy, el lunes de la semana que se ve o el dia de la columna
        /// del doble clic, nunca antes de hoy), o el primer dia de su clase si
        /// empieza despues.
        /// </summary>
        internal static DateTime DesdePorOmision(ClaseInfo clase, DateTime propuesta)
        {
            return clase != null && clase.VigenteDesde.Date > propuesta.Date ? clase.VigenteDesde.Date : propuesta.Date;
        }

        /// <summary>La nota bajo "A partir del" de una franja nueva, segun su clase haya empezado o no.</summary>
        internal static string NotaDesde(ClaseInfo clase, DateTime hoy)
        {
            if (clase == null)
            {
                return "Desde ese día sale en el horario. Faltas no la pide en las semanas que ya pasaron.";
            }

            return clase.VigenteDesde.Date > hoy
                ? "La clase empieza el " + clase.VigenteDesde.ToString("dd/MM/yyyy") + ": sus horas valen desde ese día, o desde la fecha que elijas si es después."
                : "La clase ya empezó (el " + clase.VigenteDesde.ToString("dd/MM/yyyy") + "): sus horas valen desde la fecha que elijas, "
                  + "y Faltas no la pide en las semanas que ya pasaron.";
        }

        /// <summary>Lo que se dice bajo los datos de la franja que se edita: como se guardara, o null si como siempre sin nada que avisar.</summary>
        private string AvisoDeHistoria(out Color color)
        {
            color = Color.DarkGreen;
            if (historia == null || !historia.TieneHistoria)
            {
                return null;
            }

            if (!historia.ConFecha)
            {
                color = Color.FromArgb(190, 85, 0);
                return AvisoSinFecha(historia, false);
            }

            if (YaTermino)
            {
                color = Color.FromArgb(190, 85, 0);
                return "Estas horas terminaron el " + existente.VigenteHasta.ToString("dd/MM/yyyy") + ": ya no se cambian. "
                    + "Para volver a dar esa clase, ponle horas nuevas con «Poner clase...».";
            }

            string que = historia.SemanasPasadas > 0 ? "Estas horas ya se daban antes de hoy"
                : historia.Sesiones > 0 ? "Estas horas ya tienen sesiones"
                : "Estas horas ya tienen cancelaciones";
            return que + ": el cambio aplica desde la fecha que elijas y hasta el día anterior se queda como estaba "
                + "(Faltas y la asistencia no cambian)."
                + (historia.CancelacionesFuturas > 0 ? " Sus cancelaciones de esa fecha en adelante pasan al horario nuevo." : "")
                + AdministrarHorarios.AvisoDeHoy(historia);
        }

        private void InicializarComponentes()
        {
            EstiloHorarios.PrepararDialogo(this, existente == null ? TextosDelHorario.TituloPoner : TextosDelHorario.TituloCambiar, new Size(560, 376));

            Label lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Text = (existente == null ? "Poner una clase en " : "Cambiar horas en ") + laboratorio.Texto
            };

            selector = new SelectorClase(clasesActivas, 506, null) { Location = new Point(24, 56) };

            Label lblDia = EstiloHorarios.CrearEtiqueta("Día", new Point(24, 202));
            cboDia = EstiloHorarios.CrearCombo(new Point(110, 198), 150);

            Label lblInicio = EstiloHorarios.CrearEtiqueta("Inicio", new Point(24, 246));
            cboInicio = EstiloHorarios.CrearComboHoras(new Point(110, 242), null);

            Label lblFin = EstiloHorarios.CrearEtiqueta("Fin", new Point(230, 246));
            cboFin = EstiloHorarios.CrearComboHoras(new Point(280, 242), null);

            Controls.Add(lblTitulo);
            Controls.Add(selector);
            Controls.Add(lblDia);
            Controls.Add(cboDia);
            Controls.Add(lblInicio);
            Controls.Add(cboInicio);
            Controls.Add(lblFin);
            Controls.Add(cboFin);

            // Debajo de las horas, lo que dependa de la historia de la franja (db/20):
            // desde cuando aplica el cambio, o desde cuando vale la nueva, y el aviso.
            int y = 284;
            if ((PartirDesde && !YaTermino) || hoyConFecha.HasValue)
            {
                Label lblDesde = EstiloHorarios.CrearEtiqueta(existente == null ? "A partir del" : "Aplica desde", new Point(24, y + 4));
                Controls.Add(lblDesde);
                dtpDesde = EstiloHorarios.CrearFechaConDia(this, new Point(150, y));
                if (existente == null)
                {
                    // Una nueva: desde hoy como pronto; propone la fecha propuesta o el
                    // primer dia de su clase (CargarDatos y al cambiar de clase).
                    dtpDesde.MinDate = hoyConFecha.Value;
                    dtpDesde.Value = propuesta;
                }
                else
                {
                    // La que se edita: de su primer dia posible (hoy, o manana si hoy ya hubo
                    // clase con bitacoras) a su ultimo dia; hasta el anterior queda como estaba.
                    dtpDesde.MinDate = historia.PrimerDiaPosible;
                    dtpDesde.MaxDate = existente.VigenteHasta.Date;
                    dtpDesde.Value = historia.PrimerDiaPosible;
                }

                y += 38;
            }

            Color colorAviso = Color.FromArgb(80, 80, 80);
            string aviso = existente != null ? AvisoDeHistoria(out colorAviso) : hoyConFecha.HasValue ? NotaDesde(null, hoyConFecha.Value) : null;
            if (aviso != null)
            {
                lblDesdeNota = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 8.5F, existente == null ? FontStyle.Regular : FontStyle.Bold),
                    ForeColor = colorAviso,
                    Location = new Point(24, y),
                    Size = new Size(510, 20),
                    Text = aviso
                };
                // Tan alta como su texto (de dos a cuatro lineas): el dialogo crece con
                // ella. La de una nueva cambia con la clase elegida: siempre dos lineas.
                lblDesdeNota.Height = Math.Max(existente == null ? 32 : 0, TextRenderer.MeasureText(aviso, lblDesdeNota.Font,
                    new Size(lblDesdeNota.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 4);
                Controls.Add(lblDesdeNota);
                y += lblDesdeNota.Height + 6;
            }

            Label lblNota = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(24, y),
                Size = new Size(510, 36),
                Text = NotaCruces
            };
            lblNotaCruces = lblNota;
            y += 52;

            // 24 libres debajo de los botones, como en los demas dialogos.
            ClientSize = new Size(560, y + 64);
            // Relleno, la accion principal; si ya termino (apagado), con borde.
            btnGuardar = YaTermino
                ? EstiloHorarios.CrearBoton(existente == null ? "Poner la clase" : "Guardar", new Point(249, y), new Size(135, 40))
                : EstiloAdmin.BotonPrincipal(existente == null ? "Poner la clase" : "Guardar", new Point(249, y), new Size(135, 40));
            btnGuardar.Click += (s, e) => Guardar();
            // Una que ya termino no tiene nada que cambiar (lo dice el aviso).
            btnGuardar.Enabled = !YaTermino;

            btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(396, y), new Size(140, 40));
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = btnGuardar;
            CancelButton = btnCancelar;

            Controls.Add(lblNota);
            Controls.Add(btnGuardar);
            Controls.Add(btnCancelar);
        }

        private void CargarDatos()
        {
            if (existente != null)
            {
                ClaseInfo suya = clasesActivas.FirstOrDefault(c => c.Id == existente.ClaseId);
                if (suya == null)
                {
                    // La clase de la franja esta inactiva o vencida: se muestra
                    // para poder editarla igualmente.
                    suya = new ClaseInfo
                    {
                        Id = existente.ClaseId,
                        ProfesorId = existente.ProfesorId,
                        Profesor = existente.Profesor,
                        ProfesorCorto = existente.ProfesorCorto,
                        Materia = existente.Materia + " (inactiva)",
                        Grupo = existente.Grupo,
                        Codigo = existente.Codigo,
                        VigenteDesde = existente.VigenteDesde,
                        VigenteHasta = existente.VigenteHasta,
                        Activa = false
                    };
                }

                selector.Seleccionar(suya);
            }

            // De lunes a sabado, como en la preparacion y "Clase del horario" (2026-11-03:
            // antes tambien el domingo, que la cuadricula no ensenaba). Unas horas que ya
            // son del domingo lo traen, para que se vea que dia son y se pasen a otro.
            int dia = existente != null ? existente.DiaSemana : (diaInicial ?? 1);
            EstiloHorarios.Rellenar(cboDia, TextosDelHorario.DiasParaElegir(dia), dia);

            if (existente != null)
            {
                cboInicio.Text = HorariosDatos.Hora(existente.Inicio);
                cboFin.Text = HorariosDatos.Hora(existente.Fin);
            }
            else if (horaInicial.HasValue)
            {
                cboInicio.Text = HorariosDatos.Hora(horaInicial.Value);
                cboFin.Text = HorariosDatos.Hora(EstiloHorarios.FinPropuesto(horaInicial.Value));
            }

            // Una nueva con los cambios desde una fecha: "A partir del" sigue a la
            // clase elegida (la fecha propuesta, o su primer dia si empieza despues).
            if (existente == null && dtpDesde != null)
            {
                Action ponerDesde = () =>
                {
                    ClaseInfo clase = selector.ClaseElegida;
                    dtpDesde.Value = DesdePorOmision(clase, propuesta);
                    lblDesdeNota.Text = NotaDesde(clase, hoyConFecha.Value);
                };
                selector.Cambio += ponerDesde;
                ponerDesde();
            }
        }

        private void Guardar()
        {
            ClaseInfo clase = selector.ClaseElegida;
            OpcionCombo dia = cboDia.SelectedItem as OpcionCombo;
            if (clase == null || dia == null)
            {
                MessageBox.Show("Elige a la persona docente, su clase y el día.", "Datos incompletos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            TimeSpan inicio, fin;
            if (!EstiloHorarios.LeerHora(cboInicio, out inicio) || !EstiloHorarios.LeerHora(cboFin, out fin))
            {
                MessageBox.Show("Escribe las horas como HH:mm (por ejemplo 08:00).", "Hora no válida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (fin <= inicio)
            {
                MessageBox.Show("La hora de fin tiene que ser posterior a la de inicio.", "Hora no válida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (reemplazar)
                {
                    if (GuardarReemplazando(clase, dia.Id, inicio, fin))
                    {
                        ClaseGuardada = clase.Id;
                        DialogResult = DialogResult.OK;
                        Close();
                    }

                    return;
                }

                if (existente == null)
                {
                    // Sin los cambios desde una fecha, sin desde: como siempre.
                    datos.CrearFranja(clase.Id, laboratorio.Id, dia.Id, inicio, fin, dtpDesde == null ? (DateTime?)null : dtpDesde.Value.Date);
                }
                else if (historia != null)
                {
                    // Con historia y los cambios desde una fecha, la parte desde la
                    // elegida; si no, la cambia en su sitio como ActualizarFranja (y si
                    // gano historia desde que se abrio, la parte desde su primer dia
                    // posible en vez de reescribir lo pasado).
                    datos.CambiarFranjaDesde(existente.Id, clase.Id, laboratorio.Id, dia.Id, inicio, fin,
                        dtpDesde == null ? historia.PrimerDiaPosible : dtpDesde.Value.Date);
                }
                else
                {
                    datos.ActualizarFranja(existente.Id, clase.Id, laboratorio.Id, dia.Id, inicio, fin);
                }

                ClaseGuardada = clase.Id;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                MessageBox.Show(ex.Message, TextosDelHorario.TituloDeCruce(ex.Message), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "DialogoFranja.Guardar", ex, "No se pudieron guardar las horas", Text);
            }
        }

        /// <summary>
        /// Con db/25: lo que no deja guardarla (la misma persona docente o el
        /// mismo grupo en otro laboratorio: un choque) se dice y no se guarda;
        /// lo de otras clases de este laboratorio se confirma
        /// («Reemplazar») y queda en «Falta asignar». False si no se guardo.
        /// Lanza lo mismo que Guardar.
        /// </summary>
        private bool GuardarReemplazando(ClaseInfo clase, int dia, TimeSpan inicio, TimeSpan fin)
        {
            // La que se edita, desde la fecha elegida (o su primer dia posible), como CambiarFranjaDesde.
            DateTime? desde = dtpDesde != null ? dtpDesde.Value.Date
                : existente != null && historia != null ? historia.PrimerDiaPosible
                : (DateTime?)null;
            PlanReemplazo plan = datos.PlanDeReemplazo(existente == null ? (int?)null : existente.Id, clase.Id, laboratorio.Id, dia, inicio, fin, desde);
            if (plan.Bloqueo != null)
            {
                MessageBox.Show(this, plan.Bloqueo, "Choque de horario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            bool conFecha = existente == null ? hoyConFecha.HasValue : historia != null && historia.ConFecha;
            if (plan.Quitadas.Count > 0 && !ConfirmarReemplazo.Preguntar(this, plan.Quitadas, conFecha ? desde : null))
            {
                return false;
            }

            if (existente == null)
            {
                datos.PonerClaseReemplazando(clase.Id, laboratorio.Id, dia, inicio, fin, desde);
            }
            else
            {
                datos.CambiarFranjaReemplazando(existente.Id, clase.Id, laboratorio.Id, dia, inicio, fin, desde ?? DateTime.Today);
            }

            return true;
        }
    }

    // =========================================================================
    // Dialogo: sesion extra
    // =========================================================================

    internal class DialogoExtra : Form
    {
        private readonly HorariosDatos datos;
        private readonly List<ClaseInfo> clasesActivas;
        private readonly OpcionCombo laboratorio;
        private readonly DateTime fecha;
        private readonly TimeSpan? inicioSugerido;
        private readonly int creadaPor;

        private SelectorClase selector;
        private ComboBox cboInicio;
        private ComboBox cboFin;
        private TextBox txtMotivo;
        private Button btnGuardar;
        private Button btnCancelar;

        public DialogoExtra(HorariosDatos datos, List<ClaseInfo> clasesActivas, OpcionCombo laboratorio, DateTime fecha, TimeSpan? inicioSugerido, int creadaPor)
        {
            this.datos = datos;
            this.clasesActivas = clasesActivas;
            this.laboratorio = laboratorio;
            this.fecha = fecha.Date;
            this.inicioSugerido = inicioSugerido;
            this.creadaPor = creadaPor;

            InicializarComponentes();
            CargarDatos();
        }

        private void InicializarComponentes()
        {
            EstiloHorarios.PrepararDialogo(this, "Dar una sesión extra", new Size(560, 432));

            Label lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Text = "Sesión extra en " + laboratorio.Texto + " el " + fecha.ToString("dd/MM/yyyy")
            };

            // Solo clases vigentes en esa fecha: una extra de una clase fuera de periodo no entraria de todas formas.
            selector = new SelectorClase(clasesActivas.Where(c => c.VigenteEn(fecha)).ToList(), 506, null) { Location = new Point(24, 56) };

            Label lblInicio = EstiloHorarios.CrearEtiqueta("Inicio", new Point(24, 202));
            cboInicio = EstiloHorarios.CrearComboHoras(new Point(110, 198), null);

            Label lblFin = EstiloHorarios.CrearEtiqueta("Fin", new Point(230, 202));
            cboFin = EstiloHorarios.CrearComboHoras(new Point(280, 198), null);

            Label lblMotivo = EstiloHorarios.CrearEtiqueta("Motivo", new Point(24, 246));
            txtMotivo = EstiloHorarios.CrearTexto(new Point(110, 242), 420);
            txtMotivo.MaxLength = 200;

            Label lblNota = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(24, 286),
                // Tres lineas enteras (con 50 la tercera no salia).
                Size = new Size(510, 58),
                Text = "Solo ese día y a esas horas: con el código de su docente se podrá registrar en este laboratorio. "
                    + "No se guarda si se cruza con otra clase que se da ese día (no cancelada) o con otra sesión extra."
            };

            btnGuardar = EstiloAdmin.BotonPrincipal("Dar la sesión extra", new Point(204, 368), new Size(180, 40));
            btnGuardar.Click += (s, e) => Guardar();

            btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(396, 368), new Size(140, 40));
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = btnGuardar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(selector);
            Controls.Add(lblInicio);
            Controls.Add(cboInicio);
            Controls.Add(lblFin);
            Controls.Add(cboFin);
            Controls.Add(lblMotivo);
            Controls.Add(txtMotivo);
            Controls.Add(lblNota);
            Controls.Add(btnGuardar);
            Controls.Add(btnCancelar);
        }

        private void CargarDatos()
        {
            // Tambien despues del cierre (una clase que acaba a las 21:00 o mas tarde):
            // antes no se proponia nada y el fin se cortaba en las 21:00.
            if (inicioSugerido.HasValue && inicioSugerido.Value < EstiloHorarios.UltimoMinuto)
            {
                cboInicio.Text = HorariosDatos.Hora(inicioSugerido.Value);
                cboFin.Text = HorariosDatos.Hora(EstiloHorarios.FinPropuesto(inicioSugerido.Value));
            }
        }

        private void Guardar()
        {
            ClaseInfo clase = selector.ClaseElegida;
            if (clase == null)
            {
                MessageBox.Show("Elige a la persona docente y su clase.", "Datos incompletos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            TimeSpan inicio, fin;
            if (!EstiloHorarios.LeerHora(cboInicio, out inicio) || !EstiloHorarios.LeerHora(cboFin, out fin))
            {
                MessageBox.Show("Escribe las horas como HH:mm (por ejemplo 08:00).", "Hora no válida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (fin <= inicio)
            {
                MessageBox.Show("La hora de fin tiene que ser posterior a la de inicio.", "Hora no válida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                datos.CrearExtra(clase.Id, laboratorio.Id, fecha, inicio, fin, txtMotivo.Text.Trim(), creadaPor);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigBit(ex))
            {
                MessageBox.Show(ex.Message, TextosDelHorario.TituloDeCruce(ex.Message), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "DialogoExtra.Guardar", ex, "No se pudo dar la sesión extra", Text);
            }
        }
    }

    // =========================================================================
    // Dialogo: pedir un motivo (cancelacion de un dia)
    // =========================================================================

    internal class DialogoMotivo : Form
    {
        private TextBox txtMotivo;

        public string Motivo
        {
            get { return txtMotivo.Text.Trim(); }
        }

        /// <summary>
        /// Cancelar una clase un dia (Horario semanal, Cambios de un dia, Faltas y
        /// Alertas): los botones "Cancelar la clase" (en rojo) y "Volver". Con
        /// "Aceptar" y "Cancelar", "Cancelar" parecia cancelar la clase y no lo hacia.
        /// </summary>
        internal static DialogoMotivo CancelarLaClase(string titulo, string descripcion, string etiqueta)
        {
            return new DialogoMotivo(titulo, descripcion, etiqueta, "Cancelar la clase", "Volver", true);
        }

        public DialogoMotivo(string titulo, string descripcion, string etiqueta)
            : this(titulo, descripcion, etiqueta, "Aceptar", "Cancelar", false)
        {
        }

        /// <param name="aceptar">El texto del boton que hace la accion.</param>
        /// <param name="volver">El del que cierra sin hacer nada (Esc).</param>
        /// <param name="enRojo">La accion quita algo (una clase de un dia): su boton en rojo, como "Terminar clase".</param>
        public DialogoMotivo(string titulo, string descripcion, string etiqueta, string aceptar, string volver, bool enRojo)
        {
            // Como los demas dialogos (2026-10-31): TituloDialogo y BotonesDialogo (la
            // accion rellena, en rojo si quita algo, y "Volver"/"Cancelar" a su derecha).
            const int Ancho = 520;
            Label lblDescripcion = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(24, 54),
                Size = new Size(Ancho - 48, 20),
                Text = descripcion
            };

            // Una descripcion larga (con el aviso de que la clase ya paso) no se corta: crece el dialogo.
            lblDescripcion.Height = Math.Max(20, PiezasConfiguracion.AltoDe(descripcion ?? "", lblDescripcion.Font, lblDescripcion.Width) + 4);

            Label lblEtiqueta = EstiloHorarios.CrearEtiqueta(etiqueta, new Point(24, lblDescripcion.Bottom + 12));
            txtMotivo = EstiloHorarios.CrearTexto(new Point(24, lblDescripcion.Bottom + 36), Ancho - 48);
            txtMotivo.MaxLength = 200;
            EstiloHorarios.PrepararDialogo(this, titulo, new Size(Ancho, txtMotivo.Bottom + 16 + 56));

            Controls.Add(EstiloAdmin.TituloDialogo(titulo, Ancho - 48));
            Controls.Add(lblDescripcion);
            Controls.Add(lblEtiqueta);
            Controls.Add(txtMotivo);

            Button btnAceptar = EstiloAdmin.BotonesDialogo(this, aceptar, () => DialogResult = DialogResult.OK);
            Button btnCancelar = (Button)CancelButton;
            if (btnCancelar.Text != volver)
            {
                // "Volver" (o lo que se pida), a la medida de lo que dice: a la derecha, como Cancelar.
                int derecha = btnCancelar.Right;
                btnCancelar.Text = volver;
                using (DrawingFont letra = new DrawingFont("Century Gothic", 10F, FontStyle.Bold))
                {
                    btnCancelar.Width = Math.Max(120, TextRenderer.MeasureText(volver, letra).Width + 30);
                }

                btnCancelar.Left = derecha - btnCancelar.Width;
                btnAceptar.Left = btnCancelar.Left - 10 - btnAceptar.Width;
            }

            if (enRojo)
            {
                EstiloAdmin.Peligroso(btnAceptar);
            }

            ActiveControl = txtMotivo;
        }
    }

    // =========================================================================
    // Dialogo: una fecha (quitar una franja desde una fecha, terminar una clase)
    // =========================================================================

    /// <summary>
    /// Pide una fecha entre un minimo y un maximo (los dos incluidos), con lo
    /// que se va a hacer en la descripcion y el verbo en el boton: "Quitar
    /// desde" una franja con historia (QuitarFranjaDesde), el "Ultimo dia" de
    /// una clase con sesiones (TerminarClase).
    /// </summary>
    internal class DialogoFecha : Form
    {
        private const int Ancho = 520;
        private readonly DateTimePicker dtpFecha;
        private readonly Label lblError;
        private readonly DateTime minimo;
        private readonly DateTime maximo;

        /// <summary>La fecha elegida (sin hora).</summary>
        public DateTime Fecha
        {
            get { return dtpFecha.Value.Date; }
        }

        public DialogoFecha(string titulo, string descripcion, string etiqueta, DateTime valor, DateTime minimo, DateTime maximo, string accion)
        {
            this.minimo = minimo.Date;
            this.maximo = maximo.Date < this.minimo ? this.minimo : maximo.Date;

            Label lblDescripcion = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(24, 56),
                Size = new Size(Ancho - 48, 20),
                Text = descripcion
            };
            // Tan alta como su texto: el dialogo crece con ella.
            lblDescripcion.Height = TextRenderer.MeasureText(descripcion ?? "", lblDescripcion.Font, new Size(lblDescripcion.Width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 4;

            int y = lblDescripcion.Bottom + 14;
            Label lblFecha = EstiloHorarios.CrearEtiqueta(etiqueta, new Point(24, y + 4));
            lblError = EstiloAdmin.LineaError(new Point(24, y + 38), Ancho - 48, 36);
            EstiloHorarios.PrepararDialogo(this, titulo, new Size(Ancho, lblError.Bottom + 8 + 56));
            Controls.Add(EstiloAdmin.TituloDialogo(titulo, Ancho - 48));
            Controls.Add(lblDescripcion);
            Controls.Add(lblFecha);
            dtpFecha = EstiloHorarios.CrearFechaConDia(this, new Point(24 + lblFecha.PreferredWidth + 12, y));
            dtpFecha.MinDate = this.minimo;
            dtpFecha.MaxDate = this.maximo;
            dtpFecha.Value = valor.Date < this.minimo ? this.minimo : valor.Date > this.maximo ? this.maximo : valor.Date;
            dtpFecha.ValueChanged += (s, e) => lblError.Text = "";
            Controls.Add(lblError);
            // Los dos usos quitan algo (terminar una clase, quitar una franja): como
            // las bajas (DialogoConfirmacion peligrosa), el boton en rojo (tambien al
            // pasar el raton: antes se ponia verde) y Enter cancela; la accion se
            // pulsa a proposito.
            EstiloAdmin.Peligroso(EstiloAdmin.BotonesDialogo(this, accion, Aceptar));
            AcceptButton = CancelButton as Button;
            ActiveControl = dtpFecha;
        }

        private void Aceptar()
        {
            DateTime fecha = Fecha;
            if (fecha < minimo || fecha > maximo)
            {
                lblError.Text = "Elige una fecha del " + minimo.ToString("dd/MM/yyyy") + " al " + maximo.ToString("dd/MM/yyyy") + ".";
                return;
            }

            DialogResult = DialogResult.OK;
        }
    }


    // =========================================================================
    // Dialogo: poner una clase (docente - materia - grupo) en los cuadros libres
    // elegidos en la cuadricula (Horario semanal y la preparacion de Proximo ciclo)
    // =========================================================================

    internal class DialogoAsignarClase : Form
    {
        private readonly SelectorClase selector;
        private readonly Label lblError;
        // Con los cambios desde una fecha: "A partir del" y su nota (null sin ellos).
        private readonly DateTimePicker dtpDesde;

        public int ClaseId { get; private set; }

        /// <summary>Desde cuando valen las franjas nuevas (CrearFranja con desde); null sin los cambios desde una fecha.</summary>
        public DateTime? Desde { get; private set; }

        /// <param name="crearClase">"Nueva clase...": la crea y devuelve las clases activas de nuevo, o null si se cancelo.</param>
        /// <param name="hoyConFecha">Hoy segun el servidor si los cambios de horario aplican desde una fecha; si no, null (sin "A partir del").</param>
        /// <param name="desdePropuesto">Con hoyConFecha, lo que propone "A partir del" (el lunes de la semana que se ve); null o antes de hoy, hoy.</param>
        public DialogoAsignarClase(List<ClaseInfo> clasesActivas, OpcionCombo laboratorio, List<RangoHueco> rangos, Func<IWin32Window, List<ClaseInfo>> crearClase,
            DateTime? hoyConFecha, DateTime? desdePropuesto)
        {
            // Lo que ocupa "A partir del" con su nota, entre la clase y el error.
            int extra = hoyConFecha.HasValue ? 74 : 0;
            Text = TextosDelHorario.TituloPoner;
            BackColor = Color.White;
            ClientSize = new Size(560, 360 + extra);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            Label lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(20, 14),
                Text = "Poner una clase en las horas elegidas"
            };

            // "Se pone en 1 horario: miercoles de 09:00 a 11:00, todas las semanas."
            Label lblDescripcion = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(22, 48),
                Size = new Size(516, 58),
                Text = laboratorio.Texto + Environment.NewLine
                    + "Se pone en " + rangos.Count + (rangos.Count == 1 ? " horario: " : " horarios: ") + TextosDelHorario.Rangos(rangos)
                    + (hoyConFecha.HasValue ? ", cada semana desde la fecha de abajo." : ", todas las semanas.")
            };

            selector = new SelectorClase(clasesActivas, 516, crearClase) { Location = new Point(22, 112) };

            if (hoyConFecha.HasValue)
            {
                // Desde cuando valen: en una clase que ya empezo, si no, aparecerian
                // tambien en las semanas pasadas y Faltas las pintaria de rojo.
                Controls.Add(EstiloHorarios.CrearEtiqueta("A partir del", new Point(22, 252)));
                dtpDesde = EstiloHorarios.CrearFechaConDia(this, new Point(140, 248));
                dtpDesde.MinDate = hoyConFecha.Value;
                Label lblNotaDesde = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 8.5F),
                    ForeColor = Color.FromArgb(80, 80, 80),
                    Location = new Point(22, 282),
                    Size = new Size(516, 34)
                };
                Controls.Add(lblNotaDesde);
                // Sigue a la clase elegida: la fecha propuesta, o su primer dia si empieza despues.
                DateTime propuesta = desdePropuesto.HasValue && desdePropuesto.Value.Date > hoyConFecha.Value ? desdePropuesto.Value.Date : hoyConFecha.Value;
                Action ponerDesde = () =>
                {
                    ClaseInfo clase = selector.ClaseElegida;
                    dtpDesde.Value = DialogoFranja.DesdePorOmision(clase, propuesta);
                    lblNotaDesde.Text = DialogoFranja.NotaDesde(clase, hoyConFecha.Value);
                };
                selector.Cambio += ponerDesde;
                ponerDesde();
            }

            lblError = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(170, 40, 40),
                Location = new Point(22, 248 + extra),
                Size = new Size(516, 40),
                Text = selector.HayClases ? "" : "No hay clases activas. Crea la primera con «Nueva clase...»."
            };
            selector.Cambio += () => lblError.Text = "";

            Button btnAceptar = EstiloAdmin.BotonPrincipal("Poner la clase", new Point(263, 296 + extra), new Size(150, 40));
            btnAceptar.Click += (s, e) =>
            {
                ClaseInfo elegida = selector.ClaseElegida;
                if (elegida == null)
                {
                    lblError.Text = selector.ProfesorElegido ? "Elige cuál de sus clases." : "Elige primero a la persona docente y después su clase.";
                    return;
                }

                ClaseId = elegida.Id;
                Desde = dtpDesde == null ? (DateTime?)null : dtpDesde.Value.Date;
                DialogResult = DialogResult.OK;
            };

            Button btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(423, 296 + extra), new Size(115, 40));
            btnCancelar.Click += (s, e) => DialogResult = DialogResult.Cancel;

            AcceptButton = btnAceptar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(lblDescripcion);
            Controls.Add(selector);
            Controls.Add(lblError);
            Controls.Add(btnAceptar);
            Controls.Add(btnCancelar);
            ActiveControl = selector;
        }

        /// <summary>
        /// Con una clase ya elegida (Horario semanal, la traida de "Falta asignar"
        /// con «Ponerla...»): Enter la pone. No la usa la preparacion de Proximo ciclo.
        /// </summary>
        public void Preelegir(ClaseInfo clase)
        {
            if (clase != null)
            {
                selector.Seleccionar(clase);
                ActiveControl = AcceptButton as Control;
            }
        }
    }

    /// <summary>
    /// Elegir una clase en dos pasos: primero el profesor, con su color como en
    /// la cuadricula, y despues una de SUS clases (materia - grupo). Antes era un
    /// solo desplegable con todas las clases de todos los profesores: 68 con el
    /// horario real, y habia que buscar a ojo.
    /// </summary>
    internal class SelectorClase : Panel
    {
        private readonly ComboBox cboProfesor;
        private readonly ComboBox cboClase;
        private readonly Label lblOcultas;
        private readonly Func<IWin32Window, List<ClaseInfo>> crearClase;
        private List<ClaseInfo> clases;

        /// <summary>Cambio el profesor o la clase.</summary>
        public event Action Cambio;

        /// <param name="crearClase">Si no es null, sale el enlace "Nueva clase..." (ver DialogoAsignarClase).</param>
        public SelectorClase(List<ClaseInfo> clases, int ancho, Func<IWin32Window, List<ClaseInfo>> crearClase)
        {
            this.crearClase = crearClase;
            Size = new Size(ancho, 122);
            BackColor = Color.White;

            Controls.Add(EstiloHorarios.CrearEtiqueta("Docente", new Point(0, 0)));

            // Si se esconden clases vencidas, que se diga: una con la fecha de fin
            // mal puesta no desaparece sin rastro.
            lblOcultas = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.Gray,
                Location = new Point(ancho / 2, 3),
                Size = new Size(ancho - ancho / 2, 18),
                TextAlign = ContentAlignment.TopRight
            };
            Controls.Add(lblOcultas);
            this.clases = Utiles(clases);
            // Cada etiqueta a 6 px de su lista, y aire entre las dos (2026-10-31).
            cboProfesor = EstiloHorarios.CrearCombo(new Point(0, 26), ancho);
            cboProfesor.DrawMode = DrawMode.OwnerDrawFixed;
            cboProfesor.ItemHeight = 22;
            cboProfesor.MaxDropDownItems = 16;
            cboProfesor.DrawItem += DibujarProfesor;
            cboProfesor.SelectedIndexChanged += (s, e) => LlenarClases(null);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Su clase (materia - grupo)", new Point(0, 66)));
            cboClase = EstiloHorarios.CrearCombo(new Point(0, 92), ancho);
            cboClase.MaxDropDownItems = 16;
            cboClase.SelectedIndexChanged += (s, e) => AvisarCambio();

            if (crearClase != null)
            {
                LinkLabel lnkNueva = new LinkLabel
                {
                    AutoSize = true,
                    Font = new DrawingFont("Century Gothic", 9F),
                    LinkColor = Color.DarkGreen,
                    Text = "Nueva clase..."
                };
                lnkNueva.Location = new Point(ancho - TextRenderer.MeasureText(lnkNueva.Text, lnkNueva.Font).Width - 4, 68);
                lnkNueva.LinkClicked += (s, e) => CrearClase();
                Controls.Add(lnkNueva);
            }

            Controls.Add(cboProfesor);
            Controls.Add(cboClase);
            LlenarProfesores(null);
        }

        public bool HayClases
        {
            get { return clases.Count > 0; }
        }

        /// <summary>
        /// Deja elegida esa clase, y su profesor. Si no esta entre las que se
        /// ofrecen (inactiva o vencida, al editar una franja vieja), se anade.
        /// </summary>
        public void Seleccionar(ClaseInfo clase)
        {
            if (clase == null)
            {
                return;
            }

            if (!clases.Any(c => c.Id == clase.Id))
            {
                clases.Add(clase);
            }

            LlenarProfesores(clase.ProfesorId);
            LlenarClases(clase.Id);
        }

        public bool ProfesorElegido
        {
            get { return cboProfesor.SelectedItem != null; }
        }

        public ClaseInfo ClaseElegida
        {
            get
            {
                OpcionCombo opcion = cboClase.SelectedItem as OpcionCombo;
                return opcion == null ? null : clases.FirstOrDefault(c => c.Id == opcion.Id);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            cboClase.Enabled = Enabled && cboProfesor.SelectedItem != null;
        }

        /// <summary>
        /// Las vencidas no se ofrecen; las que aun no empiezan si (se prepara el
        /// semestre siguiente). Cuantas se esconden, en lblOcultas.
        /// </summary>
        private List<ClaseInfo> Utiles(List<ClaseInfo> todas)
        {
            List<ClaseInfo> lista = todas ?? new List<ClaseInfo>();
            List<ClaseInfo> utiles = lista.Where(c => c.VigenteHasta.Date >= DateTime.Today).ToList();
            int ocultas = lista.Count - utiles.Count;
            lblOcultas.Text = ocultas == 0 ? ""
                : ocultas == 1 ? "1 clase vencida no se muestra"
                : ocultas + " clases vencidas no se muestran";
            return utiles;
        }

        private void LlenarProfesores(int? profesor)
        {
            List<OpcionCombo> profesores = clases
                .GroupBy(c => c.ProfesorId)
                .Select(g => new OpcionCombo
                {
                    Id = g.Key,
                    Texto = g.First().Profesor + "   (" + g.Count() + (g.Count() == 1 ? " clase)" : " clases)")
                })
                .OrderBy(o => o.Texto, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            EstiloHorarios.Rellenar(cboProfesor, profesores, profesor);
            if (cboProfesor.SelectedIndex < 0)
            {
                LlenarClases(null);
            }
        }

        private void LlenarClases(int? clase)
        {
            OpcionCombo profesor = cboProfesor.SelectedItem as OpcionCombo;
            List<OpcionCombo> suyas = profesor == null
                ? new List<OpcionCombo>()
                : clases.Where(c => c.ProfesorId == profesor.Id)
                    .OrderBy(c => c.Materia, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(c => c.Grupo, StringComparer.CurrentCultureIgnoreCase)
                    .Select(c => new OpcionCombo { Id = c.Id, Texto = c.Materia + " - " + c.Grupo })
                    .ToList();
            EstiloHorarios.Rellenar(cboClase, suyas, clase);
            if (cboClase.SelectedIndex < 0 && suyas.Count == 1)
            {
                cboClase.SelectedIndex = 0;
            }

            cboClase.Enabled = Enabled && profesor != null;
            AvisarCambio();
        }

        private void AvisarCambio()
        {
            if (Cambio != null)
            {
                Cambio();
            }
        }

        private void CrearClase()
        {
            HashSet<int> antes = new HashSet<int>(clases.Select(c => c.Id));
            List<ClaseInfo> todas = crearClase(FindForm());
            if (todas == null)
            {
                return;
            }

            clases = Utiles(todas);
            ClaseInfo creada = clases.FirstOrDefault(c => !antes.Contains(c.Id));
            LlenarProfesores(creada == null ? (int?)null : creada.ProfesorId);
            if (creada != null)
            {
                LlenarClases(creada.Id);
            }
        }

        private void DibujarProfesor(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0)
            {
                return;
            }

            OpcionCombo opcion = (OpcionCombo)cboProfesor.Items[e.Index];
            Rectangle muestra = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 4, 16, e.Bounds.Height - 8);
            using (SolidBrush pincel = new SolidBrush(ColoresProfesor.De(opcion.Id)))
            {
                e.Graphics.FillRectangle(pincel, muestra);
            }

            using (Pen borde = new Pen(Color.FromArgb(130, 130, 130)))
            {
                e.Graphics.DrawRectangle(borde, muestra);
            }

            Rectangle zona = new Rectangle(muestra.Right + 6, e.Bounds.Y, e.Bounds.Width - muestra.Width - 14, e.Bounds.Height);
            // Mismo criterio que DrawBackground: fondo resaltado, texto blanco.
            bool resaltado = (e.State & DrawItemState.Selected) != 0;
            TextRenderer.DrawText(e.Graphics, opcion.Texto, e.Font, zona, resaltado ? SystemColors.HighlightText : SystemColors.ControlText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }
    }


    // =========================================================================
    // Dialogo: cambio de un dia sobre los cuadros elegidos en la cuadricula,
    // solo ese dia de la semana elegida (quitar esas horas, o una sesion extra
    // de una clase en ellas)
    // =========================================================================

    /// <summary>Un bloque seleccionado llevado a su fecha en la semana elegida.</summary>
    internal class ExcepcionPlaneada
    {
        public RangoHueco Rango;
        public DateTime Fecha;
    }

    internal class DialogoExcepcionCuadricula : Form
    {
        private readonly List<RangoHueco> rangos;
        private readonly List<FranjaInfo> franjasDelLaboratorio;
        private readonly DateTime ahora;
        private readonly Func<DateTime, List<ExcepcionInfo>> leerExcepciones;
        // Las excepciones del laboratorio por semana (su lunes): las de la semana
        // de la cuadricula vienen dadas; las de otra, se leen al ir a ella.
        private readonly Dictionary<DateTime, List<ExcepcionInfo>> excepcionesPorSemana = new Dictionary<DateTime, List<ExcepcionInfo>>();
        private readonly SelectorSemana selectorSemana;
        private readonly RadioButton rbCancelar;
        private readonly RadioButton rbExtra;
        private readonly SelectorClase selector;
        private readonly TextBox txtMotivo;
        private readonly TextBox txtResumen;
        private bool sinExcepciones;

        public List<ExcepcionPlaneada> Plan { get; private set; }
        public bool EsExtra { get; private set; }
        public int ClaseId { get; private set; }
        public string Motivo { get; private set; }

        /// <summary>La semana en la que se aplica (su lunes).</summary>
        public DateTime Lunes
        {
            get { return selectorSemana.Lunes; }
        }

        /// <param name="excepcionesDeLaSemana">Las del laboratorio en la semana de lunes (las que dibuja la cuadricula).</param>
        /// <param name="ahora">Reloj del servidor: lo que ya paso.</param>
        /// <param name="leerExcepciones">Las del laboratorio en la semana de ese lunes, si se cambia de semana aqui.</param>
        public DialogoExcepcionCuadricula(List<ClaseInfo> clasesActivas, OpcionCombo laboratorio, List<RangoHueco> rangos, List<FranjaInfo> franjasDelLaboratorio,
            List<ExcepcionInfo> excepcionesDeLaSemana, DateTime ahora, DateTime lunes, Func<DateTime, List<ExcepcionInfo>> leerExcepciones,
            Func<IWin32Window, List<ClaseInfo>> crearClase)
        {
            this.rangos = rangos;
            this.franjasDelLaboratorio = franjasDelLaboratorio ?? new List<FranjaInfo>();
            this.ahora = ahora;
            this.leerExcepciones = leerExcepciones;
            excepcionesPorSemana[CuadriculaSemanal.LunesDe(lunes)] = excepcionesDeLaSemana ?? new List<ExcepcionInfo>();

            Text = "Cambio de un día";
            BackColor = Color.White;
            ClientSize = new Size(640, 632);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            Label lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(20, 14),
                Text = "Cambio de un día en " + laboratorio.Texto
            };

            Label lblSemana = EstiloHorarios.CrearEtiqueta("Semana", new Point(22, 54));
            selectorSemana = new SelectorSemana { Location = new Point(100, 50), Hoy = ahora.Date, Lunes = lunes };
            selectorSemana.Cambio += () => Recalcular();

            Label lblAyuda = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.Gray,
                Location = new Point(22, 82),
                Size = new Size(596, 34),
                Text = "Solo ese día de esa semana: las demás semanas no cambian. A las clases que se crucen se les quita solo lo que "
                    + "se cruza; lo demás de su horario se queda (como sesión extra suya)."
            };

            rbCancelar = new RadioButton
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(22, 122),
                Text = "Quitar esas horas ese día (no habrá clase)"
            };
            rbExtra = new RadioButton
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(22, 148),
                Text = "Dar una sesión extra en esas horas",
                Checked = true
            };
            rbCancelar.CheckedChanged += (s, e) => Recalcular();
            rbExtra.CheckedChanged += (s, e) => Recalcular();

            selector = new SelectorClase(clasesActivas, 596, crearClase) { Location = new Point(22, 180) };
            selector.Cambio += () => Recalcular();

            Label lblMotivo = EstiloHorarios.CrearEtiqueta("Motivo (opcional)", new Point(22, 318));
            txtMotivo = new TextBox
            {
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(22, 344),
                Size = new Size(596, 26),
                MaxLength = 200
            };

            Label lblResumen = EstiloHorarios.CrearEtiqueta("Qué se va a hacer", new Point(22, 386));
            txtResumen = new TextBox
            {
                Font = new DrawingFont("Century Gothic", 9F),
                Location = new Point(22, 412),
                Size = new Size(596, 148),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(248, 248, 248)
            };

            Button btnAplicar = EstiloAdmin.BotonPrincipal("Hacer el cambio", new Point(331, 572), new Size(160, 40));
            btnAplicar.Click += (s, e) => Aceptar();
            Button btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(503, 572), new Size(115, 40));
            btnCancelar.Click += (s, e) => DialogResult = DialogResult.Cancel;

            AcceptButton = btnAplicar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(lblSemana);
            Controls.Add(selectorSemana);
            Controls.Add(lblAyuda);
            Controls.Add(rbCancelar);
            Controls.Add(rbExtra);
            Controls.Add(selector);
            Controls.Add(lblMotivo);
            Controls.Add(txtMotivo);
            Controls.Add(lblResumen);
            Controls.Add(txtResumen);
            Controls.Add(btnAplicar);
            Controls.Add(btnCancelar);

            Recalcular();
            // Con el foco en el selector de clase, Intro aplica (en la flecha de la semana, la movia).
            ActiveControl = selector;
        }

        /// <summary>Si esa clase (fecha y hora de fin) ya termino segun el reloj del servidor.</summary>
        private bool YaPaso(DateTime fecha, TimeSpan fin)
        {
            return fecha.Date < ahora.Date || (fecha.Date == ahora.Date && fin <= ahora.TimeOfDay);
        }

        private List<ExcepcionPlaneada> Planear()
        {
            DateTime lunes = selectorSemana.Lunes;
            return rangos
                .OrderBy(r => r.Dia).ThenBy(r => r.Inicio)
                .Select(r => new ExcepcionPlaneada { Rango = r, Fecha = lunes.AddDays(r.Dia - 1) })
                .ToList();
        }

        private List<ExcepcionInfo> ExcepcionesDeLaSemana()
        {
            DateTime lunes = selectorSemana.Lunes;
            List<ExcepcionInfo> lista;
            if (!excepcionesPorSemana.TryGetValue(lunes, out lista))
            {
                try
                {
                    lista = leerExcepciones == null ? new List<ExcepcionInfo>() : leerExcepciones(lunes) ?? new List<ExcepcionInfo>();
                    excepcionesPorSemana[lunes] = lista;
                }
                catch (Exception ex)
                {
                    // El resumen sale sin ellas (y lo dice); al aplicar, la base manda.
                    Log.Error("DialogoExcepcionCuadricula: leer las excepciones de la semana del " + lunes.ToString("dd/MM/yyyy"), ex);
                    lista = new List<ExcepcionInfo>();
                }
            }

            return lista;
        }

        /// <summary>Una sesion extra de ese dia en la simulacion: las que ya hay y las que irian quedando.</summary>
        private sealed class ExtraSimulada
        {
            public int ClaseId;
            public string Codigo;
            public string Materia;
            public string Grupo;
            public string Profesor;
            public TimeSpan Inicio;
            public TimeSpan Fin;
            public int Bitacoras;
            /// <summary>Las asistencias que dio su profesor en su sesion (db/22): con alguna, como con bitacoras.</summary>
            public int Dadas;

            public ExtraSimulada Trozo(TramoHorario tramo)
            {
                return new ExtraSimulada { ClaseId = ClaseId, Codigo = Codigo, Materia = Materia, Grupo = Grupo, Profesor = Profesor, Inicio = tramo.Inicio, Fin = tramo.Fin };
            }

            public string Describir()
            {
                return Codigo + " " + Materia + " - " + Grupo + " (" + Profesor + ", " + HorariosDatos.Hora(Inicio) + "-" + HorariosDatos.Hora(Fin) + ")";
            }
        }

        /// <summary>Lo que hay un dia en el laboratorio mientras se simulan los bloques: franjas canceladas y sesiones extra.</summary>
        private sealed class DiaSimulado
        {
            public readonly HashSet<int> Canceladas = new HashSet<int>();
            public readonly List<ExtraSimulada> Extras = new List<ExtraSimulada>();
        }

        /// <summary>Lo que pasaria con un bloque: las lineas del resumen, cuanto quita y, si no se aplicaria, por que.</summary>
        private sealed class BloqueSimulado
        {
            public ExcepcionPlaneada Plan;
            public readonly List<string> Lineas = new List<string>();
            public int AQuitar;
            /// <summary>Por que no se aplicaria nada de ese bloque, o null.</summary>
            public string Rechazo;

            public string Titulo
            {
                get
                {
                    return HorariosDatos.NombresDia[Plan.Rango.Dia] + " " + Plan.Fecha.ToString("dd/MM/yyyy") + ", de "
                        + HorariosDatos.Hora(Plan.Rango.Inicio) + " a " + HorariosDatos.Hora(Plan.Rango.Fin);
                }
            }
        }

        /// <summary>
        /// Lo que hara HorariosDatos.AplicarExcepcion con cada bloque y en el mismo
        /// orden en que se aplican: cada uno ve lo que dejo el anterior (dos cuadros
        /// sueltos sobre una misma clase la recortan dos veces). Un bloque no se
        /// aplicaria (Rechazo) si la extra ya paso o su clase no es vigente ese
        /// dia, o si habria que recortar una sesion extra que ya tiene bitacoras.
        /// </summary>
        private List<BloqueSimulado> Simular(ClaseInfo elegida)
        {
            List<ExcepcionInfo> excepciones = ExcepcionesDeLaSemana();
            Dictionary<DateTime, DiaSimulado> dias = new Dictionary<DateTime, DiaSimulado>();
            List<BloqueSimulado> bloques = new List<BloqueSimulado>();
            foreach (ExcepcionPlaneada p in Planear())
            {
                BloqueSimulado bloque = new BloqueSimulado { Plan = p };
                bloques.Add(bloque);
                TimeSpan inicio = p.Rango.Inicio;
                TimeSpan fin = p.Rango.Fin;
                DiaSimulado dia;
                if (!dias.TryGetValue(p.Fecha, out dia))
                {
                    dia = new DiaSimulado();
                    foreach (ExcepcionInfo x in excepciones.Where(x => x.Fecha.Date == p.Fecha))
                    {
                        if (x.EsExtra)
                        {
                            dia.Extras.Add(new ExtraSimulada
                            {
                                ClaseId = x.ClaseId, Codigo = x.Codigo, Materia = x.Materia, Grupo = x.Grupo, Profesor = x.Profesor,
                                Inicio = x.Inicio, Fin = x.Fin, Bitacoras = x.Bitacoras, Dadas = x.Dadas
                            });
                        }
                        else if (x.FranjaId.HasValue)
                        {
                            dia.Canceladas.Add(x.FranjaId.Value);
                        }
                    }

                    dias[p.Fecha] = dia;
                }

                bool paso = YaPaso(p.Fecha, fin);
                if (rbExtra.Checked && paso)
                {
                    bloque.Rechazo = "YA PASÓ: ahí no se puede poner una sesión extra (nadie la podría usar). Pasa a otra semana con ▶.";
                }
                else if (rbExtra.Checked && elegida != null && !elegida.VigenteEn(p.Fecha))
                {
                    bloque.Rechazo = "La clase elegida " + (elegida.Activa
                        ? "solo es vigente del " + elegida.VigenteDesde.ToString("dd/MM/yyyy") + " al " + elegida.VigenteHasta.ToString("dd/MM/yyyy")
                        : "está desactivada") + ": ese día sus estudiantes no podrían entrar a la sesión extra.";
                }

                List<ExtraSimulada> extrasQueSeCruzan = dia.Extras.Where(x => x.Inicio < fin && x.Fin > inicio).OrderBy(x => x.Inicio).ToList();
                ExtraSimulada usada = extrasQueSeCruzan.FirstOrDefault(x => x.Bitacoras > 0 || x.Dadas > 0);
                if (bloque.Rechazo == null && usada != null)
                {
                    bloque.Rechazo = "La sesión extra de " + usada.Describir() + " " + HorariosDatos.YaTieneRegistros(usada.Dadas)
                        + ": no se puede recortar. Elige otras horas.";
                }

                if (bloque.Rechazo != null)
                {
                    bloque.Lineas.Add(bloque.Rechazo + " En este bloque no se cambiaría nada.");
                    continue;
                }

                if (rbExtra.Checked)
                {
                    bloque.Lineas.Add(elegida == null ? "Entra la sesión extra de la clase que elijas arriba."
                        : "Entra la sesión extra de " + elegida.Codigo + " " + elegida.Materia + " - " + elegida.Grupo + " (" + elegida.Profesor + ").");
                }
                else if (paso)
                {
                    bloque.Lineas.Add("Ya pasó: lo que se quite queda como no dado (en Faltas sale cancelado, no en rojo).");
                }

                foreach (FranjaInfo f in franjasDelLaboratorio
                    .Where(f => f.DiaSemana == p.Rango.Dia && f.Activa && f.Inicio < fin && f.Fin > inicio
                        && p.Fecha >= f.VigenteDesde.Date && p.Fecha <= f.VigenteHasta.Date)
                    .OrderBy(f => f.Inicio))
                {
                    string quien = f.Codigo + " " + f.Materia + " - " + f.Grupo + " (" + f.Profesor + ", "
                        + HorariosDatos.Hora(f.Inicio) + "-" + HorariosDatos.Hora(f.Fin) + ")";
                    if (dia.Canceladas.Contains(f.Id))
                    {
                        bloque.Lineas.Add(quien + ": ya está cancelada ese día; se deja así.");
                        continue;
                    }

                    bloque.AQuitar++;
                    dia.Canceladas.Add(f.Id);
                    List<TramoHorario> restos = HorariosDatos.Restos(f.Inicio, f.Fin, inicio, fin);
                    foreach (TramoHorario resto in restos)
                    {
                        dia.Extras.Add(new ExtraSimulada
                        {
                            ClaseId = f.ClaseId, Codigo = f.Codigo, Materia = f.Materia, Grupo = f.Grupo, Profesor = f.Profesor,
                            Inicio = resto.Inicio, Fin = resto.Fin
                        });
                    }

                    bloque.Lineas.Add(restos.Count == 0 ? quien + ": se cancela ese día."
                        : quien + ": se le quita " + HorariosDatos.Cruce(f.Inicio, f.Fin, inicio, fin) + "; ese día da " + string.Join(" y ", restos) + ".");
                }

                foreach (ExtraSimulada x in extrasQueSeCruzan)
                {
                    bloque.AQuitar++;
                    List<TramoHorario> restos = HorariosDatos.Restos(x.Inicio, x.Fin, inicio, fin);
                    bloque.Lineas.Add(restos.Count == 0 ? "Sesión extra de " + x.Describir() + ": se quita."
                        : "Sesión extra de " + x.Describir() + ": se le quita " + HorariosDatos.Cruce(x.Inicio, x.Fin, inicio, fin)
                          + "; queda " + string.Join(" y ", restos) + ".");
                    dia.Extras.Remove(x);
                    dia.Extras.AddRange(restos.Select(x.Trozo));
                }

                if (rbExtra.Checked && elegida != null)
                {
                    dia.Extras.Add(new ExtraSimulada
                    {
                        ClaseId = elegida.Id, Codigo = elegida.Codigo, Materia = elegida.Materia, Grupo = elegida.Grupo, Profesor = elegida.Profesor,
                        Inicio = inicio, Fin = fin
                    });
                }

                if (bloque.AQuitar == 0 && !rbExtra.Checked)
                {
                    bloque.Lineas.Add("No hay clases en esas horas ese día: no hay nada que quitar.");
                }
            }

            return bloques;
        }

        private void Recalcular()
        {
            selector.Enabled = rbExtra.Checked;
            List<BloqueSimulado> bloques = Simular(rbExtra.Checked ? selector.ClaseElegida : null);
            sinExcepciones = !excepcionesPorSemana.ContainsKey(selectorSemana.Lunes);
            txtResumen.Text = string.Join(Environment.NewLine + Environment.NewLine,
                    bloques.Select(b => b.Titulo + ":" + Environment.NewLine + string.Join(Environment.NewLine, b.Lineas.Select(l => "   • " + l))))
                + (sinExcepciones ? Environment.NewLine + Environment.NewLine
                    + "(No se pudieron leer las cancelaciones y sesiones extra de esa semana: al hacer el cambio, la base dirá si algo choca.)" : "");
        }

        private void Aceptar()
        {
            ClaseInfo elegida = null;
            if (rbExtra.Checked)
            {
                elegida = selector.ClaseElegida;
                if (elegida == null)
                {
                    MessageBox.Show(selector.ProfesorElegido ? "Elige cuál de sus clases tiene la sesión extra." : "Elige a la persona docente y la clase de la sesión extra.",
                        "Clase requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            List<BloqueSimulado> bloques = Simular(elegida);
            BloqueSimulado rechazado = bloques.FirstOrDefault(b => b.Rechazo != null);
            if (rechazado != null)
            {
                MessageBox.Show(rechazado.Titulo + ":" + Environment.NewLine + rechazado.Rechazo, "No se puede hacer así",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!rbExtra.Checked && bloques.Sum(b => b.AQuitar) == 0 && !sinExcepciones)
            {
                MessageBox.Show("Esa semana no hay clases en esas horas: no hay nada que quitar. Si quieres ocupar el hueco, elige «Dar una sesión extra en esas horas».",
                    "Nada que quitar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (elegida != null)
            {
                ClaseId = elegida.Id;
            }

            Plan = bloques.Select(b => b.Plan).ToList();
            EsExtra = rbExtra.Checked;
            Motivo = txtMotivo.Text.Trim();
            DialogResult = DialogResult.OK;
        }
    }

    // =========================================================================
    // Los choques que ya tiene el horario en uso (decision 3 del 2026-10-25)
    // =========================================================================

    /// <summary>
    /// Dos horas del horario en uso que chocan una semana: el mismo dia, a horas
    /// que se cruzan, en laboratorios DISTINTOS, con la misma persona docente o
    /// con el mismo grupo (decision del 2026-10-31). Dos clases a la vez en el
    /// mismo laboratorio no son un choque: al ponerla, una reemplaza a la otra
    /// (db/25), y HorariosDatos.ValidarFranja no deja guardarlas encimadas. Es
    /// la regla de RevisionCiclo.Choques (la revision de Proximo ciclo); aqui,
    /// entre las que valen en la fecha de su dia en esa semana (clase activa,
    /// dentro de su vigencia). Cada par, una vez (A antes que B).
    /// </summary>
    internal class ChoqueEnUso
    {
        public FranjaInfo A;
        public FranjaInfo B;
        /// <summary>El dia de la semana en que chocan (lunes a domingo).</summary>
        public DateTime Fecha;
        public bool MismoProfesor;
        public bool MismoGrupo;

        /// <summary>Las horas que se cruzan.</summary>
        public TimeSpan Inicio
        {
            get { return A.Inicio > B.Inicio ? A.Inicio : B.Inicio; }
        }

        public TimeSpan Fin
        {
            get { return A.Fin < B.Fin ? A.Fin : B.Fin; }
        }

        /// <summary>"misma persona docente y mismo grupo", como ChoqueCiclo.Motivos.</summary>
        public string Motivos
        {
            get { return Enumerar(MismoProfesor, MismoGrupo); }
        }

        private static string Enumerar(bool profesor, bool grupo)
        {
            List<string> motivos = new List<string>();
            if (profesor) motivos.Add("misma persona docente");
            if (grupo) motivos.Add("mismo grupo");
            return RevisionCiclo.Enumerar(motivos);
        }

        /// <summary>
        /// Esas franjas sin las que en la semana de ese lunes estan canceladas el
        /// dia que les toca (excepciones: las de esa semana, de todos los
        /// laboratorios; las extra no cuentan): una clase que ese dia no se da no
        /// choca (2026-11-03: antes seguia contando en "Choques" y saliendo en rojo
        /// bajo su rayado). Para pasarselas a DeLaSemana.
        /// </summary>
        public static IEnumerable<FranjaInfo> SinLasCanceladas(IEnumerable<FranjaInfo> franjas, DateTime lunes, IEnumerable<ExcepcionInfo> excepciones)
        {
            DateTime primero = CuadriculaSemanal.LunesDe(lunes);
            HashSet<KeyValuePair<int, DateTime>> canceladas = new HashSet<KeyValuePair<int, DateTime>>((excepciones ?? Enumerable.Empty<ExcepcionInfo>())
                .Where(x => x != null && !x.EsExtra && x.FranjaId.HasValue)
                .Select(x => new KeyValuePair<int, DateTime>(x.FranjaId.Value, x.Fecha.Date)));
            return (franjas ?? Enumerable.Empty<FranjaInfo>())
                .Where(f => f != null && !canceladas.Contains(new KeyValuePair<int, DateTime>(f.Id, primero.AddDays(f.DiaSemana - 1))));
        }

        /// <summary>
        /// Los choques de la semana de ese lunes (de lunes a domingo) entre esas
        /// franjas, por fecha y hora. Una franja cuenta si su clase esta activa y
        /// la fecha de su dia esta en su vigencia (la de su clase recortada a la
        /// suya). Las canceladas ese dia se quitan antes (SinLasCanceladas).
        /// </summary>
        public static List<ChoqueEnUso> DeLaSemana(IEnumerable<FranjaInfo> franjas, DateTime lunes)
        {
            DateTime primero = CuadriculaSemanal.LunesDe(lunes);
            List<FranjaInfo> valen = (franjas ?? Enumerable.Empty<FranjaInfo>())
                .Where(f => f != null && f.DiaSemana >= 1 && f.DiaSemana <= 7 && f.Fin > f.Inicio && f.Activa)
                .Where(f =>
                {
                    DateTime fecha = primero.AddDays(f.DiaSemana - 1);
                    return fecha >= f.VigenteDesde.Date && fecha <= f.VigenteHasta.Date;
                })
                .OrderBy(f => f.DiaSemana).ThenBy(f => f.Inicio).ThenBy(f => f.Id)
                .ToList();

            List<ChoqueEnUso> choques = new List<ChoqueEnUso>();
            for (int i = 0; i < valen.Count; i++)
            {
                FranjaInfo a = valen[i];
                for (int j = i + 1; j < valen.Count; j++)
                {
                    FranjaInfo b = valen[j];
                    // En el mismo laboratorio no es un choque (se reemplaza al ponerla).
                    if (a.DiaSemana != b.DiaSemana || !(a.Inicio < b.Fin && a.Fin > b.Inicio) || a.LaboratorioId == b.LaboratorioId)
                    {
                        continue;
                    }

                    ChoqueEnUso choque = new ChoqueEnUso
                    {
                        A = a,
                        B = b,
                        Fecha = primero.AddDays(a.DiaSemana - 1),
                        MismoProfesor = a.ProfesorId == b.ProfesorId,
                        MismoGrupo = a.GrupoId == b.GrupoId
                    };
                    if (choque.MismoProfesor || choque.MismoGrupo)
                    {
                        choques.Add(choque);
                    }
                }
            }

            return choques.OrderBy(c => c.Fecha).ThenBy(c => c.Inicio).ThenBy(c => c.A.Laboratorio, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Por cada franja que choca, sus motivos juntos ("mismo grupo"): lo que dice en rojo la cuadricula.</summary>
        public static Dictionary<int, string> MotivosPorFranja(List<ChoqueEnUso> choques)
        {
            Dictionary<int, bool[]> marcas = new Dictionary<int, bool[]>();
            Action<FranjaInfo, ChoqueEnUso> anotar = (f, c) =>
            {
                bool[] m;
                if (!marcas.TryGetValue(f.Id, out m))
                {
                    m = new bool[2];
                    marcas[f.Id] = m;
                }

                m[0] |= c.MismoProfesor;
                m[1] |= c.MismoGrupo;
            };
            foreach (ChoqueEnUso choque in choques ?? new List<ChoqueEnUso>())
            {
                anotar(choque.A, choque);
                anotar(choque.B, choque);
            }

            return marcas.ToDictionary(m => m.Key, m => Enumerar(m.Value[0], m.Value[1]));
        }

        /// <summary>"Bases de Datos con ISC-5001 (Marta Velazco)".</summary>
        public static string Clase(FranjaInfo f)
        {
            return f.Materia + " con " + f.Grupo + " (" + (string.IsNullOrEmpty(f.ProfesorCorto) ? f.Profesor : f.ProfesorCorto) + ")";
        }
    }

    /// <summary>
    /// "Choques: N" de Horario semanal: cuales son (dia, horas, las dos clases con
    /// su laboratorio y por que chocan). "Ver en el horario" lleva al laboratorio
    /// del elegido. Solo lee: tambien en solo consulta.
    /// </summary>
    internal class DialogoChoquesDelHorario : Form
    {
        private readonly DataGridView dgvChoques;
        private readonly Button btnVer;

        /// <summary>El choque elegido al pulsar "Ver en el horario" (DialogResult.OK); null si se cerro.</summary>
        public ChoqueEnUso Elegido { get; private set; }

        public DialogoChoquesDelHorario(List<ChoqueEnUso> choques, DateTime lunes, int laboratorioActual)
        {
            // Tan ancho como deja la pantalla (hasta 1320): las clases y los laboratorios tienen nombres largos.
            int ancho = Math.Min(1320, Screen.FromPoint(Cursor.Position).WorkingArea.Width - 40);
            int alto = Math.Min(600, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 60);
            EstiloHorarios.PrepararDialogo(this, "Choques del horario", new Size(ancho, alto));
            DateTime domingo = lunes.AddDays(6);
            Label titulo = EstiloAdmin.TituloDialogo("Choques del horario en uso", ancho - 48);
            string texto = "Semana del " + lunes.ToString("dd/MM") + " al " + domingo.ToString("dd/MM/yyyy") + " (de lunes a domingo), en todos los laboratorios: "
                + choques.Count + (choques.Count == 1 ? " vez" : " veces") + " la misma persona docente o el mismo grupo tiene dos clases el mismo día y a la misma hora, "
                + "en dos laboratorios distintos (las clases canceladas ese día no cuentan). Corrígelos con «Cambiar horas...» o "
                + "«Quitar horas...» en el laboratorio de cada una; los de este laboratorio salen en rojo en la cuadrícula"
                + (choques.Any(c => c.Fecha.DayOfWeek == DayOfWeek.Sunday) ? " (los del domingo, en su columna, que sale cuando ese día tiene clases)." : ".");
            Label explicacion = PiezasConfiguracion.Texto(texto, new Point(24, 54), new Size(ancho - 48, 20), 9.5F, Color.FromArgb(50, 50, 50));
            explicacion.Height = PiezasConfiguracion.AltoDe(texto, explicacion.Font, explicacion.Width) + 4;

            int arribaLista = explicacion.Bottom + 8;
            dgvChoques = EstiloHorarios.CrearGrid(new Point(24, arribaLista), new Size(ancho - 48, alto - 56 - 12 - arribaLista));
            // Los nombres enteros, en dos renglones si hace falta, con aire arriba y abajo
            // (con AllCells el renglon se quedaba pegado al texto).
            dgvChoques.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            dgvChoques.DefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
            dgvChoques.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            dgvChoques.Columns.Add("Dia", "Día");
            dgvChoques.Columns.Add("Horas", "Horas");
            dgvChoques.Columns.Add("ClaseA", "Clase");
            dgvChoques.Columns.Add("LabA", "Laboratorio");
            dgvChoques.Columns.Add("ClaseB", "Choca con");
            dgvChoques.Columns.Add("LabB", "Laboratorio");
            dgvChoques.Columns.Add("Motivo", "Por qué");
            EstiloHorarios.Pesos(dgvChoques, 9, 9, 25, 14, 25, 14, 14);
            foreach (ChoqueEnUso c in choques)
            {
                int fila = dgvChoques.Rows.Add(
                    TextosDelHorario.Dia(((int)c.Fecha.DayOfWeek + 6) % 7 + 1) + " " + c.Fecha.ToString("dd/MM"),
                    HorariosDatos.Hora(c.Inicio) + "-" + HorariosDatos.Hora(c.Fin),
                    ChoqueEnUso.Clase(c.A),
                    c.A.Laboratorio,
                    ChoqueEnUso.Clase(c.B),
                    c.B.Laboratorio,
                    c.Motivos);
                dgvChoques.Rows[fila].Tag = c;
                // Los de este laboratorio, en rojo claro: son los que se ven en la cuadricula.
                // Elegidos siguen rojos (un poco mas oscuros, letra negra): en el verde
                // de la seleccion un choque se veia como si estuviera bien.
                if (c.A.LaboratorioId == laboratorioActual || c.B.LaboratorioId == laboratorioActual)
                {
                    DataGridViewCellStyle rojo = dgvChoques.Rows[fila].DefaultCellStyle;
                    rojo.BackColor = PiezasConfiguracion.FondoRojo;
                    rojo.SelectionBackColor = EstiloHorarios.Oscurecer(PiezasConfiguracion.FondoRojo, 0.12f);
                    rojo.SelectionForeColor = Color.Black;
                }
            }

            dgvChoques.SelectionChanged += (s, e) => btnVer.Enabled = dgvChoques.CurrentRow != null;
            dgvChoques.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0)
                {
                    Ver();
                }
            };

            Button cerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(ancho - 24 - 120, ClientSize.Height - 56), new Size(120, 40));
            cerrar.DialogResult = DialogResult.Cancel;
            btnVer = EstiloAdmin.BotonPrincipal("Ver en el horario", new Point(cerrar.Left - 10 - 180, ClientSize.Height - 56), new Size(180, 40));
            btnVer.Click += (s, e) => Ver();
            btnVer.Enabled = dgvChoques.CurrentRow != null;
            ToolTip ayuda = new ToolTip();
            Disposed += (s, e) => ayuda.Dispose();
            ayuda.SetToolTip(btnVer, "Lleva a Horario semanal, al laboratorio de la clase elegida (si es este, al de la otra), con su docente resaltado (doble clic).");

            Controls.Add(titulo);
            Controls.Add(explicacion);
            Controls.Add(dgvChoques);
            Controls.Add(btnVer);
            Controls.Add(cerrar);
            CancelButton = cerrar;
            ActiveControl = dgvChoques;
        }

        private void Ver()
        {
            Elegido = dgvChoques.CurrentRow == null ? null : dgvChoques.CurrentRow.Tag as ChoqueEnUso;
            if (Elegido != null)
            {
                DialogResult = DialogResult.OK;
            }
        }
    }
}
