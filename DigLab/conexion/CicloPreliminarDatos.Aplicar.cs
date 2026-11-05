using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigLab.Infraestructura;
using MySql.Data.MySqlClient;

namespace DigLab.conexion
{
    /// <summary>
    /// La ultima sesion (o asistencia dada, db/22) de una clase de ahora despues
    /// del ultimo dia que se le quiere dar: con ella no se aplica, porque la clase
    /// no puede terminar antes de su ultima sesion.
    /// </summary>
    public class SesionPosterior
    {
        public DateTime Fecha;
        public string Materia = "";
        public string Grupo = "";
        /// <summary>Lo de ese dia es una asistencia que dio su docente (db/22), no una sesion.</summary>
        public bool EsAsistenciaDada;

        /// <summary>"Bases de datos con ISC-5002".</summary>
        public string Clase
        {
            get { return Materia + " con " + Grupo; }
        }
    }

    /// <summary>
    /// Una clase de la configuracion cuya misma persona docente, grupo y materia
    /// ya esta en Clases y tambien vale en esas fechas (regla R6, la de
    /// HorariosDatos.ExigirSinCruceDeTerna): no deja aplicar.
    /// </summary>
    public class TernaCruzada
    {
        public int CargaId;
        public int ClaseId;
        public string Materia = "";
        public string Grupo = "";
        /// <summary>"Laura Cardenas".</summary>
        public string Profesor = "";
        public DateTime Desde;
        public DateTime Hasta;
    }

    /// <summary>
    /// Un cambio "desde una fecha" (db/20) de una clase de ahora que empieza
    /// despues del ultimo dia que se le da: ya no llegara a darse.
    /// </summary>
    public class CambioQueNoLlega
    {
        public int FranjaId;
        public string Materia = "";
        public string Grupo = "";
        public DateTime Desde;
    }

    /// <summary>Una sesion extra de una clase de ahora despues del ultimo dia que se le da: para el estudiante ya no vale.</summary>
    public class ExtraFuera
    {
        public int ExcepcionId;
        public string Materia = "";
        public string Grupo = "";
        public DateTime Fecha;
    }

    /// <summary>
    /// Todo lo que revisa la pagina de Aplicacion (y otra vez Aplicar, dentro de
    /// su transaccion) para unas fechas: CicloPreliminarDatos.LeerParaAplicar.
    /// Los renglones de "Antes de aplicar" salen de aqui con RevisionAplicacion.
    /// </summary>
    public class DatosDeAplicacion
    {
        /// <summary>El horario en preparacion leido con fechas (con YaProgramadas).</summary>
        public CicloLeido Leido;
        /// <summary>RevisionCiclo.Revisar(Leido): choques (tambien con las YaProgramadas), sin docente, de baja...</summary>
        public RevisionDelCiclo Revision;
        /// <summary>Las fechas con que se leyo.</summary>
        public DatosDeFechas Fechas;
        /// <summary>Hoy segun el servidor.</summary>
        public DateTime Hoy;
        /// <summary>NOW() del servidor al leer (la conexion fija -06:00): con el se cuentan las clases de hoy que ya terminaron.</summary>
        public DateTime Ahora;
        /// <summary>Stopwatch.GetTimestamp() al leer Ahora (0: sin marca): AhoraEstimado avanza con el, no con el reloj del equipo.</summary>
        public long LeidoEnMarca;

        /// <summary>
        /// La hora del servidor en este momento, estimada con lo que paso desde
        /// la lectura (con un cronometro: el reloj del equipo se puede cambiar).
        /// Con ella la pagina tiene al dia "ya terminaron" sin volver a leer;
        /// dentro de la transaccion de Aplicar es casi NOW().
        /// </summary>
        public DateTime AhoraEstimado
        {
            get
            {
                if (LeidoEnMarca == 0)
                {
                    return Ahora;
                }

                long transcurrido = Math.Max(0, System.Diagnostics.Stopwatch.GetTimestamp() - LeidoEnMarca);
                return Ahora + TimeSpan.FromSeconds(transcurrido / (double)System.Diagnostics.Stopwatch.Frequency);
            }
        }

        /// <summary>Con "Ahora mismo": las sesiones de hoy de las clases que se recortan (se quedan como estan, con sus bitacoras).</summary>
        public int SesionesDeHoy;
        /// <summary>Con "Ahora mismo": los estudiantes que registraron hoy en esas sesiones.</summary>
        public int BitacorasDeHoy;
        /// <summary>Con "Ahora mismo" y db/22: las asistencias que dieron hoy sus docentes (las que cuentan).</summary>
        public int DadasDeHoy;
        /// <summary>
        /// De lo de hoy (SesionesDeHoy, BitacorasDeHoy y DadasDeHoy), lo de las
        /// clases cuya misma persona docente, grupo y materia sigue en el horario
        /// nuevo: cuenta en la lista de su clase nueva. Lo de las demas cuenta en
        /// la de su clase de antes (AsistenciaDatos: el dia siguiente a su ultimo).
        /// </summary>
        public int SesionesDeHoySiguen;
        public int BitacorasDeHoySiguen;
        public int DadasDeHoySiguen;
        /// <summary>La base tiene "Falta asignar" en Horario semanal (db/25): lo que le falte a la plantilla pasa alli al aplicar.</summary>
        public bool ConHorasPorAsignar;
        /// <summary>Con "Ahora mismo": los laboratorios del horario nuevo con generacion de codigos (sus clases nuevas tienen otros codigos).</summary>
        public List<string> LaboratoriosConGeneracion = new List<string>();
        /// <summary>Con "Ahora mismo": las clases que empezaron hoy sin nada registrado. Se sustituyen: se quitan.</summary>
        public int EmpezaronHoySeQuitan;
        /// <summary>Con "Ahora mismo": las que empezaron hoy y ya tienen algo registrado. Se sustituyen: quedan terminadas.</summary>
        public int EmpezaronHoyConRegistros;

        /// <summary>Las clases que valen el ultimo dia del horario de ahora y siguen despues: se recortan (tambien las desactivadas).</summary>
        public int Terminan;
        /// <summary>De ellas, las activas.</summary>
        public int TerminanActivas;
        /// <summary>La fecha de fin mas comun de las activas que se recortan.</summary>
        public DateTime? FinMasComun;
        /// <summary>El vigente_desde mas temprano de las activas que valen ese dia: las que se guardan "como quedo" (para su nombre).</summary>
        public DateTime? DesdeMasTemprano;
        /// <summary>Las clases activas que valen el ultimo dia del horario de ahora: las que se guardan "como quedo".</summary>
        public int ValenElFin;

        /// <summary>La ultima sesion despues del ultimo dia de alguna clase que se recorta, o null.</summary>
        public SesionPosterior SesionPosterior;
        public List<TernaCruzada> TernasCruzadas = new List<TernaCruzada>();
        public List<CambioQueNoLlega> CambiosQueNoLlegan = new List<CambioQueNoLlega>();
        public List<ExtraFuera> ExtrasFuera = new List<ExtraFuera>();
        /// <summary>Con "Ahora mismo": las clases que empiezan despues y siguen canceladas un dia en que se quita la extra que ocupaba sus horas (ClasesQueSiguenCanceladas).</summary>
        public List<ExtraFuera> SiguenCanceladas = new List<ExtraFuera>();
        /// <summary>Las personas docentes de la configuracion sin codigo (usuarios.codigo_clase vacio): lo reciben al aplicar.</summary>
        public List<string> DocentesSinCodigo = new List<string>();
        /// <summary>Los nombres de las otras configuraciones aplicadas (el nombre en el registro no se repite).</summary>
        public List<string> NombresAplicadas = new List<string>();
        /// <summary>El nombre que se propone para guardar el horario de ahora "como quedo" (ya libre entre las guardadas).</summary>
        public string PropuestaGuardar = "";

        /// <summary>Los cambios de horario desde una fecha estan encendidos (db/20 y la casilla de Ajustes).</summary>
        public bool ConFecha;
        /// <summary>La base tiene la vigencia de las franjas (db/20).</summary>
        public bool ConVigencia;

        /// <summary>Las clases que se crean (las de la configuracion).</summary>
        public int Clases
        {
            get { return Leido == null ? 0 : Leido.Cargas.Count(c => !c.Quitada); }
        }

        /// <summary>Los horarios (franjas) que se crean.</summary>
        public int Horarios
        {
            get { return Leido == null ? 0 : Leido.Cargas.Where(c => !c.Quitada).Sum(c => c.Franjas.Count); }
        }
    }

    /// <summary>Lo que hizo Aplicar.</summary>
    public class ResultadoAplicar
    {
        public int CicloId;
        /// <summary>El nombre con que quedo en el registro.</summary>
        public string Nombre = "";
        public DateTime Desde;
        public DateTime Hasta;
        public DateTime Fin;
        public int Clases;
        public int Horarios;
        /// <summary>Las clases de antes que ahora terminan en Fin.</summary>
        public int Terminan;
        /// <summary>La guardada "como quedo", si se pidio y ese dia habia clases.</summary>
        public int? GuardadaId;
        public string GuardadaNombre;
        /// <summary>Se pidio guardar el horario de ahora, pero ese dia no tenia clases: no se guardo.</summary>
        public bool SinClasesQueGuardar;
        /// <summary>Las personas docentes que recibieron su codigo al aplicar.</summary>
        public List<string> RecibieronCodigo = new List<string>();
        /// <summary>Empezo al aplicarlo ("Ahora mismo"): Desde es hoy y Fin ayer. Ya no se puede deshacer.</summary>
        public bool EmpezoAhora;
        /// <summary>Con "Ahora mismo": NOW() del servidor al aplicar, con el que se vio que clases de hoy ya habian terminado.</summary>
        public DateTime EmpezoA;
        /// <summary>
        /// Con "Ahora mismo": las clases (franjas) de hoy del horario nuevo que ya
        /// habian terminado, canceladas ese dia como "No tomar en cuenta" (en
        /// Faltas se pueden volver a contar).
        /// </summary>
        public int SinContarHoy;
        /// <summary>Los minutos de "Falta asignar" de la plantilla que pasaron a Horario semanal (db/25).</summary>
        public int MinutosPorAsignar;
        /// <summary>Con "Ahora mismo": las clases que empezaron hoy sin nada registrado, quitadas.</summary>
        public int EmpezaronHoyQuitadas;
        /// <summary>Con "Ahora mismo": las que empezaron hoy con algo registrado, que quedaron terminadas.</summary>
        public int EmpezaronHoyTerminadas;
        /// <summary>Con "Ahora mismo": las sesiones extra de hoy en adelante que nadie abrio, quitadas.</summary>
        public int ExtrasQuitadas;
        /// <summary>Con "Ahora mismo": los horarios aplicados hoy que no llegaron a correr y volvieron a ser plantillas.</summary>
        public List<string> VueltasAPlantilla = new List<string>();
    }

