using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DigLab.conexion;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;
using DrawingFont = System.Drawing.Font;

namespace DigLab
{
    /// <summary>
    /// "Codigos de las clases": como entran los alumnos a sus clases (db/11).
    /// Arriba, la forma de toda la escuela, con los tres nombres que eligio el
    /// usuario y en su orden: "Sin codigos", "Codigo unico" (el de cada
    /// profesor) y "Generacion de codigos" (uno por clase y dia, que se
    /// generan cada semana). Abajo, solo los
    /// laboratorios con otra forma, y "Agregar laboratorios..." los busca por
    /// cualquier parte del nombre: con muchos laboratorios iguales la lista queda
    /// corta (antes habia una fila por laboratorio). Uno suelto tambien se cambia
    /// desde Laboratorios (DialogoCodigosLaboratorio). La forma general se guarda
    /// (ModosDeAcceso.LeerGeneral) y es la de los laboratorios nuevos. Debajo de
    /// las tres formas, con db/13, la casilla "Los docentes no ven los codigos"
    /// (CodigosSoloResponsables), tambien para toda la escuela.
    ///
    /// Se abre desde el encabezado del panel del administrador, el enlace sobre
    /// "Los laboratorios ahora" y Ajustes. Solo lo puede guardar el usuario de
    /// MySQL del administrador (db/07), y antes vuelve a pedir su contrasena
    /// (DialogoContrasenaAdmin). El encargado de laboratorio (solo consulta) no
    /// lo abre: la forma de entrar ya la lee en el panel y en Laboratorios.
    /// </summary>
    internal class DialogoAccesoClases : Form
    {
        // Con nombres largos ("Edificio A - Laboratorio de Cómputo 101") y la barra
        // de la lista, cada fila cabe sin desplazarse a los lados.
        private const int Ancho = 780;
        private const int AltoFila = 34;
        private const int FilasVisibles = 4;

        private readonly LaboratoriosDatos datos = new LaboratoriosDatos();
        private readonly List<LaboratorioInfo> todos;
        private readonly List<LaboratorioInfo> laboratorios;
        private readonly ModoCodigo generalAntes;
        private readonly Dictionary<ModoCodigo, RadioButton> opciones = new Dictionary<ModoCodigo, RadioButton>();
        private readonly List<FilaOtraForma> filas = new List<FilaOtraForma>();
        private readonly ToolTip ayuda = new ToolTip();
        private readonly Label lblOtros;
        private readonly Panel pnlFilas;
        private readonly Label lblNinguno;
        private readonly Button btnAgregar;
        private readonly Label lblError;
        // "Los docentes no ven los codigos" (CodigosSoloResponsables); null sin db/13: no se ofrece.
        private readonly CheckBox chkDocentes;
        // Las explicaciones de las formas y la nota de abajo: lo que dicen de quien
        // da el codigo de la clase sigue a la casilla de los docentes.
        private readonly Dictionary<ModoCodigo, Label> explicaciones = new Dictionary<ModoCodigo, Label>();
        private readonly Label lblNota;

        /// <summary>El titulo de la ventana y de los sitios que la abren.</summary>
        public const string Titulo = "Códigos de las clases";

        /// <summary>La casilla de toda la escuela, debajo de las tres formas (las pruebas la buscan por su texto).</summary>
        public const string TextoDocentes = "Los docentes no ven los códigos: solo la administración y los responsables de laboratorio";

        /// <summary>Un laboratorio de la lista "con otra forma".</summary>
        private sealed class FilaOtraForma
        {
            public LaboratorioInfo Laboratorio;
            public Label Nombre;
            public ComboBox Modo;
            public LinkLabel Quitar;
            public Label Igual;
        }

        /// <summary>True si se cambio algo.</summary>
        public static bool Mostrar(IWin32Window dueno)
        {
            if (Permisos.Bloquear(dueno, "Cambiar los codigos de las clases")) return false;

            using (DialogoAccesoClases dialogo = new DialogoAccesoClases())
            {
                return dialogo.ShowDialog(dueno) == DialogResult.OK;
            }
        }

