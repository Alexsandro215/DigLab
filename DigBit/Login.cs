using System;
using System.Drawing;
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

            // "USUARIO" y "CONTRASENA" como texto guia (gris, tambien con el cursor
            // dentro): antes eran el texto de la caja y se borraban al entrar.
            EstiloHorarios.PonerTextoGuia(txtUsuario, "USUARIO");
            EstiloHorarios.PonerTextoGuia(txtPassword, "CONTRASEÑA");
            // Se escribe en cuanto aparece la ventana; Enter es "Iniciar sesion"
            // (AcceptButton, en el disenador). Con el foco en el enlace, Enter lo
            // abre: un LinkLabel es IButtonControl y entonces es el boton por defecto.
            ActiveControl = txtUsuario;
            // El enlace va sobre las franjas verdes: con fondo blanco, como los
            // botones, y centrado bajo "Iniciar sesion".
            linkPasswd.Left = btnIniciarSesion.Left + (btnIniciarSesion.Width - linkPasswd.Width) / 2;

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
                BackColor = Color.FromArgb(63, 63, 70),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                // Sin TabStop: que no se apague el equipo por darle a Tab y Enter
                // mientras se teclea la contrasena.
                TabStop = false
            };
            btnApagar.FlatAppearance.BorderSize = 0;
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
                BackColor = Color.FromArgb(94, 94, 102),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnEmergencia.FlatAppearance.BorderSize = 0;
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
            int franja = (lblSinConexion != null && lblSinConexion.Visible) ? lblSinConexion.Height : 0;
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
            int franja = (lblSinConexion != null && lblSinConexion.Visible) ? lblSinConexion.Height : 0;
            btnApagar.Location = new Point(
                ClientSize.Width - btnApagar.Width - margen,
                ClientSize.Height - btnApagar.Height - margen - franja);
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
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (temporizadorAviso != null)
            {
                temporizadorAviso.Stop();
                temporizadorAviso.Dispose();
                temporizadorAviso = null;
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

        private void btnIniciarSesion_Click(object sender, EventArgs e)
        {
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
                entro = Entrar(numeroIdentificadorIngresado, contraseñaIngresada);
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
                MostrarEntrando(false);
            }

            if (entro)
            {
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
        /// Prueba el servidor (o sigue con la copia local) y abre la ventana de la
        /// cuenta. True si la abrio; los avisos de por que no, los ensena por el camino.
        /// </summary>
        private bool Entrar(string numeroIdentificadorIngresado, string contraseñaIngresada)
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
                        MessageBox.Show("No hay conexión con el servidor y este equipo no tiene una copia del horario. Avisa a la persona responsable del laboratorio.", "Sin conexión", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    MessageBox.Show("Sin conexión solo pueden entrar estudiantes, y esa matrícula no está en la copia local de este equipo.", "Sin conexión", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            // base desde la fase 6; antes eran credenciales escritas aqui.
            return consultas.RealizarInicioSesion(numeroIdentificadorIngresado, contraseñaIngresada);
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

            btnIniciarSesion.Enabled = !entrando;
            btnIniciarSesion.Text = entrando ? "ENTRANDO..." : textoIniciarSesion;
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
        private void btnRegistro_Click(object sender, EventArgs e)
        {
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
                MessageBox.Show("Para crear tu cuenta hace falta conexión con el servidor, y ahora no la hay. "
                    + "Inténtalo más tarde o avisa a la persona responsable del laboratorio.", "Sin conexión", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                entro = consultas.RealizarInicioSesion(matricula, contrasena);
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
            ApiRecuperacion api;
            Cursor anterior = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                api = RecuperacionContrasena.Buscar();
            }
            finally
            {
                Cursor.Current = anterior;
            }

            if (!api.Configurada)
            {
                MessageBox.Show(api.NoSeSupo
                    ? "Ahora no hay conexión con el servidor, así que no se puede recuperar la contraseña. "
                        + "Inténtalo más tarde o pide a la administración de DigBit que te ponga una contraseña nueva."
                    : "La recuperación de contraseña por correo todavía no está activada. "
                        + "Pide a la administración de DigBit que te ponga una contraseña nueva.",
                    "Recuperar contraseña", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string escrita = txtUsuario.Text.Trim();
            string matricula;
            if (!DialogoRecuperarContrasena.Mostrar(this, api, escrita, out matricula))
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
