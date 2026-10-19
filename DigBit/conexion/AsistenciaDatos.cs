using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    /// <summary>Un dia de clase que cuenta para la asistencia (una columna de la lista).</summary>
    public class DiaDeAsistencia
    {
        public DateTime Fecha;
        /// <summary>A que hora empezo su primer bloque ese dia (para el encabezado).</summary>
        public TimeSpan Inicio;
        /// <summary>Hoy y la clase aun no termina: quien no ha registrado va con falta "hasta ahora".</summary>
        public bool EnCurso;
    }

    /// <summary>Un alumno de la lista de la clase y los dias en que registro.</summary>
    public class AlumnoDeAsistencia
    {
        public int Id;
        public string Matricula = "";
        public string Nombre = "";
        /// <summary>Los dias (DiaDeAsistencia.Fecha) en que registro su bitacora en esta clase y le cuenta como asistencia.</summary>
        public HashSet<DateTime> Presente = new HashSet<DateTime>();
        /// <summary>Los dias en que registro su bitacora pero el profesor le quito la asistencia: no estaba en el salon (db/17).</summary>
        public HashSet<DateTime> Retiradas = new HashSet<DateTime>();
        /// <summary>Sus bitacoras de cada dia que cuentan para esta clase: las que se quitan o se devuelven juntas.</summary>
        public Dictionary<DateTime, List<int>> BitacorasDelDia = new Dictionary<DateTime, List<int>>();
    }

    /// <summary>La asistencia de una clase (profesor + materia + grupo) en su periodo.</summary>
    public class AsistenciaDeClase
    {
        public DateTime AhoraServidor;
        /// <summary>El reloj de este equipo en el instante en que se leyo AhoraServidor (para estimar el desfase).</summary>
        public DateTime LeidoEnRelojLocal;
        public int ClaseId;
        public string Materia = "";
        public string Grupo = "";
        public DateTime VigenteDesde;
        /// <summary>La base guarda las asistencias que quita el profesor (db/17): mientras dura la clase, se pueden quitar.</summary>
        public bool ConRetiro;
        /// <summary>Los dias que cuentan, del primero al ultimo.</summary>
        public List<DiaDeAsistencia> Dias = new List<DiaDeAsistencia>();
        /// <summary>La lista: todos los alumnos que han registrado en alguno, por apellido.</summary>
        public List<AlumnoDeAsistencia> Alumnos = new List<AlumnoDeAsistencia>();
    }

    /// <summary>
    /// La asistencia de una clase sale de sus bitacoras (regla del usuario,
    /// 2026-10-12): cuando se registra la primera bitacora de la clase, quienes
    /// registran forman la lista; quien registra por primera vez despues entra a
    /// la lista y queda con falta en los dias anteriores, porque no registro en
    /// ellos. Hoy, quien aun no registra va con falta "hasta ahora".
    ///
    /// Una columna por DIA: dos bloques de la misma clase el mismo dia, o una
    /// franja recortada y lo que le quedo, son una sola asistencia. Cuentan los
    /// dias con al menos una bitacora de la clase (los demas no se dio o nadie
    /// fue: no cuentan contra nadie; las canceladas no tienen sesion) y hoy
    /// mientras la clase dura. Las sesiones de la clase son las del mismo
    /// profesor, materia y grupo en su periodo: las de sus franjas, sus sesiones
    /// extra y las que se quedaron sin enlace al cambiar el horario.
    ///
    /// Con el codigo fijo, quien llega en los 15 minutos antes de una clase
    /// seguida del mismo profesor en el mismo laboratorio entra en la sesion de
    /// la que va terminando (ReglasCodigo). Como en Faltas (ContarLlegadasAnticipadas),
    /// lo registrado de 15 minutos antes a 10 despues del inicio de la siguiente
    /// es de la siguiente si el alumno es de su grupo (carrera_grupo_semestre) y
    /// no del de la anterior; sin grupo conocido, si ha registrado fuera de esas
    /// ventanas en la siguiente y nunca en la anterior. Solo en laboratorios con
    /// codigo fijo (el modo de ahora, como Faltas). Compatible con MariaDB 5.5:
    /// en la escuela hora_salida es texto, por eso las horas se leen con TIME().
    /// </summary>
    public class AsistenciaDatos
    {
        private static readonly TimeSpan Margen = TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
        private static readonly TimeSpan Llenado = TimeSpan.FromMinutes(10);

        private readonly Conexion mconexion = new Conexion();

        private sealed class Sesion
        {
            public int Id;
            public DateTime Fecha;
            public TimeSpan Inicio;
            public TimeSpan Fin;
            public int LaboratorioId;
            public int GrupoId;
            public bool DeLaClase;
        }

        private sealed class Bitacora
        {
            public int Id;
            public int SesionId;
            public int AlumnoId;
            public DateTime? RegistradoEn;
            /// <summary>El profesor le quito la asistencia (db/17): sigue siendo bitacora, no asistencia.</summary>
            public bool Retirada;
        }

        /// <param name="idClase">clases.idclases.</param>
        /// <param name="hoyInicio">Si la clase es hoy (la de la tarjeta): su inicio y su fin. Su columna cuenta mientras dura, aunque aun nadie registre.</param>
        public AsistenciaDeClase DeLaClase(int idClase, TimeSpan? hoyInicio, TimeSpan? hoyFin)
        {
            AsistenciaDeClase resultado = new AsistenciaDeClase { ClaseId = idClase };
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
                {
                    resultado.AhoraServidor = Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
                    resultado.LeidoEnRelojLocal = DateTime.Now;
                }

                resultado.ConRetiro = Esquema.AsistenciaRetirable(conexion);

                int profesor, materia, grupo;
                DateTime hasta;
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT c.usuarios_idusuarios, c.materias_id_materia, c.grupos_idgrupos, c.vigente_desde, c.vigente_hasta,
                           m.nombre_materia, g.nombre_grupo
                    FROM clases c
                    INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                    INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    WHERE c.idclases = @Clase", conexion))
                {
                    comando.Parameters.AddWithValue("@Clase", idClase);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            // Mis grupos lo ensena tal cual (es de DigBit): con que hacer.
                            throw new InvalidOperationException("Esa clase ya no existe (quizá se quitó mientras tanto).");
                        }

                        profesor = Convert.ToInt32(reader[0]);
                        materia = Convert.ToInt32(reader[1]);
                        grupo = Convert.ToInt32(reader[2]);
                        resultado.VigenteDesde = Convert.ToDateTime(reader[3], CultureInfo.InvariantCulture).Date;
                        hasta = Convert.ToDateTime(reader[4], CultureInfo.InvariantCulture).Date;
                        resultado.Materia = reader.GetString(5);
                        resultado.Grupo = reader.GetString(6);
                    }
                }

                DateTime hoy = resultado.AhoraServidor.Date;
                DateTime hastaHoy = hasta < hoy ? hasta : hoy;

                // Las del profesor en esos dias (entra por su indice): las de la clase
                // y, para los que llegan antes, las de sus otras clases el mismo dia.
                List<Sesion> sesiones = new List<Sesion>();
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT ca.idcodigos_accesos, COALESCE(ca.fecha, DATE(ca.hora_registro)) AS dia,
                           TIME(ca.hora_entrada) AS entrada, TIME(ca.hora_salida) AS salida, ca.laboratorios_idlaboratorios,
                           ca.materias_id_materia, ca.grupos_idgrupos
                    FROM codigos_accesos ca
                    WHERE ca.usuarios_idusuarios = @Profesor
                      AND COALESCE(ca.fecha, DATE(ca.hora_registro)) BETWEEN @Desde AND @Hasta
                      AND ca.hora_entrada IS NOT NULL AND ca.hora_salida IS NOT NULL
                    ORDER BY dia, entrada", conexion))
                {
                    comando.Parameters.AddWithValue("@Profesor", profesor);
                    comando.Parameters.AddWithValue("@Desde", resultado.VigenteDesde);
                    comando.Parameters.AddWithValue("@Hasta", hastaHoy);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
                            {
                                continue;
                            }

                            sesiones.Add(new Sesion
                            {
                                Id = reader.GetInt32(0),
                                Fecha = Convert.ToDateTime(reader[1], CultureInfo.InvariantCulture).Date,
                                Inicio = reader.GetTimeSpan(2),
                                Fin = reader.GetTimeSpan(3),
                                LaboratorioId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                                GrupoId = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                                DeLaClase = !reader.IsDBNull(5) && !reader.IsDBNull(6) && reader.GetInt32(5) == materia && reader.GetInt32(6) == grupo
                            });
                        }
                    }
                }

                // Las clases del horario de esos dias que no tienen sesion (todos sus
                // alumnos entraron antes, en la de la clase anterior): sin ellas no se
                // sabria a donde llevar a los que llegaron antes.
                sesiones.AddRange(SinSesion(conexion, profesor, materia, grupo, sesiones, resultado.VigenteDesde, hastaHoy));

                List<Sesion> deLaClase = sesiones.Where(s => s.DeLaClase && s.Id > 0).ToList();
                HashSet<DateTime> fechas = new HashSet<DateTime>(sesiones.Where(s => s.Id > 0).Select(s => s.Fecha));
                Dictionary<int, ModoCodigo> modos = ModosDeAcceso.LeerTodos(conexion);

                // Las clases seguidas en el mismo laboratorio con codigo fijo, una de
                // ellas de esta clase: (anterior, siguiente).
                List<KeyValuePair<Sesion, Sesion>> seguidas = new List<KeyValuePair<Sesion, Sesion>>();
                foreach (Sesion siguiente in sesiones.Where(s => fechas.Contains(s.Fecha)))
                {
                    if (ModosDeAcceso.De(modos, siguiente.LaboratorioId) != ModoCodigo.Fijo)
                    {
                        continue;
                    }

                    Sesion anterior = sesiones
                        .Where(s => s != siguiente && s.Fecha == siguiente.Fecha && s.LaboratorioId == siguiente.LaboratorioId
                            && s.Inicio < siguiente.Inicio && s.Fin > siguiente.Inicio - Margen)
                        .OrderByDescending(s => s.Inicio)
                        .FirstOrDefault();
                    if (anterior != null && anterior.DeLaClase != siguiente.DeLaClase)
                    {
                        seguidas.Add(new KeyValuePair<Sesion, Sesion>(anterior, siguiente));
                    }
                }

                List<Bitacora> bitacoras = LeerBitacoras(conexion,
                    deLaClase.Select(s => s.Id).Concat(seguidas.SelectMany(p => new[] { p.Key.Id, p.Value.Id })).Where(i => i > 0).Distinct().ToList(),
                    resultado.ConRetiro);
                Dictionary<int, int> gruposDeAlumnos = LeerGrupos(conexion, bitacoras.Select(b => b.AlumnoId).Distinct().ToList());
                Dictionary<int, Sesion> porId = sesiones.ToDictionary(s => s.Id);

                // Si una bitacora de la anterior cae en la ventana de la siguiente.
                Func<Bitacora, KeyValuePair<Sesion, Sesion>> ventanaDe = b => seguidas.FirstOrDefault(p => p.Key.Id == b.SesionId
                    && b.RegistradoEn.HasValue
                    && b.RegistradoEn.Value >= p.Value.Fecha + p.Value.Inicio - Margen
                    && b.RegistradoEn.Value < p.Value.Fecha + p.Value.Inicio + Llenado);
                HashSet<int> enLaClase = new HashSet<int>(bitacoras.Where(b => porId[b.SesionId].DeLaClase && ventanaDe(b).Key == null).Select(b => b.AlumnoId));
                HashSet<int> enOtra = new HashSet<int>(bitacoras.Where(b => !porId[b.SesionId].DeLaClase && ventanaDe(b).Key == null).Select(b => b.AlumnoId));

                // Cada bitacora en el dia de la clase a la que pertenece.
                Dictionary<int, AlumnoDeAsistencia> alumnos = new Dictionary<int, AlumnoDeAsistencia>();
                foreach (Bitacora b in bitacoras)
                {
                    Sesion donde = porId[b.SesionId];
                    KeyValuePair<Sesion, Sesion> par = ventanaDe(b);
                    if (par.Key != null && EsDeLaSiguiente(b.AlumnoId, par.Key, par.Value, gruposDeAlumnos, enLaClase, enOtra))
                    {
                        donde = par.Value;
                    }

                    if (!donde.DeLaClase)
                    {
                        continue;
                    }

                    AlumnoDeAsistencia alumno;
                    if (!alumnos.TryGetValue(b.AlumnoId, out alumno))
                    {
                        alumno = new AlumnoDeAsistencia { Id = b.AlumnoId };
                        alumnos[b.AlumnoId] = alumno;
                    }

                    List<int> delDia;
                    if (!alumno.BitacorasDelDia.TryGetValue(donde.Fecha, out delDia))
                    {
                        delDia = new List<int>();
                        alumno.BitacorasDelDia[donde.Fecha] = delDia;
                    }

                    delDia.Add(b.Id);
                    // Quien registro pero no estaba en el salon (lo dijo el profesor)
                    // sigue en la lista, con falta ese dia.
                    if (b.Retirada)
                    {
                        alumno.Retiradas.Add(donde.Fecha);
                    }
                    else
                    {
                        alumno.Presente.Add(donde.Fecha);
                    }
                }

                // Si ese dia tiene otra bitacora que cuenta, ese dia asistio.
                foreach (AlumnoDeAsistencia alumno in alumnos.Values)
                {
                    alumno.Retiradas.ExceptWith(alumno.Presente);
                }

                // Solo alumnos (no la cuenta de un profesor o del administrador que probo).
                LeerNombres(conexion, alumnos);

                // Cuentan los dias con alguien (tambien los de una clase sin sesion a la
                // que todos llegaron antes, y aunque a quien registro se le quitara la
                // asistencia) y hoy mientras dura la clase.
                HashSet<DateTime> conAlguien = new HashSet<DateTime>(alumnos.Values.SelectMany(a => a.BitacorasDelDia.Keys));
                TimeSpan horaAhora = resultado.AhoraServidor.TimeOfDay;
                foreach (IGrouping<DateTime, Sesion> dia in sesiones.Where(s => s.DeLaClase).GroupBy(s => s.Fecha))
                {
                    bool enCurso = dia.Key == hoy && dia.Any(s => s.Id > 0 && horaAhora < s.Fin);
                    if (conAlguien.Contains(dia.Key) || enCurso)
                    {
                        resultado.Dias.Add(new DiaDeAsistencia { Fecha = dia.Key, Inicio = dia.Min(s => s.Inicio), EnCurso = enCurso });
                    }
                }

                // Hoy, aunque aun nadie la abra (sin sesion), mientras dura: desde que
                // empieza, no antes (antes nadie falta todavia).
                if (hoyInicio.HasValue && hoyFin.HasValue && horaAhora >= hoyInicio.Value && horaAhora < hoyFin.Value
                    && hoy >= resultado.VigenteDesde && hoy <= hasta && !resultado.Dias.Any(d => d.Fecha == hoy))
                {
                    resultado.Dias.Add(new DiaDeAsistencia { Fecha = hoy, Inicio = hoyInicio.Value, EnCurso = true });
                }
                else if (hoyFin.HasValue && horaAhora < hoyFin.Value)
                {
                    DiaDeAsistencia deHoy = resultado.Dias.FirstOrDefault(d => d.Fecha == hoy);
                    if (deHoy != null)
                    {
                        deHoy.EnCurso = true;
                    }
                }

                resultado.Dias = resultado.Dias.OrderBy(d => d.Fecha).ToList();
                resultado.Alumnos = alumnos.Values.OrderBy(a => a.Nombre, StringComparer.Create(CultureInfo.GetCultureInfo("es-MX"), true)).ToList();
            }

            return resultado;
        }

        /// <summary>
        /// Las clases del profesor que tocaban los dias en que tuvo alguna sesion
        /// (sus franjas activas, que ese dia se daban con la vigencia de su clase
        /// y la suya propia, db/20, y no canceladas, y sus sesiones extra) y que
        /// no tienen sesion: con id negativo y sin bitacoras. Asi, si todos los
        /// alumnos de una clase entraron antes (en la sesion de la anterior),
        /// igual se sabe que esa clase seguia y se les cuenta en ella.
        /// </summary>
        private static List<Sesion> SinSesion(MySqlConnection conexion, int profesor, int materia, int grupo, List<Sesion> sesiones,
            DateTime desde, DateTime hasta)
        {
            List<Sesion> faltantes = new List<Sesion>();
            HashSet<DateTime> dias = new HashSet<DateTime>(sesiones.Select(s => s.Fecha));
            if (dias.Count == 0)
            {
                return faltantes;
            }

            List<Sesion> franjas = new List<Sesion>();
            Dictionary<int, int> diaDeFranja = new Dictionary<int, int>();
            // La de la clase y la propia de la franja (null = la de la clase): se
            // cruzan en memoria con VigenciaFranjas.SeDaEn.
            Dictionary<int, DateTime[]> vigenciaClase = new Dictionary<int, DateTime[]>();
            Dictionary<int, DateTime?[]> vigenciaFranja = new Dictionary<int, DateTime?[]>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT h.idhorarios, h.laboratorios_idlaboratorios, h.dia_semana, h.hora_inicio, h.hora_fin,
                       c.materias_id_materia, c.grupos_idgrupos, c.vigente_desde, c.vigente_hasta,
                       " + VigenciaFranjas.Columnas(Esquema.VigenciaDeFranjas(conexion)) + @"
                FROM horarios h
                INNER JOIN clases c ON c.idclases = h.clases_idclases
                WHERE c.usuarios_idusuarios = @Profesor AND c.activa = 1", conexion))
            {
                comando.Parameters.AddWithValue("@Profesor", profesor);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        franjas.Add(new Sesion
                        {
                            Id = id,
                            LaboratorioId = reader.GetInt32(1),
                            Inicio = reader.GetTimeSpan(3),
                            Fin = reader.GetTimeSpan(4),
                            GrupoId = reader.GetInt32(6),
                            DeLaClase = reader.GetInt32(5) == materia && reader.GetInt32(6) == grupo
                        });
                        diaDeFranja[id] = Convert.ToInt32(reader[2]);
                        vigenciaClase[id] = new[]
                        {
                            Convert.ToDateTime(reader[7], CultureInfo.InvariantCulture).Date,
                            Convert.ToDateTime(reader[8], CultureInfo.InvariantCulture).Date
                        };
                        vigenciaFranja[id] = new[]
                        {
                            reader.IsDBNull(9) ? (DateTime?)null : Convert.ToDateTime(reader[9], CultureInfo.InvariantCulture).Date,
                            reader.IsDBNull(10) ? (DateTime?)null : Convert.ToDateTime(reader[10], CultureInfo.InvariantCulture).Date
                        };
                    }
                }
            }

            HashSet<string> canceladas = new HashSet<string>();
            List<Sesion> extras = new List<Sesion>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT x.tipo, x.horarios_idhorarios, x.fecha, x.laboratorios_idlaboratorios, x.hora_inicio, x.hora_fin,
                       c.materias_id_materia, c.grupos_idgrupos
                FROM horario_excepciones x
                LEFT JOIN horarios h ON h.idhorarios = x.horarios_idhorarios
                INNER JOIN clases c ON c.idclases = COALESCE(x.clases_idclases, h.clases_idclases)
                WHERE c.usuarios_idusuarios = @Profesor AND x.fecha BETWEEN @Desde AND @Hasta", conexion))
            {
                comando.Parameters.AddWithValue("@Profesor", profesor);
                comando.Parameters.AddWithValue("@Desde", desde);
                comando.Parameters.AddWithValue("@Hasta", hasta);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime fecha = Convert.ToDateTime(reader[2], CultureInfo.InvariantCulture).Date;
                        if (reader.GetString(0) == "cancelada")
                        {
                            if (!reader.IsDBNull(1))
                            {
                                canceladas.Add(reader.GetInt32(1) + "|" + fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                            }

                            continue;
                        }

                        if (reader.IsDBNull(3) || reader.IsDBNull(4) || reader.IsDBNull(5))
                        {
                            continue;
                        }

                        extras.Add(new Sesion
                        {
                            Fecha = fecha,
                            LaboratorioId = reader.GetInt32(3),
                            Inicio = reader.GetTimeSpan(4),
                            Fin = reader.GetTimeSpan(5),
                            GrupoId = reader.GetInt32(7),
                            DeLaClase = reader.GetInt32(6) == materia && reader.GetInt32(7) == grupo
                        });
                    }
                }
            }

            int siguienteId = -1;
            foreach (DateTime dia in dias)
            {
                int diaSemana = dia.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dia.DayOfWeek;
                IEnumerable<Sesion> tocaban = franjas
                    .Where(f => diaDeFranja[f.Id] == diaSemana
                        && VigenciaFranjas.SeDaEn(dia, true, vigenciaClase[f.Id][0], vigenciaClase[f.Id][1], vigenciaFranja[f.Id][0], vigenciaFranja[f.Id][1])
                        && !canceladas.Contains(f.Id + "|" + dia.ToString("yyyyMMdd", CultureInfo.InvariantCulture)))
                    .Concat(extras.Where(x => x.Fecha == dia));
                foreach (Sesion bloque in tocaban)
                {
                    bool tieneSesion = sesiones.Any(s => s.Fecha == dia && s.LaboratorioId == bloque.LaboratorioId && s.Inicio == bloque.Inicio);
                    if (!tieneSesion)
                    {
                        faltantes.Add(new Sesion
                        {
                            Id = siguienteId--,
                            Fecha = dia,
                            Inicio = bloque.Inicio,
                            Fin = bloque.Fin,
                            LaboratorioId = bloque.LaboratorioId,
                            GrupoId = bloque.GrupoId,
                            DeLaClase = bloque.DeLaClase
                        });
                    }
                }
            }

            return faltantes;
        }

        /// <summary>
        /// Lo registrado en la clase anterior en la ventana de la siguiente es de la
        /// siguiente si el alumno es de su grupo y no del de la anterior; sin grupo
        /// conocido, si ha registrado fuera de esas ventanas solo en la siguiente.
        /// </summary>
        private static bool EsDeLaSiguiente(int alumno, Sesion anterior, Sesion siguiente, Dictionary<int, int> grupos,
            HashSet<int> enLaClase, HashSet<int> enOtra)
        {
            int suGrupo;
            if (grupos.TryGetValue(alumno, out suGrupo) && suGrupo > 0)
            {
                return suGrupo == siguiente.GrupoId && suGrupo != anterior.GrupoId;
            }

            return siguiente.DeLaClase
                ? enLaClase.Contains(alumno) && !enOtra.Contains(alumno)
                : enOtra.Contains(alumno) && !enLaClase.Contains(alumno);
        }

        private static List<Bitacora> LeerBitacoras(MySqlConnection conexion, List<int> sesiones, bool conRetiro)
        {
            List<Bitacora> bitacoras = new List<Bitacora>();
            if (sesiones.Count == 0)
            {
                return bitacoras;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT rb.idregistros_bitacoras, rb.fk_codigo_accesos, rb.fk_usuario, rb.registrado_en, "
                + (conRetiro ? "(ar.fk_bitacora IS NOT NULL)" : "0") + " AS retirada FROM registros_bitacoras rb "
                + (conRetiro ? "LEFT JOIN asistencias_retiradas ar ON ar.fk_bitacora = rb.idregistros_bitacoras " : "")
                + "WHERE rb.fk_codigo_accesos IN (" + string.Join(",", sesiones.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    bitacoras.Add(new Bitacora
                    {
                        Id = reader.GetInt32(0),
                        SesionId = reader.GetInt32(1),
                        AlumnoId = reader.GetInt32(2),
                        RegistradoEn = reader.IsDBNull(3) ? (DateTime?)null : Convert.ToDateTime(reader[3], CultureInfo.InvariantCulture),
                        Retirada = Convert.ToInt32(reader[4]) == 1
                    });
                }
            }

            return bitacoras;
        }

        /// <summary>
        /// Quita (o devuelve) la asistencia de hoy de un alumno que registro su
        /// bitacora pero no esta en el salon: sus bitacoras de hoy que cuentan para
        /// esta clase (las de AlumnoDeAsistencia.BitacorasDelDia) dejan de contar
        /// como asistencia, o vuelven a contar. Solo mientras la clase dura, con el
        /// reloj del servidor, y solo las de sesiones de hoy de este profesor.
        /// Devuelve null si se hizo, o por que no.
        /// </summary>
        public string CambiarAsistencia(int idClase, int idProfesor, int idAlumno, IList<int> bitacoras, bool quitar)
        {
            if (bitacoras == null || bitacoras.Count == 0)
            {
                return "Esa persona no registró su bitácora hoy en esta clase.";
            }

            string lista = string.Join(",", bitacoras.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.AsistenciaRetirable(conexion))
                {
                    return "Todavía no se pueden quitar asistencias: a la base de datos le falta una actualización (db/17). Avisa a la administración de DigBit.";
                }

                // Que la clase siga: una sesion suya de hoy que no ha terminado, o
                // una franja suya de hoy que no ha terminado (aun sin sesion, si
                // todos llegaron antes y quedaron en la anterior) y que hoy se da
                // tambien con su propia vigencia (db/20).
                bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT (SELECT COUNT(*) FROM codigos_accesos ca
                             INNER JOIN clases c ON c.idclases = @Clase
                             WHERE ca.usuarios_idusuarios = c.usuarios_idusuarios AND ca.materias_id_materia = c.materias_id_materia
                               AND ca.grupos_idgrupos = c.grupos_idgrupos AND COALESCE(ca.fecha, DATE(ca.hora_registro)) = CURDATE()
                               AND TIME(ca.hora_salida) > CURTIME())
                         + (SELECT COUNT(*) FROM horarios h
                             INNER JOIN clases c ON c.idclases = h.clases_idclases
                             WHERE h.clases_idclases = @Clase AND " + VigenciaFranjas.SeDa(conVigencia, "CURDATE()") + @"
                               AND h.dia_semana = WEEKDAY(CURDATE()) + 1 AND h.hora_inicio <= CURTIME() AND h.hora_fin > CURTIME())", conexion))
                {
                    comando.Parameters.AddWithValue("@Clase", idClase);
                    if (Convert.ToInt32(comando.ExecuteScalar()) == 0)
                    {
                        return "La clase no está en curso: la asistencia solo se puede cambiar mientras dura.";
                    }
                }

                // Que sean bitacoras de hoy de ese alumno, en sesiones de este profesor.
                using (MySqlCommand comando = new MySqlCommand(
                    "SELECT COUNT(*) FROM registros_bitacoras rb "
                    + "INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos "
                    + "WHERE rb.idregistros_bitacoras IN (" + lista + ") AND rb.fk_usuario = @Alumno "
                    + "AND ca.usuarios_idusuarios = @Profesor AND COALESCE(ca.fecha, DATE(ca.hora_registro)) = CURDATE()", conexion))
                {
                    comando.Parameters.AddWithValue("@Alumno", idAlumno);
                    comando.Parameters.AddWithValue("@Profesor", idProfesor);
                    if (Convert.ToInt32(comando.ExecuteScalar()) != bitacoras.Count)
                    {
                        return "Esa bitácora no es de hoy en una de tus clases: actualiza la lista.";
                    }
                }

                using (MySqlCommand comando = new MySqlCommand(quitar
                    ? "INSERT IGNORE INTO asistencias_retiradas (fk_bitacora, fk_profesor, retirada_en) SELECT idregistros_bitacoras, @Profesor, NOW() "
                      + "FROM registros_bitacoras WHERE idregistros_bitacoras IN (" + lista + ")"
                    : "DELETE FROM asistencias_retiradas WHERE fk_bitacora IN (" + lista + ")", conexion))
                {
                    comando.Parameters.AddWithValue("@Profesor", idProfesor);
                    comando.ExecuteNonQuery();
                }
            }

            Log.Info((quitar ? "Asistencia quitada" : "Asistencia devuelta") + " por el profesor id " + idProfesor + " al alumno id " + idAlumno
                + " en la clase " + idClase + " (bitacoras " + lista + ").");
            return null;
        }

        /// <summary>El grupo de cada alumno (carrera_grupo_semestre: su columna usuarios_numero_identificador guarda el id).</summary>
        private static Dictionary<int, int> LeerGrupos(MySqlConnection conexion, List<int> alumnos)
        {
            Dictionary<int, int> grupos = new Dictionary<int, int>();
            if (alumnos.Count == 0)
            {
                return grupos;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT usuarios_numero_identificador, grupos_idgrupos FROM carrera_grupo_semestre WHERE grupos_idgrupos IS NOT NULL AND usuarios_numero_identificador IN ("
                + string.Join(",", alumnos.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    grupos[reader.GetInt32(0)] = reader.GetInt32(1);
                }
            }

            return grupos;
        }

        /// <summary>Matricula y "Apellidos Nombre" de cada uno; quien no es alumno sale de la lista.</summary>
        private static void LeerNombres(MySqlConnection conexion, Dictionary<int, AlumnoDeAsistencia> alumnos)
        {
            if (alumnos.Count == 0)
            {
                return;
            }

            HashSet<int> sonAlumnos = new HashSet<int>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idusuarios, numero_identificador, CONCAT_WS(' ', apellido_paterno, apellido_materno, nombre) FROM usuarios "
                + "WHERE fk_tipo_usuario = 1 AND idusuarios IN (" + string.Join(",", alumnos.Keys.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    AlumnoDeAsistencia alumno;
                    if (alumnos.TryGetValue(reader.GetInt32(0), out alumno))
                    {
                        sonAlumnos.Add(alumno.Id);
                        alumno.Matricula = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        alumno.Nombre = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    }
                }
            }

            foreach (int id in alumnos.Keys.Where(id => !sonAlumnos.Contains(id)).ToList())
            {
                alumnos.Remove(id);
            }
        }
    }
}
