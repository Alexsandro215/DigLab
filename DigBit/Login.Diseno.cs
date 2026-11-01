using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;

namespace DigBit
{
    /// <summary>
    /// El aspecto del inicio de sesion: una tarjeta blanca centrada sobre un fondo
    /// verde institucional que se dibuja aqui (sin imagenes de fondo), la misma para
    /// estudiantes, docentes, administracion y responsables de laboratorio. En una
    /// ventana mide 480 x 640 y se arrastra desde el fondo; en el kiosco ocupa la
    /// pantalla y arriba a la izquierda dice el laboratorio, el equipo y la hora.
    /// Las cajas son TextBox sin borde dentro de un marco redondeado que pinta la
    /// tarjeta (verde con el foco). Todo se coloca en Colocar(), con medidas
    /// propias (AutoScaleMode.None), cada vez que algo cambia de tamano o se
    /// ensena u oculta.
    /// </summary>
    public partial class Login
    {
        private static readonly Color VerdeTeschi = Color.FromArgb(0, 112, 64);
        private static readonly Color VerdeOscuro = Color.FromArgb(0, 74, 42);
        private static readonly Color VerdeClaro = Color.FromArgb(120, 184, 56);
        private static readonly Color GrisTexto = Color.FromArgb(95, 99, 96);
        private static readonly Color GrisBorde = Color.FromArgb(200, 206, 202);
        private static readonly Color FondoSoloLectura = Color.FromArgb(242, 245, 243);
        private static readonly Color RojoAviso = Color.FromArgb(190, 30, 30);

        // La tarjeta: el panel (blanco) y, alrededor, el borde redondeado que pinta el formulario.
        private const int AnchoTarjeta = 376;
        private const int Margen = 24;
        private const int Orilla = 14;
        private const int RadioTarjeta = 20;
        private const int AltoCaja = 44;

        // Segoe MDL2 Assets (Windows 10 y 11); sin ella, sin iconos y con letras.
        private const string FuenteIconos = "Segoe MDL2 Assets";
        private const string IconoUsuario = "";
        private const string IconoCandado = "";
        private const string IconoVer = "";
        private const string IconoOcultar = "";
        private const string IconoMinimizar = "";
        private const string IconoCerrar = "";

        private bool conIconos;
        private Label lblTitulo;
        private Label lblSubtitulo;
        private Label lblEtiquetaUsuario;
        private Label lblEtiquetaPassword;
        private Label lblMensaje;
        private Label lblIconoUsuario;
        private Label lblIconoPassword;
        private Label lblVerPassword;
        private Rectangle cajaUsuario;
        private Rectangle cajaPassword;
        // El aviso de privacidad, en el paso del NIP (null: no hay aviso en Ajustes).
        private LinkLabel lnkAvisoNip;
        private bool avisoNipLeido;
        // Solo en el kiosco: el laboratorio, el equipo y la hora.
        private Label lblEquipo;
        private Label lblHora;
        private Label lblFecha;
        private Timer relojKiosco;
        // Lo ultimo leido de Ajustes: los docentes entran con el SIIA (el subtitulo).
        private bool docentesConSiia;

        /// <summary>Lo que el diseñador no tiene: textos, iconos, el reloj del kiosco y el arrastre.</summary>
        private void ConstruirDiseno()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            conIconos = FontFamily.Families.Any(f => f.Name == FuenteIconos);

            lblTitulo = TextoTarjeta(new Font("Century Gothic", 17F, FontStyle.Bold), VerdeOscuro, ContentAlignment.MiddleCenter);
            lblTitulo.Text = "Iniciar sesión";
            lblSubtitulo = TextoTarjeta(new Font("Century Gothic", 9.5F), GrisTexto, ContentAlignment.TopCenter);
            lblEtiquetaUsuario = TextoTarjeta(new Font("Century Gothic", 9F, FontStyle.Bold), Color.FromArgb(55, 60, 57), ContentAlignment.BottomLeft);
            lblEtiquetaUsuario.Text = "Usuario";
            lblEtiquetaPassword = TextoTarjeta(new Font("Century Gothic", 9F, FontStyle.Bold), Color.FromArgb(55, 60, 57), ContentAlignment.BottomLeft);
            lblEtiquetaPassword.Text = "Contraseña";
            lblMensaje = TextoTarjeta(new Font("Century Gothic", 9F, FontStyle.Bold), RojoAviso, ContentAlignment.MiddleLeft);

