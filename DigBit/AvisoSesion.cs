using System;
using System.Drawing;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Aviso no modal, siempre encima, de que la sesion en el equipo esta por
    /// terminar. No bloquea nada: el alumno puede seguir guardando su trabajo.
    /// </summary>
    public class AvisoSesion : Form
    {
        public AvisoSesion(string mensaje)
        {
            // Con "Tu bitacora quedo registrada." delante (liberado con menos de 5
            // minutos) el mensaje puede pasar de dos lineas: la ventana crece con el.
            DrawingFont letraMensaje = new DrawingFont("Century Gothic", 10.5F);
            int altoMensaje = Math.Max(70, TextRenderer.MeasureText(mensaje ?? "", letraMensaje, new Size(470, int.MaxValue),
                TextFormatFlags.WordBreak).Height + 6);

            BackColor = Color.White;
            ClientSize = new Size(520, 120 + altoMensaje);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            Text = "DigBit - Tu sesión termina pronto";

            Label lblTitulo = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(25, 20),
                Size = new Size(470, 32),
                Text = "Tu sesión termina pronto"
            };

            Label lblMensaje = new Label
            {
                AutoSize = false,
                Font = letraMensaje,
                ForeColor = Color.Black,
                Location = new Point(25, 60),
                Size = new Size(470, altoMensaje),
                Text = mensaje
            };

            Button btnEntendido = new Button
            {
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(365, 65 + altoMensaje),
                Size = new Size(130, 40),
                Text = "Entendido",
                UseVisualStyleBackColor = false
            };
            btnEntendido.FlatAppearance.BorderColor = Color.DarkGreen;
            btnEntendido.FlatAppearance.BorderSize = 2;
            btnEntendido.Click += (s, e) => Close();

            Controls.Add(lblTitulo);
            Controls.Add(lblMensaje);
            Controls.Add(btnEntendido);
            AcceptButton = btnEntendido;
        }
    }
}
