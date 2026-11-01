using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;

namespace DigBit
{
    public partial class Login : Form
    {
        private Conexion mconexion;
        private Consultas consultas = new Consultas();
        // "INICIAR  SESION" del disenador, para volver a el despues de "ENTRANDO...".
        private readonly string textoIniciarSesion;

        public Login()
        {
            InitializeComponent();
            // La tarjeta, el fondo y los textos (Login.Diseno.cs).
            ConstruirDiseno();
            textoIniciarSesion = btnIniciarSesion.Text;
            mconexion = new Conexion();
            Kiosco.Aplicar(this);
            if (AppContexto.Actual != null)
            {
                AppContexto.Actual.Registrar(this);
            }

            // En kiosco no hay a donde salir ni sentido en minimizar.
            imgSalir.Visible = !Kiosco.Activo;
            imgMinimizar.Visible = !Kiosco.Activo;

            // Texto guia (gris, tambien con el cursor dentro); el del usuario lo pone
            // ActualizarSubtitulo, segun el equipo.
            EstiloHorarios.PonerTextoGuia(txtPassword, "Tu contraseña");
            // Se escribe en cuanto aparece la ventana; Enter es "Iniciar sesion"
            // (AcceptButton, en el disenador). Con el foco en el enlace, Enter lo
            // abre: un LinkLabel es IButtonControl y entonces es el boton por defecto.
            ActiveControl = txtUsuario;

            // Fase 4: franja de aviso cuando se trabaja con la copia local.
            lblSinConexion = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 34,
                BackColor = Color.FromArgb(255, 204, 128),
                ForeColor = Color.Black,
                Font = new Font("Century Gothic", 9F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            Controls.Add(lblSinConexion);
            lblSinConexion.BringToFront();
            ActualizarAvisoSinConexion();

            temporizadorAviso = new Timer { Interval = 5000 };
            temporizadorAviso.Tick += (s, e) => ActualizarAvisoSinConexion();
            temporizadorAviso.Start();

            PrepararBotonApagar();
            Colocar();

            // Con los estudiantes entrando con el SIIA, el kiosco no ofrece "Crear
            // cuenta" (se crea sola). Se mira al ensenarse (antes no hay ventana a
            // la que avisar) y cada minuto, sin congelar.
            Shown += (s, e) => RevisarSiia();
            temporizadorSiia = new Timer { Interval = 60000 };
            temporizadorSiia.Tick += (s, e) => RevisarSiia();
            temporizadorSiia.Start();
        }

        private Timer temporizadorSiia;
        // Lo ultimo leido de Ajustes: los estudiantes entran con el SIIA.
        private bool estudiantesConSiia;
        private bool revisandoSiia;

        private void RevisarSiia()
        {
            if (revisandoSiia || IsDisposed)
            {
                return;
            }

            revisandoSiia = true;
            Task.Run(() => InicioConSiia.LeerSinFallar()).ContinueWith(tarea =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    revisandoSiia = false;
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        revisandoSiia = false;
                        estudiantesConSiia = !tarea.IsFaulted && tarea.Result.Estudiantes;
                        docentesConSiia = !tarea.IsFaulted && tarea.Result.Docentes;
                        if (esperaNip == null)
                        {
                            // Con el SIIA la cuenta del estudiante se crea sola: sin "Crear cuenta".
                            btnRegistro.Visible = !estudiantesConSiia;
                            ActualizarSubtitulo();
                            Colocar();
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    revisandoSiia = false;
                }
            });
        }

        private Label lblSinConexion;
        private Timer temporizadorAviso;
        private Button btnApagar;

