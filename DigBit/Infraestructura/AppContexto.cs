using System;
using System.Configuration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigBit.conexion;
using Timer = System.Windows.Forms.Timer;

namespace DigBit.Infraestructura
{
    /// <summary>
    /// Ciclo de vida de la aplicacion, independiente de cualquier formulario.
    /// Antes, Application.Run(new Login()) ataba la vida del proceso a la
    /// ventana de login (que luego se ocultaba y quedaba viva a escondidas), y
    /// "cerrar sesion" abria un Login nuevo encima del anterior sin limpiar nada.
    /// Ahora hay un unico punto para arrancar, cerrar sesion y salir.
    /// </summary>
    internal sealed class AppContexto : ApplicationContext
    {
        private static AppContexto actual;

        public static AppContexto Actual
        {
            get { return actual; }
        }

        public AppContexto()
        {
            actual = this;
        }

        /// <summary>
        /// Comprueba configuracion y servidor antes de mostrar el login. Devuelve
        /// false si no se pudo arrancar y el usuario decidio cerrar (solo posible
        /// fuera del modo kiosco). En kiosco reintenta sola cada 30 segundos.
        /// </summary>
        public bool Iniciar()
        {
            // Si Shell Launcher relanzo DigBit con un equipo ya liberado, se vuelve
            // a ese estado sin pedir login (solo en kiosco). Va ANTES de comprobar
            // el servidor: el traspaso es local y no necesita la base, y si la red
            // fallo justo entonces no hay que tapar el escritorio del alumno con la
            // pantalla de "sin conexion". De paso, la fase 3 se puede probar sin base.
            if (SesionEquipo.Reanudar())
            {
                // Tambien aqui: antes este camino salia sin vigilancia y un DigBit
                // relanzado con el escritorio abierto no volvia a mirar el servidor.
                // (La cola la envia EnviadorBitacoras, que arranca en Program.Main.)
                IniciarVigilanciaConexion();
                return true;
            }

            while (true)
            {
                string titulo = "Sin conexión";
                string problema = ComprobarEntorno();
                if (problema == null)
                {
                    titulo = "Equipo sin laboratorio";
                    problema = ComprobarLaboratorio();
                    if (problema == null)
                    {
                        // Con servidor: se suben las bitacoras pendientes y se
                        // refresca la copia local del horario (fase 4).
                        SinConexion.AlConectar(LaboratorioEquipo.Id);
                    }
                }
                else if (SinConexion.IntentarActivar(ParaElLog(problema)))
                {
                    // Sin servidor pero con copia local valida: se atiende a los
                    // alumnos con ella y se vigila la conexion para volver.
                    problema = null;
                }

                if (problema == null)
                {
                    IniciarVigilanciaConexion();
                    MostrarLogin();
                    return true;
                }

                DialogResult respuesta = PantallaError.Mostrar(
                    titulo,
                    ConAvisoAlEstudiante(problema),
                    "Reintentar",
                    !Kiosco.Activo,
                    Kiosco.Activo ? 30 : 0);

                if (respuesta != DialogResult.Retry)
                {
                    Log.Info("El usuario cerro la aplicacion desde la pantalla de error de arranque.");
                    return false;
                }

                Log.Info("Reintentando la comprobacion de arranque.");
            }
        }

        public void MostrarLogin()
        {
            // Login se registra solo en su constructor (como los demas principales).
            new Login().Show();
        }

        /// <summary>
        /// Suscribe un formulario principal al control de "ultima ventana visible".
        /// Fuera del kiosco, cerrar la ultima termina el proceso; sin esto quedaba
        /// un proceso sin ventanas reteniendo el mutex de instancia unica y DigBit
        /// "ya no abria". Lo llaman los constructores de Login, Alumno_Principal,
        /// Profesor_Principal y PrincipalAdministrador.
        /// </summary>
        public void Registrar(Form formulario)
        {
            formulario.FormClosed -= Formulario_FormClosed;
            formulario.FormClosed += Formulario_FormClosed;
        }

