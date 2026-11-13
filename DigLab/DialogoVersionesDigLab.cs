using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// Laboratorios > "Versión de DigLab...": publicar en la base el instalador de una
    /// version nueva para que los equipos se actualicen solos (db/29; pedido del
    /// 2026-11-13), y retirarla. Los del laboratorio la instalan cuando nadie los usa
    /// (DigLab.Vigilante); los de administracion y docentes la ofrecen al abrir DigLab.
    /// </summary>
    internal sealed class DialogoVersionesDigLab : Form
    {
        private const int Ancho = 960;
        private const int AnchoLista = 400;
        private static readonly Color Gris = Color.FromArgb(80, 80, 80);

        private readonly DataGridView grid;
        private readonly PanelDetalleVersion detalle;
        private readonly Label lblEstado;
        private readonly Label lblError;
        private readonly ProgressBar barra;
        private readonly Button btnPublicar;
        private readonly Button btnRetirar;
        private readonly Button btnActualizarYa;
        private readonly Button btnCerrar;
        private List<VersionPublicada> versiones = new List<VersionPublicada>();
        private bool publicando;

        public static void Mostrar(IWin32Window dueno)
        {
            if (Permisos.Bloquear(dueno, "Abrir las versiones de DigLab")) return;
            using (DialogoVersionesDigLab dialogo = new DialogoVersionesDigLab())
            {
                dialogo.ShowDialog(dueno);
            }
        }

        private DialogoVersionesDigLab()
        {
            EstiloHorarios.PrepararDialogo(this, "Versión de DigLab", new Size(Ancho, 640));
            Controls.Add(EstiloAdmin.TituloDialogo("Versión de DigLab", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 56),
                Size = new Size(Ancho - 48, 58),
                Text = "Publica aquí el DigLab-Setup.exe de una versión nueva y los equipos se actualizan sin llevarlo en una memoria: "
                    + "los del laboratorio solos, cuando nadie los usa; los de administración y docentes la ofrecen al abrir DigLab. "
                    + "Esta computadora tiene DigLab " + ActualizacionesDatos.Mostrar(ActualizacionesDatos.VersionInstalada) + "."
            });

            // A la izquierda, las versiones; a la derecha, lo que cambia la elegida (pedido del
            // 2026-11-13: "cuando se les da clic, mencionan los cambios de forma presentable y al
            // final por autores y fecha").
            grid = EstiloHorarios.CrearGrid(new Point(24, 122), new Size(AnchoLista, 316));
            grid.Columns.Add("version", "Versión");
            grid.Columns.Add("estado", "Estado");
            grid.Columns.Add("publicada", "Publicada");
            EstiloHorarios.Pesos(grid, 80, 150, 130);
            grid.SelectionChanged += (s, e) => { ActualizarBotones(); MostrarDetalle(); };
            Controls.Add(grid);

            detalle = new PanelDetalleVersion
            {
                Location = new Point(24 + AnchoLista + 16, 122),
                Size = new Size(Ancho - 48 - AnchoLista - 16, 316)
            };
            Controls.Add(detalle);

            barra = new ProgressBar { Location = new Point(24, 446), Size = new Size(Ancho - 48, 16), Maximum = 100, Visible = false };
            Controls.Add(barra);
            lblEstado = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 468),
                Size = new Size(Ancho - 48, 40)
            };
            Controls.Add(lblEstado);
            lblError = EstiloAdmin.LineaError(new Point(24, 510), Ancho - 48, 40);
            Controls.Add(lblError);

            ClientSize = new Size(Ancho, ClientSize.Height + EstiloAdmin.AireBajoBotones);
            int y = ClientSize.Height - 56 - EstiloAdmin.AireBajoBotones;
            btnCerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(Ancho - 24 - 120, y), new Size(120, 40));
            btnCerrar.DialogResult = DialogResult.Cancel;
            CancelButton = btnCerrar;
            const string textoPublicar = "Publicar versión nueva...";
            int anchoPublicar = TextRenderer.MeasureText(textoPublicar, new DrawingFont("Century Gothic", 10F, FontStyle.Bold)).Width + 30;
            btnPublicar = EstiloAdmin.BotonPrincipal(textoPublicar, new Point(btnCerrar.Left - 10 - anchoPublicar, y), new Size(anchoPublicar, 40));
            btnPublicar.Click += (s, e) => Publicar();
            btnRetirar = EstiloAdmin.Peligroso(EstiloHorarios.CrearBoton("Retirar", new Point(24, y), new Size(120, 40)));
            btnRetirar.Click += (s, e) => Retirar();
            // La senal a los equipos de un laboratorio: instalar ya, sin esperar 10 minutos.
            btnActualizarYa = EstiloHorarios.CrearBoton("Actualizar ya un laboratorio...", new Point(btnRetirar.Right + 10, y), new Size(250, 40));
            btnActualizarYa.Click += (s, e) => ActualizarYa();
            Controls.AddRange(new Control[] { btnRetirar, btnActualizarYa, btnPublicar, btnCerrar });
            AcceptButton = btnPublicar;
            ActiveControl = btnPublicar;

            FormClosing += (s, e) =>
            {
                // A media publicacion no se cierra: se veria que termino sin que termine.
                if (publicando)
                {
                    e.Cancel = true;
                }
            };

            Recargar(null);
        }

        private void Recargar(int? elegir)
        {
            grid.Rows.Clear();
            versiones = new List<VersionPublicada>();
            try
            {
                if (!ActualizacionesDatos.Disponibles())
                {
                    btnPublicar.Enabled = false;
                    lblEstado.ForeColor = Color.FromArgb(190, 85, 0);
                    lblEstado.Text = "Para publicar versiones falta aplicar db/29_actualizaciones.sql en la base. "
                        + "Mientras, cada equipo se actualiza como antes, con el instalador en una memoria.";
                    ListaDigLab.PonerTextoVacio(grid, "Falta una actualización de la base (db/29).");
                    ActualizarBotones();
                    return;
                }

                versiones = ActualizacionesDatos.Listar();
            }
            catch (Exception ex)
            {
                lblError.Text = TextoDeFallo("Leer las versiones de DigLab", "No se pudo leer la lista", ex);
                ActualizarBotones();
                return;
            }

            Version vigente = null;
            foreach (VersionPublicada version in versiones)
            {
                if (version.Vigente && (vigente == null || version.Numero > vigente))
                {
                    vigente = version.Numero;
                }
            }

            foreach (VersionPublicada version in versiones)
            {
                string estado = !version.Completa ? "Incompleta"
                    : version.RetiradaEn.HasValue ? "Retirada " + version.RetiradaEn.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                    : version.Numero == vigente ? "La que se instala" : "Anterior";
                int fila = grid.Rows.Add(ActualizacionesDatos.Mostrar(version.Numero), estado,
                    version.PublicadaEn.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
                grid.Rows[fila].Tag = version;
                grid.Rows[fila].Cells["version"].ToolTipText = version.Cambios.Count == 0 ? null : version.CambiosEnUnRenglon;
                if (!version.Vigente)
                {
                    grid.Rows[fila].DefaultCellStyle.ForeColor = Color.FromArgb(140, 140, 140);
                }
            }

            ListaDigLab.PonerTextoVacio(grid, versiones.Count == 0 ? "Todavía no se ha publicado ninguna versión." : null);
            if (elegir.HasValue)
            {
                EstiloHorarios.SeleccionarFila(grid, f => ((VersionPublicada)f.Tag).Id == elegir.Value);
            }

            if (lblEstado.ForeColor != Color.DarkGreen || string.IsNullOrEmpty(lblEstado.Text))
            {
                lblEstado.ForeColor = Gris;
                lblEstado.Text = vigente == null
                    ? "Todavía no hay ninguna versión para instalar: cada equipo sigue con la que tiene."
                    : "Los equipos con conexión que tengan una versión anterior a la " + ActualizacionesDatos.Mostrar(vigente)
                        + " se actualizan: los del laboratorio en menos de 10 minutos, cuando nadie los usa.";
            }

            ActualizarBotones();
            MostrarDetalle();
        }

        /// <summary>La elegida, a la derecha: que cambia, quien la hizo y cuando.</summary>
        private void MostrarDetalle()
        {
            if (detalle == null)
            {
                return;
            }

            VersionPublicada elegida = Elegida();
            if (elegida == null)
            {
                detalle.Mostrar(null, null);
                return;
            }

            Version vigente = null;
            foreach (VersionPublicada version in versiones)
            {
                if (version.Vigente && (vigente == null || version.Numero > vigente))
                {
                    vigente = version.Numero;
                }
            }

            string estado = !elegida.Completa ? "Incompleta: no se ofrece"
                : elegida.RetiradaEn.HasValue ? "Retirada el " + elegida.RetiradaEn.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : elegida.Numero == vigente ? "La que se instala" : "Anterior";
            detalle.Mostrar(elegida, estado);
        }

        private void ActualizarBotones()
        {
            VersionPublicada elegida = Elegida();
            btnRetirar.Enabled = !publicando && elegida != null && !elegida.RetiradaEn.HasValue && elegida.Completa;
            btnActualizarYa.Enabled = !publicando && versiones.Exists(v => v.Vigente);
            btnCerrar.Enabled = !publicando;
            if (publicando)
            {
                btnPublicar.Enabled = false;
            }
        }

        private VersionPublicada Elegida()
        {
            return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as VersionPublicada;
        }

        private void Publicar()
        {
            if (Permisos.Bloquear(this, "Publicar una versión de DigLab")) return;
            lblError.Text = "";
            string ruta;
            using (OpenFileDialog abrir = new OpenFileDialog
            {
                Title = "Elige el DigLab-Setup.exe de la versión nueva",
                Filter = "Instalador de DigLab (*.exe)|*.exe",
                CheckFileExists = true
            })
            {
                if (abrir.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                ruta = abrir.FileName;
            }

            Version version;
            try
            {
                version = ActualizacionesDatos.VersionDelInstalador(ruta);
            }
            catch (Exception ex)
            {
                lblError.Text = TextoDeFallo("Leer el instalador " + ruta, "No se pudo leer ese archivo", ex);
                return;
            }

            // Quien hizo la ultima, para no volver a escribirlo si son los mismos.
            string autores = "";
            foreach (VersionPublicada anterior in versiones)
            {
                if (anterior.Autores.Length > 0)
                {
                    autores = anterior.Autores;
                    break;
                }
            }

            string notas;
            using (DialogoPublicarVersion dialogo = new DialogoPublicarVersion(ruta, version, autores))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                notas = dialogo.Notas;
            }

            publicando = true;
            ActualizarBotones();
            barra.Value = 0;
            barra.Visible = true;
            lblEstado.ForeColor = Gris;
            lblEstado.Text = "Subiendo DigLab " + ActualizacionesDatos.Mostrar(version) + " a la base...";
            Cursor = Cursors.WaitCursor;
            Task.Run(() => ActualizacionesDatos.Publicar(ruta, notas, porciento =>
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        barra.Value = Math.Max(0, Math.Min(100, porciento));
                        lblEstado.Text = "Subiendo DigLab " + ActualizacionesDatos.Mostrar(version) + " a la base... " + porciento + " %";
                    }));
                }
                catch (InvalidOperationException)
                {
                    // La ventana ya no esta.
                }
            })).ContinueWith(tarea =>
            {
                publicando = false;
                Cursor = Cursors.Default;
                barra.Visible = false;
                btnPublicar.Enabled = true;
                if (tarea.IsFaulted)
                {
                    lblEstado.Text = "";
                    lblError.Text = TextoDeFallo("Publicar DigLab " + version, "No se pudo publicar", tarea.Exception.GetBaseException());
                    Recargar(null);
                    return;
                }

                lblEstado.ForeColor = Color.DarkGreen;
                lblEstado.Text = "Publicada: DigLab " + ActualizacionesDatos.Mostrar(version) + ". Los equipos del laboratorio con conexión la instalan "
                    + "en menos de 10 minutos, cuando nadie los usa; los de administración y docentes la ofrecen al abrir DigLab.";
                Recargar(tarea.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void Retirar()
        {
            if (Permisos.Bloquear(this, "Retirar una versión de DigLab")) return;
            VersionPublicada version = Elegida();
            if (version == null)
            {
                return;
            }

            lblError.Text = "";
            if (!DialogoConfirmacion.Preguntar(this, "Retirar versión de DigLab", "¿Retirar DigLab " + ActualizacionesDatos.Mostrar(version.Numero) + "?",
                "Deja de ofrecerse a los equipos que aún no la tienen; los que ya la instalaron se quedan con ella. "
                + "Para regresarlos a una anterior, publica esa con un número de versión más alto.", "Retirar"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                ActualizacionesDatos.Retirar(version.Id);
            }
            catch (Exception ex)
            {
                lblError.Text = TextoDeFallo("Retirar la version de DigLab n.o " + version.Id, "No se pudo retirar", ex);
                Recargar(null);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            lblEstado.ForeColor = Color.DarkGreen;
            lblEstado.Text = "Retirada: DigLab " + ActualizacionesDatos.Mostrar(version.Numero) + ". Ya no se ofrece a los equipos.";
            Recargar(version.Id);
        }

        private void ActualizarYa()
        {
            if (Permisos.Bloquear(this, "Mandar a los equipos actualizar DigLab")) return;
            lblError.Text = "";
            List<KeyValuePair<int, string>> laboratorios;
            try
            {
                laboratorios = ActualizacionesDatos.LaboratoriosHabilitados();
            }
            catch (Exception ex)
            {
                lblError.Text = TextoDeFallo("Leer los laboratorios", "No se pudo leer la lista de laboratorios", ex);
                return;
            }

            using (DialogoActualizarYa dialogo = new DialogoActualizarYa(laboratorios))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                lblEstado.ForeColor = Color.DarkGreen;
                lblEstado.Text = "Listo: los equipos libres de " + (dialogo.IdLaboratorio.HasValue ? dialogo.NombreLaboratorio : "todos los laboratorios")
                    + " se actualizan en el próximo minuto y se reinician; los que estén en uso, al quedar libres.";
            }
        }

        /// <summary>Lo de DigLab (ErrorDeDatos) tal cual; lo demas, con su causa. Todo al log.</summary>
        private static string TextoDeFallo(string contexto, string queNoSePudo, Exception ex)
        {
            if (ex is ErrorDeDatos)
            {
                Log.Aviso(contexto + ": " + ex.Message);
                return ex.Message;
            }

            Log.Error(contexto, ex);
            return AvisoDeFalloAdmin.Texto(queNoSePudo, ex);
        }
    }

    /// <summary>
    /// Lo que se va a publicar, que cambia (un cambio por renglon), quien lo hizo y la
    /// contrasena de quien lo publica. Las dos cosas van en las notas de la version
    /// (VersionPublicada.ArmarNotas): asi no hace falta otra columna en la base.
    /// </summary>
    internal sealed class DialogoPublicarVersion : Form
    {
        private const int Ancho = 640;
        private readonly Version version;
        private readonly TextBox txtNotas;
        private readonly TextBox txtAutores;
        private readonly Label lblError;

        public string Notas { get; private set; }

        public DialogoPublicarVersion(string ruta, Version version, string autores)
        {
            this.version = version;
            string mostrada = ActualizacionesDatos.Mostrar(version);
            EstiloHorarios.PrepararDialogo(this, "Publicar DigLab " + mostrada, new Size(Ancho, 520));
            Controls.Add(EstiloAdmin.TituloDialogo("Publicar DigLab " + mostrada, Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = EstiloAdmin.TextoOscuro,
                Location = new Point(24, 58),
                Size = new Size(Ancho - 48, 24),
                Text = Path.GetFileName(ruta) + " (" + ActualizacionesDatos.Peso(new FileInfo(ruta).Length) + "). Esta computadora tiene la "
                    + ActualizacionesDatos.Mostrar(ActualizacionesDatos.VersionInstalada) + "."
            });

            Controls.Add(EstiloHorarios.CrearEtiqueta("Qué cambia: un cambio por renglón (lo ve quien actualiza)", new Point(24, 94)));
            txtNotas = EstiloHorarios.CrearTexto(new Point(24, 120), Ancho - 48);
            txtNotas.Multiline = true;
            txtNotas.AcceptsReturn = true;
            txtNotas.ScrollBars = ScrollBars.Vertical;
            txtNotas.Height = 150;
            txtNotas.MaxLength = ActualizacionesDatos.LargoNotas;
            txtNotas.AccessibleName = "Qué cambia, un cambio por renglón";
            EstiloHorarios.PonerTextoGuia(txtNotas, "Ej.: Se puede apagar los equipos desde Laboratorios");
            Controls.Add(txtNotas);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Autores (quién hizo los cambios; va al final)", new Point(24, 282)));
            txtAutores = EstiloHorarios.CrearTexto(new Point(24, 308), Ancho - 48);
            txtAutores.MaxLength = 150;
            txtAutores.Text = autores ?? "";
            txtAutores.AccessibleName = "Autores de los cambios";
            EstiloHorarios.PonerTextoGuia(txtAutores, "Nombres de quienes hicieron los cambios");
            Controls.Add(txtAutores);

            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(24, 348),
                Size = new Size(Ancho - 48, 44),
                Text = "Los equipos del laboratorio la instalan solos cuando nadie los usa, y se reinician; los de administración y docentes "
                    + "la ofrecen al abrir DigLab. Para publicarla, se te pedirá tu contraseña."
            });

            lblError = EstiloAdmin.LineaError(new Point(24, 394), Ancho - 48, 40);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Publicar", Guardar);
            ActiveControl = txtNotas;
        }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Publicar una versión de DigLab")) return;
            string notas = VersionPublicada.ArmarNotas(txtNotas.Text, txtAutores.Text);
            if (notas.Length > ActualizacionesDatos.LargoNotas)
            {
                lblError.Text = "Entre los cambios y los autores hay " + notas.Length + " caracteres; caben " + ActualizacionesDatos.LargoNotas + ". Acórtalos.";
                txtNotas.Focus();
                return;
            }

            if (!DialogoContrasenaAdmin.Confirmar(this, "¿Publicar DigLab " + ActualizacionesDatos.Mostrar(version) + "?",
                "Los equipos con conexión se actualizarán a esta versión.", "Publicar", "Publicar una versión de DigLab"))
            {
                return;
            }

            Notas = notas;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>
    /// A la derecha de las versiones: la elegida, presentable. Su numero, su estado, lo que
    /// cambia en una lista con vinetas y, al final, quien la hizo y cuando se publico.
    /// </summary>
    internal sealed class PanelDetalleVersion : Panel
    {
        private static readonly Color Verde = Color.FromArgb(0, 96, 55);
        private static readonly Color Oscuro = Color.FromArgb(40, 44, 42);
        private static readonly Color GrisTexto = Color.FromArgb(105, 110, 107);
        private static readonly Color Borde = Color.FromArgb(205, 222, 210);

        private readonly RichTextBox texto;
        private readonly DrawingFont titulo = new DrawingFont("Century Gothic", 15F, FontStyle.Bold);
        private readonly DrawingFont subtitulo = new DrawingFont("Century Gothic", 9.5F);
        private readonly DrawingFont seccion = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont normal = new DrawingFont("Century Gothic", 10.5F);
        private readonly DrawingFont firma = new DrawingFont("Century Gothic", 10F, FontStyle.Italic);

        public PanelDetalleVersion()
        {
            BackColor = Color.White;
            Padding = new Padding(18, 14, 8, 10);
            texto = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                TabStop = false,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                AccessibleName = "Qué cambia en la versión elegida"
            };
            // De solo lectura se pinta gris: blanco, como el panel.
            texto.BackColor = Color.White;
            Controls.Add(texto);
            Paint += (s, e) =>
            {
                using (Pen pluma = new Pen(Borde))
                {
                    e.Graphics.DrawRectangle(pluma, 0, 0, Width - 1, Height - 1);
                }
            };
            Resize += (s, e) => Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (DrawingFont letra in new[] { titulo, subtitulo, seccion, normal, firma })
                {
                    letra.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public void Mostrar(VersionPublicada version, string estado)
        {
            texto.Clear();
            if (version == null)
            {
                Agregar("Elige una versión para ver qué cambia.", subtitulo, GrisTexto, false);
                return;
            }

            Agregar("DigLab " + ActualizacionesDatos.Mostrar(version.Numero) + "\n", titulo, Verde, false);
            Agregar(estado + " · " + ActualizacionesDatos.Peso(version.Tamano) + "\n\n", subtitulo, GrisTexto, false);
            Agregar("Qué cambia\n", seccion, Oscuro, false);
            List<string> cambios = version.Cambios;
            if (cambios.Count == 0)
            {
                Agregar("No se escribió qué cambia al publicarla.\n", firma, GrisTexto, false);
            }
            else
            {
                foreach (string cambio in cambios)
                {
                    Agregar(cambio.TrimEnd('.') + ".\n", normal, Oscuro, true);
                }
            }

            // Al final, quien y cuando.
            Agregar("\n", normal, Oscuro, false);
            string autores = version.Autores;
            string por = version.PublicadaPor ?? "";
            Agregar((autores.Length > 0 ? "Por " + autores : por.Length > 0 ? "Por " + por : "Sin autores") + "\n", firma, Oscuro, false);
            string fecha = version.PublicadaEn.ToString("dddd d 'de' MMMM 'de' yyyy, HH:mm", CultureInfo.GetCultureInfo("es-MX"));
            Agregar(char.ToUpper(fecha[0]) + fecha.Substring(1)
                + (autores.Length > 0 && por.Length > 0 ? " · la publicó " + por : "") + "\n", subtitulo, GrisTexto, false);

            texto.SelectionStart = 0;
            texto.SelectionLength = 0;
            texto.ScrollToCaret();
        }

        private void Agregar(string parte, DrawingFont letra, Color color, bool vineta)
        {
            texto.SelectionStart = texto.TextLength;
            texto.SelectionLength = 0;
            texto.SelectionFont = letra;
            texto.SelectionColor = color;
            texto.SelectionBullet = vineta;
            texto.SelectionIndent = vineta ? 6 : 0;
            texto.BulletIndent = vineta ? 14 : 0;
            texto.SelectedText = parte;
        }
    }

    /// <summary>
    /// "Actualizar ya un laboratorio...": la senal a los equipos de un laboratorio (o de
    /// todos) para que instalen ya la version vigente. La cumplen los que estan libres, en el
    /// inicio de sesion del kiosco, en menos de un minuto; caduca en 15 minutos.
    /// </summary>
    internal sealed class DialogoActualizarYa : Form
    {
        private const int Ancho = 600;
        private readonly ComboBox cboLaboratorio;
        private readonly List<KeyValuePair<int, string>> laboratorios;
        private readonly Label lblError;

        public int? IdLaboratorio { get; private set; }
        public string NombreLaboratorio { get; private set; }

        public DialogoActualizarYa(List<KeyValuePair<int, string>> laboratorios)
        {
            this.laboratorios = laboratorios;
            EstiloHorarios.PrepararDialogo(this, "Actualizar ya un laboratorio", new Size(Ancho, 330));
            Controls.Add(EstiloAdmin.TituloDialogo("Actualizar ya un laboratorio", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(24, 56),
                Size = new Size(Ancho - 48, 58),
                Text = "Sin esperar los 10 minutos: los equipos libres de ese laboratorio instalan la versión publicada en el próximo minuto "
                    + "y se reinician. Los que tengan a alguien con su sesión no se interrumpen: se actualizan al quedar libres."
            });

            Controls.Add(EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(24, 122)));
            cboLaboratorio = EstiloHorarios.CrearCombo(new Point(24, 146), Ancho - 48);
            cboLaboratorio.DropDownStyle = ComboBoxStyle.DropDownList;
            cboLaboratorio.Items.Add("Todos los laboratorios");
            foreach (KeyValuePair<int, string> laboratorio in laboratorios)
            {
                cboLaboratorio.Items.Add(laboratorio.Value);
            }

            cboLaboratorio.SelectedIndex = laboratorios.Count == 1 ? 1 : 0;
            Controls.Add(cboLaboratorio);

            lblError = EstiloAdmin.LineaError(new Point(24, 186), Ancho - 48, 44);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Mandar", Mandar);
            ActiveControl = cboLaboratorio;
        }

        private void Mandar()
        {
            if (Permisos.Bloquear(this, "Mandar a los equipos actualizar DigLab")) return;
            int indice = cboLaboratorio.SelectedIndex;
            int? id = indice > 0 ? laboratorios[indice - 1].Key : (int?)null;
            string nombre = indice > 0 ? laboratorios[indice - 1].Value : "todos los laboratorios";
            if (!DialogoContrasenaAdmin.Confirmar(this, "¿Actualizar ya " + (id.HasValue ? "los equipos de " + nombre : "todos los laboratorios") + "?",
                "Los equipos libres se reinician en el próximo minuto para instalar la versión nueva.", "Mandar",
                "Mandar a los equipos actualizar DigLab"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                ActualizacionesDatos.MandarActualizarYa(id, nombre);
            }
            catch (ErrorDeDatos ex)
            {
                Log.Aviso("Mandar actualizar ya: " + ex.Message);
                lblError.Text = ex.Message;
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Mandar actualizar ya", ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo mandar la señal", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            IdLaboratorio = id;
            NombreLaboratorio = nombre;
            DialogResult = DialogResult.OK;
        }
    }
}
