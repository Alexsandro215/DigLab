using System;
using System.Windows.Forms;
using DigBit.conexion;
using MySql.Data.MySqlClient;

namespace DigBit.Infraestructura
{
    /// <summary>Los valores de usuarios.fk_tipo_usuario.</summary>
    internal static class TiposUsuario
    {
        public const int Alumno = 1;
        public const int Profesor = 2;
        public const int Administrador = 3;
        /// <summary>Encargado de laboratorio: el panel del administrador en solo consulta (db/18).</summary>
        public const int Encargado = 4;
    }

    /// <summary>
    /// Lo que puede cambiar quien tiene la sesion abierta, en un solo sitio. Solo
    /// consulta: el encargado de laboratorio (tipo 4). Funciona en tres capas:
    /// las pantallas no ensenan lo que escribe, cada accion de pantalla que
    /// escribe empieza con Bloquear y cada metodo de datos que escribe con
    /// Exigir. Debajo de todo, la base: la sesion del encargado va con la cuenta
    /// de MySQL de solo lectura (CuentaDeConsulta), que rechaza cualquier cambio.
    /// Ninguna pantalla pregunta TipoUsuario == 4: todas preguntan aqui.
    /// </summary>
    internal static class Permisos
    {
        public const string Explicacion = "Solo la administración de DigBit puede cambiar esto.";

        public static bool EsEncargado
        {
            get { return Datos_User.TipoUsuario == TiposUsuario.Encargado; }
        }

        /// <summary>No se puede escribir nada: lo miran las pantallas y la capa de datos.</summary>
        public static bool SoloConsulta
        {
            get { return EsEncargado; }
        }

        public static bool EsDocente
        {
            get { return Datos_User.TipoUsuario == TiposUsuario.Profesor; }
        }

        /// <summary>
        /// Los codigos generados cada semana: solo el administrador (regla de
        /// db/11). Con "Los docentes no ven los codigos" (CodigosSoloResponsables),
        /// tambien el encargado de laboratorio, y el profesor ya no.
        /// </summary>
        public static bool VeCodigosGenerados
        {
            get { return VeCodigos; }
        }

        /// <summary>
        /// El codigo unico de cada profesor y el de cada sesion: es lo que teclean
        /// los alumnos para que su bitacora cuente, y los de servicio social son
        /// alumnos. El administrador y el profesor (el suyo); con "Los docentes no
        /// ven los codigos" (CodigosSoloResponsables), el administrador y el
        /// encargado de laboratorio, y el profesor no.
        /// </summary>
        public static bool VeCodigosDeProfesor
        {
            get { return VeCodigos; }
        }

        /// <summary>
        /// El profesor con "Los docentes no ven los codigos": donde iba su codigo,
        /// que se lo pida a la persona responsable del laboratorio
        /// (CodigosSoloResponsables.PideElCodigo).
        /// </summary>
        public static bool DocenteSinCodigos
        {
            get { return EsDocente && CodigosSoloResponsables.Activa; }
        }

        private static bool VeCodigos
        {
            get { return CodigosSoloResponsables.Activa ? !EsDocente : !EsEncargado; }
        }

        /// <summary>Capa de datos: primera linea de cada metodo que escribe.</summary>
        public static void Exigir(string accion)
        {
            if (!SoloConsulta)
            {
                return;
            }

            Anotar(accion);
            throw new AccionNoPermitida(Explicacion);
        }

