using DigLab.conexion;
using DigLab.Infraestructura;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>Los tres pasos de la pestana Proximo ciclo (su barra de arriba).</summary>
    internal enum PasoProximoCiclo
    {
        /// <summary>Las plantillas guardadas y los horarios aplicados (la lista).</summary>
        Plantillas = 1,
        /// <summary>El horario en preparacion: se ve y se usa como Horario semanal.</summary>
        Preparacion = 2,
        /// <summary>Aplicar con fechas (PestanaAplicacion).</summary>
        Aplicar = 3
    }

    /// <summary>
    /// Los nombres de las acciones sobre las horas de un horario semanal: los
    /// usan la preparacion de Proximo ciclo y Horario semanal
    /// (AdministrarHorarios), para que digan lo mismo (una palabra por accion).
    /// </summary>
    internal static class TextosDelHorario
    {
        public const string PonerClase = "Poner clase...";
        public const string CambiarHoras = "Cambiar horas...";
        public const string QuitarHoras = "Quitar horas...";
        public const string QuitarEstasHoras = "Quitar estas horas del horario...";
        public const string PonerEligiendo = "Poner una clase (eligiendo día y horas)...";
        public const string QuitarSeleccion = "Quitar la selección";
        public const string SinCuadrosLibres = "Poner una clase (no hay cuadros libres en lo que elegiste)";
        /// <summary>Titulo de la ventana de los dialogos de poner una clase y de cambiar horas.</summary>
        public const string TituloPoner = "Poner una clase";
        public const string TituloCambiar = "Cambiar horas";
        public const string AyudaPonerClase = "Pon una clase en los cuadros libres que elegiste (arrastra o usa Ctrl+clic en la cuadrícula).";
        public const string AyudaCambiarHoras = "Cambia el día, las horas o la clase de las horas elegidas en la cuadrícula.";
        public const string AyudaQuitarHoras = "Quita del horario las horas elegidas en la cuadrícula (antes te pregunta).";

        /// <summary>"lunes": el dia en minuscula, para dentro de una frase.</summary>
        public static string Dia(int dia)
        {
            return dia >= 1 && dia < HorariosDatos.NombresDia.Length ? HorariosDatos.NombresDia[dia].ToLowerInvariant() : dia.ToString();
        }

        /// <summary>"de 07:00 a 09:00". Un cuadro de la ultima fila acaba a las 24:00: se dice (y se guarda) 23:59.</summary>
        public static string Horas(TimeSpan inicio, TimeSpan fin)
        {
            return "de " + HorariosDatos.Hora(inicio) + " a " + HorariosDatos.Hora(fin >= TimeSpan.FromHours(24) ? new TimeSpan(23, 59, 0) : fin);
        }

        /// <summary>"miercoles de 09:00 a 11:00", "lunes de 07:00 a 09:00 y martes de 07:00 a 09:00"; con mas de tres, las dos primeras "y 3 mas".</summary>
        public static string Rangos(IList<RangoHueco> rangos)
        {
            List<string> partes = (rangos ?? new List<RangoHueco>()).Select(r => Dia(r.Dia) + " " + Horas(r.Inicio, r.Fin)).ToList();
            if (partes.Count > 3)
            {
                partes = partes.Take(2).Concat(new[] { (partes.Count - 2) + " más" }).ToList();
            }

            return RevisionCiclo.Enumerar(partes);
        }

        /// <summary>El menu de los cuadros libres: "Poner una clase el miercoles de 09:00 a 11:00 (todas las semanas)...".</summary>
        public static string MenuPonerEnHuecos(IList<RangoHueco> libres)
        {
            return "Poner una clase el " + Rangos(libres) + " (todas las semanas)...";
        }

        /// <summary>
        /// El titulo del aviso de unas horas que no se guardan (HorariosDatos.ValidarFranja,
        /// que dice primero ": mismo laboratorio" si se solapan en el laboratorio):
        /// "Laboratorio ocupado" si se solapan en el mismo laboratorio (eso no es un
        /// choque, decision del 2026-10-31); si no, "Choque de horario" (la misma
        /// persona docente o el mismo grupo en otro laboratorio, o lo que sea).
        /// </summary>
        public static string TituloDeCruce(string mensaje)
        {
            return EsDelMismoLaboratorio(mensaje) ? "Laboratorio ocupado" : "Choque de horario";
        }

        /// <summary>Si ese aviso de HorariosDatos dice que la hora ya la tiene otra clase del mismo laboratorio.</summary>
        public static bool EsDelMismoLaboratorio(string mensaje)
        {
            return (mensaje ?? "").Contains(": mismo laboratorio");
        }

        /// <summary>El menu de una clase: "Cambiar horas de Bases de Datos con ISC-5002 (lunes de 06:00 a 08:00)...".</summary>
        public static string MenuCambiarHoras(string materia, string grupo, int dia, TimeSpan inicio, TimeSpan fin)
        {
            return "Cambiar horas de " + materia + " con " + grupo + " (" + Dia(dia) + " " + Horas(inicio, fin) + ")...";
        }

        /// <summary>El ultimo dia que se ofrece al poner o cambiar horas en una plantilla: el sabado (2026-11-03).</summary>
        public const int UltimoDiaDeClases = 6;

        /// <summary>
        /// Los dias que ofrecen los dialogos de horas de una plantilla: de lunes a
        /// sabado, como la cuadricula. Si las horas que se cambian ya estan otro
        /// dia (unas viejas el domingo), ese tambien, para que se vea cual es.
        /// </summary>
        public static List<OpcionCombo> DiasParaElegir(int? diaActual)
        {
            List<OpcionCombo> dias = Enumerable.Range(1, UltimoDiaDeClases).Select(d => new OpcionCombo { Id = d, Texto = HorariosDatos.NombresDia[d] }).ToList();
            if (diaActual.HasValue && diaActual.Value > UltimoDiaDeClases && diaActual.Value < HorariosDatos.NombresDia.Length)
            {
                dias.Add(new OpcionCombo { Id = diaActual.Value, Texto = HorariosDatos.NombresDia[diaActual.Value] });
            }

            return dias;
        }
    }

    /// <summary>
    /// Pestana Proximo ciclo de Horarios por laboratorio (decisiones del
    /// 2026-10-25): junta lo que eran Configuraciones y Aplicacion en tres pasos,
    /// con una barra de pastillas arriba:
    /// 1. Plantillas: las guardadas y los horarios aplicados; crear el horario nuevo
    ///    (DialogoCrearHorario), dar de baja el actual, y cambiarles el nombre o borrarlas.
    /// 2. Preparacion: la plantilla en preparacion, que se ve y se usa como
    ///    Horario semanal (CuadriculaSemanal sin fechas, el mismo selector de
    ///    laboratorio, los botones "Poner clase...", "Cambiar horas..." y
    ///    "Quitar horas...", los mismos menus y DialogoAsignarClase), con la
    ///    marca "En preparacion (plantilla): nada se usa hasta aplicarla" y sin
    ///    excepciones, cancelaciones ni sesiones extra. Poner horas encima de
    ///    otra clase del mismo laboratorio la reemplaza (antes pregunta): se le
    ///    quita solo lo que se cruza, y eso queda en "Falta asignar" (con su
    ///    lista, "Ponerla..." y "Ya no hace falta"), como "Quitar horas" si se
    ///    van a dar en otro horario. Los choques (la misma persona docente o el
    ///    mismo grupo a la misma hora en otro laboratorio) no impiden guardarla:
    ///    se avisan (en rojo, "Choques: N") y no dejan aplicar. Tambien
    ///    "Imprimir PDF" del laboratorio y, en la lista de clases, "Ver en el
    ///    horario" (2026-11-03: las mismas acciones que Horario semanal).
    /// 3. Aplicar con fechas: PestanaAplicacion.
    /// Nada de lo de los pasos 1 y 2 cambia el horario que se usa (regla R1).
    /// Solo la administracion escribe; la persona responsable del laboratorio
    /// (solo consulta) ve la lista de las guardadas y las aplicadas ("Horarios
    /// guardados") y las abre para verlas, sin la barra de pasos.
    /// Contrato con AdministrarHorarios: new PestanaProximoCiclo(IdAdministrador,
    /// IrASemanaDeHorarios) con Dock = Fill; Cargar() al elegir la pestana y con
    /// F5; ManejarEscape() en ProcessDialogKey; IrAPaso, Abrir, ElegirLaboratorio
    /// y LaboratorioCambiado para los saltos y el laboratorio compartido;
    /// NombrePestana y Frase para la pestana y su renglon de arriba.
    /// </summary>
    internal class PestanaProximoCiclo : Panel
    {
        private enum Vista
        {
            Semana,
            Clases
        }

        private const int Margen = 12;
        private const int AltoPie = 40;
        // La fila de la barra de pasos (y 6) y, debajo, la de los botones (y 40),
        // como la del laboratorio y los botones de Horario semanal (y 8).
        private const int ArribaFilaB = 40;
        private const string Titulo = "Próximo ciclo";

        /// <summary>El azul grisaceo de Proximo ciclo: otro horario, no el que se usa.</summary>
        internal static readonly Color AzulOscuro = Color.FromArgb(40, 70, 110);
        internal static readonly Color AzulClaro = Color.FromArgb(226, 234, 243);
        private static readonly Color BordeAzulClaro = Color.FromArgb(160, 180, 205);

        // Lo que se recuerda mientras dura la sesion de DigLab: al volver a la
        // pestana se sigue en el paso, la plantilla abierta, la vista y el
        // laboratorio de antes. Si en el mismo proceso entra otra cuenta, se
        // olvida (OlvidarSiCambioLaSesion): la persona responsable nunca hereda
        // lo que tenia abierto la administracion.
        private static PasoProximoCiclo? pasoRecordado;
        private static int? abiertaRecordada;
        private static Vista vistaRecordada = Vista.Semana;
        private static int laboratorioRecordado;
        private static string sesionRecordada;

        private readonly CicloPreliminarDatos datos = new CicloPreliminarDatos();
        private readonly HorariosDatos horarios = new HorariosDatos();
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };
        private readonly bool soloConsulta = Permisos.SoloConsulta;
        private readonly DrawingFont fuenteBase = new DrawingFont("Century Gothic", 10F);
        private readonly DrawingFont fuentePastilla = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private readonly DrawingFont fuenteChip = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteVacia = new DrawingFont("Century Gothic", 10F);
        private readonly DrawingFont fuenteNegrita = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
        private readonly Timer esperaBusqueda = new Timer { Interval = 250 };

        private SituacionConfiguraciones situacion;
        private string falloAlLeer;
        // Lo que se ensena; no se mira Visible (es false con la ventana oculta).
        private Panel panelVisible;
        private bool callarFallos;
        private List<OpcionCombo> laboratoriosActivos = new List<OpcionCombo>();
        private PasoProximoCiclo paso = PasoProximoCiclo.Plantillas;
        // Mientras se pone un laboratorio desde fuera (ElegirLaboratorio), no se avisa.
        private bool eligiendoDesdeFuera;

        // --- La barra de pasos (se pasa al panel que se ve)
        private readonly Panel barraPasos;
        private readonly PastillaConfiguracion pasoPlantillas;
        private readonly PastillaConfiguracion pasoPreparacion;
        private readonly PastillaConfiguracion pasoAplicar;

        // --- Sin base (falta la 16, la 23 o la 09)
        private readonly Panel panelSinBase;
        private readonly TarjetaConfiguracion tarjetaSinBase;
        private readonly Label lblSinBaseTitulo;
        private readonly Label lblSinBaseTexto;
        private readonly Button btnRevisar;

        // --- Paso 1: las plantillas
        private readonly Panel panelLista;
        private readonly Label lblListaTitulo;
        private readonly Label lblListaTexto;
        private readonly Button btnCrearHorario;
        private readonly Label lblGuardadas;
        private readonly TextBox txtBuscarLista;
        private readonly ComboBox cboQueLista;
        private readonly DataGridView dgvLista;
        private readonly Label lblEstadoLista;
        private readonly Button btnAbrir;
        private readonly Button btnRenombrar;
        private readonly Button btnBorrar;
        private string textoListaVacia = "";

        // --- Paso 2 sin nada en preparacion (o con una aplicada por empezar)
        private readonly Panel panelVacio;
        private readonly TarjetaConfiguracion tarjetaVacio;
        private readonly Label lblVacioTitulo;
        private readonly Label lblVacioTexto;
        private readonly Button btnPrepararVacio;
        private readonly Button btnIrAplicarVacio;
        private readonly Label lblEstadoVacio;

        // --- Una plantilla abierta: la que esta en preparacion (paso 2) o una guardada o aplicada (paso 1, solo para verla)
        private readonly Panel panelAbierta;
        private readonly Button btnCerrarVentana;
        private readonly PastillaConfiguracion pastMarca;
        private readonly PastillaConfiguracion pastChoques;
        private readonly PastillaConfiguracion pastSinDocente;
        private readonly PastillaConfiguracion pastSinHorario;
        private readonly PastillaConfiguracion pastFaltaAsignar;
        private readonly Label lblLaboratorio;
        private readonly ComboBox cboLaboratorio;
        // Las flechas del laboratorio (Deslizamiento).
        private readonly FlechasDeLista flechasLaboratorio;
        private readonly Button btnPoner;
        private readonly Button btnCambiarHoras;
        private readonly Button btnQuitarHoras;
        private readonly Button btnVerTabla;
        private readonly Button btnImprimir;
        private readonly Label lblLeyenda;
        private readonly Button btnMas;
        private readonly ContextMenuStrip menuMas;
        private readonly ToolStripMenuItem itemVista;
        // "Imprimir PDF" en el menu cuando el boton no cabe en la fila (ventanas estrechas).
        private readonly ToolStripMenuItem itemImprimir;
        private readonly ToolStripSeparator sepVista;
        private readonly ToolStripMenuItem itemGuardarComo;
        private readonly ToolStripMenuItem itemGuardarEn;
        private readonly ToolStripMenuItem itemCambiarNombre;
        private readonly ToolStripSeparator sepDescartar;
        private readonly ToolStripMenuItem itemEmpezarDeNuevo;
        private readonly ToolStripMenuItem itemDescartar;
        // El horario en Excel (pedido del 2026-11-12): cargar uno en cuadricula o descargar este para llenarlo.
        private readonly Button btnExcel;
        private readonly ContextMenuStrip menuExcel;
        private readonly ToolStripSeparator sepExcel;
        private readonly ToolStripMenuItem itemCargarExcel;
        private readonly ToolStripMenuItem itemDescargarExcel;
        private readonly Panel panelLeyenda;
        private readonly Label lblEstadoAbierta;
        private readonly Panel contenedorSemana;
        private readonly CuadriculaSemanal lienzo;
        private readonly ContextMenuStrip menuFranja;
        private readonly ContextMenuStrip menuHueco;
        // La lista de clases
        private readonly LinkLabel lnkVolverSemana;
        private readonly Label lblDocenteFiltro;
        private readonly ComboBox cboDocenteFiltro;
        private readonly Label lblBuscarClases;
        private readonly TextBox txtBuscarClases;
        private readonly PastillaConfiguracion pastFiltro;
        private readonly DataGridView dgvClases;
        private readonly Button btnVerEnSemana;
        private readonly Button btnNuevaClase;
        private readonly Button btnCambiarClase;
        private readonly Button btnQuitarClases;
        private string textoClasesVacia = "";

        // --- Paso 3: aplicar con fechas
        private readonly Panel panelAplicar;
        private readonly PestanaAplicacion aplicacion;

        // Lo de la plantilla abierta.
        private CicloLeido leido;
        private RevisionDelCiclo revision;
        private Vista vista = Vista.Semana;
        // Esta pestana es la vista de VentanaVerHorario (AbrirSoloParaVer), no la de Horarios.
        private bool enVentana;
        private FiltroClases filtro = FiltroClases.Ninguno;
        private int laboratorioElegido;
        private int profesorFijado;
        private FranjaInfo franjaEnMenu;
        private bool llenando;
        // Lo que dice la linea de estado de la abierta cuando no hay otra cosa que decir.
        private string detalleAbierta = "";
        // La clase traida de "Falta asignar" con «Ponerla...» (su idcarga): el
        // siguiente "Poner clase..." ya la trae elegida. Esc la olvida.
        private int? porPoner;

        /// <summary>Un laboratorio del combo: en la lista, con sus horas y choques; en la caja, solo su nombre (como Horario semanal).</summary>
        private sealed class ItemLaboratorio
        {
            public int Id;
            /// <summary>El nombre que se ve ("... (desactivado)").</summary>
            public string Nombre;
            /// <summary>Su nombre en la base, sin "(desactivado)": el del PDF.</summary>
            public string Laboratorio;
            public string Texto;
            public double Horas;
            public int Choques;

            public override string ToString()
            {
                return Texto;
            }
        }

        public PestanaProximoCiclo()
            : this(() => 0, lunes => { })
        {
        }

        /// <param name="idAdministrador">El idusuarios de quien tiene la sesion (cambiado_por, publicado_por, historial).</param>
        /// <param name="irASemanaDeHorarios">Lleva a Horario semanal en la semana de ese lunes (desde «3. Aplicar con fechas»).</param>
        public PestanaProximoCiclo(Func<int> idAdministrador, Action<DateTime> irASemanaDeHorarios)
        {
            IdAdministrador = idAdministrador ?? (() => 0);
            IrASemanaDeHorarios = irASemanaDeHorarios ?? (lunes => { });
            OlvidarSiCambioLaSesion();
            BackColor = Color.White;
            // La pestana hereda la negrita del TabControl: aqui, la letra normal de DigLab.
            Font = fuenteBase;

            // ----------------------------------------------------------- barra de pasos
            barraPasos = new Panel { BackColor = Color.White, Size = new Size(460, 30), Visible = false };
            pasoPlantillas = new PastillaConfiguracion("1. Plantillas", 0, 28, fuentePastilla, true) { Location = new Point(0, 0) };
            pasoPlantillas.Click += (s, e) => IrAPaso(PasoProximoCiclo.Plantillas);
            ayuda.SetToolTip(pasoPlantillas, "Las plantillas guardadas y los horarios que se aplicaron: ábrelas para verlas o úsalas para crear el horario del próximo ciclo.");
            pasoPreparacion = new PastillaConfiguracion("2. Preparación", 0, 28, fuentePastilla, true);
            pasoPreparacion.Click += (s, e) => IrAPaso(PasoProximoCiclo.Preparacion);
            pasoAplicar = new PastillaConfiguracion("3. Aplicar con fechas", 0, 28, fuentePastilla, true);
            pasoAplicar.Click += (s, e) => IrAPaso(PasoProximoCiclo.Aplicar);
            ayuda.SetToolTip(pasoAplicar, "Pon las fechas del horario en preparación, revisa que todo esté bien y aplícalo.");
            barraPasos.Controls.Add(pasoPlantillas);
            barraPasos.Controls.Add(pasoPreparacion);
            barraPasos.Controls.Add(pasoAplicar);
            AcomodarBarra();

            // Sin "Dar de baja el horario actual..." (pedido del 2026-11-12): dejaba todos los
            // laboratorios sin clases. El horario en uso solo se quita al aplicar otro con
            // "Ahora mismo", que ya lo sustituye (tambien lo que empezo hoy).

            // ----------------------------------------------------------- sin base
            panelSinBase = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Visible = false };
            tarjetaSinBase = new TarjetaConfiguracion { Size = new Size(640, 170) };
            lblSinBaseTitulo = PiezasConfiguracion.Texto("Falta preparar la base", new Point(24, 18), new Size(592, 26), 14F, Color.DarkGreen, FontStyle.Bold);
            lblSinBaseTexto = PiezasConfiguracion.Texto("", new Point(24, 52), new Size(592, 60), 10F, Color.FromArgb(50, 50, 50));
            btnRevisar = EstiloHorarios.CrearBoton("Volver a revisar", new Point(24, 118), new Size(160, 36));
            btnRevisar.Click += (s, e) => Cargar();
            tarjetaSinBase.Controls.Add(lblSinBaseTitulo);
            tarjetaSinBase.Controls.Add(lblSinBaseTexto);
            tarjetaSinBase.Controls.Add(btnRevisar);
            panelSinBase.Controls.Add(tarjetaSinBase);
            panelSinBase.Resize += (s, e) => AcomodarSinBase();

            // ----------------------------------------------------------- paso 1: plantillas
            panelLista = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Visible = false };
            lblListaTitulo = PiezasConfiguracion.Texto("Horarios guardados", new Point(Margen, 8), new Size(400, 26), 14F, Color.DarkGreen, FontStyle.Bold);
            lblListaTitulo.Visible = soloConsulta;
            lblListaTexto = PiezasConfiguracion.Texto(soloConsulta
                    ? "Las plantillas guardadas del horario y los horarios que se aplicaron. Solo consulta: ábrelos para verlos."
                    : "Las plantillas guardadas del horario y los horarios que se aplicaron en otros ciclos. Abre una para verla o úsala con «Crear horario nuevo...»: nada cambia en los laboratorios hasta aplicarlo.",
                new Point(Margen, 42), new Size(900, 20), 9.5F, Color.FromArgb(80, 80, 80));
            lblListaTexto.AutoEllipsis = true;
            btnCrearHorario = EstiloAdmin.BotonPrincipal("Crear horario nuevo...", Point.Empty, new Size(230, 34));
            btnCrearHorario.Click += (s, e) =>
            {
                CicloPreliminar elegida = ElegidaEnLista();
                CrearHorario(elegida == null ? (int?)null : elegida.Id);
            };
            ayuda.SetToolTip(btnCrearHorario, "El horario del próximo ciclo: copia uno completo, solo sus clases sin horas, o empieza en blanco. Te explica cada opción.\n"
                + "Nada cambia en los laboratorios hasta aplicarlo.");

            lblGuardadas = PiezasConfiguracion.Texto("Plantillas", new Point(Margen, 76), new Size(90, 22), 10F, Color.DarkGreen, FontStyle.Bold);
            txtBuscarLista = EstiloHorarios.CrearTexto(new Point(110, 72), 220);
            EstiloHorarios.PonerTextoGuia(txtBuscarLista, "Buscar por nombre");
            txtBuscarLista.TextChanged += (s, e) => PintarLista(null);
            txtBuscarLista.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && dgvLista.Rows.Count > 0)
                {
                    dgvLista.Focus();
                    e.Handled = true;
                }
            };
            cboQueLista = EstiloHorarios.CrearCombo(new Point(340, 72), 210);
            cboQueLista.Items.AddRange(new object[] { "Todas", "Plantillas guardadas", "Horarios aplicados" });
            cboQueLista.SelectedIndex = 0;
            cboQueLista.SelectedIndexChanged += (s, e) => PintarLista(null);

            dgvLista = EstiloHorarios.CrearGrid(new Point(Margen, 104), new Size(900, 300));
            dgvLista.Columns.Add("Nombre", "Nombre");
            dgvLista.Columns.Add("QueEs", "Qué es");
            dgvLista.Columns.Add("Docentes", "Docentes");
            dgvLista.Columns.Add("Clases", "Clases");
            dgvLista.Columns.Add("Horas", "Horas");
            dgvLista.Columns.Add("Laboratorios", "Laboratorios");
            dgvLista.Columns.Add("Fechas", "Fechas");
            dgvLista.Columns.Add("Guardada", "Guardada");
            // A 1366 no caben en un renglon el nombre, "Aplicado, empieza el 04/11/2026", "Del
            // 04/11/2026 al 03/04/2027" y "Aplicado el 03/11/2026, ADMPRUEBA": las cuentas van
            // en su minimo, "Que es" y "Fechas" en uno, y lo que no quepa pasa al segundo
            // renglon (nada se corta). Cada fila mide al menos lo de siempre (34).
            dgvLista.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            dgvLista.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            dgvLista.RowTemplate.MinimumHeight = dgvLista.RowTemplate.Height;
            EstiloHorarios.Pesos(dgvLista, 18, 22, 5, 5, 5, 5, 20, 20);
            foreach (string corta in new[] { "Docentes", "Clases", "Horas", "Laboratorios" })
            {
                dgvLista.Columns[corta].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            dgvLista.SelectionChanged += (s, e) => ActualizarBotonesLista();
            dgvLista.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0)
                {
                    AbrirElegida();
                }
            };
            dgvLista.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    AbrirElegida();
                }
            };
            dgvLista.Paint += (s, e) => PintarVacia(dgvLista, e.Graphics, textoListaVacia);

            lblEstadoLista = PiezasConfiguracion.Texto("", new Point(Margen, 0), new Size(400, 20), 9.5F, Color.DarkGreen);
            lblEstadoLista.AutoEllipsis = true;
            btnAbrir = EstiloHorarios.CrearBoton("Ver", Point.Empty, new Size(100, 34));
            btnAbrir.Click += (s, e) => AbrirElegida();
            ayuda.SetToolTip(btnAbrir, "Abre la elegida para verla, como Horario semanal (doble clic o Intro).");
            btnRenombrar = EstiloHorarios.CrearBoton("Cambiar el nombre...", Point.Empty, new Size(190, 34));
            btnRenombrar.Click += (s, e) => Renombrar(ElegidaEnLista());
            btnBorrar = EstiloHorarios.CrearBoton("Borrar...", Point.Empty, new Size(110, 34));
            btnBorrar.Click += (s, e) => Borrar(ElegidaEnLista());
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnBorrar, () =>
            {
                CicloPreliminar elegida = ElegidaEnLista();
                return elegida != null && elegida.Aplicada ? "Los horarios aplicados quedan como registro: no se borran." : null;
            });

            foreach (Control c in new Control[]
            {
                lblListaTitulo, lblListaTexto, btnCrearHorario, lblGuardadas, txtBuscarLista, cboQueLista, dgvLista, lblEstadoLista,
                btnAbrir, btnRenombrar, btnBorrar
            })
            {
                panelLista.Controls.Add(c);
            }

            panelLista.Resize += (s, e) => AcomodarLista();

            // ----------------------------------------------------------- paso 2 sin nada en preparacion
            panelVacio = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Visible = false };
            tarjetaVacio = new TarjetaConfiguracion { Size = new Size(680, 190) };
            lblVacioTitulo = PiezasConfiguracion.Texto("", new Point(24, 18), new Size(632, 26), 13F, Color.DarkGreen, FontStyle.Bold);
            lblVacioTexto = PiezasConfiguracion.Texto("", new Point(24, 52), new Size(632, 60), 10F, Color.FromArgb(50, 50, 50));
            btnPrepararVacio = EstiloAdmin.BotonPrincipal("Crear horario nuevo...", new Point(24, 124), new Size(260, 38));
            btnPrepararVacio.Click += (s, e) => CrearHorario(null);
            ayuda.SetToolTip(btnPrepararVacio, "Copia uno completo, solo sus clases sin horas, empieza en blanco o desde un Excel en cuadrícula. Te explica cada opción.\n"
                + "También puedes arrastrar aquí un Excel con el horario.");
            btnIrAplicarVacio = EstiloHorarios.CrearBoton("Ir a «3. Aplicar con fechas»", new Point(24, 124), new Size(260, 38));
            btnIrAplicarVacio.Click += (s, e) => IrAPaso(PasoProximoCiclo.Aplicar);
            tarjetaVacio.Controls.Add(lblVacioTitulo);
            tarjetaVacio.Controls.Add(lblVacioTexto);
            tarjetaVacio.Controls.Add(btnPrepararVacio);
            tarjetaVacio.Controls.Add(btnIrAplicarVacio);
            lblEstadoVacio = PiezasConfiguracion.Texto("", Point.Empty, new Size(680, 40), 9.5F, Color.DarkGreen);
            panelVacio.Controls.Add(tarjetaVacio);
            panelVacio.Controls.Add(lblEstadoVacio);
            panelVacio.Resize += (s, e) => AcomodarVacio();

            // ----------------------------------------------------------- una plantilla abierta
            panelAbierta = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Visible = false };
            // Solo en VentanaVerHorario: ahi no hay pasos ni pestanas para salir.
            btnCerrarVentana = EstiloHorarios.CrearBoton("Cerrar", Point.Empty, new Size(120, 34));
            btnCerrarVentana.DialogResult = DialogResult.Cancel;
            btnCerrarVentana.Visible = false;
            pastMarca = new PastillaConfiguracion("Marca", 0, 28, fuentePastilla, false);

            pastChoques = new PastillaConfiguracion("Choques", 0, 28, fuentePastilla, true);
            pastChoques.Click += (s, e) => CambiarVista(Vista.Clases, revision != null && revision.Choques.Count > 0 ? FiltroClases.Choques : FiltroClases.Ninguno);
            pastSinDocente = new PastillaConfiguracion("Sin docente", 0, 28, fuentePastilla, true);
            pastSinDocente.Colores(PiezasConfiguracion.FondoRojo, PiezasConfiguracion.BordeRojo, PiezasConfiguracion.TextoRojo);
            pastSinDocente.Click += (s, e) => CambiarVista(Vista.Clases, FiltroClases.SinDocente);
            ayuda.SetToolTip(pastSinDocente, "Clases sin docente: no dejan aplicar. Clic: verlas en la lista de clases.");
            pastSinHorario = new PastillaConfiguracion("Sin horario", 0, 28, fuentePastilla, true);
            pastSinHorario.Colores(PiezasConfiguracion.FondoAmbar, PiezasConfiguracion.BordeAmbar, PiezasConfiguracion.TextoAmbar);
            pastSinHorario.Click += (s, e) => CambiarVista(Vista.Clases, FiltroClases.SinHorario);
            ayuda.SetToolTip(pastSinHorario, "Clases sin horas en ningún laboratorio: es un aviso, no impide aplicar. Clic: verlas en la lista de clases.");
            // Como Horario semanal con db/25: lo que le falta a alguna clase, en ambar. Solo se ve si falta algo.
            pastFaltaAsignar = new PastillaConfiguracion("Falta asignar", 0, 28, fuentePastilla, true);
            pastFaltaAsignar.Colores(PiezasConfiguracion.FondoAmbar, PiezasConfiguracion.BordeAmbar, PiezasConfiguracion.TextoAmbar);
            pastFaltaAsignar.Click += (s, e) => VerPorAsignar();
            ayuda.SetToolTip(pastFaltaAsignar, "Horas que le faltan a alguna clase de este horario, en todos los laboratorios: las que otra clase desplazó\n"
                + "o las que se quitaron para darlas en otro horario. Bajan solas al ponerle horas a la clase. Clic: ver cuáles.");

            // Fila de los botones, como la de Horario semanal: el laboratorio y, a su derecha, las acciones.
            lblLaboratorio = EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(Margen, ArribaFilaB + 8));
            cboLaboratorio = EstiloHorarios.CrearCombo(new Point(110, ArribaFilaB + 4), 300);
            cboLaboratorio.MaxDropDownItems = 16;
            cboLaboratorio.DrawMode = DrawMode.OwnerDrawFixed;
            cboLaboratorio.ItemHeight = 20;
            cboLaboratorio.DrawItem += DibujarLaboratorio;
            cboLaboratorio.SelectedIndexChanged += (s, e) =>
            {
                ItemLaboratorio elegido = cboLaboratorio.SelectedItem as ItemLaboratorio;
                if (llenando || elegido == null)
                {
                    return;
                }

                laboratorioElegido = elegido.Id;
                laboratorioRecordado = elegido.Id;
                profesorFijado = 0;
                // Con las flechas (o la lista), el horario se desliza hacia ese lado.
                Deslizamiento.Cambiar(flechasLaboratorio == null ? 0 : flechasLaboratorio.Direccion, () =>
                {
                    lienzo.LimpiarSeleccion();
                    PintarSemana();
                    AcomodarAbierta();
                }, lienzo);
                if (!eligiendoDesdeFuera && LaboratorioCambiado != null)
                {
                    LaboratorioCambiado(elegido.Id);
                }
            };
            // Entre las flechas, solo el nombre (como la caja de antes); las horas y
            // los choques, en el menu y en la ayuda.
            flechasLaboratorio = new FlechasDeLista(cboLaboratorio, "Laboratorio", o =>
            {
                ItemLaboratorio item = o as ItemLaboratorio;
                return item == null ? null : item.Nombre;
            });

            btnPoner = EstiloHorarios.CrearBoton(TextosDelHorario.PonerClase, new Point(424, ArribaFilaB), new Size(126, 34));
            btnPoner.Click += (s, e) => PonerClaseEnHuecos();
            ayuda.SetToolTip(btnPoner, TextosDelHorario.AyudaPonerClase);
            btnCambiarHoras = EstiloHorarios.CrearBoton(TextosDelHorario.CambiarHoras, new Point(558, ArribaFilaB), new Size(140, 34));
            btnCambiarHoras.Click += (s, e) => CambiarHoras();
            ayuda.SetToolTip(btnCambiarHoras, TextosDelHorario.AyudaCambiarHoras);
            btnQuitarHoras = EstiloHorarios.CrearBoton(TextosDelHorario.QuitarHoras, new Point(706, ArribaFilaB), new Size(126, 34));
            btnQuitarHoras.Click += (s, e) => QuitarHoras();
            ayuda.SetToolTip(btnQuitarHoras, TextosDelHorario.AyudaQuitarHoras);

            // Una guardada o aplicada, solo para verla: sus acciones en el mismo sitio.
            btnVerTabla = EstiloAdmin.BotonPrincipal("Ver docentes y clases", Point.Empty, new Size(230, 34));
            btnVerTabla.Click += (s, e) => VerDocentesYClases();
            ayuda.SetToolTip(btnVerTabla, "La tabla de cada docente con sus clases y sus horas, en otra ventana. No cambia nada.");
            // Como en Horario semanal, en la preparacion.
            btnImprimir = EstiloHorarios.CrearBoton("Imprimir PDF", Point.Empty, new Size(126, 34));
            btnImprimir.Click += (s, e) => ImprimirPdf();
            ayuda.SetToolTip(btnImprimir, "El horario de este laboratorio en una hoja, sin códigos ni fechas (una semana tipo), para enseñarlo o imprimirlo.");

            lblLeyenda = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Size = new Size(300, 30)
            };

            btnMas = EstiloHorarios.CrearBoton("Más acciones ▾", Point.Empty, new Size(150, 34));
            menuMas = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            itemVista = new ToolStripMenuItem("Ver la lista de clases", null, (s, e) => CambiarVista(vista == Vista.Semana ? Vista.Clases : Vista.Semana, FiltroClases.Ninguno));
            itemImprimir = new ToolStripMenuItem("Imprimir PDF de este laboratorio", null, (s, e) => ImprimirPdf());
            sepVista = new ToolStripSeparator();
            itemGuardarComo = new ToolStripMenuItem("Guardar como plantilla nueva...", null, (s, e) => GuardarComo());
            itemGuardarEn = new ToolStripMenuItem("Guardar en la plantilla de la que viene...", null, (s, e) => GuardarEnSuSitio());
            itemCambiarNombre = new ToolStripMenuItem("Cambiar el nombre...", null, (s, e) => Renombrar(leido == null ? null : leido.Ciclo));
            sepDescartar = new ToolStripSeparator();
            itemEmpezarDeNuevo = new ToolStripMenuItem("Empezar de nuevo (copiar otro horario, solo sus clases o en blanco)...", null, (s, e) => CrearHorario(null));
            itemDescartar = new ToolStripMenuItem("Descartar este horario en preparación...", null, (s, e) => Descartar());
            sepExcel = new ToolStripSeparator();
            itemCargarExcel = new ToolStripMenuItem("Cargar un Excel en este horario...", null, (s, e) => ElegirExcel());
            itemDescargarExcel = new ToolStripMenuItem("Descargar este horario en Excel (para llenarlo)...", null, (s, e) => DescargarExcel());
            menuMas.Items.AddRange(new ToolStripItem[]
            {
                itemVista, itemImprimir, sepVista, itemGuardarComo, itemGuardarEn, itemCambiarNombre, sepExcel, itemCargarExcel, itemDescargarExcel,
                sepDescartar, itemEmpezarDeNuevo, itemDescartar
            });
            btnMas.Click += (s, e) => MostrarMenuMas();

            btnExcel = EstiloHorarios.CrearBoton("Excel ▾", Point.Empty, new Size(96, 34));
            menuExcel = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuExcel.Items.Add(new ToolStripMenuItem("Cargar un Excel en este horario...", null, (s, e) => ElegirExcel()));
            menuExcel.Items.Add(new ToolStripMenuItem("Descargar este horario en Excel (para llenarlo)...", null, (s, e) => DescargarExcel()));
            btnExcel.Click += (s, e) => menuExcel.Show(btnExcel, new Point(btnExcel.Width, btnExcel.Height), ToolStripDropDownDirection.BelowLeft);
            ayuda.SetToolTip(btnExcel, "El horario en una hoja de Excel por laboratorio (días en columnas, horas en renglones).\n"
                + "Descarga este para llenarlo, o carga uno ya lleno: también puedes arrastrar el archivo hasta el horario.\n"
                + "Lo de cada laboratorio del Excel reemplaza lo que este horario tiene en ese laboratorio; nada se usa hasta aplicarlo.");

            panelLeyenda = new Panel { BackColor = Color.White, Size = new Size(600, 26) };
            lblEstadoAbierta = PiezasConfiguracion.Texto("", Point.Empty, new Size(400, 20), 9.5F, Color.DarkGreen);
            lblEstadoAbierta.AutoEllipsis = true;
            lblEstadoAbierta.TextAlign = ContentAlignment.MiddleRight;

            contenedorSemana = new Panel { BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            // Sin fechas ni codigos (R4): una semana tipo, como Horario semanal.
            lienzo = new CuadriculaSemanal
            {
                Dock = DockStyle.Fill,
                SinFechas = true,
                SinCodigos = true,
                ResaltarHoy = false,
                SoloLectura = true
            };
            lienzo.SabadoVisible = true;
            lienzo.FranjaClic += f =>
            {
                ActualizarBotonesSemana();
                // Solo para verla no hay opciones: los datos salen al pasar el raton.
                if (f != null && Editable)
                {
                    MostrarMenuFranja(f);
                }
            };
            lienzo.FranjaDobleClic += f =>
            {
                if (Editable && f != null)
                {
                    CambiarHoras();
                }
            };
            lienzo.HuecosSeleccionados += rangos => MostrarMenuHueco();
            lienzo.HuecoDobleClic += (dia, hora) => PonerEligiendo(dia, hora);
            lienzo.SelectionChanged += (s, e) => ActualizarBotonesSemana();
            contenedorSemana.Controls.Add(lienzo);

            // Los menus de la cuadricula, como los de Horario semanal (sin nada de un solo dia).
            menuFranja = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuFranja.Items.Add(TextosDelHorario.CambiarHoras, null, (s, e) => CambiarHoras());
            menuFranja.Items.Add(TextosDelHorario.QuitarEstasHoras, null, (s, e) => QuitarHoras());
            menuFranja.Items.Add(new ToolStripSeparator());
            menuFranja.Items.Add("Cambiar el docente, el grupo o la materia de esta clase...", null, (s, e) => CambiarClase(CargaEnMenu()));
            menuFranja.Items.Add("Quitar esta clase del horario (todas sus horas)...", null, (s, e) =>
            {
                CargaCiclo c = CargaEnMenu();
                if (c != null)
                {
                    QuitarClases(new List<CargaCiclo> { c });
                }
            });
            menuFranja.Items.Add("Ver en la lista de clases", null, (s, e) => VerEnClases(CargaEnMenu()));
            menuFranja.Items.Add(new ToolStripSeparator());
            menuFranja.Items.Add(TextosDelHorario.PonerEligiendo, null, (s, e) => PonerEligiendo(null, null));

            menuHueco = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuHueco.Items.Add(TextosDelHorario.SinCuadrosLibres, null, (s, e) => PonerClaseEnHuecos());
            menuHueco.Items.Add(new ToolStripSeparator());
            menuHueco.Items.Add(new ToolStripMenuItem(TextosDelHorario.QuitarSeleccion, null, (s, e) =>
            {
                lienzo.LimpiarSeleccion();
                ActualizarBotonesSemana();
            }) { ShortcutKeyDisplayString = "Esc" });

            // La lista de clases (Mas acciones > "Ver la lista de clases", las pastillas y "Verlas" de Aplicar).
            lnkVolverSemana = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                LinkColor = Color.DarkGreen,
                Text = "< Volver a la semana"
            };
            lnkVolverSemana.LinkClicked += (s, e) => CambiarVista(Vista.Semana, FiltroClases.Ninguno);
            ayuda.SetToolTip(lnkVolverSemana, "A la semana por laboratorio (Esc).");
            lblDocenteFiltro = EstiloHorarios.CrearEtiqueta("Docente", Point.Empty);
            cboDocenteFiltro = EstiloHorarios.CrearCombo(Point.Empty, 250);
            cboDocenteFiltro.DrawMode = DrawMode.OwnerDrawFixed;
            cboDocenteFiltro.ItemHeight = 20;
            cboDocenteFiltro.MaxDropDownItems = 20;
            cboDocenteFiltro.DrawItem += DibujarDocenteFiltro;
            cboDocenteFiltro.SelectedIndexChanged += (s, e) =>
            {
                if (!llenando)
                {
                    PintarClases();
                }
            };
            lblBuscarClases = EstiloHorarios.CrearEtiqueta("Buscar", Point.Empty);
            txtBuscarClases = EstiloHorarios.CrearTexto(Point.Empty, 200);
            EstiloHorarios.PonerTextoGuia(txtBuscarClases, "Materia o grupo");
            ayuda.SetToolTip(txtBuscarClases, "Materia o grupo, sin importar acentos ni mayúsculas. Varias palabras: las clases que tengan todas.");
            txtBuscarClases.TextChanged += (s, e) =>
            {
                esperaBusqueda.Stop();
                esperaBusqueda.Start();
            };
            esperaBusqueda.Tick += (s, e) =>
            {
                esperaBusqueda.Stop();
                PintarClases();
            };
            pastFiltro = new PastillaConfiguracion("Filtro", 0, 26, fuentePastilla, true);
            pastFiltro.Colores(PiezasConfiguracion.FondoVerde, Color.DarkGreen, Color.DarkGreen);
            pastFiltro.Click += (s, e) => CambiarVista(Vista.Clases, FiltroClases.Ninguno);
            ayuda.SetToolTip(pastFiltro, "Clic: quitar el filtro y ver todas las clases.");

            dgvClases = EstiloHorarios.CrearGrid(new Point(Margen, 80), new Size(900, 300));
            dgvClases.MultiSelect = true;
            dgvClases.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            dgvClases.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            dgvClases.Columns.Add("Docente", "Docente");
            dgvClases.Columns.Add("Materia", "Materia");
            dgvClases.Columns.Add("Grupo", "Grupo");
            dgvClases.Columns.Add("Horario", "Horario");
            dgvClases.Columns.Add("Cambios", "Cambios");
            dgvClases.Columns.Add("Aviso", "Aviso");
            // Los avisos son frases enteras: con un peso de 14 cada fila crecia a 8 lineas.
            EstiloHorarios.Pesos(dgvClases, 15, 20, 8, 22, 11, 24);
            dgvClases.SelectionChanged += (s, e) => ActualizarBotonesClases();
            // Doble clic o Intro: cambiarla en la preparacion; solo para verla, verla en la semana.
            dgvClases.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && Editable)
                {
                    CambiarClaseElegida();
                }
                else if (e.RowIndex >= 0)
                {
                    VerElegidaEnSemana();
                }
            };
            dgvClases.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && Editable)
                {
                    e.Handled = true;
                    QuitarClasesElegidas();
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    if (Editable)
                    {
                        CambiarClaseElegida();
                    }
                    else
                    {
                        VerElegidaEnSemana();
                    }
                }
            };
            dgvClases.Paint += (s, e) => PintarVacia(dgvClases, e.Graphics, textoClasesVacia);

            // Como en Clases de Horarios (tambien en solo consulta: solo lleva a la semana).
            btnVerEnSemana = EstiloHorarios.CrearBoton("Ver en el horario", Point.Empty, new Size(160, 34));
            btnVerEnSemana.Click += (s, e) => VerElegidaEnSemana();
            ayuda.SetToolTip(btnVerEnSemana, "Lleva a la semana, al laboratorio donde la clase elegida tiene más horas, con esa clase elegida y su docente resaltado.");
            PiezasConfiguracion.AyudaSiDesactivado(ayuda, btnVerEnSemana, () => "Elige primero una clase de la lista.");

            btnNuevaClase = EstiloAdmin.BotonPrincipal("Nueva clase...", Point.Empty, new Size(150, 34));
            btnNuevaClase.Click += (s, e) => NuevaClase();
            ayuda.SetToolTip(btnNuevaClase, "Una clase nueva en este horario: docente, grupo, materia y sus horas.");
            btnCambiarClase = EstiloHorarios.CrearBoton("Cambiar...", Point.Empty, new Size(120, 34));
            btnCambiarClase.Click += (s, e) => CambiarClaseElegida();
            ayuda.SetToolTip(btnCambiarClase, "Cambia el docente, el grupo, la materia o las horas de la clase elegida (doble clic).");
            btnQuitarClases = EstiloHorarios.CrearBoton("Quitar...", Point.Empty, new Size(110, 34));
            btnQuitarClases.Click += (s, e) => QuitarClasesElegidas();
            ayuda.SetToolTip(btnQuitarClases, "Quita de este horario las clases elegidas (Supr). Lo que se usa hoy no cambia.");

            foreach (Control c in new Control[]
            {
                btnCerrarVentana, pastMarca, pastChoques, pastSinDocente, pastSinHorario, pastFaltaAsignar,
                lblLaboratorio, cboLaboratorio, btnPoner, btnCambiarHoras, btnQuitarHoras,
                btnVerTabla, btnExcel, btnImprimir, lblLeyenda, btnMas,
                panelLeyenda, lblEstadoAbierta, contenedorSemana,
                lnkVolverSemana, lblDocenteFiltro, cboDocenteFiltro, lblBuscarClases, txtBuscarClases, pastFiltro,
                dgvClases, btnVerEnSemana, btnNuevaClase, btnCambiarClase, btnQuitarClases
            })
            {
                panelAbierta.Controls.Add(c);
            }

            panelAbierta.Resize += (s, e) => AcomodarAbierta();

            // Un Excel se puede soltar sobre el horario en preparacion o sobre el paso 2 vacio.
            PermitirSoltarExcel(panelAbierta);
            PermitirSoltarExcel(panelVacio);

            // ----------------------------------------------------------- paso 3: aplicar con fechas
            panelAplicar = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Visible = false };
            aplicacion = new PestanaAplicacion(IdAdministrador, IrASemanaDeHorarios, IrAPlantillaDesdeAplicar)
            {
                IrAPreparacion = () => IrAPaso(PasoProximoCiclo.Preparacion),
                IrAPlantillas = () => IrAPaso(PasoProximoCiclo.Plantillas)
            };
            panelAplicar.Controls.Add(aplicacion);
            panelAplicar.Resize += (s, e) => AcomodarAplicar();

            Controls.Add(panelSinBase);
            Controls.Add(panelLista);
            Controls.Add(panelVacio);
            Controls.Add(panelAbierta);
            Controls.Add(panelAplicar);

            // Solo consulta: nada que escriba se ve, ni antes de la primera colocacion.
            Permisos.Ocultar(btnCrearHorario, btnRenombrar, btnBorrar, btnPoner, btnCambiarHoras, btnQuitarHoras,
                pastChoques, pastSinDocente, pastSinHorario, pastFaltaAsignar, btnNuevaClase, btnCambiarClase,
                btnQuitarClases);
            ColoresProfesor.Cambio += ColoresCambiaron;
        }

        // =====================================================================
        // Contrato con AdministrarHorarios
        // =====================================================================

        /// <summary>El nombre de la pestana: "Proximo ciclo" para la administracion; "Horarios guardados" en solo consulta.</summary>
        public static string NombrePestana
        {
            get { return Permisos.SoloConsulta ? "Horarios guardados" : Titulo; }
        }

        /// <summary>Lo que hace la pestana, para el renglon de arriba de la ventana (uno por pestana).</summary>
        public static string Frase
        {
            get
            {
                return Permisos.SoloConsulta
                    ? "Las plantillas guardadas del horario y los horarios que se aplicaron en cada ciclo."
                    : "Prepara el horario del siguiente ciclo sin tocar el que se usa y aplícalo con sus fechas. Aquí quedan las plantillas guardadas.";
            }
        }

        /// <summary>El paso que se ve (en solo consulta, siempre Plantillas).</summary>
        public PasoProximoCiclo Paso
        {
            get { return paso; }
        }

        /// <summary>
        /// El laboratorio que se mira en la preparacion cambio porque la persona
        /// eligio otro (no cuando se pone con ElegirLaboratorio): su id. Para que
        /// Horario semanal lo siga, si se quiere.
        /// </summary>
        public event Action<int> LaboratorioCambiado;

        private Func<int> IdAdministrador { get; }

        private Action<DateTime> IrASemanaDeHorarios { get; }

        /// <summary>Se puede editar lo que se ve: la plantilla en preparacion, y no en solo consulta.</summary>
        private bool Editable
        {
            get { return leido != null && leido.Ciclo.EnPreparacion && !soloConsulta; }
        }

        /// <summary>
        /// Al elegir la pestana y con F5: lee lo que hay (LeerSituacion) y ensena el
        /// paso de la ultima vez (la primera vez, la preparacion si hay una; si no,
        /// las plantillas), vuelto a leer y con lo mismo elegido.
        /// </summary>
        public void Cargar()
        {
            OlvidarSiCambioLaSesion();
            if (!LeerSituacion())
            {
                // Sin servidor: lo que ya se veia se queda, con el fallo en su linea de estado.
                if (panelVisible == null)
                {
                    MostrarLista(null);
                    Estado(falloAlLeer, true);
                }

                return;
            }

            if (FaltaBase())
            {
                MostrarSinBase();
                return;
            }

            string falloLaboratorios = LeerLaboratorios();
            paso = PasoInicial();
            switch (paso)
            {
                case PasoProximoCiclo.Preparacion:
                    MostrarPreparacion(vistaRecordada, FiltroClases.Ninguno, null);
                    break;
                case PasoProximoCiclo.Aplicar:
                    MostrarAplicar();
                    break;
                default:
                    MostrarPlantillas();
                    break;
            }

            if (falloLaboratorios != null && panelVisible != panelAplicar)
            {
                Estado(falloLaboratorios, true);
            }
        }

        /// <summary>
        /// Lleva a ese paso (la barra de pasos, "Ir a..." y los saltos de
        /// AdministrarHorarios). En solo consulta solo hay Plantillas. A
        /// Plantillas va a la lista, aunque hubiera una abierta.
        /// </summary>
        public void IrAPaso(PasoProximoCiclo nuevo)
        {
            OlvidarSiCambioLaSesion();
            if (soloConsulta)
            {
                nuevo = PasoProximoCiclo.Plantillas;
            }

            lienzo.LimpiarSeleccion();
            pasoRecordado = nuevo;
            if (nuevo == PasoProximoCiclo.Plantillas)
            {
                abiertaRecordada = null;
            }

            if (nuevo == PasoProximoCiclo.Preparacion)
            {
                vistaRecordada = Vista.Semana;
            }

            Cargar();
        }

        /// <summary>
        /// Abre una plantilla: la que esta en preparacion va al paso 2 (con un
        /// filtro, en la lista de clases con ese filtro: "Verlas" de Aplicar); una
        /// guardada o aplicada, al paso 1 solo para verla. En solo consulta nunca
        /// abre la que esta en preparacion.
        /// </summary>
        public void Abrir(int idConfiguracion, FiltroClases conFiltro)
        {
            OlvidarSiCambioLaSesion();
            if (!LeerSituacion())
            {
                if (panelVisible == null)
                {
                    MostrarLista(null);
                    Estado(falloAlLeer, true);
                }

                return;
            }

            if (FaltaBase())
            {
                MostrarSinBase();
                return;
            }

            if (laboratoriosActivos.Count == 0)
            {
                LeerLaboratorios();
            }

            if (!soloConsulta && situacion.Borrador != null && situacion.Borrador.Id == idConfiguracion)
            {
                paso = PasoProximoCiclo.Preparacion;
                pasoRecordado = paso;
                MostrarPreparacion(conFiltro == FiltroClases.Ninguno ? Vista.Semana : Vista.Clases, conFiltro, null);
                return;
            }

            paso = PasoProximoCiclo.Plantillas;
            pasoRecordado = paso;
            if (Buscar(idConfiguracion) == null || (situacion.Borrador != null && situacion.Borrador.Id == idConfiguracion))
            {
                abiertaRecordada = null;
                MostrarLista(null);
                Estado(soloConsulta ? "Esa plantilla no está entre las guardadas y las aplicadas: aquí solo se ven esas."
                    : "Esa plantilla ya no está: se borró desde otro equipo o desde otra ventana de DigLab.", true);
                return;
            }

            abiertaRecordada = null;
            MostrarLista(idConfiguracion);
            VerEnVentana(idConfiguracion);
        }

        /// <summary>
        /// Solo para VentanaVerHorario: esta pestana, sin pasos ni nada que cambie, con esa plantilla
        /// o ese horario aplicado. No toca lo recordado de la pestana de verdad. Devuelve lo que se
        /// abrio; si no se puede leer, deja pasar el error para que lo diga quien la abre.
        /// </summary>
        internal CicloPreliminar AbrirSoloParaVer(int idConfiguracion)
        {
            // Primero esto: sin servidor, falla una sola vez. Si en otra ventana se deshizo y
            // volvio a preparacion, aqui no: esta vista es solo para ver.
            CicloLeido nuevo = datos.Leer(idConfiguracion);
            if (nuevo.Ciclo.EnPreparacion)
            {
                throw new ErrorDeDatos("«" + nuevo.Ciclo.Nombre + "» volvió a «2. Preparación».");
            }

            enVentana = true;
            btnCerrarVentana.Visible = true;
            LeerLaboratorios();
            leido = nuevo;
            revision = RevisionCiclo.Revisar(leido);
            paso = PasoProximoCiclo.Plantillas;
            vista = Vista.Semana;
            filtro = FiltroClases.Ninguno;
            Mostrar(panelAbierta);
            PintarAbierta();
            Estado(null);
            return leido.Ciclo;
        }

        /// <summary>El boton "Cerrar" de VentanaVerHorario (su CancelButton).</summary>
        internal Button BotonCerrarVentana
        {
            get { return btnCerrarVentana; }
        }

        /// <summary>Olvida la plantilla guardada que se tenia abierta: el siguiente Cargar del paso 1 ensena la lista.</summary>
        public void OlvidarAbierta()
        {
            abiertaRecordada = null;
        }

        /// <summary>
        /// El laboratorio que se mira en la preparacion (y en una plantilla
        /// abierta), si esta en su lista; si no, se recuerda para cuando se abra.
        /// No dispara LaboratorioCambiado.
        /// </summary>
        public void ElegirLaboratorio(int idLaboratorio)
        {
            if (idLaboratorio <= 0)
            {
                return;
            }

            laboratorioRecordado = idLaboratorio;
            if (leido == null || panelVisible != panelAbierta)
            {
                laboratorioElegido = idLaboratorio;
                return;
            }

            ItemLaboratorio item = cboLaboratorio.Items.Cast<ItemLaboratorio>().FirstOrDefault(i => i.Id == idLaboratorio);
            if (item == null || item.Id == laboratorioElegido)
            {
                return;
            }

            eligiendoDesdeFuera = true;
            try
            {
                cboLaboratorio.SelectedItem = item;
            }
            finally
            {
                eligiendoDesdeFuera = false;
            }
        }

        /// <summary>
        /// Esc: vuelve atras dentro de la pestana y devuelve true; false si no hay
        /// a donde (y Esc cierra la ventana). Con cuadros o una clase elegidos en
        /// la semana, primero los suelta (luego olvida la clase traida de "Falta
        /// asignar"); una caja de busqueda con texto, la
        /// borra; la lista de clases vuelve a la semana; una plantilla abierta
        /// solo para verla, a la lista; el paso 3, al paso 2.
        /// </summary>
        public bool ManejarEscape()
        {
            if (panelVisible == panelLista)
            {
                if (txtBuscarLista.Focused && txtBuscarLista.TextLength > 0)
                {
                    txtBuscarLista.Clear();
                    return true;
                }

                return false;
            }

            if (panelVisible == panelAplicar && !soloConsulta)
            {
                IrAPaso(PasoProximoCiclo.Preparacion);
                return true;
            }

            if (panelVisible != panelAbierta || leido == null)
            {
                return false;
            }

            if (vista == Vista.Clases)
            {
                if (txtBuscarClases.Focused && txtBuscarClases.TextLength > 0)
                {
                    txtBuscarClases.Clear();
                    return true;
                }

                CambiarVista(Vista.Semana, FiltroClases.Ninguno);
                return true;
            }

            if (lienzo.SelectedCells.Cast<DataGridViewCell>().Any(c => c.ColumnIndex >= 1) || lienzo.Seleccionada != null)
            {
                lienzo.LimpiarSeleccion();
                ActualizarBotonesSemana();
                return true;
            }

            // Sin nada elegido, Esc olvida la clase traida de "Falta asignar" (como Horario semanal).
            if (porPoner.HasValue)
            {
                porPoner = null;
                AcomodarAbierta();
                Estado(null);
                return true;
            }

            if (!leido.Ciclo.EnPreparacion)
            {
                VolverALista();
                return true;
            }

            return false;
        }

        // =====================================================================
        // Lectura y lo comun
        // =====================================================================

        /// <summary>
        /// Lo recordado es de una sesion: si ahora es otra cuenta (otra persona
        /// entro en el mismo proceso tras cerrar sesion), se olvida.
        /// </summary>
        private static void OlvidarSiCambioLaSesion()
        {
            string sesion = (Datos_User.getUser() ?? "") + "|" + Datos_User.TipoUsuario;
            if (!string.Equals(sesionRecordada, sesion, StringComparison.Ordinal))
            {
                sesionRecordada = sesion;
                pasoRecordado = null;
                abiertaRecordada = null;
                vistaRecordada = Vista.Semana;
                laboratorioRecordado = 0;
            }
        }

        /// <summary>El paso que toca: el recordado; la primera vez, la preparacion si hay una, si no las plantillas.</summary>
        private PasoProximoCiclo PasoInicial()
        {
            if (soloConsulta)
            {
                return PasoProximoCiclo.Plantillas;
            }

            if (pasoRecordado.HasValue)
            {
                return pasoRecordado.Value;
            }

            return situacion != null && situacion.Borrador != null ? PasoProximoCiclo.Preparacion : PasoProximoCiclo.Plantillas;
        }

        protected override void Dispose(bool disposing)
        {
            List<DrawingFont> letras = new List<DrawingFont>();
            if (disposing)
            {
                ColoresProfesor.Cambio -= ColoresCambiaron;
                esperaBusqueda.Stop();
                esperaBusqueda.Dispose();
                ayuda.Dispose();
                // Las letras de cada etiqueta y de los menus: se juntan antes de que se
                // suelten los controles. La barra de pasos va dentro del panel que se ve.
                letras = PiezasConfiguracion.LetrasDe(this, null);
                foreach (DrawingFont letra in new[] { menuFranja.Font, menuHueco.Font, menuMas.Font })
                {
                    if (!letras.Any(l => ReferenceEquals(l, letra)))
                    {
                        letras.Add(letra);
                    }
                }

                menuFranja.Dispose();
                menuHueco.Dispose();
                menuMas.Dispose();
                if (barraPasos.Parent == null)
                {
                    barraPasos.Dispose();
                }
            }

            // Primero los controles, que usan estas letras.
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (DrawingFont letra in letras)
                {
                    if (!ReferenceEquals(letra, fuentePastilla) && !ReferenceEquals(letra, fuenteChip) && !ReferenceEquals(letra, fuenteBase))
                    {
                        letra.Dispose();
                    }
                }

                fuentePastilla.Dispose();
                fuenteChip.Dispose();
                fuenteVacia.Dispose();
                fuenteNegrita.Dispose();
                fuenteBase.Dispose();
            }
        }

        private void ColoresCambiaron()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(ColoresCambiaron));
                }
                catch (InvalidOperationException)
                {
                    // La ventana se cerro entre tanto: no hay nada que repintar.
                }

                return;
            }

            foreach (Control chip in panelLeyenda.Controls)
            {
                if (chip.Tag is int)
                {
                    chip.BackColor = ColoresProfesor.De((int)chip.Tag);
                }
            }

            lienzo.Invalidate();
            cboDocenteFiltro.Invalidate();
            if (vista == Vista.Clases && panelVisible == panelAbierta && leido != null)
            {
                PintarClases();
            }
        }

        /// <summary>La plantilla con ese id en lo ultimo que se leyo (guardadas, aplicadas y la que esta en preparacion), o null.</summary>
        private CicloPreliminar Buscar(int id)
        {
            if (situacion == null)
            {
                return null;
            }

            if (situacion.Borrador != null && situacion.Borrador.Id == id)
            {
                return situacion.Borrador;
            }

            return situacion.Configuraciones.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// Por que ya no se ve la que estaba abierta. En solo consulta no se nombra:
        /// pudo pasar a preparacion (se deshizo su aplicacion), y esa no se ensena.
        /// </summary>
        private string TextoYaNoEsta(CicloPreliminar conocida)
        {
            if (soloConsulta)
            {
                return "La plantilla que mirabas ya no está entre las guardadas y las aplicadas. Elige otra de la lista.";
            }

            if (conocida == null)
            {
                return "La plantilla que tenías abierta ya no está: se borró o se descartó desde otro equipo o desde otra ventana de DigLab.";
            }

            return conocida.EnPreparacion
                ? "«" + conocida.Nombre + "» ya no está en preparación: se descartó desde otro equipo o desde otra ventana de DigLab. "
                  + "Si lo necesitas, prepáralo otra vez con «Crear horario nuevo...»."
                : "«" + conocida.Nombre + "» ya no está: se borró desde otro equipo o desde otra ventana de DigLab.";
        }

        /// <summary>
        /// Lee la situacion. Si falla, se queda la de antes (lo que se ve no se
        /// vacia), lo dice y deja el porque en falloAlLeer.
        /// </summary>
        private bool LeerSituacion()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                situacion = datos.LeerSituacion();
                falloAlLeer = null;
                return true;
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                falloAlLeer = AvisoDeFalloAdmin.Texto("No se pudieron leer las plantillas", ex);
                MostrarFallo("PestanaProximoCiclo.LeerSituacion", "No se pudieron leer las plantillas", ex);
                return false;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// Falta la 16 o la 23 (o esta cuenta no ve sus tablas) o, para la
        /// administracion, la 09. La persona responsable solo consulta: sin la 09
        /// tambien ve lo guardado (la 09 hace falta para aplicar).
        /// </summary>
        private bool FaltaBase()
        {
            return situacion != null && (situacion.SinTablas || situacion.SinGuardados || (situacion.SinCodigoPorProfesor && !soloConsulta));
        }

        /// <summary>
        /// Los laboratorios activos (para los dialogos y el combo de la semana).
        /// Si no se pudieron leer, el porque (para la linea de estado); si no, null.
        /// </summary>
        private string LeerLaboratorios()
        {
            try
            {
                laboratoriosActivos = horarios.ObtenerLaboratoriosActivos();
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("PestanaProximoCiclo: leer los laboratorios activos", ex);
                laboratoriosActivos = new List<OpcionCombo>();
                return AvisoDeFalloAdmin.Texto("No se pudo leer la lista de laboratorios", ex);
            }
        }

        /// <summary>Ensena ese panel y le pasa la barra de pasos (salvo "Falta preparar la base" y en solo consulta).</summary>
        private void Mostrar(Panel panel)
        {
            SuspendLayout();
            panelVisible = panel;
            bool conBarra = !soloConsulta && !enVentana && panel != panelSinBase;
            if (conBarra && barraPasos.Parent != panel)
            {
                if (barraPasos.Parent != null)
                {
                    barraPasos.Parent.Controls.Remove(barraPasos);
                }

                panel.Controls.Add(barraPasos);
            }

            barraPasos.Visible = conBarra;
            barraPasos.Location = new Point(Margen, 6);

            PintarPasos();
            foreach (Panel p in new[] { panelSinBase, panelLista, panelVacio, panelAbierta, panelAplicar })
            {
                p.Visible = p == panel;
            }

            ResumeLayout();
        }

        /// <summary>La pastilla del paso que se ve, en azul grisaceo oscuro con letra blanca; las demas, en claro.</summary>
        private void PintarPasos()
        {
            foreach (PastillaConfiguracion pastilla in new[] { pasoPlantillas, pasoPreparacion, pasoAplicar })
            {
                bool elegida = (pastilla == pasoPlantillas && paso == PasoProximoCiclo.Plantillas)
                    || (pastilla == pasoPreparacion && paso == PasoProximoCiclo.Preparacion)
                    || (pastilla == pasoAplicar && paso == PasoProximoCiclo.Aplicar);
                if (elegida)
                {
                    pastilla.Colores(AzulOscuro, AzulOscuro, Color.White);
                }
                else
                {
                    pastilla.Colores(AzulClaro, BordeAzulClaro, AzulOscuro);
                }

                pastilla.AccessibleDescription = elegida ? "Paso elegido" : null;
            }

            CicloPreliminar borrador = situacion == null ? null : situacion.Borrador;
            ayuda.SetToolTip(pasoPreparacion, borrador == null
                ? "Todavía no hay un horario en preparación: créalo como copia de otro, solo con sus clases o en blanco."
                : "«" + borrador.Nombre + "»: " + RevisionCiclo.Resumen(borrador) + ". Se ve y se cambia como Horario semanal; nada se usa hasta aplicarlo.");
        }

        private void AcomodarBarra()
        {
            pasoPreparacion.Location = new Point(pasoPlantillas.Right + 6, 0);
            pasoAplicar.Location = new Point(pasoPreparacion.Right + 6, 0);
            barraPasos.Size = new Size(pasoAplicar.Right + 2, 30);
        }

        /// <summary>
        /// Lo que no se pudo hacer, por que y que hacer (AvisoDeFalloAdmin), y en
        /// rojo en la linea de estado. Al volver a leer tras otro fallo
        /// (callarFallos), solo la linea de estado y el registro.
        /// </summary>
        private void MostrarFallo(string contexto, string queNoSePudo, Exception ex)
        {
            if (callarFallos)
            {
                if (ex is ErrorDeDatos || ex is AccionNoPermitida)
                {
                    Log.Aviso(contexto + ": " + ex.Message);
                }
                else
                {
                    Log.Error(contexto, ex);
                }
            }
            else
            {
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), contexto, ex, queNoSePudo, Titulo);
            }

            Estado(AvisoDeFalloAdmin.Texto(queNoSePudo, ex), true);
        }

        /// <summary>
        /// Fallo una accion que escribe (otra persona de la administracion pudo
        /// cambiar o borrar lo mismo, o no hubo servidor): lo dice con su causa y
        /// que hacer, y vuelve a leer para no seguir con datos viejos.
        /// </summary>
        private void FalloAlEscribir(string contexto, string queNoSePudo, Exception ex)
        {
            MostrarFallo(contexto, queNoSePudo, ex);
            VolverALeerTrasFallo(AvisoDeFalloAdmin.Texto(queNoSePudo, ex));
        }

        /// <summary>
        /// Vuelve a leer lo que se ve tras un fallo (aqui o dentro de un dialogo),
        /// sin otro aviso: la abierta (si ya no esta, la lista con el porque) o la lista.
        /// </summary>
        private void VolverALeerTrasFallo(string estado)
        {
            bool antes = callarFallos;
            callarFallos = true;
            try
            {
                if (panelVisible == panelAbierta && leido != null)
                {
                    Releer(estado, estado != null);
                }
                else if (panelVisible == panelVacio)
                {
                    Cargar();
                    Estado(estado, estado != null);
                }
                else
                {
                    CargarLista(null, estado, estado != null);
                }
            }
            finally
            {
                callarFallos = antes;
            }
        }

        /// <summary>LeerSituacion sin otro aviso (ya se dijo lo que fallo): el porque, solo en la linea de estado.</summary>
        private bool LeerSituacionSinAviso()
        {
            bool antes = callarFallos;
            callarFallos = true;
            try
            {
                return LeerSituacion();
            }
            finally
            {
                callarFallos = antes;
            }
        }

        /// <summary>
        /// La linea de estado de lo que se ve: verde, o roja si algo fallo. En una
        /// plantilla abierta, sin nada que decir, el resumen de lo que tiene en gris.
        /// </summary>
        private void Estado(string texto, bool error = false)
        {
            Label linea = panelVisible == panelAbierta ? lblEstadoAbierta : panelVisible == panelVacio ? lblEstadoVacio : lblEstadoLista;
            if (linea == lblEstadoAbierta && string.IsNullOrEmpty(texto))
            {
                linea.Text = detalleAbierta;
                linea.ForeColor = PiezasConfiguracion.TextoGris;
            }
            else
            {
                linea.Text = texto ?? "";
                linea.ForeColor = error ? PiezasConfiguracion.TextoError : Color.DarkGreen;
            }

            ayuda.SetToolTip(linea, linea.Text);
            AlinearEstado();
        }

        /// <summary>
        /// La linea de estado de la semana va a la derecha de los docentes; si no
        /// cabe entera, empieza por la izquierda (los "..." al final, no al principio).
        /// </summary>
        private void AlinearEstado()
        {
            if (vista != Vista.Semana)
            {
                lblEstadoAbierta.TextAlign = ContentAlignment.MiddleLeft;
                return;
            }

            bool cabe = TextRenderer.MeasureText(lblEstadoAbierta.Text, lblEstadoAbierta.Font).Width <= lblEstadoAbierta.Width;
            lblEstadoAbierta.TextAlign = cabe ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
        }

        /// <summary>
        /// El alto de la linea de estado de una plantilla abierta: dos renglones, o
        /// los que pida ese texto en ese ancho, hasta "maximo" (lo que no quepa, con "...").
        /// </summary>
        private int AltoDelEstado(string texto, int ancho, int maximo)
        {
            // El renglon como lo mide TextRenderer (17 a 9.5 pt; Font.Height dice 16).
            int altoRenglon = TextRenderer.MeasureText("Ag", lblEstadoAbierta.Font).Height;
            int pide = string.IsNullOrEmpty(texto) ? 0 : TextRenderer.MeasureText(texto, lblEstadoAbierta.Font, new Size(Math.Max(1, ancho), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
            int renglones = Math.Max(2, Math.Min(maximo, (pide + altoRenglon - 1) / altoRenglon));
            return renglones * altoRenglon + 2;
        }

        /// <summary>Sin filas: el texto centrado (que hay que hacer o por que no hay ninguna).</summary>
        private void PintarVacia(DataGridView grid, Graphics g, string texto)
        {
            if (grid.Rows.Count > 0 || string.IsNullOrEmpty(texto))
            {
                return;
            }

            Rectangle zona = new Rectangle(24, grid.ColumnHeadersHeight + 16, Math.Max(10, grid.ClientSize.Width - 48),
                Math.Max(10, grid.ClientSize.Height - grid.ColumnHeadersHeight - 32));
            TextRenderer.DrawText(g, texto, fuenteVacia, zona, Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        // =====================================================================
        // Sin base
        // =====================================================================

        private void MostrarSinBase()
        {
            abiertaRecordada = null;
            List<string> textos = new List<string>();
            if (situacion.SinTablas || situacion.SinGuardados)
            {
                textos.Add(soloConsulta
                    ? "Para ver los horarios guardados, quien administra el servidor tiene que aplicar db/23_horarios_guardados.sql con root."
                    : "Para preparar y aplicar horarios, la base necesita las actualizaciones db/16_ciclo_preliminar.sql y db/23_horarios_guardados.sql. "
                      + "Crean tablas nuevas y no cambian nada de lo que ya hay. Las aplica quien administra el servidor con deploy\\migrar_bd.ps1.");
            }

            if (situacion.SinCodigoPorProfesor && !soloConsulta)
            {
                textos.Add("Para aplicar horarios, la base necesita el código por docente (db/09_codigo_por_profesor.sql).");
            }

            lblSinBaseTexto.Text = string.Join(Environment.NewLine + Environment.NewLine, textos);
            int alto = Math.Max(40, PiezasConfiguracion.AltoDe(lblSinBaseTexto.Text, lblSinBaseTexto.Font, lblSinBaseTexto.Width) + 4);
            lblSinBaseTexto.Height = alto;
            btnRevisar.Top = lblSinBaseTexto.Bottom + 12;
            tarjetaSinBase.Height = Math.Max(170, btnRevisar.Bottom + 18);
            Mostrar(panelSinBase);
            AcomodarSinBase();
        }

        private void AcomodarSinBase()
        {
            int ancho = panelSinBase.ClientSize.Width;
            int alto = panelSinBase.ClientSize.Height;
            tarjetaSinBase.Location = new Point(Math.Max(Margen, (ancho - tarjetaSinBase.Width) / 2), Math.Max(Margen, (alto - tarjetaSinBase.Height) / 2 - 40));
        }

        // =====================================================================
        // Paso 1: las plantillas
        // =====================================================================

        /// <summary>El paso 1: la plantilla que se tenia abierta para verla (vuelta a leer) o la lista.</summary>
        private void MostrarPlantillas()
        {
            paso = PasoProximoCiclo.Plantillas;
            if (abiertaRecordada.HasValue)
            {
                int id = abiertaRecordada.Value;
                bool misma = leido != null && leido.Ciclo.Id == id && panelVisible == panelAbierta;
                CicloPreliminar ahora = Buscar(id);
                if (ahora == null || ahora.EnPreparacion)
                {
                    CicloPreliminar conocida = misma ? leido.Ciclo : null;
                    abiertaRecordada = null;
                    leido = null;
                    MostrarLista(null);
                    Estado(TextoYaNoEsta(conocida), true);
                    return;
                }

                if (misma)
                {
                    Releer(lblEstadoAbierta.ForeColor == PiezasConfiguracion.TextoError ? lblEstadoAbierta.Text : null,
                        lblEstadoAbierta.ForeColor == PiezasConfiguracion.TextoError);
                }
                else
                {
                    AbrirConfiguracion(id, vistaRecordada, FiltroClases.Ninguno, null);
                }

                return;
            }

            MostrarLista(panelVisible == panelAbierta && leido != null && !leido.Ciclo.EnPreparacion ? leido.Ciclo.Id : (int?)null);
        }

        /// <summary>Vuelve a leer (sin la abierta) y ensena la lista, con esa plantilla elegida.</summary>
        private void CargarLista(int? elegir, string estado, bool error = false)
        {
            abiertaRecordada = null;
            paso = PasoProximoCiclo.Plantillas;
            pasoRecordado = paso;
            if (!LeerSituacion())
            {
                MostrarLista(elegir);
                Estado(string.IsNullOrEmpty(estado) ? falloAlLeer : estado + " " + falloAlLeer, true);
                return;
            }

            if (FaltaBase())
            {
                MostrarSinBase();
                return;
            }

            MostrarLista(elegir);
            Estado(estado, error);
        }

        /// <summary>Ensena la lista con lo ultimo que se leyo (no lee ni olvida la abierta: eso es de quien llama).</summary>
        private void MostrarLista(int? elegir)
        {
            paso = PasoProximoCiclo.Plantillas;
            Mostrar(panelLista);
            PintarLista(elegir);
            AcomodarLista();
            lblEstadoLista.Text = "";
            if (!txtBuscarLista.Focused)
            {
                dgvLista.Focus();
            }
        }

        private void PintarLista(int? elegir)
        {
            CicloPreliminar antes = ElegidaEnLista();
            int? buscada = elegir ?? (antes == null ? (int?)null : antes.Id);
            DateTime hoy = situacion == null ? DateTime.Today : situacion.Hoy;
            List<CicloPreliminar> todas = situacion == null ? new List<CicloPreliminar>() : situacion.Configuraciones;
            string busqueda = txtBuscarLista.Text;
            List<CicloPreliminar> vistas = todas
                .Where(c => cboQueLista.SelectedIndex <= 0 || (cboQueLista.SelectedIndex == 1 ? c.Guardada : c.Aplicada))
                .Where(c => EstiloAdmin.Coincide(EstiloAdmin.Normalizar(c.Nombre), busqueda))
                .ToList();

            dgvLista.Rows.Clear();
            foreach (CicloPreliminar c in vistas)
            {
                string queEs = c.Guardada ? "Plantilla guardada"
                    : c.EnUso(hoy) ? "Aplicado, en uso"
                    : c.PorEmpezar(hoy) ? "Aplicado, empieza el " + PiezasConfiguracion.Fecha(c.Desde.Value)
                    : "Aplicado, terminó";
                string fechas = c.Aplicada
                    ? (c.Desde.HasValue && c.Hasta.HasValue ? "Del " + PiezasConfiguracion.Fecha(c.Desde.Value) + " al " + PiezasConfiguracion.Fecha(c.Hasta.Value) : "")
                      + (c.CortadaEl.HasValue ? ", terminó el " + PiezasConfiguracion.Fecha(c.CortadaEl.Value) : "")
                    : c.CopiadoDel.HasValue ? "Horario del " + PiezasConfiguracion.Fecha(c.CopiadoDel.Value) : "";
                string guardada = c.Aplicada
                    ? (c.PublicadoEn.HasValue ? "Aplicado el " + PiezasConfiguracion.Fecha(c.PublicadoEn.Value) : "Aplicado") + (string.IsNullOrEmpty(c.PublicadoPor) ? "" : ", " + c.PublicadoPor)
                    : PiezasConfiguracion.Fecha(c.CambiadoEn ?? c.CreadoEn) + " " + PiezasConfiguracion.Hora(c.CambiadoEn ?? c.CreadoEn)
                      + (string.IsNullOrEmpty(c.CambiadoPor) ? "" : ", " + c.CambiadoPor);
                int i = dgvLista.Rows.Add(c.Nombre, queEs, c.Docentes, c.Clases, PiezasConfiguracion.NumeroDeHoras(c.Horas), c.Laboratorios, fechas, guardada);
                DataGridViewRow fila = dgvLista.Rows[i];
                fila.Tag = c;
                if (c.EnUso(hoy))
                {
                    fila.Cells["QueEs"].Style.ForeColor = Color.DarkGreen;
                    fila.Cells["QueEs"].Style.Font = fuenteNegrita;
                }
                else if (c.PorEmpezar(hoy))
                {
                    fila.Cells["QueEs"].Style.ForeColor = PiezasConfiguracion.TextoAmbar;
                }
            }

            textoListaVacia = situacion == null && falloAlLeer != null ? falloAlLeer
                : todas.Count == 0
                ? (soloConsulta
                    ? "Todavía no hay horarios guardados ni aplicados."
                    : "Todavía no hay plantillas guardadas. Pulsa «Crear horario nuevo...» para preparar uno; al aplicarlo, el que se usa ahora se guarda aquí.")
                : "Ninguna plantilla coincide con lo que buscas.";
            dgvLista.ClearSelection();
            if (buscada.HasValue)
            {
                EstiloHorarios.SeleccionarFila(dgvLista, f => ((CicloPreliminar)f.Tag).Id == buscada.Value);
            }

            if (dgvLista.SelectedRows.Count == 0 && dgvLista.Rows.Count > 0)
            {
                EstiloHorarios.SeleccionarFila(dgvLista, f => true);
            }

            dgvLista.Invalidate();
            ActualizarBotonesLista();
        }

        private CicloPreliminar ElegidaEnLista()
        {
            return dgvLista.SelectedRows.Count == 0 ? null : dgvLista.SelectedRows[0].Tag as CicloPreliminar;
        }

        private void ActualizarBotonesLista()
        {
            CicloPreliminar elegida = ElegidaEnLista();
            btnAbrir.Enabled = elegida != null;
            btnRenombrar.Enabled = elegida != null;
            btnBorrar.Enabled = elegida != null && elegida.Guardada;
        }

        private void AcomodarLista()
        {
            int ancho = panelLista.ClientSize.Width;
            int alto = panelLista.ClientSize.Height;
            if (ancho < 400 || alto < 200)
            {
                return;
            }

            btnCrearHorario.Visible = !soloConsulta;
            btnCrearHorario.Location = new Point(ancho - Margen - btnCrearHorario.Width, 4);
            lblListaTexto.SetBounds(Margen, 42, ancho - 2 * Margen, 20);
            lblGuardadas.Location = new Point(Margen, 76);
            txtBuscarLista.Location = new Point(lblGuardadas.Right + 8, 72);
            cboQueLista.Location = new Point(txtBuscarLista.Right + 8, 72);

            int pie = alto - AltoPie;
            dgvLista.SetBounds(Margen, 104, ancho - 2 * Margen, pie - 104 - 4);
            int y = pie + (AltoPie - btnAbrir.Height) / 2 + 1;
            int x = ancho - Margen;
            foreach (Button boton in new[] { btnBorrar, btnRenombrar, btnAbrir })
            {
                boton.Visible = !soloConsulta || boton == btnAbrir;
                if (!boton.Visible)
                {
                    continue;
                }

                boton.Location = new Point(x - boton.Width, y);
                x = boton.Left - 8;
            }

            lblEstadoLista.SetBounds(Margen, pie + 10, Math.Max(100, x - 2 * Margen), 20);
        }

        private void AbrirElegida()
        {
            CicloPreliminar elegida = ElegidaEnLista();
            if (elegida != null)
            {
                // Abrir vuelve a leer: la lista pudo quedar vieja (la borraron o se deshizo su aplicacion).
                Abrir(elegida.Id, FiltroClases.Ninguno);
            }
        }

        /// <summary>
        /// Una plantilla o un horario aplicado, solo para verlo, en su propia ventana: sin pestanas
        /// ni pasos (pedido del usuario, 2026-11-04). La lista se queda detras.
        /// </summary>
        private void VerEnVentana(int id)
        {
            VentanaVerHorario ventana;
            Cursor = Cursors.WaitCursor;
            try
            {
                ventana = new VentanaVerHorario(id, IdAdministrador);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                if (ex is ErrorDeDatos || ex is AccionNoPermitida)
                {
                    Log.Aviso("PestanaProximoCiclo.VerEnVentana: " + ex.Message);
                    CargarLista(null, TextoYaNoEsta(Buscar(id)), true);
                }
                else
                {
                    MostrarFallo("PestanaProximoCiclo.VerEnVentana", "No se pudo abrir la plantilla", ex);
                }

                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            using (ventana)
            {
                ventana.ShowDialog(EstiloHorarios.Dueno(this));
            }
        }

        // =====================================================================
        // Paso 2: la preparacion
        // =====================================================================

        /// <summary>El paso 2: la plantilla en preparacion en esa vista; sin ninguna, la tarjeta para empezar una.</summary>
        private void MostrarPreparacion(Vista enVista, FiltroClases conFiltro, string estado, bool error = false)
        {
            paso = PasoProximoCiclo.Preparacion;
            CicloPreliminar borrador = situacion == null ? null : situacion.Borrador;
            if (borrador == null)
            {
                bool estabaEnLa = panelVisible == panelAbierta && leido != null && leido.Ciclo.EnPreparacion;
                CicloPreliminar conocida = estabaEnLa ? leido.Ciclo : null;
                leido = null;
                MostrarVacio();
                if (estado != null)
                {
                    Estado(estado, error);
                }
                else if (conocida != null)
                {
                    CicloPreliminar aplicada = situacion == null ? null : situacion.Configuraciones.FirstOrDefault(c => c.Id == conocida.Id);
                    Estado(aplicada != null && aplicada.Aplicada
                        ? "«" + aplicada.Nombre + "» ya se aplicó: está en «1. Plantillas» como horario aplicado y ya no se cambia aquí."
                        : TextoYaNoEsta(conocida), aplicada == null);
                }

                return;
            }

            bool misma = leido != null && leido.Ciclo.Id == borrador.Id && panelVisible == panelAbierta;
            if (misma && enVista == vista && conFiltro == FiltroClases.Ninguno && estado == null)
            {
                Releer(null);
                return;
            }

            AbrirConfiguracion(borrador.Id, enVista, conFiltro, estado, error);
        }

        /// <summary>
        /// El paso 2 sin nada en preparacion: como empezar uno o, con una aplicada
        /// que aun no empieza, que no se puede preparar otro hasta que empiece.
        /// </summary>
        private void MostrarVacio()
        {
            CicloPreliminar porEmpezar = situacion == null ? null : situacion.PorEmpezar;
            if (porEmpezar != null)
            {
                tarjetaVacio.Pintar(PiezasConfiguracion.FondoAmbar, PiezasConfiguracion.BordeAmbar, 2);
                lblVacioTitulo.Text = "«" + porEmpezar.Nombre + "» ya está aplicado";
                lblVacioTexto.Text = "Empieza el " + (porEmpezar.Desde.HasValue ? PiezasConfiguracion.DiaYFecha(porEmpezar.Desde.Value) : "día que se le puso")
                    + ". Para preparar otro, espera a que empiece o deshaz su aplicación en «3. Aplicar con fechas».";
            }
            else
            {
                tarjetaVacio.Pintar(Color.White, AzulOscuro, 2);
                lblVacioTitulo.Text = "Todavía no hay un horario en preparación";
                lblVacioTexto.Text = "Empieza con una copia de otro horario, solo con sus clases o en blanco: luego cambias lo que cambie. "
                    + "Nada cambia en los laboratorios hasta que lo apliques en «3. Aplicar con fechas».";
            }

            lblVacioTitulo.ForeColor = porEmpezar != null ? PiezasConfiguracion.TextoAmbar : AzulOscuro;
            btnPrepararVacio.Visible = porEmpezar == null;
            btnIrAplicarVacio.Visible = porEmpezar != null;
            lblEstadoVacio.Text = "";
            Mostrar(panelVacio);
            AcomodarVacio();
        }

        private void AcomodarVacio()
        {
            int ancho = panelVacio.ClientSize.Width;
            int alto = panelVacio.ClientSize.Height;
            int w = Math.Max(360, Math.Min(680, ancho - 2 * Margen));
            lblVacioTitulo.Width = w - 48;
            lblVacioTexto.Width = w - 48;
            lblVacioTexto.Height = Math.Max(40, PiezasConfiguracion.AltoDe(lblVacioTexto.Text, lblVacioTexto.Font, lblVacioTexto.Width) + 4);
            btnPrepararVacio.Top = lblVacioTexto.Bottom + 14;
            btnIrAplicarVacio.Top = lblVacioTexto.Bottom + 14;
            tarjetaVacio.Size = new Size(w, Math.Max(150, btnPrepararVacio.Bottom + 20));
            tarjetaVacio.Location = new Point(Math.Max(Margen, (ancho - w) / 2), Math.Max(50, (alto - tarjetaVacio.Height) / 2 - 40));
            lblEstadoVacio.SetBounds(tarjetaVacio.Left, tarjetaVacio.Bottom + 10, w, 40);
        }

        // =====================================================================
        // Paso 3: aplicar con fechas
        // =====================================================================

        private void MostrarAplicar()
        {
            paso = PasoProximoCiclo.Aplicar;
            Mostrar(panelAplicar);
            AcomodarAplicar();
            aplicacion.Cargar();
        }

        private void AcomodarAplicar()
        {
            int ancho = panelAplicar.ClientSize.Width;
            int alto = panelAplicar.ClientSize.Height;
            int arriba = soloConsulta ? 0 : 40;
            aplicacion.SetBounds(0, arriba, Math.Max(100, ancho), Math.Max(100, alto - arriba));
        }

        /// <summary>
        /// Los saltos de Aplicar ("Verlas", "< Volver a la preparacion", "Verlo en
        /// <<1. Plantillas>>", la tabla de los aplicados): con un id, esa plantilla
        /// (la que esta en preparacion, en el paso 2); sin id, la lista.
        /// </summary>
        private void IrAPlantillaDesdeAplicar(int? id, FiltroClases conFiltro)
        {
            if (!id.HasValue)
            {
                IrAPaso(PasoProximoCiclo.Plantillas);
                return;
            }

            Abrir(id.Value, conFiltro);
        }

        // =====================================================================
        // Crear un horario, cambiar el nombre, borrar, guardar como, guardar en
        // su sitio y descartar
        // =====================================================================

        /// <summary>
        /// "Crear horario nuevo...": el horario del proximo ciclo, como copia completa, solo
        /// con las clases o en blanco (DialogoCrearHorario). Queda en preparacion.
        /// </summary>
        /// <param name="desde">La plantilla que se propone (la elegida en la lista), o null.</param>
        /// <param name="excel">Un Excel soltado en el paso 2 vacio: el dialogo abre con "Desde un Excel" y ese archivo.</param>
        private void CrearHorario(int? desde, string excel = null)
        {
            if (Permisos.Bloquear(this, "Crear un horario nuevo"))
            {
                return;
            }

            // Lo de ahora: otra ventana pudo aplicar o preparar uno.
            if (!LeerSituacion())
            {
                return;
            }

            if (situacion.PorEmpezar != null)
            {
                MessageBox.Show(EstiloHorarios.Dueno(this), "«" + situacion.PorEmpezar.Nombre + "» ya está aplicado y empieza el "
                    + (situacion.PorEmpezar.Desde.HasValue ? PiezasConfiguracion.DiaYFecha(situacion.PorEmpezar.Desde.Value) : "día que se le puso")
                    + ". Para crear otro, espera a que empiece o deshaz su aplicación en «3. Aplicar con fechas».",
                    "Crear horario nuevo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (panelVisible == panelLista)
                {
                    PintarLista(null);
                }

                return;
            }

            // La que se pidio copiar pudo borrarla otra ventana: no se propone otra sin decirlo.
            if (desde.HasValue && !situacion.Configuraciones.Any(c => c.Id == desde.Value))
            {
                MessageBox.Show(EstiloHorarios.Dueno(this),
                    situacion.Borrador != null && situacion.Borrador.Id == desde.Value
                        ? "«" + situacion.Borrador.Nombre + "» ya no está aplicado: se deshizo su aplicación desde otro equipo o desde otra ventana de DigLab y volvió a «2. Preparación»."
                        : "Esa plantilla ya no está: se borró desde otro equipo o desde otra ventana de DigLab. Elige otra.",
                    "Crear horario nuevo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (panelVisible == panelLista)
                {
                    PintarLista(null);
                }

                return;
            }

            ResultadoEmpezar resultado;
            string deDonde;
            bool sinHoras;
            ResultadoCargaExcel conExcel;
            string falloExcel;
            string archivoExcel;
            using (DialogoCrearHorario dialogo = new DialogoCrearHorario(datos, situacion.Hoy, situacion.Configuraciones,
                situacion.Borrador, desde, IdAdministrador, excel))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    if (dialogo.HuboFallo)
                    {
                        // Pudo preparar otro alguien de la administracion, o aplicarlo: lo de ahora.
                        VolverALeerTrasFallo(null);
                    }
                    else if (panelVisible == panelLista)
                    {
                        PintarLista(null);
                    }

                    return;
                }

                resultado = dialogo.Resultado;
                deDonde = dialogo.DeDonde;
                sinHoras = dialogo.SinHoras;
                conExcel = dialogo.ExcelCargado;
                falloExcel = dialogo.FalloDelExcel;
                archivoExcel = dialogo.ArchivoExcel;
            }

            LeerSituacion();
            leido = null;
            abiertaRecordada = null;
            pasoRecordado = PasoProximoCiclo.Preparacion;
            if (falloExcel != null)
            {
                MostrarPreparacion(Vista.Semana, FiltroClases.Ninguno, falloExcel, true);
                return;
            }

            if (conExcel != null)
            {
                MostrarPreparacion(Vista.Semana, FiltroClases.Ninguno, "Listo: horario nuevo con «" + archivoExcel + "»: "
                    + RevisionCiclo.TextoClases(conExcel.Nuevas) + " con " + RevisionCiclo.TextoHorarios(conExcel.Franjas) + ". Revísalo con las flechas del laboratorio.");
                return;
            }

            string origen = deDonde == null ? "" : " " + deDonde;
            string estado = resultado.Clases == 0
                ? "Listo: horario en blanco. Arrastra sobre los cuadros libres para poner la primera clase."
                : sinHoras
                ? "Listo: horario nuevo con " + RevisionCiclo.TextoClases(resultado.Clases) + " de " + RevisionCiclo.TextoDocentes(resultado.Docentes)
                  + origen + ", sin horas. Elige cuadros libres y «Poner clase...» para acomodarlas."
                : "Listo: se copiaron " + RevisionCiclo.TextoClases(resultado.Clases) + " de " + RevisionCiclo.TextoDocentes(resultado.Docentes)
                  + " con " + RevisionCiclo.TextoHorarios(resultado.Horarios) + origen + ".";
            MostrarPreparacion(Vista.Semana, FiltroClases.Ninguno, estado);
        }

        private void Renombrar(CicloPreliminar ciclo)
        {
            if (ciclo == null || Permisos.Bloquear(this, "Cambiar el nombre de una plantilla"))
            {
                return;
            }

            string viejo = ciclo.Nombre;
            using (DialogoNombreConfiguracion dialogo = new DialogoNombreConfiguracion("Cambiar el nombre", viejo, "Cambiar el nombre",
                nombre => datos.Renombrar(ciclo.Id, nombre, IdAdministrador())))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    if (dialogo.HuboFallo)
                    {
                        // Pudo borrarla o renombrarla otra persona de la administracion: lo de ahora.
                        VolverALeerTrasFallo(null);
                    }

                    return;
                }

                string estado = "Listo: «" + viejo + "» ahora se llama «" + dialogo.Nombre + "».";
                if (panelVisible == panelAbierta && leido != null && leido.Ciclo.Id == ciclo.Id)
                {
                    LeerSituacionSinAviso();
                    Releer(estado);
                }
                else
                {
                    CargarLista(ciclo.Id, estado);
                }
            }
        }

        private void Borrar(CicloPreliminar ciclo)
        {
            if (ciclo == null || Permisos.Bloquear(this, "Borrar una plantilla guardada"))
            {
                return;
            }

            if (!ciclo.Guardada)
            {
                Estado("Los horarios aplicados quedan como registro: no se borran.", true);
                return;
            }

            string clases = ciclo.Clases == 0 ? "" : ciclo.Clases == 1 ? " con su única clase" : " con sus " + ciclo.Clases + " clases";
            if (!DialogoConfirmacion.Preguntar(this, "Borrar la plantilla", "¿Borrar «" + ciclo.Nombre + "»?",
                "Se borra la plantilla guardada" + clases + ". El horario que se usa no cambia, y los horarios aplicados no se tocan.", "Borrar"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                datos.Borrar(ciclo.Id);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                FalloAlEscribir("PestanaProximoCiclo.Borrar", "No se pudo borrar la plantilla", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (leido != null && leido.Ciclo.Id == ciclo.Id)
            {
                leido = null;
            }

            CargarLista(null, "Listo: se borró «" + ciclo.Nombre + "».");
        }

        /// <summary>"Guardar en <<X>>": en la plantilla guardada de la que viene (lo pregunta); sin ella, "Guardar como plantilla nueva...".</summary>
        private void GuardarEnSuSitio()
        {
            if (leido == null || Permisos.Bloquear(this, "Guardar el horario en preparacion en su plantilla"))
            {
                return;
            }

            CicloPreliminar ciclo = leido.Ciclo;
            if (!ciclo.SeGuardaEnOrigen)
            {
                GuardarComo();
                return;
            }

            string destino = ciclo.OrigenNombre;
            string cuenta = RevisionCiclo.TextoClases(ciclo.Clases) + " de " + RevisionCiclo.TextoDocentes(ciclo.Docentes);
            if (!DialogoConfirmacion.Preguntar(this, "Guardar en la plantilla", "¿Guardar en «" + destino + "»?",
                "Lo que tenía «" + destino + "» se reemplaza con este horario: " + cuenta + ". Para conservar las dos, usa «Guardar como plantilla nueva...».",
                "Guardar", false))
            {
                return;
            }

            ResultadoEmpezar resultado;
            Cursor = Cursors.WaitCursor;
            try
            {
                resultado = datos.GuardarEn(ciclo.Id, IdAdministrador());
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                // Otra persona pudo borrar "destino" o aplicar o descartar este horario.
                FalloAlEscribir("PestanaProximoCiclo.GuardarEn", "No se pudo guardar en «" + destino + "»", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            LeerSituacionSinAviso();
            Releer("Listo: «" + resultado.Nombre + "» quedó con " + RevisionCiclo.TextoClases(resultado.Clases) + " de "
                + RevisionCiclo.TextoDocentes(resultado.Docentes) + ".");
        }

        private void GuardarComo()
        {
            if (leido == null || !leido.Ciclo.EnPreparacion || Permisos.Bloquear(this, "Guardar como plantilla nueva"))
            {
                return;
            }

            int id = leido.Ciclo.Id;
            string propuesta = leido.Ciclo.Nombre;
            try
            {
                propuesta = datos.NombreLibre(propuesta, EstadoConfiguracion.Guardada);
            }
            catch (Exception ex)
            {
                Log.Error("PestanaProximoCiclo.GuardarComo: proponer un nombre libre", ex);
            }

            ResultadoEmpezar resultado = null;
            using (DialogoNombreConfiguracion dialogo = new DialogoNombreConfiguracion("Guardar como plantilla nueva", propuesta, "Guardar",
                nombre => resultado = datos.GuardarComo(id, nombre, IdAdministrador())))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK || resultado == null)
                {
                    if (dialogo.HuboFallo)
                    {
                        // Pudo aplicarlo o descartarlo otra persona de la administracion: lo de ahora.
                        VolverALeerTrasFallo(null);
                    }

                    return;
                }
            }

            LeerSituacionSinAviso();
            Releer("Listo: se guardó como la plantilla «" + resultado.Nombre + "» con " + RevisionCiclo.TextoClases(resultado.Clases) + " de "
                + RevisionCiclo.TextoDocentes(resultado.Docentes) + ".");
        }

        private void Descartar()
        {
            if (leido == null || !leido.Ciclo.EnPreparacion || Permisos.Bloquear(this, "Descartar el horario en preparacion"))
            {
                return;
            }

            CicloPreliminar ciclo = leido.Ciclo;
            if (!DialogoConfirmacion.Preguntar(this, "Descartar el horario en preparación", "¿Descartar «" + ciclo.Nombre + "»?",
                PiezasConfiguracion.SeBorraCon(ciclo.Clases) + ". Las plantillas guardadas y el horario que se usa no cambian.", "Descartar"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                datos.Descartar(ciclo.Id);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                FalloAlEscribir("PestanaProximoCiclo.Descartar", "No se pudo descartar el horario en preparación", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            leido = null;
            LeerSituacionSinAviso();
            MostrarPreparacion(Vista.Semana, FiltroClases.Ninguno,
                "Listo: se descartó «" + ciclo.Nombre + "». Las plantillas guardadas y el horario que se usa no cambian.");
        }

        /// <summary>"Ver docentes y clases" de la que se ve, en su propia ventana (VentanaDocentesYClases).</summary>
        private void VerDocentesYClases()
        {
            if (leido == null)
            {
                return;
            }

            using (VentanaDocentesYClases ventana = new VentanaDocentesYClases(leido))
            {
                ventana.ShowDialog(EstiloHorarios.Dueno(this));
            }
        }

        // =====================================================================
        // Una plantilla abierta (la de la preparacion, o una guardada o aplicada para verla)
        // =====================================================================

        // Cortas a proposito: a 1366 caben en dos lineas entre los botones y "Mas acciones".
        private const string LeyendaPreparacion = "Arrastra o usa Ctrl+clic para elegir cuadros; clic en una clase: sus opciones. En rojo, lo que choca.";
        // Si la de arriba no cabe en dos lineas (ventanas estrechas).
        private const string LeyendaPreparacionCorta = "Arrastra para elegir cuadros; clic en una clase: sus opciones.";
        // Con "Imprimir PDF" en la fila, a 1366 quedan unos 160 px: esta cabe en dos lineas.
        private const string LeyendaPreparacionMinima = "Arrastra para elegir cuadros; clic: opciones.";
        private const string LeyendaLectura = "Pasa el ratón por una clase para ver sus datos. En rojo, lo que choca.";
        // Si "Imprimir PDF" cabe en la fila del laboratorio (si no, sale en "Mas acciones").
        private bool imprimirEnLaFila = true;

        /// <summary>
        /// Con una clase traida de "Falta asignar" (porPoner), la leyenda dice que
        /// hacer con ella, como Horario semanal; null si no hay ninguna.
        /// </summary>
        private string LeyendaPorPoner(bool corta)
        {
            CargaCiclo carga = porPoner.HasValue && leido != null ? leido.Carga(porPoner.Value) : null;
            if (carga == null)
            {
                return null;
            }

            int minutos = leido.PorAsignar.Where(p => p.CargaId == carga.Id).Sum(p => p.Minutos);
            return corta
                ? "Para poner " + carga.Nombre + ": elige cuadros y pulsa «Poner clase...». Esc: olvidarla."
                : "Para poner " + carga.Nombre + " (" + (minutos <= 60 ? "falta " : "faltan ") + HorariosDatos.Duracion(minutos)
                  + "): elige cuadros y pulsa «Poner clase...», ya la trae elegida. Esc: olvidarla.";
        }

        /// <summary>
        /// Lee la plantilla y la ensena en esa vista. Si ya estaba abierta, se
        /// queda en el mismo laboratorio, con el mismo docente y la misma busqueda.
        /// Si ya no esta (otra persona de la administracion la borro o la descarto),
        /// lo dice. Si no hubo servidor y ya se veia, se queda como estaba con el
        /// fallo en rojo. Con deVuelta (al volver a leer la que estaba abierta), la
        /// que estaba en preparacion y ya se aplico lleva al paso 2 vacio con su
        /// aviso. Devuelve si se abrio.
        /// </summary>
        private bool AbrirConfiguracion(int id, Vista enVista, FiltroClases conFiltro, string estado, bool error = false, bool deVuelta = false)
        {
            bool misma = leido != null && leido.Ciclo.Id == id;
            CicloPreliminar conocida = misma ? leido.Ciclo : Buscar(id);
            CicloLeido nuevo;
            Cursor = Cursors.WaitCursor;
            try
            {
                nuevo = datos.Leer(id);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                if (!(ex is ErrorDeDatos || ex is AccionNoPermitida))
                {
                    if (misma && panelVisible == panelAbierta)
                    {
                        // Sin servidor: se queda lo que se ve, con el porque.
                        MostrarFallo("PestanaProximoCiclo.AbrirConfiguracion", "No se pudo volver a leer «" + leido.Ciclo.Nombre + "»", ex);
                        return false;
                    }

                    MostrarLista(null);
                    MostrarFallo("PestanaProximoCiclo.AbrirConfiguracion", "No se pudo abrir la plantilla", ex);
                    return false;
                }

                // "Esa configuracion ya no esta", o la que esta en preparacion en solo consulta.
                Log.Aviso("PestanaProximoCiclo.AbrirConfiguracion: " + ex.Message);
                abiertaRecordada = null;
                leido = null;
                bool leyo = LeerSituacionSinAviso();
                if (leyo && FaltaBase())
                {
                    MostrarSinBase();
                    return false;
                }

                if (leyo && conocida != null && conocida.EnPreparacion && !soloConsulta)
                {
                    // Se descarto (o se aplico) desde otra ventana: el paso 2, con el porque.
                    MostrarPreparacion(Vista.Semana, FiltroClases.Ninguno, null);
                    if (panelVisible == panelVacio)
                    {
                        Estado(TextoYaNoEsta(conocida), true);
                    }

                    return false;
                }

                bool sigue = leyo && Buscar(id) != null;
                string porQue = leyo && !sigue ? TextoYaNoEsta(conocida)
                    : soloConsulta ? "Esa plantilla no está entre las guardadas y las aplicadas: aquí solo se ven esas."
                    : ex.Message;
                MostrarLista(sigue ? id : (int?)null);
                Estado(leyo ? porQue : porQue + " " + falloAlLeer, true);
                return false;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (deVuelta && misma && leido.Ciclo.EnPreparacion && !nuevo.Ciclo.EnPreparacion)
            {
                // Se aplico (aqui, en el paso 3, o desde otro equipo): ya no se cambia aqui.
                abiertaRecordada = null;
                leido = null;
                LeerSituacionSinAviso();
                paso = PasoProximoCiclo.Preparacion;
                MostrarVacio();
                Estado("«" + nuevo.Ciclo.Nombre + "» ya se aplicó: está en «1. Plantillas» como horario aplicado y ya no se cambia aquí.");
                return false;
            }

            leido = nuevo;
            revision = RevisionCiclo.Revisar(leido);
            paso = leido.Ciclo.EnPreparacion ? PasoProximoCiclo.Preparacion : PasoProximoCiclo.Plantillas;
            pasoRecordado = paso;
            abiertaRecordada = leido.Ciclo.EnPreparacion ? (int?)null : id;
            if (!misma)
            {
                laboratorioElegido = laboratorioRecordado;
                profesorFijado = 0;
                porPoner = null;
                llenando = true;
                cboDocenteFiltro.Items.Clear();
                llenando = false;
                txtBuscarClases.Text = "";
                esperaBusqueda.Stop();
                lienzo.LimpiarSeleccion();
            }

            // Solo para verla, siempre la semana: la tabla va en su propia ventana.
            vista = Editable ? enVista : Vista.Semana;
            vistaRecordada = vista;
            filtro = Editable ? conFiltro : FiltroClases.Ninguno;
            Mostrar(panelAbierta);
            PintarAbierta();
            Estado(estado, error);
            return true;
        }

        /// <summary>
        /// Vuelve a leer la que esta abierta (tras un cambio, F5), en la misma vista
        /// y con lo mismo elegido. Devuelve si sigue abierta.
        /// </summary>
        private bool Releer(string estado, bool error = false)
        {
            if (leido == null)
            {
                return false;
            }

            List<int> elegidas = vista == Vista.Clases ? ClasesElegidas().Select(c => c.Id).ToList() : new List<int>();
            int primeraVisible = dgvClases.FirstDisplayedScrollingRowIndex;
            bool abierta = AbrirConfiguracion(leido.Ciclo.Id, vista, filtro, estado, error, true);
            if (abierta && vista == Vista.Clases)
            {
                ElegirClases(elegidas, primeraVisible);
            }

            return abierta;
        }

        /// <summary>De una plantilla abierta para verla, a la lista (con ella elegida).</summary>
        private void VolverALista()
        {
            int? estaba = leido == null || leido.Ciclo.EnPreparacion ? (int?)null : leido.Ciclo.Id;
            abiertaRecordada = null;
            lienzo.LimpiarSeleccion();
            CargarLista(estaba, null);
        }

        private void CambiarVista(Vista nueva, FiltroClases conFiltro)
        {
            if (leido == null)
            {
                return;
            }

            vista = nueva;
            vistaRecordada = nueva;
            filtro = conFiltro;
            lienzo.LimpiarSeleccion();
            PintarAbierta();
            Estado(null);
            if (nueva == Vista.Clases)
            {
                dgvClases.Focus();
            }
        }

        /// <summary>La marca, los botones, las pastillas y la vista elegida.</summary>
        private void PintarAbierta()
        {
            CicloPreliminar ciclo = leido.Ciclo;
            bool prep = ciclo.EnPreparacion && !soloConsulta;
            bool semana = vista == Vista.Semana;
            pastMarca.Mostrado = "En preparación (plantilla): nada se usa hasta aplicarla";
            pastMarca.Colores(PiezasConfiguracion.FondoAmbar, PiezasConfiguracion.BordeAmbar, PiezasConfiguracion.TextoAmbar);
            pastMarca.Visible = prep;

            detalleAbierta = DetalleDe(ciclo);
            ayuda.SetToolTip(pastMarca, detalleAbierta);

            // Los botones de la fila del laboratorio: los de Horario semanal en la
            // preparacion. Una plantilla guardada o aplicada se ve en VentanaVerHorario,
            // solo su semana y su tabla de docentes y clases (en otra ventana); crear,
            // cambiar el nombre y borrar quedan en la lista.
            btnPoner.Visible = prep && semana;
            btnCambiarHoras.Visible = prep && semana;
            btnQuitarHoras.Visible = prep && semana;
            btnVerTabla.Visible = !prep;
            btnMas.Visible = prep;
            // Si no cabe en la fila, AcomodarAbierta lo esconde y sale en "Mas acciones".
            btnImprimir.Visible = prep && semana && !Kiosco.Activo;
            btnExcel.Visible = prep && semana && !Kiosco.Activo;
            lblLaboratorio.Visible = semana;
            cboLaboratorio.Visible = semana;
            lblLeyenda.Visible = semana;
            panelLeyenda.Visible = semana;
            contenedorSemana.Visible = semana;
            lnkVolverSemana.Visible = !semana;
            lblDocenteFiltro.Visible = !semana;
            cboDocenteFiltro.Visible = !semana;
            lblBuscarClases.Visible = !semana;
            txtBuscarClases.Visible = !semana;
            pastFiltro.Visible = !semana && filtro != FiltroClases.Ninguno;
            dgvClases.Visible = !semana;
            btnVerEnSemana.Visible = !semana;
            btnNuevaClase.Visible = !semana && Editable;
            btnCambiarClase.Visible = !semana && Editable;
            btnQuitarClases.Visible = !semana && Editable;
            ayuda.SetToolTip(lblLeyenda, prep
                ? "Un cuadro por hora. Arrastra o usa Ctrl+clic para elegir cuadros y pulsa «Poner clase...»: la clase se pone todas las semanas.\n"
                  + "Encima de otra clase de este laboratorio, la reemplaza (antes te pregunta): se le quita solo lo que se cruza, y eso queda en «Falta asignar».\n"
                  + "Clic en una clase: sus opciones (cambiar sus horas, quitarlas, cambiar su docente...). Doble clic: cambiar sus horas.\n"
                  + "Cada docente tiene su color, el mismo en todo DigLab. Haz clic en su nombre, debajo, para ver solo sus clases (otro clic, todas).\n"
                  + "En rojo, las clases que chocan (la misma persona docente o el mismo grupo a la misma hora en otro laboratorio): no dejan aplicar."
                : "Un cuadro por hora. Pasa el ratón por una clase para ver quién la da, la materia y el grupo.\n"
                  + "Cada docente tiene su color, el mismo en todo DigLab. Haz clic en su nombre, debajo, para ver solo sus clases (otro clic, todas).");

            // Las pastillas de lo que no deja aplicar: solo en la preparacion.
            pastChoques.Visible = prep;
            pastSinDocente.Visible = prep && revision.SinProfesor > 0;
            pastSinHorario.Visible = prep && revision.SinHorario > 0;
            if (revision.Choques.Count > 0)
            {
                pastChoques.Mostrado = "Choques: " + revision.Choques.Count;
                pastChoques.Colores(PiezasConfiguracion.FondoRojo, PiezasConfiguracion.BordeRojo, PiezasConfiguracion.TextoRojo);
                ayuda.SetToolTip(pastChoques, "La misma persona docente o el mismo grupo a la misma hora en dos laboratorios distintos: "
                    + "no dejan aplicar. En la cuadrícula salen en rojo. Clic: verlas en la lista de clases.");
            }
            else
            {
                pastChoques.Mostrado = "Sin choques";
                pastChoques.Colores(PiezasConfiguracion.FondoVerde, Color.DarkGreen, Color.DarkGreen);
                ayuda.SetToolTip(pastChoques, "Ninguna clase choca con otra. Clic: ver todas en la lista de clases.");
            }

            pastSinDocente.Mostrado = "Sin docente: " + revision.SinProfesor;
            pastSinHorario.Mostrado = "Sin horario: " + revision.SinHorario;
            // "Falta asignar": solo en la preparacion y si falta algo (como Horario semanal).
            int faltan = leido.MinutosPorAsignar;
            pastFaltaAsignar.Visible = prep && faltan > 0;
            pastFaltaAsignar.Mostrado = "Falta asignar: " + HorariosDatos.Duracion(faltan);
            if (porPoner.HasValue && (!prep || !leido.PorAsignar.Any(p => p.CargaId == porPoner.Value) || leido.Carga(porPoner.Value) == null))
            {
                // Ya se le pusieron sus horas (o se quito la clase): se olvida.
                porPoner = null;
            }

            LlenarLaboratorios();
            if (semana)
            {
                PintarSemana();
            }
            else
            {
                LlenarDocentesFiltro();
                PintarClases();
            }

            ActualizarBotonesSemana();
            AcomodarAbierta();
        }

        /// <summary>
        /// Lo que dice la linea de estado sin otra cosa que decir: que tiene y,
        /// segun que es, de donde viene o cuando se guardo o se aplico. En solo
        /// consulta no propone crear un horario con ella (no puede).
        /// </summary>
        private static string DetalleDe(CicloPreliminar ciclo)
        {
            string resumen = "«" + ciclo.Nombre + "»: " + RevisionCiclo.Resumen(ciclo) + ". ";
            if (ciclo.EnPreparacion)
            {
                string origen = ciclo.OrigenId.HasValue
                    ? (ciclo.OrigenEstado.HasValue ? "Copia de «" + ciclo.OrigenNombre + "». " : "Viene de una plantilla que ya se borró. ")
                    : ciclo.CopiadoDel.HasValue ? "Copia del horario del " + PiezasConfiguracion.Fecha(ciclo.CopiadoDel.Value) + ". " : "Empezó en blanco. ";
                return resumen + origen + "Los cambios se guardan solos.";
            }

            if (ciclo.Guardada)
            {
                DateTime cuando = ciclo.CambiadoEn ?? ciclo.CreadoEn;
                return resumen + "Guardada el " + PiezasConfiguracion.Fecha(cuando) + " a las " + PiezasConfiguracion.Hora(cuando)
                    + (string.IsNullOrEmpty(ciclo.CambiadoPor) ? "" : " por " + ciclo.CambiadoPor)
                    + (ciclo.CopiadoDel.HasValue ? ", del horario del " + PiezasConfiguracion.Fecha(ciclo.CopiadoDel.Value) : "")
                    + (Permisos.SoloConsulta ? "." : ". Para cambiarla, cópiala con «Crear horario nuevo...» en «1. Plantillas».");
            }

            string vigencia = ciclo.Desde.HasValue && ciclo.Hasta.HasValue
                ? "Del " + PiezasConfiguracion.Fecha(ciclo.Desde.Value) + " al " + PiezasConfiguracion.Fecha(ciclo.Hasta.Value)
                  + (ciclo.CortadaEl.HasValue ? " (terminó el " + PiezasConfiguracion.Fecha(ciclo.CortadaEl.Value) + ")" : "") + ". "
                : "";
            return resumen + vigencia + (ciclo.PublicadoEn.HasValue ? "Aplicado el " + PiezasConfiguracion.Fecha(ciclo.PublicadoEn.Value) : "Aplicado")
                + (string.IsNullOrEmpty(ciclo.PublicadoPor) ? "" : " por " + ciclo.PublicadoPor) + ". "
                + "Lo que se cambió después en Horario semanal no sale aquí.";
        }

        /// <summary>
        /// Fila A: la barra de pasos, la marca y, a la derecha, las pastillas de lo
        /// que no deja aplicar. Fila B: el laboratorio y los botones, como en
        /// Horario semanal, la leyenda y "Mas acciones". Fila C: los docentes y la
        /// linea de estado. Debajo, la cuadricula con todo el alto que queda (o la
        /// lista de clases con sus botones abajo).
        /// </summary>
        internal void AcomodarAbierta()
        {
            int ancho = panelAbierta.ClientSize.Width;
            int alto = panelAbierta.ClientSize.Height;
            if (ancho < 400 || alto < 200 || leido == null)
            {
                return;
            }

            panelAbierta.SuspendLayout();
            try
            {
                // Fila A.
                int x = Margen;
                if (barraPasos.Parent == panelAbierta && barraPasos.Visible)
                {
                    barraPasos.Location = new Point(Margen, 6);
                    x = barraPasos.Right + 12;
                }

                int xr = ancho - Margen;
                foreach (PastillaConfiguracion p in new[] { pastSinHorario, pastSinDocente, pastChoques, pastFaltaAsignar })
                {
                    if (p.Visible)
                    {
                        p.Location = new Point(xr - p.Width, 6);
                        xr = p.Left - 8;
                    }
                }

                int natural = TextRenderer.MeasureText(pastMarca.Mostrado, pastMarca.Font).Width + 22;
                pastMarca.SetBounds(x, 6, Math.Max(80, Math.Min(natural, xr - 6 - x)), 28);

                // Fila B.
                bool semana = vista == Vista.Semana;
                // En VentanaVerHorario no hay fila A (ni pasos ni marca): esta fila va arriba.
                int filaB = enVentana ? 8 : ArribaFilaB;
                btnMas.Location = new Point(ancho - Margen - btnMas.Width, filaB);
                btnCerrarVentana.Location = new Point(ancho - Margen - btnCerrarVentana.Width, filaB);
                int derecha = btnCerrarVentana.Visible ? btnCerrarVentana.Left - 8 : btnMas.Visible ? btnMas.Left - 8 : ancho - Margen;
                if (semana)
                {
                    lblLaboratorio.Location = new Point(Margen, filaB + 8);
                    cboLaboratorio.Location = new Point(110, filaB + 4);
                    int xb = flechasLaboratorio.Derecha + 14;
                    foreach (Button boton in new[] { btnPoner, btnCambiarHoras, btnQuitarHoras, btnVerTabla })
                    {
                        if (boton.Visible)
                        {
                            boton.Location = new Point(xb, filaB);
                            xb = boton.Right + 8;
                        }
                    }

                    // "Imprimir PDF" (en la preparacion) a la derecha, si cabe; si no, va en "Mas acciones".
                    imprimirEnLaFila = btnMas.Visible && !Kiosco.Activo && xb + 6 + btnImprimir.Width <= derecha;
                    btnImprimir.Visible = imprimirEnLaFila;
                    btnImprimir.Location = new Point(derecha - btnImprimir.Width, filaB);
                    int derechaLeyenda = imprimirEnLaFila ? btnImprimir.Left : derecha;
                    // "Excel" a su izquierda, si cabe (lo suyo tambien esta en "Mas acciones").
                    bool excelEnLaFila = Editable && !Kiosco.Activo && xb + 6 + btnExcel.Width + 8 <= derechaLeyenda;
                    btnExcel.Visible = excelEnLaFila;
                    btnExcel.Location = new Point(derechaLeyenda - (imprimirEnLaFila ? 8 : 0) - btnExcel.Width, filaB);
                    if (excelEnLaFila)
                    {
                        derechaLeyenda = btnExcel.Left;
                    }

                    // La leyenda, entre los botones e "Imprimir PDF", entera en una o dos lineas:
                    // la larga, la corta, la minima o ninguna (la ayuda al pasar el raton la dice entera).
                    // Con una clase traida de "Falta asignar", que hacer con ella (en ambar).
                    int izquierda = Math.Max(flechasLaboratorio.Derecha, xb - 8) + 14;
                    int anchoLeyenda = derechaLeyenda - 14 - izquierda;
                    // El renglon como lo mide TextRenderer (17 a 8.5 pt; Font.Height dice 14).
                    int altoLinea = TextRenderer.MeasureText("Ag", lblLeyenda.Font).Height;
                    string leyenda = null;
                    int altoLeyenda = 0;
                    string paraPoner = Editable ? LeyendaPorPoner(false) : null;
                    string[] opciones = paraPoner != null ? new[] { paraPoner, LeyendaPorPoner(true), "Elige cuadros y «Poner clase...». Esc: olvidarla." }
                        : Editable ? new[] { LeyendaPreparacion, LeyendaPreparacionCorta, LeyendaPreparacionMinima } : new[] { LeyendaLectura };
                    lblLeyenda.ForeColor = paraPoner != null ? PiezasConfiguracion.TextoAmbar : Color.Gray;
                    foreach (string opcion in opciones)
                    {
                        int pide = anchoLeyenda < 120 ? int.MaxValue : TextRenderer.MeasureText(opcion, lblLeyenda.Font, new Size(anchoLeyenda, int.MaxValue),
                            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
                        if (pide <= 2 * altoLinea + 2)
                        {
                            leyenda = opcion;
                            altoLeyenda = Math.Max(altoLinea, pide);
                            break;
                        }
                    }

                    lblLeyenda.Visible = leyenda != null;
                    if (leyenda != null)
                    {
                        lblLeyenda.Text = leyenda;
                        lblLeyenda.SetBounds(izquierda, filaB + (34 - altoLeyenda) / 2, anchoLeyenda, altoLeyenda);
                    }
                }
                else
                {
                    lnkVolverSemana.Location = new Point(Margen, ArribaFilaB + 9);
                    lblDocenteFiltro.Location = new Point(lnkVolverSemana.Right + 20, ArribaFilaB + 8);
                    cboDocenteFiltro.Location = new Point(lblDocenteFiltro.Right + 6, ArribaFilaB + 4);
                    lblBuscarClases.Location = new Point(cboDocenteFiltro.Right + 18, ArribaFilaB + 8);
                    txtBuscarClases.Location = new Point(lblBuscarClases.Right + 6, ArribaFilaB + 4);
                    pastFiltro.Location = new Point(txtBuscarClases.Right + 12, ArribaFilaB + 4);
                }

                // Fila C y el cuerpo.
                if (semana)
                {
                    // La linea de estado, a la derecha de los docentes, tiene dos renglones
                    // (tres si el resumen de la plantilla los pide): a 1366 no cabia en
                    // uno y se leia cortado con "...". Lo pide el resumen, no cada aviso:
                    // la cuadricula no salta con cada cambio.
                    int arriba = filaB + 38;
                    int anchoLeyenda = (int)((ancho - 2 * Margen) * 0.6);
                    panelLeyenda.SetBounds(Margen, arriba + 2, anchoLeyenda, 26);
                    int usado = PonerLeyendaEnSuSitio();
                    // Solo lo que ocupan: lo demas es de la linea de estado.
                    panelLeyenda.Width = Math.Max(1, usado);
                    int inicioEstado = Margen + Math.Max(usado, 0) + 16;
                    int anchoEstado = Math.Max(60, ancho - Margen - inicioEstado);
                    lblEstadoAbierta.SetBounds(inicioEstado, arriba - 4, anchoEstado, AltoDelEstado(detalleAbierta, anchoEstado, 3));
                    // Encima del panel de los docentes (que es mas ancho que lo que ocupan).
                    lblEstadoAbierta.BringToFront();
                    AlinearEstado();
                    // Como Horario semanal: la cuadricula usa todo el alto que queda.
                    int arribaCuerpo = lblEstadoAbierta.Bottom + 2;
                    int disponible = alto - arribaCuerpo - 10;
                    int altoCuadricula = Math.Min(disponible, CuadriculaSemanal.AltoPara(disponible));
                    contenedorSemana.SetBounds(Margen, arribaCuerpo, ancho - 2 * Margen, Math.Max(120, altoCuadricula));
                }
                else
                {
                    // Abajo, siempre la fila de los botones: "Ver en el horario" tambien solo para verla.
                    int arribaCuerpo = ArribaFilaB + 40;
                    int pie = alto - AltoPie;
                    dgvClases.SetBounds(Margen, arribaCuerpo, ancho - 2 * Margen, pie - arribaCuerpo - 4);
                    int y = alto - AltoPie + (AltoPie - btnNuevaClase.Height) / 2 + 1;
                    int xb = ancho - Margen;
                    foreach (Button boton in new[] { btnQuitarClases, btnCambiarClase, btnNuevaClase, btnVerEnSemana })
                    {
                        if (boton.Visible)
                        {
                            boton.Location = new Point(xb - boton.Width, y);
                            xb = boton.Left - 8;
                        }
                    }

                    int altoEstado = AltoDelEstado("", 100, 2);
                    lblEstadoAbierta.SetBounds(Margen, pie + (AltoPie - altoEstado) / 2, Math.Max(60, xb - 2 * Margen), altoEstado);
                    AlinearEstado();
                }
            }
            finally
            {
                panelAbierta.ResumeLayout();
            }
        }

        /// <summary>
        /// Los laboratorios activos y los que tienen horas en ella (con
        /// "(desactivado)"), con sus horas y sus choques. Se queda en el que se
        /// miraba (o el que se eligio en Horario semanal); si no, el primero con horas.
        /// </summary>
        private void LlenarLaboratorios()
        {
            List<CargaCiclo> vivas = leido.Cargas.Where(c => !c.Quitada).ToList();
            Dictionary<int, List<FranjaCiclo>> porLaboratorio = vivas.SelectMany(c => c.Franjas).GroupBy(f => f.LaboratorioId).ToDictionary(g => g.Key, g => g.ToList());
            Dictionary<int, string> nombres = laboratoriosActivos.ToDictionary(l => l.Id, l => l.Texto);
            HashSet<int> activos = new HashSet<int>(nombres.Keys);
            foreach (KeyValuePair<int, List<FranjaCiclo>> par in porLaboratorio.Where(p => !nombres.ContainsKey(p.Key)))
            {
                nombres[par.Key] = par.Value[0].Laboratorio;
            }

            List<ItemLaboratorio> items = new List<ItemLaboratorio>();
            foreach (KeyValuePair<int, string> par in nombres)
            {
                List<FranjaCiclo> franjas;
                bool conHoras = porLaboratorio.TryGetValue(par.Key, out franjas);
                double horas = conHoras ? RevisionCiclo.Horas(franjas) : 0;
                int choques = revision.Choques.Count(c => (c.FranjaA != null && c.FranjaA.LaboratorioId == par.Key) || (c.FranjaB != null && c.FranjaB.LaboratorioId == par.Key)
                    || (c.Programada != null && c.Programada.LaboratorioId == par.Key));
                // Las encimadas (datos viejos o copiados) tambien en rojo: no dejan aplicar.
                bool encimadas = revision.Encimadas.Any(c => c.FranjaA != null && c.FranjaA.LaboratorioId == par.Key);
                // Lo dice la base (FranjaCiclo.LaboratorioActivo), no la lista de activos:
                // si esa no se pudo leer, no todos salen como desactivados.
                bool desactivado = conHoras ? franjas.Any(f => !f.LaboratorioActivo) : !activos.Contains(par.Key);
                string nombre = par.Value + (desactivado ? " (deshabilitado)" : "");
                string texto = nombre + ": " + (horas == 0 ? "sin horas" : RevisionCiclo.TextoHoras(horas))
                    + (choques == 0 ? "" : choques == 1 ? ", 1 choque" : ", " + choques + " choques")
                    + (encimadas ? ", clases encimadas" : "");
                items.Add(new ItemLaboratorio { Id = par.Key, Nombre = nombre, Laboratorio = par.Value, Texto = texto, Horas = horas, Choques = choques + (encimadas ? 1 : 0) });
            }

            items = items.OrderBy(i => i.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
            ItemLaboratorio elegido = items.FirstOrDefault(i => i.Id == laboratorioElegido)
                ?? items.FirstOrDefault(i => i.Id == laboratorioRecordado)
                ?? items.FirstOrDefault(i => i.Horas > 0)
                ?? items.FirstOrDefault();
            llenando = true;
            cboLaboratorio.BeginUpdate();
            try
            {
                cboLaboratorio.Items.Clear();
                foreach (ItemLaboratorio item in items)
                {
                    cboLaboratorio.Items.Add(item);
                }

                int anchoLista = items.Count == 0 ? 0 : items.Max(i => TextRenderer.MeasureText(i.Texto, cboLaboratorio.Font).Width) + 12 + SystemInformation.VerticalScrollBarWidth;
                cboLaboratorio.DropDownWidth = Math.Max(cboLaboratorio.Width, anchoLista);
                cboLaboratorio.SelectedItem = elegido;
            }
            finally
            {
                cboLaboratorio.EndUpdate();
                llenando = false;
            }

            // Items.Clear no avisa a las flechas (sin laboratorios se quedaria el nombre de antes).
            if (flechasLaboratorio != null)
            {
                flechasLaboratorio.Refrescar();
            }

            laboratorioElegido = elegido == null ? 0 : elegido.Id;
            if (elegido != null)
            {
                laboratorioRecordado = elegido.Id;
            }

            ayuda.SetToolTip(cboLaboratorio, elegido == null ? "" : elegido.Texto);
        }

        /// <summary>En la caja, solo el nombre (como Horario semanal); en la lista, con sus horas y sus choques (en rojo).</summary>
        private void DibujarLaboratorio(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            ItemLaboratorio item = e.Index >= 0 && e.Index < cboLaboratorio.Items.Count ? cboLaboratorio.Items[e.Index] as ItemLaboratorio : null;
            if (item == null)
            {
                return;
            }

            bool enLaCaja = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool elegida = (e.State & DrawItemState.Selected) != 0;
            Color letra = elegida && !enLaCaja ? SystemColors.HighlightText
                : item.Choques > 0 && !enLaCaja ? PiezasConfiguracion.TextoRojo
                : e.ForeColor;
            TextRenderer.DrawText(e.Graphics, enLaCaja ? item.Nombre : item.Texto, e.Font, new Rectangle(e.Bounds.X + 2, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height),
                letra, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        /// <summary>El laboratorio que se mira, como lo piden los dialogos de Horario semanal.</summary>
        private OpcionCombo LaboratorioOpcion()
        {
            ItemLaboratorio item = cboLaboratorio.SelectedItem as ItemLaboratorio;
            return item == null ? null : new OpcionCombo { Id = item.Id, Texto = item.Nombre };
        }

        // =====================================================================
        // La semana
        // =====================================================================

        /// <summary>Las clases de ese laboratorio en la cuadricula (las que chocan, en rojo) y la leyenda de sus docentes.</summary>
        private void PintarSemana()
        {
            int laboratorio = laboratorioElegido;
            List<FranjaInfo> franjas = new List<FranjaInfo>();
            Dictionary<int, AspectoEnCalendario> aspectos = new Dictionary<int, AspectoEnCalendario>();

            // Por franja: con que motivo choca (puede ser mas de uno) y si se encima
            // con otra en este laboratorio (datos viejos o copiados: no deja aplicar).
            Dictionary<int, bool[]> motivos = new Dictionary<int, bool[]>();
            Action<FranjaCiclo, ChoqueCiclo> anotar = (f, c) =>
            {
                bool[] m;
                if (!motivos.TryGetValue(f.Id, out m))
                {
                    m = new bool[3];
                    motivos[f.Id] = m;
                }

                m[0] |= c.MismoLaboratorio;
                m[1] |= c.MismoProfesor && !c.MismoLaboratorio;
                m[2] |= c.MismoGrupo && !c.MismoLaboratorio;
            };
            foreach (ChoqueCiclo choque in revision.Choques.Concat(revision.Encimadas))
            {
                if (choque.FranjaA != null)
                {
                    anotar(choque.FranjaA, choque);
                }

                if (choque.FranjaB != null)
                {
                    anotar(choque.FranjaB, choque);
                }
            }

            foreach (CargaCiclo carga in leido.Cargas.Where(c => !c.Quitada))
            {
                foreach (FranjaCiclo f in carga.Franjas.Where(f => f.LaboratorioId == laboratorio))
                {
                    franjas.Add(ComoFranjaInfo(carga, f));
                    bool[] m;
                    if (motivos.TryGetValue(f.Id, out m))
                    {
                        List<string> partes = new List<string>();
                        if (m[1]) partes.Add("misma persona docente");
                        if (m[2]) partes.Add("mismo grupo");
                        string pie = partes.Count > 0 ? "Choca: " + RevisionCiclo.Enumerar(partes) : "";
                        if (m[0])
                        {
                            pie += (pie.Length > 0 ? "; se encima con otra" : "Se encima con otra clase");
                        }

                        aspectos[f.Id] = new AspectoEnCalendario
                        {
                            Fondo = PiezasConfiguracion.FondoRojo,
                            Texto = PiezasConfiguracion.LetraRojaCuadro,
                            Pie = pie
                        };
                    }
                    else if (!carga.ProfesorId.HasValue)
                    {
                        aspectos[f.Id] = new AspectoEnCalendario { Fondo = Color.White, Texto = PiezasConfiguracion.LetraRojaCuadro, Pie = "Sin docente" };
                    }
                }
            }

            FranjaInfo antes = lienzo.Seleccionada;
            lienzo.SoloLectura = !Editable;
            lienzo.AspectoDeFranja = (f, fecha) =>
            {
                AspectoEnCalendario a;
                return aspectos.TryGetValue(f.Id, out a) ? a : null;
            };
            lienzo.Franjas = franjas;
            lienzo.Seleccionada = antes == null ? null : franjas.FirstOrDefault(f => f.Id == antes.Id);
            if (!franjas.Any(f => f.ProfesorId == profesorFijado))
            {
                profesorFijado = 0;
            }

            lienzo.ProfesorResaltado = profesorFijado;
            PonerLeyenda(franjas);
        }

        /// <summary>Una clase de la plantilla como franja de la cuadricula: sin codigo y vigente siempre (SinFechas).</summary>
        private static FranjaInfo ComoFranjaInfo(CargaCiclo carga, FranjaCiclo f)
        {
            DateTime siempreDesde = new DateTime(2000, 1, 1);
            DateTime siempreHasta = new DateTime(2099, 12, 31);
            return new FranjaInfo
            {
                Id = f.Id,
                ClaseId = carga.Id,
                LaboratorioId = f.LaboratorioId,
                Laboratorio = f.Laboratorio,
                DiaSemana = f.DiaSemana,
                Inicio = f.Inicio,
                Fin = f.Fin,
                Codigo = "",
                ProfesorId = carga.ProfesorId ?? 0,
                Profesor = carga.ProfesorId.HasValue ? carga.Profesor : "Sin docente",
                ProfesorCorto = carga.ProfesorId.HasValue ? carga.ProfesorCorto : "Sin docente",
                GrupoId = carga.GrupoId,
                Grupo = carga.Grupo,
                Materia = carga.Materia,
                VigenteDesde = siempreDesde,
                VigenteHasta = siempreHasta,
                ClaseDesde = siempreDesde,
                ClaseHasta = siempreHasta,
                Activa = true
            };
        }

        // Los docentes del laboratorio que se ve, para la leyenda (se coloca en AcomodarAbierta).
        private sealed class DocenteDeLeyenda
        {
            public int Id;
            public string Corto;
            public string Nombre;
            public double Horas;
        }

        private List<DocenteDeLeyenda> docentesDeLeyenda = new List<DocenteDeLeyenda>();

        private void PonerLeyenda(List<FranjaInfo> franjas)
        {
            docentesDeLeyenda = franjas
                .Where(f => f.ProfesorId != 0)
                .GroupBy(f => f.ProfesorId)
                .Select(g => new DocenteDeLeyenda
                {
                    Id = g.Key,
                    Corto = string.IsNullOrEmpty(g.First().ProfesorCorto) ? g.First().Profesor : g.First().ProfesorCorto,
                    Nombre = g.First().Profesor,
                    Horas = g.Sum(f => (f.Fin - f.Inicio).TotalHours)
                })
                .OrderBy(p => p.Corto, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            PonerLeyendaEnSuSitio();
        }

        /// <summary>
        /// Los docentes en una sola linea, cada uno con su color, como en Horario
        /// semanal; si no caben, el ultimo dice "y 3 mas" y al pasar el raton los
        /// nombra. Un clic deja a la vista solo sus clases (otro clic, todas).
        /// Devuelve el ancho que ocupa.
        /// </summary>
        private int PonerLeyendaEnSuSitio()
        {
            panelLeyenda.SuspendLayout();
            foreach (Control viejo in panelLeyenda.Controls.Cast<Control>().ToList())
            {
                ayuda.SetToolTip(viejo, null);
                viejo.Dispose();
            }

            int ancho = panelLeyenda.Width;
            int x = 0;
            const int Separacion = 6;
            int reservaMas = TextRenderer.MeasureText("y 99 más", fuenteChip).Width + 12;
            int puestos = 0;
            for (int i = 0; i < docentesDeLeyenda.Count; i++)
            {
                DocenteDeLeyenda p = docentesDeLeyenda[i];
                int id = p.Id;
                Label chip = new Label
                {
                    AutoSize = true,
                    BackColor = ColoresProfesor.De(id),
                    BorderStyle = profesorFijado == id ? BorderStyle.FixedSingle : BorderStyle.None,
                    Cursor = Cursors.Hand,
                    Font = fuenteChip,
                    ForeColor = Color.Black,
                    Padding = new Padding(8, 3, 8, 3),
                    Tag = id,
                    Text = p.Corto
                };
                Size medida = chip.PreferredSize;
                bool ultimo = i == docentesDeLeyenda.Count - 1;
                if (x + medida.Width > ancho - (ultimo ? 0 : reservaMas))
                {
                    chip.Dispose();
                    break;
                }

                chip.Location = new Point(x, 0);
                ayuda.SetToolTip(chip, p.Nombre + Environment.NewLine
                    + RevisionCiclo.TextoHoras(p.Horas) + " a la semana en este laboratorio." + Environment.NewLine
                    + "Clic: dejar a la vista solo sus clases (otro clic, todas).");
                chip.Click += (s, e) =>
                {
                    profesorFijado = profesorFijado == id ? 0 : id;
                    lienzo.ProfesorResaltado = profesorFijado;
                    foreach (Control c in panelLeyenda.Controls)
                    {
                        Label otro = c as Label;
                        if (otro != null && otro.Tag is int)
                        {
                            otro.BorderStyle = profesorFijado != 0 && (int)otro.Tag == profesorFijado ? BorderStyle.FixedSingle : BorderStyle.None;
                        }
                    }
                };
                panelLeyenda.Controls.Add(chip);
                x += medida.Width + Separacion;
                puestos++;
            }

            int faltan = docentesDeLeyenda.Count - puestos;
            if (faltan > 0)
            {
                Label mas = new Label
                {
                    AutoSize = true,
                    Font = fuenteChip,
                    ForeColor = PiezasConfiguracion.TextoGris,
                    Location = new Point(x, 4),
                    Text = "y " + faltan + " más"
                };
                ayuda.SetToolTip(mas, string.Join(Environment.NewLine, docentesDeLeyenda.Skip(puestos).Select(p => p.Nombre)));
                panelLeyenda.Controls.Add(mas);
                x += mas.PreferredWidth + Separacion;
            }

            panelLeyenda.ResumeLayout();
            return x;
        }

        /// <summary>
        /// Con cuadros elegidos (libres u ocupados: encima de otra clase, la
        /// reemplaza), "Poner clase..."; con una clase elegida, "Cambiar horas..." y
        /// "Quitar horas...". Apagados, dicen por que.
        /// </summary>
        private void ActualizarBotonesSemana()
        {
            bool editable = Editable && vista == Vista.Semana;
            btnPoner.Enabled = editable && lienzo.ObtenerRangosSeleccionados(false).Count > 0;
            bool hay = editable && lienzo.Seleccionada != null;
            btnCambiarHoras.Enabled = hay;
            btnQuitarHoras.Enabled = hay;
        }

        /// <summary>Clic en una clase de la semana de la preparacion: sus opciones, como en Horario semanal.</summary>
        private void MostrarMenuFranja(FranjaInfo franja)
        {
            franjaEnMenu = franja;
            CargaCiclo carga = leido == null ? null : leido.Carga(franja.ClaseId);
            if (carga == null)
            {
                return;
            }

            menuFranja.Items[0].Text = TextosDelHorario.MenuCambiarHoras(carga.Materia, carga.Grupo, franja.DiaSemana, franja.Inicio, franja.Fin);
            menuFranja.Items[3].Text = "Cambiar el docente, el grupo o la materia de " + carga.Nombre + "...";
            menuFranja.Show(Cursor.Position);
        }

        private CargaCiclo CargaEnMenu()
        {
            return franjaEnMenu == null || leido == null ? null : leido.Carga(franjaEnMenu.ClaseId);
        }

        /// <summary>Se soltaron cuadros de la semana: "Poner una clase el lunes de 07:00 a 09:00 (todas las semanas)...".</summary>
        private void MostrarMenuHueco()
        {
            ActualizarBotonesSemana();
            if (!Editable)
            {
                return;
            }

            // Como Horario semanal con db/25: tambien los ocupados (los reemplaza; antes pregunta).
            List<RangoHueco> elegidos = lienzo.ObtenerRangosSeleccionados(false);
            menuHueco.Items[0].Enabled = elegidos.Count > 0;
            menuHueco.Items[0].Text = elegidos.Count == 0 ? TextosDelHorario.SinCuadrosLibres
                : lienzo.HayOcupadosSeleccionados() ? "Poner una clase el " + TextosDelHorario.Rangos(elegidos) + " (todas las semanas, reemplazando lo que haya)..."
                : TextosDelHorario.MenuPonerEnHuecos(elegidos);
            menuHueco.Show(Cursor.Position);
        }

        /// <summary>"Mas acciones": ver la lista de clases o la semana y, en la preparacion, guardarla como plantilla, cambiarle el nombre, empezar de nuevo o descartarla.</summary>
        private void MostrarMenuMas()
        {
            if (leido == null)
            {
                return;
            }

            bool prep = Editable;
            itemVista.Text = vista == Vista.Semana ? "Ver la lista de clases" : "Ver la semana";
            itemImprimir.Visible = vista == Vista.Semana && !Kiosco.Activo && !imprimirEnLaFila;
            sepVista.Visible = prep;
            itemGuardarComo.Visible = prep;
            itemGuardarEn.Visible = prep && leido.Ciclo.SeGuardaEnOrigen;
            itemGuardarEn.Text = "Guardar en «" + leido.Ciclo.OrigenNombre + "» (la reemplaza)...";
            itemCambiarNombre.Visible = prep;
            sepExcel.Visible = prep && !Kiosco.Activo;
            itemCargarExcel.Visible = prep && !Kiosco.Activo;
            itemDescargarExcel.Visible = prep && !Kiosco.Activo;
            sepDescartar.Visible = prep;
            itemEmpezarDeNuevo.Visible = prep;
            itemDescartar.Visible = prep;
            menuMas.Show(btnMas, new Point(btnMas.Width, btnMas.Height), ToolStripDropDownDirection.BelowLeft);
        }

        // =====================================================================
        // Poner clase, cambiar horas y quitar horas (como en Horario semanal)
        // =====================================================================

        /// <summary>Las clases de la plantilla como las piden los dialogos de Horario semanal (SelectorClase): vigentes siempre, sin codigo.</summary>
        private List<ClaseInfo> ClasesDeLaPlantilla()
        {
            DateTime siempreDesde = new DateTime(2000, 1, 1);
            DateTime siempreHasta = new DateTime(2099, 12, 31);
            return (leido == null ? new List<CargaCiclo>() : leido.Cargas.Where(c => !c.Quitada).ToList())
                .Select(c => new ClaseInfo
                {
                    Id = c.Id,
                    ProfesorId = c.ProfesorId ?? 0,
                    Profesor = c.ProfesorId.HasValue ? c.Profesor : "Sin docente (decidirlo después)",
                    ProfesorCorto = c.ProfesorId.HasValue ? c.ProfesorCorto : "Sin docente",
                    GrupoId = c.GrupoId,
                    Grupo = c.Grupo,
                    MateriaId = c.MateriaId,
                    Materia = c.Materia,
                    Codigo = "",
                    VigenteDesde = siempreDesde,
                    VigenteHasta = siempreHasta,
                    Activa = true
                })
                .ToList();
        }

        // Si "Nueva clase..." fallo (o creo una) dentro de un dialogo de Horario semanal: al cerrarlo se vuelve a leer.
        private bool falloEnNuevaClase;
        private bool claseNuevaEnDialogo;

        /// <summary>Al cancelar un dialogo de poner una clase: si dentro se creo una clase o algo fallo, lo de ahora.</summary>
        private void TrasCancelarPoner()
        {
            if (falloEnNuevaClase)
            {
                VolverALeerTrasFallo(null);
            }
            else if (claseNuevaEnDialogo)
            {
                Releer("La clase nueva quedó en este horario; si no le pusiste horas, sale en «Sin horario».");
            }
        }

        /// <summary>
        /// "Nueva clase..." de los dialogos de poner una clase: una clase nueva en
        /// la plantilla (docente, grupo, materia y, si se quiere, sus horas) sin
        /// cerrar el dialogo. Devuelve las clases de la plantilla otra vez, o null
        /// si se cancelo.
        /// </summary>
        private List<ClaseInfo> CrearClaseDesdeDialogo(IWin32Window dueno)
        {
            if (!Editable || Permisos.Bloquear(dueno, "Crear una clase en el horario en preparacion"))
            {
                return null;
            }

            int idCarga;
            using (DialogoClaseDelHorario dialogo = new DialogoClaseDelHorario(datos, leido, null, null, laboratorioElegido, IdAdministrador))
            {
                if (dialogo.ShowDialog(dueno) != DialogResult.OK)
                {
                    falloEnNuevaClase |= dialogo.HuboFallo;
                    return null;
                }

                idCarga = dialogo.IdCarga;
            }

            claseNuevaEnDialogo = true;
            try
            {
                leido = datos.Leer(leido.Ciclo.Id);
                revision = RevisionCiclo.Revisar(leido);
            }
            catch (Exception ex)
            {
                // Se creo; se vera al volver a leer al cerrar el dialogo.
                Log.Error("PestanaProximoCiclo: volver a leer tras crear la clase " + idCarga, ex);
                falloEnNuevaClase = true;
                return null;
            }

            return ClasesDeLaPlantilla();
        }

        /// <summary>
        /// "Poner clase..." (boton y menu de los cuadros elegidos): DialogoAsignarClase,
        /// el de Horario semanal, con las clases de la plantilla. Las horas se
        /// ponen todas o ninguna (PonerHoras), sin los cuadros que ya son de esa
        /// clase. Si chocan con otras clases (docente o grupo en otro
        /// laboratorio), lo avisa antes y se pueden poner igual; encima de otras
        /// clases de este laboratorio, pregunta y las reemplaza (se les quita solo
        /// lo que se cruza).
        /// </summary>
        private void PonerClaseEnHuecos()
        {
            if (!Editable || Permisos.Bloquear(this, "Poner una clase en el horario en preparacion"))
            {
                return;
            }

            List<RangoHueco> rangos = lienzo.ObtenerRangosSeleccionados(false);
            OpcionCombo laboratorio = LaboratorioOpcion();
            if (rangos.Count == 0 || laboratorio == null)
            {
                return;
            }

            int idCarga;
            falloEnNuevaClase = false;
            claseNuevaEnDialogo = false;
            List<ClaseInfo> clases = ClasesDeLaPlantilla();
            using (DialogoAsignarClase dialogo = new DialogoAsignarClase(clases, laboratorio, rangos, CrearClaseDesdeDialogo, null, null))
            {
                // Traida de "Falta asignar" (Ponerla...): ya elegida, como en Horario semanal.
                ClaseInfo preelegida = porPoner.HasValue ? clases.FirstOrDefault(c => c.Id == porPoner.Value) : null;
                if (preelegida != null)
                {
                    dialogo.Preelegir(preelegida);
                }

                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    TrasCancelarPoner();
                    return;
                }

                idCarga = dialogo.ClaseId;
            }

            CargaCiclo carga = leido == null ? null : leido.Carga(idCarga);
            if (carga == null)
            {
                Releer("Esa clase ya no está en este horario: elige otra.", true);
                return;
            }

            // Encima de si misma no se pone (como Horario semanal con db/25).
            rangos = lienzo.ObtenerRangosSeleccionadosSin(carga.Id);
            if (rangos.Count == 0)
            {
                MessageBox.Show(EstiloHorarios.Dueno(this), "Esas horas ya son de esa clase: no hay nada que poner.", TextosDelHorario.TituloPoner,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<FranjaCiclo> franjas = rangos.Select(r => new FranjaCiclo
            {
                LaboratorioId = laboratorio.Id,
                Laboratorio = laboratorio.Texto,
                DiaSemana = r.Dia,
                Inicio = r.Inicio,
                Fin = r.Fin >= TimeSpan.FromHours(24) ? new TimeSpan(23, 59, 0) : r.Fin
            }).ToList();
            if (!SeguirAunqueChoque(carga, franjas, null)
                || !ConfirmarReemplazoEnPreparacion.Preguntar(EstiloHorarios.Dueno(this), leido, carga.Id, franjas, null))
            {
                return;
            }

            int faltaba = PorAsignarDe(carga.Id);
            Cursor = Cursors.WaitCursor;
            try
            {
                datos.PonerHoras(carga.Id, franjas, IdAdministrador());
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                FalloAlEscribir("PestanaProximoCiclo.PonerHoras", "No se pudo poner " + carga.Nombre + " en esas horas", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            lienzo.LimpiarSeleccion();
            ReleerTrasPoner("Listo: " + carga.Nombre + " (" + (carga.ProfesorId.HasValue ? carga.ProfesorCorto : "sin docente") + ") quedó el "
                + TextosDelHorario.Rangos(rangos) + ".", carga.Id, faltaba);
        }

        /// <summary>Lo que le falta asignar a esa clase de la plantilla, en minutos.</summary>
        private int PorAsignarDe(int idCarga)
        {
            return leido == null ? 0 : leido.PorAsignar.Where(p => p.CargaId == idCarga).Sum(p => p.Minutos);
        }

        /// <summary>
        /// Releer tras ponerle horas a una clase: si le faltaba asignar, el "Listo"
        /// dice tambien lo que le queda (bajo solo, como en Horario semanal).
        /// </summary>
        private void ReleerTrasPoner(string listo, int idCarga, int faltabaAntes)
        {
            if (!Releer(listo) || faltabaAntes <= 0)
            {
                return;
            }

            int ahora = PorAsignarDe(idCarga);
            if (ahora < faltabaAntes)
            {
                Estado(listo + (ahora == 0 ? " Ya no le falta nada por asignar." : " Le falta asignar " + HorariosDatos.Duracion(ahora) + "."));
            }
        }

        /// <summary>
        /// Si esas horas de esa clase chocarian con otras (la misma persona
        /// docente o el mismo grupo en otro laboratorio), lo dice y pregunta si se
        /// ponen igual: se pueden guardar, pero no dejan aplicar hasta resolverlo.
        /// Sin choques, true. Lo del mismo laboratorio no es un choque: se
        /// reemplaza (ConfirmarReemplazoEnPreparacion).
        /// </summary>
        private bool SeguirAunqueChoque(CargaCiclo carga, List<FranjaCiclo> franjas, ICollection<int> sinFranjas)
        {
            List<string> choques = RevisionCiclo.ChoquesSi(leido, carga.ProfesorId, carga.GrupoId, franjas, new List<int> { carga.Id }, sinFranjas);
            if (choques.Count == 0)
            {
                return true;
            }

            string lista = string.Join(Environment.NewLine, choques.Take(4).Select(c => "   • " + c))
                + (choques.Count > 4 ? Environment.NewLine + "   Y " + (choques.Count - 4) + " más." : "");
            return MessageBox.Show(EstiloHorarios.Dueno(this), (choques.Count == 1 ? "Esas horas chocan con otra clase:" : "Esas horas chocan con otras clases:")
                    + Environment.NewLine + Environment.NewLine + lista + Environment.NewLine + Environment.NewLine
                    + "Se pueden poner así: el choque sale en rojo y no deja aplicar hasta resolverlo. ¿Ponerla de todos modos?",
                    "Choque de horario", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>"Poner una clase (eligiendo dia y horas)..." y doble clic en un cuadro libre: el dialogo de Horario semanal, en la plantilla.</summary>
        private void PonerEligiendo(int? dia, TimeSpan? hora)
        {
            if (!Editable || Permisos.Bloquear(this, "Poner una clase en el horario en preparacion"))
            {
                return;
            }

            OpcionCombo laboratorio = LaboratorioOpcion();
            if (laboratorio == null)
            {
                return;
            }

            int idCarga;
            FranjaCiclo franja;
            falloEnNuevaClase = false;
            claseNuevaEnDialogo = false;
            List<ClaseInfo> clases = ClasesDeLaPlantilla();
            using (DialogoHorasDePlantilla dialogo = new DialogoHorasDePlantilla(() => leido, clases, laboratorio, null, null, dia, hora, CrearClaseDesdeDialogo))
            {
                ClaseInfo preelegida = porPoner.HasValue ? clases.FirstOrDefault(c => c.Id == porPoner.Value) : null;
                if (preelegida != null)
                {
                    dialogo.Preelegir(preelegida);
                }

                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    TrasCancelarPoner();
                    return;
                }

                idCarga = dialogo.CargaId;
                franja = dialogo.Franja;
            }

            CargaCiclo carga = leido == null ? null : leido.Carga(idCarga);
            if (carga == null)
            {
                Releer("Esa clase ya no está en este horario: elige otra.", true);
                return;
            }

            int faltaba = PorAsignarDe(carga.Id);
            Cursor = Cursors.WaitCursor;
            try
            {
                datos.PonerHoras(carga.Id, new List<FranjaCiclo> { franja }, IdAdministrador());
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                FalloAlEscribir("PestanaProximoCiclo.PonerEligiendo", "No se pudo poner " + carga.Nombre + " en esas horas", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            lienzo.LimpiarSeleccion();
            ReleerTrasPoner("Listo: " + carga.Nombre + " (" + (carga.ProfesorId.HasValue ? carga.ProfesorCorto : "sin docente") + ") quedó el "
                + TextosDelHorario.Dia(franja.DiaSemana) + " " + TextosDelHorario.Horas(franja.Inicio, franja.Fin) + ".", carga.Id, faltaba);
        }

        /// <summary>"Cambiar horas..." (boton, menu y doble clic): el dia, las horas o la clase de las horas elegidas.</summary>
        private void CambiarHoras()
        {
            FranjaInfo elegida = lienzo.Seleccionada ?? franjaEnMenu;
            if (!Editable || elegida == null || Permisos.Bloquear(this, "Cambiar unas horas del horario en preparacion"))
            {
                return;
            }

            CargaCiclo carga = leido.Carga(elegida.ClaseId);
            FranjaCiclo actual = carga == null ? null : carga.Franjas.FirstOrDefault(f => f.Id == elegida.Id);
            if (actual == null)
            {
                return;
            }

            OpcionCombo laboratorio = new OpcionCombo { Id = actual.LaboratorioId, Texto = actual.Laboratorio };
            int idCarga;
            FranjaCiclo nueva;
            using (DialogoHorasDePlantilla dialogo = new DialogoHorasDePlantilla(() => leido, ClasesDeLaPlantilla(), laboratorio, carga, actual, null, null, null))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                idCarga = dialogo.CargaId;
                nueva = dialogo.Franja;
            }

            int faltaba = PorAsignarDe(idCarga);
            Cursor = Cursors.WaitCursor;
            try
            {
                datos.CambiarFranja(actual.Id, idCarga, nueva, IdAdministrador());
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                FalloAlEscribir("PestanaProximoCiclo.CambiarFranja", "No se pudieron cambiar esas horas", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            CargaCiclo destino = leido.Carga(idCarga) ?? carga;
            ReleerTrasPoner("Listo: " + destino.Nombre + " (" + (destino.ProfesorId.HasValue ? destino.ProfesorCorto : "sin docente") + ") quedó el "
                + TextosDelHorario.Dia(nueva.DiaSemana) + " " + TextosDelHorario.Horas(nueva.Inicio, nueva.Fin) + ".", destino.Id, faltaba);
        }

        /// <summary>
        /// "Quitar horas..." (boton y menu): las horas elegidas, con el dialogo de
        /// Horario semanal (DialogoQuitarHoras): "¿Se va a dar en otro horario?"
        /// Si (la opcion por omision), sus horas quedan en "Falta asignar"; No, la
        /// clase tendra menos horas. Quitar en rojo; Enter cancela.
        /// </summary>
        private void QuitarHoras()
        {
            FranjaInfo elegida = lienzo.Seleccionada ?? franjaEnMenu;
            if (!Editable || elegida == null || Permisos.Bloquear(this, "Quitar unas horas del horario en preparacion"))
            {
                return;
            }

            CargaCiclo carga = leido.Carga(elegida.ClaseId);
            if (carga == null)
            {
                return;
            }

            string cual = "el " + TextosDelHorario.Dia(elegida.DiaSemana) + " " + TextosDelHorario.Horas(elegida.Inicio, elegida.Fin) + " de " + carga.Nombre;
            string descripcion = "Se quita del horario en preparación " + cual + ". Lo que se usa hoy no cambia."
                + (carga.Franjas.Count == 1 ? " La clase se queda sin horas: sale en «Sin horario»." : "");
            bool porAsignar;
            using (DialogoQuitarHoras dialogo = new DialogoQuitarHoras(descripcion, null, null, null, HorariosDatos.MinutosEntre(elegida.Inicio, elegida.Fin)))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                porAsignar = dialogo.PorAsignar;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                datos.QuitarHoras(elegida.Id, porAsignar, IdAdministrador());
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                FalloAlEscribir("PestanaProximoCiclo.QuitarFranja", "No se pudieron quitar esas horas", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            lienzo.LimpiarSeleccion();
            Releer("Listo: se quitó " + cual + "." + (porAsignar ? " Sus horas quedan en «Falta asignar» para ponerlas en otro horario." : ""));
        }

        /// <summary>
        /// "Falta asignar: N h": la lista (DialogoFaltaAsignarPlantilla). «Ponerla...»
        /// lleva al laboratorio elegido con la clase ya elegida para el siguiente
        /// "Poner clase..." (porPoner); "Ya no hace falta" la quita de la lista.
        /// </summary>
        private void VerPorAsignar()
        {
            if (!Editable || leido == null || leido.PorAsignar.Count == 0)
            {
                return;
            }

            List<OpcionCombo> laboratorios = cboLaboratorio.Items.OfType<ItemLaboratorio>().Select(i => new OpcionCombo { Id = i.Id, Texto = i.Nombre }).ToList();
            Func<IWin32Window, PorAsignarCiclo, bool> resolver = (dueno, p) =>
            {
                CargaCiclo suya = leido.Carga(p.CargaId);
                string clase = suya == null ? "esa clase" : suya.Nombre;
                if (Permisos.Bloquear(dueno, "Dar por resueltas unas horas por asignar del horario en preparacion")
                    || !DialogoConfirmacion.Preguntar(dueno, "Ya no hace falta", "¿Ya no hace falta asignar " + HorariosDatos.Duracion(p.Minutos) + " a " + clase + "?",
                        "Salieron de " + p.Origen + ". Salen de «Falta asignar»; las horas de la clase no cambian.", "Ya no hace falta", false))
                {
                    return false;
                }

                try
                {
                    datos.YaNoHaceFalta(p.Id, IdAdministrador());
                    return true;
                }
                catch (Exception ex)
                {
                    if (ex is ErrorDeDatos || ex is AccionNoPermitida)
                    {
                        MessageBox.Show(dueno, ex.Message, "Ya no hace falta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        AvisoDeFalloAdmin.Mostrar(dueno, "PestanaProximoCiclo.YaNoHaceFalta", ex, "No se pudo quitar de «Falta asignar»", Titulo);
                    }

                    return false;
                }
            };

            PorAsignarCiclo elegida = null;
            int donde = 0;
            bool cambios;
            using (DialogoFaltaAsignarPlantilla dialogo = new DialogoFaltaAsignarPlantilla(leido, laboratorios, laboratorioElegido, resolver))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    elegida = dialogo.Elegida;
                    donde = dialogo.LaboratorioElegido;
                }

                cambios = dialogo.HuboCambios;
            }

            if (cambios && !Releer(null))
            {
                return;
            }

            if (elegida == null || leido == null || leido.Carga(elegida.CargaId) == null)
            {
                return;
            }

            // A ese laboratorio, con la clase lista para el siguiente "Poner clase...".
            porPoner = elegida.CargaId;
            lienzo.LimpiarSeleccion();
            ItemLaboratorio item = cboLaboratorio.Items.OfType<ItemLaboratorio>().FirstOrDefault(i => i.Id == donde);
            if (item != null && item.Id != laboratorioElegido)
            {
                cboLaboratorio.SelectedItem = item;
            }
            else
            {
                PintarSemana();
                AcomodarAbierta();
            }

            ActualizarBotonesSemana();
            Estado(LeyendaPorPoner(false));
            lienzo.Focus();
        }

        // =====================================================================
        // La lista de clases
        // =====================================================================

        /// <summary>"Todas las personas docentes", "Sin docente (3)" y cada docente con su color y cuantas clases tiene.</summary>
        private void LlenarDocentesFiltro()
        {
            OpcionCombo antes = cboDocenteFiltro.SelectedItem as OpcionCombo;
            List<OpcionCombo> opciones = OpcionesDeDocentes(leido);
            llenando = true;
            cboDocenteFiltro.BeginUpdate();
            try
            {
                cboDocenteFiltro.Items.Clear();
                foreach (OpcionCombo o in opciones)
                {
                    cboDocenteFiltro.Items.Add(o);
                }

                int anchoLista = opciones.Max(o => TextRenderer.MeasureText(o.Texto, cboDocenteFiltro.Font).Width) + 36 + SystemInformation.VerticalScrollBarWidth;
                cboDocenteFiltro.DropDownWidth = Math.Max(cboDocenteFiltro.Width, anchoLista);
                OpcionCombo mismo = antes == null ? null : opciones.FirstOrDefault(o => o.Id == antes.Id);
                cboDocenteFiltro.SelectedItem = (object)mismo ?? opciones[0];
            }
            finally
            {
                cboDocenteFiltro.EndUpdate();
                llenando = false;
            }
        }

        /// <summary>Las opciones del filtro de docentes de un horario; tambien para VentanaDocentesYClases.</summary>
        internal static List<OpcionCombo> OpcionesDeDocentes(CicloLeido leido)
        {
            List<CargaCiclo> vivas = leido.Cargas.Where(c => !c.Quitada).ToList();
            List<OpcionCombo> opciones = new List<OpcionCombo> { new OpcionCombo { Id = 0, Texto = "Todas las personas docentes" } };
            int sinDocente = vivas.Count(c => !c.ProfesorId.HasValue);
            if (sinDocente > 0)
            {
                opciones.Add(new OpcionCombo { Id = -1, Texto = "Sin docente (" + sinDocente + ")" });
            }

            foreach (ProfesorDelCiclo p in leido.Profesores)
            {
                int n = vivas.Count(c => c.ProfesorId == p.Id);
                if (n > 0)
                {
                    opciones.Add(new OpcionCombo { Id = p.Id, Texto = p.Nombre + " (" + RevisionCiclo.TextoClases(n) + ")" });
                }
            }

            return opciones;
        }

        /// <summary>Una opcion del filtro de docentes, con el color de cada uno; tambien para VentanaDocentesYClases.</summary>
        internal static void DibujarDocenteFiltro(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            ComboBox combo = (ComboBox)sender;
            // Mientras se vuelve a llenar puede llegar un indice que ya no esta.
            OpcionCombo opcion = e.Index >= 0 && e.Index < combo.Items.Count ? combo.Items[e.Index] as OpcionCombo : null;
            if (opcion == null)
            {
                return;
            }

            int x = e.Bounds.X + 4;
            if (opcion.Id > 0)
            {
                Rectangle cuadro = new Rectangle(x, e.Bounds.Y + 3, 14, e.Bounds.Height - 6);
                using (SolidBrush relleno = new SolidBrush(CuadriculaSemanal.ColorDeProfesor(opcion.Id)))
                {
                    e.Graphics.FillRectangle(relleno, cuadro);
                }

                e.Graphics.DrawRectangle(Pens.Gray, cuadro);
            }

            x += 20;
            bool elegida = (e.State & DrawItemState.Selected) != 0;
            Color letra = opcion.Id == -1 && !elegida ? PiezasConfiguracion.TextoError : e.ForeColor;
            TextRenderer.DrawText(e.Graphics, opcion.Texto, e.Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x, e.Bounds.Height),
                letra, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        /// <summary>Las clases que pasan el docente, la busqueda y el filtro de una pastilla, con sus horarios, cambios y avisos.</summary>
        private void PintarClases()
        {
            if (leido == null)
            {
                return;
            }

            OpcionCombo docente = cboDocenteFiltro.SelectedItem as OpcionCombo;
            int idDocente = docente == null ? 0 : docente.Id;
            string busqueda = txtBuscarClases.Text;
            List<CargaCiclo> clases = leido.Cargas
                .Where(c => !c.Quitada)
                .Where(c => idDocente == 0 || (idDocente == -1 ? !c.ProfesorId.HasValue : c.ProfesorId == idDocente))
                .Where(c => EstiloAdmin.Coincide(EstiloAdmin.Normalizar(c.Materia + " " + c.Grupo), busqueda))
                .Where(c => RevisionCiclo.EntraEnFiltro(leido, revision, c, filtro))
                .ToList();

            pastFiltro.Visible = filtro != FiltroClases.Ninguno;
            pastFiltro.Mostrado = RevisionCiclo.TextoFiltro(filtro) + "  ×";
            pastFiltro.AccessibleDescription = "Quitar el filtro";

            bool conRevision = leido.Ciclo.EnPreparacion && !soloConsulta;
            dgvClases.Columns["Cambios"].Visible = conRevision;
            dgvClases.Columns["Aviso"].Visible = conRevision;
            string frente = leido.Ciclo.FrenteA;
            dgvClases.Columns["Cambios"].ToolTipText = frente.Length > 0 ? frente : "Empezó en blanco: todas son nuevas.";

            List<int> elegidas = ClasesElegidas().Select(c => c.Id).ToList();
            dgvClases.SuspendLayout();
            dgvClases.Rows.Clear();
            foreach (CargaCiclo carga in clases)
            {
                string nombreDocente = carga.ProfesorId.HasValue
                    ? carga.Profesor + (leido.ConBajas && !carga.ProfesorActivo ? " (de baja)" : "")
                    : "Sin docente";
                string horario = carga.Franjas.Count == 0 ? "Sin horario"
                    : string.Join(Environment.NewLine, carga.Franjas.OrderBy(f => f.DiaSemana).ThenBy(f => f.Inicio).Select(f => f.Descripcion));
                ComoQueda como = revision.De(carga);
                List<AvisoDeCarga> avisos = revision.AvisosDe(carga);
                int i = dgvClases.Rows.Add(nombreDocente, carga.Materia, carga.Grupo, horario, como.Texto, string.Join(Environment.NewLine, avisos.Select(a => a.Texto)));
                DataGridViewRow fila = dgvClases.Rows[i];
                fila.Tag = carga;

                if (conRevision && revision.Bloquea(carga))
                {
                    fila.DefaultCellStyle.BackColor = PiezasConfiguracion.FondoRojo;
                    fila.DefaultCellStyle.SelectionBackColor = EstiloHorarios.Oscurecer(PiezasConfiguracion.FondoRojo, 0.12f);
                }

                PintarCeldaDocente(fila.Cells["Docente"], carga, fuenteNegrita);
                if (carga.Franjas.Count == 0)
                {
                    fila.Cells["Horario"].Style.ForeColor = PiezasConfiguracion.TextoAmbar;
                    fila.Cells["Horario"].Style.SelectionForeColor = PiezasConfiguracion.TextoAmbar;
                }

                DataGridViewCell celdaCambios = fila.Cells["Cambios"];
                Color fondoCambios = como.Tipo == TipoComoQueda.Igual ? PiezasConfiguracion.FondoVerde
                    : como.Tipo == TipoComoQueda.Nueva ? PiezasConfiguracion.FondoNueva
                    : PiezasConfiguracion.FondoAmbar;
                celdaCambios.Style.BackColor = fondoCambios;
                celdaCambios.Style.SelectionBackColor = EstiloHorarios.Oscurecer(fondoCambios, 0.12f);
                celdaCambios.Style.ForeColor = como.Tipo == TipoComoQueda.Igual ? Color.DarkGreen
                    : como.Tipo == TipoComoQueda.Nueva ? PiezasConfiguracion.TextoNueva : PiezasConfiguracion.TextoAmbar;
                celdaCambios.Style.SelectionForeColor = celdaCambios.Style.ForeColor;

                if (avisos.Count > 0)
                {
                    Color letra = avisos.Any(a => a.Bloquea) ? PiezasConfiguracion.TextoRojo : PiezasConfiguracion.TextoAmbar;
                    fila.Cells["Aviso"].Style.ForeColor = letra;
                    fila.Cells["Aviso"].Style.SelectionForeColor = letra;
                }
            }

            dgvClases.ResumeLayout();
            textoClasesVacia = leido.Cargas.All(c => c.Quitada)
                ? (Editable
                    ? "Este horario todavía no tiene clases. Pulsa «Nueva clase...» o, en la semana, arrastra sobre los cuadros libres y pulsa «Poner clase...»."
                    : "Esta plantilla no tiene clases.")
                : "Ninguna clase coincide con lo que elegiste.";
            dgvClases.ClearSelection();
            ElegirClases(elegidas, -1);
            dgvClases.Invalidate();
            ActualizarBotonesClases();
        }

        /// <summary>La celda del docente con su color, o en rojo si la clase no tiene; tambien para VentanaDocentesYClases.</summary>
        internal static void PintarCeldaDocente(DataGridViewCell celda, CargaCiclo carga, DrawingFont negrita)
        {
            if (carga.ProfesorId.HasValue)
            {
                Color color = CuadriculaSemanal.ColorDeProfesor(carga.ProfesorId.Value);
                celda.Style.BackColor = color;
                celda.Style.SelectionBackColor = EstiloHorarios.Oscurecer(color, 0.18f);
                celda.Style.SelectionForeColor = Color.Black;
            }
            else
            {
                celda.Style.ForeColor = PiezasConfiguracion.TextoError;
                celda.Style.SelectionForeColor = PiezasConfiguracion.TextoError;
                celda.Style.Font = negrita;
            }
        }

        private List<CargaCiclo> ClasesElegidas()
        {
            return dgvClases.SelectedRows.Cast<DataGridViewRow>().Select(f => f.Tag as CargaCiclo).Where(c => c != null)
                .OrderBy(c => leido == null ? 0 : leido.Cargas.IndexOf(c)).ToList();
        }

        /// <summary>Vuelve a elegir esas clases (por id) y deja a la vista la primera, o la fila de antes.</summary>
        private void ElegirClases(List<int> ids, int primeraVisible)
        {
            if (ids == null || ids.Count == 0)
            {
                return;
            }

            dgvClases.ClearSelection();
            DataGridViewRow primera = null;
            foreach (DataGridViewRow fila in dgvClases.Rows)
            {
                CargaCiclo c = fila.Tag as CargaCiclo;
                if (c != null && ids.Contains(c.Id))
                {
                    fila.Selected = true;
                    primera = primera ?? fila;
                }
            }

            if (primera == null)
            {
                return;
            }

            DataGridViewColumn columna = dgvClases.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
            if (columna != null)
            {
                dgvClases.CurrentCell = primera.Cells[columna.Index];
                foreach (DataGridViewRow fila in dgvClases.Rows)
                {
                    CargaCiclo c = fila.Tag as CargaCiclo;
                    fila.Selected = c != null && ids.Contains(c.Id);
                }
            }

            try
            {
                if (primeraVisible >= 0 && primeraVisible < dgvClases.Rows.Count)
                {
                    dgvClases.FirstDisplayedScrollingRowIndex = primeraVisible;
                }
            }
            catch (InvalidOperationException)
            {
                // Sin sitio para desplazarse todavia (la tabla aun no tiene su tamano).
            }
        }

        private void ActualizarBotonesClases()
        {
            int elegidas = dgvClases.SelectedRows.Count;
            btnVerEnSemana.Enabled = elegidas == 1;
            btnCambiarClase.Enabled = elegidas == 1;
            btnQuitarClases.Enabled = elegidas >= 1;
        }

        private void VerElegidaEnSemana()
        {
            List<CargaCiclo> elegidas = ClasesElegidas();
            if (elegidas.Count == 1)
            {
                VerEnSemana(elegidas[0]);
            }
        }

        /// <summary>
        /// "Ver en el horario" de la lista de clases (como en Clases de Horarios):
        /// la semana en el laboratorio donde la clase tiene mas horas, con su
        /// primera franja ahi elegida y su docente resaltado. Sin horas, lo dice y
        /// como ponerlas.
        /// </summary>
        private void VerEnSemana(CargaCiclo carga)
        {
            if (carga == null || leido == null)
            {
                return;
            }

            if (carga.Franjas.Count == 0)
            {
                Estado(carga.Nombre + " todavía no tiene horas" + (Editable ? ": en la semana elige cuadros libres y pulsa «Poner clase...»." : " en esta plantilla."), true);
                return;
            }

            int laboratorio = carga.Franjas
                .GroupBy(f => f.LaboratorioId)
                .OrderByDescending(g => g.Sum(f => (f.Fin - f.Inicio).TotalHours))
                .ThenBy(g => g.Min(f => f.DiaSemana * 10000 + (int)f.Inicio.TotalMinutes))
                .First().Key;
            bool otroLaboratorio = laboratorio != laboratorioElegido;
            vista = Vista.Semana;
            vistaRecordada = Vista.Semana;
            filtro = FiltroClases.Ninguno;
            laboratorioElegido = laboratorio;
            laboratorioRecordado = laboratorio;
            profesorFijado = carga.ProfesorId ?? 0;
            lienzo.LimpiarSeleccion();
            PintarAbierta();
            lienzo.Seleccionada = lienzo.Franjas.Where(f => f.ClaseId == carga.Id).OrderBy(f => f.DiaSemana).ThenBy(f => f.Inicio).FirstOrDefault();
            ActualizarBotonesSemana();
            Estado(null);
            if (otroLaboratorio && LaboratorioCambiado != null)
            {
                LaboratorioCambiado(laboratorio);
            }

            lienzo.Focus();
        }

        /// <summary>
        /// "Imprimir PDF": el horario del laboratorio que se ve en una hoja, como
        /// en Horario semanal (PdfHorario), pero de una semana tipo, sin fechas ni
        /// codigos, en el visor de PDF de DigLab (ahi se ve y, si se quiere, se guarda).
        /// </summary>
        // =====================================================================
        // El horario en Excel (pedido del 2026-11-12)
        // =====================================================================

        /// <summary>Soltar un Excel en ese panel y en todo lo que tenga dentro (tambien lo que se agregue despues).</summary>
        private void PermitirSoltarExcel(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += AlArrastrarExcel;
            control.DragOver += AlArrastrarExcel;
            control.DragDrop += AlSoltarExcel;
            // Si se lleva el archivo afuera sin soltarlo, la linea de estado deja de decir "Suelta...".
            control.DragLeave += (s, e) =>
            {
                Label linea = panelVisible == panelAbierta ? lblEstadoAbierta : panelVisible == panelVacio ? lblEstadoVacio : null;
                if (linea != null && linea.Text.StartsWith("Suelta «", StringComparison.Ordinal))
                {
                    Estado(null);
                }
            };
            control.ControlAdded += (s, e) => PermitirSoltarExcel(e.Control);
            foreach (Control hijo in control.Controls)
            {
                PermitirSoltarExcel(hijo);
            }
        }

        /// <summary>El unico archivo que se arrastra, si es un Excel y aqui se puede soltar; si no, null.</summary>
        private string ExcelArrastrado(DragEventArgs e)
        {
            if (Kiosco.Activo || soloConsulta || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return null;
            }

            bool aqui = (panelVisible == panelAbierta && Editable) || (panelVisible == panelVacio && btnPrepararVacio.Visible);
            string[] archivos = e.Data.GetData(DataFormats.FileDrop) as string[];
            return aqui && archivos != null && archivos.Length == 1 && ExcelDeHorario.EsExcel(archivos[0]) ? archivos[0] : null;
        }

        private void AlArrastrarExcel(object sender, DragEventArgs e)
        {
            string ruta = ExcelArrastrado(e);
            e.Effect = ruta != null ? DragDropEffects.Copy : DragDropEffects.None;
            if (ruta != null)
            {
                string texto = panelVisible == panelVacio ? "Suelta «" + Path.GetFileName(ruta) + "» para crear el horario con él."
                    : "Suelta «" + Path.GetFileName(ruta) + "» para cargarlo en este horario (antes te dice qué cambia).";
                Label linea = panelVisible == panelAbierta ? lblEstadoAbierta : lblEstadoVacio;
                if (linea.Text != texto)
                {
                    Estado(texto);
                }
            }
        }

        private void AlSoltarExcel(object sender, DragEventArgs e)
        {
            string ruta = ExcelArrastrado(e);
            if (ruta == null)
            {
                return;
            }

            // Despues de soltar: el Explorador no se queda esperando mientras se ven los dialogos.
            BeginInvoke(new Action(() => CargarExcel(ruta)));
        }

        /// <summary>"Cargar un Excel en este horario...": se elige el archivo.</summary>
        private void ElegirExcel()
        {
            if (!Editable)
            {
                return;
            }

            using (OpenFileDialog dialogo = new OpenFileDialog
            {
                Title = "Cargar un Excel en «" + leido.Ciclo.Nombre + "»",
                Filter = "Libro de Excel (*.xlsx)|*.xlsx;*.xlsm|Todos los archivos (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (dialogo.ShowDialog(EstiloHorarios.Dueno(this)) == DialogResult.OK)
                {
                    CargarExcel(dialogo.FileName);
                }
            }
        }

        /// <summary>"Descargar este horario en Excel": la plantilla con lo que ya tiene, para llenarla.</summary>
        private void DescargarExcel()
        {
            if (leido == null || Kiosco.Activo)
            {
                return;
            }

            byte[] libro;
            Cursor = Cursors.WaitCursor;
            try
            {
                libro = HorarioEnExcel.Crear(leido, datos.LeerCatalogosDeHorario(), leido.Ciclo.Nombre);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MostrarFallo("PestanaProximoCiclo.DescargarExcel", "No se pudo preparar el Excel del horario", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            string guardado = ExcelDeHorario.Guardar(EstiloHorarios.Dueno(this), libro, leido.Ciclo.Nombre);
            if (guardado != null)
            {
                Estado("Listo: se guardó «" + Path.GetFileName(guardado) + "». Llénalo en Excel y cárgalo aquí con «Excel ▾» o arrastrándolo hasta el horario.");
            }
        }

        /// <summary>
        /// Carga un Excel: sin horario en preparacion, crea uno con el (Crear horario nuevo,
        /// "Desde un Excel"); con uno, lo revisa contra lo registrado y, si todo esta
        /// registrado, ensena que cambia en cada laboratorio y lo carga si se confirma.
        /// Con algo por corregir lo dice y no carga nada.
        /// </summary>
        private void CargarExcel(string ruta)
        {
            if (Permisos.Bloquear(this, "Cargar un Excel en el horario en preparacion"))
            {
                return;
            }

            if (panelVisible == panelVacio)
            {
                CrearHorario(null, ruta);
                return;
            }

            if (!Editable)
            {
                return;
            }

            string archivo = Path.GetFileName(ruta);
            int idCiclo = leido.Ciclo.Id;
            LecturaDeHorario lectura;
            string error;
            Cursor = Cursors.WaitCursor;
            try
            {
                // Lo de ahora (otra ventana pudo cambiarlo), para comparar con el Excel.
                CicloLeido actual = datos.Leer(idCiclo);
                if (actual == null || !actual.Ciclo.EnPreparacion)
                {
                    Cursor = Cursors.Default;
                    VolverALeerTrasFallo("Este horario ya no está en preparación: no se cargó el Excel.");
                    return;
                }

                lectura = ExcelDeHorario.Leer(ruta, datos, actual, out error);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MostrarFallo("PestanaProximoCiclo.CargarExcel (leer)", "No se pudo revisar el Excel", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (lectura == null)
            {
                MessageBox.Show(EstiloHorarios.Dueno(this), error, "Cargar un Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Estado("No se cargó nada: " + error, true);
                return;
            }

            if (!lectura.SePuedeCargar)
            {
                using (DialogoProblemasExcel problemas = new DialogoProblemasExcel(archivo, lectura))
                {
                    problemas.ShowDialog(EstiloHorarios.Dueno(this));
                }

                Estado("No se cargó nada: «" + archivo + "» tiene " + (lectura.Problemas.Count == 1 ? "una cosa" : lectura.Problemas.Count + " cosas")
                    + " por corregir.", true);
                return;
            }

            using (DialogoConfirmarExcel confirmar = new DialogoConfirmarExcel(archivo, leido.Ciclo.Nombre, lectura, false))
            {
                if (confirmar.ShowDialog(EstiloHorarios.Dueno(this)) != DialogResult.OK)
                {
                    Estado("No se cargó el Excel: se canceló.");
                    return;
                }
            }

            ResultadoCargaExcel resultado;
            Cursor = Cursors.WaitCursor;
            try
            {
                resultado = datos.CargarDesdeExcel(idCiclo, lectura.Laboratorios.Select(l => l.Id).ToList(), lectura.Clases, IdAdministrador());
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                FalloAlEscribir("PestanaProximoCiclo.CargarExcel", "No se pudo cargar el Excel", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            // Si se veia un laboratorio que no venia en el Excel, se ve el primero del Excel.
            if (!lectura.Laboratorios.Any(l => l.Id == laboratorioElegido))
            {
                laboratorioElegido = lectura.Laboratorios[0].Id;
                laboratorioRecordado = laboratorioElegido;
            }

            int labs = lectura.Laboratorios.Count;
            Releer("Listo: se cargó «" + archivo + "» en " + (labs == 1 ? EstiloHorarios.NombreLaboratorio(lectura.Laboratorios[0].Nombre) : labs + " laboratorios") + ": "
                + RevisionCiclo.TextoHorarios(resultado.Franjas)
                + (resultado.Nuevas > 0 ? ", " + RevisionCiclo.TextoClases(resultado.Nuevas) + (resultado.Nuevas == 1 ? " nueva" : " nuevas") : "")
                + (resultado.Quitadas > 0 ? "; se " + (resultado.Quitadas == 1 ? "quitó 1 clase que no venía" : "quitaron " + resultado.Quitadas + " clases que no venían") : "")
                + ".");
        }

        private void ImprimirPdf()
        {
            ItemLaboratorio laboratorio = cboLaboratorio.SelectedItem as ItemLaboratorio;
            if (leido == null || laboratorio == null || Kiosco.Activo)
            {
                return;
            }

            CicloPreliminar ciclo = leido.Ciclo;
            string queEs = ciclo.EnPreparacion ? "Próximo ciclo «" + ciclo.Nombre + "», en preparación: semana tipo, sin fechas"
                : ciclo.Guardada ? "Plantilla guardada «" + ciclo.Nombre + "»: semana tipo, sin fechas"
                : "Horario aplicado «" + ciclo.Nombre + "»" + (ciclo.Desde.HasValue && ciclo.Hasta.HasValue
                    ? ", del " + PiezasConfiguracion.Fecha(ciclo.Desde.Value) + " al " + PiezasConfiguracion.Fecha(ciclo.Hasta.Value) : "")
                  + ": así se aplicó";
            byte[] pdf;
            Cursor = Cursors.WaitCursor;
            try
            {
                // El pie dice cuando se genero: la hora del servidor (sin servidor, la de este equipo).
                DateTime ahora = DateTime.Now;
                try
                {
                    ahora = new Consultas().AhoraServidor();
                }
                catch (Exception ex)
                {
                    Log.Error("PestanaProximoCiclo.ImprimirPdf (hora del servidor)", ex);
                }

                pdf = PdfHorario.CrearDePlantilla(laboratorio.Laboratorio, queEs, lienzo.Franjas, ahora);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MostrarFallo("PestanaProximoCiclo.ImprimirPdf", "No se pudo preparar el PDF del horario", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            try
            {
                VisorPdf.Mostrar(this, pdf, PdfBitacora.NombreArchivo("Horario", laboratorio.Laboratorio, ciclo.Nombre));
            }
            catch (Exception ex)
            {
                // Un fallo de archivo aqui es el temporal de la vista previa, no uno que se eligio.
                AvisoDeFalloAdmin.MostrarVistaPrevia(EstiloHorarios.Dueno(this), "PestanaProximoCiclo.ImprimirPdf (visor)", ex,
                    "No se pudo mostrar el PDF del horario", Titulo);
            }
        }

        /// <summary>"Ver en la lista de clases": esa clase elegida en la lista, sin filtros que la escondan.</summary>
        private void VerEnClases(CargaCiclo carga)
        {
            if (carga == null)
            {
                return;
            }

            vista = Vista.Clases;
            vistaRecordada = Vista.Clases;
            filtro = FiltroClases.Ninguno;
            llenando = true;
            txtBuscarClases.Text = "";
            esperaBusqueda.Stop();
            if (cboDocenteFiltro.Items.Count > 0)
            {
                cboDocenteFiltro.SelectedIndex = 0;
            }

            llenando = false;
            lienzo.LimpiarSeleccion();
            PintarAbierta();
            Estado(null);
            ElegirClases(new List<int> { carga.Id }, -1);
            dgvClases.Focus();
        }

        private void NuevaClase()
        {
            if (!Editable || Permisos.Bloquear(this, "Agregar una clase al horario en preparacion"))
            {
                return;
            }

            AbrirDialogoDeClase(null);
        }

        private void CambiarClaseElegida()
        {
            List<CargaCiclo> elegidas = ClasesElegidas();
            if (elegidas.Count == 1)
            {
                CambiarClase(elegidas[0]);
            }
        }

        private void CambiarClase(CargaCiclo carga)
        {
            if (!Editable || carga == null || Permisos.Bloquear(this, "Cambiar una clase del horario en preparacion"))
            {
                return;
            }

            AbrirDialogoDeClase(carga);
        }

        /// <summary>"Clase del horario": docente, grupo, materia y sus horas (una nueva, o la que se cambia).</summary>
        private void AbrirDialogoDeClase(CargaCiclo carga)
        {
            int idCarga;
            using (DialogoClaseDelHorario dialogo = new DialogoClaseDelHorario(datos, leido, carga, null, laboratorioElegido, IdAdministrador))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    if (dialogo.HuboFallo)
                    {
                        // Pudo cambiarla, aplicarla o descartarla otra persona de la administracion: lo de ahora.
                        VolverALeerTrasFallo(null);
                    }

                    return;
                }

                idCarga = dialogo.IdCarga;
            }

            lienzo.LimpiarSeleccion();
            if (!Releer(null))
            {
                return;
            }

            CargaCiclo quedo = leido == null ? null : leido.Carga(idCarga);
            if (quedo != null)
            {
                Estado("Listo: " + quedo.Nombre + " (" + (quedo.ProfesorId.HasValue ? quedo.ProfesorCorto : "sin docente") + ") quedó con "
                    + RevisionCiclo.TextoHorarios(quedo.Franjas.Count) + ".");
                if (vista == Vista.Clases)
                {
                    ElegirClases(new List<int> { quedo.Id }, -1);
                }
            }
        }

        private void QuitarClasesElegidas()
        {
            QuitarClases(ClasesElegidas());
        }

        private void QuitarClases(List<CargaCiclo> cargas)
        {
            if (!Editable || cargas == null || cargas.Count == 0 || Permisos.Bloquear(this, "Quitar clases del horario en preparacion"))
            {
                return;
            }

            int horariosQueSeVan = cargas.Sum(c => c.Franjas.Count);
            string conHorarios = horariosQueSeVan == 0 ? "" : horariosQueSeVan == 1 ? " con su horario" : " con sus " + horariosQueSeVan + " horarios";
            bool una = cargas.Count == 1;
            if (!DialogoConfirmacion.Preguntar(this, una ? "Quitar clase" : "Quitar clases",
                una ? "¿Quitar " + cargas[0].Nombre + " de este horario?" : "¿Quitar " + cargas.Count + " clases de este horario?",
                (una ? "Se quita" : "Se quitan") + conHorarios + ". Lo que se usa hoy no cambia.", "Quitar"))
            {
                return;
            }

            int quitadas;
            Cursor = Cursors.WaitCursor;
            try
            {
                quitadas = datos.QuitarClases(cargas.Select(c => c.Id).ToList(), IdAdministrador());
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                FalloAlEscribir("PestanaProximoCiclo.QuitarClases", una ? "No se pudo quitar la clase" : "No se pudieron quitar las clases", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            lienzo.LimpiarSeleccion();
            Releer(una ? "Listo: se quitó " + cargas[0].Nombre + "." : "Listo: se quitaron " + RevisionCiclo.TextoClases(quitadas) + ".");
        }
    }

    // =========================================================================
    // Dialogo: poner una clase eligiendo dia y horas, y cambiar horas
    // =========================================================================

    /// <summary>
    /// "Poner una clase" (eligiendo dia y horas) y "Cambiar horas" de la
    /// preparacion de Proximo ciclo: el mismo dialogo que en Horario semanal
    /// (DialogoFranja), con la persona docente, su clase (materia - grupo), el
    /// dia, el inicio y el fin. No escribe: lo elegido queda en CargaId y
    /// Franja. Los choques con otras clases (docente o grupo en otro
    /// laboratorio) se dicen en rojo mientras se elige y no impiden guardar:
    /// impiden aplicar. Encima de otra clase del laboratorio, al guardar
    /// pregunta si la reemplaza (ConfirmarReemplazoEnPreparacion). El
    /// laboratorio es el que se mira.
    /// </summary>
    internal class DialogoHorasDePlantilla : Form
    {
        private readonly Func<CicloLeido> leido;
        private readonly OpcionCombo laboratorio;
        private readonly FranjaCiclo existente;
        private readonly SelectorClase selector;
        private readonly ComboBox cboDia;
        private readonly ComboBox cboInicio;
        private readonly ComboBox cboFin;
        private readonly Label lblChoques;
        private readonly Label lblError;

        /// <summary>La clase de la plantilla (idcarga) a la que van las horas.</summary>
        public int CargaId { get; private set; }

        /// <summary>El dia y las horas elegidos, en el laboratorio del dialogo.</summary>
        public FranjaCiclo Franja { get; private set; }

        /// <param name="leido">La plantilla como esta ahora (cambia si "Nueva clase..." crea una): para los choques.</param>
        /// <param name="cargaActual">Al cambiar horas: la clase de esas horas; null en una nueva.</param>
        /// <param name="existente">Al cambiar horas: las horas que se cambian; null en una nueva.</param>
        /// <param name="crearClase">"Nueva clase...": solo al poner una nueva (como en Horario semanal).</param>
        public DialogoHorasDePlantilla(Func<CicloLeido> leido, List<ClaseInfo> clases, OpcionCombo laboratorio, CargaCiclo cargaActual, FranjaCiclo existente,
            int? diaInicial, TimeSpan? horaInicial, Func<IWin32Window, List<ClaseInfo>> crearClase)
        {
            this.leido = leido ?? (() => null);
            this.laboratorio = laboratorio;
            this.existente = existente;
            EstiloHorarios.PrepararDialogo(this, existente == null ? TextosDelHorario.TituloPoner : TextosDelHorario.TituloCambiar, new Size(560, 464));

            Label lblTitulo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Size = new Size(512, 30),
                Text = (existente == null ? "Poner una clase en " : "Cambiar horas de ") + laboratorio.Texto
            };

            selector = new SelectorClase(clases, 506, existente == null ? crearClase : null) { Location = new Point(24, 56) };

            Label lblDia = EstiloHorarios.CrearEtiqueta("Día", new Point(24, 202));
            cboDia = EstiloHorarios.CrearCombo(new Point(110, 198), 150);
            // De lunes a sabado, como la cuadricula (2026-11-03); unas que ya estan el domingo lo traen para verse.
            List<OpcionCombo> dias = TextosDelHorario.DiasParaElegir(existente != null ? existente.DiaSemana : (int?)null);
            int diaElegido = existente != null ? existente.DiaSemana : (diaInicial ?? 1);
            EstiloHorarios.Rellenar(cboDia, dias, dias.Any(d => d.Id == diaElegido) ? diaElegido : 1);

            Label lblInicio = EstiloHorarios.CrearEtiqueta("Inicio", new Point(24, 246));
            cboInicio = EstiloHorarios.CrearComboHoras(new Point(110, 242), null);
            Label lblFin = EstiloHorarios.CrearEtiqueta("Fin", new Point(230, 246));
            cboFin = EstiloHorarios.CrearComboHoras(new Point(280, 242), null);
            if (existente != null)
            {
                cboInicio.Text = HorariosDatos.Hora(existente.Inicio);
                cboFin.Text = HorariosDatos.Hora(existente.Fin);
            }
            else if (horaInicial.HasValue)
            {
                // Como en Horario semanal: dos horas desde la del cuadro (sin pasar del cierre).
                TimeSpan fin = horaInicial.Value + TimeSpan.FromHours(2);
                TimeSpan tope = horaInicial.Value >= TimeSpan.FromHours(EstiloHorarios.HoraCierre) ? new TimeSpan(23, 59, 0) : TimeSpan.FromHours(EstiloHorarios.HoraCierre);
                cboInicio.Text = HorariosDatos.Hora(horaInicial.Value);
                cboFin.Text = HorariosDatos.Hora(fin <= tope ? fin : tope);
            }

            // Los choques con otras clases, en vivo (no impiden guardar).
            lblChoques = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold),
                ForeColor = PiezasConfiguracion.TextoRojo,
                Location = new Point(24, 282),
                Size = new Size(510, 52)
            };
            Label lblNota = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Regular),
                ForeColor = Color.Gray,
                Location = new Point(24, 336),
                Size = new Size(510, 36),
                Text = "Encima de otra clase de este laboratorio, pregunta si la reemplaza. Un choque (docente o grupo en otro laboratorio) "
                    + "se guarda en rojo y no deja aplicar."
            };
            lblError = EstiloAdmin.LineaError(new Point(24, 372), 510, 22);

            Button btnGuardar = EstiloAdmin.BotonPrincipal("Guardar", new Point(249, 400), new Size(135, 40));
            btnGuardar.Click += (s, e) => Guardar();
            Button btnCancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(396, 400), new Size(140, 40));
            btnCancelar.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = btnGuardar;
            CancelButton = btnCancelar;

            Controls.Add(lblTitulo);
            Controls.Add(selector);
            Controls.Add(lblDia);
            Controls.Add(cboDia);
            Controls.Add(lblInicio);
            Controls.Add(cboInicio);
            Controls.Add(lblFin);
            Controls.Add(cboFin);
            Controls.Add(lblChoques);
            Controls.Add(lblNota);
            Controls.Add(lblError);
            Controls.Add(btnGuardar);
            Controls.Add(btnCancelar);

            if (cargaActual != null)
            {
                selector.Seleccionar(clases.FirstOrDefault(c => c.Id == cargaActual.Id));
            }

            selector.Cambio += RevisarChoques;
            cboDia.SelectedIndexChanged += (s, e) => RevisarChoques();
            cboInicio.TextChanged += (s, e) => RevisarChoques();
            cboFin.TextChanged += (s, e) => RevisarChoques();
            RevisarChoques();
            ActiveControl = existente != null ? (Control)cboInicio : selector;
        }

        /// <summary>Una nueva con una clase ya elegida (la traida de "Falta asignar" con «Ponerla...»).</summary>
        public void Preelegir(ClaseInfo clase)
        {
            if (existente == null && clase != null)
            {
                selector.Seleccionar(clase);
            }
        }

        protected override void Dispose(bool disposing)
        {
            List<DrawingFont> letras = disposing ? PiezasConfiguracion.LetrasDe(this, Font) : new List<DrawingFont>();
            base.Dispose(disposing);
            foreach (DrawingFont letra in letras)
            {
                letra.Dispose();
            }
        }

        /// <summary>Lo elegido como franja, o null si falta algo (el porque, en porQue).</summary>
        private FranjaCiclo Leida(out string porQue)
        {
            porQue = null;
            OpcionCombo dia = cboDia.SelectedItem as OpcionCombo;
            if (selector.ClaseElegida == null || dia == null)
            {
                porQue = selector.ProfesorElegido ? "Elige cuál de sus clases." : "Elige a la persona docente, su clase y el día.";
                return null;
            }

            TimeSpan inicio;
            TimeSpan fin;
            if (!EstiloHorarios.LeerHora(cboInicio, out inicio) || !EstiloHorarios.LeerHora(cboFin, out fin))
            {
                porQue = "Escribe las horas como HH:mm (por ejemplo 08:00).";
                return null;
            }

            if (fin <= inicio)
            {
                porQue = "La hora de fin tiene que ser posterior a la de inicio.";
                return null;
            }

            return new FranjaCiclo { LaboratorioId = laboratorio.Id, Laboratorio = laboratorio.Texto, DiaSemana = dia.Id, Inicio = inicio, Fin = fin };
        }

        /// <summary>Con que chocarian esas horas de esa clase (sin contar las que se cambian ni las suyas: esas no pueden cruzarse).</summary>
        private void RevisarChoques()
        {
            lblError.Text = "";
            string porQue;
            FranjaCiclo franja = Leida(out porQue);
            CicloLeido plantilla = leido();
            CargaCiclo carga = franja == null || plantilla == null ? null : plantilla.Carga(selector.ClaseElegida.Id);
            if (carga == null)
            {
                lblChoques.Text = "";
                return;
            }

            List<string> choques = RevisionCiclo.ChoquesSi(plantilla, carga.ProfesorId, carga.GrupoId, new[] { franja }, new List<int> { carga.Id },
                existente == null ? null : new List<int> { existente.Id });
            lblChoques.Text = choques.Count == 0 ? ""
                : choques[0] + (choques.Count > 1 ? " Y " + (choques.Count - 1) + (choques.Count == 2 ? " choque más." : " choques más.") : "");
        }

        private void Guardar()
        {
            string porQue;
            FranjaCiclo franja = Leida(out porQue);
            if (franja == null)
            {
                lblError.Text = porQue;
                return;
            }

            // Encima de otra clase de este laboratorio: la reemplaza, si se confirma.
            CicloLeido plantilla = leido();
            if (plantilla != null && !ConfirmarReemplazoEnPreparacion.Preguntar(this, plantilla, selector.ClaseElegida.Id, new List<FranjaCiclo> { franja },
                existente == null ? null : new List<int> { existente.Id }))
            {
                return;
            }

            CargaId = selector.ClaseElegida.Id;
            Franja = franja;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// En la preparacion de Proximo ciclo, antes de poner unas horas encima de
    /// otras clases del mismo laboratorio: que se les quita (RevisionCiclo.Reemplazos).
    /// Como ConfirmarReemplazo de Horario semanal: lo que se les quita queda en
    /// "Falta asignar" (2026-11-03); la que se queda sin horas sale tambien en "Sin horario".
    /// </summary>
    internal static class ConfirmarReemplazoEnPreparacion
    {
        /// <summary>El texto de la confirmacion (tambien lo usan las pruebas).</summary>
        public static string Texto(IList<FranjaReemplazada> quitadas, IList<CargaCiclo> sinHoras)
        {
            string texto = quitadas.Count == 1
                ? "Esto quita a " + quitadas[0].Describir() + " de este horario en preparación. Quedará en «Falta asignar» para ponerla en otro horario."
                : "Esto quita de este horario en preparación:" + Environment.NewLine
                    + string.Join(Environment.NewLine, quitadas.Select(q => "   • " + q.Describir())) + Environment.NewLine
                    + "Quedarán en «Falta asignar» para ponerlas en otro horario.";
            if (sinHoras.Count > 0)
            {
                texto += Environment.NewLine + Environment.NewLine
                    + (sinHoras.Count == 1
                        ? sinHoras[0].Nombre + " se queda sin horas: sale en «Sin horario»."
                        : RevisionCiclo.Enumerar(sinHoras.Select(c => c.Nombre).ToList()) + " se quedan sin horas: salen en «Sin horario».");
            }

            return texto + Environment.NewLine + Environment.NewLine + "Lo que se usa hoy no cambia.";
        }

        /// <summary>True si no se le quita nada a nadie o si se confirma "Reemplazar".</summary>
        public static bool Preguntar(IWin32Window dueno, CicloLeido leido, int idCarga, IList<FranjaCiclo> nuevas, ICollection<int> sinFranjas)
        {
            List<FranjaReemplazada> quitadas = RevisionCiclo.Reemplazos(leido, idCarga, nuevas, sinFranjas);
            if (quitadas.Count == 0)
            {
                return true;
            }

            using (DialogoTextoYBotones dialogo = new DialogoTextoYBotones("Reemplazar una clase",
                Texto(quitadas, RevisionCiclo.SeQuedanSinHoras(leido, nuevas, quitadas)), "Reemplazar", false))
            {
                return dialogo.ShowDialog(dueno) == DialogResult.OK;
            }
        }
    }

    // =========================================================================
    // Piezas dibujadas a mano
    // =========================================================================

    /// <summary>
    /// Una pastilla redonda (los pasos, la marca, los choques...). Es un Button
    /// pintado a mano, como BotonCabecera: Tab, Enter/Espacio y la accion MSAA
    /// son las de cualquier boton. Text no cambia nunca (es lo que buscan las
    /// pruebas); lo que se ve, con el numero, va en Mostrado y en AccessibleName.
    /// </summary>
    internal class PastillaConfiguracion : Button
    {
        private readonly int anchoFijo;
        private string mostrado = "";
        private Color fondo = Color.White;
        private Color borde = Color.DarkGreen;
        private Color letra = Color.DarkGreen;
        private bool encima;
        private bool pulsado;

        /// <param name="anchoFijo">0: a la medida de lo que dice.</param>
        /// <param name="clicable">Si hace algo al pulsarla (mano, se oscurece, entra con Tab); si no, solo informa.</param>
        public PastillaConfiguracion(string textoFijo, int anchoFijo, int alto, DrawingFont fuente, bool clicable)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.White;
            Font = fuente;
            Text = textoFijo;
            this.anchoFijo = anchoFijo;
            Clicable = clicable;
            Cursor = clicable ? Cursors.Hand : Cursors.Default;
            TabStop = clicable;
            Height = alto;
            Mostrado = textoFijo;
        }

        public bool Clicable { get; private set; }

        /// <summary>Lo que se ve ("Choques: 2"); tambien su nombre para los lectores de pantalla.</summary>
        public string Mostrado
        {
            get { return mostrado; }
            set
            {
                mostrado = value ?? "";
                AccessibleName = mostrado;
                Width = anchoFijo > 0 ? anchoFijo : TextRenderer.MeasureText(mostrado, Font).Width + 22;
                Invalidate();
            }
        }

        public void Colores(Color nuevoFondo, Color nuevoBorde, Color nuevaLetra)
        {
            fondo = nuevoFondo;
            borde = nuevoBorde;
            letra = nuevaLetra;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            encima = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            encima = false;
            pulsado = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pulsado = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        // Se suelta antes de llamar a base: el clic puede abrir un dialogo modal.
        protected override void OnMouseUp(MouseEventArgs e)
        {
            pulsado = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            pulsado = false;
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color relleno = fondo;
            if (Clicable && Enabled)
            {
                relleno = pulsado ? EstiloHorarios.Oscurecer(fondo, 0.14f) : encima ? EstiloHorarios.Oscurecer(fondo, 0.06f) : fondo;
            }

            Rectangle zona = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(zona, zona.Height / 2))
            using (SolidBrush pincel = new SolidBrush(relleno))
            using (Pen lapiz = new Pen(borde, 1.5f))
            {
                g.FillPath(pincel, forma);
                g.DrawPath(lapiz, forma);
            }

            TextRenderer.DrawText(g, mostrado, Font, ClientRectangle, Enabled ? letra : Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues && Clicable)
            {
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(zona, -4, -4), letra, relleno);
            }
        }
    }

    /// <summary>Una tarjeta con las esquinas redondas y borde: "Falta preparar la base" y el paso 2 sin nada en preparacion.</summary>
    internal class TarjetaConfiguracion : Panel
    {
        private Color borde = Color.DarkGreen;
        private int grosor = 2;

        public TarjetaConfiguracion()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            Radio = 12;
        }

        public int Radio { get; set; }

        /// <summary>El fondo (lo heredan sus etiquetas), el borde y su grosor.</summary>
        public void Pintar(Color relleno, Color nuevoBorde, int nuevoGrosor)
        {
            BackColor = relleno;
            borde = nuevoBorde;
            grosor = nuevoGrosor;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Todo en OnPaint: fuera de las esquinas, el color de donde esta.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle zona = new Rectangle(grosor / 2, grosor / 2, Width - grosor - 1, Height - grosor - 1);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(zona, Radio))
            using (SolidBrush pincel = new SolidBrush(BackColor))
            using (Pen lapiz = new Pen(borde, grosor))
            {
                g.FillPath(pincel, forma);
                g.DrawPath(lapiz, forma);
            }

            base.OnPaint(e);
        }
    }
}
