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
    // =========================================================================
    // Horario semanal con db/25 (horas_por_asignar): "Falta asignar: N h", la
    // confirmacion de un reemplazo y "Quitar horas" con la pregunta de si se
    // dara en otro horario. La capa de datos esta en HorariosDatos.PorAsignar.cs.
    // =========================================================================

    /// <summary>
    /// "Falta asignar: N h" de Horario semanal: lo que le falta a cada clase en
    /// todos los laboratorios, de donde salio y desde cuando. "Ponerla..." cierra
    /// con la elegida y el laboratorio donde ponerla (Elegida, LaboratorioElegido):
    /// la pestana la deja preelegida para el siguiente "Poner clase...". "Ya no
    /// hace falta" la resuelve (con confirmacion) sin cerrar. En solo consulta,
    /// solo la lista.
    /// </summary>
    internal class DialogoFaltaAsignar : Form
    {
        private readonly DataGridView dgvLista;
        private readonly Button btnPonerla;
        private readonly Button btnNoHaceFalta;
        private readonly ComboBox cboLaboratorio;
        private readonly Label lblTotal;
        private readonly Func<HoraPorAsignar, bool> yaNoHaceFalta;
        private readonly List<HoraPorAsignar> pendientes;

        /// <summary>La elegida al pulsar "Ponerla..." (DialogResult.OK); null si se cerro.</summary>
        public HoraPorAsignar Elegida { get; private set; }

        /// <summary>Donde ponerla: el laboratorio elegido en "Ponerla en".</summary>
        public int LaboratorioElegido { get; private set; }

        /// <summary>Si alguna se resolvio con "Ya no hace falta" (la pestana vuelve a leer la pastilla).</summary>
        public bool HuboCambios { get; private set; }

        /// <param name="laboratorios">Los laboratorios activos (los de Horario semanal), para "Ponerla en".</param>
        /// <param name="yaNoHaceFalta">Resuelve una (pregunta, escribe y avisa si falla): true si se resolvio. Null en solo consulta.</param>
        /// <param name="deQuien">Solo lo de un docente (la ficha del docente, solo para ver): su nombre, para la explicacion.</param>
        public DialogoFaltaAsignar(List<HoraPorAsignar> pendientes, List<OpcionCombo> laboratorios, int laboratorioActual, Func<HoraPorAsignar, bool> yaNoHaceFalta,
            string deQuien = null)
        {
            this.pendientes = new List<HoraPorAsignar>(pendientes);
            this.yaNoHaceFalta = yaNoHaceFalta;
            bool conAcciones = yaNoHaceFalta != null && !Permisos.SoloConsulta;
            int ancho = Math.Min(1180, Screen.FromPoint(Cursor.Position).WorkingArea.Width - 40);
            int alto = Math.Min(560, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 60);
            EstiloHorarios.PrepararDialogo(this, "Falta asignar", new Size(ancho, alto));

            Label titulo = EstiloAdmin.TituloDialogo("Horas que falta asignar", ancho - 48);
            string texto = (string.IsNullOrWhiteSpace(deQuien)
                    ? "Lo que le falta a cada clase en el horario en uso, en todos los laboratorios: las horas que otra clase desplazó "
                    : "Lo que le falta a cada clase de " + deQuien.Trim() + " en el horario en uso: las horas que otra clase desplazó ")
                + "o las que se quitaron para darlas en otro horario. Bajan solas al ponerle horas a la clase (lo más antiguo primero)."
                + (conAcciones ? " «Ponerla...» te lleva al laboratorio que elijas con la clase ya elegida para el siguiente «Poner clase...»."
                    : !string.IsNullOrWhiteSpace(deQuien) ? " Solo para ver: se asignan en Horarios > Horario semanal." : "");
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
            dgvLista.Columns.Add("Desde", "Desde");
            dgvLista.Columns.Add("Motivo", "Por qué");
            EstiloHorarios.Pesos(dgvLista, 24, 20, 8, 20, 9, 19);
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

            if (!conAcciones)
            {
                return;
            }

            ToolTip ayuda = new ToolTip { AutoPopDelay = 15000 };
            Disposed += (s, e) => ayuda.Dispose();

            // Abajo a la izquierda: donde ponerla y las dos acciones; Cerrar a la derecha.
            Label lblDonde = EstiloHorarios.CrearEtiqueta("Ponerla en", new Point(24, ClientSize.Height - 46));
            cboLaboratorio = EstiloHorarios.CrearCombo(new Point(24 + lblDonde.PreferredWidth + 8, ClientSize.Height - 50), 200);
            EstiloHorarios.Rellenar(cboLaboratorio, laboratorios, laboratorioActual);
            ayuda.SetToolTip(cboLaboratorio, "El laboratorio donde la quieres poner. Al elegir una clase de la lista, el de donde salió.");

            btnPonerla = EstiloAdmin.BotonPrincipal("Ponerla...", new Point(cboLaboratorio.Right + 10, ClientSize.Height - 56), new Size(130, 40));
            btnPonerla.Click += (s, e) => Ponerla();
            ayuda.SetToolTip(btnPonerla, "Lleva a Horario semanal, al laboratorio elegido, con esta clase ya elegida para el siguiente «Poner clase...».\n"
                + "Elige allí los cuadros (arrastra o usa Ctrl+clic) y pulsa «Poner clase...». Doble clic en la lista hace lo mismo.");

            btnNoHaceFalta = EstiloHorarios.CrearBoton("Ya no hace falta", new Point(btnPonerla.Right + 10, ClientSize.Height - 56), new Size(160, 40));
            btnNoHaceFalta.Click += (s, e) => NoHaceFalta();
            ayuda.SetToolTip(btnNoHaceFalta, "La quita de la lista sin ponerle horas (antes te pregunta). Queda en el historial de cambios.");

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

        private HoraPorAsignar Actual
        {
            get { return dgvLista.CurrentRow == null ? null : dgvLista.CurrentRow.Tag as HoraPorAsignar; }
        }

        private void Llenar()
        {
            dgvLista.Rows.Clear();
            foreach (HoraPorAsignar p in pendientes)
            {
                int fila = dgvLista.Rows.Add(
                    p.Clase,
                    p.Profesor,
                    HorariosDatos.Duracion(p.Minutos) + (p.Minutos < p.MinutosOriginales ? " de " + HorariosDatos.Duracion(p.MinutosOriginales) : ""),
                    p.Origen,
                    p.Desde.ToString("dd/MM/yyyy"),
                    p.Motivo == HorariosDatos.MotivoQuitada ? "Se quitó para darla en otro horario" : "Otra clase se puso en su lugar");
                dgvLista.Rows[fila].Tag = p;
            }

            int total = pendientes.Sum(p => p.Minutos);
            int clases = pendientes.Select(p => p.ClaseId).Distinct().Count();
            lblTotal.Text = pendientes.Count == 0 ? "Ya no falta asignar nada."
                : "Falta asignar: " + HorariosDatos.Duracion(total) + " de " + clases + (clases == 1 ? " clase." : " clases.");
        }

        /// <summary>Con una elegida: "Ponerla en" pasa al laboratorio de donde salio (si sigue activo).</summary>
        private void Elegir()
        {
            HoraPorAsignar p = Actual;
            btnPonerla.Enabled = p != null && cboLaboratorio.Items.Count > 0;
            btnNoHaceFalta.Enabled = p != null;
            if (p != null && p.LaboratorioId.HasValue)
            {
                OpcionCombo suyo = cboLaboratorio.Items.OfType<OpcionCombo>().FirstOrDefault(o => o.Id == p.LaboratorioId.Value);
                if (suyo != null)
                {
                    cboLaboratorio.SelectedItem = suyo;
                }
            }
        }

        private void Ponerla()
        {
            HoraPorAsignar p = Actual;
            OpcionCombo laboratorio = cboLaboratorio == null ? null : cboLaboratorio.SelectedItem as OpcionCombo;
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
            HoraPorAsignar p = Actual;
            if (p == null || yaNoHaceFalta == null || !yaNoHaceFalta(p))
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

    /// <summary>
    /// Antes de reemplazar: "Esto quita a <Materia, Grupo (Docente)> del <dia>
    /// <hh:mm-hh:mm> (N h). Quedara en «Falta asignar» para ponerla en otro
    /// horario.", con «Reemplazar» y «Cancelar».
    /// </summary>
    internal static class ConfirmarReemplazo
    {
        /// <summary>El texto de la confirmacion (tambien lo usan las pruebas de pantalla).</summary>
        public static string Texto(IList<FranjaReemplazada> quitadas, DateTime? desdeConFecha)
        {
            string texto;
            if (quitadas.Count == 1)
            {
                texto = "Esto quita a " + quitadas[0].Describir() + ". Quedará en «Falta asignar» para ponerla en otro horario.";
            }
            else
            {
                texto = "Esto quita a:" + Environment.NewLine
                    + string.Join(Environment.NewLine, quitadas.Select(q => "   • " + q.Describir())) + Environment.NewLine
                    + "Quedarán en «Falta asignar» para ponerlas en otro horario.";
            }

            if (desdeConFecha.HasValue)
            {
                texto += Environment.NewLine + Environment.NewLine + "Vale desde el " + desdeConFecha.Value.ToString("dd/MM/yyyy")
                    + ": lo que ya pasó se queda como fue (Faltas, la asistencia y las bitácoras).";
            }

            if (quitadas.Any(q => q.CambiaElPasado))
            {
                texto += Environment.NewLine + Environment.NewLine + "Ojo: también cambia las semanas que ya pasaron de "
                    + (quitadas.Count(q => q.CambiaElPasado) == 1 ? "esa clase" : "esas clases")
                    + " (en esta base los cambios de horario desde una fecha están apagados).";
            }

            return texto;
        }

        public static bool Preguntar(IWin32Window dueno, IList<FranjaReemplazada> quitadas, DateTime? desdeConFecha)
        {
            using (DialogoTextoYBotones dialogo = new DialogoTextoYBotones("Reemplazar una clase", Texto(quitadas, desdeConFecha), "Reemplazar",
                quitadas.Any(q => q.CambiaElPasado)))
            {
                return dialogo.ShowDialog(dueno) == DialogResult.OK;
            }
        }
    }

    /// <summary>Un texto (tan alto como haga falta) con el verbo de la accion y «Cancelar».</summary>
    internal class DialogoTextoYBotones : Form
    {
        private const int Ancho = 560;

        /// <param name="conAviso">El texto avisa de algo que no tiene vuelta (en ambar): Enter cancela.</param>
        public DialogoTextoYBotones(string titulo, string texto, string accion, bool conAviso)
        {
            Label lblTexto = PiezasConfiguracion.Texto(texto, new Point(24, 56), new Size(Ancho - 48, 20), 10F, Color.FromArgb(40, 40, 40));
            lblTexto.Height = PiezasConfiguracion.AltoDe(texto, lblTexto.Font, lblTexto.Width) + 6;
            EstiloHorarios.PrepararDialogo(this, titulo, new Size(Ancho, lblTexto.Bottom + 16 + 56));
            Controls.Add(EstiloAdmin.TituloDialogo(titulo, Ancho - 48));
            Controls.Add(lblTexto);
            Button hacer = EstiloAdmin.BotonesDialogo(this, accion, () => { DialogResult = DialogResult.OK; });
            if (conAviso)
            {
                AcceptButton = CancelButton as Button;
                ActiveControl = CancelButton as Button;
            }
            else
            {
                ActiveControl = hacer;
            }
        }
    }

    /// <summary>
    /// "Quitar horas..." con db/25: lo que se quita (y, con historia y los
    /// cambios desde una fecha, desde cuando), y la pregunta "¿Se va a dar en
    /// otro horario?": «Si, queda en Falta asignar» (la opcion por omision) o «No,
    /// la clase tendra menos horas». Quitar, en rojo; Enter cancela (quitar es a
    /// proposito, como antes).
    /// </summary>
    internal class DialogoQuitarHoras : Form
    {
        private const int Ancho = 560;
        private readonly DateTimePicker dtpDesde;
        private readonly RadioButton rbOtroHorario;
        private readonly Label lblError;
        private readonly DateTime minimo;
        private readonly DateTime maximo;

        public const string Pregunta = "¿Se va a dar en otro horario?";
        public const string OpcionSi = "Sí, queda en «Falta asignar»";
        public const string OpcionNo = "No, la clase tendrá menos horas";

        /// <summary>Si sus horas quedan en "Falta asignar".</summary>
        public bool PorAsignar
        {
            get { return rbOtroHorario.Checked; }
        }

        /// <summary>"Quitar desde" (solo si se pidio una fecha).</summary>
        public DateTime Fecha
        {
            get { return dtpDesde == null ? DateTime.Today : dtpDesde.Value.Date; }
        }

        /// <param name="aviso">Algo que no tiene vuelta (se quita tambien de las semanas pasadas), en ambar; o null.</param>
        /// <param name="fechaMinima">Con historia y los cambios desde una fecha: el primer dia posible; null sin fecha.</param>
        public DialogoQuitarHoras(string descripcion, string aviso, DateTime? fechaMinima, DateTime? fechaMaxima, int minutos)
        {
            minimo = fechaMinima.HasValue ? fechaMinima.Value.Date : DateTime.Today;
            maximo = fechaMaxima.HasValue && fechaMaxima.Value.Date >= minimo ? fechaMaxima.Value.Date : minimo;

            int y = 56;
            Label lblDescripcion = PiezasConfiguracion.Texto(descripcion, new Point(24, y), new Size(Ancho - 48, 20), 9.5F, Color.FromArgb(50, 50, 50));
            lblDescripcion.Height = PiezasConfiguracion.AltoDe(descripcion, lblDescripcion.Font, lblDescripcion.Width) + 4;
            Controls.Add(lblDescripcion);
            y = lblDescripcion.Bottom + 8;

            if (!string.IsNullOrEmpty(aviso))
            {
                Label lblAviso = PiezasConfiguracion.Texto(aviso, new Point(24, y), new Size(Ancho - 48, 20), 9F, Color.FromArgb(190, 85, 0), FontStyle.Bold);
                lblAviso.Height = PiezasConfiguracion.AltoDe(aviso, lblAviso.Font, lblAviso.Width) + 4;
                Controls.Add(lblAviso);
                y = lblAviso.Bottom + 8;
            }

            if (fechaMinima.HasValue)
            {
                Label lblFecha = EstiloHorarios.CrearEtiqueta("Quitar desde", new Point(24, y + 4));
                Controls.Add(lblFecha);
                dtpDesde = EstiloHorarios.CrearFechaConDia(this, new Point(24 + lblFecha.PreferredWidth + 12, y));
                dtpDesde.MinDate = minimo;
                dtpDesde.MaxDate = maximo;
                dtpDesde.Value = minimo;
                y += 40;
            }

            Label lblPregunta = PiezasConfiguracion.Texto(Pregunta, new Point(24, y + 4), new Size(Ancho - 48, 22), 10.5F, Color.FromArgb(30, 30, 30), FontStyle.Bold);
            Controls.Add(lblPregunta);
            y = lblPregunta.Bottom + 4;
            rbOtroHorario = new RadioButton
            {
                AutoSize = true,
                Checked = true,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(36, y),
                Text = OpcionSi + " (" + HorariosDatos.Duracion(minutos) + ")"
            };
            RadioButton rbMenos = new RadioButton
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(36, y + 28),
                Text = OpcionNo
            };
            Controls.Add(rbOtroHorario);
            Controls.Add(rbMenos);
            y += 60;

            lblError = EstiloAdmin.LineaError(new Point(24, y), Ancho - 48, 22);
            Controls.Add(lblError);
            if (dtpDesde != null)
            {
                dtpDesde.ValueChanged += (s, e) => lblError.Text = "";
            }

            EstiloHorarios.PrepararDialogo(this, "Quitar horas", new Size(Ancho, lblError.Bottom + 8 + 56));
            Controls.Add(EstiloAdmin.TituloDialogo("Quitar horas", Ancho - 48));
            // Como antes: el boton en rojo y Enter cancela; quitar se pulsa a proposito.
            EstiloAdmin.Peligroso(EstiloAdmin.BotonesDialogo(this, "Quitar", Aceptar));
            AcceptButton = CancelButton as Button;
            ActiveControl = rbOtroHorario;
        }

        private void Aceptar()
        {
            if (dtpDesde != null && (Fecha < minimo || Fecha > maximo))
            {
                lblError.Text = "Elige una fecha del " + minimo.ToString("dd/MM/yyyy") + " al " + maximo.ToString("dd/MM/yyyy") + ".";
                return;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
