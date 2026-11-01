using System;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Lo que ve un docente al que el SIIA acepta pero la administracion todavia
    /// no ha enlazado (EnlacesSiia): su contrasena es correcta, falta que lo
    /// enlacen, y que numero decirle a la administracion, grande. Se revisa sola
    /// cada SegundosEntreRevisiones y con "Revisar de nuevo"; en cuanto lo
    /// enlazan, se cierra con OK y el docente entra sin volver a escribir la
    /// contrasena (aqui no esta: Consultas guarda solo su huella). A los
    /// MinutosMaximos deja de revisar: nadie entra solo en un equipo que se quedo
    /// esperando sin nadie enfrente. Tambien en el kiosco, encima de todo.
    /// </summary>
    internal sealed class VentanaEnlacePendiente : Form
    {
        private const int Ancho = 600;
        private const int Alto = 376;
        private const int SegundosEntreRevisiones = 20;
        private const int MinutosMaximos = 15;
        private static readonly Color Gris = Color.FromArgb(70, 70, 70);

        private readonly string numero;
        private readonly Label lblEstado;
        private readonly Button btnRevisar;
        private readonly Timer reloj;
        private readonly DateTime desde = DateTime.UtcNow;
        private bool revisando;
        private bool vencida;
        private bool falloAvisado;

        /// <summary>True si la administracion lo enlazo mientras esperaba; false si salio (o se acabo el tiempo).</summary>
        public static bool Esperar(string numero)
        {
            using (VentanaEnlacePendiente ventana = new VentanaEnlacePendiente(numero))
            {
                return ventana.ShowDialog() == DialogResult.OK;
            }
        }

        private VentanaEnlacePendiente(string numero)
        {
            this.numero = numero;
            EstiloHorarios.PrepararDialogo(this, "Falta enlazar tu cuenta", new Size(Ancho, Alto));
            // Sin dueno visible en el kiosco: encima de la pantalla del inicio de sesion.
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Falta enlazar tu cuenta", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10.5F),
                ForeColor = Color.FromArgb(40, 40, 40),
                Location = new Point(24, 58),
                Size = new Size(Ancho - 48, 48),
                Text = "Tu contraseña del SIIA es correcta. Falta que la administración enlace tu cuenta con tus clases. Acércate y dile:"
            });
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold | FontStyle.Italic),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 112),
                Size = new Size(Ancho - 48, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "«Soy el número de empleado " + numero + ", enlázame»."
            });

            // El numero, grande y claro, para decirlo (o ensenar la pantalla).
            Panel recuadro = new Panel
            {
                BackColor = Color.FromArgb(232, 245, 233),
                Location = new Point(24, 148),
                Size = new Size(Ancho - 48, 84)
            };
            recuadro.Controls.Add(new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new DrawingFont("Century Gothic", 34F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = numero,
                AccessibleName = "Tu número de empleado: " + numero
            });
            Controls.Add(recuadro);

            lblEstado = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 242),
                Size = new Size(Ancho - 48, 56),
                Text = "En cuanto te enlacen entras, sin volver a escribir tu contraseña: se revisa sola cada " + SegundosEntreRevisiones + " segundos."
            };
            Controls.Add(lblEstado);

            btnRevisar = EstiloAdmin.BotonesDialogo(this, "Revisar de nuevo", () => Revisar(true));
            ((Button)CancelButton).Text = "Salir";
            ActiveControl = btnRevisar;

            reloj = new Timer { Interval = SegundosEntreRevisiones * 1000 };
            reloj.Tick += (s, e) => Revisar(false);
            Shown += (s, e) => reloj.Start();
            FormClosed += (s, e) => reloj.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reloj.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Si ya lo enlazaron, en segundo plano (la ventana no se congela sin red).</summary>
        private void Revisar(bool aMano)
        {
            if (revisando || vencida || IsDisposed)
            {
                return;
            }

            if (DateTime.UtcNow - desde > TimeSpan.FromMinutes(MinutosMaximos))
            {
                Vencer();
                return;
            }

            revisando = true;
            btnRevisar.Enabled = false;
            if (aMano)
            {
                lblEstado.ForeColor = Gris;
                lblEstado.Text = "Revisando...";
            }

            Task.Run(() => EnlacesSiia.EstaEnlazado(numero)).ContinueWith(tarea =>
            {
                if (IsDisposed)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() => Revisado(tarea)));
                }
                catch (InvalidOperationException)
                {
                    // Se cerro mientras se revisaba.
                }
            });
        }

        private void Revisado(Task<bool> tarea)
        {
            revisando = false;
            if (IsDisposed || vencida)
            {
                return;
            }

            btnRevisar.Enabled = true;
            string hora = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (tarea.IsFaulted)
            {
                // Una vez en el log, no cada 20 segundos.
                if (!falloAvisado)
                {
                    Log.Error("SIIA: revisar si ya enlazaron a '" + numero + "'", tarea.Exception.GetBaseException());
                    falloAvisado = true;
                }

                lblEstado.ForeColor = Color.FromArgb(190, 85, 0);
                lblEstado.Text = "No se pudo revisar a las " + hora + ": no hay conexión con el servidor. DigBit lo vuelve a intentar cada "
                    + SegundosEntreRevisiones + " segundos.";
                return;
            }

            if (tarea.Result)
            {
                reloj.Stop();
                DialogResult = DialogResult.OK;
                return;
            }

            lblEstado.ForeColor = Gris;
            lblEstado.Text = "Todavía no te enlazan (revisado a las " + hora + "). En cuanto lo hagan entras, sin volver a escribir tu contraseña: "
                + "se revisa sola cada " + SegundosEntreRevisiones + " segundos.";
        }

        /// <summary>Despues de MinutosMaximos ya no se entra sola: hay que volver a escribir la contrasena.</summary>
        private void Vencer()
        {
            vencida = true;
            reloj.Stop();
            btnRevisar.Enabled = false;
            lblEstado.ForeColor = Color.FromArgb(190, 85, 0);
            lblEstado.Text = "Pasaron " + MinutosMaximos + " minutos: por seguridad ya no se entra desde aquí. Cuando te enlacen, vuelve a entrar con tu "
                + "número de empleado y tu contraseña del SIIA.";
            ActiveControl = (Control)CancelButton;
        }
    }
}
