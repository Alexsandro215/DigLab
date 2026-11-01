using DigBit.conexion;
using DigBit.Infraestructura;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    // Los dialogos de la pestana Configuraciones de Horarios (especificacion v2,
    // 4.3): C1 "Guardar el horario de hoy", C2 "Preparar un horario", C3
    // "Nombre" y C4 "Clase del horario". Todos van con
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
        /// cerrarla (cada una se creo con new para su control: DigBit no comparte
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
    // El dia y los laboratorios que se copian (C1 y C2)
    // =========================================================================

    /// <summary>
    /// La fecha (con su dia de la semana), cuantas clases se copian y los
    /// laboratorios de los que se copian, con sus cuentas. Lee el dia con
    /// CicloPreliminarDatos.ResumirOrigen (al empezar y al cambiar la fecha) y
    /// rehace las cuentas en C# al marcar o desmarcar una casilla.
    /// </summary>
    internal class SelectorDelDia : Panel
    {
        private const int AltoCabeza = 66;

        private readonly CicloPreliminarDatos datos;
        private readonly List<OpcionCombo> laboratorios;
        private readonly string verbo;
        private readonly DateTimePicker fecha;
        private readonly Label lblCuenta;
        private readonly CheckedListBox lista;
        private List<OrigenDeClase> filas = new List<OrigenDeClase>();
        private string errorAlLeer;
        // El fallo entero, con su causa y que hacer (para la linea de error al guardar).
        private string falloAlLeer;
        private bool llenando;

        /// <summary>Cambio la cuenta (otra fecha o una casilla).</summary>
        public event Action Cambio;

        /// <summary>Cambio la fecha (para rehacer la propuesta de nombre).</summary>
        public event Action FechaCambiada;

        private sealed class ItemDeLaboratorio
        {
            /// <summary>0: "Sin horario en ningun laboratorio".</summary>
            public int Id;
            public string Texto;

            public override string ToString()
            {
                return Texto;
            }
        }

        /// <param name="verbo">"guardar" o "copiar": "no hay clases que guardar ese dia".</param>
        public SelectorDelDia(CicloPreliminarDatos datos, List<OpcionCombo> laboratorios, DateTime hoy, string verbo, int ancho, int altoLista)
        {
            this.datos = datos;
            this.laboratorios = laboratorios ?? new List<OpcionCombo>();
            this.verbo = verbo;
            BackColor = Color.White;
            Size = new Size(ancho, AltoCabeza + altoLista);

            fecha = EstiloHorarios.CrearFechaConDia(this, new Point(0, 2));
            fecha.Value = hoy.Date;
            fecha.ValueChanged += (s, e) =>
            {
                Leer();
                if (FechaCambiada != null)
                {
                    FechaCambiada();
                }
            };

            // A la derecha de la fecha y de su dia ("miercoles" es el mas largo).
            lblCuenta = PiezasConfiguracion.Texto("", new Point(236, 0), new Size(ancho - 236, 40), 9.5F, Color.DarkGreen, FontStyle.Bold);
            // Un fallo largo no se corta a media palabra: "..." y entero al pasar el raton.
            lblCuenta.AutoEllipsis = true;
            Controls.Add(lblCuenta);

            Controls.Add(EstiloHorarios.CrearEtiqueta("De estos laboratorios", new Point(0, 44)));
            lista = new CheckedListBox
            {
                CheckOnClick = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                IntegralHeight = false,
                Location = new Point(0, AltoCabeza),
                Size = new Size(ancho, altoLista)
            };
            // ItemCheck llega antes de que cambie la casilla: se cuenta con lo que va a quedar.
            lista.ItemCheck += (s, e) =>
            {
                if (!llenando)
                {
                    Contar(e.Index, e.NewValue == CheckState.Checked);
                }
            };
            Controls.Add(lista);
            Leer();
        }

        public DateTime Dia
        {
            get { return fecha.Value.Date; }
        }

        /// <summary>Los laboratorios marcados (sin la casilla de las clases sin horario).</summary>
        public List<int> LaboratoriosElegidos
        {
            get { return Elegidos(-1, false).Where(id => id != 0).ToList(); }
        }

        /// <summary>Marcada la casilla "Sin horario en ningun laboratorio".</summary>
        public bool SinHorario
        {
            get { return Elegidos(-1, false).Contains(0); }
        }

        public int Clases { get; private set; }

        public int Docentes { get; private set; }

        public int Horarios { get; private set; }

        /// <summary>No se pudo leer el dia: la causa (ya dicha en la cuenta), o null.</summary>
        public string ErrorAlLeer
        {
            get { return errorAlLeer; }
        }

        /// <summary>No se pudo leer el dia: "No se pudieron leer las clases de ese dia: causa. Que hacer.", o null.</summary>
        public string FalloAlLeer
        {
            get { return falloAlLeer; }
        }

        /// <summary>
        /// El nombre que se propone: el de las fechas de las clases que valen ese
        /// dia (el vigente_desde mas temprano y el vigente_hasta mas tardio), como
        /// "Septiembre 2026-febrero 2027". Sin clases, el mes del dia.
        /// </summary>
        public string NombrePropuesto
        {
            get
            {
                return filas.Count == 0
                    ? CicloPreliminarDatos.NombrePropuesto(Dia, Dia)
                    : CicloPreliminarDatos.NombrePropuesto(filas.Min(f => f.VigenteDesde), filas.Max(f => f.VigenteHasta));
            }
        }

        private void Leer()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                filas = datos.ResumirOrigen(Dia);
                errorAlLeer = null;
                falloAlLeer = null;
            }
            catch (Exception ex)
            {
                Log.Error("SelectorDelDia: leer las clases del " + Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ex);
                filas = new List<OrigenDeClase>();
                errorAlLeer = AvisoDeFalloAdmin.Causa(ex);
                falloAlLeer = AvisoDeFalloAdmin.Texto("No se pudieron leer las clases de ese día", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            Llenar();
        }

        /// <summary>Una casilla por laboratorio activo, con sus cuentas de ese dia; conserva lo marcado.</summary>
        private void Llenar()
        {
            Dictionary<int, bool> antes = new Dictionary<int, bool>();
            for (int i = 0; i < lista.Items.Count; i++)
            {
                antes[((ItemDeLaboratorio)lista.Items[i]).Id] = lista.GetItemChecked(i);
            }

            llenando = true;
            lista.BeginUpdate();
            try
            {
                lista.Items.Clear();
                foreach (OpcionCombo laboratorio in laboratorios)
                {
                    List<OrigenDeClase> deEse = filas.Where(f => f.LaboratorioId == laboratorio.Id).ToList();
                    int clases = deEse.Select(f => f.ClaseId).Distinct().Count();
                    string texto = EstiloHorarios.NombreLaboratorio(laboratorio.Texto) + ": "
                        + (clases == 0 ? "ninguna clase ese día" : RevisionCiclo.TextoClases(clases) + ", " + RevisionCiclo.TextoHoras(deEse.Sum(f => f.Horas)));
                    bool marcado;
                    int i = lista.Items.Add(new ItemDeLaboratorio { Id = laboratorio.Id, Texto = texto });
                    lista.SetItemChecked(i, !antes.TryGetValue(laboratorio.Id, out marcado) || marcado);
                }

                // Las clases sin horario: un renglon mas, sin marcar (no salen en ningun laboratorio).
                int sinHorario = filas.Where(f => !f.LaboratorioId.HasValue).Select(f => f.ClaseId).Distinct().Count();
                if (sinHorario > 0)
                {
                    bool marcado;
                    int i = lista.Items.Add(new ItemDeLaboratorio { Id = 0, Texto = "Sin horario en ningún laboratorio: " + RevisionCiclo.TextoClases(sinHorario) });
                    lista.SetItemChecked(i, antes.TryGetValue(0, out marcado) && marcado);
                }
            }
            finally
            {
                lista.EndUpdate();
                llenando = false;
            }

            Contar(-1, false);
        }

        /// <summary>Los ids marcados; con indice, como si esa casilla ya estuviera como dice marcada.</summary>
        private List<int> Elegidos(int indice, bool marcada)
        {
            List<int> ids = new List<int>();
            for (int i = 0; i < lista.Items.Count; i++)
            {
                if (i == indice ? marcada : lista.GetItemChecked(i))
                {
                    ids.Add(((ItemDeLaboratorio)lista.Items[i]).Id);
                }
            }

            return ids;
        }

        private void Contar(int indice, bool marcada)
        {
            List<int> elegidos = Elegidos(indice, marcada);
            bool sinHorario = elegidos.Contains(0);
            HashSet<int> labs = new HashSet<int>(elegidos.Where(id => id != 0));
            List<OrigenDeClase> enLabs = filas.Where(f => f.LaboratorioId.HasValue && labs.Contains(f.LaboratorioId.Value)).ToList();
            HashSet<int> clases = new HashSet<int>(enLabs.Select(f => f.ClaseId));
            if (sinHorario)
            {
                foreach (OrigenDeClase f in filas.Where(f => !f.LaboratorioId.HasValue))
                {
                    clases.Add(f.ClaseId);
                }
            }

            Clases = clases.Count;
            Docentes = filas.Where(f => clases.Contains(f.ClaseId)).Select(f => f.ProfesorId).Distinct().Count();
            Horarios = enLabs.Count;

            if (errorAlLeer != null)
            {
                Poner("No se pudieron leer las clases de ese día: " + errorAlLeer + ".", false);
            }
            else if (filas.Count == 0)
            {
                Poner("Ese día no hay clases vigentes: elige un día de clases.", false);
            }
            else if (Clases == 0)
            {
                Poner("Con esos laboratorios no hay clases que " + verbo + " ese día.", false);
            }
            else
            {
                Poner(RevisionCiclo.TextoClases(Clases) + " de " + RevisionCiclo.TextoDocentes(Docentes)
                    + (Horarios == 0 ? ", sin horarios." : ", con " + RevisionCiclo.TextoHorarios(Horarios) + "."), true);
            }

            if (Cambio != null)
            {
                Cambio();
            }
        }

        private void Poner(string texto, bool bien)
        {
            lblCuenta.Text = texto;
            lblCuenta.ForeColor = bien ? Color.DarkGreen : PiezasConfiguracion.TextoError;
        }
    }

    // =========================================================================
    // C1. Guardar el horario de hoy
    // =========================================================================

    /// <summary>C1. "Guardar el horario de hoy": nombre, dia y laboratorios (CicloPreliminarDatos.GuardarDia).</summary>
    internal class DialogoGuardarHorarioDeHoy : Form
    {
        private readonly CicloPreliminarDatos datos;
        private readonly Func<int> idAdministrador;
        private readonly TextBox txtNombre;
        private readonly SelectorDelDia selector;
        private readonly Label lblError;
        // El nombre lo cambio la persona: ya no se rehace al cambiar la fecha.
        private bool nombreTocado;
        private bool poniendoNombre;

        /// <summary>Lo que quedo guardado (para el estado y para elegirla en la lista).</summary>
        public ResultadoEmpezar Resultado { get; private set; }

        /// <summary>Fallo al guardar (se dijo aqui): al cancelar, la pestana vuelve a leer.</summary>
        public bool HuboFallo { get; private set; }

        public DialogoGuardarHorarioDeHoy(CicloPreliminarDatos datos, List<OpcionCombo> laboratorios, DateTime hoy, Func<int> idAdministrador)
        {
            this.datos = datos;
            this.idAdministrador = idAdministrador;
            // Alto: la linea de error cabe en 4 renglones (un fallo con su causa y que hacer).
            EstiloHorarios.PrepararDialogo(this, "Guardar el horario de hoy como plantilla", new Size(560, 490));
            Controls.Add(EstiloAdmin.TituloDialogo("Guardar el horario de hoy como plantilla", 512));
            Controls.Add(PiezasConfiguracion.Texto(
                "Guarda una plantilla del horario que se usa (cada docente con sus clases y sus horas) para cargarla en otro ciclo. Nada cambia en los laboratorios.",
                new Point(24, 52), new Size(512, 40), 9.5F, PiezasConfiguracion.TextoGris));

            Controls.Add(EstiloHorarios.CrearEtiqueta("Nombre", new Point(24, 98)));
            txtNombre = EstiloHorarios.CrearTexto(new Point(24, 120), 300);
            txtNombre.MaxLength = CicloPreliminarDatos.LargoNombre;
            txtNombre.TextChanged += (s, e) =>
            {
                if (!poniendoNombre)
                {
                    nombreTocado = true;
                }
            };
            Controls.Add(txtNombre);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Las clases que se dan el", new Point(24, 160)));
            selector = new SelectorDelDia(datos, laboratorios, hoy, "guardar", 512, 104) { Location = new Point(24, 184) };
            selector.FechaCambiada += () =>
            {
                if (!nombreTocado)
                {
                    ProponerNombre();
                }
            };
            Controls.Add(selector);

            lblError = EstiloAdmin.LineaError(new Point(24, 358), 512, 70);
            lblError.AutoEllipsis = true;
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            ProponerNombre();
            ActiveControl = txtNombre;
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

        /// <summary>NombreLibre(propuesta): "Septiembre 2026-febrero 2027", o con "(2)" si ya hay una guardada asi.</summary>
        private void ProponerNombre()
        {
            string propuesta = selector.NombrePropuesto;
            try
            {
                propuesta = datos.NombreLibre(propuesta, EstadoConfiguracion.Guardada);
            }
            catch (Exception ex)
            {
                Log.Error("DialogoGuardarHorarioDeHoy: proponer un nombre libre", ex);
            }

            poniendoNombre = true;
            txtNombre.Text = propuesta;
            poniendoNombre = false;
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

            if (selector.FalloAlLeer != null)
            {
                lblError.Text = selector.FalloAlLeer;
                return;
            }

            if (selector.LaboratoriosElegidos.Count == 0 && !selector.SinHorario)
            {
                lblError.Text = "Elige al menos un laboratorio.";
                return;
            }

            if (selector.Clases == 0)
            {
                lblError.Text = "Con esos laboratorios no hay clases que guardar ese día.";
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                Resultado = datos.GuardarDia(txtNombre.Text.Trim(), selector.Dia, selector.LaboratoriosElegidos, selector.SinHorario, idAdministrador());
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                HuboFallo = true;
                lblError.Text = PiezasConfiguracion.TextoDeFallo("DialogoGuardarHorarioDeHoy.Guardar", "No se pudo guardar el horario", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    // =========================================================================
    // C2. Preparar un horario
    // =========================================================================

    /// <summary>C2. "Preparar un horario": de un dia, de una configuracion o en blanco (CicloPreliminarDatos.Preparar).</summary>
    internal class DialogoPrepararHorario : Form
    {
        private readonly CicloPreliminarDatos datos;
        private readonly Func<int> idAdministrador;
        private readonly CicloPreliminar borrador;
        private readonly RadioButton rbDia;
        private readonly RadioButton rbConfiguracion;
        private readonly RadioButton rbBlanco;
        private readonly SelectorDelDia selector;
        private readonly ComboBox cboConfiguracion;
        private readonly TextBox txtNombre;
        private readonly Label lblError;

        private sealed class ItemDeConfiguracion
        {
            public CicloPreliminar Ciclo;

            public override string ToString()
            {
                return Ciclo.Nombre + " (" + (Ciclo.Aplicada ? "horario aplicado" : "plantilla guardada") + ", " + RevisionCiclo.TextoClases(Ciclo.Clases) + ")";
            }
        }

        /// <summary>Lo que quedo en preparacion.</summary>
        public ResultadoEmpezar Resultado { get; private set; }

        /// <summary>De donde se copio, para el estado: "de <<2026-1>>", "del horario del 24/10/2026", o null en blanco.</summary>
        public string DeDonde { get; private set; }

        /// <summary>Fallo al preparar (se dijo aqui): al cancelar, la pestana vuelve a leer.</summary>
        public bool HuboFallo { get; private set; }

        /// <param name="configuraciones">Las guardadas y las aplicadas (la segunda opcion).</param>
        /// <param name="borrador">La que ya esta en preparacion, o null: si hay una, se descarta (lo pregunta).</param>
        /// <param name="elegida">"Preparar desde esta...": la configuracion ya elegida en la segunda opcion.</param>
        public DialogoPrepararHorario(CicloPreliminarDatos datos, List<OpcionCombo> laboratorios, DateTime hoy, List<CicloPreliminar> configuraciones,
            CicloPreliminar borrador, int? elegida, Func<int> idAdministrador)
        {
            this.datos = datos;
            this.idAdministrador = idAdministrador;
            this.borrador = borrador;
            configuraciones = configuraciones ?? new List<CicloPreliminar>();

            // Alto: la linea de error cabe en 4 renglones; con el aviso ambar, lo que este
            // pida (unos 650: cabe a 1366x768). Se ajusta abajo.
            EstiloHorarios.PrepararDialogo(this, "Preparar el próximo ciclo", new Size(600, 580));
            Controls.Add(EstiloAdmin.TituloDialogo("Preparar el próximo ciclo", 552));
            Controls.Add(PiezasConfiguracion.Texto(
                "Prepara el horario del próximo ciclo sin tocar el que se usa. Cada cambio se guarda al momento y nadie lo ve hasta que lo apliques.",
                new Point(24, 50), new Size(552, 38), 9.5F, PiezasConfiguracion.TextoGris));
            Controls.Add(EstiloHorarios.CrearEtiqueta("Empezar con", new Point(24, 92)));

            DrawingFont fuenteOpcion = new DrawingFont("Century Gothic", 10F);
            rbDia = new RadioButton { AutoSize = true, Font = fuenteOpcion, Location = new Point(24, 116), Text = "El horario que se da el" };
            selector = new SelectorDelDia(datos, laboratorios, hoy, "copiar", 532, 80) { Location = new Point(44, 142) };
            rbConfiguracion = new RadioButton { AutoSize = true, Font = fuenteOpcion, Location = new Point(24, 296), Text = "Una plantilla guardada o un horario aplicado" };
            cboConfiguracion = EstiloHorarios.CrearCombo(new Point(44, 320), 360);
            cboConfiguracion.MaxDropDownItems = 16;
            foreach (CicloPreliminar ciclo in configuraciones)
            {
                cboConfiguracion.Items.Add(new ItemDeConfiguracion { Ciclo = ciclo });
            }

            rbBlanco = new RadioButton { AutoSize = true, Font = fuenteOpcion, Location = new Point(24, 354), Text = "En blanco" };
            Controls.Add(rbDia);
            Controls.Add(selector);
            Controls.Add(rbConfiguracion);
            Controls.Add(cboConfiguracion);
            Controls.Add(rbBlanco);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Nombre", new Point(24, 388)));
            txtNombre = EstiloHorarios.CrearTexto(new Point(24, 410), 300);
            txtNombre.MaxLength = CicloPreliminarDatos.LargoNombre;
            txtNombre.Text = CicloPreliminarDatos.NombrePreparacion;
            Controls.Add(txtNombre);

            int yError = 448;
            if (borrador != null)
            {
                // Del alto que pide (un nombre largo lo hace de tres renglones) y el
                // dialogo crece con el: la parte importante ("...plantilla nueva...") se cortaba.
                Label aviso = PiezasConfiguracion.AvisoAmbar(
                    "Ya hay un horario en preparación («" + borrador.Nombre + "», " + RevisionCiclo.TextoClases(borrador.Clases)
                    + "). Si sigues, se descarta; para conservarlo, cancela y usa «Más acciones», «Guardar como plantilla nueva...».",
                    new Point(24, 446), 552);
                Controls.Add(aviso);
                yError = aviso.Bottom + 6;
            }

            // La linea de error (cuatro renglones) y los botones, debajo.
            ClientSize = new Size(600, yError + 132);
            lblError = EstiloAdmin.LineaError(new Point(24, yError), 552, 70);
            lblError.AutoEllipsis = true;
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, borrador == null ? "Preparar" : "Descartar el otro y preparar", Preparar);

            rbDia.CheckedChanged += (s, e) => Activar();
            rbConfiguracion.CheckedChanged += (s, e) => Activar();
            rbBlanco.CheckedChanged += (s, e) => Activar();
            rbConfiguracion.Enabled = cboConfiguracion.Items.Count > 0;

            ItemDeConfiguracion preelegida = elegida.HasValue
                ? cboConfiguracion.Items.Cast<ItemDeConfiguracion>().FirstOrDefault(i => i.Ciclo.Id == elegida.Value) : null;
            if (preelegida != null)
            {
                cboConfiguracion.SelectedItem = preelegida;
                rbConfiguracion.Checked = true;
            }
            else
            {
                if (cboConfiguracion.Items.Count > 0)
                {
                    cboConfiguracion.SelectedIndex = 0;
                }

                rbDia.Checked = true;
            }

            Activar();
            ActiveControl = preelegida != null ? (Control)cboConfiguracion : rbDia;
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

        /// <summary>Solo se puede tocar lo de la opcion elegida.</summary>
        private void Activar()
        {
            selector.Enabled = rbDia.Checked;
            cboConfiguracion.Enabled = rbConfiguracion.Checked;
        }

        private void Preparar()
        {
            lblError.Text = "";
            string error = CicloPreliminarDatos.ErrorDeNombre(txtNombre.Text);
            if (error != null)
            {
                lblError.Text = error;
                txtNombre.Focus();
                return;
            }

            FuentePreparacion fuente;
            string deDonde;
            if (rbDia.Checked)
            {
                if (selector.FalloAlLeer != null)
                {
                    lblError.Text = selector.FalloAlLeer;
                    return;
                }

                if (selector.LaboratoriosElegidos.Count == 0 && !selector.SinHorario)
                {
                    lblError.Text = "Elige al menos un laboratorio.";
                    return;
                }

                if (selector.Clases == 0)
                {
                    lblError.Text = "Con esos laboratorios no hay clases que copiar ese día.";
                    return;
                }

                fuente = FuentePreparacion.DelDia(selector.Dia, selector.LaboratoriosElegidos, selector.SinHorario);
                deDonde = "del horario del " + PiezasConfiguracion.Fecha(selector.Dia);
            }
            else if (rbConfiguracion.Checked)
            {
                ItemDeConfiguracion item = cboConfiguracion.SelectedItem as ItemDeConfiguracion;
                if (item == null)
                {
                    lblError.Text = "Elige la plantilla que se carga.";
                    cboConfiguracion.Focus();
                    return;
                }

                fuente = FuentePreparacion.DeConfiguracion(item.Ciclo.Id);
                deDonde = "de «" + item.Ciclo.Nombre + "»";
            }
            else
            {
                fuente = FuentePreparacion.EnBlanco();
                deDonde = null;
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
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                HuboFallo = true;
                lblError.Text = PiezasConfiguracion.TextoDeFallo("DialogoPrepararHorario.Preparar", "No se pudo preparar el horario", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    // =========================================================================
    // C2b. Crear plantilla nueva
    // =========================================================================

    /// <summary>
    /// C2b. "Crear plantilla nueva...": el horario del proximo ciclo desde cero.
    /// Pregunta si se usan los docentes y sus clases (docente, materia y grupo)
    /// de una plantilla ya hecha, sin sus horas, que se acomodan de nuevo en la
    /// cuadricula; si no, empieza vacia (CicloPreliminarDatos.Preparar con
    /// FuentePreparacion.ClasesDeConfiguracion o EnBlanco).
    /// </summary>
    internal class DialogoCrearPlantilla : Form
    {
        private readonly CicloPreliminarDatos datos;
        private readonly Func<int> idAdministrador;
        private readonly CicloPreliminar borrador;
        private readonly RadioButton rbClases;
        private readonly RadioButton rbVacia;
        private readonly ComboBox cboConfiguracion;
        private readonly TextBox txtNombre;
        private readonly Label lblError;

        private sealed class ItemDeConfiguracion
        {
            public CicloPreliminar Ciclo;

            public override string ToString()
            {
                return Ciclo.Nombre + " (" + (Ciclo.Aplicada ? "horario aplicado" : "plantilla guardada") + ", " + RevisionCiclo.TextoClases(Ciclo.Clases) + ")";
            }
        }

        /// <summary>Lo que quedo en preparacion.</summary>
        public ResultadoEmpezar Resultado { get; private set; }

        /// <summary>De donde salieron las clases, para el estado: "de <<2026-1>>", o null si empezo vacia.</summary>
        public string DeDonde { get; private set; }

        /// <summary>Fallo al crear (se dijo aqui): al cancelar, la pestana vuelve a leer.</summary>
        public bool HuboFallo { get; private set; }

        /// <param name="configuraciones">Las guardadas y las aplicadas, de donde se toman las clases.</param>
        /// <param name="borrador">La que ya esta en preparacion, o null: si hay una, se descarta (lo pregunta).</param>
        public DialogoCrearPlantilla(CicloPreliminarDatos datos, List<CicloPreliminar> configuraciones, CicloPreliminar borrador, Func<int> idAdministrador)
        {
            this.datos = datos;
            this.idAdministrador = idAdministrador;
            this.borrador = borrador;
            configuraciones = configuraciones ?? new List<CicloPreliminar>();

            // El alto se ajusta abajo (con el aviso ambar, lo que este pida).
            EstiloHorarios.PrepararDialogo(this, "Crear plantilla nueva", new Size(600, 440));
            Controls.Add(EstiloAdmin.TituloDialogo("Crear plantilla nueva", 552));
            Controls.Add(PiezasConfiguracion.Texto(
                "Un horario nuevo para el próximo ciclo, que se arma desde cero. Cada cambio se guarda al momento y nadie lo ve hasta que lo apliques.",
                new Point(24, 50), new Size(552, 38), 9.5F, PiezasConfiguracion.TextoGris));
            Controls.Add(PiezasConfiguracion.Texto("¿Usar los docentes y sus clases de una plantilla ya hecha?",
                new Point(24, 94), new Size(552, 22), 10F, Color.DarkGreen, FontStyle.Bold));

            DrawingFont fuenteOpcion = new DrawingFont("Century Gothic", 10F);
            rbClases = new RadioButton { AutoSize = true, Font = fuenteOpcion, Location = new Point(24, 124), Text = "Sí, los de esta plantilla:" };
            cboConfiguracion = EstiloHorarios.CrearCombo(new Point(44, 150), 470);
            cboConfiguracion.MaxDropDownItems = 16;
            foreach (CicloPreliminar ciclo in configuraciones)
            {
                cboConfiguracion.Items.Add(new ItemDeConfiguracion { Ciclo = ciclo });
            }

            Label nota = PiezasConfiguracion.Texto(
                "Se copian el docente, la materia y el grupo de cada clase, sin sus horas: las acomodas tú en la cuadrícula.",
                new Point(44, 182), new Size(532, 40), 9F, PiezasConfiguracion.TextoGris);
            rbVacia = new RadioButton { AutoSize = true, Font = fuenteOpcion, Location = new Point(24, 230), Text = "No, empezar vacía" };
            Controls.Add(rbClases);
            Controls.Add(cboConfiguracion);
            Controls.Add(nota);
            Controls.Add(rbVacia);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Nombre", new Point(24, 270)));
            txtNombre = EstiloHorarios.CrearTexto(new Point(24, 292), 300);
            txtNombre.MaxLength = CicloPreliminarDatos.LargoNombre;
            txtNombre.Text = CicloPreliminarDatos.NombrePreparacion;
            Controls.Add(txtNombre);

            int yError = 330;
            if (borrador != null)
            {
                // Del alto que pide su texto, como en Preparar el proximo ciclo.
                Label aviso = PiezasConfiguracion.AvisoAmbar(
                    "Ya hay un horario en preparación («" + borrador.Nombre + "», " + RevisionCiclo.TextoClases(borrador.Clases)
                    + "). Si sigues, se descarta; para conservarlo, cancela y usa «Más acciones», «Guardar como plantilla nueva...».",
                    new Point(24, 328), 552);
                Controls.Add(aviso);
                yError = aviso.Bottom + 6;
            }

            // La linea de error (dos renglones) y los botones, debajo.
            ClientSize = new Size(600, yError + 110);
            lblError = EstiloAdmin.LineaError(new Point(24, yError), 552, 48);
            lblError.AutoEllipsis = true;
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, borrador == null ? "Crear" : "Descartar el otro y crear", Crear);

            rbClases.CheckedChanged += (s, e) => cboConfiguracion.Enabled = rbClases.Checked;
            rbClases.Enabled = cboConfiguracion.Items.Count > 0;
            if (cboConfiguracion.Items.Count > 0)
            {
                cboConfiguracion.SelectedIndex = 0;
                rbClases.Checked = true;
            }
            else
            {
                rbVacia.Checked = true;
                cboConfiguracion.Enabled = false;
            }

            ActiveControl = rbClases.Checked ? (Control)cboConfiguracion : txtNombre;
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

        private void Crear()
        {
            lblError.Text = "";
            string error = CicloPreliminarDatos.ErrorDeNombre(txtNombre.Text);
            if (error != null)
            {
                lblError.Text = error;
                txtNombre.Focus();
                return;
            }

            FuentePreparacion fuente;
            string deDonde = null;
            if (rbClases.Checked)
            {
                ItemDeConfiguracion item = cboConfiguracion.SelectedItem as ItemDeConfiguracion;
                if (item == null)
                {
                    lblError.Text = "Elige la plantilla de la que se toman las clases.";
                    cboConfiguracion.Focus();
                    return;
                }

                fuente = FuentePreparacion.ClasesDeConfiguracion(item.Ciclo.Id);
                deDonde = "de «" + item.Ciclo.Nombre + "»";
            }
            else
            {
                fuente = FuentePreparacion.EnBlanco();
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
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                HuboFallo = true;
                lblError.Text = PiezasConfiguracion.TextoDeFallo("DialogoCrearPlantilla.Crear", "No se pudo crear la plantilla", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
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

            // Si la clase ya tiene horas en un laboratorio desactivado, sale con su nombre (la revision lo marca).
            if (carga != null)
            {
                foreach (FranjaCiclo f in carga.Franjas.Where(f => !nombreDeLaboratorio.ContainsKey(f.LaboratorioId)))
                {
                    nombreDeLaboratorio[f.LaboratorioId] = f.Laboratorio;
                    laboratorios.Add(new OpcionCombo { Id = f.LaboratorioId, Texto = EstiloHorarios.NombreLaboratorio(f.Laboratorio) + " (desactivado)" });
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
            List<OpcionCombo> dias = Enumerable.Range(1, CuadriculaSemanal.Dias).Select(d => new OpcionCombo { Id = d, Texto = HorariosDatos.NombresDia[d] }).ToList();
            if (dia.HasValue && dia.Value > CuadriculaSemanal.Dias)
            {
                dias.Add(new OpcionCombo { Id = dia.Value, Texto = HorariosDatos.NombresDia[dia.Value] });
            }

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
}
