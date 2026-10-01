using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Seccion Laboratorios del administrador: la lista con su uso (clases en el
    /// horario, sesiones, equipos que registraron hace poco), dar de alta, cambiar
    /// el nombre, ver su horario y quitarlo. Quitar decide que hacer: uno que
    /// nunca se uso se borra; uno con historial se desactiva; uno con clases
    /// vigentes no se deja quitar hasta mover o quitar esas clases.
    /// </summary>
    public class AdministrarLaboratorios : PantallaAdmin
    {
        private readonly LaboratoriosDatos datos = new LaboratoriosDatos();
        private List<LaboratorioInfo> laboratorios = new List<LaboratorioInfo>();

        private readonly Button btnRenombrar;
        private readonly Button btnHorario;
        private readonly Button btnQuitar;

        public AdministrarLaboratorios()
            : base("Laboratorios", "Los equipos de cada laboratorio se configuran con su nombre exacto. Doble clic para cambiarlo.", new Size(1000, 560))
        {
            BotonPrincipalArriba("Nuevo laboratorio", 190).Click += (s, e) => NuevoLaboratorio();

            CrearLista(96);
            grid.Columns.Add("nombre", "Laboratorio");
            grid.Columns.Add("clases", "Franjas en el horario");
            grid.Columns.Add("sesiones", "Sesiones");
            grid.Columns.Add("ultima", "Última sesión");
            grid.Columns.Add("equipos", "Equipos (30 días)");
            grid.Columns.Add("estado", "Estado");
            EstiloHorarios.Pesos(grid, 240, 130, 80, 110, 120, 100);
            foreach (string columna in new[] { "clases", "sesiones", "equipos" })
            {
                grid.Columns[columna].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }

            ayuda.SetToolTip(grid, "Equipos (30 días): cuántas computadoras distintas registraron bitácoras en ese laboratorio el último mes.");

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            btnQuitar = BotonAbajo("Quitar", 120);
            btnQuitar.Click += (s, e) => QuitarOReactivar();
            btnHorario = BotonAbajo("Ver su horario", 150);
            btnHorario.Click += (s, e) => VerHorarioSeleccionado();
            btnRenombrar = BotonAbajo("Cambiar nombre", 160);
            btnRenombrar.Click += (s, e) => RenombrarSeleccionado();

            ActualizarBotones();
        }

        /// <summary>El administrador pide ver el horario de un laboratorio (lo abre el panel principal).</summary>
        public event Action<int> VerHorario;

        protected override void Recargar(string seleccionar)
        {
            try
            {
                laboratorios = datos.Listar();
            }
            catch (Exception ex)
            {
                laboratorios = new List<LaboratorioInfo>();
                MostrarError("Leer la lista de laboratorios", ex, "No se pudo leer la lista de laboratorios");
            }

            // Los desactivados, al final.
            List<LaboratorioInfo> ordenados = laboratorios.OrderBy(l => !l.Activo).ThenBy(l => l.Nombre).ToList();
            LlenarLista(ordenados,
                l => new object[]
                {
                    l.Nombre,
                    l.FranjasVigentes,
                    l.Sesiones,
                    EstiloAdmin.Fecha(l.UltimaSesion),
                    l.EquiposRecientes,
                    l.Activo ? "En uso" : "Desactivado"
                },
                l => !l.Activo, l => l.Nombre, seleccionar);

            int desactivados = laboratorios.Count(l => !l.Activo);
            lblResumen.Text = laboratorios.Count == 0
                ? "Todavía no hay laboratorios."
                : (laboratorios.Count - desactivados) + ((laboratorios.Count - desactivados) == 1 ? " laboratorio en uso" : " laboratorios en uso")
                  + (desactivados > 0 ? " y " + desactivados + (desactivados == 1 ? " desactivado" : " desactivados") : "");
            NotaFija = datos.DesactivacionDisponible ? "" : "Para desactivar falta la migración 08 en la base.";
        }

        protected override void ActualizarBotones()
        {
            if (btnQuitar == null)
            {
                return;
            }

            LaboratorioInfo laboratorio = Seleccionado<LaboratorioInfo>();
            btnRenombrar.Enabled = laboratorio != null;
            btnHorario.Enabled = laboratorio != null && laboratorio.Activo;
            btnQuitar.Enabled = laboratorio != null;
            btnQuitar.Text = laboratorio != null && !laboratorio.Activo ? "Reactivar" : "Quitar";
        }

        protected override void AlDobleClic()
        {
            RenombrarSeleccionado();
        }

        private void NuevoLaboratorio()
        {
            using (DialogoLaboratorio dialogo = new DialogoLaboratorio(datos, null))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.NombreGuardado);
                    Avisar("Se dio de alta " + dialogo.NombreGuardado + ". Asígnale clases en Horarios y configura sus equipos con ese nombre.");
                }
            }
        }

        private void RenombrarSeleccionado()
        {
            LaboratorioInfo laboratorio = Seleccionado<LaboratorioInfo>();
            if (laboratorio == null)
            {
                return;
            }

            using (DialogoLaboratorio dialogo = new DialogoLaboratorio(datos, laboratorio))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.NombreGuardado);
                    Avisar("Ahora se llama " + dialogo.NombreGuardado + ". Recuerda configurar sus equipos con el nombre nuevo.");
                }
            }
        }

        private void VerHorarioSeleccionado()
        {
            LaboratorioInfo laboratorio = Seleccionado<LaboratorioInfo>();
            if (laboratorio != null && VerHorario != null)
            {
                VerHorario(laboratorio.Id);
            }
        }

        private void QuitarOReactivar()
        {
            LaboratorioInfo laboratorio = Seleccionado<LaboratorioInfo>();
            if (laboratorio == null)
            {
                return;
            }

            try
            {
                if (!laboratorio.Activo)
                {
                    datos.Reactivar(laboratorio.Id);
                    Recargar(laboratorio.Nombre);
                    Avisar(laboratorio.Nombre + " vuelve a estar en uso: sale en el panel y en Horarios.");
                    return;
                }

                if (laboratorio.FranjasVigentes > 0)
                {
                    // Con clases vigentes no se puede: se explica y se ofrece ir a ellas.
                    if (MessageBox.Show(this,
                        laboratorio.Nombre + " tiene " + laboratorio.FranjasVigentes
                        + (laboratorio.FranjasVigentes == 1 ? " franja" : " franjas") + " de clases vigentes en el horario.\n\n"
                        + "Muévelas a otro laboratorio o quítalas en Horarios, y después vuelve a quitarlo.\n\n¿Abrir su horario ahora?",
                        "Quitar laboratorio", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        VerHorarioSeleccionado();
                    }

                    return;
                }

                if (laboratorio.SinUso)
                {
                    if (DialogoConfirmacion.Preguntar(this, "Quitar laboratorio",
                        "¿Borrar " + laboratorio.Nombre + "?",
                        "Nunca tuvo horario ni sesiones, así que se borra sin perder nada.",
                        "Borrar"))
                    {
                        datos.Borrar(laboratorio.Id);
                        Recargar(null);
                        Avisar("Se borró " + laboratorio.Nombre + ".");
                    }

                    return;
                }

                if (!datos.DesactivacionDisponible)
                {
                    MessageBox.Show(this,
                        laboratorio.Nombre + " tiene historial (" + laboratorio.Sesiones + " sesiones), así que no se borra: se desactiva. "
                        + "Para eso falta aplicar db/08_migracion_bajas.sql en la base de datos.",
                        "Quitar laboratorio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (DialogoConfirmacion.Preguntar(this, "Quitar laboratorio",
                    "¿Desactivar " + laboratorio.Nombre + "?",
                    "Tiene historial (" + laboratorio.Sesiones + (laboratorio.Sesiones == 1 ? " sesión" : " sesiones")
                    + "), así que no se borra: deja de salir en el panel y en Horarios, y sus bitácoras se conservan en los informes. "
                    + "Puedes reactivarlo cuando quieras.",
                    "Desactivar"))
                {
                    datos.Desactivar(laboratorio.Id);
                    Recargar(laboratorio.Nombre);
                    Avisar("Se desactivó " + laboratorio.Nombre + ".");
                }
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MostrarError("Quitar o reactivar el laboratorio " + laboratorio.Nombre, ex, "No se pudo cambiar el laboratorio");
            }
        }
    }

    /// <summary>Alta de un laboratorio o cambio de su nombre, con lo que eso implica para sus equipos.</summary>
    internal class DialogoLaboratorio : Form
    {
        private readonly LaboratoriosDatos datos;
        private readonly LaboratorioInfo existente;
        private readonly TextBox txtNombre;
        private readonly Label lblError;

        public DialogoLaboratorio(LaboratoriosDatos datos, LaboratorioInfo existente)
        {
            this.datos = datos;
            this.existente = existente;
            bool nuevo = existente == null;
            EstiloHorarios.PrepararDialogo(this, nuevo ? "Nuevo laboratorio" : "Cambiar nombre", new Size(520, 330));
            Controls.Add(EstiloAdmin.TituloDialogo(nuevo ? "Nuevo laboratorio" : "Cambiar el nombre de " + existente.Nombre, 472));

            Controls.Add(EstiloHorarios.CrearEtiqueta("Nombre *", new Point(24, 62)));
            txtNombre = EstiloHorarios.CrearTexto(new Point(24, 85), 472);
            txtNombre.MaxLength = 80;
            txtNombre.Text = nuevo ? "" : existente.Nombre;
            txtNombre.TextChanged += (s, e) => { txtNombre.BackColor = SystemColors.Window; lblError.Text = ""; };
            Controls.Add(txtNombre);

            // Lo que hay que hacer despues en los equipos, que se configuran con el nombre.
            string nota = nuevo
                ? "Después configura los equipos de este laboratorio con este nombre exacto "
                  + "(configurar_equipo.ps1 -Laboratorio \"...\"). Sus clases se asignan en Horarios."
                : "Los equipos de " + existente.Nombre + " están configurados con ese nombre: hasta que los configures con el nuevo "
                  + "(configurar_equipo.ps1 -Laboratorio \"...\") no reconocerán su laboratorio ni aceptarán códigos."
                  + (existente.EquiposRecientes > 0
                      ? " En el último mes registraron bitácoras " + existente.EquiposRecientes + (existente.EquiposRecientes == 1 ? " equipo." : " equipos.")
                      : "");
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F, nuevo ? FontStyle.Regular : FontStyle.Bold),
                ForeColor = nuevo ? Color.FromArgb(80, 80, 80) : Color.FromArgb(190, 85, 0),
                Location = new Point(24, 122),
                Size = new Size(472, 80),
                Text = nota
            });

            lblError = EstiloAdmin.LineaError(new Point(24, 206), 472, 40);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, nuevo ? "Dar de alta" : "Cambiar nombre", Guardar);
            ActiveControl = txtNombre;
        }

        public string NombreGuardado { get; private set; }

        private void Guardar()
        {
            // Sin espacios de mas: el equipo compara el nombre tal cual.
            string nombre = string.Join(" ", txtNombre.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (nombre.Length == 0)
            {
                lblError.Text = "Escribe el nombre del laboratorio.";
                txtNombre.BackColor = EstiloAdmin.FondoError;
                txtNombre.Focus();
                return;
            }

            if (existente != null && nombre == existente.Nombre)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }

            try
            {
                if (existente == null)
                {
                    datos.Crear(nombre);
                }
                else
                {
                    datos.Renombrar(existente.Id, existente.Nombre, nombre);
                }

                NombreGuardado = nombre;
                DialogResult = DialogResult.OK;
            }
            catch (ErrorDeDatos ex)
            {
                lblError.Text = ex.Message;
                txtNombre.BackColor = EstiloAdmin.FondoError;
                txtNombre.Focus();
            }
            catch (Exception ex)
            {
                Infraestructura.Log.Error(existente == null ? "Alta de laboratorio" : "Cambiar el nombre del laboratorio " + existente.Nombre, ex);
                lblError.Text = "No se pudo guardar: " + ex.Message;
            }
        }
    }
}
