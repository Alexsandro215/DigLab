using DigLab.conexion;
using DigLab.Infraestructura;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// Quien registro su bitacora en una clase concreta. La abre el profesor al
    /// pulsar una clase de su horario (BitacoraMisClases) y el administrador
    /// desde sus pantallas. Se refresca sola mientras dura la clase, lleva la
    /// cuenta atras hasta la hora de salida y descarga el PDF de esa sesion
    /// (primero la vista previa, VisorPdf; desde ahi se elige donde guardarlo).
    /// No escribe en la base: el encargado de laboratorio (solo consulta) la usa
    /// igual desde Alertas y Faltas, sin el codigo en el nombre del PDF.
    ///
    /// Las fallas que reporto el profesor (db/15) van a la vista, en una franja
    /// naranja entre el encabezado y la tabla, un renglon por reporte. La fila
    /// elegida va en verde claro (la de una falla, en un naranja mas intenso) y
    /// ni el refresco de cada 10 s ni F5 la cambian, ni el orden, ni lo que se ve.
    ///
    /// Las asistencias que dio el profesor a quien no pudo registrar (db/22) no
    /// son bitacoras: van al final de la tabla, en verde claro, una fila por
    /// asistencia ("Se la diste: su equipo no tenía internet"); la linea de abajo
    /// las cuenta y el PDF las lleva en su propia tabla.
    ///
    /// Desde 2026-10-31, con el estilo de las demas listas (EstiloHorarios.CrearGrid):
    /// el nombre como la lista oficial ("Arias Mora Gisela"; el PDF no cambia) y lo
    /// reportado en una sola columna, "Reporte" (Consultas.DescribirFallas), como en
    /// Mis bitacoras.
    /// </summary>
    public class BitacoraSesion : Form
    {
        // Mismos tonos que el resto: claro para la bitacora que reporta algo,
        // intenso para la celda con la falla.
        private static readonly Color ColorConFalla = Color.FromArgb(255, 240, 214);
        private static readonly Color ColorFallaCelda = Color.FromArgb(255, 176, 79);
        // La fila de una asistencia que dio su docente (no es una bitacora): verde claro.
        private static readonly Color ColorDada = Color.FromArgb(232, 245, 233);
        private static readonly Color ColorDadaElegida = Color.FromArgb(205, 232, 210);
        private static readonly Color TextoDada = Color.FromArgb(0, 96, 55);
        // La fila elegida: el verde claro de las listas (EstiloHorarios.FilaElegida) con
        // letra negra, no el azul de Windows. La de una falla sigue naranja, mas
        // intensa: en verde no se veria la falla.
        private static readonly Color ColorConFallaElegida = Color.FromArgb(255, 218, 168);
        private static readonly Color ColorFallaCeldaElegida = Color.FromArgb(234, 150, 52);
        // Los equipos que el alumno aviso sin internet (columna Sin internet).
        private static readonly Color TextoSinInternet = Color.FromArgb(190, 85, 0);
        // La franja de lo que reporto el profesor: el fondo de las filas con falla,
        // una barra del naranja de la celda a la izquierda y el titulo en naranja oscuro.
        private static readonly Color TituloReportes = Color.FromArgb(160, 75, 0);
        private static readonly Color TextoReportes = Color.FromArgb(50, 50, 50);
        private const int SegundosEntreRefrescos = 10;

        /// <summary>La ve el docente de la clase: los textos le hablan de tu ("Se la diste", "Lo que reportaste").</summary>
        private static bool EsSuDocente
        {
            get { return Datos_User.TipoUsuario == TiposUsuario.Profesor; }
        }

        // Donde empieza la tabla sin franja, la franja y lo que queda debajo de la tabla.
        private const int ArribaDeLaTabla = 102;
        private const int ArribaDeLaFranja = 100;
        private const int DebajoDeLaTabla = 60;
        private const int BarraFranja = 4;
        // Dentro del borde de la franja, arriba y abajo del texto.
        private const int RellenoFranja = 6;

        private readonly Consultas consultas = new Consultas();
        private readonly int? idFranja;
        private readonly int? idExcepcion;
        private readonly string descripcionClase;
        private readonly string descripcionHorario;
        private readonly string nombreProfesor;
        private readonly DateTime? fecha;

        private int idSesion;
        private VentanaCodigo ventana;
        private DateTime finEnRelojLocal;
        private int ticks;
        private Timer temporizador;
        // Mientras se lee (o se ensena el error de Actualizar), el temporizador no
        // vuelve a entrar: el MessageBox sigue despachando sus ticks.
        private bool refrescando;
        // El refresco automatico fallo: se dice en vez de la cuenta atras hasta que una lectura salga bien.
        private bool sinLeer;

        private Label lblClase;
        private Label lblHorario;
        private Label lblRestante;
        // La franja de las fallas que reporto el profesor: el marco (pinta la barra y
        // el borde, no se desplaza) y, dentro, el titulo y los renglones, que se
        // desplazan si no caben.
        private Panel pnlReportes;
        private Panel pnlRenglones;
        private Label lblReportesTitulo;
        private Label lblReportes;
        // Lo que dice la franja (vacio: sin franja); se compara para no rehacerla cada 10 s.
        private string renglonesPuestos = "";
        // Cuantos reportes dice la franja: la linea de abajo los cuenta aunque la lectura siguiente falle.
        private int reportesPuestos;
        // Ya se anoto en el log que no se pudieron leer los reportes (no cada 10 s).
        private bool avisoReportes;
        // Las asistencias que dio el profesor en esta sesion (db/22; vacia sin la 22). Si
        // una lectura falla se quedan las de la anterior, como los reportes.
        private List<AsistenciaDada> dadasPuestas = new List<AsistenciaDada>();
        private bool avisoDadas;
        private readonly LectorDeDadas lectorDadas = new LectorDeDadas();
        // La ayuda de la linea de abajo, que dice a quien se le dio. Solo desde que hubo
        // alguna: con su propia ayuda la Label ya no ensena la suya del "..." (el texto
        // entero), asi que desde entonces esta lo lleva siempre.
        private readonly ToolTip ayudaCantidad = new ToolTip { AutoPopDelay = 30000, InitialDelay = 400 };
        private bool conAyudaCantidad;
        // Lo que dice esa ayuda: solo se vuelve a poner si cambio (no cada 10 s, por si se esta viendo).
        private string ayudaCantidadPuesta;
        private readonly ToolTip ayudaBotones = new ToolTip();
        // La celda de la falla, en negrita con la letra de la tabla (no con la del formulario).
        private readonly DrawingFont fuenteFalla = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
        private DataGridView dgvAlumnos;
        private Label lblCantidad;
        private Panel pnlLeyenda;
        private Label lblLeyenda;
        // La muestra verde de la leyenda: solo con asistencias dadas en la tabla (un
        // campo y no Visible, que antes de ensenarse la ventana dice false).
        private Panel pnlLeyendaDada;
        private Label lblLeyendaDada;
        private bool conLeyendaDada;
        // La leyenda no pasa de aqui: 12 px antes de "Actualizar" (AcomodarLeyenda).
        private const int ActualizarDesdeLaDerecha = 384;
        private const string NaranjaLarga = "Naranja: la falla que se reportó en la bitácora";
        private const string NaranjaCorta = "Naranja: falla reportada";
        private const string VerdeCorta = "Verde: asistencia sin bitácora";
        private Button btnActualizar;
        private Button btnDescargar;
        private Button btnCerrar;

        /// <summary>
        /// Se recibe la franja o la excepcion, no la sesion: si al abrir todavia
        /// nadie ha registrado, la ventana la vuelve a buscar en cada refresco y
        /// se llena sola cuando el primer alumno entra. Con fecha, la sesion de la
        /// franja ese dia (la clase de hoy); sin ella, la mas reciente.
        /// </summary>
        public BitacoraSesion(int? idFranja, int? idExcepcion, string descripcionClase, string descripcionHorario, string nombreProfesor,
            DateTime? fecha = null)
        {
            this.idFranja = idFranja;
            this.idExcepcion = idExcepcion;
            this.descripcionClase = descripcionClase ?? "";
            this.descripcionHorario = descripcionHorario ?? "";
            this.nombreProfesor = nombreProfesor ?? "";
            this.fecha = fecha;

            InicializarComponentes();
            Refrescar();
        }

        private void InicializarComponentes()
        {
            BackColor = Color.White;
            System.Drawing.Rectangle area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1060, area.Width - 80), Math.Min(600, area.Height - 80));
            MinimumSize = new Size(Math.Min(860, area.Width - 40), Math.Min(480, area.Height - 40));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            // El icono de DigLab en su barra: el del panel del administrador, como Mi horario.
            Icon = (Icon)new ComponentResourceManager(typeof(PrincipalAdministrador)).GetObject("$this.Icon");
            Text = "Bitácora de la clase";

            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;

            lblClase = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 15F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Size = new Size(ancho - 48, 28),
                Text = descripcionClase
            };

            lblHorario = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(26, 48),
                Size = new Size(ancho - 52, 20),
                Text = descripcionHorario
            };

            // Con "..." y su ayuda si no cabe: el aviso de un fallo para la administracion
            // (revisa el registro...) pide unos 920 px y la ventana se puede achicar a 860.
            lblRestante = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(190, 90, 0),
                Location = new Point(26, 72),
                Size = new Size(ancho - 52, 22),
                Text = ""
            };

            // Oculta hasta que haya un reporte del profesor (Leer y AcomodarReportes).
            pnlReportes = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = ColorConFalla,
                Location = new Point(24, ArribaDeLaFranja),
                Size = new Size(ancho - 48, 40),
                Visible = false
            };
            pnlReportes.Paint += PintarMarcoReportes;
            pnlRenglones = new Panel { AutoScroll = true, BackColor = ColorConFalla };
            lblReportesTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = TituloReportes,
                Text = EsSuDocente ? "Lo que reportaste:" : "Reportado por su docente:",
                UseMnemonic = false
            };
            // Su tamano lo pone AcomodarReportes: el ancho que queda y el alto de sus renglones partidos.
            lblReportes = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.75F),
                ForeColor = TextoReportes,
                UseMnemonic = false
            };
            pnlRenglones.Controls.Add(lblReportesTitulo);
            pnlRenglones.Controls.Add(lblReportes);
            pnlReportes.Controls.Add(pnlRenglones);

            // Como las demas listas: renglones de 34, lineas suaves y sin marco; los
            // titulos enteros y, sin nadie, el porque en medio (ListaDigLab).
            dgvAlumnos = EstiloHorarios.CrearGrid(new Point(24, ArribaDeLaTabla), new Size(ancho - 48, alto - ArribaDeLaTabla - DebajoDeLaTabla));
            dgvAlumnos.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgvAlumnos.CellFormatting += dgvAlumnos_CellFormatting;

            // Hasta 10 px antes de Actualizar: si no cabe (ventana chica), "..." y el texto entero al pasar el raton.
            lblCantidad = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(26, alto - 52),
                Size = new Size(ancho - ActualizarDesdeLaDerecha - 10 - 26, 20),
                Text = "",
                UseMnemonic = false
            };

            pnlLeyenda = new Panel
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = ColorFallaCelda,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(26, alto - 28),
                Size = new Size(14, 14)
            };

            lblLeyenda = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 8.25F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(45, alto - 29),
                Text = NaranjaLarga
            };

            // Verde: una asistencia que dio su docente (al final de la tabla). Solo si hay alguna.
            pnlLeyendaDada = new Panel
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = ColorDada,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(lblLeyenda.Left + lblLeyenda.PreferredWidth + 18, alto - 28),
                Size = new Size(14, 14),
                Visible = false
            };
            lblLeyendaDada = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                AutoSize = true,
                Font = lblLeyenda.Font,
                ForeColor = lblLeyenda.ForeColor,
                Location = new Point(pnlLeyendaDada.Right + 5, alto - 29),
                Text = VerdeLarga,
                Visible = false
            };

            // Los botones de siempre (EstiloHorarios.CrearBoton): borde fino y, apagados, grises.
            btnActualizar = EstiloHorarios.CrearBoton("Actualizar", new Point(ancho - ActualizarDesdeLaDerecha, alto - 44), new Size(110, 34));
            btnActualizar.Click += (s, e) => Refrescar();
            ayudaBotones.SetToolTip(btnActualizar, "Vuelve a leer quién registró (F5).");

            // Como en Mis bitacoras: primero la vista previa, y desde ahi se guarda.
            btnDescargar = EstiloHorarios.CrearBoton("PDF de esta sesión", new Point(ancho - 264, alto - 44), new Size(160, 34));
            btnDescargar.Click += (s, e) => DescargarPdf();
            ayudaBotones.SetToolTip(btnDescargar, "Abre la vista previa del PDF de esta sesión; desde ahí se guarda o se imprime.");

            btnCerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(ancho - 94, alto - 44), new Size(70, 34));
            btnCerrar.Click += (s, e) => Close();

            foreach (Button boton in new[] { btnActualizar, btnDescargar, btnCerrar })
            {
                boton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            }

            Controls.Add(lblClase);
            Controls.Add(lblHorario);
            Controls.Add(lblRestante);
            Controls.Add(pnlReportes);
            Controls.Add(dgvAlumnos);
            Controls.Add(lblCantidad);
            Controls.Add(pnlLeyenda);
            Controls.Add(lblLeyenda);
            Controls.Add(pnlLeyendaDada);
            Controls.Add(lblLeyendaDada);
            Controls.Add(btnActualizar);
            Controls.Add(btnDescargar);
            Controls.Add(btnCerrar);
            AcomodarLeyenda();

            CancelButton = btnCerrar;
            // F5 actualiza desde cualquier sitio de la ventana (OnKeyDown), como en Mis grupos y Bitacoras.
            KeyPreview = true;

            temporizador = new Timer { Interval = 1000 };
            temporizador.Tick += Temporizador_Tick;
            temporizador.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Un Timer de WinForms vive en el bucle de mensajes, no en el
            // formulario: sin esto seguiria consultando la base tras cerrar.
            if (temporizador != null)
            {
                temporizador.Stop();
                temporizador.Dispose();
                temporizador = null;
            }

            ayudaBotones.Dispose();
            ayudaCantidad.Dispose();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                fuenteFalla.Dispose();
            }
        }

        /// <summary>F5, desde cualquier sitio de la ventana (KeyPreview): lo mismo que Actualizar.</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Handled && e.KeyCode == Keys.F5 && e.Modifiers == Keys.None)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Refrescar();
            }
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            // Al cambiar el ancho, los renglones se parten en otro sitio: la franja y la tabla se recolocan.
            AcomodarReportes(true);
            AcomodarLeyenda();
        }

        /// <summary>"Verde: ...", al docente de tu (como en sus filas: "Se la diste").</summary>
        private static string VerdeLarga
        {
            get { return EsSuDocente ? "Verde: asistencia que diste sin bitácora" : "Verde: asistencia que dio su docente sin bitácora"; }
        }

        /// <summary>
        /// La leyenda de abajo a la izquierda, sin meterse bajo "Actualizar": con la
        /// muestra verde y la ventana angosta (en su minimo, o a 1024 con el texto de la
        /// administracion) se pintaba encima del boton. Con sitio, los textos largos; si
        /// no, primero el naranja corto y despues tambien el verde, enteros al pasar el
        /// raton; si ni asi cabe (una pantalla muy angosta), el verde se corta con "...".
        /// </summary>
        private void AcomodarLeyenda()
        {
            if (lblLeyendaDada == null || pnlLeyendaDada == null)
            {
                return;
            }

            int limite = ClientSize.Width - ActualizarDesdeLaDerecha - 12;
            string verdeLarga = VerdeLarga;
            PonerLeyenda(NaranjaLarga, verdeLarga);
            if (conLeyendaDada && lblLeyendaDada.Right > limite)
            {
                PonerLeyenda(NaranjaCorta, verdeLarga);
            }

            if (conLeyendaDada && lblLeyendaDada.Right > limite)
            {
                PonerLeyenda(NaranjaCorta, VerdeCorta);
            }

            if (conLeyendaDada && lblLeyendaDada.Right > limite)
            {
                int alto = lblLeyendaDada.Height;
                lblLeyendaDada.AutoSize = false;
                lblLeyendaDada.AutoEllipsis = true;
                lblLeyendaDada.Size = new Size(Math.Max(20, limite - lblLeyendaDada.Left), alto);
            }

            ayudaBotones.SetToolTip(lblLeyenda, lblLeyenda.Text == NaranjaLarga ? null : NaranjaLarga);
            ayudaBotones.SetToolTip(lblLeyendaDada, lblLeyendaDada.Text == verdeLarga ? null : verdeLarga);
        }

        /// <summary>Esos textos en la leyenda, cada uno de su ancho y la muestra verde detras del naranja.</summary>
        private void PonerLeyenda(string naranja, string verde)
        {
            lblLeyenda.Text = naranja;
            pnlLeyendaDada.Left = lblLeyenda.Left + lblLeyenda.PreferredWidth + 18;
            lblLeyendaDada.AutoSize = true;
            lblLeyendaDada.Text = verde;
            lblLeyendaDada.Left = pnlLeyendaDada.Right + 5;
        }

        // =====================================================================
        // Datos
        // =====================================================================

        /// <summary>
        /// Lee quien registro. El refresco automatico (cada 10 s) no avisa con un
        /// MessageBox si falla: lo dice en la linea de la cuenta atras y lo
        /// reintenta en el siguiente; al abrir y con Actualizar, si.
        /// </summary>
        private void Refrescar(bool automatico = false)
        {
            if (refrescando)
            {
                return;
            }

            refrescando = true;
            try
            {
                sinLeer = false;
                Leer();
            }
            catch (Exception ex)
            {
                Log.Error("BitacoraSesion.Refrescar", ex);
                if (automatico)
                {
                    // En su linea, corto: si fue la red y que se reintenta sola; si no, a quien avisar.
                    sinLeer = true;
                    lblRestante.Text = MensajeDeFallo.EsDeRed(ex)
                        ? "No se pudo leer la bitácora: no hay conexión con el servidor. Se vuelve a intentar sola."
                        : "No se pudo leer la bitácora: se vuelve a intentar sola; si sigue, " + AvisoDeFalloDocente.SiSigue + ".";
                }
                else
                {
                    // La causa y que hacer; el detalle tecnico, al registro (arriba). Sin dueno,
                    // como antes: al abrir, esto pasa en el constructor, sin ventana aun.
                    MessageBox.Show(MensajeDeFallo.Explicar("No se pudo leer la bitácora", ex, AvisoDeFalloDocente.SiSigue), Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                refrescando = false;
            }
        }

        private void Leer()
        {
            if (idSesion == 0)
            {
                // Aun no hay sesion: se busca otra vez, por si el primer
                // alumno acaba de entrar.
                idSesion = idFranja.HasValue
                    ? (fecha.HasValue ? consultas.ObtenerIdSesionDeFranja(idFranja.Value, fecha.Value) : consultas.ObtenerIdSesionDeFranja(idFranja.Value))
                    : idExcepcion.HasValue ? consultas.ObtenerIdSesionDeExcepcion(idExcepcion.Value) : 0;

                if (idSesion != 0)
                {
                    ventana = consultas.ObtenerVentanaSesion(idSesion);
                    if (ventana != null)
                    {
                        // La ventana se lee UNA vez con el reloj del servidor y
                        // se traslada al reloj local: el temporizador no vuelve
                        // a consultar la base.
                        finEnRelojLocal = ventana.FinEnRelojLocal;
                        lblHorario.Text = descripcionHorario + " (sesión del " + ventana.Inicio.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + ")";
                    }
                }
            }

            if (idSesion == 0)
            {
                dgvAlumnos.DataSource = null;
                ListaDigLab.PonerTextoVacio(dgvAlumnos, "Todavía nadie ha registrado su bitácora en esta clase.");
                PonerReportes(new List<ReporteProfesor>());
                lblRestante.Text = "Todavía nadie ha registrado su bitácora en esta clase.";
                lblCantidad.Text = "";
                btnDescargar.Enabled = false;
                return;
            }

            DataTable alumnos = consultas.consultaRegistroSesion(idSesion);

            // Las asistencias que dio su docente a quien no pudo registrar (db/22), al final
            // de la tabla. Si no se pueden leer, quien registro se ve igual y se quedan las de antes.
            try
            {
                dadasPuestas = lectorDadas.DeLaSesion(idSesion);
            }
            catch (Exception ex)
            {
                if (!avisoDadas)
                {
                    avisoDadas = true;
                    Log.Aviso("BitacoraSesion: no se pudieron leer las asistencias dadas de la sesion " + idSesion + ": " + ex.Message);
                }
            }

            VistaDeLaTabla vista = VistaDeLaTabla.Tomar(dgvAlumnos);
            dgvAlumnos.DataSource = TablaParaVer(alumnos, dadasPuestas);
            ListaDigLab.PonerTextoVacio(dgvAlumnos, "Todavía nadie ha registrado su bitácora en esta clase.");
            ConfigurarColumnas();
            vista.Devolver(dgvAlumnos);
            bool conDadas = dadasPuestas.Count > 0;
            pnlLeyendaDada.Visible = conDadas;
            lblLeyendaDada.Visible = conDadas;
            if (conDadas != conLeyendaDada)
            {
                conLeyendaDada = conDadas;
                AcomodarLeyenda();
            }

            // Las fallas que reporto el profesor en esta clase (db/15). Si no se
            // pueden leer, quien registro se ve igual y la franja se queda como estaba.
            try
            {
                PonerReportes(new ReportesProfesorDatos().DeLaSesion(idSesion));
            }
            catch (Exception ex)
            {
                if (!avisoReportes)
                {
                    avisoReportes = true;
                    Log.Aviso("BitacoraSesion: no se pudieron leer los reportes del profesor de la sesion " + idSesion + ": " + ex.Message);
                }
            }

            int conFalla = alumnos.Rows.Cast<DataRow>().Count(Consultas.ConFalla);
            string sinInternet = Consultas.DescribirSinInternet(Consultas.ContarSinInternet(
                alumnos.Rows.Cast<DataRow>().Select(f => f["avisa_sin_internet"].ToString())), true);
            // Los que dice la franja (si esta vez no se pudieron leer, los de la lectura anterior).
            int delProfesor = reportesPuestos;
            int dadas = dadasPuestas.Count;
            // "Ningun alumno reporto fallas" y no "sin fallas": junto a lo que reporto el profesor
            // se contradiria. Sin registros (la sesion la abrio un reporte del profesor o una
            // asistencia que dio), nada de los alumnos. Lo del profesor en una sola frase: a
            // 1366 la linea mide unos 680 px y "la asistencia a N estudiantes" ya no cabria.
            // Al docente se le habla de tu ("Diste 1 asistencia"), como en sus filas ("Se la diste").
            bool esSuDocente = EsSuDocente;
            List<string> delDocente = new List<string>();
            if (delProfesor > 0)
            {
                delDocente.Add((esSuDocente ? "reportaste " : "reportó ") + (delProfesor == 1 ? "1 falla" : delProfesor + " fallas"));
            }

            if (dadas > 0)
            {
                delDocente.Add((esSuDocente ? "diste " : "dio ") + (dadas == 1 ? "1 asistencia" : dadas + " asistencias") + " sin bitácora");
            }

            string loDelDocente = string.Join(" y ", delDocente);
            lblCantidad.Text = "Registros: " + alumnos.Rows.Count
                + (alumnos.Rows.Count == 0 ? ""
                    : conFalla == 0 ? ". Nadie reportó fallas en su bitácora"
                    : conFalla == 1 ? ". 1 estudiante reportó fallas" : ". " + conFalla + " estudiantes reportaron fallas")
                + (sinInternet.Length > 0 ? ". " + sinInternet : "")
                + (delDocente.Count == 0 ? ""
                    : esSuDocente ? ". " + char.ToUpper(loDelDocente[0]) + loDelDocente.Substring(1) : ". Su docente " + loDelDocente)
                + ".";
            PonerAyudaDeLaCantidad(alumnos);
            btnDescargar.Enabled = true;
            MostrarTiempoRestante();
        }

        /// <summary>
        /// La franja con lo que reporto el profesor: un renglon por reporte
        /// (ReporteProfesor.Renglon), o sin franja si no reporto nada. Solo se
        /// rehace si cambio lo que dice: el refresco de cada 10 s no la mueve.
        /// </summary>
        private void PonerReportes(List<ReporteProfesor> reportes)
        {
            string renglones = string.Join(Environment.NewLine, reportes.Select(r => r.Renglon));
            if (renglones == renglonesPuestos)
            {
                return;
            }

            renglonesPuestos = renglones;
            reportesPuestos = reportes.Count;
            lblReportes.Text = renglones;
            pnlReportes.AccessibleName = renglones.Length == 0 ? null : lblReportesTitulo.Text + " " + renglones.Replace(Environment.NewLine, " ");
            AcomodarReportes(false);
        }

        /// <summary>
        /// La ayuda de la linea de abajo cuando su docente dio asistencias sin
        /// bitacora: la linea entera y, debajo, a quien, a que hora y por que (como
        /// mucho 15; el resto, en el PDF). Sin ninguna, y si nunca las hubo, nada:
        /// la Label sigue ensenando sola su texto entero si no cabe.
        /// </summary>
        private void PonerAyudaDeLaCantidad(DataTable alumnos)
        {
            if (dadasPuestas.Count == 0 && !conAyudaCantidad)
            {
                return;
            }

            conAyudaCantidad = true;
            string ayuda = lblCantidad.Text;
            if (dadasPuestas.Count > 0)
            {
                HashSet<string> conBitacora = new HashSet<string>(alumnos.Rows.Cast<DataRow>().Select(f => f["fk_usuario"].ToString()));
                const int Maximo = 15;
                ayuda += Environment.NewLine + Environment.NewLine
                    + (EsSuDocente ? "Asistencias que diste sin bitácora" : "Asistencias que dio su docente sin bitácora")
                    + " (cuentan en la lista, pero no son bitácoras):";
                foreach (AsistenciaDada dada in dadasPuestas.Take(Maximo))
                {
                    string detalle = dada.Motivo == AsistenciasDadas.Otro ? "" : (dada.Comentario ?? "").Trim();
                    ayuda += Environment.NewLine + dada.DadaEn.ToString("HH:mm", CultureInfo.InvariantCulture) + "  "
                        + (string.IsNullOrWhiteSpace(dada.NombreLista) ? dada.Nombre : dada.NombreLista) + ": "
                        + AsistenciasDadas.EnFrase(dada)
                        + (string.IsNullOrWhiteSpace(dada.Equipo) ? "" : " (" + dada.Equipo + ")")
                        + (detalle.Length > 0 ? ". " + detalle : "")
                        + (conBitacora.Contains(dada.AlumnoId.ToString(CultureInfo.InvariantCulture)) ? ". Después llegó su bitácora" : "")
                        + ".";
                }

                if (dadasPuestas.Count > Maximo)
                {
                    ayuda += Environment.NewLine + "... y " + (dadasPuestas.Count - Maximo) + " más (todas, en el PDF).";
                }
            }

            if (ayuda != ayudaCantidadPuesta)
            {
                ayudaCantidadPuesta = ayuda;
                ayudaCantidad.SetToolTip(lblCantidad, ayuda);
            }
        }

        /// <summary>
        /// La franja, del ancho de la tabla en 100: 4 px de barra naranja, 1 de
        /// borde y, dentro, "Reportado por el profesor:" y a su derecha los
        /// renglones (partidos si no caben). Mide lo que su texto, hasta un 40 % de
        /// lo que queda para la tabla; si no cabe, se desplaza. La tabla empieza 8
        /// px debajo de ella (sin franja, en 102) y acaba 60 px antes del borde.
        /// Con mantener (al cambiar el tamano), lo desplazado se queda donde estaba.
        /// </summary>
        private void AcomodarReportes(bool mantener)
        {
            Size cliente = ClientSize;
            if (pnlReportes == null || dgvAlumnos == null || cliente.Width < 100 || cliente.Height < 100)
            {
                return;
            }

            int ancho = cliente.Width - 48;
            int arriba = ArribaDeLaTabla;
            bool conFranja = renglonesPuestos.Length > 0;
            if (conFranja)
            {
                Point desplazado = mantener ? pnlRenglones.AutoScrollPosition : Point.Empty;
                // Los hijos de un panel desplazado se colocan respecto a lo que se ve: primero, arriba del todo.
                pnlRenglones.AutoScrollPosition = Point.Empty;
                int dentro = ancho - BarraFranja - 2;
                int xRenglones = 10 + lblReportesTitulo.PreferredSize.Width + 8;
                int anchoRenglones = Math.Max(80, dentro - xRenglones - 10);
                int maximo = Math.Max(48, (cliente.Height - ArribaDeLaFranja - DebajoDeLaTabla - 8) * 2 / 5);
                if (AltoDeLaFranja(anchoRenglones) > maximo)
                {
                    // Con la barra de desplazamiento, los renglones tienen menos ancho.
                    anchoRenglones = Math.Max(80, anchoRenglones - SystemInformation.VerticalScrollBarWidth);
                }

                int altoFranja = Math.Min(AltoDeLaFranja(anchoRenglones), maximo);
                pnlReportes.SetBounds(24, ArribaDeLaFranja, ancho, altoFranja);
                pnlRenglones.SetBounds(BarraFranja + 1, 1, dentro, altoFranja - 2);
                pnlRenglones.AutoScrollMargin = new Size(0, RellenoFranja);
                lblReportesTitulo.Location = new Point(10, RellenoFranja);
                lblReportes.SetBounds(xRenglones, RellenoFranja, anchoRenglones, AltoDeLosRenglones(anchoRenglones));
                if (mantener && desplazado != Point.Empty)
                {
                    pnlRenglones.AutoScrollPosition = new Point(-desplazado.X, -desplazado.Y);
                }

                arriba = pnlReportes.Bottom + 8;
            }

            pnlReportes.Visible = conFranja;
            dgvAlumnos.SetBounds(24, arriba, ancho, Math.Max(40, cliente.Height - DebajoDeLaTabla - arriba));
        }

        /// <summary>Lo que mide la franja con los renglones partidos a ese ancho (con su borde de 1 px arriba y abajo).</summary>
        private int AltoDeLaFranja(int anchoRenglones)
        {
            int texto = Math.Max(lblReportesTitulo.PreferredSize.Height, AltoDeLosRenglones(anchoRenglones));
            return 1 + RellenoFranja + texto + RellenoFranja + 1;
        }

        /// <summary>El alto de los renglones partidos a ese ancho, como los pinta su Label.</summary>
        private int AltoDeLosRenglones(int anchoRenglones)
        {
            return lblReportes.GetPreferredSize(new Size(anchoRenglones, 0)).Height;
        }

        /// <summary>El marco de la franja: la barra naranja de la izquierda y un borde de 1 px del mismo naranja.</summary>
        private void PintarMarcoReportes(object sender, PaintEventArgs e)
        {
            Rectangle r = pnlReportes.ClientRectangle;
            using (SolidBrush barra = new SolidBrush(ColorFallaCelda))
            using (Pen borde = new Pen(ColorFallaCelda))
            {
                e.Graphics.FillRectangle(barra, 0, 0, BarraFranja + 1, r.Height);
                e.Graphics.DrawRectangle(borde, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        /// <summary>
        /// La fila que estaba elegida (por su matricula), la primera fila que se
        /// veia y el orden por columna: al volver a llenar la tabla (cada 10 s
        /// mientras dura la clase) se devuelven, y nada salta.
        /// </summary>
        private sealed class VistaDeLaTabla
        {
            private string matricula;
            private int primeraFila = -1;
            private string columnaOrden;
            private SortOrder sentido;

            public static VistaDeLaTabla Tomar(DataGridView tabla)
            {
                VistaDeLaTabla vista = new VistaDeLaTabla
                {
                    primeraFila = tabla.FirstDisplayedScrollingRowIndex,
                    columnaOrden = tabla.SortedColumn == null ? null : tabla.SortedColumn.Name,
                    sentido = tabla.SortOrder
                };
                DataRowView fila = tabla.SelectedRows.Count > 0 ? tabla.SelectedRows[0].DataBoundItem as DataRowView : null;
                vista.matricula = fila == null ? null : Convert.ToString(fila.Row["numero_identificador"]);
                return vista;
            }

            public void Devolver(DataGridView tabla)
            {
                if (columnaOrden != null && sentido != SortOrder.None && tabla.Columns.Contains(columnaOrden))
                {
                    tabla.Sort(tabla.Columns[columnaOrden], sentido == SortOrder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending);
                }

                if (matricula != null)
                {
                    DataGridViewRow elegida = tabla.Rows.Cast<DataGridViewRow>().FirstOrDefault(f =>
                        f.DataBoundItem is DataRowView && Convert.ToString(((DataRowView)f.DataBoundItem).Row["numero_identificador"]) == matricula);
                    DataGridViewColumn primera = tabla.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
                    if (elegida != null && primera != null)
                    {
                        // Poner la celda actual lleva la tabla hasta ella; lo que se veia se devuelve despues.
                        tabla.CurrentCell = elegida.Cells[primera.Index];
                        tabla.ClearSelection();
                        elegida.Selected = true;
                    }
                }

                if (primeraFila >= 0 && primeraFila < tabla.Rows.Count && tabla.FirstDisplayedScrollingRowIndex != primeraFila)
                {
                    try
                    {
                        tabla.FirstDisplayedScrollingRowIndex = primeraFila;
                    }
                    catch (InvalidOperationException)
                    {
                        // Sin alto para mostrar filas (minimizada): se queda arriba.
                    }
                }
            }
        }

        // Las columnas que TablaParaVer anade a lo que lee la base.
        private const string ColEstudiante = "estudiante";
        private const string ColReporte = "reporte";
        private const string ColDada = "es_dada";

        /// <summary>
        /// Lo que ensena la tabla: cada bitacora, con el nombre como la lista oficial
        /// ("Arias Mora Gisela") y lo que reporto en una sola columna (DescribirFallas,
        /// como Mis bitacoras), y al final una fila por cada asistencia que dio su
        /// docente: la hora, el equipo y "Se la diste: ...". Es una copia: alumnos se
        /// queda como la lee la base, para las cuentas de abajo (y el PDF lee la suya).
        /// </summary>
        private static DataTable TablaParaVer(DataTable alumnos, List<AsistenciaDada> dadas)
        {
            DataTable tabla = alumnos.Copy();
            tabla.Columns.Add(ColEstudiante, typeof(string));
            tabla.Columns.Add(ColReporte, typeof(string));
            tabla.Columns.Add(ColDada, typeof(bool));
            foreach (DataRow fila in tabla.Rows)
            {
                string nombre = UsuarioInfo.ComoEnLaLista(Convert.ToString(fila["nombre"], CultureInfo.InvariantCulture),
                    Convert.ToString(fila["apellido_paterno"], CultureInfo.InvariantCulture), Convert.ToString(fila["apellido_materno"], CultureInfo.InvariantCulture));
                fila[ColEstudiante] = Convert.ToInt32(fila["sin_conexion"], CultureInfo.InvariantCulture) == 1 ? nombre + " (sin conexión)" : nombre;
                fila[ColReporte] = Consultas.DescribirFallas(fila);
                fila[ColDada] = false;
            }

            // A quien le habla: el docente de la clase la dio; la administracion y la
            // persona responsable leen lo que hizo su docente.
            string quien = EsSuDocente ? "Se la diste: " : "Se la dio su docente: ";
            HashSet<string> conBitacora = new HashSet<string>(alumnos.Rows.Cast<DataRow>().Select(f => Convert.ToString(f["fk_usuario"], CultureInfo.InvariantCulture)));
            foreach (AsistenciaDada dada in dadas)
            {
                DataRow fila = tabla.NewRow();
                foreach (DataColumn columna in tabla.Columns)
                {
                    if (columna.DataType == typeof(string))
                    {
                        fila[columna] = "";
                    }
                }

                fila["con_falla"] = 0;
                fila["sin_conexion"] = 0;
                fila["numero_identificador"] = dada.Matricula ?? "";
                fila["hora_registro"] = dada.DadaEn.ToString("HH:mm", CultureInfo.InvariantCulture);
                fila["numero_computadora"] = string.IsNullOrWhiteSpace(dada.Equipo) ? "—" : dada.Equipo.Trim();
                fila[ColEstudiante] = string.IsNullOrWhiteSpace(dada.NombreLista) ? dada.Nombre : dada.NombreLista;
                fila[ColReporte] = quien + AsistenciasDadas.EnFrase(dada)
                    + (conBitacora.Contains(dada.AlumnoId.ToString(CultureInfo.InvariantCulture)) ? ". Después llegó su bitácora" : "");
                fila[ColDada] = true;
                tabla.Rows.Add(fila);
            }

            return tabla;
        }

        private static bool EsDada(DataRow fila)
        {
            return fila.Table.Columns.Contains(ColDada) && fila[ColDada] is bool && (bool)fila[ColDada];
        }

        private void ConfigurarColumnas()
        {
            if (dgvAlumnos.Columns.Count == 0)
            {
                return;
            }

            // Se ve: Estudiante, Matricula, Hora, Equipo y Reporte. Lo reportado va en
            // una sola columna (antes, una por area, casi siempre vacias, y "Sin internet").
            foreach (DataGridViewColumn columna in dgvAlumnos.Columns)
            {
                columna.Visible = columna.Name == ColEstudiante || columna.Name == "numero_identificador" || columna.Name == "hora_registro"
                    || columna.Name == "numero_computadora" || columna.Name == ColReporte;
            }

            dgvAlumnos.Columns[ColEstudiante].HeaderText = "Estudiante";
            dgvAlumnos.Columns["numero_identificador"].HeaderText = "Matrícula";
            // La hora de su primer registro: con clases seguidas, dice cuando llego.
            dgvAlumnos.Columns["hora_registro"].HeaderText = "Hora";
            dgvAlumnos.Columns["numero_computadora"].HeaderText = "Equipo";
            DataGridViewColumn reporte = dgvAlumnos.Columns[ColReporte];
            reporte.HeaderText = "Reporte";
            reporte.ToolTipText = "Lo que reportó en su bitácora (red, hardware, software) y los equipos que avisó sin internet.";

            dgvAlumnos.Columns[ColEstudiante].DisplayIndex = 0;
            dgvAlumnos.Columns["numero_identificador"].DisplayIndex = 1;
            dgvAlumnos.Columns["hora_registro"].DisplayIndex = 2;
            dgvAlumnos.Columns["numero_computadora"].DisplayIndex = 3;
            // La ultima: entre las ocultas, un numero fijo la dejaba antes del Equipo.
            reporte.DisplayIndex = dgvAlumnos.Columns.Count - 1;
            dgvAlumnos.Columns[ColEstudiante].FillWeight = 28;
            dgvAlumnos.Columns["numero_identificador"].FillWeight = 13;
            dgvAlumnos.Columns["hora_registro"].FillWeight = 7;
            dgvAlumnos.Columns["numero_computadora"].FillWeight = 10;
            reporte.FillWeight = 42;
            // La lista los recuerda (si los cambia sola al quedar angosta, los vuelve a poner).
            ListaDigLab lista = dgvAlumnos as ListaDigLab;
            if (lista != null)
            {
                lista.RecordarPesos();
            }
        }

        /// <summary>
        /// Como Mis bitacoras: la fila suave si reporto algo y el Reporte en naranja
        /// intenso; "Sin novedad" en gris; solo el aviso de otros equipos sin internet,
        /// en naranja. La asistencia que dio su docente, en verde claro.
        /// </summary>
        private void dgvAlumnos_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            DataRowView vista = dgvAlumnos.Rows[e.RowIndex].DataBoundItem as DataRowView;
            if (vista == null)
            {
                return;
            }

            string columna = dgvAlumnos.Columns[e.ColumnIndex].Name;
            if (EsDada(vista.Row))
            {
                e.CellStyle.BackColor = ColorDada;
                e.CellStyle.SelectionBackColor = ColorDadaElegida;
                if (columna == ColReporte)
                {
                    e.CellStyle.ForeColor = TextoDada;
                }

                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                return;
            }

            if (!Consultas.ConFalla(vista.Row))
            {
                if (columna == ColReporte)
                {
                    e.CellStyle.ForeColor = Consultas.AvisoSinInternet(vista.Row).Length > 0 ? TextoSinInternet : Color.Gray;
                    e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                }
                else if (columna == "hora_registro" && Convert.ToString(e.Value, CultureInfo.InvariantCulture) == "—")
                {
                    e.CellStyle.ForeColor = Color.Gray;
                }

                return;
            }

            bool esLaFalla = columna == ColReporte;
            e.CellStyle.BackColor = esLaFalla ? ColorFallaCelda : ColorConFalla;
            // Elegida, su naranja mas intenso (no el verde: se perderia la falla) y su letra de siempre.
            e.CellStyle.SelectionBackColor = esLaFalla ? ColorFallaCeldaElegida : ColorConFallaElegida;
            e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
            if (esLaFalla)
            {
                e.CellStyle.Font = fuenteFalla;
            }
        }

        private void Temporizador_Tick(object sender, EventArgs e)
        {
            if (temporizador == null || IsDisposed || Disposing)
            {
                return;
            }

            MostrarTiempoRestante();

            // La lista se refresca cada 10 s, no cada segundo: es una consulta y
            // una reconstruccion completa de la tabla.
            ticks++;
            if (ticks % SegundosEntreRefrescos == 0)
            {
                Refrescar(true);
            }
        }

        private void MostrarTiempoRestante()
        {
            if (ventana == null || sinLeer)
            {
                return;
            }

            TimeSpan restante = finEnRelojLocal - DateTime.Now;
            lblRestante.Text = restante <= TimeSpan.Zero
                ? "La clase terminó a las " + ventana.Fin.ToString("HH:mm") + "."
                : "Termina a las " + ventana.Fin.ToString("HH:mm") + " (quedan "
                  + (int)restante.TotalHours + restante.ToString("\\:mm\\:ss") + ").";
        }

        // =====================================================================
        // PDF
        // =====================================================================

        private void DescargarPdf()
        {
            if (idSesion == 0)
            {
                MessageBox.Show(this, "Esta clase todavía no tiene registros.", "Sin registros", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Sin conexion (u otro fallo al leer), la causa y que hacer: antes salia
            // la pantalla de error de la aplicacion.
            EncabezadoPdfSesion datosPdf;
            try
            {
                datosPdf = consultas.ConsultarDatosPdfSesion(idSesion);
            }
            catch (Exception ex)
            {
                Log.Error("BitacoraSesion.DescargarPdf (datos de la sesion " + idSesion + ")", ex);
                MessageBox.Show(this, MensajeDeFallo.Explicar("No se pudo preparar el PDF", ex, AvisoDeFalloDocente.SiSigue), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (datosPdf == null)
            {
                MessageBox.Show(this, "No se pudo preparar el PDF: el servidor ya no tiene los datos de esta sesión (quizá se quitó mientras tanto). "
                    + "Cierra esta ventana y vuelve a abrir la clase; si sigue, " + AvisoDeFalloDocente.SiSigue + ".", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Como en Bitacoras: "codigo_fecha_HHmm_laboratorio_profesor.pdf". El codigo
            // de la sesion es lo que teclean los alumnos: el encargado de laboratorio
            // no lo ve, tampoco en el nombre del archivo (Permisos).
            string nombreArchivo = ventana == null
                ? PdfBitacora.NombreArchivo(nombreProfesor)
                : PdfBitacora.NombreArchivo(
                    Permisos.VeCodigosDeProfesor ? ventana.Codigo : "",
                    ventana.Inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ventana.Inicio.ToString("HHmm", CultureInfo.InvariantCulture),
                    ventana.Laboratorio,
                    nombreProfesor);

            // Primero se ve como saldra (VisorPdf); desde ahi se elige donde guardarlo.
            byte[] pdf;
            Cursor = Cursors.WaitCursor;
            try
            {
                using (MemoryStream memoria = new MemoryStream())
                {
                    GenerarPdf(datosPdf, memoria);
                    pdf = memoria.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log.Error("BitacoraSesion.DescargarPdf", ex);
                MessageBox.Show(this, MensajeDeFallo.Explicar("No se pudo preparar el PDF", ex, AvisoDeFalloDocente.SiSigue), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            VisorPdf.Mostrar(this, pdf, nombreArchivo);
        }

        /// <summary>
        /// El PDF de la sesion en destino: el mismo de Bitacoras (PdfBitacora), con
        /// el profesor, el grupo, la materia y el laboratorio de la sesion.
        /// </summary>
        private void GenerarPdf(EncabezadoPdfSesion datosPdf, Stream destino)
        {
            // Sin profesor guardado en la sesion (no deberia pasar con una clase): el de la clase que se abrio.
            if (string.IsNullOrWhiteSpace(datosPdf.Profesor))
            {
                datosPdf.Profesor = nombreProfesor;
            }

            // Con lo que reporto su docente (db/15) y las asistencias que dio (db/22), cada
            // uno en su propia tabla; sin las dos, el de siempre.
            PdfBitacora.Escribir(datosPdf, consultas.consultaRegistroSesion(idSesion), lectorDadas.DeLaSesion(idSesion),
                new ReportesProfesorDatos().DeLaSesion(idSesion), destino);
        }
    }
}
