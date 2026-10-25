using System;
using System.Globalization;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>
    /// La sesion de hoy de una clase mientras se da, con el reloj del servidor:
    /// el bloque que esta en curso (BloqueEnCurso), su sesion si ya existe (Leer)
    /// y, si ningun alumno ha registrado todavia, crearla (Crear). Lo usan
    /// "Reportar una falla" (db/15) y "Dar asistencia" (db/22): solo mientras
    /// dura la clase, la misma regla que quitar una asistencia (db/17). Todo en
    /// la conexion de Conexion.GetConexion, que fija -06:00: ningun metodo usa el
    /// reloj del equipo para decidir.
    /// </summary>
    internal static class SesionDeHoy
    {
        /// <summary>
        /// El bloque de esa clase que se esta dando ahora (inicio &lt;= ahora &lt; fin,
        /// reloj del servidor), o null: una franja suya que hoy se da (dia de la
        /// semana, vigencia de la clase y, con db/20, la de la franja; no
        /// cancelada hoy) o una sesion extra suya de hoy (tambien lo que le quedo
        /// a una clase recortada). La clase tiene que ser de ese profesor, activa y
        /// vigente. No cuentan los 15 minutos de antes ni la pausa entre dos
        /// bloques. Si hubiera dos a la vez, el que empezo antes.
        /// </summary>
        public static SesionAgenda BloqueEnCurso(MySqlConnection conexion, int idClase, int idProfesor)
        {
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT b.extra, b.id, b.lab, b.inicio, b.fin FROM (
                    SELECT 0 AS extra, h.idhorarios AS id, h.laboratorios_idlaboratorios AS lab, h.hora_inicio AS inicio, h.hora_fin AS fin
                    FROM horarios h
                    INNER JOIN clases c ON c.idclases = h.clases_idclases
                    WHERE h.clases_idclases = @Clase AND c.usuarios_idusuarios = @Profesor
                      AND h.dia_semana = WEEKDAY(CURDATE()) + 1
                      AND " + VigenciaFranjas.SeDa(conVigencia, "CURDATE()") + @"
                      AND h.hora_inicio <= CURTIME() AND CURTIME() < h.hora_fin
                      AND NOT EXISTS (SELECT 1 FROM horario_excepciones xc
                                      WHERE xc.tipo = 'cancelada' AND xc.horarios_idhorarios = h.idhorarios AND xc.fecha = CURDATE())
                    UNION ALL
                    SELECT 1, x.idhorario_excepciones, x.laboratorios_idlaboratorios, x.hora_inicio, x.hora_fin
                    FROM horario_excepciones x
                    INNER JOIN clases c ON c.idclases = x.clases_idclases
                    WHERE x.clases_idclases = @Clase AND c.usuarios_idusuarios = @Profesor AND x.tipo = 'extra' AND x.fecha = CURDATE()
                      AND c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta
                      AND x.hora_inicio <= CURTIME() AND CURTIME() < x.hora_fin
                ) b ORDER BY b.inicio LIMIT 1", conexion))
            {
                comando.Parameters.AddWithValue("@Clase", idClase);
                comando.Parameters.AddWithValue("@Profesor", idProfesor);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read() || reader.IsDBNull(2) || reader.IsDBNull(3) || reader.IsDBNull(4))
                    {
                        return null;
                    }

                    bool esExtra = Convert.ToInt32(reader[0]) == 1;
                    int id = Convert.ToInt32(reader[1]);
                    return new SesionAgenda
                    {
                        EsExtra = esExtra,
                        FranjaId = esExtra ? (int?)null : id,
                        ExcepcionId = esExtra ? id : (int?)null,
                        ClaseId = idClase,
                        ProfesorId = idProfesor,
                        LaboratorioId = Convert.ToInt32(reader[2]),
                        Inicio = reader.GetTimeSpan(3),
                        Fin = reader.GetTimeSpan(4)
                    };
                }
            }
        }

        /// <summary>La sesion de hoy de esa clase (franja y fecha, o su sesion extra), o null.</summary>
        public static int? Leer(MySqlConnection conexion, SesionAgenda clase)
        {
            using (MySqlCommand comando = new MySqlCommand(clase.EsExtra
                ? "SELECT idcodigos_accesos FROM codigos_accesos WHERE horario_excepciones_id = @Id ORDER BY idcodigos_accesos LIMIT 1"
                : "SELECT idcodigos_accesos FROM codigos_accesos WHERE horarios_idhorarios = @Id AND fecha = CURDATE() ORDER BY idcodigos_accesos LIMIT 1", conexion))
            {
                comando.Parameters.AddWithValue("@Id", clase.EsExtra ? clase.ExcepcionId.GetValueOrDefault() : clase.FranjaId.GetValueOrDefault());
                object resultado = comando.ExecuteScalar();
                return resultado == null || resultado == DBNull.Value ? (int?)null : Convert.ToInt32(resultado);
            }
        }

        /// <summary>
        /// Si ningun alumno ha registrado, la sesion aun no existe: se crea ahora,
        /// solo si la clase es suya, de hoy, activa y vigente, no cancelada y en
        /// curso (reloj del servidor), y el profesor no esta dado de baja. Su codigo
        /// es el que teclearan sus alumnos segun la forma de acceso del laboratorio
        /// que dice el servidor (no la que tenia el panel, que pudo cambiar): el
        /// fijo de la clase, el generado de la semana o, sin codigos, uno al azar,
        /// como la crearia el primer alumno. Null si no se pudo.
        /// </summary>
        /// <param name="paraQue">Para el log: "al reportar una falla", "al dar una asistencia".</param>
        public static int? Crear(MySqlConnection conexion, SesionAgenda clase, int idProfesor, string paraQue)
        {
            DateTime hoy;
            using (MySqlCommand comando = new MySqlCommand("SELECT CURDATE()", conexion))
            {
                hoy = Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture).Date;
            }

            int id = clase.EsExtra ? clase.ExcepcionId.GetValueOrDefault() : clase.FranjaId.GetValueOrDefault();
            ModoCodigo modo = ModosDeAcceso.Leer(conexion, clase.LaboratorioId);
            string codigo = modo == ModoCodigo.Fijo ? null
                : (modo == ModoCodigo.Renovable ? CodigosGenerados.ParaSesion(conexion, clase.EsExtra, id, hoy) : null)
                    ?? CodigosDeSesion.NuevoLibre(conexion, hoy);
            string profesorActivo = Esquema.TieneBajas(conexion) ? " AND u.activo = 1" : "";
            // La franja tiene que darse hoy tambien con su propia vigencia (db/20):
            // una terminada ya no abre sesion aunque su clase siga.
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
            string insertar = clase.EsExtra
                ? @"
                INSERT INTO codigos_accesos
                    (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos,
                     laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios, horario_excepciones_id)
                SELECT IFNULL(@Codigo, c.codigo), NOW(), CURDATE(), x.hora_inicio, x.hora_fin, c.materias_id_materia, c.grupos_idgrupos,
                       x.laboratorios_idlaboratorios, c.usuarios_idusuarios, NULL, x.idhorario_excepciones
                FROM horario_excepciones x
                INNER JOIN clases c   ON c.idclases = x.clases_idclases
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                WHERE x.idhorario_excepciones = @Id AND x.tipo = 'extra' AND x.fecha = CURDATE()
                  AND c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta" + profesorActivo + @"
                  AND c.usuarios_idusuarios = @Profesor
                  AND CURTIME() BETWEEN x.hora_inicio AND x.hora_fin"
                : @"
                INSERT INTO codigos_accesos
                    (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos,
                     laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios, horario_excepciones_id)
                SELECT IFNULL(@Codigo, c.codigo), NOW(), CURDATE(), h.hora_inicio, h.hora_fin, c.materias_id_materia, c.grupos_idgrupos,
                       h.laboratorios_idlaboratorios, c.usuarios_idusuarios, h.idhorarios, NULL
                FROM horarios h
                INNER JOIN clases c   ON c.idclases = h.clases_idclases
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                WHERE h.idhorarios = @Id AND h.dia_semana = WEEKDAY(CURDATE()) + 1
                  AND " + VigenciaFranjas.SeDa(conVigencia, "CURDATE()") + profesorActivo + @"
                  AND c.usuarios_idusuarios = @Profesor
                  AND CURTIME() BETWEEN h.hora_inicio AND h.hora_fin
                  AND NOT EXISTS (SELECT 1 FROM horario_excepciones xc
                                  WHERE xc.tipo = 'cancelada' AND xc.horarios_idhorarios = h.idhorarios AND xc.fecha = CURDATE())";
            try
            {
                using (MySqlCommand comando = new MySqlCommand(insertar, conexion))
                {
                    comando.Parameters.AddWithValue("@Codigo", codigo == null ? (object)DBNull.Value : codigo);
                    comando.Parameters.AddWithValue("@Id", id);
                    comando.Parameters.AddWithValue("@Profesor", idProfesor);
                    if (comando.ExecuteNonQuery() == 1)
                    {
                        Log.Info("Sesion " + comando.LastInsertedId + " creada " + paraQue + " el profesor " + idProfesor
                            + " (" + (clase.EsExtra ? "extra " : "franja ") + id + ").");
                        return (int)comando.LastInsertedId;
                    }
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                // La creo a la vez el primer alumno: vale esa.
                return Leer(conexion, clase);
            }

            return null;
        }
    }
}
