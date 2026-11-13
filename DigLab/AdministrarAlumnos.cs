using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;

namespace DigLab
{
    /// <summary>
    /// Seccion Alumnos del administrador: una lista con buscador y, sobre el alumno
    /// elegido, editar y dar de baja o reactivar (su contrasena es la del SIIA: aqui no
    /// se restablece ni se cambia, desde el 2026-11-13).
    /// Sustituye a las pantallas viejas de alta, edicion y borrado: ya no hace
    /// falta saberse una matricula de memoria, y quitar a un alumno ya no borra
    /// sus bitacoras. "Ver sus bitacoras" abre su historial (VentanaHistorialAlumno),
    /// desde donde se exportan sus datos. En solo consulta (el encargado de
    /// laboratorio) solo se busca y se mira: los botones que escriben no se crean.
    /// Con el SIIA no se ensena lo que su inicio de sesion no da (semestre y correo).
    /// Quien reporto fallas sale en naranja, con los tonos de sus bitacoras. Con db/27,
    /// "Suspender acceso..." le impide entrar unos dias (DialogoSuspenderAcceso); quien
    /// esta suspendido sale en rojo, con hasta cuando y, al pasar el raton, la razon.
    /// </summary>
    public class AdministrarAlumnos : PantallaAdmin
    {
        // Lo que se muestra es la cuenta (activa, de baja): sin "activos" ni "dados de baja".
        private const string Activos = "Cuentas activas";
        private const string Suspendidas = "Cuentas suspendidas";
        private const string DadosDeBaja = "Cuentas de baja";
        private const string Todos = "Todas";

        // Lejos del naranja de las fallas y del verde de lo resuelto: la fila suave y su
        // Estado en el fuerte, con el mismo patron.
        private static readonly Color ColorSuspendida = Color.FromArgb(251, 226, 226);
        private static readonly Color ColorSuspendidaCelda = Color.FromArgb(239, 137, 137);

        private readonly AlumnosDatos datos = new AlumnosDatos();
        private readonly Dictionary<int, string> textoBusqueda = new Dictionary<int, string>();
        private List<AlumnoInfo> alumnos = new List<AlumnoInfo>();
        // El encargado de laboratorio: sin "Registrar estudiante", "Editar",
        // "Dar de baja" ni "Datos personales" (no se
        // crean); el doble clic abre sus bitacoras.
        private readonly bool soloConsulta = Permisos.SoloConsulta;

        private readonly TextBox txtBuscar;
        private readonly ComboBox cboCarrera;
        private readonly ComboBox cboMostrar;
        private readonly Button btnEditar;
        private readonly Button btnBitacoras;
        private readonly Button btnBaja;
        private readonly Button btnDatosPersonales;
        private readonly Button btnSuspender;
        private readonly Font negrita = new Font("Century Gothic", 10F, FontStyle.Bold);
        private bool cargandoFiltros;

        // Los estudiantes entran con el SIIA (Ajustes): su cuenta se crea sola al
        // entrar, asi que aqui no se registran (2026-10-31: "no tiene sentido
        // agregar si ya tienen el SIIA"). Sin el SIIA, "Registrar estudiante" vuelve.
        private readonly bool estudiantesConSiia;

        // 1400 de ancho pedidos: a 1366x768 PantallaAdmin la deja en lo que cabe
        // (unos 1350) y la Carrera entera cabe a 10 pt tambien con la columna Estado.
        public AdministrarAlumnos()
            : this(InicioConSiia.LeerSinFallar().Estudiantes)
        {
        }

