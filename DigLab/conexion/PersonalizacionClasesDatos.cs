using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>Que pasa con la imagen al guardar.</summary>
    internal enum CambioImagen
    {
        /// <summary>La que se ve ahora (si viene de otra fila, el servidor la copia).</summary>
        Igual,
        /// <summary>PedidoPersonal.Bytes: PNG o JPEG de Lado x Lado ya recortado.</summary>
        Nueva,
        /// <summary>Sin imagen: las iniciales.</summary>
        Quitar
    }

    /// <summary>Una clase del profesor con su fila personal (de su grupo o de su materia), sin la imagen.</summary>
    internal sealed class FilaPersonal
    {
        public int ClaseId;
        public int MateriaId;
        public int GrupoId;
        /// <summary>Oficial (materias.nombre_materia).</summary>
        public string Materia = "";
        /// <summary>Oficial (grupos.nombre_grupo).</summary>
        public string Grupo = "";
        /// <summary>Activa y vigente hasta hace 30 dias (como Mis grupos).</summary>
        public bool Actual;
        /// <summary>La fila que manda es la de su grupo.</summary>
        public bool DeSuGrupo;
        /// <summary>Hay fila de la materia (aunque mande la del grupo).</summary>
        public bool MateriaPersonal;
        /// <summary>'#RRGGBB' o null (de la fila que manda). Uno mal escrito llega como null.</summary>
        public string Color;
        /// <summary>O null.</summary>
        public string NombreCorto;
        /// <summary>O null.</summary>
        public string ImagenSha1;
        /// <summary>El de la fila de la materia (para repartir los automaticos), o null.</summary>
        public string ColorDeLaMateria;
    }

    /// <summary>Todas las clases de un profesor, ya resueltas (PersonalizacionClasesDatos.Leer).</summary>
    internal sealed class LecturaPersonal
    {
        public int ProfesorId;
        public List<FilaPersonal> Clases = new List<FilaPersonal>();
    }

    /// <summary>Lo que el dialogo pide guardar para una clase.</summary>
    internal sealed class PedidoPersonal
    {
        public int MateriaId;
        /// <summary>El de la clase desde la que se abrio.</summary>
        public int GrupoId;
        /// <summary>Fila de la materia (y sus grupos dejan de tener la suya) o solo la del grupo.</summary>
        public bool TodaLaMateria;
        /// <summary>'#RRGGBB' o null (automatico).</summary>
        public string Color;
        /// <summary>Ya limpio (LimpiarNombre) o null.</summary>
        public string NombreCorto;
        public CambioImagen Imagen;
        /// <summary>Con Nueva: PNG o JPEG de 128 x 128, como mucho BytesImagen.</summary>
        public byte[] Bytes;
        /// <summary>Lo llena Guardar con Nueva: el SHA1 que calculo el servidor sobre lo guardado.</summary>
        public string Sha1Guardada;
    }

    /// <summary>
    /// Lo que cada profesor elige para sus clases (db/19, personalizacion_clases):
    /// una imagen, un color y un nombre corto. Es solo suyo: no cambia nada de la
    /// clase. Una fila por profesor y materia (fk_grupo = 0, vale para todos sus
    /// grupos de esa materia, tambien los del ciclo siguiente) o por profesor,
    /// materia y grupo (ese grupo tiene la suya). La fila manda entera: la del
    /// grupo si existe, si no la de la materia; NULL es lo de siempre.
    ///
    /// Leer no toca la columna de la imagen (el panel la repite cada 30 s): trae
    /// su SHA1, y las imagenes se piden aparte, solo las que no estan en memoria.
    /// </summary>
    internal class PersonalizacionClasesDatos
    {
        public const int LargoNombreCorto = 24;
        /// <summary>Lo que se guarda como mucho (la columna es MEDIUMBLOB: con sql_mode vacio un BLOB cortaria sin error).</summary>
        public const int BytesImagen = 60000;

        public const string FaltaMigracion = "Todavía no se pueden personalizar las clases: a la base de datos le falta una actualización (db/19). Avisa a la administración de DigLab.";
        public const string YaNoEsSuya = "Esa clase ya no está a tu nombre (pudo cambiarla la administración de DigLab). No se guardó nada.";
        public const string ImagenCortada = "La imagen no llegó completa al servidor. Prueba con otra más sencilla.";

        private readonly Conexion mconexion = new Conexion();

        /// <summary>
        /// Todas sus clases ya resueltas, sin la imagen. Null sin db/19 (o si esta
        /// cuenta no ve la tabla). Lanza si falla la consulta: quien lo llama lo
        /// envuelve en su try/catch y conserva lo de antes.
        /// </summary>
        public static LecturaPersonal Leer(MySqlConnection conexion, int idProfesor)
        {
            if (!Esquema.PersonalizacionDeClases(conexion))
            {
                return null;
            }

            LecturaPersonal lectura = new LecturaPersonal { ProfesorId = idProfesor };
            // "Actual" como Mis grupos: activa, ya empezo y vigente hasta hace 30 dias.
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT c.idclases, c.materias_id_materia AS materia, c.grupos_idgrupos AS grupo,
                       m.nombre_materia, g.nombre_grupo,
                       (c.activa = 1 AND c.vigente_desde <= CURDATE() AND c.vigente_hasta >= CURDATE() - INTERVAL 30 DAY) AS actual,
                       pg.fk_profesor IS NOT NULL AS hay_g, pg.color AS color_g, pg.nombre_corto AS nombre_g, pg.imagen_sha1 AS sha1_g,
                       pm.fk_profesor IS NOT NULL AS hay_m, pm.color AS color_m, pm.nombre_corto AS nombre_m, pm.imagen_sha1 AS sha1_m
                FROM clases c
                JOIN materias m ON m.id_materia = c.materias_id_materia
                JOIN grupos g ON g.idgrupos = c.grupos_idgrupos
                LEFT JOIN personalizacion_clases pg
                       ON pg.fk_profesor = c.usuarios_idusuarios AND pg.fk_materia = c.materias_id_materia AND pg.fk_grupo = c.grupos_idgrupos
                LEFT JOIN personalizacion_clases pm
                       ON pm.fk_profesor = c.usuarios_idusuarios AND pm.fk_materia = c.materias_id_materia AND pm.fk_grupo = 0
                WHERE c.usuarios_idusuarios = @Profesor
                ORDER BY c.idclases", conexion))
            {
                comando.Parameters.AddWithValue("@Profesor", idProfesor);
                using (MySqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        bool deSuGrupo = Verdad(lector["hay_g"]);
                        bool deLaMateria = Verdad(lector["hay_m"]);
                        string sufijo = deSuGrupo ? "_g" : "_m";
                        bool hayFila = deSuGrupo || deLaMateria;
                        lectura.Clases.Add(new FilaPersonal
                        {
                            ClaseId = Convert.ToInt32(lector["idclases"]),
                            MateriaId = Convert.ToInt32(lector["materia"]),
                            GrupoId = Convert.ToInt32(lector["grupo"]),
                            Materia = Texto(lector["nombre_materia"]) ?? "",
                            Grupo = Texto(lector["nombre_grupo"]) ?? "",
                            Actual = Verdad(lector["actual"]),
                            DeSuGrupo = deSuGrupo,
                            MateriaPersonal = deLaMateria,
                            Color = hayFila ? ColorValido(lector["color" + sufijo]) : null,
                            NombreCorto = hayFila ? Texto(lector["nombre" + sufijo]) : null,
                            ImagenSha1 = hayFila ? Sha1Valido(lector["sha1" + sufijo]) : null,
                            ColorDeLaMateria = deLaMateria ? ColorValido(lector["color_m"]) : null
                        });
                    }
                }
            }

            return lectura;
        }

        /// <summary>
        /// Las imagenes de esos SHA1 (solo las suyas). Nunca lanza: si falla, las
        /// que haya. Son como mucho 8 filas de 60 KB, muy por debajo del
        /// max_allowed_packet de 1 MB de MariaDB 5.5.
        /// </summary>
        public static Dictionary<string, byte[]> Imagenes(MySqlConnection conexion, int idProfesor, ICollection<string> sha1)
        {
            Dictionary<string, byte[]> imagenes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            List<string> pedir = (sha1 ?? new string[0]).Select(Sha1Valido).Where(s => s != null)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (pedir.Count == 0)
            {
                return imagenes;
            }

            try
            {
                // Los repetidos se quitan aqui: un GROUP BY con la columna BLOB
                // fallaria con el ONLY_FULL_GROUP_BY de MySQL 8.
                StringBuilder lista = new StringBuilder();
                using (MySqlCommand comando = new MySqlCommand())
                {
                    comando.Connection = conexion;
                    comando.Parameters.AddWithValue("@Profesor", idProfesor);
                    for (int i = 0; i < pedir.Count; i++)
                    {
                        lista.Append(i == 0 ? "" : ", ").Append("@s").Append(i.ToString(CultureInfo.InvariantCulture));
                        comando.Parameters.AddWithValue("@s" + i.ToString(CultureInfo.InvariantCulture), pedir[i]);
                    }

                    comando.CommandText = "SELECT imagen_sha1, imagen FROM personalizacion_clases "
                        + "WHERE fk_profesor = @Profesor AND imagen IS NOT NULL AND imagen_sha1 IN (" + lista + ")";
                    using (MySqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            string clave = Sha1Valido(lector["imagen_sha1"]);
                            byte[] datos = lector["imagen"] as byte[];
                            if (clave != null && datos != null && datos.Length > 0 && !imagenes.ContainsKey(clave))
                            {
                                imagenes[clave] = datos;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Leer las imagenes de las clases del profesor " + idProfesor, ex);
            }

            return imagenes;
        }

        /// <summary>
        /// Guarda lo de una clase, para el profesor de la sesion (nunca un id que
        /// llegue de la pantalla). Null si se guardo (despues = lo que queda, o
        /// null si no se pudo volver a leer); si no, el motivo para el profesor.
        /// Todo en una transaccion: si algo falla, no queda nada a medias.
        /// </summary>
        public string Guardar(PedidoPersonal pedido, out LecturaPersonal despues)
        {
            despues = null;
            Permisos.Exigir("Personalizar una clase");
            if (Datos_User.TipoUsuario != TiposUsuario.Profesor)
            {
                return "Solo quien imparte la clase puede personalizarla.";
            }

            if (pedido == null || pedido.MateriaId <= 0 || pedido.GrupoId <= 0)
            {
                return "No se sabe qué clase personalizar.";
            }

            string textoColor = null;
            if (pedido.Color != null)
            {
                System.Drawing.Color leido;
                if (!ColoresProfesor.DesdeTexto(pedido.Color, out leido))
                {
                    return "Ese color no es válido.";
                }

                textoColor = ColoresProfesor.ATexto(leido);
            }

            bool quito;
            string nombre = LimpiarNombre(pedido.NombreCorto, out quito);
            if (nombre != null && nombre.Length > LargoNombreCorto)
            {
                nombre = nombre.Substring(0, LargoNombreCorto).TrimEnd();
            }

            if (pedido.Imagen == CambioImagen.Nueva && !ImagenAceptable(pedido.Bytes))
            {
                return "No se pudo reducir la imagen lo suficiente. Prueba con otra.";
            }

            int destino = pedido.TodaLaMateria ? 0 : pedido.GrupoId;
            string yo = Datos_User.getUser();
            int idProfesor;
            string antesColor = null;
            string antesNombre = null;
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                MySqlTransaction transaccion = conexion.BeginTransaction();
                try
                {
                    if (!Esquema.PersonalizacionDeClases(conexion, transaccion))
                    {
                        transaccion.Rollback();
                        return FaltaMigracion;
                    }

                    // 1) Quien es y si la clase (esa materia con ese grupo) es suya.
                    using (MySqlCommand comando = new MySqlCommand(@"
                        SELECT u.idusuarios FROM usuarios u
                        WHERE u.numero_identificador = @Yo AND u.fk_tipo_usuario = 2
                          AND EXISTS (SELECT 1 FROM clases c WHERE c.usuarios_idusuarios = u.idusuarios
                                      AND c.materias_id_materia = @Materia AND c.grupos_idgrupos = @Grupo)
                        LIMIT 1", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Yo", yo ?? "");
                        comando.Parameters.AddWithValue("@Materia", pedido.MateriaId);
                        comando.Parameters.AddWithValue("@Grupo", pedido.GrupoId);
                        object id = comando.ExecuteScalar();
                        if (id == null || id == DBNull.Value)
                        {
                            transaccion.Rollback();
                            return YaNoEsSuya;
                        }

                        idProfesor = Convert.ToInt32(id);
                    }

                    // 2) De que fila sale hoy lo que se ve (la del grupo manda sobre la de la materia).
                    int? origen = null;
                    using (MySqlCommand comando = new MySqlCommand(
                        "SELECT fk_grupo, color, nombre_corto FROM personalizacion_clases "
                        + "WHERE fk_profesor = @P AND fk_materia = @M AND fk_grupo IN (0, @Grupo) ORDER BY fk_grupo DESC LIMIT 1", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@P", idProfesor);
                        comando.Parameters.AddWithValue("@M", pedido.MateriaId);
                        comando.Parameters.AddWithValue("@Grupo", pedido.GrupoId);
                        using (MySqlDataReader lector = comando.ExecuteReader())
                        {
                            if (lector.Read())
                            {
                                origen = Convert.ToInt32(lector["fk_grupo"]);
                                antesColor = ColorValido(lector["color"]);
                                antesNombre = Texto(lector["nombre_corto"]);
                            }
                        }
                    }

                    // 3) La fila de destino, con su color y su nombre.
                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO personalizacion_clases (fk_profesor, fk_materia, fk_grupo, color, nombre_corto, actualizado_en)
                        VALUES (@P, @M, @Destino, @Color, @Nombre, NOW())
                        ON DUPLICATE KEY UPDATE color = VALUES(color), nombre_corto = VALUES(nombre_corto), actualizado_en = VALUES(actualizado_en)",
                        conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@P", idProfesor);
                        comando.Parameters.AddWithValue("@M", pedido.MateriaId);
                        comando.Parameters.AddWithValue("@Destino", destino);
                        comando.Parameters.AddWithValue("@Color", (object)textoColor ?? DBNull.Value);
                        comando.Parameters.AddWithValue("@Nombre", (object)nombre ?? DBNull.Value);
                        comando.ExecuteNonQuery();
                    }

                    // 4) La imagen.
                    if (pedido.Imagen == CambioImagen.Nueva)
                    {
                        Ejecutar(conexion, transaccion, "UPDATE personalizacion_clases SET imagen = @Imagen "
                            + "WHERE fk_profesor = @P AND fk_materia = @M AND fk_grupo = @Destino", idProfesor, pedido.MateriaId, destino, pedido.Bytes);
                        // El SHA1 lo calcula el servidor sobre lo guardado: sin cifrado
                        // en .NET (FIPS) y sin mandar la imagen dos veces.
                        Ejecutar(conexion, transaccion, "UPDATE personalizacion_clases SET imagen_sha1 = SHA1(imagen) "
                            + "WHERE fk_profesor = @P AND fk_materia = @M AND fk_grupo = @Destino", idProfesor, pedido.MateriaId, destino, null);

                        // Con sql_mode vacio, lo que no cabe se corta sin error: se comprueba.
                        long largo = -1;
                        string sha1 = null;
                        using (MySqlCommand comando = new MySqlCommand("SELECT LENGTH(imagen) AS largo, imagen_sha1 FROM personalizacion_clases "
                            + "WHERE fk_profesor = @P AND fk_materia = @M AND fk_grupo = @Destino", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@P", idProfesor);
                            comando.Parameters.AddWithValue("@M", pedido.MateriaId);
                            comando.Parameters.AddWithValue("@Destino", destino);
                            using (MySqlDataReader lector = comando.ExecuteReader())
                            {
                                if (lector.Read())
                                {
                                    largo = lector["largo"] == DBNull.Value ? -1 : Convert.ToInt64(lector["largo"]);
                                    sha1 = Sha1Valido(lector["imagen_sha1"]);
                                }
                            }
                        }

                        if (largo != pedido.Bytes.Length || sha1 == null)
                        {
                            transaccion.Rollback();
                            Log.Aviso("Personalizacion de clase: la imagen llego cortada al servidor (" + largo + " de "
                                + pedido.Bytes.Length + " bytes). No se guardo nada.");
                            return ImagenCortada;
                        }

                        pedido.Sha1Guardada = sha1;
                    }
                    else if (pedido.Imagen == CambioImagen.Quitar)
                    {
                        Ejecutar(conexion, transaccion, "UPDATE personalizacion_clases SET imagen = NULL, imagen_sha1 = NULL "
                            + "WHERE fk_profesor = @P AND fk_materia = @M AND fk_grupo = @Destino", idProfesor, pedido.MateriaId, destino, null);
                    }
                    else if (origen.HasValue && origen.Value != destino)
                    {
                        // Igual, pero venia de otra fila: se copia en el servidor (el
                        // equipo no necesita tener los bytes).
                        using (MySqlCommand comando = new MySqlCommand(@"
                            UPDATE personalizacion_clases d
                            JOIN personalizacion_clases o ON o.fk_profesor = d.fk_profesor AND o.fk_materia = d.fk_materia AND o.fk_grupo = @Origen
                            SET d.imagen = o.imagen, d.imagen_sha1 = o.imagen_sha1
                            WHERE d.fk_profesor = @P AND d.fk_materia = @M AND d.fk_grupo = @Destino", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Origen", origen.Value);
                            comando.Parameters.AddWithValue("@P", idProfesor);
                            comando.Parameters.AddWithValue("@M", pedido.MateriaId);
                            comando.Parameters.AddWithValue("@Destino", destino);
                            comando.ExecuteNonQuery();
                        }
                    }

                    // 5) Toda la materia: sus grupos dejan de tener la suya.
                    if (pedido.TodaLaMateria)
                    {
                        Ejecutar(conexion, transaccion, "DELETE FROM personalizacion_clases "
                            + "WHERE fk_profesor = @P AND fk_materia = @M AND fk_grupo <> 0", idProfesor, pedido.MateriaId, destino, null);
                    }

                    // 6) La fila de la materia que quedo sin nada sobra. La de un grupo
                    // no: dice "este grupo va con lo de siempre, no con lo de la materia".
                    Ejecutar(conexion, transaccion, "DELETE FROM personalizacion_clases "
                        + "WHERE fk_profesor = @P AND fk_materia = @M AND fk_grupo = 0 "
                        + "AND color IS NULL AND nombre_corto IS NULL AND imagen_sha1 IS NULL", idProfesor, pedido.MateriaId, destino, null);

                    transaccion.Commit();
                }
                catch (Exception ex)
                {
                    try
                    {
                        transaccion.Rollback();
                    }
                    catch (Exception)
                    {
                        // La conexion ya no esta: el servidor deshace solo lo que no se confirmo.
                    }

                    // La causa y que hacer, sin el texto del driver (ese va al registro).
                    Log.Error("Guardar la personalizacion de una clase (materia " + pedido.MateriaId + ", grupo " + pedido.GrupoId + ")", ex);
                    return MensajeDeFallo.Explicar("No se pudo guardar", ex, AvisoDeFalloDocente.SiSigue);
                }
                finally
                {
                    transaccion.Dispose();
                }

                Log.Info("Personalizacion de clase: profesor " + Ascii(yo) + " cambio la materia " + pedido.MateriaId
                    + (pedido.TodaLaMateria ? " (todos sus grupos)" : " (grupo " + pedido.GrupoId + ")")
                    + ": color " + (antesColor ?? "automatico") + " -> " + (textoColor ?? "automatico")
                    + ", nombre '" + Ascii(antesNombre ?? "") + "' -> '" + Ascii(nombre ?? "") + "'"
                    + ", imagen " + (pedido.Imagen == CambioImagen.Nueva ? "nueva de " + Math.Max(1, (pedido.Bytes.Length + 512) / 1024) + " KB"
                        : pedido.Imagen == CambioImagen.Quitar ? "quitada" : "igual") + ".");

                try
                {
                    despues = Leer(conexion, idProfesor);
                }
                catch (Exception ex)
                {
                    // Ya se guardo: el panel lo vera en su siguiente lectura.
                    Log.Error("Volver a leer las clases personalizadas despues de guardar", ex);
                    despues = null;
                }

                return null;
            }
        }

        /// <summary>
        /// Trim, espacios seguidos a uno, sin caracteres de control ni nada fuera
        /// del BMP (emojis: la tabla es utf8 de 3 bytes, y en la escuela se
        /// mutilarian sin error); vacio = null. quitoAlgo dice si se quito algun
        /// caracter (no cuenta los espacios de sobra).
        /// </summary>
        public static string LimpiarNombre(string texto, out bool quitoAlgo)
        {
            quitoAlgo = false;
            if (string.IsNullOrEmpty(texto))
            {
                return null;
            }

            StringBuilder limpio = new StringBuilder(texto.Length);
            bool espacioPendiente = false;
            foreach (char c in texto)
            {
                if (char.IsWhiteSpace(c))
                {
                    espacioPendiente = limpio.Length > 0;
                    continue;
                }

                if (char.IsControl(c) || char.IsSurrogate(c) || c == '�')
                {
                    quitoAlgo = true;
                    continue;
                }

                if (espacioPendiente)
                {
                    limpio.Append(' ');
                    espacioPendiente = false;
                }

                limpio.Append(c);
            }

            return limpio.Length == 0 ? null : limpio.ToString();
        }

        /// <summary>PNG o JPEG por su firma, de 1 a BytesImagen bytes.</summary>
        public static bool ImagenAceptable(byte[] datos)
        {
            if (datos == null || datos.Length < 8 || datos.Length > BytesImagen)
            {
                return false;
            }

            bool png = datos[0] == 0x89 && datos[1] == 0x50 && datos[2] == 0x4E && datos[3] == 0x47;
            bool jpeg = datos[0] == 0xFF && datos[1] == 0xD8 && datos[2] == 0xFF;
            return png || jpeg;
        }

        private static void Ejecutar(MySqlConnection conexion, MySqlTransaction transaccion, string sql, int idProfesor, int idMateria, int destino, byte[] imagen)
        {
            using (MySqlCommand comando = new MySqlCommand(sql, conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@P", idProfesor);
                comando.Parameters.AddWithValue("@M", idMateria);
                comando.Parameters.AddWithValue("@Destino", destino);
                if (imagen != null)
                {
                    comando.Parameters.Add("@Imagen", MySqlDbType.MediumBlob).Value = imagen;
                }

                comando.ExecuteNonQuery();
            }
        }

        private static bool Verdad(object valor)
        {
            return valor != null && valor != DBNull.Value && Convert.ToInt64(valor, CultureInfo.InvariantCulture) != 0;
        }

        private static string Texto(object valor)
        {
            if (valor == null || valor == DBNull.Value)
            {
                return null;
            }

            string texto = Convert.ToString(valor, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        }

        /// <summary>'#RRGGBB' normalizado, o null si no lo es (un color escrito a mano mal cuenta como automatico).</summary>
        private static string ColorValido(object valor)
        {
            System.Drawing.Color color;
            return ColoresProfesor.DesdeTexto(Texto(valor), out color) ? ColoresProfesor.ATexto(color) : null;
        }

        private static string Sha1Valido(object valor)
        {
            string texto = Texto(valor);
            if (texto == null || texto.Length != 40)
            {
                return null;
            }

            foreach (char c in texto)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return null;
                }
            }

            return texto.ToLowerInvariant();
        }

        /// <summary>Para el log: sin acentos y solo ASCII.</summary>
        internal static string Ascii(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return texto ?? "";
            }

            StringBuilder limpio = new StringBuilder(texto.Length);
            foreach (char c in texto.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                limpio.Append(c < 128 && !char.IsControl(c) ? c : '?');
            }

            return limpio.ToString();
        }
    }
}
