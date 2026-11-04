using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DigBit.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigBit.conexion
{
    public enum TipoAlerta
    {
        /// <summary>Una clase que termino sin ninguna bitacora: el profesor no vino o no pidio registrar.</summary>
        SinBitacoras,
        /// <summary>Una clase en curso desde hace un rato (AlertasDatos.MinutosSinRegistro) en la que nadie ha registrado.</summary>
        EnCursoSinBitacoras,
        /// <summary>Fallas que reportaron los alumnos (y equipos que avisaron sin internet) en un laboratorio un dia.</summary>
        Fallas,
        /// <summary>Un cambio del horario (franja, clase, cancelacion o sesion extra) que hizo otra persona (db/21).</summary>
        CambioDeHorario,
        /// <summary>Docentes que entraron con su contrasena del SIIA y esperan que la administracion los enlace (db/24): una sola, con cuantos.</summary>
        DocentesPorEnlazar
    }

    /// <summary>Una alerta del panel del administrador (AlertasDatos).</summary>
    public class Alerta
    {
        public TipoAlerta Tipo;
        public DateTime Fecha;
        /// <summary>Desde cuando es cierta, o su ultimo cambio (otro reporte): con esto se sabe si es nueva.</summary>
        public DateTime Momento;
        public int LaboratorioId;
        public string Laboratorio = "";

        // De una clase (sin bitacoras).
        public int? FranjaId;
        public int? ExcepcionId;
        public bool EsExtra;
        public string Materia = "";
        public string Grupo = "";
        public string Profesor = "";
        public TimeSpan Inicio;
        public TimeSpan Fin;
        /// <summary>De una en curso: las asistencias que ya dio su profesor a quien no pudo registrar (db/22).</summary>
        public int AsistenciasDadas;

        // De las fallas de un dia en un laboratorio.
        public int Bitacoras;
        public int ConFallas;
        public int Red;
        public int Hardware;
        public int Software;
        /// <summary>Los equipos desde los que se reporto alguna falla ("PC-03, PC-07").</summary>
        public string Equipos = "";
        /// <summary>Las fallas que reportaron los profesores ese dia en ese laboratorio (db/15).</summary>
        public int DelProfesor;
        /// <summary>Cada una en una linea corta (ReporteProfesor.Resumen).</summary>
        public List<string> ReportesDelProfesor = new List<string>();
        public List<EquipoSinInternet> SinInternet = new List<EquipoSinInternet>();

        // De un cambio de horario (db/21): su renglon del historial, que paso y quien lo hizo.
        public int CambioId;
        public string TituloDelCambio = "";
        public string QueCambio = "";
        public string Quien = "";

        // De los docentes por enlazar con el SIIA (db/24): cuantos y los mas recientes ("12345 Zita López, ...").
        public int PorEnlazar;
        public string QuienesPorEnlazar = "";

        /// <summary>Para conservar la fila elegida al recargar.</summary>
        public string Clave
        {
            get
            {
                if (Tipo == TipoAlerta.CambioDeHorario)
                {
                    return (int)Tipo + "|c" + CambioId.ToString(CultureInfo.InvariantCulture);
                }

                if (Tipo == TipoAlerta.DocentesPorEnlazar)
                {
                    return (int)Tipo + "|enlazar";
                }

                return (int)Tipo + "|" + LaboratorioId + "|" + Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                    + "|" + (FranjaId.HasValue ? "f" + FranjaId.Value : ExcepcionId.HasValue ? "x" + ExcepcionId.Value : "");
            }
        }

        public string Titulo
        {
            get
            {
                switch (Tipo)
                {
                    case TipoAlerta.SinBitacoras:
                        return "Nadie registró bitácora";
                    case TipoAlerta.EnCursoSinBitacoras:
                        return "En curso y nadie ha registrado";
                    case TipoAlerta.CambioDeHorario:
                        return TituloDelCambio;
                    case TipoAlerta.DocentesPorEnlazar:
                        return "Docentes por enlazar (" + PorEnlazar + ")";
                    default:
                        // Las de los alumnos y las que reporto el profesor (db/15).
                        int fallas = ConFallas + DelProfesor;
                        return fallas == 0 ? "Equipos sin internet"
                            : fallas == 1 ? "1 falla reportada" : fallas + " fallas reportadas";
                }
            }
        }

        public string Detalle
        {
            get
            {
                string clase = Materia + ", " + Grupo + (Profesor.Length > 0 ? ", con " + Profesor : "");
                switch (Tipo)
                {
                    case TipoAlerta.SinBitacoras:
                        return clase + (EsExtra ? " (sesión extra)" : "") + ". ¿No vino o no pidió registrar?";
                    case TipoAlerta.EnCursoSinBitacoras:
                        // Con asistencias dadas (db/22) su docente si llego: quiza no hay internet.
                        return AsistenciasDadas > 0
                            ? clase + ": empezó a las " + HorariosDatos.Hora(Inicio) + " y nadie ha registrado; su docente ya dio "
                              + (AsistenciasDadas == 1 ? "1 asistencia" : AsistenciasDadas + " asistencias")
                              + ": quizá los equipos del laboratorio no tienen internet."
                            : clase + ": empezó a las " + HorariosDatos.Hora(Inicio) + " y nadie ha registrado. ¿Llegó su docente?";
                    case TipoAlerta.CambioDeHorario:
                        return QueCambio.TrimEnd('.') + ". Lo hizo " + Quien + ".";
                    case TipoAlerta.DocentesPorEnlazar:
                        // La persona responsable no enlaza (solo consulta): lo hace la administracion.
                        if (Permisos.SoloConsulta)
                        {
                            return (PorEnlazar == 1 ? "1 docente entró con su contraseña del SIIA y espera que la administración lo enlace"
                                    : PorEnlazar + " docentes entraron con su contraseña del SIIA y esperan que la administración los enlace")
                                + " con su cuenta de DigBit: " + QuienesPorEnlazar + ". Si te pregunta, que vaya con la administración.";
                        }

                        return (PorEnlazar == 1 ? "1 docente entró con su contraseña del SIIA y espera que lo enlaces"
                                : PorEnlazar + " docentes entraron con su contraseña del SIIA y esperan que los enlaces")
                            + " con su cuenta de DigBit: " + QuienesPorEnlazar + ".";
                    default:
                        List<string> partes = new List<string>();
                        if (ConFallas > 0)
                        {
                            List<string> areas = new List<string>();
                            if (Hardware > 0) areas.Add("hardware " + Hardware);
                            if (Software > 0) areas.Add("software " + Software);
                            if (Red > 0) areas.Add("red " + Red);
                            partes.Add((ConFallas == 1 ? "1 bitácora con falla" : ConFallas + " bitácoras con fallas")
                                + (areas.Count > 0 ? " (" + string.Join(", ", areas) + ")" : "")
                                + (Equipos.Length > 0 ? " en " + Equipos : "")
                                + " de " + Bitacoras + (Bitacoras == 1 ? " registrada" : " registradas"));
                        }

                        if (DelProfesor > 0)
                        {
                            partes.Add("reportes de docentes: " + string.Join(", ", ReportesDelProfesor.Take(3))
                                + (ReportesDelProfesor.Count > 3 ? " y " + (ReportesDelProfesor.Count - 3) + " más" : ""));
                        }

                        if (SinInternet.Count > 0)
                        {
                            partes.Add(Consultas.DescribirSinInternet(SinInternet, true));
                        }

                        return string.Join("; ", partes).TrimEnd('.') + ".";
                }
            }
        }
    }

    /// <summary>Las alertas de un periodo, con la hora del servidor de cuando se leyeron.</summary>
    public class ResultadoAlertas
    {
        public DateTime Ahora;
        public List<Alerta> Alertas = new List<Alerta>();

        /// <summary>Las que el administrador aun no ha visto (sin fecha, todas).</summary>
        public int Nuevas(DateTime? vistasHasta)
        {
            return Alertas.Count(a => !vistasHasta.HasValue || a.Momento > vistasHasta.Value);
        }
    }

    /// <summary>
    /// Que alertas avisar con una notificacion de Windows mientras el panel del
    /// administrador esta abierto. Las que ya habia al abrirlo no se avisan (para
    /// esas esta la campana con su numero). Como mucho un aviso por hora, para no
    /// saturar al administrador: el primero sale al momento y lo que aparezca en
    /// esa hora se junta en el siguiente; si mientras tanto las vio o se
    /// resolvieron solas (llegaron las bitacoras), ya no se avisan.
    /// </summary>
    public class AvisosDeAlertas
    {
        public static readonly TimeSpan EntreAvisos = TimeSpan.FromHours(1);

        // Las que ya no hay que avisar: las de la primera lectura y las avisadas.
        // Null hasta la primera lectura.
        private HashSet<string> yaNoAvisar;
        private DateTime? ultimoAviso;

        /// <summary>
        /// Con cada lectura de las alertas: las que hay que avisar ahora, de la
        /// mas reciente a la mas vieja (ninguna si no hay o si aun no pasa la hora
        /// desde el ultimo aviso). Las devueltas quedan como avisadas.
        /// </summary>
        public List<Alerta> QueAvisar(ResultadoAlertas leidas, DateTime? vistasHasta, DateTime ahora)
        {
            List<Alerta> nada = new List<Alerta>();
            if (leidas == null)
            {
                return nada;
            }

            if (yaNoAvisar == null)
            {
                yaNoAvisar = new HashSet<string>(leidas.Alertas.Select(Clave));
                return nada;
            }

            if (ultimoAviso.HasValue && ahora - ultimoAviso.Value < EntreAvisos)
            {
                return nada;
            }

            List<Alerta> nuevas = leidas.Alertas
                .Where(a => !yaNoAvisar.Contains(Clave(a)) && (!vistasHasta.HasValue || a.Momento > vistasHasta.Value))
                .OrderByDescending(a => a.Momento)
                .ToList();
            if (nuevas.Count > 0)
            {
                ultimoAviso = ahora;
                foreach (Alerta alerta in nuevas)
                {
                    yaNoAvisar.Add(Clave(alerta));
                }
            }

            return nuevas;
        }

        /// <summary>
        /// Lo que hace a una alerta otra para el aviso: una clase sin nadie se avisa
        /// una vez (si ya se aviso en curso, no otra al terminar), las fallas de
        /// un laboratorio en un dia, otra vez con cada reporte nuevo, y cada cambio
        /// de horario, una vez.
        /// </summary>
        public static string Clave(Alerta alerta)
        {
            if (alerta.Tipo == TipoAlerta.CambioDeHorario)
            {
                return "cambio|" + alerta.CambioId.ToString(CultureInfo.InvariantCulture);
            }

            // Otra vez cada que alguien mas entra con el SIIA sin estar enlazado.
            if (alerta.Tipo == TipoAlerta.DocentesPorEnlazar)
            {
                return "enlazar|" + alerta.Momento.Ticks.ToString(CultureInfo.InvariantCulture);
            }

            return alerta.Tipo == TipoAlerta.Fallas
                ? alerta.Clave + "|" + alerta.Momento.Ticks.ToString(CultureInfo.InvariantCulture)
                : "clase|" + alerta.LaboratorioId + "|" + alerta.Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                  + "|" + alerta.FranjaId + "|" + alerta.ExcepcionId;
        }

        /// <summary>El titulo del aviso: que paso si es una, cuantas si son varias (Windows corta en 63).</summary>
        public static string Titulo(List<Alerta> nuevas)
        {
            return Recortar(nuevas.Count == 1 ? nuevas[0].Titulo : nuevas.Count + " alertas nuevas", 63);
        }

        /// <summary>
        /// El texto: donde y el detalle si es una; las tres mas recientes si son
        /// varias (Windows corta en 255). Un cambio de horario ya dice donde en su
        /// detalle (y el de una clase no tiene laboratorio).
        /// </summary>
        public static string Texto(List<Alerta> nuevas)
        {
            string texto = nuevas.Count == 1
                ? (nuevas[0].Laboratorio.Length > 0 && nuevas[0].Tipo != TipoAlerta.CambioDeHorario ? nuevas[0].Laboratorio + ". " : "") + nuevas[0].Detalle
                : string.Join("\n", nuevas.Take(3).Select(a => (a.Laboratorio.Length > 0 ? a.Laboratorio + ": " : "") + a.Titulo))
                  + (nuevas.Count > 3 ? "\n... y " + (nuevas.Count - 3) + " más" : "");
            return Recortar(texto, 220) + "\nPulsa aquí para verlas.";
        }

        private static string Recortar(string texto, int maximo)
        {
            return texto.Length <= maximo ? texto : texto.Substring(0, maximo - 3) + "...";
        }
    }

    /// <summary>
    /// Alertas del panel del administrador. No guarda nada: se calculan con lo
    /// que ya hay en la base, asi que si despues llegan bitacoras (por ejemplo, de
    /// la cola sin conexion de un equipo) la alerta desaparece sola.
    ///  - Clases que terminaron sin bitacoras: la regla de la pestana Faltas
    ///    (HorariosDatos.LeerCalendario, con sus correcciones), y solo desde que
    ///    cada laboratorio usa DigBit: uno que aun no lo usa no llena de alertas.
    ///    Una en la que su profesor dio asistencias (db/22) se dio: no es Falta
    ///    (SinBitacorasConAsistencia, en ambar) y no avisa.
    ///  - Clases en curso sin nadie a los MinutosSinRegistro minutos, tambien solo
    ///    donde ya se usa DigBit y con la misma correccion de los que llegaron
    ///    antes con el codigo unico (quedan en la clase anterior del mismo profesor).
    ///    Con asistencias dadas tambien avisa (quiza no hay internet), diciendolo.
    ///  - Fallas reportadas por laboratorio y dia (tambien en el uso libre), con
    ///    los equipos que avisaron sin internet (db/10).
    ///  - Cambios del horario que hizo otra persona (db/21, HistorialCambios):
    ///    franjas, clases, cancelaciones y sesiones extra, en el momento en que
    ///    se hicieron. Estos no se resuelven solos: dejan de ser nuevos al verlos.
    /// Lo que cada usuario del panel ya vio se guarda en configuracion (db/13),
    /// por usuario: "alertas_vistas_" + su numero de identificacion. El encargado
    /// de laboratorio (solo consulta) no escribe en la base: lo suyo va a un
    /// archivo de su equipo con ese mismo nombre (ArchivoVistas).
    /// </summary>
    internal class AlertasDatos
    {
        /// <summary>Lo que se espera desde el inicio de una clase antes de avisar que nadie ha registrado.</summary>
        public const int MinutosSinRegistro = 20;
        private const string PrefijoVistas = "alertas_vistas_";
        private const string FormatoFecha = "yyyy-MM-dd HH:mm:ss";

        private readonly Conexion mconexion = new Conexion();

        /// <summary>
        /// Las alertas de los ultimos dias (con hoy), de la mas reciente a la mas
        /// vieja. Los cambios de horario, sin los que hizo quien tiene la sesion.
        /// </summary>
        public ResultadoAlertas Leer(int dias)
        {
            return Leer(dias, Datos_User.getUser());
        }

        /// <param name="usuario">Quien mira (su numero): sus propios cambios de horario no le salen como alerta.</param>
        public ResultadoAlertas Leer(int dias, string usuario)
        {
            HorariosDatos horarios = new HorariosDatos();
            PanoramaLaboratorios hoy = horarios.PanoramaDelDia();
            DateTime ahora = hoy.AhoraServidor;
            DateTime desde = ahora.Date.AddDays(-(Math.Max(1, dias) - 1));
            Dictionary<int, string> activos = new Dictionary<int, string>();
            foreach (OpcionCombo laboratorio in hoy.Laboratorios)
            {
                activos[laboratorio.Id] = laboratorio.Texto;
            }

            // 1. Las que terminaron sin bitacoras, como en Faltas. Su reloj
            // (calendario.Ahora) es el instante de la lectura: lo que ahi ya termino
            // no sigue en curso en el punto 2, y hasta ahi se da por visto lo que
            // se ensena (VentanaAlertas.Leidas).
            CalendarioBitacoras calendario = horarios.LeerCalendario(desde, ahora.Date, false);
            ResultadoAlertas resultado = new ResultadoAlertas { Ahora = calendario.Ahora };
            foreach (ClaseDelCalendario c in calendario.Clases.Where(c => c.Falta && activos.ContainsKey(c.LaboratorioId)))
            {
                resultado.Alertas.Add(new Alerta
                {
                    Tipo = TipoAlerta.SinBitacoras,
                    Fecha = c.Fecha.Date,
                    Momento = c.Fecha.Date + c.Fin,
                    LaboratorioId = c.LaboratorioId,
                    Laboratorio = activos[c.LaboratorioId],
                    FranjaId = c.FranjaId,
                    ExcepcionId = c.ExcepcionId,
                    EsExtra = c.EsExtra,
                    Materia = c.Materia ?? "",
                    Grupo = c.Grupo ?? "",
                    Profesor = c.Profesor ?? "",
                    Inicio = c.Inicio,
                    Fin = c.Fin
                });
            }

            // Solo donde ya se usa DigBit, la regla de Faltas (EstadoDe): en un
            // laboratorio que aun no lo usa, sus sesiones no estan enlazadas a la
            // clase (nunca tiene alumnos) y avisaria en cada una.
            Dictionary<int, string> conDigBit = new Dictionary<int, string>();
            foreach (KeyValuePair<int, string> laboratorio in activos)
            {
                DateTime empezo;
                if (calendario.EmpezoEl.TryGetValue(laboratorio.Key, out empezo) && ahora.Date >= empezo)
                {
                    conDigBit[laboratorio.Key] = laboratorio.Value;
                }
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                // 2. En curso y sin nadie desde hace un rato.
                foreach (SesionAgenda s in EnCursoSinRegistro(conexion, hoy, calendario.Ahora, conDigBit))
                {
                    resultado.Alertas.Add(new Alerta
                    {
                        Tipo = TipoAlerta.EnCursoSinBitacoras,
                        Fecha = ahora.Date,
                        Momento = ahora.Date + s.Inicio + TimeSpan.FromMinutes(MinutosSinRegistro),
                        LaboratorioId = s.LaboratorioId,
                        Laboratorio = activos[s.LaboratorioId],
                        FranjaId = s.FranjaId,
                        ExcepcionId = s.ExcepcionId,
                        EsExtra = s.EsExtra,
                        Materia = s.Materia ?? "",
                        Grupo = s.Grupo ?? "",
                        Profesor = s.Profesor ?? "",
                        Inicio = s.Inicio,
                        Fin = s.Fin,
                        AsistenciasDadas = s.AsistenciasDadas
                    });
                }

                // 3. Las fallas de cada dia, con las que reportaron los profesores.
                List<Alerta> fallas = LeerFallas(conexion, desde, ahora.Date, activos);
                SumarReportesDelProfesor(conexion, fallas, desde, ahora.Date, activos);
                resultado.Alertas.AddRange(fallas);

                // 4. Los cambios del horario que hizo otra persona (db/21), en el
                // momento en que se hicieron: nuevos si son de despues de lo visto.
                foreach (CambioRegistrado cambio in HistorialCambios.DeHorario(conexion, desde, usuario))
                {
                    resultado.Alertas.Add(new Alerta
                    {
                        Tipo = TipoAlerta.CambioDeHorario,
                        Fecha = cambio.Cuando.Date,
                        Momento = cambio.Cuando,
                        LaboratorioId = cambio.LaboratorioId ?? 0,
                        Laboratorio = cambio.Laboratorio,
                        CambioId = cambio.Id,
                        TituloDelCambio = cambio.Titulo,
                        QueCambio = cambio.Descripcion,
                        Quien = cambio.Quien
                    });
                }

                // 5. Quien entro con el SIIA y espera que lo enlacen (db/24): mientras
                // haya alguno, cualquiera que sea el periodo. Si falla, las demas siguen.
                try
                {
                    Alerta porEnlazar = EnlacesSiia.ParaAlertas(conexion);
                    if (porEnlazar != null)
                    {
                        resultado.Alertas.Add(porEnlazar);
                    }
                }
                catch (MySqlException ex)
                {
                    Log.Error("Alertas: leer los docentes por enlazar con el SIIA", ex);
                }
            }

            resultado.Alertas = resultado.Alertas.OrderByDescending(a => a.Momento).ToList();
            return resultado;
        }

        /// <summary>
        /// Las fallas que reportaron los profesores durante sus clases (db/15), en la
        /// alerta de fallas de su laboratorio y dia (si no la habia, una nueva):
        /// cuentan en el titulo, van en el detalle y, con cada reporte nuevo, la
        /// alerta vuelve a ser nueva (su Momento es el ultimo reporte). Sin la 15, nada.
        /// </summary>
        private static void SumarReportesDelProfesor(MySqlConnection conexion, List<Alerta> fallas, DateTime desde, DateTime hasta,
            Dictionary<int, string> activos)
        {
            if (!Esquema.ReportesDeProfesor(conexion))
            {
                return;
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT ca.laboratorios_idlaboratorios AS lab_id, DATE_FORMAT(COALESCE(ca.fecha, DATE(ca.hora_registro)), '%Y-%m-%d') AS dia,
                       rp.numero_computadora, rp.area, rp.falla, DATE_FORMAT(rp.registrado_en, '%Y-%m-%d %H:%i:%s') AS cuando
                FROM reportes_profesor rp
                INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rp.fk_codigo_accesos
                WHERE COALESCE(ca.fecha, DATE(ca.hora_registro)) BETWEEN @Desde AND @Hasta
                ORDER BY rp.registrado_en, rp.idreportes_profesor", conexion))
            {
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string nombre;
                        if (reader["lab_id"] == DBNull.Value || !activos.TryGetValue(Convert.ToInt32(reader["lab_id"]), out nombre))
                        {
                            continue;
                        }

                        int laboratorio = Convert.ToInt32(reader["lab_id"]);
                        DateTime dia = DateTime.ParseExact(Convert.ToString(reader["dia"]), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                        DateTime cuando;
                        if (!DateTime.TryParseExact(Convert.ToString(reader["cuando"]), FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out cuando))
                        {
                            cuando = dia;
                        }

                        Alerta alerta = fallas.FirstOrDefault(a => a.LaboratorioId == laboratorio && a.Fecha == dia);
                        if (alerta == null)
                        {
                            alerta = new Alerta { Tipo = TipoAlerta.Fallas, Fecha = dia, Momento = cuando, LaboratorioId = laboratorio, Laboratorio = nombre };
                            fallas.Add(alerta);
                        }

                        ReporteProfesor reporte = new ReporteProfesor
                        {
                            Equipo = reader["numero_computadora"] == DBNull.Value ? null : Convert.ToString(reader["numero_computadora"]),
                            Area = Convert.ToString(reader["area"]),
                            Falla = Convert.ToString(reader["falla"])
                        };
                        alerta.DelProfesor++;
                        alerta.ReportesDelProfesor.Add(reporte.Corto);
                        if (cuando > alerta.Momento)
                        {
                            alerta.Momento = cuando;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Las de hoy en curso desde hace MinutosSinRegistro o mas, sin canceladas y
        /// sin nadie registrado. No avisa de una sesion extra que es el resto de una
        /// franja cancelada de la misma clase con alumnos (registraron en esa), ni,
        /// con el codigo unico, de la que sigue a otra clase del mismo profesor en la
        /// que registraron los que llegaron antes (la misma regla que Faltas).
        /// </summary>
        /// <param name="ahora">
        /// El reloj del calendario (Faltas), no hoy.AhoraServidor: lo que ahi ya
        /// termino aqui no sigue en curso, y una clase no sale en curso y sin
        /// bitacoras a la vez.
        /// </param>
        /// <param name="conDigBit">Los laboratorios que ya usan DigBit (de los activos).</param>
        private static List<SesionAgenda> EnCursoSinRegistro(MySqlConnection conexion, PanoramaLaboratorios hoy, DateTime ahora, Dictionary<int, string> conDigBit)
        {
            TimeSpan hora = ahora.TimeOfDay;
            TimeSpan espera = TimeSpan.FromMinutes(MinutosSinRegistro);
            TimeSpan margen = TimeSpan.FromMinutes(HorariosDatos.MargenAntesDelInicioMinutos);
            List<SesionAgenda> agenda = hoy.Agenda;
            List<SesionAgenda> alertas = new List<SesionAgenda>();
            foreach (SesionAgenda s in agenda.Where(s => conDigBit.ContainsKey(s.LaboratorioId) && !s.Cancelada && s.Utilizable
                && s.Inicio + espera <= hora && hora < s.Fin && s.BitacorasDeLaClase == 0))
            {
                if (s.EsExtra && agenda.Any(f => !f.EsExtra && f.Cancelada && f.ClaseId == s.ClaseId && f.LaboratorioId == s.LaboratorioId
                    && f.Inicio <= s.Inicio && f.Fin >= s.Fin && f.Alumnos > 0))
                {
                    continue;
                }

                if (hoy.ModoDe(s.LaboratorioId) == ModoCodigo.Fijo)
                {
                    SesionAgenda previa = agenda
                        .Where(p => p != s && p.LaboratorioId == s.LaboratorioId && p.ProfesorId == s.ProfesorId && !p.Cancelada
                            && p.SesionId.HasValue && p.Alumnos > 0 && p.Inicio < s.Inicio && p.Fin > s.Inicio - margen)
                        .OrderByDescending(p => p.Inicio)
                        .FirstOrDefault();
                    if (previa != null && LlegaronAntes(conexion, previa.SesionId.Value, hoy.AhoraServidor.Date + s.Inicio))
                    {
                        continue;
                    }
                }

                alertas.Add(s);
            }

            return alertas;
        }

        /// <summary>
        /// Si en la sesion de la clase anterior registraron alumnos desde 15 minutos
        /// antes hasta 10 despues del inicio de la siguiente (el tiempo de llenarla),
        /// como en Faltas: con el codigo unico, los que llegaron antes quedan ahi.
        /// </summary>
        private static bool LlegaronAntes(MySqlConnection conexion, int idSesion, DateTime inicio)
        {
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT COUNT(DISTINCT rb.fk_usuario) FROM registros_bitacoras rb
                WHERE rb.fk_codigo_accesos = @Sesion
                  AND rb.registrado_en >= CAST(@Desde AS DATETIME) AND rb.registrado_en < CAST(@Hasta AS DATETIME)", conexion))
            {
                comando.Parameters.AddWithValue("@Sesion", idSesion);
                comando.Parameters.AddWithValue("@Desde", inicio.AddMinutes(-HorariosDatos.MargenAntesDelInicioMinutos).ToString(FormatoFecha, CultureInfo.InvariantCulture));
                comando.Parameters.AddWithValue("@Hasta", inicio.AddMinutes(10).ToString(FormatoFecha, CultureInfo.InvariantCulture));
                return Convert.ToInt32(comando.ExecuteScalar()) > 0;
            }
        }

        /// <summary>
        /// Por laboratorio y dia: cuantas bitacoras reportaron alguna falla (y de
        /// que), desde que equipos, cuantas hubo, que equipos avisaron sin internet
        /// (db/10) y cuando llego el ultimo reporte. Cuenta por sesion primero (como
        /// la lista de Bitacoras: un alumno, una vez por sesion) y luego suma.
        /// </summary>
        private static List<Alerta> LeerFallas(MySqlConnection conexion, DateTime desde, DateTime hasta, Dictionary<int, string> activos)
        {
            bool conAviso = Esquema.EquiposSinInternet(conexion);
            bool conLlegada = Esquema.HoraDeLlegada(conexion);
            using (MySqlCommand largo = new MySqlCommand("SET SESSION group_concat_max_len = 1048576", conexion))
            {
                largo.ExecuteNonQuery();
            }

            string falla = "(" + Consultas.CondicionConFalla + ")";
            string reporta = conAviso ? "(" + falla + " OR rb.equipos_sin_internet <> '')" : falla;
            string cuando = conLlegada ? "COALESCE(rb.recibido_en, rb.registrado_en)" : "rb.registrado_en";
            string sql = @"
                SELECT s.lab_id, DATE_FORMAT(s.dia, '%Y-%m-%d') AS dia, SUM(s.alumnos) AS bitacoras, SUM(s.con_fallas) AS con_fallas,
                       SUM(s.red) AS red, SUM(s.hardware) AS hardware, SUM(s.software) AS software,
                       DATE_FORMAT(MAX(s.ultima), '%Y-%m-%d %H:%i:%s') AS ultima,
                       COALESCE(CAST(GROUP_CONCAT(NULLIF(s.equipos, '') SEPARATOR ', ') AS CHAR), '') AS equipos"
                    + (conAviso ? @",
                       COALESCE(CAST(GROUP_CONCAT(NULLIF(s.sin_internet, '') SEPARATOR '|') AS CHAR), '') AS sin_internet" : "") + @"
                FROM (
                    SELECT ca.idcodigos_accesos, ca.laboratorios_idlaboratorios AS lab_id,
                           COALESCE(ca.fecha, DATE(ca.hora_registro)) AS dia,
                           COUNT(DISTINCT rb.fk_usuario) AS alumnos,
                           COUNT(DISTINCT CASE WHEN " + falla + @" THEN rb.fk_usuario END) AS con_fallas,
                           COUNT(DISTINCT CASE WHEN " + Consultas.CondicionFallaEn("red") + @" THEN rb.fk_usuario END) AS red,
                           COUNT(DISTINCT CASE WHEN " + Consultas.CondicionFallaEn("hardware") + @" THEN rb.fk_usuario END) AS hardware,
                           COUNT(DISTINCT CASE WHEN " + Consultas.CondicionFallaEn("software") + @" THEN rb.fk_usuario END) AS software,
                           MAX(CASE WHEN " + reporta + " THEN " + cuando + @" END) AS ultima,
                           CAST(GROUP_CONCAT(DISTINCT CASE WHEN " + falla + @" THEN rb.numero_computadora END ORDER BY rb.numero_computadora SEPARATOR ', ') AS CHAR) AS equipos"
                    + (conAviso ? @",
                           CAST(GROUP_CONCAT(NULLIF(rb.equipos_sin_internet, '') SEPARATOR '|') AS CHAR) AS sin_internet" : "") + @"
                    FROM codigos_accesos ca
                    INNER JOIN registros_bitacoras rb ON rb.fk_codigo_accesos = ca.idcodigos_accesos
                    WHERE COALESCE(ca.fecha, DATE(ca.hora_registro)) BETWEEN @Desde AND @Hasta
                    GROUP BY ca.idcodigos_accesos, ca.laboratorios_idlaboratorios, ca.fecha, ca.hora_registro
                ) s
                GROUP BY s.lab_id, s.dia
                HAVING SUM(s.con_fallas) > 0" + (conAviso ? " OR MAX(COALESCE(s.sin_internet, '')) <> ''" : "");

            List<Alerta> alertas = new List<Alerta>();
            using (MySqlCommand comando = new MySqlCommand(sql, conexion))
            {
                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader["lab_id"] == DBNull.Value)
                        {
                            continue;
                        }

                        int laboratorio = Convert.ToInt32(reader["lab_id"]);
                        string nombre;
                        if (!activos.TryGetValue(laboratorio, out nombre))
                        {
                            continue;
                        }

                        DateTime dia = DateTime.ParseExact(Convert.ToString(reader["dia"]), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                        DateTime ultima;
                        string textoUltima = reader["ultima"] == DBNull.Value ? null : Convert.ToString(reader["ultima"]);
                        if (textoUltima == null || !DateTime.TryParseExact(textoUltima, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out ultima))
                        {
                            ultima = dia;
                        }

                        alertas.Add(new Alerta
                        {
                            Tipo = TipoAlerta.Fallas,
                            Fecha = dia,
                            Momento = ultima,
                            LaboratorioId = laboratorio,
                            Laboratorio = nombre,
                            Bitacoras = Convert.ToInt32(reader["bitacoras"]),
                            ConFallas = Convert.ToInt32(reader["con_fallas"]),
                            Red = Convert.ToInt32(reader["red"]),
                            Hardware = Convert.ToInt32(reader["hardware"]),
                            Software = Convert.ToInt32(reader["software"]),
                            // Un equipo puede salir en varias sesiones del dia: una vez.
                            Equipos = string.Join(", ", Convert.ToString(reader["equipos"])
                                .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)),
                            SinInternet = conAviso ? Consultas.ContarSinInternet(new[] { Convert.ToString(reader["sin_internet"]) }) : new List<EquipoSinInternet>()
                        });
                    }
                }
            }

            return alertas;
        }

        private static string ClaveVistas(string usuario)
        {
            string clave = PrefijoVistas + (usuario ?? "");
            return clave.Length <= 64 ? clave : clave.Substring(0, 64);
        }

        /// <summary>
        /// Solo consulta: el archivo de este equipo donde queda hasta cuando vio
        /// las alertas ese usuario, "alertas_vistas_" + su numero + ".txt" en la
        /// carpeta de datos (CarpetaDatos: con Deep Freeze, la que se configuro
        /// para sobrevivir al reinicio). Si entra desde otro equipo, ahi empieza
        /// con todas como nuevas.
        /// </summary>
        private static string ArchivoVistas(string usuario)
        {
            string perfil = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigBit");
            string nombre = ClaveVistas(usuario);
            foreach (char invalido in Path.GetInvalidFileNameChars())
            {
                nombre = nombre.Replace(invalido, '_');
            }

            return Path.Combine(CarpetaDatos.Resolver(perfil), nombre + ".txt");
        }

        /// <summary>
        /// Hasta cuando vio las alertas ese usuario (hora del servidor), o null. En
        /// solo consulta (enElEquipo: lo decide quien llama, en el hilo de la
        /// pantalla y con la sesion de ese momento), del archivo de este equipo
        /// (ArchivoVistas); si este equipo aun no tiene nada, el inicio del dia de
        /// ahoraServidor (si se da): la responsable entra desde cualquier PC y en
        /// cada una nueva la campana decia "66" (todas las de los ultimos dias, como
        /// nuevas). Asi solo cuentan las de hoy. Solo la usa la responsable: la
        /// administracion lo guarda en la base.
        /// </summary>
        public DateTime? LeerVistas(string usuario, bool enElEquipo, DateTime? ahoraServidor = null)
        {
            if (enElEquipo)
            {
                return LeerVistasDelEquipo(usuario) ?? (ahoraServidor.HasValue ? ahoraServidor.Value.Date : (DateTime?)null);
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.Ajustes(conexion))
                {
                    return null;
                }

                using (MySqlCommand comando = new MySqlCommand("SELECT valor FROM configuracion WHERE clave = @Clave", conexion))
                {
                    comando.Parameters.AddWithValue("@Clave", ClaveVistas(usuario));
                    object valor = comando.ExecuteScalar();
                    DateTime vistas;
                    return valor != null && valor != DBNull.Value
                        && DateTime.TryParseExact(Convert.ToString(valor), FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out vistas)
                        ? vistas : (DateTime?)null;
                }
            }
        }

        /// <summary>
        /// Guarda hasta cuando las vio. False si no se pudo (sin db/13 o sin
        /// permiso): entonces solo vale en esta sesion. No va al log: pasa cada vez
        /// que abre las alertas. En solo consulta no toca la base: va al archivo
        /// de este equipo (ArchivoVistas). enElEquipo lo decide quien llama, antes de
        /// pasar a segundo plano: si la sesion se cierra entretanto, lo del encargado
        /// sigue yendo al archivo y nunca a la base con la cuenta del equipo.
        /// </summary>
        public bool GuardarVistas(string usuario, DateTime hasta, bool enElEquipo)
        {
            if (enElEquipo)
            {
                return GuardarVistasEnElEquipo(usuario, hasta);
            }

            try
            {
                using (MySqlConnection conexion = mconexion.GetConexion())
                {
                    if (!Esquema.Ajustes(conexion))
                    {
                        return false;
                    }

                    using (MySqlCommand comando = new MySqlCommand(
                        "INSERT INTO configuracion (clave, valor) VALUES (@Clave, @Valor) ON DUPLICATE KEY UPDATE valor = VALUES(valor)", conexion))
                    {
                        comando.Parameters.AddWithValue("@Clave", ClaveVistas(usuario));
                        comando.Parameters.AddWithValue("@Valor", hasta.ToString(FormatoFecha, CultureInfo.InvariantCulture));
                        comando.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch (MySqlException ex) when (ex.Number == 1142 || ex.Number == 1143 || ex.Number == 1146)
            {
                Log.Aviso("No se pudo guardar hasta cuando se vieron las alertas: " + ex.Message);
                return false;
            }
        }

        /// <summary>Lo que dice el archivo de este equipo, o null si aun no hay (o no se puede leer).</summary>
        private static DateTime? LeerVistasDelEquipo(string usuario)
        {
            try
            {
                string archivo = ArchivoVistas(usuario);
                if (!File.Exists(archivo))
                {
                    return null;
                }

                DateTime vistas;
                return DateTime.TryParseExact(File.ReadAllText(archivo).Trim(), FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out vistas)
                    ? vistas : (DateTime?)null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Lo escribe en el archivo de este equipo. False si no se pudo: entonces solo vale en esta sesion.</summary>
        private static bool GuardarVistasEnElEquipo(string usuario, DateTime hasta)
        {
            try
            {
                string archivo = ArchivoVistas(usuario);
                Directory.CreateDirectory(Path.GetDirectoryName(archivo));
                File.WriteAllText(archivo, hasta.ToString(FormatoFecha, CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Aviso("No se pudo guardar en este equipo hasta cuando se vieron las alertas: " + ex.Message);
                return false;
            }
        }
    }
}
