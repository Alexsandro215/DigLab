using DigLab.conexion;
using DigLab.Infraestructura;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>Guardar la plantilla y leer un Excel con horario, con los mensajes de lo que falle en el archivo.</summary>
    internal static class ExcelDeHorario
    {
        /// <summary>Un archivo que se puede intentar leer como libro de Excel (por su extension).</summary>
        public static bool EsExcel(string ruta)
        {
            string extension = (Path.GetExtension(ruta ?? "") ?? "").ToLowerInvariant();
            return extension == ".xlsx" || extension == ".xlsm" || extension == ".xls";
        }

        /// <summary>
        /// Lee el archivo y lo entiende contra lo registrado. Null (y por que, en error) si el
        /// archivo no se puede abrir o no es un .xlsx; un fallo de la base se lanza.
        /// </summary>
        public static LecturaDeHorario Leer(string ruta, CicloPreliminarDatos datos, CicloLeido leido, out string error)
        {
            error = null;
            List<HojaLeida> hojas;
            string archivo = Path.GetFileName(ruta);
            try
            {
                hojas = LectorExcel.Leer(ruta);
            }
            catch (LectorExcel.ArchivoNoValido ex)
            {
                error = "«" + archivo + "»: " + ex.Message;
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
            {
                Log.Aviso("ExcelDeHorario.Leer: " + ex.Message);
                error = "No se pudo abrir «" + archivo + "»: " + ex.Message;
                return null;
            }

            return HorarioEnExcel.Leer(hojas, datos.LeerCatalogosDeHorario(), leido);
        }

        /// <summary>Pregunta donde guardar el libro y lo guarda; la ruta, o null si se cancelo o no se pudo (ya se dijo).</summary>
        public static string Guardar(IWin32Window dueno, byte[] libro, string nombreHorario)
        {
            string nombre = new string(("Horario " + (nombreHorario ?? "")).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
            using (SaveFileDialog dialogo = new SaveFileDialog
            {
                Title = "Guardar el horario en Excel",
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                AddExtension = true,
                FileName = nombre + ".xlsx",
                OverwritePrompt = true
            })
            {
                if (dialogo.ShowDialog(dueno) != DialogResult.OK)
                {
                    return null;
                }

                try
                {
                    File.WriteAllBytes(dialogo.FileName, libro);
                    Log.Info("Horario en Excel guardado en " + dialogo.FileName + ".");
                    return dialogo.FileName;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Aviso("ExcelDeHorario.Guardar: " + ex.Message);
                    MessageBox.Show(dueno, "No se pudo guardar «" + Path.GetFileName(dialogo.FileName) + "»: " + ex.Message
                        + "\n\nSi está abierto en Excel, ciérralo y vuelve a intentarlo, o guárdalo con otro nombre.",
                        "Guardar el horario en Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }
        }
    }

    /// <summary>
    /// "No se cargo nada del Excel": lo que hay que corregir en el archivo, casilla por
    /// casilla (pedido del 2026-11-12: con algo no registrado, avisar y no cargar).
    /// </summary>
    internal class DialogoProblemasExcel : Form
    {
        private const int Ancho = 900;

        public DialogoProblemasExcel(string archivo, LecturaDeHorario lectura)
        {
            int alto = Math.Min(620, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 60);
            EstiloHorarios.PrepararDialogo(this, "Cargar un Excel", new Size(Ancho, alto));
            Label titulo = EstiloAdmin.TituloDialogo("No se cargó nada del Excel", Ancho - 48);
            titulo.ForeColor = PiezasConfiguracion.TextoRojo;
            int n = lectura.Problemas.Count;
            string texto = "«" + archivo + "» tiene " + (n == 1 ? "una cosa" : n + " cosas") + " por corregir. Corrígelas en el archivo, guárdalo y vuelve a cargarlo: "
                + "mientras tanto el horario en preparación no cambia.\n"
                + "Solo valen las personas docentes, los grupos y las materias registrados en DigLab (la hoja «" + HorarioEnExcel.HojaRegistrados
                + "» de la plantilla los tiene todos). Una clase que todavía no tiene docente lleva «" + HorarioEnExcel.SinDocente + "».";
            Label explicacion = PiezasConfiguracion.Texto(texto, new Point(24, 54), new Size(Ancho - 48, 20), 9.5F, Color.FromArgb(50, 50, 50));
            explicacion.Height = PiezasConfiguracion.AltoDe(texto, explicacion.Font, explicacion.Width) + 4;

            int arriba = explicacion.Bottom + 10;
            DataGridView lista = EstiloHorarios.CrearGrid(new Point(24, arriba), new Size(Ancho - 48, alto - 56 - 14 - arriba));
            lista.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            lista.DefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
            lista.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            lista.Columns.Add("Donde", "Dónde");
            lista.Columns.Add("Que", "Qué corregir");
            EstiloHorarios.Pesos(lista, 30, 70);
            foreach (string problema in lectura.Problemas)
            {
                int dos = problema.IndexOf(": ", StringComparison.Ordinal);
                lista.Rows.Add(dos > 0 ? problema.Substring(0, dos) : "", dos > 0 ? problema.Substring(dos + 2) : problema);
            }

            Button cerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(Ancho - 24 - 120, ClientSize.Height - 56), new Size(120, 40));
            cerrar.DialogResult = DialogResult.Cancel;
            Button copiar = EstiloHorarios.CrearBoton("Copiar la lista", new Point(cerrar.Left - 10 - 160, ClientSize.Height - 56), new Size(160, 40));
            Label copiado = PiezasConfiguracion.Texto("", new Point(24, ClientSize.Height - 46), new Size(copiar.Left - 40, 22), 9.5F, Color.DarkGreen);
            copiar.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText("No se cargó nada de «" + archivo + "». Por corregir:\r\n" + string.Join("\r\n", lectura.Problemas.Select(p => "- " + p)));
                    copiado.Text = "Lista copiada: pégala donde la necesites.";
                }
                catch (Exception ex)
                {
                    Log.Error("DialogoProblemasExcel: copiar la lista", ex);
                    copiado.ForeColor = PiezasConfiguracion.TextoError;
                    copiado.Text = "No se pudo copiar: otro programa está usando el portapapeles. Inténtalo otra vez.";
                }
            };

            Controls.Add(titulo);
            Controls.Add(explicacion);
            Controls.Add(lista);
            Controls.Add(copiado);
            Controls.Add(copiar);
            Controls.Add(cerrar);
            CancelButton = cerrar;
            AcceptButton = cerrar;
            ActiveControl = lista;
        }

        protected override void Dispose(bool disposing)
        {
            List<DrawingFont> letras = disposing ? PiezasConfiguracion.LetrasDe(this, Font) : new List<DrawingFont>();
            base.Dispose(disposing);
            foreach (DrawingFont letra in letras)
            {
                letra.Dispose();
            }
        }
    }

    /// <summary>
    /// "Cargar el Excel": lo que trae cada laboratorio frente a lo que el horario tiene
    /// ahora, lo que se quita y los avisos (choques), antes de cambiar nada.
    /// </summary>
    internal class DialogoConfirmarExcel : Form
    {
        private const int Ancho = 820;

        /// <param name="enBlanco">Es un horario nuevo, en blanco: no hay "ahora tiene".</param>
        public DialogoConfirmarExcel(string archivo, string nombreHorario, LecturaDeHorario lectura, bool enBlanco)
        {
            EstiloHorarios.PrepararDialogo(this, "Cargar un Excel", new Size(Ancho, 400));
            Label titulo = EstiloAdmin.TituloDialogo(enBlanco ? "Crear «" + nombreHorario + "» con el Excel" : "Cargar el Excel en «" + nombreHorario + "»", Ancho - 48);
            Controls.Add(titulo);
            int laboratorios = lectura.Laboratorios.Count;
            string texto = enBlanco
                ? "Se crea el horario en preparación con " + (laboratorios == 1 ? "el laboratorio" : "los " + laboratorios + " laboratorios") + " de «" + archivo + "»."
                : "De «" + archivo + "» se " + (laboratorios == 1 ? "carga este laboratorio" : "cargan estos " + laboratorios + " laboratorios")
                  + ". Lo que este horario en preparación tiene en " + (laboratorios == 1 ? "él" : "ellos") + " se reemplaza por lo del Excel; los demás laboratorios no cambian.";
            Label explicacion = PiezasConfiguracion.Texto(texto, new Point(24, 54), new Size(Ancho - 48, 20), 9.5F, Color.FromArgb(50, 50, 50));
            explicacion.Height = PiezasConfiguracion.AltoDe(texto, explicacion.Font, explicacion.Width) + 4;
            Controls.Add(explicacion);
            int y = explicacion.Bottom + 10;

            DataGridView tabla = EstiloHorarios.CrearGrid(new Point(24, y), new Size(Ancho - 48, 40 + 34 * Math.Min(6, laboratorios) + 2));
            tabla.ScrollBars = ScrollBars.Vertical;
            tabla.Columns.Add("Laboratorio", "Laboratorio");
            tabla.Columns.Add("Excel", "Lo que trae el Excel");
            if (!enBlanco)
            {
                tabla.Columns.Add("Ahora", "Lo que tiene ahora (se reemplaza)");
                EstiloHorarios.Pesos(tabla, 34, 30, 36);
            }
            else
            {
                EstiloHorarios.Pesos(tabla, 50, 50);
            }

            foreach (LaboratorioDelExcel lab in lectura.Laboratorios)
            {
                string nombre = EstiloHorarios.NombreLaboratorio(lab.Nombre) + (lab.Activo ? "" : " (deshabilitado)");
                string trae = lab.Clases == 0 ? "Vacío (sin clases)" : Clases(lab.Clases) + ", " + HorariosDatos.Duracion(lab.Minutos) + " a la semana";
                if (enBlanco)
                {
                    tabla.Rows.Add(nombre, trae);
                }
                else
                {
                    tabla.Rows.Add(nombre, trae, lab.ClasesAntes == 0 ? "Nada" : Clases(lab.ClasesAntes) + ", " + HorariosDatos.Duracion(lab.MinutosAntes));
                }
            }

            tabla.ClearSelection();
            Controls.Add(tabla);
            y = tabla.Bottom + 12;

            if (lectura.SeQuitan.Count > 0)
            {
                int n = lectura.SeQuitan.Count;
                string quitan = (n == 1 ? "Se quita del horario una clase que solo tenía horas en " : "Se quitan del horario " + n + " clases que solo tenían horas en ")
                    + (laboratorios == 1 ? "ese laboratorio" : "esos laboratorios") + " y no viene" + (n == 1 ? "" : "n") + " en el Excel: "
                    + string.Join(", ", lectura.SeQuitan.Take(6)) + (n > 6 ? " y " + (n - 6) + " más" : "") + ".";
                Label aviso = PiezasConfiguracion.AvisoAmbar(quitan, new Point(24, y), Ancho - 48);
                Controls.Add(aviso);
                y = aviso.Bottom + 10;
            }

            if (lectura.Avisos.Count > 0)
            {
                bool choques = lectura.Avisos.Any(a => a.StartsWith("Choque", StringComparison.Ordinal));
                string encabezado = "Avisos (no impiden cargarlo" + (choques ? "; los choques sí impiden aplicarlo hasta resolverlos" : "") + "):";
                Label lblAvisos = PiezasConfiguracion.Texto(encabezado, new Point(24, y), new Size(Ancho - 48, 20), 9.5F, PiezasConfiguracion.TextoAmbar, FontStyle.Bold);
                Controls.Add(lblAvisos);
                y = lblAvisos.Bottom + 4;
                TextBox avisos = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = Color.White,
                    Font = new DrawingFont("Century Gothic", 9F),
                    Location = new Point(24, y),
                    Size = new Size(Ancho - 48, Math.Min(130, 22 + 18 * lectura.Avisos.Count)),
                    Text = string.Join("\r\n", lectura.Avisos.Select(a => "• " + a)),
                    TabStop = false
                };
                Controls.Add(avisos);
                y = avisos.Bottom + 10;
            }

            Label nota = PiezasConfiguracion.Texto("Nada cambia en los laboratorios hasta aplicarlo en «3. Aplicar con fechas».",
                new Point(24, y), new Size(Ancho - 48, 20), 9.5F, PiezasConfiguracion.TextoGris);
            Controls.Add(nota);
            y = nota.Bottom + 12;

            ClientSize = new Size(Ancho, y + 40 + 16);
            Button cargar = EstiloAdmin.BotonesDialogo(this, enBlanco ? "Crear con el Excel" : "Cargar el Excel", () => DialogResult = DialogResult.OK);
            ActiveControl = cargar;
        }

        private static string Clases(int n)
        {
            return n == 1 ? "1 clase" : n + " clases";
        }

        protected override void Dispose(bool disposing)
        {
            List<DrawingFont> letras = disposing ? PiezasConfiguracion.LetrasDe(this, Font) : new List<DrawingFont>();
            base.Dispose(disposing);
            foreach (DrawingFont letra in letras)
            {
                letra.Dispose();
            }
        }
    }
}
