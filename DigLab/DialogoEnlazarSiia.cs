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
    /// "Docentes por enlazar" (desde Docentes o desde Alertas): arriba, quienes
    /// entraron con su contrasena del SIIA y esperan, con el numero de empleado
    /// y el nombre que dio el SIIA y un buscador que acepta el numero (el que le
    /// dice el docente a la administracion); abajo, los docentes de DigLab sin
    /// enlazar, con los que coinciden por nombre (o por numero) hasta arriba como
    /// sugerencia, y su buscador. La administracion elige y pulsa "Enlazar": el
    /// numero nunca se teclea a mano, viene de la solicitud. "Descartar
    /// solicitud" quita una que no corresponde a nadie. Cada accion pide
    /// confirmacion y queda en el historial (EnlacesSiia). La persona responsable
    /// del laboratorio (solo consulta) ve las dos listas, sin los botones.
    /// </summary>
    internal sealed class DialogoEnlazarSiia : Form
    {
        private const int Ancho = 1040;
        private const int Alto = 660;
        private static readonly Color Gris = Color.FromArgb(80, 80, 80);
        private static readonly Color Rojo = Color.FromArgb(190, 30, 30);
        private static readonly Color FondoSugerencia = Color.FromArgb(232, 245, 233);

        private readonly List<SolicitudSiia> solicitudes;
        private readonly List<ProfesorInfo> docentes;
        private readonly bool soloConsulta = Permisos.SoloConsulta;
        private readonly TextBox txtBuscarSolicitud;
        private readonly DataGridView gridSolicitudes;
        private readonly Label lblElegida;
        private readonly TextBox txtBuscarDocente;
        private readonly Label lblSeccion2;
        private readonly Label lblBuscarDocente;
        private readonly DataGridView gridDocentes;
        private readonly Label lblMensaje;
        private readonly Button btnEnlazar;
        private readonly Button btnDescartar;
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };

        /// <summary>True si se enlazo o se descarto alguna: quien lo abrio vuelve a leer su lista.</summary>
        public bool HuboCambios { get; private set; }

        /// <param name="pendientes">Las solicitudes pendientes (EnlacesSiia.Leer).</param>
        /// <param name="sinEnlazar">Los docentes activos que aun no estan enlazados.</param>
        /// <param name="numero">Una solicitud para elegir al abrir, o null.</param>
        public DialogoEnlazarSiia(List<SolicitudSiia> pendientes, List<ProfesorInfo> sinEnlazar, string numero)
        {
            solicitudes = new List<SolicitudSiia>(pendientes);
            docentes = new List<ProfesorInfo>(sinEnlazar);
            EstiloHorarios.PrepararDialogo(this, "Docentes por enlazar con el SIIA" + (soloConsulta ? " (solo consulta)" : ""), new Size(Ancho, Alto));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Docentes por enlazar con el SIIA", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 52),
                Size = new Size(Ancho - 48, 40),
                Text = "Entraron con su contraseña del SIIA y esperan que los enlaces con su cuenta de DigLab. Busca el número que te diga el docente, "
                    + "elige abajo quién es en la lista de docentes y pulsa «Enlazar»: desde ese momento entra con ese número, con sus clases."
            });

            // 1. Quien entro con el SIIA. Un poco mas abajo (en 98 se pegaba al texto de
            // arriba), y cada buscador con su rotulo "Buscar" (Acomodar los coloca).
            Controls.Add(Seccion("1. Quién entró con el SIIA", new Point(24, 106)));
            txtBuscarSolicitud = Buscador(new Point(Ancho - 24 - 320, 102), "Número de empleado o nombre");
            txtBuscarSolicitud.TextChanged += (s, e) => LlenarSolicitudes(null);
            RotuloBuscar(txtBuscarSolicitud);
            gridSolicitudes = Lista(new Point(24, 140), new Size(Ancho - 48, 170));
            gridSolicitudes.Columns.Add("numero", "No. de empleado");
            gridSolicitudes.Columns.Add("nombre", "Nombre en el SIIA");
            gridSolicitudes.Columns.Add("tipo", "Cuenta del SIIA");
            gridSolicitudes.Columns.Add("correo", "Correo en el SIIA");
            gridSolicitudes.Columns.Add("ultimo", "Último intento");
            gridSolicitudes.Columns.Add("intentos", "Intentos");
            EstiloHorarios.Pesos(gridSolicitudes, 120, 280, 110, 230, 130, 80);
            gridSolicitudes.Columns["intentos"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            gridSolicitudes.SelectionChanged += (s, e) => SolicitudElegida();
            Controls.Add(gridSolicitudes);

            // 2. A que docente de DigLab corresponde: justo debajo de la lista de arriba.
            lblSeccion2 = Seccion("2. Quién es en DigLab", new Point(24, 316));
            Controls.Add(lblSeccion2);
            txtBuscarDocente = Buscador(new Point(Ancho - 24 - 320, 312), "Nombre o número de DigLab");
            txtBuscarDocente.TextChanged += (s, e) => LlenarDocentes();
            lblBuscarDocente = RotuloBuscar(txtBuscarDocente);
            lblElegida = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 342),
                Size = new Size(Ancho - 48, 28)
            };
            Controls.Add(lblElegida);
            gridDocentes = Lista(new Point(24, 374), new Size(Ancho - 48, 190));
            gridDocentes.Columns.Add("sugerencia", "Sugerencia");
            gridDocentes.Columns.Add("numero", "No. en DigLab");
            gridDocentes.Columns.Add("nombre", "Nombre");
            gridDocentes.Columns.Add("clases", "Clases vigentes");
            gridDocentes.Columns.Add("laboratorios", "Laboratorios");
            EstiloHorarios.Pesos(gridDocentes, 130, 120, 300, 100, 300);
            gridDocentes.Columns["clases"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            gridDocentes.SelectionChanged += (s, e) => ActualizarBotones();
            gridDocentes.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Enlazar(); };
            Controls.Add(gridDocentes);

            btnDescartar = EstiloHorarios.CrearBoton("Descartar solicitud", new Point(24, Alto - 56), new Size(180, 40));
            btnDescartar.Click += (s, e) => Descartar();
            ayuda.SetToolTip(btnDescartar, "Para una solicitud que no corresponde a ningún docente: sale de esta lista y de Alertas. No borra ninguna cuenta.");
            Controls.Add(btnDescartar);
            lblMensaje = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                ForeColor = Rojo,
                Location = new Point(216, Alto - 64),
                Size = new Size(Ancho - 216 - 24 - 120 - 10 - 130 - 12, 56)
            };
            Controls.Add(lblMensaje);
            btnEnlazar = EstiloAdmin.BotonesDialogo(this, "Enlazar", Enlazar);
            ((Button)CancelButton).Text = "Cerrar";
            // Enter en una caja de busqueda no enlaza: solo el boton.
            AcceptButton = null;
            if (soloConsulta)
            {
                // Nunca los usa: no se ven (apagados parecian descompuestos). A cambio, que hace cada quien.
                btnEnlazar.Visible = false;
                btnDescartar.Visible = false;
                lblMensaje.Left = 24;
                lblMensaje.Width = ((Button)CancelButton).Left - 12 - 24;
                lblMensaje.ForeColor = Gris;
                lblMensaje.Text = "Solo la administración enlaza y descarta: aquí ves quién espera.";
            }

            LlenarSolicitudes(numero);
            gridSolicitudes.ColumnHeadersHeightChanged += (s, e) => Acomodar();
            gridDocentes.ColumnHeadersHeightChanged += (s, e) => Acomodar();
            Acomodar();
            ActiveControl = txtBuscarSolicitud;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Con la ventana creada, los encabezados ya tienen su alto de verdad.
            Acomodar();
        }

        /// <summary>
        /// La lista de arriba, del alto de su encabezado y de 2 a 4 renglones (casi
        /// siempre espera una o dos personas: con 170 px fijos quedaba un hueco
        /// blanco); la seccion 2 empieza 16 px debajo y su lista se queda con el
        /// resto, en renglones enteros.
        /// </summary>
        private void Acomodar()
        {
            const int Renglon = 34;
            int filas = Math.Min(4, Math.Max(2, solicitudes.Count));
            gridSolicitudes.Height = gridSolicitudes.ColumnHeadersHeight + filas * Renglon + 2;
            int y2 = gridSolicitudes.Bottom + 16;
            txtBuscarDocente.Top = y2;
            lblSeccion2.Top = y2 + (txtBuscarDocente.Height - lblSeccion2.Height) / 2;
            lblBuscarDocente.Top = y2 + (txtBuscarDocente.Height - lblBuscarDocente.Height) / 2;
            lblElegida.Top = txtBuscarDocente.Bottom + 6;
            gridDocentes.Top = lblElegida.Bottom + 4;
            int hasta = ((Button)CancelButton).Top - 16;
            int encabezado = gridDocentes.ColumnHeadersHeight;
            int renglones = Math.Max(2, (hasta - gridDocentes.Top - encabezado - 2) / Renglon);
            gridDocentes.Height = encabezado + renglones * Renglon + 2;
        }

        /// <summary>"Buscar", a la izquierda de su caja y a su altura.</summary>
        private Label RotuloBuscar(TextBox caja)
        {
            Label rotulo = EstiloHorarios.CrearEtiqueta("Buscar", Point.Empty);
            rotulo.AutoSize = true;
            Controls.Add(rotulo);
            rotulo.Location = new Point(caja.Left - 8 - rotulo.PreferredWidth, caja.Top + (caja.Height - rotulo.PreferredHeight) / 2);
            return rotulo;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Label Seccion(string texto, Point posicion)
        {
            Label seccion = EstiloHorarios.CrearEtiqueta(texto, posicion);
            seccion.Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
            return seccion;
        }

        private TextBox Buscador(Point posicion, string guia)
        {
            TextBox caja = EstiloHorarios.CrearTexto(posicion, 320);
            EstiloHorarios.PonerTextoGuia(caja, guia);
            Controls.Add(caja);
            return caja;
        }

        private static DataGridView Lista(Point posicion, Size tamano)
        {
            DataGridView lista = EstiloHorarios.CrearGrid(posicion, tamano);
            lista.RowTemplate.Height = 34;
            lista.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            lista.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            lista.MultiSelect = false;
            return lista;
        }

        private SolicitudSiia SolicitudActual
        {
            get { return gridSolicitudes.SelectedRows.Count == 0 ? null : gridSolicitudes.SelectedRows[0].Tag as SolicitudSiia; }
        }

        private ProfesorInfo DocenteActual
        {
            get { return gridDocentes.SelectedRows.Count == 0 ? null : gridDocentes.SelectedRows[0].Tag as ProfesorInfo; }
        }

        /// <summary>Las solicitudes que coinciden con la busqueda (numero, nombre o correo), con la de ese numero (o la primera) elegida.</summary>
        private void LlenarSolicitudes(string elegir)
        {
            SolicitudSiia antes = SolicitudActual;
            string buscada = elegir ?? (antes == null ? null : antes.Numero);
            string busqueda = EstiloAdmin.Normalizar(txtBuscarSolicitud.Text.Trim());
            gridSolicitudes.Rows.Clear();
            foreach (SolicitudSiia solicitud in solicitudes.Where(s =>
                EstiloAdmin.Coincide(EstiloAdmin.Normalizar(s.Numero + " " + s.NombreParaMostrar + " " + s.Correo), busqueda)))
            {
                int fila = gridSolicitudes.Rows.Add(solicitud.Numero, solicitud.NombreParaMostrar, solicitud.TipoParaMostrar, EstiloAdmin.Guion(solicitud.Correo),
                    solicitud.UltimoIntento.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), solicitud.Intentos);
                gridSolicitudes.Rows[fila].Tag = solicitud;
            }

            // Vacia, en medio de la lista: nadie espera, o nadie coincide con lo buscado.
            ListaDigLab.PonerTextoVacio(gridSolicitudes, solicitudes.Count == 0 ? "Nadie ha entrado con el SIIA sin enlazar: no hay nada que hacer aquí."
                : "Nadie coincide con la búsqueda. Borra la búsqueda.");
            gridSolicitudes.ClearSelection();
            if (buscada != null)
            {
                EstiloHorarios.SeleccionarFila(gridSolicitudes, f => ((SolicitudSiia)f.Tag).Numero == buscada);
            }

            if (gridSolicitudes.SelectedRows.Count == 0 && gridSolicitudes.Rows.Count > 0)
            {
                EstiloHorarios.SeleccionarFila(gridSolicitudes, f => f.Index == 0);
            }

            SolicitudElegida();
        }

        private void SolicitudElegida()
        {
            SolicitudSiia solicitud = SolicitudActual;
            lblElegida.Text = solicitud != null
                ? "Enlazar " + solicitud.Numero + " (" + solicitud.NombreParaMostrar + ") con:"
                : solicitudes.Count == 0 ? "No hay nadie por enlazar." : "Elige arriba quién entró con el SIIA.";
            LlenarDocentes();
        }

        /// <summary>
        /// Los docentes sin enlazar que coinciden con la busqueda; los que se
        /// parecen a la solicitud elegida (mismo numero o mismo nombre), hasta
        /// arriba y resaltados, y si hay uno, ya elegido. Es solo una sugerencia.
        /// </summary>
        private void LlenarDocentes()
        {
            SolicitudSiia solicitud = SolicitudActual;
            ProfesorInfo antes = DocenteActual;
            string busqueda = EstiloAdmin.Normalizar(txtBuscarDocente.Text.Trim());
            var filas = docentes
                .Where(d => EstiloAdmin.Coincide(EstiloAdmin.Normalizar(d.Matricula + " " + d.NombreCompleto), busqueda))
                .Select(d => new { Docente = d, Coincide = solicitud == null ? "" : EnlacesSiia.Coincidencia(solicitud, d) })
                .OrderBy(f => f.Coincide == "Mismo número" ? 0 : f.Coincide.Length > 0 ? 1 : 2)
                .ThenBy(f => f.Docente.NombreParaLista, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            gridDocentes.Rows.Clear();
            foreach (var f in filas)
            {
                int fila = gridDocentes.Rows.Add(f.Coincide.Length > 0 ? "★ " + f.Coincide : "", f.Docente.Matricula, f.Docente.NombreParaLista,
                    f.Docente.ClasesActivas, EstiloAdmin.Guion(f.Docente.Laboratorios));
                gridDocentes.Rows[fila].Tag = f.Docente;
                if (f.Coincide.Length > 0)
                {
                    gridDocentes.Rows[fila].DefaultCellStyle.BackColor = FondoSugerencia;
                }
            }

            ListaDigLab.PonerTextoVacio(gridDocentes, docentes.Count == 0 ? "Todos los docentes activos ya están enlazados con el SIIA."
                : "Ningún docente coincide con la búsqueda. Borra la búsqueda.");
            gridDocentes.ClearSelection();
            if (antes != null && solicitud != null && filas.Any(f => f.Docente.Id == antes.Id && f.Coincide.Length > 0))
            {
                EstiloHorarios.SeleccionarFila(gridDocentes, f => ((ProfesorInfo)f.Tag).Id == antes.Id);
            }
            else if (filas.Count(f => f.Coincide.Length > 0) == 1)
            {
                // Una sola sugerencia: queda elegida, pero enlaza la administracion al pulsar.
                EstiloHorarios.SeleccionarFila(gridDocentes, f => f.Index == 0);
            }

            ActualizarBotones();
        }

        private void ActualizarBotones()
        {
            if (soloConsulta)
            {
                return;
            }

            btnEnlazar.Enabled = SolicitudActual != null && DocenteActual != null;
            btnDescartar.Enabled = SolicitudActual != null;
        }

        private void Mensaje(bool bien, string texto)
        {
            lblMensaje.ForeColor = bien ? Color.DarkGreen : Rojo;
            lblMensaje.Text = texto;
        }

        private void Enlazar()
        {
            if (Permisos.Bloquear(this, "Enlazar un docente con el SIIA")) return;

            SolicitudSiia solicitud = SolicitudActual;
            ProfesorInfo docente = DocenteActual;
            if (solicitud == null || docente == null)
            {
                Mensaje(false, solicitud == null ? "Elige arriba quién entró con el SIIA." : "Elige abajo a qué docente de DigLab corresponde.");
                return;
            }

            bool coincide = EnlacesSiia.Coincidencia(solicitud, docente).Length > 0;
            bool cambiaNumero = !string.Equals(docente.Matricula, solicitud.Numero, StringComparison.OrdinalIgnoreCase);
            string detalle = "Desde ahora entra con el número " + solicitud.Numero + " y su contraseña del SIIA (en el SIIA: " + solicitud.NombreParaMostrar
                + "), con sus clases, sesiones y bitácoras de siempre."
                + (cambiaNumero ? " Su número en DigLab pasa de " + docente.Matricula + " a " + solicitud.Numero + "." : "")
                + (coincide ? "" : " Ojo: su nombre no coincide con el del SIIA; enlázalo solo si es la misma persona.");
            if (!DialogoConfirmacion.Preguntar(this, "Enlazar con el SIIA", "¿Enlazar a " + docente.NombreCompleto + " con el número " + solicitud.Numero + "?",
                detalle, "Enlazar", !coincide))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                EnlacesSiia.Enlazar(solicitud, docente);
            }
            catch (ErrorDeDatos ex)
            {
                Mensaje(false, ex.Message);
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Enlazar al docente id " + docente.Id + " con el SIIA", ex);
                Mensaje(false, AvisoDeFalloAdmin.Texto("No se pudo enlazar", ex));
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            HuboCambios = true;
            solicitudes.Remove(solicitud);
            docentes.Remove(docente);
            LlenarSolicitudes(null);
            Mensaje(true, "Listo: " + docente.NombreCompleto + " quedó enlazado con el número " + solicitud.Numero
                + ". Si está esperando en un equipo, entra en unos segundos.");
        }

        private void Descartar()
        {
            if (Permisos.Bloquear(this, "Descartar una solicitud de enlace con el SIIA")) return;

            SolicitudSiia solicitud = SolicitudActual;
            if (solicitud == null)
            {
                return;
            }

            if (!DialogoConfirmacion.Preguntar(this, "Descartar solicitud", "¿Descartar la solicitud de " + solicitud.Numero + " (" + solicitud.NombreParaMostrar + ")?",
                "Sale de esta lista y de Alertas; no borra ni cambia ninguna cuenta. Si vuelve a entrar con su contraseña del SIIA, su solicitud vuelve a aparecer.",
                "Descartar"))
            {
                return;
            }

            try
            {
                EnlacesSiia.Descartar(solicitud);
            }
            catch (ErrorDeDatos ex)
            {
                Mensaje(false, ex.Message);
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Descartar la solicitud de enlace de " + solicitud.Numero, ex);
                Mensaje(false, AvisoDeFalloAdmin.Texto("No se pudo descartar", ex));
                return;
            }

            HuboCambios = true;
            solicitudes.Remove(solicitud);
            LlenarSolicitudes(null);
            Mensaje(true, "Se descartó la solicitud de " + solicitud.Numero + ".");
        }
    }
}
