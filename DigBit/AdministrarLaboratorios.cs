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
    /// Seccion Laboratorios del administrador: la lista con su uso (clases en el
    /// horario, sesiones, equipos que registraron hace poco) y un buscador, dar de
    /// alta, cambiar el nombre, ver su horario, sus codigos y quitarlo. Quitar decide que hacer: uno que
    /// nunca se uso se borra; uno con historial se deshabilita; uno con clases
    /// vigentes no se deja quitar hasta mover o quitar esas clases. "Habilitar o
    /// deshabilitar..." apaga varios a la vez, aunque tengan clases, conservando su
    /// horario (LaboratoriosDeshabilitados). En solo consulta (el encargado de
    /// laboratorio) solo se busca y se ve su horario.
    /// </summary>
    public class AdministrarLaboratorios : PantallaAdmin
    {
        private readonly LaboratoriosDatos datos = new LaboratoriosDatos();
        private List<LaboratorioInfo> laboratorios = new List<LaboratorioInfo>();
        // El encargado de laboratorio: los botones que escriben no se crean (quedan
        // "Ver su horario" y "Cerrar") y el doble clic abre el horario.
        private readonly bool soloConsulta = Permisos.SoloConsulta;

        private readonly TextBox txtBuscar;
        private readonly Button btnRenombrar;
        private readonly Button btnHorario;
        private readonly Button btnQuitar;
        private readonly Button btnCodigos;
        private readonly Button btnHabilitar;

        public AdministrarLaboratorios()
            // La responsable no instala equipos: a ella, que hay aqui (sin instrucciones de instalacion).
            : base("Laboratorios", Permisos.SoloConsulta
                ? "Cuánto se usa cada laboratorio y cómo se entra a sus clases. Doble clic para ver su horario. F5 actualiza."
                : "Cada equipo se instala con el nombre exacto de su laboratorio. Doble clic para cambiarlo. F5 actualiza.", new Size(1100, 600))
        {
            if (!soloConsulta)
            {
                BotonPrincipalArriba("Nuevo laboratorio", 190).Click += (s, e) => NuevoLaboratorio();
            }

            // Con muchos laboratorios, el que se busca (por cualquier parte del nombre).
            txtBuscar = CrearBuscador(new Point(Margen, 112), 330);
            CrearLista(156);
            if (!soloConsulta)
            {
                // En la fila del buscador y no abajo: abajo ya no cabe sin tapar el resumen.
                btnHabilitar = EstiloHorarios.CrearBoton("Habilitar o deshabilitar...", new Point(ClientSize.Width - Margen - 240, 108), new Size(240, 38));
                btnHabilitar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnHabilitar.Click += (s, e) => HabilitarODeshabilitar();
                ayuda.SetToolTip(btnHabilitar, "Para que un laboratorio no corra todavía, conservando su horario: sin código, sin registros y sin faltas.");
                Controls.Add(btnHabilitar);
            }

            grid.Columns.Add("nombre", "Laboratorio");
            grid.Columns.Add("codigos", "Códigos");
            grid.Columns.Add("clases", "Franjas en el horario");
            grid.Columns.Add("sesiones", "Sesiones");
            grid.Columns.Add("ultima", "Última sesión");
            grid.Columns.Add("equipos", "Equipos (30 días)");
            grid.Columns.Add("estado", "Estado");
            EstiloHorarios.Pesos(grid, 230, 170, 120, 80, 110, 120, 100);
            foreach (string columna in new[] { "clases", "sesiones", "equipos" })
            {
                grid.Columns[columna].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }

            ayuda.SetToolTip(grid, "Equipos (30 días): cuántas computadoras distintas registraron bitácoras en ese laboratorio el último mes.");

            Button cerrar = BotonAbajo("Cerrar", 110);
            cerrar.Click += (s, e) => Close();
            CancelButton = cerrar;
            SeparacionAbajo(20);
            if (!soloConsulta)
            {
                btnQuitar = BotonAbajo("Quitar", 120);
                btnQuitar.Click += (s, e) => QuitarOReactivar();
            }

            btnHorario = BotonAbajo("Ver su horario", 150);
            btnHorario.Click += (s, e) => VerHorarioSeleccionado();
            if (!soloConsulta)
            {
                btnRenombrar = BotonAbajo("Cambiar nombre", 160);
                btnRenombrar.Click += (s, e) => RenombrarSeleccionado();
                // Como entran los alumnos de ESTE laboratorio (sin codigos, codigo unico o
                // generados). Toda la escuela y varios a la vez: "Codigos de las clases".
                // La columna Codigos (la forma, no un codigo) se queda tambien en solo consulta.
                btnCodigos = BotonAbajo("Códigos...", 120);
                btnCodigos.Click += (s, e) => CambiarCodigos();
                ayuda.SetToolTip(btnCodigos, "Cómo se entra a las clases de este laboratorio: «"
                    + ModosDeAcceso.Nombre(ModoCodigo.SinCodigo) + "», «" + ModosDeAcceso.Nombre(ModoCodigo.Fijo) + "» o «"
                    + ModosDeAcceso.Nombre(ModoCodigo.Renovable) + "».");
            }

            // Si se cambian desde el panel o Ajustes, la columna Códigos lo dice al momento.
            ModosDeAcceso.Cambio += ModosCambiaron;
            FormClosed += (s, e) => ModosDeAcceso.Cambio -= ModosCambiaron;
            ActiveControl = txtBuscar;
            ActualizarBotones();
        }

        /// <summary>F5: vuelve a leer la lista, como en Bitacoras (la fila elegida se conserva).</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
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

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>El administrador pide ver el horario de un laboratorio (lo abre el panel principal).</summary>
        public event Action<int> VerHorario;

        private void ModosCambiaron()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(ModosCambiaron));
                return;
            }

            Recargar(null);
        }

        protected override void AlBuscar()
        {
            Aplicar(null);
        }

        protected override void Recargar(string seleccionar)
        {
            try
            {
                laboratorios = datos.Listar();
            }
            catch (Exception ex)
            {
                laboratorios = new List<LaboratorioInfo>();
                AvisoDeFalloAdmin.Mostrar(this, "Leer la lista de laboratorios", ex, "No se pudo leer la lista de laboratorios", Text);
            }

            Aplicar(seleccionar);
        }

        private void Aplicar(string seleccionar)
        {
            // Los deshabilitados, al final.
            List<LaboratorioInfo> ordenados = laboratorios
                .Where(l => EstiloAdmin.Coincide(EstiloAdmin.Normalizar(l.Nombre), txtBuscar.Text))
                .OrderBy(l => !l.Activo).ThenBy(l => l.Nombre).ToList();
            LlenarLista(ordenados,
                l => new object[]
                {
                    l.Nombre,
                    ModosDeAcceso.Nombre(l.Modo),
                    l.FranjasVigentes,
                    l.Sesiones,
                    EstiloAdmin.Fecha(l.UltimaSesion),
                    l.EquiposRecientes,
                    l.Activo ? "En uso" : "Deshabilitado"
                },
                l => !l.Activo, l => l.Nombre, seleccionar);

            int deshabilitados = laboratorios.Count(l => !l.Activo);
            // En medio de la lista vacia, por que lo esta (no un rectangulo blanco).
            ListaDigBit.PonerTextoVacio(grid, laboratorios.Count == 0 ? "Todavía no hay laboratorios."
                : "Ningún laboratorio coincide con la búsqueda. Borra la búsqueda.");
            lblResumen.Text = laboratorios.Count == 0
                ? "Todavía no hay laboratorios."
                : ordenados.Count < laboratorios.Count
                ? "Se ven " + ordenados.Count + " de " + laboratorios.Count + " laboratorios."
                : (laboratorios.Count - deshabilitados) + ((laboratorios.Count - deshabilitados) == 1 ? " laboratorio en uso" : " laboratorios en uso")
                  + (deshabilitados > 0 ? " y " + deshabilitados + (deshabilitados == 1 ? " deshabilitado" : " deshabilitados") : "");
            // En solo consulta no hay "Quitar": la nota no le dice nada.
            NotaFija = soloConsulta || datos.DesactivacionDisponible ? "" : "Para deshabilitar falta la migración 08 en la base.";
            // Ya se sabe si la base tiene los modos (db/11): el boton de Codigos.
            ActualizarBotones();
        }

        protected override void ActualizarBotones()
        {
            // Antes de crear los botones (la lista va primero) no hay nada que hacer.
            // "Ver su horario" esta siempre; los demas, solo si no es solo consulta.
            if (btnHorario == null)
            {
                return;
            }

            LaboratorioInfo laboratorio = Seleccionado<LaboratorioInfo>();
            btnHorario.Enabled = laboratorio != null && laboratorio.Activo;
            if (btnRenombrar != null)
            {
                btnRenombrar.Enabled = laboratorio != null;
            }

            if (btnCodigos != null)
            {
                btnCodigos.Enabled = datos.ModosDisponibles && laboratorio != null && laboratorio.Activo;
            }

            if (btnQuitar != null)
            {
                btnQuitar.Enabled = laboratorio != null;
                btnQuitar.Text = laboratorio != null && !laboratorio.Activo ? "Habilitar" : "Quitar";
            }

            if (btnHabilitar != null)
            {
                btnHabilitar.Enabled = datos.DesactivacionDisponible && laboratorios.Count > 0;
            }
        }

        protected override void AlDobleClic()
        {
            // En solo consulta no se cambia el nombre: el doble clic (y Enter) abre su horario.
            if (soloConsulta)
            {
                VerHorarioSeleccionado();
                return;
            }

            RenombrarSeleccionado();
        }

        /// <summary>Los codigos del laboratorio elegido (en uso). La lista se recarga sola (ModosDeAcceso.Cambio).</summary>
        private void CambiarCodigos()
        {
            if (Permisos.Bloquear(this, "Cambiar los codigos de un laboratorio")) return;

            LaboratorioInfo laboratorio = Seleccionado<LaboratorioInfo>();
            if (laboratorio == null || !laboratorio.Activo)
            {
                return;
            }

            try
            {
                if (DialogoCodigosLaboratorio.Mostrar(this, laboratorio))
                {
                    Recargar(laboratorio.Nombre);
                    LaboratorioInfo ahora = laboratorios.FirstOrDefault(l => l.Id == laboratorio.Id);
                    Avisar("Guardado: " + ModosDeAcceso.Nombre(ahora == null ? laboratorio.Modo : ahora.Modo) + ".");
                }
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Abrir los codigos de las clases", ex, "No se pudieron abrir los códigos del laboratorio", Text);
            }
        }

        private void NuevoLaboratorio()
        {
            if (Permisos.Bloquear(this, "Dar de alta un laboratorio")) return;

            using (DialogoLaboratorio dialogo = new DialogoLaboratorio(datos, null))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.NombreGuardado);
                    Avisar("Se dio de alta " + dialogo.NombreGuardado + ". Asígnale clases en Horarios e instala sus equipos con DigBit-Instalador.exe eligiendo este laboratorio.");
                }
            }
        }

        private void RenombrarSeleccionado()
        {
            if (Permisos.Bloquear(this, "Cambiar el nombre de un laboratorio")) return;

            LaboratorioInfo laboratorio = Seleccionado<LaboratorioInfo>();
            if (laboratorio == null)
            {
                return;
            }

            using (DialogoLaboratorio dialogo = new DialogoLaboratorio(datos, laboratorio))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    Recargar(dialogo.NombreGuardado);
                    Avisar("Ahora se llama " + dialogo.NombreGuardado + ". Vuelve a instalar sus equipos con DigBit-Instalador.exe (descongelados) y elige el nombre nuevo.");
                }
            }
        }

        /// <summary>
        /// El horario del elegido, si esta en uso: Horarios no tiene los deshabilitados.
        /// Por eso con ellos "Ver su horario" se apaga y el doble clic de solo
        /// consulta no hace nada.
        /// </summary>
        private void VerHorarioSeleccionado()
        {
            LaboratorioInfo laboratorio = Seleccionado<LaboratorioInfo>();
            if (laboratorio != null && laboratorio.Activo && VerHorario != null)
            {
                VerHorario(laboratorio.Id);
            }
        }

        /// <summary>Casillas con todos los laboratorios: marcados los que corren.</summary>
        private void HabilitarODeshabilitar()
        {
            if (Permisos.Bloquear(this, "Habilitar o deshabilitar laboratorios")) return;

            using (DialogoHabilitarLaboratorios dialogo = new DialogoHabilitarLaboratorios(datos, laboratorios))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    LaboratorioInfo elegido = Seleccionado<LaboratorioInfo>();
                    Recargar(elegido == null ? null : elegido.Nombre);
                    Avisar(dialogo.Resumen);
                }
            }
        }

        private void QuitarOReactivar()
        {
            if (Permisos.Bloquear(this, "Quitar o habilitar un laboratorio")) return;

            LaboratorioInfo laboratorio = Seleccionado<LaboratorioInfo>();
            if (laboratorio == null)
            {
                return;
            }

            try
            {
                if (!laboratorio.Activo)
                {
                    datos.Reactivar(laboratorio.Id);
                    Recargar(laboratorio.Nombre);
                    Avisar(laboratorio.Nombre + " ya está habilitado: sale en el panel y en Horarios, y sus clases vuelven a correr.");
                    return;
                }

                if (laboratorio.FranjasVigentes > 0)
                {
                    // Con clases vigentes no se puede: se explica y se ofrece ir a ellas.
                    if (MessageBox.Show(this,
                        laboratorio.Nombre + " tiene " + laboratorio.FranjasVigentes
                        + (laboratorio.FranjasVigentes == 1 ? " franja" : " franjas") + " de clases vigentes en el horario.\n\n"
                        + "Muévelas a otro laboratorio o quítalas en Horarios, y después vuelve a quitarlo. "
                        + "Si solo quieres que no corra por ahora, usa «Habilitar o deshabilitar...»: conserva su horario.\n\n¿Abrir su horario ahora?",
                        "Quitar laboratorio", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        VerHorarioSeleccionado();
                    }

                    return;
                }

                if (laboratorio.SinUso)
                {
                    if (DialogoConfirmacion.Preguntar(this, "Quitar laboratorio",
                        "¿Borrar " + laboratorio.Nombre + "?",
                        "Nunca tuvo horario ni sesiones, así que se borra sin perder nada.",
                        "Borrar"))
                    {
                        datos.Borrar(laboratorio.Id);
                        Recargar(null);
                        Avisar("Se borró " + laboratorio.Nombre + ".");
                    }

                    return;
                }

                if (!datos.DesactivacionDisponible)
                {
                    MessageBox.Show(this,
                        laboratorio.Nombre + " tiene historial (" + laboratorio.Sesiones + " sesiones), así que no se borra: se deshabilita. "
                        + "Para eso falta aplicar db/08_migracion_bajas.sql en la base de datos: pide a quien administra el servidor que la aplique.",
                        "Quitar laboratorio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (DialogoConfirmacion.Preguntar(this, "Quitar laboratorio",
                    "¿Deshabilitar " + laboratorio.Nombre + "?",
                    "Tiene historial (" + laboratorio.Sesiones + (laboratorio.Sesiones == 1 ? " sesión" : " sesiones")
                    + "), así que no se borra: deja de salir en el panel y en Horarios, y sus bitácoras se conservan en los informes. "
                    + "Puedes habilitarlo cuando quieras.",
                    "Deshabilitar"))
                {
                    datos.Desactivar(laboratorio.Id);
                    Recargar(laboratorio.Nombre);
                    Avisar("Se deshabilitó " + laboratorio.Nombre + ".");
                }
            }
            catch (ErrorDeDatos ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AvisoDeFalloAdmin.Mostrar(this, "Quitar o habilitar el laboratorio " + laboratorio.Nombre, ex, "No se pudo cambiar el laboratorio", Text);
            }
        }
    }

    /// <summary>
    /// Que laboratorios corren: marcados, los habilitados. Para instalar DigBit en uno solo sin
    /// armar otra plantilla: los demas conservan su horario y no generan nada hasta habilitarlos.
    /// </summary>
    internal class DialogoHabilitarLaboratorios : Form
    {
        private readonly LaboratoriosDatos datos;
        private readonly List<LaboratorioInfo> laboratorios;
        private readonly CheckedListBox lista;
        private readonly Label lblError;

        public DialogoHabilitarLaboratorios(LaboratoriosDatos datos, IEnumerable<LaboratorioInfo> laboratorios)
        {
            this.datos = datos;
            this.laboratorios = laboratorios.OrderBy(l => l.Nombre).ToList();
            EstiloHorarios.PrepararDialogo(this, "Habilitar o deshabilitar", new Size(600, 548));
            Controls.Add(EstiloAdmin.TituloDialogo("Habilitar o deshabilitar laboratorios", 552));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(24, 58),
                Size = new Size(552, 24),
                Text = "Marca los laboratorios donde ya se usa DigBit."
            });

            lista = new CheckedListBox
            {
                CheckOnClick = true,
                Font = new DrawingFont("Century Gothic", 10F),
                IntegralHeight = false,
                Location = new Point(24, 88),
                Size = new Size(552, 220)
            };
            foreach (LaboratorioInfo laboratorio in this.laboratorios)
            {
                lista.Items.Add(laboratorio.Nombre + "  (" + laboratorio.FranjasVigentes + (laboratorio.FranjasVigentes == 1 ? " franja)" : " franjas)"), laboratorio.Activo);
            }

            Controls.Add(lista);
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = EstiloAdmin.TextoOscuro,
                Location = new Point(24, 322),
                Size = new Size(552, 24),
                Text = "Los que quedan sin marcar:"
            });
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(24, 348),
                Size = new Size(552, 88),
                Text = "•  Conservan su horario, que vuelve tal cual al habilitarlos.\n"
                    + "•  Sus docentes no ven esas clases ni su código.\n"
                    + "•  Sus equipos no aceptan registros.\n"
                    + "•  Esos días no cuentan faltas ni alertas, tampoco después de habilitarlos."
            });

            lblError = EstiloAdmin.LineaError(new Point(24, 440), 552, 44);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            ActiveControl = lista;
        }

        /// <summary>Que cambio, para el aviso de la pantalla.</summary>
        public string Resumen { get; private set; }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Habilitar o deshabilitar laboratorios")) return;

            List<LaboratorioInfo> apagar = laboratorios.Where((l, i) => l.Activo && !lista.GetItemChecked(i)).ToList();
            List<LaboratorioInfo> encender = laboratorios.Where((l, i) => !l.Activo && lista.GetItemChecked(i)).ToList();
            if (apagar.Count == 0 && encender.Count == 0)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }

            List<string> partes = new List<string>();
            if (apagar.Count > 0)
            {
                partes.Add("Se deshabilita" + (apagar.Count == 1 ? " " : "n ") + Nombres(apagar) + ": desde ahora no corre" + (apagar.Count == 1 ? "" : "n") + ".");
            }

            if (encender.Count > 0)
            {
                partes.Add("Se habilita" + (encender.Count == 1 ? " " : "n ") + Nombres(encender) + ": sus clases corren desde ahora.");
            }

            if (apagar.Count == laboratorios.Count)
            {
                partes.Add("No queda ningún laboratorio en uso.");
            }

            if (!DialogoConfirmacion.Preguntar(this, "Habilitar o deshabilitar", "¿Guardar estos cambios?", string.Join(" ", partes), "Guardar", false))
            {
                return;
            }

            try
            {
                datos.CambiarHabilitados(apagar.Select(l => l.Id).ToList(), encender.Select(l => l.Id).ToList());
                // Corto: cabe en la linea de abajo de la pantalla; el detalle ya se confirmo.
                Resumen = "Guardado: " + string.Join(" y ", new[]
                {
                    apagar.Count == 0 ? null : apagar.Count + (apagar.Count == 1 ? " deshabilitado" : " deshabilitados"),
                    encender.Count == 0 ? null : encender.Count + (encender.Count == 1 ? " habilitado" : " habilitados")
                }.Where(p => p != null)) + ".";
                DialogResult = DialogResult.OK;
            }
            catch (ErrorDeDatos ex)
            {
                lblError.Text = ex.Message;
            }
            catch (Exception ex)
            {
                Infraestructura.Log.Error("Habilitar o deshabilitar laboratorios", ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo guardar", ex);
            }
        }

        private static string Nombres(List<LaboratorioInfo> lista)
        {
            return RevisionCiclo.Enumerar(lista.Select(l => l.Nombre).ToList());
        }
    }

    /// <summary>Alta de un laboratorio o cambio de su nombre, con lo que eso implica para sus equipos.</summary>
    internal class DialogoLaboratorio : Form
    {
        private readonly LaboratoriosDatos datos;
        private readonly LaboratorioInfo existente;
        private readonly TextBox txtNombre;
        private readonly Label lblError;

        public DialogoLaboratorio(LaboratoriosDatos datos, LaboratorioInfo existente)
        {
            this.datos = datos;
            this.existente = existente;
            bool nuevo = existente == null;
            // Con el error en cuatro lineas: uno de conexion o de la base dice la causa y
            // que hacer, y el de un equipo sin permiso (1142) ocupa cuatro.
            EstiloHorarios.PrepararDialogo(this, nuevo ? "Nuevo laboratorio" : "Cambiar nombre", new Size(520, 362));
            Controls.Add(EstiloAdmin.TituloDialogo(nuevo ? "Nuevo laboratorio" : "Cambiar el nombre de " + existente.Nombre, 472));

            Controls.Add(EstiloHorarios.CrearEtiqueta("Nombre *", new Point(24, 62)));
            txtNombre = EstiloHorarios.CrearTexto(new Point(24, 85), 472);
            txtNombre.MaxLength = 80;
            txtNombre.Text = nuevo ? "" : existente.Nombre;
            txtNombre.TextChanged += (s, e) => { txtNombre.BackColor = SystemColors.Window; lblError.Text = ""; };
            Controls.Add(txtNombre);

            // Lo que hay que hacer despues en los equipos, que se instalan con el nombre:
            // con el instalador (DigBit-Instalador.exe), que es lo que usa el
            // administrador, no con los guiones de PowerShell.
            string nota = nuevo
                ? "Después instala DigBit en los equipos de este laboratorio con DigBit-Instalador.exe y elige este laboratorio en su lista. "
                  + "Sus clases se asignan en Horarios."
                : "Los equipos de " + existente.Nombre + " están instalados con ese nombre: hasta que vuelvas a instalarlos con "
                  + "DigBit-Instalador.exe (descongelados) y elijas el nombre nuevo, no reconocerán su laboratorio ni aceptarán códigos."
                  + (existente.EquiposRecientes > 0
                      ? " En el último mes registraron bitácoras " + existente.EquiposRecientes + (existente.EquiposRecientes == 1 ? " equipo." : " equipos.")
                      : "");
            // Hasta seis lineas: con un nombre largo la nota del cambio de nombre ocupa cinco.
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F, nuevo ? FontStyle.Regular : FontStyle.Bold),
                ForeColor = nuevo ? Color.FromArgb(80, 80, 80) : Color.FromArgb(190, 85, 0),
                Location = new Point(24, 122),
                Size = new Size(472, 100),
                Text = nota
            });

            lblError = EstiloAdmin.LineaError(new Point(24, 226), 472, 72);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, nuevo ? "Dar de alta" : "Cambiar nombre", Guardar);
            ActiveControl = txtNombre;
        }

        public string NombreGuardado { get; private set; }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, existente == null ? "Dar de alta un laboratorio" : "Cambiar el nombre de un laboratorio")) return;

            // Sin espacios de mas: el equipo compara el nombre tal cual.
            string nombre = string.Join(" ", txtNombre.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (nombre.Length == 0)
            {
                lblError.Text = "Escribe el nombre del laboratorio.";
                txtNombre.BackColor = EstiloAdmin.FondoError;
                txtNombre.Focus();
                return;
            }

            if (existente != null && nombre == existente.Nombre)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }

            try
            {
                if (existente == null)
                {
                    datos.Crear(nombre);
                }
                else
                {
                    datos.Renombrar(existente.Id, existente.Nombre, nombre);
                }

                NombreGuardado = nombre;
                DialogResult = DialogResult.OK;
            }
            catch (ErrorDeDatos ex)
            {
                lblError.Text = ex.Message;
                txtNombre.BackColor = EstiloAdmin.FondoError;
                txtNombre.Focus();
            }
            catch (Exception ex)
            {
                Infraestructura.Log.Error(existente == null ? "Alta de laboratorio" : "Cambiar el nombre del laboratorio " + existente.Nombre, ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo guardar", ex);
            }
        }
    }
}
