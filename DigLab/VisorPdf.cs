using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigLab
{
    /// <summary>
    /// Vista previa de un PDF ya generado (en memoria) dentro de DigLab: las
    /// paginas una debajo de otra sobre fondo gris, con zoom, y "Guardar PDF..."
    /// para elegir donde se descarga. Antes se pedia primero donde guardar y el
    /// PDF se abria despues con el lector del equipo.
    ///
    /// Las hojas, el desplazamiento y el zoom son de VistaPreviaPdf (la misma que
    /// el panel de la izquierda de Bitacoras), que dibuja con Windows.Data.Pdf en
    /// un hilo aparte; aqui van la cabecera, los botones, las teclas y el guardar.
    /// Sin WinRT (Windows viejo) o si falla, lo de antes: el PDF en un temporal
    /// abierto con el lector del equipo y, si tampoco se puede, guardarlo directo.
    /// </summary>
    internal sealed class VisorPdf : Form
    {
        private static readonly Color Linea = Color.FromArgb(214, 220, 214);

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
        private readonly VistaPreviaPdf vista;
        private readonly Label lblArchivo;
        private readonly Label lblAyuda;
        private readonly Label lblPagina;
        private readonly Label lblZoom;
        private readonly BotonCabecera btnAlejar;
        private readonly BotonCabecera btnAcercar;
        private readonly BotonCabecera btnAjustar;
        private readonly Label lblAviso;
        private readonly Button btnGuardar;
        // Otra forma de guardarlo (Mostrar con otraForma), a la izquierda de "Guardar PDF...":
        // en Bitacoras, el lote, "Uno por sesion...". Null si no la hay.
        private readonly Button btnOtraForma;
        private readonly Button btnCerrar;
        private readonly ToolTip ayuda = new ToolTip { InitialDelay = 400 };
        private readonly Timer esperaAviso = new Timer { Interval = 10000 };
        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);
        private readonly DrawingFont fuenteAyuda = new DrawingFont("Century Gothic", 9F);
        private readonly DrawingFont fuenteNegrita = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold);
        // El - y el + con la letra de las pastillas se ven diminutos.
        private readonly DrawingFont fuenteSigno = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);

        private bool guardando;
        private bool cerrando;
        private string fallo;
        private string guardadoEn;

        private VisorPdf(byte[] pdf, string nombreSugerido, string textoOtraForma, Action<IWin32Window> otraForma)
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
            btnAlejar.Click += (s, e) => { vista.Alejar(); vista.EnfocarHojas(); };
            btnAcercar.Click += (s, e) => { vista.Acercar(); vista.EnfocarHojas(); };
            btnAjustar.Click += (s, e) => { vista.AjustarAlAncho(); vista.EnfocarHojas(); };
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
            if (otraForma != null && !string.IsNullOrEmpty(textoOtraForma) && !Kiosco.Activo)
            {
                btnOtraForma = EstiloHorarios.CrearBoton(textoOtraForma,
                    new Point(0, 14), new Size(Math.Max(150, TextRenderer.MeasureText(textoOtraForma, btnGuardar.Font).Width + 30), 34));
                btnOtraForma.Click += (s, e) => otraForma(this);
                pnlAbajo.Controls.Add(btnOtraForma);
            }

            if (Kiosco.Activo)
            {
                // En el kiosco el dialogo de archivos de Windows es una salida al escritorio:
                // solo se ve el PDF.
                btnGuardar.Visible = false;
                lblAviso.Text = "En los equipos del laboratorio solo se ve el PDF: para guardarlo, ábrelo desde tu propio equipo.";
            }

            // Las hojas: la pagina y el zoom van arriba; si no se puede ensenar (no
            // abre, no dibuja la primera o tarda mas de 20 s), Mostrar pasa al respaldo.
            vista = new VistaPreviaPdf { Dock = DockStyle.Fill, SegundosParaPreparar = SegundosParaPreparar };
            vista.EstadoCambiado += (s, e) =>
            {
                lblPagina.Text = vista.TextoPagina;
                lblZoom.Text = vista.TextoZoom;
            };
            vista.Fallo += (que, ex) => Fallar(que);

            // El de Dock Fill primero: se acomoda el ultimo, en lo que dejan los otros dos.
            Controls.Add(vista);
            Controls.Add(pnlArriba);
            Controls.Add(pnlAbajo);

            CancelButton = btnCerrar;
            ActiveControl = vista;

            esperaAviso.Tick += (s, e) =>
            {
                esperaAviso.Stop();
                lblAviso.Text = "";
                ayuda.SetToolTip(lblAviso, null);
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
        /// <param name="textoOtraForma">Con otraForma: el texto de su boton, junto a "Guardar PDF...".</param>
        /// <param name="otraForma">Otra forma de guardarlo (por ejemplo, un PDF por sesion en una carpeta), con el visor como dueno.</param>
        public static string Mostrar(IWin32Window dueno, byte[] pdf, string nombreSugerido, string textoOtraForma = null, Action<IWin32Window> otraForma = null)
        {
            if (pdf == null || pdf.Length == 0)
            {
                throw new ArgumentException("El PDF esta vacio.", "pdf");
            }

            string guardado = null;
            string motivo = MotivoSinVisor();
            if (motivo == null)
            {
                using (VisorPdf visor = new VisorPdf(pdf, nombreSugerido, textoOtraForma, otraForma))
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

            return VistaPreviaPdf.MotivoSinWinRT();
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

        /// <summary>%TEMP%\DigLab\PDF\nombre.pdf, o "nombre (2).pdf" si el lector tiene abierto el anterior.</summary>
        private static string ArchivoTemporal(string nombre)
        {
            string carpeta = Path.Combine(Path.GetTempPath(), "DigLab", "PDF");
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
            int izquierdaBotones = btnGuardar.Left;
            if (btnOtraForma != null)
            {
                btnOtraForma.Location = new Point(btnGuardar.Left - 10 - btnOtraForma.Width, y);
                izquierdaBotones = btnOtraForma.Left;
            }

            lblAviso.Bounds = new Rectangle(24, y, Math.Max(40, izquierdaBotones - 16 - 24), btnCerrar.Height);
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
            vista.EnfocarHojas();
            vista.Mostrar(pdf);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Add:
                case Keys.Control | Keys.Oemplus:
                    vista.Acercar();
                    return true;
                case Keys.Control | Keys.Subtract:
                case Keys.Control | Keys.OemMinus:
                    vista.Alejar();
                    return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0:
                    vista.AjustarAlAncho();
                    return true;
                case Keys.Control | Keys.G:
                case Keys.Control | Keys.S:
                    Guardar();
                    return true;
                case Keys.Down:
                    vista.Desplazar(0, 40);
                    return true;
                case Keys.Up:
                    vista.Desplazar(0, -40);
                    return true;
                case Keys.Right:
                    vista.Desplazar(40, 0);
                    return true;
                case Keys.Left:
                    vista.Desplazar(-40, 0);
                    return true;
                case Keys.PageDown:
                    vista.PaginaAbajo();
                    return true;
                case Keys.PageUp:
                    vista.PaginaArriba();
                    return true;
                case Keys.Home:
                case Keys.Control | Keys.Home:
                    vista.AlPrincipio();
                    return true;
                case Keys.End:
                case Keys.Control | Keys.End:
                    vista.AlFinal();
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            cerrando = true;
            esperaAviso.Stop();
            vista.Soltar();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cerrando = true;
                vista.Soltar();
                esperaAviso.Dispose();
                ayuda.Dispose();
                fuenteTitulo.Dispose();
                fuenteAyuda.Dispose();
                fuenteNegrita.Dispose();
                fuenteSigno.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// La vista no pudo ensenar el PDF (ya lo dijo en el log): el visor se
        /// cierra y Mostrar pasa al respaldo. Con el dialogo de guardar abierto,
        /// espera a que vuelva (Guardar).
        /// </summary>
        private void Fallar(string que)
        {
            if (cerrando || fallo != null)
            {
                return;
            }

            fallo = que;
            if (!guardando)
            {
                Close();
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
            vista.EnfocarHojas();
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
    }
}
