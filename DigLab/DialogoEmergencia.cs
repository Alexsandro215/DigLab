using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigLab.Infraestructura;

namespace DigLab
{
    /// <summary>
    /// Pide una memoria de emergencia y nada mas. Se abre sola en cuanto
    /// reconoce una autorizada; mientras tanto dice lo que ve, para que el
    /// encargado sepa si es que no la detecta o es que no es una buena.
    ///
    /// Al abrirse pregunta a la base que memorias valen (Emergencia.Autorizadas):
    /// sin servidor, la copia de este equipo. Comparar una memoria con la lista es
    /// lento (un PBKDF2 por cada registrada), asi que va fuera del hilo de la
    /// pantalla y solo cuando cambia lo que esta conectado.
    ///
    /// El numero de serie nunca se escribe en el log.
    /// </summary>
    internal sealed class DialogoEmergencia : Form
    {
        // El naranja de los avisos de los dialogos (DialogoUsuario): la memoria
        // conectada no es una autorizada.
        private static readonly Color Aviso = Color.FromArgb(190, 85, 0);

        private const string TextoPedir = "Conecta una de las memorias de emergencia del laboratorio.\n\n"
            + "El equipo se abrirá solo en cuanto la reconozca, sin pasar por la bitácora.";

        private readonly Label lblTitulo = new Label();
        private readonly Label lblTexto = new Label();
        private readonly Button btnCancelar;
        private readonly Timer buscarMemoria = new Timer();

        // Las que valen (la base o, sin servidor, la copia), en cuanto se sepa.
        private Task<MemoriasAutorizadas> autorizadas;
        // La comparacion de las conectadas con la lista, mientras dura.
        private Task<int?> comparando;
        // Las series de las conectadas que ya se compararon (sin escribirlas en ningun lado).
        private string yaComparadas;

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
            lblTitulo.Text = "Un momento...";

            lblTexto.SetBounds(24, 64, 412, 88);
            lblTexto.Font = new Font("Century Gothic", 9.5F);
            lblTexto.Text = "Comprobando qué memorias de emergencia abren este equipo.";

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
            // La base tarda hasta su tiempo de conexion en rendirse: mientras, la ventana responde.
            autorizadas = Task.Run(() => Emergencia.Autorizadas(true));
            buscarMemoria.Interval = 300;
            buscarMemoria.Start();
        }

        private void BuscarMemoria(object remitente, EventArgs e)
        {
            if (autorizadas == null || !autorizadas.IsCompleted)
            {
                return;
            }

            buscarMemoria.Interval = 1000;
            MemoriasAutorizadas lista = autorizadas.Status == TaskStatus.RanToCompletion ? autorizadas.Result : null;
            if (lista == null || lista.Huellas.Count == 0)
            {
                buscarMemoria.Stop();
                Mostrar("Sin memorias autorizadas",
                    "Las memorias de emergencia se registran en DigLab, en Laboratorios > «Memorias de emergencia...», "
                    + "con la memoria conectada a la computadora de la administración.\n\n"
                    + "Un equipo sin conexión las recibe al volver a tenerla.", Aviso);
                Log.Aviso("Acceso de emergencia pedido, pero este equipo no tiene ninguna memoria autorizada.");
                return;
            }

            if (comparando != null)
            {
                if (!comparando.IsCompleted)
                {
                    return;
                }

                int? reconocida = comparando.Status == TaskStatus.RanToCompletion ? comparando.Result : null;
                comparando = null;
                if (reconocida.HasValue)
                {
                    buscarMemoria.Stop();
                    Log.Aviso("Memoria de emergencia reconocida: " + (reconocida.Value == 0
                        ? "la que se autorizo al instalar este equipo"
                        : "la n.o " + reconocida.Value) + ", segun " + lista.Origen + ".");
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
            }

            // Decir QUE se ve ahorra el "no funciona" sin mas datos: no es lo
            // mismo que no detecte ninguna memoria a que la conectada no sea.
            List<MemoriaUsb> conectadas = Emergencia.Conectadas();
            if (conectadas.Count == 0)
            {
                yaComparadas = null;
                Mostrar("Inserta la memoria", TextoPedir, Color.DarkGreen);
                return;
            }

            string estas = string.Join("\n", conectadas.Select(m => m.Serie).OrderBy(s => s, StringComparer.Ordinal));
            if (estas == yaComparadas)
            {
                Mostrar(conectadas.Count == 1 ? "Esa no es una memoria autorizada" : "Ninguna de esas es una memoria autorizada", TextoPedir, Aviso);
                return;
            }

            yaComparadas = estas;
            Mostrar("Comprobando la memoria...", TextoPedir, Color.DarkGreen);
            comparando = Task.Run(() => Emergencia.Reconocer(conectadas, lista));
        }

        private void Mostrar(string titulo, string texto, Color color)
        {
            lblTitulo.Text = titulo;
            lblTitulo.ForeColor = color;
            lblTexto.Text = texto;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            buscarMemoria.Stop();
            buscarMemoria.Dispose();
            base.OnFormClosed(e);
        }

        /// <summary>Devuelve true si se reconocio una memoria autorizada.</summary>
        public static bool Pedir(IWin32Window dueno)
        {
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
