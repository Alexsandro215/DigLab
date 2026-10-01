using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigBit
{
    /// <summary>
    /// Pantalla del alumno, en dos pasos y sin ventanas intermedias: 1) el codigo
    /// que dicta el profesor y 2) como esta este equipo (todo bien, una falla o
    /// trajo el suyo), con un boton final "Registrar mi bitacora": solo cuenta el
    /// primer registro, asi que un clic equivocado no se guarda solo. Arriba, la
    /// clase que hay ahora en este laboratorio.
    ///
    /// Sustituye al menu del alumno, a Buscar_Codigo_Alumno y a Registro_Bitacora
    /// con la misma logica: el codigo se resuelve con el reloj del servidor o, sin
    /// servidor, con la copia local (fase 4); quien ya registro y sigue en clase
    /// puede volver a usar el equipo; con equipo propio no se libera la maquina; y
    /// al guardar se libera el equipo hasta la hora de salida (fase 3).
    /// </summary>
    public class Alumno_Principal : Form
    {
        private const int AnchoMaximo = 900;
        private const int SegundosEntreActualizaciones = 60;
        private const string SinProblema = "Sin problema";
        private const string Otro = "Otro";
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");
        private static readonly Color TextoSecundario = Color.FromArgb(90, 90, 90);
        private static readonly Color FondoClase = Color.FromArgb(236, 246, 236);
        private static readonly Color FondoAviso = Color.FromArgb(255, 236, 204);
        private static readonly Color Rojo = Color.FromArgb(190, 30, 30);
        private static readonly Color Apagado = Color.FromArgb(165, 165, 165);

        private readonly Container componentes = new Container();
        private readonly Consultas consultas = new Consultas();
        private readonly string matricula;
        private string nombre;

        // Lo que dejo el paso 1 al aceptar el codigo.
        private string codigoAceptado;
        private VentanaCodigo ventana;
        private bool registrado;

        private Panel cabecera;
        private Label lblSaludo;
        private Label lblSubtitulo;
        private Label lblReloj;
        private Label lblFecha;
        private Button btnCerrarSesion;

        private Panel contenedor;
        private Panel columna;
        private Panel pnlClase;
        private Label lblClaseTitulo;
        private Label lblClaseDetalle;

        private Label lblPaso1;
        private TextBox txtCodigo;
        private Button btnContinuar;
        private Label lblErrorCodigo;
        private Label lblCodigoOk;
        private LinkLabel lnkOtroCodigo;

        private Label lblPaso2;
        private FlowLayoutPanel pnlEstados;
        private RadioButton rbTodoBien;
        private RadioButton rbFalla;
        private RadioButton rbPropio;
        private Label lblPista;
        private Panel pnlFallas;
        private readonly Dictionary<string, Label> etiquetasArea = new Dictionary<string, Label>();
        private readonly Dictionary<string, FlowLayoutPanel> chipsArea = new Dictionary<string, FlowLayoutPanel>();
        private readonly Dictionary<string, TextBox> otroArea = new Dictionary<string, TextBox>();
        private Label lblErrorPaso2;
        private Button btnRegistrar;

        private Label lblListo;
        private Label lblListoDetalle;
        private Panel pie;
        private LinkLabel lnkPerfil;
        private editar_Perfil perfil;

        private Timer relojTimer;
        private Timer datosTimer;
        private bool actualizando;

        /// <summary>Lo que se lee en segundo plano: el nombre del alumno y la clase de ahora.</summary>
        private sealed class DatosPantalla
        {
            public string Nombre;
            public SesionAgenda Clase;
        }

        public Alumno_Principal()
        {
            matricula = Datos_User.getUser() ?? "";
            InicializarComponentes();

            if (!Kiosco.Activo)
            {
                WindowState = FormWindowState.Maximized;
            }

            Kiosco.Aplicar(this);
            if (AppContexto.Actual != null)
            {
                AppContexto.Actual.Registrar(this);
            }

            ActualizarReloj();
            ActualizarPaso2();
        }

        // =====================================================================
        // Construccion de la interfaz
        // =====================================================================

        private void InicializarComponentes()
        {
            ComponentResourceManager recursos = new ComponentResourceManager(typeof(PrincipalAdministrador));

            BackColor = Color.White;
            ClientSize = new Size(1100, 760);
            MinimumSize = new Size(900, 640);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = (Icon)recursos.GetObject("$this.Icon");
            Text = "DigBit - Alumno";

            contenedor = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
            columna = new Panel { BackColor = Color.White };
            contenedor.Controls.Add(columna);
            contenedor.Resize += (s, e) => Colocar();

            CrearClase();
            CrearPaso1();
            CrearPaso2();

            lblListo = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Visible = false
            };
            lblListoDetalle = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 12F),
                ForeColor = TextoSecundario,
                Visible = false
            };

            lnkPerfil = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10.5F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Text = "Mi perfil (nombre, correo y carrera)"
            };
            lnkPerfil.LinkClicked += (s, e) => AbrirPerfil();

            columna.Controls.AddRange(new Control[] { lblListo, lblListoDetalle });

            // Barra fija abajo: "Registrar mi bitacora" siempre a la vista, aunque la
            // lista de fallas no quepa en una pantalla de 768 de alto.
            pie = new Panel { Dock = DockStyle.Bottom, Height = 84, BackColor = Color.White };
            pie.Controls.AddRange(new Control[] { lnkPerfil, lblErrorPaso2, btnRegistrar });
            pie.Paint += (s, e) =>
            {
                using (Pen linea = new Pen(Color.FromArgb(215, 222, 215)))
                {
                    e.Graphics.DrawLine(linea, 20, 0, pie.Width - 20, 0);
                }
            };
            pie.Resize += (s, e) => Colocar();

            // El ultimo que se anade se acopla primero: cabecera arriba, pie abajo y el resto en medio.
            Controls.Add(contenedor);
            Controls.Add(pie);
            Controls.Add(CrearCabecera(recursos));

            relojTimer = new Timer(componentes) { Interval = 1000 };
            relojTimer.Tick += (s, e) => ActualizarReloj();
            datosTimer = new Timer(componentes) { Interval = SegundosEntreActualizaciones * 1000 };
            datosTimer.Tick += (s, e) =>
            {
                // Con el codigo ya aceptado, lo de arriba es su clase: no se toca.
                if (codigoAceptado == null)
                {
                    ActualizarDatos();
                }
            };
        }

        private Control CrearCabecera(ComponentResourceManager recursos)
        {
            cabecera = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(20, 8, 20, 0), BackColor = Color.White };

            PictureBox logos = new PictureBox
            {
                Image = (Image)recursos.GetObject("Logos"),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(20, 24),
                Size = new Size(230, 48)
            };

            lblSaludo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(270, 20),
                Size = new Size(560, 34),
                Text = "Hola"
            };

            lblSubtitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                Location = new Point(273, 56),
                Text = "Matrícula " + (matricula.Length > 0 ? matricula : "-")
            };

            lblReloj = new Label
            {
                AutoSize = false,
                Size = new Size(160, 44),
                Font = new DrawingFont("Century Gothic", 26F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                TextAlign = ContentAlignment.MiddleRight
            };

            lblFecha = new Label
            {
                AutoSize = false,
                Size = new Size(320, 22),
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = TextoSecundario,
                TextAlign = ContentAlignment.MiddleRight
            };

            btnCerrarSesion = EstiloHorarios.CrearBoton("Cerrar sesión", Point.Empty, new Size(150, 40));
            // Salir de la sesion, no de la aplicacion: en el laboratorio DigBit es el shell.
            btnCerrarSesion.Click += (s, e) => AppContexto.Actual.CerrarSesion();

            cabecera.Controls.AddRange(new Control[] { logos, lblSaludo, lblSubtitulo, lblReloj, lblFecha, btnCerrarSesion });
            cabecera.Resize += (s, e) =>
            {
                int derecha = cabecera.ClientSize.Width - 20;
                btnCerrarSesion.Location = new Point(derecha - btnCerrarSesion.Width, 28);
                int finReloj = btnCerrarSesion.Left - 24;
                lblReloj.Location = new Point(finReloj - lblReloj.Width, 12);
                lblFecha.Location = new Point(finReloj - lblFecha.Width, 58);
                lblSaludo.Width = Math.Max(200, lblFecha.Left - 16 - lblSaludo.Left);
            };
            cabecera.Paint += (s, e) =>
            {
                using (Pen linea = new Pen(Color.FromArgb(215, 222, 215)))
                {
                    e.Graphics.DrawLine(linea, 20, cabecera.Height - 1, cabecera.Width - 20, cabecera.Height - 1);
                }
            };
            return cabecera;
        }

        /// <summary>La franja de arriba: que clase hay ahora en este laboratorio.</summary>
        private void CrearClase()
        {
            pnlClase = new Panel { BackColor = FondoClase };
            lblClaseTitulo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", 12.5F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Text = "Consultando la clase de ahora..."
            };
            lblClaseDetalle = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new DrawingFont("Century Gothic", 11F),
                ForeColor = Color.FromArgb(50, 50, 50)
            };
            pnlClase.Controls.Add(lblClaseTitulo);
            pnlClase.Controls.Add(lblClaseDetalle);
            columna.Controls.Add(pnlClase);
        }

        private void CrearPaso1()
        {
            lblPaso1 = TituloPaso("Paso 1. Escribe el código que te dicta tu profesor");

            txtCodigo = new TextBox
            {
                Font = new DrawingFont("Consolas", 24F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                MaxLength = 16,
                Width = 300,
                TextAlign = HorizontalAlignment.Center
            };
            txtCodigo.TextChanged += (s, e) => MostrarErrorCodigo("");

            btnContinuar = EstiloAdmin.BotonPrincipal("Continuar", Point.Empty, new Size(170, 40));
            btnContinuar.Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);
            btnContinuar.Click += (s, e) => Continuar();
            AcceptButton = btnContinuar;

            lblErrorCodigo = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold),
                ForeColor = Rojo
            };

            lblCodigoOk = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Visible = false
            };

            lnkOtroCodigo = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Text = "Usar otro código",
                Visible = false
            };
            lnkOtroCodigo.LinkClicked += (s, e) => OlvidarCodigo();

            columna.Controls.AddRange(new Control[] { lblPaso1, txtCodigo, btnContinuar, lblErrorCodigo, lblCodigoOk, lnkOtroCodigo });
        }

        private void CrearPaso2()
        {
            lblPaso2 = TituloPaso("Paso 2. ¿Cómo está este equipo?");

            pnlEstados = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.White };
            rbTodoBien = Chip("Todo bien", true);
            rbFalla = Chip("Reportar una falla", true);
            rbPropio = Chip("Traje mi propio equipo", true);
            foreach (RadioButton estado in new[] { rbTodoBien, rbFalla, rbPropio })
            {
                estado.CheckedChanged += (s, e) =>
                {
                    if (((RadioButton)s).Checked)
                    {
                        MostrarErrorPaso2("");
                        ActualizarPaso2();
                    }
                };
                pnlEstados.Controls.Add(estado);
            }

            lblPista = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10.5F),
                ForeColor = TextoSecundario
            };

            // Una fila por area: "Sin problema" marcado, las fallas de la lista y "Otro".
            pnlFallas = new Panel { BackColor = Color.White, Visible = false };
            foreach (string area in Consultas.AreasDeFalla)
            {
                Label etiqueta = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold),
                    ForeColor = Color.DarkGreen,
                    Text = char.ToUpper(area[0]) + area.Substring(1)
                };

                FlowLayoutPanel chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.White };
                RadioButton ninguna = Chip(SinProblema, false);
                ninguna.Checked = true;
                chips.Controls.Add(ninguna);
                foreach (string opcion in Consultas.OpcionesDeFalla(area))
                {
                    chips.Controls.Add(Chip(opcion, false));
                }

                RadioButton otra = Chip(Otro, false);
                chips.Controls.Add(otra);
                foreach (RadioButton chip in chips.Controls)
                {
                    chip.CheckedChanged += (s, e) =>
                    {
                        if (((RadioButton)s).Checked)
                        {
                            MostrarErrorPaso2("");
                            Colocar();
                        }
                    };
                }

                TextBox texto = new TextBox
                {
                    Font = new DrawingFont("Century Gothic", 11F),
                    MaxLength = 255,
                    Visible = false
                };
                otra.CheckedChanged += (s, e) =>
                {
                    // Colocar decide si se ve la caja (segun el chip "Otro"); aqui solo el foco.
                    Colocar();
                    if (otra.Checked)
                    {
                        texto.Focus();
                    }
                };

                etiquetasArea[area] = etiqueta;
                chipsArea[area] = chips;
                otroArea[area] = texto;
                pnlFallas.Controls.AddRange(new Control[] { etiqueta, chips, texto });
            }

            lblErrorPaso2 = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold),
                ForeColor = Rojo,
                TextAlign = ContentAlignment.MiddleRight
            };

            btnRegistrar = EstiloAdmin.BotonPrincipal("Registrar mi bitácora", Point.Empty, new Size(300, 52));
            btnRegistrar.Font = new DrawingFont("Century Gothic", 13F, FontStyle.Bold);
            btnRegistrar.Click += (s, e) => Registrar();
            // Deshabilitado se ve gris: verde oscuro con letra gris parecia pulsable.
            btnRegistrar.EnabledChanged += (s, e) =>
            {
                Color fondo = btnRegistrar.Enabled ? Color.DarkGreen : Color.FromArgb(205, 210, 205);
                btnRegistrar.BackColor = fondo;
                btnRegistrar.FlatAppearance.BorderColor = fondo;
            };

            // El error y el boton van en la barra de abajo (ver InicializarComponentes).
            columna.Controls.AddRange(new Control[] { lblPaso2, pnlEstados, lblPista, pnlFallas });
        }

        private static Label TituloPaso(string texto)
        {
            return new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 15F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Text = texto
            };
        }

        /// <summary>Opcion que se marca como un boton: verde relleno cuando esta elegida.</summary>
        private static RadioButton Chip(string texto, bool grande)
        {
            RadioButton chip = new RadioButton
            {
                Appearance = Appearance.Button,
                AutoSize = true,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", grande ? 12.5F : 10.5F, grande ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = Color.DarkGreen,
                Margin = new Padding(0, 0, grande ? 12 : 8, 8),
                MinimumSize = grande ? new Size(210, 54) : new Size(0, 34),
                Padding = grande ? new Padding(16, 6, 16, 6) : new Padding(8, 2, 8, 2),
                Text = texto,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false
            };
            chip.FlatAppearance.BorderColor = Color.DarkGreen;
            chip.FlatAppearance.BorderSize = grande ? 2 : 1;
            chip.FlatAppearance.CheckedBackColor = Color.DarkGreen;
            chip.FlatAppearance.MouseOverBackColor = Color.FromArgb(236, 246, 236);
            chip.CheckedChanged += (s, e) => chip.ForeColor = chip.Checked ? Color.White : Color.DarkGreen;
            return chip;
        }

        /// <summary>Coloca todo de arriba abajo en una columna centrada; lo que no toca, se oculta.</summary>
        private void Colocar()
        {
            if (columna == null || contenedor == null)
            {
                return;
            }

            int ancho = Math.Min(AnchoMaximo, Math.Max(560, contenedor.ClientSize.Width - 60));
            int y = 0;

            pnlClase.SetBounds(0, y, ancho, 84);
            lblClaseTitulo.SetBounds(18, 12, ancho - 36, 26);
            lblClaseDetalle.SetBounds(18, 40, ancho - 36, 40);
            y += pnlClase.Height + 30;

            // --- Paso 1 ---
            lblPaso1.SetBounds(0, y, ancho, 32);
            y += 40;
            bool pidiendoCodigo = codigoAceptado == null;
            txtCodigo.Visible = pidiendoCodigo;
            btnContinuar.Visible = pidiendoCodigo;
            lblCodigoOk.Visible = !pidiendoCodigo;
            lnkOtroCodigo.Visible = !pidiendoCodigo && !registrado;
            if (pidiendoCodigo)
            {
                txtCodigo.Location = new Point(0, y);
                btnContinuar.SetBounds(txtCodigo.Right + 12, y, btnContinuar.Width, txtCodigo.Height);
                y += txtCodigo.Height + 8;
                bool hayError = lblErrorCodigo.Text.Length > 0;
                lblErrorCodigo.Visible = hayError;
                if (hayError)
                {
                    int alto = TextRenderer.MeasureText(lblErrorCodigo.Text, lblErrorCodigo.Font, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height;
                    lblErrorCodigo.SetBounds(0, y, ancho, alto + 4);
                    y += alto + 8;
                }
            }
            else
            {
                lblErrorCodigo.Visible = false;
                lblCodigoOk.SetBounds(0, y, ancho, 28);
                y += 32;
                if (lnkOtroCodigo.Visible)
                {
                    lnkOtroCodigo.Location = new Point(0, y);
                    y += 26;
                }
            }

            y += 26;

            // --- Paso 2, o el "listo" cuando ya se registro ---
            foreach (Control control in new Control[] { lblPaso2, pnlEstados, lblPista, btnRegistrar })
            {
                control.Visible = !registrado;
            }

            lblListo.Visible = registrado;
            lblListoDetalle.Visible = registrado;
            lblErrorPaso2.Visible = !registrado && lblErrorPaso2.Text.Length > 0;
            if (registrado)
            {
                pnlFallas.Visible = false;
                lblListo.SetBounds(0, y, ancho, 38);
                y += 44;
                lblListoDetalle.SetBounds(0, y, ancho, 50);
                y += 56;
            }
            else
            {
                lblPaso2.SetBounds(0, y, ancho, 32);
                y += 42;
                pnlEstados.MaximumSize = new Size(ancho, 0);
                pnlEstados.Location = new Point(0, y);
                pnlEstados.PerformLayout();
                y += pnlEstados.Height + 2;
                lblPista.SetBounds(0, y, ancho, 24);
                y += 32;

                pnlFallas.Visible = rbFalla.Checked;
                if (pnlFallas.Visible)
                {
                    int yf = 0;
                    const int anchoEtiqueta = 110;
                    foreach (string area in Consultas.AreasDeFalla)
                    {
                        etiquetasArea[area].SetBounds(0, yf + 6, anchoEtiqueta, 26);
                        FlowLayoutPanel chips = chipsArea[area];
                        chips.MaximumSize = new Size(ancho - anchoEtiqueta, 0);
                        chips.Location = new Point(anchoEtiqueta, yf);
                        chips.PerformLayout();
                        yf += chips.Height;
                        TextBox texto = otroArea[area];
                        texto.Visible = chips.Controls.OfType<RadioButton>().Any(c => c.Checked && c.Text == Otro);
                        if (texto.Visible)
                        {
                            texto.SetBounds(anchoEtiqueta, yf, ancho - anchoEtiqueta, texto.Height);
                            yf += texto.Height + 8;
                        }

                        yf += 10;
                    }

                    pnlFallas.SetBounds(0, y, ancho, yf);
                    y += yf + 4;
                }
            }

            y += 20;
            columna.Size = new Size(ancho, y);
            int x = Math.Max(20, (contenedor.ClientSize.Width - ancho) / 2);
            columna.Location = new Point(x + contenedor.AutoScrollPosition.X, 28 + contenedor.AutoScrollPosition.Y);

            // La barra de abajo, alineada con la columna: el perfil a la izquierda,
            // el boton a la derecha y, pegado a el, lo que falte para registrar.
            if (pie != null)
            {
                btnRegistrar.Location = new Point(x + ancho - btnRegistrar.Width, (pie.Height - btnRegistrar.Height) / 2);
                lnkPerfil.Location = new Point(x, (pie.Height - lnkPerfil.Height) / 2);
                int inicioError = lnkPerfil.Right + 20;
                lblErrorPaso2.SetBounds(inicioError, 0, Math.Max(50, btnRegistrar.Left - 16 - inicioError), pie.Height);
            }
        }

        // =====================================================================
        // Paso 1: el codigo
        // =====================================================================

        private void MostrarErrorCodigo(string texto)
        {
            if (lblErrorCodigo.Text == texto)
            {
                return;
            }

            lblErrorCodigo.Text = texto;
            Colocar();
        }

        private void Continuar()
        {
            string codigo = txtCodigo.Text.Trim();
            if (codigo.Length == 0)
            {
                MostrarErrorCodigo("Escribe el código que te dicta tu profesor.");
                txtCodigo.Focus();
                return;
            }

            // El codigo se resuelve para ESTE laboratorio: sin laboratorio no hay nada que hacer.
            if (!LaboratorioEquipo.Resuelto)
            {
                MostrarErrorCodigo("Este equipo no tiene laboratorio asignado. Avisa al encargado del laboratorio.");
                return;
            }

            Cursor = Cursors.WaitCursor;
            btnContinuar.Enabled = false;
            try
            {
                ResultadoCodigo resultado;
                SesionLocal sesionLocal = null;
                if (SinConexion.Activo)
                {
                    resultado = SinConexion.Cache.ResolverCodigo(codigo, matricula, out sesionLocal);
                }
                else
                {
                    try
                    {
                        int idUsuario = consultas.IdDeUsuario(matricula);
                        if (idUsuario == 0)
                        {
                            MostrarErrorCodigo("Tu matrícula no está registrada. Avisa al encargado del laboratorio.");
                            return;
                        }

                        resultado = new HorariosDatos().ResolverCodigo(codigo, LaboratorioEquipo.Id, idUsuario);
                    }
                    catch (Exception ex)
                    {
                        // Fase 4: si el servidor se cayo a media manana, se sigue con la copia local.
                        Log.Error("Validar codigo de acceso '" + codigo + "'", ex);
                        if (!SinConexion.IntentarActivar(ex.Message))
                        {
                            MostrarErrorCodigo("No se pudo validar el código. Intenta de nuevo; si sigue fallando, avisa al encargado del laboratorio.");
                            return;
                        }

                        resultado = SinConexion.Cache.ResolverCodigo(codigo, matricula, out sesionLocal);
                        ActualizarDatos();
                    }
                }

                // Quien ya registro y vuelve durante la misma clase (se fue la luz,
                // se reinicio el equipo) no es un error: puede seguir usandolo.
                if (resultado.Motivo == MotivoCodigo.YaRegistrado && resultado.Ventana != null
                    && OfrecerReanudar(resultado.Ventana, codigo, sesionLocal))
                {
                    return;
                }

                if (!resultado.Aceptado)
                {
                    Log.Info("Codigo '" + codigo + "' rechazado (" + resultado.Motivo + ") en " + LaboratorioEquipo.Nombre + ".");
                    MostrarErrorCodigo(resultado.Mensaje);
                    txtCodigo.SelectAll();
                    txtCodigo.Focus();
                    return;
                }

                Aceptar(codigo, resultado.Ventana, sesionLocal);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnContinuar.Enabled = true;
            }
        }

        /// <summary>
        /// Le devuelve el equipo a quien ya registro y sigue en clase. True si se
        /// atendio el caso (reanudo, o dijo que no); false para mostrar el aviso de
        /// siempre. La hora es la del SERVIDOR, como en todo lo demas.
        /// </summary>
        private bool OfrecerReanudar(VentanaCodigo ventanaRegistrada, string codigo, SesionLocal sesionLocal)
        {
            if (ventanaRegistrada.AhoraServidor >= ventanaRegistrada.Fin || !Kiosco.Activo)
            {
                // La clase ya termino, o fuera del kiosco no hay equipo que liberar.
                return false;
            }

            if (!DialogoConfirmacion.Preguntar(this, "Continuar en esta clase",
                "Ya registraste tu bitácora en esta clase.",
                "Tu primer registro es el que cuenta y se queda como está. Puedes volver a usar este equipo hasta las "
                    + ventanaRegistrada.Fin.ToString("HH:mm") + ".",
                "Volver a usar el equipo", false))
            {
                Log.Info("Se ofrecio reanudar la clase de " + matricula + " con el codigo " + codigo + " y dijo que no.");
                return true;
            }

            Log.Info("Reanudando la clase de " + matricula + " con el codigo " + codigo + " hasta "
                + ventanaRegistrada.Fin.ToString("HH:mm") + ": ya tenia bitacora de esta sesion.");
            Datos_User.Setcodigo(codigo);
            Datos_User.VentanaActual = ventanaRegistrada;
            Datos_User.SesionSinConexion = sesionLocal;
            SesionEquipo.Liberar(ventanaRegistrada, matricula, codigo);
            return true;
        }

        private void Aceptar(string codigo, VentanaCodigo aceptada, SesionLocal sesionLocal)
        {
            codigoAceptado = codigo;
            ventana = aceptada;
            // Lo leen consultaFinalSesion (sin conexion) y la liberacion del equipo.
            Datos_User.Setcodigo(codigo);
            Datos_User.VentanaActual = aceptada;
            Datos_User.SesionSinConexion = sesionLocal;
            Log.Info("Codigo '" + codigo + "' aceptado para " + matricula + " en " + LaboratorioEquipo.Nombre
                + " (sesion " + aceptada.IdCodigo + ", hasta " + aceptada.Fin.ToString("HH:mm") + ").");

            lblCodigoOk.Text = "Código " + codigo + " aceptado: " + aceptada.Materia + ", " + aceptada.Grupo
                + ", hasta las " + aceptada.Fin.ToString("HH:mm") + ".";
            MostrarClase(aceptada.Materia + ", " + aceptada.Grupo
                + (string.IsNullOrEmpty(aceptada.Profesor) ? "" : ", con " + aceptada.Profesor)
                + ", hasta las " + aceptada.Fin.ToString("HH:mm") + ".", true);

            // El foco pasa al enlace ANTES de esconder la caja del codigo: si no,
            // Windows se lo daria a "Todo bien" y un RadioButton que recibe el foco
            // sin raton ni Tab se marca solo; el alumno debe elegir a proposito.
            lnkOtroCodigo.Visible = true;
            lnkOtroCodigo.Focus();
            AcceptButton = null;
            ActualizarPaso2();
        }

        private void OlvidarCodigo()
        {
            codigoAceptado = null;
            ventana = null;
            Datos_User.VentanaActual = null;
            Datos_User.SesionSinConexion = null;
            AcceptButton = btnContinuar;
            MostrarErrorCodigo("");
            ActualizarPaso2();
            ActualizarDatos();
            txtCodigo.SelectAll();
            txtCodigo.Focus();
        }

        // =====================================================================
        // Paso 2: como esta el equipo
        // =====================================================================

        private void ActualizarPaso2()
        {
            bool activo = codigoAceptado != null;
            lblPaso2.ForeColor = activo ? Color.DarkGreen : Apagado;
            pnlEstados.Enabled = activo;
            pnlFallas.Enabled = activo;
            btnRegistrar.Enabled = activo && (rbTodoBien.Checked || rbFalla.Checked || rbPropio.Checked);

            string equipo = LaboratorioEquipo.NumeroMaquina;
            lblPista.Text = !activo ? "Primero escribe el código de tu clase."
                : rbTodoBien.Checked ? "Se registra sin fallas y el equipo " + equipo + " queda para ti hasta las " + ventana.Fin.ToString("HH:mm") + "."
                : rbFalla.Checked ? "Marca lo que falla en cada parte; el encargado del laboratorio lo revisará."
                : rbPropio.Checked ? "No usarás este equipo: al registrar podrás apagarlo o dejarlo listo para otro compañero."
                : "Elige una opción; después pulsa «Registrar mi bitácora».";
            Colocar();
        }

        private void MostrarErrorPaso2(string texto)
        {
            if (lblErrorPaso2.Text == texto)
            {
                return;
            }

            lblErrorPaso2.Text = texto;
            Colocar();
        }

        /// <summary>
        /// Falla y comentario de un area, como los guardaba Registro_Bitacora:
        /// "Ninguno" sin problema, el texto de la lista, u "Otro" con lo que
        /// escribio ("Otro (sin detalle)" si no escribio nada).
        /// </summary>
        private void LeerArea(string area, out string falla, out string comentario)
        {
            RadioButton elegida = chipsArea[area].Controls.OfType<RadioButton>().FirstOrDefault(c => c.Checked);
            string texto = elegida == null ? SinProblema : elegida.Text;
            comentario = "";
            if (texto == SinProblema)
            {
                falla = "Ninguno";
            }
            else if (texto == Otro)
            {
                falla = "Otro";
                comentario = otroArea[area].Text.Trim();
                if (comentario.Length == 0)
                {
                    comentario = "Otro (sin detalle)";
                }
            }
            else
            {
                falla = texto;
            }
        }

        private void Registrar()
        {
            if (ventana == null || registrado)
            {
                return;
            }

            string[] valores;
            if (rbTodoBien.Checked)
            {
                valores = new[] { "Ninguno", "Ninguno", "Ninguno", "Ninguno", "Ninguno", "Ninguno" };
            }
            else if (rbPropio.Checked)
            {
                // Trajo su computadora: no hay nada que reportar de esta maquina.
                valores = new[] { "EP", "Equipo Propio", "EP", "Equipo Propio", "EP", "Equipo Propio" };
            }
            else if (rbFalla.Checked)
            {
                valores = new string[6];
                for (int i = 0; i < Consultas.AreasDeFalla.Length; i++)
                {
                    LeerArea(Consultas.AreasDeFalla[i], out valores[2 * i], out valores[2 * i + 1]);
                }

                if (valores.Where((v, i) => i % 2 == 0).All(v => v == "Ninguno"))
                {
                    MostrarErrorPaso2("Marca qué falla tiene el equipo, o elige «Todo bien».");
                    return;
                }
            }
            else
            {
                MostrarErrorPaso2("Elige cómo está el equipo.");
                return;
            }

            bool guardado;
            Cursor = Cursors.WaitCursor;
            btnRegistrar.Enabled = false;
            try
            {
                guardado = consultas.consultaFinalSesion(ventana.IdCodigo, matricula, LaboratorioEquipo.NumeroMaquina,
                    valores[0], valores[1], valores[2], valores[3], valores[4], valores[5]);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnRegistrar.Enabled = true;
            }

            if (!guardado)
            {
                // consultaFinalSesion ya dijo por que (codigo vencido, ya registrado...).
                return;
            }

            registrado = true;
            if (rbPropio.Checked)
            {
                TerminarConEquipoPropio();
                return;
            }

            lblListo.Text = "Listo, tu bitácora quedó registrada.";
            lblListoDetalle.Text = (SinConexion.Activo ? "Se guardó en este equipo y se enviará sola cuando vuelva la conexión. " : "")
                + "Puedes usar este equipo hasta las " + ventana.Fin.ToString("HH:mm") + ".";
            Colocar();
            Refresh();

            // Un momento para que se lea el "listo" y se entrega el equipo hasta la hora de salida.
            VentanaCodigo liberar = ventana;
            string codigo = codigoAceptado;
            Timer espera = new Timer { Interval = 1500 };
            espera.Tick += (s, e) =>
            {
                espera.Stop();
                espera.Dispose();
                SesionEquipo.Liberar(liberar, matricula, codigo);
            };
            espera.Start();
        }

        /// <summary>
        /// Registro con equipo propio: la maquina del laboratorio NO se libera,
        /// porque no la va a usar; se ofrece apagarla o dejarla en el inicio.
        /// </summary>
        private void TerminarConEquipoPropio()
        {
            Log.Info("Bitacora de '" + matricula + "' guardada con equipo propio; el equipo no se libera.");
            RegistroExitoso.Opcion opcion = RegistroExitoso.Mostrar(
                string.IsNullOrEmpty(nombre) ? matricula : nombre, matricula, ventana.Laboratorio ?? LaboratorioEquipo.Nombre, codigoAceptado);

            if (opcion == RegistroExitoso.Opcion.Apagar)
            {
                if (SesionEquipo.ApagarEquipo())
                {
                    // Windows se esta apagando; no hay nada mas que hacer.
                    return;
                }

                if (!Kiosco.Activo)
                {
                    MessageBox.Show(this, "Modo escritorio: aquí se apagaría el equipo. Se vuelve al inicio.",
                        "DigBit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            if (AppContexto.Actual != null)
            {
                AppContexto.Actual.CerrarSesion();
            }
        }

        private void AbrirPerfil()
        {
            if (perfil == null || perfil.IsDisposed)
            {
                perfil = new editar_Perfil("alumno");
                perfil.FormClosed += (s, e) => ActualizarDatos();
            }

            if (!perfil.Visible)
            {
                perfil.Show(this);
            }

            perfil.Activate();
        }

        // =====================================================================
        // Reloj y la clase de ahora
        // =====================================================================

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            relojTimer.Start();
            datosTimer.Start();
            ActualizarDatos();
            txtCodigo.Focus();
        }

        private void ActualizarReloj()
        {
            DateTime ahora = DateTime.Now;
            lblReloj.Text = ahora.ToString("HH:mm");
            string fecha = ahora.ToString("dddd d 'de' MMMM 'de' yyyy", Espanol);
            lblFecha.Text = char.ToUpper(fecha[0], Espanol) + fecha.Substring(1);
        }

        /// <summary>El nombre y la clase de ahora: de la copia local sin conexion; si no, del servidor en segundo plano.</summary>
        private void ActualizarDatos()
        {
            if (actualizando || IsDisposed)
            {
                return;
            }

            if (SinConexion.Activo)
            {
                DatosPantalla local = new DatosPantalla();
                try
                {
                    AlumnoCache alumno = SinConexion.Cache.BuscarAlumno(matricula);
                    local.Nombre = alumno == null ? null : alumno.Nombre;
                    local.Clase = LaboratorioEquipo.Resuelto ? SinConexion.Cache.ClaseEnCurso(SinConexion.Cache.AhoraEstimado) : null;
                }
                catch (Exception ex)
                {
                    Log.Error("Leer la clase de ahora de la copia local", ex);
                }

                Aplicar(local);
                return;
            }

            actualizando = true;
            string numero = matricula;
            Task.Run(() =>
            {
                DatosPantalla datos = new DatosPantalla { Nombre = new Consultas().NombreDeUsuario(numero) };
                if (LaboratorioEquipo.Resuelto)
                {
                    DateTime ahora;
                    datos.Clase = new HorariosDatos().ClaseEnCurso(LaboratorioEquipo.Id, out ahora);
                }

                return datos;
            }).ContinueWith(tarea =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        actualizando = false;
                        if (tarea.IsFaulted)
                        {
                            Log.Aviso("Pantalla del alumno: no se pudo leer la clase de ahora: " + tarea.Exception.GetBaseException().Message);
                            if (codigoAceptado == null)
                            {
                                MostrarClase("No se pudo consultar la clase de ahora. Escribe tu código de todos modos.", false);
                            }

                            return;
                        }

                        Aplicar(tarea.Result);
                    }));
                }
                catch (InvalidOperationException)
                {
                    // La pantalla se cerro mientras se consultaba.
                }
            });
        }

        private void Aplicar(DatosPantalla datos)
        {
            if (IsDisposed)
            {
                return;
            }

            if (!string.IsNullOrEmpty(datos.Nombre))
            {
                nombre = datos.Nombre;
                lblSaludo.Text = "Hola, " + nombre;
            }

            if (codigoAceptado != null)
            {
                return;
            }

            if (!LaboratorioEquipo.Resuelto)
            {
                lblClaseTitulo.Text = "Este equipo no tiene laboratorio asignado";
                lblClaseTitulo.ForeColor = Rojo;
                lblClaseDetalle.Text = "Avisa al encargado del laboratorio: sin eso no se puede registrar la bitácora.";
                pnlClase.BackColor = FondoAviso;
                return;
            }

            SesionAgenda clase = datos.Clase;
            MostrarClase(clase == null
                ? "Ahora no hay clase programada. Si tu profesor te dio un código, escríbelo: se acepta desde "
                  + HorariosDatos.MargenAntesDelInicioMinutos + " minutos antes de empezar."
                : clase.Materia + ", " + clase.Grupo + ", con " + clase.Profesor + ", hasta las " + HorariosDatos.Hora(clase.Fin)
                  + (clase.EsExtra ? " (sesión extra)." : "."), false);
        }

        /// <summary>La franja de arriba: "Ahora en CISCO, equipo PC-3" (o "Tu clase en ...") y el detalle.</summary>
        private void MostrarClase(string detalle, bool esLaTuya)
        {
            string laboratorio = LaboratorioEquipo.Nombre ?? "";
            string equipo = LaboratorioEquipo.NumeroMaquina;
            lblClaseTitulo.Text = (esLaTuya ? "Tu clase en " : "Ahora en ") + laboratorio + ", equipo " + equipo
                + (SinConexion.Activo ? " (sin conexión con el servidor)" : "");
            lblClaseTitulo.ForeColor = Color.DarkGreen;
            lblClaseDetalle.Text = detalle + (SinConexion.Activo ? " Tu bitácora se guardará en este equipo y se enviará sola." : "");
            pnlClase.BackColor = SinConexion.Activo ? FondoAviso : FondoClase;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            relojTimer.Stop();
            datosTimer.Stop();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                componentes.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
