using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigBit.conexion;
using DigBit.Infraestructura;
using DrawingFont = System.Drawing.Font;

namespace DigBit
{
    /// <summary>
    /// Ajustes de toda la escuela, guardados en la base (AjustesDatos, db/13), un
    /// renglon por tema con como esta: los codigos de las clases, la
    /// recuperacion de contrasena por correo, el aviso de privacidad y el inicio
    /// de sesion de los docentes con el SIIA, cada uno con su boton
    /// "Configurar..." (su propia ventana, que guarda sola) a la derecha de su
    /// titulo, y la regla de los cambios de horario desde una fecha
    /// (VigenciaFranjas, db/20: siempre, solo se explica). Un solo boton,
    /// "Cerrar" (2026-10-31): "Guardar" nunca guardaba nada y "Cancelar" no
    /// deshacia lo que ya guardo un "Configurar...". Asi cabe a 1366x768.
    /// Valen al momento en todos los equipos con la 1.1, tambien en
    /// los congelados con Deep Freeze, sin tocar ninguno. Se abre desde
    /// "Ajustes", arriba en el panel del administrador. Solo el administrador:
    /// el encargado de laboratorio no lo abre (ensena la clave de la API).
    /// </summary>
    internal class DialogoAjustes : Form
    {
        // Cinco temas, cada uno con su boton junto al titulo, y abajo el error al
        // leerlos en tres lineas sin pisar "Cerrar". A 760 de ancho cada estado
        // ocupa hasta tres lineas y los temas van cada 96 px (con 86, la tercera
        // linea de un estado tapaba el borde del "Configurar..." siguiente): con
        // 668 de alto cabe a 1366x768.
        private const int Ancho = 760;
        private const int Alto = 668;
        private const int EntreTemas = 96;
        private static readonly Color Gris = Color.FromArgb(80, 80, 80);
        private static readonly Color Naranja = Color.FromArgb(190, 85, 0);

        private const string NotaConFecha = "Siempre: cambiar o quitar una franja que ya se dio aplica desde la fecha que elijas, y lo pasado "
            + "(bitácoras, faltas, asistencia y estadísticas) no se reescribe. Todos los equipos deben tener esta versión de DigBit: "
            + "uno viejo vería a la vez la franja terminada y la nueva.";

        private readonly Label lblError;
        private readonly Button btnCerrar;
        // Si algun "Configurar..." guardo algo: Cerrar devuelve OK.
        private bool huboCambios;
        private readonly Label lblCodigos;
        private readonly Label lblRecuperacion;
        private readonly Button btnRecuperacion;
        private readonly Label lblConFecha;
        private readonly Label lblAviso;
        private readonly Button btnAviso;
        private readonly Label lblSiia;
        private readonly Button btnSiia;
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };
        // Lo guardado del inicio de sesion con el SIIA (lo cambia DialogoInicioSiia).
        private bool siiaGuardado;
        private bool siiaEstudiantesGuardado;
        private string siiaUrlGuardada = "";
        private string siiaRecuperarGuardada = "";
        private string siiaPaginaGuardada = "";
        // Lo guardado de la recuperacion por correo (la cambia DialogoRecuperacionCorreo).
        private string urlGuardada = "";
        private string claveGuardada = "";
        // Lo guardado del aviso de privacidad (lo cambia DialogoAvisoPrivacidad).
        private string avisoGuardado = "";
        private string avisoUrlGuardada = "";

        public static bool Mostrar(IWin32Window dueno)
        {
            // Antes de construirlo: el constructor ya lee la clave de la API.
            if (Permisos.Bloquear(dueno, "Abrir Ajustes")) return false;

            using (DialogoAjustes dialogo = new DialogoAjustes())
            {
                return dialogo.ShowDialog(dueno) == DialogResult.OK;
            }
        }

