using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigLab
{
    /// <summary>
    /// Lo comun a las pantallas de lista del administrador (Alumnos, Profesores,
    /// Laboratorios y Encargados de laboratorio): titulo, boton principal arriba
    /// a la derecha, la lista, dos lineas de estado abajo a la izquierda y los
    /// botones de accion abajo a la derecha. Cada pantalla pone sus filtros, sus
    /// columnas y sus acciones. En solo consulta (Permisos) la barra de titulo lo
    /// dice, y cada pantalla no crea los botones que escriben.
    /// </summary>
    public abstract class PantallaAdmin : Form
    {
        protected const int Margen = 30;

        protected readonly ToolTip ayuda = new ToolTip();
        protected DataGridView grid;
        protected Label lblResumen;

        private readonly Label lblAviso;
        private readonly Timer esperaAviso;
        private readonly Timer esperaBusqueda;
        private string notaFija = "";
        private int derechaAbajo;
        private Button botonMasALaIzquierda;

        protected PantallaAdmin(string titulo, string subtitulo, Size tamano)
        {
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.Sizable;
            // Sin pasarse del area de trabajo de la pantalla (sin la barra de tareas):
            // a 1366x768 Bitacoras (780 px de alto) dejaba sus botones de abajo
            // tapados hasta maximizarla. La lista y los botones se acomodan solos.
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size marco = SizeFromClientSize(Size.Empty);
            ClientSize = new Size(Math.Min(tamano.Width, area.Width - marco.Width), Math.Min(tamano.Height, area.Height - marco.Height));
            MinimumSize = new Size(Math.Min(1000, tamano.Width), 560);
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            // El icono de DigLab en la barra de todas (Estadisticas, Alertas, Bitacoras,
            // Estudiantes...): el del panel del administrador, como Mi horario y Mis grupos.
            Icon = (Icon)new System.ComponentModel.ComponentResourceManager(typeof(PrincipalAdministrador)).GetObject("$this.Icon");
            // Solo la barra de titulo ("Alumnos (solo consulta)"); el titulo grande no cambia.
            Text = titulo + (Permisos.SoloConsulta ? " (solo consulta)" : "");

            // Titulo en el verde del TESCHI y, debajo, el subtitulo en gris (como el
            // inicio de sesion): se distingue de un vistazo que es cada cosa.
            Label lblTituloGrande = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = EstiloHorarios.VerdeTeschi,
                Location = new Point(28, 14),
                Text = titulo
            };
            Controls.Add(lblTituloGrande);
            Controls.Add(new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = EstiloAdmin.TextoSecundario,
                Location = new Point(30, 52),
                Text = subtitulo
            });

            // Solo consulta: tambien junto al titulo grande, como en Horarios (la barra de
            // titulo maximizada casi no se lee). Al pulsarla, que puede hacer aqui. Despues
            // del titulo y el subtitulo: FichaDocente y Estadisticas los buscan en ese orden.
            if (Permisos.SoloConsulta)
            {
                Label soloConsulta = EstiloAdmin.EtiquetaSoloConsulta(ayuda);
                Action seguir = () => soloConsulta.Location = new Point(lblTituloGrande.Right + 8, lblTituloGrande.Top + 10);
                lblTituloGrande.LocationChanged += (s, e) => seguir();
                lblTituloGrande.SizeChanged += (s, e) => seguir();
                seguir();
                Controls.Add(soloConsulta);
            }

            lblResumen = LineaDeEstado(ClientSize.Height - 62, new DrawingFont("Century Gothic", 10F, FontStyle.Bold));
            lblResumen.ForeColor = EstiloAdmin.TextoOscuro;
            lblResumen.Text = "Cargando...";
            lblAviso = LineaDeEstado(ClientSize.Height - 38, new DrawingFont("Century Gothic", 9.5F));

            derechaAbajo = ClientSize.Width - Margen;
            esperaAviso = new Timer { Interval = 8000 };
            esperaAviso.Tick += (s, e) => { esperaAviso.Stop(); MostrarNotaFija(); };
            esperaBusqueda = new Timer { Interval = 250 };
            esperaBusqueda.Tick += (s, e) => { esperaBusqueda.Stop(); AlBuscar(); };
            Resize += (s, e) => AjustarLineasDeEstado();
        }

        // ---------------------------------------------------------------------
        // Piezas que usan las pantallas
        // ---------------------------------------------------------------------

        /// <summary>El boton de la accion principal ("Nuevo ..."), arriba a la derecha.</summary>
        protected Button BotonPrincipalArriba(string texto, int ancho)
        {
            Button boton = EstiloAdmin.BotonPrincipal(texto, new Point(ClientSize.Width - Margen - ancho, 18), new Size(ancho, 40));
            boton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(boton);
            return boton;
        }

        /// <summary>Un boton abajo a la derecha. Se colocan de derecha a izquierda.</summary>
        protected Button BotonAbajo(string texto, int ancho)
        {
            derechaAbajo -= ancho;
            Button boton = EstiloHorarios.CrearBoton(texto, new Point(derechaAbajo, ClientSize.Height - 58), new Size(ancho, 40));
            boton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(boton);
            derechaAbajo -= 10;
            botonMasALaIzquierda = boton;
            AjustarLineasDeEstado();
            return boton;
        }

        protected void SeparacionAbajo(int pixeles)
        {
            derechaAbajo -= pixeles;
        }

        /// <summary>La lista, desde esa altura hasta encima de los botones.</summary>
        protected DataGridView CrearLista(int arriba)
        {
            // 24 px libres entre la lista y los botones de abajo (antes 18: se veian pegados).
            grid = EstiloHorarios.CrearGrid(new Point(Margen, arriba), new Size(ClientSize.Width - 2 * Margen, ClientSize.Height - arriba - 82));
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.RowTemplate.Height = 34;
            // Un encabezado largo ("No. de empleado") pasa a dos lineas en vez de cortarse.
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            // Con margen arriba y abajo: el encabezado de un renglon se quedaba en
            // unos 22 px (Alertas) y se veia apretado contra la primera fila.
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 7, 8, 7);
            grid.SelectionChanged += (s, e) => ActualizarBotones();
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) AlDobleClic(); };
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    AlDobleClic();
                }
            };
            Controls.Add(grid);
            return grid;
        }

        /// <summary>Caja de busqueda: filtra al dejar de teclear (AlBuscar) y con flecha abajo baja a la lista.</summary>
        protected TextBox CrearBuscador(Point posicion, int ancho)
        {
            Controls.Add(EstiloHorarios.CrearEtiqueta("Buscar", new Point(posicion.X, posicion.Y - 26)));
            TextBox buscador = EstiloHorarios.CrearTexto(posicion, ancho);
            buscador.TextChanged += (s, e) => { esperaBusqueda.Stop(); esperaBusqueda.Start(); };
            buscador.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && grid != null && grid.Rows.Count > 0)
                {
                    grid.Focus();
                    e.Handled = true;
                }
            };
            Controls.Add(buscador);
            return buscador;
        }

        /// <summary>
        /// Vuelve a pintar la lista conservando la seleccion (por clave). Las filas
        /// "apagadas" (dados de baja, desactivados) salen en gris. Si queda una sola
        /// fila, queda elegida: Enter la abre.
        /// </summary>
        protected void LlenarLista<T>(IEnumerable<T> filas, Func<T, object[]> valores, Func<T, bool> apagada, Func<T, string> clave, string seleccionar)
            where T : class
        {
            T antes = Seleccionado<T>();
            string buscada = seleccionar ?? (antes == null ? null : clave(antes));

            grid.SuspendLayout();
            grid.Rows.Clear();
            foreach (T fila in filas)
            {
                int indice = grid.Rows.Add(valores(fila));
                grid.Rows[indice].Tag = fila;
                if (apagada(fila))
                {
                    grid.Rows[indice].DefaultCellStyle.ForeColor = Color.Gray;
                }
            }

            grid.ResumeLayout();
            grid.ClearSelection();
            if (buscada != null)
            {
                EstiloHorarios.SeleccionarFila(grid, f => clave((T)f.Tag) == buscada);
            }

            if (grid.SelectedRows.Count == 0 && grid.Rows.Count == 1)
            {
                grid.Rows[0].Selected = true;
            }

            ActualizarBotones();
        }

        protected T Seleccionado<T>() where T : class
        {
            return grid == null || grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as T;
        }

        /// <summary>Confirmacion de lo que se acaba de hacer; a los 8 s vuelve la nota fija, si la hay.</summary>
        protected void Avisar(string texto)
        {
            lblAviso.ForeColor = Color.DarkGreen;
            lblAviso.Text = texto;
            esperaAviso.Stop();
            esperaAviso.Start();
        }

        /// <summary>Aviso permanente en naranja (por ejemplo, que falta una migracion).</summary>
        protected string NotaFija
        {
            set
            {
                notaFija = value ?? "";
                if (!esperaAviso.Enabled)
                {
                    MostrarNotaFija();
                }
            }
        }

        /// <summary>
        /// mensaje es "No se pudo ..." sin punto: se le anade la causa y que hacer
        /// (MensajeDeFallo); el texto tecnico solo va al log. Quien solo consulta
        /// avisa a la administracion; la administracion tiene el detalle en su log.
        /// </summary>
        protected void MostrarError(string contexto, Exception ex, string mensaje)
        {
            Log.Error(contexto, ex);
            MessageBox.Show(this, MensajeDeFallo.Explicar(mensaje, ex,
                    Permisos.SoloConsulta ? MensajeDeFallo.AvisaALaAdministracion : MensajeDeFallo.RevisaElRegistro),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ---------------------------------------------------------------------
        // Lo que decide cada pantalla
        // ---------------------------------------------------------------------

        /// <summary>Vuelve a leer y deja elegida la fila con esa clave, si se da.</summary>
        protected abstract void Recargar(string seleccionar);

        protected virtual void ActualizarBotones()
        {
        }

        protected virtual void AlDobleClic()
        {
        }

        protected virtual void AlBuscar()
        {
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Cursor = Cursors.WaitCursor;
            try
            {
                Recargar(null);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                esperaAviso.Dispose();
                esperaBusqueda.Dispose();
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }

        private Label LineaDeEstado(int y, DrawingFont fuente)
        {
            Label linea = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = fuente,
                ForeColor = Color.DarkGreen,
                Location = new Point(Margen, y),
                Size = new Size(400, 20),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            Controls.Add(linea);
            return linea;
        }

        /// <summary>Las dos lineas de estado nunca se meten debajo de los botones.</summary>
        private void AjustarLineasDeEstado()
        {
            if (botonMasALaIzquierda == null)
            {
                return;
            }

            int ancho = Math.Max(100, botonMasALaIzquierda.Left - 16 - lblResumen.Left);
            lblResumen.Width = ancho;
            lblAviso.Width = ancho;
        }

        private void MostrarNotaFija()
        {
            lblAviso.ForeColor = Color.FromArgb(190, 85, 0);
            lblAviso.Text = notaFija;
        }
    }

    /// <summary>Lo que las pantallas nuevas del administrador anaden al estilo de Horarios.</summary>
    internal static class EstiloAdmin
    {
        public static readonly Color FondoError = Color.FromArgb(255, 228, 228);

        // Jerarquia del texto (2026-10-31): lo secundario (subtitulos, notas) en
        // gris y las etiquetas y resumenes en gris oscuro; el verde, para titulos.
        public static readonly Color TextoSecundario = Color.FromArgb(95, 99, 96);
        public static readonly Color TextoOscuro = Color.FromArgb(55, 60, 57);

        /// <summary>
        /// Linea fina de LineaSuave para separar grupos dentro de un dialogo (en
        /// vez de juntarlos o de marcarlos con bordes).
        /// </summary>
        public static Label Separador(Point posicion, int ancho)
        {
            return new Label
            {
                AutoSize = false,
                BackColor = EstiloHorarios.LineaSuave,
                Location = posicion,
                Size = new Size(ancho, 1)
            };
        }

        /// <summary>Boton relleno para la accion principal de una pantalla o dialogo.</summary>
        public static Button BotonPrincipal(string texto, Point posicion, Size tamano)
        {
            return ComoPrincipal(EstiloHorarios.CrearBoton(texto, posicion, tamano));
        }

        /// <summary>
        /// Un boton de EstiloHorarios.CrearBoton (o de PantallaAdmin.BotonAbajo) con el
        /// aspecto del principal: relleno verde y letra blanca. Devuelve el mismo boton.
        /// </summary>
        public static Button ComoPrincipal(Button boton)
        {
            BotonesApagados.PonerColores(boton, EstiloHorarios.VerdeTeschi, EstiloHorarios.VerdeTeschi);
            boton.ForeColor = Color.White;
            boton.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 96, 55);
            boton.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 84, 48);
            return boton;
        }

        /// <summary>El rojo de lo que quita algo (dar de baja, terminar una clase, quitar horas).</summary>
        public static readonly Color RojoPeligro = Color.FromArgb(170, 50, 30);

        /// <summary>
        /// El boton de una accion que quita algo, en rojo con letra blanca: tambien al
        /// pasar el raton y al pulsarlo (un rojo mas oscuro, nunca el verde, que se
        /// lee como seguro). Apagado se ve apagado como cualquier principal
        /// (BotonesApagados) y al encenderse vuelve a su rojo. Devuelve el mismo boton.
        /// </summary>
        public static Button Peligroso(Button boton)
        {
            BotonesApagados.PonerColores(boton, RojoPeligro, RojoPeligro);
            boton.ForeColor = Color.White;
            boton.FlatAppearance.MouseOverBackColor = Color.FromArgb(145, 40, 22);
            boton.FlatAppearance.MouseDownBackColor = Color.FromArgb(125, 34, 18);
            return boton;
        }

        /// <summary>
        /// Un codigo de clase como se ensena en las listas y se dicta: en mayusculas
        /// (TarjetaCodigo.Mayusculas). "SM5lu" se leia con una ele que parece un 1 o
        /// una I. Nadie distingue mayusculas al cotejarlo, asi que no cambia nada mas.
        /// </summary>
        public static string Codigo(string codigo)
        {
            return TarjetaCodigo.Mayusculas(codigo);
        }

        /// <summary>
        /// La etiqueta gris "Solo consulta" junto al titulo grande de cada seccion
        /// (PantallaAdmin y Horarios). Se pulsa: abre lo que puede hacer la persona
        /// responsable (Permisos.ExplicarSoloConsulta). Sin posicion: la pone quien la usa.
        /// </summary>
        public static Label EtiquetaSoloConsulta(ToolTip ayuda)
        {
            Label etiqueta = new Label
            {
                AutoSize = true,
                BackColor = Color.FromArgb(240, 240, 240),
                Cursor = Cursors.Hand,
                Font = new DrawingFont("Century Gothic", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(80, 80, 80),
                Padding = new Padding(8, 3, 8, 3),
                Text = "Solo consulta",
                AccessibleRole = AccessibleRole.Link,
                AccessibleDescription = "Qué puedes hacer aquí"
            };
            if (ayuda != null)
            {
                ayuda.SetToolTip(etiqueta, "Qué puedes hacer aquí");
            }

            etiqueta.Click += (s, e) => Permisos.ExplicarSoloConsulta(etiqueta.FindForm());
            return etiqueta;
        }

        /// <summary>Titulo grande dentro de un dialogo.</summary>
        public static Label TituloDialogo(string texto, int ancho)
        {
            return new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 14F, FontStyle.Bold),
                ForeColor = EstiloHorarios.VerdeTeschi,
                Location = new Point(24, 16),
                Size = new Size(ancho, 30),
                Text = texto
            };
        }

        /// <summary>Linea de error en rojo dentro de un dialogo.</summary>
        public static Label LineaError(Point posicion, int ancho, int alto)
        {
            return new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(190, 30, 30),
                Location = posicion,
                Size = new Size(ancho, alto)
            };
        }

        /// <summary>
        /// Lo que BotonesDialogo agrega abajo del dialogo (2026-10-31): los botones
        /// quedan donde estaban respecto a lo de arriba y con 24 px hasta el borde de
        /// abajo, como a los lados (antes 16: se veian pegados al borde).
        /// </summary>
        public const int AireBajoBotones = 8;

        /// <summary>
        /// Guardar (relleno) y Cancelar abajo a la derecha de un dialogo; Enter guarda y
        /// Esc cancela. El dialogo crece AireBajoBotones: los botones van en Height - 64.
        /// Llamarlo cuando el dialogo ya tiene su alto.
        /// </summary>
        public static Button BotonesDialogo(Form dialogo, string textoGuardar, Action guardar)
        {
            dialogo.ClientSize = new Size(dialogo.ClientSize.Width, dialogo.ClientSize.Height + AireBajoBotones);
            int y = dialogo.ClientSize.Height - 40 - 16 - AireBajoBotones;
            int anchoGuardar = Math.Max(130, TextRenderer.MeasureText(textoGuardar, new DrawingFont("Century Gothic", 10F, FontStyle.Bold)).Width + 30);
            Button cancelar = EstiloHorarios.CrearBoton("Cancelar", new Point(dialogo.ClientSize.Width - 24 - 120, y), new Size(120, 40));
            cancelar.DialogResult = DialogResult.Cancel;
            Button hacer = BotonPrincipal(textoGuardar, new Point(cancelar.Left - 10 - anchoGuardar, y), new Size(anchoGuardar, 40));
            hacer.Click += (s, e) => guardar();
            dialogo.Controls.Add(hacer);
            dialogo.Controls.Add(cancelar);
            dialogo.AcceptButton = hacer;
            dialogo.CancelButton = cancelar;
            return hacer;
        }

        /// <summary>Minusculas y sin acentos: "Ramírez" se encuentra buscando "ramirez".</summary>
        public static string Normalizar(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return "";
            }

            StringBuilder limpio = new StringBuilder(texto.Length);
            foreach (char c in texto.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    limpio.Append(c);
                }
            }

            return limpio.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>Todas las palabras de la busqueda aparecen en el texto (ya normalizado), en cualquier orden.</summary>
        public static bool Coincide(string textoNormalizado, string busqueda)
        {
            return Normalizar(busqueda).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(p => textoNormalizado.Contains(p));
        }

        /// <summary>"a", "a y b", "a, b y c".</summary>
        public static string Enumerar(IList<string> partes)
        {
            if (partes.Count == 1)
            {
                return partes[0];
            }

            return string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[partes.Count - 1];
        }

        /// <summary>"13/07/2026" siempre con "/": sin cultura fija el separador saldria de la configuracion de Windows.</summary>
        public static string Fecha(DateTime? fecha)
        {
            return fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—";
        }

        public static string Guion(string texto)
        {
            return string.IsNullOrWhiteSpace(texto) ? "—" : texto;
        }
    }

    /// <summary>
    /// "Solo consulta": lo que puede hacer la persona responsable de laboratorio, en
    /// vinetas cortas (Permisos.QuePuedeHacer): que puede ver, que puede guardar,
    /// donde esta el codigo que le piden y que hace la administracion. Antes era un
    /// MessageBox de tres parrafos. Un solo boton, "Entendido" (Enter y Esc).
    /// </summary>
    internal sealed class DialogoSoloConsulta : Form
    {
        private const int Ancho = 580;
        private const int Sangria = 44;

        public DialogoSoloConsulta(IList<KeyValuePair<string, string>> puntos)
        {
            EstiloHorarios.PrepararDialogo(this, "Solo consulta", new Size(Ancho, 200));
            Icon = (Icon)new System.ComponentModel.ComponentResourceManager(typeof(PrincipalAdministrador)).GetObject("$this.Icon");
            DrawingFont fuenteIntro = new DrawingFont("Century Gothic", 10F);
            DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);
            DrawingFont fuenteTexto = new DrawingFont("Century Gothic", 10F);
            Disposed += (s, e) =>
            {
                fuenteIntro.Dispose();
                fuenteTitulo.Dispose();
                fuenteTexto.Dispose();
            };

            Controls.Add(EstiloAdmin.TituloDialogo("Qué puedes hacer aquí", Ancho - 48));
            const string intro = "Entraste como responsable de laboratorio: puedes verlo todo, pero los cambios los hace la administración de DigLab.";
            Label lblIntro = Parrafo(intro, fuenteIntro, EstiloAdmin.TextoSecundario, 24, 52, Ancho - 48);
            Controls.Add(lblIntro);

            int y = lblIntro.Bottom + 14;
            int anchoTexto = Ancho - Sangria - 24;
            foreach (KeyValuePair<string, string> punto in puntos ?? new List<KeyValuePair<string, string>>())
            {
                // La vineta: un punto verde a la altura del titulo.
                Label vineta = new Label
                {
                    AutoSize = false,
                    BackColor = EstiloHorarios.VerdeTeschi,
                    Location = new Point(28, y + 7),
                    Size = new Size(7, 7)
                };
                Controls.Add(vineta);
                Label titulo = Parrafo(punto.Key, fuenteTitulo, EstiloHorarios.VerdeTeschi, Sangria, y, anchoTexto);
                Controls.Add(titulo);
                Label texto = Parrafo(punto.Value, fuenteTexto, EstiloAdmin.TextoOscuro, Sangria, titulo.Bottom + 2, anchoTexto);
                Controls.Add(texto);
                y = texto.Bottom + 12;
            }

            Button entendido = EstiloAdmin.BotonPrincipal("Entendido", new Point(Ancho - 24 - 130, y + 6), new Size(130, 40));
            entendido.DialogResult = DialogResult.OK;
            Controls.Add(entendido);
            AcceptButton = entendido;
            CancelButton = entendido;
            ClientSize = new Size(Ancho, entendido.Bottom + 18);
            ActiveControl = entendido;
        }

        private static Label Parrafo(string texto, DrawingFont fuente, Color color, int x, int y, int ancho)
        {
            return new Label
            {
                AutoSize = false,
                Font = fuente,
                ForeColor = color,
                UseMnemonic = false,
                Location = new Point(x, y),
                Size = new Size(ancho, PiezasConfiguracion.AltoDe(texto, fuente, ancho) + 2),
                Text = texto
            };
        }
    }

    /// <summary>
    /// Alta y edicion de un alumno, un profesor o un encargado de laboratorio.
    /// Cada campo con su etiqueta encima y los errores dentro del propio dialogo.
    /// Al alumno se le pide su carrera (y el semestre, si se quiere); al
    /// profesor, su codigo de clase: uno para todas sus clases
    /// (db/09_codigo_por_profesor.sql); al encargado, solo lo comun. Tambien es
    /// el "Crear cuenta" del inicio de sesion (CrearCuenta), con los mismos datos.
    /// </summary>
    internal class DialogoUsuario : Form
    {
        // "Crear cuenta": solo matriculas de alumno, que en el TESCHI son 10
        // cifras (las de 26 de los 30 alumnos de la base, 2026-10-04). Asi nadie
        // aparta desde el laboratorio un numero de empleado (TREJON) o un texto.
        private static readonly Regex MatriculaDeAlumno = new Regex("^[0-9]{10}$");
        // El codigo lo teclea el alumno en el laboratorio: letras y numeros.
        private static readonly Regex CodigoValido = new Regex("^[A-Za-z0-9]{3,16}$");
        // La nota de servicio social del encargado, debajo de la primera fila (dos lineas).
        private const int AltoNotaServicioSocial = 40;

        private readonly UsuariosDatos datos;
        private readonly ProfesoresDatos datosProfesores;
        private readonly CatalogosDatos catalogos = new CatalogosDatos();
        private readonly UsuarioInfo existente;
        private readonly int carreraInicial;
        private readonly string nombreIdentificador;
        private readonly bool conCodigo;
        private readonly string codigoOriginal;
        // El propio alumno, desde el inicio de sesion: otros textos y, al crearla,
        // la contrasena para entrar sin volver a escribirla.
        private readonly bool autoregistro;
        private string contrasenaNueva;
        // Crear cuenta con la recuperacion por correo activada: sin correo, se le
        // dice que no podra recuperar su contrasena el solo (la linea naranja).
        private readonly bool avisoSinCorreo;
        private const int AltoAvisoCorreo = 18;
        // Editar a un estudiante que entra con el SIIA: en lugar de la contrasena, una nota.
        private readonly bool contrasenaDelSiia;
        // Tres renglones: la del docente ("...(Docentes, «Enlazar...»).") con 50 se cortaba.
        private const int AltoNotaSiia = 62;
        private Label lblSinCorreo;
        // Crear cuenta con el aviso de privacidad del TESCHI en Ajustes (RNF72): el
        // aviso con desplazamiento y la casilla obligatoria que habilita el boton.
        private readonly AvisoDePrivacidad avisoPrivacidad = AvisoDePrivacidad.Ninguno;
        private const int AltoAvisoPrivacidad = 158;
        private CheckBox chkAvisoLeido;
        private Button btnGuardar;

        private TextBox txtIdentificador;
        private TextBox txtCorreo;
        private TextBox txtNombre;
        private TextBox txtPaterno;
        private TextBox txtMaterno;
        private ComboBox cboCarrera;
        private ComboBox cboSemestre;
        private TextBox txtCodigo;
        private Label lblAvisoCodigo;
        // Solo profesores: el color de sus clases (db/12).
        private SelectorColorProfesor selectorColor;
        private readonly ToolTip ayuda = new ToolTip();
        private TextBox txtContrasena;
        private TextBox txtConfirmar;
        private Label lblError;

        /// <summary>
        /// Sin usuario, un alta. Para alumnos, empieza con la carrera que se esta
        /// filtrando en la lista (0 = ninguna).
        /// </summary>
        public DialogoUsuario(UsuariosDatos datos, UsuarioInfo existente, int carreraInicial)
            : this(datos, existente, carreraInicial, false)
        {
        }

        private DialogoUsuario(UsuariosDatos datos, UsuarioInfo existente, int carreraInicial, bool autoregistro)
        {
            this.datos = datos;
            this.datosProfesores = datos as ProfesoresDatos;
            this.existente = existente;
            this.carreraInicial = carreraInicial;
            this.autoregistro = autoregistro;
            avisoSinCorreo = autoregistro && RecuperacionActiva();
            if (autoregistro)
            {
                avisoPrivacidad = AvisoDePrivacidad.Leer();
            }

            nombreIdentificador = EsAlumno ? "matrícula" : "número de empleado";
            // Quien entra con el SIIA (Ajustes): su contrasena es la del SIIA (en DigLab
            // solo esta la huella para cuando el SIIA no responde): aqui no se pide ni
            // se cambia. Estudiantes al editarlos (no se registran: su cuenta se crea
            // sola); docentes al registrarlos y al editarlos (entran con su numero de
            // empleado, su contrasena y su NIP, y la administracion los enlaza).
            if (!autoregistro && (EsAlumno || EsProfesor))
            {
                ConfiguracionSiia siia = InicioConSiia.LeerSinFallar();
                contrasenaDelSiia = EsAlumno ? existente != null && siia.Estudiantes : siia.Docentes;
            }
            conCodigo = datosProfesores != null && datosProfesores.CodigoPorProfesor;
            ProfesorInfo profesor = existente as ProfesorInfo;
            codigoOriginal = profesor == null ? "" : (profesor.Codigo ?? "");

            InicializarComponentes();
            if (EsAlumno)
            {
                CargarCatalogos();
            }

            if (conCodigo && string.IsNullOrEmpty(txtCodigo.Text))
            {
                ProponerCodigo();
            }

            // Al editar, lo normal es corregir el nombre; el identificador es la
            // credencial y no conviene que empiece seleccionado.
            ActiveControl = EsNuevo ? (Control)txtIdentificador : txtNombre;
        }

        /// <summary>
        /// "Crear cuenta" del inicio de sesion: el alumno se da de alta con los
        /// mismos datos que le pide el administrador (sin grupo: el codigo es del
        /// profesor). Si la crea, devuelve su matricula y su contrasena para
        /// entrar directamente.
        /// </summary>
        public static bool CrearCuenta(IWin32Window dueno, out string matricula, out string contrasena)
        {
            using (DialogoUsuario dialogo = new DialogoUsuario(new AlumnosDatos(), null, 0, true))
            {
                bool creada = dialogo.ShowDialog(dueno) == DialogResult.OK;
                matricula = creada ? dialogo.IdentificadorGuardado : null;
                contrasena = creada ? dialogo.contrasenaNueva : null;
                return creada;
            }
        }

        public string IdentificadorGuardado { get; private set; }

        public string NombreGuardado { get; private set; }

        /// <summary>El codigo guardado del profesor (vacio para alumnos y encargados, o sin la migracion 09).</summary>
        public string CodigoGuardado { get; private set; }

        /// <summary>True si al guardar cambio el color del profesor.</summary>
        public bool ColorCambiado { get; private set; }

        private bool EsProfesor
        {
            get { return datosProfesores != null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }

        private bool EsNuevo
        {
            get { return existente == null; }
        }

        private bool EsAlumno
        {
            get { return datos is AlumnosDatos; }
        }

        /// <summary>
        /// Editar a un estudiante que entra con el SIIA: sin correo ni semestre, que su
        /// inicio de sesion no da. Si su cuenta de antes los tiene, se conservan al guardar.
        /// </summary>
        private bool SoloLoDelSiia
        {
            get { return EsAlumno && contrasenaDelSiia; }
        }

        /// <summary>Encargado de laboratorio (db/18): sin carrera, codigo ni color.</summary>
        private bool EsEncargado
        {
            get { return datos is EncargadosDatos; }
        }

        /// <summary>"Crear cuenta" con aviso de privacidad: se ensena y hay que marcar que se leyo.</summary>
        private bool ConAvisoPrivacidad
        {
            get { return autoregistro && avisoPrivacidad.HayTexto; }
        }

        private void InicializarComponentes()
        {
            string quien = EsAlumno ? "estudiante" : EsEncargado ? "responsable de laboratorio" : "docente";
            // Los campos de contrasena estan tambien al editar (opcionales): 74; con el SIIA, una nota.
            // Con el SIIA la carrera sube junto a la matricula: sin correo ni semestre, una fila menos.
            int alto = 338 + (EsAlumno ? (SoloLoDelSiia ? 0 : 66) : conCodigo ? 88 : 0) + (contrasenaDelSiia ? AltoNotaSiia : 74) + (autoregistro ? 26 : 0)
                + (avisoSinCorreo ? AltoAvisoCorreo : 0)
                + (ConAvisoPrivacidad ? AltoAvisoPrivacidad : 0)
                + (EsProfesor ? SelectorColorProfesor.Alto + 6 : 0)
                + (EsEncargado ? AltoNotaServicioSocial : 0);
            EstiloHorarios.PrepararDialogo(this, autoregistro ? "Crear cuenta" : (EsNuevo ? "Registrar " : "Editar ") + quien, new Size(640, alto));
            Controls.Add(EstiloAdmin.TituloDialogo(autoregistro ? "Crea tu cuenta"
                : EsNuevo ? "Registrar " + quien : "Editar a " + existente.NombreCompleto, 592));

            int y = 60;
            if (autoregistro)
            {
                // Encima de la pantalla del kiosco, que esta siempre al frente.
                TopMost = Kiosco.Activo;
                Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9.5F),
                    ForeColor = Color.FromArgb(70, 70, 70),
                    Location = new Point(24, 50),
                    Size = new Size(592, 22),
                    Text = "Con tu matrícula y tu contraseña entrarás en cualquier equipo del laboratorio."
                });
                y += 26;
            }

            txtIdentificador = Campo(char.ToUpper(nombreIdentificador[0]) + nombreIdentificador.Substring(1) + " *", 24, y, 180);
            txtIdentificador.MaxLength = autoregistro ? 10 : 30;
            if (SoloLoDelSiia)
            {
                // Sin caja: guarda el que ya tuviera (TerminarDialogo), como las contrasenas.
                txtCorreo = new TextBox();
                AgregarCarrera(224, y, 392);
            }
            else
            {
                // Con el se recupera la contrasena desde el inicio de sesion (un codigo por correo).
                txtCorreo = Campo(autoregistro ? "Correo (para recuperar tu contraseña)" : "Correo (para recuperar su contraseña)", 224, y, 392);
            }

            // En la base de la escuela correo es VARCHAR(45): uno mas largo se
            // guardaria cortado sin avisar.
            txtCorreo.MaxLength = 45;

            y += 66;
            if (avisoSinCorreo)
            {
                // Debajo del correo, mientras este vacio: es opcional, pero sin el la
                // contrasena olvidada solo la puede cambiar el administrador.
                lblSinCorreo = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(190, 85, 0),
                    Location = new Point(224, y - 11),
                    Size = new Size(392, AltoAvisoCorreo),
                    Text = "Sin correo no podrás recuperar tu contraseña por tu cuenta."
                };
                Controls.Add(lblSinCorreo);
                txtCorreo.TextChanged += (s, e) => lblSinCorreo.Visible = txtCorreo.Text.Trim().Length == 0;
                y += AltoAvisoCorreo;
            }
            if (EsEncargado)
            {
                // Quien ayuda del servicio social ya tiene su cuenta de alumno, y el
                // numero y el correo son unicos en la base: su cuenta de encargado
                // lleva otros.
                Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9F),
                    ForeColor = Color.FromArgb(90, 90, 90),
                    Location = new Point(24, y - 6),
                    Size = new Size(592, 36),
                    Text = "Servicio social: SS más su matrícula (por ejemplo SS2023123456) y otro correo que el de su cuenta de estudiante, o ninguno."
                });
                y += AltoNotaServicioSocial;
            }

            txtNombre = Campo("Nombre(s) *", 24, y, 186);
            txtNombre.MaxLength = 80;
            txtPaterno = Campo("Apellido paterno *", 227, y, 186);
            txtPaterno.MaxLength = 80;
            txtMaterno = Campo("Apellido materno", 430, y, 186);
            txtMaterno.MaxLength = 80;
            y += 66;

            if (EsAlumno)
            {
                if (!SoloLoDelSiia)
                {
                    AgregarCarrera(24, y, 360);
                    Controls.Add(EstiloHorarios.CrearEtiqueta("Semestre", new Point(400, y)));
                    cboSemestre = EstiloHorarios.CrearCombo(new Point(400, y + 26), 120);
                    Controls.Add(cboSemestre);
                    y += 66;
                }
            }
            else if (conCodigo)
            {
                txtCodigo = Campo("Código de clase *", 24, y, 150);
                txtCodigo.MaxLength = 16;
                txtCodigo.Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
                ProfesorInfo profesor = existente as ProfesorInfo;
                txtCodigo.Text = profesor == null ? "" : profesor.Codigo;
                txtCodigo.TextChanged += (s, e) =>
                {
                    lblAvisoCodigo.Visible = codigoOriginal.Length > 0 && txtCodigo.Text.Trim() != codigoOriginal;
                };

                Button proponer = EstiloHorarios.CrearBoton("Proponer otro", new Point(184, y + 23), new Size(130, 32));
                proponer.Font = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
                proponer.Click += (s, e) => ProponerCodigo();
                Controls.Add(proponer);

                Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9F),
                    ForeColor = Color.FromArgb(90, 90, 90),
                    Location = new Point(330, y + 6),
                    Size = new Size(286, 50),
                    Text = "Lo teclean sus estudiantes en el laboratorio. Vale para todas sus clases."
                });

                lblAvisoCodigo = new Label
                {
                    AutoSize = true,
                    Font = new DrawingFont("Century Gothic", 9F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(190, 85, 0),
                    Location = new Point(24, y + 60),
                    Text = "Sus estudiantes tendrán que usar el código nuevo desde que lo guardes.",
                    Visible = false
                };
                Controls.Add(lblAvisoCodigo);
                y += 88;
            }

            if (EsProfesor)
            {
                ProfesorInfo profesor = existente as ProfesorInfo;
                selectorColor = new SelectorColorProfesor(existente == null ? 0 : existente.Id, profesor == null ? "" : profesor.Color,
                    datosProfesores.ColorElegible, NombreDeOtroProfesor, ayuda, 592)
                {
                    Location = new Point(24, y)
                };
                Controls.Add(selectorColor);
                y += SelectorColorProfesor.Alto + 6;
            }

            if (contrasenaDelSiia)
            {
                // Sin cajas en el dialogo: en blanco, Guardar no cambia la contrasena.
                txtContrasena = new TextBox();
                txtConfirmar = new TextBox();
                Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9.5F),
                    ForeColor = Color.FromArgb(95, 99, 96),
                    Location = new Point(24, y),
                    Size = new Size(592, AltoNotaSiia - 8),
                    Text = EsProfesor
                        ? "Sin contraseña de DigLab: entra con su número de empleado, su contraseña y su NIP del SIIA. "
                          + "La primera vez, la administración enlaza su número con esta cuenta (Docentes, «Enlazar...»)."
                        : "Su contraseña es la del SIIA del TESCHI: se cambia o se recupera en el SIIA, no aquí."
                });
                y += AltoNotaSiia;
            }
            else
            {
                AgregarContrasena(y);
                y += 74;
            }

            if (ConAvisoPrivacidad)
            {
                CrearAvisoPrivacidad(y);
                y += AltoAvisoPrivacidad;
            }

            // Tres lineas: un error con su causa y que hacer (MensajeDeFallo) no cabia en dos.
            lblError = EstiloAdmin.LineaError(new Point(24, y), 592, 54);
            Controls.Add(lblError);

            Controls.Add(new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.FromArgb(110, 110, 110),
                Location = new Point(24, ClientSize.Height - 40),
                Text = "* obligatorio"
            });
            TerminarDialogo();
        }

        private void AgregarCarrera(int x, int y, int ancho)
        {
            Controls.Add(EstiloHorarios.CrearEtiqueta("Carrera *", new Point(x, y)));
            cboCarrera = EstiloHorarios.CrearCombo(new Point(x, y + 26), ancho);
            cboCarrera.SelectedIndexChanged += (s, e) => Limpiar(cboCarrera);
            Controls.Add(cboCarrera);
        }

        /// <summary>La contrasena y su confirmacion, con "Mostrar" y la regla debajo.</summary>
        private void AgregarContrasena(int y)
        {
            // Al dar de alta, obligatoria; al editar, opcional: en blanco no se cambia.
            txtContrasena = Campo(EsNuevo ? "Contraseña *" : "Contraseña nueva", 24, y, 186);
            txtContrasena.UseSystemPasswordChar = true;
            txtConfirmar = Campo(EsNuevo ? "Repite la contraseña *" : "Repítela", 227, y, 186);
            txtConfirmar.UseSystemPasswordChar = true;
            CheckBox mostrar = new CheckBox
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                Location = new Point(430, y + 29),
                Text = "Mostrar"
            };
            mostrar.CheckedChanged += (s, e) =>
            {
                txtContrasena.UseSystemPasswordChar = !mostrar.Checked;
                txtConfirmar.UseSystemPasswordChar = !mostrar.Checked;
            };
            Controls.Add(mostrar);
            if (!EsNuevo)
            {
                ayuda.SetToolTip(txtContrasena, "Déjala en blanco para no cambiarla. Si la escribes, podrá entrar con ella en cuanto guardes.");
            }

            // La regla antes del error, debajo de la contrasena.
            Controls.Add(new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(24, y + 57),
                Text = ReglasCuenta.TextoLargoMinimo + (EsNuevo ? "" : " En blanco, no se cambia.")
            });
        }

        /// <summary>Guardar y Cancelar (y "Ver su horario" de un docente), y los datos de quien se edita.</summary>
        private void TerminarDialogo()
        {
            btnGuardar = EstiloAdmin.BotonesDialogo(this, autoregistro ? "Crear cuenta" : "Guardar", Guardar);
            if (ConAvisoPrivacidad)
            {
                // Apagado se ve gris (BotonesApagados, como todos). Lo enciende la casilla del aviso.
                btnGuardar.Enabled = false;
            }

            if (EsProfesor && !EsNuevo)
            {
                // Su horario semanal, solo para verlo (se cambia en Horarios).
                Button verHorario = EstiloHorarios.CrearBoton("Ver su horario", new Point(132, btnGuardar.Top), new Size(150, 40));
                verHorario.Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);
                verHorario.Click += (s, e) =>
                {
                    using (BitacoraMisClases horario = new BitacoraMisClases(existente.Id, existente.NombreCompleto))
                    {
                        horario.ShowDialog(this);
                    }
                };
                Controls.Add(verHorario);
            }

            if (!EsNuevo)
            {
                txtIdentificador.Text = existente.Matricula;
                txtCorreo.Text = existente.Correo;
                txtNombre.Text = existente.Nombre;
                txtPaterno.Text = existente.ApellidoPaterno;
                txtMaterno.Text = existente.ApellidoMaterno;
            }

            if (selectorColor != null)
            {
                // La muestra lleva su nombre y su codigo como en la cuadricula.
                txtNombre.TextChanged += (s, e) => MostrarMuestraDeColor();
                txtPaterno.TextChanged += (s, e) => MostrarMuestraDeColor();
                if (txtCodigo != null)
                {
                    txtCodigo.TextChanged += (s, e) => MostrarMuestraDeColor();
                }

                MostrarMuestraDeColor();
            }
        }

        private void MostrarMuestraDeColor()
        {
            selectorColor.Mostrar(HorariosDatos.NombreCorto(txtNombre.Text, txtPaterno.Text), txtCodigo == null ? "" : txtCodigo.Text.Trim());
        }

        /// <summary>
        /// "Crear cuenta": el aviso de privacidad simplificado (con desplazamiento),
        /// la casilla obligatoria "Lei el aviso de privacidad" y, si hay direccion,
        /// "Leer el aviso de privacidad completo" (en el kiosco, dentro de DigLab).
        /// </summary>
        private void CrearAvisoPrivacidad(int y)
        {
            // Un poco separado de "Al menos 6 caracteres", que va justo encima.
            y += 8;
            Controls.Add(EstiloHorarios.CrearEtiqueta("Aviso de privacidad", new Point(24, y)));
            TextBox texto = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(50, 50, 50),
                Font = new DrawingFont("Century Gothic", 9F),
                Location = new Point(24, y + 26),
                // Cinco renglones a la vista; el resto, con la barra.
                Size = new Size(592, 88),
                Text = avisoPrivacidad.Texto,
                AccessibleName = "Aviso de privacidad"
            };
            Controls.Add(texto);

            chkAvisoLeido = new CheckBox
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30),
                Location = new Point(24, y + 122),
                Text = "Leí el aviso de privacidad"
            };
            chkAvisoLeido.CheckedChanged += (s, e) =>
            {
                btnGuardar.Enabled = chkAvisoLeido.Checked;
                if (chkAvisoLeido.Checked && lblError.Text.Contains("aviso de privacidad"))
                {
                    lblError.Text = "";
                }
            };
            ayuda.SetToolTip(chkAvisoLeido, "Para crear tu cuenta, lee el aviso y marca esta casilla.");
            Controls.Add(chkAvisoLeido);

            if (avisoPrivacidad.HayUrl)
            {
                LinkLabel completo = avisoPrivacidad.CrearEnlace(this);
                Controls.Add(completo);
                completo.Location = new Point(616 - completo.PreferredWidth, y + 124);
            }
        }

        /// <summary>"Nicolas Trejo": para decir que profesor usa ya un color.</summary>
        private string NombreDeOtroProfesor(int id)
        {
            ProfesorInfo otro = datosProfesores.Ultimos.FirstOrDefault(p => p.Id == id);
            return otro == null ? null : HorariosDatos.NombreCorto(otro.Nombre, otro.ApellidoPaterno);
        }

        private TextBox Campo(string etiqueta, int x, int y, int ancho)
        {
            Controls.Add(EstiloHorarios.CrearEtiqueta(etiqueta, new Point(x, y)));
            TextBox texto = EstiloHorarios.CrearTexto(new Point(x, y + 26), ancho);
            texto.TextChanged += (s, e) => Limpiar(texto);
            Controls.Add(texto);
            return texto;
        }

        private void CargarCatalogos()
        {
            AlumnoInfo alumno = existente as AlumnoInfo;
            try
            {
                List<OpcionCombo> carreras = catalogos.Carreras();
                int? carrera = EsNuevo
                    ? (carreraInicial != 0 ? carreraInicial : carreras.Count == 1 ? carreras[0].Id : (int?)null)
                    : alumno.CarreraId;
                EstiloHorarios.Rellenar(cboCarrera, carreras, carrera);
                if (cboSemestre != null)
                {
                    List<OpcionCombo> semestres = catalogos.Semestres();
                    semestres.Insert(0, new OpcionCombo { Id = 0, Texto = "—" });
                    EstiloHorarios.Rellenar(cboSemestre, semestres, EsNuevo || !alumno.SemestreId.HasValue ? 0 : alumno.SemestreId.Value);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Cargar carreras y semestres", ex);
                lblError.Text = MensajeDeFallo.Explicar("No se pudieron cargar las carreras", ex, SiSigue);
            }
        }

        /// <summary>A quien acude quien ve un error aqui: la persona del laboratorio, o la administracion su log.</summary>
        private string SiSigue
        {
            get { return autoregistro ? MensajeDeFallo.AvisaAlLaboratorio : MensajeDeFallo.RevisaElRegistro; }
        }

        private void ProponerCodigo()
        {
            try
            {
                txtCodigo.Text = datosProfesores.ProponerCodigo(existente == null ? 0 : existente.Id);
            }
            catch (InvalidOperationException ex) when (EstiloHorarios.EsAvisoDeDigLab(ex))
            {
                // Los que probo ya estaban en uso: el aviso de ProfesoresDatos ya dice que hacer.
                Log.Aviso("Proponer un codigo de profesor: ningun codigo libre entre los que se probaron.");
                lblError.Text = ex.Message;
            }
            catch (Exception ex)
            {
                Log.Error("Proponer un codigo de profesor", ex);
                lblError.Text = MensajeDeFallo.Explicar("No se pudo proponer un código", ex, SiSigue);
            }
        }

        private void Guardar()
        {
            // Sin sesion (el "Crear cuenta" del inicio de sesion) no bloquea.
            if (Permisos.Bloquear(this, "Guardar un usuario")) return;
            // El boton ya esta apagado sin la casilla; por si se llega de otro modo.
            if (ConAvisoPrivacidad && !chkAvisoLeido.Checked)
            {
                lblError.Text = "Para crear tu cuenta, lee el aviso de privacidad y marca «Leí el aviso de privacidad».";
                chkAvisoLeido.Focus();
                return;
            }

            lblError.Text = "";
            // Lo que falta se junta en una frase ("Falta: matricula y carrera."); lo
            // que esta mal escrito se explica aparte.
            List<string> faltan = new List<string>();
            List<string> errores = new List<string>();
            Control primero = null;

            string identificador = txtIdentificador.Text.Trim();
            if (identificador.Length == 0)
            {
                Marcar(txtIdentificador, nombreIdentificador, faltan, ref primero);
            }
            else if (autoregistro && !MatriculaDeAlumno.IsMatch(identificador))
            {
                Marcar(txtIdentificador, "La matrícula son las 10 cifras de tu credencial. Si la tuya es distinta, pide a la administración "
                    + "de DigLab que te dé de alta.", errores, ref primero);
            }
            else if (!ReglasCuenta.UsuarioValido(identificador))
            {
                // La misma regla que el inicio de sesion: si no, se daria de alta a
                // alguien que despues no puede entrar.
                Marcar(txtIdentificador, (EsAlumno ? "La " : "El ") + nombreIdentificador
                    + " no puede llevar espacios ni emojis (así lo pide el inicio de sesión).", errores, ref primero);
            }

            if (txtNombre.Text.Trim().Length == 0)
            {
                Marcar(txtNombre, "nombre", faltan, ref primero);
            }

            if (txtPaterno.Text.Trim().Length == 0)
            {
                Marcar(txtPaterno, "apellido paterno", faltan, ref primero);
            }

            string correo = txtCorreo.Text.Trim();
            // Sin la caja no se puede corregir: uno de antes mal escrito no impide guardar lo demas.
            string problemaCorreo = SoloLoDelSiia ? null : ReglasCuenta.ProblemaDeCorreo(correo);
            if (problemaCorreo != null)
            {
                Marcar(txtCorreo, problemaCorreo, errores, ref primero);
            }

            OpcionCombo carrera = null;
            if (EsAlumno)
            {
                carrera = cboCarrera.SelectedItem as OpcionCombo;
                if (carrera == null)
                {
                    Marcar(cboCarrera, "carrera", faltan, ref primero);
                }
            }

            string codigo = conCodigo ? txtCodigo.Text.Trim() : "";
            if (conCodigo)
            {
                if (codigo.Length == 0)
                {
                    Marcar(txtCodigo, "código de clase", faltan, ref primero);
                }
                else if (!CodigoValido.IsMatch(codigo))
                {
                    Marcar(txtCodigo, "El código de clase lleva de 3 a 16 letras o números, sin espacios.", errores, ref primero);
                }
            }

            // Al editar, solo si se escribio una: en blanco no se cambia.
            if (!contrasenaDelSiia && (EsNuevo || txtContrasena.Text.Length > 0 || txtConfirmar.Text.Length > 0))
            {
                // Las mismas reglas que el inicio de sesion (ReglasCuenta): una
                // contrasena de solo espacios no dejaria entrar nunca.
                string problema = ReglasCuenta.ProblemaDeContrasena(txtContrasena.Text);
                if (txtContrasena.Text.Length == 0)
                {
                    Marcar(txtContrasena, "contraseña", faltan, ref primero);
                }
                else if (problema != null)
                {
                    Marcar(txtContrasena, problema, errores, ref primero);
                }
                else if (txtContrasena.Text != txtConfirmar.Text)
                {
                    Marcar(txtConfirmar, "Las dos contraseñas no coinciden.", errores, ref primero);
                }
            }

            if (primero != null)
            {
                List<string> frases = new List<string>();
                if (faltan.Count > 0)
                {
                    frases.Add("Falta: " + EstiloAdmin.Enumerar(faltan) + ".");
                }

                frases.AddRange(errores);
                lblError.Text = string.Join(" ", frases);
                primero.Focus();
                return;
            }

            DatosUsuario nuevos;
            if (EsAlumno)
            {
                OpcionCombo semestre = cboSemestre == null ? null : cboSemestre.SelectedItem as OpcionCombo;
                nuevos = new DatosAlumno
                {
                    CarreraId = carrera.Id,
                    // Sin la caja, el que ya tuviera.
                    SemestreId = cboSemestre == null ? ((AlumnoInfo)existente).SemestreId
                        : semestre == null || semestre.Id == 0 ? (int?)null : semestre.Id
                };
            }
            else if (EsEncargado)
            {
                // Solo su fila de usuarios: no tiene carrera, codigo ni color.
                nuevos = new DatosUsuario();
            }
            else
            {
                nuevos = new DatosProfesor
                {
                    Codigo = codigo,
                    ConColor = selectorColor != null && selectorColor.Habilitado,
                    Color = selectorColor == null ? null : selectorColor.ColorElegido
                };
            }

            nuevos.Matricula = identificador;
            nuevos.Nombre = txtNombre.Text.Trim();
            nuevos.ApellidoPaterno = txtPaterno.Text.Trim();
            nuevos.ApellidoMaterno = txtMaterno.Text.Trim();
            nuevos.Correo = correo;
            nuevos.ContrasenaNueva = !EsNuevo && txtContrasena.Text.Length > 0 ? txtContrasena.Text : null;

            Cursor = Cursors.WaitCursor;
            try
            {
                if (EsNuevo)
                {
                    // Con el SIIA, sin contrasena de DigLab (UsuariosDatos.Crear).
                    datos.Crear(nuevos, contrasenaDelSiia ? null : txtContrasena.Text);
                }
                else
                {
                    datos.Actualizar(existente.Id, nuevos);
                }

                if (ConAvisoPrivacidad)
                {
                    // Constancia de que lo acepto antes de dar sus datos (RNF72).
                    Log.Info("Crear cuenta: " + nuevos.Matricula + " marco 'Lei el aviso de privacidad' antes de crearla.");
                }

                IdentificadorGuardado = nuevos.Matricula;
                NombreGuardado = string.Join(" ", new[] { nuevos.Nombre, nuevos.ApellidoPaterno, nuevos.ApellidoMaterno }.Where(p => p.Length > 0));
                CodigoGuardado = codigo;
                ProfesorInfo antes = existente as ProfesorInfo;
                ColorCambiado = selectorColor != null && selectorColor.Habilitado
                    && !string.Equals(selectorColor.ColorElegido ?? "", antes == null ? "" : antes.Color ?? "", StringComparison.OrdinalIgnoreCase);
                contrasenaNueva = autoregistro ? txtContrasena.Text : null;
                DialogResult = DialogResult.OK;
            }
            catch (ErrorDeDatos ex)
            {
                // El dato que ya tiene otro usuario se senala; sin dato, es otra cosa
                // (como que a la base le falte una migracion).
                TextBox campo = ex.Campo == CampoRepetido.Codigo && conCodigo ? txtCodigo
                    : ex.Campo == CampoRepetido.Correo ? txtCorreo
                    : ex.Campo == CampoRepetido.Identificador ? txtIdentificador
                    : null;
                lblError.Text = autoregistro ? ParaElAlumno(ex, identificador) : ex.Message;
                if (campo != null)
                {
                    campo.BackColor = EstiloAdmin.FondoError;
                    campo.Focus();
                }
            }
            catch (MySqlException ex) when (autoregistro && (ex.Number == SinPermisoTabla || ex.Number == SinPermisoColumna))
            {
                Log.Error("Crear cuenta desde el inicio de sesion: el usuario de MySQL de este equipo no puede dar de alta alumnos (db/07 y db/10)", ex);
                lblError.Text = "Este equipo no tiene permiso para crear cuentas. Avisa a la persona responsable del laboratorio.";
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el texto tecnico solo va al log.
                Log.Error(autoregistro ? "Crear cuenta desde el inicio de sesion" : EsNuevo ? "Alta de usuario" : "Editar usuario", ex);
                lblError.Text = MensajeDeFallo.Explicar(autoregistro ? "No se pudo crear tu cuenta" : "No se pudo guardar", ex, SiSigue);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        // ER_TABLEACCESS_DENIED_ERROR y ER_COLUMNACCESS_DENIED_ERROR: sin INSERT en
        // usuarios, o con el permiso por columna de db/07 y sin el valor por
        // defecto de db/10 (se nombra fk_tipo_usuario y no se puede).
        private const int SinPermisoTabla = 1142;
        private const int SinPermisoColumna = 1143;

        /// <summary>Si la recuperacion de contrasena por correo esta activada (Ajustes); ante la duda, no.</summary>
        private static bool RecuperacionActiva()
        {
            try
            {
                return RecuperacionContrasena.Buscar().Configurada;
            }
            catch (Exception ex)
            {
                Log.Error("Crear cuenta: no se pudo saber si la recuperacion por correo esta activada", ex);
                return false;
            }
        }

        /// <summary>Un ErrorDeDatos de "Crear cuenta", dicho para el alumno.</summary>
        private static string ParaElAlumno(ErrorDeDatos ex, string matricula)
        {
            switch (ex.Campo)
            {
                case CampoRepetido.Identificador:
                    // Puede no ser suya: nada impide que otro la haya registrado antes.
                    return "Ya hay una cuenta con la matrícula " + matricula + ". Si es tuya y no recuerdas la contraseña, "
                        + "o si tú no la creaste, avisa a la administración de DigLab.";
                case CampoRepetido.Correo:
                    return "Ese correo ya lo usa otra cuenta. Escribe otro o déjalo vacío.";
                default:
                    Log.Aviso("Crear cuenta desde el inicio de sesion: " + ex.Message);
                    return "No se pudo crear tu cuenta desde este equipo. Avisa a la persona responsable del laboratorio.";
            }
        }

        private static void Marcar(Control control, string texto, List<string> lista, ref Control primero)
        {
            control.BackColor = EstiloAdmin.FondoError;
            lista.Add(texto);
            if (primero == null)
            {
                primero = control;
            }
        }

        private static void Limpiar(Control control)
        {
            control.BackColor = SystemColors.Window;
        }
    }

    /// <summary>
    /// Grupo nuevo dentro de una carrera. Los grupos son de las clases (profesor +
    /// grupo + materia), asi que se crean desde Horarios al dar de alta una clase.
    /// </summary>
    internal class DialogoGrupo : Form
    {
        private readonly CatalogosDatos catalogos = new CatalogosDatos();
        private readonly ComboBox cboCarrera;
        private readonly TextBox txtNombre;
        private readonly Label lblError;

        public DialogoGrupo(int carreraInicial)
        {
            // 300 de alto: el error, con su causa y que hacer, ocupa hasta tres lineas.
            EstiloHorarios.PrepararDialogo(this, "Nuevo grupo", new Size(460, 312));
            Controls.Add(EstiloAdmin.TituloDialogo("Nuevo grupo", 412));

            Controls.Add(EstiloHorarios.CrearEtiqueta("Carrera *", new Point(24, 60)));
            cboCarrera = EstiloHorarios.CrearCombo(new Point(24, 86), 412);
            Controls.Add(cboCarrera);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Nombre del grupo *", new Point(24, 126)));
            txtNombre = EstiloHorarios.CrearTexto(new Point(24, 152), 200);
            txtNombre.MaxLength = 60;
            Controls.Add(txtNombre);
            Controls.Add(new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(236, 156),
                Text = "Por ejemplo, ISC-5001"
            });

            lblError = EstiloAdmin.LineaError(new Point(24, 188), 412, 56);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Crear grupo", Guardar);

            try
            {
                EstiloHorarios.Rellenar(cboCarrera, catalogos.Carreras(), carreraInicial == 0 ? (int?)null : carreraInicial);
            }
            catch (Exception ex)
            {
                Log.Error("Cargar carreras para un grupo nuevo", ex);
                lblError.Text = MensajeDeFallo.Explicar("No se pudieron cargar las carreras", ex, MensajeDeFallo.RevisaElRegistro);
            }

            ActiveControl = cboCarrera.SelectedIndex < 0 ? (Control)cboCarrera : txtNombre;
        }

        public int IdGuardado { get; private set; }

        public string NombreGuardado { get; private set; }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Crear un grupo")) return;
            OpcionCombo carrera = cboCarrera.SelectedItem as OpcionCombo;
            string nombre = txtNombre.Text.Trim();
            if (carrera == null)
            {
                lblError.Text = "Elige la carrera del grupo.";
                cboCarrera.Focus();
                return;
            }

            if (nombre.Length == 0)
            {
                lblError.Text = "Escribe el nombre del grupo.";
                txtNombre.Focus();
                return;
            }

            try
            {
                IdGuardado = catalogos.CrearGrupo(nombre, carrera.Id);
                NombreGuardado = nombre;
                DialogResult = DialogResult.OK;
            }
            catch (ErrorDeDatos ex)
            {
                lblError.Text = ex.Message;
                txtNombre.Focus();
            }
            catch (Exception ex)
            {
                Log.Error("Crear el grupo " + nombre, ex);
                lblError.Text = MensajeDeFallo.Explicar("No se pudo crear el grupo", ex, MensajeDeFallo.RevisaElRegistro);
            }
        }
    }

    /// <summary>Contrasena nueva para un usuario, escrita dos veces.</summary>
    internal class DialogoContrasena : Form
    {
        private readonly TextBox txtContrasena;
        private readonly TextBox txtConfirmar;
        private readonly Label lblError;

        public DialogoContrasena(string quien)
        {
            EstiloHorarios.PrepararDialogo(this, "Restablecer contraseña", new Size(460, 300));

            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 16),
                Size = new Size(412, 44),
                Text = "Contraseña nueva para " + quien
            });

            Controls.Add(EstiloHorarios.CrearEtiqueta("Contraseña nueva", new Point(24, 68)));
            txtContrasena = EstiloHorarios.CrearTexto(new Point(24, 94), 200);
            txtContrasena.UseSystemPasswordChar = true;
            Controls.Add(txtContrasena);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Repítela", new Point(236, 68)));
            txtConfirmar = EstiloHorarios.CrearTexto(new Point(236, 94), 200);
            txtConfirmar.UseSystemPasswordChar = true;
            Controls.Add(txtConfirmar);

            // La regla antes del error, debajo de la contrasena; "Mostrar", debajo de la otra.
            Controls.Add(new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(24, 128),
                Text = ReglasCuenta.TextoLargoMinimo
            });

            CheckBox mostrar = new CheckBox
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                Location = new Point(236, 126),
                Text = "Mostrar"
            };
            mostrar.CheckedChanged += (s, e) =>
            {
                txtContrasena.UseSystemPasswordChar = !mostrar.Checked;
                txtConfirmar.UseSystemPasswordChar = !mostrar.Checked;
            };
            Controls.Add(mostrar);

            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(24, 162),
                Size = new Size(412, 36),
                Text = "Podrá entrar con ella en cuanto la guardes. Dásela en persona; no se envía por correo."
            });

            lblError = EstiloAdmin.LineaError(new Point(24, 202), 412, 22);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
        }

        public string Contrasena
        {
            get { return txtContrasena.Text; }
        }

        private void Guardar()
        {
            // Las mismas reglas que al dar de alta: con solo espacios el aviso
            // diria "ya puede entrar con ella" y no podria nunca.
            string problema = ReglasCuenta.ProblemaDeContrasena(txtContrasena.Text);
            if (problema != null)
            {
                lblError.Text = problema;
                txtContrasena.Focus();
                return;
            }

            if (txtContrasena.Text != txtConfirmar.Text)
            {
                lblError.Text = "Las dos contraseñas no coinciden.";
                txtConfirmar.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>
    /// Confirmacion con el verbo de la accion en el boton ("Dar de baja") en vez
    /// de Si/No: se entiende que se va a hacer sin releer la pregunta. Puede
    /// llevar una casilla (por ejemplo, desactivar tambien las clases).
    /// </summary>
    internal class DialogoConfirmacion : Form
    {
        private readonly CheckBox casilla;

        private DialogoConfirmacion(string titulo, string pregunta, string detalle, string accion, bool peligrosa, string textoCasilla, bool casillaMarcada)
        {
            int altoCasilla = textoCasilla == null ? 0 : 48;
            EstiloHorarios.PrepararDialogo(this, titulo, new Size(520, 240 + altoCasilla));

            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30),
                Location = new Point(24, 20),
                Size = new Size(472, 48),
                Text = pregunta
            });
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = Color.FromArgb(70, 70, 70),
                Location = new Point(24, 72),
                Size = new Size(472, 88),
                Text = detalle
            });

            if (textoCasilla != null)
            {
                casilla = new CheckBox
                {
                    AutoSize = false,
                    Checked = casillaMarcada,
                    Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                    Location = new Point(24, 164),
                    Size = new Size(472, 44),
                    Text = textoCasilla
                };
                Controls.Add(casilla);
            }

            Button hacer = EstiloAdmin.BotonesDialogo(this, accion, () => { DialogResult = DialogResult.OK; });
            if (peligrosa)
            {
                // En rojo, tambien al pasar el raton (no el verde del boton principal).
                EstiloAdmin.Peligroso(hacer);
                // Enter no confirma: la accion que quita algo se pulsa a proposito.
                AcceptButton = CancelButton as Button;
                ActiveControl = CancelButton as Button;
            }
        }

        public static bool Preguntar(IWin32Window dueno, string titulo, string pregunta, string detalle, string accion, bool peligrosa = true)
        {
            bool marcada;
            return Preguntar(dueno, titulo, pregunta, detalle, accion, peligrosa, null, false, out marcada);
        }

        /// <summary>Con casilla: devuelve si se confirmo y, aparte, si la casilla quedo marcada.</summary>
        public static bool Preguntar(IWin32Window dueno, string titulo, string pregunta, string detalle, string accion, bool peligrosa,
            string textoCasilla, bool casillaMarcada, out bool marcada)
        {
            using (DialogoConfirmacion dialogo = new DialogoConfirmacion(titulo, pregunta, detalle, accion, peligrosa, textoCasilla, casillaMarcada))
            {
                bool confirmado = dialogo.ShowDialog(dueno) == DialogResult.OK;
                marcada = dialogo.casilla != null && dialogo.casilla.Checked;
                return confirmado;
            }
        }
    }
}
