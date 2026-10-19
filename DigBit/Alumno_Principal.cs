using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigBit
{
    /// <summary>
    /// Pantalla del alumno, en dos pasos y sin ventanas intermedias: 1) el codigo
    /// que dicta el profesor y 2) como esta este equipo (todo bien, una falla o
    /// trajo el suyo), con un boton final "Registrar mi bitacora": solo cuenta el
    /// primer registro, asi que un clic equivocado no se guarda solo. Arriba, la
    /// clase que hay ahora en este laboratorio.
    ///
    /// Sustituye al menu del alumno, a Buscar_Codigo_Alumno y a Registro_Bitacora
    /// con la misma logica: el codigo se resuelve con el reloj del servidor o, sin
    /// servidor, con la copia local (fase 4); quien ya registro y sigue en clase
    /// puede volver a usar el equipo; con equipo propio no se libera la maquina; y
    /// al guardar se libera el equipo hasta la hora de salida (fase 3).
    /// </summary>
    public class Alumno_Principal : Form
    {
        private const int AnchoMaximo = 900;
        private const int SegundosEntreActualizaciones = 60;
        private const string SinProblema = "Sin problema";
        private const string Otro = "Otro";
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");
        private static readonly Color TextoSecundario = Color.FromArgb(90, 90, 90);
        private static readonly Color FondoClase = Color.FromArgb(236, 246, 236);
        private static readonly Color FondoAviso = Color.FromArgb(255, 236, 204);
        private static readonly Color Rojo = Color.FromArgb(190, 30, 30);
        private static readonly Color Apagado = Color.FromArgb(165, 165, 165);
        // El borde y el relleno de lo que aun no se puede pulsar, como "Registrar mi bitacora".
        private static readonly Color BordeApagado = Color.FromArgb(205, 210, 205);

        // La sesion que se queda sola: sin teclado ni raton en 3 minutos y sin
        // haber registrado, el aviso "Sigues ahi?" y, sin respuesta, el login.
        private static readonly TimeSpan EsperaInactividad = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan CuentaAtrasInactividad = TimeSpan.FromSeconds(30);
        private IDisposable vigilancia;

        private readonly Container componentes = new Container();
        private readonly Consultas consultas = new Consultas();
        private readonly string matricula;
        private string nombre;

        // Lo que dejo el paso 1 al aceptar el codigo.
        private string codigoAceptado;
        private VentanaCodigo ventana;
        private bool registrado;

        private Panel cabecera;
        private PictureBox logos;
        private PictureBox logoTeschi;
        // En una ventana angosta el logotipo del TESCHI se quita (ColocarCabecera).
        private bool conLogoTeschi = true;
        private Label lblSaludo;
        private Label lblSubtitulo;
        private int anchoSubtitulo;
        private Label lblReloj;
        private Label lblFecha;
        private Button btnCerrarSesion;

        private Panel contenedor;
        private Panel columna;
        // Colocar pide recalcular las barras del contenedor, y eso lo vuelve a llamar.
        private bool recalculandoBarras;
        private Panel pnlClase;
        private Label lblClaseTitulo;
        private Label lblClaseDetalle;
        // La clase de ahora como ficha (con el circulo que eligio el profesor, db/19);
        // sin clase, las dos etiquetas de arriba.
        private FichaClaseAlumno fichaClase;
        // Lo personal de los profesores de hoy en este laboratorio, por profesor.
        private readonly Dictionary<int, LecturaPersonal> personalPorProfesor = new Dictionary<int, LecturaPersonal>();

        private Label lblPaso1;
        private Label lblPaso1Ayuda;
        private TextBox txtCodigo;
        private Button btnContinuar;
        private Label lblErrorCodigo;
        private Label lblCodigoOk;
        private LinkLabel lnkOtroCodigo;

        // Como se entra a la clase en este laboratorio (db/11). Sin codigo (o con
        // codigos generados y sin servidor) el alumno elige su clase: un boton
        // por cada clase abierta y, debajo, la siguiente.
        private ModoCodigo modo = ModoCodigo.Fijo;
        private bool modoSinConexion;
        // Ya se aplico el modo leido una vez (del servidor o de la copia): hasta
        // entonces, el Fijo de arriba es solo el valor de partida.
        private bool modoLeido;
        private FlowLayoutPanel pnlClases;
        private Label lblProxima;
        private string clavesClases = "";
        // Sin codigos: con una sola clase abierta se elige sola, una vez por clase
        // (las ya elegidas, solas o a mano, no se repiten: si se rechazo, no se
        // insiste), y ya no despues de que el alumno pulse "Elegir otra clase".
        private readonly HashSet<string> elegidasSolas = new HashSet<string>();
        private bool eligeElAlumno;
        private Timer eleccionPendiente;
        // Sin codigos, sin ninguna clase abierta y con db/14: el tramo de uso libre
        // de ahora (hasta que se abra la siguiente clase). Null si no hay.
        private TramoLibre tramoLibre;

        private Label lblPaso2;
        private FlowLayoutPanel pnlEstados;
        private RadioButton rbTodoBien;
        private RadioButton rbFalla;
        private RadioButton rbPropio;
        private Label lblPista;
        private Panel pnlFallas;
        private readonly Dictionary<string, Label> etiquetasArea = new Dictionary<string, Label>();
        private readonly Dictionary<string, FlowLayoutPanel> chipsArea = new Dictionary<string, FlowLayoutPanel>();
        private readonly Dictionary<string, TextBox> otroArea = new Dictionary<string, TextBox>();
        private Label lblErrorPaso2;
        private Button btnRegistrar;

        // Paso 3: otras computadoras del laboratorio sin internet. Solo si la base
        // tiene donde guardarlo (db/10); sin conexion, lo que diga la copia local.
        private bool preguntarVecinos;
        private Label lblPaso3;
        private FlowLayoutPanel pnlVecinos;
        private RadioButton rbVecinosNo;
        private RadioButton rbVecinosSi;
        // Los equipos de este laboratorio para marcar (los que registraron
        // bitacoras hace poco, sin este). Sin ninguno, se escribe el numero.
        private FlowLayoutPanel pnlEquipos;
        private List<string> equiposLista = new List<string>();
        private string clavesEquipos;
        private Label lblOtroEquipo;
        private TextBox txtVecinos;
        private Label lblVecinosPista;

        private Label lblListo;
        private Label lblListoDetalle;
        private Panel pie;
        private LinkLabel lnkPerfil;
        private editar_Perfil perfil;

        private Timer relojTimer;
        private Timer datosTimer;
        private bool actualizando;

        /// <summary>Lo que se lee en segundo plano: el nombre del alumno, el modo y la agenda de hoy del laboratorio, y si se pregunta el paso 3 (y con que equipos).</summary>
        private sealed class DatosPantalla
        {
            public string Nombre;
            public ModoCodigo Modo;
            public List<SesionAgenda> Agenda = new List<SesionAgenda>();
            public DateTime Ahora;
            public bool ConAvisosDeRed;
            /// <summary>Sin codigos y con db/14: sin clase abierta, uso libre.</summary>
            public bool ConUsoLibre;
            /// <summary>Los equipos del laboratorio para marcar en el paso 3; null si no se leyeron.</summary>
            public List<string> Equipos;
            /// <summary>Lo personal de cada profesor de la agenda (db/19), por profesor; null si no se leyo.</summary>
            public Dictionary<int, LecturaPersonal> Personal;
            /// <summary>Las imagenes de sus clases de hoy que aun no estaban en memoria, por SHA1.</summary>
            public Dictionary<string, byte[]> Imagenes;
        }

        /// <summary>
        /// Se teclea un codigo (fijo, o el nuevo de la clase con servidor); si no,
        /// se elige la clase. Con lo ya aplicado (Aplicar), no con SinConexion en
        /// este instante: si no, al caer la red el paso 1 cambiaba de forma (sin
        /// caja ni lista) antes de rehacer sus textos y sus botones.
        /// </summary>
        private bool PideCodigo
        {
            get { return modo == ModoCodigo.Fijo || (modo == ModoCodigo.Renovable && !modoSinConexion); }
        }

        public Alumno_Principal()
        {
            matricula = Datos_User.getUser() ?? "";
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
            ActualizarPaso2();
        }

        // =====================================================================
        // Construccion de la interfaz
        // =====================================================================

        private void InicializarComponentes()
        {
            ComponentResourceManager recursos = new ComponentResourceManager(typeof(PrincipalAdministrador));

            BackColor = Color.White;
            ClientSize = new Size(1100, 760);
            // 920 de ancho: asi la cabecera (ya sin el logotipo del TESCHI) lleva enteras
            // la matricula y la fecha mas larga del ano; con 900 no cabian.
            MinimumSize = new Size(920, 640);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = (Icon)recursos.GetObject("$this.Icon");
            Text = "DigBit - Estudiante";

            contenedor = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
            columna = new Panel { BackColor = Color.White };
            contenedor.Controls.Add(columna);
            contenedor.Resize += (s, e) => Colocar();

            CrearClase();
            CrearPaso1();
            CrearPaso2();
            CrearPaso3();

            lblListo = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Visible = false
            };
            lblListoDetalle = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 12F),
                ForeColor = TextoSecundario,
                Visible = false
            };

            lnkPerfil = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10.5F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Text = "Mi perfil (nombre, correo y contraseña)"
            };
            lnkPerfil.LinkClicked += (s, e) => AbrirPerfil();

            columna.Controls.AddRange(new Control[] { lblListo, lblListoDetalle });

            // Barra fija abajo: "Registrar mi bitacora" siempre a la vista, aunque la
            // lista de fallas no quepa en una pantalla de 768 de alto.
            pie = new Panel { Dock = DockStyle.Bottom, Height = 84, BackColor = Color.White };
            pie.Controls.AddRange(new Control[] { lnkPerfil, lblErrorPaso2, btnRegistrar });
            pie.Paint += (s, e) =>
            {
                using (Pen linea = new Pen(Color.FromArgb(215, 222, 215)))
                {
                    e.Graphics.DrawLine(linea, 20, 0, pie.Width - 20, 0);
                }
            };
            pie.Resize += (s, e) => Colocar();

            // El ultimo que se anade se acopla primero: cabecera arriba, pie abajo y el resto en medio.
            Controls.Add(contenedor);
            Controls.Add(pie);
            Controls.Add(CrearCabecera(recursos));

            relojTimer = new Timer(componentes) { Interval = 1000 };
            relojTimer.Tick += (s, e) =>
            {
                ActualizarReloj();
                fichaClase.Tic();
            };
            datosTimer = new Timer(componentes) { Interval = SegundosEntreActualizaciones * 1000 };
            datosTimer.Tick += (s, e) =>
            {
                // Con el codigo ya aceptado, lo de arriba es su clase: no se toca.
                if (codigoAceptado == null)
                {
                    ActualizarDatos();
                }
            };
        }

        private Control CrearCabecera(ComponentResourceManager recursos)
        {
            cabecera = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(20, 8, 20, 0), BackColor = Color.White };

            logos = new PictureBox
            {
                Image = (Image)recursos.GetObject("Logos"),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(20, 24),
                Size = new Size(230, 48)
            };

            // Junto a los del Gobierno del Estado, el del TESCHI, del mismo alto y
            // separado por una raya como la que hay entre aquellos dos.
            logoTeschi = new PictureBox
            {
                AccessibleName = "Tecnológico de Estudios Superiores de Chimalhuacán",
                Image = Properties.Resources.logoteschi,
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(logos.Right + 22, 24),
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
                Location = new Point(inicioTexto, 20),
                Size = new Size(560, 34),
                Text = "Hola"
            };

            // Sin AutoSize: en una ventana angosta se acorta con "..." antes de la fecha.
            lblSubtitulo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                Location = new Point(inicioTexto + 3, 56),
                Text = "Matrícula " + (matricula.Length > 0 ? matricula : "-")
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
            lblReloj.Size = new Size(TextRenderer.MeasureText("00:00", lblReloj.Font).Width + 12, 44);

            lblFecha = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(320, 22),
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                TextAlign = ContentAlignment.MiddleRight
            };

            btnCerrarSesion = EstiloHorarios.CrearBoton("Cerrar sesión", Point.Empty, new Size(150, 40));
            // Salir de la sesion, no de la aplicacion: en el laboratorio DigBit es el shell.
            btnCerrarSesion.Click += (s, e) => AppContexto.Actual.CerrarSesion();

            cabecera.Controls.AddRange(new Control[] { logos, logoTeschi, lblSaludo, lblSubtitulo, lblReloj, lblFecha, btnCerrarSesion });
            cabecera.Resize += (s, e) => ColocarCabecera();
            // Cada dia la fecha mide distinto: con la nueva se vuelve a colocar.
            lblFecha.TextChanged += (s, e) => ColocarCabecera();
            cabecera.Paint += (s, e) =>
            {
                using (Pen linea = new Pen(Color.FromArgb(215, 222, 215)))
                {
                    e.Graphics.DrawLine(linea, 20, cabecera.Height - 1, cabecera.Width - 20, cabecera.Height - 1);
                    if (conLogoTeschi)
                    {
                        e.Graphics.DrawLine(linea, rayaLogos, 30, rayaLogos, 66);
                    }
                }
            };
            return cabecera;
        }

        /// <summary>
        /// El saludo va en el renglon del reloj y llega hasta el, nunca encima (se
        /// acorta con "..."); la matricula, en el de la fecha, hasta donde empieza la
        /// de hoy, y la caja de la fecha (alineada a la derecha) despues de ella. El
        /// logotipo del TESCHI va si con el caben enteras la matricula y la fecha; en
        /// una ventana mas angosta se quita, con su raya, y los textos pasan junto a
        /// los otros logos, donde estaban antes de el.
        /// </summary>
        private void ColocarCabecera()
        {
            int derecha = cabecera.ClientSize.Width - 20;
            btnCerrarSesion.Location = new Point(derecha - btnCerrarSesion.Width, 28);
            int finReloj = btnCerrarSesion.Left - 24;
            lblReloj.Location = new Point(finReloj - lblReloj.Width, 12);

            int anchoFecha = TextRenderer.MeasureText(lblFecha.Text, lblFecha.Font).Width;
            bool conLogo = logoTeschi.Right + 20 + 3 + anchoSubtitulo + 16 + anchoFecha <= finReloj;
            if (conLogo != conLogoTeschi)
            {
                conLogoTeschi = conLogo;
                logoTeschi.Visible = conLogo;
                // La raya la pinta la cabecera.
                cabecera.Invalidate();
            }

            int inicioTexto = (conLogo ? logoTeschi.Right : logos.Right) + 20;
            lblSaludo.Left = inicioTexto;
            lblSaludo.Width = Math.Max(0, lblReloj.Left - 16 - inicioTexto);
            lblSubtitulo.Left = inicioTexto + 3;
            lblSubtitulo.Width = Math.Max(60, Math.Min(anchoSubtitulo, finReloj - anchoFecha - 16 - lblSubtitulo.Left));
            int inicioFecha = Math.Max(finReloj - 320, lblSubtitulo.Right + 12);
            lblFecha.Bounds = new Rectangle(inicioFecha, 58, Math.Max(40, finReloj - inicioFecha), 22);
        }

        /// <summary>La franja de arriba: que clase hay ahora en este laboratorio.</summary>
        private void CrearClase()
        {
            pnlClase = new Panel { BackColor = FondoClase };
            lblClaseTitulo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", 12.5F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Text = "Consultando la clase de ahora..."
            };
            lblClaseDetalle = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", 11F),
                ForeColor = Color.FromArgb(50, 50, 50)
            };
            fichaClase = new FichaClaseAlumno { Visible = false, Dock = DockStyle.Fill };
            pnlClase.Controls.Add(fichaClase);
            pnlClase.Controls.Add(lblClaseTitulo);
            pnlClase.Controls.Add(lblClaseDetalle);
            columna.Controls.Add(pnlClase);
        }

        /// <summary>
        /// La clase como ficha: el circulo que eligio el profesor, la materia, el
        /// grupo, el profesor, el laboratorio, el equipo y la barra de la hora.
        /// esLaTuya: ya escribio su codigo (o la eligio). nota: una linea mas abajo.
        /// </summary>
        private void MostrarFicha(string materia, string grupo, string profesor, TimeSpan inicio, TimeSpan fin, bool esLaTuya, bool esExtra,
            string nota, AspectoClase aspecto)
        {
            bool sinConexion = SinConexion.Activo;
            if (sinConexion)
            {
                nota = "Sin conexión con el servidor: tu bitácora se guardará en este equipo y se enviará sola.";
            }

            fichaClase.Mostrar(materia, grupo, profesor, LaboratorioEquipo.Nombre ?? "", LaboratorioEquipo.NumeroMaquina,
                inicio, fin, esLaTuya, esExtra, nota, sinConexion, aspecto);
            lblClaseTitulo.Visible = false;
            lblClaseDetalle.Visible = false;
            pnlClase.BackColor = columna.BackColor;
            bool cambia = !fichaClase.Visible || pnlClase.Height != fichaClase.AltoNecesario;
            fichaClase.Visible = true;
            if (cambia)
            {
                Colocar();
            }
        }

        /// <summary>Vuelve a las dos etiquetas (sin clase, uso libre, errores).</summary>
        private void OcultarFicha()
        {
            if (fichaClase == null || !fichaClase.Visible)
            {
                return;
            }

            fichaClase.Visible = false;
            lblClaseTitulo.Visible = true;
            lblClaseDetalle.Visible = true;
            Colocar();
        }

        /// <summary>Como se ve esa clase (por su profesor, materia y grupo) con lo leido; null si no se sabe.</summary>
        private AspectoClase AspectoDe(int idProfesor, int idMateria, int idGrupo)
        {
            LecturaPersonal leida;
            return idProfesor > 0 && personalPorProfesor.TryGetValue(idProfesor, out leida)
                ? PersonalizacionClases.DeOtroProfesor(leida, idMateria, idGrupo)
                : null;
        }

        /// <summary>
        /// Segundo plano: lo personal (db/19) de cada profesor de la agenda de hoy y
        /// las imagenes de esas clases que aun no estan en memoria. Nunca lanza: sin
        /// db/19, sin permiso o con un fallo, la franja sale sin circulo, como antes.
        /// </summary>
        private static void LeerPersonal(DatosPantalla datos)
        {
            List<SesionAgenda> agenda = datos.Agenda ?? new List<SesionAgenda>();
            List<int> profesores = agenda.Where(s => s.ProfesorId > 0).Select(s => s.ProfesorId).Distinct().ToList();
            if (profesores.Count == 0)
            {
                return;
            }

            try
            {
                using (MySql.Data.MySqlClient.MySqlConnection conexion = new Conexion().GetConexion())
                {
                    Dictionary<int, LecturaPersonal> personal = new Dictionary<int, LecturaPersonal>();
                    Dictionary<string, byte[]> imagenes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (int idProfesor in profesores)
                    {
                        LecturaPersonal leida = PersonalizacionClasesDatos.Leer(conexion, idProfesor);
                        if (leida == null)
                        {
                            // Sin db/19: nadie tiene circulo.
                            return;
                        }

                        personal[idProfesor] = leida;
                        List<string> faltan = leida.Clases
                            .Where(c => agenda.Any(s => s.ProfesorId == idProfesor && s.MateriaId == c.MateriaId && s.GrupoId == c.GrupoId))
                            .Select(c => c.ImagenSha1)
                            .Where(s => !string.IsNullOrEmpty(s) && PersonalizacionClases.BytesDe(s) == null)
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (faltan.Count > 0)
                        {
                            foreach (KeyValuePair<string, byte[]> imagen in PersonalizacionClasesDatos.Imagenes(conexion, idProfesor, faltan))
                            {
                                imagenes[imagen.Key] = imagen.Value;
                            }
                        }
                    }

                    datos.Personal = personal;
                    datos.Imagenes = imagenes;
                }
            }
            catch (Exception ex)
            {
                Log.Aviso("Pantalla del alumno: no se pudo leer el circulo de las clases: " + ex.Message);
            }
        }

        private void CrearPaso1()
        {
            lblPaso1 = TituloPaso("Paso 1. Escribe el código que te dicta tu docente");

            // En mayusculas: el codigo no distingue (cotejo _ci en la base, sin
            // distinguir en la copia local) y asi se ve igual que en el panel del profesor.
            txtCodigo = new TextBox
            {
                CharacterCasing = CharacterCasing.Upper,
                Font = new DrawingFont("Consolas", 24F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                MaxLength = 16,
                Width = 300,
                TextAlign = HorizontalAlignment.Center
            };
            txtCodigo.TextChanged += (s, e) => MostrarErrorCodigo("");

            btnContinuar = EstiloAdmin.BotonPrincipal("Continuar", Point.Empty, new Size(170, 40));
            btnContinuar.Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);
            btnContinuar.Click += (s, e) => Continuar();
            AcceptButton = btnContinuar;

            lblErrorCodigo = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold),
                ForeColor = Rojo
            };

            lblCodigoOk = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Visible = false
            };

            lnkOtroCodigo = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Text = "Usar otro código",
                Visible = false
            };
            lnkOtroCodigo.LinkClicked += (s, e) => OlvidarCodigo();

            lblPaso1Ayuda = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10.5F),
                ForeColor = TextoSecundario
            };

            // Sin codigo: un boton por clase abierta. Botones y no RadioButton: un
            // RadioButton que recibe el foco se marca solo, y aqui un clic ya elige.
            pnlClases = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.White,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            lblProxima = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10.5F),
                ForeColor = TextoSecundario
            };

            columna.Controls.AddRange(new Control[] { lblPaso1, lblPaso1Ayuda, txtCodigo, btnContinuar, pnlClases, lblProxima, lblErrorCodigo, lblCodigoOk, lnkOtroCodigo });
        }

        /// <summary>El titulo y la ayuda del paso 1 segun el modo del laboratorio.</summary>
        private void TextosPaso1()
        {
            if (PideCodigo)
            {
                lblPaso1.Text = modo == ModoCodigo.Renovable
                    ? "Paso 1. Escribe el código de esta clase"
                    : "Paso 1. Escribe el código que te dicta tu docente";
                lblPaso1Ayuda.Text = modo == ModoCodigo.Renovable
                    ? "Cambia en cada clase: tu docente lo ve al entrar a DigBit."
                    : "";
                if (codigoAceptado == null)
                {
                    AcceptButton = btnContinuar;
                }
            }
            else
            {
                lblPaso1.Text = "Paso 1. ¿A qué clase vienes?";
                lblPaso1Ayuda.Text = modo == ModoCodigo.Renovable
                    ? "Sin conexión no se puede comprobar el código de la clase: elige tu clase. Tu bitácora quedará marcada como sin conexión."
                    : "Elige tu clase: salen las de este laboratorio a esta hora.";
                // Enter no elige por el alumno.
                AcceptButton = null;
            }
        }

        /// <summary>
        /// Un boton por clase abierta y, sin ninguna (sin codigos y con db/14), uno
        /// de uso libre de este tramo. Solo se rehacen si cambian.
        /// </summary>
        private void PonerClases(List<SesionAgenda> clases, TramoLibre libre = null)
        {
            string claves = string.Join("|", clases.Select(ClaveDe))
                + (libre == null ? "" : "|" + ClaveLibre(libre));
            if (claves == clavesClases)
            {
                return;
            }

            clavesClases = claves;
            // Si el alumno estaba en uno de los botones (con Tab o las flechas), sale
            // de el antes de quitarlo: si no, Windows pasaria el foco a otro control
            // (otra clase, o "Mi perfil") y Enter elegiria por el.
            string claveConFoco = ClaveConFoco();
            if (claveConFoco != null)
            {
                ActiveControl = null;
            }

            pnlClases.SuspendLayout();
            foreach (Control viejo in pnlClases.Controls.Cast<Control>().ToList())
            {
                viejo.Dispose();
            }

            foreach (SesionAgenda clase in clases)
            {
                SesionAgenda esta = clase;
                Button boton = BotonDeClase(clase.Materia + " — " + clase.Grupo + (clase.EsExtra ? " (sesión extra)" : "") + Environment.NewLine
                    + HorariosDatos.Hora(clase.Inicio) + " a " + HorariosDatos.Hora(clase.Fin) + ", con " + clase.Profesor, clase);
                boton.Click += (s, e) => ElegirClase(esta);
                pnlClases.Controls.Add(boton);
            }

            if (libre != null)
            {
                Button boton = BotonDeClase("Uso libre del laboratorio (sin clase)" + Environment.NewLine
                    + "Puedes usar el equipo " + HastaLas(libre.Fin), libre);
                boton.Click += (s, e) => ElegirUsoLibre();
                pnlClases.Controls.Add(boton);
            }

            PonerParadaEnClases(null);
            pnlClases.ResumeLayout();
            // El que tenia, si su clase sigue en la lista.
            EnfocarClase(claveConFoco);
        }

        /// <summary>
        /// Un boton de la lista de clases. Se llega con Tab, con una sola parada
        /// para toda la lista (dentro, las flechas), como la lista del paso 3; no
        /// hay AcceptButton, asi que Enter solo elige el boton que tiene el foco.
        /// </summary>
        private Button BotonDeClase(string texto, object dato)
        {
            Button boton = new Button
            {
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(12, 0, 0, 0),
                Tag = dato,
                TabStop = false,
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = false,
                Text = texto
            };
            boton.FlatAppearance.BorderColor = Color.DarkGreen;
            boton.FlatAppearance.BorderSize = 2;
            boton.FlatAppearance.MouseOverBackColor = FondoClase;
            // La parada de Tab se queda en el ultimo que tuvo el foco: Mayus+Tab sale de la lista.
            boton.Enter += (s, e) => PonerParadaEnClases((Button)s);
            return boton;
        }

        /// <summary>La parada de Tab de la lista de clases: el boton dado o, si no hay, el primero.</summary>
        private void PonerParadaEnClases(Button parada)
        {
            List<Button> botones = pnlClases.Controls.OfType<Button>().ToList();
            parada = parada ?? botones.FirstOrDefault();
            foreach (Button boton in botones)
            {
                boton.TabStop = boton == parada;
            }
        }

        /// <summary>La clase (ClaveDe o ClaveLibre) del boton de la lista que tiene el foco; null si ninguno.</summary>
        private string ClaveConFoco()
        {
            Button boton = pnlClases.Controls.OfType<Button>().FirstOrDefault(b => b.Focused);
            if (boton == null)
            {
                return null;
            }

            SesionAgenda clase = boton.Tag as SesionAgenda;
            TramoLibre tramo = boton.Tag as TramoLibre;
            return clase != null ? ClaveDe(clase) : tramo != null ? ClaveLibre(tramo) : null;
        }

        /// <summary>El foco al boton de esa clase, si sigue en la lista y se ve.</summary>
        private void EnfocarClase(string clave)
        {
            if (clave == null || !pnlClases.Visible || !pnlClases.Enabled)
            {
                return;
            }

            foreach (Button boton in pnlClases.Controls.OfType<Button>())
            {
                SesionAgenda clase = boton.Tag as SesionAgenda;
                TramoLibre tramo = boton.Tag as TramoLibre;
                if ((clase != null && ClaveDe(clase) == clave) || (tramo != null && ClaveLibre(tramo) == clave))
                {
                    boton.Focus();
                    return;
                }
            }
        }

        /// <summary>"hasta las 11:45", o "hasta el final del día" si no hay mas clases hoy.</summary>
        private static string HastaLas(TimeSpan fin)
        {
            return fin >= ReglasCodigo.FinDelDia ? "hasta el final del día" : "hasta las " + HorariosDatos.Hora(fin);
        }

        /// <summary>Debajo de los botones: la siguiente clase, o por que no hay ninguna abierta.</summary>
        private static string TextoProxima(List<SesionAgenda> agenda, List<SesionAgenda> abiertas, TimeSpan ahora, TramoLibre libre = null)
        {
            TimeSpan margen = TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            if (libre != null)
            {
                SesionAgenda siguiente = agenda.Where(s => !s.Cancelada && ahora < s.Inicio - margen).OrderBy(s => s.Inicio).FirstOrDefault();
                return siguiente == null ? "Hoy ya no hay más clases en este laboratorio."
                    : "La siguiente clase, " + siguiente.Materia + " — " + siguiente.Grupo + ", empieza a las " + HorariosDatos.Hora(siguiente.Inicio)
                      + ": desde las " + HorariosDatos.Hora(siguiente.Inicio - margen) + " el laboratorio es de esa clase.";
            }

            if (abiertas.Count > 0)
            {
                SesionAgenda despues = agenda.Where(s => !s.Cancelada && ahora < s.Inicio - margen).OrderBy(s => s.Inicio).FirstOrDefault();
                return despues == null ? ""
                    : "Después: " + despues.Materia + " — " + despues.Grupo + ", a las " + HorariosDatos.Hora(despues.Inicio)
                      + " (se podrá elegir desde las " + HorariosDatos.Hora(despues.Inicio - margen) + ").";
            }

            ResultadoCodigo rechazo;
            ReglasCodigo.Elegir(ReglasCodigo.DesdeAgenda(agenda, LaboratorioEquipo.Id, LaboratorioEquipo.Nombre), LaboratorioEquipo.Id, ahora,
                SinConexion.Activo, out rechazo, false);
            return rechazo == null ? "" : rechazo.Mensaje;
        }

        private void CrearPaso2()
        {
            lblPaso2 = TituloPaso("Paso 2. ¿Cómo está este equipo?");

            pnlEstados = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.White };
            rbTodoBien = Chip("Todo bien", true);
            rbFalla = Chip("Reportar una falla", true);
            rbPropio = Chip("Traje mi propio equipo", true);
            foreach (RadioButton estado in new[] { rbTodoBien, rbFalla, rbPropio })
            {
                estado.CheckedChanged += (s, e) =>
                {
                    if (((RadioButton)s).Checked)
                    {
                        MostrarErrorPaso2("");
                        ActualizarPaso2();
                    }
                };
                pnlEstados.Controls.Add(estado);
            }

            lblPista = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10.5F),
                ForeColor = TextoSecundario
            };

            // Una fila por area: "Sin problema" marcado, las fallas de la lista y "Otro".
            pnlFallas = new Panel { BackColor = Color.White, Visible = false };
            foreach (string area in Consultas.AreasDeFalla)
            {
                Label etiqueta = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold),
                    ForeColor = Color.DarkGreen,
                    Text = char.ToUpper(area[0]) + area.Substring(1)
                };

                FlowLayoutPanel chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.White };
                RadioButton ninguna = Chip(SinProblema, false);
                ninguna.Checked = true;
                chips.Controls.Add(ninguna);
                foreach (string opcion in Consultas.OpcionesDeFalla(area))
                {
                    chips.Controls.Add(Chip(opcion, false));
                }

                RadioButton otra = Chip(Otro, false);
                chips.Controls.Add(otra);
                foreach (RadioButton chip in chips.Controls)
                {
                    chip.CheckedChanged += (s, e) =>
                    {
                        if (((RadioButton)s).Checked)
                        {
                            MostrarErrorPaso2("");
                            Colocar();
                        }
                    };
                }

                TextBox texto = new TextBox
                {
                    Font = new DrawingFont("Century Gothic", 11F),
                    MaxLength = 255,
                    Visible = false
                };
                otra.CheckedChanged += (s, e) =>
                {
                    // Colocar decide si se ve la caja (segun el chip "Otro"); aqui solo el foco.
                    Colocar();
                    if (otra.Checked)
                    {
                        texto.Focus();
                    }
                };

                etiquetasArea[area] = etiqueta;
                chipsArea[area] = chips;
                otroArea[area] = texto;
                pnlFallas.Controls.AddRange(new Control[] { etiqueta, chips, texto });
            }

            lblErrorPaso2 = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold),
                ForeColor = Rojo,
                TextAlign = ContentAlignment.MiddleRight
            };

            btnRegistrar = EstiloAdmin.BotonPrincipal("Registrar mi bitácora", Point.Empty, new Size(300, 52));
            btnRegistrar.Font = new DrawingFont("Century Gothic", 13F, FontStyle.Bold);
            btnRegistrar.Click += (s, e) => Registrar();
            // Deshabilitado se ve gris: verde oscuro con letra gris parecia pulsable.
            btnRegistrar.EnabledChanged += (s, e) =>
            {
                Color fondo = btnRegistrar.Enabled ? Color.DarkGreen : Color.FromArgb(205, 210, 205);
                btnRegistrar.BackColor = fondo;
                btnRegistrar.FlatAppearance.BorderColor = fondo;
            };

            // El error y el boton van en la barra de abajo (ver InicializarComponentes).
            columna.Controls.AddRange(new Control[] { lblPaso2, pnlEstados, lblPista, pnlFallas });
        }

        /// <summary>
        /// Paso 3: si alguna computadora de alrededor no tiene internet. Va aparte
        /// de las fallas del paso 2, que son de ESTE equipo: el aviso es de otro.
        /// Es opcional: sin contestar cuenta como "no", asi que "Todo bien" sigue
        /// siendo dos clics. Sirve para que el encargado sepa enseguida que maquina
        /// se quedo sin red (esa no puede avisar: su bitacora llega tarde).
        /// </summary>
        private void CrearPaso3()
        {
            // Opcional a la vista: sin contestar cuenta como "No" (ver arriba).
            lblPaso3 = TituloPaso("Paso 3 (opcional). ¿Alguna computadora de tu alrededor no tiene internet?");
            lblPaso3.Visible = false;

            pnlVecinos = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.White, Visible = false };
            rbVecinosNo = Chip("No", false);
            rbVecinosSi = Chip("Sí, avisar de cuál", false);
            pnlVecinos.Controls.Add(rbVecinosNo);
            pnlVecinos.Controls.Add(rbVecinosSi);
            foreach (RadioButton chip in new[] { rbVecinosNo, rbVecinosSi })
            {
                chip.CheckedChanged += (s, e) =>
                {
                    if (!((RadioButton)s).Checked)
                    {
                        return;
                    }

                    MostrarErrorPaso2("");
                    Colocar();
                    if (s == rbVecinosSi)
                    {
                        if (pnlEquipos.Controls.Count > 0)
                        {
                            // El foco se queda en "Si": en una casilla, la barra espaciadora la marcaria.
                            contenedor.ScrollControlIntoView(txtVecinos);
                            contenedor.ScrollControlIntoView(lblVecinosPista);
                        }
                        else
                        {
                            txtVecinos.Focus();
                            contenedor.ScrollControlIntoView(lblVecinosPista);
                        }
                    }
                };
            }

            pnlEquipos = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.White, Visible = false };

            lblOtroEquipo = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10.5F),
                ForeColor = TextoSecundario,
                Text = "¿No está en la lista? Escribe su número (si son varias, sepáralas con comas):",
                Visible = false
            };

            txtVecinos = new TextBox { Font = new DrawingFont("Century Gothic", 11F), MaxLength = 60, Visible = false };
            txtVecinos.TextChanged += (s, e) => MostrarErrorPaso2("");

            lblVecinosPista = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10.5F),
                ForeColor = TextoSecundario,
                Visible = false
            };

            // En este orden para Tab: el "No / Si", la lista y la caja.
            columna.Controls.AddRange(new Control[] { lblPaso3, pnlVecinos, lblVecinosPista, pnlEquipos, lblOtroEquipo, txtVecinos });
        }

        /// <summary>
        /// La lista para marcar del paso 3: los equipos que registraron bitacoras
        /// en este laboratorio hace poco, sin este. Solo se rehace si cambio y no
        /// hay nada marcado, para no moverle al alumno lo que esta marcando.
        /// </summary>
        private void PonerEquipos(List<string> equipos)
        {
            if (equipos == null)
            {
                return;
            }

            // Esta maquina no sale en su lista. Sin numero configurado se llama como
            // Windows (DESKTOP-...5) y solo vale la misma escritura: si no, taparia
            // a la "5", que es otra.
            string este = LaboratorioEquipo.NumeroMaquina;
            List<string> otros = equipos.Where(e => LaboratorioEquipo.NumeroMaquinaConfigurado
                ? !Consultas.MismoEquipo(e, este)
                : Consultas.ClaveDeEquipo(e) != Consultas.ClaveDeEquipo(este)).ToList();
            string claves = string.Join("|", otros);
            if (claves == clavesEquipos || pnlEquipos.Controls.OfType<CheckBox>().Any(c => c.Checked))
            {
                return;
            }

            clavesEquipos = claves;
            equiposLista = otros;
            pnlEquipos.SuspendLayout();
            foreach (Control viejo in pnlEquipos.Controls.Cast<Control>().ToList())
            {
                viejo.Dispose();
            }

            foreach (string equipo in otros)
            {
                CheckBox casilla = Casilla(equipo);
                casilla.TabStop = false;
                casilla.CheckedChanged += (s, e) => MarcarEquipo();
                pnlEquipos.Controls.Add(casilla);
            }

            PonerParadaDeTab();
            pnlEquipos.ResumeLayout();
            ActualizarTextosPaso3();
            Colocar();
        }

        /// <summary>Hasta MaximoEquiposSinInternet: con esas marcadas, las demas no se dejan marcar.</summary>
        private void MarcarEquipo()
        {
            List<CheckBox> casillas = pnlEquipos.Controls.OfType<CheckBox>().ToList();
            bool lleno = casillas.Count(c => c.Checked) >= Consultas.MaximoEquiposSinInternet;
            foreach (CheckBox casilla in casillas)
            {
                casilla.Enabled = casilla.Checked || !lleno;
            }

            PonerParadaDeTab();
            MostrarErrorPaso2("");
            ActualizarTextosPaso3();
            Colocar();
        }

        /// <summary>
        /// Una sola parada de Tab para toda la lista (dentro, las flechas): la
        /// primera marcada o, si no hay, la primera que se puede marcar. Una
        /// desactivada no recibe el foco, y con ella Tab ya no entraria en la lista.
        /// </summary>
        private void PonerParadaDeTab()
        {
            List<CheckBox> casillas = pnlEquipos.Controls.OfType<CheckBox>().ToList();
            CheckBox parada = casillas.FirstOrDefault(c => c.Checked) ?? casillas.FirstOrDefault(c => c.Enabled);
            foreach (CheckBox casilla in casillas)
            {
                casilla.TabStop = casilla == parada;
            }
        }

        /// <summary>La ayuda del paso 3: marcar en la lista o, sin lista, escribir el numero.</summary>
        private void ActualizarTextosPaso3()
        {
            bool lleno = pnlEquipos.Controls.OfType<CheckBox>().Count(c => c.Checked) >= Consultas.MaximoEquiposSinInternet;
            lblVecinosPista.Text = pnlEquipos.Controls.Count > 0
                ? TextoAyudaLista(lleno)
                : "Escribe el número que tiene pegado; si son varias, sepáralos con comas (por ejemplo: 11, 13)." + FinDeAyudaPaso3();
        }

        /// <summary>La ayuda con lista; "lleno", la de cuando ya hay cinco marcadas.</summary>
        private static string TextoAyudaLista(bool lleno)
        {
            return (lleno
                ? "Ya marcaste " + Consultas.MaximoEquiposSinInternet + ", el máximo: avisa solo de las de tu alrededor."
                : "Márcalas en la lista (hasta " + Consultas.MaximoEquiposSinInternet + ").") + FinDeAyudaPaso3();
        }

        private static string FinDeAyudaPaso3()
        {
            return (LaboratorioEquipo.NumeroMaquinaConfigurado ? " Esta computadora es la " + LaboratorioEquipo.NumeroMaquina + "." : "")
                + (SinConexion.Activo ? " El aviso se enviará con tu bitácora cuando vuelva la conexión." : "");
        }

        private static Label TituloPaso(string texto)
        {
            return new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 15F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Text = texto
            };
        }

        /// <summary>Opcion que se marca como un boton: verde relleno cuando esta elegida.</summary>
        private static RadioButton Chip(string texto, bool grande)
        {
            RadioButton chip = new RadioButton
            {
                Appearance = Appearance.Button,
                AutoSize = true,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", grande ? 12.5F : 10.5F, grande ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = Color.DarkGreen,
                Margin = new Padding(0, 0, grande ? 12 : 8, 8),
                MinimumSize = grande ? new Size(210, 54) : new Size(0, 34),
                Padding = grande ? new Padding(16, 6, 16, 6) : new Padding(8, 2, 8, 2),
                Text = texto,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false
            };
            chip.FlatAppearance.BorderSize = grande ? 2 : 1;
            PintarMarcado(chip, false);
            chip.CheckedChanged += (s, e) => PintarMarcado(chip, chip.Checked);
            // Tambien cuando se apaga su panel (el paso 2 antes del codigo).
            chip.EnabledChanged += (s, e) => PintarMarcado(chip, chip.Checked);
            chip.Paint += PintarLetraApagada;
            return chip;
        }

        /// <summary>Un equipo para marcar en el paso 3: como un chip, pero se marcan varios.</summary>
        private static CheckBox Casilla(string equipo)
        {
            DrawingFont letra = new DrawingFont("Century Gothic", 10.5F);
            // El ancho se mide: con AutoSize, un CheckBox con forma de boton se queda
            // corto y parte el nombre ("DATABASE-05" salia "DATABASE-"). Como minimo,
            // uno comodo de pulsar, aunque el nombre sea solo un numero.
            int ancho = Math.Max(76, TextRenderer.MeasureText(equipo, letra).Width + 30);
            CheckBox casilla = new CheckBox
            {
                Appearance = Appearance.Button,
                AutoSize = false,
                Size = new Size(ancho, 34),
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = letra,
                ForeColor = Color.DarkGreen,
                Margin = new Padding(0, 0, 8, 8),
                Padding = new Padding(8, 2, 8, 2),
                Text = equipo,
                TextAlign = ContentAlignment.MiddleCenter,
                // Un '&' no subraya la letra siguiente.
                UseMnemonic = false,
                UseVisualStyleBackColor = false,
                AccessibleName = "Computadora " + equipo,
                Tag = equipo
            };
            casilla.FlatAppearance.BorderSize = 1;
            PintarMarcado(casilla, false);
            casilla.CheckedChanged += (s, e) => PintarMarcado(casilla, casilla.Checked);
            casilla.EnabledChanged += (s, e) => PintarMarcado(casilla, casilla.Checked);
            casilla.Paint += PintarLetraApagada;
            return casilla;
        }

        /// <summary>
        /// Letra blanca sobre verde si esta marcado. Con el raton encima se pinta
        /// MouseOverBackColor aunque este marcado: con el verde claro de siempre, la
        /// letra blanca del recien pulsado no se leia hasta mover el raton. Apagado
        /// (sin codigo aceptado, o la sexta casilla del paso 3), borde y letra
        /// grises: con el borde verde parecia que se podia pulsar.
        /// </summary>
        private static void PintarMarcado(ButtonBase boton, bool marcado)
        {
            bool activo = boton.Enabled;
            boton.ForeColor = marcado ? Color.White : activo ? Color.DarkGreen : Apagado;
            boton.FlatAppearance.BorderColor = activo ? Color.DarkGreen : BordeApagado;
            boton.FlatAppearance.CheckedBackColor = activo ? Color.DarkGreen : BordeApagado;
            boton.FlatAppearance.MouseOverBackColor = !activo ? Color.White
                : marcado ? Color.FromArgb(0, 120, 0) : Color.FromArgb(236, 246, 236);
        }

        /// <summary>
        /// Apagado, Windows pinta la letra de un boton plano con un gris oscuro
        /// sacado del fondo, sin mirar ForeColor, y casi no se distinguia de la de
        /// siempre: encima se repinta con el gris de los titulos apagados. Si esta
        /// marcado, sobre el gris claro de "Registrar mi bitacora" apagado.
        /// </summary>
        private static void PintarLetraApagada(object sender, PaintEventArgs e)
        {
            ButtonBase boton = (ButtonBase)sender;
            if (boton.Enabled)
            {
                return;
            }

            RadioButton opcion = boton as RadioButton;
            CheckBox casilla = boton as CheckBox;
            bool marcado = (opcion != null && opcion.Checked) || (casilla != null && casilla.Checked);
            int borde = boton.FlatAppearance.BorderSize;
            Rectangle dentro = new Rectangle(borde, borde, boton.Width - 2 * borde, boton.Height - 2 * borde);
            using (SolidBrush fondo = new SolidBrush(marcado ? BordeApagado : boton.BackColor))
            {
                e.Graphics.FillRectangle(fondo, dentro);
            }

            TextRenderer.DrawText(e.Graphics, boton.Text, boton.Font, dentro, marcado ? Color.FromArgb(90, 90, 90) : Apagado,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        /// <summary>Coloca todo de arriba abajo en una columna centrada; lo que no toca, se oculta.</summary>
        private void Colocar()
        {
            if (columna == null || contenedor == null)
            {
                return;
            }

            int ancho = Math.Min(AnchoMaximo, Math.Max(560, contenedor.ClientSize.Width - 60));
            int y = 0;

            // Con clase, la ficha (FichaClaseAlumno) llena la franja; sin ella, las dos etiquetas.
            pnlClase.SetBounds(0, y, ancho, fichaClase.Visible ? fichaClase.AltoNecesario : 84);
            lblClaseTitulo.SetBounds(18, 12, ancho - 36, 26);
            lblClaseDetalle.SetBounds(18, 40, ancho - 36, 40);
            y += pnlClase.Height + 30;

            // --- Paso 1 ---
            lblPaso1.SetBounds(0, y, ancho, 32);
            y += 40;
            bool pidiendoCodigo = codigoAceptado == null;
            bool conCodigo = PideCodigo;
            lblPaso1Ayuda.Visible = pidiendoCodigo && lblPaso1Ayuda.Text.Length > 0;
            if (lblPaso1Ayuda.Visible)
            {
                int altoAyuda = TextRenderer.MeasureText(lblPaso1Ayuda.Text, lblPaso1Ayuda.Font, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height;
                lblPaso1Ayuda.SetBounds(0, y - 6, ancho, altoAyuda + 2);
                y += altoAyuda + 4;
            }

            txtCodigo.Visible = pidiendoCodigo && conCodigo;
            btnContinuar.Visible = pidiendoCodigo && conCodigo;
            pnlClases.Visible = pidiendoCodigo && !conCodigo;
            lblProxima.Visible = pidiendoCodigo && !conCodigo && lblProxima.Text.Length > 0;
            lblCodigoOk.Visible = !pidiendoCodigo;
            lnkOtroCodigo.Visible = !pidiendoCodigo && !registrado;
            if (pidiendoCodigo && !conCodigo)
            {
                // Sin codigo: los botones de las clases abiertas y, debajo, la siguiente.
                int altoBotones = 0;
                foreach (Control boton in pnlClases.Controls)
                {
                    boton.Size = new Size(ancho, 62);
                    altoBotones += boton.Height + boton.Margin.Vertical;
                }

                pnlClases.SetBounds(0, y, ancho, altoBotones);
                y += altoBotones;
                if (lblProxima.Visible)
                {
                    int altoProxima = TextRenderer.MeasureText(lblProxima.Text, lblProxima.Font, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height;
                    lblProxima.SetBounds(0, y, ancho, altoProxima + 4);
                    y += altoProxima + 8;
                }

                bool hayErrorClase = lblErrorCodigo.Text.Length > 0;
                lblErrorCodigo.Visible = hayErrorClase;
                if (hayErrorClase)
                {
                    int alto = TextRenderer.MeasureText(lblErrorCodigo.Text, lblErrorCodigo.Font, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height;
                    lblErrorCodigo.SetBounds(0, y, ancho, alto + 4);
                    y += alto + 8;
                }
            }
            else if (pidiendoCodigo)
            {
                txtCodigo.Location = new Point(0, y);
                btnContinuar.SetBounds(txtCodigo.Right + 12, y, btnContinuar.Width, txtCodigo.Height);
                y += txtCodigo.Height + 8;
                bool hayError = lblErrorCodigo.Text.Length > 0;
                lblErrorCodigo.Visible = hayError;
                if (hayError)
                {
                    int alto = TextRenderer.MeasureText(lblErrorCodigo.Text, lblErrorCodigo.Font, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height;
                    lblErrorCodigo.SetBounds(0, y, ancho, alto + 4);
                    y += alto + 8;
                }
            }
            else
            {
                lblErrorCodigo.Visible = false;
                lblCodigoOk.SetBounds(0, y, ancho, 28);
                y += 32;
                if (lnkOtroCodigo.Visible)
                {
                    lnkOtroCodigo.Location = new Point(0, y);
                    y += 26;
                }
            }

            y += 26;

            // --- Paso 2, o el "listo" cuando ya se registro ---
            foreach (Control control in new Control[] { lblPaso2, pnlEstados, lblPista, btnRegistrar })
            {
                control.Visible = !registrado;
            }

            lblListo.Visible = registrado;
            lblListoDetalle.Visible = registrado;
            lblErrorPaso2.Visible = !registrado && lblErrorPaso2.Text.Length > 0;
            if (registrado)
            {
                pnlFallas.Visible = false;
                lblPaso3.Visible = false;
                pnlVecinos.Visible = false;
                pnlEquipos.Visible = false;
                lblOtroEquipo.Visible = false;
                txtVecinos.Visible = false;
                lblVecinosPista.Visible = false;
                lblListo.SetBounds(0, y, ancho, 38);
                y += 44;
                int altoDetalle = TextRenderer.MeasureText(lblListoDetalle.Text, lblListoDetalle.Font, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height;
                lblListoDetalle.SetBounds(0, y, ancho, altoDetalle + 6);
                y += altoDetalle + 12;
            }
            else
            {
                lblPaso2.SetBounds(0, y, ancho, 32);
                y += 42;
                pnlEstados.MaximumSize = new Size(ancho, 0);
                pnlEstados.Location = new Point(0, y);
                pnlEstados.PerformLayout();
                y += pnlEstados.Height + 2;
                lblPista.SetBounds(0, y, ancho, 24);
                y += 32;

                pnlFallas.Visible = rbFalla.Checked;
                if (pnlFallas.Visible)
                {
                    int yf = 0;
                    const int anchoEtiqueta = 110;
                    foreach (string area in Consultas.AreasDeFalla)
                    {
                        etiquetasArea[area].SetBounds(0, yf + 6, anchoEtiqueta, 26);
                        FlowLayoutPanel chips = chipsArea[area];
                        chips.MaximumSize = new Size(ancho - anchoEtiqueta, 0);
                        chips.Location = new Point(anchoEtiqueta, yf);
                        chips.PerformLayout();
                        yf += chips.Height;
                        TextBox texto = otroArea[area];
                        texto.Visible = chips.Controls.OfType<RadioButton>().Any(c => c.Checked && c.Text == Otro);
                        if (texto.Visible)
                        {
                            texto.SetBounds(anchoEtiqueta, yf, ancho - anchoEtiqueta, texto.Height);
                            yf += texto.Height + 8;
                        }

                        yf += 10;
                    }

                    pnlFallas.SetBounds(0, y, ancho, yf);
                    y += yf + 4;
                }

                // --- Paso 3 ---
                lblPaso3.Visible = preguntarVecinos;
                pnlVecinos.Visible = preguntarVecinos;
                bool avisando = preguntarVecinos && rbVecinosSi.Checked;
                bool conLista = avisando && pnlEquipos.Controls.Count > 0;
                pnlEquipos.Visible = conLista;
                lblOtroEquipo.Visible = conLista;
                txtVecinos.Visible = avisando;
                lblVecinosPista.Visible = avisando;
                if (preguntarVecinos)
                {
                    y += 16;
                    // Con "(opcional)" el titulo puede no caber en una linea: pasa a dos.
                    int altoPaso3 = Math.Max(32, TextRenderer.MeasureText(lblPaso3.Text, lblPaso3.Font, new Size(ancho, int.MaxValue),
                        TextFormatFlags.WordBreak).Height + 4);
                    lblPaso3.SetBounds(0, y, ancho, altoPaso3);
                    y += altoPaso3 + 10;
                    pnlVecinos.MaximumSize = new Size(ancho, 0);
                    pnlVecinos.Location = new Point(0, y);
                    pnlVecinos.PerformLayout();
                    y += pnlVecinos.Height + 2;
                    int altoPista = avisando
                        ? TextRenderer.MeasureText(lblVecinosPista.Text, lblVecinosPista.Font, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height
                        : 0;
                    if (conLista)
                    {
                        // Que marque (la ayuda), la lista, y la caja para una que no este.
                        // La ayuda con el alto de la mas larga de sus dos versiones: cambia
                        // al marcar la quinta, y la lista no debe moverse bajo el puntero.
                        Size limite = new Size(ancho, int.MaxValue);
                        int altoAyuda = Math.Max(
                            TextRenderer.MeasureText(TextoAyudaLista(false), lblVecinosPista.Font, limite, TextFormatFlags.WordBreak).Height,
                            TextRenderer.MeasureText(TextoAyudaLista(true), lblVecinosPista.Font, limite, TextFormatFlags.WordBreak).Height);
                        lblVecinosPista.SetBounds(0, y, ancho, altoAyuda + 4);
                        y += altoAyuda + 10;
                        pnlEquipos.MaximumSize = new Size(ancho, 0);
                        pnlEquipos.Location = new Point(0, y);
                        pnlEquipos.PerformLayout();
                        y += pnlEquipos.Height + 6;
                        int altoOtro = TextRenderer.MeasureText(lblOtroEquipo.Text, lblOtroEquipo.Font, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height;
                        lblOtroEquipo.SetBounds(0, y, ancho, altoOtro + 4);
                        y += altoOtro + 6;
                        txtVecinos.SetBounds(0, y, Math.Min(420, ancho), txtVecinos.Height);
                        y += txtVecinos.Height + 12;
                    }
                    else if (avisando)
                    {
                        txtVecinos.SetBounds(0, y, Math.Min(420, ancho), txtVecinos.Height);
                        y += txtVecinos.Height + 6;
                        lblVecinosPista.SetBounds(0, y, ancho, altoPista + 4);
                        y += altoPista + 12;
                    }
                }
            }

            y += 20;
            columna.Size = new Size(ancho, y);
            int x = Math.Max(20, (contenedor.ClientSize.Width - ancho) / 2);
            columna.Location = new Point(x + contenedor.AutoScrollPosition.X, 28 + contenedor.AutoScrollPosition.Y);

            // La barra de abajo, alineada con la columna: el perfil a la izquierda,
            // el boton a la derecha y, pegado a el, lo que falte para registrar.
            if (pie != null)
            {
                btnRegistrar.Location = new Point(x + ancho - btnRegistrar.Width, (pie.Height - btnRegistrar.Height) / 2);
                lnkPerfil.Location = new Point(x, (pie.Height - lnkPerfil.Height) / 2);
                int inicioError = lnkPerfil.Right + 20;
                lblErrorPaso2.SetBounds(inicioError, 0, Math.Max(50, btnRegistrar.Left - 16 - inicioError), pie.Height);
            }

            // Al angostar la ventana (de 1920 a 1366) Windows calcula las barras antes
            // de llegar aqui, con la columna aun donde estaba, y dejaba la horizontal:
            // si ya cabe a lo ancho, se vuelven a calcular. Va al final: si al quitar la
            // horizontal sobra tambien la vertical, Resize vuelve a llamar a Colocar con
            // el ancho nuevo, y esa llamada debe ser la ultima en colocar la barra de abajo.
            if (contenedor.HorizontalScroll.Visible && x + ancho <= contenedor.ClientSize.Width && !recalculandoBarras)
            {
                recalculandoBarras = true;
                try
                {
                    contenedor.PerformLayout();
                }
                finally
                {
                    recalculandoBarras = false;
                }
            }
        }

        // =====================================================================
        // Paso 1: el codigo
        // =====================================================================

        private void MostrarErrorCodigo(string texto)
        {
            if (lblErrorCodigo.Text == texto)
            {
                return;
            }

            lblErrorCodigo.Text = texto;
            Colocar();
        }

        private void Continuar()
        {
            string codigo = txtCodigo.Text.Trim();
            if (codigo.Length == 0)
            {
                MostrarErrorCodigo("Escribe el código que te dicta tu docente.");
                txtCodigo.Focus();
                return;
            }

            // El codigo se resuelve para ESTE laboratorio: sin laboratorio no hay nada que hacer.
            if (!LaboratorioEquipo.Resuelto)
            {
                MostrarErrorCodigo("Este equipo no tiene laboratorio asignado. Avisa a la persona responsable del laboratorio.");
                return;
            }

            Cursor = Cursors.WaitCursor;
            btnContinuar.Enabled = false;
            try
            {
                ResultadoCodigo resultado;
                SesionLocal sesionLocal = null;
                if (SinConexion.Activo)
                {
                    resultado = SinConexion.Cache.ResolverCodigo(codigo, matricula, out sesionLocal);
                }
                else
                {
                    try
                    {
                        int idUsuario = consultas.IdDeUsuario(matricula);
                        if (idUsuario == 0)
                        {
                            MostrarErrorCodigo("Tu matrícula no está registrada. Avisa a la persona responsable del laboratorio.");
                            return;
                        }

                        resultado = new HorariosDatos().ResolverCodigo(codigo, LaboratorioEquipo.Id, idUsuario, matricula);
                    }
                    catch (Exception ex)
                    {
                        // Fase 4: si el servidor se cayo a media manana, se sigue con la copia local.
                        Log.Error("Validar codigo de acceso '" + codigo + "'", ex);
                        if (!SinConexion.IntentarActivar(ex.Message))
                        {
                            MostrarErrorCodigo(SinCopiaQueSirva(ex, "No se pudo validar el código"));
                            return;
                        }

                        resultado = SinConexion.Cache.ResolverCodigo(codigo, matricula, out sesionLocal);
                        ActualizarDatos();
                    }
                }

                // Quien ya registro y vuelve durante la misma clase (se fue la luz,
                // se reinicio el equipo) no es un error: puede seguir usandolo.
                if (resultado.Motivo == MotivoCodigo.YaRegistrado && resultado.Ventana != null
                    && OfrecerReanudar(resultado.Ventana, sesionLocal))
                {
                    return;
                }

                if (!resultado.Aceptado)
                {
                    Log.Info("Codigo '" + codigo + "' rechazado (" + resultado.Motivo + ") en " + LaboratorioEquipo.Nombre + ".");
                    MostrarErrorCodigo(resultado.Mensaje);
                    if (resultado.Motivo == MotivoCodigo.ModoCambiado || resultado.Modo != modo)
                    {
                        // El laboratorio cambio de modo, o sin servidor hay que elegir
                        // la clase: el paso 1 se rehace.
                        ActualizarDatos();
                    }

                    if (txtCodigo.Visible)
                    {
                        txtCodigo.SelectAll();
                        txtCodigo.Focus();
                    }

                    return;
                }

                Aceptar(resultado.Ventana, sesionLocal, codigo);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnContinuar.Enabled = true;
            }
        }

        /// <summary>
        /// Sin codigo (o con codigos generados y sin servidor): el alumno pulso su
        /// clase, o se eligio sola por ser la unica abierta (sola). El servidor (o
        /// la copia) vuelve a comprobar que sigue abierta; con la respuesta se
        /// acepta como un codigo.
        /// </summary>
        private void ElegirClase(SesionAgenda clase, bool sola = false)
        {
            // Elegida (sola o a mano): la eleccion automatica no vuelve sobre ella.
            elegidasSolas.Add(ClaveDe(clase));
            Resolver("Clase " + clase.Descripcion,
                idUsuario => new HorariosDatos().ResolverClase(LaboratorioEquipo.Id, idUsuario, matricula, clase.FranjaId, clase.ExcepcionId),
                (out SesionLocal sesionLocal) => SinConexion.Cache.ResolverClase(clase.FranjaId, clase.ExcepcionId, matricula, out sesionLocal),
                sola, false);
        }

        /// <summary>
        /// Sin codigos y sin ninguna clase abierta (db/14): la bitacora queda como
        /// uso libre del laboratorio. El servidor (o la copia) vuelve a comprobar
        /// que no hay clase; si empezo una, manda la clase.
        /// </summary>
        private void ElegirUsoLibre(bool sola = false)
        {
            elegidasSolas.Add(ClaveLibre(tramoLibre));
            Resolver("Uso libre",
                idUsuario => new HorariosDatos().ResolverUsoLibre(LaboratorioEquipo.Id, idUsuario, matricula),
                (out SesionLocal sesionLocal) => SinConexion.Cache.ResolverUsoLibre(matricula, out sesionLocal),
                sola, true);
        }

        private delegate ResultadoCodigo ResolucionSinServidor(out SesionLocal sesionLocal);

        /// <summary>
        /// Lo comun de elegir clase y uso libre: con el servidor (y si se cayo, con
        /// la copia local), reanudar si ya registro, el rechazo, o aceptarla.
        /// </summary>
        private void Resolver(string que, Func<int, ResultadoCodigo> conServidor, ResolucionSinServidor sinServidor, bool sola, bool usoLibre)
        {
            if (!LaboratorioEquipo.Resuelto)
            {
                MostrarErrorCodigo("Este equipo no tiene laboratorio asignado. Avisa a la persona responsable del laboratorio.");
                return;
            }

            Cursor = Cursors.WaitCursor;
            // Al desactivar la lista, Windows pasa su foco al siguiente control ("Mi
            // perfil"): si no se acepta, se le devuelve al boton de esa clase.
            string claveConFoco = ClaveConFoco();
            pnlClases.Enabled = false;
            try
            {
                ResultadoCodigo resultado;
                SesionLocal sesionLocal = null;
                if (SinConexion.Activo)
                {
                    resultado = sinServidor(out sesionLocal);
                }
                else
                {
                    try
                    {
                        int idUsuario = consultas.IdDeUsuario(matricula);
                        if (idUsuario == 0)
                        {
                            MostrarErrorCodigo("Tu matrícula no está registrada. Avisa a la persona responsable del laboratorio.");
                            return;
                        }

                        resultado = conServidor(idUsuario);
                    }
                    catch (Exception ex)
                    {
                        // Fase 4: sin servidor se sigue con la copia local.
                        Log.Error("Elegir: " + que, ex);
                        if (!SinConexion.IntentarActivar(ex.Message))
                        {
                            MostrarErrorCodigo(SinCopiaQueSirva(ex, usoLibre ? "No se pudo registrar el uso libre" : "No se pudo registrar tu clase"));
                            return;
                        }

                        resultado = sinServidor(out sesionLocal);
                        ActualizarDatos();
                    }
                }

                if (resultado.Motivo == MotivoCodigo.YaRegistrado && resultado.Ventana != null
                    && OfrecerReanudar(resultado.Ventana, sesionLocal))
                {
                    return;
                }

                if (!resultado.Aceptado)
                {
                    Log.Info(que + " rechazada (" + resultado.Motivo + ") en " + LaboratorioEquipo.Nombre + ".");
                    MostrarErrorCodigo(resultado.Mensaje);
                    if (resultado.Motivo == MotivoCodigo.ElegirClase && resultado.Opciones != null)
                    {
                        PonerClases(resultado.Opciones);
                        Colocar();
                    }

                    // Sin uso libre (empezo una clase, o ya no hay db/14) la pantalla se
                    // rehace con lo que hay ahora.
                    if (usoLibre || resultado.Motivo != MotivoCodigo.ElegirClase || resultado.Modo != modo)
                    {
                        ActualizarDatos();
                    }

                    return;
                }

                Aceptar(resultado.Ventana, sesionLocal, null, sola, usoLibre);
            }
            finally
            {
                Cursor = Cursors.Default;
                pnlClases.Enabled = true;
                if (codigoAceptado == null)
                {
                    EnfocarClase(claveConFoco);
                }
            }
        }

        /// <summary>
        /// Por que no se pudo y que hacer, cuando el servidor fallo y la copia local
        /// tampoco sirvio (no hay, es de otro laboratorio o es muy vieja). Sin red,
        /// reintentar no arregla nada: queda avisar. El detalle ya esta en el log.
        /// </summary>
        private static string SinCopiaQueSirva(Exception ex, string queNoSePudo)
        {
            return MensajeDeFallo.EsDeRed(ex)
                ? queNoSePudo + ": no hay conexión con el servidor y este equipo no tiene una copia del horario que sirva. "
                  + "Avisa a la persona responsable del laboratorio."
                : MensajeDeFallo.Explicar(queNoSePudo, ex, MensajeDeFallo.AvisaAlLaboratorio);
        }

        private static string ClaveDe(SesionAgenda clase)
        {
            return clase.EsExtra ? "x" + clase.ExcepcionId : "f" + clase.FranjaId;
        }

        /// <summary>
        /// El tramo por su inicio y su fin: si se cancela la clase siguiente, el
        /// tramo crece y se puede volver a elegir solo cuando venza el uso libre.
        /// </summary>
        private static string ClaveLibre(TramoLibre tramo)
        {
            return "l" + (tramo == null ? "" : HorariosDatos.Hora(tramo.Inicio) + "-" + HorariosDatos.Hora(tramo.Fin));
        }

        /// <summary>
        /// Sin codigos: elige sola la unica clase abierta (o, sin ninguna, el uso
        /// libre), con un Timer y no al momento, para que antes se pinte la
        /// pantalla con su modo (Windows atiende los WM_TIMER despues de los
        /// WM_PAINT; con un BeginInvoke la consulta al servidor dejaba a la vista
        /// el paso 1 de antes). No con la pantalla escondida (ya se libero el
        /// equipo) ni con otra ventana suya delante (Mi perfil, un aviso): en ese
        /// caso no se gasta, y el siguiente refresco lo vuelve a intentar.
        /// </summary>
        private void ElegirSolaEnUnMomento(string clave, Action elegir)
        {
            if (eleccionPendiente != null || !SePuedeElegirSola(clave))
            {
                return;
            }

            eleccionPendiente = new Timer { Interval = 50 };
            eleccionPendiente.Tick += (s, e) =>
            {
                eleccionPendiente.Stop();
                eleccionPendiente.Dispose();
                eleccionPendiente = null;
                if (!IsDisposed && modo == ModoCodigo.SinCodigo && SePuedeElegirSola(clave))
                {
                    elegir();
                }
            };
            eleccionPendiente.Start();
        }

        private bool SePuedeElegirSola(string clave)
        {
            return codigoAceptado == null && !eligeElAlumno && Visible && !OwnedForms.Any(f => f.Visible)
                && !elegidasSolas.Contains(clave);
        }

        /// <summary>
        /// Le devuelve el equipo a quien ya registro y sigue en clase. True si se
        /// atendio el caso (reanudo, o dijo que no); false para mostrar el aviso de
        /// siempre. La hora es la del SERVIDOR, como en todo lo demas.
        /// </summary>
        private bool OfrecerReanudar(VentanaCodigo ventanaRegistrada, SesionLocal sesionLocal)
        {
            // El codigo de la SESION (nunca vacio): el traspaso y la liberacion lo usan.
            string codigo = ventanaRegistrada.Codigo;
            if (ventanaRegistrada.AhoraServidor >= ventanaRegistrada.Fin || !Kiosco.Activo)
            {
                // La clase ya termino, o fuera del kiosco no hay equipo que liberar.
                return false;
            }

            bool libre = ventanaRegistrada.EsUsoLibre;
            if (!DialogoConfirmacion.Preguntar(this, libre ? "Continuar con el uso libre" : "Continuar en esta clase",
                libre ? "Ya registraste tu bitácora de uso libre." : "Ya registraste tu bitácora en esta clase.",
                "Tu primer registro es el que cuenta y se queda como está. Puedes volver a usar este equipo "
                    + HastaLas(ventanaRegistrada.Fin.TimeOfDay) + ".",
                "Volver a usar el equipo", false))
            {
                Log.Info("Se ofrecio reanudar la clase de " + matricula + " con el codigo " + codigo + " y dijo que no.");
                return true;
            }

            Log.Info("Reanudando la clase de " + matricula + " con el codigo " + codigo + " hasta "
                + ventanaRegistrada.Fin.ToString("HH:mm") + ": ya tenia bitacora de esta sesion.");
            Datos_User.Setcodigo(codigo);
            Datos_User.VentanaActual = ventanaRegistrada;
            Datos_User.SesionSinConexion = sesionLocal;
            SesionEquipo.Liberar(ventanaRegistrada, matricula, codigo, true);
            return true;
        }

        /// <param name="tecleado">Lo que escribio el alumno, o null si eligio la clase de la lista.</param>
        /// <param name="sola">Sin codigos: era la unica clase abierta (o el uso libre) y se eligio sola.</param>
        /// <param name="usoLibre">Sin codigos y sin clase: uso libre del laboratorio (db/14).</param>
        private void Aceptar(VentanaCodigo aceptada, SesionLocal sesionLocal, string tecleado, bool sola = false, bool usoLibre = false)
        {
            // El codigo de la SESION, no lo tecleado: sin codigo no hay nada tecleado,
            // y el traspaso al vigilante y la liberacion necesitan uno.
            codigoAceptado = aceptada.Codigo;
            ventana = aceptada;
            // Lo leen consultaFinalSesion (sin conexion) y la liberacion del equipo.
            Datos_User.Setcodigo(aceptada.Codigo);
            Datos_User.VentanaActual = aceptada;
            Datos_User.SesionSinConexion = sesionLocal;
            Log.Info((usoLibre ? "Uso libre" : tecleado == null ? "Clase elegida de la lista" : "Codigo '" + tecleado + "' aceptado") + " para " + matricula + " en "
                + LaboratorioEquipo.Nombre + " (sesion " + aceptada.IdCodigo + ", hasta " + aceptada.Fin.ToString("HH:mm") + ").");

            if (usoLibre)
            {
                lblCodigoOk.Text = "Uso libre del laboratorio (sin clase), " + HastaLas(aceptada.Fin.TimeOfDay) + ".";
                lnkOtroCodigo.Text = "Volver a elegir";
                MostrarClase("Uso libre: ahora no hay clase. Puedes usar el equipo " + HastaLas(aceptada.Fin.TimeOfDay) + ".", true);
            }
            else
            {
                lblCodigoOk.Text = (tecleado != null ? "Código " + tecleado + " aceptado: " : sola ? "Tu clase (la única de ahora en este laboratorio): " : "Tu clase: ")
                    + aceptada.Materia + ", " + aceptada.Grupo + ", hasta las " + aceptada.Fin.ToString("HH:mm") + ".";
                lnkOtroCodigo.Text = tecleado == null ? "Elegir otra clase" : "Usar otro código";
                MostrarFicha(aceptada.Materia, aceptada.Grupo, aceptada.Profesor, aceptada.Inicio.TimeOfDay, aceptada.Fin.TimeOfDay, true, false,
                    null, AspectoDe(aceptada.IdProfesor, aceptada.IdMateria, aceptada.IdGrupo));
            }

            // El foco pasa al enlace ANTES de esconder la caja del codigo: si no,
            // Windows se lo daria a "Todo bien" y un RadioButton que recibe el foco
            // sin raton ni Tab se marca solo; el alumno debe elegir a proposito.
            lnkOtroCodigo.Visible = true;
            lnkOtroCodigo.Focus();
            AcceptButton = null;
            ActualizarPaso2();
        }

        private void OlvidarCodigo()
        {
            // Quiere elegir el: la clase unica ya no se elige sola.
            eligeElAlumno = true;
            codigoAceptado = null;
            ventana = null;
            Datos_User.VentanaActual = null;
            Datos_User.SesionSinConexion = null;
            TextosPaso1();
            MostrarErrorCodigo("");
            // Un error del paso 2 o 3 pediria algo que ahora no se puede tocar.
            MostrarErrorPaso2("");
            ActualizarPaso2();
            ActualizarDatos();
            if (PideCodigo)
            {
                txtCodigo.SelectAll();
                txtCodigo.Focus();
            }
            else
            {
                // Ningun boton de clase con el foco: Enter no debe elegir.
                ActiveControl = null;
            }
        }

        // =====================================================================
        // Paso 2: como esta el equipo
        // =====================================================================

        private void ActualizarPaso2()
        {
            bool activo = codigoAceptado != null;
            lblPaso2.ForeColor = activo ? Color.DarkGreen : Apagado;
            pnlEstados.Enabled = activo;
            pnlFallas.Enabled = activo;
            btnRegistrar.Enabled = activo && (rbTodoBien.Checked || rbFalla.Checked || rbPropio.Checked);

            lblPaso3.ForeColor = activo ? Color.DarkGreen : Apagado;
            pnlVecinos.Enabled = activo;
            pnlEquipos.Enabled = activo;
            txtVecinos.Enabled = activo;
            ActualizarTextosPaso3();

            string equipo = LaboratorioEquipo.NumeroMaquina;
            lblPista.Text = !activo ? (PideCodigo ? "Primero escribe el código de tu clase." : "Primero elige tu clase.")
                : rbTodoBien.Checked ? "Se registra sin fallas y el equipo " + equipo + " queda para ti hasta las " + ventana.Fin.ToString("HH:mm") + "."
                : rbFalla.Checked ? "Marca lo que falla en cada parte; la persona responsable del laboratorio lo revisará."
                : rbPropio.Checked ? "No usarás este equipo: al registrar podrás apagarlo o dejarlo listo para otra persona."
                : "Elige una opción; después pulsa «Registrar mi bitácora».";
            Colocar();
        }

        private void MostrarErrorPaso2(string texto)
        {
            if (lblErrorPaso2.Text == texto)
            {
                return;
            }

            lblErrorPaso2.Text = texto;
            Colocar();
        }

        /// <summary>
        /// Falla y comentario de un area, como los guardaba Registro_Bitacora:
        /// "Ninguno" sin problema, el texto de la lista, u "Otro" con lo que
        /// escribio ("Otro (sin detalle)" si no escribio nada).
        /// </summary>
        private void LeerArea(string area, out string falla, out string comentario)
        {
            RadioButton elegida = chipsArea[area].Controls.OfType<RadioButton>().FirstOrDefault(c => c.Checked);
            string texto = elegida == null ? SinProblema : elegida.Text;
            comentario = "";
            if (texto == SinProblema)
            {
                falla = "Ninguno";
            }
            else if (texto == Otro)
            {
                falla = "Otro";
                comentario = otroArea[area].Text.Trim();
                if (comentario.Length == 0)
                {
                    comentario = "Otro (sin detalle)";
                }
            }
            else
            {
                falla = texto;
            }
        }

        private void Registrar()
        {
            if (ventana == null || registrado)
            {
                return;
            }

            string[] valores;
            if (rbTodoBien.Checked)
            {
                valores = new[] { "Ninguno", "Ninguno", "Ninguno", "Ninguno", "Ninguno", "Ninguno" };
            }
            else if (rbPropio.Checked)
            {
                // Trajo su computadora: no hay nada que reportar de esta maquina.
                valores = new[] { "EP", "Equipo Propio", "EP", "Equipo Propio", "EP", "Equipo Propio" };
            }
            else if (rbFalla.Checked)
            {
                valores = new string[6];
                for (int i = 0; i < Consultas.AreasDeFalla.Length; i++)
                {
                    LeerArea(Consultas.AreasDeFalla[i], out valores[2 * i], out valores[2 * i + 1]);
                }

                if (valores.Where((v, i) => i % 2 == 0).All(v => v == "Ninguno"))
                {
                    MostrarErrorPaso2("Marca qué falla tiene el equipo, o elige «Todo bien».");
                    return;
                }
            }
            else
            {
                MostrarErrorPaso2("Elige cómo está el equipo.");
                return;
            }

            // Paso 3: sin contestar, o "No", no avisa de nada. Con "Si", lo marcado
            // en la lista y lo escrito en la caja.
            string equiposSinInternet = null;
            if (preguntarVecinos && rbVecinosSi.Checked)
            {
                string error;
                List<string> marcados = pnlEquipos.Controls.OfType<CheckBox>().Where(c => c.Checked).Select(c => (string)c.Tag).ToList();
                // Sin numero configurado, el de la bitacora es el nombre de Windows
                // (DESKTOP-A7F3K2): no sirve de formato ni para reconocer esta.
                equiposSinInternet = Consultas.UnirEquiposSinInternet(marcados, equiposLista, txtVecinos.Text,
                    LaboratorioEquipo.NumeroMaquinaConfigurado ? LaboratorioEquipo.NumeroMaquina : null, out error);
                if (error != null)
                {
                    MostrarErrorPaso2(error);
                    // Donde esta lo que hay que corregir: lo escrito, o la lista si no escribio nada.
                    if (txtVecinos.Text.Trim().Length == 0 && pnlEquipos.Controls.Count > 0)
                    {
                        contenedor.ScrollControlIntoView(pnlEquipos);
                    }
                    else
                    {
                        contenedor.ScrollControlIntoView(txtVecinos);
                        txtVecinos.Focus();
                    }

                    return;
                }
            }

            // El uso libre vencio con la pantalla abierta: casi siempre porque se abrio
            // la siguiente clase, pero tambien al acabar el dia o si el tramo cambio.
            // Se suelta (sin perder lo marcado en los pasos 2 y 3) y el refresco
            // elige solo lo que toque ahora: la clase que empezo o el uso libre del
            // tramo nuevo (otra clave, ClaveLibre); con dos clases, el alumno.
            if (ventana.EsUsoLibre && ventana.Restante <= TimeSpan.Zero)
            {
                Log.Info("Uso libre de " + matricula + " vencido antes de registrar (hasta " + ventana.Fin.ToString("HH:mm") + "): se vuelve a elegir.");
                string fin = ventana.Fin.ToString("HH:mm");
                codigoAceptado = null;
                ventana = null;
                Datos_User.VentanaActual = null;
                Datos_User.SesionSinConexion = null;
                lnkOtroCodigo.Visible = false;
                TextosPaso1();
                MostrarErrorCodigo("El uso libre terminó a las " + fin + ": tu bitácora va en lo que toca ahora, elígelo abajo si no se elige solo. "
                    + "Lo que marcaste se conserva.");
                ActualizarPaso2();
                ActualizarDatos();
                return;
            }

            ResultadoGuardado guardado;
            Cursor = Cursors.WaitCursor;
            btnRegistrar.Enabled = false;
            try
            {
                guardado = consultas.GuardarBitacora(ventana.IdCodigo, matricula, LaboratorioEquipo.NumeroMaquina,
                    valores[0], valores[1], valores[2], valores[3], valores[4], valores[5], equiposSinInternet);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnRegistrar.Enabled = true;
            }

            if (guardado == ResultadoGuardado.NoGuardada)
            {
                // GuardarBitacora ya dijo por que (codigo vencido, ya registrado...).
                return;
            }

            registrado = true;
            if (equiposSinInternet != null)
            {
                Log.Info("El alumno " + matricula + " (equipo " + LaboratorioEquipo.NumeroMaquina + ") aviso de equipos sin internet: " + equiposSinInternet + ".");
            }

            if (rbPropio.Checked)
            {
                TerminarConEquipoPropio(guardado == ResultadoGuardado.EnEsteEquipo);
                return;
            }

            lblListo.Text = "Listo, tu bitácora quedó registrada.";
            // Lo dice el resultado y no SinConexion.Activo: la red tambien puede
            // caerse justo al guardar, o volver entre el codigo y el registro.
            lblListoDetalle.Text = (guardado == ResultadoGuardado.EnEsteEquipo ? "Se guardó en este equipo y se enviará sola en cuanto haya conexión. " : "")
                + (equiposSinInternet != null ? "También quedó tu aviso: sin internet en " + EstiloAdmin.Enumerar(equiposSinInternet.Split(',').ToList()) + ". " : "")
                + "Puedes usar este equipo " + HastaLas(ventana.Fin.TimeOfDay) + ".";
            Colocar();
            Refresh();

            // Un momento para que se lea el "listo" y se entrega el equipo hasta la hora de salida.
            VentanaCodigo liberar = ventana;
            string codigo = codigoAceptado;
            Timer espera = new Timer { Interval = 1500 };
            espera.Tick += (s, e) =>
            {
                espera.Stop();
                espera.Dispose();
                SesionEquipo.Liberar(liberar, matricula, codigo);
            };
            espera.Start();
        }

        /// <summary>
        /// Registro con equipo propio: la maquina del laboratorio NO se libera,
        /// porque no la va a usar; se ofrece apagarla o dejarla en el inicio.
        /// </summary>
        private void TerminarConEquipoPropio(bool enEsteEquipo)
        {
            Log.Info("Bitacora de '" + matricula + "' guardada con equipo propio; el equipo no se libera.");
            RegistroExitoso.Opcion opcion = RegistroExitoso.Mostrar(
                string.IsNullOrEmpty(nombre) ? matricula : nombre, matricula, ventana.Laboratorio ?? LaboratorioEquipo.Nombre,
                ventana.EsUsoLibre ? "uso libre del laboratorio" : ventana.Materia + ", " + ventana.Grupo, enEsteEquipo);

            if (opcion == RegistroExitoso.Opcion.Apagar)
            {
                // Con bitacoras sin enviar, apagar puede perderlas (Deep Freeze):
                // se intenta enviar antes y, si no se puede, se pregunta.
                if (!AvisoApagado.PuedeApagar(this))
                {
                    if (AppContexto.Actual != null)
                    {
                        AppContexto.Actual.CerrarSesion();
                    }

                    return;
                }

                if (SesionEquipo.ApagarEquipo())
                {
                    // Windows se esta apagando; no hay nada mas que hacer.
                    return;
                }

                if (!Kiosco.Activo)
                {
                    MessageBox.Show(this, "Modo escritorio: aquí se apagaría el equipo. Se vuelve al inicio.",
                        "DigBit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            if (AppContexto.Actual != null)
            {
                AppContexto.Actual.CerrarSesion();
            }
        }

        private void AbrirPerfil()
        {
            if (perfil == null || perfil.IsDisposed)
            {
                perfil = new editar_Perfil("alumno");
                perfil.FormClosed += (s, e) => ActualizarDatos();
            }

            if (!perfil.Visible)
            {
                perfil.Show(this);
            }

            perfil.Activate();
        }

        // =====================================================================
        // Reloj y la clase de ahora
        // =====================================================================

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            relojTimer.Start();
            datosTimer.Start();
            ActualizarDatos();
            if (PideCodigo)
            {
                txtCodigo.Focus();
            }

            // Mientras no registre y el equipo no este liberado (quien vuelve a usarlo
            // en la misma clase lo libera sin registrar otra vez).
            vigilancia = Inactividad.Vigilar(this, EsperaInactividad, CuentaAtrasInactividad,
                () => !registrado && !SesionEquipo.Activa,
                "Nadie ha tocado el teclado ni el ratón en " + Inactividad.Duracion(EsperaInactividad)
                    + " y tu bitácora aún no está registrada. Si ya no estás aquí, tu sesión se cierra para que nadie la registre por ti.");
        }

        private void ActualizarReloj()
        {
            DateTime ahora = DateTime.Now;
            lblReloj.Text = ahora.ToString("HH:mm");
            string fecha = ahora.ToString("dddd d 'de' MMMM 'de' yyyy", Espanol);
            lblFecha.Text = char.ToUpper(fecha[0], Espanol) + fecha.Substring(1);
        }

        /// <summary>El nombre y la clase de ahora: de la copia local sin conexion; si no, del servidor en segundo plano.</summary>
        private void ActualizarDatos()
        {
            if (IsDisposed)
            {
                return;
            }

            // La copia local se lee al momento, aunque haya una consulta al
            // servidor en vuelo: al caer la red, el paso 1 se rehace ya con ella.
            if (SinConexion.Activo)
            {
                DatosPantalla local = new DatosPantalla();
                try
                {
                    CacheHorario copia = SinConexion.Cache;
                    AlumnoCache alumno = copia.BuscarAlumno(matricula);
                    local.Nombre = alumno == null ? null : alumno.Nombre;
                    local.Modo = copia.Modo;
                    local.Ahora = copia.AhoraEstimado;
                    local.Agenda = LaboratorioEquipo.Resuelto ? copia.AgendaDelDia(local.Ahora.Date) : new List<SesionAgenda>();
                    local.ConAvisosDeRed = copia.ConAvisosDeRed;
                    local.ConUsoLibre = copia.ConUsoLibre;
                    local.Equipos = copia.Equipos;
                }
                catch (Exception ex)
                {
                    Log.Error("Leer la clase de ahora de la copia local", ex);
                }

                Aplicar(local);
                return;
            }

            if (actualizando)
            {
                return;
            }

            actualizando = true;
            string numero = matricula;
            Task.Run(() =>
            {
                DatosPantalla datos = new DatosPantalla { Nombre = new Consultas().NombreDeUsuario(numero) };
                if (LaboratorioEquipo.Resuelto)
                {
                    // Una conexion: la hora del servidor, el modo, la agenda de hoy con
                    // sus sesiones y si se pregunta el paso 3, con que equipos.
                    SituacionLaboratorio situacion = new HorariosDatos().SituacionDelLaboratorio(LaboratorioEquipo.Id);
                    datos.Modo = situacion.Modo;
                    datos.Ahora = situacion.AhoraServidor;
                    datos.Agenda = situacion.Agenda;
                    datos.ConAvisosDeRed = situacion.ConAvisosDeRed;
                    datos.ConUsoLibre = situacion.ConUsoLibre;
                    datos.Equipos = situacion.Equipos;
                    LeerPersonal(datos);
                }
                else
                {
                    using (MySql.Data.MySqlClient.MySqlConnection conexion = new Conexion().GetConexion())
                    {
                        datos.ConAvisosDeRed = Esquema.EquiposSinInternet(conexion);
                    }
                }

                return datos;
            }).ContinueWith(tarea =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        actualizando = false;
                        if (IsDisposed)
                        {
                            return;
                        }

                        if (SinConexion.Activo)
                        {
                            // La consulta salio con servidor, pero mientras tanto se paso a
                            // la copia local (al pulsar Continuar o una clase): manda la
                            // copia, y esta respuesta tardia no debe pisar lo que ya se ve.
                            ActualizarDatos();
                            return;
                        }

                        if (tarea.IsFaulted)
                        {
                            Exception error = tarea.Exception.GetBaseException();
                            Log.Aviso("Pantalla del alumno: no se pudo leer la clase de ahora: " + error.Message);

                            // Sin caja de codigo (sin codigo), o sin haber leido aun el
                            // modo, no hay nada que pulsar que active la copia local: si
                            // es que no hay servidor, se activa aqui.
                            if (codigoAceptado == null && (!PideCodigo || !modoLeido)
                                && ColaBitacoras.Clasificar(error) == ClaseDeFallo.SinConexion
                                && SinConexion.IntentarActivar(error.Message))
                            {
                                ActualizarDatos();
                                return;
                            }

                            if (codigoAceptado == null)
                            {
                                MostrarClase(PideCodigo
                                    ? "No se pudo consultar la clase de ahora. Escribe tu código de todos modos."
                                    : "No se pudo consultar la clase de ahora; se vuelve a intentar en un momento.", false);
                            }

                            return;
                        }

                        Aplicar(tarea.Result);
                    }));
                }
                catch (InvalidOperationException)
                {
                    // La pantalla se cerro mientras se consultaba.
                }
            });
        }

        private void Aplicar(DatosPantalla datos)
        {
            if (IsDisposed)
            {
                return;
            }

            if (!string.IsNullOrEmpty(datos.Nombre))
            {
                nombre = datos.Nombre;
                lblSaludo.Text = "Hola, " + nombre;
            }

            // El paso 3 sale en cuanto la base tiene donde guardarlo; una vez
            // visto, no se quita a media bitacora.
            if (datos.ConAvisosDeRed && !preguntarVecinos)
            {
                preguntarVecinos = true;
                Colocar();
            }

            // Su lista, tambien si llega con el codigo ya aceptado (PonerEquipos no
            // la toca si el alumno ya marco alguna).
            PonerEquipos(datos.Equipos);

            // Lo que eligieron los profesores para sus clases (db/19): el circulo de la franja.
            if (datos.Personal != null)
            {
                foreach (KeyValuePair<int, LecturaPersonal> leida in datos.Personal)
                {
                    personalPorProfesor[leida.Key] = leida.Value;
                }
            }

            if (datos.Imagenes != null)
            {
                foreach (KeyValuePair<string, byte[]> imagen in datos.Imagenes)
                {
                    PersonalizacionClases.Recordar(imagen.Key, imagen.Value);
                }
            }

            if (codigoAceptado != null)
            {
                // El circulo de su clase, por si lo personal llego despues de aceptar el codigo.
                if (ventana != null && !ventana.EsUsoLibre && fichaClase.Visible)
                {
                    fichaClase.Aspecto = AspectoDe(ventana.IdProfesor, ventana.IdMateria, ventana.IdGrupo);
                }

                return;
            }

            if (!LaboratorioEquipo.Resuelto)
            {
                OcultarFicha();
                lblClaseTitulo.Text = "Este equipo no tiene laboratorio asignado";
                lblClaseTitulo.ForeColor = Rojo;
                lblClaseDetalle.Text = "Avisa a la persona responsable del laboratorio: sin eso no se puede registrar la bitácora.";
                pnlClase.BackColor = FondoAviso;
                return;
            }

            // El modo manda el paso 1; si cambio (o entro o salio el modo sin
            // conexion), se rehace sin el error de antes.
            bool cambio = datos.Modo != modo || SinConexion.Activo != modoSinConexion;
            modo = datos.Modo;
            modoSinConexion = SinConexion.Activo;
            modoLeido = true;
            datosTimer.Interval = (modo == ModoCodigo.Fijo ? SegundosEntreActualizaciones : SegundosEntreActualizaciones / 2) * 1000;
            TextosPaso1();

            TimeSpan ahora = datos.Ahora.TimeOfDay;
            List<SesionAgenda> abiertas = ReglasCodigo.Abiertas(datos.Agenda, ahora);
            // Sin codigos y sin ninguna clase (db/14): la bitacora queda como uso
            // libre del laboratorio, hasta que se abra la siguiente clase.
            tramoLibre = modo == ModoCodigo.SinCodigo && datos.ConUsoLibre && abiertas.Count == 0
                ? ReglasCodigo.TramoLibreEn(datos.Agenda, ahora) : null;
            if (!PideCodigo)
            {
                PonerClases(abiertas, tramoLibre);
                lblProxima.Text = TextoProxima(datos.Agenda, abiertas, ahora, tramoLibre);
            }

            if (tramoLibre != null)
            {
                lblPaso1.Text = "Paso 1. Ahora no hay clase";
                lblPaso1Ayuda.Text = "Tu bitácora queda como uso libre del laboratorio.";
            }

            // Sin codigos el alumno solo llena su bitacora: si ahora solo hay una
            // clase, se elige sola (con dos, en el margen entre una y la siguiente,
            // elige el); sin ninguna, el uso libre.
            if (modo == ModoCodigo.SinCodigo && abiertas.Count == 1)
            {
                SesionAgenda unica = abiertas[0];
                ElegirSolaEnUnMomento(ClaveDe(unica), () => ElegirClase(unica, true));
            }
            else if (tramoLibre != null)
            {
                string clave = ClaveLibre(tramoLibre);
                ElegirSolaEnUnMomento(clave, () =>
                {
                    // Si entre tanto cambio (empezo una clase), nada: ya lo dira el refresco.
                    if (tramoLibre != null && ClaveLibre(tramoLibre) == clave)
                    {
                        ElegirUsoLibre(true);
                    }
                });
            }

            SesionAgenda clase = abiertas.FirstOrDefault();
            string detalle;
            if (!PideCodigo)
            {
                detalle = tramoLibre != null ? "Ahora no hay clase en este laboratorio: tu bitácora queda como uso libre y puedes usar el equipo "
                        + HastaLas(tramoLibre.Fin) + "."
                    : abiertas.Count == 0 ? "Ahora no hay ninguna clase abierta en este laboratorio."
                    : abiertas.Count == 1 ? Describir(clase) + " Elígela abajo."
                    : "Hay " + abiertas.Count + " clases en este laboratorio ahora: elige la tuya abajo.";
            }
            else if (clase == null)
            {
                detalle = modo == ModoCodigo.Renovable
                    ? "Ahora no hay clase programada en este laboratorio."
                    : "Ahora no hay clase programada. Si tu docente te dio un código, escríbelo: se acepta desde "
                      + HorariosDatos.MargenAntesDelInicioMinutos + " minutos antes de empezar.";
            }
            else
            {
                // Con codigos generados, nunca se ensena el codigo: se dice si el
                // profesor ya abrio la clase (entro a DigBit durante ella).
                detalle = Describir(clase) + (modo == ModoCodigo.Renovable && !clase.SesionId.HasValue
                    ? " Tu docente todavía no entra a DigBit para esta clase: el código le aparece al entrar."
                    : "");
            }

            // Con una clase de la que hablar (la unica abierta sin codigos, o la
            // programada con codigo), la ficha con su circulo; si no, el texto.
            if (clase != null && tramoLibre == null && (PideCodigo || abiertas.Count == 1))
            {
                string nota = !PideCodigo ? "Elígela abajo."
                    : modo == ModoCodigo.Renovable && !clase.SesionId.HasValue
                        ? "Tu docente todavía no entra a DigBit para esta clase: el código le aparece al entrar."
                        : null;
                MostrarFicha(clase.Materia, clase.Grupo, clase.Profesor, clase.Inicio, clase.Fin, false, clase.EsExtra, nota,
                    AspectoDe(clase.ProfesorId, clase.MateriaId, clase.GrupoId));
            }
            else
            {
                MostrarClase(detalle, false);
            }

            if (cambio)
            {
                lblErrorCodigo.Text = "";
                // La pista del paso 2 ("escribe el codigo" o "elige tu clase")
                // depende del modo. ActualizarPaso2 tambien recoloca.
                ActualizarPaso2();

                // Al esconderse la caja con el foco, WinForms se lo pasaba a "Mi
                // perfil" (lo unico seleccionable que queda) y un Enter lo abria. Con
                // la lista, el foco en nada (como OlvidarCodigo); con la caja, en ella.
                if (!PideCodigo)
                {
                    ActiveControl = null;
                }
                else if (txtCodigo.Visible)
                {
                    ActiveControl = txtCodigo;
                }
            }
            else if (!PideCodigo)
            {
                Colocar();
            }
        }

        private static string Describir(SesionAgenda clase)
        {
            return clase.Materia + ", " + clase.Grupo + ", con " + clase.Profesor + ", hasta las " + HorariosDatos.Hora(clase.Fin)
                + (clase.EsExtra ? " (sesión extra)." : ".");
        }

        /// <summary>La franja de arriba: "Ahora en CISCO, equipo PC-3" (o "Tu clase en ...") y el detalle.</summary>
        private void MostrarClase(string detalle, bool esLaTuya)
        {
            OcultarFicha();
            string laboratorio = LaboratorioEquipo.Nombre ?? "";
            string equipo = LaboratorioEquipo.NumeroMaquina;
            lblClaseTitulo.Text = (esLaTuya ? "Tu clase en " : "Ahora en ") + laboratorio + ", equipo " + equipo
                + (SinConexion.Activo ? " (sin conexión con el servidor)" : "");
            lblClaseTitulo.ForeColor = Color.DarkGreen;
            lblClaseDetalle.Text = detalle + (SinConexion.Activo ? " Tu bitácora se guardará en este equipo y se enviará sola." : "");
            pnlClase.BackColor = SinConexion.Activo ? FondoAviso : FondoClase;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            relojTimer.Stop();
            datosTimer.Stop();
            if (vigilancia != null)
            {
                vigilancia.Dispose();
                vigilancia = null;
            }

            if (eleccionPendiente != null)
            {
                eleccionPendiente.Stop();
                eleccionPendiente.Dispose();
                eleccionPendiente = null;
            }

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
