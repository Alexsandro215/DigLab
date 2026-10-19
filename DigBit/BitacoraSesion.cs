using DigBit.conexion;
using DigBit.Infraestructura;
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

namespace DigBit
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
    /// </summary>
    public class BitacoraSesion : Form
    {
        // Mismos tonos que el resto: claro para la bitacora que reporta algo,
        // intenso para la celda con la falla.
        private static readonly Color ColorConFalla = Color.FromArgb(255, 240, 214);
        private static readonly Color ColorFallaCelda = Color.FromArgb(255, 176, 79);
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
        private readonly ToolTip ayudaBotones = new ToolTip();
        // La celda de la falla, en negrita con la letra de la tabla (no con la del formulario).
        private readonly DrawingFont fuenteFalla = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private DataGridView dgvAlumnos;
        private Label lblCantidad;
        private Panel pnlLeyenda;
        private Label lblLeyenda;
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
                Text = "Reportado por su docente:",
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

            dgvAlumnos = new DataGridView
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(24, ArribaDeLaTabla),
                Size = new Size(ancho - 48, alto - ArribaDeLaTabla - DebajoDeLaTabla),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false
            };
            dgvAlumnos.ColumnHeadersDefaultCellStyle.Font = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
            dgvAlumnos.ColumnHeadersDefaultCellStyle.ForeColor = Color.DarkGreen;
            dgvAlumnos.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 248, 240);
            dgvAlumnos.DefaultCellStyle.Font = new DrawingFont("Century Gothic", 9F);
            dgvAlumnos.DefaultCellStyle.SelectionBackColor = EstiloHorarios.FilaElegida;
            dgvAlumnos.DefaultCellStyle.SelectionForeColor = Color.Black;
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
                Size = new Size(ancho - 344 - 10 - 26, 20),
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
                Text = "Naranja: la falla que se reportó en la bitácora"
            };

            btnActualizar = CrearBoton("Actualizar", new Point(ancho - 344, alto - 44), new Size(100, 34));
            btnActualizar.Click += (s, e) => Refrescar();
            ayudaBotones.SetToolTip(btnActualizar, "Vuelve a leer quién registró (F5).");

            btnDescargar = CrearBoton("Descargar PDF", new Point(ancho - 234, alto - 44), new Size(130, 34));
            btnDescargar.Click += (s, e) => DescargarPdf();

            btnCerrar = CrearBoton("Cerrar", new Point(ancho - 94, alto - 44), new Size(70, 34));
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
            Controls.Add(btnActualizar);
            Controls.Add(btnDescargar);
            Controls.Add(btnCerrar);

            CancelButton = btnCerrar;
            // F5 actualiza desde cualquier sitio de la ventana (OnKeyDown), como en Mis grupos y Bitacoras.
            KeyPreview = true;

            temporizador = new Timer { Interval = 1000 };
            temporizador.Tick += Temporizador_Tick;
            temporizador.Start();
        }

        private Button CrearBoton(string texto, Point posicion, Size tamaño)
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
                PonerReportes(new List<ReporteProfesor>());
                lblRestante.Text = "Todavía nadie ha registrado su bitácora en esta clase.";
                lblCantidad.Text = "";
                btnDescargar.Enabled = false;
                return;
            }

            DataTable alumnos = consultas.consultaRegistroSesion(idSesion);
            VistaDeLaTabla vista = VistaDeLaTabla.Tomar(dgvAlumnos);
            dgvAlumnos.DataSource = alumnos;
            ConfigurarColumnas();
            vista.Devolver(dgvAlumnos);

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
            // "Ningun alumno reporto fallas" y no "sin fallas": junto a lo que reporto el profesor
            // se contradiria. Sin registros (la sesion la abrio un reporte del profesor), nada
            // de los alumnos.
            lblCantidad.Text = "Registros: " + alumnos.Rows.Count
                + (alumnos.Rows.Count == 0 ? ""
                    : conFalla == 0 ? ". Nadie reportó fallas en su bitácora"
                    : conFalla == 1 ? ". 1 estudiante reportó fallas" : ". " + conFalla + " estudiantes reportaron fallas")
                + (sinInternet.Length > 0 ? ". " + sinInternet : "")
                + (delProfesor == 1 ? ". Su docente reportó 1 falla"
                    : delProfesor > 1 ? ". Su docente reportó " + delProfesor + " fallas" : "")
                + ".";
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

        private void ConfigurarColumnas()
        {
            if (dgvAlumnos.Columns.Count == 0)
            {
                return;
            }

            // Una columna por area en vez de dos (falla y comentario): la celda
            // muestra lo que el alumno escribio, o el tipo de falla si no dejo
            // comentario, y queda vacia si no reporto nada.
            foreach (string oculta in new[] { "fk_codigo_accesos", "fk_usuario", "nombre", "apellido_paterno", "apellido_materno", "con_falla", "sin_conexion" })
            {
                if (dgvAlumnos.Columns.Contains(oculta))
                {
                    dgvAlumnos.Columns[oculta].Visible = false;
                }
            }

            foreach (string area in Consultas.AreasDeFalla)
            {
                dgvAlumnos.Columns["falla_" + area].Visible = false;
            }

            dgvAlumnos.Columns["nombre_completo"].HeaderText = "Estudiante";
            dgvAlumnos.Columns["numero_identificador"].HeaderText = "Matrícula";
            dgvAlumnos.Columns["numero_computadora"].HeaderText = "Equipo";
            dgvAlumnos.Columns["comentarios_red"].HeaderText = "Red";
            dgvAlumnos.Columns["comentarios_hardware"].HeaderText = "Hardware";
            dgvAlumnos.Columns["comentarios_software"].HeaderText = "Software";

            // La hora de su primer registro: con clases seguidas, dice cuando llego.
            dgvAlumnos.Columns["hora_registro"].HeaderText = "Hora";

            // Otros equipos que vio sin internet (db/10): solo si alguien aviso.
            DataGridViewColumn avisos = dgvAlumnos.Columns["avisa_sin_internet"];
            avisos.HeaderText = "Sin internet";
            avisos.ToolTipText = "Otros equipos que avisó sin internet al registrar su bitácora.";
            avisos.FillWeight = 13;
            avisos.DefaultCellStyle.ForeColor = TextoSinInternet;
            avisos.DefaultCellStyle.SelectionForeColor = TextoSinInternet;
            avisos.Visible = dgvAlumnos.DataSource is DataTable
                && ((DataTable)dgvAlumnos.DataSource).Rows.Cast<DataRow>().Any(f => f["avisa_sin_internet"].ToString().Length > 0);

            dgvAlumnos.Columns["nombre_completo"].DisplayIndex = 0;
            dgvAlumnos.Columns["numero_identificador"].DisplayIndex = 1;
            dgvAlumnos.Columns["hora_registro"].DisplayIndex = 2;
            dgvAlumnos.Columns["numero_computadora"].DisplayIndex = 3;
            dgvAlumnos.Columns["nombre_completo"].FillWeight = 26;
            dgvAlumnos.Columns["numero_identificador"].FillWeight = 12;
            dgvAlumnos.Columns["hora_registro"].FillWeight = 7;
            dgvAlumnos.Columns["numero_computadora"].FillWeight = 10;
            foreach (string area in Consultas.AreasDeFalla)
            {
                dgvAlumnos.Columns["comentarios_" + area].FillWeight = 17;
            }
        }

        /// <summary>Fila suave si reporto algo y celda intensa en la falla concreta.</summary>
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
            string area;
            bool esColumnaDeArea = Consultas.EsColumnaDeFalla(columna, out area);
            if (esColumnaDeArea)
            {
                e.Value = Consultas.ConFallaEn(vista.Row, area) ? Consultas.DetalleDeFalla(vista.Row, area) : "";
                e.FormattingApplied = true;
            }
            else if (columna == "avisa_sin_internet" && e.Value != null)
            {
                e.Value = e.Value.ToString().Replace(",", ", ");
                e.FormattingApplied = true;
            }

            if (!Consultas.ConFalla(vista.Row))
            {
                return;
            }

            bool esLaFalla = esColumnaDeArea && Consultas.ConFallaEn(vista.Row, area);
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

            PdfBitacora.Escribir(datosPdf, consultas.consultaRegistroSesion(idSesion), destino);
        }
    }
}
