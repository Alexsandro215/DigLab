using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;
using Timer = System.Windows.Forms.Timer;

namespace DigBit
{
    /// <summary>
    /// Historial de cambios (db/21, HistorialCambios): quien cambio el horario
    /// (franjas, clases, cancelaciones y sesiones extra), corrigio una bitacora o
    /// cambio un laboratorio o su forma de entrar, cuando, y lo que decia antes.
    /// Solo se consulta, tambien el encargado de laboratorio. Filtros por fechas,
    /// tipo, persona y texto; abajo, el cambio elegido entero, con lo de antes y
    /// lo de ahora. Se abre desde Alertas ("Historial de cambios..." o "Ver en el
    /// historial" de una alerta de cambio de horario).
    /// </summary>
    public class VentanaHistorialCambios : PantallaAdmin
    {
        private const string TodosLosTipos = "Todos";
        private const string TodasLasPersonas = "Todas las personas";
        private const string DeHorario = "Cambios de horario";
        private const int DiasPorOmision = 30;
        private const int AltoDetalle = 150;

        private readonly TextBox txtBuscar;
        private readonly DateTimePicker dtpDesde;
        private readonly DateTimePicker dtpHasta;
        private readonly ComboBox cboTipo;
        private readonly ComboBox cboQuien;
        private readonly Label lblQue;
        private readonly TextBox txtAntes;
        private readonly TextBox txtAhora;
        private readonly Timer esperaFechas;
        // El filtro Tipo: su texto y el tipo de historial_cambios que elige.
        private readonly Dictionary<string, string> tipoPorTexto = new Dictionary<string, string>();

        private LecturaHistorial lectura = new LecturaHistorial();
        private bool lecturaFallida;
        // Mientras las fechas o los filtros cambian desde el codigo (no los toco el usuario).
        private bool cambiandoFiltros;
        // El usuario (o Mostrar) eligio las fechas: ya no se ajustan al dia del servidor.
        private bool fechasElegidas;
        private bool mostrada;
        // Lo que pidio Mostrar antes de que la ventana se viera: se elige al leer.
        private int? porElegir;

        public VentanaHistorialCambios()
            : base("Historial de cambios",
                "Quién cambió el horario, las clases, los laboratorios y las bitácoras, y cuándo; con lo que decía antes. Solo se consulta. F5 actualiza.",
                new Size(1320, 680))
        {
            txtBuscar = CrearBuscador(new Point(Margen, 112), 230);
            ayuda.SetToolTip(txtBuscar, "Lo que dice el cambio: materia, grupo, docente, laboratorio, estudiante, motivo...");

            Controls.Add(EstiloHorarios.CrearEtiqueta("Desde", new Point(280, 88)));
            dtpDesde = CrearFecha(new Point(280, 111));
            Controls.Add(EstiloHorarios.CrearEtiqueta("Hasta", new Point(430, 88)));
            dtpHasta = CrearFecha(new Point(430, 111));
            cambiandoFiltros = true;
            dtpHasta.Value = DateTime.Today;
            dtpDesde.Value = DateTime.Today.AddDays(-DiasPorOmision);
            cambiandoFiltros = false;

            Controls.Add(EstiloHorarios.CrearEtiqueta("Tipo", new Point(580, 88)));
            cboTipo = EstiloHorarios.CrearCombo(new Point(580, 111), 190);
            cboTipo.MaxDropDownItems = 12;
            cboTipo.Items.Add(TodosLosTipos);
            cboTipo.Items.Add(DeHorario);
            foreach (string tipo in TipoDeCambio.Todos)
            {
                tipoPorTexto[TipoDeCambio.Plural(tipo)] = tipo;
                cboTipo.Items.Add(TipoDeCambio.Plural(tipo));
            }

            cboTipo.SelectedIndex = 0;
            cboTipo.SelectedIndexChanged += (s, e) => { if (!cambiandoFiltros) Aplicar(null); };
            ayuda.SetToolTip(cboTipo, "Cambios de horario: franjas, clases, cancelaciones y sesiones extra (los que avisan en Alertas).");
            Controls.Add(cboTipo);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Quién", new Point(790, 88)));
            // Ancho para un nombre con su numero; menos en una pantalla angosta.
            cboQuien = EstiloHorarios.CrearCombo(new Point(790, 111), Math.Max(180, Math.Min(300, ClientSize.Width - Margen - 790)));
            cboQuien.MaxDropDownItems = 16;
            cboQuien.Items.Add(new PersonaDelHistorial(null, TodasLasPersonas));
            cboQuien.SelectedIndex = 0;
            cboQuien.SelectedIndexChanged += (s, e) =>
            {
                AyudaDeQuien();
                if (!cambiandoFiltros) Aplicar(null);
            };
            Controls.Add(cboQuien);
            AyudaDeQuien();

            esperaFechas = new Timer { Interval = 400 };
            esperaFechas.Tick += (s, e) =>
            {
                esperaFechas.Stop();
                Actualizar();
            };

            CrearLista(156);
            grid.Columns.Add("cuando", "Cuándo");
            grid.Columns.Add("quien", "Quién");
            grid.Columns.Add("que", "Qué");
            grid.Columns.Add("laboratorio", "Laboratorio");
            grid.Columns.Add("detalle", "Lo que pasó");
            // A 1320 de ancho caben la fecha, todos los titulos de Que ("Forma de
            // entrar cambiada"), los laboratorios ("DESARROLLO DE SOFTWARE") y en
            // Quien un nombre de unos 200 px; uno mas largo y Lo que paso, enteros abajo.
            EstiloHorarios.Pesos(grid, 125, 200, 205, 200, 470);
            // La fecha como fecha: asi se ordena bien al pulsar el encabezado.
            grid.Columns["cuando"].DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
            grid.Columns["cuando"].DefaultCellStyle.FormatProvider = CultureInfo.InvariantCulture;
            grid.Columns["cuando"].ValueType = typeof(DateTime);
            ayuda.SetToolTip(grid, "Elige un cambio para ver abajo lo que decía antes y lo que dice ahora.");

            // Abajo de la lista: el cambio elegido entero, con lo de antes y lo de ahora.
            grid.Height -= AltoDetalle + 8;
            TableLayoutPanel detalle = new TableLayoutPanel
            {
                Location = new Point(Margen, grid.Bottom + 8),
                Size = new Size(ClientSize.Width - 2 * Margen, AltoDetalle),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Color.White
            };
            detalle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            detalle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            detalle.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            detalle.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            detalle.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            lblQue = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Margin = new Padding(0, 0, 0, 2)
            };
            detalle.Controls.Add(lblQue, 0, 0);
            detalle.SetColumnSpan(lblQue, 2);
            detalle.Controls.Add(EtiquetaDetalle("Antes"), 0, 1);
            detalle.Controls.Add(EtiquetaDetalle("Ahora"), 1, 1);
            txtAntes = CajaDetalle(new Padding(0, 0, 6, 0));
            txtAhora = CajaDetalle(new Padding(6, 0, 0, 0));
            detalle.Controls.Add(txtAntes, 0, 2);
            detalle.Controls.Add(txtAhora, 1, 2);
            Controls.Add(detalle);

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            Button actualizar = BotonAbajo("Actualizar", 130);
            actualizar.Click += (s, e) => Actualizar();
            ayuda.SetToolTip(actualizar, "Vuelve a leer el historial de esas fechas (F5).");

