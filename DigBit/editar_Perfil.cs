using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// "Mi perfil" de quien tiene la sesion, con el estilo de los dialogos del
    /// administrador (DialogoUsuario): sus datos y, si quiere, otra contrasena.
    /// Alumnos y profesores lo abren desde su pantalla, sin modal; el
    /// administrador y el encargado de laboratorio, desde la pastilla "Mi
    /// perfil" de su panel. Cambiar el correo o la contrasena pide la actual, y
    /// con tres equivocadas en la sesion (contando las de DialogoContrasenaAdmin)
    /// la sesion se cierra sola. El encargado, en solo consulta, cambia su correo
    /// y su contrasena, no su nombre (MiPerfilDatos). Ninguna contrasena va al log.
    /// El docente que entro con el SIIA (Ajustes) no cambia aqui su contrasena:
    /// es la del SIIA y se cambia alli.
    /// </summary>
    public partial class editar_Perfil : Form
    {
        private const int Ancho = 640;
        // Cada campo: su etiqueta, 6 px, la caja y aire hasta el siguiente.
        private const int Fila = 66;
        // Tres columnas de 186 con 16 entre ellas, de 24 a 616.
        private const int Columna2 = 227;
        private const int Columna3 = 430;
        private const int AnchoCampo = 186;
        private const int Intentos = 3;
        private static readonly Color TextoSecundario = Color.FromArgb(90, 90, 90);

        private readonly MiPerfilDatos datos = new MiPerfilDatos();
        // Docente que entro con el SIIA encendido (Ajustes): su contrasena es la del
        // SIIA y se cambia alli; aqui solo se escribe para cambiar el correo.
        private readonly bool contrasenaDelSiia = Datos_User.ContrasenaDelSiia
            && (Datos_User.TipoUsuario == TiposUsuario.Profesor || Datos_User.TipoUsuario == TiposUsuario.Alumno);
        // Con el SIIA: el NIP, que va con la contrasena en su huella (InicioConSiia.Combinar).
        private TextBox txtNip;
        private readonly CatalogosDatos catalogos = new CatalogosDatos();
        private readonly ToolTip ayuda;
        // Lo que se leyo al abrir: sin el (sin conexion) no se guarda nada.
        private UsuarioInfo leido;

        private TextBox txtNombre;
        private TextBox txtPaterno;
        private TextBox txtMaterno;
        private TextBox txtCorreo;
        private ComboBox cboCarrera;
        private ComboBox cboSemestre;
        private TextBox txtActual;
        private TextBox txtNueva;
        private TextBox txtRepetida;
        private Label lblError;
        private Button btnGuardar;

        /// <param name="modoPerfil">
        /// "alumno" o "profesor", como lo abren sus pantallas. Ya no decide nada:
        /// el perfil es siempre el de la sesion.
        /// </param>
        public editar_Perfil(string modoPerfil = null)
        {
            InitializeComponent();
            ayuda = new ToolTip(components);
            Construir();
            Cargar();

            // Los fallos de antes en la sesion (en otro Mi perfil o en DialogoContrasenaAdmin)
            // cuentan: los intentos no empiezan de nuevo, y se avisa antes del primero.
            int previos = Datos_User.ConfirmacionesFallidas;
            if (leido != null && previos > 0 && previos < Intentos)
            {
                lblError.Text = (previos == 1 ? "Ya escribiste mal tu contraseña una vez en esta sesión. "
                    : "Ya escribiste mal tu contraseña " + previos + " veces en esta sesión. ") + Quedan(previos);
            }
        }

        private bool EsAlumno
        {
            get { return datos.Tipo == TiposUsuario.Alumno; }
        }

        private bool EsAdministrador
        {
            get { return datos.Tipo == TiposUsuario.Administrador; }
        }

        private void Construir()
        {
            // Los datos (nombre, correo y, el alumno, su carrera) y debajo la contrasena.
            int finDatos = 84 + Fila * (EsAlumno ? 3 : 2);
            int finContrasena = finDatos + 90 + Fila;
            // Debajo, la linea de error (tres renglones) y los botones.
            EstiloHorarios.PrepararDialogo(this, "Mi perfil", new Size(Ancho, finContrasena + 62 + 64));
            // Sin modal (alumno y profesor) CenterParent no se aplica.
            StartPosition = FormStartPosition.CenterScreen;
            // Sin TopMost, tambien en el kiosco: se abre con dueno (el panel) y ya queda
            // encima de el. TopMost y sin modal taparia lo que abra el panel despues
            // (un MessageBox), que lo deshabilita: la pantalla pareceria colgada.

            Controls.Add(EstiloAdmin.TituloDialogo("Mi perfil", 400));
            // El fondo de antes llevaba los logotipos; queda el del TESCHI (RNF01),
            // arriba a la derecha y sin bajar a la linea de debajo del titulo.
            Controls.Add(new PictureBox
            {
                Image = Properties.Resources.logoteschi,
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(Ancho - 24 - 91, 10),
                Size = new Size(91, 40),
                AccessibleName = "Tecnológico de Estudios Superiores de Chimalhuacán"
            });
            Controls.Add(new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = TextoSecundario,
                Location = new Point(24, 52),
                Size = new Size(Ancho - 48, 22),
                Text = QuienEres()
            });

            int y = 84;
            bool cambiaNombre = datos.CambiaSuNombre;
            txtNombre = Campo(cambiaNombre ? "Nombre(s) *" : "Nombre(s)", 24, y, AnchoCampo);
            txtPaterno = Campo(cambiaNombre ? "Apellido paterno *" : "Apellido paterno", Columna2, y, AnchoCampo);
            txtMaterno = Campo("Apellido materno", Columna3, y, AnchoCampo);
            foreach (TextBox nombre in new[] { txtNombre, txtPaterno, txtMaterno })
            {
                nombre.MaxLength = 80;
                if (!cambiaNombre)
                {
                    // El encargado de laboratorio: su nombre lo da de alta el administrador.
                    nombre.ReadOnly = true;
                    nombre.TabStop = false;
                    ayuda.SetToolTip(nombre, "Tu nombre lo cambia la administración de DigBit.");
                }
            }

            y += Fila;
            // El administrador no recupera su contrasena por correo (api/recuperar.php).
            // Con el SIIA, la contrasena se recupera en el SIIA, no con este correo.
            txtCorreo = Campo(EsAdministrador || contrasenaDelSiia ? "Correo" : "Correo (para recuperar tu contraseña)", 24, y, Columna2 + AnchoCampo - 24);
            // En la base de la escuela correo es VARCHAR(45).
            txtCorreo.MaxLength = ReglasCuenta.LargoMaximoCorreo;
            y += Fila;

            if (EsAlumno)
            {
                Controls.Add(EstiloHorarios.CrearEtiqueta("Carrera *", new Point(24, y)));
                cboCarrera = EstiloHorarios.CrearCombo(new Point(24, y + 26), Columna2 + AnchoCampo - 24);
                cboCarrera.SelectedIndexChanged += (s, e) => Limpiar(cboCarrera);
                Controls.Add(cboCarrera);
                Controls.Add(EstiloHorarios.CrearEtiqueta("Semestre", new Point(Columna3, y)));
                cboSemestre = EstiloHorarios.CrearCombo(new Point(Columna3, y + 26), 120);
                Controls.Add(cboSemestre);
                y += Fila;
            }

            // La contrasena, separada de los datos con una linea como la de la cabecera del panel.
            Controls.Add(EstiloAdmin.Separador(new Point(24, y + 6), Ancho - 48));
            Label tituloContrasena = new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(24, y + 20),
                Text = "Contraseña"
            };
            Controls.Add(tituloContrasena);
            // La explicacion y, 12 px mas abajo, las cajas (antes iban pegadas).
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = TextoSecundario,
                Location = new Point(24, y + 44),
                Size = new Size(Ancho - 48, 34),
                Text = contrasenaDelSiia
                    ? "Tu contraseña es la del SIIA del TESCHI: se cambia en el SIIA, no aquí. Para cambiar tu correo, escribe la contraseña y el NIP con los que entraste."
                    : "Para cambiar tu contraseña o tu correo, escribe la actual. Si no quieres otra contraseña, deja vacías las otras dos."
            });
            y += 90;

            txtActual = Campo(contrasenaDelSiia ? "Tu contraseña (la del SIIA)" : "Contraseña actual", 24, y, AnchoCampo);
            txtActual.MaxLength = 200;
            if (contrasenaDelSiia)
            {
                // No se ensenan: vacias, Guardar nunca cambia la contrasena.
                txtNueva = new TextBox();
                txtRepetida = new TextBox();
                Controls.Add(EstiloHorarios.CrearEtiqueta("Tu NIP del SIIA", new Point(Columna2, y)));
                txtNip = CajaNip.Crear(new Point(Columna2, y + 26));
                txtNip.TextChanged += (s, e) => Limpiar(txtNip);
                Controls.Add(txtNip);
            }
            else
            {
                txtNueva = Campo("Contraseña nueva", Columna2, y, AnchoCampo);
                txtRepetida = Campo("Repite la nueva", Columna3, y, AnchoCampo);
                ayuda.SetToolTip(txtNueva, "Déjala en blanco para no cambiarla. Al menos " + ReglasCuenta.LargoMinimoContrasena + " caracteres.");
            }

            txtNueva.MaxLength = ReglasCuenta.LargoMaximoContrasena;
            txtRepetida.MaxLength = ReglasCuenta.LargoMaximoContrasena;
            foreach (TextBox contrasena in new[] { txtActual, txtNueva, txtRepetida })
            {
                contrasena.UseSystemPasswordChar = true;
            }

            // A la derecha del titulo de la seccion; despues de las cajas en el orden de Tab.
            CheckBox mostrar = new CheckBox
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                Text = "Mostrar"
            };
            mostrar.Location = new Point(Ancho - 24 - mostrar.PreferredSize.Width, tituloContrasena.Top + 1);
            mostrar.CheckedChanged += (s, e) =>
            {
                txtActual.UseSystemPasswordChar = !mostrar.Checked;
                txtNueva.UseSystemPasswordChar = !mostrar.Checked;
                txtRepetida.UseSystemPasswordChar = !mostrar.Checked;
            };
            Controls.Add(mostrar);
            y += Fila;

            // Tres renglones: Guardar junta aqui todo lo que falta y lo que esta mal.
            lblError = EstiloAdmin.LineaError(new Point(24, y), Ancho - 48, 58);
            Controls.Add(lblError);

            if (cambiaNombre)
            {
                Controls.Add(new Label
                {
                    AutoSize = true,
                    Font = new DrawingFont("Century Gothic", 8.5F),
                    ForeColor = Color.FromArgb(110, 110, 110),
                    Location = new Point(24, ClientSize.Height - 40),
                    Text = "* obligatorio"
                });
            }

            btnGuardar = EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            // Sin modal, DialogResult no cierra la ventana: Cancelar y Esc la cierran aqui.
            ((Button)CancelButton).Click += (s, e) => Close();
            ActiveControl = cambiaNombre ? txtNombre : txtCorreo;
        }

        /// <summary>Con que entra y quien lo cambia: el identificador no se edita aqui.</summary>
        private string QuienEres()
        {
            string yo = datos.Identificador ?? "-";
            switch (datos.Tipo)
            {
                case TiposUsuario.Alumno:
                    return "Matrícula: " + yo + ". Solo la administración de DigBit puede cambiarla.";
                case TiposUsuario.Profesor:
                    return "Número de empleado: " + yo + ". Solo la administración de DigBit puede cambiarlo.";
                case TiposUsuario.Encargado:
                    return "Número de empleado: " + yo + ". Tu nombre lo cambia la administración de DigBit.";
                case TiposUsuario.Administrador:
                    return "Usuario: " + yo + " (administración).";
                default:
                    return "";
            }
        }

        /// <summary>
        /// El final de un "no se pudo" (MensajeDeFallo): a quien acudir. La
        /// administracion revisa el registro de su equipo; la persona responsable
        /// de laboratorio avisa a la administracion; estudiantes y docentes, a la
        /// persona responsable del laboratorio (en su propio equipo, el docente a
        /// la administracion: AvisoDeFalloDocente).
        /// </summary>
        private string SiSigueFallando()
        {
            switch (datos.Tipo)
            {
                case TiposUsuario.Administrador:
                    return MensajeDeFallo.RevisaElRegistro;
                case TiposUsuario.Encargado:
                    return MensajeDeFallo.AvisaALaAdministracion;
                case TiposUsuario.Profesor:
                    return Kiosco.Activo ? MensajeDeFallo.AvisaAlLaboratorio : MensajeDeFallo.AvisaALaAdministracion;
                default:
                    return MensajeDeFallo.AvisaAlLaboratorio;
            }
        }

        private TextBox Campo(string etiqueta, int x, int y, int ancho)
        {
            Controls.Add(EstiloHorarios.CrearEtiqueta(etiqueta, new Point(x, y)));
            TextBox texto = EstiloHorarios.CrearTexto(new Point(x, y + 26), ancho);
            texto.TextChanged += (s, e) => Limpiar(texto);
            Controls.Add(texto);
            return texto;
        }

        private void Cargar()
        {
            try
            {
                leido = datos.Leer();
                AlumnoInfo alumno = leido as AlumnoInfo;
                if (alumno != null)
                {
                    EstiloHorarios.Rellenar(cboCarrera, catalogos.Carreras(), alumno.CarreraId);
                    List<OpcionCombo> semestres = catalogos.Semestres();
                    semestres.Insert(0, new OpcionCombo { Id = 0, Texto = "—" });
                    EstiloHorarios.Rellenar(cboSemestre, semestres, alumno.SemestreId ?? 0);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Leer Mi perfil de " + (datos.Identificador ?? "-"), ex);
                leido = null;
                // La causa (la red, un permiso, una migracion...) y que hacer; el detalle, al registro.
                Desactivar(MensajeDeFallo.Explicar("No se pudieron leer tus datos", ex, SiSigueFallando()));
                return;
            }

            if (leido == null)
            {
                Desactivar(string.IsNullOrEmpty(datos.Identificador)
                    ? "No hay ninguna sesión abierta: vuelve a entrar."
                    : "No se encontró tu cuenta: cierra la sesión y vuelve a entrar.");
                return;
            }

            txtNombre.Text = leido.Nombre;
            txtPaterno.Text = leido.ApellidoPaterno;
            txtMaterno.Text = leido.ApellidoMaterno;
            txtCorreo.Text = leido.Correo;
        }

        /// <summary>Sin sus datos no se puede guardar nada: solo queda Cancelar.</summary>
        private void Desactivar(string motivo)
        {
            lblError.Text = motivo;
            foreach (Control control in Controls)
            {
                if (control is TextBox || control is ComboBox || control is CheckBox)
                {
                    control.Enabled = false;
                }
            }

            btnGuardar.Enabled = false;
            ActiveControl = CancelButton as Control;
        }

        private void Guardar()
        {
            // Tras el tercer fallo la sesion se esta cerrando: ya no se comprueba nada.
            if (leido == null || Datos_User.ConfirmacionesFallidas >= Intentos
                || (AppContexto.Actual != null && AppContexto.Actual.CerrandoPorSeguridad))
            {
                return;
            }

            lblError.Text = "";
            // Lo que falta se junta en una frase ("Falta: nombre y carrera."); lo
            // que esta mal escrito se explica aparte (como DialogoUsuario).
            List<string> faltan = new List<string>();
            List<string> errores = new List<string>();
            Control primero = null;

            if (datos.CambiaSuNombre)
            {
                if (txtNombre.Text.Trim().Length == 0)
                {
                    Marcar(txtNombre, "nombre", faltan, ref primero);
                }

                if (txtPaterno.Text.Trim().Length == 0)
                {
                    Marcar(txtPaterno, "apellido paterno", faltan, ref primero);
                }
            }

            string correo = txtCorreo.Text.Trim();
            string problemaCorreo = ReglasCuenta.ProblemaDeCorreo(correo);
            if (problemaCorreo != null)
            {
                // Es a donde le llega el codigo de "Olvidaste tu contrasena?": uno mal
                // escrito no se descubre hasta el dia que hace falta.
                Marcar(txtCorreo, problemaCorreo + (EsAdministrador ? "" : " Ahí te llegaría el código si olvidas tu contraseña."), errores, ref primero);
            }

            OpcionCombo carrera = null;
            if (EsAlumno)
            {
                carrera = cboCarrera.SelectedItem as OpcionCombo;
                if (carrera == null)
                {
                    Marcar(cboCarrera, "carrera", faltan, ref primero);
                }
            }

            // Como lo decide MiPerfilDatos: otro correo, sin contar mayusculas.
            bool cambiaCorreo = !string.Equals(correo, (leido.Correo ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            bool conContrasena = txtNueva.Text.Length > 0 || txtRepetida.Text.Length > 0;
            if (conContrasena)
            {
                // Las mismas reglas que el alta y el inicio de sesion (ReglasCuenta).
                string problema = ReglasCuenta.ProblemaDeContrasena(txtNueva.Text);
                if (txtNueva.Text.Length == 0)
                {
                    Marcar(txtNueva, "contraseña nueva", faltan, ref primero);
                }
                else if (problema != null)
                {
                    Marcar(txtNueva, problema, errores, ref primero);
                }
                else if (txtNueva.Text != txtRepetida.Text)
                {
                    Marcar(txtRepetida, "La contraseña nueva y su repetición no coinciden.", errores, ref primero);
                }
                else if (txtNueva.Text == txtActual.Text)
                {
                    Marcar(txtNueva, "La contraseña nueva es igual a la actual.", errores, ref primero);
                }
            }

            bool pideActual = conContrasena || cambiaCorreo;
            if (pideActual && txtActual.Text.Length == 0)
            {
                // Sin ella, quien encuentre la sesion abierta se quedaria con la cuenta.
                if (conContrasena)
                {
                    Marcar(txtActual, "contraseña actual", faltan, ref primero);
                }
                else
                {
                    Marcar(txtActual, "Para cambiar tu correo, escribe tu contraseña actual.", errores, ref primero);
                }
            }

            if (pideActual && contrasenaDelSiia && !InicioConSiia.NipValido(txtNip.Text))
            {
                Marcar(txtNip, "Para cambiar tu correo, escribe también tu NIP del SIIA (4 números).", errores, ref primero);
            }

            if (primero != null)
            {
                List<string> frases = new List<string>();
                if (faltan.Count > 0)
                {
                    frases.Add("Falta: " + EstiloAdmin.Enumerar(faltan) + ".");
                }

                frases.AddRange(errores);
                lblError.Text = string.Join(" ", frases);
                primero.Focus();
                return;
            }

            DatosUsuario nuevos;
            if (EsAlumno)
            {
                OpcionCombo semestre = cboSemestre.SelectedItem as OpcionCombo;
                nuevos = new DatosAlumno
                {
                    CarreraId = carrera.Id,
                    SemestreId = semestre == null || semestre.Id == 0 ? (int?)null : semestre.Id
                };
            }
            else
            {
                nuevos = new DatosUsuario();
            }

            nuevos.Matricula = leido.Matricula;
            nuevos.Nombre = txtNombre.Text.Trim();
            nuevos.ApellidoPaterno = txtPaterno.Text.Trim();
            nuevos.ApellidoMaterno = txtMaterno.Text.Trim();
            nuevos.Correo = correo;
            nuevos.ContrasenaNueva = conContrasena ? txtNueva.Text : null;

            if (!HayCambios(nuevos))
            {
                // Con el SIIA no hay contrasena nueva que pedir: nada que guardar.
                if (txtActual.Text.Length > 0 && !contrasenaDelSiia)
                {
                    // Escribio la actual y nada mas: le falta la nueva. Con otros cambios
                    // se guardan sin tocar la contrasena y la actual no se comprueba.
                    lblError.Text = "Falta: contraseña nueva.";
                    txtNueva.BackColor = EstiloAdmin.FondoError;
                    txtNueva.Focus();
                    return;
                }

                // Nada que guardar: como Cancelar.
                Close();
                return;
            }

            bool guardado = false;
            // Desactivar Guardar con el foco lo pasa a Cancelar: si falla, el foco vuelve
            // a Guardar (si no, un Enter para reintentar cerraria sin guardar).
            bool guardarConFoco = btnGuardar.Focused;
            Cursor = Cursors.WaitCursor;
            btnGuardar.Enabled = false;
            try
            {
                // Con el SIIA, lo guardado es la huella de la contrasena y el NIP juntos.
                datos.Guardar(nuevos, !pideActual ? null : contrasenaDelSiia ? InicioConSiia.Combinar(txtActual.Text, txtNip.Text) : txtActual.Text);
                guardado = true;
            }
            catch (ContrasenaIncorrecta)
            {
                ContrasenaEquivocada();
            }
            catch (ErrorDeDatos ex)
            {
                lblError.Text = ex.Message;
                if (ex.Campo == CampoRepetido.Correo)
                {
                    txtCorreo.BackColor = EstiloAdmin.FondoError;
                    txtCorreo.Focus();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Guardar Mi perfil de " + (datos.Identificador ?? "-"), ex);
                lblError.Text = MensajeDeFallo.Explicar("No se pudieron guardar tus cambios", ex, SiSigueFallando());
            }
            finally
            {
                Cursor = Cursors.Default;
                if (!IsDisposed)
                {
                    btnGuardar.Enabled = true;
                    // Salvo que el error haya llevado el foco a una caja (la actual o el correo).
                    if (guardarConFoco && ActiveControl == (Control)CancelButton)
                    {
                        btnGuardar.Focus();
                    }
                }
            }

            if (!guardado)
            {
                return;
            }

            if (pideActual)
            {
                // Acerto: los intentos vuelven a cero (como DialogoContrasenaAdmin).
                Datos_User.ConfirmacionesFallidas = 0;
            }

            MessageBox.Show(this, conContrasena
                    ? "Se guardaron tus cambios. Desde ahora entras con tu contraseña nueva."
                    : "Se guardaron tus cambios.",
                "Mi perfil", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// La actual no era: cuenta en la sesion como las de DialogoContrasenaAdmin.
        /// Con la tercera, quien no sabe la contrasena no sigue en la sesion: se
        /// cierra sola y no se guarda nada.
        /// </summary>
        private void ContrasenaEquivocada()
        {
            int fallidos = ++Datos_User.ConfirmacionesFallidas;
            txtActual.Clear();
            if (txtNip != null)
            {
                txtNip.Clear();
            }
            Log.Aviso("Mi perfil: contrasena actual equivocada de " + (datos.Identificador ?? "-") + " (" + fallidos + " de " + Intentos + ").");
            if (fallidos >= Intentos)
            {
                Enabled = false;
                if (AppContexto.Actual != null)
                {
                    AppContexto.Actual.CerrarSesionPorSeguridad("La contraseña actual de " + datos.Identificador + " no era correcta (" + Intentos
                        + " intentos): por seguridad se cerró la sesión y no se guardó ningún cambio.");
                }

                // Los dialogos modales los cierra CerrarSesionPorSeguridad; este, sin modal, no.
                if (!Modal || AppContexto.Actual == null)
                {
                    Close();
                }

                return;
            }

            lblError.Text = (contrasenaDelSiia ? "La contraseña o el NIP no son los del SIIA con los que entraste. " : "La contraseña actual no es correcta. ")
                + Quedan(fallidos);
            txtActual.BackColor = EstiloAdmin.FondoError;
            txtActual.Focus();
        }

        private static string Quedan(int fallidos)
        {
            int quedan = Intentos - fallidos;
            return quedan == 1 ? "Te queda 1 intento: si fallas, se cierra la sesión."
                : "Te quedan " + quedan + " intentos; si fallas todos, se cierra la sesión.";
        }

        private bool HayCambios(DatosUsuario nuevos)
        {
            if (!string.IsNullOrEmpty(nuevos.ContrasenaNueva) || !Igual(nuevos.Nombre, leido.Nombre)
                || !Igual(nuevos.ApellidoPaterno, leido.ApellidoPaterno) || !Igual(nuevos.ApellidoMaterno, leido.ApellidoMaterno)
                || !Igual(nuevos.Correo, leido.Correo))
            {
                return true;
            }

            DatosAlumno alumno = nuevos as DatosAlumno;
            AlumnoInfo antes = leido as AlumnoInfo;
            return alumno != null && antes != null && (alumno.CarreraId != antes.CarreraId || alumno.SemestreId != antes.SemestreId);
        }

        private static bool Igual(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);
        }

        private static void Marcar(Control control, string texto, List<string> lista, ref Control primero)
        {
            control.BackColor = EstiloAdmin.FondoError;
            lista.Add(texto);
            if (primero == null)
            {
                primero = control;
            }
        }

        /// <summary>Quita el rojo; una caja de solo lectura (el nombre del encargado) vuelve a su gris.</summary>
        private static void Limpiar(Control control)
        {
            TextBox caja = control as TextBox;
            control.BackColor = caja != null && caja.ReadOnly ? SystemColors.Control : SystemColors.Window;
        }
    }
}
