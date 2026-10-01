using DigBit.conexion;
using DigBit.Infraestructura;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

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
        private Label lblSubtitulo;
        private Button btnCerrar;
        private TabControl tabs;
        private TabPage tabClases;
        private TabPage tabHorario;
        private TabPage tabExcepciones;

        // Pestana Clases
        private DataGridView dgvClases;
        private Button btnClaseNueva;
        private Button btnClaseEditar;
        private Button btnClaseEstado;
        private Button btnClaseActualizar;
        private Label lblClasesCantidad;

        // Pestana Horario por laboratorio
        private ComboBox cboLabHorario;
        private Label lblLeyendaHorario;
        private Panel contenedorSemana;
        private CuadriculaSemanal lienzo;
        private ContextMenuStrip menuFranja;
        private ContextMenuStrip menuHueco;
        private ContextMenuStrip menuExtra;
        private ExcepcionInfo extraEnMenu;
        private Button btnAsignar;
        private Button btnExcepcion;


        private Button btnFranjaEditar;
        private Button btnFranjaBorrar;


        // Pestana Excepciones
        private ComboBox cboPeriodo;
        private CalendarioFiltro calendario;
        private ComboBox cboLabExcepciones;
        private Label lblDiaAgenda;
        private DataGridView dgvAgenda;
        private Button btnAgendaActualizar;
        private Button btnCancelarDia;
        private Button btnQuitarCancelacion;
        private Button btnSesionExtra;
        private Button btnQuitarExtra;

        // El ultimo laboratorio que se miro en esta sesion: al volver a abrir
        // Horarios se sigue donde se quedo, en vez de en el primero de la lista.
        private static int ultimoLaboratorio;

        private readonly int laboratorioInicial;
        private readonly ToolTip ayuda = new ToolTip();

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
            if (laboratorioInicial != 0)
            {
                tabs.SelectedTab = tabHorario;
            }
        }

        /// <summary>Lleva la ventana ya abierta al horario semanal de ese laboratorio.</summary>
        public void MostrarLaboratorio(int idLaboratorio)
        {
            tabs.SelectedTab = tabHorario;
            SeleccionarPorId(cboLabHorario, idLaboratorio);
        }

        // =====================================================================
        // Construccion de la interfaz
        // =====================================================================

        private void InicializarComponentes()
        {
            BackColor = Color.White;
            ClientSize = new Size(1000, 640);
            MinimumSize = new Size(1016, 700);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Horarios por laboratorio";

            lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(28, 14),
                Text = "Horarios por laboratorio"
            };

            lblSubtitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(30, 48),
                Text = "Clases con codigo fijo, franjas semanales de cada laboratorio y excepciones por dia."
            };

            btnCerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(850, 16), new Size(130, 38));
            btnCerrar.Click += (s, e) => Close();
            btnCerrar.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            tabs = new TabControl
            {
                Location = new Point(20, 76),
                Size = new Size(960, 548),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold)
            };
            tabs.SelectedIndexChanged += tabs_SelectedIndexChanged;

            tabClases = new TabPage("Clases") { BackColor = Color.White };
            tabHorario = new TabPage("Horario por laboratorio") { BackColor = Color.White };
            tabExcepciones = new TabPage("Excepciones") { BackColor = Color.White };

            ConstruirPestanaClases();
            ConstruirPestanaHorario();
            ConstruirPestanaExcepciones();

            tabs.TabPages.Add(tabClases);
            tabs.TabPages.Add(tabHorario);
            tabs.TabPages.Add(tabExcepciones);

            Controls.Add(lblTitulo);
            Controls.Add(lblSubtitulo);
            Controls.Add(btnCerrar);
            Controls.Add(tabs);
        }

        private void ConstruirPestanaClases()
        {
            dgvClases = EstiloHorarios.CrearGrid(new Point(12, 12), new Size(928, 440));
            dgvClases.Columns.Add("Codigo", "Codigo");
            dgvClases.Columns.Add("Materia", "Materia");
            dgvClases.Columns.Add("Grupo", "Grupo");
            dgvClases.Columns.Add("Profesor", "Profesor");
            dgvClases.Columns.Add("Desde", "Vigente desde");
            dgvClases.Columns.Add("Hasta", "Vigente hasta");
            dgvClases.Columns.Add("Activa", "Activa");
            dgvClases.Columns.Add("Sesiones", "Sesiones");
            EstiloHorarios.Pesos(dgvClases, 9, 22, 10, 24, 13, 13, 8, 9);
            dgvClases.SelectionChanged += (s, e) => ActualizarBotonesClases();
            dgvClases.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditarClase(); };

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

            btnClaseNueva = EstiloHorarios.CrearBoton("Nueva", new Point(524, 462), new Size(120, 38));
            btnClaseNueva.Click += (s, e) => NuevaClase();

            btnClaseEditar = EstiloHorarios.CrearBoton("Editar", new Point(656, 462), new Size(120, 38));
            btnClaseEditar.Click += (s, e) => EditarClase();

            btnClaseEstado = EstiloHorarios.CrearBoton("Activar/Desactivar", new Point(788, 462), new Size(152, 38));
            btnClaseEstado.Click += (s, e) => CambiarEstadoClase();

            tabClases.Controls.Add(dgvClases);
            tabClases.Controls.Add(lblClasesCantidad);
            tabClases.Controls.Add(btnClaseActualizar);
            tabClases.Controls.Add(btnClaseNueva);
            tabClases.Controls.Add(btnClaseEditar);
            tabClases.Controls.Add(btnClaseEstado);
        }

        /// <summary>
        /// La ventana abre con su tamano normal y se puede agrandar o maximizar:
        /// las cuadriculas crecen con ella (la semanal hasta un alto de fila
        /// comodo; no hace falta que llene la pantalla).
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            AnclarPestanas();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ayuda.Dispose();
            base.OnFormClosed(e);
        }

        private void AnclarPestanas()
        {
            foreach (TabPage pagina in new[] { tabClases, tabExcepciones })
            {
                foreach (Control control in pagina.Controls)
                {
                    if (control is DataGridView)
                    {
                        control.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                    }
                    else if (control is Button && control.Top > 400)
                    {
                        control.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                    }
                    else if (control is Button && control.Left > 700)
                    {
                        control.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    }
                }
            }
        }

        private void ConstruirPestanaHorario()
        {
            Label lblLab = EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(12, 16));

            cboLabHorario = EstiloHorarios.CrearCombo(new Point(110, 12), 250);
            cboLabHorario.SelectedIndexChanged += (s, e) =>
            {
                OpcionCombo elegido = cboLabHorario.SelectedItem as OpcionCombo;
                if (elegido != null)
                {
                    ultimoLaboratorio = elegido.Id;
                }

                CargarFranjas();
            };

            // Botones en la misma fila del selector: la cuadricula se queda con
            // todo el alto de la pestana.
            btnAsignar = EstiloHorarios.CrearBoton("Asignar clase", new Point(380, 8), new Size(150, 34));
            btnAsignar.Click += (s, e) => AsignarClaseASeleccion();

            btnExcepcion = EstiloHorarios.CrearBoton("Aplicar excepcion", new Point(540, 8), new Size(170, 34));
            btnExcepcion.Click += (s, e) => AplicarExcepcionASeleccion();

            btnFranjaEditar = EstiloHorarios.CrearBoton("Editar", new Point(720, 8), new Size(105, 34));
            btnFranjaEditar.Click += (s, e) => EditarFranja();

            btnFranjaBorrar = EstiloHorarios.CrearBoton("Borrar", new Point(835, 8), new Size(105, 34));
            btnFranjaBorrar.Click += (s, e) => BorrarFranja();

            // Una linea corta que cabe a cualquier ancho; la explicacion entera, al
            // pasar el raton (antes era una sola linea que se cortaba a la derecha).
            lblLeyendaHorario = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(12, 48),
                Size = new Size(928, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = "Arrastra o usa Ctrl+clic para elegir cuadros; clic en una franja para sus opciones. "
                    + "Color: el profesor. Rayado naranja: sesión extra. Rayado gris: cancelada."
            };
            ayuda.SetToolTip(lblLeyendaHorario,
                "Un cuadro por hora. Arrastra o usa Ctrl+clic para seleccionar cuadros (libres u ocupados, en uno o varios días)\n"
                + "y asígnales una clase o aplica una excepción. Clic en una franja: sus opciones.\n"
                + "El color es el del profesor. Rayado naranja: sesión extra; rayado gris: franja cancelada\n"
                + "(próximos 7 días, hasta que pase su hora).");

            contenedorSemana = new Panel
            {
                Location = new Point(12, 68),
                Size = new Size(928, 430),
                AutoScroll = false,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            tabHorario.SizeChanged += (s, e) => AjustarAltoCuadricula();
            Shown += (s, e) => AjustarAltoCuadricula();

            lienzo = new CuadriculaSemanal { Dock = DockStyle.Fill };
            lienzo.FranjaClic += lienzo_FranjaClic;
            lienzo.FranjaDobleClic += (f) => EditarFranja();
            lienzo.HuecosSeleccionados += lienzo_HuecosSeleccionados;
            lienzo.HuecoDobleClic += lienzo_HuecoDobleClic;
            lienzo.ExcepcionClic += lienzo_ExcepcionClic;
            contenedorSemana.Controls.Add(lienzo);

            // Menus de opciones de la cuadricula.
            menuFranja = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuFranja.Items.Add("Editar franja...", null, (s, e) => EditarFranja());
            menuFranja.Items.Add("Borrar franja", null, (s, e) => BorrarFranja());
            menuFranja.Items.Add(new ToolStripSeparator());
            menuFranja.Items.Add("Cancelar el proximo dia...", null, (s, e) => CancelarProximoDia());
            menuFranja.Items.Add("Quitar la cancelacion...", null, (s, e) => QuitarCancelacionDeFranja());
            menuFranja.Items.Add("Nueva franja en este laboratorio...", null, (s, e) => NuevaFranja(null, null));

            menuExtra = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuExtra.Items.Add("Quitar esta sesion extra", null, (s, e) => QuitarExtraDeCuadricula());
            menuExtra.Items.Add("Ver ese dia en Excepciones", null, (s, e) => { if (extraEnMenu != null) MostrarExcepcionesDe(LaboratorioHorario(), extraEnMenu.Fecha); });

            menuHueco = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuHueco.Items.Add("Asignar clase a la seleccion...", null, (s, e) => AsignarClaseASeleccion());
            menuHueco.Items.Add("Aplicar excepcion en la seleccion...", null, (s, e) => AplicarExcepcionASeleccion());
            menuHueco.Items.Add(new ToolStripSeparator());
            menuHueco.Items.Add("Quitar seleccion", null, (s, e) => lienzo.LimpiarSeleccion());

            tabHorario.Controls.Add(lblLab);
            tabHorario.Controls.Add(cboLabHorario);
            tabHorario.Controls.Add(btnAsignar);
            tabHorario.Controls.Add(btnExcepcion);
            tabHorario.Controls.Add(btnFranjaEditar);
            tabHorario.Controls.Add(btnFranjaBorrar);
            tabHorario.Controls.Add(lblLeyendaHorario);
            tabHorario.Controls.Add(contenedorSemana);
        }

        /// <summary>La cuadricula ocupa todo el alto que quede en la pestana.</summary>
        private void AjustarAltoCuadricula()
        {
            if (contenedorSemana == null || tabHorario == null)
            {
                return;
            }

            // La cuadricula usa el alto que queda, pero no mas del que necesitan
            // 14 filas de alto comodo: con la ventana maximizada no se desborda.
            int disponible = tabHorario.ClientSize.Height - contenedorSemana.Top - 10;
            int alto = Math.Min(disponible, CuadriculaSemanal.AltoPara(disponible));
            if (alto > 120)
            {
                contenedorSemana.Height = alto;
            }

            int ancho = Math.Min(tabHorario.ClientSize.Width - 24, 1400);
            if (ancho > 300)
            {
                contenedorSemana.Width = ancho;
            }
        }

        private void ConstruirPestanaExcepciones()
        {
            // Filtro de periodo: un dia (agenda completa, para cancelar una clase)
            // o una semana / un mes (solo las excepciones, para revisarlas sin buscar).
            Label lblPeriodo = EstiloHorarios.CrearEtiqueta("Ver", new Point(12, 16));

            cboPeriodo = EstiloHorarios.CrearCombo(new Point(58, 12), 110);
            cboPeriodo.Items.Add("Dia");
            cboPeriodo.Items.Add("Semana");
            cboPeriodo.Items.Add("Mes");
            cboPeriodo.SelectedIndex = 0;
            cboPeriodo.SelectedIndexChanged += (s, e) =>
            {
                calendario.Modo = (ModoCalendario)cboPeriodo.SelectedIndex;
                CargarAgenda();
            };

            // Calendario propio a la izquierda: siempre visible, con los dias que
            // no se pueden elegir sombreados segun el modo (en Semana solo los
            // lunes; en Mes ninguno, solo se cambia de mes con las flechas).
            calendario = new CalendarioFiltro
            {
                Location = new Point(12, 50),
                Size = new Size(240, 228)
            };
            calendario.SeleccionCambiada += (f) => CargarAgenda();

            Label lblLab = EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(190, 16));

            cboLabExcepciones = EstiloHorarios.CrearCombo(new Point(292, 12), 240);
            cboLabExcepciones.SelectedIndexChanged += (s, e) => CargarAgenda();

            lblDiaAgenda = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(264, 52),
                Size = new Size(676, 20),
                Text = ""
            };

            btnAgendaActualizar = EstiloHorarios.CrearBoton("Actualizar", new Point(820, 8), new Size(120, 34));
            btnAgendaActualizar.Click += (s, e) => CargarAgenda();

            dgvAgenda = EstiloHorarios.CrearGrid(new Point(264, 78), new Size(676, 374));
            dgvAgenda.Columns.Add("Fecha", "Fecha");
            dgvAgenda.Columns.Add("Inicio", "Inicio");
            dgvAgenda.Columns.Add("Fin", "Fin");
            dgvAgenda.Columns.Add("Codigo", "Codigo");
            dgvAgenda.Columns.Add("Materia", "Materia");
            dgvAgenda.Columns.Add("Grupo", "Grupo");
            dgvAgenda.Columns.Add("Profesor", "Profesor");
            dgvAgenda.Columns.Add("Estado", "Estado");
            EstiloHorarios.Pesos(dgvAgenda, 11, 7, 7, 9, 20, 9, 19, 18);
            dgvAgenda.SelectionChanged += (s, e) => ActualizarBotonesAgenda();

            btnCancelarDia = EstiloHorarios.CrearBoton("Cancelar este dia", new Point(12, 462), new Size(170, 38));
            btnCancelarDia.Click += (s, e) => CancelarFranjaDelDia();

            btnQuitarCancelacion = EstiloHorarios.CrearBoton("Quitar cancelacion", new Point(194, 462), new Size(170, 38));
            btnQuitarCancelacion.Click += (s, e) => QuitarCancelacion();

            btnSesionExtra = EstiloHorarios.CrearBoton("Sesion extra", new Point(600, 462), new Size(164, 38));
            btnSesionExtra.Click += (s, e) => NuevaSesionExtra();

            btnQuitarExtra = EstiloHorarios.CrearBoton("Quitar extra", new Point(776, 462), new Size(164, 38));
            btnQuitarExtra.Click += (s, e) => QuitarExtra();

            tabExcepciones.Controls.Add(lblPeriodo);
            tabExcepciones.Controls.Add(cboPeriodo);
            tabExcepciones.Controls.Add(calendario);
            tabExcepciones.Controls.Add(lblLab);
            tabExcepciones.Controls.Add(cboLabExcepciones);
            tabExcepciones.Controls.Add(lblDiaAgenda);
            tabExcepciones.Controls.Add(btnAgendaActualizar);
            tabExcepciones.Controls.Add(dgvAgenda);
            tabExcepciones.Controls.Add(btnCancelarDia);
            tabExcepciones.Controls.Add(btnQuitarCancelacion);
            tabExcepciones.Controls.Add(btnSesionExtra);
            tabExcepciones.Controls.Add(btnQuitarExtra);
        }

        // =====================================================================
        // Comun
        // =====================================================================

        private void tabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Cada pestana se refresca al entrar: lo que se cambia en una afecta a las otras.
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
                Log.Error("Horarios: leer laboratorios", ex);
                MessageBox.Show("No se pudieron leer los laboratorios: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private List<ClaseInfo> ClasesActivas()
        {
            try
            {
                return datos.ObtenerClases(true);
            }
            catch (Exception ex)
            {
                Log.Error("AdministrarHorarios.ClasesActivas", ex);
                MessageBox.Show("No se pudieron leer las clases activas: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new List<ClaseInfo>();
            }
        }

        private DateTime HoyServidor()
        {
            try
            {
                return consultas.AhoraServidor().Date;
            }
            catch
            {
                return DateTime.Today;
            }
        }

        private static void MostrarError(string contexto, string mensaje, Exception ex)
        {
            Log.Error(contexto, ex);
            MessageBox.Show(mensaje + Environment.NewLine + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // =====================================================================
        // Pestana Clases
        // =====================================================================

        private void CargarClases()
        {
            List<ClaseInfo> clases;
            try
            {
                clases = datos.ObtenerClases(false);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.CargarClases", "No se pudieron leer las clases.", ex);
                clases = new List<ClaseInfo>();
            }

            ClaseInfo anterior = ClaseSeleccionada();
            dgvClases.Rows.Clear();
            foreach (ClaseInfo clase in clases)
            {
                int fila = dgvClases.Rows.Add(
                    clase.Codigo,
                    clase.Materia,
                    clase.Grupo,
                    clase.Profesor,
                    clase.VigenteDesde.ToString("dd/MM/yyyy"),
                    clase.VigenteHasta.ToString("dd/MM/yyyy"),
                    clase.Activa ? "Si" : "No",
                    clase.Sesiones);
                dgvClases.Rows[fila].Tag = clase;

                // La celda del profesor lleva su color, el mismo con el que se
                // pintan sus franjas en la cuadricula del horario.
                Color color = CuadriculaSemanal.ColorDeProfesor(clase.ProfesorId);
                DataGridViewCell celdaProfesor = dgvClases.Rows[fila].Cells[3];
                celdaProfesor.Style.BackColor = color;
                celdaProfesor.Style.SelectionBackColor = EstiloHorarios.Oscurecer(color, 0.18f);
                celdaProfesor.Style.SelectionForeColor = Color.Black;

                if (!clase.Activa)
                {
                    dgvClases.Rows[fila].DefaultCellStyle.ForeColor = Color.Gray;
                }
            }

            // Corto a proposito: a su derecha empiezan los botones.
            lblClasesCantidad.Text = clases.Count + (clases.Count == 1 ? " clase" : " clases") + " (color: su profesor)";
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

        private void ActualizarBotonesClases()
        {
            ClaseInfo clase = ClaseSeleccionada();
            btnClaseEditar.Enabled = clase != null;
            btnClaseEstado.Enabled = clase != null;
            btnClaseEstado.Text = clase == null ? "Activar/Desactivar" : (clase.Activa ? "Desactivar" : "Activar");
        }

        private void NuevaClase()
        {
            using (DialogoClase dialogo = new DialogoClase(datos, consultas, null, IdAdministrador()))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    CargarClases();
                }
            }
        }

        private void EditarClase()
        {
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
            ClaseInfo clase = ClaseSeleccionada();
            if (clase == null)
            {
                MessageBox.Show("Selecciona una clase.", "Clase requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pregunta = clase.Activa
                ? "¿Desactivar la clase " + clase.Codigo + " (" + clase.Descripcion + ")?" + Environment.NewLine
                  + "Su código dejará de entrar y sus franjas no se mostrarán a los alumnos. Las bitácoras se conservan."
                : "¿Activar de nuevo la clase " + clase.Codigo + " (" + clase.Descripcion + ")?";
            if (MessageBox.Show(pregunta, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                datos.CambiarEstadoClase(clase.Id, !clase.Activa);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.CambiarEstadoClase", "No se pudo cambiar el estado de la clase.", ex);
            }

            CargarClases();
        }

        // =====================================================================
        // Pestana Horario por laboratorio
        // =====================================================================

        private OpcionCombo LaboratorioHorario()
        {
            return cboLabHorario.SelectedItem as OpcionCombo;
        }

        private void CargarFranjas()
        {
            OpcionCombo laboratorio = LaboratorioHorario();
            List<FranjaInfo> franjas = new List<FranjaInfo>();
            if (laboratorio != null)
            {
                try
                {
                    franjas = datos.ObtenerFranjasDeLaboratorio(laboratorio.Id);
                }
                catch (Exception ex)
                {
                    MostrarError("AdministrarHorarios.CargarFranjas", "No se pudo leer el horario del laboratorio.", ex);
                }
            }

            // Excepciones de los proximos 7 dias, para dibujarlas encima.
            List<ExcepcionInfo> excepciones = new List<ExcepcionInfo>();
            DateTime ahora = DateTime.Now;
            if (laboratorio != null)
            {
                try
                {
                    ahora = consultas.AhoraServidor();
                    excepciones = datos.ObtenerExcepciones(ahora.Date, ahora.Date.AddDays(7), laboratorio.Id);
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.CargarFranjas (excepciones)", ex);
                }
            }

            FranjaInfo anterior = FranjaSeleccionada();
            lienzo.Ahora = ahora;
            lienzo.Hoy = ahora.Date;
            lienzo.Franjas = franjas;
            lienzo.Excepciones = excepciones;
            lienzo.Seleccionada = anterior == null ? null : franjas.FirstOrDefault(f => f.Id == anterior.Id);
            ActualizarBotonesFranjas();
        }

        private void lienzo_ExcepcionClic(ExcepcionInfo extra)
        {
            extraEnMenu = extra;
            menuExtra.Items[0].Text = "Quitar la sesion extra de " + extra.Codigo + " del " + extra.Fecha.ToString("dd/MM") + " (" + HorariosDatos.Hora(extra.Inicio) + "-" + HorariosDatos.Hora(extra.Fin) + ")";
            menuExtra.Show(Cursor.Position);
        }

        /// <summary>Quita la sesion extra sobre la que se hizo clic en la cuadricula.</summary>
        private void QuitarExtraDeCuadricula()
        {
            if (extraEnMenu == null)
            {
                return;
            }

            if (MessageBox.Show("¿Quitar la sesion extra de " + extraEnMenu.Codigo + " del " + extraEnMenu.Fecha.ToString("dd/MM/yyyy") + "?", "Quitar sesion extra",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                datos.BorrarExcepcion(extraEnMenu.Id);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarExtraDeCuadricula", "No se pudo quitar la sesion extra.", ex);
            }

            CargarFranjas();
        }

        private void QuitarCancelacionDeFranja()
        {
            FranjaInfo franja = FranjaSeleccionada();
            ExcepcionInfo cancelacion = lienzo.CancelacionDe(franja);
            if (cancelacion == null)
            {
                MessageBox.Show("Esa franja no tiene una cancelacion pendiente.", "Sin cancelacion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                datos.BorrarExcepcion(cancelacion.Id);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarCancelacionDeFranja", "No se pudo quitar la cancelacion.", ex);
            }

            CargarFranjas();
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

        private void ActualizarBotonesFranjas()
        {
            bool hayLaboratorio = LaboratorioHorario() != null;
            bool hay = FranjaSeleccionada() != null;
            btnAsignar.Enabled = hayLaboratorio;
            btnExcepcion.Enabled = hayLaboratorio;
            btnFranjaEditar.Enabled = hay;
            btnFranjaBorrar.Enabled = hay;
        }

        private void lienzo_FranjaClic(FranjaInfo franja)
        {
            ActualizarBotonesFranjas();
            if (franja != null)
            {
                MostrarMenuFranja(franja);
            }
        }

        private void lienzo_HuecoDobleClic(int dia, TimeSpan hora)
        {
            NuevaFranja(dia, hora);
        }

        /// <summary>Se soltaron uno o varios cuadros libres: menu para asignarles una clase.</summary>
        private void lienzo_HuecosSeleccionados(List<RangoHueco> rangos)
        {
            if (LaboratorioHorario() == null || rangos.Count == 0)
            {
                return;
            }

            string resumen = string.Join(", ", rangos.Select(r => r.Descripcion));
            if (resumen.Length > 60)
            {
                resumen = resumen.Substring(0, 57) + "...";
            }

            bool hayLibres = lienzo.ObtenerHuecosSeleccionados().Count > 0;
            menuHueco.Items[0].Text = "Asignar clase a " + resumen + "...";
            menuHueco.Items[0].Enabled = hayLibres;
            menuHueco.Items[1].Text = "Aplicar excepcion en " + resumen + "...";
            menuHueco.Show(Cursor.Position);
        }

        /// <summary>
        /// Convierte cada bloque seleccionado en una franja de la clase elegida.
        /// Los conflictos (mismo laboratorio, profesor o grupo) se informan por
        /// bloque; los demas se crean.
        /// </summary>
        private void AsignarClaseASeleccion()
        {
            OpcionCombo laboratorio = LaboratorioHorario();
            if (laboratorio == null)
            {
                MessageBox.Show("Selecciona un laboratorio.", "Laboratorio requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<RangoHueco> rangos = lienzo.ObtenerHuecosSeleccionados();
            if (rangos.Count == 0)
            {
                MessageBox.Show("Selecciona uno o varios cuadros libres en la cuadricula (arrastra o usa Ctrl+clic).", "Sin seleccion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int claseId;
            using (DialogoAsignarClase dialogo = new DialogoAsignarClase(ClasesActivas(), laboratorio, rangos))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                claseId = dialogo.ClaseId;
            }

            int creadas = 0;
            List<string> problemas = new List<string>();
            foreach (RangoHueco rango in rangos)
            {
                try
                {
                    datos.CrearFranja(claseId, laboratorio.Id, rango.Dia, rango.Inicio, rango.Fin);
                    creadas++;
                }
                catch (InvalidOperationException ex)
                {
                    problemas.Add(rango.Descripcion + ": " + ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Error("AdministrarHorarios.AsignarClaseASeleccion", ex);
                    problemas.Add(rango.Descripcion + ": " + ex.Message);
                }
            }

            CargarFranjas();
            if (problemas.Count > 0)
            {
                MessageBox.Show((creadas > 0 ? creadas + " franja(s) creada(s). " : "") + "No se pudo asignar:" + Environment.NewLine + Environment.NewLine
                    + string.Join(Environment.NewLine, problemas), "Conflictos de horario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Aplica una excepcion sobre los cuadros seleccionados, esten libres u
        /// ocupados: o se cancelan las franjas de esas horas en una fecha, o se
        /// programa una sesion extra de otra clase. Si la extra choca con franjas
        /// del horario se aplica igual y esas franjas quedan canceladas ese dia
        /// (ya se hablo con esos maestros). Al terminar muestra la pestana
        /// Excepciones en esa fecha para ver el resultado.
        /// </summary>
        private void AplicarExcepcionASeleccion()
        {
            OpcionCombo laboratorio = LaboratorioHorario();
            if (laboratorio == null)
            {
                MessageBox.Show("Selecciona un laboratorio.", "Laboratorio requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<RangoHueco> rangos = lienzo.ObtenerRangosSeleccionados(false);
            if (rangos.Count == 0)
            {
                MessageBox.Show("Selecciona uno o varios cuadros en la cuadricula (arrastra o usa Ctrl+clic; pueden estar ocupados por una franja).",
                    "Sin seleccion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<ExcepcionPlaneada> plan;
            bool esExtra;
            int claseId;
            string motivo;
            using (DialogoExcepcionCuadricula dialogo = new DialogoExcepcionCuadricula(ClasesActivas(), laboratorio, rangos, lienzo.Franjas, HoyServidor()))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                plan = dialogo.Plan;
                esExtra = dialogo.EsExtra;
                claseId = dialogo.ClaseId;
                motivo = dialogo.Motivo;
            }

            int canceladas = 0;
            int extras = 0;
            List<string> problemas = new List<string>();
            int administrador = IdAdministrador();
            foreach (ExcepcionPlaneada p in plan)
            {
                foreach (FranjaInfo franja in p.Conflictos)
                {
                    try
                    {
                        datos.CancelarFranja(franja.Id, p.Fecha, motivo, administrador);
                        canceladas++;
                    }
                    catch (InvalidOperationException)
                    {
                        // Ya estaba cancelada ese dia.
                    }
                    catch (Exception ex)
                    {
                        problemas.Add("Cancelar " + franja.Codigo + " el " + p.Fecha.ToString("dd/MM") + ": " + ex.Message);
                    }
                }

                if (esExtra)
                {
                    try
                    {
                        datos.CrearExtra(claseId, laboratorio.Id, p.Fecha, p.Rango.Inicio, p.Rango.Fin, motivo, administrador, true);
                        extras++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("AdministrarHorarios.AplicarExcepcionASeleccion", ex);
                        problemas.Add(p.Rango.Descripcion + " (" + p.Fecha.ToString("dd/MM") + "): " + ex.Message);
                    }
                }
            }

            lienzo.LimpiarSeleccion();
            string resumen = (extras > 0 ? extras + " sesion(es) extra creada(s). " : "") + (canceladas > 0 ? canceladas + " franja(s) cancelada(s) ese dia." : "");
            if (resumen.Length == 0)
            {
                resumen = "No habia nada que aplicar.";
            }

            if (problemas.Count > 0)
            {
                resumen += Environment.NewLine + Environment.NewLine + "Problemas:" + Environment.NewLine + string.Join(Environment.NewLine, problemas);
            }

            CargarFranjas();
            if (problemas.Count > 0)
            {
                MessageBox.Show(resumen, "Excepcion aplicada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Lleva a la pestana Excepciones con ese laboratorio y esa fecha (en modo Dia).</summary>
        private void MostrarExcepcionesDe(OpcionCombo laboratorio, DateTime fecha)
        {
            for (int i = 0; i < cboLabExcepciones.Items.Count; i++)
            {
                OpcionCombo opcion = cboLabExcepciones.Items[i] as OpcionCombo;
                if (opcion != null && opcion.Id == laboratorio.Id)
                {
                    cboLabExcepciones.SelectedIndex = i;
                    break;
                }
            }

            cboPeriodo.SelectedIndex = 0;
            calendario.Modo = ModoCalendario.Dia;
            calendario.Seleccion = fecha.Date;
            tabs.SelectedTab = tabExcepciones;
            CargarAgenda();
        }

        /// <summary>Clic en una franja de la cuadricula: se selecciona y sale el menu de opciones.</summary>
        private void MostrarMenuFranja(FranjaInfo franja)
        {
            DateTime proxima = ProximaFecha(franja.DiaSemana);
            menuFranja.Items[0].Text = "Editar " + franja.Codigo + " (" + NombreDia(franja.DiaSemana) + " " + HorariosDatos.Hora(franja.Inicio) + " a " + HorariosDatos.Hora(franja.Fin) + ")...";
            menuFranja.Items[3].Text = "Cancelar solo el " + NombreDia(franja.DiaSemana) + " " + proxima.ToString("dd/MM") + "...";
            ExcepcionInfo cancelacion = lienzo.CancelacionDe(franja);
            menuFranja.Items[4].Visible = cancelacion != null;
            if (cancelacion != null)
            {
                menuFranja.Items[4].Text = "Quitar la cancelacion del " + cancelacion.Fecha.ToString("dd/MM");
            }

            menuFranja.Show(Cursor.Position);
        }

        /// <summary>La proxima fecha (hoy incluido) que cae en ese dia de la semana.</summary>
        private DateTime ProximaFecha(int diaSemana)
        {
            DateTime hoy = HoyServidor().Date;
            int diaHoy = ((int)hoy.DayOfWeek + 6) % 7 + 1;
            int salto = (diaSemana - diaHoy + 7) % 7;
            return hoy.AddDays(salto);
        }

        /// <summary>Cancela la franja seleccionada solo en su proxima fecha (crea la excepcion 'cancelada').</summary>
        private void CancelarProximoDia()
        {
            FranjaInfo franja = FranjaSeleccionada();
            if (franja == null)
            {
                return;
            }

            DateTime fecha = ProximaFecha(franja.DiaSemana);
            string descripcion = franja.Codigo + " - " + franja.Materia + " - " + franja.Grupo + ", " + HorariosDatos.Hora(franja.Inicio) + " a "
                + HorariosDatos.Hora(franja.Fin) + " del " + NombreDia(franja.DiaSemana) + " " + fecha.ToString("dd/MM/yyyy");
            using (DialogoMotivo dialogo = new DialogoMotivo("Cancelar este dia", "Se cancela solo ese dia: " + descripcion + ".", "Motivo (opcional)"))
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
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "No se pudo cancelar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MostrarError("AdministrarHorarios.CancelarProximoDia", "No se pudo cancelar la franja.", ex);
                }
            }
        }

        private void NuevaFranja(int? dia, TimeSpan? hora)
        {
            OpcionCombo laboratorio = LaboratorioHorario();
            if (laboratorio == null)
            {
                MessageBox.Show("Selecciona un laboratorio.", "Laboratorio requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (DialogoFranja dialogo = new DialogoFranja(datos, ClasesActivas(), laboratorio, null, dia, hora))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    CargarFranjas();
                }
            }
        }

        private void EditarFranja()
        {
            FranjaInfo franja = FranjaSeleccionada();
            OpcionCombo laboratorio = LaboratorioHorario();
            if (franja == null || laboratorio == null)
            {
                MessageBox.Show("Selecciona una franja del horario.", "Franja requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (DialogoFranja dialogo = new DialogoFranja(datos, ClasesActivas(), laboratorio, franja, null, null))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    CargarFranjas();
                }
            }
        }

        private void BorrarFranja()
        {
            FranjaInfo franja = FranjaSeleccionada();
            if (franja == null)
            {
                MessageBox.Show("Selecciona una franja del horario.", "Franja requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pregunta = "¿Borrar la franja de " + NombreDia(franja.DiaSemana) + " "
                + HorariosDatos.Hora(franja.Inicio) + " a " + HorariosDatos.Hora(franja.Fin)
                + " (" + franja.Codigo + " - " + franja.Materia + " - " + franja.Grupo + ")?";
            if (MessageBox.Show(pregunta, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                datos.BorrarFranja(franja.Id);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.BorrarFranja", "No se pudo borrar la franja. Si ya tiene sesiones registradas, desactiva la clase en vez de borrar la franja.", ex);
            }

            CargarFranjas();
        }

        // =====================================================================
        // Pestana Excepciones
        // =====================================================================

        private OpcionCombo LaboratorioExcepciones()
        {
            return cboLabExcepciones.SelectedItem as OpcionCombo;
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

            int excepciones = filas.Count(f => f.Cancelada || f.EsExtra);
            lblDiaAgenda.Text = soloExcepciones
                ? "Excepciones del " + desde.ToString("dd/MM/yyyy") + " al " + hasta.ToString("dd/MM/yyyy") + ": " + excepciones
                  + (excepciones == 0 ? " (no hay cancelaciones ni sesiones extra en este periodo)" : "")
                : NombreDia(((int)fecha.DayOfWeek + 6) % 7 + 1) + " " + fecha.ToString("dd/MM/yyyy") + ": " + filas.Count
                  + " clase(s) en la agenda, " + excepciones + " excepcion(es)";

            dgvAgenda.Rows.Clear();
            foreach (FilaAgenda f in filas)
            {
                string estado = f.Cancelada
                    ? "CANCELADA" + (string.IsNullOrWhiteSpace(f.Motivo) ? "" : ": " + f.Motivo)
                    : f.EsExtra
                        ? "extra" + (string.IsNullOrWhiteSpace(f.Motivo) ? "" : ": " + f.Motivo)
                        : "normal";

                int fila = dgvAgenda.Rows.Add(
                    NombreDia(((int)f.Fecha.DayOfWeek + 6) % 7 + 1).Substring(0, 3) + " " + f.Fecha.ToString("dd/MM"),
                    HorariosDatos.Hora(f.Inicio),
                    HorariosDatos.Hora(f.Fin),
                    f.Codigo,
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
        }

        private void CancelarFranjaDelDia()
        {
            FilaAgenda sesion = SesionSeleccionada();
            if (sesion == null || sesion.EsExtra || sesion.Cancelada || !sesion.FranjaId.HasValue)
            {
                MessageBox.Show("Selecciona una franja normal (no cancelada ni extra) de la agenda. En Semana y Mes solo se listan las excepciones: cambia el filtro a Dia.",
                    "Franja requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DateTime fecha = sesion.Fecha;
            string descripcion = sesion.Codigo + " - " + sesion.Descripcion + ", " + HorariosDatos.Hora(sesion.Inicio) + " a " + HorariosDatos.Hora(sesion.Fin)
                + " del " + fecha.ToString("dd/MM/yyyy");
            using (DialogoMotivo dialogo = new DialogoMotivo("Cancelar este dia", "Se cancela solo ese dia: " + descripcion + ".", "Motivo (opcional)"))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    datos.CancelarFranja(sesion.FranjaId.Value, fecha, dialogo.Motivo, IdAdministrador());
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "No se pudo cancelar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MostrarError("AdministrarHorarios.CancelarFranjaDelDia", "No se pudo cancelar la franja.", ex);
                }
            }

            CargarAgenda();
        }

        private void QuitarCancelacion()
        {
            FilaAgenda sesion = SesionSeleccionada();
            OpcionCombo laboratorio = LaboratorioExcepciones();
            if (sesion == null || !sesion.Cancelada || !sesion.FranjaId.HasValue || laboratorio == null)
            {
                MessageBox.Show("Selecciona una franja cancelada de la agenda.", "Franja requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    MessageBox.Show("No se encontró la cancelación de esa franja; actualiza la agenda.", "Informacion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    CargarAgenda();
                    return;
                }

                string pregunta = "¿Quitar la cancelación del " + fecha.ToString("dd/MM/yyyy") + " de " + sesion.Codigo + " - " + sesion.Descripcion + "?"
                    + Environment.NewLine + "La clase vuelve a darse ese día con normalidad.";
                if (MessageBox.Show(pregunta, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                datos.BorrarExcepcion(idCancelacion.Value);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarCancelacion", "No se pudo quitar la cancelación.", ex);
            }

            CargarAgenda();
        }

        private void NuevaSesionExtra()
        {
            OpcionCombo laboratorio = LaboratorioExcepciones();
            if (laboratorio == null)
            {
                MessageBox.Show("Selecciona un laboratorio.", "Laboratorio requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            FilaAgenda sesion = SesionSeleccionada();
            if (sesion == null || !sesion.EsExtra || !sesion.ExcepcionId.HasValue)
            {
                MessageBox.Show("Selecciona una sesión extra de la agenda.", "Sesion requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pregunta = "¿Quitar la sesión extra de " + sesion.Codigo + " - " + sesion.Descripcion + " ("
                + HorariosDatos.Hora(sesion.Inicio) + " a " + HorariosDatos.Hora(sesion.Fin) + " del " + sesion.Fecha.ToString("dd/MM/yyyy") + ")?";
            if (MessageBox.Show(pregunta, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                datos.BorrarExcepcion(sesion.ExcepcionId.Value);
            }
            catch (Exception ex)
            {
                MostrarError("AdministrarHorarios.QuitarExtra", "No se pudo quitar la sesión extra. Si ya tiene alumnos registrados no se puede borrar.", ex);
            }

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
        public const int HoraApertura = 7;
        public const int HoraCierre = 21;

        public static Button CrearBoton(string texto, Point posicion, Size tamaño)
        {
            Button boton = new Button
            {
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = posicion,
                Size = tamaño,
                Text = texto,
                UseVisualStyleBackColor = false
            };
            boton.FlatAppearance.BorderColor = Color.DarkGreen;
            boton.FlatAppearance.BorderSize = 2;
            return boton;
        }

        public static Label CrearEtiqueta(string texto, Point posicion)
        {
            return new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
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

        public static DataGridView CrearGrid(Point posicion, Size tamaño)
        {
            DataGridView grid = new DataGridView
            {
                Location = posicion,
                Size = tamaño,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new DrawingFont("Century Gothic", 9.5F),
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                ScrollBars = ScrollBars.Both,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            grid.ColumnHeadersDefaultCellStyle.Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.DarkGreen;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 248, 240);
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
        }

        public static void SeleccionarFila(DataGridView grid, Func<DataGridViewRow, bool> condicion)
        {
            foreach (DataGridViewRow fila in grid.Rows)
            {
                if (fila.Tag != null && condicion(fila))
                {
                    grid.ClearSelection();
                    fila.Selected = true;
                    grid.CurrentCell = fila.Cells[0];
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

        public static List<OpcionCombo> OpcionesDeClases(IEnumerable<ClaseInfo> clases)
        {
            return clases.Select(c => new OpcionCombo { Id = c.Id, Texto = c.Codigo + " - " + c.Descripcion }).ToList();
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

    internal class CuadriculaSemanal : DataGridView
    {
        public const int Dias = 6;
        public const int AltoHora = 30;
        public const int AltoHoraMaximo = 46;
        public const int AltoCabecera = 24;
        public const int AnchoHoras = 96;

        public event Action<FranjaInfo> FranjaClic;
        public event Action<FranjaInfo> FranjaDobleClic;
        public event Action<List<RangoHueco>> HuecosSeleccionados;
        public event Action<int, TimeSpan> HuecoDobleClic;
        /// <summary>Clic sobre una sesion extra dibujada en la cuadricula.</summary>
        public event Action<ExcepcionInfo> ExcepcionClic;

        // Excepciones de los proximos 7 dias: se dibujan encima del horario en la
        // columna del dia en que caen, hasta que pasa su hora.
        private List<ExcepcionInfo> excepciones = new List<ExcepcionInfo>();
        private DateTime ahora = DateTime.Now;
        private ExcepcionInfo[,] extraEnCelda;
        private readonly Dictionary<int, ExcepcionInfo> cancelacionPorFranja = new Dictionary<int, ExcepcionInfo>();
        private static readonly Color ColorExtra = Color.FromArgb(255, 140, 0);
        private static readonly Color ColorCancelada = Color.FromArgb(110, 110, 110);

        /// <summary>
        /// En solo lectura no se pueden seleccionar cuadros libres ni asignar
        /// nada: la cuadricula sirve solo para elegir una clase. La usa asi la
        /// pantalla de bitacoras del profesor.
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

        private readonly DrawingFont fuenteCodigo = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteTexto = new DrawingFont("Century Gothic", 8F, FontStyle.Regular);
        private readonly DrawingFont fuenteHoras = new DrawingFont("Century Gothic", 8F, FontStyle.Regular);
        // Con la ventana grande las celdas son altas: letra mayor.
        private readonly DrawingFont fuenteCodigoGrande = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteTextoGrande = new DrawingFont("Century Gothic", 9.5F, FontStyle.Regular);

        // Un color por MAESTRO: todas sus clases y grupos se pintan igual, para
        // ver de un vistazo quien ocupa el laboratorio.
        private static readonly Color[] Paleta =
        {
            Color.FromArgb(198, 239, 206), Color.FromArgb(255, 235, 156), Color.FromArgb(189, 215, 238),
            Color.FromArgb(255, 204, 204), Color.FromArgb(226, 208, 240), Color.FromArgb(255, 222, 173),
            Color.FromArgb(204, 236, 242), Color.FromArgb(230, 230, 200), Color.FromArgb(255, 214, 235),
            Color.FromArgb(214, 228, 200), Color.FromArgb(250, 220, 200), Color.FromArgb(210, 225, 245)
        };

        public List<FranjaInfo> Franjas
        {
            get { return franjas; }
            set { franjas = value ?? new List<FranjaInfo>(); Reconstruir(); }
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
            set { excepciones = value ?? new List<ExcepcionInfo>(); Reconstruir(); }
        }

        /// <summary>Reloj del servidor: decide que excepciones de hoy ya pasaron.</summary>
        public DateTime Ahora
        {
            get { return ahora; }
            set { ahora = value; }
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
            return extraEnCelda == null || fila < 0 || columna < 1 || columna > Dias ? null : extraEnCelda[fila, columna];
        }

        /// <summary>Solo cuentan las que aun no han pasado: futuras, o de hoy con fin posterior a ahora.</summary>
        private bool ExcepcionVigente(ExcepcionInfo x)
        {
            if (x.Fecha.Date < ahora.Date || x.Fecha.Date > ahora.Date.AddDays(6))
            {
                return false;
            }

            return x.Fecha.Date > ahora.Date || x.Fin > ahora.TimeOfDay;
        }

        public CuadriculaSemanal()
        {
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
            ColumnHeadersHeight = AltoCabecera;
            ColumnHeadersDefaultCellStyle.Font = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
            ColumnHeadersDefaultCellStyle.ForeColor = Color.DarkGreen;
            ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 248, 240);
            ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            EnableHeadersVisualStyles = false;
            RowTemplate.Height = AltoHora;
            DefaultCellStyle.Font = fuenteTexto;
            DefaultCellStyle.SelectionBackColor = Color.White;
            DefaultCellStyle.SelectionForeColor = Color.Black;
            StandardTab = true;

            Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "",
                Width = AnchoHoras,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,
                Frozen = true
            });
            for (int dia = 1; dia <= Dias; dia++)
            {
                Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = HorariosDatos.NombresDia[dia],
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                    FillWeight = 100,
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            }

            int filas = EstiloHorarios.HoraCierre - EstiloHorarios.HoraApertura;
            for (int i = 0; i < filas; i++)
            {
                int fila = Rows.Add();
                Rows[fila].Height = AltoHora;
                // "07:00-08:00" ... "20:00-21:00": se ve que la ultima fila llega a las 21:00.
                Rows[fila].Cells[0].Value = HorariosDatos.Hora(HoraDeFila(i)) + "-" + HorariosDatos.Hora(HoraDeFila(i) + TimeSpan.FromHours(1));
            }

            CellPainting += CuadriculaSemanal_CellPainting;
            CellMouseDoubleClick += CuadriculaSemanal_CellMouseDoubleClick;
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

        /// <summary>Alto total que conviene al control para un alto disponible: 14 filas entre 30 y 46 px.</summary>
        public static int AltoPara(int disponible)
        {
            int filas = EstiloHorarios.HoraCierre - EstiloHorarios.HoraApertura;
            int altoFila = Math.Max(AltoHora, Math.Min(AltoHoraMaximo, (disponible - AltoCabecera - 8) / filas));
            return AltoCabecera + filas * altoFila + 8;
        }

        // Las filas se estiran para que las 14 horas llenen el alto disponible
        // (hasta un maximo comodo): el horario completo se ve sin desplazarse.
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

            // Se reparte el alto entre las 14 filas dejando un margen abajo para que
            // la ultima (20:00-21:00) se vea entera con su borde.
            int disponible = ClientSize.Height - ColumnHeadersHeight - 6;
            int alto = Math.Max(AltoHora, Math.Min(AltoHoraMaximo, disponible / Rows.Count));
            foreach (DataGridViewRow fila in Rows)
            {
                if (fila.Height != alto)
                {
                    fila.Height = alto;
                }
            }
        }

        private static TimeSpan HoraDeFila(int fila)
        {
            return TimeSpan.FromHours(EstiloHorarios.HoraApertura + fila);
        }

        private static int FilaDeHora(TimeSpan hora)
        {
            return (int)Math.Floor(hora.TotalHours - EstiloHorarios.HoraApertura);
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
            List<RangoHueco> rangos = new List<RangoHueco>();
            for (int dia = 1; dia <= Dias; dia++)
            {
                List<int> filas = new List<int>();
                foreach (DataGridViewCell celda in SelectedCells)
                {
                    if (celda.ColumnIndex == dia && (!soloLibres || celda.Tag == null))
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

            FranjaInfo franja = Rows[e.RowIndex].Cells[e.ColumnIndex].Tag as FranjaInfo;
            FijarBajoRaton(franja, e.RowIndex, e.ColumnIndex);
            Cursor = franja != null || ExtraEn(e.RowIndex, e.ColumnIndex) != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            FijarBajoRaton(null, -1, -1);
            Cursor = Cursors.Default;
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
            if (e.RowIndex < 0 || e.ColumnIndex < 1)
            {
                return;
            }

            FranjaInfo franja = Rows[e.RowIndex].Cells[e.ColumnIndex].Tag as FranjaInfo;
            if (franja != null)
            {
                if (FranjaDobleClic != null) FranjaDobleClic(franja);
            }
            else
            {
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
                for (int c = 1; c <= Dias; c++)
                {
                    fila.Cells[c].Tag = null;
                    fila.Cells[c].Value = null;
                }
            }

            int ultimaFila = Rows.Count - 1;
            foreach (FranjaInfo franja in franjas.OrderBy(f => f.Inicio))
            {
                if (franja.DiaSemana < 1 || franja.DiaSemana > Dias)
                {
                    continue;
                }

                int desde = Math.Max(0, FilaDeHora(franja.Inicio));
                int hasta = Math.Min(ultimaFila, (int)Math.Ceiling(franja.Fin.TotalHours - EstiloHorarios.HoraApertura) - 1);
                if (hasta < desde)
                {
                    continue;
                }

                for (int f = desde; f <= hasta; f++)
                {
                    DataGridViewCell celda = Rows[f].Cells[franja.DiaSemana];
                    celda.Tag = franja;
                    celda.Value = f == desde ? franja.Codigo : "";
                }
            }

            // Excepciones vigentes encima: extras por celda, cancelaciones por franja.
            extraEnCelda = new ExcepcionInfo[Rows.Count, Dias + 1];
            cancelacionPorFranja.Clear();
            foreach (ExcepcionInfo x in excepciones.Where(ExcepcionVigente))
            {
                if (x.EsExtra)
                {
                    int dia = ((int)x.Fecha.DayOfWeek + 6) % 7 + 1;
                    if (dia < 1 || dia > Dias)
                    {
                        continue;
                    }

                    int desde = Math.Max(0, FilaDeHora(x.Inicio));
                    int hasta = Math.Min(ultimaFila, (int)Math.Ceiling(x.Fin.TotalHours - EstiloHorarios.HoraApertura) - 1);
                    for (int f = desde; f <= hasta; f++)
                    {
                        extraEnCelda[f, dia] = x;
                    }
                }
                else if (x.FranjaId.HasValue && !cancelacionPorFranja.ContainsKey(x.FranjaId.Value))
                {
                    cancelacionPorFranja[x.FranjaId.Value] = x;
                }
            }

            ClearSelection();
            Invalidate();
        }

        public static Color ColorDeProfesor(int idProfesor)
        {
            return Paleta[Math.Abs(idProfesor) % Paleta.Length];
        }

        private void CuadriculaSemanal_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
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
            if (franja == null)
            {
                // Cuadro libre: blanco (gris suave bajo el raton), o verde claro con
                // borde punteado si esta seleccionado.
                e.PaintBackground(e.ClipBounds, false);
                if (extra != null)
                {
                    PintarExtra(e, extra);
                    e.Handled = true;
                    return;
                }

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

            bool vigente = franja.Activa && hoy.Date >= franja.VigenteDesde.Date && hoy.Date <= franja.VigenteHasta.Date;
            Color fondo = vigente ? ColorDeProfesor(franja.ProfesorId) : Color.FromArgb(232, 232, 232);
            Color texto = vigente ? Color.Black : Color.Gray;
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

            if (primera)
            {
                // Dos lineas en el primer cuadro: codigo, y materia - grupo.
                Rectangle arriba = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + 2, e.CellBounds.Width - 8, e.CellBounds.Height / 2 - 1);
                Rectangle abajo = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + e.CellBounds.Height / 2 - 1, e.CellBounds.Width - 8, e.CellBounds.Height / 2 - 1);
                const TextFormatFlags unaLinea = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                bool grande = e.CellBounds.Height >= 44;
                TextRenderer.DrawText(e.Graphics, franja.Codigo + "  " + HorariosDatos.Hora(franja.Inicio) + "-" + HorariosDatos.Hora(franja.Fin),
                    grande ? fuenteCodigoGrande : fuenteCodigo, arriba, texto, unaLinea);
                TextRenderer.DrawText(e.Graphics, franja.Materia + " - " + franja.Grupo, grande ? fuenteTextoGrande : fuenteTexto, abajo, texto, unaLinea);
            }

            // Franja cancelada en su proxima fecha: rayado gris y aviso en su primer cuadro.
            ExcepcionInfo cancelacion = CancelacionDe(franja);
            if (cancelacion != null)
            {
                using (System.Drawing.Drawing2D.HatchBrush rayas = new System.Drawing.Drawing2D.HatchBrush(
                    System.Drawing.Drawing2D.HatchStyle.WideUpwardDiagonal, Color.FromArgb(150, ColorCancelada), Color.FromArgb(60, Color.White)))
                {
                    e.Graphics.FillRectangle(rayas, e.CellBounds);
                }

                if (primera)
                {
                    Rectangle aviso = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + e.CellBounds.Height / 2 - 1, e.CellBounds.Width - 8, e.CellBounds.Height / 2 - 1);
                    string textoAviso = "CANCELADA " + cancelacion.Fecha.ToString("dd/MM") + (cancelacion.Motivo.Length > 0 ? " - " + cancelacion.Motivo : "");
                    using (SolidBrush fondoAviso = new SolidBrush(Color.FromArgb(235, Color.White)))
                    {
                        e.Graphics.FillRectangle(fondoAviso, aviso);
                    }

                    TextRenderer.DrawText(e.Graphics, textoAviso, e.CellBounds.Height >= 44 ? fuenteCodigoGrande : fuenteCodigo, aviso, Color.FromArgb(160, 30, 30),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
            }

            // Sesion extra encima de una franja (esa franja ya quedo cancelada ese dia).
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
        /// Sesion extra: rayado naranja con borde grueso, y en su primer cuadro un
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
                bool grande = e.CellBounds.Height >= 44;
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
                TextRenderer.DrawText(e.Graphics, extra.Codigo + " " + extra.Materia + " - " + extra.Grupo + (extra.Motivo.Length > 0 ? " (" + extra.Motivo + ")" : ""),
                    grande ? fuenteTextoGrande : fuenteTexto, abajo, Color.Black, unaLinea);
            }
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
        private LinkLabel lnkNuevoGrupo;

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

            InicializarComponentes();
            CargarDatos();
        }

        private void InicializarComponentes()
        {
            EstiloHorarios.PrepararDialogo(this, existente == null ? "Nueva clase" : "Editar clase", new Size(520, 400));

            Label lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Text = existente == null ? "Nueva clase" : "Editar clase"
            };

            Label lblProfesor = EstiloHorarios.CrearEtiqueta("Profesor", new Point(24, 66));
            cboProfesor = EstiloHorarios.CrearCombo(new Point(140, 62), 350);

            Label lblGrupo = EstiloHorarios.CrearEtiqueta("Grupo", new Point(24, 106));
            cboGrupo = EstiloHorarios.CrearCombo(new Point(140, 102), 250);

            // Los grupos son de las clases: si falta el de esta, se crea aqui mismo.
            lnkNuevoGrupo = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9F),
                LinkColor = Color.DarkGreen,
                Location = new Point(398, 107),
                Text = "Nuevo grupo"
            };
            lnkNuevoGrupo.LinkClicked += (s, e) => CrearGrupo();

            Label lblMateria = EstiloHorarios.CrearEtiqueta("Materia", new Point(24, 146));
            cboMateria = EstiloHorarios.CrearCombo(new Point(140, 142), 350);

            Label lblCodigo = EstiloHorarios.CrearEtiqueta("Codigo", new Point(24, 186));
            txtCodigo = EstiloHorarios.CrearTexto(new Point(140, 182), 140);
            txtCodigo.MaxLength = 16;
            txtCodigo.CharacterCasing = CharacterCasing.Normal;
            txtCodigo.Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);

            btnProponer = EstiloHorarios.CrearBoton("Proponer otro", new Point(292, 178), new Size(140, 34));
            btnProponer.Click += (s, e) => ProponerCodigo();

            Label lblNotaCodigo = null;
            if (codigoPorProfesor)
            {
                txtCodigo.ReadOnly = true;
                txtCodigo.TabStop = false;
                btnProponer.Visible = false;
                lblNotaCodigo = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 8.5F),
                    ForeColor = Color.FromArgb(90, 90, 90),
                    Location = new Point(292, 176),
                    Size = new Size(210, 40),
                    Text = "Es el del profesor: el mismo en todas sus clases. Se cambia en Profesores."
                };
                cboProfesor.SelectedIndexChanged += (s, e) => MostrarCodigoDelProfesor();
            }

            Label lblDesde = EstiloHorarios.CrearEtiqueta("Vigente desde", new Point(24, 230));
            dtpDesde = new DateTimePicker
            {
                Font = new DrawingFont("Century Gothic", 10F),
                Format = DateTimePickerFormat.Short,
                Location = new Point(140, 226),
                Size = new Size(130, 26),
                Value = DateTime.Today
            };

            Label lblHasta = EstiloHorarios.CrearEtiqueta("hasta", new Point(290, 230));
            dtpHasta = new DateTimePicker
            {
                Font = new DrawingFont("Century Gothic", 10F),
                Format = DateTimePickerFormat.Short,
                Location = new Point(360, 226),
                Size = new Size(130, 26),
                Value = DateTime.Today.AddMonths(5)
            };

            lblAviso = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(24, 268),
                Size = new Size(470, 50),
                Text = codigoPorProfesor
                    ? "El codigo es el del profesor y vale en todas sus clases, pero cada una solo en su laboratorio y a su hora."
                    : "El codigo lo teclean los alumnos: identifica esta clase todo el semestre y solo entra en su laboratorio y su horario."
            };

            btnGuardar = EstiloHorarios.CrearBoton("Guardar", new Point(210, 336), new Size(135, 40));
            btnGuardar.Click += (s, e) => Guardar();

            btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(355, 336), new Size(135, 40));
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
                Log.Error("DialogoClase.ObtenerProfesores", ex);
                MessageBox.Show("No se pudieron leer los profesores: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                profesores = new List<OpcionCombo>();
            }

            EstiloHorarios.Rellenar(cboProfesor, profesores, existente == null ? (int?)null : existente.ProfesorId);
            EstiloHorarios.Rellenar(cboGrupo, consultas.ObtenerGrupos(), existente == null ? (int?)null : existente.GrupoId);
            EstiloHorarios.Rellenar(cboMateria, consultas.ObtenerMaterias(), existente == null ? (int?)null : existente.MateriaId);

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

                return;
            }

            txtCodigo.Text = existente.Codigo;
            dtpDesde.Value = existente.VigenteDesde.Date;
            dtpHasta.Value = existente.VigenteHasta.Date;

            if (existente.Sesiones > 0)
            {
                // Con sesiones (y bitacoras) registradas, profesor, grupo y materia ya no se tocan.
                cboProfesor.Enabled = false;
                cboGrupo.Enabled = false;
                lnkNuevoGrupo.Enabled = false;
                cboMateria.Enabled = false;
                lblAviso.ForeColor = Color.DarkGreen;
                lblAviso.Text = "Esta clase ya tiene " + existente.Sesiones + " sesion(es) registrada(s): solo se "
                    + (codigoPorProfesor ? "puede cambiar la vigencia." : "pueden cambiar el codigo y la vigencia.");
            }
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

        private void CrearGrupo()
        {
            using (DialogoGrupo dialogo = new DialogoGrupo(0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    EstiloHorarios.Rellenar(cboGrupo, consultas.ObtenerGrupos(), dialogo.IdGuardado);
                }
            }
        }

        private void ProponerCodigo()
        {
            try
            {
                txtCodigo.Text = datos.ProponerCodigo();
            }
            catch (Exception ex)
            {
                Log.Error("DialogoClase.ProponerCodigo", ex);
                MessageBox.Show("No se pudo proponer un código: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Guardar()
        {
            OpcionCombo profesor = cboProfesor.SelectedItem as OpcionCombo;
            OpcionCombo grupo = cboGrupo.SelectedItem as OpcionCombo;
            OpcionCombo materia = cboMateria.SelectedItem as OpcionCombo;
            if (profesor == null || grupo == null || materia == null)
            {
                MessageBox.Show("Elige profesor, grupo y materia.", "Datos incompletos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Con un codigo por profesor el que se muestra es el suyo y lo pone HorariosDatos.
            string codigo = codigoPorProfesor ? "" : txtCodigo.Text.Trim();
            if (!codigoPorProfesor && codigo.Length == 0)
            {
                MessageBox.Show("Escribe un código o pulsa 'Proponer otro'.", "Codigo requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtCodigo.Focus();
                return;
            }

            if (!codigoPorProfesor && codigo.Any(char.IsWhiteSpace))
            {
                MessageBox.Show("El código no puede llevar espacios.", "Codigo no valido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtCodigo.Focus();
                return;
            }

            if (dtpHasta.Value.Date < dtpDesde.Value.Date)
            {
                MessageBox.Show("La fecha de fin de vigencia no puede ser anterior a la de inicio.", "Vigencia no valida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (!codigoPorProfesor && !datos.CodigoDisponible(codigo, existente == null ? (int?)null : existente.Id))
                {
                    MessageBox.Show("El código '" + codigo + "' ya está en uso por otra clase. Elige otro.", "Codigo repetido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "No se pudo guardar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("DialogoClase.Guardar", ex);
                MessageBox.Show("No se pudo guardar la clase: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private ComboBox cboClase;
        private ComboBox cboDia;
        private ComboBox cboInicio;
        private ComboBox cboFin;
        private Button btnGuardar;
        private Button btnCancelar;

        public DialogoFranja(HorariosDatos datos, List<ClaseInfo> clasesActivas, OpcionCombo laboratorio, FranjaInfo existente, int? diaInicial, TimeSpan? horaInicial)
        {
            this.datos = datos;
            this.clasesActivas = clasesActivas;
            this.laboratorio = laboratorio;
            this.existente = existente;
            this.diaInicial = diaInicial;
            this.horaInicial = horaInicial;

            InicializarComponentes();
            CargarDatos();
        }

        private void InicializarComponentes()
        {
            EstiloHorarios.PrepararDialogo(this, existente == null ? "Nueva franja" : "Editar franja", new Size(560, 300));

            Label lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Text = (existente == null ? "Nueva franja en " : "Editar franja de ") + laboratorio.Texto
            };

            Label lblClase = EstiloHorarios.CrearEtiqueta("Clase", new Point(24, 66));
            cboClase = EstiloHorarios.CrearCombo(new Point(110, 62), 420);

            Label lblDia = EstiloHorarios.CrearEtiqueta("Dia", new Point(24, 110));
            cboDia = EstiloHorarios.CrearCombo(new Point(110, 106), 150);

            Label lblInicio = EstiloHorarios.CrearEtiqueta("Inicio", new Point(24, 154));
            cboInicio = EstiloHorarios.CrearComboHoras(new Point(110, 150), null);

            Label lblFin = EstiloHorarios.CrearEtiqueta("Fin", new Point(230, 154));
            cboFin = EstiloHorarios.CrearComboHoras(new Point(280, 150), null);

            Label lblNota = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(24, 190),
                Size = new Size(510, 36),
                Text = "Se rechaza si se solapa con otra franja del mismo laboratorio, del mismo profesor o del mismo grupo."
            };

            btnGuardar = EstiloHorarios.CrearBoton("Guardar", new Point(250, 238), new Size(135, 40));
            btnGuardar.Click += (s, e) => Guardar();

            btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(395, 238), new Size(135, 40));
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = btnGuardar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(lblClase);
            Controls.Add(cboClase);
            Controls.Add(lblDia);
            Controls.Add(cboDia);
            Controls.Add(lblInicio);
            Controls.Add(cboInicio);
            Controls.Add(lblFin);
            Controls.Add(cboFin);
            Controls.Add(lblNota);
            Controls.Add(btnGuardar);
            Controls.Add(btnCancelar);
        }

        private void CargarDatos()
        {
            List<OpcionCombo> clases = EstiloHorarios.OpcionesDeClases(clasesActivas);
            if (existente != null && !clasesActivas.Any(c => c.Id == existente.ClaseId))
            {
                // La clase de la franja esta inactiva: se muestra para poder editarla igualmente.
                clases.Insert(0, new OpcionCombo
                {
                    Id = existente.ClaseId,
                    Texto = existente.Codigo + " - " + existente.Materia + " - " + existente.Grupo + " - " + existente.Profesor + " (inactiva)"
                });
            }

            EstiloHorarios.Rellenar(cboClase, clases, existente == null ? (int?)null : existente.ClaseId);

            List<OpcionCombo> dias = new List<OpcionCombo>();
            for (int d = 1; d <= 7; d++)
            {
                dias.Add(new OpcionCombo { Id = d, Texto = HorariosDatos.NombresDia[d] });
            }

            int dia = existente != null ? existente.DiaSemana : (diaInicial ?? 1);
            EstiloHorarios.Rellenar(cboDia, dias, dia);

            if (existente != null)
            {
                cboInicio.Text = HorariosDatos.Hora(existente.Inicio);
                cboFin.Text = HorariosDatos.Hora(existente.Fin);
            }
            else if (horaInicial.HasValue)
            {
                cboInicio.Text = HorariosDatos.Hora(horaInicial.Value);
                cboFin.Text = HorariosDatos.Hora(horaInicial.Value + TimeSpan.FromHours(2) <= TimeSpan.FromHours(EstiloHorarios.HoraCierre)
                    ? horaInicial.Value + TimeSpan.FromHours(2)
                    : TimeSpan.FromHours(EstiloHorarios.HoraCierre));
            }
        }

        private void Guardar()
        {
            OpcionCombo clase = cboClase.SelectedItem as OpcionCombo;
            OpcionCombo dia = cboDia.SelectedItem as OpcionCombo;
            if (clase == null || dia == null)
            {
                MessageBox.Show("Elige la clase y el día.", "Datos incompletos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            TimeSpan inicio, fin;
            if (!EstiloHorarios.LeerHora(cboInicio, out inicio) || !EstiloHorarios.LeerHora(cboFin, out fin))
            {
                MessageBox.Show("Escribe las horas como HH:mm (por ejemplo 08:00).", "Hora no valida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (fin <= inicio)
            {
                MessageBox.Show("La hora de fin tiene que ser posterior a la de inicio.", "Hora no valida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (existente == null)
                {
                    datos.CrearFranja(clase.Id, laboratorio.Id, dia.Id, inicio, fin);
                }
                else
                {
                    datos.ActualizarFranja(existente.Id, clase.Id, laboratorio.Id, dia.Id, inicio, fin);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Conflicto de horario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("DialogoFranja.Guardar", ex);
                MessageBox.Show("No se pudo guardar la franja: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private ComboBox cboClase;
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
            EstiloHorarios.PrepararDialogo(this, "Sesion extra", new Size(560, 320));

            Label lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Text = "Sesion extra en " + laboratorio.Texto + " el " + fecha.ToString("dd/MM/yyyy")
            };

            Label lblClase = EstiloHorarios.CrearEtiqueta("Clase", new Point(24, 66));
            cboClase = EstiloHorarios.CrearCombo(new Point(110, 62), 420);

            Label lblInicio = EstiloHorarios.CrearEtiqueta("Inicio", new Point(24, 110));
            cboInicio = EstiloHorarios.CrearComboHoras(new Point(110, 106), null);

            Label lblFin = EstiloHorarios.CrearEtiqueta("Fin", new Point(230, 110));
            cboFin = EstiloHorarios.CrearComboHoras(new Point(280, 106), null);

            Label lblMotivo = EstiloHorarios.CrearEtiqueta("Motivo", new Point(24, 154));
            txtMotivo = EstiloHorarios.CrearTexto(new Point(110, 150), 420);
            txtMotivo.MaxLength = 200;

            Label lblNota = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(24, 190),
                Size = new Size(510, 50),
                Text = "El codigo de la clase entrara en este laboratorio solo ese dia y en ese horario. Se valida contra la agenda efectiva del dia (franjas no canceladas y otras extras)."
            };

            btnGuardar = EstiloHorarios.CrearBoton("Guardar", new Point(250, 256), new Size(135, 40));
            btnGuardar.Click += (s, e) => Guardar();

            btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(395, 256), new Size(135, 40));
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = btnGuardar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(lblClase);
            Controls.Add(cboClase);
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
            // Solo clases vigentes en esa fecha: una extra de una clase fuera de periodo no entraria de todas formas.
            List<ClaseInfo> vigentes = clasesActivas.Where(c => c.VigenteEn(fecha)).ToList();
            EstiloHorarios.Rellenar(cboClase, EstiloHorarios.OpcionesDeClases(vigentes), null);

            if (inicioSugerido.HasValue && inicioSugerido.Value < TimeSpan.FromHours(EstiloHorarios.HoraCierre))
            {
                cboInicio.Text = HorariosDatos.Hora(inicioSugerido.Value);
                TimeSpan fin = inicioSugerido.Value + TimeSpan.FromHours(2);
                if (fin > TimeSpan.FromHours(EstiloHorarios.HoraCierre))
                {
                    fin = TimeSpan.FromHours(EstiloHorarios.HoraCierre);
                }

                cboFin.Text = HorariosDatos.Hora(fin);
            }
        }

        private void Guardar()
        {
            OpcionCombo clase = cboClase.SelectedItem as OpcionCombo;
            if (clase == null)
            {
                MessageBox.Show("Elige la clase.", "Datos incompletos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            TimeSpan inicio, fin;
            if (!EstiloHorarios.LeerHora(cboInicio, out inicio) || !EstiloHorarios.LeerHora(cboFin, out fin))
            {
                MessageBox.Show("Escribe las horas como HH:mm (por ejemplo 08:00).", "Hora no valida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (fin <= inicio)
            {
                MessageBox.Show("La hora de fin tiene que ser posterior a la de inicio.", "Hora no valida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                datos.CrearExtra(clase.Id, laboratorio.Id, fecha, inicio, fin, txtMotivo.Text.Trim(), creadaPor);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Conflicto de horario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("DialogoExtra.Guardar", ex);
                MessageBox.Show("No se pudo crear la sesión extra: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        public DialogoMotivo(string titulo, string descripcion, string etiqueta)
        {
            EstiloHorarios.PrepararDialogo(this, titulo, new Size(520, 230));

            Label lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Text = titulo
            };

            Label lblDescripcion = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(24, 52),
                Size = new Size(470, 50),
                Text = descripcion
            };

            Label lblEtiqueta = EstiloHorarios.CrearEtiqueta(etiqueta, new Point(24, 112));
            txtMotivo = EstiloHorarios.CrearTexto(new Point(24, 136), 470);
            txtMotivo.MaxLength = 200;

            Button btnAceptar = EstiloHorarios.CrearBoton("Aceptar", new Point(210, 178), new Size(135, 40));
            btnAceptar.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            Button btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(355, 178), new Size(135, 40));
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = btnAceptar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(lblDescripcion);
            Controls.Add(lblEtiqueta);
            Controls.Add(txtMotivo);
            Controls.Add(btnAceptar);
            Controls.Add(btnCancelar);
        }
    }


    // =========================================================================
    // Dialogo: asignar una clase (maestro - materia - grupo - codigo) a los
    // cuadros seleccionados en la cuadricula
    // =========================================================================

    internal class DialogoAsignarClase : Form
    {
        private readonly ComboBox cboClase;

        public int ClaseId { get; private set; }

        public DialogoAsignarClase(List<ClaseInfo> clasesActivas, OpcionCombo laboratorio, List<RangoHueco> rangos)
        {
            Text = "Asignar clase";
            BackColor = Color.White;
            ClientSize = new Size(560, 250);
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
                Text = "Asignar clase a los cuadros seleccionados"
            };

            string resumen = string.Join(", ", rangos.Select(r => r.Descripcion));
            Label lblDescripcion = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(22, 48),
                Size = new Size(516, 58),
                Text = laboratorio.Texto + Environment.NewLine + resumen
                    + Environment.NewLine + "Se creara una franja por cada bloque (" + rangos.Count + ")."
            };

            Label lblClase = EstiloHorarios.CrearEtiqueta("Clase", new Point(22, 116));
            cboClase = EstiloHorarios.CrearCombo(new Point(22, 140), 516);
            foreach (ClaseInfo clase in clasesActivas)
            {
                cboClase.Items.Add(new OpcionCombo { Id = clase.Id, Texto = clase.Codigo + " - " + clase.Materia + " - " + clase.Grupo + " - " + clase.Profesor });
            }

            if (cboClase.Items.Count > 0)
            {
                cboClase.SelectedIndex = 0;
            }

            Button btnAceptar = EstiloHorarios.CrearBoton("Asignar", new Point(300, 196), new Size(115, 38));
            btnAceptar.Click += (s, e) =>
            {
                OpcionCombo elegida = cboClase.SelectedItem as OpcionCombo;
                if (elegida == null)
                {
                    MessageBox.Show("No hay clases activas. Crea la clase primero en la pestana Clases.", "Sin clases", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ClaseId = elegida.Id;
                DialogResult = DialogResult.OK;
            };

            Button btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(423, 196), new Size(115, 38));
            btnCancelar.Click += (s, e) => DialogResult = DialogResult.Cancel;

            AcceptButton = btnAceptar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(lblDescripcion);
            Controls.Add(lblClase);
            Controls.Add(cboClase);
            Controls.Add(btnAceptar);
            Controls.Add(btnCancelar);
        }
    }


    // =========================================================================
    // Dialogo: aplicar una excepcion sobre los cuadros seleccionados en la
    // cuadricula (cancelar esas horas un dia, o sesion extra de otra clase)
    // =========================================================================

    /// <summary>Un bloque seleccionado llevado a una fecha concreta, con las franjas del horario que toca.</summary>
    internal class ExcepcionPlaneada
    {
        public RangoHueco Rango;
        public DateTime Fecha;
        public List<FranjaInfo> Conflictos = new List<FranjaInfo>();
    }

    internal class DialogoExcepcionCuadricula : Form
    {
        private readonly List<RangoHueco> rangos;
        private readonly List<FranjaInfo> franjasDelLaboratorio;
        private readonly DateTimePicker dtpDesde;
        private readonly RadioButton rbCancelar;
        private readonly RadioButton rbExtra;
        private readonly ComboBox cboClase;
        private readonly TextBox txtMotivo;
        private readonly TextBox txtResumen;

        public List<ExcepcionPlaneada> Plan { get; private set; }
        public bool EsExtra { get; private set; }
        public int ClaseId { get; private set; }
        public string Motivo { get; private set; }

        public DialogoExcepcionCuadricula(List<ClaseInfo> clasesActivas, OpcionCombo laboratorio, List<RangoHueco> rangos, List<FranjaInfo> franjasDelLaboratorio, DateTime hoy)
        {
            this.rangos = rangos;
            this.franjasDelLaboratorio = franjasDelLaboratorio ?? new List<FranjaInfo>();

            Text = "Aplicar excepcion";
            BackColor = Color.White;
            ClientSize = new Size(640, 520);
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
                Text = "Aplicar excepcion en " + laboratorio.Texto
            };

            Label lblDesde = EstiloHorarios.CrearEtiqueta("A partir del", new Point(22, 54));
            dtpDesde = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(140, 50),
                Size = new Size(130, 26),
                Value = hoy.Date
            };
            dtpDesde.ValueChanged += (s, e) => Recalcular();

            Label lblAyuda = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.Gray,
                Location = new Point(285, 52),
                Size = new Size(335, 34),
                Text = "Cada bloque se aplica en la primera fecha, desde ese dia, que caiga en su dia de la semana."
            };

            rbCancelar = new RadioButton
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(22, 92),
                Text = "Solo cancelar las franjas de esas horas ese dia (no habra clase)"
            };
            rbExtra = new RadioButton
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(22, 118),
                Text = "Sesion extra de una clase en esas horas (lo que choque queda cancelado ese dia)",
                Checked = true
            };
            rbCancelar.CheckedChanged += (s, e) => Recalcular();
            rbExtra.CheckedChanged += (s, e) => Recalcular();

            Label lblClase = EstiloHorarios.CrearEtiqueta("Clase", new Point(22, 152));
            cboClase = EstiloHorarios.CrearCombo(new Point(22, 176), 596);
            foreach (ClaseInfo clase in clasesActivas)
            {
                cboClase.Items.Add(new OpcionCombo { Id = clase.Id, Texto = clase.Codigo + " - " + clase.Materia + " - " + clase.Grupo + " - " + clase.Profesor });
            }

            if (cboClase.Items.Count > 0)
            {
                cboClase.SelectedIndex = 0;
            }

            Label lblMotivo = EstiloHorarios.CrearEtiqueta("Motivo (opcional)", new Point(22, 214));
            txtMotivo = new TextBox
            {
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(22, 238),
                Size = new Size(596, 26),
                MaxLength = 200
            };

            Label lblResumen = EstiloHorarios.CrearEtiqueta("Que se va a aplicar", new Point(22, 276));
            txtResumen = new TextBox
            {
                Font = new DrawingFont("Century Gothic", 9F),
                Location = new Point(22, 300),
                Size = new Size(596, 150),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(248, 248, 248)
            };

            Button btnAplicar = EstiloHorarios.CrearBoton("Aplicar", new Point(380, 466), new Size(115, 38));
            btnAplicar.Click += (s, e) => Aceptar();
            Button btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(503, 466), new Size(115, 38));
            btnCancelar.Click += (s, e) => DialogResult = DialogResult.Cancel;

            AcceptButton = btnAplicar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(lblDesde);
            Controls.Add(dtpDesde);
            Controls.Add(lblAyuda);
            Controls.Add(rbCancelar);
            Controls.Add(rbExtra);
            Controls.Add(lblClase);
            Controls.Add(cboClase);
            Controls.Add(lblMotivo);
            Controls.Add(txtMotivo);
            Controls.Add(lblResumen);
            Controls.Add(txtResumen);
            Controls.Add(btnAplicar);
            Controls.Add(btnCancelar);

            Recalcular();
        }

        private static DateTime PrimeraFecha(DateTime desde, int diaSemana)
        {
            int diaBase = ((int)desde.DayOfWeek + 6) % 7 + 1;
            return desde.Date.AddDays((diaSemana - diaBase + 7) % 7);
        }

        private List<ExcepcionPlaneada> Planear()
        {
            List<ExcepcionPlaneada> plan = new List<ExcepcionPlaneada>();
            foreach (RangoHueco rango in rangos)
            {
                ExcepcionPlaneada p = new ExcepcionPlaneada { Rango = rango, Fecha = PrimeraFecha(dtpDesde.Value, rango.Dia) };
                p.Conflictos = franjasDelLaboratorio
                    .Where(f => f.DiaSemana == rango.Dia && f.Activa && f.Inicio < rango.Fin && f.Fin > rango.Inicio
                        && p.Fecha >= f.VigenteDesde.Date && p.Fecha <= f.VigenteHasta.Date)
                    .OrderBy(f => f.Inicio)
                    .ToList();
                plan.Add(p);
            }

            return plan;
        }

        private void Recalcular()
        {
            cboClase.Enabled = rbExtra.Checked;
            List<string> lineas = new List<string>();
            foreach (ExcepcionPlaneada p in Planear())
            {
                string linea = HorariosDatos.NombresDia[p.Rango.Dia] + " " + p.Fecha.ToString("dd/MM/yyyy") + ", "
                    + HorariosDatos.Hora(p.Rango.Inicio) + " a " + HorariosDatos.Hora(p.Rango.Fin) + ": ";
                if (p.Conflictos.Count == 0)
                {
                    linea += rbExtra.Checked ? "libre, se programa la sesion extra." : "no hay franjas que cancelar.";
                }
                else
                {
                    string cuales = string.Join("; ", p.Conflictos.Select(f => f.Codigo + " " + f.Materia + " - " + f.Grupo + " (" + f.Profesor + ", "
                        + HorariosDatos.Hora(f.Inicio) + "-" + HorariosDatos.Hora(f.Fin) + ")"));
                    linea += (rbExtra.Checked ? "se cancela ese dia y entra la extra: " : "se cancela ese dia: ") + cuales;
                }

                lineas.Add(linea);
            }

            txtResumen.Text = string.Join(Environment.NewLine + Environment.NewLine, lineas);
        }

        private void Aceptar()
        {
            List<ExcepcionPlaneada> plan = Planear();
            if (rbExtra.Checked)
            {
                OpcionCombo elegida = cboClase.SelectedItem as OpcionCombo;
                if (elegida == null)
                {
                    MessageBox.Show("Elige la clase de la sesion extra.", "Clase requerida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ClaseId = elegida.Id;
            }
            else if (plan.All(p => p.Conflictos.Count == 0))
            {
                MessageBox.Show("En esas horas no hay franjas que cancelar. Si quieres ocupar el hueco, elige \"Sesion extra de una clase\".", "Nada que cancelar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Plan = plan;
            EsExtra = rbExtra.Checked;
            Motivo = txtMotivo.Text.Trim();
            DialogResult = DialogResult.OK;
        }
    }
}