        private DialogoAjustes()
        {
            EstiloHorarios.PrepararDialogo(this, "Ajustes", new Size(Ancho, Alto));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Ajustes", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Gris,
                Location = new Point(24, 50),
                Size = new Size(Ancho - 48, 20),
                Text = "Valen para toda la escuela: al momento, en todos los equipos con DigBit 1.1."
            });

            // Los codigos de las clases (db/11): su propia ventana, desde aqui tambien.
            // Cada boton "Configurar..." va a la derecha de su titulo y el estado
            // debajo, a todo lo ancho: asi caben los cuatro temas.
            const int yCodigos = 80;
            Controls.Add(Seccion(DialogoAccesoClases.Titulo, yCodigos));
            lblCodigos = Estado(yCodigos + 28);
            Controls.Add(lblCodigos);
            Button configurar = BotonDelTitulo("Configurar los códigos...", 220, yCodigos);
            configurar.Click += (s, e) =>
            {
                try
                {
                    if (DialogoAccesoClases.Mostrar(this))
                    {
                        huboCambios = true;
                    }
                }
                catch (Exception ex)
                {
                    AvisoDeFalloAdmin.Mostrar(this, "Abrir los codigos de las clases desde Ajustes", ex,
                        "No se pudieron abrir los códigos de las clases", "Ajustes");
                }

                ResumirCodigos(configurar);
            };
            Controls.Add(configurar);
            ResumirCodigos(configurar);

            // Los cambios de horario desde una fecha (db/20): cambiar o quitar una
            // franja que ya se dio la termina el dia antes y crea la nueva, sin
            // tocar lo pasado. Desde 2026-10-31 siempre (VigenciaFranjas.ConFecha):
            // es una regla, no algo que se elija. Antes era una casilla gris (marcada y
            // sin poder quitarse), que parecia "no disponible": ahora, solo el texto.
            // Sin db/20, en naranja lo que falta (CargarConFecha).
            const int yHorario = yCodigos + EntreTemas;
            Controls.Add(Seccion("Cambios de horario", yHorario));
            lblConFecha = Estado(yHorario + 28);
            lblConFecha.Text = NotaConFecha;
            Controls.Add(lblConFecha);

            // "Olvidaste tu contrasena?" (db/13): como esta y "Configurar...", que abre
            // la direccion y la clave del servicio con "Probar" (DialogoRecuperacionCorreo).
            const int yRecuperacion = yHorario + EntreTemas;
            Controls.Add(Seccion("Recuperar contraseña por correo", yRecuperacion));
            lblRecuperacion = Estado(yRecuperacion + 28);
            Controls.Add(lblRecuperacion);
            btnRecuperacion = BotonDelTitulo("Configurar...", 160, yRecuperacion);
            btnRecuperacion.Click += (s, e) => ConfigurarRecuperacion();
            ayuda.SetToolTip(btnRecuperacion, "La dirección y la clave del servicio de recuperación, con «Probar».");
            Controls.Add(btnRecuperacion);

            // El aviso de privacidad del TESCHI (RNF72): lo muestra "Crear cuenta" y lo
            // menciona "Olvidaste tu contrasena?". Sin texto no se muestra nada.
            const int yAviso = yRecuperacion + EntreTemas;
            Controls.Add(Seccion("Aviso de privacidad", yAviso));
            lblAviso = Estado(yAviso + 28);
            Controls.Add(lblAviso);
            btnAviso = BotonDelTitulo("Configurar...", 160, yAviso);
            btnAviso.AccessibleName = "Configurar el aviso de privacidad";
            btnAviso.Click += (s, e) => ConfigurarAviso();
            ayuda.SetToolTip(btnAviso, "El aviso simplificado que se muestra al crear una cuenta y la dirección del aviso integral, con su vista previa.");
            Controls.Add(btnAviso);

            // Los docentes con su contrasena del SIIA del TESCHI (InicioConSiia): el
            // interruptor y la direccion de login.ashx, con "Comprobar", en su ventana.
            const int ySiia = yAviso + EntreTemas;
            Controls.Add(Seccion("Inicio de sesión con el SIIA (docentes y estudiantes)", ySiia));
            lblSiia = Estado(ySiia + 28);
            Controls.Add(lblSiia);
            btnSiia = BotonDelTitulo("Configurar...", 160, ySiia);
            btnSiia.AccessibleName = "Configurar el inicio de sesión con el SIIA";
            btnSiia.Click += (s, e) => ConfigurarSiia();
            ayuda.SetToolTip(btnSiia, "Quién entra con el SIIA (docentes, estudiantes) y las direcciones del SIIA, con «Comprobar».");
            Controls.Add(btnSiia);

            lblError = EstiloAdmin.LineaError(new Point(24, ySiia + 88), Ancho - 48, 52);
            Controls.Add(lblError);

            // Cada "Configurar..." por encima de los textos: nunca se queda sin su borde.
            foreach (Button boton in new[] { configurar, btnRecuperacion, btnAviso, btnSiia })
            {
                boton.BringToFront();
            }

            // Un solo "Cerrar" (Enter y Esc): cada "Configurar..." guarda en su ventana.
            // Con "Guardar" y "Cancelar", quien cambiaba el SIIA y luego pulsaba
            // "Cancelar" creia que lo habia deshecho. Devuelve OK si algo cambio.
            // Con el aire de abajo de los demas dialogos (EstiloAdmin.BotonesDialogo).
            ClientSize = new Size(Ancho, ClientSize.Height + EstiloAdmin.AireBajoBotones);
            btnCerrar = EstiloHorarios.CrearBoton("Cerrar", new Point(Ancho - 24 - 120, ClientSize.Height - 56 - EstiloAdmin.AireBajoBotones), new Size(120, 40));
            btnCerrar.Click += (s, e) => DialogResult = huboCambios ? DialogResult.OK : DialogResult.Cancel;
            ayuda.SetToolTip(btnCerrar, "Cada «Configurar...» ya guardó lo suyo al aceptarlo.");
            Controls.Add(btnCerrar);
            AcceptButton = btnCerrar;
            CancelButton = btnCerrar;
            // CancelButton le pone Cancel: lo decide el clic (huboCambios).
            btnCerrar.DialogResult = DialogResult.None;
            CargarAjustes();
        }

        /// <summary>El titulo de un tema.</summary>
        private static Label Seccion(string texto, int y)
        {
            Label seccion = EstiloHorarios.CrearEtiqueta(texto, new Point(24, y));
            seccion.Font = new DrawingFont("Century Gothic", 11F, FontStyle.Bold);
            return seccion;
        }

        /// <summary>El "Configurar..." de un tema: a la derecha de su titulo, centrado con el.</summary>
        private static Button BotonDelTitulo(string texto, int ancho, int y)
        {
            return EstiloHorarios.CrearBoton(texto, new Point(Ancho - 24 - ancho, y - 7), new Size(ancho, 36));
        }

        /// <summary>Como esta un tema, debajo de su titulo: hasta tres lineas (con dos se perdia la ultima).</summary>
        private static Label Estado(int y)
        {
            return new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, y),
                Size = new Size(Ancho - 48, 56)
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Como estan ahora los codigos de las clases, con los nombres de las tres formas (ModosDeAcceso.Nombre).</summary>
        private void ResumirCodigos(Button configurar)
        {
            try
            {
                LaboratoriosDatos datos = new LaboratoriosDatos();
                List<LaboratorioInfo> activos = datos.Listar().FindAll(l => l.Activo);
                if (!datos.ModosDisponibles)
                {
                    lblCodigos.Text = "Por ahora se entra con «" + ModosDeAcceso.Nombre(ModoCodigo.Fijo) + "» en todos los laboratorios: "
                        + "para elegir otra forma falta aplicar db/11_modo_de_acceso.sql en la base.";
                    configurar.Enabled = false;
                    return;
                }

                List<ModoCodigo> modos = activos.ConvertAll(l => l.Modo);
                lblCodigos.Text = "Ahora: " + ModosDeAcceso.Resumen(ModosDeAcceso.General(datos.GeneralGuardada, modos), modos)
                    + ". Elige «" + ModosDeAcceso.Nombre(ModoCodigo.SinCodigo) + "», «" + ModosDeAcceso.Nombre(ModoCodigo.Fijo) + "» o «"
                    + ModosDeAcceso.Nombre(ModoCodigo.Renovable) + "» para toda la escuela, y otra forma para los laboratorios que la necesiten.";
            }
            catch (Exception ex)
            {
                Log.Error("Leer los codigos de las clases para Ajustes", ex);
                lblCodigos.ForeColor = Naranja;
                lblCodigos.Text = AvisoDeFalloAdmin.Texto("No se pudo leer cómo están", ex);
            }
        }

        private void CargarAjustes()
        {
            try
            {
                Dictionary<string, string> ajustes = AjustesDatos.Leer(AjustesDatos.UrlRecuperacion, AjustesDatos.ClaveRecuperacion, VigenciaFranjas.ClaveConFecha,
                    AjustesDatos.AvisoPrivacidadTexto, AjustesDatos.AvisoPrivacidadUrl, AjustesDatos.SiiaActivo, AjustesDatos.SiiaEstudiantes,
                    AjustesDatos.SiiaUrl, AjustesDatos.SiiaRecuperarUrl, AjustesDatos.SiiaPaginaUrl);
                if (ajustes == null)
                {
                    btnRecuperacion.Enabled = false;
                    btnAviso.Enabled = false;
                    btnSiia.Enabled = false;
                    lblSiia.ForeColor = Naranja;
                    lblSiia.Text = "Para usarlo falta aplicar db/13_recuperacion_contrasena.sql en la base (ahí se guarda este ajuste). "
                        + "Mientras tanto, los docentes entran con su contraseña de DigBit.";
                    lblError.Text = "Falta aplicar db/13_recuperacion_contrasena.sql en la base: hasta entonces no hay dónde guardar esto. "
                        + "Pide a quien administra el servidor que la aplique.";
                    lblRecuperacion.ForeColor = Naranja;
                    lblRecuperacion.Text = "Para activarla falta aplicar db/13_recuperacion_contrasena.sql en la base. Mientras tanto, "
                        + "«¿Olvidaste tu contraseña?» les dice que pidan a la administración de DigBit una contraseña nueva.";
                    lblAviso.ForeColor = Naranja;
                    lblAviso.Text = "Para configurarlo falta aplicar db/13_recuperacion_contrasena.sql en la base. Mientras tanto, "
                        + "«Crear cuenta» no muestra ningún aviso de privacidad.";
                    // Sin la 13 no hay donde guardarlo; la 20 solo se nombra si tambien falta.
                    bool conDb20 = false;
                    try
                    {
                        conDb20 = BaseConDb20();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Ajustes: saber si la base tiene db/20", ex);
                    }

                    lblConFecha.Text = conDb20
                        ? "Para usarlo falta aplicar db/13_recuperacion_contrasena.sql en la base (es donde se guarda este ajuste)."
                        : "Para usarlo faltan db/13_recuperacion_contrasena.sql y db/20_vigencia_franjas.sql en la base.";
                    return;
                }

                string valor;
                urlGuardada = ajustes.TryGetValue(AjustesDatos.UrlRecuperacion, out valor) ? valor.Trim() : "";
                claveGuardada = ajustes.TryGetValue(AjustesDatos.ClaveRecuperacion, out valor) ? valor : "";
                avisoGuardado = ajustes.TryGetValue(AjustesDatos.AvisoPrivacidadTexto, out valor) ? valor : "";
                avisoUrlGuardada = ajustes.TryGetValue(AjustesDatos.AvisoPrivacidadUrl, out valor) ? valor.Trim() : "";
                siiaGuardado = ajustes.TryGetValue(AjustesDatos.SiiaActivo, out valor) && valor.Trim() == "1";
                siiaEstudiantesGuardado = ajustes.TryGetValue(AjustesDatos.SiiaEstudiantes, out valor) && valor.Trim() == "1";
                siiaUrlGuardada = ajustes.TryGetValue(AjustesDatos.SiiaUrl, out valor) ? valor.Trim() : "";
                siiaRecuperarGuardada = ajustes.TryGetValue(AjustesDatos.SiiaRecuperarUrl, out valor) ? valor.Trim() : "";
                siiaPaginaGuardada = ajustes.TryGetValue(AjustesDatos.SiiaPaginaUrl, out valor) ? valor.Trim() : "";
                ResumirRecuperacion();
                ResumirAviso();
                ResumirSiia();
                CargarConFecha();
            }
            catch (Exception ex)
            {
                Log.Error("Leer los ajustes", ex);
                btnRecuperacion.Enabled = false;
                btnAviso.Enabled = false;
                btnSiia.Enabled = false;
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudieron leer los ajustes", ex);
                lblRecuperacion.Text = "No se pudo leer cómo está.";
                lblAviso.Text = "No se pudo leer cómo está.";
                lblSiia.Text = "No se pudo leer cómo está.";
            }
        }

        /// <summary>
        /// Como esta el inicio de sesion de los docentes con el SIIA: apagado
        /// (con su contrasena de DigBit) o encendido, con el servidor del SIIA. La
        /// direccion entera, en su ayuda.
        /// </summary>
        private void ResumirSiia()
        {
            ConfiguracionSiia siia = new ConfiguracionSiia(siiaGuardado, siiaEstudiantesGuardado, siiaUrlGuardada, siiaRecuperarGuardada, siiaPaginaGuardada);
            lblSiia.ForeColor = Gris;
            lblSiia.Text = !siia.Activo
                ? "Apagado: docentes y estudiantes entran con su contraseña de DigBit. Encendido, entran con su número (o su matrícula), su contraseña "
                    + "y su NIP del SIIA del TESCHI."
                : "Encendido para " + (siia.Docentes && siia.Estudiantes ? "docentes y estudiantes" : siia.Docentes ? "docentes" : "estudiantes")
                    + ", con el SIIA de " + siia.Sitio + ": entran con su contraseña y su NIP del SIIA"
                    + (siia.Docentes ? "; los docentes, una vez que los enlazas (Alertas > «Docentes por enlazar»)" : "")
                    + (siia.Estudiantes ? "; la cuenta de cada estudiante se crea sola" : "") + ".";
            ayuda.SetToolTip(lblSiia, "login.ashx: " + siia.Url + Environment.NewLine + "Recuperar la contraseña: " + siia.RecuperarUrl
                + Environment.NewLine + "Crear el NIP: " + siia.PaginaUrl);
        }

        /// <summary>Los interruptores y las direcciones del SIIA, en su ventana: si los guarda, aqui se dice como quedo.</summary>
        private void ConfigurarSiia()
        {
            if (Permisos.Bloquear(this, "Configurar el inicio de sesion con el SIIA")) return;

            using (DialogoInicioSiia dialogo = new DialogoInicioSiia(siiaGuardado, siiaEstudiantesGuardado, siiaUrlGuardada, siiaRecuperarGuardada, siiaPaginaGuardada))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    siiaGuardado = dialogo.DocentesGuardado;
                    siiaEstudiantesGuardado = dialogo.EstudiantesGuardado;
                    siiaUrlGuardada = dialogo.UrlGuardada;
                    siiaRecuperarGuardada = dialogo.RecuperarGuardada;
                    siiaPaginaGuardada = dialogo.PaginaGuardada;
                    huboCambios = true;
                    ResumirSiia();
                }
            }
        }

        /// <summary>
        /// Como esta el aviso de privacidad: sin aviso (no se muestra nada) o
        /// activado, con o sin el enlace al integral. El principio del texto y la
        /// direccion, en su ayuda.
        /// </summary>
        private void ResumirAviso()
        {
            Uri uri;
            if (avisoGuardado.Trim().Length == 0)
            {
                lblAviso.ForeColor = Gris;
                lblAviso.Text = "Sin aviso: «Crear cuenta» y «¿Olvidaste tu contraseña?» no muestran ninguno. Con «Configurar...» pega el aviso "
                    + "simplificado que publique la Unidad de Transparencia del TESCHI y la dirección del integral.";
                ayuda.SetToolTip(lblAviso, null);
                return;
            }

            int caracteres = avisoGuardado.Trim().Length;
            lblAviso.ForeColor = Gris;
            lblAviso.Text = "Activado (" + caracteres.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-MX"))
                + (caracteres == 1 ? " carácter" : " caracteres") + "): «Crear cuenta» lo muestra y pide marcar «Leí el aviso de privacidad»; "
                + "«¿Olvidaste tu contraseña?» lo menciona. "
                + (avisoUrlGuardada.Length == 0 ? "Sin enlace al aviso integral."
                    : "Con enlace al aviso integral" + (Uri.TryCreate(avisoUrlGuardada, UriKind.Absolute, out uri) ? " (" + uri.Host + ")." : "."));
            string inicio = avisoGuardado.Trim();
            ayuda.SetToolTip(lblAviso, (inicio.Length > 300 ? inicio.Substring(0, 300) + "..." : inicio)
                + (avisoUrlGuardada.Length == 0 ? "" : Environment.NewLine + Environment.NewLine + "Aviso integral: " + avisoUrlGuardada));
        }

        /// <summary>El aviso de privacidad en su ventana, con su vista previa: si lo guarda, aqui se dice como quedo.</summary>
        private void ConfigurarAviso()
        {
            if (Permisos.Bloquear(this, "Configurar el aviso de privacidad")) return;

            using (DialogoAvisoPrivacidad dialogo = new DialogoAvisoPrivacidad(avisoGuardado, avisoUrlGuardada))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    avisoGuardado = dialogo.TextoGuardado;
                    avisoUrlGuardada = dialogo.UrlGuardada;
                    huboCambios = true;
                    ResumirAviso();
                }
            }
        }

        /// <summary>
        /// Como esta la recuperacion por correo, sin lo tecnico: activada (con el
        /// servidor del servicio) o desactivada. La direccion entera, en su ayuda.
        /// </summary>
        private void ResumirRecuperacion()
        {
            Uri uri;
            string texto = urlGuardada.Length == 0
                ? "Desactivada: «¿Olvidaste tu contraseña?» les dice que pidan a la administración de DigBit una contraseña nueva."
                : "Activada" + (Uri.TryCreate(urlGuardada, UriKind.Absolute, out uri) ? ", con el servicio de " + uri.Host : "")
                  + ": «¿Olvidaste tu contraseña?» les manda un código de 6 cifras a su correo para que pongan una contraseña nueva.";
            // Un equipo con su propia direccion no usa la de aqui (y confundiria al probar en el).
            if (RecuperacionContrasena.DeEsteEquipo().Configurada)
            {
                texto += " Este equipo tiene su propia dirección y usa esa.";
            }

            lblRecuperacion.ForeColor = Gris;
            lblRecuperacion.Text = texto;
            ayuda.SetToolTip(lblRecuperacion, urlGuardada.Length == 0 ? null : "Dirección del servicio: " + urlGuardada);
        }

        /// <summary>La direccion y la clave del servicio, en su ventana: si las guarda, aqui se dice como quedo.</summary>
        private void ConfigurarRecuperacion()
        {
            if (Permisos.Bloquear(this, "Configurar la recuperacion por correo")) return;

            using (DialogoRecuperacionCorreo dialogo = new DialogoRecuperacionCorreo(urlGuardada, claveGuardada))
            {
                if (dialogo.ShowDialog(this) == DialogResult.OK)
                {
                    urlGuardada = dialogo.UrlGuardada;
                    claveGuardada = dialogo.ClaveGuardada;
                    huboCambios = true;
                    ResumirRecuperacion();
                }
            }
        }

        /// <summary>
        /// Los cambios de horario desde una fecha necesitan db/20 (cada franja con su
        /// vigencia): sin ella, en naranja, que falta y que pasa mientras tanto (la
        /// escuela aun no la tiene). Con ella, la regla (NotaConFecha).
        /// </summary>
        private void CargarConFecha()
        {
            try
            {
                // Siempre con db/20 (VigenciaFranjas.ConFecha): no se cambia aqui.
                if (!BaseConDb20())
                {
                    lblConFecha.ForeColor = Naranja;
                    lblConFecha.Text = "Falta aplicar db/20_vigencia_franjas.sql en la base. Hasta entonces, cambiar o quitar "
                        + "una franja también cambia sus semanas pasadas en Faltas.";
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ajustes: saber si la base tiene db/20", ex);
                lblConFecha.ForeColor = Naranja;
                lblConFecha.Text = AvisoDeFalloAdmin.Texto("No se pudo saber si la base lo admite", ex);
            }
        }

        /// <summary>Si la base tiene db/20 (cada franja con su vigencia). Lanza si no se puede leer.</summary>
        private static bool BaseConDb20()
        {
            using (MySql.Data.MySqlClient.MySqlConnection conexion = new Conexion().GetConexion())
            {
                return Esquema.VigenciaDeFranjas(conexion);
            }
        }

    }

    /// <summary>
    /// "Recuperar contrasena por correo", desde Ajustes: la direccion del servicio
    /// de recuperacion (api/recuperar.php en el hosting de la escuela) y su clave
    /// (clave_api de su config.php), con "Probar". Guardar lo deja en la base
    /// (AjustesDatos): vale al momento en todos los equipos con la 1.1, tambien en
    /// los congelados con Deep Freeze. Por eso no admite la direccion de la propia
    /// PC (ProblemaDeDireccionParaTodos). Lo tecnico (recuperar.php, config.php,
    /// api/LEEME.md) va en la ayuda de cada campo, no a la vista.
    /// </summary>
    internal class DialogoRecuperacionCorreo : Form
    {
        private const int Ancho = 600;
        // El error de Guardar en tres lineas (la causa y que hacer) sin pisar los botones.
        private const int Alto = 524;
        private static readonly Color Gris = Color.FromArgb(80, 80, 80);
        private static readonly Color Rojo = Color.FromArgb(190, 30, 30);

        private readonly string urlAntes;
        private readonly string claveAntes;
        private readonly TextBox txtUrl;
        private readonly TextBox txtClave;
        private readonly Button btnProbar;
        private readonly Label lblPrueba;
        private readonly Label lblError;
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };
        private bool probando;

        public DialogoRecuperacionCorreo(string url, string clave)
        {
            urlAntes = url ?? "";
            claveAntes = clave ?? "";
            EstiloHorarios.PrepararDialogo(this, "Recuperar contraseña por correo", new Size(Ancho, Alto));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Recuperar contraseña por correo", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 56),
                Size = new Size(Ancho - 48, 90),
                Text = "«¿Olvidaste tu contraseña?», en el inicio de sesión de todos los equipos (también en el kiosco), "
                    + "le manda un código de 6 cifras al correo de la persona (estudiante, docente o responsable de laboratorio) para que ponga una contraseña nueva. "
                    + "Lo envía el servicio de recuperación de DigBit, instalado en el hosting de la escuela. "
                    + "Lo que guardes aquí vale al momento en todos los equipos con DigBit 1.1."
            });

            Label etiquetaUrl = EstiloHorarios.CrearEtiqueta("Dirección del servicio de recuperación", new Point(24, 154));
            Controls.Add(etiquetaUrl);
            txtUrl = EstiloHorarios.CrearTexto(new Point(24, 178), Ancho - 48);
            txtUrl.MaxLength = 500;
            txtUrl.Text = urlAntes;
            EstiloHorarios.PonerTextoGuia(txtUrl, "https://...");
            Controls.Add(txtUrl);
            const string AyudaUrl = "La dirección completa de recuperar.php en el hosting de la escuela, con https:// "
                + "(api/LEEME.md, en el repositorio de DigBit, dice cómo se instala).";
            ayuda.SetToolTip(etiquetaUrl, AyudaUrl);
            ayuda.SetToolTip(txtUrl, AyudaUrl);

            Label etiquetaClave = EstiloHorarios.CrearEtiqueta("Clave del servicio", new Point(24, 218));
            Controls.Add(etiquetaClave);
            txtClave = EstiloHorarios.CrearTexto(new Point(24, 242), 300);
            txtClave.MaxLength = 500;
            txtClave.UseSystemPasswordChar = true;
            txtClave.Text = claveAntes;
            Controls.Add(txtClave);
            const string AyudaClave = "La clave que el servicio espera de DigBit: clave_api, en el config.php del servicio.";
            ayuda.SetToolTip(etiquetaClave, AyudaClave);
            ayuda.SetToolTip(txtClave, AyudaClave);
            CheckBox mostrar = new CheckBox
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                Location = new Point(336, 245),
                Text = "Mostrar"
            };
            mostrar.CheckedChanged += (s, e) => txtClave.UseSystemPasswordChar = !mostrar.Checked;
            Controls.Add(mostrar);

            btnProbar = EstiloHorarios.CrearBoton("Probar", new Point(24, 288), new Size(110, 36));
            btnProbar.Click += (s, e) => Probar();
            Controls.Add(btnProbar);
            lblPrueba = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Gris,
                Location = new Point(146, 284),
                Size = new Size(Ancho - 146 - 24, 76),
                Text = "Comprueba la dirección, la clave, la base y la cuenta de correo, sin mandar ningún correo."
            };
            Controls.Add(lblPrueba);

            Label nota = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Gris,
                Location = new Point(24, 366),
                Size = new Size(Ancho - 48, 36),
                Text = "Sin dirección queda desactivada: el enlace les dice que pidan a la administración de DigBit una contraseña nueva."
            };
            // Un equipo con su propia direccion no usa la de aqui (y confundiria al probar en el).
            if (RecuperacionContrasena.DeEsteEquipo().Configurada)
            {
                nota.ForeColor = Color.FromArgb(190, 85, 0);
                nota.Text = "Este equipo tiene su propia dirección en su configuración y usa esa; lo de aquí vale para los demás.";
                ayuda.SetToolTip(nota, "UrlRecuperacion en DigBit.exe.config (o la variable de entorno DIGBIT_URL_RECUPERACION).");
            }

            Controls.Add(nota);

            lblError = EstiloAdmin.LineaError(new Point(24, 406), Ancho - 48, 54);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            ActiveControl = txtUrl;
        }

        /// <summary>La direccion que quedo guardada (vacia: desactivada).</summary>
        public string UrlGuardada { get; private set; }

        /// <summary>La clave que quedo guardada (vacia sin direccion).</summary>
        public string ClaveGuardada { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Probar()
        {
            // La API deja una fila "(prueba)" en recuperacion_contrasena.
            if (Permisos.Bloquear(this, "Probar la API de recuperacion")) return;

            if (probando)
            {
                return;
            }

            string problema = RecuperacionContrasena.ProblemaDeDireccionParaTodos(txtUrl.Text);
            if (problema != null)
            {
                Resultado(false, problema);
                txtUrl.Focus();
                return;
            }

            ApiRecuperacion api = new ApiRecuperacion(txtUrl.Text, txtClave.Text);
            probando = true;
            btnProbar.Enabled = false;
            UseWaitCursor = true;
            lblPrueba.ForeColor = Gris;
            lblPrueba.Text = "Probando...";
            Task.Run(() => RecuperacionContrasena.Probar(api)).ContinueWith(tarea =>
            {
                if (IsDisposed)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        probando = false;
                        btnProbar.Enabled = true;
                        UseWaitCursor = false;
                        if (tarea.IsFaulted)
                        {
                            Exception error = tarea.Exception.GetBaseException();
                            ErrorDeRecuperacion deLaApi = error as ErrorDeRecuperacion;
                            if (deLaApi == null)
                            {
                                Log.Error("Probar la API de recuperacion", error);
                                Resultado(false, AvisoDeFalloAdmin.Texto("No se pudo probar", error));
                                return;
                            }

                            Resultado(false, ExplicarPrueba(deLaApi));
                            return;
                        }

                        RespuestaRecuperacion respuesta = tarea.Result;
                        if (respuesta.Ok)
                        {
                            Resultado(true, respuesta.Mensaje);
                        }
                        else
                        {
                            // El 403 de la clave esta escrito para el alumno; al administrador, lo que tiene que mirar.
                            Resultado(false, "El servicio contestó: " + respuesta.Mensaje
                                + (respuesta.Estado == 403 && respuesta.Mensaje.Contains("autorizado") ? " Revisa que la clave sea la misma que tiene el servicio." : ""));
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    // Se cerro el dialogo mientras se probaba.
                }
            });
        }

        private void Resultado(bool bien, string texto)
        {
            lblPrueba.ForeColor = bien ? Color.DarkGreen : Rojo;
            lblPrueba.Text = (bien ? "✔ " : "✖ ") + texto;
        }

        /// <summary>
        /// Por que no contesto el servicio, para quien lo prueba: la causa y que
        /// revisar. Lo que el detalle trae de Windows (en ingles, con su estado
        /// entre parentesis) va al log y no aqui; lo que ya viene en espanol (la
        /// direccion redirige, no es la de recuperar.php...), tal cual.
        /// </summary>
        private static string ExplicarPrueba(ErrorDeRecuperacion error)
        {
            string detalle = (error.Detalle ?? "").Trim();
            System.Text.RegularExpressions.Match estado = System.Text.RegularExpressions.Regex.Match(detalle, @"\(([A-Za-z]\w*)\)$");
            System.Net.WebExceptionStatus causa;
            if (estado.Success && Enum.TryParse(estado.Groups[1].Value, out causa))
            {
                Log.Aviso("Probar la API de recuperacion: " + detalle);
                switch (causa)
                {
                    case System.Net.WebExceptionStatus.NameResolutionFailure:
                        return "No se encontró el servidor de esa dirección: revisa que esté bien escrita y que este equipo tenga internet.";
                    case System.Net.WebExceptionStatus.ConnectFailure:
                        return "El servidor de esa dirección no aceptó la conexión: revisa la dirección y que el servicio esté instalado en el hosting.";
                    case System.Net.WebExceptionStatus.Timeout:
                        return "El servicio tardó demasiado en contestar: inténtalo de nuevo en un momento.";
                    case System.Net.WebExceptionStatus.TrustFailure:
                    case System.Net.WebExceptionStatus.SecureChannelFailure:
                        return "No se pudo abrir una conexión segura (https) con el servicio: revisa la dirección y el certificado del hosting.";
                    default:
                        return "No se pudo hablar con el servicio: revisa la dirección y que este equipo tenga internet; "
                            + "el detalle quedó en el registro de diagnóstico de este equipo.";
                }
            }

            if (detalle.IndexOf("dirección", StringComparison.OrdinalIgnoreCase) >= 0 || detalle.StartsWith("HTTP ", StringComparison.Ordinal))
            {
                return "No contestó como se esperaba: " + detalle;
            }

            Log.Aviso("Probar la API de recuperacion: " + detalle);
            return "Se cortó la comunicación con el servicio: inténtalo de nuevo; si sigue fallando, revisa el registro de diagnóstico "
                + "de este equipo, donde quedó el detalle.";
        }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Guardar los ajustes")) return;

            string url = txtUrl.Text.Trim();
            string clave = txtClave.Text.Trim();
            lblError.Text = "";
            if (url.Length > 0)
            {
                string problema = RecuperacionContrasena.ProblemaDeDireccionParaTodos(url);
                if (problema != null)
                {
                    lblError.Text = problema;
                    txtUrl.Focus();
                    return;
                }
            }
            else if (urlAntes.Length > 0
                && MessageBox.Show(this, "Sin dirección, «¿Olvidaste tu contraseña?» deja de mandar códigos en todos los equipos. ¿La desactivas?",
                    "Ajustes", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            // Sin direccion, la clave no sirve de nada: se borra con ella.
            string claveNueva = url.Length > 0 ? clave : "";
            if (url == urlAntes && claveNueva == claveAntes.Trim())
            {
                // Sin cambios: nada que guardar.
                DialogResult = DialogResult.Cancel;
                return;
            }

            try
            {
                AjustesDatos.Guardar(new Dictionary<string, string>
                {
                    { AjustesDatos.UrlRecuperacion, url },
                    { AjustesDatos.ClaveRecuperacion, claveNueva }
                });
            }
            catch (Exception ex)
            {
                Log.Error("Guardar la recuperacion por correo", ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo guardar", ex);
                return;
            }

            UrlGuardada = url;
            ClaveGuardada = claveNueva;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>
    /// "Inicio de sesion con el SIIA", desde Ajustes: un interruptor para los
    /// docentes y otro para los estudiantes (los dos apagados por omision), la
    /// direccion de login.ashx del SIIA del TESCHI, con "Comprobar" (solo mira
    /// que responde: un GET sin datos ni credenciales, que debe dar 405), y las
    /// paginas del SIIA para recuperar la contrasena y para crear el NIP (solo
    /// https). Guardar lo deja en la base (AjustesDatos) y vale al momento en
    /// todos los equipos con esta version; pide otra vez la contrasena de la
    /// administracion, porque quien controle esas direcciones decide quien entra.
    /// </summary>
    internal class DialogoInicioSiia : Form
    {
        private const int Ancho = 600;
        private const int Alto = 628;
        private static readonly Color Gris = Color.FromArgb(80, 80, 80);
        private static readonly Color Rojo = Color.FromArgb(190, 30, 30);

        private readonly bool docentesAntes;
        private readonly bool estudiantesAntes;
        private readonly string urlAntes;
        private readonly string recuperarAntes;
        private readonly string paginaAntes;
        private readonly CheckBox chkDocentes;
        private readonly CheckBox chkEstudiantes;
        private readonly TextBox txtUrl;
        private readonly TextBox txtRecuperar;
        private readonly TextBox txtPagina;
        private readonly Button btnComprobar;
        private readonly Label lblPrueba;
        private readonly Label lblError;
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };
        private bool comprobando;

        public DialogoInicioSiia(bool docentes, bool estudiantes, string url, string recuperar, string pagina)
        {
            docentesAntes = docentes;
            estudiantesAntes = estudiantes;
            // Sin nada guardado, las de por omision (son las que se usan).
            urlAntes = string.IsNullOrWhiteSpace(url) ? InicioConSiia.UrlPorOmision : url.Trim();
            recuperarAntes = string.IsNullOrWhiteSpace(recuperar) ? InicioConSiia.RecuperarPorOmision : recuperar.Trim();
            paginaAntes = string.IsNullOrWhiteSpace(pagina) ? InicioConSiia.PaginaPorOmision : pagina.Trim();
            EstiloHorarios.PrepararDialogo(this, "Inicio de sesión con el SIIA", new Size(Ancho, Alto));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Inicio de sesión con el SIIA", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 56),
                Size = new Size(Ancho - 48, 72),
                Text = "Encendido, entran con su número (o su matrícula), su contraseña y su NIP del SIIA del TESCHI, en todos los equipos. "
                    + "La administración y las personas responsables de laboratorio siguen con su contraseña de DigBit. "
                    + "DigBit solo guarda la huella de la contraseña y el NIP."
            });

            chkDocentes = Casilla("Los docentes entran con el SIIA", docentes, 134);
            ayuda.SetToolTip(chkDocentes, "La primera vez, cada docente queda en Alertas > «Docentes por enlazar» hasta que lo enlaces con su cuenta.");
            chkEstudiantes = Casilla("Los estudiantes entran con el SIIA", estudiantes, 160);
            ayuda.SetToolTip(chkEstudiantes, "Su cuenta de DigBit se usa (o se crea sola) por su matrícula; el kiosco ya no ofrece «Crear cuenta».");

            Label etiquetaUrl = EstiloHorarios.CrearEtiqueta("Dirección del inicio de sesión del SIIA (login.ashx)", new Point(24, 192));
            Controls.Add(etiquetaUrl);
            txtUrl = Caja(new Point(24, 216), urlAntes, InicioConSiia.UrlPorOmision);
            string ayudaUrl = "La dirección completa de login.ashx, con https://. El NIP se verifica en nip.ashx, en la misma carpeta. "
                + "Vacía, la del SIIA del TESCHI: " + InicioConSiia.UrlPorOmision;
            ayuda.SetToolTip(etiquetaUrl, ayudaUrl);
            ayuda.SetToolTip(txtUrl, ayudaUrl);

            btnComprobar = EstiloHorarios.CrearBoton("Comprobar", new Point(24, 256), new Size(130, 36));
            btnComprobar.Click += (s, e) => Comprobar();
            Controls.Add(btnComprobar);
            lblPrueba = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Gris,
                Location = new Point(166, 252),
                Size = new Size(Ancho - 166 - 24, 60),
                Text = "Comprueba que la dirección responde como el inicio de sesión del SIIA, sin mandar ninguna contraseña."
            };
            Controls.Add(lblPrueba);

            // "Olvidaste tu contrasena?" (o tu NIP) de quien entra con el SIIA.
            Label etiquetaRecuperar = EstiloHorarios.CrearEtiqueta("Página del SIIA para recuperar la contraseña (https)", new Point(24, 318));
            Controls.Add(etiquetaRecuperar);
            txtRecuperar = Caja(new Point(24, 342), recuperarAntes, InicioConSiia.RecuperarPorOmision);
            string ayudaRecuperar = "A donde manda «¿Olvidaste tu contraseña?» (o tu NIP): DigBit no la recupera. Fuera del kiosco la abre en el navegador; "
                + "en el kiosco solo enseña la dirección. Vacía: " + InicioConSiia.RecuperarPorOmision;
            ayuda.SetToolTip(etiquetaRecuperar, ayudaRecuperar);
            ayuda.SetToolTip(txtRecuperar, ayudaRecuperar);

            // Quien aun no tiene NIP lo crea en el SIIA (DigBit no lo crea).
            Label etiquetaPagina = EstiloHorarios.CrearEtiqueta("Página del SIIA para crear el NIP (https)", new Point(24, 380));
            Controls.Add(etiquetaPagina);
            txtPagina = Caja(new Point(24, 404), paginaAntes, InicioConSiia.PaginaPorOmision);
            string ayudaPagina = "A donde se manda a quien todavía no tiene NIP del SIIA, para crearlo. Vacía: " + InicioConSiia.PaginaPorOmision;
            ayuda.SetToolTip(etiquetaPagina, ayudaPagina);
            ayuda.SetToolTip(txtPagina, ayudaPagina);

            Label nota = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9F),
                ForeColor = Gris,
                Location = new Point(24, 440),
                Size = new Size(Ancho - 48, 76),
                Text = "Si el SIIA no responde, solo entra quien ya entró antes con él (los docentes, ya enlazados), con la contraseña y el NIP "
                    + "de la última vez; un estudiante sin eso pide a su profesor que le dé asistencia. Si el SIIA los rechaza o bloquea los "
                    + "intentos, no entran."
            };
            ayuda.SetToolTip(nota, "Sin respuesta es: sin internet, más de " + InicioConSiia.SegundosDeEspera + " segundos de espera, una falla del SIIA "
                + "o una respuesta que no se entiende. Una cuenta dada de baja en DigBit no entra aunque el SIIA la acepte.");
            Controls.Add(nota);

            lblError = EstiloAdmin.LineaError(new Point(24, 518), Ancho - 48, 44);
            Controls.Add(lblError);
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            ActiveControl = chkDocentes;
        }

        private CheckBox Casilla(string texto, bool marcada, int y)
        {
            CheckBox casilla = new CheckBox
            {
                AutoSize = true,
                Checked = marcada,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30),
                Location = new Point(24, y),
                Text = texto
            };
            casilla.CheckedChanged += (s, e) => lblError.Text = "";
            Controls.Add(casilla);
            return casilla;
        }

        private TextBox Caja(Point posicion, string texto, string guia)
        {
            TextBox caja = EstiloHorarios.CrearTexto(posicion, Ancho - 48);
            caja.MaxLength = 500;
            caja.Text = texto;
            EstiloHorarios.PonerTextoGuia(caja, guia);
            caja.TextChanged += (s, e) => lblError.Text = "";
            Controls.Add(caja);
            return caja;
        }

        /// <summary>Si quedaron encendidos los docentes.</summary>
        public bool DocentesGuardado { get; private set; }

        /// <summary>Si quedaron encendidos los estudiantes.</summary>
        public bool EstudiantesGuardado { get; private set; }

        /// <summary>Las direcciones que quedaron guardadas (vacias: las de por omision).</summary>
        public string UrlGuardada { get; private set; }

        public string RecuperarGuardada { get; private set; }

        public string PaginaGuardada { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Lo escrito, o la de por omision si la caja esta vacia.</summary>
        private static string Escrita(TextBox caja, string porOmision)
        {
            string url = caja.Text.Trim();
            return url.Length == 0 ? porOmision : url;
        }

        private void Comprobar()
        {
            if (Permisos.Bloquear(this, "Comprobar la direccion del SIIA")) return;

            if (comprobando)
            {
                return;
            }

            string url = Escrita(txtUrl, InicioConSiia.UrlPorOmision);
            string problema = InicioConSiia.ProblemaDeDireccion(url);
            if (problema != null)
            {
                Resultado(false, problema);
                txtUrl.Focus();
                return;
            }

            comprobando = true;
            btnComprobar.Enabled = false;
            UseWaitCursor = true;
            lblPrueba.ForeColor = Gris;
            lblPrueba.Text = "Comprobando...";
            Task.Run(() => InicioConSiia.Comprobar(url)).ContinueWith(tarea =>
            {
                if (IsDisposed)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        comprobando = false;
                        btnComprobar.Enabled = true;
                        UseWaitCursor = false;
                        if (tarea.IsFaulted)
                        {
                            Exception error = tarea.Exception.GetBaseException();
                            Log.Error("Comprobar la direccion del SIIA", error);
                            Resultado(false, AvisoDeFalloAdmin.Texto("No se pudo comprobar", error));
                            return;
                        }

                        Resultado(tarea.Result.Bien, tarea.Result.Texto);
                    }));
                }
                catch (InvalidOperationException)
                {
                    // Se cerro el dialogo mientras se comprobaba.
                }
            });
        }

        private void Resultado(bool bien, string texto)
        {
            lblPrueba.ForeColor = bien ? Color.DarkGreen : Rojo;
            lblPrueba.Text = (bien ? "✔ " : "✖ ") + texto;
        }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Guardar los ajustes")) return;

            lblError.Text = "";
            string url = Escrita(txtUrl, InicioConSiia.UrlPorOmision);
            string problema = InicioConSiia.ProblemaDeDireccion(url);
            if (problema != null)
            {
                lblError.Text = problema;
                txtUrl.Focus();
                return;
            }

            string recuperar = Escrita(txtRecuperar, InicioConSiia.RecuperarPorOmision);
            problema = InicioConSiia.ProblemaDePaginaParaRecuperar(recuperar);
            if (problema != null)
            {
                lblError.Text = problema;
                txtRecuperar.Focus();
                return;
            }

            string pagina = Escrita(txtPagina, InicioConSiia.PaginaPorOmision);
            problema = InicioConSiia.ProblemaDePaginaDelSiia(pagina);
            if (problema != null)
            {
                lblError.Text = problema;
                txtPagina.Focus();
                return;
            }

            bool docentes = chkDocentes.Checked;
            bool estudiantes = chkEstudiantes.Checked;
            bool cambianInterruptores = docentes != docentesAntes || estudiantes != estudiantesAntes;
            if (!cambianInterruptores && url == urlAntes && recuperar == recuperarAntes && pagina == paginaAntes)
            {
                // Sin cambios: nada que guardar.
                DialogResult = DialogResult.Cancel;
                return;
            }

            // Sin db/24 no hay enlaces: con los docentes encendidos, ninguno podria entrar.
            if (docentes && !docentesAntes)
            {
                bool conEnlaces;
                try
                {
                    using (MySql.Data.MySqlClient.MySqlConnection conexion = new Conexion().GetConexion())
                    {
                        conEnlaces = Esquema.EnlacesSiia(conexion);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Ajustes: saber si la base tiene db/24", ex);
                    lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo saber si la base lo admite", ex);
                    return;
                }

                if (!conEnlaces)
                {
                    lblError.Text = "Para que entren los docentes falta aplicar db/24_docentes_siia.sql en la base: ahí se guarda con qué número del "
                        + "SIIA está enlazado cada uno. Pide a quien administra el servidor que la aplique.";
                    chkDocentes.Focus();
                    return;
                }
            }

            Uri uri = new Uri(url, UriKind.Absolute);
            // http://127.0.0.1 sirve para probar con un SIIA simulado en esta PC, pero
            // guardada para toda la escuela cada equipo se llamaria a si mismo.
            if (uri.IsLoopback && url != urlAntes
                && MessageBox.Show(this, "Esa es la dirección de esta PC: en los demás equipos apuntaría a ellos mismos y nadie se podría comprobar "
                    + "con el SIIA (solo entraría quien ya entró antes, con su huella)." + Environment.NewLine + Environment.NewLine
                    + "¿La guardas de todos modos, solo para probar?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                txtUrl.Focus();
                return;
            }

            List<string> partes = new List<string>();
            if (docentes != docentesAntes)
            {
                partes.Add(docentes
                    ? "Los docentes entrarán con su número de empleado, su contraseña y su NIP del SIIA de " + uri.Host + ", una vez que enlaces a cada uno "
                        + "en «Docentes por enlazar»; mientras no lo enlaces, un docente no entra."
                    : "Los docentes volverán a entrar con su contraseña de DigBit (la última que el SIIA les aceptó ya no les sirve sin NIP: si alguno "
                        + "no puede, restablécele la contraseña en Docentes).");
            }

            if (estudiantes != estudiantesAntes)
            {
                partes.Add(estudiantes
                    ? "Los estudiantes entrarán con su matrícula, su contraseña y su NIP del SIIA; su cuenta se crea sola y el kiosco ya no ofrece «Crear cuenta»."
                    : "Los estudiantes volverán a entrar con su contraseña de DigBit y a crear su cuenta en «Crear cuenta».");
            }

            if (url != urlAntes || recuperar != recuperarAntes || pagina != paginaAntes)
            {
                partes.Add("Cambian las direcciones del SIIA (" + uri.Host + ").");
            }

            if (cambianInterruptores)
            {
                partes.Add("Enciéndelo cuando todos los equipos tengan esta versión de DigBit.");
            }

            // Con su contrasena otra vez: quien controle esas direcciones decide quien entra (y a donde va a recuperar).
            if (!DialogoContrasenaAdmin.Confirmar(this, cambianInterruptores ? "¿Cambiar quién entra con el SIIA?" : "¿Cambiar las direcciones del SIIA?",
                    string.Join(" ", partes), "Cambiar"))
            {
                return;
            }

            string urlParaGuardar = url == InicioConSiia.UrlPorOmision ? "" : url;
            string recuperarParaGuardar = recuperar == InicioConSiia.RecuperarPorOmision ? "" : recuperar;
            string paginaParaGuardar = pagina == InicioConSiia.PaginaPorOmision ? "" : pagina;
            try
            {
                // '1' lo enciende; vacio borra el ajuste (apagado, como sin el). Las de
                // por omision no se guardan: si un dia cambian, valen las nuevas.
                AjustesDatos.Guardar(new Dictionary<string, string>
                {
                    { AjustesDatos.SiiaActivo, docentes ? "1" : "" },
                    { AjustesDatos.SiiaEstudiantes, estudiantes ? "1" : "" },
                    { AjustesDatos.SiiaUrl, urlParaGuardar },
                    { AjustesDatos.SiiaRecuperarUrl, recuperarParaGuardar },
                    { AjustesDatos.SiiaPaginaUrl, paginaParaGuardar }
                });
            }
            catch (Exception ex)
            {
                Log.Error("Guardar el inicio de sesion con el SIIA", ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo guardar", ex);
                return;
            }

            Log.Info("Inicio de sesion con el SIIA: docentes " + (docentes ? "encendido" : "apagado") + ", estudiantes " + (estudiantes ? "encendido" : "apagado")
                + " (" + uri.Host + ") por " + (Datos_User.getUser() ?? "-") + ".");
            DocentesGuardado = docentes;
            EstudiantesGuardado = estudiantes;
            UrlGuardada = urlParaGuardar;
            RecuperarGuardada = recuperarParaGuardar;
            PaginaGuardada = paginaParaGuardar;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>
    /// "Aviso de privacidad", desde Ajustes (RNF72): el aviso simplificado del
    /// TESCHI, que "Crear cuenta" muestra con la casilla obligatoria "Lei el aviso
    /// de privacidad", y la direccion del aviso integral, a la que lleva el enlace
    /// "Leer el aviso de privacidad completo" (tambien en la linea de "Olvidaste
    /// tu contrasena?"). A la derecha, como se vera, al momento. Guardar lo deja
    /// en la base (AjustesDatos, en trozos si es largo): vale al momento en todos
    /// los equipos con esta version. Sin texto no se muestra nada: DigBit no
    /// inventa ningun aviso. En el kiosco el enlace no abre el navegador.
    /// </summary>
    internal class DialogoAvisoPrivacidad : Form
    {
        private const int Ancho = 900;
        private const int Alto = 624;
        // Dos columnas: lo que se escribe y como se vera.
        private const int AnchoColumna = 414;
        private const int XVista = 24 + AnchoColumna + 24;
        private const string TextoEnlace = "Leer el aviso de privacidad completo";
        private const string LineaRecuperar = "Tus datos se tratan conforme al aviso de privacidad del TESCHI.";
        private static readonly Color Gris = Color.FromArgb(80, 80, 80);
        private static readonly Color Naranja = Color.FromArgb(190, 85, 0);
        private static readonly System.Globalization.CultureInfo Espanol = System.Globalization.CultureInfo.GetCultureInfo("es-MX");

        private readonly string textoAntes;
        private readonly string urlAntes;
        private readonly TextBox txtAviso;
        private readonly Label lblCuenta;
        private readonly TextBox txtUrl;
        private readonly TextBox txtVista;
        private readonly LinkLabel lnkVista;
        private readonly CheckBox chkVista;
        private readonly Label lblRecuperar;
        private readonly LinkLabel lnkRecuperar;
        private readonly Label lblError;
        private readonly ToolTip ayuda = new ToolTip { AutoPopDelay = 20000 };

        public DialogoAvisoPrivacidad(string texto, string url)
        {
            textoAntes = Limpiar(texto);
            urlAntes = (url ?? "").Trim();
            EstiloHorarios.PrepararDialogo(this, "Aviso de privacidad", new Size(Ancho, Alto));
            TopMost = Kiosco.Activo;
            Controls.Add(EstiloAdmin.TituloDialogo("Aviso de privacidad", Ancho - 48));
            Controls.Add(new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 9.5F),
                ForeColor = Gris,
                Location = new Point(24, 52),
                Size = new Size(Ancho - 48, 56),
                Text = "Lo ve quien crea su cuenta en «Crear cuenta», que no se puede crear sin marcar «Leí el aviso de privacidad», "
                    + "y quien recupera su contraseña. Pega aquí el aviso simplificado y la dirección del aviso integral que publique la "
                    + "Unidad de Transparencia del TESCHI. Sin texto no se muestra ningún aviso. Vale en cuanto guardes, en todos los equipos con esta versión de DigBit."
            });

            // --- Lo que se escribe ------------------------------------------------
            Controls.Add(EstiloHorarios.CrearEtiqueta("Aviso simplificado", new Point(24, 114)));
            txtAviso = new TextBox
            {
                AcceptsReturn = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                Location = new Point(24, 138),
                MaxLength = AjustesDatos.LargoMaximo,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(AnchoColumna, 250),
                Text = textoAntes,
                WordWrap = true
            };
            txtAviso.TextChanged += (s, e) => { lblError.Text = ""; ActualizarVista(); };
            ayuda.SetToolTip(txtAviso, "El texto tal como lo da la institución; Enter hace un salto de línea.");
            Controls.Add(txtAviso);
            lblCuenta = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Gris,
                Location = new Point(24, 391),
                Size = new Size(AnchoColumna, 18),
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(lblCuenta);

            Label etiquetaUrl = EstiloHorarios.CrearEtiqueta("Dirección del aviso integral (opcional)", new Point(24, 416));
            Controls.Add(etiquetaUrl);
            txtUrl = EstiloHorarios.CrearTexto(new Point(24, 440), AnchoColumna);
            txtUrl.MaxLength = 500;
            txtUrl.Text = urlAntes;
            EstiloHorarios.PonerTextoGuia(txtUrl, "https://...");
            txtUrl.TextChanged += (s, e) => { lblError.Text = ""; ActualizarVista(); };
            Controls.Add(txtUrl);
            const string AyudaUrl = "La página del aviso de privacidad integral del TESCHI, con https://.";
            ayuda.SetToolTip(etiquetaUrl, AyudaUrl);
            ayuda.SetToolTip(txtUrl, AyudaUrl);
            // Del alto que pide su texto (en 40 px fijos se cortaba "...la dirección escrita").
            Label notaUrl = new Label
            {
                AutoSize = false,
                Font = new DrawingFont("Century Gothic", 8.5F),
                ForeColor = Gris,
                Location = new Point(24, 472),
                Text = "El enlace la abre en el navegador. En los equipos del laboratorio (kiosco) no se abre: se enseña "
                    + "el aviso dentro de DigBit y la dirección escrita, para consultarla desde otro equipo."
            };
            notaUrl.Size = new Size(AnchoColumna, PiezasConfiguracion.AltoDe(notaUrl.Text, notaUrl.Font, AnchoColumna) + 4);
            Controls.Add(notaUrl);

            // --- Como se vera -------------------------------------------------------
            Controls.Add(EstiloHorarios.CrearEtiqueta("Así se verá en «Crear cuenta»", new Point(XVista, 114)));
            Panel vista = new Panel
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(XVista, 138),
                Size = new Size(AnchoColumna, 250)
            };
            txtVista = new TextBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new DrawingFont("Century Gothic", 9F),
                Location = new Point(10, 8),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Size = new Size(AnchoColumna - 22, 170),
                TabStop = false
            };
            lnkVista = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Location = new Point(8, 186),
                TabStop = false,
                Text = TextoEnlace
            };
            lnkVista.LinkClicked += (s, e) => ProbarEnlace();
            ayuda.SetToolTip(lnkVista, "Pruébalo: abre la dirección del aviso integral.");
            chkVista = new CheckBox
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30),
                Location = new Point(10, 214),
                TabStop = false,
                Text = "Leí el aviso de privacidad"
            };
            ayuda.SetToolTip(chkVista, "Sin marcarla no se puede crear la cuenta.");
            vista.Controls.Add(txtVista);
            vista.Controls.Add(lnkVista);
            vista.Controls.Add(chkVista);
            Controls.Add(vista);

            Label etiquetaRecuperar = EstiloHorarios.CrearEtiqueta("Y en «¿Olvidaste tu contraseña?»", new Point(XVista, 400));
            Controls.Add(etiquetaRecuperar);
            lblRecuperar = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Font = new DrawingFont("Century Gothic", 9F),
                Location = new Point(XVista, 424),
                Size = new Size(AnchoColumna, 20)
            };
            Controls.Add(lblRecuperar);
            lnkRecuperar = new LinkLabel
            {
                AutoSize = true,
                Font = new DrawingFont("Century Gothic", 9F),
                LinkColor = Color.DarkGreen,
                ActiveLinkColor = Color.Green,
                Location = new Point(XVista - 2, 446),
                TabStop = false,
                Text = TextoEnlace
            };
            lnkRecuperar.LinkClicked += (s, e) => ProbarEnlace();
            Controls.Add(lnkRecuperar);

            // El error, nunca encima de la nota; si hace falta, el dialogo crece.
            lblError = EstiloAdmin.LineaError(new Point(24, Math.Max(520, notaUrl.Bottom + 6)), Ancho - 48, 40);
            Controls.Add(lblError);
            ClientSize = new Size(Ancho, Math.Max(Alto, lblError.Bottom + 8 + 56));
            EstiloAdmin.BotonesDialogo(this, "Guardar", Guardar);
            ActiveControl = txtAviso;
            // Al recibir el foco la caja elige todo su texto: una tecla lo borraria entero.
            Shown += (s, e) =>
            {
                txtAviso.SelectionStart = 0;
                txtAviso.SelectionLength = 0;
                txtAviso.ScrollToCaret();
            };
            ActualizarVista();
        }

        /// <summary>El texto que quedo guardado (vacio: sin aviso).</summary>
        public string TextoGuardado { get; private set; }

        /// <summary>La direccion del aviso integral que quedo guardada (vacia: sin enlace).</summary>
        public string UrlGuardada { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ayuda.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Sin caracteres de control (salvo los saltos de linea, que quedan como
        /// "\r\n" para las cajas de texto), sin nada fuera del BMP (la tabla es
        /// utf8 de 3 bytes: un emoji haria fallar el guardado) y sin espacios a los
        /// lados.
        /// </summary>
        internal static string Limpiar(string texto)
        {
            System.Text.StringBuilder limpio = new System.Text.StringBuilder();
            foreach (char c in (texto ?? "").Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                if (c == '\n')
                {
                    limpio.Append("\r\n");
                }
                else if (c == '\t')
                {
                    limpio.Append(' ');
                }
                else if (!char.IsControl(c) && !char.IsSurrogate(c) && c != '�')
                {
                    limpio.Append(c);
                }
            }

            return limpio.ToString().Trim();
        }

        /// <summary>Null si la direccion sirve (absoluta, http o https); si no, que escribir.</summary>
        private static string ProblemaDeDireccion(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || string.IsNullOrEmpty(uri.Host))
            {
                return "Escribe la dirección completa del aviso integral, con https:// (por ejemplo, https://www.teschi.edu.mx/aviso-de-privacidad).";
            }

            return null;
        }

        /// <summary>La vista previa y la cuenta de caracteres, con lo que hay escrito ahora.</summary>
        private void ActualizarVista()
        {
            string texto = Limpiar(txtAviso.Text);
            bool conTexto = texto.Length > 0;
            bool conEnlace = conTexto && txtUrl.Text.Trim().Length > 0;
            txtVista.Text = conTexto ? texto : "(Sin aviso: «Crear cuenta» no muestra nada aquí.)";
            txtVista.ForeColor = conTexto ? Color.FromArgb(40, 40, 40) : Color.Gray;
            lnkVista.Visible = conEnlace;
            chkVista.Visible = conTexto;
            lblRecuperar.Text = conTexto ? LineaRecuperar : "(Sin aviso: no se menciona.)";
            lblRecuperar.ForeColor = conTexto ? Color.FromArgb(40, 40, 40) : Color.Gray;
            lnkRecuperar.Visible = conEnlace;

            int escritos = txtAviso.TextLength;
            bool alTope = escritos >= txtAviso.MaxLength;
            lblCuenta.ForeColor = alTope ? Naranja : Gris;
            lblCuenta.Text = escritos.ToString("N0", Espanol) + " de " + txtAviso.MaxLength.ToString("N0", Espanol) + " caracteres"
                + (alTope ? ": no cabe más" : "");
        }

        /// <summary>
        /// El enlace de la vista previa: abre la direccion en el navegador para
        /// probarla. En el kiosco no (seria una salida al escritorio): la ensena.
        /// </summary>
        private void ProbarEnlace()
        {
            string url = txtUrl.Text.Trim();
            string problema = ProblemaDeDireccion(url);
            if (problema != null)
            {
                lblError.Text = problema;
                txtUrl.Focus();
                return;
            }

            if (Kiosco.Activo)
            {
                MessageBox.Show(this, "En los equipos del laboratorio no se abre el navegador. La dirección del aviso integral es:"
                    + Environment.NewLine + Environment.NewLine + url, "Aviso de privacidad", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Abrir la direccion del aviso de privacidad", ex);
                lblError.Text = "No se pudo abrir el navegador con esa dirección. Cópiala y ábrela a mano para revisarla.";
            }
        }

        private void Guardar()
        {
            if (Permisos.Bloquear(this, "Guardar el aviso de privacidad")) return;

            lblError.Text = "";
            string texto = Limpiar(txtAviso.Text);
            string url = txtUrl.Text.Trim();
            if (texto.Length > AjustesDatos.LargoMaximo)
            {
                lblError.Text = "El aviso es demasiado largo: caben " + AjustesDatos.LargoMaximo.ToString("N0", Espanol)
                    + " caracteres. Acórtalo y vuelve a guardar.";
                txtAviso.Focus();
                return;
            }

            if (url.Length > 0)
            {
                string problema = ProblemaDeDireccion(url);
                if (problema != null)
                {
                    lblError.Text = problema;
                    txtUrl.Focus();
                    return;
                }

                if (texto.Length == 0)
                {
                    lblError.Text = "Sin el aviso simplificado no se muestra nada, tampoco el enlace: pega el aviso o borra la dirección.";
                    txtAviso.Focus();
                    return;
                }
            }

            if (texto == textoAntes && url == urlAntes)
            {
                // Sin cambios: nada que guardar.
                DialogResult = DialogResult.Cancel;
                return;
            }

            if (texto.Length == 0
                && MessageBox.Show(this, "Sin texto, «Crear cuenta» deja de mostrar el aviso de privacidad en todos los equipos. ¿Lo quitas?",
                    "Aviso de privacidad", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                AjustesDatos.Guardar(new Dictionary<string, string>
                {
                    { AjustesDatos.AvisoPrivacidadTexto, texto },
                    { AjustesDatos.AvisoPrivacidadUrl, url }
                });
            }
            catch (Exception ex)
            {
                Log.Error("Guardar el aviso de privacidad", ex);
                lblError.Text = AvisoDeFalloAdmin.Texto("No se pudo guardar", ex);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            Log.Info("Aviso de privacidad " + (texto.Length == 0 ? "quitado" : "guardado (" + texto.Length + " caracteres, "
                + (url.Length > 0 ? "con" : "sin") + " direccion del integral)") + " por " + (Datos_User.getUser() ?? "-") + ".");
            TextoGuardado = texto;
            UrlGuardada = url;
            DialogResult = DialogResult.OK;
        }
    }
}