            lblIconoUsuario = Icono(IconoUsuario);
            lblIconoPassword = Icono(IconoCandado);
            lblVerPassword = Icono(conIconos ? IconoVer : "Ver");
            lblVerPassword.Cursor = Cursors.Hand;
            lblVerPassword.AccessibleName = "Mostrar la contraseña";
            lblVerPassword.AccessibleRole = AccessibleRole.PushButton;
            lblVerPassword.Click += (s, e) => AlternarVerPassword();
            if (!conIconos)
            {
                lblVerPassword.Font = new Font("Century Gothic", 8.5F, FontStyle.Bold);
                lblIconoUsuario.Visible = false;
                lblIconoPassword.Visible = false;
            }

            imgMinimizar.Font = conIconos ? new Font(FuenteIconos, 10F) : new Font("Century Gothic", 12F, FontStyle.Bold);
            imgMinimizar.Text = conIconos ? IconoMinimizar : "–";
            imgSalir.Font = conIconos ? new Font(FuenteIconos, 10F) : new Font("Century Gothic", 12F, FontStyle.Bold);
            imgSalir.Text = conIconos ? IconoCerrar : "×";
            foreach (Label boton in new[] { imgMinimizar, imgSalir })
            {
                Label este = boton;
                este.MouseEnter += (s, e) => este.BackColor = este == imgSalir ? Color.FromArgb(200, 40, 40) : Color.FromArgb(50, 255, 255, 255);
                este.MouseLeave += (s, e) => este.BackColor = Color.Transparent;
            }

            // El marco de cada caja: verde con el foco.
            panelTarjeta.Paint += PintarCajas;
            foreach (TextBox caja in new[] { txtUsuario, txtPassword })
            {
                caja.Enter += (s, e) => panelTarjeta.Invalidate();
                caja.Leave += (s, e) => panelTarjeta.Invalidate();
            }

            // Un clic en el marco (fuera del texto) tambien lleva a la caja.
            panelTarjeta.MouseDown += (s, e) =>
            {
                if (cajaUsuario.Contains(e.Location))
                {
                    txtUsuario.Focus();
                }
                else if (cajaPassword.Contains(e.Location))
                {
                    txtPassword.Focus();
                }
                else
                {
                    Arrastrar(e);
                }
            };
            lblIconoUsuario.Click += (s, e) => txtUsuario.Focus();
            lblIconoPassword.Click += (s, e) => txtPassword.Focus();
            MouseDown += (s, e) => Arrastrar(e);

            if (Kiosco.Activo)
            {
                ConstruirInfoKiosco();
            }

            ActualizarSubtitulo();
            Resize += (s, e) => Colocar();
            Shown += (s, e) => Colocar();
        }

