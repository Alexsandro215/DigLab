using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;

namespace DigBit
{
    /// <summary>
    /// Seccion Profesores del administrador: lista con buscador, cuantas clases
    /// vigentes tiene cada uno y en que laboratorios, y sobre el elegido editar,
    /// restablecer la contrasena y dar de baja o reactivar. Sustituye al alta y a
    /// la edicion viejas y al "Eliminar profesor", que borraba en cascada sus
    /// clases, sus horarios y las bitacoras de todos sus alumnos.
    /// </summary>
    public class AdministrarProfesores : PantallaAdmin
    {
        private const string Activos = "Activos";
        private const string DadosDeBaja = "Dados de baja";
        private const string Todos = "Todos";

        private readonly ProfesoresDatos datos = new ProfesoresDatos();
        private readonly Dictionary<int, string> textoBusqueda = new Dictionary<int, string>();
        private List<ProfesorInfo> profesores = new List<ProfesorInfo>();

        private readonly TextBox txtBuscar;
        private readonly ComboBox cboMostrar;
        private readonly Button btnEditar;
        private readonly Button btnContrasena;
        private readonly Button btnBaja;

        public AdministrarProfesores()
            : base("Profesores", "Busca por número de empleado, nombre, correo o código. Doble clic en un profesor para editarlo.", new Size(1180, 660))
        {
            BotonPrincipalArriba("Nuevo profesor", 170).Click += (s, e) => NuevoProfesor();

            txtBuscar = CrearBuscador(new Point(Margen, 112), 330);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Mostrar", new Point(380, 88)));
            cboMostrar = EstiloHorarios.CrearCombo(new Point(380, 111), 150);
            cboMostrar.Items.AddRange(new object[] { Activos, DadosDeBaja, Todos });
            cboMostrar.SelectedIndex = 0;
            cboMostrar.SelectedIndexChanged += (s, e) => Aplicar(null);
            Controls.Add(cboMostrar);

            CrearLista(156);
            grid.Columns.Add("numero", "No. de empleado");
            grid.Columns.Add("nombre", "Nombre");
            grid.Columns.Add("codigo", "Código de clase");
            grid.Columns.Add("correo", "Correo");
            grid.Columns.Add("clases", "Clases vigentes");
            grid.Columns.Add("laboratorios", "Laboratorios");
            grid.Columns.Add("sesiones", "Sesiones");
            grid.Columns.Add("ultima", "Última sesión");
            grid.Columns.Add("estado", "Estado");
            EstiloHorarios.Pesos(grid, 100, 190, 90, 170, 90, 200, 70, 100, 100);
            grid.Columns["clases"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns["sesiones"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            btnBaja = BotonAbajo("Dar de baja", 150);
            btnBaja.Click += (s, e) => CambiarEstadoSeleccionado();
            btnContrasena = BotonAbajo("Restablecer contraseña", 210);
            btnContrasena.Click += (s, e) => RestablecerContrasenaSeleccionado();
            btnEditar = BotonAbajo("Editar", 110);
            btnEditar.Click += (s, e) => EditarSeleccionado();

            ActiveControl = txtBuscar;
            ActualizarBotones();
        }

        protected override void Recargar(string seleccionar)
        {
            try
            {
                profesores = datos.Listar();
            }
            catch (Exception ex)
            {
                profesores = new List<ProfesorInfo>();
                MostrarError("Leer la lista de profesores", ex, "No se pudo leer la lista de profesores");
            }

            textoBusqueda.Clear();
            foreach (ProfesorInfo profesor in profesores)
            {
                textoBusqueda[profesor.Id] = EstiloAdmin.Normalizar(profesor.Matricula + " " + profesor.NombreCompleto + " " + profesor.Correo + " " + profesor.Codigo);
            }

            cboMostrar.Enabled = datos.BajasDisponibles;
            grid.Columns["codigo"].Visible = datos.CodigoPorProfesor;
            NotaFija = datos.BajasDisponibles ? "" : "Para dar de baja falta aplicar la migración 08 en la base.";
            Aplicar(seleccionar);
        }

        protected override void AlBuscar()
        {
            Aplicar(null);
        }

        private void Aplicar(string seleccionar)
        {
            string mostrar = datos.BajasDisponibles ? (string)cboMostrar.SelectedItem : Todos;
            List<ProfesorInfo> visibles = profesores.Where(p =>
                    (mostrar == Todos || (mostrar == Activos) == p.Activo)
                    && EstiloAdmin.Coincide(textoBusqueda[p.Id], txtBuscar.Text))
                .ToList();

            grid.Columns["estado"].Visible = mostrar != Activos;
            LlenarLista(visibles,
                p => new object[]
                {
                    p.Matricula,
                    p.NombreParaLista,
                    EstiloAdmin.Guion(p.Codigo),
                    p.Correo,
                    p.ClasesActivas,
                    EstiloAdmin.Guion(p.Laboratorios),
                    p.Sesiones,
                    EstiloAdmin.Fecha(p.UltimaSesion),
                    p.Activo ? "Activo" : "Baja" + (p.BajaEn.HasValue ? " " + p.BajaEn.Value.ToString("dd/MM/yyyy") : "")
                },
                p => !p.Activo, p => p.Matricula, seleccionar);

            int bajas = profesores.Count(p => !p.Activo);
            if (profesores.Count == 0)
            {
                lblResumen.Text = "Todavía no hay profesores registrados.";
            }
            else if (visibles.Count == 0)
            {
                lblResumen.Text = "Ningún profesor coincide con la búsqueda.";
            }
            else
            {
                lblResumen.Text = (visibles.Count == profesores.Count ? "" : visibles.Count + " de ")
                    + profesores.Count + (profesores.Count == 1 ? " profesor" : " profesores")
                    + (bajas > 0 ? " (" + bajas + (bajas == 1 ? " dado de baja)" : " dados de baja)") : "");
            }
        }

        protected override void ActualizarBotones()
        {
            if (btnEditar == null)
            {
                return;
            }

            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            btnEditar.Enabled = profesor != null;
            btnContrasena.Enabled = profesor != null && profesor.Activo;
            btnBaja.Enabled = profesor != null && datos.BajasDisponibles;
            btnBaja.Text = profesor != null && !profesor.Activo ? "Reactivar" : "Dar de baja";
        }

        protected override void AlDobleClic()
        {
            EditarSeleccionado();
        }

        private void NuevoProfesor()
        {
            using (DialogoUsuario dialogo = new DialogoUsuario(datos, null, 0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.IdentificadorGuardado);
                    Avisar("Se registró a " + dialogo.NombreGuardado
                        + (string.IsNullOrEmpty(dialogo.CodigoGuardado) ? "" : ", con el código " + dialogo.CodigoGuardado)
                        + ". Sus clases se crean en Horarios.");
                }
            }
        }

        private void EditarSeleccionado()
        {
            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            using (DialogoUsuario dialogo = new DialogoUsuario(datos, profesor, 0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.IdentificadorGuardado);
                    bool cambioCodigo = !string.IsNullOrEmpty(dialogo.CodigoGuardado) && dialogo.CodigoGuardado != profesor.Codigo;
                    Avisar("Se guardaron los cambios de " + dialogo.NombreGuardado
                        + (cambioCodigo ? ". Su código ahora es " + dialogo.CodigoGuardado + ": avísale." : "."));
                }
            }
        }

