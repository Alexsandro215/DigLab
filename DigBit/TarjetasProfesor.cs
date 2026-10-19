using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigBit
{
    /// <summary>
    /// El codigo de clase del profesor en grande y deletreado: es lo que dicta a
    /// sus alumnos, uno para todas sus clases (db/09). En una base sin la 09
    /// cada clase tiene el suyo y se listan. Segun el modo del laboratorio de su
    /// clase de ahora (db/11): con la generacion de codigos, el de la clase
    /// abierta (lo abre el panel solo, desde 15 minutos antes); sin codigo, que
    /// no hace falta dictar nada. Arriba, la ficha de la clase de la que se
    /// habla (materia, grupo, laboratorio y horario); debajo del codigo, como se
    /// deletrea con "Escuchar el codigo" a su lado (lo lee en voz alta:
    /// DictadoCodigo) y tres cifras de como va: bitacoras registradas, fallas
    /// reportadas y equipos sin internet. Mientras la clase esta abierta el
    /// borde late en verde (Resplandor, como en el panel del administrador).
    /// </summary>
    internal class TarjetaCodigo : Control
    {
        // Grande: se ve desde el fondo del salon (DibujarCodigo lo achica si no cabe).
        private const float TamanoCodigo = 120F;
        private const int Relleno = 22;
        private static readonly Color TextoPrincipal = Color.FromArgb(30, 30, 30);
        private static readonly Color TextoSecundario = Color.FromArgb(75, 75, 75);
        // La ficha de la clase (DibujarClase).
        private static readonly Color FondoFicha = Color.FromArgb(244, 250, 244);
        private static readonly Color BordeFicha = Color.FromArgb(205, 225, 205);
        private static readonly Color TituloCampo = Color.FromArgb(100, 125, 100);
        private static readonly Color VerdeAhora = Color.FromArgb(46, 125, 50);
        // Oscuro para que el blanco de su pastilla y su texto se lean (contraste de 5:1).
        private static readonly Color NaranjaPronto = Color.FromArgb(170, 80, 0);
        private static readonly Color VerdeProxima = Color.FromArgb(95, 125, 95);
        private static readonly Color GrisSinClases = Color.FromArgb(115, 125, 115);
        private static readonly Color NaranjaExtra = Color.FromArgb(190, 90, 0);
        private static readonly Color FondoBarra = Color.FromArgb(214, 232, 214);
        // La cifra de la lista con el raton encima (se pulsa): el fondo de las opciones.
        private static readonly Color FondoListaEncima = Color.FromArgb(236, 246, 236);
        private DateTime ahora;

        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 13F, FontStyle.Bold);
        private readonly DrawingFont fuenteTexto = new DrawingFont("Century Gothic", 10.5F);
        private readonly DrawingFont fuenteSinCodigo = new DrawingFont("Century Gothic", 24F, FontStyle.Bold);
        private readonly DrawingFont fuenteCodigoLista = new DrawingFont("Consolas", 16F, FontStyle.Bold);
        private readonly DrawingFont fuenteMateria = new DrawingFont("Century Gothic", 16F, FontStyle.Bold);
        private readonly DrawingFont fuenteMateriaMediana = new DrawingFont("Century Gothic", 14F, FontStyle.Bold);
        private readonly DrawingFont fuenteMateriaChica = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);
        private readonly DrawingFont fuenteNota = new DrawingFont("Century Gothic", 9.5F);
        private readonly DrawingFont fuenteRegistro = new DrawingFont("Century Gothic", 14F, FontStyle.Bold);
        private readonly DrawingFont fuenteEstado = new DrawingFont("Century Gothic", 8.25F, FontStyle.Bold);
        private readonly DrawingFont fuenteFalta = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteTituloCampo = new DrawingFont("Century Gothic", 7.75F, FontStyle.Bold);
        private readonly DrawingFont fuenteValorCampo = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteCifra = new DrawingFont("Century Gothic", 22F, FontStyle.Bold);
        private readonly DrawingFont fuenteCifraCompacta = new DrawingFont("Century Gothic", 14F, FontStyle.Bold);
        private readonly DrawingFont fuenteEtiquetaCifra = new DrawingFont("Century Gothic", 8.5F);
        private readonly DrawingFont fuenteFaltan = new DrawingFont("Century Gothic", 8.5F, FontStyle.Bold);
        private readonly Timer animacion = new Timer { Interval = Resplandor.IntervaloMs };
        private PanoramaProfesor panorama;
        private bool parpadea;

        // Lo que el profesor puede hacer desde la tarjeta: botones de verdad (se
        // usan con el teclado y las pruebas los pulsan por su texto). El pintado
        // calcula donde caen y, al terminar, ColocarAcciones los pone ahi (nunca
        // se mueven a media pintura); sin sitio, ocultos.
        private readonly BotonCabecera btnReportar = new BotonCabecera("Reportar una falla") { FondoDetras = FondoFicha };
        private readonly BotonOpcion opcionBitacora = new BotonOpcion("Bitácora");
        private readonly BotonOpcion opcionAsistencia = new BotonOpcion("Asistencia") { Descripcion = "Quién ha venido a cada clase" };
        private Rectangle zonaReportar;
        private Rectangle zonaOpciones;
        private bool opcionesCompactas;
        private bool bitacoraAbierta = true;
        private bool colocacionPendiente;
        private Rectangle reportarColocado = new Rectangle(-1, -1, 0, 0);
        private Rectangle opcionesColocadas = new Rectangle(-1, -1, 0, 0);
        private SesionAgenda claseReportar;
        private SesionAgenda sesionBitacora;
        private SesionAgenda claseAsistencia;
        // El circulo de la clase de la ficha (db/19): otro boton de verdad, de 48
        // px junto a la materia y los campos (34 px solo junto a la materia si con
        // el de 48 los campos bajan de fila). Sin aspecto, oculto: como siempre.
        private readonly BotonCirculoClase btnCirculo = new BotonCirculoClase { FondoDetras = FondoFicha };
        private Rectangle zonaCirculo;
        private Rectangle circuloColocado = new Rectangle(-1, -1, 0, 0);
        private AspectoClase aspectoCirculo;
        private AspectoClase aspectoColocado;
        private int diametroCirculo = 48;
        // "Escuchar el codigo" (RF63; DictadoCodigo): otro boton de verdad, a la
        // derecha de como se deletrea el codigo en grande (compacto, solo la
        // bocina, si asi no cabe en la linea). Sin codigo que dictar, apagado en
        // su fila bajo lo que se ve en su lugar (si cabe sin cortar lo de debajo),
        // y su ayuda dice por que. Nunca en el kiosco: alli ni se coloca.
        private readonly BotonEscuchar btnEscuchar = new BotonEscuchar();
        private const int AntesDeEscuchar = 12;
        private Rectangle zonaEscuchar;
        private Rectangle escucharColocado = new Rectangle(-1, -1, 0, 0);
        private bool escucharCompacto;
        private string codigoEscuchar;
        private string sinCodigoEscuchar;
        // La lista de cada clase de hoy (ResumenDeClase), o null. Con ella, la
        // primera cifra ("16 de 24 alumnos", "8 aun no") se pulsa y abre la
        // Asistencia de esa clase, como la opcion de debajo; el pintado deja en
        // zonaLista donde quedo (vacia: no se pulsa).
        private IDictionary<int, ListaDeClase> listas;
        private Rectangle zonaLista;
        private bool encimaLista;
        private readonly ToolTip ayudaLista = new ToolTip { InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 15000 };
        private const string AyudaDeLaLista = "Pulsa para ver en Asistencia quién falta.";

        /// <summary>"Reportar una falla" de la clase en curso (solo mientras dura).</summary>
        public event Action<SesionAgenda> ReportarFalla;

        /// <summary>El circulo de la clase de la ficha: "Personalizar tu clase" de esa clase (su id).</summary>
        public event Action<int> Personalizar;

        /// <summary>"Bitacora": quien registro hasta el momento en la sesion de las cifras.</summary>
        public event Action<SesionAgenda> VerBitacora;

        /// <summary>"Asistencia" de la clase de la tarjeta.</summary>
        public event Action<SesionAgenda> VerAsistencia;

        public TarjetaCodigo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            animacion.Tick += (s, e) =>
            {
                if (parpadea)
                {
                    Invalidate();
                }
            };
            animacion.Start();

            // El circulo primero: con Tab va antes que "Reportar una falla"; "Escuchar
            // el codigo", entre la ficha y las opciones de debajo de las cifras.
            foreach (Control accion in new Control[] { btnCirculo, btnReportar, btnEscuchar, opcionBitacora, opcionAsistencia })
            {
                accion.Visible = false;
                Controls.Add(accion);
            }

            btnCirculo.Click += (s, e) =>
            {
                if (btnCirculo.ClaseId != 0 && Personalizar != null)
                {
                    Personalizar(btnCirculo.ClaseId);
                }
            };
            btnReportar.Click += (s, e) =>
            {
                if (claseReportar != null && ReportarFalla != null)
                {
                    ReportarFalla(claseReportar);
                }
            };
            opcionBitacora.Click += (s, e) =>
            {
                if (sesionBitacora != null && VerBitacora != null)
                {
                    VerBitacora(sesionBitacora);
                }
            };
            opcionAsistencia.Click += (s, e) =>
            {
                if (claseAsistencia != null && VerAsistencia != null)
                {
                    VerAsistencia(claseAsistencia);
                }
            };
        }

        /// <summary>La hora del servidor: decide que clase se ensena (la abierta o la proxima).</summary>
        public DateTime Ahora
        {
            get { return ahora; }
            set
            {
                bool otroMinuto = value.Hour != ahora.Hour || value.Minute != ahora.Minute || value.Date != ahora.Date;
                ahora = value;
                if (otroMinuto)
                {
                    Calcular();
                    Invalidate();
                }
            }
        }

        public void Mostrar(PanoramaProfesor datos)
        {
            Mostrar(datos, null);
        }

        /// <summary>
        /// Con la lista de cada clase de hoy (ResumenDeClase): la primera cifra
        /// dice "16 de 24 alumnos" y cuantos aun no registran. Null: sin listas,
        /// solo las bitacoras registradas, como siempre.
        /// </summary>
        public void Mostrar(PanoramaProfesor datos, IDictionary<int, ListaDeClase> listasDeHoy)
        {
            panorama = datos;
            listas = listasDeHoy;
            Calcular();
            Invalidate();
        }

        /// <summary>
        /// El codigo como se ensena y se dicta: en mayusculas. Nadie distingue
        /// mayusculas (el servidor compara con un cotejo _ci y la copia local con
        /// OrdinalIgnoreCase), asi que "aBc7" y "ABC7" son el mismo codigo.
        /// </summary>
        internal static string Mayusculas(string codigo)
        {
            return (codigo ?? "").Trim().ToUpperInvariant();
        }

        /// <summary>
        /// "O (letra), cero, 6, I (letra), W": como se deletrea el codigo (en
        /// mayusculas). Solo se aclara lo que se confunde: la O y la I llevan
        /// "(letra)", y el 0 y el 1 van con su nombre; lo demas, tal cual. Un
        /// codigo de puras cifras (los generados) no se confunde con nada: va
        /// cifra por cifra. Cada letra va unida a su "(letra)" con un espacio
        /// duro: en dos lineas, el salto solo cae despues de una coma.
        /// </summary>
        internal static string Deletrear(string codigo)
        {
            string mayusculas = Mayusculas(codigo);
            bool conLetras = mayusculas.Any(char.IsLetter);
            List<string> partes = new List<string>();
            foreach (char c in mayusculas)
            {
                // "O (letra)" con espacio duro (0xA0): nunca se parte por dentro.
                partes.Add(ComoSeDice(c, conLetras).Replace(' ', (char)0xA0));
            }

            return string.Join(", ", partes);
        }

        /// <summary>
        /// Como se dice un caracter del codigo al deletrearlo: "O (letra)", "I
        /// (letra)", "cero", "uno" o el caracter tal cual, en mayuscula; si el
        /// codigo no lleva letras (conLetras), las cifras solas. Lo usan Deletrear
        /// y la voz que lo dicta (DictadoCodigo): se oye igual que se lee.
        /// </summary>
        internal static string ComoSeDice(char c, bool conLetras)
        {
            char mayuscula = char.ToUpperInvariant(c);
            if (!conLetras)
            {
                return mayuscula.ToString();
            }

            switch (mayuscula)
            {
                case 'O':
                    return "O (letra)";
                case 'I':
                    return "I (letra)";
                case '0':
                    return "cero";
                case '1':
                    return "uno";
                default:
                    return mayuscula.ToString();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animacion.Dispose();
                fuenteTitulo.Dispose();
                fuenteTexto.Dispose();
                fuenteSinCodigo.Dispose();
                fuenteCodigoLista.Dispose();
                fuenteMateria.Dispose();
                fuenteMateriaMediana.Dispose();
                fuenteMateriaChica.Dispose();
                fuenteNota.Dispose();
                fuenteRegistro.Dispose();
                if (letraCodigo != null)
                {
                    letraCodigo.Dispose();
                }
                fuenteEstado.Dispose();
                fuenteFalta.Dispose();
                fuenteTituloCampo.Dispose();
                fuenteValorCampo.Dispose();
                fuenteCifra.Dispose();
                fuenteCifraCompacta.Dispose();
                fuenteEtiquetaCifra.Dispose();
                fuenteFaltan.Dispose();
                ayudaLista.Dispose();
            }

            base.Dispose(disposing);
        }

        private TimeSpan Hora
        {
            get { return (ahora == DateTime.MinValue && panorama != null ? panorama.AhoraServidor : ahora).TimeOfDay; }
        }

        /// <summary>
        /// Manda el modo del laboratorio de su clase de ahora (o de la proxima).
        /// Con dos abiertas a la vez (seguidas, en el margen de 15 minutos), la
        /// que pide un codigo que dictar: el generado antes que el fijo, y
        /// cualquiera de los dos antes que una sin codigo; entre iguales, la que
        /// empieza. Sin las extras que el servidor no deja abrir.
        /// </summary>
        private void Elegir(TimeSpan hora, out List<SesionAgenda> abiertas, out SesionAgenda proxima, out SesionAgenda relevante)
        {
            TimeSpan margen = TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            List<SesionAgenda> utiles = panorama.Hoy.Where(s => s.Utilizable).ToList();
            abiertas = ReglasCodigo.Abiertas(utiles, hora);
            proxima = utiles.Where(s => !s.Cancelada && hora < s.Inicio - margen).OrderBy(s => s.Inicio).FirstOrDefault();
            Func<SesionAgenda, int> pideCodigo = s => panorama.ModoDe(s.LaboratorioId) == ModoCodigo.Renovable ? 2
                : panorama.ModoDe(s.LaboratorioId) == ModoCodigo.Fijo ? 1 : 0;
            relevante = abiertas.OrderByDescending(pideCodigo).ThenByDescending(s => s.Inicio).FirstOrDefault() ?? proxima;
        }

        /// <summary>Si la clase de la tarjeta esta abierta (desde 15 minutos antes hasta que termina): entonces el borde late.</summary>
        private void Calcular()
        {
            if (panorama == null || panorama.AhoraServidor.Date != (ahora == DateTime.MinValue ? panorama.AhoraServidor : ahora).Date)
            {
                parpadea = false;
                return;
            }

            List<SesionAgenda> abiertas;
            SesionAgenda proxima;
            SesionAgenda relevante;
            Elegir(Hora, out abiertas, out proxima, out relevante);
            parpadea = abiertas.Count > 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            zonaReportar = Rectangle.Empty;
            zonaOpciones = Rectangle.Empty;
            zonaCirculo = Rectangle.Empty;
            zonaEscuchar = Rectangle.Empty;
            escucharCompacto = false;
            codigoEscuchar = null;
            sinCodigoEscuchar = null;
            aspectoCirculo = null;
            zonaLista = Rectangle.Empty;
            Pintar(e.Graphics);

            // La cifra de la lista ya no esta bajo el raton (recarga, otra clase):
            // sin la mano ni su ayuda. El recuadro se pinta bien en el siguiente cuadro.
            if (encimaLista && (zonaLista.IsEmpty || !zonaLista.Contains(PointToClient(MousePosition))))
            {
                encimaLista = false;
                Cursor = Cursors.Default;
                ayudaLista.SetToolTip(this, null);
            }

            // Los botones se mueven despues del pintado, y solo si cambio su sitio
            // (el borde late 20 veces por segundo y casi nunca cambia). El aspecto
            // del circulo se compara por referencia: solo cambia si cambio algo.
            if ((zonaReportar != reportarColocado || zonaOpciones != opcionesColocadas
                    || opcionBitacora.Compacta != opcionesCompactas || opcionBitacora.Enabled != bitacoraAbierta
                    || zonaCirculo != circuloColocado || !ReferenceEquals(aspectoCirculo, aspectoColocado)
                    || zonaEscuchar != escucharColocado || btnEscuchar.Compacto != escucharCompacto
                    || btnEscuchar.Codigo != (codigoEscuchar ?? "") || btnEscuchar.SinCodigo != (sinCodigoEscuchar ?? ""))
                && !colocacionPendiente && IsHandleCreated)
            {
                colocacionPendiente = true;
                BeginInvoke(new Action(ColocarAcciones));
            }
        }

        /// <summary>Los botones donde los dejo el pintado; los que esta vez no tuvieron sitio, ocultos.</summary>
        private void ColocarAcciones()
        {
            colocacionPendiente = false;
            if (IsDisposed)
            {
                return;
            }

            reportarColocado = zonaReportar;
            opcionesColocadas = zonaOpciones;
            circuloColocado = zonaCirculo;
            aspectoColocado = aspectoCirculo;
            if (!zonaCirculo.IsEmpty)
            {
                btnCirculo.Diametro = diametroCirculo;
            }

            btnCirculo.Aspecto = aspectoCirculo;
            Colocar(btnCirculo, zonaCirculo);
            Colocar(btnReportar, zonaReportar);

            // Primero su forma y que dicta (un codigo nuevo detiene el que se dictaba), luego su sitio.
            escucharColocado = zonaEscuchar;
            btnEscuchar.Compacto = escucharCompacto;
            btnEscuchar.SinCodigo = sinCodigoEscuchar;
            btnEscuchar.Codigo = codigoEscuchar;
            Colocar(btnEscuchar, zonaEscuchar);
            opcionBitacora.Compacta = opcionesCompactas;
            opcionAsistencia.Compacta = opcionesCompactas;
            opcionBitacora.Enabled = bitacoraAbierta;
            if (zonaOpciones.IsEmpty)
            {
                Colocar(opcionBitacora, Rectangle.Empty);
                Colocar(opcionAsistencia, Rectangle.Empty);
                return;
            }

            const int Entre = 10;
            int mitad = (zonaOpciones.Width - Entre) / 2;
            Colocar(opcionBitacora, new Rectangle(zonaOpciones.Left, zonaOpciones.Top, mitad, zonaOpciones.Height));
            Colocar(opcionAsistencia, new Rectangle(zonaOpciones.Left + mitad + Entre, zonaOpciones.Top, zonaOpciones.Width - mitad - Entre, zonaOpciones.Height));
        }

        /// <summary>Lo que miden las opciones debajo de las cifras, con su aire: normales, o compactas si las cifras van compactas.</summary>
        private static int AltoDeOpciones(bool compactas)
        {
            return compactas ? 8 + BotonOpcion.AltoCompacto : 12 + BotonOpcion.AltoNormal;
        }

        private static void Colocar(Control control, Rectangle donde)
        {
            if (donde.IsEmpty)
            {
                if (control.Visible)
                {
                    control.Visible = false;
                }

                return;
            }

            if (control.Bounds != donde)
            {
                control.Bounds = donde;
            }

            if (!control.Visible)
            {
                control.Visible = true;
            }
        }

        /// <summary>
        /// "Bitacora" y "Asistencia", debajo de las cifras, en y: la bitacora de la
        /// misma sesion de la que salen las cifras (DondeRegistran) y la asistencia
        /// de la clase de la tarjeta. Antes de que sus alumnos puedan registrar, la
        /// bitacora va apagada y dice desde cuando. Devuelve lo que ocupan.
        /// </summary>
        private int PrepararOpciones(SesionAgenda clase, SesionAgenda anterior, Rectangle interior, int y, bool compactas, TimeSpan hora)
        {
            int alto = AltoDeOpciones(compactas);
            zonaOpciones = new Rectangle(interior.Left, y + alto - (compactas ? BotonOpcion.AltoCompacto : BotonOpcion.AltoNormal),
                interior.Width, compactas ? BotonOpcion.AltoCompacto : BotonOpcion.AltoNormal);
            opcionesCompactas = compactas;
            sesionBitacora = DondeRegistran(clase, anterior);
            claseAsistencia = clase;
            TimeSpan desde = clase.Inicio - TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            bitacoraAbierta = anterior != null || hora >= desde || sesionBitacora.Alumnos > 0;
            int registraron = sesionBitacora.Alumnos;
            opcionBitacora.Descripcion = !bitacoraAbierta ? "Desde las " + HorariosDatos.Hora(desde)
                : registraron == 0 ? "Nadie ha registrado todavía"
                : registraron == 1 ? "1 estudiante registró hasta ahora" : registraron + " estudiantes registraron hasta ahora";
            return alto;
        }

        /// <summary>
        /// La sesion donde registran los alumnos de la clase s: la suya; la de su
        /// franja si la recortaron (EnOtra, si la suya salio vacia); o, con
        /// anterior (codigo fijo y otra clase suya ya empezada en el mismo
        /// laboratorio), la de esa.
        /// </summary>
        private static SesionAgenda DondeRegistran(SesionAgenda s, SesionAgenda anterior)
        {
            return anterior ?? (s.Alumnos == 0 && s.EnOtra != null ? s.EnOtra : s);
        }

        private void Pintar(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle caja = new Rectangle(Resplandor.Margen + 1, Resplandor.Margen + 1,
                Width - 2 * Resplandor.Margen - 3, Height - 2 * Resplandor.Margen - 3);
            if (parpadea)
            {
                Resplandor.Dibujar(g, caja, 12);
            }

            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(caja, 12))
            {
                using (SolidBrush fondo = new SolidBrush(Color.White))
                {
                    g.FillPath(fondo, forma);
                }

                using (Pen borde = new Pen(parpadea ? Resplandor.Borde() : Color.DarkGreen, parpadea ? 3f : 2f))
                {
                    g.DrawPath(borde, forma);
                }
            }

            Rectangle interior = Rectangle.Inflate(caja, -Relleno, -18);
            int y = interior.Top;
            if (panorama == null)
            {
                Texto(g, "Tu código de clase", fuenteTitulo, Color.DarkGreen, interior, ref y, false);
                Texto(g, "Cargando...", fuenteTexto, Color.Gray, interior, ref y, false);
                return;
            }

            TimeSpan hora = Hora;
            Color nota = Color.FromArgb(90, 90, 90);

            // Pasada la medianoche sin poder recargar, los datos son de ayer: sus
            // clases (y su codigo de sesion) no son las de hoy. El codigo fijo si
            // sigue valiendo (los equipos lo aceptan aun sin servidor): se ensena.
            if (panorama.AhoraServidor.Date != (ahora == DateTime.MinValue ? panorama.AhoraServidor : ahora).Date)
            {
                Texto(g, "Cargando tus clases de hoy... Si tarda, revisa la conexión.", fuenteTexto, Color.Gray, interior, ref y, true);
                if (panorama.CodigoPorProfesor && !string.IsNullOrEmpty(panorama.Codigo) && panorama.ModoComun == ModoCodigo.Fijo)
                {
                    DibujarCodigoConRegistro(g, interior, y + 10, "Tu código de clase", panorama.Codigo, null, null, hora,
                        Nota("Tu código sigue valiendo aunque aún no se carguen tus clases de hoy.", nota));
                }

                return;
            }

            List<SesionAgenda> abiertas;
            SesionAgenda proxima;
            SesionAgenda relevante;
            Elegir(hora, out abiertas, out proxima, out relevante);

            // Sin mas clases hoy, manda el laboratorio de la proxima que ensena la ficha.
            ClaseOtroDia siguiente = relevante == null ? ProximaClase() : null;
            ModoCodigo modo = relevante != null ? panorama.ModoDe(relevante.LaboratorioId)
                : siguiente != null ? panorama.ModoDe(siguiente.LaboratorioId) : panorama.ModoComun;
            SesionAgenda otraClase = abiertas.FirstOrDefault(s => s != relevante && panorama.ModoDe(s.LaboratorioId) != modo);

            // Para "Reportar una falla": la clase que ya empezo y aun no termina. En
            // los 15 minutos antes de otra seguida, la que va terminando, aunque la
            // ficha ya ensene la siguiente.
            SesionAgenda enCursoAhora = abiertas.Where(s => !s.Cancelada && s.Inicio <= hora && hora <= s.Fin)
                .OrderByDescending(s => s.Inicio).FirstOrDefault();

            // Arriba la ficha de la clase de la que se habla (o, sin mas clases hoy,
            // de la proxima); debajo, juntos, el titulo, el codigo, como se deletrea,
            // las cifras de como va y las notas.
            DibujarClase(g, interior, ref y, relevante, siguiente, hora, enCursoAhora);
            y += 14;
            string titulo = modo == ModoCodigo.SinCodigo ? "Tus estudiantes no necesitan código"
                : modo == ModoCodigo.Renovable ? "Código de esta clase" : "Tu código de clase";
            if (modo == ModoCodigo.SinCodigo)
            {
                y += 6;
                Centrado(g, titulo, fuenteTitulo, Color.DarkGreen, interior, ref y);
                string comoEntran = "En el laboratorio, cada estudiante entra con su cuenta y llena su bitácora, desde "
                    + HorariosDatos.MargenAntesDelInicioMinutos + " minutos antes de empezar: tu clase se elige sola si es la única "
                    + "a esa hora (si no, la elige de una lista). No hace falta dictarles nada.";

                // Las cifras y, debajo, Bitacora y Asistencia; compactas si asi no cabe la nota.
                // "Escuchar el codigo", apagado (aqui no hay nada que dictar), bajo el
                // titulo: es lo que menos importa, no compacta nada y solo va si cabe.
                int altoNota = 12 + AltoDe(g, comoEntran, fuenteNota, interior.Width);
                Func<bool, int> resto = compactas => Registro(g, interior, 0, relevante, null, hora, false, compactas)
                    + (relevante != null ? AltoDeOpciones(compactas) : 0) + altoNota;
                bool compacto = relevante != null && y + 10 + resto(false) > interior.Bottom;
                y += EscucharApagado(interior, y, 10 + resto(compacto), "En este laboratorio tus estudiantes entran sin código: no hay nada que dictar.");
                y += 10;
                y += Registro(g, interior, y, relevante, null, hora, true, compacto);
                if (relevante != null)
                {
                    y += PrepararOpciones(relevante, null, interior, y, compacto, hora);
                }

                y += 12;
                Centrado(g, comoEntran, fuenteNota, nota, interior, ref y);
                return;
            }

            if (modo == ModoCodigo.Renovable)
            {
                DibujarRenovable(g, interior, y, titulo, abiertas.Where(s => panorama.ModoDe(s.LaboratorioId) == ModoCodigo.Renovable).ToList(),
                    proxima, otraClase, relevante, hora);
                return;
            }

            if (!panorama.CodigoPorProfesor)
            {
                Texto(g, titulo, fuenteTitulo, Color.DarkGreen, interior, ref y, false);
                DibujarLista(g, interior, y);
                return;
            }

            if (string.IsNullOrEmpty(panorama.Codigo))
            {
                string comoSeAsigna = "Todavía no tienes código de clase. Te lo asigna la administración de DigBit en Docentes, o se crea al darte de alta tu primera clase.";
                y += 6;
                Centrado(g, titulo, fuenteTitulo, Color.DarkGreen, interior, ref y);
                y += 8;
                Centrado(g, "Sin código", fuenteSinCodigo, Color.Gray, interior, ref y);
                y += EscucharApagado(interior, y, 10 + AltoDe(g, comoSeAsigna, fuenteNota, interior.Width), "Aún no tienes código de clase que dictar.");
                y += 10;
                Centrado(g, comoSeAsigna, fuenteNota, nota, interior, ref y);
                return;
            }

            // Con otra clase suya ya empezada en el mismo laboratorio, hasta que
            // empiece la de la tarjeta su codigo abre esa (gana la que ya empezo,
            // ReglasCodigo): los que llegan antes quedan alli.
            SesionAgenda anterior = relevante == null || hora >= relevante.Inicio ? null
                : abiertas.Where(a => a != relevante && !a.Cancelada && a.LaboratorioId == relevante.LaboratorioId && a.Inicio <= hora
                        && panorama.ModoDe(a.LaboratorioId) == ModoCodigo.Fijo)
                    .OrderByDescending(a => a.Inicio)
                    .FirstOrDefault();

            // Las notas, de la que importa en este momento a la fija: si no caben,
            // se quitan desde la ultima. La otra clase solo puede ser una sin codigo
            // aqui (una con codigo generado habria mandado).
            DibujarCodigoConRegistro(g, interior, y, titulo, panorama.Codigo, relevante, anterior, hora,
                Nota(otraClase == null ? null
                    : "La de las " + HorariosDatos.Hora(otraClase.Inicio) + " en " + panorama.Laboratorio(otraClase.LaboratorioId) + " no necesita código.", nota),
                Nota("Díctalo al empezar. Es el mismo en todas tus clases y solo vale en su laboratorio y horario.", nota));
        }

        private static KeyValuePair<string, Color> Nota(string texto, Color color)
        {
            return new KeyValuePair<string, Color>(texto, color);
        }

        /// <summary>
        /// Debajo de la ficha, un solo grupo centrado: el titulo, el codigo lo mas
        /// grande que quepa, como se deletrea, como va su clase (Registro: tres
        /// cifras) y las notas que no esten vacias. Lo que sobra de alto se reparte
        /// 45 % arriba y 55 % abajo, asi la nota queda junto al codigo del que
        /// habla. Si no cabe todo: las cifras en su forma compacta y, luego, sin
        /// las notas desde la ultima (la fija va al final).
        /// </summary>
        private void DibujarCodigoConRegistro(Graphics g, Rectangle interior, int y, string titulo, string codigo, SesionAgenda clase, SesionAgenda anterior,
            TimeSpan hora, params KeyValuePair<string, Color>[] notas)
        {
            const int AlCodigo = 6;
            const int AlDeletreo = 4;
            const int MinimoCodigo = 48;
            // Siempre en mayusculas, como lo pide la caja del alumno (no las distingue nadie).
            codigo = Mayusculas(codigo);
            List<KeyValuePair<string, Color>> pie = notas.Where(n => !string.IsNullOrEmpty(n.Key)).ToList();
            int libre = interior.Bottom - y;
            int altoTitulo = AltoDe(g, titulo, fuenteTitulo, interior.Width);
            string deletreo = Deletrear(codigo);
            // Como se deletrea y, a su derecha, "Escuchar el codigo": la fila mide lo mas alto de los dos.
            FilaDeletreo fila = MedirFilaDeletreo(g, deletreo, interior.Width);
            int altoDeletreo = fila.Alto;
            bool compacto = false;
            int altoRegistro = Registro(g, interior, 0, clase, anterior, hora, false, false);
            // Bitacora y Asistencia, debajo de las cifras (solo con una clase de la
            // que hablar): con ellas, las notas se quitan antes (el codigo no baja de
            // 80 px), porque las opciones se quedan siempre.
            bool conOpciones = clase != null && altoRegistro > 0;
            Func<int> sinElCodigo = () => altoTitulo + AlCodigo + AlDeletreo + altoDeletreo
                + (altoRegistro > 0 ? (compacto ? 10 : 16) + altoRegistro : 0)
                + (conOpciones ? AltoDeOpciones(compacto) : 0)
                + (pie.Count > 0 ? 12 + pie.Sum(n => AltoDe(g, n.Key, fuenteNota, interior.Width)) + 4 * (pie.Count - 1) : 0);
            if (altoRegistro > 0 && libre - sinElCodigo() < 100)
            {
                compacto = true;
                altoRegistro = Registro(g, interior, 0, clase, anterior, hora, false, true);
            }

            while (pie.Count > 0 && libre - sinElCodigo() < (conOpciones ? 80 : MinimoCodigo))
            {
                pie.RemoveAt(pie.Count - 1);
            }

            MedidaCodigo medida = MedirCodigo(g, codigo, interior.Width, Math.Max(MinimoCodigo, libre - sinElCodigo()));
            int alto = sinElCodigo() + medida.Alto - medida.Recorte;
            y += Math.Max(0, libre - alto) * 45 / 100;
            Centrado(g, titulo, fuenteTitulo, Color.DarkGreen, interior, ref y);
            y += AlCodigo;
            DibujarCodigo(g, codigo, medida, new Point(interior.Left + (interior.Width - medida.Ancho) / 2, y));
            y += medida.Alto - medida.Recorte + AlDeletreo;
            PintarFilaDeletreo(g, fila, deletreo, codigo, interior, y);
            y += fila.Alto;
            if (altoRegistro > 0)
            {
                y += compacto ? 10 : 16;
                y += Registro(g, interior, y, clase, anterior, hora, true, compacto);
            }

            if (conOpciones)
            {
                y += PrepararOpciones(clase, anterior, interior, y, compacto, hora);
            }

            if (pie.Count > 0)
            {
                y += 12;
                foreach (KeyValuePair<string, Color> n in pie)
                {
                    if (y + AltoDe(g, n.Key, fuenteNota, interior.Width) > interior.Bottom)
                    {
                        break;
                    }

                    Centrado(g, n.Key, fuenteNota, n.Value, interior, ref y);
                    y += 4;
                }
            }
        }

        private static int AltoDe(Graphics g, string texto, DrawingFont fuente, int ancho)
        {
            return TextRenderer.MeasureText(g, texto, fuente, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        }

        /// <summary>Como va la linea de como se deletrea el codigo, con "Escuchar el codigo" a su derecha (MedirFilaDeletreo).</summary>
        private struct FilaDeletreo
        {
            // Con el boton (no en el kiosco), y si va compacto (solo la bocina).
            public bool ConBoton;
            public bool Compacto;
            public int AnchoTexto;
            public int AltoTexto;
            // El texto y lo que se ve del boton, juntos: el grupo va centrado.
            public int AnchoGrupo;
            public int Alto;
        }

        /// <summary>
        /// La linea de como se deletrea y, a su derecha, "Escuchar el codigo": la
        /// pastilla con su texto si todo cabe en una linea; si no, solo la bocina
        /// (y el deletreo, si ni asi cabe, en varias lineas a su izquierda). Mide
        /// lo mas alto de los dos. Sin el boton (kiosco), como siempre.
        /// </summary>
        private FilaDeletreo MedirFilaDeletreo(Graphics g, string deletreo, int ancho)
        {
            FilaDeletreo fila = new FilaDeletreo();
            if (!DictadoCodigo.Permitido)
            {
                fila.AnchoTexto = ancho;
                fila.AltoTexto = AltoDe(g, deletreo, fuenteTexto, ancho);
                fila.AnchoGrupo = ancho;
                fila.Alto = fila.AltoTexto;
                return fila;
            }

            // Con un par de pixeles de holgura: medido en una linea y pintado con WordBreak, no debe partirse.
            int enUnaLinea = TextRenderer.MeasureText(g, deletreo, fuenteTexto, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + 2;
            int pastilla = btnEscuchar.Medida(false).Width - 2 * BotonEscuchar.MargenFoco;
            int bocina = btnEscuchar.Medida(true).Width - 2 * BotonEscuchar.MargenFoco;
            fila.ConBoton = true;
            fila.Compacto = enUnaLinea + AntesDeEscuchar + pastilla > ancho;
            int boton = fila.Compacto ? bocina : pastilla;
            fila.AnchoTexto = Math.Max(40, Math.Min(enUnaLinea, ancho - AntesDeEscuchar - boton));
            fila.AltoTexto = AltoDe(g, deletreo, fuenteTexto, fila.AnchoTexto);
            fila.AnchoGrupo = fila.AnchoTexto + AntesDeEscuchar + boton;
            fila.Alto = Math.Max(fila.AltoTexto, BotonEscuchar.AltoVisible);
            return fila;
        }

        /// <summary>
        /// Pinta en y la linea de como se deletrea y deja "Escuchar el codigo" a su
        /// derecha (ColocarAcciones lo pone ahi), centrado en su alto: su anillo del
        /// foco cae en el aire de arriba y de abajo. Sin sitio, sin el boton.
        /// </summary>
        private void PintarFilaDeletreo(Graphics g, FilaDeletreo fila, string deletreo, string codigo, Rectangle interior, int y)
        {
            Color gris = Color.FromArgb(90, 90, 90);
            if (!fila.ConBoton)
            {
                Centrado(g, deletreo, fuenteTexto, gris, interior, ref y);
                return;
            }

            if (y >= interior.Bottom)
            {
                return;
            }

            int x = interior.Left + (interior.Width - fila.AnchoGrupo) / 2;
            TextRenderer.DrawText(g, deletreo, fuenteTexto, new Rectangle(x, y + (fila.Alto - fila.AltoTexto) / 2, fila.AnchoTexto, fila.AltoTexto), gris,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            Size boton = btnEscuchar.Medida(fila.Compacto);
            Rectangle zona = new Rectangle(x + fila.AnchoTexto + AntesDeEscuchar - BotonEscuchar.MargenFoco, y + (fila.Alto - boton.Height) / 2,
                boton.Width, boton.Height);
            if (zona.Bottom - BotonEscuchar.MargenFoco > interior.Bottom)
            {
                return;
            }

            zonaEscuchar = zona;
            escucharCompacto = fila.Compacto;
            codigoEscuchar = codigo;
        }

        // Lo que ocupa "Escuchar el codigo" apagado en su fila (EscucharApagado):
        // su aire de arriba y el boton, sin el margen del foco de abajo, que cae
        // en el aire que sigue.
        private const int ArribaDeEscuchar = 6;
        private const int AltoEscucharApagado = ArribaDeEscuchar + BotonEscuchar.Alto - BotonEscuchar.MargenFoco;

        /// <summary>
        /// "Escuchar el codigo" apagado en su propia fila, centrado, cuando en lugar
        /// del codigo se ve otra cosa ("Aun no", "Abriendo...", "Sin codigo"): su
        /// ayuda dice por que (porQue). Es lo que menos importa: solo va si cabe con
        /// todo lo que sigue debajo (debajo, su alto), sin cortar ninguna nota ni el
        /// aviso de por que no se abrio una clase. Devuelve lo que ocupa
        /// (AltoEscucharApagado), o 0 si no va (kiosco, o sin sitio).
        /// </summary>
        private int EscucharApagado(Rectangle interior, int y, int debajo, string porQue)
        {
            if (!DictadoCodigo.Permitido || y + AltoEscucharApagado + debajo > interior.Bottom)
            {
                return 0;
            }

            bool compacto = btnEscuchar.Medida(false).Width > interior.Width;
            Size boton = btnEscuchar.Medida(compacto);
            zonaEscuchar = new Rectangle(interior.Left + (interior.Width - boton.Width) / 2, y + ArribaDeEscuchar, boton.Width, boton.Height);
            escucharCompacto = compacto;
            sinCodigoEscuchar = porQue;
            return AltoEscucharApagado;
        }

        /// <summary>
        /// La clase de la tarjeta (abierta, en curso o en los 15 minutos de
        /// antes, o la proxima de hoy) como una ficha, todo con el mismo borde
        /// izquierdo: arriba una pastilla con como va (con las palabras de los
        /// bloques de la derecha) y junto a ella cuanto falta; la materia en grande
        /// (en letra menor si no cabe); en columnas con su nombre encima, el grupo,
        /// el laboratorio y, si no esta en curso, el horario; y en curso, una linea
        /// de tiempo de su hora de inicio a la de fin con lo que lleva. Sin mas
        /// clases hoy, la ficha de la proxima de otro dia (siguiente). Con db/19,
        /// el circulo de la clase (su boton) junto a la materia y un lomo de su
        /// color en el borde izquierdo; la pastilla y lo demas no cambian.
        /// </summary>
        private void DibujarClase(Graphics g, Rectangle zona, ref int y, SesionAgenda s, ClaseOtroDia siguiente, TimeSpan hora, SesionAgenda claseEnCurso)
        {
            string estado;
            Color colorEstado;
            string falta = null;
            string aviso = null;
            string materia = null;
            int idClase = 0;
            bool extra = false;
            float? avance = null;
            TimeSpan inicio = TimeSpan.Zero;
            TimeSpan fin = TimeSpan.Zero;
            List<KeyValuePair<string, string>> campos = new List<KeyValuePair<string, string>>();
            if (s == null)
            {
                estado = panorama.Hoy.Any(c => !c.Cancelada) ? "Ya terminaste tus clases de hoy" : "Hoy no tienes clases";
                colorEstado = GrisSinClases;
                if (siguiente == null)
                {
                    aviso = "No tienes clases en los próximos 7 días.";
                }
                else
                {
                    aviso = "Tu próxima clase es " + CuandoEs(siguiente.Dia) + ":";
                    materia = siguiente.Materia;
                    idClase = siguiente.ClaseId;
                    campos = Campos(siguiente.Grupo, siguiente.Laboratorio, siguiente.Inicio, siguiente.Fin, true);
                }
            }
            else
            {
                // Abierta hasta su fin inclusive, como ReglasCodigo.Abierta: en el
                // segundo exacto en que termina sigue "En curso".
                TimeSpan margen = TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
                bool enCurso = s.Inicio <= hora && hora <= s.Fin;
                bool pronto = !enCurso && hora >= s.Inicio - margen && hora < s.Inicio;
                estado = enCurso ? "En curso" : pronto ? "Empieza pronto" : "Tu próxima clase hoy";
                colorEstado = enCurso ? VerdeAhora : pronto ? NaranjaPronto : VerdeProxima;
                falta = enCurso ? "quedan " + PanelLaboratoriosAhora.Duracion(s.Fin - hora)
                    : "empieza en " + PanelLaboratoriosAhora.Duracion(s.Inicio - hora);
                materia = s.Materia;
                idClase = s.ClaseId;
                extra = s.EsExtra;

                // En curso, el horario lo dice la linea de tiempo (con sus dos horas).
                campos = Campos(s.Grupo, panorama.Laboratorio(s.LaboratorioId), s.Inicio, s.Fin, !enCurso);
                if (enCurso && s.Fin > s.Inicio)
                {
                    avance = (float)((hora - s.Inicio).TotalSeconds / (s.Fin - s.Inicio).TotalSeconds);
                    inicio = s.Inicio;
                    fin = s.Fin;
                }
            }

            // Lo que mide cada parte: la ficha mide lo que lleva. Todo se mide y se
            // pinta sin el margen de TextRenderer, asi empieza en el mismo borde.
            const int Dentro = 14;
            const TextFormatFlags SinMargen = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            const TextFormatFlags Linea = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | SinMargen;
            int ancho = Math.Max(40, zona.Width - 2 * Dentro);
            Size medidaEstado = TextRenderer.MeasureText(g, estado, fuenteEstado, Size.Empty, SinMargen);
            Size medidaExtra = TextRenderer.MeasureText(g, "Sesión extra", fuenteEstado, Size.Empty, SinMargen);
            int altoPastilla = medidaEstado.Height + 8;
            int anchoPastillas = medidaEstado.Width + 18 + (extra ? 8 + medidaExtra.Width + 18 : 0);
            Size medidaFalta = falta == null ? Size.Empty : TextRenderer.MeasureText(g, falta, fuenteFalta, Size.Empty, SinMargen);

            // Mientras hay una clase en curso (y la base lo admite, db/15), "Reportar
            // una falla" a la derecha de la pastilla; al terminar ya no se ofrece.
            bool conReportar = claseEnCurso != null && panorama.ReportesDisponibles;
            claseReportar = conReportar ? claseEnCurso : null;
            int reserva = conReportar ? btnReportar.Width + 10 : 0;

            // Cuanto falta va junto a la pastilla; si ahi no cabe, en su linea debajo.
            bool faltaAparte = falta != null && anchoPastillas + 10 + medidaFalta.Width > ancho - reserva;

            // En curso, si con el boton "quedan X" ya no cabe junto a la pastilla, se
            // quita en vez de bajarlo a su linea: la linea de tiempo ya dice el fin.
            if (faltaAparte && conReportar && avance.HasValue)
            {
                falta = null;
                faltaAparte = false;
            }
            int altoArriba = altoPastilla + (faltaAparte ? 4 + medidaFalta.Height : 0);
            int altoAviso = aviso == null ? 0 : TextRenderer.MeasureText(g, aviso, fuenteTexto, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak | SinMargen).Height;

            // El circulo de la clase (db/19; sin el, todo como siempre): de 48 px a
            // la izquierda de la materia y los campos, que se recorren 60 px; si asi
            // los campos necesitan mas filas, de 34 px solo junto a la materia (su
            // fila mide al menos 34) y los campos con todo el ancho. Asi la ficha
            // crece como mucho 8 px: a 1366 no sobra alto.
            AspectoClase aspecto = materia == null ? null : PersonalizacionClases.De(idClase);
            List<List<KeyValuePair<string, string>>> filas = RepartirCampos(g, campos, ancho);
            int diametro = 0;
            int corrido = 0;
            bool camposCorridos = false;
            if (aspecto != null)
            {
                List<List<KeyValuePair<string, string>>> junto = RepartirCampos(g, campos, ancho - 60);
                camposCorridos = junto.Count <= filas.Count;
                diametro = camposCorridos ? 48 : 34;
                corrido = diametro + 12;
                if (camposCorridos)
                {
                    filas = junto;
                }
            }

            DrawingFont letraMateria = materia == null ? fuenteMateria : LetraQueCabe(g, materia, ancho - corrido);
            int altoMateria = materia == null ? 0 : TextRenderer.MeasureText(g, "Ág", letraMateria, Size.Empty, SinMargen).Height;
            int altoFilaMateria = diametro == 34 ? Math.Max(altoMateria, diametro) : altoMateria;
            int altoTituloCampo = TextRenderer.MeasureText(g, "Ág", fuenteTituloCampo, Size.Empty, SinMargen).Height;
            int altoValorCampo = TextRenderer.MeasureText(g, "Ág", fuenteValorCampo, Size.Empty, SinMargen).Height;
            int altoCampo = altoTituloCampo + 3 + altoValorCampo;
            int altoCampos = filas.Count * altoCampo + Math.Max(0, filas.Count - 1) * 8;
            int alto = Dentro + altoArriba
                + (altoAviso > 0 ? 8 + altoAviso : 0)
                + (altoFilaMateria > 0 ? 8 + altoFilaMateria : 0)
                + (altoCampos > 0 ? 10 + altoCampos : 0)
                + (avance.HasValue ? 12 + altoValorCampo : 0)
                + Dentro;
            Rectangle ficha = new Rectangle(zona.Left, y, zona.Width, alto);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(ficha, 10))
            using (SolidBrush fondo = new SolidBrush(FondoFicha))
            using (Pen borde = new Pen(BordeFicha))
            {
                g.FillPath(fondo, forma);
                // El lomo del color de la clase, entre el relleno y el borde.
                if (aspecto != null)
                {
                    CirculoClase.PintarLomo(g, forma, ficha, aspecto.Color, 5);
                }

                g.DrawPath(borde, forma);
            }

            // La pastilla (y la de sesion extra) y cuanto falta.
            int x = ficha.Left + Dentro;
            int yy = ficha.Top + Dentro;
            Rectangle pastilla = new Rectangle(x, yy, medidaEstado.Width + 18, altoPastilla);
            Pastilla(g, pastilla, estado, colorEstado);
            int tras = pastilla.Right;
            if (extra)
            {
                Rectangle otra = new Rectangle(tras + 8, yy, medidaExtra.Width + 18, altoPastilla);
                Pastilla(g, otra, "Sesión extra", NaranjaExtra);
                tras = otra.Right;
            }

            if (falta != null)
            {
                Rectangle donde = faltaAparte
                    ? new Rectangle(x, yy + altoPastilla + 4, ancho, medidaFalta.Height)
                    : new Rectangle(tras + 10, yy + (altoPastilla - medidaFalta.Height) / 2, x + ancho - reserva - (tras + 10), medidaFalta.Height);
                TextRenderer.DrawText(g, falta, fuenteFalta, donde, colorEstado, Linea);
            }

            if (conReportar)
            {
                // El boton mide 3 px de mas por lado (el anillo del foco): la pastilla
                // que se ve queda en el borde de dentro de la ficha.
                zonaReportar = new Rectangle(ficha.Right - Dentro + 3 - btnReportar.Width, yy + (altoPastilla - btnReportar.Height) / 2,
                    btnReportar.Width, btnReportar.Height);
            }

            yy += altoArriba;
            if (altoAviso > 0)
            {
                yy += 8;
                TextRenderer.DrawText(g, aviso, fuenteTexto, new Rectangle(x, yy, ancho, altoAviso), TextoSecundario,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | SinMargen);
                yy += altoAviso;
            }

            int yBanda = yy + 8;
            if (altoFilaMateria > 0)
            {
                yy += 8;
                TextRenderer.DrawText(g, materia, letraMateria,
                    new Rectangle(x + corrido, yy + (altoFilaMateria - altoMateria) / 2, Math.Max(10, ancho - corrido), altoMateria), TextoPrincipal, Linea);
                yy += altoFilaMateria;
            }

            // Los campos: su nombre en pequeno y, debajo, el dato (a la derecha del
            // circulo de 48, si lo hay).
            int xCampos = camposCorridos ? x + corrido : x;
            int finCampos = x + ancho;
            if (altoCampos > 0)
            {
                yy += 10;
                foreach (List<KeyValuePair<string, string>> fila in filas)
                {
                    int xc = xCampos;
                    for (int i = 0; i < fila.Count; i++)
                    {
                        int anchoCampo = i == fila.Count - 1 ? finCampos - xc : Math.Min(AnchoCampo(g, fila[i]), finCampos - xc);
                        TextRenderer.DrawText(g, fila[i].Key, fuenteTituloCampo, new Rectangle(xc, yy, anchoCampo, altoTituloCampo), TituloCampo, Linea);
                        TextRenderer.DrawText(g, fila[i].Value, fuenteValorCampo, new Rectangle(xc, yy + altoTituloCampo + 3, anchoCampo, altoValorCampo),
                            TextoPrincipal, Linea);
                        xc += anchoCampo + EntreCampos;
                    }

                    yy += altoCampo + 8;
                }

                yy -= 8;
            }

            // El circulo lo pinta su boton (ColocarAcciones lo pone aqui): el de 48
            // centrado en la banda de la materia y los campos; el de 34, en la fila
            // de la materia. La zona lleva el anillo del raton y del foco.
            if (aspecto != null)
            {
                int alturaJunto = camposCorridos ? altoMateria + (altoCampos > 0 ? 10 + altoCampos : 0) : altoFilaMateria;
                Rectangle circulo = CirculoClase.Rect(x, yBanda + Math.Max(0, (alturaJunto - diametro) / 2), diametro);
                zonaCirculo = CirculoClase.ZonaPulsable(circulo);
                aspectoCirculo = aspecto;
                diametroCirculo = diametro;
            }

            // En curso, la linea de tiempo: su hora de inicio, la barra con lo que
            // lleva y su hora de fin (asi la barra dice que mide).
            if (avance.HasValue)
            {
                yy += 12;
                string desde = HorariosDatos.Hora(inicio);
                string hasta = HorariosDatos.Hora(fin);
                int anchoDesde = TextRenderer.MeasureText(g, desde, fuenteValorCampo, Size.Empty, SinMargen).Width;
                int anchoHasta = TextRenderer.MeasureText(g, hasta, fuenteValorCampo, Size.Empty, SinMargen).Width;
                TextRenderer.DrawText(g, desde, fuenteValorCampo, new Rectangle(x, yy, anchoDesde + 2, altoValorCampo), TextoPrincipal, Linea);
                TextRenderer.DrawText(g, hasta, fuenteValorCampo, new Rectangle(x + ancho - anchoHasta, yy, anchoHasta + 2, altoValorCampo), TextoPrincipal, Linea);
                Rectangle barra = new Rectangle(x + anchoDesde + 10, yy + (altoValorCampo - 8) / 2, Math.Max(20, ancho - anchoDesde - anchoHasta - 20), 8);
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(barra, 4))
                using (SolidBrush fondo = new SolidBrush(FondoBarra))
                {
                    g.FillPath(fondo, forma);
                }

                int lleno = (int)Math.Round(barra.Width * Math.Max(0f, Math.Min(1f, avance.Value)));
                if (lleno >= 8)
                {
                    using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(new Rectangle(barra.Left, barra.Top, lleno, barra.Height), 4))
                    using (SolidBrush relleno = new SolidBrush(VerdeAhora))
                    {
                        g.FillPath(relleno, forma);
                    }
                }
            }

            y = ficha.Bottom;
        }

        private const int EntreCampos = 20;

        /// <summary>La letra de la materia: la grande si cabe en una linea, si no la mediana o la chica (y, ya sin remedio, con "...").</summary>
        private DrawingFont LetraQueCabe(Graphics g, string materia, int ancho)
        {
            foreach (DrawingFont letra in new[] { fuenteMateria, fuenteMateriaMediana })
            {
                if (TextRenderer.MeasureText(g, materia, letra, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width <= ancho)
                {
                    return letra;
                }
            }

            return fuenteMateriaChica;
        }

        /// <summary>Grupo, laboratorio (si lo hay) y, si se pide, horario, con el nombre que va encima de cada uno.</summary>
        private static List<KeyValuePair<string, string>> Campos(string grupo, string laboratorio, TimeSpan inicio, TimeSpan fin, bool conHorario)
        {
            List<KeyValuePair<string, string>> campos = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("GRUPO", grupo ?? "")
            };
            if (!string.IsNullOrWhiteSpace(laboratorio))
            {
                campos.Add(new KeyValuePair<string, string>("LABORATORIO", laboratorio.Trim()));
            }

            if (conHorario)
            {
                campos.Add(new KeyValuePair<string, string>("HORARIO", HorariosDatos.Hora(inicio) + " a " + HorariosDatos.Hora(fin)));
            }

            return campos;
        }

        /// <summary>Lo que ocupa un campo: su nombre o su dato, el mas ancho, medidos como se pintan (sin margen).</summary>
        private int AnchoCampo(Graphics g, KeyValuePair<string, string> campo)
        {
            const TextFormatFlags ComoSePinta = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            return Math.Max(TextRenderer.MeasureText(g, campo.Key, fuenteTituloCampo, Size.Empty, ComoSePinta).Width,
                TextRenderer.MeasureText(g, campo.Value, fuenteValorCampo, Size.Empty, ComoSePinta).Width) + 2;
        }

        /// <summary>Los campos en filas: en la misma mientras quepan (en una tarjeta angosta, el ultimo baja a otra).</summary>
        private List<List<KeyValuePair<string, string>>> RepartirCampos(Graphics g, List<KeyValuePair<string, string>> campos, int ancho)
        {
            List<List<KeyValuePair<string, string>>> filas = new List<List<KeyValuePair<string, string>>>();
            int usado = 0;
            foreach (KeyValuePair<string, string> campo in campos)
            {
                int medida = AnchoCampo(g, campo);
                if (filas.Count == 0 || usado + EntreCampos + medida > ancho)
                {
                    filas.Add(new List<KeyValuePair<string, string>>());
                    usado = medida;
                }
                else
                {
                    usado += EntreCampos + medida;
                }

                filas[filas.Count - 1].Add(campo);
            }

            return filas;
        }

        private void Pastilla(Graphics g, Rectangle r, string texto, Color color)
        {
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(r, r.Height / 2))
            using (SolidBrush fondo = new SolidBrush(color))
            {
                g.FillPath(fondo, forma);
            }

            TextRenderer.DrawText(g, texto, fuenteEstado, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        /// <summary>
        /// Como va la clase de la tarjeta, debajo del codigo: tres cifras en sus
        /// recuadros, las bitacoras registradas hasta el momento, las fallas que
        /// reportaron y los equipos que avisaron sin internet. De la sesion donde
        /// registran sus alumnos: la suya; la de su franja si la recortaron; o,
        /// con anterior (codigo fijo y otra clase suya ya empezada en el mismo
        /// laboratorio), la de esa, diciendolo encima. Con la lista de la clase
        /// (AlumnosEnLista, la de Mis grupos), la primera dice de cuantos ("16 de
        /// 24 alumnos") y cuantos aun no ("8 aun no"), y se pulsa para ver quien
        /// falta (zonaLista). Antes de que se acepte el codigo, desde cuando; sin
        /// clase, nada. Devuelve el alto (con pintar = false solo lo mide).
        /// Compacto: cada cifra en una linea ("3 bitácoras"); con la lista, la
        /// primera en un recuadro mas ancho.
        /// </summary>
        private int Registro(Graphics g, Rectangle zona, int y, SesionAgenda s, SesionAgenda anterior, TimeSpan hora, bool pintar, bool compacto)
        {
            if (s == null)
            {
                return 0;
            }

            const TextFormatFlags Centro = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
            TimeSpan desde = s.Inicio - TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            if (anterior == null && hora < desde)
            {
                string cuando = "Tus estudiantes podrán registrar desde las " + HorariosDatos.Hora(desde);
                int alto = AltoDe(g, cuando, fuenteRegistro, zona.Width);
                if (pintar)
                {
                    TextRenderer.DrawText(g, cuando, fuenteRegistro, new Rectangle(zona.Left, y, zona.Width, alto), Color.Gray, Centro);
                }

                return alto;
            }

            int arriba = y;
            if (anterior != null)
            {
                string aviso = "Hasta las " + HorariosDatos.Hora(s.Inicio) + " registran en tu clase de las " + HorariosDatos.Hora(anterior.Inicio) + ":";
                int altoAviso = AltoDe(g, aviso, fuenteTexto, zona.Width);
                if (pintar)
                {
                    TextRenderer.DrawText(g, aviso, fuenteTexto, new Rectangle(zona.Left, y, zona.Width, altoAviso), TextoSecundario, Centro);
                }

                y += altoAviso + 6;
            }

            SesionAgenda donde = DondeRegistran(s, anterior);
            int bitacoras = donde.Alumnos;
            // Las de sus alumnos y las que reporto el profesor (db/15); si las cifras
            // son de otra sesion (la de su franja recortada), tambien las suyas.
            int fallas = donde.ConFallas + donde.ReportesProfesor + (donde != s ? s.ReportesProfesor : 0);
            int sinInternet = donde.SinInternet == null ? 0 : donde.SinInternet.Count;
            // La lista de su clase (0: sin lista): de cuantos y cuantos aun no. Con
            // ella, la primera cifra son los de la lista que hoy ya cuentan (como
            // en Mis grupos, tambien los que llegaron antes a la clase anterior).
            int presentes;
            int enLista = AlumnosEnLista(s, anterior, out presentes);
            if (enLista > 0)
            {
                bitacoras = presentes;
            }

            int faltan = enLista - bitacoras;
            string deCuantos = enLista == 1 ? "de 1 estudiante" : "de " + enLista + " estudiantes";
            string cuantosFaltan = faltan == 0 ? "nadie falta" : faltan + " aún no";
            Color colorFaltan = faltan == 0 ? VerdeAhora : Color.FromArgb(190, 85, 0);
            KeyValuePair<int, string>[] cifras = compacto
                ? new[]
                {
                    new KeyValuePair<int, string>(bitacoras, bitacoras == 1 ? "bitácora" : "bitácoras"),
                    new KeyValuePair<int, string>(fallas, fallas == 1 ? "falla" : "fallas"),
                    new KeyValuePair<int, string>(sinInternet, "sin internet")
                }
                : new[]
                {
                    new KeyValuePair<int, string>(bitacoras, bitacoras == 1 ? "bitácora registrada" : "bitácoras registradas"),
                    new KeyValuePair<int, string>(fallas, fallas == 1 ? "falla reportada" : "fallas reportadas"),
                    new KeyValuePair<int, string>(sinInternet, sinInternet == 1 ? "equipo sin internet" : "equipos sin internet")
                };

            // Tres recuadros, el numero en grande y que es debajo, o (compacto) los
            // dos en una linea. Iguales; compactos y con la lista, el primero de la
            // mitad del ancho, para que diga de cuantos y cuantos aun no (las otras
            // dos, "0 fallas" y "0 sin internet", caben en lo que queda).
            const int Separacion = 10;
            const TextFormatFlags EnLinea = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            int util = zona.Width - 2 * Separacion;
            int anchoPrimero = Math.Max(30, enLista > 0 && compacto ? util / 2 : util / 3);
            int anchoResto = Math.Max(30, (util - anchoPrimero) / 2);
            DrawingFont fuenteNumero = compacto ? fuenteCifraCompacta : fuenteCifra;
            int altoNumero = TextRenderer.MeasureText(g, "0", fuenteNumero).Height;
            int altoEtiqueta = 0;
            if (!compacto)
            {
                // Con la lista, la primera en dos partes: de cuantos y, debajo, cuantos aun no.
                altoEtiqueta = Math.Max(cifras.Skip(1).Max(c => AltoDe(g, c.Value, fuenteEtiquetaCifra, anchoResto - 12)), enLista > 0
                    ? AltoDe(g, deCuantos, fuenteEtiquetaCifra, anchoPrimero - 12) + AltoDe(g, cuantosFaltan, fuenteFaltan, anchoPrimero - 12)
                    : AltoDe(g, cifras[0].Value, fuenteEtiquetaCifra, anchoPrimero - 12));
            }

            int altoCifra = compacto ? altoNumero + 8 : 6 + altoNumero + altoEtiqueta + 8;
            if (pintar)
            {
                int izquierda = zona.Left;
                for (int i = 0; i < cifras.Length; i++)
                {
                    Rectangle recuadro = new Rectangle(izquierda, y, i == 0 ? anchoPrimero : anchoResto, altoCifra);
                    izquierda = recuadro.Right + Separacion;
                    // La de la lista se pulsa (abre la Asistencia): con el raton encima, como las opciones.
                    bool conLista = i == 0 && enLista > 0;
                    bool resaltada = conLista && encimaLista;
                    using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(recuadro, 8))
                    using (SolidBrush fondo = new SolidBrush(resaltada ? FondoListaEncima : FondoFicha))
                    using (Pen borde = new Pen(resaltada ? Color.DarkGreen : BordeFicha, resaltada ? 2f : 1f))
                    {
                        g.FillPath(fondo, forma);
                        g.DrawPath(borde, forma);
                    }

                    if (conLista)
                    {
                        zonaLista = recuadro;
                    }

                    // Verde lo registrado; naranja si hay fallas o equipos sin internet; gris si es cero.
                    Color color = cifras[i].Key == 0 ? Color.FromArgb(150, 150, 150) : i == 0 ? VerdeAhora : Color.FromArgb(190, 85, 0);
                    string numero = cifras[i].Key.ToString(CultureInfo.InvariantCulture);
                    if (compacto)
                    {
                        // "3 bitácoras": el numero y, a su lado, que es; los dos centrados.
                        // Con la lista, lo mas que quepa: "de 24 alumnos, 8 aun no",
                        // "de 24, 8 aun no", "de 24 alumnos" o "de 24".
                        int anchoNumero = TextRenderer.MeasureText(g, numero, fuenteNumero, Size.Empty, TextFormatFlags.NoPadding).Width;
                        string etiqueta = cifras[i].Value;
                        string tras = null;
                        if (conLista)
                        {
                            int cabe = recuadro.Width - 8 - anchoNumero - 5;
                            Func<string, DrawingFont, int> medir = (t, f) => TextRenderer.MeasureText(g, t, f, Size.Empty, TextFormatFlags.NoPadding).Width;
                            string corto = "de " + enLista;
                            int conFaltan = 4 + medir(cuantosFaltan, fuenteFaltan);
                            if (medir(deCuantos + ",", fuenteEtiquetaCifra) + conFaltan <= cabe)
                            {
                                etiqueta = deCuantos + ",";
                                tras = cuantosFaltan;
                            }
                            else if (medir(corto + ",", fuenteEtiquetaCifra) + conFaltan <= cabe)
                            {
                                etiqueta = corto + ",";
                                tras = cuantosFaltan;
                            }
                            else
                            {
                                etiqueta = medir(deCuantos, fuenteEtiquetaCifra) <= cabe ? deCuantos : corto;
                            }
                        }

                        int anchoTexto = TextRenderer.MeasureText(g, etiqueta, fuenteEtiquetaCifra, Size.Empty, TextFormatFlags.NoPadding).Width;
                        int anchoTras = tras == null ? 0 : 4 + TextRenderer.MeasureText(g, tras, fuenteFaltan, Size.Empty, TextFormatFlags.NoPadding).Width;
                        int x = recuadro.Left + Math.Max(4, (recuadro.Width - anchoNumero - 5 - anchoTexto - anchoTras) / 2);
                        int xTexto = x + anchoNumero + 5;
                        TextRenderer.DrawText(g, numero, fuenteNumero, new Rectangle(x, recuadro.Top, anchoNumero + 2, recuadro.Height), color, EnLinea);
                        TextRenderer.DrawText(g, etiqueta, fuenteEtiquetaCifra,
                            new Rectangle(xTexto, recuadro.Top, Math.Max(10, recuadro.Right - 4 - xTexto), recuadro.Height), TextoSecundario,
                            EnLinea | TextFormatFlags.EndEllipsis);
                        if (tras != null)
                        {
                            TextRenderer.DrawText(g, tras, fuenteFaltan,
                                new Rectangle(xTexto + anchoTexto + 4, recuadro.Top, Math.Max(10, recuadro.Right - 4 - (xTexto + anchoTexto + 4)), recuadro.Height),
                                colorFaltan, EnLinea);
                        }

                        continue;
                    }

                    TextRenderer.DrawText(g, numero, fuenteNumero,
                        new Rectangle(recuadro.Left, recuadro.Top + 6, recuadro.Width, altoNumero), color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);
                    if (conLista)
                    {
                        int altoDeCuantos = AltoDe(g, deCuantos, fuenteEtiquetaCifra, recuadro.Width - 12);
                        TextRenderer.DrawText(g, deCuantos, fuenteEtiquetaCifra,
                            new Rectangle(recuadro.Left + 6, recuadro.Top + 6 + altoNumero, recuadro.Width - 12, altoDeCuantos), TextoSecundario, Centro);
                        TextRenderer.DrawText(g, cuantosFaltan, fuenteFaltan,
                            new Rectangle(recuadro.Left + 6, recuadro.Top + 6 + altoNumero + altoDeCuantos, recuadro.Width - 12, altoEtiqueta - altoDeCuantos),
                            colorFaltan, Centro);
                        continue;
                    }

                    TextRenderer.DrawText(g, cifras[i].Value, fuenteEtiquetaCifra,
                        new Rectangle(recuadro.Left + 6, recuadro.Top + 6 + altoNumero, recuadro.Width - 12, altoEtiqueta), TextoSecundario, Centro);
                }
            }

            return y + altoCifra - arriba;
        }

        /// <summary>
        /// Cuantos tiene la lista de la clase s y cuantos de ellos ya cuentan hoy
        /// (presentes), o 0 sin lista: sin listas leidas, mientras las cifras son
        /// de la sesion de otra clase (anterior: hasta que empiece, sus alumnos
        /// registran en la de antes) o si la lista aun no existia antes de hoy
        /// (el primer dia se forma con quienes registran: "5 de 5" no diria nada).
        /// </summary>
        private int AlumnosEnLista(SesionAgenda s, SesionAgenda anterior, out int presentes)
        {
            presentes = 0;
            ListaDeClase lista;
            if (listas == null || anterior != null || !listas.TryGetValue(s.ClaseId, out lista) || lista.AntesDeHoy == 0)
            {
                return 0;
            }

            presentes = Math.Min(lista.Hoy, lista.Alumnos);
            return lista.Alumnos;
        }

        /// <summary>El raton sobre la cifra de la lista: la mano, su ayuda y el recuadro resaltado.</summary>
        private void PonerEncimaLista(bool encima)
        {
            if (encima == encimaLista)
            {
                return;
            }

            encimaLista = encima;
            Cursor = encima ? Cursors.Hand : Cursors.Default;
            ayudaLista.SetToolTip(this, encima ? AyudaDeLaLista : null);
            if (!zonaLista.IsEmpty)
            {
                Invalidate(Rectangle.Inflate(zonaLista, 3, 3));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            PonerEncimaLista(!zonaLista.IsEmpty && zonaLista.Contains(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            PonerEncimaLista(false);
        }

        /// <summary>La cifra de la lista ("8 aun no"): la Asistencia de su clase, como la opcion de debajo.</summary>
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && !zonaLista.IsEmpty && zonaLista.Contains(e.Location)
                && claseAsistencia != null && VerAsistencia != null)
            {
                VerAsistencia(claseAsistencia);
            }
        }

        private static void Centrado(Graphics g, string texto, DrawingFont fuente, Color color, Rectangle zona, ref int y)
        {
            if (y >= zona.Bottom)
            {
                return;
            }

            TextFormatFlags formato = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
            Size medida = TextRenderer.MeasureText(g, texto, fuente, new Size(zona.Width, int.MaxValue), formato);
            TextRenderer.DrawText(g, texto, fuente, new Rectangle(zona.Left, y, zona.Width, Math.Min(medida.Height, zona.Bottom - y)), color, formato);
            y += medida.Height;
        }

        /// <summary>Una clase de otro dia, para la ficha (ProximaClase).</summary>
        private sealed class ClaseOtroDia
        {
            public DateTime Dia;
            // Para su circulo (db/19).
            public int ClaseId;
            public int LaboratorioId;
            public string Materia;
            public string Grupo;
            public string Laboratorio;
            public TimeSpan Inicio;
            public TimeSpan Fin;
        }

        /// <summary>
        /// Su primera clase de los proximos 7 dias. La del dia de la tercera
        /// columna (FechaManana: manana, o el siguiente dia con algo en su agenda)
        /// sale de su agenda real (sin las canceladas y con las extras: la que se
        /// ve en la columna de al lado); entre hoy y ese dia no tiene nada. Las de
        /// los dias de despues, de su horario semanal, que no sabe de cancelaciones.
        /// </summary>
        private ClaseOtroDia ProximaClase()
        {
            DateTime hoy = (ahora == DateTime.MinValue ? panorama.AhoraServidor : ahora).Date;
            int primerDia = 1;
            if (panorama.Manana != null && panorama.AhoraServidor.Date == hoy)
            {
                // El dia de la tercera columna, con la misma regla que ella (sin
                // FechaManana, de un panorama que no viene de PanoramaDeProfesor, manana).
                DateTime fechaManana = TarjetaTresDias.DiaDeManana(panorama, hoy);
                primerDia = (fechaManana - hoy).Days + 1;
                SesionAgenda manana = panorama.Manana.Where(s => !s.Cancelada && s.Utilizable).OrderBy(s => s.Inicio).FirstOrDefault();
                if (manana != null)
                {
                    return new ClaseOtroDia
                    {
                        Dia = fechaManana,
                        ClaseId = manana.ClaseId,
                        LaboratorioId = manana.LaboratorioId,
                        Materia = manana.Materia,
                        Grupo = manana.Grupo,
                        Laboratorio = panorama.Laboratorio(manana.LaboratorioId),
                        Inicio = manana.Inicio,
                        Fin = manana.Fin
                    };
                }
            }

            for (int d = primerDia; d <= 7; d++)
            {
                DateTime dia = hoy.AddDays(d);
                int diaSemana = ((int)dia.DayOfWeek + 6) % 7 + 1;
                FranjaInfo franja = panorama.Franjas
                    .Where(f => f.DiaSemana == diaSemana && f.Activa && dia >= f.VigenteDesde.Date && dia <= f.VigenteHasta.Date)
                    .OrderBy(f => f.Inicio)
                    .FirstOrDefault();
                if (franja != null)
                {
                    return new ClaseOtroDia
                    {
                        Dia = dia,
                        ClaseId = franja.ClaseId,
                        LaboratorioId = franja.LaboratorioId,
                        Materia = franja.Materia,
                        Grupo = franja.Grupo,
                        Laboratorio = franja.Laboratorio,
                        Inicio = franja.Inicio,
                        Fin = franja.Fin
                    };
                }
            }

            return null;
        }

        /// <summary>"manana, martes 13/10" solo si de verdad es manana; si no, "el lunes 9/11" (la fecha, como en las columnas de al lado).</summary>
        private string CuandoEs(DateTime dia)
        {
            DateTime hoy = (ahora == DateTime.MinValue ? panorama.AhoraServidor : ahora).Date;
            string nombre = TarjetaTresDias.DiaConFecha(dia);
            return (dia.Date - hoy).Days == 1 ? "mañana, " + nombre : "el " + nombre;
        }

        /// <summary>
        /// Generacion de codigos: el de la clase de la tarjeta (relevante: con dos
        /// seguidas en el margen de 15 minutos, la que empieza), en grande, y
        /// debajo la otra abierta con el suyo; si la de la tarjeta aun no tiene,
        /// "Abriendo..." (nunca en grande el de otra: se dictaria el codigo
        /// equivocado); si no hay ninguna abierta, cuando aparecera. Nunca el fijo
        /// en grande: si la que termina es de un laboratorio con otro modo
        /// (otraClase), una linea dice con que sigue esa. La materia, el grupo y
        /// el horario ya van arriba (DibujarClase).
        /// </summary>
        private void DibujarRenovable(Graphics g, Rectangle interior, int y, string titulo, List<SesionAgenda> abiertas, SesionAgenda proxima,
            SesionAgenda otraClase, SesionAgenda relevante, TimeSpan hora)
        {
            Color texto = Color.FromArgb(90, 90, 90);
            Color aviso = Color.FromArgb(190, 85, 0);
            List<SesionAgenda> conCodigo = abiertas.Where(s => !string.IsNullOrEmpty(s.CodigoSesion)).OrderByDescending(s => s.Inicio).ToList();
            bool deLaTarjeta = relevante != null && abiertas.Contains(relevante);
            SesionAgenda principal = deLaTarjeta ? (string.IsNullOrEmpty(relevante.CodigoSesion) ? null : relevante) : conCodigo.FirstOrDefault();
            if (principal != null)
            {
                // Debajo del codigo, como va su clase. Las notas, de la que importa en
                // este momento a la fija (si no caben, se quitan desde la ultima).
                SesionAgenda otra = conCodigo.FirstOrDefault(s => s != principal);
                DibujarCodigoConRegistro(g, interior, y, titulo, principal.CodigoSesion, principal, null, hora,
                    Nota(panorama.AvisoApertura, aviso),
                    Nota(otra == null ? null : "La de las " + HorariosDatos.Hora(otra.Inicio) + " (" + otra.Grupo + ") sigue con " + Mayusculas(otra.CodigoSesion)
                        + " hasta las " + HorariosDatos.Hora(otra.Fin) + ".", texto),
                    Nota(OtraClaseSigue(otraClase), texto),
                    Nota("Vale hasta las " + HorariosDatos.Hora(principal.Fin) + ". Díctalo ahora; cambia en cada clase.", texto));
                return;
            }

            y += 18;
            Centrado(g, titulo, fuenteTitulo, Color.DarkGreen, interior, ref y);
            y += 8;
            // Sin codigo que dictar, "Escuchar el codigo" apagado bajo lo que se ve en
            // su lugar, solo si cabe con todo lo de debajo: su nota, las otras abiertas
            // y lo que va al final (alFinal: con que sigue la de otro modo y el aviso).
            int alFinal = (otraClase != null ? 4 + AltoDe(g, OtraClaseSigue(otraClase), fuenteNota, interior.Width) : 0)
                + (string.IsNullOrEmpty(panorama.AvisoApertura) ? 0 : 6 + AltoDe(g, panorama.AvisoApertura, fuenteNota, interior.Width));
            if (abiertas.Count > 0)
            {
                string enUnMomento = "El código aparece aquí en un momento; si no, revisa la conexión.";
                List<string> siguen = conCodigo.Select(s => "La de las " + HorariosDatos.Hora(s.Inicio) + " (" + s.Grupo + ") sigue con " + Mayusculas(s.CodigoSesion)
                    + " hasta las " + HorariosDatos.Hora(s.Fin) + ".").ToList();
                int debajo = 10 + AltoDe(g, enUnMomento, fuenteNota, interior.Width) + siguen.Sum(n => 4 + AltoDe(g, n, fuenteNota, interior.Width)) + alFinal;
                Centrado(g, "Abriendo...", fuenteSinCodigo, Color.Gray, interior, ref y);
                y += EscucharApagado(interior, y, debajo, "Aún no hay código que dictar: tu clase se está abriendo.");
                y += 10;
                Centrado(g, enUnMomento, fuenteNota, texto, interior, ref y);
                foreach (string sigue in siguen)
                {
                    y += 4;
                    Centrado(g, sigue, fuenteNota, texto, interior, ref y);
                }
            }
            else if (proxima != null && panorama.ModoDe(proxima.LaboratorioId) == ModoCodigo.Renovable)
            {
                string aparece = HorariosDatos.Hora(proxima.Inicio - TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos));
                string cuando = "El código de tu clase de las " + HorariosDatos.Hora(proxima.Inicio) + " aparecerá aquí a las " + aparece + ". Cambia en cada clase.";
                Centrado(g, "Aún no", fuenteSinCodigo, Color.Gray, interior, ref y);
                y += EscucharApagado(interior, y, 10 + AltoDe(g, cuando, fuenteNota, interior.Width) + alFinal,
                    "Aún no hay código que dictar: aparece aquí a las " + aparece + ".");
                y += 10;
                Centrado(g, cuando, fuenteNota, texto, interior, ref y);
            }
            else
            {
                string sinMas = "Hoy ya no tienes más clases. En tus laboratorios el código cambia en cada clase y aparece aquí al empezar.";
                Centrado(g, "Sin clases", fuenteSinCodigo, Color.Gray, interior, ref y);
                y += EscucharApagado(interior, y, 10 + AltoDe(g, sinMas, fuenteNota, interior.Width) + alFinal, "Hoy ya no tienes clases con código que dictar.");
                y += 10;
                Centrado(g, sinMas, fuenteNota, texto, interior, ref y);
            }

            if (otraClase != null)
            {
                y += 4;
                Centrado(g, OtraClaseSigue(otraClase), fuenteNota, texto, interior, ref y);
            }

            if (!string.IsNullOrEmpty(panorama.AvisoApertura))
            {
                y += 6;
                Centrado(g, panorama.AvisoApertura, fuenteNota, aviso, interior, ref y);
            }
        }

        /// <summary>Con que sigue la que termina en un laboratorio con otro modo (sin codigo o con el fijo). Null sin ella.</summary>
        private string OtraClaseSigue(SesionAgenda otraClase)
        {
            return otraClase == null ? null
                : "La de las " + HorariosDatos.Hora(otraClase.Inicio) + " en " + panorama.Laboratorio(otraClase.LaboratorioId)
                    + (panorama.ModoDe(otraClase.LaboratorioId) == ModoCodigo.SinCodigo
                        ? " no necesita código."
                        : " sigue con tu código " + Mayusculas(otraClase.Codigo) + " hasta las " + HorariosDatos.Hora(otraClase.Fin) + ".");
        }

        /// <summary>Como queda el codigo: su letra, lo que mide y el aire entre letras.</summary>
        private struct MedidaCodigo
        {
            public float Tamano;
            public int[] Anchos;
            public int Espacio;
            public int Ancho;
            public int Alto;
            // Lo que queda vacio bajo la linea cuando ninguna letra baja de ella
            // (sin g, j, p, q, y ni Q): el texto de debajo sube eso.
            public int Recorte;
        }

        // La ultima medida del codigo (MedirCodigo) y su letra: el borde late 20
        // veces por segundo y cada cuadro la pide igual; solo se recalcula si
        // cambian el codigo o el sitio.
        private string codigoMedido;
        private Size sitioMedido;
        private MedidaCodigo medidaGuardada;
        private DrawingFont letraCodigo;

        /// <summary>
        /// El codigo lo mas grande que quepa en ese ancho y alto: el mayor tamano de
        /// 24 a TamanoCodigo pt, al medio punto (busqueda binaria). Si ni el menor
        /// cabe, el menor.
        /// </summary>
        private MedidaCodigo MedirCodigo(Graphics g, string codigo, int ancho, int alto)
        {
            Size sitio = new Size(ancho, alto);
            if (letraCodigo != null && codigo == codigoMedido && sitio == sitioMedido)
            {
                return medidaGuardada;
            }

            Func<MedidaCodigo, bool> cabe = m => m.Ancho <= ancho && m.Alto <= alto;
            MedidaCodigo mejor = MedirConTamano(g, codigo, TamanoCodigo);
            if (!cabe(mejor))
            {
                float menor = 24f;
                float mayor = TamanoCodigo;
                mejor = MedirConTamano(g, codigo, menor);
                while (mayor - menor > 0.5f)
                {
                    float medio = (menor + mayor) / 2f;
                    MedidaCodigo prueba = MedirConTamano(g, codigo, medio);
                    if (cabe(prueba))
                    {
                        menor = medio;
                        mejor = prueba;
                    }
                    else
                    {
                        mayor = medio;
                    }
                }
            }

            if (letraCodigo != null)
            {
                letraCodigo.Dispose();
            }

            letraCodigo = new DrawingFont("Consolas", mejor.Tamano, FontStyle.Bold);
            codigoMedido = codigo;
            sitioMedido = sitio;
            medidaGuardada = mejor;
            return mejor;
        }

        private static MedidaCodigo MedirConTamano(Graphics g, string codigo, float tamano)
        {
            using (DrawingFont fuente = new DrawingFont("Consolas", tamano, FontStyle.Bold))
            {
                int espacio = (int)(tamano / 5);
                Size[] medidas = codigo.Select(c => TextRenderer.MeasureText(g, c.ToString(), fuente, Size.Empty, TextFormatFlags.NoPadding)).ToArray();
                FontFamily familia = fuente.FontFamily;
                float bajoLaLinea = fuente.GetHeight(g) * familia.GetCellDescent(FontStyle.Bold) / familia.GetLineSpacing(FontStyle.Bold);
                bool bajan = codigo.Any(c => "gjpqyQ".IndexOf(c) >= 0);
                return new MedidaCodigo
                {
                    Tamano = tamano,
                    Anchos = medidas.Select(m => m.Width).ToArray(),
                    Espacio = espacio,
                    Ancho = medidas.Sum(m => m.Width) + espacio * Math.Max(0, codigo.Length - 1),
                    Alto = medidas.Length == 0 ? 0 : medidas.Max(m => m.Height),
                    Recorte = bajan ? 0 : (int)(bajoLaLinea * 0.6f)
                };
            }
        }

        /// <summary>Letra por letra, con aire entre ellas, en una fuente donde la O, el 0, la I y la l no se parecen.</summary>
        private void DibujarCodigo(Graphics g, string codigo, MedidaCodigo medida, Point origen)
        {
            int x = origen.X;
            for (int i = 0; i < codigo.Length; i++)
            {
                TextRenderer.DrawText(g, codigo[i].ToString(), letraCodigo, new Point(x, origen.Y), Color.DarkGreen, TextFormatFlags.NoPadding);
                x += medida.Anchos[i] + medida.Espacio;
            }
        }

        /// <summary>Sin la migracion 09: un codigo por clase.</summary>
        private void DibujarLista(Graphics g, Rectangle interior, int y)
        {
            y += 8;
            if (panorama.Clases.Count == 0)
            {
                Texto(g, "No tienes clases vigentes. La administración de DigBit crea las clases y su código.", fuenteTexto, Color.FromArgb(70, 70, 70), interior, ref y, true);
                return;
            }

            Texto(g, "Cada clase tiene el suyo; díctale a cada grupo el de su clase.", fuenteTexto, Color.FromArgb(70, 70, 70), interior, ref y, true);
            y += 8;
            foreach (ClaseInfo clase in panorama.Clases)
            {
                int altoCodigo = TextRenderer.MeasureText(g, "Ág", fuenteCodigoLista).Height;
                if (y + altoCodigo > interior.Bottom)
                {
                    break;
                }

                TextRenderer.DrawText(g, Mayusculas(clase.Codigo), fuenteCodigoLista, new Point(interior.Left, y), Color.DarkGreen);
                TextRenderer.DrawText(g, clase.Materia + ", " + clase.Grupo, fuenteTexto,
                    new Rectangle(interior.Left + 130, y + 4, interior.Width - 130, altoCodigo), Color.FromArgb(50, 50, 50),
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                y += altoCodigo + 4;
            }
        }

        private static void Texto(Graphics g, string texto, DrawingFont fuente, Color color, Rectangle interior, ref int y, bool variasLineas)
        {
            if (y >= interior.Bottom)
            {
                return;
            }

            TextFormatFlags formato = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix
                | (variasLineas ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Size medida = TextRenderer.MeasureText(g, texto, fuente, new Size(interior.Width, int.MaxValue), formato);
            TextRenderer.DrawText(g, texto, fuente, new Rectangle(interior.Left, y, interior.Width, Math.Min(medida.Height, interior.Bottom - y)), color, formato);
            y += medida.Height;
        }
    }

    /// <summary>
    /// Sus clases de tres dias como el horario de Horarios: el ultimo antes de
    /// hoy con algo en su agenda (ayer, si lo tuvo), hoy y el siguiente (manana,
    /// si lo tiene); la cabecera dice cual es (Cabecera). Una
    /// columna por dia, una fila por hora (las de la escuela, ampliadas a las de
    /// sus clases) y los huecos vacios a la vista. Cada clase es un bloque del
    /// alto de su duracion que dice, un dato por linea y con palabras, la
    /// materia, el grupo, el laboratorio, el horario y como va (todo, tambien al
    /// pasar el raton); su color dice como fue: verde con cuantas bitacoras
    /// hubo, rojo si nadie registro, gris si se cancelo; las que aun no
    /// empiezan, claras. Cada hora mide al menos lo que una clase necesita para
    /// leerse: si asi no caben todas, las horas se desplazan (barra y rueda del
    /// raton) y la vista empieza en la hora de ahora. La que esta en curso late
    /// en verde (Resplandor, como en el panel del administrador) y una linea roja
    /// marca la hora en la columna de hoy. Un clic en una que ya empezo o paso
    /// abre quien registro (BitacoraSesion). Con db/19 cada bloque lleva un lomo
    /// del color de su clase y, si cabe, su circulo (con el nombre corto, si lo
    /// tiene); un clic izquierdo en el circulo abre "Personalizar tu clase".
    /// </summary>
    internal class TarjetaTresDias : Control
    {
        private const int Relleno = 16;
        private const int AnchoHoras = 92;
        private const int AltoCabecera = 42;
        private static readonly string[] Dias = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };

        private static readonly Color Verde = Color.DarkGreen;
        private static readonly Color TextoPrincipal = Color.FromArgb(30, 30, 30);
        private static readonly Color TextoSecundario = Color.FromArgb(80, 80, 80);
        private static readonly Color TextoApagado = Color.FromArgb(140, 140, 140);
        private static readonly Color Naranja = Color.FromArgb(190, 85, 0);
        private static readonly Color Rejilla = Color.FromArgb(215, 215, 215);
        private static readonly Color FondoCabecera = Color.FromArgb(240, 248, 240);
        private static readonly Color FondoCabeceraHoy = Color.FromArgb(220, 240, 222);
        private static readonly Color FondoColumnaHoy = Color.FromArgb(249, 253, 249);
        private static readonly Color FondoConBitacoras = Color.FromArgb(222, 241, 224);
        private static readonly Color BordeConBitacoras = Color.FromArgb(56, 142, 60);
        private static readonly Color FondoSinBitacoras = Color.FromArgb(253, 228, 224);
        private static readonly Color BordeSinBitacoras = Color.FromArgb(198, 40, 40);
        private static readonly Color FondoEnCurso = Color.FromArgb(212, 242, 215);
        private static readonly Color FondoPronto = Color.FromArgb(255, 246, 220);
        private static readonly Color BordePronto = Color.FromArgb(170, 80, 0);
        private static readonly Color FondoPorVenir = Color.FromArgb(244, 247, 244);
        private static readonly Color BordePorVenir = Color.FromArgb(175, 190, 175);
        private static readonly Color FondoCancelada = Color.FromArgb(236, 236, 236);
        private static readonly Color BordeCancelada = Color.FromArgb(185, 185, 185);
        private static readonly Color LineaAhora = Color.FromArgb(220, 40, 40);

        private readonly DrawingFont fuenteDia = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteFecha = new DrawingFont("Century Gothic", 8.5F);
        private readonly DrawingFont fuenteHoras = new DrawingFont("Century Gothic", 8F);
        private readonly DrawingFont fuenteMateria = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private readonly DrawingFont fuenteDetalle = new DrawingFont("Century Gothic", 8.25F);
        private readonly DrawingFont fuenteEtiqueta = new DrawingFont("Century Gothic", 7.75F, FontStyle.Bold);
        private readonly Timer animacion = new Timer { Interval = Resplandor.IntervaloMs };
        private readonly List<Zona> zonas = new List<Zona>();
        private readonly ToolTip ayuda = new ToolTip { InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 20000 };
        // Para desplazar las horas cuando no caben todas (Acomodar la pone o la quita).
        private readonly VScrollBar barra = new VScrollBar { Visible = false, TabStop = false };

        private PanoramaProfesor panorama;
        private DateTime ahora;
        private int encima = -1;
        // El raton esta en el circulo del bloque de encima: su anillo, la mano y su ayuda.
        private bool encimaCirculo;
        // Lo que miden la materia y el detalle de un bloque (las letras no cambian):
        // el circulo se calcula en cada cuadro, tambien en los bloques que no se repintan.
        private int altoMateriaBloque;
        private int altoDetalleBloque;
        private bool ratonDentro;
        private string textoAyuda;
        private Rectangle? enCurso;
        // Cuantos pixeles de horas quedan por encima de la vista (Acomodar los
        // saca de horaArriba, la hora que se quiere arriba: asi un cambio de
        // tamano no mueve la vista a otra hora).
        private int desplazamiento;
        private double horaArriba;
        // Mientras nadie la desplace, la vista sigue la hora de ahora (Acomodar,
        // cada minuto y en cada recarga); si la desplazan (barra o rueda), manda
        // quien la desplazo, pero solo SegundosAMano desde su ultimo movimiento:
        // despues vuelve sola a la hora de ahora (antes se quedaba asi hasta el dia
        // siguiente, y una vuelta de rueda sin querer dejaba la clase en curso
        // fuera de la vista todo el dia). Tambien vuelve al cambiar el dia de los
        // datos (diaColocado) y al empezar otra clase de hoy.
        private const int SegundosAMano = 120;
        private DateTime diaColocado = DateTime.MinValue;
        private bool desplazadaAMano;
        // Reloj del equipo: Momento es el del servidor y salta con su desfase en cada recarga.
        private DateTime desplazadaEn;
        private string claseEnCursoColocada;
        // Lo que se pinto la ultima vez: los clics fuera de las horas no cuentan y la rueda baja una hora.
        private Rectangle cuerpoPintado;
        private float altoHoraPintado = 40f;

        // NoCuenta: ya paso sin bitacoras, pero Faltas no la juzga (el laboratorio
        // aun no registraba con DigBit, o una extra anotada despues de su hora).
        private enum Estado { ConBitacoras, SinBitacoras, NoCuenta, EnCurso, Pronto, PorVenir, Cancelada }

        /// <summary>Donde va cada cosa con el tamano y los datos de ahora (lo comparten el pintado y la barra).</summary>
        private sealed class Geometria
        {
            public Rectangle Caja;
            public Rectangle Interior;
            public Rectangle Tabla;
            // Las horas: lo que se desplaza, debajo de la cabecera de los dias.
            public Rectangle Cuerpo;
            public int AltoPie;
            public int Desde;
            public int Hasta;
            public float AltoHora;
            // El alto de todas las horas; si pasa del cuerpo, hace falta la barra.
            public int Contenido;
            public int AnchoBarra;
            public int AnchoColumna;
            public DateTime Dia;
            public DateTime[] Fechas;
            public List<SesionAgenda>[] Clases;
        }

        /// <summary>Donde quedo cada bloque al pintar, para saber cual se pulsa.</summary>
        private sealed class Zona
        {
            public Rectangle Rect;
            public SesionAgenda Clase;
            public DateTime Fecha;
            public bool Abre;
            // Todo lo de la clase, al pasar el raton (en un bloque bajo no cabe).
            public string Ayuda;
            // Su circulo (db/19), o vacio si no lleva (sin la 19 o sin sitio).
            public Rectangle Circulo;
            public AspectoClase Aspecto;
        }

        /// <summary>Quien registro en esa clase ese dia.</summary>
        public event Action<SesionAgenda, DateTime> VerRegistro;

        /// <summary>El circulo de un bloque: "Personalizar tu clase" de esa clase (su id).</summary>
        public event Action<int> Personalizar;

        public TarjetaTresDias()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            barra.Scroll += (s, e) => Desplazar(e.NewValue);
            Controls.Add(barra);
            animacion.Tick += (s, e) =>
            {
                // Solo la que late (con su halo): repintar todo cada 50 ms seria tirar CPU.
                if (enCurso.HasValue)
                {
                    Invalidate(Rectangle.Inflate(enCurso.Value, Resplandor.Margen + 3, Resplandor.Margen + 3));
                }
            };
            animacion.Start();
        }

        /// <summary>Hora del servidor; la pone el panel cada segundo (se repinta al cambiar el minuto).</summary>
        public DateTime Ahora
        {
            set
            {
                bool otroDia = value.Date != ahora.Date;
                bool otroMinuto = value.Hour != ahora.Hour || value.Minute != ahora.Minute || otroDia;
                ahora = value;
                // Cada minuto: asi vuelve a la hora de ahora aunque no lleguen datos.
                if (otroMinuto)
                {
                    Acomodar();
                }

                if (otroMinuto)
                {
                    Invalidate();
                }
            }
        }

        public void Mostrar(PanoramaProfesor datos)
        {
            panorama = datos;
            Acomodar();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Acomodar();
        }

        /// <summary>
        /// Donde va cada cosa. Las horas son las de la escuela, ampliadas a las de
        /// sus clases de estos tres dias; cada una se estira para llenar el alto,
        /// pero sin bajar de lo que necesita una clase para leerse (AltoHoraMinimo):
        /// si asi no caben, se desplazan con la barra.
        /// </summary>
        private Geometria Medir(Graphics g)
        {
            Geometria geo = new Geometria();
            geo.Caja = new Rectangle(Resplandor.Margen + 1, Resplandor.Margen + 1,
                Width - 2 * Resplandor.Margen - 3, Height - 2 * Resplandor.Margen - 3);
            geo.Interior = Rectangle.Inflate(geo.Caja, -Relleno, -14);

            // Las columnas son las del dia de los datos: pasada la medianoche, hasta
            // que llegan los nuevos, siguen siendo las de ayer (OnPaint las juzga asi).
            // La primera y la tercera, los dias de Ayer y Manana del panorama: el
            // ultimo antes de hoy con algo en su agenda y el siguiente (ayer y
            // manana si los tiene). Sin ellos (un panorama de otra parte), ayer y manana.
            geo.Dia = panorama.AhoraServidor.Date;
            geo.Fechas = new[] { DiaDeAyer(panorama, geo.Dia), geo.Dia, DiaDeManana(panorama, geo.Dia) };
            // Una franja cancelada que dejo restos (la recortaron) no se pinta: la
            // representan ellos, que cuentan con sus bitacoras.
            geo.Clases = new[] { panorama.Ayer, panorama.Hoy, panorama.Manana }
                .Select(dia => (dia ?? new List<SesionAgenda>()).Where(s => !TapadaPorSusRestos(s, dia)).ToList())
                .ToArray();

            geo.Desde = EstiloHorarios.HoraApertura;
            geo.Hasta = EstiloHorarios.HoraCierre;
            foreach (SesionAgenda s in geo.Clases.SelectMany(c => c))
            {
                geo.Desde = Math.Min(geo.Desde, (int)Math.Floor(s.Inicio.TotalHours));
                geo.Hasta = Math.Max(geo.Hasta, (int)Math.Ceiling(s.Fin.TotalHours));
            }

            geo.Desde = Math.Max(0, Math.Min(23, geo.Desde));
            geo.Hasta = Math.Min(24, Math.Max(geo.Hasta, geo.Desde + 1));

            geo.AltoPie = TextRenderer.MeasureText(g, "Ág", fuenteFecha).Height + 6;
            geo.Tabla = new Rectangle(geo.Interior.Left, geo.Interior.Top, geo.Interior.Width, Math.Max(AltoCabecera + 1, geo.Interior.Height - geo.AltoPie));
            geo.Cuerpo = new Rectangle(geo.Tabla.Left, geo.Tabla.Top + AltoCabecera, geo.Tabla.Width, geo.Tabla.Height - AltoCabecera);

            // Se desplaza solo si repartir el alto deja las horas por debajo del
            // minimo (comparar el total redondeado sacaba la barra por un pixel).
            int horas = geo.Hasta - geo.Desde;
            float repartido = geo.Cuerpo.Height / (float)horas;
            float minimo = AltoHoraMinimo(g);
            bool desplaza = repartido < minimo;
            geo.AltoHora = desplaza ? minimo : repartido;
            geo.Contenido = desplaza ? (int)Math.Ceiling(horas * minimo) : geo.Cuerpo.Height;
            geo.AnchoBarra = desplaza ? SystemInformation.VerticalScrollBarWidth : 0;
            geo.AnchoColumna = Math.Max(30, (geo.Tabla.Width - AnchoHoras - geo.AnchoBarra) / 3);
            return geo;
        }

        /// <summary>El dia de la primera columna: FechaAyer (siempre antes de hoy), o ayer sin ella.</summary>
        internal static DateTime DiaDeAyer(PanoramaProfesor datos, DateTime hoy)
        {
            DateTime fecha = datos == null ? DateTime.MinValue : datos.FechaAyer.Date;
            return fecha == DateTime.MinValue || fecha >= hoy.Date ? hoy.Date.AddDays(-1) : fecha;
        }

        /// <summary>El dia de la tercera columna: FechaManana (siempre despues de hoy), o manana sin ella.</summary>
        internal static DateTime DiaDeManana(PanoramaProfesor datos, DateTime hoy)
        {
            DateTime fecha = datos == null ? DateTime.MinValue : datos.FechaManana.Date;
            return fecha == DateTime.MinValue || fecha <= hoy.Date ? hoy.Date.AddDays(1) : fecha;
        }

        /// <summary>"9/10", "15/10": el dia sin cero delante y el mes con dos cifras (cabeceras y "Tu proxima clase es...").</summary>
        internal static string Fecha(DateTime dia)
        {
            return dia.ToString("d/MM", CultureInfo.InvariantCulture);
        }

        /// <summary>"viernes 9/10": el dia en minusculas y su fecha (como "Tu proxima clase es..." y la bitacora que abre un bloque).</summary>
        internal static string DiaConFecha(DateTime dia)
        {
            return Dias[(int)dia.DayOfWeek] + " " + Fecha(dia);
        }

        /// <summary>
        /// Lo que dice la cabecera de cada columna: arriba (en verde y negrita) y
        /// debajo. Hoy, "Hoy" y su fecha. Ayer y manana, igual, si lo son ("Ayer",
        /// "Miercoles 14/10"); si la columna es de otro dia (el ultimo antes de hoy
        /// con clases, o el siguiente), su fecha arriba ("Viernes 9/10") y debajo
        /// "Ultimo dia con clases" o "Siguiente dia con clases". Un dia sin
        /// ninguna clase lo dice (una columna en blanco no se distingue de
        /// "cargando" o de un fallo).
        /// </summary>
        internal static void Cabecera(int columna, DateTime fecha, DateTime hoy, bool sinClases, out string arriba, out string debajo)
        {
            string nombre = Dias[(int)fecha.DayOfWeek];
            string conFecha = char.ToUpper(nombre[0]) + nombre.Substring(1) + " " + Fecha(fecha);
            int distancia = (fecha.Date - hoy.Date).Days;
            bool deAlLado = columna == 1 || (columna == 0 && distancia == -1) || (columna == 2 && distancia == 1);
            if (deAlLado)
            {
                arriba = columna == 0 ? "Ayer" : columna == 1 ? "Hoy" : "Mañana";
                debajo = conFecha + (sinClases ? ", sin clases" : "");
                return;
            }

            arriba = conFecha;
            debajo = columna == 0 ? "Último día con clases" : "Siguiente día con clases";
        }

        /// <summary>
        /// Una franja cancelada ese dia que tiene restos: extras de su misma clase
        /// y laboratorio dentro de sus horas (lo que le quedo al recortarla, la
        /// regla de HorariosDatos.MarcarRestos).
        /// </summary>
        private static bool TapadaPorSusRestos(SesionAgenda franja, List<SesionAgenda> delDia)
        {
            return franja.Cancelada && !franja.EsExtra && delDia.Any(r => r.EsExtra && !r.Cancelada && r.ClaseId == franja.ClaseId
                && r.LaboratorioId == franja.LaboratorioId && r.Inicio >= franja.Inicio && r.Fin <= franja.Fin);
        }

        /// <summary>
        /// Lo minimo que mide una hora para que cada clase se lea entera: una de
        /// una hora, con la materia y una linea mas; una de dos, con la materia y
        /// tres (grupo, laboratorio y horario). Cuenta los margenes del bloque (2
        /// por arriba y por abajo, y 3 dentro).
        /// </summary>
        private float AltoHoraMinimo(Graphics g)
        {
            const int Margenes = 4 + 6;
            int primera = PrimeraLinea(g);
            int detalle = TextRenderer.MeasureText(g, "Ág", fuenteDetalle).Height;
            return Math.Max(primera + detalle + Margenes, (primera + 3 * detalle + Margenes) / 2f);
        }

        /// <summary>El alto de la primera linea de un bloque: la materia, o la pastilla si es mas alta.</summary>
        private int PrimeraLinea(Graphics g)
        {
            int pastilla = TextRenderer.MeasureText(g, "Ág", fuenteEtiqueta, Size.Empty, TextFormatFlags.NoPadding).Height + 3;
            return Math.Max(TextRenderer.MeasureText(g, "Ág", fuenteMateria).Height, pastilla) + 1;
        }

        /// <summary>
        /// La barra de las horas: solo si no caben, con su recorrido, y el
        /// desplazamiento dentro de el. Mientras nadie la desplace, en cada recarga
        /// la vista se pone en la hora de ahora (con la anterior arriba, para ver
        /// la clase que termina), asi sigue al dia sin que nadie la toque.
        /// </summary>
        private void Acomodar()
        {
            if (panorama == null || Width <= 0 || Height <= 0)
            {
                return;
            }

            Geometria geo;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                geo = Medir(g);
            }

            bool deHoy = Momento.Date == geo.Dia;
            string enCursoAhora = deHoy ? ClaveEnCurso(geo.Clases[1], Momento.TimeOfDay) : null;
            if (geo.Dia != diaColocado
                || (desplazadaAMano && DateTime.UtcNow - desplazadaEn > TimeSpan.FromSeconds(SegundosAMano))
                || (enCursoAhora != null && enCursoAhora != claseEnCursoColocada))
            {
                diaColocado = geo.Dia;
                desplazadaAMano = false;
            }

            claseEnCursoColocada = enCursoAhora;
            if (!desplazadaAMano)
            {
                horaArriba = deHoy ? HoraParaEmpezar(geo.Clases[1], Momento.TimeOfDay) : 0;
                // La linea de la hora de ahora siempre a la vista, con media hora
                // debajo (una clase larga que empezo temprano la dejaba abajo).
                if (deHoy)
                {
                    horaArriba = Math.Max(horaArriba, Momento.TimeOfDay.TotalHours + 0.5 - geo.Cuerpo.Height / (double)geo.AltoHora);
                }
            }

            int maximo = Math.Max(0, geo.Contenido - geo.Cuerpo.Height);
            desplazamiento = Math.Max(0, Math.Min(maximo, (int)Math.Round((horaArriba - geo.Desde) * geo.AltoHora)));
            if (maximo == 0)
            {
                barra.Visible = false;
                return;
            }

            barra.SetBounds(geo.Tabla.Right - geo.AnchoBarra, geo.Cuerpo.Top, geo.AnchoBarra, geo.Cuerpo.Height);
            barra.Minimum = 0;
            barra.Maximum = geo.Contenido - 1;
            barra.LargeChange = Math.Max(1, geo.Cuerpo.Height);
            barra.SmallChange = Math.Max(1, (int)(geo.AltoHora / 2));
            barra.Value = desplazamiento;
            barra.Visible = true;
        }

        /// <summary>La clase de hoy en curso a esa hora ("f" + franja o "x" + extra), o null: al empezar otra, la vista vuelve a la hora de ahora.</summary>
        private static string ClaveEnCurso(List<SesionAgenda> deHoy, TimeSpan hora)
        {
            SesionAgenda s = deHoy.Where(c => !c.Cancelada && c.Inicio <= hora && hora < c.Fin).OrderByDescending(c => c.Inicio).FirstOrDefault();
            return s == null ? null : s.EsExtra ? "x" + s.ExcepcionId : "f" + s.FranjaId;
        }

        /// <summary>
        /// La hora que va arriba al abrir: la anterior a la de ahora (para ver la
        /// clase que acaba de terminar), pero sin cortar una clase de hoy: si una
        /// pasa por esa hora, desde que empieza.
        /// </summary>
        private static double HoraParaEmpezar(List<SesionAgenda> deHoy, TimeSpan hora)
        {
            TimeSpan arriba = TimeSpan.FromHours(Math.Max(0, Math.Floor(hora.TotalHours) - 1));
            bool movida = true;
            while (movida)
            {
                // Siempre hacia antes: termina (como mucho, en las 00:00).
                movida = false;
                foreach (SesionAgenda s in deHoy)
                {
                    if (s.Inicio < arriba && arriba < s.Fin)
                    {
                        arriba = TimeSpan.FromHours(Math.Floor(s.Inicio.TotalHours));
                        movida = true;
                    }
                }
            }

            return arriba.TotalHours;
        }

        /// <summary>La rueda del raton desplaza las horas: una por paso.</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!barra.Visible)
            {
                return;
            }

            int maximo = Math.Max(0, barra.Maximum - barra.LargeChange + 1);
            int nuevo = Math.Max(0, Math.Min(maximo, desplazamiento - (int)Math.Round(e.Delta / 120f * altoHoraPintado)));
            if (nuevo != desplazamiento)
            {
                barra.Value = nuevo;
                Desplazar(nuevo);
            }
        }

        /// <summary>Lo que pide quien desplaza (barra o rueda): esos pixeles y, para los cambios de tamano, la hora que queda arriba.</summary>
        private void Desplazar(int pixeles)
        {
            desplazamiento = pixeles;
            desplazadaAMano = true;
            desplazadaEn = DateTime.UtcNow;
            if (panorama != null)
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    Geometria geo = Medir(g);
                    horaArriba = geo.Desde + pixeles / (double)geo.AltoHora;
                }
            }

            Invalidate();
        }

        private DateTime Momento
        {
            get { return ahora == DateTime.MinValue && panorama != null ? panorama.AhoraServidor : ahora; }
        }

        private static Estado EstadoDe(SesionAgenda s, DateTime fecha, DateTime hoy, TimeSpan hora)
        {
            if (s.Cancelada)
            {
                return Estado.Cancelada;
            }

            if (fecha < hoy || (fecha == hoy && s.Fin <= hora))
            {
                // Como Faltas: con las que quedaron en otra sesion (un recorte, los
                // que llegaron antes) y sin juzgar lo que alli no cuenta.
                return s.BitacorasDeLaClase > 0 ? Estado.ConBitacoras : s.NoContaba ? Estado.NoCuenta : Estado.SinBitacoras;
            }

            if (fecha == hoy && s.Inicio <= hora)
            {
                return Estado.EnCurso;
            }

            if (fecha == hoy && hora >= s.Inicio - TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos))
            {
                return Estado.Pronto;
            }

            return Estado.PorVenir;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            zonas.Clear();
            enCurso = null;
            if (panorama == null)
            {
                Rectangle caja = new Rectangle(Resplandor.Margen + 1, Resplandor.Margen + 1,
                    Width - 2 * Resplandor.Margen - 3, Height - 2 * Resplandor.Margen - 3);
                Marco(g, caja);
                TextRenderer.DrawText(g, "Cargando tus clases...", fuenteDetalle, Rectangle.Inflate(caja, -Relleno, -14), TextoApagado,
                    TextFormatFlags.Left | TextFormatFlags.Top);
                return;
            }

            Geometria geo = Medir(g);
            cuerpoPintado = geo.Cuerpo;
            altoHoraPintado = geo.AltoHora;
            // Con los datos de otro dia (pasada la medianoche sin poder recargar),
            // se juzgan al final de su dia: los de su "Hoy" ya pasaron y los de su
            // "Mañana" aun no (no tienen conteos: no pueden salir en rojo).
            bool alDia = geo.Dia == Momento.Date;
            DateTime hoy = alDia ? Momento.Date : geo.Dia;
            TimeSpan hora = alDia ? Momento.TimeOfDay : TimeSpan.FromHours(24);
            // El bloque bajo el raton se resalta por donde esta, no por el indice
            // de antes de recargar o desplazar.
            Point? raton = ratonDentro ? PointToClient(MousePosition) : (Point?)null;
            Rectangle tabla = geo.Tabla;
            int horas = geo.Hasta - geo.Desde;
            int anchoColumna = geo.AnchoColumna;
            int arriba = geo.Cuerpo.Top - desplazamiento;
            Func<TimeSpan, int> y = t => arriba + (int)Math.Round((t.TotalHours - geo.Desde) * geo.AltoHora);
            int abajo = y(TimeSpan.FromHours(geo.Hasta));
            int derecha = tabla.Left + AnchoHoras + 3 * anchoColumna;

            // Primero las horas, que se desplazan: la columna de hoy, una fila por
            // hora con los huecos vacios, las clases y la linea de ahora. Despues,
            // encima, lo fijo (la cabecera de los dias, el pie y el marco), que tapa
            // lo que se desplazo por debajo.
            using (SolidBrush fondo = new SolidBrush(FondoColumnaHoy))
            {
                g.FillRectangle(fondo, new Rectangle(tabla.Left + AnchoHoras + anchoColumna, arriba, anchoColumna, abajo - arriba));
            }

            using (Pen rejilla = new Pen(Rejilla))
            {
                for (int i = 0; i <= 3; i++)
                {
                    int x = tabla.Left + AnchoHoras + i * anchoColumna;
                    g.DrawLine(rejilla, x, arriba, x, abajo);
                }

                g.DrawLine(rejilla, tabla.Left, arriba, tabla.Left, abajo);
                for (int h = 0; h <= horas; h++)
                {
                    int yh = y(TimeSpan.FromHours(geo.Desde + h));
                    g.DrawLine(rejilla, tabla.Left, yh, derecha, yh);
                    if (h < horas)
                    {
                        // "07:00-08:00", como en Horarios.
                        TextRenderer.DrawText(g, HorariosDatos.Hora(TimeSpan.FromHours(geo.Desde + h)) + "-" + HorariosDatos.Hora(TimeSpan.FromHours(geo.Desde + h + 1)),
                            fuenteHoras, new Rectangle(tabla.Left, yh, AnchoHoras, y(TimeSpan.FromHours(geo.Desde + h + 1)) - yh), TextoSecundario,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                }
            }

            // La linea de la hora de ahora va debajo de las clases: la que esta en
            // curso la tapa (no tacha su materia) y se ve en los huecos; el punto,
            // encima de todo (despues de las clases).
            bool conLineaAhora = alDia && hora >= TimeSpan.FromHours(geo.Desde) && hora <= TimeSpan.FromHours(geo.Hasta);
            int yAhora = conLineaAhora ? y(hora) : 0;
            int izquierdaHoy = tabla.Left + AnchoHoras + anchoColumna;
            if (conLineaAhora)
            {
                using (Pen linea = new Pen(LineaAhora, 2f))
                {
                    g.DrawLine(linea, izquierdaHoy, yAhora, izquierdaHoy + anchoColumna, yAhora);
                }
            }

            // Las clases, cada una del alto de su duracion. Las que se cruzan, lado a
            // lado: cada grupo de clases que se cruzan reparte el ancho entre sus
            // carriles; las demas usan la columna entera.
            DateTime[] fechas = geo.Fechas;
            List<SesionAgenda>[] clases = geo.Clases;
            for (int i = 0; i < 3; i++)
            {
                int izquierda = tabla.Left + AnchoHoras + i * anchoColumna;
                List<SesionAgenda> delDia = clases[i].OrderBy(s => s.Inicio).ThenBy(s => s.Fin).ToList();
                int primera = 0;
                while (primera < delDia.Count)
                {
                    int ultima = primera;
                    TimeSpan finGrupo = delDia[primera].Fin;
                    while (ultima + 1 < delDia.Count && delDia[ultima + 1].Inicio < finGrupo)
                    {
                        ultima++;
                        finGrupo = delDia[ultima].Fin > finGrupo ? delDia[ultima].Fin : finGrupo;
                    }

                    List<TimeSpan> carriles = new List<TimeSpan>();
                    int[] carril = new int[ultima - primera + 1];
                    for (int k = primera; k <= ultima; k++)
                    {
                        int libre = carriles.FindIndex(fin => fin <= delDia[k].Inicio);
                        if (libre < 0)
                        {
                            carriles.Add(delDia[k].Fin);
                            libre = carriles.Count - 1;
                        }
                        else
                        {
                            carriles[libre] = delDia[k].Fin;
                        }

                        carril[k - primera] = libre;
                    }

                    int ancho = Math.Max(20, (anchoColumna - 8) / carriles.Count);
                    for (int k = primera; k <= ultima; k++)
                    {
                        int top = y(delDia[k].Inicio) + 2;
                        int bottom = Math.Max(top + 14, y(delDia[k].Fin) - 2);
                        DibujarBloque(g, new Rectangle(izquierda + 4 + carril[k - primera] * ancho, top, ancho - 2, bottom - top),
                            delDia[k], fechas[i], hoy, hora, geo.Cuerpo, raton, e.ClipRectangle);
                    }

                    primera = ultima + 1;
                }
            }

            // Lo que esta bajo el raton despues de recargar o desplazar: su cursor y su ayuda.
            bool circuloBajoRaton = false;
            int bajoRaton = raton.HasValue ? ZonaEn(raton.Value, out circuloBajoRaton) : -1;
            if (bajoRaton != encima || circuloBajoRaton != encimaCirculo)
            {
                encima = bajoRaton;
                encimaCirculo = circuloBajoRaton;
                Cursor = circuloBajoRaton || (bajoRaton >= 0 && zonas[bajoRaton].Abre) ? Cursors.Hand : Cursors.Default;
            }

            string ayudaBajoRaton = AyudaDe(bajoRaton, circuloBajoRaton);
            if (ayudaBajoRaton != textoAyuda)
            {
                textoAyuda = ayudaBajoRaton;
                ayuda.SetToolTip(this, ayudaBajoRaton);
            }

            // El punto de la hora de ahora, en el borde izquierdo de la columna de hoy.
            if (conLineaAhora)
            {
                using (SolidBrush punto = new SolidBrush(LineaAhora))
                {
                    g.FillEllipse(punto, izquierdaHoy - 4, yAhora - 4, 8, 8);
                }
            }

            // Lo fijo, encima: en blanco lo que se desplazo por encima o por debajo
            // de las horas; luego la cabecera de cada dia, el pie y el marco.
            // Sin suavizado: con AntiAlias, GDI+ centra cada pixel en la coordenada
            // entera y una banda que empieza en y = 0 cubria solo media fila 0, donde
            // asomaba a medias lo desplazado (una franjita de texto sobre el marco).
            g.SmoothingMode = SmoothingMode.None;
            using (SolidBrush blanco = new SolidBrush(BackColor))
            {
                // De lado a lado del control y hasta sus bordes: una clase desplazada
                // tambien se saldria del marco.
                g.FillRectangle(blanco, Rectangle.FromLTRB(0, 0, Width, geo.Cuerpo.Top));
                g.FillRectangle(blanco, Rectangle.FromLTRB(0, geo.Cuerpo.Bottom, Width, Height));
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen rejilla = new Pen(Rejilla))
            {
                for (int i = 0; i < 3; i++)
                {
                    Rectangle cabecera = new Rectangle(tabla.Left + AnchoHoras + i * anchoColumna, tabla.Top, anchoColumna, AltoCabecera);
                    using (SolidBrush fondo = new SolidBrush(i == 1 ? FondoCabeceraHoy : FondoCabecera))
                    {
                        g.FillRectangle(fondo, cabecera);
                    }

                    // "Ayer" y su fecha, o la fecha y "Ultimo dia con clases" (Cabecera).
                    string arribaCabecera;
                    string debajoCabecera;
                    Cabecera(i, fechas[i], geo.Dia, clases[i].Count == 0, out arribaCabecera, out debajoCabecera);
                    TextRenderer.DrawText(g, arribaCabecera, fuenteDia, new Rectangle(cabecera.Left, cabecera.Top + 3, cabecera.Width, cabecera.Height / 2),
                        Verde, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, debajoCabecera, fuenteFecha,
                        new Rectangle(cabecera.Left, cabecera.Top + cabecera.Height / 2 + 1, cabecera.Width, cabecera.Height / 2),
                        TextoSecundario, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    g.DrawLine(rejilla, cabecera.Left, cabecera.Top, cabecera.Left, cabecera.Bottom);
                }

                g.DrawLine(rejilla, derecha, tabla.Top, derecha, geo.Cuerpo.Top);
                g.DrawLine(rejilla, tabla.Left, tabla.Top, derecha, tabla.Top);
                g.DrawLine(rejilla, tabla.Left, tabla.Top, tabla.Left, geo.Cuerpo.Top);
                g.DrawLine(rejilla, tabla.Left, geo.Cuerpo.Top, derecha, geo.Cuerpo.Top);
                if (abajo >= geo.Cuerpo.Bottom)
                {
                    // La linea que cierra la rejilla (la ultima hora, o el corte si
                    // quedan horas abajo): la franja blanca la deja a medias.
                    g.DrawLine(rejilla, tabla.Left, geo.Cuerpo.Bottom, derecha, geo.Cuerpo.Bottom);
                }
            }

            TextRenderer.DrawText(g, "Pulsa una clase que ya empezó o terminó para ver quién registró y qué reportaron.", fuenteFecha,
                new Rectangle(geo.Interior.Left, geo.Interior.Bottom - geo.AltoPie + 6, geo.Interior.Width, geo.AltoPie), Color.FromArgb(110, 110, 110),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Marco(g, geo.Caja);
        }

        private static void Marco(Graphics g, Rectangle caja)
        {
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(caja, 12))
            using (Pen borde = new Pen(Verde, 2f))
            {
                g.DrawPath(borde, forma);
            }
        }

        private void DibujarBloque(Graphics g, Rectangle r, SesionAgenda s, DateTime fecha, DateTime hoy, TimeSpan hora, Rectangle cuerpo, Point? raton,
            Rectangle recorte)
        {
            Estado estado = EstadoDe(s, fecha, hoy, hora);
            // Una cancelada se abre si alguien alcanzo a registrar antes de cancelarla.
            bool abre = estado == Estado.ConBitacoras || estado == Estado.SinBitacoras || estado == Estado.EnCurso || estado == Estado.Pronto
                || (estado == Estado.Cancelada && s.Alumnos > 0);

            // Lo que eligio para esta clase (db/19): un lomo de su color y, si hay
            // sitio, su circulo. Sin eso (null), el bloque como siempre. Con lomo
            // el texto empieza 2 px mas a la derecha. El circulo se calcula antes
            // del recorte, como la zona: los bloques que no se repintan tambien se pulsan.
            AspectoClase aspecto = PersonalizacionClases.De(s.ClaseId);
            Rectangle t = aspecto == null ? Rectangle.Inflate(r, -7, -3) : Rectangle.FromLTRB(r.Left + 9, r.Top + 3, r.Right - 7, r.Bottom - 3);
            Rectangle circulo = aspecto == null ? Rectangle.Empty : CirculoDelBloque(g, t);
            int indice = zonas.Count;
            zonas.Add(new Zona { Rect = r, Clase = s, Fecha = fecha, Abre = abre, Circulo = circulo, Aspecto = aspecto });

            Color fondo;
            Color borde;
            string etiqueta;
            // Si la etiqueta no cabe en su pastilla, esta (la de la clase en curso, sin "En curso:").
            string etiquetaCorta = null;
            int bitacoras = s.BitacorasDeLaClase;
            string cuantas = bitacoras == 1 ? "1 bitácora" : bitacoras + " bitácoras";
            switch (estado)
            {
                case Estado.ConBitacoras:
                    fondo = FondoConBitacoras;
                    borde = BordeConBitacoras;
                    etiqueta = cuantas;
                    break;
                case Estado.NoCuenta:
                    // Gris: no se juzga (por que, en la ultima linea y al pasar el raton).
                    fondo = FondoCancelada;
                    borde = BordeCancelada;
                    etiqueta = "No cuenta";
                    break;
                case Estado.SinBitacoras:
                    fondo = FondoSinBitacoras;
                    borde = BordeSinBitacoras;
                    etiqueta = "Sin bitácoras";
                    break;
                case Estado.EnCurso:
                    // Con cuantas van: el numero se ve en cada clase, tambien en la de ahora.
                    fondo = FondoEnCurso;
                    borde = Resplandor.Borde();
                    etiqueta = "En curso: " + cuantas;
                    etiquetaCorta = cuantas;
                    // Late solo si se ve (desplazada fuera de la vista, no hay que repintarla).
                    enCurso = r.IntersectsWith(cuerpo) ? r : (Rectangle?)null;
                    break;
                case Estado.Pronto:
                    fondo = FondoPronto;
                    borde = BordePronto;
                    etiqueta = "Empieza pronto";
                    break;
                case Estado.Cancelada:
                    fondo = FondoCancelada;
                    borde = BordeCancelada;
                    etiqueta = "Cancelada";
                    break;
                default:
                    fondo = FondoPorVenir;
                    borde = BordePorVenir;
                    etiqueta = null;
                    break;
            }

            Color colorPastilla = s.ConFallas > 0 && estado == Estado.ConBitacoras ? Naranja : borde;
            string laboratorio = EstiloHorarios.NombreLaboratorio(panorama.Laboratorio(s.LaboratorioId));
            // En el bloque, su nombre corto si lo tiene; en la ayuda, siempre con el oficial.
            string nombre = aspecto == null ? s.Materia : aspecto.Nombre;
            zonas[indice].Ayuda = Ayuda(s, aspecto == null ? s.Materia : aspecto.NombreConOficial, fecha, estado, laboratorio, hora, abre);

            // Ya anotada (zona y ayuda): si no toca lo que hay que repintar (el
            // temporizador solo repinta la que late), no se vuelve a pintar.
            if (!Rectangle.Inflate(r, Resplandor.Margen + 3, Resplandor.Margen + 3).IntersectsWith(recorte))
            {
                return;
            }

            if (estado == Estado.EnCurso)
            {
                Resplandor.Dibujar(g, r, 8);
            }

            // Sobre el circulo, el que se resalta es el (su anillo), no el bloque: el clic es suyo.
            bool sobreCirculo = raton.HasValue && cuerpo.Contains(raton.Value) && CirculoClase.Contiene(circulo, raton.Value);
            bool resaltada = abre && raton.HasValue && cuerpo.Contains(raton.Value) && r.Contains(raton.Value) && !sobreCirculo;
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(r, 8))
            {
                using (SolidBrush pincel = new SolidBrush(resaltada ? Mezclar(fondo, Color.White, 0.5f) : fondo))
                {
                    g.FillPath(pincel, forma);
                }

                // El lomo del color de la clase, entre el fondo de estado y su borde.
                if (aspecto != null)
                {
                    CirculoClase.PintarLomo(g, forma, r, aspecto.Color, 4);
                }

                using (Pen lapiz = new Pen(borde, estado == Estado.EnCurso || resaltada ? 3f : 1.5f))
                {
                    g.DrawPath(lapiz, forma);
                }
            }

            if (!circulo.IsEmpty)
            {
                CirculoClase.Pintar(g, circulo, aspecto);
                if (sobreCirculo)
                {
                    CirculoClase.PintarAnillo(g, circulo);
                }
            }

            // De arriba abajo, un dato por linea y dicho con palabras: la materia, el
            // grupo, el laboratorio, el horario y como va. Solo lineas enteras; lo
            // que no quepa sale al pasar el raton. Junto al circulo, lo que se cruza
            // con el va a su derecha (Junto).
            Color texto = estado == Estado.Cancelada ? TextoApagado : TextoPrincipal;
            Color secundario = estado == Estado.Cancelada ? TextoApagado : TextoSecundario;
            const TextFormatFlags UnaLinea = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int altoMateria = TextRenderer.MeasureText(g, "Ág", fuenteMateria).Height;
            int altoDetalle = TextRenderer.MeasureText(g, "Ág", fuenteDetalle).Height;
            int altoPastilla = TextRenderer.MeasureText(g, "Ág", fuenteEtiqueta, Size.Empty, TextFormatFlags.NoPadding).Height + 3;
            if (t.Height < altoMateria)
            {
                // Una sesion de unos minutos: solo la materia (la pastilla no cabe).
                TextRenderer.DrawText(g, nombre, fuenteMateria, new Rectangle(t.Left, r.Top, t.Width, r.Height), texto, UnaLinea | TextFormatFlags.VerticalCenter);
                return;
            }

            string grupo = "Grupo " + s.Grupo;
            string horario = "De " + HorariosDatos.Hora(s.Inicio) + " a " + HorariosDatos.Hora(s.Fin) + (s.EsExtra ? " (sesión extra)" : "");
            int caben = (t.Bottom + 2 - (t.Top + altoMateria + 1)) / altoDetalle;
            if (caben <= 1)
            {
                // Una clase de una hora: la pastilla arriba a la derecha (si deja a la
                // materia la mitad del ancho) y el grupo con el laboratorio en una linea.
                // Con circulo, las dos lineas a su derecha.
                Rectangle tb = Junto(t, circulo, t.Top);
                Rectangle arriba = Pastilla(g, etiqueta, etiquetaCorta, colorPastilla, tb.Right, tb.Top + 1, tb.Width / 2);
                int anchoMateria = arriba.IsEmpty ? tb.Width : arriba.Left - 6 - tb.Left;
                TextRenderer.DrawText(g, nombre, fuenteMateria, new Rectangle(tb.Left, tb.Top, anchoMateria, altoMateria), texto, UnaLinea | TextFormatFlags.Top);
                if (caben == 1)
                {
                    Linea(g, laboratorio.Length > 0 ? grupo + ", " + laboratorio : grupo, fuenteDetalle, secundario, tb,
                        tb.Top + Math.Max(altoMateria, arriba.IsEmpty ? 0 : arriba.Height) + 1);
                }

                return;
            }

            // Con sitio: la materia con todo el ancho (o a la derecha del circulo), en
            // dos lineas si no cabe en una y sobra alto para las demas (grupo,
            // laboratorio y horario con su pastilla).
            int altoFila = Math.Max(altoDetalle, altoPastilla);
            int detalles = (laboratorio.Length > 0 ? 2 : 1) * altoDetalle + altoFila;
            Rectangle tm = Junto(t, circulo, t.Top);
            bool dosLineas = TextRenderer.MeasureText(g, nombre, fuenteMateria, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width > tm.Width
                && t.Height + 2 >= 2 * altoMateria + 1 + detalles;
            int altoTitulo = dosLineas ? 2 * altoMateria : altoMateria;
            TextRenderer.DrawText(g, nombre, fuenteMateria, new Rectangle(tm.Left, t.Top, tm.Width, altoTitulo), texto, dosLineas
                ? TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                : UnaLinea | TextFormatFlags.Top);
            int yTexto = Linea(g, grupo, fuenteDetalle, secundario, Junto(t, circulo, t.Top + altoTitulo + 1), t.Top + altoTitulo + 1);
            if (laboratorio.Length > 0)
            {
                yTexto = Linea(g, laboratorio, fuenteDetalle, secundario, Junto(t, circulo, yTexto), yTexto);
            }

            // El horario con la pastilla a su derecha: la pastilla puede tomar hasta
            // la mitad del ancho (si no, el horario se acorta; ya lo dicen las filas).
            if (yTexto + altoFila > t.Bottom + 2)
            {
                return;
            }

            Rectangle th = Junto(t, circulo, yTexto);
            int anchoHorario = TextRenderer.MeasureText(g, horario, fuenteDetalle, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            Rectangle pastilla = Pastilla(g, etiqueta, etiquetaCorta, colorPastilla, th.Right, yTexto + (altoFila - altoPastilla) / 2,
                Math.Max(th.Width - anchoHorario - 8, th.Width / 2));
            TextRenderer.DrawText(g, horario, fuenteDetalle,
                new Rectangle(th.Left, yTexto + (altoFila - altoDetalle) / 2, Math.Max(10, (pastilla.IsEmpty ? th.Right : pastilla.Left - 6) - th.Left), altoDetalle),
                secundario, UnaLinea | TextFormatFlags.Top);
            yTexto += altoFila;

            Color colorComoVa;
            string comoVa = ComoVa(s, estado, fecha, hora, out colorComoVa);
            if (comoVa != null)
            {
                Linea(g, comoVa, fuenteDetalle, colorComoVa, Junto(t, circulo, yTexto), yTexto);
            }
        }

        /// <summary>
        /// El circulo de un bloque con lomo (t ya corrido), o vacio: de 28 px
        /// arriba a la izquierda en los de 2 horas o mas; de 26 px centrado en el
        /// alto en los de una hora; ninguno en los muy cortos (sin sitio para la
        /// materia o para el circulo) ni en carriles estrechos (menos de 120 px).
        /// Las cuentas de las lineas son las de DibujarBloque.
        /// </summary>
        private Rectangle CirculoDelBloque(Graphics g, Rectangle t)
        {
            if (altoMateriaBloque == 0)
            {
                altoMateriaBloque = TextRenderer.MeasureText(g, "Ág", fuenteMateria).Height;
                altoDetalleBloque = Math.Max(1, TextRenderer.MeasureText(g, "Ág", fuenteDetalle).Height);
            }

            if (t.Width < 120 || t.Height < altoMateriaBloque)
            {
                return Rectangle.Empty;
            }

            int caben = (t.Bottom + 2 - (t.Top + altoMateriaBloque + 1)) / altoDetalleBloque;
            if (caben > 1)
            {
                return CirculoClase.Rect(t.Left, t.Top + 1, 28);
            }

            return t.Height >= 26 ? CirculoClase.Rect(t.Left, t.Top + (t.Height - 26) / 2, 26) : Rectangle.Empty;
        }

        /// <summary>Donde va una linea que empieza en y: a la derecha del circulo si se cruza con el; si no (o sin circulo), en todo t.</summary>
        private static Rectangle Junto(Rectangle t, Rectangle circulo, int y)
        {
            return circulo.IsEmpty || y >= circulo.Bottom ? t : Rectangle.FromLTRB(circulo.Right + 6, t.Top, t.Right, t.Bottom);
        }

        /// <summary>
        /// La pastilla de como fue o como va, del color del borde (naranja si
        /// alguien reporto fallas), alineada a la derecha en x = derecha. Si el
        /// texto no cabe en maximo px, el corto; si ninguno cabe, no se pinta
        /// (Rectangle.Empty): el color del bloque y la ayuda ya lo dicen.
        /// </summary>
        private Rectangle Pastilla(Graphics g, string texto, string corto, Color color, int derecha, int y, int maximo)
        {
            foreach (string intento in new[] { texto, corto })
            {
                if (intento == null)
                {
                    continue;
                }

                Size medida = TextRenderer.MeasureText(g, intento, fuenteEtiqueta, Size.Empty, TextFormatFlags.NoPadding);
                if (medida.Width + 10 > maximo)
                {
                    continue;
                }

                Rectangle pastilla = new Rectangle(derecha - medida.Width - 10, y, medida.Width + 10, medida.Height + 3);
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(pastilla, pastilla.Height / 2))
                using (SolidBrush pincel = new SolidBrush(color))
                {
                    g.FillPath(pincel, forma);
                }

                TextRenderer.DrawText(g, intento, fuenteEtiqueta, pastilla, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                return pastilla;
            }

            return Rectangle.Empty;
        }

        /// <summary>La ultima linea de un bloque: como va o como fue, dicho con palabras. Null si no hay mas que decir que la pastilla.</summary>
        private static string ComoVa(SesionAgenda s, Estado estado, DateTime fecha, TimeSpan hora, out Color color)
        {
            color = TextoSecundario;
            switch (estado)
            {
                case Estado.EnCurso:
                    // Cuantas van ya lo dice su pastilla.
                    color = Verde;
                    return "Quedan " + PanelLaboratoriosAhora.Duracion(s.Fin - hora) + (s.ConFallas > 0 ? "; " + s.ConFallas + " con fallas" : "");
                case Estado.Pronto:
                    color = Verde;
                    return "Empieza en " + PanelLaboratoriosAhora.Duracion(s.Inicio - hora) + "; ya pueden registrar";
                case Estado.ConBitacoras:
                    if (s.Alumnos == 0 && s.EnOtra != null)
                    {
                        // Sus bitacoras quedaron en otra sesion: se dice cual (al pulsarla se abre esa).
                        return s.EsResto ? "Registraron antes del recorte" : "Llegaron antes: quedaron en la de las " + HorariosDatos.Hora(s.EnOtra.Inicio);
                    }

                    color = Naranja;
                    return s.ConFallas == 0 ? null : s.ConFallas == 1 ? "1 estudiante reportó fallas" : s.ConFallas + " estudiantes reportaron fallas";
                case Estado.SinBitacoras:
                    color = BordeSinBitacoras;
                    return "Nadie registró su bitácora";
                case Estado.NoCuenta:
                    color = TextoApagado;
                    return s.EsExtra && s.CreadaEn.HasValue && s.CreadaEn.Value >= fecha + s.Fin
                        ? "Sesión extra anotada después" : "Ese laboratorio aún no usaba DigBit";
                case Estado.Cancelada:
                    color = TextoApagado;
                    return s.Alumnos == 1 ? "1 bitácora antes de cancelarla"
                        : s.Alumnos > 1 ? s.Alumnos + " bitácoras antes de cancelarla"
                        : string.IsNullOrEmpty(s.Motivo) ? null : "Motivo: " + s.Motivo;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Al pasar el raton por una clase: la materia con su grupo, el dia y la
        /// hora, el laboratorio y como fue (o va), y si se puede abrir. materia:
        /// la oficial, o "POO (Programacion Orientada a Objetos)" con nombre corto.
        /// </summary>
        private static string Ayuda(SesionAgenda s, string materia, DateTime fecha, Estado estado, string laboratorio, TimeSpan hora, bool abre)
        {
            string dia = Dias[(int)fecha.DayOfWeek];
            int cuantas = s.BitacorasDeLaClase;
            string bitacoras = cuantas == 1 ? "1 bitácora" : cuantas + " bitácoras";
            string fallas = s.ConFallas > 0 ? ", " + s.ConFallas + " con fallas" : "";
            string donde = s.Alumnos == 0 && s.EnOtra != null
                ? (s.EsResto ? " (registraron antes del recorte)" : " (llegaron antes y quedaron en tu clase de las " + HorariosDatos.Hora(s.EnOtra.Inicio) + ")")
                : "";
            string como;
            switch (estado)
            {
                case Estado.ConBitacoras:
                    como = bitacoras + fallas + donde;
                    break;
                case Estado.SinBitacoras:
                    como = "Nadie registró su bitácora";
                    break;
                case Estado.NoCuenta:
                    como = s.EsExtra && s.CreadaEn.HasValue && s.CreadaEn.Value >= fecha + s.Fin
                        ? "Sin bitácoras; la sesión extra se anotó después de su hora y no cuenta como falta"
                        : "Sin bitácoras; ese laboratorio aún no registraba con DigBit y no cuenta como falta";
                    break;
                case Estado.EnCurso:
                    como = "En curso: quedan " + PanelLaboratoriosAhora.Duracion(s.Fin - hora)
                        + (cuantas == 0 ? " y aún nadie ha registrado" : "; " + bitacoras + " hasta ahora" + fallas + donde);
                    break;
                case Estado.Pronto:
                    como = "Empieza pronto: tus estudiantes ya pueden registrar" + (cuantas > 0 ? " (" + bitacoras + ")" : "");
                    break;
                case Estado.Cancelada:
                    como = "Cancelada" + (string.IsNullOrEmpty(s.Motivo) ? "" : ": " + s.Motivo)
                        + (s.Alumnos > 0 ? (s.Alumnos == 1 ? "; 1 estudiante registró antes" : "; " + s.Alumnos + " estudiantes registraron antes") : "");
                    break;
                default:
                    como = "Aún no empieza";
                    break;
            }

            return materia
                + "\nGrupo " + s.Grupo
                + (laboratorio.Length > 0 ? "\n" + laboratorio : "")
                + "\n" + char.ToUpper(dia[0]) + dia.Substring(1) + " " + fecha.ToString("dd/MM") + ", de " + HorariosDatos.Hora(s.Inicio)
                + " a " + HorariosDatos.Hora(s.Fin) + (s.EsExtra ? " (sesión extra)" : "")
                + "\n" + como
                + (abre ? "\nPulsa para ver quién registró y qué reportaron." : "");
        }

        /// <summary>Una linea que cabe entera en el bloque (si no, no se pinta); devuelve donde sigue.</summary>
        private static int Linea(Graphics g, string texto, DrawingFont fuente, Color color, Rectangle t, int y)
        {
            int alto = TextRenderer.MeasureText(g, "Ág", fuente).Height;
            if (y + alto > t.Bottom + 2)
            {
                return y;
            }

            TextRenderer.DrawText(g, texto, fuente, new Rectangle(t.Left, y, t.Width, alto), color,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return y + alto;
        }

        private static Color Mezclar(Color color, Color con, float proporcion)
        {
            return Color.FromArgb(
                (int)(color.R + (con.R - color.R) * proporcion),
                (int)(color.G + (con.G - color.G) * proporcion),
                (int)(color.B + (con.B - color.B) * proporcion));
        }

        private int ZonaEn(Point punto)
        {
            // Lo que se desplazo bajo la cabecera o el pie no se ve: no se pulsa.
            if (!cuerpoPintado.Contains(punto))
            {
                return -1;
            }

            for (int i = 0; i < zonas.Count; i++)
            {
                if (zonas[i].Rect.Contains(punto))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>La zona bajo el punto (o -1) y si cae en su circulo.</summary>
        private int ZonaEn(Point punto, out bool enCirculo)
        {
            int indice = ZonaEn(punto);
            enCirculo = indice >= 0 && CirculoClase.Contiene(zonas[indice].Circulo, punto);
            return indice;
        }

        /// <summary>La ayuda de lo que hay bajo el raton: la del circulo (la misma en todas partes) o la del bloque; null fuera.</summary>
        private string AyudaDe(int indice, bool enCirculo)
        {
            return indice < 0 ? null : enCirculo ? CirculoClase.Ayuda(zonas[indice].Aspecto) : zonas[indice].Ayuda;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ratonDentro = true;
            bool enCirculo;
            int nueva = ZonaEn(e.Location, out enCirculo);
            if (nueva != encima || enCirculo != encimaCirculo)
            {
                // El circulo se pulsa aunque el bloque no abra (los de manana tambien se personalizan).
                encima = nueva;
                encimaCirculo = enCirculo;
                Cursor = enCirculo || (nueva >= 0 && zonas[nueva].Abre) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            // Fuera de las clases, sin ayuda (la de la ultima seguiria saliendo en los huecos).
            string texto = AyudaDe(nueva, enCirculo);
            if (texto != textoAyuda)
            {
                textoAyuda = texto;
                ayuda.SetToolTip(this, texto);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            ratonDentro = false;
            if (encima != -1 || encimaCirculo)
            {
                encima = -1;
                encimaCirculo = false;
                Cursor = Cursors.Default;
                Invalidate();
            }

            textoAyuda = null;
            ayuda.SetToolTip(this, null);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            bool enCirculo;
            int indice = ZonaEn(e.Location, out enCirculo);
            // El circulo, solo con el boton izquierdo: "Personalizar tu clase" de la
            // clase del bloque (nunca la de EnOtra). Fuera de el, el bloque como siempre.
            if (indice >= 0 && enCirculo && e.Button == MouseButtons.Left)
            {
                if (Personalizar != null)
                {
                    Personalizar(zonas[indice].Clase.ClaseId);
                }

                return;
            }

            if (indice >= 0 && zonas[indice].Abre && VerRegistro != null)
            {
                // Si su sesion salio vacia y sus alumnos quedaron en otra (un recorte,
                // los que llegaron antes), se abre esa: ahi estan sus bitacoras.
                SesionAgenda clase = zonas[indice].Clase;
                VerRegistro(clase.Alumnos == 0 && clase.EnOtra != null ? clase.EnOtra : clase, zonas[indice].Fecha);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animacion.Dispose();
                ayuda.Dispose();
                fuenteDia.Dispose();
                fuenteFecha.Dispose();
                fuenteHoras.Dispose();
                fuenteMateria.Dispose();
                fuenteDetalle.Dispose();
                fuenteEtiqueta.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
