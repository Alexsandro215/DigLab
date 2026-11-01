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
    /// Panel del administrador. Arriba, las seis secciones (bitacoras, alumnos,
    /// profesores, laboratorios, horarios y estadisticas); debajo, con un reloj, lo
    /// que pasa en cada laboratorio en la hora anterior, la actual y la siguiente.
    ///
    /// La hora es la del servidor MySQL, la misma con la que los equipos del
    /// laboratorio deciden si aceptan un codigo: si el reloj de esta PC va
    /// adelantado, el panel no contradice lo que ven los alumnos. Los datos se
    /// piden en segundo plano cada 30 s; el reloj y el "ahora" se recalculan cada
    /// segundo sobre la ultima agenda, sin ir a la base.
    ///
    /// Tambien es el panel del encargado de laboratorio (tipo 4), en solo
    /// consulta (Permisos): ve lo mismo, pero sin Ajustes ni Codigos de las
    /// clases (en su lugar, la pastilla "Solo consulta"), sin abrir clases desde
    /// el panel y sin los codigos generados. Los dos tienen "Mi perfil": el
    /// encargado cambia ahi su correo y su contrasena (MiPerfilDatos).
    ///
    /// En "Los laboratorios ahora", clic en una clase: quien registro y que
    /// reportaron (BitacoraSesion); en el nombre del laboratorio, en "Libre" o en
    /// una clase cancelada: su horario. F5 lo vuelve a leer al momento.
    /// </summary>
    public class PrincipalAdministrador : Form
    {
        private const int SegundosEntreActualizaciones = 30;
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");
        private static readonly Color TextoSecundario = Color.FromArgb(90, 90, 90);

        // Las tres formas de entrar, con los nombres que eligio el usuario (ModosDeAcceso.Nombre).
        private static readonly string ComoEntran = "Cómo entra cada estudiante a su clase: «" + ModosDeAcceso.Nombre(ModoCodigo.SinCodigo)
            + "», «" + ModosDeAcceso.Nombre(ModoCodigo.Fijo) + "» (el de su docente, el mismo en todas sus clases) o «"
            + ModosDeAcceso.Nombre(ModoCodigo.Renovable) + "» (uno distinto por clase y día).";

        // Lo que hay en Alertas, como lo dice su ventana (VentanaAlertas): los cambios
        // de horario (db/21) tambien.
        private const string QueHayEnAlertas = "clases en que nadie registró bitácora, fallas reportadas en los laboratorios "
            + "y cambios de horario que hizo otra persona.";

        private readonly Container componentes = new Container();

        // Una ventana por opcion: si ya esta abierta se trae al frente.
        private readonly Dictionary<string, Form> ventanas = new Dictionary<string, Form>();

        private Panel cabecera;
        private Label titulo;
        private Label lblSesion;
        private string usuarioSesion;
        // El del TESCHI y donde acaban los del Gobierno del Estado: en una ventana
        // angosta el del TESCHI se quita con su raya (ColocarCabecera).
        private PictureBox logoTeschi;
        private int finLogos;
        private bool conLogoTeschi = true;
        // Las acciones, en pastillas debajo del titulo, de izquierda a derecha: Mi
        // perfil, Ajustes, Codigos de las clases y Alertas; para el encargado de
        // laboratorio, Mi perfil, Solo consulta y Alertas. La ultima es Alertas.
        private readonly List<BotonCabecera> pastillas = new List<BotonCabecera>();
        private BotonCabecera btnAlertas;
        private Label lblReloj;
        private Label lblFecha;
        // El dia que ensena lblFecha (hora del servidor); al cambiar se recoloca la cabecera.
        private DateTime diaMostrado;
        private Button btnCerrarSesion;
        private Label lblEstado;
        private PanelLaboratoriosAhora panelLaboratorios;
        private Timer relojTimer;
        private Timer datosTimer;

        private TimeSpan desfaseServidor = TimeSpan.Zero;
        private bool actualizando;
        private LinkLabel lnkAcceso;
        private readonly ToolTip ayudaAcceso = new ToolTip();
        // Se pidio otra lectura mientras habia una en vuelo.
        private bool otraLecturaPendiente;
        // ColoresProfesor.Version cuando se lanzo la lectura en vuelo.
        private int versionColores;
        private DateTime? sinConexionDesde;

        // Alertas (AlertasDatos): se leen en segundo plano cada 2 minutos; el
        // enlace del encabezado dice cuantas son nuevas para este administrador.
        private const int SegundosEntreAlertas = 120;
        private const int DiasDeAlertas = 7;
        private Timer alertasTimer;
        // La notificacion de Windows de las alertas nuevas (AvisarNuevas): el icono
        // de DigBit en el area de notificacion mientras el panel esta abierto; una
        // por hora como mucho (AvisosDeAlertas).
        private NotifyIcon avisoWindows;
        private readonly AvisosDeAlertas avisos = new AvisosDeAlertas();
        private FormWindowState estadoSinMinimizar = FormWindowState.Maximized;
        // Lo mismo para cada seccion (Mostrar): si se minimizo, vuelve como estaba
        // (Estadisticas y Horarios, maximizadas).
        private readonly Dictionary<Form, FormWindowState> estadoDeVentanas = new Dictionary<Form, FormWindowState>();
        private bool leyendoAlertas;
        private ResultadoAlertas alertas;
        private DateTime? alertasVistasHasta;
        // Cambia al marcarlas como vistas: una lectura que ya iba en camino no
        // debe devolver el aviso de nuevas.
        private int versionVistas;

        public PrincipalAdministrador()
        {
            InicializarComponentes();
            // Un color cambiado desde Profesores se ve en el panel sin esperar al refresco.
            ColoresProfesor.Cambio += RepintarPanel;
            // Si se cambian los codigos (desde aqui, Ajustes o Laboratorios), el
            // enlace de "Los laboratorios ahora" y el panel lo dicen al momento.
            ModosDeAcceso.Cambio += ModosCambiaron;

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
            Text = Permisos.EsEncargado ? "DigBit - Responsable de laboratorio" : "DigBit - Administración";

            TableLayoutPanel raiz = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(28, 12, 28, 18),
                BackColor = Color.White
            };
            raiz.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 128F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            raiz.Controls.Add(CrearCabecera(recursos), 0, 0);
            raiz.Controls.Add(CrearSecciones(), 0, 1);
            raiz.Controls.Add(CrearTituloPanel(), 0, 2);

            panelLaboratorios = new PanelLaboratoriosAhora { Dock = DockStyle.Fill, Margin = new Padding(0) };
            panelLaboratorios.LaboratorioElegido += AbrirHorarios;
            panelLaboratorios.ClaseElegida += VerRegistro;
            // Abrir una clase (o ver su codigo) con el clic derecho: solo el
            // administrador. Sin nadie que lo escuche, el panel no ofrece ese menu
            // ni lo anuncia en su ayuda (el encargado de laboratorio).
            if (!Permisos.SoloConsulta && Permisos.VeCodigosGenerados)
            {
                panelLaboratorios.AbrirClasePedida += AbrirClaseComoAdministrador;
            }

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

            // Como en el inicio de sesion y en las cabeceras del alumno y del profesor
            // (RNF01): los logotipos del Gobierno del Estado de Mexico y, a 22 px y
            // separado por una raya, el del TESCHI, del mismo alto. En una ventana
            // angosta el del TESCHI se quita (ColocarCabecera), como en la del alumno.
            PictureBox logos = new PictureBox
            {
                Image = (Image)recursos.GetObject("Logos"),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(0, 16),
                Size = new Size(230, 48),
                AccessibleName = "Gobierno del Estado de México"
            };

            logoTeschi = new PictureBox
            {
                Image = Properties.Resources.logoteschi,
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(logos.Right + 22, 16),
                Size = new Size(110, 48),
                AccessibleName = "Tecnológico de Estudios Superiores de Chimalhuacán"
            };
            finLogos = logos.Right;
            int rayaLogos = logos.Right + 11;

            titulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(logoTeschi.Right + 20, 12),
                Text = Permisos.EsEncargado ? "Panel de responsable de laboratorio" : "Panel de administración"
            };

            // En el renglon del titulo, a su derecha y con su misma linea base: debajo
            // van las pastillas, que con los logotipos de las otras cabeceras ya no
            // cabian junto a la sesion y la fecha a 1366 de ancho. Sin AutoSize:
            // ColocarCabecera le da el ancho que hay hasta el reloj (si no cabe
            // entero, "Sesion: X", solo X y luego "...").
            usuarioSesion = Datos_User.getUser() ?? "-";
            lblSesion = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                Text = "Sesión iniciada como " + usuarioSesion
            };
            lblSesion.Size = new Size(240, TextRenderer.MeasureText("Ág", lblSesion.Font).Height);
            lblSesion.Location = new Point(titulo.Right + 16, titulo.Top + Ascenso(titulo.Font) - Ascenso(lblSesion.Font));
            // El texto entero, aunque ColocarCabecera lo acorte.
            Ayuda(lblSesion, "Sesión iniciada como " + usuarioSesion + ".");

            // Su propia cuenta, junto a la sesion: el encargado de laboratorio, que en
            // lo demas solo consulta, tambien cambia ahi su correo y su contrasena.
            BotonCabecera btnPerfil = new BotonCabecera("Mi perfil");
            btnPerfil.Click += (s, e) => AbrirPerfil();
            Ayuda(btnPerfil, Permisos.SoloConsulta ? "Tus datos; puedes cambiar tu correo y tu contraseña."
                : "Tu nombre, tu correo y tu contraseña.");
            pastillas.Add(btnPerfil);

            if (Permisos.SoloConsulta)
            {
                // El encargado de laboratorio no cambia nada mas: ni Ajustes (ensena la
                // clave de la API) ni Codigos de las clases. En su lugar, una pastilla
                // que dice que es solo consulta y, al pulsarla, que incluye.
                BotonCabecera btnSoloConsulta = new BotonCabecera("Solo consulta");
                btnSoloConsulta.Click += (s, e) => ExplicarSoloConsulta();
                Ayuda(btnSoloConsulta, "Entraste como responsable de laboratorio: puedes ver todo, pero los cambios los hace la administración de DigBit "
                    + "(salvo tu correo y tu contraseña, en Mi perfil). Pulsa para ver qué incluye.");
                pastillas.Add(btnSoloConsulta);
            }
            else
            {
                // Ajustes de toda la escuela (db/13): a donde llama "Olvidaste tu contrasena?".
                BotonCabecera btnAjustes = new BotonCabecera("Ajustes");
                btnAjustes.Click += (s, e) => AbrirAjustes();
                Ayuda(btnAjustes, "Códigos de las clases, cambios de horario, recuperar contraseña por correo y aviso de privacidad, para todos los equipos.");

                // Como entran los alumnos a sus clases (db/11): a la vista, junto a
                // Ajustes (antes solo estaba el enlace pequeno de "Los laboratorios ahora").
                BotonCabecera btnCodigos = new BotonCabecera(DialogoAccesoClases.Titulo);
                btnCodigos.Click += (s, e) => CambiarAcceso();
                Ayuda(btnCodigos, ComoEntran + " Una forma para toda la escuela y otra para los laboratorios que la necesiten.");
                pastillas.Add(btnAjustes);
                pastillas.Add(btnCodigos);
            }

            // La caja del reloj, de lo que mide la hora (como en las otras cabeceras):
            // a su izquierda llega la sesion.
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

            btnCerrarSesion = EstiloHorarios.CrearBoton("Cerrar sesión", Point.Empty, new Size(150, 40));
            // Salir de la sesion, no de la aplicacion (DigBit puede ser el shell).
            btnCerrarSesion.Click += (s, e) => AppContexto.Actual.CerrarSesion();

            // Lo que conviene saber: clases sin bitacoras, fallas reportadas y cambios
            // de horario, con una campana y cuantas nuevas hay (VentanaAlertas).
            btnAlertas = new BotonCabecera("Alertas", true);
            btnAlertas.Click += (s, e) => AbrirAlertas();
            Ayuda(btnAlertas, char.ToUpper(QueHayEnAlertas[0]) + QueHayEnAlertas.Substring(1));
            pastillas.Add(btnAlertas);

            // Tab: las pastillas de izquierda a derecha y al final Cerrar sesion.
            for (int i = 0; i < pastillas.Count; i++)
            {
                pastillas[i].TabIndex = i;
            }

            btnCerrarSesion.TabIndex = pastillas.Count;

            // Las pastillas primero: quedan encima en el orden Z. La caja del titulo
            // (AutoSize, 36 px de alto) llega hasta y=48 y taparia su borde de arriba.
            cabecera.Controls.AddRange(pastillas.ToArray());
            cabecera.Controls.AddRange(new Control[] { btnCerrarSesion, logos, logoTeschi, titulo, lblSesion, lblReloj, lblFecha });
            cabecera.Resize += (s, e) => ColocarCabecera();
            cabecera.Paint += (s, e) =>
            {
                using (Pen linea = new Pen(Color.FromArgb(215, 222, 215)))
                {
                    e.Graphics.DrawLine(linea, 0, cabecera.Height - 1, cabecera.Width, cabecera.Height - 1);
                    // Entre los logotipos, a la mitad de los 22 px, como en las otras cabeceras.
                    if (conLogoTeschi)
                    {
                        e.Graphics.DrawLine(linea, rayaLogos, 22, rayaLogos, 58);
                    }
                }
            };
            return cabecera;
        }

        /// <summary>"Miercoles 14 de octubre de 2026", o sin el ano: "Miercoles 14 de octubre".</summary>
        private static string FechaLarga(DateTime dia, bool conAno)
        {
            string fecha = dia.ToString(conAno ? "dddd d 'de' MMMM 'de' yyyy" : "dddd d 'de' MMMM", Espanol);
            return char.ToUpper(fecha[0], Espanol) + fecha.Substring(1);
        }

        /// <summary>Del borde de arriba de la caja de un Label a su linea base, en pixeles.</summary>
        private static int Ascenso(DrawingFont fuente)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                FontFamily familia = fuente.FontFamily;
                return (int)Math.Round(fuente.GetHeight(g) * familia.GetCellAscent(fuente.Style) / familia.GetLineSpacing(fuente.Style));
            }
        }

        /// <summary>
        /// Los logotipos, el titulo y a su derecha la sesion; debajo del titulo, las
        /// pastillas que haya (las del administrador o las del encargado), en orden
        /// y separadas 2 px, la ultima, Alertas, con su ancho guardado con "99+". A
        /// la derecha, el reloj, la fecha y "Cerrar sesion". El logotipo del TESCHI
        /// va si con el caben enteras las pastillas y la fecha de hoy, y en el
        /// renglon del titulo al menos el usuario de la sesion; si no (por debajo de
        /// unos 1280 de ancho), se quita con su raya y lo demas pasa junto a los
        /// otros logos. La sesion dice "Sesion iniciada como X" si cabe hasta el
        /// reloj; si no, "Sesion: X", solo X y luego "..." (al pasar el raton sale
        /// el texto entero). La fecha, entera si cabe; si no, sin el ano.
        /// </summary>
        // Entre una pastilla y la siguiente (antes 2: se veian pegadas).
        private const int EntrePastillas = 8;

        private void ColocarCabecera()
        {
            int derecha = cabecera.ClientSize.Width;
            btnCerrarSesion.Location = new Point(derecha - btnCerrarSesion.Width, 20);
            int finReloj = btnCerrarSesion.Left - 24;
            lblReloj.Location = new Point(finReloj - lblReloj.Width, 4);

            BotonCabecera ultima = pastillas[pastillas.Count - 1];
            int anchoPastillas = ultima.AnchoReservado;
            for (int i = 0; i < pastillas.Count - 1; i++)
            {
                anchoPastillas += pastillas[i].Width + EntrePastillas;
            }

            // Antes del primer tic del reloj (al construir la ventana), la de hoy.
            DateTime dia = diaMostrado == DateTime.MinValue ? Ahora().Date : diaMostrado;
            string fechaEntera = FechaLarga(dia, true);
            int anchoFecha = TextRenderer.MeasureText(fechaEntera, lblFecha.Font).Width;
            int anchoUsuario = Math.Min(120, TextRenderer.MeasureText(usuarioSesion, lblSesion.Font).Width);
            int textoConLogo = logoTeschi.Right + 20;
            bool caben = textoConLogo + 3 + anchoPastillas + 12 + anchoFecha <= finReloj
                && textoConLogo + titulo.Width + 16 + anchoUsuario <= lblReloj.Left - 16;
            if (caben != conLogoTeschi)
            {
                conLogoTeschi = caben;
                logoTeschi.Visible = caben;
                // La raya la pinta la cabecera.
                cabecera.Invalidate();
            }

            int inicioTexto = (caben ? logoTeschi.Right : finLogos) + 20;
            titulo.Left = inicioTexto;

            // Debajo del titulo, desde donde empieza su texto. En y=40 (la pastilla se
            // ve de 43 a 69): su texto y el de la fecha comparten la linea base.
            int x = inicioTexto + 3;
            foreach (BotonCabecera pastilla in pastillas)
            {
                pastilla.Location = new Point(x, 40);
                x = pastilla.Right + EntrePastillas;
            }

            // La fecha, alineada a la derecha, nunca debajo de las pastillas: en una
            // ventana angosta se acorta con "..." en vez de quedar tapada.
            int inicioFecha = Math.Max(finReloj - 320, ultima.Left + ultima.AnchoReservado + 12);
            lblFecha.Bounds = new Rectangle(inicioFecha, 50, Math.Max(40, finReloj - inicioFecha), 22);
            // En la ventana mas angosta, con las cuatro pastillas, la entera ya no cabe:
            // "Miercoles 14 de octubre" se lee mejor que "Miercoles 14 de octubre de...".
            lblFecha.Text = anchoFecha <= lblFecha.Width ? fechaEntera : FechaLarga(dia, false);

            int inicioSesion = titulo.Right + 16;
            int anchoSesion = lblReloj.Left - 16 - inicioSesion;
            foreach (string texto in new[] { "Sesión iniciada como " + usuarioSesion, "Sesión: " + usuarioSesion, usuarioSesion })
            {
                lblSesion.Text = texto;
                if (lblSesion.GetPreferredSize(Size.Empty).Width <= anchoSesion)
                {
                    break;
                }
            }

            lblSesion.Visible = anchoSesion >= 40;
            lblSesion.Bounds = new Rectangle(inicioSesion, lblSesion.Top, Math.Max(40, Math.Min(lblSesion.GetPreferredSize(Size.Empty).Width, anchoSesion)), lblSesion.Height);
        }

        /// <summary>El ToolTip y, para los lectores de pantalla, la misma descripcion.</summary>
        private void Ayuda(Control control, string texto)
        {
            ayudaAcceso.SetToolTip(control, texto);
            control.AccessibleDescription = texto;
        }

        /// <summary>
        /// La pastilla "Solo consulta" del encargado de laboratorio: que puede ver,
        /// que puede guardar, donde esta el codigo y que hace la administracion (lo
        /// mismo que la etiqueta junto al titulo de cada seccion).
        /// </summary>
        private void ExplicarSoloConsulta()
        {
            Permisos.ExplicarSoloConsulta(this);
        }

        /// <summary>
        /// "Mi perfil" de quien entro: el mismo dialogo que el de alumnos y
        /// profesores (editar_Perfil), aqui modal. El encargado de laboratorio
        /// tambien: lo suyo lo guarda MiPerfilDatos fuera de Permisos.
        /// </summary>
        private void AbrirPerfil()
        {
            try
            {
                using (editar_Perfil perfil = new editar_Perfil())
                {
                    perfil.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Abrir Mi perfil", ex, "No se pudo abrir Mi perfil", Text);
            }
        }

        private void AbrirAjustes()
        {
            if (Permisos.Bloquear(this, "Abrir Ajustes")) return;

            try
            {
                DialogoAjustes.Mostrar(this);
                // Desde Ajustes tambien se cambian los codigos de las clases.
                ActualizarDatos();
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Abrir los ajustes", ex, "No se pudieron abrir los ajustes", Text);
            }
        }

        private Control CrearSecciones()
        {
            TableLayoutPanel fila = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                Margin = new Padding(0, 18, 0, 10)
            };
            for (int i = 0; i < fila.ColumnCount; i++)
            {
                fila.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / fila.ColumnCount));
            }

            fila.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // El encargado de laboratorio (solo consulta) tiene las mismas seis: cambia
            // lo que dicen, porque en ellas solo busca, ve y descarga. La fila no crece
            // (a 1366x768 el panel en vivo ya va justo): donde la descripcion no cabe
            // en sus 3 renglones (a 1280 y en el minimo del panel) se pinta la corta.
            bool soloConsulta = Permisos.SoloConsulta;
            AgregarSeccion(fila, 0, "Bitácoras", soloConsulta
                ? "Consultar y descargar en PDF las bitácoras de cada clase."
                : "Consultar, corregir y descargar las bitácoras de cada clase.", soloConsulta
                ? "Consultar y descargar en PDF."
                : "Consultar, corregir y descargar en PDF.", AbrirBitacoras);
            AgregarSeccion(fila, 1, "Estudiantes", soloConsulta
                ? "Buscar estudiantes: su carrera, sus bitácoras y si su cuenta está de baja."
                : "Buscar, registrar y editar estudiantes; ver sus bitácoras; dar de baja.", soloConsulta
                ? "Buscar; ver su carrera y sus bitácoras."
                : "Buscar, registrar, editar y dar de baja.", AbrirAlumnos);
            AgregarSeccion(fila, 2, "Docentes", soloConsulta
                ? "Buscar docentes; ver su horario, sus bitácoras y todo lo suyo."
                : "Buscar, registrar y editar docentes y responsables; ver todo lo de cada uno.", soloConsulta
                ? "Buscar; ver todo lo de cada uno."
                : "Docentes y responsables de laboratorio.", AbrirProfesores);
            AgregarSeccion(fila, 3, "Laboratorios", soloConsulta
                ? "Ver cada laboratorio, cuánto se usa y su horario."
                : "Dar de alta, cambiar el nombre, ver su horario o quitarlos.", null, AbrirLaboratorios);
            AgregarSeccion(fila, 4, "Horarios", "Horario semanal, clases, excepciones y faltas.", null, () => AbrirHorarios(0));
            // La misma para la administracion y para el encargado: la ventana solo lee.
            AgregarSeccion(fila, 5, "Estadísticas", "Asistencias y reportes de fallas por periodo.", null, AbrirEstadisticas);
            return fila;
        }

        /// <param name="corta">La que se pinta si la descripcion no cabe (TarjetaOpcion.DescripcionCorta); null si siempre cabe.</param>
        private void AgregarSeccion(TableLayoutPanel fila, int columna, string titulo, string descripcion, string corta, Action abrir)
        {
            TarjetaOpcion tarjeta = new TarjetaOpcion(titulo, descripcion)
            {
                DescripcionCorta = corta,
                Dock = DockStyle.Fill,
                Margin = new Padding(columna == 0 ? 0 : 8, 0, columna == fila.ColumnCount - 1 ? 0 : 8, 0)
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
            // A la vista solo la hora; lo tecnico, al pasar el raton (como en el panel del profesor y en Mis grupos).
            Ayuda(lblEstado, "Hora del servidor. Los laboratorios se vuelven a leer solos cada " + SegundosEntreActualizaciones
                + " segundos; F5 los lee al momento.");

            // Como entran los alumnos a sus clases (db/11), con su enlace para cambiarlo
            // (el encargado de laboratorio solo lo lee: sin enlace, ver MostrarAcceso).
            lnkAcceso = new LinkLabel
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                // El color del texto cuando no es enlace (sin db/11).
                ForeColor = Color.FromArgb(90, 90, 90),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Location = new Point(250, 16),
                Size = new Size(400, 20),
                Text = ""
            };
            lnkAcceso.LinkClicked += (s, e) => CambiarAcceso();

            panel.Controls.Add(titulo);
            panel.Controls.Add(lnkAcceso);
            panel.Controls.Add(lblEstado);
            // El estado, del ancho de lo que dice (hasta 560): con 560 fijos, a 1366 la
            // linea de los codigos de la responsable ("...te piden el codigo: va en la
            // tarjeta de cada clase") se cortaba junto a un "Actualizado a las 10:42".
            Action acomodar = () =>
            {
                int pide = TextRenderer.MeasureText(lblEstado.Text ?? "", lblEstado.Font).Width + 8;
                lblEstado.Width = Math.Max(160, Math.Min(560, pide));
                lblEstado.Location = new Point(panel.ClientSize.Width - lblEstado.Width, 16);
                lnkAcceso.Location = new Point(titulo.Right + 24, 16);
                lnkAcceso.Width = Math.Max(120, lblEstado.Left - 16 - lnkAcceso.Left);
            };
            panel.Resize += (s, e) => acomodar();
            lblEstado.TextChanged += (s, e) => acomodar();
            return panel;
        }

        /// <summary>
        /// "Codigos: Generacion de codigos (2 con otra forma). Cambiar..." con los
        /// modos del ultimo panorama; para el encargado de laboratorio, sin
        /// "Cambiar..." ni enlace.
        /// </summary>
        private void MostrarAcceso(PanoramaLaboratorios datos)
        {
            if (!datos.ConModos)
            {
                // Sin db/11 no se puede cambiar: texto corto (cabe a 1280 de ancho) y
                // sin enlace, pero habilitado, porque un control deshabilitado no
                // ensena su ayuda y la explicacion esta ahi.
                lnkAcceso.Text = DialogoAccesoClases.Titulo + ": " + ModosDeAcceso.Descripcion(ModoCodigo.Fijo);
                lnkAcceso.LinkArea = new LinkArea(0, 0);
                ayudaAcceso.SetToolTip(lnkAcceso, "Para elegir «" + ModosDeAcceso.Nombre(ModoCodigo.SinCodigo) + "» o «"
                    + ModosDeAcceso.Nombre(ModoCodigo.Renovable) + "» falta aplicar db/11_modo_de_acceso.sql en la base; hasta entonces se entra con «"
                    + ModosDeAcceso.Nombre(ModoCodigo.Fijo) + "» en todos los laboratorios.");
                return;
            }

            // La forma de toda la escuela y cuantos van con otra (con muchos
            // laboratorios, "distinto en cada laboratorio" ya no decia nada). Corto:
            // a 1280-1366 de ancho, el resumen largo se comia el "Cambiar...".
            List<ModoCodigo> modos = datos.Laboratorios.Select(l => datos.ModoDe(l.Id)).ToList();
            ModoCodigo general = ModosDeAcceso.General(datos.GeneralGuardada, modos);
            int otros = modos.Count(m => m != general);
            string forma = "Códigos: " + ModosDeAcceso.Nombre(general) + (otros == 0 ? "" : " (" + otros + " con otra forma)");
            // En la ayuda, no en el texto (a 1366 ya no cabria el "Cambiar...").
            string quienLosVe = datos.CodigosSoloResponsables
                ? " Los docentes no ven los códigos: se los piden a la persona responsable del laboratorio (cada tarjeta dice el de su clase)."
                : "";
            if (Permisos.SoloConsulta)
            {
                // El encargado de laboratorio lo ve, pero no lo cambia: sin "Cambiar..."
                // ni enlace, y habilitado para que su ayuda salga (como sin db/11). Con
                // "Los docentes no ven los codigos", a la vista que se los piden a ella
                // (si en algun laboratorio se entra con codigo).
                bool conCodigos = general != ModoCodigo.SinCodigo || otros > 0;
                lnkAcceso.Text = forma + (datos.CodigosSoloResponsables && conCodigos
                    ? ". Los docentes te piden el código: va en la tarjeta de cada clase." : "");
                lnkAcceso.LinkArea = new LinkArea(0, 0);
                ayudaAcceso.SetToolTip(lnkAcceso, ComoEntran + quienLosVe + " Solo la administración de DigBit puede cambiarlo.");
                return;
            }

            lnkAcceso.Text = forma + ". Cambiar...";
            lnkAcceso.LinkArea = new LinkArea(0, lnkAcceso.Text.Length);
            ayudaAcceso.SetToolTip(lnkAcceso, ComoEntran + quienLosVe);
        }

        private void CambiarAcceso()
        {
            if (Permisos.Bloquear(this, "Cambiar los codigos de las clases")) return;

            try
            {
                if (DialogoAccesoClases.Mostrar(this))
                {
                    ActualizarDatos();
                }
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Abrir el acceso a las clases", ex, "No se pudieron abrir los códigos de las clases", Text);
            }
        }

        /// <summary>
        /// Generacion de codigos: el administrador abre la clase (o ve su
        /// codigo) desde el panel para darselo al grupo si el profesor no puede.
        /// Queda en el log quien lo hizo. El encargado de laboratorio no: abrirla
        /// crea la sesion en la base y el codigo generado solo lo ve el administrador.
        /// </summary>
        private void AbrirClaseComoAdministrador(SesionAgenda clase)
        {
            if (Permisos.Bloquear(this, "Abrir una clase desde el panel")) return;

            Cursor = Cursors.WaitCursor;
            Task.Run(() => new HorariosDatos().AbrirSesion(clase, 0)).ContinueWith(tarea =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        Cursor = Cursors.Default;
                        if (tarea.IsFaulted)
                        {
                            AvisoDeFalloAdmin.Mostrar(this, "Abrir la clase " + clase.Descripcion + " desde el panel del administrador",
                                tarea.Exception.GetBaseException(), "No se pudo abrir la clase", Text);
                            return;
                        }

                        AperturaSesion apertura = tarea.Result;
                        if (!apertura.Abierta)
                        {
                            MessageBox.Show(this, apertura.Motivo, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }

                        Log.Info("El administrador " + (Datos_User.getUser() ?? "-") + " vio el codigo de la clase " + clase.Descripcion
                            + " (sesion " + apertura.SesionId + ").");
                        string laboratorio = panelLaboratorios.Panorama == null ? ""
                            : panelLaboratorios.Panorama.Laboratorios.Where(l => l.Id == clase.LaboratorioId).Select(l => l.Texto).FirstOrDefault() ?? "";
                        DialogoCodigoDeClase.Mostrar(this, clase, apertura.Codigo, laboratorio);
                        ActualizarDatos();
                    }));
                }
                catch (InvalidOperationException)
                {
                    // El formulario se cerro mientras se abria.
                }
            });
        }

        // =====================================================================
        // Secciones
        // =====================================================================

        private void AbrirBitacoras()
        {
            // Una sola pantalla para ver, corregir y descargar (antes eran tres botones).
            Mostrar("bitacoras", () => new GestionBitacorasAdministrador());
        }

        /// <summary>Horarios; con un laboratorio (clic en su nombre, en una hora libre o en una clase cancelada del panel), directo a su horario semanal.</summary>
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
            Mostrar("profesores", () =>
            {
                AdministrarProfesores profesores = new AdministrarProfesores();
                profesores.VerBitacoras += AbrirBitacorasBuscando;
                profesores.VerTodo += p => AbrirFichaDocente(p, profesores.Enlaces);
                return profesores;
            });
        }

        /// <summary>
        /// "Ver todo lo suyo..." de Docentes: la ficha de ese docente (FichaDocente), una ventana
        /// por docente; si ya esta abierta, al frente. Solo lee: tambien la persona responsable.
        /// Se cierra con el panel (es suya), que al cerrar suelta la lectura de Estadisticas.
        /// </summary>
        private void AbrirFichaDocente(ProfesorInfo profesor, LecturaEnlaces enlaces)
        {
            if (profesor == null)
            {
                return;
            }

            Mostrar("docente:" + profesor.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), () =>
            {
                FichaDocente ficha = new FichaDocente(profesor, enlaces);
                ficha.VerBitacoras += AbrirBitacorasBuscando;
                ficha.VerEnEstadisticas += (docente, periodo) => AbrirEstadisticasDelDocente(docente, periodo, ficha.LecturaVista);
                return ficha;
            });
        }

        /// <summary>
        /// "Ver en Estadisticas" de la ficha: Estadisticas con ese docente, ese periodo y la lectura de
        /// la ficha (sus mismas cifras aunque Estadisticas tuviera otra mas vieja). Cambia lo que
        /// recuerda, a proposito.
        /// </summary>
        private void AbrirEstadisticasDelDocente(int docente, PeriodoElegido periodo, LecturaEstadisticas deLaFicha)
        {
            VentanaEstadisticas estadisticas = Mostrar("estadisticas", () => new VentanaEstadisticas()) as VentanaEstadisticas;
            if (estadisticas != null)
            {
                estadisticas.MostrarDocente(docente, periodo, deLaFicha);
            }
        }

        /// <summary>
        /// "Docentes por enlazar" de Alertas: Docentes, y encima la lista de quienes
        /// entraron con el SIIA y esperan, para enlazarlos (despues de que Docentes
        /// lea su lista, que se lee al mostrarse).
        /// </summary>
        private void AbrirDocentesPorEnlazar()
        {
            AbrirProfesores();
            Form ventana;
            AdministrarProfesores profesores = ventanas.TryGetValue("profesores", out ventana) ? ventana as AdministrarProfesores : null;
            if (profesores != null && !profesores.IsDisposed)
            {
                profesores.BeginInvoke(new Action(profesores.AbrirPorEnlazar));
            }
        }

        /// <summary>Bitacoras ya buscando ese texto: "Ver sus bitacoras" de Profesores (su nombre y su numero de empleado).</summary>
        private void AbrirBitacorasBuscando(string texto)
        {
            GestionBitacorasAdministrador bitacoras = Mostrar("bitacoras", () => new GestionBitacorasAdministrador()) as GestionBitacorasAdministrador;
            if (bitacoras != null)
            {
                bitacoras.Buscar(texto);
            }
        }

        /// <summary>
        /// Clic en una clase de "Los laboratorios ahora": quien registro y que
        /// reportaron, como desde Alertas y el panel del profesor (antes abria el
        /// horario del laboratorio). Aunque aun no haya nadie: la ventana se llena
        /// sola cuando registra el primero. No escribe: tambien el encargado.
        /// </summary>
        private void VerRegistro(SesionAgenda clase)
        {
            PanoramaLaboratorios panorama = panelLaboratorios.Panorama;
            DateTime fecha = panorama == null ? Ahora().Date : panorama.Fecha;
            string laboratorio = panorama == null ? ""
                : panorama.Laboratorios.Where(l => l.Id == clase.LaboratorioId).Select(l => l.Texto).FirstOrDefault() ?? "";
            string horario = (clase.EsExtra ? "Sesión extra de hoy" : "Hoy") + ", " + HorariosDatos.Hora(clase.Inicio) + " a "
                + HorariosDatos.Hora(clase.Fin) + (laboratorio.Length > 0 ? ", " + laboratorio : "");
            // Con "Los docentes no ven los codigos", el que se le da al grupo tambien
            // en la Bitacora de la clase: el docente se lo pide a quien la tiene abierta.
            if (panorama != null && panorama.CodigosSoloResponsables && !string.IsNullOrEmpty(clase.CodigoParaDar))
            {
                horario += ". Código " + TarjetaCodigo.Mayusculas(clase.CodigoParaDar);
            }

            try
            {
                using (BitacoraSesion bitacora = new BitacoraSesion(clase.FranjaId, clase.ExcepcionId, clase.Materia + ", " + clase.Grupo,
                    horario, clase.Profesor, fecha))
                {
                    bitacora.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Abrir la bitacora de " + clase.Descripcion + " desde el panel", ex,
                    "No se pudo abrir la bitácora de la clase", Text);
            }

            // Lo que se vio pudo cambiar el conteo de la tarjeta.
            ActualizarDatos();
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
        /// Asistencias, clases que se dieron y fallas del periodo (VentanaEstadisticas).
        /// No escribe nada: el encargado de laboratorio la ve igual (con el CSV
        /// limitado, lo decide la ventana).
        /// </summary>
        private void AbrirEstadisticas()
        {
            Mostrar("estadisticas", () => new VentanaEstadisticas());
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
                if (ventana != null)
                {
                    estadoDeVentanas.Remove(ventana);
                }

                ventana = crear();
                Form nueva = ventana;
                // Como estaba antes de minimizarla (como el panel, OnSizeChanged): las
                // que se abren maximizadas no vuelven en una ventana pequena.
                estadoDeVentanas[nueva] = nueva.WindowState == FormWindowState.Minimized ? FormWindowState.Normal : nueva.WindowState;
                nueva.SizeChanged += (s, e) =>
                {
                    if (nueva.WindowState != FormWindowState.Minimized)
                    {
                        estadoDeVentanas[nueva] = nueva.WindowState;
                    }
                };
                nueva.FormClosed += (s, e) =>
                {
                    estadoDeVentanas.Remove(nueva);
                    ActualizarDatos();
                };
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
                FormWindowState antes;
                ventana.WindowState = estadoDeVentanas.TryGetValue(ventana, out antes) ? antes : FormWindowState.Normal;
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
            // Al abrir no hay nada con el foco: la primera pastilla no sale con el
            // anillo y un Enter o un Espacio sueltos no abren nada. El primer Tab va
            // a ella (Mi perfil).
            ActiveControl = null;
            relojTimer.Start();
            datosTimer.Start();
            ActualizarDatos();
            alertasTimer = new Timer(componentes) { Interval = SegundosEntreAlertas * 1000 };
            alertasTimer.Tick += (s, ev) => LeerAlertas();
            alertasTimer.Start();
            avisoWindows = new NotifyIcon(componentes)
            {
                Icon = Icon,
                // El titulo de la ventana: el de la administracion o el de responsable de laboratorio.
                Text = Text,
                Visible = true
            };
            avisoWindows.BalloonTipClicked += (s, ev) =>
            {
                TraerAlFrente();
                AbrirAlertas();
            };
            avisoWindows.DoubleClick += (s, ev) => TraerAlFrente();
            LeerAlertas();
        }

        /// <summary>F5: los laboratorios y las alertas se vuelven a leer al momento, como F5 en las listas.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                ActualizarDatos();
                LeerAlertas();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Desde el aviso de Windows: el panel al frente, como estaba antes de minimizarlo.</summary>
        private void TraerAlFrente()
        {
            if (IsDisposed)
            {
                return;
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = estadoSinMinimizar;
            }

            Activate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (WindowState != FormWindowState.Minimized)
            {
                estadoSinMinimizar = WindowState;
            }
        }

        // =====================================================================
        // Alertas
        // =====================================================================

        /// <summary>
        /// Las alertas de los ultimos dias y hasta cuando las vio este
        /// administrador, en segundo plano (como el panel); al llegar, el enlace
        /// del encabezado dice cuantas son nuevas. Si falla, se queda lo de antes.
        /// </summary>
        private void LeerAlertas()
        {
            if (leyendoAlertas || IsDisposed)
            {
                return;
            }

            leyendoAlertas = true;
            string usuario = Datos_User.getUser();
            bool soloConsulta = Permisos.SoloConsulta;
            int version = versionVistas;
            Task.Run(() =>
            {
                AlertasDatos datos = new AlertasDatos();
                ResultadoAlertas leidas = datos.Leer(DiasDeAlertas);
                DateTime? vistas = null;
                try
                {
                    // La responsable, en un equipo donde aun no las vio: desde el inicio de hoy.
                    vistas = datos.LeerVistas(usuario, soloConsulta, leidas.Ahora);
                }
                catch (Exception ex)
                {
                    Log.Error("Leer hasta cuando se vieron las alertas", ex);
                }

                return Tuple.Create(leidas, vistas);
            })
                .ContinueWith(tarea =>
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            leyendoAlertas = false;
                            if (IsDisposed)
                            {
                                return;
                            }

                            if (tarea.IsFaulted)
                            {
                                Log.Aviso("No se pudieron leer las alertas: " + tarea.Exception.GetBaseException().Message);
                                return;
                            }

                            alertas = tarea.Result.Item1;
                            // Lo mas reciente entre lo guardado y lo de esta sesion (si no
                            // se pudo guardar, vale lo de aqui); una lectura vieja no lo pisa.
                            DateTime? vistas = tarea.Result.Item2;
                            if (version == versionVistas && vistas.HasValue && (!alertasVistasHasta.HasValue || vistas.Value > alertasVistasHasta.Value))
                            {
                                alertasVistasHasta = vistas;
                            }

                            MostrarAlertas();
                            AvisarNuevas();
                        }));
                    }
                    catch (InvalidOperationException)
                    {
                        // El formulario se cerro mientras se consultaba.
                    }
                });
        }

        /// <summary>
        /// La notificacion de Windows de las alertas que aparecieron con el panel
        /// abierto y aun no se ven, una por hora como mucho (AvisosDeAlertas). Al
        /// pulsarla se abren las alertas.
        /// </summary>
        private void AvisarNuevas()
        {
            // La responsable: sin lo que no le toca (CuentaComoNueva).
            ResultadoAlertas paraAvisar = alertas == null || !Permisos.SoloConsulta ? alertas
                : new ResultadoAlertas { Ahora = alertas.Ahora, Alertas = alertas.Alertas.Where(CuentaComoNueva).ToList() };
            List<Alerta> nuevas = avisos.QueAvisar(paraAvisar, alertasVistasHasta, DateTime.UtcNow);
            if (nuevas.Count == 0 || avisoWindows == null)
            {
                return;
            }

            avisoWindows.BalloonTipTitle = AvisosDeAlertas.Titulo(nuevas);
            avisoWindows.BalloonTipText = AvisosDeAlertas.Texto(nuevas);
            avisoWindows.BalloonTipIcon = ToolTipIcon.Warning;
            avisoWindows.ShowBalloonTip(10000);
            Log.Info("Aviso de Windows: " + nuevas.Count + (nuevas.Count == 1 ? " alerta nueva." : " alertas nuevas."));
        }

        /// <summary>
        /// La pastilla de Alertas: en rojo con un globo que dice cuantas son nuevas,
        /// o verde si no hay. El ToolTip separa las nuevas en clases, laboratorios y
        /// cambios de horario. No recoloca la cabecera: el ancho con "99+" ya esta
        /// guardado.
        /// </summary>
        private void MostrarAlertas()
        {
            const string Que = QueHayEnAlertas;
            if (alertas == null)
            {
                btnAlertas.Nuevas = 0;
                Ayuda(btnAlertas, char.ToUpper(Que[0]) + Que.Substring(1));
                return;
            }

            List<Alerta> nuevas = alertas.Alertas.Where(a => (!alertasVistasHasta.HasValue || a.Momento > alertasVistasHasta.Value) && CuentaComoNueva(a)).ToList();
            btnAlertas.Nuevas = nuevas.Count;
            int total = alertas.Alertas.Count;
            string periodo = " en los últimos " + DiasDeAlertas + " días";
            string verlas = total == 1 ? " Pulsa para verla." : " Pulsa para verlas.";
            if (total == 0)
            {
                Ayuda(btnAlertas, "Sin alertas" + periodo + ": " + Que);
            }
            else if (nuevas.Count == 0)
            {
                Ayuda(btnAlertas, total + (total == 1 ? " alerta" : " alertas") + periodo + ", ninguna nueva." + verlas);
            }
            else
            {
                // Un cambio de horario no es una clase sin bitacoras: va aparte.
                int clases = nuevas.Count(a => a.Tipo == TipoAlerta.SinBitacoras || a.Tipo == TipoAlerta.EnCursoSinBitacoras);
                int laboratorios = nuevas.Count(a => a.Tipo == TipoAlerta.Fallas);
                int cambios = nuevas.Count(a => a.Tipo == TipoAlerta.CambioDeHorario);
                List<string> partes = new List<string>();
                if (clases > 0)
                {
                    partes.Add(clases + (clases == 1 ? " clase sin bitácoras" : " clases sin bitácoras"));
                }

                if (laboratorios > 0)
                {
                    partes.Add(laboratorios + (laboratorios == 1 ? " laboratorio" : " laboratorios") + " con fallas o equipos sin internet");
                }

                if (cambios > 0)
                {
                    partes.Add(cambios + (cambios == 1 ? " cambio de horario" : " cambios de horario"));
                }

                Alerta porEnlazar = nuevas.FirstOrDefault(a => a.Tipo == TipoAlerta.DocentesPorEnlazar);
                if (porEnlazar != null)
                {
                    partes.Add(porEnlazar.PorEnlazar + (porEnlazar.PorEnlazar == 1 ? " docente por enlazar con el SIIA" : " docentes por enlazar con el SIIA"));
                }

                Ayuda(btnAlertas, nuevas.Count + (nuevas.Count == 1 ? " nueva" : " nuevas") + " de " + total + periodo
                    + (partes.Count == 0 ? "." : ": " + EstiloAdmin.Enumerar(partes) + ".") + verlas);
            }
        }

        /// <summary>
        /// Si una alerta cuenta en la campana y en el aviso de Windows. A la
        /// responsable (solo consulta) no le cuentan los docentes por enlazar con el
        /// SIIA, que no puede enlazar (siguen en la lista de Alertas): una campana
        /// siempre en rojo deja de servir. La ventana de Alertas usa lo mismo para
        /// sus nuevas (negritas, el punto y "(N nuevas)"): asi dice lo que la campana.
        /// </summary>
        internal static bool CuentaComoNueva(Alerta alerta)
        {
            return !(Permisos.SoloConsulta && alerta.Tipo == TipoAlerta.DocentesPorEnlazar);
        }

        /// <summary>
        /// Abre las alertas (las nuevas en negritas); si ya estaba abierta, la trae
        /// al frente y la vuelve a leer. No las da por vistas al abrir, sino cuando
        /// la ventana las ensena todas (MarcarVistas): si su lectura falla o un
        /// filtro esconde alguna, siguen siendo nuevas.
        /// </summary>
        private void AbrirAlertas()
        {
            // Antes de la primera lectura (o si fallo) no se sabe hasta cuando las
            // vio, y saldrian todas como nuevas. La responsable, en un equipo donde
            // aun no las vio (con Deep Freeze, tras cada reinicio), desde el inicio de
            // hoy: con la hora del servidor de la lectura o, si aun no hay ninguna, con
            // la de este equipo (sin eso salian las 65 de la semana como nuevas, y al
            // ensenarlas todas se daban por vistas). La administracion no la usa.
            if (!alertasVistasHasta.HasValue)
            {
                try
                {
                    DateTime? guardadas = new AlertasDatos().LeerVistas(Datos_User.getUser(), Permisos.SoloConsulta,
                        alertas == null ? DateTime.Now : alertas.Ahora);
                    if (guardadas.HasValue)
                    {
                        alertasVistasHasta = guardadas;
                        MostrarAlertas();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Leer hasta cuando se vieron las alertas", ex);
                }
            }

            Form previa;
            VentanaAlertas abierta = ventanas.TryGetValue("alertas", out previa) && !previa.IsDisposed ? previa as VentanaAlertas : null;
            DateTime? antes = alertasVistasHasta;
            Mostrar("alertas", () =>
            {
                VentanaAlertas ventana = new VentanaAlertas(antes);
                ventana.Leidas += MarcarVistas;
                ventana.VerBitacoras += AbrirBitacorasDeAlerta;
                ventana.VerHorario += AbrirHorarios;
                ventana.VerDocentesPorEnlazar += AbrirDocentesPorEnlazar;
                ventana.FormClosed += (s, e) => LeerAlertas();
                return ventana;
            });

            // Ya al frente: sus nuevas, las de despues de lo visto hasta ahora.
            if (abierta != null)
            {
                abierta.Renovar(antes);
            }
        }

        /// <summary>
        /// La ventana de alertas enseno todo lo que leyo: vistas hasta la hora del
        /// servidor de esa lectura (nunca hacia atras), en configuracion para este
        /// administrador. Las del encargado de laboratorio, que no escribe en la
        /// base, en un archivo de su equipo (lo decide AlertasDatos).
        /// </summary>
        private void MarcarVistas(DateTime hasta)
        {
            if (IsDisposed || (alertasVistasHasta.HasValue && hasta <= alertasVistasHasta.Value))
            {
                return;
            }

            alertasVistasHasta = hasta;
            versionVistas++;
            MostrarAlertas();
            string usuario = Datos_User.getUser();
            bool soloConsulta = Permisos.SoloConsulta;
            Task.Run(() =>
            {
                try
                {
                    new AlertasDatos().GuardarVistas(usuario, hasta, soloConsulta);
                }
                catch (Exception ex)
                {
                    Log.Error("Guardar hasta cuando se vieron las alertas", ex);
                }
            });
        }

        /// <summary>Las bitacoras con fallas de ese laboratorio ese dia.</summary>
        private void AbrirBitacorasDeAlerta(Alerta alerta)
        {
            GestionBitacorasAdministrador bitacoras = Mostrar("bitacoras", () => new GestionBitacorasAdministrador()) as GestionBitacorasAdministrador;
            if (bitacoras != null)
            {
                bitacoras.Filtrar(alerta.Fecha, alerta.Laboratorio, true);
            }
        }

        private void ActualizarReloj()
        {
            DateTime ahora = Ahora();
            lblReloj.Text = ahora.ToString("HH:mm:ss");
            // Cada dia la fecha mide distinto: con la nueva se vuelve a colocar (y
            // ColocarCabecera decide si va entera o sin el ano).
            if (ahora.Date != diaMostrado)
            {
                diaMostrado = ahora.Date;
                ColocarCabecera();
            }

            panelLaboratorios.Ahora = ahora;

            // Paso la medianoche: la agenda cargada es la de ayer.
            PanoramaLaboratorios panorama = panelLaboratorios.Panorama;
            if (panorama != null && panorama.Fecha != ahora.Date && !sinConexionDesde.HasValue)
            {
                ActualizarDatos();
            }
        }

        /// <summary>
        /// Pide la agenda en segundo plano: con el servidor caido, la ventana no se
        /// congela. Si ya hay una lectura en vuelo, se pide otra al acabar (al
        /// cerrar una seccion siempre se ven datos posteriores a lo que se guardo).
        /// </summary>
        private void ActualizarDatos()
        {
            if (IsDisposed)
            {
                return;
            }

            if (actualizando)
            {
                otraLecturaPendiente = true;
                return;
            }

            actualizando = true;
            versionColores = ColoresProfesor.Version;
            // Con "Los docentes no ven los codigos", tambien el de cada clase (para darselo al grupo).
            Task.Run(() => new HorariosDatos().PanoramaDelDia(null, true))
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

            try
            {
                AplicarPanorama(tarea);
            }
            finally
            {
                if (otraLecturaPendiente)
                {
                    otraLecturaPendiente = false;
                    ActualizarDatos();
                }
            }
        }

        private void AplicarPanorama(Task<PanoramaLaboratorios> tarea)
        {
            if (tarea.IsFaulted)
            {
                Exception error = tarea.Exception.GetBaseException();
                if (!sinConexionDesde.HasValue)
                {
                    sinConexionDesde = Ahora();
                    Log.Aviso("Panel del administrador: no se pudo leer la agenda de los laboratorios: " + error.Message);
                }

                // La red solo si no se llego al servidor; si no, la causa (una migracion
                // que falta no se arregla esperando la conexion).
                bool sinRed = ColaBitacoras.Clasificar(error) == ClaseDeFallo.SinConexion;
                panelLaboratorios.Avisar((sinRed ? "Sin conexión con el servidor desde las " + sinConexionDesde.Value.ToString("HH:mm")
                        : "No se pudo leer la agenda desde las " + sinConexionDesde.Value.ToString("HH:mm") + ": " + AvisoDeFalloAdmin.Causa(error))
                    + ". Se reintenta cada " + SegundosEntreActualizaciones + " s; lo que ves puede no estar al día.");
                lblEstado.Text = "Último intento a las " + Ahora().ToString("HH:mm") + (sinRed ? " (sin conexión)" : " (con error)");
                return;
            }

            if (sinConexionDesde.HasValue)
            {
                Log.Info("Panel del administrador: conexion recuperada.");
                sinConexionDesde = null;
            }

            PanoramaLaboratorios panorama = tarea.Result;
            desfaseServidor = panorama.AhoraServidor - DateTime.Now;
            // Cada profesor con el mismo color que en Horarios (el elegido, si tiene).
            // Si mientras se leia otra pantalla ya reparto (por ejemplo Profesores
            // al guardar un color), sus datos son mas nuevos: no se pisan.
            if (ColoresProfesor.Version == versionColores)
            {
                ColoresProfesor.Repartir(panorama.OrdenDeProfesores, panorama.ColoresElegidos);
            }

            MostrarAcceso(panorama);
            panelLaboratorios.Mostrar(panorama);
            ActualizarReloj();
            // Que es la hora del servidor y cada cuanto se renueva, en su ayuda (CrearTituloPanel).
            lblEstado.Text = "Actualizado a las " + Ahora().ToString("HH:mm");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ColoresProfesor.Cambio -= RepintarPanel;
            ModosDeAcceso.Cambio -= ModosCambiaron;
            relojTimer.Stop();
            datosTimer.Stop();
            if (alertasTimer != null)
            {
                alertasTimer.Stop();
            }

            // Que no quede el icono en el area de notificacion al cerrar sesion.
            if (avisoWindows != null)
            {
                avisoWindows.Visible = false;
            }

            // La lectura guardada de Estadisticas: otra cuenta no la reutiliza y no
            // quedan datos de estudiantes en memoria al cerrar la sesion.
            EstadisticasDatos.Olvidar();

            base.OnFormClosed(e);
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

            ActualizarDatos();
        }

        private void RepintarPanel()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RepintarPanel));
                return;
            }

            panelLaboratorios.Invalidate();
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
