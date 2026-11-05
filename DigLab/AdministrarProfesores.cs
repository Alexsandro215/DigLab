using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;

namespace DigLab
{
    /// <summary>
    /// Seccion Profesores del administrador: lista con buscador, cuantas clases
    /// vigentes tiene cada uno y en que laboratorios, y sobre el elegido editar,
    /// restablecer la contrasena y dar de baja o reactivar. Sustituye al alta y a
    /// la edicion viejas y al "Eliminar profesor", que borraba en cascada sus
    /// clases, sus horarios y las bitacoras de todos sus alumnos. De aqui se abren
    /// tambien los encargados de laboratorio (AdministrarEncargados). "Ver sus
    /// bitacoras" abre Bitacoras ya buscando su nombre y su numero de empleado.
    /// En solo consulta (el encargado) solo se busca y se ven todo lo suyo, el
    /// horario y las bitacoras de cada uno. Desde 2026-10-31 las acciones sobre la
    /// fila elegida van juntas abajo: "Ver todo lo suyo...", "Editar" y "Mas
    /// acciones" (tambien con el clic derecho); arriba, solo lo general y
    /// "Desenlazar del SIIA", su boton aparte.
    /// </summary>
    public class AdministrarProfesores : PantallaAdmin
    {
        // Lo que se muestra es la cuenta (activa, de baja): sin "activos" ni "dados de baja".
        private const string Activos = "Cuentas activas";
        private const string DadosDeBaja = "Cuentas de baja";
        private const string Todos = "Todas";

        private readonly ProfesoresDatos datos = new ProfesoresDatos();
        private readonly Dictionary<int, string> textoBusqueda = new Dictionary<int, string>();
        private List<ProfesorInfo> profesores = new List<ProfesorInfo>();
        // El encargado de laboratorio: los botones que escriben no se crean (quedan
        // "Ver todo lo suyo...", "Ver horario", "Ver sus bitacoras" y "Cerrar") y el
        // doble clic abre todo lo suyo.
        private readonly bool soloConsulta = Permisos.SoloConsulta;

        private readonly TextBox txtBuscar;
        private readonly ComboBox cboMostrar;
        private readonly Button btnEditar;
        // "Ver todo lo suyo..." (2026-10-31): la ficha del docente, solo para ver.
        // Abajo, el primero y relleno; despues "Editar" y "Mas acciones" (2026-10-31:
        // las acciones sobre la fila elegida, juntas abajo; antes, 11 botones arriba y abajo).
        private readonly Button btnVerTodo;
        // Solo consulta: "Ver horario" y "Ver sus bitacoras" como botones, junto a
        // "Ver todo lo suyo...". La administracion los tiene en "Mas acciones".
        private readonly Button btnHorario;
        private readonly Button btnBitacoras;
        // La administracion: "Mas acciones" y el mismo menu con el clic derecho en la fila.
        private readonly Button btnMas;
        private readonly ContextMenuStrip menuMas;
        private readonly ToolStripMenuItem opHorario;
        private readonly ToolStripMenuItem opBitacoras;
        private readonly ToolStripMenuItem opDatosPersonales;
        private readonly ToolStripMenuItem opContrasena;
        private readonly ToolStripMenuItem opBaja;
        private readonly Button btnEncargados;
        // El enlace de cada docente con el SIIA (db/24): la columna, la franja de
        // quienes esperan (con "Enlazar...") y "Desenlazar del SIIA". Sin la 24, nada.
        private LecturaEnlaces enlaces = LecturaEnlaces.Ninguna;
        private readonly Panel pnlPorEnlazar;
        private readonly Label lblPorEnlazar;
        private readonly Button btnDesenlazar;
        private bool franjaVisible;

        /// <summary>Ver las bitacoras de un profesor: Bitacoras ya buscando su nombre (la abre el panel principal).</summary>
        public event Action<string> VerBitacoras;

        /// <summary>"Ver todo lo suyo...": la ficha del docente elegido (la abre el panel principal, una por docente).</summary>
        public event Action<ProfesorInfo> VerTodo;

        /// <summary>Quien esta enlazado con el SIIA y quien espera, de la ultima lectura (para la ficha del docente).</summary>
        internal LecturaEnlaces Enlaces
        {
            get { return enlaces; }
        }

