using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// Pestana Aplicacion de Horarios (especificacion v2, 4.5 y 4.6): el paso
    /// aparte para aplicar el horario en preparacion. Tiene cuatro formas:
    /// - sin la base (falta db/16, db/23 o db/09): lo explica;
    /// - nada que aplicar (B): como preparar uno, y la tabla de los aplicados;
    /// - la pagina de aplicar (C): a la izquierda se decide cuando empieza el
    ///   horario nuevo (dos tarjetas: "Ahora mismo", hoy, al aplicarlo, y el de
    ///   ahora hasta ayer, por omision si no tiene fechas guardadas; u "Otro dia",
    ///   con su fecha y el ultimo dia del de ahora, por omision si las tiene), su
    ///   ultimo dia y el nombre en el registro; a la derecha, como queda: la linea
    ///   de tiempo (LineaDeFechasHorario), "Al aplicar" y "Antes de aplicar" (lo
    ///   rojo no deja aplicar, lo ambar avisa; lo que esta bien, plegado), con la
    ///   revision en segundo plano; con "Ahora mismo" lo que depende de la hora
    ///   (las clases de hoy que ya terminaron) se rehace cada 30 s sin volver a
    ///   leer; el boton de aplicar pide la contrasena de la administracion;
    /// - aplicado y aun sin empezar (D): lo que se aplico y "Deshacer la
    ///   aplicacion..." mientras no empiece ni tenga sesiones.
    /// La persona responsable del laboratorio (solo consulta) ve B y D, sin
    /// aplicar ni deshacer. Lee y escribe con CicloPreliminarDatos (Aplicar,
    /// Deshacer y LeerParaAplicar estan en CicloPreliminarDatos.Aplicar.cs) y
    /// revisa con RevisionAplicacion. AdministrarHorarios la construye con
    /// new PestanaAplicacion(IdAdministrador, IrASemanaDeHorarios, IrAConfiguraciones).
    /// </summary>
    internal class PestanaAplicacion : Panel
    {
        private const int AnchoDosColumnas = 1150;
        private const int AnchoTarjeta = 720;
        // Cabe "Aplicar desde el 30/09/2027...".
        private const int AnchoBotonAplicar = 280;
        private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;
        private static readonly Color TextoOscuro = Color.FromArgb(40, 40, 40);
        private static readonly Color TextoGris = Color.FromArgb(105, 105, 105);
        private static readonly Color TextoRojo = Color.FromArgb(170, 30, 30);
        private static readonly Color PuntoRojo = Color.FromArgb(198, 40, 40);
        private static readonly Color PuntoAmbar = Color.FromArgb(214, 160, 0);
        private static readonly Color PuntoVerde = Color.FromArgb(46, 125, 50);
        private static readonly Color PuntoGris = Color.FromArgb(160, 160, 160);

        /// <summary>
        /// Lo que se puso en la pagina de aplicar y no se guarda en la base (el
        /// ultimo dia del horario de ahora y lo escrito a mano), por horario en
        /// preparacion: vuelve al volver a la pestana mientras dure la sesion.
        /// </summary>
        private sealed class Recordado
        {
            /// <summary>"Ahora mismo" (true) u "Otro dia" (false); null: aun no se eligio (por omision, ahora mismo).</summary>
            public bool? Ahora;
            public DateTime? Fin;
            public bool FinTocado;
            public DateTime? Hasta;
            public bool HastaTocado;
            public string Nombre;
            public bool NombreTocado;
            public bool? Guardar;
            public string GuardarComo;
            public bool GuardarTocado;
        }

        // Por sesion y por horario en preparacion (ClaveRecordada): lo que escribio
        // una cuenta nunca lo ve otra, ni otro horario con el mismo numero en otra base.
        private static readonly Dictionary<string, Recordado> recordados = new Dictionary<string, Recordado>();

        private readonly CicloPreliminarDatos datos = new CicloPreliminarDatos();
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };
        private readonly Timer temporizador = new Timer { Interval = 500 };
        // Con "Ahora mismo": cada 30 s se rehace en memoria lo que depende de la
        // hora del servidor (las clases de hoy que ya terminaron), sin volver a leer.
        private readonly Timer relojAhora = new Timer { Interval = 30000 };

        // Las cuatro formas; se ve una.
        private readonly Panel vistaBase;
        private readonly Panel vistaB;
        private readonly Panel vistaC;
        private readonly Panel vistaD;
        private Panel vistaActual;

        // Sin base (o sin poder leer).
        private readonly Tarjeta tarjetaBase;
        private readonly Label lblTituloBase;
        private readonly Label lblTextoBase;
        private readonly Button btnRevisarBase;

        // B. Nada que aplicar.
        private readonly Tarjeta tarjetaB;
        private readonly Label lblTituloB;
        private readonly Label lblTextoB;
        private readonly Button btnIrB;
        private readonly Label lblEstadoB;
        private readonly Label lblAplicadosB;
        private readonly LinkLabel lnkTodosB;
        private readonly DataGridView gridB;
        private readonly Label lblVacioB;

        // C. La pagina de aplicar: columna izquierda.
        private readonly Label lblTituloC;
        private readonly Label lblResumenC;
        private readonly LinkLabel lnkVerloC;
        private readonly Label lblFechas;
        // Cuando empieza, una tarjeta por opcion: "Ahora mismo" u "Otro dia" con dtpDesde.
        private readonly Tarjeta tarjetaAhora;
        private readonly Tarjeta tarjetaOtroDia;
        private readonly RadioButton rbAhora;
        private readonly RadioButton rbOtroDia;
        private readonly Label lblHoyAhora;
        private readonly Label lblNotaAhora;
        private readonly DateTimePicker dtpDesde;
        private readonly Label diaDesde;
        private readonly Label lblNotaOtro;
        private readonly Label lblRotHasta;
        private readonly DateTimePicker dtpHasta;
        private readonly Label diaHasta;
        // Cuanto dura el horario nuevo, junto a su ultimo dia.
        private readonly Label lblDuracion;
        private readonly Label lblRotFin;
        private readonly DateTimePicker dtpFin;
        private readonly Label diaFin;
        private readonly Label lblNotaFin;
        private readonly Label lblRotNombre;
        private readonly TextBox txtNombre;
        // Como queda: la linea de tiempo.
        private readonly Label lblLinea;
        private readonly LineaDeFechasHorario linea;
        private readonly Label lblAlAplicar;
        private readonly Label lblRotCrean;
        private readonly Label lblCrean;
        private readonly Label lblRotTerminan;
        private readonly Label lblTerminan;
        private readonly Label lblRotCodigos;
        private readonly Label lblCodigos;
        // Lo que pasa en los laboratorios mientras tanto, resaltado.
        private readonly Tarjeta tarjetaNadaCambia;
        private readonly Label lblNadaCambia;
        private readonly CheckBox chkGuardar;
        private readonly TextBox txtGuardarComo;
        private readonly Label lblParaCargarlo;
        private readonly Button btnAplicar;
        private readonly Label lblEstadoC;
        // Por que no se puede aplicar todavia, en rojo junto al boton (sin pasar el raton).
        private readonly Label lblMotivoC;
        // El boton, lblEstadoC y lblMotivoC: a dos columnas, sobre la pagina (AcomodarC).
        private readonly Panel pieC;
        // El pie esta abajo, sobre la pagina, porque la columna izquierda no cabe.
        private bool pieFlotante;
        // ... columna derecha.
        private readonly Label lblAntes;
        private readonly Label lblRevisando;
        private readonly LinkLabel lnkRevisarOtraVez;
        private readonly FlowLayoutPanel flowRevision;
        // La conclusion de la revision, bajo su titulo (mientras revisa, la clave de los colores).
        private readonly Label lblPieRevision;
        private readonly DrawingFont fuenteConclusion = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteClave = new DrawingFont("Century Gothic", 9F);
        // Se ve la lista de lo que esta bien (por omision, plegada en un renglon).
        private bool verLoQueEstaBien;

        // D. Aplicado y aun sin empezar.
        private readonly Tarjeta tarjetaD;
        private readonly Label lblTituloD;
        private readonly Label lblRotAplicado;
        private readonly Label lblAplicado;
        private readonly Label lblRotEmpieza;
        private readonly Label lblEmpieza;
        private readonly Label lblRotTermina;
        private readonly Label lblTermina;
        private readonly Label lblRotAnterior;
        private readonly Label lblAnterior;
        private readonly Button btnVerHorariosD;
        private readonly Button btnVerConfiguracionD;
        private readonly Button btnDeshacer;
        private readonly Label lblNotaD;
        private readonly Label lblEstadoD;
        private readonly Label lblAplicadosD;
        private readonly LinkLabel lnkTodosD;
        private readonly DataGridView gridD;
        private readonly Label lblVacioD;

        private SituacionConfiguraciones situacion;
        private InfoDeAplicada infoAplicada;
        // El mensaje para la linea de estado de la vista que se ensena al volver a leer.
        private string mensajePendiente;
        private bool mensajePendienteEsError;

        // Lo de la pagina de aplicar.
        private CicloPreliminar borrador;
        private Recordado recordado;
        private DateTime hoy = DateTime.Today;
        // Cuando acaban las clases que valen hoy (el fin mas comun); null: hoy no vale ninguna.
        private DateTime? finDeLasDeHoy;
        private DateTime? desdeGuardado;
        private DateTime? hastaGuardado;
        // Mientras se ponen valores desde el codigo, sus eventos no cuentan como cambios de la persona.
        private bool ajustando;
        private int lectura;
        private bool revisando;
        private string errorRevision;
        private DatosDeAplicacion revisado;
        private List<RenglonRevision> renglones;
        // El nombre con que se hicieron los renglones: Aplicar solo con el que se ve.
        private string nombreRevisado;
        // Una lectura a la vez: si se pide otra mientras lee, se lanza al terminar esa.
        private Task revisionEnCurso;
        private bool otraRevision;
        // Si la revision encuentra que el horario cambio en otra ventana, se vuelve a leer todo una vez.
        private bool recargadoPorRevision;
        // Mientras aplica o deshace: ningun otro Aplicar ni Deshacer.
        private bool ocupado;
        private int anchoRenglones = -1;
        // Mientras el reloj de "Ahora mismo" rehace los renglones: la lista se queda donde estaba.
        private bool conservarDesplazamiento;
        private string motivoBoton = "";
        private string motivoDeshacer = "";

        public PestanaAplicacion()
            : this(() => 0, lunes => { }, (id, filtro) => { })
        {
        }

        /// <param name="idAdministrador">El idusuarios de quien tiene la sesion (publicado_por, creada_por, historial).</param>
        /// <param name="irASemanaDeHorarios">Lleva a la pestana Horarios en la semana de ese lunes ("Verlo en Horarios").</param>
        /// <param name="irAConfiguraciones">
        /// Lleva a la pestana Configuraciones; con un id, abre esa configuracion en
        /// Clases con ese filtro ("Verlas", "Verlo en Configuraciones"). Null: la lista.
        /// </param>
        public PestanaAplicacion(Func<int> idAdministrador, Action<DateTime> irASemanaDeHorarios, Action<int?, FiltroClases> irAConfiguraciones)
        {
            IdAdministrador = idAdministrador ?? (() => 0);
            IrASemanaDeHorarios = irASemanaDeHorarios ?? (lunes => { });
            IrAConfiguraciones = irAConfiguraciones ?? ((id, filtro) => { });
            BackColor = Color.White;
            DoubleBuffered = true;

            // --- Sin base -------------------------------------------------------
            vistaBase = NuevaVista();
            tarjetaBase = new Tarjeta { Borde = Color.DarkGreen };
            lblTituloBase = Titulo("Falta preparar la base", 14F);
            lblTextoBase = Texto(10F, TextoOscuro);
            btnRevisarBase = EstiloHorarios.CrearBoton("Volver a revisar", Point.Empty, new Size(160, 36));
            btnRevisarBase.Click += (s, e) => Cargar();
            tarjetaBase.Controls.AddRange(new Control[] { lblTituloBase, lblTextoBase, btnRevisarBase });
            vistaBase.Controls.Add(tarjetaBase);

            // --- B. Nada que aplicar --------------------------------------------
            vistaB = NuevaVista();
            tarjetaB = new Tarjeta { Borde = Color.DarkGreen };
            lblTituloB = Titulo("Aplicar con fechas", 14F);
            lblTextoB = Texto(10F, TextoOscuro);
            btnIrB = EstiloHorarios.CrearBoton("Ir a «2. Preparación»", Point.Empty, new Size(220, 36));
            btnIrB.Click += (s, e) => PedirPreparacion();
            tarjetaB.Controls.AddRange(new Control[] { lblTituloB, lblTextoB, btnIrB });
            lblEstadoB = LineaDeEstado();
            lblAplicadosB = Titulo("Horarios aplicados", 10.5F);
            lnkTodosB = Enlace("Verlos en «1. Plantillas»", (s, e) => PedirPlantillas());
            gridB = TablaDeAplicadas();
            lblVacioB = Texto(9.5F, TextoGris);
            lblVacioB.Text = "Todavía no se ha aplicado ningún horario.";
            vistaB.Controls.AddRange(new Control[] { tarjetaB, lblEstadoB, lblAplicadosB, lnkTodosB, gridB, lblVacioB });

            // --- C. La pagina de aplicar ----------------------------------------
            vistaC = NuevaVista();
            vistaC.AutoScroll = true;
            lblTituloC = Titulo("", 14F);
            lblTituloC.AutoSize = false;
            lblTituloC.AutoEllipsis = true;
            lblResumenC = Texto(9.5F, TextoGris);
            lnkVerloC = Enlace("< Volver a la preparación", (s, e) =>
            {
                if (borrador != null)
                {
                    IrAConfiguraciones(borrador.Id, FiltroClases.Ninguno);
                }
            });
            lblFechas = Titulo("¿Cuándo empieza el horario nuevo?", 10.5F);
            tarjetaAhora = new Tarjeta { Grosor = 1, Radio = 10 };
            tarjetaOtroDia = new Tarjeta { Grosor = 1, Radio = 10 };
            rbAhora = Opcion("Ahora mismo");
            rbOtroDia = Opcion("Otro día");
            lblHoyAhora = Texto(10F, TextoOscuro);
            lblNotaAhora = Texto(9F, TextoGris);
            lblNotaAhora.Text = "Empieza en cuanto lo apliques. Ya no se podrá deshacer.";
            lblNotaOtro = Texto(9F, TextoGris);
            lblNotaOtro.Text = "Hasta ese día nada cambia. Se puede deshacer.";
            ayuda.SetToolTip(rbAhora, "Empieza en el momento en que lo apliques: el horario en uso termina ayer.\n"
                + "Las clases de hoy que ya terminaron no cuentan; la que está en curso y las que faltan, sí.\n"
                + "Como empieza hoy, ya no se puede deshacer.");
            ayuda.SetToolTip(rbOtroDia, "Empieza el día que elijas (después de hoy); hasta entonces nada cambia\n"
                + "y se puede deshacer mientras no empiece.");
            dtpDesde = Fecha(tarjetaOtroDia, out diaDesde);
            diaDesde.BackColor = Color.Transparent;
            tarjetaAhora.Controls.AddRange(new Control[] { rbAhora, lblHoyAhora, lblNotaAhora });
            tarjetaOtroDia.Controls.AddRange(new Control[] { rbOtroDia, lblNotaOtro });
            rbOtroDia.TabIndex = 0;
            dtpDesde.TabIndex = 1;
            lblRotHasta = Rotulo("¿Cuándo termina?");
            dtpHasta = Fecha(vistaC, out diaHasta);
            lblDuracion = Texto(9.5F, TextoGris);
            lblRotFin = Rotulo("¿Hasta cuándo sigue el de ahora?");
            dtpFin = Fecha(vistaC, out diaFin);
            lblNotaFin = Texto(8.5F, TextoGris);
            lblNotaFin.Text = NotaFinOtroDia;
            lblRotNombre = Rotulo("¿Con qué nombre se guarda?");
            txtNombre = EstiloHorarios.CrearTexto(Point.Empty, 260);
            txtNombre.MaxLength = CicloPreliminarDatos.LargoNombre;
            ayuda.SetToolTip(txtNombre, "Con este nombre queda en «1. Plantillas» como horario aplicado. No puede llamarse igual que otro aplicado.");
            lblLinea = Titulo("Así quedarán las fechas", 10.5F);
            linea = new LineaDeFechasHorario();
            lblAlAplicar = Titulo("Al aplicar", 10.5F);
            lblRotCrean = RotuloDeRenglon("Se crean:");
            lblCrean = Texto(9.5F, TextoOscuro);
            lblRotTerminan = RotuloDeRenglon("Terminan:");
            lblTerminan = Texto(9.5F, TextoOscuro);
            lblRotCodigos = RotuloDeRenglon("Códigos:");
            lblCodigos = Texto(9.5F, TextoOscuro);
            lblNadaCambia = Texto(9.5F, TextoOscuro);
            tarjetaNadaCambia = new Tarjeta { Grosor = 1, Radio = 8 };
            tarjetaNadaCambia.Controls.Add(lblNadaCambia);
            chkGuardar = new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = TextoOscuro,
                Text = "Guardar también el horario de ahora, como quedó, con el nombre"
            };
            ayuda.SetToolTip(chkGuardar, "Antes de aplicar se guarda una copia del horario de ahora, con lo que se le cambió durante el ciclo:\n"
                + "con «Ahora mismo», el que se da hoy (si hoy no hay clases, el de ayer); con «Otro día», el que se da el último día del de ahora.\n"
                + "Si ese día no hay clases, el día de su última semana con clases en que había más. Aquí al lado dice cuántas lleva.\n"
                + "Queda en «1. Plantillas» como plantilla guardada.");
            txtGuardarComo = EstiloHorarios.CrearTexto(Point.Empty, 220);
            txtGuardarComo.MaxLength = CicloPreliminarDatos.LargoNombre;
            lblParaCargarlo = Texto(9F, TextoGris);
            lblParaCargarlo.Text = "Para usarlo en otro ciclo.";
            btnAplicar = EstiloAdmin.BotonPrincipal("Aplicar el horario...", Point.Empty, new Size(AnchoBotonAplicar, 42));
            btnAplicar.Click += (s, e) => PulsarAplicar();
            lblEstadoC = LineaDeEstado();
            lblEstadoC.AutoSize = false;
            lblMotivoC = Texto(9.5F, TextoRojo);
            lblMotivoC.AutoSize = false;
            lblMotivoC.AutoEllipsis = true;
            lblAntes = Titulo("Antes de aplicar", 10.5F);
            lblRevisando = Texto(9F, TextoGris);
            lblRevisando.AutoSize = false;
            lblRevisando.TextAlign = ContentAlignment.TopRight;
            lnkRevisarOtraVez = Enlace("Revisar otra vez", (s, e) => LanzarRevision());
            lnkRevisarOtraVez.Visible = false;
            flowRevision = new FlowLayoutPanel
            {
                AutoScroll = true,
                BackColor = Color.White,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            lblPieRevision = Texto(9F, TextoGris);
            lblPieRevision.Text = ClaveDeColores;
            // El boton con la linea de estado y el porque de no poder aplicar, en su
            // pie: a dos columnas va sobre la pagina y, si la columna no cabe, se queda
            // abajo, a la vista (AcomodarC); a una columna, en su sitio de la pagina.
            pieC = new Panel { BackColor = Color.White, TabStop = false };
            btnAplicar.TabIndex = 0;
            pieC.Controls.AddRange(new Control[] { btnAplicar, lblEstadoC, lblMotivoC });
            pieC.Paint += (s, e) =>
            {
                // Flotando sobre la pagina: una raya arriba, porque lo de abajo sigue detras.
                if (pieFlotante)
                {
                    using (Pen raya = new Pen(Color.FromArgb(222, 222, 222)))
                    {
                        e.Graphics.DrawLine(raya, 0, 0, pieC.Width, 0);
                    }
                }
            };
            vistaC.Controls.AddRange(new Control[]
            {
                lblTituloC, lblResumenC, lnkVerloC, lblFechas, tarjetaAhora, tarjetaOtroDia,
                lblRotHasta, lblDuracion, lblRotFin, lblNotaFin, lblRotNombre, txtNombre,
                lblLinea, linea,
                lblAlAplicar, lblRotCrean, lblCrean, lblRotTerminan, lblTerminan, lblRotCodigos, lblCodigos, tarjetaNadaCambia,
                chkGuardar, txtGuardarComo, lblParaCargarlo, pieC,
                lblAntes, lblRevisando, lnkRevisarOtraVez, flowRevision, lblPieRevision
            });
            // Un boton desactivado no recibe el raton: la explicacion la da su contenedor.
            AyudaSobreDesactivado(pieC, btnAplicar, () => motivoBoton);
            // El tabulador en el orden en que se ven (las fechas se anadieron antes que el resto).
            Control[] enOrden = { lnkVerloC, tarjetaAhora, tarjetaOtroDia, dtpHasta, dtpFin, txtNombre, chkGuardar, txtGuardarComo, pieC, lnkRevisarOtraVez };
            for (int i = 0; i < enOrden.Length; i++)
            {
                enOrden[i].TabIndex = i;
            }

            // Cada opcion en su tarjeta: en contenedores distintos, Windows ya no apaga la otra.
            rbAhora.CheckedChanged += (s, e) =>
            {
                if (rbAhora.Checked)
                {
                    rbOtroDia.Checked = false;
                }
            };
            rbOtroDia.CheckedChanged += (s, e) =>
            {
                if (rbOtroDia.Checked)
                {
                    rbAhora.Checked = false;
                }
            };
            // Una de las dos cambia y la otra tambien: basta con escuchar una.
            rbAhora.CheckedChanged += (s, e) => ModoCambiado();
            // Un clic en cualquier parte de la tarjeta la elige.
            EventHandler elegirAhora = (s, e) =>
            {
                if (borrador != null && !ocupado)
                {
                    rbAhora.Checked = true;
                }
            };
            tarjetaAhora.Click += elegirAhora;
            lblHoyAhora.Click += elegirAhora;
            lblNotaAhora.Click += elegirAhora;
            // Con MouseDown: la fecha apagada de "Otro dia" no recibe el raton, le llega a su contenedor.
            Action elegirOtroDia = () =>
            {
                if (borrador != null && !ocupado && !rbOtroDia.Checked)
                {
                    rbOtroDia.Checked = true;
                    dtpDesde.Focus();
                }
            };
            tarjetaOtroDia.MouseDown += (s, e) => elegirOtroDia();
            diaDesde.Click += (s, e) => elegirOtroDia();
            lblNotaOtro.Click += (s, e) => elegirOtroDia();
            dtpDesde.ValueChanged += (s, e) => DesdeCambiado();
            dtpHasta.ValueChanged += (s, e) => HastaCambiado();
            dtpFin.ValueChanged += (s, e) => FinCambiado();
            dtpDesde.Leave += (s, e) => GuardarFechasSiCambiaron();
            dtpHasta.Leave += (s, e) => GuardarFechasSiCambiaron();
            txtNombre.TextChanged += (s, e) => NombreCambiado();
            chkGuardar.CheckedChanged += (s, e) =>
            {
                txtGuardarComo.Enabled = chkGuardar.Checked && chkGuardar.Enabled;
                if (!ajustando && recordado != null)
                {
                    recordado.Guardar = chkGuardar.Checked;
                }
            };
            txtGuardarComo.TextChanged += (s, e) =>
            {
                if (!ajustando && recordado != null)
                {
                    recordado.GuardarComo = txtGuardarComo.Text;
                    recordado.GuardarTocado = true;
                }
            };
            temporizador.Tick += (s, e) => LanzarRevision();
            relojAhora.Tick += (s, e) => RelojAhora();
            relojAhora.Start();

            // --- D. Aplicado y aun sin empezar ----------------------------------
            vistaD = NuevaVista();
            tarjetaD = new Tarjeta { Borde = Color.DarkGreen };
            lblTituloD = Titulo("", 14F);
            lblTituloD.AutoSize = false;
            lblTituloD.AutoEllipsis = true;
            lblRotAplicado = RotuloDeRenglon("Aplicado:");
            lblAplicado = Texto(9.5F, TextoOscuro);
            lblRotEmpieza = RotuloDeRenglon("Empieza:");
            lblEmpieza = Texto(9.5F, TextoOscuro);
            lblRotTermina = RotuloDeRenglon("Termina:");
            lblTermina = Texto(9.5F, TextoOscuro);
            lblRotAnterior = RotuloDeRenglon("El horario anterior terminó:");
            lblAnterior = Texto(9.5F, TextoOscuro);
            btnVerHorariosD = EstiloHorarios.CrearBoton("Ver en Horario semanal", Point.Empty, new Size(210, 36));
            btnVerHorariosD.Click += (s, e) =>
            {
                if (situacion != null && situacion.PorEmpezar != null && situacion.PorEmpezar.Desde.HasValue)
                {
                    IrASemanaDeHorarios(CuadriculaSemanal.LunesDe(situacion.PorEmpezar.Desde.Value));
                }
            };
            btnVerConfiguracionD = EstiloHorarios.CrearBoton("Verlo en «1. Plantillas»", Point.Empty, new Size(220, 36));
            btnVerConfiguracionD.Click += (s, e) =>
            {
                if (situacion != null && situacion.PorEmpezar != null)
                {
                    IrAConfiguraciones(situacion.PorEmpezar.Id, FiltroClases.Ninguno);
                }
            };
            btnDeshacer = EstiloHorarios.CrearBoton("Deshacer la aplicación...", Point.Empty, new Size(230, 36));
            btnDeshacer.Click += (s, e) => PulsarDeshacer();
            lblNotaD = Texto(9F, TextoGris);
            tarjetaD.Controls.AddRange(new Control[]
            {
                lblTituloD, lblRotAplicado, lblAplicado, lblRotEmpieza, lblEmpieza, lblRotTermina, lblTermina, lblRotAnterior, lblAnterior,
                btnVerHorariosD, btnVerConfiguracionD, btnDeshacer, lblNotaD
            });
            AyudaSobreDesactivado(tarjetaD, btnDeshacer, () => motivoDeshacer);
            lblEstadoD = LineaDeEstado();
            lblAplicadosD = Titulo("Horarios aplicados", 10.5F);
            lnkTodosD = Enlace("Verlos en «1. Plantillas»", (s, e) => PedirPlantillas());
            gridD = TablaDeAplicadas();
            lblVacioD = Texto(9.5F, TextoGris);
            lblVacioD.Text = "Todavía no se ha aplicado ningún horario.";
            vistaD.Controls.AddRange(new Control[] { tarjetaD, lblEstadoD, lblAplicadosD, lnkTodosD, gridD, lblVacioD });

            Controls.AddRange(new Control[] { vistaBase, vistaB, vistaC, vistaD });
            MostrarVista(vistaB);
            Resize += (s, e) => Acomodar();
        }

        private Func<int> IdAdministrador { get; }

        private Action<DateTime> IrASemanaDeHorarios { get; }

        private Action<int?, FiltroClases> IrAConfiguraciones { get; }

        /// <summary>
        /// "Ir a <<2. Preparacion>>" (sin nada que aplicar): lo pone quien la aloja
        /// (PestanaProximoCiclo). Sin el, lleva a la lista (irAConfiguraciones con null).
        /// </summary>
        public Action IrAPreparacion { get; set; }

        /// <summary>"Verlos en <<1. Plantillas>>": lo pone quien la aloja. Sin el, irAConfiguraciones con null.</summary>
        public Action IrAPlantillas { get; set; }

        private void PedirPreparacion()
        {
            if (IrAPreparacion != null)
            {
                IrAPreparacion();
            }
            else
            {
                IrAConfiguraciones(null, FiltroClases.Ninguno);
            }
        }

        private void PedirPlantillas()
        {
            if (IrAPlantillas != null)
            {
                IrAPlantillas();
            }
            else
            {
                IrAConfiguraciones(null, FiltroClases.Ninguno);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lectura++;
                temporizador.Stop();
                temporizador.Dispose();
                relojAhora.Stop();
                relojAhora.Dispose();
                ayuda.Dispose();
                fuenteConclusion.Dispose();
                fuenteClave.Dispose();
            }

            base.Dispose(disposing);
        }

        // =====================================================================
        // Lectura
        // =====================================================================

        /// <summary>
        /// Al elegir la pestana y con F5: lee lo que hay (LeerSituacion) y ensena
        /// la forma que toca: sin base, D (una aplicada por empezar), C (el
        /// horario en preparacion; nunca en solo consulta) o B.
        /// </summary>
        public void Cargar()
        {
            if (ocupado || IsDisposed)
            {
                return;
            }

            temporizador.Stop();
            // Una revision que llegue despues ya no vale.
            lectura++;
            revisando = false;
            string mensaje = mensajePendiente;
            bool mensajeEsError = mensajePendienteEsError;
            mensajePendiente = null;
            // Lo que se veia, para avisar si otra ventana u otro equipo lo cambio.
            CicloPreliminar antesEnC = vistaActual == vistaC ? borrador : null;
            CicloPreliminar antesEnD = vistaActual == vistaD && situacion != null ? situacion.PorEmpezar : null;

            SituacionConfiguraciones leida;
            InfoDeAplicada info = null;
            string errorInfo = null;
            DateTime? finComun = null;
            Cursor = Cursors.WaitCursor;
            try
            {
                leida = datos.LeerSituacion();
            }
            catch (Exception ex)
            {
                Log.Error("PestanaAplicacion.Cargar", ex);
                Cursor = Cursors.Default;
                MostrarBase("No se pudo leer", AvisoDeFalloAdmin.Texto("No se pudo leer lo que hay que aplicar", ex));
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            // Lo que solo completa la vista: si falla, la pestana se ensena igual.
            if (!FaltaBase(leida))
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    if (leida.PorEmpezar != null)
                    {
                        info = datos.LeerAplicada(leida.PorEmpezar.Id);
                    }
                    else if (leida.Borrador != null && !Permisos.SoloConsulta)
                    {
                        // Para proponer el primer dia y para la linea de tiempo (PintarLinea).
                        finComun = datos.FinMasComunDeHoy();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("PestanaAplicacion.Cargar (detalle)", ex);
                    errorInfo = AvisoDeFalloAdmin.Texto("No se pudo revisar si se puede deshacer", ex);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }

            situacion = leida;
            hoy = leida.Hoy;
            infoAplicada = info;
            finDeLasDeHoy = finComun;
            if (FaltaBase(leida))
            {
                MostrarSinBase(leida);
            }
            else if (leida.PorEmpezar != null)
            {
                MostrarD(leida.PorEmpezar, info, errorInfo);
            }
            else if (leida.Borrador != null && !Permisos.SoloConsulta)
            {
                MostrarC(leida.Borrador, finComun);
            }
            else
            {
                MostrarB();
            }

            if (mensaje == null && !FaltaBase(leida))
            {
                mensaje = CambioDesdeOtraVentana(leida, antesEnC, antesEnD);
                mensajeEsError = false;
            }

            if (mensaje != null)
            {
                Estado(mensaje, mensajeEsError);
            }
        }

        /// <summary>
        /// Si lo que se veia (el horario en preparacion de C o el aplicado de D)
        /// ya no esta asi, porque otra pestana, otra ventana u otro equipo lo
        /// aplico, lo descarto o deshizo su aplicacion: el aviso para la linea de
        /// estado, o null.
        /// </summary>
        private static string CambioDesdeOtraVentana(SituacionConfiguraciones leida, CicloPreliminar antesEnC, CicloPreliminar antesEnD)
        {
            const string Donde = " mientras tanto";
            if (antesEnC != null)
            {
                if (leida.Borrador != null && leida.Borrador.Id == antesEnC.Id)
                {
                    return null;
                }

                if (leida.PorEmpezar != null && leida.PorEmpezar.Id == antesEnC.Id)
                {
                    return "«" + leida.PorEmpezar.Nombre + "» ya se aplicó" + Donde + ".";
                }

                return leida.Borrador != null
                    ? "El horario en preparación cambió" + Donde + ": ahora es «" + leida.Borrador.Nombre + "»."
                    : "«" + antesEnC.Nombre + "» ya no está en preparación: se aplicó o se descartó" + Donde + ".";
            }

            if (antesEnD != null)
            {
                if (leida.PorEmpezar != null && leida.PorEmpezar.Id == antesEnD.Id)
                {
                    return null;
                }

                if (leida.Borrador != null && leida.Borrador.Id == antesEnD.Id)
                {
                    return "La aplicación de «" + antesEnD.Nombre + "» se deshizo" + Donde + ": vuelve a estar en preparación.";
                }

                CicloPreliminar ahora = leida.Aplicadas.FirstOrDefault(c => c.Id == antesEnD.Id);
                return ahora != null && ahora.Desde.HasValue && ahora.Desde.Value.Date <= leida.Hoy.Date
                    ? "«" + ahora.Nombre + "» ya empezó: los cambios se hacen en Horario semanal y Clases."
                    : "«" + antesEnD.Nombre + "» ya no está por empezar: se cambió" + Donde + ".";
            }

            return null;
        }

        /// <summary>Falta lo de la base para lo que ve esta cuenta: la 16 y la 23 para ver; la 09 tambien para aplicar.</summary>
        private static bool FaltaBase(SituacionConfiguraciones s)
        {
            return s.SinTablas || s.SinGuardados || (!Permisos.SoloConsulta && s.SinCodigoPorProfesor);
        }

        private void MostrarSinBase(SituacionConfiguraciones s)
        {
            string texto;
            if (Permisos.SoloConsulta)
            {
                texto = "Para ver los horarios aplicados, quien administra el servidor tiene que aplicar db/23_horarios_guardados.sql con root.";
            }
            else if (s.SinTablas || s.SinGuardados)
            {
                texto = "Para guardar y aplicar horarios, la base necesita las actualizaciones db/16_ciclo_preliminar.sql y db/23_horarios_guardados.sql. "
                    + "Crean tablas nuevas y no cambian nada de lo que ya hay. Las aplica quien administra el servidor con deploy\\migrar_bd.ps1."
                    + (s.SinCodigoPorProfesor ? Environment.NewLine + Environment.NewLine
                        + "Para aplicar horarios, la base necesita el código por docente (db/09_codigo_por_profesor.sql)." : "");
            }
            else
            {
                texto = "Para aplicar horarios, la base necesita el código por docente (db/09_codigo_por_profesor.sql).";
            }

            MostrarBase("Falta preparar la base", texto);
        }

        private void MostrarBase(string titulo, string texto)
        {
            borrador = null;
            lblTituloBase.Text = titulo;
            lblTextoBase.Text = texto;
            MostrarVista(vistaBase);
        }

        private void MostrarVista(Panel vista)
        {
            if (vista != vistaC)
            {
                borrador = null;
            }

            foreach (Panel v in new[] { vistaBase, vistaB, vistaC, vistaD })
            {
                v.Visible = v == vista;
            }

            // A dos columnas el pie de C no esta dentro de vistaC: se esconde con ella.
            pieC.Visible = vista == vistaC;
            vistaActual = vista;
            Acomodar();
        }

        // =====================================================================
        // B. Nada que aplicar
        // =====================================================================

        private void MostrarB()
        {
            bool consulta = Permisos.SoloConsulta;
            lblTextoB.Text = consulta
                ? "Solo consulta: aquí se ve lo que se aplicó."
                : "Aplicar cambia el horario que se usa en los laboratorios, desde ese momento o desde una fecha. Se aplica el horario en preparación: "
                  + "créalo en «2. Preparación» como copia de otro, solo con sus clases o en blanco.";
            btnIrB.Visible = !consulta;
            lblEstadoB.Text = "";
            LlenarAplicadas(gridB, lblVacioB);
            MostrarVista(vistaB);
        }

        // =====================================================================
        // D. Aplicado y aun sin empezar
        // =====================================================================

        /// <param name="errorInfo">Si no se pudo leer info (cuantas recorto y si se puede deshacer): por que.</param>
        private void MostrarD(CicloPreliminar aplicada, InfoDeAplicada info, string errorInfo)
        {
            DateTime desde = aplicada.Desde ?? hoy;
            DateTime hasta = aplicada.Hasta ?? desde;
            int dias = (desde.Date - hoy.Date).Days;
            lblTituloD.Text = aplicada.Nombre + " ya está aplicado";
            ayuda.SetToolTip(lblTituloD, lblTituloD.Text);
            lblAplicado.Text = (aplicada.PublicadoEn.HasValue
                    ? HistorialCambios.DiaYFecha(aplicada.PublicadoEn.Value) + " a las " + aplicada.PublicadoEn.Value.ToString("HH:mm", Invariante)
                    : "—")
                + (aplicada.PublicadoPor.Length > 0 ? ", por " + aplicada.PublicadoPor : "");
            lblEmpieza.Text = HistorialCambios.DiaYFecha(desde) + " (" + (dias == 1 ? "mañana" : "en " + dias + " días") + "), con "
                + RevisionCiclo.TextoClases(aplicada.Clases) + " y " + RevisionCiclo.TextoHorarios(aplicada.Horarios);
            lblTermina.Text = HistorialCambios.DiaYFecha(hasta);
            if (aplicada.FinAnterior.HasValue)
            {
                lblRotAnterior.Text = aplicada.FinAnterior.Value.Date < hoy.Date ? "El horario anterior terminó:" : "El horario anterior termina:";
                lblAnterior.Text = HistorialCambios.DiaYFecha(aplicada.FinAnterior.Value)
                    + (info == null ? "" : " (" + RevisionCiclo.TextoClases(info.Recortadas) + ")");
            }
            else
            {
                lblRotAnterior.Text = "El horario anterior:";
                lblAnterior.Text = "no se recortó.";
            }

            bool consulta = Permisos.SoloConsulta;
            string vispera = HistorialCambios.DiaYFecha(desde.AddDays(-1));
            lblNotaD.Text = consulta
                ? "La administración puede deshacerlo hasta el " + vispera + ", mientras ninguna de sus clases tenga sesiones."
                : "Se puede deshacer hasta el " + vispera + ", mientras ninguna de sus clases tenga sesiones: vuelve a quedar en preparación y las "
                  + "clases de antes recuperan su fecha de fin. Para retocarlo antes de que empiece, hazlo en Horario semanal y Clases: todavía no "
                  + "tiene historia y se cambia en su sitio.";
            btnDeshacer.Visible = !consulta;
            // En solo consulta, ni el porque (podria nombrar el horario en preparacion).
            motivoDeshacer = consulta ? "" : info == null ? errorInfo ?? "" : info.Motivo ?? "";
            btnDeshacer.Enabled = !consulta && info != null && info.Motivo == null;
            btnDeshacer.AccessibleDescription = motivoDeshacer.Length > 0 ? motivoDeshacer : null;
            ayuda.SetToolTip(btnDeshacer, btnDeshacer.Enabled
                ? "Pide la contraseña de la administración. Se borran sus clases y las de antes recuperan su fecha de fin."
                : motivoDeshacer);
            lblEstadoD.Text = "";
            LlenarAplicadas(gridD, lblVacioD);
            MostrarVista(vistaD);
        }

        private void PulsarDeshacer()
        {
            if (Permisos.Bloquear(Dueno(), "Deshacer la aplicacion de un horario"))
            {
                return;
            }

            CicloPreliminar aplicada = situacion == null ? null : situacion.PorEmpezar;
            if (ocupado || vistaActual != vistaD || aplicada == null || infoAplicada == null || infoAplicada.Motivo != null)
            {
                return;
            }

            int recortadas = infoAplicada.Recortadas;
            int creadas = infoAplicada.Creadas;
            string detalle = (creadas == 1 ? "Se borra su clase con sus horarios y " : "Se borran sus " + creadas + " clases con sus horarios y ")
                + (recortadas == 0 ? "ninguna clase de antes cambia"
                    : recortadas == 1 ? "la de antes vuelve a valer hasta donde valía"
                    : "las " + recortadas + " de antes vuelven a valer hasta donde valían")
                + ". Lo que les hayas cambiado en Horario semanal o Clases después de aplicarlo se pierde. El horario vuelve a quedar en preparación.";
            if (!DialogoContrasenaAdmin.Confirmar(Dueno(), "¿Deshacer la aplicación de «" + aplicada.Nombre + "»?", detalle, "Deshacer"))
            {
                return;
            }

            // Mientras se confirmaba pudo volver a leerse la pestana: solo la misma aplicada.
            if (IsDisposed || ocupado || vistaActual != vistaD || situacion == null || situacion.PorEmpezar == null || situacion.PorEmpezar.Id != aplicada.Id)
            {
                return;
            }

            ResultadoDeshacer resultado = null;
            Exception fallo = null;
            Ocupado(true, lblEstadoD, "Deshaciendo... no cierres la ventana.");
            try
            {
                resultado = datos.Deshacer(aplicada.Id, IdAdministrador());
            }
            catch (Exception ex)
            {
                fallo = ex;
            }
            finally
            {
                Ocupado(false, null, null);
            }

            if (fallo != null)
            {
                ExplicarFallo("PestanaAplicacion.Deshacer", fallo, "No se deshizo");
                return;
            }

            List<string> partes = new List<string> { "Listo: se deshizo." };
            if (resultado.Devueltas == 1)
            {
                partes.Add("La clase de antes vuelve a valer hasta donde valía.");
            }
            else if (resultado.Devueltas > 1)
            {
                partes.Add("Las " + resultado.Devueltas + " clases de antes vuelven a valer hasta donde valían.");
            }

            if (resultado.NoDevueltas > 0)
            {
                partes.Add(resultado.NoDevueltas == 1
                    ? "1 no se tocó porque se le cambió la fecha después."
                    : resultado.NoDevueltas + " no se tocaron porque se les cambió la fecha después.");
            }

            if (resultado.PorOtraIgual > 0)
            {
                partes.Add(resultado.PorOtraIgual == 1
                    ? "1 no se tocó porque ya hay otra clase igual en esas fechas."
                    : resultado.PorOtraIgual + " no se tocaron porque ya hay otras clases iguales en esas fechas.");
            }

            mensajePendiente = string.Join(" ", partes);
            mensajePendienteEsError = false;
            Cargar();
        }

        // =====================================================================
        // C. La pagina de aplicar
        // =====================================================================

        private void MostrarC(CicloPreliminar enPreparacion, DateTime? finComun)
        {
            bool mismo = borrador != null && borrador.Id == enPreparacion.Id && vistaActual == vistaC;
            borrador = enPreparacion;
            string clave = ClaveRecordada(enPreparacion);
            if (!recordados.TryGetValue(clave, out recordado))
            {
                recordado = new Recordado();
                recordados[clave] = recordado;
            }

            ajustando = true;
            try
            {
                if (!mismo)
                {
                    recargadoPorRevision = false;
                    // Por omision, "Ahora mismo" (empieza al aplicarlo), salvo que ya tenga
                    // fechas guardadas (las eligio con "Otro dia" o vuelve de Deshacer).
                    bool ahora = recordado.Ahora ?? !enPreparacion.Desde.HasValue;
                    rbAhora.Checked = ahora;
                    rbOtroDia.Checked = !ahora;
                    // dtpDesde es siempre la fecha de "Otro dia" (y la que se guarda como propuesta).
                    DateTime desde = enPreparacion.Desde ?? PrimerDiaPropuesto(finComun);
                    DateTime empieza = ahora ? hoy.Date : desde;
                    DateTime hasta = enPreparacion.Hasta ?? (recordado.HastaTocado && recordado.Hasta.HasValue ? recordado.Hasta.Value : empieza.AddMonths(5).AddDays(-1));
                    DateTime fin = recordado.FinTocado && recordado.Fin.HasValue ? recordado.Fin.Value : desde.AddDays(-1);
                    PonerFecha(dtpDesde, desde);
                    PonerFecha(dtpHasta, hasta);
                    PonerFecha(dtpFin, ahora ? hoy.Date.AddDays(-1) : fin);
                    txtNombre.Text = recordado.NombreTocado && recordado.Nombre != null ? recordado.Nombre : NombrePropuesto();
                    chkGuardar.Checked = recordado.Guardar ?? true;
                    txtGuardarComo.Text = recordado.GuardarTocado && recordado.GuardarComo != null ? recordado.GuardarComo : "";
                    lblEstadoC.Text = "";
                }
                else if (EmpiezaAhora)
                {
                    // La misma pagina vuelta a leer (otra vez la pestana o F5), quiza
                    // despues de la medianoche: Cargar ya puso el hoy del servidor y con
                    // "Ahora mismo" el ultimo dia del de ahora es siempre ayer.
                    PonerFecha(dtpFin, hoy.Date.AddDays(-1));
                    if (!recordado.NombreTocado)
                    {
                        txtNombre.Text = NombrePropuesto();
                    }
                }

                desdeGuardado = enPreparacion.Desde;
                hastaGuardado = enPreparacion.Hasta;
                lblTituloC.Text = "Aplicar «" + enPreparacion.Nombre + "»";
                ayuda.SetToolTip(lblTituloC, lblTituloC.Text);
                lblResumenC.Text = RevisionCiclo.Resumen(enPreparacion) + ".";
            }
            finally
            {
                ajustando = false;
            }

            revisado = null;
            renglones = null;
            nombreRevisado = null;
            errorRevision = null;
            anchoRenglones = -1;
            PonerModo();
            PintarAlAplicar();
            MostrarVista(vistaC);
            LanzarRevision();
        }

        /// <summary>Lo que dice junto a "Antes de aplicar" cuando ya reviso (PonerRevisando le guarda sitio).</summary>
        private const string TextoSeRevisa = "Se revisa al cambiar las fechas.";

        /// <summary>La nota bajo el ultimo dia del horario en uso, con "Otro dia".</summary>
        private const string NotaFinOtroDia = "Por omisión, la víspera del primer día. Si antes hay vacaciones, pon el último día de clases: "
            + "las vacaciones no cuentan como faltas.";

        /// <summary>Bajo "Antes de aplicar" mientras se revisa: que quiere decir cada color.</summary>
        private const string ClaveDeColores = "Lo rojo no deja aplicar. Lo ámbar es un aviso.";

        /// <summary>"Ahora mismo": empieza al aplicarlo (hoy, y el horario de ahora hasta ayer).</summary>
        private bool EmpiezaAhora
        {
            get { return rbAhora.Checked; }
        }

        /// <summary>El primer dia del horario nuevo: hoy (del servidor) con "Ahora mismo"; si no, el de "Otro dia".</summary>
        private DateTime DesdeElegido()
        {
            return EmpiezaAhora ? hoy.Date : dtpDesde.Value.Date;
        }

        /// <summary>El ultimo dia del horario de ahora: ayer con "Ahora mismo"; si no, el que se eligio.</summary>
        private DateTime FinElegido()
        {
            return EmpiezaAhora ? hoy.Date.AddDays(-1) : dtpFin.Value.Date;
        }

        /// <summary>
        /// Lo que cambia con la opcion: la tarjeta elegida, y con "Ahora mismo" la
        /// fecha de "Otro dia" apagada y sin el ultimo dia del horario en uso, que
        /// es ayer y no se elige (AcomodarC no lo coloca): lo dice la linea de tiempo.
        /// </summary>
        private void PonerModo()
        {
            bool ahora = EmpiezaAhora;
            dtpDesde.Enabled = !ahora;
            diaDesde.ForeColor = ahora ? PuntoGris : Color.FromArgb(80, 80, 80);
            dtpFin.Enabled = !ahora;
            lblRotFin.Visible = !ahora;
            dtpFin.Visible = !ahora;
            diaFin.Visible = !ahora;
            lblNotaFin.Visible = !ahora;
            lblHoyAhora.Text = "Hoy, " + HistorialCambios.DiaYFecha(hoy);
            lblHoyAhora.ForeColor = ahora ? TextoOscuro : TextoGris;
            rbAhora.AccessibleDescription = "Hoy, " + HistorialCambios.DiaYFecha(hoy) + ". El horario en uso termina ayer.";
            PintarTarjeta(tarjetaAhora, ahora);
            PintarTarjeta(tarjetaOtroDia, !ahora);
        }

        /// <summary>La elegida, con borde y fondo verdes; la otra, apagada y con la mano de "se puede pulsar".</summary>
        private static void PintarTarjeta(Tarjeta tarjeta, bool elegida)
        {
            tarjeta.Borde = elegida ? EstiloHorarios.VerdeTeschi : Color.FromArgb(205, 205, 205);
            tarjeta.Grosor = elegida ? 2 : 1;
            tarjeta.Fondo = elegida ? Color.FromArgb(240, 248, 243) : Color.White;
            tarjeta.Cursor = elegida ? Cursors.Default : Cursors.Hand;
            tarjeta.Invalidate(true);
        }

        /// <summary>Se eligio "Ahora mismo" u "Otro dia": las fechas que dependen de ello, el nombre propuesto y otra revision.</summary>
        private void ModoCambiado()
        {
            if (ajustando || borrador == null)
            {
                return;
            }

            recordado.Ahora = rbAhora.Checked;
            ajustando = true;
            try
            {
                if (!recordado.HastaTocado && !borrador.Hasta.HasValue)
                {
                    PonerFecha(dtpHasta, DesdeElegido().AddMonths(5).AddDays(-1));
                }

                PonerFecha(dtpFin, EmpiezaAhora ? hoy.Date.AddDays(-1)
                    : recordado.FinTocado && recordado.Fin.HasValue ? recordado.Fin.Value : dtpDesde.Value.Date.AddDays(-1));
                if (!recordado.NombreTocado)
                {
                    txtNombre.Text = NombrePropuesto();
                }
            }
            finally
            {
                ajustando = false;
            }

            PonerModo();
            AcomodarC();
            PedirRevision();
        }

        /// <summary>
        /// Cada 30 s con "Ahora mismo": lo que depende de la hora (las clases de
        /// hoy que ya terminaron) se rehace en memoria con la hora del servidor
        /// estimada (DatosDeAplicacion.AhoraEstimado); solo se vuelve a pintar si
        /// cambio algo. Si en el servidor ya es otro dia, se vuelve a leer.
        /// </summary>
        private void RelojAhora()
        {
            if (IsDisposed || !Visible || vistaActual != vistaC || borrador == null || !EmpiezaAhora || revisado == null
                || renglones == null || revisando || ocupado || errorRevision != null)
            {
                return;
            }

            if (revisado.AhoraEstimado.Date != revisado.Hoy.Date)
            {
                LanzarRevision();
                return;
            }

            List<RenglonRevision> nuevos;
            try
            {
                nuevos = RevisionAplicacion.Revisar(revisado, revisado.Fechas.Desde, revisado.Fechas.Hasta, revisado.Fechas.Fin, txtNombre.Text);
            }
            catch (Exception ex)
            {
                Log.Error("PestanaAplicacion.RelojAhora", ex);
                return;
            }

            if (!nuevos.Select(r => r.Nivel + "|" + r.Texto).SequenceEqual(renglones.Select(r => r.Nivel + "|" + r.Texto)))
            {
                // La lista se rehace sola: que no vuelva arriba si se estaba leyendo abajo.
                conservarDesplazamiento = true;
                try
                {
                    RehacerRevision();
                }
                finally
                {
                    conservarDesplazamiento = false;
                }
            }
        }

        /// <summary>
        /// La clave de lo recordado: quien tiene la sesion y que horario en
        /// preparacion (su numero y cuando se creo, que no se repiten juntos en
        /// otra base). Otra sesion u otra base empiezan sin nada recordado.
        /// </summary>
        private static string ClaveRecordada(CicloPreliminar enPreparacion)
        {
            return (Datos_User.getUser() ?? "") + "|" + Datos_User.TipoUsuario.ToString(Invariante) + "|"
                + enPreparacion.Id.ToString(Invariante) + "|" + enPreparacion.CreadoEn.Ticks.ToString(Invariante);
        }

        /// <summary>
        /// El primer dia que se propone: el siguiente a la fecha de fin mas comun
        /// de las clases que valen hoy; si eso no es despues de hoy, manana. Un
        /// domingo no se propone: no hay clases, y el horario empezaria en vacio.
        /// </summary>
        private DateTime PrimerDiaPropuesto(DateTime? finComun)
        {
            DateTime propuesto = finComun.HasValue ? finComun.Value.Date.AddDays(1) : hoy.Date.AddDays(1);
            propuesto = propuesto > hoy.Date ? propuesto : hoy.Date.AddDays(1);
            return propuesto.DayOfWeek == DayOfWeek.Sunday ? propuesto.AddDays(1) : propuesto;
        }

        /// <summary>NombrePropuesto(desde, hasta) si se sigue llamando "Proximo ciclo"; si no, su nombre.</summary>
        private string NombrePropuesto()
        {
            if (borrador == null)
            {
                return "";
            }

            return borrador.Nombre == CicloPreliminarDatos.NombrePreparacion
                ? CicloPreliminarDatos.NombrePropuesto(DesdeElegido(), dtpHasta.Value.Date)
                : borrador.Nombre;
        }

        private void DesdeCambiado()
        {
            // Con "Ahora mismo" la fecha de "Otro dia" esta apagada: no manda.
            if (ajustando || borrador == null || EmpiezaAhora)
            {
                return;
            }

            ajustando = true;
            try
            {
                DateTime desde = dtpDesde.Value.Date;
                if (!recordado.HastaTocado && !borrador.Hasta.HasValue)
                {
                    PonerFecha(dtpHasta, desde.AddMonths(5).AddDays(-1));
                }

                if (!recordado.FinTocado)
                {
                    PonerFecha(dtpFin, desde.AddDays(-1));
                }

                if (!recordado.NombreTocado)
                {
                    txtNombre.Text = NombrePropuesto();
                }
            }
            finally
            {
                ajustando = false;
            }

            PedirRevision();
        }

        private void HastaCambiado()
        {
            if (ajustando || borrador == null)
            {
                return;
            }

            recordado.HastaTocado = true;
            recordado.Hasta = dtpHasta.Value.Date;
            if (!recordado.NombreTocado)
            {
                ajustando = true;
                try
                {
                    txtNombre.Text = NombrePropuesto();
                }
                finally
                {
                    ajustando = false;
                }
            }

            PedirRevision();
        }

        private void FinCambiado()
        {
            // Con "Ahora mismo" es ayer y no se cambia (y no se recuerda como tocado).
            if (ajustando || borrador == null || EmpiezaAhora)
            {
                return;
            }

            recordado.FinTocado = true;
            recordado.Fin = dtpFin.Value.Date;
            PedirRevision();
        }

        private void NombreCambiado()
        {
            if (ajustando || borrador == null)
            {
                return;
            }

            recordado.Nombre = txtNombre.Text;
            recordado.NombreTocado = true;
            // El nombre se revisa en memoria: no hace falta volver a leer.
            if (!revisando && revisado != null)
            {
                RehacerRevision();
            }
        }

        /// <summary>El primer y el ultimo dia se guardan con el horario en preparacion al salir de cada fecha (GuardarFechas).</summary>
        private void GuardarFechasSiCambiaron()
        {
            if (borrador == null || Permisos.SoloConsulta || vistaActual != vistaC || ocupado || IsDisposed)
            {
                return;
            }

            // Con "Ahora mismo" el primer dia no se ha elegido: se queda el que estuviera
            // guardado (si no, la proxima vez propondria "Otro dia" sin que nadie lo pidiera).
            DateTime? desde = EmpiezaAhora ? desdeGuardado : dtpDesde.Value.Date;
            DateTime hasta = dtpHasta.Value.Date;
            if ((desdeGuardado == desde && hastaGuardado == hasta) || hasta < desde)
            {
                // Sin cambios, o al reves: eso ya lo dice en rojo la revision.
                return;
            }

            try
            {
                datos.GuardarFechas(borrador.Id, desde, hasta, IdAdministrador());
                desdeGuardado = desde;
                hastaGuardado = hasta;
            }
            catch (Exception ex)
            {
                Log.Error("PestanaAplicacion.GuardarFechas", ex);
                string accion = AvisoDeFalloAdmin.Accion(ex);
                Estado("No se guardaron las fechas: " + Minuscula(AvisoDeFalloAdmin.Causa(ex)) + "." + (accion.Length > 0 ? " " + accion : ""), true);
                if (ex is ErrorDeDatos && IsHandleCreated)
                {
                    // Otra pestana u otro equipo lo aplico o lo descarto: se vuelve a
                    // leer despues de salir de la fecha (Cargar dice que cambio).
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed && !ocupado && vistaActual == vistaC)
                        {
                            Cargar();
                        }
                    }));
                }
            }
        }

        /// <summary>500 ms despues del ultimo cambio de una fecha se vuelve a revisar; mientras, no se aplica.</summary>
        private void PedirRevision()
        {
            temporizador.Stop();
            lectura++;
            revisando = true;
            PintarAlAplicar();
            ActualizarBotonAplicar();
            lblRevisando.Text = "Revisando...";
            lnkRevisarOtraVez.Visible = false;
            AcomodarC();
            temporizador.Start();
        }

        /// <summary>
        /// Lee en segundo plano lo que revisa "Antes de aplicar"; una lectura
        /// vieja se descarta (lectura) y nunca hay dos a la vez: si ya hay una,
        /// esta se lanza al terminar aquella, con las fechas de ese momento.
        /// </summary>
        private void LanzarRevision()
        {
            temporizador.Stop();
            if (borrador == null || vistaActual != vistaC || IsDisposed || ocupado)
            {
                return;
            }

            // Lo que llegue de una lectura anterior ya no vale.
            int esta = ++lectura;
            revisando = true;
            errorRevision = null;
            lblRevisando.Text = "Revisando...";
            lnkRevisarOtraVez.Visible = false;
            ActualizarBotonAplicar();
            if (revisionEnCurso != null && !revisionEnCurso.IsCompleted)
            {
                otraRevision = true;
                return;
            }

            otraRevision = false;
            int id = borrador.Id;
            // Con "Ahora mismo" el primer dia (hoy) y el ultimo del de ahora (ayer) los
            // pone el servidor: no el reloj de este equipo.
            bool ahora = EmpiezaAhora;
            DateTime desde = dtpDesde.Value.Date;
            DateTime hasta = dtpHasta.Value.Date;
            DateTime fin = dtpFin.Value.Date;
            revisionEnCurso = Task.Run(() =>
            {
                DatosDeAplicacion leidos = null;
                Exception fallo = null;
                try
                {
                    leidos = ahora
                        ? new CicloPreliminarDatos().LeerParaAplicarAhora(id, hasta)
                        : new CicloPreliminarDatos().LeerParaAplicar(id, desde, hasta, fin);
                }
                catch (Exception ex)
                {
                    fallo = ex;
                }

                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((Action)(() => RevisionLeida(esta, leidos, fallo)));
                    }
                }
                catch (InvalidOperationException)
                {
                    // La ventana se cerro mientras se leia.
                }
            });
        }

        private void RevisionLeida(int esta, DatosDeAplicacion leidos, Exception fallo)
        {
            if (IsDisposed)
            {
                return;
            }

            // Esta lectura ya termino (su tarea solo sale despues de pedir esto): si su
            // Task aun no se marca como terminada, LanzarRevision la veria en curso,
            // volveria a poner otraRevision y nadie la relanzaria ("Revisando..." para siempre).
            revisionEnCurso = null;

            // Se pidio otra mientras esta leia: esta ya no vale y se lanza aquella.
            if (otraRevision)
            {
                otraRevision = false;
                LanzarRevision();
                return;
            }

            if (esta != lectura || borrador == null || vistaActual != vistaC || ocupado)
            {
                return;
            }

            if (fallo == null && (leidos == null || leidos.Leido == null || leidos.Leido.Ciclo == null || leidos.Fechas == null))
            {
                fallo = new InvalidOperationException("LeerParaAplicar no devolvio lo que revisa la pagina.");
            }

            // Otra pestana, otra ventana u otro equipo lo aplico o lo descarto: se
            // vuelve a leer todo (una vez seguida) y Cargar dice que cambio.
            bool yaNoEsEste = fallo is ErrorDeDatos
                || (fallo == null && (leidos.Leido.Ciclo.Id != borrador.Id || !leidos.Leido.Ciclo.EnPreparacion));
            if (yaNoEsEste && !recargadoPorRevision)
            {
                recargadoPorRevision = true;
                Log.Aviso("PestanaAplicacion.LeerParaAplicar: el horario en preparacion cambio; se vuelve a leer la pestana.");
                Cargar();
                return;
            }

            revisando = false;
            if (fallo == null && yaNoEsEste)
            {
                fallo = new ErrorDeDatos("Este horario ya no está en preparación: pulsa F5 para ver lo que hay ahora.");
            }

            if (fallo != null)
            {
                if (fallo is ErrorDeDatos)
                {
                    Log.Aviso("PestanaAplicacion.LeerParaAplicar: " + fallo.Message);
                }
                else
                {
                    Log.Error("PestanaAplicacion.LeerParaAplicar", fallo);
                }

                revisado = null;
                renglones = null;
                nombreRevisado = null;
                string accion = AvisoDeFalloAdmin.Accion(fallo);
                errorRevision = "No se pudo revisar: " + Minuscula(AvisoDeFalloAdmin.Causa(fallo)) + ". "
                    + (accion.Length > 0 ? accion + " Luego pulsa «Revisar otra vez»." : "Pulsa «Revisar otra vez».");
            }
            else
            {
                recargadoPorRevision = false;
                revisado = leidos;
                errorRevision = null;
                SeguirHoyDelServidor(leidos.Hoy);
            }

            RehacerRevision();
        }

        /// <summary>
        /// Hoy es el del servidor (el de la ultima lectura): si paso la medianoche
        /// desde que se abrio la pestana, "Ahora mismo" pasa a ese dia (y el de
        /// ahora hasta la vispera) sin que la revision se quede esperando.
        /// </summary>
        private void SeguirHoyDelServidor(DateTime hoyLeido)
        {
            if (hoyLeido.Date == hoy.Date)
            {
                return;
            }

            hoy = hoyLeido.Date;
            ajustando = true;
            try
            {
                if (EmpiezaAhora)
                {
                    PonerFecha(dtpFin, hoy.AddDays(-1));
                }

                if (recordado != null && !recordado.NombreTocado)
                {
                    txtNombre.Text = NombrePropuesto();
                }
            }
            finally
            {
                ajustando = false;
            }

            PonerModo();
        }

        /// <summary>Los renglones con lo ultimo leido y el nombre de ahora (el nombre se revisa en memoria).</summary>
        private void RehacerRevision()
        {
            if (revisado != null)
            {
                try
                {
                    renglones = RevisionAplicacion.Revisar(revisado, revisado.Fechas.Desde, revisado.Fechas.Hasta, revisado.Fechas.Fin, txtNombre.Text);
                    nombreRevisado = txtNombre.Text.Trim();
                }
                catch (Exception ex)
                {
                    // Sin renglones no se aplica: lo dice en rojo y deja "Revisar otra vez".
                    Log.Error("PestanaAplicacion.Revisar", ex);
                    revisado = null;
                    renglones = null;
                    nombreRevisado = null;
                    errorRevision = "No se pudo revisar: " + Minuscula(AvisoDeFalloAdmin.Causa(ex)) + ". Pulsa «Revisar otra vez».";
                }
            }

            if (revisado != null && recordado != null && !recordado.GuardarTocado)
            {
                ajustando = true;
                try
                {
                    txtGuardarComo.Text = revisado.PropuestaGuardar ?? "";
                }
                finally
                {
                    ajustando = false;
                }
            }

            lblRevisando.Text = revisando ? "Revisando..." : errorRevision != null ? "" : TextoSeRevisa;
            lnkRevisarOtraVez.Visible = !revisando && errorRevision != null;
            PintarAlAplicar();
            anchoRenglones = -1;
            AcomodarC();
            ActualizarBotonAplicar();
        }

        /// <summary>Lo de "Al aplicar", con las cuentas de la ultima revision si ya la hay.</summary>
        private void PintarAlAplicar()
        {
            if (borrador == null)
            {
                return;
            }

            bool ahora = EmpiezaAhora;
            DateTime desde = DesdeElegido();
            DateTime hasta = dtpHasta.Value.Date;
            DateTime fin = FinElegido();
            bool vale = revisado != null && !revisando && MismasFechas(revisado);
            int clases = vale ? revisado.Clases : borrador.Clases;
            int horarios = vale ? revisado.Horarios : borrador.Horarios;
            PintarLinea();
            lblDuracion.Text = hasta < desde ? "" : LineaDeFechasHorario.Duracion((hasta - desde).Days + 1);
            // El boton dice que va a pasar. Su ayuda la pone ActualizarBotonAplicar.
            btnAplicar.Text = ahora ? "Aplicar ahora mismo..." : "Aplicar desde el " + HistorialCambios.Fecha(desde) + "...";
            // Las fechas ya las dice la linea de tiempo.
            lblCrean.Text = RevisionCiclo.TextoClases(clases) + " con " + RevisionCiclo.TextoHorarios(horarios) + ".";
            if (!vale)
            {
                lblTerminan.Text = ahora
                    ? "las clases de ahora, ayer, " + HistorialCambios.DiaYFecha(fin) + " (se cuentan al revisar)."
                    : "las clases que valen el " + HistorialCambios.DiaYFecha(fin) + " (se cuentan al revisar).";
                lblCodigos.Text = "cada docente sigue con el suyo.";
            }
            else
            {
                int n = revisado.Terminan;
                int apagadas = revisado.Terminan - revisado.TerminanActivas;
                string deEllas = apagadas > 0 ? " (" + (apagadas == 1 ? "1 de ellas desactivada" : apagadas + " de ellas desactivadas") + ")" : "";
                if (ahora)
                {
                    List<string> partes = new List<string>();
                    if (n > 0)
                    {
                        partes.Add((n == 1 ? "la clase de ahora" : "las " + n + " clases de ahora") + deEllas
                            + (n == 1 ? " termina" : " terminan") + " ayer, " + HistorialCambios.DiaYFecha(fin));
                    }

                    string deHoy = LasDeHoy(revisado);
                    if (deHoy != null)
                    {
                        partes.Add(deHoy);
                    }

                    lblTerminan.Text = partes.Count == 0
                        ? "ninguna clase de ahora sigue después de ayer: no se recorta nada."
                        : RevisionCiclo.Enumerar(partes) + ". Lo registrado hoy cuenta en su clase nueva o, si no sigue, en la de antes.";
                }
                else
                {
                    lblTerminan.Text = n == 0
                        ? "ninguna clase de ahora sigue después del " + HistorialCambios.DiaYFecha(fin) + ": no se recorta nada."
                        : (n == 1 ? "la clase que seguía después del " : "las " + n + " clases que seguían después del ") + HistorialCambios.DiaYFecha(fin)
                          + deEllas
                          + (n == 1 ? " termina ese día" : " terminan ese día")
                          + ". Sus sesiones, bitácoras, asistencias y faltas ya registradas no cambian.";
                }
                // Hasta tres nombres: con mas, la columna crecia y salia la barra a 1366.
                List<string> sinCodigo = revisado.DocentesSinCodigo ?? new List<string>();
                List<string> nombres = sinCodigo.Take(sinCodigo.Count > 3 ? 2 : 3).ToList();
                if (sinCodigo.Count > 3)
                {
                    nombres.Add((sinCodigo.Count - 2) + " más");
                }

                lblCodigos.Text = "cada docente sigue con el suyo."
                    + (sinCodigo.Count > 0 ? " Reciben código: " + RevisionCiclo.Enumerar(nombres) + "." : "");
                ayuda.SetToolTip(lblCodigos, sinCodigo.Count > 3 ? "Reciben código: " + RevisionCiclo.Enumerar(sinCodigo) + "." : null);
            }

            lblNadaCambia.Text = ahora
                ? "Empieza en cuanto lo apliques: desde ese momento los laboratorios y los paneles de los docentes usan el horario nuevo. "
                  + "Como empieza hoy, ya no se podrá deshacer."
                : fin < hoy.Date
                ? "Desde que lo apliques y hasta el " + HistorialCambios.Fecha(desde) + " no hay clases en los laboratorios: las de ahora terminaron el "
                  + HistorialCambios.Fecha(fin) + "."
                : "Hasta el " + HistorialCambios.Fecha(desde) + " nada cambia en los laboratorios ni en los paneles de los docentes.";
            // En ambar lo que pide fijarse (empieza ya, o unos dias sin clases); en azul, que nada cambia.
            bool pideFijarse = ahora || fin < hoy.Date;
            tarjetaNadaCambia.Fondo = pideFijarse ? Color.FromArgb(255, 248, 228) : PestanaProximoCiclo.AzulClaro;
            tarjetaNadaCambia.Borde = pideFijarse ? Color.FromArgb(236, 196, 110) : Color.FromArgb(160, 180, 205);
            tarjetaNadaCambia.Invalidate(true);

            bool hayQueGuardar = !vale || revisado.ValenElFin > 0;
            ajustando = true;
            try
            {
                chkGuardar.Enabled = hayQueGuardar;
                if (!hayQueGuardar)
                {
                    chkGuardar.Checked = false;
                }
                else if (!chkGuardar.Checked && recordado != null && (recordado.Guardar ?? true))
                {
                    chkGuardar.Checked = true;
                }
            }
            finally
            {
                ajustando = false;
            }

            txtGuardarComo.Enabled = chkGuardar.Checked && chkGuardar.Enabled;
            // Cuantas lleva: no todas las clases terminan el mismo dia, y la copia es de un solo dia.
            lblParaCargarlo.Text = !hayQueGuardar ? "No hay clases que guardar."
                : vale ? "Lleva " + RevisionCiclo.TextoClases(revisado.ValenElFin) + ", para usarlo en otro ciclo."
                : "Para usarlo en otro ciclo.";
        }

        /// <summary>
        /// La linea de tiempo con lo que se ve. Si hay horario de ahora lo dicen las
        /// clases que valen hoy y, si no se pudieron leer, las aplicadas en uso.
        /// </summary>
        private void PintarLinea()
        {
            List<CicloPreliminar> enUso = situacion == null ? new List<CicloPreliminar>() : situacion.Configuraciones.Where(c => c.EnUso(hoy)).ToList();
            CicloPreliminar unico = enUso.Count == 1 ? enUso[0] : null;
            bool hayEnUso = finDeLasDeHoy.HasValue || enUso.Count > 0;
            DateTime fin = FinElegido();
            // Poner un ultimo dia despues del suyo no alarga las clases: la barra acaba cuando acaban.
            DateTime? suFin = finDeLasDeHoy ?? (unico == null ? null : unico.UltimoDia);
            if (suFin.HasValue && suFin.Value.Date < fin)
            {
                fin = suFin.Value.Date;
            }

            bool sustituyeHoy = EmpiezaAhora && unico != null && unico.Desde.HasValue && unico.Desde.Value.Date == hoy.Date;
            string nombre = txtNombre.Text.Trim();
            linea.Poner(hayEnUso, unico == null ? null : unico.Nombre, sustituyeHoy, fin, nombre.Length > 0 ? nombre : borrador.Nombre,
                DesdeElegido(), dtpHasta.Value.Date, hoy.Date, EmpiezaAhora);
        }

        /// <summary>La revision es de lo que se ve: la misma opcion ("Ahora mismo" u "Otro dia") y las mismas fechas.</summary>
        private bool MismasFechas(DatosDeAplicacion d)
        {
            if (d == null || d.Fechas == null || d.Fechas.EmpiezaAhora != EmpiezaAhora || d.Fechas.Hasta != dtpHasta.Value.Date)
            {
                return false;
            }

            return EmpiezaAhora
                ? d.Fechas.Desde == hoy.Date && d.Fechas.Fin == hoy.Date.AddDays(-1)
                : d.Fechas.Desde == dtpDesde.Value.Date && d.Fechas.Fin == dtpFin.Value.Date;
        }

        /// <summary>
        /// Por que no se puede aplicar ahora (el primer texto rojo, o que se esta
        /// revisando), o null. Solo vale la revision del horario, las fechas y el
        /// nombre que se ven: son los que se mandan a Aplicar.
        /// </summary>
        private string MotivoParaNoAplicar()
        {
            if (ocupado)
            {
                return "Espera: se está aplicando.";
            }

            if (revisando)
            {
                return "Espera: se está revisando.";
            }

            if (errorRevision != null)
            {
                return errorRevision;
            }

            if (borrador == null || revisado == null || renglones == null || !MismasFechas(revisado)
                || revisado.Leido == null || revisado.Leido.Ciclo == null || revisado.Leido.Ciclo.Id != borrador.Id
                || nombreRevisado == null || nombreRevisado != txtNombre.Text.Trim())
            {
                return "Espera: se está revisando.";
            }

            RenglonRevision rojo = renglones.FirstOrDefault(r => r.Bloquea);
            return rojo == null ? null : rojo.Texto;
        }

        private void ActualizarBotonAplicar()
        {
            string motivo = MotivoParaNoAplicar();
            motivoBoton = motivo ?? "";
            // Apagado se ve apagado como cualquier principal (BotonesApagados), y al
            // encenderse vuelve a su VerdeTeschi (antes, colores a mano y DarkGreen).
            btnAplicar.Enabled = motivo == null;
            btnAplicar.AccessibleDescription = motivo;
            ayuda.SetToolTip(btnAplicar, motivo == null ? "Pide la contraseña de la administración." : motivo);
            // Lo rojo que no deja aplicar, a la vista; mientras se revisa o se aplica, nada.
            string antes = lblMotivoC.Text + "|" + lblPieRevision.Text;
            lblMotivoC.Text = motivo == null || revisando || ocupado || motivo.StartsWith("Espera", StringComparison.Ordinal) ? ""
                : "No se puede aplicar todavía. " + motivo;
            ayuda.SetToolTip(lblMotivoC, lblMotivoC.Text);
            PonerConclusion();
            if (antes != lblMotivoC.Text + "|" + lblPieRevision.Text && vistaActual == vistaC)
            {
                AcomodarC();
            }
        }

        /// <summary>
        /// Bajo "Antes de aplicar", lo que sale de la revision en una frase; mientras
        /// revisa o sin revision todavia, la clave de los colores.
        /// </summary>
        private void PonerConclusion()
        {
            int rojos = renglones == null ? 0 : renglones.Count(r => r.Nivel == NivelRevision.Rojo);
            int avisos = renglones == null ? 0 : renglones.Count(r => r.Nivel == NivelRevision.Ambar);
            if (revisando || (renglones == null && errorRevision == null))
            {
                lblPieRevision.Text = ClaveDeColores;
                lblPieRevision.Font = fuenteClave;
                lblPieRevision.ForeColor = TextoGris;
                return;
            }

            lblPieRevision.Font = fuenteConclusion;
            lblPieRevision.ForeColor = errorRevision != null || rojos > 0 ? TextoRojo : Color.DarkGreen;
            lblPieRevision.Text = errorRevision != null ? "No se pudo revisar: todavía no se puede aplicar."
                : rojos == 1 ? "Falta resolver 1 cosa, en rojo, para poder aplicar."
                : rojos > 1 ? "Faltan resolver " + rojos + " cosas, en rojo, para poder aplicar."
                : avisos == 1 ? "Se puede aplicar. Lee antes el aviso en ámbar."
                : avisos > 1 ? "Se puede aplicar. Lee antes los " + avisos + " avisos en ámbar."
                : "Todo en orden: se puede aplicar.";
        }

        /// <summary>Los renglones de "Antes de aplicar", del ancho de la lista.</summary>
        private void PintarRevision(int ancho)
        {
            Point desplazado = flowRevision.AutoScrollPosition;
            flowRevision.SuspendLayout();
            List<Control> viejos = flowRevision.Controls.Cast<Control>().ToList();
            flowRevision.Controls.Clear();
            foreach (Control viejo in viejos)
            {
                // Sin esto, la ayuda guardaria cada "Verlas" ya desechado.
                foreach (Control enlace in viejo.Controls.OfType<LinkLabel>())
                {
                    ayuda.SetToolTip(enlace, null);
                }

                viejo.Dispose();
            }

            List<RenglonRevision> lista = renglones;
            if (errorRevision != null)
            {
                lista = new List<RenglonRevision> { new RenglonRevision { Nivel = NivelRevision.Rojo, Texto = errorRevision } };
            }

            // Primero lo que pide atencion; lo que esta bien, plegado en un solo renglon.
            lista = lista ?? new List<RenglonRevision>();
            foreach (RenglonRevision renglon in lista.Where(r => r.Nivel != NivelRevision.Verde))
            {
                flowRevision.Controls.Add(CrearRenglon(renglon, ancho));
            }

            List<RenglonRevision> bien = lista.Where(r => r.Nivel == NivelRevision.Verde).ToList();
            if (bien.Count > 0)
            {
                flowRevision.Controls.Add(CrearRenglonDeLoBien(bien.Count, ancho));
            }

            if (verLoQueEstaBien)
            {
                foreach (RenglonRevision renglon in bien)
                {
                    flowRevision.Controls.Add(CrearRenglon(renglon, ancho));
                }
            }

            flowRevision.ResumeLayout();
            if (conservarDesplazamiento && flowRevision.AutoScroll)
            {
                flowRevision.AutoScrollPosition = new Point(-desplazado.X, -desplazado.Y);
            }

            anchoRenglones = ancho;
        }

        /// <summary>Un renglon: su circulo de color, el texto ajustado al ancho y, si lo tiene, "Verlas".</summary>
        private Control CrearRenglon(RenglonRevision renglon, int ancho)
        {
            // Configuraciones abre sin fechas: no ve las clases que ya estan en Clases.
            // Si todos los choques son con ellas, "Verlas" ensenaria una lista vacia.
            // Lo mismo con las encimadas en un laboratorio.
            List<ChoqueCiclo> deRenglon = revisado == null || revisado.Revision == null ? null
                : renglon.Ver == FiltroClases.Choques ? revisado.Revision.Choques
                : renglon.Ver == FiltroClases.Encimadas ? revisado.Revision.Encimadas
                : null;
            bool soloConProgramadas = deRenglon != null && deRenglon.Count > 0 && deRenglon.All(c => c.Programada != null);
            bool conEnlace = renglon.Ver != FiltroClases.Ninguno && borrador != null && !soloConProgramadas;
            // Con "Verlas", el renglon es un LinkLabel y el enlace va justo despues de su
            // frase, en el mismo parrafo (antes, en una columna aparte a la derecha, lejos
            // de lo que ensena).
            Label texto = conEnlace ? new LinkLabel { LinkColor = Color.DarkGreen, ActiveLinkColor = Color.ForestGreen } : new Label();
            Panel fila = FilaDeRenglon(renglon.Nivel, texto, conEnlace ? renglon.Texto + "  Verlas" : renglon.Texto, ancho);
            fila.Tag = renglon;
            fila.AccessibleName = (renglon.Nivel == NivelRevision.Rojo ? "No deja aplicar: "
                : renglon.Nivel == NivelRevision.Ambar ? "Aviso: " : "") + renglon.Texto;
            if (conEnlace)
            {
                FiltroClases filtro = renglon.Ver;
                LinkLabel ver = (LinkLabel)texto;
                ver.LinkArea = new LinkArea(renglon.Texto.Length + 2, "Verlas".Length);
                ver.LinkClicked += (s, e) =>
                {
                    if (borrador != null)
                    {
                        IrAConfiguraciones(borrador.Id, filtro);
                    }
                };
                ver.AccessibleDescription = "«Verlas» las enseña en la lista de clases del horario en preparación (paso 2).";
                ayuda.SetToolTip(ver, "«Verlas»: las enseña en la lista de clases del horario en preparación (paso 2).");
            }

            return fila;
        }

        /// <summary>Lo que esta bien, plegado: cuantas revisiones y el enlace para ver o esconder la lista.</summary>
        private Control CrearRenglonDeLoBien(int cuantas, int ancho)
        {
            string frase = cuantas == 1 ? "1 revisión más está bien." : cuantas + " revisiones más están bien.";
            string enlace = verLoQueEstaBien ? "Ocultar" : "Ver cuáles";
            LinkLabel texto = new LinkLabel { LinkColor = Color.DarkGreen, ActiveLinkColor = Color.ForestGreen };
            Panel fila = FilaDeRenglon(NivelRevision.Verde, texto, frase + "  " + enlace, ancho);
            fila.AccessibleName = frase;
            texto.LinkArea = new LinkArea(frase.Length + 2, enlace.Length);
            texto.LinkClicked += (s, e) =>
            {
                // Despues del clic: PintarRevision desecha este mismo enlace.
                BeginInvoke((Action)(() =>
                {
                    verLoQueEstaBien = !verLoQueEstaBien;
                    anchoRenglones = -1;
                    conservarDesplazamiento = true;
                    try
                    {
                        AcomodarC();
                    }
                    finally
                    {
                        conservarDesplazamiento = false;
                    }
                }));
            };
            return fila;
        }

        /// <summary>Un renglon de la lista: su circulo de color y el texto ajustado al ancho.</summary>
        private static Panel FilaDeRenglon(NivelRevision nivel, Label texto, string contenido, int ancho)
        {
            Panel fila = new Panel { BackColor = Color.White, Margin = new Padding(0, 0, 0, 7), Width = ancho };
            texto.AutoSize = true;
            texto.Font = new DrawingFont("Century Gothic", 9.5F);
            texto.ForeColor = nivel == NivelRevision.Rojo ? TextoRojo : TextoOscuro;
            texto.Location = new Point(20, 0);
            texto.MaximumSize = new Size(Math.Max(80, ancho - 20), 0);
            texto.UseMnemonic = false;
            texto.Text = contenido;
            fila.Controls.Add(texto);
            fila.Height = Math.Max(20, texto.PreferredSize.Height);
            Color punto = nivel == NivelRevision.Rojo ? PuntoRojo
                : nivel == NivelRevision.Ambar ? PuntoAmbar
                : nivel == NivelRevision.Verde ? PuntoVerde : PuntoGris;
            fila.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush relleno = new SolidBrush(punto))
                {
                    e.Graphics.FillEllipse(relleno, 2, 4, 12, 12);
                }
            };
            return fila;
        }

        private void PulsarAplicar()
        {
            if (Permisos.Bloquear(Dueno(), "Aplicar un horario"))
            {
                return;
            }

            if (ocupado || vistaActual != vistaC || borrador == null || MotivoParaNoAplicar() != null)
            {
                return;
            }

            CicloPreliminar enPreparacion = borrador;
            DatosDeAplicacion conQueSeReviso = revisado;
            bool ahora = EmpiezaAhora;
            // Con "Ahora mismo", las de la revision: hoy y ayer del servidor.
            DateTime desde = ahora ? revisado.Fechas.Desde.Date : dtpDesde.Value.Date;
            DateTime hasta = dtpHasta.Value.Date;
            DateTime fin = ahora ? revisado.Fechas.Fin.Date : dtpFin.Value.Date;
            string nombre = txtNombre.Text.Trim();
            string guardarComo = chkGuardar.Enabled && chkGuardar.Checked ? txtGuardarComo.Text.Trim() : null;
            if (guardarComo != null && guardarComo.Length == 0)
            {
                Estado("Escribe el nombre con que se guarda el horario de ahora, o desmarca la casilla.", true);
                txtGuardarComo.Focus();
                return;
            }

            int clases = revisado.Clases;
            int terminan = revisado.Terminan;
            string detalle = ahora
                ? DetalleAhora(revisado, nombre)
                : (clases == 1 ? "Se crea 1 clase" : "Se crean " + clases + " clases") + " desde el " + HistorialCambios.Fecha(desde)
                  + (terminan == 0 ? "; ninguna clase de ahora se recorta."
                      : terminan == 1 ? " y la de ahora termina el " + HistorialCambios.Fecha(fin) + "."
                      : " y las " + terminan + " de ahora terminan el " + HistorialCambios.Fecha(fin) + ".");
            if (!DialogoContrasenaAdmin.Confirmar(Dueno(), ahora ? "¿Aplicar «" + nombre + "» ahora mismo?" : "¿Aplicar «" + nombre + "»?", detalle,
                ahora ? "Aplicar ahora" : "Aplicar"))
            {
                return;
            }

            // Mientras se confirmaba pudo llegar otra revision o volver a leerse la
            // pestana: solo se aplica lo que se confirmo, con la misma revision.
            if (IsDisposed || ocupado || vistaActual != vistaC || borrador == null || borrador.Id != enPreparacion.Id
                || revisado != conQueSeReviso || MotivoParaNoAplicar() != null || EmpiezaAhora != ahora
                || (!ahora && (dtpDesde.Value.Date != desde || dtpFin.Value.Date != fin))
                || dtpHasta.Value.Date != hasta || txtNombre.Text.Trim() != nombre)
            {
                if (!IsDisposed && vistaActual == vistaC)
                {
                    Estado("Mientras confirmabas cambió la revisión: mira «Antes de aplicar» y vuelve a pulsar «" + btnAplicar.Text + "».", true);
                }

                return;
            }

            // Lo que llegue de una revision vieja ya no cuenta.
            temporizador.Stop();
            lectura++;
            ResultadoAplicar resultado = null;
            Exception fallo = null;
            Ocupado(true, lblEstadoC, "Aplicando... no cierres la ventana.");
            try
            {
                resultado = ahora
                    ? datos.AplicarAhora(enPreparacion.Id, nombre, desde, hasta, guardarComo, IdAdministrador())
                    : datos.Aplicar(enPreparacion.Id, nombre, desde, hasta, fin, guardarComo, IdAdministrador());
            }
            catch (Exception ex)
            {
                fallo = ex;
            }
            finally
            {
                Ocupado(false, null, null);
            }

            if (fallo != null)
            {
                ExplicarFallo("PestanaAplicacion.Aplicar", fallo, "No se aplicó");
                return;
            }

            recordados.Remove(ClaveRecordada(enPreparacion));
            mensajePendiente = MensajeDeAplicado(resultado);
            mensajePendienteEsError = false;
            Cargar();
        }

        /// <summary>
        /// El "Listo" de la linea de estado tras aplicar; con "Ahora mismo",
        /// tambien a que hora empezo y cuantas clases de hoy ya no cuentan.
        /// </summary>
        private static string MensajeDeAplicado(ResultadoAplicar resultado)
        {
            string mensaje = "Listo: «" + resultado.Nombre + "» quedó aplicado"
                + (!resultado.EmpezoAhora ? "."
                    : " y cuenta desde las " + resultado.EmpezoA.ToString("HH:mm", Invariante) + " de hoy."
                      + (resultado.SinContarHoy == 0 ? ""
                          : resultado.SinContarHoy == 1 ? " 1 clase de hoy que ya había terminado no cuenta (en Faltas, «Volver a contar» la devuelve)."
                          : " " + resultado.SinContarHoy + " clases de hoy que ya habían terminado no cuentan (en Faltas, «Volver a contar» las devuelve)."));
            if (resultado.VueltasAPlantilla.Count > 0)
            {
                mensaje += " " + RevisionCiclo.Enumerar(resultado.VueltasAPlantilla.Select(v => "«" + v + "»").ToList())
                    + (resultado.VueltasAPlantilla.Count == 1 ? " volvió a ser plantilla." : " volvieron a ser plantillas.");
            }

            if (resultado.ExtrasQuitadas > 0)
            {
                mensaje += resultado.ExtrasQuitadas == 1
                    ? " 1 sesión extra que nadie abrió se quitó."
                    : " " + resultado.ExtrasQuitadas + " sesiones extra que nadie abrió se quitaron.";
            }

            if (resultado.MinutosPorAsignar > 0)
            {
                mensaje += " " + HorariosDatos.Duracion(resultado.MinutosPorAsignar) + " pasan a «Falta asignar» de Horario semanal.";
            }

            if (resultado.GuardadaNombre != null)
            {
                mensaje += " También se guardó «" + resultado.GuardadaNombre + "» como quedó.";
            }
            else if (resultado.SinClasesQueGuardar)
            {
                mensaje += " El horario de ahora no se guardó: el " + HistorialCambios.Fecha(resultado.Fin) + " no tenía clases.";
            }

            if (resultado.RecibieronCodigo.Count > 0)
            {
                mensaje += " Recibieron código: " + RevisionCiclo.Enumerar(resultado.RecibieronCodigo) + ".";
            }

            return mensaje;
        }

        /// <summary>"las 23 que empezaron hoy también se sustituyen", o null si no hay ninguna.</summary>
        private static string LasDeHoy(DatosDeAplicacion d)
        {
            int deHoy = d.EmpezaronHoySeQuitan + d.EmpezaronHoyConRegistros;
            return deHoy == 0 ? null : deHoy == 1 ? "la que empezó hoy también se sustituye" : "las " + deHoy + " que empezaron hoy también se sustituyen";
        }

        /// <summary>
        /// Lo que dice la confirmacion de "Ahora mismo": que empieza en ese
        /// momento, que pasa con las clases de hoy (a la hora del servidor) y, bien
        /// claro, que ya no se podra deshacer.
        /// </summary>
        private static string DetalleAhora(DatosDeAplicacion d, string nombre)
        {
            DateTime momento = d.AhoraEstimado;
            ClasesDeHoy deHoy = RevisionAplicacion.ContarDeHoy(d, momento);
            string cuentan = RevisionAplicacion.QuienesCuentan(deHoy);
            string lasDeHoy = LasDeHoy(d);
            string terminan = d.Terminan == 0 ? (lasDeHoy == null ? "ninguna de ahora se recorta" : null)
                : d.Terminan == 1 ? "la de ahora termina ayer" : "las " + d.Terminan + " de ahora terminan ayer";
            List<string> partes = new List<string>
            {
                "Empieza en este momento, hoy " + HistorialCambios.DiaYFecha(d.Hoy) + " (" + momento.ToString("HH:mm", Invariante) + "): "
                + (d.Clases == 1 ? "se crea 1 clase" : "se crean " + d.Clases + " clases") + " desde hoy y "
                + RevisionCiclo.Enumerar(new[] { terminan, lasDeHoy }.Where(t => t != null).ToList()) + "."
            };
            if (deHoy.Terminadas > 0)
            {
                partes.Add(deHoy.Terminadas == 1
                    ? "1 clase de hoy del horario nuevo ya terminó y no cuenta."
                    : deHoy.Terminadas + " clases de hoy del horario nuevo ya terminaron y no cuentan.");
            }

            if (cuentan != null)
            {
                partes.Add(cuentan + " con el horario nuevo.");
            }

            partes.Add("Lo registrado hoy con el horario de ahora se queda y cuenta una sola vez en las listas.");
            partes.Add("Como empieza hoy, ya no se podrá deshacer: después «" + nombre + "» solo se corrige en Horario semanal y Clases.");
            return string.Join(" ", partes);
        }

        /// <summary>
        /// Un fallo de Aplicar o Deshacer: se vuelve a leer lo que hay (por si
        /// otra ventana cambio algo) y se dice, en la linea de estado y en un
        /// aviso, "No se aplico: {motivo}. No cambio nada." (todo va en una
        /// transaccion: si falla, no queda nada a medias).
        /// </summary>
        private void ExplicarFallo(string contexto, Exception ex, string queNo)
        {
            bool explicado = ex is ErrorDeDatos || ex is AccionNoPermitida;
            if (explicado)
            {
                Log.Aviso(contexto + ": " + ex.Message);
            }
            else
            {
                Log.Error(contexto, ex);
            }

            string causa = Minuscula(AvisoDeFalloAdmin.Causa(ex));
            string accion = AvisoDeFalloAdmin.Accion(ex);
            string texto = queNo + ": " + causa + ". No cambió nada." + (accion.Length > 0 ? " " + accion : "");
            mensajePendiente = texto;
            mensajePendienteEsError = true;
            if (IsDisposed)
            {
                return;
            }

            Cargar();
            MessageBox.Show(Dueno(), texto, "Aplicar con fechas", MessageBoxButtons.OK, explicado ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
        }

        /// <summary>La causa dentro de una frase: "Esta configuracion..." pasa a "esta configuracion..." (las siglas se quedan).</summary>
        private static string Minuscula(string causa)
        {
            causa = causa ?? "";
            return causa.Length > 1 && char.IsUpper(causa[0]) && char.IsLower(causa[1])
                ? char.ToLowerInvariant(causa[0]) + causa.Substring(1)
                : causa;
        }

        /// <summary>
        /// Mientras aplica o deshace: la pestana sin poder usarse, el cursor de
        /// espera y ningun otro Aplicar ni Deshacer (ocupado). Se llama con false
        /// en un finally: si algo falla, la pestana vuelve a poder usarse.
        /// </summary>
        private void Ocupado(bool activar, Label linea, string texto)
        {
            ocupado = activar;
            if (activar)
            {
                if (linea != null)
                {
                    linea.ForeColor = Color.DarkGreen;
                    linea.Text = texto;
                    linea.Refresh();
                }

                Enabled = false;
                Cursor = Cursors.WaitCursor;
                Application.UseWaitCursor = true;
                Update();
            }
            else
            {
                Application.UseWaitCursor = false;
                if (!IsDisposed)
                {
                    Cursor = Cursors.Default;
                    Enabled = true;
                }
            }
        }

        // =====================================================================
        // La tabla de los horarios aplicados (B y D)
        // =====================================================================

        private DataGridView TablaDeAplicadas()
        {
            DataGridView grid = EstiloHorarios.CrearGrid(Point.Empty, new Size(AnchoTarjeta, 200));
            grid.Columns.Add("nombre", "Nombre");
            grid.Columns.Add("del", "Del");
            grid.Columns.Add("al", "Al");
            grid.Columns.Add("clases", "Clases");
            grid.Columns.Add("aplicado", "Aplicado el");
            EstiloHorarios.Pesos(grid, 30, 14, 14, 10, 32);
            grid.Columns["clases"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is int)
                {
                    IrAConfiguraciones((int)grid.Rows[e.RowIndex].Tag, FiltroClases.Ninguno);
                }
            };
            ayuda.SetToolTip(grid, "Doble clic: verlo en «1. Plantillas».");
            return grid;
        }

        /// <summary>Hasta 8 aplicadas, de la mas reciente a la mas vieja.</summary>
        private void LlenarAplicadas(DataGridView grid, Label vacio)
        {
            grid.Rows.Clear();
            List<CicloPreliminar> aplicadas = situacion == null ? new List<CicloPreliminar>() : situacion.Aplicadas.Take(8).ToList();
            foreach (CicloPreliminar aplicada in aplicadas)
            {
                int i = grid.Rows.Add(
                    aplicada.Nombre,
                    EstiloAdmin.Fecha(aplicada.Desde),
                    EstiloAdmin.Fecha(aplicada.UltimoDia),
                    aplicada.Clases.ToString(Invariante),
                    (aplicada.PublicadoEn.HasValue ? aplicada.PublicadoEn.Value.ToString("dd/MM/yyyy", Invariante) : "—")
                        + (aplicada.PublicadoPor.Length > 0 ? ", " + aplicada.PublicadoPor : ""));
                grid.Rows[i].Tag = aplicada.Id;
                if (aplicada.CortadaEl.HasValue)
                {
                    grid.Rows[i].Cells[2].ToolTipText = "Lo cortó el horario aplicado después: su último día fue el "
                        + HistorialCambios.Fecha(aplicada.CortadaEl.Value) + " (iba al " + EstiloAdmin.Fecha(aplicada.Hasta) + ").";
                }
            }

            grid.ClearSelection();
            grid.Visible = aplicadas.Count > 0;
            vacio.Visible = aplicadas.Count == 0;
        }

        // =====================================================================
        // Linea de estado
        // =====================================================================

        /// <summary>En la linea de estado de la forma que se ve: verde, o roja si algo fallo.</summary>
        private void Estado(string texto, bool error)
        {
            Label linea = vistaActual == vistaC ? lblEstadoC : vistaActual == vistaD ? lblEstadoD : vistaActual == vistaB ? lblEstadoB : null;
            if (linea == null)
            {
                return;
            }

            linea.ForeColor = error ? TextoRojo : Color.DarkGreen;
            linea.Text = texto ?? "";
            ayuda.SetToolTip(linea, linea.Text);
            Acomodar();
        }

        private IWin32Window Dueno()
        {
            return EstiloHorarios.Dueno(this);
        }

        // =====================================================================
        // Medidas
        // =====================================================================

        private void Acomodar()
        {
            if (vistaActual == vistaBase)
            {
                AcomodarBase();
            }
            else if (vistaActual == vistaB)
            {
                AcomodarB();
            }
            else if (vistaActual == vistaC)
            {
                AcomodarC();
            }
            else if (vistaActual == vistaD)
            {
                AcomodarD();
            }
        }

        private void AcomodarBase()
        {
            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;
            int w = Math.Max(300, Math.Min(640, ancho - 48));
            lblTituloBase.Location = new Point(24, 20);
            lblTextoBase.MaximumSize = new Size(w - 48, 0);
            lblTextoBase.Location = new Point(24, 56);
            btnRevisarBase.Location = new Point(24, lblTextoBase.Bottom + 16);
            int h = Math.Max(170, btnRevisarBase.Bottom + 20);
            tarjetaBase.SetBounds(Math.Max(24, (ancho - w) / 2), Math.Max(24, (alto - h) / 3), w, h);
        }

        private void AcomodarB()
        {
            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;
            int w = Math.Max(300, Math.Min(AnchoTarjeta, ancho - 48));
            int x = Math.Max(24, (ancho - w) / 2);
            lblTituloB.Location = new Point(24, 20);
            lblTextoB.MaximumSize = new Size(w - 48, 0);
            lblTextoB.Location = new Point(24, 56);
            btnIrB.Location = new Point(24, lblTextoB.Bottom + 16);
            int h = btnIrB.Visible ? Math.Max(170, btnIrB.Bottom + 20) : Math.Max(110, lblTextoB.Bottom + 24);
            tarjetaB.SetBounds(x, 24, w, h);
            AcomodarTabla(x, w, alto, tarjetaB.Bottom, lblEstadoB, lblAplicadosB, lnkTodosB, gridB, lblVacioB);
        }

        private void AcomodarD()
        {
            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;
            int w = Math.Max(400, Math.Min(AnchoTarjeta, ancho - 48));
            int x = Math.Max(24, (ancho - w) / 2);
            lblTituloD.SetBounds(24, 18, w - 48, 30);
            int y = 58;
            foreach (Label[] par in new[]
            {
                new[] { lblRotAplicado, lblAplicado }, new[] { lblRotEmpieza, lblEmpieza },
                new[] { lblRotTermina, lblTermina }, new[] { lblRotAnterior, lblAnterior }
            })
            {
                y = Rotulado(par[0], par[1], 24, y, w - 48) + 6;
            }

            y += 6;
            btnVerHorariosD.Location = new Point(24, y);
            btnVerConfiguracionD.Location = new Point(btnVerHorariosD.Right + 8, y);
            btnDeshacer.Location = new Point(btnVerConfiguracionD.Right + 8, y);
            lblNotaD.MaximumSize = new Size(w - 48, 0);
            lblNotaD.Location = new Point(24, y + 36 + 14);
            int h = Math.Max(btnVerHorariosD.Visible ? 220 : 180, lblNotaD.Bottom + 18);
            tarjetaD.SetBounds(x, 16, w, h);
            AcomodarTabla(x, w, alto, tarjetaD.Bottom, lblEstadoD, lblAplicadosD, lnkTodosD, gridD, lblVacioD);
        }

        /// <summary>Debajo de la tarjeta: la linea de estado, "Horarios aplicados" con su enlace y la tabla (hasta 8 filas).</summary>
        private static void AcomodarTabla(int x, int w, int alto, int arriba, Label estado, Label titulo, LinkLabel enlace, DataGridView grid, Label vacio)
        {
            estado.AutoSize = false;
            // Hasta tres renglones: el "Listo" de "Ahora mismo" dice tambien a que hora
            // empezo y cuantas de hoy no cuentan (en uno se cortaba con "...").
            int altoEstado = estado.Text.Length == 0 ? 20
                : Math.Max(20, Math.Min(3 * estado.Font.Height + 4,
                    TextRenderer.MeasureText(estado.Text, estado.Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height + 2));
            estado.SetBounds(x, arriba + 8, w, altoEstado);
            titulo.Location = new Point(x, estado.Bottom + 6);
            enlace.Location = new Point(x + w - enlace.Width, estado.Bottom + 9);
            int top = titulo.Bottom + 6;
            int filas = Math.Max(1, grid.Rows.Count);
            int deseado = grid.ColumnHeadersHeight + Math.Min(8, filas) * grid.RowTemplate.Height + 3;
            grid.SetBounds(x, top, w, Math.Max(grid.ColumnHeadersHeight + grid.RowTemplate.Height + 3, Math.Min(deseado, alto - top - 12)));
            vacio.Location = new Point(x, top + 4);
        }

        private void AcomodarC()
        {
            if (vistaActual != vistaC)
            {
                return;
            }

            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;
            if (ancho < 200 || alto < 150)
            {
                return;
            }

            bool dos = ancho >= AnchoDosColumnas;
            int x0 = 24;
            int wIzq = dos ? (ancho >= 1600 ? 640 : 560) : Math.Max(360, Math.Min(760, ancho - 48 - SystemInformation.VerticalScrollBarWidth));
            vistaC.SuspendLayout();
            // Se coloca como sin desplazar y luego se vuelve a donde estaba.
            Point desplazado = vistaC.AutoScrollPosition;
            vistaC.AutoScrollPosition = Point.Empty;

            int y = 12;
            lblTituloC.SetBounds(x0, y, wIzq, 28);
            y += 32;
            lblResumenC.MaximumSize = new Size(Math.Max(100, wIzq - lnkVerloC.Width - 12), 0);
            lblResumenC.Location = new Point(x0, y);
            lnkVerloC.Location = new Point(lblResumenC.Right + 8, y);
            y += Math.Max(lblResumenC.Height, lnkVerloC.Height) + 14;

            // A una columna, como queda va antes de decidir: se ve sin bajar.
            if (!dos)
            {
                y = AcomodarLinea(x0, y, wIzq) + 12;
            }

            lblFechas.Location = new Point(x0, y);
            y = AcomodarTarjetas(x0, y + 26, wIzq) + 14;

            // Sus preguntas en una columna y las respuestas en otra.
            bool conFin = !EmpiezaAhora;
            int xFecha = x0 + Math.Max(256, new[] { lblRotHasta, lblRotFin, lblRotNombre }.Max(r => r.PreferredWidth) + 8);
            y = FilaDeFecha(lblRotHasta, dtpHasta, diaHasta, x0, xFecha, y) + 8;
            lblDuracion.Location = new Point(diaHasta.Right + 12, diaHasta.Top + 1);
            if (conFin)
            {
                y = FilaDeFecha(lblRotFin, dtpFin, diaFin, x0, xFecha, y) + 4;
                lblNotaFin.MaximumSize = new Size(Math.Min(540, wIzq), 0);
                lblNotaFin.Location = new Point(x0, y);
                y = lblNotaFin.Bottom + 10;
            }

            lblRotNombre.Location = new Point(x0, y + 4);
            txtNombre.SetBounds(xFecha, y, Math.Min(260, wIzq - (xFecha - x0)), txtNombre.Height);
            y = txtNombre.Bottom + 12;

            chkGuardar.Location = new Point(x0, y);
            y = chkGuardar.Bottom + 3;
            txtGuardarComo.SetBounds(x0 + 18, y, 220, txtGuardarComo.Height);
            lblParaCargarlo.MaximumSize = new Size(Math.Max(80, wIzq - 18 - 220 - 8), 0);
            lblParaCargarlo.Location = new Point(txtGuardarComo.Right + 8, y + 4);
            y = Math.Max(txtGuardarComo.Bottom, lblParaCargarlo.Bottom) + 12;

            if (!dos)
            {
                y = AcomodarAlAplicar(x0, y, wIzq) + 12;
            }

            // El pie: el boton y, a su derecha, la linea de estado o, si no dice nada,
            // el porque de no poder aplicar, del alto de su texto (antes, 42 fijos lo
            // cortaban). Se coloca al final, segun quepa la columna.
            int xTexto = AnchoBotonAplicar + 12;
            int anchoTexto = Math.Max(80, wIzq - xTexto);
            lblMotivoC.Visible = lblMotivoC.Text.Length > 0 && lblEstadoC.Text.Length == 0;
            int altoEstado = Math.Max(42, AltoDeTexto(lblEstadoC, anchoTexto));
            int altoMotivo = Math.Max(42, AltoDeTexto(lblMotivoC, anchoTexto));
            int altoPie = lblMotivoC.Visible ? altoMotivo : altoEstado;
            int yPie = y;
            y = yPie + altoPie + 12;

            int anchoLista;
            if (dos)
            {
                int xd = x0 + wIzq + 36;
                int wd = Math.Max(200, ancho - xd - 24);
                // Como queda, en este orden: las fechas, lo que pasa y lo que falta revisar.
                int yd = AcomodarLinea(xd, 12, wd) + 10;
                yd = AcomodarAlAplicar(xd, yd, wd) + 14;
                lblAntes.Location = new Point(xd, yd);
                // La conclusion, bajo su titulo y antes de los renglones (abajo del todo
                // quedaba lejos de lo que explica).
                lblPieRevision.Location = new Point(xd, lblAntes.Bottom + 2);
                int arriba = PonerRevisando(xd, wd, yd + 3, lblPieRevision.Bottom + 8);
                flowRevision.AutoScroll = true;
                flowRevision.SetBounds(xd, arriba, wd, Math.Max(80, alto - 10 - arriba));
                anchoLista = wd - SystemInformation.VerticalScrollBarWidth - 6;
                if (anchoLista != anchoRenglones)
                {
                    PintarRevision(anchoLista);
                }
            }
            else
            {
                int yd = y + 8;
                lblAntes.Location = new Point(x0, yd);
                anchoLista = wIzq;
                if (anchoLista != anchoRenglones)
                {
                    PintarRevision(anchoLista);
                }

                int altoLista = flowRevision.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) + 4;
                lblPieRevision.Location = new Point(x0, lblAntes.Bottom + 2);
                int arriba = PonerRevisando(x0, wIzq, yd + 3, lblPieRevision.Bottom + 8);
                flowRevision.AutoScroll = false;
                flowRevision.SetBounds(x0, arriba, wIzq, Math.Max(40, altoLista));
            }

            // A dos columnas el pie va sobre la pagina, no dentro: si la columna izquierda
            // no cabe, se queda abajo, a la vista, con el boton y su motivo (a 1366x705 el
            // boton quedaba debajo de lo visible), y la pagina reserva su sitio para poder
            // bajar a lo que queda detras. A una columna, en su sitio de la pagina.
            const int MargenPie = 8;
            pieFlotante = dos && yPie + altoPie + 2 * MargenPie > alto;
            Control padrePie = dos ? (Control)this : vistaC;
            if (pieC.Parent != padrePie)
            {
                padrePie.Controls.Add(pieC);
            }

            int dentro = pieFlotante ? MargenPie : 0;
            btnAplicar.SetBounds(0, dentro, AnchoBotonAplicar, 42);
            lblEstadoC.SetBounds(xTexto, dentro, anchoTexto, altoEstado);
            lblMotivoC.SetBounds(xTexto, dentro, anchoTexto, altoMotivo);
            if (lblMotivoC.Visible)
            {
                // La linea de estado (vacia) esta en el mismo sitio y es transparente: taparia el motivo.
                lblMotivoC.BringToFront();
            }

            int altoDelPie = altoPie + 2 * dentro;
            pieC.SetBounds(x0, pieFlotante ? alto - altoDelPie : yPie, wIzq, altoDelPie);
            vistaC.AutoScrollMinSize = pieFlotante ? new Size(0, yPie + altoPie + 2 * MargenPie) : Size.Empty;
            if (padrePie == this)
            {
                pieC.BringToFront();
            }

            pieC.Invalidate();
            vistaC.ResumeLayout();
            vistaC.AutoScrollPosition = new Point(-desplazado.X, -desplazado.Y);
        }

        /// <summary>"Asi quedaran las fechas" y su linea de tiempo. Devuelve donde acaba.</summary>
        private int AcomodarLinea(int x, int y, int ancho)
        {
            lblLinea.Location = new Point(x, y);
            linea.SetBounds(x, lblLinea.Bottom + 4, ancho, LineaDeFechasHorario.AltoPreferido);
            return linea.Bottom;
        }

        /// <summary>Las dos tarjetas de cuando empieza, lado a lado; si no caben, una sobre otra. Devuelve donde acaban.</summary>
        private int AcomodarTarjetas(int x, int y, int ancho)
        {
            const int Separacion = 12;
            const int XTexto = 32;
            bool apiladas = ancho < 500;
            int w = apiladas ? ancho : (ancho - Separacion) / 2;
            Size nota = new Size(Math.Max(100, w - XTexto - 12), 0);
            lblNotaAhora.MaximumSize = nota;
            lblNotaOtro.MaximumSize = nota;
            rbAhora.Location = new Point(13, 10);
            lblHoyAhora.Location = new Point(XTexto - 3, 38);
            lblNotaAhora.Location = new Point(XTexto - 2, 66);
            rbOtroDia.Location = new Point(13, 10);
            dtpDesde.Location = new Point(XTexto, 34);
            diaDesde.Location = new Point(dtpDesde.Right + 8, 38);
            lblNotaOtro.Location = new Point(XTexto - 2, 66);
            int alto = 66 + Math.Max(lblNotaAhora.PreferredSize.Height, lblNotaOtro.PreferredSize.Height) + 10;
            tarjetaAhora.SetBounds(x, y, w, alto);
            tarjetaOtroDia.SetBounds(apiladas ? x : x + w + Separacion, apiladas ? y + alto + Separacion : y, w, alto);
            return tarjetaOtroDia.Bottom;
        }

        /// <summary>"Al aplicar": sus renglones y, resaltado, lo que pasa mientras tanto. Devuelve donde acaba.</summary>
        private int AcomodarAlAplicar(int x, int y, int ancho)
        {
            lblAlAplicar.Location = new Point(x, y);
            y = lblAlAplicar.Bottom + 4;
            y = Rotulado(lblRotCrean, lblCrean, x, y, ancho) + 3;
            y = Rotulado(lblRotTerminan, lblTerminan, x, y, ancho) + 3;
            y = Rotulado(lblRotCodigos, lblCodigos, x, y, ancho) + 7;
            lblNadaCambia.MaximumSize = new Size(Math.Max(100, ancho - 24), 0);
            lblNadaCambia.Location = new Point(12, 7);
            tarjetaNadaCambia.SetBounds(x, y, ancho, lblNadaCambia.PreferredSize.Height + 14);
            return tarjetaNadaCambia.Bottom;
        }

        /// <summary>
        /// "Revisando..." (y "Revisar otra vez") a la derecha del titulo de "Antes de
        /// aplicar", sin encimarse con el titulo ni con la clave de los colores (a 1280
        /// se encimaba con la clave); si no cabe ahi, en su propio renglon, bajo la
        /// clave. Devuelve donde empiezan los renglones.
        /// </summary>
        private int PonerRevisando(int x, int ancho, int y, int arriba)
        {
            int necesita = TextRenderer.MeasureText(TextoSeRevisa, lblRevisando.Font).Width + 6;
            int libreDesde = Math.Max(lblAntes.Right, lblPieRevision.Right) + 12;
            int libre = Math.Min(320, x + ancho - libreDesde);
            if (libre >= necesita)
            {
                lblRevisando.SetBounds(x + ancho - libre, y, libre, 20);
            }
            else
            {
                lblRevisando.SetBounds(x, arriba, ancho, 20);
                arriba += 22;
            }

            lnkRevisarOtraVez.Location = new Point(x + ancho - lnkRevisarOtraVez.Width, lblRevisando.Top);
            return arriba;
        }

        /// <summary>El alto de la etiqueta con su texto en ese ancho, de 1 a 4 renglones (vacia, 0).</summary>
        private static int AltoDeTexto(Label etiqueta, int ancho)
        {
            if (etiqueta.Text.Length == 0)
            {
                return 0;
            }

            int alto = TextRenderer.MeasureText(etiqueta.Text, etiqueta.Font, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height + 2;
            return Math.Min(alto, 4 * TextRenderer.MeasureText("Ág", etiqueta.Font).Height + 2);
        }

        private static int FilaDeFecha(Label rotulo, DateTimePicker fecha, Label dia, int x0, int xFecha, int y)
        {
            rotulo.Location = new Point(x0, y + 4);
            fecha.Location = new Point(xFecha, y);
            dia.Location = new Point(fecha.Right + 10, y + 4);
            return y + Math.Max(fecha.Height, rotulo.Height);
        }

        /// <summary>Un renglon rotulado: el rotulo en negrita y, a su derecha, el texto ajustado al resto del ancho. Devuelve donde acaba.</summary>
        private static int Rotulado(Label rotulo, Label texto, int x, int y, int ancho)
        {
            rotulo.Location = new Point(x, y);
            int izquierda = x + rotulo.PreferredWidth + 4;
            texto.MaximumSize = new Size(Math.Max(80, x + ancho - izquierda), 0);
            texto.Location = new Point(izquierda, y);
            return y + Math.Max(rotulo.Height, texto.Height);
        }

        // =====================================================================
        // Piezas
        // =====================================================================

        private static Panel NuevaVista()
        {
            return new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Visible = false };
        }

        private static Label Titulo(string texto, float tamano)
        {
            return new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", tamano, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Text = texto,
                UseMnemonic = false
            };
        }

        private static Label Texto(float tamano, Color color)
        {
            return new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", tamano),
                ForeColor = color,
                UseMnemonic = false
            };
        }

        private static Label Rotulo(string texto)
        {
            Label rotulo = EstiloHorarios.CrearEtiqueta(texto, Point.Empty);
            rotulo.Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);
            return rotulo;
        }

        /// <summary>Una opcion de cuando empieza, dentro de su tarjeta: se ve el fondo de la tarjeta.</summary>
        private static RadioButton Opcion(string texto)
        {
            return new RadioButton
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold),
                ForeColor = TextoOscuro,
                Text = texto,
                UseMnemonic = false
            };
        }

        private static Label RotuloDeRenglon(string texto)
        {
            return new Label
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                ForeColor = TextoOscuro,
                Text = texto,
                UseMnemonic = false
            };
        }

        private static Label LineaDeEstado()
        {
            return new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.DarkGreen,
                UseMnemonic = false
            };
        }

        private static LinkLabel Enlace(string texto, LinkLabelLinkClickedEventHandler alPulsar)
        {
            LinkLabel enlace = new LinkLabel
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", 9.5F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.ForestGreen,
                Text = texto
            };
            enlace.LinkClicked += alPulsar;
            return enlace;
        }

        /// <summary>Una fecha dd/MM/aaaa con su dia de la semana a la derecha (EstiloHorarios.CrearFechaConDia).</summary>
        private static DateTimePicker Fecha(Control padre, out Label dia)
        {
            DateTimePicker fecha = EstiloHorarios.CrearFechaConDia(padre, Point.Empty);
            dia = (Label)padre.Controls[padre.Controls.Count - 1];
            fecha.MinDate = new DateTime(2000, 1, 1);
            fecha.MaxDate = new DateTime(2099, 12, 31);
            return fecha;
        }

        private static void PonerFecha(DateTimePicker fecha, DateTime valor)
        {
            DateTime dia = valor.Date;
            if (dia < fecha.MinDate)
            {
                dia = fecha.MinDate;
            }
            else if (dia > fecha.MaxDate)
            {
                dia = fecha.MaxDate;
            }

            fecha.Value = dia;
        }

        /// <summary>
        /// La explicacion de un boton desactivado al pasar el raton (un control
        /// desactivado no recibe el raton: Windows se lo da a su contenedor).
        /// </summary>
        private void AyudaSobreDesactivado(Control contenedor, Control control, Func<string> texto)
        {
            bool ensenando = false;
            contenedor.MouseMove += (s, e) =>
            {
                bool encima = control.Visible && !control.Enabled && control.Bounds.Contains(e.Location);
                string explicacion = encima ? texto() : "";
                if (encima && explicacion.Length > 0 && !ensenando)
                {
                    ayuda.Show(explicacion, contenedor, e.X + 12, e.Y + 20, 8000);
                    ensenando = true;
                }
                else if (!encima && ensenando)
                {
                    ayuda.Hide(contenedor);
                    ensenando = false;
                }
            };
            contenedor.MouseLeave += (s, e) =>
            {
                if (ensenando)
                {
                    ayuda.Hide(contenedor);
                    ensenando = false;
                }
            };
        }

        /// <summary>Una tarjeta blanca (o de color) con el borde redondeado (PanelLaboratoriosAhora.Redondeado).</summary>
        private sealed class Tarjeta : Panel
        {
            public Color Borde = Color.DarkGreen;
            public Color Fondo = Color.White;
            public int Grosor = 2;
            public int Radio = 12;

            public Tarjeta()
            {
                BackColor = Color.White;
                DoubleBuffered = true;
                ResizeRedraw = true;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent != null ? Parent.BackColor : Color.White);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle borde = new Rectangle(Grosor / 2, Grosor / 2, Width - Grosor - 1, Height - Grosor - 1);
                using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(borde, Radio))
                using (SolidBrush relleno = new SolidBrush(Fondo))
                using (Pen pluma = new Pen(Borde, Grosor))
                {
                    e.Graphics.FillPath(relleno, forma);
                    e.Graphics.DrawPath(pluma, forma);
                }
            }
        }
    }
}
