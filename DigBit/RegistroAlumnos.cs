using DigBit.conexion;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DigBit
{

    public partial class RegistroAlumnos : Form
    {
        // Declaración de variables
        private Consultas consultas;


        public RegistroAlumnos()
        {
            InitializeComponent();
            consultas = new Consultas();
            CargarDatoscbCarrera();
            QuitarGrupo();
        }

        /// <summary>
        /// El alumno ya no declara grupo: el codigo es del profesor y vale para
        /// todas sus clases. Se quita el combo (y su icono) y la contrasena y el
        /// correo suben a su hueco.
        /// </summary>
        private void QuitarGrupo()
        {
            int hueco = txtPassword.Top - cbGrupo.Top;
            cbGrupo.Visible = false;
            pictureBox6.Visible = false;
            foreach (Control control in new Control[] { pictureBox7, txtPassword, pictureBox8, rjTcorreo })
            {
                control.Top -= hueco;
            }
        }

        // (No esta cableado en el Designer: el boton usa btnCancelar_Click_1.) Se
        // deja cerrando el formulario para que nadie vuelva a crear un Login suelto;
        // el Login lo gestiona AppContexto.
        private void btnCancelar_Click(object sender, EventArgs e)
        {
            this.Close();
        }



        private void txtNombre_Enter(object sender, EventArgs e)
        {
            if (txtNombre.Texts == "Nombre")
            {
                txtNombre.Texts = "";
                txtNombre.ForeColor = Color.Black;

            }
        }
        private void txtNombre_Leave(object sender, EventArgs e)
        {
            if(txtNombre.Texts == "")
            {
                txtNombre.Texts += "Nombre";
                txtNombre.ForeColor = Color.Black;
            }
        }

        private void txtApellidoP_Enter(object sender, EventArgs e)
        {
            if(txtApellidoP.Texts== "Apellido Paterno")
            {
                txtApellidoP.Texts="";
                txtApellidoP.ForeColor = Color.Black;
            }
        }

        private void txtApellidoP_Leave(object sender, EventArgs e)
        {
            if(txtApellidoP.Texts == "")
            {
                txtApellidoP.Texts = "Apellido Paterno";
                txtApellidoP.ForeColor= Color.Black;
            }
        }

        private void txtApellidoM_Enter(object sender, EventArgs e)
        {
            if(txtApellidoM.Texts == "Apellido Materno")
            {
                txtApellidoM.Texts = "";
                txtApellidoM.ForeColor= Color.Black;
            }
        }

        private void txtApellidoM_Leave(object sender, EventArgs e)
        {
            if(txtApellidoM.Texts == "")
            {
                txtApellidoM.Texts = "Apellido Materno";
                txtApellidoM.ForeColor = Color.Black;
            }
        }

        private void txtMatricula_Enter(object sender, EventArgs e)
        {
            if(txtMatricula.Texts == "Matricula")
            {
                txtMatricula.Texts = "";
                txtMatricula.ForeColor= Color.Black;
            }
        }

        private void txtMatricula_Leave(object sender, EventArgs e)
        {
            if(txtMatricula.Texts == "")
            {
                txtMatricula.Texts = "Matricula";
                txtMatricula.ForeColor = Color.Black;   
            }
        }

        private void txtPassword_Enter(object sender, EventArgs e)
        {
            if (txtPassword.Texts == "Contraseña")
            {
                txtPassword.Texts = "";
                txtPassword.ForeColor = Color.Black;

            }
        }

        private void rjTcorreo_Leave(object sender, EventArgs e)
        {
            if (rjTcorreo.Texts == "")
            {
                rjTcorreo.Texts = "Correo";
                rjTcorreo.ForeColor = Color.Black;    
            }
        }

        private void rjTcorreo_Enter(object sender, EventArgs e)
        {
            if (rjTcorreo.Texts == "Correo")
            {
                rjTcorreo.Texts = "";
                rjTcorreo.ForeColor = Color.Black;

            }
        }
        private void txtPassword_Leave(object sender, EventArgs e)
        {
            if (txtPassword.Texts == "")
            {
                txtPassword.Texts += "Contraseña";
                txtPassword.ForeColor = Color.Black;
            }
        }

        // Evento TextChanged para el nombre
        private void txtNombre__TextChanged(object sender, EventArgs e)
        {
        }


        private void txtApellidoP__TextChanged(object sender, EventArgs e)
        {
        }

        private void txtApellidoM__TextChanged(object sender, EventArgs e)
        {
        }

        private void txtMatricula__TextChanged(object sender, EventArgs e)
        {
        }

        private void txtPassword__TextChanged(object sender, EventArgs e)
        {
        }
        private bool RealizarInsercion()
        {
            string nombre = txtNombre.Texts;
            string apellidoPaterno = txtApellidoP.Texts;
            string apellidoMaterno = txtApellidoM.Texts;
            string matricula = txtMatricula.Texts;
            string contraseña = txtPassword.Texts;
            string correo = rjTcorreo.Texts;

            // El alumno se guarda con su carrera; el grupo ya no se pide.
            OpcionCombo carrera = cbCarrera.SelectedItem as OpcionCombo;
            if (carrera == null)
            {
                MessageBox.Show("Selecciona tu carrera.", "Datos incompletos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // La misma regla que el inicio de sesion: si no, no podria entrar.
            if (!System.Text.RegularExpressions.Regex.IsMatch(matricula.Trim(), "^[A-Za-z0-9]+$"))
            {
                MessageBox.Show("La matrícula solo puede tener letras y números, sin espacios.", "Matrícula no válida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                new AlumnosDatos().Crear(new DatosAlumno
                {
                    Matricula = matricula.Trim(),
                    Nombre = nombre.Trim(),
                    ApellidoPaterno = apellidoPaterno.Trim(),
                    ApellidoMaterno = apellidoMaterno.Trim(),
                    Correo = correo.Trim(),
                    CarreraId = carrera.Id
                }, contraseña);
                return true;
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(ex.Message, "No se pudo registrar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (Exception ex)
            {
                DigBit.Infraestructura.Log.Error("Registro de alumno desde el login", ex);
                MessageBox.Show("No se pudo registrar al alumno: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
        private void btnRegistrar_Click(object sender, EventArgs e)
        {
            try
            {
                // Validar campos obligatorios
                if (string.IsNullOrWhiteSpace(txtNombre.Texts) || txtNombre.Texts == "Nombre" ||
                    string.IsNullOrWhiteSpace(txtApellidoP.Texts) || txtApellidoP.Texts == "Apellido Paterno" ||
                    string.IsNullOrWhiteSpace(txtApellidoM.Texts) || txtApellidoM.Texts == "Apellido Materno" ||
                    string.IsNullOrWhiteSpace(txtMatricula.Texts) || txtMatricula.Texts == "Matricula" ||
                    string.IsNullOrWhiteSpace(rjTcorreo.Texts) || rjTcorreo.Texts == "Correo" ||
                    string.IsNullOrWhiteSpace(txtPassword.Texts))
                {
                    // Mostrar mensaje si algún campo está vacío
                    MessageBox.Show("Todos los campos son obligatorios. Por favor, completa la información.", "Campos Vacíos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return; // Salir del método si algún campo está vacío
                }

                // Realizar el registro
                bool registrado = RealizarInsercion();
                if (registrado)
                {
                    MessageBox.Show("Alumno registrado exitosamente.", "Registro Exitoso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                // Mostrar mensaje de error si hay una excepción
                MessageBox.Show($"Error al intentar registrar el usuario: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void btnCancelar_Click_1(object sender, EventArgs e)
        {
            this.Hide();
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
                cbCarrera.Texts = "Carrera";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar los nombres de las carreras: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Lo engancha el Designer. Antes cargaba los grupos de la carrera; el grupo
        // ya no se pide.
        private void cbCarrera_OnSelectedIndexChanged(object sender, EventArgs e)
        {
        }
    }
}
