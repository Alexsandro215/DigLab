using System;
using System.Drawing;
using System.Windows.Forms;
using DigBit.conexion;
using DrawingFont = System.Drawing.Font;

namespace DigBit.Infraestructura
{
    /// <summary>
    /// Antes de apagar el equipo desde DigBit: si quedan bitacoras sin enviar,
    /// se intenta enviarlas unos segundos (con una ventanita que lo dice, sin
    /// congelar nada) y, si no se puede, se avisa. Apagar no las pierde si la
    /// carpeta de datos sobrevive al reinicio; con Deep Freeze y sin ella, si:
    /// antes los botones de apagar ni miraban la cola.
    /// </summary>
    internal static class AvisoApagado
    {
        private static readonly TimeSpan TiempoParaEnviar = TimeSpan.FromSeconds(10);

        /// <summary>True si se puede seguir apagando.</summary>
        public static bool PuedeApagar(IWin32Window dueno)
        {
            int pendientes = ColaBitacoras.Pendientes();
            if (pendientes > 0)
            {
                using (VentanaEnvio envio = new VentanaEnvio(pendientes))
                {
                    envio.ShowDialog(dueno);
                }
            }

            int quedan = ColaBitacoras.Pendientes();
            int apartadas = ColaBitacoras.Rechazadas();
            if (pendientes > 0)
            {
                Log.Info("Antes de apagar habia " + pendientes + " bitacoras por enviar; quedan " + quedan + (apartadas > 0 ? " y hay " + apartadas + " apartadas" : "") + ".");
            }

            // Lo apartado (el servidor lo rechazo) solo importa si se va a perder.
            if (quedan == 0 && (apartadas == 0 || CarpetaDatos.Persistente))
            {
                return true;
            }

            if (CarpetaDatos.Persistente)
            {
                Log.Aviso("Se apaga con " + quedan + " bitacoras sin enviar; estan en la carpeta de datos y se enviaran al volver a encender.");
                MessageBox.Show(dueno,
                    "No hay conexión con el servidor: " + Quedan(quedan) + " por enviar." + Environment.NewLine + Environment.NewLine
                    + "Están a salvo en este equipo y se enviarán solas al volver a encenderlo.",
                    "Apagar el equipo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }

            // Sin carpeta de datos que sobreviva al reinicio: con Deep Freeze se pierde todo.
            string problema = (quedan > 0 ? "No hay conexión con el servidor y " + Quedan(quedan) + " de estudiantes por enviar. " : "")
                + (apartadas > 0 ? (quedan > 0 ? "Además hay " : "Hay ") + Bitacoras(apartadas) + " que el servidor rechazó y nadie ha revisado. " : "");
            string remedio = quedan > 0
                ? "Lo seguro es dejarlo encendido en la pantalla de inicio hasta que se envíen solas"
                  + (apartadas > 0 ? ", y avisar a la persona responsable del laboratorio." : ".")
                : "Lo seguro es dejarlo encendido y avisar a la persona responsable del laboratorio.";
            bool apagar = MessageBox.Show(dueno,
                problema + Environment.NewLine + Environment.NewLine
                + "Si este equipo tiene Deep Freeze, al apagarlo se perderán. " + remedio + Environment.NewLine + Environment.NewLine
                + "¿Apagar de todos modos?",
                "Apagar el equipo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            Log.Aviso("Apagar con " + quedan + " bitacoras sin enviar y " + apartadas + " apartadas, sin carpeta de datos persistente: "
                + (apagar ? "se apaga igual." : "no se apaga."));
            return apagar;
        }

        private static string Bitacoras(int n)
        {
            return n == 1 ? "1 bitácora" : n + " bitácoras";
        }

        /// <summary>"queda 1 bitácora", "quedan 3 bitácoras".</summary>
        private static string Quedan(int n)
        {
            return (n == 1 ? "queda " : "quedan ") + Bitacoras(n);
        }

        /// <summary>"Enviando N bitacoras..." mientras el enviador lo intenta; se cierra sola.</summary>
        private sealed class VentanaEnvio : Form
        {
            public VentanaEnvio(int pendientes)
            {
                BackColor = Color.White;
                ClientSize = new Size(480, 120);
                ControlBox = false;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterScreen;
                Text = "DigBit";
                TopMost = Kiosco.Activo;

                Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Font = new DrawingFont("Century Gothic", 11F),
                    ForeColor = Color.DarkGreen,
                    Text = "Enviando " + (pendientes == 1 ? "1 bitácora pendiente" : pendientes + " bitácoras pendientes") + " al servidor...",
                    TextAlign = ContentAlignment.MiddleCenter
                });

                Shown += async (s, e) =>
                {
                    await EnviadorBitacoras.EnviarAhoraAsync(TiempoParaEnviar);
                    Close();
                };
            }
        }
    }
}
