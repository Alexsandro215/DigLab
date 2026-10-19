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
    /// Seccion Profesores del administrador: lista con buscador, cuantas clases
    /// vigentes tiene cada uno y en que laboratorios, y sobre el elegido editar,
    /// restablecer la contrasena y dar de baja o reactivar. Sustituye al alta y a
    /// la edicion viejas y al "Eliminar profesor", que borraba en cascada sus
    /// clases, sus horarios y las bitacoras de todos sus alumnos. De aqui se abren
    /// tambien los encargados de laboratorio (AdministrarEncargados). "Ver sus
    /// bitacoras" abre Bitacoras ya buscando su nombre y su numero de empleado.
    /// En solo consulta (el encargado) solo se busca y se ven el horario y las
    /// bitacoras de cada uno.
    /// </summary>
    public class AdministrarProfesores : PantallaAdmin
    {
        // Lo que se muestra es la cuenta (activa, de baja): sin "activos" ni "dados de baja".
        private const string Activos = "Cuentas activas";
        private const string DadosDeBaja = "Cuentas de baja";
        private const string Todos = "Todas";

        private readonly ProfesoresDatos datos = new ProfesoresDatos();
        private readonly Dictionary<int, string> textoBusqueda = new Dictionary<int, string>();
        private List<ProfesorInfo> profesores = new List<ProfesorInfo>();
        // El encargado de laboratorio: los botones que escriben no se crean (quedan
        // "Ver horario" y "Cerrar") y el doble clic abre el horario.
        private readonly bool soloConsulta = Permisos.SoloConsulta;

        private readonly TextBox txtBuscar;
        private readonly ComboBox cboMostrar;
        private readonly Button btnEditar;
        private readonly Button btnHorario;
        private readonly Button btnBitacoras;
        private readonly Button btnContrasena;
        private readonly Button btnBaja;
        private readonly Button btnDatosPersonales;

        /// <summary>Ver las bitacoras de un profesor: Bitacoras ya buscando su nombre (la abre el panel principal).</summary>
        public event Action<string> VerBitacoras;

        // 1400 de ancho pedidos: a 1366x768 PantallaAdmin la deja en lo que cabe
        // (unos 1350) y el Correo entero cabe a 10 pt tambien con la columna Estado.
        public AdministrarProfesores()
            : base("Docentes", Subtitulo(), new Size(1400, 660))
        {
            if (!soloConsulta)
            {
                BotonPrincipalArriba("Registrar docente", 180).Click += (s, e) => NuevoProfesor();
            }

            txtBuscar = CrearBuscador(new Point(Margen, 112), 330);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Mostrar", new Point(380, 88)));
            cboMostrar = EstiloHorarios.CrearCombo(new Point(380, 111), 170);
            cboMostrar.Items.AddRange(new object[] { Activos, DadosDeBaja, Todos });
            cboMostrar.SelectedIndex = 0;
            cboMostrar.SelectedIndexChanged += (s, e) => Aplicar(null);
            Controls.Add(cboMostrar);

            if (!soloConsulta)
            {
                // Los encargados de laboratorio (solo consulta) son personal con numero
                // de empleado, como los profesores: se administran en su propia ventana.
                // A la derecha de los filtros, no junto a "Registrar docente": ahi quedaria
                // pegado al subtitulo y lo taparia en cuanto se estrechara la ventana.
                Button btnEncargados = EstiloHorarios.CrearBoton("Responsables de laboratorio...",
                    new Point(ClientSize.Width - Margen - 260, 104), new Size(260, 40));
                btnEncargados.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnEncargados.Click += (s, e) => AbrirEncargados();
                ayuda.SetToolTip(btnEncargados, "Quienes ven el panel, las alertas, las bitácoras y los horarios sin poder cambiar nada.");
                Controls.Add(btnEncargados);

                // Derechos ARCO de quien este elegido (DerechosArco), a su izquierda:
                // abajo ya no caben mas botones con las lineas de estado.
                btnDatosPersonales = EstiloHorarios.CrearBoton("Datos personales...",
                    new Point(btnEncargados.Left - 10 - 200, 104), new Size(200, 40));
                btnDatosPersonales.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnDatosPersonales.Click += (s, e) => DatosPersonales();
                ayuda.SetToolTip(btnDatosPersonales, "De la persona elegida: exportar sus datos o, si su cuenta está de baja, anonimizarla (derechos ARCO).");
                Controls.Add(btnDatosPersonales);
            }

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
            // A 10 pt: el Correo entero a 1366 de ancho, tambien con Estado
            // (ColumnasDeLista.TextoEntero, al leer); las cortas, con el peso de su
            // encabezado entero, y lo demas al nombre y a los laboratorios.
            EstiloHorarios.Pesos(grid, 105, 220, 90, 255, 80, 230, 95, 100, 127);
            ColumnasDeLista.EncabezadoEntero(grid, "sesiones", "estado");
            grid.Columns["clases"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns["sesiones"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            // Con su codigo entran sus alumnos: quien no ve los codigos de los
            // profesores (el encargado) no tiene la columna, ni antes de leer la lista.
            grid.Columns["codigo"].Visible = Permisos.VeCodigosDeProfesor;

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            if (!soloConsulta)
            {
                btnBaja = BotonAbajo("Dar de baja", 150);
                btnBaja.Click += (s, e) => CambiarEstadoSeleccionado();
                btnContrasena = BotonAbajo("Restablecer contraseña", 210);
                btnContrasena.Click += (s, e) => RestablecerContrasenaSeleccionado();
            }

            // Tambien en solo consulta: el encargado ve Bitacoras.
            btnBitacoras = BotonAbajo("Ver sus bitácoras", 170);
            btnBitacoras.Click += (s, e) => VerBitacorasSeleccionado();
            ayuda.SetToolTip(btnBitacoras, "Abre Bitácoras con sus sesiones (busca su nombre y su número de empleado).");
            btnHorario = BotonAbajo("Ver horario", 130);
            btnHorario.Click += (s, e) => VerHorarioSeleccionado();
            if (!soloConsulta)
            {
                btnEditar = BotonAbajo("Editar", 110);
                btnEditar.Click += (s, e) => EditarSeleccionado();
                // Con siete botones abajo, mas angosta que esto (PantallaAdmin deja
                // llegar a 1000) los de la izquierda pisarian las lineas de estado.
                MinimumSize = new Size(1200, MinimumSize.Height);
            }

            ActiveControl = txtBuscar;
            ActualizarBotones();

            // Otro reparto (el panel cada 30 s, Horarios) puede cambiar colores: la
            // columna de nombres se vuelve a pintar.
            ColoresProfesor.Cambio += ColoresCambiaron;
            FormClosed += (s, e) => ColoresProfesor.Cambio -= ColoresCambiaron;
        }

        /// <summary>
        /// Sin "codigo" si no se ven los codigos de los profesores (no se buscan), y
        /// el doble clic edita o, en solo consulta, abre su horario.
        /// </summary>
        private static string Subtitulo()
        {
            return (Permisos.VeCodigosDeProfesor
                    ? "Busca por número de empleado, nombre, correo o código."
                    : "Busca por número de empleado, nombre o correo.")
                + (Permisos.SoloConsulta
                    ? " Doble clic para ver su horario."
                    : " Doble clic para editar.")
                + " F5 actualiza.";
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

        private void ColoresCambiaron()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(ColoresCambiaron));
                return;
            }

            PintarColores();
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
                AvisoDeFalloAdmin.Mostrar(this, "Leer la lista de profesores", ex, "No se pudo leer la lista de docentes", Text);
            }

            // El color de cada uno, el mismo que en Horarios: la lista lo ensena y
            // el dialogo dice que profesor usa ya cada color.
            datos.RepartirColores();

            // Sin los codigos de los profesores (el encargado), el codigo ni se ve ni se busca.
            bool conCodigo = Permisos.VeCodigosDeProfesor;
            textoBusqueda.Clear();
            foreach (ProfesorInfo profesor in profesores)
            {
                textoBusqueda[profesor.Id] = EstiloAdmin.Normalizar(profesor.Matricula + " " + profesor.NombreCompleto + " " + profesor.Correo
                    + (conCodigo ? " " + profesor.Codigo : ""));
            }

            cboMostrar.Enabled = datos.BajasDisponibles;
            grid.Columns["codigo"].Visible = datos.CodigoPorProfesor && conCodigo;
            // En solo consulta no hay "Dar de baja": la nota no le dice nada.
            NotaFija = soloConsulta || datos.BajasDisponibles ? "" : "Para dar de baja falta aplicar la migración 08 en la base.";
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
                    Permisos.VeCodigosDeProfesor ? EstiloAdmin.Guion(p.Codigo) : "",
                    p.Correo,
                    p.ClasesActivas,
                    EstiloAdmin.Guion(p.Laboratorios),
                    p.Sesiones,
                    EstiloAdmin.Fecha(p.UltimaSesion),
                    AdministrarAlumnos.EstadoDe(p)
                },
                p => !p.Activo, p => p.Matricula, seleccionar);
            // El correo entero (a 10 pt y 1366 de ancho se cortaba); las demas, con lo que quede.
            ColumnasDeLista.TextoEntero(grid, 330, "correo");

            PintarColores();

            int bajas = profesores.Count(p => !p.Activo);
            if (profesores.Count == 0)
            {
                lblResumen.Text = "Todavía no hay cuentas docentes.";
            }
            else if (visibles.Count == 0)
            {
                lblResumen.Text = "Nadie coincide con la búsqueda.";
            }
            else
            {
                lblResumen.Text = (visibles.Count == profesores.Count ? "" : visibles.Count + " de ")
                    + profesores.Count + (profesores.Count == 1 ? " docente" : " docentes")
                    + (bajas > 0 ? " (" + bajas + " de baja)" : "");
            }
        }

        /// <summary>
        /// El nombre con su color, como la columna Profesor de Clases. Sin color
        /// todavia (sin clases ni color elegido), en blanco: el que se veria seria
        /// provisional.
        /// </summary>
        private void PintarColores()
        {
            foreach (DataGridViewRow fila in grid.Rows)
            {
                ProfesorInfo profesor = (ProfesorInfo)fila.Tag;
                DataGridViewCell nombre = fila.Cells["nombre"];
                if (!ColoresProfesor.TieneColor(profesor.Id))
                {
                    nombre.Style.BackColor = Color.Empty;
                    nombre.Style.SelectionBackColor = Color.Empty;
                    nombre.Style.SelectionForeColor = Color.Empty;
                    continue;
                }

                Color color = ColoresProfesor.De(profesor.Id);
                nombre.Style.BackColor = color;
                nombre.Style.SelectionBackColor = EstiloHorarios.Oscurecer(color, 0.18f);
                nombre.Style.SelectionForeColor = Color.Black;
            }
        }

        /// <summary>El color que tiene hoy cada profesor de la lista, para ver despues a quien le cambio.</summary>
        private Dictionary<int, int> ColoresDeAhora()
        {
            return profesores.Where(p => ColoresProfesor.TieneColor(p.Id)).ToDictionary(p => p.Id, p => ColoresProfesor.De(p.Id).ToArgb());
        }

        /// <summary>" También cambió el color de Ana Ruiz y Beto Pérez." o "".</summary>
        private string OtrosQueCambiaron(Dictionary<int, int> antes, string salvoMatricula)
        {
            List<string> cambiaron = profesores
                .Where(p => p.Matricula != salvoMatricula && antes.ContainsKey(p.Id) && ColoresProfesor.TieneColor(p.Id)
                    && ColoresProfesor.De(p.Id).ToArgb() != antes[p.Id])
                .Select(p => HorariosDatos.NombreCorto(p.Nombre, p.ApellidoPaterno))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return cambiaron.Count == 0 ? "" : " También cambió el color de " + EstiloAdmin.Enumerar(cambiaron) + ".";
        }

        protected override void ActualizarBotones()
        {
            // Antes de crear los botones (la lista va primero) no hay nada que hacer.
            // "Ver horario" esta siempre; los demas, solo si no es solo consulta.
            if (btnHorario == null)
            {
                return;
            }

            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            // Una cuenta anonimizada ya no se edita ni se reactiva (UsuariosDatos.Anonimizar).
            bool anonimizada = profesor != null && profesor.Anonimizada;
            btnHorario.Enabled = profesor != null;
            btnBitacoras.Enabled = profesor != null;
            if (btnEditar != null)
            {
                btnEditar.Enabled = profesor != null && !anonimizada;
            }

            if (btnContrasena != null)
            {
                btnContrasena.Enabled = profesor != null && profesor.Activo;
            }

            if (btnBaja != null)
            {
                btnBaja.Enabled = profesor != null && datos.BajasDisponibles && !anonimizada;
                btnBaja.Text = profesor != null && !profesor.Activo ? "Reactivar" : "Dar de baja";
            }

            if (btnDatosPersonales != null)
            {
                btnDatosPersonales.Enabled = profesor != null;
            }
        }

        protected override void AlDobleClic()
        {
            // En solo consulta no se edita: el doble clic (y Enter) abre su horario.
            if (soloConsulta)
            {
                VerHorarioSeleccionado();
                return;
            }

            EditarSeleccionado();
        }

        /// <summary>
        /// Los encargados de laboratorio (ven el panel en solo consulta), en su
        /// propia ventana: alta, edicion, contrasena y baja. Solo el administrador.
        /// </summary>
        private void AbrirEncargados()
        {
            if (Permisos.Bloquear(this, "Abrir los encargados de laboratorio")) return;

            using (AdministrarEncargados encargados = new AdministrarEncargados())
            {
                encargados.ShowDialog(this);
            }
        }

        private void NuevoProfesor()
        {
            if (Permisos.Bloquear(this, "Dar de alta un profesor")) return;

            using (DialogoUsuario dialogo = new DialogoUsuario(datos, null, 0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Dictionary<int, int> antes = ColoresDeAhora();
                    Recargar(dialogo.IdentificadorGuardado);
                    Avisar("Se registró a " + dialogo.NombreGuardado
                        + (string.IsNullOrEmpty(dialogo.CodigoGuardado) ? "" : ", con el código " + dialogo.CodigoGuardado)
                        + ". Sus clases se crean en Horarios." + OtrosQueCambiaron(antes, dialogo.IdentificadorGuardado));
                }
            }
        }

        private void EditarSeleccionado()
        {
            if (Permisos.Bloquear(this, "Editar un profesor")) return;

            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            if (profesor.Anonimizada)
            {
                // Tambien con el doble clic: la cuenta ya no tiene datos que editar.
                // Corto: a 1366 la linea de estado de Docentes mide unos 320 px.
                Avisar("Cuenta anonimizada: ya no se puede editar.");
                return;
            }

            using (DialogoUsuario dialogo = new DialogoUsuario(datos, profesor, 0))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Dictionary<int, int> antes = ColoresDeAhora();
                    Recargar(dialogo.IdentificadorGuardado);
                    bool cambioCodigo = !string.IsNullOrEmpty(dialogo.CodigoGuardado) && dialogo.CodigoGuardado != profesor.Codigo;
                    Avisar("Se guardaron los cambios de " + dialogo.NombreGuardado
                        + (cambioCodigo ? ". Su código ahora es " + dialogo.CodigoGuardado + ": avísale." : ".")
                        + (dialogo.ColorCambiado ? " Su color nuevo se ve en Horarios y en el panel." : "")
                        + OtrosQueCambiaron(antes, dialogo.IdentificadorGuardado));
                }
            }
        }

        /// <summary>Su horario semanal en todos los laboratorios, solo para verlo.</summary>
        private void VerHorarioSeleccionado()
        {
            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            using (BitacoraMisClases horario = new BitacoraMisClases(profesor.Id, profesor.NombreCompleto))
            {
                horario.ShowDialog(this);
            }
        }

        /// <summary>
        /// Bitacoras con las sesiones del elegido: busca su nombre y su numero de
        /// empleado (todas sus palabras, en cualquier orden), sin otros filtros. Con
        /// el nombre solo tambien saldria otro: la busqueda encuentra cada palabra
        /// dentro de otras ("Ana Ruiz" esta en "Adriana Ruiz"). Solo lee.
        /// </summary>
        private void VerBitacorasSeleccionado()
        {
            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            string texto = ((profesor.NombreCompleto ?? "") + " " + (profesor.Matricula ?? "")).Trim();
            if (VerBitacoras != null)
            {
                VerBitacoras(texto);
                return;
            }

            // Sin el panel principal (no pasa hoy): una ventana suya.
            GestionBitacorasAdministrador bitacoras = new GestionBitacorasAdministrador();
            bitacoras.Buscar(texto);
            bitacoras.Show(this);
        }

        /// <summary>
        /// "Datos personales...": debajo del boton, exportar los datos de quien este
        /// elegido (su perfil, sus clases y sus sesiones) o, si su cuenta esta de
        /// baja y ya no tiene clases que sigan despues de hoy, anonimizarla
        /// (DerechosArco). Los avisos, cortos: la linea de estado mide unos 320 px
        /// a 1366 (la ruta del archivo sale en un mensaje).
        /// </summary>
        private void DatosPersonales()
        {
            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            DerechosArco.MostrarMenu(btnDatosPersonales, profesor, () =>
            {
                string ruta = DerechosArco.ExportarDocente(this, profesor, datos);
                if (ruta != null)
                {
                    Avisar("Se exportaron sus datos.");
                    DerechosArco.AvisarExportado(this, ruta);
                }
            }, () =>
            {
                if (DerechosArco.Anonimizar(this, datos, profesor, true))
                {
                    // Buscandola por su identificador nuevo: con la busqueda de antes (su
                    // nombre o su numero) ya no saldria.
                    string anonimo = UsuariosDatos.IdentificadorAnonimo(profesor.Id);
                    txtBuscar.Text = anonimo;
                    Recargar(anonimo);
                    Avisar("Cuenta anonimizada: ahora es " + anonimo + ".");
                }
            });
        }

        private void RestablecerContrasenaSeleccionado()
        {
            if (Permisos.Bloquear(this, "Restablecer la contrasena de un profesor")) return;

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
                    AvisoDeFalloAdmin.Mostrar(this, "Restablecer la contrasena de " + profesor.Matricula, ex, "No se pudo cambiar la contraseña", Text);
                }
            }
        }

        private void CambiarEstadoSeleccionado()
        {
            if (Permisos.Bloquear(this, "Dar de baja o reactivar un profesor")) return;

            ProfesorInfo profesor = Seleccionado<ProfesorInfo>();
            if (profesor == null)
            {
                return;
            }

            try
            {
                if (!profesor.Activo)
                {
                    // Reactivar no quita nada a nadie: no se pregunta. Sus clases no
                    // vuelven solas: las que se terminaron al darlo de baja se alargan
                    // (sus fechas) y las que se desactivaron se activan, en Horarios.
                    datos.Reactivar(profesor.Id);
                    Recargar(profesor.Matricula);
                    Avisar("Se reactivó a " + profesor.NombreCompleto + ".");
                    if (profesor.ClasesActivas == 0)
                    {
                        MessageBox.Show(this, "Se reactivó a " + profesor.NombreCompleto + ", pero no tiene clases vigentes." + Environment.NewLine + Environment.NewLine
                            + "Si se terminaron o se desactivaron al dar de baja su cuenta, en Horarios, pestaña Clases, edita las fechas de las terminadas "
                            + "para alargarlas o activa las desactivadas (o créale clases nuevas).",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    return;
                }

                string detalle = "Ya no podrá entrar a DigBit. Sus sesiones y las bitácoras de sus grupos se conservan en los informes, y puedes reactivar su cuenta cuando quieras.";
                string casilla = null;
                if (profesor.ClasesActivas > 0)
                {
                    // R4: con sesiones se terminan (su historia se conserva); sin ellas, se desactivan.
                    detalle = "Ya no podrá entrar a DigBit. Tiene " + profesor.ClasesActivas
                        + (profesor.ClasesActivas == 1 ? " clase vigente" : " clases vigentes")
                        + (string.IsNullOrEmpty(profesor.Laboratorios) ? "" : " en " + profesor.Laboratorios)
                        + ". Al terminarlas, las que ya se dieron acaban hoy y conservan su historia (Faltas y asistencia); "
                        + "las que nunca se dieron se desactivan. Si otra persona sigue con el grupo, créale una clase nueva en Horarios.";
                    casilla = "Terminar también " + (profesor.ClasesActivas == 1 ? "su clase" : "sus " + profesor.ClasesActivas + " clases")
                        + " (su código dejará de aceptarse en los laboratorios)";
                }

                bool terminarClases;
                if (!DialogoConfirmacion.Preguntar(this, "Dar de baja",
                    "¿Dar de baja a " + profesor.NombreCompleto + " (" + profesor.Matricula + ")?",
                    detalle, "Dar de baja", true, casilla, true, out terminarClases))
                {
                    return;
                }

                datos.DarDeBaja(profesor.Id, terminarClases);
                Recargar(profesor.Matricula);
                Avisar("Se dio de baja a " + profesor.NombreCompleto + ".");
                string clases = ClasesQuitadas(datos.ClasesTerminadas, datos.ClasesDesactivadas);
                if (clases.Length > 0)
                {
                    // En la linea de abajo no cabe: que se terminaron y que se desactivaron.
                    MessageBox.Show(this, "Se dio de baja a " + profesor.NombreCompleto + "." + Environment.NewLine + Environment.NewLine + clases,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Cambiar el estado del profesor " + profesor.Matricula, ex, "No se pudo cambiar el estado de la cuenta", Text);
            }
        }

        /// <summary>
        /// "Se terminaron 3 clases que ya tenian sesiones (su historia se conserva)
        /// y se desactivaron 2 que nunca se dieron." Vacio si no se toco ninguna.
        /// </summary>
        private static string ClasesQuitadas(int terminadas, int desactivadas)
        {
            string conSesiones = terminadas == 1 ? "Se terminó 1 clase que ya tenía sesiones (su historia se conserva)"
                : "Se terminaron " + terminadas + " clases que ya tenían sesiones (su historia se conserva)";
            if (desactivadas == 0)
            {
                return terminadas == 0 ? "" : conSesiones + ".";
            }

            string sinSesiones = desactivadas == 1 ? "1 que nunca se dio" : desactivadas + " que nunca se dieron";
            return terminadas == 0
                ? (desactivadas == 1 ? "Se desactivó su clase, que nunca se dio." : "Se desactivaron sus " + desactivadas + " clases, que nunca se dieron.")
                : conSesiones + (desactivadas == 1 ? " y se desactivó " : " y se desactivaron ") + sinSesiones + ".";
        }
    }
}
