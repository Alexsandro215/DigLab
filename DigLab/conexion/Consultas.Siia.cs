using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Lo que se leyo de la cuenta de DigLab con el numero que se escribio.</summary>
    internal sealed class CuentaParaEntrar
    {
        public bool Existe;
        /// <summary>TiposUsuario; 0 si no existe.</summary>
        public int Tipo;
        public string Almacenada;
        public bool Activo = true;
        /// <summary>Docente: la administracion ya lo enlazo con el SIIA (db/24).</summary>
        public bool Enlazado;

        /// <summary>
        /// Si su huella sirve para entrar cuando el SIIA no responde: un estudiante
        /// con huella, o un docente enlazado con huella. Sin huella (SinHuella o
        /// vacia) no hay acceso sin el SIIA.
        /// </summary>
        public bool ConHuella
        {
            get
            {
                return Existe && (Tipo == TiposUsuario.Alumno || (Tipo == TiposUsuario.Profesor && Enlazado))
                    && !string.IsNullOrEmpty(Almacenada) && Almacenada != EnlacesSiia.SinHuella;
            }
        }
    }

    /// <summary>
    /// El inicio de sesion con el SIIA del TESCHI (InicioConSiia), para los
    /// docentes y los estudiantes, cada uno con su interruptor en Ajustes. La
    /// validacion es la misma para los dos, en dos pasos como el propio SIIA:
    /// numero y contrasena; luego el NIP, en la misma ventana del inicio de
    /// sesion (pedirNip). Lo que cambia es lo que pasa despues:
    ///   - Docente: entra si la administracion ya enlazo ese numero con su
    ///     cuenta (EnlacesSiia); si no, queda su solicitud y espera
    ///     (VentanaEnlacePendiente). No se crea ni se enlaza nada solo.
    ///   - Estudiante ('E'): entra con su cuenta de DigLab de esa matricula, o se
    ///     le crea sola con su nombre y, si casa sin duda, su carrera. Si su cuenta
    ///     no tiene carrera, la toma igual al entrar (CompletarCarrera).
    /// Cada vez que el SIIA acepta, se guarda (o actualiza) la huella PBKDF2 de la
    /// contrasena y el NIP juntos (InicioConSiia.Combinar, Contrasenas.Hash). Si
    /// el SIIA no responde (sin red, tiempo agotado, 5xx, respuesta ilegible),
    /// los dos pasos se comprueban contra esa huella; sin huella no hay acceso.
    /// Si el SIIA rechaza (401), dice que la cuenta esta inactiva (403), bloquea
    /// por intentos (429) o que aun no tiene NIP, no entra y la huella no se usa.
    /// Una cuenta dada de baja no entra aunque el SIIA acepte. La administracion
    /// y la persona responsable del laboratorio no pasan por aqui. Ni la
    /// contrasena, ni el NIP, ni el tempToken, ni la huella van al log.
    /// </summary>
    internal partial class Consultas
    {
        private const string TituloInicio = "Iniciar sesión";
        private const string RevisaComoSeEscriben = "Revisa que estén bien escritos: la contraseña distingue mayúsculas y minúsculas. ";
        private const string PideAsistencia = "No se pudo comprobar tu cuenta con el SIIA. Pide a tu profesor que te dé asistencia.";
        private const int AltaClaveDuplicada = 1062;
        private const int AltaSinPermisoTabla = 1142;
        private const int AltaSinPermisoColumna = 1143;
        // usuarios.nombre y apellidos: VARCHAR(80) en db/01 (100 en la base de la escuela).
        private const int LargoMaximoNombre = 80;

        /// <summary>
        /// Mientras se pide el NIP: el nombre que dio el SIIA en el paso 1 (ya con
        /// la contrasena comprobada), para ensenarlo encima del NIP ("Lucía Pérez
        /// Ruiz"). Null fuera de ese paso y sin el SIIA (con la huella, la
        /// contrasena aun no esta comprobada y el nombre delataria la cuenta).
        /// </summary>
        public string NombreEnElPasoNip { get; private set; }

        /// <summary>Lo que hace con quien entra, segun su cuenta de DigLab y su tipo en el SIIA.</summary>
        private enum PerfilSiia
        {
            Docente,
            Estudiante
        }

        /// <summary>
        /// Quien entra con el SIIA (Ajustes). Sin db/24 no hay enlaces: los docentes
        /// entran como antes (y se anota); los estudiantes no la necesitan. Si no se
        /// puede leer, todo como antes (apagado).
        /// </summary>
        private static ConfiguracionSiia LeerSiiaParaEntrar(MySqlConnection conexion)
        {
            try
            {
                ConfiguracionSiia siia = InicioConSiia.Leer(conexion);
                if (siia.Docentes && !Esquema.EnlacesSiia(conexion))
                {
                    Log.Aviso("SIIA: los docentes estan encendidos en Ajustes, pero la base no tiene db/24_docentes_siia.sql (los enlaces): entran como antes.");
                    return siia.Estudiantes ? new ConfiguracionSiia(false, true, siia.Url, siia.RecuperarUrl, siia.PaginaUrl) : ConfiguracionSiia.Apagada;
                }

                return siia;
            }
            catch (Exception ex)
            {
                Log.Error("SIIA: no se pudo leer en Ajustes quien entra con el SIIA; se entra como antes", ex);
                return ConfiguracionSiia.Apagada;
            }
        }

        /// <summary>
        /// Los dos pasos con el SIIA y lo que pasa despues. Devuelve el tipo con el
        /// que entra (docente o estudiante), o 0 si no entra (con el aviso ya
        /// enseñado). pedirNip ensena el paso del NIP en la ventana del inicio de
        /// sesion (con un aviso, o null) y devuelve el NIP, o null si la persona
        /// vuelve atras.
        /// </summary>
        private async Task<int> EntrarConSiiaAsync(string numero, string contrasena, ConfiguracionSiia siia, CuentaParaEntrar cuenta,
            Func<string, Task<string>> pedirNip)
        {
            RespuestaSiia paso1 = await InicioConSiia.ConsultarAsync(siia.Url, numero, contrasena);
            switch (paso1.Resultado)
            {
                case ResultadoSiia.Aceptado:
                    break;

                case ResultadoSiia.Rechazado:
                    // El mismo aviso exista o no la cuenta en DigLab: no delata ningun numero.
                    Log.Info("SIIA: '" + numero + "' rechazado en el paso 1 (" + paso1.Causa + ").");
                    Avisar("Tu número o tu contraseña no son correctos." + Environment.NewLine + Environment.NewLine + RevisaComoSeEscriben
                        + "Son los mismos con los que entras al SIIA del TESCHI; si no recuerdas tu contraseña, usa «¿Olvidaste tu contraseña?».",
                        TituloInicio, MessageBoxIcon.Error);
                    return 0;

                case ResultadoSiia.SinNip:
                    Log.Info("SIIA: '" + numero + "' aun no tiene NIP del SIIA (" + paso1.Causa + "): no entra.");
                    VentanaPaginaSiia.CrearNip(null, siia);
                    return 0;

                case ResultadoSiia.SinRespuesta:
                    return await EntrarSinSiiaAsync(numero, contrasena, siia, cuenta, pedirNip, paso1.Causa);

                default:
                    AvisarRechazo(numero, paso1, "paso 1");
                    return 0;
            }

            DatosSiia datos = paso1.Datos;
            PerfilSiia? perfil = DecidirPerfil(numero, siia, cuenta, datos);
            if (!perfil.HasValue)
            {
                return 0;
            }

            string nip = "";
            if (paso1.NipRequerido)
            {
                NombreEnElPasoNip = NombreParaMostrar(datos);
                PasoDelNip resultado;
                try
                {
                    resultado = await PedirYVerificarNipAsync(numero, siia, paso1, pedirNip);
                }
                finally
                {
                    NombreEnElPasoNip = null;
                }

                // El tempToken ya no hace falta: fuera de la memoria.
                paso1 = null;
                if (resultado.SinSiia)
                {
                    return EntrarConHuella(numero, contrasena, resultado.Nip, siia, cuenta, resultado.Causa);
                }

                if (resultado.Nip == null)
                {
                    return 0;
                }

                nip = resultado.Nip;
            }

            // Lo unico que se guarda de la contrasena y el NIP.
            string secreto = InicioConSiia.Combinar(contrasena, nip);
            if (perfil.Value == PerfilSiia.Docente)
            {
                return cuenta.Existe && cuenta.Enlazado
                    ? EntrarCuentaAceptada(numero, secreto, cuenta)
                    : EsperarEnlace(numero, Contrasenas.Hash(secreto), datos);
            }

            if (!cuenta.Existe)
            {
                return CrearEstudiante(numero, Contrasenas.Hash(secreto), datos);
            }

            int entra = EntrarCuentaAceptada(numero, secreto, cuenta);
            if (entra == TiposUsuario.Alumno)
            {
                CompletarCarrera(numero, datos);
            }

            return entra;
        }

        /// <summary>
        /// Docente o estudiante, segun su cuenta de DigLab (si la hay) y su tipo en
        /// el SIIA ('E' estudiante; 'P', 'A' u otro, para las solicitudes de
        /// docentes). Null si no corresponde (con el aviso ya enseñado): un
        /// estudiante del SIIA no deja solicitud de docente ni a la reves.
        /// </summary>
        private static PerfilSiia? DecidirPerfil(string numero, ConfiguracionSiia siia, CuentaParaEntrar cuenta, DatosSiia datos)
        {
            bool esEstudiante = datos != null && datos.EsEstudiante;
            string tipoSiia = datos == null || datos.Tipo.Length == 0 ? "sin tipo" : datos.Tipo;
            if (cuenta.Existe && cuenta.Tipo == TiposUsuario.Profesor)
            {
                if (esEstudiante)
                {
                    Log.Info("SIIA: acepto '" + numero + "' (cuenta de docente en DigLab), pero como estudiante (E): no entra.");
                    AvisarCuentaDeEstudiante(siia);
                    return null;
                }

                return PerfilSiia.Docente;
            }

            if (cuenta.Existe && cuenta.Tipo == TiposUsuario.Alumno)
            {
                if (!esEstudiante)
                {
                    Log.Info("SIIA: acepto '" + numero + "' (cuenta de estudiante en DigLab), pero su cuenta del SIIA es '" + tipoSiia + "': no entra.");
                    AvisarNoEsEstudiante(siia);
                    return null;
                }

                return PerfilSiia.Estudiante;
            }

            // Un numero que DigLab no tiene: lo dice su tipo en el SIIA.
            if (esEstudiante)
            {
                if (siia.Estudiantes)
                {
                    return PerfilSiia.Estudiante;
                }

                Log.Info("SIIA: acepto '" + numero + "', que no esta en DigLab, como estudiante (E), y los estudiantes no entran con el SIIA.");
                AvisarCuentaDeEstudiante(siia);
                return null;
            }

            if (siia.Docentes)
            {
                return PerfilSiia.Docente;
            }

            Log.Info("SIIA: acepto '" + numero + "', que no esta en DigLab, con el tipo '" + tipoSiia + "', y solo entran estudiantes con el SIIA.");
            AvisarNoEsEstudiante(siia);
            return null;
        }

        private static void AvisarCuentaDeEstudiante(ConfiguracionSiia siia)
        {
            Avisar("Tu contraseña del SIIA es correcta, pero tu cuenta del SIIA es de estudiante, y esa no es una cuenta de docente."
                + Environment.NewLine + Environment.NewLine
                + (siia.Estudiantes ? "Como estudiante entras con tu matrícula, tu contraseña y tu NIP del SIIA."
                    : "Como estudiante entras con tu cuenta de DigLab; si todavía no tienes una, créala con «Crear cuenta»."),
                TituloInicio, MessageBoxIcon.Information);
        }

        private static void AvisarNoEsEstudiante(ConfiguracionSiia siia)
        {
            Avisar("Tu contraseña del SIIA es correcta, pero tu cuenta del SIIA no es de estudiante, así que no entras con ella como estudiante."
                + Environment.NewLine + Environment.NewLine
                + (siia.Docentes ? "Si eres docente, entra con tu número de empleado." : "Si eres docente o del personal, entra con tu cuenta de DigLab.")
                + " Si crees que es un error, avisa a la administración de DigLab.", TituloInicio, MessageBoxIcon.Information);
        }

        /// <summary>Lo que paso en el paso del NIP.</summary>
        private sealed class PasoDelNip
        {
            /// <summary>El NIP que acepto el SIIA (o el escrito, si el SIIA dejo de responder); null si no entra.</summary>
            public string Nip;
            /// <summary>El SIIA dejo de responder en el paso 2: se comprueba con la huella.</summary>
            public bool SinSiia;
            public string Causa;
        }

        /// <summary>
        /// El paso 2: pide el NIP (en la misma ventana) y lo verifica con el
        /// tempToken. Si no es el bueno, otro intento mientras valga el tempToken;
        /// al vencer, se vuelve al paso 1. Bloqueo e inactiva, como en el paso 1.
        /// </summary>
        private async Task<PasoDelNip> PedirYVerificarNipAsync(string numero, ConfiguracionSiia siia, RespuestaSiia paso1, Func<string, Task<string>> pedirNip)
        {
            PasoDelNip resultado = new PasoDelNip();
            if (pedirNip == null)
            {
                Avisar("Para entrar con el SIIA hace falta también tu NIP: vuelve a iniciar sesión desde la pantalla de inicio.", TituloInicio, MessageBoxIcon.Information);
                return resultado;
            }

            DateTime vence = DateTime.UtcNow.AddMinutes(paso1.MinutosDelToken);
            string aviso = null;
            while (true)
            {
                string nip = await pedirNip(aviso);
                if (nip == null)
                {
                    Log.Info("SIIA: '" + numero + "' volvio atras en el paso del NIP.");
                    return resultado;
                }

                if (DateTime.UtcNow >= vence)
                {
                    Log.Info("SIIA: '" + numero + "' tardo mas de " + paso1.MinutosDelToken + " minutos en escribir su NIP: vuelve al paso 1.");
                    AvisarTokenVencido(paso1.MinutosDelToken);
                    return resultado;
                }

                RespuestaSiia paso2 = await InicioConSiia.VerificarNipAsync(siia.Url, paso1.TempToken, nip);
                switch (paso2.Resultado)
                {
                    case ResultadoSiia.Aceptado:
                        resultado.Nip = nip;
                        return resultado;

                    case ResultadoSiia.NipIncorrecto:
                        Log.Info("SIIA: '" + numero + "' escribio un NIP incorrecto (" + paso2.Causa + ").");
                        aviso = "NIP incorrecto. Escríbelo otra vez.";
                        continue;

                    case ResultadoSiia.TokenVencido:
                        Log.Info("SIIA: '" + numero + "': vencio el tempToken del paso 1 (" + paso2.Causa + "): vuelve al paso 1.");
                        AvisarTokenVencido(paso1.MinutosDelToken);
                        return resultado;

                    case ResultadoSiia.SinRespuesta:
                        resultado.Nip = nip;
                        resultado.SinSiia = true;
                        resultado.Causa = "paso 2: " + paso2.Causa;
                        return resultado;

                    default:
                        AvisarRechazo(numero, paso2, "paso 2");
                        return resultado;
                }
            }
        }

        /// <summary>
        /// "LUCÍA PÉREZ RUIZ" del SIIA como "Lucía Pérez Ruiz"; null si el SIIA no
        /// dio nombre.
        /// </summary>
        private static string NombreParaMostrar(DatosSiia datos)
        {
            if (datos == null)
            {
                return null;
            }

            string completo = LimpiarDato(datos.NombreCompleto, 3 * LargoMaximoNombre);
            if (completo.Length == 0)
            {
                // LimpiarDato deja un solo espacio entre palabras aunque falte un apellido.
                completo = LimpiarDato(datos.Nombre + " " + datos.Paterno + " " + datos.Materno, 3 * LargoMaximoNombre);
            }

            if (completo.Length == 0)
            {
                return null;
            }

            TextInfo texto = CultureInfo.GetCultureInfo("es-MX").TextInfo;
            return texto.ToTitleCase(texto.ToLower(completo));
        }

        private static void AvisarTokenVencido(int minutos)
        {
            Avisar("Pasaron más de " + minutos + " minutos desde que escribiste tu contraseña, y el SIIA ya no acepta el NIP con ella."
                + Environment.NewLine + Environment.NewLine + "Vuelve a escribir tu número y tu contraseña.", TituloInicio, MessageBoxIcon.Information);
        }

        /// <summary>Inactiva (403) o demasiados intentos (429), en cualquiera de los dos pasos.</summary>
        private static void AvisarRechazo(string numero, RespuestaSiia respuesta, string paso)
        {
            if (respuesta.Resultado == ResultadoSiia.Inactivo)
            {
                Log.Info("SIIA: '" + numero + "' tiene la cuenta inactiva en el SIIA (" + paso + ", " + respuesta.Causa + ").");
                Avisar("El SIIA del TESCHI indica que tu cuenta está inactiva, así que no puedes entrar con ella." + Environment.NewLine + Environment.NewLine
                    + "Si crees que es un error, acude al área del TESCHI que administra el SIIA.", "Cuenta inactiva en el SIIA", MessageBoxIcon.Warning);
                return;
            }

            Log.Info("SIIA: '" + numero + "' bloqueado por demasiados intentos (" + paso + ", " + respuesta.Causa + ").");
            Avisar("Demasiados intentos: el SIIA del TESCHI detuvo por un momento los intentos de entrar con ese número."
                + Environment.NewLine + Environment.NewLine
                + (respuesta.SegundosParaReintentar > 0 ? "Espera " + Espera(respuesta.SegundosParaReintentar) : "Espera unos minutos")
                + " y vuelve a intentarlo.", "Demasiados intentos", MessageBoxIcon.Warning);
        }

        /// <summary>
        /// El SIIA acepto (los dos pasos) a una cuenta de DigLab que ya puede entrar
        /// con el (docente enlazado o estudiante): entra, con su huella al dia.
        /// </summary>
        private int EntrarCuentaAceptada(string numero, string secreto, CuentaParaEntrar cuenta)
        {
            // Solo despues de que el SIIA acepto: como con la contrasena de DigLab.
            if (!cuenta.Activo)
            {
                Log.Info("Inicio de sesion rechazado: '" + numero + "' esta dado de baja (el SIIA lo acepto).");
                AvisarBaja();
                return 0;
            }

            bool necesitaRehash;
            bool alDia = cuenta.Almacenada != null && Contrasenas.Verificar(secreto, cuenta.Almacenada, out necesitaRehash) && !necesitaRehash;
            if (!alDia)
            {
                GuardarHuella(numero, cuenta.Tipo, Contrasenas.Hash(secreto));
            }

            Log.Info("SIIA: '" + numero + "' (tipo " + cuenta.Tipo + ") aceptado.");
            return cuenta.Tipo;
        }

        /// <summary>
        /// La huella de la contrasena y el NIP que el SIIA acaba de aceptar, para
        /// cuando no responda: en la cuenta de ese numero y tipo (de un docente,
        /// solo si esta enlazado). Que falle no impide entrar: el SIIA ya acepto.
        /// </summary>
        private void GuardarHuella(string numero, int tipo, string huella)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE usuarios SET password = @Password WHERE numero_identificador = @Numero AND fk_tipo_usuario = @Tipo"
                    + (tipo == TiposUsuario.Profesor ? " AND siia_enlazado_en IS NOT NULL" : ""), conexion))
                {
                    comando.Parameters.AddWithValue("@Password", huella);
                    comando.Parameters.AddWithValue("@Numero", numero);
                    comando.Parameters.AddWithValue("@Tipo", tipo);
                    comando.ExecuteNonQuery();
                }

                Log.Info("SIIA: guardada la huella de la contrasena y el NIP que el SIIA acepto a '" + numero + "'.");
            }
            catch (Exception ex)
            {
                Log.Error("SIIA: no se pudo guardar la huella de '" + numero + "' (entra igual; se reintenta la proxima vez)", ex);
            }
        }

        /// <summary>
        /// El SIIA acepto (los dos pasos) a un docente que la administracion no ha
        /// enlazado: queda su solicitud y espera a que lo enlacen. El tipo
        /// (docente) si lo enlazaron mientras esperaba; 0 si salio de la espera.
        /// </summary>
        private int EsperarEnlace(string numero, string huella, DatosSiia datos)
        {
            try
            {
                EnlacesSiia.GuardarSolicitud(numero, datos);
            }
            catch (Exception ex)
            {
                Log.Error("SIIA: no se pudo guardar la solicitud de enlace de '" + numero + "' (falta db/24 o su permiso)", ex);
                Avisar("Tu contraseña del SIIA es correcta, pero falta que la administración enlace tu cuenta con tus clases, y desde este equipo "
                    + "no se pudo dejar tu solicitud." + Environment.NewLine + Environment.NewLine
                    + "Inténtalo de nuevo en un momento o desde otro equipo; si sigue fallando, " + MensajeDeFallo.AvisaAlLaboratorio + ".",
                    TituloInicio, MessageBoxIcon.Warning);
                return 0;
            }

            Log.Info("SIIA: '" + numero + "' aceptado sin enlazar: queda su solicitud para la administracion y espera.");
            if (!VentanaEnlacePendiente.Esperar(numero))
            {
                Log.Info("SIIA: '" + numero + "' salio de la espera sin que lo enlazaran.");
                return 0;
            }

            // Lo enlazaron: como al entrar, con su cuenta ya enlazada (y activa).
            CuentaParaEntrar ahora = LeerCuenta(numero);
            if (!ahora.Existe || ahora.Tipo != TiposUsuario.Profesor || !ahora.Enlazado)
            {
                Log.Aviso("SIIA: '" + numero + "' parecia enlazado, pero al entrar no lo esta (tipo " + ahora.Tipo + ").");
                Avisar("No se pudo iniciar sesión: tu cuenta cambió mientras esperabas. Vuelve a entrar con tu número de empleado y tu contraseña del SIIA.",
                    TituloInicio, MessageBoxIcon.Warning);
                return 0;
            }

            if (!ahora.Activo)
            {
                Log.Info("Inicio de sesion rechazado: '" + numero + "' esta dado de baja (lo enlazaron mientras esperaba).");
                AvisarBaja();
                return 0;
            }

            GuardarHuella(numero, TiposUsuario.Profesor, huella);
            Log.Info("SIIA: la administracion enlazo a '" + numero + "' mientras esperaba: entra.");
            return TiposUsuario.Profesor;
        }

        /// <summary>
        /// El SIIA acepto (los dos pasos) a un estudiante ('E') que DigLab no tiene:
        /// su cuenta se crea sola, con su nombre y apellidos del SIIA (sin su
        /// correo), su huella y, si el area del SIIA casa con una sola carrera de
        /// DigLab, esa carrera. Con su renglon en el historial (db/21). El tipo
        /// (estudiante) si se creo; 0 si no.
        /// </summary>
        private int CrearEstudiante(string numero, string huella, DatosSiia datos)
        {
            string nombre = LimpiarDato(datos == null ? null : datos.Nombre, LargoMaximoNombre);
            if (nombre.Length == 0)
            {
                nombre = LimpiarDato(datos == null ? null : datos.NombreCompleto, LargoMaximoNombre);
            }

            if (nombre.Length == 0)
            {
                Log.Aviso("SIIA: acepto al estudiante '" + numero + "', que no esta en DigLab, pero sin su nombre: no se crea su cuenta.");
                Avisar("Tu contraseña del SIIA es correcta, pero el SIIA no envió tu nombre, así que DigLab no puede crear tu cuenta."
                    + Environment.NewLine + Environment.NewLine + PideAsistencia, TituloInicio, MessageBoxIcon.Warning);
                return 0;
            }

            string paterno = LimpiarDato(datos.Paterno, LargoMaximoNombre);
            string materno = LimpiarDato(datos.Materno, LargoMaximoNombre);
            string completo = string.Join(" ", new[] { nombre, paterno, materno }).Trim();
            int id;
            string carrera = null;
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    // Sin fk_tipo_usuario si la base le da 1 por defecto (db/10): lo
                    // unico que la cuenta de los equipos puede crear (db/07).
                    bool porDefecto = Esquema.AlumnoPorDefecto(conexion, transaccion);
                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO usuarios (numero_identificador, nombre, apellido_paterno, apellido_materno, password"
                        + (porDefecto ? "" : ", fk_tipo_usuario") + @")
                        VALUES (@Numero, @Nombre, @Paterno, @Materno, @Password" + (porDefecto ? "" : ", @Tipo") + @");
                        SELECT LAST_INSERT_ID();", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Numero", numero);
                        comando.Parameters.AddWithValue("@Nombre", nombre);
                        comando.Parameters.AddWithValue("@Paterno", paterno);
                        comando.Parameters.AddWithValue("@Materno", materno);
                        comando.Parameters.AddWithValue("@Password", huella);
                        comando.Parameters.AddWithValue("@Tipo", TiposUsuario.Alumno);
                        id = Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }

                    int idCarrera;
                    carrera = CarreraDelArea(conexion, transaccion, datos.Area, out idCarrera);
                    if (carrera != null && Esquema.CarreraSinGrupo(conexion, transaccion))
                    {
                        using (MySqlCommand comando = new MySqlCommand(
                            "INSERT INTO carrera_grupo_semestre (usuarios_numero_identificador, carreras_idcarreras, grupos_idgrupos, semestres_idsemestres) "
                            + "VALUES (@Id, @Carrera, NULL, NULL)", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Id", id);
                            comando.Parameters.AddWithValue("@Carrera", idCarrera);
                            comando.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        carrera = null;
                    }

                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.Cuenta,
                        Accion = AccionDeCambio.Alta,
                        Tabla = "usuarios",
                        RegistroId = id,
                        Descripcion = "Cuenta de estudiante creada con el SIIA: " + numero + ", " + completo
                            + (carrera != null ? " (" + carrera + ")" : " (sin carrera: el SIIA dio «" + LimpiarDato(datos.Area, 120) + "»)"),
                        Despues = HistorialCambios.Datos("Matrícula", numero, "Nombre", completo, "Carrera", carrera ?? "Sin carrera")
                    }, numero);
                    transaccion.Commit();
                }
            }
            catch (MySqlException ex) when (ex.Number == AltaClaveDuplicada)
            {
                // Otro equipo la creo a la vez: se entra con esa (si es de estudiante).
                Log.Info("SIIA: la cuenta del estudiante '" + numero + "' se creo en otro equipo mientras tanto; se usa esa.");
                CuentaParaEntrar otra = LeerCuenta(numero);
                if (otra.Tipo == TiposUsuario.Alumno && otra.Activo)
                {
                    GuardarHuella(numero, TiposUsuario.Alumno, huella);
                    return TiposUsuario.Alumno;
                }

                Avisar("No se pudo iniciar sesión: tu cuenta cambió mientras se comprobaba. Inténtalo de nuevo; si sigue, "
                    + MensajeDeFallo.AvisaAlLaboratorio + ".", TituloInicio, MessageBoxIcon.Warning);
                return 0;
            }
            catch (MySqlException ex) when (ex.Number == AltaSinPermisoTabla || ex.Number == AltaSinPermisoColumna)
            {
                Log.Error("SIIA: la cuenta del estudiante '" + numero + "' no se pudo crear desde este equipo (permisos de MySQL, db/07 y db/10)", ex);
                Avisar("Tu contraseña del SIIA es correcta, pero desde este equipo no se puede crear tu cuenta de DigLab."
                    + Environment.NewLine + Environment.NewLine + "Inténtalo desde otro equipo; si sigue fallando, " + MensajeDeFallo.AvisaAlLaboratorio
                    + ". Si vienes a una clase, pide a tu profesor que te dé asistencia.", TituloInicio, MessageBoxIcon.Warning);
                return 0;
            }

            Log.Info("SIIA: cuenta de estudiante creada para '" + numero + "' (id " + id + ")" + (carrera != null ? ", con su carrera" : ", sin carrera") + ".");
            return TiposUsuario.Alumno;
        }

        /// <summary>
        /// El SIIA acepto (los dos pasos) a un estudiante cuya cuenta ya existia. Si
        /// no tiene carrera (las de antes del SIIA, o el area no caso al crearla),
        /// toma la del SIIA con las reglas de una cuenta nueva (CarreraDelArea) y su
        /// renglon en el historial (db/21). La que ya tiene una no se toca: pudo
        /// elegirla la administracion. Con INSERT y sin UPDATE: es lo que ya puede
        /// la cuenta de los equipos (db/07 y db/24). Que falle o no case no impide
        /// entrar: se reintenta la proxima vez.
        /// </summary>
        private void CompletarCarrera(string numero, DatosSiia datos)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    object id;
                    using (MySqlCommand comando = new MySqlCommand(@"
                        SELECT u.idusuarios FROM usuarios u
                        WHERE u.numero_identificador = @Numero AND u.fk_tipo_usuario = @Tipo
                          AND NOT EXISTS (SELECT 1 FROM carrera_grupo_semestre x WHERE x.usuarios_numero_identificador = u.idusuarios)", conexion))
                    {
                        comando.Parameters.AddWithValue("@Numero", numero);
                        comando.Parameters.AddWithValue("@Tipo", TiposUsuario.Alumno);
                        id = comando.ExecuteScalar();
                    }

                    // Lo normal: ya tiene su carrera, y esto no costo mas que esa consulta.
                    if (id == null || id == DBNull.Value)
                    {
                        return;
                    }

                    using (MySqlTransaction transaccion = conexion.BeginTransaction())
                    {
                        int idCarrera;
                        string carrera = CarreraDelArea(conexion, transaccion, datos.Area, out idCarrera);
                        if (carrera == null || !Esquema.CarreraSinGrupo(conexion, transaccion))
                        {
                            Log.Info("SIIA: '" + numero + "' sigue sin carrera: el SIIA dio «" + LimpiarDato(datos.Area, 120) + "».");
                            return;
                        }

                        // Sin NOT EXISTS, dos equipos a la vez le pondrian dos filas.
                        using (MySqlCommand comando = new MySqlCommand(@"
                            INSERT INTO carrera_grupo_semestre (usuarios_numero_identificador, carreras_idcarreras, grupos_idgrupos, semestres_idsemestres)
                            SELECT @Id, @Carrera, NULL, NULL FROM DUAL
                            WHERE NOT EXISTS (SELECT 1 FROM carrera_grupo_semestre x WHERE x.usuarios_numero_identificador = @Id)", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Id", id);
                            comando.Parameters.AddWithValue("@Carrera", idCarrera);
                            if (comando.ExecuteNonQuery() == 0)
                            {
                                return;
                            }
                        }

                        HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                        {
                            Tipo = TipoDeCambio.Cuenta,
                            Accion = AccionDeCambio.Cambio,
                            Tabla = "usuarios",
                            RegistroId = Convert.ToInt32(id, CultureInfo.InvariantCulture),
                            Descripcion = "Carrera tomada del SIIA: " + numero + ", " + carrera,
                            Antes = HistorialCambios.Datos("Carrera", "Sin carrera"),
                            Despues = HistorialCambios.Datos("Carrera", carrera)
                        }, numero);
                        transaccion.Commit();
                        Log.Info("SIIA: '" + numero + "' tomo su carrera del SIIA.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("SIIA: no se pudo poner la carrera de '" + numero + "' (entra igual; se reintenta la proxima vez)", ex);
            }
        }

        /// <summary>
        /// La carrera de DigLab que casa con el area del SIIA (sin mayusculas,
        /// acentos ni espacios de mas): su nombre, o null si no casa con ninguna o
        /// casa con mas de una. Sin una igual, vale la UNICA que este a una o dos
        /// letras de distancia: la base de la escuela tiene "Ingeneria en Sistemas
        /// Computacionales" y el SIIA da "INGENIERIA EN SISTEMAS COMPUTACIONALES".
        /// </summary>
        private static string CarreraDelArea(MySqlConnection conexion, MySqlTransaction transaccion, string area, out int idCarrera)
        {
            idCarrera = 0;
            string buscada = EnlacesSiia.Normalizar(area);
            if (buscada.Length == 0)
            {
                return null;
            }

            List<KeyValuePair<int, string>> iguales = new List<KeyValuePair<int, string>>();
            List<KeyValuePair<int, string>> parecidas = new List<KeyValuePair<int, string>>();
            int maximo = Math.Min(2, buscada.Length / 15);
            using (MySqlCommand comando = new MySqlCommand("SELECT idcarreras, nombre_carrera FROM carreras", conexion, transaccion))
            using (MySqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    string nombre = lector.IsDBNull(1) ? "" : lector.GetString(1);
                    string normalizado = EnlacesSiia.Normalizar(nombre);
                    KeyValuePair<int, string> carrera = new KeyValuePair<int, string>(Convert.ToInt32(lector[0], CultureInfo.InvariantCulture), nombre);
                    if (normalizado == buscada)
                    {
                        iguales.Add(carrera);
                    }
                    else if (maximo > 0 && Distancia(normalizado, buscada, maximo) <= maximo)
                    {
                        parecidas.Add(carrera);
                    }
                }
            }

            List<KeyValuePair<int, string>> elegidas = iguales.Count > 0 ? iguales : parecidas;
            if (elegidas.Count != 1)
            {
                return null;
            }

            idCarrera = elegidas[0].Key;
            return elegidas[0].Value;
        }

        /// <summary>
        /// Cuantas letras hay que cambiar, poner o quitar para pasar de un texto al
        /// otro (Levenshtein); en cuanto pasa de <paramref name="tope"/> devuelve tope + 1.
        /// </summary>
        private static int Distancia(string a, string b, int tope)
        {
            if (Math.Abs(a.Length - b.Length) > tope)
            {
                return tope + 1;
            }

            int[] anterior = new int[b.Length + 1];
            int[] actual = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
            {
                anterior[j] = j;
            }

            for (int i = 1; i <= a.Length; i++)
            {
                actual[0] = i;
                int menor = actual[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cambio = anterior[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    actual[j] = Math.Min(cambio, Math.Min(anterior[j] + 1, actual[j - 1] + 1));
                    menor = Math.Min(menor, actual[j]);
                }

                if (menor > tope)
                {
                    return tope + 1;
                }

                int[] cambiar = anterior;
                anterior = actual;
                actual = cambiar;
            }

            return anterior[b.Length];
        }

        /// <summary>
        /// El SIIA no dio una respuesta util en el paso 1: los dos pasos contra la
        /// huella (la contrasena y el NIP juntos), si la cuenta la tiene. Sin
        /// huella no hay acceso.
        /// </summary>
        private async Task<int> EntrarSinSiiaAsync(string numero, string contrasena, ConfiguracionSiia siia, CuentaParaEntrar cuenta,
            Func<string, Task<string>> pedirNip, string causa)
        {
            Log.Aviso("SIIA: sin respuesta para '" + numero + "' (" + causa + "): se comprueba con la huella.");
            if (!cuenta.ConHuella)
            {
                return SinHuella(numero, siia, cuenta);
            }

            // Lo guardado es su contrasena de DigLab de antes (nunca entro con el SIIA):
            // no es una huella del SIIA, y sin el SIIA no hay con que comprobarla.
            bool necesitaRehash;
            if (Contrasenas.Verificar(contrasena, cuenta.Almacenada, out necesitaRehash))
            {
                Log.Info("SIIA sin respuesta: '" + numero + "' tiene su contrasena de DigLab, no una huella del SIIA: no entra.");
                return SinHuella(numero, siia, cuenta);
            }

            // Una cuenta del SIIA sin NIP requerido: su huella es la de la contrasena sola.
            if (Contrasenas.Verificar(InicioConSiia.Combinar(contrasena, ""), cuenta.Almacenada, out necesitaRehash))
            {
                return EntrarConHuella(numero, contrasena, "", siia, cuenta, causa);
            }

            if (pedirNip == null)
            {
                return SinHuella(numero, siia, cuenta);
            }

            string nip = await pedirNip(null);
            if (nip == null)
            {
                return 0;
            }

            return EntrarConHuella(numero, contrasena, nip, siia, cuenta, causa);
        }

        /// <summary>Sin el SIIA: entra si la contrasena y el NIP son los de su huella.</summary>
        private int EntrarConHuella(string numero, string contrasena, string nip, ConfiguracionSiia siia, CuentaParaEntrar cuenta, string causa)
        {
            bool necesitaRehash;
            string secreto = InicioConSiia.Combinar(contrasena, nip);
            if (!cuenta.ConHuella || !Contrasenas.Verificar(secreto, cuenta.Almacenada, out necesitaRehash))
            {
                Log.Info("SIIA sin respuesta (" + causa + "): '" + numero + "' no entra (" + (cuenta.ConHuella ? "no coincide con su huella" : "sin huella") + ").");
                if (!cuenta.ConHuella)
                {
                    return SinHuella(numero, siia, cuenta);
                }

                Avisar("Tu contraseña o tu NIP no son correctos, o no se pudieron comprobar: ahora el SIIA del TESCHI no responde (sin internet en este "
                    + "equipo, tardó demasiado o tiene una falla) y, mientras tanto, solo se entra con la contraseña y el NIP con los que entraste la "
                    + "última vez." + Environment.NewLine + Environment.NewLine + RevisaComoSeEscriben
                    + (cuenta.Tipo == TiposUsuario.Alumno ? "Si no puedes entrar, pide a tu profesor que te dé asistencia."
                        : "Inténtalo de nuevo en unos minutos; si sigue, " + MensajeDeFallo.AvisaAlLaboratorio + "."),
                    TituloInicio, MessageBoxIcon.Error);
                return 0;
            }

            if (!cuenta.Activo)
            {
                Log.Info("Inicio de sesion rechazado: '" + numero + "' esta dado de baja.");
                AvisarBaja();
                return 0;
            }

            if (necesitaRehash)
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    MigrarContrasena(conexion, numero, secreto, Contrasenas.EsFormatoAntiguo(cuenta.Almacenada));
                }
            }

            Log.Info("SIIA sin respuesta (" + causa + "): '" + numero + "' (tipo " + cuenta.Tipo + ") entra con la huella de lo ultimo que el SIIA le acepto.");
            return cuenta.Tipo;
        }

        /// <summary>Sin el SIIA y sin huella: no hay acceso. Al estudiante, que pida asistencia a su profesor.</summary>
        private static int SinHuella(string numero, ConfiguracionSiia siia, CuentaParaEntrar cuenta)
        {
            Log.Info("SIIA sin respuesta: '" + numero + "' no entra (" + (!cuenta.Existe ? "no esta en DigLab" : cuenta.Tipo == TiposUsuario.Profesor && !cuenta.Enlazado
                ? "no esta enlazado" : "sin huella") + ").");
            // Todo junto: el aviso no dice si el numero esta en DigLab.
            bool estudiante = cuenta.Tipo == TiposUsuario.Alumno || (!cuenta.Existe && siia.Estudiantes);
            Avisar(estudiante && !(siia.Docentes && !cuenta.Existe)
                ? PideAsistencia
                : "No se pudo comprobar tu cuenta con el SIIA del TESCHI: ahora no responde (sin internet en este equipo, tardó demasiado o tiene una "
                    + "falla) y, mientras tanto, solo entra quien ya entró antes con el SIIA en DigLab." + Environment.NewLine + Environment.NewLine
                    + (siia.Estudiantes ? "Si eres estudiante y vienes a una clase, pide a tu profesor que te dé asistencia. " : "")
                    + "Inténtalo de nuevo en unos minutos; si sigue, " + MensajeDeFallo.AvisaAlLaboratorio + ".",
                TituloInicio, MessageBoxIcon.Warning);
            return 0;
        }

        /// <summary>La cuenta de ese numero, como la lee el inicio de sesion (con db/24, si esta enlazada).</summary>
        private CuentaParaEntrar LeerCuenta(string numero)
        {
            CuentaParaEntrar cuenta = new CuentaParaEntrar();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                string columnaActivo = Esquema.TieneBajas(conexion) ? "activo" : "1 AS activo";
                string columnaEnlace = Esquema.EnlacesSiia(conexion) ? "(siia_enlazado_en IS NOT NULL)" : "0";
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT fk_tipo_usuario, password, " + columnaActivo + ", " + columnaEnlace + " AS enlazado FROM usuarios WHERE numero_identificador = @Numero",
                    conexion))
                {
                    comando.Parameters.AddWithValue("@Numero", numero);
                    using (MySqlDataReader lector = comando.ExecuteReader())
                    {
                        if (lector.Read())
                        {
                            cuenta.Existe = true;
                            cuenta.Tipo = Convert.ToInt32(lector["fk_tipo_usuario"], CultureInfo.InvariantCulture);
                            cuenta.Almacenada = lector["password"] == DBNull.Value ? null : lector["password"].ToString();
                            cuenta.Activo = Convert.ToInt32(lector["activo"], CultureInfo.InvariantCulture) == 1;
                            cuenta.Enlazado = Convert.ToInt32(lector["enlazado"], CultureInfo.InvariantCulture) == 1;
                        }
                    }
                }
            }

            return cuenta;
        }

        private static void AvisarBaja()
        {
            MessageBox.Show("Tu cuenta está dada de baja. Si crees que es un error, acude a la administración de DigLab.", "Cuenta dada de baja",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void Avisar(string texto, string titulo, MessageBoxIcon icono)
        {
            MessageBox.Show(texto, titulo, MessageBoxButtons.OK, icono);
        }

        /// <summary>"45 segundos", "1 segundo" o "300 segundos (unos 5 minutos)".</summary>
        private static string Espera(int segundos)
        {
            string texto = segundos == 1 ? "1 segundo" : segundos.ToString(CultureInfo.InvariantCulture) + " segundos";
            if (segundos >= 120)
            {
                int minutos = (segundos + 59) / 60;
                texto += " (unos " + minutos.ToString(CultureInfo.InvariantCulture) + " minutos)";
            }

            return texto;
        }

        /// <summary>
        /// Un dato del SIIA como se guarda: sin caracteres de control ni nada fuera
        /// del BMP (las tablas de la escuela son utf8 de 3 bytes), con los espacios
        /// de mas quitados y recortado a lo que cabe.
        /// </summary>
        private static string LimpiarDato(string valor, int largoMaximo)
        {
            System.Text.StringBuilder limpio = new System.Text.StringBuilder();
            bool espacio = false;
            foreach (char c in valor ?? "")
            {
                if (char.IsWhiteSpace(c))
                {
                    espacio = limpio.Length > 0;
                    continue;
                }

                if (char.IsControl(c) || char.IsSurrogate(c))
                {
                    continue;
                }

                if (espacio)
                {
                    limpio.Append(' ');
                    espacio = false;
                }

                limpio.Append(c);
            }

            string texto = limpio.ToString();
            return texto.Length <= largoMaximo ? texto : texto.Substring(0, largoMaximo).TrimEnd();
        }
    }
}