            ActiveControl = txtBuscar;
            MostrarDetalle(null);
        }

        private DateTimePicker CrearFecha(Point posicion)
        {
            // Con formato propio: el corto sale de la configuracion de Windows.
            DateTimePicker fecha = new DateTimePicker
            {
                Font = new DrawingFont("Century Gothic", 10F),
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy",
                Location = posicion,
                Size = new Size(130, 26)
            };
            fecha.ValueChanged += (s, e) =>
            {
                if (cambiandoFiltros)
                {
                    return;
                }

                fechasElegidas = true;
                esperaFechas.Stop();
                esperaFechas.Start();
            };
            Controls.Add(fecha);
            return fecha;
        }

        private static Label EtiquetaDetalle(string texto)
        {
            return new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Margin = new Padding(0),
                Text = texto,
                TextAlign = ContentAlignment.BottomLeft
            };
        }

        private static TextBox CajaDetalle(Padding margen)
        {
            return new TextBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                Font = new DrawingFont("Century Gothic", 9.5F),
                Margin = margen,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = false
            };
        }

        /// <summary>
        /// Lleva la ventana a un cambio (una alerta de cambio de horario): fechas que
        /// lo incluyan, sin filtros, y ese renglon elegido.
        /// </summary>
        public void Mostrar(int idCambio, DateTime cuando)
        {
            cambiandoFiltros = true;
            try
            {
                if (cuando.Date < dtpDesde.Value.Date)
                {
                    dtpDesde.Value = cuando.Date;
                }

                if (cuando.Date > dtpHasta.Value.Date)
                {
                    dtpHasta.Value = cuando.Date;
                }

                cboTipo.SelectedIndex = 0;
                cboQuien.SelectedIndex = 0;
                txtBuscar.Text = "";
            }
            finally
            {
                cambiandoFiltros = false;
            }

            fechasElegidas = true;
            if (!mostrada)
            {
                porElegir = idCambio;
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                Recargar(Clave(idCambio));
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            mostrada = true;
            base.OnShown(e);
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
            CambioRegistrado elegido = Seleccionado<CambioRegistrado>();
            Cursor = Cursors.WaitCursor;
            try
            {
                Recargar(elegido == null ? null : Clave(elegido.Id));
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private static string Clave(int idCambio)
        {
            return idCambio.ToString(CultureInfo.InvariantCulture);
        }

        protected override void Recargar(string seleccionar)
        {
            if (seleccionar == null && porElegir.HasValue)
            {
                seleccionar = Clave(porElegir.Value);
            }

            porElegir = null;
            DateTime desde = dtpDesde.Value.Date <= dtpHasta.Value.Date ? dtpDesde.Value.Date : dtpHasta.Value.Date;
            DateTime hasta = dtpDesde.Value.Date <= dtpHasta.Value.Date ? dtpHasta.Value.Date : dtpDesde.Value.Date;
            try
            {
                lectura = HistorialCambios.Leer(desde, hasta);
                lecturaFallida = false;

                // La primera vez, los ultimos dias segun el reloj del servidor (el de
                // este equipo puede ir atrasado y dejar fuera lo de hoy).
                if (!fechasElegidas && lectura.Ahora != DateTime.MinValue && lectura.Ahora.Date != hasta)
                {
                    fechasElegidas = true;
                    cambiandoFiltros = true;
                    try
                    {
                        dtpHasta.Value = lectura.Ahora.Date;
                        dtpDesde.Value = lectura.Ahora.Date.AddDays(-DiasPorOmision);
                    }
                    finally
                    {
                        cambiandoFiltros = false;
                    }

                    lectura = HistorialCambios.Leer(dtpDesde.Value.Date, dtpHasta.Value.Date);
                }
            }
            catch (Exception ex)
            {
                // Se queda lo de la ultima lectura buena: "sin cambios" seria falso.
                lecturaFallida = true;
                AvisoDeFalloAdmin.Mostrar(this, "Leer el historial de cambios", ex, "No se pudo leer el historial de cambios", Text);
            }

            LlenarPersonas();
            Aplicar(seleccionar);
        }

        /// <summary>Las personas que hicieron algun cambio en lo leido; se conserva la elegida si sigue.</summary>
        private void LlenarPersonas()
        {
            PersonaDelHistorial antes = cboQuien.SelectedItem as PersonaDelHistorial;
            List<PersonaDelHistorial> personas = lectura.Cambios
                .GroupBy(c => c.Usuario ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(g => new PersonaDelHistorial(g.Key, g.First().Quien + (g.Key.Length > 0 && g.Key != g.First().Quien ? " (" + g.Key + ")" : "")))
                .OrderBy(p => p.Texto, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            cambiandoFiltros = true;
            try
            {
                cboQuien.BeginUpdate();
                cboQuien.Items.Clear();
                cboQuien.Items.Add(new PersonaDelHistorial(null, TodasLasPersonas));
                int elegida = 0;
                foreach (PersonaDelHistorial persona in personas)
                {
                    int indice = cboQuien.Items.Add(persona);
                    if (antes != null && antes.Usuario != null && string.Equals(antes.Usuario, persona.Usuario, StringComparison.OrdinalIgnoreCase))
                    {
                        elegida = indice;
                    }
                }

                cboQuien.SelectedIndex = elegida;
                // La lista que se despliega, tan ancha como el nombre mas largo con su numero.
                cboQuien.DropDownWidth = Math.Max(cboQuien.Width, personas.Select(p => TextRenderer.MeasureText(p.Texto, cboQuien.Font).Width)
                    .DefaultIfEmpty(0).Max() + SystemInformation.VerticalScrollBarWidth + 8);
            }
            finally
            {
                cboQuien.EndUpdate();
                cambiandoFiltros = false;
            }
        }

        /// <summary>La ayuda del filtro Quien: la persona elegida entera (un nombre largo se corta en la caja).</summary>
        private void AyudaDeQuien()
        {
            PersonaDelHistorial elegida = cboQuien.SelectedItem as PersonaDelHistorial;
            ayuda.SetToolTip(cboQuien, elegida == null || elegida.Usuario == null
                ? "Quién hizo el cambio: elige a una persona para ver solo los suyos."
                : elegida.Texto);
        }

        protected override void AlBuscar()
        {
            Aplicar(null);
        }

        private bool DelTipo(CambioRegistrado cambio)
        {
            string texto = cboTipo.SelectedItem as string;
            if (texto == null || texto == TodosLosTipos)
            {
                return true;
            }

            if (texto == DeHorario)
            {
                return TipoDeCambio.EsDeHorario(cambio.Tipo);
            }

            string tipo;
            return tipoPorTexto.TryGetValue(texto, out tipo) && cambio.Tipo == tipo;
        }

        private void Aplicar(string seleccionar)
        {
            PersonaDelHistorial persona = cboQuien.SelectedItem as PersonaDelHistorial;
            string usuario = persona == null ? null : persona.Usuario;
            List<CambioRegistrado> visibles = lectura.Cambios.Where(c =>
                    DelTipo(c)
                    && (usuario == null || string.Equals(c.Usuario ?? "", usuario, StringComparison.OrdinalIgnoreCase))
                    && EstiloAdmin.Coincide(EstiloAdmin.Normalizar(c.Quien + " " + c.Usuario + " " + c.Titulo + " " + c.Laboratorio + " "
                        + c.Descripcion + " " + c.Antes + " " + c.Despues), txtBuscar.Text))
                .ToList();
            LlenarLista(visibles,
                c => new object[] { c.Cuando, c.Quien, c.Titulo, EstiloAdmin.Guion(c.Laboratorio), c.Descripcion },
                c => false, c => Clave(c.Id), seleccionar);

            string periodo = "del " + EstiloAdmin.Fecha(dtpDesde.Value.Date <= dtpHasta.Value.Date ? dtpDesde.Value : dtpHasta.Value)
                + " al " + EstiloAdmin.Fecha(dtpDesde.Value.Date <= dtpHasta.Value.Date ? dtpHasta.Value : dtpDesde.Value);
            Color naranja = Color.FromArgb(190, 85, 0);
            if (lecturaFallida)
            {
                lblResumen.ForeColor = naranja;
                lblResumen.Text = "No se pudo leer el historial" + (lectura.Ahora == DateTime.MinValue ? ""
                    : " (lo que ves es de las " + lectura.Ahora.ToString("HH:mm", CultureInfo.InvariantCulture) + ")") + ". F5 lo vuelve a intentar.";
            }
            else if (!lectura.Disponible)
            {
                // En una linea de 974 px a 1366 (lo que pasa en la escuela, sin db/21): la
                // accion es lo que no se puede perder. La persona responsable de
                // laboratorio no administra el servidor: avisa.
                lblResumen.ForeColor = naranja;
                lblResumen.Text = "Todavía no hay historial: falta aplicar db/21_historial_cambios.sql en la base. "
                    + (Permisos.SoloConsulta ? "Avisa a la administración de DigBit." : "Pide a quien administra el servidor que la aplique.");
            }
            else
            {
                lblResumen.ForeColor = Color.DarkGreen;
                lblResumen.Text = lectura.Cambios.Count == 0 ? "Sin cambios " + periodo + "."
                    : visibles.Count + (visibles.Count == 1 ? " cambio" : " cambios")
                      + (visibles.Count < lectura.Cambios.Count ? " de " + lectura.Cambios.Count : "") + " " + periodo + ".";
            }

            ayuda.SetToolTip(lblResumen, lblResumen.Text);
            NotaFija = lectura.Recortada
                ? "Se ven los " + HistorialCambios.MaximoPorLectura.ToString("N0", CultureInfo.InvariantCulture) + " cambios más recientes: acorta las fechas para ver los demás."
                : "";
            ActualizarBotones();
        }

        protected override void ActualizarBotones()
        {
            if (txtAntes != null)
            {
                MostrarDetalle(Seleccionado<CambioRegistrado>());
            }
        }

        /// <summary>El cambio elegido entero: que paso, cuando y quien; y lo de antes y lo de ahora.</summary>
        private void MostrarDetalle(CambioRegistrado cambio)
        {
            if (cambio == null)
            {
                lblQue.Text = lectura.Cambios.Count == 0 ? "" : "Elige un cambio de la lista para verlo entero.";
                txtAntes.Text = "";
                txtAhora.Text = "";
                return;
            }

            lblQue.Text = cambio.Titulo + " el " + EstiloAdmin.Fecha(cambio.Cuando) + " a las "
                + cambio.Cuando.ToString("HH:mm", CultureInfo.InvariantCulture) + ", por " + cambio.Quien + ". " + cambio.Descripcion;
            ayuda.SetToolTip(lblQue, lblQue.Text);
            txtAntes.Text = Lineas(cambio.Antes, cambio.Accion == AccionDeCambio.Alta ? "(no existía: es nuevo)" : "—");
            txtAhora.Text = Lineas(cambio.Despues, cambio.Accion == AccionDeCambio.Quitar ? "(ya no existe)" : "—");
        }

        private static string Lineas(string datos, string vacio)
        {
            return string.IsNullOrWhiteSpace(datos) ? vacio : datos.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                esperaFechas.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Una persona del filtro Quien: su numero (null = todas) y como se ve.</summary>
        private sealed class PersonaDelHistorial
        {
            public readonly string Usuario;
            public readonly string Texto;

            public PersonaDelHistorial(string usuario, string texto)
            {
                Usuario = usuario;
                Texto = texto;
            }

            public override string ToString()
            {
                return Texto;
            }
        }
    }
}
