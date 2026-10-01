using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DigBit.Instalador
{
    /// <summary>
    /// La ventana del instalador: se elige que equipo es y, por debajo, se
    /// ejecuta deploy\configurar_equipo.ps1 con los datos de la ventana.
    ///   - Administrador y Maestro: DigBit como programa normal (una ventana y un
    ///     acceso directo en el escritorio); no se toca la cuenta ni se bloquea nada.
    ///   - Alumno: la maquina del laboratorio en modo kiosco (DigBit es la pantalla
    ///     de la cuenta del laboratorio, con el vigilante).
    /// Con todo dentro del .exe (Carga), la conexion a la base ya viene puesta y
    /// solo se teclea su contrasena. Desde un paquete que trae MySQL portatil se
    /// ofrece ademas montar un servidor de base de datos local.
    /// </summary>
    internal sealed class FormInstalador : Form
    {
        /// <summary>Donde deja DigBit configurar_equipo.ps1 (su -Destino por defecto).</summary>
        private const string CarpetaDestino = @"C:\DigBit";
        private const int AnchoSeccion = 720;
        private const int XCampo = 236;
        private const int AnchoCampo = 468;
        private static readonly Color Verde = Color.FromArgb(0, 122, 94);
        private static readonly Color Gris = Color.FromArgb(110, 110, 110);
        private const string PistaClave = "Distingue mayúsculas. No va en el instalador: se guarda solo aquí.";

        /// <summary>
        /// Las contraseñas llegan a los guiones en variables de entorno del
        /// PowerShell hijo, no escritas en el .ps1 temporal: ese archivo se quedaba
        /// en %TEMP% si se cerraba la ventana a media consulta, y si fallaba la
        /// línea que las llevaba, PowerShell la copiaba entera en el error.
        /// </summary>
        private const string VariableCadena = "DIGBIT_CADENA";
        private const string VariableClaveCuenta = "DIGBIT_CLAVE_CUENTA";
        private const string VariableClaveEquipo = "DIGBIT_CLAVE_EQUIPO";
        private const string VariableClaveAdmin = "DIGBIT_CLAVE_ADMIN";

        private enum Tipo { Ninguno, Adm, Maestro, Alumno, Servidor }

        private readonly RadioButton rbAdm = new RadioButton();
        private readonly RadioButton rbMaestro = new RadioButton();
        private readonly RadioButton rbAlumno = new RadioButton();
        private readonly RadioButton rbServidor = new RadioButton();

        private readonly Label lblConexion = new Label();
        private readonly LinkLabel lnkCambiarConexion = new LinkLabel();
        private readonly TextBox txtServidor = new TextBox();
        private readonly TextBox txtBase = new TextBox();
        private readonly TextBox txtUsuarioBd = new TextBox();
        private readonly TextBox txtClaveBd = new TextBox();
        private readonly Label lblPistaClave;

        private readonly TextBox txtNuevaClaveEquipo = new TextBox();
        private readonly TextBox txtNuevaClaveAdmin = new TextBox();

        private readonly ComboBox cboLaboratorio = new ComboBox();
        private readonly Button btnCargarLabs = new Button();
        private readonly TextBox txtNumeroMaquina = new TextBox();
        private readonly TextBox txtCuenta = new TextBox();
        private readonly TextBox txtClaveCuenta = new TextBox();
        private readonly TextBox txtCarpetaDatos = new TextBox();
        private readonly CheckBox chkMemoria = new CheckBox();
        private readonly Label lblMemoria = new Label();

        private readonly Button btnInstalar = new Button();
        private readonly Button btnDiagnostico = new Button();
        private readonly Label lblEstado = new Label();
        private readonly TextBox txtSalida = new TextBox();

        private readonly TableLayoutPanel disposicion;
        private readonly FlowLayoutPanel secciones = new FlowLayoutPanel();
        private readonly Panel pnlBase;
        private readonly Panel pnlConexion;
        private readonly Panel pnlClaveBd;
        private readonly Panel pnlClavesServidor;
        private readonly Panel pnlEquipo;

        /// <summary>Carpeta con deploy\: la extraida del .exe o la del paquete.</summary>
        private readonly string raiz;
        /// <summary>True si todo va dentro del .exe y se extrajo a una carpeta temporal.</summary>
        private readonly bool desdeCarga;
        /// <summary>Paquete completo con MySQL portatil: se puede montar un servidor local.</summary>
        private readonly bool hayServidorLocal;
        private readonly int puerto = 3306;
        private readonly string origenServidor = "";

        private bool conexionAbierta;
        private bool enMarcha;
        private bool cargandoLabs;

        /// <summary>Los .ps1 temporales que aún no se han borrado (ver EscribirGuion).</summary>
        private readonly HashSet<string> temporales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FormInstalador()
        {
            Text = "Instalador de DigBit";
            ClientSize = new Size(760, 790);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.White;
            Font = new Font("Century Gothic", 9F);

            // En una pantalla de 768 de alto, o con el texto al 125 %, la ventana
            // entera no cabe, y lo que quedaba fuera era el botón Instalar. Se
            // recorta a la pantalla y lo que no quepa se desplaza (AjustarDesplazamiento).
            int marco = Height - ClientSize.Height;
            ClientSize = new Size(ClientSize.Width,
                Math.Min(ClientSize.Height, Screen.FromPoint(Cursor.Position).WorkingArea.Height - marco));
            AutoScroll = true;

            // --- De donde sale lo que se instala -----------------------------------
            string errorCarga = null;
            if (Carga.Incluida)
            {
                try
                {
                    raiz = Carga.Extraer();
                    desdeCarga = true;
                }
                catch (Exception ex)
                {
                    errorCarga = ex.Message;
                }
            }
            else
            {
                raiz = Guiones.RaizPaquete();
            }

            hayServidorLocal = !desdeCarga && raiz != null
                && Directory.Exists(Path.Combine(raiz, "mysql"))
                && File.Exists(RutaPrepararBd());

            // --- Ventana -------------------------------------------------------------
            disposicion = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(20, 14, 20, 16)
            };
            disposicion.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            disposicion.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            secciones.FlowDirection = FlowDirection.TopDown;
            secciones.WrapContents = false;
            secciones.AutoSize = true;
            secciones.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            secciones.Margin = new Padding(0);
            disposicion.Controls.Add(secciones, 0, 0);

            txtSalida.Dock = DockStyle.Fill;
            txtSalida.Multiline = true;
            txtSalida.ReadOnly = true;
            txtSalida.ScrollBars = ScrollBars.Vertical;
            txtSalida.Font = new Font("Consolas", 8.5F);
            txtSalida.BackColor = Color.FromArgb(30, 30, 30);
            txtSalida.ForeColor = Color.Gainsboro;
            disposicion.Controls.Add(txtSalida, 0, 1);
            Controls.Add(disposicion);

            // --- Tipo de equipo --------------------------------------------------------
            Panel pnlTipo = NuevaSeccion("¿Qué equipo estás instalando?", hayServidorLocal ? 138 : 112);
            int y = 28;
            Opcion(pnlTipo, rbAdm, "Administrador   -   DigBit como programa normal, en una ventana", ref y);
            Opcion(pnlTipo, rbMaestro, "Maestro   -   DigBit como programa normal, en una ventana", ref y);
            Opcion(pnlTipo, rbAlumno, "Alumno (máquina del laboratorio)   -   modo kiosco: DigBit es la pantalla del equipo", ref y);
            if (hayServidorLocal)
            {
                Opcion(pnlTipo, rbServidor, "Servidor de base de datos local   -   solo si la base NO está en la nube", ref y);
            }

            // --- Base de datos ---------------------------------------------------------
            pnlBase = NuevaSeccion("Base de datos", desdeCarga ? 50 : 26);
            lblConexion.SetBounds(12, 28, 600, 20);
            lblConexion.ForeColor = Color.FromArgb(50, 50, 50);
            pnlBase.Controls.Add(lblConexion);
            lnkCambiarConexion.SetBounds(620, 28, 90, 20);
            lnkCambiarConexion.Text = "Cambiar";
            lnkCambiarConexion.TextAlign = ContentAlignment.TopRight;
            lnkCambiarConexion.LinkColor = Verde;
            lnkCambiarConexion.LinkClicked += (s, e) => { conexionAbierta = true; AjustarModo(); };
            pnlBase.Controls.Add(lnkCambiarConexion);
            lblConexion.Visible = desdeCarga;
            lnkCambiarConexion.Visible = desdeCarga;

            pnlConexion = NuevaSeccion(null, 96);
            y = 2;
            Campo(pnlConexion, "Servidor", txtServidor, ref y, "");
            Campo(pnlConexion, "Base", txtBase, ref y, "teschi_otru");
            Campo(pnlConexion, "Usuario", txtUsuarioBd, ref y, "digbit_equipo");

            pnlClaveBd = NuevaSeccion(null, 54);
            y = 2;
            Campo(pnlClaveBd, "Contraseña de la base", txtClaveBd, ref y, "");
            txtClaveBd.UseSystemPasswordChar = true;
            lblPistaClave = Pista(pnlClaveBd, PistaClave, y - 4);
            txtClaveBd.Leave += (s, e) => CargarLaboratoriosSiSePuede();
            txtClaveBd.Enter += (s, e) => AvisarBloqMayus();
            txtClaveBd.KeyUp += (s, e) => AvisarBloqMayus();

            if (desdeCarga)
            {
                Dictionary<string, string> conexion = Carga.LeerConexion(raiz);
                txtServidor.Text = Valor(conexion, "Servidor");
                txtBase.Text = Valor(conexion, "Base");
                txtUsuarioBd.Text = Valor(conexion, "Usuario");
                int puertoIncluido;
                if (int.TryParse(Valor(conexion, "Puerto"), out puertoIncluido) && puertoIncluido > 0)
                {
                    puerto = puertoIncluido;
                }

                origenServidor = "viene en el instalador";

                // Un .exe armado sin conexion (no deberia pasar: crear_instalador.ps1
                // se niega) deja las casillas abiertas en vez de un resumen vacio.
                conexionAbierta = txtServidor.Text.Length == 0 || txtBase.Text.Length == 0 || txtUsuarioBd.Text.Length == 0;
            }
            else if (raiz != null)
            {
                // El servidor NO se pregunta: sale del propio paquete (servidor.config).
                // Antes esta casilla arrancaba en 127.0.0.1 y habia que cambiarla a
                // mano en cada una de las 30 maquinas, que es donde se equivocaba todo
                // el mundo. Sigue siendo editable para poder corregirla.
                string origen;
                txtServidor.Text = Guiones.ServidorPorDefecto(raiz, out origen);
                origenServidor = origen;
            }

            // --- Solo al montar el servidor local --------------------------------------
            pnlClavesServidor = NuevaSeccion("Contraseñas que se van a CREAR en el servidor", 88);
            y = 28;
            Campo(pnlClavesServidor, "Para los equipos (digbit_equipo)", txtNuevaClaveEquipo, ref y, "");
            txtNuevaClaveEquipo.UseSystemPasswordChar = true;
            Campo(pnlClavesServidor, "Para el administrador (digbit_admin)", txtNuevaClaveAdmin, ref y, "");
            txtNuevaClaveAdmin.UseSystemPasswordChar = true;

            // --- Solo para la maquina del laboratorio -----------------------------------
            pnlEquipo = NuevaSeccion("Este equipo del laboratorio", 322);
            y = 28;
            Etiqueta(pnlEquipo, "Laboratorio", y);
            cboLaboratorio.SetBounds(XCampo, y, AnchoCampo - 112, 24);
            cboLaboratorio.DropDownStyle = ComboBoxStyle.DropDownList;
            pnlEquipo.Controls.Add(cboLaboratorio);
            btnCargarLabs.SetBounds(XCampo + AnchoCampo - 104, y - 1, 104, 26);
            btnCargarLabs.Text = "Cargar";
            btnCargarLabs.FlatStyle = FlatStyle.Flat;
            btnCargarLabs.Click += (s, e) => CargarLaboratorios();
            pnlEquipo.Controls.Add(btnCargarLabs);
            y += 28;
            Pista(pnlEquipo, "Sale de la base; si la lista está vacía, pulsa Cargar.", y);
            y += 22;

            Campo(pnlEquipo, "Número de máquina", txtNumeroMaquina, ref y, "");
            Pista(pnlEquipo, "El número pegado en el equipo (por ejemplo 12). Vacío: el nombre de Windows.", y - 4);
            y += 18;
            Campo(pnlEquipo, "Cuenta del laboratorio", txtCuenta, ref y, "laboratorio");
            Campo(pnlEquipo, "Contraseña de esa cuenta", txtClaveCuenta, ref y, "");
            txtClaveCuenta.UseSystemPasswordChar = true;
            Pista(pnlEquipo, "Cuenta de Windows (se crea si no existe). Entra sola; no puede ser administrador.", y - 4);
            y += 18;
            Campo(pnlEquipo, "Carpeta de datos (Deep Freeze)", txtCarpetaDatos, ref y, "");
            Pista(pnlEquipo, "Solo si el equipo tiene Deep Freeze: una unidad sin congelar, p. ej. T:\\DigBitDatos.", y - 4);
            y += 18;

            chkMemoria.SetBounds(XCampo, y, AnchoCampo, 22);
            chkMemoria.Text = "Autorizar la memoria USB conectada como llave de emergencia";
            chkMemoria.CheckedChanged += (s, e) => MostrarMemoria();
            pnlEquipo.Controls.Add(chkMemoria);
            y += 24;
            lblMemoria.SetBounds(XCampo + 20, y, AnchoCampo - 20, 20);
            lblMemoria.ForeColor = Color.DimGray;
            pnlEquipo.Controls.Add(lblMemoria);

            // --- Botones -----------------------------------------------------------------
            Panel pnlAcciones = NuevaSeccion(null, 52);
            btnInstalar.SetBounds(0, 6, 200, 40);
            btnInstalar.Text = "Instalar";
            btnInstalar.FlatStyle = FlatStyle.Flat;
            btnInstalar.BackColor = Verde;
            btnInstalar.ForeColor = Color.White;
            btnInstalar.FlatAppearance.BorderSize = 0;
            btnInstalar.Font = new Font("Century Gothic", 10F, FontStyle.Bold);
            btnInstalar.Click += (s, e) => Instalar();
            pnlAcciones.Controls.Add(btnInstalar);

            btnDiagnostico.SetBounds(212, 6, 180, 40);
            btnDiagnostico.Text = "Solo diagnosticar";
            btnDiagnostico.FlatStyle = FlatStyle.Flat;
            btnDiagnostico.Click += async (s, e) => await CorrerGuion(Guiones.Guion(raiz, "diagnostico_equipo.ps1"), "", "Diagnóstico", null);
            pnlAcciones.Controls.Add(btnDiagnostico);

            lblEstado.SetBounds(404, 18, 316, 20);
            pnlAcciones.Controls.Add(lblEstado);

            foreach (RadioButton opcion in new[] { rbAdm, rbMaestro, rbAlumno, rbServidor })
            {
                opcion.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) { AjustarModo(); } };
            }

            AjustarModo();
            MostrarMemoria();

            if (raiz == null)
            {
                txtSalida.Text = errorCarga != null
                    ? "No se pudo preparar la instalación: " + errorCarga
                    : "No encuentro deploy\\configurar_equipo.ps1." + Environment.NewLine +
                      "Este instalador tiene que estar dentro del paquete, junto a las carpetas deploy y db.";
            }
            else if (desdeCarga)
            {
                txtSalida.Text = "Todo lo necesario va dentro de este instalador." + Environment.NewLine +
                    "1. Elige el tipo de equipo.   2. Escribe la contraseña de la base.   3. Pulsa Instalar.";
            }
            else
            {
                txtSalida.Text = "Paquete: " + raiz + Environment.NewLine +
                    "Servidor: " + (txtServidor.Text.Length > 0 ? txtServidor.Text : "(vacío)") + "   (" + origenServidor + ")" + Environment.NewLine +
                    "Elige el tipo de equipo, rellena los datos y pulsa Instalar. Por debajo se ejecutan los guiones de la carpeta deploy.";
            }
        }

        // =====================================================================
        // Construccion
        // =====================================================================

        private Panel NuevaSeccion(string titulo, int alto)
        {
            Panel seccion = new Panel { Size = new Size(AnchoSeccion, alto), Margin = new Padding(0, 0, 0, 6) };
            if (titulo != null)
            {
                Label etiqueta = new Label
                {
                    Bounds = new Rectangle(0, 0, AnchoSeccion, 22),
                    Text = titulo,
                    Font = new Font("Century Gothic", 10F, FontStyle.Bold),
                    ForeColor = Verde
                };
                seccion.Controls.Add(etiqueta);
            }

            secciones.Controls.Add(seccion);
            return seccion;
        }

        private static void Opcion(Panel seccion, RadioButton opcion, string texto, ref int y)
        {
            opcion.SetBounds(12, y, AnchoSeccion - 12, 22);
            opcion.Text = texto;
            seccion.Controls.Add(opcion);
            y += 26;
        }

        private static void Etiqueta(Panel seccion, string texto, int y)
        {
            seccion.Controls.Add(new Label { Bounds = new Rectangle(12, y + 3, XCampo - 16, 20), Text = texto });
        }

        private static void Campo(Panel seccion, string etiqueta, TextBox caja, ref int y, string valor)
        {
            Etiqueta(seccion, etiqueta, y);
            caja.SetBounds(XCampo, y, AnchoCampo, 24);
            caja.Text = valor;
            seccion.Controls.Add(caja);
            y += 30;
        }

        private static Label Pista(Panel seccion, string texto, int y)
        {
            Label pista = new Label
            {
                Bounds = new Rectangle(XCampo, y, AnchoCampo, 18),
                Text = texto,
                Font = new Font("Century Gothic", 8F),
                ForeColor = Gris
            };
            seccion.Controls.Add(pista);
            return pista;
        }

        /// <summary>
        /// La contrasena de la base distingue mayusculas: con Bloq Mayus puesto se
        /// escribe mal sin verlo (los puntos no lo enseñan). Se avisa al teclear.
        /// </summary>
        private void AvisarBloqMayus()
        {
            bool activo = IsKeyLocked(Keys.CapsLock);
            lblPistaClave.Text = activo ? "Bloq Mayús está activado, y la contraseña distingue mayúsculas." : PistaClave;
            lblPistaClave.ForeColor = activo ? Color.FromArgb(190, 85, 0) : Gris;
        }

        private static string Valor(Dictionary<string, string> valores, string clave)
        {
            string valor;
            return valores.TryGetValue(clave, out valor) ? valor : "";
        }

        // =====================================================================
        // Estado de la ventana
        // =====================================================================

        private Tipo TipoElegido
        {
            get
            {
                if (rbAdm.Checked) { return Tipo.Adm; }
                if (rbMaestro.Checked) { return Tipo.Maestro; }
                if (rbAlumno.Checked) { return Tipo.Alumno; }
                if (rbServidor.Checked && hayServidorLocal) { return Tipo.Servidor; }
                return Tipo.Ninguno;
            }
        }

        /// <summary>Solo se ve lo que pide el tipo elegido.</summary>
        private void AjustarModo()
        {
            Tipo tipo = TipoElegido;
            bool conBase = tipo != Tipo.Servidor;

            secciones.SuspendLayout();
            pnlBase.Visible = conBase;
            pnlConexion.Visible = conBase && (!desdeCarga || conexionAbierta);
            pnlClaveBd.Visible = conBase;
            pnlClavesServidor.Visible = tipo == Tipo.Servidor;
            pnlEquipo.Visible = tipo == Tipo.Alumno;
            lblConexion.Visible = desdeCarga && !conexionAbierta;
            lnkCambiarConexion.Visible = desdeCarga && !conexionAbierta;
            secciones.ResumeLayout();

            btnInstalar.Text = tipo == Tipo.Servidor ? "Preparar la base" : "Instalar";

            // Con un servidor local, el equipo del administrador usa el usuario de
            // MySQL que puede administrar y los otros, el restringido. Con la base
            // de la escuela hay un solo usuario y esto no cambia nada.
            if (tipo == Tipo.Adm && txtUsuarioBd.Text == "digbit_equipo") { txtUsuarioBd.Text = "digbit_admin"; }
            if (tipo != Tipo.Adm && txtUsuarioBd.Text == "digbit_admin") { txtUsuarioBd.Text = "digbit_equipo"; }
            lblConexion.Text = "Viene en el instalador: base " + txtBase.Text.Trim() + " en " + txtServidor.Text.Trim() +
                               ", usuario " + txtUsuarioBd.Text.Trim() + ".";

            AjustarDesplazamiento();
            AjustarBotones();
            if (tipo == Tipo.Alumno)
            {
                CargarLaboratoriosSiSePuede();
            }
        }

        private void AjustarBotones()
        {
            btnInstalar.Enabled = !enMarcha && raiz != null && TipoElegido != Tipo.Ninguno;
            btnDiagnostico.Enabled = !enMarcha && raiz != null;
            btnCargarLabs.Enabled = !enMarcha && !cargandoLabs && raiz != null;
            foreach (RadioButton opcion in new[] { rbAdm, rbMaestro, rbAlumno, rbServidor })
            {
                opcion.Enabled = !enMarcha;
            }

            btnInstalar.BackColor = btnInstalar.Enabled ? Verde : Color.FromArgb(190, 200, 196);
        }

        /// <summary>
        /// Alto que piden las secciones visibles más unas líneas de salida. Si la
        /// ventana es más baja (pantalla pequeña), sale la barra de desplazamiento.
        /// </summary>
        private void AjustarDesplazamiento()
        {
            AutoScrollMinSize = new Size(0, disposicion.Padding.Vertical + secciones.PreferredSize.Height + 120);
        }

        private void MostrarMemoria()
        {
            if (!chkMemoria.Checked) { lblMemoria.Text = ""; return; }

            List<string> memorias = Guiones.MemoriasUsb();
            if (memorias.Count == 0) { lblMemoria.Text = "No veo ninguna memoria USB conectada."; }
            else if (memorias.Count == 1) { lblMemoria.Text = "Se autorizará: " + memorias[0]; }
            else { lblMemoria.Text = memorias.Count + " memorias conectadas: deja solo la que quieras autorizar."; }
        }

        /// <summary>
        /// Con el constructor de cadenas y no pegando trozos: una contraseña con
        /// ';' o comillas partía la cadena y el equipo se quedaba sin conexión.
        /// </summary>
        private string CadenaConexion()
        {
            DbConnectionStringBuilder cadena = new DbConnectionStringBuilder();
            cadena["Database"] = txtBase.Text.Trim();
            cadena["Server"] = txtServidor.Text.Trim();
            cadena["Port"] = puerto;
            cadena["User Id"] = txtUsuarioBd.Text.Trim();
            cadena["Password"] = txtClaveBd.Text;
            return cadena.ConnectionString;
        }

        /// <summary>Añade un aviso al final de la salida sin borrar lo que hubiera (un diagnóstico, por ejemplo).</summary>
        private void AgregarSalida(string texto)
        {
            if (txtSalida.TextLength > 0)
            {
                txtSalida.AppendText(Environment.NewLine + Environment.NewLine);
            }

            txtSalida.AppendText(texto);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Cerrar a mitad de configurar_equipo.ps1 dejaria el equipo a medias.
            if (enMarcha)
            {
                e.Cancel = true;
                MessageBox.Show("Espera a que termine: cerrar ahora dejaría el equipo a medio configurar.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Lo extraido del .exe no hace falta despues: lo instalado vive en C:\DigBit.
            if (desdeCarga)
            {
                Carga.Borrar(raiz);
            }

            // Si se cerró a media consulta de laboratorios, su .ps1 sigue ahí.
            foreach (string temporal in new List<string>(temporales))
            {
                BorrarGuion(temporal);
            }

            base.OnFormClosed(e);
        }

        // =====================================================================
        // Laboratorios
        // =====================================================================

        private void CargarLaboratoriosSiSePuede()
        {
            if (TipoElegido == Tipo.Alumno && cboLaboratorio.Items.Count == 0 && !cargandoLabs && !enMarcha
                && raiz != null && txtServidor.Text.Trim().Length > 0 && txtClaveBd.Text.Length > 0)
            {
                CargarLaboratorios();
            }
        }

        /// <summary>Lo que devuelve una consulta a la base desde un PowerShell aparte.</summary>
        private sealed class Consulta
        {
            public int Codigo;
            public readonly List<string> Lineas = new List<string>();
            public readonly StringBuilder Error = new StringBuilder();

            /// <summary>Todo lo que escribio, para enseñarlo si fallo.</summary>
            public string Detalle
            {
                get { return (string.Join(Environment.NewLine, Lineas) + Environment.NewLine + Error).Trim(); }
            }

            /// <summary>
            /// El servidor contesto y rechazo el usuario o la contrasena (error 1045
            /// de MySQL). No es la red: es lo que hay que decirle a quien instala.
            /// </summary>
            public bool AccesoDenegado
            {
                get { return Detalle.IndexOf("Access denied", StringComparison.OrdinalIgnoreCase) >= 0; }
            }
        }

        /// <summary>
        /// La lista de laboratorios sale de la base, no se teclea. Asi no hay
        /// forma de equivocarse con un acento o un espacio, que era el fallo
        /// que dejaba un equipo rechazando todos los codigos.
        /// </summary>
        private async void CargarLaboratorios()
        {
            if (cargandoLabs || raiz == null)
            {
                return;
            }

            cargandoLabs = true;
            AjustarBotones();
            lblEstado.ForeColor = Color.Black;
            lblEstado.Text = "Consultando los laboratorios...";

            Consulta consulta;
            try
            {
                consulta = await ConsultarLaboratorios();
            }
            finally
            {
                cargandoLabs = false;
            }

            if (IsDisposed)
            {
                return;
            }

            AjustarBotones();
            if (consulta.Codigo != 0)
            {
                lblEstado.ForeColor = Color.Firebrick;
                lblEstado.Text = consulta.AccesoDenegado ? "La base rechazó la contraseña." : "No se pudo consultar la base.";
                AgregarSalida((consulta.AccesoDenegado
                        ? "La base rechazó el usuario o la contraseña. La contraseña distingue mayúsculas y minúsculas: revísala (y Bloq Mayús)."
                        : "No se pudo leer la lista de laboratorios. ¿Tiene red este equipo?") +
                    Environment.NewLine + Environment.NewLine + consulta.Detalle);
                return;
            }

            string elegido = cboLaboratorio.Text;
            cboLaboratorio.Items.Clear();
            foreach (string linea in consulta.Lineas)
            {
                string nombre = linea.Trim();
                if (nombre.Length > 0) { cboLaboratorio.Items.Add(nombre); }
            }

            // Sin elegir por nadie: antes quedaba puesto el primero y, con prisa,
            // un equipo se instalaba en el laboratorio que no era, que es justo
            // lo que hace rechazar todos los codigos. Solo si no hay duda.
            int indice = cboLaboratorio.Items.IndexOf(elegido);
            cboLaboratorio.SelectedIndex = indice >= 0 ? indice : (cboLaboratorio.Items.Count == 1 ? 0 : -1);

            lblEstado.ForeColor = Verde;
            lblEstado.Text = cboLaboratorio.Items.Count + " laboratorio(s) en la base";
            AgregarSalida(cboLaboratorio.Items.Count == 0
                ? "La base responde, pero no hay ningún laboratorio dado de alta. Créalos primero desde el equipo del administrador."
                : "Laboratorios leídos de la base. Elige el de este equipo.");
        }

        /// <summary>
        /// Lee los nombres de los laboratorios con la conexion de la ventana. Sirve
        /// para la lista y, antes de instalar, para comprobar que la contrasena de
        /// la base es la buena: una mal escrita dejaba el equipo instalado y
        /// "sin conexion", y no se sabia por que.
        /// </summary>
        private async Task<Consulta> ConsultarLaboratorios()
        {
            // Add-Type -Path no vale aqui: ademas de cargar el DLL reflexiona
            // sobre todos sus tipos, y eso obliga a resolver BouncyCastle,
            // Protobuf, K4os y las demas dependencias de MySql.Data, que viven
            // junto al DLL y no junto a powershell.exe. Salia
            // "No se pueden cargar uno o varios tipos requeridos".
            // LoadFrom con un AssemblyResolve que mire en esa carpeta si vale;
            // es lo mismo que ya hace configurar_equipo.ps1.
            string carpeta = Path.Combine(raiz, "DigBit", "bin", "Release");
            StringBuilder ps = new StringBuilder();
            ps.AppendLine("$global:CarpetaDigBit = " + Comilla(carpeta));
            ps.AppendLine("$global:Resolviendo = @{}");
            ps.AppendLine("[AppDomain]::CurrentDomain.add_AssemblyResolve([ResolveEventHandler] {");
            ps.AppendLine("    param($remitente, $evento)");
            ps.AppendLine("    $corto = ($evento.Name -split ',')[0]");
            ps.AppendLine("    if ($global:Resolviendo.ContainsKey($corto)) { return $null }");
            ps.AppendLine("    $global:Resolviendo[$corto] = $true");
            ps.AppendLine("    try {");
            ps.AppendLine("        $dll = Join-Path $global:CarpetaDigBit \"$corto.dll\"");
            ps.AppendLine("        if (Test-Path $dll) { return [Reflection.Assembly]::LoadFrom($dll) }");
            ps.AppendLine("        return $null");
            ps.AppendLine("    } finally { $global:Resolviendo.Remove($corto) }");
            ps.AppendLine("})");
            ps.AppendLine("try {");
            ps.AppendLine("    [void][Reflection.Assembly]::LoadFrom((Join-Path $global:CarpetaDigBit 'MySql.Data.dll'))");
            ps.AppendLine("} catch [Reflection.ReflectionTypeLoadException] {");
            // Sin esto el mensaje es "uno o varios tipos" y nunca dice cual falta.
            ps.AppendLine("    $_.Exception.LoaderExceptions | ForEach-Object { Write-Host $_.Message }");
            ps.AppendLine("    throw");
            ps.AppendLine("}");
            ps.AppendLine("$c = New-Object MySql.Data.MySqlClient.MySqlConnection($env:" + VariableCadena + ")");
            ps.AppendLine("$c.Open()");
            ps.AppendLine("$m = $c.CreateCommand()");
            // Los desactivados (activo = 0) no se ofrecen. SELECT * y no la columna
            // por su nombre: una base sin la migracion 08 no la tiene.
            ps.AppendLine("$m.CommandText = 'SELECT * FROM laboratorios ORDER BY idlaboratorios'");
            ps.AppendLine("$r = $m.ExecuteReader()");
            ps.AppendLine("$activo = -1");
            ps.AppendLine("for ($i = 0; $i -lt $r.FieldCount; $i++) { if ($r.GetName($i) -eq 'activo') { $activo = $i } }");
            ps.AppendLine("while ($r.Read()) { if ($activo -lt 0 -or [int]$r.GetValue($activo) -ne 0) { [string]$r['nombre_laboratorio'] } }");
            ps.AppendLine("$r.Close()");
            ps.AppendLine("$c.Close()");

            Dictionary<string, string> entorno = new Dictionary<string, string>
            {
                { VariableCadena, CadenaConexion() + ";Connection Timeout=8" }
            };
            string temporal = EscribirGuion("digbit_labs_", ps);
            Consulta consulta = new Consulta();
            try
            {
                consulta.Codigo = await Task.Run(() => Ejecutar("powershell.exe", ArgumentosPowerShell(temporal), entorno,
                    linea => { lock (consulta.Lineas) { consulta.Lineas.Add(linea); } },
                    linea => { lock (consulta.Error) { consulta.Error.AppendLine(linea); } }));
            }
            finally
            {
                BorrarGuion(temporal);
            }

            return consulta;
        }

        // =====================================================================
        // Instalar
        // =====================================================================

        private async void Instalar()
        {
            string faltan = Validar();
            if (faltan != null)
            {
                MessageBox.Show(faltan, "Faltan datos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Tipo tipo = TipoElegido;
            if (tipo == Tipo.Servidor)
            {
                await PrepararServidor();
                return;
            }

            // Antes de tocar nada, que la base acepte la contrasena. Sin esto el
            // equipo quedaba instalado con una mal escrita (bastaba una mayuscula
            // de mas) y DigBit solo decia "sin conexion".
            enMarcha = true;
            AjustarBotones();
            lblEstado.ForeColor = Color.Black;
            lblEstado.Text = "Comprobando la conexión con la base...";
            Consulta prueba;
            try
            {
                prueba = await ConsultarLaboratorios();
            }
            finally
            {
                enMarcha = false;
                if (!IsDisposed) { AjustarBotones(); }
            }

            if (IsDisposed)
            {
                return;
            }

            if (prueba.Codigo != 0)
            {
                lblEstado.ForeColor = Color.Firebrick;
                string detalle = prueba.Detalle.Length > 500 ? prueba.Detalle.Substring(0, 500) + "..." : prueba.Detalle;
                if (prueba.AccesoDenegado)
                {
                    lblEstado.Text = "La base rechazó la contraseña.";
                    AgregarSalida(prueba.Detalle);
                    MessageBox.Show(
                        "La base rechazó el usuario '" + txtUsuarioBd.Text.Trim() + "' con esa contraseña, así que no se instala nada." + Environment.NewLine + Environment.NewLine +
                        "La contraseña distingue mayúsculas y minúsculas: revísala (y que no esté puesto Bloq Mayús).",
                        "Contraseña de la base", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtClaveBd.Focus();
                    txtClaveBd.SelectAll();
                    return;
                }

                lblEstado.Text = "No se pudo comprobar la base.";
                if (MessageBox.Show(
                        "No se pudo conectar con la base para comprobar la contraseña:" + Environment.NewLine + Environment.NewLine +
                        detalle + Environment.NewLine + Environment.NewLine +
                        "Si este equipo todavía no tiene red, puedes instalar igual: DigBit se conectará cuando la tenga." + Environment.NewLine +
                        "¿Instalar de todos modos?",
                        "Base de datos", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }

            if (!desdeCarga)
            {
                RecordarServidor();
            }

            // El laboratorio y el numero, a la vista: son lo unico que cambia de
            // un equipo a otro y lo que hace que los codigos valgan o no.
            string maquina = txtNumeroMaquina.Text.Trim().Length > 0 ? "la máquina " + txtNumeroMaquina.Text.Trim() : "el equipo " + Environment.MachineName;
            string aviso = tipo == Tipo.Alumno
                ? "Este será " + maquina + " de «" + cboLaboratorio.Text + "», en modo kiosco: la cuenta '" + txtCuenta.Text.Trim() +
                  "' entrará sola y DigBit será su pantalla. Tarda un minuto." + Environment.NewLine + Environment.NewLine +
                  "Si tiene Deep Freeze, tiene que estar DESCONGELADO o todo esto se perderá al reiniciar."
                : "Se va a instalar DigBit en " + CarpetaDestino + ", con un acceso directo en el escritorio." + Environment.NewLine +
                  "Se abre como un programa normal; no se bloquea nada.";
            if (MessageBox.Show(aviso, "Instalar", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            {
                return;
            }

            int codigo = await CorrerGuion(Guiones.Guion(raiz, "configurar_equipo.ps1"), ArgumentosInstalacion(), "Instalación", EntornoInstalacion());
            if (codigo == 0 && !IsDisposed)
            {
                Terminar(tipo);
            }
        }

        /// <summary>
        /// Los parametros de configurar_equipo.ps1 para lo que hay en la ventana.
        /// Las contraseñas no van aqui sino nombradas ($env:...): sus valores
        /// salen de EntornoInstalacion.
        /// </summary>
        internal string ArgumentosInstalacion()
        {
            StringBuilder args = new StringBuilder();
            if (TipoElegido == Tipo.Alumno)
            {
                args.Append(" -Modo maquina");
                args.Append(" -Cuenta ").Append(Guiones.Comillas(txtCuenta.Text.Trim()));
                args.Append(" -Contrasena $env:").Append(VariableClaveCuenta);
                args.Append(" -Laboratorio ").Append(Guiones.Comillas(cboLaboratorio.Text));
                if (txtNumeroMaquina.Text.Trim().Length > 0) { args.Append(" -NumeroMaquina ").Append(Guiones.Comillas(txtNumeroMaquina.Text.Trim())); }
                if (txtCarpetaDatos.Text.Trim().Length > 0) { args.Append(" -CarpetaDatos ").Append(Guiones.Comillas(txtCarpetaDatos.Text.Trim())); }
                if (chkMemoria.Checked) { args.Append(" -AutorizarMemoria"); }
            }
            else
            {
                args.Append(" -Modo ").Append(TipoElegido == Tipo.Adm ? "adm" : "maestro");
            }

            // El guion sabe deducir su carpeta de origen, pero el instalador ya conoce
            // la raiz del paquete: pasarla evita depender de $PSScriptRoot, que llega
            // vacio dentro de los valores por defecto del param cuando se lanza -File.
            args.Append(" -Origen ").Append(Guiones.Comillas(raiz));
            args.Append(" -CadenaConexion $env:").Append(VariableCadena);
            return args.ToString();
        }

        /// <summary>Los valores de las variables que nombra ArgumentosInstalacion.</summary>
        internal Dictionary<string, string> EntornoInstalacion()
        {
            Dictionary<string, string> entorno = new Dictionary<string, string> { { VariableCadena, CadenaConexion() } };
            if (TipoElegido == Tipo.Alumno)
            {
                entorno[VariableClaveCuenta] = txtClaveCuenta.Text;
            }

            return entorno;
        }

        /// <summary>Lo que se ofrece al acabar bien: abrir DigBit o reiniciar para el kiosco.</summary>
        private void Terminar(Tipo tipo)
        {
            if (tipo == Tipo.Alumno)
            {
                if (MessageBox.Show(
                        "Listo. Para que el equipo arranque en modo kiosco hay que reiniciarlo." + Environment.NewLine + Environment.NewLine +
                        "Si tiene Deep Freeze: comprueba que todo funciona y después vuelve a congelarlo." + Environment.NewLine + Environment.NewLine +
                        "¿Reiniciar ahora?",
                        "DigBit instalado", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = false, CreateNoWindow = true });
                }

                return;
            }

            if (MessageBox.Show(
                    "Listo: DigBit quedó instalado en " + CarpetaDestino + ", con el acceso directo 'DigBit' en el escritorio." + Environment.NewLine + Environment.NewLine +
                    "¿Abrirlo ahora?",
                    "DigBit instalado", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                AbrirDigBit();
            }
        }

        /// <summary>
        /// Abre DigBit como el usuario, SIN los permisos de administrador con los
        /// que corre este instalador: pedirselo a explorer.exe hace que lo lance el
        /// escritorio del usuario. Lanzado desde aqui correria elevado, y sus
        /// registros y datos irian a parar a otro sitio.
        /// </summary>
        private void AbrirDigBit()
        {
            string exe = Path.Combine(CarpetaDestino, "DigBit.exe");
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir " + exe + ": " + ex.Message + Environment.NewLine +
                    "Ábrelo con el acceso directo del escritorio.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string Validar()
        {
            Tipo tipo = TipoElegido;
            if (tipo == Tipo.Ninguno)
            {
                return "Elige qué equipo estás instalando: Administrador, Maestro o Alumno.";
            }

            if (tipo == Tipo.Servidor)
            {
                if (txtNuevaClaveEquipo.Text.Length == 0) { return "Pon una contraseña para digbit_equipo: es la que llevarán los equipos del laboratorio."; }
                if (txtNuevaClaveAdmin.Text.Length == 0) { return "Pon una contraseña para digbit_admin: es la de este equipo."; }
                if (txtNuevaClaveEquipo.Text == txtNuevaClaveAdmin.Text) { return "Pon contraseñas DISTINTAS. Si son la misma, quien lea la de un equipo del laboratorio tiene también la de administrador."; }
                if (!ClaveSencilla(txtNuevaClaveEquipo.Text) || !ClaveSencilla(txtNuevaClaveAdmin.Text))
                {
                    return "Las contraseñas nuevas solo pueden llevar letras sin acento, números y símbolos como - _ . @ # $ % & * + = ! ?" + Environment.NewLine + Environment.NewLine +
                           "Sin espacios, sin ñ ni acentos y sin comillas, barra invertida ni punto y coma: van tal cual dentro de las órdenes que crean los usuarios en MySQL.";
                }

                return null;
            }

            if (txtServidor.Text.Trim().Length == 0)
            {
                return desdeCarga
                    ? "Falta el servidor de la base. Pulsa 'Cambiar' junto a Base de datos y escríbelo."
                    : "Este paquete todavía no sabe dónde está la base.\r\n\r\n" +
                      "Lo normal es no tener que rellenar esto: prepara primero el servidor y COPIA ESE MISMO PAQUETE " +
                      "a los demás equipos; al terminar deja escrito " + Guiones.ArchivoServidor + " y la casilla sale sola.\r\n\r\n" +
                      "Si la base ya existe, escribe aquí el nombre del equipo servidor una vez: queda anotado y los " +
                      "siguientes equipos ya no lo pedirán.";
            }

            if (txtBase.Text.Trim().Length == 0) { return "Falta el nombre de la base."; }
            if (txtUsuarioBd.Text.Trim().Length == 0) { return "Falta el usuario de la base."; }
            if (desdeCarga && txtClaveBd.Text.Length == 0)
            {
                return "Escribe la contraseña de la base de datos (la del usuario " + txtUsuarioBd.Text.Trim() + ").";
            }

            if (tipo != Tipo.Alumno) { return null; }
            if (cboLaboratorio.Text.Trim().Length == 0) { return "Elige el laboratorio. Si la lista está vacía, pulsa Cargar para traerla de la base."; }
            if (txtCuenta.Text.Trim().Length == 0) { return "Falta el nombre de la cuenta del laboratorio."; }
            if (txtClaveCuenta.Text.Length == 0) { return "Falta la contraseña de la cuenta del laboratorio."; }
            return null;
        }

        /// <summary>
        /// ASCII imprimible sin espacios ni ' " \ ;. Las contraseñas del servidor
        /// local se pegan dentro del SQL de db\07_usuarios_minimos.sql, y una
        /// comilla ahí rompía la orden o, peor, metía otra.
        /// </summary>
        private static bool ClaveSencilla(string clave)
        {
            foreach (char c in clave)
            {
                if (c <= ' ' || c > '~' || c == '\'' || c == '"' || c == '\\' || c == ';')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Anota en el paquete el servidor que se acaba de usar. Asi, si alguien
        /// tuvo que escribirlo a mano en el primer equipo, no hay que volver a
        /// escribirlo en los veintinueve siguientes. Solo con paquete: el
        /// instalador de un solo archivo ya trae la conexion.
        ///
        /// 127.0.0.1 y localhost se saltan a proposito: anotar eso mandaria a
        /// cada maquina del laboratorio contra una base que no tiene, y el fallo
        /// no aparece hasta que un alumno se planta delante del equipo.
        /// </summary>
        private void RecordarServidor()
        {
            string valor = txtServidor.Text.Trim();
            if (valor.Length == 0 || valor == "127.0.0.1" || valor == "localhost") { return; }
            if (valor == Guiones.LeerServidor(raiz)) { return; }

            string fallo = Guiones.GuardarServidor(raiz, valor);
            if (fallo != null)
            {
                MessageBox.Show(
                    "No pude anotar el servidor en " + Guiones.ArchivoServidor + ":" + Environment.NewLine +
                    fallo + Environment.NewLine + Environment.NewLine +
                    "La instalación sigue igual. Lo único que pierdes es que el próximo equipo" + Environment.NewLine +
                    "volverá a preguntar la dirección en vez de traerla puesta.",
                    "Servidor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // =====================================================================
        // Servidor de base de datos local (solo desde un paquete con MySQL)
        // =====================================================================

        /// <summary>preparar_bd.ps1: en deploy\ en los paquetes nuevos, en la raiz en los antiguos.</summary>
        private string RutaPrepararBd()
        {
            string enDeploy = Guiones.Guion(raiz, "preparar_bd.ps1");
            return File.Exists(enDeploy) ? enDeploy : Path.Combine(raiz, "preparar_bd.ps1");
        }

        /// <summary>
        /// Monta la base en ESTE equipo y crea los dos usuarios. Es el primer
        /// paso de todo el despliegue y se hace una sola vez; hasta que esto no
        /// existe, no hay a donde conectar ningun otro equipo.
        ///
        /// Se genera un guion temporal en vez de encadenar comandos, y las
        /// contrasenas nuevas van en variables de entorno del proceso: ni en el
        /// archivo ni en la linea de comandos, que ve cualquiera que liste los
        /// procesos. Validar ya comprobo que no llevan nada que rompa el SQL.
        /// </summary>
        private async Task PrepararServidor()
        {
            string sqlUsuarios = Path.Combine(raiz, "db", "07_usuarios_minimos.sql");
            if (!File.Exists(sqlUsuarios))
            {
                MessageBox.Show("No encuentro " + sqlUsuarios, "Preparar la base", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // preparar_bd.ps1 se niega a escribir encima de una base que ya
            // existe, y hace bien: ahi estan las bitacoras. Pero sin esto el
            // instalador soltaba su "throw" en crudo, que no dice que hacer, y
            // no habia forma de pedir -Rehacer desde la ventana.
            bool hayBase = Directory.Exists(@"C:\DigBitDB\data");
            string rehacer = "";
            if (hayBase)
            {
                if (MessageBox.Show(
                        @"Ya hay una base de datos montada en C:\DigBitDB." + Environment.NewLine + Environment.NewLine +
                        "Si lo que quieres es instalar la aplicación en este equipo, cancela y marca" + Environment.NewLine +
                        @"""Administrador"" arriba: la base ya está lista y no hay que volver a montarla." + Environment.NewLine + Environment.NewLine +
                        "Continuar BORRA la base actual y la deja de cero. Se pierden las bitácoras," + Environment.NewLine +
                        "los laboratorios y los horarios que ya hubiera dentro.",
                        "Ya hay una base", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                {
                    return;
                }

                rehacer = " -Rehacer";
            }
            else if (MessageBox.Show(
                    @"Se va a instalar MySQL en C:\DigBitDB como servicio, con el esquema y los datos de prueba," + Environment.NewLine +
                    "y se crearán los usuarios digbit_equipo y digbit_admin." + Environment.NewLine + Environment.NewLine +
                    "Esto se hace UNA SOLA VEZ, en el equipo que hará de servidor.",
                    "Preparar la base", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            {
                return;
            }

            StringBuilder guion = new StringBuilder();
            guion.AppendLine("& " + Comilla(RutaPrepararBd()) + @" -Destino 'C:\DigBitDB'" + rehacer);
            guion.AppendLine("if ($LASTEXITCODE -ne 0) { throw 'preparar_bd.ps1 fallo.' }");
            guion.AppendLine("Write-Host ''");
            guion.AppendLine("Write-Host '== Usuarios de MySQL con permisos minimos' -ForegroundColor Cyan");
            guion.AppendLine("$sql = Get-Content -Raw " + Comilla(sqlUsuarios));
            guion.AppendLine("$sql = $sql.Replace('CAMBIAME-EQUIPO', $env:" + VariableClaveEquipo + ")");
            guion.AppendLine("$sql = $sql.Replace('CAMBIAME-ADMIN', $env:" + VariableClaveAdmin + ")");
            guion.AppendLine("$previo = $ErrorActionPreference; $ErrorActionPreference = 'Continue'");
            guion.AppendLine(@"$sql | & 'C:\DigBitDB\bin\mysql.exe' -u root");
            guion.AppendLine("$codigo = $LASTEXITCODE; $ErrorActionPreference = $previo");
            guion.AppendLine("if ($codigo -ne 0) { throw \"mysql devolvio $codigo al crear los usuarios.\" }");
            guion.AppendLine("Write-Host ''");
            guion.AppendLine("Write-Host 'Base lista. Ahora elige Administrador arriba y usa:' -ForegroundColor Green");
            guion.AppendLine("Write-Host '   Servidor 127.0.0.1   Base teschi_otru   Usuario digbit_admin'");
            guion.AppendLine("Write-Host '   y la contrasena de administrador que acabas de poner.'");

            Dictionary<string, string> entorno = new Dictionary<string, string>
            {
                { VariableClaveEquipo, txtNuevaClaveEquipo.Text },
                { VariableClaveAdmin, txtNuevaClaveAdmin.Text }
            };
            string temporal = EscribirGuion("digbit_preparar_", guion);
            int codigo;
            try
            {
                codigo = await Correr(temporal, "Preparar la base", entorno);
            }
            finally
            {
                BorrarGuion(temporal);
            }

            // Si la base no llego a montarse no hay nada a lo que apuntar, y
            // dejar escrito un servidor que no existe es peor que no dejar nada.
            if (codigo != 0 || IsDisposed) { return; }

            // Se dejan rellenos los datos del siguiente paso, para no tener que
            // acordarse de nada. Aqui se trabaja por 127.0.0.1 aunque la maquina
            // tenga nombre de red: preparar_bd.ps1 deja MySQL escuchando solo en
            // local hasta que se ejecute deploy\abrir_bd_en_red.ps1.
            txtServidor.Text = "127.0.0.1";
            txtBase.Text = "teschi_otru";
            txtClaveBd.Text = txtNuevaClaveAdmin.Text;

            // Lo que se anota en el paquete es el NOMBRE de esta maquina, que es
            // lo unico que les sirve a los demas equipos. Un nombre aguanta que
            // el DHCP le cambie la IP; la IP escrita a mano en 30 sitios, no.
            string fallo = Guiones.GuardarServidor(raiz, Environment.MachineName);
            txtSalida.AppendText(Environment.NewLine + Environment.NewLine + (fallo == null
                ? "Este paquete queda apuntando a '" + Environment.MachineName + "'." + Environment.NewLine +
                  "Cópialo TAL CUAL a los demás equipos: allí la casilla Servidor ya sale rellena y" + Environment.NewLine +
                  "nadie tiene que averiguar ninguna IP. Si el servidor se muda, cambia " + Guiones.ArchivoServidor + "." + Environment.NewLine +
                  "Antes de configurar ninguna máquina, abre la base a la red:" + Environment.NewLine +
                  "   deploy\\abrir_bd_en_red.ps1 -Contrasena '<la de digbit_equipo>'"
                : "Aviso: no pude escribir " + Guiones.ArchivoServidor + " (" + fallo + ")." + Environment.NewLine +
                  "En los demás equipos habrá que escribir '" + Environment.MachineName + "' a mano en Servidor."));
        }

        // =====================================================================
        // Ejecucion de los guiones
        // =====================================================================

        private static string Comilla(string valor)
        {
            return Guiones.Comillas(valor);
        }

        private static string ArgumentosPowerShell(string archivo)
        {
            return "-NoProfile -ExecutionPolicy Bypass -File \"" + archivo + "\"";
        }

        /// <summary>
        /// Escribe un .ps1 temporal. Con BOM, porque sin él Windows PowerShell lo
        /// lee como ANSI y un acento (el nombre de un laboratorio, una ruta) le
        /// llegaba roto al guion; y con la salida en UTF-8, que es como la lee
        /// Ejecutar: si no, sale en la página de la consola (850) y se veía "C?mputo".
        /// </summary>
        private string EscribirGuion(string prefijo, StringBuilder cuerpo)
        {
            string ruta = Path.Combine(Path.GetTempPath(), prefijo + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(ruta,
                "try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }" + Environment.NewLine +
                "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                cuerpo,
                new UTF8Encoding(true));
            temporales.Add(ruta);
            return ruta;
        }

        private void BorrarGuion(string ruta)
        {
            try
            {
                File.Delete(ruta);
                temporales.Remove(ruta);
            }
            catch (Exception)
            {
                // Se reintenta al cerrar la ventana (OnFormClosed).
            }
        }

        /// <summary>
        /// Lanza un guion con argumentos, metiendolos en un .ps1 temporal.
        /// </summary>
        /// <remarks>
        /// Los argumentos se entrecomillan al estilo de PowerShell ('asi'), pero
        /// "powershell -File" recibe la linea ya troceada por Windows, y Windows
        /// no sabe nada de la comilla simple. El resultado era que
        /// -Origen 'A:\DigBit-Paquete' llegaba con las comillas dentro del valor
        /// y GetFullPath reventaba, y que la cadena de conexion se partia en el
        /// espacio de "User Id" y su segunda mitad aterrizaba en el primer
        /// parametro posicional del guion, que es -Cuenta.
        ///
        /// Dentro de un .ps1 quien parsea es PowerShell, que es de quien son esas
        /// comillas. Las contraseñas no van en el archivo: van en 'entorno'.
        /// </remarks>
        private async Task<int> CorrerGuion(string guion, string argumentos, string que, IDictionary<string, string> entorno)
        {
            if (!File.Exists(guion))
            {
                MessageBox.Show("No encuentro " + guion, que, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return -1;
            }

            string temporal = EscribirGuion("digbit_lanzar_", new StringBuilder("& " + Comilla(guion) + argumentos + Environment.NewLine));
            try
            {
                return await Correr(temporal, que, entorno);
            }
            finally
            {
                BorrarGuion(temporal);
            }
        }

        /// <summary>
        /// Ejecuta un .ps1 de EscribirGuion y va mostrando lo que escribe mientras
        /// corre (antes se veia todo al final, con la ventana congelada un minuto).
        /// </summary>
        private async Task<int> Correr(string guion, string que, IDictionary<string, string> entorno)
        {
            enMarcha = true;
            AjustarBotones();
            lblEstado.ForeColor = Color.Black;
            lblEstado.Text = que + " en marcha...";
            txtSalida.Clear();
            UseWaitCursor = true;

            int codigo;
            try
            {
                codigo = await Task.Run(() => Ejecutar("powershell.exe", ArgumentosPowerShell(guion), entorno, Mostrar, Mostrar));
            }
            finally
            {
                enMarcha = false;
                UseWaitCursor = false;
                if (!IsDisposed)
                {
                    AjustarBotones();
                }
            }

            if (!IsDisposed)
            {
                lblEstado.ForeColor = codigo == 0 ? Verde : Color.Firebrick;
                lblEstado.Text = codigo == 0 ? que + " correcta." : que + " FALLÓ (código " + codigo + ").";
            }

            return codigo;
        }

        /// <summary>Añade una linea a la salida desde el hilo que lee el proceso.</summary>
        private void Mostrar(string linea)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(() => txtSalida.AppendText(linea + Environment.NewLine)));
            }
            catch (InvalidOperationException)
            {
                // La ventana se cerro mientras llegaba la linea.
            }
        }

        /// <summary>
        /// Lanza un proceso y entrega cada linea que escribe (salida y errores por
        /// separado). Se captura en vez de enseñar una consola porque el guion
        /// imprime cosas que hay que leer, y una ventana negra que se cierra sola
        /// no sirve de nada.
        /// </summary>
        private static int Ejecutar(string programa, string argumentos, IDictionary<string, string> entorno, Action<string> alSalir, Action<string> alError)
        {
            ProcessStartInfo inicio = new ProcessStartInfo(programa, argumentos)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Solo para este proceso y sus hijos: no pasan por ningun archivo ni
            // por la linea de comandos, que ve cualquiera que liste los procesos.
            if (entorno != null)
            {
                foreach (KeyValuePair<string, string> variable in entorno)
                {
                    inicio.EnvironmentVariables[variable.Key] = variable.Value;
                }
            }

            using (Process proceso = new Process { StartInfo = inicio })
            {
                proceso.OutputDataReceived += (s, e) => { if (e.Data != null) { alSalir(e.Data); } };
                proceso.ErrorDataReceived += (s, e) => { if (e.Data != null) { alError(e.Data); } };
                proceso.Start();
                proceso.BeginOutputReadLine();
                proceso.BeginErrorReadLine();
                // Sin tiempo limite: asi tambien espera a que se vacien las dos
                // salidas, y no se pierden las ultimas lineas.
                proceso.WaitForExit();
                return proceso.ExitCode;
            }
        }
    }
}
