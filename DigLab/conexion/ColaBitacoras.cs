using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Xml.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Una bitacora guardada en el equipo mientras no habia servidor.</summary>
    public class BitacoraPendiente
    {
        public Guid Id = Guid.NewGuid();
        /// <summary>Cuando registro el alumno, en el reloj del servidor estimado.</summary>
        public DateTime Registrada;
        public string Matricula;
        public SesionLocal Sesion;
        public string Computadora;
        public string FallaRed;
        public string ComentarioRed;
        public string FallaHardware;
        public string ComentarioHardware;
        public string FallaSoftware;
        public string ComentarioSoftware;
        /// <summary>Otros equipos que vio sin internet ("11,13"); vacio si no aviso (db/10).</summary>
        public string EquiposSinInternet;
        /// <summary>
        /// True si el alumno entro CON contrasena (todo se valido en linea) y la
        /// bitacora se encolo porque la red se cayo al guardar. Se sube con
        /// registrado_sin_conexion = 0: esa marca dice "entro solo con su matricula".
        /// </summary>
        public bool IdentidadVerificada;
        /// <summary>Pasadas en las que fallo, por lo que fuera.</summary>
        public int Intentos;
        /// <summary>Errores de ESTA bitacora (no de red): con MaximoFallosPropios se aparta.</summary>
        public int IntentosPropios;
        public string UltimoError;
    }

    public class ResultadoSincronizacion
    {
        public int Enviadas;
        public int Duplicadas;
        public int ConError;
        public int Apartadas;
        public int Pendientes;
    }

    /// <summary>Error de una bitacora concreta que no se arregla reintentando (la matricula no existe).</summary>
    public class BitacoraImposibleException : Exception
    {
        public BitacoraImposibleException(string mensaje) : base(mensaje)
        {
        }
    }

    /// <summary>
    /// El INSERT no dijo ni "1 fila" ni "0 filas": el servidor lo aborto (KILL
    /// QUERY) y el driver devuelve -1. No se puede dar por enviada.
    /// </summary>
    public class EnvioIncompletoException : Exception
    {
        public EnvioIncompletoException(string mensaje) : base(mensaje)
        {
        }
    }

    /// <summary>De quien es un fallo al enviar una bitacora (ColaBitacoras.Clasificar).</summary>
    internal enum ClaseDeFallo
    {
        /// <summary>No se llega al servidor: red, servidor caido o apagandose, conexion perdida, credenciales.</summary>
        SinConexion,

        /// <summary>
        /// El servidor responde pero no deja escribir (falta una migracion, sin
        /// permisos, solo lectura, disco lleno, bloqueos) o no se sabe: se espera
        /// y se reintenta, sin culpar a la bitacora.
        /// </summary>
        DelServidor,

        /// <summary>De esa bitacora: reintentarla no la arregla (matricula que no existe, un dato que la base rechaza).</summary>
        DeLaBitacora
    }

    /// <summary>
    /// Cola de bitacoras registradas sin servidor (fase 4). Archivo XML en la
    /// carpeta de datos (CarpetaDatos, o %LOCALAPPDATA%\DigLab). La vacia el
    /// enviador en segundo plano (EnviadorBitacoras): cada una crea (o encuentra)
    /// su sesion y se inserta.
    ///
    /// La red va siempre FUERA del candado, que solo protege el archivo: asi
    /// encolar o contar pendientes desde la pantalla no espera nunca a un
    /// servidor lento. Cada bitacora sale de la cola en cuanto llega, no al final
    /// de la pasada. Un fallo de conexion corta la pasada (el enviador espera y
    /// reintenta); uno del servidor (sin permisos, solo lectura, falta una
    /// migracion, o uno que no se reconoce) se salta sin culpar a nadie; uno de
    /// la propia bitacora se le anota, y tras MaximoFallosPropios se aparta en
    /// bitacoras_rechazadas.xml en vez de reintentarse para siempre. Lo apartado
    /// nunca se borra solo. Ver Clasificar.
    /// </summary>
    internal static class ColaBitacoras
    {
        public const int MaximoFallosPropios = 5;

        private static readonly object candado = new object();
        private const string FmtHora = @"hh\:mm\:ss";

        public static string Ruta
        {
            get { return Path.Combine(CacheHorario.Carpeta, "bitacoras_pendientes.xml"); }
        }

        public static string RutaRechazadas
        {
            get { return Path.Combine(CacheHorario.Carpeta, "bitacoras_rechazadas.xml"); }
        }

        public static void Encolar(BitacoraPendiente bitacora)
        {
            lock (candado)
            {
                List<BitacoraPendiente> lista = Leer(Ruta);
                lista.Add(bitacora);
                Guardar(Ruta, lista);
            }

            Log.Info("Bitacora de " + bitacora.Matricula + " guardada en la cola local (" + bitacora.Sesion.Clave
                + (bitacora.IdentidadVerificada ? ", validada en linea" : "") + ").");
            EnviadorBitacoras.Despertar();
        }

        /// <summary>
        /// Si el alumno ya tiene una bitacora de esa sesion en la cola. Si el
        /// archivo no se puede leer en ese momento, false: lo peor es una copia
        /// repetida en la cola, y al subir el NOT EXISTS la descarta.
        /// </summary>
        public static bool YaRegistrada(string claveSesion, string matricula)
        {
            lock (candado)
            {
                try
                {
                    return Leer(Ruta).Any(b => b.Sesion.Clave == claveSesion && string.Equals(b.Matricula, matricula, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex) when (EsDeAcceso(ex))
                {
                    Log.Aviso("No se pudo leer " + Ruta + " para ver si " + matricula + " ya registro: " + ex.Message);
                    return false;
                }
            }
        }

        private static int ultimasPendientes;
        private static int ultimasRechazadas;

        /// <summary>
        /// Cuantas faltan por enviar. La pantalla lo pregunta cada pocos segundos:
        /// si el archivo esta ocupado un momento (antivirus, copia de seguridad),
        /// se devuelve lo ultimo que se supo en vez de lanzar.
        /// </summary>
        public static int Pendientes()
        {
            lock (candado)
            {
                try
                {
                    ultimasPendientes = Leer(Ruta).Count;
                }
                catch (Exception ex) when (EsDeAcceso(ex))
                {
                    Log.Aviso("No se pudo leer " + Ruta + " para contar las pendientes: " + ex.Message);
                }

                return ultimasPendientes;
            }
        }

        public static int Rechazadas()
        {
            lock (candado)
            {
                try
                {
                    ultimasRechazadas = Leer(RutaRechazadas).Count;
                }
                catch (Exception ex) when (EsDeAcceso(ex))
                {
                    Log.Aviso("No se pudo leer " + RutaRechazadas + " para contar las apartadas: " + ex.Message);
                }

                return ultimasRechazadas;
            }
        }

        /// <summary>
        /// Sube la cola. Lo llama solo el enviador, desde su hilo. Lanza si el
        /// fallo es de red o del servidor (lo que se envio antes ya salio de la
        /// cola); los fallos de una bitacora concreta los cuenta y sigue.
        /// </summary>
        public static ResultadoSincronizacion Sincronizar()
        {
            ResultadoSincronizacion resultado = new ResultadoSincronizacion();
            List<BitacoraPendiente> lote;
            lock (candado)
            {
                lote = Leer(Ruta);
            }

            if (lote.Count > 0)
            {
                using (MySqlConnection conexion = new Conexion().GetConexionParaEnvio())
                {
                    bool avisadoServidor = false;
                    foreach (BitacoraPendiente b in lote)
                    {
                        EstadoEnvio estado;
                        try
                        {
                            estado = Enviar(conexion, b);
                        }
                        catch (Exception ex) when (Clasificar(ex) == ClaseDeFallo.DeLaBitacora)
                        {
                            resultado.ConError++;
                            if (AnotarFalloPropio(b.Id, ex.Message))
                            {
                                resultado.Apartadas++;
                            }

                            Log.Error("Enviar la bitacora pendiente de " + b.Matricula + " (" + b.Sesion.Clave + ")", ex);
                            continue;
                        }
                        catch (Exception ex) when (Clasificar(ex) == ClaseDeFallo.DelServidor)
                        {
                            // El servidor no deja escribir (o no se sabe por que): no es
                            // culpa de esta bitacora, no se le anota, y se sigue con las
                            // demas por si a alguna le va bien. Se reintenta en la
                            // siguiente pasada. Un fallo de conexion, en cambio, corta
                            // la pasada (sale de aqui como excepcion).
                            resultado.ConError++;
                            if (!avisadoServidor)
                            {
                                avisadoServidor = true;
                                Log.Error("Enviar la bitacora pendiente de " + b.Matricula + " (" + b.Sesion.Clave
                                    + "); el servidor la rechaza y se reintenta despues sin apartarla", ex);
                            }

                            continue;
                        }

                        Quitar(b.Id);
                        if (estado == EstadoEnvio.Enviada)
                        {
                            resultado.Enviadas++;
                        }
                        else
                        {
                            resultado.Duplicadas++;
                        }
                    }
                }
            }

            resultado.Pendientes = Pendientes();
            if (resultado.Enviadas + resultado.Duplicadas + resultado.ConError > 0)
            {
                Log.Info("Sincronizacion de bitacoras: " + resultado.Enviadas + " enviadas, " + resultado.Duplicadas + " ya existian, "
                    + resultado.ConError + " con error" + (resultado.Apartadas > 0 ? " (" + resultado.Apartadas + " apartadas)" : "")
                    + ", " + resultado.Pendientes + " pendientes.");
            }

            return resultado;
        }

        /// <summary>
        /// De quien es un fallo. Solo es de la bitacora lo que se sabe que es suyo
        /// (la matricula no existe, un dato que la base rechaza); lo demas, y lo
        /// que no se reconoce, es del servidor o de la red y no se le anota: un
        /// servidor en solo lectura o con el disco lleno no puede acabar apartando
        /// todas las bitacoras del laboratorio.
        /// </summary>
        internal static ClaseDeFallo Clasificar(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is BitacoraImposibleException)
                {
                    return ClaseDeFallo.DeLaBitacora;
                }

                if (e is SocketException || e is IOException || e is TimeoutException)
                {
                    return ClaseDeFallo.SinConexion;
                }

                MySqlException mysql = e as MySqlException;
                if (mysql != null)
                {
                    switch (mysql.Number)
                    {
                        case 0:     // sin conexion / error del driver
                        case 1040:  // demasiadas conexiones
                        case 1042:  // no se llega al servidor
                        case 1043:  // saludo fallido
                        case 1044:  // sin acceso a la base
                        case 1045:  // usuario o contrasena
                        case 1053:  // el servidor se esta apagando
                        case 1129:  // equipo bloqueado por max_connect_errors
                        case 1130:  // equipo sin permiso para conectar
                        case 2006:  // se fue el servidor
                        case 2013:  // se perdio la conexion
                            return ClaseDeFallo.SinConexion;
                        case 1048:  // un dato obligatorio vacio
                        case 1264:  // numero fuera de rango
                        case 1265:  // dato truncado
                        case 1292:  // fecha u hora no valida
                        case 1366:  // texto con caracteres que la columna no admite
                        case 1406:  // dato demasiado largo
                        case 1452:  // el alumno o la sesion ya no existen
                            return ClaseDeFallo.DeLaBitacora;
                        default:
                            // 1054/1146 (falta una migracion), 1142/1143 (permisos),
                            // 1205/1213 (bloqueos), 1290 (solo lectura), disco lleno...
                            return ClaseDeFallo.DelServidor;
                    }
                }

                InvalidOperationException operacion = e as InvalidOperationException;
                if (operacion != null && operacion.Message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ClaseDeFallo.SinConexion;
                }
            }

            return ClaseDeFallo.DelServidor;
        }

        private enum EstadoEnvio { Enviada, Duplicada }

        private static EstadoEnvio Enviar(MySqlConnection conexion, BitacoraPendiente b)
        {
            int idUsuario;
            using (MySqlCommand comando = new MySqlCommand("SELECT idusuarios FROM usuarios WHERE numero_identificador = @M", conexion))
            {
                comando.Parameters.AddWithValue("@M", b.Matricula);
                object r = comando.ExecuteScalar();
                if (r == null)
                {
                    throw new BitacoraImposibleException("La matricula " + b.Matricula + " no existe en el servidor.");
                }

                idUsuario = Convert.ToInt32(r);
            }

            int idSesion = b.Sesion.IdSesionServidor.HasValue && ExisteSesion(conexion, b.Sesion.IdSesionServidor.Value)
                ? b.Sesion.IdSesionServidor.Value
                : b.Sesion.EsUsoLibre ? SesionDeUsoLibre(conexion, b, idUsuario)
                : ObtenerOCrearSesion(conexion, b.Sesion);

            bool conAviso = !string.IsNullOrEmpty(b.EquiposSinInternet) && Esquema.EquiposSinInternet(conexion);
            if (!string.IsNullOrEmpty(b.EquiposSinInternet) && !conAviso)
            {
                Log.Aviso("La bitacora de " + b.Matricula + " avisaba de equipos sin internet (" + b.EquiposSinInternet
                    + "), pero la base aun no tiene donde guardarlo (falta db/10). La bitacora se envia igual.");
            }

            bool conLlegada = Esquema.HoraDeLlegada(conexion);

            try
            {
                // Solo si no existe ya. Aqui importa mas que en linea: si la red se
                // cae despues de insertar pero antes de la respuesta, la cola lo
                // reintenta, y sin esta comprobacion la bitacora entraria dos veces.
                // Tambien lo evita el UNIQUE (fk_codigo_accesos, fk_usuario) de la 03,
                // pero una base con duplicados viejos (la base vieja de la escuela, con
                // pares de 2024) no lo tiene: la 03 no lo pone si los hay. Donde si
                // esta, salta el 1062 de abajo.
                using (MySqlCommand comando = new MySqlCommand(@"
                    INSERT INTO registros_bitacoras
                        (fk_codigo_accesos, fk_usuario, numero_computadora, falla_red, comentarios_red, falla_hardware, comentarios_hardware,
                         falla_software, comentarios_software, registrado_sin_conexion, registrado_en"
                        + (conAviso ? ", equipos_sin_internet" : "") + (conLlegada ? ", recibido_en" : "") + @")
                    SELECT @S, @U, @C, @FR, @CR, @FH, @CH, @FS, @CS, @SinConexion, @En"
                        + (conAviso ? ", @Equipos" : "") + (conLlegada ? ", NOW()" : "") + @"
                    FROM DUAL
                    WHERE NOT EXISTS (SELECT 1 FROM registros_bitacoras WHERE fk_codigo_accesos = @S AND fk_usuario = @U)", conexion))
                {
                    comando.Parameters.AddWithValue("@S", idSesion);
                    comando.Parameters.AddWithValue("@U", idUsuario);
                    comando.Parameters.AddWithValue("@C", b.Computadora);
                    comando.Parameters.AddWithValue("@FR", b.FallaRed);
                    comando.Parameters.AddWithValue("@CR", b.ComentarioRed);
                    comando.Parameters.AddWithValue("@FH", b.FallaHardware);
                    comando.Parameters.AddWithValue("@CH", b.ComentarioHardware);
                    comando.Parameters.AddWithValue("@FS", b.FallaSoftware);
                    comando.Parameters.AddWithValue("@CS", b.ComentarioSoftware);
                    comando.Parameters.AddWithValue("@SinConexion", b.IdentidadVerificada ? 0 : 1);
                    comando.Parameters.AddWithValue("@En", b.Registrada);
                    comando.Parameters.AddWithValue("@Equipos", b.EquiposSinInternet ?? "");
                    int filas = comando.ExecuteNonQuery();
                    if (filas == 0)
                    {
                        Log.Aviso("Bitacora pendiente de " + b.Matricula + " descartada: ya existia en la sesion " + idSesion + ".");
                        return EstadoEnvio.Duplicada;
                    }

                    if (filas != 1)
                    {
                        // -1: el servidor aborto la orden (KILL QUERY). No llego.
                        throw new EnvioIncompletoException("El servidor no confirmo la bitacora de " + b.Matricula + " (respuesta " + filas + ").");
                    }
                }

                return EstadoEnvio.Enviada;
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                // Ya habia una bitacora de ese alumno en esa sesion (por ejemplo la
                // registro en linea antes de que se cayera la red).
                Log.Aviso("Bitacora pendiente de " + b.Matricula + " descartada: ya existia en la sesion " + idSesion + ".");
                return EstadoEnvio.Duplicada;
            }
        }

        private static bool ExisteSesion(MySqlConnection conexion, int idSesion)
        {
            return Existe(conexion, "SELECT 1 FROM codigos_accesos WHERE idcodigos_accesos = @Id", idSesion);
        }

        /// <summary>
        /// Uso libre registrado sin conexion (db/14): la sesion de uso libre del
        /// laboratorio y dia que cubre la hora del registro, o una nueva con el
        /// tramo que calculo la copia local.
        /// </summary>
        private static int SesionDeUsoLibre(MySqlConnection conexion, BitacoraPendiente b, int idUsuario)
        {
            SesionLocal s = b.Sesion;
            if (!Esquema.UsoLibre(conexion))
            {
                throw new BitacoraImposibleException("La bitacora de uso libre de " + b.Matricula + " no se puede guardar: la base no admite "
                    + "sesiones sin clase (falta db/14_registro_sin_clase.sql).");
            }

            // La hora del registro (reloj del servidor estimado) si es de ese dia y
            // cae en el tramo; si no, el inicio del tramo.
            TimeSpan momento = b.Registrada.Date == s.Fecha.Date && b.Registrada.TimeOfDay >= s.Inicio && b.Registrada.TimeOfDay <= s.Fin
                ? b.Registrada.TimeOfDay : s.Inicio;
            // El tramo de esa hora segun el horario de AHORA en el servidor: si mientras
            // el equipo estaba sin red se puso una clase en medio, la bitacora no cae
            // en la sesion del tramo de antes (donde, si el alumno ya tenia una, se
            // descartaba como repetida). Si ahora hay una clase justo a esa hora (se
            // puso despues), su propia sesion desde esa hora: el tramo que vio el equipo
            // pudo abarcar otros y caer en la sesion de uno de ellos.
            TramoLibre tramo = HorariosDatos.TramoLibreEn(conexion, s.LaboratorioId, s.Fecha, momento)
                ?? new TramoLibre { Inicio = momento, Fin = momento };
            return HorariosDatos.ObtenerOCrearSesionLibre(conexion, s.LaboratorioId, s.Fecha, tramo, momento, idUsuario);
        }

        /// <summary>
        /// La sesion de la franja/extra en esa fecha. Si la franja o la extra ya no
        /// existen en el servidor, la sesion se crea sin enlace (queda como
        /// historial, igual que las de antes de la fase 6). Una franja que ese dia
        /// no se da (terminada o aun sin empezar, db/20) cede su sitio a la que la
        /// sustituye (FranjaQueSeDa), y la sesion nueva lleva las horas de esa, no
        /// las de la copia vieja; si no hay, la sesion va sin enlace.
        /// </summary>
        private static int ObtenerOCrearSesion(MySqlConnection conexion, SesionLocal s)
        {
            TimeSpan inicio = s.Inicio, fin = s.Fin;
            int? franja = s.FranjaId.HasValue ? FranjaQueSeDa(conexion, s, ref inicio, ref fin) : null;
            bool franjaExiste = franja.HasValue;
            bool extraExiste = s.ExcepcionId.HasValue && Existe(conexion, "SELECT 1 FROM horario_excepciones WHERE idhorario_excepciones = @Id", s.ExcepcionId.Value);

            // La de respaldo no tiene un UNIQUE detras: con ORDER BY, todos los
            // reintentos eligen la misma aunque dos equipos hubieran creado dos.
            string buscar = franjaExiste
                ? "SELECT idcodigos_accesos FROM codigos_accesos WHERE horarios_idhorarios = @Id AND fecha = @Fecha"
                : extraExiste
                    ? "SELECT idcodigos_accesos FROM codigos_accesos WHERE horario_excepciones_id = @Id"
                    : "SELECT idcodigos_accesos FROM codigos_accesos WHERE codigo = @Codigo AND fecha = @Fecha AND hora_entrada = @Inicio AND laboratorios_idlaboratorios = @Lab ORDER BY idcodigos_accesos LIMIT 1";

            using (MySqlCommand comando = new MySqlCommand(buscar, conexion))
            {
                comando.Parameters.AddWithValue("@Id", franjaExiste ? franja.Value : s.ExcepcionId.GetValueOrDefault());
                comando.Parameters.AddWithValue("@Fecha", s.Fecha.Date);
                comando.Parameters.AddWithValue("@Codigo", s.Codigo);
                comando.Parameters.AddWithValue("@Inicio", HorariosDatos.HoraParaGuardar(s.Inicio));
                comando.Parameters.AddWithValue("@Lab", s.LaboratorioId);
                object r = comando.ExecuteScalar();
                if (r != null)
                {
                    return Convert.ToInt32(r);
                }
            }

            // Fuera del codigo fijo (db/11) una sesion enlazada nueva nunca lleva el
            // fijo de la copia ni lo que se tecleo: con generacion de codigos, el
            // generado de esa clase y fecha (el que ve el administrador); sin codigo
            // (o sin secreto), uno al azar. Las que van sin enlace siguen con
            // s.Codigo, que es como la encuentra la busqueda de respaldo de arriba
            // en los reintentos.
            string codigo = s.Codigo;
            ModoCodigo modo = franjaExiste || extraExiste ? ModosDeAcceso.Leer(conexion, s.LaboratorioId) : ModoCodigo.Fijo;
            if (modo != ModoCodigo.Fijo)
            {
                codigo = (modo == ModoCodigo.Renovable
                        ? CodigosGenerados.ParaSesion(conexion, !franjaExiste, franjaExiste ? franja.Value : s.ExcepcionId.Value, s.Fecha)
                        : null)
                    ?? CodigosDeSesion.NuevoLibre(conexion, s.Fecha);
            }

            try
            {
                using (MySqlCommand comando = new MySqlCommand(@"
                    INSERT INTO codigos_accesos
                        (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos,
                         laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios, horario_excepciones_id)
                    VALUES (@Codigo, NOW(), @Fecha, @Inicio, @Fin, @Materia, @Grupo, @Lab, @Profesor, @Franja, @Excepcion);
                    SELECT LAST_INSERT_ID();", conexion))
                {
                    comando.Parameters.AddWithValue("@Codigo", codigo);
                    comando.Parameters.AddWithValue("@Fecha", s.Fecha.Date);
                    comando.Parameters.AddWithValue("@Inicio", HorariosDatos.HoraParaGuardar(inicio));
                    comando.Parameters.AddWithValue("@Fin", HorariosDatos.HoraParaGuardar(fin));
                    comando.Parameters.AddWithValue("@Materia", s.MateriaId);
                    comando.Parameters.AddWithValue("@Grupo", s.GrupoId);
                    comando.Parameters.AddWithValue("@Lab", s.LaboratorioId);
                    comando.Parameters.AddWithValue("@Profesor", s.ProfesorId);
                    comando.Parameters.AddWithValue("@Franja", franjaExiste ? (object)franja.Value : DBNull.Value);
                    comando.Parameters.AddWithValue("@Excepcion", extraExiste ? (object)s.ExcepcionId.Value : DBNull.Value);
                    return Convert.ToInt32(comando.ExecuteScalar());
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                // La creo otro equipo mientras tanto.
                using (MySqlCommand comando = new MySqlCommand(buscar, conexion))
                {
                    comando.Parameters.AddWithValue("@Id", franjaExiste ? franja.Value : s.ExcepcionId.GetValueOrDefault());
                    comando.Parameters.AddWithValue("@Fecha", s.Fecha.Date);
                    comando.Parameters.AddWithValue("@Codigo", s.Codigo);
                    comando.Parameters.AddWithValue("@Inicio", HorariosDatos.HoraParaGuardar(s.Inicio));
                    comando.Parameters.AddWithValue("@Lab", s.LaboratorioId);
                    return Convert.ToInt32(comando.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// La franja a la que se enlaza la sesion de una bitacora de la cola: la
        /// suya, si ese dia se da (ese dia de la semana, clase activa, dentro de
        /// la vigencia de la clase y de la propia de la franja, db/20). Si no se
        /// da (la copia del equipo era de antes del cambio y estuvo dias sin red),
        /// la de la misma clase y laboratorio que ese dia si se da, cuyas horas
        /// cubren el inicio de la sesion; entonces inicio y fin pasan a ser las
        /// horas de esa (las de la copia eran las de la franja vieja). Si no hay,
        /// o la franja ya no existe, null: la sesion va sin enlace, con sus datos,
        /// y Faltas y la asistencia la reconocen por profesor, materia, grupo y
        /// horas. Nunca se enlaza a una franja que ese dia no se da.
        /// </summary>
        private static int? FranjaQueSeDa(MySqlConnection conexion, SesionLocal s, ref TimeSpan inicio, ref TimeSpan fin)
        {
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
            int claseDeLaFranja;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT h.clases_idclases, (h.dia_semana = WEEKDAY(@Fecha) + 1 AND " + VigenciaFranjas.SeDa(conVigencia, "@Fecha") + ") AS se_da"
                + " FROM horarios h INNER JOIN clases c ON c.idclases = h.clases_idclases WHERE h.idhorarios = @Id", conexion))
            {
                comando.Parameters.AddWithValue("@Id", s.FranjaId.Value);
                comando.Parameters.AddWithValue("@Fecha", s.Fecha.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    claseDeLaFranja = reader.GetInt32(0);
                    if (!reader.IsDBNull(1) && Convert.ToInt32(reader[1]) == 1)
                    {
                        return s.FranjaId.Value;
                    }
                }
            }

            // La copia trae la clase (las bitacoras validadas en linea, no: entonces la de la franja).
            int clase = s.ClaseId > 0 ? s.ClaseId : claseDeLaFranja;
            int? sucesora = null;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT h.idhorarios, h.hora_inicio, h.hora_fin FROM horarios h INNER JOIN clases c ON c.idclases = h.clases_idclases"
                + " WHERE h.clases_idclases = @Clase AND h.laboratorios_idlaboratorios = @Lab AND h.idhorarios <> @Id"
                + " AND h.dia_semana = WEEKDAY(@Fecha) + 1 AND " + VigenciaFranjas.SeDa(conVigencia, "@Fecha")
                + " AND h.hora_inicio <= @Inicio AND h.hora_fin > @Inicio"
                + " ORDER BY h.idhorarios DESC LIMIT 1", conexion))
            {
                comando.Parameters.AddWithValue("@Clase", clase);
                comando.Parameters.AddWithValue("@Lab", s.LaboratorioId);
                comando.Parameters.AddWithValue("@Id", s.FranjaId.Value);
                comando.Parameters.AddWithValue("@Fecha", s.Fecha.Date);
                comando.Parameters.AddWithValue("@Inicio", HorariosDatos.HoraParaGuardar(s.Inicio));
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        sucesora = reader.GetInt32(0);
                        inicio = reader.GetTimeSpan(1);
                        fin = reader.GetTimeSpan(2);
                    }
                }
            }

            Log.Info("Bitacora de la cola: la franja " + s.FranjaId.Value + " no se da el " + s.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + (sucesora.HasValue ? "; su sesion va a la franja " + sucesora.Value + ", que la sustituye (" + HorariosDatos.Hora(inicio) + "-"
                    + HorariosDatos.Hora(fin) + ")." : "; su sesion va sin enlace a franja."));
            return sucesora;
        }

        private static bool Existe(MySqlConnection conexion, string consulta, int id)
        {
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            {
                comando.Parameters.AddWithValue("@Id", id);
                return comando.ExecuteScalar() != null;
            }
        }

        // ---------------------------------------------------------------------
        // Disco
        // ---------------------------------------------------------------------

        /// <summary>Saca de la cola una bitacora que ya llego.</summary>
        private static void Quitar(Guid id)
        {
            lock (candado)
            {
                List<BitacoraPendiente> lista = Leer(Ruta);
                if (lista.RemoveAll(b => b.Id == id) > 0)
                {
                    Guardar(Ruta, lista);
                }
            }
        }

        /// <summary>Le anota un error suyo; con MaximoFallosPropios la aparta. True si la aparto.</summary>
        private static bool AnotarFalloPropio(Guid id, string error)
        {
            lock (candado)
            {
                List<BitacoraPendiente> lista = Leer(Ruta);
                BitacoraPendiente b = lista.FirstOrDefault(x => x.Id == id);
                if (b == null)
                {
                    return false;
                }

                b.Intentos++;
                b.IntentosPropios++;
                b.UltimoError = error;
                bool apartar = b.IntentosPropios >= MaximoFallosPropios;
                if (apartar)
                {
                    lista.Remove(b);
                    List<BitacoraPendiente> rechazadas = Leer(RutaRechazadas);
                    rechazadas.Add(b);
                    Guardar(RutaRechazadas, rechazadas);
                    Log.Aviso("Bitacora de " + b.Matricula + " (" + b.Sesion.Clave + ") apartada en " + RutaRechazadas + " tras "
                        + b.IntentosPropios + " intentos: " + error + " No se borra: revisala a mano.");
                }

                Guardar(Ruta, lista);
                return apartar;
            }
        }

        /// <summary>
        /// Un archivo de la cola. Si no se puede LEER (otro programa lo tiene
        /// abierto, sin permiso) lanza: devolver una lista vacia haria que el
        /// siguiente Guardar machacara las bitacoras que hay dentro. Solo si esta
        /// ROTO lo aparta como .corrupto, y rescata la version anterior si la hay.
        /// </summary>
        private static List<BitacoraPendiente> Leer(string ruta)
        {
            if (!File.Exists(ruta))
            {
                return new List<BitacoraPendiente>();
            }

            try
            {
                return Interpretar(ruta);
            }
            catch (Exception ex) when (EsArchivoRoto(ex))
            {
                string apartado = ruta + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".corrupto";
                Log.Error("Leer " + ruta + "; esta roto y se aparta como " + apartado, ex);

                // Si ni eso se puede, que lance: mejor reintentar que perderlo.
                File.Move(ruta, apartado);

                string anterior = ruta + ".anterior";
                if (File.Exists(anterior))
                {
                    try
                    {
                        List<BitacoraPendiente> rescatadas = Interpretar(anterior);
                        File.Copy(anterior, ruta);
                        Log.Aviso("Se recuperan " + rescatadas.Count + " bitacoras de la version anterior de " + ruta
                            + " (puede faltar el ultimo cambio; el apartado se conserva).");
                        return rescatadas;
                    }
                    catch (Exception exAnterior) when (EsArchivoRoto(exAnterior))
                    {
                        Log.Error("La version anterior de " + ruta + " tambien esta rota", exAnterior);
                    }
                }

                return new List<BitacoraPendiente>();
            }
        }

        /// <summary>No se pudo abrir el archivo (no es que este roto): se reintenta despues.</summary>
        private static bool EsDeAcceso(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException;
        }

        /// <summary>El archivo se abrio pero su contenido no se entiende.</summary>
        private static bool EsArchivoRoto(Exception ex)
        {
            return ex is System.Xml.XmlException || ex is FormatException || ex is InvalidCastException
                || ex is ArgumentException || ex is NullReferenceException || ex is OverflowException;
        }

        private static List<BitacoraPendiente> Interpretar(string ruta)
        {
            // Lo que se anadio despues de la fase 4 se lee como opcional: una cola
            // escrita por la version anterior tiene que seguir cargando (si no,
            // iria entera a .corrupto al actualizar DigLab).
            return XDocument.Load(ruta).Root.Elements("bitacora").Select(e => new BitacoraPendiente
            {
                Id = Guid.Parse((string)e.Attribute("id")),
                Registrada = DateTime.Parse((string)e.Attribute("registrada"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Matricula = (string)e.Attribute("matricula"),
                Computadora = (string)e.Attribute("computadora"),
                FallaRed = (string)e.Attribute("fallaRed"),
                ComentarioRed = (string)e.Attribute("comentarioRed"),
                FallaHardware = (string)e.Attribute("fallaHardware"),
                ComentarioHardware = (string)e.Attribute("comentarioHardware"),
                FallaSoftware = (string)e.Attribute("fallaSoftware"),
                ComentarioSoftware = (string)e.Attribute("comentarioSoftware"),
                EquiposSinInternet = (string)e.Attribute("equiposSinInternet") ?? "",
                IdentidadVerificada = (bool?)e.Attribute("verificada") ?? false,
                Intentos = (int?)e.Attribute("intentos") ?? 0,
                IntentosPropios = (int?)e.Attribute("propios") ?? 0,
                UltimoError = (string)e.Attribute("ultimoError"),
                Sesion = LeerSesion(e.Element("sesion"))
            }).ToList();
        }

        private static SesionLocal LeerSesion(XElement e)
        {
            return new SesionLocal
            {
                Codigo = (string)e.Attribute("codigo"),
                ClaseId = (int)e.Attribute("claseId"),
                FranjaId = string.IsNullOrEmpty((string)e.Attribute("franjaId")) ? (int?)null : (int)e.Attribute("franjaId"),
                ExcepcionId = string.IsNullOrEmpty((string)e.Attribute("excepcionId")) ? (int?)null : (int)e.Attribute("excepcionId"),
                Fecha = DateTime.ParseExact((string)e.Attribute("fecha"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Inicio = TimeSpan.ParseExact((string)e.Attribute("inicio"), FmtHora, CultureInfo.InvariantCulture),
                Fin = TimeSpan.ParseExact((string)e.Attribute("fin"), FmtHora, CultureInfo.InvariantCulture),
                LaboratorioId = (int)e.Attribute("laboratorioId"),
                Laboratorio = (string)e.Attribute("laboratorio"),
                MateriaId = (int)e.Attribute("materiaId"),
                Materia = (string)e.Attribute("materia"),
                GrupoId = (int)e.Attribute("grupoId"),
                Grupo = (string)e.Attribute("grupo"),
                ProfesorId = (int)e.Attribute("profesorId"),
                Profesor = (string)e.Attribute("profesor"),
                IdSesionServidor = (int?)e.Attribute("idSesion")
            };
        }

        private static void Guardar(string ruta, List<BitacoraPendiente> lista)
        {
            Directory.CreateDirectory(CacheHorario.Carpeta);
            XElement raiz = new XElement("pendientes", lista.Select(b => new XElement("bitacora",
                new XAttribute("id", b.Id.ToString()),
                new XAttribute("registrada", b.Registrada.ToString("o", CultureInfo.InvariantCulture)),
                new XAttribute("matricula", b.Matricula ?? ""),
                new XAttribute("computadora", b.Computadora ?? ""),
                new XAttribute("fallaRed", b.FallaRed ?? ""),
                new XAttribute("comentarioRed", b.ComentarioRed ?? ""),
                new XAttribute("fallaHardware", b.FallaHardware ?? ""),
                new XAttribute("comentarioHardware", b.ComentarioHardware ?? ""),
                new XAttribute("fallaSoftware", b.FallaSoftware ?? ""),
                new XAttribute("comentarioSoftware", b.ComentarioSoftware ?? ""),
                new XAttribute("equiposSinInternet", b.EquiposSinInternet ?? ""),
                new XAttribute("verificada", b.IdentidadVerificada),
                new XAttribute("intentos", b.Intentos),
                new XAttribute("propios", b.IntentosPropios),
                new XAttribute("ultimoError", b.UltimoError ?? ""),
                new XElement("sesion",
                    new XAttribute("codigo", b.Sesion.Codigo ?? ""),
                    new XAttribute("claseId", b.Sesion.ClaseId),
                    new XAttribute("franjaId", b.Sesion.FranjaId.HasValue ? b.Sesion.FranjaId.Value.ToString() : ""),
                    new XAttribute("excepcionId", b.Sesion.ExcepcionId.HasValue ? b.Sesion.ExcepcionId.Value.ToString() : ""),
                    new XAttribute("fecha", b.Sesion.Fecha.ToString("yyyy-MM-dd")),
                    new XAttribute("inicio", b.Sesion.Inicio.ToString(FmtHora)),
                    new XAttribute("fin", b.Sesion.Fin.ToString(FmtHora)),
                    new XAttribute("laboratorioId", b.Sesion.LaboratorioId),
                    new XAttribute("laboratorio", b.Sesion.Laboratorio ?? ""),
                    new XAttribute("materiaId", b.Sesion.MateriaId),
                    new XAttribute("materia", b.Sesion.Materia ?? ""),
                    new XAttribute("grupoId", b.Sesion.GrupoId),
                    new XAttribute("grupo", b.Sesion.Grupo ?? ""),
                    new XAttribute("profesorId", b.Sesion.ProfesorId),
                    new XAttribute("profesor", b.Sesion.Profesor ?? ""),
                    b.Sesion.IdSesionServidor.HasValue ? new XAttribute("idSesion", b.Sesion.IdSesionServidor.Value) : null))));

            // Al disco de verdad antes de sustituir: con un corte de luz justo
            // despues, el archivo nuevo podia quedar vacio (lleno de ceros) y con
            // el se iba toda la cola. La version que se sustituye queda como
            // .anterior, y Leer la rescata si la nueva saliera rota.
            string temporal = ruta + ".tmp";
            using (FileStream flujo = new FileStream(temporal, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new XDocument(raiz).Save(flujo);
                flujo.Flush(true);
            }

            if (File.Exists(ruta))
            {
                File.Replace(temporal, ruta, ruta + ".anterior");
            }
            else
            {
                File.Move(temporal, ruta);
            }
        }
    }

    /// <summary>
    /// Lo que se le dice a la persona cuando algo falla (RNF12): que no se pudo,
    /// por que y que hacer, en espanol y sin el texto del driver ni nada en
    /// ingles (eso va al log con Log.Error, que hace quien llama). La red solo se
    /// culpa cuando de verdad no se llego al servidor (ColaBitacoras.Clasificar):
    /// un permiso o una migracion que falta no se arreglan revisando el cable.
    /// Los avisos que DigLab ya escribe para las personas (solo consulta, un dato
    /// repetido, la API de recuperacion) se dicen tal cual.
    /// </summary>
    internal static class MensajeDeFallo
    {
        /// <summary>En los equipos del laboratorio: estudiantes y docentes.</summary>
        public const string AvisaAlLaboratorio = "avisa a la persona responsable del laboratorio";

        /// <summary>Lo que solo puede arreglar la administracion de DigLab.</summary>
        public const string AvisaALaAdministracion = "avisa a la administración de DigLab";

        /// <summary>Para la administracion: el detalle quedo en el log de su equipo.</summary>
        public const string RevisaElRegistro = "revisa el registro de diagnóstico de este equipo, donde quedó el detalle";

        /// <summary>
        /// "No se pudo guardar: no hubo conexion con el servidor. Revisa la red e
        /// intentalo de nuevo; si sigue, avisa a la persona responsable del
        /// laboratorio." queNoSePudo va sin punto final; siSigue es una de las
        /// constantes de arriba (en minusculas y sin punto).
        /// </summary>
        public static string Explicar(string queNoSePudo, Exception ex, string siSigue)
        {
            string propio = AvisoPropio(ex);
            if (propio != null)
            {
                return queNoSePudo + ". " + propio;
            }

            bool reintentar;
            bool red;
            string causa = Causa(ex, out reintentar, out red);
            return queNoSePudo + ": " + causa + ". " + (reintentar
                ? (red ? "Revisa la red e inténtalo de nuevo" : "Inténtalo de nuevo") + "; si sigue, " + siSigue + "."
                : char.ToUpperInvariant(siSigue[0]) + siSigue.Substring(1) + ".");
        }

        /// <summary>Sin excepcion a mano (la conexion vino vacia): lo mismo, culpando a la red.</summary>
        public static string SinConexion(string queNoSePudo, string siSigue)
        {
            return queNoSePudo + ": no hubo conexión con el servidor. Revisa la red e inténtalo de nuevo; si sigue, " + siSigue + ".";
        }

        /// <summary>
        /// Si se culpa a la red, con la misma regla que Explicar: la cuenta de la
        /// base rechazada (1044/1045) o un archivo de este equipo (IOException o
        /// un permiso de Windows que no vienen del driver) no son la red, aunque
        /// ColaBitacoras.Clasificar los cuente asi. Para quien arma su propio
        /// texto (la pantalla de error, la del estudiante sin copia del horario).
        /// </summary>
        public static bool EsDeRed(Exception ex)
        {
            bool reintentar;
            bool red;
            Causa(ex, out reintentar, out red);
            return red;
        }

        /// <summary>El mensaje de las excepciones que DigLab ya escribe para la persona; null si no es una de ellas.</summary>
        private static string AvisoPropio(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is DigLab.Infraestructura.AccionNoPermitida || e is ErrorDeDatos || e is ErrorDeRecuperacion)
                {
                    return e.Message;
                }
            }

            return null;
        }

        /// <summary>
        /// Por que, en minusculas y sin punto. reintentar: si volver a intentarlo
        /// puede servir (con un permiso que falta, no). red: si se culpa a la red.
        /// </summary>
        private static string Causa(Exception ex, out bool reintentar, out bool red)
        {
            reintentar = true;
            red = false;
            if (ex == null)
            {
                return "ocurrió un error inesperado";
            }

            MySqlException mysql = null;
            bool deLaBase = false;
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                mysql = mysql ?? e as MySqlException;
                deLaBase |= e is MySqlException || e is SocketException || e is TimeoutException
                    || (e.Source ?? "").StartsWith("MySql", StringComparison.OrdinalIgnoreCase);
            }

            if (mysql != null && (mysql.Number == 1044 || mysql.Number == 1045))
            {
                reintentar = false;
                return "el servidor rechazó la cuenta de la base de datos de este equipo";
            }

            // Un archivo de este equipo (exportar, la cola local, una DLL que falta),
            // no la red: Clasificar toma IOException como red porque asi llegan los
            // cortes del driver. Tambien dentro de otra (TypeInitializationException...).
            if (!deLaBase)
            {
                bool archivo = false;
                for (Exception e = ex; e != null; e = e.InnerException)
                {
                    if (e is UnauthorizedAccessException)
                    {
                        return "Windows no dio permiso para usar ese archivo o esa carpeta";
                    }

                    archivo |= e is IOException;
                }

                if (archivo)
                {
                    return "no se pudo usar el archivo (puede estar abierto en otro programa, o el disco, lleno)";
                }
            }

            if (ColaBitacoras.Clasificar(ex) == ClaseDeFallo.SinConexion)
            {
                red = true;
                return "no hubo conexión con el servidor";
            }

            if (mysql == null)
            {
                return "ocurrió un error inesperado";
            }

            switch (mysql.Number)
            {
                case 1142:  // sin permiso sobre la tabla
                case 1143:  // sin permiso sobre la columna
                case 1227:  // sin el privilegio
                case 1370:  // sin permiso para ejecutar
                    reintentar = false;
                    return "la cuenta de la base de datos de este equipo no tiene permiso para hacerlo";
                case 1054:  // columna que no existe
                case 1146:  // tabla que no existe
                    reintentar = false;
                    return "a la base de datos le falta una actualización";
                case 1205:  // espera de bloqueo agotada
                case 1213:  // interbloqueo
                    return "la base de datos estaba ocupada con otro cambio";
                case 1036:  // tabla de solo lectura
                case 1290:  // servidor en solo lectura
                    reintentar = false;
                    return "la base de datos no admite cambios en este momento";
                default:
                    return "el servidor no aceptó la operación";
            }
        }
    }
}