        /// <summary>Pantallas: if (Permisos.Bloquear(this, "Editar una franja")) return;</summary>
        public static bool Bloquear(IWin32Window dueno, string accion)
        {
            if (!SoloConsulta)
            {
                return false;
            }

            Anotar(accion);
            MessageBox.Show(dueno, Explicacion, "Solo consulta", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        /// <summary>En solo consulta, oculta controles que escriben.</summary>
        public static void Ocultar(params Control[] controles)
        {
            if (!SoloConsulta)
            {
                return;
            }

            foreach (Control control in controles)
            {
                if (control != null)
                {
                    control.Visible = false;
                }
            }
        }

        /// <summary>Lo mismo para opciones de menu (se ocultan, nunca se quitan: el codigo las usa por posicion).</summary>
        public static void Ocultar(params ToolStripItem[] opciones)
        {
            if (!SoloConsulta)
            {
                return;
            }

            foreach (ToolStripItem opcion in opciones)
            {
                if (opcion != null)
                {
                    opcion.Visible = false;
                }
            }
        }

        /// <summary>
        /// A la vista pero sin poder usarse, con la explicacion al pasar el raton y
        /// para los lectores de pantalla. Un control deshabilitado no recibe el
        /// raton (Windows se lo da a su contenedor) y su ToolTip no sale: la ayuda
        /// la ensena el contenedor. Si la pantalla lo vuelve a habilitar, se apaga solo.
        /// </summary>
        public static void Desactivar(ToolTip ayuda, Control control, string explicacion)
        {
            if (!SoloConsulta || control == null)
            {
                return;
            }

            control.Enabled = false;
            control.AccessibleDescription = explicacion;
            control.EnabledChanged += (s, e) =>
            {
                if (control.Enabled)
                {
                    control.Enabled = false;
                }
            };

            if (control.Parent != null)
            {
                AyudaDesdeElContenedor(ayuda, control, explicacion);
                return;
            }

            EventHandler alPonerse = null;
            alPonerse = (s, e) =>
            {
                control.ParentChanged -= alPonerse;
                if (control.Parent != null)
                {
                    AyudaDesdeElContenedor(ayuda, control, explicacion);
                }
            };
            control.ParentChanged += alPonerse;
        }

        private static void AyudaDesdeElContenedor(ToolTip ayuda, Control control, string texto)
        {
            Control padre = control.Parent;
            bool ensenando = false;
            padre.MouseMove += (s, e) =>
            {
                bool encima = control.Visible && control.Bounds.Contains(e.Location);
                if (encima && !ensenando)
                {
                    ayuda.Show(texto, padre, e.X + 12, e.Y + 20, 5000);
                    ensenando = true;
                }
                else if (!encima && ensenando)
                {
                    ayuda.Hide(padre);
                    ensenando = false;
                }
            };
            padre.MouseLeave += (s, e) =>
            {
                if (ensenando)
                {
                    ayuda.Hide(padre);
                    ensenando = false;
                }
            };
        }

        private static void Anotar(string accion)
        {
            Log.Aviso("Solo consulta: se bloqueo '" + accion + "' de " + (Datos_User.getUser() ?? "-")
                + " (tipo " + Datos_User.TipoUsuario + ").");
        }
    }

    /// <summary>Lo que lanza Permisos.Exigir. Es InvalidOperationException: las pantallas ya ensenan su mensaje.</summary>
    internal class AccionNoPermitida : InvalidOperationException
    {
        public AccionNoPermitida(string mensaje) : base(mensaje)
        {
        }
    }

    /// <summary>
    /// Los avisos de error de las pantallas de la administracion (RNF12): que no
    /// se pudo hacer, por que y que hacer, en segunda persona. El detalle tecnico
    /// (el mensaje de la excepcion y su pila) va solo al log. Un ErrorDeDatos o
    /// un AccionNoPermitida ya traen su texto para quien lo lee y salen tal cual.
    /// La red solo se culpa cuando no se llego al servidor: un permiso o una
    /// migracion que falta no se arreglan con el cable.
    /// </summary>
    internal static class AvisoDeFalloAdmin
    {
        private enum Clase
        {
            Explicado,
            Archivo,
            /// <summary>Un archivo temporal de la vista previa (MostrarVistaPrevia), no uno que eligio la persona.</summary>
            VistaPrevia,
            SinPermiso,
            CuentaRechazada,
            FaltaMigracion,
            Ocupada,
            SoloLectura,
            SinConexion,
            Servidor,
            Inesperado
        }

        /// <summary>
        /// "No se pudo guardar: no hubo conexion con el servidor. Revisa la red..."
        /// queNoSePudo va con mayuscula y sin punto ("No se pudo guardar").
        /// </summary>
        public static string Texto(string queNoSePudo, Exception ex)
        {
            string explicado;
            Clase clase = Clasificar(ex, out explicado);
            if (clase == Clase.Explicado)
            {
                return explicado;
            }

            return queNoSePudo + ": " + Causa(clase) + ". " + Accion(clase);
        }

        /// <summary>Solo la causa, en minusculas y sin punto: para una lista ("PDF de tal: no hubo conexion...").</summary>
        public static string Causa(Exception ex)
        {
            string explicado;
            Clase clase = Clasificar(ex, out explicado);
            return clase == Clase.Explicado ? explicado.TrimEnd('.') : Causa(clase);
        }

        /// <summary>Solo lo que hay que hacer, para despues de una lista de causas.</summary>
        public static string Accion(Exception ex)
        {
            string explicado;
            Clase clase = Clasificar(ex, out explicado);
            return clase == Clase.Explicado ? "" : Accion(clase);
        }

        /// <summary>
        /// Al log (contexto en ASCII, con la excepcion entera) y en un MessageBox
        /// con la causa y que hacer. Lo que ya viene explicado sale como aviso.
        /// </summary>
        public static void Mostrar(IWin32Window dueno, string contexto, Exception ex, string queNoSePudo, string titulo)
        {
            string explicado;
            bool yaExplicado = Clasificar(ex, out explicado) == Clase.Explicado;
            if (yaExplicado)
            {
                Log.Aviso(contexto + ": " + explicado);
            }
            else
            {
                Log.Error(contexto, ex);
            }

            MessageBox.Show(dueno, Texto(queNoSePudo, ex), titulo, MessageBoxButtons.OK,
                yaExplicado ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
        }

        /// <summary>
        /// Como Mostrar, para ensenar un PDF (VisorPdf): ahi no se guarda ningun
        /// archivo que haya elegido la persona, sino uno temporal de Windows, y
        /// "cierralo o elige otra carpeta" no le sirve de nada. Un fallo de archivo
        /// dice que no se pudo preparar la vista previa; los demas, como Mostrar.
        /// </summary>
        public static void MostrarVistaPrevia(IWin32Window dueno, string contexto, Exception ex, string queNoSePudo, string titulo)
        {
            string explicado;
            if (Clasificar(ex, out explicado) != Clase.Archivo)
            {
                Mostrar(dueno, contexto, ex, queNoSePudo, titulo);
                return;
            }

            Log.Error(contexto, ex);
            MessageBox.Show(dueno, queNoSePudo + ": " + Causa(Clase.VistaPrevia) + ". " + Accion(Clase.VistaPrevia), titulo,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static Clase Clasificar(Exception ex, out string explicado)
        {
            explicado = null;
            if (ex == null)
            {
                return Clase.Inesperado;
            }

            // Lo que llega envuelto (Task, reflexion) se mira por dentro.
            while ((ex is AggregateException || ex is System.Reflection.TargetInvocationException) && ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            if (ex is ErrorDeDatos || ex is AccionNoPermitida)
            {
                explicado = ex.Message;
                return Clase.Explicado;
            }

            MySqlException mysql = null;
            for (Exception e = ex; e != null && mysql == null; e = e.InnerException)
            {
                mysql = e as MySqlException;
            }

            if (mysql == null && (ex is System.IO.IOException || ex is UnauthorizedAccessException))
            {
                // Sin la base de por medio: un archivo que Windows no dejo escribir.
                return Clase.Archivo;
            }

            if (mysql != null)
            {
                switch (mysql.Number)
                {
                    case 1142:
                    case 1143:
                        return Clase.SinPermiso;
                    case 1044:
                    case 1045:
                        return Clase.CuentaRechazada;
                    case 1054:
                    case 1146:
                        return Clase.FaltaMigracion;
                    case 1205:
                    case 1213:
                        return Clase.Ocupada;
                    case 1290:
                        return Clase.SoloLectura;
                }
            }

            if (ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion)
            {
                return Clase.SinConexion;
            }

            return mysql != null ? Clase.Servidor : Clase.Inesperado;
        }

        private static string Causa(Clase clase)
        {
            switch (clase)
            {
                case Clase.Archivo:
                    return "Windows no dejó guardar el archivo (la carpeta no lo permite, el disco está lleno o el archivo está abierto en otro programa, como Excel)";
                case Clase.VistaPrevia:
                    return "no se pudo preparar la vista previa en este equipo (Windows no dejó usar su carpeta de archivos temporales, o el disco está lleno)";
                case Clase.SinPermiso:
                    return "la cuenta de la base de datos de este equipo no tiene permiso para hacerlo";
                case Clase.CuentaRechazada:
                    return "el servidor rechazó la cuenta de la base de datos de este equipo";
                case Clase.FaltaMigracion:
                    return "a la base de datos le falta una actualización que esta versión de DigBit necesita";
                case Clase.Ocupada:
                    return "la base de datos estaba ocupada con otro cambio al mismo tiempo";
                case Clase.SoloLectura:
                    return "en este momento el servidor de la base de datos solo deja leer";
                case Clase.SinConexion:
                    return "no hubo conexión con el servidor";
                case Clase.Servidor:
                    return "el servidor de la base de datos no lo aceptó";
                default:
                    return "ocurrió un error inesperado en DigBit";
            }
        }

        private static string Accion(Clase clase)
        {
            switch (clase)
            {
                case Clase.Archivo:
                    return "Ciérralo o elige otra carpeta e inténtalo de nuevo.";
                case Clase.VistaPrevia:
                    return "Cierra otros programas o reinicia DigBit e inténtalo de nuevo; si sigue fallando, "
                        + (Permisos.SoloConsulta ? "avisa a la administración de DigBit." : "revisa que el disco de este equipo tenga espacio libre.");
                case Clase.Ocupada:
                    return "Inténtalo de nuevo en un momento.";
                case Clase.SinConexion:
                    return "Revisa la red de este equipo e inténtalo de nuevo; si sigue fallando, "
                        + (Permisos.SoloConsulta ? "avisa a la administración de DigBit." : "avisa a quien administra el servidor.");
            }

            // La persona responsable de laboratorio no administra nada: avisa.
            if (Permisos.SoloConsulta)
            {
                return clase == Clase.Servidor || clase == Clase.Inesperado
                    ? "Inténtalo de nuevo; si sigue fallando, avisa a la administración de DigBit."
                    : "Avisa a la administración de DigBit.";
            }

            switch (clase)
            {
                case Clase.SinPermiso:
                    return "Hazlo desde el equipo de la administración; si ya estás en él, pide a quien administra el servidor que revise sus permisos (db/07).";
                case Clase.CuentaRechazada:
                    return "Pide a quien administra el servidor que la revise, o vuelve a instalar este equipo con DigBit-Instalador.exe.";
                case Clase.FaltaMigracion:
                    return "Pide a quien administra el servidor que aplique las migraciones de la carpeta db de DigBit.";
                case Clase.SoloLectura:
                    return "Inténtalo más tarde; si sigue así, avisa a quien administra el servidor.";
                default:
                    return "Inténtalo de nuevo; si sigue fallando, revisa el registro de diagnóstico de este equipo, donde quedó el detalle.";
            }
        }
    }
}
