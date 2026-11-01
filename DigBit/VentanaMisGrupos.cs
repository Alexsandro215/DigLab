using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// "Mis grupos" del panel del profesor. Arriba, una sola linea: el titulo, el
    /// grupo elegido con su materia, cuando se leyo su lista y los botones
    /// Copiar la lista, Guardar en Excel (CSV), Actualizar (o F5) y Cerrar; mientras
    /// dura la clase elegida, antes de ellos, "Dar asistencia..." (relleno de verde).
    /// "Copiar la lista" pone la hoja entera en el portapapeles para pegarla en
    /// Excel y lo dice unos segundos en el estado; "Guardar en Excel (CSV)..." la
    /// guarda en un archivo que Excel abre con doble clic (GuardarCsv). Ninguno de
    /// los dos se ofrece en kiosco (ni dialogos de archivos): alli el
    /// profesor no tiene donde pegarla y quien entra despues si (el alumno abre el
    /// escritorio en la misma sesion de Windows); al cerrar, el portapapeles se
    /// vacia por si copio con Ctrl+C (OnFormClosed). A la
    /// izquierda, sus grupos de este ciclo: cada uno con
    /// su horario, cuantos alumnos tiene, cuantos van sin derecho a evaluacion, su
    /// asistencia con un medidor y, si hoy le toca, "En curso", "Hoy 02:00" o
    /// "Terminó hoy". A la derecha, el tablero con las cuatro cifras del grupo
    /// elegido (TableroAsistencia) y, debajo, su lista de asistencia como hoja de
    /// Excel (HojaAsistencia). La lista se arma con quienes registran su bitacora
    /// (AsistenciaDatos); las cuentas salen de ResumenAsistencia, una vez por lectura.
    ///
    /// Un solo reloj de 30 s renueva las pastillas de la lista y la clase de hoy
    /// del tablero, y vuelve a leer la lista del grupo elegido (sin "Leyendo...")
    /// mientras uno de sus bloques de hoy esta en curso o cuando su clase de hoy
    /// cambia de estado (empieza, hace una pausa o termina): la columna de hoy
    /// aparece y se cierra sola. Mientras dura, el profesor puede quitar la
    /// asistencia a quien registro sin estar en el salon, o devolversela, y (con
    /// db/22) darla a quien esta en el salon pero no pudo registrar su bitacora
    /// (DialogoDarAsistencia), tambien a quien aun no esta en la lista, y quitar
    /// la que dio. Lo decide y lo guarda el servidor con su reloj; despues, la
    /// ficha del panel vuelve a contar (ResumenDeClase.Olvidar). En kiosco, cada
    /// cambio pide antes su contrasena si no la confirmo en los ultimos 2
    /// minutos (ConfirmarEnKiosco). Las
    /// tarjetas del tablero que se pulsan marcan a sus alumnos en la hoja.
    /// Tambien la abre "Lista de asistencia" de la tarjeta del codigo, con esa clase elegida.
    ///
    /// Con db/19, lo que el profesor eligio para cada clase (PersonalizacionClases):
    /// arriba de cada tarjeta, una banda de su color con su circulo, el grupo y el
    /// nombre que le puso; en la cabecera, el circulo de la elegida, y sobre la
    /// hoja, una cinta de su color. El circulo (con el raton, F2 o el menu del
    /// grupo) abre "Personalizar tu clase". Sin db/19 todo se ve como siempre.
    ///
    /// Dos tamanos: amplio desde 1600 x 860 de cliente (1920x1080) y compacto por
    /// debajo (1366x768 o la ventana restaurada). Todo se coloca a mano en
    /// Acomodar, sin Dock ni Anchor.
    /// </summary>
    internal class VentanaMisGrupos : Form
    {
        private const int SegundosEntreLecturas = 30;
        /// <summary>Lo que mide cada grupo en la lista: su tarjeta mide 8 menos.</summary>
        private const int AltoGrupo = 104;
        /// <summary>Con db/19: la banda del color de la clase arriba de cada tarjeta y su circulo.</summary>
        private const int AltoBanda = 46;
        private const int DiametroCirculo = 34;
        /// <summary>Lo menos que mide el estado: con su texto mas largo (TextosDeEstado), la materia de la cabecera se corta antes y nada se mueve.</summary>
        private const int AnchoEstadoMinimo = 200;
        /// <summary>Lo que dura un aviso en el estado ("Lista copiada..."); despues vuelve lo de antes.</summary>
        private const int SegundosDelAviso = 5;
        private const string TextoCopiada = "Lista copiada: pégala en Excel.";
        private const string TextoEsperaLista = "Espera a que se lea la lista.";
        private const string TextoSinLista = "Aún no hay lista que copiar.";
        private const string TextoSinListaQueGuardar = "Aún no hay lista que guardar.";
        private const string TextoSinAlumnosQueCopiar = "La lista aún no tiene estudiantes.";
        // Corto: era el texto mas ancho y fijaba el sitio del estado, que le quita a la
        // materia de la cabecera a 1366 (por que, en su ayuda).
        private const string TextoNoSeCopio = "No se copió: inténtalo de nuevo.";
        private const string TextoGuardada = "Lista guardada: ábrela con Excel.";
        private const string TextoDada = "Asistencia dada.";
        private const string TituloGuardarCsv = "Guardar en Excel (CSV)...";
        /// <summary>Todo lo que puede decir el estado (con la hora mas ancha): el mas largo fija su ancho.</summary>
        private static readonly string[] TextosDeEstado =
        {
            "Leyendo tus grupos...", "Leyendo la lista...", "No se pudo leer la lista", "Actualizado a las 00:00",
            "No se pudo actualizar a las 00:00", TextoCopiada, TextoEsperaLista, TextoSinLista, TextoSinListaQueGuardar,
            TextoSinAlumnosQueCopiar, TextoNoSeCopio, TextoGuardada, TextoDada
        };
        private const string TextoNota = "Tus grupos de este ciclo. La lista de cada uno se arma con quienes registran su bitácora: "
            + "quien llega después entra con falta en las clases anteriores. F5 actualiza.";
        // Con db/22: tambien entra a la lista quien recibe del profesor la asistencia.
        private const string TextoNotaConDadas = "Tus grupos de este ciclo. La lista de cada uno se arma con quienes registran su bitácora "
            + "o reciben de ti la asistencia: quien llega después entra con falta en las clases anteriores. F5 actualiza.";
        /// <summary>En kiosco, cuanto vale la contrasena del profesor confirmada antes de cambiar una asistencia.</summary>
        private static readonly TimeSpan VigenciaDeLaContrasena = TimeSpan.FromMinutes(2);
        // La hora del estado es la del servidor; lo de cada 30 s va en su ayuda, no a la vista.
        private const string AyudaEstado = "Hora del servidor. Mientras dura la clase de hoy, la lista se vuelve a leer sola cada 30 segundos.";
        private const string AyudaEstadoEnCurso = "Hora del servidor. La clase de hoy sigue: la lista se vuelve a leer sola cada 30 segundos.";

        // Los textos se miden y se pintan sin el relleno de TextRenderer (como BotonCabecera).
        private const TextFormatFlags Linea = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        private const TextFormatFlags Renglon = Linea | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        private const TextFormatFlags Centrado = Linea | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        private const TextFormatFlags Parrafo = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        private static readonly Color FondoElegido = Color.FromArgb(236, 246, 236);
        private static readonly Color BordeSuave = Color.FromArgb(215, 222, 215);
        private static readonly Color TextoGris = Color.FromArgb(105, 105, 105);
        private static readonly Color TextoMateria = Color.FromArgb(40, 40, 40);
        private static readonly Color TextoError = Color.FromArgb(183, 28, 28);
        private static readonly Color CifraVerde = Color.FromArgb(46, 125, 50);
        private static readonly Color CifraRoja = Color.FromArgb(156, 0, 6);
        private static readonly Color SinDato = Color.FromArgb(150, 150, 150);
        private static readonly Color FondoHoy = Color.FromArgb(226, 242, 226);
        private static readonly Color FondoTermino = Color.FromArgb(236, 236, 236);
        private static readonly Color LetraTermino = Color.FromArgb(90, 90, 90);
        // Sobre la banda las pastillas van blancas con borde: su relleno de siempre no se distingue del color.
        private static readonly Color BordeTermino = Color.FromArgb(70, 70, 70);
        // El medidor de cada grupo: los colores de las barras de la columna Derecho de la hoja.
        private static readonly Color PistaMedidor = Color.FromArgb(236, 236, 236);
        private static readonly Color MedidorConDerecho = Color.FromArgb(99, 190, 123);
        private static readonly Color MedidorSinDerecho = Color.FromArgb(248, 105, 107);
        private static readonly Color MarcaMinimo = Color.FromArgb(70, 70, 70);
        private static readonly string[] DiasCortos = { "", "lun", "mar", "mié", "jue", "vie", "sáb", "dom" };

        private readonly int idProfesor;
        private readonly string profesor;
        private readonly Func<int, List<TramoHorario>> deHoy;
        private readonly Rotulo lblTitulo;
        private readonly Rotulo lblGrupo;
        private readonly Rotulo lblMateria;
        private readonly Rotulo lblEstado;
        private readonly Rotulo lblNota;
        private readonly ListBox lista;
        private readonly TableroAsistencia tablero;
        private readonly HojaAsistencia hoja;
        // "Dar asistencia...": la accion mas urgente durante la clase, en la cabecera antes
        // de Copiar la lista (en kiosco, antes de Actualizar). Solo mientras la clase elegida
        // esta en curso y la hoja deja darla (HojaAsistencia.PuedeDarSinLista).
        private readonly BotonCabecera btnDar;
        private bool verDar;
        private readonly BotonCabecera btnCopiar;
        // "Guardar en Excel (CSV)...": la hoja en un archivo (GuardarCsv). Como Copiar, nunca en kiosco.
        private readonly BotonCabecera btnGuardarCsv;
        private readonly BotonCabecera btnActualizar;
        private readonly BotonCabecera btnCerrar;
        // El circulo de la clase elegida (db/19), antes del grupo; oculto sin el.
        private readonly BotonCirculoClase btnCirculo;
        // El menu de un grupo: "Personalizar esta clase..." (clic derecho, Mayus+F10 o la tecla de menu).
        private readonly ContextMenuStrip menuGrupo;
        private readonly Timer reloj = new Timer { Interval = SegundosEntreLecturas * 1000 };
        // Quita el aviso del estado ("Lista copiada...") a los 5 s.
        private readonly Timer relojAviso = new Timer { Interval = SegundosDelAviso * 1000 };
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };
        private readonly int anchoEstado;

        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 16F, FontStyle.Bold);
        private readonly DrawingFont fuenteCabeceraGrupo = new DrawingFont("Century Gothic", 15F, FontStyle.Bold);
        private readonly DrawingFont fuenteCabeceraMateria = new DrawingFont("Century Gothic", 13F);
        private readonly DrawingFont fuenteEstado = new DrawingFont("Century Gothic", 9F);
        private readonly DrawingFont fuenteGrupo = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
        private readonly DrawingFont fuenteMateria = new DrawingFont("Century Gothic", 9.5F);
        private readonly DrawingFont fuenteDetalle = new DrawingFont("Century Gothic", 8.5F);
        private readonly DrawingFont fuenteDetalleNegrita = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private readonly DrawingFont fuentePorcentaje = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);
        private readonly DrawingFont fuentePastilla = new DrawingFont("Century Gothic", 8F, FontStyle.Bold);

        private List<GrupoDelProfesor> grupos = new List<GrupoDelProfesor>();
        // Reloj del servidor menos el de este equipo (para "En curso").
        private TimeSpan desfase = TimeSpan.Zero;
        // Cambia al volver a leer los grupos y al cerrar: la lectura de resumenes en curso se detiene.
        private volatile int generacionResumenes;
        private int? pedida;
        private bool leyendoGrupos;
        // Ya se leyeron los grupos una vez (antes, la nota de la lista no se ve: no salta).
        private bool gruposLeidos;
        private bool cambiando;
        // El constructor termino: Acomodar ya puede colocar.
        private bool listo;
        // Lecturas de la lista del grupo elegido: solo vale la ultima que se pidio.
        private int lecturas;
        private bool leyendoHoja;
        // La clase cuyos datos se ven en el tablero y la hoja (0: leyendo, con error o sin grupos).
        private int claseMostrada;
        // Como iba hoy la clase del grupo elegido en su ultima lectura (null: hay que volver a leerla).
        private EstadoHoy? estadoLeido;
        // Lo que pinta la lista (pastillas y cuentas): solo se repinta si cambia, sin parpadeo.
        private string firmaLista;
        private string ayudaLista;
        // El grupo de la lista con el raton sobre su circulo (-1: ninguno): lleva el anillo.
        private int circuloEncima = -1;
        // Lo que dice el estado (PonerEstado); mientras dura un aviso, se guarda y vuelve al quitarlo.
        private string estadoTexto = "";
        private bool estadoError;
        private string estadoDetalle;
        private bool conAviso;
        // En kiosco: cuando confirmo su contrasena por ultima vez (DateTime.UtcNow), o null.
        // Se olvida al cerrar la ventana (va con ella).
        private DateTime? contrasenaConfirmada;

        /// <summary>Como va hoy la clase de un grupo.</summary>
        private enum EstadoHoy
        {
            SinClase,
            Antes,
            EnCurso,
            Pausa,
            Termino
        }

        private sealed class GrupoDelProfesor
        {
            public ClaseInfo Clase;
            /// <summary>Sus horarios en la semana (dia, horas y laboratorio).</summary>
            public List<FranjaInfo> Franjas = new List<FranjaInfo>();
            /// <summary>"Lun y mié, 07:00 a 09:00": el renglon de su horario en la lista.</summary>
            public string Horario = "";
            /// <summary>"Lunes de 07:00 a 09:00 y miércoles de 07:00 a 09:00, en Laboratorio Redes.": para su ayuda.</summary>
            public string HorarioCompleto = "";
            /// <summary>La ultima asistencia leida.</summary>
            public AsistenciaDeClase Leida;
            /// <summary>Sus cuentas (ResumenAsistencia.De(Leida)): se calculan una vez, al leerla, no al pintar.</summary>
            public ResumenAsistencia Resumen;
            /// <summary>No se pudo leer y aun no hay nada leido: la lista lo dice en lugar de "Leyendo...".</summary>
            public bool NoSeLeyo;
        }

        /// <summary>Lo que el profesor eligio para sus clases (db/19), leido con los grupos.</summary>
        private sealed class PersonalLeido
        {
            /// <summary>PersonalizacionClases.Version al empezar a leer.</summary>
            public int Version;
            /// <summary>Falso si la consulta fallo: se queda lo de antes (no se publica).</summary>
            public bool Leido;
            /// <summary>Null sin db/19.</summary>
            public LecturaPersonal Lectura;
            public Dictionary<string, byte[]> Imagenes;
        }

        /// <param name="deHoy">Los bloques de hoy de una clase, en orden (vacia si hoy no le toca).</param>
        public VentanaMisGrupos(int idProfesor, string profesor, Func<int, List<TramoHorario>> deHoy)
        {
            this.idProfesor = idProfesor;
            this.profesor = profesor ?? "";
            this.deHoy = deHoy;

            ComponentResourceManager recursos = new ComponentResourceManager(typeof(PrincipalAdministrador));
            Icon = (Icon)recursos.GetObject("$this.Icon");
            BackColor = Color.White;
            Text = "Mis grupos";

            // La cabecera, una sola linea: el titulo, el grupo elegido, el estado y los botones.
            lblTitulo = new Rotulo { Font = fuenteTitulo, ForeColor = Color.DarkGreen, Text = "Mis grupos", TabIndex = 8 };
            lblGrupo = new Rotulo { Font = fuenteCabeceraGrupo, ForeColor = Color.DarkGreen, TabIndex = 9 };
            lblMateria = new Rotulo { Font = fuenteCabeceraMateria, ForeColor = TextoMateria, TabIndex = 10 };
            lblEstado = new Rotulo { Font = fuenteEstado, ForeColor = TextoGris, ALaDerecha = true, TabIndex = 11 };
            // El Text de los botones no cambia: es lo que buscan las pruebas.
            btnCirculo = new BotonCirculoClase(32) { TabIndex = 3, Visible = false };
            btnCirculo.Click += (s, e) => Personalizar(btnCirculo.ClaseId);
            // Lo mismo que el enlace de debajo de la hoja: buscar al estudiante por su matricula.
            // Tambien en kiosco (ahi pide antes la contrasena del docente: ConfirmarEnKiosco).
            btnDar = new BotonCabecera("Dar asistencia...") { TabIndex = 3, Visible = false, Principal = true };
            btnDar.Click += (s, e) => DarAsistencia(null);
            ayuda.SetToolTip(btnDar, "Para quien está en el salón y no pudo registrar su bitácora: lo buscas por su matrícula. "
                + "A quien ya está en la lista también se la das desde su casilla de hoy. Solo mientras dura la clase.");
            // En kiosco no se ofrece (ver la clase): nunca se ve.
            btnCopiar = new BotonCabecera("Copiar la lista") { TabIndex = 4, Visible = !Kiosco.Activo };
            btnCopiar.Click += (s, e) => CopiarLista();
            ayuda.SetToolTip(btnCopiar, "Copia la hoja entera, con sus encabezados, para pegarla en Excel (Ctrl+V). "
                + "Ctrl+C en la hoja copia solo las casillas que elijas.");
            // En kiosco tampoco: alli no hay dialogos de archivos (seria una salida al escritorio).
            btnGuardarCsv = new BotonCabecera(TituloGuardarCsv) { TabIndex = 5, Visible = !Kiosco.Activo };
            btnGuardarCsv.Click += (s, e) => GuardarCsv();
            ayuda.SetToolTip(btnGuardarCsv, "Guarda la hoja entera en un archivo que Excel abre con doble clic, "
                + "con la materia, el grupo y el periodo en las primeras líneas.");
            btnActualizar = new BotonCabecera("Actualizar") { TabIndex = 6 };
            btnActualizar.Click += (s, e) => Actualizar();
            ayuda.SetToolTip(btnActualizar, "Vuelve a leer tus grupos y la lista (F5).");
            btnCerrar = new BotonCabecera("Cerrar") { TabIndex = 7 };
            btnCerrar.Click += (s, e) => Close();
            CancelButton = btnCerrar;
            // F5 actualiza desde cualquier sitio de la ventana (OnKeyDown), como en Bitacoras.
            KeyPreview = true;

            lista = new ListBox
            {
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                IntegralHeight = false,
                ItemHeight = AltoGrupo,
                TabIndex = 0
            };
            lista.DrawItem += DibujarGrupo;
            lista.SelectedIndexChanged += (s, e) => GrupoElegido();
            lista.MouseMove += (s, e) => AyudaDeLaLista(e.Location);
            lista.MouseLeave += (s, e) =>
            {
                PonerCirculoEncima(-1);
                PonerAyudaDeLaLista(null);
            };
            // El circulo de cada grupo (db/19): clic izquierdo, F2 o su menu.
            lista.MouseClick += (s, e) => ClicEnLaLista(e);
            lista.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    MenuDelGrupo(e.Location);
                }
            };
            lista.KeyDown += TeclaEnLaLista;
            ToolStripMenuItem opcionPersonalizar = new ToolStripMenuItem("Personalizar esta clase...") { ShortcutKeyDisplayString = "F2" };
            opcionPersonalizar.Click += (s, e) => PersonalizarElegido();
            menuGrupo = new ContextMenuStrip { Font = fuenteMateria };
            menuGrupo.Items.Add(opcionPersonalizar);
            lblNota = new Rotulo { Font = fuenteDetalle, ForeColor = TextoGris, EnRenglones = true, Text = TextoNota, Visible = false, TabIndex = 12 };

            tablero = new TableroAsistencia { TabIndex = 1 };
            tablero.TarjetaPulsada += TarjetaDelTableroPulsada;

            // Oculta hasta saber que tiene grupos.
            hoja = new HojaAsistencia { TabIndex = 2, Visible = false };
            hoja.CambiarAsistencia += CambiarAsistencia;
            hoja.DarSinLista += () => DarAsistencia(null);
            hoja.MarcaQuitada += MarcaQuitadaEnLaHoja;

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                anchoEstado = Math.Max(AnchoEstadoMinimo, TextosDeEstado.Max(t => Medir(g, t, fuenteEstado).Width));
            }

            Controls.AddRange(new Control[] { lblTitulo, btnCirculo, lblGrupo, lblMateria, lblEstado, btnDar, btnCopiar, btnGuardarCsv, btnActualizar, btnCerrar, lista, lblNota, tablero, hoja });
            reloj.Tick += (s, e) => Tic();
            relojAviso.Tick += (s, e) => QuitarAviso();
            // Lo que se guarde aqui o en otra ventana (o llegue con otra lectura) se ve al momento.
            PersonalizacionClases.Cambio += PersonalizacionCambio;

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            // Con 1180 de ancho, cada tarjeta compacta del tablero mide al menos 200 px.
            MinimumSize = new Size(Math.Min(1180, area.Width - 20), Math.Min(600, area.Height - 20));
            ClientSize = new Size(Math.Min(1340, area.Width - 60), Math.Min(800, area.Height - 60));
            StartPosition = FormStartPosition.CenterScreen;
            // A pantalla completa (lo pidio el usuario): la lista se ve entera, como en Excel.
            WindowState = FormWindowState.Maximized;
            listo = true;
            Acomodar();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            reloj.Start();
            LeerGrupos();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Acomodar();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            reloj.Stop();
            relojAviso.Stop();
            // La lectura de resumenes que siga en curso ya no hace mas consultas.
            generacionResumenes++;
            PersonalizacionClases.Cambio -= PersonalizacionCambio;
            if (Kiosco.Activo)
            {
                // Lo que copio con Ctrl+C en la hoja no se queda para quien entre
                // despues (el alumno abre el escritorio en esta misma sesion).
                try
                {
                    Clipboard.Clear();
                }
                catch (ExternalException ex)
                {
                    Log.Aviso("Mis grupos: no se pudo vaciar el portapapeles al cerrar (kiosco): " + ex.Message);
                }
            }

            base.OnFormClosed(e);
        }

        /// <summary>F5, desde cualquier sitio de la ventana (KeyPreview): lo mismo que Actualizar.</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Handled && e.KeyCode == Keys.F5 && e.Modifiers == Keys.None)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Actualizar();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Tambien si se libera sin haberse cerrado. Las imagenes no: son de la cache comun.
                PersonalizacionClases.Cambio -= PersonalizacionCambio;
                reloj.Stop();
                reloj.Dispose();
                relojAviso.Stop();
                relojAviso.Dispose();
                ayuda.Dispose();
                menuGrupo.Dispose();
            }

            base.Dispose(disposing);
            if (disposing)
            {
                foreach (DrawingFont fuente in new[] { fuenteTitulo, fuenteCabeceraGrupo, fuenteCabeceraMateria, fuenteEstado, fuenteGrupo,
                    fuenteMateria, fuenteDetalle, fuenteDetalleNegrita, fuentePorcentaje, fuentePastilla })
                {
                    fuente.Dispose();
                }
            }
        }

        /// <summary>Lleva a esa clase (desde "Lista de asistencia" de la tarjeta). Si aun se leen los grupos, se elige al terminar.</summary>
        public void Elegir(int idClase)
        {
            int indice = grupos.FindIndex(g => g.Clase.Id == idClase);
            if (indice < 0)
            {
                pedida = idClase;
                return;
            }

            if (lista.SelectedIndex == indice)
            {
                GrupoElegido();
            }
            else
            {
                lista.SelectedIndex = indice;
            }
        }

        private GrupoDelProfesor Elegido
        {
            get { return lista.SelectedIndex >= 0 && lista.SelectedIndex < grupos.Count ? grupos[lista.SelectedIndex] : null; }
        }

        // =====================================================================
        // Medidas
        // =====================================================================

        /// <summary>
        /// Coloca todo a mano segun el tamano. Amplio (desde 1600 x 860 de cliente):
        /// margenes de 22, lista de 300 (336 con la banda de db/19) y tablero de 108
        /// de alto; compacto: 16, 260 y 84. La hoja va debajo del tablero hasta abajo;
        /// la lista de grupos, a la altura del tablero, con su nota debajo si cabe.
        /// </summary>
        private void Acomodar()
        {
            Size cliente = ClientSize;
            if (!listo || WindowState == FormWindowState.Minimized || cliente.Width <= 0 || cliente.Height <= 0)
            {
                return;
            }

            bool amplio = cliente.Width >= 1600 && cliente.Height >= 860;
            int margen = amplio ? 22 : 16;
            // En compacto no se ensancha: el membrete de la hoja cabe justo (HojaAsistencia.Acomodar).
            int anchoLista = amplio ? (PersonalizacionClases.Disponible ? 336 : 300) : 260;
            int x0 = margen + anchoLista + margen;
            int anchoDerecho = Math.Max(0, cliente.Width - x0 - margen);
            int arriba = amplio ? 56 : 48;
            int altoTablero = amplio ? 108 : 84;
            int abajo = cliente.Height - (amplio ? 12 : 8);

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                // Cerrar pegado al margen; Actualizar, Guardar en Excel (CSV) y Copiar la lista
                // 8 px antes cada uno, y el estado 12 px antes de Copiar (su sitio se guarda
                // aunque, sin grupos, no se vean). En kiosco Copiar y Guardar nunca se ven: el
                // estado va 12 px antes de Actualizar.
                int yBotones = amplio ? 12 : 8;
                btnCerrar.Location = new Point(cliente.Width - margen - btnCerrar.Width, yBotones);
                btnActualizar.Location = new Point(btnCerrar.Left - 8 - btnActualizar.Width, yBotones);
                btnGuardarCsv.Location = new Point(btnActualizar.Left - 8 - btnGuardarCsv.Width, yBotones);
                btnCopiar.Location = new Point(btnGuardarCsv.Left - 8 - btnCopiar.Width, yBotones);
                lblTitulo.SetBounds(margen, amplio ? 15 : 11, Medir(g, lblTitulo.Text, lblTitulo.Font).Width + 2, AltoDe(g, lblTitulo.Font));
                int primerBoton = Kiosco.Activo ? btnActualizar.Left : btnCopiar.Left;
                // Durante la clase, "Dar asistencia..." antes de los demas; el estado mide
                // entonces solo su texto de ahora (asi la materia no se queda en "..." a 1366).
                int anchoDelEstado = anchoEstado;
                if (verDar)
                {
                    btnDar.Location = new Point(primerBoton - 8 - btnDar.Width, yBotones);
                    primerBoton = btnDar.Left;
                    anchoDelEstado = Math.Min(anchoEstado, Medir(g, lblEstado.Text, lblEstado.Font).Width + 2);
                }

                btnDar.Visible = verDar;
                int inicioEstado = primerBoton - 12 - anchoDelEstado;
                // Va alineado a la derecha: 2 px de mas a la izquierda para que el texto mas largo nunca lleve "...".
                lblEstado.SetBounds(inicioEstado - 2, amplio ? 20 : 16, anchoDelEstado + 2, AltoDe(g, lblEstado.Font));
                // El circulo de la clase elegida (db/19), alineado con el tablero y con
                // los botones; el grupo y la materia, oficiales, 10 px despues.
                int xCabecera = x0;
                if (btnCirculo.Aspecto != null)
                {
                    btnCirculo.Location = new Point(x0 - CirculoClase.MargenAnillo, yBotones - CirculoClase.MargenAnillo);
                    xCabecera = x0 + btnCirculo.Diametro + 10;
                }

                ColocarCabecera(g, xCabecera, amplio ? 18 : 14, amplio ? 19 : 15, inicioEstado - 16);

                // El tablero y, debajo, la hoja hasta abajo.
                tablero.Amplio = amplio;
                tablero.SetBounds(x0, arriba, anchoDerecho, altoTablero);
                int yHoja = arriba + altoTablero + (amplio ? 16 : 10);
                hoja.SetBounds(x0, yHoja, anchoDerecho, Math.Max(0, abajo - yHoja));

                // La lista mide lo que sus grupos; si debajo caben 12 px y la nota, la nota
                // va ahi; si no, la nota se oculta y la lista usa todo el alto con su barra.
                int altoLibre = Math.Max(0, abajo - arriba);
                int contenido = lista.Items.Count * AltoGrupo;
                int separacion = contenido > 0 ? 12 : 0;
                int altoNota = AltoPartido(g, lblNota.Text, lblNota.Font, anchoLista);
                bool conNota = gruposLeidos && contenido + separacion + altoNota <= altoLibre;
                lista.SetBounds(margen, arriba, anchoLista, conNota ? contenido : altoLibre);
                lblNota.SetBounds(margen, arriba + contenido + separacion, anchoLista, altoNota);
                lblNota.Visible = conNota;
            }

            MostrarElegidoEnLista();
        }

        /// <summary>
        /// El grupo elegido y, a 10 px, su materia, que lleva "..." hasta 16 px antes
        /// de donde empieza el estado con su texto mas largo: asi nada se mueve. Lo
        /// que se corta va completo en su ayuda.
        /// </summary>
        private void ColocarCabecera(Graphics g, int x, int yGrupo, int yMateria, int limite)
        {
            int anchoGrupo = Medir(g, lblGrupo.Text, lblGrupo.Font).Width;
            int cabeGrupo = Math.Max(0, limite - x);
            lblGrupo.SetBounds(x, yGrupo, Math.Min(anchoGrupo + 2, cabeGrupo), AltoDe(g, lblGrupo.Font));
            int xMateria = x + Math.Min(anchoGrupo, cabeGrupo) + 10;
            int anchoMateria = Medir(g, lblMateria.Text, lblMateria.Font).Width;
            int cabeMateria = Math.Max(0, limite - xMateria);
            lblMateria.SetBounds(xMateria, yMateria, Math.Min(anchoMateria + 2, cabeMateria), AltoDe(g, lblMateria.Font));
            bool materiaCortada = anchoMateria > cabeMateria;
            ayuda.SetToolTip(lblMateria, materiaCortada ? lblMateria.Text : null);
            ayuda.SetToolTip(lblGrupo, anchoGrupo > cabeGrupo || materiaCortada ? lblGrupo.Text + ", " + lblMateria.Text : null);
        }

        /// <summary>El grupo elegido, a la vista aunque la lista se desplace.</summary>
        private void MostrarElegidoEnLista()
        {
            int elegido = lista.SelectedIndex;
            if (elegido < 0 || lista.ClientSize.Height <= 0)
            {
                return;
            }

            int visibles = Math.Max(1, lista.ClientSize.Height / AltoGrupo);
            if (elegido < lista.TopIndex)
            {
                lista.TopIndex = elegido;
            }
            else if (elegido >= lista.TopIndex + visibles)
            {
                lista.TopIndex = Math.Max(0, elegido - visibles + 1);
            }
        }

        /// <summary>Lo que mide un texto en una linea. Siempre con un Graphics: sin el, MeasureText no hace caso de NoPadding y da 7 px de mas.</summary>
        private static Size Medir(IDeviceContext g, string texto, DrawingFont fuente)
        {
            return string.IsNullOrEmpty(texto) ? Size.Empty : TextRenderer.MeasureText(g, texto, fuente, Size.Empty, Linea);
        }

        /// <summary>El alto de una linea de esa letra.</summary>
        private static int AltoDe(IDeviceContext g, DrawingFont fuente)
        {
            return TextRenderer.MeasureText(g, "Ág", fuente, Size.Empty, Linea).Height;
        }

        /// <summary>El alto de un texto partido en renglones de ese ancho.</summary>
        private static int AltoPartido(IDeviceContext g, string texto, DrawingFont fuente, int ancho)
        {
            return string.IsNullOrEmpty(texto) ? 0
                : TextRenderer.MeasureText(g, texto, fuente, new Size(Math.Max(1, ancho), int.MaxValue), Parrafo).Height;
        }

        // =====================================================================
        // Lectura
        // =====================================================================

        /// <summary>
        /// Sus clases activas de este ciclo (las que valen hoy, o terminaron hace
        /// menos de un mes: la lista final sigue a mano), por grupo y materia, con
        /// su horario.
        /// </summary>
        private void LeerGrupos()
        {
            if (leyendoGrupos)
            {
                return;
            }

            leyendoGrupos = true;
            generacionResumenes++;
            Cursor = Cursors.WaitCursor;
            QuitarMarca();
            claseMostrada = 0;
            tablero.MostrarLeyendo();
            PonerEstado("Leyendo tus grupos...", false, null);
            // Lo personal (db/19) solo es del profesor de la sesion; su Version, antes de leer.
            bool conPersonal = Datos_User.TipoUsuario == TiposUsuario.Profesor;
            int versionPersonal = PersonalizacionClases.Version;
            Task.Run(() =>
            {
                HorariosDatos datos = new HorariosDatos();
                DateTime ahora = new Consultas().AhoraServidor();
                // El reloj de este equipo en el mismo instante: el desfase no debe llevar lo que tarda el resto de la lectura.
                DateTime leidoEnRelojLocal = DateTime.Now;
                DateTime hoy = ahora.Date;
                List<FranjaInfo> franjas = datos.ObtenerFranjasDeProfesor(idProfesor);
                List<GrupoDelProfesor> leidos = datos.ObtenerClasesDeProfesor(idProfesor)
                    .Where(c => c.Activa && c.VigenteDesde.Date <= hoy && c.VigenteHasta.Date >= hoy.AddDays(-30))
                    .OrderBy(c => c.Grupo, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Materia, StringComparer.CurrentCultureIgnoreCase)
                    .Select(c => NuevoGrupo(c, HorarioDeAhora(franjas.Where(f => f.ClaseId == c.Id).ToList(), hoy)
                        .OrderBy(f => f.DiaSemana).ThenBy(f => f.Inicio).ToList()))
                    .ToList();
                PersonalLeido personal = conPersonal ? LeerPersonal(versionPersonal) : null;
                return Tuple.Create(ahora, leidos, leidoEnRelojLocal, personal, ConDadas());
            }).ContinueWith(tarea => EnLaVentana(() => GruposLeidos(tarea)));
        }

        /// <summary>
        /// En segundo plano: si la base guarda las asistencias que da el profesor
        /// (db/22), para la nota de la lista desde que se ve. Esquema lo recuerda por
        /// cuenta. Si falla, como sin ella: la nota se pone al dia con la lista.
        /// </summary>
        private static bool ConDadas()
        {
            try
            {
                using (MySql.Data.MySqlClient.MySqlConnection conexion = new Conexion().GetConexion())
                {
                    return Esquema.AsistenciaDada(conexion);
                }
            }
            catch (Exception ex)
            {
                Log.Aviso("Mis grupos: no se pudo ver si la base tiene db/22: " + ex.Message);
                return false;
            }
        }

        /// <summary>La nota de debajo de la lista de grupos, con db/22 o sin ella; si cambia, se vuelve a colocar.</summary>
        private void PonerNota(bool conDadas)
        {
            string nota = conDadas ? TextoNotaConDadas : TextoNota;
            if (lblNota.Text != nota)
            {
                lblNota.Text = nota;
                Acomodar();
            }
        }

        /// <summary>
        /// En segundo plano: lo que el profesor eligio para sus clases (db/19) y
        /// las imagenes que aun no estan en memoria. Si falla, Leido queda en falso
        /// y se queda lo que ya habia (los grupos se ven igual).
        /// </summary>
        private PersonalLeido LeerPersonal(int version)
        {
            PersonalLeido personal = new PersonalLeido { Version = version };
            try
            {
                using (MySql.Data.MySqlClient.MySqlConnection conexion = new Conexion().GetConexion())
                {
                    personal.Lectura = PersonalizacionClasesDatos.Leer(conexion, idProfesor);
                }

                personal.Leido = true;
            }
            catch (Exception ex)
            {
                Log.Error("Mis grupos: leer la personalizacion de las clases del profesor " + idProfesor, ex);
                return personal;
            }

            try
            {
                personal.Imagenes = PersonalizacionClases.TraerImagenes(idProfesor, personal.Lectura);
            }
            catch (Exception ex)
            {
                // Sin ellas van las iniciales; se piden otra vez en la siguiente lectura.
                Log.Error("Mis grupos: traer las imagenes de las clases del profesor " + idProfesor, ex);
            }

            return personal;
        }

        private void GruposLeidos(Task<Tuple<DateTime, List<GrupoDelProfesor>, DateTime, PersonalLeido, bool>> tarea)
        {
            leyendoGrupos = false;
            Cursor = Cursors.Default;
            if (tarea.IsFaulted)
            {
                Exception error = tarea.Exception.GetBaseException();
                Log.Error("Mis grupos: leer los grupos del profesor " + idProfesor, error);
                // La causa y que hacer (Actualizar lo vuelve a intentar); el detalle, al registro.
                tablero.MostrarError(MensajeDeFallo.Explicar("No se pudieron leer tus grupos", error, AvisoDeFalloDocente.SiSigue));
                PonerEstado("No se pudo leer la lista", true, null);
                return;
            }

            HoraDelServidor(tarea.Result.Item1, tarea.Result.Item3);
            // Antes de llenar la lista: si cambio algo, PersonalizacionCambio lo repinta.
            PersonalLeido personal = tarea.Result.Item4;
            if (personal != null && personal.Leido)
            {
                PersonalizacionClases.Publicar(personal.Lectura, personal.Imagenes, personal.Version);
            }

            int antes = Elegido == null ? 0 : Elegido.Clase.Id;
            grupos = tarea.Result.Item2;
            gruposLeidos = true;
            // Antes de colocar: la nota que se ve ya es la de esta base (Acomodar, abajo).
            lblNota.Text = tarea.Result.Item5 ? TextoNotaConDadas : TextoNota;
            lista.BeginUpdate();
            lista.Items.Clear();
            foreach (GrupoDelProfesor grupo in grupos)
            {
                lista.Items.Add(grupo.Clase.Grupo + " " + grupo.Clase.Materia);
            }

            lista.EndUpdate();
            firmaLista = null;
            Acomodar();
            if (grupos.Count == 0)
            {
                PonerCabecera(null);
                hoja.Visible = false;
                // Sin grupos no hay lista que copiar ni guardar; su sitio se queda (Acomodar).
                btnCopiar.Visible = false;
                btnGuardarCsv.Visible = false;
                tablero.MostrarSinGrupos();
                PonerEstado(Actualizado(tarea.Result.Item1), false, AyudaEstado);
                ActualizarBotonDar();
                return;
            }

            hoja.Visible = true;
            btnCopiar.Visible = !Kiosco.Activo;
            btnGuardarCsv.Visible = !Kiosco.Activo;
            // La que se pidio, la que se veia, la que esta en curso, la de hoy o la primera.
            int quiero = pedida ?? antes;
            pedida = null;
            int indice = grupos.FindIndex(g => g.Clase.Id == quiero);
            if (indice < 0)
            {
                indice = grupos.FindIndex(g => EnCurso(g));
            }

            if (indice < 0)
            {
                indice = grupos.FindIndex(g => BloquesDeHoy(g).Count > 0);
            }

            indice = Math.Max(0, indice);
            if (lista.SelectedIndex == indice)
            {
                GrupoElegido();
            }
            else
            {
                lista.SelectedIndex = indice;
            }

            LeerResumenes();
        }

        /// <summary>Otro grupo: su cabecera al momento y su lista, con "Leyendo..." en el tablero.</summary>
        private void GrupoElegido()
        {
            GrupoDelProfesor grupo = Elegido;
            lista.Invalidate();
            if (grupo == null)
            {
                return;
            }

            PonerCabecera(grupo);
            LeerHoja(grupo);
        }

        /// <summary>
        /// La asistencia de esa clase y sus cuentas, en segundo plano; si hoy le
        /// toca, su columna de hoy cuenta mientras dura. Callada (la del reloj o
        /// tras quitar una asistencia): sin "Leyendo...", lo que se ve se queda hasta
        /// que llega lo nuevo, y no se pide otra si ya se esta leyendo (salvo forzada).
        /// </summary>
        private void LeerHoja(GrupoDelProfesor grupo, bool callado = false, bool forzada = false)
        {
            if (callado && !forzada && leyendoHoja)
            {
                return;
            }

            int numero = ++lecturas;
            leyendoHoja = true;
            estadoLeido = EstadoDeHoy(grupo);
            if (grupo.NoSeLeyo)
            {
                grupo.NoSeLeyo = false;
                RepintarLista();
            }

            if (!callado)
            {
                // Nada se mueve: el tablero deja sus rotulos y la hoja conserva su caja.
                claseMostrada = 0;
                QuitarMarca();
                tablero.MostrarLeyendo();
                hoja.MostrarCargando(grupo.Clase.Materia, grupo.Clase.Grupo, profesor);
                PonerEstado("Leyendo la lista...", false, null);
                // Otro grupo (o Actualizar): hasta tener su lista, sin "Dar asistencia...".
                ActualizarBotonDar();
            }

            int idClase = grupo.Clase.Id;
            TramoHorario tramo = DiaDeHoy(grupo);
            TimeSpan? inicio = tramo == null ? (TimeSpan?)null : tramo.Inicio;
            TimeSpan? fin = tramo == null ? (TimeSpan?)null : tramo.Fin;
            Task.Run(() =>
            {
                AsistenciaDeClase leida = new AsistenciaDatos().DeLaClase(idClase, inicio, fin);
                return Tuple.Create(leida, ResumenAsistencia.De(leida));
            }).ContinueWith(tarea => EnLaVentana(() => HojaLeida(grupo, numero, callado, tarea)));
        }

        private void HojaLeida(GrupoDelProfesor grupo, int numero, bool callado, Task<Tuple<AsistenciaDeClase, ResumenAsistencia>> tarea)
        {
            // Si despues se pidio otra (otro grupo, Actualizar), esta ya no se muestra.
            bool ultima = numero == lecturas;
            if (ultima)
            {
                leyendoHoja = false;
            }

            if (tarea.IsFaulted)
            {
                Exception error = tarea.Exception.GetBaseException();
                Log.Error("Mis grupos: leer la asistencia de la clase " + grupo.Clase.Id, error);
                if (grupo.Leida == null && !grupo.NoSeLeyo)
                {
                    grupo.NoSeLeyo = true;
                    RepintarLista();
                }

                if (!ultima || Elegido != grupo)
                {
                    return;
                }

                if (callado && claseMostrada == grupo.Clase.Id)
                {
                    // Lo que se ve se queda; el siguiente tic lo intenta otra vez. Por que, en su ayuda.
                    estadoLeido = null;
                    PonerEstado("No se pudo actualizar a las " + (DateTime.Now + desfase).ToString("HH:mm", CultureInfo.InvariantCulture), true,
                        AvisoDeFalloDocente.EsAvisoPropio(error) ? error.Message
                            : MensajeDeFallo.Explicar("No se pudo actualizar la lista", error, AvisoDeFalloDocente.SiSigue));
                }
                else
                {
                    claseMostrada = 0;
                    // Un aviso de DigBit (la clase ya no existe) tal cual; si no, la causa y que hacer.
                    tablero.MostrarError(AvisoDeFalloDocente.EsAvisoPropio(error)
                        ? "No se pudo leer la asistencia. " + error.Message + " Pulsa Actualizar."
                        : MensajeDeFallo.Explicar("No se pudo leer la asistencia", error, AvisoDeFalloDocente.SiSigue));
                    PonerEstado("No se pudo leer la lista", true, null);
                }

                return;
            }

            grupo.Leida = tarea.Result.Item1;
            grupo.Resumen = tarea.Result.Item2;
            grupo.NoSeLeyo = false;
            HoraDelServidor(grupo.Leida.AhoraServidor, grupo.Leida.LeidoEnRelojLocal);
            RepintarLista();
            if (!ultima || Elegido != grupo)
            {
                return;
            }

            if (hoja.MenuAbierto)
            {
                // No se rehace la hoja debajo del menu: el siguiente tic la vuelve a leer.
                estadoLeido = null;
                return;
            }

            MostrarLeida(grupo);
        }

        /// <summary>
        /// Lo leido del grupo elegido en la hoja y el tablero (que solo se repinta si
        /// cambio algo). Si habia una tarjeta marcada, sus alumnos se recalculan con
        /// lo nuevo y se vuelven a marcar sin mover la hoja; si ya no queda nadie, sin marca.
        /// </summary>
        private void MostrarLeida(GrupoDelProfesor grupo)
        {
            // Cambiar asistencias, solo con un bloque de hoy en curso (no en el hueco entre dos).
            hoja.PuedeCambiar = EnCurso(grupo);
            hoja.Mostrar(grupo.Leida, profesor);
            PonerNota(grupo.Leida.ConDadas);
            int? marcada = tablero.Marcada;
            tablero.Mostrar(grupo.Resumen, BloquesDeHoy(grupo), Ahora);
            claseMostrada = grupo.Clase.Id;
            if (marcada.HasValue)
            {
                IList<int> ids = tablero.IdsDe(marcada.Value);
                if (ids != null && ids.Count > 0)
                {
                    hoja.MarcarAlumnos(ids, false);
                    if (tablero.Marcada != marcada)
                    {
                        tablero.Marcada = marcada;
                    }
                }
                else
                {
                    if (tablero.Marcada.HasValue)
                    {
                        tablero.Marcada = null;
                    }

                    hoja.QuitarMarca();
                }
            }

            PonerEstado(Actualizado(grupo.Leida.AhoraServidor), false, EnCurso(grupo) ? AyudaEstadoEnCurso : AyudaEstado);
            ActualizarBotonDar();
        }

        /// <summary>"Actualizado a las 15:23" (hora del servidor), como en el panel del profesor y en el del administrador.</summary>
        private static string Actualizado(DateTime servidor)
        {
            return "Actualizado a las " + servidor.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>El resumen de cada grupo de la lista (alumnos y asistencia), uno tras otro y en segundo plano.</summary>
        private void LeerResumenes()
        {
            // Sus bloques de hoy se toman aqui, en el hilo de la ventana (salen del panel del profesor).
            List<Tuple<GrupoDelProfesor, TimeSpan?, TimeSpan?>> pendientes = new List<Tuple<GrupoDelProfesor, TimeSpan?, TimeSpan?>>();
            foreach (GrupoDelProfesor grupo in grupos.Where(g => g.Leida == null && g != Elegido))
            {
                TramoHorario tramo = DiaDeHoy(grupo);
                pendientes.Add(Tuple.Create(grupo, tramo == null ? (TimeSpan?)null : tramo.Inicio, tramo == null ? (TimeSpan?)null : tramo.Fin));
            }

            if (pendientes.Count == 0)
            {
                return;
            }

            // Se deja de leer al cerrar la ventana o al volver a leer los grupos (Actualizar).
            int generacion = generacionResumenes;
            Task.Run(() =>
            {
                foreach (Tuple<GrupoDelProfesor, TimeSpan?, TimeSpan?> pendiente in pendientes)
                {
                    if (generacion != generacionResumenes)
                    {
                        return;
                    }

                    GrupoDelProfesor grupo = pendiente.Item1;
                    try
                    {
                        AsistenciaDeClase leida = new AsistenciaDatos().DeLaClase(grupo.Clase.Id, pendiente.Item2, pendiente.Item3);
                        ResumenAsistencia resumen = ResumenAsistencia.De(leida);
                        EnLaVentana(() =>
                        {
                            if (grupo.Leida == null)
                            {
                                grupo.Leida = leida;
                                grupo.Resumen = resumen;
                                grupo.NoSeLeyo = false;
                                RepintarLista();
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        // Solo es el resumen: al elegirlo se vuelve a leer y ahi se dice.
                        Log.Aviso("Mis grupos: resumen de la clase " + grupo.Clase.Id + ": " + ex.Message);
                        EnLaVentana(() =>
                        {
                            if (grupo.Leida == null && !grupo.NoSeLeyo)
                            {
                                grupo.NoSeLeyo = true;
                                RepintarLista();
                            }
                        });
                    }
                }
            });
        }

        /// <summary>
        /// Cada 30 s: las pastillas de la lista, la clase de hoy del tablero (sin
        /// leer la base) y, callada, la lista del grupo elegido si uno de sus bloques
        /// de hoy esta en curso o si su clase de hoy cambio de estado desde la ultima
        /// lectura. No con un menu abierto ni mientras se guarda un cambio.
        /// </summary>
        private void Tic()
        {
            RepintarLista();
            tablero.ActualizarHora(Ahora);
            // Al terminar el bloque, "Dar asistencia..." se va sin esperar a la lectura.
            ActualizarBotonDar();
            GrupoDelProfesor grupo = Elegido;
            if (grupo == null || leyendoGrupos || cambiando || hoja.MenuAbierto)
            {
                return;
            }

            EstadoHoy estado = EstadoDeHoy(grupo);
            // Tambien si la lista ya trae la columna de hoy (alguien registro antes de
            // empezar, o en la pausa entre dos bloques): sus pendientes pueden cambiar.
            bool conColumnaDeHoy = grupo.Leida != null && grupo.Leida.Dias.Any(d => d.EnCurso);
            if (estado == EstadoHoy.EnCurso || conColumnaDeHoy || estado != estadoLeido)
            {
                LeerHoja(grupo, true);
            }
        }

        /// <summary>Otra vez los grupos y los resumenes (y la lista del elegido).</summary>
        private void Actualizar()
        {
            LeerGrupos();
        }

        /// <summary>
        /// Lo que pidio el profesor en el menu de la casilla de hoy: quitar o
        /// devolver la asistencia (db/17), darla (db/22, con el dialogo) o quitar la
        /// que dio. Lo comprueba y lo guarda AsistenciaDatos con el reloj del
        /// servidor; en kiosco, antes, su contrasena (ConfirmarEnKiosco).
        /// </summary>
        private void CambiarAsistencia(AlumnoDeAsistencia alumno, DateTime fecha, AccionAsistencia accion)
        {
            GrupoDelProfesor grupo = Elegido;
            if (grupo == null || cambiando || alumno == null)
            {
                return;
            }

            if (accion == AccionAsistencia.Dar)
            {
                DarAsistencia(alumno);
                return;
            }

            int idClase = grupo.Clase.Id;
            if (!ConfirmarEnKiosco(idClase))
            {
                return;
            }

            if (accion == AccionAsistencia.QuitarLaQueDiste)
            {
                Guardar(grupo, () => new AsistenciaDatos().QuitarLaQueDiste(idClase, idProfesor, alumno.Id),
                    "Quitar la asistencia", "No se pudo quitar la asistencia", "quitar la asistencia dada al alumno " + alumno.Id);
                return;
            }

            bool quitar = accion == AccionAsistencia.Quitar;
            List<int> bitacoras;
            alumno.BitacorasDelDia.TryGetValue(fecha, out bitacoras);
            List<int> deHoyEnLaClase = bitacoras ?? new List<int>();
            Guardar(grupo, () => new AsistenciaDatos().CambiarAsistencia(idClase, idProfesor, alumno.Id, deHoyEnLaClase, quitar),
                quitar ? "Quitar la asistencia" : "Devolver la asistencia",
                quitar ? "No se pudo quitar la asistencia" : "No se pudo devolver la asistencia",
                (quitar ? "quitar" : "devolver") + " la asistencia del alumno " + alumno.Id);
        }

        /// <summary>
        /// Un cambio de asistencia en segundo plano (con el cursor de espera y sin
        /// releer la hoja debajo). Si no se pudo, por que (y que hacer) en un
        /// mensaje; despues, la lista otra vez y la ficha del panel vuelve a contar.
        /// </summary>
        private void Guardar(GrupoDelProfesor grupo, Func<string> cambio, string titulo, string queNoSePudo, string paraElLog)
        {
            cambiando = true;
            Cursor = Cursors.WaitCursor;
            Task.Run(cambio)
                .ContinueWith(tarea => EnLaVentana(() =>
                {
                    cambiando = false;
                    Cursor = Cursors.Default;
                    // La causa y que hacer; el detalle tecnico, al registro (abajo).
                    string problema = tarea.IsFaulted
                        ? MensajeDeFallo.Explicar(queNoSePudo, tarea.Exception.GetBaseException(), AvisoDeFalloDocente.SiSigue)
                        : tarea.Result;
                    if (tarea.IsFaulted)
                    {
                        Log.Error("Mis grupos: " + paraElLog, tarea.Exception.GetBaseException());
                    }

                    if (problema != null)
                    {
                        MessageBox.Show(this, problema, titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }

                    ListaCambiada(grupo);
                }));
        }

        /// <summary>
        /// Tras un cambio de asistencia: la ficha del panel del profesor vuelve a
        /// contar en su siguiente lectura y la lista se lee otra vez, callada, aunque
        /// ya se este leyendo (esa lectura pudo ser de antes del cambio).
        /// </summary>
        private void ListaCambiada(GrupoDelProfesor grupo)
        {
            ResumenDeClase.Olvidar();
            if (Elegido == grupo)
            {
                LeerHoja(grupo, true, true);
            }
        }

        /// <summary>
        /// Darle la asistencia de hoy (db/22) a ese alumno de la lista o, con null, a
        /// quien se busque por su matricula. En kiosco, antes su contrasena. Con el
        /// cursor de espera, PrepararDar dice si ahora se puede (la clase en curso,
        /// con el reloj del servidor) y trae sus equipos; si no, un mensaje con por
        /// que; si si, el dialogo, que lo guarda. Mientras esta abierto el reloj no
        /// relee la hoja. Dada, el estado lo dice 5 s y la lista se lee otra vez;
        /// sin darla, tambien se lee si el servidor dijo que no o la clase termino
        /// con el dialogo abierto (DialogoDarAsistencia.HayQueReleer).
        /// </summary>
        private void DarAsistencia(AlumnoDeAsistencia alumno)
        {
            GrupoDelProfesor grupo = Elegido;
            if (grupo == null || cambiando)
            {
                return;
            }

            int idClase = grupo.Clase.Id;
            if (!ConfirmarEnKiosco(idClase))
            {
                return;
            }

            cambiando = true;
            Cursor = Cursors.WaitCursor;
            // Por matricula, tambien si los estudiantes entran con el SIIA: a quien no tiene
            // cuenta no se le manda a "Crear cuenta" (con el SIIA no existe; se crea sola).
            bool porMatricula = alumno == null;
            Task.Run(() => Tuple.Create(new AsistenciaDatos().PrepararDar(idClase, idProfesor),
                    porMatricula && InicioConSiia.LeerSinFallar().Estudiantes))
                .ContinueWith(tarea => EnLaVentana(() =>
                {
                    Cursor = Cursors.Default;
                    string nombre = null;
                    bool releer = false;
                    try
                    {
                        if (tarea.IsFaulted)
                        {
                            Exception error = tarea.Exception.GetBaseException();
                            Log.Error("Mis grupos: preparar la asistencia que se da en la clase " + idClase, error);
                            MessageBox.Show(this, MensajeDeFallo.Explicar("No se pudo dar la asistencia", error, AvisoDeFalloDocente.SiSigue),
                                "Dar la asistencia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        PreparacionDar preparacion = tarea.Result.Item1;
                        bool estudiantesConSiia = tarea.Result.Item2;
                        if (preparacion.Problema != null)
                        {
                            MessageBox.Show(this, preparacion.Problema, "Dar la asistencia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            ListaCambiada(grupo);
                            return;
                        }

                        AsistenciaDeClase lista = grupo.Leida;
                        DiaDeAsistencia deHoy = lista == null ? null : lista.Dias.LastOrDefault(d => d.EnCurso);
                        DateTime hoy = deHoy != null ? deHoy.Fecha : (DateTime.Now + desfase).Date;
                        // Quien aun no esta en la lista entra con falta solo si ya hubo clases que cuentan.
                        bool conClasesAntes = lista != null && lista.Dias.Any(d => d.Fecha.Date < hoy.Date);
                        using (DialogoDarAsistencia dialogo = new DialogoDarAsistencia(grupo.Clase.Materia, grupo.Clase.Grupo, preparacion, hoy, alumno,
                            conClasesAntes, estudiantesConSiia, () => DateTime.Now + desfase,
                            matricula => new AsistenciaDatos().BuscarEstudiante(idClase, matricula),
                            id => grupo.Leida == null ? null : grupo.Leida.Alumnos.FirstOrDefault(a => a.Id == id),
                            (id, motivo, equipo, detalle) => new AsistenciaDatos().DarAsistencia(idClase, idProfesor, id, motivo, equipo, detalle)))
                        {
                            DialogResult resultado = dialogo.ShowDialog(this);
                            if (resultado != DialogResult.OK || IsDisposed)
                            {
                                // El servidor dijo que no (ya registro, la clase termino...) o la
                                // clase termino con el dialogo abierto: la lista, al dia ya.
                                releer = dialogo.HayQueReleer && !IsDisposed;
                                return;
                            }

                            nombre = dialogo.NombreDelEstudiante;
                        }
                    }
                    finally
                    {
                        cambiando = false;
                        if (releer && Elegido == grupo)
                        {
                            LeerHoja(grupo, true, true);
                        }
                    }

                    Avisar(TextoDada, Color.DarkGreen, "Le diste la asistencia de hoy a " + nombre + ": ya cuenta en la lista.");
                    ListaCambiada(grupo);
                }));
        }

        /// <summary>
        /// En un equipo del laboratorio (kiosco), antes de cualquier cambio de
        /// asistencia: la contrasena del profesor, si no la confirmo en los ultimos 2
        /// minutos (si se aleja con su sesion abierta, un estudiante no puede darse la
        /// asistencia). Tres equivocadas cierran la sesion (DialogoContrasenaAdmin).
        /// En su propio equipo no se pide. Mientras se pide, el reloj no relee la hoja.
        /// </summary>
        private bool ConfirmarEnKiosco(int idClase)
        {
            if (!Kiosco.Activo)
            {
                return true;
            }

            DateTime ahora = DateTime.UtcNow;
            if (contrasenaConfirmada.HasValue && ahora >= contrasenaConfirmada.Value && ahora - contrasenaConfirmada.Value < VigenciaDeLaContrasena)
            {
                return true;
            }

            bool confirmada;
            cambiando = true;
            try
            {
                confirmada = DialogoContrasenaAdmin.Confirmar(this, "¿Cambiar la asistencia desde este equipo del laboratorio?",
                    "Estás en un equipo de estudiantes. Para que nadie más cambie la asistencia con tu sesión abierta, escribe tu contraseña. "
                    + "Durante 2 minutos no te la vuelve a pedir.",
                    "Continuar", "Cambiar asistencia en kiosco, clase " + idClase, TiposUsuario.Profesor);
            }
            finally
            {
                cambiando = false;
            }

            if (!confirmada || IsDisposed)
            {
                return false;
            }

            contrasenaConfirmada = DateTime.UtcNow;
            return true;
        }

        /// <summary>En el hilo de la ventana, si sigue abierta.</summary>
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
        // La clase de hoy
        // =====================================================================

        /// <summary>La hora del servidor, estimada con la ultima lectura.</summary>
        private TimeSpan Ahora
        {
            get { return (DateTime.Now + desfase).TimeOfDay; }
        }

        /// <summary>El desfase con el servidor, con las dos horas tomadas en el mismo instante (como VentanaCodigo).</summary>
        private void HoraDelServidor(DateTime servidor, DateTime leidoEnRelojLocal)
        {
            desfase = servidor - leidoEnRelojLocal;
        }

        /// <summary>Sus bloques de hoy, en orden (vacia si hoy no le toca).</summary>
        private List<TramoHorario> BloquesDeHoy(GrupoDelProfesor grupo)
        {
            return deHoy(grupo.Clase.Id) ?? new List<TramoHorario>();
        }

        /// <summary>Uno de sus bloques de hoy esta en curso (no cuenta el hueco entre dos).</summary>
        private bool EnCurso(GrupoDelProfesor grupo)
        {
            TimeSpan ahora = Ahora;
            return BloquesDeHoy(grupo).Any(b => b.Inicio <= ahora && ahora < b.Fin);
        }

        /// <summary>Su proximo bloque de hoy que aun no empieza, o null.</summary>
        private TramoHorario Siguiente(GrupoDelProfesor grupo)
        {
            TimeSpan ahora = Ahora;
            return BloquesDeHoy(grupo).Where(b => b.Inicio > ahora).OrderBy(b => b.Inicio).FirstOrDefault();
        }

        /// <summary>
        /// Hoy entero para la lista: del primer inicio al ultimo fin. Su columna de
        /// hoy cuenta desde que empieza el primer bloque hasta que termina el
        /// ultimo (en el hueco entre dos, quien no ha registrado sigue pendiente).
        /// </summary>
        private TramoHorario DiaDeHoy(GrupoDelProfesor grupo)
        {
            List<TramoHorario> bloques = BloquesDeHoy(grupo);
            return bloques.Count == 0 ? null : new TramoHorario(bloques.Min(b => b.Inicio), bloques.Max(b => b.Fin));
        }

        /// <summary>Sin clase hoy, antes de empezar, con un bloque en curso, en la pausa entre dos o ya terminada.</summary>
        private EstadoHoy EstadoDeHoy(GrupoDelProfesor grupo)
        {
            List<TramoHorario> bloques = BloquesDeHoy(grupo);
            if (bloques.Count == 0)
            {
                return EstadoHoy.SinClase;
            }

            TimeSpan ahora = Ahora;
            if (bloques.Any(b => b.Inicio <= ahora && ahora < b.Fin))
            {
                return EstadoHoy.EnCurso;
            }

            if (ahora < bloques.Min(b => b.Inicio))
            {
                return EstadoHoy.Antes;
            }

            return ahora >= bloques.Max(b => b.Fin) ? EstadoHoy.Termino : EstadoHoy.Pausa;
        }

        // =====================================================================
        // Cabecera y marcas
        // =====================================================================

        /// <summary>El grupo y la materia de la cabecera (vacias sin grupo): cambian al elegir, antes de leer.</summary>
        private void PonerCabecera(GrupoDelProfesor grupo)
        {
            lblGrupo.Text = grupo == null ? "" : grupo.Clase.Grupo;
            lblMateria.Text = grupo == null ? "" : grupo.Clase.Materia;
            PonerPersonal(grupo);
            Acomodar();
        }

        /// <summary>
        /// Lo que el profesor eligio para la clase del grupo elegido (db/19): el
        /// circulo de la cabecera y la cinta de la hoja. Sin db/19 o sin grupo, ni
        /// circulo ni cinta, como siempre. Quien lo llama coloca despues (Acomodar).
        /// </summary>
        private void PonerPersonal(GrupoDelProfesor grupo)
        {
            AspectoClase aspecto = grupo == null ? null : PersonalizacionClases.De(grupo.Clase.Id);
            btnCirculo.Aspecto = aspecto;
            btnCirculo.Visible = aspecto != null;
            hoja.ColorDeLaClase = aspecto == null ? (Color?)null : aspecto.Color;
        }

        /// <summary>
        /// Cambio lo que el profesor eligio para sus clases (aqui, en otra ventana o
        /// con otra lectura): la lista (con el ancho que toca), la cabecera y la hoja.
        /// </summary>
        private void PersonalizacionCambio()
        {
            if (IsDisposed)
            {
                return;
            }

            PonerPersonal(Elegido);
            Acomodar();
            PonerCirculoEncima(-1);
            ayudaLista = null;
            firmaLista = null;
            RepintarLista();
        }

        /// <summary>"Personalizar tu clase" para esa clase; lo que se guarde llega por PersonalizacionClases.Cambio.</summary>
        private void Personalizar(int idClase)
        {
            if (idClase <= 0 || PersonalizacionClases.De(idClase) == null)
            {
                return;
            }

            PonerCirculoEncima(-1);
            PersonalizacionClases.Abrir(this, idClase);
        }

        private void PersonalizarElegido()
        {
            GrupoDelProfesor grupo = Elegido;
            if (grupo != null)
            {
                Personalizar(grupo.Clase.Id);
            }
        }

        /// <summary>
        /// Lo que dice el estado de la cabecera; en rojo si algo fallo, con su
        /// detalle de ayuda. Si hay un aviso a la vista, se guarda y sale al quitarlo.
        /// </summary>
        private void PonerEstado(string texto, bool error, string detalle)
        {
            estadoTexto = texto;
            estadoError = error;
            estadoDetalle = detalle;
            if (!conAviso)
            {
                MostrarEstado(estadoTexto, estadoError ? TextoError : TextoGris, estadoDetalle);
            }
        }

        private void MostrarEstado(string texto, Color color, string detalle)
        {
            bool otroTexto = lblEstado.Text != texto;
            lblEstado.Text = texto;
            lblEstado.ForeColor = color;
            ayuda.SetToolTip(lblEstado, detalle);
            if (otroTexto && verDar)
            {
                // Con "Dar asistencia..." el estado mide su texto (Acomodar).
                Acomodar();
            }
        }

        /// <summary>
        /// "Dar asistencia..." de la cabecera: solo con la lista del grupo elegido a
        /// la vista, uno de sus bloques de hoy en curso y la hoja dejando darla (la
        /// condicion del enlace de debajo, HojaAsistencia.PuedeDarSinLista). Si cambia,
        /// se vuelve a colocar la cabecera.
        /// </summary>
        private void ActualizarBotonDar()
        {
            GrupoDelProfesor grupo = Elegido;
            bool ver = grupo != null && grupos.Count > 0 && claseMostrada == grupo.Clase.Id && hoja.PuedeDarSinLista && EnCurso(grupo);
            if (ver != verDar)
            {
                verDar = ver;
                Acomodar();
            }
        }

        /// <summary>Un aviso en el lugar del estado durante 5 s (en verde si salio bien); despues, otra vez el estado.</summary>
        private void Avisar(string texto, Color color, string detalle)
        {
            conAviso = true;
            MostrarEstado(texto, color, detalle);
            relojAviso.Stop();
            relojAviso.Start();
        }

        private void QuitarAviso()
        {
            relojAviso.Stop();
            conAviso = false;
            MostrarEstado(estadoTexto, estadoError ? TextoError : TextoGris, estadoDetalle);
        }

        /// <summary>
        /// "Copiar la lista": la hoja entera al portapapeles, para pegarla en Excel
        /// (HojaAsistencia.CopiarTodo). Solo el portapapeles, ningun dialogo de
        /// archivos. En kiosco no se ofrece (ver la clase).
        /// </summary>
        private void CopiarLista()
        {
            string detalle;
            HojaAsistencia.Copia copia;
            try
            {
                copia = hoja.CopiarTodo(out detalle);
            }
            catch (Exception ex)
            {
                Log.Error("Mis grupos: copiar la lista de la clase " + claseMostrada, ex);
                copia = HojaAsistencia.Copia.Fallo;
                detalle = MensajeDeFallo.Explicar("No se pudo copiar la lista", ex, AvisoDeFalloDocente.SiSigue);
            }

            switch (copia)
            {
                case HojaAsistencia.Copia.Hecha:
                    Avisar(TextoCopiada, Color.DarkGreen, "Pégala en Excel con Ctrl+V: van los encabezados, un renglón por estudiante y la fila Asistieron.");
                    break;
                case HojaAsistencia.Copia.SinAlumnos:
                    Avisar(TextoSinAlumnosQueCopiar, TextoGris, null);
                    break;
                case HojaAsistencia.Copia.SinLista:
                    // Se esta leyendo, o no se pudo leer (lo dice el tablero).
                    Avisar(leyendoGrupos || leyendoHoja ? TextoEsperaLista : TextoSinLista, TextoGris, null);
                    break;
                default:
                    Avisar(TextoNoSeCopio, TextoError, detalle);
                    break;
            }
        }

        /// <summary>
        /// "Guardar en Excel (CSV)...": la hoja entera en un archivo .csv que Excel
        /// abre con doble clic (HojaAsistencia.ComoCsv: el membrete en las primeras
        /// lineas), en UTF-8 con BOM para que Excel lea los acentos. Pide donde
        /// guardarlo (en Documentos, con el grupo, la materia y la fecha en el
        /// nombre). Si sale bien, lo dice el estado unos segundos (la ruta, en su
        /// ayuda); si Windows no deja guardarlo, un mensaje con por que y que hacer.
        /// En kiosco no se ofrece (alli no hay dialogos de archivos): si se pidiera, nada.
        /// </summary>
        private void GuardarCsv()
        {
            if (Kiosco.Activo)
            {
                return;
            }

            GrupoDelProfesor grupo = Elegido;
            HojaAsistencia.Copia como;
            string texto = hoja.ComoCsv(out como);
            if (texto == null || grupo == null)
            {
                Avisar(como == HojaAsistencia.Copia.SinAlumnos ? TextoSinAlumnosQueCopiar
                    : leyendoGrupos || leyendoHoja ? TextoEsperaLista : TextoSinListaQueGuardar, TextoGris, null);
                return;
            }

            string ruta;
            using (SaveFileDialog dialogo = new SaveFileDialog
            {
                Title = "Guardar la lista de asistencia para Excel",
                Filter = "Archivo CSV para Excel (*.csv)|*.csv",
                DefaultExt = "csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = NombreDelCsv(grupo),
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
                // La causa y que hacer; el detalle (con la ruta), al registro.
                Log.Error("Mis grupos: guardar en CSV la lista de la clase " + grupo.Clase.Id, ex);
                MessageBox.Show(this, VisorPdf.ExplicarArchivo("No se pudo guardar la lista", ex, "Excel"), "Guardar en Excel (CSV)",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Log.Info("Mis grupos: lista de la clase " + grupo.Clase.Id + " guardada en CSV.");
            Avisar(TextoGuardada, Color.DarkGreen, "Se guardó en " + ruta + ". Ábrela con doble clic: se abre en Excel.");
        }

        /// <summary>"Lista de asistencia ISC-7001 Programacion Web 2026-10-16.csv", sin los caracteres que Windows no admite en un nombre.</summary>
        private string NombreDelCsv(GrupoDelProfesor grupo)
        {
            string nombre = "Lista de asistencia " + grupo.Clase.Grupo + " " + grupo.Clase.Materia + " "
                + (DateTime.Now + desfase).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (char invalido in Path.GetInvalidFileNameChars())
            {
                nombre = nombre.Replace(invalido, '-');
            }

            return nombre.Trim() + ".csv";
        }

        /// <summary>Una tarjeta del tablero: marca a sus alumnos en la hoja; otra vez, quita la marca.</summary>
        private void TarjetaDelTableroPulsada(int tarjeta)
        {
            if (tablero.Marcada == tarjeta)
            {
                hoja.QuitarMarca();
                tablero.Marcada = null;
                return;
            }

            IList<int> ids = tablero.IdsDe(tarjeta);
            if (ids == null || ids.Count == 0)
            {
                QuitarMarca();
                return;
            }

            hoja.MarcarAlumnos(ids);
            tablero.Marcada = tarjeta;
        }

        /// <summary>El profesor pulso o se movio en la hoja: la tarjeta deja de verse marcada.</summary>
        private void MarcaQuitadaEnLaHoja()
        {
            if (tablero.Marcada.HasValue)
            {
                tablero.Marcada = null;
            }
        }

        private void QuitarMarca()
        {
            if (tablero.Marcada.HasValue)
            {
                tablero.Marcada = null;
                hoja.QuitarMarca();
            }
        }

        // =====================================================================
        // La lista de grupos
        // =====================================================================

        /// <summary>
        /// Una tarjeta por grupo, en cinco renglones: el grupo con su pastilla de
        /// hoy; la materia; el horario; sus alumnos, los que van sin derecho y la
        /// asistencia del grupo; y un medidor con la marca del minimo. Con db/19,
        /// los dos primeros van sobre la banda del color de la clase (DibujarBanda)
        /// y lo de debajo baja 1 px; sin el, como siempre.
        /// </summary>
        private void DibujarGrupo(object sender, DrawItemEventArgs e)
        {
            Graphics g = e.Graphics;
            g.FillRectangle(Brushes.White, e.Bounds);
            if (e.Index < 0 || e.Index >= grupos.Count)
            {
                return;
            }

            GrupoDelProfesor grupo = grupos[e.Index];
            AspectoClase aspecto = PersonalizacionClases.De(grupo.Clase.Id);
            bool elegido = e.Index == lista.SelectedIndex;
            Rectangle tarjeta = TarjetaDe(e.Bounds);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(tarjeta, 10))
            using (SolidBrush fondo = new SolidBrush(elegido ? FondoElegido : Color.White))
            using (Pen borde = new Pen(elegido ? Color.DarkGreen : BordeSuave, elegido ? 2 : 1))
            {
                g.FillPath(fondo, forma);
                if (aspecto != null)
                {
                    // Despues del fondo y antes del borde: el del elegido queda encima.
                    CirculoClase.PintarBanda(g, forma, new Rectangle(tarjeta.X, tarjeta.Y, tarjeta.Width, AltoBanda), aspecto.Color);
                }

                g.DrawPath(borde, forma);
            }

            g.SmoothingMode = SmoothingMode.None;
            int x = tarjeta.X + 12;
            int derecha = tarjeta.Right - 10;
            int y = tarjeta.Y;
            int bajo = 0;
            if (aspecto != null)
            {
                DibujarBanda(g, grupo, aspecto, e.Bounds, e.Index == circuloEncima);
                bajo = 1;
            }
            else
            {
                // 1. El grupo y, a la derecha, como va hoy: "En curso", "Hoy 02:00" o "Terminó hoy".
                int finGrupo = derecha;
                Color fondoPastilla;
                Color letraPastilla;
                string pastilla = PastillaDe(grupo, out fondoPastilla, out letraPastilla);
                if (pastilla != null)
                {
                    int ancho = Medir(g, pastilla, fuentePastilla).Width + 16;
                    Rectangle caja = new Rectangle(derecha - ancho, y + 9, ancho, 20);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(caja, 10))
                    using (SolidBrush fondo = new SolidBrush(fondoPastilla))
                    {
                        g.FillPath(fondo, forma);
                    }

                    g.SmoothingMode = SmoothingMode.None;
                    TextRenderer.DrawText(g, pastilla, fuentePastilla, caja, letraPastilla, Centrado);
                    finGrupo = caja.Left - 6;
                }

                TextRenderer.DrawText(g, grupo.Clase.Grupo, fuenteGrupo, new Rectangle(x, y + 7, Math.Max(0, finGrupo - x), 24), Color.DarkGreen, Renglon);

                // 2. La materia.
                TextRenderer.DrawText(g, grupo.Clase.Materia, fuenteMateria, new Rectangle(x, y + 31, derecha - x, 18), Color.Black, Renglon);
            }

            // 3. Su horario.
            TextRenderer.DrawText(g, grupo.Horario, fuenteDetalle, new Rectangle(x, y + 50 + bajo, derecha - x, 17 - bajo), TextoGris, Renglon);

            // 4. Sus cuentas y 5. el medidor, solo si ya hay clases que cuenten.
            DibujarCuentas(g, grupo, new Rectangle(x, y + 68, derecha - x, 17));
            ResumenAsistencia r = grupo.Resumen;
            if (r != null && r.Alumnos.Count > 0 && r.Promedio.HasValue)
            {
                DibujarMedidor(g, new Rectangle(x, y + 84 + bajo, derecha - x, 6), r.Promedio.Value, y + 82 + bajo);
            }
        }

        /// <summary>
        /// Los renglones 1 y 2 sobre la banda del color de la clase (db/19): su
        /// circulo con aro blanco (y el anillo con el raton encima), el grupo y el
        /// nombre que le puso el profesor (o el oficial), en negro, que nunca baja de
        /// 9 de contraste sobre la paleta. La pastilla de hoy se lee sobre cualquier
        /// color: "En curso" como siempre; "Hoy 02:00" y la de ya termino, blancas con borde.
        /// </summary>
        private void DibujarBanda(Graphics g, GrupoDelProfesor grupo, AspectoClase aspecto, Rectangle elemento, bool encima)
        {
            Rectangle tarjeta = TarjetaDe(elemento);
            Rectangle circulo = CirculoEnTarjeta(elemento);
            CirculoClase.Pintar(g, circulo, aspecto, true);
            if (encima)
            {
                CirculoClase.PintarAnillo(g, circulo);
            }

            int x = tarjeta.X + 50;
            int derecha = tarjeta.Right - 10;
            int y = tarjeta.Y;
            int finGrupo = derecha;
            Color fondoPastilla;
            Color letraPastilla;
            string pastilla = PastillaDe(grupo, out fondoPastilla, out letraPastilla);
            if (pastilla != null)
            {
                int ancho = Medir(g, pastilla, fuentePastilla).Width + 16;
                Rectangle caja = new Rectangle(derecha - ancho, y + 8, ancho, 20);
                bool enCurso = fondoPastilla == Color.DarkGreen;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(caja, 10))
                using (SolidBrush fondo = new SolidBrush(enCurso ? Color.DarkGreen : Color.White))
                {
                    g.FillPath(fondo, forma);
                    if (!enCurso)
                    {
                        using (Pen borde = new Pen(fondoPastilla == FondoTermino ? BordeTermino : Color.DarkGreen, 1.5f))
                        {
                            g.DrawPath(borde, forma);
                        }
                    }
                }

                g.SmoothingMode = SmoothingMode.None;
                TextRenderer.DrawText(g, pastilla, fuentePastilla, caja, letraPastilla, Centrado);
                finGrupo = caja.Left - 6;
            }

            TextRenderer.DrawText(g, grupo.Clase.Grupo, fuenteGrupo, new Rectangle(x, y + 5, Math.Max(0, finGrupo - x), 22), Color.Black, Renglon);
            TextRenderer.DrawText(g, aspecto.Nombre, fuenteMateria, new Rectangle(x, y + 27, Math.Max(0, derecha - x), 17), Color.Black, Renglon);
        }

        /// <summary>La tarjeta de un grupo dentro de su elemento de la lista: 2 px a los lados, 3 arriba y 8 menos de alto.</summary>
        private static Rectangle TarjetaDe(Rectangle elemento)
        {
            return new Rectangle(elemento.X + 2, elemento.Y + 3, elemento.Width - 6, elemento.Height - 8);
        }

        /// <summary>El circulo de la clase en la banda de su tarjeta (34 px en X+8, Y+6): lo usan el pintado y el raton.</summary>
        private static Rectangle CirculoEnTarjeta(Rectangle elemento)
        {
            Rectangle tarjeta = TarjetaDe(elemento);
            return CirculoClase.Rect(tarjeta.X + 8, tarjeta.Y + 6, DiametroCirculo);
        }

        /// <summary>
        /// "19 alumnos" y, 8 px despues, "16 sin derecho" en rojo; a la derecha, la
        /// asistencia del grupo (roja bajo el minimo). Antes de leer, "Leyendo...";
        /// sin alumnos, "Aún sin lista"; sin clases que cuenten, "—".
        /// </summary>
        private void DibujarCuentas(Graphics g, GrupoDelProfesor grupo, Rectangle renglon)
        {
            ResumenAsistencia r = grupo.Resumen;
            string alumnos;
            string sinDerecho = null;
            string porcentaje = null;
            Color colorPorcentaje = SinDato;
            if (r == null)
            {
                alumnos = grupo.NoSeLeyo ? "No se pudo leer" : "Leyendo...";
            }
            else if (r.Alumnos.Count == 0)
            {
                alumnos = "Aún sin lista";
            }
            else
            {
                alumnos = Alumnos(r.Alumnos.Count);
                porcentaje = "—";
                double? promedio = r.Promedio;
                if (promedio.HasValue)
                {
                    // Los mismos de la columna Derecho de la hoja, y el mismo redondeo que su %.
                    int sin = r.Alumnos.Count(a => !a.ConDerecho);
                    sinDerecho = sin > 0 ? sin + " sin derecho" : null;
                    porcentaje = ResumenAsistencia.PorcentajeMostrado(promedio.Value) + " %";
                    colorPorcentaje = ResumenAsistencia.TieneDerecho(promedio.Value) ? CifraVerde : CifraRoja;
                }
            }

            int limite = renglon.Right;
            if (porcentaje != null)
            {
                TextRenderer.DrawText(g, porcentaje, fuentePorcentaje, renglon, colorPorcentaje, Linea | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                limite -= Medir(g, porcentaje, fuentePorcentaje).Width + 8;
            }

            TextRenderer.DrawText(g, alumnos, fuenteDetalle, new Rectangle(renglon.X, renglon.Y, Math.Max(0, limite - renglon.X), renglon.Height), TextoGris, Renglon);
            int xRojo = renglon.X + Medir(g, alumnos, fuenteDetalle).Width + 8;
            if (sinDerecho != null && limite - xRojo > 0)
            {
                TextRenderer.DrawText(g, sinDerecho, fuenteDetalleNegrita, new Rectangle(xRojo, renglon.Y, limite - xRojo, renglon.Height), CifraRoja, Renglon);
            }
        }

        /// <summary>La asistencia del grupo como las barras de la columna Derecho: pista gris, relleno verde o rojo y la marca del minimo.</summary>
        private static void DibujarMedidor(Graphics g, Rectangle pista, double promedio, int yMarca)
        {
            double valor = Math.Max(0, Math.Min(100, promedio));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(pista, 3))
            using (SolidBrush fondo = new SolidBrush(PistaMedidor))
            {
                g.FillPath(fondo, forma);
            }

            int largo = (int)Math.Round(pista.Width * valor / 100.0);
            if (largo > 0)
            {
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(new Rectangle(pista.X, pista.Y, largo, pista.Height), 3))
                using (SolidBrush relleno = new SolidBrush(ResumenAsistencia.TieneDerecho(promedio) ? MedidorConDerecho : MedidorSinDerecho))
                {
                    g.FillPath(relleno, forma);
                }
            }

            g.SmoothingMode = SmoothingMode.None;
            int marca = pista.X + (int)Math.Round(pista.Width * ResumenAsistencia.Minimo / 100.0);
            using (SolidBrush gris = new SolidBrush(MarcaMinimo))
            {
                g.FillRectangle(gris, marca, yMarca, 1, 10);
            }
        }

        /// <summary>"En curso" (un bloque de hoy sigue), "Hoy 02:00" (el siguiente) o "Terminó hoy"; null si hoy no le toca.</summary>
        private string PastillaDe(GrupoDelProfesor grupo, out Color fondo, out Color letra)
        {
            fondo = FondoHoy;
            letra = Color.DarkGreen;
            switch (EstadoDeHoy(grupo))
            {
                case EstadoHoy.SinClase:
                    return null;
                case EstadoHoy.EnCurso:
                    fondo = Color.DarkGreen;
                    letra = Color.White;
                    return "En curso";
                case EstadoHoy.Termino:
                    fondo = FondoTermino;
                    letra = LetraTermino;
                    return "Terminó hoy";
                default:
                    {
                        TramoHorario siguiente = Siguiente(grupo);
                        return siguiente == null ? null : "Hoy " + HorariosDatos.Hora(siguiente.Inicio);
                    }
            }
        }

        /// <summary>Repinta la lista solo si cambia lo que dice (cada 30 s no parpadea).</summary>
        private void RepintarLista()
        {
            StringBuilder firma = new StringBuilder();
            foreach (GrupoDelProfesor grupo in grupos)
            {
                Color fondo;
                Color letra;
                firma.Append(grupo.Clase.Id).Append(':').Append(PastillaDe(grupo, out fondo, out letra)).Append(':');
                ResumenAsistencia r = grupo.Resumen;
                if (r != null)
                {
                    firma.Append(r.Alumnos.Count).Append(',').Append(r.Promedio).Append(',').Append(r.Alumnos.Count(a => !a.ConDerecho));
                }

                firma.Append(grupo.NoSeLeyo ? "!" : "").Append('|');
            }

            string nueva = firma.ToString();
            if (nueva != firmaLista)
            {
                firmaLista = nueva;
                lista.Invalidate();
            }
        }

        private void AyudaDeLaLista(Point punto)
        {
            int indice = GrupoEn(punto);
            bool sobreUno = indice >= 0;
            // Sobre el circulo de una clase (db/19): la mano, el anillo y su ayuda.
            AspectoClase aspecto = sobreUno ? PersonalizacionClases.De(grupos[indice].Clase.Id) : null;
            bool sobreCirculo = aspecto != null && CirculoClase.Contiene(CirculoEnTarjeta(lista.GetItemRectangle(indice)), punto);
            PonerCirculoEncima(sobreCirculo ? indice : -1);
            PonerAyudaDeLaLista(sobreCirculo ? CirculoClase.Ayuda(aspecto) : sobreUno ? AyudaDe(grupos[indice]) : null);
        }

        /// <summary>El grupo de la lista bajo ese punto, o -1.</summary>
        private int GrupoEn(Point punto)
        {
            int indice = lista.IndexFromPoint(punto);
            return indice >= 0 && indice < grupos.Count && indice < lista.Items.Count && lista.GetItemRectangle(indice).Contains(punto) ? indice : -1;
        }

        /// <summary>El circulo con el raton encima (-1: ninguno): se repinta solo su zona y el cursor pasa a la mano.</summary>
        private void PonerCirculoEncima(int indice)
        {
            if (indice == circuloEncima)
            {
                return;
            }

            foreach (int i in new[] { circuloEncima, indice })
            {
                if (i >= 0 && i < lista.Items.Count)
                {
                    lista.Invalidate(CirculoClase.ZonaPulsable(CirculoEnTarjeta(lista.GetItemRectangle(i))));
                }
            }

            circuloEncima = indice;
            // Null: el de la ventana, como siempre (el de espera mientras lee).
            lista.Cursor = indice >= 0 ? Cursors.Hand : null;
        }

        /// <summary>
        /// Clic izquierdo en el circulo de un grupo (db/19): "Personalizar tu clase".
        /// El ListBox ya eligio ese grupo al bajar el boton, y es lo esperado.
        /// </summary>
        private void ClicEnLaLista(MouseEventArgs e)
        {
            int indice = e.Button == MouseButtons.Left ? GrupoEn(e.Location) : -1;
            if (indice >= 0 && PersonalizacionClases.De(grupos[indice].Clase.Id) != null
                && CirculoClase.Contiene(CirculoEnTarjeta(lista.GetItemRectangle(indice)), e.Location))
            {
                Personalizar(grupos[indice].Clase.Id);
            }
        }

        /// <summary>F2 personaliza el grupo elegido; Mayus+F10 o la tecla de menu abren su menu (db/19).</summary>
        private void TeclaEnLaLista(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2 && e.Modifiers == Keys.None)
            {
                PersonalizarElegido();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Modifiers == Keys.Shift))
            {
                MenuDelGrupo(null);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        /// <summary>
        /// El menu de un grupo con "Personalizar esta clase..." (solo con db/19).
        /// Con el raton (clic derecho), primero elige el grupo de debajo y sale
        /// donde se pulso; con el teclado, el del grupo elegido, junto a su circulo.
        /// </summary>
        private void MenuDelGrupo(Point? raton)
        {
            int indice = raton.HasValue ? GrupoEn(raton.Value) : lista.SelectedIndex;
            if (indice < 0 || indice >= grupos.Count || PersonalizacionClases.De(grupos[indice].Clase.Id) == null)
            {
                return;
            }

            if (lista.SelectedIndex != indice)
            {
                lista.SelectedIndex = indice;
            }

            Rectangle circulo = CirculoEnTarjeta(lista.GetItemRectangle(indice));
            menuGrupo.Show(lista, raton ?? new Point(circulo.Right, circulo.Bottom));
            if (!raton.HasValue && menuGrupo.Items.Count > 0)
            {
                menuGrupo.Items[0].Select();
            }
        }

        /// <summary>La ayuda del grupo bajo el raton: solo se pone si cambia (si no, no se veria).</summary>
        private void PonerAyudaDeLaLista(string texto)
        {
            if (texto != ayudaLista)
            {
                ayudaLista = texto;
                ayuda.SetToolTip(lista, texto);
            }
        }

        /// <summary>
        /// "ISC-5002, Bases de Datos. Lunes de 07:00 a 09:00 y miércoles de 07:00 a
        /// 09:00, en Laboratorio Redes. 19 alumnos; asistencia del grupo: 51 %; sin
        /// derecho a evaluación: 16." Con pocas clases, por que hay tantos sin derecho.
        /// Con nombre corto (db/19), tras la materia, como lo llama el profesor ("BD").
        /// </summary>
        private static string AyudaDe(GrupoDelProfesor grupo)
        {
            AspectoClase aspecto = PersonalizacionClases.De(grupo.Clase.Id);
            string texto = grupo.Clase.Grupo + ", " + grupo.Clase.Materia + ". "
                + (aspecto != null && aspecto.NombreCorto != null ? "Tú la llamas \"" + aspecto.NombreCorto + "\". " : "")
                + grupo.HorarioCompleto;
            ResumenAsistencia r = grupo.Resumen;
            if (r == null)
            {
                return texto;
            }

            if (r.Alumnos.Count == 0)
            {
                return texto + " Aún sin lista: se arma con la primera clase en que registren su bitácora.";
            }

            texto += " " + Alumnos(r.Alumnos.Count);
            if (!r.Promedio.HasValue)
            {
                return texto + "; aún no hay clases que cuenten.";
            }

            int sinDerecho = r.Alumnos.Count(a => !a.ConDerecho);
            texto += "; asistencia del grupo: " + ResumenAsistencia.PorcentajeMostrado(r.Promedio.Value) + " %; sin derecho a evaluación: " + sinDerecho + ".";
            int clases = r.Lista.Dias.Count;
            if (clases <= 4 && sinDerecho > 0)
            {
                texto += " " + PocasClases(clases);
            }

            return texto;
        }

        /// <summary>"Con 2 clases, una falta ya deja 50 %." (con una: "Con 1 clase, quien faltó lleva 0 %.").</summary>
        private static string PocasClases(int clases)
        {
            return clases <= 1
                ? "Con 1 clase, quien faltó lleva 0 %."
                : "Con " + clases + " clases, una falta ya deja " + ResumenAsistencia.PorcentajeMostrado(100.0 * (clases - 1) / clases) + " %.";
        }

        private static string Alumnos(int cuantos)
        {
            return cuantos == 1 ? "1 estudiante" : cuantos + " estudiantes";
        }

        // =====================================================================
        // El horario de cada grupo
        // =====================================================================

        /// <summary>
        /// El horario de una clase "de ahora", sin mezclar el viejo con el nuevo
        /// cuando una franja se cambio desde una fecha (db/20): las franjas que se
        /// dan la proxima vez que llega su dia (de hoy a 6 dias vista). Si
        /// ninguna (sus franjas aun no empiezan), las de la primera semana en que
        /// empiecen; si la clase ya termino, las de su ultimo dia, que son las que
        /// trae ObtenerFranjasDeProfesor.
        /// </summary>
        private static List<FranjaInfo> HorarioDeAhora(List<FranjaInfo> suyas, DateTime hoy)
        {
            List<FranjaInfo> deEstaSemana = SeDanDesde(suyas, hoy);
            if (deEstaSemana.Count > 0)
            {
                return deEstaSemana;
            }

            List<DateTime> futuras = suyas.Where(f => f.VigenteDesde.Date > hoy).Select(f => f.VigenteDesde.Date).ToList();
            if (futuras.Count > 0)
            {
                List<FranjaInfo> primeras = SeDanDesde(suyas, futuras.Min());
                if (primeras.Count > 0)
                {
                    return primeras;
                }
            }

            return suyas;
        }

        /// <summary>Las franjas que se dan la proxima vez que llega su dia, de desde a 6 dias despues.</summary>
        private static List<FranjaInfo> SeDanDesde(List<FranjaInfo> franjas, DateTime desde)
        {
            int diaDesde = ((int)desde.DayOfWeek + 6) % 7 + 1;
            return franjas.Where(f =>
                {
                    DateTime fecha = desde.Date.AddDays((f.DiaSemana - diaDesde + 7) % 7);
                    return f.Activa && fecha >= f.VigenteDesde.Date && fecha <= f.VigenteHasta.Date;
                })
                .ToList();
        }

        private static GrupoDelProfesor NuevoGrupo(ClaseInfo clase, List<FranjaInfo> franjas)
        {
            return new GrupoDelProfesor
            {
                Clase = clase,
                Franjas = franjas,
                Horario = HorarioCorto(franjas),
                HorarioCompleto = HorarioCompleto(franjas)
            };
        }

        /// <summary>
        /// Si todos sus dias tienen los mismos bloques, "Lun y mié, 07:00 a 09:00";
        /// si no, cada dia con su primer inicio: "Lun 07:00, mié 08:00 y vie 11:00".
        /// Sin franjas, "Sin horario".
        /// </summary>
        private static string HorarioCorto(List<FranjaInfo> franjas)
        {
            if (franjas.Count == 0)
            {
                return "Sin horario";
            }

            List<IGrouping<int, FranjaInfo>> dias = franjas.GroupBy(f => f.DiaSemana).OrderBy(d => d.Key).ToList();
            List<List<string>> bloques = dias
                .Select(d => d.OrderBy(f => f.Inicio).ThenBy(f => f.Fin)
                    .Select(f => HorariosDatos.Hora(f.Inicio) + " a " + HorariosDatos.Hora(f.Fin)).Distinct().ToList())
                .ToList();
            if (bloques.All(b => b.SequenceEqual(bloques[0])))
            {
                return ConMayuscula(EstiloAdmin.Enumerar(dias.Select(d => DiaCorto(d.Key)).ToList())) + ", " + string.Join(" y ", bloques[0]);
            }

            return ConMayuscula(EstiloAdmin.Enumerar(dias.Select(d => DiaCorto(d.Key) + " " + HorariosDatos.Hora(d.Min(f => f.Inicio))).ToList()));
        }

        /// <summary>"Lunes de 07:00 a 09:00 y miércoles de 07:00 a 09:00, en Laboratorio Redes." (con varios laboratorios, cada bloque con el suyo).</summary>
        private static string HorarioCompleto(List<FranjaInfo> franjas)
        {
            if (franjas.Count == 0)
            {
                return "Sin horario.";
            }

            List<string> laboratorios = franjas.Select(f => EstiloHorarios.NombreLaboratorio(f.Laboratorio)).Distinct().ToList();
            bool unoSolo = laboratorios.Count == 1;
            List<string> partes = franjas.OrderBy(f => f.DiaSemana).ThenBy(f => f.Inicio)
                .Select(f =>
                {
                    string laboratorio = EstiloHorarios.NombreLaboratorio(f.Laboratorio);
                    return DiaLargo(f.DiaSemana) + " de " + HorariosDatos.Hora(f.Inicio) + " a " + HorariosDatos.Hora(f.Fin)
                        + (unoSolo || laboratorio.Length == 0 ? "" : " en " + laboratorio);
                })
                .Distinct()
                .ToList();
            return ConMayuscula(EstiloAdmin.Enumerar(partes)) + (unoSolo && laboratorios[0].Length > 0 ? ", en " + laboratorios[0] : "") + ".";
        }

        private static string DiaCorto(int dia)
        {
            return dia >= 1 && dia < DiasCortos.Length ? DiasCortos[dia] : "";
        }

        private static string DiaLargo(int dia)
        {
            return dia >= 1 && dia < HorariosDatos.NombresDia.Length ? HorariosDatos.NombresDia[dia].ToLowerInvariant() : "";
        }

        /// <summary>Solo la primera letra en mayuscula ("lun y mié" pasa a "Lun y mié").</summary>
        private static string ConMayuscula(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpperInvariant(texto[0]) + texto.Substring(1);
        }

        /// <summary>
        /// Un texto de la ventana pintado sin el relleno de Label (como BotonCabecera):
        /// empieza justo donde se coloca y mide lo que TextRenderer.MeasureText con
        /// NoPadding. En una linea lleva "..." si no cabe; EnRenglones, se parte.
        /// Sigue siendo un Label para la accesibilidad y las ayudas.
        /// </summary>
        private sealed class Rotulo : Label
        {
            public Rotulo()
            {
                AutoSize = false;
                UseMnemonic = false;
            }

            /// <summary>Alineado a la derecha (el estado de la cabecera).</summary>
            public bool ALaDerecha { get; set; }

            /// <summary>Partido en renglones (la nota bajo la lista).</summary>
            public bool EnRenglones { get; set; }

            protected override void OnPaint(PaintEventArgs e)
            {
                TextFormatFlags formato = EnRenglones
                    ? Parrafo
                    : Linea | TextFormatFlags.EndEllipsis | (ALaDerecha ? TextFormatFlags.Right : TextFormatFlags.Left);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, formato);
            }
        }
    }
}
