using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// "Olvidaste tu contrasena?" del inicio de sesion, tambien en el kiosco. En
    /// dos pasos: la matricula (la API manda un codigo de 6 cifras a su correo) y
    /// luego el codigo con la contrasena nueva. La API de recuperacion lo hace
    /// todo (RecuperacionContrasena); aqui solo se pide y se ensena lo que contesta.
    /// </summary>
    internal class DialogoRecuperarContrasena : Form
    {
        private const int Ancho = 520;
        private const int Alto = 430;
        private static readonly Regex SoloCifras = new Regex("^[0-9]{6}$");
        // La misma regla que el inicio de sesion (Login.cs) y la API.
        private static readonly Regex Identificador = new Regex("^[A-Za-z0-9]+$");
        private static readonly Color Gris = Color.FromArgb(70, 70, 70);
        private static readonly Color Rojo = Color.FromArgb(190, 30, 30);

        private readonly Label lblExplicacion;
        private readonly Panel pnlPaso1;
        private readonly Panel pnlPaso2;
        private readonly TextBox txtMatricula;
        private readonly TextBox txtCodigo;
        private readonly TextBox txtNueva;
        private readonly TextBox txtRepite;
        private readonly LinkLabel lnkOtraVez;
        private readonly LinkLabel lnkOtraMatricula;
        private readonly Label lblError;
        private readonly Button btnSiguiente;
        private readonly ApiRecuperacion api;
        // Con aviso de privacidad (Ajustes): una linea abajo con su enlace. Null sin aviso.
        private readonly LinkLabel lnkPrivacidad;
        private const int AltoAvisoPrivacidad = 42;
        private bool enPaso2;
        private bool ocupado;

        /// <summary>La matricula con la que se cambio la contrasena (para ponerla en el inicio de sesion).</summary>
        public string Matricula { get; private set; }

        /// <summary>True si se cambio la contrasena.</summary>
        public static bool Mostrar(IWin32Window dueno, ApiRecuperacion api, string matriculaEscrita, out string matricula)
        {
            using (DialogoRecuperarContrasena dialogo = new DialogoRecuperarContrasena(api, matriculaEscrita))
            {
                bool cambiada = dialogo.ShowDialog(dueno) == DialogResult.OK;
                matricula = cambiada ? dialogo.Matricula : null;
                return cambiada;
            }
        }

        private DialogoRecuperarContrasena(ApiRecuperacion api, string matriculaEscrita)
        {
            this.api = api;
            // El aviso de privacidad del TESCHI, si la administracion lo puso en Ajustes:
            // aqui se piden la matricula y el correo, asi que va su linea abajo.
            AvisoDePrivacidad aviso = AvisoDePrivacidad.Leer();
            EstiloHorarios.PrepararDialogo(this, "Recuperar contraseña", new Size(Ancho, Alto + (aviso.HayTexto ? AltoAvisoPrivacidad : 0)));
            // Encima de la pantalla del kiosco, que esta siempre al frente.
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Recuperar tu contraseña", Ancho - 48));

            lblExplicacion = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 10F),
                ForeColor = Gris,
                Location = new Point(24, 56),
                Size = new Size(Ancho - 48, 62)
            };
            Controls.Add(lblExplicacion);

            // Paso 1: la matricula.
            pnlPaso1 = new Panel { Location = new Point(24, 124), Size = new Size(Ancho - 48, 160), BackColor = Color.White };
            pnlPaso1.Controls.Add(EstiloHorarios.CrearEtiqueta("Matrícula o número de empleado", new Point(0, 0)));
            txtMatricula = EstiloHorarios.CrearTexto(new Point(0, 24), 260);
            txtMatricula.MaxLength = 45;
            txtMatricula.Font = new DrawingFont("Century Gothic", 12F, FontStyle.Bold);
            txtMatricula.Text = matriculaEscrita ?? "";
            pnlPaso1.Controls.Add(txtMatricula);
            pnlPaso1.Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Gris,
                Location = new Point(0, 70),
                Size = new Size(Ancho - 48, 60),
                Text = "Si tu cuenta no tiene correo, o no te llega, pide a la administración de DigBit que te ponga una contraseña nueva."
            });
            Controls.Add(pnlPaso1);

            // Paso 2: el codigo del correo y la contrasena nueva.
            pnlPaso2 = new Panel { Location = new Point(24, 124), Size = new Size(Ancho - 48, 160), BackColor = Color.White, Visible = false };
            pnlPaso2.Controls.Add(EstiloHorarios.CrearEtiqueta("Código del correo (6 cifras)", new Point(0, 0)));
            txtCodigo = EstiloHorarios.CrearTexto(new Point(0, 24), 160);
            // Copiado del correo puede traer espacios delante: se dejan solo las cifras.
            txtCodigo.MaxLength = 40;
            txtCodigo.Font = new DrawingFont("Consolas", 16F, FontStyle.Bold);
            txtCodigo.TextChanged += (s, e) =>
            {
                string cifras = new string(txtCodigo.Text.Where(c => c >= '0' && c <= '9').Take(6).ToArray());
                if (cifras != txtCodigo.Text)
                {
                    txtCodigo.Text = cifras;
                    txtCodigo.SelectionStart = cifras.Length;
                }
            };
            pnlPaso2.Controls.Add(txtCodigo);

            // Junto al codigo, una encima de otra: otro correo, o volver a la
            // matricula si se escribio mal (antes solo quedaba cancelar).
            lnkOtraVez = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                LinkColor = Color.DarkGreen,
                Location = new Point(180, 22),
                Text = "Mandar otro código"
            };
            lnkOtraVez.LinkClicked += (s, e) =>
            {
                if (!ocupado)
                {
                    PedirCodigo();
                }
            };
            pnlPaso2.Controls.Add(lnkOtraVez);

            lnkOtraMatricula = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                LinkColor = Color.DarkGreen,
                Location = new Point(180, 44),
                Text = "Usar otra matrícula"
            };
            lnkOtraMatricula.LinkClicked += (s, e) =>
            {
                if (!ocupado)
                {
                    VolverAlPaso1();
                }
            };
            pnlPaso2.Controls.Add(lnkOtraMatricula);

            pnlPaso2.Controls.Add(EstiloHorarios.CrearEtiqueta("Contraseña nueva", new Point(0, 74)));
            txtNueva = EstiloHorarios.CrearTexto(new Point(0, 98), 200);
            txtNueva.MaxLength = 128;
            txtNueva.UseSystemPasswordChar = true;
            pnlPaso2.Controls.Add(txtNueva);
            pnlPaso2.Controls.Add(EstiloHorarios.CrearEtiqueta("Repítela", new Point(220, 74)));
            txtRepite = EstiloHorarios.CrearTexto(new Point(220, 98), 200);
            txtRepite.MaxLength = 128;
            txtRepite.UseSystemPasswordChar = true;
            pnlPaso2.Controls.Add(txtRepite);
            // La regla antes del error, debajo de la contrasena; "Mostrar", debajo de la otra.
            pnlPaso2.Controls.Add(new Label
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Gris,
                Location = new Point(0, 130),
                Text = ReglasCuenta.TextoLargoMinimo
            });
            CheckBox mostrar = new CheckBox
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                Location = new Point(220, 128),
                Text = "Mostrar"
            };
            mostrar.CheckedChanged += (s, e) =>
            {
                txtNueva.UseSystemPasswordChar = !mostrar.Checked;
                txtRepite.UseSystemPasswordChar = !mostrar.Checked;
            };
            pnlPaso2.Controls.Add(mostrar);
            Controls.Add(pnlPaso2);

            lblError = EstiloAdmin.LineaError(new Point(24, 292), Ancho - 48, 72);
            Controls.Add(lblError);

            if (aviso.HayTexto)
            {
                lnkPrivacidad = aviso.CrearLinea(this, new Point(24, 368), Ancho - 48);
                Controls.Add(lnkPrivacidad);
            }

            // El boton se mide con el texto mas largo de los dos pasos.
            btnSiguiente = EstiloAdmin.BotonesDialogo(this, "Cambiar contraseña", Siguiente);

            // Mientras se espera a la API no se cierra (ni con Esc ni con la X): si
            // la contrasena llega a cambiar, el alumno tiene que enterarse.
            FormClosing += (s, e) =>
            {
                if (ocupado && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                }
            };

            MostrarPaso1();
            Shown += (s, e) => (txtMatricula.Text.Length > 0 ? (Control)btnSiguiente : txtMatricula).Focus();
        }

        private void MostrarPaso1()
        {
            enPaso2 = false;
            lblExplicacion.Text = "Te mandaremos a tu correo un código para poner una contraseña nueva. "
                + "Escribe tu matrícula (o tu número de empleado, si eres docente o responsable de laboratorio).";
            pnlPaso1.Visible = true;
            pnlPaso2.Visible = false;
            btnSiguiente.Text = "Enviar código";
        }

        /// <summary>"Usar otra matricula": de vuelta al paso 1, con la matricula escrita lista para cambiarla.</summary>
        private void VolverAlPaso1()
        {
            lblError.Text = "";
            MostrarPaso1();
            txtMatricula.SelectAll();
            txtMatricula.Focus();
        }

        private void MostrarPaso2(string explicacion)
        {
            enPaso2 = true;
            lblExplicacion.Text = explicacion;
            pnlPaso1.Visible = false;
            pnlPaso2.Visible = true;
            btnSiguiente.Text = "Cambiar contraseña";
            txtCodigo.Text = "";
            txtCodigo.Focus();
        }

        private void Siguiente()
        {
            if (ocupado)
            {
                return;
            }

            if (enPaso2)
            {
                CambiarContrasena();
            }
            else
            {
                PedirCodigo();
            }
        }

        private void PedirCodigo()
        {
            string matricula = txtMatricula.Text.Trim();
            if (!Identificador.IsMatch(matricula))
            {
                MostrarPaso1();
                Error("Escribe tu matrícula (o tu número de empleado), sin espacios.", txtMatricula);
                return;
            }

            Ejecutar("Enviando el código...", () => RecuperacionContrasena.Solicitar(api, matricula), respuesta =>
            {
                if (respuesta.Ok)
                {
                    lblError.Text = "";
                    MostrarPaso2(respuesta.Mensaje + " Revisa también la carpeta de spam. Escribe aquí el código y tu contraseña nueva.");
                }
                else if (enPaso2 || !string.IsNullOrEmpty(respuesta.Correo))
                {
                    // No hay codigo nuevo, pero sigue valiendo el del ultimo correo
                    // (la API solo anula los anteriores cuando el nuevo ya salio). Con
                    // correo en la respuesta es que ya se le mandaron demasiados.
                    if (!enPaso2)
                    {
                        MostrarPaso2("Escribe el código del último correo que te llegó y tu contraseña nueva.");
                    }

                    Error(respuesta.Mensaje, txtCodigo);
                }
                else
                {
                    Error(respuesta.Mensaje, txtMatricula);
                }
            });
        }

        private void CambiarContrasena()
        {
            string matricula = txtMatricula.Text.Trim();
            string codigo = txtCodigo.Text;
            if (!SoloCifras.IsMatch(codigo))
            {
                Error("El código son las 6 cifras que te llegaron por correo.", txtCodigo);
                return;
            }

            // Las mismas reglas que al dar de alta y que la API (ReglasCuenta).
            string problema = ReglasCuenta.ProblemaDeContrasena(txtNueva.Text);
            if (problema != null)
            {
                Error(problema, txtNueva);
                return;
            }

            if (txtNueva.Text != txtRepite.Text)
            {
                Error("Las dos contraseñas no coinciden.", txtRepite);
                return;
            }

            string nueva = txtNueva.Text;
            Ejecutar("Cambiando tu contraseña...", () => RecuperacionContrasena.Confirmar(api, matricula, codigo, nueva), respuesta =>
            {
                if (respuesta.Ok)
                {
                    Matricula = matricula;
                    DialogResult = DialogResult.OK;
                    return;
                }

                Error(respuesta.Mensaje, txtCodigo);
                txtCodigo.SelectAll();
            });
        }

        /// <summary>La llamada a la API en segundo plano (puede tardar unos segundos) y su respuesta de vuelta aqui.</summary>
        private void Ejecutar(string espera, Func<RespuestaRecuperacion> llamada, Action<RespuestaRecuperacion> despues)
        {
            ocupado = true;
            UseWaitCursor = true;
            btnSiguiente.Enabled = false;
            lnkOtraVez.Enabled = false;
            lnkOtraMatricula.Enabled = false;
            ActivarAvisoPrivacidad(false);
            ((Control)CancelButton).Enabled = false;
            lblError.ForeColor = Gris;
            lblError.Text = espera;
            Task.Run(llamada).ContinueWith(tarea =>
            {
                if (IsDisposed)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        ocupado = false;
                        UseWaitCursor = false;
                        btnSiguiente.Enabled = true;
                        lnkOtraVez.Enabled = true;
                        lnkOtraMatricula.Enabled = true;
                        ActivarAvisoPrivacidad(true);
                        ((Control)CancelButton).Enabled = true;
                        if (tarea.IsFaulted)
                        {
                            Exception error = tarea.Exception.GetBaseException();
                            if (!(error is ErrorDeRecuperacion))
                            {
                                Log.Error("Recuperar contrasena", error);
                            }

                            // Los de la API ya dicen la causa y que hacer; lo demas no se esperaba.
                            Error(error is ErrorDeRecuperacion ? error.Message
                                : "No se pudo completar por un error inesperado. Inténtalo de nuevo; si sigue, "
                                  + MensajeDeFallo.AvisaAlLaboratorio + ".",
                                enPaso2 ? txtCodigo : txtMatricula);
                            return;
                        }

                        despues(tarea.Result);
                    }));
                }
                catch (InvalidOperationException)
                {
                    // Se cerro el dialogo mientras se esperaba la respuesta.
                }
            });
        }

        private void Error(string texto, Control donde)
        {
            lblError.ForeColor = Rojo;
            lblError.Text = texto;
            if (donde.Visible)
            {
                donde.Focus();
            }
        }

        /// <summary>Mientras se espera a la API, el enlace del aviso tampoco se pulsa (como los demas).</summary>
        private void ActivarAvisoPrivacidad(bool activo)
        {
            if (lnkPrivacidad != null)
            {
                lnkPrivacidad.Enabled = activo;
            }
        }
    }

    /// <summary>
    /// El aviso de privacidad del TESCHI (RNF72): el simplificado (texto) y la
    /// direccion del integral, que la administracion pone en Ajustes (tabla
    /// configuracion, db/13; los equipos solo la leen). Mientras el texto este
    /// vacio no se ensena nada: DigBit no inventa ningun aviso. Lo muestran
    /// "Crear cuenta" (el texto y la casilla "Lei el aviso de privacidad") y
    /// "Olvidaste tu contrasena?" (una linea con el enlace).
    /// </summary>
    internal sealed class AvisoDePrivacidad
    {
        /// <summary>Sin aviso: falta db/13, o la administracion no lo ha puesto.</summary>
        public static readonly AvisoDePrivacidad Ninguno = new AvisoDePrivacidad("", "");

        private const string TextoEnlace = "Leer el aviso de privacidad completo";

        private static readonly object candado = new object();
        // Lo ultimo leido de la base (null: nada todavia): sin conexion con ella se sigue usando.
        private static AvisoDePrivacidad ultimo;

        private AvisoDePrivacidad(string texto, string url)
        {
            // Como lo pega la administracion, con sus saltos de linea (el TextBox quiere \r\n).
            Texto = (texto ?? "").Trim().Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
            Url = (url ?? "").Trim();
        }

        /// <summary>El aviso simplificado, con sus saltos de linea; vacio si no hay.</summary>
        public string Texto { get; private set; }

        /// <summary>La direccion del aviso integral, tal como se guardo (puede no servir: ver HayUrl).</summary>
        public string Url { get; private set; }

        /// <summary>Hay aviso que ensenar. Sin texto no se ensena nada, aunque haya direccion.</summary>
        public bool HayTexto
        {
            get { return Texto.Length > 0; }
        }

        /// <summary>
        /// Solo una direccion http(s) completa cuenta como enlace: nada mas se le
        /// pasa a Windows para abrirla (otra cosa podria lanzar un programa).
        /// </summary>
        public bool HayUrl
        {
            get
            {
                Uri uri;
                return Uri.TryCreate(Url, UriKind.Absolute, out uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
            }
        }

        /// <summary>
        /// El de la base, que la administracion puede haber cambiado. Sin conexion
        /// (o si ahora falla) vale el ultimo leido; si no hay ninguno, Ninguno.
        /// Nunca lanza: el aviso no puede impedir crear una cuenta ni recuperarla.
        /// </summary>
        public static AvisoDePrivacidad Leer()
        {
            if (!SinConexion.Activo)
            {
                Cursor anterior = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    Dictionary<string, string> ajustes = AjustesDatos.Leer(AjustesDatos.AvisoPrivacidadTexto, AjustesDatos.AvisoPrivacidadUrl);
                    AvisoDePrivacidad leido = ajustes == null ? Ninguno
                        : new AvisoDePrivacidad(Valor(ajustes, AjustesDatos.AvisoPrivacidadTexto), Valor(ajustes, AjustesDatos.AvisoPrivacidadUrl));
                    lock (candado)
                    {
                        ultimo = leido;
                    }

                    return leido;
                }
                catch (Exception ex)
                {
                    Log.Error("Aviso de privacidad: no se pudo leer de la base; se usa el ultimo leido", ex);
                }
                finally
                {
                    Cursor.Current = anterior;
                }
            }

            lock (candado)
            {
                return ultimo ?? Ninguno;
            }
        }

        private static string Valor(Dictionary<string, string> ajustes, string clave)
        {
            string valor;
            return ajustes.TryGetValue(clave, out valor) ? valor : "";
        }

        /// <summary>
        /// "Tus datos se tratan conforme al aviso de privacidad del TESCHI." y, en
        /// el renglon de abajo, el enlace "Leer el aviso de privacidad completo".
        /// Para el pie de un dialogo; quien la usa la anade a sus controles.
        /// </summary>
        public LinkLabel CrearLinea(IWin32Window dueno, Point posicion, int ancho)
        {
            string frase = "Tus datos se tratan conforme al aviso de privacidad del TESCHI." + Environment.NewLine;
            LinkLabel linea = new LinkLabel
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Color.FromArgb(70, 70, 70),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                DisabledLinkColor = Color.FromArgb(150, 150, 150),
                Location = posicion,
                Size = new Size(ancho, 36),
                Text = frase + TextoEnlace,
                LinkArea = new LinkArea(frase.Length, TextoEnlace.Length),
                AccessibleName = frase.TrimEnd() + " " + TextoEnlace
            };
            linea.LinkClicked += (s, e) => MostrarCompleto(dueno);
            return linea;
        }

        /// <summary>El enlace "Leer el aviso de privacidad completo" solo, para "Crear cuenta".</summary>
        public LinkLabel CrearEnlace(IWin32Window dueno)
        {
            LinkLabel enlace = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Text = TextoEnlace
            };
            enlace.LinkClicked += (s, e) => MostrarCompleto(dueno);
            return enlace;
        }

        /// <summary>
        /// "Leer el aviso de privacidad completo". Fuera del kiosco y con direccion,
        /// el navegador. En el kiosco nunca: seria una salida al escritorio. Ahi (o
        /// sin direccion, o si el navegador no abre) el aviso dentro de DigBit, con la
        /// direccion escrita para consultarla desde otro equipo o el celular.
        /// </summary>
        public void MostrarCompleto(IWin32Window dueno)
        {
            if (HayUrl && !Kiosco.Activo)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
                    Log.Info("Aviso de privacidad: se abre el integral en el navegador.");
                    return;
                }
                catch (Exception ex)
                {
                    // Sin navegador que lo abra: se ensena aqui, con la direccion escrita.
                    Log.Error("Abrir en el navegador el aviso de privacidad integral", ex);
                }
            }

            using (VentanaAvisoDePrivacidad dialogo = new VentanaAvisoDePrivacidad(this))
            {
                dialogo.ShowDialog(dueno);
            }
        }
    }

    /// <summary>
    /// El aviso de privacidad dentro de DigBit: el texto con desplazamiento y, si
    /// hay, la direccion del integral escrita (se puede seleccionar, no se abre).
    /// Es lo que se ve en el kiosco, donde no hay navegador.
    /// </summary>
    internal sealed class VentanaAvisoDePrivacidad : Form
    {
        private const int Ancho = 560;

        public VentanaAvisoDePrivacidad(AvisoDePrivacidad aviso)
        {
            bool conDireccion = aviso.HayUrl;
            int altoTexto = 260;
            int alto = 64 + altoTexto + (conDireccion ? 100 : 0) + 72;
            EstiloHorarios.PrepararDialogo(this, "Aviso de privacidad", new Size(Ancho, alto));
            // Encima de la pantalla del kiosco, que esta siempre al frente.
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Aviso de privacidad del TESCHI", Ancho - 48));

            TextBox texto = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(40, 40, 40),
                Font = new DrawingFont("Century Gothic", 10F),
                Location = new Point(24, 58),
                Size = new Size(Ancho - 48, altoTexto),
                Text = aviso.Texto,
                AccessibleName = "Aviso de privacidad"
            };
            Controls.Add(texto);

            int y = 58 + altoTexto + 14;
            if (conDireccion)
            {
                Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                    ForeColor = Color.DarkGreen,
                    Location = new Point(24, y),
                    Size = new Size(Ancho - 48, 20),
                    Text = "El aviso de privacidad integral está en esta dirección:"
                });
                TextBox direccion = new TextBox
                {
                    ReadOnly = true,
                    BackColor = Color.White,
                    Font = new DrawingFont("Century Gothic", 10F),
                    Location = new Point(24, y + 22),
                    Size = new Size(Ancho - 48, 26),
                    Text = aviso.Url,
                    AccessibleName = "Dirección del aviso de privacidad integral"
                };
                Controls.Add(direccion);
                Controls.Add(new Label
                {
                    AutoSize = false,
                    Font = new DrawingFont("Century Gothic", 9F),
                    ForeColor = Color.FromArgb(90, 90, 90),
                    Location = new Point(24, y + 54),
                    Size = new Size(Ancho - 48, 36),
                    Text = Kiosco.Activo
                        ? "En este equipo no se abre el navegador: consúltala desde tu celular o desde otro equipo."
                        : "No se pudo abrir el navegador: copia la dirección y ábrela tú."
                });
            }

            Button cerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(Ancho - 24 - 120, ClientSize.Height - 56), new Size(120, 40));
            cerrar.DialogResult = DialogResult.OK;
            Controls.Add(cerrar);
            AcceptButton = cerrar;
            CancelButton = cerrar;
            // Que se lea desde el principio, sin todo el texto seleccionado.
            Shown += (s, e) =>
            {
                texto.Select(0, 0);
                cerrar.Focus();
            };
        }
    }
}