    /// <summary>Lo que hizo Deshacer.</summary>
    public class ResultadoDeshacer
    {
        public string Nombre = "";
        public int ClasesBorradas;
        /// <summary>Las clases de antes que recuperaron su fecha de fin.</summary>
        public int Devueltas;
        /// <summary>Las que no se tocaron porque se les cambio la fecha de fin despues de aplicar.</summary>
        public int NoDevueltas;
        /// <summary>Las que no se tocaron porque ya hay otra igual (misma docente, grupo y materia) en esas fechas (regla R6).</summary>
        public int PorOtraIgual;
    }

    /// <summary>Lo que ensena la pestana Aplicacion de una aplicada que todavia no empieza.</summary>
    public class InfoDeAplicada
    {
        /// <summary>Las clases de antes que se recortaron al aplicarla.</summary>
        public int Recortadas;
        /// <summary>Las clases que creo y siguen en Clases.</summary>
        public int Creadas;
        /// <summary>Por que no se puede deshacer, o null si se puede.</summary>
        public string Motivo;
    }

    /// <summary>
    /// Aplicar y Deshacer una configuracion de horario, y lo que lee la pestana
    /// Aplicacion (especificacion v2, 3.5). Con "Otro dia", Aplicar solo hace INSERT
    /// de clases y de franjas nuevas (sin fecha propia: su vigencia es la de su
    /// clase, asi un DigLab anterior a la 20 no ve dos veces la misma hora) y UPDATE
    /// de vigente_hasta de las clases que valen el ultimo dia del horario de ahora,
    /// nunca a una fecha anterior a su ultima sesion. Nunca pone activa = 0, nunca
    /// borra, nunca cambia franjas, excepciones ni sesiones, y anota un solo renglon
    /// en el historial. Deshacer es lo contrario: borra solo las clases que creo, si
    /// no tienen sesiones ni asistencias dadas, y devuelve su fecha de fin a las que
    /// recorto, si nadie la cambio despues. Todo en una transaccion; solo la
    /// administracion escribe (Permisos.Exigir).
    /// Con "Ahora mismo" (AplicarAhora) el horario nuevo sustituye al de ahora en el
    /// momento, tambien si este empezo hoy (pedido del usuario, 2026-11-04): las de
    /// antes terminan ayer, y las de hoy se quitan o, con algo registrado, quedan sin
    /// dias propios (TerminarYQuitar). Lo ya registrado hoy se queda y cuenta una vez
    /// en las listas de asistencia (AsistenciaDatos: en la clase nueva de la misma
    /// terna o, si no sigue, en la de antes), y quien no alcanzo a registrar en una
    /// clase sin terminar no tiene falta (CancelarLasQueNoTerminaron). Las clases de
    /// hoy del horario nuevo que ya terminaron se cancelan ese dia como "No tomar en
    /// cuenta" (horario_excepciones, sin tabla nueva), asi no salen en Faltas ni en
    /// Alertas y "Volver a contar" las devuelve. Lo que empieza hoy ya no se deshace.
    /// </summary>
    internal partial class CicloPreliminarDatos
    {
        /// <summary>SQL: la clase (alias c) vale el ultimo dia del horario de ahora (@Fin) y sigue despues: se recorta.</summary>
        private const string SeRecorta = "c.vigente_desde <= @Fin AND c.vigente_hasta > @Fin";

        /// <summary>SQL: la clase (alias c) empezo hoy (@Desde con "Ahora mismo") y vale hoy: "Ahora mismo" tambien la sustituye.</summary>
        private const string EmpezoHoy = "c.vigente_desde = @Desde AND c.vigente_hasta >= @Desde";

        // ---------------------------------------------------------------------
        // Lo que lee la pagina de aplicar
        // ---------------------------------------------------------------------

