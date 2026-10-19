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
    /// Panel del profesor. A la izquierda, su codigo de clase en grande (el que
    /// dicta a sus alumnos) con la clase de ahora o la proxima (materia, grupo,
    /// laboratorio y horario), con el borde latiendo mientras esta abierta; a la
    /// derecha sus clases en tres columnas (el ultimo dia con clases antes de hoy,
    /// hoy y el siguiente: ayer y manana si los tiene), de colores segun
    /// hubo o no bitacoras y con cuantas (TarjetaTresDias); abajo Mi horario, Mis
    /// bitacoras y Mi perfil. Sustituye al menu de cuatro botones (Mis clases, Ver
    /// mis codigos, Bitacora de mis clases y Editar perfil). Con db/19 cada clase
    /// lleva su circulo y su color (PersonalizacionClases): pulsar el circulo abre
    /// "Personalizar tu clase".
    ///
    /// Como el panel del administrador: la hora es la del servidor, los datos se
    /// piden en segundo plano cada 30 s y el reloj se recalcula cada segundo.
    /// En un equipo del laboratorio (kiosco), si nadie lo usa en 10 minutos y no
    /// esta en una de sus clases, se cierra su sesion (Inactividad).
    /// </summary>
    public class Profesor_Principal : Form
    {
        private const int SegundosEntreActualizaciones = 30;
        // Lo que dura en la linea de estado un aviso como "Tu reporte quedo guardado".
        private const int SegundosDelAviso = 10;
        // Kiosco: sin teclado ni raton tanto tiempo (fuera de sus clases), "Sigues ahi?".
        private static readonly TimeSpan EsperaSinUso = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan CuentaAtras = TimeSpan.FromSeconds(30);
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");
        private static readonly Color TextoSecundario = Color.FromArgb(90, 90, 90);
        private static readonly Color GrisEstado = Color.FromArgb(110, 110, 110);
        private static readonly Color NaranjaEstado = Color.FromArgb(190, 85, 0);
        private static readonly Color VerdeEstado = Color.FromArgb(46, 125, 50);

        private readonly Container componentes = new Container();
        private readonly Dictionary<string, Form> ventanas = new Dictionary<string, Form>();
        private readonly string numeroEmpleado;
        private ToolTip ayuda;
        private IDisposable vigilancia;
        // Un aviso en la linea de estado (verde, unos segundos): mientras dura, la
        // recarga de los datos no lo pisa; al terminar vuelve lo de siempre.
        private Timer avisoTimer;
        private bool conAviso;
        // El aviso va en negritas (la linea de estado es pequena): se ve aunque se mire a la tarjeta.
        private DrawingFont letraEstado;
        private DrawingFont letraAviso;
        private string estadoNormal = "Cargando...";
        private Color colorNormal = GrisEstado;
        private string ayudaNormal;
        // La lista de cada clase de hoy ("16 de 24 alumnos"); si una lectura falla, se queda la de antes.
        private Dictionary<int, ListaDeClase> listasDeHoy;
        private static bool listasAvisadas;

        private Panel cabecera;
        private Label lblSaludo;
        private Label lblSubtitulo;
        private int anchoSubtitulo;
        private int anchoFechaMax;
        private Label lblReloj;
        private Label lblFecha;
        private Button btnCerrarSesion;
        private TarjetaCodigo tarjetaCodigo;
        private TarjetaTresDias tarjetaDias;
        private Label lblEstado;
        private Timer relojTimer;
        private Timer datosTimer;

        private PanoramaProfesor panorama;
        private TimeSpan desfaseServidor = TimeSpan.Zero;
        private bool actualizando;
        // Generacion de codigos: las clases cuya apertura ya se pidio hoy al
        // entrar en su ventana (para pedirla una sola vez).
        private readonly HashSet<string> clasesPedidas = new HashSet<string>();
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

            // Lo que elige para sus clases (aqui o en Mis grupos) se ve al momento.
            PersonalizacionClases.Cambio += PersonalizacionCambio;
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
            Text = "DigBit - Docente";

            TableLayoutPanel raiz = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(20, 8, 20, 16),
                BackColor = Color.White
            };
            // Arriba la cabecera y las secciones (como en el panel del
            // administrador); debajo, el codigo y sus clases; al pie, de cuando son.
            raiz.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

            raiz.Controls.Add(CrearCabecera(recursos), 0, 0);
            raiz.Controls.Add(CrearSecciones(), 0, 1);
            raiz.Controls.Add(CrearTarjetas(), 0, 2);

            lblEstado = new Label
            {
                Dock = DockStyle.Fill,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = GrisEstado,
                TextAlign = ContentAlignment.MiddleRight,
                Text = "Cargando..."
            };
            raiz.Controls.Add(lblEstado, 0, 3);
            letraEstado = lblEstado.Font;
            letraAviso = new DrawingFont(letraEstado, FontStyle.Bold);
            // Lo tecnico de la linea de estado (la hora del servidor, cada cuanto se renueva) va en su ayuda.
            ayuda = new ToolTip(componentes) { InitialDelay = 300, AutoPopDelay = 15000 };
            avisoTimer = new Timer(componentes) { Interval = SegundosDelAviso * 1000 };
            avisoTimer.Tick += (s, e) => QuitarAviso();
            // La cabecera ("Cerrar sesion") la ultima en el orden de Tab: siendo la
            // primera, tenia el foco al abrir y un Enter o Espacio de mas cerraba la
            // sesion. Ahora se abre en "Mi horario".
            cabecera.TabIndex = raiz.Controls.Count;

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

            // Junto a los del Gobierno del Estado, el del TESCHI, del mismo alto y
            // separado por una raya como la que hay entre aquellos dos.
            PictureBox logoTeschi = new PictureBox
            {
                AccessibleName = "Tecnológico de Estudios Superiores de Chimalhuacán",
                Image = Properties.Resources.logoteschi,
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(logos.Right + 22, 16),
                Size = new Size(110, 48)
            };
            int rayaLogos = logos.Right + 11;
            int inicioTexto = logoTeschi.Right + 20;

            lblSaludo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(inicioTexto, 12),
                Size = new Size(600, 34),
                Text = "Hola"
            };

            // Sin AutoSize: en una ventana angosta se acorta con "..." antes de la fecha.
            lblSubtitulo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                Location = new Point(inicioTexto + 3, 48),
                Text = "Panel docente. Número de empleado: " + (numeroEmpleado.Length > 0 ? numeroEmpleado : "-")
            };
            lblSubtitulo.Size = lblSubtitulo.GetPreferredSize(Size.Empty);
            anchoSubtitulo = lblSubtitulo.Width;

            // La caja del reloj, de lo que mide la hora: a su izquierda llega el saludo.
            lblReloj = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 26F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                TextAlign = ContentAlignment.MiddleRight
            };
            lblReloj.Size = new Size(TextRenderer.MeasureText("00:00:00", lblReloj.Font).Width + 12, 44);

            lblFecha = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(320, 22),
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                TextAlign = ContentAlignment.MiddleRight
            };
            anchoFechaMax = TextRenderer.MeasureText("Miércoles 30 de septiembre de 2026", lblFecha.Font).Width;

            btnCerrarSesion = EstiloHorarios.CrearBoton("Cerrar sesión", Point.Empty, new Size(150, 40));
            // Salir de la sesion, no de la aplicacion (DigBit puede ser el shell).
            btnCerrarSesion.Click += (s, e) => AppContexto.Actual.CerrarSesion();

            cabecera.Controls.AddRange(new Control[] { logos, logoTeschi, lblSaludo, lblSubtitulo, lblReloj, lblFecha, btnCerrarSesion });
            cabecera.Resize += (s, e) => ColocarCabecera();
            cabecera.Paint += (s, e) =>
            {
                using (Pen linea = new Pen(Color.FromArgb(215, 222, 215)))
                {
                    e.Graphics.DrawLine(linea, 0, cabecera.Height - 1, cabecera.Width, cabecera.Height - 1);
                    e.Graphics.DrawLine(linea, rayaLogos, 22, rayaLogos, 58);
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
            // El saludo va en el renglon del reloj y llega hasta el, sin taparlo; el
            // numero de empleado, en el de la fecha, hasta donde empieza la mas larga,
            // y la caja de la fecha (alineada a la derecha) despues de el, nunca debajo.
            lblSaludo.Width = Math.Max(200, lblReloj.Left - 16 - lblSaludo.Left);
            lblSubtitulo.Width = Math.Max(60, Math.Min(anchoSubtitulo, finReloj - anchoFechaMax - 16 - lblSubtitulo.Left));
            int inicioFecha = Math.Max(finReloj - 320, lblSubtitulo.Right + 12);
            lblFecha.Bounds = new Rectangle(inicioFecha, 50, Math.Max(40, finReloj - inicioFecha), 22);
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
            fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37F));
            fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63F));
            fila.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Sin margen: cada tarjeta deja el suyo para el halo animado.
            tarjetaCodigo = new TarjetaCodigo { Dock = DockStyle.Fill, Margin = new Padding(0) };
            tarjetaCodigo.ReportarFalla += ReportarFalla;
            tarjetaCodigo.VerBitacora += sesion => VerRegistro(sesion, panorama == null ? Ahora().Date : panorama.AhoraServidor.Date);
            tarjetaCodigo.VerAsistencia += VerAsistencia;
            tarjetaCodigo.Personalizar += Personalizar;
            tarjetaDias = new TarjetaTresDias { Dock = DockStyle.Fill, Margin = new Padding(0) };
            tarjetaDias.VerRegistro += VerRegistro;
            tarjetaDias.Personalizar += Personalizar;
            fila.Controls.Add(tarjetaCodigo, 0, 0);
            fila.Controls.Add(tarjetaDias, 1, 0);
            return fila;
        }

        private Control CrearSecciones()
        {
            // A los lados, lo que dejan las tarjetas de abajo para su halo: asi quedan alineadas.
            TableLayoutPanel fila = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(Resplandor.Margen, 12, Resplandor.Margen, 0)
            };
            for (int i = 0; i < fila.ColumnCount; i++)
            {
                fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / fila.ColumnCount));
            }

            fila.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Cada descripcion en dos renglones a 1366: con tres, el ultimo salia cortado.
            AgregarSeccion(fila, 0, "Mi horario", "Tu semana con cancelaciones y sesiones extra. Pulsa una clase para ver su bitácora.", AbrirHorario);
            AgregarSeccion(fila, 1, "Mis grupos", "Tus grupos y la lista de asistencia de cada uno, como en Excel.", () => AbrirGrupos(null));
            AgregarSeccion(fila, 2, "Mis bitácoras", "Todas tus sesiones: quién registró, qué reportó y su PDF.", AbrirBitacoras);
            AgregarSeccion(fila, 3, "Mi perfil", "Tu nombre, tu correo y tu contraseña.", AbrirPerfil);
            return fila;
        }

        private static void AgregarSeccion(TableLayoutPanel fila, int columna, string titulo, string descripcion, Action abrir)
        {
            TarjetaOpcion tarjeta = new TarjetaOpcion(titulo, descripcion)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(columna == 0 ? 0 : 6, 0, columna == fila.ColumnCount - 1 ? 0 : 6, 0)
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
            int idProfesor = IdProfesor("Mis bitacoras");
            if (idProfesor != 0)
            {
                Mostrar("bitacoras", () => new GestionBitacorasAdministrador(idProfesor));
            }
        }

        /// <summary>Mis grupos: sus grupos y la lista de asistencia de cada uno; con una clase, esa ya elegida.</summary>
        private void AbrirGrupos(int? idClase)
        {
            int idProfesor = IdProfesor("Mis grupos");
            if (idProfesor == 0)
            {
                return;
            }

            VentanaMisGrupos ventana = (VentanaMisGrupos)Mostrar("grupos",
                () => new VentanaMisGrupos(idProfesor, panorama == null ? "" : panorama.Nombre, HorarioDeHoy));
            if (idClase.HasValue)
            {
                ventana.Elegir(idClase.Value);
            }
        }

        /// <summary>
        /// Los bloques de hoy de una de sus clases (sin los cancelados), en orden;
        /// vacia si hoy no le toca. Su columna de hoy en la lista de asistencia
        /// cuenta mientras dura el ultimo.
        /// </summary>
        private List<TramoHorario> HorarioDeHoy(int idClase)
        {
            PanoramaProfesor actual = panorama;
            List<TramoHorario> bloques = new List<TramoHorario>();
            if (actual == null)
            {
                return bloques;
            }

            foreach (SesionAgenda sesion in actual.Hoy.FindAll(s => s.ClaseId == idClase && !s.Cancelada))
            {
                bloques.Add(new TramoHorario(sesion.Inicio, sesion.Fin));
            }

            bloques.Sort((a, b) => a.Inicio.CompareTo(b.Inicio));
            return bloques;
        }

        /// <summary>Su id (de la ultima lectura del panel, o leyendolo); 0 y el aviso si no se pudo.</summary>
        private int IdProfesor(string seccion)
        {
            if (panorama != null && panorama.ProfesorId != 0)
            {
                return panorama.ProfesorId;
            }

            try
            {
                return new HorariosDatos().PanoramaDeProfesor(numeroEmpleado).ProfesorId;
            }
            catch (Exception ex)
            {
                Log.Error("Abrir " + seccion + " de " + numeroEmpleado, ex);
                // La causa y que hacer; el detalle tecnico se queda en el registro.
                // Las de DigBit (no encontro su cuenta) ya vienen escritas para el docente.
                MessageBox.Show(this, AvisoDeFalloDocente.EsAvisoPropio(ex)
                        ? ex.Message + " " + AvisoDeFalloDocente.Frase(MensajeDeFallo.AvisaALaAdministracion)
                        : MensajeDeFallo.Explicar("No se pudieron leer tus datos", ex, AvisoDeFalloDocente.SiSigue),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0;
            }
        }

        private void AbrirPerfil()
        {
            Mostrar("perfil", () => new editar_Perfil("profesor"));
        }

        /// <summary>Quien registro en una clase de las tres columnas (de la primera, ayer o el ultimo dia con clases, o de hoy, aunque aun no haya nadie).</summary>
        private void VerRegistro(SesionAgenda sesion, DateTime fecha)
        {
            string laboratorio = panorama == null ? "" : panorama.Laboratorio(sesion.LaboratorioId);
            DateTime hoy = panorama == null ? Ahora().Date : panorama.AhoraServidor.Date;
            // Otro dia (la primera columna salta los dias sin clases): "el viernes 9/10",
            // como su cabecera; una sesion extra, "del viernes 9/10".
            bool otroDia = fecha.Date != hoy && fecha.Date != hoy.AddDays(-1);
            string dia = fecha.Date == hoy ? "hoy" : !otroDia ? "ayer" : TarjetaTresDias.DiaConFecha(fecha);
            string horario = (sesion.EsExtra ? "Sesión extra " + (otroDia ? "del " : "de ") + dia
                    : otroDia ? "El " + dia : char.ToUpper(dia[0]) + dia.Substring(1)) + ", "
                + HorariosDatos.Hora(sesion.Inicio) + " a " + HorariosDatos.Hora(sesion.Fin) + (laboratorio.Length > 0 ? ", " + laboratorio : "");
            using (BitacoraSesion bitacora = new BitacoraSesion(sesion.FranjaId, sesion.ExcepcionId,
                sesion.Materia + ", " + sesion.Grupo, horario, panorama == null ? "" : panorama.Nombre, fecha.Date))
            {
                bitacora.ShowDialog(this);
            }

            ActualizarDatos();
        }

        /// <summary>
        /// La asistencia de la clase de la tarjeta: Mis grupos con esa clase
        /// elegida (la lista que se arma con quienes registran). Si la clase es
        /// de hoy, su columna cuenta mientras dura aunque aun nadie registre.
        /// </summary>
        private void VerAsistencia(SesionAgenda clase)
        {
            AbrirGrupos(clase.ClaseId);
        }

        /// <summary>
        /// "Personalizar tu clase" desde su circulo (la ficha o un bloque de las
        /// tres columnas). Lo guardado ya se ve (Cambio); el panel se relee para
        /// quedar al dia con la base.
        /// </summary>
        private void Personalizar(int idClase)
        {
            if (PersonalizacionClases.Abrir(this, idClase))
            {
                ActualizarDatos();
            }
        }

        /// <summary>Cambio lo que se ve de sus clases: se repintan las dos tarjetas (con el boton del circulo de la ficha).</summary>
        private void PersonalizacionCambio()
        {
            if (IsDisposed)
            {
                return;
            }

            tarjetaCodigo.Invalidate(true);
            tarjetaDias.Invalidate();
        }

        /// <summary>
        /// "Reportar una falla" de la clase en curso: donde, de que es y que falla.
        /// Lo guarda ReportesProfesorDatos con el reloj del servidor (si la clase
        /// ya termino, no se guarda y el dialogo lo dice; si falla, tambien lo dice
        /// alli). Lo ve el administrador en sus alertas y en Bitacoras; aqui suma
        /// en "fallas reportadas". Guardado, lo dice la linea de estado en verde
        /// unos segundos, sin un mensaje que haya que cerrar en plena clase.
        /// </summary>
        private void ReportarFalla(SesionAgenda clase)
        {
            if (panorama == null)
            {
                return;
            }

            ReportesProfesorDatos datos = new ReportesProfesorDatos();
            List<string> equipos;
            try
            {
                equipos = datos.Equipos(clase.LaboratorioId);
            }
            catch (Exception ex)
            {
                // Sin la lista, el profesor escribe el numero del equipo.
                Log.Aviso("No se pudo leer la lista de equipos para reportar una falla: " + ex.Message);
                equipos = new List<string>();
            }

            string laboratorio = panorama.Laboratorio(clase.LaboratorioId);
            string descripcion = clase.Materia + ", grupo " + clase.Grupo + (laboratorio.Length > 0 ? ", en " + laboratorio : "")
                + ", de " + HorariosDatos.Hora(clase.Inicio) + " a " + HorariosDatos.Hora(clase.Fin);
            int idProfesor = panorama.ProfesorId;
            using (DialogoReporteFalla dialogo = new DialogoReporteFalla(descripcion, clase.Fin, equipos,
                (equipo, area, falla, detalle) => datos.Guardar(clase, idProfesor, equipo, area, falla, detalle)))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            MostrarAviso("Tu reporte quedó guardado: la administración de DigBit lo verá en sus alertas y en la bitácora de esta clase.");
            ActualizarDatos();
        }

        // =====================================================================
        // Linea de estado
        // =====================================================================

        /// <summary>
        /// Lo que dice la linea de estado (la de siempre: cuando se actualizo, o
        /// que no hay conexion) y su ayuda con lo tecnico. Si hay un aviso a la
        /// vista, se guarda para cuando se quite.
        /// </summary>
        private void PonerEstado(string texto, Color color, string ayudaEstado)
        {
            estadoNormal = texto;
            colorNormal = color;
            ayudaNormal = ayudaEstado;
            if (conAviso)
            {
                return;
            }

            lblEstado.ForeColor = color;
            lblEstado.Text = texto;
            ayuda.SetToolTip(lblEstado, ayudaEstado);
        }

        /// <summary>Un aviso en verde y en negritas en la linea de estado durante unos segundos (SegundosDelAviso).</summary>
        private void MostrarAviso(string texto)
        {
            conAviso = true;
            lblEstado.Font = letraAviso;
            lblEstado.ForeColor = VerdeEstado;
            lblEstado.Text = texto;
            ayuda.SetToolTip(lblEstado, null);
            avisoTimer.Stop();
            avisoTimer.Start();
        }

        private void QuitarAviso()
        {
            avisoTimer.Stop();
            conAviso = false;
            lblEstado.Font = letraEstado;
            PonerEstado(estadoNormal, colorNormal, ayudaNormal);
        }

        /// <summary>
        /// Una ventana por seccion: si ya esta abierta se trae al frente. Al
        /// cerrarla se refresca el panel, con la lista de su clase contada otra
        /// vez (pudo cambiar su nombre en el perfil o quitar una asistencia en
        /// Mis grupos).
        /// </summary>
        private Form Mostrar(string clave, Func<Form> crear)
        {
            Form ventana;
            if (!ventanas.TryGetValue(clave, out ventana) || ventana.IsDisposed)
            {
                ventana = crear();
                ventana.FormClosed += (s, e) =>
                {
                    ResumenDeClase.Olvidar();
                    ActualizarDatos();
                };
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

            // En un equipo del laboratorio, una sesion olvidada se cierra sola: 10
            // minutos sin teclado ni raton, y nunca mientras dura una de sus clases
            // (puede tener el codigo a la vista para los que llegan tarde).
            if (Kiosco.Activo && vigilancia == null)
            {
                vigilancia = Inactividad.Vigilar(this, EsperaSinUso, CuentaAtras, () => !EnUnaDeSusClases(),
                    "No se ha tocado el teclado ni el ratón en " + (int)EsperaSinUso.TotalMinutes + " minutos y no estás en una de tus clases. "
                    + "Si ya te fuiste, tu sesión se cierra para que nadie más use tu cuenta en este equipo.");
            }
        }

        /// <summary>
        /// Si esta en una de sus clases (abierta: desde 15 minutos antes de que
        /// empiece hasta que termina, con la hora del servidor). Sin sus datos de
        /// hoy no se sabe: entonces no.
        /// </summary>
        private bool EnUnaDeSusClases()
        {
            PanoramaProfesor actual = panorama;
            DateTime ahora = Ahora();
            return actual != null && actual.AhoraServidor.Date == ahora.Date && ReglasCodigo.Abiertas(actual.Hoy, ahora.TimeOfDay).Count > 0;
        }

        private void ActualizarReloj()
        {
            DateTime ahora = Ahora();
            lblReloj.Text = ahora.ToString("HH:mm:ss");
            string fecha = ahora.ToString("dddd d 'de' MMMM 'de' yyyy", Espanol);
            lblFecha.Text = char.ToUpper(fecha[0], Espanol) + fecha.Substring(1);
            tarjetaDias.Ahora = ahora;
            tarjetaCodigo.Ahora = ahora;

            // Paso la medianoche: las clases cargadas son las de ayer.
            if (panorama != null && panorama.AhoraServidor.Date != ahora.Date && !sinConexionDesde.HasValue)
            {
                clasesPedidas.Clear();
                ActualizarDatos();
                return;
            }

            // Generacion de codigos: en cuanto una clase suya entra en su
            // ventana (15 minutos antes) se piden los datos, que la abren, sin
            // esperar al refresco de 30 s. Una sola vez por clase.
            if (panorama != null && !sinConexionDesde.HasValue)
            {
                foreach (SesionAgenda clase in panorama.Hoy)
                {
                    string clave = (clase.EsExtra ? "x" : "f") + (clase.EsExtra ? clase.ExcepcionId : clase.FranjaId);
                    if (!clase.SesionId.HasValue && !clase.Cancelada && panorama.ModoDe(clase.LaboratorioId) == ModoCodigo.Renovable
                        && ReglasCodigo.Abierta(clase.Inicio, clase.Fin, false, ahora.TimeOfDay) && clasesPedidas.Add(clave))
                    {
                        ActualizarDatos();
                        break;
                    }
                }
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
            // Lo de sus clases (db/19) se publica con la version de antes de leer:
            // si mientras tanto se guardo algo, esta lectura no lo pisa.
            int versionPersonal = PersonalizacionClases.Version;
            Task.Run(() =>
            {
                // Lee su dia y, con la generacion de codigos, abre las suyas que
                // ya estan en su ventana (AbrirClasesDeHoy nunca lanza).
                HorariosDatos datos = new HorariosDatos();
                PanoramaProfesor leido = datos.PanoramaDeProfesor(numero);
                datos.AbrirClasesDeHoy(leido);
                // Las imagenes de sus clases que aun no estan en memoria (nunca lanza).
                if (leido.PersonalLeido)
                {
                    leido.ImagenesPersonal = PersonalizacionClases.TraerImagenes(leido.ProfesorId, leido.Personal);
                }

                return new LecturaDelPanel { Panorama = leido, Listas = LeerListas(leido) };
            })
                .ContinueWith(tarea =>
                {
                    try
                    {
                        BeginInvoke(new Action(() => AplicarDatos(tarea, versionPersonal)));
                    }
                    catch (InvalidOperationException)
                    {
                        // El formulario se cerro mientras se consultaba.
                    }
                });
        }

        /// <summary>Lo que trae cada lectura del panel: su dia y la lista de cada clase de hoy (null si no se pudo).</summary>
        private sealed class LecturaDelPanel
        {
            public PanoramaProfesor Panorama;
            public Dictionary<int, ListaDeClase> Listas;
        }

        /// <summary>
        /// La lista de su clase abierta ("16 de 24 alumnos", ResumenDeClase: la
        /// de Mis grupos). Nunca lanza: si no se pudo, null y el panel se queda
        /// con la de la lectura anterior.
        /// </summary>
        private static Dictionary<int, ListaDeClase> LeerListas(PanoramaProfesor leido)
        {
            try
            {
                return ResumenDeClase.Leer(leido);
            }
            catch (Exception ex)
            {
                // Se repetiria cada 30 s: al registro, solo la primera vez.
                if (!listasAvisadas)
                {
                    listasAvisadas = true;
                    Log.Error("Panel del profesor: no se pudo contar la lista de sus clases", ex);
                }

                return null;
            }
        }

        private void AplicarDatos(Task<LecturaDelPanel> tarea, int versionPersonal)
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

                // La causa y que hacer, sin el texto del driver (al registro, arriba):
                // un aviso de DigBit tal cual; la red, desde cuando; otro fallo, por que.
                PonerEstado(AvisoDeFalloDocente.EsAvisoPropio(error)
                        ? error.Message + " " + AvisoDeFalloDocente.Frase(MensajeDeFallo.AvisaALaAdministracion)
                        : MensajeDeFallo.EsDeRed(error)
                        ? "Sin conexión con el servidor desde las " + sinConexionDesde.Value.ToString("HH:mm") + ". Lo que ves puede no estar al día."
                        : MensajeDeFallo.Explicar("No se pudieron leer tus clases", error, AvisoDeFalloDocente.SiSigue),
                    NaranjaEstado, "Se vuelve a intentar solo cada " + SegundosEntreActualizaciones + " s.");
                return;
            }

            if (sinConexionDesde.HasValue)
            {
                Log.Info("Panel del profesor: conexion recuperada.");
                sinConexionDesde = null;
            }

            panorama = tarea.Result.Panorama;
            if (tarea.Result.Listas != null)
            {
                listasDeHoy = tarea.Result.Listas;
            }

            desfaseServidor = panorama.AhoraServidor - DateTime.Now;
            lblSaludo.Text = "Hola, " + panorama.Nombre;
            // La hora del servidor antes que los datos: con ella colocan la vista
            // (hasta ahora tenian la del equipo, que puede ir adelantada).
            tarjetaCodigo.Ahora = Ahora();
            tarjetaDias.Ahora = Ahora();
            // Antes de pintar, el circulo y el color de cada clase (db/19). Si esta
            // vez no se pudieron leer, se queda lo de antes.
            if (panorama.PersonalLeido)
            {
                PersonalizacionClases.Publicar(panorama.Personal, panorama.ImagenesPersonal, versionPersonal);
            }

            tarjetaCodigo.Mostrar(panorama, listasDeHoy);
            tarjetaDias.Mostrar(panorama);
            ActualizarReloj();
            string actualizado = "Actualizado a las " + Ahora().ToString("HH:mm");
            string comoSeActualiza = "Con la hora del servidor. El panel se actualiza solo cada " + SegundosEntreActualizaciones + " s.";
            if (!string.IsNullOrEmpty(panorama.AvisoApertura))
            {
                // No es una caida de la red: el resto del panel esta al dia.
                PonerEstado(panorama.AvisoApertura, NaranjaEstado, actualizado + ". " + comoSeActualiza);
                return;
            }

            PonerEstado(actualizado, GrisEstado, comoSeActualiza);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            relojTimer.Stop();
            datosTimer.Stop();
            avisoTimer.Stop();
            if (vigilancia != null)
            {
                vigilancia.Dispose();
                vigilancia = null;
            }

            PersonalizacionClases.Cambio -= PersonalizacionCambio;
            // En un equipo del laboratorio entra otro despues: nada de sus listas.
            ResumenDeClase.Olvidar();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                componentes.Dispose();
                // La etiqueta no libera su letra (Control.Dispose no lo hace).
                if (letraAviso != null)
                {
                    letraAviso.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Lo comun de los avisos de fallo de las pantallas del docente (RNF12): a
    /// quien acudir si sigue fallando, segun quien tiene la sesion (para
    /// MensajeDeFallo.Explicar), y que excepciones ya vienen escritas por DigBit
    /// para la persona. La administracion revisa el registro de su equipo; la
    /// persona responsable de laboratorio (solo consulta) avisa a la
    /// administracion; en un equipo del laboratorio (kiosco), estudiantes y
    /// docentes avisan a la persona responsable del laboratorio; el docente en
    /// su propio equipo, a la administracion.
    /// </summary>
    internal static class AvisoDeFalloDocente
    {
        /// <summary>El "si sigue" de MensajeDeFallo (en minusculas y sin punto) para quien tiene la sesion.</summary>
        public static string SiSigue
        {
            get
            {
                switch (Datos_User.TipoUsuario)
                {
                    case TiposUsuario.Administrador:
                        return MensajeDeFallo.RevisaElRegistro;
                    case TiposUsuario.Encargado:
                        return MensajeDeFallo.AvisaALaAdministracion;
                    case TiposUsuario.Alumno:
                        return MensajeDeFallo.AvisaAlLaboratorio;
                    default:
                        return Kiosco.Activo ? MensajeDeFallo.AvisaAlLaboratorio : MensajeDeFallo.AvisaALaAdministracion;
                }
            }
        }

        /// <summary>Una de las constantes de MensajeDeFallo como frase: con mayuscula y punto.</summary>
        public static string Frase(string accion)
        {
            return string.IsNullOrEmpty(accion) ? "" : char.ToUpperInvariant(accion[0]) + accion.Substring(1) + ".";
        }

        /// <summary>
        /// Una InvalidOperationException que lanzo DigBit (ya escrita para la
        /// persona, como "No se encontro tu cuenta..."), no la de una biblioteca
        /// (MySql.Data: "Connection must be valid and open", en ingles).
        /// </summary>
        public static bool EsAvisoPropio(Exception ex)
        {
            return ex is InvalidOperationException
                && string.Equals(ex.Source, typeof(AvisoDeFalloDocente).Assembly.GetName().Name, StringComparison.Ordinal);
        }
    }
}