        private AdministrarAlumnos(bool conSiia)
            : base("Estudiantes", (conSiia ? "Busca por matrícula o nombre." : "Busca por matrícula, nombre o correo.")
                + (Permisos.SoloConsulta
                    ? " Doble clic para ver sus bitácoras. F5 actualiza."
                    : " Doble clic para editar. F5 actualiza."
                      + (conSiia ? " Las cuentas se crean solas cuando el estudiante entra con el SIIA." : "")), new Size(1400, 700))
        {
            estudiantesConSiia = conSiia;
            if (!soloConsulta && !estudiantesConSiia)
            {
                BotonPrincipalArriba("Registrar estudiante", 200).Click += (s, e) => NuevoAlumno();
            }

            txtBuscar = CrearBuscador(new Point(Margen, 112), 290);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Carrera", new Point(340, 86)));
            cboCarrera = EstiloHorarios.CrearCombo(new Point(340, 111), 300);
            cboCarrera.SelectedIndexChanged += (s, e) => { if (!cargandoFiltros) Aplicar(null); };
            Controls.Add(cboCarrera);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Mostrar", new Point(660, 86)));
            cboMostrar = EstiloHorarios.CrearCombo(new Point(660, 111), 170);
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

                // Junto a el, por lo mismo. Sale solo con db/27 (Recargar).
                btnSuspender = EstiloHorarios.CrearBoton("Suspender acceso...", new Point(btnDatosPersonales.Left - 10 - 180, 104), new Size(180, 40));
                btnSuspender.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnSuspender.Visible = false;
                btnSuspender.Click += (s, e) => CambiarSuspensionSeleccionado();
                ayuda.SetToolTip(btnSuspender, "De la persona elegida: que no pueda entrar a DigLab por unos días. Al terminar el plazo vuelve a entrar sola.");
                Controls.Add(btnSuspender);
                // Mas angosta, los botones pisarian el filtro Mostrar. Sin pasarse de la
                // pantalla: a 1280 todavia cabe; PantallaAdmin ya la dejo en lo que cabe.
                MinimumSize = new Size(Math.Min(1280, Width), MinimumSize.Height);
            }

            CrearLista(156);
            grid.Columns.Add("matricula", "Matrícula");
            grid.Columns.Add("nombre", "Nombre");
            grid.Columns.Add("carrera", "Carrera");
            grid.Columns.Add("semestre", "Semestre");
            grid.Columns.Add("correo", "Correo");
            grid.Columns.Add("bitacoras", "Bitácoras");
            grid.Columns.Add("fallas", "Con falla");
            grid.Columns.Add("ultima", "Última bitácora");
            grid.Columns.Add("estado", "Estado");
            // A 10 pt: la Carrera ("Ingenieria en Sistemas Computacionales") entera a
            // 1366 de ancho, tambien con Estado (ColumnasDeLista.TextoEntero, al leer);
            // las cortas, con el peso de su encabezado entero (ListaDigLab no las deja
            // mas angostas que su titulo), y lo demas al nombre y al correo.
            EstiloHorarios.Pesos(grid, 100, 220, 320, 95, 240, 85, 85, 115, 127);
            // Los numeros, a la derecha y con su titulo encima: las columnas son anchas.
            foreach (string numero in new[] { "bitacoras", "fallas" })
            {
                grid.Columns[numero].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                grid.Columns[numero].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }

