using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;

namespace DigBit
{
    /// <summary>
    /// Seccion Alumnos del administrador: una lista con buscador y, sobre el alumno
    /// elegido, editar, restablecer la contrasena y dar de baja o reactivar.
    /// Sustituye a las pantallas viejas de alta, edicion y borrado: ya no hace
    /// falta saberse una matricula de memoria, y quitar a un alumno ya no borra
    /// sus bitacoras.
    /// </summary>
    public class AdministrarAlumnos : PantallaAdmin
    {
        private const string Activos = "Activos";
        private const string DadosDeBaja = "Dados de baja";
        private const string Todos = "Todos";

        private readonly AlumnosDatos datos = new AlumnosDatos();
        private readonly Dictionary<int, string> textoBusqueda = new Dictionary<int, string>();
        private List<AlumnoInfo> alumnos = new List<AlumnoInfo>();

        private readonly TextBox txtBuscar;
        private readonly ComboBox cboCarrera;
        private readonly ComboBox cboMostrar;
        private readonly Button btnEditar;
        private readonly Button btnContrasena;
        private readonly Button btnBaja;
        private bool cargandoFiltros;

        public AdministrarAlumnos()
            : base("Alumnos", "Busca por matrícula, nombre o correo. Doble clic en un alumno para editarlo.", new Size(1180, 700))
        {
            BotonPrincipalArriba("Nuevo alumno", 170).Click += (s, e) => NuevoAlumno();

            txtBuscar = CrearBuscador(new Point(Margen, 112), 330);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Carrera", new Point(380, 88)));
            cboCarrera = EstiloHorarios.CrearCombo(new Point(380, 111), 300);
            cboCarrera.SelectedIndexChanged += (s, e) => { if (!cargandoFiltros) Aplicar(null); };
            Controls.Add(cboCarrera);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Mostrar", new Point(700, 88)));
            cboMostrar = EstiloHorarios.CrearCombo(new Point(700, 111), 150);
            cboMostrar.Items.AddRange(new object[] { Activos, DadosDeBaja, Todos });
            cboMostrar.SelectedIndex = 0;
            cboMostrar.SelectedIndexChanged += (s, e) => { if (!cargandoFiltros) Aplicar(null); };
            Controls.Add(cboMostrar);

            CrearLista(156);
            grid.Columns.Add("matricula", "Matrícula");
            grid.Columns.Add("nombre", "Nombre");
            grid.Columns.Add("carrera", "Carrera");
            grid.Columns.Add("semestre", "Semestre");
            grid.Columns.Add("correo", "Correo");
            grid.Columns.Add("bitacoras", "Bitácoras");
            grid.Columns.Add("ultima", "Última bitácora");
            grid.Columns.Add("estado", "Estado");
            EstiloHorarios.Pesos(grid, 90, 200, 220, 70, 180, 70, 120, 100);
            grid.Columns["bitacoras"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

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

        // =====================================================================
        // Carga y filtros
        // =====================================================================

        protected override void Recargar(string seleccionar)
        {
            try
            {
                alumnos = datos.Listar();
            }
            catch (Exception ex)
            {
                alumnos = new List<AlumnoInfo>();
                MostrarError("Leer la lista de alumnos", ex, "No se pudo leer la lista de alumnos");
            }

            textoBusqueda.Clear();
            foreach (AlumnoInfo alumno in alumnos)
            {
                textoBusqueda[alumno.Id] = EstiloAdmin.Normalizar(alumno.Matricula + " " + alumno.NombreCompleto + " " + alumno.Correo);
            }

            // Sin la migracion 08 no hay bajas: se ve a todos y no se da de baja a nadie.
            cboMostrar.Enabled = datos.BajasDisponibles;
            NotaFija = datos.BajasDisponibles ? "" : "Para dar de baja falta aplicar la migración 08 en la base.";

            RellenarCarreras();
            Aplicar(seleccionar);
        }

        protected override void AlBuscar()
        {
            Aplicar(null);
        }

        private void RellenarCarreras()
        {
            OpcionCombo anterior = cboCarrera.SelectedItem as OpcionCombo;
            cargandoFiltros = true;
            cboCarrera.Items.Clear();
            cboCarrera.Items.Add(new OpcionCombo { Id = 0, Texto = "Todas las carreras" });
            foreach (OpcionCombo carrera in alumnos.Where(a => a.CarreraId.HasValue)
                         .GroupBy(a => a.CarreraId.Value)
                         .Select(g => new OpcionCombo { Id = g.Key, Texto = g.First().Carrera })
                         .OrderBy(o => o.Texto))
            {
                cboCarrera.Items.Add(carrera);
            }

            cboCarrera.SelectedIndex = IndiceDe(cboCarrera, anterior == null ? 0 : anterior.Id);
            cargandoFiltros = false;
        }

        private static int IndiceDe(ComboBox combo, int id)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (((OpcionCombo)combo.Items[i]).Id == id)
                {
                    return i;
                }
            }

            return 0;
        }

        private int CarreraFiltro()
        {
            OpcionCombo opcion = cboCarrera.SelectedItem as OpcionCombo;
            return opcion == null ? 0 : opcion.Id;
        }