        private DialogoAccesoClases()
        {
            todos = datos.Listar();
            laboratorios = todos.Where(l => l.Activo).OrderBy(l => l.Nombre).ToList();
            generalAntes = ModosDeAcceso.General(datos.GeneralGuardada, laboratorios.Select(l => l.Modo));

            EstiloHorarios.PrepararDialogo(this, Titulo, new Size(Ancho, 640));
            Controls.Add(EstiloAdmin.TituloDialogo("¿Cómo entra cada estudiante a su clase?", Ancho - 48));

            Controls.Add(PiezasDeModos.Seccion("Para toda la escuela", new Point(24, 56)));
            Panel pnlGeneral = new Panel { Location = new Point(24, 84), Width = Ancho - 48, BackColor = Color.White };
            pnlGeneral.Height = PiezasDeModos.Opciones(pnlGeneral, opciones, datos.UsoLibre, datos.SesionesUnicas, null,
                datos.DocentesSinCodigos ?? false, explicaciones);
            opciones[generalAntes].Checked = true;
            foreach (RadioButton opcion in opciones.Values)
            {
                opcion.CheckedChanged += (s, e) => MarcarIguales();
            }

            Controls.Add(pnlGeneral);

            int y = pnlGeneral.Bottom + 8;
            if (datos.DocentesSinCodigos.HasValue)
            {
                // Para toda la escuela, debajo de las tres formas: quien ve los codigos.
                // Se guarda con lo demas (Guardar), en configuracion (db/13).
                chkDocentes = CasillaDocentes(new Point(24, pnlGeneral.Bottom - 2), Ancho - 48);
                chkDocentes.Checked = datos.DocentesSinCodigos.Value;
                chkDocentes.CheckedChanged += (s, e) =>
                {
                    lblError.Text = "";
                    QuienDaElCodigo(chkDocentes.Checked);
                };
                Controls.Add(chkDocentes);
                Label lblDocentes = PiezasDeModos.Nota("Cada docente le pide el código de su clase a la persona responsable del laboratorio. "
                    + "No aplica a «" + ModosDeAcceso.Nombre(ModoCodigo.SinCodigo) + "».", new Point(44, chkDocentes.Bottom + 1), Ancho - 68);
                // Un clic en la explicacion tambien la marca o desmarca.
                lblDocentes.Click += (s, e) => { if (chkDocentes.Enabled) chkDocentes.Checked = !chkDocentes.Checked; };
                Controls.Add(lblDocentes);
                y = lblDocentes.Bottom + 8;
            }

            lblOtros = PiezasDeModos.Seccion("Laboratorios con otra forma", new Point(24, y));
            Controls.Add(lblOtros);
            btnAgregar = EstiloHorarios.CrearBoton("Agregar laboratorios...", new Point(Ancho - 24 - 210, y - 6), new Size(210, 34));
            btnAgregar.Click += (s, e) => AgregarLaboratorios();
            ayuda.SetToolTip(btnAgregar, "Busca laboratorios por cualquier parte de su nombre y dales otra forma que la de toda la escuela.");
            Controls.Add(btnAgregar);

            pnlFilas = new Panel
            {
                Location = new Point(24, y + 32),
                Size = new Size(Ancho - 48, FilasVisibles * AltoFila + 6),
                AutoScroll = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            lblNinguno = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Italic),
                ForeColor = Color.FromArgb(110, 110, 110),
                Location = new Point(8, 8),
                Size = new Size(pnlFilas.Width - 20, 40),
                Text = "Ninguno: todos los laboratorios van con la forma de toda la escuela. "
                    + "Con «Agregar laboratorios...» le das otra a los que la necesiten."
            };
            pnlFilas.Controls.Add(lblNinguno);
            Controls.Add(pnlFilas);

            foreach (LaboratorioInfo laboratorio in laboratorios.Where(l => l.Modo != generalAntes))
            {
                AgregarFila(laboratorio, laboratorio.Modo);
            }

            y = pnlFilas.Bottom + 10;
            // Tan alta como la mas alta de sus dos versiones: la casilla la cambia sin mover nada.
            bool sinCodigos = datos.DocentesSinCodigos ?? false;
            lblNota = PiezasDeModos.Nota(NotaDeCambio(sinCodigos), new Point(24, y), Ancho - 48, NotaDeCambio(!sinCodigos));
            Controls.Add(lblNota);

            // Tres lineas: un error de conexion o de la base dice la causa y que hacer.
            lblError = EstiloAdmin.LineaError(new Point(24, lblNota.Bottom + 4), Ancho - 48, 54);
            Controls.Add(lblError);
            int alto = lblError.Bottom + 8 + 56;
            // Que quepa en la pantalla: a 1366x768 la barra de tareas deja unos 720 de
            // alto y, con la casilla de los docentes, la lista de abajo ensena una
            // fila menos (las demas, con su barra).
            // Con el aire que BotonesDialogo agrega abajo.
            int filas = FilasQueCaben(SizeFromClientSize(new Size(Ancho, alto + EstiloAdmin.AireBajoBotones)).Height, Screen.PrimaryScreen.WorkingArea.Height);
            if (filas < FilasVisibles)
            {
                int menos = (FilasVisibles - filas) * AltoFila;
                pnlFilas.Height -= menos;
                lblNota.Top -= menos;
                lblError.Top -= menos;
                alto -= menos;
            }

            ClientSize = new Size(Ancho, alto);
            Button guardar = EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            Acomodar();
            if (!datos.ModosDisponibles)
            {
                // Sin db/11 no hay donde guardarlo: se ve, pero no se puede cambiar
                // (antes se elegia todo, se pedia la contrasena y solo entonces fallaba).
                foreach (RadioButton opcion in opciones.Values)
                {
                    opcion.Enabled = false;
                }

                btnAgregar.Enabled = false;
                guardar.Enabled = false;
                if (chkDocentes != null)
                {
                    chkDocentes.Enabled = false;
                }

                lblError.Text = "Para cambiarlo falta aplicar db/11_modo_de_acceso.sql en la base: hasta entonces todos los laboratorios van con «Código único».";
            }
            Shown += (s, e) =>
            {
                // El foco en la opcion marcada: Espacio o las flechas la cambian.
                RadioButton marcada = opciones.Values.FirstOrDefault(r => r.Checked);
                if (marcada != null)
                {
                    marcada.Focus();
                }
            };
        }

        private ModoCodigo General
        {
            get
            {
                KeyValuePair<ModoCodigo, RadioButton> marcada = opciones.FirstOrDefault(o => o.Value.Checked);
                return marcada.Value != null ? marcada.Key : generalAntes;
            }
        }

