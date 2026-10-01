using DigBit.conexion;
using DigBit.Infraestructura;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.tool.xml;
using System;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Quien registro su bitacora en una clase concreta. La abre el profesor al
    /// pulsar una clase de su horario (BitacoraMisClases) y el administrador
    /// desde sus pantallas. Se refresca sola mientras dura la clase, lleva la
    /// cuenta atras hasta la hora de salida y descarga el PDF de esa sesion.
    /// </summary>
    public class BitacoraSesion : Form
    {
        // Mismos tonos que el resto: claro para la bitacora que reporta algo,
        // intenso para la celda con la falla.
        private static readonly Color ColorConFalla = Color.FromArgb(255, 240, 214);
        private static readonly Color ColorFallaCelda = Color.FromArgb(255, 176, 79);
        private const int SegundosEntreRefrescos = 10;

        private readonly Consultas consultas = new Consultas();
        private readonly int? idFranja;
        private readonly int? idExcepcion;
        private readonly string descripcionClase;
        private readonly string descripcionHorario;
        private readonly string nombreProfesor;
        private readonly DateTime? fecha;

        private int idSesion;
        private VentanaCodigo ventana;
        private DateTime finEnRelojLocal;
        private int ticks;
        private Timer temporizador;

        private Label lblClase;
        private Label lblHorario;
        private Label lblRestante;
        private DataGridView dgvAlumnos;
        private Label lblCantidad;
        private Panel pnlLeyenda;
        private Label lblLeyenda;
        private Button btnActualizar;
        private Button btnDescargar;
        private Button btnCerrar;

        /// <summary>
        /// Se recibe la franja o la excepcion, no la sesion: si al abrir todavia
        /// nadie ha registrado, la ventana la vuelve a buscar en cada refresco y
        /// se llena sola cuando el primer alumno entra. Con fecha, la sesion de la
        /// franja ese dia (la clase de hoy); sin ella, la mas reciente.
        /// </summary>
        public BitacoraSesion(int? idFranja, int? idExcepcion, string descripcionClase, string descripcionHorario, string nombreProfesor,
            DateTime? fecha = null)
        {
            this.idFranja = idFranja;
            this.idExcepcion = idExcepcion;
            this.descripcionClase = descripcionClase ?? "";
            this.descripcionHorario = descripcionHorario ?? "";
            this.nombreProfesor = nombreProfesor ?? "";
            this.fecha = fecha;

            InicializarComponentes();
            Refrescar();
        }

        private void InicializarComponentes()
        {
            BackColor = Color.White;
            System.Drawing.Rectangle area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1060, area.Width - 80), Math.Min(600, area.Height - 80));
            MinimumSize = new Size(Math.Min(860, area.Width - 40), Math.Min(480, area.Height - 40));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Text = "Bitacora de la clase";

            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;

            lblClase = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 15F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Size = new Size(ancho - 48, 28),
                Text = descripcionClase
            };

            lblHorario = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(26, 48),
                Size = new Size(ancho - 52, 20),
                Text = descripcionHorario
            };

            lblRestante = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(190, 90, 0),
                Location = new Point(26, 72),
                Size = new Size(ancho - 52, 22),
                Text = ""
            };

            dgvAlumnos = new DataGridView
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(24, 102),
                Size = new Size(ancho - 48, alto - 162),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false
            };
            dgvAlumnos.ColumnHeadersDefaultCellStyle.Font = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
            dgvAlumnos.ColumnHeadersDefaultCellStyle.ForeColor = Color.DarkGreen;
            dgvAlumnos.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 248, 240);
            dgvAlumnos.DefaultCellStyle.Font = new DrawingFont("Century Gothic", 9F);
            dgvAlumnos.CellFormatting += dgvAlumnos_CellFormatting;

            lblCantidad = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(26, alto - 52),
                Text = ""
            };

            pnlLeyenda = new Panel
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = ColorFallaCelda,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(26, alto - 28),
                Size = new Size(14, 14)
            };

            lblLeyenda = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 8.25F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(45, alto - 29),
                Text = "Naranja: la falla reportada"
            };

            btnActualizar = CrearBoton("Actualizar", new Point(ancho - 344, alto - 44), new Size(100, 34));
            btnActualizar.Click += (s, e) => Refrescar();

            btnDescargar = CrearBoton("Descargar PDF", new Point(ancho - 234, alto - 44), new Size(130, 34));
            btnDescargar.Click += (s, e) => DescargarPdf();

            btnCerrar = CrearBoton("Cerrar", new Point(ancho - 94, alto - 44), new Size(70, 34));
            btnCerrar.Click += (s, e) => Close();

            foreach (Button boton in new[] { btnActualizar, btnDescargar, btnCerrar })
            {
                boton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            }

            Controls.Add(lblClase);
            Controls.Add(lblHorario);
            Controls.Add(lblRestante);
            Controls.Add(dgvAlumnos);
            Controls.Add(lblCantidad);
            Controls.Add(pnlLeyenda);
            Controls.Add(lblLeyenda);
            Controls.Add(btnActualizar);
            Controls.Add(btnDescargar);
            Controls.Add(btnCerrar);

            CancelButton = btnCerrar;

            temporizador = new Timer { Interval = 1000 };
            temporizador.Tick += Temporizador_Tick;
            temporizador.Start();
        }

        private Button CrearBoton(string texto, Point posicion, Size tamaño)
        {
            Button boton = new Button
            {
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = posicion,
                Size = tamaño,
                Text = texto,
                UseVisualStyleBackColor = false
            };
            boton.FlatAppearance.BorderColor = Color.DarkGreen;
            boton.FlatAppearance.BorderSize = 2;
            return boton;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Un Timer de WinForms vive en el bucle de mensajes, no en el
            // formulario: sin esto seguiria consultando la base tras cerrar.
            if (temporizador != null)
            {
                temporizador.Stop();
                temporizador.Dispose();
                temporizador = null;
            }

            base.OnFormClosed(e);
        }

        // =====================================================================
        // Datos
        // =====================================================================

        private void Refrescar()
        {
            try
            {
                if (idSesion == 0)
                {
                    // Aun no hay sesion: se busca otra vez, por si el primer
                    // alumno acaba de entrar.
                    idSesion = idFranja.HasValue
                        ? (fecha.HasValue ? consultas.ObtenerIdSesionDeFranja(idFranja.Value, fecha.Value) : consultas.ObtenerIdSesionDeFranja(idFranja.Value))
                        : idExcepcion.HasValue ? consultas.ObtenerIdSesionDeExcepcion(idExcepcion.Value) : 0;

                    if (idSesion != 0)
                    {
                        ventana = consultas.ObtenerVentanaSesion(idSesion);
                        if (ventana != null)
                        {
                            // La ventana se lee UNA vez con el reloj del servidor y
                            // se traslada al reloj local: el temporizador no vuelve
                            // a consultar la base.
                            finEnRelojLocal = ventana.FinEnRelojLocal;
                            lblHorario.Text = descripcionHorario + "  ·  sesion del " + ventana.Inicio.ToString("dd/MM/yyyy");
                        }
                    }
                }

                if (idSesion == 0)
                {
                    dgvAlumnos.DataSource = null;
                    lblRestante.Text = "Todavia nadie ha registrado su bitacora en esta clase.";
                    lblCantidad.Text = "";
                    btnDescargar.Enabled = false;
                    return;
                }

                DataTable alumnos = consultas.consultaRegistroSesion(idSesion);
                dgvAlumnos.DataSource = alumnos;
                ConfigurarColumnas();

                int conFalla = alumnos.Rows.Cast<DataRow>().Count(Consultas.ConFalla);
                lblCantidad.Text = "Registrados: " + alumnos.Rows.Count
                    + (conFalla > 0 ? "  ·  con falla reportada: " + conFalla : "  ·  sin fallas reportadas");
                btnDescargar.Enabled = true;
                MostrarTiempoRestante();
            }
            catch (Exception ex)
            {
                Log.Error("BitacoraSesion.Refrescar", ex);
                MessageBox.Show("No se pudo leer la bitacora: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ConfigurarColumnas()
        {
            if (dgvAlumnos.Columns.Count == 0)
            {
                return;
            }

            // Una columna por area en vez de dos (falla y comentario): la celda
            // muestra lo que el alumno escribio, o el tipo de falla si no dejo
            // comentario, y queda vacia si no reporto nada.
            foreach (string oculta in new[] { "fk_codigo_accesos", "fk_usuario", "nombre", "apellido_paterno", "apellido_materno", "con_falla", "sin_conexion" })
            {
                if (dgvAlumnos.Columns.Contains(oculta))
                {
                    dgvAlumnos.Columns[oculta].Visible = false;
                }
            }

            foreach (string area in Consultas.AreasDeFalla)
            {
                dgvAlumnos.Columns["falla_" + area].Visible = false;
            }

            dgvAlumnos.Columns["nombre_completo"].HeaderText = "Alumno";
            dgvAlumnos.Columns["numero_identificador"].HeaderText = "Matricula";
            dgvAlumnos.Columns["numero_computadora"].HeaderText = "Equipo";
            dgvAlumnos.Columns["comentarios_red"].HeaderText = "Red";
            dgvAlumnos.Columns["comentarios_hardware"].HeaderText = "Hardware";
            dgvAlumnos.Columns["comentarios_software"].HeaderText = "Software";

            // La hora de su primer registro: con clases seguidas, dice cuando llego.
            dgvAlumnos.Columns["hora_registro"].HeaderText = "Hora";

            dgvAlumnos.Columns["nombre_completo"].DisplayIndex = 0;
            dgvAlumnos.Columns["numero_identificador"].DisplayIndex = 1;
            dgvAlumnos.Columns["hora_registro"].DisplayIndex = 2;
            dgvAlumnos.Columns["numero_computadora"].DisplayIndex = 3;
            dgvAlumnos.Columns["nombre_completo"].FillWeight = 26;
            dgvAlumnos.Columns["numero_identificador"].FillWeight = 12;
            dgvAlumnos.Columns["hora_registro"].FillWeight = 7;
            dgvAlumnos.Columns["numero_computadora"].FillWeight = 10;
            foreach (string area in Consultas.AreasDeFalla)
            {
                dgvAlumnos.Columns["comentarios_" + area].FillWeight = 17;
            }
        }

        /// <summary>Fila suave si reporto algo y celda intensa en la falla concreta.</summary>
        private void dgvAlumnos_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            DataRowView vista = dgvAlumnos.Rows[e.RowIndex].DataBoundItem as DataRowView;
            if (vista == null)
            {
                return;
            }

            string columna = dgvAlumnos.Columns[e.ColumnIndex].Name;
            string area;
            bool esColumnaDeArea = Consultas.EsColumnaDeFalla(columna, out area);
            if (esColumnaDeArea)
            {
                e.Value = Consultas.ConFallaEn(vista.Row, area) ? Consultas.DetalleDeFalla(vista.Row, area) : "";
                e.FormattingApplied = true;
            }

            if (!Consultas.ConFalla(vista.Row))
            {
                return;
            }

            bool esLaFalla = esColumnaDeArea && Consultas.ConFallaEn(vista.Row, area);
            Color fondo = esLaFalla ? ColorFallaCelda : ColorConFalla;
            e.CellStyle.BackColor = fondo;
            e.CellStyle.SelectionBackColor = ControlPaint.Dark(fondo, 0.12f);
            e.CellStyle.SelectionForeColor = Color.Black;
            if (esLaFalla)
            {
                e.CellStyle.Font = new DrawingFont(dgvAlumnos.Font, FontStyle.Bold);
            }
        }

        private void Temporizador_Tick(object sender, EventArgs e)
        {
            if (temporizador == null || IsDisposed || Disposing)
            {
                return;
            }

            MostrarTiempoRestante();

            // La lista se refresca cada 10 s, no cada segundo: es una consulta y
            // una reconstruccion completa de la tabla.
            ticks++;
            if (ticks % SegundosEntreRefrescos == 0)
            {
                Refrescar();
            }
        }

        private void MostrarTiempoRestante()
        {
            if (ventana == null)
            {
                return;
            }

            TimeSpan restante = finEnRelojLocal - DateTime.Now;
            lblRestante.Text = restante <= TimeSpan.Zero
                ? "La clase termino a las " + ventana.Fin.ToString("HH:mm") + "."
                : "Termina a las " + ventana.Fin.ToString("HH:mm") + "  ·  quedan "
                  + (int)restante.TotalHours + restante.ToString("\\:mm\\:ss");
        }

        // =====================================================================
        // PDF
        // =====================================================================

        private void DescargarPdf()
        {
            if (idSesion == 0)
            {
                MessageBox.Show("Esta clase todavia no tiene registros.", "Sin registros", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Usuario datosPdf = consultas.ConsultarDatosPdfSesion(idSesion);
            if (datosPdf == null)
            {
                MessageBox.Show("No se encontraron datos de la sesion.", "Informacion", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string nombreArchivo = SanitizarNombreArchivo(
                (ventana != null ? ventana.Codigo + "_" + ventana.Inicio.ToString("yyyy-MM-dd") + "_" : "") + nombreProfesor) + ".pdf";

            string rutaCompleta;
            using (SaveFileDialog dialogo = new SaveFileDialog())
            {
                dialogo.Title = "Guardar bitacora PDF";
                dialogo.Filter = "Archivo PDF (*.pdf)|*.pdf";
                dialogo.DefaultExt = "pdf";
                dialogo.FileName = nombreArchivo;
                dialogo.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (dialogo.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                rutaCompleta = dialogo.FileName;
            }

            try
            {
                GenerarPdf(datosPdf, rutaCompleta);
            }
            catch (Exception ex)
            {
                Log.Error("BitacoraSesion.DescargarPdf", ex);
                MessageBox.Show("No se pudo generar el PDF: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show("El PDF se guardo en: " + rutaCompleta, "Informacion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            AbrirArchivo(rutaCompleta);
        }

        private void GenerarPdf(Usuario datosPdf, string rutaCompleta)
        {
            string paginahtml_texto = Properties.Resources.plantilla.ToString();
            paginahtml_texto = paginahtml_texto.Replace("@HORARIODEENTRADA", datosPdf.HoraEntrada);
            paginahtml_texto = paginahtml_texto.Replace("@HORARIODESALIDA", datosPdf.HoraSalida);
            paginahtml_texto = paginahtml_texto.Replace("@PROFESOR", nombreProfesor);
            paginahtml_texto = paginahtml_texto.Replace("@GRUPO", datosPdf.GruposId);
            paginahtml_texto = paginahtml_texto.Replace("@MATERIA", datosPdf.MateriasId);
            paginahtml_texto = paginahtml_texto.Replace("@FECHA", datosPdf.HoraRegistro);

            using (FileStream stream = new FileStream(rutaCompleta, FileMode.Create))
            {
                Document pdfDoc = new Document(PageSize.A4, 25, 25, 25, 25);
                PdfWriter writer = PdfWriter.GetInstance(pdfDoc, stream);
                pdfDoc.Open();
                pdfDoc.AddTitle("Bitacora DigBit");
                pdfDoc.AddAuthor("DigBit");

                iTextSharp.text.Image logoTeschi = iTextSharp.text.Image.GetInstance(Properties.Resources.logoteschi, System.Drawing.Imaging.ImageFormat.Png);
                logoTeschi.ScaleToFit(100, 60);
                logoTeschi.Alignment = iTextSharp.text.Image.UNDERLYING;
                logoTeschi.SetAbsolutePosition(470, 750);
                pdfDoc.Add(logoTeschi);

                iTextSharp.text.Image logoEDOMEX = iTextSharp.text.Image.GetInstance(Properties.Resources.estadoMexico, System.Drawing.Imaging.ImageFormat.Png);
                logoEDOMEX.ScaleToFit(100, 60);
                logoEDOMEX.Alignment = iTextSharp.text.Image.UNDERLYING;
                logoEDOMEX.SetAbsolutePosition(40, 750);
                pdfDoc.Add(logoEDOMEX);

                using (StringReader sr = new StringReader(paginahtml_texto))
                {
                    XMLWorkerHelper.GetInstance().ParseXHtml(writer, pdfDoc, sr);
                }

                TablaAlumnosPdf.Agregar(pdfDoc, consultas.consultaRegistroSesion(idSesion));
                pdfDoc.Close();
            }
        }

        private static string SanitizarNombreArchivo(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return "Bitacora";
            }

            foreach (char invalido in Path.GetInvalidFileNameChars())
            {
                nombre = nombre.Replace(invalido, '_');
            }

            return nombre;
        }

        private void AbrirArchivo(string ruta)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = ruta, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("El PDF se guardo, pero no se pudo abrir: " + ex.Message,
                    "Vista previa no disponible", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
