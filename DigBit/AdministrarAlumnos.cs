using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;

namespace DigBit
{
    /// <summary>
    /// Seccion Alumnos del administrador: una lista con buscador y, sobre el alumno
    /// elegido, editar, restablecer la contrasena y dar de baja o reactivar.
    /// Sustituye a las pantallas viejas de alta, edicion y borrado: ya no hace
    /// falta saberse una matricula de memoria, y quitar a un alumno ya no borra
    /// sus bitacoras. "Ver sus bitacoras" abre su historial (VentanaHistorialAlumno),
    /// desde donde se exportan sus datos. En solo consulta (el encargado de
    /// laboratorio) solo se busca y se mira: los botones que escriben no se crean.
    /// </summary>
    public class AdministrarAlumnos : PantallaAdmin
    {
        // Lo que se muestra es la cuenta (activa, de baja): sin "activos" ni "dados de baja".
        private const string Activos = "Cuentas activas";
        private const string DadosDeBaja = "Cuentas de baja";
        private const string Todos = "Todas";

        private readonly AlumnosDatos datos = new AlumnosDatos();
        private readonly Dictionary<int, string> textoBusqueda = new Dictionary<int, string>();
        private List<AlumnoInfo> alumnos = new List<AlumnoInfo>();
        // El encargado de laboratorio: sin "Registrar estudiante", "Editar",
        // "Restablecer contrasena", "Dar de baja" ni "Datos personales" (no se
        // crean); el doble clic abre sus bitacoras.
        private readonly bool soloConsulta = Permisos.SoloConsulta;

        private readonly TextBox txtBuscar;
        private readonly ComboBox cboCarrera;
        private readonly ComboBox cboMostrar;
        private readonly Button btnEditar;
        private readonly Button btnBitacoras;
        private readonly Button btnContrasena;
        private readonly Button btnBaja;
        private readonly Button btnDatosPersonales;
        private bool cargandoFiltros;

        // 1400 de ancho pedidos: a 1366x768 PantallaAdmin la deja en lo que cabe
        // (unos 1350) y la Carrera entera cabe a 10 pt tambien con la columna Estado.
        public AdministrarAlumnos()
            : base("Estudiantes", Permisos.SoloConsulta
                ? "Busca por matrícula, nombre o correo. Doble clic para ver sus bitácoras. F5 actualiza."
                : "Busca por matrícula, nombre o correo. Doble clic para editar. F5 actualiza.", new Size(1400, 700))
        {
            if (!soloConsulta)
            {
                BotonPrincipalArriba("Registrar estudiante", 200).Click += (s, e) => NuevoAlumno();
            }

            txtBuscar = CrearBuscador(new Point(Margen, 112), 330);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Carrera", new Point(380, 88)));
            cboCarrera = EstiloHorarios.CrearCombo(new Point(380, 111), 300);
            cboCarrera.SelectedIndexChanged += (s, e) => { if (!cargandoFiltros) Aplicar(null); };
            Controls.Add(cboCarrera);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Mostrar", new Point(700, 88)));
            cboMostrar = EstiloHorarios.CrearCombo(new Point(700, 111), 170);
            cboMostrar.Items.AddRange(new object[] { Activos, DadosDeBaja, Todos });
            cboMostrar.SelectedIndex = 0;
            cboMostrar.SelectedIndexChanged += (s, e) => { if (!cargandoFiltros) Aplicar(null); };
            Controls.Add(cboMostrar);

            if (!soloConsulta)
            {
                // Derechos ARCO de quien este elegido: entregarle sus datos o, con la
                // cuenta de baja, anonimizarla (DerechosArco). Arriba, junto a los
                // filtros: abajo ya no caben mas botones con las lineas de estado.
                btnDatosPersonales = EstiloHorarios.CrearBoton("Datos personales...", new Point(ClientSize.Width - Margen - 200, 104), new Size(200, 40));
                btnDatosPersonales.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnDatosPersonales.Click += (s, e) => DatosPersonales();
                ayuda.SetToolTip(btnDatosPersonales, "De la persona elegida: exportar sus datos o, si su cuenta está de baja, anonimizarla (derechos ARCO).");
                Controls.Add(btnDatosPersonales);
                // Mas angosta, el boton pisaria el filtro Mostrar.
                MinimumSize = new Size(1120, MinimumSize.Height);
            }

            CrearLista(156);
            grid.Columns.Add("matricula", "Matrícula");
            grid.Columns.Add("nombre", "Nombre");
            grid.Columns.Add("carrera", "Carrera");
            grid.Columns.Add("semestre", "Semestre");
            grid.Columns.Add("correo", "Correo");
            grid.Columns.Add("bitacoras", "Bitácoras");
            grid.Columns.Add("ultima", "Última bitácora");
            grid.Columns.Add("estado", "Estado");
            // A 10 pt: la Carrera ("Ingenieria en Sistemas Computacionales") entera a
            // 1366 de ancho, tambien con Estado (ColumnasDeLista.TextoEntero, al leer);
            // las cortas, con el peso de su encabezado entero, y lo demas al nombre y al correo.
            EstiloHorarios.Pesos(grid, 100, 220, 320, 95, 240, 95, 95, 127);
            ColumnasDeLista.EncabezadoEntero(grid, "matricula", "semestre", "bitacoras", "estado");
            grid.Columns["bitacoras"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            // Solo consulta: quedan "Ver sus bitacoras" y "Cerrar".
            if (!soloConsulta)
            {
                btnBaja = BotonAbajo("Dar de baja", 150);
                btnBaja.Click += (s, e) => CambiarEstadoSeleccionado();
                btnContrasena = BotonAbajo("Restablecer contraseña", 210);
                btnContrasena.Click += (s, e) => RestablecerContrasenaSeleccionado();
            }

            // Su historial: cada bitacora que registro, con "Ver la sesion" y la
            // exportacion de sus datos (eso, solo el administrador).
            btnBitacoras = BotonAbajo("Ver sus bitácoras", 170);
            btnBitacoras.Click += (s, e) => VerBitacorasSeleccionado();
            ayuda.SetToolTip(btnBitacoras, "Cada bitácora que registró, con su clase, el equipo y lo que reportó.");
            if (!soloConsulta)
            {
                btnEditar = BotonAbajo("Editar", 110);
                btnEditar.Click += (s, e) => EditarSeleccionado();
            }

            ActiveControl = txtBuscar;
            ActualizarBotones();
        }

        /// <summary>F5: vuelve a leer la lista, como en Bitacoras (la fila elegida se conserva).</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    Recargar(null);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
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
                AvisoDeFalloAdmin.Mostrar(this, "Leer la lista de alumnos", ex, "No se pudo leer la lista de estudiantes", Text);
            }

