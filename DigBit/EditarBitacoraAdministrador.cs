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

            EstiloHorarios.PrepararDialogo(this, "Corregir registro", new Size(680, 430));
            Controls.Add(EstiloAdmin.TituloDialogo("Registro de " + alumno, 632));
            // Quien es (su matricula) y de que sesion, debajo del nombre.
            Controls.Add(new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(24, 48),
                Size = new Size(632, 20),
                Text = (matricula.Length > 0 ? "Matrícula " + matricula + " · " : "") + sesion,
                UseMnemonic = false
            });

            Controls.Add(EstiloHorarios.CrearEtiqueta("Equipo *", new Point(24, 82)));
            txtEquipo = EstiloHorarios.CrearTexto(new Point(24, 105), 200);
            // Los limites de la base del TESCHI, mas cortos que los de 01_schema.sql.
            txtEquipo.MaxLength = 80;
            txtEquipo.Text = original["numero_computadora"];
            txtEquipo.TextChanged += (s, e) => txtEquipo.BackColor = SystemColors.Window;
            Controls.Add(txtEquipo);

            // La falla, tan ancha como su etiqueta mas larga ("El cable está suelto o
            // desconectado"); el comentario, con lo que quede.
            DrawingFont letraFalla = new DrawingFont("Century Gothic", 10F);
            int anchoFalla = Math.Max(250, Consultas.AreasDeFalla.SelectMany(Consultas.OpcionesDeFalla)
                .Max(v => TextRenderer.MeasureText(Consultas.EtiquetaDeFalla(v), letraFalla).Width) + 30);
            int xComentario = 24 + anchoFalla + 16;
            int y = 146;
            foreach (string area in Consultas.AreasDeFalla)
            {
                Controls.Add(EstiloHorarios.CrearEtiqueta("Falla de " + area, new Point(24, y)));
                ComboBox falla = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDown,
                    Font = letraFalla,
                    Location = new Point(24, y + 23),
                    Size = new Size(anchoFalla, 26),
                    MaxLength = 45
                };
                falla.Items.Add("Ninguno");
                falla.Items.AddRange(Consultas.OpcionesDeFalla(area));
                falla.Items.Add("Otro");
                // Las mismas palabras que ve el estudiante (Consultas.EtiquetaDeFalla); se
                // guarda el valor de siempre (Guardar, ValorDeFalla).
                falla.FormattingEnabled = true;
                falla.Format += (s, e) => e.Value = Consultas.EtiquetaDeFalla(e.ListItem as string);
                falla.Text = Consultas.EtiquetaDeFalla(original["falla_" + area]);
                Controls.Add(falla);
                fallas[area] = falla;

                Controls.Add(EstiloHorarios.CrearEtiqueta("Comentario de " + area, new Point(xComentario, y)));
                TextBox comentario = EstiloHorarios.CrearTexto(new Point(xComentario, y + 23), 680 - 24 - xComentario);
                comentario.MaxLength = 255;
                comentario.Text = original["comentarios_" + area];
                comentario.TextChanged += (s, e) => comentario.BackColor = SystemColors.Window;
                Controls.Add(comentario);
                comentarios[area] = comentario;
                // Con "Ninguno" no hay nada que comentar: al elegirlo de la lista, el
                // comentario se vacia y se apaga, como lo registra el estudiante. Al abrir
                // y al teclear solo se apaga si ya esta vacio (no se borra nada a medias).
                string estaArea = area;
                AjustarComentario(estaArea, false);
                falla.SelectedIndexChanged += (s, e) => AjustarComentario(estaArea, true);
                falla.TextChanged += (s, e) => AjustarComentario(estaArea, false);
                y += 62;
            }

            lblError = EstiloAdmin.LineaError(new Point(24, y + 8), 632, 22);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            ActiveControl = txtEquipo;
        }

        private static bool EsNinguno(string falla)
        {
            string valor = Consultas.ValorDeFalla(falla);
            return valor.Length == 0 || valor.Equals("Ninguno", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// El comentario de un area con "Ninguno": vacio y apagado. alElegir: se eligio
        /// "Ninguno" de la lista (se vacia); si no, solo se apaga si ya esta vacio.
        /// </summary>
        private void AjustarComentario(string area, bool alElegir)
        {
            TextBox comentario = comentarios[area];
            ComboBox falla = fallas[area];
            // Al elegir de la lista, el elemento (el valor): el texto de la caja puede ir un paso atras.
            string elegido = alElegir ? falla.SelectedItem as string : null;
            bool ninguno = EsNinguno(elegido ?? falla.Text);
            if (ninguno && elegido != null)
            {
                comentario.Text = "";
            }

            comentario.Enabled = !ninguno || comentario.Text.Trim().Length > 0;
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
                // Lo que se lee ("No tiene cable de red") se guarda como siempre ("Sin cable de red").
                string falla = Consultas.ValorDeFalla(fallas[area].Text);
                string comentario = comentarios[area].Text.Trim();
                // "Otro" lleva escrito que falla, como lo registra el estudiante.
                if (falla.Equals("Otro", StringComparison.OrdinalIgnoreCase) && comentario.Length == 0)
                {
                    lblError.Text = "Con «Otro», escribe qué falla en el comentario de " + area + ".";
                    comentarios[area].Enabled = true;
                    comentarios[area].BackColor = EstiloAdmin.FondoError;
                    comentarios[area].Focus();
                    return;
                }

                nuevo["falla_" + area] = falla.Length == 0 ? "Ninguno" : falla;
                nuevo["comentarios_" + area] = comentario;
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