            grid.Columns["fallas"].ToolTipText = "Cuántas de sus bitácoras reportan una falla o un comentario sin resolver; en verde, con ✓, si ya se resolvió todo. Se leen en «Ver sus bitácoras».";
            // Con el SIIA su cuenta se crea con lo que da su inicio de sesion (matricula,
            // nombre y carrera): semestre y correo estarian siempre vacios.
            grid.Columns["semestre"].Visible = !estudiantesConSiia;
            grid.Columns["correo"].Visible = !estudiantesConSiia;
            grid.CellFormatting += PintarFallas;

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            // Solo consulta: quedan "Ver sus bitacoras" y "Cerrar".
            if (!soloConsulta)
            {
                btnBaja = BotonAbajo("Dar de baja", 150);
                btnBaja.Click += (s, e) => CambiarEstadoSeleccionado();
                // Sin "Restablecer contrasena" (pedido del 2026-11-13: "aqui no le incumbe
                // al admin"): la contrasena de un estudiante es la del SIIA y se cambia alla.
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                negrita.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Quien reporto fallas, con los tonos de sus bitacoras: la fila suave y su
        /// numero en el naranja fuerte, tambien elegida (como el Reporte de su
        /// historial, VentanaHistorialAlumno). Los que no, con su 0 en gris.
        /// </summary>
        private void PintarFallas(object sender, DataGridViewCellFormattingEventArgs e)
        {
            AlumnoInfo alumno = e.RowIndex < 0 ? null : grid.Rows[e.RowIndex].Tag as AlumnoInfo;
            if (alumno == null)
            {
                return;
            }

            string columna = grid.Columns[e.ColumnIndex].Name;
            bool esElNumero = columna == "fallas";
            // Suspendida (db/27): toda la fila en rojo, con su Estado en el fuerte. La
            // celda de sus fallas se queda con su color: dice otra cosa.
            if (alumno.Suspendida)
            {
                if (columna == "estado")
                {
                    // Sin negrita: la columna se mide con la letra normal y se cortaria.
                    e.CellStyle.BackColor = ColorSuspendidaCelda;
                    e.CellStyle.SelectionBackColor = ColorSuspendidaCelda;
                    e.CellStyle.ForeColor = Color.Black;
                    e.CellStyle.SelectionForeColor = Color.Black;
                    return;
                }

                if (!esElNumero || alumno.BitacorasConFalla + alumno.BitacorasResueltas == 0)
                {
                    e.CellStyle.BackColor = ColorSuspendida;
                    if (esElNumero)
                    {
                        e.CellStyle.ForeColor = Color.Gray;
                    }

                    return;
                }
            }

            if (alumno.BitacorasConFalla == 0 && alumno.BitacorasResueltas > 0)
            {
                // Lo que reporto ya se resolvio (db/26): en verde, con la marca de resuelta.
                if (esElNumero)
                {
                    e.Value = "✓ " + alumno.BitacorasResueltas;
                    e.FormattingApplied = true;
                    e.CellStyle.BackColor = VentanaHistorialAlumno.ColorResueltaCelda;
                    e.CellStyle.SelectionBackColor = VentanaHistorialAlumno.ColorResueltaCelda;
                    e.CellStyle.ForeColor = Color.Black;
                    e.CellStyle.SelectionForeColor = Color.Black;
                    e.CellStyle.Font = negrita;
                }
                else
                {
                    e.CellStyle.BackColor = VentanaHistorialAlumno.ColorResuelta;
                }

                return;
            }

            if (alumno.BitacorasConFalla == 0)
            {
                if (esElNumero)
                {
                    e.CellStyle.ForeColor = Color.Gray;
                }

                return;
            }

            if (esElNumero)
            {
                e.CellStyle.BackColor = VentanaHistorialAlumno.ColorFallaCelda;
                e.CellStyle.SelectionBackColor = VentanaHistorialAlumno.ColorFallaCelda;
                // Negra tambien en una cuenta de baja: el gris no se lee sobre este naranja.
                e.CellStyle.ForeColor = Color.Black;
                e.CellStyle.SelectionForeColor = Color.Black;
                e.CellStyle.Font = negrita;
            }
            else
            {
                e.CellStyle.BackColor = VentanaHistorialAlumno.ColorConFalla;
            }
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
                // Con el SIIA el correo no se ve: una fila no sale por algo que no se ve.
                textoBusqueda[alumno.Id] = EstiloAdmin.Normalizar(alumno.Matricula + " " + alumno.NombreCompleto + (estudiantesConSiia ? "" : " " + alumno.Correo));
            }

            // Sin la migracion 08 no hay bajas: se ve a todos y no se da de baja a nadie.
            // En solo consulta no hay "Dar de baja": la nota no le dice nada.
            cboMostrar.Enabled = datos.BajasDisponibles;
            NotaFija = soloConsulta || datos.BajasDisponibles ? "" : "Para dar de baja falta aplicar la migración 08 en la base.";

            // Con db/27: el boton y el filtro. Sin ella, la pantalla queda como antes.
            if (btnSuspender != null)
            {
                btnSuspender.Visible = datos.SuspensionesDisponibles;
            }

            if (datos.SuspensionesDisponibles && !cboMostrar.Items.Contains(Suspendidas))
            {
                object elegido = cboMostrar.SelectedItem;
                cargandoFiltros = true;
                cboMostrar.Items.Insert(1, Suspendidas);
                cboMostrar.SelectedItem = elegido;
                cargandoFiltros = false;
            }

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
                    (mostrar == Todos
                        || (mostrar == Activos && a.Activo)
                        || (mostrar == DadosDeBaja && !a.Activo)
                        || (mostrar == Suspendidas && a.Suspendida))
                    && (carrera == 0 || a.CarreraId == carrera)
                    && EstiloAdmin.Coincide(textoBusqueda[a.Id], txtBuscar.Text))
                .ToList();

