using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Una version de DigLab publicada en la base (db/29).</summary>
    internal sealed class VersionPublicada
    {
        public int Id;
        /// <summary>Como esta en la base: "1.2.2.0".</summary>
        public string Version;
        public Version Numero;
        public string Archivo;
        public long Tamano;
        /// <summary>SHA-256 del instalador, en hexadecimal minuscula.</summary>
        public string Huella;
        public int Trozos;
        public string Notas;
        public bool Completa;
        public DateTime PublicadaEn;
        /// <summary>Quien la publico; "" si ya no se sabe.</summary>
        public string PublicadaPor;
        public DateTime? RetiradaEn;

        public bool Vigente
        {
            get { return Completa && !RetiradaEn.HasValue; }
        }

        /// <summary>Lo que se pone en Notas antes de quien hizo los cambios (el ultimo renglon).</summary>
        public const string PrefijoAutores = "Autores:";

        /// <summary>
        /// Lo que cambia, un renglon por cosa (sin vinetas ni el de los autores): como se
        /// escribio al publicar, un cambio por renglon, o en uno solo separados por ";".
        /// </summary>
        public List<string> Cambios
        {
            get
            {
                List<string> cambios = new List<string>();
                foreach (string renglon in (Notas ?? "").Replace("\r\n", "\n").Split('\n'))
                {
                    // Con una version anterior (una sola linea), "Autores: ..." puede ir tras un ";".
                    foreach (string parte in renglon.Split(';'))
                    {
                        string cambio = parte.Trim().TrimStart('-', '*', '•', '·').Trim();
                        if (cambio.Length > 0 && !cambio.StartsWith(PrefijoAutores, StringComparison.OrdinalIgnoreCase))
                        {
                            cambios.Add(char.ToUpper(cambio[0]) + cambio.Substring(1));
                        }
                    }
                }

                return cambios;
            }
        }

        /// <summary>Quien hizo los cambios ("Autores: ..." al final de las notas); vacio si no se dijo.</summary>
        public string Autores
        {
            get
            {
                foreach (string renglon in (Notas ?? "").Replace("\r\n", "\n").Split('\n'))
                {
                    foreach (string parte in renglon.Split(';'))
                    {
                        string limpio = parte.Trim().TrimStart('-', '*', '•', '·').Trim();
                        if (limpio.StartsWith(PrefijoAutores, StringComparison.OrdinalIgnoreCase))
                        {
                            return limpio.Substring(PrefijoAutores.Length).Trim().TrimEnd('.');
                        }
                    }
                }

                return "";
            }
        }

        /// <summary>Lo que cambia en un renglon, para una lista: "Esto · Aquello".</summary>
        public string CambiosEnUnRenglon
        {
            get { return string.Join(" · ", Cambios); }
        }

        /// <summary>Las notas que se guardan: un cambio por renglon y, al final, "Autores: ...".</summary>
        public static string ArmarNotas(string cambios, string autores)
        {
            List<string> renglones = new List<string>();
            foreach (string renglon in (cambios ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string limpio = renglon.Trim().TrimStart('-', '*', '•', '·').Trim();
                if (limpio.Length > 0 && !limpio.StartsWith(PrefijoAutores, StringComparison.OrdinalIgnoreCase))
                {
                    renglones.Add(limpio);
                }
            }

            string quien = (autores ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (quien.Length > 0)
            {
                renglones.Add(PrefijoAutores + " " + quien);
            }

            return string.Join("\n", renglones);
        }
    }

    /// <summary>
    /// Laboratorios > "Versión de DigLab...": la administracion publica en la base el
    /// instalador de una version nueva (DigLab-Setup.exe), partido en trozos y con su
    /// huella SHA-256; los equipos lo descargan y se actualizan (Actualizador). Solo la
    /// administracion publica y retira; los equipos solo leen (digbit_equipo, solo
    /// SELECT: con INSERT, un estudiante publicaria un instalador suyo que el vigilante
    /// correria como SYSTEM). Una version retirada deja de ofrecerse y sus trozos se
    /// borran; el renglon se queda.
    /// </summary>
    internal static class ActualizacionesDatos
    {
        public const int LargoNotas = 500;

        /// <summary>Lo que dice el instalador de si mismo (VersionInfoDescription de instalador/DigLab.iss).</summary>
        public const string DescripcionDelInstalador = "Instalador de DigLab";

        // Trozos de hasta 512 KB, o la mitad de max_allowed_packet si el servidor lo tiene mas chico.
        private const int TrozoMaximo = 512 * 1024;
        private const int TrozoMinimo = 32 * 1024;
        private const long TamanoMaximo = 64L * 1024 * 1024;
        private const int Espera = 180;

        /// <summary>La version de este DigLab.</summary>
        public static Version VersionInstalada
        {
            get { return typeof(ActualizacionesDatos).Assembly.GetName().Version; }
        }

        /// <summary>"1.2.2" (sin el ".0" del final) o "1.2.2.5".</summary>
        public static string Mostrar(Version version)
        {
            if (version == null)
            {
                return "—";
            }

            return version.Revision > 0 ? version.ToString(4) : version.ToString(3);
        }

        public static Version Leer(string texto)
        {
            Version version;
            return Version.TryParse((texto ?? "").Trim(), out version) ? version : new Version(0, 0, 0, 0);
        }

        /// <summary>Si la base ya las tiene (db/29) y esta cuenta puede leerlas. Lanza si no hay servidor.</summary>
        public static bool Disponibles()
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                return Esquema.Actualizaciones(conexion);
            }
        }

        /// <summary>Todas (tambien las retiradas), la mas reciente primero. Lanza si no hay servidor.</summary>
        public static List<VersionPublicada> Listar()
        {
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTablas(conexion);
                return Leer(conexion, null, false);
            }
        }

        /// <summary>
        /// La version vigente mas alta (completa y sin retirar), o null si no hay o la base
        /// no tiene db/29. Lanza si no hay servidor.
        /// </summary>
        public static VersionPublicada UltimaVigente(MySqlConnection conexion)
        {
            if (!Esquema.Actualizaciones(conexion))
            {
                return null;
            }

            VersionPublicada mejor = null;
            foreach (VersionPublicada version in Leer(conexion, null, true))
            {
                if (mejor == null || version.Numero > mejor.Numero)
                {
                    mejor = version;
                }
            }

            return mejor;
        }

        private static List<VersionPublicada> Leer(MySqlConnection conexion, MySqlTransaction transaccion, bool soloVigentes)
        {
            List<VersionPublicada> lista = new List<VersionPublicada>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT a.idactualizaciones, a.version, a.archivo, a.tamano, a.huella_sha256, a.trozos, a.notas,
                       a.completa, a.publicada_en, a.retirada_en,
                       TRIM(CONCAT(IFNULL(u.nombre, ''), ' ', IFNULL(u.apellido_paterno, ''))) AS por
                FROM actualizaciones a
                LEFT JOIN usuarios u ON u.idusuarios = a.publicada_por"
                + (soloVigentes ? " WHERE a.completa = 1 AND a.retirada_en IS NULL" : "") + @"
                ORDER BY a.idactualizaciones DESC
                LIMIT 50", conexion, transaccion))
            using (MySqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    lista.Add(new VersionPublicada
                    {
                        Id = lector.GetInt32(0),
                        Version = lector.GetString(1),
                        Numero = Leer(lector.GetString(1)),
                        Archivo = lector.GetString(2),
                        Tamano = lector.GetInt64(3),
                        Huella = lector.GetString(4).Trim().ToLowerInvariant(),
                        Trozos = lector.GetInt32(5),
                        Notas = lector.IsDBNull(6) ? "" : lector.GetString(6),
                        Completa = lector.GetBoolean(7),
                        PublicadaEn = lector.GetDateTime(8),
                        RetiradaEn = lector.IsDBNull(9) ? (DateTime?)null : lector.GetDateTime(9),
                        PublicadaPor = lector.IsDBNull(10) ? "" : lector.GetString(10)
                    });
                }
            }

            return lista;
        }

        /// <summary>
        /// Lo que dice el archivo de si mismo, sin tocar la base: la version, o ErrorDeDatos si
        /// no es un instalador de DigLab.
        /// </summary>
        public static Version VersionDelInstalador(string ruta)
        {
            FileInfo archivo = new FileInfo(ruta);
            if (!archivo.Exists)
            {
                throw new ErrorDeDatos("No encuentro ese archivo.");
            }

            if (archivo.Length == 0 || archivo.Length > TamanoMaximo)
            {
                throw new ErrorDeDatos("Ese archivo no parece un instalador de DigLab (pesa " + Peso(archivo.Length) + ").");
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(ruta);
            if (!string.Equals((info.FileDescription ?? "").Trim(), DescripcionDelInstalador, StringComparison.OrdinalIgnoreCase))
            {
                throw new ErrorDeDatos("Ese archivo no es un instalador de DigLab: elige el DigLab-Setup.exe de la versión nueva.");
            }

            Version version = Leer(info.FileVersion);
            if (version.Major == 0 && version.Minor == 0)
            {
                throw new ErrorDeDatos("No se pudo leer la versión de ese instalador.");
            }

            return version;
        }

        /// <summary>
        /// Sube el instalador a la base y lo deja vigente. progreso recibe de 0 a 100. Lo que
        /// no se puede (no es un instalador, ya hay una version igual o mas nueva) sale como
        /// ErrorDeDatos. Si se corta a medias, se borra lo subido: ningun equipo lo ve.
        /// </summary>
        public static int Publicar(string ruta, string notas, Action<int> progreso)
        {
            Permisos.Exigir("Publicar una versión de DigLab");
            Version version = VersionDelInstalador(ruta);
            string textoVersion = version.ToString(4);
            string limpias = HistorialCambios.Limpiar(notas, LargoNotas);
            byte[] contenido = File.ReadAllBytes(ruta);
            string huella = Huella(contenido);

            int id;
            int trozos;
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTablas(conexion);
                VersionPublicada vigente = UltimaVigente(conexion);
                if (vigente != null && vigente.Numero >= version)
                {
                    throw new ErrorDeDatos("Ya está publicada la versión " + Mostrar(vigente.Numero) + ": solo se puede publicar una más nueva. "
                        + "Para volver a una anterior, retira la que está.");
                }

                int trozo = TamanoDeTrozo(conexion);
                trozos = (int)((contenido.Length + trozo - 1) / trozo);

                using (MySqlCommand comando = new MySqlCommand(@"
                    INSERT INTO actualizaciones (version, archivo, tamano, huella_sha256, trozos, notas, completa, publicada_en, publicada_por)
                    VALUES (@Version, @Archivo, @Tamano, @Huella, @Trozos, @Notas, 0, NOW(), @Por)", conexion))
                {
                    comando.Parameters.AddWithValue("@Version", textoVersion);
                    comando.Parameters.AddWithValue("@Archivo", Path.GetFileName(ruta));
                    comando.Parameters.AddWithValue("@Tamano", (long)contenido.Length);
                    comando.Parameters.AddWithValue("@Huella", huella);
                    comando.Parameters.AddWithValue("@Trozos", trozos);
                    comando.Parameters.AddWithValue("@Notas", (object)limpias ?? DBNull.Value);
                    comando.Parameters.AddWithValue("@Por", IdDeLaSesion(conexion, null));
                    comando.ExecuteNonQuery();
                    id = (int)comando.LastInsertedId;
                }

                try
                {
                    for (int numero = 0; numero < trozos; numero++)
                    {
                        int desde = numero * trozo;
                        int largo = Math.Min(trozo, contenido.Length - desde);
                        byte[] datos = new byte[largo];
                        Buffer.BlockCopy(contenido, desde, datos, 0, largo);
                        using (MySqlCommand comando = new MySqlCommand(
                            "INSERT INTO actualizaciones_trozos (idactualizaciones, numero, datos) VALUES (@Id, @Numero, @Datos)", conexion))
                        {
                            comando.CommandTimeout = Espera;
                            comando.Parameters.AddWithValue("@Id", id);
                            comando.Parameters.AddWithValue("@Numero", numero);
                            comando.Parameters.Add("@Datos", MySqlDbType.MediumBlob).Value = datos;
                            comando.ExecuteNonQuery();
                        }

                        if (progreso != null)
                        {
                            progreso((numero + 1) * 100 / trozos);
                        }
                    }

                    using (MySqlTransaction transaccion = conexion.BeginTransaction())
                    {
                        using (MySqlCommand comando = new MySqlCommand(
                            "UPDATE actualizaciones SET completa = 1 WHERE idactualizaciones = @Id", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Id", id);
                            comando.ExecuteNonQuery();
                        }

                        HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                        {
                            Tipo = TipoDeCambio.VersionDigLab,
                            Accion = AccionDeCambio.Alta,
                            Tabla = "actualizaciones",
                            RegistroId = id,
                            Descripcion = "Se publicó DigLab " + Mostrar(version) + ": los equipos con conexión se actualizan.",
                            Despues = HistorialCambios.Datos("Versión", Mostrar(version), "Tamaño", Peso(contenido.Length), "Notas", limpias)
                        });
                        transaccion.Commit();
                    }
                }
                catch
                {
                    BorrarAMedias(conexion, id);
                    throw;
                }
            }

            Log.Info("Version de DigLab publicada: " + textoVersion + " (n.o " + id + ", " + trozos + " trozos, " + contenido.Length + " bytes).");
            return id;
        }

        private static void BorrarAMedias(MySqlConnection conexion, int id)
        {
            try
            {
                using (MySqlCommand comando = new MySqlCommand(
                    "DELETE FROM actualizaciones_trozos WHERE idactualizaciones = @Id; DELETE FROM actualizaciones WHERE idactualizaciones = @Id AND completa = 0",
                    conexion))
                {
                    comando.CommandTimeout = Espera;
                    comando.Parameters.AddWithValue("@Id", id);
                    comando.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                // Queda con completa = 0: ningun equipo la ve.
                Log.Aviso("No se pudo borrar la version a medias n.o " + id + ": " + ex.Message);
            }
        }

        /// <summary>Deja de ofrecerse y se borran sus trozos. ErrorDeDatos si ya no estaba.</summary>
        public static void Retirar(int id)
        {
            Permisos.Exigir("Retirar la versión de DigLab n.o " + id);
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTablas(conexion);
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    string version;
                    using (MySqlCommand leer = new MySqlCommand(
                        "SELECT version FROM actualizaciones WHERE idactualizaciones = @Id AND retirada_en IS NULL FOR UPDATE", conexion, transaccion))
                    {
                        leer.Parameters.AddWithValue("@Id", id);
                        version = Convert.ToString(leer.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }

                    if (string.IsNullOrEmpty(version))
                    {
                        throw new ErrorDeDatos("Esa versión ya estaba retirada: alguien más la retiró. Actualiza la lista.");
                    }

                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE actualizaciones SET retirada_en = NOW(), retirada_por = @Por WHERE idactualizaciones = @Id;
                        DELETE FROM actualizaciones_trozos WHERE idactualizaciones = @Id;", conexion, transaccion))
                    {
                        comando.CommandTimeout = Espera;
                        comando.Parameters.AddWithValue("@Id", id);
                        comando.Parameters.AddWithValue("@Por", IdDeLaSesion(conexion, transaccion));
                        comando.ExecuteNonQuery();
                    }

                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.VersionDigLab,
                        Accion = AccionDeCambio.Quitar,
                        Tabla = "actualizaciones",
                        RegistroId = id,
                        Descripcion = "Se retiró DigLab " + Mostrar(Leer(version)) + ": ya no se ofrece a los equipos.",
                        Antes = HistorialCambios.Datos("Versión", Mostrar(Leer(version)))
                    });
                    transaccion.Commit();
                }
            }

            Log.Info("Version de DigLab retirada: n.o " + id + ".");
        }

        /// <summary>
        /// Baja el instalador a "destino" trozo por trozo y comprueba el tamano y la huella;
        /// si no coinciden, borra lo bajado y lanza ErrorDeDatos. progreso recibe de 0 a 100.
        /// </summary>
        public static void Descargar(VersionPublicada version, string destino, Action<int> progreso)
        {
            string parcial = destino + ".parcial";
            File.Delete(parcial);
            long escritos = 0;
            using (SHA256 sha = SHA256.Create())
            {
                using (FileStream archivo = new FileStream(parcial, FileMode.Create, FileAccess.Write, FileShare.None))
                using (MySqlConnection conexion = new Conexion().GetConexion())
                {
                    for (int numero = 0; numero < version.Trozos; numero++)
                    {
                        byte[] datos;
                        using (MySqlCommand comando = new MySqlCommand(
                            "SELECT datos FROM actualizaciones_trozos WHERE idactualizaciones = @Id AND numero = @Numero", conexion))
                        {
                            comando.CommandTimeout = Espera;
                            comando.Parameters.AddWithValue("@Id", version.Id);
                            comando.Parameters.AddWithValue("@Numero", numero);
                            datos = comando.ExecuteScalar() as byte[];
                        }

                        if (datos == null)
                        {
                            throw new ErrorDeDatos("A la versión " + Mostrar(version.Numero) + " le falta un trozo en la base (¿la retiraron mientras se descargaba?).");
                        }

                        archivo.Write(datos, 0, datos.Length);
                        sha.TransformBlock(datos, 0, datos.Length, null, 0);
                        escritos += datos.Length;
                        if (progreso != null)
                        {
                            progreso((numero + 1) * 100 / Math.Max(1, version.Trozos));
                        }
                    }
                }

                sha.TransformFinalBlock(new byte[0], 0, 0);
                string huella = Hex(sha.Hash);
                if (escritos != version.Tamano || huella != version.Huella)
                {
                    TryDelete(parcial);
                    throw new ErrorDeDatos("La descarga de DigLab " + Mostrar(version.Numero) + " no coincide con lo que se publicó (tamaño o huella): no se instala.");
                }
            }

            TryDelete(destino);
            File.Move(parcial, destino);
        }

        // --- "Actualizar ya": la senal a los equipos de un laboratorio (ordenes_equipos) ---

        public const string OrdenActualizar = "actualizar";

        /// <summary>Lo que dura la senal: un equipo apagado no la cumple al encenderse otro dia.</summary>
        public const int MinutosDeLaOrden = 15;

        /// <summary>Los laboratorios habilitados, para elegir a cual mandar la senal: id y nombre.</summary>
        public static List<KeyValuePair<int, string>> LaboratoriosHabilitados()
        {
            List<KeyValuePair<int, string>> lista = new List<KeyValuePair<int, string>>();
            using (MySqlConnection conexion = new Conexion().GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idlaboratorios, nombre_laboratorio FROM laboratorios WHERE activo = 1 ORDER BY nombre_laboratorio", conexion))
            using (MySqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    lista.Add(new KeyValuePair<int, string>(lector.GetInt32(0), lector.GetString(1)));
                }
            }

            return lista;
        }

        /// <summary>
        /// Manda a los equipos libres de ese laboratorio (o de todos, con null) la senal de
        /// instalar ya la version vigente. ErrorDeDatos si no hay ninguna que instalar.
        /// </summary>
        public static void MandarActualizarYa(int? idLaboratorio, string nombreLaboratorio)
        {
            Permisos.Exigir("Mandar a los equipos actualizar DigLab");
            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                ExigirTablas(conexion);
                if (!Esquema.OrdenesEquipos(conexion))
                {
                    throw new ErrorDeDatos("A la base de datos le falta aplicar otra vez db/29_actualizaciones.sql (la tabla de órdenes a los equipos). "
                        + "Mientras, los equipos se actualizan solos en menos de 10 minutos.");
                }

                VersionPublicada vigente = UltimaVigente(conexion);
                if (vigente == null)
                {
                    throw new ErrorDeDatos("Todavía no hay ninguna versión publicada que instalar.");
                }

                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    int id;
                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO ordenes_equipos (tipo, laboratorios_idlaboratorios, creada_en, creada_por, caduca_en)
                        VALUES (@Tipo, @Lab, NOW(), @Por, NOW() + INTERVAL " + MinutosDeLaOrden + " MINUTE)", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Tipo", OrdenActualizar);
                        comando.Parameters.AddWithValue("@Lab", idLaboratorio.HasValue ? (object)idLaboratorio.Value : DBNull.Value);
                        comando.Parameters.AddWithValue("@Por", IdDeLaSesion(conexion, transaccion));
                        comando.ExecuteNonQuery();
                        id = (int)comando.LastInsertedId;
                    }

                    string donde = idLaboratorio.HasValue ? "los equipos de " + nombreLaboratorio : "los equipos de todos los laboratorios";
                    HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                    {
                        Tipo = TipoDeCambio.VersionDigLab,
                        Accion = AccionDeCambio.Cambio,
                        Tabla = "ordenes_equipos",
                        RegistroId = id,
                        Descripcion = "Se mandó a " + donde + " instalar ya DigLab " + Mostrar(vigente.Numero) + " (los que estén libres).",
                        Despues = HistorialCambios.Datos("Versión", Mostrar(vigente.Numero), "Laboratorio", idLaboratorio.HasValue ? nombreLaboratorio : "Todos")
                    });
                    transaccion.Commit();
                }
            }

            Log.Info("Senal de actualizar ya mandada a " + (idLaboratorio.HasValue ? "el laboratorio " + idLaboratorio.Value : "todos los laboratorios") + ".");
        }

        /// <summary>
        /// Si hay una senal vigente de actualizar ya para los equipos de ese laboratorio. False si
        /// la base no tiene la tabla. Lanza si no hay servidor.
        /// </summary>
        public static bool HayOrdenDeActualizar(MySqlConnection conexion, int idLaboratorio)
        {
            if (!Esquema.OrdenesEquipos(conexion))
            {
                return false;
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT COUNT(*) FROM ordenes_equipos
                WHERE tipo = @Tipo AND caduca_en > NOW()
                  AND (laboratorios_idlaboratorios IS NULL OR laboratorios_idlaboratorios = @Lab)", conexion))
            {
                comando.Parameters.AddWithValue("@Tipo", OrdenActualizar);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                return Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        /// <summary>SHA-256 de un archivo, en hexadecimal minuscula.</summary>
        public static string HuellaDe(string ruta)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream archivo = File.OpenRead(ruta))
            {
                return Hex(sha.ComputeHash(archivo));
            }
        }

        private static string Huella(byte[] contenido)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(contenido));
            }
        }

        private static string Hex(byte[] bytes)
        {
            StringBuilder texto = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                texto.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return texto.ToString();
        }

        /// <summary>"11.6 MB".</summary>
        public static string Peso(long bytes)
        {
            return bytes >= 1024 * 1024
                ? (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.GetCultureInfo("es-MX")) + " MB"
                : (bytes / 1024.0).ToString("0", CultureInfo.GetCultureInfo("es-MX")) + " KB";
        }

        private static int TamanoDeTrozo(MySqlConnection conexion)
        {
            try
            {
                using (MySqlCommand comando = new MySqlCommand("SELECT @@max_allowed_packet", conexion))
                {
                    long maximo = Convert.ToInt64(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
                    return (int)Math.Max(TrozoMinimo, Math.Min(TrozoMaximo, maximo / 2 - 4096));
                }
            }
            catch (Exception ex)
            {
                Log.Aviso("No se pudo leer max_allowed_packet; se sube en trozos de " + TrozoMinimo / 1024 + " KB: " + ex.Message);
                return TrozoMinimo;
            }
        }

        private static void TryDelete(string ruta)
        {
            try
            {
                File.Delete(ruta);
            }
            catch (Exception)
            {
                // Se queda; la siguiente descarga lo reemplaza.
            }
        }

        private static void ExigirTablas(MySqlConnection conexion)
        {
            if (!Esquema.Actualizaciones(conexion))
            {
                throw new ErrorDeDatos("Todavía no se pueden publicar versiones de DigLab: a la base de datos le falta una actualización (db/29).");
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
