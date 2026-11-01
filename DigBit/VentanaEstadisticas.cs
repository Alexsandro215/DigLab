using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigBit
{
    /// <summary>
    /// Estadisticas de toda la escuela (paquetes 4a y 4b de su especificacion, con
    /// las decisiones del usuario del 2026-10-21, 2026-10-22 y 2026-10-31): arriba el
    /// periodo y cinco cifras grandes de todo el TESCHI, que llevan a su pestana, y
    /// abajo siete pestanas, cada una con SUS filtros (2026-10-31, el usuario: "estas
    /// opciones deberian estar en sus secciones y no en la general"; eligio "cada
    /// apartado, los suyos"): Estudiantes (con "Sus clases" y "Ver sus bitacoras"),
    /// Clases (con "Estudiantes de la clase"), Grupos, Docentes (SOLO la asistencia de
    /// los docentes, con sus clases dia por dia y la bitacora de cada una con doble
    /// clic), Laboratorios, Equipos (de un laboratorio a la vez) y Graficas.
    ///
    /// Nunca lee en el hilo de la pantalla: EstadisticasDatos.Leer va en Task.Run con
    /// un numero de lectura (la que llega tarde se descarta). Se lee una vez por
    /// periodo; cada pestana calcula con sus filtros (EstadisticasDatos.Calcular, en
    /// segundo plano y sin ir a la base) y lo calculado se guarda por filtro mientras
    /// no cambien la lectura ni el periodo: dos pestanas con los mismos filtros (o con
    /// "Todos", como las cifras) comparten el calculo. Nunca escribe en la base: la
    /// persona responsable del laboratorio la ve igual, y solo guarda en CSV
    /// Laboratorios, Grupos, Equipos y Graficas. En kiosco no hay CSV ni se puede
    /// copiar de sus tablas.
    ///
    /// Dos disposiciones, como Mis grupos: amplia desde 1600 x 860 de cliente (todas
    /// las columnas) y compacta por debajo (lo que no cabe va en la ayuda de la fila).
    /// Con menos de 640 de alto (1366x768 con Windows al 125 %) se quita el subtitulo
    /// para que quepan las tablas.
    /// </summary>
    internal sealed class VentanaEstadisticas : PantallaAdmin
    {
        private const string Titulo = "Estadísticas";
        private const string Subtitulo = "Arriba, las cifras generales del periodo en todo el TESCHI; abajo, cada apartado con sus filtros. F5 actualiza.";
        private const string AyudaGeneral = "Es de todo el TESCHI en el periodo: no cambia con los filtros de los apartados de abajo.";
        private const string TextoLeyendo = "Leyendo las estadísticas...";
        private const string TextoCalculando = "Calculando...";
        private const string TituloCsv = "Guardar en Excel (CSV)";
        private const string AyudaCsv = "Guarda la pestaña que ves, con todas sus columnas y las filas que deja la búsqueda, "
            + "en un archivo que Excel abre con doble clic.";
        // Encima del CSV apagado de la persona responsable (Permisos.MotivoVisible); el
        // texto largo (SinCsvParaSoloConsulta) sigue en la ayuda del raton.
        private const string MotivoSinCsvALaVista = "Esta pestaña lleva nombres: el Excel te lo da la administración.";
        // De lo de abajo de las pestanas al borde: 10 px mas en solo consulta, para que
        // quepa la linea de MotivoVisible entre las pestanas y los botones.
        private static int AbajoDeLasPestanas
        {
            get { return Permisos.SoloConsulta ? 82 : 72; }
        }

        private const int AnchoBuscador = 220;
        private const int Separacion = 10;
        private const int AltoMinimoNormal = 640;
        private const int AltoCifrasBaja = 78;
        // La primera fila de cada pestana (sus listas y Buscar) y lo que hay entre una lista y la siguiente.
        private const int AltoFila = 26;
        private const int EntreListas = 16;

        private static readonly Color Gris = Color.FromArgb(150, 150, 150);
        private static readonly Color GrisElegida = Color.FromArgb(105, 105, 105);
        private static readonly Color TextoGris = Color.FromArgb(105, 105, 105);
        private static readonly Color TextoOscuro = Color.FromArgb(40, 40, 40);
        private static readonly Color RojoTexto = HojaAsistencia.FaltaTexto;
        private static readonly Color VerdeTexto = Color.FromArgb(46, 125, 50);
        private static readonly Color VerdeOscuro = Color.FromArgb(27, 94, 32);
        private static readonly Color NaranjaTexto = Color.FromArgb(190, 85, 0);
        private static readonly Color FondoTodos = Color.FromArgb(236, 246, 236);
        private static readonly Color FondoRepetido = Color.FromArgb(255, 240, 214);
        private static readonly Color BordeFicha = Color.FromArgb(215, 222, 215);
        private static readonly Color TextoError = Color.FromArgb(183, 28, 28);
        private static readonly Color FondoNoCuenta = PestanaCalendario.GrisNoCuenta;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Lo ultimo que se eligio, mientras DigBit siga abierto (tambien si se cierra
        // la sesion), como la casilla de Faltas: el periodo (tambien un ciclo pasado, por
        // su primer dia, y "Hasta hoy") y los filtros de cada pestana.
        private static PeriodoElegido ultimoPeriodo = PeriodoElegido.De(TipoPeriodo.Ciclo);
        private static readonly Dictionary<PestanaEstadisticas, ClaveDeFiltro> filtrosRecordados = new Dictionary<PestanaEstadisticas, ClaveDeFiltro>();

        private readonly DrawingFont fuenteNegrita = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
        private readonly DrawingFont fuenteNombre = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);
        private readonly DrawingFont fuenteTituloFicha = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
        private readonly DrawingFont fuenteRotulo = new DrawingFont("Century Gothic", 8F, FontStyle.Bold);
        private readonly DrawingFont fuenteValor = new DrawingFont("Century Gothic", 10F);
        private readonly DrawingFont fuenteLinea = new DrawingFont("Century Gothic", 9F);
        private readonly DrawingFont fuenteAviso = new DrawingFont("Century Gothic", 9.5F);
        private readonly DrawingFont fuentePestana = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);

        private readonly bool construida;
        private readonly Label lblSubtitulo;
        private readonly Label lblPeriodo;
        private readonly ComboBox cboPeriodo;
        private readonly CifraGrande[] cifras = new CifraGrande[5];
        private readonly TabControl tabs;
        // Cada pestana con sus listas (Pagina.Filtro), lo que pide y lo que se ve.
        private readonly Dictionary<PestanaEstadisticas, Pagina> paginas = new Dictionary<PestanaEstadisticas, Pagina>();
        private readonly Button btnActualizar;
        private readonly Button btnCsv;
        private readonly Timer esperaCalculo = new Timer { Interval = 300 };

        // Estudiantes y su detalle "Sus clases".
        private readonly Tabla<FilaEstudiante> tEstudiantes;
        private readonly Tabla<ParDeAsistencia> tSusClases;
        private readonly Ficha fichaEstudiante;
        private readonly Label lblNombreEstudiante;
        private readonly Rotulo rMatricula;
        private readonly Rotulo rGrupo;
        private readonly Rotulo rBitacoras;
        private readonly Rotulo rUltima;
        private readonly Label lblDatosEstudiante;
        private readonly Label lblNotasEstudiante;
        private readonly Button btnVerBitacoras;

        // Clases y su detalle "Estudiantes de la clase".
        private readonly Tabla<FilaClase> tClases;
        private readonly Tabla<ParDeAsistencia> tLista;
        private readonly Ficha fichaClase;
        private readonly Label lblTituloClase;
        private readonly Rotulo rMateria;
        private readonly Rotulo rGrupoClase;
        private readonly Rotulo rDocente;
        private readonly Rotulo rLaboratorio;
        private readonly Label lblDatosClase;
        private readonly Label lblLineaLista;
        private Dictionary<ParDeAsistencia, int> numeroEnLista = new Dictionary<ParDeAsistencia, int>();
        // Las clases del calendario de la lectura que se ve, por clase-dia (doble clic en Docentes).
        private Dictionary<string, ClaseDelCalendario> clasesDelDia;
        private LecturaEstadisticas clasesDelDiaDe;

        // Grupos, Docentes (con sus clases dia por dia), Laboratorios y Equipos.
        private readonly Tabla<FilaGrupo> tGrupos;
        private readonly Tabla<FilaDocente> tDocentes;
        private readonly Tabla<ClaseDiaDocente> tSusClasesDia;
        private readonly Ficha fichaDocente;
        private readonly Label lblNombreDocente;
        private readonly Label lblResumenDocente;
        private readonly Tabla<FilaLaboratorio> tLaboratorios;
        private readonly Tabla<FilaEquipo> tEquipos;

        // Graficas (2026-10-31): las cuatro secciones, la agrupacion y el reporte para auditoria.
        private static AgrupacionGraficas? agrupacionElegida;
        private readonly PanelDeGraficas panelGraficas;
        private readonly Label lblAgrupar;
        private readonly RadioButton rbPorSemana;
        private readonly RadioButton rbPorMes;
        private readonly Button btnReporte;
        private bool poniendoAgrupacion;

        // Lo pedido y lo leido.
        private PeriodoElegido periodo;
        // El periodo con que se pidio la lectura que se ve (el de lo general): con el calcula
        // cada pestana y a el se vuelve si otra lectura no se pudo hacer (2026-10-31: tras fallar
        // "Este ciclo", las pestanas calculaban el ciclo con la lectura del mes que se veia).
        private PeriodoElegido periodoLeido;
        private LecturaEstadisticas lectura;
        // "Ver en Estadisticas" de la ficha del docente (MostrarDocente): la lectura de la ficha y su
        // periodo, para que la siguiente lectura (sin F5 y con ese mismo periodo) calcule con ella.
        private LecturaEstadisticas lecturaDeLaFicha;
        private PeriodoElegido periodoDeLaFicha;
        // Lo general del periodo (todos los laboratorios, docentes y grupos): las cifras de
        // arriba (2026-10-31, el usuario: "arriba las generales y abajo las que van por
        // apartado") y las opciones de las listas de todas las pestanas.
        private ResultadoEstadisticas resultadoGeneral;
        // Lo calculado con esta lectura y este periodo, por lo que pidio cada pestana
        // (laboratorio, docente y grupo): se vacia con cada lectura que llega. Cada calculo
        // trae todas las tablas: se guardan a lo mas MaximoGuardados (Guardar).
        private const int MaximoGuardados = 12;
        private readonly Dictionary<ClaveDeFiltro, ResultadoEstadisticas> calculados = new Dictionary<ClaveDeFiltro, ResultadoEstadisticas>();
        private readonly List<ClaveDeFiltro> ordenDeGuardados = new List<ClaveDeFiltro>();
        private string error;
        // No se pudo volver a leer y se sigue viendo lo de antes: lo que dice lblResumen y su ayuda.
        private string falloDeLectura;
        private string ayudaDelFallo;
        private int lecturas;
        private bool leyendoBase;
        // Hay una lectura de la base en marcha; lo pedido mientras tanto espera a que acabe.
        private bool baseEnMarcha;
        private bool otraPedida;
        private bool otraForzada;
        private bool poniendoCombos;
        private bool disposicionAmplia;
        private bool disposicionPuesta;
        private bool disposicionBaja;
        private int periodoAntes = -1;
        private bool leyendoAlumno;
        private string motivoSinCsv;
        private bool ayudaCsvEnsenada;

        public VentanaEstadisticas()
            : base(Titulo, Subtitulo, new Size(1280, 720))
        {
            // El subtitulo es de PantallaAdmin: con poco alto (1366x768 al 125 %) se quita.
            lblSubtitulo = Controls.OfType<Label>().FirstOrDefault(l => l.Text == Subtitulo);

            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            // Cabe tambien en 1366x768 con Windows al 125 % (unos 1093x576 utiles):
            // por debajo de 640 de alto va la disposicion baja (sin subtitulo).
            MinimumSize = new Size(Math.Min(1000, area.Width - 20), Math.Min(560, area.Height - 20));
            // Es de toda la escuela: a pantalla completa, como Horarios y Mis grupos.
            WindowState = FormWindowState.Maximized;
            // F5 desde cualquier sitio (OnKeyDown); Enter lo usa cada tabla o cada cifra.
            KeyPreview = true;

            periodo = PeriodoRecordado();

            // ----- Periodo (arriba, junto al titulo: es de todo) -----
            // Con Tab, en el orden en que se ven: el periodo, las cifras generales, las
            // pestanas y, dentro de cada una, sus filtros, Buscar y la tabla.
            lblPeriodo = Etiqueta("Periodo");
            cboPeriodo = Combo(this, 0, "Periodo");
            cboPeriodo.DrawItem += DibujarOpcion;
            cboPeriodo.SelectedIndexChanged += (s, e) => { if (!poniendoCombos) PeriodoCambiado(); };

            // ----- Cifras -----
            string[] etiquetas = { "asistencia de los estudiantes", "clases que se dieron", "bitácoras registradas", "fallas reportadas", "equipos avisados sin internet" };
            for (int i = 0; i < cifras.Length; i++)
            {
                int cual = i;
                cifras[i] = new CifraGrande { Etiqueta = etiquetas[i], TabIndex = 1 + i };
                cifras[i].Click += (s, e) => CifraPulsada(cual);
                Controls.Add(cifras[i]);
            }

            // ----- Pestanas -----
            tabs = new TabControl
            {
                Font = fuentePestana,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(132, 26),
                TabIndex = 6
            };
            tabs.DrawItem += DibujarPestana;
            tabs.SelectedIndexChanged += (s, e) => PestanaCambiada();
            Controls.Add(tabs);

            Pagina estudiantes = NuevaPagina(PestanaEstadisticas.Estudiantes, "Estudiantes", true);
            Pagina clases = NuevaPagina(PestanaEstadisticas.Clases, "Clases", true);
            Pagina grupos = NuevaPagina(PestanaEstadisticas.Grupos, "Grupos", true);
            Pagina docentes = NuevaPagina(PestanaEstadisticas.Docentes, "Docentes", true);
            Pagina laboratorios = NuevaPagina(PestanaEstadisticas.Laboratorios, "Laboratorios", true);
            Pagina equipos = NuevaPagina(PestanaEstadisticas.Equipos, "Equipos", true);
            Pagina graficas = NuevaPagina(PestanaEstadisticas.Graficas, "Gráficas", false);

            // Los filtros de cada pestana, arriba a la izquierda y en este orden (2026-10-31: "cada
            // apartado, los suyos"); cada una recuerda los suyos mientras DigBit siga abierto.
            // Laboratorios no tiene: cada fila ya es un laboratorio.
            AgregarListas(estudiantes, false, TipoDeFiltro.Grupo, TipoDeFiltro.Docente, TipoDeFiltro.Laboratorio);
            AgregarListas(clases, false, TipoDeFiltro.Docente, TipoDeFiltro.Grupo, TipoDeFiltro.Laboratorio);
            AgregarListas(grupos, false, TipoDeFiltro.Docente, TipoDeFiltro.Laboratorio);
            AgregarListas(docentes, false, TipoDeFiltro.Laboratorio, TipoDeFiltro.Grupo);
            // Equipos y Graficas: el laboratorio entre flechas, como en Horarios. En Equipos sin
            // "Todos los laboratorios" (los equipos son de uno: "PC-07" hay en todos).
            AgregarListas(equipos, true, TipoDeFiltro.Laboratorio);
            AgregarListas(graficas, true, TipoDeFiltro.Laboratorio, TipoDeFiltro.Docente, TipoDeFiltro.Grupo);

            // Estudiantes.
            tEstudiantes = CrearTablaEstudiantes();
            estudiantes.Agregar(tEstudiantes.Grid, 10);
            estudiantes.Tabla = tEstudiantes;
            fichaEstudiante = new Ficha(BordeFicha) { Vacio = "Elige un estudiante para ver sus clases.", Font = fuenteValor };
            lblNombreEstudiante = TextoDeFicha(fichaEstudiante, fuenteNombre, Color.DarkGreen);
            rMatricula = new Rotulo(fichaEstudiante.Contenido, "MATRÍCULA", fuenteRotulo, fuenteValor);
            rGrupo = new Rotulo(fichaEstudiante.Contenido, "GRUPO", fuenteRotulo, fuenteValor);
            rBitacoras = new Rotulo(fichaEstudiante.Contenido, "BITÁCORAS EN EL PERIODO", fuenteRotulo, fuenteValor);
            rUltima = new Rotulo(fichaEstudiante.Contenido, "ÚLTIMA BITÁCORA", fuenteRotulo, fuenteValor);
            lblDatosEstudiante = TextoDeFicha(fichaEstudiante, fuenteLinea, TextoOscuro);
            lblDatosEstudiante.AutoEllipsis = false;
            tSusClases = CrearTablaSusClases();
            fichaEstudiante.Contenido.Controls.Add(tSusClases.Grid);
            lblNotasEstudiante = TextoDeFicha(fichaEstudiante, fuenteLinea, TextoOscuro);
            lblNotasEstudiante.AutoEllipsis = false;
            btnVerBitacoras = EstiloHorarios.CrearBoton("Ver sus bitácoras", Point.Empty, new Size(170, 36));
            btnVerBitacoras.Click += (s, e) => VerSusBitacoras(tEstudiantes.Elegido);
            ayuda.SetToolTip(btnVerBitacoras, "Cada bitácora que registró, con su clase, el equipo y lo que reportó. También con doble clic o Enter en la tabla.");
            fichaEstudiante.Contenido.Controls.Add(btnVerBitacoras);
            fichaEstudiante.Contenido.Resize += (s, e) => AcomodarFichaEstudiante();
            estudiantes.Agregar(fichaEstudiante, 11);
            estudiantes.Tablas.AddRange(new ITablaDisposicion[] { tEstudiantes, tSusClases });
            tEstudiantes.Elegida += () => MostrarEstudiante(tEstudiantes.Elegido);
            tEstudiantes.Abrir += VerSusBitacoras;

            // Clases.
            tClases = CrearTablaClases();
            clases.Agregar(tClases.Grid, 10);
            clases.Tabla = tClases;
            fichaClase = new Ficha(BordeFicha) { Vacio = "Elige una clase para ver a sus estudiantes.", Font = fuenteValor };
            lblTituloClase = TextoDeFicha(fichaClase, fuenteTituloFicha, Color.DarkGreen);
            lblTituloClase.Text = "Estudiantes de la clase";
            rMateria = new Rotulo(fichaClase.Contenido, "MATERIA", fuenteRotulo, fuenteValor);
            rGrupoClase = new Rotulo(fichaClase.Contenido, "GRUPO", fuenteRotulo, fuenteValor);
            rDocente = new Rotulo(fichaClase.Contenido, "DOCENTE", fuenteRotulo, fuenteValor);
            rLaboratorio = new Rotulo(fichaClase.Contenido, "LABORATORIO", fuenteRotulo, fuenteValor);
            lblDatosClase = TextoDeFicha(fichaClase, fuenteLinea, TextoOscuro);
            lblDatosClase.AutoEllipsis = false;
            lblLineaLista = TextoDeFicha(fichaClase, fuenteLinea, TextoGris);
            lblLineaLista.AutoEllipsis = false;
            tLista = CrearTablaLista();
            fichaClase.Contenido.Controls.Add(tLista.Grid);
            fichaClase.Contenido.Resize += (s, e) => AcomodarFichaClase();
            clases.Agregar(fichaClase, 11);
            clases.Tablas.AddRange(new ITablaDisposicion[] { tClases, tLista });
            tClases.Elegida += () => MostrarClase(tClases.Elegido);

            // Grupos.
            tGrupos = CrearTablaGrupos();
            grupos.Agregar(tGrupos.Grid, 10);
            grupos.Tabla = tGrupos;
            grupos.Tablas.Add(tGrupos);
            tGrupos.Abrir += g => IrAClasesCon(grupos, 0, g.GrupoId);

            // Docentes: arriba (o a la izquierda) los docentes; debajo, sus clases dia por dia.
            tDocentes = CrearTablaDocentes();
            docentes.Agregar(tDocentes.Grid, 10);
            docentes.Tabla = tDocentes;
            fichaDocente = new Ficha(BordeFicha) { Vacio = "Elige a un docente para ver sus clases día por día.", Font = fuenteValor };
            lblNombreDocente = TextoDeFicha(fichaDocente, fuenteNombre, Color.DarkGreen);
            lblResumenDocente = TextoDeFicha(fichaDocente, fuenteLinea, TextoOscuro);
            tSusClasesDia = CrearTablaSusClasesDia();
            fichaDocente.Contenido.Controls.Add(tSusClasesDia.Grid);
            fichaDocente.Contenido.Resize += (s, e) => AcomodarFichaDocente();
            docentes.Agregar(fichaDocente, 11);
            docentes.Tablas.AddRange(new ITablaDisposicion[] { tDocentes, tSusClasesDia });
            tDocentes.Elegida += () => MostrarDocente(tDocentes.Elegido);
            tDocentes.Abrir += d => IrAClasesCon(docentes, d.DocenteId, 0);
            tSusClasesDia.Abrir += AbrirBitacora;

            // Laboratorios y Equipos.
            tLaboratorios = CrearTablaLaboratorios();
            laboratorios.Agregar(tLaboratorios.Grid, 10);
            laboratorios.Tabla = tLaboratorios;
            laboratorios.Tablas.Add(tLaboratorios);
            tLaboratorios.Abrir += l => IrAEquiposDe(l.EsTodos ? 0 : l.LaboratorioId);
            tEquipos = CrearTablaEquipos();
            equipos.Agregar(tEquipos.Grid, 10);
            equipos.Tabla = tEquipos;
            equipos.Tablas.Add(tEquipos);

            // Graficas: las cuatro secciones (ContenidoGraficas) con desplazamiento y, arriba a la
            // derecha, por semana o por mes y el reporte para auditoria (en kiosco no: no hay archivos).
            panelGraficas = new PanelDeGraficas();
            graficas.Agregar(panelGraficas, 10);
            lblAgrupar = EstiloHorarios.CrearEtiqueta("Agrupar", Point.Empty);
            rbPorSemana = OpcionDeAgrupacion("Por semana", AgrupacionGraficas.Semana);
            rbPorMes = OpcionDeAgrupacion("Por mes", AgrupacionGraficas.Mes);
            graficas.Agregar(lblAgrupar, 4);
            graficas.Agregar(rbPorSemana, 5);
            graficas.Agregar(rbPorMes, 6);
            if (!Kiosco.Activo)
            {
                btnReporte = EstiloHorarios.CrearBoton("Reporte para auditoría (PDF)...", Point.Empty, new Size(262, 32));
                btnReporte.Click += (s, e) => ReporteParaAuditoria();
                ayuda.SetToolTip(btnReporte, "El reporte de uso de laboratorios con el encabezado del TESCHI, el periodo, los filtros de esta pestaña, estas gráficas y sus tablas, "
                    + "listo para imprimir o entregar. Se ve antes de guardarlo."
                    + (EstadisticasDatos.ReporteConDocentes(Permisos.SoloConsulta) ? "" : " Sale sin lo de cada docente: sus nombres solo se ven en pantalla."));
                graficas.Agregar(btnReporte, 7);
            }

            foreach (Pagina pagina in paginas.Values)
            {
                Pagina esta = pagina;
                esta.Tab.Resize += (s, e) => AcomodarPagina(esta);
            }

            // ----- Botones de abajo (de derecha a izquierda) -----
            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            // En kiosco no hay CSV: alli no hay dialogos de archivos (seria una salida al escritorio).
            if (!Kiosco.Activo)
            {
                btnCsv = BotonAbajo(TituloCsv + "...", 220);
                btnCsv.Click += (s, e) => GuardarCsv();
                ayuda.SetToolTip(btnCsv, AyudaCsv);
                // Apagado no recibe el raton: su motivo lo ensena la ventana (como Permisos.Desactivar).
                MouseMove += (s, e) => AyudaDelCsvApagado(e.Location);
                MouseLeave += (s, e) => EsconderAyudaDelCsv();
                // La persona responsable lo ve encenderse y apagarse entre pestanas: el
                // motivo, a la vista encima del boton mientras esta apagado (no solo al
                // pasar el raton).
                if (Permisos.SoloConsulta)
                {
                    Permisos.MotivoVisible(btnCsv, MotivoSinCsvALaVista);
                }
            }

            btnActualizar = BotonAbajo("Actualizar", 120);
            btnActualizar.Click += (s, e) => Leer(true);
            ayuda.SetToolTip(btnActualizar, "Vuelve a leer las estadísticas de la base de datos (F5).");
            // Con Tab, de izquierda a derecha como se ven (se crearon de derecha a izquierda).
            btnActualizar.TabIndex = 7;
            if (btnCsv != null)
            {
                btnCsv.TabIndex = 8;
            }

            cerrar.TabIndex = 9;

            esperaCalculo.Tick += (s, e) =>
            {
                esperaCalculo.Stop();
                if (!leyendoBase && FiltroDe(PestanaElegida).Calculando)
                {
                    lblResumen.Text = TextoCalculando;
                }
            };

            PonerListasIniciales();
            foreach (CifraGrande cifra in cifras)
            {
                cifra.Leyendo = true;
            }

            construida = true;
            Acomodar();
            PestanaCambiada();
            // Al abrir, el foco en las pestanas y no en "Periodo": donde hay un laboratorio
            // entre flechas (Equipos y Graficas), ← y → lo mueven; en una lista elegida con
            // el raton, eligen en esa lista.
            ActiveControl = tabs;
        }

        // =====================================================================
        // Lo que ve E5 y las pruebas
        // =====================================================================

        /// <summary>La pestana que se ve.</summary>
        internal PestanaEstadisticas PestanaElegida
        {
            get { return tabs.SelectedIndex < 0 ? PestanaEstadisticas.Estudiantes : (PestanaEstadisticas)tabs.SelectedIndex; }
        }

        /// <summary>Lo calculado para la pestana que se ve, con sus filtros (null mientras no hay nada).</summary>
        internal ResultadoEstadisticas Resultado
        {
            get { return ResultadoDe(PestanaElegida); }
        }

        /// <summary>Las cifras de arriba: todo el TESCHI en el periodo (null mientras no hay nada).</summary>
        internal ResultadoEstadisticas ResultadoGeneral
        {
            get { return resultadoGeneral; }
        }

        /// <summary>Lo calculado para esa pestana con sus filtros, o null si aun no se calcula (o no se pudo).</summary>
        internal ResultadoEstadisticas ResultadoDe(PestanaEstadisticas pestana)
        {
            Pagina pagina;
            return paginas.TryGetValue(pestana, out pagina) ? pagina.Filtro.Resultado : null;
        }

        /// <summary>Elige una pestana (la usan las cifras y el doble clic de las tablas).</summary>
        internal void ElegirPestana(PestanaEstadisticas pestana)
        {
            tabs.SelectedIndex = (int)pestana;
        }

        /// <summary>
        /// "Ver en Estadisticas" de la ficha del docente: ese periodo y, en cada pestana que lo
        /// tiene, ese docente (los demas filtros en "Todos"); Docentes, sin laboratorio ni
        /// grupo. Equipos y Laboratorios no cambian. Pasa a Clases (sus clases con la lista de
        /// cada una). A proposito cambia lo que Estadisticas recuerda. Antes de la primera
        /// lectura (recien abierta) solo lo deja pedido: la lectura de OnShown ya lo trae.
        /// </summary>
        internal void MostrarDocente(int docente, PeriodoElegido p)
        {
            MostrarDocente(docente, p, null);
        }

        /// <summary>
        /// Lo mismo con la lectura de la ficha (deLaFicha): si no es la que ya se ve, Estadisticas
        /// calcula con ella en vez de con la suya (que puede ser de hace mas de 10 minutos, sin las
        /// clases que se pusieron despues) o de leer otra. Asi sus cifras y sus CSV salen de la
        /// misma foto que la ficha y su PDF, y su "Leido a las" es el de la ficha.
        /// </summary>
        internal void MostrarDocente(int docente, PeriodoElegido p, LecturaEstadisticas deLaFicha)
        {
            foreach (Pagina pagina in paginas.Values)
            {
                if (pagina.Cual == PestanaEstadisticas.Equipos || pagina.Cual == PestanaEstadisticas.Laboratorios)
                {
                    continue;
                }

                PonerFiltro(pagina, 0, docente, 0);
            }

            bool otroPeriodo = p != null && !SelectorDePeriodo.Iguales(p, periodo);
            if (otroPeriodo)
            {
                periodo = p;
                ultimoPeriodo = p;
                bool antes = poniendoCombos;
                poniendoCombos = true;
                try
                {
                    LlenarPeriodo(lectura, null);
                }
                finally
                {
                    poniendoCombos = antes;
                }
            }

            // La de la ficha la usa la siguiente lectura (LanzarLectura), tambien la de OnShown
            // o la que se lance al acabar la que va.
            bool otraLectura = deLaFicha != null && p != null && deLaFicha != lectura;
            if (otraLectura)
            {
                lecturaDeLaFicha = deLaFicha;
                periodoDeLaFicha = p;
            }

            bool sinLeer = lecturas == 0;
            if (tabs.SelectedIndex != (int)PestanaEstadisticas.Clases)
            {
                ElegirPestana(PestanaEstadisticas.Clases);
            }

            if (sinLeer)
            {
                return;
            }

            if (otroPeriodo || otraLectura)
            {
                Leer(false);
                return;
            }

            MostrarApartado(paginas[PestanaEstadisticas.Clases]);
            PonerResumen();
        }

        private FiltroDeApartado FiltroDe(PestanaEstadisticas pestana)
        {
            return paginas[pestana].Filtro;
        }

        /// <summary>Lo que solo existe con una migracion (ConDadas, ConReportesDocentes, ConAvisos): de la lectura, igual en todas las pestanas.</summary>
        private bool Con(PestanaEstadisticas pestana, Func<ResultadoEstadisticas, bool> que)
        {
            ResultadoEstadisticas r = ResultadoDe(pestana) ?? resultadoGeneral;
            return r != null && que(r);
        }

        // =====================================================================
        // Construccion de las tablas (columnas de sec. 3 y 6.3, con las decisiones del usuario)
        // =====================================================================

        private Tabla<FilaEstudiante> CrearTablaEstudiantes()
        {
            Tabla<FilaEstudiante> t = new Tabla<FilaEstudiante>("Estudiantes", fuenteNegrita)
            {
                TextoDeBusqueda = f => f.Matricula + " " + f.Nombre + " " + f.Grupo,
                ClaveDeFila = f => f.Id.ToString(Inv),
                AlFinal = f => f.SoloFuera,
                FilaGris = f => !f.Activo,
                AyudaDeFila = AyudaDeEstudiante,
                SinCoincidencias = "Nadie coincide con la búsqueda.",
                ElegirLaUnica = true
            };
            t.TextoSinFilas = () => TextoSinFilas(PestanaEstadisticas.Estudiantes, "Nadie registró su bitácora en las clases de este periodo.");
            t.SinFilasEsError = () => SinFilasPorError(PestanaEstadisticas.Estudiantes);
            t.Agregar(new Columna<FilaEstudiante> { Nombre = "matricula", Titulo = "Matrícula", Ancho = 100, Texto = f => f.Matricula, Clave = f => f.Matricula });
            t.Agregar(new Columna<FilaEstudiante>
            {
                Nombre = "estudiante", Titulo = "Estudiante", Rellena = true, Ancho = 200,
                Texto = f => f.Nombre + (f.Activo ? "" : " (de baja)"), Clave = f => f.Nombre
            });
            t.Agregar(new Columna<FilaEstudiante> { Nombre = "grupo", Titulo = "Grupo", Ancho = 90, SoloAmplia = true, Texto = f => Guion(f.Grupo), Clave = f => Vacio(f.Grupo) });
            t.Agregar(new Columna<FilaEstudiante>
            {
                Nombre = "clases", Titulo = "Clases", Ancho = 70, Derecha = true, SoloAmplia = true, PrimeroDescendente = true,
                Texto = f => f.Clases == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(f.Clases), Clave = f => f.Clases == 0 ? null : (IComparable)f.Clases,
                AyudaDelTitulo = "Las clases del periodo en cuya lista está y que tienen días que cuentan."
            });
            t.Agregar(new Columna<FilaEstudiante>
            {
                Nombre = "entro", Titulo = "Entró", Ancho = 110, Texto = f => EstadisticasDatos.XdeN(f.Entro, f.Veces),
                Fraccion = f => BarraEnCelda.Fraccion(f.Entro, f.Veces), Estilo = EstiloBarra.Asistencia, Clave = f => Fraccion(f.Entro, f.Veces),
                AyudaDelTitulo = "Las veces que entró de las que contaban en sus clases, sumadas: un día de clase cuenta una vez, como en Mis grupos."
            });
            t.Agregar(new Columna<FilaEstudiante>
            {
                Nombre = "asistencia", Titulo = "Asistencia", Ancho = 95, Derecha = true,
                Texto = f => EstadisticasDatos.Porcentaje(f.Porcentaje), Clave = f => f.Porcentaje,
                ColorTexto = f => f.Porcentaje.HasValue && !ResumenAsistencia.TieneDerecho(f.Porcentaje.Value) ? RojoTexto : (Color?)null
            });
            t.Agregar(new Columna<FilaEstudiante>
            {
                Nombre = "faltas", Titulo = "Faltas", Ancho = 70, Derecha = true, PrimeroDescendente = true,
                Texto = f => f.Veces == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(f.Faltas), Clave = f => f.Veces == 0 ? null : (IComparable)f.Faltas,
                AyudaDelTitulo = "Incluye las asistencias que quitó su docente: registró sin estar en el salón."
            });
            t.Agregar(new Columna<FilaEstudiante>
            {
                Nombre = "bajo", Titulo = "Bajo el 80 %", Ancho = 90, Derecha = true, SoloAmplia = true, PrimeroDescendente = true,
                Texto = f => f.Clases == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(f.ClasesBajo) + " de " + EstadisticasDatos.Miles(f.Clases),
                Clave = f => f.Clases == 0 ? null : (IComparable)f.ClasesBajo,
                ColorTexto = f => f.ClasesBajo > 0 ? RojoTexto : (Color?)null,
                AyudaDelTitulo = "Sus clases que no llegan al 80 % de asistencia en el periodo, de las que tiene."
            });
            t.Agregar(new Columna<FilaEstudiante>
            {
                Nombre = "bitacoras", Titulo = "Bitácoras", Ancho = 85, Derecha = true, SoloAmplia = true, PrimeroDescendente = true,
                Texto = f => EstadisticasDatos.Miles(f.Bitacoras), Clave = f => f.Bitacoras,
                AyudaDelTitulo = "Todas sus bitácoras del periodo, también en uso libre y fuera del horario."
            });
            t.Agregar(new Columna<FilaEstudiante>
            {
                Nombre = "fallas", Titulo = "Fallas reportadas", Ancho = 85, Derecha = true, PrimeroDescendente = true,
                Texto = f => EstadisticasDatos.Miles(f.Fallas), Clave = f => f.Fallas, ColorTexto = f => f.Fallas > 0 ? NaranjaTexto : (Color?)null
            });
            t.Agregar(new Columna<FilaEstudiante>
            {
                Nombre = "ultima", Titulo = "Última bitácora", Ancho = 105, SoloAmplia = true, PrimeroDescendente = true,
                Texto = f => f.UltimaBitacora.HasValue ? EstadisticasDatos.Fecha(f.UltimaBitacora.Value) : EstadisticasDatos.SinDato,
                Clave = f => f.UltimaBitacora
            });
            return t;
        }

        private string AyudaDeEstudiante(FilaEstudiante f, bool amplia)
        {
            if (amplia)
            {
                return f.SoloFuera ? "No está en la lista de ninguna clase de este periodo: sus bitácoras son de uso libre o de sesiones fuera de sus clases." : null;
            }

            List<string> partes = new List<string>
            {
                "Grupo: " + (f.Grupo.Length > 0 ? f.Grupo : "sin grupo en su cuenta") + ".",
                f.Clases == 0 ? "No está en la lista de ninguna clase de este periodo." : EstadisticasDatos.Contar(f.Clases, "clase", "clases") + " (" + EstadisticasDatos.Miles(f.ClasesBajo) + " bajo el 80 %).",
                EstadisticasDatos.Contar(f.Bitacoras, "bitácora", "bitácoras") + " en el periodo" + (f.UltimaBitacora.HasValue ? "; la última, el " + EstadisticasDatos.Fecha(f.UltimaBitacora.Value) : "") + "."
            };
            return string.Join("\n", partes);
        }

        private Tabla<ParDeAsistencia> CrearTablaSusClases()
        {
            Tabla<ParDeAsistencia> t = new Tabla<ParDeAsistencia>("Sus clases", fuenteNegrita)
            {
                ClaveDeFila = p => p.ClaseId.ToString(Inv),
                AyudaDeFila = (p, amplia) => amplia ? null : "Grupo " + p.Grupo + "; docente: " + p.Docente + ".",
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => "No está en la lista de ninguna clase de este periodo: sus bitácoras son de uso libre o de sesiones fuera de sus clases.";
            t.Agregar(new Columna<ParDeAsistencia> { Nombre = "materia", Titulo = "Materia", Rellena = true, Ancho = 90, Texto = p => p.Materia, Clave = p => p.Materia });
            t.Agregar(new Columna<ParDeAsistencia> { Nombre = "grupo", Titulo = "Grupo", Ancho = 80, SoloAmplia = true, Texto = p => p.Grupo, Clave = p => p.Grupo });
            t.Agregar(new Columna<ParDeAsistencia> { Nombre = "docente", Titulo = "Docente", Ancho = 150, SoloAmplia = true, Texto = p => p.Docente, Clave = p => p.Docente });
            AgregarColumnasDeAsistencia(t, PestanaEstadisticas.Estudiantes);
            return t;
        }

        /// <summary>
        /// Entro (con su barra), % y la columna del 80 %, comunes a "Sus clases" y "Estudiantes de
        /// la clase": su titulo y su texto, con lo calculado para esa pestana (su laboratorio).
        /// </summary>
        private void AgregarColumnasDeAsistencia(Tabla<ParDeAsistencia> t, PestanaEstadisticas pestana)
        {
            t.Agregar(new Columna<ParDeAsistencia>
            {
                Nombre = "entro", Titulo = "Entró", Ancho = 85, Texto = p => EstadisticasDatos.XdeN(p.Entro, p.Veces),
                Fraccion = p => BarraEnCelda.Fraccion(p.Entro, p.Veces), Estilo = EstiloBarra.Asistencia, Clave = p => Fraccion(p.Entro, p.Veces)
            });
            t.Agregar(new Columna<ParDeAsistencia>
            {
                Nombre = "porcentaje", Titulo = "%", Ancho = 50, Derecha = true, Texto = p => EstadisticasDatos.Porcentaje(p.Porcentaje), Clave = p => p.Porcentaje,
                ColorTexto = p => p.Bajo ? RojoTexto : (Color?)null
            });
            t.Agregar(new Columna<ParDeAsistencia>
            {
                Nombre = "derecho", Titulo = "Derecho (80 %)", Ancho = 92,
                // Siempre el corto: el largo ("Derecho a evaluacion (80 %)") no cabe en 92 px y
                // se cortaba; lo explica la ayuda del titulo y va entero en el CSV.
                TituloSegun = amplia =>
                {
                    ResultadoEstadisticas r = ResultadoDe(pestana);
                    return r == null ? "Derecho (80 %)" : r.TituloDerechoCorto;
                },
                Texto = p => p.TextoDerecho(Con(pestana, r => r.CicloEntero)), Clave = p => p.Veces == 0 ? null : (IComparable)(p.Bajo ? 0 : 1),
                ColorTexto = p => p.Veces == 0 ? (Color?)null : p.Bajo ? RojoTexto : VerdeTexto,
                Negrita = p => p.Bajo,
                Ayuda = p => p.NotaPocasClases,
                AyudaDelTitulo = "Con el ciclo entero y todos los laboratorios, el derecho a evaluación (80 % o más, como en Mis grupos). "
                    + "Con otro periodo o un laboratorio, si llega al 80 % en lo que se ve: el derecho es de todo el ciclo."
            });
        }

        private Tabla<FilaClase> CrearTablaClases()
        {
            Tabla<FilaClase> t = new Tabla<FilaClase>("Clases", fuenteNegrita)
            {
                TextoDeBusqueda = c => c.Materia + " " + c.Grupo + " " + c.Docente + " " + c.Laboratorios,
                ClaveDeFila = c => c.ClaseId.ToString(Inv),
                AlFinal = c => c.SinNada,
                AyudaDeFila = AyudaDeClase,
                SinCoincidencias = "Ninguna clase coincide con la búsqueda."
            };
            t.TextoSinFilas = () => TextoSinFilas(PestanaEstadisticas.Clases, "No hay clases en este periodo con estos filtros.");
            t.SinFilasEsError = () => SinFilasPorError(PestanaEstadisticas.Clases);
            t.Agregar(new Columna<FilaClase> { Nombre = "materia", Titulo = "Materia", Rellena = true, Ancho = 125, Peso = 130, Texto = c => c.Materia, Clave = c => c.Materia });
            t.Agregar(new Columna<FilaClase> { Nombre = "grupo", Titulo = "Grupo", Ancho = 75, Texto = c => c.Grupo, Clave = c => c.Grupo });
            t.Agregar(new Columna<FilaClase> { Nombre = "docente", Titulo = "Docente", Rellena = true, Ancho = 120, Peso = 100, SoloAmplia = true, Texto = c => c.Docente, Clave = c => c.Docente });
            t.Agregar(new Columna<FilaClase> { Nombre = "laboratorio", Titulo = "Laboratorio", Rellena = true, Ancho = 110, Peso = 90, SoloAmplia = true, Texto = c => Guion(c.Laboratorios), Clave = c => Vacio(c.Laboratorios) });
            // En compacta "Clases con bitacoras" va en la ayuda de la fila (como en Laboratorios): asi caben la
            // materia y, en el detalle, el nombre de cada estudiante.
            AgregarColumnasDeClasesDadas(t, c => c.SeDieron, c => c.QueContaban, c => c.SinRegistro, c => c.ConBitacoras, false);
            t.Agregar(new Columna<FilaClase>
            {
                Nombre = "estudiantes", Titulo = "Estudiantes", Ancho = 85, Derecha = true, PrimeroDescendente = true,
                Texto = c => EstadisticasDatos.Miles(c.Estudiantes), Clave = c => c.Estudiantes,
                AyudaDelTitulo = "El tamaño de su lista: quienes han registrado su bitácora en esta clase (DigBit no tiene la lista oficial del grupo)."
            });
            t.Agregar(new Columna<FilaClase>
            {
                Nombre = "asistencia", Titulo = "Asistencia del grupo", Ancho = 110,
                Texto = c => EstadisticasDatos.Porcentaje(c.Asistencia), Fraccion = c => c.Asistencia.HasValue ? c.Asistencia.Value / 100.0 : (double?)null,
                Estilo = EstiloBarra.Asistencia, Clave = c => c.Asistencia,
                ColorTexto = c => c.Asistencia.HasValue && !ResumenAsistencia.TieneDerecho(c.Asistencia.Value) ? RojoTexto : (Color?)null,
                AyudaDelTitulo = "Las veces que entraron de las que contaban, sumadas en toda su lista, como «Asistencia del grupo» de Mis grupos."
            });
            t.Agregar(new Columna<FilaClase>
            {
                Nombre = "bajo", Titulo = "Bajo el 80 %", Ancho = 80, Derecha = true, PrimeroDescendente = true,
                Texto = c => c.Veces == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(c.Bajo), Clave = c => c.Veces == 0 ? null : (IComparable)c.Bajo,
                ColorTexto = c => c.Bajo > 0 ? RojoTexto : (Color?)null,
                AyudaDelTitulo = "Estudiantes de su lista que no llegan al 80 % en el periodo."
            });
            t.Agregar(new Columna<FilaClase>
            {
                Nombre = "fallas", Titulo = "Fallas reportadas", Ancho = 85, Derecha = true, SoloAmplia = true, PrimeroDescendente = true,
                Texto = c => EstadisticasDatos.Miles(c.Fallas), Clave = c => c.Fallas, ColorTexto = c => c.Fallas > 0 ? NaranjaTexto : (Color?)null,
                AyudaDelTitulo = "Bitácoras con falla y reportes de docentes de sus sesiones."
            });
            return t;
        }

        private static string AyudaDeClase(FilaClase c, bool amplia)
        {
            if (amplia)
            {
                return c.SinNada ? "Nada que contar en el periodo: ninguna clase suya ya pasó y contaba, y su lista no tiene días que cuenten." : null;
            }

            List<string> partes = new List<string>
            {
                "Docente: " + c.Docente + ".",
                "Laboratorio: " + Guion(c.Laboratorios) + ".",
                c.QueContaban == 0 ? "Ninguna clase suya ya pasó y contaba." : EstadisticasDatos.Contar(c.SinRegistro, "clase", "clases") + " sin registro"
                    + (c.Canceladas > 0 ? "; " + EstadisticasDatos.Contar(c.Canceladas, "cancelada", "canceladas") : "") + ".",
                c.QueContaban == 0 ? null : "Clases con bitácoras: " + EstadisticasDatos.XdeN(c.ConBitacoras, c.QueContaban) + ".",
                "Fallas reportadas: " + EstadisticasDatos.Miles(c.Fallas) + "."
            };
            return string.Join("\n", partes.Where(p => p != null));
        }

        /// <summary>
        /// "Se dieron" X de N (con su barra), "Sin registro" (en rojo) y "Clases con
        /// bitacoras" X de N: las clases del horario contadas como Faltas (pedido del
        /// usuario del 2026-10-22). Clases, Grupos y Laboratorios.
        /// </summary>
        internal static void AgregarColumnasDeClasesDadas<T>(Tabla<T> t, Func<T, int> seDieron, Func<T, int> queContaban, Func<T, int> sinRegistro,
            Func<T, int> conBitacoras, bool conBitacorasSiempre) where T : class
        {
            t.Agregar(new Columna<T>
            {
                Nombre = "sedieron", Titulo = "Se dieron", Ancho = 100,
                Texto = f => EstadisticasDatos.XdeN(seDieron(f), queContaban(f)),
                Fraccion = f => BarraEnCelda.Fraccion(seDieron(f), queContaban(f)), Estilo = EstiloBarra.Clases,
                Clave = f => Fraccion(seDieron(f), queContaban(f)),
                AyudaDelTitulo = "Clases del horario que ya pasaron y contaban, como en la pestaña Faltas de Horarios. " + EstadisticasDatos.AyudaClasesQueSeDieron
            });
            t.Agregar(new Columna<T>
            {
                Nombre = "sinregistro", Titulo = "Sin registro", Ancho = 80, Derecha = true, PrimeroDescendente = true,
                Texto = f => queContaban(f) == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(sinRegistro(f)),
                Clave = f => queContaban(f) == 0 ? null : (IComparable)sinRegistro(f),
                ColorTexto = f => sinRegistro(f) > 0 ? RojoTexto : (Color?)null,
                Negrita = f => sinRegistro(f) > 0,
                AyudaDelTitulo = "Clases que ya pasaron sin bitácoras ni asistencias que diera su docente (en rojo en Faltas): puede que no se diera o que nadie registrara."
            });
            t.Agregar(new Columna<T>
            {
                Nombre = "conbitacoras", Titulo = "Clases con bitácoras", Ancho = 112, SoloAmplia = !conBitacorasSiempre,
                Texto = f => EstadisticasDatos.XdeN(conBitacoras(f), queContaban(f)),
                Clave = f => Fraccion(conBitacoras(f), queContaban(f)),
                AyudaDelTitulo = "Cada bloque del horario y cada sesión extra cuentan aparte, como en la pestaña Faltas de Horarios. "
                    + "La asistencia cuenta días: dos bloques el mismo día son un día, y también cuentan las sesiones de la clase fuera de su horario."
            });
        }

        private Tabla<ParDeAsistencia> CrearTablaLista()
        {
            Tabla<ParDeAsistencia> t = new Tabla<ParDeAsistencia>("Estudiantes de la clase", fuenteNegrita)
            {
                ClaveDeFila = p => p.AlumnoId.ToString(Inv),
                FilaGris = p => !p.Activo,
                AyudaDeFila = (p, amplia) => Unir(amplia ? null : "Matrícula " + p.Matricula + ".", p.AyudaDeEntrada),
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => "Nadie ha registrado su bitácora en esta clase.";
            t.Agregar(new Columna<ParDeAsistencia>
            {
                Nombre = "numero", Titulo = "No.", Ancho = 42, Derecha = true,
                Texto = p => NumeroEnLista(p).ToString(Inv), Clave = p => NumeroEnLista(p)
            });
            t.Agregar(new Columna<ParDeAsistencia> { Nombre = "matricula", Titulo = "Matrícula", Ancho = 100, SoloAmplia = true, Texto = p => p.Matricula, Clave = p => p.Matricula });
            t.Agregar(new Columna<ParDeAsistencia>
            {
                Nombre = "estudiante", Titulo = "Estudiante", Rellena = true, Ancho = 105,
                Texto = p => p.Nombre + (p.Activo ? "" : " (de baja)"), Clave = p => p.Nombre
            });
            AgregarColumnasDeAsistencia(t, PestanaEstadisticas.Clases);
            return t;
        }

        private int NumeroEnLista(ParDeAsistencia par)
        {
            int numero;
            return numeroEnLista.TryGetValue(par, out numero) ? numero : 0;
        }

        private Tabla<FilaGrupo> CrearTablaGrupos()
        {
            Tabla<FilaGrupo> t = new Tabla<FilaGrupo>("Grupos", fuenteNegrita)
            {
                TextoDeBusqueda = g => g.Grupo + " " + g.Materias,
                ClaveDeFila = g => g.GrupoId.ToString(Inv),
                AlFinal = g => g.SinNada,
                AyudaDeFila = (g, amplia) => g.SinNada ? "Nada que contar en el periodo." : null,
                SinCoincidencias = "Ningún grupo coincide con la búsqueda."
            };
            t.TextoSinFilas = () => TextoSinFilas(PestanaEstadisticas.Grupos, "No hay grupos con clases en este periodo.");
            t.SinFilasEsError = () => SinFilasPorError(PestanaEstadisticas.Grupos);
            t.Agregar(new Columna<FilaGrupo> { Nombre = "grupo", Titulo = "Grupo", Rellena = true, Ancho = 150, Texto = g => g.Grupo, Clave = g => g.Grupo });
            t.Agregar(new Columna<FilaGrupo>
            {
                Nombre = "clases", Titulo = "Clases", Ancho = 70, Derecha = true, PrimeroDescendente = true,
                Texto = g => EstadisticasDatos.Miles(g.Clases), Clave = g => g.Clases, Ayuda = g => g.Materias,
                AyudaDelTitulo = "Sus clases del periodo; al pasar el ratón, sus materias."
            });
            t.Agregar(new Columna<FilaGrupo>
            {
                Nombre = "estudiantes", Titulo = "Estudiantes", Ancho = 95, Derecha = true, PrimeroDescendente = true,
                Texto = g => EstadisticasDatos.Miles(g.Estudiantes), Clave = g => g.Estudiantes,
                AyudaDelTitulo = "Estudiantes distintos con días que cuentan en alguna de sus clases."
            });
            t.Agregar(new Columna<FilaGrupo>
            {
                Nombre = "asistencia", Titulo = "Asistencia", Ancho = 120,
                Texto = g => EstadisticasDatos.Porcentaje(g.Asistencia), Fraccion = g => g.Asistencia.HasValue ? g.Asistencia.Value / 100.0 : (double?)null,
                Estilo = EstiloBarra.Asistencia, Clave = g => g.Asistencia,
                ColorTexto = g => g.Asistencia.HasValue && !ResumenAsistencia.TieneDerecho(g.Asistencia.Value) ? RojoTexto : (Color?)null,
                Ayuda = g => g.Veces == 0 ? null : "Entraron " + EstadisticasDatos.Miles(g.Entro) + " de " + EstadisticasDatos.Miles(g.Veces) + " veces."
            });
            t.Agregar(new Columna<FilaGrupo>
            {
                Nombre = "bajo", Titulo = "Bajo el 80 %", Ancho = 90, Derecha = true, PrimeroDescendente = true,
                Texto = g => g.Veces == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(g.Bajo), Clave = g => g.Veces == 0 ? null : (IComparable)g.Bajo,
                ColorTexto = g => g.Bajo > 0 ? RojoTexto : (Color?)null,
                AyudaDelTitulo = "Estudiantes distintos con alguna clase del grupo por debajo del 80 % en el periodo."
            });
            AgregarColumnasDeClasesDadas(t, g => g.ConBitacoras + g.ConDadas, g => g.QueContaban, g => g.SinBitacoras, g => g.ConBitacoras, true);
            return t;
        }

        private Tabla<FilaDocente> CrearTablaDocentes()
        {
            Tabla<FilaDocente> t = new Tabla<FilaDocente>("Docentes", fuenteNegrita)
            {
                TextoDeBusqueda = d => d.Docente + " " + d.NombresDeMaterias,
                ClaveDeFila = d => d.DocenteId.ToString(Inv),
                AlFinal = d => d.SinNada,
                AyudaDeFila = (d, amplia) => d.SinNada ? "Ninguna de sus clases contaba en el periodo." : null,
                SinCoincidencias = "Nadie coincide con la búsqueda."
            };
            t.TextoSinFilas = () => TextoSinFilas(PestanaEstadisticas.Docentes, "No hay docentes con clases en este periodo.");
            t.SinFilasEsError = () => SinFilasPorError(PestanaEstadisticas.Docentes);
            t.Agregar(new Columna<FilaDocente>
            {
                Nombre = "docente", Titulo = "Docente", Rellena = true, Ancho = 170, Texto = d => d.Docente, Clave = d => d.Docente,
                Cuadrito = d => d.DocenteId
            });
            t.Agregar(new Columna<FilaDocente>
            {
                Nombre = "materias", Titulo = "Materias", Ancho = 80, Derecha = true, PrimeroDescendente = true,
                Texto = d => EstadisticasDatos.Miles(d.Materias), Clave = d => d.Materias, Ayuda = d => d.NombresDeMaterias,
                AyudaDelTitulo = "Sus clases (materia y grupo) del periodo; al pasar el ratón, cuáles."
            });
            t.Agregar(new Columna<FilaDocente>
            {
                Nombre = "sedieron", Titulo = "Se dieron", Ancho = 105,
                Texto = d => EstadisticasDatos.XdeN(d.SeDieron, d.QueContaban),
                Fraccion = d => d.FraccionSeDieron, Estilo = EstiloBarra.Clases, Clave = d => d.FraccionSeDieron,
                Ayuda = d => d.QueContaban == 0 ? null : EstadisticasDatos.Miles(d.ConBitacoras) + " con bitácoras"
                    + (d.ConDadas > 0 ? " y " + EstadisticasDatos.Miles(d.ConDadas) + " sin bitácoras pero con asistencias que dio" : "") + ".",
                AyudaDelTitulo = "Sus clases del horario que ya pasaron y contaban, como en la pestaña Faltas de Horarios. " + EstadisticasDatos.AyudaClasesQueSeDieron
            });
            t.Agregar(new Columna<FilaDocente>
            {
                Nombre = "sinregistro", Titulo = "Sin registro", Ancho = 85, Derecha = true, PrimeroDescendente = true,
                Texto = d => d.QueContaban == 0 ? EstadisticasDatos.SinDato : EstadisticasDatos.Miles(d.SinRegistro),
                Clave = d => d.QueContaban == 0 ? null : (IComparable)d.SinRegistro,
                ColorTexto = d => d.SinRegistro > 0 ? RojoTexto : (Color?)null, Negrita = d => d.SinRegistro > 0,
                AyudaDelTitulo = "Clases que ya pasaron sin bitácoras ni asistencias que diera (en rojo en Faltas): no siempre es falta del docente, puede que nadie registrara."
            });
            t.Agregar(new Columna<FilaDocente>
            {
                Nombre = "canceladas", Titulo = "Canceladas", Ancho = 90, Derecha = true, PrimeroDescendente = true,
                Texto = d => EstadisticasDatos.Miles(d.Canceladas), Clave = d => d.Canceladas,
                AyudaDelTitulo = "Clases canceladas por una excepción (festivo, aviso del docente): no cuentan."
            });
            t.Agregar(new Columna<FilaDocente>
            {
                Nombre = "dadas", Titulo = "Asistencias que dio", Ancho = 95, Derecha = true, PrimeroDescendente = true,
                Existe = () => Con(PestanaEstadisticas.Docentes, r => r.ConDadas),
                Texto = d => EstadisticasDatos.Miles(d.AsistenciasQueDio), Clave = d => d.AsistenciasQueDio,
                AyudaDelTitulo = "Asistencias que dio en sus clases del periodo a quien no pudo registrar su bitácora (por ejemplo, su equipo no tenía internet). Las anuladas no cuentan."
            });
            t.Agregar(new Columna<FilaDocente>
            {
                Nombre = "reportes", Titulo = "Sus reportes", Ancho = 85, Derecha = true, PrimeroDescendente = true,
                Existe = () => Con(PestanaEstadisticas.Docentes, r => r.ConReportesDocentes),
                Texto = d => EstadisticasDatos.Miles(d.SusReportes), Clave = d => d.SusReportes,
                ColorTexto = d => d.SusReportes > 0 ? NaranjaTexto : (Color?)null,
                AyudaDelTitulo = "Fallas que reportó desde su panel en sus sesiones del periodo."
            });
            return t;
        }

        private Tabla<ClaseDiaDocente> CrearTablaSusClasesDia()
        {
            Tabla<ClaseDiaDocente> t = new Tabla<ClaseDiaDocente>("Sus clases día por día", fuenteNegrita)
            {
                ClaveDeFila = d => d.ClaseId.ToString(Inv) + "|" + d.Fecha.ToString("yyyyMMdd", Inv) + "|" + d.Inicio.Ticks.ToString(Inv) + "|" + (d.EsExtra ? "x" : "f") + "|" + d.LaboratorioId.ToString(Inv),
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => "No tiene clases del horario en este periodo.";
            t.Agregar(new Columna<ClaseDiaDocente>
            {
                Nombre = "fecha", Titulo = "Fecha", Ancho = 160, Texto = d => d.FechaTexto, Clave = d => d.Fecha.Ticks + d.Inicio.Ticks, PrimeroDescendente = true
            });
            t.Agregar(new Columna<ClaseDiaDocente> { Nombre = "materia", Titulo = "Materia", Rellena = true, Ancho = 150, Texto = d => d.Materia, Clave = d => d.Materia });
            t.Agregar(new Columna<ClaseDiaDocente> { Nombre = "grupo", Titulo = "Grupo", Ancho = 80, Texto = d => d.Grupo, Clave = d => d.Grupo });
            t.Agregar(new Columna<ClaseDiaDocente> { Nombre = "horario", Titulo = "Horario", Ancho = 110, Texto = d => d.Horario, Clave = d => d.Inicio });
            t.Agregar(new Columna<ClaseDiaDocente> { Nombre = "laboratorio", Titulo = "Laboratorio", Rellena = true, Ancho = 110, Peso = 60, Texto = d => d.Laboratorio, Clave = d => d.Laboratorio });
            t.Agregar(new Columna<ClaseDiaDocente>
            {
                // Crece con su texto mas largo (AjustarAnchos): el de la ambar ("Si (2 asistencias dadas, ...)") solo cuando la hay.
                Nombre = "sedio", Titulo = "¿Se dio?", Ancho = 160, Holgura = 20, Texto = d => d.SeDioTexto, Clave = d => OrdenDeEstado(d),
                Fondo = d => FondoDeEstado(d), ColorTexto = d => TextoDeEstado(d), Negrita = d => d.SinRegistro,
                // La misma regla que el doble clic (AbrirBitacora): solo se ofrece si de verdad abre una.
                Ayuda = d => Unir(d.SeDioTexto, d.Ayuda, SesionDelDia(d) != null ? "Doble clic para ver su bitácora." : null),
                AyudaDelTitulo = EstadisticasDatos.AyudaClasesQueSeDieron
            });
            return t;
        }

        internal static int OrdenDeEstado(ClaseDiaDocente d)
        {
            return d.SinRegistro ? 0 : d.Estado == EstadoEnCalendario.SinBitacorasConAsistencia ? 1 : d.Estado == EstadoEnCalendario.ConBitacoras ? 2 : d.Cancelada ? 3 : 4;
        }

        /// <summary>Los colores de Faltas: verde si se dio con bitacoras, ambar con asistencias dadas, rojo sin registro y gris lo demas.</summary>
        internal static Color? FondoDeEstado(ClaseDiaDocente d)
        {
            switch (d.Estado)
            {
                case EstadoEnCalendario.ConBitacoras:
                    return PestanaCalendario.VerdeBien;
                case EstadoEnCalendario.SinBitacorasConAsistencia:
                    return PestanaCalendario.AmbarDadas;
                case EstadoEnCalendario.SinSesion:
                case EstadoEnCalendario.SesionVacia:
                    return PestanaCalendario.RojoFalta;
                default:
                    return FondoNoCuenta;
            }
        }

        internal static Color? TextoDeEstado(ClaseDiaDocente d)
        {
            switch (d.Estado)
            {
                case EstadoEnCalendario.ConBitacoras:
                    return VerdeOscuro;
                case EstadoEnCalendario.SinBitacorasConAsistencia:
                    return PestanaCalendario.TextoAmbarDadas;
                case EstadoEnCalendario.SinSesion:
                case EstadoEnCalendario.SesionVacia:
                    return Color.White;
                default:
                    return Color.FromArgb(90, 90, 90);
            }
        }

        private Tabla<FilaLaboratorio> CrearTablaLaboratorios()
        {
            Tabla<FilaLaboratorio> t = new Tabla<FilaLaboratorio>("Laboratorios", fuenteNegrita)
            {
                // Buscar deja siempre "Todos" (va arriba).
                TextoDeBusqueda = l => l.Laboratorio,
                ClaveDeFila = l => (l.EsTodos ? "todos" : l.LaboratorioId.ToString(Inv)),
                Arriba = l => l.EsTodos,
                FilaNegrita = l => l.EsTodos,
                FondoDeFila = l => l.EsTodos ? FondoTodos : (Color?)null,
                FilaGris = l => l.AunNo,
                AyudaDeFila = AyudaDeLaboratorio,
                SinCoincidencias = "Ningún laboratorio coincide con la búsqueda.",
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => TextoSinFilas(PestanaEstadisticas.Laboratorios, "");
            t.SinFilasEsError = () => SinFilasPorError(PestanaEstadisticas.Laboratorios);
            t.Agregar(new Columna<FilaLaboratorio> { Nombre = "laboratorio", Titulo = "Laboratorio", Rellena = true, Ancho = 190, Texto = l => l.Laboratorio, Clave = l => l.Laboratorio });
            t.Agregar(new Columna<FilaLaboratorio>
            {
                Nombre = "desde", Titulo = "Registra sus clases desde", Ancho = 105, Texto = l => l.RegistraDesdeTexto, Clave = l => l.RegistraDesde,
                AyudaDelTitulo = "Primera clase del horario con bitácoras en DigBit; el uso libre no cuenta."
            });
            AgregarColumnasDeClasesDadas(t, l => l.ConBitacoras + l.ConDadas, l => l.QueContaban, l => l.SinBitacoras, l => l.ConBitacoras, false);
            t.Agregar(new Columna<FilaLaboratorio>
            {
                Nombre = "bitacoras", Titulo = "Bitácoras", Ancho = 85, Derecha = true, PrimeroDescendente = true,
                Texto = l => EstadisticasDatos.Miles(l.Bitacoras), Clave = l => l.Bitacoras,
                AyudaDelTitulo = "Bitácoras registradas en el periodo, también en uso libre y fuera del horario."
            });
            t.Agregar(new Columna<FilaLaboratorio>
            {
                Nombre = "estudiantes", Titulo = "Estudiantes distintos", Ancho = 95, Derecha = true, PrimeroDescendente = true,
                Texto = l => EstadisticasDatos.Miles(l.Estudiantes), Clave = l => l.Estudiantes,
                AyudaDelTitulo = "El de «Todos» no es la suma: un estudiante puede ir a varios laboratorios."
            });
            AgregarColumnasDeFallas(t, PestanaEstadisticas.Laboratorios, l => l.Fallas, l => l.Hardware, l => l.Software, l => l.Red, l => l.DeDocentes, false);
            t.Agregar(new Columna<FilaLaboratorio>
            {
                Nombre = "sininternet", Titulo = "Equipos sin internet", Ancho = 90, Derecha = true, PrimeroDescendente = true,
                Existe = () => Con(PestanaEstadisticas.Laboratorios, r => r.ConAvisos),
                Texto = l => l.EquiposSinInternet.HasValue ? EstadisticasDatos.Miles(l.EquiposSinInternet.Value) : EstadisticasDatos.SinDato,
                Clave = l => l.EquiposSinInternet, ColorTexto = l => l.EquiposSinInternet > 0 ? NaranjaTexto : (Color?)null,
                Ayuda = l => l.AvisosSinInternet == 0 ? null : "En " + EstadisticasDatos.Contar(l.AvisosSinInternet, "aviso", "avisos") + ".",
                AyudaDelTitulo = "Equipos que otros estudiantes avisaron que no tenían internet."
            });
            return t;
        }

        private static string AyudaDeLaboratorio(FilaLaboratorio l, bool amplia)
        {
            List<string> partes = new List<string>();
            if (l.AunNo)
            {
                partes.Add("Aún no registra sus clases con DigBit: sus clases sin bitácoras no cuentan, como en Faltas.");
            }

            if (!amplia)
            {
                partes.Add("Clases con bitácoras: " + EstadisticasDatos.XdeN(l.ConBitacoras, l.QueContaban) + ".");
            }

            // Los equipos son de un laboratorio a la vez (IrAEquiposDe con "Todos" pasa al primero).
            partes.Add(l.EsTodos ? "Doble clic para ver los equipos, un laboratorio a la vez: empieza por el primero." : "Doble clic para ver sus equipos.");
            return string.Join("\n", partes);
        }

        /// <summary>Fallas reportadas, Hardware, Software, Red y De docentes (db/15), en Laboratorios y Equipos.</summary>
        private void AgregarColumnasDeFallas<T>(Tabla<T> t, PestanaEstadisticas pestana, Func<T, int> fallas, Func<T, int> hardware, Func<T, int> software,
            Func<T, int> red, Func<T, int> deDocentes, bool docentesSoloAmplia) where T : class
        {
            t.Agregar(new Columna<T>
            {
                Nombre = "fallas", Titulo = "Fallas reportadas", Ancho = 85, Derecha = true, PrimeroDescendente = true,
                Texto = f => EstadisticasDatos.Miles(fallas(f)), Clave = f => fallas(f), ColorTexto = f => fallas(f) > 0 ? NaranjaTexto : (Color?)null,
                Negrita = f => fallas(f) > 0,
                AyudaDelTitulo = "Bitácoras con alguna falla" + " y reportes de docentes. Cada área suma las de esa área: una bitácora puede reportar en varias, "
                    + "así que las tres pueden sumar más o menos que el total."
            });
            t.Agregar(new Columna<T> { Nombre = "hardware", Titulo = "Hardware", Ancho = 80, Derecha = true, PrimeroDescendente = true, Texto = f => EstadisticasDatos.Miles(hardware(f)), Clave = f => hardware(f) });
            t.Agregar(new Columna<T> { Nombre = "software", Titulo = "Software", Ancho = 80, Derecha = true, PrimeroDescendente = true, Texto = f => EstadisticasDatos.Miles(software(f)), Clave = f => software(f) });
            t.Agregar(new Columna<T> { Nombre = "red", Titulo = "Red", Ancho = 55, Derecha = true, PrimeroDescendente = true, Texto = f => EstadisticasDatos.Miles(red(f)), Clave = f => red(f) });
            t.Agregar(new Columna<T>
            {
                Nombre = "dedocentes", Titulo = "De docentes", Ancho = 80, Derecha = true, PrimeroDescendente = true, SoloAmplia = docentesSoloAmplia,
                Existe = () => Con(pestana, r => r.ConReportesDocentes),
                Texto = f => EstadisticasDatos.Miles(deDocentes(f)), Clave = f => deDocentes(f),
                AyudaDelTitulo = "Fallas que reportaron los docentes desde su panel."
            });
        }

        private Tabla<FilaEquipo> CrearTablaEquipos()
        {
            Tabla<FilaEquipo> t = new Tabla<FilaEquipo>("Equipos", fuenteNegrita)
            {
                TextoDeBusqueda = e => e.Laboratorio + " " + e.Equipo + " " + e.LoUltimo,
                ClaveDeFila = e => e.LaboratorioId.ToString(Inv) + "|" + e.Tipo + "|" + (e.Equipo ?? "").ToUpperInvariant(),
                FondoDeFila = e => e.Repetido ? FondoRepetido : (Color?)null,
                AyudaDeFila = AyudaDeEquipo,
                SinCoincidencias = "Ningún equipo coincide con la búsqueda.",
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => TextoSinFilas(PestanaEstadisticas.Equipos, "Ningún equipo tiene fallas ni avisos en este periodo.");
            t.SinFilasEsError = () => SinFilasPorError(PestanaEstadisticas.Equipos);
            t.Agregar(new Columna<FilaEquipo> { Nombre = "laboratorio", Titulo = "Laboratorio", Rellena = true, Ancho = 150, Peso = 55, Texto = e => e.Laboratorio, Clave = e => e.Laboratorio });
            t.Agregar(new Columna<FilaEquipo>
            {
                Nombre = "equipo", Titulo = "Equipo", Ancho = 130, Texto = e => e.Equipo, Clave = e => e.Equipo,
                ColorTexto = e => e.Tipo == TipoFilaEquipo.Equipo ? (Color?)null : TextoGris
            });
            AgregarColumnasDeFallas(t, PestanaEstadisticas.Equipos, e => e.Fallas, e => e.Hardware, e => e.Software, e => e.Red, e => e.DeDocentes, true);
            // "Fallo en" va detras de Fallas reportadas (sec. 6.3).
            t.Agregar(new Columna<FilaEquipo>
            {
                Nombre = "falloen", Titulo = "Falló en", Ancho = 135, Texto = e => e.FalloEnTexto, Despues = "fallas",
                Fraccion = e => e.Tipo == TipoFilaEquipo.TodoElLaboratorio || !e.Registro ? (double?)null : BarraEnCelda.Fraccion(e.SesionesConFalla, e.SesionesUsado),
                Estilo = EstiloBarra.FalloEn, PrimeroDescendente = true,
                Clave = e => e.Tipo == TipoFilaEquipo.TodoElLaboratorio || !e.Registro ? null : Fraccion(e.SesionesConFalla, e.SesionesUsado),
                AyudaDelTitulo = "Sesiones en que alguien que registró desde este equipo reportó una falla, de las sesiones en que se usó. "
                    + "Los reportes de docentes cuentan en Fallas reportadas, no aquí."
            });
            t.Agregar(new Columna<FilaEquipo>
            {
                Nombre = "avisos", Titulo = "Avisos sin internet", Ancho = 90, Derecha = true, PrimeroDescendente = true,
                Existe = () => Con(PestanaEstadisticas.Equipos, r => r.ConAvisos),
                Texto = e => EstadisticasDatos.Miles(e.Avisos), Clave = e => e.Avisos, ColorTexto = e => e.Avisos > 0 ? NaranjaTexto : (Color?)null,
                AyudaDelTitulo = "Bitácoras de otros estudiantes del mismo laboratorio que avisaron que este equipo no tenía internet (una por bitácora)."
            });
            t.Agregar(new Columna<FilaEquipo>
            {
                Nombre = "dias", Titulo = "Días con reporte", Ancho = 85, Derecha = true, SoloAmplia = true, PrimeroDescendente = true,
                Texto = e => EstadisticasDatos.Miles(e.DiasConReporte), Clave = e => e.DiasConReporte,
                AyudaDelTitulo = "Días distintos con una falla desde él, un reporte de docente que lo nombra o un aviso. Con 2 o más, la fila va en naranja."
            });
            t.Agregar(new Columna<FilaEquipo>
            {
                Nombre = "ultimo", Titulo = "Último reporte", Ancho = 120, PrimeroDescendente = true,
                Texto = e => Guion(e.UltimoReporte), Clave = e => e.UltimoDia.HasValue ? (IComparable)(e.UltimoDia.Value.Ticks + (e.UltimaHora.HasValue ? e.UltimaHora.Value.Ticks + 1 : 0)) : null,
                AyudaDelTitulo = "El día de la sesión y la hora en que se registró."
            });
            t.Agregar(new Columna<FilaEquipo>
            {
                Nombre = "loultimo", Titulo = "Lo último que reportaron", Rellena = true, Ancho = 160, Texto = e => Guion(e.LoUltimo), Clave = e => Vacio(e.LoUltimo)
            });
            return t;
        }

        private static string AyudaDeEquipo(FilaEquipo e, bool amplia)
        {
            List<string> partes = new List<string>();
            if (e.Tipo == TipoFilaEquipo.TodoElLaboratorio)
            {
                partes.Add("Reportes de docentes sin equipo: de todo el laboratorio.");
            }
            else if (e.Tipo == TipoFilaEquipo.SinEquipo)
            {
                partes.Add("Bitácoras registradas sin el nombre del equipo.");
            }
            else if (e.EsNombreDeWindows)
            {
                partes.Add("Equipo sin número (nombre de Windows): ponle su «Número de máquina» al instalar DigBit.");
            }

            if (!amplia)
            {
                partes.Add("Días con reporte: " + EstadisticasDatos.Miles(e.DiasConReporte) + "."
                    + (e.DeDocentes > 0 ? " De docentes: " + EstadisticasDatos.Miles(e.DeDocentes) + "." : ""));
            }

            if (!string.IsNullOrEmpty(e.LoUltimo))
            {
                partes.Add("Lo último: " + e.LoUltimo);
            }

            return partes.Count == 0 ? null : string.Join("\n", partes);
        }

        /// <summary>
        /// Lo que se pinta en una tabla sin filas: leyendo, el error de la lectura, el del
        /// calculo de esa pestana, que calcula o el vacio de esa pestana con sus filtros.
        /// </summary>
        private string TextoSinFilas(PestanaEstadisticas pestana, string vacio)
        {
            if (leyendoBase)
            {
                return TextoLeyendo;
            }

            if (error != null)
            {
                return error;
            }

            FiltroDeApartado f = FiltroDe(pestana);
            if (f.Error != null)
            {
                return f.Error;
            }

            if (f.Resultado == null)
            {
                return lectura == null ? TextoLeyendo : TextoCalculando;
            }

            return f.Resultado.TextoVacio ?? vacio;
        }

        /// <summary>Lo que se pinta sin filas es un error (de la lectura o del calculo de esa pestana), en rojo.</summary>
        private bool SinFilasPorError(PestanaEstadisticas pestana)
        {
            return !leyendoBase && (error != null || FiltroDe(pestana).Error != null);
        }

        // =====================================================================
        // Piezas de la pantalla
        // =====================================================================

        private Label Etiqueta(string texto)
        {
            Label etiqueta = EstiloHorarios.CrearEtiqueta(texto, Point.Empty);
            Controls.Add(etiqueta);
            return etiqueta;
        }

        private ComboBox Combo(Control padre, int orden, string nombre)
        {
            ComboBox combo = EstiloHorarios.CrearCombo(Point.Empty, 200);
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 20;
            combo.MaxDropDownItems = 20;
            combo.TabIndex = orden;
            combo.AccessibleName = nombre;
            padre.Controls.Add(combo);
            return combo;
        }

        /// <summary>
        /// Las listas de los filtros de una pestana (2026-10-31), en ese orden, con lo que
        /// recordaba esa pestana. Con laboratorioConFlechas, el laboratorio va entre flechas
        /// (FlechasDeLista, como en Horarios, sin deslizamiento: aqui los numeros se calculan
        /// despues, en segundo plano); en Equipos se salta "Todos los laboratorios".
        /// </summary>
        private void AgregarListas(Pagina pagina, bool laboratorioConFlechas, params TipoDeFiltro[] tipos)
        {
            FiltroDeApartado f = pagina.Filtro;
            ClaveDeFiltro recordado;
            filtrosRecordados.TryGetValue(pagina.Cual, out recordado);
            foreach (TipoDeFiltro tipo in tipos)
            {
                ListaDeFiltro lista = new ListaDeFiltro { Tipo = tipo };
                lista.Etiqueta = EstiloHorarios.CrearEtiqueta(NombreDe(tipo), Point.Empty);
                pagina.Tab.Controls.Add(lista.Etiqueta);
                lista.Combo = Combo(pagina.Tab, f.Listas.Count, NombreDe(tipo));
                lista.Combo.Tag = tipo;
                lista.Combo.DrawItem += tipo == TipoDeFiltro.Docente ? (DrawItemEventHandler)DibujarDocente : DibujarOpcion;
                // Antes que las flechas (FlechasDeLista.Direccion lo pide asi).
                lista.Combo.SelectedIndexChanged += (s, e) => { if (!poniendoCombos) ListaCambiada(pagina); };
                if (laboratorioConFlechas && tipo == TipoDeFiltro.Laboratorio)
                {
                    ComboBox combo = lista.Combo;
                    lista.Flechas = new FlechasDeLista(combo, "Laboratorio");
                    if (pagina.Cual == PestanaEstadisticas.Equipos)
                    {
                        lista.Flechas.Saltar = i => (combo.Items[i] as OpcionCombo)?.Id == 0;
                    }
                }

                f.Listas.Add(lista);
                f.Poner(tipo, recordado.Id(tipo));
            }
        }

        private static string NombreDe(TipoDeFiltro tipo)
        {
            switch (tipo)
            {
                case TipoDeFiltro.Laboratorio:
                    return "Laboratorio";
                case TipoDeFiltro.Docente:
                    return "Docente";
                default:
                    return "Grupo";
            }
        }

        /// <summary>El "Todos" de cada lista, como los pone EstadisticasDatos (ArmarOpciones).</summary>
        private static string TextoDeTodos(TipoDeFiltro tipo)
        {
            switch (tipo)
            {
                case TipoDeFiltro.Laboratorio:
                    return "Todos los laboratorios";
                case TipoDeFiltro.Docente:
                    return "Todo el personal docente";
                default:
                    return "Todos los grupos";
            }
        }

        private Pagina NuevaPagina(PestanaEstadisticas cual, string titulo, bool conBuscador)
        {
            Pagina pagina = new Pagina { Cual = cual, Tab = new TabPage(titulo) { BackColor = Color.White, UseVisualStyleBackColor = false } };
            pagina.Linea = new Label { AutoSize = false, AutoEllipsis = true, Font = fuenteLinea, ForeColor = TextoGris };
            pagina.Tab.Controls.Add(pagina.Linea);
            if (conBuscador)
            {
                pagina.EtiquetaBuscar = EstiloHorarios.CrearEtiqueta("Buscar", Point.Empty);
                pagina.Buscador = EstiloHorarios.CrearTexto(Point.Empty, AnchoBuscador);
                pagina.Buscador.AccessibleName = "Buscar en " + titulo;
                pagina.Espera = new Timer { Interval = 250 };
                pagina.Espera.Tick += (s, e) =>
                {
                    pagina.Espera.Stop();
                    if (pagina.Tabla != null)
                    {
                        pagina.Tabla.Buscar(pagina.Buscador.Text);
                    }
                };
                pagina.Buscador.TextChanged += (s, e) => { pagina.Espera.Stop(); pagina.Espera.Start(); };
                pagina.Buscador.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Down && pagina.Tabla != null && pagina.Tabla.Grid.Rows.Count > 0)
                    {
                        pagina.Tabla.Grid.Focus();
                        e.Handled = true;
                    }
                };
                pagina.Tab.Controls.Add(pagina.EtiquetaBuscar);
                // Con Tab, despues de las listas de la pestana y antes de su tabla.
                pagina.Buscador.TabIndex = 5;
                pagina.Tab.Controls.Add(pagina.Buscador);
            }

            paginas[cual] = pagina;
            tabs.TabPages.Add(pagina.Tab);
            return pagina;
        }

        private Label TextoDeFicha(Ficha ficha, DrawingFont fuente, Color color)
        {
            Label etiqueta = new Label { AutoSize = false, AutoEllipsis = true, Font = fuente, ForeColor = color, UseMnemonic = false };
            ficha.Contenido.Controls.Add(etiqueta);
            return etiqueta;
        }

        /// <summary>Una pestana con la letra y el verde de DigBit, como Horarios.</summary>
        private void DibujarPestana(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= tabs.TabPages.Count)
            {
                return;
            }

            bool elegida = e.Index == tabs.SelectedIndex;
            using (SolidBrush fondo = new SolidBrush(elegida ? Color.DarkGreen : Color.FromArgb(232, 244, 232)))
            {
                e.Graphics.FillRectangle(fondo, e.Bounds);
            }

            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, fuentePestana, e.Bounds, elegida ? Color.White : Color.DarkGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (elegida && (e.State & DrawItemState.Focus) != 0 && tabs.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -4, -4), Color.White, Color.DarkGreen);
            }
        }

        /// <summary>Una opcion de un combo, cortada con "..." si no cabe (entera en la lista y en la ayuda).</summary>
        private void DibujarOpcion(object sender, DrawItemEventArgs e)
        {
            // Lo mismo que la ficha del docente (con la raya del bloque de las plantillas en Periodo).
            SelectorDePeriodo.DibujarOpcion((ComboBox)sender, e);
        }

        /// <summary>El cuadrito con el color del docente y su nombre (como el combo de Horarios, AdministrarHorarios.DibujarProfesorConColor).</summary>
        private void DibujarDocente(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0)
            {
                return;
            }

            ComboBox combo = (ComboBox)sender;
            OpcionCombo opcion = combo.Items[e.Index] as OpcionCombo;
            int x = e.Bounds.X + 4;
            if (opcion != null && opcion.Id != 0)
            {
                Rectangle cuadro = new Rectangle(x, e.Bounds.Y + 3, 14, e.Bounds.Height - 6);
                using (SolidBrush relleno = new SolidBrush(CuadriculaSemanal.ColorDeProfesor(opcion.Id)))
                {
                    e.Graphics.FillRectangle(relleno, cuadro);
                }

                e.Graphics.DrawRectangle(Pens.Gray, cuadro);
            }

            // En el combo cerrado, "Todo el personal docente" sin el hueco del cuadrito: asi cabe entero.
            if (opcion == null || opcion.Id != 0 || (e.State & DrawItemState.ComboBoxEdit) == 0)
            {
                x += 20;
            }

            TextRenderer.DrawText(e.Graphics, Convert.ToString(combo.Items[e.Index], Inv), e.Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x, e.Bounds.Height),
                e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            e.DrawFocusRectangle();
        }

        // =====================================================================
        // Disposicion (sec. 6.2 y 6.3)
        // =====================================================================

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Acomodar();
        }

        /// <summary>Coloca todo segun el tamano: amplia desde 1600 x 860 de cliente, compacta por debajo y baja (sin subtitulo) con menos de 640 de alto.</summary>
        private void Acomodar()
        {
            if (!construida || WindowState == FormWindowState.Minimized || ClientSize.Width < 300 || ClientSize.Height < 300)
            {
                return;
            }

            Size cliente = ClientSize;
            bool amplia = cliente.Width >= 1600 && cliente.Height >= 860;
            bool baja = cliente.Height < AltoMinimoNormal;
            if (lblSubtitulo != null)
            {
                lblSubtitulo.Visible = !baja;
            }

            SuspendLayout();
            // Arriba lo de todo (el periodo, junto al titulo, y las cifras generales); los
            // filtros de cada apartado, dentro de su pestana (AcomodarPagina).
            AcomodarPeriodo(cliente.Width, amplia);
            int yCifras = baja ? 50 : 84;
            int anchoCifras = cliente.Width - 2 * Margen;
            int cada = (anchoCifras - 4 * Separacion) / 5;
            // Baja: los tres renglones de la cifra caben en 78 px (se centran solos); asi las tablas ganan una fila.
            int altoCifras = baja ? AltoCifrasBaja : CifraGrande.Alto;
            for (int i = 0; i < cifras.Length; i++)
            {
                int x = Margen + i * (cada + Separacion);
                cifras[i].SetBounds(x, yCifras, i == cifras.Length - 1 ? Margen + anchoCifras - x : cada, altoCifras);
            }

            // Sin la fila de filtros de antes (2026-10-31): las pestanas, justo debajo de las cifras.
            int yTabs = yCifras + altoCifras + (baja ? 8 : 12);
            disposicionBaja = baja;
            tabs.SetBounds(Margen, yTabs, cliente.Width - 2 * Margen, Math.Max(150, cliente.Height - AbajoDeLasPestanas - yTabs));
            // Las siete pestanas caben siempre: a 1000 px de ancho, sin las flechas de desplazamiento del TabControl.
            int anchoPestana = Math.Max(100, Math.Min(132, (tabs.Width - 8) / Math.Max(1, tabs.TabPages.Count)));
            if (tabs.ItemSize.Width != anchoPestana)
            {
                tabs.ItemSize = new Size(anchoPestana, 26);
            }
            if (amplia != disposicionAmplia || !disposicionPuesta)
            {
                disposicionAmplia = amplia;
                disposicionPuesta = true;
                foreach (ITablaDisposicion tabla in Tablas())
                {
                    tabla.Disposicion(amplia);
                }
            }

            ResumeLayout(true);
            foreach (Pagina pagina in paginas.Values)
            {
                AcomodarPagina(pagina);
            }

            PonerNota();
        }

        private IEnumerable<ITablaDisposicion> Tablas()
        {
            return new ITablaDisposicion[] { tEstudiantes, tSusClases, tClases, tLista, tGrupos, tDocentes, tSusClasesDia, tLaboratorios, tEquipos };
        }

        /// <summary>El periodo, arriba a la derecha junto al titulo: es de todo (las cifras generales y los apartados).</summary>
        private void AcomodarPeriodo(int ancho, bool amplia)
        {
            // Con el nombre de la plantilla ("Este ciclo: 2026-2 (desde el 05/08/2026)") pide mas.
            int anchoCombo = amplia || ancho >= 1600 ? 380 : 340;
            int x = ancho - Margen - anchoCombo;
            cboPeriodo.SetBounds(x, 17, anchoCombo, cboPeriodo.Height);
            lblPeriodo.Location = new Point(x - 6 - lblPeriodo.PreferredSize.Width, 21);
            AnchoDeLaLista(cboPeriodo);
        }

        /// <summary>
        /// Las listas de una pestana en una fila, de x hasta "hasta" (sec. 6.2; desde 2026-10-31
        /// dentro de cada pestana): el laboratorio 300 px, el docente 260 y el grupo 170; con
        /// mucho ancho, 400, 320 y 200. Entre flechas (Equipos y Graficas) el nombre del
        /// laboratorio es lo que se lee: si sobra sitio, crece hasta su nombre mas largo. Si no
        /// caben, encogen por turnos (primero lo que crecio) hasta que se lea entero su "Todos"
        /// y despues hasta su minimo (el nombre entero va en la ayuda).
        /// </summary>
        private void PonerListas(FiltroDeApartado f, int x, int y, int hasta)
        {
            List<ListaDeFiltro> listas = f.Listas;
            if (listas.Count == 0)
            {
                return;
            }

            bool ancha = disposicionAmplia || ClientSize.Width >= 1600;
            int[] preferidos = listas.Select(l => AnchoPreferido(l.Tipo, ancha)).ToArray();
            int[] anchos = listas.Select((l, i) => l.Flechas == null ? preferidos[i] : Math.Max(preferidos[i], AnchoDelMasLargo(l))).ToArray();
            int[] comodos = listas.Select(AnchoComodo).ToArray();
            int[] minimos = listas.Select(l => l.Flechas != null ? 150 : 100).ToArray();
            int[] todas = Enumerable.Range(0, listas.Count).ToArray();
            int disponible = hasta - x - AnchoDeEtiquetas(f);
            Encoger(anchos, disponible, todas, preferidos);
            Encoger(anchos, disponible, todas, comodos);
            Encoger(anchos, disponible, todas, minimos);
            for (int i = 0; i < listas.Count; i++)
            {
                ListaDeFiltro lista = listas[i];
                Size etiqueta = lista.Etiqueta.PreferredSize;
                lista.Etiqueta.Location = new Point(x, y + (AltoFila - etiqueta.Height) / 2);
                x += etiqueta.Width + 6;
                lista.Combo.SetBounds(x, y + (AltoFila - lista.Combo.Height) / 2, anchos[i], lista.Combo.Height);
                x += anchos[i] + EntreListas;
                AnchoDeLaLista(lista.Combo);
            }
        }

        private static int AnchoPreferido(TipoDeFiltro tipo, bool ancha)
        {
            switch (tipo)
            {
                case TipoDeFiltro.Laboratorio:
                    return ancha ? 400 : 300;
                case TipoDeFiltro.Docente:
                    return ancha ? 320 : 260;
                default:
                    return ancha ? 200 : 170;
            }
        }

        /// <summary>Lo que pide un laboratorio entre flechas para que se lea entera su opcion mas larga (sin pasar de 560 px).</summary>
        private int AnchoDelMasLargo(ListaDeFiltro lista)
        {
            int largo = 0;
            foreach (object opcion in lista.Combo.Items)
            {
                largo = Math.Max(largo, TextRenderer.MeasureText(Convert.ToString(opcion, Inv), fuenteNegrita).Width);
            }

            return Math.Min(560, largo + 2 * (30 + 4) + 10);
        }

        /// <summary>Lo que pide una lista para que se lea entero su "Todos" (con flechas, entre ellas, en negrita).</summary>
        private int AnchoComodo(ListaDeFiltro lista)
        {
            string todos = TextoDeTodos(lista.Tipo);
            return lista.Flechas != null
                ? TextRenderer.MeasureText(todos, fuenteNegrita).Width + 2 * (30 + 4) + 10
                : TextRenderer.MeasureText(todos, lista.Combo.Font).Width + 12 + SystemInformation.VerticalScrollBarWidth + 6;
        }

        /// <summary>Las etiquetas de las listas y lo que va entre ellas (sin las listas).</summary>
        private static int AnchoDeEtiquetas(FiltroDeApartado f)
        {
            return f.Listas.Sum(l => l.Etiqueta.PreferredSize.Width + 6) + EntreListas * Math.Max(0, f.Listas.Count - 1);
        }

        /// <summary>Lo que piden todas las listas de una pestana para leerse enteras, con sus etiquetas.</summary>
        private int AnchoComodoDeListas(FiltroDeApartado f)
        {
            return AnchoDeEtiquetas(f) + f.Listas.Sum(l => AnchoComodo(l));
        }

        /// <summary>
        /// Con poco ancho no caben en una fila tres listas (las de Estudiantes y Clases) con su
        /// "Todos" entero y Buscar: entonces, en todas las pestanas con listas, Buscar pasa a la
        /// derecha de la linea (asi no salta de sitio al cambiar de pestana).
        /// </summary>
        private bool BuscadorEnLaLinea(int anchoPagina)
        {
            Pagina referencia = paginas[PestanaEstadisticas.Estudiantes];
            int buscar = referencia.EtiquetaBuscar.PreferredSize.Width + 8 + AnchoBuscador;
            return AnchoComodoDeListas(referencia.Filtro) + 24 + buscar > anchoPagina - 16;
        }

        /// <summary>Buscar a la derecha de esa fila; devuelve hasta donde llega lo que va a su izquierda.</summary>
        private static int PonerBuscador(Pagina pagina, int derecha, int fila)
        {
            if (pagina.Buscador == null)
            {
                return derecha;
            }

            pagina.Buscador.SetBounds(derecha - AnchoBuscador, fila + (AltoFila - pagina.Buscador.Height) / 2, AnchoBuscador, pagina.Buscador.Height);
            Size etiqueta = pagina.EtiquetaBuscar.PreferredSize;
            pagina.EtiquetaBuscar.Location = new Point(pagina.Buscador.Left - 8 - etiqueta.Width, fila + (AltoFila - etiqueta.Height) / 2);
            return pagina.EtiquetaBuscar.Left - 16;
        }

        /// <summary>"Agrupar", "Por semana" y "Por mes" acabando en "hasta", centrados en esa fila; devuelve donde empiezan.</summary>
        private int PonerAgrupar(int hasta, int fila, int alto)
        {
            int yOpcion = fila + (alto - rbPorMes.PreferredSize.Height) / 2;
            rbPorMes.Location = new Point(hasta - rbPorMes.PreferredSize.Width, yOpcion);
            rbPorSemana.Location = new Point(rbPorMes.Left - 10 - rbPorSemana.PreferredSize.Width, yOpcion);
            lblAgrupar.Location = new Point(rbPorSemana.Left - 6 - lblAgrupar.PreferredSize.Width, fila + (alto - lblAgrupar.PreferredSize.Height) / 2);
            return lblAgrupar.Left;
        }

        /// <summary>
        /// Lo de arriba de Graficas: sus listas a la izquierda y, a la derecha, la agrupacion y
        /// el reporte; debajo, su linea. Si no cabe todo en una fila, el reporte se queda arriba
        /// y la agrupacion pasa junto a la linea; con aun menos ancho (1000 px), los dos.
        /// Devuelve donde acaba la fila de la linea.
        /// </summary>
        private int AcomodarArribaDeGraficas(Pagina pagina, int izquierda, int derecha, int y0, int y1)
        {
            int anchoAgrupar = lblAgrupar.PreferredSize.Width + 6 + rbPorSemana.PreferredSize.Width + 10 + rbPorMes.PreferredSize.Width;
            int anchoReporte = btnReporte == null ? 0 : btnReporte.Width;
            int listas = AnchoComodoDeListas(pagina.Filtro);
            int disponible = derecha - izquierda;
            bool agruparArriba = listas + 24 + anchoAgrupar + (anchoReporte > 0 ? 20 + anchoReporte : 0) <= disponible;
            bool reporteArriba = anchoReporte > 0 && listas + 24 + anchoReporte <= disponible;

            // Con Tab, en el orden en que se leen: si el reporte se queda arriba y la agrupacion baja
            // a la fila de la linea (con unos 1200 a 1470 px de ancho), primero el reporte; si no, la
            // agrupacion (a su izquierda en la misma fila).
            bool reporteAntes = btnReporte != null && reporteArriba && !agruparArriba;
            lblAgrupar.TabIndex = reporteAntes ? 5 : 4;
            rbPorSemana.TabIndex = reporteAntes ? 6 : 5;
            rbPorMes.TabIndex = reporteAntes ? 7 : 6;
            if (btnReporte != null)
            {
                btnReporte.TabIndex = reporteAntes ? 4 : 7;
            }

            // Hasta donde llegan las listas, hasta donde la linea y el alto de la fila de la linea.
            int finArriba = derecha;
            int finLinea = derecha;
            int altoAbajo = 20;
            if (btnReporte != null)
            {
                if (reporteArriba)
                {
                    btnReporte.Location = new Point(derecha - anchoReporte, y0 + (AltoFila - btnReporte.Height) / 2);
                    finArriba = btnReporte.Left - 20;
                }
                else
                {
                    btnReporte.Location = new Point(derecha - anchoReporte, y1);
                    finLinea = btnReporte.Left - 20;
                    altoAbajo = btnReporte.Height;
                }
            }

            if (agruparArriba)
            {
                finArriba = PonerAgrupar(finArriba, y0, AltoFila) - 24;
            }
            else
            {
                altoAbajo = Math.Max(altoAbajo, AltoFila);
                finLinea = PonerAgrupar(finLinea, y1, altoAbajo) - 16;
            }

            PonerListas(pagina.Filtro, izquierda, y0, finArriba);
            pagina.Linea.SetBounds(izquierda, y1 + (altoAbajo - 20) / 2, Math.Max(50, finLinea - izquierda), 20);
            return y1 + altoAbajo;
        }

        /// <summary>Reduce de uno en uno, por turnos, los anchos de esos indices (sin bajar de su minimo) hasta que la suma quepa.</summary>
        private static void Encoger(int[] anchos, int disponible, int[] cuales, int[] minimos)
        {
            while (anchos.Sum() > disponible)
            {
                bool alguno = false;
                foreach (int i in cuales)
                {
                    if (anchos[i] > minimos[i] && anchos.Sum() > disponible)
                    {
                        anchos[i]--;
                        alguno = true;
                    }
                }

                if (!alguno)
                {
                    return;
                }
            }
        }

        /// <summary>La lista desplegada, tan ancha como su opcion mas larga (con el cuadrito del docente), como en Horarios.</summary>
        private void AnchoDeLaLista(ComboBox combo)
        {
            int pide = 0;
            foreach (object opcion in combo.Items)
            {
                pide = Math.Max(pide, TextRenderer.MeasureText(Convert.ToString(opcion, Inv), combo.Font).Width);
            }

            pide += (combo.Tag is TipoDeFiltro && (TipoDeFiltro)combo.Tag == TipoDeFiltro.Docente ? 36 : 14) + SystemInformation.VerticalScrollBarWidth;
            combo.DropDownWidth = Math.Max(combo.Width, Math.Min(pide, 640));
        }

        /// <summary>
        /// Dentro de una pestana (2026-10-31): arriba sus listas a la izquierda y Buscar a la
        /// derecha (en Graficas, la agrupacion y el reporte), debajo su linea y despues su tabla
        /// (y su detalle). Con poco ancho, Buscar pasa a la fila de la linea; sin listas
        /// (Laboratorios), la linea y Buscar van juntos en la primera fila.
        /// </summary>
        private void AcomodarPagina(Pagina pagina)
        {
            if (!construida)
            {
                return;
            }

            Size tam = pagina.Tab.ClientSize;
            if (tam.Width < 100 || tam.Height < 80)
            {
                return;
            }

            int izquierda = 8;
            int derecha = tam.Width - 8;
            // La fila de las listas (y0), la de la linea (y1) y lo que se deja antes de la tabla.
            int y0 = disposicionBaja ? 3 : 6;
            int y1 = y0 + AltoFila + (disposicionBaja ? 4 : 7);
            int hueco = disposicionBaja ? 4 : 6;
            int y;
            if (pagina.Cual == PestanaEstadisticas.Graficas)
            {
                y = AcomodarArribaDeGraficas(pagina, izquierda, derecha, y0, y1) + hueco;
            }
            else if (pagina.Filtro.Listas.Count == 0)
            {
                int finLinea = PonerBuscador(pagina, derecha, y0);
                pagina.Linea.SetBounds(izquierda, y0 + 4, Math.Max(50, finLinea - izquierda), 20);
                y = y0 + AltoFila + hueco;
            }
            else if (BuscadorEnLaLinea(tam.Width))
            {
                PonerListas(pagina.Filtro, izquierda, y0, derecha);
                int finLinea = PonerBuscador(pagina, derecha, y1);
                pagina.Linea.SetBounds(izquierda, y1 + 4, Math.Max(50, finLinea - izquierda), 20);
                y = y1 + AltoFila + hueco;
            }
            else
            {
                int finListas = PonerBuscador(pagina, derecha, y0) - 8;
                PonerListas(pagina.Filtro, izquierda, y0, finListas);
                pagina.Linea.SetBounds(izquierda, y1, Math.Max(50, derecha - izquierda), 20);
                y = y1 + 20 + hueco;
            }

            int alto = tam.Height - y - 8;
            int ancho = tam.Width - 16;
            bool amplia = disposicionAmplia;
            switch (pagina.Cual)
            {
                case PestanaEstadisticas.Estudiantes:
                    Lado(tEstudiantes.Grid, fichaEstudiante, ancho, y, alto, amplia ? 0.66 : ancho < 1150 ? 0.6 : 0.64);
                    break;
                case PestanaEstadisticas.Clases:
                    Lado(tClases.Grid, fichaClase, ancho, y, alto, amplia ? 0.66 : 0.6);
                    break;
                case PestanaEstadisticas.Docentes:
                    // Mientras lee o si no se pudo leer, sin docentes: la tabla (con su aviso) ocupa todo.
                    // (Visible no sirve para preguntarlo: es false mientras la pestana no se ve.)
                    bool conDetalle = tDocentes.TieneFilas;
                    fichaDocente.Visible = conDetalle;
                    if (!conDetalle)
                    {
                        tDocentes.Grid.SetBounds(8, y, ancho, alto);
                    }
                    else if (amplia)
                    {
                        // Amplia: los docentes con lo que piden sus columnas (sin barra horizontal y unos
                        // 40 px mas para el nombre); el resto, sus clases dia por dia.
                        Lado(tDocentes.Grid, fichaDocente, ancho, y, alto, Math.Max(0.34, Math.Min(0.5, (tDocentes.AnchoMinimo + 40.0) / (ancho - Separacion))));
                    }
                    else
                    {
                        // Compacta: los docentes arriba (con el alto de sus filas, sin pasar de casi la mitad) y sus clases debajo, a lo ancho.
                        int arriba = Math.Max(disposicionBaja ? 90 : 100, Math.Min((int)(alto * (disposicionBaja ? 0.4 : 0.45)), tDocentes.AltoParaFilas(tDocentes.Grid.Rows.Count)));
                        tDocentes.Grid.SetBounds(8, y, ancho, arriba);
                        fichaDocente.SetBounds(8, y + arriba + 8, ancho, Math.Max(80, alto - arriba - 8));
                    }

                    break;
                case PestanaEstadisticas.Graficas:
                    // Sin el margen de las tablas: el panel deja el suyo (y su barra de desplazamiento al borde).
                    panelGraficas.SetBounds(2, y, tam.Width - 4, tam.Height - y - 2);
                    break;
                default:
                    pagina.Tabla.Grid.SetBounds(8, y, ancho, alto);
                    break;
            }
        }

        /// <summary>La tabla a la izquierda (esa fraccion) y su detalle a la derecha.</summary>
        private static void Lado(Control tabla, Control detalle, int ancho, int y, int alto, double fraccion)
        {
            int izquierda = (int)((ancho - Separacion) * fraccion);
            tabla.SetBounds(8, y, izquierda, alto);
            detalle.SetBounds(8 + izquierda + Separacion, y, ancho - izquierda - Separacion, alto);
        }

        private void AcomodarFichaEstudiante()
        {
            Size tam = fichaEstudiante.Contenido.ClientSize;
            if (tam.Width < 50 || tam.Height < 50)
            {
                return;
            }

            int x = 12;
            int ancho = tam.Width - 24;
            int y = 10;
            bool corta = tam.Height < 330;
            // Corta: "Ver sus bitacoras" arriba, junto al nombre, para dejarle sitio a la tabla.
            int arribaBoton = corta ? 6 : tam.Height - 10 - btnVerBitacoras.Height;
            btnVerBitacoras.Location = new Point(tam.Width - 12 - btnVerBitacoras.Width, arribaBoton);
            lblNombreEstudiante.SetBounds(x, y, corta ? Math.Max(60, btnVerBitacoras.Left - 8 - x) : ancho, 24);
            y += corta ? 34 : 30;
            foreach (Rotulo r in new[] { rMatricula, rGrupo, rBitacoras, rUltima })
            {
                r.Visible = !corta;
            }

            lblDatosEstudiante.Visible = corta;
            if (corta)
            {
                int altoDatos = AltoDeTexto(lblDatosEstudiante.Text, lblDatosEstudiante.Font, ancho, 40);
                lblDatosEstudiante.SetBounds(x, y, ancho, altoDatos);
                y += altoDatos + 6;
            }
            else
            {
                int mitad = (ancho - 12) / 2;
                rMatricula.Poner(x, y, mitad);
                rGrupo.Poner(x + mitad + 12, y, ancho - mitad - 12);
                y += 38;
                rBitacoras.Poner(x, y, mitad);
                rUltima.Poner(x + mitad + 12, y, ancho - mitad - 12);
                y += 42;
            }

            int abajo = corta ? tam.Height - 4 : arribaBoton - 2;
            int altoNotas = string.IsNullOrEmpty(lblNotasEstudiante.Text) ? 0 : AltoDeTexto(lblNotasEstudiante.Text, lblNotasEstudiante.Font, ancho, 80);
            lblNotasEstudiante.SetBounds(x, abajo - 4 - altoNotas, ancho, altoNotas);
            int abajoTabla = abajo - 6 - (altoNotas > 0 ? altoNotas + 4 : 0);
            tSusClases.Grid.SetBounds(x, y, ancho, Math.Max(52, abajoTabla - y));
        }

        private void AcomodarFichaClase()
        {
            Size tam = fichaClase.Contenido.ClientSize;
            if (tam.Width < 50 || tam.Height < 50)
            {
                return;
            }

            int x = 12;
            int ancho = tam.Width - 24;
            int y = 10;
            bool corta = tam.Height < 330;
            // Muy corta (1366x768 al 125 %): sin el titulo (la linea "Lista de 24 estudiantes..." ya lo
            // dice), asi la lista gana una fila.
            bool conTitulo = tam.Height >= 250;
            lblTituloClase.Visible = conTitulo;
            if (conTitulo)
            {
                lblTituloClase.SetBounds(x, y, ancho, 22);
                y += 28;
            }

            foreach (Rotulo r in new[] { rMateria, rGrupoClase, rDocente, rLaboratorio })
            {
                r.Visible = !corta;
            }

            lblDatosClase.Visible = corta;
            if (corta)
            {
                int altoDatos = AltoDeTexto(lblDatosClase.Text, lblDatosClase.Font, ancho, 40);
                lblDatosClase.SetBounds(x, y, ancho, altoDatos);
                y += altoDatos + 4;
            }
            else
            {
                int mitad = (ancho - 12) / 2;
                rMateria.Poner(x, y, mitad);
                rGrupoClase.Poner(x + mitad + 12, y, ancho - mitad - 12);
                y += 38;
                rDocente.Poner(x, y, mitad);
                rLaboratorio.Poner(x + mitad + 12, y, ancho - mitad - 12);
                y += 40;
            }

            // "Lista de 24 estudiantes; 9 dias de clase..." entera, en uno o dos renglones.
            int altoLinea = Math.Max(18, AltoDeTexto(lblLineaLista.Text, lblLineaLista.Font, ancho, 34));
            lblLineaLista.SetBounds(x, y, ancho, altoLinea);
            y += altoLinea + 4;
            tLista.Grid.SetBounds(x, y, ancho, Math.Max(52, tam.Height - 10 - y));
        }

        private void AcomodarFichaDocente()
        {
            Size tam = fichaDocente.Contenido.ClientSize;
            if (tam.Width < 50 || tam.Height < 40)
            {
                return;
            }

            int x = 12;
            int ancho = tam.Width - 24;
            // A lo ancho (compacta) el nombre y el resumen van en un renglon; al lado, en dos.
            int anchoNombre = Math.Min(ancho, TextRenderer.MeasureText(lblNombreDocente.Text, lblNombreDocente.Font).Width + 8);
            if (!disposicionAmplia && ancho - anchoNombre > 360)
            {
                int arribaTabla = disposicionBaja ? 32 : 38;
                lblNombreDocente.SetBounds(x, disposicionBaja ? 5 : 8, anchoNombre, 24);
                lblResumenDocente.SetBounds(x + anchoNombre + 8, disposicionBaja ? 9 : 12, ancho - anchoNombre - 8, 20);
                tSusClasesDia.Grid.SetBounds(x, arribaTabla, ancho, Math.Max(52, tam.Height - arribaTabla - 6));
            }
            else
            {
                lblNombreDocente.SetBounds(x, 8, ancho, 24);
                lblResumenDocente.SetBounds(x, 34, ancho, 20);
                tSusClasesDia.Grid.SetBounds(x, 60, ancho, Math.Max(52, tam.Height - 60 - 10));
            }
        }

        private static int AltoDeTexto(string texto, DrawingFont fuente, int ancho, int maximo)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return 0;
            }

            int alto = TextRenderer.MeasureText(texto, fuente, new Size(Math.Max(20, ancho), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            return Math.Min(alto + 2, maximo);
        }

        // =====================================================================
        // Lectura y calculo (sec. 5.2 y 6.4)
        // =====================================================================

        protected override void Recargar(string seleccionar)
        {
            Leer(false);
        }

        /// <summary>
        /// Lee (o toma la lectura guardada si cubre el periodo) en segundo plano y calcula
        /// lo general (las cifras) y la pestana que se ve con sus filtros; las demas, al
        /// elegirlas. forzar = Actualizar (F5): olvida la guardada.
        /// </summary>
        private void Leer(bool forzar)
        {
            ++lecturas;
            leyendoBase = true;
            esperaCalculo.Stop();
            btnActualizar.Enabled = false;
            lblResumen.Text = TextoLeyendo;
            ayuda.SetToolTip(lblResumen, null);
            foreach (CifraGrande cifra in cifras)
            {
                cifra.Leyendo = true;
            }

            // Lo que aun se calculaba con la lectura de antes ya no se pinta (llega con otro numero).
            foreach (Pagina pagina in paginas.Values)
            {
                pagina.Filtro.Calculando = false;
                pagina.Filtro.Error = null;
            }

            // Mientras lee no se ensena lo de otro periodo: las tablas dicen que lee.
            error = null;
            VaciarTablas();
            NotaFija = "";

            if (baseEnMarcha)
            {
                // Una sola lectura de la base a la vez: cambiar el periodo (o Actualizar)
                // mientras lee (con la base remota, varios segundos) no lanza otra. Al
                // acabar la que va, se lanza la ultima pedida, casi siempre desde la
                // lectura guardada. Los filtros de las pestanas no leen: solo calculan.
                otraPedida = true;
                otraForzada |= forzar;
                return;
            }

            LanzarLectura(forzar);
        }

        /// <summary>
        /// La lectura de la base en Task.Run, con el periodo de ahora; de paso calcula lo
        /// general y la pestana que se ve con sus filtros (si son "Todos", el mismo calculo).
        /// </summary>
        private void LanzarLectura(bool forzar)
        {
            int numero = lecturas;
            baseEnMarcha = true;
            PeriodoElegido pedido = periodo;
            ClaveDeFiltro clave = FiltroDe(PestanaElegida).Clave;
            // La de la ficha del docente ("Ver en Estadisticas"), una vez: con F5 u otro periodo, se lee.
            LecturaEstadisticas deLaFicha = !forzar && lecturaDeLaFicha != null && SelectorDePeriodo.Iguales(pedido, periodoDeLaFicha) ? lecturaDeLaFicha : null;
            lecturaDeLaFicha = null;
            periodoDeLaFicha = null;
            Task.Run(() =>
            {
                LecturaEstadisticas leida = deLaFicha != null && EstadisticasDatos.Cubre(deLaFicha, pedido) ? deLaFicha : EstadisticasDatos.Leer(pedido, forzar);
                return Calcular(pedido, leida, clave);
            }).ContinueWith(t => EnLaVentana(() =>
            {
                baseEnMarcha = false;
                if (otraPedida)
                {
                    // Mientras leia se pidio otra (otro periodo o Actualizar): esta ya no vale.
                    bool forzada = otraForzada;
                    otraPedida = false;
                    otraForzada = false;
                    LanzarLectura(forzada);
                    return;
                }

                Recibir(numero, pedido, clave, t);
            }, t));
        }

        /// <summary>Lo que devuelve la lectura en segundo plano.</summary>
        private sealed class Calculado
        {
            public LecturaEstadisticas Lectura;
            /// <summary>Todos los laboratorios, docentes y grupos: las cifras de arriba.</summary>
            public ResultadoEstadisticas General;
            /// <summary>Con los filtros de la pestana que se veia al pedir la lectura.</summary>
            public ResultadoEstadisticas DelFiltro;
        }

        /// <summary>Lo general y lo de esos filtros. Solo calcula: la lectura ya esta.</summary>
        private static Calculado Calcular(PeriodoElegido pedido, LecturaEstadisticas leida, ClaveDeFiltro clave)
        {
            ResultadoEstadisticas general = EstadisticasDatos.Calcular(leida, EstadisticasDatos.Resolver(pedido, leida, 0, 0, 0));
            return new Calculado { Lectura = leida, General = general, DelFiltro = CalcularFiltro(pedido, leida, clave, general) };
        }

        /// <summary>
        /// Lo de los filtros de una pestana (en segundo plano: no toca la pantalla). Si
        /// quedan en "Todos" sin aviso (un laboratorio que ya no existe), es lo general; con
        /// aviso (un docente o un grupo sin clases en el periodo, sec. 6.5) se calcula
        /// aparte: el aviso es de la pestana que lo pidio, no de las cifras.
        /// </summary>
        private static ResultadoEstadisticas CalcularFiltro(PeriodoElegido pedido, LecturaEstadisticas leida, ClaveDeFiltro clave, ResultadoEstadisticas general)
        {
            if (general != null && clave.EsGeneral)
            {
                return general;
            }

            FiltroEstadisticas filtro = EstadisticasDatos.Resolver(pedido, leida, clave.Laboratorio, clave.Docente, clave.Grupo);
            if (general != null && filtro.Laboratorio == 0 && filtro.Docente == 0 && filtro.Grupo == 0 && filtro.Aviso == null)
            {
                return general;
            }

            return EstadisticasDatos.Calcular(leida, filtro);
        }

        private void Recibir(int numero, PeriodoElegido pedido, ClaveDeFiltro clave, Task<Calculado> tarea)
        {
            if (numero != lecturas || IsDisposed)
            {
                // Llego tarde: ya se pidio otra.
                return;
            }

            leyendoBase = false;
            esperaCalculo.Stop();
            btnActualizar.Enabled = true;
            if (tarea.IsFaulted || tarea.IsCanceled)
            {
                Exception ex = tarea.Exception == null ? new OperationCanceledException() : tarea.Exception.GetBaseException();
                Log.Error("Estadisticas: leer", ex);
                if (resultadoGeneral != null && lectura != null)
                {
                    // Se sigue viendo lo leido, con su periodo y los filtros de cada pestana (lo
                    // calculado con esa lectura sigue valiendo). El periodo vuelve al de lo leido:
                    // el combo, lo que calculan las pestanas, F5 y lo que se recuerda, no el que se
                    // pidio y no se pudo leer.
                    VolverAlPeriodoLeido();
                    error = null;
                    DateTime ahora = DateTime.Now + Desfase(lectura);
                    falloDeLectura = "No se pudo actualizar a las " + ahora.ToString("HH:mm", Inv) + " (" + AvisoDeFalloAdmin.Causa(ex) + "): se ve lo leído a las "
                        + lectura.AhoraServidor.ToString("HH:mm", Inv) + ".";
                    ayudaDelFallo = AvisoDeFalloAdmin.Texto("No se pudieron leer las estadísticas", ex);
                    PonerGeneral();
                    foreach (Pagina pagina in paginas.Values)
                    {
                        // La que cambio sus filtros mientras se leia se calcula al verse.
                        if (pagina.Filtro.AlDia)
                        {
                            PonerApartado(pagina, pagina.Filtro.Resultado);
                        }
                        else
                        {
                            pagina.Filtro.Resultado = null;
                        }
                    }

                    MostrarApartado(paginas[PestanaElegida]);
                    PonerResumen();
                    PonerNota();
                    return;
                }

                error = AvisoDeFalloAdmin.Texto("No se pudieron leer las estadísticas", ex);
                resultadoGeneral = null;
                foreach (Pagina pagina in paginas.Values)
                {
                    pagina.Filtro.Resultado = null;
                }

                foreach (CifraGrande cifra in cifras)
                {
                    cifra.Mostrar(CifraGrande.SinDato, CifraGrande.ColorGris, "no se pudo leer", null, error);
                }

                VaciarTablas();
                lblResumen.Text = "No se pudieron leer las estadísticas.";
                ayuda.SetToolTip(lblResumen, error);
                NotaFija = "";
                return;
            }

            // Otra lectura (u otro periodo): lo calculado antes ya no vale. Se ponen lo general y
            // la pestana que se ve; las demas, al elegirlas (con "Todos", lo general mismo).
            Calculado c = tarea.Result;
            lectura = c.Lectura;
            // Mientras se leia no se pudo pedir otro periodo sin otra lectura (Leer): es el de ahora.
            periodoLeido = pedido;
            resultadoGeneral = c.General;
            error = null;
            falloDeLectura = null;
            ayudaDelFallo = null;
            calculados.Clear();
            ordenDeGuardados.Clear();
            Guardar(ClaveDeFiltro.General, c.General);
            Guardar(clave, c.DelFiltro);
            foreach (Pagina pagina in paginas.Values)
            {
                pagina.Filtro.Resultado = null;
                pagina.Filtro.Calculando = false;
                pagina.Filtro.Error = null;
                PonerLinea(pagina.Cual, "");
            }

            PonerGeneral();
            MostrarApartado(paginas[PestanaElegida]);
            PonerResumen();
            PonerNota();
        }

        private void VaciarTablas()
        {
            foreach (Pagina pagina in paginas.Values)
            {
                VaciarApartado(pagina);
            }
        }

        /// <summary>Las tablas de una pestana sin filas: pintan "Leyendo...", el error o "Calculando...".</summary>
        private void VaciarApartado(Pagina pagina)
        {
            foreach (ITablaDisposicion tabla in pagina.Tablas)
            {
                tabla.Vaciar();
            }

            switch (pagina.Cual)
            {
                case PestanaEstadisticas.Estudiantes:
                    MostrarEstudiante(null);
                    break;
                case PestanaEstadisticas.Clases:
                    MostrarClase(null);
                    break;
                case PestanaEstadisticas.Docentes:
                    MostrarDocente(null);
                    AcomodarPagina(pagina);
                    break;
                case PestanaEstadisticas.Graficas:
                    panelGraficas.Avisar(TextoSinFilas(PestanaEstadisticas.Graficas, ""), SinFilasPorError(PestanaEstadisticas.Graficas));
                    break;
            }
        }

        /// <summary>
        /// Lo general en la pantalla: el periodo, las cinco cifras (de todo el TESCHI) y las
        /// opciones de las listas de todas las pestanas (todas las del periodo).
        /// </summary>
        private void PonerGeneral()
        {
            ResultadoEstadisticas g = resultadoGeneral;
            // Lo pedido queda como lo que se ve ("Este ciclo" entre ciclos se sigue pidiendo como
            // "Este ciclo"; un ciclo que ya no esta pasa al que se ve): tambien tras un fallo de lectura.
            periodo = PeriodoQueQueda(periodo, g.Filtro);
            ultimoPeriodo = periodo;
            bool antes = poniendoCombos;
            poniendoCombos = true;
            try
            {
                LlenarPeriodo(lectura, g);
                AnchoDeLaLista(cboPeriodo);
            }
            finally
            {
                poniendoCombos = antes;
            }

            CifrasEstadisticas generales = g.Cifras;
            CifraEstadistica[] cinco = { generales.Asistencia, generales.ClasesQueSeDieronCifra, generales.Bitacoras, generales.Fallas, generales.SinInternet };
            for (int i = 0; i < cifras.Length; i++)
            {
                cifras[i].Etiqueta = cinco[i].Etiqueta;
                cifras[i].Mostrar(cinco[i].Valor, ColorDe(cinco[i].Tono), cinco[i].Detalle, cinco[i].ParteRoja,
                    string.IsNullOrEmpty(cinco[i].Ayuda) ? AyudaGeneral : cinco[i].Ayuda + Environment.NewLine + AyudaGeneral);
            }

            foreach (Pagina pagina in paginas.Values)
            {
                LlenarListas(pagina, g);
                // Con otras opciones, el laboratorio entre flechas puede pedir otro ancho (PonerListas).
                if (pagina.Filtro.Flechas != null)
                {
                    AcomodarPagina(pagina);
                }
            }

            // Las columnas que solo existen con una migracion (db/10, db/15 y db/22) ya se saben
            // (son de la lectura): asi una cifra puede ordenar por ellas antes de que su pestana calcule.
            foreach (ITablaDisposicion tabla in Tablas())
            {
                tabla.Columnas(disposicionAmplia);
            }
        }

        /// <summary>
        /// Que la pestana ensene lo de sus filtros: si ya se tiene (de ella, de otra pestana con
        /// los mismos filtros o lo general), al momento; si no, se calcula en segundo plano. Un
        /// calculo que llega tarde (de otra lectura, u otro filtro pedido despues) no se pinta.
        /// Mientras se lee no hace nada: la lectura, al llegar, pone la pestana que se ve.
        /// </summary>
        private void MostrarApartado(Pagina pagina)
        {
            if (!construida || lectura == null || leyendoBase || error != null)
            {
                return;
            }

            AjustarEquipos(pagina);
            FiltroDeApartado f = pagina.Filtro;
            if (f.AlDia)
            {
                // Volvio a lo que ya ensena: lo que aun se calculaba (otros filtros) ya no se pinta.
                if (f.Calculando)
                {
                    f.Pedido++;
                    f.Calculando = false;
                    if (pagina.Cual == PestanaElegida)
                    {
                        esperaCalculo.Stop();
                        PonerResumen();
                    }
                }

                return;
            }

            ClaveDeFiltro clave = f.Clave;
            if (f.Calculando && f.EnCurso.Equals(clave))
            {
                return;
            }

            ResultadoEstadisticas guardado;
            if (calculados.TryGetValue(clave, out guardado))
            {
                PonerApartado(pagina, guardado);
                return;
            }

            int numero = lecturas;
            int pedido = ++f.Pedido;
            f.Calculando = true;
            f.EnCurso = clave;
            f.Error = null;
            if (f.Resultado == null)
            {
                // Sin nada que ensenar mientras calcula: sus tablas dicen "Calculando...".
                VaciarApartado(pagina);
            }

            if (pagina.Cual == PestanaElegida)
            {
                // Si tarda mas de 300 ms, la linea de abajo lo dice.
                esperaCalculo.Stop();
                esperaCalculo.Start();
            }

            LecturaEstadisticas leida = lectura;
            // Con el periodo de esa lectura (el de lo general y el combo), nunca con uno que se pidio
            // despues y no se pudo leer: lo de cada pestana es siempre del mismo periodo que lo demas.
            PeriodoElegido pedidoPeriodo = periodoLeido ?? periodo;
            ResultadoEstadisticas general = resultadoGeneral;
            Task.Run(() => CalcularFiltro(pedidoPeriodo, leida, clave, general))
                .ContinueWith(t => EnLaVentana(() => RecibirApartado(pagina, numero, pedido, clave, t), t));
        }

        /// <summary>
        /// Lo calculado con esos filtros, para no volver a calcularlo con esta lectura. Sin pasar
        /// de MaximoGuardados: se olvida lo mas viejo que ya no ensena ninguna pestana (lo general
        /// se queda: son las cifras y lo de toda pestana en "Todos").
        /// </summary>
        private void Guardar(ClaveDeFiltro clave, ResultadoEstadisticas r)
        {
            if (!calculados.ContainsKey(clave))
            {
                ordenDeGuardados.Add(clave);
            }

            calculados[clave] = r;
            int i = 0;
            while (calculados.Count > MaximoGuardados && i < ordenDeGuardados.Count)
            {
                ClaveDeFiltro vieja = ordenDeGuardados[i];
                ResultadoEstadisticas guardado = calculados[vieja];
                if (vieja.EsGeneral || vieja.Equals(clave) || paginas.Values.Any(p => p.Filtro.Resultado == guardado))
                {
                    i++;
                    continue;
                }

                calculados.Remove(vieja);
                ordenDeGuardados.RemoveAt(i);
            }
        }

        private void RecibirApartado(Pagina pagina, int numero, int pedido, ClaveDeFiltro clave, Task<ResultadoEstadisticas> tarea)
        {
            if (numero != lecturas || IsDisposed)
            {
                // De otra lectura (u otro periodo): ya no vale.
                return;
            }

            FiltroDeApartado f = pagina.Filtro;
            if (!tarea.IsFaulted && !tarea.IsCanceled)
            {
                // Vale para esta lectura aunque la pestana ya pidiera otra cosa: si vuelve a esos filtros, ya esta.
                Guardar(clave, tarea.Result);
                if (pedido == f.Pedido)
                {
                    PonerApartado(pagina, tarea.Result);
                }

                return;
            }

            if (pedido != f.Pedido)
            {
                return;
            }

            f.Calculando = false;
            Exception ex = tarea.Exception == null ? new OperationCanceledException() : tarea.Exception.GetBaseException();
            if (ex is ArgumentException && falloDeLectura == null)
            {
                // La lectura no cubre el periodo (no deberia pasar: se calcula con el de la lectura): se
                // lee. Si la ultima lectura ya fallo, aqui no se vuelve a leer (sin red no acabaria): F5.
                Leer(false);
                return;
            }

            Log.Error("Estadisticas: calcular la pestana " + pagina.Cual, ex);
            f.Resultado = null;
            f.Error = AvisoDeFalloAdmin.Texto("No se pudieron calcular las estadísticas", ex);
            VaciarApartado(pagina);
            if (pagina.Cual == PestanaElegida)
            {
                esperaCalculo.Stop();
                PonerResumen();
                PonerNota();
            }
        }

        /// <summary>
        /// Lo calculado para una pestana, en ella: sus listas (Resolver pudo volver un docente o
        /// un grupo a "Todos"), sus columnas, sus filas y su linea; si es la que se ve, tambien
        /// la linea y la nota de abajo.
        /// </summary>
        private void PonerApartado(Pagina pagina, ResultadoEstadisticas r)
        {
            FiltroDeApartado f = pagina.Filtro;
            // Lo que aun se calculaba para ella (otros filtros) ya no se pinta.
            f.Pedido++;
            f.Resultado = r;
            f.Calculando = false;
            f.Error = null;
            f.Laboratorio = r.Filtro.Laboratorio;
            f.Docente = r.Filtro.Docente;
            f.Grupo = r.Filtro.Grupo;
            Recordar(pagina);
            ElegirEnListas(pagina);

            // Solo las columnas (las de db/10, db/15 y db/22 y los titulos): cada tabla se
            // llena una vez, con lo nuevo (Poner, y las de detalle al elegirse la fila).
            foreach (ITablaDisposicion tabla in pagina.Tablas)
            {
                tabla.Columnas(disposicionAmplia);
            }

            switch (pagina.Cual)
            {
                case PestanaEstadisticas.Estudiantes:
                    tEstudiantes.Poner(r.Estudiantes);
                    PonerLinea(pagina.Cual, r.LineaEstudiantes);
                    break;
                case PestanaEstadisticas.Clases:
                    tClases.Poner(r.Clases);
                    PonerLinea(pagina.Cual, r.LineaClases);
                    break;
                case PestanaEstadisticas.Grupos:
                    tGrupos.Poner(r.Grupos);
                    PonerLinea(pagina.Cual, r.LineaGrupos);
                    break;
                case PestanaEstadisticas.Docentes:
                    tDocentes.Poner(r.Docentes);
                    PonerLinea(pagina.Cual, r.LineaDocentes + " Elige uno para ver sus clases día por día.");
                    AcomodarPagina(pagina);
                    break;
                case PestanaEstadisticas.Laboratorios:
                    tLaboratorios.Poner(r.Laboratorios);
                    PonerLinea(pagina.Cual, r.LineaLaboratorios);
                    break;
                case PestanaEstadisticas.Equipos:
                    tEquipos.Poner(r.Equipos);
                    PonerLinea(pagina.Cual, r.LineaEquipos);
                    break;
                default:
                    PonerGraficas();
                    break;
            }

            if (pagina.Cual == PestanaElegida)
            {
                esperaCalculo.Stop();
                PonerResumen();
                PonerNota();
            }
        }

        /// <summary>
        /// La linea de abajo (lblResumen) con lo de la pestana que se ve: las fechas, la hora de
        /// la lectura y, si lo hay, el aviso de sus filtros (sec. 6.5); o que no se pudo volver a
        /// leer. Mientras esa pestana calcula por primera vez, la de lo general.
        /// </summary>
        private void PonerResumen()
        {
            if (!construida || leyendoBase || error != null || resultadoGeneral == null)
            {
                return;
            }

            FiltroDeApartado f = FiltroDe(PestanaElegida);
            if (f.Error != null)
            {
                lblResumen.Text = "No se pudieron calcular las estadísticas.";
                ayuda.SetToolTip(lblResumen, f.Error);
                return;
            }

            ResultadoEstadisticas r = f.Resultado ?? resultadoGeneral;
            string aviso = f.Resultado == null ? null : f.Resultado.Filtro.Aviso;
            string texto = falloDeLectura == null ? r.LineaResumen : falloDeLectura + (aviso == null ? "" : " " + aviso);
            lblResumen.Text = texto;
            ayuda.SetToolTip(lblResumen, falloDeLectura == null ? texto : ayudaDelFallo);
        }

        private void PonerLinea(PestanaEstadisticas cual, string texto)
        {
            paginas[cual].Linea.Text = texto;
            ayuda.SetToolTip(paginas[cual].Linea, texto);
        }

        private static Color ColorDe(TonoCifra tono)
        {
            switch (tono)
            {
                case TonoCifra.Verde:
                    return CifraGrande.ColorVerde;
                case TonoCifra.Rojo:
                    return CifraGrande.ColorRojo;
                case TonoCifra.Naranja:
                    return CifraGrande.ColorNaranja;
                default:
                    return CifraGrande.ColorGris;
            }
        }

        /// <summary>
        /// La nota naranja de mas prioridad que aplica, con lo de la pestana que se ve (la de
        /// Windows, solo en Equipos y con su laboratorio), la larga si cabe. Mientras esa
        /// pestana calcula, las de lo general (las demas notas son de la lectura).
        /// </summary>
        private void PonerNota()
        {
            ResultadoEstadisticas propio = !construida || leyendoBase ? null : ResultadoDe(PestanaElegida);
            ResultadoEstadisticas r = !construida || leyendoBase ? null : propio ?? resultadoGeneral;
            if (r == null)
            {
                NotaFija = "";
                return;
            }

            bool deEquipos = propio != null && PestanaElegida == PestanaEstadisticas.Equipos;
            NotaEstadisticas nota = r.Notas.FirstOrDefault(n => !n.SoloEnEquipos || deEquipos);
            if (nota == null)
            {
                NotaFija = "";
                return;
            }

            bool cabe = nota.TextoLargo != null && TextRenderer.MeasureText(nota.TextoLargo, fuenteAviso).Width <= lblResumen.Width - 4;
            NotaFija = cabe ? nota.TextoLargo : nota.Texto;
        }

        internal static TimeSpan Desfase(LecturaEstadisticas leida)
        {
            return leida == null ? TimeSpan.Zero : leida.Asistencia.AhoraServidor - leida.Asistencia.LeidoEnRelojLocal;
        }

        /// <summary>
        /// Lo de una tarea en segundo plano, en el hilo de la pantalla. Su excepcion
        /// queda vista siempre: una lectura que llega tarde (o con la ventana ya
        /// cerrada) no deja un fallo sin observar.
        /// </summary>
        private void EnLaVentana(Action accion, Task tarea)
        {
            if (tarea != null && tarea.IsFaulted)
            {
                AggregateException vista = tarea.Exception;
                GC.KeepAlive(vista);
            }

            EnLaVentana(accion);
        }

        private void EnLaVentana(Action accion)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed)
                    {
                        accion();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // Se cerro mientras se leia.
            }
        }

        // =====================================================================
        // Combos y filtros (R2, R6 y sec. 6.4)
        // =====================================================================

        private static PeriodoElegido PeriodoRecordado()
        {
            return ultimoPeriodo ?? PeriodoElegido.De(TipoPeriodo.Ciclo);
        }

        /// <summary>
        /// Lo que queda pedido despues de una lectura (sin pantalla): con fechas, las pedidas
        /// (o las que se ven, con su "Hasta hoy"); con un ciclo pasado, "Este ciclo" si eso se
        /// pidio (entre ciclos se ve el ultimo que termino, y el dia que empiece otro se vera
        /// ese) o el que se ve; con lo demas, "Este ciclo" si se pidio (sin ningun ciclo se
        /// ven los ultimos 30 dias) o el tipo que se ve. Una plantilla aplicada queda si se ve
        /// esa misma; si ya no esta en la lista (o se ve otra cosa), "Este ciclo". Asi, tras un
        /// fallo de lectura, el periodo vuelve a ser el de lo que se ve.
        /// </summary>
        internal static PeriodoElegido PeriodoQueQueda(PeriodoElegido pedido, FiltroEstadisticas resuelto)
        {
            if (resuelto.Tipo == TipoPeriodo.Plantilla && resuelto.Plantilla != null)
            {
                return pedido.Tipo == TipoPeriodo.Plantilla && pedido.Plantilla == resuelto.Plantilla.Id ? pedido : PeriodoElegido.DePlantilla(resuelto.Plantilla);
            }

            if (pedido.Tipo == TipoPeriodo.Plantilla)
            {
                // La plantilla ya no esta (ResolverPeriodo paso a "Este ciclo"): se pide y se recuerda "Este ciclo".
                return PeriodoElegido.De(TipoPeriodo.Ciclo);
            }

            switch (resuelto.Tipo)
            {
                case TipoPeriodo.Fechas:
                    if (pedido.Tipo == TipoPeriodo.Fechas)
                    {
                        return pedido;
                    }

                    return resuelto.HastaHoy ? PeriodoElegido.DesdeHastaHoy(resuelto.Desde) : PeriodoElegido.EntreFechas(resuelto.Desde, resuelto.Hasta);
                case TipoPeriodo.CicloPasado:
                    if (pedido.Tipo == TipoPeriodo.Ciclo
                        || (pedido.Tipo == TipoPeriodo.CicloPasado && resuelto.Ciclo != null && pedido.Desde == resuelto.Ciclo.Desde))
                    {
                        return pedido;
                    }

                    return resuelto.Ciclo != null ? PeriodoElegido.DelCiclo(resuelto.Ciclo) : PeriodoElegido.De(TipoPeriodo.Ciclo);
                default:
                    if (pedido.Tipo == TipoPeriodo.Ciclo || pedido.Tipo == resuelto.Tipo)
                    {
                        return pedido;
                    }

                    return PeriodoElegido.De(resuelto.Tipo);
            }
        }

        /// <summary>
        /// Otra lectura no se pudo hacer y se sigue viendo la de antes: lo pedido (y lo que se
        /// recuerda) vuelve al periodo de ella. Asi LlenarPeriodo y PeriodoQueQueda quedan como
        /// cuando se leyo; sin esto, la regla de "Este ciclo" (que se sigue pidiendo como "Este
        /// ciclo" aunque pase a otro) dejaba pedido el ciclo que fallo junto a lo del mes.
        /// </summary>
        private void VolverAlPeriodoLeido()
        {
            if (periodoLeido == null)
            {
                return;
            }

            periodo = periodoLeido;
            ultimoPeriodo = periodoLeido;
        }

        /// <summary>Los filtros de esa pestana, para la proxima vez que se abra Estadisticas (mientras DigBit siga abierto).</summary>
        private static void Recordar(Pagina pagina)
        {
            filtrosRecordados[pagina.Cual] = pagina.Filtro.Clave;
        }

        private static int IdElegido(ComboBox combo, int siNoHay)
        {
            OpcionCombo opcion = combo.SelectedItem as OpcionCombo;
            return opcion == null ? siNoHay : opcion.Id;
        }

        /// <summary>Antes de la primera lectura: el periodo recordado y, en cada lista, lo que recordaba su pestana.</summary>
        private void PonerListasIniciales()
        {
            poniendoCombos = true;
            try
            {
                LlenarPeriodo(null, null);
                foreach (Pagina pagina in paginas.Values)
                {
                    FiltroDeApartado f = pagina.Filtro;
                    foreach (ListaDeFiltro lista in f.Listas)
                    {
                        int id = f.Id(lista.Tipo);
                        Llenar(lista, new[] { new OpcionCombo { Id = id, Texto = id == 0 ? TextoDeTodos(lista.Tipo) : TextoLeyendo } }, id);
                    }
                }
            }
            finally
            {
                poniendoCombos = false;
            }
        }

        /// <summary>Las opciones de las listas de una pestana: las de lo general (todas las del periodo), con lo que ella pide elegido.</summary>
        private void LlenarListas(Pagina pagina, ResultadoEstadisticas general)
        {
            FiltroDeApartado f = pagina.Filtro;
            bool antes = poniendoCombos;
            poniendoCombos = true;
            try
            {
                foreach (ListaDeFiltro lista in f.Listas)
                {
                    Llenar(lista, OpcionesDe(general, lista.Tipo), f.Id(lista.Tipo));
                    AnchoDeLaLista(lista.Combo);
                }
            }
            finally
            {
                poniendoCombos = antes;
            }
        }

        private static IEnumerable<OpcionCombo> OpcionesDe(ResultadoEstadisticas r, TipoDeFiltro tipo)
        {
            switch (tipo)
            {
                case TipoDeFiltro.Laboratorio:
                    return r.OpcionesLaboratorio;
                case TipoDeFiltro.Docente:
                    return r.OpcionesDocente;
                default:
                    return r.OpcionesGrupo;
            }
        }

        private void Llenar(ListaDeFiltro lista, IEnumerable<OpcionCombo> opciones, int elegido)
        {
            ComboBox combo = lista.Combo;
            combo.BeginUpdate();
            EstiloHorarios.Rellenar(combo, opciones, elegido);
            if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            combo.EndUpdate();
            PonerAyudaDeLista(lista);
            // Items.Clear no avisa a las flechas (sin laboratorios se quedaria el nombre de antes).
            if (lista.Flechas != null)
            {
                lista.Flechas.Refrescar();
            }
        }

        private void PonerAyudaDeLista(ListaDeFiltro lista)
        {
            OpcionCombo opcion = lista.Combo.SelectedItem as OpcionCombo;
            ayuda.SetToolTip(lista.Combo, opcion == null ? null : opcion.Texto);
        }

        /// <summary>En cada lista de la pestana, lo que ella pide (sin calcular).</summary>
        private void ElegirEnListas(Pagina pagina)
        {
            FiltroDeApartado f = pagina.Filtro;
            bool antes = poniendoCombos;
            poniendoCombos = true;
            try
            {
                foreach (ListaDeFiltro lista in f.Listas)
                {
                    ElegirEnCombo(lista.Combo, f.Id(lista.Tipo));
                    PonerAyudaDeLista(lista);
                }
            }
            finally
            {
                poniendoCombos = antes;
            }
        }

        /// <summary>
        /// Lo que pide una pestana (solo los filtros que tiene; los demas, "Todos"), en sus
        /// listas y en lo que recuerda. No calcula: eso, al verse (MostrarApartado).
        /// </summary>
        private void PonerFiltro(Pagina pagina, int laboratorio, int docente, int grupo)
        {
            FiltroDeApartado f = pagina.Filtro;
            f.Laboratorio = f.Tiene(TipoDeFiltro.Laboratorio) ? laboratorio : 0;
            f.Docente = f.Tiene(TipoDeFiltro.Docente) ? docente : 0;
            f.Grupo = f.Tiene(TipoDeFiltro.Grupo) ? grupo : 0;
            Recordar(pagina);
            ElegirEnListas(pagina);
        }

        /// <summary>
        /// Las opciones del periodo (sec. 1.1): "Este ciclo" (si hoy vale alguna clase), un
        /// renglon por cada ciclo que ya termino, "Este mes", "Esta semana", "Ultimos 30
        /// dias", las fechas elegidas (si las hay) y "Elegir fechas..." al final, que siempre
        /// abre el dialogo. Elige lo que se ve (r; sin el, lo pedido) y, con r, pone la ayuda.
        /// </summary>
        private void LlenarPeriodo(LecturaEstadisticas leida, ResultadoEstadisticas r)
        {
            // La misma pieza que la ficha del docente (SelectorDePeriodo.Llenar).
            periodoAntes = SelectorDePeriodo.Llenar(cboPeriodo, leida, r, periodo);
            if (r != null)
            {
                ayuda.SetToolTip(cboPeriodo, EstadisticasDatos.AyudaDelPeriodo(r, leida));
            }
        }

        private void PeriodoCambiado()
        {
            OpcionPeriodo opcion = cboPeriodo.SelectedItem as OpcionPeriodo;
            if (opcion == null)
            {
                return;
            }

            if (opcion.ElegirFechas)
            {
                ElegirFechas();
                return;
            }

            periodoAntes = cboPeriodo.SelectedIndex;
            periodo = opcion.Periodo;
            ultimoPeriodo = periodo;
            Leer(false);
        }

        /// <summary>"Elegir fechas...": el dialogo (con "Hasta hoy"); con Cancelar vuelve la opcion de antes.</summary>
        private void ElegirFechas()
        {
            PeriodoElegido elegido = SelectorDePeriodo.PedirFechas(this, lectura, resultadoGeneral, periodo, Desfase(lectura));
            if (elegido == null)
            {
                poniendoCombos = true;
                cboPeriodo.SelectedIndex = periodoAntes >= 0 && periodoAntes < cboPeriodo.Items.Count ? periodoAntes : 0;
                poniendoCombos = false;
                return;
            }

            periodo = elegido;
            ultimoPeriodo = periodo;
            poniendoCombos = true;
            try
            {
                // El combo ya dice las fechas mientras lee.
                LlenarPeriodo(lectura, null);
            }
            finally
            {
                poniendoCombos = false;
            }

            Leer(false);
        }

        /// <summary>
        /// Otra opcion en una lista de esa pestana (con el raton, el teclado o sus flechas): solo
        /// ella vuelve a calcular, sin ir a la base y en segundo plano; las demas no cambian.
        /// </summary>
        private void ListaCambiada(Pagina pagina)
        {
            FiltroDeApartado f = pagina.Filtro;
            foreach (ListaDeFiltro lista in f.Listas)
            {
                f.Poner(lista.Tipo, IdElegido(lista.Combo, f.Id(lista.Tipo)));
                PonerAyudaDeLista(lista);
            }

            Recordar(pagina);
            if (lectura == null)
            {
                // Sin nada leido: mientras lee, la lectura calcula la que se ve al llegar; si no se pudo leer, se vuelve a intentar.
                if (!leyendoBase)
                {
                    Leer(false);
                }

                return;
            }

            MostrarApartado(pagina);
        }

        private void ElegirEnCombo(ComboBox combo, int id)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                OpcionCombo opcion = combo.Items[i] as OpcionCombo;
                if (opcion != null && opcion.Id == id)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        // =====================================================================
        // Cifras, pestanas y dobles clics
        // =====================================================================

        /// <summary>
        /// Cada cifra elige su pestana y su orden (sec. 3.1), con los filtros de esa pestana en
        /// "Todos" para que coincida con la cifra (es de todo el TESCHI); Equipos conserva su
        /// laboratorio (sus equipos son de uno).
        /// </summary>
        private void CifraPulsada(int cual)
        {
            PestanaEstadisticas pestana = cual == 0 ? PestanaEstadisticas.Estudiantes
                : cual == 1 ? PestanaEstadisticas.Docentes
                : cual == 2 ? PestanaEstadisticas.Laboratorios
                : PestanaEstadisticas.Equipos;
            Pagina pagina = paginas[pestana];
            if (pestana != PestanaEstadisticas.Equipos)
            {
                PonerFiltro(pagina, 0, 0, 0);
            }

            ElegirPestana(pestana);
            // Si ya se veia, ElegirPestana no avisa: que ensene lo de sus filtros nuevos.
            MostrarApartado(pagina);
            switch (cual)
            {
                case 0:
                    tEstudiantes.OrdenPorOmision();
                    tEstudiantes.Grid.Focus();
                    break;
                case 1:
                    tDocentes.OrdenPorOmision();
                    tDocentes.Grid.Focus();
                    break;
                case 2:
                    tLaboratorios.Ordenar("bitacoras", true);
                    tLaboratorios.Grid.Focus();
                    break;
                case 3:
                    tEquipos.Ordenar("fallas", true);
                    tEquipos.Grid.Focus();
                    break;
                default:
                    if (Con(PestanaEstadisticas.Equipos, r => r.ConAvisos))
                    {
                        tEquipos.Ordenar("avisos", true);
                    }
                    else
                    {
                        tEquipos.OrdenPorOmision();
                    }

                    tEquipos.Grid.Focus();
                    break;
            }
        }

        private void PestanaCambiada()
        {
            if (!construida)
            {
                return;
            }

            // Lo de sus filtros (de lo ya calculado o en segundo plano); Equipos con "Todos"
            // pasa al primer laboratorio (AjustarEquipos).
            Pagina pagina = paginas[PestanaElegida];
            MostrarApartado(pagina);
            if (pagina.Filtro.Flechas != null)
            {
                pagina.Filtro.Flechas.Refrescar();
            }

            // Al elegir una pestana con el raton (si las pestanas no tenian el foco) o desde el
            // codigo (ElegirPestana), WinForms pone el foco en su primer control, que ahora es una
            // lista: ahi una tecla cambiaria el filtro sin querer y ← y → elegirian en ella. Va a
            // su Buscar, como antes, o, sin Buscar (Graficas), a las pestanas, para que ← y →
            // muevan su laboratorio.
            if (pagina.Filtro.Listas.Any(l => l.Combo.Focused))
            {
                if (pagina.Buscador != null)
                {
                    pagina.Buscador.Focus();
                }
                else
                {
                    tabs.Focus();
                }
            }

            // Si aun calcula (se pidio en otra visita), la linea de abajo lo dira si tarda.
            if (!pagina.Filtro.Calculando)
            {
                esperaCalculo.Stop();
            }
            else if (!esperaCalculo.Enabled)
            {
                esperaCalculo.Start();
            }

            PonerResumen();
            ActualizarCsv();
            PonerNota();
            tabs.Invalidate();
        }

        /// <summary>
        /// Los equipos son de un laboratorio ("PC-07" hay en todos): Equipos con "Todos los
        /// laboratorios" pasa al primero; las flechas llevan a los demas.
        /// </summary>
        private void AjustarEquipos(Pagina pagina)
        {
            FiltroDeApartado f = pagina.Filtro;
            if (pagina.Cual != PestanaEstadisticas.Equipos || f.Laboratorio != 0 || f.Listas.Count == 0)
            {
                return;
            }

            ComboBox combo = f.Listas[0].Combo;
            OpcionCombo primero = combo.Enabled && combo.Items.Count > 1 ? combo.Items[1] as OpcionCombo : null;
            if (primero == null || primero.Id == 0)
            {
                return;
            }

            f.Laboratorio = primero.Id;
            Recordar(pagina);
            ElegirEnListas(pagina);
        }

        /// <summary>
        /// Doble clic en un grupo (Grupos) o en un docente (Docentes): Clases con ese grupo o
        /// ese docente y con los demas filtros con que se conto la fila (el laboratorio de la
        /// pestana de donde viene y, si lo tiene, su docente o su grupo), y pasa a Clases.
        /// </summary>
        private void IrAClasesCon(Pagina origen, int docente, int grupo)
        {
            FiltroDeApartado de = origen.Filtro;
            FiltroEstadisticas visto = de.Resultado == null ? null : de.Resultado.Filtro;
            int laboratorio = visto != null ? visto.Laboratorio : de.Laboratorio;
            int suDocente = visto != null ? visto.Docente : de.Docente;
            int suGrupo = visto != null ? visto.Grupo : de.Grupo;
            Pagina clases = paginas[PestanaEstadisticas.Clases];
            PonerFiltro(clases, laboratorio, docente != 0 ? docente : suDocente, grupo != 0 ? grupo : suGrupo);
            ElegirPestana(PestanaEstadisticas.Clases);
            MostrarApartado(clases);
        }

        /// <summary>Doble clic en un laboratorio: Equipos con ese laboratorio ("Todos" pasa al primero) y pasa a Equipos.</summary>
        private void IrAEquiposDe(int laboratorio)
        {
            Pagina equipos = paginas[PestanaEstadisticas.Equipos];
            PonerFiltro(equipos, laboratorio, 0, 0);
            ElegirPestana(PestanaEstadisticas.Equipos);
            MostrarApartado(equipos);
        }

        // =====================================================================
        // Detalles
        // =====================================================================

        private void MostrarEstudiante(FilaEstudiante f)
        {
            if (f == null)
            {
                fichaEstudiante.ConDatos = false;
                fichaEstudiante.Vacio = leyendoBase || ResultadoDe(PestanaEstadisticas.Estudiantes) == null ? "" : "Elige un estudiante para ver sus clases.";
                tSusClases.Vaciar();
                return;
            }

            lblNombreEstudiante.Text = f.Nombre + (f.Activo ? "" : " (de baja)");
            ayuda.SetToolTip(lblNombreEstudiante, lblNombreEstudiante.Text);
            string grupo = f.Grupo.Length > 0 ? f.Grupo : "Sin grupo en su cuenta";
            string ultima = f.UltimaBitacora.HasValue ? EstadisticasDatos.Fecha(f.UltimaBitacora.Value) : EstadisticasDatos.SinDato;
            rMatricula.Texto = f.Matricula;
            rGrupo.Texto = grupo;
            rBitacoras.Texto = EstadisticasDatos.Miles(f.Bitacoras);
            rUltima.Texto = ultima;
            lblDatosEstudiante.Text = "Matrícula " + f.Matricula + ". Grupo: " + (f.Grupo.Length > 0 ? f.Grupo : "sin grupo en su cuenta") + ". "
                + EstadisticasDatos.Contar(f.Bitacoras, "bitácora", "bitácoras") + " en el periodo" + (f.UltimaBitacora.HasValue ? "; la última, el " + ultima : "") + ".";
            lblNotasEstudiante.Text = string.Join("\n", f.Notas);
            tSusClases.Poner(f.SusClases);
            btnVerBitacoras.Enabled = !leyendoAlumno;
            fichaEstudiante.ConDatos = true;
            AcomodarFichaEstudiante();
        }

        private void MostrarClase(FilaClase c)
        {
            if (c == null)
            {
                fichaClase.ConDatos = false;
                fichaClase.Vacio = leyendoBase || ResultadoDe(PestanaEstadisticas.Clases) == null ? "" : "Elige una clase para ver a sus estudiantes.";
                tLista.Vaciar();
                return;
            }

            rMateria.Texto = c.Materia;
            rGrupoClase.Texto = c.Grupo;
            rDocente.Texto = c.Docente;
            rLaboratorio.Texto = Guion(c.Laboratorios);
            lblDatosClase.Text = c.Materia + " (" + c.Grupo + "). Docente: " + c.Docente + ". Laboratorio: " + Guion(c.Laboratorios) + ".";
            lblLineaLista.Text = c.LineaDeLaLista;
            ayuda.SetToolTip(lblLineaLista, c.LineaDeLaLista);
            numeroEnLista = new Dictionary<ParDeAsistencia, int>();
            for (int i = 0; i < c.Lista.Count; i++)
            {
                numeroEnLista[c.Lista[i]] = i + 1;
            }

            tLista.Poner(c.Lista);
            fichaClase.ConDatos = true;
            AcomodarFichaClase();
        }

        private void MostrarDocente(FilaDocente d)
        {
            if (d == null)
            {
                fichaDocente.ConDatos = false;
                fichaDocente.Vacio = leyendoBase || ResultadoDe(PestanaEstadisticas.Docentes) == null ? "" : "Elige a un docente para ver sus clases día por día.";
                tSusClasesDia.Vaciar();
                return;
            }

            lblNombreDocente.Text = d.Docente;
            ayuda.SetToolTip(lblNombreDocente, d.Docente);
            List<string> partes = new List<string>();
            partes.Add(d.QueContaban == 0 ? "Ninguna de sus clases contaba en el periodo"
                : "Se dieron " + EstadisticasDatos.Miles(d.SeDieron) + " de " + EstadisticasDatos.Contar(d.QueContaban, "clase", "clases"));
            if (d.QueContaban > 0)
            {
                partes.Add(EstadisticasDatos.Miles(d.SinRegistro) + " sin registro");
            }

            if (d.Canceladas > 0)
            {
                partes.Add(EstadisticasDatos.Contar(d.Canceladas, "cancelada", "canceladas"));
            }

            string resumen = string.Join("; ", partes) + ". Doble clic en una clase para ver su bitácora.";
            lblResumenDocente.Text = resumen;
            ayuda.SetToolTip(lblResumenDocente, resumen);
            tSusClasesDia.Poner(d.SusClasesDia);
            fichaDocente.ConDatos = true;
            AcomodarFichaDocente();
        }

        // =====================================================================
        // Ver sus bitacoras y la bitacora de una clase
        // =====================================================================

        /// <summary>
        /// "Bitacoras de ...": lee primero, en segundo plano, el estudiante entero
        /// (AlumnosDatos.Obtener, el mismo SELECT que Estudiantes): su "Exportar sus
        /// datos" escribe el perfil con lo que recibe.
        /// </summary>
        private void VerSusBitacoras(FilaEstudiante fila)
        {
            if (fila == null || leyendoAlumno)
            {
                return;
            }

            int id = fila.Id;
            leyendoAlumno = true;
            btnVerBitacoras.Enabled = false;
            Cursor = Cursors.AppStarting;
            Task.Run(() => new AlumnosDatos().Obtener(id)).ContinueWith(t => EnLaVentana(() =>
            {
                leyendoAlumno = false;
                btnVerBitacoras.Enabled = true;
                Cursor = Cursors.Default;
                if (t.IsFaulted)
                {
                    AvisoDeFalloAdmin.Mostrar(this, "Leer los datos del estudiante id " + id, t.Exception.GetBaseException(), "No se pudieron leer los datos del estudiante", Text);
                    return;
                }

                AlumnoInfo alumno = t.Result;
                if (alumno == null)
                {
                    MessageBox.Show(this, "Ese estudiante ya no está en DigBit.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (VentanaHistorialAlumno historial = new VentanaHistorialAlumno(alumno))
                {
                    historial.ShowDialog(this);
                }
            }, t));
        }

        /// <summary>Doble clic en una clase de un docente: su bitacora, como la abren Alertas y Faltas (BitacoraSesion).</summary>
        private void AbrirBitacora(ClaseDiaDocente dia)
        {
            if (dia == null || lectura == null)
            {
                return;
            }

            ClaseDelCalendario abrir = SesionDelDia(dia);
            if (abrir == null)
            {
                // "En curso" con la regla de E2 (EstadisticasDatos.ClaseDia): de hoy y ya empezada.
                bool enCurso = dia.Estado == EstadoEnCalendario.PorVenir && dia.Fecha == lectura.Hoy && dia.Inicio <= lectura.Calendario.Ahora.TimeOfDay;
                Avisar(dia.Cancelada ? "Esa clase se canceló: no tiene bitácora."
                    : !dia.SesionId.HasValue && enCurso ? "Esa clase sigue en curso: todavía nadie ha registrado su bitácora."
                    : !dia.SesionId.HasValue && dia.Estado == EstadoEnCalendario.PorVenir ? "Esa clase aún no pasa: todavía nadie ha registrado su bitácora."
                    : !dia.SesionId.HasValue ? "Esa clase no tiene bitácora: nadie abrió su sesión."
                    : "Su sesión no está en el horario: búscala en la sección Bitácoras.");
                return;
            }

            string horario = HorariosDatos.NombresDia[((int)abrir.Fecha.DayOfWeek + 6) % 7 + 1] + " " + EstadisticasDatos.Fecha(abrir.Fecha) + ", "
                + HorariosDatos.Hora(abrir.Inicio) + " a " + HorariosDatos.Hora(abrir.Fin) + ", " + Guion(dia.Laboratorio)
                + (abrir.EsExtra ? " (sesión extra)" : "");
            using (BitacoraSesion bitacora = new BitacoraSesion(abrir.EsExtra ? (int?)null : abrir.FranjaId, abrir.EsExtra ? abrir.ExcepcionId : null,
                abrir.Materia + ", " + abrir.Grupo, horario, abrir.Profesor, abrir.Fecha))
            {
                bitacora.ShowDialog(this);
            }
        }

        /// <summary>
        /// La clase del calendario cuya bitacora abre el doble clic en esa clase-dia
        /// (ClaseDiaDocente no trae la franja ni la excepcion), o null. Se busca por
        /// clase, fecha, horas, extra y laboratorio en un diccionario de la lectura que se ve.
        /// </summary>
        private ClaseDelCalendario SesionDelDia(ClaseDiaDocente dia)
        {
            if (lectura == null)
            {
                return null;
            }

            if (clasesDelDiaDe != lectura)
            {
                clasesDelDia = new Dictionary<string, ClaseDelCalendario>(StringComparer.Ordinal);
                foreach (ClaseDelCalendario c in lectura.Calendario.Clases)
                {
                    string clave = ClaveDelDia(c.ClaseId, c.Fecha, c.Inicio, c.Fin, c.EsExtra, c.LaboratorioId);
                    if (!clasesDelDia.ContainsKey(clave))
                    {
                        clasesDelDia[clave] = c;
                    }
                }

                clasesDelDiaDe = lectura;
            }

            ClaseDelCalendario clase;
            return clasesDelDia.TryGetValue(ClaveDelDia(dia.ClaseId, dia.Fecha, dia.Inicio, dia.Fin, dia.EsExtra, dia.LaboratorioId), out clase)
                ? SesionQueAbrir(clase) : null;
        }

        private static string ClaveDelDia(int claseId, DateTime fecha, TimeSpan inicio, TimeSpan fin, bool extra, int laboratorio)
        {
            return claseId.ToString(Inv) + "|" + fecha.Ticks.ToString(Inv) + "|" + inicio.Ticks.ToString(Inv) + "|" + fin.Ticks.ToString(Inv)
                + "|" + (extra ? "x" : "f") + "|" + laboratorio.ToString(Inv);
        }

        /// <summary>
        /// La clase cuya sesion se abre: la suya; o, si sus estudiantes quedaron en otra
        /// (BitacorasEnOtra), esa. Null si no hay una que BitacoraSesion sepa abrir (por
        /// franja y fecha, o por extra). Es la regla de Faltas (PestanaCalendario.SesionQueAbrir).
        /// </summary>
        private static ClaseDelCalendario SesionQueAbrir(ClaseDelCalendario clase)
        {
            if (clase.SesionId.HasValue && !clase.SesionSinEnlace && (clase.Bitacoras > 0 || clase.OtraClase == null))
            {
                return clase;
            }

            ClaseDelCalendario otra = clase.OtraClase;
            return otra != null && otra.SesionId.HasValue && !otra.SesionSinEnlace ? otra : null;
        }

        // =====================================================================
        // Graficas y reporte para auditoria (2026-10-31)
        // =====================================================================

        private RadioButton OpcionDeAgrupacion(string texto, AgrupacionGraficas cual)
        {
            RadioButton opcion = new RadioButton
            {
                AutoSize = true,
                Text = texto,
                Font = fuenteValor,
                ForeColor = EstiloAdmin.TextoOscuro,
                BackColor = Color.White,
                UseMnemonic = false
            };
            opcion.CheckedChanged += (s, e) =>
            {
                if (opcion.Checked && !poniendoAgrupacion)
                {
                    agrupacionElegida = cual;
                    PonerGraficas();
                }
            };
            // ← → ↑ ↓ eligen la otra opcion, como un grupo de opciones de Windows: no salen a las listas
            // ni al reporte (ahi una flecha mas cambiaria el laboratorio o el grupo sin querer).
            opcion.PreviewKeyDown += (s, e) =>
            {
                if (EsFlecha(e.KeyData))
                {
                    e.IsInputKey = true;
                }
            };
            opcion.KeyDown += (s, e) =>
            {
                if (EsFlecha(e.KeyData))
                {
                    e.Handled = true;
                    RadioButton otra = opcion == rbPorSemana ? rbPorMes : rbPorSemana;
                    otra.Checked = true;
                    otra.Focus();
                }
            };
            return opcion;
        }

        /// <summary>Una flecha sola (sin Ctrl, Alt ni Mayus).</summary>
        private static bool EsFlecha(Keys tecla)
        {
            return tecla == Keys.Left || tecla == Keys.Right || tecla == Keys.Up || tecla == Keys.Down;
        }

        /// <summary>La que eligio (mientras DigBit siga abierto) o, si no eligio, por semana hasta 16 semanas y despues por mes.</summary>
        private AgrupacionGraficas AgrupacionVista
        {
            get
            {
                ResultadoEstadisticas r = ResultadoDe(PestanaEstadisticas.Graficas);
                return agrupacionElegida ?? (r != null ? r.Graficas.Sugerida : AgrupacionGraficas.Semana);
            }
        }

        /// <summary>Las cuatro secciones con lo calculado para Graficas con sus filtros (sin base): en pantalla, con lo de cada docente.</summary>
        private void PonerGraficas()
        {
            ResultadoEstadisticas resultado = ResultadoDe(PestanaEstadisticas.Graficas);
            if (resultado == null || leyendoBase)
            {
                panelGraficas.Avisar(TextoSinFilas(PestanaEstadisticas.Graficas, ""), SinFilasPorError(PestanaEstadisticas.Graficas));
                return;
            }

            AgrupacionGraficas agrupacion = AgrupacionVista;
            poniendoAgrupacion = true;
            try
            {
                rbPorSemana.Checked = agrupacion == AgrupacionGraficas.Semana;
                rbPorMes.Checked = agrupacion == AgrupacionGraficas.Mes;
            }
            finally
            {
                poniendoAgrupacion = false;
            }

            // "Ciclo ... , del ... al ...", "Este ciclo (en curso), del ..." o "Del ... al ...": los dias que cuentan.
            PonerLinea(PestanaEstadisticas.Graficas, EstadisticasDatos.CabezaDelPeriodo(resultado)
                + (agrupacion == AgrupacionGraficas.Mes ? ", por mes" : ", por semana")
                + ": uso de los laboratorios, clases y docentes, asistencia y fallas, con los filtros de arriba.");
            if (resultado.TextoVacio != null)
            {
                panelGraficas.Avisar(resultado.TextoVacio, false);
                return;
            }

            panelGraficas.Poner(ContenidoGraficas.Armar(resultado, agrupacion, true));
        }

        /// <summary>
        /// "Reporte para auditoria (PDF)...": lo de Graficas con el encabezado del TESCHI,
        /// en el visor de DigBit (de ahi se guarda). La persona responsable del
        /// laboratorio lo genera sin lo de cada docente (EstadisticasDatos.ReporteConDocentes,
        /// la regla del CSV de Docentes). En kiosco no hay boton.
        /// </summary>
        private void ReporteParaAuditoria()
        {
            if (Kiosco.Activo || btnReporte == null)
            {
                return;
            }

            // Con los filtros de Graficas: mientras calcula, lo que se ve aun es lo de los de antes.
            FiltroDeApartado filtro = FiltroDe(PestanaEstadisticas.Graficas);
            if (leyendoBase || filtro.Calculando)
            {
                Avisar(leyendoBase ? "Espera a que se lean las estadísticas para el reporte." : "Espera a que se calculen las estadísticas para el reporte.");
                return;
            }

            ResultadoEstadisticas resultado = filtro.Resultado;
            if (resultado == null || lectura == null)
            {
                Avisar("Aún no hay estadísticas para el reporte: pulsa Actualizar.");
                return;
            }

            DatosDelReporte datos = DatosParaElReporte();
            byte[] pdf;
            Cursor = Cursors.WaitCursor;
            try
            {
                pdf = ReporteAuditoriaPdf.Crear(resultado, datos);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MostrarError("Estadisticas: reporte para auditoria", ex, "No se pudo preparar el reporte para auditoría");
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            Log.Info("Estadisticas: reporte para auditoria del " + datos.Desde.ToString("yyyy-MM-dd", Inv) + " al " + datos.Hasta.ToString("yyyy-MM-dd", Inv)
                + " (" + pdf.Length + " bytes" + (datos.ConDocentes ? "" : ", sin lo de cada docente") + ").");
            try
            {
                VisorPdf.Mostrar(this, pdf, ReporteAuditoriaPdf.NombreArchivo(datos, resultado.Filtro.Laboratorio != 0));
            }
            catch (Exception ex)
            {
                // Un fallo de archivo aqui es el temporal de la vista previa, no uno que se eligio.
                AvisoDeFalloAdmin.MostrarVistaPrevia(this, "Estadisticas: reporte para auditoria (visor)", ex, "No se pudo mostrar el reporte para auditoría", Text);
            }
        }

        /// <summary>
        /// El periodo (su nombre y los dias que de verdad cuentan, de lo calculado: no del texto
        /// del combo), los filtros de Graficas (como el membrete del CSV), quien y cuando, para
        /// la ficha del reporte.
        /// </summary>
        internal DatosDelReporte DatosParaElReporte()
        {
            ResultadoEstadisticas r = ResultadoDe(PestanaEstadisticas.Graficas);
            FiltroEstadisticas f = r.Filtro;
            return new DatosDelReporte
            {
                Periodo = EstadisticasDatos.NombreDelPeriodo(r),
                NombreCorto = EstadisticasDatos.NombreCortoDelPeriodo(r),
                NotaDelPeriodo = EstadisticasDatos.NotaDelPeriodoParaElReporte(r),
                Desde = r.Desde,
                Hasta = r.Hasta,
                Laboratorio = f.Laboratorio == 0 ? "Todos los laboratorios" : NombreDeLaboratorio(f.Laboratorio, r),
                Docente = f.Docente == 0 ? "Todo el personal docente" : TextoDeOpcion(r.OpcionesDocente, f.Docente),
                Grupo = f.Grupo == 0 ? "Todos los grupos" : TextoDeOpcion(r.OpcionesGrupo, f.Grupo),
                Agrupacion = AgrupacionVista,
                GeneradoPor = (Datos_User.getUser() ?? "-") + (Permisos.SoloConsulta ? " (responsable de laboratorio, solo consulta)" : " (administración de DigBit)"),
                GeneradoEn = DateTime.Now + Desfase(lectura),
                LeidoEn = lectura.AhoraServidor,
                ConDocentes = EstadisticasDatos.ReporteConDocentes(Permisos.SoloConsulta),
                // Como arriba de la ventana: con un filtro en Graficas, las cifras grandes son las generales.
                Generales = resultadoGeneral != null && (f.Laboratorio != 0 || f.Docente != 0 || f.Grupo != 0) ? resultadoGeneral.Cifras : null
            };
        }

        // =====================================================================
        // Guardar en Excel (CSV) (sec. 8)
        // =====================================================================

        private void ActualizarCsv()
        {
            if (btnCsv == null)
            {
                return;
            }

            bool puede = EstadisticasDatos.SeGuardaEnCsv(PestanaElegida, Permisos.SoloConsulta);
            btnCsv.Enabled = puede;
            motivoSinCsv = puede ? null : EstadisticasDatos.SinCsvParaSoloConsulta;
            btnCsv.AccessibleDescription = puede ? AyudaCsv : motivoSinCsv;
            if (puede)
            {
                EsconderAyudaDelCsv();
            }
        }

        private void AyudaDelCsvApagado(Point raton)
        {
            bool encima = btnCsv != null && !btnCsv.Enabled && motivoSinCsv != null && btnCsv.Visible && btnCsv.Bounds.Contains(raton);
            if (encima && !ayudaCsvEnsenada)
            {
                ayuda.Show(motivoSinCsv, this, raton.X + 12, raton.Y + 20, 6000);
                ayudaCsvEnsenada = true;
            }
            else if (!encima && ayudaCsvEnsenada)
            {
                EsconderAyudaDelCsv();
            }
        }

        private void EsconderAyudaDelCsv()
        {
            if (ayudaCsvEnsenada)
            {
                ayuda.Hide(this);
                ayudaCsvEnsenada = false;
            }
        }

        /// <summary>
        /// "Guardar en Excel (CSV)...": la pestana que se ve, con todas sus columnas y
        /// las filas que deja la busqueda, en UTF-8 con BOM. En kiosco no existe; la
        /// persona responsable solo guarda Laboratorios, Grupos y Equipos.
        /// </summary>
        private void GuardarCsv()
        {
            PestanaEstadisticas pestana = PestanaElegida;
            if (Kiosco.Activo || btnCsv == null || !EstadisticasDatos.SeGuardaEnCsv(pestana, Permisos.SoloConsulta))
            {
                return;
            }

            // Mientras lee, las tablas estan vacias; mientras esa pestana calcula, aun son las de sus filtros de antes.
            if (leyendoBase || FiltroDe(pestana).Calculando)
            {
                Avisar(leyendoBase ? "Espera a que se lean las estadísticas para guardarlas." : "Espera a que se calculen las estadísticas para guardarlas.");
                return;
            }

            string texto = ComoCsv(pestana);
            if (texto == null)
            {
                Avisar("Aún no hay estadísticas que guardar: pulsa Actualizar.");
                return;
            }

            string ruta;
            using (SaveFileDialog dialogo = new SaveFileDialog
            {
                Title = "Guardar las estadísticas para Excel",
                Filter = "Archivo CSV para Excel (*.csv)|*.csv",
                DefaultExt = "csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = NombreDelCsv(pestana),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                ruta = dialogo.FileName;
            }

            try
            {
                File.WriteAllText(ruta, texto, new UTF8Encoding(true));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                || ex is NotSupportedException || ex is ArgumentException)
            {
                Log.Error("Estadisticas: guardar en CSV la pestana " + pestana, ex);
                MessageBox.Show(this, VisorPdf.ExplicarArchivo("No se pudo guardar el archivo", ex, "Excel"), TituloCsv, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Guardar las estadisticas en CSV", ex, "No se pudo guardar el archivo", Text);
                return;
            }

            Log.Info("Estadisticas: pestana " + pestana + " guardada en CSV.");
            Avisar("Guardado: ábrelo con Excel.");
        }

        /// <summary>"Estadisticas Clases 2026-09-30 al 2027-02-28.csv", con los dias que cuentan; sin nada calculado, con la fecha del servidor.</summary>
        internal string NombreDelCsv(PestanaEstadisticas pestana)
        {
            ResultadoEstadisticas r = ResultadoDe(pestana);
            if (r == null)
            {
                return "Estadisticas " + NombreDePestana(pestana) + " " + (DateTime.Now + Desfase(lectura)).ToString("yyyy-MM-dd", Inv) + ".csv";
            }

            return "Estadisticas " + NombreDePestana(pestana) + " " + r.Desde.ToString("yyyy-MM-dd", Inv) + " al " + r.Hasta.ToString("yyyy-MM-dd", Inv) + ".csv";
        }

        private static string NombreDePestana(PestanaEstadisticas pestana)
        {
            switch (pestana)
            {
                case PestanaEstadisticas.Estudiantes:
                    return "Estudiantes";
                case PestanaEstadisticas.Clases:
                    return "Clases";
                case PestanaEstadisticas.Grupos:
                    return "Grupos";
                case PestanaEstadisticas.Docentes:
                    return "Docentes";
                case PestanaEstadisticas.Laboratorios:
                    return "Laboratorios";
                case PestanaEstadisticas.Graficas:
                    return "Graficas";
                default:
                    return "Equipos";
            }
        }

        /// <summary>
        /// El texto del CSV de esa pestana, o null si aun no hay nada calculado para ella:
        /// el membrete (sec. 8) con SUS filtros, una fila vacia y la tabla con todas sus
        /// columnas (las "X de N" en dos columnas, numeros sin separador de miles y "-"
        /// como celda vacia). Clases va en formato largo (una fila por clase y estudiante);
        /// Docentes lleva al final sus clases dia por dia.
        /// </summary>
        internal string ComoCsv(PestanaEstadisticas pestana)
        {
            // Mientras lee, las tablas estan vacias (lo calculado aun es lo de antes).
            ResultadoEstadisticas r = ResultadoDe(pestana);
            if (r == null || lectura == null || leyendoBase)
            {
                return null;
            }

            string separador = HojaAsistencia.SeparadorDeListas();
            StringBuilder csv = new StringBuilder();
            Action<IEnumerable<string>> renglon = campos => csv.Append(string.Join(separador, campos.Select(c => HojaAsistencia.CampoCsv(c, separador)))).Append("\r\n");
            FiltroEstadisticas f = r.Filtro;
            // Los dias que de verdad cuentan (hasta hoy y con 400 como mucho).
            DateTime desde = r.Desde;
            DateTime hasta = r.Hasta;

            renglon(new[] { "Estadísticas de DigBit" });
            renglon(new[] { "Pestaña: " + NombreDePestana(pestana) });
            renglon(new[] { "Periodo: " + EstadisticasDatos.NombreDelPeriodo(r) + " (del " + EstadisticasDatos.Fecha(desde) + " al " + EstadisticasDatos.Fecha(hasta) + ")" });
            if (r.CicloCerrado)
            {
                renglon(new[] { "Nota: " + EstadisticasDatos.NotaDeCicloCerrado });
            }

            renglon(new[] { "Laboratorio: " + (f.Laboratorio == 0 ? "Todos los laboratorios" : NombreDeLaboratorio(f.Laboratorio, r)) });
            renglon(new[] { "Docente: " + (f.Docente == 0 ? "todo el personal docente" : TextoDeOpcion(r.OpcionesDocente, f.Docente)) });
            renglon(new[] { "Grupo: " + (f.Grupo == 0 ? "Todos los grupos" : TextoDeOpcion(r.OpcionesGrupo, f.Grupo)) });
            renglon(new[] { "Leído: " + lectura.AhoraServidor.ToString("dd/MM/yyyy HH:mm", Inv) + " (hora del servidor)" });
            Pagina pagina = paginas[pestana];
            if (pagina.Buscador != null && pagina.Buscador.Text.Trim().Length > 0)
            {
                renglon(new[] { "Búsqueda: " + pagina.Buscador.Text.Trim() });
            }

            csv.Append("\r\n");
            switch (pestana)
            {
                case PestanaEstadisticas.Estudiantes:
                    CsvEstudiantes(r, renglon);
                    break;
                case PestanaEstadisticas.Clases:
                    CsvClases(r, renglon);
                    break;
                case PestanaEstadisticas.Grupos:
                    CsvGrupos(renglon);
                    break;
                case PestanaEstadisticas.Docentes:
                    CsvDocentes(r, renglon, csv);
                    break;
                case PestanaEstadisticas.Laboratorios:
                    CsvLaboratorios(r, renglon);
                    break;
                case PestanaEstadisticas.Graficas:
                    CsvGraficas(r, renglon, csv);
                    break;
                default:
                    CsvEquipos(r, renglon);
                    break;
            }

            return csv.ToString();
        }

        /// <summary>
        /// Graficas: lo que solo esta ahi, sin nombres de personas (la persona responsable
        /// tambien lo guarda): el uso del horario de cada laboratorio y cada semana (o mes)
        /// con sus clases, horas y asistencia. Lo demas va en el CSV de su pestana.
        /// </summary>
        private void CsvGraficas(ResultadoEstadisticas r, Action<IEnumerable<string>> renglon, StringBuilder csv)
        {
            AgrupacionGraficas agrupacion = AgrupacionVista;
            GraficasEstadisticas g = r.Graficas;
            Func<double, string> horas = h => h.ToString("0.##", Inv);
            renglon(new[] { "Uso de los laboratorios:" });
            renglon(new[]
            {
                "Laboratorio", "Horas del horario", "Horas en que se dio clase", "Ocupación (%)", "Clases que se dieron", "Clases que contaban", "Sin registro",
                "Canceladas", "Horas canceladas"
            });
            List<UsoDeLaboratorio> usos = g.Laboratorios.ToList();
            if (usos.Count != 1)
            {
                usos.Add(g.Total);
            }

            foreach (UsoDeLaboratorio u in usos)
            {
                renglon(new[]
                {
                    u == g.Total && u.LaboratorioId == 0 ? "Total" : u.Laboratorio, horas(u.HorasDelHorario), horas(u.HorasUsadas), Pct(u.Ocupacion),
                    N(u.SeDieron), N(u.QueContaban), N(u.SinRegistro), N(u.Canceladas), horas(u.HorasCanceladas)
                });
            }

            csv.Append("\r\n");
            renglon(new[] { agrupacion == AgrupacionGraficas.Mes ? "Por mes:" : "Por semana (de lunes a domingo):" });
            renglon(new[]
            {
                "Desde", "Hasta", "Horas del horario", "Horas en que se dio clase", "Ocupación (%)", "Clases que se dieron", "Clases que contaban", "Con bitácoras",
                "Con asistencias dadas", "Sin registro", "Canceladas", "Entraron", "Veces que contaban", "Asistencia (%)"
            });
            foreach (TramoDelPeriodo t in g.Tramos(agrupacion))
            {
                renglon(new[]
                {
                    FechaCsv(t.Desde), FechaCsv(t.Hasta), horas(t.HorasDelHorario), horas(t.HorasUsadas), Pct(t.Ocupacion), N(t.SeDieron), N(t.QueContaban),
                    N(t.ConBitacoras), N(t.ConDadas), N(t.SinRegistro), N(t.Canceladas), N(t.Entraron), N(t.Veces), Pct(t.Asistencia)
                });
            }
        }

        private string NombreDeLaboratorio(int id, ResultadoEstadisticas r)
        {
            LaboratorioLeido laboratorio;
            return lectura.Laboratorios.TryGetValue(id, out laboratorio) ? laboratorio.Nombre : TextoDeOpcion(r.OpcionesLaboratorio, id);
        }

        private static string TextoDeOpcion(IEnumerable<OpcionCombo> opciones, int id)
        {
            OpcionCombo opcion = opciones.FirstOrDefault(o => o.Id == id);
            return opcion == null ? "" : opcion.Texto;
        }

        private static string N(int n)
        {
            return n.ToString(Inv);
        }

        private static string N(int? n)
        {
            return n.HasValue ? n.Value.ToString(Inv) : "";
        }

        private static string Pct(double? valor)
        {
            return valor.HasValue ? ResumenAsistencia.PorcentajeMostrado(valor.Value).ToString(Inv) : "";
        }

        private static string FechaCsv(DateTime? dia)
        {
            return dia.HasValue ? dia.Value.ToString("dd/MM/yyyy", Inv) : "";
        }

        private void CsvEstudiantes(ResultadoEstadisticas r, Action<IEnumerable<string>> renglon)
        {
            bool conRetiro = lectura.Asistencia.ConRetiro;
            bool conDadas = r.ConDadas;
            List<string> titulos = new List<string> { "Matrícula", "Estudiante", "Cuenta", "Grupo", "Clases", "Entró", "Veces que contaban", "Asistencia (%)", "Faltas", "Clases bajo el 80 %" };
            if (conRetiro)
            {
                titulos.Add("Asistencias quitadas");
            }

            if (conDadas)
            {
                titulos.Add("Asistencias que le dio su docente");
            }

            titulos.AddRange(new[] { "Bitácoras", "Bitácoras fuera de sus clases", "Fallas reportadas", "Última bitácora" });
            renglon(titulos);
            foreach (FilaEstudiante f in tEstudiantes.Visibles)
            {
                List<string> campos = new List<string>
                {
                    f.Matricula, f.Nombre, f.Activo ? "Activa" : "De baja", f.Grupo, N(f.Clases),
                    f.Veces == 0 ? "" : N(f.Entro), f.Veces == 0 ? "" : N(f.Veces), Pct(f.Porcentaje), f.Veces == 0 ? "" : N(f.Faltas), f.Veces == 0 ? "" : N(f.ClasesBajo)
                };
                if (conRetiro)
                {
                    campos.Add(N(f.Retiradas));
                }

                if (conDadas)
                {
                    campos.Add(N(f.Dadas));
                }

                campos.AddRange(new[] { N(f.Bitacoras), N(f.FueraDeSusClases), N(f.Fallas), FechaCsv(f.UltimaBitacora) });
                renglon(campos);
            }
        }

        private void CsvClases(ResultadoEstadisticas r, Action<IEnumerable<string>> renglon)
        {
            // Formato largo (RF141, listo para tabla dinamica): una fila por clase y estudiante con dias que cuentan.
            renglon(new[] { "Materia", "Grupo", "Docente", "Matrícula", "Estudiante", "Entró", "Veces que contaban", "Asistencia (%)", r.TituloDerecho });
            foreach (FilaClase c in tClases.Visibles)
            {
                foreach (ParDeAsistencia p in c.Lista.Where(p => p.Veces > 0))
                {
                    renglon(new[] { c.Materia, c.Grupo, c.Docente, p.Matricula, p.Nombre, N(p.Entro), N(p.Veces), Pct(p.Porcentaje), p.TextoDerecho(r.CicloEntero) });
                }
            }
        }

        private void CsvGrupos(Action<IEnumerable<string>> renglon)
        {
            renglon(new[]
            {
                "Grupo", "Clases", "Materias", "Estudiantes", "Entró", "Veces que contaban", "Asistencia (%)", "Estudiantes bajo el 80 %",
                "Clases que se dieron", "Clases que contaban", "Sin registro", "Clases con bitácoras"
            });
            foreach (FilaGrupo g in tGrupos.Visibles)
            {
                renglon(new[]
                {
                    g.Grupo, N(g.Clases), g.Materias, N(g.Estudiantes), g.Veces == 0 ? "" : N(g.Entro), g.Veces == 0 ? "" : N(g.Veces), Pct(g.Asistencia),
                    g.Veces == 0 ? "" : N(g.Bajo), g.QueContaban == 0 ? "" : N(g.ConBitacoras + g.ConDadas), N(g.QueContaban),
                    g.QueContaban == 0 ? "" : N(g.SinBitacoras), g.QueContaban == 0 ? "" : N(g.ConBitacoras)
                });
            }
        }

        private void CsvDocentes(ResultadoEstadisticas r, Action<IEnumerable<string>> renglon, StringBuilder csv)
        {
            bool conDadas = r.ConDadas;
            bool conReportes = r.ConReportesDocentes;
            List<string> titulos = new List<string> { "Docente", "Materias", "Sus materias", "Clases que se dieron", "Clases que contaban", "Sin registro", "Canceladas", "Clases con bitácoras" };
            if (conDadas)
            {
                titulos.Add("Sin bitácoras, con asistencias dadas");
                titulos.Add("Asistencias que dio");
            }

            if (conReportes)
            {
                titulos.Add("Sus reportes");
            }

            renglon(titulos);
            List<FilaDocente> docentes = tDocentes.Visibles.ToList();
            foreach (FilaDocente d in docentes)
            {
                List<string> campos = new List<string>
                {
                    d.Docente, N(d.Materias), d.NombresDeMaterias, d.QueContaban == 0 ? "" : N(d.SeDieron), N(d.QueContaban),
                    d.QueContaban == 0 ? "" : N(d.SinRegistro), N(d.Canceladas), d.QueContaban == 0 ? "" : N(d.ConBitacoras)
                };
                if (conDadas)
                {
                    campos.Add(d.QueContaban == 0 ? "" : N(d.ConDadas));
                    campos.Add(N(d.AsistenciasQueDio));
                }

                if (conReportes)
                {
                    campos.Add(N(d.SusReportes));
                }

                renglon(campos);
            }

            // Sus clases dia por dia (el detalle de la pestana), de la mas reciente a la mas vieja.
            csv.Append("\r\n");
            renglon(new[] { "Clases día por día:" });
            renglon(new[] { "Docente", "Fecha", "Día", "Materia", "Grupo", "Inicio", "Fin", "Sesión extra", "Laboratorio", "¿Se dio?", "Bitácoras", "Asistencias dadas", "Por qué" });
            foreach (FilaDocente d in docentes)
            {
                foreach (ClaseDiaDocente dia in d.SusClasesDia)
                {
                    renglon(new[]
                    {
                        d.Docente, FechaCsv(dia.Fecha), HorariosDatos.NombresDia[((int)dia.Fecha.DayOfWeek + 6) % 7 + 1], dia.Materia, dia.Grupo,
                        HorariosDatos.Hora(dia.Inicio), HorariosDatos.Hora(dia.Fin), dia.EsExtra ? "Sí" : "No", dia.Laboratorio, dia.SeDioTexto,
                        N(dia.Bitacoras), N(dia.AsistenciasDadas), dia.Ayuda ?? ""
                    });
                }
            }
        }

        private void CsvLaboratorios(ResultadoEstadisticas r, Action<IEnumerable<string>> renglon)
        {
            bool conReportes = r.ConReportesDocentes;
            bool conAvisos = r.ConAvisos;
            List<string> titulos = new List<string>
            {
                "Laboratorio", "Registra sus clases desde", "Clases que se dieron", "Clases que contaban", "Sin registro", "Clases con bitácoras",
                "Bitácoras", "Estudiantes distintos", "Fallas reportadas", "Hardware", "Software", "Red"
            };
            if (conReportes)
            {
                titulos.Add("De docentes");
            }

            if (conAvisos)
            {
                titulos.Add("Equipos sin internet");
                titulos.Add("Avisos sin internet");
            }

            renglon(titulos);
            foreach (FilaLaboratorio l in tLaboratorios.Visibles)
            {
                List<string> campos = new List<string>
                {
                    l.Laboratorio, l.RegistraDesde.HasValue ? FechaCsv(l.RegistraDesde) : l.RegistraDesdeTexto,
                    l.QueContaban == 0 ? "" : N(l.ConBitacoras + l.ConDadas), N(l.QueContaban), l.QueContaban == 0 ? "" : N(l.SinBitacoras),
                    l.QueContaban == 0 ? "" : N(l.ConBitacoras), N(l.Bitacoras), N(l.Estudiantes), N(l.Fallas), N(l.Hardware), N(l.Software), N(l.Red)
                };
                if (conReportes)
                {
                    campos.Add(N(l.DeDocentes));
                }

                if (conAvisos)
                {
                    campos.Add(N(l.EquiposSinInternet));
                    campos.Add(N(l.AvisosSinInternet));
                }

                renglon(campos);
            }
        }

        private void CsvEquipos(ResultadoEstadisticas r, Action<IEnumerable<string>> renglon)
        {
            bool conReportes = r.ConReportesDocentes;
            bool conAvisos = r.ConAvisos;
            List<string> titulos = new List<string> { "Laboratorio", "Equipo", "Fallas reportadas", "Falló en (sesiones)", "Sesiones en que se usó", "Hardware", "Software", "Red" };
            if (conReportes)
            {
                titulos.Add("De docentes");
            }

            if (conAvisos)
            {
                titulos.Add("Avisos sin internet");
            }

            titulos.AddRange(new[] { "Días con reporte", "Último reporte", "Hora del último reporte", "Lo último que reportaron" });
            renglon(titulos);
            foreach (FilaEquipo e in tEquipos.Visibles)
            {
                bool conSesiones = e.Tipo != TipoFilaEquipo.TodoElLaboratorio && e.Registro && e.SesionesUsado > 0;
                List<string> campos = new List<string>
                {
                    e.Laboratorio, e.Equipo, N(e.Fallas), conSesiones ? N(e.SesionesConFalla) : "", conSesiones ? N(e.SesionesUsado) : "",
                    N(e.Hardware), N(e.Software), N(e.Red)
                };
                if (conReportes)
                {
                    campos.Add(N(e.DeDocentes));
                }

                if (conAvisos)
                {
                    campos.Add(N(e.Avisos));
                }

                campos.AddRange(new[]
                {
                    N(e.DiasConReporte), FechaCsv(e.UltimoDia),
                    e.UltimaHora.HasValue ? new DateTime(e.UltimaHora.Value.Ticks % TimeSpan.TicksPerDay).ToString("HH:mm", Inv) : "", e.LoUltimo ?? ""
                });
                renglon(campos);
            }
        }

        // =====================================================================
        // Teclado, cierre y limpieza
        // =====================================================================

        /// <summary>F5, desde cualquier sitio de la ventana (KeyPreview): lo mismo que Actualizar.</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Handled && e.KeyCode == Keys.F5 && e.Modifiers == Keys.None)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (btnActualizar.Enabled)
                {
                    Leer(true);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardOwner();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr ventana, out int proceso);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Lo que siga leyendo ya no se ensena.
            lecturas++;
            esperaCalculo.Stop();
            foreach (Pagina pagina in paginas.Values)
            {
                if (pagina.Espera != null)
                {
                    pagina.Espera.Stop();
                }
            }

            if (Kiosco.Activo)
            {
                VaciarPortapapelesDeDigBit();
            }

            base.OnFormClosed(e);
        }

        /// <summary>
        /// Kiosco: lo que se copio desde "Bitacoras de ..." o desde la bitacora de una
        /// clase (abiertas desde aqui) no se queda para quien entre despues. Solo si lo
        /// que hay lo puso DigBit: lo de otro programa no se toca. Sus tablas copian por
        /// el portapapeles de OLE, cuya ventana es de este proceso mientras DigBit siga
        /// abierto (aunque esas ventanas ya se cerraran); lo copiado de una caja de
        /// texto ya cerrada se queda sin dueno y no se toca (solo es lo que se tecleo).
        /// </summary>
        internal static void VaciarPortapapelesDeDigBit()
        {
            try
            {
                IntPtr dueno = GetClipboardOwner();
                int proceso;
                if (dueno == IntPtr.Zero || GetWindowThreadProcessId(dueno, out proceso) == 0)
                {
                    return;
                }

                using (System.Diagnostics.Process este = System.Diagnostics.Process.GetCurrentProcess())
                {
                    if (proceso != este.Id)
                    {
                        return;
                    }
                }

                Clipboard.Clear();
            }
            catch (ExternalException ex)
            {
                Log.Aviso("Estadisticas: no se pudo vaciar el portapapeles al cerrar (kiosco): " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                esperaCalculo.Dispose();
                foreach (Pagina pagina in paginas.Values)
                {
                    if (pagina.Espera != null)
                    {
                        pagina.Espera.Dispose();
                    }
                }

            }

            base.Dispose(disposing);
            if (disposing)
            {
                // Despues de los controles que las usan.
                foreach (DrawingFont fuente in new[] { fuenteNegrita, fuenteNombre, fuenteTituloFicha, fuenteRotulo, fuenteValor, fuenteLinea, fuenteAviso, fuentePestana })
                {
                    fuente.Dispose();
                }
            }
        }

        // =====================================================================
        // Ayudantes de texto
        // =====================================================================

        internal static string Guion(string texto)
        {
            return string.IsNullOrWhiteSpace(texto) ? EstadisticasDatos.SinDato : texto;
        }

        /// <summary>Para ordenar: null (al final) si esta vacio.</summary>
        internal static IComparable Vacio(string texto)
        {
            return string.IsNullOrWhiteSpace(texto) ? null : texto;
        }

        /// <summary>X / N para ordenar, o null con N = 0 (al final).</summary>
        internal static IComparable Fraccion(int x, int n)
        {
            return n <= 0 ? null : (IComparable)((double)x / n);
        }

        internal static string Unir(params string[] partes)
        {
            List<string> conTexto = partes.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return conTexto.Count == 0 ? null : string.Join("\n", conTexto);
        }

        // =====================================================================
        // Clases de apoyo
        // =====================================================================

        /// <summary>Una opcion del combo Periodo.</summary>
        internal sealed class OpcionPeriodo
        {
            /// <summary>Lo que pide (null en "Elegir fechas...").</summary>
            public PeriodoElegido Periodo;
            /// <summary>"Elegir fechas...": abre el dialogo.</summary>
            public bool ElegirFechas;
            /// <summary>Abre un bloque (la primera plantilla aplicada y lo que sigue a la ultima): la lista abierta pinta una raya encima.</summary>
            public bool AbreBloque;
            public string Texto = "";

            public override string ToString()
            {
                return Texto;
            }
        }

        /// <summary>Lo de cada pestana: sus filtros, su linea de arriba, su buscador, su tabla principal y todas sus tablas.</summary>
        private sealed class Pagina
        {
            public PestanaEstadisticas Cual;
            public TabPage Tab;
            public Label Linea;
            public Label EtiquetaBuscar;
            public TextBox Buscador;
            public Timer Espera;
            public ITablaDisposicion TablaBase;
            public readonly FiltroDeApartado Filtro = new FiltroDeApartado();
            /// <summary>Las que se llenan con lo calculado para esta pestana (la principal y su detalle).</summary>
            public readonly List<ITablaDisposicion> Tablas = new List<ITablaDisposicion>();

            public ITablaBuscable Tabla
            {
                get { return TablaBase as ITablaBuscable; }
                set { TablaBase = value; }
            }

            /// <summary>Un control de la pestana, con su lugar en el orden de Tab (sus listas van primero).</summary>
            public void Agregar(Control control, int orden)
            {
                control.TabIndex = orden;
                Tab.Controls.Add(control);
            }
        }

        /// <summary>Lo que puede filtrar cada lista de una pestana.</summary>
        private enum TipoDeFiltro
        {
            Laboratorio,
            Docente,
            Grupo
        }

        /// <summary>Una lista de los filtros de una pestana, con su etiqueta y, si va entre flechas, sus flechas.</summary>
        private sealed class ListaDeFiltro
        {
            public TipoDeFiltro Tipo;
            public Label Etiqueta;
            public ComboBox Combo;
            public FlechasDeLista Flechas;
        }

        /// <summary>
        /// Los filtros de una pestana (2026-10-31, "cada apartado, los suyos"): sus listas, lo
        /// que pide (0 = "Todos"; lo que no tiene, siempre 0), lo calculado que ensena y lo que
        /// aun calcula. Pedido cuenta lo pedido: un calculo que llega con otro numero ya no se pinta.
        /// </summary>
        private sealed class FiltroDeApartado
        {
            public readonly List<ListaDeFiltro> Listas = new List<ListaDeFiltro>();
            public int Laboratorio;
            public int Docente;
            public int Grupo;
            /// <summary>Lo que ensenan sus tablas (de esta lectura y este periodo), o null.</summary>
            public ResultadoEstadisticas Resultado;
            /// <summary>Su calculo fallo (lo que pintan sus tablas, en rojo), o null.</summary>
            public string Error;
            public bool Calculando;
            public ClaveDeFiltro EnCurso;
            public int Pedido;

            /// <summary>El laboratorio entre flechas (Equipos y Graficas), o null.</summary>
            public FlechasDeLista Flechas
            {
                get
                {
                    ListaDeFiltro lista = Listas.FirstOrDefault(l => l.Flechas != null);
                    return lista == null ? null : lista.Flechas;
                }
            }

            public ClaveDeFiltro Clave
            {
                get { return new ClaveDeFiltro(Laboratorio, Docente, Grupo); }
            }

            /// <summary>Lo que ensena es lo que pide (Resolver ya volvio a "Todos" lo que no tenia clases).</summary>
            public bool AlDia
            {
                get
                {
                    return Resultado != null && Resultado.Filtro.Laboratorio == Laboratorio && Resultado.Filtro.Docente == Docente
                        && Resultado.Filtro.Grupo == Grupo;
                }
            }

            public bool Tiene(TipoDeFiltro tipo)
            {
                return Listas.Any(l => l.Tipo == tipo);
            }

            public int Id(TipoDeFiltro tipo)
            {
                return tipo == TipoDeFiltro.Laboratorio ? Laboratorio : tipo == TipoDeFiltro.Docente ? Docente : Grupo;
            }

            public void Poner(TipoDeFiltro tipo, int id)
            {
                switch (tipo)
                {
                    case TipoDeFiltro.Laboratorio:
                        Laboratorio = id;
                        break;
                    case TipoDeFiltro.Docente:
                        Docente = id;
                        break;
                    default:
                        Grupo = id;
                        break;
                }
            }
        }

        /// <summary>Laboratorio, docente y grupo pedidos (0 = "Todos"): la clave de lo calculado.</summary>
        private struct ClaveDeFiltro : IEquatable<ClaveDeFiltro>
        {
            public static readonly ClaveDeFiltro General = new ClaveDeFiltro(0, 0, 0);

            public readonly int Laboratorio;
            public readonly int Docente;
            public readonly int Grupo;

            public ClaveDeFiltro(int laboratorio, int docente, int grupo)
            {
                Laboratorio = laboratorio;
                Docente = docente;
                Grupo = grupo;
            }

            public bool EsGeneral
            {
                get { return Laboratorio == 0 && Docente == 0 && Grupo == 0; }
            }

            public int Id(TipoDeFiltro tipo)
            {
                return tipo == TipoDeFiltro.Laboratorio ? Laboratorio : tipo == TipoDeFiltro.Docente ? Docente : Grupo;
            }

            public bool Equals(ClaveDeFiltro otra)
            {
                return Laboratorio == otra.Laboratorio && Docente == otra.Docente && Grupo == otra.Grupo;
            }

            public override bool Equals(object obj)
            {
                return obj is ClaveDeFiltro && Equals((ClaveDeFiltro)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Laboratorio * 397 ^ Docente) * 397 ^ Grupo;
                }
            }

            public override string ToString()
            {
                return Laboratorio.ToString(Inv) + "/" + Docente.ToString(Inv) + "/" + Grupo.ToString(Inv);
            }
        }

        /// <summary>Lo que la ventana hace igual con todas las tablas, sin importar su tipo de fila.</summary>
        internal interface ITablaDisposicion
        {
            /// <summary>Las columnas de esa disposicion y vuelve a llenar (la ayuda de cada fila cambia con ella).</summary>
            void Disposicion(bool amplia);

            /// <summary>Solo las columnas que se ven y sus titulos, sin volver a llenar.</summary>
            void Columnas(bool amplia);

            void Vaciar();
        }

        internal interface ITablaBuscable : ITablaDisposicion
        {
            DataGridView Grid { get; }

            void Buscar(string texto);
        }

        /// <summary>Un recuadro de detalle (a la derecha o debajo de su tabla) con borde suave y su texto cuando no hay nada elegido.</summary>
        internal sealed class Ficha : Panel
        {
            private readonly Color borde;
            private bool conDatos;
            private string vacio = "";

            public Ficha(Color borde)
            {
                this.borde = borde;
                BackColor = Color.White;
                DoubleBuffered = true;
                ResizeRedraw = true;
                Padding = new Padding(1);
                Contenido = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Visible = false };
                Controls.Add(Contenido);
            }

            /// <summary>Donde van sus controles; se esconde entero sin nada elegido.</summary>
            public Panel Contenido { get; private set; }

            public string Vacio
            {
                get { return vacio; }
                set
                {
                    vacio = value ?? "";
                    Invalidate();
                }
            }

            public bool ConDatos
            {
                get { return conDatos; }
                set
                {
                    conDatos = value;
                    Contenido.Visible = value;
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Pen lapiz = new Pen(borde))
                {
                    e.Graphics.DrawRectangle(lapiz, 0, 0, Width - 1, Height - 1);
                }

                if (!conDatos && vacio.Length > 0)
                {
                    TextRenderer.DrawText(e.Graphics, vacio, Font, new Rectangle(16, 0, Width - 32, Height), Color.Gray,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }
            }
        }

        /// <summary>Un dato con su rotulo en mayusculas encima ("MATRICULA" / "2026900001"), como la ficha del docente.</summary>
        internal sealed class Rotulo
        {
            private readonly Label titulo;
            private readonly Label valor;

            public Rotulo(Control padre, string texto, DrawingFont fuenteTitulo, DrawingFont fuenteValor)
            {
                titulo = new Label { AutoSize = false, AutoEllipsis = true, Font = fuenteTitulo, ForeColor = TextoGris, Text = texto, UseMnemonic = false };
                valor = new Label { AutoSize = false, AutoEllipsis = true, Font = fuenteValor, ForeColor = TextoOscuro, UseMnemonic = false };
                padre.Controls.Add(titulo);
                padre.Controls.Add(valor);
            }

            public string Texto
            {
                set { valor.Text = value ?? ""; }
            }

            public bool Visible
            {
                set
                {
                    titulo.Visible = value;
                    valor.Visible = value;
                }
            }

            public void Poner(int x, int y, int ancho)
            {
                titulo.SetBounds(x, y, ancho, 15);
                valor.SetBounds(x, y + 15, ancho, 20);
            }
        }

        /// <summary>Una columna de una tabla de Estadisticas: su texto, como se ordena, su barra y sus colores.</summary>
        internal sealed class Columna<T> where T : class
        {
            public string Nombre = "";
            public string Titulo = "";
            /// <summary>El titulo segun la disposicion (amplia o compacta), si cambia.</summary>
            public Func<bool, string> TituloSegun;
            public Func<T, string> Texto;
            /// <summary>Para ordenar; null = sin dato (siempre al final).</summary>
            public Func<T, IComparable> Clave;
            public bool PrimeroDescendente;
            /// <summary>Con barra (BarraEnCelda): la fraccion de 0 a 1, o null (solo el texto, en gris).</summary>
            public Func<T, double?> Fraccion;
            public EstiloBarra Estilo;
            public Func<T, Color?> ColorTexto;
            public Func<T, bool> Negrita;
            public Func<T, Color?> Fondo;
            /// <summary>La ayuda propia de la celda; si no, la de la fila.</summary>
            public Func<T, string> Ayuda;
            public string AyudaDelTitulo;
            /// <summary>Fija: su ancho (crece con lo que pide su texto). Rellena: su minimo.</summary>
            public int Ancho = 90;
            /// <summary>Lo que se deja al crecer, ademas de lo que mide su texto mas largo (con fondo de color, mas).</summary>
            public int Holgura = 10;
            public bool Rellena;
            /// <summary>Rellena: su parte del sitio que sobra (FillWeight).</summary>
            public float Peso = 100;
            public bool Derecha;
            /// <summary>En la disposicion compacta no se ve (su dato va en la ayuda de la fila).</summary>
            public bool SoloAmplia;
            /// <summary>Solo con una migracion (db/10, db/15, db/22): sin ella, ni se ve ni va al CSV.</summary>
            public Func<bool> Existe;
            /// <summary>El cuadrito con el color de ese docente (su id), delante del texto.</summary>
            public Func<T, int> Cuadrito;
            /// <summary>Se pone detras de esa columna (por su nombre).</summary>
            public string Despues;
            public int Indice;
        }

        /// <summary>
        /// Una tabla (EstiloHorarios.CrearGrid, con doble bufer) llenada con filas de T:
        /// busqueda sin acentos, orden propio con un clic en el titulo (los "-" y las
        /// filas sin nada al final, "Todos" arriba), barras en las celdas, colores, la
        /// ayuda de cada fila y el texto que se pinta cuando no hay filas. Conserva la
        /// fila elegida al volver a llenarse.
        /// </summary>
        internal sealed class Tabla<T> : ITablaBuscable where T : class
        {
            private static readonly PropertyInfo DobleBufer = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly StringComparer PorNombre = StringComparer.Create(CultureInfo.GetCultureInfo("es-MX"), true);

            private readonly DataGridView grid;
            private readonly List<Columna<T>> columnas = new List<Columna<T>>();
            private readonly DrawingFont negrita;
            private List<T> todas = new List<T>();
            private List<T> visibles = new List<T>();
            private Columna<T> orden;
            private bool descendente;
            private string busqueda = "";
            private bool amplia;
            private bool llenando;
            // Hay una fila elegida de verdad (por quien usa la ventana o conservada al volver a llenar).
            private bool hayEleccion;

            public Func<T, string> TextoDeBusqueda;
            public Func<T, string> ClaveDeFila;
            public Func<T, bool> Arriba;
            public Func<T, bool> AlFinal;
            public Func<T, bool, string> AyudaDeFila;
            public Func<T, Color?> FondoDeFila;
            public Func<T, bool> FilaGris;
            public Func<T, bool> FilaNegrita;
            public Func<string> TextoSinFilas;
            public Func<bool> SinFilasEsError;
            public string SinCoincidencias = "Nadie coincide con la búsqueda.";
            /// <summary>Con una sola fila, queda elegida (Enter la abre).</summary>
            public bool ElegirLaUnica = true;

            public event Action Elegida;
            public event Action<T> Abrir;

            public Tabla(string nombre, DrawingFont negrita)
            {
                this.negrita = negrita;
                grid = EstiloHorarios.CrearGrid(Point.Empty, new Size(200, 120));
                grid.AccessibleName = nombre;
                grid.RowTemplate.Height = 34;
                // Un titulo largo ("Fallas reportadas") pasa a dos lineas en vez de cortarse, como PantallaAdmin.CrearLista.
                grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
                grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
                // Con margen arriba y abajo, como PantallaAdmin.CrearLista.
                grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 7, 8, 7);
                // El titulo de la columna de la celda elegida no se pinta con el azul de Windows
                // (con .NET 4.7.2 la tabla lo marca como elegido): siempre con su fondo y su verde.
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = grid.ColumnHeadersDefaultCellStyle.ForeColor;
                // Fuera del kiosco, Ctrl+C copia la fila con sus titulos; en kiosco, nada (sec. 7).
                grid.ClipboardCopyMode = Kiosco.Activo ? DataGridViewClipboardCopyMode.Disable : DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
                grid.StandardTab = true;
                // Con barras en varias columnas: sin parpadeo al cambiar de fila (CrearGrid no lo trae; AdministrarHorarios no se toca).
                if (DobleBufer != null)
                {
                    DobleBufer.SetValue(grid, true, null);
                }

                grid.CellFormatting += Formato;
                grid.CellPainting += Pintar;
                grid.ColumnHeaderMouseClick += ClicEnTitulo;
                grid.SelectionChanged += (s, e) =>
                {
                    if (!llenando && Elegida != null)
                    {
                        Elegida();
                    }
                };
                grid.CellDoubleClick += (s, e) =>
                {
                    // La fila del doble clic (el primer clic ya la eligio).
                    if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count && Abrir != null && grid.Rows[e.RowIndex].Tag is T)
                    {
                        Abrir((T)grid.Rows[e.RowIndex].Tag);
                    }
                };
                // Al verse por primera vez (al abrir su pestana), DataGridView elige sola la
                // primera fila: sin eleccion de verdad se quita, y el detalle dice "Elige...".
                grid.VisibleChanged += (s, e) =>
                {
                    if (grid.Visible && grid.IsHandleCreated)
                    {
                        grid.BeginInvoke(new Action(QuitarEleccionSola));
                    }
                };
                grid.HandleCreated += (s, e) => grid.BeginInvoke(new Action(QuitarEleccionSola));
                grid.CellMouseDown += (s, e) => hayEleccion = true;
                grid.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown
                        || e.KeyCode == Keys.Home || e.KeyCode == Keys.End)
                    {
                        hayEleccion = true;
                    }
                };
                grid.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None)
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        AbrirElegida();
                    }
                };
                grid.Paint += PintarSinFilas;
            }

            public DataGridView Grid
            {
                get { return grid; }
            }

            public T Elegido
            {
                get { return grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as T; }
            }

            /// <summary>Tiene filas, aunque la busqueda las esconda todas.</summary>
            public bool TieneFilas
            {
                get { return todas.Count > 0; }
            }

            /// <summary>Las filas que se ven (las que deja la busqueda), en su orden: lo que va al CSV.</summary>
            public IList<T> Visibles
            {
                get { return visibles.AsReadOnly(); }
            }

            public void Agregar(Columna<T> c)
            {
                c.Indice = columnas.Count;
                columnas.Add(c);
                DataGridViewTextBoxColumn columna = new DataGridViewTextBoxColumn
                {
                    Name = c.Nombre,
                    HeaderText = SinCortarElPorcentaje(c.Titulo),
                    SortMode = c.Clave != null ? DataGridViewColumnSortMode.Programmatic : DataGridViewColumnSortMode.NotSortable,
                    ToolTipText = c.AyudaDelTitulo ?? "",
                    ReadOnly = true
                };
                if (c.Rellena)
                {
                    columna.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    columna.MinimumWidth = c.Ancho;
                    columna.FillWeight = c.Peso;
                }
                else
                {
                    columna.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    columna.Width = c.Ancho;
                }

                columna.DefaultCellStyle.Alignment = c.Derecha ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft;
                grid.Columns.Add(columna);
                if (c.Despues != null && grid.Columns.Contains(c.Despues))
                {
                    columna.DisplayIndex = grid.Columns[c.Despues].DisplayIndex + 1;
                }
            }

            public void Poner(IEnumerable<T> filas)
            {
                todas = filas == null ? new List<T>() : filas.ToList();
                Mostrar();
            }

            public void Vaciar()
            {
                Poner(null);
            }

            public void Buscar(string texto)
            {
                busqueda = texto ?? "";
                Mostrar();
            }

            public void Disposicion(bool esAmplia)
            {
                Columnas(esAmplia);

                // La ayuda de cada fila cambia con lo que se ve.
                if (todas.Count > 0)
                {
                    Mostrar();
                }
            }

            public void Columnas(bool esAmplia)
            {
                amplia = esAmplia;
                foreach (Columna<T> c in columnas)
                {
                    DataGridViewColumn columna = grid.Columns[c.Indice];
                    columna.Visible = (c.Existe == null || c.Existe()) && (!c.SoloAmplia || amplia);
                    if (c.TituloSegun != null)
                    {
                        columna.HeaderText = SinCortarElPorcentaje(c.TituloSegun(amplia));
                    }
                }
            }

            /// <summary>"80 %" no se parte en dos renglones en un titulo ("Derecho (80" / "%)"): espacio que no se corta.</summary>
            private static string SinCortarElPorcentaje(string titulo)
            {
                return (titulo ?? "").Replace(" %", " %");
            }

            /// <summary>Ordena por esa columna (por su nombre); si no se ve, deja el orden por omision.</summary>
            public void Ordenar(string nombre, bool desc)
            {
                Columna<T> c = columnas.FirstOrDefault(x => x.Nombre == nombre);
                if (c == null || c.Clave == null || !grid.Columns[c.Indice].Visible)
                {
                    OrdenPorOmision();
                    return;
                }

                orden = c;
                descendente = desc;
                Mostrar();
            }

            /// <summary>
            /// Elige la primera fila que cumple eso (como si se pulsara: no se quita al verse la
            /// tabla) y la lleva a la vista; false si no hay ninguna. Lo usa la ficha del docente.
            /// </summary>
            public bool ElegirFila(Func<T, bool> cual)
            {
                DataGridViewRow fila = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is T && cual((T)r.Tag));
                if (fila == null)
                {
                    return false;
                }

                hayEleccion = true;
                DataGridViewColumn primera = grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
                if (primera != null)
                {
                    grid.CurrentCell = fila.Cells[primera.Index];
                }

                grid.ClearSelection();
                fila.Selected = true;
                return true;
            }

            /// <summary>El orden en que llegan (el de la especificacion para cada pestana).</summary>
            public void OrdenPorOmision()
            {
                orden = null;
                descendente = false;
                Mostrar();
            }

            /// <summary>El ancho para ver todas sus columnas sin barra horizontal (las que rellenan, en su minimo).</summary>
            public int AnchoMinimo
            {
                get
                {
                    int ancho = SystemInformation.VerticalScrollBarWidth + 3;
                    foreach (DataGridViewColumn columna in grid.Columns)
                    {
                        if (columna.Visible)
                        {
                            ancho += columna.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill ? columna.MinimumWidth : columna.Width;
                        }
                    }

                    return ancho;
                }
            }

            /// <summary>El alto para que se vean esas filas (con su titulo), sin barra de desplazamiento.</summary>
            public int AltoParaFilas(int filas)
            {
                return grid.ColumnHeadersHeight + Math.Max(1, filas) * grid.RowTemplate.Height + 4
                    + (grid.HorizontalScrollingOffset > 0 || grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) > grid.ClientSize.Width ? SystemInformation.HorizontalScrollBarHeight : 0);
            }

            /// <summary>Quita la fila que DataGridView eligio sola al verse la tabla, si nadie eligio ninguna.</summary>
            private void QuitarEleccionSola()
            {
                if (hayEleccion || grid.IsDisposed || grid.SelectedRows.Count == 0)
                {
                    return;
                }

                llenando = true;
                try
                {
                    grid.ClearSelection();
                    grid.CurrentCell = null;
                }
                finally
                {
                    llenando = false;
                }

                if (Elegida != null)
                {
                    Elegida();
                }
            }

            private void AbrirElegida()
            {
                T fila = Elegido;
                if (fila != null && Abrir != null)
                {
                    Abrir(fila);
                }
            }

            private void ClicEnTitulo(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || e.ColumnIndex < 0 || e.ColumnIndex >= columnas.Count)
                {
                    return;
                }

                Columna<T> c = columnas[e.ColumnIndex];
                if (c.Clave == null)
                {
                    return;
                }

                descendente = orden == c ? !descendente : c.PrimeroDescendente;
                orden = c;
                Mostrar();
            }

            /// <summary>Busca, ordena y vuelve a llenar, conservando la fila elegida.</summary>
            private void Mostrar()
            {
                T antes = Elegido;
                string clave = antes == null || ClaveDeFila == null ? null : ClaveDeFila(antes);
                string buscada = busqueda.Trim();
                List<T> pasan = buscada.Length == 0 || TextoDeBusqueda == null
                    ? todas.ToList()
                    : todas.Where(f => (Arriba != null && Arriba(f)) || EstiloAdmin.Coincide(EstiloAdmin.Normalizar(TextoDeBusqueda(f)), buscada)).ToList();
                visibles = Ordenadas(pasan);

                llenando = true;
                try
                {
                    grid.SuspendLayout();
                    grid.Rows.Clear();
                    if (visibles.Count > 0)
                    {
                        DataGridViewRow[] filas = new DataGridViewRow[visibles.Count];
                        for (int i = 0; i < visibles.Count; i++)
                        {
                            filas[i] = Fila(visibles[i]);
                        }

                        grid.Rows.AddRange(filas);
                    }

                    foreach (Columna<T> c in columnas)
                    {
                        grid.Columns[c.Indice].HeaderCell.SortGlyphDirection = c == orden ? (descendente ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
                    }

                    grid.ClearSelection();
                    grid.CurrentCell = null;
                    DataGridViewRow volver = null;
                    if (clave != null)
                    {
                        volver = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ClaveDeFila((T)r.Tag) == clave);
                    }

                    if (volver == null && ElegirLaUnica && grid.Rows.Count == 1)
                    {
                        volver = grid.Rows[0];
                    }

                    if (volver != null)
                    {
                        DataGridViewColumn primera = grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
                        if (primera != null)
                        {
                            grid.CurrentCell = volver.Cells[primera.Index];
                        }

                        grid.ClearSelection();
                        volver.Selected = true;
                    }

                    hayEleccion = volver != null;
                    AjustarAnchos();
                }
                finally
                {
                    grid.ResumeLayout();
                    llenando = false;
                }

                grid.Invalidate();
                if (Elegida != null)
                {
                    Elegida();
                }
            }

            private List<T> Ordenadas(List<T> lista)
            {
                List<T> arriba = Arriba == null ? new List<T>() : lista.Where(Arriba).ToList();
                List<T> resto = Arriba == null ? lista : lista.Where(f => !Arriba(f)).ToList();
                if (orden != null)
                {
                    Columna<T> c = orden;
                    int signo = descendente ? -1 : 1;
                    resto = resto
                        .Select((f, i) => new { Fila = f, Indice = i, Clave = c.Clave(f), Final = AlFinal != null && AlFinal(f) })
                        .OrderBy(x => x.Final ? 1 : 0)
                        .ThenBy(x => x.Clave == null ? 1 : 0)
                        .ThenBy(x => x.Clave, Comparer<IComparable>.Create((a, b) => signo * Comparar(a, b)))
                        .ThenBy(x => x.Indice)
                        .Select(x => x.Fila)
                        .ToList();
                }

                arriba.AddRange(resto);
                return arriba;
            }

            private static int Comparar(IComparable a, IComparable b)
            {
                if (a == null || b == null)
                {
                    return a == null ? (b == null ? 0 : 1) : -1;
                }

                string textoA = a as string;
                string textoB = b as string;
                if (textoA != null && textoB != null)
                {
                    return PorNombre.Compare(textoA, textoB);
                }

                return a.CompareTo(b);
            }

            private DataGridViewRow Fila(T f)
            {
                DataGridViewRow fila = new DataGridViewRow();
                fila.CreateCells(grid);
                // Una fila hecha a mano no toma el alto de RowTemplate: sin esto
                // salia de 22 y la barra se encimaba en el texto.
                fila.Height = grid.RowTemplate.Height;
                string ayuda = AyudaDeFila == null ? null : AyudaDeFila(f, amplia);
                for (int i = 0; i < columnas.Count; i++)
                {
                    Columna<T> c = columnas[i];
                    string texto = c.Texto(f) ?? "";
                    fila.Cells[i].Value = texto;
                    string propia = c.Ayuda == null ? null : c.Ayuda(f);
                    // Una columna que rellena puede cortarse con "...": su texto entero va primero.
                    fila.Cells[i].ToolTipText = propia ?? (c.Rellena ? (ayuda == null ? "" : texto + "\n" + ayuda) : (ayuda ?? ""));
                }

                fila.Tag = f;
                if (FondoDeFila != null)
                {
                    Color? fondo = FondoDeFila(f);
                    if (fondo.HasValue)
                    {
                        fila.DefaultCellStyle.BackColor = fondo.Value;
                    }
                }

                if (FilaGris != null && FilaGris(f))
                {
                    fila.DefaultCellStyle.ForeColor = Color.Gray;
                }

                if (FilaNegrita != null && FilaNegrita(f))
                {
                    fila.DefaultCellStyle.Font = negrita;
                }

                return fila;
            }

            private void Formato(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.ColumnIndex >= columnas.Count)
                {
                    return;
                }

                T fila = grid.Rows[e.RowIndex].Tag as T;
                if (fila == null)
                {
                    return;
                }

                Columna<T> c = columnas[e.ColumnIndex];
                if (string.Equals(e.Value as string, EstadisticasDatos.SinDato, StringComparison.Ordinal))
                {
                    e.CellStyle.ForeColor = Gris;
                    e.CellStyle.SelectionForeColor = GrisElegida;
                }
                else if (c.ColorTexto != null)
                {
                    Color? color = c.ColorTexto(fila);
                    if (color.HasValue)
                    {
                        e.CellStyle.ForeColor = color.Value;
                        e.CellStyle.SelectionForeColor = color.Value;
                    }
                }

                if (c.Negrita != null && c.Negrita(fila))
                {
                    e.CellStyle.Font = negrita;
                }

                if (c.Fondo != null)
                {
                    Color? fondo = c.Fondo(fila);
                    if (fondo.HasValue)
                    {
                        e.CellStyle.BackColor = fondo.Value;
                        e.CellStyle.SelectionBackColor = EstiloHorarios.Oscurecer(fondo.Value, 0.14f);
                    }
                }
            }

            private void Pintar(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.ColumnIndex >= columnas.Count)
                {
                    return;
                }

                T fila = grid.Rows[e.RowIndex].Tag as T;
                if (fila == null)
                {
                    return;
                }

                Columna<T> c = columnas[e.ColumnIndex];
                if (c.Fraccion != null)
                {
                    BarraEnCelda.Pintar(e, null, c.Fraccion(fila), c.Estilo);
                    return;
                }

                if (c.Cuadrito != null)
                {
                    PintarCuadrito(e, c.Cuadrito(fila));
                }
            }

            /// <summary>El cuadrito con el color del docente y su nombre, como el combo de Horarios.</summary>
            private static void PintarCuadrito(DataGridViewCellPaintingEventArgs e, int idDocente)
            {
                e.Paint(e.CellBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
                if ((e.PaintParts & DataGridViewPaintParts.ContentForeground) != 0)
                {
                    Rectangle cuadro = new Rectangle(e.CellBounds.X + 6, e.CellBounds.Y + 6, 13, e.CellBounds.Height - 13);
                    using (SolidBrush relleno = new SolidBrush(CuadriculaSemanal.ColorDeProfesor(idDocente)))
                    {
                        e.Graphics.FillRectangle(relleno, cuadro);
                    }

                    e.Graphics.DrawRectangle(Pens.Gray, cuadro);
                    bool elegida = (e.State & DataGridViewElementStates.Selected) != 0;
                    Rectangle zona = new Rectangle(cuadro.Right + 7, e.CellBounds.Y, Math.Max(0, e.CellBounds.Right - cuadro.Right - 10), e.CellBounds.Height - 1);
                    TextRenderer.DrawText(e.Graphics, Convert.ToString(e.FormattedValue, Inv), e.CellStyle.Font, zona,
                        elegida ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                        | TextFormatFlags.PreserveGraphicsClipping);
                }

                e.Handled = true;
            }

            /// <summary>Sin filas: leyendo, el error (en rojo), el vacio de la pestana o que nadie coincide con la busqueda.</summary>
            private void PintarSinFilas(object sender, PaintEventArgs e)
            {
                if (grid.Rows.Count > 0)
                {
                    return;
                }

                bool porBusqueda = busqueda.Trim().Length > 0 && todas.Count > 0;
                string texto = porBusqueda ? SinCoincidencias : TextoSinFilas == null ? null : TextoSinFilas();
                if (string.IsNullOrEmpty(texto))
                {
                    return;
                }

                bool esError = !porBusqueda && SinFilasEsError != null && SinFilasEsError();
                Rectangle area = new Rectangle(16, grid.ColumnHeadersHeight, Math.Max(10, grid.ClientSize.Width - 32), Math.Max(10, grid.ClientSize.Height - grid.ColumnHeadersHeight));
                TextRenderer.DrawText(e.Graphics, texto, grid.Font, area, esError ? TextoError : Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }

            /// <summary>
            /// Las columnas fijas nunca mas angostas que su texto mas largo de lo que se ve
            /// (hasta 300 filas) ni que la palabra mas larga de su titulo: un numero o una
            /// fecha no se cortan. Solo crecen: al filtrar, las columnas no bailan.
            /// </summary>
            private void AjustarAnchos()
            {
                int hasta = Math.Min(grid.Rows.Count, 300);
                DrawingFont titulo = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
                foreach (Columna<T> c in columnas)
                {
                    DataGridViewColumn columna = grid.Columns[c.Indice];
                    if (c.Rellena || !columna.Visible)
                    {
                        continue;
                    }

                    // El titulo, partido en renglones, nunca mas angosto que su palabra mas larga
                    // (con el margen y el sitio de la flecha de ordenar), como ColumnasDeLista.EncabezadoEntero.
                    string encabezado = columna.HeaderText ?? "";
                    int pide = columna.GetPreferredWidth(DataGridViewAutoSizeColumnMode.ColumnHeader, true);
                    if (encabezado.IndexOf(' ') > 0)
                    {
                        pide = pide - TextRenderer.MeasureText(encabezado, titulo).Width
                            + encabezado.Split(' ').Max(p => TextRenderer.MeasureText(p, titulo).Width);
                    }

                    DrawingFont fuente = c.Negrita != null || FilaNegrita != null ? negrita : grid.Font;
                    HashSet<string> medidos = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < hasta; i++)
                    {
                        string texto = Convert.ToString(grid.Rows[i].Cells[c.Indice].Value, Inv);
                        if (!string.IsNullOrEmpty(texto) && medidos.Add(texto))
                        {
                            pide = Math.Max(pide, TextRenderer.MeasureText(texto, fuente).Width + (c.Fraccion != null ? Math.Max(16, c.Holgura) : c.Holgura) + (c.Cuadrito != null ? 26 : 0));
                        }
                    }

                    if (pide > columna.Width)
                    {
                        columna.Width = Math.Min(pide, Math.Max(c.Ancho * 2, 160));
                    }
                }
            }
        }

        /// <summary>
        /// "Elegir fechas" (sec. 1.5): del y al, cualquier dia hasta hoy (fecha del servidor),
        /// con como mucho 400 dias entre los dos, y "Hasta hoy" (el ultimo dia avanza solo).
        /// </summary>
        internal sealed class DialogoFechas : Form
        {
            private static readonly DateTime PrimerDia = new DateTime(2000, 1, 1);
            private readonly DateTimePicker dtpDesde;
            private readonly DateTimePicker dtpHasta;
            private readonly CheckBox chkHastaHoy;
            private readonly Label lblError;
            private readonly DateTime hoy;
            private readonly DrawingFont fuenteNota = new DrawingFont("Century Gothic", 9F);
            private readonly DrawingFont fuenteCasilla = new DrawingFont("Century Gothic", 9.5F);

            public DialogoFechas(DateTime hoy, DateTime desde, DateTime hasta, bool hastaHoy)
            {
                this.hoy = hoy.Date;
                EstiloHorarios.PrepararDialogo(this, "Elegir fechas", new Size(520, 300));
                Controls.Add(EstiloAdmin.TituloDialogo("Elegir fechas", 472));
                Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = fuenteNota,
                    ForeColor = TextoGris,
                    Location = new Point(24, 50),
                    Size = new Size(472, 54),
                    Text = "Las dos fechas cuentan. Puedes elegir cualquier día hasta hoy, con como mucho 400 días entre las dos. "
                        + "Para un ciclo completo, con el derecho a evaluación (80 %), elígelo en Periodo."
                });

                Controls.Add(EstiloHorarios.CrearEtiqueta("Del", new Point(24, 112)));
                dtpDesde = EstiloHorarios.CrearFechaConDia(this, new Point(24, 135));
                Controls.Add(EstiloHorarios.CrearEtiqueta("al", new Point(272, 112)));
                dtpHasta = EstiloHorarios.CrearFechaConDia(this, new Point(272, 135));
                foreach (DateTimePicker fecha in new[] { dtpDesde, dtpHasta })
                {
                    // Primero el minimo y despues el maximo: el valor que traen se ajusta a los dos.
                    fecha.MinDate = PrimerDia;
                    fecha.MaxDate = this.hoy;
                }

                dtpDesde.Value = Recortar(desde);
                dtpHasta.Value = Recortar(hasta);
                dtpDesde.AccessibleName = "Del";
                dtpHasta.AccessibleName = "al";
                chkHastaHoy = new CheckBox
                {
                    AutoSize = true,
                    Checked = hastaHoy,
                    Font = fuenteCasilla,
                    Location = new Point(272, 170),
                    Text = "Hasta hoy (avanza solo cada día)"
                };
                Controls.Add(chkHastaHoy);
                // Dos renglones: el error de los 400 dias (el mismo texto de Faltas) no cabe en uno.
                lblError = EstiloAdmin.LineaError(new Point(24, 202), 472, 40);
                Controls.Add(lblError);
                dtpDesde.ValueChanged += (s, e) => lblError.Text = "";
                dtpHasta.ValueChanged += (s, e) => lblError.Text = "";
                chkHastaHoy.CheckedChanged += (s, e) => PonerHastaHoy();
                PonerHastaHoy();
                EstiloAdmin.BotonesDialogo(this, "Aceptar", Aceptar);
                ActiveControl = dtpDesde;
            }

            public DateTime Desde
            {
                get { return dtpDesde.Value.Date; }
            }

            /// <summary>El ultimo dia: hoy con "Hasta hoy".</summary>
            public DateTime Hasta
            {
                get { return chkHastaHoy.Checked ? hoy : dtpHasta.Value.Date; }
            }

            public bool HastaHoy
            {
                get { return chkHastaHoy.Checked; }
            }

            /// <summary>Con "Hasta hoy", "al" se apaga y pasa a hoy.</summary>
            private void PonerHastaHoy()
            {
                if (chkHastaHoy.Checked)
                {
                    dtpHasta.Value = hoy;
                }

                dtpHasta.Enabled = !chkHastaHoy.Checked;
                lblError.Text = "";
            }

            private DateTime Recortar(DateTime dia)
            {
                return dia.Date < PrimerDia ? PrimerDia : dia.Date > hoy ? hoy : dia.Date;
            }

            private void Aceptar()
            {
                string problema = EstadisticasDatos.ValidarFechas(Desde, Hasta, hoy);
                if (problema != null)
                {
                    lblError.Text = problema;
                    return;
                }

                DialogResult = DialogResult.OK;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    fuenteNota.Dispose();
                    fuenteCasilla.Dispose();
                }
            }
        }
    }
}
