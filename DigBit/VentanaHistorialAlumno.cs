using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Las bitacoras de un alumno: una fila por cada vez que registro, de la mas
    /// reciente a la mas antigua, con su clase, el equipo, la hora y lo que
    /// reporto. Es para "si vine el martes y no me aparece": antes habia que
    /// abrir sesion por sesion en Bitacoras. Doble clic (o "Ver la sesion"):
    /// quien mas registro en esa clase ese dia (BitacoraSesion; una sesion que no
    /// es de una clase del horario, en Bitacoras). "Exportar sus datos" guarda su
    /// perfil y todas sus bitacoras en un CSV para Excel: el derecho de acceso
    /// del punto 28 de pendientes. Se abre desde Alumnos ("Ver sus bitacoras").
    /// Solo lee: el encargado de laboratorio (solo consulta) la ve igual, pero
    /// exportar es del administrador; y en kiosco no se abre ningun dialogo de
    /// archivos de Windows (dejaria recorrer el disco del equipo).
    ///
    /// Con db/22, tambien una fila por cada asistencia que le dio su profesor sin
    /// bitacora (las anuladas, en gris como las quitadas): no son bitacoras, asi
    /// que no cuentan en el resumen de bitacoras ni en el de fallas.
    /// </summary>
    public class VentanaHistorialAlumno : PantallaAdmin
    {
        private static readonly string[] DiasCortos = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };
        // Los tonos de Bitacoras: la fila suave si reporto algo y la celda del reporte en fuerte.
        private static readonly Color ColorConFalla = Color.FromArgb(255, 240, 214);
        private static readonly Color ColorFallaCelda = Color.FromArgb(255, 176, 79);
        private static readonly Color ColorAviso = Color.FromArgb(190, 85, 0);
        // La asistencia que le dio su profesor, en el verde de su casilla en Mis grupos
        // (el estilo "Bueno" de Excel).
        private static readonly Color DadaFondo = Color.FromArgb(198, 239, 206);
        private static readonly Color DadaTexto = Color.FromArgb(0, 97, 0);
        private const string SinNovedad = "Sin novedad";

        private readonly AlumnosDatos datos = new AlumnosDatos();
        private readonly Consultas consultas = new Consultas();
        private readonly AlumnoInfo alumno;
        private readonly TextBox txtBuscar;
        private readonly Button btnVer;
        private readonly Button btnExportar;
        private readonly DrawingFont negrita;
        private List<Registro> registros = new List<Registro>();
        private List<Registro> visibles = new List<Registro>();
        // Si la ultima lectura salio bien: sin ella no se exporta (el archivo saldria
        // incompleto) y la lista dice que lo que se ve es de antes.
        private bool leida;
        // La ultima lectura buena, tal cual: es lo que se exporta (DerechosArco).
        private DataTable tablaLeida;
        // Las asistencias que le dio su profesor sin bitacora en esa misma lectura (db/22;
        // vacia sin ella), tambien las anuladas: tambien se exportan.
        private List<AsistenciaDada> dadasLeidas;

        /// <summary>
        /// Una bitacora del alumno (una fila de AlumnosDatos.Bitacoras), ya
        /// interpretada, o una asistencia que le dio su profesor sin bitacora (Dada).
        /// </summary>
        private sealed class Registro
        {
            /// <summary>La fila, para Consultas.DescribirFallas y compania, y para exportar lo que reporto tal cual. Null en una asistencia dada.</summary>
            public DataRow Fila;
            /// <summary>La asistencia que le dio su profesor sin bitacora (db/22), o null si es una bitacora.</summary>
            public AsistenciaDada Dada;
            /// <summary>Ya se leyo de que franja o sesion extra es la sesion de la asistencia dada (Ver la sesion).</summary>
            public bool SesionLeida;
            public int Id;
            public int SesionId;
            public DateTime? Fecha;
            public TimeSpan? Entrada;
            public TimeSpan? Salida;
            public int? FranjaId;
            public int? ExcepcionId;
            public bool UsoLibre;
            public string Laboratorio;
            public string Materia;
            public string Grupo;
            public string Profesor;
            public string Equipo;
            public DateTime? RegistradoEn;
            public bool SinConexion;
            /// <summary>Cuando su profesor le quito esta asistencia (db/17), o null.</summary>
            public DateTime? RetiradaEn;
            public bool ConFalla;
            public string Reporte;
            public string TextoBusqueda;

            // dd/MM/yyyy con InvariantCulture: el separador no sale de la configuracion de Windows.
            public string FechaCorta
            {
                get { return Fecha.HasValue ? DiasCortos[(int)Fecha.Value.DayOfWeek] + " " + Fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—"; }
            }

            public string Horario
            {
                get { return HoraCorta(Entrada) + " – " + HoraCorta(Salida); }
            }

            public string Clase
            {
                get
                {
                    return UsoLibre ? "Uso libre del laboratorio"
                        : EstiloAdmin.Guion(Materia) + (Grupo.Length > 0 ? ", " + Grupo : "");
                }
            }

            /// <summary>La hora en que registro, "—" en las de antes de guardarse la hora.</summary>
            public string Hora
            {
                get { return RegistradoEn.HasValue ? RegistradoEn.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "—"; }
            }

            /// <summary>Una sesion de una clase del horario, que BitacoraSesion encuentra por su franja y su dia o por su sesion extra.</summary>
            public bool DeUnaClase
            {
                get { return (FranjaId.HasValue && Fecha.HasValue) || ExcepcionId.HasValue; }
            }

            /// <summary>La de la lista: el id de la bitacora, o "d" y el de la asistencia dada (son tablas distintas).</summary>
            public string Clave
            {
                get { return Dada == null ? Id.ToString(CultureInfo.InvariantCulture) : "d" + Id.ToString(CultureInfo.InvariantCulture); }
            }

            /// <summary>En gris: la asistencia que le quito su profesor, o la que le dio y anulo.</summary>
            public bool Apagada
            {
                get { return RetiradaEn.HasValue || (Dada != null && !Dada.Activa); }
            }
        }

        // 1320 de ancho, como Bitacoras: la fecha y el laboratorio caben enteros (a
        // 1366x768 cabe; PantallaAdmin la limita a la pantalla).
        public VentanaHistorialAlumno(AlumnoInfo alumno)
            : base("Bitácoras de " + alumno.NombreCompleto,
                "Matrícula " + alumno.Matricula + ". Doble clic en una bitácora para ver quién más registró en esa clase. F5 actualiza.",
                new Size(1320, 640))
        {
            this.alumno = alumno;
            negrita = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);

            txtBuscar = CrearBuscador(new Point(Margen, 112), 330);
            ayuda.SetToolTip(txtBuscar, "Fecha (13/10/2026), clase, laboratorio, docente, equipo o lo que reportó");

            CrearLista(156);
            grid.Columns.Add("fecha", "Fecha");
            grid.Columns.Add("horario", "Horario");
            grid.Columns.Add("laboratorio", "Laboratorio");
            grid.Columns.Add("clase", "Clase");
            grid.Columns.Add("profesor", "Docente");
            grid.Columns.Add("equipo", "Equipo");
            grid.Columns.Add("hora", "Hora");
            grid.Columns.Add("reporte", "Reporte");
            EstiloHorarios.Pesos(grid, 130, 110, 205, 230, 175, 75, 60, 275);
            foreach (DataGridViewColumn columna in grid.Columns)
            {
                // Ya vienen de la mas reciente a la mas antigua; ordenar por el texto
                // "lun 13/10/2026" las desordenaria.
                columna.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            grid.Columns["hora"].ToolTipText = "Hora en que registró. Las bitácoras de antes de guardarse la hora muestran «—».";
            // El reporte, la clase, el laboratorio y el profesor completos, aunque sean
            // largos ("Programacion Orientada a Objetos, ISC-5001" no cabe en una
            // linea a 1366): la fila crece.
            foreach (string columna in new[] { "laboratorio", "clase", "profesor", "reporte" })
            {
                grid.Columns[columna].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            }

            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.CellFormatting += Formato;
            grid.Paint += PintarListaVacia;

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            // Entregarle sus datos a quien los pide es cosa de la administracion: la
            // persona responsable (solo consulta) nunca lo hace, asi que no lo ve (un
            // boton siempre apagado parecia descompuesto).
            if (!Permisos.SoloConsulta)
            {
                btnExportar = BotonAbajo("Exportar sus datos...", 200);
                btnExportar.Click += (s, e) => Exportar();
                ayuda.SetToolTip(btnExportar, "Guarda sus datos y todas sus bitácoras en un archivo CSV que se abre con Excel, "
                    + "para entregárselos si los pide.");
            }

            btnVer = BotonAbajo("Ver la sesión", 150);
            btnVer.Click += (s, e) => VerSesion();
            ayuda.SetToolTip(btnVer, "Quién más registró en esa clase ese día y qué reportaron. También con doble clic.");

            ActiveControl = txtBuscar;
            ActualizarBotones();
        }

        // =====================================================================
        // Carga y busqueda
        // =====================================================================

        protected override void Recargar(string seleccionar)
        {
            DataTable tabla;
            List<AsistenciaDada> dadas;
            try
            {
                tabla = datos.Bitacoras(alumno.Id);
                // Las asistencias que le dio su docente sin bitacora (db/22), tambien las
                // anuladas; vacia sin la 22. Si fallan, la lectura entera: sin ellas, su
                // historial (y lo que se exporta) saldria incompleto.
                dadas = AsistenciasDadas.DelEstudiante(alumno.Id);
            }
            catch (Exception ex)
            {
                // Se queda lo de la ultima lectura buena, si la hubo: "no ha registrado
                // ninguna" seria falso.
                leida = false;
                AvisoDeFalloAdmin.Mostrar(this, "Leer las bitacoras del alumno " + alumno.Matricula, ex, "No se pudieron leer sus bitácoras", Text);
                Aplicar(seleccionar);
                return;
            }

            registros = Mezclar(tabla.Rows.Cast<DataRow>().Select(Leer).ToList(), dadas);
            tablaLeida = tabla;
            dadasLeidas = dadas;
            leida = true;
            Aplicar(seleccionar);
        }

        /// <summary>
        /// Las bitacoras y las asistencias dadas en una sola lista, de la mas reciente
        /// a la mas antigua. Sin dadas, las bitacoras tal como vienen (como antes). El
        /// orden es estable: en una misma sesion, primero sus bitacoras.
        /// </summary>
        private static List<Registro> Mezclar(List<Registro> bitacoras, List<AsistenciaDada> dadas)
        {
            if (dadas.Count == 0)
            {
                return bitacoras;
            }

            return bitacoras.Concat(dadas.Select(LeerDada))
                .OrderByDescending(r => r.Fecha ?? DateTime.MinValue)
                .ThenByDescending(r => r.Entrada ?? TimeSpan.MinValue)
                .ToList();
        }

        /// <summary>
        /// Una asistencia que le dio su profesor sin bitacora: la fecha, las horas, el
        /// laboratorio, la clase y el profesor de su sesion; el equipo que no le
        /// sirvio y la hora en que se la dio.
        /// </summary>
        private static Registro LeerDada(AsistenciaDada dada)
        {
            Registro registro = new Registro
            {
                Dada = dada,
                Id = dada.Id,
                SesionId = dada.SesionId,
                Fecha = dada.Fecha,
                Entrada = dada.Inicio,
                Salida = dada.Fin,
                Laboratorio = dada.Laboratorio ?? "",
                Materia = dada.Materia ?? "",
                Grupo = dada.Grupo ?? "",
                Profesor = dada.Docente ?? "",
                Equipo = (dada.Equipo ?? "").Trim(),
                // La columna Hora: cuando se la dio.
                RegistradoEn = dada.DadaEn,
                Reporte = DescribirDada(dada)
            };
            registro.TextoBusqueda = EstiloAdmin.Normalizar(string.Join(" ", registro.FechaCorta,
                registro.Fecha.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                registro.Clase, registro.Laboratorio, registro.Profesor, registro.Equipo, registro.Reporte, "asistencia dada"));
            return registro;
        }

        /// <summary>
        /// "Sin bitacora: su docente le dio la asistencia (su equipo no tenia internet)"
        /// (con sus acentos); la anulada, "Su docente le dio la asistencia (...) y la
        /// anulo el 13/10/2026 10:50". Lo que escribio el profesor, al final (con
        /// "otro" ya es el motivo).
        /// </summary>
        private static string DescribirDada(AsistenciaDada dada)
        {
            string porque = AsistenciasDadas.EnFrase(dada);
            string detalle = dada.Motivo == AsistenciasDadas.Otro ? "" : (dada.Comentario ?? "").Trim();
            string texto = dada.Activa
                ? "Sin bitácora: su docente le dio la asistencia (" + porque + ")"
                : "Su docente le dio la asistencia (" + porque + ") y la anuló"
                  + (dada.AnuladaEn.HasValue ? " el " + dada.AnuladaEn.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "");
            return texto + (detalle.Length > 0 ? ". Detalle: " + detalle : "");
        }

        protected override void AlBuscar()
        {
            Aplicar(null);
        }

        private static Registro Leer(DataRow fila)
        {
            Registro registro = new Registro
            {
                Fila = fila,
                Id = Entero(fila, "idregistros_bitacoras") ?? 0,
                SesionId = Entero(fila, "idcodigos_accesos") ?? 0,
                Fecha = Fecha(fila, "fecha"),
                Entrada = HorariosDatos.LeerHora(fila["hora_entrada"]),
                Salida = HorariosDatos.LeerHora(fila["hora_salida"]),
                FranjaId = Entero(fila, "horarios_idhorarios"),
                ExcepcionId = Entero(fila, "horario_excepciones_id"),
                UsoLibre = Entero(fila, "uso_libre") == 1,
                Laboratorio = Texto(fila, "laboratorio"),
                Materia = Texto(fila, "materia"),
                Grupo = Texto(fila, "grupo"),
                Profesor = Texto(fila, "profesor"),
                Equipo = Texto(fila, "numero_computadora"),
                RegistradoEn = Fecha(fila, "registrado_en"),
                SinConexion = Entero(fila, "sin_conexion") == 1,
                RetiradaEn = Fecha(fila, "retirada_en"),
                ConFalla = Consultas.ConFalla(fila)
            };
            registro.Reporte = DescribirReporte(registro);
            registro.TextoBusqueda = EstiloAdmin.Normalizar(string.Join(" ", registro.FechaCorta,
                registro.Fecha.HasValue ? registro.Fecha.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                registro.Clase, registro.Laboratorio, registro.Profesor, registro.Equipo, registro.Reporte));
            return registro;
        }

        /// <summary>
        /// Lo que reporto (fallas y equipos sin internet que aviso, como en
        /// Bitacoras) y lo que conviene saber de esa bitacora: que se registro sin
        /// conexion o que su profesor le quito la asistencia (ya no cuenta en su
        /// lista). "Sin novedad" si nada.
        /// </summary>
        private static string DescribirReporte(Registro registro)
        {
            List<string> partes = new List<string>();
            if (registro.ConFalla || Consultas.AvisoSinInternet(registro.Fila).Length > 0)
            {
                partes.Add(Consultas.DescribirFallas(registro.Fila));
            }

            if (registro.SinConexion)
            {
                partes.Add("Registró sin conexión con el servidor");
            }

            if (registro.RetiradaEn.HasValue)
            {
                partes.Add("Su docente le quitó esta asistencia el " + registro.RetiradaEn.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            }

            return partes.Count == 0 ? SinNovedad : string.Join(" | ", partes);
        }

        /// <summary>Filtra en memoria y vuelve a pintar la lista; las asistencias quitadas (y las dadas que se anularon), en gris.</summary>
        private void Aplicar(string seleccionar)
        {
            visibles = registros.Where(r => EstiloAdmin.Coincide(r.TextoBusqueda, txtBuscar.Text)).ToList();
            LlenarLista(visibles,
                r => new object[]
                {
                    r.FechaCorta,
                    r.Horario,
                    EstiloAdmin.Guion(r.Laboratorio),
                    r.Clase,
                    EstiloAdmin.Guion(r.Profesor),
                    EstiloAdmin.Guion(r.Equipo),
                    r.Hora,
                    r.Reporte
                },
                r => r.Apagada, r => r.Clave, seleccionar);
            // Siempre hay una elegida (la mas reciente, si no), como en Bitacoras: "Ver la sesion" a la primera.
            if (grid.SelectedRows.Count == 0 && grid.Rows.Count > 0)
            {
                grid.Rows[0].Selected = true;
                grid.CurrentCell = grid.Rows[0].Cells[0];
            }

            grid.Invalidate();

            // Las cuentas son de bitacoras: las asistencias dadas van aparte (no son bitacoras)
            // y solo las que cuentan (las anuladas se ven en gris en la lista).
            List<Registro> bitacoras = registros.Where(r => r.Dada == null).ToList();
            int bitacorasVisibles = visibles.Count(r => r.Dada == null);
            int conFallas = bitacoras.Count(r => r.ConFalla);
            int retiradas = bitacoras.Count(r => r.RetiradaEn.HasValue);
            int dadas = registros.Count(r => r.Dada != null && r.Dada.Activa);
            List<DateTime> fechas = bitacoras.Where(r => r.Fecha.HasValue).Select(r => r.Fecha.Value.Date).ToList();
            // En naranja si la ultima lectura fallo: no es un resumen, es un aviso.
            lblResumen.ForeColor = leida ? Color.DarkGreen : ColorAviso;
            if (!leida)
            {
                lblResumen.Text = registros.Count == 0 ? "No se pudieron leer sus bitácoras: F5 lo vuelve a intentar."
                    : "No se pudieron volver a leer: lo que ves es de la lectura anterior (F5 lo vuelve a intentar).";
            }
            else if (registros.Count == 0)
            {
                lblResumen.Text = "Todavía no ha registrado ninguna bitácora.";
            }
            else if (visibles.Count == 0)
            {
                lblResumen.Text = SinCoincidencias;
            }
            else if (bitacoras.Count == 0)
            {
                lblResumen.Text = "Todavía no ha registrado ninguna bitácora"
                    + (dadas > 0 ? "; su docente le dio " + Plural(dadas, "asistencia", "asistencias") + " sin bitácora" : "") + ".";
            }
            else
            {
                string periodo = fechas.Count == 0 ? ""
                    : fechas.Min() == fechas.Max() ? ", el " + EstiloAdmin.Fecha(fechas.Min())
                    : ", del " + EstiloAdmin.Fecha(fechas.Min()) + " al " + EstiloAdmin.Fecha(fechas.Max());
                // Con quitadas y dadas a la vez, en una sola frase mas corta: a 1366 la linea
                // deja unos 744 px y las dos por separado pedirian unos 860.
                lblResumen.Text = (bitacorasVisibles == bitacoras.Count ? "" : bitacorasVisibles + " de ")
                    + Plural(bitacoras.Count, "bitácora", "bitácoras") + periodo
                    + (conFallas > 0 ? "; " + conFallas + " con fallas" : "")
                    + (retiradas > 0 && dadas > 0
                        ? "; " + Plural(retiradas, "asistencia quitada", "asistencias quitadas") + " y " + Plural(dadas, "dada", "dadas") + " por su docente"
                        : retiradas > 0 ? "; " + Plural(retiradas, "asistencia quitada", "asistencias quitadas")
                        : dadas > 0 ? "; " + Plural(dadas, "asistencia que le dio su docente sin bitácora", "asistencias que le dio su docente sin bitácora")
                        : "")
                    + ".";
            }
        }

        /// <summary>La busqueda no deja nada: "ninguna bitacora" si solo tiene bitacoras (como antes).</summary>
        private string SinCoincidencias
        {
            get { return registros.Any(r => r.Dada != null) ? "Nada coincide con la búsqueda." : "Ninguna bitácora coincide con la búsqueda."; }
        }

        protected override void ActualizarBotones()
        {
            // Antes de crear los botones (la lista va primero) no hay nada que hacer.
            if (btnVer == null)
            {
                return;
            }

            btnVer.Enabled = Seleccionado<Registro>() != null;
            if (btnExportar != null)
            {
                btnExportar.Enabled = leida;
            }
        }

        protected override void AlDobleClic()
        {
            VerSesion();
        }

        /// <summary>F5: vuelve a leer sus bitacoras, como en Bitacoras (la fila elegida se conserva).</summary>
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

        /// <summary>Con la lista vacia se dice por que, en medio de la lista.</summary>
        private void PintarListaVacia(object sender, PaintEventArgs e)
        {
            if (visibles.Count > 0)
            {
                return;
            }

            string texto = registros.Count == 0
                ? (leida ? "Todavía no ha registrado ninguna bitácora." : "No se pudieron leer sus bitácoras. F5 lo vuelve a intentar.")
                : SinCoincidencias;
            Rectangle area = new Rectangle(0, grid.ColumnHeadersHeight, grid.ClientSize.Width, grid.ClientSize.Height - grid.ColumnHeadersHeight);
            TextRenderer.DrawText(e.Graphics, texto, grid.Font, area, Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        /// <summary>
        /// Como en Bitacoras: la fila suave si reporto alguna falla y el reporte en
        /// tono fuerte; si solo aviso de equipos sin internet, el reporte en naranja;
        /// "Sin novedad" y lo demas, en gris.
        /// </summary>
        private void Formato(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            Registro registro = grid.Rows[e.RowIndex].Tag as Registro;
            if (registro == null)
            {
                return;
            }

            string columna = grid.Columns[e.ColumnIndex].Name;
            if (registro.Dada != null)
            {
                // La que cuenta, con el verde de su casilla en Mis grupos, tambien elegida; la
                // anulada se queda toda en gris (LlenarLista).
                if (registro.Dada.Activa && columna == "reporte")
                {
                    e.CellStyle.BackColor = DadaFondo;
                    e.CellStyle.ForeColor = DadaTexto;
                    e.CellStyle.SelectionBackColor = DadaFondo;
                    e.CellStyle.SelectionForeColor = DadaTexto;
                }

                return;
            }

            if (registro.ConFalla)
            {
                if (columna == "reporte")
                {
                    // Elegida, la fila va con el color de seleccion y el reporte sigue en naranja.
                    e.CellStyle.BackColor = ColorFallaCelda;
                    e.CellStyle.SelectionBackColor = ColorFallaCelda;
                    e.CellStyle.SelectionForeColor = Color.Black;
                    e.CellStyle.Font = negrita;
                }
                else
                {
                    e.CellStyle.BackColor = ColorConFalla;
                }
            }
            else if (columna == "reporte")
            {
                e.CellStyle.ForeColor = Consultas.AvisoSinInternet(registro.Fila).Length > 0 ? ColorAviso : Color.Gray;
            }
            else if (columna == "hora" && !registro.RegistradoEn.HasValue)
            {
                e.CellStyle.ForeColor = Color.Gray;
            }
        }

        // =====================================================================
        // Ver la sesion
        // =====================================================================

        /// <summary>
        /// Quien mas registro en esa clase ese dia. La de una clase del horario
        /// (franja o sesion extra), en BitacoraSesion, como desde Alertas y Faltas;
        /// la que no lo es (uso libre, o del sistema anterior, sin enlace a una
        /// clase), BitacoraSesion no la encuentra: en Bitacoras, en su dia y elegida.
        /// La de una asistencia dada es la sesion en que se dio.
        /// </summary>
        private void VerSesion()
        {
            Registro registro = Seleccionado<Registro>();
            if (registro == null)
            {
                return;
            }

            try
            {
                if (registro.Dada != null && !registro.SesionLeida)
                {
                    // La asistencia dada no trae de que franja o sesion extra es su sesion:
                    // se lee una vez, al abrirla.
                    VentanaCodigo sesion = consultas.ObtenerVentanaSesion(registro.SesionId);
                    if (sesion != null)
                    {
                        registro.FranjaId = sesion.FranjaId;
                        registro.ExcepcionId = sesion.ExcepcionId;
                    }

                    registro.SesionLeida = true;
                }

                if (registro.DeUnaClase)
                {
                    bool extra = !registro.FranjaId.HasValue;
                    string horario = (registro.Fecha.HasValue
                            ? HorariosDatos.NombresDia[((int)registro.Fecha.Value.DayOfWeek + 6) % 7 + 1] + " "
                              + registro.Fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + ", "
                            : "")
                        + HoraCorta(registro.Entrada) + " a " + HoraCorta(registro.Salida)
                        + (registro.Laboratorio.Length > 0 ? ", " + registro.Laboratorio : "")
                        + (extra ? " (sesión extra)" : "");
                    using (BitacoraSesion bitacora = new BitacoraSesion(registro.FranjaId, extra ? registro.ExcepcionId : null,
                        registro.Clase, horario, registro.Profesor, registro.Fecha))
                    {
                        bitacora.ShowDialog(this);
                    }

                    return;
                }

                using (GestionBitacorasAdministrador bitacoras = new GestionBitacorasAdministrador())
                {
                    bitacoras.MostrarSesion(registro.SesionId, registro.Fecha);
                    bitacoras.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Ver la sesion " + registro.SesionId + " del historial del alumno " + alumno.Matricula, ex,
                    "No se pudo abrir la sesión", Text);
            }
        }

        // =====================================================================
        // Exportar sus datos (derecho de acceso)
        // =====================================================================

        /// <summary>
        /// Su perfil y todas sus bitacoras (no solo las que deja ver la busqueda)
        /// en un CSV, donde elija el administrador (DerechosArco): lo de la ultima
        /// lectura buena o, si fallo, otra lectura. Queda en el log quien lo hizo.
        /// </summary>
        private void Exportar()
        {
            string ruta = DerechosArco.ExportarEstudiante(this, alumno, leida ? tablaLeida : null, leida ? dadasLeidas : null);
            if (ruta != null)
            {
                Avisar("Se exportaron sus datos.");
                DerechosArco.AvisarExportado(this, ruta);
            }
        }

        // =====================================================================
        // Utilidades
        // =====================================================================

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                negrita.Dispose();
            }

            base.Dispose(disposing);
        }

        private static string HoraCorta(TimeSpan? hora)
        {
            return hora.HasValue ? HorariosDatos.Hora(hora.Value) : "?";
        }

        private static string Plural(int n, string uno, string varios)
        {
            return n + " " + (n == 1 ? uno : varios);
        }

        private static string Texto(DataRow fila, string columna)
        {
            return !fila.Table.Columns.Contains(columna) || fila[columna] == DBNull.Value ? "" : fila[columna].ToString().Trim();
        }

        private static int? Entero(DataRow fila, string columna)
        {
            int numero;
            return int.TryParse(Texto(fila, columna), NumberStyles.Integer, CultureInfo.InvariantCulture, out numero) ? numero : (int?)null;
        }

        private static DateTime? Fecha(DataRow fila, string columna)
        {
            object valor = fila.Table.Columns.Contains(columna) ? fila[columna] : null;
            return valor is DateTime ? (DateTime)valor : (DateTime?)null;
        }
    }

    /// <summary>
    /// Derechos ARCO desde la administracion (punto 28 de pendientes):
    /// entregarle a una persona sus datos en un CSV para Excel (estudiantes y
    /// docentes) y anonimizar una cuenta ya dada de baja cuando pide la
    /// cancelacion (UsuariosDatos.Anonimizar). Lo usan Estudiantes (y el historial
    /// de cada estudiante) y Docentes. Solo la administracion: la persona
    /// responsable de laboratorio no lo tiene. En el kiosco no se abre ningun
    /// dialogo de archivos de Windows (dejaria recorrer el disco del equipo).
    /// </summary>
    internal static class DerechosArco
    {
        private const string Titulo = "Datos personales";
        private const string SinArchivosEnKiosco = "En los equipos del laboratorio DigBit no guarda archivos. "
            + "Exporta los datos desde el equipo de la administración.";

        /// <summary>
        /// El boton "Datos personales..." de una lista: debajo de el, exportar sus
        /// datos y anonimizar la cuenta, con lo que no se puede dicho en la opcion.
        /// </summary>
        public static void MostrarMenu(Control boton, UsuarioInfo persona, Action exportar, Action anonimizar)
        {
            // Lo elegido corre cuando el menu ya se cerro: abre ventanas modales y,
            // mientras, el menu se libera.
            ContextMenuStrip menu = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 10F), ShowItemToolTips = true };
            LlenarMenu(menu.Items, boton, persona, exportar, anonimizar);
            menu.Closed += (s, e) => boton.BeginInvoke(new Action(menu.Dispose));
            menu.Show(boton, new Point(0, boton.Height));
        }

        /// <summary>
        /// Las dos opciones de MostrarMenu en esa lista de opciones (un menu propio o el
        /// submenu "Datos personales" de "Mas acciones" en Docentes). Lo elegido corre
        /// con BeginInvoke de dueno, cuando el menu ya se cerro.
        /// </summary>
        public static void LlenarMenu(ToolStripItemCollection opciones, Control dueno, UsuarioInfo persona, Action exportar, Action anonimizar)
        {
            Control boton = dueno;
            ToolStripMenuItem opcionExportar = new ToolStripMenuItem("Exportar sus datos (CSV)...", null, (s, e) => boton.BeginInvoke(exportar))
            {
                Enabled = !persona.Anonimizada,
                ToolTipText = persona.Anonimizada ? "La cuenta se anonimizó: ya no tiene datos personales."
                    : "Su perfil y lo que registró, en un archivo para Excel, para entregárselo si lo pide."
            };
            ToolStripMenuItem opcionAnonimizar = new ToolStripMenuItem(persona.Anonimizada ? "La cuenta ya está anonimizada"
                : persona.Activo ? "Anonimizar la cuenta (primero dala de baja)" : "Anonimizar la cuenta...", null, (s, e) => boton.BeginInvoke(anonimizar))
            {
                Enabled = !persona.Activo && !persona.Anonimizada,
                ToolTipText = "Si pide que se cancelen sus datos: borra su nombre, sus apellidos, su correo y su identificador, "
                    + "y conserva sus registros sin su nombre. No se puede deshacer."
            };
            opciones.Add(opcionExportar);
            opciones.Add(opcionAnonimizar);
        }

        // =====================================================================
        // Exportar sus datos
        // =====================================================================

        /// <summary>
        /// Su perfil, todas sus bitacoras y, con db/22, todas las asistencias que le
        /// dio su profesor sin bitacora (tambien las anuladas: es su derecho de
        /// acceso) en un CSV, donde elija el administrador. Lo que no llegue ya leido
        /// (null), lo lee. La ruta del archivo, o null si no se guardo (cancelo,
        /// kiosco, error ya explicado).
        /// </summary>
        public static string ExportarEstudiante(IWin32Window dueno, AlumnoInfo alumno, DataTable bitacoras, List<AsistenciaDada> dadas = null)
        {
            if (!SePuedeExportar(dueno, alumno, "Exportar los datos de un alumno"))
            {
                return null;
            }

            if (bitacoras == null)
            {
                try
                {
                    bitacoras = new AlumnosDatos().Bitacoras(alumno.Id);
                }
                catch (Exception ex)
                {
                    AvisoDeFalloAdmin.Mostrar(dueno, "Leer las bitacoras del alumno " + alumno.Matricula + " para exportarlas", ex,
                        "No se pudieron leer sus bitácoras, así que no se exportó nada", Titulo);
                    return null;
                }
            }

            if (dadas == null)
            {
                try
                {
                    // Vacia sin db/22: el archivo sale como siempre.
                    dadas = AsistenciasDadas.DelEstudiante(alumno.Id);
                }
                catch (Exception ex)
                {
                    AvisoDeFalloAdmin.Mostrar(dueno, "Leer las asistencias dadas del alumno " + alumno.Matricula + " para exportarlas", ex,
                        "No se pudieron leer las asistencias que le dio su docente, así que no se exportó nada", Titulo);
                    return null;
                }
            }

            string ruta = ElegirArchivo(dueno, "Exportar los datos de " + alumno.NombreCompleto, alumno.Matricula);
            if (ruta == null || !Escribir(dueno, ruta, CsvDeEstudiante(alumno, bitacoras, dadas), "Exportar los datos del alumno " + alumno.Matricula))
            {
                return null;
            }

            // Solo su id (y en Docentes igual): con su matricula, el log uniria a la
            // persona con su "ANON-id" si despues se anonimiza su cuenta.
            Log.Info("Datos del usuario id " + alumno.Id + " (estudiante) exportados por " + (Datos_User.getUser() ?? "-")
                + ": su perfil y " + bitacoras.Rows.Count + " bitacoras"
                + (dadas.Count > 0 ? " y " + dadas.Count + " asistencias dadas" : "") + ".");
            return ruta;
        }

        /// <summary>
        /// Su perfil, sus clases (con su horario), sus sesiones (cuantas bitacoras
        /// hubo en cada una, sin los nombres de nadie) y, con db/15, las fallas que
        /// reporto, en un CSV. La ruta, o null si no se guardo.
        /// </summary>
        public static string ExportarDocente(IWin32Window dueno, ProfesorInfo profesor, ProfesoresDatos datos)
        {
            if (!SePuedeExportar(dueno, profesor, "Exportar los datos de un profesor"))
            {
                return null;
            }

            DatosDeDocente suyos;
            try
            {
                suyos = datos.DatosParaExportar(profesor.Id);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(dueno, "Leer los datos del profesor " + profesor.Matricula + " para exportarlos", ex,
                    "No se pudieron leer sus clases y sesiones, así que no se exportó nada", Titulo);
                return null;
            }

            string ruta = ElegirArchivo(dueno, "Exportar los datos de " + profesor.NombreCompleto, profesor.Matricula);
            if (ruta == null || !Escribir(dueno, ruta, CsvDeDocente(profesor, suyos), "Exportar los datos del profesor " + profesor.Matricula))
            {
                return null;
            }

            Log.Info("Datos del usuario id " + profesor.Id + " (docente) exportados por " + (Datos_User.getUser() ?? "-")
                + ": su perfil, " + suyos.Clases.Rows.Count + " clases y " + suyos.Sesiones.Rows.Count + " sesiones.");
            return ruta;
        }

        /// <summary>
        /// Donde quedo lo exportado, entero: en la linea de estado de las listas la
        /// ruta no cabe (a 1366, Docentes deja unos 320 px). La linea dice solo que se hizo.
        /// </summary>
        public static void AvisarExportado(IWin32Window dueno, string ruta)
        {
            MessageBox.Show(dueno, "Se guardaron sus datos en:" + Environment.NewLine + Environment.NewLine + ruta, Titulo,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static bool SePuedeExportar(IWin32Window dueno, UsuarioInfo persona, string accion)
        {
            if (Permisos.Bloquear(dueno, accion))
            {
                return false;
            }

            if (Kiosco.Activo)
            {
                MessageBox.Show(dueno, SinArchivosEnKiosco, Titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (persona.Anonimizada)
            {
                MessageBox.Show(dueno, "Esta cuenta se anonimizó: ya no tiene datos personales que exportar.", Titulo,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            return true;
        }

        /// <summary>"Guardar como" en Documentos, con "datos_IDENTIFICADOR_aaaa-mm-dd.csv" (sin su nombre). Null si cancela.</summary>
        private static string ElegirArchivo(IWin32Window dueno, string titulo, string identificador)
        {
            string seguro = new string((identificador ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            using (SaveFileDialog dialogo = new SaveFileDialog
            {
                Title = titulo,
                Filter = "Archivo para Excel (CSV)|*.csv",
                DefaultExt = "csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "datos_" + seguro + "_" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                return dialogo.ShowDialog(dueno) == DialogResult.OK ? dialogo.FileName : null;
            }
        }

        private static bool Escribir(IWin32Window dueno, string ruta, string contenido, string contexto)
        {
            try
            {
                // Con BOM: sin el, Excel toma el archivo por otra codificacion y rompe los acentos.
                File.WriteAllText(ruta, contenido, new UTF8Encoding(true));
                return true;
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(dueno, contexto, ex, "No se pudo guardar el archivo", Titulo);
                return false;
            }
        }

        /// <summary>
        /// Separado por comas, como lo abre Excel en espanol de Mexico. Primero sus
        /// datos, un renglon por dato; despues, una tabla con una fila por bitacora y
        /// lo que reporto tal como se guardo; y, si las tiene (db/22), otra con las
        /// asistencias que le dio su profesor sin bitacora, tambien las anuladas.
        /// </summary>
        private static string CsvDeEstudiante(AlumnoInfo alumno, DataTable bitacoras, List<AsistenciaDada> dadas)
        {
            dadas = dadas ?? new List<AsistenciaDada>();
            int dadasQueCuentan = dadas.Count(d => d.Activa);
            StringBuilder csv = new StringBuilder();
            Renglon(csv, "Datos personales en DigBit (cuenta de estudiante)");
            Renglon(csv, "Matrícula", alumno.Matricula);
            Renglon(csv, "Nombre(s)", alumno.Nombre);
            Renglon(csv, "Apellido paterno", alumno.ApellidoPaterno);
            Renglon(csv, "Apellido materno", alumno.ApellidoMaterno);
            Renglon(csv, "Correo", alumno.Correo);
            Renglon(csv, "Carrera", alumno.Carrera);
            Renglon(csv, "Semestre", alumno.Semestre);
            if (!string.IsNullOrWhiteSpace(alumno.Grupo))
            {
                Renglon(csv, "Grupo", alumno.Grupo);
            }

            Renglon(csv, "Estado", Estado(alumno));
            Renglon(csv, "Bitácoras registradas", bitacoras.Rows.Count.ToString(CultureInfo.InvariantCulture));
            if (dadas.Count > 0)
            {
                Renglon(csv, "Asistencias que le dio su docente sin bitácora", dadasQueCuentan.ToString(CultureInfo.InvariantCulture));
                if (dadasQueCuentan < dadas.Count)
                {
                    Renglon(csv, "Asistencias que le dio su docente y después anuló", (dadas.Count - dadasQueCuentan).ToString(CultureInfo.InvariantCulture));
                }
            }

            Pie(csv);

            Renglon(csv, "Fecha", "Día", "Entrada", "Salida", "Laboratorio", "Materia", "Grupo", "Docente", "Equipo", "Registró",
                "Sin conexión", "Falla de red", "Comentario de red", "Falla de hardware", "Comentario de hardware",
                "Falla de software", "Comentario de software", "Equipos sin internet que avisó", "Asistencia quitada por su docente");
            foreach (DataRow fila in bitacoras.Rows)
            {
                DateTime? fecha = FechaDe(fila, "fecha");
                DateTime? registrado = FechaDe(fila, "registrado_en");
                DateTime? retirada = FechaDe(fila, "retirada_en");
                Renglon(csv,
                    Dia(fecha),
                    fecha.HasValue ? NombreDelDia(fecha.Value) : "",
                    HoraDe(fila, "hora_entrada"),
                    HoraDe(fila, "hora_salida"),
                    TextoDe(fila, "laboratorio"),
                    TextoDe(fila, "uso_libre") == "1" ? "Uso libre del laboratorio" : TextoDe(fila, "materia"),
                    TextoDe(fila, "grupo"),
                    TextoDe(fila, "profesor"),
                    TextoDe(fila, "numero_computadora"),
                    registrado.HasValue ? registrado.Value.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) : "",
                    TextoDe(fila, "sin_conexion") == "1" ? "Sí" : "No",
                    TextoDe(fila, "falla_red"),
                    TextoDe(fila, "comentarios_red"),
                    TextoDe(fila, "falla_hardware"),
                    TextoDe(fila, "comentarios_hardware"),
                    TextoDe(fila, "falla_software"),
                    TextoDe(fila, "comentarios_software"),
                    TextoDe(fila, "avisa_sin_internet").Replace(",", ", "),
                    retirada.HasValue ? retirada.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "");
            }

            // Las asistencias dadas: no son bitacoras, en su propia tabla. Fecha es el dia en
            // que cuentan; Docente, el de su sesion; Anulada, cuando la anulo (vacia si cuenta).
            if (dadas.Count > 0)
            {
                csv.Append("\r\n");
                Renglon(csv, "Asistencias que le dio su docente sin bitácora");
                Renglon(csv, "Fecha", "Día", "Laboratorio", "Materia", "Grupo", "Docente", "Se la dio", "Motivo", "Equipo", "Detalle", "Anulada");
                foreach (AsistenciaDada dada in dadas)
                {
                    Renglon(csv,
                        Dia(dada.Fecha),
                        NombreDelDia(dada.Fecha),
                        dada.Laboratorio,
                        dada.Materia,
                        dada.Grupo,
                        dada.Docente,
                        dada.DadaEn.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                        AsistenciasDadas.NombreDeMotivo(dada.Motivo),
                        dada.Equipo,
                        dada.Comentario,
                        dada.Activa ? ""
                            : dada.AnuladaEn.HasValue ? dada.AnuladaEn.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "Sí");
                }
            }

            return csv.ToString();
        }

        /// <summary>
        /// Como el de estudiantes: su perfil y, en tablas separadas por un renglon
        /// vacio, sus clases, sus sesiones y las fallas que reporto.
        /// </summary>
        private static string CsvDeDocente(ProfesorInfo profesor, DatosDeDocente suyos)
        {
            StringBuilder csv = new StringBuilder();
            Renglon(csv, "Datos personales en DigBit (cuenta docente)");
            Renglon(csv, "Número de empleado", profesor.Matricula);
            Renglon(csv, "Nombre(s)", profesor.Nombre);
            Renglon(csv, "Apellido paterno", profesor.ApellidoPaterno);
            Renglon(csv, "Apellido materno", profesor.ApellidoMaterno);
            Renglon(csv, "Correo", profesor.Correo);
            Renglon(csv, "Estado", Estado(profesor));
            Renglon(csv, "Clases", suyos.Clases.Rows.Count.ToString(CultureInfo.InvariantCulture));
            Renglon(csv, "Sesiones", suyos.Sesiones.Rows.Count.ToString(CultureInfo.InvariantCulture));
            Pie(csv);

            // Su horario, franja por franja: "Lunes 07:00 a 09:00 en Laboratorio 1".
            Dictionary<int, List<string>> horarios = new Dictionary<int, List<string>>();
            foreach (DataRow franja in suyos.Franjas.Rows)
            {
                int clase = Convert.ToInt32(franja["clases_idclases"], CultureInfo.InvariantCulture);
                int dia = Convert.ToInt32(franja["dia_semana"], CultureInfo.InvariantCulture);
                DateTime? desde = FechaDe(franja, "vigente_desde");
                DateTime? hasta = FechaDe(franja, "vigente_hasta");
                string texto = (dia >= 1 && dia <= 7 ? HorariosDatos.NombresDia[dia] : "Día " + dia) + " " + HoraDe(franja, "hora_inicio") + " a "
                    + HoraDe(franja, "hora_fin") + (TextoDe(franja, "laboratorio").Length > 0 ? " en " + TextoDe(franja, "laboratorio") : "")
                    + (desde.HasValue || hasta.HasValue ? " (" + (desde.HasValue ? "desde el " + Dia(desde) : "")
                        + (desde.HasValue && hasta.HasValue ? " " : "") + (hasta.HasValue ? "hasta el " + Dia(hasta) : "") + ")" : "");
                List<string> lista;
                if (!horarios.TryGetValue(clase, out lista))
                {
                    lista = new List<string>();
                    horarios[clase] = lista;
                }

                lista.Add(texto);
            }

            Renglon(csv, "Clases");
            Renglon(csv, "Materia", "Grupo", "Vigente desde", "Vigente hasta", "Activa", "Horario");
            foreach (DataRow clase in suyos.Clases.Rows)
            {
                List<string> horario;
                horarios.TryGetValue(Convert.ToInt32(clase["idclases"], CultureInfo.InvariantCulture), out horario);
                Renglon(csv, TextoDe(clase, "materia"), TextoDe(clase, "grupo"), Dia(FechaDe(clase, "vigente_desde")),
                    Dia(FechaDe(clase, "vigente_hasta")), TextoDe(clase, "activa") == "1" ? "Sí" : "No",
                    horario == null ? "" : string.Join("; ", horario));
            }

            csv.Append("\r\n");
            Renglon(csv, "Sesiones");
            // Con db/22, cuantas asistencias dio sin bitacora en cada una (solo el numero: el
            // detalle es de cada estudiante); sin ella, la tabla de siempre.
            bool conDadas = suyos.Sesiones.Columns.Contains("dadas");
            List<string> encabezados = new List<string> { "Fecha", "Día", "Entrada", "Salida", "Laboratorio", "Materia", "Grupo", "Sesión extra", "Bitácoras registradas" };
            if (conDadas)
            {
                encabezados.Add("Asistencias que dio sin bitácora");
            }

            Renglon(csv, encabezados.ToArray());
            foreach (DataRow sesion in suyos.Sesiones.Rows)
            {
                DateTime? fecha = FechaDe(sesion, "fecha");
                List<string> celdas = new List<string>
                {
                    Dia(fecha), fecha.HasValue ? NombreDelDia(fecha.Value) : "", HoraDe(sesion, "hora_entrada"), HoraDe(sesion, "hora_salida"),
                    TextoDe(sesion, "laboratorio"), TextoDe(sesion, "materia"), TextoDe(sesion, "grupo"),
                    TextoDe(sesion, "extra") == "1" ? "Sí" : "No", TextoDe(sesion, "bitacoras")
                };
                if (conDadas)
                {
                    celdas.Add(TextoDe(sesion, "dadas"));
                }

                Renglon(csv, celdas.ToArray());
            }

            if (suyos.Reportes != null)
            {
                csv.Append("\r\n");
                Renglon(csv, "Fallas que reportó");
                Renglon(csv, "Reportada el", "Fecha de la clase", "Laboratorio", "Materia", "Grupo", "Equipo", "Área", "Falla", "Comentario");
                foreach (DataRow reporte in suyos.Reportes.Rows)
                {
                    DateTime? cuando = FechaDe(reporte, "registrado_en");
                    Renglon(csv, cuando.HasValue ? cuando.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "",
                        Dia(FechaDe(reporte, "fecha")), TextoDe(reporte, "laboratorio"), TextoDe(reporte, "materia"), TextoDe(reporte, "grupo"),
                        TextoDe(reporte, "equipo"), TextoDe(reporte, "area"), TextoDe(reporte, "falla"), TextoDe(reporte, "comentarios"));
                }
            }

            return csv.ToString();
        }

        /// <summary>"Cuenta activa" o "Cuenta dada de baja el 01/10/2026".</summary>
        private static string Estado(UsuarioInfo persona)
        {
            return persona.Activo ? "Cuenta activa"
                : "Cuenta dada de baja" + (persona.BajaEn.HasValue ? " el " + EstiloAdmin.Fecha(persona.BajaEn) : "");
        }

        /// <summary>Cuando y quien lo exporto, y un renglon vacio antes de las tablas.</summary>
        private static void Pie(StringBuilder csv)
        {
            Renglon(csv, "Exportado el", DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
            Renglon(csv, "Exportado por", Datos_User.getUser());
            csv.Append("\r\n");
        }

        private static void Renglon(StringBuilder csv, params string[] celdas)
        {
            csv.Append(string.Join(",", celdas.Select(Celda))).Append("\r\n");
        }

        /// <summary>
        /// Una celda del CSV. Entre comillas si lleva comas, comillas o saltos de
        /// linea. Lo que empieza con =, +, - o @ (un comentario de quien registro)
        /// lleva delante un apostrofo: Excel lo ejecutaria como formula.
        /// </summary>
        private static string Celda(string valor)
        {
            string texto = (valor ?? "").Trim();
            if (texto.Length > 0 && "=+-@".IndexOf(texto[0]) >= 0)
            {
                texto = "'" + texto;
            }

            return texto.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + texto.Replace("\"", "\"\"") + "\"" : texto;
        }

        private static string TextoDe(DataRow fila, string columna)
        {
            return !fila.Table.Columns.Contains(columna) || fila[columna] == DBNull.Value
                ? "" : Convert.ToString(fila[columna], CultureInfo.InvariantCulture).Trim();
        }

        private static DateTime? FechaDe(DataRow fila, string columna)
        {
            object valor = fila.Table.Columns.Contains(columna) ? fila[columna] : null;
            return valor is DateTime ? (DateTime)valor : (DateTime?)null;
        }

        /// <summary>"07:00", o "" sin hora (una sesion del sistema anterior).</summary>
        private static string HoraDe(DataRow fila, string columna)
        {
            TimeSpan? hora = fila.Table.Columns.Contains(columna) ? HorariosDatos.LeerHora(fila[columna]) : null;
            return hora.HasValue ? HorariosDatos.Hora(hora.Value) : "";
        }

        private static string Dia(DateTime? fecha)
        {
            return fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";
        }

        /// <summary>"martes".</summary>
        private static string NombreDelDia(DateTime fecha)
        {
            return HorariosDatos.NombresDia[((int)fecha.DayOfWeek + 6) % 7 + 1].ToLowerInvariant();
        }

        // =====================================================================
        // Anonimizar
        // =====================================================================

        /// <summary>
        /// Cancelacion u oposicion de una cuenta ya dada de baja: avisa que no se
        /// deshace, vuelve a pedir la contrasena de quien lo hace y la anonimiza
        /// (UsuariosDatos.Anonimizar). True si quedo anonimizada.
        /// </summary>
        public static bool Anonimizar(IWin32Window dueno, UsuariosDatos datos, UsuarioInfo persona, bool esDocente)
        {
            if (Permisos.Bloquear(dueno, "Anonimizar una cuenta"))
            {
                return false;
            }

            if (persona.Anonimizada || persona.Activo)
            {
                MessageBox.Show(dueno, persona.Anonimizada ? "Esta cuenta ya está anonimizada."
                    : "Solo se anonimiza una cuenta dada de baja: primero dala de baja (botón «Dar de baja») y después anonimízala.",
                    Titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            // Lo que lo impide (un docente con clases que siguen despues de hoy...), con
            // la regla de la capa de datos y la base de ahora, antes de las dos
            // confirmaciones: la lista puede ser de antes de terminar sus clases en Horarios.
            try
            {
                datos.ComprobarAnonimizar(persona.Id);
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(dueno, ex.Message, Titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(dueno, "Comprobar si se puede anonimizar la cuenta del usuario id " + persona.Id, ex,
                    "No se pudo comprobar si la cuenta se puede anonimizar", Titulo);
                return false;
            }

            string anonimo = UsuariosDatos.IdentificadorAnonimo(persona.Id);
            string quien = persona.NombreCompleto + " (" + persona.Matricula + ")";
            if (!DialogoConfirmacion.Preguntar(dueno, "Anonimizar cuenta", "¿Anonimizar la cuenta de " + quien + "?",
                "No se puede deshacer. Su nombre, sus apellidos y su correo se borran, su " + (esDocente ? "número de empleado" : "matrícula")
                + " pasa a ser " + anonimo + " y su contraseña deja de servir. " + (esDocente ? "Sus clases, sesiones y bitácoras" : "Sus bitácoras")
                + " se conservan sin su nombre. Hazlo solo si pidió que se cancelen sus datos.", "Anonimizar"))
            {
                return false;
            }

            // Con su contrasena otra vez: si dejo la sesion abierta y no esta, nadie mas lo
            // hace. Al log va solo su id: su nombre y su matricula junto a "ANON-id"
            // dejarian escrito justo lo que se borra. Sin db/21 no hay historial (solo
            // el log): la constancia se dice sin decir donde.
            if (!DialogoContrasenaAdmin.Confirmar(dueno, "¿Anonimizar la cuenta de " + quien + "?",
                "No se puede deshacer: sus datos personales se borran y la cuenta queda como " + anonimo + ". Queda constancia de quién lo hizo y cuándo.",
                "Anonimizar", "Anonimizar la cuenta del usuario id " + persona.Id))
            {
                return false;
            }

            try
            {
                datos.Anonimizar(persona.Id);
                return true;
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(dueno, "Anonimizar la cuenta del usuario id " + persona.Id, ex, "No se pudo anonimizar la cuenta", Titulo);
                return false;
            }
        }
    }
}