        /// <summary>
        /// Vuelve a un login limpio: olvida el usuario en memoria y cierra todos
        /// los formularios abiertos, incluidos los ocultos con Hide().
        /// </summary>
        public void CerrarSesion()
        {
            Log.Info("Cierre de sesion de '" + (Datos_User.getUser() ?? "-") + "'.");
            Datos_User.Limpiar();
            // Las imagenes y los circulos de las clases del profesor: en un equipo
            // del laboratorio entra otro despues.
            PersonalizacionClases.Olvidar();

            // Primero se abre el login nuevo y despues se cierra lo viejo, para que
            // nunca haya un instante sin ventanas visibles (ver Formulario_FormClosed).
            Form[] abiertos = Application.OpenForms.Cast<Form>().ToArray();
            MostrarLogin();
            foreach (Form formulario in abiertos)
            {
                Kiosco.CerrarAutorizado(formulario);
            }
        }

        private bool cerrandoPorSeguridad;

        /// <summary>
        /// True desde el tercer fallo hasta que la sesion se cierra: entonces ya no
        /// se pide ni se comprueba ninguna contrasena (DialogoContrasenaAdmin).
        /// </summary>
        public bool CerrandoPorSeguridad
        {
            get { return cerrandoPorSeguridad; }
        }

        /// <summary>
        /// Cierra la sesion porque quien esta no es su dueno (tres contrasenas
        /// equivocadas al confirmar un cambio, DialogoContrasenaAdmin) y lo avisa
        /// en el login. Se llama desde dentro de dialogos modales, asi que primero
        /// se cierran todos: cada ShowDialog vuelve como cancelado y el codigo que
        /// lo abrio sigue su camino normal. En cuanto no queda ninguno se cierra la
        /// sesion como con el boton. Cerrar de golpe la ventana duena con sus
        /// dialogos encima dejaria ese codigo trabajando con ventanas destruidas.
        /// </summary>
        public void CerrarSesionPorSeguridad(string aviso)
        {
            if (cerrandoPorSeguridad)
            {
                return;
            }

            cerrandoPorSeguridad = true;
            Log.Aviso("Cierre de sesion por seguridad de '" + (Datos_User.getUser() ?? "-") + "'.");
            SinDialogos(() =>
            {
                try
                {
                    CerrarSesion();
                }
                finally
                {
                    cerrandoPorSeguridad = false;
                }

                MessageBox.Show(Application.OpenForms.OfType<Login>().LastOrDefault(), aviso, "Sesión cerrada",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }

        private bool cerrandoPorInactividad;

        /// <summary>
        /// Cierra la sesion que se quedo sola (Inactividad: nadie toco el teclado
        /// ni el raton y nadie pulso "Seguir aqui"). Como CerrarSesionPorSeguridad,
        /// primero los dialogos modales que hubiera abiertos y despues la sesion,
        /// pero sin aviso en el login: el siguiente no tiene nada que cerrar.
        /// </summary>
        public void CerrarSesionPorInactividad()
        {
            if (cerrandoPorSeguridad || cerrandoPorInactividad)
            {
                return;
            }

            cerrandoPorInactividad = true;
            SinDialogos(() =>
            {
                try
                {
                    CerrarSesion();
                }
                finally
                {
                    cerrandoPorInactividad = false;
                }
            });
        }

        /// <summary>
        /// Cierra los dialogos modales abiertos y, en cuanto no queda ninguno, hace
        /// lo que se pide (cerrar la sesion): cada ShowDialog vuelve antes como
        /// cancelado y el codigo que lo abrio sigue su camino normal.
        /// </summary>
        private static void SinDialogos(Action despues)
        {
            CerrarDialogos();

            // Si algun dialogo no se dejara cerrar, a los 10 s se sigue igual (con
            // un cronometro: el reloj de Windows se puede cambiar).
            System.Diagnostics.Stopwatch reloj = System.Diagnostics.Stopwatch.StartNew();
            Timer espera = new Timer { Interval = 50 };
            espera.Tick += (s, e) =>
            {
                if (Application.OpenForms.Cast<Form>().Any(f => f.Modal) && reloj.ElapsedMilliseconds < 10000)
                {
                    // Lo que se abrio mientras tanto (un Enter que ya estaba en cola
                    // vuelve a abrir el dialogo antes del primer tick) se cierra tambien.
                    CerrarDialogos();
                    return;
                }

                espera.Stop();
                espera.Dispose();
                despues();
            };
            espera.Start();
        }

        /// <summary>Cierra los dialogos modales abiertos, del de arriba al de abajo.</summary>
        private static void CerrarDialogos()
        {
            foreach (Form dialogo in Application.OpenForms.Cast<Form>().Where(f => f.Modal).Reverse().ToArray())
            {
                Kiosco.CerrarAutorizado(dialogo);
            }
        }

        /// <summary>
        /// Termina el proceso. En modo kiosco se ignora: la unica salida es que
        /// Windows cierre la sesion.
        /// </summary>
        public void Salir()
        {
            if (Kiosco.Activo)
            {
                Log.Aviso("Intento de salir de la aplicacion en modo kiosco; ignorado.");
                return;
            }

            Log.Info("Salida de la aplicacion por el usuario.");
            ExitThread();
        }

        private void Formulario_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (Kiosco.Activo)
            {
                return;
            }

            // Fuera del kiosco, cerrar la ultima ventana visible termina el proceso
            // (comportamiento normal de escritorio, util al desarrollar).
            bool quedaAlgoVisible = Application.OpenForms.Cast<Form>().Any(f => f != sender && f.Visible);
            if (!quedaAlgoVisible)
            {
                Log.Info("Se cerro la ultima ventana; fin de la aplicacion.");
                ExitThread();
            }
        }

        private Timer vigilanciaConexion;
        private int minutosDesdeRefresco;
        private int sondeando;

        /// <summary>
        /// Fase 4: cada minuto. Sin conexion, prueba el servidor y, si volvio,
        /// sale del modo sin conexion y refresca la copia. Con conexion, cada 10
        /// minutos refresca la copia. La consulta va en segundo plano (antes, sin
        /// red, congelaba la pantalla hasta 5 s cada minuto) y lo que encuentra se
        /// aplica en el hilo de la interfaz. La cola no es cosa suya: la envia
        /// EnviadorBitacoras, aqui solo se le avisa de que volvio el servidor.
        /// </summary>
        private void IniciarVigilanciaConexion()
        {
            if (vigilanciaConexion != null)
            {
                return;
            }

            vigilanciaConexion = new Timer { Interval = 60000 };
            vigilanciaConexion.Tick += (s, e) => VigilarConexion();
            vigilanciaConexion.Start();
        }

        /// <summary>Lo que averiguo un sondeo en segundo plano.</summary>
        private sealed class Sondeo
        {
            public bool EraSinConexion;
            public bool Respondio;
            public int LaboratorioId;
            public string Laboratorio;
            public string Problema;
            public CacheHorario Copia;
            public Exception Error;
        }

        private void VigilarConexion()
        {
            bool sinConexion = SinConexion.Activo;
            bool refrescar = false;
            if (!sinConexion)
            {
                minutosDesdeRefresco++;
                refrescar = minutosDesdeRefresco >= 10 && LaboratorioEquipo.Resuelto;
            }

            if (!sinConexion && !refrescar)
            {
                return;
            }

            if (Interlocked.Exchange(ref sondeando, 1) == 1)
            {
                return;
            }

            if (refrescar)
            {
                minutosDesdeRefresco = 0;
            }

            int laboratorio = LaboratorioEquipo.Id;
            Task.Run(() => Sondear(sinConexion, laboratorio))
                .ContinueWith(tarea =>
                {
                    Interlocked.Exchange(ref sondeando, 0);
                    AplicarSondeo(tarea.Result);
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>En segundo plano. Nunca lanza: el error va en el resultado.</summary>
        private static Sondeo Sondear(bool sinConexion, int laboratorio)
        {
            Sondeo sondeo = new Sondeo { EraSinConexion = sinConexion };
            try
            {
                if (sinConexion)
                {
                    Conexion.Probar(false);
                    sondeo.Respondio = true;

                    int id;
                    string nombre;
                    sondeo.Problema = LaboratorioEquipo.Buscar(out id, out nombre);
                    sondeo.LaboratorioId = id;
                    sondeo.Laboratorio = nombre;
                    laboratorio = id > 0 ? id : laboratorio;
                }

                if (laboratorio > 0)
                {
                    sondeo.Copia = CacheHorario.Actualizar(laboratorio);
                }
            }
            catch (Exception ex)
            {
                sondeo.Error = ex;
            }

            return sondeo;
        }

        /// <summary>En el hilo de la interfaz: aplica lo que encontro el sondeo.</summary>
        private void AplicarSondeo(Sondeo sondeo)
        {
            if (sondeo.EraSinConexion)
            {
                // Sigue sin servidor (o se volvio a ir a media consulta): se queda
                // como esta y se reintenta en el siguiente minuto, sin ensuciar el log.
                if (!sondeo.Respondio || (sondeo.Error != null && sondeo.LaboratorioId == 0))
                {
                    return;
                }

                if (sondeo.LaboratorioId > 0)
                {
                    LaboratorioEquipo.Fijar(sondeo.LaboratorioId, sondeo.Laboratorio);
                }
                else if (sondeo.Problema != null)
                {
                    // Problema es el texto para la pantalla; el motivo tecnico ya lo
                    // apunto LaboratorioEquipo.Buscar.
                    Log.Aviso("Al recuperar la conexion no se pudo resolver el laboratorio de este equipo; ver el aviso del laboratorio, mas arriba.");
                }

                SinConexion.Desactivar();
                minutosDesdeRefresco = 0;
                EnviadorBitacoras.Despertar();
            }

            if (sondeo.Error != null)
            {
                if (!sondeo.EraSinConexion)
                {
                    Log.Aviso("Vigilancia de conexion: " + sondeo.Error.Message);
                }

                return;
            }

            SinConexion.UsarCopia(sondeo.Copia);
        }

        /// <summary>
        /// Fase 6: cada equipo tiene que saber en que laboratorio esta, porque un
        /// codigo solo vale en el suyo. En kiosco es bloqueante (el alumno no
        /// podria hacer nada util); en escritorio solo se deja en el log y la
        /// pantalla de codigo se lo dira al alumno.
        /// </summary>
        private static string ComprobarLaboratorio()
        {
            try
            {
                string problema = LaboratorioEquipo.Resolver();
                if (problema == null)
                {
                    return null;
                }

                if (Kiosco.Activo)
                {
                    return problema;
                }

                // problema es el texto para la pantalla; el motivo tecnico ya lo
                // apunto LaboratorioEquipo.Buscar.
                Log.Aviso("Laboratorio del equipo sin resolver (modo escritorio); ver el aviso del laboratorio, mas arriba.");
                return null;
            }
            catch (Exception ex)
            {
                // La causa y que hacer; el detalle tecnico, al registro (la pantalla dice donde esta).
                Log.Error("Resolver el laboratorio del equipo", ex);
                return Kiosco.Activo
                    ? MensajeDeFallo.Explicar("No se pudo comprobar el laboratorio de este equipo", ex, MensajeDeFallo.AvisaAlLaboratorio)
                    : null;
            }
        }

        /// <summary>
        /// Lo que se le dice al estudiante cuando este equipo del laboratorio no le
        /// sirve para registrar su bitacora (las pantallas de arranque y el login sin
        /// copia del horario): que le avise a su docente, que solo puede darle la
        /// asistencia mientras dura la clase ("ahora"). Pide, no promete: sin la db/22
        /// el docente aun no la puede dar en DigBit.
        /// </summary>
        internal const string AvisoAlEstudiante = "Si vienes a una clase, avísale ahora a tu docente para que te dé la asistencia.";

        // La caja del mensaje de PantallaError (lblMensaje): 580 x 170, Century Gothic 11.
        private const int AnchoMensajeDeError = 580;
        private const int AltoMensajeDeError = 170;

        /// <summary>
        /// En el kiosco, el problema con el aviso al estudiante debajo: cualquier
        /// pantalla de arranque deja este equipo sin servir para registrar (sin
        /// servidor, sin connections.config o danado, con la cuenta de la base
        /// rechazada o sin laboratorio), y quien la ve suele ser un estudiante que
        /// viene a una clase. Solo en pantalla: el log y el modo sin conexion llevan
        /// el problema solo. En el equipo del docente o de la administracion no va.
        /// No se repite si ya esta. Si no cabe en la caja, va pegado (un salto en vez
        /// de dos) y, si ni asi, no va: mejor el problema entero que cortado.
        /// </summary>
        private static string ConAvisoAlEstudiante(string problema)
        {
            if (!Kiosco.Activo || problema.Contains(AvisoAlEstudiante))
            {
                return problema;
            }

            foreach (string separacion in new[] { Environment.NewLine + Environment.NewLine, Environment.NewLine })
            {
                string texto = problema + separacion + AvisoAlEstudiante;
                if (CabeEnLaPantallaDeError(texto))
                {
                    return texto;
                }
            }

            Log.Aviso("El aviso al estudiante no cabe en la pantalla de arranque con este problema; se muestra sin el.");
            return problema;
        }

        /// <summary>Si el texto cabe en la caja del mensaje de PantallaError, medido como lo pinta su Label.</summary>
        private static bool CabeEnLaPantallaDeError(string texto)
        {
            using (System.Drawing.Font fuente = new System.Drawing.Font("Century Gothic", 11F))
            {
                System.Drawing.Size medida = TextRenderer.MeasureText(texto, fuente,
                    new System.Drawing.Size(AnchoMensajeDeError, int.MaxValue), TextFormatFlags.WordBreak);
                return medida.Height <= AltoMensajeDeError;
            }
        }

        private static string ComprobarEntorno()
        {
            try
            {
                Conexion.Probar();
                Log.Info("Conexion con el servidor comprobada.");
                return null;
            }
            // En pantalla, la causa y que hacer; el texto del driver solo va al
            // registro (PantallaError dice en que archivo).
            catch (ConfigurationErrorsException ex)
            {
                Log.Error("Configuracion de conexion", ex);
                return "No se pudo leer la configuración de conexión a la base de datos: el archivo connections.config "
                       + "de DigBit falta o está dañado." +
                       Environment.NewLine + Environment.NewLine +
                       "Vuelve a instalar este equipo con DigBit-Instalador.exe, que lo escribe; si sigue, avisa a la persona responsable del laboratorio.";
            }
            catch (Exception ex)
            {
                Log.Error("Prueba de conexion con el servidor", ex);

                // El servidor contesto y rechazo el usuario o la contrasena: no es
                // un problema de red, y decir "comprueba la red" mandaba a buscar
                // donde no era (paso al instalar con la contrasena en mayusculas).
                if (AccesoDenegado(ex))
                {
                    return "El servidor de bitácoras rechazó el usuario o la contraseña de la base." +
                           Environment.NewLine + Environment.NewLine +
                           "La contraseña distingue mayúsculas y minúsculas. Vuelve a instalar este equipo con " +
                           "DigBit-Instalador.exe y escríbela bien, o corrige C:\\DigBit\\connections.config.";
                }

                return "No hay conexión con el servidor de bitácoras." +
                       Environment.NewLine + Environment.NewLine +
                       "Comprueba que el equipo tiene red y que el servidor está encendido; si sigue, avisa a la persona responsable del laboratorio.";
            }
        }

        /// <summary>
        /// El texto sin acentos ni signos de apertura, para el log (que va en
        /// ASCII): el motivo del modo sin conexion se apunta alli.
        /// </summary>
        private static string ParaElLog(string texto)
        {
            System.Text.StringBuilder limpio = new System.Text.StringBuilder(texto.Length);
            foreach (char c in texto.Normalize(System.Text.NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                    && c != '¿' && c != '¡')
                {
                    limpio.Append(c);
                }
            }

            return limpio.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        /// <summary>Error 1045 de MySQL (Access denied), en la excepcion o en alguna de las internas.</summary>
        private static bool AccesoDenegado(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                MySql.Data.MySqlClient.MySqlException mysql = e as MySql.Data.MySqlClient.MySqlException;
                if ((mysql != null && mysql.Number == 1045) || e.Message.IndexOf("Access denied", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
