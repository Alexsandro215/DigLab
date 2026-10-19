using System;
using System.Drawing;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Vuelve a pedir la contrasena del administrador que inicio sesion antes de
    /// un cambio delicado (los codigos de las clases y anonimizar una cuenta): si dejo la
    /// sesion abierta y no esta, otra persona no puede hacerlo. Con tres
    /// equivocadas en la sesion (aunque cierre el dialogo y lo vuelva a abrir) la
    /// sesion se cierra sola. Queda en el log quien lo confirmo o fallo, nunca la
    /// contrasena ni los datos de la persona de la que se trata (accionParaElLog).
    /// </summary>
    internal class DialogoContrasenaAdmin : Form
    {
        private const int Ancho = 520;
        private const int Intentos = 3;

        private readonly string usuario;
        // Lo que se anota en el log: la pregunta, o el texto sin datos personales que se dio.
        private readonly string accionParaElLog;
        private readonly TextBox txtContrasena;
        private readonly Label lblError;

        /// <summary>True si escribio bien su contrasena.</summary>
        /// <param name="accion">La pregunta: "Cambiar como se entra a las clases?"</param>
        /// <param name="detalle">Que va a cambiar.</param>
        /// <param name="textoBoton">El verbo de la accion, como en DialogoConfirmacion.</param>
        /// <param name="accionParaElLog">
        /// Lo que se anota en el log en lugar de la pregunta, en ASCII, cuando la
        /// pregunta nombra a una persona: el log no se borra nunca. Sin el, la pregunta.
        /// </param>
        public static bool Confirmar(IWin32Window dueno, string accion, string detalle, string textoBoton, string accionParaElLog = null)
        {
            // Tras el tercer fallo ya no se pregunta ni se comprueba nada: la sesion
            // se esta cerrando, y una tecla que ya estaba en cola podria volver a
            // abrir el dialogo antes de que termine de cerrarse.
            if (Datos_User.ConfirmacionesFallidas >= Intentos || (AppContexto.Actual != null && AppContexto.Actual.CerrandoPorSeguridad))
            {
                return false;
            }

            // El encargado de laboratorio nunca confirma cambios: solo consulta.
            if (Permisos.SoloConsulta)
            {
                Log.Aviso("Solo consulta: se pidio confirmar '" + (accionParaElLog ?? accion ?? "") + "' a " + (Datos_User.getUser() ?? "-") + ".");
                MessageBox.Show(dueno, "Solo la administración de DigBit puede hacer este cambio.", "Solo consulta",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            string usuario = Datos_User.getUser();
            if (string.IsNullOrEmpty(usuario))
            {
                MessageBox.Show(dueno, "No se sabe quién inició sesión: vuelve a entrar para hacer este cambio.", "Confirma que eres tú",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            using (DialogoContrasenaAdmin dialogo = new DialogoContrasenaAdmin(usuario, accion, detalle, textoBoton, accionParaElLog ?? accion))
            {
                return dialogo.ShowDialog(dueno) == DialogResult.OK;
            }
        }

        private DialogoContrasenaAdmin(string usuario, string accion, string detalle, string textoBoton, string accionParaElLog)
        {
            this.usuario = usuario;
            this.accionParaElLog = accionParaElLog;
            DrawingFont fuenteDetalle = new DrawingFont("Century Gothic", 10F);
            int altoDetalle = Math.Min(160, TextRenderer.MeasureText(detalle ?? "", fuenteDetalle, new Size(Ancho - 48, int.MaxValue),
                TextFormatFlags.WordBreak).Height + 4);
            int y = 72 + altoDetalle + 14;
            // El error, en cuatro lineas: el de un fallo de conexion o de permisos dice
            // la causa y que hacer, y el mas largo (AvisoDeFalloAdmin) ocupa cuatro.
            EstiloHorarios.PrepararDialogo(this, "Confirma que eres tú", new Size(Ancho, y + 24 + 36 + 8 + 72 + 72));
            TopMost = Kiosco.Activo;

            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30),
                Location = new Point(24, 20),
                Size = new Size(Ancho - 48, 48),
                Text = accion
            });
            Controls.Add(new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = fuenteDetalle,
                ForeColor = Color.FromArgb(70, 70, 70),
                Location = new Point(24, 72),
                Size = new Size(Ancho - 48, altoDetalle),
                Text = detalle ?? ""
            });

            Label pedir = EstiloHorarios.CrearEtiqueta("Por seguridad, escribe la contraseña de " + usuario + ":", new Point(24, y));
            Controls.Add(pedir);
            txtContrasena = EstiloHorarios.CrearTexto(new Point(24, y + 24), 300);
            txtContrasena.UseSystemPasswordChar = true;
            txtContrasena.MaxLength = 200;
            Controls.Add(txtContrasena);
            lblError = EstiloAdmin.LineaError(new Point(24, y + 24 + 36 + 8), Ancho - 48, 72);
            int previos = Datos_User.ConfirmacionesFallidas;
            if (previos > 0)
            {
                // Volvio a abrirlo despues de fallar: los intentos no empiezan de nuevo.
                lblError.Text = (previos == 1 ? "Ya fallaste una vez. " : "Ya fallaste " + previos + " veces. ") + Quedan(previos);
            }

            Controls.Add(lblError);

            EstiloAdmin.BotonesDialogo(this, textoBoton, Comprobar);
            ActiveControl = txtContrasena;
        }

        private void Comprobar()
        {
            if (txtContrasena.Text.Length == 0)
            {
                lblError.Text = "Escribe tu contraseña.";
                txtContrasena.Focus();
                return;
            }

            bool correcta;
            Cursor = Cursors.WaitCursor;
            try
            {
                correcta = new Consultas().ConfirmarContrasenaAdministrador(usuario, txtContrasena.Text);
            }
            catch (Exception ex)
            {
                // No cuenta como intento fallido: no se llego a comparar.
                Log.Error("Confirmar la contrasena del administrador", ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo comprobar la contraseña", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (correcta)
            {
                Datos_User.ConfirmacionesFallidas = 0;
                Log.Info("El administrador " + usuario + " confirmo su contrasena para: " + accionParaElLog);
                DialogResult = DialogResult.OK;
                return;
            }

            int fallidos = ++Datos_User.ConfirmacionesFallidas;
            txtContrasena.Clear();
            Log.Aviso("Contrasena equivocada al confirmar como " + usuario + " (" + fallidos + " de " + Intentos + "): " + accionParaElLog);
            if (fallidos >= Intentos)
            {
                // Quien no sabe la contrasena no sigue en la sesion: se cierra sola
                // (con este dialogo y los de debajo) y el aviso sale en el login.
                DialogResult = DialogResult.Abort;
                if (AppContexto.Actual != null)
                {
                    AppContexto.Actual.CerrarSesionPorSeguridad("La contraseña de " + usuario + " no era correcta (" + Intentos
                        + " intentos): por seguridad se cerró la sesión y no se hizo el cambio.");
                }

                return;
            }

            lblError.Text = "Contraseña incorrecta. " + Quedan(fallidos);
            txtContrasena.Focus();
        }

        private static string Quedan(int fallidos)
        {
            int quedan = Intentos - fallidos;
            return quedan == 1 ? "Te queda 1 intento: si fallas, se cierra la sesión."
                : "Te quedan " + quedan + " intentos; si fallas todos, se cierra la sesión.";
        }
    }
}
