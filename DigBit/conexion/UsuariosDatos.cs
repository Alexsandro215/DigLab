using System;
using System.Linq;
using System.Text.RegularExpressions;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Lo comun a alumnos, profesores y encargados de laboratorio en las pantallas del administrador.</summary>
    public class UsuarioInfo
    {
        public int Id;
        /// <summary>numero_identificador: matricula del alumno o numero de empleado del profesor o del encargado.</summary>
        public string Matricula;
        public string Nombre;
        public string ApellidoPaterno;
        public string ApellidoMaterno;
        public string Correo;
        public bool Activo;
        public DateTime? BajaEn;

        public string NombreCompleto
        {
            get
            {
                return string.Join(" ", new[] { Nombre, ApellidoPaterno, ApellidoMaterno }
                    .Where(p => !string.IsNullOrWhiteSpace(p)));
            }
        }

        /// <summary>
        /// "Arias Mora Gisela": el nombre de un estudiante en pantalla, como en la
        /// lista oficial de su grupo (ComoEnLaLista). El PDF de bitacoras no cambia.
        /// </summary>
        public string NombreComoEnLaLista
        {
            get { return ComoEnLaLista(Nombre, ApellidoPaterno, ApellidoMaterno); }
        }

        /// <summary>
        /// "Apellido paterno Apellido materno Nombre", sin coma, como la lista oficial
        /// ("Arias Mora Gisela"): el mismo estudiante se lee igual en todas las
        /// pantallas (Estudiantes, Bitacoras, la Bitacora de la clase, Mis grupos) y
        /// se cruza con la lista sin leerlo dos veces. Lo que falte se salta. El PDF
        /// de bitacoras sigue con "Nombre Apellidos" (ya hay PDF entregados asi).
        /// </summary>
        public static string ComoEnLaLista(string nombre, string paterno, string materno)
        {
            return string.Join(" ", new[] { paterno, materno, nombre }
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
        }

        /// <summary>"Barrera Islas, Hugo": como en las listas de la escuela, y como se ordena.</summary>
        public string NombreParaLista
        {
            get
            {
                string apellidos = string.Join(" ", new[] { ApellidoPaterno, ApellidoMaterno }
                    .Where(p => !string.IsNullOrWhiteSpace(p)));
                return string.IsNullOrWhiteSpace(Nombre) ? apellidos : apellidos.Length == 0 ? Nombre : apellidos + ", " + Nombre;
            }
        }

        /// <summary>Se anonimizo (UsuariosDatos.Anonimizar): su identificador es "ANON-" y su id.</summary>
        public bool Anonimizada
        {
            get { return string.Equals(Matricula, UsuariosDatos.IdentificadorAnonimo(Id), StringComparison.OrdinalIgnoreCase); }
        }
    }

    /// <summary>Lo que se guarda de cualquier usuario al darlo de alta o editarlo.</summary>
    public class DatosUsuario
    {
        public string Matricula;
        public string Nombre;
        public string ApellidoPaterno;
        public string ApellidoMaterno;
        public string Correo;
        /// <summary>Al editar: la contrasena nueva que puso el administrador, o null para no tocarla.</summary>
        public string ContrasenaNueva;
    }

    /// <summary>Un error que se le puede ensenar tal cual al administrador.</summary>
    public class ErrorDeDatos : Exception
    {
        public ErrorDeDatos(string mensaje) : base(mensaje)
        {
        }

        /// <param name="campo">El dato que choca con otro usuario (ver CampoRepetido), para senalarlo en el dialogo.</param>
        public ErrorDeDatos(string mensaje, CampoRepetido campo) : base(mensaje)
        {
            Campo = campo;
        }

        public CampoRepetido Campo { get; private set; }
    }

    /// <summary>Que dato de un alta ya lo tiene otro usuario.</summary>
    public enum CampoRepetido
    {
        Ninguno,
        Identificador,
        Correo,
        Codigo
    }

    /// <summary>"Mi perfil": la contrasena actual que se escribio no es la de la cuenta. No se guardo nada.</summary>
    public class ContrasenaIncorrecta : ErrorDeDatos
    {
        public ContrasenaIncorrecta() : base("La contraseña actual no es correcta.")
        {
        }
    }

    /// <summary>
    /// Alta, edicion, contrasena y baja de usuarios de un tipo (alumno, profesor
    /// o encargado de laboratorio). No muestra mensajes: lanza la excepcion y la
    /// pantalla decide. Nadie se borra desde aqui: se da de baja
    /// (db/08_migracion_bajas.sql), porque las claves foraneas borrarian en
    /// cascada su historial. Lo que escribe empieza con Permisos.Exigir: en solo
    /// consulta no se hace. Sin sesion (el "Crear cuenta" del inicio de sesion)
    /// no bloquea.
    /// </summary>
    internal abstract class UsuariosDatos
    {
        protected const int ErrorClaveDuplicada = 1062;
        // ER_NO_REFERENCED_ROW_2: en la base de la escuela usuarios.fk_tipo_usuario
        // tiene clave foranea a tipo_usuarios, y un tipo que no esta ahi se rechaza.
        private const int ErrorClaveForanea = 1452;

        protected readonly Conexion mconexion = new Conexion();

        /// <summary>usuarios.fk_tipo_usuario: 1 alumno, 2 profesor, 4 encargado de laboratorio (TiposUsuario).</summary>
        protected abstract int TipoUsuario { get; }

        /// <summary>"matrícula" o "número de empleado", para los mensajes.</summary>
        public abstract string NombreIdentificador { get; }

        /// <summary>False si la base aun no tiene la migracion 08: se lista, pero no se da de baja a nadie.</summary>
        public bool BajasDisponibles { get; protected set; }

        /// <param name="contrasena">
        /// Null: sin contrasena de DigBit (EnlacesSiia.SinHuella). Es el docente o el
        /// estudiante que entra con el SIIA: no entra hasta hacerlo con el SIIA (y,
        /// el docente, hasta que la administracion lo enlace).
        /// </param>
        public void Crear(DatosUsuario datos, string contrasena)
        {
            Permisos.Exigir("Dar de alta un usuario de tipo " + TipoUsuario);
            string hash = contrasena == null ? EnlacesSiia.SinHuella : Contrasenas.Hash(contrasena);
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    // Un alumno no nombra fk_tipo_usuario si la base ya le da 1 por
                    // defecto (db/10): es lo que permite al usuario de MySQL de los
                    // equipos, que no puede escribir esa columna (db/07), crear
                    // cuentas desde el laboratorio. El resultado es el mismo.
                    bool tipoPorDefecto = TipoUsuario == TiposUsuario.Alumno && Esquema.AlumnoPorDefecto(conexion, transaccion);
                    int id;
                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO usuarios (numero_identificador, nombre, apellido_paterno, apellido_materno, correo, password"
                            + (tipoPorDefecto ? "" : ", fk_tipo_usuario") + @")
                        VALUES (@Matricula, @Nombre, @Paterno, @Materno, @Correo, @Password" + (tipoPorDefecto ? "" : ", @Tipo") + @");
                        SELECT LAST_INSERT_ID();", conexion, transaccion))
                    {
                        ParametrosUsuario(comando, datos);
                        comando.Parameters.AddWithValue("@Password", hash);
                        comando.Parameters.AddWithValue("@Tipo", TipoUsuario);
                        id = Convert.ToInt32(comando.ExecuteScalar());
                    }

                    GuardarExtras(conexion, transaccion, id, datos);
                    transaccion.Commit();
                    Log.Info("Alta del usuario " + datos.Matricula + " (tipo " + TipoUsuario + ") por " + Quien() + ".");
                }
                catch (MySqlException ex) when (ex.Number == ErrorClaveDuplicada)
                {
                    transaccion.Rollback();
                    throw Repetido(ex, datos, "Ya hay una cuenta con ");
                }
                catch (MySqlException ex) when (ex.Number == ErrorClaveForanea && TipoUsuario == TiposUsuario.Encargado)
                {
                    // En la escuela, sin la fila del 4 en tipo_usuarios (db/18).
                    transaccion.Rollback();
                    Log.Aviso("Alta del encargado " + datos.Matricula + " rechazada por la base (1452): falta db/18_encargados.sql.");
                    throw new ErrorDeDatos("Falta aplicar db/18_encargados.sql en la base: sin ella no se pueden registrar responsables de laboratorio. "
                        + "Pide a quien administra el servidor que la aplique.");
                }
                catch
                {
                    transaccion.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// El 1062 de un alta o una edicion, dicho para el que lo lee: la base de
        /// la escuela tiene UNIQUE en el correo ademas de en la matricula, y el
        /// mensaje de MySQL dice cual ("for key 'correo_UNIQUE'").
        /// </summary>
        private ErrorDeDatos Repetido(MySqlException ex, DatosUsuario datos, string inicio)
        {
            Match clave = Regex.Match(ex.Message, "for key '([^']*)'");
            if (clave.Success && clave.Groups[1].Value.IndexOf("correo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ErrorDeDatos(inicio + "el correo " + (datos.Correo ?? "").Trim() + ".", CampoRepetido.Correo);
            }

            return new ErrorDeDatos(inicio + NombreIdentificador + " " + datos.Matricula + ".", CampoRepetido.Identificador);
        }

        /// <summary>Sus datos y, si datos.ContrasenaNueva trae una, tambien la contrasena: todo o nada.</summary>
        public void Actualizar(int id, DatosUsuario datos)
        {
            Permisos.Exigir("Editar el usuario id " + id);
            bool conContrasena = !string.IsNullOrEmpty(datos.ContrasenaNueva);
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    object correoAntes = null;
                    using (MySqlCommand leer = new MySqlCommand(
                        "SELECT correo, numero_identificador FROM usuarios WHERE idusuarios = @Id AND fk_tipo_usuario = @Tipo", conexion, transaccion))
                    {
                        leer.Parameters.AddWithValue("@Id", id);
                        leer.Parameters.AddWithValue("@Tipo", TipoUsuario);
                        using (MySqlDataReader reader = leer.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                correoAntes = reader["correo"];
                                // Lo anonimizado no vuelve: darle otra vez un nombre seria deshacerlo.
                                if (string.Equals(Texto(reader, "numero_identificador"), IdentificadorAnonimo(id), StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new ErrorDeDatos("Esta cuenta se anonimizó y sus datos ya no se pueden editar. Si la persona vuelve, regístrala otra vez.");
                                }
                            }
                        }
                    }

                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE usuarios
                           SET numero_identificador = @Matricula, nombre = @Nombre, apellido_paterno = @Paterno,
                               apellido_materno = @Materno, correo = @Correo" + (conContrasena ? ", password = @Password" : "") + @"
                         WHERE idusuarios = @Id AND fk_tipo_usuario = @Tipo", conexion, transaccion))
                    {
                        ParametrosUsuario(comando, datos);
                        comando.Parameters.AddWithValue("@Id", id);
                        comando.Parameters.AddWithValue("@Tipo", TipoUsuario);
                        if (conContrasena)
                        {
                            comando.Parameters.AddWithValue("@Password", Contrasenas.Hash(datos.ContrasenaNueva));
                        }

                        comando.ExecuteNonQuery();
                    }

                    // Un codigo que salio hacia el correo de antes no debe seguir valiendo.
                    string antes = correoAntes == null || correoAntes == DBNull.Value ? "" : correoAntes.ToString().Trim();
                    if (conContrasena || !string.Equals(antes, (datos.Correo ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        AnularCodigosDeRecuperacion(conexion, transaccion, id);
                    }

                    GuardarExtras(conexion, transaccion, id, datos);
                    transaccion.Commit();
                    Log.Info("Cambios en el usuario " + datos.Matricula + " (id " + id + ")" + (conContrasena ? ", con contrasena nueva," : "")
                        + " por " + Quien() + ".");
                }
                catch (MySqlException ex) when (ex.Number == ErrorClaveDuplicada)
                {
                    transaccion.Rollback();
                    throw Repetido(ex, datos, "Ya hay otra cuenta con ");
                }
                catch
                {
                    transaccion.Rollback();
                    throw;
                }
            }
        }

        /// <summary>Lo que cada tipo guarda ademas de la fila de usuarios, en la misma transaccion.</summary>
        protected virtual void GuardarExtras(MySqlConnection conexion, MySqlTransaction transaccion, int id, DatosUsuario datos)
        {
        }

        /// <summary>
        /// GuardarExtras desde "Mi perfil" (MiPerfilDatos), en su transaccion: la
        /// carrera y el semestre del alumno, igual que cuando lo edita el
        /// administrador. Solo el alumno: lo del profesor (codigo y color) lo
        /// cambia el administrador.
        /// </summary>
        internal void GuardarExtrasPropios(MySqlConnection conexion, MySqlTransaction transaccion, int id, DatosUsuario datos)
        {
            if (TipoUsuario != TiposUsuario.Alumno)
            {
                throw new InvalidOperationException("Mi perfil solo guarda extras del alumno.");
            }

            GuardarExtras(conexion, transaccion, id, datos);
        }

        public void RestablecerContrasena(int id, string nueva)
        {
            Permisos.Exigir("Restablecer la contrasena del usuario id " + id);
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE usuarios SET password = @Password WHERE idusuarios = @Id", conexion))
                {
                    comando.Parameters.AddWithValue("@Password", Contrasenas.Hash(nueva));
                    comando.Parameters.AddWithValue("@Id", id);
                    comando.ExecuteNonQuery();
                }

                AnularCodigosDeRecuperacion(conexion, null, id);
            }

            Log.Info("Contrasena restablecida para el usuario id " + id + " por " + Quien() + ".");
        }

        /// <summary>
        /// Los codigos de "Olvidaste tu contrasena?" pendientes de esa cuenta dejan
        /// de valer (db/13): cambio su contrasena, o el correo al que pudieron
        /// llegar. Sin la tabla, o sin permiso sobre ella, no hay nada que anular.
        /// </summary>
        private static void AnularCodigosDeRecuperacion(MySqlConnection conexion, MySqlTransaction transaccion, int id)
        {
            if (!Esquema.Recuperacion(conexion, transaccion))
            {
                return;
            }

            try
            {
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE recuperacion_contrasena SET usado_en = NOW() WHERE usuario_id = @Id AND usado_en IS NULL", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", id);
                    int anulados = comando.ExecuteNonQuery();
                    if (anulados > 0)
                    {
                        Log.Info("Anulados " + anulados + " codigos de recuperacion pendientes del usuario id " + id + ".");
                    }
                }
            }
            catch (MySqlException ex) when (ex.Number == 1142 || ex.Number == 1143)
            {
                // Sin permiso (no deberia: el administrador escribe en toda la base).
                Log.Aviso("No se pudieron anular los codigos de recuperacion del usuario id " + id + ": " + ex.Message);
            }
        }

        public void DarDeBaja(int id)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                CambiarEstado(conexion, null, id, false);
            }
        }

        public void Reactivar(int id)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                CambiarEstado(conexion, null, id, true);
            }
        }

        /// <summary>Baja o reactivacion; la usan tambien ProfesoresDatos.DarDeBaja y las bajas de encargados.</summary>
        protected void CambiarEstado(MySqlConnection conexion, MySqlTransaction transaccion, int id, bool activo)
        {
            Permisos.Exigir((activo ? "Reactivar" : "Dar de baja") + " el usuario id " + id);
            if (!Esquema.TieneBajas(conexion, transaccion))
            {
                throw new ErrorDeDatos("La base de datos todavía no admite bajas: falta aplicar db/08_migracion_bajas.sql. "
                    + "Pide a quien administra el servidor que la aplique.");
            }

            if (activo)
            {
                // Una cuenta anonimizada no vuelve: ya no es de nadie.
                using (MySqlCommand comando = new MySqlCommand("SELECT numero_identificador FROM usuarios WHERE idusuarios = @Id", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", id);
                    object identificador = comando.ExecuteScalar();
                    if (identificador != null && identificador != DBNull.Value
                        && string.Equals(identificador.ToString(), IdentificadorAnonimo(id), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ErrorDeDatos("Esta cuenta se anonimizó y ya no se puede reactivar. Si la persona vuelve, regístrala otra vez.");
                    }
                }
            }

            using (MySqlCommand comando = new MySqlCommand(activo
                ? "UPDATE usuarios SET activo = 1, baja_en = NULL WHERE idusuarios = @Id"
                : "UPDATE usuarios SET activo = 0, baja_en = NOW() WHERE idusuarios = @Id AND activo = 1", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                comando.ExecuteNonQuery();
            }

            Log.Info((activo ? "Reactivado" : "Dado de baja") + " el usuario id " + id + " por " + Quien() + ".");
        }

        /// <summary>El principio del identificador de una cuenta anonimizada.</summary>
        public const string PrefijoAnonimo = "ANON-";

        // Lo que queda en vez de su nombre: "Persona ANON-123" (nombre y apellido
        // paterno), sin apellido materno ni correo.
        private const string NombreAnonimo = "Persona";

        // Lo que queda en vez de su contrasena: no es PBKDF2 ni MD5, asi que
        // Contrasenas.Verificar nunca la da por buena.
        private const string ContrasenaAnulada = "anonimizada";

        /// <summary>"ANON-123": unico, porque lleva su id (y un identificador de verdad no lleva "-").</summary>
        public static string IdentificadorAnonimo(int id)
        {
            return PrefijoAnonimo + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>"estudiante" o "docente", para los textos del historial.</summary>
        protected virtual string NombreDelTipo
        {
            get { return "cuenta"; }
        }

        /// <summary>
        /// Derechos ARCO (cancelacion u oposicion): una cuenta de este tipo ya dada
        /// de baja deja de tener datos personales. Su nombre y su apellido paterno
        /// pasan a ser "Persona" y "ANON-id", sin apellido materno ni correo; su
        /// matricula o numero de empleado, "ANON-id"; su contrasena deja de servir y
        /// sus codigos de recuperacion pendientes tambien. Sus bitacoras, sesiones
        /// y clases se quedan, enlazadas por su id, sin su nombre; las asistencias
        /// que le dio su profesor sin bitacora (db/22), tambien, pero sin el detalle
        /// que escribio el profesor (puede llevar su nombre). No se deshace
        /// (tampoco se reactiva ni se edita despues): la pantalla pide antes la
        /// contrasena de quien lo hace. Con db/21 queda en el historial, en la
        /// misma transaccion; siempre, en el log, sin sus datos. Lo que no se
        /// puede (no esta de baja, ya se anonimizo...) sale como ErrorDeDatos.
        /// </summary>
        public void Anonimizar(int id)
        {
            Permisos.Exigir("Anonimizar la cuenta del usuario id " + id);
            ExigirTipoAnonimizable();
            string anonimo = IdentificadorAnonimo(id);
            // Cuantos detalles de asistencias dadas se vaciaron (solo para el log).
            int detallesVaciados = 0;
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    ComprobarQueSePuedeAnonimizar(conexion, transaccion, id);

                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE usuarios
                           SET numero_identificador = @Anonimo, nombre = @Nombre, apellido_paterno = @Anonimo, apellido_materno = '',
                               correo = NULL, password = @Anulada
                         WHERE idusuarios = @Id AND fk_tipo_usuario = @Tipo AND activo = 0", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Anonimo", anonimo);
                        comando.Parameters.AddWithValue("@Nombre", NombreAnonimo);
                        comando.Parameters.AddWithValue("@Anulada", ContrasenaAnulada);
                        comando.Parameters.AddWithValue("@Id", id);
                        comando.Parameters.AddWithValue("@Tipo", TipoUsuario);
                        comando.ExecuteNonQuery();
                    }

                    // Las asistencias que le dio su profesor sin bitacora (db/22): el detalle lo
                    // escribio el profesor y puede llevar su nombre, asi que se vacia. La fila se
                    // queda (es su asistencia, enlazada por su id), igual que sus bitacoras.
                    if (TipoUsuario == TiposUsuario.Alumno && Esquema.AsistenciaDada(conexion, transaccion))
                    {
                        using (MySqlCommand comando = new MySqlCommand(
                            "UPDATE asistencias_dadas SET comentario = NULL WHERE fk_alumno = @Id AND comentario IS NOT NULL", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Id", id);
                            detallesVaciados = comando.ExecuteNonQuery();
                        }
                    }

                    AnonimizarRecuperacion(conexion, transaccion, id, anonimo);

                    string identificadorDelTipo = TipoUsuario == TiposUsuario.Alumno ? "matrícula" : "número de empleado";
                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.Cuenta,
                        Accion = AccionDeCambio.Anonimizar,
                        Tabla = "usuarios",
                        RegistroId = id,
                        // Sin ningun dato suyo: el historial tambien lo lee la persona responsable de laboratorio.
                        Descripcion = "Cuenta de " + NombreDelTipo + " dada de baja: ahora es " + anonimo + ". Sus "
                            + (TipoUsuario == TiposUsuario.Alumno ? "bitácoras" : "clases, sesiones y bitácoras") + " se conservan sin su nombre.",
                        Antes = HistorialCambios.Datos("Tipo de cuenta", NombreDelTipo, "Estado", "de baja",
                            "Datos personales", "nombre, apellidos, correo y " + identificadorDelTipo),
                        Despues = HistorialCambios.Datos("Tipo de cuenta", NombreDelTipo, "Estado", "de baja",
                            "Identificador", anonimo, "Nombre", NombreAnonimo + " " + anonimo, "Correo", "(sin correo)", "Contraseña", "ya no sirve")
                    });
                    transaccion.Commit();
                }
                catch (MySqlException ex) when (ex.Number == ErrorClaveDuplicada)
                {
                    // Otra cuenta con ese identificador (escrito a mano en la base): no se toca nada.
                    transaccion.Rollback();
                    Log.Aviso("Anonimizar la cuenta id " + id + ": ya hay otra cuenta con el identificador " + anonimo + ".");
                    throw new ErrorDeDatos("Ya hay otra cuenta con el identificador " + anonimo + ", así que no se anonimizó nada. "
                        + "Pide a quien administra el servidor que revise esa cuenta.");
                }
                catch
                {
                    transaccion.Rollback();
                    throw;
                }
            }

            Log.Info("Cuenta del usuario id " + id + " (tipo " + TipoUsuario + ") anonimizada como " + anonimo + " por " + Quien()
                + (detallesVaciados > 0 ? "; se vacio el detalle de " + detallesVaciados + " asistencias dadas" : "") + ".");
        }

        /// <summary>
        /// Lo que comprueba Anonimizar, sin cambiar nada: la pantalla lo pregunta
        /// antes de pedir las confirmaciones, con la base de ahora (la lista puede
        /// ser de antes). ErrorDeDatos con el motivo si no se puede.
        /// </summary>
        public void ComprobarAnonimizar(int id)
        {
            ExigirTipoAnonimizable();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                ComprobarQueSePuedeAnonimizar(conexion, null, id);
            }
        }

        private void ExigirTipoAnonimizable()
        {
            if (TipoUsuario != TiposUsuario.Alumno && TipoUsuario != TiposUsuario.Profesor)
            {
                throw new InvalidOperationException("Solo se anonimizan cuentas de estudiantes y docentes (tipo " + TipoUsuario + ").");
            }
        }

        /// <summary>
        /// Que la cuenta este de baja y sin anonimizar, y lo que exija su tipo
        /// (ComprobarAntesDeAnonimizar). En la transaccion de Anonimizar la fila
        /// queda bloqueada hasta el final; sin transaccion, solo se lee.
        /// </summary>
        private void ComprobarQueSePuedeAnonimizar(MySqlConnection conexion, MySqlTransaction transaccion, int id)
        {
            if (!Esquema.TieneBajas(conexion, transaccion))
            {
                throw new ErrorDeDatos("La base de datos todavía no admite bajas (falta db/08_migracion_bajas.sql), y solo se anonimiza una cuenta dada de baja. "
                    + "Pide a quien administra el servidor que la aplique.");
            }

            string identificador;
            bool activa;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT numero_identificador, activo FROM usuarios WHERE idusuarios = @Id AND fk_tipo_usuario = @Tipo"
                + (transaccion == null ? "" : " FOR UPDATE"), conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                comando.Parameters.AddWithValue("@Tipo", TipoUsuario);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        throw new ErrorDeDatos("Esa cuenta ya no está en la base: actualiza la lista (F5).");
                    }

                    identificador = Texto(reader, "numero_identificador");
                    activa = Convert.ToInt32(reader["activo"]) == 1;
                }
            }

            if (string.Equals(identificador, IdentificadorAnonimo(id), StringComparison.OrdinalIgnoreCase))
            {
                throw new ErrorDeDatos("Esa cuenta ya estaba anonimizada.");
            }

            if (activa)
            {
                throw new ErrorDeDatos("Solo se anonimiza una cuenta dada de baja: primero dala de baja y después anonimízala.");
            }

            ComprobarAntesDeAnonimizar(conexion, transaccion, id);
        }

        /// <summary>Lo que cada tipo exige antes de anonimizar (en la transaccion, o sin ella al comprobar); lanza ErrorDeDatos si no se puede.</summary>
        protected virtual void ComprobarAntesDeAnonimizar(MySqlConnection conexion, MySqlTransaction transaccion, int id)
        {
        }

        /// <summary>
        /// Los codigos de "Olvidaste tu contrasena?" de la cuenta (db/13): su
        /// matricula tampoco se queda ahi, y los pendientes dejan de valer. Sin la
        /// tabla no hay nada; sin permiso (no deberia pasar), queda en el log.
        /// </summary>
        private static void AnonimizarRecuperacion(MySqlConnection conexion, MySqlTransaction transaccion, int id, string anonimo)
        {
            if (!Esquema.Recuperacion(conexion, transaccion))
            {
                return;
            }

            try
            {
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE recuperacion_contrasena SET matricula = @Anonimo, usado_en = COALESCE(usado_en, NOW()) WHERE usuario_id = @Id",
                    conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Anonimo", anonimo);
                    comando.Parameters.AddWithValue("@Id", id);
                    comando.ExecuteNonQuery();
                }
            }
            catch (MySqlException ex) when (ex.Number == 1142 || ex.Number == 1143)
            {
                Log.Aviso("Anonimizar la cuenta id " + id + ": no se pudieron anonimizar sus codigos de recuperacion: " + ex.Message);
            }
        }

        /// <summary>Columnas de estado para un SELECT de usuarios u, sirva o no la base para bajas.</summary>
        protected string ColumnasEstado(MySqlConnection conexion)
        {
            BajasDisponibles = Esquema.TieneBajas(conexion);
            return BajasDisponibles ? "u.activo, u.baja_en" : "1 AS activo, NULL AS baja_en";
        }

        /// <summary>Rellena los campos comunes de un usuario leido con ColumnasEstado.</summary>
        protected static void LeerComunes(MySqlDataReader reader, UsuarioInfo usuario)
        {
            usuario.Id = Convert.ToInt32(reader["idusuarios"]);
            usuario.Matricula = Texto(reader, "numero_identificador");
            usuario.Nombre = Texto(reader, "nombre");
            usuario.ApellidoPaterno = Texto(reader, "apellido_paterno");
            usuario.ApellidoMaterno = Texto(reader, "apellido_materno");
            usuario.Correo = Texto(reader, "correo");
            usuario.Activo = Convert.ToInt32(reader["activo"]) == 1;
            usuario.BajaEn = Fecha(reader, "baja_en");
        }

        private static void ParametrosUsuario(MySqlCommand comando, DatosUsuario datos)
        {
            comando.Parameters.AddWithValue("@Matricula", datos.Matricula);
            comando.Parameters.AddWithValue("@Nombre", datos.Nombre);
            comando.Parameters.AddWithValue("@Paterno", datos.ApellidoPaterno);
            // apellido_materno es NOT NULL: vacio. El correo, en cambio, NULL si no
            // hay: en la base del TESCHI es UNICO (correo_UNIQUE) y admite NULL, asi
            // que con "" el segundo usuario sin correo chocaba con el primero.
            comando.Parameters.AddWithValue("@Materno", datos.ApellidoMaterno ?? "");
            comando.Parameters.AddWithValue("@Correo", string.IsNullOrWhiteSpace(datos.Correo) ? (object)DBNull.Value : datos.Correo.Trim());
        }

        protected static string Quien()
        {
            // Sin nadie dentro, es el alta que hace el alumno desde el inicio de sesion.
            return Datos_User.getUser() ?? "el propio alumno en " + Environment.MachineName;
        }

        protected static string Texto(MySqlDataReader reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? "" : valor.ToString();
        }

        protected static int? Entero(MySqlDataReader reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? (int?)null : Convert.ToInt32(valor);
        }

        protected static DateTime? Fecha(MySqlDataReader reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor);
        }
    }

    /// <summary>
    /// "Mi perfil": la cuenta de quien tiene la sesion, de cualquier tipo, y solo
    /// la suya (su numero_identificador y su tipo). Escribe solo lo que la cuenta
    /// de MySQL de los equipos puede actualizar (db/07): nombre, apellidos,
    /// correo y contrasena, y la carrera y el semestre del alumno. Cambiar el
    /// correo (a donde llega el codigo de "Olvidaste tu contrasena?") o la
    /// contrasena pide la actual: si no, quien encuentre una sesion abierta se
    /// quedaria con la cuenta.
    ///
    /// No pasa por Permisos.Exigir: el encargado de laboratorio, en solo
    /// consulta, tambien cambia su correo y su contrasena (no su nombre). Su
    /// sesion va con la cuenta de solo lectura, asi que eso se escribe con la del
    /// equipo, como Consultas.MigrarContrasena, y solo en su fila. No muestra
    /// mensajes y nunca escribe una contrasena en el log.
    /// </summary>
    internal class MiPerfilDatos
    {
        private const int ErrorClaveDuplicada = 1062;
        // ER_SIGNAL_EXCEPTION: el disparador de db/07 no deja a la cuenta de los
        // equipos modificar una cuenta de administrador.
        private const int ErrorDelDisparador = 1644;
        private const int SinPermisoTabla = 1142;
        private const int SinPermisoColumna = 1143;
        private const int TablaInexistente = 1146;

        private readonly Conexion mconexion = new Conexion();
        private readonly string identificador = Datos_User.getUser();
        private readonly int tipo = Datos_User.TipoUsuario;

        /// <summary>usuarios.fk_tipo_usuario de la sesion (TiposUsuario); 0 sin sesion.</summary>
        public int Tipo
        {
            get { return tipo; }
        }

        /// <summary>El numero_identificador con el que entro: matricula, numero de empleado o usuario.</summary>
        public string Identificador
        {
            get { return identificador; }
        }

        /// <summary>El encargado de laboratorio solo cambia su correo y su contrasena: su nombre lo cambia el administrador.</summary>
        public bool CambiaSuNombre
        {
            get { return tipo != TiposUsuario.Encargado; }
        }

        private bool HaySesion
        {
            get { return !string.IsNullOrEmpty(identificador) && tipo >= TiposUsuario.Alumno && tipo <= TiposUsuario.Encargado; }
        }

        /// <summary>
        /// Sus datos; para el alumno, un AlumnoInfo con la carrera y el semestre de
        /// su ultima fila (como la lista de Alumnos). Null sin sesion o si su cuenta
        /// ya no esta. Con la cuenta de la sesion: leer tambien puede la de solo consulta.
        /// </summary>
        public UsuarioInfo Leer()
        {
            if (!HaySesion)
            {
                return null;
            }

            bool alumno = tipo == TiposUsuario.Alumno;
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT u.idusuarios, u.numero_identificador, u.nombre, u.apellido_paterno, u.apellido_materno, u.correo"
                + (alumno ? @", cgs.carreras_idcarreras, cgs.semestres_idsemestres
                FROM usuarios u
                LEFT JOIN carrera_grupo_semestre cgs ON cgs.idcarrera_grupo_semestre =
                    (SELECT MAX(x.idcarrera_grupo_semestre) FROM carrera_grupo_semestre x
                      WHERE x.usuarios_numero_identificador = u.idusuarios)" : @"
                FROM usuarios u") + @"
                WHERE u.numero_identificador = @Yo AND u.fk_tipo_usuario = @Tipo", conexion))
            {
                ParametrosDeLaCuenta(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    UsuarioInfo usuario = alumno
                        ? new AlumnoInfo { CarreraId = Entero(reader["carreras_idcarreras"]), SemestreId = Entero(reader["semestres_idsemestres"]) }
                        : new UsuarioInfo();
                    usuario.Id = Convert.ToInt32(reader["idusuarios"]);
                    usuario.Matricula = Texto(reader["numero_identificador"]);
                    usuario.Nombre = Texto(reader["nombre"]);
                    usuario.ApellidoPaterno = Texto(reader["apellido_paterno"]);
                    usuario.ApellidoMaterno = Texto(reader["apellido_materno"]);
                    usuario.Correo = Texto(reader["correo"]);
                    usuario.Activo = true;
                    return usuario;
                }
            }
        }

        /// <summary>
        /// Sus datos y, si nuevos.ContrasenaNueva trae una, su contrasena: todo o
        /// nada. Para el alumno, nuevos es un DatosAlumno (carrera y semestre). Si
        /// cambia el correo o la contrasena, contrasenaActual tiene que ser la
        /// buena: si no, ContrasenaIncorrecta. Lo que se le puede decir tal cual a
        /// quien lo intento sale como ErrorDeDatos.
        /// </summary>
        public void Guardar(DatosUsuario nuevos, string contrasenaActual)
        {
            if (!HaySesion)
            {
                throw new ErrorDeDatos("No hay ninguna sesión abierta: vuelve a entrar.");
            }

            // Solo la del encargado va con otra cuenta (la de solo consulta), y
            // entonces lo suyo se escribe con la del equipo.
            if (Conexion.ConCuentaDeSesion && tipo != TiposUsuario.Encargado)
            {
                throw new InvalidOperationException("Mi perfil: cuenta de sesion inesperada para el tipo " + tipo + ".");
            }

            bool conContrasena = !string.IsNullOrEmpty(nuevos.ContrasenaNueva);
            // Lo lento (PBKDF2) antes de empezar la transaccion.
            string hash = conContrasena ? Contrasenas.Hash(nuevos.ContrasenaNueva) : null;
            string correo = (nuevos.Correo ?? "").Trim();
            using (MySqlConnection conexion = Conexion.ConCuentaDeSesion ? Conexion.AbrirCadena(Conexion.CadenaDelEquipo()) : mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                try
                {
                    int id;
                    string almacenada;
                    string correoAntes;
                    using (MySqlCommand leer = new MySqlCommand(
                        "SELECT idusuarios, password, correo FROM usuarios WHERE numero_identificador = @Yo AND fk_tipo_usuario = @Tipo",
                        conexion, transaccion))
                    {
                        ParametrosDeLaCuenta(leer);
                        using (MySqlDataReader reader = leer.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                throw new ErrorDeDatos("No se encontró tu cuenta: cierra la sesión y vuelve a entrar.");
                            }

                            id = Convert.ToInt32(reader["idusuarios"]);
                            almacenada = reader["password"] == DBNull.Value ? null : reader["password"].ToString();
                            correoAntes = Texto(reader["correo"]).Trim();
                        }
                    }

                    bool cambiaCorreo = !string.Equals(correoAntes, correo, StringComparison.OrdinalIgnoreCase);
                    if (cambiaCorreo || conContrasena)
                    {
                        if (string.IsNullOrEmpty(contrasenaActual))
                        {
                            throw new ErrorDeDatos(conContrasena ? "Escribe tu contraseña actual." : "Para cambiar tu correo, escribe tu contraseña actual.");
                        }

                        bool necesitaRehash;
                        if (almacenada == null || !Contrasenas.Verificar(contrasenaActual, almacenada, out necesitaRehash))
                        {
                            throw new ContrasenaIncorrecta();
                        }
                    }

                    using (MySqlCommand comando = new MySqlCommand("UPDATE usuarios SET "
                        + (CambiaSuNombre ? "nombre = @Nombre, apellido_paterno = @Paterno, apellido_materno = @Materno, " : "")
                        + "correo = @Correo" + (conContrasena ? ", password = @Password" : "")
                        + " WHERE idusuarios = @Id AND numero_identificador = @Yo AND fk_tipo_usuario = @Tipo", conexion, transaccion))
                    {
                        ParametrosDeLaCuenta(comando);
                        comando.Parameters.AddWithValue("@Id", id);
                        comando.Parameters.AddWithValue("@Nombre", (nuevos.Nombre ?? "").Trim());
                        comando.Parameters.AddWithValue("@Paterno", (nuevos.ApellidoPaterno ?? "").Trim());
                        comando.Parameters.AddWithValue("@Materno", (nuevos.ApellidoMaterno ?? "").Trim());
                        // NULL sin correo: en la base de la escuela es UNICO (ver ParametrosUsuario).
                        comando.Parameters.AddWithValue("@Correo", correo.Length == 0 ? (object)DBNull.Value : correo);
                        if (conContrasena)
                        {
                            comando.Parameters.AddWithValue("@Password", hash);
                        }

                        comando.ExecuteNonQuery();
                    }

                    DatosAlumno alumno = nuevos as DatosAlumno;
                    if (tipo == TiposUsuario.Alumno && alumno != null)
                    {
                        new AlumnosDatos().GuardarExtrasPropios(conexion, transaccion, id, alumno);
                    }

                    if (cambiaCorreo || conContrasena)
                    {
                        AnularCodigos(conexion, transaccion, id);
                    }

                    transaccion.Commit();
                    Log.Info("Mi perfil: " + identificador + " (tipo " + tipo + ") guardo sus datos"
                        + (cambiaCorreo ? ", con otro correo" : "") + (conContrasena ? ", con contrasena nueva" : "") + ".");
                }
                catch (MySqlException ex) when (ex.Number == ErrorClaveDuplicada)
                {
                    transaccion.Rollback();
                    throw new ErrorDeDatos("Ese correo ya lo usa otra cuenta. Escribe otro o déjalo vacío.", CampoRepetido.Correo);
                }
                catch (MySqlException ex) when (ex.Number == ErrorDelDisparador)
                {
                    transaccion.Rollback();
                    Log.Aviso("Mi perfil de " + identificador + ": la base no deja cambiar esta cuenta desde este equipo (" + ex.Message + ").");
                    throw new ErrorDeDatos("Desde los equipos del laboratorio no se puede cambiar una cuenta de la administración: "
                        + "hazlo desde el equipo de la administración.");
                }
                catch (MySqlException ex) when (ex.Number == SinPermisoTabla || ex.Number == SinPermisoColumna)
                {
                    transaccion.Rollback();
                    Log.Error("Mi perfil de " + identificador + ": la cuenta de MySQL de este equipo no puede guardarlo (db/07)", ex);
                    throw new ErrorDeDatos("Este equipo no tiene permiso para guardar los cambios de tu cuenta. Avisa a la administración de DigBit.");
                }
                catch
                {
                    transaccion.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Los codigos de "Olvidaste tu contrasena?" pendientes dejan de valer
        /// (como en UsuariosDatos). La cuenta de los equipos no puede tocar esa
        /// tabla (db/13): ahi el codigo caduca solo a los 15 minutos. Sin la tabla
        /// (sin db/13) no hay nada que anular. Un error de permiso no deshace lo
        /// demas: MySQL solo rechaza esa orden.
        /// </summary>
        private void AnularCodigos(MySqlConnection conexion, MySqlTransaction transaccion, int id)
        {
            try
            {
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE recuperacion_contrasena SET usado_en = NOW() WHERE usuario_id = @Id AND usado_en IS NULL", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Id", id);
                    int anulados = comando.ExecuteNonQuery();
                    if (anulados > 0)
                    {
                        Log.Info("Mi perfil: anulados " + anulados + " codigos de recuperacion pendientes de " + identificador + ".");
                    }
                }
            }
            catch (MySqlException ex) when (ex.Number == SinPermisoTabla || ex.Number == TablaInexistente)
            {
                Log.Info("Mi perfil de " + identificador + ": los codigos de recuperacion pendientes no se anulan desde este equipo; caducan solos.");
            }
        }

        private void ParametrosDeLaCuenta(MySqlCommand comando)
        {
            comando.Parameters.AddWithValue("@Yo", identificador);
            comando.Parameters.AddWithValue("@Tipo", tipo);
        }

        private static string Texto(object valor)
        {
            return valor == null || valor == DBNull.Value ? "" : valor.ToString();
        }

        private static int? Entero(object valor)
        {
            return valor == null || valor == DBNull.Value ? (int?)null : Convert.ToInt32(valor);
        }
    }
}
