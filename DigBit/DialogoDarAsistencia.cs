using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// "Dar asistencia" de Mis grupos (db/22): el profesor le da la asistencia de
    /// hoy a quien esta en el salon pero no pudo registrar su bitacora (su equipo
    /// no tenia internet, no funcionaba, no habia uno libre u otro motivo). Solo
    /// mientras dura la clase: lo decide el servidor al guardar
    /// (AsistenciaDatos.DarAsistencia) y, si ya termino, lo dice aqui sin cerrar.
    ///
    /// Dos modos: con el alumno (desde su casilla de hoy) o por su matricula
    /// EXACTA (desde el enlace "Dar asistencia a alguien que no esta en la
    /// lista..."), que se busca y ensena su nombre para comprobarlo antes de
    /// darla; nunca se busca por nombre ni se ensena el correo. Como
    /// DialogoReporteFalla: buscar y guardar son llamadas sincronas con el cursor
    /// de espera; un error no cierra el dialogo. Si la clase termina con el
    /// dialogo abierto (hora estimada del servidor), "Darle la asistencia" se
    /// apaga y lo dice: el servidor ya no la aceptaria. En kiosco va encima de
    /// todo y no abre dialogos de archivos ni usa el portapapeles.
    /// </summary>
    internal class DialogoDarAsistencia : Form
    {
        private const int Ancho = 580;
        private const int LargoMatricula = 45;
        private static readonly Color TextoSubtitulo = Color.FromArgb(70, 70, 70);
        private static readonly Color TextoGris = Color.FromArgb(100, 100, 100);
        // Apagado, el boton principal se ve gris: verde oscuro con letra gris parecia pulsable (como en Alumno_Principal).
        private static readonly Color BotonApagado = Color.FromArgb(205, 210, 205);

        private readonly Func<string, EstudianteBuscado> buscar;
        private readonly Func<int, AlumnoDeAsistencia> deLaLista;
        private readonly Func<int, string, string, string, string> dar;
        private readonly Func<DateTime> ahoraServidor;
        private readonly DateTime hoy;
        private readonly DateTime fin;
        private readonly bool porMatricula;
        private readonly bool conClasesAntes;
        private readonly string textoTerminada;
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };
        // Mira cada segundo si la clase ya termino (ver RevisarFin).
        private readonly Timer reloj = new Timer { Interval = 1000 };
        // Las letras propias del dialogo: se liberan con el (las de EstiloHorarios/EstiloAdmin no son suyas).
        private readonly DrawingFont fuenteDetalle = new DrawingFont("Century Gothic", 10F);
        private readonly DrawingFont fuenteGris = new DrawingFont("Century Gothic", 9.5F);
        private readonly DrawingFont fuenteNombre = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
        private readonly DrawingFont fuenteOpcion = new DrawingFont("Century Gothic", 10F);

        private readonly TextBox txtMatricula;
        private readonly Button btnBuscar;
        private readonly Label lblNombre;
        private readonly Label lblMatricula;
        private readonly List<RadioButton> motivos = new List<RadioButton>();
        private readonly ComboBox cmbEquipo;
        private readonly Label etiquetaDetalle;
        private readonly TextBox txtDetalle;
        private readonly Label lblError;
        private readonly Button btnDar;

        // El alumno al que se le da: el de la casilla, o el que se encontro por su matricula.
        private int idAlumno;
        private string nombre = "";
        // La clase ya termino (hora estimada del servidor): no se ofrece darla.
        private bool terminada;

        /// <param name="materia">La materia de la clase.</param>
        /// <param name="grupo">El grupo de la clase.</param>
        /// <param name="preparacion">El bloque en curso, sus equipos y lo que ya avisaron sin internet (AsistenciaDatos.PrepararDar).</param>
        /// <param name="hoy">El dia de hoy (del servidor): para saber si se le quito la asistencia hoy.</param>
        /// <param name="alumno">El de la casilla; null para buscarlo por su matricula.</param>
        /// <param name="conClasesAntes">La lista ya tiene clases antes de hoy: quien entra hoy, entra con falta en ellas.</param>
        /// <param name="ahoraServidor">La hora del servidor, estimada (la de Mis grupos): para apagar el boton al terminar la clase.</param>
        /// <param name="buscar">matricula -> la cuenta, o null si no hay ninguna (AsistenciaDatos.BuscarEstudiante).</param>
        /// <param name="deLaLista">id -> ese alumno en la lista que se ve, o null si aun no esta.</param>
        /// <param name="dar">(alumno, motivo, equipo o null, detalle) -> null si quedo, o por que no (AsistenciaDatos.DarAsistencia).</param>
        public DialogoDarAsistencia(string materia, string grupo, PreparacionDar preparacion, DateTime hoy, AlumnoDeAsistencia alumno,
            bool conClasesAntes, Func<DateTime> ahoraServidor,
            Func<string, EstudianteBuscado> buscar, Func<int, AlumnoDeAsistencia> deLaLista, Func<int, string, string, string, string> dar)
        {
            this.buscar = buscar;
            this.deLaLista = deLaLista;
            this.dar = dar;
            this.ahoraServidor = ahoraServidor;
            this.conClasesAntes = conClasesAntes;
            this.hoy = hoy.Date;
            fin = this.hoy + preparacion.Fin;
            porMatricula = alumno == null;
            string hasta = HorariosDatos.Hora(preparacion.Fin);
            // El mismo texto que da el servidor si se intenta despues (AsistenciaDatos.DarAsistencia).
            textoTerminada = "La clase terminó a las " + hasta + ": la asistencia solo se puede dar mientras dura.";
            string subtitulo = materia + ", grupo " + grupo + ", de " + HorariosDatos.Hora(preparacion.Inicio) + " a " + hasta
                + ". Para quien está en el salón pero no pudo registrar su bitácora. Solo mientras dura la clase (hasta las " + hasta + ").";
            int altoSubtitulo = AltoDe(subtitulo, fuenteDetalle);
            List<Control> controles = new List<Control>();

            // Compacto: con la pista y por matricula cabe en los 720 px utiles de 1366x768.
            int y = 52 + altoSubtitulo + 12;
            if (porMatricula)
            {
                controles.Add(EstiloHorarios.CrearEtiqueta("Matrícula del estudiante", new Point(24, y)));
                txtMatricula = EstiloHorarios.CrearTexto(new Point(24, y + 24), 260);
                txtMatricula.MaxLength = LargoMatricula;
                txtMatricula.TextChanged += (s, e) => OlvidarEncontrado();
                btnBuscar = EstiloHorarios.CrearBoton("Buscar", new Point(24 + 260 + 10, y + 22), new Size(110, 32));
                btnBuscar.Click += (s, e) => Buscar();
                ayuda.SetToolTip(btnBuscar, "Busca la cuenta con esa matrícula exacta (también con Enter) y enseña su nombre para que lo compruebes.");
                controles.Add(txtMatricula);
                controles.Add(btnBuscar);
                y += 24 + 32 + 6;
            }

            lblNombre = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = fuenteNombre,
                ForeColor = Color.DarkGreen,
                Location = new Point(24, y),
                Size = new Size(Ancho - 48, 22),
                UseMnemonic = false
            };
            lblMatricula = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = fuenteGris,
                ForeColor = TextoGris,
                Location = new Point(24, y + 23),
                Size = new Size(Ancho - 48, 20),
                UseMnemonic = false
            };
            controles.Add(lblNombre);
            controles.Add(lblMatricula);
            y += 23 + 20 + 10;

            controles.Add(EstiloHorarios.CrearEtiqueta("¿Por qué no pudo registrar su bitácora?", new Point(24, y)));
            // En dos columnas: internet | equipo libre; no funcionaba | otro.
            string[] orden = { AsistenciasDadas.SinInternet, AsistenciasDadas.SinEquipo, AsistenciasDadas.EquipoNoFunciona, AsistenciasDadas.Otro };
            for (int i = 0; i < orden.Length; i++)
            {
                string clave = orden[i];
                RadioButton opcion = new RadioButton
                {
                    AutoSize = true,
                    Font = fuenteOpcion,
                    ForeColor = Color.FromArgb(30, 30, 30),
                    Location = new Point(i % 2 == 0 ? 24 : 24 + 272, y + 24 + (i / 2) * 26),
                    Tag = clave,
                    Text = AsistenciasDadas.NombreDeMotivo(clave),
                    Checked = clave == AsistenciasDadas.SinInternet
                };
                opcion.CheckedChanged += (s, e) =>
                {
                    if (((RadioButton)s).Checked)
                    {
                        CambiarMotivo();
                    }
                };
                string ayudaMotivo = AsistenciasDadas.AyudaDeMotivo(clave);
                if (ayudaMotivo.Length > 0)
                {
                    ayuda.SetToolTip(opcion, ayudaMotivo);
                }

                motivos.Add(opcion);
                controles.Add(opcion);
            }

            y += 24 + 26 + 26 + 8;
            controles.Add(EstiloHorarios.CrearEtiqueta("¿Qué equipo? (opcional)", new Point(24, y)));
            cmbEquipo = EstiloHorarios.CrearCombo(new Point(24, y + 24), 330);
            cmbEquipo.DropDownStyle = ComboBoxStyle.DropDown;
            cmbEquipo.MaxLength = AsistenciasDadas.LargoEquipo;
            cmbEquipo.Items.Add(AsistenciasDadas.NoLoSe);
            foreach (string equipo in (preparacion.Equipos ?? new List<string>()).Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                cmbEquipo.Items.Add(equipo.Trim());
            }

            cmbEquipo.SelectedIndex = 0;
            ayuda.SetToolTip(cmbEquipo, "El equipo que no le sirvió, de la lista o escrito. Si no lo sabes, deja «" + AsistenciasDadas.NoLoSe + "».");
            controles.Add(cmbEquipo);
            y += 24 + 32 + 6;

            // Lo que ya avisaron hoy los estudiantes de esta clase (db/10): ayuda a elegir el equipo.
            if (!string.IsNullOrWhiteSpace(preparacion.Pista))
            {
                int altoPista = AltoDe(preparacion.Pista, fuenteGris);
                controles.Add(new Label
                {
                    AutoSize = false,
                    Font = fuenteGris,
                    ForeColor = TextoGris,
                    Location = new Point(24, y),
                    Size = new Size(Ancho - 48, altoPista),
                    Text = preparacion.Pista,
                    UseMnemonic = false
                });
                y += altoPista + 8;
            }

            etiquetaDetalle = EstiloHorarios.CrearEtiqueta("Detalle (opcional)", new Point(24, y));
            controles.Add(etiquetaDetalle);
            txtDetalle = EstiloHorarios.CrearTexto(new Point(24, y + 24), Ancho - 48);
            txtDetalle.MaxLength = AsistenciasDadas.LargoComentario;
            controles.Add(txtDetalle);
            y += 24 + 32 + 8;

            string nota = "Contará como asistencia, igual que una bitácora, pero no es una bitácora. "
                + "Queda constancia de que tú se la diste, a qué hora y por qué.";
            int altoNota = AltoDe(nota, fuenteGris);
            controles.Add(new Label
            {
                AutoSize = false,
                Font = fuenteGris,
                ForeColor = TextoGris,
                Location = new Point(24, y),
                Size = new Size(Ancho - 48, altoNota),
                Text = nota
            });
            y += altoNota + 8;

            // Tres renglones: un fallo dice por que y que hacer (MensajeDeFallo).
            lblError = EstiloAdmin.LineaError(new Point(24, y), Ancho - 48, 56);
            controles.Add(lblError);
            y += 56 + 4;

            EstiloHorarios.PrepararDialogo(this, "Dar asistencia", new Size(Ancho, y + 40 + 16));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Dar asistencia", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = fuenteDetalle,
                ForeColor = TextoSubtitulo,
                Location = new Point(24, 52),
                Size = new Size(Ancho - 48, altoSubtitulo),
                Text = subtitulo,
                UseMnemonic = false
            });
            Controls.AddRange(controles.ToArray());
            btnDar = EstiloAdmin.BotonesDialogo(this, "Darle la asistencia", Dar);
            btnDar.EnabledChanged += (s, e) =>
            {
                Color fondo = btnDar.Enabled ? Color.DarkGreen : BotonApagado;
                btnDar.BackColor = fondo;
                btnDar.FlatAppearance.BorderColor = fondo;
            };
            reloj.Tick += (s, e) => RevisarFin();

            if (porMatricula)
            {
                // Hasta encontrarlo no se puede dar: el nombre es la comprobacion.
                btnDar.Enabled = false;
                ActiveControl = txtMatricula;
            }
            else
            {
                idAlumno = alumno.Id;
                nombre = string.IsNullOrWhiteSpace(alumno.Nombre) ? alumno.Matricula : alumno.Nombre;
                lblNombre.Text = nombre;
                lblMatricula.Text = "Matrícula " + alumno.Matricula;
                ActiveControl = motivos[0];
            }

            CambiarMotivo();
        }

        /// <summary>A quien se le dio (para el aviso de la ventana).</summary>
        public string NombreDelEstudiante
        {
            get { return nombre; }
        }

        /// <summary>
        /// Sin darla: el servidor dijo que no (ya registro, ya se la dio, la clase
        /// termino...) o la clase termino con el dialogo abierto. La lista de debajo
        /// puede estar vieja: quien lo abrio la vuelve a leer.
        /// </summary>
        public bool HayQueReleer { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            reloj.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            reloj.Stop();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reloj.Dispose();
                ayuda.Dispose();
            }

            base.Dispose(disposing);
            if (disposing)
            {
                // Despues de los controles que las usan.
                fuenteDetalle.Dispose();
                fuenteGris.Dispose();
                fuenteNombre.Dispose();
                fuenteOpcion.Dispose();
            }
        }

        /// <summary>
        /// Cada segundo: si ya paso el fin del bloque (hora estimada del servidor),
        /// "Darle la asistencia" y "Buscar" se apagan y el error dice por que; el
        /// servidor la rechazaria con ese mismo texto.
        /// </summary>
        private void RevisarFin()
        {
            if (terminada || ahoraServidor == null || ahoraServidor() < fin)
            {
                return;
            }

            terminada = true;
            HayQueReleer = true;
            reloj.Stop();
            btnDar.Enabled = false;
            if (btnBuscar != null)
            {
                btnBuscar.Enabled = false;
            }

            lblError.Text = textoTerminada;
            Log.Info("Dar asistencia: la clase termino con el dialogo abierto (" + HorariosDatos.Hora(fin.TimeOfDay) + ").");
        }

        /// <summary>Borra el error; si la clase ya termino, se queda diciendolo.</summary>
        private void LimpiarError()
        {
            lblError.Text = terminada ? textoTerminada : "";
        }

        /// <summary>Enter en la caja de la matricula busca (no da la asistencia).</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (porMatricula && keyData == Keys.Enter && ActiveControl == txtMatricula)
            {
                Buscar();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static int AltoDe(string texto, DrawingFont fuente)
        {
            return TextRenderer.MeasureText(texto, fuente, new Size(Ancho - 48, int.MaxValue), TextFormatFlags.WordBreak).Height + 4;
        }

        private string MotivoElegido
        {
            get
            {
                RadioButton elegido = motivos.FirstOrDefault(m => m.Checked);
                return elegido == null ? null : (string)elegido.Tag;
            }
        }

        /// <summary>El equipo solo con "no tenia internet" o "no funcionaba"; con "Otro motivo" el detalle es obligatorio.</summary>
        private void CambiarMotivo()
        {
            string motivo = MotivoElegido;
            cmbEquipo.Enabled = AsistenciasDadas.ConEquipo(motivo);
            etiquetaDetalle.Text = motivo == AsistenciasDadas.Otro ? "Detalle (escribe el motivo)" : "Detalle (opcional)";
            LimpiarError();
        }

        /// <summary>Cambio la matricula: lo encontrado ya no vale y no se puede dar hasta buscar otra vez.</summary>
        private void OlvidarEncontrado()
        {
            idAlumno = 0;
            nombre = "";
            lblNombre.Text = "";
            lblMatricula.Text = "";
            LimpiarError();
            btnDar.Enabled = false;
        }

        /// <summary>
        /// La cuenta con esa matricula exacta: su nombre para comprobarlo, o por que
        /// no se le puede dar (no existe, no es de estudiante, esta dada de baja, ya
        /// registro hoy o ya se la dio). Solo lee.
        /// </summary>
        private void Buscar()
        {
            string matricula = (txtMatricula.Text ?? "").Trim();
            OlvidarEncontrado();
            if (terminada)
            {
                // La clase termino: ya no se puede dar a nadie (el error lo dice).
                return;
            }

            if (matricula.Length == 0)
            {
                lblError.Text = "Escribe su matrícula.";
                txtMatricula.Focus();
                return;
            }

            EstudianteBuscado cuenta;
            Cursor = Cursors.WaitCursor;
            btnBuscar.Enabled = false;
            try
            {
                cuenta = buscar(matricula);
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro.
                Log.Error("Dar asistencia: buscar una matricula", ex);
                lblError.Text = MensajeDeFallo.Explicar("No se pudo buscar la matrícula", ex, AvisoDeFalloDocente.SiSigue);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
                btnBuscar.Enabled = !terminada;
            }

            string problema = PorQueNo(cuenta, matricula);
            if (problema != null)
            {
                lblError.Text = problema;
                txtMatricula.Focus();
                txtMatricula.SelectAll();
                return;
            }

            AlumnoDeAsistencia enLista = deLaLista == null ? null : deLaLista(cuenta.Id);
            idAlumno = cuenta.Id;
            nombre = string.IsNullOrWhiteSpace(cuenta.Nombre) ? cuenta.Matricula : cuenta.Nombre;
            lblNombre.Text = nombre;
            // Si aun no esta: con falta en las clases que ya cuentan; si es la primera, desde hoy.
            lblMatricula.Text = "Matrícula " + cuenta.Matricula + (enLista != null ? ""
                : conClasesAntes ? ". Entrará a la lista con falta en las clases anteriores." : ". Entrará a la lista desde hoy.");
            btnDar.Enabled = !terminada;
            // El siguiente paso es elegir por que: Enter desde ahi la da.
            RadioButton elegido = motivos.FirstOrDefault(m => m.Checked) ?? motivos[0];
            elegido.Focus();
        }

        /// <summary>Por que a esa cuenta no se le puede dar la asistencia de hoy, o null si si.</summary>
        private string PorQueNo(EstudianteBuscado cuenta, string matricula)
        {
            if (cuenta == null)
            {
                const string Resto = ": revisa que esté bien escrita. Si aún no tiene cuenta, que la cree con «Crear cuenta» al iniciar sesión "
                    + "en un equipo con internet; después podrás darle la asistencia si la clase sigue.";
                return QueQuepa("No hay ninguna cuenta con la matrícula " + matricula + Resto, "No hay ninguna cuenta con esa matrícula" + Resto);
            }

            if (!cuenta.EsEstudiante)
            {
                return QueQuepa("La matrícula " + matricula + " no es de una cuenta de estudiante: revisa que esté bien escrita.",
                    "Esa matrícula no es de una cuenta de estudiante: revisa que esté bien escrita.");
            }

            if (!cuenta.Activo)
            {
                return QueQuepa("La cuenta con la matrícula " + matricula + " está dada de baja: avisa a la administración de DigBit.",
                    "La cuenta con esa matrícula está dada de baja: avisa a la administración de DigBit.");
            }

            string quien = string.IsNullOrWhiteSpace(cuenta.Nombre) ? cuenta.Matricula : cuenta.Nombre;
            if (cuenta.BitacorasHoy > 0)
            {
                // Si registro y se le quito, lo que toca es devolversela desde su casilla.
                AlumnoDeAsistencia enLista = deLaLista == null ? null : deLaLista(cuenta.Id);
                return enLista != null && enLista.Retiradas.Contains(hoy)
                    ? "Hoy registró su bitácora y le quitaste la asistencia: si está en el salón, devuélvesela desde su casilla de hoy."
                    : quien + " ya registró su bitácora hoy en esta clase: ya tiene asistencia.";
            }

            if (cuenta.DadaHoy)
            {
                return "Ya le diste la asistencia hoy a " + quien + ".";
            }

            return null;
        }

        /// <summary>
        /// El aviso con la matricula escrita si cabe en los renglones del error; si no
        /// (una matricula larga: la caja admite 45), el que dice "esa matricula": la
        /// matricula sigue a la vista en la caja y el aviso no se corta.
        /// </summary>
        private string QueQuepa(string conMatricula, string sinMatricula)
        {
            Size medida = TextRenderer.MeasureText(conMatricula, lblError.Font, new Size(lblError.Width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return medida.Height <= lblError.Height ? conMatricula : sinMatricula;
        }

        /// <summary>
        /// Le da la asistencia (lo comprueba y lo guarda el servidor, con su reloj).
        /// Si quedo, se cierra; si no, dice por que y se queda abierto.
        /// </summary>
        private void Dar()
        {
            RevisarFin();
            if (terminada)
            {
                return;
            }

            if (idAlumno <= 0)
            {
                lblError.Text = porMatricula ? "Escribe su matrícula y pulsa «Buscar»." : "No se sabe a quién darle la asistencia: cierra y vuelve a pulsar su casilla.";
                if (porMatricula)
                {
                    txtMatricula.Focus();
                }

                return;
            }

            string motivo = MotivoElegido;
            if (motivo == null)
            {
                lblError.Text = "Elige por qué no pudo registrar su bitácora.";
                motivos[0].Focus();
                return;
            }

            string detalle = (txtDetalle.Text ?? "").Trim();
            if (motivo == AsistenciasDadas.Otro && detalle.Length == 0)
            {
                lblError.Text = "Escribe el motivo en el detalle.";
                txtDetalle.Focus();
                return;
            }

            string equipo = AsistenciasDadas.ConEquipo(motivo) ? (cmbEquipo.Text ?? "").Trim() : null;
            if (string.IsNullOrEmpty(equipo) || string.Equals(equipo, AsistenciasDadas.NoLoSe, StringComparison.OrdinalIgnoreCase))
            {
                equipo = null;
            }

            string problema;
            btnDar.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                problema = dar(idAlumno, motivo, equipo, detalle.Length == 0 ? null : detalle);
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro.
                Log.Error("Dar asistencia: guardar", ex);
                problema = MensajeDeFallo.Explicar("No se pudo dar la asistencia", ex, AvisoDeFalloDocente.SiSigue);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnDar.Enabled = !terminada;
            }

            if (problema == null)
            {
                DialogResult = DialogResult.OK;
                return;
            }

            // La lista de debajo puede no ser la de ahora (ya registro, ya se la dio, la clase termino...).
            HayQueReleer = true;
            lblError.Text = problema;
        }
    }
}
