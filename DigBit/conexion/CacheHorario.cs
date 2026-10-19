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
        /// <summary>
        /// La vigencia propia de la franja (db/20; null = la de su clase). Una
        /// copia de antes no la trae: entonces vale toda la de la clase.
        /// </summary>
        public DateTime? VigenteDesde;
        public DateTime? VigenteHasta;
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

    /// <summary>
    /// Alumno de la copia local: solo su id y su matricula. La copia esta en una
    /// carpeta que la cuenta del laboratorio puede leer: cualquier alumno
    /// sentado en el equipo podria llevarse la lista.
    /// </summary>
    public class AlumnoCache
    {
        public int Id;
        public string Matricula;
        /// <summary>
        /// Solo de una copia de antes, que aun lo guardaba; si no, null y la
        /// pantalla del alumno usa la matricula.
        /// </summary>
        public string Nombre;
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

        /// <summary>
        /// La sesion (codigos_accesos) en el servidor, si ya existia: la de una
        /// bitacora validada en linea que se encolo porque la red se cayo al
        /// guardar. Con ella la subida no tiene que buscarla.
        /// </summary>
        public int? IdSesionServidor;

        /// <summary>
        /// Uso libre (sin codigos y sin clase, db/14): sin franja, sin extra y sin
        /// profesor. Las de las clases siempre llevan su profesor.
        /// </summary>
        public bool EsUsoLibre
        {
            get { return !FranjaId.HasValue && !ExcepcionId.HasValue && ProfesorId == 0; }
        }

        /// <summary>
        /// Identifica la sesion (franja o extra + fecha; en el uso libre, el
        /// laboratorio y el inicio de su tramo) para la regla de una bitacora por alumno.
        /// </summary>
        public string Clave
        {
            get
            {
                return (EsUsoLibre ? "l" + LaboratorioId + "-" + Inicio.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
                    : FranjaId.HasValue ? "f" + FranjaId.Value : "x" + ExcepcionId.GetValueOrDefault()) + "@" + Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// La sesion de un codigo aceptado EN LINEA, para encolar su bitacora si
        /// la red se cae al guardar. Misma Clave que la de la copia local para
        /// esa franja y fecha, asi que "una bitacora por alumno" vale en los dos modos.
        /// </summary>
        public static SesionLocal DesdeVentana(VentanaCodigo v)
        {
            return new SesionLocal
            {
                Codigo = v.Codigo,
                FranjaId = v.FranjaId,
                ExcepcionId = v.ExcepcionId,
                Fecha = v.Inicio.Date,
                Inicio = v.Inicio.TimeOfDay,
                Fin = v.Fin.TimeOfDay,
                LaboratorioId = v.IdLaboratorio,
                Laboratorio = v.Laboratorio,
                MateriaId = v.IdMateria,
                Materia = v.Materia,
                GrupoId = v.IdGrupo,
                Grupo = v.Grupo,
                ProfesorId = v.IdProfesor,
                Profesor = v.Profesor,
                IdSesionServidor = v.IdCodigo > 0 ? v.IdCodigo : (int?)null
            };
        }
    }

    /// <summary>
    /// Copia local del horario de ESTE laboratorio (fase 4, modo sin conexion):
    /// clases activas, franjas del laboratorio que aun se dan (con su vigencia
    /// propia, db/20, como atributos opcionales), excepciones de los proximos dias,
    /// la lista de alumnos (solo id y matricula) y los equipos que se marcan en el
    /// paso 3 de la bitacora. Se refresca cada vez que hay
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

        /// <summary>
        /// La base guardaba los equipos de alrededor sin internet cuando se hizo
        /// la copia (db/10). Sin conexion, solo entonces se le pregunta al alumno:
        /// si no, el aviso se perderia al subir.
        /// </summary>
        public bool ConAvisosDeRed;

        /// <summary>
        /// Como entraban los alumnos a sus clases en este laboratorio cuando se hizo
        /// la copia (db/11). Una copia de antes, o de una base sin db/11, no lo trae
        /// y cuenta como Fijo, que era lo que habia entonces.
        /// </summary>
        public ModoCodigo Modo;

        /// <summary>
        /// Sin codigos y con db/14 cuando se hizo la copia: sin clase abierta, el
        /// alumno registra uso libre tambien sin conexion. Las copias de antes no
        /// lo traen: entonces no.
        /// </summary>
        public bool ConUsoLibre;

        /// <summary>
        /// Los equipos que registraron bitacoras en este laboratorio hace poco, para
        /// marcarlos en el paso 3 sin conexion (Consultas.EquiposDelLaboratorio).
        /// Una copia de antes no la trae: entonces el alumno escribe el numero.
        /// </summary>
        public List<string> Equipos = new List<string>();

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
                cache.ConAvisosDeRed = Esquema.EquiposSinInternet(conexion);
                cache.Modo = ModosDeAcceso.Leer(conexion, idLaboratorio);
                cache.ConUsoLibre = cache.Modo == ModoCodigo.SinCodigo && Esquema.UsoLibre(conexion);
                if (cache.ConAvisosDeRed)
                {
                    try
                    {
                        cache.Equipos = Consultas.EquiposDelLaboratorio(conexion, idLaboratorio);
                    }
                    catch (Exception ex)
                    {
                        // Sin la lista la copia sigue sirviendo: el alumno escribe el numero.
                        Log.Aviso("Copia local: no se pudo leer la lista de equipos del paso 3: " + ex.Message);
                    }
                }

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

                // Con su vigencia propia (db/20) y solo las que aun sirven: la copia
                // se usa de hoy en adelante, y una franja terminada hace tiempo solo
                // ocuparia sitio (cambiada cada semestre, la historia crece).
                bool conVigencia = Esquema.VigenciaDeFranjas(conexion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT h.idhorarios, h.clases_idclases, h.dia_semana, h.hora_inicio, h.hora_fin, " + VigenciaFranjas.Columnas(conVigencia) + @"
                    FROM horarios h INNER JOIN clases c ON c.idclases = h.clases_idclases
                    WHERE h.laboratorios_idlaboratorios = @Lab AND c.activa = 1
                      AND " + VigenciaFranjas.Hasta(conVigencia) + " >= CURDATE() - INTERVAL 1 DAY", conexion))
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
                                Fin = reader.GetTimeSpan("hora_fin"),
                                VigenteDesde = reader.IsDBNull(reader.GetOrdinal("franja_desde")) ? (DateTime?)null : reader.GetDateTime("franja_desde").Date,
                                VigenteHasta = reader.IsDBNull(reader.GetOrdinal("franja_hasta")) ? (DateTime?)null : reader.GetDateTime("franja_hasta").Date
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

                // Alumnos: solo id y matricula. Ni el nombre ni el grupo (ver
                // AlumnoCache): sin servidor la pantalla usa la matricula. Nunca la
                // contrasena: sin servidor el alumno entra solo con su matricula y la
                // bitacora queda marcada. Los dados de baja no entran en la copia:
                // sin servidor tampoco pasan.
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT u.idusuarios, u.numero_identificador
                    FROM usuarios u
                    WHERE u.fk_tipo_usuario = 1" + (Esquema.TieneBajas(conexion) ? " AND u.activo = 1" : ""), conexion))
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cache.Alumnos.Add(new AlumnoCache
                        {
                            Id = reader.GetInt32("idusuarios"),
                            Matricula = reader.GetString("numero_identificador")
                        });
                    }
                }
            }

            cache.Guardar();
            Log.Info("Copia local del horario actualizada: " + cache.Laboratorio + ", " + cache.Clases.Count + " clases, "
                + cache.Franjas.Count + " franjas, " + cache.Excepciones.Count + " excepciones, " + cache.Alumnos.Count + " alumnos, "
                + cache.Equipos.Count + " equipos" + (cache.Modo == ModoCodigo.Fijo ? "." : ", acceso " + ModosDeAcceso.Valor(cache.Modo) + "."));
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
                new XAttribute("avisosDeRed", ConAvisosDeRed),
                // Opcional, como avisosDeRed: la version anterior lo ignora (version sigue en 1).
                new XAttribute("modoCodigo", ModosDeAcceso.Valor(Modo)),
                new XAttribute("usoLibre", ConUsoLibre),
                new XElement("clases", Clases.Select(c => new XElement("clase",
                    new XAttribute("id", c.Id), new XAttribute("codigo", c.Codigo),
                    new XAttribute("desde", c.VigenteDesde.ToString(Fmt, CultureInfo.InvariantCulture)),
                    new XAttribute("hasta", c.VigenteHasta.ToString(Fmt, CultureInfo.InvariantCulture)),
                    new XAttribute("profesorId", c.ProfesorId), new XAttribute("profesor", c.Profesor),
                    new XAttribute("grupoId", c.GrupoId), new XAttribute("grupo", c.Grupo),
                    new XAttribute("materiaId", c.MateriaId), new XAttribute("materia", c.Materia)))),
                // desde y hasta, opcionales (db/20): solo si la franja tiene la suya. La
                // version anterior los ignora (y veria la franja toda la vigencia de la clase).
                new XElement("franjas", Franjas.Select(f => new XElement("franja",
                    new XAttribute("id", f.Id), new XAttribute("claseId", f.ClaseId), new XAttribute("dia", f.DiaSemana),
                    new XAttribute("inicio", f.Inicio.ToString(FmtHora)), new XAttribute("fin", f.Fin.ToString(FmtHora)),
                    f.VigenteDesde.HasValue ? new XAttribute("desde", f.VigenteDesde.Value.ToString(Fmt, CultureInfo.InvariantCulture)) : null,
                    f.VigenteHasta.HasValue ? new XAttribute("hasta", f.VigenteHasta.Value.ToString(Fmt, CultureInfo.InvariantCulture)) : null))),
                new XElement("excepciones", Excepciones.Select(x => new XElement("excepcion",
                    new XAttribute("id", x.Id), new XAttribute("tipo", x.Tipo), new XAttribute("fecha", x.Fecha.ToString(Fmt, CultureInfo.InvariantCulture)),
                    new XAttribute("franjaId", x.FranjaId.HasValue ? x.FranjaId.Value.ToString() : ""),
                    new XAttribute("claseId", x.ClaseId.HasValue ? x.ClaseId.Value.ToString() : ""),
                    new XAttribute("inicio", x.Inicio.HasValue ? x.Inicio.Value.ToString(FmtHora) : ""),
                    new XAttribute("fin", x.Fin.HasValue ? x.Fin.Value.ToString(FmtHora) : ""),
                    new XAttribute("motivo", x.Motivo ?? "")))),
                // Solo id y matricula, tambien si la copia se leyo de una de antes con
                // nombres. grupoId va siempre en 0: la version anterior no lo usa, pero
                // sin el no podria leer la copia (sin el nombre, lo deja en null).
                new XElement("alumnos", Alumnos.Select(a => new XElement("alumno",
                    new XAttribute("id", a.Id), new XAttribute("matricula", a.Matricula), new XAttribute("grupoId", 0)))),
                // Opcional, como modoCodigo: la version anterior lo ignora.
                new XElement("equipos", (Equipos ?? new List<string>()).Select(e => new XElement("equipo", new XAttribute("nombre", e)))));

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
                    Desfase = TimeSpan.FromSeconds(double.Parse((string)raiz.Attribute("desfaseSegundos"), CultureInfo.InvariantCulture)),
                    // Las copias de antes no lo traen: entonces no se pregunta.
                    ConAvisosDeRed = (bool?)raiz.Attribute("avisosDeRed") ?? false,
                    // Ni el modo: entonces es el codigo fijo, lo que habia.
                    Modo = ModosDeAcceso.Desde((string)raiz.Attribute("modoCodigo")),
                    // Ni el uso libre (db/14): entonces no lo hay.
                    ConUsoLibre = (bool?)raiz.Attribute("usoLibre") ?? false
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
                    Fin = Hora((string)e.Attribute("fin")).Value,
                    // Las copias de antes no los traen: la franja vale lo que su clase.
                    VigenteDesde = FechaOpcional((string)e.Attribute("desde")),
                    VigenteHasta = FechaOpcional((string)e.Attribute("hasta"))
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

                // Las copias de antes traen tambien el nombre y el grupo. El nombre se
                // sigue mostrando hasta que la copia se refresque (ya esta en disco) y
                // el grupo no se lee.
                cache.Alumnos = raiz.Element("alumnos").Elements("alumno").Select(e => new AlumnoCache
                {
                    Id = (int)e.Attribute("id"),
                    Matricula = (string)e.Attribute("matricula"),
                    Nombre = (string)e.Attribute("nombre")
                }).ToList();

                // Las copias de antes no la traen: sin lista, el alumno escribe el numero.
                XElement equipos = raiz.Element("equipos");
                cache.Equipos = equipos == null ? new List<string>()
                    : equipos.Elements("equipo").Select(e => ((string)e.Attribute("nombre") ?? "").Trim()).Where(n => n.Length > 0).ToList();

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

        private static DateTime? FechaOpcional(string texto)
        {
            return string.IsNullOrEmpty(texto) ? (DateTime?)null : Fecha(texto);
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

        /// <summary>
        /// Agenda efectiva de este laboratorio en una fecha, con la copia local: lo
        /// que el servidor aceptaria (tambien las extras de clases vigentes). Una
        /// franja cuenta si ese dia se da con la vigencia de su clase y la suya
        /// propia (db/20): la terminada y su sucesora nunca salen juntas.
        /// </summary>
        public List<SesionAgenda> AgendaDelDia(DateTime fecha)
        {
            int dia = ((int)fecha.DayOfWeek + 6) % 7 + 1;
            List<SesionAgenda> lista = new List<SesionAgenda>();

            foreach (FranjaCache f in Franjas.Where(f => f.DiaSemana == dia))
            {
                ClaseCache c = Clases.FirstOrDefault(x => x.Id == f.ClaseId);
                if (c == null || !VigenciaFranjas.SeDaEn(fecha, true, c.VigenteDesde, c.VigenteHasta, f.VigenteDesde, f.VigenteHasta))
                {
                    continue;
                }

                ExcepcionCache cancelacion = Excepciones.FirstOrDefault(x => x.Tipo == "cancelada" && x.FranjaId == f.Id && x.Fecha.Date == fecha.Date);
                lista.Add(Entrada(c, false, f.Id, null, f.Inicio, f.Fin, cancelacion != null, cancelacion != null ? cancelacion.Motivo : ""));
            }

            foreach (ExcepcionCache x in Excepciones.Where(x => x.Tipo == "extra" && x.Fecha.Date == fecha.Date && x.ClaseId.HasValue && x.Inicio.HasValue && x.Fin.HasValue))
            {
                ClaseCache c = Clases.FirstOrDefault(k => k.Id == x.ClaseId.Value);
                if (c == null || fecha.Date < c.VigenteDesde.Date || fecha.Date > c.VigenteHasta.Date)
                {
                    continue;
                }

                lista.Add(Entrada(c, true, null, x.Id, x.Inicio.Value, x.Fin.Value, false, x.Motivo));
            }

            return lista.OrderBy(s => s.Inicio).ToList();
        }

        private SesionAgenda Entrada(ClaseCache c, bool esExtra, int? franja, int? excepcion, TimeSpan inicio, TimeSpan fin, bool cancelada, string motivo)
        {
            return new SesionAgenda
            {
                EsExtra = esExtra, FranjaId = franja, ExcepcionId = excepcion, ClaseId = c.Id, Codigo = c.Codigo,
                Profesor = c.Profesor, Grupo = c.Grupo, Materia = c.Materia, Inicio = inicio, Fin = fin,
                Cancelada = cancelada, Motivo = motivo ?? "", ProfesorId = c.ProfesorId, LaboratorioId = LaboratorioId,
                MateriaId = c.MateriaId, GrupoId = c.GrupoId
            };
        }

        public SesionAgenda ClaseEnCurso(DateTime ahora)
        {
            return ReglasCodigo.Abiertas(AgendaDelDia(ahora.Date), ahora.TimeOfDay).FirstOrDefault();
        }

        /// <summary>Las clases de este laboratorio en las que se puede registrar ahora (sin codigo).</summary>
        public List<SesionAgenda> ClasesAbiertas(DateTime ahora)
        {
            return ReglasCodigo.Abiertas(AgendaDelDia(ahora.Date), ahora.TimeOfDay);
        }

        /// <summary>
        /// Mismas reglas que HorariosDatos.ResolverCodigo pero con la copia local
        /// y el reloj estimado. Si entra, deja en sesionLocal lo necesario para
        /// encolar la bitacora, y devuelve una VentanaCodigo con IdCodigo = 0.
        /// Con la generacion de codigos (db/11) no hay nada que comprobar sin
        /// servidor: se pide elegir la clase, y el codigo fijo NUNCA se acepta (si
        /// no, bastaria desenchufar el cable para volver al codigo fijo).
        /// </summary>
        public ResultadoCodigo ResolverCodigo(string codigo, string matricula, out SesionLocal sesionLocal)
        {
            sesionLocal = null;
            if (Modo == ModoCodigo.Renovable)
            {
                return new ResultadoCodigo
                {
                    Motivo = MotivoCodigo.ModoCambiado,
                    Modo = Modo,
                    Mensaje = "Sin conexión no se puede comprobar el código de la clase: elige tu clase de la lista."
                };
            }

            codigo = (codigo ?? "").Trim();
            DateTime ahora = AhoraEstimado;

            // Una clase antes de la migracion 09; despues, todas las del profesor.
            List<ClaseCache> clases = Clases.Where(c => string.Equals(c.Codigo, codigo, StringComparison.OrdinalIgnoreCase)).ToList();
            if (clases.Count == 0)
            {
                return new ResultadoCodigo { Motivo = MotivoCodigo.NoExiste, Modo = Modo, Mensaje = "Código no válido. Revisa que esté bien escrito." };
            }

            List<ClaseCache> vigentes = clases.Where(c => ahora.Date >= c.VigenteDesde.Date && ahora.Date <= c.VigenteHasta.Date).ToList();
            if (vigentes.Count == 0)
            {
                return new ResultadoCodigo { Motivo = MotivoCodigo.FueraDePeriodo, Modo = Modo, Mensaje = "Esa clase no está en el periodo actual (vigente del "
                    + clases.Min(c => c.VigenteDesde).ToString("dd/MM/yyyy") + " al " + clases.Max(c => c.VigenteHasta).ToString("dd/MM/yyyy") + ")." };
            }

            // Sin servidor solo se conoce el horario de este laboratorio.
            HashSet<int> ids = new HashSet<int>(vigentes.Select(c => c.Id));
            List<SesionAgenda> hoy = AgendaDelDia(ahora.Date).Where(s => ids.Contains(s.ClaseId)).ToList();
            ResultadoCodigo rechazo;
            Candidata elegida = ReglasCodigo.Elegir(ReglasCodigo.DesdeAgenda(hoy, LaboratorioId, Laboratorio), LaboratorioId, ahora.TimeOfDay, true, out rechazo);
            if (elegida == null)
            {
                rechazo.Modo = Modo;
                return rechazo;
            }

            SesionAgenda vigente = hoy.First(s => s.ClaseId == elegida.ClaseId && s.EsExtra == elegida.EsExtra
                && (elegida.EsExtra ? s.ExcepcionId == elegida.Id : s.FranjaId == elegida.Id));
            return Aceptar(vigente, ahora, matricula, out sesionLocal);
        }

        /// <summary>
        /// Sin codigo, o con la generacion de codigos y sin servidor: el
        /// alumno eligio esta clase de la lista. Se vuelve a comprobar con la copia
        /// que sigue abierta. La bitacora queda marcada como sin conexion.
        /// </summary>
        public ResultadoCodigo ResolverClase(int? franjaId, int? excepcionId, string matricula, out SesionLocal sesionLocal)
        {
            sesionLocal = null;
            if (Modo == ModoCodigo.Fijo)
            {
                return new ResultadoCodigo { Motivo = MotivoCodigo.ModoCambiado, Modo = Modo, Mensaje = "En este laboratorio hace falta el código de tu docente." };
            }

            DateTime ahora = AhoraEstimado;
            List<SesionAgenda> agenda = AgendaDelDia(ahora.Date);
            List<SesionAgenda> abiertas = ReglasCodigo.Abiertas(agenda, ahora.TimeOfDay);
            SesionAgenda elegida = abiertas.FirstOrDefault(s => franjaId.HasValue
                ? !s.EsExtra && s.FranjaId == franjaId
                : s.EsExtra && excepcionId.HasValue && s.ExcepcionId == excepcionId);
            if (elegida == null)
            {
                if (abiertas.Count > 0)
                {
                    return new ResultadoCodigo { Motivo = MotivoCodigo.ElegirClase, Modo = Modo, Opciones = abiertas, Mensaje = "Esa clase ya no está abierta. Elige la tuya de la lista." };
                }

                ResultadoCodigo rechazo;
                ReglasCodigo.Elegir(ReglasCodigo.DesdeAgenda(agenda, LaboratorioId, Laboratorio), LaboratorioId, ahora.TimeOfDay, true, out rechazo, false);
                rechazo = rechazo ?? new ResultadoCodigo { Motivo = MotivoCodigo.HoyNoHayClase, Mensaje = "Hoy no hay clases en este laboratorio." };
                rechazo.Modo = Modo;
                return rechazo;
            }

            return Aceptar(elegida, ahora, matricula, out sesionLocal);
        }

        /// <summary>
        /// Sin codigos, sin clase abierta y sin servidor: uso libre (db/14) con la
        /// copia local, como HorariosDatos.ResolverUsoLibre. La sesion de uso libre
        /// la busca o la crea la cola al subir la bitacora (marcada sin conexion).
        /// </summary>
        public ResultadoCodigo ResolverUsoLibre(string matricula, out SesionLocal sesionLocal)
        {
            sesionLocal = null;
            if (Modo != ModoCodigo.SinCodigo)
            {
                return new ResultadoCodigo
                {
                    Motivo = MotivoCodigo.ModoCambiado,
                    Modo = Modo,
                    Mensaje = Modo == ModoCodigo.Renovable
                        ? "Sin conexión no se puede comprobar el código de la clase: elige tu clase de la lista."
                        : "En este laboratorio hace falta el código de tu docente."
                };
            }

            if (!ConUsoLibre)
            {
                return new ResultadoCodigo { Motivo = MotivoCodigo.HoyNoHayClase, Modo = Modo, Mensaje = "Ahora no hay ninguna clase en este laboratorio." };
            }

            DateTime ahora = AhoraEstimado;
            List<SesionAgenda> agenda = AgendaDelDia(ahora.Date);
            List<SesionAgenda> abiertas = ReglasCodigo.Abiertas(agenda, ahora.TimeOfDay);
            TramoLibre tramo = ReglasCodigo.TramoLibreEn(agenda, ahora.TimeOfDay);
            if (abiertas.Count > 0 || tramo == null)
            {
                return new ResultadoCodigo
                {
                    Motivo = MotivoCodigo.ElegirClase,
                    Modo = Modo,
                    Opciones = abiertas,
                    Mensaje = "Ya empezó una clase en este laboratorio: tu bitácora va en ella."
                };
            }

            sesionLocal = new SesionLocal
            {
                Codigo = HorariosDatos.CodigoUsoLibre, ClaseId = 0, FranjaId = null, ExcepcionId = null,
                Fecha = ahora.Date, Inicio = tramo.Inicio, Fin = tramo.Fin,
                LaboratorioId = LaboratorioId, Laboratorio = Laboratorio,
                MateriaId = 0, Materia = "", GrupoId = 0, Grupo = "", ProfesorId = 0, Profesor = ""
            };

            VentanaCodigo ventana = new VentanaCodigo
            {
                IdCodigo = 0,
                Codigo = HorariosDatos.CodigoUsoLibre,
                Inicio = ahora.Date + tramo.Inicio,
                Fin = ahora.Date + tramo.Fin,
                AhoraServidor = ahora,
                LeidoEnRelojLocal = DateTime.Now,
                IdLaboratorio = LaboratorioId,
                Laboratorio = Laboratorio,
                Grupo = "",
                Materia = "",
                Profesor = "",
                Estado = EstadoCodigo.Vigente
            };

            ResultadoCodigo resultado = new ResultadoCodigo { Motivo = MotivoCodigo.Aceptado, Modo = Modo, Ventana = ventana, Mensaje = "" };
            if (ColaBitacoras.YaRegistrada(sesionLocal.Clave, matricula))
            {
                ventana.Estado = EstadoCodigo.YaRegistrado;
                resultado.Motivo = MotivoCodigo.YaRegistrado;
                resultado.Mensaje = "Ya registraste tu bitácora de uso libre (pendiente de enviar al servidor).";
            }

            return resultado;
        }

        /// <summary>La clase aceptada sin servidor: lo que hay que recordar para encolar la bitacora y su VentanaCodigo.</summary>
        private ResultadoCodigo Aceptar(SesionAgenda vigente, DateTime ahora, string matricula, out SesionLocal sesionLocal)
        {
            ClaseCache clase = Clases.First(c => c.Id == vigente.ClaseId);
            // Codigo nunca vacio: la cola lo inserta en las sesiones que ya no tienen
            // franja ni extra, y las busca por el (ColaBitacoras). Fuera del codigo
            // fijo, al subirla, una sesion enlazada lleva el codigo generado de esa
            // clase y fecha (generacion de codigos) o uno al azar (sin codigos, o
            // sin secreto).
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
                FranjaId = vigente.FranjaId,
                ExcepcionId = vigente.ExcepcionId,
                IdMateria = clase.MateriaId,
                IdProfesor = clase.ProfesorId,
                Estado = EstadoCodigo.Vigente
            };

            ResultadoCodigo resultado = new ResultadoCodigo { Motivo = MotivoCodigo.Aceptado, Modo = Modo, Ventana = ventana, Mensaje = "" };
            if (ColaBitacoras.YaRegistrada(sesionLocal.Clave, matricula))
            {
                ventana.Estado = EstadoCodigo.YaRegistrado;
                resultado.Motivo = MotivoCodigo.YaRegistrado;
                resultado.Mensaje = "Ya registraste tu bitácora en esta clase (pendiente de enviar al servidor).";
            }

            return resultado;
        }
    }
}
