using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// El codigo de clase dicho en voz alta (RF63, RNF14), para el boton
    /// "Escuchar el codigo" (BotonEscuchar) del panel del profesor, de Mi horario
    /// y del codigo de una clase que abre el administrador. Lo lee una voz de
    /// Windows en espanol (System.Speech: de Mexico si la hay; si no, de otro
    /// pais), mas despacio que lo normal, caracter por caracter como lo deletrea
    /// TarjetaCodigo.Deletrear ("O (letra), cero, 6"), con una pausa entre
    /// caracteres; tras otra pausa lo repite una vez ("Repito: ...").
    /// SpeakAsync: la ventana no se congela. Uno a la vez en todo DigBit: dictar
    /// otro, pulsar otra vez o cerrar la ventana lo detiene, y el sintetizador se
    /// libera en cuanto termina.
    ///
    /// Nunca en el kiosco ni para el alumno (Permitido): quien dicta es el
    /// profesor o el administrador. Sin voz en espanol, sin salida de audio o si
    /// falla, el boton se apaga con una ayuda que lo explica (NoDisponible) y
    /// queda en el log. Todo en el hilo de la ventana.
    /// </summary>
    internal static class DictadoCodigo
    {
        /// <summary>Mas despacio que lo normal (de -10 a 10): se dicta a un salon que lo va escribiendo.</summary>
        public const int Velocidad = -2;

        /// <summary>Lo que dice la voz antes de repetirlo.</summary>
        public const string Repito = "Repito";

        // Un silencio al empezar (la bocina ya esta despierta en el primer
        // caracter), la pausa entre caracteres y la de antes de repetirlo.
        private static readonly TimeSpan AlEmpezar = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan EntreCaracteres = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan AntesDeRepetir = TimeSpan.FromMilliseconds(1500);

        private const string SinVozEnEspanol = "Este equipo no tiene una voz en español para leerlo. Se agrega en la configuración de Windows: "
            + "Hora e idioma, Voz, Español (México); luego vuelve a abrir DigBit.";
        private const string SinSalidaDeAudio = "Este equipo no tiene bocinas ni audífonos conectados: conéctalos para escucharlo.";
        private const string NoSePudo = "No se pudo leer en voz alta en este equipo. Revisa las bocinas o los audífonos y vuelve a abrir DigBit.";

        private static bool comprobado;
        // La voz elegida (su nombre y su idioma), o null sin ninguna en espanol.
        // Se mira una vez por proceso: System.Speech guarda las voces del equipo
        // al crear su primer sintetizador y no ve las que se agregan despues (por
        // eso, sin voz, la ayuda pide volver a abrir DigBit).
        private static string voz;
        private static CultureInfo cultura;
        private static string sinVoz;
        // Sin salida de audio la ultima vez que se miro (se vuelve a mirar: RevisarAudio).
        private static bool sinAudio;
        // Fallo al dictar. Cuando un boton ya enseno por que (AyudaVista), la
        // siguiente vez que se mira (RevisarAudio: el raton vuelve al boton) tiene
        // otra oportunidad, por si fue pasajero; si no, al volver a abrir DigBit.
        private static string fallo;
        private static bool falloVisto;
        private static SpeechSynthesizer sintetizador;
        private static BotonEscuchar quien;

        /// <summary>
        /// Solo para las pruebas: se llama con cada sintetizador antes de hablar,
        /// para mandarlo a un .wav (SetOutputToWaveFile) o bajarle el volumen y no
        /// sonar en las bocinas. En DigBit, siempre null.
        /// </summary>
        internal static Action<SpeechSynthesizer> PrepararParaPruebas { get; set; }

        /// <summary>Cambio lo que se puede en este equipo (fallo, o aparecio o se fue la salida de audio): los botones se ponen al dia.</summary>
        public static event Action Cambio;

        [DllImport("winmm.dll")]
        private static extern uint waveOutGetNumDevs();

        /// <summary>
        /// Si se ofrece a quien tiene la sesion en este equipo: nunca en el kiosco,
        /// al alumno ni al encargado de laboratorio (no ve los codigos).
        /// </summary>
        public static bool Permitido
        {
            get { return !Kiosco.Activo && Datos_User.TipoUsuario != TiposUsuario.Alumno && Permisos.VeCodigosDeProfesor; }
        }

        /// <summary>Por que no se puede dictar en este equipo (la ayuda del boton apagado), o null si se puede.</summary>
        public static string NoDisponible
        {
            get
            {
                Comprobar();
                return fallo ?? sinVoz ?? (sinAudio ? SinSalidaDeAudio : null);
            }
        }

        /// <summary>La voz que dicta ("Microsoft Sabina Desktop"), o null sin ninguna en espanol.</summary>
        public static string Voz
        {
            get
            {
                Comprobar();
                return voz;
            }
        }

        /// <summary>Si ese boton es el que esta dictando.</summary>
        public static bool Dicta(BotonEscuchar boton)
        {
            return sintetizador != null && boton != null && ReferenceEquals(boton, quien);
        }

        /// <summary>
        /// La voz en espanol: la de Mexico si la hay; si no, la primera de otro
        /// pais, en el orden de Windows. Null sin ninguna.
        /// </summary>
        internal static VoiceInfo ElegirVoz(IEnumerable<VoiceInfo> voces)
        {
            List<VoiceInfo> enEspanol = voces.Where(v => v != null && v.Culture != null && v.Culture.TwoLetterISOLanguageName == "es").ToList();
            return enEspanol.FirstOrDefault(v => string.Equals(v.Culture.Name, "es-MX", StringComparison.OrdinalIgnoreCase)) ?? enEspanol.FirstOrDefault();
        }

        /// <summary>
        /// Una vez por proceso, la primera vez que se pregunta: si hay salida de
        /// audio y que voz en espanol tiene el equipo (unos 60 ms). Lo que falte
        /// queda en el log.
        /// </summary>
        private static void Comprobar()
        {
            if (comprobado)
            {
                return;
            }

            comprobado = true;
            sinAudio = !HaySalidaDeAudio();
            List<VoiceInfo> voces = new List<VoiceInfo>();
            try
            {
                using (SpeechSynthesizer prueba = new SpeechSynthesizer())
                {
                    voces = prueba.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToList();
                }
            }
            catch (Exception ex)
            {
                // Sin el servicio de voz de Windows (SAPI) no hay ninguna.
                Log.Error("Dictado del codigo: no se pudieron leer las voces del equipo", ex);
            }

            VoiceInfo elegida = ElegirVoz(voces);
            if (elegida == null)
            {
                sinVoz = SinVozEnEspanol;
                Log.Aviso("Dictado del codigo: el equipo no tiene voz en espanol (" + (voces.Count == 0 ? "ninguna voz instalada" : "voces: " + Describir(voces))
                    + "); Escuchar el codigo queda apagado.");
                return;
            }

            voz = elegida.Name;
            cultura = elegida.Culture;
            Log.Info("Dictado del codigo: voz " + Describir(new[] { elegida })
                + (sinAudio ? "; sin salida de audio por ahora, Escuchar el codigo apagado hasta que la haya." : "."));
        }

        /// <summary>Si Windows tiene alguna salida de audio. Si no se puede saber, si: lo dira el intento.</summary>
        private static bool HaySalidaDeAudio()
        {
            try
            {
                return waveOutGetNumDevs() > 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                return true;
            }
        }

        /// <summary>
        /// Vuelve a mirar lo que pudo cambiar con DigBit abierto: si hay salida de
        /// audio (pudieron conectar o quitar las bocinas) y, tras un fallo cuyo
        /// porque ya se vio (AyudaVista), le da otra oportunidad. Lo piden el boton
        /// apagado al pasarle el raton y Dictar. Si cambio algo, los botones se
        /// ponen al dia. La voz no se vuelve a mirar (ver voz).
        /// </summary>
        public static void RevisarAudio()
        {
            if (!comprobado)
            {
                Comprobar();
                return;
            }

            bool cambio = false;
            if (fallo != null && falloVisto)
            {
                fallo = null;
                falloVisto = false;
                Log.Info("Dictado del codigo: se vuelve a ofrecer tras el fallo.");
                cambio = true;
            }

            bool sin = !HaySalidaDeAudio();
            if (sin != sinAudio)
            {
                sinAudio = sin;
                Log.Info(sin ? "Dictado del codigo: el equipo se quedo sin salida de audio." : "Dictado del codigo: ya hay salida de audio.");
                cambio = true;
            }

            if (cambio)
            {
                AvisarCambio();
            }
        }

        /// <summary>
        /// Un boton apagado acaba de ensenar su ayuda: si era la de un fallo, ya se
        /// vio por que y la siguiente vez que se mire (RevisarAudio) se vuelve a
        /// ofrecer. Antes no: el porque de un fallo a media lectura (SpeakCompleted,
        /// sin clic que lo ensene) no se veria nunca.
        /// </summary>
        public static void AyudaVista()
        {
            if (fallo != null)
            {
                falloVisto = true;
            }
        }

        /// <summary>
        /// Dicta el codigo desde ese boton (antes detiene lo que se estuviera
        /// dictando). False si no se pudo: NoDisponible dice por que.
        /// </summary>
        public static bool Dictar(BotonEscuchar boton, string codigo)
        {
            if (sintetizador != null)
            {
                Parar();
            }

            if (!Permitido || string.IsNullOrEmpty(codigo))
            {
                return false;
            }

            RevisarAudio();
            if (NoDisponible != null)
            {
                return false;
            }

            SpeechSynthesizer nuevo = null;
            try
            {
                // Creado en el hilo de la ventana: SpeakCompleted llega por su contexto, a ese hilo.
                nuevo = new SpeechSynthesizer();
                nuevo.SelectVoice(voz);
                nuevo.Rate = Velocidad;
                nuevo.SetOutputToDefaultAudioDevice();
                if (PrepararParaPruebas != null)
                {
                    PrepararParaPruebas(nuevo);
                }

                nuevo.SpeakCompleted += AlTerminar;
                sintetizador = nuevo;
                quien = boton;
                nuevo.SpeakAsync(QueDecir(codigo, cultura));
                return true;
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(sintetizador, nuevo))
                {
                    sintetizador = null;
                    quien = null;
                }

                if (nuevo != null)
                {
                    nuevo.SpeakCompleted -= AlTerminar;
                    nuevo.Dispose();
                }

                Fallo(ex);
                return false;
            }
        }

        /// <summary>Si ese boton esta dictando, lo detiene y libera el sintetizador.</summary>
        public static void Detener(BotonEscuchar boton)
        {
            if (Dicta(boton))
            {
                Parar();
            }
        }

        /// <summary>Corta lo que se este dictando y libera el sintetizador; el boton que dictaba se pone al dia.</summary>
        private static void Parar()
        {
            BotonEscuchar antes = quien;
            try
            {
                sintetizador.SpeakAsyncCancelAll();
            }
            catch (Exception ex)
            {
                Log.Error("Dictado del codigo: no se pudo detener", ex);
            }

            Liberar();
            if (antes != null && !antes.Disposing && !antes.IsDisposed)
            {
                antes.Refrescar();
            }
        }

        private static void Liberar()
        {
            SpeechSynthesizer viejo = sintetizador;
            sintetizador = null;
            quien = null;
            if (viejo == null)
            {
                return;
            }

            viejo.SpeakCompleted -= AlTerminar;
            try
            {
                viejo.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Dictado del codigo: no se pudo liberar el sintetizador", ex);
            }
        }

        private static void AlTerminar(object sender, SpeakCompletedEventArgs e)
        {
            BotonEscuchar boton = quien;
            // Llega al hilo de la ventana (por su contexto); si no, se pasa a el.
            if (boton != null && !boton.IsDisposed && boton.IsHandleCreated && boton.InvokeRequired)
            {
                boton.BeginInvoke(new Action(() => AlTerminar(sender, e)));
                return;
            }

            // De uno que ya se libero (lo detuvo otro dictado): nada.
            if (!ReferenceEquals(sender, sintetizador))
            {
                return;
            }

            Liberar();
            // System.Speech da Cancelled tambien cuando falla (con el error): una
            // cancelacion de verdad es la que trae OperationCanceledException.
            if (e.Error != null && !(e.Error is OperationCanceledException))
            {
                Fallo(e.Error);
            }

            if (boton != null && !boton.IsDisposed)
            {
                boton.Refrescar();
            }
        }

        /// <summary>
        /// No se pudo dictar: sin salida de audio se vuelve a intentar cuando la
        /// haya; si no, apagado con el porque hasta que se vea y el raton vuelva al
        /// boton (AyudaVista, RevisarAudio).
        /// </summary>
        private static void Fallo(Exception ex)
        {
            Log.Error("Dictado del codigo: no se pudo leer en voz alta con " + Ascii(voz ?? "-"), ex);
            if (HaySalidaDeAudio())
            {
                fallo = NoSePudo;
                falloVisto = false;
            }
            else
            {
                sinAudio = true;
            }

            AvisarCambio();
        }

        private static void AvisarCambio()
        {
            Action cambio = Cambio;
            if (cambio != null)
            {
                cambio();
            }
        }

        /// <summary>
        /// Lo que se le pide a la voz: un silencio, cada caracter como lo
        /// deletrea TarjetaCodigo (en mayusculas; la letra por su nombre, "ele" o
        /// "doble u", y la cifra sola, "seis", nunca como palabra o numero; "O
        /// letra", "cero" y "uno" donde se confunden) con una pausa entre
        /// caracteres; tras otra pausa, "Repito" y otra vez el codigo.
        /// </summary>
        internal static PromptBuilder QueDecir(string codigo, CultureInfo idioma)
        {
            PromptBuilder decir = new PromptBuilder(idioma ?? CultureInfo.GetCultureInfo("es-MX"));
            decir.AppendBreak(AlEmpezar);
            Deletrear(decir, codigo);
            decir.AppendBreak(AntesDeRepetir);
            decir.AppendText(Repito);
            decir.AppendBreak(EntreCaracteres);
            Deletrear(decir, codigo);
            return decir;
        }

        /// <summary>
        /// Cada caracter con TarjetaCodigo.ComoSeDice: uno solo ("W", "6") se
        /// deletrea; "O (letra)" es la letra deletreada y "letra"; una palabra
        /// ("cero", "uno"), tal cual.
        /// </summary>
        private static void Deletrear(PromptBuilder decir, string codigo)
        {
            string mayusculas = TarjetaCodigo.Mayusculas(codigo);
            bool conLetras = mayusculas.Any(char.IsLetter);
            for (int i = 0; i < mayusculas.Length; i++)
            {
                if (i > 0)
                {
                    decir.AppendBreak(EntreCaracteres);
                }

                string como = TarjetaCodigo.ComoSeDice(mayusculas[i], conLetras);
                if (como.Length == 1)
                {
                    decir.AppendTextWithHint(como, SayAs.SpellOut);
                }
                else if (como[0] == mayusculas[i] && como[1] == ' ')
                {
                    decir.AppendTextWithHint(como.Substring(0, 1), SayAs.SpellOut);
                    decir.AppendText(como.Substring(1).Replace("(", "").Replace(")", "").Trim());
                }
                else
                {
                    decir.AppendText(como);
                }
            }
        }

        /// <summary>
        /// Lo que se oye, en texto y con las mismas partes que QueDecir: "O
        /// (letra), cero, 6. Repito: O (letra), cero, 6." (las pruebas lo
        /// comparan con lo que dice la voz y con Deletrear).
        /// </summary>
        internal static string TextoDictado(string codigo)
        {
            string mayusculas = TarjetaCodigo.Mayusculas(codigo);
            bool conLetras = mayusculas.Any(char.IsLetter);
            string una = string.Join(", ", mayusculas.Select(c => TarjetaCodigo.ComoSeDice(c, conLetras)));
            return una + ". " + Repito + ": " + una + ".";
        }

        /// <summary>"Microsoft Sabina Desktop (es-MX), Microsoft Zira Desktop (en-US)", en ASCII para el log.</summary>
        private static string Describir(IEnumerable<VoiceInfo> voces)
        {
            return string.Join(", ", voces.Select(v => Ascii(v.Name) + " (" + (v.Culture == null ? "?" : v.Culture.Name) + ")"));
        }

        /// <summary>Sin acentos ni nada fuera de ASCII: el log va en ASCII.</summary>
        private static string Ascii(string texto)
        {
            StringBuilder limpio = new StringBuilder(texto.Length);
            foreach (char c in texto.Normalize(NormalizationForm.FormD))
            {
                if (c < 128)
                {
                    limpio.Append(c);
                }
                else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    limpio.Append('?');
                }
            }

            return limpio.ToString();
        }
    }

    /// <summary>
    /// "Escuchar el codigo" (DictadoCodigo): una pastilla blanca con borde verde
    /// (la forma de BotonCabecera) con una bocina y su texto; compacto, solo la
    /// bocina en un circulo (junto al codigo de Mi horario, o en una tarjeta
    /// angosta). Mientras dicta, verde lleno con el cuadro de detener y
    /// "Detener": pulsarlo otra vez lo detiene. Sin codigo que dictar (Codigo
    /// vacio; SinCodigo dice por que) o sin poder dictar en este equipo, apagado
    /// y gris; su ayuda la ensena el contenedor, porque Windows no le da el raton
    /// a un control apagado.
    ///
    /// Es un Button pintado a mano: Tab, Enter/Espacio y la accion por defecto de
    /// MSAA (la de Pulsar en las pruebas) son las de cualquier boton. Text no
    /// cambia nunca (es lo que buscan las pruebas); mientras dicta, AccessibleName
    /// dice "Detener". El control mide 3 px mas por lado que la pastilla, para el
    /// anillo del foco.
    /// </summary>
    internal class BotonEscuchar : Button
    {
        public const string Texto = "Escuchar el código";
        public const int Alto = 32;
        public const int MargenFoco = 3;
        /// <summary>Lo que mide de alto la pastilla que se ve (sin el margen del foco).</summary>
        public const int AltoVisible = Alto - 2 * MargenFoco;
        private const int Relleno = 12;
        private const int LadoIcono = 15;
        private const int TrasIcono = 6;
        private const string TextoDetener = "Detener";
        private const string AyudaLista = "Lee el código en voz alta, carácter por carácter, y lo repite una vez. Pulsa otra vez para detenerlo.";
        private const string AyudaDictando = "Leyendo el código en voz alta. Pulsa para detenerlo.";
        private const string NadaQueDictar = "No hay código que dictar.";

        private static readonly Color Verde = Color.DarkGreen;
        private static readonly Color Apagado = Color.FromArgb(150, 150, 150);
        private static readonly Color FondoEncima = Color.FromArgb(236, 246, 236);
        private static readonly Color FondoPulsado = Color.FromArgb(214, 232, 214);
        // Mientras dicta (relleno verde): encima, un poco mas claro; pulsado, mas oscuro.
        private static readonly Color VerdeEncima = Color.FromArgb(0, 122, 0);
        private static readonly Color VerdePulsado = Color.FromArgb(0, 78, 0);

        private const TextFormatFlags FormatoTexto = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        // La suya, para liberarla al final (Control.Dispose no libera su Font).
        private readonly DrawingFont fuentePropia = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private readonly ToolTip ayuda = new ToolTip { InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 15000 };
        // La pastilla con el mas largo de sus dos textos: al dictar no cambia de tamano.
        private readonly int anchoNormal;
        private string codigo = "";
        private string sinCodigo = "";
        private string ayudaActual;
        private bool compacto;
        private bool encima;
        private bool pulsado;
        // El contenedor, que ensena la ayuda mientras esta apagado.
        private Control padre;
        private bool ayudaEnPadre;

        public BotonEscuchar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.White;
            Font = fuentePropia;
            Cursor = Cursors.Hand;
            TabStop = true;
            Text = Texto;

            // Siempre con un Graphics: sin el, MeasureText no hace caso de NoPadding.
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                int etiqueta = Math.Max(TextRenderer.MeasureText(g, Texto, fuentePropia, Size.Empty, FormatoTexto).Width,
                    TextRenderer.MeasureText(g, TextoDetener, fuentePropia, Size.Empty, FormatoTexto).Width);
                anchoNormal = 2 * MargenFoco + 2 * Relleno + LadoIcono + TrasIcono + etiqueta;
            }

            Size = Medida(false);
            DictadoCodigo.Cambio += Refrescar;
            Refrescar();
        }

        /// <summary>El codigo que dicta. Vacio: nada que dictar, apagado (SinCodigo dice por que). Si cambia mientras dicta, se detiene.</summary>
        public string Codigo
        {
            get
            {
                return codigo;
            }

            set
            {
                string valor = value ?? "";
                if (valor == codigo)
                {
                    return;
                }

                codigo = valor;
                DictadoCodigo.Detener(this);
                Refrescar();
            }
        }

        /// <summary>Sin codigo, por que (la ayuda del boton apagado). Vacio: la de siempre, que no hay codigo que dictar.</summary>
        public string SinCodigo
        {
            get
            {
                return sinCodigo;
            }

            set
            {
                string valor = value ?? "";
                if (valor == sinCodigo)
                {
                    return;
                }

                sinCodigo = valor;
                Refrescar();
            }
        }

        /// <summary>Solo la bocina, en un circulo de Alto x Alto (con el margen del foco).</summary>
        public bool Compacto
        {
            get
            {
                return compacto;
            }

            set
            {
                if (value == compacto)
                {
                    return;
                }

                compacto = value;
                Size = Medida(value);
                Invalidate();
            }
        }

        /// <summary>
        /// El color de detras, para las esquinas fuera de la pastilla, cuando no es
        /// el del contenedor (dentro del recuadro del codigo de Mi horario, su
        /// verde claro). Vacio: el del contenedor.
        /// </summary>
        public Color FondoDetras { get; set; }

        /// <summary>Lo que mide el control (con el margen del foco): la pastilla con su texto o, compacta, el circulo.</summary>
        public Size Medida(bool compacta)
        {
            return compacta ? new Size(Alto, Alto) : new Size(anchoNormal, Alto);
        }

        /// <summary>
        /// Se pone al dia con el codigo y con lo que se puede en este equipo:
        /// apagado sin codigo o sin poder dictar (su ayuda dice por que); mientras
        /// dicta, "Detener".
        /// </summary>
        internal void Refrescar()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            string porQue = PorQueApagado();
            bool dicta = DictadoCodigo.Dicta(this);
            if (Enabled != (porQue == null))
            {
                Enabled = porQue == null;
            }

            string texto = porQue ?? (dicta ? AyudaDictando : AyudaLista);
            if (texto != ayudaActual)
            {
                ayudaActual = texto;
                ayuda.SetToolTip(this, texto);
                AccessibleDescription = texto;
            }

            string nombre = dicta ? TextoDetener : null;
            if (AccessibleName != nombre)
            {
                AccessibleName = nombre;
            }

            if (Enabled && ayudaEnPadre)
            {
                ayuda.Hide(padre);
                ayudaEnPadre = false;
            }

            Invalidate();
        }

        /// <summary>Lo del equipo antes que lo del codigo: sin voz, nunca se podra en este equipo.</summary>
        private string PorQueApagado()
        {
            if (!DictadoCodigo.Permitido)
            {
                // Nunca se ensena asi (el kiosco y el alumno no lo tienen).
                return NadaQueDictar;
            }

            return DictadoCodigo.NoDisponible ?? (codigo.Length > 0 ? null : sinCodigo.Length > 0 ? sinCodigo : NadaQueDictar);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (DictadoCodigo.Dicta(this))
            {
                DictadoCodigo.Detener(this);
                return;
            }

            if (DictadoCodigo.Dictar(this, codigo))
            {
                Refrescar();
                return;
            }

            // No se pudo: se apaga y dice por que ahi mismo, sin esperar al raton.
            Refrescar();
            if (!Enabled && padre != null && Visible)
            {
                ayuda.Show(ayudaActual, padre, Left + 4, Bottom + 2, 8000);
                ayudaEnPadre = true;
                DictadoCodigo.AyudaVista();
            }
        }

        protected override void OnParentChanged(EventArgs e)
        {
            if (padre != null)
            {
                padre.MouseMove -= PadreMouseMove;
                padre.MouseLeave -= PadreMouseLeave;
            }

            padre = Parent;
            if (padre != null)
            {
                padre.MouseMove += PadreMouseMove;
                padre.MouseLeave += PadreMouseLeave;
            }

            base.OnParentChanged(e);
        }

        /// <summary>
        /// Apagado, el raton se lo lleva el contenedor: ahi se ensena su ayuda
        /// (como Permisos.Desactivar). Antes se vuelve a mirar si ya hay bocinas
        /// y, tras un fallo cuyo porque ya se vio, se le da otra oportunidad
        /// (DictadoCodigo.RevisarAudio): si con eso se enciende, ninguna.
        /// </summary>
        private void PadreMouseMove(object sender, MouseEventArgs e)
        {
            bool sobre = !Enabled && Visible && Bounds.Contains(e.Location);
            if (sobre && !ayudaEnPadre)
            {
                DictadoCodigo.RevisarAudio();
                if (Enabled)
                {
                    return;
                }

                ayuda.Show(ayudaActual, padre, e.X + 12, e.Y + 20, 8000);
                ayudaEnPadre = true;
                DictadoCodigo.AyudaVista();
            }
            else if (!sobre && ayudaEnPadre)
            {
                ayuda.Hide(padre);
                ayudaEnPadre = false;
            }
        }

        private void PadreMouseLeave(object sender, EventArgs e)
        {
            if (ayudaEnPadre)
            {
                ayuda.Hide(padre);
                ayudaEnPadre = false;
            }
        }

        /// <summary>Oculto (sin sitio en la tarjeta) o con su ventana cerrada u oculta: deja de dictar.</summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
            {
                DictadoCodigo.Detener(this);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
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

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pulsado = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                pulsado = true;
                Invalidate();
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                pulsado = false;
                Invalidate();
            }

            base.OnKeyUp(e);
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

        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            Invalidate();
            base.OnChangeUICues(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DictadoCodigo.Cambio -= Refrescar;
                DictadoCodigo.Detener(this);
                if (padre != null)
                {
                    padre.MouseMove -= PadreMouseMove;
                    padre.MouseLeave -= PadreMouseLeave;
                    padre = null;
                }

                ayuda.Dispose();
                fuentePropia.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // Fuera de la pastilla, el fondo de donde esta (Button no lo pinta solo).
            g.Clear(!FondoDetras.IsEmpty ? FondoDetras : Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool dicta = DictadoCodigo.Dicta(this);
            Color borde = Enabled ? Verde : Apagado;
            Color tinta = !Enabled ? Apagado : dicta ? Color.White : Verde;
            Color fondo = !Enabled ? Color.White
                : dicta ? (pulsado ? VerdePulsado : encima ? VerdeEncima : Verde)
                : (pulsado ? FondoPulsado : encima ? FondoEncima : Color.White);

            // La pastilla (o el circulo), con el borde de 2 px por dentro, como BotonCabecera.
            Rectangle vis = Rectangle.Inflate(ClientRectangle, -MargenFoco, -MargenFoco);
            Rectangle trazo = new Rectangle(vis.X + 1, vis.Y + 1, vis.Width - 3, vis.Height - 3);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(trazo, trazo.Height))
            using (SolidBrush relleno = new SolidBrush(fondo))
            using (Pen lapiz = new Pen(borde, 2f))
            {
                g.FillPath(relleno, forma);
                g.DrawPath(lapiz, forma);
            }

            // La bocina (o, al dictar, el cuadro de detener) y su texto, centrados.
            string etiqueta = compacto ? null : dicta ? TextoDetener : Text;
            int anchoEtiqueta = etiqueta == null ? 0 : TextRenderer.MeasureText(g, etiqueta, Font, Size.Empty, FormatoTexto).Width;
            int anchoContenido = LadoIcono + (etiqueta == null ? 0 : TrasIcono + anchoEtiqueta);
            int x = vis.X + (vis.Width - anchoContenido) / 2;
            RectangleF icono = new RectangleF(x, vis.Y + (vis.Height - LadoIcono) / 2f, LadoIcono, LadoIcono);
            if (dicta)
            {
                DibujarDetener(g, icono, tinta);
            }
            else
            {
                DibujarBocina(g, icono, tinta);
            }

            if (etiqueta != null)
            {
                TextRenderer.DrawText(g, etiqueta, Font, new Rectangle(x + LadoIcono + TrasIcono, vis.Y, anchoEtiqueta + 1, vis.Height), tinta, FormatoTexto);
            }

            // El foco del teclado (Tab), no despues de un clic: un anillo 1 px por fuera.
            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath anillo = PanelLaboratoriosAhora.Redondeado(new Rectangle(1, 1, Width - 3, Height - 3), Height))
                using (Pen lapiz = new Pen(borde, 2f))
                {
                    g.DrawPath(lapiz, anillo);
                }
            }
        }

        /// <summary>
        /// Una bocina rellena (el cuerpo y el cono) con dos ondas, pintada con
        /// GDI+: no depende de ninguna fuente de iconos ni de emojis.
        /// </summary>
        private static void DibujarBocina(Graphics g, RectangleF r, Color color)
        {
            Func<float, float, PointF> en = (u, v) => new PointF(r.Left + u * r.Width, r.Top + v * r.Height);
            using (GraphicsPath cuerpo = new GraphicsPath())
            using (SolidBrush brocha = new SolidBrush(color))
            using (Pen onda = new Pen(color, Math.Max(1.4f, r.Width / 10f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                cuerpo.AddPolygon(new[] { en(0.02f, 0.35f), en(0.24f, 0.35f), en(0.50f, 0.10f), en(0.50f, 0.90f), en(0.24f, 0.65f), en(0.02f, 0.65f) });
                g.FillPath(brocha, cuerpo);

                // Las ondas: arcos con centro en la boca del cono.
                PointF boca = en(0.50f, 0.5f);
                foreach (float radio in new[] { 0.22f, 0.44f })
                {
                    float rx = radio * r.Width;
                    g.DrawArc(onda, boca.X - rx, boca.Y - rx, 2 * rx, 2 * rx, -48, 96);
                }
            }
        }

        /// <summary>El cuadro de detener, con las esquinas redondeadas.</summary>
        private static void DibujarDetener(Graphics g, RectangleF r, Color color)
        {
            int lado = (int)Math.Round(r.Width * 0.64f);
            Rectangle cuadro = new Rectangle((int)Math.Round(r.Left + (r.Width - lado) / 2f), (int)Math.Round(r.Top + (r.Height - lado) / 2f), lado, lado);
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(cuadro, 2))
            using (SolidBrush brocha = new SolidBrush(color))
            {
                g.FillPath(brocha, forma);
            }
        }
    }
}