        /// <summary>Filtra la lista en memoria y la vuelve a pintar.</summary>
        private void Aplicar(string seleccionarMatricula)
        {
            int carrera = CarreraFiltro();
            string mostrar = datos.BajasDisponibles ? (string)cboMostrar.SelectedItem : Todos;

            List<AlumnoInfo> visibles = alumnos.Where(a =>
                    (mostrar == Todos || (mostrar == Activos) == a.Activo)
                    && (carrera == 0 || a.CarreraId == carrera)
                    && EstiloAdmin.Coincide(textoBusqueda[a.Id], txtBuscar.Text))
                .ToList();

            grid.Columns["estado"].Visible = mostrar != Activos;
            LlenarLista(visibles,
                a => new object[]
                {
                    a.Matricula,
                    a.NombreParaLista,
                    EstiloAdmin.Guion(a.Carrera),
                    EstiloAdmin.Guion(a.Semestre),
                    a.Correo,
                    a.Bitacoras,
                    EstiloAdmin.Fecha(a.UltimaBitacora),
                    a.Activo ? "Activo" : "Baja" + (a.BajaEn.HasValue ? " " + a.BajaEn.Value.ToString("dd/MM/yyyy") : "")
                },
                a => !a.Activo, a => a.Matricula, seleccionarMatricula);

            int bajas = alumnos.Count(a => !a.Activo);
            if (alumnos.Count == 0)
            {
                lblResumen.Text = "Todavía no hay alumnos registrados.";
            }
            else if (visibles.Count == 0)
            {
                lblResumen.Text = "Ningún alumno coincide con la búsqueda.";
            }
            else
            {
                lblResumen.Text = (visibles.Count == alumnos.Count ? "" : visibles.Count + " de ")
                    + alumnos.Count + (alumnos.Count == 1 ? " alumno" : " alumnos")
                    + (bajas > 0 ? " (" + bajas + (bajas == 1 ? " dado de baja)" : " dados de baja)") : "");
            }
        }

        protected override void ActualizarBotones()
        {
            if (btnEditar == null)
            {
                return;
            }

            AlumnoInfo alumno = Seleccionado<AlumnoInfo>();
            btnEditar.Enabled = alumno != null;
            btnContrasena.Enabled = alumno != null && alumno.Activo;
            btnBaja.Enabled = alumno != null && datos.BajasDisponibles;
            btnBaja.Text = alumno != null && !alumno.Activo ? "Reactivar" : "Dar de baja";
        }

        protected override void AlDobleClic()
        {
            EditarSeleccionado();
        }

        // =====================================================================
        // Acciones
        // =====================================================================

        private void NuevoAlumno()
        {
            using (DialogoUsuario dialogo = new DialogoUsuario(datos, null, CarreraFiltro()))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.IdentificadorGuardado);
                    Avisar("Se registró a " + dialogo.NombreGuardado + ".");
                }
            }
        }

        private void EditarSeleccionado()
        {
            AlumnoInfo alumno = Seleccionado<AlumnoInfo>();
            if (alumno == null)
            {
                return;
            }

            using (DialogoUsuario dialogo = new DialogoUsuario(datos, alumno, 0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.IdentificadorGuardado);
                    Avisar("Se guardaron los cambios de " + dialogo.NombreGuardado + ".");
                }
            }
        }

        private void RestablecerContrasenaSeleccionado()
        {
            AlumnoInfo alumno = Seleccionado<AlumnoInfo>();
            if (alumno == null)
            {
                return;
            }

            using (DialogoContrasena dialogo = new DialogoContrasena(alumno.NombreCompleto + " (" + alumno.Matricula + ")"))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    datos.RestablecerContrasena(alumno.Id, dialogo.Contrasena);
                    Avisar("Contraseña nueva para " + alumno.NombreCompleto + ". Ya puede entrar con ella.");
                }
                catch (Exception ex)
                {
                    MostrarError("Restablecer la contrasena de " + alumno.Matricula, ex, "No se pudo cambiar la contraseña");
                }
            }
        }

        private void CambiarEstadoSeleccionado()
        {
            AlumnoInfo alumno = Seleccionado<AlumnoInfo>();
            if (alumno == null)
            {
                return;
            }

            try
            {
                if (alumno.Activo)
                {
                    string bitacoras = alumno.Bitacoras == 0
                        ? "No tiene bitácoras registradas."
                        : "Sus " + (alumno.Bitacoras == 1 ? "bitácora se conserva" : alumno.Bitacoras + " bitácoras se conservan") + " en los informes.";
                    if (!DialogoConfirmacion.Preguntar(this, "Dar de baja",
                        "¿Dar de baja a " + alumno.NombreCompleto + " (" + alumno.Matricula + ")?",
                        "Ya no podrá entrar a DigBit, tampoco en los equipos sin conexión. " + bitacoras
                        + " Puedes reactivar su cuenta cuando quieras.",
                        "Dar de baja"))
                    {
                        return;
                    }

                    datos.DarDeBaja(alumno.Id);
                    Recargar(alumno.Matricula);
                    Avisar("Se dio de baja a " + alumno.NombreCompleto + ". Aparece en «Mostrar: Dados de baja».");
                }
                else
                {
                    // Reactivar no quita nada a nadie: no se pregunta.
                    datos.Reactivar(alumno.Id);
                    Recargar(alumno.Matricula);
                    Avisar("Se reactivó a " + alumno.NombreCompleto + ". Ya puede volver a entrar.");
                }
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MostrarError("Cambiar el estado del alumno " + alumno.Matricula, ex, "No se pudo cambiar el estado del alumno");
            }
        }
    }
}
