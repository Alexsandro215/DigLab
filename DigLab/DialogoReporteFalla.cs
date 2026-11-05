using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// "Reportar una falla" del panel del profesor, mientras su clase esta en
    /// curso: donde (todo el laboratorio o un equipo), de que es (red,
    /// hardware, software u otra cosa, como el proyector), que falla (de la
    /// misma lista que ve el alumno, o escrita) y un detalle opcional. Quien
    /// guarda es el panel (ReportesProfesorDatos.Guardar): si la clase ya
    /// termino, lo dice aqui y no se guarda.
    ///
    /// Como "Dar asistencia": "Enviar reporte" se enciende solo con lo que hace
    /// falta (de que es y que falla, o con "Otra", que falla escrita); mientras
    /// tanto la linea del error dice en gris que falta. Nunca se le da el foco a
    /// un area: un RadioButton se marca solo al recibirlo.
    /// </summary>
    internal class DialogoReporteFalla : Form
    {
        private const int Ancho = 580;
        private const string TodoElLaboratorio = "Todo el laboratorio (no es de un equipo)";
        private const string OtraFalla = "Otra (escríbela)";
        // Lo que falta para enviarlo (sin error a la vista), en la linea del error.
        private static readonly Color TextoFalta = Color.FromArgb(105, 105, 105);

        private readonly Func<string, string, string, string, string> guardar;
        private readonly ComboBox cmbEquipo;
        private readonly List<RadioButton> areas = new List<RadioButton>();
        private readonly ComboBox cmbFalla;
        private readonly TextBox txtFalla;
        private readonly TextBox txtDetalle;
        private readonly Label lblError;
        private readonly Color colorError;
        private readonly Button btnEnviar;
        // Mientras se guarda, apagado aunque todo este elegido.
        private bool enviando;

        /// <param name="clase">"Programacion Orientada a Objetos, grupo ISC-5001, en Prueba Codigo unico, de 07:00 a 09:00"</param>
        /// <param name="hasta">Hasta cuando se puede reportar (el fin de la clase).</param>
        /// <param name="equipos">Los equipos del laboratorio (pueden faltar: entonces se escribe el numero).</param>
        /// <param name="guardar">(equipo o null, area, falla, detalle) -> null si se guardo, o por que no.</param>
        public DialogoReporteFalla(string clase, TimeSpan hasta, IEnumerable<string> equipos, Func<string, string, string, string, string> guardar)
        {
            this.guardar = guardar;
            DrawingFont fuenteDetalle = new DrawingFont("Century Gothic", 10F);
            // La clase ya trae su horario ("de 07:00 a 09:00"): no se repite la hora.
            string subtitulo = clase + ". Solo se puede reportar mientras la clase sigue.";
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
            // Se lee la etiqueta ("No tiene cable de red"); el elemento, y lo que se guarda,
            // sigue siendo el valor de la lista (Consultas.EtiquetaDeFalla).
            cmbFalla.FormattingEnabled = true;
            cmbFalla.Format += (s, e) => e.Value = Consultas.EtiquetaDeFalla(e.ListItem as string);
            cmbFalla.SelectedIndexChanged += (s, e) =>
            {
                MostrarFallaEscrita();
                ActualizarEnviar(true);
            };
            txtFalla = EstiloHorarios.CrearTexto(new Point(24, y + 24 + 34 + 6), Ancho - 48);
            txtFalla.MaxLength = ReportesProfesorDatos.LargoFalla;
            txtFalla.Visible = false;
            txtFalla.TextChanged += (s, e) => ActualizarEnviar(true);
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
            lblError.UseMnemonic = false;
            colorError = lblError.ForeColor;
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
            ActualizarEnviar(true);
        }

        /// <summary>Lo que falta para enviarlo, o null si ya se puede: de que es, que falla y, con "Otra", escribirla.</summary>
        private string QueFalta()
        {
            string area = AreaElegida;
            if (area == null)
            {
                return "Falta: elegir de qué es la falla.";
            }

            string elegida = cmbFalla.SelectedItem as string;
            if (elegida == null)
            {
                return "Falta: elegir qué falla.";
            }

            return elegida == OtraFalla && txtFalla.Text.Trim().Length == 0 ? "Falta: escribir qué falla." : null;
        }

        /// <summary>
        /// "Enviar reporte" encendido solo con lo que hace falta (y no mientras se
        /// guarda); apagado, la linea del error dice en gris que falta. Un error en
        /// rojo se quita al cambiar algo (limpiarError) y se queda tras no poder guardar.
        /// </summary>
        private void ActualizarEnviar(bool limpiarError)
        {
            if (btnEnviar == null)
            {
                return;
            }

            string falta = QueFalta();
            bool listo = falta == null && !enviando;
            if (btnEnviar.Enabled != listo)
            {
                btnEnviar.Enabled = listo;
            }

            if (limpiarError || lblError.ForeColor != colorError || lblError.Text.Length == 0)
            {
                lblError.ForeColor = TextoFalta;
                lblError.Text = falta ?? "";
            }
        }

        /// <summary>Un error (en rojo): por que no se pudo y que hacer.</summary>
        private void MostrarError(string texto)
        {
            lblError.ForeColor = colorError;
            lblError.Text = texto ?? "";
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
            // Otra area: el error de antes ya no aplica; queda lo que falta.
            ActualizarEnviar(true);
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
                // Sin dar el foco a un area: la marcaria sola.
                MostrarError("Elige de qué es la falla.");
                return;
            }

            string falla = cmbFalla.SelectedItem as string == OtraFalla ? txtFalla.Text.Trim() : cmbFalla.SelectedItem as string;
            if (string.IsNullOrEmpty(falla))
            {
                MostrarError(cmbFalla.SelectedItem as string == OtraFalla ? "Escribe qué falla." : "Elige qué falla.");
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
            enviando = true;
            ActualizarEnviar(false);
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
                enviando = false;
            }

            if (motivo == null)
            {
                DialogResult = DialogResult.OK;
                return;
            }

            // Encendido otra vez solo si sigue todo elegido (y el error a la vista).
            MostrarError(motivo);
            ActualizarEnviar(false);
        }
    }
}
