using DigBit.conexion;
using DigBit.Infraestructura;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Pestana Calendario de Horarios: que clases se quedaron sin bitacoras. Se
    /// elige un laboratorio; a la izquierda sale el calendario del mes con cada
    /// dia en rojo si alguna clase se quedo sin bitacoras (verde si todas las que
    /// contaban registraron, gris si no contaban o aun no pasan); al pulsar un
    /// dia, a la derecha sale el horario de su semana tal cual, sin los colores
    /// ni los codigos de los profesores, con en rojo las clases sin bitacoras.
    /// Con db/22, una clase sin bitacoras pero con asistencias que dio su
    /// profesor sale en ambar: se dio (ni falta ni "Marcar como no dada").
    /// Lee HorariosDatos.LeerCalendario: las seis semanas que se ven del mes.
    /// </summary>
    internal class PestanaCalendario : Panel
    {
        internal static readonly Color RojoFalta = Color.FromArgb(229, 72, 72);
        internal static readonly Color VerdeBien = Color.FromArgb(200, 232, 200);
        internal static readonly Color GrisNoCuenta = Color.FromArgb(234, 234, 234);
        // Sin bitacoras pero con asistencias que dio su profesor (db/22): la clase
        // se dio. Ambar, no rojo.
        internal static readonly Color AmbarDadas = Color.FromArgb(255, 235, 156);
        internal static readonly Color TextoAmbarDadas = Color.FromArgb(156, 87, 0);
        private static readonly Color TextoGris = Color.FromArgb(140, 140, 140);
        private const int AnchoIzquierda = 340;

        // Lo ultimo que se eligio en esta sesion de DigBit: al volver a abrir
        // Horarios se sigue en el mismo laboratorio y con la misma casilla. La
        // casilla empieza sin marcar: asi el rojo es siempre una falta de verdad
        // (lo de antes de DigBit y los laboratorios que aun no lo usan, en gris).
        private static int ultimoLaboratorio;
        private static bool contarTodos;

        private readonly HorariosDatos datos = new HorariosDatos();
        private readonly Func<int> idAdministrador;
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };

        private readonly ComboBox cboLaboratorio;
        private readonly CheckBox chkContarTodos;
        private readonly Button btnActualizar;
        private readonly Label lblPasos;
        private readonly MesDeBitacoras mes;
        private readonly LeyendaDelMes leyendaMes;
        private readonly Label lblMes;
        private readonly Label lblTitulo;
        private readonly Label lblResumen;
        private readonly Panel contenedor;
        private readonly CuadriculaSemanal cuadricula;
        private readonly Label lblLeyenda;
        private readonly LinkLabel lnkOtras;
        // "Ver esta semana en Horario semanal >", a la derecha del titulo de la semana.
        private readonly LinkLabel lnkVerSemana;
        private readonly ContextMenuStrip menuClase;
        // "No tomar en cuenta..." y "Volver a contar...": bajo el resumen del mes,
        // solo la administracion (en solo consulta no estan y no ocupan sitio).
        private readonly Button btnNoTomar;
        private readonly Button btnVolverAContar;
        private readonly bool conBotones;
        private int anchoIzquierdaActual = AnchoIzquierda;

        private CalendarioBitacoras calendario;
        private bool iniciado;
        // Mientras el laboratorio se pone desde fuera (ElegirLaboratorio), no se avisa.
        private bool eligiendoDesdeFuera;
        // La lista de laboratorios no se pudo leer (Actualizar la vuelve a pedir).
        private bool laboratoriosConError;
        private List<ClaseDelCalendario> clasesVistas = new List<ClaseDelCalendario>();
        private ClaseDelCalendario claseEnMenu;

        /// <summary>
        /// "Ver esta semana en Horario semanal >": el laboratorio y el lunes de la
        /// semana que se ve (AdministrarHorarios lleva a esa semana de Horario semanal).
        /// </summary>
        public event Action<int, DateTime> VerSemanaEnHorario;

        /// <summary>
        /// La persona eligio otro laboratorio aqui (no cuando se pone con
        /// ElegirLaboratorio): su id. Para que las demas pestanas lo sigan.
        /// </summary>
        public event Action<int> LaboratorioCambiado;

        /// <param name="idAdministrador">Quien cancela una clase desde aqui (creada_por).</param>
        public PestanaCalendario(Func<int> idAdministrador)
        {
            this.idAdministrador = idAdministrador;
            BackColor = Color.White;

            // 1. El laboratorio.
            Controls.Add(EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(12, 15)));
            cboLaboratorio = EstiloHorarios.CrearCombo(new Point(112, 11), 300);
            cboLaboratorio.MaxDropDownItems = 16;
            cboLaboratorio.SelectedIndexChanged += (s, e) => AlCambiarLaboratorio();

            // Un laboratorio que todavia registra en el sistema anterior no tiene
            // bitacoras aqui: sin esto sale en gris ("no cuenta"); con esto, en rojo.
            chkContarTodos = new CheckBox
            {
                AutoSize = true,
                Checked = contarTodos,
                Font = new DrawingFont("Century Gothic", 9.5F),
                Location = new Point(430, 14),
                Text = "Contar también los laboratorios que aún no usan DigBit"
            };
            chkContarTodos.CheckedChanged += (s, e) =>
            {
                contarTodos = chkContarTodos.Checked;
                if (iniciado)
                {
                    Releer();
                }
            };
            ayuda.SetToolTip(chkContarTodos,
                "Sin marcar (así empieza), el rojo es siempre una falta de verdad: cada laboratorio cuenta desde su primera\n"
                + "bitácora con DigBit, y lo de antes, igual que los laboratorios que aún no lo usan, sale en gris («no cuenta»).\n"
                + "Márcala para ver en rojo también las clases sin bitácoras de los laboratorios que todavía registran en el\n"
                + "sistema anterior: ahí el rojo no quiere decir que no se dio la clase.");

            btnActualizar = EstiloHorarios.CrearBoton("Actualizar", new Point(880, 8), new Size(120, 34));
            btnActualizar.Click += (s, e) => Releer();

            // 2. El calendario del mes; 3. la semana pulsada, a la derecha.
            lblPasos = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.Gray,
                Location = new Point(12, 48),
                Size = new Size(AnchoIzquierda, 34),
                Text = "Pulsa un día para ver el horario de su semana a la derecha. En rojo: días con alguna clase sin bitácoras."
            };

            mes = new MesDeBitacoras { Location = new Point(12, 84), Size = new Size(AnchoIzquierda, 320) };
            mes.SemanaElegida += lunes => MostrarSemana();
            mes.MesCambiado += primero => MesCambiado();

            leyendaMes = new LeyendaDelMes { Location = new Point(12, 410), Size = new Size(AnchoIzquierda, 42) };

            lblMes = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.FromArgb(60, 60, 60),
                Location = new Point(12, 458),
                Size = new Size(AnchoIzquierda, 90)
            };

            lblTitulo = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(AnchoIzquierda + 28, 48),
                Size = new Size(600, 22)
            };

            lblResumen = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.FromArgb(60, 60, 60),
                Location = new Point(AnchoIzquierda + 28, 72),
                Size = new Size(600, 34)
            };

            contenedor = new Panel
            {
                Location = new Point(AnchoIzquierda + 28, 110),
                Size = new Size(600, 340),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            // El horario tal cual, sin colores ni codigos (se puede ensenar o imprimir).
            // Las horas, como en Horario semanal: mas alla de 07:00 a 21:00 si una
            // clase de la semana empieza antes o acaba despues.
            cuadricula = new CuadriculaSemanal
            {
                Dock = DockStyle.Fill,
                SoloLectura = true,
                SinCodigos = true,
                HorasSegunClases = true,
                AspectoDeFranja = AspectoDeFranja,
                AspectoDeExtra = AspectoDeExtra
            };
            cuadricula.FranjaClic += f => { if (f != null) MostrarMenu(ClaseDeFranja(f)); };
            cuadricula.ExcepcionClic += x => MostrarMenu(clasesVistas.FirstOrDefault(c => c.EsExtra && c.ExcepcionId == x.Id));
            contenedor.Controls.Add(cuadricula);

            lblLeyenda = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Color.Gray,
                Location = new Point(AnchoIzquierda + 28, 456),
                Size = new Size(420, 18),
                Text = "Rojo: ya pasó y nadie registró bitácora. Gris: por venir o no cuenta. Rayado: cancelada. Marco: sesión extra."
            };
            // En solo consulta (Permisos) una clase no se puede marcar como no dada.
            // Con db/22 la ayuda dice tambien el ambar (AyudaDeLaLeyenda, al leer).
            ayuda.SetToolTip(lblLeyenda, AyudaDeLaLeyenda(false));

            lnkOtras = new LinkLabel
            {
                AutoSize = false,
                TextAlign = ContentAlignment.TopRight,
                Font = new DrawingFont("Century Gothic", 8.5F),
                LinkColor = Color.DarkGreen,
                Location = new Point(800, 456),
                Size = new Size(176, 18)
            };
            lnkOtras.LinkClicked += (s, e) => MostrarOtras();

            lnkVerSemana = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9F),
                LinkColor = Color.DarkGreen,
                Location = new Point(800, 50),
                Text = "Ver esta semana en Horario semanal >"
            };
            lnkVerSemana.LinkClicked += (s, e) =>
            {
                int laboratorio = LaboratorioElegido();
                if (laboratorio != 0 && VerSemanaEnHorario != null)
                {
                    VerSemanaEnHorario(laboratorio, mes.Semana);
                }
            };
            ayuda.SetToolTip(lnkVerSemana, "Lleva a Horario semanal, en este laboratorio y esta semana: el horario que se usa, con sus cambios de un día.");

            menuClase = new ContextMenuStrip { Font = new DrawingFont("Century Gothic", 9.5F) };
            menuClase.Items.Add("Ver quién registró...", null, (s, e) => VerBitacoras());
            menuClase.Items.Add("Cancelar esta clase ese día (no se dio)...", null, (s, e) => MarcarNoDada());

            // 4. Las clases en rojo de unas fechas que no se dieron por un motivo
            // (un cambio de horario): dejan de contar en lote, y se puede deshacer.
            conBotones = !Permisos.SoloConsulta;
            btnNoTomar = EstiloHorarios.CrearBoton("No tomar en cuenta...", new Point(12, 558), new Size(182, 34));
            btnNoTomar.Click += (s, e) => NoTomarEnCuenta();
            ayuda.SetToolTip(btnNoTomar,
                "Las clases sin bitácoras (en rojo) de unas fechas dejan de contar como faltas, por ejemplo por un cambio de horario.\n"
                + "Cada una se cancela ese día con el motivo. No se borra nada y se puede deshacer con «Volver a contar...».");
            btnVolverAContar = EstiloHorarios.CrearBoton("Volver a contar...", new Point(202, 558), new Size(150, 34));
            btnVolverAContar.Click += (s, e) => VolverAContar();
            ayuda.SetToolTip(btnVolverAContar, "Deshace un «No tomar en cuenta»: sus clases vuelven a contar en Faltas como antes.");
            btnNoTomar.Visible = conBotones;
            btnVolverAContar.Visible = conBotones;

            Controls.Add(cboLaboratorio);
            Controls.Add(chkContarTodos);
            Controls.Add(btnActualizar);
            Controls.Add(lblPasos);
            Controls.Add(mes);
            Controls.Add(leyendaMes);
            Controls.Add(lblMes);
            Controls.Add(lblTitulo);
            Controls.Add(lnkVerSemana);
            Controls.Add(lblResumen);
            Controls.Add(contenedor);
            Controls.Add(lblLeyenda);
            Controls.Add(lnkOtras);
            Controls.Add(btnNoTomar);
            Controls.Add(btnVolverAContar);
            Resize += (s, e) => Acomodar();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                ayuda.Dispose();
                menuClase.Dispose();
            }
        }

        /// <summary>
        /// La ayuda de la leyenda de la semana. conDadas: la base tiene las
        /// asistencias que da el profesor (db/22) y se explica tambien el ambar.
        /// </summary>
        private static string AyudaDeLaLeyenda(bool conDadas)
        {
            return "Rojo: la clase ya pasó, contaba y nadie registró bitácora (o se abrió su sesión y nadie registró).\n"
                + (conDadas ? "Ámbar: ya pasó y nadie registró bitácora, pero su docente dio la asistencia a quien no pudo\n"
                    + "registrar: la clase sí se dio.\n" : "")
                + "Blanco: con bitácoras; debajo de la materia dice cuántas se registraron.\n"
                + "Gris: todavía no termina, o no cuenta (su docente estaba de baja, la sesión extra se puso cuando ya\n"
                + "había pasado, o el laboratorio aún no registraba con DigBit y la casilla de arriba está sin marcar).\n"
                + "Rayado: cancelada ese día. Marco y «EXTRA»: sesión extra.\n"
                + (Permisos.SoloConsulta
                    ? "Clic en una clase: ver quién registró."
                    : "Clic en una clase: ver quién registró, o cancelarla ese día si no se dio.");
        }

        /// <summary>El calendario del mes a la izquierda; el horario de la semana, con todo lo demas.</summary>
        private void Acomodar()
        {
            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;
            if (ancho < 600 || alto < 300)
            {
                return;
            }

            // Arriba a la derecha si cabe; si no (ventana chica), justo despues de la casilla.
            btnActualizar.Left = Math.Max(chkContarTodos.Right + 16, ancho - 12 - btnActualizar.Width);

            // La columna del calendario crece con la ventana (es por donde se navega)
            // y sus dias son casi cuadrados, pero dejan abajo sitio para el resumen
            // del mes (unas seis lineas) aunque la ventana sea baja; con los botones
            // de la administracion, tambien para ellos.
            int anchoIzquierda = Math.Max(AnchoIzquierda, Math.Min(440, ancho * 23 / 100));
            int abajoDelMes = 96 + (conBotones ? btnNoTomar.Height + 6 : 0);
            int altoPorAlto = (alto - mes.Top - 6 - leyendaMes.Height - 6 - abajoDelMes - 12 - 52) / 6;
            int altoDia = Math.Max(30, Math.Min(56, Math.Min(anchoIzquierda / 7 - 4, altoPorAlto)));
            lblPasos.Width = anchoIzquierda;
            mes.SetBounds(12, mes.Top, anchoIzquierda, 52 + 6 * altoDia);
            leyendaMes.SetBounds(12, mes.Bottom + 6, anchoIzquierda, leyendaMes.Height);
            anchoIzquierdaActual = anchoIzquierda;
            AcomodarResumenDelMes();
            int izquierda = 12 + anchoIzquierda + 16;
            int derecha = ancho - 12;
            // El enlace a Horario semanal, a la derecha del titulo; el titulo, hasta el.
            lnkVerSemana.Location = new Point(derecha - lnkVerSemana.PreferredWidth, lblTitulo.Top + (lblTitulo.Height - lnkVerSemana.PreferredHeight) / 2);
            lblTitulo.SetBounds(izquierda, lblTitulo.Top, Math.Max(100, lnkVerSemana.Left - 12 - izquierda), lblTitulo.Height);
            lblResumen.SetBounds(izquierda, lblResumen.Top, derecha - izquierda, lblResumen.Height);
            int abajo = alto - 12 - lblLeyenda.Height;
            lnkOtras.Location = new Point(derecha - lnkOtras.Width, abajo);
            lblLeyenda.SetBounds(izquierda, abajo, Math.Max(100, lnkOtras.Left - 8 - izquierda), lblLeyenda.Height);
            contenedor.SetBounds(izquierda, contenedor.Top, derecha - izquierda, abajo - 6 - contenedor.Top);
        }

        /// <summary>
        /// El resumen del mes bajo la leyenda; con los botones de la administracion,
        /// tan alto como su texto y los botones justo debajo (a 1920 no se quedan
        /// lejos, abajo del todo). Va tambien al cambiar el texto (Pintar).
        /// </summary>
        private void AcomodarResumenDelMes()
        {
            int arriba = leyendaMes.Bottom + 6;
            int disponible = Math.Max(40, ClientSize.Height - 12 - arriba);
            if (!conBotones)
            {
                lblMes.SetBounds(12, arriba, anchoIzquierdaActual, disponible);
                return;
            }

            int texto = TextRenderer.MeasureText(lblMes.Text ?? "", lblMes.Font, new Size(anchoIzquierdaActual, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 4;
            int altoTexto = Math.Max(20, Math.Min(texto, disponible - btnNoTomar.Height - 6));
            lblMes.SetBounds(12, arriba, anchoIzquierdaActual, altoTexto);
            btnNoTomar.Location = new Point(12, lblMes.Bottom + 6);
            btnVolverAContar.Location = new Point(btnNoTomar.Right + 8, btnNoTomar.Top);
        }

        // =====================================================================
        // Lectura
        // =====================================================================

        /// <summary>
        /// Al entrar a la pestana. La primera vez: el laboratorio (el ultimo que se
        /// miro aqui o, si no, el sugerido, el del Horario) y el mes y la semana de
        /// hoy segun el servidor. Despues, lee de nuevo lo que se ve.
        /// </summary>
        public void Cargar(int laboratorioSugerido = 0)
        {
            // El del Horario manda (se abrio Horarios para ese, o es el ultimo que se miro alli).
            if (cboLaboratorio.Items.Count == 0)
            {
                LlenarLaboratorios(laboratorioSugerido != 0 ? laboratorioSugerido : ultimoLaboratorio);
            }

            if (!iniciado)
            {
                DateTime hoy = DateTime.Today;
                try
                {
                    hoy = new Consultas().AhoraServidor().Date;
                }
                catch (Exception ex)
                {
                    Log.Error("PestanaCalendario: hora del servidor", ex);
                }

                mes.Hoy = hoy;
                mes.Mes = hoy;
                mes.Semana = SelectorSemana.SemanaActual(hoy);
                iniciado = true;
            }

            Releer();
        }

        /// <summary>Para ir a una semana desde fuera (y en las pruebas): cambia de mes si hace falta.</summary>
        internal void IrA(DateTime fecha)
        {
            DateTime lunes = CuadriculaSemanal.LunesDe(fecha);
            if (lunes < mes.PrimerDiaVisible || lunes.AddDays(6) > mes.UltimoDiaVisible)
            {
                mes.Mes = fecha;
                mes.Semana = lunes;
                Releer();
                return;
            }

            mes.Semana = lunes;
            MostrarSemana();
        }

        private void LlenarLaboratorios(int preferido)
        {
            List<OpcionCombo> laboratorios = new List<OpcionCombo>();
            laboratoriosConError = false;
            try
            {
                laboratorios = datos.ObtenerLaboratoriosActivos();
            }
            catch (Exception ex)
            {
                laboratoriosConError = true;
                Log.Error("PestanaCalendario: leer laboratorios", ex);
            }

            foreach (OpcionCombo laboratorio in laboratorios)
            {
                cboLaboratorio.Items.Add(laboratorio);
            }

            // Aun sin calendario leido: AlCambiarLaboratorio no pinta nada.
            int indice = laboratorios.FindIndex(l => l.Id == preferido);
            if (cboLaboratorio.Items.Count > 0)
            {
                cboLaboratorio.SelectedIndex = indice >= 0 ? indice : 0;
            }
        }

        private int LaboratorioElegido()
        {
            OpcionCombo laboratorio = cboLaboratorio.SelectedItem as OpcionCombo;
            return laboratorio == null ? 0 : laboratorio.Id;
        }

        /// <summary>
        /// El laboratorio que se elige en otra pestana (el laboratorio compartido de
        /// AdministrarHorarios): se pinta con el calendario ya leido (son todos los
        /// laboratorios). Si la lista aun no se lleno, se usa al llenarla. No
        /// dispara LaboratorioCambiado.
        /// </summary>
        public void ElegirLaboratorio(int idLaboratorio)
        {
            if (idLaboratorio <= 0)
            {
                return;
            }

            ultimoLaboratorio = idLaboratorio;
            for (int i = 0; i < cboLaboratorio.Items.Count; i++)
            {
                OpcionCombo opcion = cboLaboratorio.Items[i] as OpcionCombo;
                if (opcion != null && opcion.Id == idLaboratorio)
                {
                    if (cboLaboratorio.SelectedIndex != i)
                    {
                        eligiendoDesdeFuera = true;
                        try
                        {
                            cboLaboratorio.SelectedIndex = i;
                        }
                        finally
                        {
                            eligiendoDesdeFuera = false;
                        }
                    }

                    return;
                }
            }
        }

        private void AlCambiarLaboratorio()
        {
            int id = LaboratorioElegido();
            if (id != 0)
            {
                ultimoLaboratorio = id;
            }

            if (calendario != null)
            {
                Pintar();
            }

            if (id != 0 && !eligiendoDesdeFuera && iniciado && LaboratorioCambiado != null)
            {
                LaboratorioCambiado(id);
            }
        }

        /// <summary>
        /// Lee de la base las seis semanas que ensena el calendario (todos los
        /// laboratorios). Si falla deja el calendario vacio y devuelve el error,
        /// que se ensena DESPUES de pintar: mientras el aviso esta abierto la
        /// cuadricula se repinta y no puede quedarse con clases de un calendario
        /// que ya no esta.
        /// </summary>
        private string Leer()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                calendario = datos.LeerCalendario(mes.PrimerDiaVisible, mes.UltimoDiaVisible, chkContarTodos.Checked);
                mes.Hoy = calendario.Ahora.Date;
                return null;
            }
            catch (Exception ex)
            {
                // Al aviso, la causa y que hacer; el detalle tecnico, al registro.
                Log.Error("PestanaCalendario.Leer", ex);
                calendario = null;
                return ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion
                    ? "No se pudo leer el calendario: no hubo conexión con el servidor." + Environment.NewLine + Environment.NewLine
                      + "Revisa la red y pulsa Actualizar."
                    : "No se pudo leer el calendario por un error inesperado." + Environment.NewLine + Environment.NewLine
                      + "Pulsa Actualizar; si sigue fallando, " + SiSigueFallando() + ".";
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>El aviso de Leer, que ya dice la causa y que hacer.</summary>
        private static void AvisarError(string error)
        {
            if (error != null)
            {
                MessageBox.Show(error, "Faltas", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Que hacer si un fallo que no es de la red se repite: la administracion
        /// tiene el detalle en el registro de diagnostico de su equipo; la persona
        /// responsable de laboratorio (solo consulta) se lo dice a ella.
        /// </summary>
        private static string SiSigueFallando()
        {
            return Permisos.SoloConsulta
                ? "avisa a la administración de DigBit"
                : "revisa el registro de diagnóstico de este equipo, donde quedó el detalle";
        }

        /// <summary>
        /// Una InvalidOperationException de DigBit (HorariosDatos, Permisos) ya dice
        /// la causa y que hacer; una de MySql.Data (la conexion se cayo a media
        /// transaccion) es texto tecnico en ingles y va con los demas fallos.
        /// </summary>
        private static bool EsAvisoDeDigBit(Exception ex)
        {
            return string.Equals(ex.Source, typeof(PestanaCalendario).Assembly.GetName().Name, StringComparison.Ordinal);
        }

        private void Releer()
        {
            // Si la lista de laboratorios fallo la primera vez, Actualizar la vuelve a pedir.
            if (cboLaboratorio.Items.Count == 0)
            {
                LlenarLaboratorios(ultimoLaboratorio);
            }

            string error = Leer();
            Pintar();
            AvisarError(error);
        }

        /// <summary>
        /// Con las flechas del calendario: lee el mes nuevo y, si la semana elegida
        /// ya no se ve, elige la primera de ese mes con algun dia en rojo o, si no
        /// hay, su primera semana (la de su primer jueves: la del dia 1 puede ser
        /// casi toda del mes anterior).
        /// </summary>
        private void MesCambiado()
        {
            string error = Leer();
            if (mes.Semana < mes.PrimerDiaVisible || mes.Semana.AddDays(6) > mes.UltimoDiaVisible)
            {
                DateTime primera = CuadriculaSemanal.LunesDe(mes.Mes);
                if (primera.AddDays(3).Month != mes.Mes.Month)
                {
                    primera = primera.AddDays(7);
                }

                Dictionary<DateTime, ResumenDia> dias = DiasDe(LaboratorioElegido());
                List<DateTime> enRojo = dias.Where(d => d.Value.Faltan > 0 && d.Key.Month == mes.Mes.Month && d.Key.Year == mes.Mes.Year)
                    .Select(d => d.Key).ToList();
                mes.Semana = enRojo.Count > 0 ? enRojo.Min() : primera;
            }

            Pintar();
            AvisarError(error);
        }

        // =====================================================================
        // El mes del laboratorio elegido
        // =====================================================================

        /// <summary>Como le fue a cada dia, en el laboratorio elegido, de lo que se leyo.</summary>
        private Dictionary<DateTime, ResumenDia> DiasDe(int laboratorio)
        {
            Dictionary<DateTime, ResumenDia> dias = new Dictionary<DateTime, ResumenDia>();
            if (calendario == null || laboratorio == 0)
            {
                return dias;
            }

            foreach (ClaseDelCalendario clase in calendario.Clases.Where(c => c.LaboratorioId == laboratorio))
            {
                ResumenDia dia;
                if (!dias.TryGetValue(clase.Fecha, out dia))
                {
                    dia = new ResumenDia();
                    dias[clase.Fecha] = dia;
                }

                dia.Contar(clase.Estado);
            }

            return dias;
        }

        /// <summary>El color de cada dia del mes, su resumen a la izquierda y la semana elegida a la derecha.</summary>
        private void Pintar()
        {
            int laboratorio = LaboratorioElegido();
            Dictionary<DateTime, ResumenDia> dias = DiasDe(laboratorio);
            mes.Dias = dias;
            lblMes.Text = ResumenDelMes(laboratorio, dias);
            // Entero al pasar el raton, por si en una ventana baja no cabe.
            ayuda.SetToolTip(lblMes, lblMes.Text);
            if (ClientSize.Width >= 600 && ClientSize.Height >= 300)
            {
                AcomodarResumenDelMes();
            }

            ayuda.SetToolTip(lblLeyenda, AyudaDeLaLeyenda(calendario != null && calendario.ConDadas));
            leyendaMes.ConDadas = calendario != null && calendario.ConDadas;
            MostrarSemana();
        }

        /// <summary>"En octubre: 65 clases sin bitácoras en 9 días" y desde cuando cuenta el laboratorio.</summary>
        private string ResumenDelMes(int laboratorio, Dictionary<DateTime, ResumenDia> dias)
        {
            if (laboratorio == 0)
            {
                return SinLaboratorio();
            }

            if (calendario == null)
            {
                return "No se pudo leer el calendario: pulsa Actualizar.";
            }

            List<ResumenDia> delMes = dias.Where(d => d.Key.Month == mes.Mes.Month && d.Key.Year == mes.Mes.Year).Select(d => d.Value).ToList();
            int faltan = delMes.Sum(d => d.Faltan);
            int diasEnRojo = delMes.Count(d => d.Faltan > 0);
            int con = delMes.Sum(d => d.ConBitacoras);
            // Sin bitacoras pero con asistencias que dio su profesor (db/22): se dieron.
            int conDadas = delMes.Sum(d => d.SinBitacorasConAsistencia);
            int porVenir = delMes.Sum(d => d.PorVenir);
            int noCuentan = delMes.Sum(d => d.NoCuentan);
            string nombreMes = CalendarioFiltro.Meses[mes.Mes.Month].ToLowerInvariant();
            // Sin rojo ni verde: o las que ya pasaron no cuentan, o aun no pasa ninguna, o estan canceladas.
            string cuenta = delMes.Count == 0 ? "En " + nombreMes + " no hay clases en este laboratorio."
                : faltan > 0 || con > 0 || conDadas > 0
                    ? "En " + nombreMes + ": " + (faltan == 0 ? (conDadas > 0 ? "ninguna clase en rojo" : "ninguna clase sin bitácoras")
                        : faltan + (faltan == 1 ? " clase" : " clases") + " sin bitácoras en " + diasEnRojo + (diasEnRojo == 1 ? " día" : " días"))
                      + (con > 0 ? "; " + con + (con == 1 ? " clase" : " clases") + " con bitácoras" : "")
                      + (conDadas > 0 ? "; " + conDadas + (conDadas == 1 ? " clase" : " clases") + " sin bitácoras con asistencia dada" : "") + "."
                : noCuentan > 0 ? "En " + nombreMes + ", las clases que ya pasaron no cuentan (en gris)."
                : porVenir > 0 ? "En " + nombreMes + " sus clases aún no pasan."
                : "En " + nombreMes + " sus clases están canceladas.";
            return cuenta + " " + DesdeCuando(laboratorio);
        }

        /// <summary>Sin laboratorio elegido: la lista no se pudo leer, o no hay ninguno activo.</summary>
        private string SinLaboratorio()
        {
            return laboratoriosConError ? "No se pudieron leer los laboratorios: pulsa Actualizar." : "No hay laboratorios activos.";
        }

        /// <summary>Desde cuando registra el laboratorio con DigBit y que pasa con lo de antes (segun la casilla).</summary>
        private string DesdeCuando(int laboratorio)
        {
            DateTime empezo;
            if (calendario.EmpezoEl.TryGetValue(laboratorio, out empezo))
            {
                return "Registra con DigBit desde el " + empezo.ToString("dd/MM/yyyy")
                    + (calendario.ContarTodos ? "; lo de antes también se cuenta (casilla de arriba)." : "; lo de antes sale en gris.");
            }

            return calendario.ContarTodos
                ? "Aún no registra bitácoras con DigBit: se cuenta igual (casilla de arriba). Aquí el rojo no quiere decir que no se dio la clase."
                : "Aún no registra bitácoras con DigBit: sus clases salen en gris. Para verlas en rojo, marca la casilla de arriba.";
        }

        // =====================================================================
        // La semana elegida, a la derecha
        // =====================================================================

        private void MostrarSemana()
        {
            int laboratorio = LaboratorioElegido();
            DateTime lunes = mes.Semana;
            DateTime domingo = lunes.AddDays(6);
            if (calendario == null || laboratorio == 0 || lunes < calendario.Desde || domingo > calendario.Hasta)
            {
                clasesVistas = new List<ClaseDelCalendario>();
                lblTitulo.Text = laboratorio == 0 ? SinLaboratorio() : SelectorSemana.Describir(lunes);
                lblResumen.Text = calendario == null ? "No se pudo leer el calendario: pulsa Actualizar." : "";
                lnkOtras.Text = "";
                cuadricula.Ahora = calendario == null ? DateTime.Now : calendario.Ahora;
                cuadricula.Semana = lunes;
                cuadricula.Franjas = new List<FranjaInfo>();
                cuadricula.Excepciones = new List<ExcepcionInfo>();
                return;
            }

            clasesVistas = calendario.Clases
                .Where(c => c.LaboratorioId == laboratorio && c.Fecha >= lunes && c.Fecha <= domingo)
                .ToList();

            // Cada franja sale una vez en la semana (es de un dia); las extras y las
            // cancelaciones, como excepciones para que la cuadricula las dibuje igual
            // que en Horario.
            List<FranjaInfo> franjas = clasesVistas.Where(c => !c.EsExtra && c.FranjaId.HasValue)
                .GroupBy(c => c.FranjaId.Value)
                .Select(g => ComoFranja(g.First()))
                .ToList();
            List<ExcepcionInfo> excepciones = clasesVistas.Where(c => !c.EsExtra && c.Cancelada && c.FranjaId.HasValue)
                .Select(c => ComoExcepcion(c, false))
                .Concat(clasesVistas.Where(c => c.EsExtra && c.ExcepcionId.HasValue).Select(c => ComoExcepcion(c, true)))
                .ToList();

            cuadricula.Ahora = calendario.Ahora;
            cuadricula.Hoy = calendario.Ahora.Date;
            cuadricula.Semana = lunes;
            cuadricula.Franjas = franjas;
            cuadricula.Excepciones = excepciones;

            lblTitulo.Text = NombreLaboratorio(laboratorio) + " — " + SelectorSemana.Describir(lunes);
            lblResumen.Text = clasesVistas.Count == 0 ? "No hay clases de este laboratorio esa semana." : ResumenDeLaSemana(clasesVistas);

            int otras = calendario.Otras.Count(o => o.LaboratorioId == laboratorio && o.Fecha >= lunes && o.Fecha <= domingo);
            lnkOtras.Text = otras == 0 ? "" : otras == 1 ? "1 sesión fuera del horario" : otras + " sesiones fuera del horario";
        }

        private static string ResumenDeLaSemana(List<ClaseDelCalendario> clases)
        {
            int con = clases.Count(c => c.Estado == EstadoEnCalendario.ConBitacoras);
            int faltan = clases.Count(c => c.Falta);
            // Sin bitacoras pero con asistencias que dio su profesor (db/22): contaban y se dieron.
            int conDadas = clases.Count(c => c.Estado == EstadoEnCalendario.SinBitacorasConAsistencia);
            int contaban = con + faltan + conDadas;
            int canceladas = clases.Count(c => c.Estado == EstadoEnCalendario.Cancelada);
            int porVenir = clases.Count(c => c.Estado == EstadoEnCalendario.PorVenir);
            int noCuentan = clases.Count(c => c.Estado == EstadoEnCalendario.NoContaba);

            List<string> partes = new List<string>();
            if (contaban > 0 && conDadas > 0)
            {
                // "De 12 clases que ya pasaron: 9 con bitacoras, 2 sin bitacoras (en rojo) y 1 sin bitacoras con asistencia dada (en ambar)."
                // Sin rojas, sin "0 sin bitacoras": junto a las ambar se leia como una contradiccion.
                partes.Add("De " + contaban + (contaban == 1 ? " clase que ya pasó: " : " clases que ya pasaron: ")
                    + con + " con bitácoras" + (faltan > 0 ? ", " + faltan + " sin bitácoras (en rojo)" : "")
                    + " y " + conDadas + " sin bitácoras con asistencia dada (en ámbar).");
            }
            else if (contaban > 0)
            {
                partes.Add("De " + contaban + (contaban == 1 ? " clase que ya pasó: " : " clases que ya pasaron: ")
                    + con + " con bitácoras y " + faltan + " sin bitácoras" + (faltan > 0 ? " (en rojo)." : "."));
            }

            List<string> otras = new List<string>();
            if (canceladas > 0)
            {
                otras.Add(canceladas + (canceladas == 1 ? " cancelada" : " canceladas"));
            }

            if (porVenir > 0)
            {
                otras.Add(porVenir + " por venir");
            }

            if (noCuentan > 0)
            {
                otras.Add(noCuentan + " que no cuentan");
            }

            if (otras.Count > 0)
            {
                partes.Add(char.ToUpper(otras[0][0]) + string.Join(", ", otras).Substring(1) + ".");
            }

            return string.Join(" ", partes);
        }

        private string NombreLaboratorio(int id)
        {
            string nombre;
            return calendario != null && calendario.Laboratorios.TryGetValue(id, out nombre) ? nombre : "Laboratorio " + id;
        }

        /// <summary>La clase del calendario de esa franja en la semana que se ve.</summary>
        private ClaseDelCalendario ClaseDeFranja(FranjaInfo franja)
        {
            return franja == null ? null : clasesVistas.FirstOrDefault(c => !c.EsExtra && c.FranjaId == franja.Id);
        }

        private static FranjaInfo ComoFranja(ClaseDelCalendario c)
        {
            return new FranjaInfo
            {
                Id = c.FranjaId ?? 0,
                ClaseId = c.ClaseId,
                LaboratorioId = c.LaboratorioId,
                DiaSemana = ((int)c.Fecha.DayOfWeek + 6) % 7 + 1,
                Inicio = c.Inicio,
                Fin = c.Fin,
                Codigo = c.Codigo,
                ProfesorId = c.ProfesorId,
                Profesor = c.Profesor,
                ProfesorCorto = c.ProfesorCorto,
                Grupo = c.Grupo,
                Materia = c.Materia,
                VigenteDesde = c.Fecha,
                VigenteHasta = c.Fecha,
                Activa = true
            };
        }

        private static ExcepcionInfo ComoExcepcion(ClaseDelCalendario c, bool extra)
        {
            return new ExcepcionInfo
            {
                Id = extra ? c.ExcepcionId.Value : 0,
                Tipo = extra ? "extra" : "cancelada",
                Fecha = c.Fecha,
                FranjaId = extra ? (int?)null : c.FranjaId,
                ClaseId = c.ClaseId,
                LaboratorioId = c.LaboratorioId,
                Inicio = c.Inicio,
                Fin = c.Fin,
                Motivo = c.Motivo ?? "",
                Codigo = c.Codigo,
                ProfesorId = c.ProfesorId,
                Profesor = c.Profesor,
                ProfesorCorto = c.ProfesorCorto,
                Grupo = c.Grupo,
                Materia = c.Materia,
                CreadaEn = c.CreadaEn
            };
        }

        private AspectoEnCalendario AspectoDeFranja(FranjaInfo franja, DateTime fecha)
        {
            ClaseDelCalendario clase = clasesVistas.FirstOrDefault(c => !c.EsExtra && c.FranjaId == franja.Id && c.Fecha == fecha.Date);
            return clase == null ? new AspectoEnCalendario { Fondo = GrisNoCuenta, Texto = TextoGris, Pie = "No tocaba" } : Aspecto(clase);
        }

        private AspectoEnCalendario AspectoDeExtra(ExcepcionInfo extra)
        {
            ClaseDelCalendario clase = clasesVistas.FirstOrDefault(c => c.EsExtra && c.ExcepcionId == extra.Id);
            return clase == null ? null : Aspecto(clase);
        }

        /// <summary>Blanco con bitacoras, rojo si faltan, gris si aun no pasa o no cuenta; y la linea de estado.</summary>
        private AspectoEnCalendario Aspecto(ClaseDelCalendario c)
        {
            switch (c.Estado)
            {
                case EstadoEnCalendario.ConBitacoras:
                    return new AspectoEnCalendario { Pie = TextoBitacoras(c) };
                case EstadoEnCalendario.SinSesion:
                    return new AspectoEnCalendario { Fondo = RojoFalta, Texto = Color.White, Pie = "Sin bitácoras" };
                case EstadoEnCalendario.SesionVacia:
                    return new AspectoEnCalendario { Fondo = RojoFalta, Texto = Color.White, Pie = "Sin bitácoras (se abrió su sesión)" };
                case EstadoEnCalendario.SinBitacorasConAsistencia:
                    // Se dio: nadie registro, pero su profesor dio la asistencia a quien no pudo (db/22).
                    return new AspectoEnCalendario
                    {
                        Fondo = AmbarDadas,
                        Texto = TextoAmbarDadas,
                        Pie = "Sin bitácoras: " + AsistenciasDadasTexto(c.AsistenciasDadas)
                    };
                case EstadoEnCalendario.PorVenir:
                    // Gris claro, como en el calendario del mes; el reloj, el de la ultima lectura.
                    DateTime ahora = calendario == null ? DateTime.Now : calendario.Ahora;
                    bool enCurso = c.Fecha == ahora.Date && c.Inicio <= ahora.TimeOfDay;
                    return new AspectoEnCalendario
                    {
                        Fondo = Color.FromArgb(247, 247, 247),
                        Texto = TextoGris,
                        Pie = enCurso ? "En curso" + (c.Bitacoras > 0 ? ": " + Alumnos(c.Bitacoras) : "") : "Por venir"
                    };
                case EstadoEnCalendario.Cancelada:
                    return new AspectoEnCalendario { Texto = TextoGris, Pie = c.Bitacoras > 0 ? "Con " + Alumnos(c.Bitacoras) : "" };
                default:
                    return new AspectoEnCalendario { Fondo = GrisNoCuenta, Texto = TextoGris, Pie = "No cuenta: " + PorQueNoCuenta(c) };
            }
        }

        internal static string Alumnos(int cuantos)
        {
            return cuantos + (cuantos == 1 ? " bitácora" : " bitácoras");
        }

        /// <summary>"1 asistencia dada", "2 asistencias dadas" (las que dio el profesor sin bitacora, db/22).</summary>
        internal static string AsistenciasDadasTexto(int cuantas)
        {
            return cuantas + (cuantas == 1 ? " asistencia dada" : " asistencias dadas");
        }

        private static string TextoBitacoras(ClaseDelCalendario c)
        {
            if (c.Bitacoras > 0 || c.OtraClase == null)
            {
                return Alumnos(c.Bitacoras);
            }

            // Sus alumnos quedaron en otra sesion (ver ClaseDelCalendario.BitacorasEnOtra).
            return c.OtraClase.ClaseId == c.ClaseId
                ? Alumnos(c.BitacorasEnOtra) + " (en su sesión antes del cambio)"
                : Alumnos(c.BitacorasEnOtra) + " (llegaron antes: en la sesión de las " + HorariosDatos.Hora(c.OtraClase.Inicio) + ")";
        }

        /// <summary>
        /// Por que una clase no cuenta, en el mismo orden que HorariosDatos.EstadoDe:
        /// si no se cuentan todos, lo de antes de que el laboratorio usara DigBit;
        /// si no, la extra puesta despues de su hora o el profesor de baja.
        /// </summary>
        private string PorQueNoCuenta(ClaseDelCalendario c)
        {
            if (calendario == null)
            {
                return "sin datos";
            }

            DateTime empezo;
            if (!calendario.ContarTodos && !calendario.EmpezoEl.TryGetValue(c.LaboratorioId, out empezo))
            {
                return "el laboratorio aún no usa DigBit";
            }

            if (!calendario.ContarTodos && calendario.EmpezoEl.TryGetValue(c.LaboratorioId, out empezo) && c.Fecha < empezo)
            {
                return "antes de usar DigBit";
            }

            if (c.EsExtra && !c.EsResto && c.CreadaEn.HasValue && c.CreadaEn.Value >= c.Fecha + c.Fin)
            {
                return "se puso después de su hora";
            }

            return c.EsExtra ? "clase inactiva o docente de baja" : "docente de baja";
        }

        // =====================================================================
        // Clic en una clase
        // =====================================================================

        private void MostrarMenu(ClaseDelCalendario clase)
        {
            claseEnMenu = clase;
            if (clase == null)
            {
                return;
            }

            ClaseDelCalendario conSesion = SesionQueAbrir(clase);
            menuClase.Items[0].Text = conSesion == null
                    ? (clase.SesionSinEnlace ? "Su sesión no está enlazada a la clase: búscala en Bitácoras"
                        : clase.Estado == EstadoEnCalendario.PorVenir ? "Todavía nadie ha registrado"
                        : "Nadie abrió su sesión")
                : conSesion == clase ? "Ver quién registró (" + Alumnos(clase.Bitacoras)
                    + (clase.AsistenciasDadas > 0 ? "; " + AsistenciasDadasTexto(clase.AsistenciasDadas) : "") + ")..."
                : "Ver quién registró (en la sesión de las " + HorariosDatos.Hora(conSesion.Inicio) + ")...";
            menuClase.Items[0].Enabled = conSesion != null;
            menuClase.Items[1].Text = clase.EsExtra ? "Quitar esta sesión extra (no se dio)..." : "Cancelar esta clase ese día (no se dio)...";
            // Solo lo que salio en rojo: lo demas ya tiene bitacoras, no ha pasado o
            // ya esta cancelado. Tampoco la ambar (db/22): su profesor dio asistencias,
            // la clase se dio y su lista cuenta ese dia. En solo consulta nunca:
            // escribe. Va aqui porque el menu se rehace en cada clic.
            menuClase.Items[1].Visible = clase.Falta && !Permisos.SoloConsulta;
            menuClase.Show(Cursor.Position);
        }

        /// <summary>
        /// La clase cuya sesion se abre para ver quien registro: la suya; o, si sus
        /// alumnos quedaron en otra (BitacorasEnOtra), esa. Null si no hay ninguna
        /// que BitacoraSesion sepa abrir (por franja y fecha, o por extra).
        /// </summary>
        private static ClaseDelCalendario SesionQueAbrir(ClaseDelCalendario clase)
        {
            if (clase.SesionId.HasValue && !clase.SesionSinEnlace && (clase.Bitacoras > 0 || clase.OtraClase == null))
            {
                return clase;
            }

            ClaseDelCalendario otra = clase.OtraClase;
            return otra != null && otra.SesionId.HasValue && !otra.SesionSinEnlace ? otra : null;
        }

        private void VerBitacoras()
        {
            ClaseDelCalendario c = claseEnMenu == null ? null : SesionQueAbrir(claseEnMenu);
            if (c == null)
            {
                return;
            }

            string horario = HorariosDatos.NombresDia[((int)c.Fecha.DayOfWeek + 6) % 7 + 1] + " " + c.Fecha.ToString("dd/MM/yyyy") + ", "
                + HorariosDatos.Hora(c.Inicio) + " a " + HorariosDatos.Hora(c.Fin) + ", " + NombreLaboratorio(c.LaboratorioId)
                + (c.EsExtra ? " (sesión extra)" : "");
            using (BitacoraSesion bitacora = new BitacoraSesion(c.EsExtra ? (int?)null : c.FranjaId, c.EsExtra ? c.ExcepcionId : null,
                c.Materia + ", " + c.Grupo, horario, c.Profesor, c.Fecha))
            {
                bitacora.ShowDialog(FindForm());
            }
        }

        /// <summary>
        /// Una clase en rojo que no se dio por un motivo (festivo, el profesor
        /// aviso): se cancela ese dia y deja de contar. Una extra que no se dio,
        /// se quita.
        /// </summary>
        private void MarcarNoDada()
        {
            ClaseDelCalendario c = claseEnMenu;
            if (MarcarNoDada(FindForm(), datos, c != null && c.Falta ? c : null, idAdministrador == null ? 0 : idAdministrador(), "Faltas"))
            {
                Releer();
            }
        }

        /// <summary>
        /// Marcar como no dada una clase que ya paso sin bitacoras, igual desde
        /// Faltas que desde una alerta "Nadie registro bitacora" (VentanaAlertas):
        /// una franja se cancela ese dia con su motivo (DialogoMotivo) y deja de
        /// contar como falta; una sesion extra que no se dio, se quita. Quien llama
        /// decide que es una falta. True si se cambio (hay que volver a leer);
        /// false si se cancelo, si no se pudo (ya lo dijo) o en solo consulta.
        /// </summary>
        /// <param name="desde">Donde se pidio, para el log ("Faltas", "Alertas").</param>
        internal static bool MarcarNoDada(IWin32Window dueno, HorariosDatos datos, ClaseDelCalendario c, int idAdministrador, string desde)
        {
            if (Permisos.Bloquear(dueno, "Marcar una clase como no dada (" + desde + ")")) return false;
            if (c == null || (c.EsExtra ? !c.ExcepcionId.HasValue : !c.FranjaId.HasValue))
            {
                return false;
            }

            string descripcion = c.Materia + " - " + c.Grupo + " (" + c.Profesor + "), " + HorariosDatos.Hora(c.Inicio) + " a " + HorariosDatos.Hora(c.Fin)
                + " del " + c.Fecha.ToString("dd/MM/yyyy");
            try
            {
                if (c.EsExtra)
                {
                    if (MessageBox.Show(dueno, "¿Quitar la sesión extra de " + descripcion + "? Nadie registró bitácora en ella.", "Quitar sesión extra",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        return false;
                    }

                    datos.QuitarExtra(c.ExcepcionId.Value, false);
                }
                else
                {
                    using (DialogoMotivo dialogo = DialogoMotivo.CancelarLaClase("Cancelar la clase ese día",
                        "Se cancela " + descripcion + " porque no se dio. Deja de contar como falta (en Faltas y en Alertas); el motivo queda en Cambios de un día.",
                        "Motivo (por ejemplo: día festivo)"))
                    {
                        if (dialogo.ShowDialog(dueno) != DialogResult.OK)
                        {
                            return false;
                        }

                        datos.CancelarFranja(c.FranjaId.Value, c.Fecha, dialogo.Motivo, idAdministrador);
                    }
                }
            }
            catch (InvalidOperationException ex) when (EsAvisoDeDigBit(ex))
            {
                // Ya cancelada ese dia, la extra ya tiene bitacoras...: ya dice que pasa.
                MessageBox.Show(dueno, ex.Message, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro.
                Log.Error("Marcar como no dada (" + desde + ")", ex);
                string que = c.EsExtra ? "No se pudo quitar la sesión extra" : "No se pudo marcar la clase como no dada";
                MessageBox.Show(dueno, ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion
                        ? que + ": no hubo conexión con el servidor." + Environment.NewLine + Environment.NewLine + "Revisa la red y vuelve a intentarlo."
                        : que + ": el servidor no aceptó el cambio." + Environment.NewLine + Environment.NewLine
                          + "Vuelve a intentarlo; si sigue fallando, " + SiSigueFallando() + ".",
                    "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        /// <summary>Las sesiones de la semana que no son de ninguna clase del horario de ese laboratorio.</summary>
        private void MostrarOtras()
        {
            int laboratorio = LaboratorioElegido();
            if (calendario == null || laboratorio == 0)
            {
                return;
            }

            DateTime lunes = mes.Semana;
            DateTime domingo = lunes.AddDays(6);
            List<string> lineas = calendario.Otras
                .Where(o => o.LaboratorioId == laboratorio && o.Fecha >= lunes && o.Fecha <= domingo)
                .Select(o => HorariosDatos.NombresDia[((int)o.Fecha.DayOfWeek + 6) % 7 + 1] + " " + o.Fecha.ToString("dd/MM")
                    + (o.Entrada.HasValue ? ", " + HorariosDatos.Hora(o.Entrada.Value) + (o.Salida.HasValue ? "-" + HorariosDatos.Hora(o.Salida.Value) : "") : "")
                    + (string.IsNullOrWhiteSpace(o.Profesor) ? "" : ", " + o.Profesor) + ": " + Alumnos(o.Bitacoras))
                .ToList();
            MessageBox.Show("Sesiones de esa semana en " + NombreLaboratorio(laboratorio) + " que no son de ninguna clase de su horario "
                + "(una clase que se cambió o se borró, o del sistema anterior). No cuentan para el calendario:" + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, lineas), "Sesiones fuera del horario", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // =====================================================================
        // No tomar en cuenta y Volver a contar (solo la administracion)
        // =====================================================================

        /// <summary>
        /// "No tomar en cuenta...": el dialogo con el laboratorio elegido y, por
        /// omision, el mes que se ve hasta hoy. Si se aplico, Faltas se vuelve a
        /// leer y se dice que se hizo y como deshacerlo.
        /// </summary>
        private void NoTomarEnCuenta()
        {
            if (Permisos.Bloquear(FindForm(), "No tomar en cuenta (Faltas)"))
            {
                return;
            }

            List<OpcionCombo> laboratorios = cboLaboratorio.Items.OfType<OpcionCombo>().ToList();
            if (laboratorios.Count == 0)
            {
                MessageBox.Show(FindForm(), SinLaboratorio(), "No tomar en cuenta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DateTime hoy = calendario == null ? mes.Hoy : calendario.Ahora.Date;
            DateTime finDelMes = mes.Mes.AddMonths(1).AddDays(-1);
            DateTime hasta = finDelMes < hoy ? finDelMes : hoy;
            DateTime desde = mes.Mes < hasta ? mes.Mes : hasta;
            using (DialogoNoTomarEnCuenta dialogo = new DialogoNoTomarEnCuenta(datos, laboratorios, LaboratorioElegido(), desde, hasta, hoy,
                chkContarTodos.Checked, idAdministrador == null ? 0 : idAdministrador()))
            {
                if (dialogo.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    Releer();
                    MessageBox.Show(FindForm(), dialogo.Resumen, "No tomar en cuenta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        /// <summary>"Volver a contar...": cada "No tomar en cuenta" que sigue puesto, para deshacer uno.</summary>
        private void VolverAContar()
        {
            if (Permisos.Bloquear(FindForm(), "Volver a contar (Faltas)"))
            {
                return;
            }

            using (DialogoVolverAContar dialogo = new DialogoVolverAContar(datos))
            {
                dialogo.ShowDialog(FindForm());
                if (dialogo.HuboCambios)
                {
                    Releer();
                }
            }
        }

        /// <summary>"lun 19/10/2026": el dia de una fila de los dialogos.</summary>
        private static string DiaCorto(DateTime fecha)
        {
            return HorariosDatos.NombresDia[((int)fecha.DayOfWeek + 6) % 7 + 1].Substring(0, 3).ToLowerInvariant() + " " + HistorialCambios.Fecha(fecha);
        }

        /// <summary>"el 19/10/2026" o "del 01/10/2026 al 23/10/2026".</summary>
        private static string Fechas(DateTime desde, DateTime hasta)
        {
            return desde == hasta ? "el " + HistorialCambios.Fecha(desde) : "del " + HistorialCambios.Fecha(desde) + " al " + HistorialCambios.Fecha(hasta);
        }

        /// <summary>Un fallo al escribir que no es un aviso de DigBit: la causa (nada cambio: es una transaccion) y que hacer.</summary>
        private static string NoSePudo(string que, Exception ex)
        {
            return ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion
                ? que + ": no hubo conexión con el servidor. No se cambió nada." + Environment.NewLine + Environment.NewLine
                  + "Revisa la red y vuelve a intentarlo."
                : que + ": el servidor no aceptó el cambio. No se cambió nada." + Environment.NewLine + Environment.NewLine
                  + "Vuelve a intentarlo; si sigue fallando, " + SiSigueFallando() + ".";
        }

        /// <summary>El boton principal apagado se ve apagado (gris verdoso), no verde con la letra gris.</summary>
        private static void AtenuarAlApagar(Button boton)
        {
            EventHandler pintar = (s, e) => boton.BackColor = boton.Enabled ? Color.DarkGreen : Color.FromArgb(200, 212, 200);
            boton.EnabledChanged += pintar;
            pintar(boton, EventArgs.Empty);
        }

        /// <summary>Un texto gris o rojo encima de una lista vacia: no hay nada, o no se pudo leer.</summary>
        private static Label TextoSobreLista(DataGridView lista)
        {
            return new Label
            {
                AutoSize = false,
                BackColor = Color.White,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = Color.FromArgb(110, 110, 110),
                Location = new Point(lista.Left + 2, lista.Top + 34),
                Size = new Size(lista.Width - 4, lista.Height - 36),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(24, 0, 24, 0),
                Visible = false
            };
        }

        /// <summary>
        /// "No tomar en cuenta": el laboratorio (o todos los de Faltas), las fechas
        /// (hasta hoy) y el motivo, con la vista previa de las clases en rojo que
        /// dejan de contar: las mismas que Faltas (LeerCalendario con su casilla y
        /// HorariosDatos.EntraEnNoTomar). Se manda lo que se ve: si algo cambio y no
        /// se ha vuelto a leer, Aceptar primero lee. HorariosDatos.NoTomarEnCuenta
        /// las cancela en una transaccion; Resumen dice lo que se hizo.
        /// </summary>
        private sealed class DialogoNoTomarEnCuenta : Form
        {
            private const int Ancho = 800;
            private readonly HorariosDatos datos;
            private readonly List<OpcionCombo> laboratorios;
            private readonly bool contarTodos;
            private readonly int idAdministrador;
            private readonly ComboBox cboLab;
            private readonly DateTimePicker dtpDesde;
            private readonly DateTimePicker dtpHasta;
            private readonly TextBox txtMotivo;
            private readonly Label lblVista;
            private readonly LinkLabel lnkLeer;
            private readonly DataGridView dgvClases;
            private readonly Label lblVacia;
            private readonly Label lblNota;
            private readonly Label lblError;
            private readonly Button btnAceptar;
            private readonly Timer espera = new Timer { Interval = 350 };
            // Lo de la ultima lectura: lo que se ve en la lista es lo que se manda.
            private List<ClaseDelCalendario> enRojo = new List<ClaseDelCalendario>();
            private Dictionary<int, string> nombres = new Dictionary<int, string>();
            private DateTime leidoDesde;
            private DateTime leidoHasta;
            private int leidoLab = -1;
            private string leidoDonde = "";

            /// <summary>Lo que se hizo y como deshacerlo, para el aviso de Faltas (con OK).</summary>
            public string Resumen { get; private set; }

            public DialogoNoTomarEnCuenta(HorariosDatos datos, List<OpcionCombo> laboratorios, int laboratorio, DateTime desde, DateTime hasta,
                DateTime hoy, bool contarTodos, int idAdministrador)
            {
                this.datos = datos;
                this.laboratorios = laboratorios;
                this.contarTodos = contarTodos;
                this.idAdministrador = idAdministrador;
                const string Titulo = "No tomar en cuenta";

                Label lblDescripcion = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9.5F),
                    ForeColor = Color.FromArgb(50, 50, 50),
                    Location = new Point(24, 54),
                    Size = new Size(Ancho - 48, 20),
                    Text = "Las clases que ya pasaron sin bitácoras (en rojo) en esas fechas dejan de contar como faltas en Faltas, Alertas y "
                        + "Estadísticas: cada una se cancela ese día con el motivo. Las que tienen bitácoras o asistencia dada no se tocan. "
                        + "No se borra nada y se puede deshacer con «Volver a contar...»."
                };
                lblDescripcion.Height = TextRenderer.MeasureText(lblDescripcion.Text, lblDescripcion.Font, new Size(lblDescripcion.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 4;

                int y = lblDescripcion.Bottom + 14;
                Label lblLab = EstiloHorarios.CrearEtiqueta("Laboratorio", new Point(24, y + 4));
                cboLab = EstiloHorarios.CrearCombo(new Point(130, y), 340);
                cboLab.MaxDropDownItems = 16;
                cboLab.Items.Add(new OpcionCombo { Id = 0, Texto = "Todos los laboratorios" });
                foreach (OpcionCombo opcion in laboratorios)
                {
                    cboLab.Items.Add(opcion);
                }

                int indice = laboratorios.FindIndex(l => l.Id == laboratorio);
                cboLab.SelectedIndex = indice >= 0 ? indice + 1 : 0;

                y += 38;
                Label lblDel = EstiloHorarios.CrearEtiqueta("Del", new Point(24, y + 4));
                dtpDesde = EstiloHorarios.CrearFechaConDia(this, new Point(130, y));
                Label lblAl = EstiloHorarios.CrearEtiqueta("al", new Point(372, y + 4));
                dtpHasta = EstiloHorarios.CrearFechaConDia(this, new Point(404, y));
                // Solo lo que ya paso: lo de despues de hoy no es una falta.
                dtpDesde.MaxDate = hoy.Date;
                dtpHasta.MaxDate = hoy.Date;
                dtpDesde.Value = desde.Date > hoy.Date ? hoy.Date : desde.Date;
                dtpHasta.Value = hasta.Date > hoy.Date ? hoy.Date : hasta.Date;

                y += 38;
                Label lblMotivo = EstiloHorarios.CrearEtiqueta("Motivo", new Point(24, y + 4));
                txtMotivo = EstiloHorarios.CrearTexto(new Point(130, y), 440);
                txtMotivo.MaxLength = HorariosDatos.LargoMotivoNoTomar;
                txtMotivo.Text = "Cambio de horario";

                y += 44;
                lblVista = new Label
                {
                    AutoSize = false,
                    AutoEllipsis = true,
                    Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(40, 40, 40),
                    Location = new Point(24, y),
                    Size = new Size(Ancho - 48 - 130, 22)
                };
                lnkLeer = new LinkLabel
                {
                    AutoSize = false,
                    TextAlign = ContentAlignment.TopRight,
                    Font = new DrawingFont("Century Gothic", 9F),
                    LinkColor = Color.DarkGreen,
                    Location = new Point(Ancho - 24 - 120, y + 2),
                    Size = new Size(120, 20),
                    Text = "Volver a leer",
                    Visible = false
                };
                lnkLeer.LinkClicked += (s, e) => Leer();

                y += 26;
                dgvClases = EstiloHorarios.CrearGrid(new Point(24, y), new Size(Ancho - 48, 220));
                dgvClases.Columns.Add("Dia", "Día");
                dgvClases.Columns.Add("Horas", "Horas");
                dgvClases.Columns.Add("Laboratorio", "Laboratorio");
                dgvClases.Columns.Add("Clase", "Clase");
                dgvClases.Columns.Add("Docente", "Docente");
                EstiloHorarios.Pesos(dgvClases, 16, 16, 26, 54, 28);
                lblVacia = TextoSobreLista(dgvClases);

                y = dgvClases.Bottom + 6;
                lblNota = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9F),
                    ForeColor = Color.FromArgb(150, 85, 0),
                    Location = new Point(24, y),
                    Size = new Size(Ancho - 48, 34)
                };

                y += 38;
                lblError = EstiloAdmin.LineaError(new Point(24, y), Ancho - 48, 20);
                y += 24;

                EstiloHorarios.PrepararDialogo(this, Titulo, new Size(Ancho, y + 8 + 40 + 16));
                Button btnCerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(Ancho - 24 - 120, ClientSize.Height - 56), new Size(120, 40));
                btnCerrar.DialogResult = DialogResult.Cancel;
                int anchoAceptar = TextRenderer.MeasureText(Titulo, btnCerrar.Font).Width + 34;
                btnAceptar = EstiloAdmin.BotonPrincipal(Titulo, new Point(btnCerrar.Left - 10 - anchoAceptar, btnCerrar.Top), new Size(anchoAceptar, 40));
                btnAceptar.Enabled = false;
                AtenuarAlApagar(btnAceptar);
                btnAceptar.Click += (s, e) => Aceptar();
                // Sin AcceptButton: un Enter en el motivo no cancela de golpe todas esas clases.
                CancelButton = btnCerrar;

                Controls.Add(EstiloAdmin.TituloDialogo(Titulo, Ancho - 48));
                Controls.Add(lblDescripcion);
                Controls.Add(lblLab);
                Controls.Add(cboLab);
                Controls.Add(lblDel);
                Controls.Add(lblAl);
                Controls.Add(lblMotivo);
                Controls.Add(txtMotivo);
                Controls.Add(lblVista);
                Controls.Add(lnkLeer);
                Controls.Add(lblVacia);
                Controls.Add(dgvClases);
                Controls.Add(lblNota);
                Controls.Add(lblError);
                Controls.Add(btnAceptar);
                Controls.Add(btnCerrar);
                lblVacia.BringToFront();
                ActiveControl = cboLab;

                // Cada cambio vuelve a leer la vista previa, un momento despues (al
                // pasar las fechas con las flechas no lee en cada dia).
                EventHandler cambio = (s, e) =>
                {
                    espera.Stop();
                    espera.Start();
                };
                cboLab.SelectedIndexChanged += cambio;
                dtpDesde.ValueChanged += cambio;
                dtpHasta.ValueChanged += cambio;
                espera.Tick += (s, e) => Leer();
                Shown += (s, e) => Leer();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    espera.Dispose();
                }

                base.Dispose(disposing);
            }

            private int LaboratorioElegido()
            {
                OpcionCombo opcion = cboLab.SelectedItem as OpcionCombo;
                return opcion == null ? 0 : opcion.Id;
            }

            private string NombreDe(int laboratorio)
            {
                string nombre;
                return nombres.TryGetValue(laboratorio, out nombre) ? nombre : "Laboratorio " + laboratorio;
            }

            /// <summary>Lee esas fechas como Faltas y ensena las clases en rojo del laboratorio (o de todos).</summary>
            private void Leer()
            {
                espera.Stop();
                lnkLeer.Visible = false;
                lblError.Text = "";
                DateTime desde = dtpDesde.Value.Date;
                DateTime hasta = dtpHasta.Value.Date;
                int laboratorio = LaboratorioElegido();
                if (desde > hasta)
                {
                    Mostrar(null, 0, "La fecha «Del» tiene que ser la misma que «al» o anterior.");
                    return;
                }

                if ((hasta - desde).TotalDays >= HorariosDatos.DiasMaximosCalendario)
                {
                    Mostrar(null, 0, "Elige como mucho " + HorariosDatos.DiasMaximosCalendario + " días (un poco más de un año). "
                        + "Si hace falta más, hazlo en varias veces.");
                    return;
                }

                Cursor = Cursors.WaitCursor;
                try
                {
                    CalendarioBitacoras leido = datos.LeerCalendario(desde, hasta, contarTodos);
                    HashSet<int> deAqui = new HashSet<int>(laboratorio != 0 ? new[] { laboratorio } : laboratorios.Select(l => l.Id).ToArray());
                    List<ClaseDelCalendario> delRango = leido.Clases
                        .Where(c => deAqui.Contains(c.LaboratorioId) && c.Fecha >= desde && c.Fecha <= hasta).ToList();
                    nombres = leido.Laboratorios;
                    leidoDesde = desde;
                    leidoHasta = hasta;
                    leidoLab = laboratorio;
                    leidoDonde = laboratorio != 0 ? cboLab.Text : "todos los laboratorios";
                    Mostrar(delRango.Where(HorariosDatos.EntraEnNoTomar).OrderBy(c => c.Fecha).ThenBy(c => c.Inicio).ThenBy(c => NombreDe(c.LaboratorioId)).ToList(),
                        delRango.Count(c => c.EsExtra && c.Falta), null);
                }
                catch (Exception ex)
                {
                    Log.Error("No tomar en cuenta: vista previa", ex);
                    Mostrar(null, 0, ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion
                        ? "No se pudo leer la vista previa: no hubo conexión con el servidor. Revisa la red y pulsa «Volver a leer»."
                        : "No se pudo leer la vista previa por un error inesperado. Pulsa «Volver a leer»; si sigue fallando, " + SiSigueFallando() + ".");
                    lnkLeer.Visible = true;
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }

            /// <summary>La lista y lo que se dice de ella. Con problema (fechas que no valen, no se pudo leer), vacia y sin poder aceptar.</summary>
            private void Mostrar(List<ClaseDelCalendario> clases, int extras, string problema)
            {
                if (problema != null)
                {
                    leidoLab = -1;
                    clases = new List<ClaseDelCalendario>();
                }

                enRojo = clases;
                dgvClases.Rows.Clear();
                foreach (ClaseDelCalendario c in clases)
                {
                    dgvClases.Rows.Add(DiaCorto(c.Fecha), HistorialCambios.Horas(c.Inicio, c.Fin), NombreDe(c.LaboratorioId),
                        (c.Materia ?? "") + " - " + (c.Grupo ?? ""), c.Profesor ?? "");
                }

                // Solo para ver: sin una fila elegida que parezca que se puede escoger.
                dgvClases.ClearSelection();

                dgvClases.Columns["Laboratorio"].Visible = leidoLab == 0;
                lblVista.Text = problema != null ? "Sin vista previa."
                    : clases.Count == 0 ? "Ninguna clase dejará de contar."
                    : clases.Count == 1 ? "1 clase sin bitácoras dejará de contar como falta:"
                    : clases.Count + " clases sin bitácoras dejarán de contar como faltas:";
                lblVacia.ForeColor = problema != null ? Color.FromArgb(190, 30, 30) : Color.FromArgb(110, 110, 110);
                lblVacia.Text = problema ?? (clases.Count == 0 ? "En esas fechas no hay clases sin bitácoras en " + leidoDonde + ": no hay nada que deje de contar." : "");
                lblVacia.Visible = problema != null || clases.Count == 0;

                List<string> notas = new List<string>();
                if (extras > 0)
                {
                    notas.Add(extras == 1
                        ? "Hay además 1 sesión extra sin bitácoras: sigue contando, porque quitarla aquí la borraría. Si no se dio, quítala en su semana de Faltas (clic en ella)."
                        : "Hay además " + extras + " sesiones extra sin bitácoras: siguen contando, porque quitarlas aquí las borraría. "
                          + "Si no se dieron, quítalas en su semana de Faltas (clic en cada una).");
                }

                if (contarTodos && problema == null)
                {
                    notas.Add("Cuenta como Faltas ahora, con «Contar también los laboratorios que aún no usan DigBit» marcada.");
                }

                lblNota.Text = string.Join(" ", notas);
                btnAceptar.Enabled = clases.Count > 0;
            }

            private void Aceptar()
            {
                lblError.Text = "";
                // Se manda lo que se ve: si algo cambio y aun no se leyo, primero la vista previa.
                if (espera.Enabled || leidoLab != LaboratorioElegido() || leidoDesde != dtpDesde.Value.Date || leidoHasta != dtpHasta.Value.Date)
                {
                    Leer();
                    return;
                }

                string motivo = txtMotivo.Text.Trim();
                if (motivo.Length == 0)
                {
                    lblError.Text = "Escribe el motivo (por ejemplo: cambio de horario): queda en cada clase que deja de contar.";
                    txtMotivo.Focus();
                    return;
                }

                if (enRojo.Count == 0)
                {
                    return;
                }

                Cursor = Cursors.WaitCursor;
                try
                {
                    ResultadoNoTomar hecho = datos.NoTomarEnCuenta(enRojo, leidoDesde, leidoHasta, contarTodos, motivo, idAdministrador);
                    Resumen = "Listo: " + (hecho.Canceladas == 1 ? "1 clase sin bitácoras ya no cuenta como falta" : hecho.Canceladas + " clases sin bitácoras ya no cuentan como faltas")
                        + " (" + Fechas(leidoDesde, leidoHasta) + ", " + leidoDonde + "). "
                        + (hecho.Canceladas == 1 ? "Quedó cancelada ese día" : "Quedaron canceladas esos días") + " con el motivo «" + motivo + "»."
                        + (hecho.YaNoEnRojo == 0 ? ""
                            : hecho.YaNoEnRojo == 1 ? " 1 de la lista ya no estaba en rojo (alguien registró o ya estaba cancelada) y no se tocó."
                            : " " + hecho.YaNoEnRojo + " de la lista ya no estaban en rojo (alguien registró o ya estaban canceladas) y no se tocaron.")
                        + Environment.NewLine + Environment.NewLine + "Para deshacerlo, pulsa «Volver a contar...» en Faltas.";
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (InvalidOperationException ex) when (EsAvisoDeDigBit(ex))
                {
                    // Ya no queda ninguna en rojo, otra persona las cancelo, solo consulta...: ya dice que pasa.
                    Cursor = Cursors.Default;
                    MessageBox.Show(this, ex.Message, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Leer();
                }
                catch (Exception ex)
                {
                    Log.Error("No tomar en cuenta", ex);
                    Cursor = Cursors.Default;
                    MessageBox.Show(this, NoSePudo("No se pudo dejar de contar esas clases", ex), "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        /// <summary>
        /// "Volver a contar": cada "No tomar en cuenta" que sigue puesto
        /// (HorariosDatos.LeerLotesSinContar). Al elegir uno, VolverAContar quita
        /// solo sus cancelaciones y sus clases vuelven a contar en Faltas.
        /// </summary>
        private sealed class DialogoVolverAContar : Form
        {
            private const int Ancho = 940;
            private const string TituloVentana = "Volver a contar";
            private readonly HorariosDatos datos;
            private readonly DataGridView dgvLotes;
            private readonly Label lblVacia;
            private readonly Label lblEstado;
            private readonly Button btnVolver;

            /// <summary>Se volvio a contar alguno: Faltas tiene que volver a leer.</summary>
            public bool HuboCambios { get; private set; }

            public DialogoVolverAContar(HorariosDatos datos)
            {
                this.datos = datos;
                const string Titulo = TituloVentana;

                Label lblDescripcion = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9.5F),
                    ForeColor = Color.FromArgb(50, 50, 50),
                    Location = new Point(24, 54),
                    Size = new Size(Ancho - 48, 20),
                    Text = "Cada vez que se usó «No tomar en cuenta» y sigue puesto. Al volver a contar uno se quitan solo las cancelaciones "
                        + "que se pusieron esa vez: sus clases vuelven a salir en Faltas como antes (en rojo las que siguen sin bitácoras)."
                };
                lblDescripcion.Height = TextRenderer.MeasureText(lblDescripcion.Text, lblDescripcion.Font, new Size(lblDescripcion.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 4;

                int y = lblDescripcion.Bottom + 12;
                dgvLotes = EstiloHorarios.CrearGrid(new Point(24, y), new Size(Ancho - 48, 240));
                dgvLotes.Columns.Add("SePuso", "Se puso");
                dgvLotes.Columns.Add("Motivo", "Motivo");
                dgvLotes.Columns.Add("Clases", "Clases");
                dgvLotes.Columns.Add("Fechas", "Fechas");
                dgvLotes.Columns.Add("Laboratorio", "Laboratorio");
                dgvLotes.Columns.Add("Quien", "Quién");
                EstiloHorarios.Pesos(dgvLotes, 20, 30, 9, 30, 28, 28);
                dgvLotes.Columns["Clases"].MinimumWidth = 72;
                lblVacia = TextoSobreLista(dgvLotes);

                y = dgvLotes.Bottom + 8;
                lblEstado = new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                    Location = new Point(24, y),
                    Size = new Size(Ancho - 48, 40)
                };
                y += 44;

                EstiloHorarios.PrepararDialogo(this, Titulo, new Size(Ancho, y + 8 + 40 + 16));
                Button btnCerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(Ancho - 24 - 120, ClientSize.Height - 56), new Size(120, 40));
                btnCerrar.DialogResult = DialogResult.Cancel;
                int anchoVolver = TextRenderer.MeasureText(Titulo, btnCerrar.Font).Width + 34;
                btnVolver = EstiloAdmin.BotonPrincipal(Titulo, new Point(btnCerrar.Left - 10 - anchoVolver, btnCerrar.Top), new Size(anchoVolver, 40));
                btnVolver.Enabled = false;
                AtenuarAlApagar(btnVolver);
                btnVolver.Click += (s, e) => Volver();
                CancelButton = btnCerrar;

                Controls.Add(EstiloAdmin.TituloDialogo(Titulo, Ancho - 48));
                Controls.Add(lblDescripcion);
                Controls.Add(lblVacia);
                Controls.Add(dgvLotes);
                Controls.Add(lblEstado);
                Controls.Add(btnVolver);
                Controls.Add(btnCerrar);
                lblVacia.BringToFront();

                dgvLotes.SelectionChanged += (s, e) => btnVolver.Enabled = Elegido() != null;
                Shown += (s, e) => Leer();
            }

            private LoteSinContar Elegido()
            {
                return dgvLotes.CurrentRow == null ? null : dgvLotes.CurrentRow.Tag as LoteSinContar;
            }

            private void Leer()
            {
                List<LoteSinContar> lotes = new List<LoteSinContar>();
                string problema = null;
                Cursor = Cursors.WaitCursor;
                try
                {
                    lotes = datos.LeerLotesSinContar();
                }
                catch (Exception ex)
                {
                    Log.Error("Volver a contar: leer la lista", ex);
                    problema = ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion
                        ? "No se pudo leer la lista: no hubo conexión con el servidor. Revisa la red, cierra y vuelve a abrir «Volver a contar...»."
                        : "No se pudo leer la lista por un error inesperado. Cierra y vuelve a abrir «Volver a contar...»; si sigue fallando, " + SiSigueFallando() + ".";
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                dgvLotes.Rows.Clear();
                foreach (LoteSinContar lote in lotes)
                {
                    // "06/10 al 22/10/2026" (el anio una vez si es el mismo): cabe sin cortarse.
                    string fechas = lote.Desde == lote.Hasta ? HistorialCambios.Fecha(lote.Desde)
                        : (lote.Desde.Year == lote.Hasta.Year ? lote.Desde.ToString("dd'/'MM", System.Globalization.CultureInfo.InvariantCulture)
                            : HistorialCambios.Fecha(lote.Desde)) + " al " + HistorialCambios.Fecha(lote.Hasta);
                    int fila = dgvLotes.Rows.Add(HistorialCambios.Fecha(lote.CreadaEn) + " " + lote.CreadaEn.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                        lote.Motivo, lote.Clases, fechas, lote.Laboratorios == 1 ? lote.Laboratorio : lote.Laboratorios + " laboratorios", lote.Quien);
                    dgvLotes.Rows[fila].Tag = lote;
                }

                lblVacia.ForeColor = problema != null ? Color.FromArgb(190, 30, 30) : Color.FromArgb(110, 110, 110);
                lblVacia.Text = problema ?? "Todavía no se ha usado «No tomar en cuenta», o ya se volvió a contar todo: no hay nada que volver a contar.";
                lblVacia.Visible = problema != null || lotes.Count == 0;
                btnVolver.Enabled = Elegido() != null;
            }

            private void Volver()
            {
                LoteSinContar lote = Elegido();
                if (lote == null)
                {
                    lblEstado.ForeColor = Color.FromArgb(190, 30, 30);
                    lblEstado.Text = "Elige en la lista cuál volver a contar.";
                    return;
                }

                string donde = lote.Laboratorios == 1 ? lote.Laboratorio : lote.Laboratorios + " laboratorios";
                if (MessageBox.Show(this, "¿Volver a contar " + (lote.Clases == 1 ? "la clase" : "las " + lote.Clases + " clases") + " de «" + lote.Motivo + "» ("
                        + Fechas(lote.Desde, lote.Hasta) + ", " + donde + ")?" + Environment.NewLine + Environment.NewLine
                        + "Se quitan solo las cancelaciones que se pusieron esa vez. En Faltas vuelven a salir como antes: en rojo las que siguen sin bitácoras.",
                        TituloVentana, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    return;
                }

                lblEstado.Text = "";
                Cursor = Cursors.WaitCursor;
                try
                {
                    int vuelven = datos.VolverAContar(lote);
                    HuboCambios = true;
                    Leer();
                    lblEstado.ForeColor = Color.DarkGreen;
                    lblEstado.Text = "Listo: " + (vuelven == 1 ? "1 clase vuelve a contar como antes." : vuelven + " clases vuelven a contar como antes.")
                        + " Faltas se actualiza al cerrar.";
                }
                catch (InvalidOperationException ex) when (EsAvisoDeDigBit(ex))
                {
                    // Ya se quitaron, una sesion extra en sus horas, solo consulta...: ya dice que pasa.
                    Cursor = Cursors.Default;
                    MessageBox.Show(this, ex.Message, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Leer();
                }
                catch (Exception ex)
                {
                    Log.Error("Volver a contar", ex);
                    Cursor = Cursors.Default;
                    MessageBox.Show(this, NoSePudo("No se pudo volver a contar esas clases", ex), "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }
    }

    // =========================================================================
    // El calendario del mes del Calendario de bitacoras
    // =========================================================================

    /// <summary>Como le fue a un dia en un laboratorio: cuantas de sus clases quedaron en cada estado.</summary>
    internal sealed class ResumenDia
    {
        public int Faltan;
        public int ConBitacoras;
        /// <summary>
        /// Sin bitacoras pero con asistencias que dio su profesor (db/22): ya
        /// pasaron, contaban y se dieron, en ambar. No son Faltan (el dia no se
        /// pinta de rojo por ellas) ni ConBitacoras (Estadisticas, C2: cuentan en
        /// las que ya pasaron, no en las que tienen bitacoras).
        /// </summary>
        public int SinBitacorasConAsistencia;
        public int PorVenir;
        public int NoCuentan;
        public int Canceladas;

        public int Clases
        {
            get { return Faltan + ConBitacoras + SinBitacorasConAsistencia + PorVenir + NoCuentan + Canceladas; }
        }

        public void Contar(EstadoEnCalendario estado)
        {
            switch (estado)
            {
                case EstadoEnCalendario.SinSesion:
                case EstadoEnCalendario.SesionVacia:
                    Faltan++;
                    break;
                case EstadoEnCalendario.ConBitacoras:
                    ConBitacoras++;
                    break;
                case EstadoEnCalendario.SinBitacorasConAsistencia:
                    SinBitacorasConAsistencia++;
                    break;
                case EstadoEnCalendario.PorVenir:
                    PorVenir++;
                    break;
                case EstadoEnCalendario.Cancelada:
                    Canceladas++;
                    break;
                default:
                    NoCuentan++;
                    break;
            }
        }

        /// <summary>"3 sin bitácoras, 4 con bitácoras, 1 cancelada": para la ayuda de cada dia.</summary>
        public string Describir()
        {
            List<string> partes = new List<string>();
            if (Faltan > 0)
            {
                partes.Add(Faltan + " sin bitácoras");
            }

            if (ConBitacoras > 0)
            {
                partes.Add(ConBitacoras + " con bitácoras");
            }

            if (SinBitacorasConAsistencia > 0)
            {
                partes.Add(SinBitacorasConAsistencia + " sin bitácoras con asistencia dada");
            }

            if (PorVenir > 0)
            {
                partes.Add(PorVenir + " por venir");
            }

            if (NoCuentan > 0)
            {
                partes.Add(NoCuentan + " que no cuentan");
            }

            if (Canceladas > 0)
            {
                partes.Add(Canceladas + (Canceladas == 1 ? " cancelada" : " canceladas"));
            }

            return Clases + (Clases == 1 ? " clase: " : " clases: ") + string.Join(", ", partes) + ".";
        }
    }

    /// <summary>
    /// Un mes en seis semanas de lunes a domingo, cada dia del color de como le
    /// fue: rojo si alguna clase se quedo sin bitacoras (con cuantas, abajo),
    /// verde si todas las que contaban registraron, gris si sus clases no
    /// contaban o aun no pasan, blanco sin clases. Pulsar cualquier dia elige su
    /// semana (fila con marco verde). Flechas del teclado: semana; Re Pag / Av
    /// Pag: mes.
    /// </summary>
    internal class MesDeBitacoras : Control
    {
        private const int AltoCabecera = 32;
        private const int AltoDias = 20;
        private const int Semanas = 6;
        private static readonly string[] Iniciales = { "Lu", "Ma", "Mi", "Ju", "Vi", "Sa", "Do" };

        /// <summary>El usuario eligio una semana (su lunes).</summary>
        public event Action<DateTime> SemanaElegida;
        /// <summary>El usuario cambio de mes (su dia 1): hay que leer las semanas nuevas.</summary>
        public event Action<DateTime> MesCambiado;

        private DateTime mes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime semana = CuadriculaSemanal.LunesDe(DateTime.Today);
        private DateTime hoy = DateTime.Today;
        private Dictionary<DateTime, ResumenDia> dias = new Dictionary<DateTime, ResumenDia>();
        private int filaBajoRaton = -1;
        // El texto de ayuda puesto ahora (se compara el texto, no el dia: tambien
        // hay ayuda en las flechas y cambia al cambiar los datos).
        private string ayudaPuesta = "";
        private readonly ToolTip ayuda = new ToolTip { InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 10000 };
        private readonly DrawingFont fuenteMes = new DrawingFont("Century Gothic", 10.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteFlecha = new DrawingFont("Segoe UI Symbol", 10F, FontStyle.Bold);
        private readonly DrawingFont fuenteDias = new DrawingFont("Century Gothic", 8F, FontStyle.Bold);
        private readonly DrawingFont fuenteNumero = new DrawingFont("Century Gothic", 9.5F, FontStyle.Regular);
        private readonly DrawingFont fuenteNumeroHoy = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold);
        private readonly DrawingFont fuenteCuenta = new DrawingFont("Century Gothic", 7.5F, FontStyle.Bold);

        public MesDeBitacoras()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Color.White;
            TabStop = true;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                ayuda.Dispose();
                fuenteMes.Dispose();
                fuenteFlecha.Dispose();
                fuenteDias.Dispose();
                fuenteNumero.Dispose();
                fuenteNumeroHoy.Dispose();
                fuenteCuenta.Dispose();
            }
        }

        /// <summary>El mes que se ve (al ponerlo vale cualquier dia de el).</summary>
        public DateTime Mes
        {
            get { return mes; }
            set
            {
                mes = new DateTime(value.Year, value.Month, 1);
                PonerAyuda("");
                Invalidate();
            }
        }

        /// <summary>El lunes de la semana elegida (al ponerla vale cualquier dia de ella).</summary>
        public DateTime Semana
        {
            get { return semana; }
            set
            {
                semana = CuadriculaSemanal.LunesDe(value);
                Invalidate();
            }
        }

        public DateTime Hoy
        {
            get { return hoy; }
            set
            {
                hoy = value.Date;
                Invalidate();
            }
        }

        /// <summary>Como le fue a cada dia (los que no estan no tienen clases).</summary>
        public Dictionary<DateTime, ResumenDia> Dias
        {
            get { return dias; }
            set
            {
                dias = value ?? new Dictionary<DateTime, ResumenDia>();
                // La ayuda del dia bajo el raton tenia los numeros de antes.
                PonerAyuda("");
                Invalidate();
            }
        }

        /// <summary>El primer dia que se ve: el lunes de la semana del dia 1.</summary>
        public DateTime PrimerDiaVisible
        {
            get { return CuadriculaSemanal.LunesDe(mes); }
        }

        /// <summary>El ultimo dia que se ve: el domingo de la sexta semana.</summary>
        public DateTime UltimoDiaVisible
        {
            get { return PrimerDiaVisible.AddDays(Semanas * 7 - 1); }
        }

        // --- Geometria --------------------------------------------------------

        private int AnchoCelda
        {
            get { return Math.Max(1, ClientSize.Width / 7); }
        }

        private int AltoCelda
        {
            get { return Math.Max(1, (ClientSize.Height - AltoCabecera - AltoDias) / Semanas); }
        }

        private Rectangle RectanguloCelda(int fila, int columna)
        {
            return new Rectangle(columna * AnchoCelda, AltoCabecera + AltoDias + fila * AltoCelda, AnchoCelda, AltoCelda);
        }

        private Rectangle FlechaIzquierda
        {
            get { return new Rectangle(4, 4, 30, AltoCabecera - 8); }
        }

        private Rectangle FlechaDerecha
        {
            get { return new Rectangle(ClientSize.Width - 34, 4, 30, AltoCabecera - 8); }
        }

        private DateTime? FechaEn(Point punto)
        {
            if (punto.Y < AltoCabecera + AltoDias)
            {
                return null;
            }

            int fila = (punto.Y - AltoCabecera - AltoDias) / AltoCelda;
            int columna = punto.X / AnchoCelda;
            if (fila < 0 || fila >= Semanas || columna < 0 || columna > 6)
            {
                return null;
            }

            return PrimerDiaVisible.AddDays(fila * 7 + columna);
        }

        // --- Interaccion ------------------------------------------------------

        private void CambiarMes(int meses)
        {
            mes = mes.AddMonths(meses);
            Invalidate();
            if (MesCambiado != null)
            {
                MesCambiado(mes);
            }
        }

        private void ElegirSemana(DateTime fecha)
        {
            semana = CuadriculaSemanal.LunesDe(fecha);
            Invalidate();
            if (SemanaElegida != null)
            {
                SemanaElegida(semana);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Focus();
            if (FlechaIzquierda.Contains(e.Location))
            {
                CambiarMes(-1);
                return;
            }

            if (FlechaDerecha.Contains(e.Location))
            {
                CambiarMes(1);
                return;
            }

            DateTime? fecha = FechaEn(e.Location);
            if (fecha.HasValue)
            {
                ElegirSemana(fecha.Value);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            DateTime? fecha = FechaEn(e.Location);
            bool sobreFlecha = FlechaIzquierda.Contains(e.Location) || FlechaDerecha.Contains(e.Location);
            Cursor = fecha.HasValue || sobreFlecha ? Cursors.Hand : Cursors.Default;
            int fila = fecha.HasValue ? (int)(fecha.Value - PrimerDiaVisible).TotalDays / 7 : -1;
            if (fila != filaBajoRaton)
            {
                filaBajoRaton = fila;
                Invalidate();
            }

            PonerAyuda(fecha.HasValue ? Ayuda(fecha.Value) : sobreFlecha ? "Cambiar de mes" : "");
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
            PonerAyuda("");
            if (filaBajoRaton >= 0)
            {
                filaBajoRaton = -1;
                Invalidate();
            }
        }

        /// <summary>Cambia la ayuda solo si es otro texto (si no, parpadea al mover el raton).</summary>
        private void PonerAyuda(string texto)
        {
            if (texto != ayudaPuesta)
            {
                ayudaPuesta = texto;
                ayuda.SetToolTip(this, texto);
            }
        }

        /// <summary>"Martes 06/10/2026: 7 clases: 3 sin bitácoras, 4 con bitácoras. Pulsa para ver su semana."</summary>
        private string Ayuda(DateTime fecha)
        {
            ResumenDia dia;
            string cual = HorariosDatos.NombresDia[((int)fecha.DayOfWeek + 6) % 7 + 1] + " " + fecha.ToString("dd/MM/yyyy") + ": ";
            return (dias.TryGetValue(fecha, out dia) ? cual + dia.Describir() : cual + "sin clases en este laboratorio.")
                + Environment.NewLine + "Pulsa para ver el horario de su semana.";
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Las cuatro flechas son del calendario (si no, la izquierda y la derecha
            // sacaban el foco a otro control).
            return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int semanas = e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Down ? 1 : 0;
            if (semanas != 0)
            {
                DateTime nueva = semana.AddDays(7 * semanas);
                if (nueva < PrimerDiaVisible || nueva.AddDays(6) > UltimoDiaVisible)
                {
                    // Fuera de lo que se ve: el mes de esa semana (su jueves decide el mes).
                    semana = nueva;
                    CambiarMesA(nueva.AddDays(3));
                }
                else
                {
                    ElegirSemana(nueva);
                }

                e.Handled = true;
            }
            else if (e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                // Izquierda y derecha, como las flechas de arriba: el mes.
                CambiarMes(e.KeyCode == Keys.PageUp || e.KeyCode == Keys.Left ? -1 : 1);
                e.Handled = true;
            }
        }

        private void CambiarMesA(DateTime fecha)
        {
            mes = new DateTime(fecha.Year, fecha.Month, 1);
            Invalidate();
            if (MesCambiado != null)
            {
                MesCambiado(mes);
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        // --- Pintado ----------------------------------------------------------

        /// <summary>Hacia el blanco: los dias de otro mes, mas palidos.</summary>
        private static Color Palido(Color color, float cuanto)
        {
            return Color.FromArgb(
                color.R + (int)((255 - color.R) * cuanto),
                color.G + (int)((255 - color.G) * cuanto),
                color.B + (int)((255 - color.B) * cuanto));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int ancho = ClientSize.Width;
            g.Clear(BackColor);

            // Cabecera: flechas y nombre del mes.
            using (SolidBrush fondo = new SolidBrush(Color.FromArgb(240, 248, 240)))
            {
                g.FillRectangle(fondo, 0, 0, ancho, AltoCabecera);
            }

            TextRenderer.DrawText(g, "◀", fuenteFlecha, FlechaIzquierda, Color.DarkGreen, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "▶", fuenteFlecha, FlechaDerecha, Color.DarkGreen, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, CalendarioFiltro.Meses[mes.Month] + " " + mes.Year, fuenteMes, new Rectangle(34, 0, ancho - 68, AltoCabecera),
                Color.DarkGreen, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            for (int c = 0; c < 7; c++)
            {
                TextRenderer.DrawText(g, Iniciales[c], fuenteDias, new Rectangle(c * AnchoCelda, AltoCabecera, AnchoCelda, AltoDias),
                    c == 6 ? Color.Silver : Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            DateTime primero = PrimerDiaVisible;
            for (int fila = 0; fila < Semanas; fila++)
            {
                for (int columna = 0; columna < 7; columna++)
                {
                    DateTime fecha = primero.AddDays(fila * 7 + columna);
                    Rectangle celda = RectanguloCelda(fila, columna);
                    bool otroMes = fecha.Month != mes.Month;
                    ResumenDia dia;
                    dias.TryGetValue(fecha, out dia);
                    // Una clase sin bitacoras pero con asistencias que dio su profesor
                    // (db/22) se dio: no pinta el dia de rojo, cuenta como las que registraron.
                    Color fondo = dia == null || dia.Clases == 0 ? Color.White
                        : dia.Faltan > 0 ? PestanaCalendario.RojoFalta
                        : dia.ConBitacoras > 0 || dia.SinBitacorasConAsistencia > 0 ? PestanaCalendario.VerdeBien
                        : PestanaCalendario.GrisNoCuenta;
                    if (otroMes)
                    {
                        fondo = Palido(fondo, 0.55f);
                    }

                    if (fila == filaBajoRaton)
                    {
                        fondo = EstiloHorarios.Oscurecer(fondo, 0.07f);
                    }

                    using (SolidBrush pincel = new SolidBrush(fondo))
                    {
                        g.FillRectangle(pincel, celda.X + 1, celda.Y + 1, celda.Width - 2, celda.Height - 2);
                    }

                    bool rojo = dia != null && dia.Faltan > 0;
                    Color colorNumero = otroMes ? Color.FromArgb(175, 175, 175) : rojo ? Color.White : Color.FromArgb(40, 40, 40);
                    Rectangle arriba = new Rectangle(celda.X, celda.Y + 2, celda.Width, celda.Height / 2 + 4);
                    TextRenderer.DrawText(g, fecha.Day.ToString(), fecha == hoy ? fuenteNumeroHoy : fuenteNumero, arriba, colorNumero,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                    // En rojo, cuantas clases se quedaron sin bitacoras ese dia.
                    if (rojo)
                    {
                        Rectangle abajo = new Rectangle(celda.X, celda.Y + celda.Height / 2 + 2, celda.Width, celda.Height / 2 - 4);
                        TextRenderer.DrawText(g, dia.Faltan.ToString(), fuenteCuenta, abajo, otroMes ? Color.FromArgb(255, 235, 235) : Color.FromArgb(255, 230, 230),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }

                    if (fecha == hoy)
                    {
                        using (Pen naranja = new Pen(Color.FromArgb(255, 140, 0), 2))
                        {
                            g.DrawRectangle(naranja, celda.X + 2, celda.Y + 2, celda.Width - 5, celda.Height - 5);
                        }
                    }
                }
            }

            // La semana elegida, con un marco verde alrededor de toda la fila.
            int filaElegida = (int)(semana - primero).TotalDays / 7;
            if (semana >= primero && filaElegida >= 0 && filaElegida < Semanas)
            {
                Rectangle izquierda = RectanguloCelda(filaElegida, 0);
                using (Pen verde = new Pen(Color.DarkGreen, 3))
                {
                    g.DrawRectangle(verde, izquierda.X + 1, izquierda.Y + 1, AnchoCelda * 7 - 3, AltoCelda - 3);
                }
            }

            if (Focused)
            {
                ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, ancho, ClientSize.Height));
            }
        }
    }

    /// <summary>Que quiere decir cada color del calendario del mes.</summary>
    internal class LeyendaDelMes : Control
    {
        private readonly DrawingFont fuente = new DrawingFont("Century Gothic", 8.5F);
        private bool conDadas;

        /// <summary>La base tiene las asistencias que da el profesor (db/22): el verde dice "Todas se dieron".</summary>
        public bool ConDadas
        {
            get { return conDadas; }
            set
            {
                if (value != conDadas)
                {
                    conDadas = value;
                    Invalidate();
                }
            }
        }

        public LeyendaDelMes()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            // Solo se mira: sin foco (con el, el foco "desaparecia" al pasar por aqui).
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.White;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                fuente.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            // La primera columna, mas ancha: sus textos son los largos.
            int segunda = ClientSize.Width * 6 / 10;
            Muestra(g, new Point(0, 2), segunda, PestanaCalendario.RojoFalta, null, "Alguna clase sin bitácoras");
            // Con db/22 el verde tambien es un dia de clases sin bitacoras pero con
            // asistencias que dio su docente (en ambar en la semana): se dieron.
            Muestra(g, new Point(segunda, 2), ClientSize.Width - segunda, PestanaCalendario.VerdeBien, null, ConDadas ? "Todas se dieron" : "Todas registraron");
            Muestra(g, new Point(0, 22), segunda, PestanaCalendario.GrisNoCuenta, null, "No contaban o por venir");
            Muestra(g, new Point(segunda, 22), ClientSize.Width - segunda, Color.White, Color.FromArgb(255, 140, 0), "Hoy");
        }

        private void Muestra(Graphics g, Point donde, int ancho, Color relleno, Color? borde, string texto)
        {
            Rectangle cuadro = new Rectangle(donde.X + 2, donde.Y + 2, 14, 14);
            using (SolidBrush pincel = new SolidBrush(relleno))
            {
                g.FillRectangle(pincel, cuadro);
            }

            using (Pen lapiz = new Pen(borde ?? Color.FromArgb(190, 190, 190), borde.HasValue ? 2 : 1))
            {
                g.DrawRectangle(lapiz, cuadro);
            }

            TextRenderer.DrawText(g, texto, fuente, new Rectangle(cuadro.Right + 6, donde.Y, Math.Max(10, ancho - 26), 18), Color.FromArgb(70, 70, 70),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
