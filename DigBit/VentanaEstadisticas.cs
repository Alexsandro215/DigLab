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
    /// las decisiones del usuario del 2026-10-21 y 2026-10-22): un periodo y tres
    /// filtros (laboratorio, docente y grupo), cinco cifras grandes que llevan a su
    /// pestana y seis pestanas: Estudiantes (con "Sus clases" y "Ver sus
    /// bitacoras"), Clases (con "Estudiantes de la clase"), Grupos, Docentes (SOLO la
    /// asistencia de los docentes, con sus clases dia por dia y la bitacora de cada
    /// una con doble clic), Laboratorios y Equipos.
    ///
    /// Nunca lee en el hilo de la pantalla: EstadisticasDatos.Leer va en Task.Run con
    /// un numero de lectura (la que llega tarde se descarta), y cambiar laboratorio,
    /// docente o grupo solo vuelve a calcular (EstadisticasDatos.Calcular), sin ir a
    /// la base. Nunca escribe en la base: la persona responsable del laboratorio la
    /// ve igual, y solo guarda en CSV Laboratorios, Grupos y Equipos. En kiosco no hay
    /// CSV ni se puede copiar de sus tablas.
    ///
    /// Dos disposiciones, como Mis grupos: amplia desde 1600 x 860 de cliente (todas
    /// las columnas) y compacta por debajo (lo que no cabe va en la ayuda de la fila).
    /// Con menos de 640 de alto (1366x768 con Windows al 125 %) se quita el subtitulo
    /// para que quepan las tablas.
    /// </summary>
    internal sealed class VentanaEstadisticas : PantallaAdmin
    {
        private const string Titulo = "Estadísticas";
        private const string Subtitulo = "Cuántas veces entró cada estudiante a sus clases, qué clases se dieron y qué fallas se reportaron, "
            + "en el periodo que elijas. F5 actualiza.";
        private const string TextoLeyendo = "Leyendo las estadísticas...";
        private const string TextoCalculando = "Calculando...";
        private const string TituloCsv = "Guardar en Excel (CSV)";
        private const string AyudaCsv = "Guarda la pestaña que ves, con todas sus columnas y las filas que deja la búsqueda, "
            + "en un archivo que Excel abre con doble clic.";
        private const int AnchoBuscador = 220;
        private const int Separacion = 10;
        private const int AltoMinimoNormal = 640;
        private const int AltoCifrasBaja = 78;

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
        // la sesion), como la casilla de Faltas.
        private static TipoPeriodo ultimoTipo = TipoPeriodo.Ciclo;
        private static DateTime ultimoDesde;
        private static DateTime ultimoHasta;
        private static int ultimoLaboratorio;
        private static int ultimoDocente;
        private static int ultimoGrupo;

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
        private readonly Label lblLaboratorio;
        private readonly Label lblDocente;
        private readonly Label lblGrupo;
        private readonly ComboBox cboPeriodo;
        private readonly ComboBox cboLaboratorio;
        private readonly ComboBox cboDocente;
        private readonly ComboBox cboGrupo;
        private readonly CifraGrande[] cifras = new CifraGrande[5];
        private readonly TabControl tabs;
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

        // Lo pedido y lo leido.
        private PeriodoElegido periodo;
        private LecturaEstadisticas lectura;
        private ResultadoEstadisticas resultado;
        private string error;
        private int lecturas;
        private bool leyendoBase;
        // Hay una lectura de la base en marcha; lo pedido mientras tanto espera a que acabe.
        private bool baseEnMarcha;
        private bool otraPedida;
        private bool otraForzada;
        private bool calculando;
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

            // ----- Filtros -----
            lblPeriodo = Etiqueta("Periodo");
            lblLaboratorio = Etiqueta("Laboratorio");
            lblDocente = Etiqueta("Docente");
            lblGrupo = Etiqueta("Grupo");
            cboPeriodo = Combo(0, "Periodo");
            cboLaboratorio = Combo(1, "Laboratorio");
            cboDocente = Combo(2, "Docente");
            cboGrupo = Combo(3, "Grupo");
            cboPeriodo.DrawItem += DibujarOpcion;
            cboLaboratorio.DrawItem += DibujarOpcion;
            cboDocente.DrawItem += DibujarDocente;
            cboGrupo.DrawItem += DibujarOpcion;
            cboPeriodo.SelectedIndexChanged += (s, e) => { if (!poniendoCombos) PeriodoCambiado(); };
            cboLaboratorio.SelectedIndexChanged += (s, e) => { if (!poniendoCombos) FiltroCambiado(); };
            cboDocente.SelectedIndexChanged += (s, e) => { if (!poniendoCombos) FiltroCambiado(); };
            cboGrupo.SelectedIndexChanged += (s, e) => { if (!poniendoCombos) FiltroCambiado(); };

            // ----- Cifras -----
            string[] etiquetas = { "asistencia de los estudiantes", "clases que se dieron", "bitácoras registradas", "fallas reportadas", "equipos avisados sin internet" };
            for (int i = 0; i < cifras.Length; i++)
            {
                int cual = i;
                cifras[i] = new CifraGrande { Etiqueta = etiquetas[i], TabIndex = 4 + i };
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
                TabIndex = 9
            };
            tabs.DrawItem += DibujarPestana;
            tabs.SelectedIndexChanged += (s, e) => PestanaCambiada();
            Controls.Add(tabs);

            Pagina estudiantes = NuevaPagina(PestanaEstadisticas.Estudiantes, "Estudiantes", true);
            Pagina clases = NuevaPagina(PestanaEstadisticas.Clases, "Clases", true);
            Pagina grupos = NuevaPagina(PestanaEstadisticas.Grupos, "Grupos", true);
            Pagina docentes = NuevaPagina(PestanaEstadisticas.Docentes, "Docentes", true);
            Pagina laboratorios = NuevaPagina(PestanaEstadisticas.Laboratorios, "Laboratorios", false);
            Pagina equipos = NuevaPagina(PestanaEstadisticas.Equipos, "Equipos", true);

            // Estudiantes.
            tEstudiantes = CrearTablaEstudiantes();
            estudiantes.Agregar(tEstudiantes.Grid);
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
            estudiantes.Agregar(fichaEstudiante);
            tEstudiantes.Elegida += () => MostrarEstudiante(tEstudiantes.Elegido);
            tEstudiantes.Abrir += VerSusBitacoras;

            // Clases.
            tClases = CrearTablaClases();
            clases.Agregar(tClases.Grid);
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
            clases.Agregar(fichaClase);
            tClases.Elegida += () => MostrarClase(tClases.Elegido);

            // Grupos.
            tGrupos = CrearTablaGrupos();
            grupos.Agregar(tGrupos.Grid);
            grupos.Tabla = tGrupos;
            tGrupos.Abrir += g => IrAClasesCon(0, g.GrupoId);

            // Docentes: arriba (o a la izquierda) los docentes; debajo, sus clases dia por dia.
            tDocentes = CrearTablaDocentes();
            docentes.Agregar(tDocentes.Grid);
            docentes.Tabla = tDocentes;
            fichaDocente = new Ficha(BordeFicha) { Vacio = "Elige a un docente para ver sus clases día por día.", Font = fuenteValor };
            lblNombreDocente = TextoDeFicha(fichaDocente, fuenteNombre, Color.DarkGreen);
            lblResumenDocente = TextoDeFicha(fichaDocente, fuenteLinea, TextoOscuro);
            tSusClasesDia = CrearTablaSusClasesDia();
            fichaDocente.Contenido.Controls.Add(tSusClasesDia.Grid);
            fichaDocente.Contenido.Resize += (s, e) => AcomodarFichaDocente();
            docentes.Agregar(fichaDocente);
            tDocentes.Elegida += () => MostrarDocente(tDocentes.Elegido);
            tDocentes.Abrir += d => IrAClasesCon(d.DocenteId, 0);
            tSusClasesDia.Abrir += AbrirBitacora;

            // Laboratorios y Equipos.
            tLaboratorios = CrearTablaLaboratorios();
            laboratorios.Agregar(tLaboratorios.Grid);
            laboratorios.Tabla = tLaboratorios;
            tLaboratorios.Abrir += l => IrAEquiposDe(l.EsTodos ? 0 : l.LaboratorioId);
            tEquipos = CrearTablaEquipos();
            equipos.Agregar(tEquipos.Grid);
            equipos.Tabla = tEquipos;

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
            }

            btnActualizar = BotonAbajo("Actualizar", 120);
            btnActualizar.Click += (s, e) => Leer(true);
            ayuda.SetToolTip(btnActualizar, "Vuelve a leer las estadísticas de la base de datos (F5).");
            // Con Tab, de izquierda a derecha como se ven (se crearon de derecha a izquierda).
            btnActualizar.TabIndex = 10;
            if (btnCsv != null)
            {
                btnCsv.TabIndex = 11;
            }

            cerrar.TabIndex = 12;

            esperaCalculo.Tick += (s, e) =>
            {
                esperaCalculo.Stop();
                if (calculando)
                {
                    lblResumen.Text = TextoCalculando;
                }
            };

            PonerCombosIniciales();
            foreach (CifraGrande cifra in cifras)
            {
                cifra.Leyendo = true;
            }

            construida = true;
            Acomodar();
            PestanaCambiada();
        }

        // =====================================================================
        // Lo que ve E5 y las pruebas
        // =====================================================================

        /// <summary>La pestana que se ve.</summary>
        internal PestanaEstadisticas PestanaElegida
        {
            get { return tabs.SelectedIndex < 0 ? PestanaEstadisticas.Estudiantes : (PestanaEstadisticas)tabs.SelectedIndex; }
        }

        /// <summary>Lo ultimo calculado (null mientras no hay nada).</summary>
        internal ResultadoEstadisticas Resultado
        {
            get { return resultado; }
        }

        /// <summary>Elige una pestana (la usan las cifras y el doble clic de las tablas).</summary>
        internal void ElegirPestana(PestanaEstadisticas pestana)
        {
            tabs.SelectedIndex = (int)pestana;
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
            t.TextoSinFilas = () => TextoSinFilas("Nadie registró su bitácora en las clases de este periodo.");
            t.SinFilasEsError = SinFilasPorError;
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
            AgregarColumnasDeAsistencia(t);
            return t;
        }

        /// <summary>Entro (con su barra), % y la columna del 80 %, comunes a "Sus clases" y "Estudiantes de la clase".</summary>
        private void AgregarColumnasDeAsistencia(Tabla<ParDeAsistencia> t)
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
                TituloSegun = amplia => resultado == null ? "Derecho (80 %)" : amplia ? resultado.TituloDerecho : resultado.TituloDerechoCorto,
                Texto = p => p.TextoDerecho(resultado != null && resultado.CicloEntero), Clave = p => p.Veces == 0 ? null : (IComparable)(p.Bajo ? 0 : 1),
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
            t.TextoSinFilas = () => TextoSinFilas("No hay clases en este periodo con estos filtros.");
            t.SinFilasEsError = SinFilasPorError;
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
        private static void AgregarColumnasDeClasesDadas<T>(Tabla<T> t, Func<T, int> seDieron, Func<T, int> queContaban, Func<T, int> sinRegistro,
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
            AgregarColumnasDeAsistencia(t);
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
            t.TextoSinFilas = () => TextoSinFilas("No hay grupos con clases en este periodo.");
            t.SinFilasEsError = SinFilasPorError;
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
            t.TextoSinFilas = () => TextoSinFilas("No hay docentes con clases en este periodo.");
            t.SinFilasEsError = SinFilasPorError;
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
                Existe = () => resultado != null && resultado.ConDadas,
                Texto = d => EstadisticasDatos.Miles(d.AsistenciasQueDio), Clave = d => d.AsistenciasQueDio,
                AyudaDelTitulo = "Asistencias que dio en sus clases del periodo a quien no pudo registrar su bitácora (por ejemplo, su equipo no tenía internet). Las anuladas no cuentan."
            });
            t.Agregar(new Columna<FilaDocente>
            {
                Nombre = "reportes", Titulo = "Sus reportes", Ancho = 85, Derecha = true, PrimeroDescendente = true,
                Existe = () => resultado != null && resultado.ConReportesDocentes,
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

        private static int OrdenDeEstado(ClaseDiaDocente d)
        {
            return d.SinRegistro ? 0 : d.Estado == EstadoEnCalendario.SinBitacorasConAsistencia ? 1 : d.Estado == EstadoEnCalendario.ConBitacoras ? 2 : d.Cancelada ? 3 : 4;
        }

        /// <summary>Los colores de Faltas: verde si se dio con bitacoras, ambar con asistencias dadas, rojo sin registro y gris lo demas.</summary>
        private static Color? FondoDeEstado(ClaseDiaDocente d)
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

        private static Color? TextoDeEstado(ClaseDiaDocente d)
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
                ClaveDeFila = l => (l.EsTodos ? "todos" : l.LaboratorioId.ToString(Inv)),
                Arriba = l => l.EsTodos,
                FilaNegrita = l => l.EsTodos,
                FondoDeFila = l => l.EsTodos ? FondoTodos : (Color?)null,
                FilaGris = l => l.AunNo,
                AyudaDeFila = AyudaDeLaboratorio,
                ElegirLaUnica = false
            };
            t.TextoSinFilas = () => TextoSinFilas("");
            t.SinFilasEsError = SinFilasPorError;
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
            AgregarColumnasDeFallas(t, l => l.Fallas, l => l.Hardware, l => l.Software, l => l.Red, l => l.DeDocentes, false);
            t.Agregar(new Columna<FilaLaboratorio>
            {
                Nombre = "sininternet", Titulo = "Equipos sin internet", Ancho = 90, Derecha = true, PrimeroDescendente = true,
                Existe = () => resultado != null && resultado.ConAvisos,
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

            partes.Add(l.EsTodos ? "Doble clic para ver los equipos de todos los laboratorios." : "Doble clic para ver sus equipos.");
            return string.Join("\n", partes);
        }

        /// <summary>Fallas reportadas, Hardware, Software, Red y De docentes (db/15), en Laboratorios y Equipos.</summary>
        private void AgregarColumnasDeFallas<T>(Tabla<T> t, Func<T, int> fallas, Func<T, int> hardware, Func<T, int> software, Func<T, int> red,
            Func<T, int> deDocentes, bool docentesSoloAmplia) where T : class
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
                Existe = () => resultado != null && resultado.ConReportesDocentes,
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
            t.TextoSinFilas = () => TextoSinFilas("Ningún equipo tiene fallas ni avisos en este periodo.");
            t.SinFilasEsError = SinFilasPorError;
            t.Agregar(new Columna<FilaEquipo> { Nombre = "laboratorio", Titulo = "Laboratorio", Rellena = true, Ancho = 150, Peso = 55, Texto = e => e.Laboratorio, Clave = e => e.Laboratorio });
            t.Agregar(new Columna<FilaEquipo>
            {
                Nombre = "equipo", Titulo = "Equipo", Ancho = 130, Texto = e => e.Equipo, Clave = e => e.Equipo,
                ColorTexto = e => e.Tipo == TipoFilaEquipo.Equipo ? (Color?)null : TextoGris
            });
            AgregarColumnasDeFallas(t, e => e.Fallas, e => e.Hardware, e => e.Software, e => e.Red, e => e.DeDocentes, true);
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
                Existe = () => resultado != null && resultado.ConAvisos,
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

        /// <summary>Lo que se pinta en una tabla sin filas: leyendo, el error o el vacio de esa pestana.</summary>
        private string TextoSinFilas(string vacio)
        {
            if (leyendoBase)
            {
                return TextoLeyendo;
            }

            if (error != null)
            {
                return error;
            }

            if (resultado == null)
            {
                return TextoLeyendo;
            }

            return resultado.TextoVacio ?? vacio;
        }

        /// <summary>Lo que se pinta sin filas es el error de la lectura (en rojo).</summary>
        private bool SinFilasPorError()
        {
            return !leyendoBase && error != null;
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

        private ComboBox Combo(int orden, string nombre)
        {
            ComboBox combo = EstiloHorarios.CrearCombo(Point.Empty, 200);
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 20;
            combo.MaxDropDownItems = 20;
            combo.TabIndex = orden;
            combo.AccessibleName = nombre;
            Controls.Add(combo);
            return combo;
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
            ComboBox combo = (ComboBox)sender;
            e.DrawBackground();
            if (e.Index >= 0)
            {
                TextRenderer.DrawText(e.Graphics, Convert.ToString(combo.Items[e.Index], Inv), e.Font,
                    new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), e.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }

            e.DrawFocusRectangle();
        }

        /// <summary>El cuadrito con el color del docente y su nombre (como el combo de Horarios, AdministrarHorarios.DibujarProfesorConColor).</summary>
        private void DibujarDocente(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0)
            {
                return;
            }

            OpcionCombo opcion = cboDocente.Items[e.Index] as OpcionCombo;
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

            TextRenderer.DrawText(e.Graphics, Convert.ToString(cboDocente.Items[e.Index], Inv), e.Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x, e.Bounds.Height),
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
            int yFiltros = baja ? 50 : 84;
            AcomodarFiltros(cliente.Width, yFiltros, amplia);

            int yCifras = yFiltros + 42;
            int anchoCifras = cliente.Width - 2 * Margen;
            int cada = (anchoCifras - 4 * Separacion) / 5;
            // Baja: los tres renglones de la cifra caben en 78 px (se centran solos); asi las tablas ganan una fila.
            int altoCifras = baja ? AltoCifrasBaja : CifraGrande.Alto;
            for (int i = 0; i < cifras.Length; i++)
            {
                int x = Margen + i * (cada + Separacion);
                cifras[i].SetBounds(x, yCifras, i == cifras.Length - 1 ? Margen + anchoCifras - x : cada, altoCifras);
            }

            int yTabs = yCifras + altoCifras + 10;
            disposicionBaja = baja;
            tabs.SetBounds(Margen, yTabs, cliente.Width - 2 * Margen, Math.Max(150, cliente.Height - 72 - yTabs));
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

        /// <summary>
        /// Periodo, Laboratorio, Docente y Grupo en una fila (sec. 6.2): 260, 230, 220 y
        /// 150 px; a 1920, 300, 300, 300 y 160. Si no cabe, Laboratorio y Docente
        /// encogen hasta 190 y despues todos (el nombre entero va en la ayuda).
        /// </summary>
        private void AcomodarFiltros(int ancho, int y, bool amplia)
        {
            Label[] etiquetas = { lblPeriodo, lblLaboratorio, lblDocente, lblGrupo };
            ComboBox[] combos = { cboPeriodo, cboLaboratorio, cboDocente, cboGrupo };
            int[] anchos = amplia || ancho >= 1600 ? new[] { 300, 300, 300, 160 } : new[] { 260, 230, 220, 150 };
            int disponible = ancho - 2 * Margen - etiquetas.Sum(l => l.PreferredSize.Width) - 4 * 6 - 3 * 18;
            Encoger(anchos, disponible, new[] { 1, 2 }, new[] { 0, 190, 190, 0 });
            Encoger(anchos, disponible, new[] { 0, 1, 2, 3 }, new[] { 210, 125, 125, 100 });

            int x = Margen;
            for (int i = 0; i < combos.Length; i++)
            {
                etiquetas[i].Location = new Point(x, y + 7);
                x += etiquetas[i].PreferredSize.Width + 6;
                combos[i].SetBounds(x, y + 3, anchos[i], combos[i].Height);
                x += anchos[i] + 18;
                AnchoDeLaLista(combos[i]);
            }
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

            pide += (combo == cboDocente ? 36 : 14) + SystemInformation.VerticalScrollBarWidth;
            combo.DropDownWidth = Math.Max(combo.Width, Math.Min(pide, 640));
        }

        /// <summary>Dentro de una pestana: la linea y el buscador arriba y debajo su tabla (y su detalle).</summary>
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

            int derecha = tam.Width - 8;
            if (pagina.Buscador != null)
            {
                pagina.Buscador.SetBounds(derecha - AnchoBuscador, disposicionBaja ? 3 : 5, AnchoBuscador, pagina.Buscador.Height);
                int anchoEtiqueta = pagina.EtiquetaBuscar.PreferredSize.Width;
                pagina.EtiquetaBuscar.Location = new Point(pagina.Buscador.Left - 8 - anchoEtiqueta, disposicionBaja ? 6 : 8);
                derecha = pagina.EtiquetaBuscar.Left - 16;
            }

            pagina.Linea.SetBounds(8, disposicionBaja ? 7 : 9, Math.Max(50, derecha - 8), 20);
            int y = disposicionBaja ? 34 : 38;
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
        /// Lee (o toma la lectura guardada si cubre el periodo) en segundo plano y
        /// calcula con los filtros elegidos. forzar = Actualizar (F5): olvida la guardada.
        /// </summary>
        private void Leer(bool forzar)
        {
            ++lecturas;
            leyendoBase = true;
            calculando = false;
            esperaCalculo.Stop();
            btnActualizar.Enabled = false;
            lblResumen.Text = TextoLeyendo;
            ayuda.SetToolTip(lblResumen, null);
            foreach (CifraGrande cifra in cifras)
            {
                cifra.Leyendo = true;
            }

            // Mientras lee no se ensena lo de otro periodo: las tablas dicen que lee.
            error = null;
            VaciarTablas();
            NotaFija = "";

            if (baseEnMarcha)
            {
                // Una sola lectura de la base a la vez: cambiar el periodo o un filtro
                // mientras lee (con la base remota, varios segundos) no lanza otra. Al
                // acabar la que va, se lanza la ultima pedida, casi siempre desde la
                // lectura guardada.
                otraPedida = true;
                otraForzada |= forzar;
                return;
            }

            LanzarLectura(forzar);
        }

        /// <summary>La lectura de la base en Task.Run, con el periodo y los filtros que se ven ahora.</summary>
        private void LanzarLectura(bool forzar)
        {
            int numero = lecturas;
            baseEnMarcha = true;
            PeriodoElegido pedido = periodo;
            int laboratorio = IdElegido(cboLaboratorio, ultimoLaboratorio);
            int docente = IdElegido(cboDocente, ultimoDocente);
            int grupo = IdElegido(cboGrupo, ultimoGrupo);
            Task.Run(() =>
            {
                LecturaEstadisticas leida = EstadisticasDatos.Leer(pedido, forzar);
                FiltroEstadisticas filtro = EstadisticasDatos.Resolver(pedido, leida, laboratorio, docente, grupo);
                return Tuple.Create(leida, EstadisticasDatos.Calcular(leida, filtro));
            }).ContinueWith(t => EnLaVentana(() =>
            {
                baseEnMarcha = false;
                if (otraPedida)
                {
                    // Mientras leia se pidio otra (otro periodo o filtro): esta ya no vale.
                    bool forzada = otraForzada;
                    otraPedida = false;
                    otraForzada = false;
                    LanzarLectura(forzada);
                    return;
                }

                Recibir(numero, t);
            }, t));
        }

        /// <summary>Otro laboratorio, docente o grupo: solo se vuelve a calcular (sin base), en segundo plano.</summary>
        private void Recalcular()
        {
            if (lectura == null || leyendoBase)
            {
                Leer(false);
                return;
            }

            int numero = ++lecturas;
            calculando = true;
            esperaCalculo.Stop();
            esperaCalculo.Start();
            LecturaEstadisticas leida = lectura;
            PeriodoElegido pedido = periodo;
            int laboratorio = IdElegido(cboLaboratorio, 0);
            int docente = IdElegido(cboDocente, 0);
            int grupo = IdElegido(cboGrupo, 0);
            Task.Run(() =>
            {
                FiltroEstadisticas filtro = EstadisticasDatos.Resolver(pedido, leida, laboratorio, docente, grupo);
                return Tuple.Create(leida, EstadisticasDatos.Calcular(leida, filtro));
            }).ContinueWith(t => EnLaVentana(() =>
            {
                // La lectura no cubre el periodo (no deberia pasar al cambiar solo un filtro): se lee.
                if (t.IsFaulted && numero == lecturas && t.Exception.GetBaseException() is ArgumentException)
                {
                    Leer(false);
                    return;
                }

                Recibir(numero, t);
            }, t));
        }

        private void Recibir(int numero, Task<Tuple<LecturaEstadisticas, ResultadoEstadisticas>> tarea)
        {
            if (numero != lecturas || IsDisposed)
            {
                // Llego tarde: ya se pidio otra.
                return;
            }

            leyendoBase = false;
            calculando = false;
            esperaCalculo.Stop();
            btnActualizar.Enabled = true;
            if (tarea.IsFaulted || tarea.IsCanceled)
            {
                Exception ex = tarea.Exception == null ? new OperationCanceledException() : tarea.Exception.GetBaseException();
                Log.Error("Estadisticas: leer", ex);
                if (resultado != null && lectura != null)
                {
                    // Se sigue viendo lo leido, con su periodo y sus filtros.
                    error = null;
                    PonerResultado();
                    DateTime ahora = DateTime.Now + Desfase(lectura);
                    lblResumen.Text = "No se pudo actualizar a las " + ahora.ToString("HH:mm", Inv) + " (" + AvisoDeFalloAdmin.Causa(ex) + "): se ve lo leído a las "
                        + lectura.AhoraServidor.ToString("HH:mm", Inv) + ".";
                    ayuda.SetToolTip(lblResumen, AvisoDeFalloAdmin.Texto("No se pudieron leer las estadísticas", ex));
                    return;
                }

                error = AvisoDeFalloAdmin.Texto("No se pudieron leer las estadísticas", ex);
                resultado = null;
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

            lectura = tarea.Result.Item1;
            resultado = tarea.Result.Item2;
            error = null;
            PonerResultado();
        }

        private void VaciarTablas()
        {
            foreach (ITablaDisposicion tabla in Tablas())
            {
                tabla.Vaciar();
            }

            MostrarEstudiante(null);
            MostrarClase(null);
            MostrarDocente(null);
            AcomodarPagina(paginas[PestanaEstadisticas.Docentes]);
        }

        /// <summary>Lo calculado en la pantalla: combos, cifras, tablas, lineas y pie.</summary>
        private void PonerResultado()
        {
            ResultadoEstadisticas r = resultado;
            FiltroEstadisticas filtro = r.Filtro;
            PonerCombos(r);
            Recordar(filtro);

            CifraEstadistica[] cinco = { r.Cifras.Asistencia, r.Cifras.ClasesQueSeDieronCifra, r.Cifras.Bitacoras, r.Cifras.Fallas, r.Cifras.SinInternet };
            for (int i = 0; i < cifras.Length; i++)
            {
                cifras[i].Etiqueta = cinco[i].Etiqueta;
                cifras[i].Mostrar(cinco[i].Valor, ColorDe(cinco[i].Tono), cinco[i].Detalle, cinco[i].ParteRoja, cinco[i].Ayuda);
            }

            // Solo las columnas (las de db/10, db/15 y db/22 y los titulos): cada tabla se
            // llena una vez, con lo nuevo (Poner, y las de detalle al elegirse la fila).
            foreach (ITablaDisposicion tabla in Tablas())
            {
                tabla.Columnas(disposicionAmplia);
            }

            tEstudiantes.Poner(r.Estudiantes);
            tClases.Poner(r.Clases);
            tGrupos.Poner(r.Grupos);
            tDocentes.Poner(r.Docentes);
            tLaboratorios.Poner(r.Laboratorios);
            tEquipos.Poner(r.Equipos);

            PonerLinea(PestanaEstadisticas.Estudiantes, r.LineaEstudiantes);
            PonerLinea(PestanaEstadisticas.Clases, r.LineaClases);
            PonerLinea(PestanaEstadisticas.Grupos, r.LineaGrupos);
            PonerLinea(PestanaEstadisticas.Docentes, r.LineaDocentes + " Elige uno para ver sus clases día por día.");
            PonerLinea(PestanaEstadisticas.Laboratorios, r.LineaLaboratorios);
            PonerLinea(PestanaEstadisticas.Equipos, r.LineaEquipos);

            lblResumen.Text = r.LineaResumen;
            ayuda.SetToolTip(lblResumen, r.LineaResumen);
            PonerNota();
            AcomodarPagina(paginas[PestanaEstadisticas.Docentes]);
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

        /// <summary>La nota naranja de mas prioridad que aplica (la de Windows, solo en Equipos), la larga si cabe.</summary>
        private void PonerNota()
        {
            if (resultado == null)
            {
                NotaFija = "";
                return;
            }

            NotaEstadisticas nota = resultado.Notas.FirstOrDefault(n => !n.SoloEnEquipos || PestanaElegida == PestanaEstadisticas.Equipos);
            if (nota == null)
            {
                NotaFija = "";
                return;
            }

            bool cabe = nota.TextoLargo != null && TextRenderer.MeasureText(nota.TextoLargo, fuenteAviso).Width <= lblResumen.Width - 4;
            NotaFija = cabe ? nota.TextoLargo : nota.Texto;
        }

        private static TimeSpan Desfase(LecturaEstadisticas leida)
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
            return ultimoTipo == TipoPeriodo.Fechas && ultimoHasta >= ultimoDesde && ultimoDesde > DateTime.MinValue
                ? PeriodoElegido.EntreFechas(ultimoDesde, ultimoHasta)
                : PeriodoElegido.De(ultimoTipo == TipoPeriodo.Fechas ? TipoPeriodo.Ciclo : ultimoTipo);
        }

        private static void Recordar(FiltroEstadisticas filtro)
        {
            // "Este ciclo" que paso a otro se sigue pidiendo como "Este ciclo".
            if (!(ultimoTipo == TipoPeriodo.Ciclo && filtro.Tipo != TipoPeriodo.Fechas))
            {
                ultimoTipo = filtro.Tipo;
            }

            if (filtro.Tipo == TipoPeriodo.Fechas)
            {
                ultimoDesde = filtro.Desde;
                ultimoHasta = filtro.Hasta;
            }

            ultimoLaboratorio = filtro.Laboratorio;
            ultimoDocente = filtro.Docente;
            ultimoGrupo = filtro.Grupo;
        }

        private static int IdElegido(ComboBox combo, int siNoHay)
        {
            OpcionCombo opcion = combo.SelectedItem as OpcionCombo;
            return opcion == null ? siNoHay : opcion.Id;
        }

        /// <summary>Antes de la primera lectura: el periodo recordado y "Todos"; los combos se habilitan al leer.</summary>
        private void PonerCombosIniciales()
        {
            poniendoCombos = true;
            try
            {
                LlenarPeriodo(null, null);
                Llenar(cboLaboratorio, new[] { new OpcionCombo { Id = ultimoLaboratorio, Texto = ultimoLaboratorio == 0 ? "Todos los laboratorios" : TextoLeyendo } }, ultimoLaboratorio);
                Llenar(cboDocente, new[] { new OpcionCombo { Id = ultimoDocente, Texto = ultimoDocente == 0 ? "Todo el personal docente" : TextoLeyendo } }, ultimoDocente);
                Llenar(cboGrupo, new[] { new OpcionCombo { Id = ultimoGrupo, Texto = ultimoGrupo == 0 ? "Todos los grupos" : TextoLeyendo } }, ultimoGrupo);
            }
            finally
            {
                poniendoCombos = false;
            }
        }

        private void PonerCombos(ResultadoEstadisticas r)
        {
            poniendoCombos = true;
            try
            {
                LlenarPeriodo(lectura, r.Filtro);
                Llenar(cboLaboratorio, r.OpcionesLaboratorio, r.Filtro.Laboratorio);
                Llenar(cboDocente, r.OpcionesDocente, r.Filtro.Docente);
                Llenar(cboGrupo, r.OpcionesGrupo, r.Filtro.Grupo);
                foreach (ComboBox combo in new[] { cboPeriodo, cboLaboratorio, cboDocente, cboGrupo })
                {
                    AnchoDeLaLista(combo);
                }
            }
            finally
            {
                poniendoCombos = false;
            }
        }

        private void Llenar(ComboBox combo, IEnumerable<OpcionCombo> opciones, int elegido)
        {
            combo.BeginUpdate();
            EstiloHorarios.Rellenar(combo, opciones, elegido);
            if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            combo.EndUpdate();
            OpcionCombo opcion = combo.SelectedItem as OpcionCombo;
            ayuda.SetToolTip(combo, opcion == null ? null : opcion.Texto);
        }

        /// <summary>
        /// Las opciones del periodo (R2): "Este ciclo" (o "Ciclo anterior"), "Este mes",
        /// "Esta semana", "Ultimos 30 dias", las fechas elegidas (si las hay) y "Elegir
        /// fechas..." al final, que siempre abre el dialogo.
        /// </summary>
        private void LlenarPeriodo(LecturaEstadisticas leida, FiltroEstadisticas filtro)
        {
            TipoPeriodo elegido = filtro != null ? filtro.Tipo : periodo.Tipo;
            cboPeriodo.BeginUpdate();
            cboPeriodo.Items.Clear();
            foreach (TipoPeriodo tipo in EstadisticasDatos.OpcionesDePeriodo(leida).Where(t => t != TipoPeriodo.Fechas))
            {
                cboPeriodo.Items.Add(new OpcionPeriodo { Tipo = tipo, Texto = EstadisticasDatos.TextoDePeriodo(tipo, leida, null) });
            }

            if (elegido == TipoPeriodo.Fechas)
            {
                DateTime desde = filtro != null ? filtro.Desde : periodo.Desde;
                DateTime hasta = filtro != null ? filtro.Hasta : periodo.Hasta;
                cboPeriodo.Items.Add(new OpcionPeriodo { Tipo = TipoPeriodo.Fechas, Desde = desde, Hasta = hasta, Texto = EstadisticasDatos.DelAl(desde, hasta) });
            }

            cboPeriodo.Items.Add(new OpcionPeriodo { Tipo = TipoPeriodo.Fechas, ElegirFechas = true, Texto = "Elegir fechas..." });
            int indice = 0;
            for (int i = 0; i < cboPeriodo.Items.Count; i++)
            {
                OpcionPeriodo opcion = (OpcionPeriodo)cboPeriodo.Items[i];
                if (!opcion.ElegirFechas && opcion.Tipo == elegido)
                {
                    indice = i;
                    break;
                }
            }

            cboPeriodo.SelectedIndex = indice;
            cboPeriodo.EndUpdate();
            periodoAntes = indice;
            if (filtro != null)
            {
                DateTime primero = EstadisticasDatos.PrimerDiaElegible(leida.Hoy);
                DateTime desdeVisto = filtro.Desde < primero ? primero : filtro.Desde;
                ayuda.SetToolTip(cboPeriodo, EstadisticasDatos.DelAl(desdeVisto, filtro.Hasta) + (filtro.Hasta == leida.Hoy ? " (hoy)." : "."));
                periodo = filtro.Tipo == TipoPeriodo.Fechas ? PeriodoElegido.EntreFechas(filtro.Desde, filtro.Hasta)
                    : PeriodoElegido.De(periodo.Tipo == TipoPeriodo.Ciclo && filtro.Tipo != TipoPeriodo.Fechas ? TipoPeriodo.Ciclo : filtro.Tipo);
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
            periodo = opcion.Tipo == TipoPeriodo.Fechas ? PeriodoElegido.EntreFechas(opcion.Desde, opcion.Hasta) : PeriodoElegido.De(opcion.Tipo);
            ultimoTipo = periodo.Tipo;
            Leer(false);
        }

        /// <summary>"Elegir fechas...": el dialogo; con Cancelar vuelve la opcion de antes.</summary>
        private void ElegirFechas()
        {
            DateTime hoy = lectura != null ? lectura.Hoy : (DateTime.Now + Desfase(lectura)).Date;
            DateTime desde = resultado != null ? resultado.Filtro.Desde : hoy.AddDays(-6);
            DateTime hasta = resultado != null ? resultado.Filtro.Hasta : hoy;
            using (DialogoFechas dialogo = new DialogoFechas(hoy, desde, hasta))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    poniendoCombos = true;
                    cboPeriodo.SelectedIndex = periodoAntes >= 0 && periodoAntes < cboPeriodo.Items.Count ? periodoAntes : 0;
                    poniendoCombos = false;
                    return;
                }

                periodo = PeriodoElegido.EntreFechas(dialogo.Desde, dialogo.Hasta);
            }

            ultimoTipo = TipoPeriodo.Fechas;
            ultimoDesde = periodo.Desde;
            ultimoHasta = periodo.Hasta;
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

        private void FiltroCambiado()
        {
            ultimoLaboratorio = IdElegido(cboLaboratorio, 0);
            ultimoDocente = IdElegido(cboDocente, 0);
            ultimoGrupo = IdElegido(cboGrupo, 0);
            foreach (ComboBox combo in new[] { cboLaboratorio, cboDocente, cboGrupo })
            {
                OpcionCombo opcion = combo.SelectedItem as OpcionCombo;
                ayuda.SetToolTip(combo, opcion == null ? null : opcion.Texto);
            }

            Recalcular();
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

        /// <summary>Cada cifra elige su pestana y su orden (sec. 3.1).</summary>
        private void CifraPulsada(int cual)
        {
            switch (cual)
            {
                case 0:
                    ElegirPestana(PestanaEstadisticas.Estudiantes);
                    tEstudiantes.OrdenPorOmision();
                    tEstudiantes.Grid.Focus();
                    break;
                case 1:
                    ElegirPestana(PestanaEstadisticas.Docentes);
                    tDocentes.OrdenPorOmision();
                    tDocentes.Grid.Focus();
                    break;
                case 2:
                    ElegirPestana(PestanaEstadisticas.Laboratorios);
                    tLaboratorios.Ordenar("bitacoras", true);
                    tLaboratorios.Grid.Focus();
                    break;
                case 3:
                    ElegirPestana(PestanaEstadisticas.Equipos);
                    tEquipos.Ordenar("fallas", true);
                    tEquipos.Grid.Focus();
                    break;
                default:
                    ElegirPestana(PestanaEstadisticas.Equipos);
                    if (resultado != null && resultado.ConAvisos)
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
            ActualizarCsv();
            PonerNota();
            tabs.Invalidate();
        }

        /// <summary>Doble clic en un grupo o en un docente: lo elige en su filtro y pasa a Clases.</summary>
        private void IrAClasesCon(int docente, int grupo)
        {
            poniendoCombos = true;
            try
            {
                if (docente != 0)
                {
                    ElegirEnCombo(cboDocente, docente);
                }

                if (grupo != 0)
                {
                    ElegirEnCombo(cboGrupo, grupo);
                }
            }
            finally
            {
                poniendoCombos = false;
            }

            ElegirPestana(PestanaEstadisticas.Clases);
            FiltroCambiado();
        }

        /// <summary>Doble clic en un laboratorio: lo elige en el filtro y pasa a Equipos.</summary>
        private void IrAEquiposDe(int laboratorio)
        {
            poniendoCombos = true;
            try
            {
                ElegirEnCombo(cboLaboratorio, laboratorio);
            }
            finally
            {
                poniendoCombos = false;
            }

            ElegirPestana(PestanaEstadisticas.Equipos);
            FiltroCambiado();
        }

        // =====================================================================
        // Detalles
        // =====================================================================

        private void MostrarEstudiante(FilaEstudiante f)
        {
            if (f == null)
            {
                fichaEstudiante.ConDatos = false;
                fichaEstudiante.Vacio = leyendoBase || resultado == null ? "" : "Elige un estudiante para ver sus clases.";
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
                fichaClase.Vacio = leyendoBase || resultado == null ? "" : "Elige una clase para ver a sus estudiantes.";
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
                fichaDocente.Vacio = leyendoBase || resultado == null ? "" : "Elige a un docente para ver sus clases día por día.";
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

            // Mientras lee, las tablas estan vacias; mientras calcula, aun son las de los filtros de antes.
            if (leyendoBase || calculando)
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

        /// <summary>"Estadisticas Estudiantes 2026-10-22.csv", con la fecha del servidor.</summary>
        internal string NombreDelCsv(PestanaEstadisticas pestana)
        {
            return "Estadisticas " + NombreDePestana(pestana) + " " + (DateTime.Now + Desfase(lectura)).ToString("yyyy-MM-dd", Inv) + ".csv";
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
                default:
                    return "Equipos";
            }
        }

        /// <summary>
        /// El texto del CSV de esa pestana, o null si aun no hay nada leido: el
        /// membrete (sec. 8), una fila vacia y la tabla con todas sus columnas (las "X de
        /// N" en dos columnas, numeros sin separador de miles y "-" como celda vacia).
        /// Clases va en formato largo (una fila por clase y estudiante); Docentes lleva
        /// al final sus clases dia por dia.
        /// </summary>
        internal string ComoCsv(PestanaEstadisticas pestana)
        {
            // Mientras lee, las tablas estan vacias (resultado aun es el de antes).
            if (resultado == null || lectura == null || leyendoBase)
            {
                return null;
            }

            string separador = HojaAsistencia.SeparadorDeListas();
            StringBuilder csv = new StringBuilder();
            Action<IEnumerable<string>> renglon = campos => csv.Append(string.Join(separador, campos.Select(c => HojaAsistencia.CampoCsv(c, separador)))).Append("\r\n");
            ResultadoEstadisticas r = resultado;
            FiltroEstadisticas f = r.Filtro;
            DateTime primero = EstadisticasDatos.PrimerDiaElegible(lectura.Hoy);
            DateTime desde = f.Desde < primero ? primero : f.Desde;
            DateTime hasta = f.Hasta > lectura.Hoy ? lectura.Hoy : f.Hasta;

            renglon(new[] { "Estadísticas de DigBit" });
            renglon(new[] { "Pestaña: " + NombreDePestana(pestana) });
            renglon(new[] { "Periodo: del " + EstadisticasDatos.Fecha(desde) + " al " + EstadisticasDatos.Fecha(hasta) });
            renglon(new[] { "Laboratorio: " + (f.Laboratorio == 0 ? "Todos los laboratorios" : NombreDeLaboratorio(f.Laboratorio)) });
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
                    CsvEstudiantes(renglon);
                    break;
                case PestanaEstadisticas.Clases:
                    CsvClases(renglon);
                    break;
                case PestanaEstadisticas.Grupos:
                    CsvGrupos(renglon);
                    break;
                case PestanaEstadisticas.Docentes:
                    CsvDocentes(renglon, csv);
                    break;
                case PestanaEstadisticas.Laboratorios:
                    CsvLaboratorios(renglon);
                    break;
                default:
                    CsvEquipos(renglon);
                    break;
            }

            return csv.ToString();
        }

        private string NombreDeLaboratorio(int id)
        {
            LaboratorioLeido laboratorio;
            return lectura.Laboratorios.TryGetValue(id, out laboratorio) ? laboratorio.Nombre : TextoDeOpcion(resultado.OpcionesLaboratorio, id);
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

        private void CsvEstudiantes(Action<IEnumerable<string>> renglon)
        {
            bool conRetiro = lectura.Asistencia.ConRetiro;
            bool conDadas = resultado.ConDadas;
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

        private void CsvClases(Action<IEnumerable<string>> renglon)
        {
            // Formato largo (RF141, listo para tabla dinamica): una fila por clase y estudiante con dias que cuentan.
            renglon(new[] { "Materia", "Grupo", "Docente", "Matrícula", "Estudiante", "Entró", "Veces que contaban", "Asistencia (%)", resultado.TituloDerecho });
            foreach (FilaClase c in tClases.Visibles)
            {
                foreach (ParDeAsistencia p in c.Lista.Where(p => p.Veces > 0))
                {
                    renglon(new[] { c.Materia, c.Grupo, c.Docente, p.Matricula, p.Nombre, N(p.Entro), N(p.Veces), Pct(p.Porcentaje), p.TextoDerecho(resultado.CicloEntero) });
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

        private void CsvDocentes(Action<IEnumerable<string>> renglon, StringBuilder csv)
        {
            bool conDadas = resultado.ConDadas;
            bool conReportes = resultado.ConReportesDocentes;
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

        private void CsvLaboratorios(Action<IEnumerable<string>> renglon)
        {
            bool conReportes = resultado.ConReportesDocentes;
            bool conAvisos = resultado.ConAvisos;
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

        private void CsvEquipos(Action<IEnumerable<string>> renglon)
        {
            bool conReportes = resultado.ConReportesDocentes;
            bool conAvisos = resultado.ConAvisos;
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
        private static void VaciarPortapapelesDeDigBit()
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

        private static string Guion(string texto)
        {
            return string.IsNullOrWhiteSpace(texto) ? EstadisticasDatos.SinDato : texto;
        }

        /// <summary>Para ordenar: null (al final) si esta vacio.</summary>
        private static IComparable Vacio(string texto)
        {
            return string.IsNullOrWhiteSpace(texto) ? null : texto;
        }

        /// <summary>X / N para ordenar, o null con N = 0 (al final).</summary>
        private static IComparable Fraccion(int x, int n)
        {
            return n <= 0 ? null : (IComparable)((double)x / n);
        }

        private static string Unir(params string[] partes)
        {
            List<string> conTexto = partes.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return conTexto.Count == 0 ? null : string.Join("\n", conTexto);
        }

        // =====================================================================
        // Clases de apoyo
        // =====================================================================

        /// <summary>Una opcion del combo Periodo.</summary>
        private sealed class OpcionPeriodo
        {
            public TipoPeriodo Tipo;
            public DateTime Desde;
            public DateTime Hasta;
            /// <summary>"Elegir fechas...": abre el dialogo.</summary>
            public bool ElegirFechas;
            public string Texto = "";

            public override string ToString()
            {
                return Texto;
            }
        }

        /// <summary>Lo de cada pestana: su linea de arriba, su buscador y su tabla principal.</summary>
        private sealed class Pagina
        {
            public PestanaEstadisticas Cual;
            public TabPage Tab;
            public Label Linea;
            public Label EtiquetaBuscar;
            public TextBox Buscador;
            public Timer Espera;
            public ITablaDisposicion TablaBase;

            public ITablaBuscable Tabla
            {
                get { return TablaBase as ITablaBuscable; }
                set { TablaBase = value; }
            }

            public void Agregar(Control control)
            {
                Tab.Controls.Add(control);
            }
        }

        /// <summary>Lo que la ventana hace igual con todas las tablas, sin importar su tipo de fila.</summary>
        private interface ITablaDisposicion
        {
            /// <summary>Las columnas de esa disposicion y vuelve a llenar (la ayuda de cada fila cambia con ella).</summary>
            void Disposicion(bool amplia);

            /// <summary>Solo las columnas que se ven y sus titulos, sin volver a llenar.</summary>
            void Columnas(bool amplia);

            void Vaciar();
        }

        private interface ITablaBuscable : ITablaDisposicion
        {
            DataGridView Grid { get; }

            void Buscar(string texto);
        }

        /// <summary>Un recuadro de detalle (a la derecha o debajo de su tabla) con borde suave y su texto cuando no hay nada elegido.</summary>
        private sealed class Ficha : Panel
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
        private sealed class Rotulo
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
        private sealed class Columna<T> where T : class
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
        private sealed class Tabla<T> : ITablaBuscable where T : class
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
                grid.RowTemplate.Height = 26;
                // Un titulo largo ("Fallas reportadas") pasa a dos lineas en vez de cortarse, como PantallaAdmin.CrearLista.
                grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
                grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
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

        /// <summary>"Elegir fechas" (sec. 6.4): del y al, de los ultimos 400 dias hasta hoy (fecha del servidor).</summary>
        private sealed class DialogoFechas : Form
        {
            private readonly DateTimePicker dtpDesde;
            private readonly DateTimePicker dtpHasta;
            private readonly Label lblError;
            private readonly DateTime hoy;
            private readonly DrawingFont fuenteNota = new DrawingFont("Century Gothic", 9F);

            public DialogoFechas(DateTime hoy, DateTime desde, DateTime hasta)
            {
                this.hoy = hoy.Date;
                EstiloHorarios.PrepararDialogo(this, "Elegir fechas", new Size(520, 252));
                Controls.Add(EstiloAdmin.TituloDialogo("Elegir fechas", 472));
                DateTime primero = EstadisticasDatos.PrimerDiaElegible(this.hoy);
                Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = fuenteNota,
                    ForeColor = TextoGris,
                    Location = new Point(24, 50),
                    Size = new Size(472, 36),
                    Text = "Las dos fechas cuentan. Puedes elegir de los últimos 400 días: desde el " + EstadisticasDatos.Fecha(primero) + " hasta hoy."
                });

                Controls.Add(EstiloHorarios.CrearEtiqueta("Del", new Point(24, 96)));
                dtpDesde = EstiloHorarios.CrearFechaConDia(this, new Point(24, 119));
                Controls.Add(EstiloHorarios.CrearEtiqueta("al", new Point(272, 96)));
                dtpHasta = EstiloHorarios.CrearFechaConDia(this, new Point(272, 119));
                foreach (DateTimePicker fecha in new[] { dtpDesde, dtpHasta })
                {
                    fecha.MinDate = primero;
                    fecha.MaxDate = this.hoy;
                }

                dtpDesde.Value = Recortar(desde, primero);
                dtpHasta.Value = Recortar(hasta, primero);
                dtpDesde.AccessibleName = "Del";
                dtpHasta.AccessibleName = "al";
                lblError = EstiloAdmin.LineaError(new Point(24, 156), 472, 22);
                Controls.Add(lblError);
                dtpDesde.ValueChanged += (s, e) => lblError.Text = "";
                dtpHasta.ValueChanged += (s, e) => lblError.Text = "";
                EstiloAdmin.BotonesDialogo(this, "Aceptar", Aceptar);
                ActiveControl = dtpDesde;
            }

            public DateTime Desde
            {
                get { return dtpDesde.Value.Date; }
            }

            public DateTime Hasta
            {
                get { return dtpHasta.Value.Date; }
            }

            private DateTime Recortar(DateTime dia, DateTime primero)
            {
                return dia.Date < primero ? primero : dia.Date > hoy ? hoy : dia.Date;
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
                }
            }
        }
    }
}
