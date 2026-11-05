using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigLab
{
    /// <summary>
    /// "Ver todo lo suyo" de Docentes (2026-10-31; el usuario: "aparte de sus configuraciones,
    /// quiero ver todo lo relacionado a el: estadisticas, lista de alumnos con sus asistencias,
    /// asistencias de el, faltas, graficas, etc."; decidio que SOLO ENSENA). Arriba su nombre,
    /// su cuenta, el periodo (la misma pieza que Estadisticas, SelectorDePeriodo) y las cinco
    /// cifras de Estadisticas con Docente = el; abajo seis pestanas: Resumen ("Para revisar" y
    /// sus clases por semana), Sus clases y faltas (su asistencia, dia por dia, con el mes de
    /// Faltas), Sus grupos (cada clase con la lista de sus estudiantes y "Ver la lista dia por
    /// dia"), Graficas, Fallas y Asistencias que dio (db/22).
    ///
    /// Todo sale de UN calculo de Estadisticas (EstadisticasDatos.Calcular con el docente fijo,
    /// FichaDocenteDatos.Armar): cada numero es el mismo que en Estadisticas, Horarios > Faltas
    /// y Mis grupos. Lee en segundo plano (Task.Run, con un numero de lectura: la que llega tarde
    /// se descarta) y casi siempre desde la lectura guardada de Estadisticas (10 minutos). Nunca
    /// escribe: ni cancela dias, ni "No tomar en cuenta", ni da o quita asistencias. La persona
    /// responsable del laboratorio la ve entera, sin guardarla en PDF. Nunca ensena codigos.
    /// En kiosco no hay PDF y, al cerrar, se vacia lo que DigLab dejo en el portapapeles.
    /// </summary>
    internal sealed class FichaDocente : PantallaAdmin
    {
        private const string TextoLeyendo = "Leyendo...";
        private const string TextoCalculando = "Calculando...";
        private const string NotaSinRegistro = "Sin registro no siempre es falta del docente.";
        private const int Separacion = 10;
        private const int AltoMinimoNormal = 640;
        private const int AltoCifrasBaja = 78;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Color BordeFicha = Color.FromArgb(215, 222, 215);
        private static readonly Color TextoGris = Color.FromArgb(105, 105, 105);
        private static readonly Color TextoOscuro = Color.FromArgb(40, 40, 40);
        private static readonly Color RojoTexto = HojaAsistencia.FaltaTexto;
        private static readonly Color VerdeTexto = Color.FromArgb(46, 125, 50);
        private static readonly Color NaranjaTexto = Color.FromArgb(190, 85, 0);
        private static readonly Color FondoRepetido = Color.FromArgb(255, 240, 214);

        // Lo ultimo que se eligio en cualquier ficha (para comparar docentes con el mismo
        // periodo), mientras DigLab siga abierto. No toca lo que recuerda Estadisticas.
        private static PeriodoElegido ultimoPeriodo = PeriodoElegido.De(TipoPeriodo.Ciclo);
        private static AgrupacionGraficas? agrupacionElegida;

        private readonly DrawingFont fuenteNegrita = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
        private readonly DrawingFont fuenteTituloFicha = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
        private readonly DrawingFont fuenteRotulo = new DrawingFont("Century Gothic", 8F, FontStyle.Bold);
        private readonly DrawingFont fuenteValor = new DrawingFont("Century Gothic", 10F);
        private readonly DrawingFont fuenteLinea = new DrawingFont("Century Gothic", 9F);
        private readonly DrawingFont fuentePestana = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
        private readonly DrawingFont fuenteOpcion = new DrawingFont("Century Gothic", 9.5F);
        private readonly EstiloGrafica estiloGrafica = EstiloGrafica.Pantalla();

        private readonly int id;
        private ProfesorInfo profesor;
        private LecturaEnlaces enlaces;
        private readonly bool construida;
        private readonly Label lblTitulo;
        private readonly Label lblSubtitulo;
        private readonly Panel cuadro;
        private readonly SelectorDePeriodo selector;
        private readonly Button btnHorario;
        private readonly Button btnBitacoras;
        private readonly Button btnFaltaAsignar;
        private readonly CifraGrande[] cifras = new CifraGrande[5];
        private readonly TabControl tabs;
        private readonly TabPage tpResumen;
        private readonly TabPage tpClases;
        private readonly TabPage tpGrupos;
        private readonly TabPage tpGraficas;
        private readonly TabPage tpFallas;
        private readonly TabPage tpDadas;
        private readonly Button btnPdf;
        private readonly Button btnEstadisticas;
        private readonly Button btnActualizar;
        private readonly Timer espera = new Timer { Interval = 300 };

        // Resumen.
        private readonly Label lblParaRevisar;
        private readonly VentanaEstadisticas.Tabla<PendienteDeDocente> tPendientes;
        private readonly VentanaEstadisticas.Ficha fichaGrafica;
        private readonly Label lblClicGraficas;
        private readonly Label lblSinClases;
        private LienzoGrafica lienzo;
        // Del estado y no de Visible (falso mientras la ventana no se ve): su color y "Falta asignar".
        private bool conColor;
        private bool conFaltaAsignar;
        private ColumnasPorTramo grafica;

        // Sus clases y faltas.
        private readonly Label lblLineaClases;
        private readonly Label lblMostrar;
        private readonly RadioButton[] rbMostrar = new RadioButton[5];
        private readonly MesDeBitacoras mes;
        private readonly LeyendaDelMes leyenda;
        private readonly Label lblSemana;
        private readonly LinkLabel lnkQuitarSemana;
        private readonly VentanaEstadisticas.Tabla<ClaseDiaDocente> tDias;
        private MostrarDias mostrar = MostrarDias.Todas;
        private DateTime? semana;

        // Sus grupos.
        private readonly Label lblLineaGrupos;
        private readonly VentanaEstadisticas.Tabla<FilaClase> tClases;
        private readonly VentanaEstadisticas.Ficha fichaClase;
        private readonly Label lblTituloClase;
        private readonly VentanaEstadisticas.Rotulo rMateria;
        private readonly VentanaEstadisticas.Rotulo rGrupo;
        private readonly VentanaEstadisticas.Rotulo rLaboratorio;
        private readonly VentanaEstadisticas.Rotulo rLista;
        private readonly Label lblDatosClase;
        private readonly VentanaEstadisticas.Tabla<ParDeAsistencia> tLista;
        private readonly Button btnListaDia;
        private readonly Button btnSusBitacoras;
        private Dictionary<ParDeAsistencia, int> numeroEnLista = new Dictionary<ParDeAsistencia, int>();
        private bool leyendoAlumno;

        // Graficas.
        private readonly Label lblLineaGraficas;
        private readonly Label lblAgrupar;
        private readonly RadioButton rbPorSemana;
        private readonly RadioButton rbPorMes;
        private readonly PanelDeGraficas panelGraficas;
        private bool poniendoAgrupacion;

        // Fallas.
        private readonly Label lblLineaFallas;
        private readonly Label lblTituloReportes;
        private readonly Label lblSinReportes;
        private readonly VentanaEstadisticas.Tabla<FilaReporteDocente> tReportes;
        private readonly Label lblTituloEquipos;
        private readonly VentanaEstadisticas.Tabla<FilaEquipo> tEquipos;

        // Asistencias que dio (db/22).
        private readonly Label lblLineaDadas;
        private readonly VentanaEstadisticas.Tabla<FilaAsistenciaDada> tDadas;

        // Lo pedido y lo leido.
        private PeriodoElegido periodo;
        private PeriodoElegido periodoVisto;
        private LecturaEstadisticas lectura;
        private DatosDeLaFicha datos;
        private string error;
        private string fallo;
        private string ayudaDelFallo;
        private int lecturas;
        private bool leyendo;
        private bool enMarcha;
        private bool otraPedida;
        private bool otraForzada;
        private bool disposicionAmplia;
        private bool disposicionPuesta;
        private bool disposicionBaja;

        /// <summary>"Ver sus bitacoras...": Bitacoras ya buscando su nombre y su numero (la abre el panel).</summary>
        public event Action<string> VerBitacoras;

        /// <summary>"Ver en Estadisticas": Estadisticas con el y el mismo periodo (la abre el panel).</summary>
        public event Action<int, PeriodoElegido> VerEnEstadisticas;

        public FichaDocente(ProfesorInfo profesor, LecturaEnlaces enlaces)
            : base(NombreDe(profesor), "", new Size(1280, 720))
        {
            if (profesor == null)
            {
                throw new ArgumentNullException("profesor");
            }

            id = profesor.Id;
            this.profesor = profesor;
            this.enlaces = enlaces;
            List<Label> deArriba = Controls.OfType<Label>().ToList();
            lblTitulo = deArriba[0];
            lblTitulo.UseMnemonic = false;
            lblSubtitulo = deArriba[1];
            lblSubtitulo.AutoSize = false;
            lblSubtitulo.AutoEllipsis = true;
            lblSubtitulo.UseMnemonic = false;
            lblSubtitulo.Text = FichaDocenteDatos.Subtitulo(profesor, enlaces);
            Text = "Docente: " + NombreDe(profesor) + (Permisos.SoloConsulta ? " (solo consulta)" : "");

            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(1000, area.Width - 20), Math.Min(560, area.Height - 20));
            // Como Estadisticas: a pantalla completa.
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            periodo = ultimoPeriodo ?? PeriodoElegido.De(TipoPeriodo.Ciclo);

            // ----- Cabecera: su color, el periodo y lo suyo -----
            cuadro = new Panel { Size = new Size(18, 18), Visible = false };
            cuadro.Paint += (s, e) => e.Graphics.DrawRectangle(Pens.Gray, 0, 0, cuadro.Width - 1, cuadro.Height - 1);
            Controls.Add(cuadro);
            PonerColor();

            selector = new SelectorDePeriodo(periodo, ayuda) { TabIndex = 0 };
            selector.LecturaVista = () => lectura;
            selector.ResultadoVisto = () => datos == null ? null : datos.R;
            selector.Cambio += p =>
            {
                periodo = p;
                ultimoPeriodo = p;
                Leer(false);
            };
            Controls.Add(selector);

            btnHorario = EstiloHorarios.CrearBoton("Ver su horario...", Point.Empty, new Size(150, 32));
            btnHorario.TabIndex = 1;
            btnHorario.Click += (s, e) => VerHorario(null);
            ayuda.SetToolTip(btnHorario, "Su horario de la semana en todos los laboratorios, solo para verlo y sin sus códigos de clase.");
            btnBitacoras = EstiloHorarios.CrearBoton("Ver sus bitácoras...", Point.Empty, new Size(170, 32));
            btnBitacoras.TabIndex = 2;
            btnBitacoras.Click += (s, e) => VerSusBitacorasDocente();
            ayuda.SetToolTip(btnBitacoras, "Abre Bitácoras con sus sesiones (busca su nombre y su número de empleado).");
            btnFaltaAsignar = EstiloHorarios.CrearBoton("Falta asignar...", Point.Empty, new Size(230, 32));
            btnFaltaAsignar.TabIndex = 3;
            btnFaltaAsignar.Visible = false;
            btnFaltaAsignar.Click += (s, e) => VerFaltaAsignar();
            ayuda.SetToolTip(btnFaltaAsignar, "Las horas de sus clases que otra clase desplazó o que se quitaron para darlas en otro horario, en el horario en uso. Solo para ver: se asignan en Horarios.");
            Controls.Add(btnHorario);
            Controls.Add(btnBitacoras);
            Controls.Add(btnFaltaAsignar);

            // ----- Las cinco cifras de Estadisticas, con su filtro -----
            string[] etiquetas = { "clases que se dieron", "asistencia de sus estudiantes", "bitácoras registradas", "fallas reportadas", "equipos avisados sin internet" };
            for (int i = 0; i < cifras.Length; i++)
            {
                int cual = i;
                cifras[i] = new CifraGrande { Etiqueta = etiquetas[i], TabIndex = 4 + i };
                cifras[i].Click += (s, e) => CifraPulsada(cual);
                Controls.Add(cifras[i]);
            }

            // ----- Pestanas -----
            tabs = new TabControl
            {
                Font = fuentePestana,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(170, 26),
                TabIndex = 9
            };
            tabs.DrawItem += DibujarPestana;
            tabs.SelectedIndexChanged += (s, e) => tabs.Invalidate();
            Controls.Add(tabs);
            tpResumen = NuevaPagina("Resumen");
            tpClases = NuevaPagina("Sus clases y faltas");
            tpGrupos = NuevaPagina("Sus grupos");
            tpGraficas = NuevaPagina("Gráficas");
            tpFallas = NuevaPagina("Fallas");
            // Solo con db/22: se agrega al saberlo (Poner).
            tpDadas = new TabPage("Asistencias que dio") { BackColor = Color.White, UseVisualStyleBackColor = false };

            // Resumen: "Para revisar" y una grafica.
            lblParaRevisar = Texto(tpResumen, "Para revisar", fuenteTituloFicha, Color.DarkGreen);
            tPendientes = CrearTablaPendientes();
            Agregar(tpResumen, tPendientes.Grid, 1);
            fichaGrafica = new VentanaEstadisticas.Ficha(BordeFicha) { Font = fuenteValor };
            lblClicGraficas = new Label { AutoSize = false, Font = fuenteLinea, ForeColor = TextoGris, Text = "Clic en la gráfica: todas sus gráficas.", UseMnemonic = false };
            lblSinClases = new Label { AutoSize = false, Font = fuenteValor, ForeColor = TextoOscuro, UseMnemonic = false, Visible = false, TextAlign = ContentAlignment.MiddleCenter };
            fichaGrafica.Contenido.Controls.Add(lblClicGraficas);
            fichaGrafica.Contenido.Controls.Add(lblSinClases);
            fichaGrafica.Contenido.Resize += (s, e) => AcomodarGrafica();
            Agregar(tpResumen, fichaGrafica, 2);

            // Sus clases y faltas: el mes de Faltas y sus clases dia por dia.
            lblLineaClases = Linea(tpClases);
            lblMostrar = EstiloHorarios.CrearEtiqueta("Mostrar", Point.Empty);
            tpClases.Controls.Add(lblMostrar);
            for (int i = 0; i < rbMostrar.Length; i++)
            {
                MostrarDias cual = (MostrarDias)i;
                RadioButton opcion = new RadioButton { AutoSize = true, Font = fuenteOpcion, ForeColor = EstiloAdmin.TextoOscuro, BackColor = Color.White, UseMnemonic = false, TabIndex = 1 + i };
                opcion.Checked = i == 0;
                opcion.CheckedChanged += (s, e) =>
                {
                    if (opcion.Checked && mostrar != cual)
                    {
                        mostrar = cual;
                        PonerDias();
                    }
                };
                rbMostrar[i] = opcion;
                tpClases.Controls.Add(opcion);
            }

            PonerTextosMostrar();
            mes = new MesDeBitacoras
            {
                TabIndex = 7,
                TextoSinClases = "sin clases suyas.",
                TextoAlPulsar = "Pulsa para ver solo esa semana en sus clases día por día."
            };
            mes.Semana = new DateTime(1900, 1, 1);
            mes.SemanaElegida += lunes =>
            {
                semana = lunes;
                PonerDias();
            };
            tpClases.Controls.Add(mes);
            leyenda = new LeyendaDelMes();
            tpClases.Controls.Add(leyenda);
            lblSemana = new Label { AutoSize = true, Font = fuenteNegrita, ForeColor = Color.DarkGreen, UseMnemonic = false, Visible = false };
            lnkQuitarSemana = new LinkLabel { AutoSize = true, Font = fuenteLinea, Text = "Quitar", LinkColor = Color.DarkGreen, ActiveLinkColor = Color.DarkGreen, Visible = false, TabIndex = 8 };
            lnkQuitarSemana.LinkClicked += (s, e) => QuitarSemana();
            tpClases.Controls.Add(lblSemana);
            tpClases.Controls.Add(lnkQuitarSemana);
            tDias = CrearTablaDias();
            Agregar(tpClases, tDias.Grid, 9);
            tDias.Abrir += AbrirBitacora;
            // Clic derecho en una clase-dia: su bitacora o esa semana en su horario (solo para ver).
            ContextMenuStrip menuDia = new ContextMenuStrip { Font = fuenteOpcion };
            ToolStripMenuItem verBitacora = new ToolStripMenuItem("Ver su bitácora");
            verBitacora.Click += (s, e) => AbrirBitacora(tDias.Elegido);
            ToolStripMenuItem verSemana = new ToolStripMenuItem("Ver esa semana en su horario");
            verSemana.Click += (s, e) =>
            {
                ClaseDiaDocente dia = tDias.Elegido;
                if (dia != null)
                {
                    VerHorario(dia.Fecha);
                }
            };
            menuDia.Items.Add(verBitacora);
            menuDia.Items.Add(verSemana);
            tDias.Grid.CellMouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.RowIndex < tDias.Grid.Rows.Count)
                {
                    tDias.ElegirFila(x => x == tDias.Grid.Rows[e.RowIndex].Tag);
                    menuDia.Show(Cursor.Position);
                }
            };
            Disposed += (s, e) => menuDia.Dispose();

            // Sus grupos: sus clases y, de la elegida, la lista de sus estudiantes.
            lblLineaGrupos = Linea(tpGrupos);
            tClases = CrearTablaClases();
            Agregar(tpGrupos, tClases.Grid, 1);
            tClases.Elegida += () => MostrarClase(tClases.Elegido);
            fichaClase = new VentanaEstadisticas.Ficha(BordeFicha) { Vacio = "Elige una clase para ver a sus estudiantes.", Font = fuenteValor };
            lblTituloClase = TextoDeFicha(fichaClase, fuenteTituloFicha, Color.DarkGreen);
            lblTituloClase.Text = "Estudiantes de la clase";
            rMateria = new VentanaEstadisticas.Rotulo(fichaClase.Contenido, "MATERIA", fuenteRotulo, fuenteValor);
            rGrupo = new VentanaEstadisticas.Rotulo(fichaClase.Contenido, "GRUPO", fuenteRotulo, fuenteValor);
            rLaboratorio = new VentanaEstadisticas.Rotulo(fichaClase.Contenido, "LABORATORIO", fuenteRotulo, fuenteValor);
            rLista = new VentanaEstadisticas.Rotulo(fichaClase.Contenido, "LISTA", fuenteRotulo, fuenteValor);
            // En dos lineas como mucho, la ultima con "..." (el texto entero, en su ayuda).
            lblDatosClase = TextoDeFicha(fichaClase, fuenteLinea, TextoOscuro);
            tLista = CrearTablaLista();
            fichaClase.Contenido.Controls.Add(tLista.Grid);
            tLista.Abrir += p => VerSusBitacoras(p.AlumnoId);
            btnListaDia = EstiloHorarios.CrearBoton("Ver la lista día por día...", Point.Empty, new Size(220, 34));
            btnListaDia.Click += (s, e) => VerListaDiaPorDia(tClases.Elegido);
            ayuda.SetToolTip(btnListaDia, "La hoja de la clase entera (como la ve su docente en Mis grupos) y su tablero, solo para verla.");
            btnSusBitacoras = EstiloHorarios.CrearBoton("Ver sus bitácoras", Point.Empty, new Size(160, 34));
            btnSusBitacoras.Click += (s, e) =>
            {
                ParDeAsistencia par = tLista.Elegido;
                if (par == null)
                {
                    Avisar("Elige a un estudiante de la lista.");
                    return;
                }

                VerSusBitacoras(par.AlumnoId);
            };
            ayuda.SetToolTip(btnSusBitacoras, "Las bitácoras del estudiante elegido. También con doble clic o Enter en la lista.");
            fichaClase.Contenido.Controls.Add(btnListaDia);
            fichaClase.Contenido.Controls.Add(btnSusBitacoras);
            fichaClase.Contenido.Resize += (s, e) => AcomodarFichaClase();
            Agregar(tpGrupos, fichaClase, 2);

            // Graficas: las cuatro secciones de Estadisticas, ya de el.
            lblLineaGraficas = Linea(tpGraficas);
            lblAgrupar = EstiloHorarios.CrearEtiqueta("Agrupar", Point.Empty);
            rbPorSemana = OpcionDeAgrupacion("Por semana", AgrupacionGraficas.Semana);
            rbPorMes = OpcionDeAgrupacion("Por mes", AgrupacionGraficas.Mes);
            tpGraficas.Controls.Add(lblAgrupar);
            Agregar(tpGraficas, rbPorSemana, 1);
            Agregar(tpGraficas, rbPorMes, 2);
            panelGraficas = new PanelDeGraficas();
            Agregar(tpGraficas, panelGraficas, 3);

            // Fallas: lo que reporto el y los equipos con fallas en sus clases.
            lblLineaFallas = Linea(tpFallas);
            lblTituloReportes = Texto(tpFallas, "Lo que reportó desde su panel", fuenteNegrita, Color.DarkGreen);
            lblSinReportes = Texto(tpFallas, "La base no tiene todavía los reportes de los docentes desde su panel: falta una actualización (db/15).", fuenteLinea, TextoGris);
            lblSinReportes.Visible = false;
            tReportes = CrearTablaReportes();
            Agregar(tpFallas, tReportes.Grid, 1);
            tReportes.Abrir += AbrirBitacoraDelReporte;
            lblTituloEquipos = Texto(tpFallas, "Equipos con fallas en sus clases", fuenteNegrita, Color.DarkGreen);
            tEquipos = CrearTablaEquipos();
            Agregar(tpFallas, tEquipos.Grid, 2);

            // Asistencias que dio.
            lblLineaDadas = Linea(tpDadas);
            tDadas = CrearTablaDadas();
            tDadas.Grid.TabIndex = 1;
            tpDadas.Controls.Add(tDadas.Grid);
            tDadas.Abrir += d => VerSusBitacoras(d.AlumnoId);

            foreach (TabPage pagina in new[] { tpResumen, tpClases, tpGrupos, tpGraficas, tpFallas, tpDadas })
            {
                TabPage esta = pagina;
                esta.Resize += (s, e) => AcomodarPagina(esta);
            }

            // ----- Abajo (de derecha a izquierda) -----
            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            btnActualizar = BotonAbajo("Actualizar", 120);
            btnActualizar.Click += (s, e) => Leer(true);
            ayuda.SetToolTip(btnActualizar, "Vuelve a leer de la base de datos (F5).");
            btnEstadisticas = BotonAbajo("Ver en Estadísticas", 170);
            btnEstadisticas.Click += (s, e) => AbrirEnEstadisticas();
            // A la persona responsable no se le promete el Excel: de Docentes, Clases y
            // Estudiantes no lo tiene (EstadisticasDatos.SeGuardaEnCsv).
            ayuda.SetToolTip(btnEstadisticas, Permisos.SoloConsulta ? "Estadísticas con esta persona como docente y el mismo periodo."
                : "Estadísticas con esta persona como docente y el mismo periodo: ahí se guarda cada pestaña en Excel.");
            // En kiosco no hay archivos (seria una salida al escritorio).
            if (!Kiosco.Activo)
            {
                btnPdf = BotonAbajo("Reporte del docente (PDF)...", 230);
                btnPdf.Click += (s, e) => ReporteDelDocente();
                ayuda.SetToolTip(btnPdf, "Sus cifras, sus clases y faltas, la asistencia de sus grupos, sus fallas y lo que dio en este periodo, con el encabezado del TESCHI. "
                    + "Se ve antes de guardarlo.");
                if (!EstadisticasDatos.ReporteConDocentes(Permisos.SoloConsulta))
                {
                    Permisos.Desactivar(ayuda, btnPdf, EstadisticasDatos.SinCsvParaSoloConsulta);
                    // El motivo tambien a la vista, encima del boton (no solo al pasar el raton).
                    Permisos.MotivoVisible(btnPdf, "Lleva nombres: el PDF del docente te lo da la administración.");
                }
            }

            if (btnPdf != null)
            {
                btnPdf.TabIndex = 10;
            }

            btnEstadisticas.TabIndex = 11;
            btnActualizar.TabIndex = 12;
            cerrar.TabIndex = 13;

            espera.Tick += (s, e) =>
            {
                espera.Stop();
                if (leyendo)
                {
                    lblResumen.Text = TextoCalculando;
                    ayuda.SetToolTip(lblResumen, null);
                }
            };

            foreach (CifraGrande cifra in cifras)
            {
                cifra.Leyendo = true;
            }

            lblResumen.Text = "";
            NotaFija = NotaSinRegistro;
            construida = true;
            Acomodar();
            ActiveControl = tabs;
        }

        private static string NombreDe(ProfesorInfo profesor)
        {
            if (profesor == null)
            {
                return "Docente";
            }

            string nombre = (profesor.NombreCompleto ?? "").Trim();
            return nombre.Length > 0 ? nombre : (profesor.Matricula ?? "Docente");
        }

        // =====================================================================
        // Lo que ven el panel y las pruebas
        // =====================================================================

        /// <summary>usuarios.idusuarios del docente.</summary>
        internal int DocenteId
        {
            get { return id; }
        }

        /// <summary>Lo armado con la ultima lectura que llego (null antes).</summary>
        internal DatosDeLaFicha Datos
        {
            get { return datos; }
        }

        internal PeriodoElegido Periodo
        {
            get { return periodo; }
        }

        /// <summary>La lectura de lo que se ve (null antes de la primera): "Ver en Estadisticas" la pasa a Estadisticas.</summary>
        internal LecturaEstadisticas LecturaVista
        {
            get { return lectura; }
        }

        internal bool Leyendo
        {
            get { return leyendo; }
        }

        // =====================================================================
        // Construccion de las tablas
        // =====================================================================

        private VentanaEstadisticas.Tabla<PendienteDeDocente> CrearTablaPendientes()
        {
            VentanaEstadisticas.Tabla<PendienteDeDocente> t = new VentanaEstadisticas.Tabla<PendienteDeDocente>("Para revisar", fuenteNegrita)
            {
                ClaveDeFila = p => p.Regla + "|" + p.ClaseId.ToString(Inv) + "|" + p.Texto,
                AyudaDeFila = (p, amplia) => VentanaEstadisticas.Unir(p.Ayuda, p.SeAbre ? "Doble clic o Enter: " + QueAbre(p) + "." : null),
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => TextoSinFilas(datos != null && datos.SinClases != null ? datos.SinClases : "Nada que revisar en estas fechas.");
            t.SinFilasEsError = () => !leyendo && error != null;
            t.Agregar(new VentanaEstadisticas.Columna<PendienteDeDocente>
            {
                Nombre = "tono", Titulo = "", Ancho = 10, Texto = p => "", Fondo = p => ColorDeTono(p.Tono)
            });
            t.Agregar(new VentanaEstadisticas.Columna<PendienteDeDocente> { Nombre = "texto", Titulo = "Lo que hay que mirar", Rellena = true, Ancho = 200, Texto = p => p.Texto });
            t.Agregar(new VentanaEstadisticas.Columna<PendienteDeDocente>
            {
                Nombre = "ver", Titulo = "", Ancho = 60, Texto = p => p.SeAbre ? "Ver ›" : "", ColorTexto = p => EstiloHorarios.VerdeTeschi, Negrita = p => p.SeAbre
            });
            // Los textos de "Para revisar" son frases enteras: cada renglon crece con el suyo (dos o tres lineas).
            t.Grid.Columns["texto"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            t.Grid.Columns["texto"].DefaultCellStyle.Padding = new Padding(8, 7, 8, 7);
            t.Grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            t.Abrir += IrA;
            return t;
        }

        private VentanaEstadisticas.Tabla<ClaseDiaDocente> CrearTablaDias()
        {
            VentanaEstadisticas.Tabla<ClaseDiaDocente> t = new VentanaEstadisticas.Tabla<ClaseDiaDocente>("Sus clases día por día", fuenteNegrita)
            {
                ClaveDeFila = d => d.ClaseId.ToString(Inv) + "|" + d.Fecha.ToString("yyyyMMdd", Inv) + "|" + d.Inicio.Ticks.ToString(Inv) + "|" + (d.EsExtra ? "x" : "f") + "|" + d.LaboratorioId.ToString(Inv),
                // En compacta el laboratorio va en la ayuda de la fila: asi no sale la barra horizontal.
                AyudaDeFila = (d, amplia) => amplia ? null : "Laboratorio: " + d.Laboratorio + ".",
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => TextoSinFilas(datos != null && datos.Todas > 0 ? "Ninguna de sus clases con lo que eliges en «Mostrar»." : "No tiene clases del horario en estas fechas.");
            t.SinFilasEsError = () => !leyendo && error != null;
            t.Agregar(new VentanaEstadisticas.Columna<ClaseDiaDocente>
            {
                Nombre = "fecha", Titulo = "Fecha", Ancho = 160, Texto = d => d.FechaTexto, Clave = d => d.Fecha.Ticks + d.Inicio.Ticks, PrimeroDescendente = true
            });
            t.Agregar(new VentanaEstadisticas.Columna<ClaseDiaDocente> { Nombre = "materia", Titulo = "Materia", Rellena = true, Ancho = 140, Texto = d => d.Materia, Clave = d => d.Materia });
            t.Agregar(new VentanaEstadisticas.Columna<ClaseDiaDocente> { Nombre = "grupo", Titulo = "Grupo", Ancho = 80, Texto = d => d.Grupo, Clave = d => d.Grupo });
            t.Agregar(new VentanaEstadisticas.Columna<ClaseDiaDocente> { Nombre = "horario", Titulo = "Horario", Ancho = 110, Texto = d => d.Horario, Clave = d => d.Inicio });
            t.Agregar(new VentanaEstadisticas.Columna<ClaseDiaDocente> { Nombre = "laboratorio", Titulo = "Laboratorio", Rellena = true, Ancho = 110, Peso = 60, SoloAmplia = true, Texto = d => d.Laboratorio, Clave = d => d.Laboratorio });
            t.Agregar(new VentanaEstadisticas.Columna<ClaseDiaDocente>
            {
                Nombre = "sedio", Titulo = "¿Se dio?", Ancho = 160, Holgura = 20, Texto = d => d.SeDioTexto, Clave = d => VentanaEstadisticas.OrdenDeEstado(d),
                Fondo = d => VentanaEstadisticas.FondoDeEstado(d), ColorTexto = d => VentanaEstadisticas.TextoDeEstado(d), Negrita = d => d.SinRegistro,
                Ayuda = d => VentanaEstadisticas.Unir(d.SeDioTexto, d.Ayuda, SesionDelDia(d) != null ? "Doble clic para ver su bitácora." : null),
                AyudaDelTitulo = EstadisticasDatos.AyudaClasesQueSeDieron
            });
            return t;
        }

        private VentanaEstadisticas.Tabla<FilaClase> CrearTablaClases()
        {
            VentanaEstadisticas.Tabla<FilaClase> t = new VentanaEstadisticas.Tabla<FilaClase>("Sus clases", fuenteNegrita)
            {
                ClaveDeFila = c => c.ClaseId.ToString(Inv),
                AlFinal = c => c.SinNada,
                AyudaDeFila = (c, amplia) => VentanaEstadisticas.Unir(
                    "Laboratorio: " + VentanaEstadisticas.Guion(c.Laboratorios) + ".",
                    amplia ? null : "Lista de " + EstadisticasDatos.Contar(c.Estudiantes, "estudiante", "estudiantes") + ".",
                    c.QueContaban == 0 ? null : "Clases con bitácoras: " + EstadisticasDatos.XdeN(c.ConBitacoras, c.QueContaban) + "."
                        + (c.Canceladas > 0 ? " " + EstadisticasDatos.Contar(c.Canceladas, "cancelada", "canceladas") + "." : ""),
                    "Fallas reportadas: " + EstadisticasDatos.Miles(c.Fallas) + "."),
                ElegirLaUnica = true
            };
            t.TextoSinFilas = () => TextoSinFilas(datos != null && datos.SinClases != null ? datos.SinClases : "No tiene clases en estas fechas.");
            t.SinFilasEsError = () => !leyendo && error != null;
            t.Agregar(new VentanaEstadisticas.Columna<FilaClase> { Nombre = "materia", Titulo = "Materia", Rellena = true, Ancho = 120, Texto = c => c.Materia, Clave = c => c.Materia });
            t.Agregar(new VentanaEstadisticas.Columna<FilaClase> { Nombre = "grupo", Titulo = "Grupo", Ancho = 70, Texto = c => c.Grupo, Clave = c => c.Grupo });
            VentanaEstadisticas.AgregarColumnasDeClasesDadas(t, c => c.SeDieron, c => c.QueContaban, c => c.SinRegistro, c => c.ConBitacoras, false);
            t.Agregar(new VentanaEstadisticas.Columna<FilaClase>
            {
                Nombre = "estudiantes", Titulo = "Estudiantes", Ancho = 85, Derecha = true, PrimeroDescendente = true, SoloAmplia = true,
                Texto = c => EstadisticasDatos.Miles(c.Estudiantes), Clave = c => c.Estudiantes,
                AyudaDelTitulo = "El tamaño de su lista: quienes han registrado su bitácora en esta clase (DigLab no tiene la lista oficial del grupo)."
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaClase>
            {
                Nombre = "asistencia", Titulo = "Asistencia del grupo", Ancho = 110,
                Texto = c => EstadisticasDatos.Porcentaje(c.Asistencia), Fraccion = c => c.Asistencia.HasValue ? c.Asistencia.Value / 100.0 : (double?)null,
                Estilo = EstiloBarra.Asistencia, Clave = c => c.Asistencia,
                ColorTexto = c => c.Asistencia.HasValue && !ResumenAsistencia.TieneDerecho(c.Asistencia.Value) ? RojoTexto : (Color?)null,
                AyudaDelTitulo = "Las veces que entraron de las que contaban, sumadas en toda su lista, como «Asistencia del grupo» de Mis grupos."
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaClase>
            {
                Nombre = "bajo", Titulo = "Bajo el 80 %", Ancho = 80, Derecha = true, PrimeroDescendente = true,
                Texto = c => c.Veces == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(c.Bajo), Clave = c => c.Veces == 0 ? null : (IComparable)c.Bajo,
                ColorTexto = c => c.Bajo > 0 ? RojoTexto : (Color?)null,
                AyudaDelTitulo = "Estudiantes de su lista que no llegan al 80 % en el periodo."
            });
            return t;
        }

        private VentanaEstadisticas.Tabla<ParDeAsistencia> CrearTablaLista()
        {
            VentanaEstadisticas.Tabla<ParDeAsistencia> t = new VentanaEstadisticas.Tabla<ParDeAsistencia>("Estudiantes de la clase", fuenteNegrita)
            {
                ClaveDeFila = p => p.AlumnoId.ToString(Inv),
                FilaGris = p => !p.Activo,
                AyudaDeFila = (p, amplia) => VentanaEstadisticas.Unir(amplia ? null : "Matrícula " + p.Matricula + ".", p.AyudaDeEntrada,
                    amplia ? null : DeLasMigraciones(p)),
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => "Nadie ha registrado su bitácora en esta clase.";
            t.Agregar(new VentanaEstadisticas.Columna<ParDeAsistencia>
            {
                Nombre = "numero", Titulo = "No.", Ancho = 42, Derecha = true, Texto = p => NumeroEnLista(p).ToString(Inv), Clave = p => NumeroEnLista(p)
            });
            // Como en Estadisticas: en compacta, la matricula va en la ayuda de la fila (asi cabe el nombre).
            t.Agregar(new VentanaEstadisticas.Columna<ParDeAsistencia> { Nombre = "matricula", Titulo = "Matrícula", Ancho = 100, SoloAmplia = true, Texto = p => p.Matricula, Clave = p => p.Matricula });
            t.Agregar(new VentanaEstadisticas.Columna<ParDeAsistencia>
            {
                Nombre = "estudiante", Titulo = "Estudiante", Rellena = true, Ancho = 105, Texto = p => p.Nombre + (p.Activo ? "" : " (de baja)"), Clave = p => p.Nombre
            });
            t.Agregar(new VentanaEstadisticas.Columna<ParDeAsistencia>
            {
                Nombre = "entro", Titulo = "Entró", Ancho = 85, Texto = p => EstadisticasDatos.XdeN(p.Entro, p.Veces),
                Fraccion = p => BarraEnCelda.Fraccion(p.Entro, p.Veces), Estilo = EstiloBarra.Asistencia, Clave = p => VentanaEstadisticas.Fraccion(p.Entro, p.Veces)
            });
            t.Agregar(new VentanaEstadisticas.Columna<ParDeAsistencia>
            {
                Nombre = "porcentaje", Titulo = "%", Ancho = 50, Derecha = true, Texto = p => EstadisticasDatos.Porcentaje(p.Porcentaje), Clave = p => p.Porcentaje,
                ColorTexto = p => p.Bajo ? RojoTexto : (Color?)null
            });
            t.Agregar(new VentanaEstadisticas.Columna<ParDeAsistencia>
            {
                Nombre = "derecho", Titulo = "Derecho (80 %)", Ancho = 104, Holgura = 24,
                TituloSegun = amplia => datos == null ? "Derecho (80 %)" : datos.R.TituloDerechoCorto,
                Texto = p => p.TextoDerecho(datos != null && datos.R.CicloEntero), Clave = p => p.Veces == 0 ? null : (IComparable)(p.Bajo ? 0 : 1),
                ColorTexto = p => p.Veces == 0 ? (Color?)null : p.Bajo ? RojoTexto : VerdeTexto,
                Negrita = p => p.Bajo,
                Ayuda = p => p.NotaPocasClases,
                AyudaDelTitulo = "Con el ciclo entero, el derecho a evaluación (80 % o más, como en Mis grupos). Con otro periodo, si llega al 80 % en esas fechas: el derecho es de todo el ciclo."
            });
            t.Agregar(new VentanaEstadisticas.Columna<ParDeAsistencia>
            {
                Nombre = "quitadas", Titulo = "Quitadas", Ancho = 75, Derecha = true, SoloAmplia = true, PrimeroDescendente = true,
                Existe = () => lectura != null && lectura.Asistencia.ConRetiro,
                Texto = p => EstadisticasDatos.Miles(p.Retiradas), Clave = p => p.Retiradas, ColorTexto = p => p.Retiradas > 0 ? NaranjaTexto : (Color?)null,
                AyudaDelTitulo = "Asistencias que le quitó su docente en estas fechas: registró sin estar en el salón (cuentan como falta)."
            });
            t.Agregar(new VentanaEstadisticas.Columna<ParDeAsistencia>
            {
                Nombre = "dadas", Titulo = "Dadas", Ancho = 65, Derecha = true, SoloAmplia = true, PrimeroDescendente = true,
                Existe = () => datos != null && datos.R.ConDadas,
                Texto = p => EstadisticasDatos.Miles(p.Dadas), Clave = p => p.Dadas,
                AyudaDelTitulo = "Asistencias que le dio su docente sin bitácora en estas fechas (cuentan como asistencia)."
            });
            return t;
        }

        private string DeLasMigraciones(ParDeAsistencia p)
        {
            List<string> partes = new List<string>();
            if (lectura != null && lectura.Asistencia.ConRetiro && p.Retiradas > 0)
            {
                partes.Add("Le quitaron la asistencia " + EstadisticasDatos.Veces(p.Retiradas) + ".");
            }

            if (datos != null && datos.R.ConDadas && p.Dadas > 0)
            {
                partes.Add("Su docente le dio la asistencia " + EstadisticasDatos.Veces(p.Dadas) + " sin bitácora.");
            }

            return partes.Count == 0 ? null : string.Join(" ", partes);
        }

        private int NumeroEnLista(ParDeAsistencia par)
        {
            int numero;
            return numeroEnLista.TryGetValue(par, out numero) ? numero : 0;
        }

        private VentanaEstadisticas.Tabla<FilaReporteDocente> CrearTablaReportes()
        {
            VentanaEstadisticas.Tabla<FilaReporteDocente> t = new VentanaEstadisticas.Tabla<FilaReporteDocente>("Lo que reportó desde su panel", fuenteNegrita)
            {
                ClaveDeFila = x => x.Id.ToString(Inv),
                AyudaDeFila = (x, amplia) => VentanaEstadisticas.Unir(x.Falla, SesionDeLaSesion(x.SesionId) != null ? "Doble clic para ver la bitácora de esa clase." : null),
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => TextoSinFilas("No reportó fallas desde su panel en estas fechas.");
            t.SinFilasEsError = () => !leyendo && error != null;
            t.Agregar(new VentanaEstadisticas.Columna<FilaReporteDocente>
            {
                Nombre = "cuando", Titulo = "Fecha y hora", Ancho = 132, Holgura = 24, PrimeroDescendente = true,
                Texto = x => EstadisticasDatos.DiaYHora(x.Dia, x.RegistradoEn.HasValue ? x.RegistradoEn.Value.TimeOfDay : (TimeSpan?)null),
                Clave = x => x.Dia.Ticks + (x.RegistradoEn.HasValue ? x.RegistradoEn.Value.TimeOfDay.Ticks : 0)
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaReporteDocente> { Nombre = "laboratorio", Titulo = "Laboratorio", Rellena = true, Ancho = 110, Peso = 60, Texto = x => x.Laboratorio, Clave = x => x.Laboratorio });
            t.Agregar(new VentanaEstadisticas.Columna<FilaReporteDocente> { Nombre = "clase", Titulo = "Clase", Rellena = true, Ancho = 130, Peso = 80, Texto = x => x.Clase, Clave = x => x.Clase });
            t.Agregar(new VentanaEstadisticas.Columna<FilaReporteDocente>
            {
                Nombre = "equipo", Titulo = "Equipo", Ancho = 120, Texto = x => x.Equipo ?? "Todo el laboratorio", Clave = x => x.Equipo ?? "",
                ColorTexto = x => x.Equipo == null ? TextoGris : (Color?)null
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaReporteDocente> { Nombre = "area", Titulo = "Área", Ancho = 85, Texto = x => ReportesProfesorDatos.NombreDeArea(x.Area), Clave = x => x.Area });
            t.Agregar(new VentanaEstadisticas.Columna<FilaReporteDocente> { Nombre = "falla", Titulo = "Falla", Rellena = true, Ancho = 160, Peso = 140, Texto = x => x.Falla, Clave = x => x.Falla });
            return t;
        }

        private VentanaEstadisticas.Tabla<FilaEquipo> CrearTablaEquipos()
        {
            VentanaEstadisticas.Tabla<FilaEquipo> t = new VentanaEstadisticas.Tabla<FilaEquipo>("Equipos con fallas en sus clases", fuenteNegrita)
            {
                ClaveDeFila = e => e.LaboratorioId.ToString(Inv) + "|" + e.Tipo + "|" + (e.Equipo ?? "").ToUpperInvariant(),
                FondoDeFila = e => e.Repetido ? FondoRepetido : (Color?)null,
                AyudaDeFila = (e, amplia) => VentanaEstadisticas.Unir(e.Repetido ? "Con reportes en " + EstadisticasDatos.Miles(e.DiasConReporte) + " días distintos." : null,
                    string.IsNullOrEmpty(e.LoUltimo) ? null : "Lo último: " + e.LoUltimo),
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => TextoSinFilas("Ningún equipo tiene fallas ni avisos en sus clases en estas fechas.");
            t.SinFilasEsError = () => !leyendo && error != null;
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo> { Nombre = "laboratorio", Titulo = "Laboratorio", Rellena = true, Ancho = 130, Peso = 55, Texto = e => e.Laboratorio, Clave = e => e.Laboratorio });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo>
            {
                Nombre = "equipo", Titulo = "Equipo", Ancho = 120, Texto = e => e.Equipo, Clave = e => e.Equipo,
                ColorTexto = e => e.Tipo == TipoFilaEquipo.Equipo ? (Color?)null : TextoGris
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo>
            {
                Nombre = "fallas", Titulo = "Fallas reportadas", Ancho = 85, Derecha = true, PrimeroDescendente = true,
                Texto = e => EstadisticasDatos.Miles(e.Fallas), Clave = e => e.Fallas, ColorTexto = e => e.Fallas > 0 ? NaranjaTexto : (Color?)null, Negrita = e => e.Fallas > 0
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo> { Nombre = "hardware", Titulo = "Hardware", Ancho = 80, Derecha = true, PrimeroDescendente = true, Texto = e => EstadisticasDatos.Miles(e.Hardware), Clave = e => e.Hardware });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo> { Nombre = "software", Titulo = "Software", Ancho = 80, Derecha = true, PrimeroDescendente = true, Texto = e => EstadisticasDatos.Miles(e.Software), Clave = e => e.Software });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo> { Nombre = "red", Titulo = "Red", Ancho = 55, Derecha = true, PrimeroDescendente = true, Texto = e => EstadisticasDatos.Miles(e.Red), Clave = e => e.Red });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo>
            {
                Nombre = "dedocentes", Titulo = "De docentes", Ancho = 80, Derecha = true, PrimeroDescendente = true, SoloAmplia = true,
                Existe = () => datos != null && datos.R.ConReportesDocentes,
                Texto = e => EstadisticasDatos.Miles(e.DeDocentes), Clave = e => e.DeDocentes
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo>
            {
                Nombre = "avisos", Titulo = "Avisos sin internet", Ancho = 90, Derecha = true, PrimeroDescendente = true,
                Existe = () => datos != null && datos.R.ConAvisos,
                Texto = e => EstadisticasDatos.Miles(e.Avisos), Clave = e => e.Avisos, ColorTexto = e => e.Avisos > 0 ? NaranjaTexto : (Color?)null
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo>
            {
                Nombre = "dias", Titulo = "Días con reporte", Ancho = 85, Derecha = true, SoloAmplia = true, PrimeroDescendente = true,
                Texto = e => EstadisticasDatos.Miles(e.DiasConReporte), Clave = e => e.DiasConReporte
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo>
            {
                Nombre = "ultimo", Titulo = "Último reporte", Ancho = 132, Holgura = 24, PrimeroDescendente = true,
                Texto = e => VentanaEstadisticas.Guion(e.UltimoReporte),
                Clave = e => e.UltimoDia.HasValue ? (IComparable)(e.UltimoDia.Value.Ticks + (e.UltimaHora.HasValue ? e.UltimaHora.Value.Ticks + 1 : 0)) : null
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaEquipo>
            {
                Nombre = "loultimo", Titulo = "Lo último que reportaron", Rellena = true, Ancho = 150, Texto = e => VentanaEstadisticas.Guion(e.LoUltimo), Clave = e => VentanaEstadisticas.Vacio(e.LoUltimo)
            });
            return t;
        }

        private VentanaEstadisticas.Tabla<FilaAsistenciaDada> CrearTablaDadas()
        {
            VentanaEstadisticas.Tabla<FilaAsistenciaDada> t = new VentanaEstadisticas.Tabla<FilaAsistenciaDada>("Asistencias que dio", fuenteNegrita)
            {
                ClaveDeFila = x => x.Id.ToString(Inv),
                AyudaDeFila = (x, amplia) => VentanaEstadisticas.Unir(amplia || string.IsNullOrWhiteSpace(x.Comentario) ? null : "Detalle: " + x.Comentario.Trim(),
                    "Doble clic para ver las bitácoras del estudiante (ahí salen también las anuladas)."),
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => TextoSinFilas("No dio asistencias sin bitácora en estas fechas.");
            t.SinFilasEsError = () => !leyendo && error != null;
            // La fecha y el motivo con Holgura 24: las celdas dejan 8 + 8 de margen y con la de siempre
            // (10) se cortaban ("24/10/20...", "Su equipo no tenía inter...") en todos los tamaños.
            t.Agregar(new VentanaEstadisticas.Columna<FilaAsistenciaDada>
            {
                Nombre = "fecha", Titulo = "Fecha", Ancho = 100, Holgura = 24, PrimeroDescendente = true, Texto = x => EstadisticasDatos.Fecha(x.Fecha), Clave = x => x.Fecha.Ticks + x.DadaEn.TimeOfDay.Ticks
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaAsistenciaDada> { Nombre = "clase", Titulo = "Clase", Rellena = true, Ancho = 130, Peso = 80, Texto = x => x.Clase, Clave = x => x.Clase });
            t.Agregar(new VentanaEstadisticas.Columna<FilaAsistenciaDada> { Nombre = "matricula", Titulo = "Matrícula", Ancho = 100, Texto = x => x.Matricula, Clave = x => x.Matricula });
            t.Agregar(new VentanaEstadisticas.Columna<FilaAsistenciaDada> { Nombre = "estudiante", Titulo = "Estudiante", Rellena = true, Ancho = 150, Peso = 100, Texto = x => x.Nombre, Clave = x => x.Nombre });
            t.Agregar(new VentanaEstadisticas.Columna<FilaAsistenciaDada> { Nombre = "motivo", Titulo = "Motivo", Ancho = 150, Holgura = 24, Texto = x => AsistenciasDadas.NombreDeMotivo(x.Motivo), Clave = x => x.Motivo });
            t.Agregar(new VentanaEstadisticas.Columna<FilaAsistenciaDada>
            {
                Nombre = "equipo", Titulo = "Equipo", Ancho = 90, Texto = x => AsistenciasDadas.ConEquipo(x.Motivo) ? VentanaEstadisticas.Guion((x.Equipo ?? "").Trim()) : EstadisticasDatos.SinDato,
                Clave = x => VentanaEstadisticas.Vacio(x.Equipo)
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaAsistenciaDada>
            {
                Nombre = "detalle", Titulo = "Detalle", Rellena = true, Ancho = 120, Peso = 70, SoloAmplia = true,
                Texto = x => VentanaEstadisticas.Guion((x.Comentario ?? "").Trim()), Clave = x => VentanaEstadisticas.Vacio(x.Comentario)
            });
            t.Agregar(new VentanaEstadisticas.Columna<FilaAsistenciaDada>
            {
                Nombre = "hora", Titulo = "La dio a las", Ancho = 104, Texto = x => x.DadaEn.ToString("HH:mm", Inv), Clave = x => x.DadaEn.TimeOfDay
            });
            return t;
        }

        /// <summary>Sin filas: leyendo, el error o lo que dice esa tabla vacia.</summary>
        private string TextoSinFilas(string vacio)
        {
            if (datos == null)
            {
                return error ?? TextoLeyendo;
            }

            return vacio;
        }

        // =====================================================================
        // Piezas
        // =====================================================================

        private TabPage NuevaPagina(string titulo)
        {
            TabPage pagina = new TabPage(titulo) { BackColor = Color.White, UseVisualStyleBackColor = false };
            tabs.TabPages.Add(pagina);
            return pagina;
        }

        private static void Agregar(TabPage pagina, Control control, int orden)
        {
            control.TabIndex = orden;
            pagina.Controls.Add(control);
        }

        private Label Linea(TabPage pagina)
        {
            Label linea = new Label { AutoSize = false, AutoEllipsis = true, Font = fuenteLinea, ForeColor = TextoGris, UseMnemonic = false };
            pagina.Controls.Add(linea);
            return linea;
        }

        private static Label Texto(TabPage pagina, string texto, DrawingFont fuente, Color color)
        {
            Label etiqueta = new Label { AutoSize = false, AutoEllipsis = true, Font = fuente, ForeColor = color, Text = texto, UseMnemonic = false };
            pagina.Controls.Add(etiqueta);
            return etiqueta;
        }

        private static Label TextoDeFicha(VentanaEstadisticas.Ficha ficha, DrawingFont fuente, Color color)
        {
            Label etiqueta = new Label { AutoSize = false, AutoEllipsis = true, Font = fuente, ForeColor = color, UseMnemonic = false };
            ficha.Contenido.Controls.Add(etiqueta);
            return etiqueta;
        }

        private RadioButton OpcionDeAgrupacion(string texto, AgrupacionGraficas cual)
        {
            RadioButton opcion = new RadioButton { AutoSize = true, Text = texto, Font = fuenteValor, ForeColor = EstiloAdmin.TextoOscuro, BackColor = Color.White, UseMnemonic = false };
            opcion.CheckedChanged += (s, e) =>
            {
                if (opcion.Checked && !poniendoAgrupacion)
                {
                    agrupacionElegida = cual;
                    PonerGraficas();
                }
            };
            return opcion;
        }

        private static Color ColorDeTono(TonoPendiente tono)
        {
            switch (tono)
            {
                case TonoPendiente.Rojo:
                    return PestanaCalendario.RojoFalta;
                case TonoPendiente.Ambar:
                    return Color.FromArgb(240, 180, 40);
                case TonoPendiente.Naranja:
                    return Color.FromArgb(230, 126, 34);
                case TonoPendiente.Verde:
                    return Color.FromArgb(76, 160, 80);
                default:
                    return Color.FromArgb(175, 175, 175);
            }
        }

        private static Color ColorDeCifra(TonoCifra tono)
        {
            switch (tono)
            {
                case TonoCifra.Verde:
                    return CifraGrande.ColorVerde;
                case TonoCifra.Rojo:
                    return CifraGrande.ColorRojo;
                case TonoCifra.Naranja:
                    return CifraGrande.ColorNaranja;
                default:
                    return CifraGrande.ColorGris;
            }
        }

        private void PonerColor()
        {
            conColor = ColoresProfesor.TieneColor(id);
            cuadro.Visible = conColor;
            if (conColor)
            {
                cuadro.BackColor = ColoresProfesor.De(id);
            }
        }

        /// <summary>Una pestana con la letra y el verde de DigLab, como Estadisticas; si no cabe su titulo, con "...".</summary>
        private void DibujarPestana(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= tabs.TabPages.Count)
            {
                return;
            }

            bool elegida = e.Index == tabs.SelectedIndex;
            using (SolidBrush fondo = new SolidBrush(elegida ? Color.DarkGreen : Color.FromArgb(232, 244, 232)))
            {
                e.Graphics.FillRectangle(fondo, e.Bounds);
            }

            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, fuentePestana, e.Bounds, elegida ? Color.White : Color.DarkGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (elegida && (e.State & DrawItemState.Focus) != 0 && tabs.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -4, -4), Color.White, Color.DarkGreen);
            }
        }

        // =====================================================================
        // Disposicion
        // =====================================================================

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Acomodar();
        }

        /// <summary>Como Estadisticas: amplia desde 1600 x 860 de cliente, compacta por debajo y baja (sin subtitulo, cifras de 78) con menos de 640 de alto.</summary>
        private void Acomodar()
        {
            if (!construida || WindowState == FormWindowState.Minimized || ClientSize.Width < 300 || ClientSize.Height < 300)
            {
                return;
            }

            Size cliente = ClientSize;
            bool amplia = cliente.Width >= 1600 && cliente.Height >= 860;
            bool baja = cliente.Height < AltoMinimoNormal;
            SuspendLayout();
            int xTitulo = 28;
            if (conColor)
            {
                cuadro.SetBounds(30, 23, 18, 18);
                xTitulo = 52;
            }

            lblTitulo.Location = new Point(xTitulo, 14);
            lblSubtitulo.Visible = !baja;
            lblSubtitulo.SetBounds(30, 52, Math.Max(100, cliente.Width - 2 * Margen), 20);
            ayuda.SetToolTip(lblSubtitulo, lblSubtitulo.Text);

            // La fila del periodo: el periodo a la izquierda y los botones de lo suyo a la derecha.
            int yFila = baja ? 50 : 82;
            int derecha = cliente.Width - Margen;
            foreach (Button boton in new[] { btnFaltaAsignar, btnBitacoras, btnHorario })
            {
                if (boton == btnFaltaAsignar && !conFaltaAsignar)
                {
                    continue;
                }

                boton.Location = new Point(derecha - boton.Width, yFila - 3);
                derecha = boton.Left - 10;
            }

            int anchoCombo = Math.Max(220, Math.Min(amplia ? 420 : 360, derecha - 20 - Margen - selector.AnchoEtiqueta));
            selector.SetBounds(Margen, yFila, selector.AnchoEtiqueta + anchoCombo, selector.Combo.Height);

            int yCifras = yFila + (baja ? 34 : 40);
            int anchoCifras = cliente.Width - 2 * Margen;
            int cada = (anchoCifras - 4 * Separacion) / 5;
            int altoCifras = baja ? AltoCifrasBaja : CifraGrande.Alto;
            for (int i = 0; i < cifras.Length; i++)
            {
                int x = Margen + i * (cada + Separacion);
                cifras[i].SetBounds(x, yCifras, i == cifras.Length - 1 ? Margen + anchoCifras - x : cada, altoCifras);
            }

            int yTabs = yCifras + altoCifras + (baja ? 8 : 12);
            // En solo consulta, 10 px mas abajo: la linea del motivo del PDF apagado
            // (Permisos.MotivoVisible) va entre las pestanas y los botones.
            tabs.SetBounds(Margen, yTabs, cliente.Width - 2 * Margen, Math.Max(150, cliente.Height - (Permisos.SoloConsulta ? 82 : 72) - yTabs));
            int anchoPestana = Math.Max(110, Math.Min(190, (tabs.Width - 8) / Math.Max(1, tabs.TabPages.Count)));
            if (tabs.ItemSize.Width != anchoPestana)
            {
                tabs.ItemSize = new Size(anchoPestana, 26);
            }

            disposicionBaja = baja;
            if (amplia != disposicionAmplia || !disposicionPuesta)
            {
                disposicionAmplia = amplia;
                disposicionPuesta = true;
                foreach (VentanaEstadisticas.ITablaDisposicion tabla in Tablas())
                {
                    tabla.Disposicion(amplia);
                }
            }

            ResumeLayout(true);
            foreach (TabPage pagina in new[] { tpResumen, tpClases, tpGrupos, tpGraficas, tpFallas, tpDadas })
            {
                AcomodarPagina(pagina);
            }

            PonerLineaDeAbajo();
        }

        private IEnumerable<VentanaEstadisticas.ITablaDisposicion> Tablas()
        {
            return new VentanaEstadisticas.ITablaDisposicion[] { tPendientes, tDias, tClases, tLista, tReportes, tEquipos, tDadas };
        }

        private void AcomodarPagina(TabPage pagina)
        {
            if (!construida)
            {
                return;
            }

            Size tam = pagina.ClientSize;
            if (tam.Width < 100 || tam.Height < 80)
            {
                // Una pestana que aun no se ve mide 0: se acomoda con el tamano de las que se ven.
                tam = tabs.DisplayRectangle.Size;
                if (tam.Width < 100 || tam.Height < 80)
                {
                    return;
                }
            }

            int ancho = tam.Width - 16;
            int y0 = disposicionBaja ? 4 : 8;
            if (pagina == tpResumen)
            {
                int izquierda = (int)((ancho - Separacion) * (disposicionAmplia ? 0.55 : 0.52));
                lblParaRevisar.SetBounds(8, y0, izquierda, 22);
                tPendientes.Grid.SetBounds(8, y0 + 26, izquierda, Math.Max(60, tam.Height - y0 - 26 - 8));
                fichaGrafica.SetBounds(8 + izquierda + Separacion, y0, ancho - izquierda - Separacion, Math.Max(80, tam.Height - y0 - 8));
                AcomodarGrafica();
            }
            else if (pagina == tpClases)
            {
                int altoLinea = AltoDeTexto(lblLineaClases.Text, lblLineaClases.Font, ancho, disposicionBaja ? 18 : 36);
                lblLineaClases.SetBounds(8, y0, ancho, Math.Max(18, altoLinea));
                int y = y0 + Math.Max(18, altoLinea) + 4;
                Size etiqueta = lblMostrar.PreferredSize;
                lblMostrar.Location = new Point(8, y + (24 - etiqueta.Height) / 2);
                int x = 8 + etiqueta.Width + 8;
                foreach (RadioButton opcion in rbMostrar)
                {
                    opcion.Location = new Point(x, y + (24 - opcion.PreferredSize.Height) / 2);
                    x += opcion.PreferredSize.Width + 14;
                }

                y += 30;
                int alto = tam.Height - y - 8;
                bool conMes = !disposicionBaja;
                mes.Visible = conMes;
                leyenda.Visible = conMes;
                int xTabla = 8;
                if (conMes)
                {
                    // La leyenda, del alto que pide (con db/22, un renglon mas para el ambar).
                    int altoMes = Math.Min(320, Math.Max(200, alto - 6 - leyenda.AltoNecesario));
                    mes.SetBounds(8, y, 340, altoMes);
                    leyenda.SetBounds(8, y + altoMes + 6, 340, leyenda.AltoNecesario);
                    xTabla = 8 + 340 + Separacion;
                }

                int yTabla = y;
                // Del estado y no de Visible: con la pestana oculta, Visible siempre dice false.
                if (semana.HasValue)
                {
                    lblSemana.Location = new Point(xTabla, y + 2);
                    lnkQuitarSemana.Location = new Point(lblSemana.Right + 10, y + 4);
                    yTabla = y + 26;
                }

                tDias.Grid.SetBounds(xTabla, yTabla, Math.Max(100, 8 + ancho - xTabla), Math.Max(60, tam.Height - yTabla - 8));
            }
            else if (pagina == tpGrupos)
            {
                int altoLinea = AltoDeTexto(lblLineaGrupos.Text, lblLineaGrupos.Font, ancho, disposicionBaja ? 18 : 36);
                lblLineaGrupos.SetBounds(8, y0, ancho, Math.Max(18, altoLinea));
                int y = y0 + Math.Max(18, altoLinea) + 6;
                // Lado a lado, mitad y mitad, tambien en la baja: una encima de otra, a 1000x560
                // (1366x768 al 125 %) la lista de estudiantes no ensenaba ni una fila.
                int alto = tam.Height - y - 8;
                int izquierda = (ancho - Separacion) / 2;
                tClases.Grid.SetBounds(8, y, izquierda, alto);
                fichaClase.SetBounds(8 + izquierda + Separacion, y, ancho - izquierda - Separacion, alto);

                AcomodarFichaClase();
            }
            else if (pagina == tpGraficas)
            {
                int yOpcion = y0 + (26 - rbPorMes.PreferredSize.Height) / 2;
                rbPorMes.Location = new Point(8 + ancho - rbPorMes.PreferredSize.Width, yOpcion);
                rbPorSemana.Location = new Point(rbPorMes.Left - 10 - rbPorSemana.PreferredSize.Width, yOpcion);
                lblAgrupar.Location = new Point(rbPorSemana.Left - 6 - lblAgrupar.PreferredSize.Width, y0 + (26 - lblAgrupar.PreferredSize.Height) / 2);
                lblLineaGraficas.SetBounds(8, y0 + 3, Math.Max(50, lblAgrupar.Left - 24), 20);
                int y = y0 + 26 + 4;
                panelGraficas.SetBounds(2, y, tam.Width - 4, Math.Max(80, tam.Height - y - 2));
            }
            else if (pagina == tpFallas)
            {
                int altoLinea = AltoDeTexto(lblLineaFallas.Text, lblLineaFallas.Font, ancho, disposicionBaja ? 18 : 36);
                lblLineaFallas.SetBounds(8, y0, ancho, Math.Max(18, altoLinea));
                int y = y0 + Math.Max(18, altoLinea) + 6;
                int alto = tam.Height - y - 8;
                lblTituloReportes.SetBounds(8, y, ancho, 20);
                y += 22;
                int altoReportes;
                // Sin db/15 o sin nada que reporto: una linea en vez de la tabla vacia (a 1000x560
                // la tabla vacia dejaba sin ninguna fila a la de los equipos).
                bool sinReportes = datos != null && (!datos.R.ConReportesDocentes || datos.SusReportes.Count == 0);
                lblSinReportes.Visible = sinReportes;
                if (sinReportes)
                {
                    lblSinReportes.SetBounds(8, y, ancho, 20);
                    altoReportes = 22;
                    tReportes.Grid.Visible = false;
                }
                else
                {
                    tReportes.Grid.Visible = true;
                    altoReportes = Math.Max(disposicionBaja ? 70 : 90, Math.Min((int)((alto - 50) * 0.4), tReportes.AltoParaFilas(tReportes.Grid.Rows.Count)));
                    tReportes.Grid.SetBounds(8, y, ancho, altoReportes);
                }

                y += altoReportes + 8;
                lblTituloEquipos.SetBounds(8, y, ancho, 20);
                y += 22;
                tEquipos.Grid.SetBounds(8, y, ancho, Math.Max(60, tam.Height - y - 8));
            }
            else if (pagina == tpDadas)
            {
                lblLineaDadas.SetBounds(8, y0, ancho, 20);
                int y = y0 + 26;
                tDadas.Grid.SetBounds(8, y, ancho, Math.Max(60, tam.Height - y - 8));
            }
        }

        private void AcomodarGrafica()
        {
            Size tam = fichaGrafica.Contenido.ClientSize;
            if (tam.Width < 50 || tam.Height < 50)
            {
                return;
            }

            lblSinClases.SetBounds(16, 0, tam.Width - 32, tam.Height);
            lblClicGraficas.SetBounds(12, tam.Height - 24, tam.Width - 24, 20);
            if (lienzo != null && grafica != null)
            {
                // Las columnas, con el alto que queda (como mucho 260): sin esto, a 1000x560 se cortaban por abajo.
                int ancho = tam.Width - 24;
                float disponible = tam.Height - 10 - 30;
                grafica.AltoDibujo = 170F;
                float encima = lienzo.AltoPara(ancho) - 170F;
                grafica.AltoDibujo = Math.Max(60F, Math.Min(260F, disponible - encima));
                lienzo.SetBounds(12, 10, ancho, Math.Max(60, lienzo.AltoPara(ancho)));
                lienzo.Invalidate();
            }
        }

        private void AcomodarFichaClase()
        {
            Size tam = fichaClase.Contenido.ClientSize;
            if (tam.Width < 50 || tam.Height < 50)
            {
                return;
            }

            int x = 12;
            int ancho = tam.Width - 24;
            int y = 8;
            bool corta = tam.Height < 330;
            // Con poco ancho, los botones con su texto corto (el largo, en su ayuda).
            bool angosta = ancho < 560;
            btnListaDia.Text = angosta ? "Día por día..." : "Ver la lista día por día...";
            btnListaDia.Width = angosta ? 130 : 220;
            btnSusBitacoras.Text = angosta ? "Sus bitácoras" : "Ver sus bitácoras";
            btnSusBitacoras.Width = angosta ? 130 : 160;
            // Los botones abajo; con poco alto, arriba, y en vez del titulo, los datos de la clase a su izquierda.
            int yBotones = corta ? 6 : tam.Height - 8 - btnListaDia.Height;
            btnSusBitacoras.Location = new Point(tam.Width - 12 - btnSusBitacoras.Width, yBotones);
            btnListaDia.Location = new Point(btnSusBitacoras.Left - 8 - btnListaDia.Width, yBotones);
            lblTituloClase.Visible = !corta;
            lblTituloClase.SetBounds(x, y, ancho, 22);
            foreach (VentanaEstadisticas.Rotulo r in new[] { rMateria, rGrupo, rLaboratorio, rLista })
            {
                r.Visible = !corta;
            }

            lblDatosClase.Visible = corta;
            if (corta)
            {
                int anchoDatos = Math.Max(60, btnListaDia.Left - 8 - x);
                // El renglon como lo dibuja la etiqueta (GDI): con Century Gothic, Font.Height se queda corto.
                int renglon = TextRenderer.MeasureText("Ág", lblDatosClase.Font).Height;
                int altoDatos = Math.Max(18, AltoDeTexto(lblDatosClase.Text, lblDatosClase.Font, anchoDatos, 2 * renglon + 2));
                lblDatosClase.SetBounds(x, 6, anchoDatos, altoDatos);
                y = Math.Max(6 + altoDatos, yBotones + btnListaDia.Height) + 6;
            }
            else
            {
                y += 28;
                int mitad = (ancho - 12) / 2;
                rMateria.Poner(x, y, mitad);
                rGrupo.Poner(x + mitad + 12, y, ancho - mitad - 12);
                y += 38;
                rLaboratorio.Poner(x, y, mitad);
                rLista.Poner(x + mitad + 12, y, ancho - mitad - 12);
                y += 40;
            }

            int abajo = corta ? tam.Height - 6 : yBotones - 6;
            tLista.Grid.SetBounds(x, y, ancho, Math.Max(52, abajo - y));
            // El tabulador en el orden en que se leen: con poco alto los botones van arriba de la lista.
            // (Numeros altos: las etiquetas de la ficha tienen los primeros y no se eligen.)
            tLista.Grid.TabIndex = corta ? 102 : 100;
            btnListaDia.TabIndex = corta ? 100 : 101;
            btnSusBitacoras.TabIndex = corta ? 101 : 102;
        }

        private static int AltoDeTexto(string texto, DrawingFont fuente, int ancho, int maximo)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return 0;
            }

            int alto = TextRenderer.MeasureText(texto, fuente, new Size(Math.Max(20, ancho), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            return Math.Min(alto + 2, maximo);
        }

        // =====================================================================
        // Lectura (como Estadisticas: en segundo plano, una a la vez, la que llega tarde se descarta)
        // =====================================================================

        protected override void Recargar(string seleccionar)
        {
            Leer(false);
        }

        /// <summary>Lo que devuelve la lectura en segundo plano.</summary>
        internal sealed class Leido
        {
            public LecturaEstadisticas Lectura;
            public DatosDeLaFicha Datos;
            public ProfesorInfo Profesor;
            public LecturaEnlaces Enlaces;
        }

        /// <summary>
        /// Lee (casi siempre de la lectura guardada de Estadisticas) en segundo plano y arma la
        /// ficha. forzar = Actualizar (F5): olvida la lectura guardada y vuelve a leer quien es
        /// (ProfesoresDatos.Listar) y su enlace con el SIIA. Mientras lee se sigue viendo lo de antes.
        /// </summary>
        private void Leer(bool forzar)
        {
            ++lecturas;
            leyendo = true;
            btnActualizar.Enabled = false;
            espera.Stop();
            espera.Start();
            foreach (CifraGrande cifra in cifras)
            {
                cifra.Leyendo = true;
            }

            if (enMarcha)
            {
                otraPedida = true;
                otraForzada |= forzar;
                return;
            }

            Lanzar(forzar);
        }

        private void Lanzar(bool forzar)
        {
            int numero = lecturas;
            enMarcha = true;
            PeriodoElegido pedido = periodo;
            ProfesorInfo quien = profesor;
            LecturaEnlaces suEnlace = enlaces;
            int docente = id;
            Task.Run(() => LeerYArmar(pedido, forzar, docente, quien, suEnlace)).ContinueWith(t => EnLaVentana(() =>
            {
                enMarcha = false;
                if (otraPedida)
                {
                    bool forzada = otraForzada;
                    otraPedida = false;
                    otraForzada = false;
                    Lanzar(forzada);
                    return;
                }

                Recibir(numero, pedido, t);
            }, t));
        }

        /// <summary>Lo de la ficha, sin tocar la pantalla (en segundo plano; tambien lo usan las pruebas).</summary>
        internal static Leido LeerYArmar(PeriodoElegido pedido, bool forzar, int docente, ProfesorInfo profesor, LecturaEnlaces enlaces)
        {
            if (forzar || profesor == null)
            {
                try
                {
                    profesor = new ProfesoresDatos().Listar().FirstOrDefault(p => p.Id == docente) ?? profesor;
                }
                catch (Exception ex)
                {
                    Log.Error("Ficha del docente " + docente + ": leer quien es", ex);
                }
            }

            if (forzar || enlaces == null)
            {
                try
                {
                    enlaces = EnlacesSiia.Leer();
                }
                catch (Exception ex)
                {
                    Log.Error("Ficha del docente " + docente + ": leer su enlace con el SIIA", ex);
                    enlaces = enlaces ?? LecturaEnlaces.Ninguna;
                }
            }

            LecturaEstadisticas l = EstadisticasDatos.Leer(pedido, forzar);
            FiltroEstadisticas filtro = EstadisticasDatos.Resolver(pedido, l, 0, docente, 0, true);
            ResultadoEstadisticas r = EstadisticasDatos.Calcular(l, filtro);
            List<HoraPorAsignar> porAsignar = null;
            if (r.Hasta == l.Hoy)
            {
                try
                {
                    porAsignar = new HorariosDatos().LeerHorasPorAsignar();
                }
                catch (Exception ex)
                {
                    Log.Aviso("Ficha del docente " + docente + ": no se pudo leer lo que falta asignar (" + ex.GetType().Name + ").");
                }
            }

            return new Leido
            {
                Lectura = l,
                Datos = FichaDocenteDatos.Armar(l, r, docente, profesor, enlaces, porAsignar),
                Profesor = profesor,
                Enlaces = enlaces
            };
        }

        private void Recibir(int numero, PeriodoElegido pedido, Task<Leido> tarea)
        {
            if (numero != lecturas || IsDisposed)
            {
                return;
            }

            leyendo = false;
            espera.Stop();
            btnActualizar.Enabled = true;
            if (tarea.IsFaulted || tarea.IsCanceled)
            {
                Exception ex = tarea.Exception == null ? new OperationCanceledException() : tarea.Exception.GetBaseException();
                Log.Error("Ficha del docente " + id + ": leer", ex);
                if (datos != null && lectura != null)
                {
                    // Se sigue viendo lo leido, con su periodo.
                    periodo = periodoVisto ?? periodo;
                    ultimoPeriodo = periodo;
                    selector.Volver(periodoVisto, lectura, datos.R);
                    DateTime ahora = DateTime.Now + VentanaEstadisticas.Desfase(lectura);
                    fallo = "No se pudo actualizar a las " + ahora.ToString("HH:mm", Inv) + " (" + AvisoDeFalloAdmin.Causa(ex) + "): se ve lo leído a las "
                        + lectura.AhoraServidor.ToString("HH:mm", Inv) + ".";
                    ayudaDelFallo = AvisoDeFalloAdmin.Texto("No se pudo leer la ficha del docente", ex);
                    Poner();
                    return;
                }

                error = AvisoDeFalloAdmin.Texto("No se pudo leer la ficha del docente", ex);
                foreach (CifraGrande cifra in cifras)
                {
                    cifra.Mostrar(CifraGrande.SinDato, CifraGrande.ColorGris, "no se pudo leer", null, error);
                }

                foreach (VentanaEstadisticas.ITablaDisposicion tabla in Tablas())
                {
                    tabla.Vaciar();
                }

                panelGraficas.Avisar(error, true);
                lblResumen.Text = "No se pudo leer la ficha del docente.";
                ayuda.SetToolTip(lblResumen, error);
                return;
            }

            Leido leido = tarea.Result;
            lectura = leido.Lectura;
            datos = leido.Datos;
            profesor = leido.Profesor ?? profesor;
            enlaces = leido.Enlaces ?? enlaces;
            periodoVisto = pedido;
            error = null;
            fallo = null;
            ayudaDelFallo = null;
            selector.Poner(lectura, datos.R);
            periodo = selector.Periodo;
            ultimoPeriodo = periodo;
            Poner();
        }

        /// <summary>Lo leido en la pantalla: la cabecera, las cifras, las seis pestanas y las lineas de abajo.</summary>
        private void Poner()
        {
            DatosDeLaFicha d = datos;
            ResultadoEstadisticas r = d.R;
            string nombre = NombreDe(profesor);
            lblTitulo.Text = nombre;
            Text = "Docente: " + nombre + (Permisos.SoloConsulta ? " (solo consulta)" : "");
            lblSubtitulo.Text = FichaDocenteDatos.Subtitulo(profesor, enlaces);
            PonerColor();

            CifrasEstadisticas c = r.Cifras;
            CifraEstadistica[] cinco = { c.ClasesQueSeDieronCifra, c.Asistencia, c.Bitacoras, c.Fallas, c.SinInternet };
            string[] etiquetas = { "clases que se dieron", "asistencia de sus estudiantes", "bitácoras registradas", "fallas reportadas", "equipos avisados sin internet" };
            // La de las bitacoras, la suya: la de Estadisticas cuenta el uso libre, que con un docente nunca entra.
            string[] ayudas = { cinco[0].Ayuda, cinco[1].Ayuda, FichaDocenteDatos.AyudaBitacoras(r), cinco[3].Ayuda, cinco[4].Ayuda };
            // Arriba, Estadisticas ensena siempre las de todo el TESCHI (no cambian con los filtros de sus apartados).
            string deEl = FichaDocenteDatos.ComoEnEstadisticas(nombre);
            for (int i = 0; i < cifras.Length; i++)
            {
                cifras[i].Etiqueta = etiquetas[i];
                cifras[i].Mostrar(cinco[i].Valor, ColorDeCifra(cinco[i].Tono), cinco[i].Detalle, cinco[i].ParteRoja,
                    string.IsNullOrEmpty(ayudas[i]) ? deEl : ayudas[i] + Environment.NewLine + deEl);
            }

            // "Falta asignar": solo si el periodo llega a hoy y tiene horas pendientes (db/25).
            bool conFalta = d.PorAsignar != null && d.PorAsignar.Count > 0;
            conFaltaAsignar = conFalta;
            btnFaltaAsignar.Visible = conFalta;
            if (conFalta)
            {
                btnFaltaAsignar.Text = "Falta asignar: " + HorariosDatos.Duracion(d.MinutosPorAsignar) + "...";
                btnFaltaAsignar.Width = Math.Max(150, TextRenderer.MeasureText(btnFaltaAsignar.Text, btnFaltaAsignar.Font).Width + 30);
            }

            // Asistencias que dio, solo con db/22.
            bool conDadas = r.ConDadas;
            if (conDadas && !tabs.TabPages.Contains(tpDadas))
            {
                tabs.TabPages.Add(tpDadas);
            }
            else if (!conDadas && tabs.TabPages.Contains(tpDadas))
            {
                tabs.TabPages.Remove(tpDadas);
            }

            foreach (VentanaEstadisticas.ITablaDisposicion tabla in Tablas())
            {
                tabla.Columnas(disposicionAmplia);
            }

            PonerResumen();
            PonerTextosMostrar();
            mes.Hoy = lectura.Hoy;
            mes.Mes = r.Hasta;
            mes.Dias = d.Mes;
            leyenda.ConDadas = r.ConDadas;
            semana = null;
            mes.Semana = new DateTime(1900, 1, 1);
            lblLineaClases.Text = d.LineaClases;
            ayuda.SetToolTip(lblLineaClases, d.LineaClases);
            PonerDias();
            lblLineaGrupos.Text = d.LineaGrupos;
            ayuda.SetToolTip(lblLineaGrupos, d.LineaGrupos);
            tClases.Poner(r.Clases);
            MostrarClase(tClases.Elegido);
            PonerGraficas();
            lblLineaFallas.Text = d.LineaFallas;
            ayuda.SetToolTip(lblLineaFallas, d.LineaFallas);
            lblSinReportes.Text = r.ConReportesDocentes ? "No reportó fallas desde su panel en estas fechas."
                : "La base no tiene todavía los reportes de los docentes desde su panel: falta una actualización (db/15).";
            tReportes.Poner(d.SusReportes);
            tEquipos.Poner(r.Equipos);
            lblLineaDadas.Text = d.LineaDadas;
            ayuda.SetToolTip(lblLineaDadas, d.LineaDadas);
            tDadas.Poner(d.SusDadas);

            NotaFija = NotaSinRegistro;
            Acomodar();
            PonerLineaDeAbajo();
        }

        /// <summary>La linea de abajo: el periodo y la hora de la lectura (la corta si la larga no cabe), o que no se pudo actualizar.</summary>
        private void PonerLineaDeAbajo()
        {
            if (datos == null || leyendo)
            {
                return;
            }

            string larga = fallo ?? datos.LineaPeriodo;
            // La larga, la corta o (a 1000 de ancho, junto al PDF) solo los dias; la larga va en la ayuda.
            string texto = larga;
            if (fallo == null)
            {
                foreach (string otra in new[] { datos.LineaPeriodo, datos.LineaPeriodoCorta, datos.LineaPeriodoMinima })
                {
                    texto = otra;
                    if (TextRenderer.MeasureText(otra, lblResumen.Font).Width <= lblResumen.Width - 4)
                    {
                        break;
                    }
                }
            }

            lblResumen.Text = texto;
            ayuda.SetToolTip(lblResumen, fallo == null ? datos.LineaPeriodo : ayudaDelFallo);
        }

        private void PonerResumen()
        {
            DatosDeLaFicha d = datos;
            tPendientes.Poner(d.Pendientes);
            if (lienzo != null)
            {
                fichaGrafica.Contenido.Controls.Remove(lienzo);
                lienzo.Dispose();
                lienzo = null;
            }

            if (d.SinClases != null)
            {
                lblSinClases.Text = d.SinClases;
                lblSinClases.Visible = true;
                lblClicGraficas.Visible = false;
            }
            else
            {
                lblSinClases.Visible = false;
                lblClicGraficas.Visible = true;
                AgrupacionGraficas agrupacion = d.R.Graficas.Sugerida;
                grafica = ContenidoGraficas.ColumnasDeClases(d.R.Graficas.Tramos(agrupacion), ContenidoGraficas.PorTramo(agrupacion), d.R.Cifras.ClasesConDadas > 0);
                grafica.Titulo = "Sus clases " + ContenidoGraficas.PorTramo(agrupacion);
                lienzo = new LienzoGrafica(grafica, estiloGrafica) { Cursor = Cursors.Hand };
                lienzo.Click += (s, e) => tabs.SelectedTab = tpGraficas;
                ayuda.SetToolTip(lienzo, "Clic: todas sus gráficas.");
                fichaGrafica.Contenido.Controls.Add(lienzo);
            }

            fichaGrafica.ConDatos = true;
            AcomodarGrafica();
        }

        private void PonerTextosMostrar()
        {
            DatosDeLaFicha d = datos;
            string[] textos = { "Todas", "Sin registro", "Canceladas", "Se dieron", "Aún no pasan o no cuentan" };
            int[] cuentas = d == null ? new int[5] : new[] { d.Todas, d.SinRegistro, d.Canceladas, d.SeDieron, d.AunNoONoCuentan };
            for (int i = 0; i < rbMostrar.Length; i++)
            {
                rbMostrar[i].Text = textos[i] + (d == null ? "" : " " + EstadisticasDatos.Miles(cuentas[i]));
            }
        }

        /// <summary>Sus clases dia por dia con lo que pide "Mostrar" y, si se eligio, solo esa semana.</summary>
        private void PonerDias()
        {
            if (datos == null)
            {
                tDias.Vaciar();
                return;
            }

            IEnumerable<ClaseDiaDocente> dias = datos.Dias.Where(x => FichaDocenteDatos.Pasa(x, mostrar));
            if (semana.HasValue)
            {
                DateTime lunes = semana.Value;
                dias = dias.Where(x => x.Fecha >= lunes && x.Fecha <= lunes.AddDays(6));
                lblSemana.Text = "Semana del " + EstadisticasDatos.Fecha(lunes) + " al " + EstadisticasDatos.Fecha(lunes.AddDays(6));
            }

            lblSemana.Visible = semana.HasValue;
            lnkQuitarSemana.Visible = semana.HasValue;
            tDias.Poner(dias.ToList());
            AcomodarPagina(tpClases);
        }

        private void QuitarSemana()
        {
            semana = null;
            mes.Semana = new DateTime(1900, 1, 1);
            PonerDias();
        }

        private void MostrarClase(FilaClase c)
        {
            if (c == null)
            {
                fichaClase.ConDatos = false;
                fichaClase.Vacio = datos == null ? "" : datos.R.Clases.Count == 0 ? "" : "Elige una clase para ver a sus estudiantes.";
                tLista.Vaciar();
                return;
            }

            string laboratorio = VentanaEstadisticas.Guion(c.Laboratorios);
            rMateria.Texto = c.Materia;
            rGrupo.Texto = c.Grupo;
            rLaboratorio.Texto = laboratorio;
            rLista.Texto = EstadisticasDatos.Contar(c.Estudiantes, "estudiante", "estudiantes") + " · " + EstadisticasDatos.Contar(c.DiasQueCuentan, "día", "días");
            lblDatosClase.Text = c.Materia + " (" + c.Grupo + ") · " + laboratorio + " · " + c.LineaDeLaLista;
            ayuda.SetToolTip(lblDatosClase, lblDatosClase.Text);
            numeroEnLista = new Dictionary<ParDeAsistencia, int>();
            for (int i = 0; i < c.Lista.Count; i++)
            {
                numeroEnLista[c.Lista[i]] = i + 1;
            }

            tLista.Poner(c.Lista);
            fichaClase.ConDatos = true;
            AcomodarFichaClase();
        }

        private AgrupacionGraficas AgrupacionVista
        {
            get { return agrupacionElegida ?? (datos != null ? datos.R.Graficas.Sugerida : AgrupacionGraficas.Semana); }
        }

        private void PonerGraficas()
        {
            if (datos == null)
            {
                panelGraficas.Avisar(error ?? TextoLeyendo, error != null);
                return;
            }

            ResultadoEstadisticas r = datos.R;
            AgrupacionGraficas agrupacion = AgrupacionVista;
            poniendoAgrupacion = true;
            try
            {
                rbPorSemana.Checked = agrupacion == AgrupacionGraficas.Semana;
                rbPorMes.Checked = agrupacion == AgrupacionGraficas.Mes;
            }
            finally
            {
                poniendoAgrupacion = false;
            }

            string linea = EstadisticasDatos.CabezaDelPeriodo(r) + (agrupacion == AgrupacionGraficas.Mes ? ", por mes" : ", por semana")
                + ": sus clases, la asistencia de sus estudiantes, las fallas en sus clases y el uso de sus horas.";
            lblLineaGraficas.Text = linea;
            ayuda.SetToolTip(lblLineaGraficas, linea);
            if (r.TextoVacio != null && r.Clases.Count == 0)
            {
                panelGraficas.Avisar(datos.SinClases ?? r.TextoVacio, false);
                return;
            }

            panelGraficas.Poner(ContenidoGraficas.Armar(r, agrupacion, true, true));
        }

        // =====================================================================
        // Lo que se abre (todo solo lee)
        // =====================================================================

        private void CifraPulsada(int cual)
        {
            tabs.SelectedTab = cual == 0 ? tpClases : cual <= 2 ? tpGrupos : tpFallas;
        }

        /// <summary>"Ver ›" de un renglon de "Para revisar": su evidencia, con su filtro puesto.</summary>
        private void IrA(PendienteDeDocente p)
        {
            if (p == null || datos == null)
            {
                return;
            }

            switch (p.Destino)
            {
                case DestinoPendiente.SinRegistro:
                case DestinoPendiente.Canceladas:
                case DestinoPendiente.NoCuentan:
                    MostrarDias cual = p.Destino == DestinoPendiente.SinRegistro ? MostrarDias.SinRegistro
                        : p.Destino == DestinoPendiente.Canceladas ? MostrarDias.Canceladas : MostrarDias.AunNoONoCuentan;
                    semana = null;
                    mes.Semana = new DateTime(1900, 1, 1);
                    rbMostrar[(int)cual].Checked = true;
                    if (mostrar != cual)
                    {
                        mostrar = cual;
                    }

                    PonerDias();
                    tabs.SelectedTab = tpClases;
                    break;
                case DestinoPendiente.Clase:
                    tabs.SelectedTab = tpGrupos;
                    if (tClases.ElegirFila(c => c.ClaseId == p.ClaseId))
                    {
                        MostrarClase(tClases.Elegido);
                    }

                    break;
                case DestinoPendiente.FaltaAsignar:
                    VerFaltaAsignar();
                    break;
                case DestinoPendiente.Fallas:
                    tabs.SelectedTab = tpFallas;
                    break;
                case DestinoPendiente.Dadas:
                    if (tabs.TabPages.Contains(tpDadas))
                    {
                        tabs.SelectedTab = tpDadas;
                    }

                    break;
            }
        }

        private static string QueAbre(PendienteDeDocente p)
        {
            switch (p.Destino)
            {
                case DestinoPendiente.SinRegistro:
                    return "sus clases sin registro";
                case DestinoPendiente.Canceladas:
                    return "sus clases canceladas";
                case DestinoPendiente.NoCuentan:
                    return "sus clases que aún no pasan o no cuentan";
                case DestinoPendiente.Clase:
                    return "la lista de esa clase";
                case DestinoPendiente.FaltaAsignar:
                    return "lo que le falta asignar";
                case DestinoPendiente.Fallas:
                    return "sus fallas";
                case DestinoPendiente.Dadas:
                    return "las asistencias que dio";
                default:
                    return "";
            }
        }

        private ClaseDelCalendario SesionDelDia(ClaseDiaDocente dia)
        {
            return dia == null || dia.Calendario == null ? null : PestanaCalendario.SesionQueAbrir(dia.Calendario);
        }

        /// <summary>La clase del calendario cuya bitacora abre esa sesion, o null si no esta en el horario leido.</summary>
        private ClaseDelCalendario SesionDeLaSesion(int sesion)
        {
            if (lectura == null)
            {
                return null;
            }

            ClaseDelCalendario clase = lectura.Calendario.Clases.FirstOrDefault(c => c.SesionId == sesion && c.ProfesorId == id);
            return clase == null ? null : PestanaCalendario.SesionQueAbrir(clase);
        }

        /// <summary>Doble clic en una clase-dia: su bitacora, como Estadisticas (BitacoraSesion, solo lee).</summary>
        private void AbrirBitacora(ClaseDiaDocente dia)
        {
            if (dia == null || lectura == null)
            {
                return;
            }

            ClaseDelCalendario abrir = SesionDelDia(dia);
            if (abrir == null)
            {
                bool enCurso = dia.Estado == EstadoEnCalendario.PorVenir && dia.Fecha == lectura.Hoy && dia.Inicio <= lectura.Calendario.Ahora.TimeOfDay;
                Avisar(dia.Cancelada ? "Esa clase se canceló: no tiene bitácora."
                    : !dia.SesionId.HasValue && enCurso ? "Esa clase sigue en curso: todavía nadie ha registrado su bitácora."
                    : !dia.SesionId.HasValue && dia.Estado == EstadoEnCalendario.PorVenir ? "Esa clase aún no pasa: todavía nadie ha registrado su bitácora."
                    : !dia.SesionId.HasValue ? "Esa clase no tiene bitácora: nadie abrió su sesión."
                    : "Su sesión no está en el horario: búscala en la sección Bitácoras.");
                return;
            }

            AbrirBitacoraDe(abrir, dia.Laboratorio);
        }

        private void AbrirBitacoraDelReporte(FilaReporteDocente reporte)
        {
            ClaseDelCalendario abrir = reporte == null ? null : SesionDeLaSesion(reporte.SesionId);
            if (abrir == null)
            {
                Avisar("Su sesión no está en el horario: búscala en la sección Bitácoras.");
                return;
            }

            AbrirBitacoraDe(abrir, reporte.Laboratorio);
        }

        private void AbrirBitacoraDe(ClaseDelCalendario abrir, string laboratorio)
        {
            string horario = HorariosDatos.NombresDia[((int)abrir.Fecha.DayOfWeek + 6) % 7 + 1] + " " + EstadisticasDatos.Fecha(abrir.Fecha) + ", "
                + HorariosDatos.Hora(abrir.Inicio) + " a " + HorariosDatos.Hora(abrir.Fin) + ", " + VentanaEstadisticas.Guion(laboratorio)
                + (abrir.EsExtra ? " (sesión extra)" : "");
            try
            {
                using (BitacoraSesion bitacora = new BitacoraSesion(abrir.EsExtra ? (int?)null : abrir.FranjaId, abrir.EsExtra ? abrir.ExcepcionId : null,
                    abrir.Materia + ", " + abrir.Grupo, horario, abrir.Profesor, abrir.Fecha))
                {
                    bitacora.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Ficha del docente " + id + ": abrir una bitacora", ex, "No se pudo abrir la bitácora de la clase", Text);
            }
        }

        /// <summary>
        /// Las bitacoras de un estudiante (VentanaHistorialAlumno, como "Ver sus bitacoras" de
        /// Estadisticas): primero lee al estudiante en segundo plano (AlumnosDatos.Obtener).
        /// </summary>
        private void VerSusBitacoras(int alumno)
        {
            if (leyendoAlumno)
            {
                return;
            }

            leyendoAlumno = true;
            btnSusBitacoras.Enabled = false;
            Cursor = Cursors.AppStarting;
            Task.Run(() => new AlumnosDatos().Obtener(alumno)).ContinueWith(t => EnLaVentana(() =>
            {
                leyendoAlumno = false;
                btnSusBitacoras.Enabled = true;
                Cursor = Cursors.Default;
                if (t.IsFaulted)
                {
                    AvisoDeFalloAdmin.Mostrar(this, "Leer los datos del estudiante id " + alumno, t.Exception.GetBaseException(), "No se pudieron leer los datos del estudiante", Text);
                    return;
                }

                AlumnoInfo encontrado = t.Result;
                if (encontrado == null)
                {
                    MessageBox.Show(this, "Ese estudiante ya no está en DigLab.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (VentanaHistorialAlumno historial = new VentanaHistorialAlumno(encontrado))
                {
                    historial.ShowDialog(this);
                }
            }, t));
        }

        /// <summary>"Ver la lista dia por dia...": la hoja y el tablero de la clase, solo para ver (VentanaListaDeClase).</summary>
        private void VerListaDiaPorDia(FilaClase c)
        {
            if (c == null || lectura == null)
            {
                Avisar("Elige una clase.");
                return;
            }

            using (VentanaListaDeClase lista = new VentanaListaDeClase(lectura, c, NombreDe(profesor)))
            {
                lista.ShowDialog(this);
            }
        }

        /// <summary>Su horario de la semana (compacto, solo para ver, sin codigos de clase); con un dia, en la semana de ese dia.</summary>
        private void VerHorario(DateTime? dia)
        {
            using (BitacoraMisClases horario = new BitacoraMisClases(id, NombreDe(profesor), true))
            {
                if (dia.HasValue)
                {
                    horario.EmpezarEnLaSemana(dia.Value);
                }

                horario.ShowDialog(this);
            }
        }

        private void VerSusBitacorasDocente()
        {
            string texto = ((profesor.NombreCompleto ?? "") + " " + (profesor.Matricula ?? "")).Trim();
            if (VerBitacoras != null)
            {
                VerBitacoras(texto);
                return;
            }

            // Sin el panel (no pasa hoy): una ventana suya.
            GestionBitacorasAdministrador bitacoras = new GestionBitacorasAdministrador();
            bitacoras.Buscar(texto);
            bitacoras.Show(this);
        }

        /// <summary>"Falta asignar...": la lista de lo suyo, solo para verla (sin "Ponerla" ni "Ya no hace falta").</summary>
        private void VerFaltaAsignar()
        {
            if (datos == null || datos.PorAsignar == null || datos.PorAsignar.Count == 0)
            {
                Avisar("No le falta nada por asignar en el horario en uso.");
                return;
            }

            using (DialogoFaltaAsignar dialogo = new DialogoFaltaAsignar(datos.PorAsignar, new List<OpcionCombo>(), 0, null, NombreDe(profesor)))
            {
                dialogo.ShowDialog(this);
            }
        }

        /// <summary>
        /// "Ver en Estadisticas": con el como docente, el periodo de lo que se ve y la misma lectura
        /// (LecturaVista: Estadisticas calcula con ella), para que sus cifras y sus CSV sean los de la
        /// ficha (ahi se guarda en Excel). Mientras lee otro periodo, espera, como el PDF.
        /// </summary>
        private void AbrirEnEstadisticas()
        {
            if (leyendo)
            {
                Avisar("Espera a que se lea la ficha para verla en Estadísticas.");
                return;
            }

            if (datos == null || lectura == null)
            {
                Avisar("Aún no hay nada que ver en Estadísticas: pulsa Actualizar.");
                return;
            }

            // Sin clases del horario en el periodo, Estadisticas no lo deja elegido (vuelve a todo el
            // personal, con su aviso): mejor decirlo aqui que ensenar cifras de toda la escuela.
            if (datos.SinClases != null)
            {
                Avisar("Sin clases en estas fechas: elige otro periodo.");
                return;
            }

            PeriodoElegido visto = periodoVisto ?? periodo;
            if (VerEnEstadisticas != null)
            {
                VerEnEstadisticas(id, visto);
                return;
            }

            VentanaEstadisticas estadisticas = new VentanaEstadisticas();
            estadisticas.Show(this);
            estadisticas.MostrarDocente(id, visto, lectura);
        }

        // =====================================================================
        // Reporte del docente (PDF)
        // =====================================================================

        /// <summary>
        /// "Reporte del docente (PDF)...": primero pregunta si lleva la lista de cada grupo (sin
        /// marcar); despues, con el motor del reporte para auditoria, su vista previa. Solo la
        /// administracion (la regla del CSV de Docentes); en kiosco no existe.
        /// </summary>
        private void ReporteDelDocente()
        {
            if (Kiosco.Activo || btnPdf == null || !EstadisticasDatos.ReporteConDocentes(Permisos.SoloConsulta))
            {
                return;
            }

            if (leyendo)
            {
                Avisar("Espera a que se lea la ficha para el reporte.");
                return;
            }

            if (datos == null || lectura == null)
            {
                Avisar("Aún no hay nada para el reporte: pulsa Actualizar.");
                return;
            }

            bool conListas;
            if (!DialogoConfirmacion.Preguntar(this, "Reporte del docente", "¿Preparar el reporte de " + NombreDe(profesor) + "?",
                "Lleva sus cifras, sus clases día por día, la asistencia de sus grupos, sus fallas y lo que dio en el periodo elegido, con las firmas al final. "
                + "Lo verás antes de guardarlo.", "Preparar", false,
                "Incluir la lista de cada grupo (matrícula, nombre y asistencia de cada estudiante)", false, out conListas))
            {
                return;
            }

            byte[] pdf;
            DatosDelReporte datosDelReporte;
            Cursor = Cursors.WaitCursor;
            try
            {
                pdf = ReporteEnBytes(conListas, out datosDelReporte);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MostrarError("Ficha del docente " + id + ": reporte", ex, "No se pudo preparar el reporte del docente");
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            try
            {
                VisorPdf.Mostrar(this, pdf, NombreDelPdf(datosDelReporte));
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.MostrarVistaPrevia(this, "Ficha del docente: reporte (visor)", ex, "No se pudo mostrar el reporte del docente", Text);
            }
        }

        /// <summary>El PDF del reporte con lo que se ve (sin dialogos: tambien lo usan las pruebas).</summary>
        internal byte[] ReporteEnBytes(bool conListas, out DatosDelReporte d)
        {
            d = DatosParaElReporte(conListas);
            byte[] pdf = ReporteAuditoriaPdf.Crear(datos.R, d);
            // Sin nombres en el log: el id, las fechas y los bytes.
            Log.Info("Ficha del docente: reporte del docente id " + id + " del " + d.Desde.ToString("yyyy-MM-dd", Inv) + " al "
                + d.Hasta.ToString("yyyy-MM-dd", Inv) + " (" + pdf.Length + " bytes" + (conListas ? ", con las listas" : "") + ").");
            return pdf;
        }

        internal DatosDelReporte DatosParaElReporte(bool conListas)
        {
            ResultadoEstadisticas r = datos.R;
            AgrupacionGraficas agrupacion = AgrupacionVista;
            DateTime generado = DateTime.Now + VentanaEstadisticas.Desfase(lectura);
            // Cada laboratorio una vez: el texto de cada clase ya viene unido ("A y B") y juntarlos
            // repetia los que comparten ("A y B, A").
            List<string> laboratorios = r.Clases.SelectMany(c => c.NombresDeLaboratorios).Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            List<string> grupos = r.Clases.Select(c => c.Grupo).Distinct().OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase).ToList();
            return new DatosDelReporte
            {
                Titulo = "Reporte del docente",
                Periodo = EstadisticasDatos.NombreDelPeriodo(r),
                NombreCorto = EstadisticasDatos.NombreCortoDelPeriodo(r),
                NotaDelPeriodo = EstadisticasDatos.NotaDelPeriodoParaElReporte(r),
                Desde = r.Desde,
                Hasta = r.Hasta,
                Docente = NombreDe(profesor) + " (No. de empleado " + (profesor.Matricula ?? "").Trim() + ")",
                Laboratorio = laboratorios.Count == 0 ? "Sin clases del horario en estas fechas" : "Sus laboratorios: " + string.Join(", ", laboratorios),
                Grupo = grupos.Count == 0 ? "Sin grupos en estas fechas" : "Sus grupos: " + string.Join(", ", grupos),
                Agrupacion = agrupacion,
                GeneradoPor = (Datos_User.getUser() ?? "-") + (Permisos.SoloConsulta ? " (responsable de laboratorio, solo consulta)" : " (administración de DigLab)"),
                GeneradoEn = generado,
                LeidoEn = lectura.AhoraServidor,
                ConDocentes = true,
                Secciones = ContenidoFichaDocente.Armar(datos, agrupacion, conListas),
                NotasFinales = ContenidoFichaDocente.NotasFinales(generado)
            };
        }

        /// <summary>"Reporte del docente 104523 2026-09-30 al 2026-10-31.pdf": con su numero, no con su nombre.</summary>
        internal string NombreDelPdf(DatosDelReporte d)
        {
            return PdfBitacora.NombreArchivo("Reporte del docente " + (profesor.Matricula ?? id.ToString(Inv)).Trim() + " "
                + d.Desde.ToString("yyyy-MM-dd", Inv) + " al " + d.Hasta.ToString("yyyy-MM-dd", Inv));
        }

        // =====================================================================
        // Teclado, cierre y limpieza
        // =====================================================================

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Handled && e.KeyCode == Keys.F5 && e.Modifiers == Keys.None)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (btnActualizar.Enabled)
                {
                    Leer(true);
                }
            }
        }

        private void EnLaVentana(Action accion, Task tarea)
        {
            if (tarea != null && tarea.IsFaulted)
            {
                AggregateException vista = tarea.Exception;
                GC.KeepAlive(vista);
            }

            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed)
                    {
                        accion();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // Se cerro mientras se leia.
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            lecturas++;
            espera.Stop();
            if (Kiosco.Activo)
            {
                VentanaEstadisticas.VaciarPortapapelesDeDigLab();
            }

            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                espera.Dispose();
                if (!tabs.TabPages.Contains(tpDadas))
                {
                    tpDadas.Dispose();
                }
            }

            base.Dispose(disposing);
            if (disposing)
            {
                foreach (DrawingFont fuente in new[] { fuenteNegrita, fuenteTituloFicha, fuenteRotulo, fuenteValor, fuenteLinea, fuentePestana, fuenteOpcion })
                {
                    fuente.Dispose();
                }

                estiloGrafica.Dispose();
            }
        }
    }
}
