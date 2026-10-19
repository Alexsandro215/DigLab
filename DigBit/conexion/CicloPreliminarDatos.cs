using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Un horario preliminar del proximo ciclo (ciclos_preliminares, db/16).</summary>
    public class CicloPreliminar
    {
        public int Id;
        public string Nombre = "";
        /// <summary>Primer dia de clases: el vigente_desde de sus clases al publicar.</summary>
        public DateTime Desde;
        /// <summary>Ultimo dia de clases: el vigente_hasta.</summary>
        public DateTime Hasta;
        /// <summary>El dia cuyas clases se copiaron; null si se empezo en blanco.</summary>
        public DateTime? CopiadoDel;
        public bool Publicado;
        public DateTime CreadoEn;
        /// <summary>Publicado: el ultimo dia que se dio a las clases de antes.</summary>
        public DateTime? FinAnterior;
        /// <summary>Publicado: el numero de empleado de quien lo publico.</summary>
        public string PublicadoPor = "";
        public DateTime? PublicadoEn;
        /// <summary>Publicado: las clases y los horarios que creo.</summary>
        public int ClasesPublicadas;
        public int HorariosPublicados;
    }

    /// <summary>Las horas de una materia del borrador en un laboratorio (ciclo_franjas).</summary>
    public class FranjaCiclo
    {
        public int Id;
        public int CargaId;
        public int LaboratorioId;
        public string Laboratorio = "";
        public bool LaboratorioActivo = true;
        /// <summary>1 = lunes ... 7 = domingo.</summary>
        public int DiaSemana;
        public TimeSpan Inicio;
        public TimeSpan Fin;

        /// <summary>"Lunes de 07:00 a 09:00, Laboratorio CISCO".</summary>
        public string Descripcion
        {
            get
            {
                return HorariosDatos.NombresDia[DiaSemana] + " de " + HorariosDatos.Hora(Inicio) + " a " + HorariosDatos.Hora(Fin)
                    + ", " + EstiloHorarios.NombreLaboratorio(Laboratorio);
            }
        }
    }

    /// <summary>
    /// Una materia con un grupo en el borrador (ciclo_cargas): una clase al
    /// publicar. Recuerda como era la clase de la que se copio, para decir que
    /// cambio.
    /// </summary>
    public class CargaCiclo
    {
        public int Id;
        /// <summary>Null: "Sin docente".</summary>
        public int? ProfesorId;
        public string Profesor = "";
        /// <summary>"Laura Cardenas": el primer nombre y el apellido paterno.</summary>
        public string ProfesorCorto = "";
        public bool ProfesorActivo = true;
        public int GrupoId;
        public string Grupo = "";
        public int? GrupoCarreraId;
        public int MateriaId;
        public string Materia = "";
        public bool Quitada;
        /// <summary>La clase de la que se copio; null si se agrego a mano.</summary>
        public int? ClaseOrigen;
        public int? ProfesorOrigenId;
        public string ProfesorOrigenCorto = "";
        public int? GrupoOrigenId;
        public string GrupoOrigen = "";
        public int? MateriaOrigenId;
        public string MateriaOrigen = "";
        /// <summary>La firma de su horario al copiarla (RevisionCiclo.Firma); null si no se copio.</summary>
        public string HorarioOrigen;
        public int? ClasePublicada;
        public List<FranjaCiclo> Franjas = new List<FranjaCiclo>();

        /// <summary>"Graficación con 5ISC11".</summary>
        public string Nombre
        {
            get { return Materia + " con " + Grupo; }
        }

        /// <summary>Se copio de una clase (o al menos de un profesor): no es nueva.</summary>
        public bool Copiada
        {
            get { return ClaseOrigen.HasValue || ProfesorOrigenId.HasValue; }
        }
    }

    public enum EstadoProfesorCiclo
    {
        PorRevisar,
        Revisado,
        NoSigue
    }

    /// <summary>Un profesor del ciclo (ciclo_profesores) y como va su revision.</summary>
    public class ProfesorDelCiclo
    {
        public int Id;
        /// <summary>El nombre entero.</summary>
        public string Nombre = "";
        public string Corto = "";
        public bool Activo = true;
        public EstadoProfesorCiclo Estado;
    }

    /// <summary>Una clase de este ciclo que ninguna materia del borrador copio, y por que.</summary>
    public class ClaseNoCopiada
    {
        public ClaseInfo Clase;
        public List<FranjaInfo> Franjas = new List<FranjaInfo>();
        public string PorQue = "";
    }

    /// <summary>Todo lo de un borrador, leido de una vez (CicloPreliminarDatos.Leer).</summary>
    public class CicloLeido
    {
        public CicloPreliminar Ciclo;
        /// <summary>Hoy segun el servidor.</summary>
        public DateTime Hoy;
        public List<CargaCiclo> Cargas = new List<CargaCiclo>();
        /// <summary>Por apellido paterno y nombre.</summary>
        public List<ProfesorDelCiclo> Profesores = new List<ProfesorDelCiclo>();
        public List<ClaseNoCopiada> NoCopiadas = new List<ClaseNoCopiada>();
        /// <summary>Franjas de clases que ya estan en Clases y empiezan dentro del ciclo: ocupan su hora.</summary>
        public List<FranjaInfo> YaProgramadas = new List<FranjaInfo>();
        /// <summary>La base guarda bajas de profesores (db/08): un profesor dado de baja no deja publicar.</summary>
        public bool ConBajas;
        /// <summary>La base guarda laboratorios desactivados (db/08).</summary>
        public bool ConLaboratoriosDesactivables;

        public ProfesorDelCiclo Profesor(int id)
        {
            return Profesores.FirstOrDefault(p => p.Id == id);
        }
    }

    /// <summary>Que hay en la pestana Proximo ciclo al entrar (CicloPreliminarDatos.LeerSituacion).</summary>
    public class SituacionCiclo
    {
        public DateTime Hoy;
        /// <summary>Falta db/16.</summary>
        public bool SinTablas;
        /// <summary>Falta db/09: sin un codigo por profesor no se sabe que codigo dar a las clases nuevas.</summary>
        public bool SinCodigoPorProfesor;
        public CicloPreliminar Borrador;
        /// <summary>Uno publicado que todavia no empieza.</summary>
        public CicloPreliminar PorEmpezar;
        /// <summary>El ultimo publicado que ya empezo.</summary>
        public CicloPreliminar UltimoPublicado;
    }

    /// <summary>Una franja (o una clase sin franjas) de las que se copiarian: para contar antes de empezar.</summary>
    public class OrigenDeClase
    {
        public int ClaseId;
        public int ProfesorId;
        public DateTime VigenteHasta;
        /// <summary>Null: la clase no tiene horario en ningun laboratorio.</summary>
        public int? LaboratorioId;
        public double Horas;
    }

    public class ResultadoEmpezar
    {
        public int CicloId;
        public int Materias;
        public int Profesores;
        public int Horarios;
    }

    /// <summary>
    /// El horario preliminar del proximo ciclo (Horarios > Proximo ciclo, db/16).
    /// Mientras es borrador vive solo en sus tablas: nada de esto toca clases,
    /// horarios, excepciones, sesiones ni bitacoras. Cada escritura va en una
    /// transaccion que empieza bloqueando el ciclo (FOR UPDATE) y se niega si ya
    /// no es borrador. Hoy es CURDATE() del servidor (zona de la conexion).
    /// </summary>
    internal partial class CicloPreliminarDatos
    {
        public const int LargoNombre = 60;
        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

        private readonly Conexion mconexion = new Conexion();

        // ---------------------------------------------------------------------
        // Al entrar a la pestana
        // ---------------------------------------------------------------------

        public SituacionCiclo LeerSituacion()
        {
            SituacionCiclo situacion = new SituacionCiclo();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                situacion.Hoy = Hoy(conexion, null);
                situacion.SinTablas = !Esquema.CicloPreliminar(conexion);
                situacion.SinCodigoPorProfesor = !Esquema.CodigoPorProfesor(conexion);
                if (situacion.SinTablas)
                {
                    return situacion;
                }

                List<CicloPreliminar> ciclos = LeerCiclos(conexion, null,
                    "WHERE cp.estado = 'borrador' OR (cp.estado = 'publicado' AND cp.desde > CURDATE()) "
                    + "OR cp.idciclo = (SELECT MAX(x.idciclo) FROM ciclos_preliminares x WHERE x.estado = 'publicado' AND x.desde <= CURDATE())", null);
                situacion.Borrador = ciclos.FirstOrDefault(c => !c.Publicado);
                situacion.PorEmpezar = ciclos.Where(c => c.Publicado && c.Desde > situacion.Hoy).OrderBy(c => c.Desde).FirstOrDefault();
                situacion.UltimoPublicado = ciclos.Where(c => c.Publicado && c.Desde <= situacion.Hoy).OrderByDescending(c => c.Desde).FirstOrDefault();
            }

            return situacion;
        }

        private static DateTime Hoy(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            using (MySqlCommand comando = new MySqlCommand("SELECT CURDATE()", conexion, transaccion))
            {
                return Convert.ToDateTime(comando.ExecuteScalar()).Date;
            }
        }

        private static List<CicloPreliminar> LeerCiclos(MySqlConnection conexion, MySqlTransaction transaccion, string condicion, Action<MySqlCommand> parametros)
        {
            List<CicloPreliminar> ciclos = new List<CicloPreliminar>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT cp.idciclo, cp.nombre, cp.desde, cp.hasta, cp.copiado_del, cp.estado, cp.creado_en,
                       cp.fin_anterior, cp.publicado_en, u.numero_identificador AS publicado_por,
                       (SELECT COUNT(*) FROM ciclo_cargas cc WHERE cc.idciclo = cp.idciclo AND cc.clase_publicada IS NOT NULL) AS clases,
                       (SELECT COUNT(*) FROM horarios h INNER JOIN ciclo_cargas cc ON cc.clase_publicada = h.clases_idclases
                         WHERE cc.idciclo = cp.idciclo) AS horarios
                FROM ciclos_preliminares cp
                LEFT JOIN usuarios u ON u.idusuarios = cp.publicado_por " + condicion + @"
                ORDER BY cp.idciclo", conexion, transaccion))
            {
                if (parametros != null)
                {
                    parametros(comando);
                }

                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ciclos.Add(new CicloPreliminar
                        {
                            Id = reader.GetInt32("idciclo"),
                            Nombre = reader.GetString("nombre"),
                            Desde = reader.GetDateTime("desde").Date,
                            Hasta = reader.GetDateTime("hasta").Date,
                            CopiadoDel = Fecha(reader, "copiado_del"),
                            Publicado = reader.GetString("estado") == "publicado",
                            CreadoEn = reader.GetDateTime("creado_en"),
                            FinAnterior = Fecha(reader, "fin_anterior"),
                            PublicadoEn = FechaHora(reader, "publicado_en"),
                            PublicadoPor = Texto(reader, "publicado_por"),
                            ClasesPublicadas = Convert.ToInt32(reader["clases"]),
                            HorariosPublicados = Convert.ToInt32(reader["horarios"])
                        });
                    }
                }
            }

            return ciclos;
        }

        // ---------------------------------------------------------------------
        // Antes de empezar: que se copiaria
        // ---------------------------------------------------------------------

        /// <summary>
        /// Las clases que valen ese dia: una fila por cada franja que ese dia se da
        /// (con su vigencia propia, db/20) en un laboratorio activo, o una sola sin
        /// laboratorio si la clase no tiene horario. Las
        /// cuentas (por laboratorio, por casillas) se hacen en la pantalla.
        /// </summary>
        public List<OrigenDeClase> ResumirOrigen(DateTime dia)
        {
            List<OrigenDeClase> filas = new List<OrigenDeClase>();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                string labActivo = Esquema.LaboratoriosDesactivables(conexion) ? " OR l.activo = 1" : " OR l.idlaboratorios IS NOT NULL";
                bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT c.idclases, c.usuarios_idusuarios, c.vigente_hasta, h.laboratorios_idlaboratorios,
                           TIME_TO_SEC(TIMEDIFF(h.hora_fin, h.hora_inicio)) / 3600 AS horas
                    FROM clases c
                    LEFT JOIN horarios h ON h.clases_idclases = c.idclases AND " + FranjaDeEseDia(conVigencia, "@Dia") + @"
                    LEFT JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                    WHERE c.activa = 1 AND @Dia BETWEEN c.vigente_desde AND c.vigente_hasta
                      AND (h.idhorarios IS NULL" + labActivo + ")", conexion))
                {
                    comando.Parameters.AddWithValue("@Dia", dia.Date);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            filas.Add(new OrigenDeClase
                            {
                                ClaseId = reader.GetInt32("idclases"),
                                ProfesorId = reader.GetInt32("usuarios_idusuarios"),
                                VigenteHasta = reader.GetDateTime("vigente_hasta").Date,
                                LaboratorioId = Entero(reader, "laboratorios_idlaboratorios"),
                                Horas = reader.IsDBNull(reader.GetOrdinal("horas")) ? 0 : Convert.ToDouble(reader["horas"])
                            });
                        }
                    }
                }
            }

            return filas;
        }

        /// <summary>"Febrero-julio 2027" o "Agosto 2026-enero 2027": el nombre que se propone para unas fechas.</summary>
        public static string NombrePropuesto(DateTime desde, DateTime hasta)
        {
            string primero = Espanol.DateTimeFormat.GetMonthName(desde.Month);
            primero = Espanol.TextInfo.ToUpper(primero[0]) + primero.Substring(1);
            string ultimo = Espanol.DateTimeFormat.GetMonthName(hasta.Month);
            return desde.Year == hasta.Year
                ? (desde.Month == hasta.Month ? primero + " " + desde.Year : primero + "-" + ultimo + " " + desde.Year)
                : primero + " " + desde.Year + "-" + ultimo + " " + hasta.Year;
        }

        // ---------------------------------------------------------------------
        // Empezar, fechas y descartar
        // ---------------------------------------------------------------------

        /// <summary>
        /// Abre el borrador. Con copiarDel copia las clases que valen ese dia con
        /// sus franjas en esos laboratorios (y, con sinHorario, las que no tienen
        /// ninguna): cada una con su profesor, salvo los dados de baja, cuyas
        /// materias quedan "Sin docente" y ellos "No sigue". Sin copiarDel, en blanco.
        /// </summary>
        public ResultadoEmpezar Empezar(string nombre, DateTime desde, DateTime hasta, DateTime? copiarDel,
            ICollection<int> laboratorios, bool sinHorario, int idAdministrador)
        {
            Permisos.Exigir("Empezar el horario del proximo ciclo");
            nombre = (nombre ?? "").Trim();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                DateTime hoy = Hoy(conexion, transaccion);
                ValidarFechas(nombre, desde, hasta, hoy);
                if (copiarDel.HasValue && laboratorios.Count == 0 && !sinHorario)
                {
                    throw new ErrorDeDatos("Elige al menos un laboratorio.");
                }

                // Nadie mas empieza otro a la vez: el segundo espera aqui y luego ve el primero.
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT nombre, estado FROM ciclos_preliminares
                     WHERE estado = 'borrador' OR (estado = 'publicado' AND desde > CURDATE())
                     ORDER BY idciclo LIMIT 1 FOR UPDATE", conexion, transaccion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        throw new ErrorDeDatos(reader.GetString("estado") == "publicado"
                            ? reader.GetString("nombre") + " ya está publicado y todavía no empieza: deshaz su publicación para cambiarlo."
                            : "Ya hay un horario preliminar abierto: vuelve a entrar a la pestaña.");
                    }
                }

                int idCiclo;
                using (MySqlCommand comando = new MySqlCommand(@"
                    INSERT INTO ciclos_preliminares (nombre, desde, hasta, copiado_del, estado, creado_por, creado_en)
                    VALUES (@Nombre, @Desde, @Hasta, @Copiado, 'borrador', @Admin, NOW());
                    SELECT LAST_INSERT_ID();", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@Nombre", nombre);
                    comando.Parameters.AddWithValue("@Desde", desde.Date);
                    comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                    comando.Parameters.AddWithValue("@Copiado", copiarDel.HasValue ? (object)copiarDel.Value.Date : DBNull.Value);
                    comando.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
                    idCiclo = Convert.ToInt32(comando.ExecuteScalar());
                }

                ResultadoEmpezar resultado = new ResultadoEmpezar { CicloId = idCiclo };
                if (copiarDel.HasValue)
                {
                    resultado.Materias = CopiarClases(conexion, transaccion, idCiclo, copiarDel.Value, null, laboratorios, sinHorario);
                    if (resultado.Materias == 0)
                    {
                        throw new ErrorDeDatos("Con esos laboratorios no hay clases que copiar ese día.");
                    }

                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO ciclo_profesores (idciclo, profesor, estado)
                        SELECT DISTINCT @C, profesor, 'por_revisar' FROM ciclo_cargas WHERE idciclo = @C AND profesor IS NOT NULL;
                        INSERT IGNORE INTO ciclo_profesores (idciclo, profesor, estado)
                        SELECT DISTINCT @C, profesor_origen, 'no_sigue' FROM ciclo_cargas
                         WHERE idciclo = @C AND profesor IS NULL AND profesor_origen IS NOT NULL;
                        SELECT COUNT(DISTINCT profesor_origen) FROM ciclo_cargas WHERE idciclo = @C;", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@C", idCiclo);
                        resultado.Profesores = Convert.ToInt32(comando.ExecuteScalar());
                    }

                    resultado.Horarios = ContarFranjas(conexion, transaccion, idCiclo);
                }

                transaccion.Commit();
                Log.Info("Horario preliminar " + idCiclo + " (" + nombre + ") empezado: del " + desde.ToString("yyyy-MM-dd") + " al " + hasta.ToString("yyyy-MM-dd")
                    + (copiarDel.HasValue
                        ? ", copia de las clases del " + copiarDel.Value.ToString("yyyy-MM-dd") + " en los laboratorios " + string.Join(",", laboratorios)
                          + (sinHorario ? " y las sin horario" : "") + ": " + resultado.Materias + " materias de " + resultado.Profesores
                          + " profesores con " + resultado.Horarios + " horarios."
                        : ", en blanco."));
                return resultado;
            }
        }

        /// <summary>
        /// Copia al borrador las clases que valen ese dia (o solo esas, con
        /// clases) con sus franjas en esos laboratorios. Devuelve cuantas materias entraron.
        /// </summary>
        private static int CopiarClases(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, DateTime dia,
            ICollection<int> clases, ICollection<int> laboratorios, bool sinHorario)
        {
            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                comando.Parameters.AddWithValue("@Dia", dia.Date);
                comando.Parameters.AddWithValue("@SinHorario", sinHorario ? 1 : 0);
                string labs = Lista(comando, "l", laboratorios);
                string soloEstas = clases == null ? "" : " AND c.idclases IN (" + Lista(comando, "k", clases) + ")";
                string profesor = Esquema.TieneBajas(conexion, transaccion) ? "IF(u.activo = 1, c.usuarios_idusuarios, NULL)" : "c.usuarios_idusuarios";
                // Solo las franjas de la clase como estaba ese dia (db/20): ni las ya
                // terminadas ni las que la sustituyen despues.
                string vigente = FranjaDeEseDia(Esquema.VigenciaDeFranjas(conexion, transaccion), "@Dia");
                comando.CommandText = @"
                    INSERT INTO ciclo_cargas (idciclo, profesor, grupo, materia, quitada, clase_origen,
                                              profesor_origen, grupo_origen, materia_origen, horario_origen)
                    SELECT @C, " + profesor + @", c.grupos_idgrupos, c.materias_id_materia, 0, c.idclases,
                           c.usuarios_idusuarios, c.grupos_idgrupos, c.materias_id_materia,
                           (SELECT GROUP_CONCAT(CONCAT(h.laboratorios_idlaboratorios, ':', h.dia_semana, ':',
                                   TIME_FORMAT(h.hora_inicio, '%H:%i'), '-', TIME_FORMAT(h.hora_fin, '%H:%i'))
                                   ORDER BY h.dia_semana, h.hora_inicio, h.laboratorios_idlaboratorios SEPARATOR ';')
                              FROM horarios h WHERE h.clases_idclases = c.idclases AND h.laboratorios_idlaboratorios IN (" + labs + @")
                               AND " + vigente + @")
                    FROM clases c
                    INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                    WHERE c.activa = 1" + (clases == null ? " AND @Dia BETWEEN c.vigente_desde AND c.vigente_hasta" : "") + soloEstas + @"
                      AND (EXISTS (SELECT 1 FROM horarios h WHERE h.clases_idclases = c.idclases AND h.laboratorios_idlaboratorios IN (" + labs + @")
                                   AND " + vigente + @")
                           OR (@SinHorario = 1 AND NOT EXISTS (SELECT 1 FROM horarios h WHERE h.clases_idclases = c.idclases AND " + vigente + @")))
                      AND NOT EXISTS (SELECT 1 FROM ciclo_cargas x WHERE x.idciclo = @C AND x.clase_origen = c.idclases);
                    SELECT ROW_COUNT();";
                int materias = Convert.ToInt32(comando.ExecuteScalar());

                comando.CommandText = @"
                    INSERT INTO ciclo_franjas (idcarga, laboratorio, dia_semana, hora_inicio, hora_fin)
                    SELECT cc.idcarga, h.laboratorios_idlaboratorios, h.dia_semana, h.hora_inicio, h.hora_fin
                    FROM ciclo_cargas cc
                    INNER JOIN horarios h ON h.clases_idclases = cc.clase_origen
                    INNER JOIN clases c   ON c.idclases = h.clases_idclases
                    WHERE cc.idciclo = @C AND h.laboratorios_idlaboratorios IN (" + labs + @") AND " + vigente + @"
                      AND NOT EXISTS (SELECT 1 FROM ciclo_franjas f WHERE f.idcarga = cc.idcarga)" + (clases == null ? "" : " AND cc.clase_origen IN (" + Lista(comando, "m", clases) + ")");
                comando.ExecuteNonQuery();
                return materias;
            }
        }

        private static int ContarFranjas(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT COUNT(*) FROM ciclo_franjas f INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                 WHERE cc.idciclo = @C AND cc.quitada = 0", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                return Convert.ToInt32(comando.ExecuteScalar());
            }
        }

        /// <summary>Las fechas y el nombre de un ciclo, con las reglas de siempre (hoy es el del servidor).</summary>
        private static void ValidarFechas(string nombre, DateTime desde, DateTime hasta, DateTime hoy)
        {
            if (desde.Date <= hoy)
            {
                throw new ErrorDeDatos("El primer día de clases tiene que ser después de hoy (" + hoy.ToString("dd/MM/yyyy") + ").");
            }

            if (hasta.Date < desde.Date)
            {
                throw new ErrorDeDatos("El último día de clases no puede ser antes del primero.");
            }

            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new ErrorDeDatos("Escribe un nombre para el ciclo.");
            }

            if (nombre.Trim().Length > LargoNombre)
            {
                throw new ErrorDeDatos("El nombre del ciclo puede tener hasta " + LargoNombre + " letras.");
            }
        }

        /// <summary>Cambia el nombre y las fechas del borrador.</summary>
        public void CambiarFechas(int idCiclo, string nombre, DateTime desde, DateTime hasta)
        {
            Permisos.Exigir("Cambiar las fechas del proximo ciclo");
            nombre = (nombre ?? "").Trim();
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                Bloquear(conexion, transaccion, idCiclo);
                ValidarFechas(nombre, desde, hasta, Hoy(conexion, transaccion));
                using (MySqlCommand comando = new MySqlCommand(
                    "UPDATE ciclos_preliminares SET nombre = @Nombre, desde = @Desde, hasta = @Hasta WHERE idciclo = @C", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idCiclo);
                    comando.Parameters.AddWithValue("@Nombre", nombre);
                    comando.Parameters.AddWithValue("@Desde", desde.Date);
                    comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                    comando.ExecuteNonQuery();
                }

                transaccion.Commit();
                Log.Info("Horario preliminar " + idCiclo + ": ahora " + nombre + ", del " + desde.ToString("yyyy-MM-dd") + " al " + hasta.ToString("yyyy-MM-dd") + ".");
            }
        }

        /// <summary>Borra el borrador entero (sus materias, franjas y profesores se van con el). Las clases de ahora no cambian.</summary>
        public void Descartar(int idCiclo)
        {
            Permisos.Exigir("Descartar el horario del proximo ciclo");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                Bloquear(conexion, transaccion, idCiclo);
                using (MySqlCommand comando = new MySqlCommand(
                    "DELETE FROM ciclos_preliminares WHERE idciclo = @C AND estado = 'borrador'", conexion, transaccion))
                {
                    comando.Parameters.AddWithValue("@C", idCiclo);
                    comando.ExecuteNonQuery();
                }

                transaccion.Commit();
                Log.Info("Horario preliminar " + idCiclo + " descartado.");
            }
        }

        /// <summary>
        /// Al empezar cada escritura: bloquea el ciclo hasta el fin de la
        /// transaccion y comprueba que siga en borrador (otra ventana pudo
        /// publicarlo o descartarlo).
        /// </summary>
        private static void Bloquear(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo)
        {
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT estado FROM ciclos_preliminares WHERE idciclo = @C FOR UPDATE", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                object estado = comando.ExecuteScalar();
                if (estado == null || estado == DBNull.Value || estado.ToString() != "borrador")
                {
                    throw new ErrorDeDatos("Este horario ya se publicó o se descartó: vuelve a entrar a la pestaña.");
                }
            }
        }

        // ---------------------------------------------------------------------
        // Leer el borrador
        // ---------------------------------------------------------------------

        public CicloLeido Leer(int idCiclo)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return Leer(conexion, null, idCiclo);
            }
        }

        /// <summary>El ciclo, sus materias con sus franjas, sus profesores, las clases que no se copiaron y las ya programadas.</summary>
        internal static CicloLeido Leer(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo)
        {
            CicloLeido leido = new CicloLeido
            {
                Hoy = Hoy(conexion, transaccion),
                ConBajas = Esquema.TieneBajas(conexion, transaccion),
                ConLaboratoriosDesactivables = Esquema.LaboratoriosDesactivables(conexion, transaccion)
            };
            leido.Ciclo = LeerCiclos(conexion, transaccion, "WHERE cp.idciclo = @C", c => c.Parameters.AddWithValue("@C", idCiclo)).FirstOrDefault();
            if (leido.Ciclo == null)
            {
                throw new ErrorDeDatos("Este horario ya se publicó o se descartó: vuelve a entrar a la pestaña.");
            }

            string activoProfesor = leido.ConBajas ? "u.activo" : "1";
            string activoLaboratorio = leido.ConLaboratoriosDesactivables ? "l.activo" : "1";

            // Las materias, con los nombres de como eran al copiarlas.
            Dictionary<int, CargaCiclo> porId = new Dictionary<int, CargaCiclo>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT cc.idcarga, cc.profesor, cc.grupo, cc.materia, cc.quitada, cc.clase_origen, cc.profesor_origen,
                       cc.grupo_origen, cc.materia_origen, cc.horario_origen, cc.clase_publicada,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor_nombre,
                       u.nombre AS p_nombre, u.apellido_paterno AS p_paterno, " + activoProfesor + @" AS p_activo,
                       g.nombre_grupo, g.Carrera_id, m.nombre_materia,
                       uor.nombre AS o_nombre, uor.apellido_paterno AS o_paterno,
                       gor.nombre_grupo AS o_grupo, mor.nombre_materia AS o_materia
                FROM ciclo_cargas cc
                LEFT JOIN usuarios u    ON u.idusuarios = cc.profesor
                INNER JOIN grupos g     ON g.idgrupos = cc.grupo
                INNER JOIN materias m   ON m.id_materia = cc.materia
                LEFT JOIN usuarios uor  ON uor.idusuarios = cc.profesor_origen
                LEFT JOIN grupos gor    ON gor.idgrupos = cc.grupo_origen
                LEFT JOIN materias mor  ON mor.id_materia = cc.materia_origen
                WHERE cc.idciclo = @C
                ORDER BY m.nombre_materia, g.nombre_grupo, cc.idcarga", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        CargaCiclo carga = new CargaCiclo
                        {
                            Id = reader.GetInt32("idcarga"),
                            ProfesorId = Entero(reader, "profesor"),
                            GrupoId = reader.GetInt32("grupo"),
                            Grupo = reader.GetString("nombre_grupo"),
                            GrupoCarreraId = Entero(reader, "Carrera_id"),
                            MateriaId = reader.GetInt32("materia"),
                            Materia = reader.GetString("nombre_materia"),
                            Quitada = Convert.ToBoolean(reader["quitada"]),
                            ClaseOrigen = Entero(reader, "clase_origen"),
                            ProfesorOrigenId = Entero(reader, "profesor_origen"),
                            GrupoOrigenId = Entero(reader, "grupo_origen"),
                            GrupoOrigen = Texto(reader, "o_grupo"),
                            MateriaOrigenId = Entero(reader, "materia_origen"),
                            MateriaOrigen = Texto(reader, "o_materia"),
                            HorarioOrigen = reader.IsDBNull(reader.GetOrdinal("horario_origen")) ? null : reader.GetString("horario_origen"),
                            ClasePublicada = Entero(reader, "clase_publicada")
                        };
                        if (carga.ProfesorId.HasValue)
                        {
                            carga.Profesor = Texto(reader, "profesor_nombre");
                            carga.ProfesorCorto = HorariosDatos.NombreCorto(Texto(reader, "p_nombre"), Texto(reader, "p_paterno"));
                            carga.ProfesorActivo = Convert.ToInt32(reader["p_activo"]) == 1;
                        }

                        if (carga.ProfesorOrigenId.HasValue)
                        {
                            carga.ProfesorOrigenCorto = HorariosDatos.NombreCorto(Texto(reader, "o_nombre"), Texto(reader, "o_paterno"));
                        }

                        porId[carga.Id] = carga;
                        leido.Cargas.Add(carga);
                    }
                }
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT f.idfranja, f.idcarga, f.laboratorio, l.nombre_laboratorio, " + activoLaboratorio + @" AS lab_activo,
                       f.dia_semana, f.hora_inicio, f.hora_fin
                FROM ciclo_franjas f
                INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                INNER JOIN laboratorios l  ON l.idlaboratorios = f.laboratorio
                WHERE cc.idciclo = @C
                ORDER BY f.dia_semana, f.hora_inicio, f.laboratorio", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        CargaCiclo carga;
                        if (porId.TryGetValue(reader.GetInt32("idcarga"), out carga))
                        {
                            carga.Franjas.Add(new FranjaCiclo
                            {
                                Id = reader.GetInt32("idfranja"),
                                CargaId = carga.Id,
                                LaboratorioId = reader.GetInt32("laboratorio"),
                                Laboratorio = reader.GetString("nombre_laboratorio"),
                                LaboratorioActivo = Convert.ToInt32(reader["lab_activo"]) == 1,
                                DiaSemana = Convert.ToInt32(reader["dia_semana"]),
                                Inicio = reader.GetTimeSpan("hora_inicio"),
                                Fin = reader.GetTimeSpan("hora_fin")
                            });
                        }
                    }
                }
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT cp.profesor, cp.estado, CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS nombre,
                       u.nombre AS p_nombre, u.apellido_paterno AS p_paterno, " + activoProfesor + @" AS activo
                FROM ciclo_profesores cp
                INNER JOIN usuarios u ON u.idusuarios = cp.profesor
                WHERE cp.idciclo = @C
                ORDER BY u.apellido_paterno, u.nombre", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string estado = reader.GetString("estado");
                        leido.Profesores.Add(new ProfesorDelCiclo
                        {
                            Id = reader.GetInt32("profesor"),
                            Nombre = reader.GetString("nombre"),
                            Corto = HorariosDatos.NombreCorto(Texto(reader, "p_nombre"), Texto(reader, "p_paterno")),
                            Activo = Convert.ToInt32(reader["activo"]) == 1,
                            Estado = estado == "revisado" ? EstadoProfesorCiclo.Revisado
                                : estado == "no_sigue" ? EstadoProfesorCiclo.NoSigue : EstadoProfesorCiclo.PorRevisar
                        });
                    }
                }
            }

            LeerNoCopiadas(conexion, transaccion, leido);
            // Las franjas que se dan en algun dia del ciclo (db/20: no las terminadas antes).
            leido.YaProgramadas = LeerFranjasReales(conexion, transaccion,
                "c.activa = 1 AND c.vigente_desde >= @Desde AND c.vigente_desde <= @Hasta AND "
                + VigenciaFranjas.SeCruzaCon(Esquema.VigenciaDeFranjas(conexion, transaccion), "@Desde", "@Hasta"), c =>
                {
                    c.Parameters.AddWithValue("@Desde", leido.Ciclo.Desde);
                    c.Parameters.AddWithValue("@Hasta", leido.Ciclo.Hasta);
                });
            return leido;
        }

        /// <summary>
        /// Las clases activas que valen algun dia entre el que se copio y la
        /// vispera del inicio y que ninguna materia del borrador tiene como origen.
        /// </summary>
        private static void LeerNoCopiadas(MySqlConnection conexion, MySqlTransaction transaccion, CicloLeido leido)
        {
            CicloPreliminar ciclo = leido.Ciclo;
            DateTime referencia = ciclo.CopiadoDel ?? ciclo.CreadoEn.Date;
            Dictionary<int, ClaseNoCopiada> porClase = new Dictionary<int, ClaseNoCopiada>();
            Dictionary<int, DateTime?> creadas = new Dictionary<int, DateTime?>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT c.idclases, c.usuarios_idusuarios, CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                       u.nombre AS p_nombre, u.apellido_paterno AS p_paterno, u.numero_identificador,
                       c.grupos_idgrupos, g.nombre_grupo, c.materias_id_materia, m.nombre_materia,
                       c.codigo, c.vigente_desde, c.vigente_hasta, c.activa, c.creada_en
                FROM clases c
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                WHERE c.activa = 1 AND c.vigente_hasta >= @Referencia AND c.vigente_desde < @Desde
                  AND NOT EXISTS (SELECT 1 FROM ciclo_cargas cc WHERE cc.idciclo = @C AND cc.clase_origen = c.idclases)
                ORDER BY m.nombre_materia, g.nombre_grupo", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", ciclo.Id);
                comando.Parameters.AddWithValue("@Referencia", referencia);
                comando.Parameters.AddWithValue("@Desde", ciclo.Desde);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ClaseInfo clase = new ClaseInfo
                        {
                            Id = reader.GetInt32("idclases"),
                            ProfesorId = reader.GetInt32("usuarios_idusuarios"),
                            Profesor = reader.GetString("profesor"),
                            ProfesorCorto = HorariosDatos.NombreCorto(Texto(reader, "p_nombre"), Texto(reader, "p_paterno")),
                            NumeroEmpleado = Texto(reader, "numero_identificador"),
                            GrupoId = reader.GetInt32("grupos_idgrupos"),
                            Grupo = reader.GetString("nombre_grupo"),
                            MateriaId = reader.GetInt32("materias_id_materia"),
                            Materia = reader.GetString("nombre_materia"),
                            Codigo = Texto(reader, "codigo"),
                            VigenteDesde = reader.GetDateTime("vigente_desde").Date,
                            VigenteHasta = reader.GetDateTime("vigente_hasta").Date,
                            Activa = true
                        };
                        porClase[clase.Id] = new ClaseNoCopiada { Clase = clase };
                        creadas[clase.Id] = FechaHora(reader, "creada_en");
                        leido.NoCopiadas.Add(porClase[clase.Id]);
                    }
                }
            }

            if (porClase.Count == 0)
            {
                return;
            }

            // Su horario como estaba el dia de referencia (db/20: sin la franja ya
            // terminada ni la que la sustituye despues).
            foreach (FranjaInfo franja in LeerFranjasReales(conexion, transaccion, "h.clases_idclases IN (" + string.Join(",", porClase.Keys) + ") AND "
                + FranjaDeEseDia(Esquema.VigenciaDeFranjas(conexion, transaccion), "@Referencia"), c => c.Parameters.AddWithValue("@Referencia", referencia)))
            {
                porClase[franja.ClaseId].Franjas.Add(franja);
            }

            foreach (ClaseNoCopiada nc in leido.NoCopiadas)
            {
                DateTime? creada = creadas[nc.Clase.Id];
                nc.PorQue = !ciclo.CopiadoDel.HasValue ? "Empezaste el ciclo en blanco"
                    : creada.HasValue && creada.Value > ciclo.CreadoEn ? "Se creó después de copiar"
                    : nc.Clase.VigenteDesde > ciclo.CopiadoDel.Value ? "Empieza después del día que copiaste"
                    : nc.Franjas.Count == 0 ? "No tiene horario en ningún laboratorio"
                    : "Sus horas eran en un laboratorio que no copiaste";
            }
        }

        /// <summary>
        /// SQL: la franja (h) de la clase (c) como estaba ese dia (expresion SQL):
        /// se da ese dia o, si la clase empezo despues o ya habia terminado, su
        /// primer o su ultimo dia. Con db/20 deja fuera la franja terminada antes
        /// y la que la sustituye despues (no se copian las dos); sin ella, todas
        /// las de la clase, como antes. Exige la clase activa.
        /// </summary>
        private static string FranjaDeEseDia(bool conVigencia, string dia)
        {
            return VigenciaFranjas.SeDa(conVigencia, "LEAST(GREATEST(DATE(" + dia + "), c.vigente_desde), c.vigente_hasta)");
        }

        /// <summary>
        /// Franjas de clases reales (horarios), con su clase, como las lee Horarios:
        /// VigenteDesde/VigenteHasta son las de la franja (la de su clase recortada
        /// a la suya propia, db/20).
        /// </summary>
        private static List<FranjaInfo> LeerFranjasReales(MySqlConnection conexion, MySqlTransaction transaccion, string condicion, Action<MySqlCommand> parametros)
        {
            List<FranjaInfo> franjas = new List<FranjaInfo>();
            bool conVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT h.idhorarios, h.clases_idclases, h.laboratorios_idlaboratorios, l.nombre_laboratorio,
                       h.dia_semana, h.hora_inicio, h.hora_fin, c.codigo, c.usuarios_idusuarios,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                       u.nombre AS profesor_nombre, u.apellido_paterno AS profesor_paterno,
                       c.grupos_idgrupos, g.nombre_grupo, m.nombre_materia, c.vigente_desde, c.vigente_hasta, c.activa,
                       " + VigenciaFranjas.Desde(conVigencia) + " AS desde_efectivo, " + VigenciaFranjas.Hasta(conVigencia) + @" AS hasta_efectivo,
                       " + VigenciaFranjas.Columnas(conVigencia) + @"
                FROM horarios h
                INNER JOIN clases c       ON c.idclases = h.clases_idclases
                INNER JOIN usuarios u     ON u.idusuarios = c.usuarios_idusuarios
                INNER JOIN grupos g       ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN materias m     ON m.id_materia = c.materias_id_materia
                INNER JOIN laboratorios l ON l.idlaboratorios = h.laboratorios_idlaboratorios
                WHERE " + condicion + @"
                ORDER BY h.dia_semana, h.hora_inicio", conexion, transaccion))
            {
                if (parametros != null)
                {
                    parametros(comando);
                }

                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        franjas.Add(new FranjaInfo
                        {
                            Id = reader.GetInt32("idhorarios"),
                            ClaseId = reader.GetInt32("clases_idclases"),
                            LaboratorioId = reader.GetInt32("laboratorios_idlaboratorios"),
                            Laboratorio = reader.GetString("nombre_laboratorio"),
                            DiaSemana = Convert.ToInt32(reader["dia_semana"]),
                            Inicio = reader.GetTimeSpan("hora_inicio"),
                            Fin = reader.GetTimeSpan("hora_fin"),
                            Codigo = Texto(reader, "codigo"),
                            ProfesorId = reader.GetInt32("usuarios_idusuarios"),
                            Profesor = reader.GetString("profesor"),
                            ProfesorCorto = HorariosDatos.NombreCorto(Texto(reader, "profesor_nombre"), Texto(reader, "profesor_paterno")),
                            GrupoId = reader.GetInt32("grupos_idgrupos"),
                            Grupo = reader.GetString("nombre_grupo"),
                            Materia = reader.GetString("nombre_materia"),
                            VigenteDesde = reader.GetDateTime("desde_efectivo").Date,
                            VigenteHasta = reader.GetDateTime("hasta_efectivo").Date,
                            FranjaDesde = Fecha(reader, "franja_desde"),
                            FranjaHasta = Fecha(reader, "franja_hasta"),
                            ClaseDesde = reader.GetDateTime("vigente_desde").Date,
                            ClaseHasta = reader.GetDateTime("vigente_hasta").Date,
                            Activa = Convert.ToBoolean(reader["activa"])
                        });
                    }
                }
            }

            return franjas;
        }

        // ---------------------------------------------------------------------
        // Utilidades
        // ---------------------------------------------------------------------

        /// <summary>"@l0, @l1, ...": los valores como parametros del comando. Vacia: un id que no existe.</summary>
        private static string Lista(MySqlCommand comando, string prefijo, IEnumerable<int> valores)
        {
            List<string> nombres = new List<string>();
            foreach (int valor in valores)
            {
                string nombre = "@" + prefijo + nombres.Count;
                comando.Parameters.AddWithValue(nombre, valor);
                nombres.Add(nombre);
            }

            return nombres.Count == 0 ? "-1" : string.Join(", ", nombres);
        }

        private static int? Entero(MySqlDataReader reader, string columna)
        {
            int i = reader.GetOrdinal(columna);
            return reader.IsDBNull(i) ? (int?)null : Convert.ToInt32(reader.GetValue(i));
        }

        private static DateTime? Fecha(MySqlDataReader reader, string columna)
        {
            int i = reader.GetOrdinal(columna);
            return reader.IsDBNull(i) ? (DateTime?)null : reader.GetDateTime(i).Date;
        }

        private static DateTime? FechaHora(MySqlDataReader reader, string columna)
        {
            int i = reader.GetOrdinal(columna);
            return reader.IsDBNull(i) ? (DateTime?)null : reader.GetDateTime(i);
        }

        private static string Texto(MySqlDataReader reader, string columna)
        {
            object valor = reader[columna];
            return valor == DBNull.Value ? "" : valor.ToString();
        }
    }
}
