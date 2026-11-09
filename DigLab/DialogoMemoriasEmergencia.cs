using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// Laboratorios > "Memorias de emergencia...": las memorias USB que abren el
    /// escritorio de cualquier equipo del laboratorio sin bitacora (db/28). Se
    /// registran con la memoria conectada a esta computadora, un nombre para
    /// reconocerla y la contrasena de quien la registra; se quitan si se pierden.
    /// Ya no se autoriza ninguna al instalar los equipos (pedido del 2026-11-07).
    /// </summary>
    internal sealed class DialogoMemoriasEmergencia : Form
    {
        // A 760 se cortaban el modelo de la memoria y quien la registro.
        private const int Ancho = 880;
        private static readonly Color Gris = Color.FromArgb(80, 80, 80);

        private readonly DataGridView grid;
        private readonly Label lblEstado;
        private readonly Label lblError;
        private readonly Button btnRegistrar;
        private readonly Button btnQuitar;
        private List<MemoriaRegistrada> memorias = new List<MemoriaRegistrada>();

        public static void Mostrar(IWin32Window dueno)
        {
            if (Permisos.Bloquear(dueno, "Abrir las memorias de emergencia")) return;
            using (DialogoMemoriasEmergencia dialogo = new DialogoMemoriasEmergencia())
            {
                dialogo.ShowDialog(dueno);
            }
        }

        private DialogoMemoriasEmergencia()
        {
            EstiloHorarios.PrepararDialogo(this, "Memorias de emergencia", new Size(Ancho, 540));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Memorias de emergencia", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 56),
                Size = new Size(Ancho - 48, 58),
                Text = "Cualquiera de estas memorias USB abre el escritorio de cualquier equipo del laboratorio sin bitácora, "
                    + "con «Acceso de emergencia» en la pantalla de inicio. Guárdalas como las llaves del laboratorio: "
                    + "si una se pierde, quítala aquí."
            });

            grid = EstiloHorarios.CrearGrid(new Point(24, 122), new Size(Ancho - 48, 236));
            grid.Columns.Add("nombre", "Nombre");
            grid.Columns.Add("memoria", "Memoria");
            grid.Columns.Add("registrada", "Registrada");
            grid.Columns.Add("por", "Por");
            EstiloHorarios.Pesos(grid, 210, 260, 110, 200);
            grid.SelectionChanged += (s, e) => ActualizarBotones();
            Controls.Add(grid);

            lblEstado = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 366),
                Size = new Size(Ancho - 48, 40)
            };
            Controls.Add(lblEstado);
            lblError = EstiloAdmin.LineaError(new Point(24, 410), Ancho - 48, 40);
            Controls.Add(lblError);

            ClientSize = new Size(Ancho, ClientSize.Height + EstiloAdmin.AireBajoBotones);
            int y = ClientSize.Height - 56 - EstiloAdmin.AireBajoBotones;
            Button cerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(Ancho - 24 - 120, y), new Size(120, 40));
            cerrar.DialogResult = DialogResult.Cancel;
            CancelButton = cerrar;
            const string textoRegistrar = "Registrar la memoria conectada...";
            int anchoRegistrar = TextRenderer.MeasureText(textoRegistrar, new DrawingFont("Century Gothic", 10F, FontStyle.Bold)).Width + 30;
            btnRegistrar = EstiloAdmin.BotonPrincipal(textoRegistrar, new Point(cerrar.Left - 10 - anchoRegistrar, y), new Size(anchoRegistrar, 40));
            btnRegistrar.Click += (s, e) => Registrar();
            btnQuitar = EstiloAdmin.Peligroso(EstiloHorarios.CrearBoton("Quitar", new Point(24, y), new Size(120, 40)));
            btnQuitar.Click += (s, e) => Quitar();
            Controls.AddRange(new Control[] { btnQuitar, btnRegistrar, cerrar });
            AcceptButton = btnRegistrar;
            ActiveControl = btnRegistrar;
            Recargar(null);
        }

        /// <summary>Vuelve a leer la lista; con id, deja elegida esa.</summary>
        private void Recargar(int? elegir)
        {
            grid.Rows.Clear();
            memorias = new List<MemoriaRegistrada>();
            try
            {
                if (!MemoriasEmergenciaDatos.Disponibles())
                {
                    btnRegistrar.Enabled = false;
                    lblEstado.ForeColor = Color.FromArgb(190, 85, 0);
                    lblEstado.Text = "Para registrarlas falta aplicar db/28_memorias_emergencia.sql en la base. "
                        + "Mientras, cada equipo acepta solo la memoria que se autorizó al instalarlo, si la tiene.";
                    ListaDigLab.PonerTextoVacio(grid, "Falta una actualización de la base (db/28).");
                    ActualizarBotones();
                    return;
                }

                memorias = MemoriasEmergenciaDatos.Vigentes();
            }
            catch (Exception ex)
            {
                lblError.Text = DialogoTextoDeFallo("Leer las memorias de emergencia", "No se pudo leer la lista", ex);
                ActualizarBotones();
                return;
            }

            foreach (MemoriaRegistrada memoria in memorias)
            {
                int fila = grid.Rows.Add(memoria.Nombre, string.IsNullOrEmpty(memoria.Modelo) ? "—" : memoria.Modelo,
                    memoria.AgregadaEn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    string.IsNullOrEmpty(memoria.AgregadaPor) ? "—" : memoria.AgregadaPor);
                grid.Rows[fila].Tag = memoria;
            }

            ListaDigLab.PonerTextoVacio(grid, memorias.Count == 0 ? "Todavía no hay ninguna registrada." : null);
            if (elegir.HasValue)
            {
                EstiloHorarios.SeleccionarFila(grid, f => ((MemoriaRegistrada)f.Tag).Id == elegir.Value);
            }

            if (lblEstado.ForeColor != Color.DarkGreen || string.IsNullOrEmpty(lblEstado.Text))
            {
                DecirComoEsta();
            }

            ActualizarBotones();
        }

        private void DecirComoEsta()
        {
            lblEstado.ForeColor = Gris;
            lblEstado.Text = memorias.Count == 0
                ? "Sin ninguna registrada, cada equipo acepta solo la memoria que se autorizó al instalarlo, si la tiene. "
                    + "Conecta una memoria a esta computadora y regístrala."
                : "Los equipos con conexión se enteran de lo que registres o quites en menos de 10 minutos; "
                    + "los que estén sin conexión, al volver a tenerla.";
        }

        private void ActualizarBotones()
        {
            btnQuitar.Enabled = Elegida() != null;
        }

        private MemoriaRegistrada Elegida()
        {
            return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as MemoriaRegistrada;
        }

        private void Registrar()
        {
            if (Permisos.Bloquear(this, "Registrar una memoria de emergencia")) return;
            lblError.Text = "";
            List<MemoriaUsb> conectadas = Emergencia.Conectadas();
            if (conectadas.Count == 0)
            {
                lblError.Text = "No veo ninguna memoria USB conectada a esta computadora: conecta la que quieras registrar.";
                return;
            }

            if (conectadas.Count > 1)
            {
                lblError.Text = "Hay " + conectadas.Count + " memorias USB conectadas a esta computadora: deja solo la que quieras registrar.";
                return;
            }

            MemoriaUsb memoria = conectadas[0];
            if (!Emergencia.SerieConfiable(memoria.Serie))
            {
                lblError.Text = "Esa memoria no tiene un número de serie propio: otras iguales dicen lo mismo y también abrirían los equipos. Usa otra memoria.";
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                string yaEsta = MemoriasEmergenciaDatos.YaRegistrada(memoria.Serie);
                if (yaEsta != null)
                {
                    lblError.Text = "Esa memoria ya está registrada como «" + yaEsta + "».";
                    return;
                }
            }
            catch (Exception ex)
            {
                lblError.Text = DialogoTextoDeFallo("Comprobar la memoria conectada", "No se pudo comprobar la memoria", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            using (DialogoRegistrarMemoria dialogo = new DialogoRegistrarMemoria(memoria))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                lblEstado.ForeColor = Color.DarkGreen;
                lblEstado.Text = "Registrada: «" + dialogo.Nombre + "». Los equipos con conexión la reciben en menos de 10 minutos; "
                    + "los que estén sin conexión, al volver a tenerla.";
                Recargar(dialogo.Id);
            }
        }

        private void Quitar()
        {
            if (Permisos.Bloquear(this, "Quitar una memoria de emergencia")) return;
            MemoriaRegistrada memoria = Elegida();
            if (memoria == null)
            {
                return;
            }

            lblError.Text = "";
            string detalle = "Deja de abrir los equipos en menos de 10 minutos; los que estén sin conexión, al volver a tenerla."
                + (memorias.Count == 1 ? " Sin ninguna, cada equipo acepta solo la que se autorizó al instalarlo." : "");
            if (!DialogoConfirmacion.Preguntar(this, "Quitar memoria de emergencia", "¿Quitar «" + memoria.Nombre + "»?", detalle, "Quitar"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                MemoriasEmergenciaDatos.Quitar(memoria.Id);
            }
            catch (Exception ex)
            {
                lblError.Text = DialogoTextoDeFallo("Quitar la memoria de emergencia n.o " + memoria.Id, "No se pudo quitar", ex);
                Recargar(null);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            lblEstado.ForeColor = Color.DarkGreen;
            lblEstado.Text = "Quitada: «" + memoria.Nombre + "». Deja de abrir los equipos con conexión en menos de 10 minutos; "
                + "los que estén sin conexión, al volver a tenerla.";
            Recargar(null);
        }

        /// <summary>Lo de DigLab (ErrorDeDatos) tal cual; lo demas, con su causa. Todo al log.</summary>
        private static string DialogoTextoDeFallo(string contexto, string queNoSePudo, Exception ex)
        {
            if (ex is ErrorDeDatos)
            {
                Log.Aviso(contexto + ": " + ex.Message);
                return ex.Message;
            }

            Log.Error(contexto, ex);
            return AvisoDeFalloAdmin.Texto(queNoSePudo, ex);
        }
    }

    /// <summary>El nombre de la memoria conectada y la contrasena de quien la registra.</summary>
    internal sealed class DialogoRegistrarMemoria : Form
    {
        private const int Ancho = 560;
        private readonly MemoriaUsb memoria;
        private readonly TextBox txtNombre;
        private readonly Label lblError;

        public int Id { get; private set; }
        public string Nombre { get; private set; }

        public DialogoRegistrarMemoria(MemoriaUsb memoria)
        {
            this.memoria = memoria;
            EstiloHorarios.PrepararDialogo(this, "Registrar memoria de emergencia", new Size(Ancho, 330));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Registrar memoria de emergencia", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = EstiloAdmin.TextoOscuro,
                Location = new Point(24, 58),
                Size = new Size(Ancho - 48, 24),
                AutoEllipsis = true,
                Text = "Memoria conectada: " + (string.IsNullOrEmpty(memoria.Modelo) ? "sin nombre en Windows" : memoria.Modelo)
            });

            Controls.Add(EstiloHorarios.CrearEtiqueta("Nombre para reconocerla", new Point(24, 96)));
            txtNombre = EstiloHorarios.CrearTexto(new Point(24, 122), Ancho - 48);
            txtNombre.MaxLength = MemoriasEmergenciaDatos.LargoNombre;
            EstiloHorarios.PonerTextoGuia(txtNombre, "Ej.: Llavero de la responsable de CISCO");
            Controls.Add(txtNombre);

            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(24, 160),
                Size = new Size(Ancho - 48, 44),
                Text = "Abrirá el escritorio de cualquier equipo del laboratorio sin bitácora. Para registrarla, "
                    + "se te pedirá tu contraseña."
            });

            lblError = EstiloAdmin.LineaError(new Point(24, 210), Ancho - 48, 44);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Registrar", Guardar);
            ActiveControl = txtNombre;
        }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Registrar una memoria de emergencia")) return;
            string nombre = HistorialCambios.Limpiar(txtNombre.Text, MemoriasEmergenciaDatos.LargoNombre);
            if (nombre == null)
            {
                lblError.Text = "Escribe un nombre para reconocerla.";
                txtNombre.Focus();
                return;
            }

            if (!DialogoContrasenaAdmin.Confirmar(this, "¿Registrar esta memoria de emergencia?",
                "«" + nombre + "» abrirá el escritorio de cualquier equipo del laboratorio sin bitácora.", "Registrar",
                "Registrar una memoria de emergencia"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                Id = MemoriasEmergenciaDatos.Registrar(nombre, memoria);
                Nombre = nombre;
            }
            catch (ErrorDeDatos ex)
            {
                Log.Aviso("Registrar una memoria de emergencia: " + ex.Message);
                lblError.Text = ex.Message;
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Registrar una memoria de emergencia", ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo registrar", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
