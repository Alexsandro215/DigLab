using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Por que un codigo no entra (o Aceptado).</summary>
    public enum MotivoCodigo
    {
        Aceptado,
        NoExiste,
        Inactiva,
        FueraDePeriodo,
        HoyNoHayClase,
        OtroLaboratorio,
        Cancelada,
        AunNoEmpieza,
        Expirado,
        YaRegistrado
    }

    public class ResultadoCodigo
    {
        public MotivoCodigo Motivo { get; set; }
        /// <summary>Texto para el alumno, ya redactado.</summary>
        public string Mensaje { get; set; }
        /// <summary>La sesion de hoy, solo si Motivo es Aceptado o YaRegistrado.</summary>
        public VentanaCodigo Ventana { get; set; }

        public bool Aceptado
        {
            get { return Motivo == MotivoCodigo.Aceptado; }
        }
    }

    /// <summary>Clase = profesor + grupo + materia + codigo fijo + vigencia.</summary>
    public class ClaseInfo
    {
        public int Id;
        public int ProfesorId;
        public string Profesor;
        public string NumeroEmpleado;
        public int GrupoId;
        public string Grupo;
        public int MateriaId;
        public string Materia;
        public string Codigo;
        public DateTime VigenteDesde;
        public DateTime VigenteHasta;
        public bool Activa;
        public int Sesiones;

        public string Descripcion
        {
            get { return Materia + " - " + Grupo + " - " + Profesor; }
        }

        public bool VigenteEn(DateTime fecha)
        {
            return Activa && fecha.Date >= VigenteDesde.Date && fecha.Date <= VigenteHasta.Date;
        }
    }

    /// <summary>Franja semanal de una clase en un laboratorio.</summary>
    public class FranjaInfo
    {
        public int Id;
        public int ClaseId;
        public int LaboratorioId;
        public string Laboratorio;
        /// <summary>1 = lunes ... 7 = domingo.</summary>
        public int DiaSemana;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public string Codigo;
        public int ProfesorId;
        public string Profesor;
        public int GrupoId;
        public string Grupo;
        public string Materia;
        public DateTime VigenteDesde;
        public DateTime VigenteHasta;
        public bool Activa;
    }

    /// <summary>Excepcion de un dia: 'cancelada' (una franja) o 'extra' (sesion fuera de horario).</summary>
    public class ExcepcionInfo
    {
        public int Id;
        public string Tipo;
        public DateTime Fecha;
        public int? FranjaId;
        public int ClaseId;
        public int LaboratorioId;
        public string Laboratorio;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public string Motivo;
        public string Codigo;
        public string Profesor;
        public string Grupo;
        public string Materia;

        public bool EsExtra
        {
            get { return Tipo == "extra"; }
        }
    }

    /// <summary>Una entrada de la agenda efectiva de un laboratorio en un dia.</summary>
    public class SesionAgenda
    {
        public bool EsExtra;
        public int? FranjaId;
        public int? ExcepcionId;
        public int ClaseId;
        public string Codigo;
        public string Profesor;
        public string Grupo;
        public string Materia;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public bool Cancelada;
        public string Motivo;
        public int ProfesorId;
        public int LaboratorioId;

        // Solo los rellena PanoramaDelDia: la sesion (codigos_accesos) de esta
        // entrada, si ya existe, y cuantos alumnos registraron en ella.
        public int? SesionId;
        public int Alumnos;
        public int ConFallas;

        public string Descripcion
        {
            get { return Materia + " - " + Grupo + " - " + Profesor; }
        }
    }

    /// <summary>Lo que el panel del administrador necesita de un dia.</summary>
    public class PanoramaLaboratorios
    {
        public DateTime AhoraServidor;
        public DateTime Fecha;
        public List<OpcionCombo> Laboratorios = new List<OpcionCombo>();
        /// <summary>Agenda efectiva del dia en todos los laboratorios, con sus conteos.</summary>
        public List<SesionAgenda> Agenda = new List<SesionAgenda>();
    }

    /// <summary>Lo que el panel del profesor necesita: quien es, su codigo y su dia.</summary>
    public class PanoramaProfesor
    {
        public DateTime AhoraServidor;
        public int ProfesorId;
        public string Nombre = "";
        public string NumeroEmpleado = "";
        /// <summary>True con un codigo por profesor (db/09).</summary>
        public bool CodigoPorProfesor;
        /// <summary>Su codigo (db/09), o null si todavia no tiene.</summary>
        public string Codigo;
        /// <summary>Sin la 09: sus clases vigentes, cada una con su propio codigo.</summary>
        public List<ClaseInfo> Clases = new List<ClaseInfo>();
        /// <summary>Sus clases de hoy (agenda efectiva, con canceladas y extras) y cuantos registraron.</summary>
        public List<SesionAgenda> Hoy = new List<SesionAgenda>();
        /// <summary>Nombre de cada laboratorio por id.</summary>
        public Dictionary<int, string> Laboratorios = new Dictionary<int, string>();
        /// <summary>Su horario semanal (clases activas), para decir cual es su proxima clase otro dia.</summary>
        public List<FranjaInfo> Franjas = new List<FranjaInfo>();

        public string Laboratorio(int id)
        {
            string nombre;
            return Laboratorios.TryGetValue(id, out nombre) ? nombre : "";
        }
    }

    /// <summary>
    /// Acceso a datos de la fase 6: clases con codigo fijo, horario semanal por
    /// laboratorio, excepciones por dia, agenda efectiva y resolucion del codigo
    /// que teclea el alumno (docs/fase6-horarios.md). Todo con el reloj del
    /// servidor MySQL.
    /// </summary>
    internal class HorariosDatos
    {
        public const int MargenAntesDelInicioMinutos = 15;
        public static readonly string[] NombresDia = { "", "Lunes", "Martes", "Miercoles", "Jueves", "Viernes", "Sabado", "Domingo" };

        private readonly Conexion mconexion = new Conexion();

        private const string SelectClase = @"
            SELECT c.idclases, c.usuarios_idusuarios, u.numero_identificador,
                   CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                   c.grupos_idgrupos, g.nombre_grupo, c.materias_id_materia, m.nombre_materia,
                   c.codigo, c.vigente_desde, c.vigente_hasta, c.activa,
                   (SELECT COUNT(*) FROM codigos_accesos ca
                      LEFT JOIN horarios h ON h.idhorarios = ca.horarios_idhorarios
                      LEFT JOIN horario_excepciones x ON x.idhorario_excepciones = ca.horario_excepciones_id
                     WHERE h.clases_idclases = c.idclases OR x.clases_idclases = c.idclases) AS sesiones
            FROM clases c
            INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
            INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
            INNER JOIN materias m ON m.id_materia = c.materias_id_materia";

        private const string SelectFranja = @"
            SELECT h.idhorarios, h.clases_idclases, h.laboratorios_idlaboratorios, l.nombre_laboratorio,
                   h.dia_semana, h.hora_inicio, h.hora_fin,
                   c.codigo, c.usuarios_idusuarios,
                   CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                   c.grupos_idgrupos, g.nombre_grupo, m.nombre_materia,
                   c.vigente_desde, c.vigente_hasta, c.activa
            FROM horarios h
            INNER JOIN clases c        ON c.idclases = h.clases_idclases
            INNER JOIN usuarios u      ON u.idusuarios = c.usuarios_idusuarios
            INNER JOIN grupos g        ON g.idgrupos = c.grupos_idgrupos
            INNER JOIN materias m      ON m.id_materia = c.materias_id_materia
            INNER JOIN laboratorios l  ON l.idlaboratorios = h.laboratorios_idlaboratorios";

        // ---------------------------------------------------------------------
        // Clases
        // ---------------------------------------------------------------------

        public List<ClaseInfo> ObtenerClases(bool soloActivas)
        {
            return LeerClases(SelectClase + (soloActivas ? " WHERE c.activa = 1" : "")
                + " ORDER BY c.activa DESC, m.nombre_materia, g.nombre_grupo", null);
        }

        public List<ClaseInfo> ObtenerClasesDeProfesor(int idProfesor)
        {
            return LeerClases(SelectClase + " WHERE c.usuarios_idusuarios = @Profesor ORDER BY c.activa DESC, m.nombre_materia, g.nombre_grupo",
                cmd => cmd.Parameters.AddWithValue("@Profesor", idProfesor));
        }

        public ClaseInfo ObtenerClase(int idClase)
        {
            return LeerClases(SelectClase + " WHERE c.idclases = @Id", cmd => cmd.Parameters.AddWithValue("@Id", idClase)).FirstOrDefault();
        }

        private List<ClaseInfo> LeerClases(string consulta, Action<MySqlCommand> parametros)
        {
            List<ClaseInfo> lista = new List<ClaseInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            {
                if (parametros != null)
                {
                    parametros(comando);
                }

                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ClaseInfo
                        {
                            Id = reader.GetInt32("idclases"),
                            ProfesorId = reader.GetInt32("usuarios_idusuarios"),
                            NumeroEmpleado = reader.GetString("numero_identificador"),
                            Profesor = reader.GetString("profesor"),
                            GrupoId = reader.GetInt32("grupos_idgrupos"),
                            Grupo = reader.GetString("nombre_grupo"),
                            MateriaId = reader.GetInt32("materias_id_materia"),
                            Materia = reader.GetString("nombre_materia"),
                            Codigo = reader.GetString("codigo"),
                            VigenteDesde = reader.GetDateTime("vigente_desde"),
                            VigenteHasta = reader.GetDateTime("vigente_hasta"),
                            Activa = reader.GetBoolean("activa"),
                            Sesiones = Convert.ToInt32(reader["sesiones"])
                        });
                    }
                }
            }

            return lista;
        }

        /// <summary>
        /// Profesores (tipo de usuario 2) para el combo de la pantalla de clases.
        /// Los dados de baja no salen, salvo el que se indique: el de la clase que
        /// se esta editando, para que no se quede sin profesor en el combo.
        /// </summary>
        public List<OpcionCombo> ObtenerProfesores(int? incluir = null)
        {
            List<OpcionCombo> lista = new List<OpcionCombo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                string soloActivos = Esquema.TieneBajas(conexion) ? " AND (activo = 1 OR idusuarios = @Incluir)" : "";
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT idusuarios, CONCAT_WS(' ', nombre, apellido_paterno, apellido_materno) AS nombre_completo
                    FROM usuarios
                    WHERE fk_tipo_usuario = 2" + soloActivos + @"
                    ORDER BY apellido_paterno, nombre", conexion))
                {
                    comando.Parameters.AddWithValue("@Incluir", incluir ?? 0);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new OpcionCombo
                            {
                                Id = reader.GetInt32("idusuarios"),
                                Texto = reader.GetString("nombre_completo")
                            });
                        }
                    }
                }
            }

            return lista;
        }

        /// <summary>Laboratorios en uso (sin los desactivados), para Horarios.</summary>
        public List<OpcionCombo> ObtenerLaboratoriosActivos()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LeerLaboratorios(conexion);
            }
        }

        private static List<OpcionCombo> LeerLaboratorios(MySqlConnection conexion)
        {
            List<OpcionCombo> lista = new List<OpcionCombo>();
            string soloActivos = Esquema.LaboratoriosDesactivables(conexion) ? " WHERE activo = 1" : "";
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idlaboratorios, nombre_laboratorio FROM laboratorios" + soloActivos + " ORDER BY nombre_laboratorio", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    lista.Add(new OpcionCombo
                    {
                        Id = Convert.ToInt32(reader["idlaboratorios"]),
                        Texto = reader["nombre_laboratorio"].ToString()
                    });
                }
            }

            return lista;
        }

        /// <summary>Un codigo de 5 caracteres que no este en uso.</summary>
        public string ProponerCodigo()
        {
            for (int intento = 0; intento < 50; intento++)
            {
                string codigo = CodigoGenerador.GenerarCodigoAleatorio(5);
                if (CodigoDisponible(codigo, null))
                {
                    return codigo;
                }
            }

            throw new InvalidOperationException("No se pudo proponer un codigo libre.");
        }

        public bool CodigoDisponible(string codigo, int? excluirClaseId)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT COUNT(*) FROM clases WHERE codigo = @Codigo AND idclases <> @Excluir", conexion))
            {
                comando.Parameters.AddWithValue("@Codigo", codigo);
                comando.Parameters.AddWithValue("@Excluir", excluirClaseId ?? 0);
                return Convert.ToInt32(comando.ExecuteScalar()) == 0;
            }
        }

        /// <summary>True si la base tiene un codigo por profesor (db/09): las clases toman el suyo.</summary>
        public bool UsaCodigoPorProfesor()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return Esquema.CodigoPorProfesor(conexion);
            }
        }

        /// <summary>El codigo del profesor, o null si aun no tiene (se le da al crearle su primera clase).</summary>
        public string CodigoDelProfesor(int idProfesor)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return CodigosDeProfesor.Leer(conexion, null, idProfesor);
            }
        }

        /// <summary>
        /// Con un codigo por profesor (db/09) el codigo que llega se ignora: la
        /// clase toma el de su profesor, que se crea si aun no tiene. Sin la 09,
        /// cada clase lleva el suyo, como antes.
        /// </summary>
        public int CrearClase(int idProfesor, int idGrupo, int idMateria, string codigo, DateTime desde, DateTime hasta, int creadaPor)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    codigo = CodigoParaClase(conexion, transaccion, idProfesor, codigo);
                    ValidarClase(codigo, desde, hasta);
                    int id;
                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta, activa, creada_por, creada_en)
                        VALUES (@Profesor, @Grupo, @Materia, @Codigo, @Desde, @Hasta, 1, @CreadaPor, NOW());
                        SELECT LAST_INSERT_ID();", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Profesor", idProfesor);
                        comando.Parameters.AddWithValue("@Grupo", idGrupo);
                        comando.Parameters.AddWithValue("@Materia", idMateria);
                        comando.Parameters.AddWithValue("@Codigo", codigo);
                        comando.Parameters.AddWithValue("@Desde", desde.Date);
                        comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                        comando.Parameters.AddWithValue("@CreadaPor", creadaPor > 0 ? (object)creadaPor : DBNull.Value);
                        id = Convert.ToInt32(comando.ExecuteScalar());
                    }

                    transaccion.Commit();
                    Log.Info("Clase creada: id " + id + ", codigo " + codigo + ".");
                    return id;
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                throw new InvalidOperationException("El codigo '" + codigo + "' ya esta en uso por otra clase.");
            }
        }

        public void ActualizarClase(int idClase, int idProfesor, int idGrupo, int idMateria, string codigo, DateTime desde, DateTime hasta)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    // Si cambia de profesor, pasa a tener el codigo del nuevo.
                    codigo = CodigoParaClase(conexion, transaccion, idProfesor, codigo);
                    ValidarClase(codigo, desde, hasta);
                    using (MySqlCommand comando = new MySqlCommand(@"
                        UPDATE clases
                           SET usuarios_idusuarios = @Profesor, grupos_idgrupos = @Grupo, materias_id_materia = @Materia,
                               codigo = @Codigo, vigente_desde = @Desde, vigente_hasta = @Hasta
                         WHERE idclases = @Id", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Id", idClase);
                        comando.Parameters.AddWithValue("@Profesor", idProfesor);
                        comando.Parameters.AddWithValue("@Grupo", idGrupo);
                        comando.Parameters.AddWithValue("@Materia", idMateria);
                        comando.Parameters.AddWithValue("@Codigo", codigo);
                        comando.Parameters.AddWithValue("@Desde", desde.Date);
                        comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                        comando.ExecuteNonQuery();
                    }

                    transaccion.Commit();
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                throw new InvalidOperationException("El codigo '" + codigo + "' ya esta en uso por otra clase.");
            }
        }

        private static string CodigoParaClase(MySqlConnection conexion, MySqlTransaction transaccion, int idProfesor, string codigo)
        {
            return Esquema.CodigoPorProfesor(conexion, transaccion)
                ? CodigosDeProfesor.ObtenerOCrear(conexion, transaccion, idProfesor)
                : (codigo ?? "").Trim();
        }

        public void CambiarEstadoClase(int idClase, bool activa)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand("UPDATE clases SET activa = @Activa WHERE idclases = @Id", conexion))
            {
                comando.Parameters.AddWithValue("@Activa", activa ? 1 : 0);
                comando.Parameters.AddWithValue("@Id", idClase);
                comando.ExecuteNonQuery();
            }
        }

        private static void ValidarClase(string codigo, DateTime desde, DateTime hasta)
        {
            if (string.IsNullOrWhiteSpace(codigo) || codigo.Trim().Length > 16)
            {
                throw new InvalidOperationException("El codigo tiene que tener entre 1 y 16 caracteres.");
            }

            if (hasta.Date < desde.Date)
            {
                throw new InvalidOperationException("La fecha de fin de vigencia no puede ser anterior a la de inicio.");
            }
        }

        // ---------------------------------------------------------------------
        // Franjas (horario semanal)
        // ---------------------------------------------------------------------

        public List<FranjaInfo> ObtenerFranjasDeLaboratorio(int idLaboratorio)
        {
            return LeerFranjas(SelectFranja + " WHERE h.laboratorios_idlaboratorios = @Lab ORDER BY h.dia_semana, h.hora_inicio",
                cmd => cmd.Parameters.AddWithValue("@Lab", idLaboratorio));
        }

        public List<FranjaInfo> ObtenerFranjasDeClase(int idClase)
        {
            return LeerFranjas(SelectFranja + " WHERE h.clases_idclases = @Clase ORDER BY h.dia_semana, h.hora_inicio",
                cmd => cmd.Parameters.AddWithValue("@Clase", idClase));
        }

        public List<FranjaInfo> ObtenerFranjasDeProfesor(int idProfesor)
        {
            return LeerFranjas(SelectFranja + " WHERE c.usuarios_idusuarios = @Profesor AND c.activa = 1 ORDER BY h.dia_semana, h.hora_inicio",
                cmd => cmd.Parameters.AddWithValue("@Profesor", idProfesor));
        }

        private List<FranjaInfo> LeerFranjas(string consulta, Action<MySqlCommand> parametros)
        {
            List<FranjaInfo> lista = new List<FranjaInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(consulta, conexion))
            {
                parametros(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new FranjaInfo
                        {
                            Id = reader.GetInt32("idhorarios"),
                            ClaseId = reader.GetInt32("clases_idclases"),
                            LaboratorioId = reader.GetInt32("laboratorios_idlaboratorios"),
                            Laboratorio = reader.GetString("nombre_laboratorio"),
                            DiaSemana = reader.GetInt32("dia_semana"),
                            Inicio = reader.GetTimeSpan("hora_inicio"),
                            Fin = reader.GetTimeSpan("hora_fin"),
                            Codigo = reader.GetString("codigo"),
                            ProfesorId = reader.GetInt32("usuarios_idusuarios"),
                            Profesor = reader.GetString("profesor"),
                            GrupoId = reader.GetInt32("grupos_idgrupos"),
                            Grupo = reader.GetString("nombre_grupo"),
                            Materia = reader.GetString("nombre_materia"),
                            VigenteDesde = reader.GetDateTime("vigente_desde"),
                            VigenteHasta = reader.GetDateTime("vigente_hasta"),
                            Activa = reader.GetBoolean("activa")
                        });
                    }
                }
            }

            return lista;
        }

        /// <summary>
        /// Devuelve null si la franja se puede guardar, o el conflicto en texto:
        /// mismo laboratorio, mismo profesor o mismo grupo a la misma hora, entre
        /// clases activas cuyas vigencias se solapan.
        /// </summary>
        public string ValidarFranja(int? excluirFranjaId, int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            if (diaSemana < 1 || diaSemana > 7)
            {
                return "El dia de la semana no es valido.";
            }

            if (fin <= inicio)
            {
                return "La hora de fin tiene que ser posterior a la de inicio.";
            }

            ClaseInfo clase = ObtenerClase(idClase);
            if (clase == null)
            {
                return "La clase no existe.";
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT l.nombre_laboratorio, c.codigo, m.nombre_materia, g.nombre_grupo, h.hora_inicio, h.hora_fin,
                       (h.laboratorios_idlaboratorios = @Lab) AS mismo_lab,
                       (c.usuarios_idusuarios = @Profesor) AS mismo_profesor,
                       (c.grupos_idgrupos = @Grupo) AS mismo_grupo
                FROM horarios h
                INNER JOIN clases c       ON c.idclases = h.clases_idclases
                INNER JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m     ON m.id_materia = c.materias_id_materia
                INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                WHERE h.dia_semana = @Dia
                  AND h.idhorarios <> @Excluir
                  AND c.activa = 1
                  AND h.hora_inicio < @Fin AND h.hora_fin > @Inicio
                  AND c.vigente_desde <= @Hasta AND c.vigente_hasta >= @Desde
                  AND (h.laboratorios_idlaboratorios = @Lab OR c.usuarios_idusuarios = @Profesor OR c.grupos_idgrupos = @Grupo)
                ORDER BY h.hora_inicio
                LIMIT 1", conexion))
            {
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Profesor", clase.ProfesorId);
                comando.Parameters.AddWithValue("@Grupo", clase.GrupoId);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Excluir", excluirFranjaId ?? 0);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.Parameters.AddWithValue("@Desde", clase.VigenteDesde.Date);
                comando.Parameters.AddWithValue("@Hasta", clase.VigenteHasta.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return DescribirConflicto(reader);
                }
            }
        }

        private static string DescribirConflicto(MySqlDataReader reader)
        {
            List<string> motivos = new List<string>();
            if (reader.GetBoolean("mismo_lab")) motivos.Add("mismo laboratorio");
            if (reader.GetBoolean("mismo_profesor")) motivos.Add("mismo profesor");
            if (reader.GetBoolean("mismo_grupo")) motivos.Add("mismo grupo");

            return "Se solapa con " + reader.GetString("nombre_materia") + " - " + reader.GetString("nombre_grupo")
                + " (" + reader.GetString("codigo") + ") de " + Hora(reader.GetTimeSpan("hora_inicio")) + " a " + Hora(reader.GetTimeSpan("hora_fin"))
                + " en " + reader.GetString("nombre_laboratorio") + ": " + string.Join(", ", motivos) + ".";
        }

        public int CrearFranja(int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            string conflicto = ValidarFranja(null, idClase, idLaboratorio, diaSemana, inicio, fin);
            if (conflicto != null)
            {
                throw new InvalidOperationException(conflicto);
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin)
                VALUES (@Clase, @Lab, @Dia, @Inicio, @Fin);
                SELECT LAST_INSERT_ID();", conexion))
            {
                comando.Parameters.AddWithValue("@Clase", idClase);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                return Convert.ToInt32(comando.ExecuteScalar());
            }
        }

        public void ActualizarFranja(int idFranja, int idClase, int idLaboratorio, int diaSemana, TimeSpan inicio, TimeSpan fin)
        {
            string conflicto = ValidarFranja(idFranja, idClase, idLaboratorio, diaSemana, inicio, fin);
            if (conflicto != null)
            {
                throw new InvalidOperationException(conflicto);
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                UPDATE horarios
                   SET clases_idclases = @Clase, laboratorios_idlaboratorios = @Lab, dia_semana = @Dia, hora_inicio = @Inicio, hora_fin = @Fin
                 WHERE idhorarios = @Id", conexion))
            {
                comando.Parameters.AddWithValue("@Id", idFranja);
                comando.Parameters.AddWithValue("@Clase", idClase);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Dia", diaSemana);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.ExecuteNonQuery();
            }
        }

        public void BorrarFranja(int idFranja)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand("DELETE FROM horarios WHERE idhorarios = @Id", conexion))
            {
                comando.Parameters.AddWithValue("@Id", idFranja);
                comando.ExecuteNonQuery();
            }
        }

        // ---------------------------------------------------------------------
        // Excepciones
        // ---------------------------------------------------------------------

        public List<ExcepcionInfo> ObtenerExcepciones(DateTime desde, DateTime hasta, int idLaboratorio)
        {
            List<ExcepcionInfo> lista = new List<ExcepcionInfo>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT x.idhorario_excepciones, x.tipo, x.fecha, x.horarios_idhorarios, x.motivo,
                       COALESCE(x.clases_idclases, h.clases_idclases) AS clase_id,
                       COALESCE(x.laboratorios_idlaboratorios, h.laboratorios_idlaboratorios) AS lab_id,
                       l.nombre_laboratorio,
                       COALESCE(x.hora_inicio, h.hora_inicio) AS inicio,
                       COALESCE(x.hora_fin, h.hora_fin) AS fin,
                       c.codigo, CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                       g.nombre_grupo, m.nombre_materia
                FROM horario_excepciones x
                LEFT JOIN horarios h      ON h.idhorarios = x.horarios_idhorarios
                INNER JOIN clases c       ON c.idclases = COALESCE(x.clases_idclases, h.clases_idclases)
                INNER JOIN usuarios u     ON u.idusuarios = c.usuarios_idusuarios
                INNER JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m     ON m.id_materia = c.materias_id_materia
                INNER JOIN laboratorios l ON l.idlaboratorios = COALESCE(x.laboratorios_idlaboratorios, h.laboratorios_idlaboratorios)
                WHERE x.fecha BETWEEN @Desde AND @Hasta
                  AND (@Lab = 0 OR COALESCE(x.laboratorios_idlaboratorios, h.laboratorios_idlaboratorios) = @Lab)
                ORDER BY x.fecha, inicio", conexion))
            {
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new ExcepcionInfo
                        {
                            Id = reader.GetInt32("idhorario_excepciones"),
                            Tipo = reader.GetString("tipo"),
                            Fecha = reader.GetDateTime("fecha"),
                            FranjaId = reader.IsDBNull(reader.GetOrdinal("horarios_idhorarios")) ? (int?)null : reader.GetInt32("horarios_idhorarios"),
                            ClaseId = reader.GetInt32("clase_id"),
                            LaboratorioId = reader.GetInt32("lab_id"),
                            Laboratorio = reader.GetString("nombre_laboratorio"),
                            Inicio = reader.GetTimeSpan("inicio"),
                            Fin = reader.GetTimeSpan("fin"),
                            Motivo = reader.IsDBNull(reader.GetOrdinal("motivo")) ? "" : reader.GetString("motivo"),
                            Codigo = reader.GetString("codigo"),
                            Profesor = reader.GetString("profesor"),
                            Grupo = reader.GetString("nombre_grupo"),
                            Materia = reader.GetString("nombre_materia")
                        });
                    }
                }
            }

            return lista;
        }

        public int CancelarFranja(int idFranja, DateTime fecha, string motivo, int creadaPor)
        {
            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                using (MySqlCommand comando = new MySqlCommand(@"
                    INSERT INTO horario_excepciones (tipo, fecha, horarios_idhorarios, motivo, creada_por, creada_en)
                    VALUES ('cancelada', @Fecha, @Franja, @Motivo, @CreadaPor, NOW());
                    SELECT LAST_INSERT_ID();", conexion))
                {
                    comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                    comando.Parameters.AddWithValue("@Franja", idFranja);
                    comando.Parameters.AddWithValue("@Motivo", string.IsNullOrWhiteSpace(motivo) ? (object)DBNull.Value : motivo.Trim());
                    comando.Parameters.AddWithValue("@CreadaPor", creadaPor > 0 ? (object)creadaPor : DBNull.Value);
                    return Convert.ToInt32(comando.ExecuteScalar());
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                throw new InvalidOperationException("Esa franja ya esta cancelada para el " + fecha.ToString("dd/MM/yyyy") + ".");
            }
        }

        /// <summary>
        /// Devuelve null si la sesion extra cabe, o el conflicto: contra las
        /// franjas efectivas de ese dia (no canceladas, de clases vigentes) y
        /// contra otras extras, por laboratorio, profesor o grupo.
        /// </summary>
        public string ValidarExtra(int? excluirExcepcionId, int idClase, int idLaboratorio, DateTime fecha, TimeSpan inicio, TimeSpan fin)
        {
            if (fin <= inicio)
            {
                return "La hora de fin tiene que ser posterior a la de inicio.";
            }

            ClaseInfo clase = ObtenerClase(idClase);
            if (clase == null)
            {
                return "La clase no existe.";
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT l.nombre_laboratorio, c.codigo, m.nombre_materia, g.nombre_grupo, h.hora_inicio, h.hora_fin,
                           (h.laboratorios_idlaboratorios = @Lab) AS mismo_lab,
                           (c.usuarios_idusuarios = @Profesor) AS mismo_profesor,
                           (c.grupos_idgrupos = @Grupo) AS mismo_grupo
                    FROM horarios h
                    INNER JOIN clases c       ON c.idclases = h.clases_idclases
                    INNER JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                    INNER JOIN materias m     ON m.id_materia = c.materias_id_materia
                    INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                    WHERE h.dia_semana = WEEKDAY(@Fecha) + 1
                      AND c.activa = 1
                      AND @Fecha BETWEEN c.vigente_desde AND c.vigente_hasta
                      AND h.hora_inicio < @Fin AND h.hora_fin > @Inicio
                      AND (h.laboratorios_idlaboratorios = @Lab OR c.usuarios_idusuarios = @Profesor OR c.grupos_idgrupos = @Grupo)
                      AND NOT EXISTS (SELECT 1 FROM horario_excepciones x
                                       WHERE x.tipo = 'cancelada' AND x.horarios_idhorarios = h.idhorarios AND x.fecha = @Fecha)
                    ORDER BY h.hora_inicio
                    LIMIT 1", conexion))
                {
                    AgregarParametrosConflicto(comando, clase, idLaboratorio, fecha, inicio, fin, 0);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return DescribirConflicto(reader);
                        }
                    }
                }

                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT l.nombre_laboratorio, c.codigo, m.nombre_materia, g.nombre_grupo, x.hora_inicio, x.hora_fin,
                           (x.laboratorios_idlaboratorios = @Lab) AS mismo_lab,
                           (c.usuarios_idusuarios = @Profesor) AS mismo_profesor,
                           (c.grupos_idgrupos = @Grupo) AS mismo_grupo
                    FROM horario_excepciones x
                    INNER JOIN clases c       ON c.idclases = x.clases_idclases
                    INNER JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                    INNER JOIN materias m     ON m.id_materia = c.materias_id_materia
                    INNER JOIN laboratorios l ON l.idlaboratorios = x.laboratorios_idlaboratorios
                    WHERE x.tipo = 'extra' AND x.fecha = @Fecha AND x.idhorario_excepciones <> @Excluir
                      AND x.hora_inicio < @Fin AND x.hora_fin > @Inicio
                      AND (x.laboratorios_idlaboratorios = @Lab OR c.usuarios_idusuarios = @Profesor OR c.grupos_idgrupos = @Grupo)
                    ORDER BY x.hora_inicio
                    LIMIT 1", conexion))
                {
                    AgregarParametrosConflicto(comando, clase, idLaboratorio, fecha, inicio, fin, excluirExcepcionId ?? 0);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return DescribirConflicto(reader) + " (sesion extra)";
                        }
                    }
                }
            }

            return null;
        }

        private static void AgregarParametrosConflicto(MySqlCommand comando, ClaseInfo clase, int idLaboratorio, DateTime fecha, TimeSpan inicio, TimeSpan fin, int excluir)
        {
            comando.Parameters.AddWithValue("@Lab", idLaboratorio);
            comando.Parameters.AddWithValue("@Profesor", clase.ProfesorId);
            comando.Parameters.AddWithValue("@Grupo", clase.GrupoId);
            comando.Parameters.AddWithValue("@Fecha", fecha.Date);
            comando.Parameters.AddWithValue("@Inicio", inicio);
            comando.Parameters.AddWithValue("@Fin", fin);
            comando.Parameters.AddWithValue("@Excluir", excluir);
        }

        public int CrearExtra(int idClase, int idLaboratorio, DateTime fecha, TimeSpan inicio, TimeSpan fin, string motivo, int creadaPor)
        {
            return CrearExtra(idClase, idLaboratorio, fecha, inicio, fin, motivo, creadaPor, false);
        }

        /// <summary>
        /// Con forzar en true no se comprueban solapes: lo usa la cuadricula del
        /// administrador cuando aplica una excepcion encima del horario (las
        /// franjas que chocan se cancelan ese dia por separado, porque ya se
        /// hablo con esos maestros).
        /// </summary>
        public int CrearExtra(int idClase, int idLaboratorio, DateTime fecha, TimeSpan inicio, TimeSpan fin, string motivo, int creadaPor, bool forzar)
        {
            if (fin <= inicio)
            {
                throw new InvalidOperationException("La hora de fin tiene que ser posterior a la de inicio.");
            }

            string conflicto = forzar ? null : ValidarExtra(null, idClase, idLaboratorio, fecha, inicio, fin);
            if (conflicto != null)
            {
                throw new InvalidOperationException(conflicto);
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                INSERT INTO horario_excepciones (tipo, fecha, clases_idclases, laboratorios_idlaboratorios, hora_inicio, hora_fin, motivo, creada_por, creada_en)
                VALUES ('extra', @Fecha, @Clase, @Lab, @Inicio, @Fin, @Motivo, @CreadaPor, NOW());
                SELECT LAST_INSERT_ID();", conexion))
            {
                comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                comando.Parameters.AddWithValue("@Clase", idClase);
                comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                comando.Parameters.AddWithValue("@Inicio", inicio);
                comando.Parameters.AddWithValue("@Fin", fin);
                comando.Parameters.AddWithValue("@Motivo", string.IsNullOrWhiteSpace(motivo) ? (object)DBNull.Value : motivo.Trim());
                comando.Parameters.AddWithValue("@CreadaPor", creadaPor > 0 ? (object)creadaPor : DBNull.Value);
                return Convert.ToInt32(comando.ExecuteScalar());
            }
        }

        public void BorrarExcepcion(int idExcepcion)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand("DELETE FROM horario_excepciones WHERE idhorario_excepciones = @Id", conexion))
            {
                comando.Parameters.AddWithValue("@Id", idExcepcion);
                comando.ExecuteNonQuery();
            }
        }

        // ---------------------------------------------------------------------
        // Agenda efectiva
        // ---------------------------------------------------------------------

        /// <summary>
        /// Lo que hay en un laboratorio un dia: franjas de clases vigentes y
        /// activas (con su cancelacion, si la hay) mas las sesiones extra.
        /// </summary>
        public List<SesionAgenda> AgendaDelDia(int idLaboratorio, DateTime fecha)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LeerAgenda(conexion, idLaboratorio, fecha);
            }
        }

        /// <summary>
        /// La agenda efectiva de un dia en un laboratorio o, sin laboratorio, en
        /// todos (ordenada por laboratorio y hora).
        /// </summary>
        private static List<SesionAgenda> LeerAgenda(MySqlConnection conexion, int? idLaboratorio, DateTime fecha)
        {
            string filtroFranja = idLaboratorio.HasValue ? "h.laboratorios_idlaboratorios = @Lab AND " : "";
            string filtroExtra = idLaboratorio.HasValue ? " AND x.laboratorios_idlaboratorios = @Lab" : "";
            string orden = idLaboratorio.HasValue ? "inicio" : "laboratorio_id, inicio";

            List<SesionAgenda> lista = new List<SesionAgenda>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT 0 AS es_extra, h.idhorarios AS franja_id, NULL AS excepcion_id, c.idclases, c.codigo,
                       c.usuarios_idusuarios AS profesor_id,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                       g.nombre_grupo, m.nombre_materia, h.laboratorios_idlaboratorios AS laboratorio_id,
                       h.hora_inicio AS inicio, h.hora_fin AS fin,
                       (x.idhorario_excepciones IS NOT NULL) AS cancelada, x.motivo
                FROM horarios h
                INNER JOIN clases c   ON c.idclases = h.clases_idclases
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                LEFT JOIN horario_excepciones x ON x.tipo = 'cancelada' AND x.horarios_idhorarios = h.idhorarios AND x.fecha = @Fecha
                WHERE " + filtroFranja + @"h.dia_semana = WEEKDAY(@Fecha) + 1
                  AND c.activa = 1
                  AND @Fecha BETWEEN c.vigente_desde AND c.vigente_hasta
                UNION ALL
                SELECT 1, NULL, x.idhorario_excepciones, c.idclases, c.codigo,
                       c.usuarios_idusuarios,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno),
                       g.nombre_grupo, m.nombre_materia, x.laboratorios_idlaboratorios,
                       x.hora_inicio, x.hora_fin, 0, x.motivo
                FROM horario_excepciones x
                INNER JOIN clases c   ON c.idclases = x.clases_idclases
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                WHERE x.tipo = 'extra'" + filtroExtra + @" AND x.fecha = @Fecha
                ORDER BY " + orden, conexion))
            {
                if (idLaboratorio.HasValue)
                {
                    comando.Parameters.AddWithValue("@Lab", idLaboratorio.Value);
                }

                comando.Parameters.AddWithValue("@Fecha", fecha.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new SesionAgenda
                        {
                            EsExtra = Convert.ToInt32(reader["es_extra"]) == 1,
                            FranjaId = reader.IsDBNull(reader.GetOrdinal("franja_id")) ? (int?)null : Convert.ToInt32(reader["franja_id"]),
                            ExcepcionId = reader.IsDBNull(reader.GetOrdinal("excepcion_id")) ? (int?)null : Convert.ToInt32(reader["excepcion_id"]),
                            ClaseId = reader.GetInt32("idclases"),
                            Codigo = reader.GetString("codigo"),
                            Profesor = reader.GetString("profesor"),
                            Grupo = reader.GetString("nombre_grupo"),
                            Materia = reader.GetString("nombre_materia"),
                            Inicio = reader.GetTimeSpan("inicio"),
                            Fin = reader.GetTimeSpan("fin"),
                            Cancelada = Convert.ToInt32(reader["cancelada"]) == 1,
                            Motivo = reader.IsDBNull(reader.GetOrdinal("motivo")) ? "" : reader.GetString("motivo"),
                            ProfesorId = Convert.ToInt32(reader["profesor_id"]),
                            LaboratorioId = reader.IsDBNull(reader.GetOrdinal("laboratorio_id")) ? 0 : Convert.ToInt32(reader["laboratorio_id"])
                        });
                    }
                }
            }

            return lista;
        }

        /// <summary>
        /// Para el panel del administrador: todos los laboratorios, la agenda de
        /// un dia en cada uno y, por cada clase que ya tenga sesion, cuantos
        /// alumnos registraron bitacora y cuantos reportaron alguna falla (el
        /// mismo criterio que la pantalla de bitacoras). Una conexion, cuatro
        /// consultas. Sin fecha, el dia de hoy segun el servidor.
        /// </summary>
        public PanoramaLaboratorios PanoramaDelDia(DateTime? fecha = null)
        {
            PanoramaLaboratorios panorama = new PanoramaLaboratorios();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
                {
                    panorama.AhoraServidor = Convert.ToDateTime(comando.ExecuteScalar());
                }

                panorama.Fecha = (fecha ?? panorama.AhoraServidor).Date;
                panorama.Laboratorios = LeerLaboratorios(conexion);
                panorama.Agenda = LeerAgenda(conexion, null, panorama.Fecha);

                // La sesion de una franja es la de su dia; la de una sesion extra,
                // la de su excepcion (ver codigos_accesos en db/01_schema.sql).
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT ca.idcodigos_accesos, ca.horarios_idhorarios, ca.horario_excepciones_id,
                           COUNT(DISTINCT rb.fk_usuario) AS alumnos,
                           COUNT(DISTINCT CASE WHEN " + Consultas.CondicionConFalla + @" THEN rb.fk_usuario END) AS con_fallas
                    FROM codigos_accesos ca
                    LEFT JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos
                    WHERE ca.fecha = @Fecha
                    GROUP BY ca.idcodigos_accesos, ca.horarios_idhorarios, ca.horario_excepciones_id", conexion))
                {
                    comando.Parameters.AddWithValue("@Fecha", panorama.Fecha);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int? franja = reader.IsDBNull(reader.GetOrdinal("horarios_idhorarios")) ? (int?)null : Convert.ToInt32(reader["horarios_idhorarios"]);
                            int? excepcion = reader.IsDBNull(reader.GetOrdinal("horario_excepciones_id")) ? (int?)null : Convert.ToInt32(reader["horario_excepciones_id"]);
                            SesionAgenda entrada = panorama.Agenda.FirstOrDefault(s => s.EsExtra
                                ? excepcion.HasValue && s.ExcepcionId == excepcion
                                : franja.HasValue && s.FranjaId == franja);
                            if (entrada == null)
                            {
                                continue;
                            }

                            entrada.SesionId = Convert.ToInt32(reader["idcodigos_accesos"]);
                            entrada.Alumnos = Convert.ToInt32(reader["alumnos"]);
                            entrada.ConFallas = Convert.ToInt32(reader["con_fallas"]);
                        }
                    }
                }
            }

            return panorama;
        }

        /// <summary>
        /// Para el panel del profesor (por su numero de empleado): su nombre, su
        /// codigo, sus clases de hoy con los conteos de bitacoras (lo mismo que ve
        /// el administrador) y su horario semanal. Hora del servidor.
        /// </summary>
        public PanoramaProfesor PanoramaDeProfesor(string numeroEmpleado)
        {
            PanoramaProfesor panorama = new PanoramaProfesor { NumeroEmpleado = numeroEmpleado ?? "" };
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT idusuarios, CONCAT_WS(' ', nombre, apellido_paterno, apellido_materno) AS nombre
                    FROM usuarios
                    WHERE numero_identificador = @Numero AND fk_tipo_usuario = 2", conexion))
                {
                    comando.Parameters.AddWithValue("@Numero", panorama.NumeroEmpleado);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            throw new InvalidOperationException("No se encontró al profesor con número de empleado " + numeroEmpleado + ".");
                        }

                        panorama.ProfesorId = Convert.ToInt32(reader["idusuarios"]);
                        panorama.Nombre = reader["nombre"].ToString();
                    }
                }

                panorama.CodigoPorProfesor = Esquema.CodigoPorProfesor(conexion);
                if (panorama.CodigoPorProfesor)
                {
                    panorama.Codigo = CodigosDeProfesor.Leer(conexion, null, panorama.ProfesorId);
                }
            }

            PanoramaLaboratorios dia = PanoramaDelDia();
            panorama.AhoraServidor = dia.AhoraServidor;
            panorama.Hoy = dia.Agenda.Where(s => s.ProfesorId == panorama.ProfesorId).OrderBy(s => s.Inicio).ToList();
            foreach (OpcionCombo laboratorio in dia.Laboratorios)
            {
                panorama.Laboratorios[laboratorio.Id] = laboratorio.Texto;
            }

            panorama.Franjas = ObtenerFranjasDeProfesor(panorama.ProfesorId);
            foreach (FranjaInfo franja in panorama.Franjas)
            {
                // Un laboratorio desactivado no esta en la lista de arriba.
                if (!panorama.Laboratorios.ContainsKey(franja.LaboratorioId))
                {
                    panorama.Laboratorios[franja.LaboratorioId] = franja.Laboratorio;
                }
            }

            if (!panorama.CodigoPorProfesor)
            {
                panorama.Clases = ObtenerClasesDeProfesor(panorama.ProfesorId).Where(c => c.VigenteEn(dia.AhoraServidor)).ToList();
            }

            return panorama;
        }

        /// <summary>
        /// La clase en curso ahora mismo en un laboratorio (reloj del servidor),
        /// o null si no hay. Con el margen de 15 minutos antes del inicio.
        /// </summary>
        public SesionAgenda ClaseEnCurso(int idLaboratorio, out DateTime ahoraServidor)
        {
            ahoraServidor = new Consultas().AhoraServidor();
            TimeSpan ahora = ahoraServidor.TimeOfDay;
            TimeSpan margen = TimeSpan.FromMinutes(MargenAntesDelInicioMinutos);
            return AgendaDelDia(idLaboratorio, ahoraServidor.Date)
                .Where(s => !s.Cancelada && ahora >= s.Inicio - margen && ahora <= s.Fin)
                .OrderBy(s => s.Inicio)
                .FirstOrDefault();
        }

        // ---------------------------------------------------------------------
        // El alumno teclea un codigo
        // ---------------------------------------------------------------------

        /// <summary>
        /// Resuelve el codigo para ESTE laboratorio y este momento (reloj del
        /// servidor). Si entra, devuelve la sesion de hoy (creandola si es la
        /// primera vez que alguien la usa) como VentanaCodigo.
        /// </summary>
        public ResultadoCodigo ResolverCodigo(string codigo, int idLaboratorio, int idAlumno)
        {
            codigo = (codigo ?? string.Empty).Trim();

            // 1) Las clases con ese codigo: una sola antes de la migracion 09;
            // despues, todas las del profesor.
            List<ClaseDelCodigo> clases = new List<ClaseDelCodigo>();
            DateTime ahora = DateTime.MinValue;
            DateTime hoy = DateTime.MinValue;
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                string profesorActivo = Esquema.TieneBajas(conexion) ? "u.activo" : "1";
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT c.idclases, c.usuarios_idusuarios, c.grupos_idgrupos, c.materias_id_materia, c.activa,
                           c.vigente_desde, c.vigente_hasta, " + profesorActivo + @" AS profesor_activo,
                           NOW() AS ahora, CURDATE() AS hoy
                    FROM clases c
                    INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                    WHERE c.codigo = @Codigo", conexion))
                {
                    comando.Parameters.AddWithValue("@Codigo", codigo);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            clases.Add(new ClaseDelCodigo
                            {
                                Id = reader.GetInt32("idclases"),
                                ProfesorId = reader.GetInt32("usuarios_idusuarios"),
                                GrupoId = reader.GetInt32("grupos_idgrupos"),
                                MateriaId = reader.GetInt32("materias_id_materia"),
                                Activa = reader.GetBoolean("activa") && Convert.ToInt32(reader["profesor_activo"]) == 1,
                                Desde = reader.GetDateTime("vigente_desde"),
                                Hasta = reader.GetDateTime("vigente_hasta")
                            });
                            ahora = reader.GetDateTime("ahora");
                            hoy = reader.GetDateTime("hoy");
                        }
                    }
                }
            }

            if (clases.Count == 0)
            {
                return Rechazo(MotivoCodigo.NoExiste, "Código no válido. Revisa que esté bien escrito.");
            }

            List<ClaseDelCodigo> activas = clases.Where(c => c.Activa).ToList();
            if (activas.Count == 0)
            {
                return Rechazo(MotivoCodigo.Inactiva, "Ese código ya no está activo. Pregúntale a tu profesor.");
            }

            if (!activas.Any(c => hoy.Date >= c.Desde.Date && hoy.Date <= c.Hasta.Date))
            {
                DateTime desde = activas.Min(c => c.Desde);
                DateTime hasta = activas.Max(c => c.Hasta);
                return Rechazo(MotivoCodigo.FueraDePeriodo, "Esa clase no está en el periodo actual (vigente del "
                    + desde.ToString("dd/MM/yyyy") + " al " + hasta.ToString("dd/MM/yyyy") + ").");
            }

            // 2) Candidatas de hoy de esas clases, en todos los laboratorios, y la que toca aqui y ahora.
            List<Candidata> candidatas = LeerCandidatas(codigo);
            ResultadoCodigo rechazo;
            Candidata vigente = ReglasCodigo.Elegir(candidatas, idLaboratorio, ahora.TimeOfDay, false, out rechazo);
            if (vigente == null)
            {
                return rechazo;
            }

            // 3) La sesion de hoy (se crea al primer uso), con la materia y el grupo de SU clase.
            ClaseDelCodigo clase = clases.First(c => c.Id == vigente.ClaseId);
            int idSesion = ObtenerOCrearSesion(codigo, vigente, clase.MateriaId, clase.GrupoId, clase.ProfesorId);
            Consultas consultas = new Consultas();
            VentanaCodigo ventana = consultas.ObtenerVentanaSesion(idSesion);
            if (ventana == null)
            {
                throw new InvalidOperationException("La sesion " + idSesion + " no se pudo leer despues de crearla.");
            }

            ResultadoCodigo resultado = new ResultadoCodigo { Motivo = MotivoCodigo.Aceptado, Ventana = ventana, Mensaje = "" };
            if (consultas.AlumnoYaRegistro(idSesion, idAlumno))
            {
                ventana.Estado = EstadoCodigo.YaRegistrado;
                resultado.Motivo = MotivoCodigo.YaRegistrado;
                resultado.Mensaje = "Ya registraste tu bitácora con este código.";
            }

            return resultado;
        }

        /// <summary>Una de las clases que comparten el codigo tecleado.</summary>
        private class ClaseDelCodigo
        {
            public int Id;
            public int ProfesorId;
            public int GrupoId;
            public int MateriaId;
            /// <summary>La clase esta activa y su profesor no esta dado de baja.</summary>
            public bool Activa;
            public DateTime Desde;
            public DateTime Hasta;
        }

        /// <summary>
        /// Franjas y sesiones extra de hoy de las clases activas y vigentes con ese
        /// codigo (las de un profesor), en todos los laboratorios.
        /// </summary>
        private List<Candidata> LeerCandidatas(string codigo)
        {
            List<Candidata> lista = new List<Candidata>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT 0 AS es_extra, h.idhorarios AS id, h.clases_idclases AS clase, h.laboratorios_idlaboratorios AS lab, l.nombre_laboratorio,
                       h.hora_inicio AS inicio, h.hora_fin AS fin, (x.idhorario_excepciones IS NOT NULL) AS cancelada
                FROM horarios h
                INNER JOIN clases c       ON c.idclases = h.clases_idclases
                INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                LEFT JOIN horario_excepciones x ON x.tipo = 'cancelada' AND x.horarios_idhorarios = h.idhorarios AND x.fecha = CURDATE()
                WHERE c.codigo = @Codigo AND c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta
                  AND h.dia_semana = WEEKDAY(CURDATE()) + 1
                UNION ALL
                SELECT 1, x.idhorario_excepciones, x.clases_idclases, x.laboratorios_idlaboratorios, l.nombre_laboratorio, x.hora_inicio, x.hora_fin, 0
                FROM horario_excepciones x
                INNER JOIN clases c       ON c.idclases = x.clases_idclases
                INNER JOIN laboratorios l ON l.idlaboratorios = x.laboratorios_idlaboratorios
                WHERE c.codigo = @Codigo AND c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta
                  AND x.tipo = 'extra' AND x.fecha = CURDATE()
                ORDER BY inicio", conexion))
            {
                comando.Parameters.AddWithValue("@Codigo", codigo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new Candidata
                        {
                            ClaseId = Convert.ToInt32(reader["clase"]),
                            EsExtra = Convert.ToInt32(reader["es_extra"]) == 1,
                            Id = Convert.ToInt32(reader["id"]),
                            LaboratorioId = Convert.ToInt32(reader["lab"]),
                            Laboratorio = reader.GetString("nombre_laboratorio"),
                            Inicio = reader.GetTimeSpan("inicio"),
                            Fin = reader.GetTimeSpan("fin"),
                            Cancelada = Convert.ToInt32(reader["cancelada"]) == 1
                        });
                    }
                }
            }

            return lista;
        }

        private int ObtenerOCrearSesion(string codigo, Candidata candidata, int idMateria, int idGrupo, int idProfesor)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                try
                {
                    using (MySqlCommand insertar = new MySqlCommand(@"
                        INSERT INTO codigos_accesos
                            (codigo, hora_registro, fecha, hora_entrada, hora_salida, materias_id_materia, grupos_idgrupos,
                             laboratorios_idlaboratorios, usuarios_idusuarios, horarios_idhorarios, horario_excepciones_id)
                        VALUES (@Codigo, NOW(), CURDATE(), @Inicio, @Fin, @Materia, @Grupo, @Lab, @Profesor, @Franja, @Excepcion);
                        SELECT LAST_INSERT_ID();", conexion))
                    {
                        insertar.Parameters.AddWithValue("@Codigo", codigo);
                        insertar.Parameters.AddWithValue("@Inicio", candidata.Inicio);
                        insertar.Parameters.AddWithValue("@Fin", candidata.Fin);
                        insertar.Parameters.AddWithValue("@Materia", idMateria);
                        insertar.Parameters.AddWithValue("@Grupo", idGrupo);
                        insertar.Parameters.AddWithValue("@Lab", candidata.LaboratorioId);
                        insertar.Parameters.AddWithValue("@Profesor", idProfesor);
                        insertar.Parameters.AddWithValue("@Franja", candidata.EsExtra ? (object)DBNull.Value : candidata.Id);
                        insertar.Parameters.AddWithValue("@Excepcion", candidata.EsExtra ? candidata.Id : (object)DBNull.Value);
                        int id = Convert.ToInt32(insertar.ExecuteScalar());
                        Log.Info("Sesion " + id + " creada para el codigo " + codigo + " (" + (candidata.EsExtra ? "extra " : "franja ") + candidata.Id + ").");
                        return id;
                    }
                }
                catch (MySqlException ex) when (ex.Number == 1062)
                {
                    // Otro alumno la creo un instante antes: se lee la suya.
                }

                using (MySqlCommand leer = new MySqlCommand(candidata.EsExtra
                    ? "SELECT idcodigos_accesos FROM codigos_accesos WHERE horario_excepciones_id = @Id"
                    : "SELECT idcodigos_accesos FROM codigos_accesos WHERE horarios_idhorarios = @Id AND fecha = CURDATE()", conexion))
                {
                    leer.Parameters.AddWithValue("@Id", candidata.Id);
                    object resultado = leer.ExecuteScalar();
                    if (resultado == null)
                    {
                        throw new InvalidOperationException("No se pudo crear ni encontrar la sesion de hoy.");
                    }

                    return Convert.ToInt32(resultado);
                }
            }
        }

        private static ResultadoCodigo Rechazo(MotivoCodigo motivo, string mensaje)
        {
            return new ResultadoCodigo { Motivo = motivo, Mensaje = mensaje };
        }

        public static string Hora(TimeSpan hora)
        {
            return new DateTime(2000, 1, 1).Add(hora).ToString("HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
