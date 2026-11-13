using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// Laboratorios > "Apagar equipos...": la orden de apagar los equipos encendidos de un
    /// laboratorio, o de todos (OrdenesEquiposDatos; pedido del 2026-11-13: "Manda senal y
    /// las apaga"). La cumple el vigilante de cada equipo en el minuto siguiente; a quien
    /// este usando uno, Windows le avisa 2 minutos antes. Se confirma con la contrasena.
    /// </summary>
    internal sealed class DialogoApagarEquipos : Form
    {
        private const int Ancho = 600;
        private readonly ComboBox cboLaboratorio;
        private readonly List<KeyValuePair<int, string>> laboratorios;
        private readonly Label lblError;

        /// <param name="idElegido">El laboratorio elegido en la lista, para empezar con el; 0, ninguno.</param>
        public static void Mostrar(IWin32Window dueno, int idElegido)
        {
            if (Permisos.Bloquear(dueno, "Mandar apagar los equipos de un laboratorio")) return;
            List<KeyValuePair<int, string>> laboratorios;
            try
            {
                laboratorios = ActualizacionesDatos.LaboratoriosHabilitados();
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(dueno, "Leer los laboratorios para apagar sus equipos", ex, "No se pudo leer la lista de laboratorios", "Apagar equipos");
                return;
            }

            string nombre = null;
            using (DialogoApagarEquipos dialogo = new DialogoApagarEquipos(laboratorios, idElegido))
            {
                if (dialogo.ShowDialog(dueno) != DialogResult.OK)
                {
                    return;
                }

                nombre = dialogo.Mandado;
            }

            MessageBox.Show(dueno, "Listo: los equipos encendidos de " + nombre + " se apagan en el próximo minuto. "
                + "A quien esté usando uno, Windows le avisa y se apaga 2 minutos después.",
                "Apagar equipos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>A quien se mando: "DESARROLLO DE SOFTWARE" o "todos los laboratorios".</summary>
        public string Mandado { get; private set; }

        private DialogoApagarEquipos(List<KeyValuePair<int, string>> laboratorios, int idElegido)
        {
            this.laboratorios = laboratorios;
            EstiloHorarios.PrepararDialogo(this, "Apagar equipos", new Size(Ancho, 360));
            Controls.Add(EstiloAdmin.TituloDialogo("Apagar los equipos", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(24, 56),
                Size = new Size(Ancho - 48, 96),
                Text = "Los equipos encendidos de ese laboratorio se apagan en el próximo minuto. A quien esté usando uno, Windows "
                    + "le avisa y se apaga 2 minutos después, para que guarde su trabajo. Los que estén apagados no hacen nada, y los que "
                    + "se enciendan después tampoco: la orden vale " + OrdenesEquiposDatos.MinutosParaApagar + " minutos. "
                    + "Solo los equipos del laboratorio con DigLab 1.3 o más nueva."
            });

            Controls.Add(EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(24, 160)));
            cboLaboratorio = EstiloHorarios.CrearCombo(new Point(24, 184), Ancho - 48);
            cboLaboratorio.DropDownStyle = ComboBoxStyle.DropDownList;
            cboLaboratorio.Items.Add("Todos los laboratorios");
            int elegir = laboratorios.Count == 1 ? 1 : 0;
            for (int i = 0; i < laboratorios.Count; i++)
            {
                cboLaboratorio.Items.Add(laboratorios[i].Value);
                if (laboratorios[i].Key == idElegido)
                {
                    elegir = i + 1;
                }
            }

            cboLaboratorio.SelectedIndex = elegir;
            Controls.Add(cboLaboratorio);

            lblError = EstiloAdmin.LineaError(new Point(24, 224), Ancho - 48, 44);
            Controls.Add(lblError);
            EstiloAdmin.Peligroso(EstiloAdmin.BotonesDialogo(this, "Apagar", Mandar));
            ActiveControl = cboLaboratorio;
        }

        private void Mandar()
        {
            if (Permisos.Bloquear(this, "Mandar apagar los equipos de un laboratorio")) return;
            int indice = cboLaboratorio.SelectedIndex;
            int? id = indice > 0 ? laboratorios[indice - 1].Key : (int?)null;
            string nombre = indice > 0 ? laboratorios[indice - 1].Value : "todos los laboratorios";
            if (!DialogoContrasenaAdmin.Confirmar(this, "¿Apagar los equipos de " + nombre + "?",
                "Se apagan los que estén encendidos; a quien esté usando uno, Windows le avisa 2 minutos antes.", "Apagar",
                "Mandar apagar los equipos de un laboratorio"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                OrdenesEquiposDatos.MandarApagar(id, nombre);
            }
            catch (ErrorDeDatos ex)
            {
                Log.Aviso("Mandar apagar los equipos: " + ex.Message);
                lblError.Text = ex.Message;
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Mandar apagar los equipos", ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo mandar la orden", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            Mandado = nombre;
            DialogResult = DialogResult.OK;
        }
    }
}
