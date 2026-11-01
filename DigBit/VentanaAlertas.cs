using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Alertas del administrador (AlertasDatos): clases en que nadie registro
    /// bitacora (el profesor no vino o no pidio registrar), clases en curso en las
    /// que nadie ha registrado a los 20 minutos, fallas que reportaron los
    /// alumnos, por laboratorio y dia, y cambios del horario que hizo otra
    /// persona (db/21). Las nuevas (desde la ultima vez que las vio) van en
    /// negritas con un punto rojo. Desde una alerta se va a quien registro en la
    /// clase (BitacoraSesion), a sus bitacoras con fallas, al horario del
    /// laboratorio, a Faltas o al historial de cambios; una clase en que nadie
    /// registro se puede marcar como no dada aqui mismo, como en Faltas.
    /// </summary>
    public class VentanaAlertas : PantallaAdmin
    {
        private const string Hoy = "Hoy";
        private const string Semana = "Últimos 7 días";
        private const string Mes = "Últimos 30 días";
        private const string Todas = "Todas";
        private const string SinBitacoras = "Sin bitácoras";
        private const string Fallas = "Fallas reportadas";
        private const string Cambios = "Cambios de horario";
        private const string PorEnlazar = "Docentes por enlazar";
        private static readonly string[] DiasCortos = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };
        private static readonly Color FondoSinBitacoras = Color.FromArgb(253, 234, 231);
        private static readonly Color FondoEnCurso = Color.FromArgb(255, 243, 214);
        private static readonly Color FondoFallas = Color.FromArgb(255, 240, 214);
        // Un cambio no es un problema: un gris calido, que no se confunde con la fila elegida.
        private static readonly Color FondoCambio = Color.FromArgb(244, 242, 238);
        // Quien espera que lo enlacen con el SIIA: una tarea, no una falla.
        private static readonly Color FondoPorEnlazar = Color.FromArgb(230, 241, 251);
        private static readonly Color RojoNueva = Color.FromArgb(198, 40, 40);

        private readonly AlertasDatos datos = new AlertasDatos();
        private DateTime? vistasAntes;
        private readonly TextBox txtBuscar;
        private readonly ComboBox cboPeriodo;
        private readonly ComboBox cboTipo;
        private readonly Button btnVer;
        private readonly Button btnHorario;
        private readonly Button btnNoDada;
        private readonly DrawingFont negrita;
        // El historial de cambios abierto desde aqui (uno solo), o null.
        private VentanaHistorialCambios historial;
        // Quien marca una clase como no dada (creada_por de la cancelacion); 0 si no se supo.
        private int? idAdministrador;
        // La ultima lectura buena (vacia mientras no haya una). Si la ultima que se
        // intento fallo, se sigue ensenando esa, diciendo de que hora es.
        private ResultadoAlertas resultado = new ResultadoAlertas();
        private bool lecturaFallida;

        /// <summary>
        /// La lista ensena todo lo leido: se pueden dar por vistas hasta la hora
        /// del servidor de esa lectura (las da el panel principal). No se dispara
        /// con una lectura fallida, con filtros que esconden alguna ni con "Hoy".
        /// </summary>
        public event Action<DateTime> Leidas;

        /// <summary>Ver las bitacoras con fallas de ese laboratorio ese dia (las abre el panel principal).</summary>
        public event Action<Alerta> VerBitacoras;

        /// <summary>Ver el horario de un laboratorio (lo abre el panel principal).</summary>
        public event Action<int> VerHorario;

        /// <summary>Docentes con la lista de quienes entraron con el SIIA y esperan que los enlacen (la abre el panel principal).</summary>
        public event Action VerDocentesPorEnlazar;

        /// <param name="vistasAntes">Hasta cuando las habia visto: las de despues salen como nuevas.</param>
        // 1400 de ancho pedidos: a 1366x768 PantallaAdmin la deja en lo que cabe y
        // a 1920 el Detalle tiene mas sitio.
        public VentanaAlertas(DateTime? vistasAntes)
            : base("Alertas", "Clases en que nadie registró bitácora, fallas reportadas, cambios de horario que hizo otra persona y docentes por enlazar con el SIIA. F5 actualiza.",
                new Size(1400, 680))
        {
            this.vistasAntes = vistasAntes;
            negrita = new DrawingFont("Century Gothic", 9.75F, FontStyle.Bold);

            txtBuscar = CrearBuscador(new Point(Margen, 112), 300);
            ayuda.SetToolTip(txtBuscar, "Laboratorio, materia, grupo, docente, equipo o quién hizo un cambio");

            Controls.Add(EstiloHorarios.CrearEtiqueta("Periodo", new Point(350, 86)));
            cboPeriodo = EstiloHorarios.CrearCombo(new Point(350, 111), 170);
            cboPeriodo.Items.AddRange(new object[] { Hoy, Semana, Mes });
            cboPeriodo.SelectedItem = Semana;
            cboPeriodo.SelectedIndexChanged += (s, e) =>
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    Recargar(null);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            };
            Controls.Add(cboPeriodo);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Tipo", new Point(540, 86)));
            cboTipo = EstiloHorarios.CrearCombo(new Point(540, 111), 190);
            cboTipo.Items.AddRange(new object[] { Todas, SinBitacoras, Fallas, Cambios, PorEnlazar });
            cboTipo.SelectedItem = Todas;
            cboTipo.SelectedIndexChanged += (s, e) => Aplicar(null);
            ayuda.SetToolTip(cboTipo, "Sin bitácoras: desde que cada laboratorio usa DigBit.\n"
                + "Cambios de horario: franjas, clases, cancelaciones y sesiones extra que cambió otra persona.\n"
                + "Docentes por enlazar: entraron con su contraseña del SIIA y esperan que los enlaces con su cuenta.");
            Controls.Add(cboTipo);

            // Todo lo que cambio quien sea, con lo que decia antes (db/21): en su propia ventana.
            Button btnHistorial = EstiloHorarios.CrearBoton("Historial de cambios...", new Point(ClientSize.Width - Margen - 200, 106), new Size(200, 36));
            btnHistorial.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnHistorial.Click += (s, e) => AbrirHistorial(null);
            ayuda.SetToolTip(btnHistorial, "Quién cambió el horario, las clases, los laboratorios y las bitácoras, y cuándo (también lo que tú cambiaste).");
            Controls.Add(btnHistorial);

            CrearLista(156);
            grid.Columns.Add("nueva", "");
            grid.Columns.Add("fecha", "Día");
            grid.Columns.Add("hora", "Hora");
            grid.Columns.Add("laboratorio", "Laboratorio");
            grid.Columns.Add("alerta", "Alerta");
            grid.Columns.Add("detalle", "Detalle");
            EstiloHorarios.Pesos(grid, 26, 80, 108, 200, 215, 640);
            // El detalle entero aunque sea largo (a 10 pt y 1366 de ancho no cabia en
            // una linea): la fila crece. Tambien un laboratorio o una alerta largos.
            foreach (string columna in new[] { "laboratorio", "alerta", "detalle" })
            {
                grid.Columns[columna].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                // En dos lineas, con aire arriba y abajo (antes tocaban la raya de la fila).
                grid.Columns[columna].DefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
            }

            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            // Las de una linea, del alto de siempre (sin esto quedaban mas apretadas).
            grid.RowTemplate.MinimumHeight = grid.RowTemplate.Height;
            grid.Columns["nueva"].DefaultCellStyle.ForeColor = RojoNueva;
            grid.Columns["nueva"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            ayuda.SetToolTip(grid, "Doble clic: quién registró en esa clase; en las fallas, sus bitácoras; en un cambio de horario, el historial.");

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            Button actualizar = BotonAbajo("Actualizar", 130);
            actualizar.Click += (s, e) => Actualizar();
            ayuda.SetToolTip(actualizar, "Vuelve a leer las alertas (F5).");
            btnHorario = BotonAbajo("Ver horario", 140);
            btnHorario.Click += (s, e) => VerHorarioOFaltas();
            btnVer = BotonAbajo("Ver quién registró", 170);
            btnVer.Click += (s, e) => Ver();
            // Lo mismo que el menu de Faltas; el encargado (solo consulta) no lo tiene.
            if (!Permisos.SoloConsulta)
            {
                btnNoDada = BotonAbajo("Marcar como no dada...", 200);
                btnNoDada.Click += (s, e) => MarcarNoDada();
                ayuda.SetToolTip(btnNoDada, "Si la clase no se dio por un motivo (día festivo, junta): se cancela ese día y deja de contar como falta.");
            }

            ActiveControl = txtBuscar;
            ActualizarBotones();
        }

        /// <summary>F5, desde cualquier sitio de la ventana: lo mismo que Actualizar.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                Actualizar();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Actualizar()
        {
            Alerta elegida = Seleccionado<Alerta>();
            Cursor = Cursors.WaitCursor;
            try
            {
                Recargar(elegida == null ? null : elegida.Clave);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private int Dias
        {
            get
            {
                string periodo = cboPeriodo.SelectedItem as string;
                return periodo == Hoy ? 1 : periodo == Mes ? 30 : 7;
            }
        }

        protected override void Recargar(string seleccionar)
        {
            try
            {
                resultado = datos.Leer(Dias);
                lecturaFallida = false;
            }
            catch (Exception ex)
            {
                // Se queda lo de la ultima lectura buena: "sin alertas" seria falso.
                lecturaFallida = true;
                AvisoDeFalloAdmin.Mostrar(this, "Leer las alertas", ex, "No se pudieron leer las alertas", Text);
            }

            Aplicar(seleccionar);
        }

        /// <summary>
        /// Con la ventana ya abierta, al pedirla otra vez desde el panel: las
        /// nuevas pasan a ser las de despues de antes y se vuelve a leer, con la
        /// misma fila elegida.
        /// </summary>
        public void Renovar(DateTime? antes)
        {
            vistasAntes = antes;
            Alerta elegida = Seleccionado<Alerta>();
            Cursor = Cursors.WaitCursor;
            try
            {
                Recargar(elegida == null ? null : elegida.Clave);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        protected override void AlBuscar()
        {
            Aplicar(null);
        }

        /// <summary>
        /// De despues de lo visto y de las que cuentan en la campana: a la responsable
        /// los docentes por enlazar no le salen como nuevos (PrincipalAdministrador.CuentaComoNueva).
        /// </summary>
        private bool EsNueva(Alerta alerta)
        {
            return (!vistasAntes.HasValue || alerta.Momento > vistasAntes.Value) && PrincipalAdministrador.CuentaComoNueva(alerta);
        }

        /// <summary>Si la alerta es del tipo del filtro ("Sin bitacoras" incluye las en curso).</summary>
        private static bool DelTipo(Alerta alerta, string tipo)
        {
            switch (tipo)
            {
                case SinBitacoras:
                    return alerta.Tipo == TipoAlerta.SinBitacoras || alerta.Tipo == TipoAlerta.EnCursoSinBitacoras;
                case Fallas:
                    return alerta.Tipo == TipoAlerta.Fallas;
                case Cambios:
                    return alerta.Tipo == TipoAlerta.CambioDeHorario;
                case PorEnlazar:
                    return alerta.Tipo == TipoAlerta.DocentesPorEnlazar;
                default:
                    return true;
            }
        }

        private void Aplicar(string seleccionar)
        {
            string tipo = cboTipo.SelectedItem as string;
            List<Alerta> visibles = resultado.Alertas.Where(a =>
                    DelTipo(a, tipo)
                    && EstiloAdmin.Coincide(EstiloAdmin.Normalizar(a.Laboratorio + " " + a.Materia + " " + a.Grupo + " " + a.Profesor
                        + " " + a.Equipos + " " + a.Titulo + " " + a.Detalle), txtBuscar.Text))
                .ToList();
            LlenarLista(visibles,
                a => new object[] { EsNueva(a) ? "●" : "", Dia(a.Fecha), Hora(a), EstiloAdmin.Guion(a.Laboratorio), a.Titulo, a.Detalle },
                a => false, a => a.Clave, seleccionar);

            foreach (DataGridViewRow fila in grid.Rows)
            {
                Alerta alerta = fila.Tag as Alerta;
                if (alerta == null)
                {
                    continue;
                }

                fila.DefaultCellStyle.BackColor = alerta.Tipo == TipoAlerta.Fallas ? FondoFallas
                    : alerta.Tipo == TipoAlerta.EnCursoSinBitacoras ? FondoEnCurso
                    : alerta.Tipo == TipoAlerta.CambioDeHorario ? FondoCambio
                    : alerta.Tipo == TipoAlerta.DocentesPorEnlazar ? FondoPorEnlazar : FondoSinBitacoras;
                if (EsNueva(alerta))
                {
                    fila.DefaultCellStyle.Font = negrita;
                }
            }

            int nuevas = visibles.Count(EsNueva);
            // En medio de la lista vacia, por que lo esta (no un rectangulo blanco).
            ListaDigBit.PonerTextoVacio(grid,
                lecturaFallida ? "No se pudieron leer las alertas. F5 lo vuelve a intentar."
                : resultado.Alertas.Count == 0 ? "Sin alertas en " + Periodo() + "."
                : "Ninguna alerta coincide con la búsqueda. Borra la búsqueda o cambia «Tipo».",
                lecturaFallida);
            // En naranja si la ultima lectura fallo: no es un resumen, es un aviso.
            lblResumen.ForeColor = lecturaFallida ? Color.FromArgb(190, 85, 0) : EstiloAdmin.TextoOscuro;
            if (lecturaFallida)
            {
                lblResumen.Text = "No se pudieron leer las alertas" + (resultado.Ahora == DateTime.MinValue ? ""
                    : " (lo que ves es de las " + resultado.Ahora.ToString("HH:mm", CultureInfo.InvariantCulture) + ")") + ". F5 lo vuelve a intentar.";
            }
            else
            {
                lblResumen.Text = resultado.Alertas.Count == 0
                    ? "Sin alertas en " + Periodo() + "."
                    : visibles.Count + (visibles.Count == 1 ? " alerta" : " alertas")
                      + (nuevas > 0 ? " (" + nuevas + (nuevas == 1 ? " nueva" : " nuevas") + ")" : "")
                      + (visibles.Count < resultado.Alertas.Count ? " de " + resultado.Alertas.Count : "") + " en " + Periodo() + ".";
            }

            ActualizarBotones();

            // Vistas hasta esa lectura solo si la lista la ensena entera (ningun
            // filtro esconde alguna) y abarca los dias que cuenta el panel: con
            // "Hoy" faltarian las de los dias anteriores.
            if (!lecturaFallida && resultado.Ahora != DateTime.MinValue && visibles.Count == resultado.Alertas.Count
                && (cboPeriodo.SelectedItem as string) != Hoy && Leidas != null)
            {
                Leidas(resultado.Ahora);
            }
        }

        private string Periodo()
        {
            string periodo = cboPeriodo.SelectedItem as string;
            return periodo == Hoy ? "el día de hoy" : periodo == Mes ? "los últimos 30 días" : "los últimos 7 días";
        }

        /// <summary>"Hoy", "Ayer" o "lun 12/10" (con la fecha del servidor).</summary>
        private string Dia(DateTime fecha)
        {
            DateTime hoy = resultado.Ahora == DateTime.MinValue ? DateTime.Today : resultado.Ahora.Date;
            return fecha.Date == hoy ? "Hoy" : fecha.Date == hoy.AddDays(-1) ? "Ayer"
                : DiasCortos[(int)fecha.DayOfWeek] + " " + fecha.ToString("dd/MM", CultureInfo.InvariantCulture);
        }

        private static string Hora(Alerta alerta)
        {
            switch (alerta.Tipo)
            {
                case TipoAlerta.Fallas:
                case TipoAlerta.DocentesPorEnlazar:
                    return "último " + alerta.Momento.ToString("HH:mm", CultureInfo.InvariantCulture);
                case TipoAlerta.CambioDeHorario:
                    // Cuando se hizo el cambio; el dia al que afecta lo dice el detalle.
                    return "a las " + alerta.Momento.ToString("HH:mm", CultureInfo.InvariantCulture);
                default:
                    return HorariosDatos.Hora(alerta.Inicio) + " – " + HorariosDatos.Hora(alerta.Fin);
            }
        }

        protected override void ActualizarBotones()
        {
            if (btnVer == null)
            {
                return;
            }

            Alerta alerta = Seleccionado<Alerta>();
            bool sinNadie = alerta != null && alerta.Tipo == TipoAlerta.SinBitacoras;
            btnVer.Enabled = alerta != null;
            btnVer.Text = alerta != null && alerta.Tipo == TipoAlerta.Fallas ? "Ver sus bitácoras"
                : alerta != null && alerta.Tipo == TipoAlerta.CambioDeHorario ? "Ver en el historial"
                : alerta != null && alerta.Tipo == TipoAlerta.DocentesPorEnlazar ? (Permisos.SoloConsulta ? "Ver en Docentes" : "Enlazar...")
                : "Ver quién registró";
            // Un cambio de una clase (no de una franja) no tiene laboratorio.
            btnHorario.Enabled = alerta != null && alerta.LaboratorioId > 0;
            btnHorario.Text = sinNadie ? "Ver en Faltas" : "Ver horario";
            if (btnNoDada != null)
            {
                btnNoDada.Visible = sinNadie;
                btnNoDada.Text = sinNadie && alerta.EsExtra ? "Quitar la sesión extra..." : "Marcar como no dada...";
            }
        }

        protected override void AlDobleClic()
        {
            Ver();
        }

        /// <summary>
        /// Fallas: sus bitacoras (Bitacoras, ese dia, ese laboratorio y solo con
        /// fallas). Un cambio de horario: su renglon en el historial. Una clase:
        /// quien registro (se ve aunque no haya sesion, y se renueva sola). Al
        /// volver se recarga: la alerta pudo dejar de serlo.
        /// </summary>
        private void Ver()
        {
            Alerta alerta = Seleccionado<Alerta>();
            if (alerta == null)
            {
                return;
            }

            if (alerta.Tipo == TipoAlerta.Fallas)
            {
                if (VerBitacoras != null)
                {
                    VerBitacoras(alerta);
                }

                return;
            }

            if (alerta.Tipo == TipoAlerta.CambioDeHorario)
            {
                AbrirHistorial(alerta);
                return;
            }

            // Docentes, con la lista de quienes esperan: se enlazan ahi. Al volver a
            // Alertas (F5 o al reabrirla) ya no estan los que se enlazaron.
            if (alerta.Tipo == TipoAlerta.DocentesPorEnlazar)
            {
                if (VerDocentesPorEnlazar != null)
                {
                    VerDocentesPorEnlazar();
                }

                return;
            }

            string horario = Dia(alerta.Fecha) + ", " + HorariosDatos.Hora(alerta.Inicio) + " a " + HorariosDatos.Hora(alerta.Fin)
                + (alerta.EsExtra ? " (sesión extra)" : "") + ", " + alerta.Laboratorio;
            using (BitacoraSesion bitacora = new BitacoraSesion(alerta.FranjaId, alerta.ExcepcionId, alerta.Materia + ", " + alerta.Grupo,
                horario, alerta.Profesor, alerta.Fecha))
            {
                bitacora.ShowDialog(this);
            }

            Recargar(alerta.Clave);
        }

        /// <summary>Una clase en que nadie registro: Faltas, en su laboratorio y su semana. Lo demas: el horario del laboratorio.</summary>
        private void VerHorarioOFaltas()
        {
            Alerta alerta = Seleccionado<Alerta>();
            if (alerta == null || alerta.LaboratorioId <= 0)
            {
                return;
            }

            if (alerta.Tipo == TipoAlerta.SinBitacoras)
            {
                VerEnFaltas(alerta);
            }
            else if (VerHorario != null)
            {
                VerHorario(alerta.LaboratorioId);
            }
        }

        /// <summary>
        /// Horarios en la pestana Faltas, en el laboratorio y la semana de la clase
        /// (AdministrarHorarios.IrAFaltas). La ventana de Horarios es una sola y la
        /// abre el panel principal (VerHorario): si no esta abierta, se le pide.
        /// </summary>
        private void VerEnFaltas(Alerta alerta)
        {
            AdministrarHorarios horarios = HorariosAbierto();
            if (horarios == null && VerHorario != null)
            {
                VerHorario(alerta.LaboratorioId);
                horarios = HorariosAbierto();
            }

            if (horarios == null)
            {
                // Sin el panel principal (no deberia pasar): una propia.
                horarios = new AdministrarHorarios(alerta.LaboratorioId);
                horarios.Show(Owner);
            }

            horarios.IrAFaltas(alerta.LaboratorioId, alerta.Fecha);
            horarios.Activate();
        }

        private static AdministrarHorarios HorariosAbierto()
        {
            return Application.OpenForms.OfType<AdministrarHorarios>().LastOrDefault(f => !f.IsDisposed);
        }

        /// <summary>
        /// Una clase en que nadie registro y que no se dio (festivo, junta): lo mismo
        /// que "Marcar como no dada" de Faltas (PestanaCalendario.MarcarNoDada).
        /// Despues deja de ser alerta y se vuelve a leer.
        /// </summary>
        private void MarcarNoDada()
        {
            Alerta alerta = Seleccionado<Alerta>();
            if (alerta == null || alerta.Tipo != TipoAlerta.SinBitacoras)
            {
                return;
            }

            ClaseDelCalendario clase = new ClaseDelCalendario
            {
                Fecha = alerta.Fecha,
                LaboratorioId = alerta.LaboratorioId,
                EsExtra = alerta.EsExtra,
                FranjaId = alerta.FranjaId,
                ExcepcionId = alerta.ExcepcionId,
                Materia = alerta.Materia,
                Grupo = alerta.Grupo,
                Profesor = alerta.Profesor,
                Inicio = alerta.Inicio,
                Fin = alerta.Fin
            };
            if (PestanaCalendario.MarcarNoDada(this, new HorariosDatos(), clase, IdAdministrador(), "Alertas"))
            {
                Actualizar();
                // Corto: cabe junto a los botones (el motivo ya lo dijo el dialogo: queda en Excepciones).
                Avisar(alerta.EsExtra ? "Listo: se quitó la sesión extra; ya no cuenta como falta."
                    : "Listo: se canceló la clase ese día; ya no cuenta como falta.");
            }
        }

        private int IdAdministrador()
        {
            if (!idAdministrador.HasValue)
            {
                int id = 0;
                string usuario = Datos_User.getUser();
                if (!string.IsNullOrWhiteSpace(usuario))
                {
                    try
                    {
                        id = new Consultas().ObtenerIdPorMatricula(usuario);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Alertas: no se pudo saber el id del administrador '" + usuario + "'", ex);
                    }
                }

                idAdministrador = id;
            }

            return idAdministrador.Value;
        }

        /// <summary>
        /// El historial de cambios, uno solo y encima de las alertas; con una alerta
        /// de cambio de horario, en su renglon.
        /// </summary>
        private void AbrirHistorial(Alerta alerta)
        {
            if (historial == null || historial.IsDisposed)
            {
                historial = new VentanaHistorialCambios();
                if (alerta != null)
                {
                    historial.Mostrar(alerta.CambioId, alerta.Momento);
                }

                historial.Show(this);
                return;
            }

            if (alerta != null)
            {
                historial.Mostrar(alerta.CambioId, alerta.Momento);
            }

            historial.Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                negrita.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
