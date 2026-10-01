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
    /// <summary>Clase del horario tal como se guarda en la copia local.</summary>
    public class ClaseCache
    {
        public int Id;
        public string Codigo;
        public DateTime VigenteDesde;
        public DateTime VigenteHasta;
        public int ProfesorId;
        public string Profesor;
        public int GrupoId;
        public string Grupo;
        public int MateriaId;
        public string Materia;
    }

    public class FranjaCache
    {
        public int Id;
        public int ClaseId;
        public int DiaSemana;
        public TimeSpan Inicio;
        public TimeSpan Fin;
    }

    public class ExcepcionCache
    {
        public int Id;
        public string Tipo;
        public DateTime Fecha;
        public int? FranjaId;
        public int? ClaseId;
        public TimeSpan? Inicio;
        public TimeSpan? Fin;
        public string Motivo;
    }

    public class AlumnoCache
    {
        public int Id;
        public string Matricula;
        public string Nombre;
        public int GrupoId;
    }

    /// <summary>
    /// Lo que hay que recordar de una sesion resuelta SIN servidor para poder
    /// guardar la bitacora en la cola y subirla despues (fase 4).
    /// </summary>
    public class SesionLocal
    {
        public string Codigo;
        public int ClaseId;
        public int? FranjaId;
        public int? ExcepcionId;
        public DateTime Fecha;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public int LaboratorioId;
        public string Laboratorio;
        public int MateriaId;
        public string Materia;
        public int GrupoId;
        public string Grupo;
        public int ProfesorId;
        public string Profesor;

        /// <summary>Identifica la sesion (franja o extra + fecha) para la regla de una bitacora por alumno.</summary>
        public string Clave
        {
            get { return (FranjaId.HasValue ? "f" + FranjaId.Value : "x" + ExcepcionId.GetValueOrDefault()) + "@" + Fecha.ToString("yyyy-MM-dd"); }
        }
    }

    /// <summary>
    /// Copia local del horario de ESTE laboratorio (fase 4, modo sin conexion):
    /// clases activas, franjas del laboratorio, excepciones de los proximos dias
    /// y la lista de alumnos (sin contrasenas). Se refresca cada vez que hay
    /// servidor y se usa para resolver codigos cuando no lo hay. Archivo XML en
    /// %LOCALAPPDATA%\DigBit\horario_local.xml.
    /// </summary>
    internal class CacheHorario
    {
        public const int DiasHaciaAdelante = 14;
        private const string Fmt = "yyyy-MM-dd";
        private const string FmtHora = @"hh\:mm\:ss";

        public int LaboratorioId;
        public string Laboratorio;
        /// <summary>NOW() del servidor cuando se genero.</summary>
        public DateTime Generado;
        /// <summary>Reloj del servidor menos reloj local en el momento de generar.</summary>
        public TimeSpan Desfase;
        public List<ClaseCache> Clases = new List<ClaseCache>();
        public List<FranjaCache> Franjas = new List<FranjaCache>();
        public List<ExcepcionCache> Excepciones = new List<ExcepcionCache>();
        public List<AlumnoCache> Alumnos = new List<AlumnoCache>();

        public static string Carpeta
        {
            get
            {
                // Con Deep Freeze el perfil se borra al reiniciar y con el se irian el
                // horario local y las bitacoras sin enviar. CarpetaDatos permite
                // apuntar a un ThawSpace que si sobrevive.
                return CarpetaDatos.Resolver(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigBit"));
            }
        }

        public static string Ruta
        {
            get { return Path.Combine(Carpeta, "horario_local.xml"); }
        }

        /// <summary>Mejor estimacion del reloj del servidor sin servidor.</summary>
        public DateTime AhoraEstimado
        {
            get { return DateTime.Now + Desfase; }
        }

        public TimeSpan Edad
        {
            get { return DateTime.Now - (Generado - Desfase); }
        }

        // ---------------------------------------------------------------------
        // Refrescar desde la base
        // ---------------------------------------------------------------------

        /// <summary>Lee el horario del laboratorio desde la base, lo guarda en disco y lo devuelve.</summary>
        public static CacheHorario Actualizar(int idLaboratorio)
        {
            CacheHorario cache = new CacheHorario { LaboratorioId = idLaboratorio };
            DateTime localAntes = DateTime.Now;

            using (MySqlConnection conexion = new Conexion().GetConexion())
            {
                using (MySqlCommand comando = new MySqlCommand("SELECT NOW(), (SELECT nombre_laboratorio FROM laboratorios WHERE idlaboratorios = @Lab)", conexion))
                {
                    comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (!reader.Read() || reader.IsDBNull(1))
                        {
                            throw new InvalidOperationException("El laboratorio " + idLaboratorio + " no existe.");
                        }

                        cache.Generado = reader.GetDateTime(0);
                        cache.Laboratorio = reader.GetString(1);
                    }
                }

                cache.Desfase = cache.Generado - localAntes;

                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT c.idclases, c.codigo, c.vigente_desde, c.vigente_hasta, c.usuarios_idusuarios,
                           CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS profesor,
                           c.grupos_idgrupos, g.nombre_grupo, c.materias_id_materia, m.nombre_materia
                    FROM clases c
                    INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                    INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                    WHERE c.activa = 1" + (Esquema.TieneBajas(conexion) ? " AND u.activo = 1" : ""), conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cache.Clases.Add(new ClaseCache
                        {
                            Id = reader.GetInt32("idclases"),
                            Codigo = reader.GetString("codigo"),
                            VigenteDesde = reader.GetDateTime("vigente_desde"),
                            VigenteHasta = reader.GetDateTime("vigente_hasta"),
                            ProfesorId = reader.GetInt32("usuarios_idusuarios"),
                            Profesor = reader.GetString("profesor"),
                            GrupoId = reader.GetInt32("grupos_idgrupos"),
                            Grupo = reader.GetString("nombre_grupo"),
                            MateriaId = reader.GetInt32("materias_id_materia"),
                            Materia = reader.GetString("nombre_materia")
                        });
                    }
                }

                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT h.idhorarios, h.clases_idclases, h.dia_semana, h.hora_inicio, h.hora_fin
                    FROM horarios h INNER JOIN clases c ON c.idclases = h.clases_idclases
                    WHERE h.laboratorios_idlaboratorios = @Lab AND c.activa = 1", conexion))
                {
                    comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            cache.Franjas.Add(new FranjaCache
                            {
                                Id = reader.GetInt32("idhorarios"),
                                ClaseId = reader.GetInt32("clases_idclases"),
                                DiaSemana = reader.GetInt32("dia_semana"),
                                Inicio = reader.GetTimeSpan("hora_inicio"),
                                Fin = reader.GetTimeSpan("hora_fin")
                            });
                        }
                    }
                }

                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT x.idhorario_excepciones, x.tipo, x.fecha, x.horarios_idhorarios, x.clases_idclases, x.hora_inicio, x.hora_fin, x.motivo
                    FROM horario_excepciones x
                    LEFT JOIN horarios h ON h.idhorarios = x.horarios_idhorarios
                    WHERE x.fecha BETWEEN CURDATE() - INTERVAL 1 DAY AND CURDATE() + INTERVAL @Dias DAY
                      AND COALESCE(x.laboratorios_idlaboratorios, h.laboratorios_idlaboratorios) = @Lab", conexion))
                {
                    comando.Parameters.AddWithValue("@Lab", idLaboratorio);
                    comando.Parameters.AddWithValue("@Dias", DiasHaciaAdelante);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            cache.Excepciones.Add(new ExcepcionCache
                            {
                                Id = reader.GetInt32("idhorario_excepciones"),
                                Tipo = reader.GetString("tipo"),
                                Fecha = reader.GetDateTime("fecha"),
                                FranjaId = reader.IsDBNull(reader.GetOrdinal("horarios_idhorarios")) ? (int?)null : reader.GetInt32("horarios_idhorarios"),
                                ClaseId = reader.IsDBNull(reader.GetOrdinal("clases_idclases")) ? (int?)null : reader.GetInt32("clases_idclases"),
                                Inicio = reader.IsDBNull(reader.GetOrdinal("hora_inicio")) ? (TimeSpan?)null : reader.GetTimeSpan("hora_inicio"),
                                Fin = reader.IsDBNull(reader.GetOrdinal("hora_fin")) ? (TimeSpan?)null : reader.GetTimeSpan("hora_fin"),
                                Motivo = reader.IsDBNull(reader.GetOrdinal("motivo")) ? "" : reader.GetString("motivo")
                            });
                        }
                    }
                }

                // Alumnos: matricula, nombre y grupo. Nunca la contrasena: sin servidor
                // el alumno entra solo con su matricula y la bitacora queda marcada.
                // Los dados de baja no entran en la copia: sin servidor tampoco pasan.
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT u.idusuarios, u.numero_identificador,
                           CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS nombre,
                           COALESCE(cgs.grupos_idgrupos, 0) AS grupo_id
                    FROM usuarios u
                    LEFT JOIN carrera_grupo_semestre cgs ON cgs.usuarios_numero_identificador = u.idusuarios
                    WHERE u.fk_tipo_usuario = 1" + (Esquema.TieneBajas(conexion) ? " AND u.activo = 1" : ""), conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cache.Alumnos.Add(new AlumnoCache
                        {
                            Id = reader.GetInt32("idusuarios"),
                            Matricula = reader.GetString("numero_identificador"),
                            Nombre = reader.GetString("nombre"),
                            GrupoId = reader.GetInt32("grupo_id")
                        });
                    }
                }
            }

            cache.Guardar();
            Log.Info("Copia local del horario actualizada: " + cache.Laboratorio + ", " + cache.Clases.Count + " clases, "
                + cache.Franjas.Count + " franjas, " + cache.Excepciones.Count + " excepciones, " + cache.Alumnos.Count + " alumnos.");
            return cache;
        }

        // ---------------------------------------------------------------------
        // Disco
        // ---------------------------------------------------------------------

        public void Guardar()
        {
            Directory.CreateDirectory(Carpeta);
            XElement raiz = new XElement("horario",
                new XAttribute("version", 1),
                new XAttribute("laboratorioId", LaboratorioId),
                new XAttribute("laboratorio", Laboratorio ?? ""),
                new XAttribute("generado", Generado.ToString("o", CultureInfo.InvariantCulture)),
                new XAttribute("desfaseSegundos", Desfase.TotalSeconds.ToString(CultureInfo.InvariantCulture)),
                new XElement("clases", Clases.Select(c => new XElement("clase",
                    new XAttribute("id", c.Id), new XAttribute("codigo", c.Codigo),
                    new XAttribute("desde", c.VigenteDesde.ToString(Fmt)), new XAttribute("hasta", c.VigenteHasta.ToString(Fmt)),
                    new XAttribute("profesorId", c.ProfesorId), new XAttribute("profesor", c.Profesor),
                    new XAttribute("grupoId", c.GrupoId), new XAttribute("grupo", c.Grupo),
                    new XAttribute("materiaId", c.MateriaId), new XAttribute("materia", c.Materia)))),
                new XElement("franjas", Franjas.Select(f => new XElement("franja",
                    new XAttribute("id", f.Id), new XAttribute("claseId", f.ClaseId), new XAttribute("dia", f.DiaSemana),
                    new XAttribute("inicio", f.Inicio.ToString(FmtHora)), new XAttribute("fin", f.Fin.ToString(FmtHora))))),
                new XElement("excepciones", Excepciones.Select(x => new XElement("excepcion",
                    new XAttribute("id", x.Id), new XAttribute("tipo", x.Tipo), new XAttribute("fecha", x.Fecha.ToString(Fmt)),
                    new XAttribute("franjaId", x.FranjaId.HasValue ? x.FranjaId.Value.ToString() : ""),
                    new XAttribute("claseId", x.ClaseId.HasValue ? x.ClaseId.Value.ToString() : ""),
                    new XAttribute("inicio", x.Inicio.HasValue ? x.Inicio.Value.ToString(FmtHora) : ""),
                    new XAttribute("fin", x.Fin.HasValue ? x.Fin.Value.ToString(FmtHora) : ""),
                    new XAttribute("motivo", x.Motivo ?? "")))),
                new XElement("alumnos", Alumnos.Select(a => new XElement("alumno",
                    new XAttribute("id", a.Id), new XAttribute("matricula", a.Matricula),
                    new XAttribute("nombre", a.Nombre), new XAttribute("grupoId", a.GrupoId)))));

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

        /// <summary>Devuelve null si no hay copia o no se puede leer.</summary>
        public static CacheHorario Cargar()
        {
            if (!File.Exists(Ruta))
            {
                return null;
            }

            try
            {
                XElement raiz = XDocument.Load(Ruta).Root;
                CacheHorario cache = new CacheHorario
                {
                    LaboratorioId = (int)raiz.Attribute("laboratorioId"),
                    Laboratorio = (string)raiz.Attribute("laboratorio"),
                    Generado = DateTime.Parse((string)raiz.Attribute("generado"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    Desfase = TimeSpan.FromSeconds(double.Parse((string)raiz.Attribute("desfaseSegundos"), CultureInfo.InvariantCulture))
                };

                cache.Clases = raiz.Element("clases").Elements("clase").Select(e => new ClaseCache
                {
                    Id = (int)e.Attribute("id"),
                    Codigo = (string)e.Attribute("codigo"),
                    VigenteDesde = Fecha((string)e.Attribute("desde")),
                    VigenteHasta = Fecha((string)e.Attribute("hasta")),
                    ProfesorId = (int)e.Attribute("profesorId"),
                    Profesor = (string)e.Attribute("profesor"),
                    GrupoId = (int)e.Attribute("grupoId"),
                    Grupo = (string)e.Attribute("grupo"),
                    MateriaId = (int)e.Attribute("materiaId"),
                    Materia = (string)e.Attribute("materia")
                }).ToList();

                cache.Franjas = raiz.Element("franjas").Elements("franja").Select(e => new FranjaCache
                {
                    Id = (int)e.Attribute("id"),
                    ClaseId = (int)e.Attribute("claseId"),
                    DiaSemana = (int)e.Attribute("dia"),
                    Inicio = Hora((string)e.Attribute("inicio")).Value,
                    Fin = Hora((string)e.Attribute("fin")).Value
                }).ToList();

                cache.Excepciones = raiz.Element("excepciones").Elements("excepcion").Select(e => new ExcepcionCache
                {
                    Id = (int)e.Attribute("id"),
                    Tipo = (string)e.Attribute("tipo"),
                    Fecha = Fecha((string)e.Attribute("fecha")),
                    FranjaId = Entero((string)e.Attribute("franjaId")),
                    ClaseId = Entero((string)e.Attribute("claseId")),
                    Inicio = Hora((string)e.Attribute("inicio")),
                    Fin = Hora((string)e.Attribute("fin")),
                    Motivo = (string)e.Attribute("motivo")
                }).ToList();

                cache.Alumnos = raiz.Element("alumnos").Elements("alumno").Select(e => new AlumnoCache
                {
                    Id = (int)e.Attribute("id"),
                    Matricula = (string)e.Attribute("matricula"),
                    Nombre = (string)e.Attribute("nombre"),
                    GrupoId = (int)e.Attribute("grupoId")
                }).ToList();

                return cache;
            }
            catch (Exception ex)
            {
                Log.Error("Leer la copia local del horario", ex);
                return null;
            }
        }

        private static DateTime Fecha(string texto)
        {
            return DateTime.ParseExact(texto, Fmt, CultureInfo.InvariantCulture);
        }

        private static TimeSpan? Hora(string texto)
        {
            return string.IsNullOrEmpty(texto) ? (TimeSpan?)null : TimeSpan.ParseExact(texto, FmtHora, CultureInfo.InvariantCulture);
        }

        private static int? Entero(string texto)
        {
            return string.IsNullOrEmpty(texto) ? (int?)null : int.Parse(texto, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------------
        // Consultas sin servidor
        // ---------------------------------------------------------------------

        public AlumnoCache BuscarAlumno(string matricula)
        {
            matricula = (matricula ?? "").Trim();
            return Alumnos.FirstOrDefault(a => string.Equals(a.Matricula, matricula, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Agenda efectiva de este laboratorio en una fecha, con la copia local.</summary>
        public List<SesionAgenda> AgendaDelDia(DateTime fecha)
        {
            int dia = ((int)fecha.DayOfWeek + 6) % 7 + 1;
            List<SesionAgenda> lista = new List<SesionAgenda>();

            foreach (FranjaCache f in Franjas.Where(f => f.DiaSemana == dia))
            {
                ClaseCache c = Clases.FirstOrDefault(x => x.Id == f.ClaseId);
                if (c == null || fecha.Date < c.VigenteDesde.Date || fecha.Date > c.VigenteHasta.Date)
                {
                    continue;
                }

                ExcepcionCache cancelacion = Excepciones.FirstOrDefault(x => x.Tipo == "cancelada" && x.FranjaId == f.Id && x.Fecha.Date == fecha.Date);
                lista.Add(new SesionAgenda
                {
                    EsExtra = false, FranjaId = f.Id, ClaseId = c.Id, Codigo = c.Codigo, Profesor = c.Profesor, Grupo = c.Grupo, Materia = c.Materia,
                    Inicio = f.Inicio, Fin = f.Fin, Cancelada = cancelacion != null, Motivo = cancelacion != null ? cancelacion.Motivo : ""
                });
            }

            foreach (ExcepcionCache x in Excepciones.Where(x => x.Tipo == "extra" && x.Fecha.Date == fecha.Date && x.ClaseId.HasValue && x.Inicio.HasValue && x.Fin.HasValue))
            {
                ClaseCache c = Clases.FirstOrDefault(k => k.Id == x.ClaseId.Value);
                if (c == null)
                {
                    continue;
                }

                lista.Add(new SesionAgenda
                {
                    EsExtra = true, ExcepcionId = x.Id, ClaseId = c.Id, Codigo = c.Codigo, Profesor = c.Profesor, Grupo = c.Grupo, Materia = c.Materia,
                    Inicio = x.Inicio.Value, Fin = x.Fin.Value, Cancelada = false, Motivo = x.Motivo
                });
            }

            return lista.OrderBy(s => s.Inicio).ToList();
        }

        public SesionAgenda ClaseEnCurso(DateTime ahora)
        {
            TimeSpan margen = TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            return AgendaDelDia(ahora.Date)
                .Where(s => !s.Cancelada && ahora.TimeOfDay >= s.Inicio - margen && ahora.TimeOfDay <= s.Fin)
                .OrderBy(s => s.Inicio)
                .FirstOrDefault();
        }

        /// <summary>
        /// Mismas reglas que HorariosDatos.ResolverCodigo pero con la copia local
        /// y el reloj estimado. Si entra, deja en sesionLocal lo necesario para
        /// encolar la bitacora, y devuelve una VentanaCodigo con IdCodigo = 0.
        /// </summary>
        public ResultadoCodigo ResolverCodigo(string codigo, string matricula, out SesionLocal sesionLocal)
        {
            sesionLocal = null;
            codigo = (codigo ?? "").Trim();
            DateTime ahora = AhoraEstimado;

            // Una clase antes de la migracion 09; despues, todas las del profesor.
            List<ClaseCache> clases = Clases.Where(c => string.Equals(c.Codigo, codigo, StringComparison.OrdinalIgnoreCase)).ToList();
            if (clases.Count == 0)
            {
                return new ResultadoCodigo { Motivo = MotivoCodigo.NoExiste, Mensaje = "Código no válido. Revisa que esté bien escrito." };
            }

            List<ClaseCache> vigentes = clases.Where(c => ahora.Date >= c.VigenteDesde.Date && ahora.Date <= c.VigenteHasta.Date).ToList();
            if (vigentes.Count == 0)
            {
                return new ResultadoCodigo { Motivo = MotivoCodigo.FueraDePeriodo, Mensaje = "Esa clase no está en el periodo actual (vigente del "
                    + clases.Min(c => c.VigenteDesde).ToString("dd/MM/yyyy") + " al " + clases.Max(c => c.VigenteHasta).ToString("dd/MM/yyyy") + ")." };
            }

            // Sin servidor solo se conoce el horario de este laboratorio.
            HashSet<int> ids = new HashSet<int>(vigentes.Select(c => c.Id));
            List<SesionAgenda> hoy = AgendaDelDia(ahora.Date).Where(s => ids.Contains(s.ClaseId)).ToList();
            List<Candidata> candidatas = hoy.Select(s => new Candidata
            {
                ClaseId = s.ClaseId,
                EsExtra = s.EsExtra,
                Id = s.EsExtra ? s.ExcepcionId.GetValueOrDefault() : s.FranjaId.GetValueOrDefault(),
                LaboratorioId = LaboratorioId,
                Laboratorio = Laboratorio,
                Inicio = s.Inicio,
                Fin = s.Fin,
                Cancelada = s.Cancelada
            }).ToList();

            ResultadoCodigo rechazo;
            Candidata elegida = ReglasCodigo.Elegir(candidatas, LaboratorioId, ahora.TimeOfDay, true, out rechazo);
            if (elegida == null)
            {
                return rechazo;
            }

            ClaseCache clase = vigentes.First(c => c.Id == elegida.ClaseId);
            SesionAgenda vigente = hoy.First(s => s.ClaseId == elegida.ClaseId && s.EsExtra == elegida.EsExtra
                && (elegida.EsExtra ? s.ExcepcionId == elegida.Id : s.FranjaId == elegida.Id));

            sesionLocal = new SesionLocal
            {
                Codigo = clase.Codigo, ClaseId = clase.Id, FranjaId = vigente.FranjaId, ExcepcionId = vigente.ExcepcionId,
                Fecha = ahora.Date, Inicio = vigente.Inicio, Fin = vigente.Fin,
                LaboratorioId = LaboratorioId, Laboratorio = Laboratorio,
                MateriaId = clase.MateriaId, Materia = clase.Materia, GrupoId = clase.GrupoId, Grupo = clase.Grupo,
                ProfesorId = clase.ProfesorId, Profesor = clase.Profesor
            };

            // El fin se expresa en reloj del servidor estimado; FinEnRelojLocal lo
            // traslada al reloj de este equipo restando el desfase.
            VentanaCodigo ventana = new VentanaCodigo
            {
                IdCodigo = 0,
                Codigo = clase.Codigo,
                Inicio = ahora.Date + vigente.Inicio,
                Fin = ahora.Date + vigente.Fin,
                AhoraServidor = ahora,
                LeidoEnRelojLocal = DateTime.Now,
                IdLaboratorio = LaboratorioId,
                Laboratorio = Laboratorio,
                IdGrupo = clase.GrupoId,
                Grupo = clase.Grupo,
                Materia = clase.Materia,
                Profesor = clase.Profesor,
                Estado = EstadoCodigo.Vigente
            };

            ResultadoCodigo resultado = new ResultadoCodigo { Motivo = MotivoCodigo.Aceptado, Ventana = ventana, Mensaje = "" };
            if (ColaBitacoras.YaRegistrada(sesionLocal.Clave, matricula))
            {
                ventana.Estado = EstadoCodigo.YaRegistrado;
                resultado.Motivo = MotivoCodigo.YaRegistrado;
                resultado.Mensaje = "Ya registraste tu bitácora con este código (pendiente de enviar al servidor).";
            }

            return resultado;
        }
    }
}