            // Una cuenta suspendida sigue siendo activa: el Estado sale si hay alguna a la vista.
            grid.Columns["estado"].Visible = mostrar != Activos || visibles.Any(a => a.Suspendida);
            LlenarLista(visibles,
                a => new object[]
                {
                    a.Matricula,
                    // Como la lista oficial: "Arias Mora Gisela" (igual en todas las pantallas).
                    a.NombreComoEnLaLista,
                    EstiloAdmin.Guion(a.Carrera),
                    EstiloAdmin.Guion(a.Semestre),
                    a.Correo,
                    a.Bitacoras,
                    a.BitacorasConFalla,
                    EstiloAdmin.Fecha(a.UltimaBitacora),
                    a.Suspendida ? EstadoSuspendida(a.SuspendidaHasta.Value) : EstadoDe(a)
                },
                a => !a.Activo, a => a.Matricula, seleccionarMatricula);
            // La carrera entera (a 10 pt y 1366 de ancho se cortaba), y lo de quien esta
            // suspendida; las demas, con lo que quede.
            ColumnasDeLista.TextoEntero(grid, 340, "carrera");
            if (visibles.Any(a => a.Suspendida))
            {
                ColumnasDeLista.TextoEntero(grid, 240, "estado");
            }

            // Hasta cuando y por que, al pasar el raton por cualquier parte de su fila.
            foreach (DataGridViewRow fila in grid.Rows)
            {
                AlumnoInfo alumno = (AlumnoInfo)fila.Tag;
                if (alumno.Suspendida)
                {
                    string motivo = "Acceso suspendido: vuelve a entrar desde el " + SuspensionesDatos.Regreso(alumno.SuspendidaHasta.Value) + "."
                        + Environment.NewLine + "Razón: " + alumno.RazonSuspension;
                    foreach (DataGridViewCell celda in fila.Cells)
                    {
                        celda.ToolTipText = motivo;
                    }
                }
            }

