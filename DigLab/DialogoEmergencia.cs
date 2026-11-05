using System;
using System.Drawing;
using System.Windows.Forms;
using DigLab.Infraestructura;

namespace DigLab
{
    /// <summary>
    /// Pide la memoria de emergencia y nada mas. Se abre sola en cuanto
    /// reconoce la autorizada; mientras tanto dice lo que ve, para que el
    /// encargado sepa si es que no la detecta o es que no es la buena.
    ///
    /// El numero de serie nunca se escribe en el log.
    /// </summary>
    internal sealed class DialogoEmergencia : Form
    {
        // El naranja de los avisos de los dialogos (DialogoUsuario): la memoria
        // conectada no es la autorizada.
        private static readonly Color Aviso = Color.FromArgb(190, 85, 0);

        private readonly Label lblTitulo = new Label();
        private readonly Label lblTexto = new Label();
        private readonly Button btnCancelar;
        private readonly Timer buscarMemoria = new Timer();

        private DialogoEmergencia()
        {
            Text = "Acceso de emergencia";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 220);
            BackColor = Color.White;
            TopMost = true;

            // El estilo de los dialogos de DigLab: titulo en verde y el boton de EstiloHorarios.
            lblTitulo.SetBounds(24, 24, 412, 32);
            lblTitulo.Font = new Font("Century Gothic", 13F, FontStyle.Bold);
            lblTitulo.ForeColor = Color.DarkGreen;
            lblTitulo.Text = "Inserta la memoria";

            lblTexto.SetBounds(24, 64, 412, 80);
            lblTexto.Font = new Font("Century Gothic", 9.5F);
            lblTexto.Text = "Conecta la memoria de emergencia del laboratorio." + Environment.NewLine + Environment.NewLine +
                            "El equipo se abrirá solo en cuanto la reconozca, sin pasar por la bitácora.";

            btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(ClientSize.Width - 24 - 120, ClientSize.Height - 56), new Size(120, 40));
            btnCancelar.DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { lblTitulo, lblTexto, btnCancelar });
            CancelButton = btnCancelar;

            buscarMemoria.Interval = 1000;
            buscarMemoria.Tick += BuscarMemoria;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            buscarMemoria.Start();
            BuscarMemoria(null, EventArgs.Empty);
        }

        private void BuscarMemoria(object remitente, EventArgs e)
        {
            if (Emergencia.MemoriaPresente())
            {
                buscarMemoria.Stop();
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            // Decir QUE se ve ahorra el "no funciona" sin mas datos: no es lo
            // mismo que no detecte ninguna memoria a que la conectada no sea.
            int cuantas = Emergencia.MemoriasConectadas();
            lblTitulo.Text = cuantas == 0
                ? "Inserta la memoria"
                : (cuantas == 1 ? "Esa no es la memoria autorizada" : "Ninguna de esas es la memoria autorizada");
            lblTitulo.ForeColor = cuantas == 0 ? Color.DarkGreen : Aviso;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            buscarMemoria.Stop();
            buscarMemoria.Dispose();
            base.OnFormClosed(e);
        }

        /// <summary>Devuelve true si se reconocio la memoria autorizada.</summary>
        public static bool Pedir(IWin32Window dueno)
        {
            if (!Emergencia.Configurada)
            {
                MessageBox.Show(
                    "Este equipo no tiene memoria de emergencia autorizada." + Environment.NewLine + Environment.NewLine +
                    "La autoriza la persona responsable del laboratorio al instalar DigLab en el equipo con DigLab-Setup.exe, "
                    + "con la memoria conectada.",
                    "Acceso de emergencia", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            using (DialogoEmergencia dialogo = new DialogoEmergencia())
            {
                bool concedido = dialogo.ShowDialog(dueno) == DialogResult.OK;
                if (!concedido)
                {
                    Log.Info("Acceso de emergencia cancelado.");
                }

                return concedido;
            }
        }
    }
}