        // 1400 de ancho pedidos: a 1366x768 PantallaAdmin la deja en lo que cabe
        // (unos 1350) y el Correo entero cabe a 10 pt tambien con la columna Estado.
        public AdministrarProfesores()
            : base("Docentes", Subtitulo(), new Size(1400, 660))
        {
            // Arriba, solo lo general: "Registrar docente" y "Responsables de laboratorio...".
            if (!soloConsulta)
            {
                Button registrar = BotonPrincipalArriba("Registrar docente", 180);
                registrar.Click += (s, e) => NuevoProfesor();
            }

            txtBuscar = CrearBuscador(new Point(Margen, 112), 330);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Mostrar", new Point(380, 86)));
            cboMostrar = EstiloHorarios.CrearCombo(new Point(380, 111), 170);
            cboMostrar.Items.AddRange(new object[] { Activos, DadosDeBaja, Todos });
            cboMostrar.SelectedIndex = 0;
            cboMostrar.SelectedIndexChanged += (s, e) => Aplicar(null);
            Controls.Add(cboMostrar);

            if (!soloConsulta)
            {
                // Los encargados de laboratorio (solo consulta) son personal con numero
                // de empleado, como los profesores: se administran en su propia ventana.
                // A la derecha de los filtros, no junto a "Registrar docente": ahi quedaria
                // pegado al subtitulo y lo taparia en cuanto se estrechara la ventana.
                btnEncargados = EstiloHorarios.CrearBoton("Responsables de laboratorio...",
                    new Point(ClientSize.Width - Margen - 260, 104), new Size(260, 40));
                btnEncargados.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnEncargados.Click += (s, e) => AbrirEncargados();
                ayuda.SetToolTip(btnEncargados, "Quienes ven el panel, las alertas, las bitácoras y los horarios sin poder cambiar nada.");
                Controls.Add(btnEncargados);

                // Deshacer un enlace con el SIIA hecho por error (solo con db/24: Recargar lo
                // ensena). Su boton aparte, en la fila de los filtros (no en "Mas acciones").
                btnDesenlazar = EstiloHorarios.CrearBoton("Desenlazar del SIIA",
                    new Point(btnEncargados.Left - 10 - 190, 104), new Size(190, 40));
                btnDesenlazar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnDesenlazar.Visible = false;
                btnDesenlazar.Click += (s, e) => DesenlazarSeleccionado();
                ayuda.SetToolTip(btnDesenlazar, "Desenlazar del SIIA. Si enlazaste a esta persona con un número de empleado que no es el suyo: deja de entrar con él y su solicitud vuelve a «Docentes por enlazar».");
                Controls.Add(btnDesenlazar);
                AcomodarDesenlazar();
            }

            // Quienes entraron con el SIIA y esperan que los enlacen: una franja sobre
            // la lista, solo mientras haya alguno (ColocarFranja).
            pnlPorEnlazar = new Panel
            {
                BackColor = Color.FromArgb(230, 241, 251),
                Location = new Point(Margen, 152),
                Size = new Size(ClientSize.Width - 2 * Margen, 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Visible = false
            };
            Button btnPorEnlazar = EstiloHorarios.CrearBoton(soloConsulta ? "Ver quién espera..." : "Enlazar...", new Point(0, 0), new Size(soloConsulta ? 180 : 130, 32));
            btnPorEnlazar.Dock = DockStyle.Right;
            btnPorEnlazar.Click += (s, e) => AbrirPorEnlazar();
            lblPorEnlazar = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 60, 110),
                Padding = new Padding(8, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlPorEnlazar.Controls.Add(lblPorEnlazar);
            pnlPorEnlazar.Controls.Add(btnPorEnlazar);
            Controls.Add(pnlPorEnlazar);

            CrearLista(156);
            // Diez columnas: con el margen de 8 de las demas listas, a 1366 de ancho
            // el nombre se cortaba. Aqui, 4.
            grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 7, 4, 7);
            grid.Columns.Add("numero", "No. de empleado");
            grid.Columns.Add("nombre", "Nombre");
            grid.Columns.Add("codigo", "Código de clase");
            grid.Columns.Add("correo", "Correo");
            grid.Columns.Add("clases", "Clases vigentes");
            grid.Columns.Add("laboratorios", "Laboratorios");
            grid.Columns.Add("sesiones", "Sesiones");
            grid.Columns.Add("ultima", "Última sesión");
            grid.Columns.Add("estado", "Estado");
            // Enlazado / Pendiente (pidio enlace) / Sin enlazar: solo con db/24 (Recargar).
            grid.Columns.Add("enlace", "Enlace con el SIIA");
            grid.Columns["enlace"].Visible = false;
            // A 10 pt: el Correo entero a 1366 de ancho, tambien con Estado
            // (ColumnasDeLista.TextoEntero, al leer); las cortas, con el peso de su
            // encabezado entero (ListaDigLab no las deja mas angostas que su titulo),
            // y lo demas al nombre y a los laboratorios, con algo mas para el nombre: es
            // lo que se busca en la fila, y a 1366 "Domínguez Peña, Héctor" cabe entero
            // (los laboratorios de una fila casi nunca caben enteros a ese ancho).
            EstiloHorarios.Pesos(grid, 105, 230, 90, 255, 80, 220, 95, 100, 127, 150);
            grid.Columns["clases"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns["sesiones"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            // Con su codigo entran sus alumnos: quien no ve los codigos de los
            // profesores (el encargado) no tiene la columna, ni antes de leer la lista.
            grid.Columns["codigo"].Visible = Permisos.VeCodigosDeProfesor;

            // Abajo, de izquierda a derecha: "Ver todo lo suyo..." (relleno), "Editar",
            // "Mas acciones ▾" y, aparte, "Cerrar" (se colocan de derecha a izquierda). La
            // responsable (solo consulta): "Ver todo lo suyo...", "Ver horario" y "Ver sus
            // bitacoras", juntos.
            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            if (soloConsulta)
            {
                btnBitacoras = BotonAbajo("Ver sus bitácoras", 170);
                btnBitacoras.Click += (s, e) => VerBitacorasSeleccionado();
                ayuda.SetToolTip(btnBitacoras, "Abre Bitácoras con sus sesiones (busca su nombre y su número de empleado).");
                btnHorario = BotonAbajo("Ver horario", 130);
                btnHorario.Click += (s, e) => VerHorarioSeleccionado();
                ayuda.SetToolTip(btnHorario, "Su horario de la semana en todos los laboratorios.");
            }
            else
            {
                menuMas = new ContextMenuStrip { Font = new System.Drawing.Font("Century Gothic", 10F), ShowItemToolTips = true };
                opHorario = new ToolStripMenuItem("Ver horario", null, (s, e) => VerHorarioSeleccionado())
                {
                    ToolTipText = "Su horario de la semana en todos los laboratorios."
                };
                opBitacoras = new ToolStripMenuItem("Ver sus bitácoras", null, (s, e) => VerBitacorasSeleccionado())
                {
                    ToolTipText = "Abre Bitácoras con sus sesiones (busca su nombre y su número de empleado)."
                };
                // Un submenu (Exportar sus datos, Anonimizar): ActualizarBotones lo llena con la fila elegida.
                opDatosPersonales = new ToolStripMenuItem("Datos personales")
                {
                    ToolTipText = "Exportar sus datos o, si su cuenta está de baja, anonimizarla (derechos ARCO)."
                };
                menuMas.Items.AddRange(new ToolStripItem[] { opHorario, opBitacoras, opDatosPersonales });
                // Con el SIIA entran con la contrasena del SIIA (y su NIP): aqui no se restablece.
                if (!InicioConSiia.LeerSinFallar().Docentes)
                {
                    opContrasena = new ToolStripMenuItem("Restablecer contraseña...", null, (s, e) => RestablecerContrasenaSeleccionado());
                    menuMas.Items.Add(opContrasena);
                }

                // Lo que quita algo, al final y aparte.
                menuMas.Items.Add(new ToolStripSeparator());
                opBaja = new ToolStripMenuItem("Dar de baja...", null, (s, e) => CambiarEstadoSeleccionado());
                menuMas.Items.Add(opBaja);
                // Antes de abrirse, con lo que vale para la fila elegida (ActualizarBotones).
                menuMas.Opening += (s, e) =>
                {
                    ActualizarBotones();
                    e.Cancel = Seleccionado<ProfesorInfo>() == null;
                };

                btnMas = BotonAbajo("Más acciones ▾", 160);
                btnMas.Click += (s, e) => menuMas.Show(btnMas, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
                ayuda.SetToolTip(btnMas, "Ver su horario o sus bitácoras, sus datos personales"
                    + (opContrasena != null ? ", restablecer su contraseña" : "") + " y darlo de baja. También con el clic derecho en la lista.");
                btnEditar = BotonAbajo("Editar", 110);
                btnEditar.Click += (s, e) => EditarSeleccionado();
                ayuda.SetToolTip(btnEditar, "Sus datos y su código de clase. También con doble clic.");

                // El mismo menu con el clic derecho en una fila (la elige primero).
                grid.CellMouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    {
                        return;
                    }

                    grid.ClearSelection();
                    grid.Rows[e.RowIndex].Selected = true;
                    DataGridViewCell celda = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
                    if (celda.Visible)
                    {
                        grid.CurrentCell = celda;
                    }
                };
                grid.ContextMenuStrip = menuMas;
                // La lista no lo suelta al cerrarse (no es suyo).
                Disposed += (s, e) => menuMas.Dispose();
            }

            // El principal (relleno): es lo que mas se abre y lo que hace el doble clic de la responsable.
            btnVerTodo = BotonAbajo("Ver todo lo suyo...", 170);
            EstiloAdmin.ComoPrincipal(btnVerTodo);
            btnVerTodo.Click += (s, e) => VerTodoSeleccionado();
            ayuda.SetToolTip(btnVerTodo, "De la persona elegida: sus clases y faltas, sus grupos con la lista de cada uno, gráficas, fallas y lo que dio, en el periodo que elijas.");

            ActiveControl = txtBuscar;
            ActualizarBotones();

            // Otro reparto (el panel cada 30 s, Horarios) puede cambiar colores: la
            // columna de nombres se vuelve a pintar.
            ColoresProfesor.Cambio += ColoresCambiaron;
            FormClosed += (s, e) => ColoresProfesor.Cambio -= ColoresCambiaron;
        }

        /// <summary>
        /// Sin "codigo" si no se ven los codigos de los profesores (no se buscan), y
        /// el doble clic edita o, en solo consulta, abre su horario.
        /// </summary>
        private static string Subtitulo()
        {
            return (Permisos.VeCodigosDeProfesor
                    ? "Busca por número de empleado, nombre, correo o código."
                    : "Busca por número de empleado, nombre o correo.")
                + (Permisos.SoloConsulta
                    ? " Doble clic para ver todo lo suyo."
                    : " Doble clic para editar.")
                + " F5 actualiza.";
        }

        /// <summary>F5: vuelve a leer la lista, como en Bitacoras (la fila elegida se conserva).</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    Recargar(null);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
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

            PintarColores();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AcomodarDesenlazar();
        }

        /// <summary>
        /// "Desenlazar del SIIA" a la izquierda de "Responsables de laboratorio...", en la fila de
        /// los filtros (su boton aparte; "Datos personales" paso a "Mas acciones"): con poco ancho
        /// (a 1000, el minimo, o a 1366 al 125 %) tapaba la mitad del combo "Mostrar", con su
        /// flecha. Entonces va con el texto corto ("Desenlazar"; lo demas, en su ayuda) y del
        /// ancho que queda.
        /// </summary>
        private void AcomodarDesenlazar()
        {
            if (btnDesenlazar == null || btnEncargados == null || cboMostrar == null)
            {
                return;
            }

            const int Largo = 190;
            int derecha = btnEncargados.Left - 10;
            int izquierda = cboMostrar.Right + 10;
            bool cabe = derecha - Largo >= izquierda;
            int ancho = cabe ? Largo : Math.Max(100, derecha - izquierda);
            btnDesenlazar.Text = cabe ? "Desenlazar del SIIA" : "Desenlazar";
            btnDesenlazar.SetBounds(derecha - ancho, btnEncargados.Top, ancho, btnEncargados.Height);
        }

        protected override void Recargar(string seleccionar)
        {
            try
            {
                profesores = datos.Listar();
            }
            catch (Exception ex)
            {
                profesores = new List<ProfesorInfo>();
                AvisoDeFalloAdmin.Mostrar(this, "Leer la lista de profesores", ex, "No se pudo leer la lista de docentes", Text);
            }

            // Quien esta enlazado con el SIIA y quien espera (db/24). Si falla, la
            // lista se ensena igual, sin la columna.
            try
            {
                enlaces = EnlacesSiia.Leer();
            }
            catch (Exception ex)
            {
                Log.Error("Docentes: leer los enlaces con el SIIA", ex);
                enlaces = LecturaEnlaces.Ninguna;
            }

            grid.Columns["enlace"].Visible = enlaces.Disponible;
            if (btnDesenlazar != null)
            {
                btnDesenlazar.Visible = enlaces.Disponible;
            }

            MostrarPorEnlazar();

            // El color de cada uno, el mismo que en Horarios: la lista lo ensena y
            // el dialogo dice que profesor usa ya cada color.
            datos.RepartirColores();

            // Sin los codigos de los profesores (el encargado), el codigo ni se ve ni se busca.
            bool conCodigo = Permisos.VeCodigosDeProfesor;
            textoBusqueda.Clear();
            foreach (ProfesorInfo profesor in profesores)
            {
                textoBusqueda[profesor.Id] = EstiloAdmin.Normalizar(profesor.Matricula + " " + profesor.NombreCompleto + " " + profesor.Correo
                    + (conCodigo ? " " + profesor.Codigo : ""));
            }

            cboMostrar.Enabled = datos.BajasDisponibles;
            grid.Columns["codigo"].Visible = datos.CodigoPorProfesor && conCodigo;
            // En solo consulta no hay "Dar de baja": la nota no le dice nada.
            NotaFija = soloConsulta || datos.BajasDisponibles ? "" : "Para dar de baja falta aplicar la migración 08 en la base.";
            Aplicar(seleccionar);
        }

        protected override void AlBuscar()
        {
            Aplicar(null);
        }

        private void Aplicar(string seleccionar)
        {
            string mostrar = datos.BajasDisponibles ? (string)cboMostrar.SelectedItem : Todos;
            List<ProfesorInfo> visibles = profesores.Where(p =>
                    (mostrar == Todos || (mostrar == Activos) == p.Activo)
                    && EstiloAdmin.Coincide(textoBusqueda[p.Id], txtBuscar.Text))
                .ToList();

            grid.Columns["estado"].Visible = mostrar != Activos;
            LlenarLista(visibles,
                p => new object[]
                {
                    p.Matricula,
                    p.NombreParaLista,
                    Permisos.VeCodigosDeProfesor ? EstiloAdmin.Guion(EstiloAdmin.Codigo(p.Codigo)) : "",
                    p.Correo,
                    p.ClasesActivas,
                    EstiloAdmin.Guion(p.Laboratorios),
                    p.Sesiones,
                    EstiloAdmin.Fecha(p.UltimaSesion),
                    AdministrarAlumnos.EstadoDe(p),
                    EstadoDelEnlace(p)
                },
                p => !p.Activo, p => p.Matricula, seleccionar);
            // El correo entero (a 10 pt y 1366 de ancho se cortaba); las demas, con lo que quede.
            // Tambien el enlace con el SIIA ("Pendiente (pidio enlace)"), si se ve.
            ColumnasDeLista.TextoEntero(grid, 330, "correo");
            ColumnasDeLista.TextoEntero(grid, 220, "enlace");
            // Las de numeros y fechas, de lo que miden (antes, a 1920, 150 px para un "0"):
            // el nombre y los laboratorios se reparten el resto por sus pesos. Con los
            // laboratorios fijos (hasta 460, como pedia el plan) el nombre se quedaba en
            // su minimo a 1366 y salia la barra de abajo.
            ColumnasDeLista.TextoEntero(grid, 170, "numero", "codigo", "clases", "sesiones", "ultima", "estado");

            PintarColores();

            int bajas = profesores.Count(p => !p.Activo);
            // Lo mismo, en medio de la lista vacia (no un rectangulo blanco).
            ListaDigLab.PonerTextoVacio(grid, profesores.Count == 0 ? "Todavía no hay cuentas docentes."
                : "Nadie coincide con la búsqueda. Borra la búsqueda o cambia «Mostrar».");
            if (profesores.Count == 0)
            {
                lblResumen.Text = "Todavía no hay cuentas docentes.";
            }
            else if (visibles.Count == 0)
            {
                lblResumen.Text = "Nadie coincide con la búsqueda.";
            }
            else
            {
                lblResumen.Text = (visibles.Count == profesores.Count ? "" : visibles.Count + " de ")
                    + profesores.Count + (profesores.Count == 1 ? " docente" : " docentes")
                    + (bajas > 0 ? " (" + bajas + " de baja)" : "");
            }
        }

        /// <summary>
        /// El nombre con su color, como la columna Profesor de Clases. Sin color
        /// todavia (sin clases ni color elegido), en blanco: el que se veria seria
        /// provisional.
        /// </summary>
        private void PintarColores()
        {
            foreach (DataGridViewRow fila in grid.Rows)
            {
                ProfesorInfo profesor = (ProfesorInfo)fila.Tag;
                DataGridViewCell nombre = fila.Cells["nombre"];
                if (!ColoresProfesor.TieneColor(profesor.Id))
                {
                    nombre.Style.BackColor = Color.Empty;
                    nombre.Style.SelectionBackColor = Color.Empty;
                    nombre.Style.SelectionForeColor = Color.Empty;
                    continue;
                }

                Color color = ColoresProfesor.De(profesor.Id);
                nombre.Style.BackColor = color;
                nombre.Style.SelectionBackColor = EstiloHorarios.Oscurecer(color, 0.18f);
                nombre.Style.SelectionForeColor = Color.Black;
            }
        }

        /// <summary>El color que tiene hoy cada profesor de la lista, para ver despues a quien le cambio.</summary>
        private Dictionary<int, int> ColoresDeAhora()
        {
            return profesores.Where(p => ColoresProfesor.TieneColor(p.Id)).ToDictionary(p => p.Id, p => ColoresProfesor.De(p.Id).ToArgb());
        }

        /// <summary>" También cambió el color de Ana Ruiz y Beto Pérez." o "".</summary>
        private string OtrosQueCambiaron(Dictionary<int, int> antes, string salvoMatricula)
        {
            List<string> cambiaron = profesores
                .Where(p => p.Matricula != salvoMatricula && antes.ContainsKey(p.Id) && ColoresProfesor.TieneColor(p.Id)
                    && ColoresProfesor.De(p.Id).ToArgb() != antes[p.Id])
                .Select(p => HorariosDatos.NombreCorto(p.Nombre, p.ApellidoPaterno))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return cambiaron.Count == 0 ? "" : " También cambió el color de " + EstiloAdmin.Enumerar(cambiaron) + ".";
        }

        protected override void ActualizarBotones()
        {
            // Antes de crear los botones (la lista va primero) no hay nada que hacer.
            // "Ver todo lo suyo..." esta siempre (es el ultimo que se crea).
            if (btnVerTodo == null)
            {
                return;
            }

            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            // Una cuenta anonimizada ya no se edita ni se reactiva (UsuariosDatos.Anonimizar).
            bool anonimizada = profesor != null && profesor.Anonimizada;
            // Tambien de baja o anonimizada: su historia se sigue viendo.
            btnVerTodo.Enabled = profesor != null;
            if (btnHorario != null)
            {
                btnHorario.Enabled = profesor != null;
                btnBitacoras.Enabled = profesor != null;
            }

            if (btnEditar != null)
            {
                btnEditar.Enabled = profesor != null && !anonimizada;
            }

            if (menuMas != null)
            {
                btnMas.Enabled = profesor != null;
                opHorario.Enabled = profesor != null;
                opBitacoras.Enabled = profesor != null;
                opDatosPersonales.Enabled = profesor != null;
                LlenarDatosPersonales(profesor);
                if (opContrasena != null)
                {
                    opContrasena.Enabled = profesor != null && profesor.Activo;
                    opContrasena.ToolTipText = profesor != null && !profesor.Activo ? "Su cuenta está de baja: reactívala primero."
                        : "Una contraseña nueva para que vuelva a entrar.";
                }

                // Reactivar no pregunta (no quita nada): sin "...". Dar de baja pregunta antes.
                opBaja.Enabled = profesor != null && datos.BajasDisponibles && !anonimizada;
                opBaja.Text = profesor != null && !profesor.Activo ? "Reactivar" : "Dar de baja...";
                opBaja.ToolTipText = !datos.BajasDisponibles ? "Para dar de baja falta aplicar la migración 08 en la base."
                    : anonimizada ? "La cuenta está anonimizada: ya no se reactiva."
                    : profesor != null && !profesor.Activo ? "Vuelve a dejarlo entrar a DigLab. Sus clases no vuelven solas: se alargan o se activan en Horarios."
                    : "Ya no podrá entrar a DigLab; sus sesiones y las bitácoras de sus grupos se conservan. Antes de hacerlo, se pregunta.";
            }

            if (btnDesenlazar != null)
            {
                btnDesenlazar.Enabled = profesor != null && enlaces.Enlazados.ContainsKey(profesor.Id);
            }
        }

        /// <summary>
        /// "Enlazado" (la administracion lo enlazo con su numero de empleado del
        /// SIIA), "Pendiente (pidio enlace)" (alguien que entro con el SIIA tiene
        /// su numero o su nombre y espera) o "Sin enlazar". Sin db/24, nada.
        /// </summary>
        private string EstadoDelEnlace(ProfesorInfo profesor)
        {
            if (!enlaces.Disponible)
            {
                return "";
            }

            if (enlaces.Enlazados.ContainsKey(profesor.Id))
            {
                return "Enlazado";
            }

            return profesor.Activo && enlaces.Pendientes.Any(s => EnlacesSiia.Coincidencia(s, profesor).Length > 0)
                ? "Pendiente (pidió enlace)" : "Sin enlazar";
        }

        /// <summary>La franja de quienes esperan que los enlacen, solo si hay alguno; la lista baja lo que ocupa.</summary>
        private void MostrarPorEnlazar()
        {
            int cuantos = enlaces.Pendientes.Count;
            bool mostrar = enlaces.Disponible && cuantos > 0;
            if (mostrar)
            {
                List<SolicitudSiia> primeros = enlaces.Pendientes.Take(3).ToList();
                // La persona responsable no enlaza (solo consulta): espera a la administracion.
                lblPorEnlazar.Text = (Permisos.SoloConsulta
                        ? (cuantos == 1 ? "1 docente entró con su contraseña del SIIA y espera que la administración lo enlace: "
                            : cuantos + " docentes entraron con su contraseña del SIIA y esperan que la administración los enlace: ")
                        : (cuantos == 1 ? "1 docente entró con su contraseña del SIIA y espera que lo enlaces: "
                            : cuantos + " docentes entraron con su contraseña del SIIA y esperan que los enlaces: "))
                    + string.Join(", ", primeros.Select(s => s.Numero + " " + s.NombreParaMostrar))
                    + (cuantos > primeros.Count ? " y " + (cuantos - primeros.Count) + " más." : ".");
            }

            if (mostrar == franjaVisible || grid == null)
            {
                return;
            }

            const int Franja = 46;
            grid.Top += mostrar ? Franja : -Franja;
            grid.Height += mostrar ? -Franja : Franja;
            pnlPorEnlazar.Visible = mostrar;
            franjaVisible = mostrar;
        }

        /// <summary>
        /// La lista de quienes esperan, para enlazarlos con un docente de esta
        /// lista (o descartarlos). La abre la franja y "Docentes por enlazar" de
        /// Alertas. La persona responsable del laboratorio la ve sin poder enlazar.
        /// </summary>
        public void AbrirPorEnlazar()
        {
            if (IsDisposed)
            {
                return;
            }

            // Con lo de ahora: desde Alertas la ventana pudo estar abierta hace rato.
            Cursor = Cursors.WaitCursor;
            try
            {
                Recargar(null);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (!enlaces.Disponible)
            {
                MessageBox.Show(this, "Para enlazar docentes con el SIIA falta aplicar db/24_docentes_siia.sql en la base. "
                    + "Pide a quien administra el servidor que la aplique.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<ProfesorInfo> sinEnlazar = profesores
                .Where(p => p.Activo && !p.Anonimizada && !enlaces.Enlazados.ContainsKey(p.Id))
                .ToList();
            bool cambios;
            using (DialogoEnlazarSiia dialogo = new DialogoEnlazarSiia(enlaces.Pendientes, sinEnlazar, null))
            {
                dialogo.ShowDialog(this);
                cambios = dialogo.HuboCambios;
            }

            if (cambios)
            {
                Recargar(null);
            }
        }

        /// <summary>
        /// Deshace el enlace con el SIIA del docente elegido (un error al enlazar):
        /// vuelve a su numero de antes (o uno provisional) y su solicitud vuelve a
        /// "Docentes por enlazar". Solo la administracion, con confirmacion.
        /// </summary>
        private void DesenlazarSeleccionado()
        {
            if (Permisos.Bloquear(this, "Desenlazar un docente del SIIA")) return;

            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null || !enlaces.Enlazados.ContainsKey(profesor.Id))
            {
                return;
            }

            if (!DialogoConfirmacion.Preguntar(this, "Desenlazar del SIIA", "¿Desenlazar a " + profesor.NombreCompleto + " del SIIA?",
                profesor.NombreCompleto + " deja de entrar con el número " + profesor.Matricula + " y la contraseña del SIIA; su número en DigLab vuelve "
                + "a ser el de antes (o uno provisional). La solicitud de " + profesor.Matricula + " vuelve a «Docentes por enlazar» para que la enlaces "
                + "con quien sí es. Sus clases y bitácoras no cambian.", "Desenlazar"))
            {
                return;
            }

            string nuevo;
            Cursor = Cursors.WaitCursor;
            try
            {
                nuevo = EnlacesSiia.Desenlazar(profesor);
            }
            catch (ErrorDeDatos ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Recargar(null);
                return;
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MostrarError("Desenlazar del SIIA al docente id " + profesor.Id, ex, "No se pudo desenlazar");
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            Recargar(nuevo);
            Avisar(profesor.NombreCompleto + " quedó desenlazado del SIIA; ahora su número en DigLab es " + nuevo + ".");
        }

        protected override void AlDobleClic()
        {
            // En solo consulta no se edita: el doble clic (y Enter) abre todo lo suyo (su
            // horario queda a un clic, en la ficha).
            if (soloConsulta)
            {
                VerTodoSeleccionado();
                return;
            }

            EditarSeleccionado();
        }

        /// <summary>
        /// Los encargados de laboratorio (ven el panel en solo consulta), en su
        /// propia ventana: alta, edicion, contrasena y baja. Solo el administrador.
        /// </summary>
        private void AbrirEncargados()
        {
            if (Permisos.Bloquear(this, "Abrir los encargados de laboratorio")) return;

            using (AdministrarEncargados encargados = new AdministrarEncargados())
            {
                encargados.ShowDialog(this);
            }
        }

        private void NuevoProfesor()
        {
            if (Permisos.Bloquear(this, "Dar de alta un profesor")) return;

            using (DialogoUsuario dialogo = new DialogoUsuario(datos, null, 0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Dictionary<int, int> antes = ColoresDeAhora();
                    Recargar(dialogo.IdentificadorGuardado);
                    Avisar("Se registró a " + dialogo.NombreGuardado
                        + (string.IsNullOrEmpty(dialogo.CodigoGuardado) ? "" : ", con el código " + dialogo.CodigoGuardado)
                        + ". Sus clases se crean en Horarios." + OtrosQueCambiaron(antes, dialogo.IdentificadorGuardado));
                }
            }
        }

        private void EditarSeleccionado()
        {
            if (Permisos.Bloquear(this, "Editar un profesor")) return;

            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            if (profesor.Anonimizada)
            {
                // Tambien con el doble clic: la cuenta ya no tiene datos que editar.
                // Corto: a 1366 la linea de estado de Docentes mide unos 320 px.
                Avisar("Cuenta anonimizada: ya no se puede editar.");
                return;
            }

            using (DialogoUsuario dialogo = new DialogoUsuario(datos, profesor, 0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Dictionary<int, int> antes = ColoresDeAhora();
                    Recargar(dialogo.IdentificadorGuardado);
                    bool cambioCodigo = !string.IsNullOrEmpty(dialogo.CodigoGuardado) && dialogo.CodigoGuardado != profesor.Codigo;
                    Avisar("Se guardaron los cambios de " + dialogo.NombreGuardado
                        + (cambioCodigo ? ". Su código ahora es " + dialogo.CodigoGuardado + ": avísale." : ".")
                        + (dialogo.ColorCambiado ? " Su color nuevo se ve en Horarios y en el panel." : "")
                        + OtrosQueCambiaron(antes, dialogo.IdentificadorGuardado));
                }
            }
        }

        /// <summary>
        /// "Ver todo lo suyo...": la ficha del docente elegido (FichaDocente), solo para ver.
        /// Con el panel principal, una ventana por docente (si ya esta abierta, al frente).
        /// </summary>
        private void VerTodoSeleccionado()
        {
            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            if (VerTodo != null)
            {
                VerTodo(profesor);
                return;
            }

            // Sin el panel principal (no pasa hoy): modal, como el historial de un estudiante.
            using (FichaDocente ficha = new FichaDocente(profesor, enlaces))
            {
                ficha.ShowDialog(this);
            }
        }

        /// <summary>Su horario semanal en todos los laboratorios, solo para verlo.</summary>
        private void VerHorarioSeleccionado()
        {
            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            using (BitacoraMisClases horario = new BitacoraMisClases(profesor.Id, profesor.NombreCompleto))
            {
                horario.ShowDialog(this);
            }
        }

        /// <summary>
        /// Bitacoras con las sesiones del elegido: busca su nombre y su numero de
        /// empleado (todas sus palabras, en cualquier orden), sin otros filtros. Con
        /// el nombre solo tambien saldria otro: la busqueda encuentra cada palabra
        /// dentro de otras ("Ana Ruiz" esta en "Adriana Ruiz"). Solo lee.
        /// </summary>
        private void VerBitacorasSeleccionado()
        {
            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            string texto = ((profesor.NombreCompleto ?? "") + " " + (profesor.Matricula ?? "")).Trim();
            if (VerBitacoras != null)
            {
                VerBitacoras(texto);
                return;
            }

            // Sin el panel principal (no pasa hoy): una ventana suya.
            GestionBitacorasAdministrador bitacoras = new GestionBitacorasAdministrador();
            bitacoras.Buscar(texto);
            bitacoras.Show(this);
        }

        /// <summary>
        /// "Datos personales" de "Mas acciones" (un submenu, con la fila elegida):
        /// exportar los datos de quien este elegido (su perfil, sus clases y sus
        /// sesiones) o, si su cuenta esta de baja y ya no tiene clases que sigan
        /// despues de hoy, anonimizarla (DerechosArco). Los avisos, cortos: la linea
        /// de estado mide unos 320 px a 1366 (la ruta del archivo sale en un mensaje).
        /// </summary>
        private void LlenarDatosPersonales(ProfesorInfo profesor)
        {
            ToolStripItemCollection opciones = opDatosPersonales.DropDownItems;
            while (opciones.Count > 0)
            {
                opciones[0].Dispose();
            }

            if (profesor == null)
            {
                return;
            }

            DerechosArco.LlenarMenu(opciones, this, profesor, () =>
            {
                string ruta = DerechosArco.ExportarDocente(this, profesor, datos);
                if (ruta != null)
                {
                    Avisar("Se exportaron sus datos.");
                    DerechosArco.AvisarExportado(this, ruta);
                }
            }, () =>
            {
                if (DerechosArco.Anonimizar(this, datos, profesor, true))
                {
                    // Buscandola por su identificador nuevo: con la busqueda de antes (su
                    // nombre o su numero) ya no saldria.
                    string anonimo = UsuariosDatos.IdentificadorAnonimo(profesor.Id);
                    txtBuscar.Text = anonimo;
                    Recargar(anonimo);
                    Avisar("Cuenta anonimizada: ahora es " + anonimo + ".");
                }
            });
        }

        private void RestablecerContrasenaSeleccionado()
        {
            if (Permisos.Bloquear(this, "Restablecer la contrasena de un profesor")) return;

            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            using (DialogoContrasena dialogo = new DialogoContrasena(profesor.NombreCompleto + " (" + profesor.Matricula + ")"))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    datos.RestablecerContrasena(profesor.Id, dialogo.Contrasena);
                    Avisar("Contraseña nueva para " + profesor.NombreCompleto + ". Ya puede entrar con ella.");
                }
                catch (Exception ex)
                {
                    AvisoDeFalloAdmin.Mostrar(this, "Restablecer la contrasena de " + profesor.Matricula, ex, "No se pudo cambiar la contraseña", Text);
                }
            }
        }

        private void CambiarEstadoSeleccionado()
        {
            if (Permisos.Bloquear(this, "Dar de baja o reactivar un profesor")) return;

            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            try
            {
                if (!profesor.Activo)
                {
                    // Reactivar no quita nada a nadie: no se pregunta. Sus clases no
                    // vuelven solas: las que se terminaron al darlo de baja se alargan
                    // (sus fechas) y las que se desactivaron se activan, en Horarios.
                    datos.Reactivar(profesor.Id);
                    Recargar(profesor.Matricula);
                    Avisar("Se reactivó a " + profesor.NombreCompleto + ".");
                    if (profesor.ClasesActivas == 0)
                    {
                        MessageBox.Show(this, "Se reactivó a " + profesor.NombreCompleto + ", pero no tiene clases vigentes." + Environment.NewLine + Environment.NewLine
                            + "Si se terminaron o se desactivaron al dar de baja su cuenta, en Horarios, pestaña Clases, edita las fechas de las terminadas "
                            + "para alargarlas o activa las desactivadas (o créale clases nuevas).",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    return;
                }

                string detalle = "Ya no podrá entrar a DigLab. Sus sesiones y las bitácoras de sus grupos se conservan en los informes, y puedes reactivar su cuenta cuando quieras.";
                string casilla = null;
                if (profesor.ClasesActivas > 0)
                {
                    // R4: con sesiones se terminan (su historia se conserva); sin ellas, se desactivan.
                    detalle = "Ya no podrá entrar a DigLab. Tiene " + profesor.ClasesActivas
                        + (profesor.ClasesActivas == 1 ? " clase vigente" : " clases vigentes")
                        + (string.IsNullOrEmpty(profesor.Laboratorios) ? "" : " en " + profesor.Laboratorios)
                        + ". Al terminarlas, las que ya se dieron acaban hoy y conservan su historia (Faltas y asistencia); "
                        + "las que nunca se dieron se desactivan. Si otra persona sigue con el grupo, créale una clase nueva en Horarios.";
                    casilla = "Terminar también " + (profesor.ClasesActivas == 1 ? "su clase" : "sus " + profesor.ClasesActivas + " clases")
                        + " (su código dejará de aceptarse en los laboratorios)";
                }

                bool terminarClases;
                if (!DialogoConfirmacion.Preguntar(this, "Dar de baja",
                    "¿Dar de baja a " + profesor.NombreCompleto + " (" + profesor.Matricula + ")?",
                    detalle, "Dar de baja", true, casilla, true, out terminarClases))
                {
                    return;
                }

                datos.DarDeBaja(profesor.Id, terminarClases);
                Recargar(profesor.Matricula);
                Avisar("Se dio de baja a " + profesor.NombreCompleto + ".");
                string clases = ClasesQuitadas(datos.ClasesTerminadas, datos.ClasesDesactivadas);
                if (clases.Length > 0)
                {
                    // En la linea de abajo no cabe: que se terminaron y que se desactivaron.
                    MessageBox.Show(this, "Se dio de baja a " + profesor.NombreCompleto + "." + Environment.NewLine + Environment.NewLine + clases,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Cambiar el estado del profesor " + profesor.Matricula, ex, "No se pudo cambiar el estado de la cuenta", Text);
            }
        }

        /// <summary>
        /// "Se terminaron 3 clases que ya tenian sesiones (su historia se conserva)
        /// y se desactivaron 2 que nunca se dieron." Vacio si no se toco ninguna.
        /// </summary>
        private static string ClasesQuitadas(int terminadas, int desactivadas)
        {
            string conSesiones = terminadas == 1 ? "Se terminó 1 clase que ya tenía sesiones (su historia se conserva)"
                : "Se terminaron " + terminadas + " clases que ya tenían sesiones (su historia se conserva)";
            if (desactivadas == 0)
            {
                return terminadas == 0 ? "" : conSesiones + ".";
            }

            string sinSesiones = desactivadas == 1 ? "1 que nunca se dio" : desactivadas + " que nunca se dieron";
            return terminadas == 0
                ? (desactivadas == 1 ? "Se desactivó su clase, que nunca se dio." : "Se desactivaron sus " + desactivadas + " clases, que nunca se dieron.")
                : conSesiones + (desactivadas == 1 ? " y se desactivó " : " y se desactivaron ") + sinSesiones + ".";
        }
    }
}
