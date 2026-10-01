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
    /// Panel del administrador. Arriba, las cinco secciones (bitacoras, alumnos,
    /// profesores, laboratorios y horarios); debajo, con un reloj, lo que pasa en
    /// cada laboratorio en la hora anterior, la actual y la siguiente.
    ///
    /// La hora es la del servidor MySQL, la misma con la que los equipos del
    /// laboratorio deciden si aceptan un codigo: si el reloj de esta PC va
    /// adelantado, el panel no contradice lo que ven los alumnos. Los datos se
    /// piden en segundo plano cada 30 s; el reloj y el "ahora" se recalculan cada
    /// segundo sobre la ultima agenda, sin ir a la base.
    /// </summary>
    public class PrincipalAdministrador : Form
    {
        private const int SegundosEntreActualizaciones = 30;
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");
        private static readonly Color TextoSecundario = Color.FromArgb(90, 90, 90);

        private readonly Container componentes = new Container();

        // Una ventana por opcion: si ya esta abierta se trae al frente.
        private readonly Dictionary<string, Form> ventanas = new Dictionary<string, Form>();

        private Panel cabecera;
        private Label lblReloj;
        private Label lblFecha;
        private Button btnCerrarSesion;
        private Label lblEstado;
        private PanelLaboratoriosAhora panelLaboratorios;
        private Timer relojTimer;
        private Timer datosTimer;

        private TimeSpan desfaseServidor = TimeSpan.Zero;
        private bool actualizando;
        private DateTime? sinConexionDesde;

        public PrincipalAdministrador()
        {
            InicializarComponentes();

            // Maximizado caben mas laboratorios legibles en el panel. En kiosco ya
            // ocupa la pantalla entera y no se toca.
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
            ComponentResourceManager recursos = new ComponentResourceManager(typeof(PrincipalAdministrador));

            BackColor = Color.White;
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(1100, 700);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = (Icon)recursos.GetObject("$this.Icon");
            Text = "DigBit - Administrador";

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
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 122F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            raiz.Controls.Add(CrearCabecera(recursos), 0, 0);
            raiz.Controls.Add(CrearSecciones(), 0, 1);
            raiz.Controls.Add(CrearTituloPanel(), 0, 2);

            panelLaboratorios = new PanelLaboratoriosAhora { Dock = DockStyle.Fill, Margin = new Padding(0) };
            panelLaboratorios.LaboratorioElegido += AbrirHorarios;
            raiz.Controls.Add(panelLaboratorios, 0, 3);

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

            Label titulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(250, 12),
                Text = "Panel del administrador"
            };

            Label sesion = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                Location = new Point(253, 48),
                Text = "Sesión iniciada como " + (Datos_User.getUser() ?? "-")
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

            cabecera.Controls.AddRange(new Control[] { logos, titulo, sesion, lblReloj, lblFecha, btnCerrarSesion });
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
        }

        private Control CrearSecciones()
        {
            TableLayoutPanel fila = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0, 14, 0, 8)
            };
            for (int i = 0; i < 5; i++)
            {
                fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            }

            fila.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            AgregarSeccion(fila, 0, "Bitácoras", "Consultar, corregir y descargar las bitácoras de cada clase.", AbrirBitacoras);
            AgregarSeccion(fila, 1, "Alumnos", "Buscar, registrar y editar alumnos; dar de baja; grupos.", AbrirAlumnos);
            AgregarSeccion(fila, 2, "Profesores", "Buscar, registrar y editar profesores; dar de baja.", AbrirProfesores);
            AgregarSeccion(fila, 3, "Laboratorios", "Dar de alta, cambiar el nombre, ver su horario o quitarlos.", AbrirLaboratorios);
            AgregarSeccion(fila, 4, "Horarios", "Clases, horario semanal y excepciones por día.", () => AbrirHorarios(0));
            return fila;
        }

        private void AgregarSeccion(TableLayoutPanel fila, int columna, string titulo, string descripcion, Action abrir)
        {
            TarjetaOpcion tarjeta = new TarjetaOpcion(titulo, descripcion)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(columna == 0 ? 0 : 6, 0, columna == 4 ? 0 : 6, 0)
            };
            tarjeta.Click += (s, e) => abrir();
            fila.Controls.Add(tarjeta, columna, 0);
        }

        private Control CrearTituloPanel()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };

            Label titulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 13F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(0, 12),
                Text = "Los laboratorios ahora"
            };

            lblEstado = new Label
            {
                AutoSize = false,
                Size = new Size(560, 20),
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.FromArgb(110, 110, 110),
                TextAlign = ContentAlignment.MiddleRight,
                Text = "Cargando..."
            };

            panel.Controls.Add(titulo);
            panel.Controls.Add(lblEstado);
            panel.Resize += (s, e) => lblEstado.Location = new Point(panel.ClientSize.Width - lblEstado.Width, 16);
            return panel;
        }

        // =====================================================================
        // Secciones
        // =====================================================================

        private void AbrirBitacoras()
        {
            // Una sola pantalla para ver, corregir y descargar (antes eran tres botones).
            Mostrar("bitacoras", () => new GestionBitacorasAdministrador());
        }

        /// <summary>Horarios; con un laboratorio (clic en su columna del panel), directo a su horario semanal.</summary>
        private void AbrirHorarios(int laboratorio)
        {
            AdministrarHorarios abierta = Mostrar("horarios", () => new AdministrarHorarios(laboratorio)) as AdministrarHorarios;
            if (abierta != null && laboratorio != 0)
            {
                abierta.MostrarLaboratorio(laboratorio);
            }
        }

        private void AbrirAlumnos()
        {
            Mostrar("alumnos", () => new AdministrarAlumnos());
        }

        private void AbrirProfesores()
        {
            Mostrar("profesores", () => new AdministrarProfesores());
        }

        private void AbrirLaboratorios()
        {
            Mostrar("laboratorios", () =>
            {
                AdministrarLaboratorios laboratorios = new AdministrarLaboratorios();
                laboratorios.VerHorario += AbrirHorarios;
                return laboratorios;
            });
        }

        /// <summary>
        /// Una ventana por seccion: si ya esta abierta se trae al frente. Al
        /// cerrarla se refresca el panel, que puede haber cambiado (un laboratorio
        /// nuevo, una clase desactivada).
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

            // Ya abierta: solo se trae al frente. Show sobre una ventana visible
            // lanza InvalidOperationException (paso al pulsar Horarios con Horarios
            // ya abierto desde "Ver su horario").
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
            panelLaboratorios.Ahora = ahora;

            // Paso la medianoche: la agenda cargada es la de ayer.
            PanoramaLaboratorios panorama = panelLaboratorios.Panorama;
            if (panorama != null && panorama.Fecha != ahora.Date && !sinConexionDesde.HasValue)
            {
                ActualizarDatos();
            }
        }

        /// <summary>Pide la agenda en segundo plano: con el servidor caido, la ventana no se congela.</summary>
        private void ActualizarDatos()
        {
            if (actualizando || IsDisposed)
            {
                return;
            }

            actualizando = true;
            Task.Run(() => new HorariosDatos().PanoramaDelDia())
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

        private void AplicarDatos(Task<PanoramaLaboratorios> tarea)
        {
            actualizando = false;
            if (IsDisposed)
            {
                return;
            }

            if (tarea.IsFaulted)
            {
                if (!sinConexionDesde.HasValue)
                {
                    sinConexionDesde = Ahora();
                    Log.Aviso("Panel del administrador: no se pudo leer la agenda de los laboratorios: "
                        + tarea.Exception.GetBaseException().Message);
                }

                panelLaboratorios.Avisar("Sin conexión con el servidor desde las " + sinConexionDesde.Value.ToString("HH:mm")
                    + ". Se reintenta cada " + SegundosEntreActualizaciones + " s; lo que ves puede no estar al día.");
                lblEstado.Text = "Último intento: " + Ahora().ToString("HH:mm:ss") + " (sin conexión)";
                return;
            }

            if (sinConexionDesde.HasValue)
            {
                Log.Info("Panel del administrador: conexion recuperada.");
                sinConexionDesde = null;
            }

            PanoramaLaboratorios panorama = tarea.Result;
            desfaseServidor = panorama.AhoraServidor - DateTime.Now;
            panelLaboratorios.Mostrar(panorama);
            ActualizarReloj();
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
