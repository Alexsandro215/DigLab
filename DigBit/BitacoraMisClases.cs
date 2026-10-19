using DigBit.conexion;
using DigBit.Infraestructura;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// El horario del profesor en una semana de calendario: de lunes a sabado,
    /// cada dia con su fecha, y el selector de Horarios (SelectorSemana) para ir
    /// a la anterior o a la siguiente y volver a "Esta semana". Al pulsar una
    /// clase se abre su bitacora de ESE dia (BitacoraSesion) con quien registro
    /// en ella; si aun no empieza, la ayuda dice desde cuando se registra.
    /// Sustituye a la pantalla que pedia el codigo a mano, asi que un profesor
    /// solo ve sus propias clases. El administrador lo abre desde Profesores
    /// para un profesor concreto ("Horario de X"), y tambien el encargado de
    /// laboratorio sin el codigo del profesor: solo para verlo (el horario se
    /// cambia en Horarios; BitacoraSesion no escribe). F5 lo vuelve a leer.
    /// </summary>
    public class BitacoraMisClases : Form
    {
        private const string AyudaNormal = "Pulsa una clase para ver quién registró ese día. F5 actualiza.";
        private const int SegundosDelAviso = 6;
        private static readonly Color GrisAyuda = Color.Gray;
        private static readonly Color NaranjaAviso = Color.FromArgb(190, 85, 0);

        private readonly Consultas consultas = new Consultas();
        private readonly HorariosDatos datos = new HorariosDatos();
        private int idProfesor;
        private string nombreProfesor = "";
        // El administrador (o el encargado de laboratorio) mirando el horario de otro: solo para verlo.
        private readonly bool soloVer;
        // Laboratorios que no entran con el codigo fijo (db/11).
        private HashSet<int> sinCodigoFijo = new HashSet<int>();
        // El horario se leyo bien antes de abrirse: al abrir, la ventana se ajusta
        // a el (solo entonces; despues manda el tamano que le de quien la usa).
        private bool leidoAlAbrir;
        // El subtitulo sin su codigo de clase, que se le anade al leer el horario.
        private string subtituloBase = "";
        // Mi horario del profesor (no "Horario de X"): sus clases con lo que eligio para ellas (db/19).
        private readonly bool personal;

        // Lo que se lee al abrir y con Actualizar (F5): sus clases y la hora del
        // servidor (con el reloj del equipo de ese momento, para estimarla
        // despues). Las franjas de cada clase se piden la primera vez que una
        // semana las necesita: ir a otra semana solo lee sus excepciones.
        private List<ClaseInfo> clases = new List<ClaseInfo>();
        private readonly Dictionary<int, List<FranjaInfo>> franjasDeClase = new Dictionary<int, List<FranjaInfo>>();
        private DateTime ahoraLeida;
        private DateTime leidaEnUtc;
        // La semana la eligio quien la usa (flechas o "Esta semana"): Actualizar se queda en ella.
        private bool semanaElegida;
        // Ya salio bien una lectura completa (CargarHorario). Mientras no, cambiar de
        // semana lo vuelve a leer todo: sin sus clases diria que no tiene ninguna.
        private bool horarioLeido;
        // Las horas de la semana de hoy: otra semana las amplia, pero no las quita
        // (al moverse, la cuadricula no cambia de forma). Null sin clases.
        private HorasDeSemana horasDeHoy;

        private Label lblTitulo;
        private Label lblSubtitulo;
        private SelectorSemana selectorSemana;
        private Label lblAyuda;
        private Timer avisoTimer;
        private CuadroCodigo cuadroCodigo;
        private Panel contenedorSemana;
        private CuadriculaSemanal cuadricula;
        private LeyendaDelHorario leyenda;
        private Button btnActualizar;
        private Button btnCerrar;

        public BitacoraMisClases()
        {
            InicializarComponentes();
            // El circulo de cada clase la personaliza; lo que se guarde (aqui o en
            // otra ventana) se ve al momento. Antes de CargarHorario, que lo lee.
            personal = Datos_User.TipoUsuario == TiposUsuario.Profesor;
            if (personal)
            {
                cuadricula.CirculoClic += id => PersonalizacionClases.Abrir(this, id);
                PersonalizacionClases.Cambio += PersonalizacionCambio;
                FormClosed += (s, e) => PersonalizacionClases.Cambio -= PersonalizacionCambio;
            }

            // Lee tambien quien es (CargarProfesor): si al abrir no se pudo, Actualizar,
            // F5 y las flechas de la semana lo reintentan.
            CargarHorario();
        }

        /// <summary>
        /// El horario de ese profesor para el administrador (o el encargado de
        /// laboratorio), solo para verlo: tambien se pulsa una clase para ver
        /// quien registro ese dia (BitacoraSesion solo lee).
        /// </summary>
        public BitacoraMisClases(int idProfesor, string nombre)
        {
            soloVer = true;
            this.idProfesor = idProfesor;
            nombreProfesor = nombre ?? "";
            InicializarComponentes();
            // Quien no ve los codigos de los profesores (el encargado de laboratorio):
            // ni en los cuadros ni en su ayuda, ni el cuadro grande ni el subtitulo
            // (CargarHorario), ni en la bitacora que se abre. Antes de
            // CargarHorario, que pone las franjas.
            cuadricula.SinCodigos = !Permisos.VeCodigosDeProfesor;

            Text = "Horario de " + nombreProfesor;
            lblTitulo.Text = "Horario de " + nombreProfesor;
            subtituloBase = "Sus clases de la semana en todos los laboratorios. Solo para verlo: se cambia en Horarios.";
            lblSubtitulo.Text = subtituloBase;
            CargarHorario();
        }

        private void InicializarComponentes()
        {
            BackColor = Color.White;
            System.Drawing.Rectangle area = Screen.PrimaryScreen.WorkingArea;
            // Tamano de diseno: las anclas se ponen con el. Al leer el horario
            // (AjustarTamano) la ventana se queda con lo justo para sus horas y
            // sus dias; no a pantalla completa, que para un solo profesor sobra.
            ClientSize = new Size(Math.Min(1180, area.Width - 80), Math.Min(700, area.Height - 80));
            // Lo minimo: una hora con su alto minimo (un horario de una sola clase
            // de una hora tambien se ajusta). Arriba 110 de titulo, abajo 58 de
            // botones, 2 de borde y la cabecera de la cuadricula con su margen.
            MinimumSize = SizeFromClientSize(new Size(Math.Min(720, area.Width - 40),
                110 + 58 + 2 + CuadriculaSemanal.AltoCabecera + 6 + CuadriculaSemanal.AltoHora));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            // El icono de DigBit: el del panel del administrador, como en el del profesor.
            Icon = (Icon)new ComponentResourceManager(typeof(PrincipalAdministrador)).GetObject("$this.Icon");
            Text = "Mi horario";

            int ancho = ClientSize.Width;
            int alto = ClientSize.Height;

            lblTitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 18F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(28, 18),
                Text = "Mi horario"
            };

            lblSubtitulo = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10.5F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(30, 55),
                Text = ""
            };

            // La semana que se ve, con el selector de Horarios; a su derecha, en su
            // renglon, la ayuda (ColocarAyuda), que dice tambien por que no se abre
            // una clase que aun no empieza (MostrarAviso).
            selectorSemana = new SelectorSemana { Location = new Point(30, 79) };
            selectorSemana.Cambio += CambioDeSemana;
            selectorSemana.SizeChanged += (s, e) => ColocarAyuda();

            lblAyuda = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = GrisAyuda,
                Location = new Point(30, 84),
                Size = new Size(400, 18),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = AyudaNormal
            };
            avisoTimer = new Timer { Interval = SegundosDelAviso * 1000 };
            avisoTimer.Tick += (s, e) => QuitarAviso();
            FormClosed += (s, e) => avisoTimer.Dispose();

            // Su codigo de clase en grande, arriba a la derecha (lo pone CargarHorario).
            cuadroCodigo = new CuadroCodigo
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(ancho - 30 - 160, 10),
                Size = new Size(160, 70),
                Visible = false
            };

            contenedorSemana = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(30, 110),
                Size = new Size(ancho - 60, alto - 168),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Cada clase dice su materia, su grupo y en que laboratorio es (son de un
            // solo profesor), sin repetir las horas.
            cuadricula = new CuadriculaSemanal { Dock = DockStyle.Fill, SoloLectura = true, DeUnProfesor = true };
            cuadricula.FranjaClic += cuadricula_FranjaClic;
            cuadricula.ExcepcionClic += cuadricula_ExcepcionClic;
            contenedorSemana.Controls.Add(cuadricula);

            btnActualizar = CrearBoton("Actualizar", new Point(ancho - 220, alto - 46), new Size(110, 34));
            btnActualizar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnActualizar.Click += (s, e) => CargarHorario();

            btnCerrar = CrearBoton("Cerrar", new Point(ancho - 100, alto - 46), new Size(70, 34));
            btnCerrar.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCerrar.Click += (s, e) => Close();

            // Que quiere decir el aspecto de cada clase: abajo a la izquierda, a media
            // altura de los botones (el color del profesor lo pone CargarHorario).
            leyenda = new LeyendaDelHorario { Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            leyenda.Location = new Point(30, btnActualizar.Top + (btnActualizar.Height - leyenda.Height) / 2);

            Controls.Add(cuadroCodigo);
            Controls.Add(lblTitulo);
            Controls.Add(lblSubtitulo);
            Controls.Add(selectorSemana);
            Controls.Add(lblAyuda);
            Controls.Add(contenedorSemana);
            Controls.Add(leyenda);
            Controls.Add(btnActualizar);
            Controls.Add(btnCerrar);

            Resize += (s, e) => ColocarAyuda();
            ColocarAyuda();

            CancelButton = btnCerrar;
        }

        /// <summary>F5 lo vuelve a leer, como Actualizar (Esc ya cierra: CancelButton).</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                CargarHorario();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// La ayuda a la derecha del selector de semana, en su renglon, hasta
        /// donde empieza el codigo en grande (o el borde): lo que no quepa
        /// termina en "...".
        /// </summary>
        private void ColocarAyuda()
        {
            if (selectorSemana == null || lblAyuda == null || cuadroCodigo == null)
            {
                return;
            }

            int izquierda = selectorSemana.Left + selectorSemana.PreferredSize.Width + 14;
            int derecha = cuadroCodigo.ConCodigo ? cuadroCodigo.Left - 12 : ClientSize.Width - 30;
            int alto = lblAyuda.Font.Height + 4;
            lblAyuda.Bounds = new System.Drawing.Rectangle(izquierda, selectorSemana.Top + (selectorSemana.PreferredSize.Height - alto) / 2,
                Math.Max(40, derecha - izquierda), alto);
        }

        /// <summary>
        /// Un aviso en naranja en el renglon de la ayuda durante unos segundos
        /// (SegundosDelAviso); despues vuelve la ayuda de siempre.
        /// </summary>
        private void MostrarAviso(string texto)
        {
            lblAyuda.ForeColor = NaranjaAviso;
            lblAyuda.Text = texto;
            avisoTimer.Stop();
            avisoTimer.Start();
        }

        private void QuitarAviso()
        {
            avisoTimer.Stop();
            lblAyuda.ForeColor = GrisAyuda;
            lblAyuda.Text = AyudaNormal;
        }

        private Button CrearBoton(string texto, Point posicion, Size tamano)
        {
            Button boton = new Button
            {
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = posicion,
                Size = tamano,
                Text = texto,
                UseVisualStyleBackColor = false
            };
            boton.FlatAppearance.BorderColor = Color.DarkGreen;
            boton.FlatAppearance.BorderSize = 2;
            return boton;
        }

        // =====================================================================
        // Horario del profesor
        // =====================================================================

        private void CargarProfesor()
        {
            string numero = Datos_User.getUser();
            try
            {
                idProfesor = consultas.ObtenerIdPorMatricula(numero);
                nombreProfesor = consultas.MostrarNombreProfesor(numero);
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro. Sin dueno, como
                // antes: al abrir, esto pasa en el constructor, sin ventana aun.
                Log.Error("BitacoraMisClases.CargarProfesor", ex);
                MessageBox.Show(MensajeDeFallo.Explicar("No se pudieron leer tus datos", ex, AvisoDeFalloDocente.SiSigue), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            subtituloBase = string.IsNullOrWhiteSpace(nombreProfesor)
                ? "Tu horario de la semana."
                : nombreProfesor + ": tu horario de la semana.";
            lblSubtitulo.Text = subtituloBase;
        }

        /// <summary>
        /// Lee todo otra vez (al abrir, con Actualizar, con F5 y, hasta que salga
        /// bien una vez, al cambiar de semana): la hora del servidor, sus clases,
        /// los colores, los modos y, en Mi horario, quien es (si aun no se sabe) y
        /// lo que eligio para sus clases; las franjas de cada clase se vuelven a
        /// pedir cuando una semana las necesite. Al abrir, la semana de hoy (o la
        /// siguiente si hoy es domingo); despues, la que se esta viendo.
        /// </summary>
        private void CargarHorario()
        {
            // Mi horario: quien es, al abrir o si entonces no se pudo leer.
            if (!soloVer && idProfesor <= 0)
            {
                CargarProfesor();
            }

            if (idProfesor <= 0)
            {
                return;
            }

            try
            {
                ahoraLeida = consultas.AhoraServidor();
                leidaEnUtc = DateTime.UtcNow;
                clases = datos.ObtenerClasesDeProfesor(idProfesor);
                franjasDeClase.Clear();

                // Su color, el mismo que le ve el administrador en Horarios y en el panel.
                RepartoDeColores reparto = datos.DatosDeColores();
                ColoresProfesor.Repartir(reparto.Orden, reparto.Elegidos);
                leyenda.IdProfesor = idProfesor;

                // En los laboratorios sin codigo fijo, el suyo no lo usa nadie: no se pinta.
                Dictionary<int, ModoCodigo> modos = datos.LeerModos();
                sinCodigoFijo = modos == null ? new HashSet<int>()
                    : new HashSet<int>(modos.Where(m => m.Value != ModoCodigo.Fijo).Select(m => m.Key));
                cuadricula.LaboratoriosSinCodigoFijo = sinCodigoFijo;

                if (personal)
                {
                    LeerPersonal();
                }

                selectorSemana.Hoy = ahoraLeida.Date;
                DateTime estaSemana = SelectorSemana.SemanaActual(ahoraLeida.Date);
                if (!semanaElegida)
                {
                    selectorSemana.Lunes = estaSemana;
                }

                horasDeHoy = HorasDeSemana.De(FranjasDeLaSemana(estaSemana).Where(f => SeDaEnLaSemana(f, estaSemana)));
                MostrarSemana();
                horarioLeido = true;
            }
            catch (Exception ex)
            {
                // Sin dueno, como antes: al abrir, esto pasa en el constructor.
                Log.Error("BitacoraMisClases.CargarHorario", ex);
                MessageBox.Show(MensajeDeFallo.Explicar(soloVer ? "No se pudo leer su horario" : "No se pudo leer tu horario", ex, AvisoDeFalloDocente.SiSigue),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Otra semana en el selector (flechas o "Esta semana"): solo se leen sus
        /// excepciones y las franjas que falten. Si aun no ha salido bien una
        /// lectura completa (fallo al abrir), se repite entera, ya en esa semana.
        /// </summary>
        private void CambioDeSemana()
        {
            semanaElegida = true;
            QuitarAviso();
            if (!horarioLeido)
            {
                CargarHorario();
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                MostrarSemana();
            }
            catch (Exception ex)
            {
                Log.Error("BitacoraMisClases.CambioDeSemana", ex);
                MessageBox.Show(this, MensajeDeFallo.Explicar(soloVer ? "No se pudo leer su horario de esa semana" : "No se pudo leer tu horario de esa semana",
                        ex, AvisoDeFalloDocente.SiSigue),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// La semana del selector: las franjas que se dan cada dia (de una franja
        /// cambiada desde una fecha, la vieja o la nueva, no las dos), sus
        /// cancelaciones y sesiones extra (tambien las ya pasadas), las horas, el
        /// codigo y el subtitulo. Lanza si no se puede leer.
        /// </summary>
        private void MostrarSemana()
        {
            DateTime lunes = selectorSemana.Lunes;
            DateTime ahora = Ahora();
            List<FranjaInfo> franjas = FranjasDeLaSemana(lunes);

            // Solo las excepciones de sus clases: cancelaciones y sesiones extra.
            HashSet<int> misClases = new HashSet<int>(clases.Select(c => c.Id));
            List<ExcepcionInfo> excepciones = datos.ObtenerExcepciones(lunes, lunes.AddDays(6), 0)
                .Where(x => misClases.Contains(x.ClaseId))
                .ToList();
            foreach (ExcepcionInfo x in excepciones)
            {
                x.Codigo = TarjetaCodigo.Mayusculas(x.Codigo);
            }

            // Cada columna es la fecha de esa semana: la de hoy dice "Hoy, ...".
            cuadricula.Ahora = ahora;
            cuadricula.Hoy = ahora.Date;
            cuadricula.Semana = lunes;
            List<FranjaInfo> semana = franjas.Where(cuadricula.VigenteEnSuColumna).ToList();

            // Su codigo de clase, una sola vez (los cuadros ya no lo llevan): el de
            // sus clases en laboratorios que entran con el codigo fijo. Uno (db/09),
            // en grande arriba a la derecha; varios (sin la 09), en el subtitulo.
            // Sin los codigos de los profesores (SinCodigos), ninguno: el cuadro se oculta.
            // Los de las clases de esa semana; una semana sin clases, los que ya se leyeron.
            List<string> codigos = cuadricula.SinCodigos ? new List<string>()
                : (semana.Count > 0 ? semana : franjasDeClase.Values.SelectMany(l => l))
                    .Where(f => !sinCodigoFijo.Contains(f.LaboratorioId) && !string.IsNullOrEmpty(f.Codigo))
                    .Select(f => f.Codigo).Distinct().ToList();
            cuadroCodigo.Mostrar(soloVer ? "Su código de clase" : "Tu código de clase", codigos.Count == 1 ? codigos[0] : null);
            // Grande, pero sin bajar de y = 104: la cuadricula empieza en 110.
            cuadroCodigo.Top = Math.Max(0, Math.Min(10, contenedorSemana.Top - 6 - cuadroCodigo.Height));
            lblSubtitulo.Text = subtituloBase + (codigos.Count <= 1 ? ""
                : (soloVer ? " Sus códigos de clase: " : " Tus códigos de clase: ") + string.Join(", ", codigos.Take(3)) + (codigos.Count > 3 ? "..." : "") + ".");
            MostrarSoloLoSuyo(semana, excepciones);
            if (personal)
            {
                // Antes que Franjas, que rehace las ayudas de los cuadros.
                PonerPersonal();
            }

            cuadricula.Franjas = semana;
            cuadricula.Excepciones = excepciones;
            if (personal)
            {
                PonerColorDeMuestra();
            }

            if (!clases.Any(c => c.Activa))
            {
                lblSubtitulo.Text = soloVer ? "Todavía no tiene clases en el horario. Se le asignan en Horarios."
                    : "Todavía no tienes clases en el horario: te las asigna la administración de DigBit.";
            }
            else if (semana.Count == 0 && !excepciones.Any(cuadricula.ExtraSeDibuja))
            {
                // Tiene horario, pero nada esa semana (sus clases aun no empiezan o ya terminaron).
                string cual = lunes == SelectorSemana.SemanaActual(ahoraLeida.Date) ? " Esta semana" : " Esa semana";
                lblSubtitulo.Text += cual + (soloVer ? " no tiene clases." : " no tienes clases.");
            }

            // Al estrecharla, el codigo (anclado a la derecha) no debe tapar el
            // titulo, el subtitulo ni la ayuda, ni los botones la leyenda: el
            // minimo les deja sitio.
            int minimo = Math.Min(Screen.PrimaryScreen.WorkingArea.Width - 40, SizeFromClientSize(new Size(AnchoSinTaparse(), 100)).Width);
            if (minimo > MinimumSize.Width)
            {
                MinimumSize = new Size(minimo, MinimumSize.Height);
            }

            ColocarAyuda();

            // Leido antes de abrirse: al abrir (OnLoad) la ventana se ajusta a esto.
            if (!Visible)
            {
                leidoAlAbrir = true;
            }
        }

        /// <summary>La hora del servidor, estimada con la de la ultima lectura (sin leerla aun, la del equipo).</summary>
        private DateTime Ahora()
        {
            return ahoraLeida == DateTime.MinValue ? DateTime.Now : ahoraLeida + (DateTime.UtcNow - leidaEnUtc);
        }

        /// <summary>
        /// Las franjas de sus clases activas cuya vigencia toca esa semana (de
        /// lunes a sabado). Las de cada clase se piden una vez (ObtenerFranjasDeClase:
        /// todas, tambien las que ya terminaron o aun no empiezan, db/20) y se
        /// guardan hasta la siguiente lectura completa (CargarHorario). El codigo,
        /// en mayusculas, como se dicta.
        /// </summary>
        private List<FranjaInfo> FranjasDeLaSemana(DateTime lunes)
        {
            DateTime sabado = lunes.AddDays(CuadriculaSemanal.Dias - 1);
            List<FranjaInfo> franjas = new List<FranjaInfo>();
            foreach (ClaseInfo clase in clases.Where(c => c.Activa && c.VigenteDesde.Date <= sabado && c.VigenteHasta.Date >= lunes))
            {
                List<FranjaInfo> suyas;
                if (!franjasDeClase.TryGetValue(clase.Id, out suyas))
                {
                    suyas = datos.ObtenerFranjasDeClase(clase.Id);
                    foreach (FranjaInfo franja in suyas)
                    {
                        franja.Codigo = TarjetaCodigo.Mayusculas(franja.Codigo);
                    }

                    franjasDeClase[clase.Id] = suyas;
                }

                franjas.AddRange(suyas);
            }

            return franjas;
        }

        /// <summary>Si la franja se da en su dia de esa semana (como CuadriculaSemanal.VigenteEnSuColumna, para otra semana que la que se ve).</summary>
        private static bool SeDaEnLaSemana(FranjaInfo franja, DateTime lunes)
        {
            DateTime fecha = lunes.AddDays(franja.DiaSemana - 1);
            return franja.Activa && franja.DiaSemana >= 1 && franja.DiaSemana <= CuadriculaSemanal.Dias
                && fecha >= franja.VigenteDesde.Date && fecha <= franja.VigenteHasta.Date;
        }

        /// <summary>
        /// Lo que el profesor eligio para sus clases (db/19) y las imagenes que aun
        /// no estan en memoria, como todo aqui en el hilo de la ventana. Si la
        /// consulta falla, se queda lo que ya habia (el panel lo vuelve a leer).
        /// </summary>
        private void LeerPersonal()
        {
            int version = PersonalizacionClases.Version;
            LecturaPersonal leida;
            try
            {
                using (MySql.Data.MySqlClient.MySqlConnection conexion = new Conexion().GetConexion())
                {
                    leida = PersonalizacionClasesDatos.Leer(conexion, idProfesor);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Mi horario: leer la personalizacion de las clases del profesor " + idProfesor, ex);
                return;
            }

            Dictionary<string, byte[]> imagenes = null;
            try
            {
                imagenes = PersonalizacionClases.TraerImagenes(idProfesor, leida);
            }
            catch (Exception ex)
            {
                // Sin ellas van las iniciales.
                Log.Error("Mi horario: traer las imagenes de las clases del profesor " + idProfesor, ex);
            }

            PersonalizacionClases.Publicar(leida, imagenes, version);
        }

        /// <summary>El gancho de la cuadricula: con db/19, el color, el circulo y el nombre corto de cada clase; sin el, como siempre.</summary>
        private void PonerPersonal()
        {
            cuadricula.PersonalDe = PersonalizacionClases.Disponible ? (Func<int, AspectoClase>)PersonalizacionClases.De : null;
        }

        /// <summary>
        /// Las muestras de la leyenda con el color de su primera clase de la semana
        /// (db/19), para que se parezcan a lo que ve; sin el, el del profesor.
        /// </summary>
        private void PonerColorDeMuestra()
        {
            AspectoClase primera = cuadricula.PersonalDe == null ? null
                : cuadricula.Franjas.OrderBy(f => f.DiaSemana).ThenBy(f => f.Inicio)
                    .Select(f => PersonalizacionClases.De(f.ClaseId)).FirstOrDefault(a => a != null);
            leyenda.ColorDeMuestra = primera == null ? (Color?)null : primera.Color;
        }

        /// <summary>Cambio lo que el profesor eligio para sus clases (aqui o en otra ventana): se repinta.</summary>
        private void PersonalizacionCambio()
        {
            if (IsDisposed)
            {
                return;
            }

            PonerPersonal();
            PonerColorDeMuestra();
            cuadricula.Invalidate();
        }

        /// <summary>
        /// La cuadricula solo con las horas en que tiene algo que se vea (sus
        /// clases de lunes a sabado de esa semana y las sesiones extra que la
        /// cuadricula pinta) mas las de la semana de hoy (horasDeHoy: al cambiar
        /// de semana no se encoge), y sin el sabado si ninguna de las dos lo usa.
        /// Sin nada, el dia entero. Necesita cuadricula.Semana ya puesta.
        /// </summary>
        private void MostrarSoloLoSuyo(List<FranjaInfo> franjas, List<ExcepcionInfo> excepciones)
        {
            List<FranjaInfo> visibles = franjas.Where(f => f.DiaSemana >= 1 && f.DiaSemana <= CuadriculaSemanal.Dias).ToList();
            List<ExcepcionInfo> extras = excepciones.Where(cuadricula.ExtraSeDibuja).ToList();
            List<TimeSpan> inicios = visibles.Select(f => f.Inicio).Concat(extras.Select(x => x.Inicio)).ToList();
            List<TimeSpan> fines = visibles.Select(f => f.Fin).Concat(extras.Select(x => x.Fin)).ToList();
            bool sabado = visibles.Any(f => f.DiaSemana == CuadriculaSemanal.Dias) || extras.Any(x => x.Fecha.DayOfWeek == DayOfWeek.Saturday);
            if (horasDeHoy != null)
            {
                inicios.Add(horasDeHoy.Desde);
                fines.Add(horasDeHoy.Hasta);
                sabado |= horasDeHoy.Sabado;
            }

            if (inicios.Count == 0)
            {
                cuadricula.MostrarHoras(EstiloHorarios.HoraApertura, EstiloHorarios.HoraCierre);
            }
            else
            {
                cuadricula.MostrarHoras((int)Math.Floor(inicios.Min().TotalHours), (int)Math.Ceiling(fines.Max().TotalHours));
            }

            cuadricula.SabadoVisible = sabado;
        }

        /// <summary>De que hora a que hora tiene clases una semana, y si da algo el sabado.</summary>
        private sealed class HorasDeSemana
        {
            public TimeSpan Desde;
            public TimeSpan Hasta;
            public bool Sabado;

            /// <summary>Las de esas franjas (de lunes a sabado), o null si no hay ninguna.</summary>
            public static HorasDeSemana De(IEnumerable<FranjaInfo> franjas)
            {
                List<FranjaInfo> visibles = franjas.Where(f => f.DiaSemana >= 1 && f.DiaSemana <= CuadriculaSemanal.Dias).ToList();
                return visibles.Count == 0 ? null : new HorasDeSemana
                {
                    Desde = visibles.Min(f => f.Inicio),
                    Hasta = visibles.Max(f => f.Fin),
                    Sabado = visibles.Any(f => f.DiaSemana == CuadriculaSemanal.Dias)
                };
            }
        }

        /// <summary>
        /// Al abrirse, si el horario se leyo: la ventana justa para lo que se ve,
        /// en la pantalla donde se abre (la de quien la abre) y centrada en ella.
        /// Antes de base.OnLoad, que centra las modales con el tamano nuevo; las
        /// demas ya tienen sitio y se centran despues.
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            Screen pantalla = Owner != null ? Screen.FromControl(Owner) : Screen.FromPoint(Cursor.Position);
            if (leidoAlAbrir)
            {
                AjustarTamano(pantalla.WorkingArea);
            }

            base.OnLoad(e);
            if (leidoAlAbrir && !Modal)
            {
                CenterToScreen();
            }
        }

        /// <summary>
        /// La ventana justa para lo que se ve: cada hora con un alto en el que la
        /// clase se lee con letra grande (58 px), cada dia con su ancho (260), y
        /// sitio para los textos de arriba con el codigo y para la leyenda con los
        /// botones; como mucho, el area de trabajo de esa pantalla (si no caben,
        /// la cuadricula se desplaza).
        /// </summary>
        private void AjustarTamano(System.Drawing.Rectangle area)
        {
            // Con sitio para el laboratorio entero, la materia y el grupo de cada
            // clase (con 165 de ancho, "Bases de Datos" ya salia cortada).
            const int AltoFila = 58;
            const int AnchoDia = 260;

            // Lo que rodea a la cuadricula: arriba el titulo, abajo los botones
            // (contenedorSemana mide alto - 168 y empieza en 110); el borde, y el
            // margen que deja AjustarFilas bajo la ultima hora.
            int filas = cuadricula.Rows.Count;
            int dias = cuadricula.Columns.Cast<DataGridViewColumn>().Count(c => c.Visible) - 1;
            int alto = contenedorSemana.Top + 58 + 2 + CuadriculaSemanal.AltoCabecera + 6 + filas * AltoFila;
            int ancho = 60 + 2 + CuadriculaSemanal.AnchoHoras + dias * AnchoDia;
            ancho = Math.Max(ancho, Math.Max(DerechaDeLosTextos() + 30, AnchoSinTaparse()));

            ClientSize = new Size(Math.Min(ancho, area.Width - 40), Math.Min(alto, area.Height - 40));
        }

        /// <summary>Hasta donde llegan el titulo, el subtitulo y, en su renglon, el selector de semana con la ayuda entera.</summary>
        private int DerechaDeLosTextos()
        {
            int derecha = Math.Max(lblTitulo.Left + lblTitulo.PreferredWidth, lblSubtitulo.Left + lblSubtitulo.PreferredWidth);
            int ayuda = TextRenderer.MeasureText(AyudaNormal, lblAyuda.Font).Width;
            return Math.Max(derecha, selectorSemana.Left + selectorSemana.PreferredSize.Width + 14 + ayuda);
        }

        /// <summary>
        /// El ancho (de area cliente) por debajo del cual algo se tapa: el codigo
        /// en grande, anclado a la derecha y alto hasta la ayuda, con los textos de
        /// arriba; y la leyenda, abajo a la izquierda, con los botones.
        /// </summary>
        private int AnchoSinTaparse()
        {
            int abajo = leyenda.Right + 24 + (ClientSize.Width - btnActualizar.Left);
            return cuadroCodigo.ConCodigo ? Math.Max(abajo, DerechaDeLosTextos() + 40 + cuadroCodigo.Width + 30) : abajo;
        }

        // =====================================================================
        // Abrir la bitacora de una clase
        // =====================================================================

        /// <summary>Una clase del horario: su bitacora del dia de su columna (la fecha de la semana que se ve).</summary>
        private void cuadricula_FranjaClic(FranjaInfo franja)
        {
            if (franja == null)
            {
                return;
            }

            DateTime fecha = cuadricula.FechaDeColumna(franja.DiaSemana);
            ExcepcionInfo cancelacion = cuadricula.CancelacionDe(franja);
            AbrirBitacora(franja.Id, null, DescripcionDeClase(franja.LaboratorioId, franja.Codigo, franja.Materia, franja.Grupo),
                Mayuscula(DiaYFecha(fecha)) + ", de " + HorariosDatos.Hora(franja.Inicio) + " a " + HorariosDatos.Hora(franja.Fin) + ", " + franja.Laboratorio
                    + (cancelacion != null ? "; se canceló ese día" : ""),
                fecha, franja.Inicio, cancelacion);
        }

        private void cuadricula_ExcepcionClic(ExcepcionInfo extra)
        {
            if (extra == null)
            {
                return;
            }

            AbrirBitacora(null, extra.Id, DescripcionDeClase(extra.LaboratorioId, extra.Codigo, extra.Materia, extra.Grupo),
                "Sesión extra del " + DiaYFecha(extra.Fecha) + ", de " + HorariosDatos.Hora(extra.Inicio) + " a "
                    + HorariosDatos.Hora(extra.Fin) + ", " + extra.Laboratorio,
                extra.Fecha, extra.Inicio, null);
        }

        /// <summary>
        /// "UNICO7 - Programacion Web - ISC-7001": sin el codigo en los
        /// laboratorios que no entran con el, ni para quien no ve los codigos de
        /// los profesores (el encargado de laboratorio).
        /// </summary>
        private string DescripcionDeClase(int idLaboratorio, string codigo, string materia, string grupo)
        {
            bool conCodigo = !cuadricula.SinCodigos && !sinCodigoFijo.Contains(idLaboratorio) && !string.IsNullOrEmpty(codigo);
            return (conCodigo ? codigo + " - " : "") + materia + " - " + grupo;
        }

        /// <summary>
        /// La bitacora de esa clase ese dia en su propia ventana (BitacoraSesion
        /// solo lee: vale igual para el administrador y el encargado). Si aun no
        /// empieza (se registra desde 15 minutos antes) no hay nada que ver: la
        /// ayuda dice desde cuando, o que se cancelo, unos segundos.
        /// </summary>
        private void AbrirBitacora(int? idFranja, int? idExcepcion, string clase, string horario, DateTime fecha, TimeSpan inicio,
            ExcepcionInfo cancelacion)
        {
            DateTime ahora = Ahora();
            DateTime desde = fecha.Date + inicio - TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            if (ahora < desde)
            {
                MostrarAviso(cancelacion != null
                    ? "Esa clase se canceló ese día" + (string.IsNullOrWhiteSpace(cancelacion.Motivo) ? "." : ": " + cancelacion.Motivo.Trim() + ".")
                    : "Esa clase aún no empieza: se registra desde las " + desde.ToString("HH:mm")
                        + (desde.Date == ahora.Date ? "." : " del " + DiaYFecha(desde) + "."));
                return;
            }

            QuitarAviso();
            using (BitacoraSesion bitacora = new BitacoraSesion(idFranja, idExcepcion, clase, horario, nombreProfesor, fecha.Date))
            {
                bitacora.ShowDialog(this);
            }
        }

        /// <summary>"jueves 16/10".</summary>
        private static string DiaYFecha(DateTime fecha)
        {
            return HorariosDatos.NombresDia[((int)fecha.DayOfWeek + 6) % 7 + 1].ToLowerInvariant() + " " + fecha.ToString("dd/MM");
        }

        private static string Mayuscula(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpperInvariant(texto[0]) + texto.Substring(1);
        }
    }

    /// <summary>
    /// El codigo de clase en grande (es lo que dicta a sus alumnos), en
    /// mayusculas y en la letra del panel del profesor, que no confunde la O con
    /// el 0 ni la I con el 1; deletreado al pasar el raton. A su derecha,
    /// "Escuchar el codigo" compacto (la bocina): lo lee en voz alta
    /// (DictadoCodigo; nunca en el kiosco).
    /// </summary>
    internal sealed class CuadroCodigo : Control
    {
        private const int Aire = 4;
        // Entre el codigo y la bocina.
        private const int AntesDeEscuchar = 14;
        private static readonly Color Fondo = Color.FromArgb(240, 248, 240);
        private readonly DrawingFont fuenteTitulo = new DrawingFont("Century Gothic", 9F, FontStyle.Bold);
        private readonly DrawingFont fuenteCodigo = new DrawingFont("Consolas", 36F, FontStyle.Bold);
        private readonly ToolTip ayuda = new ToolTip();
        private readonly BotonEscuchar btnEscuchar = new BotonEscuchar { Compacto = true, FondoDetras = Fondo, Visible = false };
        private string titulo = "";
        private string codigo = "";
        private string textoAyuda;

        public CuadroCodigo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            Controls.Add(btnEscuchar);
        }

        /// <summary>Si tiene codigo que ensenar (Visible no sirve: es falso mientras la ventana no se ve).</summary>
        public bool ConCodigo
        {
            get { return codigo.Length > 0; }
        }

        /// <summary>Con la bocina: con codigo y fuera del kiosco.</summary>
        private bool ConEscuchar
        {
            get { return ConCodigo && DictadoCodigo.Permitido; }
        }

        /// <summary>Pone el codigo, en mayusculas (sin el, se oculta), y se ajusta a el sin despegarse del borde derecho.</summary>
        public void Mostrar(string titulo, string codigo)
        {
            this.titulo = titulo ?? "";
            this.codigo = TarjetaCodigo.Mayusculas(codigo);
            int derecha = Right;
            Size medidaTitulo;
            Size medidaCodigo;
            // Con el contexto de la pantalla, como al pintar: sin el, MeasureText no
            // hace caso de NoPadding y cada letra mide casi el doble.
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                medidaTitulo = TextRenderer.MeasureText(g, this.titulo, fuenteTitulo);
                medidaCodigo = MedirCodigo(g);
            }

            Size = new Size(Math.Max(medidaTitulo.Width, AnchoGrupo(medidaCodigo.Width)) + 32, 6 + medidaTitulo.Height + 2 + medidaCodigo.Height + 6);
            Left = derecha - Width;
            Visible = ConCodigo;
            textoAyuda = ConCodigo ? TarjetaCodigo.Deletrear(this.codigo) : null;
            ayuda.SetToolTip(this, textoAyuda);

            // La bocina a la derecha del codigo y centrada en su alto (OnPaint centra los dos juntos).
            btnEscuchar.Codigo = this.codigo;
            btnEscuchar.Location = new Point((Width - AnchoGrupo(medidaCodigo.Width)) / 2 + medidaCodigo.Width + AntesDeEscuchar - BotonEscuchar.MargenFoco,
                6 + medidaTitulo.Height + 2 + (medidaCodigo.Height - btnEscuchar.Height) / 2);
            btnEscuchar.Visible = ConEscuchar;
            Invalidate();
        }

        /// <summary>El codigo y, si va, la bocina (lo que se ve de ella) con su aire: lo que se centra.</summary>
        private int AnchoGrupo(int anchoCodigo)
        {
            return anchoCodigo + (ConEscuchar ? AntesDeEscuchar + btnEscuchar.Width - 2 * BotonEscuchar.MargenFoco : 0);
        }

        /// <summary>
        /// Sobre la bocina apagada el raton llega aqui (Windows no se lo da a ella):
        /// entonces su ayuda (la ensena ella misma) y no el deletreo.
        /// </summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool sobreLaBocina = btnEscuchar.Visible && !btnEscuchar.Enabled && btnEscuchar.Bounds.Contains(e.Location);
            string texto = !ConCodigo || sobreLaBocina ? null : TarjetaCodigo.Deletrear(codigo);
            if (texto != textoAyuda)
            {
                textoAyuda = texto;
                ayuda.SetToolTip(this, texto);
            }
        }

        private Size MedirCodigo(Graphics g)
        {
            int ancho = 0;
            int alto = 0;
            foreach (char c in codigo)
            {
                Size medida = TextRenderer.MeasureText(g, c.ToString(), fuenteCodigo, Size.Empty, TextFormatFlags.NoPadding);
                ancho += medida.Width;
                alto = Math.Max(alto, medida.Height);
            }

            return new Size(ancho + Aire * Math.Max(0, codigo.Length - 1), alto);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath forma = PanelLaboratoriosAhora.Redondeado(new Rectangle(1, 1, Width - 3, Height - 3), 10))
            using (SolidBrush fondo = new SolidBrush(Fondo))
            using (Pen borde = new Pen(Color.DarkGreen, 2f))
            {
                g.FillPath(fondo, forma);
                g.DrawPath(borde, forma);
            }

            int altoTitulo = TextRenderer.MeasureText(g, titulo, fuenteTitulo).Height;
            TextRenderer.DrawText(g, titulo, fuenteTitulo, new Rectangle(0, 6, Width, altoTitulo), Color.DarkGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            // Letra por letra, con aire entre ellas, como en el panel; con la bocina a su derecha, los dos centrados.
            int x = (Width - AnchoGrupo(MedirCodigo(g).Width)) / 2;
            foreach (char c in codigo)
            {
                TextRenderer.DrawText(g, c.ToString(), fuenteCodigo, new Point(x, 6 + altoTitulo + 2), Color.DarkGreen, TextFormatFlags.NoPadding);
                x += TextRenderer.MeasureText(g, c.ToString(), fuenteCodigo, Size.Empty, TextFormatFlags.NoPadding).Width + Aire;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
                fuenteTitulo.Dispose();
                fuenteCodigo.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// La leyenda del horario, abajo: que quiere decir el aspecto de una clase,
    /// cada caso con una muestra de 14x14 pintada como la pinta la cuadricula
    /// (CuadriculaSemanal_CellPainting y PintarExtraComoClase), con el color
    /// del profesor (en Mi horario con db/19, el de su primera clase: ColorDeMuestra).
    /// </summary>
    internal sealed class LeyendaDelHorario : Control
    {
        private const int Muestra = 14;
        private const int AntesDelTexto = 6;
        private const int EntreElementos = 18;
        // Se mide sin VerticalCenter: con el y SingleLine, MeasureText da la mitad de alto.
        private const TextFormatFlags Medida = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        private const TextFormatFlags Formato = Medida | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
        // Sin "Fuera de vigencia": el horario solo trae las clases que se dan esos
        // dias (CargarHorario), asi que ya no sale ninguna en gris.
        private static readonly string[] Textos = { "Sesión extra", "Cancelada ese día" };
        // Copiados de CuadriculaSemanal: el marco de las sesiones extra (ColorExtra)
        // y el rayado de las canceladas (ColorCancelada).
        private static readonly Color ColorExtra = Color.FromArgb(255, 140, 0);
        private static readonly Color ColorCancelada = Color.FromArgb(110, 110, 110);
        private readonly DrawingFont fuente = new DrawingFont("Century Gothic", 9F);
        private int idProfesor;
        private Color? colorDeMuestra;

        public LeyendaDelHorario()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            // Solo se mira: sin foco.
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.White;

            // Del tamano de lo que pinta. Con el contexto de la pantalla, como al
            // pintar: sin el, MeasureText no hace caso de NoPadding.
            int ancho = 0;
            int alto = Muestra;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                foreach (string texto in Textos)
                {
                    Size medida = TextRenderer.MeasureText(g, texto, fuente, Size.Empty, Medida);
                    ancho += Muestra + AntesDelTexto + medida.Width + EntreElementos;
                    alto = Math.Max(alto, medida.Height);
                }
            }

            Size = new Size(ancho - EntreElementos, alto);
        }

        /// <summary>El profesor del horario: las muestras de sus clases van con su color (ColoresProfesor).</summary>
        public int IdProfesor
        {
            get { return idProfesor; }
            set
            {
                idProfesor = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Mi horario con db/19: el color de las muestras, el de su primera clase de
        /// la semana (cada clase tiene el suyo). Null: el del profesor, como siempre.
        /// </summary>
        public Color? ColorDeMuestra
        {
            get
            {
                return colorDeMuestra;
            }

            set
            {
                if (colorDeMuestra == value)
                {
                    return;
                }

                colorDeMuestra = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Color profesor = colorDeMuestra ?? ColoresProfesor.De(idProfesor);

            // Sesion extra: el color del profesor dentro del marco naranja de 2 px.
            Rectangle cuadro = CuadroEn(0);
            using (SolidBrush marco = new SolidBrush(ColorExtra))
            using (SolidBrush fondo = new SolidBrush(profesor))
            {
                g.FillRectangle(marco, cuadro);
                g.FillRectangle(fondo, Rectangle.Inflate(cuadro, -2, -2));
            }

            // Cancelada ese dia: la clase con su borde, como en la cuadricula, y el rayado gris encima.
            cuadro = CuadroEn(Escribir(g, cuadro, Textos[0]));
            using (SolidBrush fondo = new SolidBrush(profesor))
            using (Pen borde = new Pen(ControlPaint.Dark(profesor, 0.15f)))
            using (HatchBrush rayas = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(150, ColorCancelada), Color.FromArgb(60, Color.White)))
            {
                g.FillRectangle(fondo, cuadro);
                g.DrawRectangle(borde, cuadro.X, cuadro.Y, cuadro.Width - 1, cuadro.Height - 1);
                g.FillRectangle(rayas, cuadro);
            }

            Escribir(g, cuadro, Textos[1]);
        }

        /// <summary>La muestra que empieza en x, centrada en alto.</summary>
        private Rectangle CuadroEn(int x)
        {
            return new Rectangle(x, (Height - Muestra) / 2, Muestra, Muestra);
        }

        /// <summary>El texto a la derecha de su muestra; devuelve donde empieza la siguiente.</summary>
        private int Escribir(Graphics g, Rectangle cuadro, string texto)
        {
            int x = cuadro.Right + AntesDelTexto;
            int ancho = TextRenderer.MeasureText(g, texto, fuente, Size.Empty, Medida).Width;
            TextRenderer.DrawText(g, texto, fuente, new Rectangle(x, 0, ancho, Height), Color.FromArgb(80, 80, 80), Formato);
            return x + ancho + EntreElementos;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fuente.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
