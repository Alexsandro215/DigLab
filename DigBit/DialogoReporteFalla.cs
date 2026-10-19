using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// "Reportar una falla" del panel del profesor, mientras su clase esta en
    /// curso: donde (todo el laboratorio o un equipo), de que es (red,
    /// hardware, software u otra cosa, como el proyector), que falla (de la
    /// misma lista que ve el alumno, o escrita) y un detalle opcional. Quien
    /// guarda es el panel (ReportesProfesorDatos.Guardar): si la clase ya
    /// termino, lo dice aqui y no se guarda.
    /// </summary>
    internal class DialogoReporteFalla : Form
    {
        private const int Ancho = 580;
        private const string TodoElLaboratorio = "Todo el laboratorio (no es de un equipo)";
        private const string OtraFalla = "Otra (escríbela)";

        private readonly Func<string, string, string, string, string> guardar;
        private readonly ComboBox cmbEquipo;
        private readonly List<RadioButton> areas = new List<RadioButton>();
        private readonly ComboBox cmbFalla;
        private readonly TextBox txtFalla;
        private readonly TextBox txtDetalle;
        private readonly Label lblError;
        private readonly Button btnEnviar;

        /// <param name="clase">"Programacion Orientada a Objetos, grupo ISC-5001, en Prueba Codigo unico"</param>
        /// <param name="hasta">Hasta cuando se puede reportar (el fin de la clase).</param>
        /// <param name="equipos">Los equipos del laboratorio (pueden faltar: entonces se escribe el numero).</param>
        /// <param name="guardar">(equipo o null, area, falla, detalle) -> null si se guardo, o por que no.</param>
        public DialogoReporteFalla(string clase, TimeSpan hasta, IEnumerable<string> equipos, Func<string, string, string, string, string> guardar)
        {
            this.guardar = guardar;
            DrawingFont fuenteDetalle = new DrawingFont("Century Gothic", 10F);
            string subtitulo = clase + ". Solo se puede reportar mientras la clase está en curso (hasta las " + HorariosDatos.Hora(hasta) + ").";
            int altoSubtitulo = TextRenderer.MeasureText(subtitulo, fuenteDetalle, new Size(Ancho - 48, int.MaxValue), TextFormatFlags.WordBreak).Height + 4;

            int y = 52 + altoSubtitulo + 16;
            Label etiquetaEquipo = EstiloHorarios.CrearEtiqueta("¿Dónde está la falla?", new Point(24, y));
            cmbEquipo = EstiloHorarios.CrearCombo(new Point(24, y + 24), 330);
            cmbEquipo.DropDownStyle = ComboBoxStyle.DropDown;
            cmbEquipo.MaxLength = ReportesProfesorDatos.LargoEquipo;
            cmbEquipo.Items.Add(TodoElLaboratorio);
            foreach (string equipo in (equipos ?? Enumerable.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                cmbEquipo.Items.Add(equipo.Trim());
            }

            cmbEquipo.SelectedIndex = 0;
            y += 24 + 34 + 14;

            Label etiquetaArea = EstiloHorarios.CrearEtiqueta("¿De qué es?", new Point(24, y));
            int x = 24;
            foreach (string area in ReportesProfesorDatos.Areas)
            {
                RadioButton opcion = new RadioButton
                {
                    AutoSize = true,
                    Font = new DrawingFont("Century Gothic", 10F),
                    ForeColor = Color.FromArgb(30, 30, 30),
                    Location = new Point(x, y + 26),
                    Tag = area,
                    Text = area == "otro" ? "Otra cosa (proyector, luz...)" : ReportesProfesorDatos.NombreDeArea(area)
                };
                opcion.CheckedChanged += (s, e) =>
                {
                    if (((RadioButton)s).Checked)
                    {
                        CambiarArea();
                    }
                };
                areas.Add(opcion);
                Controls.Add(opcion);
                x += TextRenderer.MeasureText(opcion.Text, opcion.Font).Width + 34;
            }

            y += 26 + 28 + 14;
            Label etiquetaFalla = EstiloHorarios.CrearEtiqueta("¿Qué falla?", new Point(24, y));
            cmbFalla = EstiloHorarios.CrearCombo(new Point(24, y + 24), 330);
            cmbFalla.Enabled = false;
            cmbFalla.SelectedIndexChanged += (s, e) => MostrarFallaEscrita();
            txtFalla = EstiloHorarios.CrearTexto(new Point(24, y + 24 + 34 + 6), Ancho - 48);
            txtFalla.MaxLength = ReportesProfesorDatos.LargoFalla;
            txtFalla.Visible = false;
            y += 24 + 34 + 6 + 34 + 12;

            Label etiquetaDetalle = EstiloHorarios.CrearEtiqueta("Detalle (opcional)", new Point(24, y));
            txtDetalle = EstiloHorarios.CrearTexto(new Point(24, y + 24), Ancho - 48);
            txtDetalle.Multiline = true;
            txtDetalle.ScrollBars = ScrollBars.Vertical;
            txtDetalle.Height = 64;
            txtDetalle.MaxLength = ReportesProfesorDatos.LargoComentarios;
            y += 24 + 64 + 10;

            // Tres renglones: un fallo dice por que y que hacer (MensajeDeFallo).
            lblError = EstiloAdmin.LineaError(new Point(24, y), Ancho - 48, 56);
            y += 56 + 8;

            EstiloHorarios.PrepararDialogo(this, "Reportar una falla", new Size(Ancho, y + 40 + 16));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Reportar una falla", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = fuenteDetalle,
                ForeColor = Color.FromArgb(70, 70, 70),
                Location = new Point(24, 52),
                Size = new Size(Ancho - 48, altoSubtitulo),
                Text = subtitulo
            });
            Controls.AddRange(new Control[] { etiquetaEquipo, cmbEquipo, etiquetaArea, etiquetaFalla, cmbFalla, txtFalla, etiquetaDetalle, txtDetalle, lblError });
            btnEnviar = EstiloAdmin.BotonesDialogo(this, "Enviar reporte", Enviar);
            ActiveControl = cmbEquipo;
        }

        private string AreaElegida
        {
            get
            {
                RadioButton elegida = areas.FirstOrDefault(a => a.Checked);
                return elegida == null ? null : (string)elegida.Tag;
            }
        }

        /// <summary>Las fallas de la lista de esa area (las del alumno) y "Otra"; de "otra cosa", solo escribirla.</summary>
        private void CambiarArea()
        {
            string area = AreaElegida;
            cmbFalla.Items.Clear();
            foreach (string opcion in Consultas.OpcionesDeFalla(area))
            {
                cmbFalla.Items.Add(opcion);
            }

            cmbFalla.Items.Add(OtraFalla);
            cmbFalla.Enabled = area != "otro";
            cmbFalla.SelectedIndex = area == "otro" ? cmbFalla.Items.Count - 1 : -1;
            MostrarFallaEscrita();
            lblError.Text = "";
        }

        private void MostrarFallaEscrita()
        {
            bool escrita = cmbFalla.SelectedItem as string == OtraFalla;
            txtFalla.Visible = escrita;
            if (escrita && Visible)
            {
                txtFalla.Focus();
            }
        }

        private void Enviar()
        {
            string area = AreaElegida;
            if (area == null)
            {
                lblError.Text = "Elige de qué es la falla.";
                areas[0].Focus();
                return;
            }

            string falla = cmbFalla.SelectedItem as string == OtraFalla ? txtFalla.Text.Trim() : cmbFalla.SelectedItem as string;
            if (string.IsNullOrEmpty(falla))
            {
                lblError.Text = cmbFalla.SelectedItem as string == OtraFalla ? "Escribe qué falla." : "Elige qué falla.";
                if (txtFalla.Visible)
                {
                    txtFalla.Focus();
                }
                else
                {
                    cmbFalla.Focus();
                }

                return;
            }

            string equipo = cmbEquipo.Text.Trim();
            if (equipo.Length == 0 || equipo == TodoElLaboratorio)
            {
                equipo = null;
            }

            string motivo;
            btnEnviar.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                motivo = guardar(equipo, area, falla, txtDetalle.Text);
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro.
                Log.Error("Guardar el reporte de falla del profesor", ex);
                motivo = MensajeDeFallo.Explicar("No se pudo guardar el reporte", ex, AvisoDeFalloDocente.SiSigue);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnEnviar.Enabled = true;
            }

            if (motivo == null)
            {
                DialogResult = DialogResult.OK;
                return;
            }

            lblError.Text = motivo;
        }
    }
}
