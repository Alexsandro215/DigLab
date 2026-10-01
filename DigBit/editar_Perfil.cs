using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DigBit.conexion;

namespace DigBit
{
    public partial class editar_Perfil : Form
    {
        private Consultas consultas;
        private string modoPerfil;
        private bool cargandoCombos;

        public editar_Perfil(string modoPerfil = null)
        {
            InitializeComponent();
            consultas = new Consultas();
            this.modoPerfil = modoPerfil;
            cargandoCombos = true;
            CargarDatoscbCarrera();
            LimpiarComboGrupo();
            CargarDatoscbSemestre();
            cargandoCombos = false;
            string smatricula = Datos_User.getUser();
            txtId.Visible = false;
            AplicarModoFormulario();

            if (!string.IsNullOrEmpty(smatricula))
            {
                txtMatricula.Texts = smatricula;
                txtMatricula.Enabled = false;
            }
            else
            {
                txtMatricula.Enabled = true;
            }


        }

        private void AplicarModoFormulario()
        {
            bool esProfesor = string.Equals(modoPerfil, "profesor", StringComparison.OrdinalIgnoreCase);
            bool esAlumno = string.Equals(modoPerfil, "alumno", StringComparison.OrdinalIgnoreCase);

            if (txtMatricula.Enabled)
            {
                if (esProfesor)
                {
                    txtMatricula.Texts = "Numero de empleado";
                }
                else if (esAlumno)
                {
                    txtMatricula.Texts = "Matricula";
                }
                else
                {
                    txtMatricula.Texts = "Numero de identificador";
                }
            }

            lblCarrera.Visible = !esProfesor;
            lblSemestre.Visible = !esProfesor;
            cbCarrera.Visible = !esProfesor;
            cbSemestre.Visible = !esProfesor;

            // El alumno ya no declara grupo: el codigo es del profesor y vale para
            // todas sus clases. El que tuviera guardado se conserva.
            lblGrupo.Visible = false;
            cbGrupo.Visible = false;
        }


        private void btnCancelar_Click(object sender, EventArgs e)
        {
            this.Hide();
        }