        private void RestablecerContrasenaSeleccionado()
        {
            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            using (DialogoContrasena dialogo = new DialogoContrasena(profesor.NombreCompleto + " (" + profesor.Matricula + ")"))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    datos.RestablecerContrasena(profesor.Id, dialogo.Contrasena);
                    Avisar("Contraseña nueva para " + profesor.NombreCompleto + ". Ya puede entrar con ella.");
                }
                catch (Exception ex)
                {
                    MostrarError("Restablecer la contrasena de " + profesor.Matricula, ex, "No se pudo cambiar la contraseña");
                }
            }
        }

        private void CambiarEstadoSeleccionado()
        {
            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            try
            {
                if (!profesor.Activo)
                {
                    // Reactivar no quita nada a nadie: no se pregunta. Sus clases, si
                    // se desactivaron al darlo de baja, se reactivan en Horarios.
                    datos.Reactivar(profesor.Id);
                    Recargar(profesor.Matricula);
                    Avisar("Se reactivó a " + profesor.NombreCompleto + ". Si tenía clases desactivadas, actívalas en Horarios.");
                    return;
                }

                string detalle = "Ya no podrá entrar a DigBit. Sus sesiones y las bitácoras de sus alumnos se conservan en los informes, y puedes reactivar su cuenta cuando quieras.";
                string casilla = null;
                if (profesor.ClasesActivas > 0)
                {
                    detalle = "Ya no podrá entrar a DigBit. Tiene " + profesor.ClasesActivas
                        + (profesor.ClasesActivas == 1 ? " clase vigente" : " clases vigentes")
                        + (string.IsNullOrEmpty(profesor.Laboratorios) ? "" : " en " + profesor.Laboratorios)
                        + ". Si un suplente sigue con el grupo, créale una clase nueva en Horarios. Sus bitácoras se conservan.";
                    casilla = "Desactivar también " + (profesor.ClasesActivas == 1 ? "su clase" : "sus " + profesor.ClasesActivas + " clases")
                        + " (su código dejará de aceptarse en los laboratorios)";
                }

                bool desactivarClases;
                if (!DialogoConfirmacion.Preguntar(this, "Dar de baja",
                    "¿Dar de baja a " + profesor.NombreCompleto + " (" + profesor.Matricula + ")?",
                    detalle, "Dar de baja", true, casilla, true, out desactivarClases))
                {
                    return;
                }

                int desactivadas = datos.DarDeBaja(profesor.Id, desactivarClases);
                Recargar(profesor.Matricula);
                Avisar("Se dio de baja a " + profesor.NombreCompleto
                    + (desactivadas > 0 ? " y se desactivaron " + desactivadas + (desactivadas == 1 ? " clase." : " clases.") : "."));
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MostrarError("Cambiar el estado del profesor " + profesor.Matricula, ex, "No se pudo cambiar el estado del profesor");
            }
        }
    }
}