            textoBusqueda.Clear();
            foreach (AlumnoInfo alumno in alumnos)
            {
                textoBusqueda[alumno.Id] = EstiloAdmin.Normalizar(alumno.Matricula + " " + alumno.NombreCompleto + " " + alumno.Correo);
            }

            // Sin la migracion 08 no hay bajas: se ve a todos y no se da de baja a nadie.
            // En solo consulta no hay "Dar de baja": la nota no le dice nada.
            cboMostrar.Enabled = datos.BajasDisponibles;
            NotaFija = soloConsulta || datos.BajasDisponibles ? "" : "Para dar de baja falta aplicar la migración 08 en la base.";

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
                    EstadoDe(a)
                },
                a => !a.Activo, a => a.Matricula, seleccionarMatricula);
            // La carrera entera (a 10 pt y 1366 de ancho se cortaba); las demas, con lo que quede.
            ColumnasDeLista.TextoEntero(grid, 340, "carrera");

            int bajas = alumnos.Count(a => !a.Activo);
            if (alumnos.Count == 0)
            {
                lblResumen.Text = "Todavía no hay cuentas de estudiantes.";
            }
            else if (visibles.Count == 0)
            {
                lblResumen.Text = "Nadie coincide con la búsqueda.";
            }
            else
            {
                lblResumen.Text = (visibles.Count == alumnos.Count ? "" : visibles.Count + " de ")
                    + alumnos.Count + (alumnos.Count == 1 ? " estudiante" : " estudiantes")
                    + (bajas > 0 ? " (" + bajas + " de baja)" : "");
            }
        }

        /// <summary>La columna Estado: la cuenta activa, de baja (con su fecha) o anonimizada.</summary>
        internal static string EstadoDe(UsuarioInfo persona)
        {
            return persona.Anonimizada ? "Anonimizada"
                : persona.Activo ? "Activa"
                : "Baja" + (persona.BajaEn.HasValue ? " " + persona.BajaEn.Value.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : "");
        }

        protected override void ActualizarBotones()
        {
            // Antes de crear los botones (la lista va primero) no hay nada que hacer.
            // "Ver sus bitacoras" esta siempre; los demas, solo si no es solo consulta.
            if (btnBitacoras == null)
            {
                return;
            }

            AlumnoInfo alumno = Seleccionado<AlumnoInfo>();
            btnBitacoras.Enabled = alumno != null;
            if (btnEditar == null)
            {
                return;
            }

            // Una cuenta anonimizada ya no se edita ni se reactiva (UsuariosDatos.Anonimizar).
            bool anonimizada = alumno != null && alumno.Anonimizada;
            btnEditar.Enabled = alumno != null && !anonimizada;
            btnContrasena.Enabled = alumno != null && alumno.Activo;
            btnBaja.Enabled = alumno != null && datos.BajasDisponibles && !anonimizada;
            btnBaja.Text = alumno != null && !alumno.Activo ? "Reactivar" : "Dar de baja";
            btnDatosPersonales.Enabled = alumno != null;
        }

        protected override void AlDobleClic()
        {
            // En solo consulta no se edita: el doble clic (y Enter) abre sus bitacoras.
            if (soloConsulta)
            {
                VerBitacorasSeleccionado();
                return;
            }

            EditarSeleccionado();
        }

        // =====================================================================
        // Acciones
        // =====================================================================

        private void NuevoAlumno()
        {
            if (Permisos.Bloquear(this, "Dar de alta un alumno")) return;

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
            if (Permisos.Bloquear(this, "Editar un alumno")) return;

            AlumnoInfo alumno = Seleccionado<AlumnoInfo>();
            if (alumno == null)
            {
                return;
            }

            if (alumno.Anonimizada)
            {
                // Tambien con el doble clic: la cuenta ya no tiene datos que editar.
                // El mismo aviso corto que en Docentes, donde la linea es mas angosta.
                Avisar("Cuenta anonimizada: ya no se puede editar.");
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

        /// <summary>
        /// El historial del elegido: sus bitacoras, "Ver la sesion" y "Exportar sus
        /// datos". Solo lee: tambien en solo consulta (la lista no cambia; F5 la
        /// vuelve a leer).
        /// </summary>
        private void VerBitacorasSeleccionado()
        {
            AlumnoInfo alumno = Seleccionado<AlumnoInfo>();
            if (alumno == null)
            {
                return;
            }

            using (VentanaHistorialAlumno historial = new VentanaHistorialAlumno(alumno))
            {
                historial.ShowDialog(this);
            }
        }

        /// <summary>
        /// "Datos personales...": debajo del boton, exportar los datos de quien este
        /// elegido o, si su cuenta esta de baja, anonimizarla (DerechosArco). Los
        /// avisos, cortos: la linea de estado mide unos 460 px a 1366 (la ruta del
        /// archivo sale en un mensaje).
        /// </summary>
        private void DatosPersonales()
        {
            AlumnoInfo alumno = Seleccionado<AlumnoInfo>();
            if (alumno == null)
            {
                return;
            }

            DerechosArco.MostrarMenu(btnDatosPersonales, alumno, () =>
            {
                string ruta = DerechosArco.ExportarEstudiante(this, alumno, null);
                if (ruta != null)
                {
                    Avisar("Se exportaron sus datos.");
                    DerechosArco.AvisarExportado(this, ruta);
                }
            }, () =>
            {
                if (DerechosArco.Anonimizar(this, datos, alumno, false))
                {
                    // Buscandola por su identificador nuevo: con la busqueda de antes (su
                    // nombre o su matricula) ya no saldria.
                    string anonimo = UsuariosDatos.IdentificadorAnonimo(alumno.Id);
                    txtBuscar.Text = anonimo;
                    Recargar(anonimo);
                    Avisar("Cuenta anonimizada: ahora es " + anonimo + ".");
                }
            });
        }

        private void RestablecerContrasenaSeleccionado()
        {
            if (Permisos.Bloquear(this, "Restablecer la contrasena de un alumno")) return;

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
                    AvisoDeFalloAdmin.Mostrar(this, "Restablecer la contrasena de " + alumno.Matricula, ex, "No se pudo cambiar la contraseña", Text);
                }
            }
        }

        private void CambiarEstadoSeleccionado()
        {
            if (Permisos.Bloquear(this, "Dar de baja o reactivar un alumno")) return;

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
                    Avisar("Se dio de baja a " + alumno.NombreCompleto + ". Aparece en «Mostrar: " + DadosDeBaja + "».");
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
                AvisoDeFalloAdmin.Mostrar(this, "Cambiar el estado del alumno " + alumno.Matricula, ex, "No se pudo cambiar el estado de la cuenta", Text);
            }
        }
    }
}