        /// <summary>
        /// Lo que revisa "Antes de aplicar" para esas fechas: el horario en
        /// preparacion con las clases que ya estan en Clases en esas fechas, las
        /// que terminan, sus sesiones posteriores, las ternas cruzadas, los
        /// cambios y las extras que quedan fuera, quien recibe codigo y si los
        /// cambios desde una fecha estan encendidos. Solo lee; la pantalla lo
        /// pide en segundo plano.
        /// </summary>
        public DatosDeAplicacion LeerParaAplicar(int idCiclo, DateTime desde, DateTime hasta, DateTime fin)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LeerParaAplicar(conexion, null, idCiclo, new DatosDeFechas { Desde = desde.Date, Hasta = hasta.Date, Fin = fin.Date });
            }
        }

        /// <summary>
        /// Como LeerParaAplicar, para "Ahora mismo": el primer dia es hoy y el
        /// ultimo del horario de ahora ayer, los dos del servidor (nunca del reloj
        /// de este equipo). Ademas cuenta lo ya registrado hoy en las que se
        /// recortan (SesionesDeHoy, BitacorasDeHoy, DadasDeHoy).
        /// </summary>
        public DatosDeAplicacion LeerParaAplicarAhora(int idCiclo, DateTime hasta)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LeerParaAplicar(conexion, null, idCiclo, FechasDeAhora(Hoy(conexion, null), hasta));
            }
        }

        /// <summary>Las fechas de "Ahora mismo" para ese hoy (del servidor): desde hoy, y el horario de ahora hasta ayer.</summary>
        private static DatosDeFechas FechasDeAhora(DateTime hoy, DateTime hasta)
        {
            return new DatosDeFechas { Desde = hoy.Date, Hasta = hasta.Date, Fin = hoy.Date.AddDays(-1), EmpiezaAhora = true };
        }

        /// <summary>
        /// El motivo de las cancelaciones de las clases de hoy que ya terminaron al
        /// aplicar con "Ahora mismo": "Antes de aplicar el horario <<nombre>> (12:40)"
        /// con la marca de "No tomar en cuenta" (Faltas las reconoce y las puede
        /// volver a contar).
        /// </summary>
        internal static string MotivoAntesDeAplicar(string nombre, DateTime momento)
        {
            return "Antes de aplicar el horario «" + nombre + "» (" + momento.ToString("HH:mm", CultureInfo.InvariantCulture) + ")"
                + HorariosDatos.MarcaNoTomarEnCuenta;
        }

        /// <summary>
        /// La fecha de fin mas comun de las clases activas que valen hoy (para
        /// proponer el primer dia del horario nuevo: el dia siguiente), o null.
        /// </summary>
        public DateTime? FinMasComunDeHoy()
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT c.vigente_hasta FROM clases c
                 WHERE c.activa = 1 AND CURDATE() BETWEEN c.vigente_desde AND c.vigente_hasta
                 GROUP BY c.vigente_hasta ORDER BY COUNT(*) DESC, c.vigente_hasta DESC LIMIT 1", conexion))
            {
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor).Date;
            }
        }

        internal static DatosDeAplicacion LeerParaAplicar(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, DatosDeFechas fechas)
        {
            DatosDeAplicacion datos = new DatosDeAplicacion { Fechas = fechas };
            datos.Leido = Leer(conexion, transaccion, idCiclo, fechas);
            datos.Hoy = datos.Leido.Hoy;
            using (MySqlCommand comando = new MySqlCommand("SELECT NOW()", conexion, transaccion))
            {
                datos.Ahora = Convert.ToDateTime(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
                datos.LeidoEnMarca = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            datos.Revision = RevisionCiclo.Revisar(datos.Leido);
            datos.ConVigencia = Esquema.VigenciaDeFranjas(conexion, transaccion);
            datos.ConFecha = VigenciaFranjas.ConFecha(conexion, transaccion);
            datos.ConHorasPorAsignar = Esquema.HorasPorAsignar(conexion, transaccion);
            bool conDadas = Esquema.AsistenciaDada(conexion, transaccion);
            DateTime diaDeAhora = DiaDelHorarioDeAhora(conexion, transaccion, fechas);
            // Las que este horario deja sin dias desde hoy: con "Ahora mismo", tambien las de hoy.
            string lasQueTerminan = fechas.EmpiezaAhora ? "((" + SeRecorta + ") OR (" + EmpezoHoy + "))" : SeRecorta;
            Action<MySqlCommand> conFechas = c =>
            {
                c.Parameters.AddWithValue("@C", idCiclo);
                c.Parameters.AddWithValue("@Desde", fechas.Desde.Date);
                c.Parameters.AddWithValue("@Hasta", fechas.Hasta.Date);
                c.Parameters.AddWithValue("@Fin", fechas.Fin.Date);
                c.Parameters.AddWithValue("@DiaDeAhora", diaDeAhora);
                // Lo que impide aplicar es lo registrado despues de este dia: el
                // ultimo del horario de ahora o, con "Ahora mismo", hoy (lo de hoy
                // paso antes de aplicar y se queda como esta).
                c.Parameters.AddWithValue("@Limite", fechas.EmpiezaAhora ? fechas.Desde.Date : fechas.Fin.Date);
            };

            // 1) Las que terminan: todas las que valen ese dia y siguen despues
            //    (tambien una futura que empiece antes y las desactivadas).
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT COUNT(*) AS n, COALESCE(SUM(c.activa = 1), 0) AS activas,
                       (SELECT MIN(v.vigente_desde) FROM clases v WHERE v.activa = 1 AND @DiaDeAhora BETWEEN v.vigente_desde AND v.vigente_hasta) AS desde_min,
                       (SELECT COUNT(*) FROM clases v WHERE v.activa = 1 AND @DiaDeAhora BETWEEN v.vigente_desde AND v.vigente_hasta) AS valen
                FROM clases c WHERE " + SeRecorta, conexion, transaccion))
            {
                conFechas(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        datos.Terminan = Convert.ToInt32(reader["n"]);
                        datos.TerminanActivas = Convert.ToInt32(reader["activas"]);
                        datos.DesdeMasTemprano = Fecha(reader, "desde_min");
                        datos.ValenElFin = Convert.ToInt32(reader["valen"]);
                    }
                }
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT c.vigente_hasta FROM clases c WHERE c.activa = 1 AND " + SeRecorta + @"
                 GROUP BY c.vigente_hasta ORDER BY COUNT(*) DESC, c.vigente_hasta DESC LIMIT 1", conexion, transaccion))
            {
                conFechas(comando);
                object valor = comando.ExecuteScalar();
                datos.FinMasComun = valor == null || valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor).Date;
            }

            // 2) La ultima sesion posterior al ultimo dia de las que se recortan
            //    (con "Ahora mismo", posterior a hoy) y, con "Ahora mismo", lo de hoy.
            datos.SesionPosterior = UltimaSesionDespues(conexion, transaccion, conFechas, conDadas, lasQueTerminan);
            PlanDeBaja deHoy = null;
            if (fechas.EmpiezaAhora)
            {
                LeerRegistradoHoy(conexion, transaccion, conFechas, conDadas, datos);
                LeerLaboratoriosConGeneracion(conexion, transaccion, idCiclo, datos);
                deHoy = LasDeHoy(conexion, transaccion, fechas.Desde, conDadas, false);
                datos.EmpezaronHoySeQuitan = deHoy.SeQuitan.Count;
                datos.EmpezaronHoyConRegistros = deHoy.ConRegistrosDeHoy.Count;
            }

            // 3) R6: la misma terna ya esta en Clases en esas fechas. Las que se
            //    recortan no hace falta mirarlas: terminan en @Fin, antes de @Desde. Con
            //    "Ahora mismo", las que empezaron hoy tampoco: se sustituyen.
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT cc.idcarga, c.idclases, c.vigente_desde, c.vigente_hasta, m.nombre_materia, g.nombre_grupo,
                       u.nombre AS p_nombre, u.apellido_paterno AS p_paterno
                FROM ciclo_cargas cc
                INNER JOIN clases c   ON c.usuarios_idusuarios = cc.profesor AND c.grupos_idgrupos = cc.grupo AND c.materias_id_materia = cc.materia
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                INNER JOIN usuarios u ON u.idusuarios = c.usuarios_idusuarios
                WHERE cc.idciclo = @C AND cc.quitada = 0 AND c.activa = 1
                  AND c.vigente_desde > " + (fechas.EmpiezaAhora ? "@Desde" : "@Fin") + @" AND c.vigente_desde <= @Hasta AND c.vigente_hasta >= @Desde
                ORDER BY c.vigente_desde, m.nombre_materia, g.nombre_grupo", conexion, transaccion))
            {
                conFechas(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        datos.TernasCruzadas.Add(new TernaCruzada
                        {
                            CargaId = reader.GetInt32("idcarga"),
                            ClaseId = reader.GetInt32("idclases"),
                            Desde = reader.GetDateTime("vigente_desde").Date,
                            Hasta = reader.GetDateTime("vigente_hasta").Date,
                            Materia = Texto(reader, "nombre_materia"),
                            Grupo = Texto(reader, "nombre_grupo"),
                            Profesor = HorariosDatos.NombreCorto(Texto(reader, "p_nombre"), Texto(reader, "p_paterno"))
                        });
                    }
                }
            }

            // 4) Cambios "desde una fecha" (db/20) de las que se recortan que ya no llegan.
            if (datos.ConVigencia)
            {
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT h.idhorarios, h.vigente_desde, m.nombre_materia, g.nombre_grupo
                    FROM horarios h
                    INNER JOIN clases c   ON c.idclases = h.clases_idclases
                    INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                    INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                    WHERE c.activa = 1 AND " + lasQueTerminan + @" AND h.vigente_desde > @Fin AND h.vigente_desde <= c.vigente_hasta
                    ORDER BY h.vigente_desde, m.nombre_materia, g.nombre_grupo", conexion, transaccion))
                {
                    conFechas(comando);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            datos.CambiosQueNoLlegan.Add(new CambioQueNoLlega
                            {
                                FranjaId = reader.GetInt32("idhorarios"),
                                Desde = reader.GetDateTime("vigente_desde").Date,
                                Materia = Texto(reader, "nombre_materia"),
                                Grupo = Texto(reader, "nombre_grupo")
                            });
                        }
                    }
                }
            }

            // 5) Sesiones extra de las que se recortan despues de su ultimo dia.
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT x.idhorario_excepciones, x.fecha, m.nombre_materia, g.nombre_grupo
                FROM horario_excepciones x
                INNER JOIN clases c   ON c.idclases = x.clases_idclases
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                WHERE x.tipo = 'extra' AND x.fecha > @Fin AND x.fecha <= c.vigente_hasta AND c.activa = 1 AND " + lasQueTerminan + @"
                ORDER BY x.fecha, m.nombre_materia, g.nombre_grupo", conexion, transaccion))
            {
                conFechas(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        datos.ExtrasFuera.Add(new ExtraFuera
                        {
                            ExcepcionId = reader.GetInt32("idhorario_excepciones"),
                            Fecha = reader.GetDateTime("fecha").Date,
                            Materia = Texto(reader, "nombre_materia"),
                            Grupo = Texto(reader, "nombre_grupo")
                        });
                    }
                }
            }

            // 5b) Con "Ahora mismo": lo que esas extras dejan cancelado en las clases que siguen.
            if (fechas.EmpiezaAhora)
            {
                datos.SiguenCanceladas = ClasesQueSiguenCanceladas(conexion, transaccion, fechas.Desde);
            }

            // 6) Quien recibe codigo (db/09: sin ella Aplicar se niega).
            if (Esquema.CodigoPorProfesor(conexion, transaccion))
            {
                foreach (KeyValuePair<int, string> docente in DocentesSinCodigo(conexion, transaccion, idCiclo))
                {
                    datos.DocentesSinCodigo.Add(docente.Value);
                }
            }

            // 7) Los nombres de las otras aplicadas y la propuesta para la guardada "como quedo".
            //    La que este horario devuelve a plantilla ya no ocupa su nombre.
            HashSet<int> vuelven = deHoy == null ? new HashSet<int>() : AplicadasQueVuelven(conexion, transaccion, fechas.Desde, deHoy.SeQuitan);
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idciclo, nombre FROM ciclos_preliminares WHERE estado = 'publicado' AND idciclo <> @C ORDER BY idciclo", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!vuelven.Contains(reader.GetInt32(0)))
                        {
                            datos.NombresAplicadas.Add(reader.GetString(1));
                        }
                    }
                }
            }

            datos.PropuestaGuardar = NombreLibre(conexion, transaccion,
                NombrePropuesto(datos.DesdeMasTemprano ?? diaDeAhora, diaDeAhora), EstadoConfiguracion.Guardada);
            return datos;
        }

        /// <summary>
        /// El dia cuyas clases son "el horario de ahora", el que se guarda como quedo: con "Ahora
        /// mismo", hoy, para que entre lo que empezo hoy; si hoy no vale ninguna, o con "Otro
        /// dia", el ultimo dia del horario de ahora; y si ese dia tampoco (las clases terminaron un
        /// sabado y el ultimo dia quedo en domingo, o hay vacaciones de por medio), de su ultima
        /// semana con clases, el dia en que valian mas: se guarda por omision y ya no hay otro
        /// boton para guardarlo, y no todas las clases terminan el mismo dia.
        /// </summary>
        private static DateTime DiaDelHorarioDeAhora(MySqlConnection conexion, MySqlTransaction transaccion, DatosDeFechas fechas)
        {
            Func<DateTime, bool> tieneClases = dia => ValorEnLaBase(conexion, transaccion,
                "SELECT 1 FROM clases c WHERE c.activa = 1 AND @Dia BETWEEN c.vigente_desde AND c.vigente_hasta LIMIT 1",
                c => c.Parameters.AddWithValue("@Dia", dia.Date)) != null;
            if (fechas.EmpiezaAhora && tieneClases(fechas.Desde))
            {
                return fechas.Desde.Date;
            }

            if (tieneClases(fechas.Fin))
            {
                return fechas.Fin.Date;
            }

            object ultimo = ValorEnLaBase(conexion, transaccion,
                "SELECT MAX(c.vigente_hasta) FROM clases c WHERE c.activa = 1 AND c.vigente_desde <= c.vigente_hasta AND c.vigente_hasta <= @Fin",
                c => c.Parameters.AddWithValue("@Fin", fechas.Fin.Date));
            if (ultimo == null)
            {
                return fechas.Fin.Date;
            }

            // Sin ir mas atras que la ultima clase que empezo: lo de antes es otro horario, aunque tuviera mas.
            DateTime ultimoDia = Convert.ToDateTime(ultimo, CultureInfo.InvariantCulture).Date;
            object conMas = ValorEnLaBase(conexion, transaccion, @"
                SELECT c.vigente_hasta FROM clases c
                 WHERE c.activa = 1 AND c.vigente_desde <= c.vigente_hasta
                   AND c.vigente_hasta BETWEEN DATE_SUB(@Ultimo, INTERVAL 6 DAY) AND @Ultimo
                   AND c.vigente_hasta >= (SELECT MAX(u.vigente_desde) FROM clases u WHERE u.activa = 1 AND @Ultimo BETWEEN u.vigente_desde AND u.vigente_hasta)
                 GROUP BY c.vigente_hasta
                 ORDER BY (SELECT COUNT(*) FROM clases v WHERE v.activa = 1 AND c.vigente_hasta BETWEEN v.vigente_desde AND v.vigente_hasta) DESC, c.vigente_hasta DESC
                 LIMIT 1", c => c.Parameters.AddWithValue("@Ultimo", ultimoDia));
            return conMas == null ? ultimoDia : Convert.ToDateTime(conMas, CultureInfo.InvariantCulture).Date;
        }

        /// <summary>
        /// La ultima sesion (con db/22, tambien una asistencia dada) despues de
        /// @Limite (@Fin o, con "Ahora mismo", hoy) de las clases que se
        /// recortan, o null. Son las de
        /// VigenciaFranjas.SesionDeLaClase y DiaDeSesion, pero por partes que
        /// usan los indices (enlazadas a una de sus franjas, a una de sus extras,
        /// o sin enlace de su terna dentro de su vigencia), como
        /// HorariosDatos.AnotarSesionesDeClases: con el OR de SesionDeLaClase,
        /// MariaDB 5.5 recorreria codigos_accesos entera por cada clase.
        /// </summary>
        /// <param name="lasClases">Las clases que se miran (SQL, alias c); por omision, las que se recortan.</param>
        private static SesionPosterior UltimaSesionDespues(MySqlConnection conexion, MySqlTransaction transaccion, Action<MySqlCommand> conFechas, bool conDadas,
            string lasClases = SeRecorta)
        {
            string dia = VigenciaFranjas.DiaDeSesion("s");
            string nombres = @"
                INNER JOIN materias m ON m.id_materia = c.materias_id_materia
                INNER JOIN grupos g   ON g.idgrupos = c.grupos_idgrupos
                WHERE " + lasClases;
            string sql = @"
                (SELECT " + dia + @" AS dia, 0 AS dada, m.nombre_materia, g.nombre_grupo
                   FROM clases c
                   INNER JOIN horarios hh       ON hh.clases_idclases = c.idclases
                   INNER JOIN codigos_accesos s ON s.horarios_idhorarios = hh.idhorarios" + nombres + " AND " + dia + @" > @Limite
                  ORDER BY 1 DESC LIMIT 1)
                UNION ALL
                (SELECT " + dia + @", 0, m.nombre_materia, g.nombre_grupo
                   FROM clases c
                   INNER JOIN horario_excepciones xx ON xx.clases_idclases = c.idclases
                   INNER JOIN codigos_accesos s      ON s.horario_excepciones_id = xx.idhorario_excepciones" + nombres + " AND " + dia + @" > @Limite
                  ORDER BY 1 DESC LIMIT 1)
                UNION ALL
                (SELECT " + dia + @", 0, m.nombre_materia, g.nombre_grupo
                   FROM clases c
                   INNER JOIN codigos_accesos s
                           ON s.horarios_idhorarios IS NULL AND s.horario_excepciones_id IS NULL
                          AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia
                          AND s.grupos_idgrupos = c.grupos_idgrupos AND " + dia + " BETWEEN c.vigente_desde AND c.vigente_hasta" + nombres + " AND " + dia + @" > @Limite
                  ORDER BY 1 DESC LIMIT 1)"
                + (conDadas ? @"
                UNION ALL
                (SELECT ad.fecha, 1, m.nombre_materia, g.nombre_grupo
                   FROM clases c
                   INNER JOIN asistencias_dadas ad ON ad.fk_clase = c.idclases" + nombres + @" AND ad.fecha > @Limite
                  ORDER BY 1 DESC LIMIT 1)" : "");
            SesionPosterior ultima = null;
            using (MySqlCommand comando = new MySqlCommand(sql, conexion, transaccion))
            {
                conFechas(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(0))
                        {
                            continue;
                        }

                        DateTime fecha = Convert.ToDateTime(reader.GetValue(0)).Date;
                        if (ultima == null || fecha > ultima.Fecha)
                        {
                            ultima = new SesionPosterior
                            {
                                Fecha = fecha,
                                EsAsistenciaDada = Convert.ToInt32(reader.GetValue(1)) == 1,
                                Materia = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Grupo = reader.IsDBNull(3) ? "" : reader.GetString(3)
                            };
                        }
                    }
                }
            }

            return ultima;
        }

        /// <summary>
        /// Con "Ahora mismo": lo ya registrado hoy (@Desde) en las clases que se
        /// recortan, que se queda como esta: sus sesiones (por las mismas partes
        /// que UltimaSesionDespues), los estudiantes que registraron en ellas y,
        /// con db/22, las asistencias que dieron sus docentes (las que cuentan).
        /// Aparte, cuanto de eso es de una clase cuya misma persona docente, grupo
        /// y materia esta en el horario nuevo (cuenta en la lista de la clase
        /// nueva; lo demas, en la de la clase de antes). Solo lee.
        /// </summary>
        private static void LeerRegistradoHoy(MySqlConnection conexion, MySqlTransaction transaccion, Action<MySqlCommand> conFechas, bool conDadas,
            DatosDeAplicacion datos)
        {
            string dia = VigenciaFranjas.DiaDeSesion("s");
            string alumnos = "(SELECT COUNT(DISTINCT rb.fk_usuario) FROM registros_bitacoras rb WHERE rb.fk_codigo_accesos = s.idcodigos_accesos)";
            // 1 si la terna de la clase (alias c) sigue en el horario nuevo.
            string sigue = @"IF((SELECT COUNT(*) FROM ciclo_cargas k
                                  WHERE k.idciclo = @C AND k.quitada = 0 AND k.profesor = c.usuarios_idusuarios
                                    AND k.grupo = c.grupos_idgrupos AND k.materia = c.materias_id_materia) > 0, 1, 0)";
            // Las que se recortan, las que empezaron hoy y las que ya terminaron ayer: una baja
            // de hoy (o un "Ahora mismo" anterior) ya les quito el dia, tambien a las que se
            // quedaron sin dias propios.
            string lasDeHoy = "((" + SeRecorta + ") OR (" + EmpezoHoy + ") OR (c.vigente_hasta = @Fin AND c.vigente_desde <= @Desde))";
            string deHoy = " WHERE " + lasDeHoy + " AND " + dia + " = @Desde";
            // UNION (sin ALL): una sesion que saliera por dos partes cuenta una vez.
            string sql = @"
                SELECT COUNT(*), COALESCE(SUM(t.alumnos), 0), COALESCE(SUM(t.sigue), 0), COALESCE(SUM(t.sigue * t.alumnos), 0) FROM (
                    SELECT s.idcodigos_accesos AS id, " + alumnos + @" AS alumnos, " + sigue + @" AS sigue
                      FROM clases c
                      INNER JOIN horarios hh       ON hh.clases_idclases = c.idclases
                      INNER JOIN codigos_accesos s ON s.horarios_idhorarios = hh.idhorarios" + deHoy + @"
                    UNION
                    SELECT s.idcodigos_accesos, " + alumnos + @", " + sigue + @"
                      FROM clases c
                      INNER JOIN horario_excepciones xx ON xx.clases_idclases = c.idclases
                      INNER JOIN codigos_accesos s      ON s.horario_excepciones_id = xx.idhorario_excepciones" + deHoy + @"
                    UNION
                    SELECT s.idcodigos_accesos, " + alumnos + @", " + sigue + @"
                      FROM clases c
                      INNER JOIN codigos_accesos s
                              ON s.horarios_idhorarios IS NULL AND s.horario_excepciones_id IS NULL
                             AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia
                             AND s.grupos_idgrupos = c.grupos_idgrupos AND " + dia + " BETWEEN c.vigente_desde AND DATE_ADD(c.vigente_hasta, INTERVAL 1 DAY)" + deHoy + @"
                ) t";
            using (MySqlCommand comando = new MySqlCommand(sql, conexion, transaccion))
            {
                conFechas(comando);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        datos.SesionesDeHoy = Convert.ToInt32(reader.GetValue(0));
                        datos.BitacorasDeHoy = Convert.ToInt32(reader.GetValue(1));
                        datos.SesionesDeHoySiguen = Convert.ToInt32(reader.GetValue(2));
                        datos.BitacorasDeHoySiguen = Convert.ToInt32(reader.GetValue(3));
                    }
                }
            }

            if (conDadas)
            {
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT COUNT(*), COALESCE(SUM(" + sigue + @"), 0) FROM asistencias_dadas ad
                    INNER JOIN clases c ON c.idclases = ad.fk_clase
                    WHERE " + lasDeHoy + " AND ad.fecha = @Desde AND ad.activa = 1", conexion, transaccion))
                {
                    conFechas(comando);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            datos.DadasDeHoy = Convert.ToInt32(reader.GetValue(0));
                            datos.DadasDeHoySiguen = Convert.ToInt32(reader.GetValue(1));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Con "Ahora mismo": los laboratorios del horario nuevo con generacion de
        /// codigos. Los codigos generados dependen de la franja y estas son nuevas:
        /// los ya dictados dejan de valer (RevisionAplicacion lo avisa). Solo lee.
        /// </summary>
        private static void LeerLaboratoriosConGeneracion(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, DatosDeAplicacion datos)
        {
            if (!Esquema.ModoDeAcceso(conexion, transaccion))
            {
                return;
            }

            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT DISTINCT l.nombre_laboratorio
                FROM ciclo_franjas f
                INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                INNER JOIN laboratorios l  ON l.idlaboratorios = f.laboratorio
                WHERE cc.idciclo = @C AND cc.quitada = 0 AND l.modo_codigo = 'renovable'
                ORDER BY l.nombre_laboratorio", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        datos.LaboratoriosConGeneracion.Add(reader.GetString(0));
                    }
                }
            }
        }

        /// <summary>Las personas docentes de la configuracion sin codigo (usuarios.codigo_clase vacio), por id, con su nombre corto. Necesita db/09.</summary>
        private static List<KeyValuePair<int, string>> DocentesSinCodigo(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo)
        {
            List<KeyValuePair<int, string>> docentes = new List<KeyValuePair<int, string>>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT DISTINCT u.idusuarios, u.nombre, u.apellido_paterno
                FROM ciclo_cargas cc
                INNER JOIN usuarios u ON u.idusuarios = cc.profesor
                WHERE cc.idciclo = @C AND cc.quitada = 0 AND (u.codigo_clase IS NULL OR u.codigo_clase = '')
                ORDER BY u.apellido_paterno, u.nombre", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        docentes.Add(new KeyValuePair<int, string>(reader.GetInt32("idusuarios"),
                            HorariosDatos.NombreCorto(Texto(reader, "nombre"), Texto(reader, "apellido_paterno"))));
                    }
                }
            }

            return docentes;
        }

        // ---------------------------------------------------------------------
        // Aplicar
        // ---------------------------------------------------------------------

        /// <summary>
        /// Aplica el horario en preparacion: sus clases quedan en Clases del
        /// primer al ultimo dia (desde, hasta) con sus franjas sin fecha propia, y
        /// las que valen el ultimo dia del horario de ahora (fin) terminan ese dia.
        /// Con guardarAhoraComo (null = no), antes guarda el horario de ese dia
        /// como quedo. Se revisa otra vez todo dentro de la transaccion
        /// (RevisionAplicacion): con algo en rojo, ErrorDeDatos con su texto y no
        /// cambia nada. Un solo renglon en el historial. El primer dia tiene que
        /// ser despues de hoy: para que empiece hoy, AplicarAhora.
        /// </summary>
        public ResultadoAplicar Aplicar(int idCiclo, string nombre, DateTime desde, DateTime hasta, DateTime fin, string guardarAhoraComo, int idAdministrador)
        {
            return AplicarConFechas(idCiclo, nombre, new DatosDeFechas { Desde = desde.Date, Hasta = hasta.Date, Fin = fin.Date },
                guardarAhoraComo, idAdministrador);
        }

        /// <summary>
        /// "Ahora mismo": aplica como Aplicar, pero empieza en este momento. El
        /// primer dia es hoy y el horario de ahora termina ayer (los dos del
        /// servidor); hoy es el dia con que se reviso: si en el servidor ya es
        /// otro, no aplica. Lo registrado hoy en las clases que se recortan se
        /// queda, y en la misma transaccion cada clase de hoy del horario nuevo
        /// que ya termino (hora_fin hasta la hora de NOW()) se cancela ese dia
        /// como "No tomar en cuenta", todas con el mismo creada_en: no salen en
        /// Faltas ni en Alertas y "Volver a contar" las devuelve. La que esta en
        /// curso y las que faltan cuentan con el horario nuevo. Ya no se deshace.
        /// </summary>
        public ResultadoAplicar AplicarAhora(int idCiclo, string nombre, DateTime hoy, DateTime hasta, string guardarAhoraComo, int idAdministrador)
        {
            return AplicarConFechas(idCiclo, nombre, FechasDeAhora(hoy, hasta), guardarAhoraComo, idAdministrador);
        }

        private ResultadoAplicar AplicarConFechas(int idCiclo, string nombre, DatosDeFechas fechas, string guardarAhoraComo, int idAdministrador)
        {
            Permisos.Exigir("Aplicar un horario");
            string guardar = string.IsNullOrWhiteSpace(guardarAhoraComo) ? null : guardarAhoraComo.Trim();
            if (guardar != null && guardar.Length > LargoNombre)
            {
                throw new ErrorDeDatos("El nombre con que se guarda el horario de ahora puede tener hasta " + LargoNombre + " letras.");
            }

            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                ExigirBaseParaAplicar(conexion, transaccion);

                // a) Que siga en preparacion, que no haya otra aplicada por empezar y el nombre.
                Bloquear(conexion, transaccion, idCiclo, EstadoConfiguracion.EnPreparacion);
                string porEmpezar = Convert.ToString(ValorEnLaBase(conexion, transaccion,
                    "SELECT nombre FROM ciclos_preliminares WHERE estado = 'publicado' AND desde > CURDATE() ORDER BY desde LIMIT 1 FOR UPDATE", null));
                if (!string.IsNullOrEmpty(porEmpezar))
                {
                    throw new ErrorDeDatos("«" + porEmpezar + "» ya está aplicado y todavía no empieza: deshaz su aplicación para aplicar otro.");
                }

                // Los nombres de las aplicadas se bloquean aqui, ciclos antes que clases como
                // siempre; se comparan abajo, cuando ya se sabe cuales vuelven a ser plantilla.
                NombresDe(conexion, transaccion, EstadoConfiguracion.Aplicada, idCiclo, true);

                // "Ahora mismo" se reviso con un hoy: si el servidor ya esta en otro dia
                // (paso la medianoche mientras se confirmaba), no se aplica.
                if (fechas.EmpiezaAhora)
                {
                    DateTime hoyServidor = Hoy(conexion, transaccion);
                    if (hoyServidor != fechas.Desde.Date)
                    {
                        throw new ErrorDeDatos("En el servidor ya es " + HistorialCambios.DiaYFecha(hoyServidor)
                            + ": vuelve a revisar «Antes de aplicar» y aplica otra vez.");
                    }
                }

                // Las clases que se recortan, bloqueadas antes de leer: nadie las
                // cambia entre la revision y el recorte.
                ValorEnLaBase(conexion, transaccion, "SELECT COUNT(*) FROM clases c WHERE " + SeRecorta + " FOR UPDATE",
                    c => c.Parameters.AddWithValue("@Fin", fechas.Fin));

                // Con "Ahora mismo", tambien las que empezaron hoy, separadas ya con todo bloqueado.
                PlanDeBaja deHoy = fechas.EmpiezaAhora
                    ? LasDeHoy(conexion, transaccion, fechas.Desde, Esquema.AsistenciaDada(conexion, transaccion), true)
                    : null;

                nombre = ValidarNombre(conexion, transaccion, nombre, EstadoConfiguracion.Aplicada, idCiclo,
                    deHoy == null ? null : AplicadasQueVuelven(conexion, transaccion, fechas.Desde, deHoy.SeQuitan));

                // b) La misma revision que la pantalla, aqui dentro.
                DatosDeAplicacion datos = LeerParaAplicar(conexion, transaccion, idCiclo, fechas);
                RenglonRevision rojo = RevisionAplicacion.Revisar(datos, fechas.Desde, fechas.Hasta, fechas.Fin, nombre).FirstOrDefault(r => r.Bloquea);
                if (rojo != null)
                {
                    throw new ErrorDeDatos(rojo.Texto);
                }

                ResultadoAplicar resultado = new ResultadoAplicar
                {
                    CicloId = idCiclo,
                    Nombre = nombre,
                    Desde = fechas.Desde,
                    Hasta = fechas.Hasta,
                    Fin = fechas.Fin
                };
                Action<MySqlCommand> parametros = c =>
                {
                    c.Parameters.AddWithValue("@C", idCiclo);
                    c.Parameters.AddWithValue("@Desde", fechas.Desde);
                    c.Parameters.AddWithValue("@Hasta", fechas.Hasta);
                    c.Parameters.AddWithValue("@Fin", fechas.Fin);
                    c.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
                };

                // c) El horario de ahora como quedo, antes de tocar nada, con lo que le falta asignar.
                DateTime diaDeAhora = DiaDelHorarioDeAhora(conexion, transaccion, fechas);
                if (guardar != null)
                {
                    string nombreGuardada = NombreLibre(conexion, transaccion, guardar, EstadoConfiguracion.Guardada);
                    int idGuardada = InsertarCiclo(conexion, transaccion, nombreGuardada, EstadoConfiguracion.Guardada, diaDeAhora, null, idAdministrador);
                    if (CopiarClases(conexion, transaccion, idGuardada, diaDeAhora, null, null, true) == 0)
                    {
                        CambiarEnLaBase(conexion, transaccion, "DELETE FROM ciclos_preliminares WHERE idciclo = @G AND estado = 'guardado'",
                            c => c.Parameters.AddWithValue("@G", idGuardada));
                        resultado.SinClasesQueGuardar = true;
                    }
                    else
                    {
                        resultado.GuardadaId = idGuardada;
                        resultado.GuardadaNombre = nombreGuardada;
                        PasarLoQueFaltaALaCopia(conexion, transaccion, idGuardada, diaDeAhora);
                    }
                }

                // Los horarios aplicados que tenian las de hoy, antes de que se borren.
                List<KeyValuePair<int, string>> aplicadasDeHoy = deHoy == null ? new List<KeyValuePair<int, string>>()
                    : AplicadasQueCambian(conexion, transaccion, fechas.Desde, deHoy.DeHoy);

                // d) Recortes: todas las que valen @Fin y siguen despues terminan ese
                //    dia. activa nunca se toca. Lo de antes queda en ciclo_recortes.
                resultado.Terminan = CambiarEnLaBase(conexion, transaccion, @"
                    INSERT INTO ciclo_recortes (idciclo, clase, hasta_antes)
                    SELECT @C, c.idclases, c.vigente_hasta FROM clases c WHERE " + SeRecorta + " ORDER BY c.idclases", parametros);
                CambiarEnLaBase(conexion, transaccion, @"
                    UPDATE clases c INNER JOIN ciclo_recortes r ON r.clase = c.idclases AND r.idciclo = @C
                       SET c.vigente_hasta = @Fin", parametros);

                // d2) "Ahora mismo": se sustituye lo de hoy. Las que empezaron hoy se quitan o, con
                //     algo registrado, terminan ayer como las recortadas, y a todas se les quitan
                //     sus extras pendientes y se les cancela lo que hoy no termino.
                DateTime? finDeLasQueVolvieron = null;
                if (deHoy != null)
                {
                    List<int> terminan = new List<int>(deHoy.ConRegistrosDeHoy);
                    using (MySqlCommand comando = new MySqlCommand("SELECT clase FROM ciclo_recortes WHERE idciclo = @C", conexion, transaccion))
                    {
                        comando.Parameters.AddWithValue("@C", idCiclo);
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                terminan.Add(reader.GetInt32(0));
                            }
                        }
                    }

                    resultado.ExtrasQuitadas = ContarExtrasQueSeQuitan(conexion, transaccion, fechas.Desde, terminan.Concat(deHoy.SeQuitan).ToList());
                    TerminarYQuitar(conexion, transaccion, fechas.Desde, terminan, deHoy.SeQuitan, idAdministrador);
                    finDeLasQueVolvieron = AjustarAplicadas(conexion, transaccion, aplicadasDeHoy, idAdministrador, resultado.VueltasAPlantilla);
                    resultado.EmpezaronHoyQuitadas = deHoy.SeQuitan.Count;
                    resultado.EmpezaronHoyTerminadas = deHoy.ConRegistrosDeHoy.Count;
                }

                // e) Codigos: cada clase nueva lleva el de su docente; quien no tiene, lo recibe.
                foreach (KeyValuePair<int, string> docente in DocentesSinCodigo(conexion, transaccion, idCiclo))
                {
                    CodigosDeProfesor.ObtenerOCrear(conexion, transaccion, docente.Key);
                    resultado.RecibieronCodigo.Add(docente.Value);
                }

                // f) Las clases, con un INSERT ... SELECT, y cada clase de la
                //    configuracion enlazada a la suya (la terna es unica entre las nuevas).
                int esperadas = Convert.ToInt32(ValorEnLaBase(conexion, transaccion,
                    "SELECT COUNT(*) FROM ciclo_cargas WHERE idciclo = @C AND quitada = 0", parametros));
                int antes = Convert.ToInt32(ValorEnLaBase(conexion, transaccion, "SELECT COALESCE(MAX(idclases), 0) FROM clases", null));
                CambiarEnLaBase(conexion, transaccion, "UPDATE ciclo_cargas SET clase_publicada = NULL WHERE idciclo = @C", parametros);
                resultado.Clases = CambiarEnLaBase(conexion, transaccion, @"
                    INSERT INTO clases (usuarios_idusuarios, grupos_idgrupos, materias_id_materia, codigo, vigente_desde, vigente_hasta,
                                        activa, creada_por, creada_en)
                    SELECT cc.profesor, cc.grupo, cc.materia, u.codigo_clase, @Desde, @Hasta, 1, @Admin, NOW()
                    FROM ciclo_cargas cc
                    INNER JOIN usuarios u ON u.idusuarios = cc.profesor
                    WHERE cc.idciclo = @C AND cc.quitada = 0
                    ORDER BY cc.idcarga", parametros);
                if (resultado.Clases != esperadas)
                {
                    throw new ErrorDeDatos("No se pudieron crear todas las clases (" + resultado.Clases + " de " + esperadas
                        + "): alguna persona docente ya no está en DigLab. Vuelve a entrar a la pestaña y revisa el horario.");
                }

                CambiarEnLaBase(conexion, transaccion, @"
                    UPDATE ciclo_cargas cc
                    INNER JOIN clases c ON c.idclases > @Antes AND c.usuarios_idusuarios = cc.profesor AND c.grupos_idgrupos = cc.grupo
                                       AND c.materias_id_materia = cc.materia AND c.vigente_desde = @Desde AND c.vigente_hasta = @Hasta
                                       AND c.creada_por <=> @Admin
                       SET cc.clase_publicada = c.idclases
                     WHERE cc.idciclo = @C AND cc.quitada = 0", c =>
                    {
                        parametros(c);
                        c.Parameters.AddWithValue("@Antes", antes);
                    });
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT COALESCE(SUM(clase_publicada IS NULL), 0), COUNT(DISTINCT clase_publicada), COALESCE(SUM(clase_publicada <= @Antes), 0)
                    FROM ciclo_cargas WHERE idciclo = @C AND quitada = 0", conexion, transaccion))
                {
                    parametros(comando);
                    comando.Parameters.AddWithValue("@Antes", antes);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        reader.Read();
                        if (Convert.ToInt32(reader.GetValue(0)) != 0 || Convert.ToInt32(reader.GetValue(1)) != esperadas || Convert.ToInt32(reader.GetValue(2)) != 0)
                        {
                            throw new ErrorDeDatos("No se pudo enlazar cada clase nueva con la suya: vuelve a intentarlo. No cambió nada.");
                        }
                    }
                }

                // g) Las franjas, sin fecha propia (vigente_desde y vigente_hasta en
                //    NULL): su vigencia es la de su clase (R3).
                resultado.Horarios = CambiarEnLaBase(conexion, transaccion, @"
                    INSERT INTO horarios (clases_idclases, laboratorios_idlaboratorios, dia_semana, hora_inicio, hora_fin)
                    SELECT cc.clase_publicada, f.laboratorio, f.dia_semana, f.hora_inicio, f.hora_fin
                    FROM ciclo_franjas f
                    INNER JOIN ciclo_cargas cc ON cc.idcarga = f.idcarga
                    WHERE cc.idciclo = @C AND cc.quitada = 0
                    ORDER BY cc.idcarga, f.idfranja", parametros);

                // g1) Lo que falta asignar en la plantilla pasa a "Falta asignar" de Horario
                //     semanal, a la clase que se acaba de crear (sin db/25, solo lo avisa
                //     la revision). Sus renglones se quedan en la plantilla: Deshacer la
                //     devuelve como estaba.
                PasarLoQueFaltaAsignar(conexion, transaccion, resultado, idCiclo, fechas.Desde, idAdministrador);

                // g2) "Ahora mismo": las clases de hoy del horario nuevo que ya
                //     terminaron (hora_fin hasta la hora del servidor) no cuentan hoy.
                //     Se cancelan ese dia como "No tomar en cuenta" (HorariosDatos),
                //     todas con el mismo creada_en: "Volver a contar" las devuelve.
                if (fechas.EmpiezaAhora)
                {
                    CancelarLasYaTerminadas(conexion, transaccion, resultado, fechas, nombre, idAdministrador, parametros);
                }

                // h) Queda aplicada, con su vigencia y el ultimo dia del horario de antes. Si
                //    no se recorto nada y el que se sustituye nunca corrio, ese dia es el que
                //    el le puso al de antes (con "Otro dia" pudo dejar dias sin clases de por medio).
                DateTime finAnterior = resultado.Terminan == 0 && finDeLasQueVolvieron.HasValue && finDeLasQueVolvieron.Value < fechas.Fin
                    ? finDeLasQueVolvieron.Value : fechas.Fin;
                int aplicada = CambiarEnLaBase(conexion, transaccion, @"
                    UPDATE ciclos_preliminares
                       SET estado = 'publicado', nombre = @Nombre, desde = @Desde, hasta = @Hasta, fin_anterior = @FinAnterior,
                           publicado_por = @Admin, publicado_en = NOW(), cambiado_en = NOW(), cambiado_por = @Admin
                     WHERE idciclo = @C AND estado = 'borrador'", c =>
                    {
                        parametros(c);
                        c.Parameters.AddWithValue("@Nombre", nombre);
                        c.Parameters.AddWithValue("@FinAnterior", finAnterior);
                    });
                if (aplicada != 1)
                {
                    throw new ErrorDeDatos("Esta plantilla ya se aplicó, se guardó o se borró: vuelve a entrar a la pestaña.");
                }

                // i) Un solo renglon para todo el horario (no uno por clase). Con
                //    "Ahora mismo", a que hora empezo y cuantas de hoy no cuentan.
                string hora = resultado.EmpezoA.ToString("HH:mm", CultureInfo.InvariantCulture);
                string empezo = !resultado.EmpezoAhora ? ""
                    : "; empezó hoy a las " + hora
                      + (resultado.SinContarHoy == 0 ? ""
                          : resultado.SinContarHoy == 1 ? " y 1 clase de hoy que ya había terminado no cuenta"
                          : " y " + resultado.SinContarHoy + " clases de hoy que ya habían terminado no cuentan");
                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Horario,
                    Accion = AccionDeCambio.Aplicar,
                    Tabla = "ciclos_preliminares",
                    RegistroId = idCiclo,
                    Fecha = fechas.Desde,
                    Descripcion = "Horario «" + nombre + "» aplicado: " + RevisionCiclo.TextoClases(resultado.Clases) + " "
                        + HistorialCambios.Vigencia(fechas.Desde, fechas.Hasta) + empezo + "; "
                        + (resultado.Terminan == 0
                            ? "ninguna clase de antes se recorta."
                            : (resultado.Terminan == 1 ? "la de antes termina" : "las " + resultado.Terminan + " de antes terminan")
                              + " el " + HistorialCambios.Fecha(fechas.Fin) + (resultado.EmpezoAhora ? " (ayer)." : "."))
                        + (resultado.EmpezaronHoyQuitadas + resultado.EmpezaronHoyTerminadas == 0 ? ""
                            : " De las que empezaron hoy, " + TextoDeLaBaja(0, resultado.EmpezaronHoyQuitadas, resultado.EmpezaronHoyTerminadas, 0) + ".")
                        + (resultado.ExtrasQuitadas == 0 ? "" : " " + TextoDeLaBaja(0, 0, 0, resultado.ExtrasQuitadas) + "."),
                    Antes = null,
                    Despues = HistorialCambios.Datos(
                        "Vigencia", HistorialCambios.Vigencia(fechas.Desde, fechas.Hasta),
                        "Empezó", resultado.EmpezoAhora ? "hoy, " + HistorialCambios.DiaYFecha(fechas.Desde) + ", a las " + hora + " (al aplicarlo)" : null,
                        "Clases nuevas", resultado.Clases.ToString(CultureInfo.InvariantCulture),
                        "Horarios", resultado.Horarios.ToString(CultureInfo.InvariantCulture),
                        "Falta asignar", resultado.MinutosPorAsignar > 0 ? HorariosDatos.Duracion(resultado.MinutosPorAsignar) : null,
                        "No cuentan hoy", resultado.EmpezoAhora && resultado.SinContarHoy > 0
                            ? RevisionCiclo.TextoClases(resultado.SinContarHoy) + " que ya había" + (resultado.SinContarHoy == 1 ? "" : "n")
                              + " terminado (canceladas ese día como «no tomar en cuenta»; en Faltas se pueden volver a contar)"
                            : null,
                        "Terminan", RevisionCiclo.TextoClases(resultado.Terminan) + " el " + HistorialCambios.Fecha(fechas.Fin),
                        "De las que empezaron hoy", resultado.EmpezaronHoyQuitadas + resultado.EmpezaronHoyTerminadas == 0 ? null
                            : TextoDeLaBaja(0, resultado.EmpezaronHoyQuitadas, resultado.EmpezaronHoyTerminadas, 0),
                        "Sesiones extra que se quitan", resultado.ExtrasQuitadas > 0 ? resultado.ExtrasQuitadas.ToString(CultureInfo.InvariantCulture) : null,
                        "Guardado como quedó", resultado.GuardadaNombre,
                        "Vuelven a ser plantilla", resultado.VueltasAPlantilla.Count > 0 ? string.Join(", ", resultado.VueltasAPlantilla) : null)
                });

                transaccion.Commit();
                Log.Info("Horario " + idCiclo + " (" + nombre + ") aplicado del " + fechas.Desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + " al " + fechas.Hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " + resultado.Clases + " clases con "
                    + resultado.Horarios + " horarios; " + resultado.Terminan + " clases de antes terminan el "
                    + fechas.Fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + (resultado.EmpezoAhora ? "; ahora mismo, a las " + resultado.EmpezoA.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                        + ", con " + resultado.SinContarHoy + " clases de hoy ya terminadas sin contar; de las que empezaron hoy, "
                        + resultado.EmpezaronHoyQuitadas + " quitadas y " + resultado.EmpezaronHoyTerminadas + " terminadas con registros; "
                        + resultado.ExtrasQuitadas + " extras quitadas" : "")
                    + (resultado.GuardadaId.HasValue ? "; guardado como quedo en " + resultado.GuardadaId.Value : "")
                    + (resultado.RecibieronCodigo.Count > 0 ? "; " + resultado.RecibieronCodigo.Count + " docentes recibieron codigo" : "") + ".");
                return resultado;
            }
        }

        /// <summary>
        /// Dentro de la transaccion de Aplicar y despues de crear las clases: cada
        /// renglon de "Falta asignar" de la plantilla (QuitadaPorAsignar) queda
        /// tambien en horas_por_asignar de la clase nueva que le toca. Sin db/25, o si
        /// esa clase no se creo, nada: la revision ya lo aviso.
        /// </summary>
        private static void PasarLoQueFaltaAsignar(MySqlConnection conexion, MySqlTransaction transaccion, ResultadoAplicar resultado, int idCiclo, DateTime desde,
            int idAdministrador)
        {
            if (!Esquema.HorasPorAsignar(conexion, transaccion))
            {
                return;
            }

            List<PorAsignarCiclo> pendientes = LeerPorAsignar(conexion, transaccion, idCiclo, "", null, true);
            if (pendientes.Count == 0)
            {
                return;
            }

            Dictionary<int, int> claseDeCarga = new Dictionary<int, int>();
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT idcarga, clase_publicada FROM ciclo_cargas WHERE idciclo = @C AND quitada = 0 AND clase_publicada IS NOT NULL", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        claseDeCarga[reader.GetInt32(0)] = reader.GetInt32(1);
                    }
                }
            }

            foreach (PorAsignarCiclo p in pendientes)
            {
                int clase;
                if (claseDeCarga.TryGetValue(p.CargaId, out clase))
                {
                    HorariosDatos.InsertarPendienteDePlantilla(conexion, transaccion, clase, p.Minutos, Math.Max(p.Minutos, p.MinutosOriginales), p.LaboratorioId, p.DiaSemana,
                        p.Inicio, p.Fin, desde, p.Motivo, idAdministrador);
                    resultado.MinutosPorAsignar += p.Minutos;
                }
            }
        }

        /// <summary>
        /// "Ahora mismo", dentro de la transaccion de Aplicar y despues de crear
        /// las franjas: cada franja de hoy de las clases nuevas que ya termino a la
        /// hora del servidor (hora_fin hasta la de NOW()) se cancela hoy, como
        /// "No tomar en cuenta" de Faltas (HorariosDatos.InsertarCancelada con el
        /// motivo MotivoAntesDeAplicar y un mismo creada_en, MomentoDelLote). La
        /// que esta en curso y las que faltan no se tocan: cuentan con el horario
        /// nuevo. Si en el servidor ya es otro dia, ErrorDeDatos (no cambia nada).
        /// </summary>
        private static void CancelarLasYaTerminadas(MySqlConnection conexion, MySqlTransaction transaccion, ResultadoAplicar resultado,
            DatosDeFechas fechas, string nombre, int idAdministrador, Action<MySqlCommand> parametros)
        {
            resultado.EmpezoAhora = true;
            resultado.EmpezoA = Convert.ToDateTime(ValorEnLaBase(conexion, transaccion, "SELECT NOW()", null), CultureInfo.InvariantCulture);
            if (resultado.EmpezoA.Date != fechas.Desde.Date)
            {
                throw new ErrorDeDatos("En el servidor ya es " + HistorialCambios.DiaYFecha(resultado.EmpezoA.Date)
                    + ": vuelve a revisar «Antes de aplicar» y aplica otra vez.");
            }

            TimeSpan hora = new TimeSpan(resultado.EmpezoA.Hour, resultado.EmpezoA.Minute, resultado.EmpezoA.Second);
            List<int> terminadas = new List<int>();
            using (MySqlCommand comando = new MySqlCommand(@"
                SELECT h.idhorarios FROM horarios h
                INNER JOIN ciclo_cargas cc ON cc.clase_publicada = h.clases_idclases
                WHERE cc.idciclo = @C AND cc.quitada = 0 AND h.dia_semana = WEEKDAY(@Desde) + 1 AND h.hora_fin <= @Hora
                ORDER BY h.hora_inicio, h.laboratorios_idlaboratorios, h.idhorarios", conexion, transaccion))
            {
                parametros(comando);
                comando.Parameters.AddWithValue("@Hora", hora);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        terminadas.Add(reader.GetInt32(0));
                    }
                }
            }

            if (terminadas.Count > 0)
            {
                string motivo = MotivoAntesDeAplicar(nombre, resultado.EmpezoA);
                DateTime creadaEn = HorariosDatos.MomentoDelLote(conexion, transaccion, motivo, idAdministrador);
                foreach (int franja in terminadas)
                {
                    HorariosDatos.InsertarCancelada(conexion, transaccion, franja, fechas.Desde, motivo, idAdministrador, creadaEn);
                }
            }

            resultado.SinContarHoy = terminadas.Count;
        }

        /// <summary>
        /// Al sustituir hoy el horario ("Ahora mismo" o Dar de baja el horario actual): las franjas
        /// de hoy de esas clases que aun no terminan (en curso o por venir) ya no se van a dar y se
        /// cancelan hoy. Asi la lista sabe, al leerla, que quien no registro no tiene falta
        /// (AsistenciaDatos.NoTuvoHoraEseDia), y que una clase ya dada entera si la da: al leer no
        /// se conoce la hora a la que se sustituyo. Sin la marca de "No tomar en cuenta": no sale
        /// en "Volver a contar". Una que ya estaba cancelada se queda cancelada, con su motivo
        /// (TerminarYQuitar le quita esa marca).
        /// </summary>
        private static void CancelarLasQueNoTerminaron(MySqlConnection conexion, MySqlTransaction transaccion, List<int> clases, DateTime hoy, int idAdministrador)
        {
            if (clases.Count == 0)
            {
                return;
            }

            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@Hoy", hoy.Date);
                comando.Parameters.AddWithValue("@Motivo", "Se cambió el horario a las ");
                comando.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
                comando.CommandText = @"
                    INSERT INTO horario_excepciones (tipo, fecha, horarios_idhorarios, motivo, creada_por, creada_en)
                    SELECT 'cancelada', @Hoy, h.idhorarios, CONCAT(@Motivo, TIME_FORMAT(NOW(), '%H:%i')), @Admin, NOW()
                    FROM horarios h
                    WHERE h.clases_idclases IN (" + Lista(comando, "n", clases) + @")
                      AND h.dia_semana = WEEKDAY(@Hoy) + 1 AND h.hora_fin > CURTIME()"
                    + (Esquema.VigenciaDeFranjas(conexion, transaccion)
                        ? " AND (h.vigente_desde IS NULL OR @Hoy >= h.vigente_desde) AND (h.vigente_hasta IS NULL OR @Hoy <= h.vigente_hasta)"
                        : "") + @"
                      AND NOT EXISTS (SELECT 1 FROM horario_excepciones x
                                       WHERE x.tipo = 'cancelada' AND x.horarios_idhorarios = h.idhorarios AND x.fecha = @Hoy)
                    ORDER BY h.hora_inicio, h.idhorarios";
                comando.ExecuteNonQuery();
            }
        }

        /// <summary>Sin db/16, db/23 o db/09 no se aplica (las pestanas ya lo explican).</summary>
        private static void ExigirBaseParaAplicar(MySqlConnection conexion, MySqlTransaction transaccion)
        {
            if (!Esquema.CicloPreliminar(conexion, transaccion) || !Esquema.HorariosGuardados(conexion, transaccion))
            {
                throw new ErrorDeDatos("La base no tiene lo de las plantillas de horario (db/16_ciclo_preliminar.sql y db/23_horarios_guardados.sql): pide a quien administra el servidor que las aplique.");
            }

            if (!Esquema.CodigoPorProfesor(conexion, transaccion))
            {
                throw new ErrorDeDatos("La base no tiene el código por docente (db/09_codigo_por_profesor.sql): sin él no se sabe qué código dar a las clases nuevas. Pide a quien administra el servidor que la aplique.");
            }
        }

        // ---------------------------------------------------------------------
        // Deshacer
        // ---------------------------------------------------------------------

        /// <summary>
        /// Deshace una aplicada que todavia no empieza: borra las clases que creo
        /// (si no tienen sesiones ni asistencias dadas), devuelve su fecha de fin a
        /// las que recorto (solo si nadie la cambio despues y si no se cruzan con
        /// otra igual) y la deja otra vez en preparacion, con sus fechas como
        /// propuesta. La guardada "como quedo" se queda. Un renglon en el historial.
        /// </summary>
        public ResultadoDeshacer Deshacer(int idCiclo, int idAdministrador)
        {
            Permisos.Exigir("Deshacer la aplicacion de un horario");
            using (MySqlConnection conexion = mconexion.GetConexion())
            using (MySqlTransaction transaccion = conexion.BeginTransaction())
            {
                AplicadaPorDeshacer aplicada = LeerPorDeshacer(conexion, transaccion, idCiclo, true);
                if (aplicada.Motivo != null)
                {
                    throw new ErrorDeDatos(aplicada.Motivo);
                }

                ResultadoDeshacer resultado = new ResultadoDeshacer { Nombre = aplicada.Nombre };
                Action<MySqlCommand> parametros = c =>
                {
                    c.Parameters.AddWithValue("@C", idCiclo);
                    c.Parameters.AddWithValue("@FinAnterior", aplicada.FinAnterior.Date);
                    c.Parameters.AddWithValue("@Admin", idAdministrador > 0 ? (object)idAdministrador : DBNull.Value);
                };

                // Las que recupera su fecha: las que siguen terminando en el fin que se les
                // puso, menos las que se cruzarian con otra igual que no es de esta aplicada (R6).
                List<int> recortadas = new List<int>();
                List<int> sinCambios = new List<int>();
                using (MySqlCommand comando = new MySqlCommand(@"
                    SELECT r.clase, c.vigente_hasta FROM ciclo_recortes r INNER JOIN clases c ON c.idclases = r.clase
                     WHERE r.idciclo = @C ORDER BY r.clase FOR UPDATE", conexion, transaccion))
                {
                    parametros(comando);
                    using (MySqlDataReader reader = comando.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            recortadas.Add(reader.GetInt32(0));
                            if (reader.GetDateTime(1).Date == aplicada.FinAnterior.Date)
                            {
                                sinCambios.Add(reader.GetInt32(0));
                            }
                        }
                    }
                }

                HashSet<int> conOtraIgual = new HashSet<int>();
                if (sinCambios.Count > 0)
                {
                    using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                    {
                        parametros(comando);
                        comando.CommandText = @"
                            SELECT DISTINCT r.clase
                            FROM ciclo_recortes r
                            INNER JOIN clases c ON c.idclases = r.clase
                            INNER JOIN clases o ON o.usuarios_idusuarios = c.usuarios_idusuarios AND o.grupos_idgrupos = c.grupos_idgrupos
                                               AND o.materias_id_materia = c.materias_id_materia AND o.idclases <> c.idclases AND o.activa = 1
                                               AND o.vigente_desde <= r.hasta_antes AND o.vigente_hasta > @FinAnterior
                            WHERE r.idciclo = @C AND c.activa = 1 AND o.idclases NOT IN (" + Lista(comando, "n", aplicada.Clases) + ")";
                        using (MySqlDataReader reader = comando.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                conOtraIgual.Add(reader.GetInt32(0));
                            }
                        }
                    }
                }

                List<int> devolver = sinCambios.Where(id => !conOtraIgual.Contains(id)).ToList();
                if (devolver.Count > 0)
                {
                    using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                    {
                        parametros(comando);
                        comando.CommandText = @"
                            UPDATE clases c INNER JOIN ciclo_recortes r ON r.clase = c.idclases AND r.idciclo = @C
                               SET c.vigente_hasta = r.hasta_antes
                             WHERE c.vigente_hasta = @FinAnterior AND c.idclases IN (" + Lista(comando, "d", devolver) + ")";
                        resultado.Devueltas = comando.ExecuteNonQuery();
                    }
                }

                resultado.PorOtraIgual = sinCambios.Count(id => conOtraIgual.Contains(id));
                resultado.NoDevueltas = recortadas.Count - sinCambios.Count;

                // Lo que Aplicar paso a "Falta asignar" de esas clases: horas_por_asignar no
                // tiene claves foraneas y se quedaria sin clase. La plantilla conserva lo suyo.
                if (aplicada.Clases.Count > 0 && Esquema.HorasPorAsignar(conexion, transaccion))
                {
                    using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                    {
                        comando.CommandText = "DELETE FROM horas_por_asignar WHERE clases_idclases IN (" + Lista(comando, "p", aplicada.Clases) + ")";
                        comando.ExecuteNonQuery();
                    }
                }

                // Las clases que creo, por lista (no multitabla: la clave SET NULL de
                // ciclo_cargas). Sus franjas y excepciones se van con ellas.
                if (aplicada.Clases.Count > 0)
                {
                    using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                    {
                        comando.CommandText = "DELETE FROM clases WHERE idclases IN (" + Lista(comando, "k", aplicada.Clases) + ")";
                        resultado.ClasesBorradas = comando.ExecuteNonQuery();
                    }
                }

                CambiarEnLaBase(conexion, transaccion, "DELETE FROM ciclo_recortes WHERE idciclo = @C", parametros);
                CambiarEnLaBase(conexion, transaccion, "UPDATE ciclo_cargas SET clase_publicada = NULL WHERE idciclo = @C", parametros);
                int vuelta = CambiarEnLaBase(conexion, transaccion, @"
                    UPDATE ciclos_preliminares
                       SET estado = 'borrador', fin_anterior = NULL, publicado_por = NULL, publicado_en = NULL,
                           cambiado_en = NOW(), cambiado_por = @Admin
                     WHERE idciclo = @C AND estado = 'publicado'", parametros);
                if (vuelta != 1)
                {
                    throw new ErrorDeDatos("Este horario ya no está aplicado: vuelve a entrar a la pestaña.");
                }

                HistorialCambios.Anotar(conexion, transaccion, new CambioPorAnotar
                {
                    Tipo = TipoDeCambio.Horario,
                    Accion = AccionDeCambio.Deshacer,
                    Tabla = "ciclos_preliminares",
                    RegistroId = idCiclo,
                    Fecha = aplicada.Desde,
                    Descripcion = "Aplicación de «" + aplicada.Nombre + "» deshecha: se quitaron sus " + RevisionCiclo.TextoClases(resultado.ClasesBorradas)
                        + (resultado.Devueltas == 0 ? " y ninguna recuperó su fecha de fin."
                            : resultado.Devueltas == 1 ? " y 1 recuperó su fecha de fin."
                            : " y " + resultado.Devueltas + " recuperaron su fecha de fin."),
                    Antes = HistorialCambios.Datos(
                        "Vigencia", HistorialCambios.Vigencia(aplicada.Desde, aplicada.Hasta),
                        "Clases nuevas", resultado.ClasesBorradas.ToString(CultureInfo.InvariantCulture),
                        "Terminaban", RevisionCiclo.TextoClases(recortadas.Count) + " el " + HistorialCambios.Fecha(aplicada.FinAnterior)),
                    Despues = HistorialCambios.Datos(
                        "Recuperaron su fecha de fin", RevisionCiclo.TextoClases(resultado.Devueltas),
                        "No se tocaron", resultado.NoDevueltas + resultado.PorOtraIgual == 0 ? null
                            : (resultado.NoDevueltas + resultado.PorOtraIgual).ToString(CultureInfo.InvariantCulture))
                });

                transaccion.Commit();
                Log.Info("Aplicacion del horario " + idCiclo + " (" + aplicada.Nombre + ") deshecha: " + resultado.ClasesBorradas + " clases borradas, "
                    + resultado.Devueltas + " recuperaron su fecha de fin, " + resultado.NoDevueltas + " cambiadas despues y "
                    + resultado.PorOtraIgual + " con otra igual no se tocaron.");
                return resultado;
            }
        }

        /// <summary>Por que no se puede deshacer esa aplicada, o null si se puede. Solo lee.</summary>
        public string PuedeDeshacer(int idCiclo)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                return LeerPorDeshacer(conexion, null, idCiclo, false).Motivo;
            }
        }

        /// <summary>Lo que ensena la pestana de una aplicada por empezar: cuantas recorto, cuantas creo y si se puede deshacer. Solo lee.</summary>
        public InfoDeAplicada LeerAplicada(int idCiclo)
        {
            using (MySqlConnection conexion = mconexion.GetConexion())
            {
                AplicadaPorDeshacer aplicada = LeerPorDeshacer(conexion, null, idCiclo, false);
                return new InfoDeAplicada
                {
                    Recortadas = Convert.ToInt32(ValorEnLaBase(conexion, null, "SELECT COUNT(*) FROM ciclo_recortes WHERE idciclo = @C",
                        c => c.Parameters.AddWithValue("@C", idCiclo))),
                    Creadas = aplicada.Clases.Count,
                    Motivo = aplicada.Motivo
                };
            }
        }

        /// <summary>Lo que Deshacer necesita saber de una aplicada, y por que no se puede (Motivo), o null.</summary>
        private sealed class AplicadaPorDeshacer
        {
            public string Nombre = "";
            public DateTime Desde;
            public DateTime Hasta;
            public DateTime FinAnterior;
            public List<int> Clases = new List<int>();
            public string Motivo;
        }

        /// <summary>
        /// Las reglas de Deshacer, con o sin bloquear: aplicada y por empezar, sin
        /// otra en preparacion, y sin sesiones ni asistencias dadas en las clases
        /// que creo (VigenciaFranjas.SesionDeLaClase, por partes con indice).
        /// </summary>
        private static AplicadaPorDeshacer LeerPorDeshacer(MySqlConnection conexion, MySqlTransaction transaccion, int idCiclo, bool bloquear)
        {
            AplicadaPorDeshacer aplicada = new AplicadaPorDeshacer();
            string bloqueo = bloquear ? " FOR UPDATE" : "";
            string estado = null;
            bool empezo = false;
            using (MySqlCommand comando = new MySqlCommand(
                "SELECT nombre, estado, desde, hasta, fin_anterior, (desde <= CURDATE()) AS empezo FROM ciclos_preliminares WHERE idciclo = @C" + bloqueo,
                conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        aplicada.Nombre = Texto(reader, "nombre");
                        estado = Texto(reader, "estado");
                        aplicada.Desde = Fecha(reader, "desde") ?? DateTime.MinValue;
                        aplicada.Hasta = Fecha(reader, "hasta") ?? DateTime.MinValue;
                        aplicada.FinAnterior = Fecha(reader, "fin_anterior") ?? DateTime.MinValue;
                        empezo = !reader.IsDBNull(reader.GetOrdinal("empezo")) && Convert.ToInt32(reader["empezo"]) == 1;
                    }
                }
            }

            if (estado != "publicado")
            {
                aplicada.Motivo = "Este horario ya no está aplicado: vuelve a entrar a la pestaña.";
                return aplicada;
            }

            if (empezo)
            {
                aplicada.Motivo = "Ya empezó: los cambios se hacen en Horario semanal y Clases.";
                return aplicada;
            }

            if (aplicada.FinAnterior == DateTime.MinValue)
            {
                aplicada.Motivo = "Esta aplicada no guarda el último día del horario de antes: no se puede deshacer. Los cambios se hacen en Horario semanal y Clases.";
                return aplicada;
            }

            if (Convert.ToInt32(ValorEnLaBase(conexion, transaccion, "SELECT COUNT(*) FROM ciclos_preliminares WHERE estado = 'borrador'" + bloqueo, null)) > 0)
            {
                aplicada.Motivo = "Ya hay otro horario en preparación: guárdalo o descártalo antes de deshacer.";
                return aplicada;
            }

            using (MySqlCommand comando = new MySqlCommand(
                "SELECT clase_publicada FROM ciclo_cargas WHERE idciclo = @C AND clase_publicada IS NOT NULL ORDER BY clase_publicada", conexion, transaccion))
            {
                comando.Parameters.AddWithValue("@C", idCiclo);
                using (MySqlDataReader reader = comando.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        aplicada.Clases.Add(reader.GetInt32(0));
                    }
                }
            }

            if (aplicada.Clases.Count == 0)
            {
                return aplicada;
            }

            if (bloquear)
            {
                // Tambien sus franjas y sus extras: una sesion nueva que se enlace a
                // una de ellas espera a que esto termine (su clave foranea necesita
                // la fila) y, si se borra, falla en vez de quedar sin enlace (SET NULL).
                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "SELECT COUNT(*) FROM clases WHERE idclases IN (" + Lista(comando, "k", aplicada.Clases) + ") FOR UPDATE";
                    comando.ExecuteScalar();
                }

                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "SELECT COUNT(*) FROM horarios WHERE clases_idclases IN (" + Lista(comando, "h", aplicada.Clases) + ") FOR UPDATE";
                    comando.ExecuteScalar();
                }

                using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
                {
                    comando.CommandText = "SELECT COUNT(*) FROM horario_excepciones WHERE clases_idclases IN (" + Lista(comando, "x", aplicada.Clases) + ") FOR UPDATE";
                    comando.ExecuteScalar();
                }
            }

            string dia = VigenciaFranjas.DiaDeSesion("s");
            bool conDadas = Esquema.AsistenciaDada(conexion, transaccion);
            using (MySqlCommand comando = new MySqlCommand("", conexion, transaccion))
            {
                string a = Lista(comando, "a", aplicada.Clases);
                string b = Lista(comando, "b", aplicada.Clases);
                string c3 = Lista(comando, "c", aplicada.Clases);
                comando.CommandText = @"
                    SELECT (SELECT COUNT(*) FROM horarios hh INNER JOIN codigos_accesos s ON s.horarios_idhorarios = hh.idhorarios
                             WHERE hh.clases_idclases IN (" + a + @"))
                         + (SELECT COUNT(*) FROM horario_excepciones xx INNER JOIN codigos_accesos s ON s.horario_excepciones_id = xx.idhorario_excepciones
                             WHERE xx.clases_idclases IN (" + b + @"))
                         + (SELECT COUNT(*) FROM clases c
                             INNER JOIN codigos_accesos s
                                     ON s.horarios_idhorarios IS NULL AND s.horario_excepciones_id IS NULL
                                    AND s.usuarios_idusuarios = c.usuarios_idusuarios AND s.materias_id_materia = c.materias_id_materia
                                    AND s.grupos_idgrupos = c.grupos_idgrupos AND " + dia + @" BETWEEN c.vigente_desde AND c.vigente_hasta
                             WHERE c.idclases IN (" + c3 + "))"
                    + (conDadas ? " + (SELECT COUNT(*) FROM asistencias_dadas ad WHERE ad.fk_clase IN (" + Lista(comando, "d", aplicada.Clases) + "))" : "");
                if (Convert.ToInt32(comando.ExecuteScalar()) > 0)
                {
                    aplicada.Motivo = "Ya hay sesiones de sus clases: los cambios se hacen en Horario semanal y Clases.";
                }
            }

            return aplicada;
        }

        // ---------------------------------------------------------------------
        // Utilidades de este archivo
        // ---------------------------------------------------------------------

        private static object ValorEnLaBase(MySqlConnection conexion, MySqlTransaction transaccion, string sql, Action<MySqlCommand> parametros)
        {
            using (MySqlCommand comando = new MySqlCommand(sql, conexion, transaccion))
            {
                if (parametros != null)
                {
                    parametros(comando);
                }

                object valor = comando.ExecuteScalar();
                return valor == DBNull.Value ? null : valor;
            }
        }

        /// <summary>Ejecuta y devuelve las filas tocadas (las encontradas, con MySql.Data).</summary>
        private static int CambiarEnLaBase(MySqlConnection conexion, MySqlTransaction transaccion, string sql, Action<MySqlCommand> parametros)
        {
            using (MySqlCommand comando = new MySqlCommand(sql, conexion, transaccion))
            {
                if (parametros != null)
                {
                    parametros(comando);
                }

                return comando.ExecuteNonQuery();
            }
        }
    }
}