        /// <summary>La nota de abajo; soloResponsables: con la casilla de los docentes, el codigo lo da la persona responsable.</summary>
        private static string NotaDeCambio(bool soloResponsables)
        {
            return "Vale en cuanto guardes. Las clases que ya empezaron conservan su código hasta que terminen; si iban sin código, "
                + "a quien llegue tarde le hará falta " + (soloResponsables ? "el que da la persona responsable del laboratorio" : "el que ve su docente en DigLab")
                + ". Antes de cambiarlo, actualiza DigLab (versión 1.1) en los equipos de ese laboratorio y en las computadoras de sus docentes.";
        }

        /// <summary>Lo que dicen la generacion de codigos y la nota de quien da el codigo, como la casilla de los docentes.</summary>
        private void QuienDaElCodigo(bool soloResponsables)
        {
            Label renovable;
            if (explicaciones.TryGetValue(ModoCodigo.Renovable, out renovable))
            {
                renovable.Text = PiezasDeModos.Explicacion(ModoCodigo.Renovable, datos.UsoLibre, soloResponsables);
            }

            lblNota.Text = NotaDeCambio(soloResponsables);
        }

        /// <summary>
        /// Las filas de "Laboratorios con otra forma" que se ensenan: FilasVisibles o,
        /// si con ellas el dialogo (de altoDelDialogo) no cabe en el alto util de la
        /// pantalla, las que hagan falta menos (nunca menos de dos).
        /// </summary>
        internal static int FilasQueCaben(int altoDelDialogo, int altoUtil)
        {
            int sobra = altoDelDialogo - altoUtil;
            return sobra <= 0 ? FilasVisibles : Math.Max(2, FilasVisibles - (sobra + AltoFila - 1) / AltoFila);
        }

        /// <summary>
        /// La casilla de los docentes, en una linea: en negritas como las formas de
        /// arriba, en letra un poco menor si asi no cabe en ese ancho.
        /// </summary>
        private static CheckBox CasillaDocentes(Point posicion, int ancho)
        {
            CheckBox casilla = new CheckBox
            {
                AutoSize = true,
                ForeColor = Color.DarkGreen,
                Location = posicion,
                Text = TextoDocentes
            };
            foreach (float tamano in new[] { 10F, 9.5F, 9F })
            {
                casilla.Font = new DrawingFont("Century Gothic", tamano, FontStyle.Bold);
                if (casilla.GetPreferredSize(Size.Empty).Width <= ancho)
                {
                    break;
                }
            }

            return casilla;
        }