            int bajas = alumnos.Count(a => !a.Activo);
            // Lo mismo, en medio de la lista vacia (no un rectangulo blanco).
            ListaDigLab.PonerTextoVacio(grid, alumnos.Count == 0 ? "Todavía no hay cuentas de estudiantes."
                : "Nadie coincide con la búsqueda. Borra la búsqueda o cambia «Carrera» o «Mostrar».");
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
                int conFallas = visibles.Count(a => a.BitacorasConFalla > 0);
                int yaResueltas = visibles.Count(a => a.BitacorasConFalla == 0 && a.BitacorasResueltas > 0);
                int suspendidas = visibles.Count(a => a.Suspendida);
                lblResumen.Text = (visibles.Count == alumnos.Count ? "" : visibles.Count + " de ")
                    + alumnos.Count + (alumnos.Count == 1 ? " estudiante" : " estudiantes")
                    + (bajas > 0 ? " (" + bajas + " de baja)" : "")
                    + (suspendidas == 0 ? "" : "; en rojo, " + suspendidas + (suspendidas == 1 ? " suspendida" : " suspendidas"))
                    + (conFallas == 0 ? "" : "; en naranja, " + (conFallas == 1 ? "1 reportó fallas" : conFallas + " reportaron fallas"))
                    + (yaResueltas == 0 ? "" : "; en verde, " + yaResueltas + " ya resueltas");
            }
        }

        /// <summary>
        /// "Suspendida, vuelve el 09/11": corto, para que el nombre conserve el ancho. Con
        /// el año solo si no es este; el dia de la semana (o la hora, en las de antes del
        /// 2026-11-13), al pasar el raton.
        /// </summary>
        private static string EstadoSuspendida(DateTime hasta)
        {
            return "Suspendida, vuelve el " + hasta.ToString(hasta.Year == DateTime.Now.Year ? "dd/MM" : "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
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
            btnBaja.Enabled = alumno != null && datos.BajasDisponibles && !anonimizada;
            btnBaja.Text = alumno != null && !alumno.Activo ? "Reactivar" : "Dar de baja";
            btnDatosPersonales.Enabled = alumno != null;
            // Una cuenta de baja ya no entra: no hay nada que suspender.
            btnSuspender.Enabled = alumno != null && alumno.Activo && datos.SuspensionesDisponibles;
            btnSuspender.Text = alumno != null && alumno.Suspendida ? "Quitar suspensión..." : "Suspender acceso...";
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

        /// <summary>
        /// El boton de arriba: suspender a quien este elegido o, si ya esta suspendido,
        /// ver hasta cuando y por que, y quitarle la suspension.
        /// </summary>
        private void CambiarSuspensionSeleccionado()
        {
            if (Permisos.Bloquear(this, "Suspender el acceso de un alumno")) return;

            AlumnoInfo alumno = Seleccionado<AlumnoInfo>();
            if (alumno == null)
            {
                return;
            }

            if (!alumno.Suspendida)
            {
                using (DialogoSuspenderAcceso dialogo = new DialogoSuspenderAcceso(alumno.Id, alumno.NombreCompleto + " (" + alumno.Matricula + ")"))
                {
                    if (dialogo.ShowDialog(this) == DialogResult.OK)
                    {
                        Recargar(alumno.Matricula);
                        Avisar("Acceso suspendido: podrá volver a entrar desde el " + SuspensionesDatos.Regreso(dialogo.Hasta) + ".");
                    }
                }

                return;
            }

            // Aqui se lee la razon entera: este dialogo crece con el texto. Devolverle el
            // acceso no le quita nada: no va en rojo.
            using (DialogoTextoYBotones dialogo = new DialogoTextoYBotones("Quitar suspensión",
                alumno.NombreCompleto + " (" + alumno.Matricula + ") tiene el acceso suspendido: vuelve a entrar desde el "
                + SuspensionesDatos.Regreso(alumno.SuspendidaHasta.Value) + "." + Environment.NewLine + Environment.NewLine
                + "Razón: " + alumno.RazonSuspension + Environment.NewLine + Environment.NewLine
                + "Si se la quitas, podrá volver a entrar de inmediato.",
                "Quitar suspensión", false))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            try
            {
                SuspensionesDatos.Quitar(alumno.Id);
                Recargar(alumno.Matricula);
                Avisar("Se quitó la suspensión. Ya puede volver a entrar.");
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Recargar(alumno.Matricula);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Quitar la suspension del alumno " + alumno.Matricula, ex, "No se pudo quitar la suspensión", Text);
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
                        "Ya no podrá entrar a DigLab, tampoco en los equipos sin conexión. " + bitacoras
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