        /// <summary>
        /// En el kiosco no hay menu Inicio ni barra de tareas, asi que sin esto
        /// la unica forma de apagar es Ctrl+Alt+Supr, que mucha gente no conoce,
        /// o el boton fisico. Tiene que verse y decir lo que hace.
        ///
        /// Fuera del kiosco no se pone: ahi Windows ya tiene su propio menu, y
        /// ademas ApagarEquipo() no apaga nada en un equipo de desarrollo.
        /// </summary>
        private void PrepararBotonApagar()
        {
            if (!Kiosco.Activo)
            {
                return;
            }

            btnApagar = new Button
            {
                Text = "Apagar el equipo",
                Size = new Size(200, 46),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Century Gothic", 10F, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 52, 30),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                // Sin TabStop: que no se apague el equipo por darle a Tab y Enter
                // mientras se teclea la contrasena.
                TabStop = false
            };
            btnApagar.FlatAppearance.BorderSize = 0;
            btnApagar.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 40, 22);
            btnApagar.Click += (s, e) => ConfirmarApagado();
            Controls.Add(btnApagar);
            btnApagar.BringToFront();

            ColocarBotonApagar();
            PrepararBotonEmergencia();
            Resize += (s, e) => { ColocarBotonApagar(); ColocarBotonEmergencia(); };
        }

        private Button btnEmergencia;

        /// <summary>
        /// Salida para cuando DigBit no puede validar ningun codigo: sin
        /// servidor y sin copia local utilizable, el laboratorio se queda
        /// inservible. Pide clave y memoria USB (los dos), y abre el escritorio
        /// sin bitacora.
        ///
        /// Se muestra siempre, no solo cuando falla el servidor: asi tambien
        /// sirve si el problema es otro (un horario mal cargado, un codigo que
        /// no llega). A cambio es una puerta permanente, y lo unico que la
        /// cierra es que la memoria este en el llavero del encargado. Cada uso
        /// queda registrado.
        /// </summary>
        private void PrepararBotonEmergencia()
        {
            if (!Kiosco.Activo)
            {
                return;
            }

            btnEmergencia = new Button
            {
                Text = "Acceso de emergencia",
                Size = new Size(200, 32),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Century Gothic", 8.5F),
                BackColor = Color.FromArgb(0, 84, 48),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnEmergencia.FlatAppearance.BorderSize = 1;
            btnEmergencia.FlatAppearance.BorderColor = Color.FromArgb(0, 52, 30);
            btnEmergencia.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 66, 38);
            btnEmergencia.Click += (s, e) => AbrirEmergencia();
            Controls.Add(btnEmergencia);
            btnEmergencia.BringToFront();
            ColocarBotonEmergencia();
        }

        /// <summary>Justo encima del de apagar, y mas discreto que el.</summary>
        private void ColocarBotonEmergencia()
        {
            if (btnEmergencia == null || IsDisposed)
            {
                return;
            }

            const int margen = 24;
            int franja = AltoFranja();
            int altoApagar = (btnApagar != null) ? btnApagar.Height + 10 : 0;
            btnEmergencia.Location = new Point(
                ClientSize.Width - btnEmergencia.Width - margen,
                ClientSize.Height - btnEmergencia.Height - margen - franja - altoApagar);
        }