        private void AgregarFila(LaboratorioInfo laboratorio, ModoCodigo modo)
        {
            FilaOtraForma fila = new FilaOtraForma { Laboratorio = laboratorio };
            fila.Nombre = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Size = new Size(300, 22),
                Text = laboratorio.Nombre
            };
            ayuda.SetToolTip(fila.Nombre, laboratorio.Nombre);
            fila.Modo = PiezasDeModos.ComboDeModos(Point.Empty, 220, datos.SesionesUnicas || laboratorio.Modo == ModoCodigo.Renovable);
            PiezasDeModos.Elegir(fila.Modo, modo);
            fila.Modo.SelectedIndexChanged += (s, e) =>
            {
                lblError.Text = "";
                MarcarIguales();
            };
            fila.Quitar = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Text = "Quitar"
            };
            fila.Quitar.LinkClicked += (s, e) => QuitarFila(fila);
            ayuda.SetToolTip(fila.Quitar, "Vuelve a la forma de toda la escuela.");
            // Siempre visible (vacia si no aplica): una etiqueta oculta no tiene ventana
            // y al desplazar la lista no se movia con su fila.
            fila.Igual = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F, FontStyle.Italic),
                ForeColor = Color.FromArgb(110, 110, 110),
                Size = new Size(105, 22),
                Text = ""
            };
            filas.Add(fila);
            pnlFilas.Controls.AddRange(new Control[] { fila.Nombre, fila.Modo, fila.Quitar, fila.Igual });
        }

        private void QuitarFila(FilaOtraForma fila)
        {
            filas.Remove(fila);
            foreach (Control control in new Control[] { fila.Nombre, fila.Modo, fila.Quitar, fila.Igual })
            {
                pnlFilas.Controls.Remove(control);
                control.Dispose();
            }

            lblError.Text = "";
            Acomodar();
        }

        /// <summary>Coloca las filas por nombre (con el desplazamiento de la lista) y pone el numero en el titulo.</summary>
        private void Acomodar()
        {
            pnlFilas.SuspendLayout();
            int desplazamiento = pnlFilas.AutoScrollPosition.Y;
            int i = 0;
            foreach (FilaOtraForma fila in filas.OrderBy(f => f.Laboratorio.Nombre))
            {
                int arriba = i * AltoFila + 4 + desplazamiento;
                // Hasta x = 705: dentro del ancho util con la barra vertical (~713).
                fila.Nombre.Location = new Point(8, arriba + 6);
                fila.Modo.Location = new Point(316, arriba + 2);
                fila.Quitar.Location = new Point(546, arriba + 7);
                fila.Igual.Location = new Point(600, arriba + 8);
                i++;
            }

            lblNinguno.Visible = filas.Count == 0;
            pnlFilas.ResumeLayout();
            lblOtros.Text = "Laboratorios con otra forma" + (filas.Count == 0 ? "" : " (" + filas.Count + ")");
            btnAgregar.Enabled = laboratorios.Count > filas.Count;
            MarcarIguales();
        }

        /// <summary>Las filas que quedaron con la forma de toda la escuela lo dicen: al guardar salen de la lista.</summary>
        private void MarcarIguales()
        {
            ModoCodigo general = General;
            foreach (FilaOtraForma fila in filas)
            {
                bool igual = PiezasDeModos.ModoDe(fila.Modo) == general;
                fila.Igual.Text = igual ? "igual que arriba" : "";
                ayuda.SetToolTip(fila.Igual, igual ? "Va igual que toda la escuela: al guardar deja de estar en esta lista." : null);
            }
        }

        private void AgregarLaboratorios()
        {
            lblError.Text = "";
            HashSet<int> yaEstan = new HashSet<int>(filas.Select(f => f.Laboratorio.Id));
            List<LaboratorioInfo> libres = laboratorios.Where(l => !yaEstan.Contains(l.Id)).ToList();
            List<LaboratorioInfo> elegidos;
            ModoCodigo modo;
            if (!DialogoElegirLaboratorios.Elegir(this, libres, General, datos.SesionesUnicas, out elegidos, out modo))
            {
                return;
            }

            foreach (LaboratorioInfo laboratorio in elegidos)
            {
                AgregarFila(laboratorio, modo);
            }

            Acomodar();
            // El primero que se agrego, a la vista.
            FilaOtraForma primera = filas.Where(f => elegidos.Contains(f.Laboratorio)).OrderBy(f => f.Laboratorio.Nombre).FirstOrDefault();
            if (primera != null)
            {
                pnlFilas.ScrollControlIntoView(primera.Modo);
                Acomodar();
            }
        }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Guardar los codigos de las clases")) return;

            lblError.Text = "";
            ModoCodigo general = General;
            Dictionary<int, ModoCodigo> otraForma = filas.ToDictionary(f => f.Laboratorio.Id, f => PiezasDeModos.ModoDe(f.Modo));
            bool generalCambia = general != generalAntes;

            // Lo que cambia: los de la lista, con la suya; los demas en uso, con la
            // de toda la escuela. Los desactivados no se ven aqui: solo siguen a la
            // forma general cuando esta cambia (como antes "para toda la escuela").
            Dictionary<int, ModoCodigo> nuevos = new Dictionary<int, ModoCodigo>();
            foreach (LaboratorioInfo laboratorio in todos)
            {
                ModoCodigo modo;
                if (!otraForma.TryGetValue(laboratorio.Id, out modo))
                {
                    if (!laboratorio.Activo && !generalCambia)
                    {
                        continue;
                    }

                    modo = general;
                }

                if (modo != laboratorio.Modo)
                {
                    nuevos[laboratorio.Id] = modo;
                }
            }

            // Quien ve los codigos (la casilla de los docentes), si se ofrece y cambio.
            bool docentesCambia = chkDocentes != null && chkDocentes.Checked != (datos.DocentesSinCodigos ?? false);
            bool formasCambian = nuevos.Count > 0 || generalCambia;
            if (!formasCambian && !docentesCambia)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }

            string detalle = formasCambian ? Detalle(general, generalCambia, nuevos) : "";
            if (docentesCambia)
            {
                detalle += (detalle.Length > 0 ? " " : "") + (chkDocentes.Checked
                    ? "Los docentes dejan de ver los códigos: solo los ven la administración y la persona responsable de cada laboratorio."
                    : "Los docentes vuelven a ver los códigos de sus clases en DigLab.");
            }

            // Con su contrasena otra vez: si dejo la sesion abierta y no esta, nadie mas lo cambia.
            if (!DialogoContrasenaAdmin.Confirmar(this, formasCambian ? "¿Cambiar cómo se entra a las clases?" : "¿Cambiar quién ve los códigos de las clases?",
                detalle + (formasCambian
                    ? " Vale en cuanto guardes; las clases que ya empezaron conservan su código hasta que terminen."
                    : " Vale en cuanto guardes; el panel de cada docente se pone al día en menos de un minuto."),
                "Cambiar"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                // Tambien la forma general, aunque no cambie: guardada, la lista de
                // abajo no se voltea cuando sus laboratorios llegan a ser mas.
                datos.CambiarModos(general, nuevos, docentesCambia ? chkDocentes.Checked : (bool?)null);
                DialogResult = DialogResult.OK;
            }
            catch (ErrorDeDatos ex)
            {
                lblError.Text = ex.Message;
            }
            catch (MySqlException ex) when (ex.Number == 1142 || ex.Number == 1143)
            {
                Log.Error("Cambiar el modo de acceso: sin permiso", ex);
                lblError.Text = "Este equipo no puede cambiarlo (su cuenta de MySQL no puede modificar laboratorios). Hazlo desde el equipo de la administración.";
            }
            catch (Exception ex)
            {
                Log.Error("Cambiar el modo de acceso de los laboratorios", ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo guardar", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>"Para toda la escuela: sin códigos. Cambian 3 laboratorios: A: ..., B: ... y C: ...".</summary>
        private string Detalle(ModoCodigo general, bool generalCambia, Dictionary<int, ModoCodigo> nuevos)
        {
            const int Maximo = 4;
            List<string> partes = new List<string>();
            if (generalCambia)
            {
                partes.Add("Para toda la escuela: " + ModosDeAcceso.Descripcion(general) + ".");
            }

            List<string> cambios = laboratorios.Where(l => nuevos.ContainsKey(l.Id))
                .Select(l => l.Nombre + ": " + ModosDeAcceso.Descripcion(nuevos[l.Id])).ToList();
            if (cambios.Count > 0)
            {
                string lista = cambios.Count <= Maximo ? EstiloAdmin.Enumerar(cambios)
                    : string.Join(", ", cambios.Take(Maximo)) + " y " + (cambios.Count - Maximo) + " más";
                partes.Add((cambios.Count == 1 ? "Cambia " : "Cambian " + cambios.Count + " laboratorios: ") + lista + ".");
            }

            int desactivados = todos.Count(l => !l.Activo && nuevos.ContainsKey(l.Id));
            if (desactivados > 0)
            {
                partes.Add("También " + (desactivados == 1 ? "un laboratorio deshabilitado." : desactivados + " laboratorios deshabilitados."));
            }

            return string.Join(" ", partes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Los codigos de un solo laboratorio, desde Laboratorios ("Codigos..."): con
    /// muchos laboratorios, uno se busca en esa lista y se cambia aqui.
    /// </summary>
    internal class DialogoCodigosLaboratorio : Form
    {
        private const int Ancho = 620;

        private readonly LaboratoriosDatos datos = new LaboratoriosDatos();
        private readonly LaboratorioInfo laboratorio;
        private readonly ModoCodigo general;
        private readonly Dictionary<ModoCodigo, RadioButton> opciones = new Dictionary<ModoCodigo, RadioButton>();
        private readonly Label lblError;

        /// <summary>True si se cambio.</summary>
        public static bool Mostrar(IWin32Window dueno, LaboratorioInfo laboratorio)
        {
            if (Permisos.Bloquear(dueno, "Cambiar los codigos de un laboratorio")) return false;

            using (DialogoCodigosLaboratorio dialogo = new DialogoCodigosLaboratorio(laboratorio))
            {
                return dialogo.ShowDialog(dueno) == DialogResult.OK;
            }
        }

        private DialogoCodigosLaboratorio(LaboratorioInfo elegido)
        {
            // Releido: el de la lista pudo cambiar desde que se abrio.
            List<LaboratorioInfo> todos = datos.Listar();
            laboratorio = todos.FirstOrDefault(l => l.Id == elegido.Id) ?? elegido;
            general = ModosDeAcceso.General(datos.GeneralGuardada, todos.Where(l => l.Activo).Select(l => l.Modo));

            EstiloHorarios.PrepararDialogo(this, "Códigos de " + laboratorio.Nombre, new Size(Ancho, 480));
            Controls.Add(EstiloAdmin.TituloDialogo("¿Cómo se entra a las clases de " + laboratorio.Nombre + "?", Ancho - 48));

            Panel pnlOpciones = new Panel { Location = new Point(24, 60), Width = Ancho - 48, BackColor = Color.White };
            pnlOpciones.Height = PiezasDeModos.Opciones(pnlOpciones, opciones, datos.UsoLibre,
                datos.SesionesUnicas || laboratorio.Modo == ModoCodigo.Renovable, general, datos.DocentesSinCodigos ?? false);
            opciones[laboratorio.Modo].Checked = true;
            Controls.Add(pnlOpciones);

            Label lblNota = PiezasDeModos.Nota("Vale en cuanto guardes; las clases que ya empezaron conservan su código hasta que terminen. "
                + "Para toda la escuela y para varios laboratorios a la vez, usa «" + DialogoAccesoClases.Titulo + "» en el panel.",
                new Point(24, pnlOpciones.Bottom + 6), Ancho - 48);
            Controls.Add(lblNota);
            // Tres lineas: un error de conexion o de la base dice la causa y que hacer.
            lblError = EstiloAdmin.LineaError(new Point(24, lblNota.Bottom + 4), Ancho - 48, 54);
            Controls.Add(lblError);
            ClientSize = new Size(Ancho, lblError.Bottom + 8 + 56);
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            Shown += (s, e) => opciones[laboratorio.Modo].Focus();
        }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Guardar los codigos de un laboratorio")) return;

            lblError.Text = "";
            ModoCodigo modo = opciones.First(o => o.Value.Checked).Key;
            if (modo == laboratorio.Modo)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }

            if (!DialogoContrasenaAdmin.Confirmar(this, "¿Cambiar cómo se entra a las clases de " + laboratorio.Nombre + "?",
                laboratorio.Nombre + ": " + ModosDeAcceso.Descripcion(modo) + ". Vale en cuanto guardes; las clases que ya empezaron "
                + "conservan su código hasta que terminen.", "Cambiar"))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                // Con la forma general de antes del cambio: si aun no estaba guardada
                // (es la de la mayoria), queda fija y cambiar laboratorios uno por uno
                // no la voltea.
                datos.CambiarModos(general, new Dictionary<int, ModoCodigo> { { laboratorio.Id, modo } });
                DialogResult = DialogResult.OK;
            }
            catch (ErrorDeDatos ex)
            {
                lblError.Text = ex.Message;
            }
            catch (MySqlException ex) when (ex.Number == 1142 || ex.Number == 1143)
            {
                Log.Error("Cambiar el modo de acceso: sin permiso", ex);
                lblError.Text = "Este equipo no puede cambiarlo (su cuenta de MySQL no puede modificar laboratorios). Hazlo desde el equipo de la administración.";
            }
            catch (Exception ex)
            {
                Log.Error("Cambiar el modo de acceso del laboratorio " + laboratorio.Nombre, ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo guardar", ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    /// <summary>
    /// "Agregar laboratorios...": los busca por cualquier parte del nombre (sin
    /// acentos ni mayusculas) y deja marcar varios para darles una misma forma.
    /// </summary>
    internal class DialogoElegirLaboratorios : Form
    {
        private const int Ancho = 520;

        private readonly List<LaboratorioInfo> libres;
        private readonly HashSet<int> marcados = new HashSet<int>();
        private readonly TextBox txtBuscar;
        private readonly CheckedListBox lstLaboratorios;
        private readonly ComboBox cboModo;
        private readonly Label lblError;
        private readonly ModoCodigo general;
        private bool llenando;

        public static bool Elegir(IWin32Window dueno, List<LaboratorioInfo> libres, ModoCodigo general, bool conGeneracion,
            out List<LaboratorioInfo> elegidos, out ModoCodigo modo)
        {
            using (DialogoElegirLaboratorios dialogo = new DialogoElegirLaboratorios(libres, general, conGeneracion))
            {
                bool listo = dialogo.ShowDialog(dueno) == DialogResult.OK;
                elegidos = listo ? dialogo.libres.Where(l => dialogo.marcados.Contains(l.Id)).ToList() : new List<LaboratorioInfo>();
                modo = PiezasDeModos.ModoDe(dialogo.cboModo);
                return listo && elegidos.Count > 0;
            }
        }

        private DialogoElegirLaboratorios(List<LaboratorioInfo> libres, ModoCodigo general, bool conGeneracion)
        {
            this.libres = libres.OrderBy(l => l.Nombre).ToList();
            this.general = general;
            EstiloHorarios.PrepararDialogo(this, "Agregar laboratorios", new Size(Ancho, 520));
            Controls.Add(EstiloAdmin.TituloDialogo("Laboratorios con otra forma", Ancho - 48));

            Controls.Add(EstiloHorarios.CrearEtiqueta("Buscar", new Point(24, 56)));
            txtBuscar = EstiloHorarios.CrearTexto(new Point(24, 80), Ancho - 48);
            EstiloHorarios.PonerTextoGuia(txtBuscar, "Cualquier parte del nombre");
            txtBuscar.TextChanged += (s, e) => Llenar();
            txtBuscar.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && lstLaboratorios.Items.Count > 0)
                {
                    lstLaboratorios.Focus();
                    lstLaboratorios.SelectedIndex = 0;
                    e.Handled = true;
                }
            };
            Controls.Add(txtBuscar);

            lstLaboratorios = new CheckedListBox
            {
                CheckOnClick = true,
                IntegralHeight = false,
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(24, 116),
                Size = new Size(Ancho - 48, 220)
            };
            lstLaboratorios.ItemCheck += (s, e) =>
            {
                if (llenando)
                {
                    return;
                }

                int id = ((OpcionCombo)lstLaboratorios.Items[e.Index]).Id;
                if (e.NewValue == CheckState.Checked)
                {
                    marcados.Add(id);
                }
                else
                {
                    marcados.Remove(id);
                }

                // ItemCheck llega antes de cambiar la casilla: el conteo, despues.
                BeginInvoke(new Action(Contar));
            };
            Controls.Add(lstLaboratorios);

            Controls.Add(EstiloHorarios.CrearEtiqueta("Forma para los marcados", new Point(24, 348)));
            cboModo = PiezasDeModos.ComboDeModos(new Point(24, 372), 300, conGeneracion);
            // Por defecto, la primera que no es la de toda la escuela.
            PiezasDeModos.Elegir(cboModo, PiezasDeModos.Orden.First(m => m != general && (conGeneracion || m != ModoCodigo.Renovable)));
            Controls.Add(cboModo);

            lblError = EstiloAdmin.LineaError(new Point(24, 408), Ancho - 48, 36);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Agregar", Aceptar);
            Llenar();
            ActiveControl = txtBuscar;
        }

        private void Llenar()
        {
            llenando = true;
            lstLaboratorios.BeginUpdate();
            try
            {
                lstLaboratorios.Items.Clear();
                foreach (LaboratorioInfo laboratorio in libres.Where(l => EstiloAdmin.Coincide(EstiloAdmin.Normalizar(l.Nombre), txtBuscar.Text)))
                {
                    int indice = lstLaboratorios.Items.Add(new OpcionCombo { Id = laboratorio.Id, Texto = laboratorio.Nombre });
                    lstLaboratorios.SetItemChecked(indice, marcados.Contains(laboratorio.Id));
                }
            }
            finally
            {
                lstLaboratorios.EndUpdate();
                llenando = false;
            }

            Contar();
        }

        private void Contar()
        {
            lblError.ForeColor = Color.FromArgb(80, 80, 80);
            lblError.Text = (lstLaboratorios.Items.Count == 0 ? "Ningún laboratorio con ese nombre. " : "")
                + (marcados.Count == 0 ? "Marca los que van con otra forma." : marcados.Count == 1 ? "1 marcado." : marcados.Count + " marcados.");
        }

        private void Aceptar()
        {
            if (marcados.Count == 0)
            {
                lblError.ForeColor = Color.FromArgb(190, 30, 30);
                lblError.Text = "Marca al menos un laboratorio.";
                txtBuscar.Focus();
                return;
            }

            if (PiezasDeModos.ModoDe(cboModo) == general)
            {
                lblError.ForeColor = Color.FromArgb(190, 30, 30);
                lblError.Text = "Esa es la forma de toda la escuela: elige otra para estos laboratorios.";
                cboModo.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Lo que comparten los dialogos de los codigos: las tres opciones y sus textos.</summary>
    internal static class PiezasDeModos
    {
        private static readonly Color Gris = Color.FromArgb(80, 80, 80);

        /// <summary>El orden en que los pidio el usuario.</summary>
        public static readonly ModoCodigo[] Orden = { ModoCodigo.SinCodigo, ModoCodigo.Fijo, ModoCodigo.Renovable };

        /// <summary>El nombre de cada forma: el mismo en todo DigLab (ModosDeAcceso.Nombre), sin sinonimos.</summary>
        public static string Titulo(ModoCodigo modo)
        {
            return ModosDeAcceso.Nombre(modo);
        }

        /// <summary>
        /// Lo que hace cada forma, sin darle otro nombre: cada una se llama siempre
        /// como en Titulo. soloResponsables: "Los docentes no ven los codigos"
        /// (CodigosSoloResponsables), el codigo de cada clase lo da la persona
        /// responsable del laboratorio.
        /// </summary>
        public static string Explicacion(ModoCodigo modo, bool usoLibre, bool soloResponsables = false)
        {
            switch (modo)
            {
                case ModoCodigo.SinCodigo:
                    return "Desactiva los códigos: cada estudiante solo llena su bitácora. Si hay una clase queda en ella (con dos a la vez, "
                        + "elige la suya); si no, como uso libre del laboratorio."
                        + (usoLibre ? "" : " (Para registrar sin clase falta aplicar db/14 en la base.)");
                case ModoCodigo.Renovable:
                    return "Un código distinto para cada clase y cada día, que DigLab genera al empezar la semana. Solo tú los ves todos "
                        + (soloResponsables ? "(en Horarios); el de cada clase lo da la persona responsable del laboratorio cuando inicia (desde "
                            : "(en Horarios); cada docente ve el de su clase al entrar a DigLab cuando inicia (desde ")
                        + HorariosDatos.MargenAntesDelInicioMinutos + " minutos antes).";
                default:
                    return "Cada docente tiene un código, el mismo en todas sus clases, que dicta a su grupo. "
                        + "Se asigna en Docentes.";
            }
        }

        /// <summary>Titulo de una seccion de los dialogos.</summary>
        public static Label Seccion(string texto, Point posicion)
        {
            Label seccion = EstiloHorarios.CrearEtiqueta(texto, posicion);
            seccion.Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
            return seccion;
        }

        /// <summary>
        /// Texto gris tan alto como lo que dice (no corta la ultima linea); con
        /// otroTexto, como el mas alto de los dos: puede pasar al otro sin mover nada.
        /// </summary>
        public static Label Nota(string texto, Point posicion, int ancho, string otroTexto = null)
        {
            DrawingFont fuente = new DrawingFont("Century Gothic", 9F);
            int alto = Math.Max(AltoDe(texto, fuente, ancho), otroTexto == null ? 0 : AltoDe(otroTexto, fuente, ancho)) + 4;
            return new Label { AutoSize = false, Font = fuente, ForeColor = Gris, Location = posicion, Size = new Size(ancho, alto), Text = texto };
        }

        private static int AltoDe(string texto, DrawingFont fuente, int ancho)
        {
            return TextRenderer.MeasureText(texto, fuente, new Size(ancho, int.MaxValue), TextFormatFlags.WordBreak).Height;
        }

        /// <summary>
        /// Las tres opciones, cada una con su explicacion debajo, dentro del panel;
        /// devuelve el alto que ocupan. Con general, esa opcion lo dice ("como toda
        /// la escuela"). Sin conGeneracion (falta un indice de db/04) la generacion
        /// de codigos no se puede elegir. soloResponsables, como en Explicacion; con
        /// explicaciones, deja ahi la de cada forma, tan alta que puede pasar a la
        /// otra version (la casilla de los docentes) sin mover nada.
        /// </summary>
        public static int Opciones(Panel panel, Dictionary<ModoCodigo, RadioButton> opciones, bool usoLibre, bool conGeneracion, ModoCodigo? general,
            bool soloResponsables = false, Dictionary<ModoCodigo, Label> explicaciones = null)
        {
            DrawingFont fuente = new DrawingFont("Century Gothic", 9.5F);
            int ancho = panel.Width - 20;
            int y = 0;
            foreach (ModoCodigo modo in Orden)
            {
                RadioButton opcion = new RadioButton
                {
                    AutoSize = true,
                    Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold),
                    ForeColor = Color.DarkGreen,
                    Location = new Point(0, y),
                    Text = Titulo(modo) + (general == modo ? "  (como toda la escuela)" : "")
                };
                if (modo == ModoCodigo.Renovable && !conGeneracion)
                {
                    // Sin una sesion por clase y dia no se puede garantizar un solo codigo por clase.
                    opcion.Enabled = false;
                    opcion.Text += " (falta un índice de db/04 en la base)";
                }

                opciones[modo] = opcion;
                panel.Controls.Add(opcion);
                string explicacion = Explicacion(modo, usoLibre, soloResponsables);
                // Tan alta como su texto: con una altura fija, una explicacion de
                // cuatro lineas perdia la ultima. Si la casilla la puede cambiar,
                // como la mas alta de las dos.
                int alto = Math.Max(AltoDe(explicacion, fuente, ancho),
                    explicaciones == null ? 0 : AltoDe(Explicacion(modo, usoLibre, !soloResponsables), fuente, ancho));
                Label detalle = new Label
                {
                    AutoSize = false,
                    Font = fuente,
                    ForeColor = Gris,
                    Location = new Point(20, y + 26),
                    Size = new Size(ancho, alto + 4),
                    Text = explicacion
                };
                // Un clic en la explicacion tambien elige la opcion.
                detalle.Click += (s, e) => { if (opcion.Enabled) opcion.Checked = true; };
                panel.Controls.Add(detalle);
                if (explicaciones != null)
                {
                    explicaciones[modo] = detalle;
                }

                y += 26 + alto + 12;
            }

            return y;
        }

        /// <summary>Un combo con los modos, con los nombres cortos de las opciones ("Sin códigos", "Código único"...).</summary>
        public static ComboBox ComboDeModos(Point posicion, int ancho, bool conGeneracion)
        {
            ComboBox combo = EstiloHorarios.CrearCombo(posicion, ancho);
            foreach (ModoCodigo modo in Orden)
            {
                if (modo != ModoCodigo.Renovable || conGeneracion)
                {
                    combo.Items.Add(new OpcionCombo { Id = (int)modo, Texto = ModosDeAcceso.Nombre(modo) });
                }
            }

            combo.SelectedIndex = 0;
            return combo;
        }

        public static void Elegir(ComboBox combo, ModoCodigo modo)
        {
            OpcionCombo opcion = combo.Items.Cast<OpcionCombo>().FirstOrDefault(o => o.Id == (int)modo);
            if (opcion != null)
            {
                combo.SelectedItem = opcion;
            }
        }

        public static ModoCodigo ModoDe(ComboBox combo)
        {
            OpcionCombo opcion = combo.SelectedItem as OpcionCombo;
            return opcion == null ? ModoCodigo.Fijo : (ModoCodigo)opcion.Id;
        }
    }

    /// <summary>
    /// El codigo de una clase en grande, para el administrador que la abre desde
    /// el panel (generacion de codigos) y se lo da al grupo; debajo de como se
    /// deletrea, "Escuchar el codigo" lo lee en voz alta (DictadoCodigo; se
    /// detiene al cerrar). Los codigos generados solo los ve el administrador
    /// (regla de db/11): al encargado de laboratorio no se le ensena aunque
    /// alguna pantalla lo intente.
    /// </summary>
    internal class DialogoCodigoDeClase : Form
    {
        private const int Ancho = 520;

        public static void Mostrar(IWin32Window dueno, SesionAgenda clase, string codigo, string laboratorio)
        {
            if (!Permisos.VeCodigosGenerados)
            {
                Log.Aviso("Solo consulta: no se ensena a " + (Datos_User.getUser() ?? "-") + " el codigo de la clase "
                    + (clase == null ? "-" : clase.Descripcion) + ".");
                return;
            }

            using (DialogoCodigoDeClase dialogo = new DialogoCodigoDeClase(clase, codigo, laboratorio))
            {
                dialogo.ShowDialog(dueno);
            }
        }

        private DialogoCodigoDeClase(SesionAgenda clase, string codigo, string laboratorio)
        {
            // En mayusculas, como en el panel del profesor: nadie las distingue.
            codigo = TarjetaCodigo.Mayusculas(codigo);
            // Con "Escuchar el codigo" (fuera del kiosco), su fila bajo el deletreo: 44 px mas alto.
            bool conEscuchar = DictadoCodigo.Permitido;
            int alto = conEscuchar ? 374 : 330;
            EstiloHorarios.PrepararDialogo(this, "Código de la clase", new Size(Ancho, alto));
            Controls.Add(EstiloAdmin.TituloDialogo("Código de esta clase", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Consolas", 48F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, 60),
                Size = new Size(Ancho - 48, 90),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = codigo
            });
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(24, 152),
                Size = new Size(Ancho - 48, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = TarjetaCodigo.Deletrear(codigo)
            });

            int y = 188;
            if (conEscuchar)
            {
                BotonEscuchar escuchar = new BotonEscuchar { Codigo = codigo };
                escuchar.Location = new Point((Ancho - escuchar.Width) / 2, 180);
                Controls.Add(escuchar);
                y = 232;
            }

            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(24, y),
                Size = new Size(Ancho - 48, 60),
                Text = clase.Materia + ", " + clase.Grupo + ", en " + laboratorio + " con " + clase.Profesor + ". Vale hasta las "
                    + HorariosDatos.Hora(clase.Fin) + " y solo para esta clase: díctaselo al grupo."
            });
            Button cerrar = EstiloAdmin.BotonPrincipal("Cerrar", new Point(Ancho - 24 - 130, alto - 56), new Size(130, 40));
            cerrar.DialogResult = DialogResult.OK;
            Controls.Add(cerrar);
            AcceptButton = cerrar;
            CancelButton = cerrar;
            // Enter sigue cerrando, como antes: el foco empieza en Cerrar, no en Escuchar.
            ActiveControl = cerrar;
        }
    }
}