        private void btnBuscarPerfil_Click(object sender, EventArgs e)
        {
            string numeroIdentificador = ObtenerMatriculaABuscar();

            if (!string.IsNullOrEmpty(numeroIdentificador))
            {
                // Llamar al método ConsultaEditar de la instancia consultas y obtener el usuario con información adicional
                UsuarioConInformacionAdicional usuario = consultas.ConsultaEditar(numeroIdentificador);
                if (usuario != null)
                {
                    // Mostrar los datos del usuario en los controles
                    MostrarDatos(usuario);
                }
                else
                {
                    MessageBox.Show("No se encontró información para el usuario con el número de identificador proporcionado.", "Información no encontrada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                MessageBox.Show("No se ha proporcionado un número de identificador.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }

        private string ObtenerMatriculaABuscar()
        {
            string matriculaCapturada = txtMatricula.Texts?.Trim();
            bool esTextoGuia =
                string.Equals(matriculaCapturada, "Ingresa tu matricula", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(matriculaCapturada, "Numero de empleado", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(matriculaCapturada, "Matricula", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(matriculaCapturada, "Numero de identificador", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(matriculaCapturada) && !esTextoGuia)
            {
                return matriculaCapturada;
            }

            return Datos_User.getUser();
        }

        public void MostrarDatos(UsuarioConInformacionAdicional usuario)
        {
            if (usuario.TipoUsuarioId == 2)
            {
                modoPerfil = "profesor";
            }
            else if (usuario.TipoUsuarioId == 1)
            {
                modoPerfil = "alumno";
            }

            AplicarModoFormulario();
            txtMatricula.Texts = ObtenerMatriculaABuscar();

            // Asignar los valores a los controles en editar_Perfil
            txtNombre.Texts = usuario.Nombre;
            txtApellidoP.Texts = usuario.ApellidoPaterno;
            txtApellidoM.Texts = usuario.ApellidoMaterno;
            txtCorreo.Texts = usuario.Correo;
            cbSemestre.Texts = usuario.NombreSemestre.ToString();
            txtId.Text = usuario.id.ToString();

            cargandoCombos = true;
            SeleccionarOpcionPorTexto(cbCarrera, usuario.NombreCarrera);
            OpcionCombo carreraSeleccionada = cbCarrera.SelectedItem as OpcionCombo;
            if (carreraSeleccionada != null)
            {
                CargarDatoscbGrupo(carreraSeleccionada.Id);
                SeleccionarOpcionPorTexto(cbGrupo, usuario.NombreGrupo);
            }
            else
            {
                LimpiarComboGrupo();
            }
            cargandoCombos = false;

            cbSemestre.Texts = usuario.NombreSemestre;
        }

        private void btnGuardar_Click(object sender, EventArgs e)
        {
            string idUsuario = txtId.Text;
            string nuevoNombre = txtNombre.Texts;
            string nuevoApellidoPaterno = txtApellidoP.Texts;
            string nuevoApellidoMaterno = txtApellidoM.Texts;
            string nuevoCorreo = txtCorreo.Texts;
            bool esProfesor = string.Equals(modoPerfil, "profesor", StringComparison.OrdinalIgnoreCase);
            bool exito;

            if (esProfesor)
            {
                exito = consultas.ActualizarUsuarioBasico(idUsuario, nuevoNombre, nuevoApellidoPaterno, nuevoApellidoMaterno, nuevoCorreo);
            }
            else
            {
                OpcionCombo carreraSeleccionada = cbCarrera.SelectedItem as OpcionCombo;
                if (carreraSeleccionada == null)
                {
                    MessageBox.Show("Selecciona tu carrera.", "Datos incompletos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int nuevaCarreraId = carreraSeleccionada.Id;
                int nuevoSemestreId = consultas.ObtenerIdSemestre(cbSemestre.Texts);
                exito = consultas.ActualizarUsuario(idUsuario, nuevoNombre, nuevoApellidoPaterno, nuevoApellidoMaterno, nuevoCorreo, nuevaCarreraId, null, nuevoSemestreId);
            }

            if (exito)
            {
                MessageBox.Show("Datos actualizados correctamente.", "Éxito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Error al actualizar los datos del usuario.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }
        private void CargarDatoscbCarrera()
        {
            try
            {
                cbCarrera.Items.Clear();

                foreach (OpcionCombo carrera in consultas.ObtenerCarreras())
                {
                    cbCarrera.Items.Add(carrera);
                }

                cbCarrera.SelectedIndex = -1;
                cbCarrera.Texts = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar los nombres de las carreras: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CargarDatoscbGrupo(int idCarrera)
        {
            try
            {
                cbGrupo.Items.Clear();

                foreach (OpcionCombo grupo in consultas.ObtenerGruposPorCarrera(idCarrera))
                {
                    cbGrupo.Items.Add(grupo);
                }

                cbGrupo.SelectedIndex = -1;
                cbGrupo.Texts = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar los nombres del grupo: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LimpiarComboGrupo()
        {
            cbGrupo.Items.Clear();
            cbGrupo.SelectedIndex = -1;
            cbGrupo.Texts = "";
        }

        private void CargarDatoscbSemestre()
        {
            try
            {
                List<string> nombresSemestre = consultas.ObtenerNombresSemestre();
                cbSemestre.Items.Clear();
                foreach (string nombreSemestre in nombresSemestre)
                {
                    cbSemestre.Items.Add(nombreSemestre);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar los nombres del semestre: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SeleccionarOpcionPorTexto(RJControls.RJComboBox combo, string texto)
        {
            combo.SelectedIndex = -1;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), texto, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void cbCarrera_OnSelectedIndexChanged(object sender, EventArgs e)
        {
            if (cargandoCombos)
            {
                return;
            }

            OpcionCombo carreraSeleccionada = cbCarrera.SelectedItem as OpcionCombo;
            if (carreraSeleccionada == null)
            {
                LimpiarComboGrupo();
                return;
            }

            CargarDatoscbGrupo(carreraSeleccionada.Id);
        }
    }
}