        private void AbrirEmergencia()
        {
            if (!DialogoEmergencia.Pedir(this))
            {
                return;
            }

            this.Hide();
            if (!SesionEquipo.LiberarEmergencia())
            {
                this.Show();
                MessageBox.Show(
                    "No se pudo abrir el escritorio." + Environment.NewLine + Environment.NewLine +
                    "Avisa a la persona responsable del laboratorio.",
                    "Acceso de emergencia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Abajo a la derecha, por encima de la franja de sin conexion si la hay.</summary>
        private void ColocarBotonApagar()
        {
            if (btnApagar == null || IsDisposed)
            {
                return;
            }

            const int margen = 24;
            int franja = AltoFranja();
            btnApagar.Location = new Point(
                ClientSize.Width - btnApagar.Width - margen,
                ClientSize.Height - btnApagar.Height - margen - franja);
        }

        /// <summary>
        /// Lo que ocupa abajo la franja de sin conexion. Se mira su texto y no
        /// Visible: antes de mostrarse el login, Visible de la franja es false aunque
        /// ya se le haya puesto true (su ventana aun no se ve), y los botones del
        /// kiosco se colocaban tapandola hasta el primer tic del reloj de 5 s.
        /// </summary>
        private int AltoFranja()
        {
            return (lblSinConexion != null && lblSinConexion.Text.Length > 0) ? lblSinConexion.Height : 0;
        }

        private void ConfirmarApagado()
        {
            DialogResult respuesta = MessageBox.Show(
                "¿Seguro que quieres apagar el equipo?" + Environment.NewLine + Environment.NewLine +
                "Guarda antes tu trabajo.",
                "Apagar el equipo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (respuesta != DialogResult.Yes)
            {
                return;
            }

            // Con bitacoras sin enviar, primero se intenta enviarlas.
            if (!AvisoApagado.PuedeApagar(this))
            {
                return;
            }

            if (!SesionEquipo.ApagarEquipo())
            {
                // ApagarEquipo ya dejo en el registro que fallo (el privilegio o la orden).
                MessageBox.Show(
                    "No se pudo apagar el equipo: Windows no aceptó la orden de apagado." + Environment.NewLine + Environment.NewLine +
                    "Avisa a la persona responsable del laboratorio.",
                    "Apagar el equipo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void ActualizarAvisoSinConexion()
        {
            if (lblSinConexion == null || IsDisposed)
            {
                return;
            }

            string texto = SinConexion.TextoAviso();
            lblSinConexion.Text = texto;
            lblSinConexion.Visible = texto.Length > 0;
            // En el kiosco la franja va a todo lo ancho de la pantalla y crece con el
            // texto (con "la persona responsable del laboratorio" puede pasar de dos
            // lineas). En una ventana de 400 se queda en 34: mas alta taparia los botones.
            if (Kiosco.Activo && texto.Length > 0)
            {
                int alto = TextRenderer.MeasureText(texto, lblSinConexion.Font, new Size(Math.Max(100, ClientSize.Width - 8), int.MaxValue),
                    TextFormatFlags.WordBreak).Height + 14;
                lblSinConexion.Height = Math.Max(34, alto);
            }

            ColocarBotonApagar();
            ColocarBotonEmergencia();
            // La tarjeta va al centro de lo que deja libre la franja: solo si cambio.
            if (AltoFranja() != altoFranjaColocada)
            {
                altoFranjaColocada = AltoFranja();
                Colocar();
            }
        }

        private int altoFranjaColocada;

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (temporizadorAviso != null)
            {
                temporizadorAviso.Stop();
                temporizadorAviso.Dispose();
                temporizadorAviso = null;
            }

            if (temporizadorSiia != null)
            {
                temporizadorSiia.Stop();
                temporizadorSiia.Dispose();
                temporizadorSiia = null;
            }

            base.OnFormClosed(e);
        }


        //Se configura la imagen "imgSalir" para que al momento de dar clic se cierre la aplicacion (solo fuera del kiosco)
        private void imgSalir_Click(object sender, EventArgs e)
        {
            AppContexto.Actual.Salir();
        }

        // Se configura la imagen "imgMinimizar" para que al momento de dar clic de minimice la ventana
        private void imgMinimizar_Click(object sender, EventArgs e)
        {
            if (!Kiosco.Activo)
            {
                this.WindowState = FormWindowState.Minimized;
            }
        }

        // Mientras se comprueba una cuenta (con el SIIA, hasta unos segundos en
        // segundo plano): la ventana se mueve y se repinta, pero no se empieza otra
        // cosa (otro inicio de sesion, "Crear cuenta", "Olvidaste tu contrasena?").
        private bool entrando;

        // El paso del NIP del SIIA (PedirNip): la misma tarjeta, con la caja de la
        // contrasena para el NIP. Mientras se espera, la tarea que lo recibe.
        private TaskCompletionSource<string> esperaNip;
        private string textoRegistro;
        private string textoEnlace;
        private bool cajaSoloCifras;

        private async void btnIniciarSesion_Click(object sender, EventArgs e)
        {
            // En el paso del NIP, "Entrar" entrega el NIP a quien lo espera.
            if (esperaNip != null)
            {
                EntregarNip();
                return;
            }

            if (entrando)
            {
                return;
            }

            // Una matricula no lleva espacios: los de mas al principio o al final se quitan.
            string numeroIdentificadorIngresado = txtUsuario.Text.Trim();
            string contraseñaIngresada = txtPassword.Text;

            // Con el texto guia, una caja sin escribir esta vacia de verdad.
            bool sinUsuario = numeroIdentificadorIngresado.Length == 0;
            bool sinContrasena = string.IsNullOrWhiteSpace(contraseñaIngresada);
            if (sinUsuario || sinContrasena)
            {
                MessageBox.Show(sinUsuario && sinContrasena ? "Escribe tu usuario y tu contraseña."
                    : sinUsuario ? "Escribe tu usuario." : "Escribe tu contraseña.",
                    "Iniciar sesión", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                (sinUsuario ? txtUsuario : txtPassword).Focus();
                return;
            }

            // Validar que el usuario no contenga caracteres especiales
            if (!System.Text.RegularExpressions.Regex.IsMatch(numeroIdentificadorIngresado, @"^[a-zA-Z0-9]+$"))
            {
                MessageBox.Show("Tu usuario solo puede llevar letras y números, sin espacios ni símbolos.",
                    "Iniciar sesión", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtUsuario.SelectAll();
                txtUsuario.Focus();
                return;
            }

            bool entro = false;
            MostrarEntrando(true);
            try
            {
                entro = await Entrar(numeroIdentificadorIngresado, contraseñaIngresada);
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro.
                Log.Error("Iniciar sesion de '" + numeroIdentificadorIngresado + "'", ex);
                MostrarEntrando(false);
                MessageBox.Show("No se pudo iniciar sesión por un error inesperado." + Environment.NewLine + Environment.NewLine
                    + "Inténtalo de nuevo; si sigue fallando, avisa a la persona responsable del laboratorio.",
                    "Iniciar sesión", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Despues del paso del NIP, la caja vuelve a ser la de la contrasena.
                SalirDelPasoNip();
                MostrarEntrando(false);
            }

            if (entro)
            {
                txtPassword.Clear();
                this.Hide();
                return;
            }

            // Lo mas comun es una contrasena mal escrita: queda lista para escribirla otra vez.
            if (!IsDisposed && Visible)
            {
                txtPassword.SelectAll();
                txtPassword.Focus();
            }
        }

        /// <summary>
        /// El paso 2 del SIIA, en esta misma ventana: la caja de la contrasena pasa
        /// a pedir el NIP (4 numeros, oculto), "¿Olvidaste tu NIP?" y "Volver". La
        /// contrasena se queda solo en la memoria de quien la pidio. Devuelve el NIP,
        /// o null si la persona vuelve atras (Volver, Esc o salir del kiosco).
        /// </summary>
        private Task<string> PedirNip(string aviso)
        {
            if (IsDisposed)
            {
                return Task.FromResult<string>(null);
            }

            if (textoRegistro == null)
            {
                textoRegistro = btnRegistro.Text;
                textoEnlace = linkPasswd.Text;
            }

            if (!cajaSoloCifras)
            {
                CajaNip.SoloCifras(txtPassword);
                cajaSoloCifras = true;
            }

            // Arriba, quien es (el nombre que dio el SIIA, ya con la contrasena comprobada).
            string nombre = consultas.NombreEnElPasoNip;
            lblTitulo.Text = "Escribe tu NIP";
            lblSubtitulo.ForeColor = nombre != null ? VerdeOscuro : GrisTexto;
            lblSubtitulo.Text = (nombre != null ? "Hola, " + nombre + "." + Environment.NewLine : "") + "Es el NIP de 4 dígitos del SIIA.";
            lblEtiquetaPassword.Text = "NIP del SIIA";
            PrepararAvisoNip();
            if (lnkAvisoNip != null)
            {
                lnkAvisoNip.Visible = true;
            }

            UsuarioSoloLectura(true);
            OcultarPassword();
            txtPassword.Clear();
            txtPassword.MaxLength = 4;
            EstiloHorarios.PonerTextoGuia(txtPassword, "4 dígitos");
            btnRegistro.Text = "Volver";
            btnRegistro.Visible = true;
            linkPasswd.Text = "¿Olvidaste tu NIP?";
            MostrarEntrando(false);
            btnIniciarSesion.Text = "Entrar";
            MostrarMensaje(aviso);
            // Lo que sigue al NIP no corre dentro del clic de "Entrar".
            esperaNip = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ActiveControl = txtPassword;
            txtPassword.Focus();
            return esperaNip.Task;
        }

        /// <summary>"Entrar" en el paso del NIP: solo 4 numeros; luego se comprueba (otra vez "Entrando...").</summary>
        private void EntregarNip()
        {
            string nip = txtPassword.Text;
            if (!InicioConSiia.NipValido(nip))
            {
                MostrarMensaje("El NIP son 4 números.");
                txtPassword.Focus();
                return;
            }

            TaskCompletionSource<string> espera = esperaNip;
            esperaNip = null;
            txtPassword.Clear();
            MostrarMensaje(null);
            MostrarEntrando(true);
            espera.TrySetResult(nip);
        }

        /// <summary>"Volver" (o Esc): sin NIP; quien lo esperaba no entra y la ventana vuelve al paso 1.</summary>
        private void CancelarNip()
        {
            TaskCompletionSource<string> espera = esperaNip;
            esperaNip = null;
            if (espera != null)
            {
                espera.TrySetResult(null);
            }
        }

        /// <summary>La tarjeta como al principio: la caja vuelve a ser la de la contrasena.</summary>
        private void SalirDelPasoNip()
        {
            CancelarNip();
            if (textoRegistro == null || IsDisposed)
            {
                return;
            }

            if (lnkAvisoNip != null)
            {
                lnkAvisoNip.Visible = false;
            }

            UsuarioSoloLectura(false);
            OcultarPassword();
            txtPassword.MaxLength = 32767;
            EstiloHorarios.PonerTextoGuia(txtPassword, "Tu contraseña");
            lblEtiquetaPassword.Text = "Contraseña";
            btnRegistro.Text = textoRegistro;
            btnRegistro.Visible = !estudiantesConSiia;
            linkPasswd.Text = textoEnlace;
            ActualizarSubtitulo();
            MostrarMensaje(null);
        }

        /// <summary>En el paso del NIP el usuario ya no se cambia: la caja, gris y de solo lectura.</summary>
        private void UsuarioSoloLectura(bool soloLectura)
        {
            txtUsuario.ReadOnly = soloLectura;
            txtUsuario.BackColor = soloLectura ? FondoSoloLectura : Color.White;
            lblIconoUsuario.BackColor = txtUsuario.BackColor;
            panelTarjeta.Invalidate();
        }

        /// <summary>Esc en el paso del NIP: volver al paso 1.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && esperaNip != null)
            {
                CancelarNip();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Prueba el servidor (o sigue con la copia local) y abre la ventana de la
        /// cuenta. True si la abrio; los avisos de por que no, los ensena por el camino.
        /// Solo un docente con el SIIA encendido espera algo en segundo plano.
        /// </summary>
        private async Task<bool> Entrar(string numeroIdentificadorIngresado, string contraseñaIngresada)
        {
            // Fase 4: sin servidor, el alumno entra solo con su matricula contra la
            // copia local; su bitacora quedara marcada como registrada sin conexion.
            if (!SinConexion.Activo)
            {
                try
                {
                    Conexion.Probar();
                }
                catch (Exception ex)
                {
                    Log.Error("El servidor no responde al iniciar sesion", ex);
                    if (!SinConexion.IntentarActivar(ex.Message))
                    {
                        MostrarEntrando(false);
                        MessageBox.Show("No hay conexión con el servidor y este equipo no tiene una copia del horario. Avisa a la persona responsable del laboratorio."
                            + ParaElEstudiante(AppContexto.AvisoAlEstudiante),
                            "Sin conexión", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    ActualizarAvisoSinConexion();
                }
            }

            if (SinConexion.Activo)
            {
                AlumnoCache alumno = SinConexion.Cache.BuscarAlumno(numeroIdentificadorIngresado);
                if (alumno == null)
                {
                    MostrarEntrando(false);
                    MessageBox.Show("Sin conexión solo pueden entrar estudiantes, y esa matrícula no está en la copia local de este equipo."
                        + ParaElEstudiante("Revisa que esté bien escrita. Si lo está y vienes a una clase, avísale ahora a tu docente "
                            + "para que te dé la asistencia."),
                        "Sin conexión", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                Log.Aviso("Alumno " + alumno.Matricula + " entra SIN CONEXION (matricula sin contrasena).");
                // Nada de la sesion anterior (ni la cuenta de solo consulta de un encargado).
                Datos_User.Limpiar();
                // Con true el login se esconde, como con servidor (antes se quedaba abierto detras).
                return consultas.AbrirPestanaSegunTipoUsuario(1, alumno.Matricula);
            }

            // Consultas abre la ventana segun el tipo de la cuenta: el alumno (1)
            // y el profesor (2), la suya; el administrador (3) y el encargado de
            // laboratorio (4), el panel del administrador, el encargado en solo
            // consulta. Si esta version no conoce el tipo, devuelve false y el
            // login se queda a la vista. El administrador es un usuario mas de la
            // base desde la fase 6; antes eran credenciales escritas aqui. Un
            // docente o un estudiante, con el SIIA encendido para ellos en Ajustes,
            // se comprueba con el SIIA, con su NIP en esta misma ventana.
            return await consultas.RealizarInicioSesionAsync(numeroIdentificadorIngresado, contraseñaIngresada,
                aviso => PedirNip(aviso));
        }

        /// <summary>
        /// Un parrafo mas para el estudiante cuando este equipo no llega al servidor
        /// (que le avise a su docente, que solo puede darle la asistencia mientras
        /// dura la clase). Solo en el kiosco (los equipos del laboratorio): en el del
        /// docente o el de la administracion, quien lee no es un estudiante.
        /// </summary>
        private static string ParaElEstudiante(string texto)
        {
            return Kiosco.Activo ? Environment.NewLine + Environment.NewLine + texto : "";
        }

        /// <summary>
        /// Mientras se prueba el servidor y se comprueba la cuenta (sin servidor,
        /// hasta que se agota la espera de la conexion, unos 5 s): cursor de
        /// espera, el boton desactivado y "ENTRANDO...". Todo va en el hilo de la
        /// ventana, asi que el boton se repinta antes de empezar. Los avisos de esta
        /// ventana lo quitan antes de salir: detras de un aviso ya no se esta entrando.
        /// </summary>
        private void MostrarEntrando(bool entrando)
        {
            if (IsDisposed)
            {
                return;
            }

            this.entrando = entrando;
            btnIniciarSesion.Enabled = !entrando;
            btnIniciarSesion.Text = entrando ? "Entrando..." : textoIniciarSesion;
            // UseWaitCursor tambien mientras se espera al SIIA: entonces la ventana
            // procesa mensajes y Cursor.Current volveria a la flecha.
            UseWaitCursor = entrando;
            Cursor.Current = entrando ? Cursors.WaitCursor : Cursors.Default;
            if (entrando)
            {
                btnIniciarSesion.Refresh();
            }
        }



        /// <summary>
        /// "Crear cuenta", tambien en el kiosco: el alumno que no esta dado de alta
        /// se registra aqui mismo, con los datos que le pediria el administrador.
        /// El usuario de MySQL de los equipos, cuya contrasena esta en
        /// connections.config y el alumno puede leer, solo tiene INSERT en usuarios
        /// POR COLUMNA y sin fk_tipo_usuario, que vale 1 (alumno) por defecto: con
        /// esas credenciales se crean alumnos y nada mas. Ver
        /// db/07_usuarios_minimos.sql y db/10_red_y_autoregistro.sql.
        /// </summary>
        private async void btnRegistro_Click(object sender, EventArgs e)
        {
            // En el paso del NIP es "Volver".
            if (esperaNip != null)
            {
                CancelarNip();
                return;
            }

            if (entrando)
            {
                return;
            }

            // Con los estudiantes entrando con el SIIA no hace falta crear cuenta:
            // se crea sola la primera vez (el kiosco ni ensena el boton).
            if (estudiantesConSiia || InicioConSiia.LeerSinFallar().Estudiantes)
            {
                MessageBox.Show("Ya no hace falta crear una cuenta: entra con tu matrícula, tu contraseña y tu NIP del SIIA, y tu cuenta de DigBit "
                    + "se crea sola la primera vez.", "Crear cuenta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Sin servidor no se puede: ni saber si la matricula ya existe ni
            // guardar la cuenta. Y sin la migracion 10 la base no admite un
            // alumno sin grupo. Las dos cosas se miran antes de que el alumno lo
            // escriba todo.
            bool hayServidor = !SinConexion.Activo;
            bool disponible = false;
            if (hayServidor)
            {
                // Sin servidor la consulta tarda lo que la espera de la conexion.
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    disponible = new AlumnosDatos().AltaDisponible();
                }
                catch (Exception ex)
                {
                    Log.Error("El servidor no responde al crear una cuenta", ex);
                    hayServidor = false;
                    if (SinConexion.IntentarActivar(ex.Message))
                    {
                        ActualizarAvisoSinConexion();
                    }
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }

            if (!hayServidor)
            {
                // Sin cuenta su docente no puede darle la asistencia. En un equipo libre
                // con conexion la crea y ahi mismo registra su bitacora; si no hay
                // ninguno, que lo sepa su docente (sin prometer lo que no puede hacer).
                MessageBox.Show("Para crear tu cuenta hace falta conexión con el servidor, y ahora no la hay. "
                    + "Inténtalo más tarde o avisa a la persona responsable del laboratorio."
                    + ParaElEstudiante("Si vienes a una clase, créala en otro equipo que sí tenga conexión y registra ahí tu bitácora. "
                        + "Si no hay ninguno libre, avísale a tu docente."),
                    "Sin conexión", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!disponible)
            {
                MessageBox.Show("Todavía no se pueden crear cuentas desde el laboratorio. Pide a la administración de DigBit que te dé de alta.",
                    "Crear cuenta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string matricula;
            string contrasena;
            if (!DialogoUsuario.CrearCuenta(this, out matricula, out contrasena))
            {
                return;
            }

            // Recien creada, se entra con ella sin volver a escribirla. Si eso
            // falla (RealizarInicioSesion ya enseno su error), que el alumno sepa
            // que la cuenta si existe: si no, la volveria a crear y le diria que
            // esa matricula ya esta tomada.
            bool entro = false;
            MostrarEntrando(true);
            try
            {
                entro = await consultas.RealizarInicioSesionAsync(matricula, contrasena);
            }
            catch (Exception ex)
            {
                Log.Error("Entrar con la cuenta recien creada", ex);
            }
            finally
            {
                MostrarEntrando(false);
            }

            if (entro)
            {
                this.Hide();
                return;
            }

            txtUsuario.Text = matricula;
            txtPassword.Clear();
            MessageBox.Show("Tu cuenta quedó creada, pero no se pudo entrar con ella ahora." + Environment.NewLine + Environment.NewLine
                + "Inicia sesión con tu matrícula y tu contraseña.", "Crear cuenta", MessageBoxButtons.OK, MessageBoxIcon.Information);
            // La matricula ya esta escrita: falta la contrasena.
            txtPassword.Focus();
        }

        /// <summary>
        /// "Olvidaste tu contrasena?", tambien en el kiosco: la API de recuperacion
        /// (api/recuperar.php, en el hosting de la escuela) le manda un codigo a su
        /// correo y con el pone una contrasena nueva. Su direccion la pone el
        /// administrador en Ajustes (la base) y vale para todos los equipos; sin
        /// ella solo queda pedirle al administrador de DigBit una contrasena nueva.
        /// </summary>
        private void linkPasswd_LinkClicked_1(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // En el paso del NIP: "¿Olvidaste tu NIP?", a la pagina del SIIA.
            if (esperaNip != null)
            {
                VentanaPaginaSiia.Recuperar(this, InicioConSiia.LeerSinFallar());
                txtPassword.Focus();
                return;
            }

            if (entrando)
            {
                return;
            }

            ApiRecuperacion api;
            // Con el SIIA encendido, la contrasena de quien entra con el (docente o
            // estudiante) es la del SIIA y se recupera alli (tambien en el kiosco,
            // sin abrir ningun navegador). La recuperacion por correo de DigBit
            // queda para las cuentas propias de DigBit (la administracion...).
            ConfiguracionSiia siia;
            string escrita = txtUsuario.Text.Trim();
            bool conSiia = false;
            Cursor anterior = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                api = RecuperacionContrasena.Buscar();
                siia = InicioConSiia.LeerSinFallar();
                if (siia.Activo)
                {
                    try
                    {
                        // Sin escribir nada: con el SIIA encendido, lo mas probable es que
                        // sea un estudiante o un docente (la administracion, que recupera
                        // por correo, escribe antes su usuario).
                        conSiia = escrita.Length == 0 ? siia.Activo
                            : System.Text.RegularExpressions.Regex.IsMatch(escrita, @"^[a-zA-Z0-9]+$") && siia.ParaElTipo(InicioConSiia.TipoDeCuenta(escrita));
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Recuperar contrasena: saber si '" + escrita + "' entra con el SIIA", ex);
                    }
                }
            }
            finally
            {
                Cursor.Current = anterior;
            }

            if (conSiia)
            {
                Log.Info("Recuperar contrasena: '" + (escrita.Length == 0 ? "(sin usuario)" : escrita) + "' entra con el SIIA: se le manda a la pagina del SIIA.");
                VentanaPaginaSiia.Recuperar(this, siia);
                return;
            }

            if (!api.Configurada)
            {
                MessageBox.Show((api.NoSeSupo
                    ? "Ahora no hay conexión con el servidor, así que no se puede recuperar la contraseña. "
                        + "Inténtalo más tarde o pide a la administración de DigBit que te ponga una contraseña nueva."
                    : "La recuperación de contraseña por correo todavía no está activada. "
                        + "Pide a la administración de DigBit que te ponga una contraseña nueva.")
                    + (siia.Activo ? Environment.NewLine + Environment.NewLine + InicioConSiia.TextoSiEresDocente(siia) : ""),
                    "Recuperar contraseña", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string matricula;
            if (!DialogoRecuperarContrasena.Mostrar(this, api, escrita, siia.Activo ? siia : null, out matricula))
            {
                return;
            }

            txtUsuario.Text = matricula;
            // La que tuviera escrita era la vieja.
            txtPassword.Clear();
            MessageBox.Show("Listo: ya puedes entrar con tu contraseña nueva.", "Recuperar contraseña",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            txtPassword.Focus();
        }
    }
}
