using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DigBit.Infraestructura;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigBit
{
    /// <summary>
    /// Las hojas de un PDF en memoria, una debajo de otra sobre fondo gris, con
    /// barras, rueda del raton y zoom (Ctrl + rueda, Acercar, Alejar y
    /// AjustarAlAncho). Es la vista de VisorPdf (la ventana con "Guardar PDF...")
    /// y la del panel de la izquierda de Bitacoras, que cambia de PDF cada vez
    /// que se elige otra sesion (Mostrar). Los botones y los textos de la pagina
    /// y del zoom los pone quien la usa (EstadoCambiado, TextoPagina, TextoZoom).
    ///
    /// Dibuja con Windows.Data.Pdf (WinRT de Windows 10/11, sin librerias): sus
    /// operaciones se esperan con Completed y un evento, sin
    /// System.Runtime.WindowsRuntime (pide el Windows.winmd del SDK). Todo lo de
    /// WinRT corre en un hilo aparte, uno por PDF (Trabajo): al cambiar de PDF el
    /// anterior se suelta y lo que entregue tarde ya no se pinta. La pantalla solo
    /// convierte el PNG en Bitmap. Sin WinRT (Windows viejo), o si el PDF no se
    /// puede abrir, dice por que en medio, avisa con Fallo y no truena.
    /// </summary>
    internal sealed class VistaPreviaPdf : UserControl
    {
        public static readonly Color FondoGris = Color.FromArgb(222, 225, 222);
        private static readonly Color BordeHoja = Color.FromArgb(178, 184, 178);
        private static readonly Color TextoGris = Color.FromArgb(100, 106, 100);

        // Pasos del zoom (1 = tamano real a 96 ppp): +, - y Ctrl + rueda saltan de uno en uno.
        private static readonly float[] Pasos = { 0.25f, 0.33f, 0.5f, 0.67f, 0.75f, 0.9f, 1f, 1.1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f };

        // El proceso es de 32 bits: una hoja no se dibuja a mas de este ancho (con
        // mas zoom se estira) y las imagenes juntas no pasan del presupuesto; si lo
        // pasan, se sueltan las de las hojas mas lejos de la vista.
        private const int AnchoMaximoDibujo = 2400;
        private const long PixelesMaximosHoja = 10000000;
        private const long PresupuestoImagenes = 160L * 1024 * 1024;

        // Mientras no se lee el tamano de una pagina, A4 (en DIP, 96 ppp).
        private const double AnchoA4 = 793.7;
        private const double AltoA4 = 1122.5;

        /// <summary>Para las pruebas (por reflexion): como si este Windows no tuviera Windows.Data.Pdf.</summary>
        internal static bool ForzarSinWinRT { get; set; }

        private static bool avisadoSinWinRT;

        /// <summary>Cambio la pagina que se ve, el zoom o el PDF (TextoPagina, TextoZoom, HayHojas).</summary>
        public event EventHandler EstadoCambiado;

        /// <summary>
        /// El PDF no se pudo ensenar: que paso (para el log, en ASCII) y la excepcion,
        /// si la hubo. Ya se dijo en medio (TextoSiFalla) y ya va en el log.
        /// </summary>
        public event Action<string, Exception> Fallo;

        /// <summary>Se leyeron las paginas del PDF de Mostrar (ya se ven, en blanco, hasta que se dibujan).</summary>
        public event EventHandler Abierto;

        private readonly Lienzo lienzo;
        private readonly Timer esperaDibujo = new Timer { Interval = 150 };
        private readonly Timer esperaCarga = new Timer();
        private readonly DrawingFont fuenteNegrita = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold);
        private readonly DrawingFont fuenteHoja = new DrawingFont("Century Gothic", 11F);
        private readonly DrawingFont fuenteMensaje = new DrawingFont("Century Gothic", 10F);

        private int margen = 24;
        private int separacion = 18;
        private int segundosParaPreparar = 20;
        private List<Hoja> hojas;
        private bool ajustarAlAncho = true;
        private float escala = 1f;
        private bool cambiandoZoom;
        private int ruedaZoom;
        private bool algunaMostrada;
        private bool fallida;
        private bool marcarFoco;
        // Sin PDF: el texto gris en medio; null, "Preparando la vista previa...".
        private string mensaje;
        private string textoPagina = "";
        private string textoZoom = "";

        // El hilo de WinRT del PDF que se ve, y el PDF que espera a que haya ventana.
        private Trabajo actual;
        private byte[] pendiente;

        private sealed class Hoja
        {
            // Tamano de la pagina en DIP (96 ppp).
            public double Ancho = AnchoA4;
            public double Alto = AltoA4;
            public bool TamanoLeido;
            // Donde va en el documento, a la escala actual.
            public Rectangle Lugar;
            public Bitmap Imagen;
            public bool Fallida;
        }

        private sealed class Pedido
        {
            public int Indice;
            public int Ancho;
        }

        /// <summary>
        /// Un PDF y su hilo de WinRT: la pantalla le deja la lista de lo que falta
        /// dibujar (lo visible primero) y la cambia entera cuando se mueve o cambia
        /// el zoom. Terminar lo suelta; el evento lo libera el propio hilo al salir.
        /// </summary>
        private sealed class Trabajo
        {
            public readonly byte[] Pdf;
            public readonly object Candado = new object();
            public readonly AutoResetEvent HayPedidos = new AutoResetEvent(false);
            public List<Pedido> Pedidos = new List<Pedido>();
            public Pedido EnCurso;
            public volatile bool Terminar;
            private bool eventoLiberado;

            public Trabajo(byte[] pdf)
            {
                Pdf = pdf;
            }

            public void Despertar()
            {
                lock (Candado)
                {
                    if (!eventoLiberado)
                    {
                        HayPedidos.Set();
                    }
                }
            }

            public void Liberar()
            {
                lock (Candado)
                {
                    EnCurso = null;
                    if (!eventoLiberado)
                    {
                        HayPedidos.Dispose();
                        eventoLiberado = true;
                    }
                }
            }
        }

        public VistaPreviaPdf()
        {
            BackColor = FondoGris;
            lienzo = new Lienzo { Dock = DockStyle.Fill, BackColor = FondoGris, AccessibleName = "Hojas del PDF" };
            lienzo.Paint += PintarLienzo;
            lienzo.Resize += (s, e) => AlCambiarTamano();
            lienzo.Movido += (s, e) => AlMoverse();
            lienzo.ZoomConRueda += ZoomConRueda;
            lienzo.KeyDown += TeclaEnLasHojas;
            Controls.Add(lienzo);

            esperaDibujo.Tick += (s, e) =>
            {
                esperaDibujo.Stop();
                PedirDibujos();
            };
            esperaCarga.Tick += (s, e) =>
            {
                esperaCarga.Stop();
                if (!algunaMostrada && actual != null)
                {
                    Fallar("la vista previa no estuvo lista en " + segundosParaPreparar + " s", null);
                }
            };
        }

        // =====================================================================
        // Lo que usa quien la pone
        // =====================================================================

        /// <summary>Margen alrededor de las hojas (24 en la ventana; menos en un panel angosto).</summary>
        public int MargenHojas
        {
            get { return margen; }
            set { margen = Math.Max(0, value); Redistribuir(); }
        }

        /// <summary>Espacio entre una hoja y la siguiente.</summary>
        public int SeparacionHojas
        {
            get { return separacion; }
            set { separacion = Math.Max(0, value); Redistribuir(); }
        }

        /// <summary>Si a los tantos segundos no se ve ninguna hoja, se da por fallida (0: sin limite).</summary>
        public int SegundosParaPreparar
        {
            get { return segundosParaPreparar; }
            set { segundosParaPreparar = Math.Max(0, value); }
        }

        /// <summary>Lo que se anade al texto de cuando no se pudo ensenar (por ejemplo, otra forma de verlo).</summary>
        public string AyudaSiFalla { get; set; }

        /// <summary>
        /// Con el foco en las hojas, un marco verde por dentro del borde: en Bitacoras
        /// las flechas mueven entonces la hoja y no la lista. VisorPdf no lo pide (sus
        /// teclas las atiende la ventana, tenga el foco quien lo tenga).
        /// </summary>
        public bool MarcarFoco
        {
            get { return marcarFoco; }
            set { marcarFoco = value; lienzo.Invalidate(); }
        }

        /// <summary>"Pagina 2 de 3", o vacio sin PDF.</summary>
        public string TextoPagina
        {
            get { return textoPagina; }
        }

        /// <summary>"75 %", o vacio sin PDF.</summary>
        public string TextoZoom
        {
            get { return textoZoom; }
        }

        /// <summary>Ya se leyeron las paginas del PDF (se puede acercar, alejar y moverse).</summary>
        public bool HayHojas
        {
            get { return hojas != null; }
        }

        /// <summary>Ya se ve dibujada alguna hoja del PDF de ahora.</summary>
        public bool AlgunaMostrada
        {
            get { return algunaMostrada; }
        }

        /// <summary>El PDF que se ve o se esta preparando (null: ninguno).</summary>
        public byte[] PdfActual
        {
            get { return actual != null ? actual.Pdf : pendiente; }
        }

        /// <summary>El texto gris que se ve sin PDF (null: "Preparando..." o hay PDF).</summary>
        public string Mensaje
        {
            get { return hojas == null ? mensaje : null; }
        }

        /// <summary>Por que este equipo no puede dibujar PDF (null: si puede).</summary>
        public static string MotivoSinWinRT()
        {
            if (ForzarSinWinRT)
            {
                return "Windows.Data.Pdf no disponible (forzado en una prueba)";
            }

            try
            {
                DocumentoWinRT.Comprobar();
                return null;
            }
            catch (Exception ex)
            {
                return "Windows.Data.Pdf no disponible (" + Describir(ex) + ")";
            }
        }

        /// <summary>
        /// Ensena ese PDF desde arriba, con el zoom que ya tenia (al principio, a lo
        /// ancho). El anterior se suelta: lo que su hilo entregue tarde ya no se pinta.
        /// </summary>
        public void Mostrar(byte[] pdf)
        {
            if (pdf == null || pdf.Length == 0)
            {
                throw new ArgumentException("El PDF esta vacio.", "pdf");
            }

            Soltar();
            mensaje = null;
            fallida = false;
            algunaMostrada = false;
            ruedaZoom = 0;
            string motivo = MotivoSinWinRT();
            if (motivo != null)
            {
                Fallar(motivo, null, true);
                return;
            }

            if (IsHandleCreated)
            {
                Empezar(pdf);
            }
            else
            {
                // BeginInvoke necesita la ventana: empieza al crearse (OnHandleCreated).
                pendiente = pdf;
            }

            lienzo.Invalidate();
            AvisarEstado();
        }

        /// <summary>
        /// Sin PDF, con el motivo de que este equipo no pueda dibujarlos en medio
        /// (MotivoSinWinRT, no null): quien genera el PDF solo para verlo aqui se
        /// lo ahorra.
        /// </summary>
        public void MostrarSinWinRT(string motivo)
        {
            Soltar();
            mensaje = null;
            fallida = false;
            algunaMostrada = false;
            Fallar(string.IsNullOrEmpty(motivo) ? "Windows.Data.Pdf no disponible" : motivo, null, true);
        }

        /// <summary>Sin PDF, con "Preparando la vista previa..." en medio (mientras quien la usa lo genera).</summary>
        public void MostrarPreparando()
        {
            Soltar();
            mensaje = null;
            fallida = false;
            lienzo.Invalidate();
            AvisarEstado();
        }

        /// <summary>Sin PDF, con ese texto en gris en medio (vacio: solo el fondo).</summary>
        public void MostrarTexto(string texto)
        {
            Soltar();
            mensaje = texto ?? "";
            fallida = false;
            lienzo.Invalidate();
            AvisarEstado();
        }

        /// <summary>Deja de dibujar y suelta el PDF y sus imagenes (al cerrar la ventana).</summary>
        public void Soltar()
        {
            if (actual != null)
            {
                actual.Terminar = true;
                actual.Despertar();
                actual = null;
            }

            pendiente = null;
            algunaMostrada = false;
            esperaDibujo.Stop();
            esperaCarga.Stop();
            LiberarImagenes();
            hojas = null;
            if (!lienzo.IsDisposed)
            {
                lienzo.AutoScrollMinSize = Size.Empty;
            }
        }

        public void EnfocarHojas()
        {
            lienzo.Focus();
        }

        public void Acercar()
        {
            Acercar(null);
        }

        public void Alejar()
        {
            Alejar(null);
        }

        /// <summary>La hoja a todo el ancho (como al abrir).</summary>
        public void AjustarAlAncho()
        {
            CambiarZoom(true, escala, null);
        }

        public void Desplazar(int dx, int dy)
        {
            lienzo.Desplazar(dx, dy);
        }

        /// <summary>Lo que se ve, menos un renglon (Av Pag).</summary>
        public void PaginaAbajo()
        {
            lienzo.Desplazar(0, Math.Max(40, lienzo.ClientSize.Height - 40));
        }

        public void PaginaArriba()
        {
            lienzo.Desplazar(0, -Math.Max(40, lienzo.ClientSize.Height - 40));
        }

        public void AlPrincipio()
        {
            lienzo.IrA(new Point(-lienzo.AutoScrollPosition.X, 0));
        }

        public void AlFinal()
        {
            lienzo.IrA(new Point(-lienzo.AutoScrollPosition.X, lienzo.AutoScrollMinSize.Height));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (pendiente != null)
            {
                byte[] pdf = pendiente;
                pendiente = null;
                Empezar(pdf);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Soltar();
                esperaDibujo.Dispose();
                esperaCarga.Dispose();
                fuenteNegrita.Dispose();
                fuenteHoja.Dispose();
                fuenteMensaje.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Con el foco en las hojas (en Bitacoras, tras un clic en ellas): flechas,
        /// Av Pag, Re Pag, Inicio, Fin y Ctrl y +, - o 0. En VisorPdf no llegan
        /// aqui: la ventana las atiende antes, tenga el foco quien lo tenga.
        /// </summary>
        private void TeclaEnLasHojas(object sender, KeyEventArgs e)
        {
            bool atendida = true;
            switch (e.KeyData)
            {
                case Keys.Control | Keys.Add:
                case Keys.Control | Keys.Oemplus:
                    Acercar(null);
                    break;
                case Keys.Control | Keys.Subtract:
                case Keys.Control | Keys.OemMinus:
                    Alejar(null);
                    break;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0:
                    AjustarAlAncho();
                    break;
                case Keys.Down:
                    lienzo.Desplazar(0, 40);
                    break;
                case Keys.Up:
                    lienzo.Desplazar(0, -40);
                    break;
                case Keys.Right:
                    lienzo.Desplazar(40, 0);
                    break;
                case Keys.Left:
                    lienzo.Desplazar(-40, 0);
                    break;
                case Keys.PageDown:
                    PaginaAbajo();
                    break;
                case Keys.PageUp:
                    PaginaArriba();
                    break;
                case Keys.Home:
                case Keys.Control | Keys.Home:
                    AlPrincipio();
                    break;
                case Keys.End:
                case Keys.Control | Keys.End:
                    AlFinal();
                    break;
                default:
                    atendida = false;
                    break;
            }

            if (atendida)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void AvisarEstado()
        {
            if (hojas == null)
            {
                textoPagina = "";
                textoZoom = "";
            }

            EventHandler aviso = EstadoCambiado;
            if (aviso != null)
            {
                aviso(this, EventArgs.Empty);
            }
        }

        private void LiberarImagenes()
        {
            if (hojas == null)
            {
                return;
            }

            foreach (Hoja hoja in hojas.Where(h => h.Imagen != null))
            {
                hoja.Imagen.Dispose();
                hoja.Imagen = null;
            }
        }

        // =====================================================================
        // Hojas y zoom
        // =====================================================================

        /// <summary>Coloca las hojas a la escala actual (o la que llena el ancho) y fija el tamano del documento.</summary>
        private void Distribuir()
        {
            if (ajustarAlAncho)
            {
                escala = EscalaParaElAncho();
            }

            int anchoMayor = hojas.Max(h => (int)(h.Ancho * escala));
            int y = margen;
            foreach (Hoja hoja in hojas)
            {
                int ancho = Math.Max(1, (int)(hoja.Ancho * escala));
                int alto = Math.Max(1, (int)(hoja.Alto * escala));
                hoja.Lugar = new Rectangle(margen + (anchoMayor - ancho) / 2, y, ancho, alto);
                y += alto + separacion;
            }

            lienzo.AutoScrollMinSize = new Size(anchoMayor + 2 * margen, y - separacion + margen);
            textoZoom = Math.Round(escala * 100).ToString(CultureInfo.InvariantCulture) + " %";
        }

        private void Redistribuir()
        {
            if (hojas != null)
            {
                CambiarZoom(ajustarAlAncho, escala, new Point(lienzo.ClientSize.Width / 2, 0));
            }
        }

        private float EscalaParaElAncho()
        {
            double anchoMayor = hojas.Max(h => h.Ancho);
            double altoTotal = hojas.Sum(h => h.Alto);
            int disponible = lienzo.Width - 2 * margen;

            // Si a ese ancho el documento no cabe a lo alto, la barra vertical le quita su parte.
            double alto = altoTotal * disponible / anchoMayor + (hojas.Count - 1) * separacion + 2 * margen;
            if (alto > lienzo.Height)
            {
                disponible -= SystemInformation.VerticalScrollBarWidth;
            }

            return Limitar((float)(Math.Max(1, disponible) / anchoMayor));
        }

        private static float Limitar(float valor)
        {
            return Math.Max(Pasos[0], Math.Min(Pasos[Pasos.Length - 1], valor));
        }

        private void Acercar(Point? ancla)
        {
            float siguiente = Pasos.Where(p => p > escala + 0.005f).DefaultIfEmpty(Pasos[Pasos.Length - 1]).First();
            CambiarZoom(false, siguiente, ancla);
        }

        private void Alejar(Point? ancla)
        {
            float anterior = Pasos.Where(p => p < escala - 0.005f).DefaultIfEmpty(Pasos[0]).Last();
            CambiarZoom(false, anterior, ancla);
        }

        private void ZoomConRueda(int delta, Point punto)
        {
            // Las ruedas finas (y los paneles tactiles) mandan trozos de muesca: se suman.
            ruedaZoom += delta;
            while (Math.Abs(ruedaZoom) >= 120)
            {
                if (ruedaZoom > 0)
                {
                    Acercar(punto);
                    ruedaZoom -= 120;
                }
                else
                {
                    Alejar(punto);
                    ruedaZoom += 120;
                }
            }
        }

        /// <summary>
        /// Cambia el zoom dejando quieto el punto del documento bajo el ancla (el
        /// raton con Ctrl + rueda; si no, el centro de la vista). Las imagenes
        /// viejas se estiran hasta que llegan las nuevas, que se piden al parar.
        /// </summary>
        private void CambiarZoom(bool ajustar, float nueva, Point? ancla)
        {
            if (hojas == null)
            {
                return;
            }

            Point punto = ancla ?? new Point(lienzo.ClientSize.Width / 2, lienzo.ClientSize.Height / 2);
            Point origen = Origen();
            Point enDocumento = new Point(punto.X - origen.X, punto.Y - origen.Y);
            int indice = HojaEn(enDocumento.Y);
            Rectangle antes = hojas[indice].Lugar;
            double fx = (enDocumento.X - antes.X) / (double)Math.Max(1, antes.Width);
            double fy = (enDocumento.Y - antes.Y) / (double)Math.Max(1, antes.Height);

            ajustarAlAncho = ajustar;
            if (!ajustar)
            {
                escala = Limitar(nueva);
            }

            Distribuir();
            Rectangle despues = hojas[indice].Lugar;
            cambiandoZoom = true;
            try
            {
                lienzo.IrA(new Point((int)Math.Round(despues.X + fx * despues.Width) - punto.X,
                    (int)Math.Round(despues.Y + fy * despues.Height) - punto.Y));
            }
            finally
            {
                cambiandoZoom = false;
            }

            lienzo.Invalidate();
            ActualizarPagina();
            esperaDibujo.Stop();
            esperaDibujo.Start();
        }

        private void AlCambiarTamano()
        {
            if (hojas == null)
            {
                lienzo.Invalidate();
                return;
            }

            if (ajustarAlAncho)
            {
                CambiarZoom(true, escala, new Point(lienzo.ClientSize.Width / 2, 0));
                return;
            }

            lienzo.Invalidate();
            ActualizarPagina();
            esperaDibujo.Stop();
            esperaDibujo.Start();
        }

        private void AlMoverse()
        {
            ActualizarPagina();
            if (!cambiandoZoom)
            {
                PedirDibujos();
            }
        }

        /// <summary>Donde cae el (0, 0) del documento en el lienzo: el desplazamiento y, si sobra ancho, centrado.</summary>
        private Point Origen()
        {
            int sobra = Math.Max(0, (lienzo.ClientSize.Width - lienzo.AutoScrollMinSize.Width) / 2);
            return new Point(lienzo.AutoScrollPosition.X + sobra, lienzo.AutoScrollPosition.Y);
        }

        /// <summary>Lo que se ve, en coordenadas del documento.</summary>
        private Rectangle Vista()
        {
            Point origen = Origen();
            return new Rectangle(-origen.X, -origen.Y, lienzo.ClientSize.Width, lienzo.ClientSize.Height);
        }

        /// <summary>La hoja a esa altura del documento (o la mas cercana).</summary>
        private int HojaEn(int y)
        {
            for (int i = 0; i < hojas.Count; i++)
            {
                if (y < hojas[i].Lugar.Bottom + separacion / 2)
                {
                    return i;
                }
            }

            return hojas.Count - 1;
        }

        /// <summary>"Pagina X de N": la del centro de la vista; arriba del todo la primera, abajo del todo la ultima.</summary>
        private void ActualizarPagina()
        {
            if (hojas == null)
            {
                AvisarEstado();
                return;
            }

            int arriba = -lienzo.AutoScrollPosition.Y;
            int maximo = lienzo.AutoScrollMinSize.Height - lienzo.ClientSize.Height;
            int pagina = arriba <= 0 ? 0
                : arriba >= maximo ? hojas.Count - 1
                : HojaEn(arriba + lienzo.ClientSize.Height / 2);
            textoPagina = "Página " + (pagina + 1) + " de " + hojas.Count;
            AvisarEstado();
        }

        /// <summary>A cuantos pixeles se dibuja la hoja: los que ocupa, con los topes de memoria.</summary>
        private static int AnchoDeDibujo(Hoja hoja)
        {
            int ancho = Math.Min(hoja.Lugar.Width, AnchoMaximoDibujo);
            double pixeles = (double)ancho * ancho * hoja.Alto / hoja.Ancho;
            if (pixeles > PixelesMaximosHoja)
            {
                ancho = (int)(ancho * Math.Sqrt(PixelesMaximosHoja / pixeles));
            }

            return Math.Max(1, ancho);
        }

        // =====================================================================
        // Pintar
        // =====================================================================

        private void PintarLienzo(object sender, PaintEventArgs e)
        {
            PintarHojas(e);
            if (marcarFoco && lienzo.Focused)
            {
                // En coordenadas de la ventana, no del documento: no se mueve con las hojas.
                Rectangle marco = lienzo.ClientRectangle;
                using (Pen verde = new Pen(Color.DarkGreen, 2f))
                {
                    e.Graphics.DrawRectangle(verde, marco.X + 1, marco.Y + 1, marco.Width - 2, marco.Height - 2);
                }
            }
        }

        private void PintarHojas(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(FondoGris);

            if (hojas == null)
            {
                if (mensaje == null)
                {
                    TextRenderer.DrawText(g, "Preparando la vista previa...", fuenteNegrita, lienzo.ClientRectangle, Color.FromArgb(60, 80, 60),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                else if (mensaje.Length > 0)
                {
                    Rectangle area = Rectangle.Inflate(lienzo.ClientRectangle, -Math.Max(16, margen), -Math.Max(16, margen));
                    TextRenderer.DrawText(g, mensaje, fuenteMensaje, area, TextoGris,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }

                return;
            }

            Point origen = Origen();
            for (int i = 0; i < hojas.Count; i++)
            {
                Hoja hoja = hojas[i];
                Rectangle r = hoja.Lugar;
                r.Offset(origen);
                if (!Rectangle.Inflate(r, 6, 6).IntersectsWith(e.ClipRectangle))
                {
                    continue;
                }

                PintarSombra(g, r);
                g.FillRectangle(Brushes.White, r);

                Bitmap imagen = hoja.Imagen;
                if (imagen != null)
                {
                    if (imagen.Width == r.Width && Math.Abs(imagen.Height - r.Height) <= 2)
                    {
                        // A su tamano: pixel a pixel, nitida (el alto puede bailar 1 px por redondeo).
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.SetClip(r, CombineMode.Intersect);
                        g.DrawImage(imagen, new Rectangle(r.X, r.Y, imagen.Width, imagen.Height));
                        g.ResetClip();
                        g.SetClip(e.ClipRectangle);
                    }
                    else
                    {
                        // La de otro zoom, estirada mientras llega la nueva.
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(imagen, r);
                    }
                }
                else
                {
                    TextRenderer.DrawText(g, hoja.Fallida ? "No se pudo mostrar esta página." : "Página " + (i + 1), fuenteHoja, r,
                        Color.FromArgb(150, 150, 150), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }

                using (Pen borde = new Pen(BordeHoja))
                {
                    g.DrawRectangle(borde, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
                }
            }
        }

        /// <summary>Sombra suave bajo la hoja: tres capas que se suman cerca del borde.</summary>
        private static void PintarSombra(Graphics g, Rectangle r)
        {
            using (SolidBrush sombra = new SolidBrush(Color.FromArgb(20, 0, 0, 0)))
            {
                for (int i = 4; i >= 2; i--)
                {
                    g.FillRectangle(sombra, r.X - i + 1, r.Y - i + 3, r.Width + 2 * i - 2, r.Height + 2 * i - 2);
                }
            }
        }

        // =====================================================================
        // Lo que llega del hilo de WinRT (solo si es del PDF de ahora)
        // =====================================================================

        private void AlAbrir(Trabajo trabajo, List<Hoja> leidas)
        {
            if (trabajo != actual || IsDisposed)
            {
                return;
            }

            hojas = leidas;
            Distribuir();
            lienzo.IrA(Point.Empty);
            lienzo.Invalidate();
            ActualizarPagina();
            PedirDibujos();
            EventHandler abierto = Abierto;
            if (abierto != null)
            {
                abierto(this, EventArgs.Empty);
            }
        }

        private void AlDibujar(Trabajo trabajo, Pedido pedido, byte[] png, Exception error)
        {
            if (trabajo != actual || IsDisposed || hojas == null || pedido.Indice >= hojas.Count)
            {
                return;
            }

            Hoja hoja = hojas[pedido.Indice];
            Bitmap imagen = null;
            if (error == null)
            {
                // Si el zoom cambio mientras se dibujaba, sirve si se acerca mas a lo que hace falta que la que hay.
                int objetivo = AnchoDeDibujo(hoja);
                if (hoja.Imagen != null && Math.Abs(hoja.Imagen.Width - objetivo) <= Math.Abs(pedido.Ancho - objetivo))
                {
                    return;
                }

                try
                {
                    imagen = Decodificar(png);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }

            if (error != null)
            {
                if (!algunaMostrada)
                {
                    Fallar("no se pudo dibujar la pagina " + (pedido.Indice + 1), error);
                    return;
                }

                Log.Aviso("Visor de PDF: no se pudo dibujar la pagina " + (pedido.Indice + 1) + " a " + pedido.Ancho + " px: " + Describir(error));
                hoja.Fallida = true;
                lienzo.Invalidate();
                return;
            }

            if (hoja.Imagen != null)
            {
                hoja.Imagen.Dispose();
            }

            hoja.Imagen = imagen;
            algunaMostrada = true;
            if (!hoja.TamanoLeido)
            {
                // Sin su tamano (no se pudo leer), la proporcion sale de la imagen.
                hoja.Alto = hoja.Ancho * imagen.Height / imagen.Width;
                hoja.TamanoLeido = true;
                CambiarZoom(ajustarAlAncho, escala, new Point(lienzo.ClientSize.Width / 2, 0));
            }

            Recortar();
            lienzo.Invalidate();
        }

        /// <summary>El PNG, en un Bitmap propio de 24 bits (el de FromStream necesita su stream vivo).</summary>
        private static Bitmap Decodificar(byte[] png)
        {
            using (MemoryStream memoria = new MemoryStream(png))
            using (Image leida = Image.FromStream(memoria))
            {
                Bitmap copia = new Bitmap(leida.Width, leida.Height, PixelFormat.Format24bppRgb);
                try
                {
                    using (Graphics g = Graphics.FromImage(copia))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(leida, new Rectangle(0, 0, leida.Width, leida.Height));
                    }

                    return copia;
                }
                catch
                {
                    copia.Dispose();
                    throw;
                }
            }
        }

        /// <summary>Si las imagenes pasan del presupuesto, fuera las de las hojas que no se ven, de la mas lejana a la mas cercana.</summary>
        private void Recortar()
        {
            long total = hojas.Where(h => h.Imagen != null).Sum(h => (long)h.Imagen.Width * h.Imagen.Height * 3);
            if (total <= PresupuestoImagenes)
            {
                return;
            }

            Rectangle vista = Vista();
            int centro = vista.Y + vista.Height / 2;
            foreach (Hoja hoja in hojas.Where(h => h.Imagen != null && !h.Lugar.IntersectsWith(vista))
                .OrderByDescending(h => Math.Abs(h.Lugar.Y + h.Lugar.Height / 2 - centro)).ToList())
            {
                total -= (long)hoja.Imagen.Width * hoja.Imagen.Height * 3;
                hoja.Imagen.Dispose();
                hoja.Imagen = null;
                if (total <= PresupuestoImagenes)
                {
                    break;
                }
            }
        }

        private void Fallar(string que, Exception ex)
        {
            Fallar(que, ex, false);
        }

        /// <summary>
        /// El PDF de ahora no se puede ensenar: al log, se suelta, se dice en medio
        /// por que (sinWinRT: le falta Windows.Data.Pdf a este Windows) y se avisa.
        /// </summary>
        private void Fallar(string que, Exception ex, bool sinWinRT)
        {
            if (fallida || IsDisposed)
            {
                return;
            }

            fallida = true;
            // Que a este Windows le falta WinRT se dice una vez: en Bitacoras pasaria en cada sesion elegida.
            if (!sinWinRT || !avisadoSinWinRT)
            {
                avisadoSinWinRT = avisadoSinWinRT || sinWinRT;
                Log.Aviso("Visor de PDF: " + que + (ex == null ? "" : ": " + Ascii(ex.ToString())));
            }

            Soltar();
            mensaje = (sinWinRT
                    ? "Este equipo no puede mostrar la vista previa: le falta Windows.Data.Pdf, que viene con Windows 10 y 11."
                    : "No se pudo mostrar la vista previa de este PDF.")
                + (string.IsNullOrEmpty(AyudaSiFalla) ? "" : " " + AyudaSiFalla);
            lienzo.Invalidate();
            AvisarEstado();
            Action<string, Exception> fallo = Fallo;
            if (fallo != null)
            {
                fallo(que, ex);
            }
        }

        // =====================================================================
        // Hilo de WinRT
        // =====================================================================

        private void Empezar(byte[] pdf)
        {
            Trabajo trabajo = new Trabajo(pdf);
            actual = trabajo;
            esperaCarga.Stop();
            if (segundosParaPreparar > 0)
            {
                esperaCarga.Interval = segundosParaPreparar * 1000;
                esperaCarga.Start();
            }

            Thread hilo = new Thread(() => Trabajar(trabajo)) { IsBackground = true, Name = "VisorPdf" };
            hilo.SetApartmentState(ApartmentState.MTA);
            try
            {
                hilo.Start();
            }
            catch (Exception ex)
            {
                trabajo.Liberar();
                Fallar("no se pudo empezar el hilo de dibujo", ex);
            }
        }

        /// <summary>
        /// Lo que falta dibujar, lo visible primero y luego lo de una vista arriba
        /// y abajo. Lo que ya esta a su ancho, o se esta dibujando, no se pide.
        /// </summary>
        private void PedirDibujos()
        {
            Trabajo trabajo = actual;
            if (hojas == null || trabajo == null)
            {
                return;
            }

            Rectangle vista = Vista();
            Rectangle cerca = Rectangle.Inflate(vista, 0, vista.Height);
            List<Pedido> nuevos = new List<Pedido>();
            lock (trabajo.Candado)
            {
                foreach (bool visibles in new[] { true, false })
                {
                    for (int i = 0; i < hojas.Count; i++)
                    {
                        Hoja hoja = hojas[i];
                        bool seVe = hoja.Lugar.IntersectsWith(vista);
                        if (hoja.Fallida || seVe != visibles || !hoja.Lugar.IntersectsWith(cerca))
                        {
                            continue;
                        }

                        int ancho = AnchoDeDibujo(hoja);
                        bool yaEsta = hoja.Imagen != null && hoja.Imagen.Width == ancho;
                        bool enCamino = trabajo.EnCurso != null && trabajo.EnCurso.Indice == i && trabajo.EnCurso.Ancho == ancho;
                        if (!yaEsta && !enCamino)
                        {
                            nuevos.Add(new Pedido { Indice = i, Ancho = ancho });
                        }
                    }
                }

                trabajo.Pedidos = nuevos;
            }

            trabajo.Despertar();
        }

        /// <summary>El hilo de WinRT de un PDF: lo abre, lee sus paginas y dibuja lo que se le pide hasta que se suelta.</summary>
        private void Trabajar(Trabajo trabajo)
        {
            DocumentoWinRT documento = null;
            try
            {
                List<Hoja> leidas = new List<Hoja>();
                try
                {
                    documento = DocumentoWinRT.Abrir(trabajo.Pdf);
                    int paginas = documento.Paginas;
                    if (paginas == 0)
                    {
                        throw new InvalidDataException("El PDF no tiene paginas.");
                    }

                    for (int i = 0; i < paginas && !trabajo.Terminar; i++)
                    {
                        Hoja hoja = new Hoja();
                        try
                        {
                            double[] tamano = documento.Tamano(i);
                            if (tamano != null)
                            {
                                hoja.Ancho = tamano[0];
                                hoja.Alto = tamano[1];
                                hoja.TamanoLeido = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (i == 0)
                            {
                                Log.Aviso("Visor de PDF: no se pudo leer el tamano de las paginas (" + Describir(ex) + "); sale de la imagen.");
                            }
                        }

                        leidas.Add(hoja);
                    }
                }
                catch (Exception ex)
                {
                    Publicar(trabajo, () => { if (trabajo == actual) Fallar("no se pudo abrir el PDF con Windows.Data.Pdf", ex); });
                    return;
                }

                Publicar(trabajo, () => AlAbrir(trabajo, leidas));

                while (!trabajo.Terminar)
                {
                    Pedido pedido;
                    lock (trabajo.Candado)
                    {
                        trabajo.EnCurso = trabajo.Pedidos.Count > 0 ? trabajo.Pedidos[0] : null;
                        if (trabajo.EnCurso != null)
                        {
                            trabajo.Pedidos.RemoveAt(0);
                        }

                        pedido = trabajo.EnCurso;
                    }

                    if (pedido == null)
                    {
                        trabajo.HayPedidos.WaitOne();
                        continue;
                    }

                    byte[] png = null;
                    Exception error = null;
                    try
                    {
                        png = documento.Dibujar(pedido.Indice, pedido.Ancho);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }

                    Publicar(trabajo, () => AlDibujar(trabajo, pedido, png, error));
                }
            }
            catch (Exception ex)
            {
                // Un fallo sin atrapar en este hilo cerraria DigBit entero.
                Log.Aviso("Visor de PDF: el hilo de dibujo termino por un fallo: " + Ascii(ex.ToString()));
            }
            finally
            {
                if (documento != null)
                {
                    try
                    {
                        documento.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }

                trabajo.Liberar();
            }
        }

        /// <summary>Al hilo de la pantalla; si ya se solto ese PDF o ya no hay ventana, se pierde.</summary>
        private void Publicar(Trabajo trabajo, Action accion)
        {
            if (trabajo.Terminar)
            {
                return;
            }

            try
            {
                BeginInvoke(accion);
            }
            catch (InvalidOperationException)
            {
                // Sin ventana (se cerro mientras tanto): ya no hay a quien entregarlo.
            }
        }

        // =====================================================================
        // Utilidades
        // =====================================================================

        private static string Describir(Exception ex)
        {
            return ex == null ? "" : ex.GetType().Name + ": " + Ascii(ex.Message);
        }

        /// <summary>Para el log: sin acentos y solo ASCII (los saltos de linea se quedan).</summary>
        internal static string Ascii(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return texto ?? "";
            }

            StringBuilder limpio = new StringBuilder(texto.Length);
            foreach (char c in texto.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                limpio.Append(c == '\r' || c == '\n' || (c < 128 && !char.IsControl(c)) ? c : '?');
            }

            return limpio.ToString();
        }

        /// <summary>
        /// La superficie gris con sus barras: AutoScrollMinSize es el documento
        /// entero y quien la usa pinta en Paint con AutoScrollPosition. La rueda
        /// la lleva ella (con Ctrl avisa para el zoom) y todo desplazamiento avisa con Movido.
        /// </summary>
        private sealed class Lienzo : Panel
        {
            public event EventHandler Movido;
            public event Action<int, Point> ZoomConRueda;

            public Lienzo()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                AutoScroll = true;
                TabStop = true;
            }

            public void Desplazar(int dx, int dy)
            {
                IrA(new Point(-AutoScrollPosition.X + dx, -AutoScrollPosition.Y + dy));
            }

            /// <summary>A esa posicion del documento (positiva); fuera de rango se queda en el borde.</summary>
            public void IrA(Point posicion)
            {
                AutoScrollPosition = new Point(Math.Max(0, posicion.X), Math.Max(0, posicion.Y));
                Invalidate();
                Movido?.Invoke(this, EventArgs.Empty);
            }

            /// <summary>Las flechas, Av Pag, Re Pag, Inicio y Fin llegan a KeyDown (y no mueven el foco).</summary>
            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Left:
                    case Keys.Right:
                    case Keys.PageUp:
                    case Keys.PageDown:
                    case Keys.Home:
                    case Keys.End:
                        return true;
                }

                return base.IsInputKey(keyData);
            }

            protected override void OnScroll(ScrollEventArgs se)
            {
                base.OnScroll(se);
                Invalidate();
                Movido?.Invoke(this, EventArgs.Empty);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                Focus();
                base.OnMouseDown(e);
            }

            // El marco del foco (MarcarFoco) aparece y se va.
            protected override void OnGotFocus(EventArgs e)
            {
                Invalidate();
                base.OnGotFocus(e);
            }

            protected override void OnLostFocus(EventArgs e)
            {
                Invalidate();
                base.OnLostFocus(e);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                // Sin base: el desplazamiento de ScrollableControl es el que se cambia aqui.
                HandledMouseEventArgs manejado = e as HandledMouseEventArgs;
                if (manejado != null)
                {
                    manejado.Handled = true;
                }

                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    ZoomConRueda?.Invoke(e.Delta, e.Location);
                    return;
                }

                // Las lineas por muesca de Windows (-1: una pantalla por muesca), a 40 px la linea.
                int lineas = SystemInformation.MouseWheelScrollLines;
                int porMuesca = lineas < 0 ? Math.Max(40, ClientSize.Height - 40) : Math.Max(1, lineas) * 40;
                int paso = -(int)Math.Round(porMuesca * e.Delta / 120.0);
                if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                {
                    Desplazar(paso, 0);
                }
                else
                {
                    Desplazar(0, paso);
                }
            }
        }

        /// <summary>
        /// Lo unico que toca WinRT, para que su falta (Windows viejo) salte al
        /// llamarlo y no al cargar la vista. Cada uno se usa siempre desde el mismo hilo (MTA).
        /// </summary>
        private sealed class DocumentoWinRT : IDisposable
        {
            private const int SegundosPorOperacion = 30;

            private readonly InMemoryRandomAccessStream entrada;
            private readonly PdfDocument documento;

            private DocumentoWinRT(InMemoryRandomAccessStream entrada, PdfDocument documento)
            {
                this.entrada = entrada;
                this.documento = documento;
            }

            /// <summary>Falla si este Windows no tiene Windows.Data.Pdf (crea una clase suya).</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Comprobar()
            {
                GC.KeepAlive(new PdfPageRenderOptions());
            }

            public static DocumentoWinRT Abrir(byte[] pdf)
            {
                InMemoryRandomAccessStream entrada = new InMemoryRandomAccessStream();
                try
                {
                    // Los bytes pasan con DataWriter: sin System.Runtime.WindowsRuntime
                    // no hay AsStream ni AsBuffer.
                    using (DataWriter escritor = new DataWriter(entrada.GetOutputStreamAt(0)))
                    {
                        escritor.WriteBytes(pdf);
                        Esperar(escritor.StoreAsync());
                        Esperar(escritor.FlushAsync());
                        escritor.DetachStream();
                    }

                    entrada.Seek(0);
                    PdfDocument documento = Esperar(PdfDocument.LoadFromStreamAsync(entrada));
                    return new DocumentoWinRT(entrada, documento);
                }
                catch
                {
                    entrada.Dispose();
                    throw;
                }
            }

            public int Paginas
            {
                get { return (int)documento.PageCount; }
            }

            /// <summary>Ancho y alto de la pagina en DIP (96 ppp), o null.</summary>
            public double[] Tamano(int indice)
            {
                // PdfPage.Size es un Windows.Foundation.Size, que .NET proyecta en
                // System.Runtime.WindowsRuntime: se lee por reflexion para no compilar contra el.
                using (PdfPage pagina = documento.GetPage((uint)indice))
                {
                    object tamano = typeof(PdfPage).GetProperty("Size").GetValue(pagina, null);
                    Type tipo = tamano.GetType();
                    double ancho = Convert.ToDouble(tipo.GetProperty("Width").GetValue(tamano, null), CultureInfo.InvariantCulture);
                    double alto = Convert.ToDouble(tipo.GetProperty("Height").GetValue(tamano, null), CultureInfo.InvariantCulture);
                    return ancho > 1 && alto > 1 ? new[] { ancho, alto } : null;
                }
            }

            /// <summary>La pagina en PNG, a ese ancho en pixeles (el alto, en proporcion).</summary>
            public byte[] Dibujar(int indice, int ancho)
            {
                using (PdfPage pagina = documento.GetPage((uint)indice))
                using (InMemoryRandomAccessStream salida = new InMemoryRandomAccessStream())
                {
                    PdfPageRenderOptions opciones = new PdfPageRenderOptions
                    {
                        DestinationWidth = (uint)ancho,
                        // Con el alto contraste de Windows, los colores de verdad del PDF.
                        IsIgnoringHighContrast = true
                    };
                    Esperar(pagina.RenderToStreamAsync(salida, opciones));

                    uint largo = (uint)salida.Size;
                    using (DataReader lector = new DataReader(salida.GetInputStreamAt(0)))
                    {
                        Esperar(lector.LoadAsync(largo));
                        byte[] png = new byte[largo];
                        lector.ReadBytes(png);
                        return png;
                    }
                }
            }

            public void Dispose()
            {
                entrada.Dispose();
            }

            // Sin System.Runtime.WindowsRuntime no hay await: se espera a Completed.
            // Si se pasa del tiempo, el Set tardio no debe tumbar nada.
            private static T Esperar<T>(Windows.Foundation.IAsyncOperation<T> operacion)
            {
                EsperarFin(operacion, listo => operacion.Completed = (o, s) => Avisar(listo));
                return operacion.GetResults();
            }

            private static void Esperar(Windows.Foundation.IAsyncAction accion)
            {
                EsperarFin(accion, listo => accion.Completed = (o, s) => Avisar(listo));
            }

            private static void EsperarFin(Windows.Foundation.IAsyncInfo operacion, Action<ManualResetEvent> alTerminar)
            {
                ManualResetEvent listo = new ManualResetEvent(false);
                alTerminar(listo);
                if (!listo.WaitOne(TimeSpan.FromSeconds(SegundosPorOperacion)))
                {
                    operacion.Cancel();
                    throw new TimeoutException("WinRT no termino en " + SegundosPorOperacion + " s.");
                }

                listo.Dispose();
                if (operacion.Status != Windows.Foundation.AsyncStatus.Completed)
                {
                    throw new IOException("WinRT: " + operacion.Status
                        + (operacion.ErrorCode != null ? " (" + operacion.ErrorCode.Message + ")" : ""), operacion.ErrorCode);
                }
            }

            private static void Avisar(ManualResetEvent listo)
            {
                try
                {
                    listo.Set();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }
}
