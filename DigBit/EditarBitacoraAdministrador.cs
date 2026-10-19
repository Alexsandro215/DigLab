using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Corrige lo que un alumno registro en una sesion: el equipo y, por area
    /// (red, hardware, software), la falla y su comentario. Las listas son las
    /// mismas que ve el alumno al registrar (Consultas.OpcionesDeFalla); se
    /// puede escribir otra. El UPDATE solo entra si el registro sigue como se leyo.
    /// Cada correccion queda en el registro de diagnostico (Log), con lo que habia antes.
    /// </summary>
    public class EditarBitacoraAdministrador : Form
    {
        private readonly Consultas consultas = new Consultas();
        private readonly Dictionary<string, string> original = new Dictionary<string, string>();
        private readonly Dictionary<string, ComboBox> fallas = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, TextBox> comentarios = new Dictionary<string, TextBox>();
        private readonly TextBox txtEquipo;
        private readonly Label lblError;
        // Para el log de la correccion: la matricula del alumno y la sesion como se ve arriba.
        private readonly string matricula;
        private readonly string descripcionSesion;

        public EditarBitacoraAdministrador(DataRow fila, string alumno, string sesion)
        {
            foreach (string columna in new[] { "fk_codigo_accesos", "fk_usuario", "numero_computadora" }
                         .Concat(Consultas.AreasDeFalla.SelectMany(a => new[] { "falla_" + a, "comentarios_" + a })))
            {
                original[columna] = fila[columna] == DBNull.Value ? "" : fila[columna].ToString();
            }

            matricula = fila.Table.Columns.Contains("numero_identificador") && fila["numero_identificador"] != DBNull.Value
                ? fila["numero_identificador"].ToString()
                : "";
            descripcionSesion = sesion ?? "";

            EstiloHorarios.PrepararDialogo(this, "Corregir registro", new Size(680, 470));
            Controls.Add(EstiloAdmin.TituloDialogo("Registro de " + alumno, 632));
            Controls.Add(new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(24, 48),
                Size = new Size(632, 20),
                Text = sesion
            });

            Controls.Add(EstiloHorarios.CrearEtiqueta("Equipo *", new Point(24, 82)));
            txtEquipo = EstiloHorarios.CrearTexto(new Point(24, 105), 200);
            // Los limites de la base del TESCHI, mas cortos que los de 01_schema.sql.
            txtEquipo.MaxLength = 80;
            txtEquipo.Text = original["numero_computadora"];
            txtEquipo.TextChanged += (s, e) => txtEquipo.BackColor = SystemColors.Window;
            Controls.Add(txtEquipo);

            int y = 146;
            foreach (string area in Consultas.AreasDeFalla)
            {
                Controls.Add(EstiloHorarios.CrearEtiqueta("Falla de " + area, new Point(24, y)));
                ComboBox falla = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDown,
                    Font = new DrawingFont("Century Gothic", 10F),
                    Location = new Point(24, y + 23),
                    Size = new Size(250, 26),
                    MaxLength = 45
                };
                falla.Items.Add("Ninguno");
                falla.Items.AddRange(Consultas.OpcionesDeFalla(area));
                falla.Items.Add("Otro");
                falla.Text = original["falla_" + area];
                Controls.Add(falla);
                fallas[area] = falla;

                Controls.Add(EstiloHorarios.CrearEtiqueta("Comentario de " + area, new Point(290, y)));
                TextBox comentario = EstiloHorarios.CrearTexto(new Point(290, y + 23), 366);
                comentario.MaxLength = 255;
                comentario.Text = original["comentarios_" + area];
                Controls.Add(comentario);
                comentarios[area] = comentario;
                y += 62;
            }

            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(24, y + 4),
                Size = new Size(632, 20),
                Text = "Sin falla en un área: «Ninguno» y el comentario vacío."
            });

            lblError = EstiloAdmin.LineaError(new Point(24, y + 30), 632, 22);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            ActiveControl = txtEquipo;
        }

        private void Guardar()
        {
            string equipo = txtEquipo.Text.Trim();
            if (equipo.Length == 0)
            {
                lblError.Text = "Falta el equipo en el que registró.";
                txtEquipo.BackColor = EstiloAdmin.FondoError;
                txtEquipo.Focus();
                return;
            }

            Dictionary<string, string> nuevo = new Dictionary<string, string> { { "numero_computadora", equipo } };
            foreach (string area in Consultas.AreasDeFalla)
            {
                string falla = fallas[area].Text.Trim();
                nuevo["falla_" + area] = falla.Length == 0 ? "Ninguno" : falla;
                nuevo["comentarios_" + area] = comentarios[area].Text.Trim();
            }

            if (nuevo.All(par => par.Value == original[par.Key].Trim()))
            {
                // Nada que guardar.
                DialogResult = DialogResult.Cancel;
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                bool guardado = consultas.ActualizarRegistroBitacora(
                    original["fk_codigo_accesos"],
                    original["fk_usuario"],
                    original["numero_computadora"],
                    original["falla_red"],
                    original["comentarios_red"],
                    original["falla_hardware"],
                    original["comentarios_hardware"],
                    original["falla_software"],
                    original["comentarios_software"],
                    nuevo["numero_computadora"],
                    nuevo["falla_red"],
                    nuevo["comentarios_red"],
                    nuevo["falla_hardware"],
                    nuevo["comentarios_hardware"],
                    nuevo["falla_software"],
                    nuevo["comentarios_software"]);

                if (guardado)
                {
                    // Constancia de la correccion (RNF79), en el registro de diagnostico
                    // de este equipo: quien corrigio, la sesion, el alumno y cada campo
                    // que cambio, antes y despues. En la base no queda (haria falta una
                    // migracion con quien y cuando la corrigio).
                    Log.Info("Correccion de bitacora: el administrador " + ParaElLog(Datos_User.getUser())
                        + " corrigio el registro del alumno " + ParaElLog(matricula) + " (usuario " + original["fk_usuario"] + ")"
                        + " en la sesion " + original["fk_codigo_accesos"] + " (" + ParaElLog(descripcionSesion) + "): "
                        + string.Join("; ", nuevo.Where(par => par.Value != original[par.Key].Trim())
                            .Select(par => par.Key + " '" + ParaElLog(original[par.Key]) + "' -> '" + ParaElLog(par.Value) + "'"))
                        + ".");
                    DialogResult = DialogResult.OK;
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>Solo ASCII, como el resto del log: sin acentos y con los guiones largos de la descripcion como "-".</summary>
        private static string ParaElLog(string texto)
        {
            return PersonalizacionClasesDatos.Ascii((texto ?? "").Replace('\u2013', '-').Replace('\u2014', '-'));
        }
    }
}
