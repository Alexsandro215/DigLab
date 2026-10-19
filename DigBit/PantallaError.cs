using System;
using System.Drawing;
using System.Windows.Forms;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Pantalla de error legible para el usuario final. Sustituye al dialogo de
    /// excepcion no controlada de .NET y a los MessageBox con el error crudo de
    /// MySQL. En modo kiosco no se puede cerrar y, si se pide, reintenta sola
    /// pasado un tiempo, para que un equipo sin red se recupere sin que nadie
    /// tenga que tocarlo.
    /// </summary>
    public class PantallaError : Form
    {
        private readonly int segundosAutoReintento;
        private int segundosRestantes;
        private Timer temporizador;
        private Button btnPrincipal;
        private Button btnCerrar;
        private Label lblTitulo;
        private Label lblMensaje;
        private Label lblRutaLog;

        private PantallaError(string titulo, string mensaje, string textoBotonPrincipal, bool permitirCerrar, int segundosAutoReintento)
        {
            this.segundosAutoReintento = segundosAutoReintento;
            InicializarComponentes(titulo, mensaje, textoBotonPrincipal, permitirCerrar);
        }

        /// <summary>
        /// Muestra la pantalla de forma modal. Devuelve DialogResult.Retry si el
        /// usuario pulsa el boton principal (o vence el reintento automatico) y
        /// DialogResult.Cancel si pulsa Cerrar.
        /// </summary>
        public static DialogResult Mostrar(string titulo, string mensaje, string textoBotonPrincipal, bool permitirCerrar, int segundosAutoReintento)
        {
            using (PantallaError pantalla = new PantallaError(titulo, mensaje, textoBotonPrincipal, permitirCerrar, segundosAutoReintento))
            {
                return pantalla.ShowDialog();
            }
        }

        private void InicializarComponentes(string titulo, string mensaje, string textoBotonPrincipal, bool permitirCerrar)
        {
            BackColor = Color.White;
            ClientSize = new Size(640, 360);
            FormBorderStyle = Kiosco.Activo ? FormBorderStyle.None : FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = permitirCerrar;
            // Visible en la barra de tareas: es la unica ventana de la app cuando
            // aparece, y ocultarla de ahi la convierte en una ventana "con dueno"
            // que ni el usuario ni las herramientas de diagnostico encuentran.
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = Kiosco.Activo;
            Text = "DigBit - " + titulo;

            lblTitulo = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 16F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(30, 25),
                Size = new Size(580, 40),
                Text = titulo
            };

            lblMensaje = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 11F),
                ForeColor = Color.Black,
                Location = new Point(30, 80),
                Size = new Size(580, 170),
                Text = mensaje
            };

            lblRutaLog = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8F),
                ForeColor = Color.DimGray,
                Location = new Point(30, 258),
                Size = new Size(580, 36),
                Text = "Detalle técnico registrado en: " + Log.ArchivoDeHoy
            };

            btnPrincipal = CrearBoton(textoBotonPrincipal, permitirCerrar ? 320 : 480, 300);
            btnPrincipal.DialogResult = DialogResult.Retry;

            Controls.Add(lblTitulo);
            Controls.Add(lblMensaje);
            Controls.Add(lblRutaLog);
            Controls.Add(btnPrincipal);
            AcceptButton = btnPrincipal;

            if (permitirCerrar)
            {
                btnCerrar = CrearBoton("Cerrar", 480, 300);
                btnCerrar.DialogResult = DialogResult.Cancel;
                Controls.Add(btnCerrar);
                CancelButton = btnCerrar;
            }
            else
            {
                // Sin boton Cerrar tampoco se cierra con Alt+F4 / Escape (UserClosing)
                // ni con un WM_CLOSE directo como el de "Finalizar tarea"
                // (TaskManagerClosing). Los botones y el reintento automatico cierran
                // asignando DialogResult, que llega aqui como CloseReason.None, asi que
                // no se bloquean; el apagado de Windows (WindowsShutDown) tampoco.
                // (No sirve mirar DialogResult: en un formulario modal WinForms lo pone
                // en Cancel ANTES de disparar FormClosing.)
                FormClosing += (s, e) =>
                {
                    if (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.TaskManagerClosing)
                    {
                        e.Cancel = true;
                    }
                };
            }

            if (segundosAutoReintento > 0)
            {
                segundosRestantes = segundosAutoReintento;
                ActualizarTextoReintento();
                temporizador = new Timer { Interval = 1000 };
                temporizador.Tick += Temporizador_Tick;
                temporizador.Start();
            }
        }

        private Button CrearBoton(string texto, int x, int y)
        {
            Button boton = new Button
            {
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(x, y),
                Size = new Size(130, 42),
                Text = texto,
                UseVisualStyleBackColor = false
            };
            boton.FlatAppearance.BorderColor = Color.DarkGreen;
            boton.FlatAppearance.BorderSize = 2;
            return boton;
        }

        private void Temporizador_Tick(object sender, EventArgs e)
        {
            segundosRestantes--;
            if (segundosRestantes <= 0)
            {
                temporizador.Stop();
                // En un formulario modal basta con asignar DialogResult para cerrarlo;
                // Close() se reportaria como UserClosing y lo bloquearia la guarda.
                DialogResult = DialogResult.Retry;
                return;
            }

            ActualizarTextoReintento();
        }

        private void ActualizarTextoReintento()
        {
            btnPrincipal.Text = btnPrincipal.Text.Split('(')[0].TrimEnd() + " (" + segundosRestantes + ")";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && temporizador != null)
            {
                temporizador.Dispose();
                temporizador = null;
            }

            base.Dispose(disposing);
        }
    }
}
