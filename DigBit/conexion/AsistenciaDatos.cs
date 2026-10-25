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
        /// <summary>
        /// Los laboratorios de ese dia: los de las sesiones de la clase (las reales y
        /// las de sus bloques sin sesion) y los de las sesiones de sus asistencias
        /// dadas. Un dia con bloques en dos laboratorios es de los dos. Para el filtro
        /// de laboratorio de Estadisticas (Mis grupos no lo usa). Vacio en el dia de
        /// hoy que aun no tiene ninguna sesion: ahi todos estan pendientes y no cuenta.
        /// </summary>
        public HashSet<int> Laboratorios = new HashSet<int>();
    }

    /// <summary>Un alumno de la lista de la clase y los dias en que registro.</summary>
    public class AlumnoDeAsistencia
    {
        public int Id;
        public string Matricula = "";
        public string Nombre = "";
        /// <summary>
        /// Los dias (DiaDeAsistencia.Fecha) en que tiene asistencia en esta clase:
        /// registro su bitacora y le cuenta, o se la dio su profesor (db/22).
        /// </summary>
        public HashSet<DateTime> Presente = new HashSet<DateTime>();
        /// <summary>Los dias en que registro su bitacora pero el profesor le quito la asistencia: no estaba en el salon (db/17).</summary>
        public HashSet<DateTime> Retiradas = new HashSet<DateTime>();
        /// <summary>Sus bitacoras de cada dia que cuentan para esta clase: las que se quitan o se devuelven juntas.</summary>
        public Dictionary<DateTime, List<int>> BitacorasDelDia = new Dictionary<DateTime, List<int>>();
        /// <summary>La asistencia que le dio su profesor cada dia (db/22), solo las que cuentan (no anuladas).</summary>
        public Dictionary<DateTime, AsistenciaDada> Dadas = new Dictionary<DateTime, AsistenciaDada>();
        /// <summary>Los dias que solo cuentan por una asistencia dada (sin bitacora que cuente).</summary>
        public HashSet<DateTime> SinBitacora = new HashSet<DateTime>();

        /// <summary>Los dias en que esta en la lista: con bitacora (tambien quitada) o con asistencia dada.</summary>
        public IEnumerable<DateTime> DiasEnLista
        {
            get { return BitacorasDelDia.Keys.Union(Dadas.Keys); }
        }

        /// <summary>El primer dia en que entro a la lista (DateTime.MaxValue si ninguno).</summary>
        public DateTime Entro
        {
            get { return DiasEnLista.DefaultIfEmpty(DateTime.MaxValue).Min(); }
        }
    }

    /// <summary>
    /// Una asistencia que dio el profesor a quien no pudo registrar su bitacora
    /// (db/22, asistencias_dadas). Las horas y el laboratorio son los de su sesion.
    /// AsistenciaDatos.LeerDadas llena lo de arriba; AsistenciasDadas.DeLaSesion y
    /// DelEstudiante, tambien lo de abajo.
    /// </summary>
    public class AsistenciaDada
    {
        public int Id;
        public int ClaseId;
        public int AlumnoId;
        public int ProfesorId;
        public int SesionId;
        public int LaboratorioId;
        /// <summary>El dia (de la lista) en que cuenta.</summary>
        public DateTime Fecha;
        /// <summary>Cuando se dio (reloj del servidor).</summary>
        public DateTime DadaEn;
        /// <summary>sin_internet, equipo, sin_equipo u otro (AsistenciasDadas.Motivos).</summary>
        public string Motivo = "";
        /// <summary>El equipo que no le sirvio, o null.</summary>
        public string Equipo;
        public string Comentario;
        /// <summary>Entrada y salida de su sesion.</summary>
        public TimeSpan Inicio;
        public TimeSpan Fin;

        // Solo AsistenciasDadas.DeLaSesion y DelEstudiante.
        public string DadaDesde;
        public bool Activa = true;
        public DateTime? AnuladaEn;
        public int? AnuladaPor;
        public string Matricula = "";
        /// <summary>"Apellidos Nombre" del alumno.</summary>
        public string Nombre = "";

        // Solo AsistenciasDadas.DelEstudiante: los de su sesion.
        public string Laboratorio = "";
        public string Materia = "";
        public string Grupo = "";
        public string Docente = "";
    }

    /// <summary>Lo que necesita el dialogo de dar asistencia (AsistenciaDatos.PrepararDar).</summary>
    public class PreparacionDar
    {
        /// <summary>Por que no se puede dar ahora (sin db/22, la clase no esta en curso), o null.</summary>
        public string Problema;
        /// <summary>El bloque en curso (reloj del servidor).</summary>
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public int LaboratorioId;
        public string Laboratorio = "";
        /// <summary>Los equipos del laboratorio (Consultas.EquiposDelLaboratorio); vacia si no se pudo leer.</summary>
        public List<string> Equipos = new List<string>();
        /// <summary>"En esta clase ya avisaron. Sin internet: equipo 12 (...)." o "" (con db/10).</summary>
        public string Pista = "";
    }

    /// <summary>Una cuenta buscada por su matricula exacta para darle la asistencia (AsistenciaDatos.BuscarEstudiante).</summary>
    public class EstudianteBuscado
    {
        public int Id;
        public string Matricula = "";
        /// <summary>"Apellidos Nombre".</summary>
        public string Nombre = "";
        /// <summary>usuarios.fk_tipo_usuario (1 = alumno).</summary>
        public int Tipo;
        /// <summary>No esta dado de baja (sin db/08, siempre true).</summary>
        public bool Activo;
        /// <summary>Bitacoras suyas de hoy en sesiones de esta clase (tambien las quitadas).</summary>
        public int BitacorasHoy;
        /// <summary>Hoy ya tiene una asistencia dada que cuenta en esta clase.</summary>
        public bool DadaHoy;

        public bool EsEstudiante
        {
            get { return Tipo == TiposUsuario.Alumno; }
        }
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
        /// <summary>La base guarda las asistencias que da el profesor (db/22): cuentan, y mientras dura la clase se pueden dar.</summary>
        public bool ConDadas;
        /// <summary>Los dias que cuentan, del primero al ultimo.</summary>
        public List<DiaDeAsistencia> Dias = new List<DiaDeAsistencia>();
        /// <summary>La lista: todos los alumnos que han registrado en alguno (o a quienes se les dio la asistencia), por apellido.</summary>
        public List<AlumnoDeAsistencia> Alumnos = new List<AlumnoDeAsistencia>();
    }

    /// <summary>Una clase leida: la de AsistenciaDatos.DeLaClase, o una de E1 en AsistenciaDatos.LeerEnLote.</summary>
    internal sealed class ClaseLeida
    {
        public int Id;
        public int ProfesorId;
        public int MateriaId;
        public int GrupoId;
        public DateTime VigenteDesde;
        public DateTime VigenteHasta;
        public string Materia = "";
        public string Grupo = "";
        /// <summary>"Nombre Apellidos" del docente (solo LeerEnLote; vacio en DeLaClase).</summary>
        public string Docente = "";
    }

    /// <summary>
    /// Una sesion (codigos_accesos): en LeerEnLote todas las del rango (E2), tambien
    /// las de uso libre y las de antes de los horarios; en DeLaClase, las del
    /// profesor con sus dos horas.
    /// </summary>
    internal sealed class SesionLeida
    {
        public int Id;
        /// <summary>El dia de la sesion (VigenciaFranjas.DiaDeSesion: su fecha o, sin ella, el dia en que se creo).</summary>
        public DateTime Fecha;
        /// <summary>TIME(hora_entrada) y TIME(hora_salida); null si no hay (la asistencia solo usa las que tienen las dos).</summary>
        public TimeSpan? Inicio;
        public TimeSpan? Fin;
        public int? LaboratorioId;
        public int? MateriaId;
        public int? GrupoId;
        public int? DocenteId;
        /// <summary>De uso libre (db/14, HorariosDatos.CondicionUsoLibre): sin profesor, franja ni extra.</summary>
        public bool UsoLibre;
    }

    /// <summary>Una franja de una clase activa (E3), con la vigencia de su clase y la suya propia (db/20; null sin ella).</summary>
    internal sealed class FranjaLeida
    {
        public int Id;
        public int ClaseId;
        public int DocenteId;
        public int MateriaId;
        public int GrupoId;
        public int LaboratorioId;
        /// <summary>1 = lunes ... 7 = domingo.</summary>
        public int DiaSemana;
        public TimeSpan Inicio;
        public TimeSpan Fin;
        public DateTime ClaseDesde;
        public DateTime ClaseHasta;
        public DateTime? FranjaDesde;
        public DateTime? FranjaHasta;
    }

    /// <summary>Una excepcion del horario (E4): una franja cancelada un dia, o una sesion extra.</summary>
    internal sealed class ExcepcionLeida
    {
        public int Id;
        /// <summary>"cancelada" o "extra".</summary>
        public string Tipo = "";
        /// <summary>La franja (en una cancelada); null en una extra.</summary>
        public int? FranjaId;
        public DateTime Fecha;
        public int? LaboratorioId;
        public TimeSpan? Inicio;
        public TimeSpan? Fin;
        /// <summary>La de la extra o la de la franja cancelada, con su profesor, materia y grupo.</summary>
        public int ClaseId;
        public int DocenteId;
        public int MateriaId;
        public int GrupoId;

        public bool EsCancelada
        {
            get { return Tipo == "cancelada"; }
        }
    }

    /// <summary>Una bitacora: una cuenta que registro en una sesion (E6 en LeerEnLote).</summary>
    internal sealed class BitacoraLeida
    {
        // Las banderas de E6 (se suman).
        public const int BanderaConFalla = 1;
        public const int BanderaRed = 2;
        public const int BanderaHardware = 4;
        public const int BanderaSoftware = 8;
        public const int BanderaEquipoPropio = 16;
        public const int BanderaSinConexion = 32;
        public const int BanderaRetirada = 64;

        public int Id;
        public int SesionId;
        /// <summary>registros_bitacoras.fk_usuario (puede no ser una cuenta de estudiante).</summary>
        public int AlumnoId;
        /// <summary>Null antes del 2026-09-29; estimado en lo guardado sin conexion.</summary>
        public DateTime? RegistradoEn;
        /// <summary>Las Bandera* que tiene. En DeLaClase solo BanderaRetirada.</summary>
        public int Banderas;
        /// <summary>El equipo tal como se escribio, sin espacios a los lados ("" si no hay). Solo en LeerEnLote.</summary>
        public string Equipo = "";

        /// <summary>El profesor le quito la asistencia (db/17): sigue siendo bitacora, no asistencia.</summary>
        public bool Retirada
        {
            get { return (Banderas & BanderaRetirada) != 0; }
        }
    }

    /// <summary>Una cuenta de estudiante (tipo 1) con bitacoras o asistencias dadas en lo leido (E7).</summary>
    internal sealed class EstudianteLeido
    {
        public int Id;
        public string Matricula = "";
        /// <summary>"Apellidos Nombre" (CONCAT_WS, como Mis grupos).</summary>
        public string Nombre = "";
        /// <summary>No esta dado de baja (sin db/08, siempre true). Solo LeerEnLote.</summary>
        public bool Activo = true;
        /// <summary>El grupo guardado en su cuenta (la fila de carrera_grupo_semestre con grupo de mayor id), o null. Solo LeerEnLote.</summary>
        public int? GrupoId;
        public string Grupo = "";
    }

    /// <summary>
    /// Lo leido de la base para contar la asistencia (AsistenciaDatos.Calcular): lo
    /// de UNA clase (DeLaClase) o lo de TODAS (LeerEnLote, para Estadisticas). Calcular
    /// no consulta nada: toma de aqui lo de cada clase.
    /// </summary>
    internal sealed class LecturaDeAsistencia
    {
        public DateTime AhoraServidor;
        /// <summary>El reloj de este equipo en el instante en que se leyo AhoraServidor.</summary>
        public DateTime LeidoEnRelojLocal;
        /// <summary>Lo leido va de DesdeLectura a HastaLectura (en el lote, hasta hoy).</summary>
        public DateTime DesdeLectura;
        public DateTime HastaLectura;
        /// <summary>Esquema.AsistenciaRetirable (db/17).</summary>
        public bool ConRetiro;
        /// <summary>Esquema.AsistenciaDada (db/22).</summary>
        public bool ConDadas;
        /// <summary>E1 (o la clase de DeLaClase).</summary>
        public List<ClaseLeida> Clases = new List<ClaseLeida>();
        /// <summary>E2 por profesor (0 = sin profesor: uso libre), en orden (dia, entrada, id).</summary>
        public Dictionary<int, List<SesionLeida>> SesionesPorDocente = new Dictionary<int, List<SesionLeida>>();
        /// <summary>Las mismas sesiones, por id.</summary>
        public Dictionary<int, SesionLeida> SesionPorId = new Dictionary<int, SesionLeida>();
        /// <summary>E3 por profesor.</summary>
        public Dictionary<int, List<FranjaLeida>> FranjasPorDocente = new Dictionary<int, List<FranjaLeida>>();
        /// <summary>E4 por profesor.</summary>
        public Dictionary<int, List<ExcepcionLeida>> ExcepcionesPorDocente = new Dictionary<int, List<ExcepcionLeida>>();
        /// <summary>ModosDeAcceso.LeerTodos.</summary>
        public Dictionary<int, ModoCodigo> Modos = new Dictionary<int, ModoCodigo>();
        /// <summary>E6 por sesion.</summary>
        public Dictionary<int, List<BitacoraLeida>> BitacorasPorSesion = new Dictionary<int, List<BitacoraLeida>>();
        /// <summary>El grupo guardado de cada alumno (E7), para los que llegan antes a una clase seguida.</summary>
        public Dictionary<int, int> Grupos = new Dictionary<int, int>();
        /// <summary>E7: solo las cuentas de estudiante; quien no esta aqui sale de las listas.</summary>
        public Dictionary<int, EstudianteLeido> Estudiantes = new Dictionary<int, EstudianteLeido>();
        /// <summary>E11 (db/22) por clase: las que cuentan (no anuladas). Vacio sin db/22.</summary>
        public Dictionary<int, List<AsistenciaDada>> DadasPorClase = new Dictionary<int, List<AsistenciaDada>>();
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
    ///
    /// Con db/22, el profesor tambien da la asistencia, durante su clase, a quien
    /// vino pero no pudo registrar su bitacora (AplicarDadas): es asistencia de
    /// ese dia, hace contar el dia para toda la lista y quien solo tiene dadas
    /// entra a la lista; nunca es bitacora y no pasa por el reparto de clases
    /// seguidas. Se lee por clase y dia: aunque Horarios mueva su sesion, no se
    /// va a otra lista.
    ///
    /// La lectura y el calculo van separados: DeLaClase lee lo de una clase y
    /// LeerEnLote lo de todas (Estadisticas); las dos cuentan con el mismo
    /// Calcular, que no consulta nada.
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

        /// <summary>
        /// La asistencia de una clase. Lee lo mismo que antes de que la lectura y el
        /// calculo se separaran (las mismas consultas, en el mismo orden) y lo cuenta
        /// con Calcular, el mismo calculo que el lote de Estadisticas (LeerEnLote y
        /// CalcularTodas).
        /// </summary>
        /// <param name="idClase">clases.idclases.</param>
        /// <param name="hoyInicio">Si la clase es hoy (la de la tarjeta): su inicio y su fin. Su columna cuenta mientras dura, aunque aun nadie registre.</param>
        public AsistenciaDeClase DeLaClase(int idClase, TimeSpan? hoyInicio, TimeSpan? hoyFin)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                LecturaDeAsistencia lectura = new LecturaDeAsistencia();
                using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
                {
                    lectura.AhoraServidor = Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
                    lectura.LeidoEnRelojLocal = DateTime.Now;
                }

                lectura.ConRetiro = Esquema.AsistenciaRetirable(conexion);
                lectura.ConDadas = Esquema.AsistenciaDada(conexion);

                // Por su id, aunque ya no este activa (Mis grupos ensena tambien las terminadas).
                ClaseLeida clase;
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

                        clase = new ClaseLeida
                        {
                            Id = idClase,
                            ProfesorId = Convert.ToInt32(reader[0]),
                            MateriaId = Convert.ToInt32(reader[1]),
                            GrupoId = Convert.ToInt32(reader[2]),
                            VigenteDesde = Convert.ToDateTime(reader[3], CultureInfo.InvariantCulture).Date,
                            VigenteHasta = Convert.ToDateTime(reader[4], CultureInfo.InvariantCulture).Date,
                            Materia = reader.GetString(5),
                            Grupo = reader.GetString(6)
                        };
                    }
                }

                lectura.Clases.Add(clase);
                int profesor = clase.ProfesorId;
                DateTime hastaHoy = HastaHoy(clase, lectura);
                lectura.DesdeLectura = clase.VigenteDesde;
                lectura.HastaLectura = hastaHoy;

                // Las del profesor en esos dias (entra por su indice): las de la clase
                // y, para los que llegan antes, las de sus otras clases el mismo dia.
                List<SesionLeida> sesiones = new List<SesionLeida>();
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
                    comando.Parameters.AddWithValue("@Desde", clase.VigenteDesde);
                    comando.Parameters.AddWithValue("@Hasta", hastaHoy);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
                            {
                                continue;
                            }

                            sesiones.Add(new SesionLeida
                            {
                                Id = reader.GetInt32(0),
                                Fecha = Convert.ToDateTime(reader[1], CultureInfo.InvariantCulture).Date,
                                Inicio = reader.GetTimeSpan(2),
                                Fin = reader.GetTimeSpan(3),
                                LaboratorioId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                                MateriaId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                                GrupoId = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                                DocenteId = profesor
                            });
                        }
                    }
                }

                Guardar(lectura, sesiones);

                // Sus clases del horario de esos dias (para las que no tienen sesion):
                // solo si tuvo alguna sesion, como siempre.
                if (sesiones.Count > 0)
                {
                    lectura.FranjasPorDocente[profesor] = LeerFranjas(conexion, profesor);
                    lectura.ExcepcionesPorDocente[profesor] = LeerExcepciones(conexion, profesor, clase.VigenteDesde, hastaHoy);
                }

                lectura.Modos = ModosDeAcceso.LeerTodos(conexion);

                // Solo las bitacoras que va a mirar Calcular: las de las sesiones de la
                // clase y las de sus clases seguidas. Con ellas, el grupo de cada uno.
                List<BitacoraLeida> bitacoras = LeerBitacoras(conexion, SesionesConBitacoras(clase, lectura), lectura.ConRetiro);
                foreach (BitacoraLeida b in bitacoras)
                {
                    Agregar(lectura.BitacorasPorSesion, b.SesionId, b);
                }

                List<int> registraron = bitacoras.Select(b => b.AlumnoId).Distinct().ToList();
                lectura.Grupos = LeerGrupos(conexion, registraron);

                // Las que dio el profesor (db/22), de la clase y de su periodo.
                List<AsistenciaDada> dadas = new List<AsistenciaDada>();
                if (lectura.ConDadas)
                {
                    dadas = LeerDadas(conexion, idClase, clase.VigenteDesde, hastaHoy);
                    lectura.DadasPorClase[idClase] = dadas;
                }

                // Solo alumnos (no la cuenta de un profesor o del administrador que probo).
                lectura.Estudiantes = LeerNombres(conexion, registraron.Concat(dadas.Select(d => d.AlumnoId)).Distinct().ToList());
                return Calcular(clase, lectura, hoyInicio, hoyFin);
            }
        }

        /// <summary>
        /// La asistencia de una clase con lo ya leido, sin ninguna consulta. Con la
        /// lectura de su DeLaClase da lo mismo que DeLaClase; con la del lote
        /// (LeerEnLote), lo mismo que daria DeLaClase de esa clase leida en ese
        /// momento. Toma de la lectura lo que DeLaClase lee: las sesiones del
        /// profesor en [vigente_desde, min(vigente_hasta, hoy)], sus franjas activas y
        /// sus excepciones de esos dias (para sus bloques sin sesion), las bitacoras
        /// de las sesiones de la clase y de sus clases seguidas, y las asistencias
        /// dadas de la clase en esos dias.
        /// </summary>
        /// <param name="hoyInicio">Si la clase es hoy: el inicio de su primer bloque y el fin del ultimo (como en Mis grupos), o null.</param>
        internal static AsistenciaDeClase Calcular(ClaseLeida clase, LecturaDeAsistencia lectura, TimeSpan? hoyInicio, TimeSpan? hoyFin)
        {
            AsistenciaDeClase resultado = new AsistenciaDeClase
            {
                ClaseId = clase.Id,
                AhoraServidor = lectura.AhoraServidor,
                LeidoEnRelojLocal = lectura.LeidoEnRelojLocal,
                ConRetiro = lectura.ConRetiro,
                ConDadas = lectura.ConDadas,
                Materia = clase.Materia,
                Grupo = clase.Grupo,
                VigenteDesde = clase.VigenteDesde
            };

            DateTime hoy = lectura.AhoraServidor.Date;
            DateTime hasta = clase.VigenteHasta;
            DateTime hastaHoy = HastaHoy(clase, lectura);

            // Las sesiones del profesor en esos dias, y las clases del horario de esos
            // dias que no tienen sesion (todos sus alumnos entraron antes, en la de la
            // clase anterior): sin ellas no se sabria a donde llevar a los que llegaron antes.
            List<Sesion> sesiones = SesionesDe(clase, lectura, hastaHoy);

            // Las clases seguidas en el mismo laboratorio con codigo fijo, una de
            // ellas de esta clase: (anterior, siguiente).
            List<KeyValuePair<Sesion, Sesion>> seguidas = Seguidas(sesiones, lectura.Modos);

            // Siempre en el mismo orden (el de su indice unico, sesion y alumno): asi
            // DeLaClase y el lote dan lo mismo, tambien en el orden de las listas.
            List<BitacoraLeida> bitacoras = new List<BitacoraLeida>();
            foreach (int id in ConBitacoras(sesiones, seguidas))
            {
                List<BitacoraLeida> deLaSesion;
                if (lectura.BitacorasPorSesion.TryGetValue(id, out deLaSesion))
                {
                    bitacoras.AddRange(deLaSesion);
                }
            }

            bitacoras = bitacoras.OrderBy(b => b.SesionId).ThenBy(b => b.AlumnoId).ThenBy(b => b.Id).ToList();
            Dictionary<int, int> gruposDeAlumnos = lectura.Grupos;
            Dictionary<int, Sesion> porId = sesiones.ToDictionary(s => s.Id);

            // Si una bitacora de la anterior cae en la ventana de la siguiente (el par,
            // o uno vacio): una vez por bitacora y solo entre los pares de su sesion.
            // Con un semestre lleno, buscar en todos los pares de la clase por cada
            // bitacora era casi todo lo que tardaba el lote.
            Dictionary<int, List<KeyValuePair<Sesion, Sesion>>> paresDeLaAnterior = new Dictionary<int, List<KeyValuePair<Sesion, Sesion>>>();
            foreach (KeyValuePair<Sesion, Sesion> p in seguidas)
            {
                Agregar(paresDeLaAnterior, p.Key.Id, p);
            }

            List<KeyValuePair<Sesion, Sesion>> ventanas = bitacoras.Select(b => VentanaDe(b, paresDeLaAnterior)).ToList();
            HashSet<int> enLaClase = new HashSet<int>();
            HashSet<int> enOtra = new HashSet<int>();
            for (int i = 0; i < bitacoras.Count; i++)
            {
                if (ventanas[i].Key == null)
                {
                    (porId[bitacoras[i].SesionId].DeLaClase ? enLaClase : enOtra).Add(bitacoras[i].AlumnoId);
                }
            }

            // Cada bitacora en el dia de la clase a la que pertenece.
            Dictionary<int, AlumnoDeAsistencia> alumnos = new Dictionary<int, AlumnoDeAsistencia>();
            for (int i = 0; i < bitacoras.Count; i++)
            {
                BitacoraLeida b = bitacoras[i];
                Sesion donde = porId[b.SesionId];
                KeyValuePair<Sesion, Sesion> par = ventanas[i];
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

            // Las que dio el profesor (db/22): por clase y dia, sin reparto. Solo las de
            // su periodo (el lote las lee de todas las clases y de todo lo leido).
            List<AsistenciaDada> dadasDeLaClase;
            if (resultado.ConDadas && lectura.DadasPorClase.TryGetValue(clase.Id, out dadasDeLaClase))
            {
                AplicarDadas(alumnos, dadasDeLaClase
                    .Where(d => d.Fecha.Date >= clase.VigenteDesde && d.Fecha.Date <= hastaHoy)
                    .OrderBy(d => d.Fecha).ThenBy(d => d.AlumnoId).ThenBy(d => d.Id));
            }

            // Si ese dia tiene otra bitacora que cuenta (o una asistencia dada), ese dia asistio.
            foreach (AlumnoDeAsistencia alumno in alumnos.Values)
            {
                alumno.Retiradas.ExceptWith(alumno.Presente);
            }

            // Solo alumnos (no la cuenta de un profesor o del administrador que probo).
            foreach (int id in alumnos.Keys.ToList())
            {
                EstudianteLeido estudiante;
                if (lectura.Estudiantes.TryGetValue(id, out estudiante))
                {
                    alumnos[id].Matricula = estudiante.Matricula;
                    alumnos[id].Nombre = estudiante.Nombre;
                }
                else
                {
                    alumnos.Remove(id);
                }
            }

            // Cuentan los dias con alguien (tambien los de una clase sin sesion a la
            // que todos llegaron antes, aunque a quien registro se le quitara la
            // asistencia, y los que solo tienen asistencias dadas) y hoy mientras
            // dura la clase.
            HashSet<DateTime> conAlguien = new HashSet<DateTime>(alumnos.Values.SelectMany(a => a.DiasEnLista));
            TimeSpan horaAhora = resultado.AhoraServidor.TimeOfDay;
            foreach (IGrouping<DateTime, Sesion> dia in sesiones.Where(s => s.DeLaClase).GroupBy(s => s.Fecha))
            {
                bool enCurso = dia.Key == hoy && dia.Any(s => s.Id > 0 && horaAhora < s.Fin);
                if (conAlguien.Contains(dia.Key) || enCurso)
                {
                    DiaDeAsistencia nuevo = new DiaDeAsistencia { Fecha = dia.Key, Inicio = dia.Min(s => s.Inicio), EnCurso = enCurso };
                    nuevo.Laboratorios.UnionWith(dia.Select(s => s.LaboratorioId).Where(l => l > 0));
                    resultado.Dias.Add(nuevo);
                }
            }

            // Una asistencia dada nunca se pierde: si su dia ya no tiene ninguna
            // sesion de la clase (la movio un DigBit anterior a la 22), el dia
            // cuenta igual, con la hora de su sesion.
            List<AsistenciaDada> dadas = alumnos.Values.SelectMany(a => a.Dadas.Values).ToList();
            resultado.Dias.AddRange(DiasSoloDeDadas(dadas, new HashSet<DateTime>(sesiones.Where(s => s.DeLaClase).Select(s => s.Fecha))));

            // El laboratorio de la sesion de cada asistencia dada tambien es de su dia.
            foreach (AsistenciaDada dada in dadas.Where(d => d.LaboratorioId > 0))
            {
                DiaDeAsistencia suDia = resultado.Dias.FirstOrDefault(d => d.Fecha == dada.Fecha.Date);
                if (suDia != null)
                {
                    suDia.Laboratorios.Add(dada.LaboratorioId);
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
            return resultado;
        }

        /// <summary>Hasta cuando se cuenta la clase: su ultimo dia, o hoy si aun no termina.</summary>
        private static DateTime HastaHoy(ClaseLeida clase, LecturaDeAsistencia lectura)
        {
            DateTime hoy = lectura.AhoraServidor.Date;
            return clase.VigenteHasta < hoy ? clase.VigenteHasta : hoy;
        }

        /// <summary>
        /// Las sesiones del profesor de la clase en [vigente_desde, hastaHoy] que
        /// tienen sus dos horas, en orden (dia, entrada, id), y despues las de sus
        /// clases del horario de esos dias que no tienen sesion (SinSesion).
        /// </summary>
        private static List<Sesion> SesionesDe(ClaseLeida clase, LecturaDeAsistencia lectura, DateTime hastaHoy)
        {
            List<Sesion> sesiones = new List<Sesion>();
            List<SesionLeida> delProfesor;
            if (lectura.SesionesPorDocente.TryGetValue(clase.ProfesorId, out delProfesor))
            {
                foreach (SesionLeida s in delProfesor
                    .Where(x => x.Inicio.HasValue && x.Fin.HasValue && x.Fecha >= clase.VigenteDesde && x.Fecha <= hastaHoy)
                    .OrderBy(x => x.Fecha).ThenBy(x => x.Inicio.Value).ThenBy(x => x.Id))
                {
                    sesiones.Add(new Sesion
                    {
                        Id = s.Id,
                        Fecha = s.Fecha,
                        Inicio = s.Inicio.Value,
                        Fin = s.Fin.Value,
                        LaboratorioId = s.LaboratorioId ?? 0,
                        GrupoId = s.GrupoId ?? 0,
                        DeLaClase = s.MateriaId.HasValue && s.GrupoId.HasValue && s.MateriaId.Value == clase.MateriaId && s.GrupoId.Value == clase.GrupoId
                    });
                }
            }

            List<FranjaLeida> franjas;
            List<ExcepcionLeida> excepciones;
            lectura.FranjasPorDocente.TryGetValue(clase.ProfesorId, out franjas);
            lectura.ExcepcionesPorDocente.TryGetValue(clase.ProfesorId, out excepciones);
            sesiones.AddRange(SinSesion(sesiones, franjas ?? new List<FranjaLeida>(), excepciones ?? new List<ExcepcionLeida>(),
                clase.MateriaId, clase.GrupoId, clase.VigenteDesde, hastaHoy));
            return sesiones;
        }

        /// <summary>
        /// Con el codigo fijo, las clases seguidas del profesor en el mismo
        /// laboratorio, una de ellas de esta clase: (anterior, siguiente). La
        /// anterior es la que empieza antes y sigue en los 15 minutos antes de la
        /// siguiente.
        /// </summary>
        private static List<KeyValuePair<Sesion, Sesion>> Seguidas(List<Sesion> sesiones, IDictionary<int, ModoCodigo> modos)
        {
            HashSet<DateTime> fechas = new HashSet<DateTime>(sesiones.Where(s => s.Id > 0).Select(s => s.Fecha));
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

            return seguidas;
        }

        /// <summary>
        /// El primer par de clases seguidas (en el orden de Seguidas) cuya anterior es
        /// la sesion de la bitacora y en cuya ventana se registro (de 15 minutos antes
        /// a 10 despues del inicio de la siguiente), o un par vacio (Key null). Sin
        /// hora de registro no cae en ninguna ventana.
        /// </summary>
        private static KeyValuePair<Sesion, Sesion> VentanaDe(BitacoraLeida b, Dictionary<int, List<KeyValuePair<Sesion, Sesion>>> paresDeLaAnterior)
        {
            List<KeyValuePair<Sesion, Sesion>> pares;
            if (b.RegistradoEn.HasValue && paresDeLaAnterior.TryGetValue(b.SesionId, out pares))
            {
                foreach (KeyValuePair<Sesion, Sesion> p in pares)
                {
                    if (b.RegistradoEn.Value >= p.Value.Fecha + p.Value.Inicio - Margen && b.RegistradoEn.Value < p.Value.Fecha + p.Value.Inicio + Llenado)
                    {
                        return p;
                    }
                }
            }

            return default(KeyValuePair<Sesion, Sesion>);
        }

        /// <summary>Las sesiones (reales) cuyas bitacoras cuenta la clase: las suyas y las de sus clases seguidas.</summary>
        private static List<int> ConBitacoras(List<Sesion> sesiones, List<KeyValuePair<Sesion, Sesion>> seguidas)
        {
            return sesiones.Where(s => s.DeLaClase && s.Id > 0).Select(s => s.Id)
                .Concat(seguidas.SelectMany(p => new[] { p.Key.Id, p.Value.Id }))
                .Where(i => i > 0).Distinct().ToList();
        }

        /// <summary>Las sesiones cuyas bitacoras va a mirar Calcular para esa clase (DeLaClase solo lee esas).</summary>
        private static List<int> SesionesConBitacoras(ClaseLeida clase, LecturaDeAsistencia lectura)
        {
            List<Sesion> sesiones = SesionesDe(clase, lectura, HastaHoy(clase, lectura));
            return ConBitacoras(sesiones, Seguidas(sesiones, lectura.Modos));
        }

        /// <summary>Pone las sesiones en la lectura: por profesor (0 sin profesor) y por id.</summary>
        private static void Guardar(LecturaDeAsistencia lectura, IEnumerable<SesionLeida> sesiones)
        {
            foreach (SesionLeida sesion in sesiones)
            {
                Agregar(lectura.SesionesPorDocente, sesion.DocenteId ?? 0, sesion);
                lectura.SesionPorId[sesion.Id] = sesion;
            }
        }

        private static void Agregar<T>(Dictionary<int, List<T>> porClave, int clave, T valor)
        {
            List<T> lista;
            if (!porClave.TryGetValue(clave, out lista))
            {
                lista = new List<T>();
                porClave[clave] = lista;
            }

            lista.Add(valor);
        }

        /// <summary>
        /// Condicion SQL: la bitacora rb es de alguien que trajo su propio equipo
        /// (bandera 16 de E6). Cuando Consultas tenga la suya (paquete 2 de
        /// Estadisticas), esta pasa a usarla.
        /// </summary>
        internal const string CondicionEquipoPropio = "TRIM(COALESCE(rb.falla_red, '')) = 'EP'";

        /// <summary>
        /// La clase es del periodo [desde, hasta]: su vigencia lo toca y, con un ciclo
        /// ("Este ciclo" o "Ciclo anterior"), ademas empezo en el (R2 de Estadisticas).
        /// </summary>
        internal static bool DelPeriodo(ClaseLeida clase, DateTime desde, DateTime hasta, bool esCiclo)
        {
            return clase.VigenteDesde <= hasta.Date && clase.VigenteHasta >= desde.Date && (!esCiclo || clase.VigenteDesde >= desde.Date);
        }

        /// <summary>
        /// Desde cuando hay que leer para que la lista de cada clase del periodo salga
        /// de todo su ciclo, como en Mis grupos (R3): el menor entre desde y el primer
        /// dia de las clases del periodo. Nunca despues de desde.
        /// </summary>
        internal static DateTime DesdeLecturaDe(IEnumerable<ClaseLeida> clases, DateTime desde, DateTime hasta, bool esCiclo)
        {
            DateTime resultado = desde.Date;
            foreach (ClaseLeida clase in clases)
            {
                if (DelPeriodo(clase, desde, hasta, esCiclo) && clase.VigenteDesde < resultado)
                {
                    resultado = clase.VigenteDesde;
                }
            }

            return resultado;
        }

        /// <summary>
        /// Lo que hace falta para la asistencia de TODAS las clases a la vez
        /// (Estadisticas), en una conexion y con pocas consultas en vez de unas diez
        /// por clase: NOW(), E1 (todas las clases activas que tocan los ultimos 400
        /// dias, sin filtro de periodo), DesdeLectura (DesdeLecturaDe) y, de
        /// [DesdeLectura, hoy], E2 (sesiones, tambien las de uso libre), E3 (franjas
        /// de las clases activas), E4 (excepciones), los modos, E6 (bitacoras con sus
        /// banderas), E7 (estudiantes con bitacoras o asistencias dadas) y E11 (las
        /// asistencias dadas, db/22). Solo lee. CalcularTodas lo cuenta.
        /// </summary>
        /// <param name="desde">Primer dia del periodo elegido.</param>
        /// <param name="hasta">Ultimo dia del periodo elegido (hoy o antes).</param>
        /// <param name="esCiclo">"Este ciclo" o "Ciclo anterior": solo son del periodo las clases que empezaron en el.</param>
        internal static LecturaDeAsistencia LeerEnLote(MySqlConnection conexion, DateTime desde, DateTime hasta, bool esCiclo)
        {
            LecturaDeAsistencia lectura = new LecturaDeAsistencia();
            using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion))
            {
                lectura.AhoraServidor = Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
                lectura.LeidoEnRelojLocal = DateTime.Now;
            }

            lectura.ConRetiro = Esquema.AsistenciaRetirable(conexion);
            lectura.ConDadas = Esquema.AsistenciaDada(conexion);
            DateTime hoy = lectura.AhoraServidor.Date;

            // E1: el periodo se aplica en memoria.
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT c.idclases, c.usuarios_idusuarios, c.materias_id_materia, c.grupos_idgrupos,
                       c.vigente_desde, c.vigente_hasta, m.nombre_materia, g.nombre_grupo,
                       CONCAT_WS(' ', u.nombre, u.apellido_paterno, u.apellido_materno) AS docente
                FROM clases c
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                WHERE c.activa = 1 AND c.vigente_desde <= CURDATE() AND c.vigente_hasta >= CURDATE() - INTERVAL 399 DAY
                ORDER BY c.usuarios_idusuarios, c.idclases", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    lectura.Clases.Add(new ClaseLeida
                    {
                        Id = reader.GetInt32(0),
                        ProfesorId = Convert.ToInt32(reader[1]),
                        MateriaId = Convert.ToInt32(reader[2]),
                        GrupoId = Convert.ToInt32(reader[3]),
                        VigenteDesde = Convert.ToDateTime(reader[4], CultureInfo.InvariantCulture).Date,
                        VigenteHasta = Convert.ToDateTime(reader[5], CultureInfo.InvariantCulture).Date,
                        Materia = reader.IsDBNull(6) ? "" : reader.GetString(6),
                        Grupo = reader.IsDBNull(7) ? "" : reader.GetString(7),
                        Docente = reader.IsDBNull(8) ? "" : reader.GetString(8)
                    });
                }
            }

            lectura.DesdeLectura = DesdeLecturaDe(lectura.Clases, desde, hasta, esCiclo);
            lectura.HastaLectura = hoy;

            // E2: todas las sesiones, por profesor, en el orden de DeLaClase (dia y
            // entrada) con desempate por id.
            List<SesionLeida> sesiones = new List<SesionLeida>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT ca.idcodigos_accesos, " + VigenciaFranjas.DiaDeSesion("ca") + @" AS dia,
                       TIME(ca.hora_entrada) AS entrada, TIME(ca.hora_salida) AS salida,
                       ca.laboratorios_idlaboratorios, ca.materias_id_materia, ca.grupos_idgrupos, ca.usuarios_idusuarios,
                       CASE WHEN " + HorariosDatos.CondicionUsoLibre + @" THEN 1 ELSE 0 END AS uso_libre
                FROM codigos_accesos ca
                WHERE " + VigenciaFranjas.DiaDeSesion("ca") + @" BETWEEN @Desde AND @Hasta
                ORDER BY ca.usuarios_idusuarios, dia, entrada, ca.idcodigos_accesos", conexion))
            {
                comando.CommandTimeout = 60;
                comando.Parameters.AddWithValue("@Desde", lectura.DesdeLectura);
                comando.Parameters.AddWithValue("@Hasta", hoy);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(1))
                        {
                            continue;
                        }

                        sesiones.Add(new SesionLeida
                        {
                            Id = reader.GetInt32(0),
                            Fecha = Convert.ToDateTime(reader[1], CultureInfo.InvariantCulture).Date,
                            Inicio = reader.IsDBNull(2) ? (TimeSpan?)null : reader.GetTimeSpan(2),
                            Fin = reader.IsDBNull(3) ? (TimeSpan?)null : reader.GetTimeSpan(3),
                            LaboratorioId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                            MateriaId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                            GrupoId = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                            DocenteId = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7),
                            UsoLibre = Convert.ToInt32(reader[8]) == 1
                        });
                    }
                }
            }

            Guardar(lectura, sesiones);

            // E3 y E4, por profesor.
            foreach (FranjaLeida franja in LeerFranjas(conexion, null))
            {
                Agregar(lectura.FranjasPorDocente, franja.DocenteId, franja);
            }

            foreach (ExcepcionLeida excepcion in LeerExcepciones(conexion, null, lectura.DesdeLectura, hoy))
            {
                Agregar(lectura.ExcepcionesPorDocente, excepcion.DocenteId, excepcion);
            }

            lectura.Modos = ModosDeAcceso.LeerTodos(conexion);

            // E6: las banderas en un entero (los textos de las fallas no hacen falta aqui).
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT rb.idregistros_bitacoras, rb.fk_codigo_accesos, rb.fk_usuario, rb.registrado_en, "
                + "(CASE WHEN (" + Consultas.CondicionConFalla + ") THEN " + BitacoraLeida.BanderaConFalla + " ELSE 0 END)"
                + " + (CASE WHEN " + Consultas.CondicionFallaEn("red") + " THEN " + BitacoraLeida.BanderaRed + " ELSE 0 END)"
                + " + (CASE WHEN " + Consultas.CondicionFallaEn("hardware") + " THEN " + BitacoraLeida.BanderaHardware + " ELSE 0 END)"
                + " + (CASE WHEN " + Consultas.CondicionFallaEn("software") + " THEN " + BitacoraLeida.BanderaSoftware + " ELSE 0 END)"
                + " + (CASE WHEN " + CondicionEquipoPropio + " THEN " + BitacoraLeida.BanderaEquipoPropio + " ELSE 0 END)"
                + " + (CASE WHEN rb.registrado_sin_conexion = 1 THEN " + BitacoraLeida.BanderaSinConexion + " ELSE 0 END)"
                + " + " + (lectura.ConRetiro ? "(CASE WHEN ar.fk_bitacora IS NOT NULL THEN " + BitacoraLeida.BanderaRetirada + " ELSE 0 END)" : "0") + " AS banderas, "
                + "TRIM(COALESCE(rb.numero_computadora, '')) AS equipo "
                + "FROM registros_bitacoras rb "
                + "INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos "
                + (lectura.ConRetiro ? "LEFT JOIN asistencias_retiradas ar ON ar.fk_bitacora = rb.idregistros_bitacoras " : "")
                + "WHERE " + VigenciaFranjas.DiaDeSesion("ca") + " BETWEEN @Desde AND @Hasta", conexion))
            {
                comando.CommandTimeout = 60;
                comando.Parameters.AddWithValue("@Desde", lectura.DesdeLectura);
                comando.Parameters.AddWithValue("@Hasta", hoy);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        BitacoraLeida bitacora = new BitacoraLeida
                        {
                            Id = reader.GetInt32(0),
                            SesionId = reader.GetInt32(1),
                            AlumnoId = reader.GetInt32(2),
                            RegistradoEn = reader.IsDBNull(3) ? (DateTime?)null : Convert.ToDateTime(reader[3], CultureInfo.InvariantCulture),
                            Banderas = Convert.ToInt32(reader[4]),
                            Equipo = reader.IsDBNull(5) ? "" : Convert.ToString(reader[5], CultureInfo.InvariantCulture)
                        };
                        Agregar(lectura.BitacorasPorSesion, bitacora.SesionId, bitacora);
                    }
                }
            }

            // E7: las cuentas de estudiante con bitacoras o con asistencias dadas (sin
            // la UNION, quien solo tiene dadas saldria de la lista), con su grupo guardado.
            string conBitacora = "rb.fk_usuario AS id FROM registros_bitacoras rb "
                + "INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = rb.fk_codigo_accesos "
                + "WHERE " + VigenciaFranjas.DiaDeSesion("ca") + " BETWEEN @Desde AND @Hasta";
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT u.idusuarios, u.numero_identificador,
                       CONCAT_WS(' ', u.apellido_paterno, u.apellido_materno, u.nombre) AS nombre,
                       " + (Esquema.TieneBajas(conexion) ? "IF(u.activo = 1, 1, 0)" : "1") + @" AS activo,
                       cg.grupos_idgrupos, g.nombre_grupo
                FROM (" + (lectura.ConDadas
                    ? "SELECT " + conBitacora + " UNION SELECT d.fk_alumno FROM asistencias_dadas d WHERE d.activa = 1 AND d.fecha BETWEEN @Desde AND @Hasta"
                    : "SELECT DISTINCT " + conBitacora) + @") p
                INNER JOIN usuarios u ON u.idusuarios = p.id AND u.fk_tipo_usuario = 1
                LEFT JOIN (SELECT x.usuarios_numero_identificador AS estudiante, x.grupos_idgrupos
                             FROM carrera_grupo_semestre x
                             INNER JOIN (SELECT usuarios_numero_identificador AS estudiante, MAX(idcarrera_grupo_semestre) AS ultimo
                                           FROM carrera_grupo_semestre WHERE grupos_idgrupos IS NOT NULL
                                          GROUP BY usuarios_numero_identificador) m ON m.ultimo = x.idcarrera_grupo_semestre) cg
                       ON cg.estudiante = u.idusuarios
                LEFT JOIN grupos g ON g.idgrupos = cg.grupos_idgrupos", conexion))
            {
                comando.Parameters.AddWithValue("@Desde", lectura.DesdeLectura);
                comando.Parameters.AddWithValue("@Hasta", hoy);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        EstudianteLeido estudiante = new EstudianteLeido
                        {
                            Id = reader.GetInt32(0),
                            Matricula = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Nombre = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Activo = Convert.ToInt32(reader[3]) == 1,
                            GrupoId = reader.IsDBNull(4) ? (int?)null : Convert.ToInt32(reader[4]),
                            Grupo = reader.IsDBNull(5) ? "" : reader.GetString(5)
                        };
                        lectura.Estudiantes[estudiante.Id] = estudiante;
                        if (estudiante.GrupoId.HasValue)
                        {
                            lectura.Grupos[estudiante.Id] = estudiante.GrupoId.Value;
                        }
                    }
                }
            }

            // E11: las asistencias dadas de todas las clases (Calcular se queda con
            // las del periodo de cada una).
            if (lectura.ConDadas)
            {
                foreach (AsistenciaDada dada in LeerDadas(conexion, null, lectura.DesdeLectura, hoy))
                {
                    Agregar(lectura.DadasPorClase, dada.ClaseId, dada);
                }
            }

            return lectura;
        }

        /// <summary>
        /// La asistencia de cada clase de la lectura del lote que empezo desde
        /// DesdeLectura (las demas no se leyeron enteras), con Calcular: lo mismo que
        /// DeLaClase de cada una. A la que hoy tiene bloques se le pasa su tramo de
        /// hoy (el inicio del primero y el fin del ultimo, sin los cancelados, como
        /// en Mis grupos): asi su columna de hoy esta en curso igual que en Mis
        /// grupos, tambien en la pausa entre dos bloques y aunque aun no tenga sesion.
        /// </summary>
        /// <param name="tramosDeHoy">Por idclases; null o sin la clase = hoy no tiene bloques.</param>
        internal static Dictionary<int, AsistenciaDeClase> CalcularTodas(LecturaDeAsistencia lectura, IDictionary<int, TramoHorario> tramosDeHoy)
        {
            Dictionary<int, AsistenciaDeClase> listas = new Dictionary<int, AsistenciaDeClase>();
            foreach (ClaseLeida clase in lectura.Clases.Where(c => c.VigenteDesde >= lectura.DesdeLectura))
            {
                TramoHorario tramo = null;
                if (tramosDeHoy != null)
                {
                    tramosDeHoy.TryGetValue(clase.Id, out tramo);
                }

                listas[clase.Id] = tramo == null
                    ? Calcular(clase, lectura, null, null)
                    : Calcular(clase, lectura, tramo.Inicio, tramo.Fin);
            }

            return listas;
        }

        /// <summary>
        /// Las clases del profesor que tocaban los dias en que tuvo alguna sesion
        /// (sus franjas activas, que ese dia se daban con la vigencia de su clase
        /// y la suya propia, db/20, y no canceladas, y sus sesiones extra) y que
        /// no tienen sesion: con id negativo y sin bitacoras. Asi, si todos los
        /// alumnos de una clase entraron antes (en la sesion de la anterior),
        /// igual se sabe que esa clase seguia y se les cuenta en ella. Sin consultas:
        /// las franjas activas del profesor (LeerFranjas, E3) y sus excepciones
        /// (LeerExcepciones, E4), de las que solo cuentan las de [desde, hasta].
        /// Siempre en el mismo orden (franjas y extras por id).
        /// </summary>
        private static List<Sesion> SinSesion(List<Sesion> sesiones, List<FranjaLeida> franjasDelProfesor, List<ExcepcionLeida> excepcionesDelProfesor,
            int materia, int grupo, DateTime desde, DateTime hasta)
        {
            List<Sesion> faltantes = new List<Sesion>();
            HashSet<DateTime> dias = new HashSet<DateTime>(sesiones.Select(s => s.Fecha));
            if (dias.Count == 0)
            {
                return faltantes;
            }

            List<FranjaLeida> franjas = franjasDelProfesor.OrderBy(f => f.Id).ToList();
            HashSet<string> canceladas = new HashSet<string>();
            List<Sesion> extras = new List<Sesion>();
            foreach (ExcepcionLeida x in excepcionesDelProfesor.Where(e => e.Fecha >= desde && e.Fecha <= hasta).OrderBy(e => e.Id))
            {
                if (x.EsCancelada)
                {
                    if (x.FranjaId.HasValue)
                    {
                        canceladas.Add(x.FranjaId.Value + "|" + x.Fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                    }

                    continue;
                }

                if (!x.LaboratorioId.HasValue || !x.Inicio.HasValue || !x.Fin.HasValue)
                {
                    continue;
                }

                extras.Add(new Sesion
                {
                    Fecha = x.Fecha,
                    LaboratorioId = x.LaboratorioId.Value,
                    Inicio = x.Inicio.Value,
                    Fin = x.Fin.Value,
                    GrupoId = x.GrupoId,
                    DeLaClase = x.MateriaId == materia && x.GrupoId == grupo
                });
            }

            int siguienteId = -1;
            foreach (DateTime dia in dias)
            {
                int diaSemana = dia.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dia.DayOfWeek;
                IEnumerable<Sesion> tocaban = franjas
                    .Where(f => f.DiaSemana == diaSemana
                        && VigenciaFranjas.SeDaEn(dia, true, f.ClaseDesde, f.ClaseHasta, f.FranjaDesde, f.FranjaHasta)
                        && !canceladas.Contains(f.Id + "|" + dia.ToString("yyyyMMdd", CultureInfo.InvariantCulture)))
                    .Select(f => new Sesion
                    {
                        Id = f.Id,
                        LaboratorioId = f.LaboratorioId,
                        Inicio = f.Inicio,
                        Fin = f.Fin,
                        GrupoId = f.GrupoId,
                        DeLaClase = f.MateriaId == materia && f.GrupoId == grupo
                    })
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
        /// Las franjas de las clases activas (sin mirar fechas: SinSesion las cruza
        /// con VigenciaFranjas.SeDaEn), con la vigencia de su clase y la suya propia
        /// (db/20): las de un profesor (DeLaClase), o las de todos con null (E3).
        /// </summary>
        private static List<FranjaLeida> LeerFranjas(MySqlConnection conexion, int? profesor)
        {
            List<FranjaLeida> franjas = new List<FranjaLeida>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT h.idhorarios, h.laboratorios_idlaboratorios, h.dia_semana, h.hora_inicio, h.hora_fin,
                       c.materias_id_materia, c.grupos_idgrupos, c.vigente_desde, c.vigente_hasta,
                       " + VigenciaFranjas.Columnas(Esquema.VigenciaDeFranjas(conexion)) + @",
                       c.idclases, c.usuarios_idusuarios
                FROM horarios h
                INNER JOIN clases c ON c.idclases = h.clases_idclases
                WHERE " + (profesor.HasValue ? "c.usuarios_idusuarios = @Profesor AND " : "") + "c.activa = 1", conexion))
            {
                if (profesor.HasValue)
                {
                    comando.Parameters.AddWithValue("@Profesor", profesor.Value);
                }

                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        franjas.Add(new FranjaLeida
                        {
                            Id = reader.GetInt32(0),
                            LaboratorioId = reader.GetInt32(1),
                            DiaSemana = Convert.ToInt32(reader[2]),
                            Inicio = reader.GetTimeSpan(3),
                            Fin = reader.GetTimeSpan(4),
                            MateriaId = reader.GetInt32(5),
                            GrupoId = reader.GetInt32(6),
                            ClaseDesde = Convert.ToDateTime(reader[7], CultureInfo.InvariantCulture).Date,
                            ClaseHasta = Convert.ToDateTime(reader[8], CultureInfo.InvariantCulture).Date,
                            FranjaDesde = reader.IsDBNull(9) ? (DateTime?)null : Convert.ToDateTime(reader[9], CultureInfo.InvariantCulture).Date,
                            FranjaHasta = reader.IsDBNull(10) ? (DateTime?)null : Convert.ToDateTime(reader[10], CultureInfo.InvariantCulture).Date,
                            ClaseId = reader.GetInt32(11),
                            DocenteId = reader.GetInt32(12)
                        });
                    }
                }
            }

            return franjas;
        }

        /// <summary>
        /// Las franjas canceladas y las sesiones extra con fecha en [desde, hasta], sin
        /// mirar si su clase sigue activa: las de un profesor (DeLaClase), o las de
        /// todos con null (E4).
        /// </summary>
        private static List<ExcepcionLeida> LeerExcepciones(MySqlConnection conexion, int? profesor, DateTime desde, DateTime hasta)
        {
            List<ExcepcionLeida> excepciones = new List<ExcepcionLeida>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT x.tipo, x.horarios_idhorarios, x.fecha, x.laboratorios_idlaboratorios, x.hora_inicio, x.hora_fin,
                       c.materias_id_materia, c.grupos_idgrupos, x.idhorario_excepciones, c.idclases, c.usuarios_idusuarios
                FROM horario_excepciones x
                LEFT JOIN horarios h ON h.idhorarios = x.horarios_idhorarios
                INNER JOIN clases c ON c.idclases = COALESCE(x.clases_idclases, h.clases_idclases)
                WHERE " + (profesor.HasValue ? "c.usuarios_idusuarios = @Profesor AND " : "") + "x.fecha BETWEEN @Desde AND @Hasta", conexion))
            {
                if (profesor.HasValue)
                {
                    comando.Parameters.AddWithValue("@Profesor", profesor.Value);
                }

                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        excepciones.Add(new ExcepcionLeida
                        {
                            Tipo = reader.GetString(0),
                            FranjaId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                            Fecha = Convert.ToDateTime(reader[2], CultureInfo.InvariantCulture).Date,
                            LaboratorioId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                            Inicio = reader.IsDBNull(4) ? (TimeSpan?)null : reader.GetTimeSpan(4),
                            Fin = reader.IsDBNull(5) ? (TimeSpan?)null : reader.GetTimeSpan(5),
                            MateriaId = reader.GetInt32(6),
                            GrupoId = reader.GetInt32(7),
                            Id = reader.GetInt32(8),
                            ClaseId = reader.GetInt32(9),
                            DocenteId = reader.GetInt32(10)
                        });
                    }
                }
            }

            return excepciones;
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

        private static List<BitacoraLeida> LeerBitacoras(MySqlConnection conexion, List<int> sesiones, bool conRetiro)
        {
            List<BitacoraLeida> bitacoras = new List<BitacoraLeida>();
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
                    bitacoras.Add(new BitacoraLeida
                    {
                        Id = reader.GetInt32(0),
                        SesionId = reader.GetInt32(1),
                        AlumnoId = reader.GetInt32(2),
                        RegistradoEn = reader.IsDBNull(3) ? (DateTime?)null : Convert.ToDateTime(reader[3], CultureInfo.InvariantCulture),
                        Banderas = Convert.ToInt32(reader[4]) == 1 ? BitacoraLeida.BanderaRetirada : 0
                    });
                }
            }

            return bitacoras;
        }

        // Lo que se dice si algo quiere escribir una asistencia dada sin db/22.
        private const string FaltaLa22 = "Todavía no se puede dar asistencia: a la base de datos le falta una actualización (db/22). Avisa a la administración de DigBit.";
        private const string DarFueraDeClase = "Ahora no hay una clase de este grupo en curso: la asistencia solo se puede dar mientras dura la clase.";
        private const string CambiarFueraDeClase = "La clase no está en curso: la asistencia solo se puede cambiar mientras dura.";

        /// <summary>
        /// Quita (o devuelve) la asistencia de hoy de un alumno que registro su
        /// bitacora pero no esta en el salon: sus bitacoras de hoy que cuentan para
        /// esta clase (las de AlumnoDeAsistencia.BitacorasDelDia) dejan de contar
        /// como asistencia, o vuelven a contar. Solo mientras dura un bloque de la
        /// clase, con el reloj del servidor (SesionDeHoy.BloqueEnCurso: no en la
        /// pausa entre dos bloques, ni en una franja cancelada, ni si la clase no es
        /// suya), y solo las de sesiones de hoy de este profesor. Al quitar, si ese
        /// dia tambien se le dio la asistencia (db/22), se anula en la misma
        /// transaccion: si no, la dada le ganaria a la quitada. Devolver no revive
        /// una dada anulada (la bitacora vuelve a contar). Devuelve null si se hizo,
        /// o por que no.
        /// </summary>
        public string CambiarAsistencia(int idClase, int idProfesor, int idAlumno, IList<int> bitacoras, bool quitar)
        {
            Permisos.Exigir(quitar ? "Quitar una asistencia" : "Devolver una asistencia");
            if (bitacoras == null || bitacoras.Count == 0)
            {
                return "Esa persona no registró su bitácora hoy en esta clase.";
            }

            string lista = string.Join(",", bitacoras.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            int anuladas = 0;
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.AsistenciaRetirable(conexion))
                {
                    return "Todavía no se pueden quitar asistencias: a la base de datos le falta una actualización (db/17). Avisa a la administración de DigBit.";
                }

                // Que la clase siga: un bloque suyo de hoy en curso (aun sin sesion, si
                // todos llegaron antes y quedaron en la anterior).
                if (SesionDeHoy.BloqueEnCurso(conexion, idClase, idProfesor) == null)
                {
                    return CambiarFueraDeClase;
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

                bool anularDada = quitar && Esquema.AsistenciaDada(conexion);
                using (MySqlTransaction transaccion = conexion.BeginTransaction())
                {
                    using (MySqlCommand comando = new MySqlCommand(quitar
                        ? "INSERT IGNORE INTO asistencias_retiradas (fk_bitacora, fk_profesor, retirada_en) SELECT idregistros_bitacoras, @Profesor, NOW() "
                          + "FROM registros_bitacoras WHERE idregistros_bitacoras IN (" + lista + ")"
                        : "DELETE FROM asistencias_retiradas WHERE fk_bitacora IN (" + lista + ")", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@Profesor", idProfesor);
                        comando.ExecuteNonQuery();
                    }

                    // Registro su bitacora y no esta en el salon: tampoco le cuenta la que se le dio.
                    if (anularDada)
                    {
                        using (MySqlCommand comando = new MySqlCommand(@"
                            UPDATE asistencias_dadas
                               SET activa = NULL, anulada_en = NOW(), anulada_por = @Profesor
                             WHERE fk_clase = @Clase AND fk_alumno = @Alumno AND fecha = CURDATE() AND activa = 1", conexion, transaccion))
                        {
                            comando.Parameters.AddWithValue("@Profesor", idProfesor);
                            comando.Parameters.AddWithValue("@Clase", idClase);
                            comando.Parameters.AddWithValue("@Alumno", idAlumno);
                            anuladas = comando.ExecuteNonQuery();
                        }
                    }

                    transaccion.Commit();
                }
            }

            Log.Info((quitar ? "Asistencia quitada" : "Asistencia devuelta") + " por el profesor id " + idProfesor + " al alumno id " + idAlumno
                + " en la clase " + idClase + " (bitacoras " + lista + (anuladas > 0 ? "; tambien se anulo la asistencia que le dio" : "") + ").");
            return null;
        }

        /// <summary>
        /// Lo que necesita el dialogo para dar la asistencia en esa clase: el bloque en
        /// curso, su laboratorio y sus equipos, y lo que ya avisaron hoy los alumnos de
        /// la clase de los equipos sin internet (db/10). Problema dice por que no se
        /// puede ahora (sin db/22, o la clase no esta en curso). Lanza si no hay conexion.
        /// </summary>
        public PreparacionDar PrepararDar(int idClase, int idProfesor)
        {
            Permisos.Exigir("Dar asistencia");
            PreparacionDar preparacion = new PreparacionDar();
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.AsistenciaDada(conexion))
                {
                    preparacion.Problema = FaltaLa22;
                    return preparacion;
                }

                SesionAgenda bloque = SesionDeHoy.BloqueEnCurso(conexion, idClase, idProfesor);
                if (bloque == null)
                {
                    preparacion.Problema = DarFueraDeClase;
                    return preparacion;
                }

                preparacion.Inicio = bloque.Inicio;
                preparacion.Fin = bloque.Fin;
                preparacion.LaboratorioId = bloque.LaboratorioId;
                using (MySqlCommand comando = new MySqlCommand("SELECT nombre_laboratorio FROM laboratorios WHERE idlaboratorios = @Lab", conexion))
                {
                    comando.Parameters.AddWithValue("@Lab", bloque.LaboratorioId);
                    object nombre = comando.ExecuteScalar();
                    preparacion.Laboratorio = nombre == null || nombre == DBNull.Value ? "" : Convert.ToString(nombre, CultureInfo.InvariantCulture);
                }

                // Sin la lista, el profesor escribe el numero (como en Reportar una falla).
                try
                {
                    preparacion.Equipos = Consultas.EquiposDelLaboratorio(conexion, bloque.LaboratorioId);
                }
                catch (MySqlException ex)
                {
                    Log.Aviso("Dar asistencia: no se pudieron leer los equipos del laboratorio " + bloque.LaboratorioId + ": " + ex.Message);
                }

                if (Esquema.EquiposSinInternet(conexion))
                {
                    List<string> avisos = new List<string>();
                    using (MySqlCommand comando = new MySqlCommand(@"
                        SELECT rb.equipos_sin_internet
                        FROM registros_bitacoras rb
                        INNER JOIN codigos_accesos s ON s.idcodigos_accesos = rb.fk_codigo_accesos
                        INNER JOIN clases c ON c.idclases = @Clase
                        WHERE s.fecha = CURDATE() AND s.usuarios_idusuarios = c.usuarios_idusuarios
                          AND s.materias_id_materia = c.materias_id_materia AND s.grupos_idgrupos = c.grupos_idgrupos
                          AND rb.equipos_sin_internet IS NOT NULL AND rb.equipos_sin_internet <> ''", conexion))
                    {
                        comando.Parameters.AddWithValue("@Clase", idClase);
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                avisos.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture));
                            }
                        }
                    }

                    List<EquipoSinInternet> contados = Consultas.ContarSinInternet(avisos);
                    if (contados.Count > 0)
                    {
                        preparacion.Pista = "En esta clase ya avisaron. " + Consultas.DescribirSinInternet(contados, false) + ".";
                    }
                }
            }

            return preparacion;
        }

        /// <summary>
        /// La cuenta con esa matricula EXACTA (sin espacios al inicio ni al final), o
        /// null si no hay ninguna: su nombre, si es alumno y esta activo, y si hoy ya
        /// tiene bitacoras o una asistencia dada en esta clase. Nunca lee el correo ni
        /// la contrasena. Solo lee.
        /// </summary>
        public EstudianteBuscado BuscarEstudiante(int idClase, string matricula)
        {
            matricula = (matricula ?? "").Trim();
            if (matricula.Length == 0)
            {
                return null;
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                bool conBajas = Esquema.TieneBajas(conexion);
                bool conDadas = Esquema.AsistenciaDada(conexion);
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT u.idusuarios, u.numero_identificador,
                           CONCAT_WS(' ', u.apellido_paterno, u.apellido_materno, u.nombre) AS nombre,
                           u.fk_tipo_usuario, " + (conBajas ? "IF(u.activo = 1, 1, 0)" : "1") + @" AS activo,
                           (SELECT COUNT(*) FROM registros_bitacoras rb
                              INNER JOIN codigos_accesos s ON s.idcodigos_accesos = rb.fk_codigo_accesos
                              INNER JOIN clases c ON c.idclases = @Clase
                             WHERE rb.fk_usuario = u.idusuarios AND s.fecha = CURDATE()
                               AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia
                               AND s.grupos_idgrupos = c.grupos_idgrupos) AS bitacoras_hoy,
                           " + (conDadas
                                ? @"(SELECT COUNT(*) FROM asistencias_dadas d
                             WHERE d.fk_clase = @Clase AND d.fecha = CURDATE() AND d.fk_alumno = u.idusuarios AND d.activa = 1)"
                                : "0") + @" AS dada_hoy
                    FROM usuarios u
                    WHERE u.numero_identificador = @Matricula
                    LIMIT 1", conexion))
                {
                    comando.Parameters.AddWithValue("@Clase", idClase);
                    comando.Parameters.AddWithValue("@Matricula", matricula);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return null;
                        }

                        return new EstudianteBuscado
                        {
                            Id = Convert.ToInt32(reader[0]),
                            Matricula = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Nombre = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Tipo = Convert.ToInt32(reader[3]),
                            Activo = Convert.ToInt32(reader[4]) == 1,
                            BitacorasHoy = Convert.ToInt32(reader[5]),
                            DadaHoy = Convert.ToInt32(reader[6]) > 0
                        };
                    }
                }
            }
        }

        /// <summary>
        /// Le da la asistencia de hoy en esta clase a un alumno que esta en el salon
        /// pero no pudo registrar su bitacora (db/22). Solo mientras dura un bloque de
        /// la clase (reloj del servidor); si ningun alumno ha registrado, crea la
        /// sesion del bloque, como Reportar una falla. Lo comprueba el servidor en la
        /// misma sentencia: que la sesion y la clase sean de ese profesor, de hoy y
        /// en curso, la clase activa y vigente, la cuenta de un alumno activo y que
        /// hoy no tenga ninguna bitacora en esta clase (tampoco una quitada: eso se
        /// devuelve). Una por clase, dia y alumno (UNIQUE). Sin transaccion: asi no
        /// hay lectura vieja de REPEATABLE READ tras un 1062 al crear la sesion.
        /// Devuelve null si quedo, o por que no.
        /// </summary>
        /// <param name="motivo">Una de AsistenciasDadas.Motivos.</param>
        /// <param name="equipo">El equipo que no le sirvio, o null (solo con sin_internet y equipo).</param>
        /// <param name="comentario">El detalle (obligatorio con otro), en un renglon.</param>
        public string DarAsistencia(int idClase, int idProfesor, int idAlumno, string motivo, string equipo, string comentario)
        {
            Permisos.Exigir("Dar asistencia");
            if (motivo == null || !AsistenciasDadas.Motivos.Contains(motivo))
            {
                return "Elige por qué no pudo registrar su bitácora.";
            }

            comentario = HistorialCambios.Limpiar(Limpio(comentario), AsistenciasDadas.LargoComentario);
            if (motivo == AsistenciasDadas.Otro && comentario == null)
            {
                return "Escribe el motivo en el detalle.";
            }

            equipo = AsistenciasDadas.ConEquipo(motivo) ? HistorialCambios.Limpiar(Limpio(equipo), int.MaxValue) : null;
            if (equipo != null && string.Equals(equipo, AsistenciasDadas.NoLoSe, StringComparison.OrdinalIgnoreCase))
            {
                equipo = null;
            }

            if (equipo != null && equipo.Length > AsistenciasDadas.LargoEquipo)
            {
                equipo = equipo.Substring(0, AsistenciasDadas.LargoEquipo).TrimEnd();
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.AsistenciaDada(conexion))
                {
                    return FaltaLa22;
                }

                SesionAgenda bloque = SesionDeHoy.BloqueEnCurso(conexion, idClase, idProfesor);
                int? sesion = bloque == null ? null
                    : SesionDeHoy.Leer(conexion, bloque) ?? SesionDeHoy.Crear(conexion, bloque, idProfesor, "al dar una asistencia");
                if (!sesion.HasValue)
                {
                    return DarFueraDeClase;
                }

                bool conBajas = Esquema.TieneBajas(conexion);
                // Con TIMESTAMP: en la base de la escuela hora_salida es texto (VARCHAR).
                // Inicio <= ahora < fin, la misma ventana que BloqueEnCurso: en el
                // segundo en que termina la clase ya no se da.
                try
                {
                    using (MySqlCommand comando = new MySqlCommand(@"
                        INSERT INTO asistencias_dadas
                            (fk_clase, fecha, fk_alumno, fk_codigo_accesos, fk_profesor, dada_en, motivo, numero_computadora, comentario, dada_desde, activa)
                        SELECT c.idclases, ca.fecha, u.idusuarios, ca.idcodigos_accesos, @Profesor, NOW(), @Motivo, @Equipo, @Comentario, @Desde, 1
                        FROM codigos_accesos ca
                        INNER JOIN clases c   ON c.idclases = @Clase
                        INNER JOIN usuarios u ON u.idusuarios = @Alumno AND u.fk_tipo_usuario = 1" + (conBajas ? " AND u.activo = 1" : "") + @"
                        INNER JOIN usuarios p ON p.idusuarios = @Profesor" + (conBajas ? " AND p.activo = 1" : "") + @"
                        WHERE ca.idcodigos_accesos = @Sesion AND ca.fecha = CURDATE()
                          AND NOW() >= TIMESTAMP(ca.fecha, ca.hora_entrada) AND NOW() < TIMESTAMP(ca.fecha, ca.hora_salida)
                          AND ca.usuarios_idusuarios = @Profesor AND c.usuarios_idusuarios = @Profesor
                          AND ca.materias_id_materia = c.materias_id_materia AND ca.grupos_idgrupos = c.grupos_idgrupos
                          AND c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta
                          AND NOT EXISTS (SELECT 1 FROM registros_bitacoras rb
                                          INNER JOIN codigos_accesos s ON s.idcodigos_accesos = rb.fk_codigo_accesos
                                          WHERE rb.fk_usuario = u.idusuarios AND s.fecha = CURDATE()
                                            AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia
                                            AND s.grupos_idgrupos = c.grupos_idgrupos)", conexion))
                    {
                        comando.Parameters.AddWithValue("@Clase", idClase);
                        comando.Parameters.AddWithValue("@Alumno", idAlumno);
                        comando.Parameters.AddWithValue("@Profesor", idProfesor);
                        comando.Parameters.AddWithValue("@Sesion", sesion.Value);
                        comando.Parameters.AddWithValue("@Motivo", motivo);
                        comando.Parameters.AddWithValue("@Equipo", equipo == null ? (object)DBNull.Value : equipo);
                        comando.Parameters.AddWithValue("@Comentario", comentario == null ? (object)DBNull.Value : comentario);
                        comando.Parameters.AddWithValue("@Desde", AsistenciasDadas.Desde());
                        if (comando.ExecuteNonQuery() == 1)
                        {
                            Log.Info("Asistencia dada por el profesor id " + idProfesor + " al alumno id " + idAlumno + " en la clase " + idClase
                                + " (sesion " + sesion.Value + ", motivo " + motivo + ").");
                            return null;
                        }
                    }
                }
                catch (MySqlException ex) when (ex.Number == 1062)
                {
                    return "Ya le diste la asistencia hoy en esta clase.";
                }

                return PorQueNoSeDio(conexion, idClase, idAlumno, sesion.Value, bloque, conBajas);
            }
        }

        /// <summary>
        /// Por que el INSERT de DarAsistencia no guardo nada, en este orden: la clase
        /// ya termino, la cuenta no es de un alumno activo, hoy registro y se le quito
        /// la asistencia (db/17), hoy ya registro. Un SELECT de subconsultas sin FROM
        /// (vale en MariaDB 5.5 porque no lleva WHERE).
        /// </summary>
        private static string PorQueNoSeDio(MySqlConnection conexion, int idClase, int idAlumno, int sesion, SesionAgenda bloque, bool conBajas)
        {
            const string Terna = "s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia AND s.grupos_idgrupos = c.grupos_idgrupos";
            bool conRetiro = Esquema.AsistenciaRetirable(conexion);
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT (SELECT COUNT(*) FROM codigos_accesos ca
                         WHERE ca.idcodigos_accesos = @Sesion AND ca.fecha = CURDATE()
                           AND NOW() >= TIMESTAMP(ca.fecha, ca.hora_entrada) AND NOW() < TIMESTAMP(ca.fecha, ca.hora_salida)) AS en_curso,
                       (SELECT TIME(ca.hora_salida) FROM codigos_accesos ca WHERE ca.idcodigos_accesos = @Sesion) AS salida,
                       (SELECT COUNT(*) FROM usuarios u
                         WHERE u.idusuarios = @Alumno AND u.fk_tipo_usuario = 1" + (conBajas ? " AND u.activo = 1" : "") + @") AS estudiante_activo,
                       (SELECT COUNT(*) FROM registros_bitacoras rb
                          INNER JOIN codigos_accesos s ON s.idcodigos_accesos = rb.fk_codigo_accesos
                          INNER JOIN clases c ON c.idclases = @Clase
                         WHERE rb.fk_usuario = @Alumno AND s.fecha = CURDATE() AND " + Terna + @") AS bitacoras_hoy,
                       " + (conRetiro
                            ? @"(SELECT COUNT(*) FROM asistencias_retiradas ar
                          INNER JOIN registros_bitacoras rb ON rb.idregistros_bitacoras = ar.fk_bitacora
                          INNER JOIN codigos_accesos s ON s.idcodigos_accesos = rb.fk_codigo_accesos
                          INNER JOIN clases c ON c.idclases = @Clase
                         WHERE rb.fk_usuario = @Alumno AND s.fecha = CURDATE() AND " + Terna + ")"
                            : "0") + " AS retiradas_hoy", conexion))
            {
                comando.Parameters.AddWithValue("@Sesion", sesion);
                comando.Parameters.AddWithValue("@Alumno", idAlumno);
                comando.Parameters.AddWithValue("@Clase", idClase);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        if (Convert.ToInt32(reader["en_curso"]) == 0)
                        {
                            // La hora de salida de su sesion (la misma que la del bloque, salvo que la cambiaran).
                            int salida = reader.GetOrdinal("salida");
                            TimeSpan fin = reader.IsDBNull(salida) ? bloque.Fin : reader.GetTimeSpan(salida);
                            return "La clase terminó a las " + HorariosDatos.Hora(fin) + ": la asistencia solo se puede dar mientras dura.";
                        }

                        if (Convert.ToInt32(reader["estudiante_activo"]) == 0)
                        {
                            return "Esa cuenta no es de un estudiante activo: revisa la matrícula o avisa a la administración de DigBit.";
                        }

                        if (Convert.ToInt32(reader["retiradas_hoy"]) > 0)
                        {
                            return "Hoy registró su bitácora y le quitaste la asistencia: si está en el salón, devuélvesela desde su casilla de hoy.";
                        }

                        if (Convert.ToInt32(reader["bitacoras_hoy"]) > 0)
                        {
                            return "Ya registró su bitácora hoy en esta clase: ya tiene asistencia. Actualiza la lista (F5).";
                        }
                    }
                }
            }

            return "No se pudo dar la asistencia: actualiza la lista (F5) y vuelve a intentarlo.";
        }

        /// <summary>
        /// Anula la asistencia que este profesor le dio hoy en esta clase a un alumno
        /// que ya no esta en el salon (db/22): no la borra, la deja con activa = NULL,
        /// quien y cuando. Solo mientras dura un bloque de la clase (reloj del
        /// servidor). Se puede volver a dar (fila nueva). Devuelve null si se hizo, o
        /// por que no.
        /// </summary>
        public string QuitarLaQueDiste(int idClase, int idProfesor, int idAlumno)
        {
            Permisos.Exigir("Quitar una asistencia dada");
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                if (!Esquema.AsistenciaDada(conexion))
                {
                    return FaltaLa22;
                }

                SesionAgenda bloque = SesionDeHoy.BloqueEnCurso(conexion, idClase, idProfesor);
                if (bloque == null)
                {
                    return CambiarFueraDeClase;
                }

                // La ventana del bloque se leyo hace un momento en esta misma conexion. En
                // segundos: un TimeSpan llega como texto ('0 09:30:00.000000') y CURTIME()
                // se compararia con el como texto.
                using (MySqlCommand comando = new MySqlCommand(@"
                    UPDATE asistencias_dadas
                       SET activa = NULL, anulada_en = NOW(), anulada_por = @Profesor
                     WHERE fk_clase = @Clase AND fk_alumno = @Alumno AND fecha = CURDATE() AND activa = 1
                       AND fk_profesor = @Profesor
                       AND TIME_TO_SEC(CURTIME()) >= @Inicio AND TIME_TO_SEC(CURTIME()) < @Fin", conexion))
                {
                    comando.Parameters.AddWithValue("@Profesor", idProfesor);
                    comando.Parameters.AddWithValue("@Clase", idClase);
                    comando.Parameters.AddWithValue("@Alumno", idAlumno);
                    comando.Parameters.AddWithValue("@Inicio", (int)bloque.Inicio.TotalSeconds);
                    comando.Parameters.AddWithValue("@Fin", (int)bloque.Fin.TotalSeconds);
                    if (comando.ExecuteNonQuery() == 0)
                    {
                        // Si aun la tiene, es que la clase termino entre las dos sentencias.
                        using (MySqlCommand sigue = new MySqlCommand(
                            "SELECT COUNT(*) FROM asistencias_dadas WHERE fk_clase = @Clase AND fk_alumno = @Alumno AND fecha = CURDATE() AND activa = 1 AND fk_profesor = @Profesor",
                            conexion))
                        {
                            sigue.Parameters.AddWithValue("@Profesor", idProfesor);
                            sigue.Parameters.AddWithValue("@Clase", idClase);
                            sigue.Parameters.AddWithValue("@Alumno", idAlumno);
                            return Convert.ToInt32(sigue.ExecuteScalar()) > 0
                                ? "La clase terminó a las " + HorariosDatos.Hora(bloque.Fin) + ": la asistencia solo se puede cambiar mientras dura."
                                : "Ya no tenía la asistencia que le diste: actualiza la lista (F5).";
                        }
                    }
                }
            }

            Log.Info("Asistencia dada anulada por el profesor id " + idProfesor + " al alumno id " + idAlumno + " en la clase " + idClase + ".");
            return null;
        }

        /// <summary>
        /// Las asistencias dadas que cuentan (no anuladas) con fecha entre desde y
        /// hasta, con las horas y el laboratorio de su sesion: las de una clase, o
        /// las de todas con idClase null (el calculo en lote de Estadisticas: se
        /// agrupan por ClaseId y cada grupo pasa por AplicarDadas). Una consulta;
        /// quien llama ya sabe que la base tiene db/22 (Esquema.AsistenciaDada).
        /// </summary>
        internal static List<AsistenciaDada> LeerDadas(MySqlConnection conexion, int? idClase, DateTime desde, DateTime hasta)
        {
            List<AsistenciaDada> dadas = new List<AsistenciaDada>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT d.idasistencias_dadas, d.fk_clase, d.fecha, d.fk_alumno, d.fk_codigo_accesos, d.fk_profesor, d.dada_en, d.motivo,
                       d.numero_computadora, d.comentario, TIME(ca.hora_entrada) AS entrada, TIME(ca.hora_salida) AS salida,
                       ca.laboratorios_idlaboratorios AS lab
                FROM asistencias_dadas d
                INNER JOIN codigos_accesos ca ON ca.idcodigos_accesos = d.fk_codigo_accesos
                WHERE " + (idClase.HasValue ? "d.fk_clase = @Clase AND " : "") + "d.activa = 1 AND d.fecha BETWEEN @Desde AND @Hasta", conexion))
            {
                if (idClase.HasValue)
                {
                    comando.Parameters.AddWithValue("@Clase", idClase.Value);
                }

                comando.Parameters.AddWithValue("@Desde", desde.Date);
                comando.Parameters.AddWithValue("@Hasta", hasta.Date);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        dadas.Add(LeerDada(reader));
                    }
                }
            }

            return dadas;
        }

        /// <summary>Una fila con las columnas de LeerDadas (por nombre).</summary>
        internal static AsistenciaDada LeerDada(MySqlDataReader reader)
        {
            int equipo = reader.GetOrdinal("numero_computadora");
            int comentario = reader.GetOrdinal("comentario");
            int entrada = reader.GetOrdinal("entrada");
            int salida = reader.GetOrdinal("salida");
            int lab = reader.GetOrdinal("lab");
            return new AsistenciaDada
            {
                Id = Convert.ToInt32(reader["idasistencias_dadas"]),
                ClaseId = Convert.ToInt32(reader["fk_clase"]),
                Fecha = Convert.ToDateTime(reader["fecha"], CultureInfo.InvariantCulture).Date,
                AlumnoId = Convert.ToInt32(reader["fk_alumno"]),
                SesionId = Convert.ToInt32(reader["fk_codigo_accesos"]),
                ProfesorId = Convert.ToInt32(reader["fk_profesor"]),
                DadaEn = Convert.ToDateTime(reader["dada_en"], CultureInfo.InvariantCulture),
                Motivo = Convert.ToString(reader["motivo"], CultureInfo.InvariantCulture),
                Equipo = reader.IsDBNull(equipo) ? null : reader.GetString(equipo),
                Comentario = reader.IsDBNull(comentario) ? null : reader.GetString(comentario),
                Inicio = reader.IsDBNull(entrada) ? TimeSpan.Zero : reader.GetTimeSpan(entrada),
                Fin = reader.IsDBNull(salida) ? TimeSpan.Zero : reader.GetTimeSpan(salida),
                LaboratorioId = reader.IsDBNull(lab) ? 0 : Convert.ToInt32(reader.GetValue(lab))
            };
        }

        /// <summary>
        /// Pone en la lista las asistencias que dio el profesor (db/22). Va despues del
        /// reparto de bitacoras y antes de Retiradas.ExceptWith(Presente): cada una es
        /// asistencia de ese dia (Presente); si ese dia no tenia una bitacora que
        /// contara, el dia queda en SinBitacora; quien aun no estaba entra a la lista
        /// (su nombre lo pone LeerNombres despues). No pasa por el reparto de clases
        /// seguidas: la da el profesor a su clase. Todas tienen que ser de la misma
        /// clase. Sin consultas: Estadisticas la llama igual en su calculo en lote.
        /// </summary>
        internal static void AplicarDadas(Dictionary<int, AlumnoDeAsistencia> alumnos, IEnumerable<AsistenciaDada> dadas)
        {
            foreach (AsistenciaDada dada in dadas)
            {
                AlumnoDeAsistencia alumno;
                if (!alumnos.TryGetValue(dada.AlumnoId, out alumno))
                {
                    alumno = new AlumnoDeAsistencia { Id = dada.AlumnoId };
                    alumnos[dada.AlumnoId] = alumno;
                }

                DateTime dia = dada.Fecha.Date;
                alumno.Dadas[dia] = dada;
                if (!alumno.Presente.Contains(dia))
                {
                    alumno.SinBitacora.Add(dia);
                }

                alumno.Presente.Add(dia);
            }
        }

        /// <summary>
        /// Los dias de esas asistencias dadas que no estan en conSesion (los dias con
        /// alguna sesion de la clase, real o de SinSesion), con la entrada de su sesion
        /// como inicio: una dada nunca se pierde aunque su sesion se moviera. Lo normal
        /// es que no haya ninguno.
        /// </summary>
        internal static List<DiaDeAsistencia> DiasSoloDeDadas(IEnumerable<AsistenciaDada> dadas, ICollection<DateTime> conSesion)
        {
            return dadas
                .Where(d => !conSesion.Contains(d.Fecha.Date))
                .GroupBy(d => d.Fecha.Date)
                .Select(g => new DiaDeAsistencia { Fecha = g.Key, Inicio = g.Min(d => d.Inicio) })
                .ToList();
        }

        /// <summary>
        /// En un renglon y sin lo que la tabla utf8 de 3 bytes no guarda (emojis,
        /// caracteres de control): los saltos de linea, tabuladores y espacios
        /// seguidos quedan en un espacio.
        /// </summary>
        private static string Limpio(string texto)
        {
            return string.Join(" ", (HistorialCambios.Limpiar(texto, int.MaxValue) ?? "").Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// El grupo de cada alumno (carrera_grupo_semestre: su columna
        /// usuarios_numero_identificador guarda el id). Si tiene varias filas con
        /// grupo, queda la de mayor id (la ultima en el ORDER BY), como en E7.
        /// </summary>
        private static Dictionary<int, int> LeerGrupos(MySqlConnection conexion, List<int> alumnos)
        {
            Dictionary<int, int> grupos = new Dictionary<int, int>();
            if (alumnos.Count == 0)
            {
                return grupos;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT usuarios_numero_identificador, grupos_idgrupos FROM carrera_grupo_semestre WHERE grupos_idgrupos IS NOT NULL AND usuarios_numero_identificador IN ("
                + string.Join(",", alumnos.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ") ORDER BY idcarrera_grupo_semestre", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    grupos[reader.GetInt32(0)] = reader.GetInt32(1);
                }
            }

            return grupos;
        }

        /// <summary>
        /// Matricula y "Apellidos Nombre" de las cuentas de alumno de esos ids; las
        /// demas no salen (Calcular las deja fuera de la lista).
        /// </summary>
        private static Dictionary<int, EstudianteLeido> LeerNombres(MySqlConnection conexion, List<int> ids)
        {
            Dictionary<int, EstudianteLeido> estudiantes = new Dictionary<int, EstudianteLeido>();
            if (ids.Count == 0)
            {
                return estudiantes;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idusuarios, numero_identificador, CONCAT_WS(' ', apellido_paterno, apellido_materno, nombre) FROM usuarios "
                + "WHERE fk_tipo_usuario = 1 AND idusuarios IN (" + string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")", conexion))
            using (MySqlDataReader reader = comando.ExecuteReader())
            {
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    estudiantes[id] = new EstudianteLeido
                    {
                        Id = id,
                        Matricula = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Nombre = reader.IsDBNull(2) ? "" : reader.GetString(2)
                    };
                }
            }

            return estudiantes;
        }
    }
}
