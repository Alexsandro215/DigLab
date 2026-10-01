using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigBit
{
    /// <summary>
    /// Panel del profesor. Arriba, su codigo de clase en grande (el que dicta a
    /// sus alumnos; uno para todas sus clases) y su clase de ahora o la proxima,
    /// con cuantos registraron bitacora; abajo Mi horario, Mis bitacoras y Mi
    /// perfil. Sustituye al menu de cuatro botones (Mis clases, Ver mis codigos,
    /// Bitacora de mis clases y Editar perfil).
    ///
    /// Como el panel del administrador: la hora es la del servidor, los datos se
    /// piden en segundo plano cada 30 s y el reloj se recalcula cada segundo.
    /// </summary>
    public class Profesor_Principal : Form
    {
        private const int SegundosEntreActualizaciones = 30;
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");
        private static readonly Color TextoSecundario = Color.FromArgb(90, 90, 90);

        private readonly Container componentes = new Container();
        private readonly Dictionary<string, Form> ventanas = new Dictionary<string, Form>();
        private readonly string numeroEmpleado;

        private Panel cabecera;
        private Label lblSaludo;
        private Label lblReloj;
        private Label lblFecha;
        private Button btnCerrarSesion;
        private TarjetaCodigo tarjetaCodigo;
        private TarjetaAhora tarjetaAhora;
        private Label lblEstado;
        private Timer relojTimer;
        private Timer datosTimer;

        private PanoramaProfesor panorama;
        private TimeSpan desfaseServidor = TimeSpan.Zero;
        private bool actualizando;
        private DateTime? sinConexionDesde;

        public Profesor_Principal()
        {
            numeroEmpleado = Datos_User.getUser() ?? "";
            InicializarComponentes();

            if (!Kiosco.Activo)
            {
                WindowState = FormWindowState.Maximized;
            }

            Kiosco.Aplicar(this);
            if (AppContexto.Actual != null)
            {
                AppContexto.Actual.Registrar(this);
            }

            ActualizarReloj();
        }

        /// <summary>La hora del servidor, estimada con el desfase de la ultima lectura.</summary>
        private DateTime Ahora()
        {
            return DateTime.Now + desfaseServidor;
        }

        // =====================================================================
        // Construccion de la interfaz
        // =====================================================================

        private void InicializarComponentes()
        {
            // Los logos y el icono son los del panel del administrador.
            ComponentResourceManager recursos = new ComponentResourceManager(typeof(PrincipalAdministrador));

            BackColor = Color.White;
            ClientSize = new Size(1180, 740);
            MinimumSize = new Size(1000, 680);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = (Icon)recursos.GetObject("$this.Icon");
            Text = "DigBit - Profesor";

            TableLayoutPanel raiz = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(20, 8, 20, 16),
                BackColor = Color.White
            };
            raiz.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));

            raiz.Controls.Add(CrearCabecera(recursos), 0, 0);
            raiz.Controls.Add(CrearTarjetas(), 0, 1);

            lblEstado = new Label
            {
                Dock = DockStyle.Fill,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.FromArgb(110, 110, 110),
                TextAlign = ContentAlignment.MiddleRight,
                Text = "Cargando..."
            };
            raiz.Controls.Add(lblEstado, 0, 2);
            raiz.Controls.Add(CrearSecciones(), 0, 3);

            Controls.Add(raiz);

            relojTimer = new Timer(componentes) { Interval = 1000 };
            relojTimer.Tick += (s, e) => ActualizarReloj();
            datosTimer = new Timer(componentes) { Interval = SegundosEntreActualizaciones * 1000 };
            datosTimer.Tick += (s, e) => ActualizarDatos();
        }

        private Control CrearCabecera(ComponentResourceManager recursos)
        {
            cabecera = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };

            PictureBox logos = new PictureBox
            {
                Image = (Image)recursos.GetObject("Logos"),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(0, 16),
                Size = new Size(230, 48)
            };

            lblSaludo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(250, 12),
                Size = new Size(600, 34),
                Text = "Hola"
            };

            Label subtitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                Location = new Point(253, 48),
                Text = "Panel del profesor. Número de empleado: " + (numeroEmpleado.Length > 0 ? numeroEmpleado : "-")
            };

            lblReloj = new Label
            {
                AutoSize = false,
                Size = new Size(260, 44),
                Font = new DrawingFont("Century Gothic", 26F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                TextAlign = ContentAlignment.MiddleRight
            };

            lblFecha = new Label
            {
                AutoSize = false,
                Size = new Size(320, 22),
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                TextAlign = ContentAlignment.MiddleRight
            };

            btnCerrarSesion = EstiloHorarios.CrearBoton("Cerrar sesión", Point.Empty, new Size(150, 40));
            // Salir de la sesion, no de la aplicacion (DigBit puede ser el shell).
            btnCerrarSesion.Click += (s, e) => AppContexto.Actual.CerrarSesion();

            cabecera.Controls.AddRange(new Control[] { logos, lblSaludo, subtitulo, lblReloj, lblFecha, btnCerrarSesion });
            cabecera.Resize += (s, e) => ColocarCabecera();
            cabecera.Paint += (s, e) =>
            {
                using (Pen linea = new Pen(Color.FromArgb(215, 222, 215)))
                {
                    e.Graphics.DrawLine(linea, 0, cabecera.Height - 1, cabecera.Width, cabecera.Height - 1);
                }
            };
            return cabecera;
        }

        private void ColocarCabecera()
        {
            int derecha = cabecera.ClientSize.Width;
            btnCerrarSesion.Location = new Point(derecha - btnCerrarSesion.Width, 20);
            int finReloj = btnCerrarSesion.Left - 24;
            lblReloj.Location = new Point(finReloj - lblReloj.Width, 4);
            lblFecha.Location = new Point(finReloj - lblFecha.Width, 50);
            // El saludo llega hasta la fecha, sin taparla.
            lblSaludo.Width = Math.Max(200, lblFecha.Left - 16 - lblSaludo.Left);
        }

        private Control CrearTarjetas()
        {
            TableLayoutPanel fila = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 8, 0, 0)
            };
            fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            fila.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Sin margen: cada tarjeta deja el suyo para el halo animado.
            tarjetaCodigo = new TarjetaCodigo { Dock = DockStyle.Fill, Margin = new Padding(0) };
            tarjetaAhora = new TarjetaAhora { Dock = DockStyle.Fill, Margin = new Padding(0) };
            tarjetaAhora.VerRegistro += VerRegistro;
            fila.Controls.Add(tarjetaCodigo, 0, 0);
            fila.Controls.Add(tarjetaAhora, 1, 0);
            return fila;
        }

        private Control CrearSecciones()
        {
            TableLayoutPanel fila = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(Resplandor.Margen, 6, Resplandor.Margen, 0)
            };
            for (int i = 0; i < 3; i++)
            {
                fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            }

            fila.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            AgregarSeccion(fila, 0, "Mi horario", "Tu semana, con las clases canceladas y las sesiones extra. Pulsa una clase para ver su bitácora.", AbrirHorario);
            AgregarSeccion(fila, 1, "Mis bitácoras", "Todas tus sesiones: quién registró, qué reportó y su PDF.", AbrirBitacoras);
            AgregarSeccion(fila, 2, "Mi perfil", "Tu nombre y tu correo.", AbrirPerfil);
            return fila;
        }

        private static void AgregarSeccion(TableLayoutPanel fila, int columna, string titulo, string descripcion, Action abrir)
        {
            TarjetaOpcion tarjeta = new TarjetaOpcion(titulo, descripcion)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(columna == 0 ? 0 : 6, 0, columna == 2 ? 0 : 6, 0)
            };
            tarjeta.Click += (s, e) => abrir();
            fila.Controls.Add(tarjeta, columna, 0);
        }

        // =====================================================================
        // Secciones
        // =====================================================================

        private void AbrirHorario()
        {
            Mostrar("horario", () => new BitacoraMisClases());
        }

        private void AbrirBitacoras()
        {
            int idProfesor = panorama == null ? 0 : panorama.ProfesorId;
            if (idProfesor == 0)
            {
                try
                {
                    idProfesor = new HorariosDatos().PanoramaDeProfesor(numeroEmpleado).ProfesorId;
                }
                catch (Exception ex)
                {
                    Log.Error("Abrir Mis bitacoras de " + numeroEmpleado, ex);
                    MessageBox.Show(this, "No se pudieron leer tus datos: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            Mostrar("bitacoras", () => new GestionBitacorasAdministrador(idProfesor));
        }

        private void AbrirPerfil()
        {
            Mostrar("perfil", () => new editar_Perfil("profesor"));
        }

        /// <summary>Quien registro en la clase de la tarjeta "Ahora" (la de hoy, aunque aun no haya nadie).</summary>
        private void VerRegistro(SesionAgenda sesion)
        {
            string laboratorio = panorama == null ? "" : panorama.Laboratorio(sesion.LaboratorioId);
            string horario = (sesion.EsExtra ? "Sesión extra de hoy, " : "Hoy, ") + HorariosDatos.Hora(sesion.Inicio) + " a " + HorariosDatos.Hora(sesion.Fin)
                + (laboratorio.Length > 0 ? ", " + laboratorio : "");
            DateTime hoy = panorama == null ? Ahora().Date : panorama.AhoraServidor.Date;
            using (BitacoraSesion bitacora = new BitacoraSesion(sesion.FranjaId, sesion.ExcepcionId,
                sesion.Materia + ", " + sesion.Grupo, horario, panorama == null ? "" : panorama.Nombre, hoy))
            {
                bitacora.ShowDialog(this);
            }

            ActualizarDatos();
        }

        /// <summary>
        /// Una ventana por seccion: si ya esta abierta se trae al frente. Al
        /// cerrarla se refresca el panel (pudo cambiar su nombre en el perfil).
        /// </summary>
        private Form Mostrar(string clave, Func<Form> crear)
        {
            Form ventana;
            if (!ventanas.TryGetValue(clave, out ventana) || ventana.IsDisposed)
            {
                ventana = crear();
                ventana.FormClosed += (s, e) => ActualizarDatos();
                ventanas[clave] = ventana;
            }

            if (!ventana.Visible)
            {
                ventana.Show(this);
            }

            if (ventana.WindowState == FormWindowState.Minimized)
            {
                ventana.WindowState = FormWindowState.Normal;
            }

            ventana.Activate();
            return ventana;
        }

        // =====================================================================
        // Reloj y datos
        // =====================================================================

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            relojTimer.Start();
            datosTimer.Start();
            ActualizarDatos();
        }

        private void ActualizarReloj()
        {
            DateTime ahora = Ahora();
            lblReloj.Text = ahora.ToString("HH:mm:ss");
            string fecha = ahora.ToString("dddd d 'de' MMMM 'de' yyyy", Espanol);
            lblFecha.Text = char.ToUpper(fecha[0], Espanol) + fecha.Substring(1);
            tarjetaAhora.Ahora = ahora;

            // Paso la medianoche: las clases cargadas son las de ayer.
            if (panorama != null && panorama.AhoraServidor.Date != ahora.Date && !sinConexionDesde.HasValue)
            {
                ActualizarDatos();
            }
        }

        /// <summary>Pide los datos en segundo plano: con el servidor caido, la ventana no se congela.</summary>
        private void ActualizarDatos()
        {
            if (actualizando || IsDisposed)
            {
                return;
            }

            actualizando = true;
            string numero = numeroEmpleado;
            Task.Run(() => new HorariosDatos().PanoramaDeProfesor(numero))
                .ContinueWith(tarea =>
                {
                    try
                    {
                        BeginInvoke(new Action(() => AplicarDatos(tarea)));
                    }
                    catch (InvalidOperationException)
                    {
                        // El formulario se cerro mientras se consultaba.
                    }
                });
        }

        private void AplicarDatos(Task<PanoramaProfesor> tarea)
        {
            actualizando = false;
            if (IsDisposed)
            {
                return;
            }

            if (tarea.IsFaulted)
            {
                Exception error = tarea.Exception.GetBaseException();
                if (!sinConexionDesde.HasValue)
                {
                    sinConexionDesde = Ahora();
                    Log.Aviso("Panel del profesor " + numeroEmpleado + ": no se pudieron leer sus datos: " + error.Message);
                }

                lblEstado.ForeColor = Color.FromArgb(190, 85, 0);
                lblEstado.Text = error is InvalidOperationException
                    ? error.Message
                    : "Sin conexión con el servidor desde las " + sinConexionDesde.Value.ToString("HH:mm")
                      + ". Se reintenta cada " + SegundosEntreActualizaciones + " s; lo que ves puede no estar al día.";
                return;
            }

            if (sinConexionDesde.HasValue)
            {
                Log.Info("Panel del profesor: conexion recuperada.");
                sinConexionDesde = null;
            }

            panorama = tarea.Result;
            desfaseServidor = panorama.AhoraServidor - DateTime.Now;
            lblSaludo.Text = "Hola, " + panorama.Nombre;
            tarjetaCodigo.Mostrar(panorama);
            tarjetaAhora.Mostrar(panorama);
            ActualizarReloj();
            lblEstado.ForeColor = Color.FromArgb(110, 110, 110);
            lblEstado.Text = "Hora del servidor. Actualizado a las " + Ahora().ToString("HH:mm:ss")
                + "; se renueva solo cada " + SegundosEntreActualizaciones + " s";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            relojTimer.Stop();
            datosTimer.Stop();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                componentes.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
