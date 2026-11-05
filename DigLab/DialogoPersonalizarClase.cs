using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// "Personalizar tu clase": la imagen del circulo (elegir un archivo, pegarla
    /// o quitarla, y encuadrarla), el color (automatico, los de la paleta de
    /// DigLab u otro), un nombre corto y, si da esa materia a mas de un grupo, si
    /// vale para todos sus grupos (y el ciclo siguiente) o solo para este. Abajo,
    /// "Asi se vera": una tarjeta como las de Mis grupos.
    ///
    /// Es solo del profesor: no cambia la clase. Nada se escribe hasta Guardar
    /// (PersonalizacionClasesDatos.Guardar, sincrono como DialogoReporteFalla);
    /// si falla, el motivo sale en la linea de error y lo elegido no se pierde.
    /// Lo abre PersonalizacionClases.Abrir, que publica lo guardado.
    /// </summary>
    internal class DialogoPersonalizarClase : Form
    {
        private const int Ancho = 640;
        private const int AltoMinimo = 580;
        private const int XDerecha = 196;
        private const int AnchoDerecha = 420;
        private const int AltoBanda = 46;

        private const string TituloVentana = "Personalizar tu clase";
        private const string NotaNombre = "Sale en lugar del nombre largo donde no cabe (tus clases del panel, Mi horario y Mis grupos). Con 4 caracteres o menos, va también en el círculo.";
        private const string NotaQuitados = " Se quitaron los caracteres que la base no guarda (emojis).";
        private const string NotaImagen = "PNG, JPG, BMP o GIF. Se guarda en pequeño (128 x 128) en el servidor de la escuela.";

        private static readonly Color Gris = Color.FromArgb(90, 90, 90);
        private static readonly Color Naranja = Color.FromArgb(160, 70, 0);
        private static readonly Color Azul = Color.FromArgb(30, 80, 140);
        private static readonly Color BordeSuave = Color.FromArgb(215, 222, 215);

        private readonly AspectoClase clase;
        // Sus clases de esta materia de este ciclo (y la que se personaliza).
        private readonly List<AspectoClase> deLaMateria;
        private readonly bool teniaImagen;
        // Como estaba la casilla al abrir: Guardar sin tocar nada no escribe (ni borra lo de otro grupo).
        private readonly bool todaAlAbrir;

        private readonly ToolTip ayuda = new ToolTip();
        private readonly DrawingFont fuenteSubtitulo = new DrawingFont("Century Gothic", 10F);
        private readonly DrawingFont fuenteNota = new DrawingFont("Century Gothic", 8.5F);
        private readonly DrawingFont fuenteNotaImagen = new DrawingFont("Century Gothic", 8F);
        private readonly DrawingFont fuenteGrupo = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
        private readonly DrawingFont fuenteMateria = new DrawingFont("Century Gothic", 9.5F);

        private readonly VistaCirculo vista;
        private readonly Label lblNotaVista;
        private readonly Label lblAcercar;
        private readonly TrackBar barraAcercar;
        private readonly Button btnElegir;
        private readonly Button btnPegar;
        private readonly Button btnQuitar;
        private readonly FlowLayoutPanel paleta;
        private readonly List<MuestraColor> muestras = new List<MuestraColor>();
        private readonly MuestraColor muestraAutomatico;
        private readonly MuestraColor muestraOtro;
        private readonly Label lblNotaColor;
        private readonly TextBox txtNombre;
        private readonly Label lblNotaNombre;
        private readonly CheckBox chkTodos;
        private readonly Label lblNotaAlcance;
        private readonly Lienzo tarjeta;
        private readonly Label lblError;
        private readonly Button btnRestablecer;
        private readonly Button btnGuardar;

        // Null = automatico.
        private Color? elegido;
        // El de "Otro color...", si se eligio uno fuera de la paleta.
        private Color? personalizado;
        private bool aclarado;
        // La imagen que se ve (del dialogo: se libera al cerrarlo), o null.
        private Bitmap fuente;
        // Se eligio o pego otra, o se reencuadro la guardada: al guardar va como nueva.
        private bool imagenNueva;
        // Se quito la guardada (aunque no se pudiera ensenar).
        private bool imagenQuitada;
        private bool poniendoZoom;

        public DialogoPersonalizarClase(AspectoClase clase)
        {
            if (clase == null)
            {
                throw new ArgumentNullException("clase");
            }

            this.clase = clase;
            deLaMateria = PersonalizacionClases.DeLaMateria(clase.MateriaId).Where(a => a.Actual || a.ClaseId == clase.ClaseId).ToList();
            if (!deLaMateria.Any(a => a.ClaseId == clase.ClaseId))
            {
                deLaMateria.Add(clase);
            }

            Color guardado;
            if (ColoresProfesor.DesdeTexto(clase.ColorElegido, out guardado))
            {
                elegido = guardado;
                if (ColoresProfesor.Nombre(guardado) == null)
                {
                    personalizado = guardado;
                }
            }

            teniaImagen = clase.ImagenSha1 != null;
            fuente = teniaImagen ? ImagenDeClase.DesdeBytes(PersonalizacionClases.BytesDe(clase.ImagenSha1)) : null;

            // ---- Titulo y subtitulo -------------------------------------------------
            string subtitulo = clase.Materia + ", grupo " + clase.Grupo + ".\r\n"
                + "Tus estudiantes verán el círculo al registrar su bitácora; el nombre corto solo lo ves tú. No cambia la clase: "
                + "la administración, la lista de asistencia y los PDF siguen con el nombre oficial.";
            int altoSubtitulo = Medir(subtitulo, fuenteSubtitulo, Ancho - 48) + 2;
            int y0 = Math.Max(104, 52 + altoSubtitulo + 10);

            // ---- Columna de la imagen -----------------------------------------------
            vista = new VistaCirculo { Location = new Point(28, y0), TabIndex = 0 };
            vista.EncuadreCambiado += () =>
            {
                imagenNueva = fuente != null;
                Actualizar();
            };
            vista.PedirZoom += paso => barraAcercar.Value = Math.Max(barraAcercar.Minimum, Math.Min(barraAcercar.Maximum, barraAcercar.Value + paso));

            // Hasta la columna de la derecha: "Arrastra para encuadrar." no cabia en 152.
            lblNotaVista = Nota(new Rectangle(24, y0 + 132, XDerecha - 30, 32), fuenteNota, Gris);
            lblAcercar = new Label
            {
                AutoSize = false,
                Font = fuenteNota,
                ForeColor = Gris,
                Location = new Point(24, y0 + 168),
                Size = new Size(64, 22),
                Text = "Acercar",
                TextAlign = ContentAlignment.MiddleLeft
            };
            barraAcercar = new TrackBar
            {
                AutoSize = false,
                Location = new Point(88, y0 + 164),
                Size = new Size(92, 30),
                Minimum = 100,
                Maximum = 400,
                SmallChange = 10,
                LargeChange = 50,
                TickStyle = TickStyle.None,
                Value = 100,
                AccessibleName = "Acercar la imagen",
                TabIndex = 1
            };
            barraAcercar.ValueChanged += (s, e) =>
            {
                vista.Zoom = barraAcercar.Value / 100f;
                if (!poniendoZoom && fuente != null)
                {
                    imagenNueva = true;
                    Actualizar();
                }
            };

            btnElegir = EstiloHorarios.CrearBoton("Elegir imagen...", new Point(24, y0 + 200), new Size(152, 32));
            btnElegir.TabIndex = 2;
            btnElegir.Click += (s, e) => ElegirImagen();
            btnPegar = EstiloHorarios.CrearBoton("Pegar imagen", new Point(24, y0 + 238), new Size(152, 32));
            btnPegar.TabIndex = 3;
            btnPegar.Click += (s, e) => PegarImagen();
            btnQuitar = EstiloHorarios.CrearBoton("Quitar imagen", new Point(24, y0 + 276), new Size(152, 32));
            btnQuitar.TabIndex = 4;
            if (Kiosco.Activo)
            {
                // En el kiosco el dialogo de archivos de Windows es una salida al escritorio:
                // la imagen solo se pega (copiada de un navegador).
                btnElegir.Visible = false;
                btnPegar.Top = y0 + 200;
                btnQuitar.Top = y0 + 238;
            }
            btnQuitar.Click += (s, e) => QuitarImagen();
            ayuda.SetToolTip(btnPegar, "Pega la imagen que copiaste (también con Ctrl+V).");
            Label lblNotaImagen = Nota(new Rectangle(24, y0 + 314, 152, 64), fuenteNotaImagen, Color.FromArgb(110, 110, 110));
            lblNotaImagen.Text = NotaImagen;
            int finIzquierda = y0 + 314 + 64;

            // ---- Columna del color, el nombre y el alcance --------------------------
            int y = y0;
            Label etiquetaColor = EstiloHorarios.CrearEtiqueta("Color", new Point(XDerecha, y));
            paleta = new FlowLayoutPanel
            {
                Location = new Point(XDerecha, y + 24),
                Size = new Size(304, 60),
                Margin = new Padding(0),
                Padding = new Padding(0),
                WrapContents = true,
                TabIndex = 5
            };
            muestraAutomatico = AgregarMuestra(null, false, "Automático");
            foreach (Color color in ColoresProfesor.Muestras)
            {
                AgregarMuestra(color, false, Capital(ColoresProfesor.Nombre(color)));
            }

            muestraOtro = AgregarMuestra(personalizado, true, "Otro color...");
            lblNotaColor = Nota(new Rectangle(XDerecha, y + 88, AnchoDerecha, 34), fuenteNota, Gris);
            y += 88 + 34 + 10;

            Label etiquetaNombre = EstiloHorarios.CrearEtiqueta("Nombre corto (opcional)", new Point(XDerecha, y));
            txtNombre = EstiloHorarios.CrearTexto(new Point(XDerecha, y + 24), 240);
            txtNombre.MaxLength = PersonalizacionClasesDatos.LargoNombreCorto;
            txtNombre.AccessibleName = "Nombre corto";
            txtNombre.TabIndex = 6;
            txtNombre.Text = clase.NombreCorto ?? "";
            txtNombre.TextChanged += (s, e) => Actualizar();
            int altoNotaNombre = Medir(NotaNombre + NotaQuitados, fuenteNota, AnchoDerecha) + 2;
            lblNotaNombre = Nota(new Rectangle(XDerecha, y + 58, AnchoDerecha, altoNotaNombre), fuenteNota, Gris);
            y += 58 + altoNotaNombre + 8;

            // Solo si da esa materia a mas de un grupo; si no, la nota y va como la de la materia.
            int grupos = deLaMateria.Select(a => a.GrupoId).Distinct().Count();
            if (grupos >= 2)
            {
                string textoCasilla = "Igual en todos mis grupos de " + clase.Materia;
                int altoCasilla = Math.Max(22, Medir(textoCasilla, fuenteMateria, AnchoDerecha - 20) + 4);
                chkTodos = new CheckBox
                {
                    AutoSize = false,
                    Font = fuenteMateria,
                    ForeColor = Color.FromArgb(30, 30, 30),
                    Location = new Point(XDerecha, y),
                    Size = new Size(AnchoDerecha, altoCasilla),
                    Text = textoCasilla,
                    Checked = !clase.DeSuGrupo,
                    TabIndex = 7
                };
                chkTodos.CheckedChanged += (s, e) => Actualizar();
                lblNotaAlcance = Nota(new Rectangle(XDerecha + 18, y + altoCasilla + 2, AnchoDerecha - 18, 34), fuenteNota, Gris);
                y += altoCasilla + 2 + 34 + 8;
            }
            else
            {
                lblNotaAlcance = Nota(new Rectangle(XDerecha, y, AnchoDerecha, 34), fuenteNota, Gris);
                y += 34 + 8;
            }

            todaAlAbrir = TodaLaMateria;

            Label etiquetaMuestra = EstiloHorarios.CrearEtiqueta("Así se verá", new Point(XDerecha, y));
            tarjeta = new Lienzo { Location = new Point(XDerecha, y + 24), Size = new Size(300, 64), AccessibleName = "Así se verá", TabStop = false };
            tarjeta.Paint += PintarTarjeta;
            y += 24 + 64 + 8;

            // Tres renglones: un fallo dice por que y que hacer (MensajeDeFallo).
            lblError = EstiloAdmin.LineaError(new Point(XDerecha, y), AnchoDerecha, 56);
            y += 56 + 8;

            int alto = Math.Max(AltoMinimo, Math.Max(y, finIzquierda + 8) + 56);
            EstiloHorarios.PrepararDialogo(this, TituloVentana, new Size(Ancho, alto));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo(TituloVentana, Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = fuenteSubtitulo,
                ForeColor = Color.FromArgb(70, 70, 70),
                Location = new Point(24, 52),
                Size = new Size(Ancho - 48, altoSubtitulo),
                Text = subtitulo
            });
            Controls.AddRange(new Control[]
            {
                vista, lblNotaVista, lblAcercar, barraAcercar, btnElegir, btnPegar, btnQuitar, lblNotaImagen,
                etiquetaColor, paleta, lblNotaColor, etiquetaNombre, txtNombre, lblNotaNombre, lblNotaAlcance,
                etiquetaMuestra, tarjeta, lblError
            });
            if (chkTodos != null)
            {
                Controls.Add(chkTodos);
            }

            btnRestablecer = EstiloHorarios.CrearBoton("Restablecer", new Point(24, ClientSize.Height - 56), new Size(130, 40));
            btnRestablecer.TabIndex = 8;
            btnRestablecer.Click += (s, e) => Restablecer();
            ayuda.SetToolTip(btnRestablecer, "Sin imagen, color automático y el nombre oficial. Se aplica al guardar.");
            Controls.Add(btnRestablecer);
            btnGuardar = EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            btnGuardar.TabIndex = 9;
            if (CancelButton is Button cancelar)
            {
                cancelar.TabIndex = 10;
            }

            vista.Fuente = fuente;
            Actualizar();
            MuestraColor marcada = muestras.FirstOrDefault(m => m.Checked);
            ActiveControl = (Control)marcada ?? txtNombre;
        }

        /// <summary>True si se guardo algo (y ya se publico).</summary>
        public bool Guardado { get; private set; }

        private Color ColorActual
        {
            get { return elegido.HasValue ? ColoresProfesor.ParaTextoNegro(elegido.Value) : clase.ColorAutomatico; }
        }

        private bool TodaLaMateria
        {
            get { return chkTodos == null || chkTodos.Checked; }
        }

        private string NombreLimpio(out bool quito)
        {
            return PersonalizacionClasesDatos.LimpiarNombre(txtNombre.Text, out quito);
        }

        // Ctrl+V pega la imagen, salvo con el foco en el nombre (ahi pega texto).
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.V) && !txtNombre.Focused)
            {
                PegarImagen();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                vista.Fuente = null;
                if (fuente != null)
                {
                    fuente.Dispose();
                    fuente = null;
                }

                ayuda.Dispose();
                fuenteSubtitulo.Dispose();
                fuenteNota.Dispose();
                fuenteNotaImagen.Dispose();
                fuenteGrupo.Dispose();
                fuenteMateria.Dispose();
            }

            base.Dispose(disposing);
        }

        // =====================================================================
        // La imagen
        // =====================================================================

        private void ElegirImagen()
        {
            if (Kiosco.Activo)
            {
                return;
            }

            lblError.Text = "";
            string archivo;
            using (OpenFileDialog dialogo = new OpenFileDialog())
            {
                dialogo.Title = "Elige una imagen para tu clase";
                dialogo.Filter = "Imágenes (*.png; *.jpg; *.jpeg; *.bmp; *.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                dialogo.CheckFileExists = true;
                dialogo.Multiselect = false;
                dialogo.RestoreDirectory = true;
                // Su dueno es este dialogo (TopMost en el kiosco): sale encima.
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                archivo = dialogo.FileName;
            }

            string motivo;
            Bitmap abierta;
            Cursor = Cursors.WaitCursor;
            try
            {
                abierta = ImagenDeClase.Abrir(archivo, out motivo);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (abierta == null)
            {
                lblError.Text = motivo ?? ImagenDeClase.NoSeAbre;
                return;
            }

            PonerImagen(abierta);
        }

        private void PegarImagen()
        {
            lblError.Text = "";
            string motivo;
            Bitmap pegada;
            Cursor = Cursors.WaitCursor;
            try
            {
                pegada = ImagenDeClase.DelPortapapeles(out motivo);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (pegada == null)
            {
                lblError.Text = motivo ?? ImagenDeClase.SinImagenEnPortapapeles;
                return;
            }

            PonerImagen(pegada);
        }

        private void PonerImagen(Bitmap nueva)
        {
            Bitmap anterior = fuente;
            fuente = nueva;
            imagenNueva = true;
            imagenQuitada = false;
            PonerZoomSinMarcar(100);
            vista.Fuente = nueva;
            if (anterior != null)
            {
                anterior.Dispose();
            }

            Actualizar();
        }

        private void QuitarImagen()
        {
            lblError.Text = "";
            Bitmap anterior = fuente;
            fuente = null;
            imagenNueva = false;
            imagenQuitada = teniaImagen;
            PonerZoomSinMarcar(100);
            vista.Fuente = null;
            if (anterior != null)
            {
                anterior.Dispose();
            }

            Actualizar();
        }

        private void PonerZoomSinMarcar(int valor)
        {
            poniendoZoom = true;
            try
            {
                barraAcercar.Value = valor;
            }
            finally
            {
                poniendoZoom = false;
            }
        }

        // =====================================================================
        // El color
        // =====================================================================

        private MuestraColor AgregarMuestra(Color? color, bool otro, string texto)
        {
            MuestraColor muestra = new MuestraColor(color, otro, texto) { Margin = new Padding(0, 0, 4, 4) };
            muestra.Click += (s, e) => ElegirMuestra(muestra);
            muestras.Add(muestra);
            paleta.Controls.Add(muestra);
            return muestra;
        }

        private void ElegirMuestra(MuestraColor muestra)
        {
            lblError.Text = "";
            if (muestra.EsOtro)
            {
                bool yaElegida = muestra.Checked;
                if (personalizado.HasValue && (!yaElegida || muestra.PorFoco))
                {
                    // Ya tiene un color propio: un clic (o llegar con las flechas) lo
                    // vuelve a elegir. Para cambiarlo, otro clic (abre el selector).
                    elegido = personalizado;
                }
                else if (muestra.PorFoco)
                {
                    // Llegar con las flechas no abre el selector de Windows: Espacio si.
                    return;
                }
                else
                {
                    using (ColorDialog dialogo = new ColorDialog { FullOpen = true, AnyColor = true, Color = personalizado ?? ColorActual })
                    {
                        if (personalizado.HasValue)
                        {
                            dialogo.CustomColors = new[] { ColorTranslator.ToWin32(personalizado.Value) };
                        }

                        if (dialogo.ShowDialog(this) != DialogResult.OK)
                        {
                            return;
                        }

                        Color pedido = Color.FromArgb(dialogo.Color.R, dialogo.Color.G, dialogo.Color.B);
                        Color usable = ColoresProfesor.ParaTextoNegro(pedido);
                        aclarado = usable.ToArgb() != pedido.ToArgb();
                        if (ColoresProfesor.Nombre(usable) != null)
                        {
                            // Es uno de la paleta: se elige su muestra, sin duplicarlo en "Otro".
                            elegido = usable;
                            aclarado = false;
                        }
                        else
                        {
                            personalizado = usable;
                            muestra.Color = usable;
                            elegido = usable;
                        }
                    }
                }
            }
            else
            {
                aclarado = false;
                elegido = muestra.Color;
            }

            Actualizar();
        }

        // =====================================================================
        // Restablecer y Guardar
        // =====================================================================

        private void Restablecer()
        {
            lblError.Text = "";
            elegido = null;
            aclarado = false;
            txtNombre.Text = "";
            if (HayImagen)
            {
                QuitarImagen();
            }

            Actualizar();
        }

        /// <summary>Se ve una imagen, o habia una guardada que aun no se quito (aunque no se pudiera ensenar).</summary>
        private bool HayImagen
        {
            get { return fuente != null || (teniaImagen && !imagenQuitada); }
        }

        private void Guardar()
        {
            lblError.Text = "";
            bool quito;
            string nombre = NombreLimpio(out quito);
            bool todaLaMateria = TodaLaMateria;
            CambioImagen cambioImagen = imagenNueva && fuente != null ? CambioImagen.Nueva
                : imagenQuitada ? CambioImagen.Quitar : CambioImagen.Igual;
            string color = elegido.HasValue ? ColoresProfesor.ATexto(elegido.Value) : null;
            // Solo lo que el profesor toco: si no, Guardar con la casilla marcada (o Enter) borraria
            // en silencio lo propio de sus otros grupos de la materia.
            bool nadaCambio = color == clase.ColorElegido && nombre == clase.NombreCorto && cambioImagen == CambioImagen.Igual
                && todaLaMateria == todaAlAbrir;
            if (nadaCambio)
            {
                Guardado = false;
                DialogResult = DialogResult.OK;
                return;
            }

            PedidoPersonal pedido = new PedidoPersonal
            {
                MateriaId = clase.MateriaId,
                GrupoId = clase.GrupoId,
                TodaLaMateria = todaLaMateria,
                Color = color,
                NombreCorto = nombre,
                Imagen = cambioImagen
            };

            if (cambioImagen == CambioImagen.Nueva)
            {
                string motivoImagen;
                pedido.Bytes = ImagenDeClase.Recortar(fuente, vista.Cuadrado, out motivoImagen);
                if (pedido.Bytes == null)
                {
                    lblError.Text = motivoImagen;
                    return;
                }
            }

            string motivo;
            LecturaPersonal despues = null;
            btnGuardar.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                motivo = new PersonalizacionClasesDatos().Guardar(pedido, out despues);
            }
            catch (AccionNoPermitida ex)
            {
                motivo = ex.Message;
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro.
                Log.Error("Guardar la personalizacion de la clase " + clase.ClaseId, ex);
                motivo = MensajeDeFallo.Explicar("No se pudo guardar", ex, AvisoDeFalloDocente.SiSigue);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnGuardar.Enabled = true;
            }

            if (motivo != null)
            {
                lblError.Text = motivo;
                return;
            }

            Dictionary<string, byte[]> nuevas = null;
            if (pedido.Sha1Guardada != null)
            {
                nuevas = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { { pedido.Sha1Guardada, pedido.Bytes } };
            }

            if (despues != null)
            {
                PersonalizacionClases.Publicar(despues, nuevas, PersonalizacionClases.Version);
            }
            else if (nuevas != null)
            {
                // No se pudo volver a leer: la imagen queda en memoria y el panel
                // vera lo demas en su siguiente lectura.
                PersonalizacionClases.Recordar(pedido.Sha1Guardada, pedido.Bytes);
            }

            Guardado = true;
            DialogResult = DialogResult.OK;
        }

        // =====================================================================
        // Lo que se ve
        // =====================================================================

        /// <summary>Pone al dia la vista, la paleta, las notas y la muestra con lo elegido.</summary>
        private void Actualizar()
        {
            bool quito;
            string nombre = NombreLimpio(out quito);
            Color color = ColorActual;
            bool conImagen = fuente != null;

            vista.ColorClase = color;
            vista.Texto = CirculoClase.TextoDelCirculo(clase.Materia, nombre);
            lblNotaVista.Text = conImagen ? "Arrastra para encuadrar." : "Sin imagen: van tus iniciales sobre el color.";
            lblAcercar.Visible = conImagen;
            barraAcercar.Visible = conImagen;
            btnQuitar.Enabled = HayImagen;
            ayuda.SetToolTip(vista, conImagen
                ? "Arrastra para encuadrar; la rueda o + y - acercan, las flechas mueven e Inicio centra."
                : "Así se verá el círculo de tu clase.");

            ActualizarPaleta();
            ActualizarNotaColor();

            lblNotaNombre.Text = NotaNombre + (quito ? NotaQuitados : "");

            ActualizarNotaAlcance();
            btnRestablecer.Enabled = elegido.HasValue || nombre != null || HayImagen;
            vista.Invalidate();
            tarjeta.Invalidate();
        }

        /// <summary>Las otras clases de este ciclo, las que no cambian al guardar.</summary>
        private List<AspectoClase> Otras()
        {
            bool todaLaMateria = TodaLaMateria;
            return PersonalizacionClases.Actuales()
                .Where(a => todaLaMateria ? a.MateriaId != clase.MateriaId : !(a.MateriaId == clase.MateriaId && a.GrupoId == clase.GrupoId))
                .ToList();
        }

        private void ActualizarPaleta()
        {
            List<AspectoClase> otras = Otras();
            foreach (MuestraColor muestra in muestras)
            {
                muestra.Checked = muestra.EsOtro
                    ? elegido.HasValue && personalizado.HasValue && elegido.Value.ToArgb() == personalizado.Value.ToArgb()
                    : muestra.Color.HasValue
                        ? elegido.HasValue && elegido.Value.ToArgb() == muestra.Color.Value.ToArgb()
                        : !elegido.HasValue;
                // Solo la marcada entra con Tab; las flechas recorren las demas.
                muestra.TabStop = muestra.Checked;

                if (muestra == muestraAutomatico)
                {
                    muestra.Automatico = clase.ColorAutomatico;
                }

                List<AspectoClase> usan = muestra.Color.HasValue
                    ? otras.Where(a => a.Color.ToArgb() == ColoresProfesor.ParaTextoNegro(muestra.Color.Value).ToArgb()).ToList()
                    : new List<AspectoClase>();
                muestra.Usado = usan.Count > 0;

                string que = muestra == muestraAutomatico
                    ? "Automático: el que le toca a esta materia (" + (ColoresProfesor.Nombre(clase.ColorAutomatico) ?? "un color propio") + ")"
                    : muestra.EsOtro
                        ? (personalizado.HasValue ? "Otro color: el tuyo (otro clic para cambiarlo)" : "Otro color...")
                        : muestra.Text;
                ayuda.SetToolTip(muestra, usan.Count > 0 ? que + ". Ya lo usa " + TusClases(usan) + "." : que);
            }
        }

        private void ActualizarNotaColor()
        {
            List<AspectoClase> otras = Otras();
            Color color = ColorActual;
            string nombreColor = ColoresProfesor.Nombre(elegido ?? clase.ColorAutomatico);

            // A que materias les cambiaria el color (solo si vale para toda la materia:
            // el color de un solo grupo es una excepcion y no mueve a nadie).
            List<KeyValuePair<int, Color>> cambios = TodaLaMateria
                ? PersonalizacionClases.Simular(clase.MateriaId, elegido)
                : new List<KeyValuePair<int, Color>>();
            List<string> frases = cambios
                .Select(c => new { Nombre = NombreDeMateria(c.Key), Color = ColoresProfesor.Nombre(c.Value) ?? "otro color" })
                .Where(c => c.Nombre != null)
                .OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => c.Nombre + " pasará a " + c.Color)
                .ToList();
            string alGuardar = frases.Count == 0 ? "" : " Al guardar, " + EstiloAdmin.Enumerar(frases) + ".";

            // Quien saldria igual despues de guardar (los desplazados ya no).
            Dictionary<int, Color> nuevos = cambios.ToDictionary(c => c.Key, c => c.Value);
            List<AspectoClase> usan = otras.Where(a =>
            {
                Color nuevo;
                Color despues = a.ColorElegido == null && !a.DeSuGrupo && nuevos.TryGetValue(a.MateriaId, out nuevo)
                    ? ColoresProfesor.ParaTextoNegro(nuevo) : a.Color;
                return despues.ToArgb() == color.ToArgb();
            }).ToList();

            if (!elegido.HasValue)
            {
                lblNotaColor.ForeColor = frases.Count > 0 ? Azul : Gris;
                lblNotaColor.Text = TodaLaMateria
                    ? "Automático: " + (nombreColor ?? "un color propio") + ". Cada materia sale de un color distinto." + alGuardar
                    : "Automático: el de tus otros grupos de " + clase.Materia + " (" + (nombreColor ?? "un color propio") + ").";
            }
            else if (nombreColor == null)
            {
                lblNotaColor.ForeColor = usan.Count > 0 ? Naranja : frases.Count > 0 ? Azul : Gris;
                lblNotaColor.Text = "Color elegido a mano." + (aclarado ? " Se aclaró un poco para que el texto negro se lea." : "")
                    + (usan.Count > 0 ? " Ya lo usa " + TusClases(usan) + ": no se distinguirán por el color." : "") + alGuardar;
            }
            else if (usan.Count > 0)
            {
                lblNotaColor.ForeColor = Naranja;
                lblNotaColor.Text = Capital(nombreColor) + " ya lo usa " + TusClases(usan) + ": no se distinguirán por el color." + alGuardar;
            }
            else if (frases.Count > 0)
            {
                lblNotaColor.ForeColor = Azul;
                lblNotaColor.Text = Capital(nombreColor) + "." + alGuardar;
            }
            else
            {
                lblNotaColor.ForeColor = Gris;
                lblNotaColor.Text = Capital(nombreColor) + ": ninguna otra de tus clases lo usa.";
            }

            ayuda.SetToolTip(lblNotaColor, lblNotaColor.Text);
        }

        private void ActualizarNotaAlcance()
        {
            if (chkTodos == null)
            {
                lblNotaAlcance.ForeColor = Gris;
                lblNotaAlcance.Text = "También en tus clases de " + clase.Materia + " del próximo ciclo.";
                return;
            }

            if (!chkTodos.Checked)
            {
                lblNotaAlcance.ForeColor = Gris;
                lblNotaAlcance.Text = "Solo en el grupo " + clase.Grupo + ", y solo este ciclo: el próximo, el grupo será otro.";
                return;
            }

            List<string> conLaSuya = deLaMateria.Where(a => a.GrupoId != clase.GrupoId && a.DeSuGrupo)
                .Select(a => a.Grupo).Distinct().OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (conLaSuya.Count > 0)
            {
                lblNotaAlcance.ForeColor = Naranja;
                lblNotaAlcance.Text = EstiloAdmin.Enumerar(conLaSuya)
                    + (conLaSuya.Count == 1 ? " tiene la suya: al guardar, también tomará esta." : " tienen la suya: al guardar, también tomarán esta.");
            }
            else
            {
                lblNotaAlcance.ForeColor = Gris;
                lblNotaAlcance.Text = "También en tus clases de " + clase.Materia + " del próximo ciclo.";
            }
        }

        /// <summary>"Asi se vera": una tarjeta de Mis grupos, con la banda del color de la clase.</summary>
        private void PintarTarjeta(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            bool quito;
            string nombre = NombreLimpio(out quito) ?? clase.Materia;
            Color color = ColorActual;
            Rectangle caja = new Rectangle(0, 0, tarjeta.Width - 1, tarjeta.Height - 1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(caja, 10))
            using (Pen borde = new Pen(BordeSuave, 1))
            {
                g.FillPath(Brushes.White, forma);
                CirculoClase.PintarBanda(g, forma, new Rectangle(caja.X, caja.Y, caja.Width + 1, AltoBanda), color);
                g.DrawPath(borde, forma);
            }

            Rectangle circulo = CirculoClase.Rect(caja.X + 8, caja.Y + 6, 34);
            CirculoClase.Dibujar(g, circulo, color, fuente, vista.Cuadrado, CirculoClase.TextoDelCirculo(clase.Materia, NombreLimpio(out quito)), true);
            g.SmoothingMode = SmoothingMode.None;
            const TextFormatFlags Renglon = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int x = caja.X + 50;
            TextRenderer.DrawText(g, clase.Grupo, fuenteGrupo, new Rectangle(x, caja.Y + 5, caja.Right - 8 - x, 22), Color.Black, color, Renglon);
            TextRenderer.DrawText(g, nombre, fuenteMateria, new Rectangle(x, caja.Y + 27, caja.Right - 8 - x, 17), Color.Black, color, Renglon);
        }

        // =====================================================================
        // Ayudantes
        // =====================================================================

        /// <summary>"tu clase Bases de Datos (ISC-5002)" o "tus clases ... y ...".</summary>
        private static string TusClases(IEnumerable<AspectoClase> clases)
        {
            List<string> nombres = clases.Select(a => a.Materia + " (" + a.Grupo + ")").Distinct().ToList();
            if (nombres.Count == 1)
            {
                return "tu clase " + nombres[0];
            }

            return "tus clases " + (nombres.Count > 3
                ? string.Join(", ", nombres.Take(3)) + " y " + (nombres.Count - 3) + " más"
                : EstiloAdmin.Enumerar(nombres));
        }

        private static string NombreDeMateria(int idMateria)
        {
            AspectoClase una = PersonalizacionClases.DeLaMateria(idMateria).OrderByDescending(a => a.Actual).FirstOrDefault();
            return una == null || !PersonalizacionClases.Actuales().Any(a => a.MateriaId == idMateria) ? null : una.Materia;
        }

        private static string Capital(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0]) + texto.Substring(1);
        }

        private static int Medir(string texto, DrawingFont fuente, int ancho)
        {
            return TextRenderer.MeasureText(texto, fuente, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        }

        private static Label Nota(Rectangle zona, DrawingFont fuente, Color color)
        {
            return new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = fuente,
                ForeColor = color,
                Location = zona.Location,
                Size = zona.Size,
                UseMnemonic = false
            };
        }

        /// <summary>Un control que solo se pinta (con doble bufer): la muestra de la tarjeta.</summary>
        private sealed class Lienzo : Control
        {
            public Lienzo()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.Selectable, false);
                AccessibleRole = AccessibleRole.Graphic;
            }
        }

        /// <summary>
        /// Una muestra de la paleta: un RadioButton pintado a mano (como la
        /// casilla de SelectorColorProfesor) para que las flechas recorran el
        /// grupo y MSAA la lea como opcion marcada o no. Su texto no se pinta: es
        /// para Buscar y los lectores de pantalla ("Celeste", "Automatico"...).
        /// AutoCheck esta apagado: la marca la pone el dialogo (asi "Otro color..."
        /// no queda marcado si se cancela el selector).
        /// </summary>
        private sealed class MuestraColor : RadioButton
        {
            private const int Lado = 26;
            private Color? color;
            private Color automatico = System.Drawing.Color.FromArgb(245, 245, 245);
            private bool usado;

            public MuestraColor(Color? color, bool esOtro, string texto)
            {
                this.color = color;
                EsOtro = esOtro;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                AutoCheck = false;
                AutoSize = false;
                Size = new Size(Lado, Lado);
                Cursor = Cursors.Hand;
                Text = texto;
                UseMnemonic = false;
                TabStop = false;
            }

            public bool EsOtro { get; private set; }

            /// <summary>Null: la de automatico, u "Otro color..." sin elegir todavia.</summary>
            public Color? Color
            {
                get { return color; }
                set { color = value; Invalidate(); }
            }

            /// <summary>En "Automatico": el color que le toca.</summary>
            public Color Automatico
            {
                get { return automatico; }
                set { automatico = value; Invalidate(); }
            }

            /// <summary>Ya lo usa otra de sus clases: un triangulo oscuro en la esquina.</summary>
            public bool Usado
            {
                get { return usado; }
                set { usado = value; Invalidate(); }
            }

            /// <summary>
            /// True mientras dura el clic que WinForms da al llegar con las flechas
            /// (o al recibir el foco sin raton ni Tab): ese no abre el selector.
            /// </summary>
            public bool PorFoco { get; private set; }

            protected override void OnEnter(EventArgs e)
            {
                PorFoco = true;
                try
                {
                    base.OnEnter(e);
                }
                finally
                {
                    PorFoco = false;
                }
            }

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

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.White);
                Rectangle zona = new Rectangle(2, 2, Width - 5, Height - 5);
                System.Drawing.Color relleno = color ?? (EsOtro ? System.Drawing.Color.White : automatico);
                using (SolidBrush pincel = new SolidBrush(relleno))
                {
                    g.FillRectangle(pincel, zona);
                }

                using (Pen borde = new Pen(Checked ? System.Drawing.Color.Black : System.Drawing.Color.FromArgb(150, 150, 150), Checked ? 2.5f : 1f))
                {
                    g.DrawRectangle(borde, zona);
                }

                // "A" en la de automatico; "+" en "Otro color..." mientras no tiene color.
                string letra = color.HasValue ? null : EsOtro ? "+" : "A";
                if (letra != null)
                {
                    using (DrawingFont fuente = new DrawingFont("Century Gothic", 9F, FontStyle.Bold))
                    {
                        TextRenderer.DrawText(g, letra, fuente, zona, System.Drawing.Color.FromArgb(60, 60, 60),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }
                }

                if (usado)
                {
                    Point[] esquina = { new Point(zona.Right - 9, zona.Top), new Point(zona.Right, zona.Top), new Point(zona.Right, zona.Top + 9) };
                    using (SolidBrush marca = new SolidBrush(System.Drawing.Color.FromArgb(70, 70, 70)))
                    {
                        g.FillPolygon(marca, esquina);
                    }
                }

                if (Checked && color.HasValue)
                {
                    using (Pen palomita = new Pen(System.Drawing.Color.Black, 2f))
                    {
                        int cx = zona.Left + zona.Width / 2;
                        int cy = zona.Top + zona.Height / 2;
                        g.DrawLines(palomita, new[] { new Point(cx - 5, cy), new Point(cx - 1, cy + 4), new Point(cx + 6, cy - 4) });
                    }
                }

                // El foco del teclado: un marco verde punteado por fuera.
                if (Focused && ShowFocusCues)
                {
                    using (Pen foco = new Pen(System.Drawing.Color.DarkGreen, 1f) { DashStyle = DashStyle.Dot })
                    {
                        g.DrawRectangle(foco, 0, 0, Width - 1, Height - 1);
                    }
                }
            }
        }

        /// <summary>
        /// La vista previa del circulo (120 px y 4 px para el anillo del foco). Con
        /// imagen, se arrastra para moverla y la rueda acerca; con el foco, las
        /// flechas la mueven 4 px, + y - acercan e Inicio la centra. Siempre
        /// "cubre" el circulo: no deja huecos al mover.
        /// </summary>
        private sealed class VistaCirculo : Control
        {
            public const int Diametro = 120;
            private const int Margen = 4;
            private const int PasoTeclado = 4;
            private const int PasoZoom = 10;

            private Bitmap fuente;
            private float zoom = 1f;
            private PointF centro;
            private Point? arrastreDesde;
            private PointF centroAlEmpezar;
            private Color colorClase = System.Drawing.Color.White;
            private string texto = "";

            public VistaCirculo()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                Size = new Size(Diametro + 2 * Margen, Diametro + 2 * Margen);
                BackColor = System.Drawing.Color.White;
                AccessibleName = "Vista previa del círculo";
                AccessibleRole = AccessibleRole.Graphic;
                TabStop = false;
            }

            /// <summary>El usuario movio o acerco la imagen.</summary>
            public event Action EncuadreCambiado;

            /// <summary>La rueda o + y -: cuanto acercar (en %); el dialogo lo pasa a la barra.</summary>
            public event Action<int> PedirZoom;

            /// <summary>La imagen que se encuadra (del dialogo, que la libera), o null.</summary>
            public Bitmap Fuente
            {
                get
                {
                    return fuente;
                }

                set
                {
                    fuente = value;
                    zoom = 1f;
                    centro = fuente == null ? PointF.Empty : new PointF(fuente.Width / 2f, fuente.Height / 2f);
                    arrastreDesde = null;
                    Cursor = fuente == null ? Cursors.Default : Cursors.SizeAll;
                    TabStop = fuente != null;
                    Invalidate();
                }
            }

            public System.Drawing.Color ColorClase
            {
                get { return colorClase; }
                set { colorClase = value; Invalidate(); }
            }

            public string Texto
            {
                get { return texto; }
                set { texto = value ?? ""; Invalidate(); }
            }

            /// <summary>De 1 (cubrir) a 4.</summary>
            public float Zoom
            {
                get
                {
                    return zoom;
                }

                set
                {
                    zoom = Math.Max(1f, Math.Min(4f, value));
                    Ajustar();
                    Invalidate();
                }
            }

            /// <summary>El cuadrado de la imagen que se ve en el circulo (vacio sin imagen).</summary>
            public RectangleF Cuadrado
            {
                get
                {
                    if (fuente == null)
                    {
                        return RectangleF.Empty;
                    }

                    float lado = Math.Min(fuente.Width, fuente.Height) / zoom;
                    return new RectangleF(centro.X - lado / 2f, centro.Y - lado / 2f, lado, lado);
                }
            }

            private Rectangle Circulo
            {
                get { return new Rectangle(Margen, Margen, Diametro, Diametro); }
            }

            /// <summary>Que el cuadrado no se salga de la imagen.</summary>
            private void Ajustar()
            {
                if (fuente == null)
                {
                    return;
                }

                float mitad = Math.Min(fuente.Width, fuente.Height) / zoom / 2f;
                centro = new PointF(
                    Math.Max(mitad, Math.Min(fuente.Width - mitad, centro.X)),
                    Math.Max(mitad, Math.Min(fuente.Height - mitad, centro.Y)));
            }

            private void Mover(float dx, float dy)
            {
                if (fuente == null)
                {
                    return;
                }

                // dx, dy en px de la vista: la imagen va con el raton.
                float escala = Cuadrado.Width / Diametro;
                centro = new PointF(centro.X - dx * escala, centro.Y - dy * escala);
                Ajustar();
                Invalidate();
                Avisar();
            }

            private void Avisar()
            {
                Action aviso = EncuadreCambiado;
                if (aviso != null)
                {
                    aviso();
                }
            }

            private void Acercar(int paso)
            {
                Action<int> pedir = PedirZoom;
                if (fuente != null && pedir != null)
                {
                    pedir(paso);
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (fuente != null && e.Button == MouseButtons.Left)
                {
                    Focus();
                    arrastreDesde = e.Location;
                    centroAlEmpezar = centro;
                    Capture = true;
                }

                base.OnMouseDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (arrastreDesde.HasValue && fuente != null)
                {
                    PointF antes = centro;
                    centro = centroAlEmpezar;
                    float escala = Cuadrado.Width / Diametro;
                    centro = new PointF(centroAlEmpezar.X - (e.X - arrastreDesde.Value.X) * escala,
                        centroAlEmpezar.Y - (e.Y - arrastreDesde.Value.Y) * escala);
                    Ajustar();
                    if (centro != antes)
                    {
                        Invalidate();
                        Avisar();
                    }
                }

                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                arrastreDesde = null;
                Capture = false;
                base.OnMouseUp(e);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                Acercar(e.Delta > 0 ? PasoZoom : -PasoZoom);
                base.OnMouseWheel(e);
            }

            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Left:
                    case Keys.Right:
                    case Keys.Up:
                    case Keys.Down:
                        return fuente != null;
                    default:
                        return base.IsInputKey(keyData);
                }
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (fuente != null)
                {
                    switch (e.KeyCode)
                    {
                        // La flecha mueve la imagen hacia ese lado.
                        case Keys.Left: Mover(-PasoTeclado, 0); e.Handled = true; break;
                        case Keys.Right: Mover(PasoTeclado, 0); e.Handled = true; break;
                        case Keys.Up: Mover(0, -PasoTeclado); e.Handled = true; break;
                        case Keys.Down: Mover(0, PasoTeclado); e.Handled = true; break;
                        case Keys.Add:
                        case Keys.Oemplus: Acercar(PasoZoom); e.Handled = true; break;
                        case Keys.Subtract:
                        case Keys.OemMinus: Acercar(-PasoZoom); e.Handled = true; break;
                        case Keys.Home:
                            centro = new PointF(fuente.Width / 2f, fuente.Height / 2f);
                            Ajustar();
                            Invalidate();
                            Avisar();
                            e.Handled = true;
                            break;
                    }
                }

                base.OnKeyDown(e);
            }

            protected override void OnGotFocus(EventArgs e)
            {
                Invalidate();
                base.OnGotFocus(e);
            }

            protected override void OnLostFocus(EventArgs e)
            {
                arrastreDesde = null;
                Invalidate();
                base.OnLostFocus(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                CirculoClase.Dibujar(e.Graphics, Circulo, colorClase, fuente, Cuadrado, texto, false);
                if (Focused && ShowFocusCues)
                {
                    CirculoClase.PintarAnillo(e.Graphics, Circulo);
                }
            }
        }
    }
}
