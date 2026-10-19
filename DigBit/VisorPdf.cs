using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Vista previa de un PDF ya generado (en memoria) dentro de DigBit: las
    /// paginas una debajo de otra sobre fondo gris, con zoom, y "Guardar PDF..."
    /// para elegir donde se descarga. Antes se pedia primero donde guardar y el
    /// PDF se abria despues con el lector del equipo.
    ///
    /// Dibuja con Windows.Data.Pdf (WinRT de Windows 10/11, sin librerias): sus
    /// operaciones se esperan con Completed y un evento, sin
    /// System.Runtime.WindowsRuntime (pide el Windows.winmd del SDK). Todo lo de
    /// WinRT corre en un hilo aparte; la pantalla solo convierte el PNG en Bitmap.
    /// Sin WinRT (Windows viejo) o si falla, lo de antes: el PDF en un temporal
    /// abierto con el lector del equipo y, si tampoco se puede, guardarlo directo.
    /// </summary>
    internal sealed class VisorPdf : Form
    {
        private static readonly Color FondoGris = Color.FromArgb(222, 225, 222);
        private static readonly Color Linea = Color.FromArgb(214, 220, 214);
        private static readonly Color BordeHoja = Color.FromArgb(178, 184, 178);
        private const int Margen = 24;
        private const int Separacion = 18;

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

        // Si a los 20 s no se ve ninguna hoja, se deja el visor y se usa el respaldo.
        private const int SegundosParaPreparar = 20;

        // Para las pruebas (por reflexion): forzar el respaldo sin WinRT y no abrir
        // de verdad el lector del equipo.
        internal static bool ForzarRespaldo { get; set; }
        internal static Action<string> AbrirConElLector = ruta => Process.Start(new ProcessStartInfo { FileName = ruta, UseShellExecute = true });

        private readonly byte[] pdf;
        private readonly string nombreSugerido;

        private readonly Panel pnlArriba;
        private readonly Panel pnlAbajo;
        private readonly Lienzo lienzo;
        private readonly Label lblArchivo;
        private readonly Label lblAyuda;
        private readonly Label lblPagina;
        private readonly Label lblZoom;
        private readonly BotonCabecera btnAlejar;
        private readonly BotonCabecera btnAcercar;
        private readonly BotonCabecera btnAjustar;
        private readonly Label lblAviso;
        private readonly Button btnGuardar;
        private readonly Button btnCerrar;
        private readonly ToolTip ayuda = new ToolTip { InitialDelay = 400 };
        private readonly Timer esperaDibujo = new Timer { Interval = 150 };
        private readonly Timer esperaAviso = new Timer { Interval = 10000 };
        private readonly Timer esperaCarga = new Timer { Interval = SegundosParaPreparar * 1000 };
        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);
        private readonly DrawingFont fuenteAyuda = new DrawingFont("Century Gothic", 9F);
        private readonly DrawingFont fuenteNegrita = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold);
        private readonly DrawingFont fuenteHoja = new DrawingFont("Century Gothic", 11F);
        // El - y el + con la letra de las pastillas se ven diminutos.
        private readonly DrawingFont fuenteSigno = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);

        private List<Hoja> hojas;
        private bool ajustarAlAncho = true;
        private float escala = 1f;
        private bool cambiandoZoom;
        private int ruedaZoom;
        private bool algunaMostrada;
        private bool guardando;
        private string fallo;
        private string guardadoEn;

        // El hilo de WinRT: la pantalla le deja la lista de lo que falta dibujar
        // (lo visible primero) y la cambia entera cuando se mueve o cambia el zoom.
        private readonly object candado = new object();
        private readonly AutoResetEvent hayPedidos = new AutoResetEvent(false);
        private List<Pedido> pedidos = new List<Pedido>();
        private Pedido enCurso;
        private bool hiloVivo;
        private bool eventoLiberado;
        private volatile bool cerrando;

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

        private VisorPdf(byte[] pdf, string nombreSugerido)
        {
            this.pdf = pdf;
            this.nombreSugerido = string.IsNullOrWhiteSpace(nombreSugerido) ? "Bitacora.pdf" : nombreSugerido;

            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Vista previa del PDF";
            // Encima de la pantalla del kiosco, que esta siempre al frente.
            TopMost = Kiosco.Activo;
            Colocar(Screen.PrimaryScreen);

            pnlArriba = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Color.White };
            pnlArriba.Paint += (s, e) => PintarLinea(e.Graphics, pnlArriba.Height - 1, pnlArriba.Width);
            pnlArriba.Resize += (s, e) => ColocarArriba();

            lblArchivo = new Label
            {
                AutoEllipsis = true,
                AutoSize = false,
                Font = fuenteTitulo,
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 11),
                Size = new Size(400, 24),
                Text = this.nombreSugerido,
                UseMnemonic = false
            };

            lblAyuda = new Label
            {
                AutoEllipsis = true,
                AutoSize = false,
                Font = fuenteAyuda,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(25, 37),
                Size = new Size(400, 18),
                Text = "Así saldrá el PDF. Ctrl + rueda del ratón: acercar o alejar."
            };

            lblPagina = new Label
            {
                AutoSize = false,
                Font = fuenteNegrita,
                ForeColor = Color.DarkGreen,
                Size = new Size(150, 24),
                Text = "",
                TextAlign = ContentAlignment.MiddleRight
            };

            lblZoom = new Label
            {
                AutoSize = false,
                Font = fuenteNegrita,
                ForeColor = Color.DarkGreen,
                Size = new Size(60, 24),
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter
            };

            btnAlejar = new BotonCabecera("−") { AccessibleName = "Alejar", Font = fuenteSigno };
            btnAcercar = new BotonCabecera("+") { AccessibleName = "Acercar", Font = fuenteSigno };
            btnAjustar = new BotonCabecera("Ajustar al ancho");
            btnAlejar.Click += (s, e) => { Alejar(null); lienzo.Focus(); };
            btnAcercar.Click += (s, e) => { Acercar(null); lienzo.Focus(); };
            btnAjustar.Click += (s, e) => { AjustarAlAncho(); lienzo.Focus(); };
            ayuda.SetToolTip(btnAlejar, "Alejar (Ctrl + rueda del ratón, o Ctrl y -)");
            ayuda.SetToolTip(btnAcercar, "Acercar (Ctrl + rueda del ratón, o Ctrl y +)");
            ayuda.SetToolTip(btnAjustar, "La hoja a todo el ancho de la ventana (Ctrl y 0)");

            pnlArriba.Controls.AddRange(new Control[] { lblArchivo, lblAyuda, lblPagina, btnAlejar, lblZoom, btnAcercar, btnAjustar });

            pnlAbajo = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Color.White };
            pnlAbajo.Paint += (s, e) => PintarLinea(e.Graphics, 0, pnlAbajo.Width);
            pnlAbajo.Resize += (s, e) => ColocarAbajo();

            lblAviso = new Label
            {
                AutoEllipsis = true,
                AutoSize = false,
                Font = fuenteNegrita,
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 14),
                Size = new Size(300, 34),
                Text = "",
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };

            btnGuardar = EstiloHorarios.CrearBoton("Guardar PDF...", new Point(0, 14), new Size(150, 34));
            btnGuardar.Click += (s, e) => Guardar();
            btnCerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(0, 14), new Size(90, 34));
            btnCerrar.Click += (s, e) => Close();
            ayuda.SetToolTip(btnGuardar, "Elegir dónde guardar este PDF (Ctrl + G)");
            pnlAbajo.Controls.AddRange(new Control[] { lblAviso, btnGuardar, btnCerrar });
            if (Kiosco.Activo)
            {
                // En el kiosco el dialogo de archivos de Windows es una salida al escritorio:
                // solo se ve el PDF.
                btnGuardar.Visible = false;
                lblAviso.Text = "En los equipos del laboratorio solo se ve el PDF: para guardarlo, ábrelo desde tu propio equipo.";
            }

            lienzo = new Lienzo { Dock = DockStyle.Fill, BackColor = FondoGris, AccessibleName = "Hojas del PDF" };
            lienzo.Paint += PintarLienzo;
            lienzo.Resize += (s, e) => AlCambiarTamano();
            lienzo.Movido += (s, e) => AlMoverse();
            lienzo.ZoomConRueda += ZoomConRueda;

            // El de Dock Fill primero: se acomoda el ultimo, en lo que dejan los otros dos.
            Controls.Add(lienzo);
            Controls.Add(pnlArriba);
            Controls.Add(pnlAbajo);

            CancelButton = btnCerrar;
            ActiveControl = lienzo;

            esperaDibujo.Tick += (s, e) =>
            {
                esperaDibujo.Stop();
                PedirDibujos();
            };
            esperaAviso.Tick += (s, e) =>
            {
                esperaAviso.Stop();
                lblAviso.Text = "";
                ayuda.SetToolTip(lblAviso, null);
            };
            esperaCarga.Tick += (s, e) =>
            {
                esperaCarga.Stop();
                if (!algunaMostrada)
                {
                    Fallar("la vista previa no estuvo lista en " + SegundosParaPreparar + " s", null);
                }
            };

            ColocarArriba();
            ColocarAbajo();
        }

        // =====================================================================
        // Entrada
        // =====================================================================

        /// <summary>
        /// Ensena el PDF y deja guardarlo donde se elija. Devuelve donde se guardo
        /// (la ultima vez), o null si no se guardo.
        /// </summary>
        public static string Mostrar(IWin32Window dueno, byte[] pdf, string nombreSugerido)
        {
            if (pdf == null || pdf.Length == 0)
            {
                throw new ArgumentException("El PDF esta vacio.", "pdf");
            }

            string guardado = null;
            string motivo = MotivoSinVisor();
            if (motivo == null)
            {
                using (VisorPdf visor = new VisorPdf(pdf, nombreSugerido))
                {
                    visor.Colocar(PantallaDe(dueno));
                    visor.ShowDialog(dueno);
                    guardado = visor.guardadoEn;
                    motivo = visor.fallo;
                }

                if (motivo == null)
                {
                    return guardado;
                }
            }

            if (Kiosco.Activo)
            {
                // El lector de PDF del equipo y el dialogo de archivos son salidas del kiosco.
                Log.Aviso("Visor de PDF: " + motivo + "; en el kiosco no se abre con el lector del equipo.");
                MessageBox.Show(dueno, "No se pudo mostrar el PDF en este equipo. Ábrelo desde tu propio equipo o desde el de la administración.",
                    "Vista previa del PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return guardado;
            }

            Log.Aviso("Visor de PDF: " + motivo + "; se abre con el lector de PDF del equipo.");
            return AbrirFuera(dueno, pdf, nombreSugerido) ?? guardado;
        }

        /// <summary>Por que no se puede usar el visor (null: si se puede).</summary>
        private static string MotivoSinVisor()
        {
            if (ForzarRespaldo)
            {
                return "respaldo forzado (prueba)";
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
        /// Lo de antes del visor: el PDF en un temporal abierto con el lector del
        /// equipo (desde ahi se guarda). Si tampoco se puede, se pide donde guardarlo.
        /// </summary>
        private static string AbrirFuera(IWin32Window dueno, byte[] pdf, string nombreSugerido)
        {
            string temporal = null;
            try
            {
                temporal = ArchivoTemporal(nombreSugerido);
                File.WriteAllBytes(temporal, pdf);
                AbrirConElLector(temporal);
                return null;
            }
            catch (Exception ex)
            {
                Log.Aviso("Visor de PDF: tampoco se pudo abrir con el lector del equipo"
                    + (temporal == null ? "" : " (" + Ascii(temporal) + ")") + ": " + Describir(ex) + "; se pide donde guardarlo.");
            }

            string ruta = PedirRuta(dueno, nombreSugerido);
            if (ruta == null || !Escribir(dueno, ruta, pdf))
            {
                return null;
            }

            MessageBox.Show(dueno, "El PDF se guardó en: " + ruta, "Guardar PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return ruta;
        }

        /// <summary>%TEMP%\DigBit\PDF\nombre.pdf, o "nombre (2).pdf" si el lector tiene abierto el anterior.</summary>
        private static string ArchivoTemporal(string nombre)
        {
            string carpeta = Path.Combine(Path.GetTempPath(), "DigBit", "PDF");
            Directory.CreateDirectory(carpeta);

            // Los de dias pasados ya no hacen falta; si alguno sigue abierto, se queda.
            foreach (string viejo in Directory.GetFiles(carpeta, "*.pdf"))
            {
                try
                {
                    if (File.GetLastWriteTime(viejo) < DateTime.Now.AddDays(-1))
                    {
                        File.Delete(viejo);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            string sinExtension = Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(nombre) ? "Bitacora.pdf" : nombre);
            for (int n = 1; n <= 50; n++)
            {
                string ruta = Path.Combine(carpeta, sinExtension + (n == 1 ? "" : " (" + n + ")") + ".pdf");
                try
                {
                    if (File.Exists(ruta))
                    {
                        File.Delete(ruta);
                    }

                    return ruta;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return Path.Combine(carpeta, sinExtension + " " + Guid.NewGuid().ToString("N").Substring(0, 8) + ".pdf");
        }

        private static Screen PantallaDe(IWin32Window dueno)
        {
            Control control = dueno as Control;
            return control != null && control.IsHandleCreated ? Screen.FromControl(control) : Screen.FromPoint(Cursor.Position);
        }

        /// <summary>Grande desde el principio: casi todo el alto de la pantalla, centrado.</summary>
        private void Colocar(Screen pantalla)
        {
            Rectangle area = pantalla.WorkingArea;
            Size tamano = new Size(Math.Min(1100, area.Width - 40), Math.Max(420, area.Height - 40));
            MinimumSize = new Size(Math.Min(640, area.Width), Math.Min(480, area.Height));
            Bounds = new Rectangle(area.X + (area.Width - tamano.Width) / 2, area.Y + (area.Height - tamano.Height) / 2, tamano.Width, tamano.Height);
        }

        // =====================================================================
        // Ventana
        // =====================================================================

        private void ColocarArriba()
        {
            if (btnAjustar == null)
            {
                return;
            }

            int centro = (pnlArriba.Height - 1) / 2;
            int x = pnlArriba.ClientSize.Width - 21;
            btnAjustar.Location = new Point(x - btnAjustar.Width, centro - btnAjustar.Height / 2);
            x = btnAjustar.Left - 4;
            btnAcercar.Location = new Point(x - btnAcercar.Width, centro - btnAcercar.Height / 2);
            x = btnAcercar.Left;
            lblZoom.Location = new Point(x - lblZoom.Width, centro - lblZoom.Height / 2);
            x = lblZoom.Left;
            btnAlejar.Location = new Point(x - btnAlejar.Width, centro - btnAlejar.Height / 2);
            x = btnAlejar.Left - 12;
            lblPagina.Location = new Point(x - lblPagina.Width, centro - lblPagina.Height / 2);
            int anchoTitulo = Math.Max(60, lblPagina.Left - 16 - lblArchivo.Left);
            lblArchivo.Width = anchoTitulo;
            lblAyuda.Width = anchoTitulo;
        }

        private void ColocarAbajo()
        {
            if (btnCerrar == null)
            {
                return;
            }

            int y = (pnlAbajo.Height - btnCerrar.Height) / 2 + 1;
            btnCerrar.Location = new Point(pnlAbajo.ClientSize.Width - 24 - btnCerrar.Width, y);
            btnGuardar.Location = new Point(btnCerrar.Left - 10 - btnGuardar.Width, y);
            lblAviso.Bounds = new Rectangle(24, y, Math.Max(40, btnGuardar.Left - 16 - 24), btnCerrar.Height);
        }

        private static void PintarLinea(Graphics g, int y, int ancho)
        {
            using (Pen lapiz = new Pen(Linea))
            {
                g.DrawLine(lapiz, 0, y, ancho, y);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            lienzo.Focus();
            esperaCarga.Start();

            lock (candado)
            {
                hiloVivo = true;
            }

            Thread hilo = new Thread(Trabajar) { IsBackground = true, Name = "VisorPdf" };
            hilo.SetApartmentState(ApartmentState.MTA);
            hilo.Start();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Add:
                case Keys.Control | Keys.Oemplus:
                    Acercar(null);
                    return true;
                case Keys.Control | Keys.Subtract:
                case Keys.Control | Keys.OemMinus:
                    Alejar(null);
                    return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0:
                    AjustarAlAncho();
                    return true;
                case Keys.Control | Keys.G:
                case Keys.Control | Keys.S:
                    Guardar();
                    return true;
                case Keys.Down:
                    lienzo.Desplazar(0, 40);
                    return true;
                case Keys.Up:
                    lienzo.Desplazar(0, -40);
                    return true;
                case Keys.Right:
                    lienzo.Desplazar(40, 0);
                    return true;
                case Keys.Left:
                    lienzo.Desplazar(-40, 0);
                    return true;
                case Keys.PageDown:
                    lienzo.Desplazar(0, Math.Max(40, lienzo.ClientSize.Height - 40));
                    return true;
                case Keys.PageUp:
                    lienzo.Desplazar(0, -Math.Max(40, lienzo.ClientSize.Height - 40));
                    return true;
                case Keys.Home:
                case Keys.Control | Keys.Home:
                    lienzo.IrA(new Point(-lienzo.AutoScrollPosition.X, 0));
                    return true;
                case Keys.End:
                case Keys.Control | Keys.End:
                    lienzo.IrA(new Point(-lienzo.AutoScrollPosition.X, lienzo.AutoScrollMinSize.Height));
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            cerrando = true;
            Despertar();
            esperaDibujo.Stop();
            esperaAviso.Stop();
            esperaCarga.Stop();
            LiberarImagenes();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cerrando = true;
                Despertar();
                LiberarImagenes();
                esperaDibujo.Dispose();
                esperaAviso.Dispose();
                esperaCarga.Dispose();
                ayuda.Dispose();
                fuenteTitulo.Dispose();
                fuenteAyuda.Dispose();
                fuenteNegrita.Dispose();
                fuenteHoja.Dispose();
                fuenteSigno.Dispose();

                // Si el hilo vive, lo libera el al salir (puede estar esperandolo).
                lock (candado)
                {
                    if (!hiloVivo && !eventoLiberado)
                    {
                        hayPedidos.Dispose();
                        eventoLiberado = true;
                    }
                }
            }

            base.Dispose(disposing);
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
        // Guardar
        // =====================================================================

        private void Guardar()
        {
            // En el kiosco no hay boton, pero Ctrl + G y Ctrl + S tampoco abren el dialogo de archivos.
            if (Kiosco.Activo)
            {
                return;
            }

            // Mientras esta abierto el dialogo, un fallo del visor no lo cierra: espera a que vuelva.
            guardando = true;
            try
            {
                string ruta = PedirRuta(this, nombreSugerido);
                if (ruta != null)
                {
                    GuardarEn(ruta);
                }
            }
            finally
            {
                guardando = false;
            }

            if (fallo != null)
            {
                Close();
                return;
            }

            // La rueda y las flechas, otra vez a las hojas.
            lienzo.Focus();
        }

        /// <summary>Escribe los mismos bytes que se ven y dice donde quedaron.</summary>
        private bool GuardarEn(string ruta)
        {
            if (!Escribir(this, ruta, pdf))
            {
                return false;
            }

            guardadoEn = ruta;
            lblAviso.Text = AvisoGuardado(ruta);
            ayuda.SetToolTip(lblAviso, lblAviso.Text.EndsWith(ruta, StringComparison.Ordinal) ? null : ruta);
            esperaAviso.Stop();
            esperaAviso.Start();
            return true;
        }

        /// <summary>
        /// "PDF guardado en: ruta" en un renglon. Si no cabe, se quitan las carpetas
        /// de arriba (C:\...\Documentos\archivo.pdf): lo que importa es donde quedo
        /// al final y con que nombre. La ruta entera, en la ayuda del aviso.
        /// </summary>
        private string AvisoGuardado(string ruta)
        {
            const string Prefijo = "PDF guardado en: ";
            Func<string, bool> cabe = texto => TextRenderer.MeasureText(texto, lblAviso.Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width <= lblAviso.Width - 4;
            if (cabe(Prefijo + ruta))
            {
                return Prefijo + ruta;
            }

            string[] partes = ruta.Split(Path.DirectorySeparatorChar);
            for (int quitar = 1; quitar < partes.Length - 1; quitar++)
            {
                string corta = partes[0] + @"\...\" + string.Join(@"\", partes.Skip(1 + quitar));
                if (cabe(Prefijo + corta))
                {
                    return Prefijo + corta;
                }
            }

            return Prefijo + partes[partes.Length - 1];
        }

        private static string PedirRuta(IWin32Window dueno, string nombre)
        {
            using (SaveFileDialog dialogo = new SaveFileDialog
            {
                // Sirve igual para la bitacora que para el horario de un laboratorio.
                Title = "Guardar el PDF",
                Filter = "Archivo PDF (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                FileName = nombre,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                return dialogo.ShowDialog(dueno) == DialogResult.OK ? dialogo.FileName : null;
            }
        }

        private static bool Escribir(IWin32Window dueno, string ruta, byte[] pdf)
        {
            try
            {
                File.WriteAllBytes(ruta, pdf);
                return true;
            }
            catch (IOException ex)
            {
                Log.Aviso("Visor de PDF: no se pudo escribir " + Ascii(ruta) + ": " + Describir(ex));
                MessageBox.Show(dueno, ExplicarArchivo("No se pudo guardar el PDF", ex, "el lector de PDF"),
                    "Guardar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Aviso("Visor de PDF: no se pudo guardar " + Ascii(ruta) + ": " + Describir(ex));
                MessageBox.Show(dueno, ExplicarArchivo("No se pudo guardar el PDF", ex, "el lector de PDF"), "Guardar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return false;
        }

        /// <summary>
        /// Por que no se pudo guardar un archivo en ese sitio y que hacer (RNF12),
        /// sin el texto de Windows (ese va al registro, lo escribe quien llama):
        /// una carpeta que ya no existe, una ruta demasiado larga, el archivo
        /// abierto en otro programa (programa: "el lector de PDF", "Excel") o el
        /// disco lleno, una carpeta sin permiso, o un nombre que no vale. Lo usan
        /// Guardar PDF y "Guardar en Excel (CSV)..." de Mis grupos.
        /// </summary>
        internal static string ExplicarArchivo(string queNoSePudo, Exception ex, string programa)
        {
            const string OtraCarpeta = " Elige otra carpeta (por ejemplo, Documentos) e inténtalo de nuevo.";
            if (ex is DirectoryNotFoundException || ex is DriveNotFoundException)
            {
                return queNoSePudo + ": esa carpeta ya no existe (o la memoria USB se quitó)." + OtraCarpeta;
            }

            if (ex is PathTooLongException)
            {
                return queNoSePudo + ": la ruta del archivo es demasiado larga. Elige una carpeta más cercana (por ejemplo, Documentos) o un nombre más corto.";
            }

            if (ex is IOException)
            {
                return queNoSePudo + ": el archivo está abierto en otro programa"
                    + (string.IsNullOrEmpty(programa) ? "" : " (¿" + programa + "?)")
                    + " o el disco está lleno. Ciérralo, o guárdalo con otro nombre, e inténtalo de nuevo.";
            }

            if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                return queNoSePudo + ": Windows no deja guardar en esa carpeta (o el archivo es de solo lectura)." + OtraCarpeta;
            }

            return queNoSePudo + ": el nombre o la carpeta no son válidos." + OtraCarpeta;
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
            int y = Margen;
            foreach (Hoja hoja in hojas)
            {
                int ancho = Math.Max(1, (int)(hoja.Ancho * escala));
                int alto = Math.Max(1, (int)(hoja.Alto * escala));
                hoja.Lugar = new Rectangle(Margen + (anchoMayor - ancho) / 2, y, ancho, alto);
                y += alto + Separacion;
            }

            lienzo.AutoScrollMinSize = new Size(anchoMayor + 2 * Margen, y - Separacion + Margen);
            lblZoom.Text = Math.Round(escala * 100).ToString(CultureInfo.InvariantCulture) + " %";
        }

        private float EscalaParaElAncho()
        {
            double anchoMayor = hojas.Max(h => h.Ancho);
            double altoTotal = hojas.Sum(h => h.Alto);
            int disponible = lienzo.Width - 2 * Margen;

            // Si a ese ancho el documento no cabe a lo alto, la barra vertical le quita su parte.
            double alto = altoTotal * disponible / anchoMayor + (hojas.Count - 1) * Separacion + 2 * Margen;
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

        private void AjustarAlAncho()
        {
            CambiarZoom(true, escala, null);
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
                if (y < hojas[i].Lugar.Bottom + Separacion / 2)
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
                lblPagina.Text = "";
                return;
            }

            int arriba = -lienzo.AutoScrollPosition.Y;
            int maximo = lienzo.AutoScrollMinSize.Height - lienzo.ClientSize.Height;
            int actual = arriba <= 0 ? 0
                : arriba >= maximo ? hojas.Count - 1
                : HojaEn(arriba + lienzo.ClientSize.Height / 2);
            lblPagina.Text = "Página " + (actual + 1) + " de " + hojas.Count;
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
            Graphics g = e.Graphics;
            g.Clear(FondoGris);

            if (hojas == null)
            {
                TextRenderer.DrawText(g, "Preparando la vista previa...", fuenteNegrita, lienzo.ClientRectangle, Color.FromArgb(60, 80, 60),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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
        // Lo que llega del hilo de WinRT
        // =====================================================================

        private void AlAbrir(List<Hoja> leidas)
        {
            if (cerrando)
            {
                return;
            }

            hojas = leidas;
            Distribuir();
            lienzo.IrA(Point.Empty);
            lienzo.Invalidate();
            ActualizarPagina();
            PedirDibujos();
        }

        private void AlDibujar(Pedido pedido, byte[] png, Exception error)
        {
            if (cerrando || hojas == null || pedido.Indice >= hojas.Count)
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

        /// <summary>El visor no puede seguir: se cierra y Mostrar pasa al respaldo.</summary>
        private void Fallar(string que, Exception ex)
        {
            if (cerrando || fallo != null)
            {
                return;
            }

            fallo = que;
            Log.Aviso("Visor de PDF: " + que + (ex == null ? "" : ": " + Ascii(ex.ToString())));
            if (!guardando)
            {
                Close();
            }
        }

        // =====================================================================
        // Hilo de WinRT
        // =====================================================================

        /// <summary>
        /// Lo que falta dibujar, lo visible primero y luego lo de una vista arriba
        /// y abajo. Lo que ya esta a su ancho, o se esta dibujando, no se pide.
        /// </summary>
        private void PedirDibujos()
        {
            if (hojas == null || cerrando)
            {
                return;
            }

            Rectangle vista = Vista();
            Rectangle cerca = Rectangle.Inflate(vista, 0, vista.Height);
            List<Pedido> nuevos = new List<Pedido>();
            lock (candado)
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
                        bool enCamino = enCurso != null && enCurso.Indice == i && enCurso.Ancho == ancho;
                        if (!yaEsta && !enCamino)
                        {
                            nuevos.Add(new Pedido { Indice = i, Ancho = ancho });
                        }
                    }
                }

                pedidos = nuevos;
            }

            Despertar();
        }

        private void Despertar()
        {
            lock (candado)
            {
                if (!eventoLiberado)
                {
                    hayPedidos.Set();
                }
            }
        }

        /// <summary>El hilo de WinRT: abre el documento, lee sus paginas y dibuja lo que se le pide.</summary>
        private void Trabajar()
        {
            DocumentoWinRT documento = null;
            try
            {
                List<Hoja> leidas = new List<Hoja>();
                try
                {
                    documento = DocumentoWinRT.Abrir(pdf);
                    int paginas = documento.Paginas;
                    if (paginas == 0)
                    {
                        throw new InvalidDataException("El PDF no tiene paginas.");
                    }

                    for (int i = 0; i < paginas; i++)
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
                    Publicar(() => Fallar("no se pudo abrir el PDF con Windows.Data.Pdf", ex));
                    return;
                }

                Publicar(() => AlAbrir(leidas));

                while (!cerrando)
                {
                    Pedido pedido;
                    lock (candado)
                    {
                        enCurso = pedidos.Count > 0 ? pedidos[0] : null;
                        if (enCurso != null)
                        {
                            pedidos.RemoveAt(0);
                        }

                        pedido = enCurso;
                    }

                    if (pedido == null)
                    {
                        hayPedidos.WaitOne();
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

                    Publicar(() => AlDibujar(pedido, png, error));
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

                lock (candado)
                {
                    hiloVivo = false;
                    enCurso = null;
                    if (!eventoLiberado)
                    {
                        hayPedidos.Dispose();
                        eventoLiberado = true;
                    }
                }
            }
        }

        /// <summary>Al hilo de la pantalla; si la ventana ya se cerro, se pierde.</summary>
        private void Publicar(Action accion)
        {
            if (cerrando)
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
        private static string Ascii(string texto)
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
        /// llamarlo y no al cargar el visor. Se usa siempre desde el mismo hilo (MTA).
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
