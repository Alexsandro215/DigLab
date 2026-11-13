using DigLab.conexion;
using DigLab.Infraestructura;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    // Los dialogos de la pestana Configuraciones de Horarios (especificacion v2,
    // 4.3): C2 "Crear horario nuevo", C3 "Nombre" y C4 "Clase del horario". Todos van con
    // EstiloHorarios.PrepararDialogo y EstiloAdmin.BotonesDialogo, a la medida
    // de su contenido (caben a 1366x768), y los errores salen en rojo dentro
    // del dialogo, que sigue abierto. Cada uno hace su operacion de
    // CicloPreliminarDatos al pulsar su boton: si falla, lo dice ahi mismo.

    /// <summary>Colores, textos y piezas que comparten la pestana Configuraciones y sus dialogos.</summary>
    internal static class PiezasConfiguracion
    {
        public static readonly Color TextoGris = Color.FromArgb(90, 90, 90);
        public static readonly Color TextoError = Color.FromArgb(190, 30, 30);
        // Rojo (lo que no deja aplicar), como el bloque "Sin bitacoras" del panel.
        public static readonly Color FondoRojo = Color.FromArgb(253, 228, 224);
        public static readonly Color BordeRojo = Color.FromArgb(198, 40, 40);
        public static readonly Color TextoRojo = Color.FromArgb(183, 28, 28);
        // Lo que se pinta en rojo dentro de la cuadricula (choques y sin docente).
        public static readonly Color LetraRojaCuadro = Color.FromArgb(160, 30, 30);
        // Ambar (un aviso que no impide nada).
        public static readonly Color FondoAmbar = Color.FromArgb(255, 243, 205);
        public static readonly Color BordeAmbar = Color.FromArgb(214, 160, 0);
        public static readonly Color TextoAmbar = Color.FromArgb(133, 90, 0);
        // Verde claro (todo bien, o lo que esta en preparacion).
        public static readonly Color FondoVerde = Color.FromArgb(226, 242, 226);
        public static readonly Color FondoPreparacion = Color.FromArgb(236, 246, 236);
        public static readonly Color FondoNueva = Color.FromArgb(214, 238, 234);
        public static readonly Color TextoNueva = Color.FromArgb(0, 105, 92);
        public static readonly Color GrisGuardada = Color.FromArgb(240, 240, 240);
        public static readonly Color BordeGris = Color.FromArgb(190, 190, 190);

        /// <summary>"24/10/2026", siempre con "/".</summary>
        public static string Fecha(DateTime fecha)
        {
            return fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        /// <summary>"martes 02/02/2027".</summary>
        public static string DiaYFecha(DateTime fecha)
        {
            return HorariosDatos.NombresDia[((int)fecha.DayOfWeek + 6) % 7 + 1].ToLowerInvariant() + " " + Fecha(fecha);
        }

        /// <summary>"10:42".</summary>
        public static string Hora(DateTime momento)
        {
            return momento.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>"hoy a las 10:42", "ayer a las 10:42" o "el 22/10/2026 a las 10:42".</summary>
        public static string Cuando(DateTime momento, DateTime hoy)
        {
            string dia = momento.Date == hoy.Date ? "hoy" : momento.Date == hoy.Date.AddDays(-1) ? "ayer" : "el " + Fecha(momento);
            return dia + " a las " + Hora(momento);
        }

        /// <summary>Horas para una tabla: "147", "14.5".</summary>
        public static string NumeroDeHoras(double horas)
        {
            double redondeadas = Math.Round(horas * 2) / 2;
            return redondeadas.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>"Se borra con sus 23 clases" (sin punto), bien dicho con una o ninguna.</summary>
        public static string SeBorraCon(int clases)
        {
            return clases == 0 ? "Se borra (no tiene clases)" : clases == 1 ? "Se borra con su única clase" : "Se borra con sus " + clases + " clases";
        }

        /// <summary>
        /// Un aviso ambar (negrita de 9.5 con su margen) de ese ancho y del alto que
        /// pide su texto: nunca se corta. Con 44 y 64 px fijos, el de "Ya hay un
        /// horario en preparacion" perdia justo lo importante ("...plantilla nueva...").
        /// </summary>
        public static Label AvisoAmbar(string texto, Point posicion, int ancho)
        {
            Label aviso = Texto(texto, posicion, new Size(ancho, 20), 9.5F, TextoAmbar, FontStyle.Bold);
            aviso.BackColor = FondoAmbar;
            aviso.Padding = new Padding(6, 4, 6, 2);
            aviso.Height = AltoDe(texto, aviso.Font, ancho - aviso.Padding.Horizontal) + aviso.Padding.Vertical;
            return aviso;
        }

        /// <summary>Un texto de una o varias lineas, sin negrita salvo que se pida.</summary>
        public static Label Texto(string texto, Point posicion, Size tamano, float puntos, Color color, FontStyle estilo = FontStyle.Regular)
        {
            return new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", puntos, estilo),
                ForeColor = color,
                Location = posicion,
                Size = tamano,
                Text = texto
            };
        }

        /// <summary>
        /// Las letras propias de los controles de una ventana, para soltarlas al
        /// cerrarla (cada una se creo con new para su control: DigLab no comparte
        /// letras entre ventanas). No cuenta la de Windows, la de los controles por
        /// omision ni la que se hereda (la de la ventana, si no se le puso). Se
        /// juntan antes de base.Dispose y se sueltan despues.
        /// </summary>
        public static List<DrawingFont> LetrasDe(Control raiz, DrawingFont heredada)
        {
            List<DrawingFont> letras = new List<DrawingFont>();
            Juntar(raiz, heredada, letras);
            return letras;
        }

        private static void Juntar(Control control, DrawingFont heredada, List<DrawingFont> letras)
        {
            foreach (Control hijo in control.Controls)
            {
                DrawingFont letra = hijo.Font;
                // Por referencia: dos letras iguales de dos controles son dos objetos.
                if (letra != null && !ReferenceEquals(letra, heredada) && !ReferenceEquals(letra, Control.DefaultFont) && !letra.IsSystemFont
                    && !letras.Any(l => ReferenceEquals(l, letra)))
                {
                    letras.Add(letra);
                }

                Juntar(hijo, heredada, letras);
            }
        }

        /// <summary>Suelta las letras de un control que se quita (sin la que hereda de su ventana).</summary>
        public static void SoltarConSuLetra(Control control, DrawingFont heredada)
        {
            DrawingFont letra = control.Font;
            control.Dispose();
            if (letra != null && !ReferenceEquals(letra, heredada) && !ReferenceEquals(letra, Control.DefaultFont) && !letra.IsSystemFont)
            {
                letra.Dispose();
            }
        }

        /// <summary>Lo que mide de alto un texto que se ajusta a ese ancho.</summary>
        public static int AltoDe(string texto, DrawingFont fuente, int ancho)
        {
            return TextRenderer.MeasureText(texto ?? "", fuente, new Size(ancho, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
        }

        /// <summary>
        /// El fallo de una operacion para la linea de error de un dialogo: lo que
        /// ya viene explicado (ErrorDeDatos, AccionNoPermitida) tal cual; lo demas,
        /// con su causa y que hacer (AvisoDeFalloAdmin). El detalle, al log.
        /// </summary>
        public static string TextoDeFallo(string contexto, string queNoSePudo, Exception ex)
        {
            if (ex is ErrorDeDatos || ex is AccionNoPermitida)
            {
                Log.Aviso(contexto + ": " + ex.Message);
            }
            else
            {
                Log.Error(contexto, ex);
            }

            return AvisoDeFalloAdmin.Texto(queNoSePudo, ex);
        }

        /// <summary>
        /// La ayuda de un control mientras esta desactivado: Windows no le da el
        /// raton (se lo da a su contenedor) y su ToolTip no sale. La ensena el
        /// contenedor al pasar por encima; activado, la suya normal.
        /// </summary>
        public static void AyudaSiDesactivado(ToolTip ayuda, Control control, Func<string> texto)
        {
            bool ensenando = false;
            MouseEventHandler moverse = (s, e) =>
            {
                Control padre = control.Parent;
                string t = texto();
                bool encima = padre != null && control.Visible && !control.Enabled && !string.IsNullOrEmpty(t) && control.Bounds.Contains(e.Location);
                if (encima && !ensenando)
                {
                    ayuda.Show(t, padre, e.X + 12, e.Y + 20, 5000);
                    ensenando = true;
                }
                else if (!encima && ensenando)
                {
                    ayuda.Hide(padre);
                    ensenando = false;
                }
            };
            EventHandler salir = (s, e) =>
            {
                if (ensenando && control.Parent != null)
                {
                    ayuda.Hide(control.Parent);
                    ensenando = false;
                }
            };

            Control conectado = null;
            EventHandler conectar = (s, e) =>
            {
                if (conectado != null)
                {
                    conectado.MouseMove -= moverse;
                    conectado.MouseLeave -= salir;
                }

                conectado = control.Parent;
                if (conectado != null)
                {
                    conectado.MouseMove += moverse;
                    conectado.MouseLeave += salir;
                }
            };
            control.ParentChanged += conectar;
            conectar(control, EventArgs.Empty);
        }
    }

    // =========================================================================
    // C2. Crear horario nuevo
    // =========================================================================

    /// <summary>
    /// C2. "Crear horario nuevo...": el unico camino para empezar el horario del proximo
    /// ciclo (CicloPreliminarDatos.Preparar). "Cargar como base" y "Crear plantilla nueva"
    /// se confundian (pedido del usuario, 2026-11-04): aqui cada opcion dice que trae. El
    /// horario en uso se copia como esta hoy, con lo que se le cambio durante el ciclo; una
    /// plantilla o un horario que ya termino, como se guardo. Solo se ve lo que pide la
    /// opcion elegida, asi que el dialogo mide lo que necesita.
    /// </summary>
    internal class DialogoCrearHorario : Form
    {
        private const int AnchoDialogo = 640;
        private const int AnchoDentro = AnchoDialogo - 48;
        private const int XOpcion = 24;
        private const int XDetalle = 44;

        private readonly CicloPreliminarDatos datos;
        private readonly Func<int> idAdministrador;
        private readonly CicloPreliminar borrador;
        private readonly DateTime hoy;
        private readonly List<OpcionDeInicio> opciones = new List<OpcionDeInicio>();
        private readonly RadioButton rbCopiar;
        private readonly RadioButton rbClases;
        private readonly RadioButton rbBlanco;
        // Desde un Excel en cuadricula (pedido del 2026-11-12).
        private readonly RadioButton rbExcel;
        private readonly Panel panelExcel;
        private readonly Label lblArchivoExcel;
        private string rutaExcel;
        private readonly ComboBox cboConfiguracion;
        private readonly Label lblNombre;
        private readonly TextBox txtNombre;
        private readonly Label lblAviso;
        private readonly Label lblError;
        private readonly Button btnCrear;
        private readonly Button btnCancelar;

        private sealed class OpcionDeInicio
        {
            public RadioButton Radio;
            public Label Nota;
            /// <summary>Lo que pide esa opcion, o null: solo se ve si esta elegida.</summary>
            public Control Detalle;
        }

        private sealed class ItemDeConfiguracion
        {
            /// <summary>La plantilla o el horario aplicado; null: el horario de hoy, sin un aplicado que lo diga.</summary>
            public CicloPreliminar Ciclo;
            /// <summary>Es el horario en uso: se copia como esta hoy, con sus cambios.</summary>
            public bool EnUso;

            public override string ToString()
            {
                if (Ciclo == null)
                {
                    return "El horario de hoy (como está, con sus cambios)";
                }

                return Ciclo.Nombre + (EnUso ? " (en uso: como está hoy, con sus cambios)"
                    : " (" + (Ciclo.Aplicada ? "como se aplicó" : "plantilla guardada") + ", " + RevisionCiclo.TextoClases(Ciclo.Clases) + ")");
            }
        }

        /// <summary>Lo que quedo en preparacion.</summary>
        public ResultadoEmpezar Resultado { get; private set; }

        /// <summary>De donde salio, para el estado: "de <<2026-1>>", "de <<2026-1>>, como esta hoy", o null en blanco.</summary>
        public string DeDonde { get; private set; }

        /// <summary>Se eligio "Solo las clases": el horario queda sin horas.</summary>
        public bool SinHoras { get; private set; }

        /// <summary>Fallo al crear (se dijo aqui): al cancelar, la pestana vuelve a leer.</summary>
        public bool HuboFallo { get; private set; }

        /// <summary>Se creo con un Excel: lo que se cargo (null si no fue con Excel o si fallo al cargarlo).</summary>
        public ResultadoCargaExcel ExcelCargado { get; private set; }

        /// <summary>Se creo en blanco pero el Excel no se pudo cargar: por que (para la linea de estado).</summary>
        public string FalloDelExcel { get; private set; }

        /// <summary>El nombre del archivo de Excel con el que se creo, o null.</summary>
        public string ArchivoExcel
        {
            get { return rbExcel.Checked && rutaExcel != null ? Path.GetFileName(rutaExcel) : null; }
        }

        /// <param name="configuraciones">Las guardadas y las aplicadas, de donde se puede copiar.</param>
        /// <param name="borrador">La que ya esta en preparacion, o null: si hay una, se descarta (lo pregunta).</param>
        /// <param name="elegida">La plantilla que se propone copiar (la elegida en la lista).</param>
        /// <param name="excel">Un Excel que se solto en la pestana: ya elegido, con "Desde un Excel".</param>
        public DialogoCrearHorario(CicloPreliminarDatos datos, DateTime hoy, List<CicloPreliminar> configuraciones,
            CicloPreliminar borrador, int? elegida, Func<int> idAdministrador, string excel = null)
        {
            this.datos = datos;
            this.idAdministrador = idAdministrador;
            this.borrador = borrador;
            this.hoy = hoy.Date;
            configuraciones = configuraciones ?? new List<CicloPreliminar>();

            EstiloHorarios.PrepararDialogo(this, "Crear horario nuevo", new Size(AnchoDialogo, 520));
            Controls.Add(EstiloAdmin.TituloDialogo("Crear horario nuevo", AnchoDentro));
            Controls.Add(PiezasConfiguracion.Texto(
                "Se prepara aparte, sin tocar el que se usa: cada cambio se guarda al momento y nadie lo ve hasta que lo apliques.",
                new Point(XOpcion, 50), new Size(AnchoDentro, 38), 9.5F, PiezasConfiguracion.TextoGris));
            Controls.Add(PiezasConfiguracion.Texto("¿Cómo quieres empezar?", new Point(XOpcion, 92), new Size(AnchoDentro, 22), 10F, Color.DarkGreen, FontStyle.Bold));

            cboConfiguracion = EstiloHorarios.CrearCombo(Point.Empty, 540);
            cboConfiguracion.MaxDropDownItems = 16;
            // Con un aplicado en uso, ese se copia como esta hoy. Sin ninguno (clases puestas a mano o
            // importadas) o con varios, el de hoy va aparte y cada aplicado se copia como se aplico.
            bool unoEnUso = configuraciones.Count(c => c.EnUso(hoy)) == 1;
            if (!unoEnUso && HayClasesHoy())
            {
                cboConfiguracion.Items.Add(new ItemDeConfiguracion { EnUso = true });
            }

            foreach (CicloPreliminar ciclo in configuraciones)
            {
                cboConfiguracion.Items.Add(new ItemDeConfiguracion { Ciclo = ciclo, EnUso = unoEnUso && ciclo.EnUso(hoy) });
            }

            Controls.Add(cboConfiguracion);

            string sinNinguno = cboConfiguracion.Items.Count == 0 ? " Todavía no hay ninguno." : "";
            rbCopiar = AgregarOpcion("Copiar un horario completo: clases y horas",
                "Queda igual que la plantilla o el horario que elijas; luego cambias solo lo que cambie." + sinNinguno, cboConfiguracion);
            rbClases = AgregarOpcion("Solo las clases, sin horas",
                "De la plantilla que elijas, trae cada docente con su materia y grupo; tú pones las horas." + sinNinguno, cboConfiguracion);
            rbBlanco = AgregarOpcion("En blanco",
                "Empieza vacío: agregas los docentes, las clases y las horas.", null);

            panelExcel = new Panel { Size = new Size(AnchoDialogo - XDetalle - XOpcion, 62) };
            Button btnElegirExcel = EstiloHorarios.CrearBoton("Elegir el archivo...", new Point(0, 0), new Size(170, 32));
            btnElegirExcel.Click += (s, e) => ElegirExcel();
            lblArchivoExcel = PiezasConfiguracion.Texto("Ningún archivo elegido.", new Point(180, 7), new Size(panelExcel.Width - 180, 20), 9.5F, PiezasConfiguracion.TextoGris);
            lblArchivoExcel.AutoEllipsis = true;
            LinkLabel lnkPlantilla = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9F),
                Location = new Point(0, 40),
                Text = "Descargar la plantilla para llenarla (una hoja por laboratorio)..."
            };
            lnkPlantilla.LinkClicked += (s, e) => DescargarPlantilla();
            panelExcel.Controls.Add(btnElegirExcel);
            panelExcel.Controls.Add(lblArchivoExcel);
            panelExcel.Controls.Add(lnkPlantilla);
            Controls.Add(panelExcel);
            rbExcel = AgregarOpcion("Desde un Excel en cuadrícula",
                "Una hoja por laboratorio, con los días y las horas; en cada casilla la materia, el grupo y la persona docente registrados. "
                + "Si algo no está registrado, dice qué y en qué casilla, y no crea nada.", panelExcel);

            lblNombre = EstiloHorarios.CrearEtiqueta("Nombre", Point.Empty);
            txtNombre = EstiloHorarios.CrearTexto(Point.Empty, 300);
            txtNombre.MaxLength = CicloPreliminarDatos.LargoNombre;
            txtNombre.Text = CicloPreliminarDatos.NombrePreparacion;
            Controls.Add(lblNombre);
            Controls.Add(txtNombre);

            if (borrador != null)
            {
                // Del alto que pide su texto: un nombre largo lo hace de tres renglones.
                lblAviso = PiezasConfiguracion.AvisoAmbar(
                    "Ya hay un horario en preparación («" + borrador.Nombre + "», " + RevisionCiclo.TextoClases(borrador.Clases)
                    + "). Si sigues, se descarta; para conservarlo, cancela y usa «Más acciones», «Guardar como plantilla nueva...».",
                    Point.Empty, AnchoDentro);
                Controls.Add(lblAviso);
            }

            // Sin error no ocupa nada: MostrarError le da el alto que pide.
            lblError = EstiloAdmin.LineaError(Point.Empty, AnchoDentro, 0);
            Controls.Add(lblError);
            btnCrear = EstiloAdmin.BotonesDialogo(this, borrador == null ? "Crear" : "Descartar el otro y crear", Crear);
            btnCancelar = (Button)CancelButton;

            bool hayDeDonde = cboConfiguracion.Items.Count > 0;
            rbCopiar.Enabled = hayDeDonde;
            rbClases.Enabled = hayDeDonde;
            if (hayDeDonde)
            {
                List<ItemDeConfiguracion> items = cboConfiguracion.Items.Cast<ItemDeConfiguracion>().ToList();
                cboConfiguracion.SelectedItem = items.FirstOrDefault(i => elegida.HasValue && i.Ciclo != null && i.Ciclo.Id == elegida.Value)
                    ?? items.FirstOrDefault(i => i.EnUso) ?? items[0];
                // Lo mas comun: copiar el horario del ciclo anterior.
                rbCopiar.Checked = true;
            }
            else
            {
                rbBlanco.Checked = true;
            }

            if (!string.IsNullOrEmpty(excel))
            {
                PonerExcel(excel);
                rbExcel.Checked = true;
            }

            Acomodar();
            ActiveControl = rbExcel.Checked ? (Control)txtNombre : hayDeDonde ? (Control)cboConfiguracion : txtNombre;
        }

        private void PonerExcel(string ruta)
        {
            rutaExcel = ruta;
            lblArchivoExcel.Text = Path.GetFileName(ruta);
            lblArchivoExcel.ForeColor = Color.Black;
            if (txtNombre.Text == CicloPreliminarDatos.NombrePreparacion)
            {
                string nombre = Path.GetFileNameWithoutExtension(ruta).Trim();
                if (CicloPreliminarDatos.ErrorDeNombre(nombre) == null)
                {
                    txtNombre.Text = nombre;
                }
            }
        }

        private void ElegirExcel()
        {
            using (OpenFileDialog dialogo = new OpenFileDialog
            {
                Title = "Elegir el Excel con el horario",
                Filter = "Libro de Excel (*.xlsx)|*.xlsx;*.xlsm|Todos los archivos (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    PonerExcel(dialogo.FileName);
                    rbExcel.Checked = true;
                    MostrarError("");
                }
            }
        }

        /// <summary>La plantilla en blanco: una hoja por laboratorio en uso y la de lo registrado.</summary>
        private void DescargarPlantilla()
        {
            string nombre = string.IsNullOrWhiteSpace(txtNombre.Text) ? CicloPreliminarDatos.NombrePreparacion : txtNombre.Text.Trim();
            byte[] libro;
            Cursor = Cursors.WaitCursor;
            try
            {
                libro = HorarioEnExcel.Crear(null, datos.LeerCatalogosDeHorario(), nombre);
            }
            catch (Exception ex)
            {
                MostrarError(PiezasConfiguracion.TextoDeFallo("DialogoCrearHorario.DescargarPlantilla", "No se pudo preparar la plantilla de Excel", ex));
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            string guardado = ExcelDeHorario.Guardar(this, libro, nombre);
            if (guardado != null)
            {
                PonerExcel(guardado);
                rbExcel.Checked = true;
                MostrarError("");
                lblArchivoExcel.Text = Path.GetFileName(guardado) + " (llénalo, guárdalo y pulsa «Crear»)";
            }
        }

        /// <summary>Si hoy se da alguna clase. Si no se puede leer, el dialogo abre igual, sin esa opcion.</summary>
        private bool HayClasesHoy()
        {
            try
            {
                return datos.ContarClasesDelDia(hoy) > 0;
            }
            catch (Exception ex)
            {
                Log.Error("DialogoCrearHorario: contar las clases de hoy", ex);
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            List<DrawingFont> letras = disposing ? PiezasConfiguracion.LetrasDe(this, Font) : new List<DrawingFont>();
            base.Dispose(disposing);
            foreach (DrawingFont letra in letras)
            {
                letra.Dispose();
            }
        }

        /// <summary>Una opcion: su titulo y lo que explica que trae. Pulsar la explicacion tambien la elige.</summary>
        private RadioButton AgregarOpcion(string titulo, string nota, Control detalle)
        {
            RadioButton radio = new RadioButton
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                Text = titulo
            };
            radio.CheckedChanged += (s, e) =>
            {
                if (radio.Checked)
                {
                    Acomodar();
                }
            };
            Label etiqueta = PiezasConfiguracion.Texto(nota, Point.Empty, new Size(AnchoDialogo - XDetalle - XOpcion, 18), 9F, PiezasConfiguracion.TextoGris);
            // Con holgura: la etiqueta parte el renglon antes que la medida, y el final se perdia.
            etiqueta.Height = PiezasConfiguracion.AltoDe(nota, etiqueta.Font, etiqueta.Width - 24) + 2;
            etiqueta.Click += (s, e) =>
            {
                if (radio.Enabled)
                {
                    radio.Checked = true;
                }
            };
            Controls.Add(radio);
            Controls.Add(etiqueta);
            opciones.Add(new OpcionDeInicio { Radio = radio, Nota = etiqueta, Detalle = detalle });
            return radio;
        }

        /// <summary>Todo de arriba abajo, con lo que pide la opcion elegida, y el dialogo a esa medida.</summary>
        private void Acomodar()
        {
            int y = 116;
            int orden = 0;
            cboConfiguracion.Visible = rbCopiar.Checked || rbClases.Checked;
            panelExcel.Visible = rbExcel.Checked;
            // Las opciones seguidas: el combo en medio atrapaba las flechas y sin raton no se llegaba a las demas.
            foreach (OpcionDeInicio opcion in opciones)
            {
                opcion.Radio.TabIndex = orden++;
            }

            foreach (OpcionDeInicio opcion in opciones)
            {
                opcion.Radio.Location = new Point(XOpcion, y);
                y += 22;
                opcion.Nota.Location = new Point(XDetalle, y);
                y += opcion.Nota.Height + 4;
                if (opcion.Radio.Checked && opcion.Detalle != null)
                {
                    opcion.Detalle.Location = new Point(XDetalle, y);
                    opcion.Detalle.TabIndex = orden++;
                    y += opcion.Detalle.Height + 6;
                }

                y += 6;
            }

            lblNombre.Location = new Point(XOpcion, y + 2);
            y += 24;
            txtNombre.Location = new Point(XOpcion, y);
            txtNombre.TabIndex = orden++;
            y += txtNombre.Height + 12;
            if (lblAviso != null)
            {
                lblAviso.Location = new Point(XOpcion, y);
                y += lblAviso.Height + 8;
            }

            lblError.Location = new Point(XOpcion, y);
            y += lblError.Height > 0 ? lblError.Height + 6 : 0;
            btnCrear.TabIndex = orden++;
            btnCancelar.TabIndex = orden;

            ClientSize = new Size(AnchoDialogo, y + 40 + 16 + EstiloAdmin.AireBajoBotones);
            btnCrear.Top = y;
            btnCancelar.Top = y;
            if (IsHandleCreated)
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                if (Bottom > area.Bottom)
                {
                    Top = Math.Max(area.Top, area.Bottom - Height);
                }
            }
        }

        /// <summary>El error en rojo, con el alto que pide: un fallo lleva su causa y que hacer. Vacio, no ocupa nada.</summary>
        private void MostrarError(string texto)
        {
            lblError.Text = texto ?? "";
            lblError.Height = string.IsNullOrEmpty(texto) ? 0 : PiezasConfiguracion.AltoDe(texto, lblError.Font, lblError.Width - 24) + 4;
            Acomodar();
        }

        private void Crear()
        {
            MostrarError("");
            string error = CicloPreliminarDatos.ErrorDeNombre(txtNombre.Text);
            if (error != null)
            {
                MostrarError(error);
                txtNombre.Focus();
                return;
            }

            if (rbExcel.Checked)
            {
                CrearConExcel();
                return;
            }

            FuentePreparacion fuente;
            string deDonde = null;
            if (rbBlanco.Checked)
            {
                fuente = FuentePreparacion.EnBlanco();
            }
            else
            {
                ItemDeConfiguracion item = cboConfiguracion.SelectedItem as ItemDeConfiguracion;
                if (item == null)
                {
                    MostrarError("Elige la plantilla o el horario.");
                    cboConfiguracion.Focus();
                    return;
                }

                if (item.EnUso)
                {
                    // Como esta hoy, tambien de los laboratorios deshabilitados.
                    fuente = rbClases.Checked ? FuentePreparacion.ClasesDelDia(hoy) : FuentePreparacion.DelDia(hoy, null, true);
                    deDonde = item.Ciclo == null ? "del horario de hoy" : "de «" + item.Ciclo.Nombre + "», como está hoy";
                }
                else
                {
                    fuente = rbClases.Checked ? FuentePreparacion.ClasesDeConfiguracion(item.Ciclo.Id) : FuentePreparacion.DeConfiguracion(item.Ciclo.Id);
                    deDonde = "de «" + item.Ciclo.Nombre + "»";
                }
            }

            if (borrador != null && !DialogoConfirmacion.Preguntar(this, "Descartar el horario en preparación", "¿Descartar «" + borrador.Nombre + "»?",
                PiezasConfiguracion.SeBorraCon(borrador.Clases) + ". Las plantillas guardadas y el horario que se usa no cambian.", "Descartar"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                Resultado = datos.Preparar(txtNombre.Text.Trim(), fuente, borrador != null, idAdministrador());
                DeDonde = deDonde;
                SinHoras = rbClases.Checked;
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                HuboFallo = true;
                MostrarError(PiezasConfiguracion.TextoDeFallo("DialogoCrearHorario.Crear", "No se pudo crear el horario", ex));
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// "Desde un Excel": lo lee y lo revisa contra lo registrado antes de crear nada; con
        /// algo por corregir lo dice (DialogoProblemasExcel) y no crea. Si todo esta bien, crea
        /// el horario en blanco y le carga el Excel.
        /// </summary>
        private void CrearConExcel()
        {
            if (rutaExcel == null)
            {
                MostrarError("Elige el archivo de Excel (o descarga la plantilla para llenarla).");
                return;
            }

            string archivo = Path.GetFileName(rutaExcel);
            LecturaDeHorario lectura;
            string error;
            Cursor = Cursors.WaitCursor;
            try
            {
                lectura = ExcelDeHorario.Leer(rutaExcel, datos, null, out error);
            }
            catch (Exception ex)
            {
                MostrarError(PiezasConfiguracion.TextoDeFallo("DialogoCrearHorario.LeerExcel", "No se pudo revisar el Excel", ex));
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (lectura == null)
            {
                MostrarError(error);
                return;
            }

            if (!lectura.SePuedeCargar)
            {
                using (DialogoProblemasExcel problemas = new DialogoProblemasExcel(archivo, lectura))
                {
                    problemas.ShowDialog(this);
                }

                MostrarError("No se creó nada: «" + archivo + "» tiene " + (lectura.Problemas.Count == 1 ? "una cosa" : lectura.Problemas.Count + " cosas")
                    + " por corregir. Corrígelo, guárdalo y vuelve a pulsar «" + btnCrear.Text + "».");
                return;
            }

            // Con choques o laboratorios deshabilitados, que lo vea antes; si no, «Crear» basta.
            if (lectura.Avisos.Count > 0)
            {
                using (DialogoConfirmarExcel confirmar = new DialogoConfirmarExcel(archivo, txtNombre.Text.Trim(), lectura, true))
                {
                    if (confirmar.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                }
            }

            if (borrador != null && !DialogoConfirmacion.Preguntar(this, "Descartar el horario en preparación", "¿Descartar «" + borrador.Nombre + "»?",
                PiezasConfiguracion.SeBorraCon(borrador.Clases) + ". Las plantillas guardadas y el horario que se usa no cambian.", "Descartar"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                Resultado = datos.Preparar(txtNombre.Text.Trim(), FuentePreparacion.EnBlanco(), borrador != null, idAdministrador());
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                HuboFallo = true;
                MostrarError(PiezasConfiguracion.TextoDeFallo("DialogoCrearHorario.CrearConExcel", "No se pudo crear el horario", ex));
                return;
            }

            try
            {
                ExcelCargado = datos.CargarDesdeExcel(Resultado.CicloId, lectura.Laboratorios.Select(l => l.Id).ToList(), lectura.Clases, idAdministrador());
            }
            catch (Exception ex)
            {
                // Ya se creo (en blanco): se queda, y la pestana dice que el Excel no entro.
                FalloDelExcel = PiezasConfiguracion.TextoDeFallo("DialogoCrearHorario.CargarExcel",
                    "Se creó «" + Resultado.Nombre + "» en blanco, pero no se pudo cargar el Excel", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            DeDonde = "de «" + archivo + "»";
            SinHoras = false;
            DialogResult = DialogResult.OK;
        }
    }

    // =========================================================================
    // C2b. Ver un horario, solo para verlo
    // =========================================================================

    /// <summary>
    /// C2b. "Ver" (o doble clic) en la lista de plantillas: una plantilla o un horario aplicado en
    /// su propia ventana (pedido del usuario, 2026-11-04), sin pestanas ni pasos: solo la semana
    /// por laboratorio y "Ver docentes y clases". No cambia nada. Por dentro es la misma vista
    /// de la preparacion (PestanaProximoCiclo.AbrirSoloParaVer), con lo que escribe oculto.
    /// </summary>
    internal class VentanaVerHorario : Form
    {
        /// <exception cref="Exception">Si no se puede leer: lo dice quien la abre.</exception>
        public VentanaVerHorario(int idConfiguracion, Func<int> idAdministrador)
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon = (Icon)new System.ComponentModel.ComponentResourceManager(typeof(PrincipalAdministrador)).GetObject("$this.Icon");
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            // La cuadricula necesita alto: casi toda la pantalla, y se puede maximizar.
            Size = new Size(Math.Min(1500, area.Width - 40), area.Height - 30);
            MinimumSize = new Size(Math.Min(1000, area.Width - 40), Math.Min(560, area.Height - 30));
            PestanaProximoCiclo vista = new PestanaProximoCiclo(idAdministrador, null) { Dock = DockStyle.Fill };
            Controls.Add(vista);
            try
            {
                CicloPreliminar ciclo = vista.AbrirSoloParaVer(idConfiguracion);
                Text = (ciclo.Aplicada ? "Horario aplicado «" : "Plantilla guardada «") + ciclo.Nombre + "»";
            }
            catch
            {
                Dispose();
                throw;
            }

            CancelButton = vista.BotonCerrarVentana;
            // Antes de ensenarse, los controles dicen que no se ven: la fila se coloca otra vez.
            Shown += (s, e) => vista.AcomodarAbierta();
        }
    }

    // =========================================================================
    // C2c. Docentes y clases de un horario, solo para verlos
    // =========================================================================

    /// <summary>
    /// C2c. "Ver docentes y clases" de una plantilla o de un horario aplicado: la tabla en su
    /// propia ventana (pedido del usuario, 2026-11-04), de cada docente con sus clases, con
    /// filtro por docente y buscador. No cambia nada.
    /// </summary>
    internal class VentanaDocentesYClases : Form
    {
        private readonly CicloLeido leido;
        private readonly ComboBox cboDocente;
        private readonly TextBox txtBuscar;
        private readonly DataGridView dgvClases;
        private readonly Label lblCuenta;
        private readonly DrawingFont fuenteNegrita = new DrawingFont("Century Gothic", 10F, FontStyle.Bold);

        public VentanaDocentesYClases(CicloLeido leido)
        {
            this.leido = leido;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int ancho = Math.Min(1100, area.Width - 40);
            int alto = Math.Min(680, area.Height - 60);
            EstiloHorarios.PrepararDialogo(this, "Docentes y clases de «" + leido.Ciclo.Nombre + "»", new Size(ancho, alto));
            // Con muchas clases se agradece mas alto: esta si se puede agrandar.
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon = (Icon)new System.ComponentModel.ComponentResourceManager(typeof(PrincipalAdministrador)).GetObject("$this.Icon");
            MaximizeBox = true;
            MinimumSize = new Size(Math.Min(760, ancho), Math.Min(420, alto));

            Label titulo = EstiloAdmin.TituloDialogo("Docentes y clases", ancho - 48);
            string texto = "«" + leido.Ciclo.Nombre + "»: " + RevisionCiclo.Resumen(leido.Ciclo) + "."
                + (leido.Ciclo.Aplicada ? " Así se aplicó: lo que se cambió después en Horario semanal no sale aquí." : "");
            Label explicacion = PiezasConfiguracion.Texto(texto, new Point(24, 50), new Size(ancho - 48, 20), 9.5F, PiezasConfiguracion.TextoGris);
            explicacion.Height = PiezasConfiguracion.AltoDe(texto, explicacion.Font, explicacion.Width - 24) + 4;
            explicacion.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            int fila = explicacion.Bottom + 8;
            Label lblDocente = EstiloHorarios.CrearEtiqueta("Docente", new Point(24, fila + 4));
            cboDocente = EstiloHorarios.CrearCombo(new Point(24 + lblDocente.PreferredWidth + 8, fila), 330);
            cboDocente.DrawMode = DrawMode.OwnerDrawFixed;
            cboDocente.ItemHeight = 20;
            cboDocente.MaxDropDownItems = 20;
            cboDocente.DrawItem += PestanaProximoCiclo.DibujarDocenteFiltro;
            List<OpcionCombo> opciones = PestanaProximoCiclo.OpcionesDeDocentes(leido);
            foreach (OpcionCombo opcion in opciones)
            {
                cboDocente.Items.Add(opcion);
            }

            cboDocente.DropDownWidth = Math.Max(cboDocente.Width, opciones.Max(o => TextRenderer.MeasureText(o.Texto, cboDocente.Font).Width) + 36 + SystemInformation.VerticalScrollBarWidth);
            cboDocente.SelectedIndex = 0;
            cboDocente.SelectedIndexChanged += (s, e) => Pintar();

            Label lblBuscar = EstiloHorarios.CrearEtiqueta("Buscar", new Point(cboDocente.Right + 18, fila + 4));
            txtBuscar = EstiloHorarios.CrearTexto(new Point(cboDocente.Right + 18 + lblBuscar.PreferredWidth + 8, fila), 220);
            EstiloHorarios.PonerTextoGuia(txtBuscar, "Materia o grupo");
            txtBuscar.TextChanged += (s, e) => Pintar();

            int arriba = fila + 36;
            dgvClases = EstiloHorarios.CrearGrid(new Point(24, arriba), new Size(ancho - 48, alto - 72 - arriba));
            dgvClases.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgvClases.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            dgvClases.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            dgvClases.Columns.Add("Docente", "Docente");
            dgvClases.Columns.Add("Materia", "Materia");
            dgvClases.Columns.Add("Grupo", "Grupo");
            dgvClases.Columns.Add("Horario", "Horario");
            EstiloHorarios.Pesos(dgvClases, 24, 28, 9, 39);

            lblCuenta = PiezasConfiguracion.Texto("", new Point(24, alto - 56), new Size(ancho - 48 - 140, 40), 9.5F, PiezasConfiguracion.TextoGris);
            lblCuenta.TextAlign = ContentAlignment.MiddleLeft;
            lblCuenta.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Button cerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(ancho - 24 - 120, alto - 56), new Size(120, 40));
            cerrar.DialogResult = DialogResult.Cancel;
            cerrar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            Controls.Add(titulo);
            Controls.Add(explicacion);
            Controls.Add(lblDocente);
            Controls.Add(cboDocente);
            Controls.Add(lblBuscar);
            Controls.Add(txtBuscar);
            Controls.Add(dgvClases);
            Controls.Add(lblCuenta);
            Controls.Add(cerrar);
            CancelButton = cerrar;
            Pintar();
            ActiveControl = dgvClases;
        }

        protected override void Dispose(bool disposing)
        {
            List<DrawingFont> letras = disposing ? PiezasConfiguracion.LetrasDe(this, Font) : new List<DrawingFont>();
            base.Dispose(disposing);
            foreach (DrawingFont letra in letras)
            {
                letra.Dispose();
            }

            if (disposing)
            {
                fuenteNegrita.Dispose();
            }
        }

        /// <summary>Las clases que pasan el docente y la busqueda, de cada docente en orden alfabetico.</summary>
        private void Pintar()
        {
            OpcionCombo docente = cboDocente.SelectedItem as OpcionCombo;
            int idDocente = docente == null ? 0 : docente.Id;
            List<CargaCiclo> vivas = leido.Cargas.Where(c => !c.Quitada).ToList();
            List<CargaCiclo> clases = vivas
                .Where(c => idDocente == 0 || (idDocente == -1 ? !c.ProfesorId.HasValue : c.ProfesorId == idDocente))
                .Where(c => EstiloAdmin.Coincide(EstiloAdmin.Normalizar(c.Materia + " " + c.Grupo), txtBuscar.Text))
                .OrderBy(c => c.ProfesorId.HasValue ? 0 : 1)
                .ThenBy(c => c.Profesor, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(c => c.Materia, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(c => c.Grupo, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            dgvClases.SuspendLayout();
            dgvClases.Rows.Clear();
            foreach (CargaCiclo carga in clases)
            {
                string nombre = carga.ProfesorId.HasValue
                    ? carga.Profesor + (leido.ConBajas && !carga.ProfesorActivo ? " (de baja)" : "")
                    : "Sin docente";
                string horario = carga.Franjas.Count == 0 ? "Sin horario"
                    : string.Join(Environment.NewLine, carga.Franjas.OrderBy(f => f.DiaSemana).ThenBy(f => f.Inicio).Select(f => f.Descripcion));
                int i = dgvClases.Rows.Add(nombre, carga.Materia, carga.Grupo, horario);
                DataGridViewRow fila = dgvClases.Rows[i];
                PestanaProximoCiclo.PintarCeldaDocente(fila.Cells["Docente"], carga, fuenteNegrita);
                if (carga.Franjas.Count == 0)
                {
                    fila.Cells["Horario"].Style.ForeColor = PiezasConfiguracion.TextoAmbar;
                    fila.Cells["Horario"].Style.SelectionForeColor = PiezasConfiguracion.TextoAmbar;
                }
            }

            dgvClases.ResumeLayout();
            dgvClases.ClearSelection();
            lblCuenta.Text = vivas.Count == 0 ? "Este horario no tiene clases."
                : clases.Count == 0 ? "Ninguna clase coincide con lo que elegiste."
                : clases.Count == vivas.Count ? RevisionCiclo.TextoClases(vivas.Count) + "."
                : clases.Count + " de " + RevisionCiclo.TextoClases(vivas.Count) + ".";
        }
    }

    // =========================================================================
    // C3. Nombre
    // =========================================================================

    /// <summary>C3. "Nombre": para Guardar como, Renombrar y Cambiar nombre. Hace la operacion al pulsar su boton.</summary>
    internal class DialogoNombreConfiguracion : Form
    {
        private readonly TextBox txtNombre;
        private readonly Label lblError;
        private readonly Action<string> guardar;

        /// <summary>El nombre con que quedo.</summary>
        public string Nombre { get; private set; }

        /// <summary>Fallo la operacion (se dijo aqui): al cancelar, la pestana vuelve a leer.</summary>
        public bool HuboFallo { get; private set; }

        /// <param name="guardar">La operacion con el nombre (lanza ErrorDeDatos con un nombre repetido): el dialogo sigue abierto si falla.</param>
        public DialogoNombreConfiguracion(string titulo, string nombre, string textoBoton, Action<string> guardar)
        {
            this.guardar = guardar;
            // Alto: un fallo con su causa y que hacer ocupa hasta 5 renglones de 432.
            EstiloHorarios.PrepararDialogo(this, titulo, new Size(480, 250));
            Controls.Add(EstiloAdmin.TituloDialogo(titulo, 432));
            Controls.Add(EstiloHorarios.CrearEtiqueta("Nombre", new Point(24, 66)));
            txtNombre = EstiloHorarios.CrearTexto(new Point(100, 62), 300);
            txtNombre.MaxLength = CicloPreliminarDatos.LargoNombre;
            txtNombre.Text = nombre ?? "";
            Controls.Add(txtNombre);
            lblError = EstiloAdmin.LineaError(new Point(24, 98), 432, 88);
            lblError.AutoEllipsis = true;
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, textoBoton, Guardar);
            ActiveControl = txtNombre;
            txtNombre.SelectAll();
        }

        protected override void Dispose(bool disposing)
        {
            List<DrawingFont> letras = disposing ? PiezasConfiguracion.LetrasDe(this, Font) : new List<DrawingFont>();
            base.Dispose(disposing);
            foreach (DrawingFont letra in letras)
            {
                letra.Dispose();
            }
        }

        private void Guardar()
        {
            lblError.Text = "";
            string error = CicloPreliminarDatos.ErrorDeNombre(txtNombre.Text);
            if (error != null)
            {
                lblError.Text = error;
                txtNombre.Focus();
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                guardar(txtNombre.Text.Trim());
                Nombre = txtNombre.Text.Trim();
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                HuboFallo = true;
                lblError.Text = PiezasConfiguracion.TextoDeFallo("DialogoNombreConfiguracion (" + Text + ")", "No se pudo " + (Text.StartsWith("Guardar", StringComparison.Ordinal) ? "guardar" : "cambiar el nombre"), ex);
                txtNombre.Focus();
                txtNombre.SelectAll();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    // =========================================================================
    // C4. Clase del horario
    // =========================================================================

    /// <summary>
    /// C4. "Clase del horario": docente, grupo, materia y sus horarios en los
    /// laboratorios, con los choques en vivo (RevisionCiclo.ChoquesSi). Sirve
    /// para una clase nueva (AgregarClase), para cambiar una (CambiarClase) y,
    /// desde cuadros libres, para agregar esas horas a una que ya esta
    /// (PonerHoras). Encima de otra clase del mismo laboratorio, al guardar
    /// pregunta si la reemplaza (ConfirmarReemplazoEnPreparacion: se le quita
    /// solo lo que se cruza). Los choques (docente o grupo en otro laboratorio)
    /// no impiden guardar: impiden aplicar.
    /// </summary>
    internal class DialogoClaseDelHorario : Form
    {
        private const int Ancho = 640;
        private const int AltoMinimo = 380;
        private const int AltoMaximo = 600;
        private const int AltoRenglon = 32;
        private const int RenglonesSinBarra = 6;
        private const int AltoLibre = 20;
        private const int AltoAgregar = 24;
        private const int LineasDeChoques = 4;
        // La linea de error: un fallo con su causa y que hacer ocupa hasta 4 renglones de 592.
        private const int AltoError = 66;
        // La nota gris de debajo de los choques: dos renglones de 592.
        private const int AltoNota = 38;
        // Laboratorio (230), dia (110), de y a (90 cada uno) y "Quitar": caben en los 592 del dialogo.
        private static readonly int[] Columnas = { 0, 236, 352, 448 };

        private readonly CicloPreliminarDatos datos;
        private readonly HorariosDatos horarios = new HorariosDatos();
        private readonly CicloLeido leido;
        // La que se cambia ("Cambiar la clase..."); null si es nueva.
        private readonly CargaCiclo carga;
        private readonly Func<int> idAdministrador;
        private readonly bool desdeCuadros;
        private readonly int laboratorioPropuesto;

        private readonly Label lblTitulo;
        private RadioButton rbNueva;
        private RadioButton rbExistente;
        private ComboBox cboExistente;
        private readonly Label lblDocente;
        private readonly ComboBox cboDocente;
        private readonly Label lblGrupo;
        private readonly ComboBox cboGrupo;
        private readonly LinkLabel lnkNuevoGrupo;
        private readonly Label lblMateria;
        private readonly ComboBox cboMateria;
        private readonly Label lblHorario;
        private readonly Label[] encabezados;
        private readonly Panel panelRenglones;
        private readonly LinkLabel lnkAgregar;
        private readonly Label lblLibre;
        private readonly Label lblChoques;
        private readonly Label lblNota;
        private readonly Label lblError;
        private readonly Button btnGuardar;
        private readonly Button btnCancelar;
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };
        // Una sola para todos los "Quitar" (se crean y se quitan renglones con cada clic).
        private readonly DrawingFont fuenteQuitar = new DrawingFont("Century Gothic", 9F);

        private readonly List<Renglon> renglones = new List<Renglon>();
        private Renglon conFoco;
        private List<OpcionCombo> laboratorios = new List<OpcionCombo>();
        // El nombre de cada laboratorio tal como esta en la base (FranjaCiclo.Laboratorio).
        private readonly Dictionary<int, string> nombreDeLaboratorio = new Dictionary<int, string>();
        private List<OpcionDeCarrera> grupos = new List<OpcionDeCarrera>();
        private List<OpcionDeCarrera> materias = new List<OpcionDeCarrera>();
        private bool colocando;

        private sealed class Renglon
        {
            public ComboBox Laboratorio;
            public ComboBox Dia;
            public ComboBox De;
            public ComboBox A;
            public LinkLabel Quitar;

            public IEnumerable<Control> Controles
            {
                get { return new Control[] { Laboratorio, Dia, De, A, Quitar }; }
            }
        }

        private sealed class ItemDeCarga
        {
            public CargaCiclo Carga;

            public override string ToString()
            {
                return Carga.Nombre + " (" + (Carga.ProfesorId.HasValue ? Carga.ProfesorCorto : "sin docente") + ")";
            }
        }

        /// <summary>La clase que quedo (la nueva, la cambiada o a la que se le pusieron las horas): para el estado.</summary>
        public int IdCarga { get; private set; }

        /// <summary>Fallo al guardar (se dijo aqui): al cancelar, la pestana vuelve a leer.</summary>
        public bool HuboFallo { get; private set; }

        /// <param name="carga">La clase que se cambia; null para una nueva.</param>
        /// <param name="rangos">Cuadros libres de la cuadricula (ya vienen puestos como horarios); null si no se viene de ahi.</param>
        /// <param name="laboratorio">El laboratorio que se mira en la semana: el de los cuadros libres y el que se propone en un horario nuevo.</param>
        public DialogoClaseDelHorario(CicloPreliminarDatos datos, CicloLeido leido, CargaCiclo carga, List<RangoHueco> rangos, int laboratorio, Func<int> idAdministrador)
        {
            this.datos = datos;
            this.leido = leido;
            this.carga = carga;
            this.idAdministrador = idAdministrador;
            desdeCuadros = rangos != null && rangos.Count > 0 && carga == null;
            laboratorioPropuesto = laboratorio;

            EstiloHorarios.PrepararDialogo(this, "Clase del horario", new Size(Ancho, AltoMinimo));
            lblTitulo = EstiloAdmin.TituloDialogo(carga != null ? carga.Nombre : "Clase nueva", Ancho - 48);
            Controls.Add(lblTitulo);

            if (desdeCuadros)
            {
                DrawingFont fuenteOpcion = new DrawingFont("Century Gothic", 10F);
                rbNueva = new RadioButton { AutoSize = true, Checked = true, Font = fuenteOpcion, Text = "Una clase nueva" };
                rbExistente = new RadioButton { AutoSize = true, Font = fuenteOpcion, Text = "Agregar estas horas a una clase que ya está en este horario" };
                cboExistente = EstiloHorarios.CrearCombo(Point.Empty, 440);
                cboExistente.MaxDropDownItems = 16;
                foreach (CargaCiclo c in leido.Cargas.Where(c => !c.Quitada).OrderBy(c => c.Materia, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(c => c.Grupo, StringComparer.CurrentCultureIgnoreCase))
                {
                    cboExistente.Items.Add(new ItemDeCarga { Carga = c });
                }

                if (cboExistente.Items.Count > 0)
                {
                    cboExistente.SelectedIndex = 0;
                }

                rbExistente.Enabled = cboExistente.Items.Count > 0;
                cboExistente.Enabled = false;
                rbNueva.CheckedChanged += (s, e) => CambioDeOpcion();
                cboExistente.SelectedIndexChanged += (s, e) => CambioDeOpcion();
                Controls.Add(rbNueva);
                Controls.Add(rbExistente);
                Controls.Add(cboExistente);
            }

            lblDocente = EstiloHorarios.CrearEtiqueta("Docente", Point.Empty);
            // Con escritura: con muchos docentes se encuentra tecleando su nombre.
            cboDocente = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                DrawMode = DrawMode.OwnerDrawFixed,
                Font = new DrawingFont("Century Gothic", 10F),
                ItemHeight = 20,
                MaxDropDownItems = 16,
                Size = new Size(420, 26)
            };
            cboDocente.DrawItem += DibujarDocente;
            cboDocente.SelectedIndexChanged += (s, e) => RevisarEnVivo();
            cboDocente.TextChanged += (s, e) => RevisarEnVivo();

            lblGrupo = EstiloHorarios.CrearEtiqueta("Grupo", Point.Empty);
            cboGrupo = EstiloHorarios.CrearCombo(Point.Empty, 250);
            cboGrupo.MaxDropDownItems = 16;
            lnkNuevoGrupo = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9F),
                LinkColor = Color.DarkGreen,
                Text = "Nuevo grupo..."
            };
            lnkNuevoGrupo.LinkClicked += (s, e) => CrearGrupo();

            lblMateria = EstiloHorarios.CrearEtiqueta("Materia", Point.Empty);
            cboMateria = EstiloHorarios.CrearCombo(Point.Empty, 420);
            cboMateria.MaxDropDownItems = 16;

            lblHorario = EstiloHorarios.CrearEtiqueta("Horario", Point.Empty);
            DrawingFont fuenteEncabezado = new DrawingFont("Century Gothic", 9F);
            encabezados = new[] { "Laboratorio", "Día", "De", "A" }.Select(t => new Label
            {
                AutoSize = true,
                Font = fuenteEncabezado,
                ForeColor = PiezasConfiguracion.TextoGris,
                Text = t
            }).ToArray();

            panelRenglones = new Panel { AutoScroll = true, BackColor = Color.White };
            lblLibre = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.Gray,
                Size = new Size(560, AltoLibre - 2)
            };
            lnkAgregar = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                LinkColor = Color.DarkGreen,
                Text = "Agregar otro horario"
            };
            lnkAgregar.LinkClicked += (s, e) =>
            {
                Renglon ultimo = renglones.LastOrDefault();
                OpcionCombo lab = ultimo == null ? null : ultimo.Laboratorio.SelectedItem as OpcionCombo;
                Renglon nuevo = AgregarRenglon(lab != null ? lab.Id : laboratorioPropuesto, null, null, null);
                conFoco = nuevo;
                Colocar();
                nuevo.Dia.Focus();
            };
            panelRenglones.Controls.Add(lblLibre);
            panelRenglones.Controls.Add(lnkAgregar);

            lblChoques = PiezasConfiguracion.Texto("", Point.Empty, new Size(Ancho - 48, 0), 9F, PiezasConfiguracion.TextoError);
            lblNota = PiezasConfiguracion.Texto("Encima de otra clase de este laboratorio, pregunta si la reemplaza: se le quita solo lo que se cruza. "
                + "Se guarda aunque choque (docente o grupo en otro laboratorio): sale en rojo y no deja aplicar.",
                Point.Empty, new Size(Ancho - 48, AltoNota - 4), 8.5F, Color.Gray);
            lblError = EstiloAdmin.LineaError(Point.Empty, Ancho - 48, AltoError - 4);
            lblError.AutoEllipsis = true;

            Controls.Add(lblDocente);
            Controls.Add(cboDocente);
            Controls.Add(lblGrupo);
            Controls.Add(cboGrupo);
            Controls.Add(lnkNuevoGrupo);
            Controls.Add(lblMateria);
            Controls.Add(cboMateria);
            Controls.Add(lblHorario);
            foreach (Label encabezado in encabezados)
            {
                Controls.Add(encabezado);
            }

            Controls.Add(panelRenglones);
            Controls.Add(lblChoques);
            Controls.Add(lblNota);
            Controls.Add(lblError);
            btnGuardar = EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            btnCancelar = (Button)CancelButton;

            CargarCatalogos();
            if (carga != null)
            {
                foreach (FranjaCiclo f in carga.Franjas.OrderBy(f => f.DiaSemana).ThenBy(f => f.Inicio))
                {
                    AgregarRenglon(f.LaboratorioId, f.DiaSemana, f.Inicio, f.Fin);
                }
            }
            else if (desdeCuadros)
            {
                foreach (RangoHueco r in rangos.OrderBy(r => r.Dia).ThenBy(r => r.Inicio))
                {
                    // Hasta el final del dia: 24:00 no es una hora (se leeria 00:00).
                    AgregarRenglon(laboratorio, r.Dia, r.Inicio, r.Fin >= TimeSpan.FromHours(24) ? new TimeSpan(23, 59, 0) : r.Fin);
                }
            }
            else
            {
                // Una clase nueva desde la lista: un horario vacio en el laboratorio que se miraba.
                AgregarRenglon(laboratorio, null, null, null);
            }

            conFoco = renglones.FirstOrDefault();
            Colocar();
            RevisarEnVivo();
            ActiveControl = desdeCuadros ? (Control)rbNueva : cboDocente;
        }

        protected override void Dispose(bool disposing)
        {
            List<DrawingFont> letras = new List<DrawingFont>();
            if (disposing)
            {
                ayuda.Dispose();
                letras = PiezasConfiguracion.LetrasDe(this, Font);
            }

            base.Dispose(disposing);
            foreach (DrawingFont letra in letras)
            {
                letra.Dispose();
            }

            if (disposing)
            {
                fuenteQuitar.Dispose();
            }
        }

        /// <summary>Docentes, grupos, materias y laboratorios activos (con los desactivados donde ya tiene horas).</summary>
        private void CargarCatalogos()
        {
            List<OpcionCombo> profesores = new List<OpcionCombo>();
            try
            {
                profesores = horarios.ObtenerProfesores(carga == null ? null : carga.ProfesorId);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "DialogoClaseDelHorario.ObtenerProfesores", ex, "No se pudo leer la lista de docentes", Text);
            }

            cboDocente.Items.Add(new OpcionCombo { Id = 0, Texto = "Sin docente (decidirlo después)" });
            foreach (OpcionCombo profesor in profesores)
            {
                // La de baja que ya tiene la clase se ve como tal: la revision no deja aplicarla.
                bool deBaja = carga != null && leido.ConBajas && carga.ProfesorId == profesor.Id && !carga.ProfesorActivo;
                cboDocente.Items.Add(new OpcionCombo { Id = profesor.Id, Texto = profesor.Texto + (deBaja ? " (de baja)" : "") });
            }

            int anchoLista = cboDocente.Items.Cast<OpcionCombo>().Max(p => TextRenderer.MeasureText(p.Texto, cboDocente.Font).Width) + 36
                + SystemInformation.VerticalScrollBarWidth;
            cboDocente.DropDownWidth = Math.Max(cboDocente.Width, anchoLista);
            int docente = carga != null && carga.ProfesorId.HasValue ? carga.ProfesorId.Value : 0;
            for (int i = 0; i < cboDocente.Items.Count; i++)
            {
                if (((OpcionCombo)cboDocente.Items[i]).Id == docente)
                {
                    cboDocente.SelectedIndex = i;
                    break;
                }
            }

            try
            {
                grupos = horarios.GruposConCarrera();
                materias = horarios.MateriasConCarrera();
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "DialogoClaseDelHorario: grupos y materias", ex, "No se pudieron leer los grupos y las materias", Text);
            }

            EstiloHorarios.Rellenar(cboGrupo, grupos, carga == null ? (int?)null : carga.GrupoId);
            FiltrarMaterias(carga == null ? (int?)null : carga.MateriaId);
            cboGrupo.SelectedIndexChanged += (s, e) =>
            {
                FiltrarMaterias(null);
                RevisarEnVivo();
            };

            List<OpcionCombo> activos = new List<OpcionCombo>();
            try
            {
                activos = horarios.ObtenerLaboratoriosActivos();
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(EstiloHorarios.Dueno(this), "DialogoClaseDelHorario.ObtenerLaboratoriosActivos", ex, "No se pudieron leer los laboratorios", Text);
            }

            laboratorios = activos.Select(l => new OpcionCombo { Id = l.Id, Texto = EstiloHorarios.NombreLaboratorio(l.Texto) }).ToList();
            foreach (OpcionCombo l in activos)
            {
                nombreDeLaboratorio[l.Id] = l.Texto;
            }

            // Si la clase ya tiene horas en un laboratorio deshabilitado, sale con su nombre (la revision lo marca).
            if (carga != null)
            {
                foreach (FranjaCiclo f in carga.Franjas.Where(f => !nombreDeLaboratorio.ContainsKey(f.LaboratorioId)))
                {
                    nombreDeLaboratorio[f.LaboratorioId] = f.Laboratorio;
                    laboratorios.Add(new OpcionCombo { Id = f.LaboratorioId, Texto = EstiloHorarios.NombreLaboratorio(f.Laboratorio) + " (deshabilitado)" });
                }
            }
        }

        /// <summary>
        /// Las materias de la carrera del grupo elegido (y las que no tienen
        /// carrera), como DialogoClase.FiltrarMaterias. La de la clase que se
        /// cambia sale siempre.
        /// </summary>
        private void FiltrarMaterias(int? seleccionar)
        {
            OpcionDeCarrera grupo = cboGrupo.SelectedItem as OpcionDeCarrera;
            OpcionCombo elegida = cboMateria.SelectedItem as OpcionCombo;
            int? materia = seleccionar ?? (elegida == null ? (int?)null : elegida.Id);
            int? materiaDeLaClase = carga == null ? (int?)null : carga.MateriaId;

            List<OpcionDeCarrera> ofrecidas = materias;
            if (grupo != null && grupo.CarreraId.HasValue)
            {
                List<OpcionDeCarrera> deSuCarrera = materias.Where(m => !m.CarreraId.HasValue || m.CarreraId == grupo.CarreraId).ToList();
                if (deSuCarrera.Count > 0)
                {
                    ofrecidas = deSuCarrera;
                }
            }

            if (materia.HasValue && !ofrecidas.Any(m => m.Id == materia.Value))
            {
                OpcionDeCarrera suya = materia == materiaDeLaClase ? materias.FirstOrDefault(m => m.Id == materia.Value) : null;
                if (suya != null)
                {
                    ofrecidas = new List<OpcionDeCarrera>(ofrecidas) { suya };
                }
                else
                {
                    materia = null;
                }
            }

            EstiloHorarios.Rellenar(cboMateria, ofrecidas, materia);
        }

        private void CrearGrupo()
        {
            using (DialogoGrupo dialogo = new DialogoGrupo(0))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    grupos = horarios.GruposConCarrera();
                }
                catch (Exception ex)
                {
                    Log.Error("DialogoClaseDelHorario: releer los grupos", ex);
                }

                EstiloHorarios.Rellenar(cboGrupo, grupos, dialogo.IdGuardado);
            }
        }

        /// <summary>El cuadrito con el color del docente (el de sus clases en la cuadricula) y su nombre.</summary>
        private void DibujarDocente(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            // Mientras se escribe o se vuelve a llenar puede llegar un indice que ya no esta.
            OpcionCombo opcion = e.Index >= 0 && e.Index < cboDocente.Items.Count ? cboDocente.Items[e.Index] as OpcionCombo : null;
            if (opcion == null)
            {
                return;
            }
            int x = e.Bounds.X + 4;
            if (opcion.Id != 0)
            {
                Rectangle cuadro = new Rectangle(x, e.Bounds.Y + 3, 14, e.Bounds.Height - 6);
                using (SolidBrush relleno = new SolidBrush(CuadriculaSemanal.ColorDeProfesor(opcion.Id)))
                {
                    e.Graphics.FillRectangle(relleno, cuadro);
                }

                e.Graphics.DrawRectangle(Pens.Gray, cuadro);
            }

            x += 20;
            TextRenderer.DrawText(e.Graphics, opcion.Texto, e.Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x, e.Bounds.Height),
                e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        /// <summary>Un renglon de horario: laboratorio, dia, de y a, y "Quitar".</summary>
        private Renglon AgregarRenglon(int? laboratorio, int? dia, TimeSpan? de, TimeSpan? a)
        {
            Renglon r = new Renglon
            {
                Laboratorio = EstiloHorarios.CrearCombo(Point.Empty, 230),
                Dia = EstiloHorarios.CrearCombo(Point.Empty, 110),
                De = EstiloHorarios.CrearComboHoras(Point.Empty, de),
                A = EstiloHorarios.CrearComboHoras(Point.Empty, a),
                Quitar = new LinkLabel
                {
                    AutoSize = true,
                    Font = fuenteQuitar,
                    LinkColor = Color.DarkGreen,
                    Text = "Quitar"
                }
            };
            r.Laboratorio.MaxDropDownItems = 16;
            // Sin laboratorio (o uno que ya no esta), vacio: al guardar se pide.
            EstiloHorarios.Rellenar(r.Laboratorio, laboratorios, laboratorio);
            // De lunes a sabado (y el dia que ya tenia, si era otro), como "Cambiar horas".
            List<OpcionCombo> dias = TextosDelHorario.DiasParaElegir(dia);
            EstiloHorarios.Rellenar(r.Dia, dias, dia);
            r.Quitar.LinkClicked += (s, e) => QuitarRenglon(r);
            ayuda.SetToolTip(r.Quitar, "Quita este horario de la clase (al guardar).");
            foreach (Control c in r.Controles)
            {
                c.Enter += (s, e) =>
                {
                    if (conFoco != r)
                    {
                        conFoco = r;
                        Colocar();
                    }
                };
            }

            r.Laboratorio.SelectedIndexChanged += (s, e) => RevisarEnVivo();
            r.Dia.SelectedIndexChanged += (s, e) => RevisarEnVivo();
            r.De.TextChanged += (s, e) => RevisarEnVivo();
            r.A.TextChanged += (s, e) => RevisarEnVivo();
            r.De.SelectedIndexChanged += (s, e) => RevisarEnVivo();
            r.A.SelectedIndexChanged += (s, e) => RevisarEnVivo();
            foreach (Control c in r.Controles)
            {
                panelRenglones.Controls.Add(c);
            }

            renglones.Add(r);
            return r;
        }

        private void QuitarRenglon(Renglon r)
        {
            int indice = renglones.IndexOf(r);
            renglones.Remove(r);
            foreach (Control c in r.Controles)
            {
                panelRenglones.Controls.Remove(c);
                ayuda.SetToolTip(c, null);
                if (c == r.Quitar)
                {
                    // Su letra es la de todos los "Quitar": se suelta al cerrar.
                    c.Dispose();
                }
                else
                {
                    // Cada combo trae su letra (EstiloHorarios): se suelta con el.
                    PiezasConfiguracion.SoltarConSuLetra(c, Font);
                }
            }

            if (conFoco == r)
            {
                conFoco = renglones.Count == 0 ? null : renglones[Math.Min(indice, renglones.Count - 1)];
            }

            Colocar();
            RevisarEnVivo();
        }

        /// <summary>"Agregar estas horas a una clase que ya esta": se ocultan docente, grupo y materia, y el titulo dice la clase.</summary>
        private void CambioDeOpcion()
        {
            ItemDeCarga item = cboExistente == null ? null : cboExistente.SelectedItem as ItemDeCarga;
            cboExistente.Enabled = rbExistente.Checked;
            lblTitulo.Text = PonerHoras && item != null ? item.Carga.Nombre : "Clase nueva";
            Colocar();
            RevisarEnVivo();
        }

        private bool PonerHoras
        {
            get { return desdeCuadros && rbExistente != null && rbExistente.Checked; }
        }

        /// <summary>
        /// Coloca todo de arriba abajo segun lo que se ve: las opciones de los
        /// cuadros libres, los datos de la clase, los renglones (con la ayuda de
        /// las horas libres bajo el que tiene el foco), los choques, la nota y el
        /// error. El dialogo mide de 380 a 600 de alto: con mas de 6 renglones,
        /// los horarios llevan barra.
        /// </summary>
        private void Colocar()
        {
            if (colocando)
            {
                return;
            }

            colocando = true;
            SuspendLayout();
            try
            {
                int x = 24;
                int y = 56;
                if (desdeCuadros)
                {
                    rbNueva.Location = new Point(x, y);
                    rbExistente.Location = new Point(x, y + 24);
                    cboExistente.Location = new Point(x + 20, y + 50);
                    y += 88;
                }

                bool datosDeClase = !PonerHoras;
                foreach (Control c in new Control[] { lblDocente, cboDocente, lblGrupo, cboGrupo, lnkNuevoGrupo, lblMateria, cboMateria })
                {
                    c.Visible = datosDeClase;
                }

                if (datosDeClase)
                {
                    lblDocente.Location = new Point(x, y + 4);
                    cboDocente.Location = new Point(x + 96, y);
                    y += 36;
                    lblGrupo.Location = new Point(x, y + 4);
                    cboGrupo.Location = new Point(x + 96, y);
                    lnkNuevoGrupo.Location = new Point(cboGrupo.Right + 10, y + 5);
                    y += 36;
                    lblMateria.Location = new Point(x, y + 4);
                    cboMateria.Location = new Point(x + 96, y);
                    y += 42;
                }

                lblHorario.Location = new Point(x, y);
                y += 24;
                int[] columnas = Columnas;
                for (int i = 0; i < encabezados.Length; i++)
                {
                    encabezados[i].Location = new Point(x + columnas[i], y);
                    encabezados[i].Visible = renglones.Count > 0;
                }

                if (renglones.Count > 0)
                {
                    y += 20;
                }

                // Dentro del panel de los horarios (con su desplazamiento si tiene barra).
                int arriba = panelRenglones.AutoScrollPosition.Y;
                int yy = 0;
                string libre = conFoco == null ? null : TextoLibre(conFoco);
                lblLibre.Visible = false;
                foreach (Renglon r in renglones)
                {
                    r.Laboratorio.Location = new Point(columnas[0], yy + arriba);
                    r.Dia.Location = new Point(columnas[1], yy + arriba);
                    r.De.Location = new Point(columnas[2], yy + arriba);
                    r.A.Location = new Point(columnas[3], yy + arriba);
                    r.Quitar.Location = new Point(columnas[3] + 98, yy + arriba + 5);
                    yy += AltoRenglon;
                    if (r == conFoco && !string.IsNullOrEmpty(libre))
                    {
                        lblLibre.Text = libre;
                        lblLibre.Location = new Point(2, yy + arriba - 4);
                        lblLibre.Visible = true;
                        yy += AltoLibre - 2;
                    }
                }

                lnkAgregar.Text = renglones.Count == 0 ? "Agregar un horario" : "Agregar otro horario";
                lnkAgregar.Location = new Point(0, yy + arriba + 2);
                yy += AltoAgregar;
                int contenido = yy;

                // Los choques, en las lineas que pidan (hasta 4).
                int altoChoques = string.IsNullOrEmpty(lblChoques.Text) ? 0
                    : Math.Min(PiezasConfiguracion.AltoDe(lblChoques.Text, lblChoques.Font, lblChoques.Width) + 2, LineasDeChoques * (lblChoques.Font.Height + 1) + 2);
                int pie = (altoChoques > 0 ? altoChoques + 6 : 0) + AltoNota + AltoError + 56;

                // El panel de horarios: todo su contenido, salvo que pase de 6 renglones o del alto maximo.
                int maximoPanel = RenglonesSinBarra * AltoRenglon + AltoLibre + AltoAgregar;
                int altoPanel = Math.Min(contenido, maximoPanel);
                // Los 6 de debajo del panel tambien cuentan: si no, la linea de error pisaba los botones.
                if (y + altoPanel + 6 + pie > AltoMaximo)
                {
                    altoPanel = Math.Max(AltoRenglon + AltoAgregar, AltoMaximo - y - pie - 6);
                }

                // Con barra (mas de 6 renglones), mas ancho: la barra no tapa "Quitar" ni pide otra horizontal.
                panelRenglones.SetBounds(x, y, Ancho - 48 + (contenido > altoPanel ? SystemInformation.VerticalScrollBarWidth : 0), altoPanel);
                y += altoPanel + 6;

                lblChoques.Visible = altoChoques > 0;
                lblChoques.SetBounds(x, y, Ancho - 48, altoChoques);
                y += altoChoques > 0 ? altoChoques + 6 : 0;
                lblNota.Location = new Point(x, y);
                y += AltoNota;
                lblError.Location = new Point(x, y);
                y += AltoError;

                int alto = Math.Max(AltoMinimo, Math.Min(AltoMaximo, y + 56));
                ClientSize = new Size(Ancho, alto);
                btnCancelar.Location = new Point(Ancho - 24 - btnCancelar.Width, alto - 56);
                btnGuardar.Location = new Point(btnCancelar.Left - 10 - btnGuardar.Width, alto - 56);
                if (Visible)
                {
                    // Crece hacia abajo: a 1366x768 sus botones no deben quedar bajo la barra de tareas.
                    Rectangle zona = Screen.FromControl(this).WorkingArea;
                    if (Bottom > zona.Bottom)
                    {
                        Top = Math.Max(zona.Top, zona.Bottom - Height);
                    }
                }
            }
            finally
            {
                ResumeLayout();
                colocando = false;
            }
        }

        /// <summary>
        /// "Libre ese dia en ese laboratorio: de 07:00 a 08:00 y de 13:00 a
        /// 21:00.": de 07:00 a 21:00, sin lo que ya ocupa ese laboratorio ese dia
        /// en esta configuracion (sin esta misma clase, que se cambia entera) ni
        /// los otros renglones de este dialogo. Null si el renglon aun no tiene
        /// laboratorio y dia.
        /// </summary>
        private string TextoLibre(Renglon r)
        {
            OpcionCombo lab = r.Laboratorio.SelectedItem as OpcionCombo;
            OpcionCombo dia = r.Dia.SelectedItem as OpcionCombo;
            if (lab == null || dia == null)
            {
                return null;
            }

            List<KeyValuePair<TimeSpan, TimeSpan>> ocupado = leido.Cargas
                .Where(c => !c.Quitada && (carga == null || c.Id != carga.Id))
                .SelectMany(c => c.Franjas)
                .Where(f => f.LaboratorioId == lab.Id && f.DiaSemana == dia.Id)
                .Select(f => new KeyValuePair<TimeSpan, TimeSpan>(f.Inicio, f.Fin))
                .ToList();
            foreach (Renglon otro in renglones.Where(o => o != r))
            {
                FranjaCiclo f = FranjaDe(otro);
                if (f != null && f.LaboratorioId == lab.Id && f.DiaSemana == dia.Id)
                {
                    ocupado.Add(new KeyValuePair<TimeSpan, TimeSpan>(f.Inicio, f.Fin));
                }
            }

            List<string> libres = new List<string>();
            TimeSpan desde = TimeSpan.FromHours(EstiloHorarios.HoraApertura);
            TimeSpan cierre = TimeSpan.FromHours(EstiloHorarios.HoraCierre);
            foreach (KeyValuePair<TimeSpan, TimeSpan> o in ocupado.OrderBy(o => o.Key))
            {
                if (o.Key > desde)
                {
                    libres.Add("de " + HorariosDatos.Hora(desde) + " a " + HorariosDatos.Hora(o.Key < cierre ? o.Key : cierre));
                }

                if (o.Value > desde)
                {
                    desde = o.Value;
                }

                if (desde >= cierre)
                {
                    break;
                }
            }

            if (desde < cierre)
            {
                libres.Add("de " + HorariosDatos.Hora(desde) + " a " + HorariosDatos.Hora(cierre));
            }

            libres = libres.Where(t => !t.StartsWith("de " + HorariosDatos.Hora(cierre))).ToList();
            return libres.Count == 0
                ? "Ese día ese laboratorio no tiene horas libres en este horario."
                : "Libre ese día en ese laboratorio: " + RevisionCiclo.Enumerar(libres) + ".";
        }

        /// <summary>La franja de un renglon completo y bien escrito; null si le falta algo.</summary>
        private FranjaCiclo FranjaDe(Renglon r)
        {
            OpcionCombo lab = r.Laboratorio.SelectedItem as OpcionCombo;
            OpcionCombo dia = r.Dia.SelectedItem as OpcionCombo;
            TimeSpan de;
            TimeSpan a;
            if (lab == null || dia == null || !EstiloHorarios.LeerHora(r.De, out de) || !EstiloHorarios.LeerHora(r.A, out a) || a <= de)
            {
                return null;
            }

            string nombre;
            return new FranjaCiclo
            {
                LaboratorioId = lab.Id,
                Laboratorio = nombreDeLaboratorio.TryGetValue(lab.Id, out nombre) ? nombre : lab.Texto,
                DiaSemana = dia.Id,
                Inicio = de,
                Fin = a
            };
        }

        /// <summary>El docente elegido (o tecleado tal cual esta en la lista); 0 es "Sin docente"; null si no es ninguno.</summary>
        private OpcionCombo DocenteElegido()
        {
            OpcionCombo elegido = cboDocente.SelectedItem as OpcionCombo;
            string texto = (cboDocente.Text ?? "").Trim();
            if (elegido != null && string.Equals(elegido.Texto, texto, StringComparison.Ordinal))
            {
                return elegido;
            }

            string buscado = EstiloAdmin.Normalizar(texto);
            return buscado.Length == 0 ? null : cboDocente.Items.Cast<OpcionCombo>().FirstOrDefault(o => EstiloAdmin.Normalizar(o.Texto) == buscado);
        }

        /// <summary>Las horas libres bajo el renglon con el foco y, en rojo, con que chocarian estas horas (sin impedir guardar).</summary>
        private void RevisarEnVivo()
        {
            if (colocando)
            {
                return;
            }

            int? profesor;
            int grupo;
            List<int> excluir = new List<int>();
            if (PonerHoras)
            {
                ItemDeCarga item = cboExistente.SelectedItem as ItemDeCarga;
                profesor = item == null ? null : item.Carga.ProfesorId;
                grupo = item == null ? 0 : item.Carga.GrupoId;
                if (item != null)
                {
                    excluir.Add(item.Carga.Id);
                }
            }
            else
            {
                OpcionCombo docente = DocenteElegido();
                profesor = docente == null || docente.Id == 0 ? (int?)null : docente.Id;
                OpcionCombo g = cboGrupo.SelectedItem as OpcionCombo;
                grupo = g == null ? 0 : g.Id;
                if (carga != null)
                {
                    excluir.Add(carga.Id);
                }
            }

            List<FranjaCiclo> franjas = renglones.Select(FranjaDe).Where(f => f != null).ToList();
            List<string> choques = new List<string>();
            try
            {
                choques = RevisionCiclo.ChoquesSi(leido, profesor, grupo, franjas, excluir);
            }
            catch (Exception ex)
            {
                Log.Error("DialogoClaseDelHorario: revisar los choques", ex);
            }

            string texto = choques.Count == 0 ? ""
                : choques.Count <= LineasDeChoques ? string.Join(Environment.NewLine, choques)
                : string.Join(Environment.NewLine, choques.Take(LineasDeChoques - 1)) + Environment.NewLine
                    + "y " + (choques.Count - LineasDeChoques + 1) + " choques más.";
            ayuda.SetToolTip(lblChoques, choques.Count > LineasDeChoques ? string.Join(Environment.NewLine, choques) : null);
            bool cambiaAlto = (lblChoques.Text ?? "") != texto;
            lblChoques.Text = texto;
            // Siempre: la ayuda de las horas libres depende de lo que se acaba de cambiar.
            Colocar();
            if (cambiaAlto)
            {
                lblChoques.Invalidate();
            }
        }

        private void Guardar()
        {
            lblError.Text = "";
            // Primero lo de la clase (docente, grupo y materia), y luego sus horarios.
            if (!PonerHoras)
            {
                if (DocenteElegido() == null)
                {
                    Error("Elige a la persona docente de la lista, o «Sin docente (decidirlo después)».", cboDocente);
                    return;
                }

                if (cboGrupo.SelectedItem == null || cboMateria.SelectedItem == null)
                {
                    Error("Elige grupo y materia.", cboGrupo.SelectedItem == null ? (Control)cboGrupo : cboMateria);
                    return;
                }
            }

            List<FranjaCiclo> franjas = new List<FranjaCiclo>();
            for (int i = 0; i < renglones.Count; i++)
            {
                Renglon r = renglones[i];
                string cual = renglones.Count > 1 ? " (renglón " + (i + 1) + ")" : "";
                OpcionCombo lab = r.Laboratorio.SelectedItem as OpcionCombo;
                OpcionCombo dia = r.Dia.SelectedItem as OpcionCombo;
                if (lab == null || dia == null)
                {
                    Error("Elige el laboratorio y el día de cada horario" + cual + ".", lab == null ? r.Laboratorio : r.Dia);
                    return;
                }

                TimeSpan de;
                TimeSpan a;
                if (!EstiloHorarios.LeerHora(r.De, out de) || !EstiloHorarios.LeerHora(r.A, out a))
                {
                    Error("Escribe las horas como 07:00.", EstiloHorarios.LeerHora(r.De, out de) ? r.A : r.De);
                    return;
                }

                if (a <= de)
                {
                    Error("La hora de fin tiene que ser después de la de inicio" + cual + ".", r.A);
                    return;
                }

                franjas.Add(FranjaDe(r));
            }

            int administrador = idAdministrador();
            Cursor = Cursors.WaitCursor;
            try
            {
                if (PonerHoras)
                {
                    ItemDeCarga item = cboExistente.SelectedItem as ItemDeCarga;
                    if (item == null)
                    {
                        Error("Elige la clase a la que se agregan estas horas.", cboExistente);
                        return;
                    }

                    if (franjas.Count == 0)
                    {
                        Error("Elige al menos un horario.", lnkAgregar);
                        return;
                    }

                    // Que no se crucen con las que ya tiene (lo revisa tambien PonerHoras).
                    CicloPreliminarDatos.ValidarFranjas(item.Carga.Franjas.Concat(franjas).ToList());
                    // Encima de otras clases del mismo laboratorio: las reemplaza, si se confirma.
                    if (!ConfirmarReemplazoEnPreparacion.Preguntar(this, leido, item.Carga.Id, franjas, null))
                    {
                        return;
                    }

                    datos.PonerHoras(item.Carga.Id, franjas, administrador);
                    IdCarga = item.Carga.Id;
                }
                else
                {
                    OpcionCombo docente = DocenteElegido();
                    if (docente == null)
                    {
                        Error("Elige a la persona docente de la lista, o «Sin docente (decidirlo después)».", cboDocente);
                        return;
                    }

                    OpcionCombo grupo = cboGrupo.SelectedItem as OpcionCombo;
                    OpcionCombo materia = cboMateria.SelectedItem as OpcionCombo;
                    if (grupo == null || materia == null)
                    {
                        Error("Elige grupo y materia.", grupo == null ? (Control)cboGrupo : cboMateria);
                        return;
                    }

                    CicloPreliminarDatos.ValidarFranjas(franjas);
                    // Encima de otras clases del mismo laboratorio: las reemplaza, si se confirma.
                    if (!ConfirmarReemplazoEnPreparacion.Preguntar(this, leido, carga != null ? carga.Id : 0, franjas, null))
                    {
                        return;
                    }

                    int? profesor = docente.Id == 0 ? (int?)null : docente.Id;
                    if (carga != null)
                    {
                        datos.CambiarClase(carga.Id, profesor, grupo.Id, materia.Id, franjas, administrador);
                        IdCarga = carga.Id;
                    }
                    else
                    {
                        IdCarga = datos.AgregarClase(leido.Ciclo.Id, profesor, grupo.Id, materia.Id, franjas, administrador);
                    }
                }

                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                HuboFallo = true;
                lblError.Text = PiezasConfiguracion.TextoDeFallo("DialogoClaseDelHorario.Guardar", "No se pudo guardar la clase", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void Error(string texto, Control donde)
        {
            lblError.Text = texto;
            if (donde != null && donde.Visible && donde.Enabled)
            {
                donde.Focus();
            }
        }
    }

    /// <summary>
    /// "Falta asignar: N h" de la preparacion de Proximo ciclo (2026-11-03), como
    /// el de Horario semanal (DialogoFaltaAsignar): lo que le falta a cada clase
    /// del horario en preparacion, de donde salio y por que. «Ponerla...» cierra
    /// con la elegida y el laboratorio donde ponerla (Elegida, LaboratorioElegido):
    /// la pestana la deja preelegida para el siguiente "Poner clase...". "Ya no
    /// hace falta" la quita (con confirmacion) sin cerrar. No hay fechas: una
    /// plantilla no las tiene.
    /// </summary>
    internal class DialogoFaltaAsignarPlantilla : Form
    {
        private readonly CicloLeido leido;
        private readonly DataGridView dgvLista;
        private readonly Button btnPonerla;
        private readonly Button btnNoHaceFalta;
        private readonly ComboBox cboLaboratorio;
        private readonly Label lblTotal;
        private readonly Func<IWin32Window, PorAsignarCiclo, bool> yaNoHaceFalta;
        private readonly List<PorAsignarCiclo> pendientes;

        /// <summary>La elegida al pulsar "Ponerla..." (DialogResult.OK); null si se cerro.</summary>
        public PorAsignarCiclo Elegida { get; private set; }

        /// <summary>Donde ponerla: el laboratorio elegido en "Ponerla en".</summary>
        public int LaboratorioElegido { get; private set; }

        /// <summary>Si alguna se quito con "Ya no hace falta" (la pestana vuelve a leer).</summary>
        public bool HuboCambios { get; private set; }

        /// <param name="leido">La plantilla en preparacion: lo que le falta a sus clases (PorAsignar) y sus nombres.</param>
        /// <param name="laboratorios">Los del selector de la semana, para "Ponerla en".</param>
        /// <param name="yaNoHaceFalta">Pregunta, la quita y avisa si falla (con este dialogo de dueno): true si se quito.</param>
        public DialogoFaltaAsignarPlantilla(CicloLeido leido, List<OpcionCombo> laboratorios, int laboratorioActual, Func<IWin32Window, PorAsignarCiclo, bool> yaNoHaceFalta)
        {
            this.leido = leido;
            this.yaNoHaceFalta = yaNoHaceFalta;
            pendientes = new List<PorAsignarCiclo>(leido.PorAsignar);
            int ancho = Math.Min(1180, Screen.FromPoint(Cursor.Position).WorkingArea.Width - 40);
            int alto = Math.Min(560, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 60);
            EstiloHorarios.PrepararDialogo(this, "Falta asignar", new Size(ancho, alto));

            Label titulo = EstiloAdmin.TituloDialogo("Horas que falta asignar", ancho - 48);
            string texto = "Lo que le falta a cada clase de este horario en preparación, en todos los laboratorios: las horas que otra clase desplazó "
                + "o las que se quitaron para darlas en otro horario. Bajan solas al ponerle horas a la clase (lo más antiguo primero). "
                + "«Ponerla...» te lleva al laboratorio que elijas con la clase ya elegida para el siguiente «Poner clase...». Lo que se usa hoy no cambia.";
            Label explicacion = PiezasConfiguracion.Texto(texto, new Point(24, 54), new Size(ancho - 48, 20), 9.5F, Color.FromArgb(50, 50, 50));
            explicacion.Height = PiezasConfiguracion.AltoDe(texto, explicacion.Font, explicacion.Width) + 4;

            lblTotal = PiezasConfiguracion.Texto("", new Point(24, explicacion.Bottom + 6), new Size(ancho - 48, 20), 9.5F, PiezasConfiguracion.TextoAmbar, FontStyle.Bold);

            int arribaLista = lblTotal.Bottom + 6;
            dgvLista = EstiloHorarios.CrearGrid(new Point(24, arribaLista), new Size(ancho - 48, alto - 56 - 12 - arribaLista));
            dgvLista.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            dgvLista.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            dgvLista.Columns.Add("Clase", "Clase (materia - grupo)");
            dgvLista.Columns.Add("Docente", "Docente");
            dgvLista.Columns.Add("Faltan", "Faltan");
            dgvLista.Columns.Add("Origen", "Salió de");
            dgvLista.Columns.Add("Motivo", "Por qué");
            EstiloHorarios.Pesos(dgvLista, 26, 22, 9, 22, 21);
            Llenar();

            Button cerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(ancho - 24 - 120, ClientSize.Height - 56), new Size(120, 40));
            cerrar.DialogResult = DialogResult.Cancel;
            Controls.Add(titulo);
            Controls.Add(explicacion);
            Controls.Add(lblTotal);
            Controls.Add(dgvLista);
            Controls.Add(cerrar);
            CancelButton = cerrar;
            ActiveControl = dgvLista;

            ToolTip ayuda = new ToolTip { AutoPopDelay = 15000 };
            Disposed += (s, e) => ayuda.Dispose();

            // Abajo a la izquierda: donde ponerla y las dos acciones; Cerrar a la derecha.
            Label lblDonde = EstiloHorarios.CrearEtiqueta("Ponerla en", new Point(24, ClientSize.Height - 46));
            cboLaboratorio = EstiloHorarios.CrearCombo(new Point(24 + lblDonde.PreferredWidth + 8, ClientSize.Height - 50), 220);
            EstiloHorarios.Rellenar(cboLaboratorio, laboratorios ?? new List<OpcionCombo>(), laboratorioActual);
            ayuda.SetToolTip(cboLaboratorio, "El laboratorio donde la quieres poner. Al elegir una clase de la lista, el de donde salió.");

            btnPonerla = EstiloAdmin.BotonPrincipal("Ponerla...", new Point(cboLaboratorio.Right + 10, ClientSize.Height - 56), new Size(130, 40));
            btnPonerla.Click += (s, e) => Ponerla();
            ayuda.SetToolTip(btnPonerla, "Lleva al laboratorio elegido, con esta clase ya elegida para el siguiente «Poner clase...».\n"
                + "Elige allí los cuadros (arrastra o usa Ctrl+clic) y pulsa «Poner clase...». Doble clic en la lista hace lo mismo.");

            btnNoHaceFalta = EstiloHorarios.CrearBoton("Ya no hace falta", new Point(btnPonerla.Right + 10, ClientSize.Height - 56), new Size(160, 40));
            btnNoHaceFalta.Click += (s, e) => NoHaceFalta();
            ayuda.SetToolTip(btnNoHaceFalta, "La quita de la lista sin ponerle horas (antes te pregunta). Las horas de la clase no cambian.");

            dgvLista.SelectionChanged += (s, e) => Elegir();
            dgvLista.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0)
                {
                    Ponerla();
                }
            };

            Controls.Add(lblDonde);
            Controls.Add(cboLaboratorio);
            Controls.Add(btnPonerla);
            Controls.Add(btnNoHaceFalta);
            Elegir();
        }

        private PorAsignarCiclo Actual
        {
            get { return dgvLista.CurrentRow == null ? null : dgvLista.CurrentRow.Tag as PorAsignarCiclo; }
        }

        private void Llenar()
        {
            dgvLista.Rows.Clear();
            foreach (PorAsignarCiclo p in pendientes)
            {
                CargaCiclo carga = leido.Carga(p.CargaId);
                int fila = dgvLista.Rows.Add(
                    carga == null ? "" : carga.Materia + " - " + carga.Grupo,
                    carga == null || !carga.ProfesorId.HasValue ? "Sin docente" : carga.Profesor,
                    HorariosDatos.Duracion(p.Minutos) + (p.Minutos < p.MinutosOriginales ? " de " + HorariosDatos.Duracion(p.MinutosOriginales) : ""),
                    p.Origen,
                    p.Motivo == HorariosDatos.MotivoQuitada ? "Se quitó para darla en otro horario" : "Otra clase se puso en su lugar");
                dgvLista.Rows[fila].Tag = p;
            }

            int total = pendientes.Sum(p => p.Minutos);
            int clases = pendientes.Select(p => p.CargaId).Distinct().Count();
            lblTotal.Text = pendientes.Count == 0 ? "Ya no falta asignar nada."
                : "Falta asignar: " + HorariosDatos.Duracion(total) + " de " + clases + (clases == 1 ? " clase." : " clases.");
        }

        /// <summary>Con una elegida: "Ponerla en" pasa al laboratorio de donde salio (si esta en la lista).</summary>
        private void Elegir()
        {
            PorAsignarCiclo p = Actual;
            btnPonerla.Enabled = p != null && cboLaboratorio.Items.Count > 0;
            btnNoHaceFalta.Enabled = p != null && yaNoHaceFalta != null;
            if (p != null)
            {
                OpcionCombo suyo = cboLaboratorio.Items.OfType<OpcionCombo>().FirstOrDefault(o => o.Id == p.LaboratorioId);
                if (suyo != null)
                {
                    cboLaboratorio.SelectedItem = suyo;
                }
            }
        }

        private void Ponerla()
        {
            PorAsignarCiclo p = Actual;
            OpcionCombo laboratorio = cboLaboratorio.SelectedItem as OpcionCombo;
            if (p == null || laboratorio == null)
            {
                return;
            }

            Elegida = p;
            LaboratorioElegido = laboratorio.Id;
            DialogResult = DialogResult.OK;
        }

        private void NoHaceFalta()
        {
            PorAsignarCiclo p = Actual;
            if (p == null || yaNoHaceFalta == null || !yaNoHaceFalta(this, p))
            {
                return;
            }

            HuboCambios = true;
            pendientes.Remove(p);
            Llenar();
            Elegir();
            if (pendientes.Count == 0)
            {
                DialogResult = DialogResult.Cancel;
            }
        }
    }
}
