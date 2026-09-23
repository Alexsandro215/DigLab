using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Una bitacora guardada en el equipo mientras no habia servidor.</summary>
    public class BitacoraPendiente
    {
        public Guid Id = Guid.NewGuid();
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
        public int Intentos;
        public string UltimoError;
    }

    public class ResultadoSincronizacion
    {
        public int Enviadas;
        public int Duplicadas;
        public int ConError;
        public int Pendientes;
    }

    /// <summary>
    /// Cola de bitacoras registradas sin servidor (fase 4). Archivo XML en
    /// %LOCALAPPDATA%\DigBit\bitacoras_pendientes.xml. Se vacia con Sincronizar
    /// cuando vuelve la conexion: cada una crea (o encuentra) su sesion y se
    /// inserta marcada como registrada sin conexion.
    /// </summary>
    internal static class ColaBitacoras
    {
        private static readonly object candado = new object();
        private const string FmtHora = @"hh\:mm\:ss";

        public static string Ruta
        {
            get { return Path.Combine(CacheHorario.Carpeta, "bitacoras_pendientes.xml"); }
        }

        public static void Encolar(BitacoraPendiente bitacora)
        {
            lock (candado)
            {
                List<BitacoraPendiente> lista = Leer();
                lista.Add(bitacora);
                Guardar(lista);
            }

            Log.Info("Bitacora de " + bitacora.Matricula + " guardada en la cola local (" + bitacora.Sesion.Clave + ").");
        }

        public static bool YaRegistrada(string claveSesion, string matricula)
        {
            lock (candado)
            {
                return Leer().Any(b => b.Sesion.Clave == claveSesion && string.Equals(b.Matricula, matricula, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static int Pendientes()
        {
            lock (candado)
            {
                return Leer().Count;
            }
        }

        /// <summary>Sube la cola al servidor. Requiere conexion; los fallos se registran y la bitacora se queda en la cola.</summary>
        public static ResultadoSincronizacion Sincronizar()
        {
            ResultadoSincronizacion resultado = new ResultadoSincronizacion();
            lock (candado)
            {
                List<BitacoraPendiente> lista = Leer();
                if (lista.Count == 0)
                {
                    return resultado;
                }

                List<BitacoraPendiente> restantes = new List<BitacoraPendiente>();
                using (MySqlConnection conexion = new Conexion().GetConexion())
                {
                    foreach (BitacoraPendiente b in lista)
                    {
                        try
                        {
                            EstadoEnvio estado = Enviar(conexion, b);
                            if (estado == EstadoEnvio.Enviada)
                            {
                                resultado.Enviadas++;
                            }
                            else
                            {
                                resultado.Duplicadas++;
                            }
                        }
                        catch (Exception ex)
                        {
                            b.Intentos++;
                            b.UltimoError = ex.Message;
                            resultado.ConError++;
                            restantes.Add(b);
                            Log.Error("Enviar la bitacora pendiente de " + b.Matricula + " (" + b.Sesion.Clave + ")", ex);
                        }
                    }
                }

                Guardar(restantes);
                resultado.Pendientes = restantes.Count;
            }

            if (resultado.Enviadas + resultado.Duplicadas + resultado.ConError > 0)
            {
                Log.Info("Sincronizacion de bitacoras: " + resultado.Enviadas + " enviadas, " + resultado.Duplicadas + " ya existian, "
                    + resultado.ConError + " con error, " + resultado.Pendientes + " pendientes.");
            }

            return resultado;
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
                    throw new InvalidOperationException("La matricula " + b.Matricula + " no existe en el servidor.");
                }

                idUsuario = Convert.ToInt32(r);
            }

            int idSesion = ObtenerOCrearSesion(conexion, b.Sesion);

            try
            {
                // Solo si no existe ya. Aqui importa mas que en linea: si la red se
                // cae despues de insertar pero antes de la respuesta, la cola lo
                // reintenta, y sin esta comprobacion la bitacora entraria dos veces.
                // Antes eso lo evitaba el UNIQUE (fk_codigo_accesos, fk_usuario),
                // que la base del TESCHI no puede llevar por sus duplicados
                // historicos. El catch del 1062 sigue para las bases que si.
                using (MySqlCommand comando = new MySqlCommand(@"
                    INSERT INTO registros_bitacoras
                        (fk_codigo_accesos, fk_usuario, numero_computadora, falla_red, comentarios_red, falla_hardware, comentarios_hardware,
                         falla_software, comentarios_software, registrado_sin_conexion, registrado_en)
                    SELECT @S, @U, @C, @FR, @CR, @FH, @CH, @FS, @CS, 1, @En
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
                    comando.Parameters.AddWithValue("@En", b.Registrada);
                    if (comando.ExecuteNonQuery() == 0)
                    {
                        Log.Aviso("Bitacora pendiente de " + b.Matricula + " descartada: ya existia en la sesion " + idSesion + ".");
                        return EstadoEnvio.Duplicada;
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

        /// <summary>
        /// La sesion de la franja/extra en esa fecha. Si la franja o la extra ya no
        /// existen en el servidor, la sesion se crea sin enlace (queda como
        /// historial, igual que las de antes de la fase 6).
        /// </summary>
        private static int ObtenerOCrearSesion(MySqlConnection conexion, SesionLocal s)
        {
            bool franjaExiste = s.FranjaId.HasValue && Existe(conexion, "SELECT 1 FROM horarios WHERE idhorarios = @Id", s.FranjaId.Value);
            bool extraExiste = s.ExcepcionId.HasValue && Existe(conexion, "SELECT 1 FROM horario_excepciones WHERE idhorario_excepciones = @Id", s.ExcepcionId.Value);

            string buscar = franjaExiste
                ? "SELECT idcodigos_accesos FROM codigos_accesos WHERE horarios_idhorarios = @Id AND fecha = @Fecha"
                : extraExiste
                    ? "SELECT idcodigos_accesos FROM codigos_accesos WHERE horario_excepciones_id = @Id"
                    : "SELECT idcodigos_accesos FROM codigos_accesos WHERE codigo = @Codigo AND fecha = @Fecha AND hora_entrada = @Inicio AND laboratorios_idlaboratorios = @Lab";

            using (MySqlCommand comando = new MySqlCommand(buscar, conexion))
            {
                comando.Parameters.AddWithValue("@Id", franjaExiste ? s.FranjaId.GetValueOrDefault() : s.ExcepcionId.GetValueOrDefault());
                comando.Parameters.AddWithValue("@Fecha", s.Fecha.Date);
                comando.Parameters.AddWithValue("@Codigo", s.Codigo);
                comando.Parameters.AddWithValue("@Inicio", s.Inicio);
                comando.Parameters.AddWithValue("@Lab", s.LaboratorioId);
                object r = comando.ExecuteScalar();
                if (r != null)
                {
                    return Convert.ToInt32(r);
                }
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
                    comando.Parameters.AddWithValue("@Codigo", s.Codigo);
                    comando.Parameters.AddWithValue("@Fecha", s.Fecha.Date);
                    comando.Parameters.AddWithValue("@Inicio", s.Inicio);
                    comando.Parameters.AddWithValue("@Fin", s.Fin);
                    comando.Parameters.AddWithValue("@Materia", s.MateriaId);
                    comando.Parameters.AddWithValue("@Grupo", s.GrupoId);
                    comando.Parameters.AddWithValue("@Lab", s.LaboratorioId);
                    comando.Parameters.AddWithValue("@Profesor", s.ProfesorId);
                    comando.Parameters.AddWithValue("@Franja", franjaExiste ? (object)s.FranjaId.Value : DBNull.Value);
                    comando.Parameters.AddWithValue("@Excepcion", extraExiste ? (object)s.ExcepcionId.Value : DBNull.Value);
                    return Convert.ToInt32(comando.ExecuteScalar());
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                // La creo otro equipo mientras tanto.
                using (MySqlCommand comando = new MySqlCommand(buscar, conexion))
                {
                    comando.Parameters.AddWithValue("@Id", franjaExiste ? s.FranjaId.GetValueOrDefault() : s.ExcepcionId.GetValueOrDefault());
                    comando.Parameters.AddWithValue("@Fecha", s.Fecha.Date);
                    comando.Parameters.AddWithValue("@Codigo", s.Codigo);
                    comando.Parameters.AddWithValue("@Inicio", s.Inicio);
                    comando.Parameters.AddWithValue("@Lab", s.LaboratorioId);
                    return Convert.ToInt32(comando.ExecuteScalar());
                }
            }
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

        private static List<BitacoraPendiente> Leer()
        {
            if (!File.Exists(Ruta))
            {
                return new List<BitacoraPendiente>();
            }

            try
            {
                return XDocument.Load(Ruta).Root.Elements("bitacora").Select(e => new BitacoraPendiente
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
                    Intentos = (int)e.Attribute("intentos"),
                    UltimoError = (string)e.Attribute("ultimoError"),
                    Sesion = LeerSesion(e.Element("sesion"))
                }).ToList();
            }
            catch (Exception ex)
            {
                // Una cola ilegible no debe tumbar la app; se aparta para revisarla a mano.
                Log.Error("Leer la cola de bitacoras pendientes; se renombra como .corrupto", ex);
                try { File.Move(Ruta, Ruta + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".corrupto"); } catch (Exception) { }
                return new List<BitacoraPendiente>();
            }
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
                Profesor = (string)e.Attribute("profesor")
            };
        }

        private static void Guardar(List<BitacoraPendiente> lista)
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
                new XAttribute("intentos", b.Intentos),
                new XAttribute("ultimoError", b.UltimoError ?? ""),
                new XElement("sesion",
                    new XAttribute("codigo", b.Sesion.Codigo),
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
                    new XAttribute("profesor", b.Sesion.Profesor ?? "")))));

            string temporal = Ruta + ".tmp";
            new XDocument(raiz).Save(temporal);
            if (File.Exists(Ruta))
            {
                File.Replace(temporal, Ruta, null);
            }
            else
            {
                File.Move(temporal, Ruta);
            }
        }
    }
}
