using System;
using System.Collections.Generic;
using System.Globalization;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Una memoria de emergencia registrada (db/28), como la ve la administracion.</summary>
    internal sealed class MemoriaRegistrada
    {
        public int Id;
        public string Nombre;
        /// <summary>Lo que decia Windows de ella al registrarla; "" si nada.</summary>
        public string Modelo;
        public DateTime AgregadaEn;
        /// <summary>Quien la registro; "" si ya no se sabe.</summary>
        public string AgregadaPor;
    }

    /// <summary>Una memoria que abre los equipos: su numero (0: la que se autorizo al instalar el equipo) y su numero de serie hasheado.</summary>
    internal sealed class HuellaDeMemoria
    {
        public int Id;
        public string Huella;
    }

    /// <summary>
    /// Laboratorios > "Memorias de emergencia...": las memorias USB que abren el
    /// escritorio de cualquier equipo del laboratorio sin bitacora (Emergencia). Solo
    /// la administracion las registra y las quita; los equipos solo leen las huellas
    /// (digbit_equipo, solo SELECT: con INSERT, un estudiante registraria la suya). En
    /// la base va el numero de serie hasheado, nunca en claro; los renglones no se
    /// borran (el de una quitada lleva quitada_en).
    /// </summary>
    internal static class MemoriasEmergenciaDatos
    {
        public const int LargoNombre = 80;
        private const int LargoModelo = 120;

        /// <summary>
        /// Cada equipo compara la memoria conectada con todas (un PBKDF2 por cada una):
        /// con mas, el aviso de emergencia tardaria en reconocerla.
        /// </summary>
        public const int MaximoVigentes = 20;

        /// <summary>Si la base ya las tiene (db/28) y esta cuenta puede leerlas. Lanza si no hay servidor.</summary>
        public static bool Disponibles()
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                return Esquema.MemoriasEmergencia(conexion);
            }
        }

        /// <summary>Las vigentes, en el orden en que se registraron. Lanza si no hay servidor.</summary>
        public static List<MemoriaRegistrada> Vigentes()
        {
            List<MemoriaRegistrada> lista = new List<MemoriaRegistrada>();
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTabla(conexion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT m.idmemorias_emergencia, m.nombre, m.modelo, m.agregada_en,
                           TRIM(CONCAT(IFNULL(u.nombre, ''), ' ', IFNULL(u.apellido_paterno, ''))) AS por
                    FROM memorias_emergencia m
                    LEFT JOIN usuarios u ON u.idusuarios = m.agregada_por
                    WHERE m.quitada_en IS NULL
                    ORDER BY m.agregada_en, m.idmemorias_emergencia", conexion))
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        lista.Add(new MemoriaRegistrada
                        {
                            Id = lector.GetInt32(0),
                            Nombre = lector.GetString(1),
                            Modelo = lector.IsDBNull(2) ? "" : lector.GetString(2),
                            AgregadaEn = lector.GetDateTime(3),
                            AgregadaPor = lector.IsDBNull(4) ? "" : lector.GetString(4)
                        });
                    }
                }
            }

            return lista;
        }

        /// <summary>
        /// Las huellas de las vigentes, o null si la base no tiene db/28 (o esta cuenta no
        /// puede leerla: lo avisa Esquema en el log). Lanza si no hay servidor.
        /// </summary>
        public static List<HuellaDeMemoria> HuellasVigentes(MySqlConnection conexion, MySqlTransaction transaccion = null)
        {
            if (!Esquema.MemoriasEmergencia(conexion, transaccion))
            {
                return null;
            }

            List<HuellaDeMemoria> huellas = new List<HuellaDeMemoria>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idmemorias_emergencia, huella FROM memorias_emergencia WHERE quitada_en IS NULL ORDER BY idmemorias_emergencia",
                conexion, transaccion))
            using (MySqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    huellas.Add(new HuellaDeMemoria { Id = lector.GetInt32(0), Huella = lector.GetString(1) });
                }
            }

            return huellas;
        }

        /// <summary>El nombre con que ya esta registrada esa memoria, o null. Lanza si no hay servidor.</summary>
        public static string YaRegistrada(string serie)
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTabla(conexion);
                return NombreDeLaQueCoincide(conexion, null, serie, false);
            }
        }

        /// <summary>
        /// Registra la memoria conectada con ese nombre y devuelve su numero. Lo que no
        /// se puede (ya esta, sin nombre, un numero de serie que no la distingue...)
        /// sale como ErrorDeDatos.
        /// </summary>
        public static int Registrar(string nombre, MemoriaUsb memoria)
        {
            Permisos.Exigir("Registrar una memoria de emergencia");
            string limpio = HistorialCambios.Limpiar(nombre, LargoNombre);
            if (limpio == null)
            {
                throw new ErrorDeDatos("Escribe un nombre para reconocerla, por ejemplo «Llavero de la responsable de CISCO».");
            }

            if (memoria == null || !Emergencia.SerieConfiable(memoria.Serie))
            {
                throw new ErrorDeDatos("Esa memoria no tiene un número de serie propio: otras iguales dicen lo mismo y también abrirían los equipos. Usa otra memoria.");
            }

            // Antes de abrir la transaccion: es lo lento.
            string huella = Contrasenas.Hash(memoria.Serie.Trim());
            string modelo = HistorialCambios.Limpiar(memoria.Modelo, LargoModelo);
            int id;
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTabla(conexion);
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    // FOR UPDATE: dos administradores a la vez no registran la misma dos veces.
                    string yaEsta = NombreDeLaQueCoincide(conexion, transaccion, memoria.Serie, true);
                    if (yaEsta != null)
                    {
                        throw new ErrorDeDatos("Esa memoria ya está registrada como «" + yaEsta + "».");
                    }

                    using (MySqlCommand contar = new MySqlCommand(
                        "SELECT COUNT(*) FROM memorias_emergencia WHERE quitada_en IS NULL", conexion, transaccion))
                    {
                        if (Convert.ToInt32(contar.ExecuteScalar(), CultureInfo.InvariantCulture) >= MaximoVigentes)
                        {
                            throw new ErrorDeDatos("Ya hay " + MaximoVigentes + " memorias registradas, el máximo: quita las que ya no se usen.");
                        }
                    }

                    using (MySqlCommand repetido = new MySqlCommand(
                        "SELECT COUNT(*) FROM memorias_emergencia WHERE quitada_en IS NULL AND nombre = @Nombre", conexion, transaccion))
                    {
                        repetido.Parameters.AddWithValue("@Nombre", limpio);
                        if (Convert.ToInt32(repetido.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                        {
                            throw new ErrorDeDatos("Ya hay otra memoria con el nombre «" + limpio + "»: ponle uno que las distinga.");
                        }
                    }

                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO memorias_emergencia (nombre, modelo, huella, agregada_en, agregada_por)
                        VALUES (@Nombre, @Modelo, @Huella, NOW(), @Por)", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Nombre", limpio);
                        comando.Parameters.AddWithValue("@Modelo", (object)modelo ?? DBNull.Value);
                        comando.Parameters.AddWithValue("@Huella", huella);
                        comando.Parameters.AddWithValue("@Por", IdDeLaSesion(conexion, transaccion));
                        comando.ExecuteNonQuery();
                        id = (int)comando.LastInsertedId;
                    }

                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.MemoriaEmergencia,
                        Accion = AccionDeCambio.Alta,
                        Tabla = "memorias_emergencia",
                        RegistroId = id,
                        // Sin el numero de serie: ni siquiera hasheado.
                        Descripcion = "Se registró la memoria de emergencia «" + limpio + "»: abre cualquier equipo del laboratorio sin bitácora.",
                        Despues = HistorialCambios.Datos("Nombre", limpio, "Memoria", modelo)
                    });
                    transaccion.Commit();
                }
            }

            Log.Info("Memoria de emergencia registrada: n.o " + id + ".");
            return id;
        }

        /// <summary>Deja de abrir los equipos. ErrorDeDatos si ya no estaba (otra persona la quito).</summary>
        public static void Quitar(int id)
        {
            Permisos.Exigir("Quitar la memoria de emergencia n.o " + id);
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTabla(conexion);
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    string nombre;
                    using (MySqlCommand leer = new MySqlCommand(
                        "SELECT nombre FROM memorias_emergencia WHERE idmemorias_emergencia = @Id AND quitada_en IS NULL FOR UPDATE", conexion, transaccion))
                    {
                        leer.Parameters.AddWithValue("@Id", id);
                        nombre = Convert.ToString(leer.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }

                    if (string.IsNullOrEmpty(nombre))
                    {
                        throw new ErrorDeDatos("Esa memoria ya no está registrada: alguien más la quitó. Actualiza la lista.");
                    }

                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE memorias_emergencia SET quitada_en = NOW(), quitada_por = @Por
                        WHERE idmemorias_emergencia = @Id AND quitada_en IS NULL", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Id", id);
                        comando.Parameters.AddWithValue("@Por", IdDeLaSesion(conexion, transaccion));
                        comando.ExecuteNonQuery();
                    }

                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.MemoriaEmergencia,
                        Accion = AccionDeCambio.Quitar,
                        Tabla = "memorias_emergencia",
                        RegistroId = id,
                        Descripcion = "Se quitó la memoria de emergencia «" + nombre + "»: ya no abre los equipos.",
                        Antes = HistorialCambios.Datos("Nombre", nombre)
                    });
                    transaccion.Commit();
                }
            }

            Log.Info("Memoria de emergencia quitada: n.o " + id + ".");
        }

        /// <summary>
        /// DigLab.exe --memorias-emergencia (el servicio DigLab.Vigilante y el instalador,
        /// con permisos de administrador): copia las huellas vigentes al equipo, para
        /// cuando no haya servidor. Devuelve el codigo de salida: 0 copiadas, 1 sin
        /// servidor, 2 no se pudo escribir la copia, 3 la base no las tiene (db/28). En
        /// los tres casos de fallo la copia anterior se queda como estaba.
        /// </summary>
        public static int CopiarAlEquipo(string ruta, out string detalle)
        {
            List<HuellaDeMemoria> huellas;
            DateTime ahora;
            try
            {
                using (MySqlConnection conexion = new Conexion().GetConexion())
                {
                    huellas = HuellasVigentes(conexion);
                    if (huellas == null)
                    {
                        detalle = "La base no tiene las memorias de emergencia (falta db/28, o esta cuenta no puede leerlas): no se copió nada.";
                        return 3;
                    }

                    using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
                    {
                        ahora = Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }
                }
            }
            catch (Exception ex)
            {
                detalle = "No se pudo leer la base: " + ex.Message;
                return 1;
            }

            try
            {
                Emergencia.EscribirCopia(ruta, huellas, ahora);
            }
            catch (Exception ex)
            {
                detalle = "No se pudo escribir " + ruta + ": " + ex.Message;
                return 2;
            }

            detalle = huellas.Count == 1 ? "1 memoria registrada." : huellas.Count + " memorias registradas.";
            return 0;
        }

        /// <summary>El nombre de la vigente cuya huella es la de esa serie, o null. Un PBKDF2 por cada una.</summary>
        private static string NombreDeLaQueCoincide(MySqlConnection conexion, MySqlTransaction transaccion, string serie, bool bloquear)
        {
            List<KeyValuePair<string, string>> vigentes = new List<KeyValuePair<string, string>>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT nombre, huella FROM memorias_emergencia WHERE quitada_en IS NULL" + (bloquear ? " FOR UPDATE" : ""), conexion, transaccion))
            using (MySqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    vigentes.Add(new KeyValuePair<string, string>(lector.GetString(0), lector.GetString(1)));
                }
            }

            foreach (KeyValuePair<string, string> vigente in vigentes)
            {
                // Una huella danada (escrita a mano en la base) no es esta memoria, y no
                // debe impedir registrar las demas.
                try
                {
                    bool necesitaRehash;
                    if (Contrasenas.Verificar((serie ?? "").Trim(), vigente.Value, out necesitaRehash))
                    {
                        return vigente.Key;
                    }
                }
                catch (Exception ex)
                {
                    Log.Aviso("La huella de la memoria de emergencia «" + vigente.Key + "» no se puede comprobar: " + ex.Message);
                }
            }

            return null;
        }

        private static void ExigirTabla(MySqlConnection conexion)
        {
            if (!Esquema.MemoriasEmergencia(conexion))
            {
                throw new ErrorDeDatos("Todavía no se pueden registrar memorias de emergencia: a la base de datos le falta una actualización (db/28).");
            }
        }

        private static object IdDeLaSesion(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idusuarios FROM usuarios WHERE numero_identificador = @Yo LIMIT 1", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Yo", Datos_User.getUser() ?? "");
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? (object)DBNull.Value : Convert.ToInt32(valor, CultureInfo.InvariantCulture);
            }
        }
    }
}