        private Label TextoTarjeta(Font letra, Color color, ContentAlignment alineacion)
        {
            Label texto = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.White,
                Font = letra,
                ForeColor = color,
                TextAlign = alineacion,
                UseMnemonic = false
            };
            panelTarjeta.Controls.Add(texto);
            return texto;
        }

        private Label Icono(string glifo)
        {
            Label icono = new Label
            {
                AutoSize = false,
                BackColor = Color.White,
                Font = new Font(FuenteIconos, 12F),
                ForeColor = Color.FromArgb(120, 128, 123),
                Size = new Size(28, 28),
                Text = glifo,
                TextAlign = ContentAlignment.MiddleCenter
            };
            panelTarjeta.Controls.Add(icono);
            icono.BringToFront();
            return icono;
        }

        /// <summary>El ojo de la caja de la contrasena (o del NIP): ensenarla u ocultarla.</summary>
        private void AlternarVerPassword()
        {
            bool ver = txtPassword.UseSystemPasswordChar;
            int cursor = txtPassword.SelectionStart;
            txtPassword.UseSystemPasswordChar = !ver;
            lblVerPassword.Text = conIconos ? (ver ? IconoOcultar : IconoVer) : (ver ? "Ocultar" : "Ver");
            lblVerPassword.AccessibleName = ver ? "Ocultar la contraseña" : "Mostrar la contraseña";
            txtPassword.Focus();
            txtPassword.SelectionStart = Math.Min(cursor, txtPassword.TextLength);
        }

        /// <summary>Siempre oculta al volver a empezar (y en el paso del NIP).</summary>
        private void OcultarPassword()
        {
            if (!txtPassword.UseSystemPasswordChar)
            {
                AlternarVerPassword();
            }
        }

        /// <summary>
        /// Lo que se escribe, segun quien entra con el SIIA (Ajustes): el kiosco es
        /// de los estudiantes; los equipos de la administracion y de los docentes
        /// sirven a todos.
        /// </summary>
        private void ActualizarSubtitulo()
        {
            if (esperaNip != null || lblSubtitulo == null)
            {
                return;
            }

            lblTitulo.Text = "Iniciar sesión";
            lblSubtitulo.ForeColor = GrisTexto;
            if (Kiosco.Activo)
            {
                lblSubtitulo.Text = estudiantesConSiia ? "Entra con tu matrícula y tu contraseña del SIIA." : "Entra con tu matrícula y tu contraseña.";
            }
            else if (estudiantesConSiia || docentesConSiia)
            {
                lblSubtitulo.Text = (docentesConSiia && estudiantesConSiia ? "Docentes y estudiantes: con su cuenta del SIIA."
                    : docentesConSiia ? "Docentes: número de empleado y contraseña del SIIA." : "Estudiantes: matrícula y contraseña del SIIA.")
                    + Environment.NewLine + "Administración: con su cuenta de DigBit.";
            }
            else
            {
                lblSubtitulo.Text = "Escribe tu usuario y tu contraseña de DigBit.";
            }

            EstiloHorarios.PonerTextoGuia(txtUsuario, "Matrícula o número de empleado");
        }

        /// <summary>Un aviso en rojo bajo la contrasena (o el NIP); null o vacio lo quita.</summary>
        private void MostrarMensaje(string texto)
        {
            lblMensaje.Text = texto ?? "";
            Colocar();
        }

        /// <summary>El aviso de privacidad bajo "Volver", en el paso del NIP (una vez leido de la base).</summary>
        private void PrepararAvisoNip()
        {
            if (avisoNipLeido)
            {
                return;
            }

            avisoNipLeido = true;
            AvisoDePrivacidad privacidad = AvisoDePrivacidad.Leer();
            if (!privacidad.HayTexto)
            {
                return;
            }

            lnkAvisoNip = privacidad.CrearEnlace(this);
            lnkAvisoNip.Font = new Font("Century Gothic", 8.5F);
            lnkAvisoNip.BackColor = Color.White;
            lnkAvisoNip.LinkBehavior = LinkBehavior.HoverUnderline;
            lnkAvisoNip.Visible = false;
            panelTarjeta.Controls.Add(lnkAvisoNip);
        }

        /// <summary>
        /// Cada cosa en su sitio: la tarjeta crece con lo que ensena ("Crear cuenta"
        /// o "Volver", el aviso de privacidad, un mensaje) y va al centro de lo que
        /// deja libre la franja de sin conexion.
        /// </summary>
        private void Colocar()
        {
            if (lblTitulo == null || IsDisposed)
            {
                return;
            }

            int ancho = AnchoTarjeta - 2 * Margen;
            imgLogo.SetBounds((AnchoTarjeta - 180) / 2, 6, 180, 72);
            lblTitulo.SetBounds(Margen, 86, ancho, 34);
            lblSubtitulo.SetBounds(Margen, 122, ancho, 40);

            int y = 172;
            lblEtiquetaUsuario.SetBounds(Margen, y, ancho, 20);
            cajaUsuario = new Rectangle(Margen, y + 22, ancho, AltoCaja);
            ColocarCaja(txtUsuario, cajaUsuario, lblIconoUsuario, null);
            y = cajaUsuario.Bottom + 10;

            lblEtiquetaPassword.SetBounds(Margen, y, ancho, 20);
            cajaPassword = new Rectangle(Margen, y + 22, ancho, AltoCaja);
            ColocarCaja(txtPassword, cajaPassword, lblIconoPassword, lblVerPassword);
            y = cajaPassword.Bottom + 4;

            bool conMensaje = lblMensaje.Text.Length > 0;
            lblMensaje.Visible = conMensaje;
            lblMensaje.SetBounds(Margen, y, ancho, 22);
            y += conMensaje ? 28 : 14;

            btnIniciarSesion.SetBounds(Margen, y, ancho, 46);
            y = btnIniciarSesion.Bottom + 12;
            linkPasswd.Location = new Point((AnchoTarjeta - linkPasswd.PreferredWidth) / 2, y);
            y = linkPasswd.Top + linkPasswd.PreferredHeight + 14;

            if (btnRegistro.Visible)
            {
                btnRegistro.SetBounds(Margen, y, ancho, 42);
                y = btnRegistro.Bottom + 12;
            }

            if (lnkAvisoNip != null && lnkAvisoNip.Visible)
            {
                lnkAvisoNip.Location = new Point((AnchoTarjeta - lnkAvisoNip.PreferredWidth) / 2, y);
                y = lnkAvisoNip.Top + lnkAvisoNip.PreferredHeight + 10;
            }

            int alto = y + 8;
            int libre = ClientSize.Height - AltoFranja();
            panelTarjeta.SetBounds((ClientSize.Width - AnchoTarjeta) / 2, Math.Max(Orilla + 8, (libre - alto) / 2), AnchoTarjeta, alto);

            imgSalir.Location = new Point(ClientSize.Width - imgSalir.Width - 8, 8);
            imgMinimizar.Location = new Point(imgSalir.Left - imgMinimizar.Width - 2, 8);
            ColocarInfoKiosco();
            Invalidate();
            panelTarjeta.Invalidate();
        }

        /// <summary>El TextBox (sin borde) centrado de alto dentro de su marco, con su icono y, si lo hay, el ojo.</summary>
        private static void ColocarCaja(TextBox caja, Rectangle marco, Label icono, Label derecha)
        {
            int izquierda = marco.Left + (icono.Visible ? 44 : 14);
            int final = marco.Right - (derecha != null ? 44 : 14);
            caja.SetBounds(izquierda, marco.Top + (marco.Height - caja.PreferredHeight) / 2 + 1, final - izquierda, caja.PreferredHeight);
            icono.Location = new Point(marco.Left + 10, marco.Top + (marco.Height - icono.Height) / 2);
            if (derecha != null)
            {
                derecha.Size = new Size(36, 28);
                derecha.Location = new Point(marco.Right - 40, marco.Top + (marco.Height - derecha.Height) / 2);
            }
        }

        private void PintarCajas(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            PintarCaja(e.Graphics, cajaUsuario, txtUsuario);
            PintarCaja(e.Graphics, cajaPassword, txtPassword);
        }

        private static void PintarCaja(Graphics g, Rectangle marco, TextBox caja)
        {
            if (marco.Width <= 0)
            {
                return;
            }

            bool foco = caja.Focused;
            Rectangle borde = new Rectangle(marco.X, marco.Y, marco.Width - 1, marco.Height - 1);
            using (GraphicsPath camino = Redondeado(borde, 10))
            {
                using (SolidBrush relleno = new SolidBrush(caja.BackColor))
                {
                    g.FillPath(relleno, camino);
                }

                using (Pen pluma = new Pen(foco ? VerdeTeschi : GrisBorde, foco ? 2F : 1F))
                {
                    g.DrawPath(pluma, camino);
                }
            }
        }

        /// <summary>El fondo verde con circulos tenues, la sombra y el borde redondeado de la tarjeta.</summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle todo = ClientRectangle;
            if (todo.Width <= 0 || todo.Height <= 0)
            {
                return;
            }

            using (LinearGradientBrush fondo = new LinearGradientBrush(todo, VerdeOscuro, VerdeTeschi, LinearGradientMode.ForwardDiagonal))
            {
                g.FillRectangle(fondo, todo);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            int grande = Math.Max(todo.Width, todo.Height);
            using (Pen anillo = new Pen(Color.FromArgb(28, 255, 255, 255), Math.Max(24, grande / 28)))
            using (SolidBrush mancha = new SolidBrush(Color.FromArgb(22, VerdeClaro)))
            {
                g.FillEllipse(mancha, todo.Right - grande * 45 / 100, -grande * 20 / 100, grande * 70 / 100, grande * 70 / 100);
                g.DrawEllipse(anillo, -grande * 25 / 100, todo.Bottom - grande * 35 / 100, grande * 60 / 100, grande * 60 / 100);
            }

            if (panelTarjeta.Width <= 0)
            {
                return;
            }

            Rectangle tarjeta = Rectangle.Inflate(panelTarjeta.Bounds, Orilla, Orilla);
            for (int i = 1; i <= 8; i++)
            {
                Rectangle sombra = new Rectangle(tarjeta.X - i + 2, tarjeta.Y - i + 6, tarjeta.Width + 2 * i - 4, tarjeta.Height + 2 * i - 4);
                using (GraphicsPath camino = Redondeado(sombra, RadioTarjeta + i))
                using (SolidBrush pincel = new SolidBrush(Color.FromArgb(9, 0, 0, 0)))
                {
                    g.FillPath(pincel, camino);
                }
            }

            using (GraphicsPath camino = Redondeado(tarjeta, RadioTarjeta))
            {
                g.FillPath(Brushes.White, camino);
            }
        }

        private static GraphicsPath Redondeado(Rectangle r, int radio)
        {
            int d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
            GraphicsPath camino = new GraphicsPath();
            camino.AddArc(r.X, r.Y, d, d, 180, 90);
            camino.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            camino.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            camino.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            camino.CloseFigure();
            return camino;
        }

        // --- Kiosco: laboratorio, equipo y hora ------------------------------------

        private void ConstruirInfoKiosco()
        {
            lblEquipo = TextoFondo(new Font("Century Gothic", 12F, FontStyle.Bold));
            lblHora = TextoFondo(new Font("Century Gothic", 40F, FontStyle.Bold));
            lblFecha = TextoFondo(new Font("Century Gothic", 13F));
            string laboratorio = LaboratorioEquipo.Nombre;
            if (string.IsNullOrWhiteSpace(laboratorio))
            {
                laboratorio = LaboratorioEquipo.NombreConfigurado;
            }

            string equipo = LaboratorioEquipo.NumeroMaquinaConfigurado ? "Equipo " + LaboratorioEquipo.NumeroMaquina : "";
            lblEquipo.Text = string.Join(" · ", new[] { string.IsNullOrWhiteSpace(laboratorio) ? "" : EstiloHorarios.NombreLaboratorio(laboratorio), equipo }
                .Where(t => t.Length > 0));
            ActualizarHora();
            relojKiosco = new Timer { Interval = 15000 };
            relojKiosco.Tick += (s, e) => ActualizarHora();
            relojKiosco.Start();
            FormClosed += (s, e) => relojKiosco.Dispose();
        }

        private Label TextoFondo(Font letra)
        {
            Label texto = new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = letra,
                ForeColor = Color.White,
                UseMnemonic = false
            };
            Controls.Add(texto);
            return texto;
        }

        private void ActualizarHora()
        {
            CultureInfo mexico = CultureInfo.GetCultureInfo("es-MX");
            DateTime ahora = DateTime.Now;
            lblHora.Text = ahora.ToString("HH:mm", mexico);
            lblFecha.Text = ahora.ToString("dddd d 'de' MMMM", mexico);
            ColocarInfoKiosco();
        }

        private void ColocarInfoKiosco()
        {
            if (lblHora == null)
            {
                return;
            }

            lblEquipo.Location = new Point(40, 36);
            lblHora.Location = new Point(34, lblEquipo.Text.Length > 0 ? 62 : 36);
            lblFecha.Location = new Point(42, lblHora.Bottom + 2);
        }

        // --- Ventana sin borde: se arrastra desde el fondo -------------------------

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr ventana, int mensaje, IntPtr w, IntPtr l);

        private void Arrastrar(MouseEventArgs e)
        {
            const int WM_NCLBUTTONDOWN = 0xA1;
            const int HT_CAPTION = 0x2;
            if (Kiosco.Activo || e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HT_CAPTION), IntPtr.Zero);
        }
    }
}
